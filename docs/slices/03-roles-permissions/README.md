# Slice 03 — Roles and granular per-page permissions

Status: **INCREMENTS `03.1`–`03.4` VERIFIED (`03.2` SQL `6/6` PASS; `03.3`–`03.4` WEB & UI IMPLEMENTED)** (2026-11-10) — Schema (`auth.RoleDefinition`, `auth.RolePermission`, `auth.UserPermissionGrant`, `ScopedRoleAssignment` FK upgrade, `CK_SecurityEvent_Code`), non-destructive migration `20261110134703_RolesAndGranularPermissions`, dynamic `RuntimeSessions.ScopeAllowed`, `SqlRoleOperationsStore`, `AddRoleManagement()`, `RoleManagementSqlTests` (`6/6` SQL PASS in `10.1s`), dynamic `ReadScopes`, `MainLayout` hiding empty parent `Settings` menu and exposing `/roles`, `Roles.razor`, per-user page/operation permission dialog in `Users.razor`, bilingual `fa-IR`/`en-US` `.resx` catalogs, and `RoleManagementPageTests` are implemented. Live browser sign-off (`03.5`) follows applying the migration to `localhost/testforgflow`.

## 1. Objective, actors, preconditions, outputs, and out-of-scope

### Roadmap alignment
Per [ForgeFlow.md](../../../ForgeFlow.md) `۰۰.۴` row `04`, Slice 03 follows Slice 02 (`Users and memberships`) and delivers **roles and per-page/per-operation permission assignments** with server-side enforcement, immediate access change on open circuits/requests, and last-administrator protection. Audit-log viewer UI remains Slice 04 (`۰۰.۴` row `05`), and Item/Unit/BOM integration follows in Slice 05 (`۰۰.۴` row `06`).

### Objective
Replace static role-to-permission switching (`FixedRolePolicy.Allows`) with data-driven scoped permission evaluation across every application page and operation (e.g., Organizations: View/Create/Edit/Activate-Deactivate; Sites: View/Create/Edit/Activate-Deactivate; Users: View/Create/Edit Status/Unlock/Reset Password/Manage Memberships; Roles & Permissions: View/Create/Edit/Assign), so authorized administrators can grant exact page operations to custom organization roles and/or individual users within strict organization and site boundaries.

### Actors and trusted provenance
- **Deployment provisioner** (`ScopeKind = 0`, `Platform`): only role able to manage platform-level assignments (`Organizations.Create`, `Users.AttachOrganization`, `DeploymentProvisioner`).
- **Organization administrator / delegated role manager** (`ScopeKind = 1`, `Organization(orgId)`): creates/edits/deactivates custom roles scoped strictly to `orgId` and assigns allowed tenant/site operations to users belonging to `orgId`.
- **Site operator / custom-granted user** (`ScopeKind = 1 or 2`): receives access strictly matching their active memberships (`OrganizationMembership` and, for `Site` scope, `SiteMembership`) AND active role/operation grants for that exact scope and page operation.
- **Provenance**: always server-resolved `ITrustedActorAccessor` (`SessionActorAccessor`) and `IOrganizationPermissionEvaluator` (`SessionPermissionEvaluator`) backed by live SQL queries; no permissions cached in claims cookies.

### Preconditions
- `AUTH-01`, Slice 01 (`01-organization-sites`), and Slice 02 (`02-users-memberships`) schemas and trusted session/membership invariants remain intact.
- Existing built-in `RoleId` values (`1 = OrganizationAdministrator`, `2 = SiteOperator`, `3 = DeploymentProvisioner`) stay preserved without breaking existing initialized installations (including `localhost/testforgflow`).

### Out-of-scope
- Self-service permission requests, approval workflows, or time-bound temporary privilege elevation.
- Cross-tenant shared custom role templates visible across organizations.
- Generic audit-event browser/search page (Slice 04).
- Business-domain Item/Unit/BOM permissions before their slices exist (the permission catalog registers only existing implemented modules, extendable per future slice).

## 2. Scenarios: happy paths, failures, retries, and concurrency

### Happy paths
1. **Browse permission catalog grouped by page/module**: Authorized admin opens `/roles` (or a user's **Permissions** dialog from `/users`) and sees every page grouped clearly with its operations (e.g., **Organizations**: View, Create *(Platform only)*, Edit, Activate/Deactivate; **Sites**: View, Create, Edit, Activate/Deactivate; **Users**: View, Create, Enable/Disable, Unlock, Reset Password, Manage Memberships; **Roles**: View, Manage).
2. **Create & configure custom organization role**: Admin in `orgId` creates `"Warehouse Supervisor"` (`ScopeKind = Organization` or `Site`), checks `Sites.Read`, `Sites.Update`, saves atomically (`RoleDefinition` + `RolePermission` + `'RoleDefinitionChanged'` audit).
3. **Assign granular page operations / custom role to user**: Admin assigns `"Warehouse Supervisor"` (or individual page operations per `Q-R01`) to user `u` inside `orgId` (or specific `siteId`). Next navigation/button check or API call immediately permits only those operations (e.g., user sees `/sites`, Edit button renders, Create and Activate/Deactivate buttons remain hidden and denied server-side!).
4. **Revoke a permission or deactivate custom role**: Admin unchecks `Sites.Update` on role or user. On user's very next UI read/write or Blazor circuit action, `Sites.Update` immediately returns `AccessDenied` without waiting for logout/cookie expiry.

### Failure, idempotency, and concurrency rules
- **Scope boundary escalation attempt**: An `OrganizationAdministrator` cannot assign `Organizations.Create`, `Users.AttachOrganization`, or any `Platform` permission (`Roles.EscalationForbidden` / `Roles.AccessDenied`). Assigning a `Site`-scoped grant without active `SiteMembership` or for a site outside `orgId` is rejected (`Site.NotFound` / `Users.SiteMembershipRequired`).
- **System role immutability**: Built-in system roles (`1, 2, 3`) cannot be renamed, deactivated as definitions, or stripped of system permissions (`Roles.SystemRoleImmutable`).
- **Duplicate custom role name/code in same org**: Returns `Roles.NameDuplicate`; two distinct organizations may reuse the same role name independently (`(OrganizationId, NormalizedName)` unique).
- **Self/last-admin protection**: Cannot remove `Roles.Manage` / `OrganizationAdministrator` / `Users.SetActive` from self or the last effective administrator of an active organization (`Users.LastOrganizationAdminRequired`), serialized under `Organization` `UPDLOCK, HOLDLOCK`.
- **Stale `RowVersion` / no-op**: Same strict 8-byte Base64 `ExpectedRowVersion` concurrency and zero-audit no-op rules as Slice 01/02.

## 3. Conceptual & physical model summary

See [physical-erd.md](physical-erd.md) for exact proposed tables (`auth.PermissionCatalog` / static code catalog + `auth.RoleDefinition`, `auth.RolePermission`, `auth.UserPermissionGrant`), updated `ScopedRoleAssignment` FK/check compatibility, and non-destructive migration + rollback rules for populated databases.

## 4. Proposed Application use cases and boundaries

New/extended contracts in `ForgeFlow.Domain.Roles` & `ForgeFlow.Application.Roles`:

| Use case | Scope required | Key inputs / outputs |
|---|---|---|
| `GetPermissionCatalogAsync` | Authorized org or platform | Returns code-defined modules/pages/operations (never unimplemented future entities), plus scope applicability (`Platform`, `Organization`, `Site`) |
| `ListOrganizationRolesAsync` | `Roles.Read` at `Organization(orgId)` | Paged org roles (system tenant templates + custom roles scoped to `orgId`) |
| `GetOrganizationRoleAsync` | `Roles.Read` at `Organization(orgId)` | Role metadata, `RowVersion`, assigned permission codes |
| `CreateOrganizationRoleAsync` | `Roles.Manage` at `Organization(orgId)` | `orgId`, `Name`, `TargetScopeKind` (`Organization` or `Site`), `PermissionCodes` -> created detail |
| `UpdateOrganizationRoleAsync` | `Roles.Manage` at `Organization(orgId)` | `orgId`, `RoleId`, `Name`, `IsActive`, `PermissionCodes`, `ExpectedRowVersion` |
| `GetUserPermissionsAsync` | `Roles.Read` / `Permissions.Assign` at `Organization(orgId)` | Target user's org/site memberships, assigned roles (fixed + custom), and effective/direct per-operation grants inside `orgId` |
| `SetUserRoleAssignmentAsync` | `Permissions.Assign` at `Organization(orgId)` (or `Platform`) | Assign/toggle fixed or custom role for user at explicit `Organization` or `Site` scope |
| `SetUserOperationGrantsAsync` *(if `Q-R01` includes direct per-user grants)* | `Permissions.Assign` at `Organization(orgId)` | `orgId`, `SiteId?`, `UserId`, set of `PermissionCode` toggles, `ExpectedConcurrency` -> atomic diff + `'DirectPermissionChanged'` |

Stable error codes: `Roles.NameInvalid`, `Roles.NameDuplicate`, `Roles.NotFound`, `Roles.SystemRoleImmutable`, `Roles.PermissionUnknown`, `Roles.PermissionScopeInvalid`, `Roles.EscalationForbidden`, `Roles.MembershipRequired`, `Roles.ParentOrganizationInactive`, `Roles.ParentSiteInactive`, `Roles.SelfDeactivationForbidden`, `Roles.LastOrganizationAdminRequired`, `Roles.ConcurrencyConflict`, `Roles.AccessDenied`, `Roles.ActorRequired`, `Roles.ScopeRequired`, `Roles.AuditUnavailable`, `Roles.PersistenceUnavailable`, `Roles.OutcomeUnknown`.

## 5. Operation x Permission x Scope matrix

| Operation | Required permission | Required scope | Escalation / cross-org guard |
|---|---|---|---|
| List/Get org roles & user permissions | `Roles.Read` | `Organization(orgId)` | Filters strictly by `OrganizationId == orgId`; guessed `RoleId` from another org returns `Roles.NotFound` |
| Create/Update/Deactivate custom role | `Roles.Manage` | `Organization(orgId)` | Rejects Platform-only permission codes; rejects modifying system roles `1..3` |
| Assign/Toggle custom or fixed tenant role / per-user operation grants | `Permissions.Assign` | `Organization(orgId)` | Caller cannot grant any permission they do not themselves hold (or outside `orgId`), and cannot modify `Platform` grants |
| Assign `DeploymentProvisioner` or Platform grants | `Permissions.Assign` | `Platform` | Requires active `DeploymentProvisioner` (`ScopeKind = 0`) |

## 6. Audit & security events

Extends `auth.SecurityEvent` (`CK_SecurityEvent_Code`) additively with `'RoleDefinitionChanged'` and `'DirectPermissionChanged'` (alongside existing `'ScopedRoleChanged'` and `'AccessDenied'`).
- All role/permission mutations write their `SecurityEvent` in the same `Serializable` SQL transaction; audit insert failure rolls back cleanly (`Roles.AuditUnavailable`).
- Allowlisted `DataJson`: action, `RoleId`/`RoleCode`, `TargetUserId`, `OrganizationId`, `SiteId`, added/removed permission codes (safe machine identifiers only — never user secrets/tokens or unauthorized tenant data).

## 7. Proposed UI pages & bilingual / mobile contract

- **`/roles` & `/organizations/{OrganizationId:long}/roles`**: `BitDataGrid` list of organization roles, Create/Edit `BitDialog` with page-grouped permission checkboxes (**Organizations**, **Sites**, **Users**, **Roles**), localized status toggles with confirmations and `RowVersion` conflict reload.
- **`/users` integration (or `/users/{UserId:long}/permissions`)**: Direct "Permissions" action button per user opening a compact `BitDialog` showing assigned custom/fixed roles plus page-by-page operation checkboxes per authorized organization/site (`View`, `Create`, `Edit`, `Activate/Deactivate`, etc.).
- **Dynamic Nav & Page Button Gating across existing pages**:
  - `MainLayout.razor` & `ReadScopes`: discover scopes from BOTH active `ScopedRoleAssignment` and active custom/direct grants so a user with only `Sites.Read` (without `SiteOperator` fixed role!) sees **Sites** in Navigation and loads `/sites` cleanly.
  - `OrganizationReader.razor`, `Users.razor`: already evaluate granular `Sites.Create`, `Sites.Update`, `Sites.SetActive`, `Users.Create`, `Users.SetActive`, etc. per operation via `IOrganizationPermissionEvaluator` — Slice 03 plugs directly into that without rewriting page authorization logic!
- Full `fa-IR` (RTL) & `en-US` (LTR), desktop (`1440px`) & mobile (`320px`) zero horizontal overflow.

## 8. Acceptance scenarios

See [test-plan.md](test-plan.md) (`R01`–`R06` Domain/Application, `S01`–`S07` disposable SQL + populated migration tests, `B01`–`B06` authenticated bilingual browser tests).

## 9. Decisions & impact vs Slice 02

See [decisions.md](decisions.md) for open design questions `Q-R01`–`Q-R05`.

## 10. Separate status and next step

| Track | Status | Notes |
|---|---|---|
| Design (`03.0`) | APPROVED (2026-11-10) | `Q-R01`–`Q-R05` closed in [decisions.md](decisions.md) |
| Implementation (`03.1`) | VERIFIED | Domain catalog/invariants (`src/ForgeFlow.Domain/Roles/*`), Application DTOs/ports/use cases (`src/ForgeFlow.Application/Roles/*`), and 20 passing unit tests |
| Implementation (`03.2`) | VERIFIED (2026-11-10) | Non-destructive migration `20261110134703_RolesAndGranularPermissions`, dynamic `ScopeAllowed`, `SqlRoleOperationsStore`, `AddRoleManagement()`, and `RoleManagementSqlTests` (`6/6` PASS in `10.1s` on SQL Server) |
| Implementation (`03.3`–`03.4`) | VERIFIED (2026-11-10) | Web DI (`AddRoleManagement()`), dynamic `ReadScopes` (role + direct operation grants), `MainLayout` omitting empty parent `Settings` menu and adding `/roles`, `Roles.razor`, per-user page/operation matrix dialog in `Users.razor`, bilingual `fa-IR`/`en-US` `.resx`, and `RoleManagementPageTests` (`323` non-SQL tests PASS) |
| Verification & Acceptance (`03.5`) | IN PROGRESS | Automated unit (`323` pass), JS draft (`19` pass), and disposable SQL (`6/6` pass) complete; awaiting user migration of `localhost/testforgflow` and manual bilingual/mobile browser sign-off |

- **Next small step**: Apply migration `20261110134703_RolesAndGranularPermissions` to `localhost/testforgflow` and verify `/roles`, `/users` permissions dialog, and dynamic navigation menu in `fa-IR` (RTL) and `en-US` (LTR).
