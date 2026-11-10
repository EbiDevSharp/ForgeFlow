# Slice 07 Design Decisions (`Q-I01`–`Q-I04`)

Status: **APPROVED (`07.0`, 2026-11-10) — User Authorized**

Confirmed decisions (`Q-I01`–`Q-I04`):
- `Q-I01`: Site-scoped `PermissionModule.Inventory = 10` (`Inventory.Read`, `Inventory.Manage`) as `OrganizationOrSite`.
- `Q-I02`: Support `ProductionIssue` & `ProductionReceipt` inside `/inventory` plus optional `/production-orders` shortcuts powered by `InventoryUseCases`.
- `Q-I03`: Strict non-negative `OnHandBaseQuantity >= 0` under serialized SQL lock + optional `LotCode` (`varchar(32)`, empty string when un-lotted).
- `Q-I04`: New collapsible `InventoryNavGroup` -> `/inventory` (`Balances`, `Movements / Ledger`, `Warehouses`).

## Open Questions

### `Q-I01` — Scope & Permission Granularity (`OrganizationOrSite`)
- **Question**: Should Slice 07 introduce `PermissionModule.Inventory = 10` with `Inventory.Read` and `Inventory.Manage` marked `PermissionScopeApplicability.OrganizationOrSite` (matching `ProductionOrders`, so a Site Operator can view/manage Warehouses and post movements only for their authorized Site(s), while inter-site transfers require access covering both Warehouses or `Organization` scope)?
- **Recommended**: Yes — `OrganizationOrSite` keeps Site-A stock and movements completely isolated from Site-B operators, while granting both to built-in `OrganizationAdministrator`.

### `Q-I02` — Production Order Integration Surface
- **Question**: Where should operators post `ProductionIssue` (consume raw materials) and `ProductionReceipt` (receive finished/co/by-products) for a `Released` or `InProgress` `ProductionOrder`?
- **Options**:
  1. **(Recommended)** Both inside `/inventory` (select Warehouse + Transaction Type = `ProductionIssue` / `ProductionReceipt` + choose active `Released`/`InProgress` order, pre-filling planned inputs/outputs) **and** as action buttons in the `/production-orders` workbench modal once a Warehouse is selected.
  2. Only inside `/inventory`, keeping `/production-orders` completely decoupled from Warehouse selection.
- **Recommended**: Option 1 — seamless workflow for operators while keeping `InventoryUseCases` as the single authoritative stock ledger service (`ProductionOrderUseCases` stays clean of stock mutation).

### `Q-I03` — Negative Stock Prevention & Optional Lot Tracking
- **Question**: Should Slice 07 strictly forbid negative on-hand stock (`OnHandBaseQuantity >= 0` enforced under serialized SQL row lock + `CHECK` constraint) and make `LotCode` optional (`""` sentinel when untracked, optional alphanumeric batch/lot code when tracked)?
- **Recommended**: Yes — strict `OnHandBaseQuantity >= 0` guarantees physical integrity and prevents oversell/over-issue races, while optional `LotCode` (`varchar(32)`, empty string for un-lotted stock) keeps everyday items simple while supporting batch traceability (`انبار/بچ و lot`) where needed.

### `Q-I04` — Navigation Placement & Inventory Workspace
- **Question**: Add a collapsible **Inventory (`InventoryNavGroup`)** nav group right below **Production** in `MainLayout.razor` linking to `/inventory` (3 tabs: `On-Hand Balances`, `Stock Movements`, `Warehouses`), visible to any user with `Inventory.Read`?
- **Recommended**: Yes — mirrors `ProductionNavGroup` and `MasterDataNavGroup` cleanly without crowding the sidebar.
