# Slice 03 design decisions (`03-roles-permissions`)

Status: **`03.0` DECISIONS APPROVED** (2026-11-10) — Approved Option A (recommended) for `Q-R01`–`Q-R05` per user approval (`"اوکی خوبه"`). Ready for increment `03.1` (Domain & Application contracts/invariants and unit tests).

## Inherited locked boundaries (from AUTH-01, Slice 01, Slice 02)

- **FF-R01 (Permission granularity already aligned)**: Existing Application/UI checks already evaluate fine-grained operation strings (`Organizations.Read/Create/Update/SetActive`, `Sites.Read/Create/Update/SetActive`, `Users.Read/Create/SetActive/Unlock/ResetPassword/AttachOrganization`, `Memberships.Manage`, `Roles.AssignFixed`) via `IOrganizationPermissionEvaluator`.
- **FF-R02 (Membership before permission)**: Roles and permission grants never bypass active `ApplicationAccount.IsActive`, active `OrganizationMembership.IsActive`, or (for `Site` scope) active `SiteMembership.IsActive`.
- **FF-R03 (No role/permission caching in cookies)**: Claims carry only identity/session references; permissions resolve live against SQL on every protected HTTP/circuit/use-case boundary.
- **FF-R04 (Immutable history & atomic audit)**: Role definitions and grants are toggled via `IsActive`, never hard-deleted, and commit atomically with `auth.SecurityEvent`.

---

## Open material decisions requiring confirmation before `03.1`

### `Q-R01` — Grant model: Custom Roles, Direct Per-User Page Operation Toggles, or Hybrid?
User requested: assigning individual operations per page (View, Create, Edit, Deactivate) to each user.
- **Option A (Hybrid — Recommended)**:
  - Keep the 3 built-in roles (`OrganizationAdministrator`, `SiteOperator`, `DeploymentProvisioner`) as system templates.
  - Add **organization-scoped Custom Roles** (`RoleDefinition` + `RolePermission`) where an admin groups page permissions once (e.g., `"Site Editor"`) and assigns that role to multiple users at `Organization` or `Site` scope.
  - **AND** support **direct per-user, per-scope operation grants** (`UserPermissionGrant` keyed by `(UserId, ScopeKind, OrganizationId, SiteId, PermissionCode)`) so from `/users` an admin can also toggle individual page operations (`Sites.Read`, `Sites.Create`, `Sites.Update`, `Sites.SetActive`, etc.) directly for a specific user!
  - Effective permission = `(Active Organization/Custom Roles in scope) UNION (Active Direct UserPermissionGrants in scope)` (checked after active `ApplicationAccount` & active `OrganizationMembership`/`SiteMembership`).
- **Option B (Custom Roles only)**: Every custom permission bundle must first be saved as a named `RoleDefinition`, then assigned to a user (zero `UserPermissionGrant` table, slightly fewer DB objects, extra step when customizing a single user).
- **Option C (Per-User Direct Matrix only)**: Drop custom roles and only store per-user page/operation checkboxes. Simpler UI for 1–5 users, repetitive when scaling to 20+ users.

### `Q-R02` — Permission dependencies (e.g. granting `Sites.Update` or `Sites.Create` without `Sites.Read`)
- **Option A (Recommended)**:
  - Server enforces **explicit grants only** (granting `Sites.Create` never implicitly grants `Sites.Read` or `Organizations.Read`).
  - UI permission matrix automatically checks `*.Read` when the admin enables `*.Create`, `*.Update`, or `*.SetActive` (and explains that viewing a list requires `*.Read`), while allowing the admin to grant `*.Read` alone for read-only users.
- **Option B**: Automatically imply `*.Read` on the server whenever any write permission on that page exists.

### `Q-R03` — Privilege-escalation & "Last Admin" protection with custom roles
- **Proposal (Recommended)**:
  1. An administrator can assign/grant only **tenant/site** permissions in their own active `Organization(orgId)` — never `Platform` permissions (`Organizations.Create`, `Users.AttachOrganization`, `DeploymentProvisioner`).
  2. **No privilege escalation ("grant ceiling")**: An administrator who lacks permission `P` (unless they hold active `OrganizationAdministrator` system role) cannot grant `P` to a custom role or another user (`Roles.EscalationForbidden`).
  3. Built-in `OrganizationAdministrator` (`RoleId = 1`) and any org's last active user able to manage users/roles (`OrganizationAdministrator` OR holding `Roles.Manage` + `Permissions.Assign` + `Users.SetActive`?) — **Recommended: retain the invariant requiring at least one active built-in `OrganizationAdministrator` in every active organization** (`Users.LastOrganizationAdminRequired`), so custom roles cannot accidentally brick tenant administration.

### `Q-R04` — Built-in system roles (`RoleId 1, 2, 3`) mutability
- **Recommended**: Built-in system roles (`OrganizationAdministrator = 1`, `SiteOperator = 2`, `DeploymentProvisioner = 3`) are seeded into `auth.RoleDefinition` with `IsSystem = 1` and remain immutable (cannot be renamed, deleted, or stripped of default operations). All custom roles start with identity `>= 100` (`OrganizationId != NULL`, `IsSystem = 0`).

### `Q-R05` — Safe migration for existing `localhost/testforgflow` database
- Currently `auth.ScopedRoleAssignment` has `CK_Role_Scope` restricting `RoleId IN (1, 2, 3)` and `auth.SecurityEvent` has `CK_SecurityEvent_Code`.
- **Recommended**: Additive migration (`RolesAndGranularPermissions`) that:
  1. Creates `auth.RoleDefinition` and seeds `Id = 1, 2, 3` (`SET IDENTITY_INSERT` in migration `Up` script, matching `OrganizationAdministrator`, `SiteOperator`, `DeploymentProvisioner`) so existing rows in `auth.ScopedRoleAssignment` immediately match FK `FK_ScopedRoleAssignment_RoleDefinition` cleanly!
  2. Replaces `CK_Role_Scope` with a structural scope check (`(ScopeKind=0 AND OrganizationId IS NULL AND SiteId IS NULL) OR (ScopeKind=1 AND OrganizationId IS NOT NULL AND SiteId IS NULL) OR (ScopeKind=2 AND OrganizationId IS NOT NULL AND SiteId IS NOT NULL)`), plus FK to `RoleDefinition` ensuring custom roles are only used inside their own `OrganizationId`!
  3. Creates `auth.RolePermission` (and `auth.UserPermissionGrant` if `Q-R01` Option A/C is approved), and expands `CK_SecurityEvent_Code` with `'RoleDefinitionChanged'` and `'DirectPermissionChanged'`.
  4. Never drops/truncates existing `BootstrapState`, `User`, `OrganizationMembership`, `SiteMembership`, `ScopedRoleAssignment`, or `SecurityEvent` rows.
