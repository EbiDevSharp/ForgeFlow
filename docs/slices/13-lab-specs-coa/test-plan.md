# Slice 13 Test Plan

- **`L01` (Unit — `LabSpecTests.cs`)**:
  - `QualityParameter`, `QualitySpec`, and `QualitySpecLine` enforce code/symbol formatting, `TargetKind` XOR invariants, `Min <= Max` bounds, and `DataType` precision (`0..6`).
  - `QualityInspectionLine` evaluates numeric `[Min, Max]` pass/fail inclusively (and single-sided bounds).
  - `QualityInspection.RecordDisposition` enforces concession note/defect code when accepting (`acceptQty > 0`) while any line has `IsPassed == false`.
  - `QualitySpecUseCases.GetLotCertificate` authorizes `Quality.Read`, `Inventory.Read`, or `ProductionOrders.Read` and rejects unauthorized actors.
- **`L02` (SQL Integration — `LabSpecSqlTests.cs`)**:
  - End-to-end **Styrene Monomer** flow: Classification `MONOMER` + Parameters `MFI (g/10min)` / `PURITY (%)` + Spec attached to `MONOMER` -> `PurchaseOrder` Goods Receipt creates Lot `LOT-SM-01` & auto-populates `QualityInspectionLine` rows from Classification inheritance -> Lab enters `MFI = 12.85`, `PURITY = 99.85`, verifies auto `IsPassed = true`, accepts lot -> Production Order reserves `LOT-SM-01` & reads exact COA values via `GetLotCertificate`.
  - Verifies **Item-level override Spec** supersedes Classification spec and out-of-spec acceptance requires concession note while persisting exact numeric reading for Production.
- **Regression & Contracts**:
  - Updated `OrganizationPersistenceModelTests` (`42` entity types, `12` migrations) & `OrganizationSqlServerTests` (`42` tables, `12` migrations).
  - Full non-SQL + HTTP test suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`), `ResourceContractTests`, `HardcodedDisplayTextScannerTests`, `drafts.test.mjs`, and `browser-check.cjs`.
