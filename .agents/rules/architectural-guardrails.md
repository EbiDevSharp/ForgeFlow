# ForgeFlow Architectural Guardrails & Audit Defenses

These rules are derived from the comprehensive architectural audit and must be strictly observed in all code modifications.

### 1. EF Core Model Configuration & Migration Synchronization
- Never configure entity mappings inside a monolithic `OrganizationModel.cs`. Use modular `IEntityTypeConfiguration<T>` classes under `ForgeFlow.Infrastructure/Organizations/Configurations/`.
- Every model change must be backed by an explicit migration and synchronized snapshot. `HasPendingModelChanges()` must always be `false`.
- Never use manual `.Ignore<T>()` calls in `AuthDbContext` for operational entities; maintain isolated DbContext configuration scans.
- Never override `SaveChanges` or `SaveChangesAsync` to throw `InvalidOperationException` (violates LSP). Use `SaveAttributed` or EF Core interceptors (`OrganizationAuditSaveChangesInterceptor`).

### 2. Explicit Optimistic Concurrency (`IHasRowVersion`)
- Domain aggregate roots requiring optimistic concurrency MUST implement `IHasRowVersion` with an explicit typed CLR property: `public byte[] RowVersion { get; }`.
- Never use string-based shadow property lookups (such as `EF.Property<byte[]>(x, "RowVersion")` or `db.Entry(x).Property("RowVersion")`) in stores or domain code.

### 3. Date Handling & Persian Calendar Boundary Safety
- In Blazor components using `BitDatePicker` with `PersianCalendar`, `default(DateOnly)` (`0001-01-01`) falls outside `PersianCalendar.MinSupportedDateTime` (`0622-03-22`). Passing year 1 throws an unhandled `ArgumentOutOfRangeException` and crashes the Blazor circuit when opening "New" / "جدید" dialogs.
- Always guard `DateOnly` bindings before converting to `DateTimeOffset`:
  `d == default ? null : new(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero)`.

### 4. Vertical Slice Architecture (Application Layer)
- Avoid monolithic `*UseCases.cs` classes (>300 lines). Every feature domain (CRUD, approvals, cancellations, dispatches, queries) must reside in dedicated slice classes under `ForgeFlow.Application/<Module>/Slices/`.
- `*UseCases.cs` classes must act strictly as thin, backward-compatible facades delegating to specialized slice handlers.

### 5. Blazor UI Component Modularization & Namespace Safety
- Avoid monolithic Razor pages (>800 lines). Extract grids, detail viewers, and complex modals into dedicated subcomponents under `src/ForgeFlow.Web/Components/Pages/<Module>Sections/`.
- Never create subcomponents in a subfolder that shares the exact same name as a Razor page (e.g. `Pages/Items/` colliding with `Items.razor`), as the Razor source generator produces duplicate partial classes (`CS0101`). Always use `*Sections` suffix (e.g., `ItemSections/`, `SalesSections/`, `BomSections/`, `QualitySections/`).

### 6. Query Performance & Anti-Patterns
- For all read queries in operational stores, always use `.AsNoTracking()` and project directly with `.Select(...)` instead of loading tracked entities into memory and calling mapper functions (eliminates N+1 queries).
- Implement cancellation token forwarding and search input debouncing (>=250ms) on heavy reporting/filter interfaces.

### 7. Domain Logic & Domain Events
- Business rules, state transition validations, and price/tax/discount calculations belong inside domain entities (e.g., `SalesOrder.CalculateTotalAmount()`), never inline in SQL stores.
- State changes must raise `IAuditableDomainEvent` via `IHasDomainEvents` so that audit logging occurs uniformly.

### 8. Localization & UI Primitives
- Strictly use ASP.NET Core Localization (`IStringLocalizer` and `.resx` resources) for all user-facing text. Never hardcode English or Persian strings in UI markup.
- Strictly reuse existing UI primitives (`ff-icon--*`, `<BitDialog>`, `<ForgeFormDialog>`, `<BitDataGrid>`, `ff-card`, `ff-box`, `ff-toolbar`) preserving RTL/LTR layout integrity.
