# Slice 05 — Units of Measure, Item Catalog & Manufacturing BOM (`05-item-catalog`)

Status: **ACCEPTED (`05.5`)** (2026-11-10) — Establishes the organization-scoped Units of Measure, Item Master (with hierarchical Classifications, Attribute definitions/values, and operational Capabilities), and Manufacturing BOM (with Version lifecycle, Context Applicability, deterministic Resolver, and recursive Explosion) per [ForgeFlow.md](../../../ForgeFlow.md) `۰۰.۴` row `06`.

## 1. Objective, actors, preconditions, outputs, and out-of-scope

### Roadmap alignment
Per [ForgeFlow.md](../../../ForgeFlow.md) `۰۰.۴` row `06`, `FF-A002`, `FF-A006`, `FF-A007`, and the approved Slice 01 Ownership Matrix ([decisions.md](../01-organization-sites/decisions.md) `Q1`–`Q3`), Slice 05 upgrades the core master-data and manufacturing definition domain to the greenfield security, multi-organization ownership, optimistic concurrency (`RowVersion`), atomic business audit (`organization.OrganizationChange`), and bilingual (`fa-IR` / `en-US`) `Bit.BlazorUI` 10.7.0 architecture established in Slices `00` through `04`.

### Phased sub-slices for high UI & engineering quality
Because row `06` spans three rich functional areas and the user explicitly requested extra polish on `BitDataGrid`, `BitDialog` modals, and zero-hardcoded bilingual UI, Slice 05 is structured into three sequential increments:
- **Sub-slice `05A` — Units of Measure & Unit Conversions (`/units`)**:
  - Shipped immutable unit dimensions (`Mass`, `Volume`, `Length`, `Area`, `Count`, `Time`) + organization-owned `Unit` master and directed `UnitConversion` graph with BFS multi-hop conversion resolution.
- **Sub-slice `05B` — Item Catalog, Classifications, Capabilities & Attributes (`/items`)**:
  - Universal `Item` master (`Code`, `Name`, `BaseUnitId`, operational capabilities `CanBePurchased`, `CanBeManufactured`, `CanBeSold`, `CanBeConsumed`, `IsActive`), hierarchical `ClassificationSet` / `Classification`, item-specific `ItemUnitConversion` (integrated with global `UnitConversion` in multi-hop resolution — closing `A08`), and extensible `AttributeDefinition` / `ItemAttributeValue`.
- **Sub-slice `05C` — Manufacturing BOM, Versions, Applicability, Resolver & Explosion (`/boms`)**:
  - `Bom` header (`Code`, `Name`, `PrimaryOutputItemId`), `BomVersion` (`VersionNumber`, `Status`: `Draft` / `Approved` / `Active` / `Superseded` / `Obsolete`, `EffectiveFromDate`, `EffectiveToDate`, `BaseOutputQuantity`, `BaseOutputUnitId`), `BomInput`, `BomOutput` (`MainProduct`, `CoProduct`, `ByProduct`, `Waste`), `ContextDimension` & `BomApplicability` / `BomApplicabilityCondition`, deterministic `BomResolver` (closing `A07` and `A11`), and cycle-safe recursive `BomExplosion` (closing `A14`).

### Actors and trusted provenance
- **Organization administrator (`RoleId = 1`)**: automatically holds full read/write permissions for `Units`, `Items`, and `Boms` within their authorized organization(s), and can delegate granular permissions (`Units.Read`, `Units.Manage`, `Items.Read`, `Items.Manage`, `Boms.Read`, `Boms.Manage`) to custom organization roles or direct user grants via `/roles` and `/users`.
- **Site-scoped operator / planner**: can read organization-shared `Units`, `Items`, and `Boms` when granted the corresponding `.Read` permission, and can run BOM resolution/explosion for their authorized `Site` context (`PLANT = Site.Code`).
- **Provenance**: every read and write resolves the caller via `ITrustedActorAccessor` and verifies exact scope via `IOrganizationPermissionEvaluator`.

### Preconditions
- Slices `01` (`Organization` / `Site`), `02` (`Users`), `03` (`Roles & Granular Permissions`), and `04` (`Audit Viewer`) are implemented and pushed.
- Existing `organization.OrganizationChange` append-only business audit table and `auth` permission infrastructure are active.

### Out-of-scope
- Procurement (Purchase Orders), Sales Orders, Inventory balances/warehouses/lots, Production Orders, Shop-floor Routing, and Costing / Accounting (deferred to later roadmap modules).

## 2. Historical audit defects explicitly closed in Slice 05 (`ForgeFlow.md` `FF-A002`)

| Finding ID | Historical defect | Slice 05 resolution |
|---|---|---|
| `A02` | Hardcoded `"system"` actor strings and missing authorization on Item/BOM writes | Every use case requires `ITrustedActorAccessor` + `IOrganizationPermissionEvaluator` and records `ActorUserId` (`long`) in `organization.OrganizationChange` |
| `A03` | Domain/EF constraint drift (`Factor > 0`, `Quantity > 0`, `ScrapPercentage` bounds, date ranges) | Matching Domain invariants AND explicit SQL `CHECK` constraints in EF Core configuration |
| `A04` | Scoped `DbContext` concurrency hazard in Blazor Server circuits | All stores use `IDbContextFactory<OrganizationDbContext>` per operation (`FF-A007`) |
| `A07` | Unrelated invalid `BomVersion` outside the requested date/context aborted `BomResolver` | Resolver filters candidate active versions by `EffectiveFromDate`/`EffectiveToDate` and `BomApplicability` first; Structural validation runs on matching candidates and during version approval/activation |
| `A08` | Multi-hop unit conversion ignored chained `ItemUnitConversion` + organization `UnitConversion` | `UnitConversionGraph` builds a unified graph of organization `UnitConversion` edges + the specific item's `ItemUnitConversion` edges (`18,6` decimal precision) |
| `A11` | `BomResolver` accepted inactive `BomApplicability` records | Resolver and validator strictly require `Applicability.IsActive == true` and all referenced `ContextDimension.IsActive == true` |
| `A14` | `BomExplosionRequest` accepted `RequestedQuantity <= 0` | Domain & Application reject `RequestedQuantity <= 0` with `Boms.QuantityInvalid` before querying |

## 3. Operation x Permission x Scope matrix

| Module | Permission code | Scope applicability | Covered operations |
|---|---|---|---|
| Units (`PermissionModule.Units = 6`) | `Units.Read` | `OrganizationOrSite` | List/get units, unit dimensions, and unit conversions; preview conversion factor |
| Units (`PermissionModule.Units = 6`) | `Units.Manage` | `OrganizationOnly` | Create/update/activate/deactivate organization units and directed unit conversions; seed standard units for an organization |
| Items (`PermissionModule.Items = 7`) | `Items.Read` | `OrganizationOrSite` | List/get items, classifications, item capabilities, item unit conversions, and item attributes |
| Items (`PermissionModule.Items = 7`) | `Items.Manage` | `OrganizationOnly` | Create/update/activate/deactivate items, classification sets/nodes, attribute definitions, item unit conversions, and item attribute values |
| BOM (`PermissionModule.Boms = 8`) | `Boms.Read` | `OrganizationOrSite` | List/get BOMs, BOM versions, inputs/outputs, applicability rules; execute deterministic BOM Resolver and multi-level BOM Explosion |
| BOM (`PermissionModule.Boms = 8`) | `Boms.Manage` | `OrganizationOnly` | Create/update BOM headers, draft BOM versions, inputs/outputs, context dimensions, applicability conditions, and transition version status (`Draft` -> `Approved` -> `Active` -> `Superseded` / `Obsolete`) |

## 4. UI & Localization standards (per user guidance)

1. **Bit BlazorUI 10.7.0 components**:
   - Every list view uses `BitDataGrid` inside `<div class="read-grid-card"><div class="read-grid">` with server-side pagination (`Pageable="true"`, `PageSize="25"`, `PageSizeOptions="[10, 25, 50, 100]"`, `Strings="@GridStrings"`, `SelectionMode="BitSelectionMode.Single"`, `WrapCellText="true"`), paired with `<div class="read-toolbar-card">`.
   - Every view/create/edit modal uses `BitDialog` with `Width="580px"` (or `680px` for master-detail BOM version dialogs), `MaxWidth="calc(100vw - 24px)"`, `Classes="@DialogClasses"`, `ShowOkButton="false"`, `ShowCancelButton="false"`, `ShowCloseButton="true"`, `AutoFocus="true"`, `TrapFocus="true"`, `RestoreFocus="true"`, `CloseOnEscape="true"`, and clean inner padding so built-in `Ok`/`Cancel` buttons never appear and desktop modals never scroll horizontally.
2. **Zero hardcoded text**:
   - All labels, titles, columns, badges, buttons, empty states, validation errors, and confirmation prompts live in `SharedResources.fa-IR.resx` and `SharedResources.en-US.resx` and are verified by `ResourceContractTests` and `HardcodedDisplayTextScannerTests`.
3. **Navigation**:
   - Top-level or grouped navigation items in `MainLayout.razor` appear dynamically only when the signed-in user holds the corresponding `.Read` permission in at least one scope; any parent menu group with zero accessible child items is hidden automatically.

## 5. Separate status and next step

| Track | Status | Notes |
|---|---|---|
| Design (`05.0`) | APPROVED | `Q-M01`–`Q-M04` approved in [decisions.md](decisions.md) |
| Sub-slice `05A` (Units & Conversions) | ACCEPTED (`05.5`) | Domain, Application, `20261110164056_UnitsAndConversions` EF migration, `UnitOperationsStore`, `/units` UI (`BitDataGrid` + `BitDialog`), `fa-IR`/`en-US` `.resx`, `UnitTests`, `UnitSqlTests` |
| Sub-slice `05B` (Item Catalog & Classifications) | ACCEPTED (`05.5`) | Domain (`Item`, `ClassificationSet`, `Classification`, `ItemUnitConversion`, `AttributeDefinition`, `ItemAttributeValue`), `ItemUseCases` + hybrid `A08` conversion resolver, `20261110171954_ItemsClassificationsAttributes` migration, `/items` UI (`BitDataGrid` + `BitDialog`), `ItemTests`, `ItemSqlTests` |
| Sub-slice `05C` (BOM, Resolver & Explosion) | ACCEPTED (`05.5`) | Domain (`Bom`, `BomVersion`, `BomInput`, `BomOutput`, `ContextDimension`, `BomApplicability`, `BomApplicabilityCondition`, `BomResolutionEngine`), `BomUseCases` (deterministic resolver + recursive `A08`/`A14` explosion), `BomOperationsStore`, `20261110173434_ManufacturingBomsAndApplicability` migration, `/boms` UI (`BitDataGrid` + `BitDialog` + `BitDatePicker` + simulator), `BomTests`, `BomSqlTests` |
| Verification & Acceptance (`05.5`) | ACCEPTED (2026-11-10) | Release build `0W/0E`; `344` non-SQL pass; user SQL run `9/9 PASS` (`UnitSqlTests|ItemSqlTests|BomSqlTests|OrganizationSqlServerTests`, `6.5s`); `19/19` drafts JS; `4/4` `browser-check.cjs`; user live verification of `testforgflow`, `/boms` (`BitDatePicker` Jalali/Gregorian, localized context dimensions), and redesigned header `UserMenu` |
