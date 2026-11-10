# Slice 06 — Test & Acceptance Plan (`06-production-orders`)

## 1. Domain & Application Unit Tests (`ProductionOrderTests.cs`)
- **`P01` Scope & Permission enforcement**: `ProductionOrders.Read` and `ProductionOrders.Manage` allow `OrganizationScope.Site(orgId, siteId)` only for the exact matching `SiteId`, and deny access across sibling sites or unrelated organizations.
- **`P02` Draft creation & BOM snapshot capture**: Creating a `Draft` Production Order resolves the effective `BomVersion` (injecting `PLANT = Site.Code` automatically when omitted), validates manufacturability and positive quantity, and stores planned inputs/outputs and context criteria.
- **`P03` Lifecycle state machine**: Only valid transitions succeed:
  - `Draft -> Released` (sets `ReleasedAtUtc`)
  - `Released -> InProgress` (sets `StartedAtUtc`)
  - `InProgress -> Completed` (sets `CompletedAtUtc`, validates `ActualProducedQuantity >= 0` & `ActualScrapQuantity >= 0`)
  - `Draft / Released / InProgress -> Cancelled` (sets `CancelledAtUtc` & reason)
  - Reverse transitions (`Released -> Draft`, `Completed -> InProgress`, `Cancelled -> Draft`) and structural updates once `Status != Draft` fail with `ProductionOrders.OrderImmutable` or `ProductionOrders.TransitionInvalid`.
- **`P04` Snapshot immutability across later BOM changes (`Day 11` core invariant)**: Once a Production Order is `Released`, superseding or obsoleting the source `BomVersion` (or activating a newer BOM version with changed quantities) never mutates the existing Production Order's `BomVersionId`, `BomVersionNumberSnapshot`, `Inputs`, or `Outputs`.
- **`P05` Inactive Site / Item / Unit rejection**: Creating or releasing an order for an inactive Site, inactive Item, or inactive Unit fails deterministically.
- **`P06` Optimistic concurrency (`RowVersion`)**: Stale `ExpectedRowVersion` fails with `ProductionOrders.ConcurrencyConflict`.
- **`P07` Bilingual contract & route checks**: `/production-orders` and `/organizations/{OrganizationId:long}/production-orders` routes, `ResourceContractTests` key completeness, and `RolePermissionLocalization` error mappings pass in both `fa-IR` and `en-US`.

## 2. Live SQL Server Integration Tests (`ProductionOrderSqlTests.cs`, `[OrganizationSqlServerFact]`)
- **`S01` Fresh migration & rollback**: `OrganizationDbContext` applies `ProductionOrders` migration cleanly with `HasPendingModelChanges() == false` (and `AuthDbContext.HasPendingModelChanges() == false`) and rolls back cleanly when empty, rejecting rollback if non-empty.
- **`S02` Cross-tenant & cross-site SQL FKs**: Creating a `ProductionOrder` referencing a `Site`, `Item`, or `BomVersion` from another organization fails via composite FKs.
- **`S03` Runtime SQL principal protection**: `ForgeFlowOrganizationRuntime` cannot `DELETE FROM [organization].[ProductionOrder]` (SQL error 229).
- **`S04` Atomic business audit**: Every `ProductionOrder` write emits the corresponding `SiteId`-populated `ProductionOrder.*` event in `organization.OrganizationChange`; simulated audit failure rolls back the business write.

## 3. UI & Browser Acceptance (`B01`–`B04`)
- **`B01`**: Clean build (`0 Warning(s), 0 Error(s)`), all non-SQL + SQL tests pass, zero horizontal overflow in `fa-IR` RTL and `en-US` LTR at `1440px` and `320px`.
- **`B02`**: `BitDatePicker` on `/production-orders` (`PlannedStartDate`, `PlannedDueDate`) displays Jalali in `fa-IR` and Gregorian in `en-US` and formats grid/detail dates with `DisplayFormatter.Date`.
- **`B03`**: Site-scoped `SiteOperator` only sees and manages Production Orders for their assigned `Site`; `OrganizationAdministrator` can switch sites via the scope dropdown.
- **`B04`**: Full lifecycle walkthrough in browser: Create Draft (with automatic BOM preview) → Release → Start → Complete with actual output quantity → verify later BOM version updates do not alter the completed order snapshot.
