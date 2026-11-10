# Slice 04 physical model & index review (`04-audit-viewer`)

## 1. Existing audit tables consumed (no destructive changes)

### `organization.OrganizationChange` (from Slice 01)
- **Primary key**: `Id bigint IDENTITY(1,1)`
- **Columns**: `OccurredAtUtc datetime2(7)`, `ActorId bigint`, `ActorProvenance tinyint`, `CorrelationId uniqueidentifier`, `OrganizationId bigint`, `SiteId bigint NULL`, `EntityKind tinyint`, `EntityId bigint`, `ActionKind tinyint`, `ChangeJson nvarchar(2000)`
- **Existing index**: `IX_OrganizationChange_OrganizationId_OccurredAtUtc_Id` on `(OrganizationId, OccurredAtUtc, Id)` — supports scoped paging ordered by `OccurredAtUtc DESC, Id DESC`.

### `auth.SecurityEvent` (from AUTH-01, Slice 02, Slice 03)
- **Primary key**: `Id bigint IDENTITY(1,1)`
- **Columns**: `OccurredAtUtc datetime2(7)`, `ActorKind tinyint`, `ActorUserId bigint NULL`, `HostOperatorReference nvarchar(256) NULL`, `SubjectUserId bigint NULL`, `SessionId uniqueidentifier NULL`, `OrganizationId bigint NULL`, `SiteId bigint NULL`, `EventCode varchar(64)`, `Outcome tinyint`, `CorrelationId uniqueidentifier`, `DataJson nvarchar(2000)`
- **Existing indexes**:
  - `IX_SecurityEvent_OrganizationId_OccurredAtUtc_Id` on `(OrganizationId, OccurredAtUtc, Id)`
  - `IX_SecurityEvent_SubjectUserId_OccurredAtUtc_Id` on `(SubjectUserId, OccurredAtUtc, Id)`
  - `IX_SecurityEvent_CorrelationId` on `(CorrelationId)`
  - `IX_SecurityEvent_OccurredAtUtc_Id` on `(OccurredAtUtc, Id)`

## 2. Additive permission seed (when `Q-A01` is approved)

- Insert `('Audit.Read', 1, '2026-11-10T00:00:00Z', 0, 1, NULL, NULL)` and `('Audit.Read', 3, '2026-11-10T00:00:00Z', 0, 1, NULL, NULL)` into `auth.RolePermission` via a small additive migration so `OrganizationAdministrator` (`RoleId = 1`) and `DeploymentProvisioner` (`RoleId = 3`) automatically hold `Audit.Read`, and custom roles / per-user operation grants can also assign `Audit.Read`.
