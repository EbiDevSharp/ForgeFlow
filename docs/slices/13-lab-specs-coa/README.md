# Slice 13 — Dynamic Lab Parameters, Inspection Specifications & Lot COA

## Scope (`13-lab-specs-coa`)
Enable organizations with thousands of distinct items (polymers, chemicals, metals, additives, finished goods) to define reusable laboratory test parameters with custom scientific symbols and units (e.g., `MFI` in `g/10min`, `Purity` in `%`, `Viscosity` in `cP`, `TBC` in `ppm`), attach specification templates to **Item Classifications** or specific **Items**, auto-populate incoming/production **Quality Inspections**, evaluate pass/fail against `[Min, Max]` bounds, and surface **Lot Certificates of Analysis (COA)** to **Production** inside Production Orders, Inventory Balances, and Lot Traceability.

## Increments
- `13.0`: Slice design package (`README.md`, `decisions.md`, `physical-erd.md`, `test-plan.md`).
- `13.1`: Domain (`QualityParameter`, `QualitySpec`, `QualitySpecLine`, `QualityInspection`/`QualityInspectionLine` structured snapshots & numeric evaluation, `QualityErrors`).
- `13.2`: Application (`QualitySpecContracts.cs`, `QualitySpecUseCases.cs`, extended `QualityContracts.cs`/`QualityUseCases.cs`, `AuditEventCatalog.cs`).
- `13.3`: Infrastructure & EF Migration (`OrganizationModel`, `OrganizationDbContext`, `AuthDbContext`, `SqlQualitySpecStore`, auto-spec snapshot on inspection creation, Lot COA query, migration + `localhost/testforgflow` upgrade).
- `13.4`: Web UI & Localization (`Quality.razor` Lab Parameters/Specs tabs & structured measurements + concession guard, `Traceability.razor`, `ProductionOrders.razor`, `Inventory.razor`, `Items.razor`, `.resx`, `ResourceContractTests.cs`, `app.css`).
- `13.5`: Unit + SQL tests (`LabSpecTests.cs`, `LabSpecSqlTests.cs`, updated model/SQL table counts).
- `13.6`: Live UI review (`fa-IR`/`en-US`, Light/Dark, RTL/LTR, mobile `320px`) & acceptance.
