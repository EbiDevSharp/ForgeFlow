# Slice 05 — Test & Verification Plan (`05-item-catalog`)

Status: **PROPOSED (`05.0`)** (2026-11-10)

## 1. Domain & Application Unit Tests (`M01`–`M10`)

- **`M01` — Canonical codes, names & numeric bounds (`A03`, `A14`)**:
  - `Unit`, `Item`, `ClassificationSet`, `Classification`, `AttributeDefinition`, `ContextDimension`, and `Bom` codes normalize to uppercase ASCII (`1..32` chars) and reject invalid characters.
  - `UnitConversion` and `ItemUnitConversion` reject `Factor <= 0` and self-conversion (`FromUnitId == ToUnitId`).
  - `BomVersion` rejects `BaseOutputQuantity <= 0` or `EffectiveToDate < EffectiveFromDate`; `BomInput` rejects `Quantity <= 0` or `ScrapPercentage` outside `[0, 100)`; `BomExplosionRequest` rejects `RequestedQuantity <= 0` (`A14`).
- **`M02` — Chained multi-hop unit conversion (`A08`)**:
  - `UnitConversionGraph` resolves direct conversions, inverse conversions, multi-hop organization conversions (`TON -> KG -> G`), and hybrid paths combining `ItemUnitConversion` (`BOX -> BOTTLE` for a specific item) with organization `UnitConversion` (`BOTTLE -> ML -> L`), while rejecting disconnected or cyclic conflicting factors.
- **`M03` — Item capabilities & classification invariants**:
  - `Item` enforces at least one operational capability (`CanBePurchased`, `CanBeManufactured`, `CanBeSold`, `CanBeConsumed`) or explicit lifecycle validation, and rejects circular `Classification` parent chains.
- **`M04` — BOM Version lifecycle & immutability (`Day 8`)**:
  - `Draft` versions allow input/output/applicability edits.
  - Transitioning to `Approved` / `Active` requires at least one `MainProduct` output matching `Bom.PrimaryOutputItemId`, at least one `BomInput`, valid unit conversion paths, and zero direct self-consumption.
  - Approved, Active, Superseded, and Obsolete versions reject structural mutations (`Boms.VersionImmutable`).
- **`M05` — Deterministic BOM Resolver & Dominance (`Day 9`, `A07`, `A11`)**:
  - Filters candidate `Active` versions by `EffectiveFromDate <= ProductionDate <= EffectiveToDate` and `Applicability.IsActive == true` (`A11`).
  - Unrelated invalid versions outside the candidate date/context window do not abort resolution (`A07`).
  - Evaluates `Equals`, `NotEquals`, `In` (JSON array or CSV), and `NotIn`; missing context dimension never matches (`Change 5`).
  - Applies strict subset dominance (`Plant=Tehran AND Line=L1` dominates `Plant=Tehran`) and returns `Boms.AmbiguousResolution` when non-dominating candidates tie.
- **`M06` — Recursive BOM Explosion & Cycle Detection (`Day 7`, `Day 10`)**:
  - Scales inputs (including `ScrapPercentage`) and outputs (`MainProduct`, `CoProduct`, `ByProduct`, `Waste`) across multi-level sub-assemblies using resolved child BOM versions, converts units via `UnitConversionGraph` (`A08`), treats unmanufactured/BOM-less items as leaf materials, and detects direct or indirect `A -> B -> A` cycles (`Boms.CircularDependency`).
- **`M07` — Fail-closed authorization & cross-organization isolation (`A02`)**:
  - Missing actor, missing `Units.*` / `Items.*` / `Boms.*` permission, or cross-organization target ID returns `AccessDenied` or `NotFound` without leaking tenant existence.

## 2. Disposable SQL Server Integration Tests (`S01`–`S06`)

- **`S01` — Additive schema upgrade**: Upgrading from `20261110134703_RolesAndGranularPermissions` preserves all existing organizations, sites, accounts, roles, permissions, and audit history.
- **`S02` — Cross-organization composite FK & unique index enforcement**: Organization A cannot reference Organization B's `Unit`, `Item`, `Classification`, or `Bom` at SQL level; duplicate codes are permitted across distinct organizations and rejected within the same organization.
- **`S03` — Atomic business audit (`organization.OrganizationChange`)**: Creating or mutating a `Unit`, `Item`, or `BomVersion` writes a matching `OrganizationChange` row in the same transaction; simulated audit failure rolls back the entity write.
- **`S04` — Optimistic concurrency (`RowVersion`)**: Stale 8-byte `RowVersion` fails with `Conflict` and emits zero audit rows.

## 3. Localization, Static Scanner & Browser Verification (`B01`–`B04`)

- `ResourceContractTests` & `HardcodedDisplayTextScannerTests`: 100% key parity between `SharedResources.fa-IR.resx` and `SharedResources.en-US.resx` and zero hardcoded display literals across `.razor`, `.cs`, and `.mjs`.
- `node .\verification\slice01-ui\browser-check.cjs`: `4/4 PASS` (`fa-IR` / `en-US` at `1440px` and `320px`).
- Live UI verification on `/units`, `/items`, and `/boms`: `BitDataGrid` server-side paging (`10/25/50/100`), `BitDialog` modals with `ShowOkButton="false" ShowCancelButton="false"` and zero horizontal overflow on desktop and mobile.
