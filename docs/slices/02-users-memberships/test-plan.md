# Slice 02 acceptance test plan (`02-users-memberships`)

This test plan covers Domain/Application, real disposable SQL Server, HTTP/session invalidation, and bilingual browser verification before Slice 02 can ever be marked accepted. All SQL tests create only generated `ForgeFlow_Slice02_Test_*` disposable databases (`FORGEFLOW_TEST_SQLSERVER`), never touching `localhost/testforgflow` or any existing database.

## 1. Domain & Application contract tests (no SQL)

| ID | Scenario and required assertion |
|---|---|
| U01 | Username validation matches `AUTH-01` rules (`[A-Za-z0-9._@+-]`, length `1..128`, rejects spaces/Persian/control chars, trims/normalizes consistently in `fa-IR` and `en-US`) |
| U02 | Immutable `UserName` and `ScopedRoleAssignment` scope keys stay rejected by `AuthDbContext.Guard()` while `IsActive`/`PreferredCulture`/`LockoutEnd` updates succeed cleanly |
| U03 | Scope & anti-enumeration checks: denied caller, wrong org ID, missing org scope, or untrusted actor returns `Users.AccessDenied` / `Users.ActorRequired` / `Users.ScopeRequired`; guessing a user ID outside `orgId` returns `Users.NotFound` (indistinguishable from nonexistent user) |
| U04 | Self-deactivation and last-active-admin protection rules return `Users.SelfDeactivationForbidden` and `Users.LastOrganizationAdminRequired` before persistence writes |
| U05 | Concurrency & no-op rules: matching expected version no-op performs zero audit/DB writes; stale expected version returns `Users.ConcurrencyConflict`; audit failure rolls back cleanly and maps to `Users.AuditUnavailable` |
| U06 | Stable error codes map 1:1 to explicit `fa-IR` / `en-US` `.resx` keys (verified by `ResourceContractTests`) without leaked exception text or concatenated fragments |

## 2. Real SQL Server integration tests (disposable DBs, barrier races, fault injection)

Using synthetic organizations `ALPHA` and `BETA`, sites `A1`/`A2` and `B1`:

| ID | Scenario and required assertion |
|---|---|
| S01 | Atomic user + `OrganizationMembership` + optional `SiteMembership` creation writes `auth.User`, memberships, and `SecurityEvent` (`AccountCreated`, `MembershipChanged`) in one transaction; fault injection on membership or `SecurityEvent` insert rolls back `auth.User` cleanly |
| S02 | Duplicate `NormalizedUserName` across organizations returns `Users.UserNameDuplicate`, preserves the existing account and emits zero success `SecurityEvent` rows |
| S03 | Composite FK & parent-active enforcement: assigning `SiteMembership` for `B1` inside `ALPHA` scope is rejected (`Site.NotFound`); assigning `SiteMembership` while `OrganizationMembership` or `Site` is inactive is rejected |
| S04 | Deactivating `ApplicationAccount.IsActive = false` atomically revokes all open `AccountSession` rows (`RevokeReason = 'AccountDisabled'`), rotates `SecurityStamp`, and emits `'AccountEligibilityChanged'` + `'SessionRevoked'`; immediate HTTP cookie replay & `SessionPermissionEvaluator` calls fail closed |
| S05 | Deactivating `OrganizationMembership` or `SiteMembership` leaves active session cookie valid only if other scope permits, and immediately denies the revoked `orgId`/`siteId` use cases while persisting `'MembershipChanged'` |
| S06 | Concurrent race (barrier-synchronized): two admins in `ALPHA` simultaneously attempt to deactivate each other — exactly one succeeds and the second is blocked by `Users.LastOrganizationAdminRequired` under `Organization` `UPDLOCK, HOLDLOCK`, guaranteeing at least one active admin remains |
| S07 | Concurrent rowversion edit on the same `OrganizationMembership` / `ApplicationAccount`: one succeeds, the loser returns `Users.ConcurrencyConflict`; runtime SQL principal still cannot DELETE rows or UPDATE `SecurityEvent` |

## 3. Authenticated bilingual & mobile browser acceptance (`fa-IR` & `en-US`, `1440px` & `320px`)

| ID | Scenario and required assertion |
|---|---|
| B01 | Authorized `OrganizationAdministrator` opens `/users` or `/organizations/{orgId}/users`, filters active/inactive, pages `BitDataGrid`, creates a user with `OrganizationMembership`/`SiteMembership`, reloads and confirms persistence |
| B02 | Cross-tenant user isolation: `ALPHA` admin cannot view, edit, or guess-URL-open a user belonging only to `BETA` (sees localized forbidden/not-found alert with zero `BETA` data leaked) |
| B03 | Deactivation & live session revocation: user logged in across two tabs/browsers is deactivated by admin; within the running-circuit cadence (or on next navigation/write), active UI masks content, clears scoped drafts, and forces redirect to `/account/login` |
| B04 | Full workflow verified in both `fa-IR` (RTL) and `en-US` (LTR) at desktop (`1440px`) and mobile (`320px`) with zero horizontal overflow, clean keyboard focus trapping in dialogs, and accessible names/announcements |
| B05 | Conflict reload & unsaved-change warning: editing a stale rowversion shows localized conflict dialog with reload action; closing a dirty edit modal prompts before discarding |
| B06 | Sensitive input hygiene (`Q-U01` if temporary password is used): temporary password field is never stored in `sessionStorage` drafts across culture switch or refresh |
