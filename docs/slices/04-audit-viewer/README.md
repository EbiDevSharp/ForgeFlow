# Slice 04 — Audit & Security Events Viewer (`04-audit-viewer`)

Status: **ACCEPTED (`04.0`–`04.5`)** (2026-11-10) — Delivers the read-only, scope-isolated Audit & Security Events Viewer over `organization.OrganizationChange` and `auth.SecurityEvent` (`ForgeFlow.md` `۰۰.۴` row `05`).

## 1. Objective, actors, preconditions, outputs, and out-of-scope

### Roadmap alignment
Per [ForgeFlow.md](../../../ForgeFlow.md) `۰۰.۴` row `05`, Slice 04 delivers the **Audit & Security Events review pages** over the events recorded by Slices `01` through `03` (`organization.OrganizationChange` and `auth.SecurityEvent`), with server-enforced scope boundaries, filtering, pagination, and safe detail inspection before Slice 05 (`۰۰.۴` row `06`: Item Catalog, Units & BOM).

### Objective
Provide authorized administrators and delegated auditors with a bilingual (`fa-IR` / `en-US`), strictly read-only `/audit` page that queries business change history (`organization.OrganizationChange`) and security/access events (`auth.SecurityEvent`) within their authorized `Organization`, `Site`, or `Platform` scope, displaying actor/subject display info, localized event types/outcomes, UTC-to-culture formatted timestamps, correlation IDs, and allowlisted JSON payload details without ever exposing cross-tenant records or sensitive data.

### Actors and trusted provenance
- **Organization administrator / custom role with `Audit.Read` (or `Roles.Read`) at `Organization(orgId)`**: inspects all `OrganizationChange` rows for `orgId` (and its sites) and all `SecurityEvent` rows scoped to `orgId`.
- **Site-scoped auditor (`Audit.Read` at `Site(orgId, siteId)`)**: inspects `OrganizationChange` and `SecurityEvent` rows strictly matching `OrganizationId == orgId && SiteId == siteId`.
- **Deployment provisioner (`ScopeKind = 0`, `Platform`)**: inspects platform-scoped `SecurityEvent` rows (`OrganizationId IS NULL`, e.g., bootstrap, platform role changes, global account lockouts/sign-in failures) when selecting Platform scope, without implicit access to private tenant audit logs unless also holding an active organization grant.
- **Provenance**: always resolved via `ITrustedActorAccessor` and `IOrganizationPermissionEvaluator` on every list and detail query.

### Preconditions
- Slice 03 (`03-roles-permissions`) UI acceptance (`03.5`) is confirmed by the user.
- Existing `organization.OrganizationChange` and `auth.SecurityEvent` tables, indexes, and append-only database role permissions (`DENY UPDATE, DELETE`) remain intact.

### Out-of-scope
- Mutating, deleting, archiving, or exporting audit rows to external SIEMs.
- Recording new business entities (Item/Unit/BOM belong to Slice 05).
- Real-time streaming/websocket push of audit events.

## 2. Scenarios: happy paths, failures, retries, and concurrency

### Happy paths
1. **Browse scoped business changes (`OrganizationChange`)**: Authorized user opens `/audit`, selects an authorized organization (or site) scope, views paged organization/site creation, rename, and activation/deactivation events with actor username, timestamp, entity code/name, correlation ID, and before/after field details.
2. **Browse scoped security events (`SecurityEvent`)**: User switches to (or filters by) Security Events and filters by event code (`AccountCreated`, `MembershipChanged`, `ScopedRoleChanged`, `RoleDefinitionChanged`, `DirectPermissionChanged`, `AccessDenied`, `PasswordResetCompleted`, etc.), outcome (`Succeeded`, `Rejected`, `Failed`), date range, or correlation ID / actor search.
3. **Inspect event detail modal**: Clicking the details icon opens a read-only `BitDialog` showing structured key-value fields from `ChangeJson` / `DataJson`, actor & subject usernames, correlation GUID, and scope badges.

### Failure, idempotency, and concurrency rules
- **Strict scope isolation (no cross-tenant leakage)**: Requesting an event ID belonging to another organization (or a platform-only event from an organization scope) returns `Audit.NotFound`.
- **Read-only guarantee**: All queries use `AsNoTracking()`; no write endpoints or mutations exist in Slice 04.
- **Bounded pagination & query safety**: Page size is clamped to `1..100` (default `25`), ordered deterministically by `(OccurredAtUtc DESC, Id DESC)` using existing composite indexes.

## 3. Conceptual & physical model summary

See [physical-erd.md](physical-erd.md). Slice 04 reads directly from existing `organization.OrganizationChange` and `auth.SecurityEvent` (joining `auth.User` on `ActorUserId` / `SubjectUserId` for display usernames). If `Q-A01` approves a dedicated `Audit.Read` permission code, a tiny additive migration seeds `Audit.Read` into `auth.RolePermission` for `OrganizationAdministrator` (`RoleId = 1`) and `DeploymentProvisioner` (`RoleId = 3`).

## 4. Proposed Application use cases and boundaries

New read-only contracts in `ForgeFlow.Domain.Audit` & `ForgeFlow.Application.Audit`:

| Use case | Required permission & scope | Inputs / Outputs |
|---|---|---|
| `ListAuditEntries` | `Audit.Read` at `Organization(orgId)`, `Site(orgId, siteId)`, or `Platform` | `AuditListQuery` (`Category`: Business / Security / All, `EventCode`, `Search`, `FromUtc`, `ToUtc`, `Page`, `PageSize`) -> `OrganizationPage<AuditEntrySummary>` |
| `GetAuditEntry` | `Audit.Read` at matching scope | `AuditCategory`, `EntryId` -> `AuditEntryDetails` (or `Audit.NotFound`) |

Stable error codes: `Audit.NotFound`, `Audit.AccessDenied`, `Audit.ActorRequired`, `Audit.ScopeRequired`, `Audit.InvalidFilter`, `Audit.PersistenceUnavailable`.

## 5. Operation x Permission x Scope matrix

| Operation | Permission | Scope | Boundary rule |
|---|---|---|---|
| List / Get tenant business or security audit | `Audit.Read` | `Organization(orgId)` | Returns only rows with `OrganizationId == orgId` |
| List / Get site-scoped audit | `Audit.Read` | `Site(orgId, siteId)` | Returns only rows with `OrganizationId == orgId && SiteId == siteId` |
| List / Get platform security events | `Audit.Read` | `Platform` | Returns only `auth.SecurityEvent` rows with `OrganizationId IS NULL` |

## 6. Audit & security events

- Denied attempts to read `/audit` or unauthorized scopes emit `'AccessDenied'` in `auth.SecurityEvent` via `SessionPermissionEvaluator` (existing behavior).
- Normal authorized audit reads do not write self-referential audit rows (`ForgeFlow.md` `FF-A005`: ordinary reads are not audited).

## 7. Proposed UI pages & bilingual / mobile contract

- **Routes**: `/audit` and `/organizations/{OrganizationId:long}/audit`.
- **Navigation**: Added under `Settings` in `MainLayout.razor` (visible when the actor holds `Audit.Read` in at least one scope; parent `Settings` remains hidden if the actor has zero child items).
- **Controls**: Scope selector, category filter (All / Business Changes / Security Events), event-type filter, search box (username / correlation GUID / entity code), `BitDataGrid` with server-side paging, and a read-only `BitDialog` detail inspector.
- **Bilingual & responsive**: Full `fa-IR` (RTL, Solar Hijri date formatting via `DisplayFormatter`) and `en-US` (LTR, Gregorian UTC formatting), zero horizontal overflow at `1440px` and `320px`.

## 8. Acceptance scenarios

See [test-plan.md](test-plan.md) (`A01`–`A05` unit, `S01`–`S05` disposable SQL Server isolation & append-only checks, `B01`–`B04` bilingual mobile/desktop checks).

## 9. Decisions & impact vs Slice 03

See [decisions.md](decisions.md) for open design questions `Q-A01`–`Q-A04`.

## 10. Separate status and next step

| Track | Status | Notes |
|---|---|---|
| Design (`04.0`) | APPROVED | `Q-A01`–`Q-A04` closed in [decisions.md](decisions.md) |
| Implementation (`04.1`–`04.4`) | IMPLEMENTED | `AuditErrors`, `AuditPayloadParser`, `AuditUseCases`, `SqlAuditOperationsStore`, `Audit.razor`, bilingual `.resx`, `AuditTests`, `AuditSqlTests` |
| Verification & Acceptance (`04.5`) | ACCEPTED | Offline tests (`329 passed, 0 failed`), browser check (`4/4 PASS`), and user UI sign-off on `/audit` |

- **Next small step**: Propose Slice 05 (`05-item-catalog` — Item Catalog, Units & BOM, `ForgeFlow.md` `۰۰.۴` row `06`) design (`05.0`).
