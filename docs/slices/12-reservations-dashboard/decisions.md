# Slice 12 Architecture Decisions (`R-Q01`–`R-Q05`) — APPROVED

| ID | Question | Approved Decision |
|---|---|---|
| **R-Q01** | Where is reserved quantity stored? | `InventoryBalance.ReservedBaseQuantity` (`decimal(18,6) >= 0`), making `AvailableBaseQuantity = OnHandBaseQuantity - BlockedBaseQuantity - ReservedBaseQuantity` enforced by `CK_InventoryBalance_Quantities`. |
| **R-Q02** | How do reservations interact with `ProductionIssue`, `CompleteOrder`, and `CancelOrder`? | Explicit or FIFO auto-selected creation on `Released`/`InProgress` Production Orders; `ProductionIssue` automatically consumes active matching `(Warehouse, Item, LotCode)` reservations first; `CompleteOrder` and `CancelOrder` automatically release any remaining active reservations. |
| **R-Q03** | Should `MaterialShortageReport` reflect reserved stock? | Yes: `MaterialShortageItemSummary.ReservedBaseQuantity` is reported, and `AvailableBaseQuantity` deducts both `BlockedBaseQuantity` and `ReservedBaseQuantity`. |
| **R-Q04** | Where is the Operations Dashboard located? | Directly on **`/` (`Home.razor`)** with an organization/site Scope Switcher and quick navigation links into operational modules. |
| **R-Q05** | How is Dashboard visibility governed? | Permission-filtered per card/queue using existing `*.Read` permissions (`ProductionOrders.Read`, `Inventory.Read`, `Quality.Read`, `Procurement.Read`, `Routings.Read`, `Reports.Read`) in the user's active scope. |
