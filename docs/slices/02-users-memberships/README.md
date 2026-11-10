# Slice 02 — Users and memberships

Status: **DESIGN APPROVED (`02.0` -> READY FOR `02.1`)** (2026-11-10) — Decisions `Q-U01`–`Q-U05` are locked in [decisions.md](decisions.md). No Slice 02 code or schema change has been written yet; implementation starts at increment `02.1`.

## 1. Objective, actors, preconditions, outputs, and out-of-scope

### Roadmap alignment
In the baseline roadmap ([ForgeFlow.md](../../../ForgeFlow.md) `۰۰.۴`), item `02` was initial login/bootstrap/session (`AUTH-01`, completed and accepted alongside Slice 01 on 2026-11-10) and item `03` is User & Membership creation, edit, and deactivation. Because `AUTH-01` carried the unnumbered auth slot, this slice takes folder `docs/slices/02-users-memberships` and implements user & membership administration, while general custom role/permission matrices remain in the next slice (roadmap item `04`) and the dedicated audit viewer follows after that (roadmap item `05`).

### Objective
Enable authorized platform and organization administrators to create and inspect user accounts within authorized scope, edit user eligibility (`IsActive`), unlock locked-out accounts (if approved in `Q-U04`), and manage `OrganizationMembership` and `SiteMembership` records atomically with immediate session/circuit invalidation and immutable security audit logging.

### Actors and trusted provenance
- **Deployment / Platform administrator** (`FixedAccessRoles.DeploymentProvisioner`, `ScopeKind = 0`): explicit platform authority where scoped. Does not implicitly bypass organization membership for company-private operations unless granted.
- **Organization administrator** (`FixedAccessRoles.OrganizationAdministrator`, `ScopeKind = 1`): manages users and organization/site memberships strictly within the explicitly requested active `OrganizationId` where their own `ApplicationAccount`, `OrganizationMembership`, and role assignment are active.
- **Authenticated self-user**: can read only their own session/account culture state via existing AUTH-01 endpoints; cannot escalate scope, reactivate themselves, or inspect sibling accounts.
- **Actor provenance**: every write uses `ITrustedActorAccessor` (`SessionActorAccessor`) backed by current database state (`ApplicationAccount`, `AccountSession`, `OrganizationMembership`, `SiteMembership`, `ScopedRoleAssignment`). Request DTO `ActorId` or UI state never supplies authority.

### Preconditions
- `AUTH-01` and Slice 01 (`01-organization-sites`) remain intact: `ApplicationAccount`, `OrganizationMembership`, `SiteMembership`, `ScopedRoleAssignment`, `AccountSession`, `SecurityEvent`, and `Organization`/`Site` tables exist and pass migration checks.
- No bypass or loosening of `AuthDbContext.Guard()`, `SecurityEvent` append-only rules, or fail-closed `RuntimeSessions` validation.

### Out-of-scope (for Slice 02 unless explicitly brought in by decisions)
- Self-registration, anonymous password reset, email/SMS token delivery, passkeys/MFA/SSO.
- Custom user-defined roles, fine-grained permission editors, or generic platform-wide multi-tenant directory lookup for single-organization admins (roadmap item `04`).
- Full searchable audit-log viewer UI (roadmap item `05`), though Slice 02 writes atomic `SecurityEvent` rows from its very first operation.
- Hard delete of users, memberships, sessions, or security events.
- Any Item, Unit, BOM, warehouse, or production order work.

## 2. Scenarios: happy paths, failures, retries, and concurrency

### Happy paths
1. **List / view scoped users**: An authorized `OrganizationAdministrator` opens `/organizations/{orgId}/users` (or selects an authorized scope in `/users`) and sees only accounts holding an `OrganizationMembership` in `orgId` (filtered by active/inactive, search on canonical/prefix `UserName`), plus each user's organization membership status and site memberships belonging to `orgId`.
2. **Create user & initial organization/site membership**: An authorized administrator creates a new account (`UserName` valid ASCII `[A-Za-z0-9._@+-]`, active `OrganizationMembership` in `orgId`, optional `SiteMembership` rows for active sites within `orgId`, plus initial credential/role handling per `Q-U01`/`Q-U02`), committing `auth.User`, `auth.OrganizationMembership`, optional `auth.SiteMembership`, and `auth.SecurityEvent` (`AccountCreated`, `MembershipChanged`) in one SQL transaction.
3. **Update user eligibility (Deactivate / Reactivate) & Unlock**: Deactivating `ApplicationAccount.IsActive = false` atomically revokes all active `AccountSession` rows for that user (`RevokeReason = 'AccountDisabled'`) and emits `'AccountEligibilityChanged'` + `'SessionRevoked'`. On next HTTP request, protected use case, or SignalR circuit revalidation check, the deactivated user loses access immediately.
4. **Manage Organization / Site memberships**: Adding, activating, or deactivating an `OrganizationMembership` or `SiteMembership` within `orgId` validates parent activity (`Organization.IsActive`, `Site.IsActive`, active `OrganizationMembership` before `SiteMembership`) and records `'MembershipChanged'` in the same transaction. Deactivating an `OrganizationMembership` or `SiteMembership` denies that scope on the very next permission evaluation (`SessionPermissionEvaluator`) without waiting for cookie expiry.

### Failure, idempotency, and concurrency rules
- **Duplicate `UserName` (`UX_User_NormalizedUserName`)**: Returns `Users.UserNameDuplicate` cleanly with no `SecurityEvent` created. Cross-organization account attachment vs rejection is decided in `Q-U03`.
- **Stale concurrency (`RowVersion` on `OrganizationMembership` / `SiteMembership` and `ConcurrencyStamp` / `RowVersion` on `ApplicationAccount`)**: Concurrent updates fail with `Users.ConcurrencyConflict` (`OrganizationSite.ConcurrencyConflict` pattern), requiring explicit reload in UI; never silently overwrite.
- **No-op status updates with matching expected version**: Return current state/version unchanged with no DB mutation or duplicate audit event.
- **Self-deactivation & last-active-admin protection (`Q-U05`)**: Serialized on the `Organization` row (`UPDLOCK, HOLDLOCK`) before evaluating active `OrganizationAdministrator` count inside `orgId`, preventing two concurrent admins from both deactivating each other and leaving an active organization administrator-less.
- **Lock ordering discipline (deadlock prevention)**: Acquire locks in fixed order: `Organization` -> `Site` (if applicable) -> `ApplicationAccount` -> `OrganizationMembership` -> `SiteMembership` -> `ScopedRoleAssignment` -> `AccountSession`. Every write path affecting user eligibility and session revocation follows the same user lock used by `RuntimeSessions` login and `HostRecovery` reset (`AUTH-01` lock discipline).
- **Unknown commit (`OutcomeUnknown`)**: No blind retry that might reset credentials or duplicate side effects; surface correlation ID and reload option.

## 3. Conceptual & physical model summary

Full tables, existing constraints, indexes, and transition notes are specified in [physical-erd.md](physical-erd.md). Summary:
- Reuses existing `auth.User` (`ApplicationAccount`), `auth.OrganizationMembership`, `auth.SiteMembership`, `auth.ScopedRoleAssignment`, `auth.Session` (`AccountSession`), and `auth.SecurityEvent` tables created in `AUTH-01`.
- SQL composite FK `(UserId, OrganizationId)` guarantees no `SiteMembership` exists without a parent `OrganizationMembership`, and `(OrganizationId, SiteId)` guarantees `SiteMembership` always matches the same `OrganizationId`.
- `AuthDbContext.Guard()` already blocks deletion of any entity, modification of `SecurityEvent`, modification of `BootstrapState` after bootstrap, and mutation of `UserName` / `NormalizedUserName` or `ScopedRoleAssignment` scope keys.

## 4. Proposed Application use cases and boundaries

Application layer (`ForgeFlow.Application.Users` or extension alongside `AccessContracts`) will expose narrow DTO use cases over `IUserManagementStore` / specialized persistence port:

| Use case | Scope required | Expected inputs | Expected concurrency / outputs |
|---|---|---|---|
| `ListOrganizationUsersAsync` | `OrganizationScope.Organization(orgId)` | `orgId`, `Search`, `IncludeInactive`, `Page`, `PageSize` (max 100) | Scoped paged items (`UserId`, `UserName`, `IsActive`, `MembershipActive`, `RowVersion`), no cross-org leakage |
| `GetOrganizationUserAsync` | `OrganizationScope.Organization(orgId)` | `orgId`, `UserId` | Detail DTO (`UserName`, `IsActive`, `PreferredCulture`, `IsLockedOut`, versions, org membership + scoped site memberships inside `orgId`) or `Users.NotFound` |
| `CreateOrganizationUserAsync` | `OrganizationScope.Organization(orgId)` | `orgId`, `UserName`, initial password, `AssignOrganizationAdmin`, initial `SiteIds` (+ optional `SiteOperator` flag) | `UserId`, opaque versions, correlation ID; atomic `'AccountCreated'`, `'MembershipChanged'`, optional `'ScopedRoleChanged'` |
| `AttachExistingUserToOrganizationAsync` | `OrganizationScope.Platform` | `orgId`, `UserName`, `AssignOrganizationAdmin`, initial `SiteIds` | Multi-org attachment without exposing cross-org data; atomic `'MembershipChanged'` / `'ScopedRoleChanged'` |
| `SetUserActiveAsync` | `Organization(orgId)` *(single-org users)* or `Platform` | `orgId?`, `UserId`, `IsActive`, `ExpectedUserVersion` | Updated version; revokes active `AccountSession`s (`AccountDisabled`) & rotates stamp when `false` |
| `SetOrganizationMembershipActiveAsync` | `OrganizationScope.Organization(orgId)` | `orgId`, `UserId`, `IsActive`, `ExpectedMembershipVersion` | Updated version; enforces self/last-admin guard and `Organization.IsActive` on activation |
| `AssignOrToggleSiteMembershipAsync` | `OrganizationScope.Organization(orgId)` | `orgId`, `SiteId`, `UserId`, `IsActive`, `ExpectedVersion?` | Created/updated membership version; validates `Site` belongs to `orgId`, `Site.IsActive`, and active `OrganizationMembership` |
| `SetTenantRoleActiveAsync` | `OrganizationScope.Organization(orgId)` | `orgId`, `UserId`, `RoleId` (`OrganizationAdministrator`/`SiteOperator`), `SiteId?`, `IsActive`, `ExpectedVersion?` | Grants/toggles fixed tenant role under self/last-admin & active-membership guards; emits `'ScopedRoleChanged'` |
| `UnlockUserAsync` | `Organization(orgId)` *(single-org users)* or `Platform` | `orgId?`, `UserId`, `ExpectedUserVersion` | Clears `LockoutEnd`/`AccessFailedCount`, emits `'AccountEligibilityChanged'` |

### Stable error codes (never translated Domain strings)
`Users.UserNameInvalid`, `Users.UserNameDuplicate`, `Users.PasswordInvalid`, `Users.NotFound`, `Users.SelfDeactivationForbidden`, `Users.LastOrganizationAdminRequired`, `Users.LastPlatformAdminRequired`, `Users.ParentOrganizationInactive`, `Users.ParentSiteInactive`, `Users.OrganizationMembershipRequired`, `Users.SiteMembershipRequired`, `Users.RoleScopeInvalid`, `Users.ScopeRequired`, `Users.AccessDenied`, `Users.ActorRequired`, `Users.ConcurrencyConflict`, `Users.AuditUnavailable`, `Users.PersistenceUnavailable`, `Users.OutcomeUnknown`.

## 5. Operation x Permission x Scope matrix

New constants in Application permission contracts (exact permission names finalized at `02.1`):

| Operation | Required permission | Required scope | Guessed-ID / anti-enumeration protection |
|---|---|---|---|
| List/Get user in org | `Users.Read` | `Organization(orgId)` | Denied or missing membership in `orgId` returns `Users.AccessDenied` (for scope) or `Users.NotFound` (for target user not belonging to `orgId`); never confirms user existence in another org |
| Create user in org | `Users.Create` | `Organization(orgId)` | Validates caller active `OrganizationAdministrator` (or approved grant) in `orgId`; rejects Site IDs not belonging to `orgId` with `Site.NotFound` |
| Enable/Disable user | `Users.SetActive` | `Organization(orgId)` (subject to `Q-U03`) | Must verify target user has membership in `orgId`; blocks cross-tenant eligibility changes |
| Manage Org/Site memberships | `Memberships.Manage` | `Organization(orgId)` | Enforces `Site.OrganizationId == orgId` and parent active flags in SQL transaction |
| Manage fixed role *(if `Q-U02`)* | `Roles.AssignFixed` | `Organization(orgId)` | Only `OrganizationAdministrator` (inside `orgId`) or `SiteOperator` (inside active `Site` of `orgId`); never `DeploymentProvisioner` |

Platform-level cross-org provisioning (`OrganizationScope.Platform`) remains restricted to `DeploymentProvisioner` (`Q-U03`).

## 6. Security events and fail-closed auditing

Reuses `auth.SecurityEvent` (`CK_SecurityEvent_Code` already allowlists `'AccountCreated'`, `'AccountEligibilityChanged'`, `'MembershipChanged'`, `'ScopedRoleChanged'`, `'SessionRevoked'`, `'AccessDenied'`).
- **Transaction boundary**: Every successful write inserts its `SecurityEvent` row(s) in the same `AuthDbContext` transaction as the account/membership mutation. If audit insert fails, the entire transaction rolls back and returns `Users.AuditUnavailable` (or `Users.OutcomeUnknown` if after commit failure).
- **Allowlisted `DataJson` fields**: Event subtype (`"UserDisabled"`, `"OrgMembershipActivated"`, `"SiteMembershipDeactivated"`, etc.), affected safe IDs (`TargetUserId`, `OrganizationId`, `SiteId`), previous/new boolean flags, and operation correlation ID. **Never store passwords, hashes, security stamps, cookies, tokens, or unauthorized cross-tenant names.**
- **Access denial logging**: Denied operations emit `'AccessDenied'` outside failed business transactions with nullable actor/subject handling matching `AUTH-01`.

## 7. Proposed UI pages and bilingual / mobile contract

- **Routes**:
  - `/users` (explicit authorized Organization scope selector; single-org admins pre-selected cleanly without an unfiltered platform directory) and `/organizations/{OrganizationId:long}/users` (deep link).
  - `/users/{UserId:long}` or scoped `/organizations/{OrganizationId:long}/users/{UserId:long}` detail dialog/page (no guessed global directory leakage).
- **NavMenu entry**: Added to `NavMenu.razor` under localized navigation key, visible only when the signed-in actor has `Users.Read` in at least one authorized organization scope (following the exact pattern in `NavMenu.razor` for `Organizations.Read`/`Sites.Read`).
- **Components & UX rules (Bit BlazorUI 10.7)**:
  - `BitDataGrid` server-side paging (`PageSize` default 25, max 100), search input, active/inactive status filter, empty/loading/error/access-denied/not-found alerts.
  - Compact `BitDialog` create/edit/detail dialogs matching Slice 01 visual design (`ShowCloseButton="true"`, `ShowCancelButton="false"`, side-by-side small action buttons, localized status-toggle confirmation naming the target `UserName`, unsaved-change confirmation + conflict-reload prompt).
  - **Sensitive fields**: If temporary password entry is included (`Q-U01`), `type="password"` with reveal toggle if appropriate, **never saved to `DraftCoordinator` / `sessionStorage`**, and cleared immediately on dialog close, save, or navigation.
  - **Bilingual & responsive gates**: Full `fa-IR` (RTL) and `en-US` (LTR) `.resx` resources, `Culture.css` direction, keyboard/focus management, zero horizontal overflow at `1440px` and `320px`.

## 8. Acceptance scenarios

See [test-plan.md](test-plan.md) for Domain/Application unit tests (`U01`–`U06`), real SQL Server transactional/concurrency/FK tests (`S01`–`S07`), and authenticated bilingual desktop/mobile browser tests (`B01`–`B06`).

## 9. Decisions and impact vs Slice 01 / AUTH-01

See [decisions.md](decisions.md) for inherited boundaries and open questions `Q-U01`–`Q-U05`.

## 10. Separate status and next step

| Track | Status | Notes |
|---|---|---|
| Design (`02.0`) | APPROVED (2026-11-10) | `Q-U01`–`Q-U05` closed in [decisions.md](decisions.md); zero schema migration needed |
| Implementation (`02.1`–`02.4`) | NOT STARTED | Next: `02.1` Domain & Application user/membership contracts |
| Verification & Acceptance (`02.5`) | NOT STARTED | Requires Domain/App tests, disposable SQL tests, Chrome FA/EN 1440px/320px checks, and manual sign-off |

- **Blocker**: None for `02.1` (design gate closed).
- **Next small step**: Implement increment `02.1` (Domain user-account/membership/role invariants + Application `Users`/`Memberships` DTOs, permission constants, and persistence ports + unit tests).
