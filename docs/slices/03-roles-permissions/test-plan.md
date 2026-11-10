# Slice 03 acceptance test plan (`03-roles-permissions`)

All SQL tests run exclusively on generated disposable `ForgeFlow_Slice03_Test_*` databases (`FORGEFLOW_TEST_SQLSERVER`), never against `localhost/testforgflow`.

## 1. Domain & Application contract tests (no SQL)

| ID | Scenario and required assertion |
|---|---|
| R01 | Permission catalog contains every page/module operation (`Organizations.*`, `Sites.*`, `Users.*`, `Memberships.*`, `Roles.*`, `Permissions.*`) with explicit allowed scopes (`Platform`, `Organization`, `Site`) and rejects unknown codes (`Roles.PermissionUnknown`) |
| R02 | Custom role name normalization & invariant checks reject blank/control/>100-char names consistently in `fa-IR` and `en-US`; reject `Platform`-only permissions inside tenant roles (`Roles.EscalationForbidden`) |
| R03 | Built-in system roles (`1, 2, 3`) reject rename/modify/deactivate (`Roles.SystemRoleImmutable`) |
| R04 | Scope/membership/escalation checks reject missing actor/scope/permission before store access, return `Roles.NotFound` for cross-org role IDs, and reject granting permissions above caller's own ceiling (`Roles.EscalationForbidden`) |
| R05 | Stable `Roles.*` error codes map 1:1 to explicit `fa-IR`/`en-US` `.resx` keys without raw descriptions or missing placeholders |

## 2. Real SQL Server integration & concurrency tests (disposable DBs)

| ID | Scenario and required assertion |
|---|---|
| S01 | Migration upgrade on an already-bootstrapped disposable DB preserves existing `BootstrapState`, `ApplicationAccount`, `OrganizationMembership`, `SiteMembership`, `ScopedRoleAssignment` (`RoleId = 1`), and `SecurityEvent` rows cleanly |
| S02 | Creating custom role `"Site Reader"` (`Sites.Read` only) in `ALPHA` and assigning it to user `u1` allows `ListSites` / `GetSite` inside `ALPHA`, while `CreateSite`, `RenameSite`, `SetSiteActive`, `Organizations.Read`, `Users.Read`, and `BETA` remain denied (`AccessDenied` + `'AccessDenied'` event) |
| S03 | Assigning direct per-user operation grant `Sites.Update` at `Site(ALPHA, A1)` permits `RenameSite` ONLY on `A1`, while `A2`, `CreateSite`, and `SetSiteActive` stay denied; revoking `SiteMembership` or the grant denies the very next call immediately |
| S04 | Cross-organization role isolation: `BETA` admin cannot view, edit, or assign an `ALPHA` custom role (`Roles.NotFound`); two orgs can each have a role named `"Supervisor"` independently (`(OrganizationId, NormalizedName)`) |
| S05 | Audit failure rollback (trigger on `auth.SecurityEvent`) rolls back `RoleDefinition`, `RolePermission`, `UserPermissionGrant`, and `ScopedRoleAssignment` atomically (`Roles.AuditUnavailable`) |
| S06 | Concurrent rowversion updates on `RoleDefinition` / `UserPermissionGrant` enforce optimistic concurrency (`Roles.ConcurrencyConflict`) and no-op emits zero extra audit events |
| S07 | Last active `OrganizationAdministrator` safeguard cannot be bypassed through role edits or permission revocation under barrier-synchronized concurrency (`Users.LastOrganizationAdminRequired`) |

## 3. Authenticated bilingual desktop & mobile browser acceptance (`fa-IR` & `en-US`, `1440px` & `320px`)

| ID | Scenario and required assertion |
|---|---|
| B01 | Admin creates custom role via `/roles`, toggles per-page operation checkboxes (e.g., `Sites`: View + Edit without Create/Deactivate), assigns to user, logs in as that user in second browser/incognito: nav shows only `Sites`, `/sites` loads grid & Edit icon, Create/Deactivate buttons absent, direct `/users` shows `/account/access-denied` |
| B02 | Admin revokes `Sites.Update` from user while user's browser tab stays open; on user's next save/navigation attempt, localized denial displays cleanly without silent success |
| B03 | Per-user Permissions editor in `/users` (View/Create/Edit/Deactivate per page) loads, saves, shows `RowVersion` conflict reload cleanly |
| B04 | Full workflows pass in `fa-IR` (RTL) and `en-US` (LTR) at `1440px` desktop and `320px` mobile with zero horizontal overflow, clean focus trapping in dialogs, and icon-only drawer toggle on small screens |
