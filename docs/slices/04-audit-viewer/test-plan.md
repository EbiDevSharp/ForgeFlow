# Slice 04 acceptance test plan (`04-audit-viewer`)

## 1. Domain & Application unit tests (no SQL)

| ID | Scenario and required assertion |
|---|---|
| A01 | `PermissionCatalog` includes `Audit.Read` (`PermissionModule.Audit`, `OrganizationOrSite`) and `AuditUseCases` denies missing actor/scope/permission before store access |
| A02 | Paging bounds (`1..100`), category/event-code filter validation, and cross-organization `Audit.NotFound` guards behave deterministically |
| A03 | `ChangeJson` and `DataJson` parser extracts safe key-value pairs and handles empty `{}` payloads cleanly without throwing |
| A04 | All `Audit.*` error codes, event codes (`BootstrapCompleted`, `AccountCreated`, `SignInFailed`, `AccountLocked`, `SessionIssued`, `SessionRevoked`, `AccessDenied`, `PasswordResetCompleted`, `AccountEligibilityChanged`, `MembershipChanged`, `ScopedRoleChanged`, `RoleDefinitionChanged`, `DirectPermissionChanged`, `CulturePreferenceChanged`), and business action kinds map 1:1 to `fa-IR` and `en-US` `.resx` keys |

## 2. Real SQL Server integration tests (disposable DBs)

| ID | Scenario and required assertion |
|---|---|
| S01 | Scoped query isolation: `ALPHA` admin sees `ALPHA` `OrganizationChange` and `SecurityEvent` rows (with resolved actor/subject `UserName`s), never `BETA` rows or `OrganizationId IS NULL` platform rows |
| S02 | Site-scoped auditor (`Audit.Read` at `Site(ALPHA, A1)`) sees only events for `SiteId == A1`, while `A2` and org-wide events return empty / `Audit.NotFound` |
| S03 | Platform scope (`DeploymentProvisioner`) lists `OrganizationId IS NULL` security events and cannot read `BETA` tenant events without `BETA` membership & grant |
| S04 | Filter by category (`Business` / `Security`), `EventCode`, `CorrelationId`, and username search returns exact ordered pages (`OccurredAtUtc DESC, Id DESC`) |
| S05 | Append-only verification: `SecurityEvent` and `OrganizationChange` remain immutable via `AuthDbContext.Guard()` and runtime principal permissions |

## 3. Authenticated bilingual desktop & mobile browser acceptance (`fa-IR` & `en-US`, `1440px` & `320px`)

| ID | Scenario and required assertion |
|---|---|
| B01 | `/audit` appears under `Settings` when `Audit.Read` is granted, displays localized timestamps (`DisplayFormatter` Solar Hijri in `fa-IR`, Gregorian in `en-US`), localized event badges, and opens the detail modal cleanly |
| B02 | Custom role or direct per-user grant for `Audit.Read` on `/roles` / `/users` immediately enables `/audit` for that user |
| B03 | Zero horizontal overflow at `1440px` and `320px` in both `fa-IR` (RTL) and `en-US` (LTR) |
