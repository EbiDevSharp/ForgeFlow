# Slice 12 — Stock Reservations & Operations Dashboard (`12-reservations-dashboard`)

## Scope
Closes the end-to-end ERP operational cycle across Inventory, Production Orders, Quality Control, Procurement, and Work Centers:
1. **Site-Scoped Stock Reservations (`StockReservation` + `InventoryBalance.ReservedBaseQuantity`)**:
   - Hard allocation of free stock (`AvailableBaseQuantity = OnHandBaseQuantity - BlockedBaseQuantity - ReservedBaseQuantity`) per `(Site, Warehouse, Item, LotCode)` toward a `Released` or `InProgress` `ProductionOrder`.
   - Manual warehouse/lot reservation or FIFO auto-allocation across available non-quarantined/non-expired balances.
   - Automatic reservation consumption when `InventoryUseCases` posts a `ProductionIssue` against that order, and automatic release of any remaining active reservations when a `ProductionOrder` transitions to `Completed` or `Cancelled`.
   - Shortage Reporting integration (`AvailableBaseQuantity` deducts both `BlockedBaseQuantity` and `ReservedBaseQuantity`).
2. **Permission-Aware Operations Dashboard (`/` — `Home.razor`)**:
   - Real-time KPI summary cards and actionable operational queues scoped to the user's selected Organization or Site, filtered dynamically by existing `*.Read` permissions (`ProductionOrders.Read`, `Inventory.Read`, `Quality.Read`, `Procurement.Read`, `Routings.Read`, `Reports.Read`).

## Boundaries
- Reservations allocate existing free inventory (`AvailableBaseQuantity > 0`) only; negative available balance is never allowed (`CHECK OnHand >= Blocked + Reserved`).
- No cross-site reservation or transfer of reservations.
- All display strings in `fa-IR` and `en-US` `.resx` catalogs with zero inline literals.
