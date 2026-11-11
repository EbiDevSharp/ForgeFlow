# Slice 11 — Work Centers, Routings & Shop-Floor Operations (`11-work-centers-routings`)

## 1. Purpose
Introduce site-scoped **Work Centers** (`WorkCenter`), versioned **Routings** (`Routing` / `RoutingOperation`), and frozen **Production Order Operation** execution tracking (`ProductionOrderOperation`) integrated cleanly with Slice 06 Manufacturing Orders and Slice 05 Engineering Master Data.

## 2. Scope
- **Work Centers (`/work-centers`)**: Site-scoped physical/logical stations (`Machine`, `AssemblyLine`, `WorkCell`, `QualityStation`, `ExternalVendor`) with capacity per hour, daily available hours, optional hourly cost rate, and active status.
- **Routings (`/routings`)**: Site-scoped versioned operation sequences for manufacturable items (`Draft -> Active -> Obsolete`), optional `BomVersionId` reference, single active default routing per `(OrganizationId, SiteId, ItemId)`, setup/run/queue/wait minutes, and instructions.
- **Production Order Operation Snapshot (`/production-orders`)**: Optional `RoutingId` on `ProductionOrder` (auto-resolving active default routing when omitted on draft if one exists); freezing steps into `ProductionOrderOperation` (`Pending -> Ready -> InProgress -> Paused -> Completed / Skipped`) with shop-floor execution logging (actual setup/run minutes, completed/scrap quantities, notes) and work center load summary.

## 3. Security & Boundaries
- `PermissionModule.Routings = 15`: `Routings.Read`, `Routings.Manage`, `Operations.Execute` (`OrganizationOrSite` applicability).
- Strict tenant + site isolation enforced in `RoutingUseCases` and `SqlRoutingOperationsStore` with composite foreign keys `(OrganizationId, SiteId, Id)` and rowversion concurrency.
