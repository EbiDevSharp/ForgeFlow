# ForgeFlow Architectural Guardrails & Audit Defenses

These rules are derived from the comprehensive architectural audit and remediation efforts. They must be strictly observed in all code modifications to prevent regressions.

### 1. EF Core Model Configuration, DbContext Boundaries & Migrations
- **Modular Entity Configurations**: Never configure entity mappings inside a monolithic `OrganizationModel.cs`. Use modular `IEntityTypeConfiguration<T>` classes under `ForgeFlow.Infrastructure/Organizations/Configurations/` and `ForgeFlow.Infrastructure/Identity/Configurations/`.
- **DbContext Boundary Separation**: `AuthDbContext` and `OrganizationDbContext` must remain strictly separated using marker interfaces (`IAuthEntityConfiguration` vs `IOrganizationEntityConfiguration`) with assembly scan filtering.
- **Zero Cross-Context Navigations & No Manual Ignore**: Never add navigation properties between Identity/Auth entities and ERP domain entities (use scalar foreign keys like `long OrganizationId` or `Guid/long UserId`). Never use manual `.Ignore<T>()` calls in `AuthDbContext` to hide domain entities.
- **Migration Synchronization**: Every model change must be backed by an explicit migration and synchronized snapshot. `HasPendingModelChanges()` must always evaluate to `false`.
- **LSP Compliance in DbContext**: Never override `SaveChanges` or `SaveChangesAsync` to throw `InvalidOperationException` (violates Liskov Substitution Principle). Use `SaveAttributed` or EF Core interceptors (`OrganizationAuditSaveChangesInterceptor`).

### 2. Explicit Optimistic Concurrency (`IHasRowVersion`)
- Domain aggregate roots requiring optimistic concurrency MUST implement `IHasRowVersion` with an explicit typed CLR property: `public byte[] RowVersion { get; }`.
- Never use string-based shadow property lookups (such as `EF.Property<byte[]>(x, "RowVersion")` or `db.Entry(x).Property("RowVersion")`) in stores or domain code.

### 3. Domain Logic, Aggregate Invariants & Domain Events
- **Domain Encapsulation**: Business rules, state transition workflows (`Approve()`, `Release()`, `Complete()`, `Cancel()`, `Dispatch()`), and price/tax/discount calculations belong inside domain entities and aggregate roots, never inline in SQL stores or Application slices.
- **Property Setter Protection**: Protect entity state using `private set` or `internal set` so callers cannot bypass validation methods or place entities into invalid states.
- **Uniform Auditable Domain Events**: State transitions must raise canonical domain events implementing `IAuditableDomainEvent` via `IHasDomainEvents`. Ensure corresponding audit event handlers (e.g. `ProcurementAuditHandler`, `SalesOrderAuditHandler`) are registered in DI.

### 4. Vertical Slice Architecture (Application Layer)
- **Slice Decomposition**: Avoid monolithic `*UseCases.cs` classes (>300 lines). Every feature domain (CRUD, approvals, cancellations, dispatches, queries) must reside in dedicated slice classes under `ForgeFlow.Application/<Module>/Slices/`.
- **Thin Facades**: `*UseCases.cs` classes must act strictly as thin, backward-compatible facades delegating to specialized slice handlers.
- **Audit Telemetry Preservation**: When dispatching operations within slices (e.g., `StockReservationSlices`), always propagate valid `IOperationRecorder` and `OperationContext` instances (never pass `null, null` to `RecordIfRejectedAsync`).

### 5. Blazor UI Components, Modal Lifecycles & Two-Way Binding
- **Component Modularization**: Avoid monolithic Razor pages (>800 lines). Extract grids, detail viewers, and complex modals into dedicated subcomponents under `src/ForgeFlow.Web/Components/Pages/<Module>Sections/`.
- **Namespace Collision Prevention**: Never create subcomponents in a subfolder sharing the exact same name as a Razor page (e.g., `Pages/Items/` colliding with `Items.razor`), as the Razor generator produces duplicate partial classes (`CS0101`). Always use `*Sections` suffix (e.g., `ItemSections/`, `SalesSections/`, `BomSections/`, `QualitySections/`).
- **Modal Two-Way Binding (`@bind-IsOpen` & `IsOpenChanged`)**: In modal dialog subcomponents:
  - The modal's `Open()` method must ALWAYS invoke `_ = IsOpenChanged.InvokeAsync(true);` alongside setting its internal `IsOpen = true`.
  - The parent component's open handler (e.g., `OpenCreateDim()`, `OpenEditDim()`) must explicitly set its binding flag (e.g., `createDimOpen = true;`). Failing to do so causes Blazor to re-render the parent on button click completion and overwrite the child's `IsOpen` parameter back to `false`, immediately closing or suppressing the modal.
  - Guard `try { StateHasChanged(); } catch (InvalidOperationException) { }` inside modal methods so calling them outside an active renderer (such as in unit tests) does not throw `The render handle is not yet assigned`.
  - Use `@bind:event="oninput"` on text inputs so submit button enablement evaluates reactively as the user types, rather than requiring an input blur.
  - Implement `OnDismissing` handlers on `<BitDialog>` to prevent modal dismissal while background save operations (`saving == true`) are in flight.
- **Dispatcher Thread Safety**: Wrap background JS interop invocations (such as `clearDrafts` upon authentication state change) inside `await InvokeAsync(...)` with try/catch to prevent Blazor dispatcher thread-affinity exceptions.

### 6. Database Concurrency, Locking & Query Anti-Patterns
- **No Request-Time Serializable Locks**: Never use `Serializable` transactions or row locks (`UPDLOCK/HOLDLOCK`) for routine read validation of session tokens or user preferences. Move `app.MapStaticAssets()` before auth middleware, use `READ COMMITTED`, and cache validated session principals in memory with a short sliding window (e.g., 15–20s) with immediate eviction on session revocation.
- **Avoid Routine Organization Locks**: Do not acquire exclusive `UPDLOCK, HOLDLOCK` on the tenant `Organization` row for routine operational writes (inventory, sales, procurement). Use non-blocking validation (`OrganizationActivityGuard`) and reserve exclusive locks strictly for organization-level administrative operations.
- **Query Projection**: For all read queries in operational stores, always use `.AsNoTracking()` and project directly with `.Select(...)` instead of loading tracked entities into memory and calling mapper functions (eliminates N+1 queries).
- **Catch-Block Ordering**: In EF Core persistence stores, always catch specific concurrency exceptions (`DbUpdateConcurrencyException`) BEFORE general database exceptions (`DbUpdateException`).
- **No Silent Empty Catch Blocks**: Never use empty silent catch blocks (`catch { }`). Always catch specific exception types and report structured diagnostics via `ILogger`.
- **Search Debouncing & Cancellation**: Implement cancellation token forwarding and search input debouncing (>=250ms) via `DebouncedSearchController` on heavy reporting/filter interfaces.

### 7. Authentication, WebAuthn / Passkey & Security Hardening
- **WebAuthn User Verification (UV)**: Enforce the User Verification flag (`flags & 0x04 != 0`) alongside User Presence in `WebAuthnVerifier`. Configure `userVerification = "required"` for both registration and login options.
- **Single-Use Passkey Challenges**: Store WebAuthn challenges in a short-lived server cache (`PasskeyChallengeStore`) to prevent challenge replay attacks.
- **Opaque User Identifiers**: Use random, opaque byte handles for `user.id` in WebAuthn credentials rather than sequential database identity IDs.
- **Step-Up Authentication**: Require current password re-verification (step-up auth) before generating passkey registration options.
- **Anti-Timing & Lockout Defense**: Execute dummy PBKDF2 hashing for non-existent or inactive accounts to ensure constant-time response and prevent username enumeration. Enforce progressive exponential delays based on client IP and username to mitigate brute-force and account lockout DoS.
- **Forwarded Headers & Reverse Proxies**: Always bind `UseForwardedHeaders` to configured known networks (`KnownIPNetworks`) and resolve client IP from `RemoteIpAddress` rather than trusting unvalidated raw request headers.

### 8. Production Deployment Defaults & Observability
- **HTTPS Redirection & HSTS**: Enforce `app.UseHttpsRedirection()` and `app.UseHsts()` in production deployments to prevent credential and cookie stripping.
- **Persistent DataProtection KeyRing Fail-Fast**: In `Production`, `DataProtection:KeyRingPath` must be explicitly configured. Fail fast at startup (`InvalidOperationException`) if unconfigured, rather than silently falling back to ephemeral keys.
- **Health Checks**: Expose lightweight `/health` checking both `OrganizationDbContext` (resolving via `IDbContextFactory`) and `AuthDbContext`, plus host disk space monitoring (`DiskSpaceHealthCheck`).
- **Blazor Circuit Limits**: Always configure bounded `CircuitOptions` (`DisconnectedCircuitMaxRetained = 100`, `DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3)`, `MaxBufferedUnacknowledgedRenderBatches = 10`) on `AddInteractiveServerComponents` to prevent circuit exhaustion.
- **Tooling Separation**: Keep administrative test tools (such as test account creators or recovery scripts) in dedicated `tools/` projects, never inside production infrastructure assemblies.

### 9. Date/Timezone Safety, Localization & Regional Standards
- **Persian Calendar Boundary Safety**: In Blazor components using `BitDatePicker` with `PersianCalendar`, `default(DateOnly)` (`0001-01-01`) falls outside `PersianCalendar.MinSupportedDateTime` (`0622-03-22`). Always guard `DateOnly` bindings before converting to `DateTimeOffset`:
  `d == default ? null : new(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero)`.
- **Tenant Organization Clock**: Bind date pickers (`BitDatePicker.MaxDate` / `MinDate`) to the tenant organization's clock/time zone (`IOrganizationClock`) rather than server machine `DateTimeOffset.Now`.
- **Localization Resources**: Strictly use ASP.NET Core Localization (`IStringLocalizer` and `.resx` resources) for all user-facing text. Never hardcode English or Persian strings in UI markup. Keep business data values (e.g. ISO country code `"IR"`) language-independent, resolving display names via resource keys.
- **Client Draft Obfuscation Transparency**: Document client draft storage as lightweight obfuscation for shoulder-surfing defense in `sessionStorage`/`localStorage`; never claim strong cryptography and always exclude sensitive credentials.
