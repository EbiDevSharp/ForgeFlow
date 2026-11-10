# Slice 04 design decisions (`04-audit-viewer`)

Status: **APPROVED & CLOSED** (2026-11-10)

## Closed design decisions

### `Q-A01` — Dedicated `Audit.Read` permission (`APPROVED`)
- **Decision**: Added an explicit **`Audit.Read`** permission under `PermissionModule.Audit` (`OrganizationOrSite` applicability, plus `Platform` for `DeploymentProvisioner`).
- **Implementation**: Registered in `PermissionCatalog`, `AuditPermissions.AuditRead`, `RuntimeSessions.ScopeAllowed`, `FixedRolePolicy.Allows`, and `SqlRoleOperationsStore` so built-in `OrganizationAdministrator` (`RoleId = 1`) and `DeploymentProvisioner` (`RoleId = 3`) hold `Audit.Read` automatically without requiring a destructive or model-diverging schema change, while custom roles and per-user direct operation grants can also assign `Audit.Read`.

### `Q-A02` — Unified `/audit` timeline with Category & EventCode filters (`APPROVED`)
- **Decision**: One `/audit` page (`/audit` and `/organizations/{OrganizationId:long}/audit`) with a **Category selector (`All`, `Business Changes`, `Security Events`)**, event-code filter, and search box, normalizing both `organization.OrganizationChange` and `auth.SecurityEvent` into `AuditEntrySummary` / `AuditEntryDetails`.

### `Q-A03` — Strict scope isolation & Platform-null `SecurityEvent` visibility (`APPROVED`)
- **Decision**:
  - `Organization(orgId)` returns only rows with `OrganizationId == orgId`.
  - `Site(orgId, siteId)` returns only rows with `OrganizationId == orgId && SiteId == siteId`.
  - `Platform` returns only `auth.SecurityEvent` rows with `OrganizationId == null`.

### `Q-A04` — Structured JSON payload presentation in Detail Dialog (`APPROVED`)
- **Decision**: `AuditPayloadParser.ParseFields` flattens allowlisted `ChangesJson` / `DataJson` fields into a key-value table inside the `BitDialog` (with `dir="ltr"` for keys, codes, and `CorrelationId`), alongside the read-only raw JSON block.
