# Slice 02 design decisions (`02-users-memberships`)

Status: **`02.0` DECISIONS APPROVED** (2026-11-10) — User authorized selecting the strongest engineering/security option for `Q-U01`–`Q-U05`. Ready to implement increment `02.1` (Domain & Application contracts/invariants).

## Inherited locked boundaries (from AUTH-01 and Slice 01)

- **FF-U01 (Username format and immutability)**: `auth.User.UserName` is ASCII `[A-Za-z0-9._@+-]`, length `1..128`, uniquely indexed case-insensitively/canonically via `NormalizedUserName` (`Latin1_General_100_BIN2`), and immutable after creation (`AuthDbContext.Guard()` throws `Auth.ImmutableHistory` on `UserName` or `NormalizedUserName` modification).
- **FF-U02 (Membership hierarchy and composite integrity)**: A `SiteMembership(UserId, OrganizationId, SiteId)` cannot exist without a parent `OrganizationMembership(UserId, OrganizationId)` and a matching `Site(OrganizationId, Id)`. Membership alone never grants permissions without explicit `ScopedRoleAssignment`.
- **FF-U03 (No soft/hard delete & immutable audit)**: Users, memberships, role assignments, sessions, and `SecurityEvent` rows are never deleted; eligibility toggles use `IsActive` (`bool`) with audit events in the same transaction.
- **FF-U04 (Immediate invalidation on disable/revocation)**: Disabling an account revokes all active `AccountSession` rows atomically (`RevokeReason = 'AccountDisabled'`) and rotates `SecurityStamp`; revoking membership or role takes effect immediately on the next `SessionPermissionEvaluator` / HTTP / SignalR check.
- **FF-U05 (No email infrastructure assumption)**: This greenfield app has no email/SMS sender; workflows operate without outbound email links.

---

## Approved decisions for Slice 02 (`Q-U01`–`Q-U05`)

### `Q-U01` — Initial credential provisioning when creating a user — **APPROVED: Option A (initial password at creation, zero schema migration)**
- During `CreateOrganizationUserAsync`, the authorized administrator provides a one-time initial password in a masked input (`type="password"`), validated through ASP.NET Identity's `PasswordValidator` and hashed via `UserManager.CreateAsync(user, password)`.
- **Security hygiene**: The initial password is never stored in `DraftCoordinator` / `sessionStorage`, never logged (`ILogger`), and never written to `SecurityEvent.DataJson`.
- **Deferred item**: Forced password change on first login (`MustChangePassword` column and `/account/change-password` interception flow) is deferred to keep `auth.User` free of speculative schema changes in Slice 02.

### `Q-U02` — Fixed role assignment boundary (`OrganizationAdministrator` / `SiteOperator`) — **APPROVED: Option A (fixed tenant roles included)**
- Slice 02 supports assigning/toggling the two existing tenant-scoped fixed roles (`FixedAccessRoles.OrganizationAdministrator` at `Organization(orgId)` scope, `FixedAccessRoles.SiteOperator` at granted `Site(orgId, siteId)` scope) alongside user creation and membership management, using existing `auth.ScopedRoleAssignment` and `'ScopedRoleChanged'` `SecurityEvent`.
- Assigning `SiteOperator` requires active `OrganizationMembership` and active `SiteMembership` in `(orgId, siteId)` (enforced in Application checks and SQL composite FKs).
- Platform-level `DeploymentProvisioner` assignment, custom role definitions, and fine-grained permission matrices remain strictly deferred to Slice 03 (`۰۰.۴` row 04).

### `Q-U03` — Tenant isolation, global `NormalizedUserName`, and account vs membership deactivation — **APPROVED: Option A (strict tenant isolation + hybrid single-org deactivation)**
- **Directory scoping**: An `OrganizationAdministrator` in `orgId` sees **only** users holding an `OrganizationMembership` in `orgId`.
- **Duplicate usernames**: `CreateOrganizationUserAsync` enforces global `NormalizedUserName` uniqueness; collisions return `Users.UserNameDuplicate` without leaking whether the existing account belongs to another organization.
- **Multi-org attachment**: Attaching an already-existing `UserName` across organizations and toggling global `ApplicationAccount.IsActive` for multi-org / platform users require `OrganizationScope.Platform` (`DeploymentProvisioner`).
- **Deactivating a user from Organization scope**:
  - `SetOrganizationMembershipActiveAsync` is always available in `Organization(orgId)` scope and immediately cuts off all access to `orgId` without affecting memberships in other organizations.
  - `SetUserActiveAsync` (global `ApplicationAccount.IsActive`, which revokes all `AccountSession` rows with `RevokeReason = 'AccountDisabled'` and rotates `SecurityStamp`) is allowed in `Organization(orgId)` scope **only when the target user belongs exclusively to `orgId`** (no membership in any other organization and no Platform `DeploymentProvisioner` assignment); otherwise `Users.AccessDenied` is returned and the org admin toggles `OrganizationMembership.IsActive` instead.

### `Q-U04` — Editable account fields, lockout unlock, and password reset — **APPROVED: zero schema additions + UnlockUserAsync (host-only reset preserved)**
- **Fields**: Keep `auth.User` schema unchanged (`UserName` immutable, `IsActive`, `PreferredCulture`, `LockoutEnd`/`AccessFailedCount`). No speculative `DisplayName` column is added in Slice 02.
- **Lockout recovery**: `UnlockUserAsync` is provided in `Organization(orgId)` scope (with the same exclusive-org/Platform isolation as `SetUserActiveAsync`), clearing `LockoutEnd` and `AccessFailedCount` atomically with `'AccountEligibilityChanged'`.
- **Password reset**: Remains host-operator only via `HostRecovery` (no tenant-admin credential takeover across accounts in Slice 02).

### `Q-U05` — Self-protection and last-active-admin safeguard — **APPROVED**
Under the `Organization` `UPDLOCK, HOLDLOCK` lock protocol (followed by `ApplicationAccount` lock):
1. An actor cannot deactivate their own `ApplicationAccount`, their own `OrganizationMembership` in `orgId`, or their own `OrganizationAdministrator` role in `orgId` (`Users.SelfDeactivationForbidden`).
2. Deactivating an account, `OrganizationMembership`, or `OrganizationAdministrator` role cannot leave an active organization with zero active `OrganizationAdministrator`s (requiring active `ApplicationAccount` + active `OrganizationMembership` + active `OrganizationAdministrator` assignment) (`Users.LastOrganizationAdminRequired`).
3. At `Platform` scope, deactivating an account or `DeploymentProvisioner` grant cannot leave zero active `DeploymentProvisioner`s if at least one exists (`Users.LastPlatformAdminRequired`).
