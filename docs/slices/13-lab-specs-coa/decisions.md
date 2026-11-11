# Slice 13 Architecture Decisions (`L-Q01`–`L-Q04`)

Approved by user via `lab_specs_coa_plan.md` (2026-11-11).

- **`L-Q01` Out-of-Spec Acceptance Policy (Concession):**
  When any inspection line has `IsPassed = false` (including values outside `[Min, Max]`), `RecordDisposition` allows accepting (`acceptQty > 0m`) only with an explicit concession reason (`DefectReasonCode` or `Notes` provided), while displaying a clear out-of-spec warning in the Lab workbench. Existing tests with empty lines or valid concession notes remain compatible.
- **`L-Q02` Spec Inheritance for Thousands of Items:**
  Resolution priority when auto-populating a `QualityInspection` (`Incoming`, `Production`, `Manual`):
  1. Active `QualitySpec` targeting `(OrganizationId, ItemId, Checkpoint)` or `All` checkpoint (`Item` beats `All` if exact checkpoint exists).
  2. Otherwise, active `QualitySpec` targeting `(OrganizationId, ClassificationId, Checkpoint)` walking `Item.ClassificationId` and its ancestors up to root.
  Plus an explicit **"Load Spec Template"** (`ApplySpecTemplate`) action on open inspections.
- **`L-Q03` Production Read Access to Lot COA:**
  Reuses existing permissions without adding new permission flags: `Quality.Manage` controls parameters/specs/measurements, while `Quality.Read`, `Inventory.Read`, or `ProductionOrders.Read` grants read-only access to `GetLotCertificate` / `GetItemSpecSummary` within the actor's authorized organization/site scope.
- **`L-Q04` Scientific Units on `QualityParameter`:**
  Store normalized display `UnitSymbol` (e.g. `g/10min`, `ppm`, `cP`, `%wt`, `APHA`, `°C`) and `Symbol` (e.g. `MFI`, `η`, `ρ`) directly on `QualityParameter` rather than forcing lab dimensions into `UnitDimension` (Mass/Volume/Length/Area/Count/Time).
