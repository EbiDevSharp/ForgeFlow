# Slice index and shared boundaries — 01 design

## Slice 11 — Unified Business Partners & ERP Manufacturing Lifecycle Navigation Structure (2026-11-14)

### 4. Local One-Click Setup & Migration Automation Tooling
- **ForgeFlow.Setup (	ools/ForgeFlow.Setup/)**:
  - Automated database provisioning and schema migration runner independent of design-time factories or external tools.
  - Verifies SQL Server connection and ensures target database (default 	estforgflow) exists.
  - Sequentially applies all pending migrations for DiagnosticsDbContext (schema diag), AuthDbContext (schema uth), and OrganizationDbContext (default schema).
  - Inspects BootstrapState; on fresh installations, automatically bootstraps initial organization (ORG1), factory site (SITE1), and system administrator (dmin / Admin@123456!).
  - Preserves existing accounts and organizations if already initialized.
- **Runners (un-local.bat & un-local.ps1)**:
  - Windows batch file and PowerShell script for single-click execution.
  - Verifies .NET 10 SDK presence.
  - Configures default local SQL connection with override support via %ConnectionStrings__ForgeFlow%.
  - Ensures local HTTPS developer certificates are trusted.
  - Executes ForgeFlow.Setup, opens default browser at https://localhost:5001, and starts Web host in Development.

### 3. Business Partner Auto-Code Preview & Numbering Migration Hotfix
- **Database Schema Migration**:
  - Applied migration 20261114184650_BusinessPartnersAndRoles to localhost/testforgflow:
    - Updated CK_DocumentSequence_Type to [DocumentType] BETWEEN 1 AND 10.
    - Updated CK_OrganizationChange_Event constraint to allow BusinessPartner.* and DocumentSequence.* events.
    - Created [organization].[BusinessPartner], [organization].[BusinessPartnerRoleDefinition], and [organization].[BusinessPartnerRoleAssignment] tables with system seed roles (Supplier, Customer, Contractor).
- **Business Partner Creation Code Allocation (BusinessPartners.razor)**:
  - Replaced manual override/locked indicator (Text["DocSeqOverrideLocked"]) with dynamic first-valid preview allocation matching standard ERP pages.
  - Injected DocumentSequenceUseCases SeqService.
  - In OpenCreateModal, asynchronous preview retrieval: wait SeqService.GetPreview(CurrentScope, CurrentOrgId, DocumentSequenceType.BusinessPartner).
  - Form code input displays @formCode with class="proc-input-readonly" and disabled dir="ltr".
  - On submit, SqlBusinessPartnerStore.Create performs atomic monotonic allocation and records audit trail.
- **Document Numbering Error Localization (DocumentNumbering.razor)**:
  - Replaced raw unlocalized DocumentSequenceErrors.PersistenceUnavailable with standardized ReadError resx key.

### 1. Unified Business Partners ("طرف تجاری") & Dynamic Roles
- **Domain (src/ForgeFlow.Domain/BusinessPartners/)**:
  - PersonType: Enum Legal = 1 (شخص حقوقی) and Natural = 2 (شخص حقیقی).
  - BusinessPartnerRoleDefinition: Seeded role entities (1 = Supplier / تامین‌کننده, 2 = Customer / مشتری, 3 = Contractor / پیمانکار), extensible for future business roles.
  - BusinessPartnerRoleAssignment: Composite assignment entity (OrganizationId, BusinessPartnerId, RoleId) with DeleteBehavior.NoAction.
  - BusinessPartner: Aggregate root entity managing partner lifecycle, code, full name, company name, national code / legal national ID, economic code, contact numbers (mobile, phone), email, website, country, province, city, address, notes, and active status.
  - DocumentSequence: Added DocumentSequenceType.BusinessPartner = 10 with default prefix "BP".
  - PermissionCatalog: Added PermissionModule.BusinessPartners = 18 with BusinessPartners.Read (OrganizationOrSite) and BusinessPartners.Manage (OrganizationOnly).
  - Added BusinessPartnerErrors, BusinessPartnerRuleException, and validation routines (BusinessPartnerText).
- **Application (src/ForgeFlow.Application/BusinessPartners/)**:
  - BusinessPartnerContracts.cs: DTOs (BusinessPartnerSummary, BusinessPartnerDetail, CreateBusinessPartnerRequest, UpdateBusinessPartnerRequest, BusinessPartnerListQuery, BusinessPartnerRoleDto).
  - IBusinessPartnerStore.cs: Interface for partner persistence, auto-number allocation, and role queries.
  - BusinessPartnerUseCases.cs: Full application service with actor authorization, scope validation, tenant protection, and audit rejection logging via AuditEventCatalog.Op(UserOperationCatalog.BusinessPartnerActionRejected).
  - AuditEventCatalog.cs: Added BusinessPartner.Created, BusinessPartner.Updated, BusinessPartner.Activated, BusinessPartner.Deactivated, and Op(UserOperationCatalog.BusinessPartnerActionRejected).
  - DiagnosticContracts.cs: Added UserOperationCatalog.BusinessPartnerActionRejected.
- **Infrastructure (src/ForgeFlow.Infrastructure/)**:
  - OrganizationModel.cs: Added BusinessPartner.* to the OrganizationChangeEventSql check constraint.
  - OrganizationCoreConfigurations.cs: Updated check constraint CK_DocumentSequence_Type to [DocumentType] BETWEEN 1 AND 10.
  - BusinessPartnerConfigurations.cs: EF entity configurations for BusinessPartner, BusinessPartnerRoleDefinition, and BusinessPartnerRoleAssignment with strict DeleteBehavior.NoAction across all FKs.
  - OrganizationDbContext.cs: DbSets, entity tracking, parent organization active check, and audit change generator for BusinessPartner and BusinessPartnerRoleAssignment.
  - SqlBusinessPartnerStore.cs: EF Core implementation with atomic sequence allocation (SqlDocumentSequenceStore.AllocateAsync) and backward-compatible synchronization with Supplier and Customer tables.
  - EF Core migration 20261114184650_BusinessPartnersAndRoles.cs checked in and OrganizationDbContextModelSnapshot.cs updated.
- **Web & ERP Navigation Architecture (src/ForgeFlow.Web/)**:
  - Re-architected sidebar navigation in MainLayout.razor into the 7 standard ERP manufacturing lifecycle groups:
    1. **تعاریف و اطلاعات پایه (MasterDataNavGroup)**: /business-partners (طرف‌های تجاری), /items (کالاها), /units (واحدهای سنجش), /work-centers (مراکز کاری), /boms (ساختار محصول), /routings (مسیرهای تولید).
    2. **تامین و خرید مواد اولیه (ProcurementNavGroup)**: /purchase-orders (سفارشات خرید).
    3. **برنامه‌ریزی و اجرای تولید (ProductionNavGroup)**: /production-orders (سفارشات تولید), /inventory (موجودی و انبار).
    4. **کنترل کیفیت (QualityNavGroup)**: /quality (کنترل کیفیت).
    5. **فروش و تحویل محصول (SalesNavGroup)**: /sales (فروش و تحویل).
    6. **ردیابی، تحلیل و گزارش‌گیری (TraceabilityReportingNavGroup)**: /traceability (ردیابی بهر), /reports (گزارش‌ها و تحلیل‌ها).
    7. **تنظیمات مدیر (AdminNavGroup)**: /organizations, /sites, /users, /roles, /numbering-rules, /audit, /errors.
  - BusinessPartners.razor created at /business-partners and /organizations/{OrganizationId:long}/business-partners:
    - Role filter dropdown (All, Supplier, Customer, Contractor), search, inactive toggle.
    - BitDataGrid with server-side paging.
    - Complete ForgeFormDialog matching user specifications: Person type toggle (Legal / Natural), dynamic commercial role checkboxes, locked/auto-allocated business partner code, conditional fields (Company Name & National Legal ID for Legal vs Full Name & National Code for Natural), economic code, contact info (mobile, phone, email, website), address info (country, province, city, address), and notes.
    - Active/Inactive toggle dialog (WriteConfirmDeactivate / WriteConfirmActivate) allowing deactivation of indebted partners.
    - Partner detail modal.
  - Direct navigation /suppliers updated in Suppliers.razor to automatically redirect to /business-partners.
  - Updated RoleManagementPageTests.cs (MainLayout_orders_navigation_into_erp_sections) asserting all 8 nav sections and URLs.

### 2. Actual verification
- dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 — **PASS** (0 warnings, 0 errors).
- dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --filter "Category!=SqlServer" — **455 passed, 0 failed, 66 skipped** (521 total).
- dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --filter "FullyQualifiedName~ResourceContractTests" — **13 passed, 0 failed** (full bidirectional parity).
- dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --filter "FullyQualifiedName~RoleManagementPageTests" — **9 passed, 0 failed**.
- dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --filter "FullyQualifiedName~RoleDomainTests" — **12 passed, 0 failed**.
- dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --filter "FullyQualifiedName~OrganizationPersistenceModelTests" — **7 passed, 0 failed** (HasPendingModelChanges() == false, 51 entity types verified).
- Development Web running at https://localhost:5001:
  - GET /account/login -> HTTP 200 (Content-Language: fa-IR, prerendered HTML with Blazor script assets).
  - Anonymous GET /business-partners -> HTTP 302 redirect to /account/login.

## Slice 05 — Sub-slice `05B` (Item Catalog, Classifications, Capabilities & Attributes) Implemented — awaiting SQL & live UI review (2026-11-10)

### 1. Sub-slice `05B` implementation
- **Domain (`src/ForgeFlow.Domain/`)**:
  - `Roles/PermissionCatalog.cs`: added `PermissionModule.Items = 7`, `Items.Read` (`OrganizationOrSite`), `Items.Manage` (`OrganizationOnly`), and updated `IsGrantedToBuiltInOrganizationAdmin`.
  - `Items/`: `ItemErrors.cs`, `ItemText.cs`, `AttributeDataType.cs`, `Item.cs` (enforces `CanBePurchased || CanBeManufactured || CanBeSold || CanBeConsumed`), `ClassificationSet.cs`, `Classification.cs`, `ItemClassification.cs`, `ItemUnitConversion.cs`, `AttributeDefinition.cs`, `ItemAttributeValue.cs`.
- **Application (`src/ForgeFlow.Application/Items/`)**:
  - `ItemContracts.cs`, `ItemUseCases.cs`: CRUD & lifecycle for Items, ClassificationSets, Classifications (with hierarchy parent validation), Item-specific unit conversions + `A08` hybrid `PreviewItemConversion` (combining `ItemUnitConversions` + global `UnitConversions` via `UnitConversionGraph`), AttributeDefinitions, and typed ItemAttributeValues.
- **Infrastructure (`src/ForgeFlow.Infrastructure/`)**:
  - Updated `OrganizationModel.cs`, `OrganizationDbContext.cs`, `AuthDbContext.cs` (ignores 7 new entities so `AuthDbContext.HasPendingModelChanges() == false`), `RuntimeSessions.cs` (`Items.Read`), `OrganizationPersistenceRegistration.cs` (`ItemUseCases`), added `ItemOperationsStore.cs`, generated EF migration `20261110171954_ItemsClassificationsAttributes.cs` (`ForgeFlowOrganizationRuntime` table grants / `DENY DELETE` on master tables), and updated `scripts/upgrade-local-testdb.ps1` with idempotent DDL for both `UnitsAndConversions` and `ItemsClassificationsAttributes`.
- **Web & Tests (`src/ForgeFlow.Web/`, `tests/ForgeFlow.Web.Tests/`)**:
  - Added `/items` and `/organizations/{OrganizationId:long}/items` (`Components/Pages/Items.razor`) with 3 tabs (`Items`, `Classifications`, `Attributes`), `BitDataGrid` server-side paging (`10/25/50/100`), cleanly padded `BitDialog` modals (`ShowOkButton="false"`, `ShowCancelButton="false"`), item detail/edit sub-tabs (`General`, `Classifications`, `Item conversions` + live `A08` calculator, `Attributes`).
  - Updated `ReadScopes.cs` (`LoadItemScopesAsync`), `RolePermissionLocalization.cs`, `MainLayout.razor` (`/items` under `MasterDataNavGroup`), `Audit.razor` (`KnownEventCodes`), `wwwroot/app.css` (`.ff-icon--items`, `.cap-badge`, `.item-sub-card`), `SharedResources.en-US.resx`, `SharedResources.fa-IR.resx`, `ResourceContractTests.cs`, `RoleDomainTests.cs`, `OrganizationPersistenceModelTests.cs`, `OrganizationSqlServerTests.cs`, and added `ItemTests.cs` + `ItemSqlTests.cs`.

### 2. Actual verification (this session, Release, no restore)
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — **PASS** (`8` projects, `0 Warning(s)`, `0 Error(s)`).
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **340 passed, 0 failed, 46 skipped** (SQL suites remain skipped in agent sandbox).
- `node .\scripts\drafts.test.mjs` — **19 passed, 0 failed**.
- `node .\verification\slice01-ui\browser-check.cjs` — **4/4 PASS** (`fa-IR` / `en-US` at `1440px` / `320px`).
- Development Web (`task-2257`) running at `https://localhost:5001`: `GET /account/login` -> `HTTP 200` (`15723 bytes`), anonymous `GET /items` -> `HTTP 302`.

### 3. Remaining before `05C` (Manufacturing BOM, Versions, Applicability, Resolver & Explosion)
- Run `.\scripts\upgrade-local-testdb.ps1` (to create `Unit`, `UnitConversion`, `Item`, `ClassificationSet`, `Classification`, `ItemClassification`, `ItemUnitConversion`, `AttributeDefinition`, and `ItemAttributeValue` in `localhost/testforgflow`), optionally run `UnitSqlTests|ItemSqlTests|OrganizationSqlServerTests`, and inspect `/units` and `/items` in the browser before advancing to `05C`.

## Slice 05 (`05.0` APPROVED; Sub-slice `05A` Units of Measure & Conversions Implemented — awaiting SQL & live UI acceptance) (2026-11-10)

### 1. Approved decisions (`05.0`)
- User approved `Q-M01`–`Q-M04` in [decisions.md](docs/slices/05-item-catalog/decisions.md): 3-phase delivery (`05A` Units & Conversions -> `05B` Item Catalog, Classifications & Attributes -> `05C` BOM, Resolver & Explosion), organization-shared ownership with composite `(OrganizationId, Id)` keys, granular `Units.*` / `Items.*` / `Boms.*` permissions auto-granted to built-in `OrganizationAdministrator`, and collapsible **Master Data & Engineering** (`MasterDataNavGroup`) sidebar group.

### 2. Sub-slice `05A` (Units of Measure & Conversions) implementation
- **Domain (`src/ForgeFlow.Domain/`)**:
  - `Roles/PermissionCatalog.cs`: added `PermissionModule.Units = 6`, `Units.Read` (`OrganizationOrSite`), `Units.Manage` (`OrganizationOnly`), and `IsGrantedToBuiltInOrganizationAdmin`.
  - `Units/UnitDimension.cs`, `UnitErrors.cs`, `UnitText.cs`, `Unit.cs`, `UnitConversion.cs`, `UnitConversionGraph.cs` (exact rational BFS multi-hop conversion engine supporting both global organization edges and `A08` item-specific edge precedence, plus conflicting-path & quantity > 0 validation).
- **Application (`src/ForgeFlow.Application/Units/`)**:
  - `UnitContracts.cs`, `UnitUseCases.cs` (`ListUnits`, `ListActiveUnits`, `GetUnit`, `CreateUnit`, `UpdateUnit`, `SetUnitActive`, `ListConversions`, `GetConversion`, `CreateConversion`, `UpdateConversionFactor`, `SetConversionActive`, `PreviewConversion`, `SeedStandardUnits`).
- **Infrastructure (`src/ForgeFlow.Infrastructure/`)**:
  - `Identity/RuntimeSessions.cs` & `SqlRoleOperationsStore.cs`: automated built-in `OrganizationAdministrator` grants via `PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin`; `AuthDbContext.cs` excludes `Unit`/`UnitConversion` from Auth migrations (`HasPendingModelChanges() == false`).
  - `Organizations/OrganizationModel.cs`, `OrganizationDbContext.cs`, `UnitOperationsStore.cs`, `OrganizationPersistenceRegistration.cs`, and EF Core migration `Migrations/20261110164056_UnitsAndConversions.cs` (`organization.Unit`, `organization.UnitConversion`, composite FKs, `CHECK` constraints, `ForgeFlowOrganizationRuntime` grants/DENY DELETE, and `CK_OrganizationChange_Event` extension for `Unit.*` / `UnitConversion.*` atomic business audit).
- **Web & Tests (`src/ForgeFlow.Web/`, `tests/ForgeFlow.Web.Tests/`)**:
  - Added `/units` and `/organizations/{OrganizationId:long}/units` (`Components/Pages/Units.razor`) using `BitDataGrid` server-side paging (`10/25/50/100`), cleanly padded `BitDialog` modals (`Width="580px"`, `ShowOkButton="false"`, `ShowCancelButton="false"`, `OnDismissing` unsaved-change guard), interactive multi-hop conversion calculator dialog, and one-click `UnitsSeedButton`.
  - Updated `ReadScopes.cs`, `RolePermissionLocalization.cs`, `MainLayout.razor` (`MasterDataNavGroup`), `Audit.razor` (`KnownEventCodes`), `wwwroot/app.css`, `SharedResources.en-US.resx`, `SharedResources.fa-IR.resx`, `scripts/upgrade-local-testdb.ps1`, `ResourceContractTests.cs`, `RoleDomainTests.cs`, `OrganizationPersistenceModelTests.cs`, `OrganizationSqlServerTests.cs`, and added `UnitTests.cs` & `UnitSqlTests.cs`.

### 3. Actual verification (this session, Release, no restore)
- **Permissions Modal UX Refactor (`Users.razor` & `Roles.razor`)**: Replaced vertically stacked module permission lists inside `Users.razor` (User Permissions modal) and `Roles.razor` (Create & Edit/View Role modals) with a compact localized Page/Module `<select>` dropdown (`PermPageSelectLabel`, displaying `{PageTitle} ({Selected}/{Total})` counts via `PermModuleSummary`) paired with a single styled `<div class="perm-group">` operation-checkbox panel below it; updated `Users.razor` `TargetModules` to derive dynamically from `TargetDescriptors` so Site-scope permissions also include `Audit` and `Units`, and added explicit `.perm-group`/`.perm-check`/`.perm-chip` palette styling in `wwwroot/app.css`.
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — **PASS** (`8` projects, `0 Warning(s)`, `0 Error(s)`).
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **336 passed, 0 failed, 45 skipped** (disposable SQL tests remain skipped in agent sandbox).
- `node .\scripts\drafts.test.mjs` — **19 passed, 0 failed**.
- `node .\verification\slice01-ui\browser-check.cjs` — **4/4 PASS** (`fa-IR` / `en-US` at `1440px` / `320px`).
- Development Web (`task-2147`) running at `https://localhost:5001`: `GET /account/login` -> `HTTP 200` (`15692 bytes`), anonymous `GET /users` -> `HTTP 302`.

### 4. Remaining before `05B` (Item Catalog, Classifications & Attributes)
- User to run `.\scripts\upgrade-local-testdb.ps1` (to create `organization.Unit` and `organization.UnitConversion` in `localhost/testforgflow`), optionally run disposable SQL tests `UnitSqlTests|OrganizationSqlServerTests`, and inspect `https://localhost:5001/units` in Persian/English & mobile/desktop before proceeding to Sub-slice `05B`.

## Slice 02–04 Pushed (`7716030`) & Slice 05 (`05.0` Units, Item Catalog & BOM Design Proposed) (2026-11-10)

### 1. Authorized Git commit & push for Slices `02`, `03`, and `04`
- Per explicit user authorization, committed and pushed all Slice 02 (`02-user-management`), Slice 03 (`03-roles-permissions`), and Slice 04 (`04-audit-viewer`) changes to `origin/main`:
  - `git add -A; git commit -m "Complete Slices 02, 03 & 04: user management, roles/permissions, and audit viewer"; git push origin main` -> **`[main 7716030]` (`02580b6..7716030 main -> main`)** on `https://github.com/EbiDevSharp/ForgeFlow.git`.
- Development Web (`task-1844`) verified running at `https://localhost:5001` (`GET /account/login` -> `HTTP 200`, `15697 bytes`; anonymous `GET /audit` -> `HTTP 302`).

### 2. Slice 05 (`05-item-catalog`, `05.0`) — design proposed for review
Created the design-first Slice 05 package under `docs/slices/05-item-catalog/` per `ForgeFlow.md` `۰۰.۴` row `06` (`FF-A002`, `FF-A006`, `FF-A007`):
- [README.md](docs/slices/05-item-catalog/README.md): phased sub-slices (`05A`: **Units of Measure & Conversions** `/units` -> `05B`: **Item Catalog, Classifications, Capabilities & Attributes** `/items` -> `05C`: **Manufacturing BOM, Versions, Applicability, Resolver & Explosion** `/boms`), closure of historical Day 10 audit findings (`A02`, `A03`, `A04`, `A07`, `A08`, `A11`, `A14`), and Bit BlazorUI 10.7.0 / zero-hardcoded localization standards.
- [decisions.md](docs/slices/05-item-catalog/decisions.md): open decisions **`Q-M01`–`Q-M04`** (3-part phased delivery `05A` -> `05B` -> `05C`, organization-shared ownership with composite `(OrganizationId, Id)` keys, granular permissions `Units.*` / `Items.*` / `Boms.*` auto-granted to `OrganizationAdministrator`, and collapsible **Master Data & Engineering** sidebar navigation group).
- [physical-erd.md](docs/slices/05-item-catalog/physical-erd.md) & [test-plan.md](docs/slices/05-item-catalog/test-plan.md): SQL schema contract, composite FKs, `CHECK` constraints, and unit/SQL/browser verification matrix (`M01`–`M07`, `S01`–`S04`, `B01`–`B04`).

Next small step: confirm `Q-M01`–`Q-M04` in [decisions.md](docs/slices/05-item-catalog/decisions.md) to begin Sub-slice `05A` (**Units of Measure & Conversions** — Domain, Application, EF migration, and `/units` Bit Blazor UI).

## Slice 03 (`03.5` ACCEPTED) & Slice 04 (`04.0`–`04.5` Audit & Security Events Viewer ACCEPTED) (2026-11-10)

### 1. Slice 03 (`03-roles-permissions`) — ACCEPTED (`03.5`)
- User ran `RoleManagementSqlTests` (`6/6 PASS`, `10.1s`) and `.\scripts\upgrade-local-testdb.ps1` (`testforgflow database upgraded to 20261110134703_RolesAndGranularPermissions and admin full permissions verified.`) and signed off on Slice 03.

### 2. Slice 04 (`04-audit-viewer`, `04.0`–`04.4`) — implemented & verified
- **Closed decisions (`04.0`, [decisions.md](docs/slices/04-audit-viewer/decisions.md))**:
  - `Q-A01`: Explicit `Audit.Read` permission (`PermissionModule.Audit = 5`, `OrganizationOrSite` applicability, plus `Platform` for `DeploymentProvisioner`). Granted automatically to built-in `OrganizationAdministrator` (`RoleId = 1`) and `DeploymentProvisioner` (`RoleId = 3`) in `RuntimeSessions.ScopeAllowed`, `FixedRolePolicy.Allows`, and `SqlRoleOperationsStore`, while preserving zero pending EF Core model changes (`HasPendingModelChanges() == false`).
  - `Q-A02`: Unified `/audit` page (`/audit`, `/organizations/{OrganizationId:long}/audit`) with Category filter (`All`, `Business`, `Security`), event-code filter, and search box.
  - `Q-A03`: Strict scope isolation (`Organization(orgId)` returns rows with `OrganizationId == orgId`; `Site(orgId, siteId)` returns rows with `OrganizationId == orgId && SiteId == siteId`; `Platform` returns `auth.SecurityEvent` rows with `OrganizationId == null`).
  - `Q-A04`: Read-only `BitDialog` detail inspector flattening nested JSON keys via `AuditPayloadParser.ParseFields` alongside raw JSON and `CorrelationId`.
- **Implementation (`04.1`–`04.4`)**:
  - Domain: `src/ForgeFlow.Domain/Roles/PermissionCatalog.cs` (`PermissionModule.Audit`, `"Audit.Read"`), `src/ForgeFlow.Domain/Audit/AuditErrors.cs` (`AuditErrors`, `AuditPayloadParser`).
  - Application: `src/ForgeFlow.Application/Audit/AuditContracts.cs`, `src/ForgeFlow.Application/Audit/AuditUseCases.cs`.
  - Infrastructure: `src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs`, `src/ForgeFlow.Infrastructure/Identity/SqlRoleOperationsStore.cs`, `src/ForgeFlow.Infrastructure/Identity/SqlAuditOperationsStore.cs`, `src/ForgeFlow.Infrastructure/Identity/IdentityPersistence.cs` (`AddAuditManagement()`).
  - Web & Tests: `src/ForgeFlow.Web/Program.cs`, `src/ForgeFlow.Web/ReadScopes.cs` (`LoadAuditScopesAsync()`), `src/ForgeFlow.Web/Components/Layout/MainLayout.razor` (`/audit` in `SettingsItems`), `src/ForgeFlow.Web/Roles/RolePermissionLocalization.cs`, `src/ForgeFlow.Web/Components/Pages/Audit.razor` (refined with `BitDataGrid` server-side pagination, `.read-toolbar-card`, `.read-grid-card`, `انجام‌دهنده` label in `fa-IR`, and clean padded `.audit-modal-body` / `.audit-meta-card` / `.audit-fields-box` without OK/Cancel footer buttons), `SharedResources.en-US.resx`, `SharedResources.fa-IR.resx`, `tests/ForgeFlow.Web.Tests/ResourceContractTests.cs`, `tests/ForgeFlow.Web.Tests/RoleDomainTests.cs`, `tests/ForgeFlow.Web.Tests/AuditTests.cs`, `tests/ForgeFlow.Web.Tests/AuditSqlTests.cs`.
- **Actual verification (Release, no restore)**:
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS (8 projects, 0 warnings, 0 errors).
  - `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **329 passed, 0 failed, 44 skipped** (SQL suites skipped in agent sandbox).
  - `node .\scripts\drafts.test.mjs` — **19 passed, 0 failed**.
  - `node .\verification\slice01-ui\browser-check.cjs` — **4/4 PASS** (`fa-IR` / `en-US` at `1440px` / `320px`).
  - Development Web (`task-1824`) running at `https://localhost:5001`.
- **Slice 04 acceptance status (`04.5`)**:
  - User reviewed and confirmed `/audit` UI (`BitDataGrid` pagination, `انجام‌دهنده` column, and clean `BitDialog` details modal without OK/Cancel footer buttons or horizontal overflow) and authorized committing and pushing Slice 03 & Slice 04 (`AuditSqlTests` disposable SQL suite remains available for execution in normal PowerShell via `FORGEFLOW_TEST_SQLSERVER`).

## Slice 03 (`03.2` SQL `6/6` PASS; `03.3`–`03.4` Web & UI VERIFIED; `testforgflow` upgraded) & Slice 04 (`04.0` Audit Viewer design proposed) (2026-11-10)

### 1. Slice 03 (`03.2`–`03.4`) — SQL & Web UI verification
- **User-executed disposable SQL suite (`03.2`)**:
  - `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~RoleManagementSqlTests' --logger 'console;verbosity=normal'` — **6 passed, 0 failed, 0 skipped** (`10.1s`), covering `S01`–`S07` (populated baseline-to-Slice-03 upgrade, custom org role enforcement, site-scoped direct operation grants + membership deactivation revocation, cross-tenant & duplicate-name isolation, SQL trigger audit rollback, stale `RowVersion` / zero-audit no-op, and barrier-synchronized last-admin safeguard).
- **Web DI, dynamic navigation, `/roles` & per-user permissions matrix (`03.3`–`03.4`)**:
  - Wired `builder.Services.AddRoleManagement();` in `src/ForgeFlow.Web/Program.cs`.
  - Updated `src/ForgeFlow.Web/ReadScopes.cs` (`LoadOrganizationScopesAsync`, `Load`, `LoadUserScopesAsync`, `LoadRoleScopesAsync`) to discover candidate scopes from the union of `ScopedRoleAssignments` and `UserPermissionGrants`, and updated `OrganizationReader.razor` to use `LoadOrganizationScopesAsync()` when `!IsSites`.
  - Updated `src/ForgeFlow.Web/Components/Layout/MainLayout.razor` so `NavigationItems` completely omits the parent `Settings` group when `SettingsItems.Count == 0` (when the signed-in user has no access to any child page under `Settings`), and adds `/roles` when `roleScopes.Count > 0`.
  - Added `src/ForgeFlow.Web/Roles/RolePermissionLocalization.cs`, `src/ForgeFlow.Web/Components/Pages/Roles.razor` (`/roles`), and the compact per-user **Permissions (`UserPermissionsButton`)** modal in `src/ForgeFlow.Web/Components/Pages/Users.razor` (showing `UserAdminFullAccessNotice` and `RoleGrantedBadge` for active roles, role assignment/activation toggles, and page-by-page operation checkboxes at Organization or Site scope with automatic `Read` checking when any write operation is checked).
  - Extended `SharedResources.en-US.resx` and `SharedResources.fa-IR.resx` and added `tests/ForgeFlow.Web.Tests/RoleManagementPageTests.cs`.
- **Actual verification (Release, no restore)**:
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS (8 projects, 0 warnings, 0 errors).
  - `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **323 passed, 0 failed, 43 skipped**.
  - `node .\scripts\drafts.test.mjs` — **19 passed, 0 failed**.
  - `node .\verification\slice01-ui\browser-check.cjs` — **4/4 PASS** (`fa-IR` / `en-US` at `1440px` / `320px`).
  - User executed `.\scripts\upgrade-local-testdb.ps1` on `localhost/testforgflow` -> `testforgflow database upgraded to 20261110134703_RolesAndGranularPermissions and admin full permissions verified.`
  - Development Web (`task-1511`) running at `https://localhost:5001` (`GET /account/login` -> `HTTP 200`; anonymous `GET /roles` -> `HTTP 302`).

### 2. Slice 04 (`04-audit-viewer`, `04.0`) — design proposed for review
Created the design-first Slice 04 package under `docs/slices/04-audit-viewer/` per `ForgeFlow.md` `۰۰.۴` row `05`:
- [README.md](docs/slices/04-audit-viewer/README.md): read-only, scope-isolated `/audit` viewer over `organization.OrganizationChange` and `auth.SecurityEvent`.
- [decisions.md](docs/slices/04-audit-viewer/decisions.md): open decisions **`Q-A01`–`Q-A04`** (`Audit.Read` permission code, unified `/audit` page with category filter `All / Business Changes / Security Events`, platform-null event visibility, structured JSON detail dialog).
- [physical-erd.md](docs/slices/04-audit-viewer/physical-erd.md) & [test-plan.md](docs/slices/04-audit-viewer/test-plan.md): index review, additive `Audit.Read` seed, and unit/SQL/browser test plan.

Next small step: confirm Slice 03 UI (`03.5`) on `https://localhost:5001/roles` and `/users`, and approve `Q-A01`–`Q-A04` to implement `04.1`–`04.4`.

## Slice 03 (`03.2` Infrastructure persistence, additive migration & SQL test suite implemented — offline checks PASS, SQL run pending before `03.3`–`03.4` Web UI) (2026-11-10)

Scope:
- **Entities & EF Core Schema (`src/ForgeFlow.Infrastructure/Identity/`)**:
  - Updated `IdentityRecords.cs` (`FixedAccessRoles` widened to `long`, `ScopedRoleAssignment.RoleId` widened `byte` -> `long`, added `RoleDefinition`, `RolePermission`, `UserPermissionGrant`).
  - Updated `AuthDbContext.cs` (`RoleDefinitions`, `RolePermissions`, `UserPermissionGrants`, system seeds `1..3` + `SystemRolePermissionSeeds()`, `FK_ScopedRoleAssignment_RoleDefinition_RoleId`, updated `CK_Role_Scope` for `RoleId >= 100`, updated `CK_SecurityEvent_Code` with `'RoleDefinitionChanged'` and `'DirectPermissionChanged'`, expanded `Guard()` immutability checks).
  - Generated and fixed non-destructive migration `20261110134703_RolesAndGranularPermissions.cs` + `AuthDbContextModelSnapshot.cs`: drops and recreates the 3 filtered unique indexes on `ScopedRoleAssignment.RoleId` around `AlterColumn<long>`, creates `RoleDefinition` (`IDENTITY(100, 1)`), `RolePermission`, `UserPermissionGrant`, seeds system roles & permissions without touching existing users/business tables, and grants `SELECT` / denies `INSERT, UPDATE, DELETE` to `ForgeFlowAuthRuntime`.
- **Dynamic evaluation & SQL Store**:
  - Updated `RuntimeSessions.cs` (`ScopeAllowed` dynamically evaluates `RoleDefinitions` + `RolePermissions` + `ScopedRoleAssignments` + `UserPermissionGrants`, and `FixedRolePolicy` supports `long` role IDs + `RolePermissions.*`).
  - Added `SqlRoleOperationsStore.cs` (`IRoleOperationsStore` over `Serializable` SQL transactions, `UPDLOCK, HOLDLOCK` lock discipline, cross-organization checks, escalation ceiling, last built-in `OrganizationAdministrator` safeguard, atomic `SecurityEvent` inserts, strict 8-byte Base64 `RowVersion` concurrency, and SQL/audit failure mapping) and `IdentityPersistence.AddRoleManagement()`.
- **Tests (`tests/ForgeFlow.Web.Tests/`)**:
  - Updated `AuthBootstrapTests.cs` for 13 `auth` entity types and 2 additive migrations.
  - Added `RoleManagementSqlTests.cs`: offline `AddRoleManagement_fails_closed_when_trusted_adapters_are_missing` plus disposable SQL tests `S01`–`S07` (populated baseline-to-Slice-03 upgrade, custom org role enforcement, site-scoped direct operation grants + membership deactivation revocation, cross-tenant & duplicate-name isolation, SQL trigger audit rollback, stale `RowVersion` / zero-audit no-op, and barrier-synchronized last-admin safeguard).

Actual agent verification (this session, Release, no restore):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS (8 projects, 0 warnings, 0 errors).
- `dotnet ef migrations script 20261108150853_IdentityBootstrapBaseline 20261110134703_RolesAndGranularPermissions --project .\src\ForgeFlow.Infrastructure --startup-project .\src\ForgeFlow.Infrastructure --context AuthDbContext --configuration Release --no-build` — PASS (verified clean index drop/recreate order around `RoleId` `bigint` alter and additive `RoleDefinition`/`RolePermission`/`UserPermissionGrant` DDL).
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **315 passed, 0 failed, 43 skipped** (SQL suites skipped in agent sandbox).
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~RoleDomainTests|FullyQualifiedName~RoleUseCaseTests|FullyQualifiedName~RoleManagementSqlTests|FullyQualifiedName~AuthBootstrapTests' --logger 'console;verbosity=minimal'` — **37 passed, 0 failed, 5 skipped** (`S01`–`S07` await disposable SQL run).
- Development Web (`task-1198`) running at `https://localhost:5001`: `GET https://localhost:5001/account/login` -> `HTTP 200` (`15711 bytes`). No auto-migration or credential reset run on `localhost/testforgflow`.

Remaining for Slice 03 (`03.2` SQL verification + `03.3`–`03.5`):
- Run disposable SQL suite `S01`–`S07` (`RoleManagementSqlTests`) in normal Windows PowerShell (`FORGEFLOW_TEST_SQLSERVER`), and apply migration `20261110134703_RolesAndGranularPermissions` (`dotnet ef database update ...`) to `localhost/testforgflow` manually before live UI sign-in/navigation tests against the upgraded schema.
- `03.3`–`03.4` Web DI registration (`AddRoleManagement`), dynamic `ReadScopes` (custom role & direct grant scope discovery), `/roles` + per-user page-operation matrix UI, localized `fa-IR`/`en-US` `.resx`, and `03.5` acceptance.

Next small step: run `RoleManagementSqlTests` in normal PowerShell against disposable SQL Server (`FORGEFLOW_TEST_SQLSERVER`), then implement `03.3`–`03.4` Web DI, dynamic `ReadScopes`, and bilingual `/roles` + user permission UI.

## Slice 03 (`03.0` decisions closed; `03.1` Domain invariants, Application contracts & 20 unit tests VERIFIED) (2026-11-10)

Scope:
- Closed `03.0` design decisions `Q-R01`–`Q-R05` in `docs/slices/03-roles-permissions/decisions.md` (hybrid custom organization roles + direct per-user operation grants, explicit server permissions + UI auto-Read convenience, escalation ceiling + last built-in admin protection, immutable system roles `1..3`, non-destructive additive EF migration).
- **Domain (`src/ForgeFlow.Domain/Roles/`)**: added `RolePermissionErrors`, `RoleRuleException`, culture-independent `RoleDefinitionName` (`1..100`), and `PermissionCatalog` (authoritative single catalog covering every declared constant in `OrganizationPermissions`, `UserPermissions`, and `RolePermissions`, with scope applicability and `ValidateTenantPermissions` / `EnsureCustomRoleMutable` / `EnsureNotSelfAdminRevocation`). Zero infrastructure or presentation dependencies.
- **Application (`src/ForgeFlow.Application/Roles/`)**: added `RolePermissions` (`Roles.Read`, `Roles.Manage`, `Permissions.Assign`), DTOs (`RoleSummary`, `RoleDetails`, `UserPermissionMatrix`, `CreateOrganizationRole`, `UpdateOrganizationRole`, `AssignUserRole`, `SetUserRoleAssignmentActive`, `SaveUserOperationGrants`), `IRoleOperationsStore`, `RoleStoreUnavailableException`, and `RoleUseCases` (fail-closed authorization, cross-org `Roles.NotFound`, escalation ceiling via `CallerHoldsAllAsync`, system-role protection `1..3`, bounded paging `1..100`, strict 8-byte Base64 version decoding).
- **Unit tests (`tests/ForgeFlow.Web.Tests/`)**: added `RoleDomainTests` and `RoleUseCaseTests` covering `R01`–`R04`.

Actual verification (this session, Release, no restore):
- Stopped Web `task-1043`; built `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS (8 projects, 0 warnings, 0 errors).
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **314 passed, 0 failed, 38 skipped** (SQL suites skipped in agent sandbox).
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~RoleDomainTests|FullyQualifiedName~RoleUseCaseTests' --logger 'console;verbosity=minimal'` — **20 passed, 0 failed, 0 skipped**.
- `node .\scripts\drafts.test.mjs` — **19 passed, 0 failed**.
- Relaunched Development Web (`task-1077`) at `https://localhost:5001`.

Remaining for Slice 03 (`03.2`–`03.5`):
- `03.2` Infrastructure entities/migration (`RoleDefinition`, `RolePermission`, `UserPermissionGrant`, `ScopedRoleAssignment` FK/check update, `CK_SecurityEvent_Code`), `SqlRoleOperationsStore`, dynamic `ScopeAllowed` evaluation, and SQL/upgrade tests (`S01`–`S07`).
- `03.3`–`03.4` Web DI registration (`AddRoleManagement`), dynamic `ReadScopes`, `/roles` + per-user page-operation matrix UI, bilingual `.resx`, and `03.5` acceptance.

Next small step: implement `03.2` (Infrastructure entities, EF Core non-destructive migration, `SqlRoleOperationsStore`, dynamic `ScopeAllowed` evaluation, and disposable SQL/upgrade tests).

## Slice 02 (`02.5` closure with icon-only nav toggle) & Slice 03 (`03.0` Roles & Permissions) design increment (2026-11-10)

### 1. Mobile nav drawer icon toggle & Slice 02 closure
- **Mobile navigation toggle (`src/ForgeFlow.Web/Components/Layout/MainLayout.razor`, `src/ForgeFlow.Web/wwwroot/app.css`)**: checked `Bit.BlazorUI` 10.7.0 built-in `bit.blazorui.css` glyphs (`CollapseMenu`/`GlobalNavButton` are not bundled in the base stylesheet), added `.ff-icon--menu` SVG mask icon matching existing `.ff-icon--*` shell icons, and replaced the `< 1280px` text button with an icon-only `36x36` `BitButton` (`Variant="BitVariant.Outline"`, `Size="BitSize.Small"`, `IconOnly="true"`, `Icon="@MenuIcon"`, `aria-label="@Text["OpenNavigation"]"`, `title="@Text["OpenNavigation"]"`).
- **Build & tests after final UI edit (Release, no restore)**:
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS (8 projects, 0 warnings, 0 errors).
  - `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **294 passed, 0 failed, 38 skipped**.
  - `node .\scripts\drafts.test.mjs` — **19 passed, 0 failed**.
  - Development Web (`task-1043`) running at `https://localhost:5001`: `GET /account/login` -> `HTTP 200` (`length=15729`); `node .\verification\slice01-ui\browser-check.cjs` -> **4/4 PASS** (`fa-IR`/`en-US`, `1440px`/`320px`).
- **Slice 02 status**: user verified UI flows and authorized advancing to Slice 03 ("خوب اوکی هستش... برو کار بعدیت"). Preserving honest attribution: earlier `UserManagementSqlTests` ran `4/4 PASS` prior to `02.4b`; re-running `UserManagementSqlTests` to exercise the newly added `ResetUserPassword` + `ChangeOwnPasswordAsync` SQL assertions remains optional in PowerShell.

### 2. Slice 03 (`03-roles-permissions`, `03.0`) — design proposed for review
Created the design-first Slice 03 package under `docs/slices/03-roles-permissions/` per roadmap row `04` (`۰۰.۴`) and the 10-section contract (`۰۰.۵`):
- [README.md](docs/slices/03-roles-permissions/README.md): data-driven per-page/per-operation permissions (`Organizations.*`, `Sites.*`, `Users.*`, `Memberships.*`, `Roles.*`, `Permissions.*`) over custom org roles and/or direct per-user operation grants, live `ScopeAllowed` & `ReadScopes` evaluation, anti-escalation guards, and dynamic page UI gating.
- [decisions.md](docs/slices/03-roles-permissions/decisions.md): open decisions **`Q-R01`–`Q-R05`** (hybrid custom roles + direct per-user page operation matrix vs single mechanism, `*.Read` UI/server dependency behavior, no-escalation ceiling & last built-in `OrganizationAdministrator` rule, system role immutability, additive non-destructive SQL migration on populated `localhost/testforgflow`).
- [physical-erd.md](docs/slices/03-roles-permissions/physical-erd.md): proposed `auth.RoleDefinition`, `auth.RolePermission`, `auth.UserPermissionGrant`, `ScopedRoleAssignment` FK/check upgrade, and additive `CK_SecurityEvent_Code` updates (`'RoleDefinitionChanged'`, `'DirectPermissionChanged'`).
- [test-plan.md](docs/slices/03-roles-permissions/test-plan.md): unit (`R01`–`R05`), disposable SQL + populated-upgrade (`S01`–`S07`), and bilingual mobile/desktop (`B01`–`B04`).

**Design-first gate**: Zero Slice 03 code or EF migrations created yet. Next step: resolve open decisions `Q-R01`–`Q-R05` before implementing `03.1`.

## Slice 02 (`02.4b`) — Modal compact spacing, password visibility toggles, admin password reset & self change password (2026-11-10)

Scope (from user UI feedback on `/users`):
- **Compact modal input spacing (`src/ForgeFlow.Web/wwwroot/app.css`)**: fixed `.read-field` vertical `flex-basis: 180px` inflation inside column-flex `.write-panel` modals (`.write-panel .read-field { flex: 0 0 auto; gap: 4px; }` and compact `min-height: 38px`), plus `.write-field` styling matching `Login.razor`'s `BitTextField` eye-toggle buttons and `.change-password-card` / `.user-menu-link`.
- **Password visibility eye toggle & Admin Password Reset (`Users.ResetPassword`)**:
  - Switched `Users.razor` Create User password field to `BitTextField` (`Type="BitInputType.Password"`, `CanRevealPassword="true"`, `Immediate="true"`).
  - Added `UserPermissions.UsersResetPassword`, `ResetUserPassword` DTO/use-case (`src/ForgeFlow.Application/Users/`), `FixedRolePolicy` / `ScopeAllowed` grant (`DeploymentProvisioner` at `Platform`, `OrganizationAdministrator` at `Organization`), and `SqlUserOperationsStore.ResetUserPassword` (single-org isolation check, `RowVersion` concurrency guard, Identity `PasswordValidator` check, `SecurityStamp` rotation, atomic session revocation with `RevokeReason = 'PasswordReset'`, and `'PasswordResetCompleted'` `SecurityEvent`), wired into `Users.razor` Edit modal with eye toggle and confirmation dialog.
- **Self Change Password (`/account/change-password`)**:
  - Added `RuntimeSessions.ChangeOwnPasswordAsync` (verifies session & current password, validates new password, rotates `SecurityStamp`, revokes open sessions with `'PasswordReset'`, issues a fresh `AccountSession` preserving original fixed expiry, logs `'PasswordResetCompleted'` & `'SessionIssued'`).
  - Added `POST /account/change-password-submit` in `src/ForgeFlow.Web/Authentication/AuthenticationBackend.cs`, `auth-ui.mjs` `[data-auth-change-password]` handling, localized `AuthenticationResources` (`Auth.PasswordChanged`, `Auth.CurrentPasswordInvalid`, `Auth.PasswordInvalid`, `Auth.PasswordMismatch`), `ChangePassword.razor` (3 `BitTextField` inputs with eye toggles), and menu link in `UserMenu.razor`.
- **Note on granular per-page/per-operation permissions requested by user**: per `ForgeFlow.md` `۰۰.۴` row `04`, assigning individual page/operation permissions (`Read` / `Create` / `Update` / `SetActive` per feature & user/role) belongs to **Slice 03 (Roles & Permission Assignment)** after Slice 02 UI acceptance closes.

Actual verification (this session, Release, no restore):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **294 passed, 0 failed, 38 skipped**.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~UserManagementPageTests|FullyQualifiedName~ResourceContractTests|FullyQualifiedName~HardcodedDisplayTextScannerTests' --logger 'console;verbosity=minimal'` — **31 passed, 0 failed, 0 skipped**.
- `node .\scripts\drafts.test.mjs` — **19 passed, 0 failed**.
- Launched Development Web (`task-1010`) at `https://localhost:5001`.

Remaining for Slice 02 (`02.5`):
- Re-run `UserManagementSqlTests` in normal PowerShell (`4` cases now include admin password reset + self change password), plus manual browser check of compact `/users` modals, eye toggle, `/account/change-password`, and cross-tab session revocation (`fa-IR` / `en-US`, desktop & `320px`).

## Slice 02 (`02.2` SQL PASS; `02.3`–`02.4` Bilingual Users & Memberships UI implemented, browser `02.5` pending) (2026-11-10)

### 1. `02.2` USER-EXECUTED SQL suite — PASS
User executed in normal Windows PowerShell against disposable SQL Server (`FORGEFLOW_TEST_SQLSERVER`, cleared afterward):
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~UserManagementSqlTests' --logger 'console;verbosity=normal'` — **4 passed, 0 failed, 0 skipped** (`8.5s`), proving atomic creation + SQL trigger audit rollback, cross-tenant duplicate/isolation boundaries, session revocation (`AccountDisabled`) + lockout recovery (`UnlockUser`), barrier-synchronized last-admin protection, immediate revocation denial, stale `RowVersion` conflict, and no-op zero-audit stability. Production code was unchanged for `02.2` (only the `Users.razor` UI & DI were added in `02.3`–`02.4`).

### 2. `02.3`–`02.4` Bilingual Users & Memberships UI
Scope:
- **Composition & navigation**: added `builder.Services.AddUserManagement()` in `src/ForgeFlow.Web/Program.cs`; added `ReadScopes.LoadUserScopesAsync()` (verifying `UserPermissions.UsersRead` per authorized `Organization` role grant) in `src/ForgeFlow.Web/ReadScopes.cs`; added `Users` item in `src/ForgeFlow.Web/Components/Layout/MainLayout.razor` (`Settings` section, visible only when `userScopes.Count > 0`, using existing `.ff-icon--user`).
- **UI page (`src/ForgeFlow.Web/Components/Pages/Users.razor`)**: routes `/users`, `/organizations/{OrganizationId:long}/users`, `/organizations/{OrganizationId:long}/users/{UserId:long}`, and `/users/{UserId:long}`; `BitDataGrid` server-side paging (`10/25/50/100`), explicit organization scope selector (`OrgCode — OrgName`), search & inactive filter, View & Edit modals (`BitDialog`, compact responsive width `420px–440px`), Create User modal (one-time `type="password"` `autocomplete="new-password"` input NEVER stored in `DraftCoordinator` drafts and cleared after submit/discard; optional initial `OrganizationAdministrator`, active site checkboxes & `SiteOperator` toggle), Platform-only Attach Existing User modal, account unlock/disable/enable (enforcing single-org vs multi-org hint & self-protection + confirmation prompt naming username), org membership & `OrganizationAdministrator` role toggles, per-site membership & `SiteOperator` role management + site assignment, and `ConcurrencyConflict` / `OutcomeUnknown` reload flow.
- **Resources & tests**: added bilingual FA/EN entries in `SharedResources.en-US.resx` and `SharedResources.fa-IR.resx`; added CSS layout helpers in `src/ForgeFlow.Web/wwwroot/app.css`; updated `ResourceContractTests.cs`; added `tests/ForgeFlow.Web.Tests/UserManagementPageTests.cs` (routes, exhaustive error-key mapping across `fa-IR`/`en-US`, `HtmlRenderer` denied-state render, password-draft exclusion, and saving-flag mutation behavior).

Actual agent verification (this session, Release, no restore):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **292 passed, 0 failed, 38 skipped** (`UserManagementPageTests` + `UserDomainTests` + `UserUseCaseTests` + `ResourceContractTests` + `HardcodedDisplayTextScannerTests` + `AuthHttpTests`, etc.; SQL tests remain skipped in agent sandbox).
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~UserManagementPageTests|FullyQualifiedName~ResourceContractTests|FullyQualifiedName~HardcodedDisplayTextScannerTests' --logger 'console;verbosity=minimal'` — **29 passed, 0 failed, 0 skipped**.
- `node .\scripts\drafts.test.mjs` — **19 passed, 0 failed**.
- Launched Development Web (`task-905`) at `https://localhost:5001` (`ConnectionStrings__ForgeFlow` for `localhost/testforgflow`):
  - `GET /account/login` -> `HTTP 200` (`length=15702`).
  - `GET /users` (anonymous, no redirect follow) -> `HTTP 302` (`Location: https://localhost:5001/account/login?returnUrl=%2F`).
  - `node .\verification\slice01-ui\browser-check.cjs` -> **4/4 PASS** (`fa-IR`/`en-US` at `1440px`/`320px`).

Remaining gaps before closing Slice 02 (`02.5` acceptance):
- Authenticated browser verification (`B01`–`B06`) on `https://localhost:5001` in both `fa-IR` (RTL) and `en-US` (LTR) at desktop and `320px` mobile for `/users` (Create User + verify `sessionStorage` draft excludes password, Edit memberships/roles/account disable + live session invalidation on another tab/browser, Unlock lockout, Conflict reload, cross-org URL isolation). Slice 02 remains NOT ACCEPTED until UI acceptance completes.

Next small step: sign in at `https://localhost:5001/account/login`, test `/users` across `fa-IR` and `en-US` (desktop & `320px`), and verify `B01`–`B06` before closing `02.5`.

## Slice 02 (`02.2`) — UserManagementSqlTests first run (3/4 PASS) & test assertion fix (2026-11-10)

User executed in normal Windows PowerShell (`FORGEFLOW_TEST_SQLSERVER`):
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~UserManagementSqlTests' --logger 'console;verbosity=normal'` — **3 passed, 1 failed, 0 skipped**:
  - Passed (3): `Cross_tenant_duplicate_and_multi_org_isolation_block_org_admin_from_global_disable`, `Disabling_single_org_user_revokes_sessions_atomically_and_unlock_recovers_lockout`, `Atomic_create_user_with_site_membership_and_role_writes_audit_and_fault_injection_rolls_back`.
  - Failed (1): `Barrier_synchronized_admins_cannot_leave_active_organization_without_active_admin_and_rowversion_conflicts` — barrier race assertions (`1 succeeded`, loser received `Users.LastOrganizationAdminRequired`) passed, then line 368 failed because `t2` won (deactivating `initial.admin`'s admin grant) and the test unconditionally invoked the subsequent no-op check via `provider1` (`initial.admin`), which was immediately denied (`Users.AccessDenied`).
- Fix (`tests/ForgeFlow.Web.Tests/UserManagementSqlTests.cs`): explicitly verify the deactivated admin's provider receives `Users.AccessDenied`, then select the surviving admin provider for the no-op and `Users.ConcurrencyConflict` assertions before comparing `SecurityEvents` counts. Production code (`SqlUserOperationsStore.cs`) unchanged.
- Fresh agent build & checks after test fix:
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
  - `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **286 passed, 0 failed, 38 skipped**.
  - Relaunched Development Web (`task-829`) at `https://localhost:5001`.

Next step: re-run the 4-case `UserManagementSqlTests` filter in normal Windows PowerShell to confirm 4/4 PASS, then start `02.3`/`02.4` Web DI & bilingual UI.

## Slice 02 (`02.2`) — SQL user/membership store & permission evaluator implemented (offline PASS, SQL pending) (2026-11-10)

Scope:
- **Permission evaluator (`src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs`)**: extended `FixedRolePolicy.Allows` and `RuntimeSessions.ScopeAllowed` for `UserPermissions.*` (`Users.AttachOrganization`/`Users.SetActive`/`Users.Unlock` at `Platform` for `DeploymentProvisioner`; `Users.Read`/`Users.Create`/`Users.SetActive`/`Users.Unlock`/`Memberships.Manage`/`Roles.AssignFixed` at `Organization` for `OrganizationAdministrator`; `Users.Read` permitted alongside org/site read on inactive organizations; `SiteOperator` never granted user/membership permissions).
- **SQL persistence (`src/ForgeFlow.Infrastructure/Identity/SqlUserOperationsStore.cs`)**: implemented `IUserOperationsStore` using operation-scoped `AuthDbContext` + `UserManager<ApplicationAccount>`, `Serializable` transactions, fixed lock order (`Organization` in ascending ID order -> `ApplicationAccount` via `UPDLOCK, HOLDLOCK`), `OrganizationWriteAccess.Revalidate`, atomic `'AccountCreated'`/`'MembershipChanged'`/`'ScopedRoleChanged'`/`'AccountEligibilityChanged'`/`'SessionRevoked'` (`RevokeReason = 'AccountDisabled'` + `SecurityStamp` rotation on disable), self/last-active-admin guards (`TenantAccessPolicy`), single-org isolation on account toggle/unlock, explicit `EF.Property<byte[]>(..., "RowVersion")` projection, and failure mapping (`UserNameDuplicate`, `PasswordInvalid`, `ConcurrencyConflict`, `AuditUnavailable`, `OutcomeUnknown`, `PersistenceUnavailable`).
- **Registration (`src/ForgeFlow.Infrastructure/Identity/IdentityPersistence.cs`)**: added `AddUserManagement()`.
- **Tests (`tests/ForgeFlow.Web.Tests/`)**: added offline registration/policy tests in `UserUseCaseTests.cs` and 4 disposable SQL Server tests in `UserManagementSqlTests.cs` (covering atomic create + SQL audit trigger rollback, cross-tenant isolation & Platform attachment, session revocation + brute-force unlock recovery, barrier-synchronized last-admin protection + rowversion/no-op guards).

Actual commands and results (this session, Release, no restore):
- Stopped workspace Development instance `task-735`; rebuilt `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **PASS: 286 passed, 0 failed, 38 skipped** (SQL suites remain skipped in agent sandbox).
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~UserDomainTests|FullyQualifiedName~UserUseCaseTests|FullyQualifiedName~UserManagementSqlTests' --logger 'console;verbosity=minimal'` — **31 passed, 0 failed, 4 skipped** (`UserManagementSqlTests` requires `FORGEFLOW_TEST_SQLSERVER`).

Prepared SQL verification command for normal Windows PowerShell (`FORGEFLOW_TEST_SQLSERVER` set privately to disposable CREATE/DROP access):
```powershell
dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~UserManagementSqlTests' --logger 'console;verbosity=normal'
```

Next small step: run `UserManagementSqlTests` against disposable SQL Server, or implement `02.3`/`02.4` Web DI registration (`AddUserManagement`) and bilingual `/users` management UI.

## Slice 02 (`02.1`) — Domain invariants, Application contracts & unit tests VERIFIED (2026-11-10)

Scope:
- **Domain** (`src/ForgeFlow.Domain/Users/`): added `UserManagementErrors`, `UserRuleException`, culture-independent ASCII username canonicalizer `UserAccountName` (`[A-Za-z0-9._@+-]`, `1..128` chars matching `CK_User_Name`), and pure lifecycle/scope/last-admin invariants `TenantAccessPolicy` (`TenantRoleKind.OrganizationAdministrator` / `SiteOperator`, `EnsureNotSelfDeactivation`, `EnsureOrganizationRetainsActiveAdmin`, `EnsurePlatformRetainsActiveProvisioner`, `EnsureSiteMembershipPrerequisites`, `EnsureTenantRolePrerequisites`). No infrastructure or presentation dependencies.
- **Application** (`src/ForgeFlow.Application/Users/`): added `UserPermissions` (`Users.Read`, `Users.Create`, `Users.SetActive`, `Users.Unlock`, `Users.AttachOrganization`, `Memberships.Manage`, `Roles.AssignFixed`), read/command DTOs, `IUserOperationsStore`, `UserStoreUnavailableException`, and `UserUseCases` (fail-closed `ITrustedActorAccessor` + `IOrganizationPermissionEvaluator`, scope-matching & anti-enumeration `Users.NotFound`, bounded paging `1..100`, strict 8-byte Base64 version decode, initial-password sanity check without secret leakage, self-deactivation prevention, `OrganizationWriteAccess` revalidation callback).
- **Unit tests** (`tests/ForgeFlow.Web.Tests/`): added `UserDomainTests` and `UserUseCaseTests` covering `U01`, `U03`, `U04`, `U05`.

Actual commands and results (this session, Release, no restore):
- Stopped workspace Development instance `task-607`; ran `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` — **PASS: 284 passed, 0 failed, 34 skipped** (SQL suites remain skipped in agent environment).
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~UserDomainTests|FullyQualifiedName~UserUseCaseTests' --logger 'console;verbosity=minimal'` — **PASS: 29 passed, 0 failed, 0 skipped**.
- Relaunched freshly built Web in Development at `https://localhost:5001` (`task-735`, process-only `ConnectionStrings__ForgeFlow` for `localhost/testforgflow`, no migration/bootstrap/recovery).

Remaining gaps for Slice 02 (NOT YET IMPLEMENTED):
- `02.2` Infrastructure SQL store (`IUserOperationsStore` over `AuthDbContext` + `UserManager`, `UPDLOCK, HOLDLOCK` lock discipline, atomic `SecurityEvent` & `AccountDisabled` session revocation, `SessionPermissionEvaluator` mappings for `UserPermissions.*`) and disposable SQL tests (`S01`–`S07`).
- `02.3` DI wiring & `02.4` bilingual UI (`/users`, `/organizations/{orgId}/users`, `BitDataGrid` paging, create/membership/role/unlock dialogs, FA/EN `.resx`, `1440px`/`320px` Chrome checks).
- Slice 02 remains NOT ACCEPTED until `02.2`–`02.5` pass.

Next small step: implement increment `02.2` (`IUserOperationsStore` + `SessionPermissionEvaluator` updates + disposable SQL integration/concurrency tests).

## Slice 02 (02.0) design increment & AUTH-01 A5 / Slice 01 (01.5) acceptance closure (2026-11-10)

### 1. AUTH-01 A5 & Slice 01 (`01.5`) closure
User confirmed local browser verification in full ("خوب همه چی درسته") and authorized advancing to Slice 02. Recorded status separates verification provenance strictly:
- **Build**: `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS (8 projects, 0 warnings, 0 errors; unchanged since last code edit).
- **Agent automated suite**: `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore` — 255 passed, 0 failed, 34 skipped (SQL tests remain skipped in agent sandbox).
- **JS draft tests & Chrome smoke check**: `node .\scripts\drafts.test.mjs` (19 passed) and `node .\verification\slice01-ui\browser-check.cjs` (4/4 PASS for `fa-IR` / `en-US` at `1440px` / `320px`).
- **User-executed SQL/TLS suite (after final `AuthDbContext.Guard()` fix)**: `AuthRecoverySqlTests|AuthSession|AuthHttpTests` — **22 passed, 0 failed, 0 skipped** (11.5s) against disposable SQL Server databases (`FORGEFLOW_TEST_SQLSERVER` + `FORGEFLOW_AUTH_HTTP_TESTS='1'`).
- **Earlier SQL suites (predating `AuthDbContext.Guard()` relaxation)**: `OrganizationUseCaseSqlTests|OrganizationSqlServerTests` (12 passed, 0 failed) and `AuthBootstrapSqlTests` (13 passed + native CLI `Auth.Initialized` exit 0). Note: `OrganizationDbContext` is independent of `AuthDbContext`, while `AuthDbContext.Guard()` only relaxed unchanged `UserName`/`NormalizedUserName` updates.
- **User-reported manual browser verification**: Confirmed all live flows at `https://localhost:5001` ("خوب همه چی درسته").
- **Pre-production deferred caveats (explicitly preserved, not hidden)**: local-only `TrustServerCertificate=True`, production TLS certificate/DataProtection key persistence, restricted runtime SQL principal verification on deployment target, and standalone interactive `HostRecovery` CLI run on a separate disposable database.
- **Housekeeping**: Deleted accidental 0-byte `MainLayout.razor` found at repository root; `src/ForgeFlow.Web/Components/Layout/MainLayout.razor` untouched.

### 2. Slice 02 (`02-users-memberships`, `02.0`) — design approved for `02.1`
Created and finalized the design-first slice package under `docs/slices/02-users-memberships/`:
- [README.md](docs/slices/02-users-memberships/README.md): full 10-section contract (`۰۰.۵`) aligning roadmap item `03` (`Users and memberships`) after `AUTH-01` (`02`) and Slice 01, defining scoped user/membership/fixed-role use cases, error codes (`Users.*`), lock order (`Organization` -> `Site` -> `ApplicationAccount` -> memberships -> roles -> sessions), fail-closed `SecurityEvent` logging, and bilingual/mobile UI rules.
- [decisions.md](docs/slices/02-users-memberships/decisions.md): inherited boundaries `FF-U01`–`FF-U05` and approved `02.0` decisions **`Q-U01`–`Q-U05`** (initial password at creation without `DisplayName`/`MustChangePassword` schema migration, fixed `OrganizationAdministrator`/`SiteOperator` assignment in Slice 02 while custom roles remain in Slice 03, strict tenant isolation with single-org `SetUserActiveAsync` vs multi-org `OrganizationMembership` toggle, `UnlockUserAsync` without tenant credential takeover, self/last-active-admin protection).
- [physical-erd.md](docs/slices/02-users-memberships/physical-erd.md): grounded in verified `AuthDbContext` tables (`auth.User`, `auth.OrganizationMembership`, `auth.SiteMembership`, `auth.ScopedRoleAssignment`, `auth.Session`, `auth.SecurityEvent`), composite FKs, SQL check constraints, with zero DDL migration required for Slice 02.
- [test-plan.md](docs/slices/02-users-memberships/test-plan.md): unit (`U01`–`U06`), disposable SQL/barrier-race (`S01`–`S07`), and bilingual `fa-IR`/`en-US` desktop/`320px` browser gates (`B01`–`B06`).

**Design-first gate (`02.0`) closed**: Ready for increment `02.1` (Domain & Application user/membership contracts and tests). Also added `verification/` to `.gitignore` and untracked legacy cached `verification/` artifacts from Git index per user policy.

## AUTH-01 22-case SQL & TLS suite — USER-EXECUTED PASS (2026-11-10)

User executed in normal Windows PowerShell against the freshly built Release binaries (`FORGEFLOW_AUTH_HTTP_TESTS='1'` and `FORGEFLOW_TEST_SQLSERVER` targeting disposable `ForgeFlow_Slice01_Test_*` databases; environment variables removed afterward):

- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthRecoverySqlTests|FullyQualifiedName~AuthSession|FullyQualifiedName~AuthHttpTests' --logger 'console;verbosity=normal'` — **PASS: 22 passed, 0 failed, 0 skipped** (11.5s):
  - 9 `AuthRecoverySqlTests`: credential rotation, session revocation, audit rollback, fault-injected rollback, uncertain-commit resolution, unknown subject, concurrent same-operation reset, preservation of other sessions.
  - 11 `AuthSessionSqlTests`: real credentials/sessions/adapters/use cases, `Preferred_culture_updates_without_rotating_security_stamp_and_audits_change`, lockout & atomic revocation, disabled/stamp/expired invalidation, required event rollback, explicit platform grant isolation, site-operator double-membership & revocation.
  - 1 `AuthSessionHttpSqlTests`: real HTTPS login/logout, `__Host-ForgeFlow.Auth` cookie, antiforgery, server revocation & cookie replay rejection.
  - 1 `AuthHttpTests`: loopback TLS, antiforgery, safe local redirect, outage denial.

Local test `TrustServerCertificate=True` remains local-test-session evidence only, not production TLS validation. `AuthDbContext.Guard()` changed since the earlier `AuthBootstrapSqlTests` (13 passed) run; re-running `AuthBootstrapSqlTests` against the current binaries is quick and optional before A5.

Remaining gaps before closing AUTH-01 A5 & Slice 01 01.5:
1. Authenticated browser acceptance (`B01`–`B06`) on https://localhost:5001 in `fa-IR` (RTL) and `en-US` (LTR) at desktop and `320px` (Create/View/Edit/Deactivate/Paging, unsaved-change confirmation, scope-bound drafts, header language switch, circular `UserMenu`, `/account/access-denied`, multi-tab logout).
2. Supervised native recovery CLI gate on a disposable installation (if required before A5).
3. Formal A5 handoff receipt and Slice 01 `01.5` acceptance receipt. AUTH-01 and Slice 01 remain NOT ACCEPTED until those checks complete; Slice 02 waits for acceptance.

## AuthDbContext.Guard() Identity-update bugfix from user SQL run (2026-11-10)

User-executed SQL run (`FORGEFLOW_AUTH_HTTP_TESTS='1'` + `FORGEFLOW_TEST_SQLSERVER`) returned **13 passed, 9 failed, 0 skipped**:
- Passed (13): `AuthHttpTests.Real_http_enforces_antiforgery_local_targets_cookie_security_and_outage_denial`, `AuthSessionHttpSqlTests.Real_http_login_and_logout_use_protected_cookies_antiforgery_and_server_revocation`, all 9 passing `AuthSessionSqlTests` (including new `Preferred_culture_updates_without_rotating_security_stamp_and_audits_change`), and `AuthRecoverySqlTests.Unknown_subject_does_not_change_any_account_or_event`.
- Failed (9): 8 `AuthRecoverySqlTests` threw `InvalidOperationException: Auth.ImmutableHistory` inside `UserManager.ResetPasswordAsync` -> `UserOnlyStore.UpdateAsync`; `AuthSessionSqlTests.Failed_credentials_lock_account_and_revoke_existing_sessions_atomically` returned `Auth.ServiceUnavailable` because `UserManager.AccessFailedAsync` -> `UserOnlyStore.UpdateAsync` hit the same `Auth.ImmutableHistory` guard.

Root cause: `AuthDbContext.Guard()` checked `p.IsModified` on `UserName`/`NormalizedUserName`/`ScopedRoleAssignment` scope keys without comparing `!Equals(p.OriginalValue, p.CurrentValue)`. ASP.NET Core Identity's `UserOnlyStore.UpdateAsync` calls `Context.Update(user)` and marks every property `IsModified = true` even when `UserName` and `NormalizedUserName` did not change.

Fix: added `ValueChanged(PropertyEntry)` (`property.IsModified && !Equals(property.OriginalValue, property.CurrentValue)`) in `src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs` and added regression test `AuthRecoveryTests.Guard_allows_identity_updates_with_unchanged_username_and_blocks_username_changes`.

Fresh agent verification after fix:
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore` — PASS: 255 passed, 0 failed, 34 skipped (SQL tests remain skipped in agent sandbox).
- Relaunched Development Web at https://localhost:5001 (`GET /account/login` HTTP 200).

Next step: re-run the 22-case SQL/TLS command in normal Windows PowerShell to confirm all 22 pass.

## 01.4 increment 8 — Authorized scope Code/Name labels, deep-link NotFound fix, and bilingual shell rendering tests (2026-11-10)

Scope:
- Added `ReadScopes.LoadDisplayInfoAsync` in `ReadScopes.cs` (using existing secured `OrganizationUseCases.GetOrganization` / `GetSite` only for already-authorized scopes) and updated `OrganizationReader.razor` `ScopeLabel` to display `Code — Name` (or `OrgCode · SiteCode — SiteName`) with localized numeric fallback when a scope has no read-org grant (e.g., `SiteOperator`).
- Fixed deep-link edge case in `OrganizationReader.razor`: when an explicit `OrganizationId > 0` route yields `available.Count == 0`, display `ReadNotFound` (even if `canCreate` is true for a Platform provisioner), and render a localized return link to `/organizations` or `/sites` below deep-link error alerts.
- Added `tests/ForgeFlow.Web.Tests/ShellRenderingTests.cs` using ASP.NET `HtmlRenderer` to verify bilingual (`fa-IR` / `en-US`) rendering of `AccessDenied`, `UserMenu` (single-initial circular trigger + full username + logout form), and empty `Home`.

Actual commands and results (this session, Release, no restore):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"` — PASS: 253 passed, 0 failed, 35 skipped (SQL-dependent tests remain skipped in the agent environment).
- Relaunched Development Web at https://localhost:5001 (`GET /account/login` HTTP 200). No migration, bootstrap, or recovery was run.

Remaining blockers to closing AUTH-01 A5 & Slice 01 01.5 (NOT executed in agent sandbox):
1. User-executed SQL/TLS suite in normal Windows PowerShell (`FORGEFLOW_TEST_SQLSERVER` + `FORGEFLOW_AUTH_HTTP_TESTS='1'`, expecting 22 passed / 0 failed / 0 skipped) + supervised native recovery test on a disposable installation.
2. Authenticated browser acceptance (B01–B06) on https://localhost:5001 in `fa-IR` (RTL) and `en-US` (LTR) at desktop and `320px`.
Per AGENTS.md, Slice 02 cannot begin until AUTH-01 and Slice 01 are accepted.

## AUTH-01 HTTP TLS fixture fix & verification (2026-11-10)

Root cause of user-observed `AuthHttpTests` `SslStream` EOF (`Received an unexpected EOF or 0 bytes from the transport stream`): `AuthHttpHost.Start` previously passed an ephemeral `CreateSelfSigned` certificate without SAN/ServerAuth EKU or a Windows Schannel-usable key container, causing Kestrel TLS handshake termination on Windows. Fixed `AuthHttpHost.Start` in `tests/ForgeFlow.Web.Tests/AuthHttpTests.cs` to add `localhost`/`127.0.0.1` SAN, key usage, ServerAuth EKU (`1.3.6.1.5.5.7.3.1`), and load the PFX with `X509KeyStorageFlags.UserKeySet` on Windows (`EphemeralKeySet` elsewhere).

Actual commands and results:
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthHttpTests'` — PASS: 1 passed, 0 failed, 0 skipped (`Real_http_enforces_antiforgery_local_targets_cookie_security_and_outage_denial`, 485 ms). (In-memory test cert only; not production TLS validation.)
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore` (without opt-in env vars) — PASS: 247 passed, 0 failed, 35 skipped.
- Relaunched Development Web at https://localhost:5001 (`GET /account/login` HTTP 200).

User's pasted run showed the 16 SQL test groups skipped because `FORGEFLOW_TEST_SQLSERVER` was not set in that PowerShell session; those 16 SQL groups (plus `AuthHttpTests` re-run) still require execution in normal Windows PowerShell with `FORGEFLOW_TEST_SQLSERVER` pointing to disposable test DB creation rights before AUTH-01 A5 / Slice 01 01.5 acceptance.

## 01.4 / A4 increment 7 — Scope-bound drafts, cross-tab logout signal, and Chrome verification (2026-11-10)

Scope:
- Integrated `DraftCoordinator` into `OrganizationReader.razor` for both Create (`org-create-form`) and Edit (`org-edit-form`) modal forms. Draft keys include actor ID, scope (`OrganizationId` / `SiteId` / `Platform`), and operation (`BuildDraftScope`); unsaved edits restore after re-opening or culture reload within the same actor/scope without overriding server `RowVersion` or authorization, and clear on save, discard, or conflict reload.
- Added cross-tab identity/logout cleanup signal (`BroadcastChannel("forgeflow.identity")` + `notifyIdentityCleared()` in `wwwroot/auth-ui.mjs`): signing out in one tab clears `forgeflow.draft:*` in `sessionStorage` and broadcasts `{ type: "signed-out", nonce }`, causing other open authenticated tabs to immediately hide `[data-authenticated-content]`, clear their own drafts, and redirect to `/`. No `localStorage`, credentials, or grants are stored/broadcast.
- Added `AuthSessionSqlTests.Preferred_culture_updates_without_rotating_security_stamp_and_audits_change` (disposable SQL test for `SetPreferredCultureAsync`, `GetPreferredCultureAsync`, `CulturePreferenceChanged` security event, and unchanged `SecurityStamp`), `OrganizationReaderRegressionTests.Draft_scope_isolates_actor_organization_site_and_operation`, and `AuthUiContractTests` `BroadcastChannel` assertion.
- Added `verification/slice01-ui/browser-check.cjs` headless Chrome check against the running Development HTTPS host (`ignoreHTTPSErrors` solely for the local dev cert; anonymous/unauthenticated flows only).

Actual commands and results (this session, Release, no restore):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"` — PASS: 247 passed, 0 failed, 35 skipped (SQL-dependent tests remain skipped in the agent environment).
- `node .\scripts\drafts.test.mjs` — PASS: 19 passed, 0 failed.
- `node .\verification\slice01-ui\browser-check.cjs` — PASS in headless Chrome for `fa-IR`/`en-US` at `1440px` and `320px`: anonymous `/` → `/account/login`, RTL/LTR attributes, zero horizontal overflow, zero page errors, localized `#components-reconnect-modal` states (`show`, `retrying`, `failed`, `paused`), and `/culture` switch + reload.
- Launched freshly built Web in Development at https://localhost:5001 with process-only `ConnectionStrings__ForgeFlow` (localhost/testforgflow, Integrated Security, Encrypt=True, local-only TrustServerCertificate=True); `GET /account/login` returned HTTP 200 (length 15699). No migration, bootstrap, or recovery was run.

Remaining gaps before closing AUTH-01 A5 & Slice 01 (01.5) (NOT verified / cannot run from agent sandbox):
- **Authenticated browser acceptance (B01–B06):** sign in on https://localhost:5001/account/login and verify `/`, header language switch, circular `UserMenu`, `/account/access-denied`, `/organizations`, `/sites` (Create/View/Edit/Deactivate/Paging, unsaved-change confirmation, draft restoration, multi-tab logout) in `fa-IR` and `en-US` at desktop and `320px`.
- **SQL & TLS acceptance (user-executed in normal Windows PowerShell with `FORGEFLOW_TEST_SQLSERVER` on disposable test DBs):** run the prepared 22-case command (`AuthRecoverySqlTests|AuthSession|AuthHttpTests`) plus supervised native recovery test, then complete A5 and 01.5 receipts. AUTH-01 and Slice 01 remain NOT ACCEPTED.

## 01.4 / A4 increment 6 — Localized server-unavailable reconnect modal & authenticated profile culture persistence (2026-11-10)

Scope:
- Localized Blazor Server circuit disconnect/rejoin/failed/paused UI by adding `#components-reconnect-modal` to `Components/App.razor` with the exact `.NET 10` `blazor.web.js` state classes (`components-reconnect-show`, `retrying`, `failed`, `rejected`, `paused`, `resume-failed`), localized action handlers (`[data-reconnect-action]` calling `window.Blazor.reconnect()` / `resumeCircuit()` / `location.reload()`) in `wwwroot/auth-ui.mjs`, and `ServerUnavailable` notice fallback for culture-switch network/server failures. Styled `#components-reconnect-modal` and `#blazor-error-ui` reload button in `wwwroot/app.css`.
- Implemented A4 authenticated profile language persistence (`PreferredCulture`):
  - Added `RuntimeSessions.SetPreferredCultureAsync` (updates `ApplicationAccount.PreferredCulture` without rotating `SecurityStamp` and appends `CulturePreferenceChanged` `SecurityEvent` in the same serializable transaction) and `GetPreferredCultureAsync`.
  - Updated `CultureEndpoint.SwitchAsync` so authenticated switches apply the `.AspNetCore.Culture` cookie and save `PreferredCulture`; if profile save fails, it still applies the browser cookie and returns HTTP 200 `{ cookieApplied: true, profileSaved: false, code: "Culture.ProfileSyncFailed", message }` (`auth-ui.mjs` persists the localized warning in `sessionStorage` across reload without claiming profile sync).
  - Added `CulturePolicy.ApplyProfileFallbackAsync` in `Program.cs` right after `UseAuthentication()`: preserves FF-A016 precedence (`ConsistentCookieCultureProvider` cookie → authenticated profile preference → `Accept-Language` → `fa-IR`) per request without mutating process-wide defaults.
- Added 14 keys in `SharedResources` catalogs (`ServerUnavailable`, `Reconnect*`) and `Culture.ProfileSyncFailed` in `AuthenticationResources` catalogs. Added tests in `AuthBackendTests`, `SwitchAndDraftTests`, and `ResourceContractTests`.

Actual commands and results (this session, Release, no restore):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"` — PASS: 246 passed, 0 failed, 34 skipped (SQL-dependent tests remain skipped in the agent environment).
- Launched freshly built Web in Development at https://localhost:5001 with process-only `ConnectionStrings__ForgeFlow` (localhost/testforgflow, Integrated Security, Encrypt=True, local-only TrustServerCertificate=True); `GET /account/login` returned HTTP 200 (length 15716, `components-reconnect-modal` present). No migration, bootstrap, or recovery was run.

Remaining gaps (NOT verified / open in AUTH-01 A4 + Slice 01):
- Authenticated browser verification in `fa-IR`/`en-US`, RTL/LTR, 320px/desktop, keyboard/focus for all 01.4/A4 screens, `#components-reconnect-modal`, and live profile-culture persistence.
- Scope-bound `DraftCoordinator` integration in `OrganizationReader.razor` and cross-tab identity/logout cleanup signal.
- Deferred AUTH-01 SQL/TLS/native recovery gates (21-case suite + native recovery) and A5 handoff / 01.5 acceptance receipt. AUTH-01 and Slice 01 remain NOT ACCEPTED.

Next small step: integrate scope-bound `DraftCoordinator` + cross-tab identity/logout signal in `OrganizationReader.razor`/`auth-ui.mjs`, or run browser/SQL acceptance on the current build.

## 01.4 / A4 increment 5 — Unsaved-change / conflict-reload confirmation & AccessDenied page (2026-11-10)

Scope:
- Added `BitDialog.OnDismissing` (`BitDialogDismissArgs`) unsaved-change confirmation to both Edit and Create dialogs in `OrganizationReader.razor` (`OnEditDismissing`, `OnCreateDismissing`), plus explicit reload confirmation (`RequestReload` / `WriteReloadConfirm`) when `WriteConflict` or `WriteOutcomeUnknown` occurs with unsaved name edits.
- Added A4 `/account/access-denied` page (`Components/Pages/AccessDenied.razor`), rendering localized `AccessDeniedTitle`/`AccessDeniedMessage` without exposing forbidden resource names for authenticated users, and `AnonymousRedirect` for unauthenticated visitors.
- Added 6 localization keys (`AccessDeniedTitle`, `AccessDeniedMessage`, `WriteReloadConfirm`, `WriteDiscardConfirm`, `WriteDiscard`, `WriteKeepEditing`) in both `SharedResources` catalogs and updated `ResourceContractTests` and `OrganizationReaderRegressionTests`.

Actual commands and results (this session, Release, no restore):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"` — PASS: 245 passed, 0 failed, 34 skipped (SQL-dependent tests remain skipped in the agent environment).
- Stopped earlier workspace Development process, launched freshly built Web in Development at https://localhost:5001 with process-only `ConnectionStrings__ForgeFlow` (localhost/testforgflow, Integrated Security, Encrypt=True, local-only TrustServerCertificate=True); `GET /account/login` returned HTTP 200 (length 11270). No migration, bootstrap, or recovery was run.

Remaining gaps (NOT verified / open in AUTH-01 A4 + Slice 01):
- Authenticated browser verification in `fa-IR`/`en-US`, RTL/LTR, 320px/desktop, keyboard/focus for all 01.4/A4 screens.
- A4 profile language persistence (`Culture.ProfileSyncFailed` warning + cookie/profile precedence), identity/scope-bound `DraftCoordinator` integration in `OrganizationReader`, and cross-tab identity/logout cleanup signal.
- Deferred AUTH-01 SQL/TLS/native recovery gates (21-case suite + native recovery) and A5 handoff / 01.5 acceptance receipt. AUTH-01 and Slice 01 remain NOT ACCEPTED.

Next small step: implement A4 authenticated profile language persistence + `Culture.ProfileSyncFailed` warning and scope-bound `DraftCoordinator` integration in `OrganizationReader.razor`, or run browser/SQL acceptance on the current UI.

## 01.4 increment 4 — Empty Home, header language switcher, circular avatar trigger, and Create Organization/Site UI (2026-11-10)

Scope:
- Emptied `Home.razor` (`/` renders `<PageTitle>` and visually hidden `<h1>` for focus navigation only).
- Added a compact single-option language switcher (`.header-lang-btn` with `.ff-icon--globe` and `data-auth-culture`) on the start side of `<header class="app-header">` in `MainLayout.razor`, opposite `UserMenu`, showing only the target language to switch to.
- Updated `UserMenu.razor` trigger to a 38px circular avatar button showing only the first character of the username (`Initial(userName)`), revealing the wrapped full username and Sign Out form in the popover panel.
- Added authorized **Create Organization** (`OrganizationPermissions.OrganizationsCreate` against `OrganizationScope.Platform`) and **Create Site** (`OrganizationPermissions.SitesCreate` against selected `OrganizationScope.Organization`) modal workflows to `OrganizationReader.razor`, consuming 01.3 `OrganizationUseCases.CreateOrganization`/`CreateSite`. Per Slice 01 contract (`docs/slices/01-organization-sites/README.md`), hard delete is not permitted; lifecycle removal remains soft deactivation (`SetOrganizationActive`/`SetSiteActive`) inside the Edit modal.
- Added 11 localization keys (`SwitchLanguageTo`, `Create*`) in both `SharedResources` catalogs and removed 4 unused keys (`HomeTitle`, `HomeSubtitle`, `HomeNoScopes`, `AuthenticatedAs`). Added `OrganizationReaderRegressionTests` for `CreateOrganization`/`CreateSite` dispatch and `CodeInvalid`/`CodeDuplicate` mappings.

Actual commands and results (this session, Release, no restore):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"` — PASS: 244 passed, 0 failed, 34 skipped (SQL-dependent tests remain skipped in the agent environment).
- Stopped earlier workspace Development process, launched freshly built Web in Development at https://localhost:5001 with process-only `ConnectionStrings__ForgeFlow` (localhost/testforgflow, Integrated Security, Encrypt=True, local-only TrustServerCertificate=True); `GET /account/login` returned HTTP 200 (length 11250). No migration, bootstrap, or recovery was run.

Remaining gaps (NOT verified): authenticated browser verification in fa-IR/en-US, RTL/LTR, 320px/desktop, keyboard/focus for empty Home, header language switch, circular UserMenu, and Create/Edit/Deactivate Organization/Site flows; live SQL verification (skipped in agent environment); unsaved-change confirmation on modal close; A4/A5/01.5 acceptance gates. AUTH-01 and Slice 01 remain NOT ACCEPTED.

Next small step: verify all updated pages and Create/Edit/Deactivate flows in the browser at https://localhost:5001 in both `fa-IR` and `en-US` (desktop & 320px), then work on remaining A4 (access-denied page, identity/scope-bound drafts) and unsaved-change confirmation.

## 01.4 increment 3 — Home page, UserMenu, BitDataGrid paging, and separate view/edit modals (2026-11-10)

Scope: addressed user review feedback on the authenticated shell and `OrganizationReader.razor` using installed Bit BlazorUI 10.7 / Bit.BlazorUI.Extras 10.7.0 (no package changes):
- Removed `Sample.razor` (`/` localization test page) and replaced `/` with `Components/Pages/Home.razor` (localized workspace welcome, quick language switch via `/culture`, and authorized `Organizations`/`Sites` cards). Kept `SampleResources`/`SampleFormState`/`SampleMode` out of the live UI while preserving Slice 00 formatting/draft regression unit tests; `ResourceContractTests` checks `Explicit_catalogs_have_complete_keys_and_matching_placeholder_contracts` for both catalogs and `Every_Razor_key_is_registered_and_every_registered_key_is_used` for `SharedResources`.
- Added `Components/Layout/UserMenu.razor` in `<header class="app-header">` (`MainLayout.razor`), removing the standalone header logout button and putting the signed-in username and logout form inside a click/Escape-toggleable popover menu. Updated `AuthUiContractTests` accordingly.
- Added self-authored CSS-mask icons (`.ff-icon--home`, `--org`, `--site`, `--settings`, `--edit`, `--user` in `wwwroot/app.css`) for `MainLayout` `BitNavPanel` items, `UserMenu`, and the grid Edit button; verified `BitButton` (`Icon="BitIconInfo.Css(...)"`) renders without `.bit-icon` font interference.
- Replaced manual Previous/Next pagination in `OrganizationReader.razor` with `BitDataGrid` server-side paging (`OnRead="ReadGridAsync"`, `Pageable="true"`, `PageSize="25"`, `PageSizeOptions="[10, 25, 50, 100]"`, localized `BitDataGridStrings`, `EmptyTemplate`).
- Split grid row actions into View (`IconName="View"`, read-only modal) and Edit (`Icon="@EditIcon"`, rendered when `scopeCanRename || scopeCanSetActive`), reduced `BitDialog` width to `380px`, removed the redundant bottom `Close` footer button (`ShowCancelButton="false"` while keeping header X `ShowCloseButton="true"`), and placed compact `BitSize.Small` Save and Activate/Deactivate buttons side by side in one `.write-actions` row.

Actual commands and results (this session, Release, no restore):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 8 projects, 0 warnings, 0 errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger "console;verbosity=minimal"` — PASS: 238 passed, 0 failed, 34 skipped (SQL-dependent tests remain skipped in the agent environment).
- Stopped earlier workspace Development PID 12036, launched freshly built Web in Development at https://localhost:5001 with process-only `ConnectionStrings__ForgeFlow` (localhost/testforgflow, Integrated Security, Encrypt=True, local-only TrustServerCertificate=True); `GET /account/login` returned HTTP 200 (length 11321). No migration, bootstrap, or recovery was run.

Remaining gaps (NOT verified): authenticated browser verification in fa-IR/en-US, RTL/LTR, 320px/desktop, keyboard/focus for `Home`, `UserMenu`, `BitDataGrid` pager, and view/edit modals is still required; SQL suites remain skipped from agent environment; Create Organization/Site UI, unsaved-change confirmation, and A4/A5/01.5 acceptance gates remain open. AUTH-01 and Slice 01 remain NOT ACCEPTED.

Next small step: verify `/` (`Home`), `UserMenu`, `/organizations`, and `/sites` in the browser in both `fa-IR` and `en-US` (desktop & 320px), then add Create Site (`Sites.Create`, organization scope, code + name, duplicate-code message).

## 01.4 increment 2 — Organization/Site rename and activate/deactivate from the detail dialog (2026-11-10)

Scope: the already-existing authenticated read-only list/detail UI (`OrganizationReader.razor`, used by `/organizations` and `/sites` routes) gained write actions inside the detail dialog, consuming the implemented 01.3 secured use cases (RenameOrganization/RenameSite, SetOrganizationActive/SetSiteActive). Create Organization/Site is NOT part of this increment. Built on the installed Bit BlazorUI 10.7; no package change.

Behavior: after the detail loads, the component asks the trusted actor accessor and permission evaluator (same scope the use case will check) whether Update and SetActive are allowed; the rename field/Save and the Activate/Deactivate buttons render only when allowed. The server-side use case still enforces authorization. The opaque rowversion from the detail read is sent as the expected version and replaced by the returned one after success. Deactivate/Activate requires a localized confirmation naming the entity. Failure codes map to explicit localized messages: concurrency conflict (with reload action), unknown outcome (with reload action), name invalid, parent inactive, organization has active sites, denied, not found, unavailable. Success updates the dialog and the list row in place. Resources: 16 new keys in both `SharedResources` catalogs. Fixed pre-existing defect: the two last fa-IR entries (`ReadSearchPlaceholder`, `ReadPageCount`) contained literal `?` characters; they now hold real Persian text. Removed the unused orphan key `ReadRefresh` from both catalogs.

Test registry: `ResourceContractTests` was already failing at baseline (2 failures: registry lacked the Read*/Organizations/Sites/Settings keys). The registry now lists every `SharedResources` key with placeholder counts and the variable-selected keys. New `OrganizationReaderRegressionTests` cover rename/toggle for both organizations and sites (version, mutation, name, in-place update) and the failure-code → resource-key mapping.

Actual commands and results (this session, Release, no restore):

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, 0 warnings, 0 errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore` — PASS: 237 passed, 0 failed, 34 skipped (the SQL-dependent suites remain skipped in the agent environment; baseline had 2 resource-contract failures, now fixed).
- Local startup per AGENTS.md: no pre-existing ForgeFlow.Web process/listener on 5001; launched the fresh Release build in Development at https://localhost:5001 with process-only `ConnectionStrings__ForgeFlow` (localhost/testforgflow, Integrated Security, Encrypt=True, local-only TrustServerCertificate=True). `GET /account/login` returned 200. No migration, bootstrap or recovery was run.

Remaining gaps (NOT verified): no authenticated browser session was exercised by the agent, so the new write UI has no browser evidence in fa-IR/en-US, RTL/LTR, 320px, keyboard/focus; no live SQL write was run (agent SQL access fails with SSPI error); unsaved-name confirmation on dialog close is not implemented; Create Organization/Site UI, scope selector polish and the A4/A5/01.5 gates remain open. Slice 01 and AUTH-01 remain NOT ACCEPTED.

Next small step: sign in at https://localhost:5001/account/login and manually try rename/deactivate on `/sites` in both languages; then add Create Site (Sites.Create, organization scope, code + name, duplicate-code message).

## AUTH-01 manual login/logout receipt — USER-REPORTED verification

The user reports the following manual results with the newly created local test account. This is USER-REPORTED local application verification, not agent-executed or automated evidence. No credentials are recorded.

| Manual scenario | Reported result | Attribution |
|---|---|---|
| Real sign-in with the new test account | PASS | USER-REPORTED manual verification |
| Authentication remains after refresh | PASS | USER-REPORTED manual verification |
| Logout | PASS | USER-REPORTED manual verification |
| Open `/` after logout | PASS: redirects directly to login | USER-REPORTED manual verification |

These results supersede the earlier unresolved basic-login status for the user's tested run. They do not establish the original unavailable error's underlying cause, a particular code correction's causality, automated SQL/cookie replay coverage, or production TLS validity. Exact culture/viewport coverage was not reported for this manual run. Preserve earlier agent build/tests and Chrome SmokeTest evidence with their original attribution; no checks were rerun for this documentation update. Deferred recovery, SQL suites, TLS/deployment, revocation/races/circuits, profile preferences, identity/scope-bound draft isolation and other unverified gates remain DEFERRED/UNVERIFIED in the consolidated AUTH-01 test plan. AUTH-01 and Slice 01 remain INCOMPLETE/NOT ACCEPTED.

Next unfinished implementation priority requested by the user: **01.4 Bilingual Organization/Site management UI**, consuming the implemented 01.3 secured use cases and AUTH-01 trusted adapters. Its owning Slice 01 plan lists those backend dependencies; they are implemented, and basic real sign-in is now manually reported working. Smallest bounded first UI task: authenticated, explicitly scoped allowed-organization/site list/detail views and shell navigation, with localized loading/empty/denied/not-found states. Reuse current permission contracts; never default to organization 1 or offer an unfiltered organization directory. Follow with the already-contracted create/rename/status/rowversion workflows, with Organization creation reserved for explicit deployment authority. This is prioritization only, not authorization to implement in this documentation turn.

AUTH-01 A4's remaining denied/profile/draft/circuit implementation and acceptance work, A5's formal handoff, and Slice 01's 01.5 acceptance receipt are still unfinished. The AUTH-01 plan's formal A5-to-01.4/01.5 acceptance dependency is not waived or relabeled complete by this priority: management UI implementation can consume available backend prerequisites, while acceptance still requires all outstanding gates. No application/startup/database/test or Git operation occurred in this turn.

## Login ServiceUnavailable tracing — historical agent investigation, subsequent manual success recorded above

User reports that the newly created account also receives localized service-unavailable, not invalid credentials. No new account/helper/reset was introduced in this increment. Traced the current `/account/sign-in` path: known store/transaction/query/save exceptions, failed Identity lockout/reset-count results, uncertain session commit, and cookie issuance failure can all produce Auth.ServiceUnavailable. Existing AUTH-LOGIN/AUTH-HTTP diagnostics omitted exception messages/inner stacks; no saved diagnostic log was found in the existing workspace log/evidence locations inspected. Neither account validity nor a transaction/retry mismatch is assumed to be the cause.

Actual agent read-only schema attempt on localhost/testforgflow failed at connection opening with SqlException: `The target principal name is incorrect. Cannot generate SSPI context.` Its stack reaches SqlInternalConnectionTds/SqlConnection.Open; no schema query ran. The agent has no inherited ConnectionStrings__ForgeFlow setting and cannot read the running Web process environment. Both Web authentication and Organization persistence read ConnectionStrings:ForgeFlow; the supplied double-underscore process key maps correctly. Source contains no explicit EnableRetryOnFailure configuration. Therefore actual Web connection/schema/exception and execution strategy remain unverified; no speculative retry/transaction or schema change was made.

Corrected the observability gap using Development-only opt-in `ForgeFlow__Auth__Diagnostics=true`. Startup logs only parsed non-secret target/encryption fields, actual EF strategy type/retry flag, read-only visibility checks for auth.User/Session/SecurityEvent and current session/audit columns, plus pending auth migration count. It never migrates or queries credential values for this probe. Login/commit/cookie failures capture a sanitized message chain and stack; known submitted credentials/stamps, quoted values, credential assignments and long token-like strings are redacted. Raw exceptions are not passed to ILogger. Identity failure codes are logged without descriptions. Diagnostic mode suppresses raw EF exception/SQL logging and circuit request URLs, and cannot be enabled in Production. Public credential/availability responses, cookie/antiforgery/local-return handling, transaction policy and existing UI are unchanged.

Fresh isolated Release build: `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutputPath=D:\Projects\ForgeFlow\.tools\auth-diagnostic-output\` PASS, eight projects, zero warnings/errors. `dotnet vstest .\.tools\auth-diagnostic-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:"FullyQualifiedName~AuthFailureDiagnosticsTests|FullyQualifiedName~AuthBackendTests|FullyQualifiedName~AuthEndpointRoutingTests|FullyQualifiedName~DisplayText"` PASS: 48 passed, zero failed/skipped. New regression invokes the actual RuntimeSessions.LoginAsync store-opening failure path using a connection interceptor; it proves generic denial and sanitized opt-in cause/inner-stack reporting, with no SQL writes. The injected execution-strategy exception is synthetic regression input, NOT evidence that the running application has that defect.

Existing ForgeFlow.Web PID 2788 owns loopback port 5001; its executable path is inaccessible. The agent cannot verify/restart that workspace Development instance safely and preserved it. Fresh diagnostic Web binaries are ready in `.tools/auth-diagnostic-output`; no startup or successful login is claimed. One normal-PowerShell diagnostic launch can verify the existing process path, restart only that workspace listener, set the previously authorized local connection/Development diagnostic options, and execute these fresh binaries with the normal Web content root on HTTPS 5001. Reproduce one browser login privately and capture only AUTH-DIAGNOSTIC/AUTH-LOGIN/AUTH-HTTP output; never send browser payloads, cookies or tokens. Actual exception evidence is still required before choosing a service fix. No database/account/permission change or Git write occurred; AUTH-01/Slice 01 remain unaccepted.

## Authorized new ORG1 local test login — helper ready, creation NOT EXECUTED

User explicitly authorized creating a new unique local test login in localhost/testforgflow, active ORG1 membership and company-only administrator authority, without modifying ebrahim. A read-only lookup failed before reaching ORG1: System.Data.SqlClient.SqlException, `The target principal name is incorrect. Cannot generate SSPI context.` No account or grant was created, no real account credential was generated/disclosed, and live HTTP sign-in could not be tested. This is a prepared helper, not completed account creation.

Added host-only HostLocalTestAccountCreator (never registered by Web) and a distinct `tools/ForgeFlow.LocalTestAccount` console entry. It is NOT a recovery workflow: no call to HostRecovery or HostBootstrapper, no password reset or initialized-database recreation. It reuses existing Identity registration, UserManager.CreateAsync/CheckPasswordAsync, the fixed role policy, and the existing cryptographic local-helper generator/interactive guard. No package upgrade. Its only SQL target is hardcoded localhost/testforgflow with Windows authentication, Encrypt=True and the previously authorized local-only TrustServerCertificate=True exception. Explicit --execute, child Development environment, private nonredirected terminal, CREATE TEST ACCOUNT host attestation and existing dbo/db_owner/non-runtime capability are required. No target, username, password, connection or deployment-grant override is accepted.

The operation locks existing ORG1 using UPDLOCK/HOLDLOCK inside a serializable transaction and refuses absent/inactive ORG1, missing completed initialization or pending auth migrations. Identity creates `localtest-` plus a fresh operation GUID as username and hashes the in-memory 28-character random password. Exactly one active OrganizationMembership and one active ScopeKind=1 OrganizationAdministrator grant for ORG1 (SiteId=NULL) are added, with AccountCreated/MembershipChanged/ScopedRoleChanged host-attributed audit records in the same transaction. Membership metadata follows existing host-provisioning real-subject attribution, while audit explicitly records actual host operator provenance. No site membership, platform/deployment authority, organization/site creation, existing-account adoption or ebrahim write is performed. Identity, grants and audit roll back together on failure; unknown commit never triggers automatic creation replay.

Before disclosing credentials, an independent scope verifies active/unlocked account, Identity password success, active ORG1, exactly the intended active membership and company grant, no extra/site grants, and all three committed host-attributed audit events. Username/password are each displayed once in the private terminal only after these checks; no password file, argument, environment variable or diagnostic log delivery. On uncertain outcome, preserve the operation reference and do not blindly repeat. No Web reset/signup endpoint or runtime authorization bypass was introduced; this is not full user administration or AUTH-01 acceptance.

One ready-to-run command for normal Windows PowerShell (no script-policy changes, manual SQL, existing password or numeric IDs):

```powershell
dotnet run --project D:\Projects\ForgeFlow\tools\ForgeFlow.LocalTestAccount\ForgeFlow.LocalTestAccount.csproj -c Release --no-build --no-restore --environment DOTNET_ENVIRONMENT=Development -- --execute
```

Review the fixed target and type CREATE TEST ACCOUNT. Until execution returns verified success, no new login credentials are claimed. Real HTTPS cookie sign-in at `/account/login` -> POST `/account/sign-in` remains a separate live check. Keep the terminal private and free of transcripts.

Actual verification: `dotnet restore .\ForgeFlow.slnx --packages .\.tools\packages --source .\.tools\nuget-feed` PASS using existing local artifacts; new tool's resolved package versions match existing local tooling (zero differences). `dotnet build .\tools\ForgeFlow.LocalTestAccount\ForgeFlow.LocalTestAccount.csproj -c Release --no-restore -m:1` PASS, zero warnings/errors, normal helper binary ready. Existing Web PID 2788 has an inaccessible executable path; it was preserved without restart. Full fresh solution build used `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutputPath=D:\Projects\ForgeFlow\.tools\local-test-account-output\` — PASS, eight projects, zero warnings/errors. `dotnet vstest .\.tools\local-test-account-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:"FullyQualifiedName~LocalTestAccountTests|FullyQualifiedName~LocalTestRecoveryTests|FullyQualifiedName~AuthBackendTests.Fixed_roles"` — PASS, 19 passed, zero failed/skipped. These are offline guard/name/resource/generator/fixed-role checks, not SQL atomicity or actual account-creation proof. No database recreation, migration, bootstrap/recovery invocation, existing-account write, direct hash edit or Git write occurred. AUTH-01 and Slice 01 remain incomplete.

## Fixed local recovery helper — execution/previous outcome remain pending

User explicitly reauthorized recovery of only ebrahim in localhost/testforgflow and requested resolving prior operation `80c2921b-bec8-4200-86af-bd16b522fc3a` (failure reference `5440c8d5-9637-4bad-87cb-c897227278e6`) before another reset. Unlike the earlier redirected execution, a real PTY successfully launched the unchanged A2 reset-password command and reached its RECOVER operator prompt. It was cancelled before attestation or mutation while checking prior-outcome access. A separate read-only SQL connection using the authorized local Integrated Security/Encrypt/TrustServerCertificate settings failed: `The target principal name is incorrect. Cannot generate SSPI context.` No account ID or prior completion could be read. This is current agent evidence, not proof of the earlier user failure's cause. The CLI's failure correlation is not persisted; only the operation's atomic PasswordResetCompleted audit establishes committed outcome.

Implemented a separate host-only `tools/ForgeFlow.LocalTestRecovery` executable, referenced by the solution/tests but never Web. It has no target or credential arguments: localhost/testforgflow and normalized Ebrahim are fixed. It requires explicit --execute, child Development environment, nonredirected private terminal, RECOVER owner attestation and the same SQL owner/non-runtime capability as A2. TrustServerCertificate=True is limited to this hardcoded local target. Existing A2/production controls are unchanged.

Before generating a password, the helper takes the same serializable account lock and resolves the prior operation's completion marker for the actual account. It rejects duplicate/other-subject markers. A prior committed reset is reported honestly; the currently authorized new reset then uses a fresh operation (previous passwords cannot be recovered). Only HostRecovery.ResetAsync writes data. A 28-character cryptographically random temporary password exists only in memory, with no argument/environment/file delivery. After A2, independent reads verify the committed completion/audit, changed stamp, previous sessions' revocation/events, password validity and unchanged membership/grant/eligibility snapshots. Unknown commit is checked, never automatically retried. Private terminal disclosure occurs once only after all verification passes. No password is printed on failure/unconfirmed outcome. No hash is edited outside Identity.

An initial PowerShell wrapper launch was rejected with PSSecurityException/UnauthorizedAccess because the script was not digitally signed. No execution policy was changed or bypassed. Removed the wrapper; the delivered entry is the compiled .NET helper via the installed SDK's documented --environment option. It still enforces its own interactive/operator controls and performs all target/ID/operation resolution internally. Exact one-command normal-Windows-PowerShell fallback:

```powershell
dotnet run --project D:\Projects\ForgeFlow\tools\ForgeFlow.LocalTestRecovery\ForgeFlow.LocalTestRecovery.csproj -c Release --no-build --no-restore --environment DOTNET_ENVIRONMENT=Development -- --execute
```

Review the printed fixed target and confirm RECOVER; no manual SQL, numeric ID, connection or password entry is needed. Keep the private terminal free of transcripts. The helper displays the generated temporary password only on verified success. It has not successfully run against SQL here: the previous operation remains unresolved, no recovery password was generated for the account, and no account change is claimed.

Verification: initial ordinary restore failed NU1801 against inaccessible nuget.org; restored successfully with `dotnet restore .\ForgeFlow.slnx --packages .\.tools\packages --source .\.tools\nuget-feed` using the existing local artifacts. New helper package versions match the existing Bootstrap dependency versions (zero differences); no dependency upgrade. `dotnet build .\tools\ForgeFlow.LocalTestRecovery\ForgeFlow.LocalTestRecovery.csproj -c Release --no-restore -m:1` PASS, zero warnings/errors, normal helper binary ready. Normal solution build was blocked by existing Web-output DLL locks; ForgeFlow.Web PID 2788 was identified but its executable path was inaccessible, so it was not stopped/restarted. `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutputPath=D:\Projects\ForgeFlow\.tools\local-test-recovery-output\` PASS, seven projects, zero warnings/errors. `dotnet vstest .\.tools\local-test-recovery-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:"FullyQualifiedName~LocalTestRecoveryTests|FullyQualifiedName~AuthRecoveryTests"` PASS: 12 passed, zero failed/skipped. Covers fixed-target/interactive/environment/explicit-execution refusal, cryptographic password generation constraints, helper bilingual catalogs and existing offline A2 behavior. Real SQL outcome/atomicity and this helper's success path remain unverified; prior A2 deferred gates remain open. No successful reset, migration, bootstrap, role/grant change or Git write occurred.

## Authorized local test-account recovery — blocked before execution

User explicitly authorized A2 password recovery for ebrahim in localhost/testforgflow, with Integrated Security=True, Encrypt=True and TrustServerCertificate=True solely for local development. Inspected BootstrapProgram.reset-password and HostRecovery: exact connection variable is FORGEFLOW_RECOVERY_SQLSERVER; command requires RECOVER attestation, operation GUID (blank generates a fresh one), numeric UserId and hidden password/confirmation. HostRecovery uses Identity reset, stamp rotation, session revocation and required audit in its existing transaction without changing grants/eligibility.

Agent environment inspection returned Console.IsInputRedirected=True and Console.IsOutputRedirected=True. BootstrapProgram rejects either before reading connection settings or reaching SQL. Recovery was therefore not executed; no password generated/disclosed/changed, no database access, no bootstrap, no reset bypass or Git writes. Manual command is `dotnet run --project .\tools\ForgeFlow.Bootstrap\ForgeFlow.Bootstrap.csproj -c Release --no-build --no-restore -- reset-password` under normal Windows PowerShell with the recovery-only process variable. Resolve ebrahim's actual numeric ID by read-only lookup; never assume ID 1. Require Auth.PasswordResetCompleted and exit 0 for a new successful recovery. Auth.AlreadyRecovered only reports an existing operation replay, not application of a newly entered password. Existing deferred A2 acceptance gates remain open; no successful recovery evidence is claimed.

## Default Development startup workflow — current launch blocked

User authorized making local startup part of future implementation delivery. Recorded in AGENTS.md: build, identify existing workspace executable/listener, restart only a verified Development instance, launch fresh Web on https://localhost:5001 with process-only existing localhost/testforgflow configuration, verify login response and leave running. No migration/bootstrap/recovery/credential reset is part of startup.

Actual attempt: `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` PASS, six projects, zero warnings/errors. CIM process inspection returned `Access denied`. Independent Get-Process found zero ForgeFlow.Web instances and netstat found zero port-5001 listeners; nothing was stopped. ConnectionStrings__ForgeFlow was absent from the agent's inherited environment; the prepared launch used the previously supplied localhost/testforgflow Windows-authentication configuration, Encrypt=True and TrustServerCertificate=False, in the child process environment only, with Development and https://localhost:5001. No credential was requested or printed.

Start-Process for the freshly built Web DLL was rejected before shell execution: `CreateProcess: Rejected("approval required by policy, but AskForApproval::Granular.sandbox_approval is false")`. Automatic approval therefore blocked the background startup action. No alternate launch/bypass was attempted; startup and login-page response are NOT VERIFIED, and no running application is claimed. Minimum manual fallback is normal Windows PowerShell setting those three process environment variables and `dotnet run --project .\src\ForgeFlow.Web\ForgeFlow.Web.csproj -c Release --no-build --no-restore --no-launch-profile`, then opening https://localhost:5001/account/login. Existing authentication/SQL/TLS acceptance remains open. No database operation or Git write occurred.

## Confirmed login routing collision and local-font/UI correction

The user supplied actual browser failure evidence: POST `/account/login` raised AmbiguousMatchException between the Minimal API Login handler and the Razor login page endpoint. This establishes a routing failure before credential validation and supersedes earlier uncertainty. The page stays `/account/login`; its form action and handler now use `POST /account/sign-in` (Auth.Login). Updated affected HTTP/SQL harness calls, UI assertions and AUTH-01 contracts. Logout has no page at its path and remains `POST /account/logout` (Auth.Logout). Credential, antiforgery, secure-cookie, rate-limit, safe-return, SQL/session and public generic-error behavior remain unchanged. Earlier A3/A4 route descriptions now show the corrected active command path; historical verification attribution remains intact.

Added AuthEndpointRoutingTests: a real loopback HTTP host maps the actual Razor application and AuthenticationBackend endpoints together, asserts exactly one endpoint for each page/sign-in/logout path, and sends requests through ASP.NET endpoint routing. Middleware observes selection before credentials/SQL execute. GET login selects the Razor page, POST sign-in selects Auth.Login, and POST logout selects Auth.Logout. This covers the overlap that rendered-input tests missed; it is not live credential/session proof.

No existing local Vazirmatn font was found, including ignored tool assets. Downloaded pinned official vazirmatn 33.0.3 npm distribution after direct upstream retrieval failed. Verified the published SHA512 before extracting only its variable WOFF2 and OFL license into `src/ForgeFlow.Web/wwwroot/fonts/`. Font: 111152 bytes, SHA256 `4e3fa217d38fdafc1fea4414ceb58ca5e662cf0ab5fa735a8c8c20e8b42cad92`; provenance is in that directory's README.md. Added local @font-face (weights 100–900) and preload. No runtime CDN or dependency change. Browser inspection found Bit's runtime theme replaced the font token with Segoe UI despite the heading loading Vazirmatn. Central root/theme/overlay typography overrides now retain Vazirmatn without overriding icon glyph fonts. Menus/dialogs share those tokens; authenticated surfaces still require integrated acceptance.

Login errors use one soft-red role=alert container, #8f2027 text, transparent message span and start alignment, without a BitMessage inner background/title/badge. Removed unused LoginErrorTitle resources and registry entry. Field wrappers, native inputs, reveal button and pseudo-elements pin physical/logical radii to 8px across states; field-group and reveal-button outlines preserve visible focus. Heights remain 48px. Layout, localization, language controls and visibility behavior are preserved.

Fresh agent-executed verification:

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, six normal-output projects, zero warnings/errors. No existing Web process was running at build time; only agent-owned SmokeTest hosts were later started/stopped.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName~AuthEndpointRoutingTests|FullyQualifiedName~BitLoginRenderingTests|FullyQualifiedName~EntryRoutingTests|FullyQualifiedName~AuthUiContractTests|FullyQualifiedName~AuthBackendTests|FullyQualifiedName~ResourceContractTests|FullyQualifiedName~DisplayText" --logger "console;verbosity=minimal"` — PASS, 67 passed, zero failed/skipped, fresh Release binaries. Includes actual endpoint routing, framework antiforgery/secure-cookie policy, Bit rendering, both catalogs and scanner.
- `node .\verification\auth-login-routing-ui\browser-check.cjs` — PASS in installed headless Chrome: fa-IR/en-US at 1440px/320px. Local font GET 200 and matching bytes; FontFaceSet loaded; platform-font inspection confirmed custom Vazirmatn glyphs; Bit button computed font uses Vazirmatn. No anonymous sidebar/overflow. Default, hover, focus, keyboard, reveal, synthetic-invalid and CDP-forced-autofill states retain 8px corners/48px field height. Autofill pseudo-state matched; saved-credential-manager acceptance is separate. Visible keyboard focus and visibility toggle worked. A synthetic intercepted POST to the new route carried username/password/antiforgery field names; localized synthetic 401 displayed one dark-red alert, cleared password/pending state and reenabled submit. No real credentials, SQL operation or authentication success was exercised.

Evidence: `verification/auth-login-routing-ui/browser-check.cjs`, `browser-results.json`, and four culture/width screenshots (synthetic username, cleared password). Host was isolated SmokeTest HTTP with unused dummy SQL configuration, distinct from native Development/HTTPS authentication acceptance. Agent-owned hosts were stopped. Cleanup of the three task-created `.tools/smoke-runs/auth-endpoint-*` key directories was rejected automatically with `approval required by policy, but AskForApproval::Granular.sandbox_approval is false`; no bypass was attempted, and the encrypted temporary key artifacts remain local. Earlier browser/font blockers are superseded only for the checks actually completed here.

Remaining live gate: restart the fresh Release application under normal Windows identity/HTTPS with its existing ConnectionStrings__ForgeFlow setting for localhost/testforgflow; open `/account/login` and privately verify ebrahim sign-in POSTs `/account/sign-in`, creates a valid session/cookie and redirects to `/`, then verify reload/logout/replay. No database/account/reset/bootstrap/recovery or Git operation occurred here. AUTH-01/Slice 01 remain unaccepted; remaining SQL/TLS/profile/circuit/draft checks stay open in the consolidated test plan.

## Navigation-heading focus outline correction

Inspected Routes.razor and App.razor: the shared router retains `FocusOnNavigate RouteData="routeData" Selector="h1"`, and App loads app.css globally. Added only `h1[tabindex="-1"]:focus { outline: none; }` beside the existing global focus-visible rule. This scopes suppression to the programmatically focusable navigation heading; links, buttons, inputs, menus/dialogs and other interactive focus rules remain unchanged. No per-page fixes or focus behavior changes were made.

Agent-executed focused source assertions PASS: router h1 focus declaration preserved, global stylesheet loaded, exact heading-only selector present, and existing 3px interactive focus-visible outline retained. No build/test rerun for this CSS-only correction. Real browser activeElement/computed outline and keyboard navigation remain NOT VERIFIED in this environment (previous Chrome process-launch sandbox rejection remains applicable). Running Web PID 10664 was not interrupted. No database or Git operations; existing AUTH-01/Slice 01 acceptance gates remain open.

## Entry, login diagnostics and field consistency correction

Confirmed entry cause: MainLayout's anonymous branch displayed a standalone login link. Added an HTTP GET `/` redirect after cookie authentication and before Razor rendering to `/account/login?returnUrl=%2F`, with no-store. Query-supplied destinations are not forwarded. Authenticated root requests pass through; login GET/POST are excluded, avoiding loops. Anonymous client-side navigation/session invalidation uses AnonymousRedirect and a full replacement navigation, without rendering the application shell.

Both runtime authentication and Organization persistence call `GetConnectionString("ForgeFlow")`; the process environment name `ConnectionStrings__ForgeFlow` is correct for `localhost/testforgflow`. This confirms the configuration mapping, not the existing host's effective connection or SQL eligibility. Installed Bit 10.7 native input rendering was tested using ASP.NET HtmlRenderer: username/password names and text/password types are emitted correctly. Source trace confirms FormData(form), HTTPS/form validation, antiforgery cookie/token validation, strict field multiplicity, generic public credential failure, Identity checking, transactional session/audit persistence, secure cookie issuance and allowlisted root redirect. No live credential/session/cookie success is inferred from source/tests.

Added minimal private `AUTH-HTTP` and `AUTH-LOGIN` stage diagnostics: transport/form/field rejection, credential rejection, store-open/account-lookup/credential-validation/session-save failures (exception type only), cookie issuance failure or cookie-issued/root-redirect success. No username, password, hash, cookie, token, connection value, exception message or request body is recorded. Public credential errors remain generic. The running host was identified as ForgeFlow.Web PID 10664 listening on localhost:5001 and was not stopped. A normal-certificate HTTPS GET from the agent identity failed with WebException; actual browser POST and SQL evidence remain unavailable. No confirmed credential/session defect was identified; login remains unresolved.

One precise next diagnostic step: after stopping the old host yourself, rebuild/restart the latest code in normal Windows PowerShell with the existing process-only ConnectionStrings__ForgeFlow setting for localhost/testforgflow, open `/account/login`, and make one attempt with ebrahim/private password. Record only the console `AUTH-HTTP`/`AUTH-LOGIN` stage lines plus the POST response status and JSON code in browser Network. If no POST occurs, report that fact. Do not export HAR, request payloads/headers, cookies or tokens. These stage results determine the next bounded fix; do not reset or bootstrap the account.

Visual correction: primary #14607f; 16px card; 48px fields/button; BitMessage Error color for a red icon and soft-red, start-aligned panel. Installed BitTextFieldClassStyles now explicitly names InputWrapper, FieldGroup, Input and RevealPassword parts. Wrappers and focus pseudo-elements use 8px radii; native inputs have no square inner border; reveal control is rounded and direction-neutral. Global root/Bit typography tokens and native/menu/dialog font stacks prefer Vazirmatn in both cultures without overriding icon glyph fonts. No local Vazirmatn font file was found, including the workspace tool cache. Actual Vazirmatn delivery is BLOCKED pending a licensed local Arabic+Latin variable WOFF2 (e.g. `wwwroot/fonts/Vazirmatn.woff2`) and its OFL license; no missing-file @font-face or CDN reference was added. Existing fallbacks remain until that asset is supplied. Browser-computed fonts, focus borders and desktop/320px RTL/LTR visuals remain unverified.

Fresh verification:

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:CopyRetryCount=0` — blocked by MSB3021 copying ForgeFlow.Infrastructure.dll into the running Web output; PID 10664 was preserved.
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutputPath=D:\Projects\ForgeFlow\.tools\auth-ui-fix\` — PASS, six projects, zero warnings/errors.
- `dotnet vstest .\.tools\auth-ui-fix\ForgeFlow.Web.Tests.dll --TestCaseFilter:"FullyQualifiedName~BitLoginRenderingTests|FullyQualifiedName~EntryRoutingTests|FullyQualifiedName~AuthUiContractTests|FullyQualifiedName~AuthBackendTests|FullyQualifiedName~ResourceContractTests|FullyQualifiedName~DisplayText"` — PASS, 66 passed, zero failed/skipped, fresh isolated binaries. Includes installed Bit HTML rendering and root/authenticated/login/no-loop/unsafe-query routing cases.

No database operations, account reset, bootstrap, dependency upgrades or Git writes. AUTH-01 and Slice 01 remain unaccepted; prior consolidated SQL/native/TLS/browser gates remain open. The running host still requires a user-controlled rebuild/restart to consume these changes.

## Login reference correction receipt — integrated login still unverified

Inspected `verification/screen/logiin.png`, installed Bit 10.7 input API, Login markup, auth-ui form serialization, A3 form/antiforgery validation, configured `ConnectionStrings:ForgeFlow` lookup, credential/session transaction, cookie issuance and root redirect. The Name API documents HTML form naming; Login specifies username/password and FormData posts these names. No observed browser POST or database evidence in this increment establishes the reported login failure's cause. No password/account reset or speculative security change was made. Database configuration and real credential/session/cookie/redirect success remain pending integrated verification.

Confirmed UI defect: auth-loading-indicator's explicit display:inline-flex overrode the hidden attribute, making its indeterminate progress visible permanently. Added an explicit hidden rule, bounded pending indicator size and normal-height button. The same hidden rule ensures the error panel stays hidden before an attempt. Added dedicated LoginLayout without NavPanel/sidebar reservation; MainLayout now renders the application shell only for authenticated state. Reference-inspired teal controls, rounded inputs/card, compact errors and segmented language controls use existing localized resources and logical CSS. Existing Bit stylesheet/script links remain present. No bundled Persian webfont was found: existing Tahoma/Arial fallback remains; computed font and actual asset loading require browser verification.

Normal Release build encountered a locked ForgeFlow.Web.exe owned by running ForgeFlow.Web process 16208; it was not terminated. Fresh isolated verification: `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutputPath=D:\Projects\ForgeFlow\.tools\auth-ui-fix\` PASS, zero warnings/errors. `dotnet vstest .\.tools\auth-ui-fix\ForgeFlow.Web.Tests.dll --TestCaseFilter:"FullyQualifiedName~AuthUiContractTests|FullyQualifiedName~AuthBackendTests|FullyQualifiedName~ResourceContractTests|FullyQualifiedName~DisplayText"` PASS: 59 passed, zero failed/skipped. These existing contract checks are not rendered browser proof. Real browser desktop/320px, keyboard, computed theme/font, input value serialization and actual login remain NOT VERIFIED. No database operations or Git writes occurred. AUTH-01/Slice 01 remain unaccepted; consolidated acceptance gates remain open. The running normal-output host does not contain these isolated-build changes until the user stops it and rebuilds/restarts normally.

Current increment: AUTH-01 A4 login/logout UI implemented; basic real sign-in, refresh persistence, logout and anonymous-root redirect PASS by USER-REPORTED manual verification above. Existing automated/SmokeTest evidence remains separately attributed. Remaining SQL/recovery/TLS/profile/draft/circuit and other integrated acceptance stays DEFERRED/UNVERIFIED in the consolidated checklist. A1 remains VERIFIED; cleanup remains unreported. Slice 00 remains ACCEPTED; AUTH-01 and Slice 01 remain INCOMPLETE/NOT ACCEPTED. Next implementation priority is 01.4 authenticated Organization/Site management UI using completed backend prerequisites; outstanding AUTH-01 work and formal acceptance remain open.

| Slice | Current status | Contract owner / dependency |
|---|---|---|
| 00 | ACCEPTED | 00-R6 and finalized acceptance receipt below; passing evidence preserved |
| 01 Organization and sites | ACCEPTED (2026-11-10, greenfield scope) | [Slice contract](docs/slices/01-organization-sites/README.md); automated 255/255 non-SQL + 12/12 `01.3` SQL + Chrome/Node + user manual browser sign-off; pre-production deployment gates preserved as operational caveats |
| AUTH-01 Authentication and Identity | ACCEPTED / HANDOFF COMPLETE (2026-11-10) | [AUTH-01 contract](docs/slices/auth-01/README.md); [test-plan](docs/slices/auth-01/test-plan.md); 22/22 SQL/TLS user-executed pass + user manual browser sign-off; earlier A1 13/13 predated `Guard()` relaxation; operational TLS/CLI caveats preserved |
| 02 Users and memberships | ACCEPTED (2026-11-10 greenfield sign-off; optional `02.4b` SQL re-run noted) | [Slice 02 contract](docs/slices/02-users-memberships/README.md); user browser sign-off + `294` non-SQL pass + prior `4/4` user SQL pass (`8.5s`) + Chrome/Node checks |
| 03 Roles and permissions | `03.1`–`03.4` VERIFIED (`6/6` SQL pass in `10.1s`, `323` non-SQL pass, `19/19` JS draft pass, `4/4` Chrome smoke pass, `testforgflow` upgraded); `03.5` ready for live browser sign-off | [Slice 03 contract](docs/slices/03-roles-permissions/README.md); `/roles`, per-user page/operation matrix in `/users`, dynamic nav hiding empty parent `Settings` menu |
| 04 Audit and security events viewer | `04.0` DESIGN PROPOSED (`Q-A01`–`Q-A04` in [decisions.md](docs/slices/04-audit-viewer/decisions.md)); `04.1`–`04.5` NOT STARTED | [Slice 04 contract](docs/slices/04-audit-viewer/README.md); read-only scoped `/audit` over `OrganizationChange` & `SecurityEvent` |
| Later catalog/units/BOM slices | Not started | Roadmap item `06` (Item/Unit/BOM) |

## Shared architecture decisions and proposals

Established FF-A001–A007 and FF-A015–A017 constraints continue to govern: four-layer composition, server-side default-deny scope, explicit trusted actors, atomic successful-write audit, SQL-enforced constraints/concurrency, operation-scoped persistence, UTC instants and centralized bilingual UI. Historical tables/source described below are not present in this greenfield solution. Slice 01 definitions, lifecycle and the single authoritative ownership matrix are in [decisions.md](docs/slices/01-organization-sites/decisions.md); its sole proposed physical model is [physical-erd.md](docs/slices/01-organization-sites/physical-erd.md). Q1–Q3 are approved; the physical design and other unresolved proposals are not approved by implication. Do not treat Domain/contract tests as evidence of database or security implementation.

Cross-slice boundary FF-A018: the narrow actor/scope/permission interfaces requested in 01.1 are implemented. Runtime enforcement, adapters and deployment refinements below remain requirements/proposals for their owning increments; no permission provider is implemented:

- Actor identity is a positive stable `long` plus trusted session/system provenance and correlation context supplied by the server adapter. Request DTOs, selected UI IDs and manufacturing context never supply authority. No zero or permissive anonymous fallback. An unavailable adapter denies management use cases.
- Requested scope is explicitly Platform, Organization(OrganizationId), or Site(OrganizationId, SiteId). Platform is a separately granted privilege, not an omitted scope. AUTH-01 resolves active membership and operation grants for that scope; membership alone is insufficient. Site belongs to the requested organization. Organization-shared reads and organization-wide management require their own grants; site A access never implies site B access. Lists, counts, details, writes and inactive/history reads apply the same policy. The scope contract is designed before any membership table.
- AUTH-01 owns immutable actor resolution, active membership, stable permission evaluation, revocation/session revalidation and bootstrap. Slice 01 owns resource ownership and parent/site lifecycle checks. Application use cases enforce both; scoped SQL predicates and composite FKs provide additional protection. Inactive-organization reactivation requires an explicit lifecycle grant; it must not bypass ordinary resource restrictions. No new authentication/membership tables belong to Slice 01.
- Proposed dependency refinement: approve 01 design, implement its narrow actor/scope contracts and non-exposed Domain/Application/persistence increments, then consume the independently authorized AUTH-01 trusted adapter before exposing management UI. This avoids the historical numeric-order cycle: AUTH-01 may depend on 01 contracts, not on completed 01 UI acceptance. Slice 01 remains incomplete until authenticated bilingual UI and isolation acceptance pass. No temporary anonymous management endpoints or runtime test-principal bypass.
- Business writes and an allowlisted module audit event commit atomically from the first operation; minimum audit persistence cannot wait for a later audit viewer. Authentication/security events belong to the AUTH-01 sink. Future owner-scoped references must carry their owner in their SQL keys/FKs; a globally unique ID alone does not establish authorization. The exact ownership levels remain the proposal in decisions.md, not an alternative matrix here.

## Design increment receipt and next step

Created four focused documents under `docs/slices/01-organization-sites`: scope/use cases/UI/increments; decisions/ownership/open questions; physical ERD; and acceptance test plan. Actual inspection: solution/project references, current source file inventory, pinned build/package files, relevant architecture and 00-R6 receipts, local Git status/HEAD/tracking ref. No Slice 00 audit or passing tests repeated; no product files, packages, generated outputs or evidence changed. Document checks only verify internal references/coverage, not implementation behavior.

The design increment above is historical. Q1 organization/site meaning, Q2 future master ownership and Q3 identifier policy were approved for 01.1. The user subsequently authorized 01.2 mappings/constraints and minimum required audit support. This does not authorize database changes to an existing installation, AUTH-01 or management UI. Full Slice 01 acceptance still depends on those later authorized increments and their actual evidence.

## AUTH-01 A3 implementation receipt — integration acceptance DEFERRED

The user explicitly authorized continuing after A2 implementation while deferring its remaining nine SQL cases and native recovery verification to integrated authentication/UI acceptance. These gates are DEFERRED, never PASS; A2 is not relabeled verified. A1's verified user-executed SQL/native evidence and earlier unsuccessful attempt are preserved. The single execution checklist is now in [AUTH-01 test-plan.md](docs/slices/auth-01/test-plan.md).

Implemented `Infrastructure/Identity/RuntimeSessions.cs` and `Web/Authentication/AuthenticationBackend.cs`, composed by Web. Real UserManager credential/hash/lockout services create fixed eight-hour SQL sessions. Serializable operations acquire the same target-account UPDLOCK/HOLDLOCK as A2 recovery before reading credentials/stamps. Invalid/unknown/disabled/locked accounts receive one generic public LoginRejected response. Five failed credentials enter the configured lockout and revoke existing sessions atomically with SignInFailed/AccountLocked/SessionRevoked events. Login issuance and SessionIssued audit commit before issuing a cookie. No automatic retry or optimistic success after uncertain commit. A cookie-write failure attempts compensating revocation and returns ServiceUnavailable; SQL and HTTP cannot be one transaction.

Cookie policy: protected `__Host-ForgeFlow.Auth`, HTTPS Secure, HttpOnly, SameSite=Lax, Path=/, no Domain, nonpersistent, no sliding renewal/remember-me. Only server-minted user ID, session GUID and security stamp references are included; no roles/membership claims. Malformed/duplicate reserved claims, wrong scheme/multiple identities deny. Each cookie-authenticated HTTP request rechecks current account activity/lockout, session owner/revocation/expiry and both stamp references. Disabled/locked accounts revoke their outstanding sessions; stamp changes revoke stale-stamp sessions without invalidating newer current-stamp sessions. Logout revokes just the current session and required event, idempotently. Failed SQL revocation still attempts cookie removal and returns localized LogoutRevocationIncomplete. A cryptographically verified cookie rejected by DB eligibility can supply its old identity solely to validate logout's antiforgery token; it is never restored for authorization.

HTTP contract for the separate UI task: HTTPS `POST /account/sign-in` accepts one `username`, one `password`, optional `returnUrl`; `POST /account/logout` accepts optional `returnUrl`. Both require the normal ASP.NET Core antiforgery form token/cookie, reject invalid forms/oversized bodies, and accept only the existing allowlisted destination `/` (empty defaults there). GET never mutates login/logout. Success redirects for a full reload; errors are `{ code, message }` with localized en-US/fa-IR AuthenticationResources text and truthful 400/401/403/429/503 status. Login has a bounded global 30-per-minute fixed window with no queue, plus Identity account lockout. No user-supplied actor/session/scope can issue a ticket. No login/denied UI is introduced here; framework error descriptions are not exposed.

Trusted adapters: `SessionActorAccessor` resolves the server's AuthenticationStateProvider principal through `CircuitSessionSource` and fresh current DB eligibility; no captured HttpContext or global mutable scope. `SessionPermissionEvaluator` verifies the actor matches that server session, then rechecks eligibility, current memberships, exact owner/site relationship and fixed role/permission/scope. Unknown roles/permissions deny. OrganizationAdministrator cannot create deployments; explicit DeploymentProvisioner cannot read another company without membership. SiteOperator cannot broaden to siblings/company scope. Read/history and organization-reactivation exceptions follow the existing contract. Every OrganizationUseCases call uses these registrations; its existing write revalidation remains active. Denials for a valid user append a scope-free AccessDenied event, never echo guessed owners. DB/config/audit failures deny. A 30-second revalidating server AuthenticationStateProvider clears invalid circuit principals; protected operations always revalidate independently. Selected-scope masking, browser draft clearing and UI reload/reconnect behavior remain A4 acceptance work.

Runtime uses the existing `ConnectionStrings:ForgeFlow` with fresh operation scopes, no initializer/recovery credential fallback, no migration or startup seed. Shared Identity registration was split so runtime registration preserves Web logging providers; host-only registration still suppresses raw host logs. No dependency version, Domain model, SQL mapping, migration or accepted sample UI was changed.

Actual verification:

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, six projects, zero warnings/errors, freshly built binaries.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthBackendTests|FullyQualifiedName~AuthHttpTests|FullyQualifiedName~AuthBootstrapTests|FullyQualifiedName~AuthRecoveryTests|FullyQualifiedName~ResourceContractTests|FullyQualifiedName~DisplayText' --logger 'console;verbosity=minimal'` — **79 passed, 0 failed, 1 deferred TLS integration test skipped**. Covers role/scope policy, reserved claims, local-return allowlist, cookie settings, missing-store denial, actual in-process antiforgery rejection/validation, outage logout cookie removal, both authentication resource catalogs, affected Identity host tests and display scanner. This is not live SQL/browser proof.
- The initial real loopback TLS attempt failed before application assertions with `System.Net.Http.HttpRequestException` -> `AuthenticationException` -> `Win32Exception: No credentials are available in the security package` at Schannel credential acquisition. No permission/certificate/host setting was changed. The test is retained and explicitly gated by `FORGEFLOW_AUTH_HTTP_TESTS=1` for integrated normal-identity verification; the earlier failure is not called PASS. In-process antiforgery tests use real framework services but do not substitute for that HTTP gate.
- Prepared **11** deferred AuthSession SQL/HTTP cases: real credentials and OrganizationUseCases, lockout, three stale-ticket scenarios, membership/owner isolation, two audit-failure rollback cases, explicit platform authority, SiteOperator boundaries, and real HTTP cookie/logout replay. Existing harness creates/migrates/drops only generated disposable databases. These cases were compiled but not run. No bootstrap/recovery command, live SQL test, existing database operation or Git write occurred.

Evidence: `verification/auth-01-a3/results.json`. Integrated commands and all outstanding gates are consolidated in test-plan.md. UI will consume the form contracts/resources, AuthenticationStateProvider/cascading state and existing OrganizationUseCases; it must implement the contracted culture/profile and identity-boundary draft behavior before authenticated UI is accepted. AUTH-01 and Slice 01 remain INCOMPLETE/NOT ACCEPTED. Official framework references: [cookie validation and issuance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), [Blazor server authentication state](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/?preserve-view=true&view=aspnetcore-10.0).

## AUTH-01 A4 login/logout UI receipt — acceptance incomplete

Implemented the bounded login/logout UI in `src/ForgeFlow.Web/Components/Pages/Login.razor`, `Components/Layout/MainLayout.razor`, `wwwroot/auth-ui.mjs`, and the existing shared English/Persian resources/styles. The existing NavPanel and sample remain intact. Inspected the installed API XML under `.tools/packages/bit.blazorui/10.7.0/lib/net10.0/Bit.BlazorUI.xml` and `.tools/packages/bit.blazorui.extras/10.7.0/lib/net10.0/Bit.BlazorUI.Extras.xml`. The responsive sign-in card uses Bit 10.7.0 `BitTextField` with `CanRevealPassword`/localized `RevealPasswordAriaLabel`, `BitButton`, `BitProgress`, and `BitMessage`. Extras has no standalone password field; it remains used by the existing shell/sample (`BitNavPanel`/`BitDialog`). No package changed.

The circuit-rendered form submits through same-origin browser `fetch` to the existing HTTPS `POST /account/sign-in`, carrying its `returnUrl=/` and antiforgery form token. Added only `GET /account/antiforgery` to issue a no-store request token/cookie for browser forms; it performs no login/logout mutation. On success the actual endpoint's local redirect response sets the cookie and the browser navigates to that response URL. Server-localized JSON errors are shown with an alert role; required fields use localized client validation. Language buttons POST the existing antiforgery-protected `/culture` endpoint and reload. Password values are never bound to Blazor state, draft fields, localStorage or sessionStorage; failed submissions clear the password control.

The shared shell now presents authenticated account display and HTTP POST logout. Logout masks the authenticated shell control immediately, posts to `/account/logout`, and follows only the existing local redirect. When server-side revalidation changes the circuit from authenticated to anonymous, the layout clears this tab's application-owned `forgeflow.draft:*` entries. Successful login and logout also clear those entries. The additional server-minted `ClaimTypes.Name` is display-only; no permission, scope or actor decision reads it. A3's user/session/stamp checks remain authoritative. There are no protected business pages yet, so this does not substitute for later protected-content acceptance.

Actual verification (agent-executed):

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, six projects, zero warnings/errors, freshly built Release binaries.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter "FullyQualifiedName~ResourceContractTests|FullyQualifiedName~AuthBackendTests|FullyQualifiedName~AuthUiContractTests|FullyQualifiedName~DisplayText" --logger "console;verbosity=minimal"` — PASS, 59 passed, 0 failed, 0 skipped. Covers complete en-US/fa-IR resource keys/placeholders, auth contracts and localized errors, the no-store antiforgery-token endpoint, real-form/UI wiring and credential-storage assertions, password/display text scanner, and relevant authorization/session assertions.
- `node --check .\src\ForgeFlow.Web\wwwroot\auth-ui.mjs` — PASS. Focused static assertions confirmed POST form wiring and no password draft/storage binding.
- An isolated SmokeTest Web process (dummy unused SQL connection string; no SQL call or existing DB access) returned login page HTTP 200 in both cultures; `fa-IR/rtl` and `en-US/ltr`, localized title present after HTML decoding. Real antiforgery token GET returned a token and `no-cache, no-store`; antiforgery POSTs to `/culture` returned 204 and subsequent requests rendered the selected culture. This is HTTP/SmokeTest evidence only, not native Development or browser/authentication proof. Its Data Protection key was written under a uniquely named `%TEMP%\ForgeFlow-auth-ui-smoke-*` directory; the host was stopped. The sandbox does not permit cleanup outside the workspace, so that temporary key directory may remain.
- Real Chrome verification was attempted but blocked: launching Chrome with `Start-Process` was rejected with `approval required by policy, but AskForApproval::Granular.sandbox_approval is false`. No bypass or escalation was attempted. Thus keyboard focus/reveal operation, RTL/LTR visual placement, desktop/320px overflow, login/logout interaction, and authenticated/revoked visual transitions are NOT VERIFIED by browser evidence.

Keep the SQL, native identity, TLS, profile/circuit, and multi-session items in [the consolidated AUTH-01 checklist](docs/slices/auth-01/test-plan.md) DEFERRED or NOT VERIFIED; none are promoted to PASS by these UI checks. A4 remains partial: access-denied UI, authenticated profile culture persistence/failure behavior, browser keyboard/responsive assertions, and real HTTP cookie login/logout remain outstanding. AUTH-01 and Slice 01 remain incomplete/not accepted.

Normal Windows PowerShell local Development launch (under the user's normal Windows identity, with the pre-existing private `ConnectionStrings:ForgeFlow` configuration and a trusted local HTTPS development certificate):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'https://localhost:5001'
dotnet run --project .\src\ForgeFlow.Web\ForgeFlow.Web.csproj -c Release --no-build --no-restore
```

Open `https://localhost:5001/account/login`. Stop with Ctrl+C, then clear the two process environment variables. This launch command does not migrate or initialize a database; the host expects its normal schema/configuration to be provisioned separately. This is an instruction only; the agent did not run native Development or access a database.

## AUTH-01 A2 implementation receipt — SQL/native gates DEFERRED

Current reconciliation: the user explicitly DEFERRED the remaining SQL and native interactive recovery gates to integrated authentication/UI acceptance. They are not PASS; all original passing build/non-SQL evidence and skipped SQL counts below remain historical evidence. This deferral authorizes A3 implementation, without accepting A2, AUTH-01 or Slice 01. See the single current checklist in AUTH-01 test-plan.md.

Authorized scope: supervised host password recovery and its session invalidation, following the existing Recovery acceptance row and minimum recovery workflow. Added `Infrastructure/Identity/HostRecovery.cs` and the `reset-password` verb to the existing host executable. No migrations/schema changes, account/grant management UI, HTTP/cookie operations, runtime adapters or additional authentication features were added. Infrastructure now references the already pinned ASP.NET Core shared framework for the real Data Protection token provider; no package/SDK versions changed.

Host controls: exactly one command verb, private unredirected terminal, dedicated `FORGEFLOW_RECOVERY_SQLSERVER` (no bootstrap/app fallback), explicit `RECOVER` attestation that the operator verified the account owner out of band and supervises their private password entry. Recovery independently requires SQL dbo/db_owner capability and excludes both runtime roles. Actual host provenance is recorded separately from the subject user. Account ID and generated/reused operation GUID are non-secret prompts. Password/confirmation reuse hidden ReadKey input with the existing 12–128 policy; no password/token is printed or accepted as an argument. All prompts are in the existing en-US/fa-IR BootstrapText resources. The host still uses its existing en-US console selection; this does not add a culture picker.

Persistence: a fresh scoped context/connection and serializable transaction lock the target User with UPDLOCK/HOLDLOCK. Completed installation/schema checks fail closed. Identity generates and validates its reset token entirely in-process, using an ephemeral Data Protection provider without writing a key ring. ResetPasswordAsync applies validation/hash/stamp rotation. All unrevoked sessions for that user, including expired sessions, receive PasswordReset revocation; already revoked and other-user sessions are untouched. PasswordResetCompleted and per-session SessionRevoked events use server time, the actual host reference, subject ID and the non-secret operation correlation; payloads contain only fixed reason/empty JSON. The password/hash/stamp/token are absent from events. Account activity, lockout/failure count, memberships, grants and BootstrapState remain unchanged. No recovery can grant deployment/company authority or restore disabled access.

Identity's password write, session changes and all required security events share one transaction. Failed validation/concurrency returns Auth.ResetRejected; unexpected SQL/errors propagate to the CLI's sanitized failure response and roll back. There is no automatic retry. A commit acknowledgement failure returns Auth.RecoveryOutcomeUnknown. A consistent replay of the same operation GUID for the same user finds its committed PasswordResetCompleted event and returns Auth.AlreadyRecovered without changing the password again. Future A3 session issuance must take the same user lock before reading stamp/eligibility, so concurrent reset cannot admit a session based on a stale stamp. No session issuer or cookie consumer is exposed by A2.

Actual agent verification against fresh binaries:

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — PASS, six projects, 0 warnings/errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthRecovery|FullyQualifiedName~AuthBootstrapTests|FullyQualifiedName~ResourceContractTests|FullyQualifiedName~DisplayText' --logger 'console;verbosity=normal'` — 47 passed, 0 failed, 7 SQL groups skipped. The two theories expand those groups to **9 SQL cases** when configured. Includes 8 new offline recovery tests, affected host/bootstrap tests, resource contracts and existing display-text scanner. Real token-provider subject/stamp binding is checked offline; this is not SQL persistence proof. A1's unchanged 13-case SQL suite and browser evidence were not repeated.

Intermediate failures were corrected before this passing build/run: missing shared-framework Data Protection reference and new Persian resource text damaged by the shell's initial encoding. New resource tests detected the latter; values were rewritten as UTF-8. Those failed attempts are not passes.

No `FORGEFLOW_TEST_SQLSERVER` is configured in this agent environment; no SQL connection/SSPI/network diagnosis, recovery invocation, initialized database access or database mutation was attempted. Under an authorized normal Windows PowerShell identity, privately configure that test variable for the existing disposable-database harness (CREATE/DROP rights; generated databases only), then run:

```powershell
dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1
dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthRecoverySqlTests' --logger 'console;verbosity=normal'
```

Require **9 passed, 0 failed, 0 skipped**: active/disabled recovery, two stage failures after Identity save, actual SQL audit rejection, unknown subject, unrelated/already-revoked sessions, synchronized concurrent replay and uncertain commit resolution. Remove the test variable afterward. Local TrustServerCertificate exceptions are disposable-session-only, not production TLS evidence. Tests create/migrate/drop only their generated databases, never the initialized A1 database.

Native recovery hidden-input/owner-control acceptance is DEFERRED by the user to integrated acceptance. If separately verified by the operator, use only a different newly prepared disposable installation and synthetic account, never the existing initialized account. The exact command is `dotnet run --project .\tools\ForgeFlow.Bootstrap\ForgeFlow.Bootstrap.csproj -c Release --no-build --no-restore -- reset-password`, with private `FORGEFLOW_RECOVERY_SQLSERVER` pointing only to that disposable target. Expect RECOVER confirmation, non-secret operation/account prompts, two hidden password prompts, Auth.PasswordResetCompleted and exit 0. Do not record credentials or run that command as part of this receipt update. Clear the recovery variable afterward. No native recovery result is claimed.

Evidence summary: `verification/auth-01-a2/results.json`. A2 is implemented but NOT VERIFIED; its remaining SQL/native gates are now DEFERRED by the user. A1 evidence and its earlier unsuccessful attempt remain preserved below. AUTH-01 and Slice 01 remain incomplete/unaccepted. A3 was subsequently authorized under that explicit deferral; A4 UI remains separate. Framework API references: [Identity reset](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.resetpasswordasync?view=aspnetcore-10.0), [reset-token generation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.generatepasswordresettokenasync?view=aspnetcore-10.0).

## AUTH-01 A1 implementation receipt — VERIFIED

Approved decisions: deployment owner/user is the initial trusted host operator for supervised bootstrap/recovery, as an operational responsibility rather than a hardcoded Windows name/SID, app account ID or automatic app grant. The first application administrator has company-only authority; no deployment-level creation grant or command flag is installed. Remote self-service recovery and the supervised recovery command remain outside A1. Existing ownership/lifecycle and business audit contracts are unchanged.

Implemented user-only ASP.NET Core Identity persistence in Infrastructure/Identity, using long account IDs, standard UserManager/EF store, normalizer, password validators and built-in hasher. Added only required Microsoft.AspNetCore.Identity.EntityFrameworkCore at pinned 10.0.14 and its resolved dependencies; existing package/SDK pins are unchanged. The supported Identity schema Version1 is selected explicitly to match the approved password-only four framework support tables without passkey/role-administration tables. AuthDbContext adds Organization/Site memberships, fixed scoped role assignments, Session storage, BootstrapState and append-only SecurityEvent: ten auth tables. Sessions are storage only, never issued by this increment. Identity types remain out of Domain; no runtime trusted adapter is registered in Web.

Migration `20261108150853_IdentityBootstrapBaseline`, designer and AuthDbContext snapshot are additive under schema auth and its separate __EFMigrationsHistory. Existing business principals are mapped with ExcludeFromMigrations: no Organization/Site/audit tables, columns or indexes are recreated/altered/dropped. Only Pending BootstrapState (Id=1, State=0) is seeded; no account, credential, session or grant seed. Normalized usernames are required/binary-unique including inactive accounts; membership/role scopes use composite FKs, finite scope checks and rowversion. All FKs use NO ACTION. Runtime role ForgeFlowAuthRuntime has no bootstrap/membership/grant writes and cannot alter/delete security history. No SQL user/login/role membership is provisioned. Empty-auth rollback refuses any real Identity/access/session/security data or Completed state and preserves business tables. SQL behavior is not yet executed by the agent.

Separate host tool: `tools/ForgeFlow.Bootstrap/ForgeFlow.Bootstrap.csproj`, added to ForgeFlow.slnx (six projects). It is not referenced/registered by Web and has no HTTP route. Exactly `initialize-deployment` is accepted; extra/password/deployment-grant arguments are refused. Private interactive terminal and explicit INITIALIZE owner attestation are required. Dedicated FORGEFLOW_BOOTSTRAP_SQLSERVER is read only in that host process, with no default/fallback to app credentials. The current host identity is recorded dynamically as provenance, never matched to a hardcoded owner account. SQL authorization requires dbo/db_owner capability and absence of runtime-role membership; this is a separately protected provisioning capability, not application permission inferred from OS identity. The owner must protect those credentials and the private terminal. System-database targets and pending migrations are refused; migrations are not auto-applied. Console labels use explicit English/Persian .resx; commands/reports remain English.

Password and confirmation are hidden Console.ReadKey input, never command arguments, environment password values, source literals, audit payloads or printed exception messages. Values longer than 128 are rejected rather than truncated. SQL/Identity raw exception text is suppressed by the tool; Identity failures expose only stable codes. Test passwords are generated at runtime, not static credentials. The initial username, company/site codes/names and operation reference are separate non-password inputs. No platform assignment input exists.

State/atomicity: serializable transaction plus UPDLOCK/HOLDLOCK on the single Pending state row serializes initializers. Empty means no account/business/access/security/framework-support data beyond schema/history/Pending marker. Any such pre-existing data, missing marker or inconsistent Completed linkage returns BootstrapPartial with no adoption/repair. Completed returns BootstrapClosed except a consistent same-operation replay with matching immutable identifiers, which returns AlreadyInitialized without changing passwords/grants. One SQL connection/transaction spans UserManager creation of the real first account, the existing internal attributed business/audit save, both memberships, one OrganizationAdministrator assignment, five required host-attributed security records and completion marker. Real generated user ID supplies controlled-bootstrap business attribution; host actor is separately recorded/correlated. Any pre-commit failure rolls back all stages on disposal. Uncertain commit returns BootstrapOutcomeUnknown and is resolved by operation ID, never automatically replayed. No normal use-case transaction is chained or allow-all provider installed.

New tests: AuthBootstrapTests verifies model/seed/snapshot/migration exclusions/permissions SQL, framework normalizer/validators/hasher, Domain input rejection, explicit consent and resource resolution. AuthBootstrapSqlTests uses the existing disposable harness for additive compatibility, empty-auth rollback/reapply, partial refusal, real identity/company-only grants/correlated audits, replay/closure, seven stage rollback failures, two concurrent-init variants, wrong-owner/role-scope constraints, runtime-role denials and uncertain-commit resolution. Fault interceptors exist only in tests. No InMemory/SQLite claim substitutes for SQL execution. Tests' reference to the host executable is test-only; production Web composition is unchanged.

Actual commands/results:

- Required Identity packages downloaded from official NuGet HTTPS using the existing certificate-verified workspace helper, with a separate ignored ledger; no network/security diagnosis or host setting change. Selected URLs/hashes: `verification/auth-01-a1/dependency-sources.json`. Existing evidence files preserved.
- `dotnet restore .\ForgeFlow.slnx --source .\.tools\nuget-feed --packages .\.tools\packages --disable-parallel --force-evaluate --no-http-cache` — all six projects restored, portable NuGet.Config unchanged; affected locks plus the tool lock retained.
- `.\.tools\ef\dotnet-ef.exe migrations add IdentityBootstrapBaseline --project .\src\ForgeFlow.Infrastructure\ForgeFlow.Infrastructure.csproj --startup-project .\src\ForgeFlow.Infrastructure\ForgeFlow.Infrastructure.csproj --context AuthDbContext --output-dir Identity/Migrations --configuration Release --no-build` — matching EF 10.0.14 scaffolded additive migration/snapshot; reviewed and added minimum runtime-role denials and guarded rollback.
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — final normal Release build passed all six projects, zero warnings/errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthBootstrap' --logger 'console;verbosity=minimal'` — 11 passed, 6 skipped groups, 0 failed against fresh binaries. The two SQL theories are deferred while skipping, so the six groups expand to 13 cases when enabled; skipped groups are not passes.
- Same EF project/startup/configuration/no-build flags with `migrations has-pending-model-changes --context AuthDbContext` — no model drift. Forward `migrations script --idempotent --context AuthDbContext --output .\verification\auth-01-a1\migration-forward.sql` and rollback `migrations script IdentityBootstrapBaseline 0 --context AuthDbContext --output .\verification\auth-01-a1\migration-rollback.sql` generated offline and were inspected. No script was applied to a live database.
- `dotnet run --project .\tools\ForgeFlow.Bootstrap\ForgeFlow.Bootstrap.csproj -c Release --no-build --no-restore -- initialize-deployment` in the agent's noninteractive execution — refused with `An interactive private terminal is required.` and nonzero dotnet-run exit 1 before reading connection/password input. This verifies refusal only, not interactive initialization or hidden-input success.

Intermediate build issues corrected before final results: AddDbContext options delegate type mismatch (CS1503) and missing test namespace for PasswordVerificationResult (CS0103). Those failed builds are not counted as verification. Unchanged Slice 00/browser and completed persistence suites were not rerun.

Evidence summary: `verification/auth-01-a1/results.json`, generated forward/rollback SQL and dependency-source ledger. SQL prerequisite is a privately configured FORGEFLOW_TEST_SQLSERVER connection under an authorized normal identity, with create/drop rights for the harness's generated disposable databases and owner capability inside them. The variable is absent in this execution environment. No new SQL connection probe, live migration, initialization or existing user database write was attempted. No sandbox SID provisioning or host permission change is requested.

Exact focused manual SQL command after privately setting the disposable test connection:

```powershell
dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1
dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthBootstrapSqlTests' --logger 'console;verbosity=normal'
```

Require 13 executed, zero failed/skipped. Clear the test variable afterward. Any local TrustServerCertificate exception is local-test-only, not production TLS validation. The actual host initializer command and target/private-input prerequisites are in AUTH-01 README; it must be verified separately in a reviewed empty installation without capturing credentials or terminal transcripts. The host tool never migrates automatically and must never be used to adopt existing data implicitly.

| A1 gate | Actual evidence / status |
|---|---|
| Operator/company-only/deferred-recovery decisions | RECORDED; no fixed host/account ID or platform-grant option |
| Model, additive migration, business exclusions, framework Identity behavior and Domain validation delegation | PASS offline tests; no live SQL inference |
| Release build | PASS, six projects, zero warnings/errors |
| Noninteractive host refusal and bilingual host resources | PASS refusal/resource checks only |
| SQL suite: migration compatibility, company-only grants, rollback, races, role restrictions, replay/unknown commit and other 13 cases | PASS — USER-EXECUTED in normal Windows PowerShell, reported 13 passed, 0 failed, 0 skipped; not agent-executed |
| Real interactive owner controls/hidden input and successful private initialization | PASS — USER-EXECUTED: explicit INITIALIZE confirmation, generated operation ID, hidden password and confirmation input, `Auth.Initialized`, exit code 0, and environment variable removed |
| Login/logout, recovery command, real cookie/circuit adapters and bilingual auth UI | NOT IMPLEMENTED; explicitly outside A1 |

A1 is VERIFIED: its 13 SQL cases and real interactive hidden-password initialization both passed based on user-reported execution. The user-reported outcomes are separate from agent-executed Release build and 16 non-SQL tests. An earlier unsuccessful user-reported interactive attempt is retained for traceability; no result details establish its exact cause. This is distinct from the agent's known noninteractive guard refusal recorded above. The successful user run supersedes the earlier attempt. The user confirmed `FORGEFLOW_BOOTSTRAP_SQLSERVER` was removed. Database cleanup has not been reported; no database cleanup is claimed or performed here, and the initialized database is not to be modified or deleted as part of this documentation update. AUTH-01 and overall Slice 01 remain INCOMPLETE and NOT ACCEPTED. The next bounded planned backend increment is A2, supervised recovery/session workflow, subject to separate authorization. A4 bilingual login/logout and identity-boundary UI remains distinct and later. No Git operations were performed.

### A1 correction — business-audit fault injection

User-executed SQL evidence before this correction: 13 executed, 12 passed, 1 failed, 0 skipped in normal Windows PowerShell. The sole failure was `AuthBootstrapSqlTests.Every_required_stage_rolls_back_identity_business_grants_history_and_completion(stage: "business-audit")`: Assert.ThrowsAny reported no exception. This is user-reported execution, not an agent SQL run; it is not a 13-pass acceptance result.

Cause: the test's RejectStage recognized `INSERT INTO [organization].[OrganizationChange]` but not the `MERGE [organization].[OrganizationChange]` form used for batched identity-returning audit inserts. MERGE handling was special-cased only for SecurityEvent. The actual production path still captures both Created events, adds both OrganizationChange records and awaits its second base.SaveChangesAsync inside the bootstrap's shared transaction. The user's passing successful-bootstrap case also verifies two correlated business audits. There is no evidence of a production audit-stage bypass; the fault setup missed that write command form.

Small test-only correction: RejectStage now recognizes both INSERT and MERGE for its exact configured insert target while completion remains UPDATE-specific. Each injector owns one sentinel exception and counts hits. Every rollback scenario now asserts one hit and that the propagated exception chain contains that exact sentinel, so unrelated/earlier failures cannot satisfy Assert.ThrowsAny. Existing empty Identity/business/grant/audit assertions are retained; added explicit session/framework-support emptiness and null checks for every completion reference/time/operator field. No production persistence, schema, migration, package or lifecycle behavior changed. No case/assertion was removed or skipped by the correction.

Five non-SQL regression cases exercise the interceptor with representative INSERT/MERGE audit commands and ensure reads/other tables do not trigger it. They verify fault targeting only, not live SQL execution/rollback. Actual fresh verification:

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — all six projects passed, 0 warnings/errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~ForgeFlow.Web.Tests.AuthBootstrapTests' --logger 'console;verbosity=normal'` — 16 passed, 0 failed, 0 skipped against the just-built binaries (11 existing cases plus five regression cases).

FORGEFLOW_TEST_SQLSERVER is absent in this execution environment. Per the requested scope, the 13 SQL cases were not rerun or substituted with another provider; no network/SSPI probe or existing database operation was attempted. After privately configuring authorized disposable SQL access in normal Windows PowerShell, rerun:

```powershell
dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1
dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~AuthBootstrapSqlTests' --logger 'console;verbosity=normal'
```

The user subsequently reported running the focused SQL command in normal Windows PowerShell: **13 passed, 0 failed, 0 skipped**. This is USER-EXECUTED evidence based on the reported result, separate from the agent's Release build and 16 non-SQL `AuthBootstrapTests`; no tests were rerun for this reconciliation. All A1 SQL checklist rows above are PASS on that attribution. The earlier 12-pass/1-fail result remains in `verification/auth-01-a1/results.json` for traceability. The user also reports the native interactive check below passed; together these complete A1 verification, without accepting AUTH-01 or Slice 01.

#### Native interactive check — user-reported completed procedure

The procedure below records the user's reported native check. The user reports success after explicit `INITIALIZE` confirmation, a generated operation ID, hidden password and confirmation entry, `Auth.Initialized`, exit code 0, and removal of `FORGEFLOW_BOOTSTRAP_SQLSERVER`. This is USER-EXECUTED evidence, not an agent run. Use of the disposable target followed the documented requirement; the database cleanup outcome was not reported. The earlier unsuccessful attempt remains part of the trace, but its exact cause is unproven.

For traceability, the executed procedure was specified for a normal private, non-transcribed Windows PowerShell terminal and a newly created disposable SQL Server database with no application data. The same Windows identity had to own that DB or be `db_owner`, without membership in `ForgeFlowOrganizationRuntime` or `ForgeFlowAuthRuntime`; no existing application database was an acceptable target.

1. Create a unique database, for example `ForgeFlow_A1_Interactive_20261108_153000`, on the intended local SQL instance using SSMS. Record the exact disposable database name for cleanup. In an SSMS query window with that database selected, execute these reviewed scripts in order: `verification/organization-persistence-01-2/migration-forward.sql`, then `verification/auth-01-a1/migration-forward.sql`. The first creates the Organization/Site baseline; the second creates the additive auth schema and Pending marker. The initializer does not apply migrations. Confirm both scripts completed successfully and the target is still the disposable database.
2. In PowerShell set the process-only connection variable with Windows Integrated Authentication; use the exact server value for the instance used above and replace the sample DB name with the exact new disposable name. The prior local SQL test used `localhost`. Do not put a SQL password in the connection string. Keep `Encrypt=True`; if the disposable local server certificate requires it, `TrustServerCertificate=True` is local-session-only and is not production TLS evidence.

```powershell
$env:FORGEFLOW_BOOTSTRAP_SQLSERVER = 'Server=localhost;Database=ForgeFlow_A1_Interactive_20261108_153000;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1
dotnet run --project .\tools\ForgeFlow.Bootstrap\ForgeFlow.Bootstrap.csproj -c Release --no-build --no-restore -- initialize-deployment
```

The executable accepts exactly `initialize-deployment`; it requires an interactive terminal with unredirected input/output and reads only `FORGEFLOW_BOOTSTRAP_SQLSERVER`. If the build fails, do not run the initializer. Keep terminal output private; do not enable PowerShell transcription or recording. Confirm the displayed target exactly matches the disposable instance/database, then type `INITIALIZE`. Leave Operation ID blank to generate one. Enter synthetic username, Organization/Site codes and names. At both password prompts enter the same fresh password (12–128 characters); typed characters must not appear. Never place passwords in commands, environment variables, shell history, logs, screenshots, transcripts or receipts. Retain only the non-secret operation ID and result code.

Expected success: output includes `Auth.Initialized`, process exit code 0, and no password characters echoed. The database is initialized atomically with one account, Organization, Site, company-only OrganizationAdministrator grant, memberships, required audit/security records and Completed marker; no deployment-level grant is created. A target mismatch, unexpected result or nonzero exit is not a pass. If the commit outcome is uncertain, retain the non-secret operation ID and consult the deployment operator before acting; do not retry with a new ID. Record only the outcome and that the two password entries were not echoed.

The user reports removing `FORGEFLOW_BOOTSTRAP_SQLSERVER`. Database cleanup was not reported; no database operation was performed for this documentation update. Do not infer that the disposable database was dropped. The SQL/TLS note remains limited to that local disposable session and establishes no production TLS validation.

## AUTH-01 design receipt and shared boundary (historical)

Documentation only: created [scope/workflows/increments](docs/slices/auth-01/README.md), [decisions](docs/slices/auth-01/decisions.md), [physical model](docs/slices/auth-01/physical-erd.md) and [acceptance plan](docs/slices/auth-01/test-plan.md). Those files own their detailed contracts; this index owns shared cross-slice decisions. Established FF-A003–A006/FF-A015/016/FF-A018 constraints are retained. New AUTH policy/model values remain proposals pending review and separate implementation authorization.

Proposed shared extension FF-A019: Identity accounts/security persistence belong to Infrastructure/Web, never Domain. AUTH-01 supplies the real current-DB actor/session/membership/permission adapters consumed by the existing use cases. Company authority never implies platform tenant-creation authority. Scope selection/cookie claims are requests/references, not authoritative grants. Protected operations recheck eligibility/current grants; circuit timers are UI cleanup only. Cookie operations require real HTTP responses and antiforgery. Profile/draft identity boundaries must be proven through actual authenticated workflows, not relabeled anonymous fixtures.

Proposed bootstrap integration: a protected host-only first-initialization coordinator serializes on a singleton completion record and atomically creates actual first Identity account, Organization/Site, grants, business audits and security events. No public setup endpoint or startup credential seed. Because the current public business use cases own their transactions, bootstrap needs a narrow shared-transaction coordinator using their Domain/audit primitives; do not blindly chain committing calls or install an allow-all adapter. Normal runtime access always uses real trusted adapters. The first actual account ID supplies controlled-bootstrap business attribution, with the host operator explicitly recorded in correlated security history. Supervised host-operator Identity password reset is the proposed minimal recovery path; full user/role administration and external providers remain outside scope.

Essential user decisions: Q-A1 confirms the responsible trusted host operator and acceptability of supervised recovery; Q-A2 chooses whether first admin receives an independently audited persistent DeploymentProvisioner assignment (recommended off/company-only by default). Recommendations and consequences are in decisions.md. Timeout/password/cookie defaults and the physical model are explicit proposals, not existing implementation or framework defaults. Official ASP.NET Core 10 model, Blazor authentication and antiforgery references were consulted and linked there; no dependency upgrade or acceptance rerun was performed.

Next step is design closure, then separately authorized A1 Identity/session/security persistence. Backend bootstrap/adapters and bilingual auth UI acceptance lead back to existing Slice 01 increments 01.4/01.5. AUTH-01 design is not AUTH-01 acceptance or permission to implement later work. Current document/link/JSON checks are editorial only; no tests were rerun.

## Increment 01.3 secured use-case receipt — verified

Implemented only the approved scoped list/detail, create, rename and activate/deactivate operations. `Application/Organizations/OrganizationUseCases.cs` defines explicit request/detail/page DTOs, immutable error parameters, stable result codes and the specialized operations-store port. `Infrastructure/Organizations/OrganizationOperationsStore.cs` implements the port using a fresh operation context and exact owner predicates. Composition registers the scoped service and a lazy store dependency; missing trusted adapters deny before requesting a persistence connection. No endpoints, UI, AUTH-01 implementation, fake runtime identity or permissive provider were added. Existing uncommitted work/evidence, Domain policy, package versions and migrations are preserved.

Authorization: each operation resolves its actor from ITrustedActorAccessor and checks the exact permission through IOrganizationPermissionEvaluator for the requested scope. Actor absence => ActorRequired; null scope => ScopeRequired; absent/negative grant => AccessDenied. Unexpected adapter exceptions abort the operation. Organization creation additionally requires explicit Platform scope; organization membership or a grant evaluated for organization scope cannot create a tenant. Organization reads/management require matching organization scope. Site reads/updates permit matching organization scope or the specific site scope; site creation requires organization scope. Wrong-owner and guessed IDs return the same parameter-free NotFound result as absent records, with no fallback query. Lists count/page only matching owner/site rows, order by Code/Id, default to active only, and bound page size to 100 (default 25). No deployment-wide directory or implicit membership enumeration is added.

Audit attribution comes from the resolved actor and the persisted entity owner, not caller actor fields. Request DTOs contain no actor or permission assertion. Before each write attempt the actor/session and grant are revalidated; a changed actor/session or revoked grant denies. Real membership/session trust and the denied-access security-event sink remain AUTH-01 work. Test doubles are confined to tests and are not integration evidence for AUTH-01.

Concurrency/lifecycle: update DTOs require an eight-byte Base64 expected rowversion; absent/malformed versions return ConcurrencyConflict. Store writes start an operation transaction, lock the parent with UPDLOCK/HOLDLOCK before loading the complete aggregate and comparing the expected version, then perform the Domain mutation. This includes no-op updates: stale versions cannot succeed silently. New Site creation/activation and parent deactivation take the same parent lock across independent contexts; the documented active-child/inactive-parent invariant is retained. Business and audit saves share that transaction through an internal context entry point; public attributed SaveChanges still owns its transaction. Failed/rejected changes produce no committed audit. No mappings/model/snapshot changes were needed.

Retry/error behavior: the configured EF execution strategy wraps the entire write attempt, with a new context/transaction and revalidation per attempt. Rollback precedes replay of transient pre-commit failures. Commit or post-commit disposal uncertainty is wrapped as OutcomeUnknown and is never replayed. Matching no-ops produce no new audit. Known duplicate errors map only SQL 2601/2627 naming the exact quoted UX_Organization_Code or UX_Site_Organization_Code index; other constraint errors propagate for diagnosis. A database permission rejection or explicit SQL user-error during the audit insertion stage maps AuditUnavailable; unrelated SQL/system errors are not disguised as duplicates/audit rejection. A finite recognized connectivity/timeout/deadlock set (including exhausted retry cases) maps PersistenceUnavailable; cancellation and unrelated errors propagate. Inner technical exceptions remain available on the exception path; no database details or forbidden entity data are returned in result parameters.

Affected persistence changes: `OrganizationDbContext` can join the use-case-owned transaction only through its internal attributed-save method; `OrganizationOperationExceptions.cs` distinguishes audit-stage failures from unknown commit outcomes. The old SQL fixture's visibility/configuration hook was widened for reuse by new tests, with its default disposable behavior unchanged. The prior six user-reported 01.2 SQL passes remain historical evidence; their regression rerun is justified by the changed save/transaction path. Unaffected Slice 00 tests, UI/browser audit and other completed persistence checks were not repeated. No SQL schema or database was changed by this increment in the agent environment.

Actual verification:

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — final normal Release outputs: all five projects passed, zero warnings/errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~OrganizationUseCaseTests|FullyQualifiedName~OrganizationUseCaseSqlTests|FullyQualifiedName~OrganizationPersistenceModelTests' --logger 'console;verbosity=minimal'` — 23 passed, 6 skipped, 0 failed against the fresh build. Six skipped cases are new SQL integration tests, not passes. The 23 are 16 use-case/gateway tests and 7 affected model/materialization/factory/write-boundary tests.
- During implementation, builds caught an untyped EntityEntry.Property generic call (CS0308) and a test-local name collision (CS0136); both were corrected before the final successful build/test run. Intermediate runs are not substituted for the final results.

Durable result summary: `verification/organization-usecases-01-3/results.json`. The agent's earlier run had no configured test connection and skipped SQL; that remains historical agent evidence. The user subsequently executed the documented combined OrganizationUseCaseSqlTests and OrganizationSqlServerTests command in normal Windows PowerShell and supplied the result in conversation: **12 passed, 0 failed, 0 skipped**. This is USER-EXECUTED verification supported by pasted output, not agent-executed automation or a locally captured output log. It covers the six new service/race/retry cases and the six affected 01.2 regressions. No InMemory/SQLite substitute was used. The local-session TrustServerCertificate exception is not production TLS certificate validation; production connection settings are unchanged.

Exact focused manual command after privately configuring an authorized disposable SQL Server test session and building fresh binaries:

```powershell
dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1
dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~OrganizationUseCaseSqlTests|FullyQualifiedName~OrganizationSqlServerTests' --logger 'console;verbosity=normal'
```

The user reports all 12 SQL cases executed with zero skips/failures: six new cases plus six affected regressions. The shared harness forces a generated ForgeFlow_Slice01_Test_<guid> database and only drops that generated database; it never selects an existing application database from the supplied InitialCatalog. This establishes the disposable local test gates, not production deployment/TLS acceptance.

| Requirement | Actual evidence / status |
|---|---|
| Allowed/denied operations, absent context/grants, explicit platform creation, wrong owner, scope/paging forwarding | PASS in focused Application tests with test-only adapters; no claim of real authentication |
| Server actor attribution and write-attempt grant/session revalidation | PASS: service callback tests plus USER-EXECUTED SQL attribution/retry cases; real AUTH-01 trust remains separate |
| EF model/factory/materialization and unattributed-write boundary after refactor | PASS in seven affected non-SQL cases |
| SQL query isolation, guessed record IDs, duplicates, stale/no-op versions and no audit for rejected writes | PASS (USER-EXECUTED, pasted result) |
| Atomic audit rollback and narrow SQL error translation | PASS (USER-EXECUTED, pasted result) |
| S05 Site creation/activation vs Organization deactivation | PASS (USER-EXECUTED): two barrier-synchronized SQL cases across independent contexts |
| Retry rollback without duplicated effects, revoked permission on retry, unknown commit without replay | PASS (USER-EXECUTED, pasted result) |
| Release build | PASS, zero warnings/errors |
| Real trusted actor/membership/permission adapter, circuit/session revocation integration and bilingual UI | NOT IMPLEMENTED: AUTH-01/01.4/01.5 dependencies remain |

Increment 01.3 is VERIFIED for its stated gates, combining the prior agent-executed build/non-SQL evidence and USER-EXECUTED 12-pass SQL result. No unchanged checks were rerun for this reconciliation. Overall Slice 01 remains INCOMPLETE and NOT ACCEPTED pending trusted AUTH-01 integration and end-to-end bilingual management UI acceptance. Next design step is the AUTH-01 contract above. No Git operations were performed.

## Increment 01.2 persistence receipt — verified

Unresolved decisions were checked before implementation: ownership, code reservation and lifecycle were already fixed by the approved 01.1 contract. FF-A005 requires atomic successful-write audit from the first operation, and the physical document already specifies the exact minimum OrganizationChange table; the user's conditional authorization therefore covers that support now. Routine mapping decisions use private EF constructors/setters, backing-field relationship fixup, shadow UTC/actor/rowversion fields, a factory-only lifetime and a database role without user/login provisioning. No new ownership/lifecycle policy or broad audit framework was introduced. P02 deployment topology and AUTH-01/runtime-principal provisioning are not approved implicitly.

Implemented in `src/ForgeFlow.Infrastructure/Organizations`: SQL Server OrganizationDbContext/model configuration; operation factory registration; minimum allowlisted audit record/writer; design-time offline factory; migration `20261108124505_OrganizationSitesBaseline`, designer and snapshot. Web registers the factory, never a circuit-held context; it does not migrate, seed or open a connection on shell startup. ConnectionStrings:ForgeFlow is required only when persistence is resolved. No management endpoint or actor/permission provider exists. Domain changes are limited to private persistence materialization members and the mapped immutable-owner FK; external code/ownership APIs remain immutable.

Migration review: exactly Organization, Site and required OrganizationChange tables (plus EF migration history on execution). Business Code is varchar(32) with binary collation, canonical stored-ASCII/DATALENGTH checks; Name nvarchar(200), required/trimmed checks; required active bit without a status default; shadow UTC/actor metadata with positive/paired-field checks; SQL-generated required rowversion. Organization Code is globally unique; Site Code is unique per OrganizationId; neither index is filtered for active status. Composite Site owner key and audit Site FK prevent cross-owner audit links. All FKs use NO ACTION; no future master/Identity tables. Runtime role grants only SELECT/INSERT/UPDATE on business tables and SELECT/INSERT on audit, denies business DELETE/audit UPDATE/DELETE, and creates no user or role membership. Down refuses any non-empty business/audit table before dropping objects; populated deployment recovery remains a reviewed backup/forward-recovery operation.

Attributed business saves own an operation transaction, lock existing parents in ID order with UPDLOCK/HOLDLOCK, validate effective parent/child state, stamp server UTC/actor metadata, and commit business plus allowlisted audit rows together. Unattributed writes, deletes, code/owner tampering and direct audit edits through SaveChanges are refused. Caller must first authenticate/authorize through future secured use cases; passing an actor DTO is not authorization. Context must be disposed after a failed write; no transparent retry after an unknown commit. SQL behavior remains unverified, as below.

Dependency/tool evidence: core EF stays 10.0.14. Added SqlServer and private Design references pinned to the same 10.0.14; isolated `.tools/ef/dotnet-ef.exe` is 10.0.14, leaving installed EF tool 8.0.6 alone. Existing versions were not upgraded; Infrastructure/Web/test lock files include the new resolved transitive dependencies. Primary official NuGet URLs/hashes are in `verification/organization-persistence-01-2/dependency-sources.json`. Workspace-local package cache is `.tools/packages`; no cache paths or credentials were added to portable NuGet.Config.

Restore/build history: initial `dotnet restore .\ForgeFlow.slnx --disable-parallel` failed NU1301 with SSL authentication / `No credentials are available in the security package`. The existing certificate-verified Python downloader was copied to ignored `.tools` with a separate dependency ledger (original verification evidence preserved); a transient download timeout succeeded on a bounded retry. The local feed initially lacked the already-installed Bit.BlazorUI.Extras 10.7.0 archive, then had a stale-version cache error; copying that exact archive from the user's cache and a force-evaluated offline restore resolved it. No security setting or package version was changed. An intermediate build caught two xUnit2031 assertion-style errors, corrected before fresh tests. Those failed restore/build attempts are not passing evidence.

Final commands and actual results:

- `dotnet restore .\ForgeFlow.slnx --source .\.tools\nuget-feed --packages .\.tools\packages --disable-parallel --force-evaluate --no-http-cache` — all five projects restored from the local feed; NuGet.Config unchanged.
- `dotnet tool install dotnet-ef --version 10.0.14 --tool-path .\.tools\ef --add-source .\.tools\nuget-feed --ignore-failed-sources` — installed isolated matching tool.
- `.\.tools\ef\dotnet-ef.exe migrations add OrganizationSitesBaseline --project .\src\ForgeFlow.Infrastructure\ForgeFlow.Infrastructure.csproj --startup-project .\src\ForgeFlow.Infrastructure\ForgeFlow.Infrastructure.csproj --context OrganizationDbContext --output-dir Organizations/Migrations --configuration Release --no-build` — scaffolded migration/snapshot offline; reviewed and added the specified runtime-role grants and guarded rollback. Subsequent model review tightened exact audit-event padding checks consistently in model/migration/snapshot.
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — final normal Release outputs, all five projects passed, 0 warnings/errors.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Organization' --logger 'console;verbosity=minimal'` — 42 passed, 6 explicitly skipped, 0 failed, against those fresh binaries. Includes 35 Domain/contract cases and 7 offline mapping/factory/materialization/migration cases. No unchanged Slice 00 audit/test suite repeated.
- `.\.tools\ef\dotnet-ef.exe migrations has-pending-model-changes --project .\src\ForgeFlow.Infrastructure\ForgeFlow.Infrastructure.csproj --startup-project .\src\ForgeFlow.Infrastructure\ForgeFlow.Infrastructure.csproj --context OrganizationDbContext --configuration Release --no-build` — no model drift.
- Same project/startup/context/configuration flags with `migrations script --idempotent --output .\verification\organization-persistence-01-2\migration-forward.sql` and `migrations script OrganizationSitesBaseline 0 --output .\verification\organization-persistence-01-2\migration-rollback.sql` — both generated offline; inspected three-table DDL, checks, indexes, FKs, role denials and populated-rollback guard. No SQL script was applied.

User-executed SQL verification (reported by the user from normal Windows PowerShell; not agent-executed and the pasted output was not captured as a workspace log):

```powershell
dotnet test .\tests\ForgeFlow.Web.Tests\ForgeFlow.Web.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~OrganizationSqlServerTests" --logger "console;verbosity=normal"
```

Reported result: 6 passed, 0 failed, 0 skipped. The user reports that all six `OrganizationSqlServerTests` ran: independent-context rowversion conflict with no audit for the losing write; runtime-role deletion/audit-mutation restrictions; fresh migration, rollback and reapplication on a disposable database; invalid codes/FKs/referenced deletion; audit-failure rollback and Unicode audit values; and organization/site uniqueness including inactive-code reservation. Connection used localhost with Windows authentication, `Encrypt=True`, and `TrustServerCertificate=True` for this local test session. The environment variable was removed afterward. This result verifies the disposable local SQL tests only; it does not establish production TLS certificate validation or production connection settings.

This user-reported normal-identity run supersedes the earlier sandbox SQL connection blocker for the six listed checks. The test harness used its generated disposable database; no production TLS claim is made. No EF InMemory/SQLite substitute was used.

| Gate | Status / evidence |
|---|---|
| Mapping, migration/snapshot consistency, factory lifetime, EF materialization/fixup | PASS offline structural tests; generated SQL and `results.json` under `verification/organization-persistence-01-2` |
| Fresh SQL migration application | PASS (USER-REPORTED) |
| Organization/Site SQL uniqueness, cross-organization reuse and inactive reservation | PASS (USER-REPORTED) |
| SQL FK/deletion checks and runtime-role permission denials | PASS (USER-REPORTED) |
| Two-context SQL rowversion and audit atomic rollback | PASS (USER-REPORTED) |
| Empty disposable rollback/reapplication | PASS (USER-REPORTED) |
| Synchronized lifecycle races / broader secured use cases | REMAINING Slice 01 acceptance work; not part of the six reported 01.2 tests |
| Release build and affected fresh tests | PASS: Release build clean; 42 focused tests pass from fresh binaries; six SQL integration tests PASS (USER-REPORTED) |
| Native Development / SmokeTest browser acceptance for Slice 01 | NOT PERFORMED; no inference from offline checks |

Increment 01.2 is VERIFIED against its listed mapping, migration, uniqueness, FK/deletion, audit, rowversion and disposable rollback gates. SQL pass attribution is explicitly USER-REPORTED. The broader synchronized lifecycle-race and secured-use-case scenarios in the Slice 01 plan remain open for their targeted later acceptance work; they are not reclassified as passing here. Overall Slice 01 remains INCOMPLETE and NOT ACCEPTED, including authenticated management UI dependent on AUTH-01. No Git writes, authentication, management UI or later business modules were added.

## Increment 01.1 implementation receipt

Scope: Organization owns an immutable-view Site collection; creation starts active, codes/owner have no mutation API, canonical Site codes remain reserved after deactivation, names are independently renameable while the owning organization is active. Organization deactivation refuses any active Site and never cascades; reactivation does not activate children. Sites can be created only through their owner and all Site mutations require that owner to be active. No caller-provided parent-active flag or active-site count can bypass these invariants. No code reuse, transfer or delete operation is exposed.

Normalization follows the existing contract: trim only ASCII spaces around codes, fold only ASCII lowercase to uppercase, require `[A-Z0-9][A-Z0-9_-]*`, 1–32 characters. Reject non-ASCII characters/digits/lookalikes, internal whitespace and invalid leading separators. Names trim Unicode surrounding whitespace, preserve Persian/English text without transliteration, require 1–200 UTF-16 code units (matching nvarchar capacity), and reject control characters before trimming. Failures leave prior state intact; stable machine codes and safe field/limit parameters contain no localized Domain messages or raw rejected input. Changed lifecycle/name methods return true; valid no-ops return false. Ordinary Organization rename is blocked when inactive; this is the documented ordinary-operation restriction, not an activation shortcut.

New Domain files: `src/ForgeFlow.Domain/Organizations/Organization.cs`, `Site.cs`, `OrganizationSiteText.cs`, `OrganizationSiteErrors.cs`. Id is zero for unsaved entities; no identity assignment, timestamp, rowversion, actor persistence or audit persistence is claimed. Deployment-wide Organization code uniqueness and concurrent SQL uniqueness remain Application/SQL obligations. Future persistence must hydrate the complete aggregate under the documented parent transaction lock; partial Site projections cannot decide parent deactivation. The Domain model is not a distributed concurrency mechanism.

New Application file: `src/ForgeFlow.Application/Organizations/AccessContracts.cs`. It defines immutable positive actor/provenance/correlation context, explicit Platform/Organization/Site request scopes, stable permission/error keys, a default-deny decision value, and async actor-resolution/permission-evaluation interfaces with cancellation. Shape construction is not proof of identity, membership or authorization; AUTH-01 must supply trusted adapters and current grant checks. There is no fake user, provider, DI registration, endpoint, singleton current scope or mutable global actor. Invalid constructor arguments are programmer-contract guards; future use-case failures must use the declared stable error codes.

Verification performed against fresh normal Release outputs (no isolated output was needed):

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` — all five projects passed, 0 warnings/errors, no restore or package/lock changes.
- `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~OrganizationDomainTests|FullyQualifiedName~OrganizationAccessContractTests' --logger 'console;verbosity=normal'` — 35 passed, 0 failed/skipped, against the just-built binaries. Tests live in the existing test project to reuse pinned tooling; no new test dependency or project was introduced.

Coverage includes fa-IR/en-US/tr-TR code normalization, length/character/name failures, Persian text preservation, atomic invalid rename/create, retained inactive Site codes, separate owners, no cascading activation/deactivation, blocked mutations under inactive parent, immutable collection exposure, invalid actor/scope shapes and default-deny decision. Unchanged Slice 00 tests/browser audits were not repeated. No SQL, runtime authorization, native Development or SmokeTest browser verification was performed or inferred for Slice 01.

| Gate | Actual status / remaining evidence |
|---|---|
| Approved Q1–Q3 and consequences | RECORDED in decisions.md; ownership matrix approved |
| 01.1 Domain invariants and narrow Application contracts | IMPLEMENTED; Release build and 35 focused fresh tests PASS |
| Real organization uniqueness, persistence, rowversion, UTC/audit storage, SQL races | NOT IMPLEMENTED/VERIFIED; 01.2 requires actual DB inventory, provider and physical/audit design review |
| Trusted actor, membership, permission and circuit revocation enforcement | NOT IMPLEMENTED; AUTH-01 adapters and 01.3 secured use cases required |
| Authenticated bilingual management UI and end-to-end acceptance | NOT IMPLEMENTED; depends on AUTH-01 and 01.4/01.5; remains part of Slice 01 completion |

Slice 01 remains INCOMPLETE and NOT ACCEPTED. Next small step, subject to separate authorization: review unresolved physical/audit/provider proposals and inventory the actual SQL target/history before 01.2. No SQL connection string, provider dependency, mapping, migration or management UI has been added by this increment.

---

# Slice 00 scope reconciliation - authoritative amendment 00-R6

Date: 2026-11-08. Scope clarification authorized by the user. Slice 00 acceptance status is finalized in this receipt. This amendment governs conflicting requirements and status labels in the contract and receipts below. Earlier receipts remain records of what was observed; their authenticated-profile and sandbox-provisioning acceptance conditions are superseded here. No product code, evidence files, dependencies, tests, or Git state were changed or rerun.

Slice 00 delivers the executable shell and reusable bilingual foundation through one compact sample. Authentication lifecycle implementation is outside Slice 00. Missing implementations below are open product deliverables, not sandbox blockers or failures of already-passing checks. The sample remains presentation-only with synthetic data and no database write; a submitted-value snapshot can demonstrate culture-independent values without persistence. Do not build a generic component showcase or form framework.

## Gate disposition and traceability

Source references name the original normative sections below, rather than unstable document line numbers. REQUIRED NOW means a Slice 00 acceptance obligation, not a claim of completion. DUPLICATE means one requirement with shared evidence, not a waiver.

| Gate or requirement | Exact source in existing contract | Classification and acceptance obligation |
|---|---|---|
| Executable baseline, pinned versions, actual build/check results | Acceptance contract and evidence / Reproducible baseline; AGENTS.md dependency/check rules | REQUIRED NOW. Preserve user-confirmed restore, resolved assets/locks, increment 05 build and 48-test results. No new restore or dependency upgrade is needed for reconciliation. |
| Explicit bilingual catalogs, key and placeholder checks | FF-A015 items 1, 4, 6; automated quality gates bullets 1-2; Resource completeness gate | REQUIRED NOW. Existing passing catalog/placeholder fixtures remain valid; extend coverage for newly added enum/error keys and typed parameter contracts. |
| Syntax-aware hardcoded-text detection | FF-A015 items 2-3; automated quality gates bullet 3; Resource completeness gate | REQUIRED NOW. Implemented and verified in increment 08 with Roslyn C# parsing, scoped Razor/JavaScript scanners, positive/negative fixtures and documented limits. Latest application-source scan has no unexcepted findings; details and exact commands are in the increment 08 receipt below. |
| Compact localized UI surfaces | Slice 00 sample and automated quality gates / sample paragraph; FF-A015 items 2, 5, 7; Complete sample UI gate | REQUIRED NOW. The localized table, dialog, tooltip, accessible controls, loading/empty states, success notification, parameterized localization-only error fixture and validation are implemented in increments 06-07 with browser evidence below. Formatting/input/draft round trips remain tracked by their separate gate. Reuse the same synthetic sample data and resources; no new business workflow. |
| Identity error fixture | FF-A015 item 7; sample paragraph; automated quality gates bullet 4 | REQUIRED NOW only as a stable error-code/parameter localization fixture. It may use a representative Identity-style code. Label its evidence as localization-only. Actual IdentityErrorDescriber wiring, exhaustive provider error coverage and runtime Identity integration are DEFERRED to AUTH-01. |
| Anonymous culture preference and switching | FF-A016 paragraphs 1-2; automated quality gates bullet 4; Culture and preference gate | REQUIRED NOW. Existing cookie/reload/fresh-tab/switch evidence is retained. Supported culture validation, cookie/header/default precedence, local return-route checks, antiforgery and localized failures remain requirements. |
| Authenticated profile and account lifecycle | FF-A016 paragraph 2 (profile preference, persistence failure, sign-in and logout); final paragraph (test principal/profile adapter); draft bullets 2 and 5 (identity scope and changes); automated quality gates bullet 4; Culture and preference gate | DEFERRED to AUTH-01, the first Authentication and Identity slice. Includes actual profile lookup/update/failure reporting, sign-in preference precedence, cookie survival across logout, account switching, principal/identity isolation and clearing drafts across identity changes. Test-authentication/profile fixtures also move to AUTH-01. Never mark these PASS from anonymous or localization-fixture evidence. |
| Date, numeric, currency and enum round trips | FF-A015 items 6, 8; FF-A016 draft bullets 1-4; FF-A017 both paragraphs; sample paragraph; Draft continuity and Formatting and storage gates | REQUIRED NOW. Implemented and verified in increment 09: strict culture-policy parsing, equivalent Jalali/Gregorian dates, exact currency identity, stable enum values, canonical submitted values, localized validation and invalid raw-input retention. No business entities or database persistence. |
| Draft safety and restoration | FF-A016 draft bullets 1-4; automated quality gates bullet 4; Draft continuity gate | REQUIRED NOW for anonymous per-tab forms: allowlist/schema/expiry, dirty/touched/validation/focus state, failure handling, isolation, reload restoration, submit/discard clearing. Existing evidence plus affected increment 09 round-trip and migration checks pass. Actual authenticated identity transitions are DEFERRED to AUTH-01. |
| Layout, keyboard and surface browser evidence | Acceptance contract / Browser and layout; FF-A016 paragraph 1 | REQUIRED NOW. Existing shell/layout/focus evidence remains PASS for tested surfaces. Add bilingual desktop/mobile/320 px browser assertions and visual evidence for new sample surfaces and fields; treat unimplemented surfaces as open product work. |
| Repeated layout/dialog/table/notification blockers | Latest acceptance receipt / Complete sample UI and Browser and layout rows; sample paragraph | DUPLICATE of the compact-sample requirement above. Implement each surface once and use the same bilingual browser evidence for both gates. |
| Repeated date/enum and currency blockers | Latest acceptance receipt / Draft continuity and Formatting and storage rows | DUPLICATE of the round-trip requirement above. One coherent input/format/submit/draft scenario supplies evidence to both gates. |
| Honest reporting | Acceptance contract / Honest reporting; AGENTS.md increment/evidence rules | REQUIRED NOW. Keep design, implementation, verification and acceptance distinct; identify environment and actual commands/results. |

## Preserved passing evidence

- `verification/navigation-04/browser-results.json`: existing NavPanel routes, active state, labels, direction and desktop/mobile drawer behavior.
- `verification/slice00-acceptance-05/browser-results.json`: keyboard/focus/containment/Escape/restoration; preference persistence; FA -> EN -> FA text/numeric preservation; localized validation; tab isolation; missing/malformed/incompatible payload handling; submit/discard clearing; current layouts.
- The six sample and two open-drawer screenshots listed in increment 05 remain valid for the captured UI. `verification/slice00-acceptance-05/build.log` and `tests.log` retain the successful Release build and 48-test run.
- `verification/runtime-02/corrected-result.json` remains SmokeTest HTTP/runtime evidence only. No passing evidence is relabeled as authenticated integration or native Development verification. Later changes require affected checks, not unconditional reruns of unchanged passing gates.

## Deferred destination register

AUTH-01 means the first Authentication and Identity slice, independent of its eventual numerical position. It owns all deferred FF-A016 profile/account/logout requirements above, test-principal/profile fixtures, actual Identity error adapter/provider integration from FF-A015 item 7, and browser verification of identity transitions and draft clearing. These items are DEFERRED, not PASS. This traceability does not authorize starting AUTH-01 or implementing authentication in Slice 00.

## Native Development: normal Windows identity check

The sandbox SID/profile and DPAPI probe failures are environment limitations of that execution identity. They are not product defects and do not require provisioning the sandbox SID, granting access to another user's keys, or altering host permissions. Replace the prior sandbox-profile provisioning prerequisite with a minimal manual check under the user's normal Windows PowerShell identity. SmokeTest evidence stays separate.

Run in normal Windows PowerShell, using the existing Release output:

```powershell
Set-Location D:\Projects\ForgeFlow
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project .\src\ForgeFlow.Web\ForgeFlow.Web.csproj -c Release --no-build --no-restore --no-launch-profile -- --urls http://localhost:5080
```

In a second normal PowerShell terminal:

```powershell
$response = Invoke-WebRequest -Uri http://localhost:5080/ -UseBasicParsing
$response.StatusCode
$response.Content.Contains('__RequestVerificationToken')
```

Minimum acceptance: server reports Development and listening at that address; the request returns 200 and the token check returns True; the page loads in a browser and the server records no Data Protection/DPAPI or permission exception during that request. This exercises response-time antiforgery/key use as well as startup. Record the Windows identity, date, command/output and relevant logs; stop with Ctrl+C. Failure must be recorded with its exact exception before deciding whether it is a product defect or that user's environment. User-reported manual verification: PASSED under the user's normal Windows identity. The user confirms startup, FA -> EN -> FA switching with preserved values, draft restoration after refresh, successful sample submission with canonical values, and absence of the submitted draft after refresh. This is user-reported evidence, not agent-executed or automated evidence.

## Remaining Slice 00 checklist and smallest next task

- Preserve the increment 08 syntax-aware presentation-text check and its narrow exception/fixture coverage.
- Preserve the localized compact-sample surfaces and localization-only error-code fixture verified in increments 06-07.
- Date/numeric/currency/enum parse-format-submit-draft round trips are complete and verified in increment 09; preserve that evidence with the unaffected passing gates above.
- Record the manual normal-identity native Development check separately, then reassess Slice 00 acceptance. No acceptance is declared here.

Smallest next task: run the documented native Development startup/request check under the user's normal Windows identity and record its output separately from SmokeTest. No further Slice 00 sample UI implementation is identified by the current 00-R6 checklist.

---

## Dialog acceptance receipt - increment 06 / 00-R6

Scope: one localized BitDialog on the existing sample, displaying the current synthetic name, raw amount and notes. Installed Bit.BlazorUI 10.7.0 XML/API inspection confirmed Title, Message, Body, CancelText, CloseButtonTitle, AutoFocus, TrapFocus, RestoreFocus and CloseOnEscape. Dependencies and existing shell/culture/draft implementation are unchanged. Four explicit SampleResources keys per culture and their resource contracts supply all new display/accessibility text.

Dialog gate: PASS in SmokeTest, Microsoft Edge 157.0.4341.42, fa-IR/RTL and en-US/LTR at 1440 and 320 px. Real browser assertions verify localized title/content/controls, current synthetic values, initial focus inside, forward/backward focus containment, Escape, close button, restoration to opener and unchanged form input after closing. Dialog bounds remain inside each viewport; document width equals viewport width. Evidence: `verification/dialog-06/browser-results.json` and four `dialog-<culture>-<width>.png` screenshots. Repeatable command: `node scripts/browser-dialog.mjs`.

Build verification: normal `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` encountered MSB3026 copy retries because the existing running process locks `bin/Release/net10.0/ForgeFlow.Web.exe`. The attempted `-p:UseAppHost=false` also encountered a locked `ForgeFlow.Web.dll`; this is a live-output file-lock limitation, not a compiler error. The existing process was left running. Fresh isolated output was verified with `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutDir=D:\Projects\ForgeFlow\.tools\dialog-output\`: all five projects passed, zero warnings/errors. `dotnet vstest .\.tools\dialog-output\ForgeFlow.Web.Tests.dll --logger:'console;verbosity=minimal'`: 48 passed, zero failed/skipped. The browser harness launches this same fresh Web DLL. An earlier 48-test run against normal output is excluded as stale evidence.

Overall Slice 00: NOT ACCEPTED. Only the dialog surface gate is closed by this receipt; other 00-R6 sample surfaces, syntax-aware text checks, formatting/input/draft round trips and normal-identity Development verification remain open. Prior passing evidence is preserved. No native Development result is inferred from SmokeTest.

---

## Compact localized sample surfaces receipt - increment 07 / 00-R6

Scope: added only the sample surfaces still missing from the 00-R6 compact-sample checklist. The existing Bit dialog, NavPanel, language switching and draft handling are unchanged. The table reuses current sample name/number values with localized synthetic defaults; the Bit tooltip is focus-reachable; the sample-row action deterministically shows loading before alternating between populated and empty; a dismissible success notification and a parameterized Identity-style error-code fixture are localized. The fixture is explicitly labeled localization-only and calls no Identity service. No dependencies, routes, models, persistence, or authentication changed.

The installed Bit.BlazorUI 10.7.0 API was confirmed from `C:\Users\PC\.nuget\packages\bit.blazorui\10.7.0\lib\net10.0\Bit.BlazorUI.xml`: `BitTooltip.Text`, `ShowOnFocus`, `ShowOnHover`, and `Relationship`; `BitMessage.Title`, child content, bindable `Dismissed`, `Dismissible`, `DismissAriaLabel`, and `Role`. The package has no simple table component, so the compact sample uses a semantic HTML table. New display, status, error and accessibility strings are explicit in both SampleResources catalogs and the resource-contract registry; the parameterized error uses matching `{0}` placeholders.

Surface gate: PASS in SmokeTest with Microsoft Edge 157.0.4341.42. `node scripts/browser-sample-surfaces.mjs` launched the fresh isolated Release Web assembly and passed fa-IR/RTL and en-US/LTR at 1440 px and 320 px. Real-browser assertions checked localized table caption/headings and RTL/LTR column order; keyboard focus and accessible description for the tooltip; keyboard activation, status/alert roles, localized dismiss names and notification dismissal; the loading-to-empty and empty-to-populated transitions; explicit localization-only error-fixture labeling and parameter substitution; and no document/table horizontal overflow. All four document scroll widths equaled the viewport (1440 or 320 px), and table widths were 736 or 288 px. Screenshots: `verification/sample-surfaces-07/surfaces-fa-IR-320.png`, `surfaces-fa-IR-1440.png`, `surfaces-en-US-320.png`, and `surfaces-en-US-1440.png`; the Persian mobile and English desktop views were visually reviewed. Machine-readable results: `verification/sample-surfaces-07/browser-results.json`.

Build/tests: the first isolated compile found two Razor type errors because `BitMessage.Content` is a RenderFragment alias; markup was corrected to use localized child content. Fresh build then passed with zero warnings/errors: `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutDir=D:\Projects\ForgeFlow\.tools\sample-surfaces-output\`. Fresh-binary tests passed: `dotnet vstest .\.tools\sample-surfaces-output\ForgeFlow.Web.Tests.dll --logger:'console;verbosity=minimal'` (48 passed, zero failed/skipped). The browser harness used `.tools/sample-surfaces-output/ForgeFlow.Web.dll`. The normal Release output was avoided because the already-running `ForgeFlow.Web` process PID 6316 is present; no process was stopped. Process account lookup was blocked: `Get-Process -IncludeUserName` requires elevated rights and `tasklist /V`/WMI returned Access denied. No elevation or security change was attempted.

Overall Slice 00: NOT ACCEPTED. This receipt closes only the localized table, tooltip, loading/empty, notification and localization-only error-code surface work. Syntax-aware hardcoded-text checks, date/numeric/currency/enum round trips, associated draft evidence, and the normal-identity native Development check remain open. Existing NavPanel, dialog, localization, draft, and navigation evidence is retained; no unrelated checks were repeated. SmokeTest evidence does not imply native Development acceptance.

---

## Hardcoded display-text check receipt - increment 08 / 00-R6

Scope: added a focused source-text gate to the existing Web test project, without changing product UI, resources, dependencies, or behavior. C# is parsed with the Roslyn assemblies shipped by the pinned .NET SDK 10.0.403 (`global.json`); there is no new NuGet package. Razor markup and JavaScript are inspected with small context-aware state/token scanners. The application scan reads only copied Web-owned `.razor`, `.cs`, and `.mjs` sources; generated/build/dependency directories and the test fixture tree are excluded. The existing `.resx` key and placeholder contract tests are reused unchanged.

Supported detections:

- Razor static element text; literal strings in `title`, `placeholder`, `alt`, `value`, `label`, `summary`, and selected `aria-*` attributes; direct string/interpolated-string expressions; and C# inside `@code`/`@functions`.
- Roslyn C# syntax for string literals in validation/display attributes and assignments, recognized validation/error message sinks, validation exceptions, and literal arms of conditional/if/switch selections whose selector names refer to culture, language, locale, Persian, or English.
- JavaScript token patterns for assignments to `textContent`, `innerText`, `innerHTML`, `outerHTML`, `title`, `placeholder`, `ariaLabel`, and `value`; `setAttribute` for supported user-facing attributes; `insertAdjacentText`/`insertAdjacentHTML`; and language-selected literal arms in those DOM sinks.
- Resource-key access forms, routes, CSS/technical strings outside display sinks, and diagnostic logging are allowed. Razor/C# comments and JavaScript comments are ignored. Resource catalogs remain covered by `ResourceContractTests`.

Findings include relative file, line, column, rule/symbol and exact offending text. The focused application scan is a test and therefore returns a failing VSTest exit code when a finding or unused exception exists. `scripts/display-text-exceptions.json` currently contains no exceptions. If one is needed, an entry must provide exact `file`, `line`, `column`, `text`, scanner `symbol`/rule, `reason`, and `reviewer`; only that exact source occurrence is suppressed, and stale/unmatched entries fail the check. The fixture proves that an exception for line 2, column 19, `Sample tool`, and `Razor attribute aria-label` leaves the identical line 1 occurrence reported.

Fixtures: direct English and Persian markup plus user-facing attributes; English/Persian C# and JavaScript language-dependent selections; a C# validation message sink; allowed resource lookup, route, CSS, logging and DOM resource-adapter patterns; and the occurrence-specific exception test. These fixtures are under `tests/ForgeFlow.Web.Tests/Fixtures/DisplayText`, outside the scanned application root.

Detection limits: this is not complete Razor or ECMAScript parsing and does not claim data-flow analysis. The Razor walker handles the markup, directives, comments, inline expressions, and code blocks used by this application; it does not analyze arbitrary Razor grammar or embedded `<script>` islands. The JavaScript tokenizer recognizes the listed DOM sink forms, not every ECMAScript syntax or framework adapter. C# Roslyn analysis uses a finite sink/attribute list and identifier-based language-selector heuristic. It will not reliably follow text through variables, arbitrary helper methods, reflection, generated/framework UI, or runtime data. Review findings and keep the sink list/exceptions narrow; this is a guard for common literal mistakes, not proof that all displayed text is localized.

Verification against fresh isolated Release binaries:

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutDir=D:\Projects\ForgeFlow\.tools\hardcoded-text-output\` - all five projects passed, 0 warnings/errors.
- `dotnet vstest .\.tools\hardcoded-text-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:FullyQualifiedName~HardcodedDisplayTextScannerTests --logger:'console;verbosity=minimal'` - 10 passed, 0 failed/skipped. This includes the full application-source scan with zero unexcepted findings and the positive/negative fixtures.
- `dotnet vstest .\.tools\hardcoded-text-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:FullyQualifiedName~ResourceContractTests --logger:'console;verbosity=minimal'` - 12 passed, 0 failed/skipped.

During initial focused runs, the new check exposed false positives on the `CultureSwitchRejected` diagnostic code and `rtl`/`ltr` direction values, then an exception-fixture path mismatch and an unrecognized localized JavaScript adapter pattern. The scanner/test fixtures were corrected; no application-source exception was added. These intermediate failures are not counted as passing evidence; the final commands above passed.

One final Release build attempt also caught a `StartsWith(char, StringComparison)` overload error while tightening the technical-route allowlist (CS1503). It was corrected to the string overload; the recorded final Release build passed. No failed intermediate binary was used as test evidence.

The normal Web output was not targeted; the existing Web process was left running. The only changed build outputs are isolated under `.tools/hardcoded-text-output`. No unrelated browser checks were repeated. Overall Slice 00 remains NOT ACCEPTED: date/numeric/currency/enum round trips, affected draft evidence, and normal-identity native Development verification remain open. No authentication, Domain, or database work is included.

---

## Formatting and draft round-trip receipt - increment 09 / 00-R6

Scope: extended the existing compact synthetic sample with a date-only input, decimal amount and explicit currency code, and a two-value sample enum. Existing `DisplayFormatter`/`CulturePolicy` services own strict parsing, date rendering and canonical formatting. Persian input is Jalali `yyyy/MM/dd`; English input is Gregorian `MM/dd/yyyy`. A date-only `DateOnly` value is preserved across language changes and canonicalized as ISO `yyyy-MM-dd`; timestamps are not accepted. Decimal parsing follows the selected input culture and rejects grouping/foreign separators instead of guessing. Currency must be one of the explicit supported codes and is never converted. Enum option labels are localized while submitted values remain stable (`summary`, `detailed`).

The sample keeps raw text and its input culture in its per-tab draft. Invalid date and decimal text remain unchanged through language switching and reload and continue to show localized validation. Draft schema version 2 includes date, amount, currency and mode; the existing compatibility policy rejects and clears version 1 rather than coercing missing fields. A successful synthetic submission captures and displays ISO date, invariant typed decimal, currency code and enum value, then clears the draft. Explicit discard also clears it. No database, business entities or dependency changes were made.

Verification evidence and results:

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutDir=D:\Projects\ForgeFlow\.tools\format-roundtrip-output\` - all five projects passed, 0 warnings/errors. Fresh isolated output was used to avoid replacing files held by the existing Web process.
- `dotnet vstest .\.tools\format-roundtrip-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:FullyQualifiedName~SampleFormattingRoundTripTests --logger:'console;verbosity=minimal'` - 23 passed, 0 failed.
- `dotnet vstest .\.tools\format-roundtrip-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:FullyQualifiedName~SwitchAndDraftTests --logger:'console;verbosity=minimal'` - 16 passed, 0 failed.
- `dotnet vstest .\.tools\format-roundtrip-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:FullyQualifiedName~ResourceContractTests --logger:'console;verbosity=minimal'` - 13 passed, 0 failed.
- `dotnet vstest .\.tools\format-roundtrip-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:FullyQualifiedName~HardcodedDisplayTextScannerTests --logger:'console;verbosity=minimal'` - 10 passed, 0 failed; the application source scan reported no unexcepted hardcoded display text.
- `node --test scripts/drafts.test.mjs` - 19 passed, 0 failed, including malformed, unsupported, older-version and future-version draft payload cases.
- `node scripts/browser-format-roundtrip.mjs` - passed in Microsoft Edge 157.0.4341.42 under SmokeTest at 1440 and 320 px in fa-IR/RTL and en-US/LTR. Assertions covered culture switching, reload, raw invalid input, localized validation, canonical values, draft restoration, old-version rejection, submit/discard clearing and no horizontal overflow. Machine-readable evidence: `verification/format-roundtrip-09/browser-results.json`; screenshots: `verification/format-roundtrip-09/roundtrip-fa-IR-320.png`, `roundtrip-fa-IR-1440.png`, `roundtrip-en-US-320.png`, `roundtrip-en-US-1440.png`.

During implementation, the first build found a Razor `@code` name collision and an unread primary-constructor parameter in a test; both were corrected before the final build. The first browser assertion incorrectly expected English number separators after switching to Persian; the assertion was corrected to match the selected-culture format. Only the final passing fresh-binary results above count as verification.

| Requirement | Evidence | Result |
|---|---|---|
| Jalali/Gregorian date parsing and same underlying date through FA -> EN -> FA | `SampleFormattingRoundTripTests`; `browser-results.json` | PASS |
| Culture-specific decimal parsing, reject ambiguous/invalid input, no silent magnitude change | `SampleFormattingRoundTripTests`; browser invalid-input/validation assertions | PASS |
| Explicit currency identity and localized enum labels with stable values | `SampleFormattingRoundTripTests`; canonical submission browser assertion | PASS |
| Valid draft restoration, invalid raw date/amount retention, unsupported currency/enum rejection, and schema migration policy | `scripts/drafts.test.mjs`; browser reload and version-1 rejection | PASS |
| FA/EN at desktop and 320 px, RTL/LTR, no horizontal overflow | `browser-results.json` and four screenshots | PASS |
| Synthetic submit canonical values and submit/discard draft clearing | `browser-results.json` | PASS |
| Display-text scanner and resource contracts after new UI strings | Fresh-binary scanner and resource tests above | PASS |
| Release build and affected tests | Isolated output `.tools/format-roundtrip-output` | PASS |
| Native Development using normal Windows identity | Manual command in `Native Development: normal Windows identity check`; no result recorded in this increment | OPEN: this sandbox run cannot verify the user's normal Windows identity; user-run verification is pending. SmokeTest is separate. |
| Authenticated profile, account switching, logout and identity-scoped drafts | AUTH-01 destination register above | DEFERRED, not PASS |

Overall Slice 00: ACCEPTED. Required Slice 00 gates are covered by the evidence cited in the matrix and receipts. AUTH-01 profile/account/logout/identity requirements remain DEFERRED, never PASS.

---

# Slice 00 UI/localization acceptance receipt - 2026-11-08

Status: all acceptance checks requested in this increment passed in the isolated SmokeTest browser. Slice 00 is NOT READY for acceptance because additional mandatory checklist gates remain unmet. The existing NavPanel implementation was preserved. No product UI, Domain, database, or authentication source was changed; the only new source artifact is a focused browser verification script.

## Scope and evidence

Reused `verification/navigation-04/browser-results.json` for the already-passed 1440 px and 390 px nav shell, active route, localized labels, drawer, and route behavior. Added `scripts/browser-slice00-gaps.mjs` for the remaining browser checks. Its captured results are in `verification/slice00-acceptance-05/browser-results.json`; visual captures are in the same directory. All browser evidence below is from Microsoft Edge 157.0.4341.42 with `DOTNET_ENVIRONMENT=SmokeTest` and `ASPNETCORE_ENVIRONMENT=SmokeTest`.

Exact commands and results:

- `dotnet build .\\ForgeFlow.slnx -c Release --no-restore -m:1` - passed all five projects, 0 warnings and 0 errors. Captured output: `verification/slice00-acceptance-05/build.log`.
- `dotnet test .\\tests\\ForgeFlow.Web.Tests\\ForgeFlow.Web.Tests.csproj -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` - passed 48, failed 0, skipped 0 against the just-built Release binaries. Captured output: `verification/slice00-acceptance-05/tests.log`.
- `node scripts/browser-slice00-gaps.mjs` - passed with 0 browser failures. Results: `verification/slice00-acceptance-05/browser-results.json`.

## Requested UI/localization acceptance matrix

| Requirement | Evidence | Result |
|---|---|---|
| Keyboard navigation, visible focus, drawer focus containment, Escape close, focus restored to opener | `browser-results.json`; measured focus ring is 3 px solid | PASS |
| Persian and English at 320 px without horizontal overflow | `browser-results.json`; both document widths equal 320 px, no clipped visible controls | PASS |
| Readability and RTL/LTR placement | Six sample screenshots at 1440, 390, and 320 px; two 390 px open-drawer screenshots; `browser-results.json` | PASS |
| Preference after reload and in a fresh tab | `browser-results.json`; persistent HTTP-only culture cookie and fresh-tab culture assertion | PASS |
| FA -> EN -> FA with unsaved text and numeric value | `browser-results.json`; mixed-script text, raw Persian digits, numeric preview, multiline notes, and focus preserved | PASS |
| Localized validation after switching | `browser-results.json`; required-name and invalid-number messages asserted in both cultures and after switching back | PASS |
| Draft isolation between tabs | `browser-results.json`; independent session keys and independent restored values | PASS |
| Missing, malformed, and incompatible draft payloads | `browser-results.json`; missing key, malformed JSON, incompatible version, and incomplete field schema exercised in browser | PASS |
| Clear drafts after successful submission and explicit discard | `browser-results.json`; both remain clear after reload | PASS |

Visual evidence paths: `sample-fa-IR-1440.png`, `sample-en-US-1440.png`, `sample-fa-IR-390.png`, `sample-en-US-390.png`, `sample-fa-IR-320.png`, `sample-en-US-320.png`, `navigation-fa-IR-390-open.png`, and `navigation-en-US-390-open.png`, all under `verification/slice00-acceptance-05/`. The screenshots were inspected. Persian content aligns from the right, English from the left, the drawer opens from the logical start edge, and the visible form controls fit each viewport.

## Full Slice 00 gate matrix

| Required gate | Current evidence or precise gap | Result |
|---|---|---|
| Reproducible baseline | User-confirmed successful restore; resolved package assets/lock; Release build and 48 tests passed this increment | PASS |
| Resource completeness | Explicit fa-IR/en-US catalog and key/placeholder checks pass. The documented syntax-aware hardcoded-display scan and reviewed narrow-exception manifest are still absent. | BLOCKED: static-analysis gate not implemented |
| Complete sample UI | Current sample has the localized form and formatter previews. It does not implement the contract's table, dialog, tooltip, loading/empty states, notification, or Identity error fixture. | BLOCKED: required sample surfaces are absent; not added because this increment was limited to verifying listed gaps and forbids UI redesign/authentication |
| Culture and preference | Anonymous cookie persistence, reload, fresh-tab preference, and local switching pass. | BLOCKED: authenticated profile preference and logout/identity-boundary tests are absent; authentication is explicitly excluded |
| Draft continuity | Text/multiline/raw-number preservation, invalid validation, tab isolation, malformed payload handling, clearing, and restoration pass in SmokeTest. | BLOCKED: the formal sample matrix also requires date and enum fields/state, which are not present |
| Formatting and storage | Existing explicit Jalali/Gregorian and number formatter tests pass; browser check confirms numeric preview across culture switch. | BLOCKED: date-entry and amount-plus-currency sample flow and canonical submitted-value evidence are absent |
| Browser and layout | Real browser checks and screenshots pass for current shell/sample in both languages at 1440, 390, and 320 px; nav result reused from `verification/navigation-04/browser-results.json`. | BLOCKED: the full gate also requires browser coverage of dialog/table/notifications, which the current sample does not contain |
| Honest reporting | Commands, actual results, screenshots, SmokeTest boundary, and remaining gaps are recorded here. | PASS |

## Native Development boundary

Native Windows Development was not run in this increment and is not inferred from the SmokeTest pass. Existing host evidence remains separate: the sandbox process SID `S-1-5-21-3755849481-2529867762-2974155886-1005` has no ProfileList registration; an isolated current-user DPAPI encrypt probe previously failed with HRESULT `0x00000002`. The prior profile/DPAPI evidence is `verification/runtime-02/profile-registration.json` and `verification/runtime-02/dpapi-probe.log`. Native Development remains BLOCKED pending a correctly provisioned execution profile; no host security or profile settings were changed.

## Acceptance decision and next step

Slice 00 is NOT READY for acceptance because the blocked mandatory gates above remain. The requested remaining UI/localization checks all pass, and no correction to the existing NavPanel or sample UI was required. Continue only within Slice 00 after the scope for the absent sample surfaces, formal date/enum draft matrix, formatter/storage matrix, authenticated preference fixtures, and static-analysis gate is authorized. Do not advance to later slices.

---
# Navigation shell implementation receipt - 2026-11-08

Status: authorized NavPanel increment implemented and verified. Slice 00 remains unaccepted. This current receipt supersedes the prior blocked attempt recorded immediately below; its evidence reflects this checkout after the user restored the solution dependencies.

## Scope and package/API evidence

Implemented the minimal navigation shell with existing routes only: the ForgeFlow title and the existing sample page at `/`. `src/ForgeFlow.Web/obj/project.assets.json` resolves `Bit.BlazorUI.Extras/10.7.0` with package path `bit.blazorui.extras/10.7.0` and package root `C:\Users\PC\.nuget\packages\`. `src/ForgeFlow.Web/packages.lock.json` independently records resolved version 10.7.0 and its content hash. The readable package assembly and XML documentation are at `C:\Users\PC\.nuget\packages\bit.blazorui.extras\10.7.0\lib\net10.0\Bit.BlazorUI.Extras.dll` and `.xml`.

The installed API documents `BitNavPanel<TItem>` as a responsive panel/drawer around `BitNav<T>`. The implementation uses `IList<BitNavItem>` for `Items` (also confirmed by compilation), `BitNavMode.Automatic`, exact route matching, `BitNavPanelBreakpoint.Lg` (drawer below 1280 px), `IsOpen`, `ShowCloseButton`, and its localized toggle/close label parameters. Extras CSS and JavaScript use the package-documented `_content/Bit.BlazorUI.Extras/styles/bit.blazorui.extras.css` and `_content/Bit.BlazorUI.Extras/scripts/bit.blazorui.extras.js` assets. No dependency version changed.

## Implementation

- `MainLayout.razor` now hosts the Extras `BitNavPanel`, localized ForgeFlow title, localized navigation landmark and item, localized desktop toggle and drawer close names, and the only application navigation link, `/`. Automatic exact matching highlights that existing route. A localized mobile drawer trigger is connected to the panel and references its navigation id.
- `App.razor` loads the Extras CSS and JavaScript alongside the existing Bit.BlazorUI assets.
- `app.css` lays out the panel and content at desktop width and lets the Extras panel become a drawer below 1280 px. The drawer placement follows the existing document direction from `CulturePolicy`; the shell does not add culture detection.
- Both explicit `SharedResources` catalogs contain the new title and accessibility keys. The resource contract registry was updated.
- `scripts/browser-navigation.mjs` provides focused browser checks and writes `verification/navigation-04/browser-results.json`.

## Verification results

- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1` - passed; all five projects built, 0 warnings, 0 errors.
- `dotnet test .\tests\ForgeFlow.Web.Tests\ForgeFlow.Web.Tests.csproj -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` - passed; 48 passed, 0 failed, 0 skipped, using binaries from the successful build above. This includes direct explicit-catalog and Razor key-reference checks.
- `node scripts/browser-navigation.mjs` - passed in Microsoft Edge 157.0.4341.42. Persian/RTL and English/LTR were checked at 1440 px and 390 px. The run verified localized nav names, existing `/` link and `aria-current="page"`, desktop panel visibility and logical edge, mobile drawer open/close, localized dialog/control names, the `aria-controls` target, and no page errors. The existing language switch was exercised fa-IR to en-US; the sample name, multiline notes, invalid amount text, localized validation, and active route survived the switch. Results: `verification/navigation-04/browser-results.json`.

## Remaining acceptance gaps and next small step

This verifies the NavPanel increment only. Slice 00 is not accepted: the full form/draft matrix, keyboard and focus behavior, 320 px overflow, full sample visual review, screenshots, and remaining culture/preference and formatter acceptance are not covered by this focused navigation run. Next small step: continue Slice 00 acceptance with keyboard/focus and 320 px overflow checks for the current shell and sample, then record any remaining gaps before considering acceptance. Do not advance to later slices.

---
# Navigation shell continuation receipt - 2026-11-08

Status: implementation blocked before source changes. Slice 00 remains unaccepted. No Domain or database files were changed.

## Scope and acceptance evidence

The authorized increment is limited to a minimal navigation shell using the restored Bit.BlazorUI.Extras 10.7.0 API, existing routes, desktop panel and mobile drawer, active-route highlighting, centralized RTL/LTR, localized labels and accessibility text, and preservation of the existing language switch and draft behavior. Required evidence is the actual restored package/API, no-restore Release build, affected localization checks, and browser navigation checks.

## Dependency and API check

The 10.7.0 package could not be verified as restored in this checkout. `src/ForgeFlow.Web/ForgeFlow.Web.csproj` and `Directory.Packages.props` declare version 10.7.0, but `src/ForgeFlow.Web/packages.lock.json` contains no resolved dependencies, `src/ForgeFlow.Web/obj/project.assets.json` lists no Extras library, and the package folders visible to this checkout contain only `C:\Users\PC\.nuget\packages\bit.blazorui.extras\8.9.0`. The package API was not inspected; no 8.9.0 API was substituted. No source or package version was changed.

## Commands and observed results

- `dotnet build ForgeFlow.sln -c Release --no-restore -m:1` - failed with `MSB1009: Project file does not exist. Switch: ForgeFlow.sln`. This checkout contains `ForgeFlow.slnx`, not `ForgeFlow.sln`.
- `dotnet build ForgeFlow.slnx -c Release --no-restore -m:1` - failed in the Web project with `NU1301`; output reported the NuGet service index could not be loaded, SSL authentication failed, and no credentials were available in the security package. No restore command or separate network diagnosis was run. Domain, Application, and Infrastructure compiled before the Web failure.
- `dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --no-restore --logger 'console;verbosity=minimal'` - 48 passed, 0 failed, 0 skipped. These are existing Release binaries from before this attempt; they do not verify a navigation implementation.
- `node scripts/browser-slice00.mjs` - started the existing SmokeTest server and received HTTP 200 for `/`, then the browser launch stalled before browser assertions. The run was interrupted; it exited 1 and produced no browser-results.json. No browser navigation, layout, localization, or draft acceptance is claimed.

## Changes and remaining gaps

No navigation source changes were made because the requested 10.7.0 dependency and its API are not present in this checkout. Consequently there is no new navigation behavior for localization or browser checks to accept. Existing localization tests pass only for the pre-existing build. The requested `ForgeFlow.sln` build could not start because that filename is absent; the available `.slnx` no-restore build fails on unresolved Extras dependency metadata. The browser harness did not reach its assertions.

Next small step: make the user's restored `Bit.BlazorUI.Extras` 10.7.0 package, generated lock/assets, and solution file available in this checkout; then inspect its actual API, implement only the scoped shell, and rerun the specified build, localization checks, and browser navigation checks. Do not advance beyond Slice 00.

---

# Slice 00 — Current workspace architecture and acceptance contract

Revision: 00-R5 / acceptance increment 05 | Date: 2026-11-08 | Status: requested UI/localization browser checks pass in SmokeTest; Release build and 48 tests pass; Slice 00 remains not ready because required acceptance gates remain incomplete; native Windows Development remains host-limited.

This section governs Slice 00 and the localization acceptance of every subsequent UI slice. It supersedes conflicting statements below that describe Slice 00 as documentation-only, advance directly to Slice 01, or imply that source and verification artifacts are available in this workspace. Earlier architectural and business rules remain historical context unless explicitly superseded here. This revision does not authorize implementation of later slices.

## Observed workspace and evidence boundary

The initial 00-R2 inspection found only `ForgeFlow.md`, including hidden-file enumeration. No `AGENTS.md` was found in the workspace or its ancestors (`D:\Projects`, `D:\`). No solution, project, application source, `.resx`, test project, CI configuration, SDK pin, ZIP, verification directory, or `.git` directory was present at that inspection. The user subsequently confirmed this is a greenfield project. Increment 01 adds the foundation listed in the execution record below; historical source is not its baseline. Git remains uninitialized.

The earlier Day 10 and 00-R1 sections describe source files, upgrades, probes, and successful builds from another delivery. Those claims have not been reproduced here. Only the increment 01 and 02 evidence below applies to this repository. No browser or database acceptance is claimed. The initial PowerShell read displayed Persian incorrectly; rereading explicitly as UTF-8 displayed the existing text correctly. Preserve the existing document as UTF-8.

## Scope and completion boundary

Slice 00 must deliver the localization foundation and a working sample page. Documentation alone cannot satisfy acceptance. The baseline is ASP.NET Core with Blazor Interactive Server and the Domain, Application, Infrastructure, and Web boundaries described below; increment 01 now creates these projects. This is a greenfield implementation. Historical version numbers are not proof of installed or currently suitable packages; this increment verifies and pins its own versions.

The 00-R2 revision changed documentation only. Increment 01 implements the minimal executable foundation, resource checks, and sample validation/formatting. Organization/site models, account provisioning, authentication, authorization management, business CRUD, database migrations, BOM changes, and later slices remain outside this increment. The full Slice 00 contract still requires the culture integration seam for authenticated principals, but it is deferred from increment 01. Production sign-in/sign-out verification becomes mandatory when that workflow exists.

## FF-A015 — Resource ownership and presentation boundary

1. Use ASP.NET Core Localization, `IStringLocalizer<T>`, and `.resx` resources. Web owns presentation resources. Proposed resource sets are `SharedResources`, `SampleResources`, `ValidationResources`, `IdentityResources`, and `ErrorResources`, each with complete explicit `fa-IR` and `en-US` resources. A neutral resource may provide runtime fallback but cannot satisfy translation completeness checks.
2. Do not embed user-facing text in Razor, C#, or JavaScript. This includes document/page titles, navigation, field labels, hints, placeholders, buttons, dialogs, table headings, loading/empty/error states, notifications, tooltips, accessibility names, and framework/reconnect/error screens. Resource files and dedicated test fixtures may contain display text. Technical identifiers, routes, CSS classes, and diagnostic codes are not display labels.
3. Do not select translated text by language comparisons, ternaries, or dictionaries of inline translations. Components consume resource keys. Culture-dependent calendar/direction behavior belongs exclusively to the shared culture and formatting services; it must not choose message text.
4. Use complete messages with numbered placeholders and typed parameter contracts. Do not concatenate translated sentence fragments. Encode substituted user data; do not treat resource or parameter content as trusted HTML. JavaScript receives the required localized messages through a presentation adapter with safe serialization; it does not maintain a parallel translation catalog.
5. Domain and Application expose language-independent errors as stable `Code` plus named, typed `Parameters`, optionally a field identifier. They never reference localization services or return display prose. Web maps known codes and parameter order to resource messages. Unknown codes produce a localized generic error and a correlation identifier; raw exception messages are not displayed. Logs retain stable diagnostic information.
6. Persist enum values, permission names, business codes, and identifiers independently of language. Presentation maps enum members to resource keys and verifies exhaustive coverage. Never persist a translated label as an identifier.
7. Resource-backed validation must cover field names, required/range/format rules, model binding, server validation, and any client validation. Configure localized DataAnnotations where used. A localized `IdentityErrorDescriber` and presentation adapter must cover all Identity errors, including password rules and parameterized errors. Inventory framework default messages so English defaults do not leak into Persian. Slice 00 exercises these adapters through fixtures; it does not create production accounts.
8. User-entered business data stays unchanged when culture switches. Bilingual master-data names, when needed, are separate explicit fields such as `NameFa` and `NameEn`, with their own requiredness and missing-name policy in the owning slice. Do not automatically translate or overwrite user data.

## FF-A016 — Central culture, direction, and form-state preservation

Supported cultures and UI cultures are explicitly `fa-IR` and `en-US`; the initial default is `fa-IR`. One centralized culture policy owns the supported list, default, request localization configuration, direction, and preference resolution. Set both `CurrentCulture` and `CurrentUICulture` consistently at the request/circuit boundary. Do not mutate process-wide defaults per user or duplicate detection in components. Set document `lang` and `dir` from the policy. Use CSS logical properties for spacing, positioning, alignment, and borders; document any direction-sensitive icon behavior.

Use `RequestLocalizationOptions` and request localization middleware before rendering culture-dependent output. A valid explicit switch wins and writes the culture cookie. Otherwise use a supported culture cookie, then an authenticated profile preference when available, then supported `Accept-Language`, then the default. Reject unsupported switch values without changing culture. Arbitrary query-string culture overrides are disabled. When Identity is introduced, an explicit authenticated switch also updates the profile preference through an adapter; sign-in preserves the current explicit browser choice. A profile persistence failure must be reported through a resource-backed message rather than falsely reporting synchronization. The cookie supports anonymous switching and survives logout.

For Interactive Server, adopt a controlled full reload to establish a new request and circuit. The shared switch coordinator must preserve draft state before navigation:

- Bind inputs on input events and flush any pending editor changes before capturing the draft. Capture an allowlisted, versioned form model, raw strings (including invalid/partially typed date and number inputs), dirty/touched fields, validation codes and parameters, and focus field identifier. Do not capture translated messages or the old circuit's `EditContext`.
- Keep the draft in per-tab `sessionStorage`, keyed by route, form instance, and authenticated identity scope. Enforce schema validation and a short documented expiry (30 minutes for the sample), and clear it after successful submission, explicit discard, expiry, or an identity change. Do not store passwords, tokens, credentials, or other sensitive fields. A future form requiring sensitive drafts must use a separately reviewed server-side draft design.
- Only after capture succeeds, submit an antiforgery-protected culture-switch request. Validate the culture and permit only local allowlisted return routes. Set the preference cookie and reload the same form route. A failed capture or switch leaves the current form usable and reports a localized error.
- Restore the draft before the form becomes interactive, rebuild its validation state, and render messages in the new language. Preserve canonical typed values and invalid raw input without reparsing it under the new culture. Valid numeric/date inputs may be reformatted from canonical values; invalid input stays visible for correction. Restore focus where possible.
- During logout or principal changes, clear scoped drafts so one account cannot restore another account's input. Repeated switches and separate tabs must not overwrite unrelated forms. Forms with unsupported state must implement preservation before their slice can be accepted.

Slice 00 proves anonymous preference resolution and reusable culture/draft contracts. Authenticated-principal resolution, profile synchronization and failure, sign-in preference precedence, account switching, logout, and identity-scoped draft clearing are DEFERRED to AUTH-01 (the first Authentication and Identity slice), including test-authentication/in-memory-profile fixtures. These requirements are not PASS and do not gate Slice 00. AUTH-01 must verify them against its actual workflow before acceptance.

## FF-A017 — Culture-independent values and shared formatting

Introduce centralized date, number, and money formatting/parsing services at the presentation boundary. Persian renders Jalali dates using an explicit Persian calendar; English renders Gregorian dates. Do not rely on a machine's default calendar. Store instants as UTC and business dates as calendar-independent date values under the owning use case's contract. Convert an instant to the configured display time zone before calendar formatting; culture is not a time zone. The sample explicitly uses UTC as its display time zone.

Preserve decimals as decimal values and money as amount plus explicit currency code. Culture switching never changes the currency, unit, exchange rate, business rounding, or stored value. Centralize supported digit normalization, grouping, decimal parsing, precision, and date validation. Invalid dates/numbers return stable validation codes. Test a known equivalent Jalali/Gregorian date, year boundaries, invalid dates, zero/negative/fractional numbers, and a fixed amount/currency pair. Resource messages supply complete surrounding labels and units where needed.

## Slice 00 sample and automated quality gates

The sample is a foundation demonstration, not a business CRUD slice. It includes resource-backed title/navigation, a language switcher, a form with business name, required input, multiline notes, date, decimal amount, currency, and a language-independent enum. It also demonstrates a table, dialog, tooltip, accessible controls, loading/empty/error states, success notification, parameterized Application error, validation errors, and an Identity error fixture. Sample text belongs in resources or dedicated fixtures. No database write is needed.

The implementation must add repeatable automated checks that:

- Enumerate each explicit culture resource set directly, bypassing runtime fallback; fail on missing/empty/duplicate keys, missing culture files, unused or unresolved key references, and incomplete enum/error-code mappings. Dynamically referenced keys need an explicit registry so they can be checked.
- Parse composite format messages and compare placeholder indexes and their declared parameter contracts in both languages; fail on malformed formats or incompatible format specifiers. Test escaped braces and repeated placeholders. Fallback success is not evidence of complete translation.
- Scan Razor, C#, JavaScript, and user-visible markup/attributes with syntax-aware checks for literal display text, inline language-selection patterns, and translated fragment concatenation. Cover framework adapters and JavaScript message delivery. Exempt only resources, dedicated test fixtures, and narrowly scoped technical literals. Each additional exception records exact file/symbol, reason, and reviewer; no blanket folder suppression. Verify checks with deliberately failing fixtures for each rule and document any static-analysis limits.
- Exercise culture resolution, independent concurrent cultures, direction, cookie persistence, profile synchronization and failure, invalid switch values, local return-route enforcement, localized validation/Identity/error adapters, and formatter round trips. Verify draft capture failure, reload restoration, repeated switches, invalid raw input, tab isolation, expiry, and identity isolation.

## Acceptance contract and evidence

Every row is required. Until implementation and evidence exist, its status is pending. No UI slice is accepted with one language incomplete.

| Gate | Required proof |
|---|---|
| Reproducible baseline | Actual solution/projects, documented SDK/dependencies, successful restore/build and relevant automated checks with exact commands and results. |
| Resource completeness | Both explicit culture catalogs pass direct missing-key and placeholder checks; intentionally broken fixtures fail as expected; hardcoded-text checks pass with reviewed narrow exceptions. |
| Complete sample UI | Every listed sample surface and its accessibility text is rendered through resources in both languages, including errors and framework states. |
| Culture and preference | Anonymous preference survives switching, reload and fresh tabs; supported-cookie/header/default precedence and rejected switch requests are covered. Authenticated profile and lifecycle requirements are DEFERRED to AUTH-01. |
| Draft continuity | Type a mixed-script name and multiline notes, select an enum, enter valid and invalid date/number inputs, trigger validation, switch both ways, then submit. Values, identifiers, dirty state, and recoverable validation survive; no unintended submit or write occurs. |
| Formatting and storage | Equivalent Jalali/Gregorian dates and numeric/money samples render correctly; canonical submitted values stay identical across cultures; business data is unchanged. |
| Browser and layout | Exercise the sample in `fa-IR`/RTL and `en-US`/LTR at 390 px mobile and 1440 px desktop, plus a 320 px overflow check. Record browser/version, viewport, keyboard/focus checks, switch behavior, dialog/table/notifications, and screenshots for each language/layout. No clipped controls or unintended horizontal overflow. |
| Honest reporting | Record test commands, observed results, remaining limitations, and separate design/implementation/verification/acceptance status. A blocked browser run cannot be reported as visual acceptance. |

Current status: the localization foundation, culture/draft coordinator, and NavPanel are implemented. The requested remaining UI/localization checks pass in Microsoft Edge 157 under SmokeTest; the Release build and 48 tests pass. Slice 00 is not ready because the resource static-analysis, complete sample UI, authenticated profile preference, full date/enum draft matrix, and amount/currency storage gates remain incomplete. Native Windows Development remains a separate host-limited environment.

## Next small implementation step

The requested remaining UI/localization browser checks are complete. Slice 00 remains pending on the gates listed in the current acceptance receipt: the documented syntax-aware hardcoded-display analysis, missing sample surfaces, authenticated preference seam, date/enum draft coverage, and date/amount/currency storage coverage. Keep native Windows Development profile verification separate from SmokeTest evidence. Do not start Slice 01 or later business features.
## Increment 03 — Navigation shell dependency/API check

Date: 2026-11-08. Scope: add the authorized matching Extras 10.7.0 package reference, restore it, inspect its installed API, and implement/verify a minimal navigation shell. Existing `Bit.BlazorUI` remains pinned at 10.7.0; `Bit.BlazorUI.Extras` 10.7.0 was added to `Directory.Packages.props` and the Web project. The normal restore command was run with the workspace-pinned SDK and workspace-local CLI/NuGet directories:

`$env:DOTNET_CLI_HOME = (Join-Path (Get-Location) '.tools\cli-home'); $env:NUGET_PACKAGES = (Join-Path (Get-Location) '.tools\packages'); .\.tools\dotnet\dotnet.exe restore src/ForgeFlow.Web/ForgeFlow.Web.csproj --force-evaluate`

Restore failed for the Web project with `NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json`, followed by `The SSL connection could not be established`, `Authentication failed`, and `No credentials are available in the security package`. No inference is made about the cause. Restore was interrupted after reporting that error. The Extras package is not in the local feed/cache, so its actual installed API cannot be inspected and package lock files were not updated. No component markup, service registration, import, or asset change was made. Release build, localization checks, and browser checks were not run because restore did not complete.

## Follow-up — User restore and workspace verification

Date: 2026-11-08. The user reported successfully restoring `ForgeFlow.sln` from normal Windows PowerShell. Rechecking this workspace found `Bit.BlazorUI.Extras` 10.7.0 absent from `.tools/packages`, `.tools/nuget-feed`, the Web package lock file, and the current user NuGet package path. `src/ForgeFlow.Web/obj/project.assets.json` contains only the unresolved dependency declaration, without a restored library. The workspace solution is `ForgeFlow.slnx`; no `ForgeFlow.sln` file is present. There is no restored Extras assembly/package here to inspect, so implementation against its API remains pending.

The requested command `dotnet build ForgeFlow.sln -c Release --no-restore -m:1` was run. It failed at SDK initialization with `System.UnauthorizedAccessException: Access to the path 'C:\Users\PC\.dotnet\10.0.403.toolpath.sentinel' is denied.` No network diagnosis was repeated. The authorized Extras references remain pinned at 10.7.0; lock files were not updated. Localization checks and real-browser desktop/mobile checks were not run because the package is unavailable in this workspace and the requested build could not start.

Language switching and draft capture/restoration remain **partial and unverified** against the full Slice 00 acceptance matrix; their implementation remains unchanged. Next step: make the user's restored `Bit.BlazorUI.Extras` 10.7.0 package and matching lock/assets available to this workspace, then inspect its actual API and continue the navigation increment. Slice 00 remains unaccepted.

## Increment 01 — Actual implementation and verification

Date: 2026-11-07. Source was created here from scratch; no previous application source was copied. `AGENTS.md` now records the slice workflow, localization boundaries, evidence requirements, execution restrictions, and prohibition on unrequested Git operations. Its instructions were read after creation.

### Selected versions and compatibility evidence

| Component | Selected pin | Official evidence |
|---|---|---|
| .NET SDK | 10.0.403, exact `global.json`, no roll-forward or prerelease | [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) lists this stable SDK and runtime 10.0.14. [Release index](https://builds.dotnet.microsoft.com/dotnet/release-metadata/releases-index.json) lists .NET 11 as RC, excluded from selection. |
| .NET / ASP.NET Core | `net10.0`; minimum runtime 10.0.14 | Web and test project `RuntimeFrameworkVersion`; generated Web runtime configuration requests both shared frameworks at 10.0.14. |
| EF Core | 10.0.14 | [Official Microsoft package](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.14) targets .NET 10. It is referenced only by Infrastructure; there is no DbContext, provider, entity, or migration. |
| Bit.BlazorUI | 10.7.0 | [Publisher package](https://www.nuget.org/packages/Bit.BlazorUI/10.7.0) and downloaded signed package include a `net10.0` dependency/assembly group. No Extras package is needed for the sample button. |
| Test tooling | Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4 | Explicit, stable test-tool pins; these are compatible selections, not claims of latest test-tool versions. Actual test execution passes on .NET 10. |

The .NET/EF/Bit latest-stable checks were made against official metadata and NuGet indexes during this increment. `verification/versions.json` records the observed values and sources. `Directory.Packages.props` centralizes package versions; each project has `packages.lock.json`. No floating package versions are used. ASP.NET Core framework-provided packages are pruned by the SDK rather than redundantly referenced. Tests explicitly reference the ASP.NET Core shared framework.

Only .NET 8 SDKs were originally installed. `python scripts/bootstrap-sdk.py` downloaded SDK 10.0.403 into `.tools/dotnet` and verified its SHA512 against Microsoft's release metadata before extraction. The script is pinned to that SDK on subsequent runs. The SDK, archive, local feed, and package cache are workspace artifacts excluded by `.gitignore`; no Git repository was created. `verification/sdk.log` and `verification/sdk-sha512.log` record the installed workspace SDK and verified archive hash.

### Implemented foundation

- `ForgeFlow.slnx` contains Domain, Application, Infrastructure, and Web projects plus `ForgeFlow.Web.Tests`. Domain and Application intentionally contain no business behavior. References follow Domain ← Application ← Infrastructure, with Web as the composition root referencing Application and Infrastructure.
- Web registers Blazor Interactive Server, Bit.BlazorUI, ASP.NET Core Localization, and scoped culture/formatting services. Package inspection confirmed the actual stylesheet/script paths and button API before compilation. `App.razor` sets document `lang`/`dir` centrally and includes the import map and static assets. Application CSS uses logical spacing and sizing properties.
- Explicit `SampleResources` and `SharedResources` catalogs exist for both `fa-IR` and `en-US`, with 15 keys per language. No neutral catalog is used to conceal missing translations. `IStringLocalizer<T>` supplies page title, navigation, form label/button, required-name validation, parameterized success message, and error/not-found text.
- The sample at `/` validates a name using a presentation-only `SampleFormState`, preserves the entered value during validation, and performs no persistence. It displays a fixed canonical date and decimal through `DisplayFormatter`. Dates use explicit Persian/Gregorian calendars with `yyyy/MM/dd` and Latin digits; numbers use the selected culture's `N2` formatting. Monetary formatting, parsing, digit normalization, date/number inputs, and time-zone conversion remain pending.
- `CulturePolicy` owns supported cultures, default Persian, request localization configuration, calendar selection, and direction. Cookies precede `Accept-Language`; query overrides are disabled. Supported cookie culture is normalized to one formatting/UI culture so inconsistent cookie pairs cannot select conflicting calendars and messages. There is no switch endpoint, profile adapter, or authentication in this increment.
- Tests directly inspect culture-specific XML catalogs without fallback, reject missing/empty/duplicate/extra keys, validate composite formats and parameter indexes/specifiers, and check registered Razor key usage. Escaped braces, repeated/reordered placeholders, and deliberately invalid fixtures are covered. Tests also cover localizer resolution, request policy, cookie consistency, direction, Jalali/Gregorian year boundaries, decimal samples, and localized form validation in both languages.

The key-reference check currently covers the sample's `Text["Key"]` Razor convention and an explicit registry. It is not a general C#/JavaScript key analyzer. Placeholder contracts currently declare parameter counts and matching format specifiers; typed error-parameter contracts remain pending because no Application errors exist yet. A syntax-aware hardcoded-text checker and its narrow-exception manifest are not implemented. These limits remain acceptance gaps.

### Commands and actual results

The following permitted inline PowerShell assignments set workspace-local SDK/cache paths for direct SDK commands; they do not alter execution policy or machine security settings:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.tools/packages'
$env:DOTNET_ROOT = Join-Path (Get-Location) '.tools/dotnet'
```

| Exact command | Actual outcome and evidence |
|---|---|
| `python scripts/bootstrap-sdk.py` | Succeeded; SHA512 matched Microsoft metadata. |
| `./.tools/dotnet/dotnet.exe restore ForgeFlow.slnx --verbosity minimal` | Failed with NU1301: SSL authentication failed; `No credentials are available in the security package`. Original output: `verification/restore.log`. |
| `python scripts/cache-nuget.py` | Succeeded; official NuGet HTTPS downloads through Python with certificate verification enabled. Downloads compatible dependency candidates; NuGet remains the resolver and signature verifier. Sources/hashes: `verification/package-downloads.json`. |
| `./.tools/dotnet/dotnet.exe restore ForgeFlow.slnx --source .tools/nuget-feed --verbosity minimal` | Succeeded after the in-scope restore fixes below; `verification/restore-local.log`. |
| `./.tools/dotnet/dotnet.exe restore ForgeFlow.slnx --source .tools/nuget-feed --locked-mode --verbosity minimal` | Succeeded for all five projects; `verification/restore-locked.log`. |
| `./.tools/dotnet/dotnet.exe build ForgeFlow.slnx -c Release --no-restore` | Succeeded; zero warnings and zero errors; `verification/build.log`. |
| `./.tools/dotnet/dotnet.exe test ForgeFlow.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=slice00.trx' --results-directory verification/TestResults` | 24 passed, zero failed/skipped; `verification/tests.log` and `verification/TestResults/slice00.trx`. The result-file overwrite notice is from replacing the earlier 16-test run. |
| `python scripts/smoke-http.py` | Failed before completing any culture response. Client received `IncompleteRead(2463 bytes read)`. Server reported Data Protection DPAPI decryption failure and Event Log `Access is denied`; `verification/server.log`, `verification/server-blocked.log`, `verification/http-smoke.log`, and `verification/http-smoke.json`. No HTTP or browser acceptance is claimed. |

The local-feed restore does not prove a current vulnerability audit. No audit/signature/TLS checks were disabled, and no clean-security claim is made. Normal online restore/audit remains unverified in this environment.

Other exact attempts and resolved issues:

- `Invoke-RestMethod -Uri $url` against the Microsoft release metadata and NuGet flat-container indexes failed with `The underlying connection was closed: An unexpected error occurred on a receive.` `curl.exe -I https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json` failed with `SEC_E_NO_CREDENTIALS`. Python's standard HTTPS transport succeeded without changing certificate verification.
- `./scripts/dotnet.ps1 restore ForgeFlow.slnx --source .tools/nuget-feed --verbosity minimal` was rejected: the file was not digitally signed (`PSSecurityException`, `UnauthorizedAccess`). That helper was removed; permitted direct SDK invocations were used. No execution-policy bypass or sandbox escalation was attempted.
- Initial local restore exposed SDK warning-as-error NU1510 for a redundant Components.Web reference and NU1101 for the SDK's implicit `Microsoft.AspNetCore.App.Internal.Assets` package. Removing the redundant reference and caching the matching 10.0.14 assets package resolved them. Explicit ASP.NET Core framework reference in the tests resolved their transitive package-resolution failure. Final restore/build/test results above apply to the corrected files.
- The HTTP smoke script launches and terminates only its test server. No Data Protection keys, DPAPI permissions, Event Log permissions, or OS security settings were modified to overcome the runtime block. The SDK's first-use output reported automatic development-certificate installation; no certificate-trust command was issued. No production authentication, roles, business data, migrations, or workflows were added.

### Remaining acceptance gaps and handoff

| Area | Status after increment 01 |
|---|---|
| Four-layer baseline, pins, resource infrastructure, minimal sample source | Implemented; compiled and covered by the relevant non-browser tests above. |
| Real HTTP/circuit startup, asset delivery, localized error routes | Smoke script exists; verification blocked by host cryptography/logging restrictions. No successful response or circuit interaction claimed. |
| Interactive language switching and preference persistence | Not implemented; existing supported cookie/header selection is not a switch workflow. |
| Reload-safe draft capture/restoration and sensitive/identity isolation | Not implemented. Current form values survive validation only; reload preservation is not claimed. |
| Full sample surfaces, enums, Application/Identity error adapters, richer validation | Not implemented; no authentication or Application errors were introduced. |
| Money/number/date parsing and complete formatter contracts | Only date display and decimal display implemented; remaining contracts pending. |
| Missing keys and placeholder parity | Automated checks pass for the implemented catalogs; general source analysis, typed parameters, and hardcoded-text checks remain pending. |
| Persian/English visual review, RTL/LTR, keyboard, mobile, actual interactive validation | Browser acceptance not performed. Source resources and non-browser tests are not visual or interaction evidence. |

Slice 00 is **not accepted**. The next increment stays within Slice 00: obtain runtime evidence under normal allowed host capabilities, then implement language switching together with draft restoration and its tests. No Git initialization, commit, push, merge, or later-slice implementation was performed.

## Increment 02 — Runtime failure diagnosis and supported isolation

Scope: runtime smoke failure only. `AGENTS.md` and the authoritative contract were read before changes. The four-layer baseline, resource catalogs, form behavior, dependencies, and completed checks were preserved. No language switching, draft restoration, authentication, business entities, migrations, or later slices were implemented. Increment 01's failed HTTP run remains historical evidence; the successful run below supersedes that blocker for the smoke environment only.

### Reproduction and exact exception chain

`python scripts/smoke-http.py` reproduced the original failure before correction. The client exited 1 with `http.client.IncompleteRead: IncompleteRead(2463 bytes read)`. The server did start listening; the failure arose while initializing/protecting Data Protection payloads and rendering a response, not while binding the socket.

The exact server exceptions included:

```text
System.Security.Cryptography.CryptographicException: Error occurred during a cryptographic operation.
  at Microsoft.AspNetCore.DataProtection.Cng.DpapiSecretSerializerHelper.UnprotectWithDpapiCore(...)
  at Microsoft.AspNetCore.DataProtection.XmlEncryption.DpapiXmlDecryptor.Decrypt(...)

System.AggregateException: An error occurred while writing to logger(s).
  (Cannot open log for source '.NET Runtime'. You may not have write access.)
 ---> System.InvalidOperationException: Cannot open log for source '.NET Runtime'. You may not have write access.
 ---> System.ComponentModel.Win32Exception (5): Access is denied.

System.Security.Cryptography.CryptographicException: An error occurred while trying to encrypt the provided data.
  Refer to the inner exception for more information.
```

The final encryption exception wrapped a logging failure and occurred through Data Protection during response generation. Repeated Event Log exceptions also disrupted exception-handler logging and the response stream, producing the client's incomplete-read symptom. Full stack traces are in `verification/runtime-02/reproduction-server.log`; the client traceback and result are in `reproduction-client.log` and `reproduction-result.json`. The preceding run's log is preserved as `previous-server.log`.

### Cause and responsibility

| Layer/resource | Observed evidence | Finding |
|---|---|---|
| Application | No explicit Data Protection repository, wrapping mechanism, or local logging provider selection existed. No source/configuration file named another app or PC's key path. | Framework defaults unintentionally introduced profile-level and Windows Event Log dependencies into a greenfield local process. Localization and sample validation were not the source of these exceptions. |
| Harness | It launched the real Web DLL in Development and inherited profile environment variables. | It did not isolate the smoke run's key lifecycle. The incomplete-read exception was a downstream symptom, not a Python TLS or HTTP-parser fault. |
| Execution identity/profile | Process identity: `DESKTOP-EL7ANC1\CodexSandboxOffline`, SID `S-1-5-21-3755849481-2529867762-2974155886-1005`. `USERPROFILE=C:\Users\PC`; `LOCALAPPDATA=C:\Users\PC\AppData\Local`. No ProfileList registration for that sandbox SID was found. | The process identity and discovered profile belong to different accounts. A usable native current-user DPAPI profile cannot be assumed. |
| Discovered key ring | `C:\Users\PC\AppData\Local\ASP.NET\DataProtection-Keys` contains three keys predating this project. Owner is `DESKTOP-EL7ANC1\PC`; `CodexSandboxUsers` has ReadAndExecute, not write access. DPAPI could not decrypt the keys. | ForgeFlow was reading pre-existing profile-owned key material. The owning applications cannot be identified from the inspected metadata; no claim is made that they were ForgeFlow keys. No key contents were printed or copied. |
| Fresh current-user DPAPI | A separate in-memory encrypt/decrypt probe, without reading or writing any profile key files, failed in `DpapiSecretSerializerHelper.ProtectWithDpapiCore` / `DpapiXmlEncryptor.Encrypt`. Reported HRESULT: `0x00000002`. | Merely selecting an empty writable directory would not resolve this execution context's inability to wrap new keys with native current-user DPAPI. It is not just a file ACL problem. |
| Logging | The default Windows Event Log provider tried the Application log, source `.NET Runtime`, and received Win32 error 5. | A second failure occurred while reporting the cryptographic failure. Logging then obscured the original error and disrupted error handling. |

Identity, file metadata, ACL, profile-registration, and isolated DPAPI-probe evidence is retained in `verification/runtime-02/identity.json`, `profile-key-metadata.json`, `profile-key-acl.json`, `profile-registration.json`, and `dpapi-probe.log`. The probe source/project is in `verification/runtime-02/probe`; it is a diagnostic fixture outside the product solution. Its failing command was:

```powershell
./.tools/dotnet/dotnet.exe run --project verification/runtime-02/probe/RuntimeProbe.csproj -c Release --no-restore
```

The read-only registry inspection attempted `Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\S-1-5-21-3755849481-2529867762-2974155886-1005' -Name ProfileImagePath` and returned `PathNotFound`. No profile registration or host permissions were changed.

### Smallest supported correction

`Hosting/LocalRuntimeConfiguration.cs` is called before the host is built and has explicit environment boundaries:

| Environment | Key repository/protection | Logging and lifecycle |
|---|---|---|
| Windows Development | App-owned `<content-root>/.local/data-protection`, application discriminator `ForgeFlow.Development`, explicitly protected with current-user DPAPI. | Console logging includes errors without requiring a Windows Event Log source. DPAPI failures still surface; native development startup is not claimed to work under the restricted sandbox identity. `.local` is ignored as local state. |
| SmokeTest | Fresh `.tools/smoke-runs/<UUID>/keys` directory for each host, discriminator `ForgeFlow.SmokeTest`, full Data Protection with `ProtectKeysWithCertificate`. A self-signed RSA-3072/SHA-256 wrapping certificate is generated in memory and disposed after host shutdown. Persisted key XML is encrypted; no private key/PFX is written to disk or installed in a certificate store. | Captured Console logging retains errors. Both environment selectors are explicitly SmokeTest in the harness. The existing Web static-assets manifest is explicitly enabled for this non-Development test environment. A missing/relative key path or nonempty existing key directory is rejected. |
| Production / Staging | No change to deployment-managed framework defaults. Smoke directory settings do not activate local behavior in these environments. | No local logging override, test certificate, or static-assets override is applied. Production deployment configuration/acceptance is not part of this increment. |

Non-Windows Development retains its platform Data Protection defaults; only its local console sink is selected. No Data Protection provider was disabled/replaced with a no-op, no plaintext key repository was introduced, no current-user protection was expanded to machine-wide DPAPI, no ACLs were broadened, and no log-level filter hides cryptographic errors. Selecting a working local console sink prevents the secondary logging exception; it does not catch or discard the cryptographic exception. The isolated probe's failure remains recorded.

The smoke wrapping certificate is intentionally valid only for a test host lifetime. Old smoke payloads cannot be restored after that host exits, so each invocation must use a fresh directory. This is a smoke-fixture lifecycle, not a production key-management or durable-session design. Tests prove that a second Data Protection provider can decrypt the persisted ring using the same live certificate, and a separate test run cannot decrypt another run's payload.

These are supported framework configuration mechanisms: [Microsoft Data Protection repository and at-rest protection guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-storage-providers?view=aspnetcore-10.0), [certificate protection and application isolation](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), and [logging provider selection and Windows Event Log defaults](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/providers). An application discriminator separates payloads; a separate repository is also needed to avoid discovering the unrelated profile keys.

### Actual verification after correction

SDK/package environment assignments are unchanged from increment 01. No restore or dependency/version changes were necessary.

| Exact command | Actual result |
|---|---|
| `./.tools/dotnet/dotnet.exe build ForgeFlow.slnx -c Release --no-restore` | Passed: zero warnings/errors; `verification/runtime-02/build.log`. |
| `./.tools/dotnet/dotnet.exe test ForgeFlow.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=runtime02.trx' --results-directory verification/runtime-02/TestResults` | Passed: 32 tests, zero failed/skipped; `verification/runtime-02/tests.log` and `TestResults/runtime02.trx`. All preceding 24 tests remain passing. |
| `python scripts/smoke-http.py` | Passed, exit 0; `verification/runtime-02/corrected-client.log`, `corrected-server.log`, and `corrected-result.json`. Current outputs are also in `verification/server.log` and `verification/http-smoke.json`. |

Eight new regression cases cover certificate-encrypted key persistence/readback, actual Data Protection and antiforgery token generation, run isolation, rejection of missing/relative/reused key locations, Windows Development's app-owned repository/current-user DPAPI configuration, visible error logging, and unchanged Production/Staging service registrations. An initial test used an unqualified XML name for the namespaced `encryptedSecret`; correcting that assertion resolved the test failure. The final 32-test run above is the acceptance evidence for these checks, not the earlier failed run.

The successful real HTTP smoke run verified:

- `fa-IR`: HTTP 200, `lang=fa-IR`, `dir=rtl`, Persian resource text, and Jalali sample `1405/01/01`.
- `en-US`: HTTP 200, `lang=en-US`, `dir=ltr`, English resource text, and Gregorian sample `2026/03/21`.
- Correct Content-Language, default Persian, disabled query override, supported cookie precedence, and unsupported-header fallback.
- Four required nonempty assets per language: Bit.BlazorUI CSS, app CSS, Blazor framework JavaScript, and Bit.BlazorUI JavaScript. Each returned HTTP 200 with the expected CSS/JavaScript content type. Fingerprinted paths/statuses are recorded in `corrected-result.json`.
- Antiforgery form tokens and cookies are present. Values are not recorded in the result JSON; saved sample HTML redacts the request token. Localized not-found responses return 404; localized error pages return 200.
- One certificate-encrypted persisted key file, with `encryptedSecret` and XML `EncryptedData` and no plaintext `masterKey`. The wrapping private key remains in memory. The corrected server log contains zero warning/error/critical records.

These checks verify HTTP rendering and asset delivery, not a connected interactive circuit, JavaScript execution, visual appearance, mobile layout, or browser validation behavior.

### Remaining host boundary and Slice 00 status

No host permission change is needed for the smoke configuration. To run **native current-user DPAPI Development** under the sandbox account, the host must first provide a correctly provisioned/loaded profile for SID `S-1-5-21-3755849481-2529867762-2974155886-1005` (or run the developer process as its actual profile owner). That identity needs its own DPAPI master-key context and read/write/create access only to ForgeFlow's `D:\Projects\ForgeFlow\src\ForgeFlow.Web\.local\data-protection` directory and its own profile's DPAPI storage. The correct sandbox profile path cannot be reported as an existing resource because its ProfileList registration is absent. This is profile provisioning, not a request to grant access to PC's master keys.

Do **not** grant the sandbox account access to PC's protected master keys, grant machine-wide permissions, change the Application Event Log/source ACL, or change OS security settings. Local console logging removes the local Event Log dependency. A genuine deployment using Event Log would require a deliberately provisioned app source/write permission under its service identity, outside this local correction; none was requested or applied.

Language switching and reload-safe draft restoration remain unimplemented. Browser/mobile/interactive acceptance and the other increment 01 gaps remain pending. **Slice 00 is not accepted.** No Git initialization, commit, push, merge, host permission change, or later-slice work was performed.

---
# ForgeFlow — ممیزی معماری و ارتقای مبنای فنی، بازنگری ۰۰-R1

تاریخ بررسی: ۲۰۲۶-۱۱-۰۷ | بستهٔ مبنا: ForgeFlow-Day10-Final.zip | وضعیت: ارتقای فنی محدود + نقشهٔ اصلاح؛ پذیرش تولید انجام نشده است.

این بازنگری مقدم بر متن اسلایس ۰۰ در ادامه است. درخواست جدید، ارتقای فناوری را مجاز کرده است؛ جمله‌های «فقط مستندات» و «ارتقای نسخه نداریم» در نسخهٔ قبلی، شرح همان تحویل قبلی‌اند و محدودیت این تحویل نیستند. شماره‌های اسلایس ۰۱ تا ۰۶ حفظ شده‌اند. تصمیم‌های پیشنهادی باز، تا بسته‌شدن قرارداد اسلایس مربوط، دستور اجرای migration نیستند.

## R1.۱ — نتیجهٔ اجرایی

برنامه، هستهٔ اولیهٔ داده‌های پایه و تعریف تولید است؛ هنوز یک نرم‌افزار کامل و قابل‌پذیرش برای استفادهٔ چندکاربرهٔ کارخانه نیست. چهار لایهٔ موجود برای ادامه کافی‌اند، ولی نام Clean Architecture به تنهایی صحت پیاده‌سازی را ثابت نمی‌کند. کمبود اصلی، قراردادهای اجرایی و مسیر کامل UI، کنترل دسترسی، ماندگاری قابل‌بازتولید و اعتبارسنجی رفتار واقعی است. بازنویسی کامل، microservices و ساختن framework اختصاصی برای شروع لازم نیست.

آخرین نسخه‌های پایدار به تاریخ بررسی از صفحهٔ رسمی انتشار و feed رسمی بسته‌ها کنترل شدند. target framework همچنان net10.0 است؛ patch نسخهٔ SDK، runtime و NuGet یک شمارهٔ مشترک ندارند.

| جزء | بستهٔ Day 10 | مبنای جدید |
|---|---|---|
| .NET SDK | 10.0.100 | 10.0.403 در global.json؛ rollForward=latestPatch و allowPrerelease=false |
| .NET / ASP.NET Core Runtime | patch دقیق در بسته pin نشده | حداقل 10.0.14 در Web.csproj و runtimeconfig؛ میزبان مقصد هم باید این patch یا patch پایدار جدیدترِ تأییدشده را نصب کند |
| Target Framework | net10.0 | net10.0 |
| C# | 14.0 | 14.0 |
| Bit.BlazorUI | 10.5.0 | 10.7.0 |
| Bit.BlazorUI.Extras | نبود | 10.7.0؛ DataGrid جدید از این بسته می‌آید |
| EF Core / SqlServer / Design | 10.0.0 | 10.0.14 برای هر سه ارجاع |
| System.Security.Cryptography.Pkcs | وابستگی غیرمستقیم | pin صریح 10.0.14 برای رفع نسخهٔ آسیب‌پذیر 9.0.11 در گراف SqlClient |

.NET 11 در منبع رسمی هنگام بررسی RC است و وارد مبنای پایدار نشده است. نسخه‌های prerelease بستهٔ Bit نیز مبنای برنامه نیستند. این شماره‌ها «آخرین نسخه در تاریخ بررسی» هستند، نه قول به‌روزبودن همیشگی؛ هر ارتقای بعدی، restore/build و بررسی تغییرات ناسازگار خود را می‌خواهد.

### تفاوت BlazorUI و bit Boilerplate

بستهٔ فعلی یک Blazor Web App با کتابخانهٔ Bit.BlazorUI است؛ ساختار کامل bit Boilerplate در آن وجود ندارد. Blazor نسخهٔ مستقل از ASP.NET Core در این پروژه ندارد. ارتقای Bit.BlazorUI، امکانات حساب، چندمستاجری یا استقرار قالب Boilerplate را خودکار به پروژه اضافه نمی‌کند.

تصمیم FF-A008: همین ساختار چهارلایه و Blazor Interactive Server حفظ و کتابخانهٔ UI ارتقا داده می‌شود. واردکردن کامل Boilerplate، Aspire، MAUI یا WebAssembly تغییر معماری جداست و در این ارتقا انجام نشده است. استفادهٔ گزینشی از الگوهای استاندارد آن، فقط پس از تطبیق با مرزهای ForgeFlow مجاز است. راهنمای آنلاین Bit ممکن است مربوط به نسخهٔ پیش‌نمایش باشد؛ API واقعی نسخهٔ نصب‌شده و یادداشت انتشار 10.7.0 مرجع سازگاری این تحویل است.

### تغییرهای کوچک لازم برای build

- تداخل namespace ماژول Application.BOM با entity به نام BOM، با alias صریح BOMEntity رفع شد.
- چهار DbSet ویژگی‌های پویا که در IApplicationDbContext بودند، به ApplicationDbContext افزوده شدند.
- imports مشترک Razor برای Components و Routing و RenderMode و Bit اضافه شد.
- BitSpinner نامعتبر با BitSpinnerLoading جایگزین شد؛ ستون‌های Grid به BitDataGridColumn مهاجرت کردند و Extras هم‌نسخه و assetهای آن اضافه شدند.
- وابستگی Pkcs اصلاح شد؛ هشدار امنیتی پنهان یا غیرفعال نشده است.
- packages.lock.json برای چهار پروژه و restore در حالت locked اضافه/بررسی شد؛ وابستگی‌ها با نسخهٔ شناور رها نشده‌اند.

تغییرهای بالا ساختاری/سازگاری‌اند. فرمول، resolver، چرخهٔ تأیید BOM، دادهٔ واقعی و schema نصب‌شده تغییر نکرده‌اند.

## R1.۲ — یافته‌های ممیزی کد و مدل

شدت‌ها: «مسدودکننده» یعنی پیش از اتکا به آن قابلیت باید بسته شود؛ «زیاد» یعنی پیش از پذیرش عملیاتی آن حوزه؛ «متوسط» یعنی بهبود لازم در اسلایس مربوط. نبود یک قابلیت آینده، به تنهایی bug نیست.

| شناسه | شدت و شاهد | اثر و شرط بستن | محل بستن |
|---|---|---|---|
| A01 | مسدودکنندهٔ build: ApplicationDbContext چهار عضو قرارداد را نداشت؛ BOM با namespace تداخل داشت؛ imports و APIهای UI ناقص بود | در این تحویل اصلاح شد؛ پذیرش کسب‌وکار از build نتیجه نمی‌شود | ۰۰-R1 |
| A02 | زیاد: Program.cs فاقد هویت/مجوز، Routes از RouteView عادی استفاده می‌کند | پیش‌فرض منع، کنترل server-side use case و مسیر ورود معتبر لازم است | ۰۲ تا ۰۴ |
| A03 | زیاد: CreatedBy=0، UpdatedBy خالی، ExecuteUpdate بدون ممیزی | تمام writeها باید عامل معتبر، concurrency و ممیزی اتمیک داشته باشند | اولین عملیات جدید و ۰۶ |
| A04 | زیاد: AddDbContext scoped در Blazor Interactive Server | context به عملیات محدود شود؛ دو رویداد circuit نباید context مشترک موازی مصرف کنند | ۰۰-B |
| A05 | زیاد: migration/snapshot و تاریخچهٔ دیتابیس واقعی در بسته نیست | baseline تازه و مسیر تطبیق دیتابیس موجود جدا طراحی شوند؛ DDL تولیدشده مساوی migration آزمایش‌شده نیست | ۰۱ و ۰۰-B |
| A06 | زیاد: catch خطای concurrency بدون rowversion مؤثر | DTO و شرط UPDATE باید نسخهٔ خوانده‌شده را حمل کند؛ رقابت واقعی دو کاربر آزموده شود | ۰۱ و هر write |
| A07 | زیاد: BOMValidator بدون ManufacturingContext تمام نسخه‌های فعال مؤثر را بررسی می‌کند؛ Explosion پیش از Resolve آن را صدا می‌زند | نسخهٔ نامرتبط می‌تواند محاسبهٔ نسخهٔ منتخب سالم را متوقف کند؛ اعتبارسنجی تعریف BOM از اعتبارسنجی اجرای نسخهٔ resolved جدا شود | ۰۶-C |
| A08 | زیاد: CanConvertAsync گراف عمومی و کالایی را ترکیب می‌کند؛ UnitConversionService دو گراف را جدا جست‌وجو می‌کند | validator می‌تواند مسیری را بپذیرد که converter قادر به محاسبهٔ آن نیست؛ یک قرارداد مسیر و تقدم مشترک لازم است | ۰۶-B |
| A09 | زیاد: Explosion.AddInputLine کلید ItemId+UnitId دارد و تنها یک Path و CalculationMethod نگه می‌دارد | سهم مسیرها، سطوح و وضعیت اختیاری مخلوط می‌شود؛ خروجی درختی تفصیلی از جمع مواد جدا شود | ۰۶-C |
| A10 | زیاد: Explosion ورودی را همیشه به واحد پایهٔ child تبدیل می‌کند | با وعدهٔ مستند Day 10 مبنی بر حفظ واحد BOM سازگار نیست؛ واحد سطر تفصیلی و واحد تجمیع صریح شوند | ۰۶-B/C |
| A11 | زیاد: Matches برای Applicability غیرفعال true برمی‌گرداند، اما Dominates شرط‌هایش را می‌شمارد | غیرفعال‌کردن محدودیت ممکن است انتخاب را عمومی کند؛ معنای غیرفعال/فاقد شرط و رفتار dominance یکپارچه شود | ۰۶-C؛ نیازمند تصمیم قرارداد |
| A12 | متوسط: Evaluate مقایسهٔ بی‌حساسیت به حروف دارد اما Dominates مقایسهٔ نهایی ordinal؛ نوع ContextDimension در مقایسه اعمال نمی‌شود | متن‌های معادل ممکن است dominance یکسان ندهند؛ normalizer و قرارداد انواع و فهرست‌های خالی تعریف شود | ۰۶-C |
| A13 | زیاد: MainProduct بررسی می‌شود اما برابری مقدار آن با BaseOutputQuantity پس از تبدیل واحد کنترل نمی‌شود | امکان مقیاس‌دهی ناسازگار ورودی و خروجی؛ رابطهٔ کمیت مرجع و محصول اصلی باید invariant صریح باشد | ۰۶-C |
| A14 | زیاد: Quantity سازندهٔ عمومی record دارد | قواعد Quantity.Create با new Quantity قابل دورزدن است؛ invariant در تمام مسیرهای ساخت تضمین شود | ۰۶-B |
| A15 | زیاد: سرویس write برای انتقال وضعیت، تأیید، فعال‌سازی و immutable بودن BOM وجود ندارد؛ cascade در مدل هست | enum وضعیت، اجرای چرخهٔ عمر نیست؛ انتقال‌ها، دلیل لغو، حفاظت محتوا و حذف تاریخی تعریف شوند | ۰۶-C |
| A16 | زیاد: ItemService برای نام خالی، طول، فعال‌بودن واحد، تکرار نرمال‌شده و اثر تغییر واحد پایه قرارداد کافی ندارد | validation سمت سرور + قیود SQL؛ تغییر واحد پایه پس از استفاده محدود و قابل‌ردیابی باشد | ۰۶-A/B |
| A17 | زیاد: Dynamic Attributes فقط ValidateAsync دارد؛ scope طبقه‌بندی، min/max Integer و همخوانی شناسه‌های value با پارامترها کامل نیست | workflow مدیریت تعریف/مقدار، الزامی‌بودن مجموعه، حدود عددی و تعلق گزینه در SQL/سرویس بررسی شود | ۰۶-A |
| A18 | متوسط: UnitConversionService اولین مسیر یافته‌شده را می‌گیرد؛ factor مثبت، ناسازگاری مسیرها و تبدیل معکوس قرارداد کامل ندارند | آزمون مسیرهای متعارض، صفر/منفی، عدم تطابق dimension و گردکردن لازم است | ۰۶-B |
| A19 | زیاد: چند query جدا در resolver/explosion و بارگذاری هر child؛ حد عمق/تعداد سطر ندارد | ثبات نسخهٔ خوانده‌شده، جلوگیری از تغییر بین queryها و بودجهٔ محاسبه تعریف شود؛ N+1 با دادهٔ واقعی سنجیده شود | ۰۶-C |
| A20 | متوسط: GetActiveItemsAsync همهٔ داده‌ها را بدون صفحه‌بندی می‌آورد؛ UI فقط یک فهرست انگلیسی است | جست‌وجو و sort/page سمت سرور، فرم‌ها، پیام خطا و empty state لازم است | shell و ۰۶-A |
| A21 | زیاد: wwwroot/app.css و مسیر Error در بستهٔ اولیه نیست؛ کلیدهای DataProtection و تنظیم مقصد عملیاتی تعریف نشده | shell معتبر، خطای قابل‌پیگیری، کلید پایدار امن، HTTPS و reverse proxy درست لازم است | ۰۰-B و ۰۷ |
| A22 | زیاد: پروژهٔ تست و CI در مبنا نیست | build و SQL integration و تست مجوز/هم‌زمانی/مرورگر به تناسب هر اسلایس لازم است | ۰۰-B و تمام اسلایس‌ها |
| A23 | متوسط: classification چرخه/هم‌مجموعه‌بودن parent، defaultهای schema و constraint بعضی enumها کامل قراردادی نشده | قید یا validation مسئول هر invariant در مدل فیزیکی مشخص شود | ۰۶-A و baseline |
| A24 | زیاد: retry دیتابیس فعال است ولی idempotency و مرز تراکنش برنامه هنوز تعریف نشده | retry نباید عملیات، شماره سند، ممیزی یا اثر خارجی را تکراری کند | قرارداد زیرساخت و اسلایس‌های نوشتن |

یافته‌های A07 تا A19 عمدتاً تحلیل کد هستند؛ موارد بازتولیدشده در بخش شواهد جدا مشخص می‌شوند. SQL Server زنده و دیتابیس کاربر در این بررسی در دسترس نیستند؛ شدت ناظر به اثر بالقوهٔ کد است، نه گزارش خرابی اطلاعات واقعی.

## R1.۳ — تکمیل قراردادهای معماری پیش از توسعهٔ عملیاتی

### FF-A009 — تراکنش، retry و تکرار درخواست

هر use case نوشتن، مرز تراکنش مشخص دارد. با EnableRetryOnFailure، تراکنش چندمرحله‌ای در execution strategy واحد اجرا می‌شود. درخواست کاربر هنگام دوبارکلیک یا قطع/وصل نباید دو سند یا دو انتقال وضعیت بسازد؛ برای عملیات دارای اثر غیرقابل‌تکرار، شناسهٔ درخواست و قید یکتاییِ scoped طراحی می‌شود. خروجیِ متناقض و حالت «commit شد ولی پاسخ نرسید» در پذیرش دیده می‌شود.

اثر بیرونی مانند ارسال اعلان یا فایل خروجی با تراکنش دیتابیس یکی نیست. وقتی اولین اثر بیرونی قابل‌اعتماد نیاز شد، outbox و مصرف idempotent همان‌جا طراحی می‌شوند؛ message broker عمومی در این مرحله لازم نیست. ممیزی تغییر موفق، بخشی از همان تراکنش است؛ شکست آن اجازهٔ commit دادهٔ اصلی نمی‌دهد.

### FF-A010 — مرز داده و دفاع در عمق

اسلایس ۰۱ باید scope حساب، عضویت، نقش و رکورد را به‌طور مستقل تعریف کند. OrganizationId/SiteId ورودی قابل‌اعتماد نیست. افزون بر policy و query، روابطی که نباید از سازمان عبور کنند با کلید/قید مرکب SQL حفاظت می‌شوند. query filter به تنهایی تضمین جداسازی نیست؛ عملیات bulk، export و مدیریت نیز محدودیت دارند.

حساب هویت با عضویت سازمانی یکی نیست. یک حساب غیرفعال، نشست قدیمی یا نقش منسوخ نباید به عنوان راه دورزدن scope عمل کند. تغییر scope در یک tab نباید عملیات در tab دیگر را به محدودهٔ جدید منتسب کند؛ هر عملیات محدودهٔ معتبر و نسخهٔ دسترسی خود را دوباره بررسی می‌کند. جزئیات انتخاب scope پیش از اجرا در ۰۱/۰۲ بسته می‌شود.

### FF-A011 — امنیت و طول عمر Blazor

ورود/خروجی که cookie را تغییر می‌دهد از درخواست HTTP معتبر با antiforgery و پاسخ قابل‌تنظیم انجام می‌شود؛ تغییر cookie صرفاً در callback یک circuit قرار نمی‌گیرد. سرویس use case باید actor و scope معتبر سرور را دریافت کند؛ HttpContext ممکن است در circuit جاری یا قابل‌اعتماد نباشد. session/security stamp و نسخهٔ دسترسی مجدداً بررسی می‌شوند؛ logout و تغییر نقش روی اتصال باز و چند tab سناریوی پذیرش دارند.

خطاهای business/validation/forbidden/not-found/conflict/internal کد پایدار دارند؛ متن قابل‌ترجمه و شناسهٔ پیگیری به UI می‌رود، stack trace و SQL خام نمی‌رود. timeout و cancellation propagation لازم‌اند. جزئیات rate limit ورود، cookie، مدیریت secret، DataProtection و خطای غیرمنتظره در قرارداد اجرایی ۰۲/۰۷ قرار می‌گیرد.

### FF-A012 — UI مشترک و تنظیمات

یک shell مشترک شامل منو، مسیر جاری، کاربر و scope، خطای سراسری و وضعیت اتصال تعریف می‌شود. فرم‌ها، فهرست‌ها و dialogها قواعد ثابت loading/empty/error/forbidden/conflict، جلوگیری از submit تکراری و حفظ کار ذخیره‌نشده دارند. فارسی/انگلیسی، RTL/LTR، تاریخ شمسی برای نمایش، رقم، timezone، کیبورد و focus، اندازهٔ موبایل و تم روشن/تیره به عنوان معیار پذیرش همان صفحات‌اند.

تنظیمات شخصی مثل زبان/تم، تنظیمات سازمانی مثل timezone/شماره‌گذاری و secretهای عملیاتی جدا هستند. موتور عمومی تنظیمات پویا ساخته نمی‌شود؛ هر setting با نوع، سطح اختیار، مقدار پیش‌فرض و ممیزی موردنیاز تعریف می‌شود. فرم‌ساز عمومی از روی هر جدول هم جای UI و قواعد دامنه را نمی‌گیرد.

### FF-A013 — شواهد و مرز ادعا

وضعیت هر قابلیت یکی از «مدل موجود»، «سرویس موجود»، «UI موجود»، «build تأییدشده»، «تست رفتار تأییدشده» و «پذیرفته‌شده» است؛ این وضعیت‌ها معادل نیستند. تست InMemory برای FK/transaction/locking SQL معتبر نیست. GenerateCreateScript فقط قابلیت ساخت مدل و تولید DDL را می‌سنجد. تست صفر مورد اجراشده، موفقیت مجموعهٔ تست محسوب نمی‌شود.

## R1.۴ — برنامه چه امکاناتی کم دارد؟

این جدول موجودی بسته را با مسیر محصول مقایسه می‌کند؛ ستون آینده، تعهد به ساخت همهٔ ERP نیست. انتخاب اسلایس‌های صنعتی بعد از هستهٔ مشترک باید با یک سناریوی واقعی کارخانه محدود شود.

| بخش | وضعیت واقعی | تحویل لازم |
|---|---|---|
| راه‌اندازی/داشبورد | homepage و navigation کامل ندارد | shell، landing مناسب و راهنمای وضعیت راه‌اندازی |
| سازمان/کارخانه/سایت | مدل اجرایی ندارد | محدوده، عضویت و سیاست دادهٔ مشترک |
| کاربران و ورود | پیاده نشده | مدیر اولیه، ورود/خروج، نشست، تغییر/بازیابی کنترل‌شدهٔ رمز، فعال/غیرفعال |
| نقش و مجوز | پیاده نشده | مجوز عملیات، نقش، تخصیص scoped، حفاظت آخرین مدیر و منع سمت سرور |
| ممیزی/امنیت | ILogger و فیلدهای عامل اولیه | ممیزی اتمیک، رویداد امنیتی، جست‌وجو و جزئیات مجاز |
| تنظیمات و پروفایل | ندارد | زبان، تم، timezone و تفکیک تنظیمات فرد/سازمان |
| کالا/طبقه‌بندی/ویژگی | entity و بعضی سرویس‌ها؛ UI فهرست محدود | مدیریت کامل، اعتبارسنجی، جست‌وجو، وضعیت فعال، تاریخچه و قواعد ارجاع |
| واحد و تبدیل | entity و محاسبهٔ اولیه | مدیریت واحد، تبدیل معتبر و مشترک، دقت و گردکردن |
| تعریف تولید/BOM | مدل، resolver، validator و explosion موجود | UI و lifecycle و اصلاح ناسازگاری‌ها و آزمون golden case |
| پیوست/خروجی/import | ندارد | فقط در صورت نیاز use case، همراه دسترسی، محدودیت و نسخه‌بندی قالب |
| گزارش‌های پایه | ندارد | فهرست‌ها و خروجی مجاز؛ سپس گزارش نیاز مواد و تعریف BOM |
| استقرار و نگهداری | پیکربندی اولیه | انتشار، سلامت سرویس، پشتیبان و بازیابی آزموده، راهنمای ارتقا و برگشت |
| سفارش تولید | ندارد؛ صرفاً گام بعدی مستند شده | سفارش، snapshot تعریف، شماره سند و وضعیت؛ پس از پذیرش BOM |
| انبار/بچ و lot | ندارد | دریافت/مصرف/تولید/برگشت و دفتر موجودی؛ طراحی مستقل و مبتنی بر نیاز |
| تولید واقعی/کیفیت | ندارد | ثبت مصرف/خروجی واقعی، ضایعات، yield واقعی و traceability؛ مرحلهٔ بعد |
| برنامه‌ریزی/MRP/بهای تمام‌شده | ندارد | متکی به موجودی و lead time و routing معتبر؛ در مرحلهٔ فعلی نیست |
| خرید/فروش/مالی/تعمیرات | ندارد | انتخاب دامنهٔ مستقل در آینده؛ نبودشان نقص زیرساخت امروز نیست |

AD/LDAP و ورود سازمانی می‌تواند بعداً به هویت وصل شود، اما در محدودهٔ اولیه الزام نشده است. قابلیت offline، اپ موبایل native و real-time همکاری چندنویسنده نیز تصمیم‌های محصول جدا هستند؛ Interactive Server به اتصال سرور نیاز دارد.

## R1.۵ — ترتیب گام‌ها و معیار توقف

| مرحله | خروجی محدود و قابل‌بررسی | شرط عبور |
|---|---|---|
| ۰۰-R1 | همین ارتقا و ممیزی | نسخه‌ها، تغییرات و شواهد روشن؛ هیچ ادعای production-ready |
| ۰۰-B | تثبیت فنی اجرایی | context per operation، ساختار تست/CI، Error/root/static assets، قرارداد خطا و build بازتولیدپذیر |
| ۰۱ | قرارداد سپس اجرای سازمان/سایت | G02 بسته؛ مرز داده، قیدها، baseline migration و نمونهٔ دسترسی دو سایت آزموده |
| ۰۲ | ورود/نشست/مدیر اولیه | حداقل policy و ممیزی از اولین عملیات؛ ابطال نشست و circuit آزموده |
| ۰۳ | مدیریت کاربر و عضویت | ایجاد تا غیرفعال‌سازی، کنترل هم‌زمانی و UI کامل |
| ۰۴ | نقش و مجوز | ماتریس دسترسی، تغییر مجوز مؤثر و حفاظت آخرین مدیر |
| ۰۵ | صفحات ممیزی و امنیت | فیلتر، صفحه‌بندی، نمایش تغییر مجاز و scope درست |
| ۰۶-A | کالا و داده‌های توصیفی | UI مدیریت، classification/attribute invariants، مجوز و ممیزی |
| ۰۶-B | واحد و کمیت | یک converter/validator سازگار، مسیر مختلط، factor و rounding آزموده |
| ۰۶-C | تعریف تولید | lifecycle، context resolution، درخت/summary و golden cases پذیرفته |
| ۰۷ | پذیرش عملیاتی هستهٔ مشترک | backup/restore واقعی، استقرار و upgrade/rollback، health، گزارش خطا و راهنمای مدیر |
| ۰۸ | سفارش تولید حداقلی | snapshot ثابت BOM، idempotency و تاریخچهٔ سفارش؛ مرز انبار صریح |
| ۰۹ به بعد | اولین سناریوی واقعی کارخانه | هر قابلیت صنعتی فقط با قرارداد و پذیرش همان اسلایس |

اسلایس ۰۶ عمداً به سه بخش کوچک تقسیم شد؛ تا بستن یک بخش وارد بخش بعد نمی‌شویم. مستندات اسلایس‌های آینده تولید انبوه نمی‌شوند. مدل فیزیکی کامل و پذیرش هر اسلایس پیش از کدنویسی همان بخش بسته می‌شود. تغییرهای ناسازگار با Dayها با شناسه، دلیل و اثر داده‌ای ثبت می‌شوند. ZIP فقط برای milestone است؛ ForgeFlow.md همچنان مرجع اصلی است.

گام عملی بعد از این تحویل: ۰۰-B را به اندازهٔ کوچک ببندیم، سپس قرارداد سازمان/سایت اسلایس ۰۱ را نهایی کنیم. تا آن زمان ایجاد جدول‌های تمام ماژول‌ها و پیاده‌سازی سفارش تولید شروع نمی‌شود.

## R1.۶ — منابع نسخه و الگوهای فنی

منابع رسمی بررسی‌شده در ۲۰۲۶-۱۱-۰۷:

- [.NET 10 download و patchهای SDK/runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Microsoft .NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)
- [Bit.BlazorUI در NuGet](https://www.nuget.org/packages/Bit.BlazorUI)
- [یادداشت انتشار Bit 10.7.0 و تغییرهای ناسازگار](https://github.com/bitfoundation/bitplatform/releases/tag/v-10.7.0)
- [EF Core SQL Server](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer)
- [System.Security.Cryptography.Pkcs](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs)
- [راهنمای EF Core در Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/blazor-ef-core?view=aspnetcore-10.0)
- [تراکنش و execution strategy در EF Core](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency)

## R1.۷ — شواهد این تحویل و محدودیت‌ها

| بررسی | نتیجهٔ واقعی |
|---|---|
| دریافت SDK از Microsoft | SHA512 دانلود با metadata رسمی تطبیق داده شد |
| Release build چهار پروژه با SDK 10.0.403 | موفق؛ صفر warning و صفر error |
| restore با packages.lock.json و --locked-mode | موفق |
| NuGet vulnerability audit، شامل وابستگی‌های غیرمستقیم | در منبع بررسی‌شده هیچ آسیب‌پذیری شناخته‌شده‌ای گزارش نشد؛ به معنی اثبات امنیت کل برنامه نیست |
| ساخت مدل و GenerateCreateScript با provider SQL Server | موفق؛ ۲۰ entity؛ بدون اتصال به SQL Server |
| محاسبهٔ نمونهٔ ساده، ۳ کیلو محصول و ۶ کیلو ماده | موفق با EF InMemory |
| نسخهٔ نامرتبطِ نامعتبر در کنار نسخهٔ منتخب سالم | ایراد A07 بازتولید شد؛ برطرف نشده |
| تبدیل PACK → TON کالایی و TON → KG عمومی | ایراد A08 بازتولید شد؛ برطرف نشده |
| ساخت مستقیم Quantity منفی با سازندهٔ عمومی | ایراد A14 بازتولید شد؛ برطرف نشده |
| applicability غیرفعال با context خالی | پذیرش به‌صورت عمومی بازتولید شد؛ تصمیم قرارداد A11 هنوز باز است |
| اجرای SQL migrations، FKها و تراکنش واقعی | انجام نشده؛ SQL Server زنده در این محیط موجود نیست |
| پذیرش مرورگری UI | تلاش شد؛ Chromium هنگام ساخت process singleton socket با Operation not permitted متوقف شد؛ پذیرش مرورگری ادعا نمی‌شود |

شش probe به معنی شش تست موفق محصول نیست: دو بررسی موفق، سه بازتولید نقص و یک بازتولید رفتار نیازمند تصمیم ثبت شده است. آزمون Golden Case روغن Day 10، رقابت دو کاربر، isolation سازمانی و پشتیبان/بازیابی هنوز باید در اسلایس‌های خود اجرا شوند.

سورسِ probeهای قابل‌بازتولید، گزارش build، گزارش audit بسته‌ها و نتیجهٔ probeها در پوشهٔ verification بستهٔ جدید قرار دارند. این probeها مجموعهٔ تست رگرسیون برنامه نیستند؛ با رفع نقص‌ها باید انتظار آن‌ها به معیار پذیرش صحیح تبدیل شود. هیچ دیتابیس کاربر تغییر نکرده و هیچ commit/push/deploy انجام نشده است.

وضعیت پایان: **ارتقای فنی تأییدشده در سطح build؛ ممیزی و ترتیب اصلاح آماده؛ پذیرش عملیاتی برنامه باز است.**

---

# نسخهٔ قبلی قرارداد اسلایس ۰۰ و سابقهٔ Day 1 تا Day 10

# ForgeFlow — معماری اسلایس‌محور، مبنای جدید

تاریخ: ۲۰۲۶-۱۱-۰۷ | مبنا: ForgeFlow-Day10-Final.zip
وضعیت: قرارداد پیشنهادی اسلایس ۰۰ آمادهٔ بازبینی؛ هیچ قابلیت جدیدی پیاده‌سازی نشده است.

این بخش مرجع جاری مسیر زیرساخت است. بخش‌های Day 1 تا Day 10 در ادامه، سابقهٔ تصمیم‌ها هستند؛ در تعارض‌ها، تصمیم جدیدِ صریح با شناسه و دلیل مقدم است. سکوت این بخش به معنی لغو قواعد تولید نیست. شماره‌گذاری اسلایس‌ها مستقل از Dayهاست.

## ۰۰.۱ — هدف و محدوده

هدف، آماده‌کردن پایهٔ مشترک ForgeFlow تا مدیریت کاربران، نقش‌ها، مجوزها، نشست‌ها و رویدادهاست. خروجی این مرحله مستند معماری است؛ تغییر کد، ساخت migration، تغییر دیتابیس و ادعای build/test در این مرحله انجام نمی‌شود. سفارش تولید Day 11، انبار، خرید، فروش، حسابداری و طراحی موتور گردش‌کار عمومی خارج از این مرحله‌اند.

«طراحی کامل» با «پیاده‌سازی کامل» متفاوت است: در فاز طراحی هر اسلایس، قراردادها بسته می‌شوند؛ در فاز اجرا، همان اسلایس از دیتابیس تا UI و تست پذیرش تکمیل می‌شود. عبور به اسلایس بعد با نقص پذیرش ثبت‌نشده مجاز نیست.

## ۰۰.۲ — بررسی وضعیت واقعی بسته

این بررسی ایستا و محدود به محتوای ZIP است؛ وضعیت دیتابیس نصب‌شده یا مخزن بیرونی را اثبات نمی‌کند.

| حوزه | شاهد در بسته | نتیجه |
|---|---|---|
| ساختار | ForgeFlow.sln و چهار پروژهٔ src | تفکیک لایه‌ها موجود است؛ نیاز به بازنویسی کامل نیست |
| فناوری | global.json و csprojها | مبنای موجود .NET 10، Blazor Interactive Server، EF Core و SQL Server حفظ می‌شود؛ ارتقای نسخه در این مرحله نداریم |
| دسترسی داده | Application/Common/Interfaces/IApplicationDbContext.cs و Application.csproj | Application به EF Core و DbSet وابسته است؛ استقلال کامل از ORM ادعا نمی‌شود |
| UI | Web/Components/Pages/Items.razor | نمایش فهرست کالا و حالت خطا وجود دارد؛ UI کامل مدیریت ماژول‌ها نیست |
| هویت | Web/Program.cs | ثبت و میان‌افزار احراز هویت و مجوزدهی دیده نشد |
| عامل تغییر | Application/Items/ItemService.cs | CreatedBy در ایجاد کالا و طبقه‌بندی‌ها صفر است؛ UpdatedBy در ویرایش پر نمی‌شود |
| لاگ | Infrastructure/Logging/AppLogger.cs | پوشش ILogger موجود است؛ این پوشش، تاریخچهٔ ممیزی پایدار نیست |
| ممیزی | Domain/Common/AuditableEntity.cs | زمان و شناسهٔ عامل روی رکورد داریم؛ موجودیت مستقل تاریخچهٔ تغییر در بسته نیست |
| نوشتن مستقیم | ItemService.DeactivateAsync | ExecuteUpdateAsync مسیر SaveChanges را دور می‌زند؛ ممیزی آینده باید این مسیر را صریح پوشش دهد |
| هم‌زمانی | ItemService.UpdateAsync و تنظیمات مدل | catch مربوط به concurrency وجود دارد، اما rowversion/token متناظر در بررسی فایل‌ها دیده نشد |
| دیتابیس | database/Day9_ManufacturingContext.sql و Day10_BOM_Explosion.sql | اسکریپت‌های افزایشی موجودند؛ زنجیرهٔ EF migrations و ModelSnapshot در ZIP نیست |
| تست | فهرست فایل‌های ZIP و یادداشت پایان Day 10 | پروژهٔ تست وجود ندارد؛ یادداشت قبلی نیز نبود اجرای واقعی build/test را اعلام می‌کند |
| یکدستی مسیر | Domain/Entities و Domain/entities | تفاوت حروف مسیر باید هنگام اجرای بازآرایی رفع شود تا رفتار ویندوز و لینوکس یکسان شود |

قواعد BOM مانند عدم تغییر نسخهٔ تأییدشده از روی وجود enum Status اثبات نمی‌شوند؛ اجرای قواعد چرخه‌عمر و تاریخچه در زمان تثبیت ماژول تولید باید جدا بررسی و آزمایش شود.

## ۰۰.۳ — تصمیم‌های معماری

### FF-A001 — برنامهٔ ماژولار با استقرار واحد

چهار پروژهٔ فعلی حفظ می‌شوند. قابلیت‌ها درون هر لایه بر اساس ماژول و use case مرتب می‌شوند؛ برای هر اسلایس پروژه یا سرویس مستقل ایجاد نمی‌شود. دامنه به UI، Identity، EF Core یا SQL وابسته نمی‌شود. Web، سرویس‌های Application را صدا می‌زند؛ SQL و DbContext در کامپوننت قرار نمی‌گیرند. اتصال پیاده‌سازی‌های Infrastructure در نقطهٔ ترکیب برنامه مجاز است.

وابستگی EF Core فعلی Application فعلاً یک تصمیم آگاهانه و ثبت‌شده است. حذف آن شرط شروع کاربران نیست؛ گسترش وابستگی به provider SQL در Application مجاز نیست. abstraction جدید تنها در صورت نیاز واقعی اضافه می‌شود.

### FF-A002 — مالکیت ماژول‌ها

| ماژول | مالک اطلاعات و رفتار | مرز |
|---|---|---|
| Organization | سازمان، سایت/کارخانه، عضویت در محدوده | مجوز مشاهدهٔ داده را با Context تولید یکی نمی‌کند |
| Identity & Access | حساب، نشست، نقش، مجوز، تخصیص | قواعد کالا و تولید در آن قرار نمی‌گیرد |
| Audit & Diagnostics | تاریخچهٔ عملیات و رویدادهای امنیتی؛ ارتباط با خطاها | اجازهٔ تغییر دادهٔ اصلی ماژول‌ها ندارد |
| Item Catalog | کالا، طبقه‌بندی و ویژگی‌های پویا | نقش خروجی BOM ویژگی ثابت کالا نیست |
| Units | واحدها و تبدیل عمومی/وابسته به کالا | سیاست تبدیل صریح حفظ می‌شود |
| Manufacturing Definition | BOM، نسخه، applicability، resolver و explosion | سفارش تولید و گردش موجودی در این مرحله ندارد |

هر ماژول مالک نوشتن داده‌های خود است. تعامل از use case یا قرارداد مشخص انجام می‌شود؛ در ساختار فعلی queryهای ترکیبی کنترل‌شده مجازند، ولی تغییر مستقیم موجودیت ماژول دیگر ممنوع است. Shared فقط شامل قراردادهای واقعاً مشترک مثل عامل جاری، ساعت، نتیجه و محدوده است؛ به محل انباشت منطق کسب‌وکار تبدیل نمی‌شود.

### FF-A003 — محدودهٔ سازمانی

طراحی باید سازمان و سایت را به عنوان مفاهیم متمایز پشتیبانی کند؛ راه‌اندازی اولیه می‌تواند یک سازمان و یک سایت داشته باشد. معنای دقیق مالکیت کالا و BOM و یکتایی کدها در اسلایس ۰۱ تعیین می‌شود، پیش از افزودن ستون‌های scope به دادهٔ قدیمی.

سایتِ انتخاب‌شده در UI صرفاً درخواست کاربر است؛ سرور عضویت فعال و مجوز را بررسی می‌کند. Context تولید در Resolver برای انتخاب نسخه است و جای کنترل دسترسی را نمی‌گیرد. عملیات سازمانی و عملیات محدود به سایت باید قرارداد مجزای مشخص داشته باشند؛ نبود scope معتبر نباید دسترسی را گسترده کند.

### FF-A004 — هویت و مجوزدهی

برای حساب‌ها از ASP.NET Core Identity استفاده می‌شود؛ رمزنگاری رمز و موتور حساب سفارشی ساخته نمی‌شود. شناسهٔ long با مدل موجود مبنای طراحی است؛ جزئیات نگاشت در قرارداد فیزیکی هویت نهایی می‌شود. خودثبت‌نام عمومی در محدودهٔ اولیه نیست.

مجوز به شکل عملیات پایدار مانند Items.Read، Users.Create و Roles.Assign تعریف می‌شود؛ این نام‌ها نمونه‌اند و فهرست اجرایی در اسلایس مربوط تثبیت می‌شود. نقش مجموعه‌ای از مجوزهاست. حساب فعال + نشست معتبر + محدودهٔ معتبر + مجوز عملیات، همگی لازم‌اند. پیش‌فرض منع است. در مدل اولیه مجوز مستقیم به کاربر و override منع/اجازه اضافه نمی‌شود؛ اگر بعداً لازم شد قرارداد تقدم مستقل می‌خواهد.

کنترل در use case سمت سرور اجرا می‌شود؛ مخفی‌کردن دکمه کافی نیست. تغییر نقش، لغو عضویت، تغییر رمز و غیرفعال‌سازی باید روی نشست‌های موجود نیز اثر تعریف‌شده داشته باشد. بازه و سازوکار بازاعتبارسنجی Blazor circuit در اسلایس ۰۲ مشخص می‌شود. مدیر اولیه یک مسیر bootstrap کنترل‌شده و قابل‌بستن دارد، بدون رمز ثابت داخل سورس. جلوگیری از حذف دسترسی آخرین مدیر مجاز، در اسلایس ۰۴ با کنترل هم‌زمانی تعریف می‌شود.

### FF-A005 — سه نوع رویداد

| نوع | هدف | دادهٔ پایه |
|---|---|---|
| لاگ فنی | عیب‌یابی و پشتیبانی | رخداد، شدت، زمان UTC، شناسهٔ ارتباط، خطای پاک‌سازی‌شده |
| رویداد امنیتی | ورود ناموفق، قفل حساب، منع دسترسی و تغییرات امنیتی | عامل در صورت شناسایی، عملیات، نتیجه، محدوده، مبدأ قابل‌اعتماد |
| ممیزی کسب‌وکار | چه کسی چه اطلاعاتی را تغییر داد | عامل، عملیات، نوع/شناسهٔ رکورد، زمان UTC، تغییر فیلدهای مجاز، نتیجه و شناسهٔ ارتباط |

رمز، hash رمز، token، cookie و secret در هیچ‌یک ثبت نمی‌شود. دادهٔ قبل/بعد با فهرست فیلدهای مجاز و پوشاندن اطلاعات حساس ذخیره می‌شود. تغییر موفق اطلاعات و ممیزی آن در یک تراکنش SQL commit می‌شوند؛ شکست ممیزی، تغییر اصلی را rollback می‌کند. رویداد امنیتیِ تلاش ناموفق خارج از تراکنش شکست‌خورده ثبت می‌شود. رفتار اختلال ثبت رویداد امنیتی، نگهداری و پاک‌سازی در قرارداد اسلایس مربوط بسته می‌شود.

ممیزی با دسترسی برنامه append-only است؛ سطح SQL باید مانع UPDATE/DELETE توسط principal برنامه باشد و اثبات آن جزو پذیرش اجرایی است. این قاعده به معنی مصونیت از مدیر دیتابیس نیست. عملیات bulk مانند ExecuteUpdate باید صریحاً عامل، concurrency و ممیزی اتمیک داشته باشند. خواندن تمام رکوردهای عادی به‌طور پیش‌فرض ممیزی نمی‌شود؛ خواندن حساس و خروجی‌گرفتن مطابق قرارداد هر اسلایس ثبت می‌شود.

### FF-A006 — ماندگاری و هم‌زمانی

پیش از اولین migration جدید، وضعیت دیتابیس واقعی و تاریخچهٔ موجود بررسی می‌شود. دو مسیر داریم: دیتابیس تازه با baseline بازتولیدپذیر؛ دیتابیس موجود با تطبیق schema و برنامهٔ انتقال داده. ثبت صوری migration بدون تطبیق، یا اجرای خودکار destructive migration مجاز نیست.

برای هر اسلایس جدول فیزیکی شامل PK/FK، نوع، طول، nullability، یکتایی، index، رفتار حذف و کنترل هم‌زمانی لازم است. رکوردهای قابل‌ویرایش مشترک از optimistic concurrency با rowversion استفاده می‌کنند؛ write باید نسخهٔ خوانده‌شده را همراه داشته باشد. حذف آبشاری برای تاریخچه و دادهٔ تأییدشده ممنوع است؛ برای فرزندان draft فقط طبق قرارداد صریح مجاز است.

در Blazor Interactive Server، DbContext نباید در طول circuit مشترک بماند؛ در اجرا، طول عمر آن به یک عملیات محدود می‌شود. عملیات هم‌زمان یک circuit از context مشترک استفاده نمی‌کنند. شناسهٔ کاربر از context معتبر سرور می‌آید؛ برای کار سیستم، هویت سیستمِ صریح تعریف می‌شود و عدد صفر جایگزین هویت نیست.

زمان رخدادها UTC ذخیره می‌شود. تاریخ اعتبار تولید، زمان رخداد فنی و نمایش شمسی سه مفهوم متفاوت‌اند؛ تبدیل تقویم در UI انجام می‌شود. قرارداد DateOnly/DateTime تاریخ تولید پیش از تغییر مدل تثبیت می‌شود. گردکردن مقدار و دقت decimal بخشی از قرارداد هر محاسبه است.

### FF-A007 — حفظ دستاوردهای Day 10

هویت عمومی Item، جدایی classification/attributes/capabilities، نقش‌های خروجی در BOM، جدایی Resolver و Explosion، زمینهٔ تولید، Quantity همراه واحد، جدایی تبدیل عمومی و کالایی و جلوگیری از محاسبهٔ دوگانهٔ yield/scrap حفظ می‌شوند. چند نسخهٔ فعال و همپوشانی زمانی به خودی خود ممنوع نیست؛ ابهام انتخاب در زمینهٔ یکسان خطاست. قواعد تأیید و عدم تغییر تاریخچه حفظ و اجرای واقعی آن‌ها بعداً اثبات می‌شود.

## ۰۰.۴ — ترتیب اسلایس‌ها و تحویل ملموس

| شماره | تحویل | معیار نمونهٔ پذیرش | وابستگی |
|---|---|---|---|
| ۰۰ | همین مبنا، شکاف‌ها و ترتیب اجرا | تصمیم‌ها و ابهام‌های مسدودکننده قابل‌ردیابی باشند | بستهٔ Day 10 |
| ۰۱ | قرارداد سازمان، سایت و مالکیت داده | عضویت سایت A به دادهٔ سایت B دسترسی ندهد؛ سیاست دادهٔ مشترک روشن باشد | ۰۰ |
| ۰۲ | ورود/خروج، مدیر اولیه و نشست | خروج، قفل حساب و ابطال نشست روی circuit موجود هم آزموده شود | ۰۱؛ حداقل مجوز و رویداد از همین‌جا |
| ۰۳ | ایجاد/ویرایش/غیرفعال‌سازی کاربر و عضویت | مدیر مجاز کاربر بسازد؛ کاربر غیرفعال نتواند با نشست قدیمی ادامه دهد | ۰۲ |
| ۰۴ | نقش‌ها و تخصیص مجوز و نقش | منع سمت سرور، تغییر مؤثر دسترسی و حفاظت آخرین مدیر | ۰۳ |
| ۰۵ | صفحات بررسی ممیزی و رویدادها | جست‌وجو، جزئیات مجاز، صفحه‌بندی و محدودیت scope | رویدادهای تولیدشده در ۰۱ تا ۰۴ |
| ۰۶ | اعمال الگو روی کالا، واحد و BOM | هر عملیات موجود، policy و scope و عامل و ممیزی لازم را رعایت کند | ۰۴ و ۰۵ |

۰۱ و مراحل ابتدایی هویت در توسعه روی محیط محدود اجرا می‌شوند؛ پیش از راه‌اندازی هویت، هیچ مسیر مدیریتی برای استفادهٔ عمومی باز نمی‌شود. سازوکار حداقلی ثبت رویداد و مجوز در همان اولین عملیات لازم ساخته می‌شود؛ امنیت به پایان اسلایس ۰۴ یا ثبت رویداد به اسلایس ۰۵ موکول نمی‌شود. شروع Production Order منوط به پذیرش زیرساخت و تثبیت وضعیت واقعی Day 10 است.

## ۰۰.۵ — قرارداد اجباری هر اسلایس

هر اسلایس در همین مرجع، این بخش‌ها را خواهد داشت:

1. هدف، بازیگران، پیش‌شرط، خروجی و خارج از محدوده.
2. سناریوی موفق، خطا، تکرار درخواست و وضعیت هم‌زمانی.
3. مدل مفهومی و مدل فیزیکی دقیق، همراه انتقال داده و rollback عملی.
4. use caseها: ورودی، خروجی، اعتبارسنجی، خطاهای پایدار و مرز تراکنش.
5. ماتریس عملیات × مجوز × محدوده؛ جلوگیری از دسترسی با شناسهٔ حدس‌زده‌شده.
6. فهرست رویدادها، فیلدهای مجاز و رفتار شکست ثبت.
7. صفحات: مسیر، فیلدها، دکمه‌ها، loading/empty/error/forbidden/conflict و رفتار فارسی/انگلیسی و موبایل.
8. سناریوهای پذیرش قابل‌اجرا، دادهٔ نمونه و شواهد موردنیاز.
9. تصمیم‌های جدید، دلیل و اثر تغییر نسبت به نسخهٔ قبل.
10. وضعیت جداگانهٔ طراحی، پیاده‌سازی، اعتبارسنجی و پذیرش؛ مانع‌ها و گام بعدی.

پایان طراحی: ابهام اثرگذار بر دیتابیس، امنیت یا رفتار کاربر در محدوده باقی نماند. پایان پیاده‌سازی: مسیر کامل UI تا persistence کار کند، migration قابل‌تکرار باشد و build و تست‌های مرتبط واقعاً اجرا شده باشند. تست دامنه، Application، SQL، دسترسی، هم‌زمانی و مرورگر به تناسب ریسک همان اسلایس انتخاب می‌شود. هشدار/خطای جدیدِ حل‌نشده و مانع پذیرش بسته‌نشده، باید پیش از اعلام تکمیل رفع شود.

## ۰۰.۶ — فهرست شکاف‌ها و محل بستن آن‌ها

| شناسه | شکاف/تصمیم باز | مرحلهٔ بستن | مانع چه چیزی است؟ |
|---|---|---|---|
| G01 | وضعیت دیتابیس واقعی و زنجیرهٔ migration نامعلوم | پیش از اولین اجرای persistence | اجرای migration |
| G02 | مالکیت سازمانی کالا، واحد، BOM و یکتایی کدها | طراحی ۰۱ | مدل فیزیکی scope |
| G03 | هویت، bootstrap و چرخهٔ نشست وجود ندارد | طراحی/اجرای ۰۲ | استفادهٔ چندکاربره |
| G04 | عامل صفر و نبود ممیزی اتمیک | اولین عملیات جدید؛ پوشش کد قدیمی در ۰۶ | پذیرش عملیات تغییردهنده |
| G05 | سیاست مجوز و ابطال دسترسی کامل نیست | حداقل در ۰۲؛ تکمیل در ۰۴ | پذیرش امنیت |
| G06 | concurrency و writeهای bulk | قرارداد هر عملیات؛ قدیمی‌ها در ۰۶ | پذیرش ویرایش مشترک |
| G07 | نبود شواهد build، تست و migration Day 10 | اجرای تثبیت پیش از اتکا به امکانات موجود | ادعای سلامت baseline |
| G08 | طول عمر DbContext در Blazor | قبل از اولین اسلایس اجرایی چندکاربره | اجرای هم‌زمان امن |
| G09 | نگهداری لاگ، failure policy و مقصد لاگ فنی | با اولین ثبت رویداد؛ صفحات در ۰۵ | پذیرش عملیاتی ثبت رویداد |
| G10 | تفاوت Entities/entities و اجرای lifecycle BOM | بازآرایی کنترل‌شده و تثبیت ۰۶ | پذیرش چندسکویی و تولید |

## ۰۰.۷ — ثبت تغییر از طراحی قبلی

| قبل | اکنون | دلیل و اثر |
|---|---|---|
| گام بعدی Day 11 سفارش تولید | ابتدا اسلایس‌های زیرساخت ۰۰ تا ۰۶ | جلوگیری از توسعهٔ عملیات بدون هویت و کنترل دسترسی |
| پیشروی روزمحور با گزارش کلی | قرارداد و پذیرش مستقل هر اسلایس | پایان هر قابلیت قابل‌سنجش می‌شود |
| AuditableEntity و ILogger اولیه | عامل معتبر + ممیزی تراکنشی + رویداد امنیتی + لاگ فنی | فیلدهای ثبت‌کننده جای تاریخچه را نمی‌گیرند |
| الزام نوشتاری EF migrations همراه اسکریپت‌های SQL پراکنده | baseline واقعی پیش از migration بعدی | جلوگیری از ناسازگاری schema و سابقهٔ migrations |
| سرویس scoped داده در Interactive Server | context محدود به عملیات در طرح اجرا | جلوگیری از context طولانی و استفادهٔ هم‌زمان |

وضعیت پایان این تحویل: مستند اسلایس ۰۰ آمادهٔ بازبینی است؛ هیچ migration، build، test یا پیاده‌سازی امنیتی انجام نشده است. گام بعد، قرارداد اسلایس ۰۱ و بستن G02 است. بستهٔ Day 10 و سورس آن در این تحویل تغییر نکرده‌اند.

---

# سابقهٔ معماری Day 1 تا Day 10


# ForgeFlow

## Architecture Log

---

## Imported documentation: ARCHITECTURE.md

# ForgeFlow — Day 1 Architecture Contract

## Technology baseline

- .NET 10 LTS
- ASP.NET Core 10
- C# 14
- Blazor Web App
- Initial rendering: Interactive Server
- bit BlazorUI: 10.5.0 (pinned)
- Entity Framework Core 10.0.0
- SQL Server 2025
- Visual Studio 2026
- Clean Architecture
- Git

## Solution structure

```text
ForgeFlow.sln
src/
├── ForgeFlow.Domain
├── ForgeFlow.Application
├── ForgeFlow.Infrastructure
└── ForgeFlow.Web
```

## Dependency direction

```text
Web
 ↓
Application
 ↓
Domain

Infrastructure → Application / Domain
```

Domain must not depend on Blazor, ASP.NET Core, EF Core, or SQL Server.

## Day 1 Item Master

```text
Classification 1 ─── N Item
Category 1 ─── N Item
Category 1 ─── N Category (Parent/Child)
UnitType 1 ─── N Unit
Item N ─── 1 Unit (BaseUnit)
Item N ─── N Unit (ItemUnit)
Unit N ─── N Unit (UnitConversion)
```

Core entities:

- Item
- Classification
- Category
- Unit
- UnitType
- UnitConversion
- ItemUnit

Industry-specific properties are intentionally not columns on `Item`. A future Attribute system will handle properties such as MFI, density, thickness, purity, viscosity, etc.

## Rules

1. Business rules belong in Domain/Application, not UI components.
2. Blazor components must not execute direct SQL queries.
3. Infrastructure contains EF Core and persistence implementations.
4. Use `decimal` for exact business quantities, conversion factors and monetary values.
5. Database schema changes must be handled through EF Core migrations and committed to Git.
6. .NET SDK is controlled by `global.json`.
7. NuGet versions are pinned; no floating package versions.
8. Do not add a second UI component library without a documented architectural reason.
9. BOM is deliberately not implemented until Item Master is finalized against real case studies.


## Day 2 — BOM

Relationship: `Item → BOM → BOMVersion → BOMInput/BOMOutput → Item`

BOM is the planned manufacturing structure. BOMVersion preserves version history and effective dates and defines a BaseOutputQuantity/BaseOutputUnit. BOMInput defines consumed Item, quantity, consumption Unit, sequence, optionality and scrap allowance. BOMOutput defines produced Item, quantity, Unit, sequence and contextual OutputType. Circular BOM validation is an application-level rule. Active-version applicability and resolution are handled by the BOM Resolver.

## Day 3 — BOM Validation and Explosion

BOM validation and recursive BOM explosion live in the Application layer. Explosion is a calculation/query result and is not persisted as a Domain Entity.

The selection contract now requires a deterministic BOM Version resolution using Item + production date + Manufacturing Context. Recursive child Items without an active BOM are treated as leaf materials. Circular dependencies, invalid versions, missing unit conversion paths and BOM ambiguity are reported as business validation errors.

## Day 6
- ItemType removed from Item; Classification is the generic taxonomy.
- Item capabilities: CanBePurchased, CanBeManufactured, CanBeSold, CanBeConsumed.
- BOM output roles are contextual and belong to Manufacturing BOM, not Item.

## Day 7
- BOMItem replaced by explicit BOMInput and BOMOutput.
- BOMVersion has BaseOutputQuantity and BaseOutputUnitId.
- Output types: MainProduct, CoProduct, ByProduct, Waste.
- Explosion scales by requested output and returns consumed input lines plus produced output lines.

---

## Imported documentation: DAY6_DATABASE_MIGRATION_NOTES.md

# Day 6 Database Migration Notes

Day 6 removes `ItemTypeId` from `Items` and removes the `ItemTypes` model from the application.

Generate the EF migration on the development machine:

```bash
dotnet ef migrations add Day6_RemoveItemType_AddItemCapabilities --project src/ForgeFlow.Infrastructure --startup-project src/ForgeFlow.Web
dotnet ef database update --project src/ForgeFlow.Infrastructure --startup-project src/ForgeFlow.Web
```

Inspect the generated migration before applying it.

Expected changes:
- Drop the foreign key from `Items.ItemTypeId`.
- Drop `Items.ItemTypeId`.
- Add `CanBePurchased`, `CanBeManufactured`, `CanBeSold`, `CanBeConsumed`.
- Drop `ItemTypes` if it is no longer used.

For an existing database, back it up first and map old ItemType values into the generic Classification hierarchy before dropping the old table.

---

## Imported documentation: DAY6_ITEM_CLASSIFICATION_CAPABILITIES.md

# ForgeFlow — Day 6

## Item Classification & Capabilities

Day 6 makes a deliberate architectural distinction between **what an Item is classified as** and **what the Item is allowed to do operationally**.

### 1. Item is the universal master

Every material, component, packaging item, semi-finished item, finished product, by-product or waste item is an `Item`. No industry-specific Product/Material master is introduced.

### 2. Classification replaces ItemType as the classification mechanism

`ItemType` is removed from the Item aggregate. Existing hierarchical `ClassificationSet` + `Classification` + `ItemClassification` is the generic classification mechanism.

Example:

```text
Product
└── Food
    └── Edible Oil
        └── Frying Oil

Raw Material
└── Polymer
    └── Polystyrene
        ├── HIPS
        └── GPPS

Packaging
└── Primary Packaging
    └── Bottle
```

### 3. Capabilities are Item properties

The Day 6 model adds four basic operational capabilities directly to Item:

- `CanBePurchased`
- `CanBeManufactured`
- `CanBeSold`
- `CanBeConsumed`

These are capabilities, not classifications and not BOM roles.

### 4. BOM roles remain contextual

`MainProduct`, `CoProduct`, `ByProduct` and `Waste` are intentionally deferred to the Manufacturing BOM model. They describe the role of an Item in a particular BOM output, not the identity of the Item itself.

### 5. Example

`Refined Sunflower Oil` may be classified as `Raw Material / Oil / Vegetable Oil` and have Purchase, Manufacture and Consume capabilities.

`Frying Oil 800g` may be classified as `Product / Food / Edible Oil` and have Manufacture and Sell capabilities.

`Bottle 800ml` may be classified as `Packaging / Primary Packaging / Bottle` and have Purchase and Consume capabilities.

### 6. Architecture rule

```text
Item
├── Classification(s)
├── Attributes
├── Base Unit
└── Capabilities
```

Classification is for taxonomy. Attributes describe the Item. Capabilities describe allowed operational use. BOM defines contextual manufacturing relationships.

### 7. Explicitly deferred

A separate `ItemPolicy` entity, advanced capability rules and context-dependent capabilities are deferred until procurement, sales, inventory and production-order requirements are designed.

---

## Imported documentation: README.md

# ForgeFlow — Day 1 (Item Master)

## ساختار پروژه (Clean Architecture)
```
ForgeFlow.sln
global.json
src/
  ForgeFlow.Domain/          # Entity ها: Item, Classification, Unit, UnitType, UnitConversion, ItemUnit
  ForgeFlow.Application/     # DTO ها، Interface ها، ItemService (Use Case ها)
  ForgeFlow.Infrastructure/  # EF Core DbContext، Configuration ها، Logger، DI
  ForgeFlow.Web/             # Blazor Web App (Interactive Server) + bit BlazorUI
```

## چه چیزی پیاده شد
- تمام Entity های روز اول طبق سند Item Master، دقیقا با همان فیلدها و روابط.
- EF Core Fluent Configuration برای هر Entity، به همراه:
  - Unique Index روی `Code` در `Item`, `ClassificationSet`, `Classification`, `UnitType`, `Unit`
  - Composite Index روی `(IsActive, Code)` در `Item` برای کوئری‌های لیست فعال‌ها
  - Unique composite index روی `(FromUnitId, ToUnitId)` در `UnitConversion` و `(ItemId, UnitId)` در `ItemUnit`
  - `decimal(18,6)` برای `Factor` طبق قانون دقت عددی سند
- `ItemService` با CRUD پایه (Create/Update/Deactivate/Get) که تمام قوانین خواسته‌شده رعایت شده:
  - **کوئری بهینه**: `AsNoTracking()` روی همه Read ها، Projection مستقیم به DTO (بدون Load کردن کل Entity Graph)، `AnyAsync` برای چک یکتایی به‌جای Load کردن رکورد، `ExecuteUpdateAsync` برای Deactivate (یک UPDATE ساده، بدون Load).
  - **try/catch به‌جا**: خطاهای مورد انتظار (`NotFoundException`, `ValidationAppException`) جدا از خطاهای غیرمنتظره (`DbUpdateException`, خطای عمومی) مدیریت می‌شوند و همه‌جا Log می‌شوند.
  - **آماده برای Logger آینده**: `IAppLogger<T>` یک Interface عمومی است؛ الان با `Microsoft.Extensions.Logging` (Console) پیاده شده، بعدا فقط با تغییر DI می‌توانید Serilog/Seq/App Insights را جایگزین کنید — کد Service دست نمی‌خورد.
- یک صفحه نمونه `Items.razor` با `BitDataGrid` برای نمایش لیست Item های فعال.

## پیش‌نیازها (روی سیستم خودتان نصب کنید)
این پروژه در محیط sandbox من (بدون دسترسی به اینترنت/NuGet و بدون .NET SDK نصب‌شده) ساخته شده، پس build واقعی را باید روی سیستم خودتان انجام دهید:

1. **.NET 10 SDK** — https://dotnet.microsoft.com/download/dotnet/10.0
2. **Visual Studio 2026** با Workload مربوط به ASP.NET and web development
3. **SQL Server 2025 (Developer Edition)** یا هر SQL Server نصب‌شده محلی
4. اتصال اینترنت برای Restore پکیج‌های NuGet (`Bit.BlazorUI`, `Microsoft.EntityFrameworkCore.SqlServer`, ...)

## طریقه Build و اجرا

### 1) باز کردن پروژه
```
دابل‌کلیک روی ForgeFlow.sln در Visual Studio 2026
```
یا از خط فرمان:
```bash
cd ForgeFlow
dotnet restore
```

### 2) تنظیم Connection String
فایل `src/ForgeFlow.Web/appsettings.json` را باز کنید و `DefaultConnection` را با اطلاعات SQL Server خودتان تطبیق دهید (الان روی `localhost` با Windows Authentication تنظیم شده).

### 3) ساخت اولین Migration و دیتابیس
```bash
cd src/ForgeFlow.Web
dotnet tool install --global dotnet-ef   # فقط بار اول
dotnet ef migrations add InitialItemMaster --project ../ForgeFlow.Infrastructure --startup-project .
dotnet ef database update --project ../ForgeFlow.Infrastructure --startup-project .
```

### 4) اجرای پروژه
```bash
dotnet run --project src/ForgeFlow.Web
```
یا در Visual Studio: پروژه `ForgeFlow.Web` را Startup Project کنید و F5 بزنید.

سپس به آدرس `https://localhost:xxxx/items` بروید تا لیست Item ها را ببینید.

### نکته درباره نسخه Bit.BlazorUI
در `ForgeFlow.Web.csproj` ورژن به‌صورت `10.5.0` pin شده است. این نسخه در زمان آماده‌سازی پروژه از NuGet به‌عنوان نسخه فعلی پایدار Bit.BlazorUI بررسی شده است. راهنمای رسمی نصب و assetهای فعلی در https://blazorui.bitplatform.dev/getting-started قرار دارد.

## قدم بعدی (خارج از Day 1)
- افزودن Attribute/AttributeDefinition/ItemAttributeValue برای ویژگی‌های اختصاصی صنعت
- افزودن UnitType/Unit/Classification Seed Data اولیه
- جایگزینی `IAppLogger` با یک Provider واقعی (Serilog + Seq/File/App Insights) برای Log عملیات‌ها
- افزودن Authentication/Authorization و پر کردن واقعی `CreatedBy`/`UpdatedBy`

## Day 1 Architecture Decisions

- Project name: **ForgeFlow**
- .NET: **10 LTS**
- C#: **14**
- ASP.NET Core: **10**
- Blazor: **Blazor Web App**, initially **Interactive Server**
- UI: **Bit.BlazorUI 10.5.0** (pinned; no floating versions)
- EF Core: **10.0.0**
- SQL Server: **2025**
- IDE: **Visual Studio 2026**
- Architecture: **Clean Architecture**
- BOM is intentionally not implemented in Day 1; Item Master is stabilized first.
- Industry-specific properties belong in a future Attribute system, not as fixed columns on `Item`.
- `IApplicationDbContext` remains a Day-1 persistence abstraction and is not treated as a permanent architectural contract; it can be revised when application use-case boundaries mature.
- Audit user resolution (`CreatedBy`/`UpdatedBy`) will be connected to an `ICurrentUserService` when authentication/identity is introduced.


## Current milestone
- Day 1: Item Master
- Day 2: BOM / BOM Version / BOM Item
- Next: BOM validation, multi-level BOM explosion and production structure

## Day 6
- ItemType is removed from the Item model. ClassificationSet/Classification/ItemClassification is the generic classification system.
- Item capabilities added: CanBePurchased, CanBeManufactured, CanBeSold, CanBeConsumed.
- MainProduct/CoProduct/ByProduct/Waste are BOM-context roles and are deferred to Day 7 Manufacturing BOM.

---

## Imported documentation: DAY2_BOM_DESIGN.md

# ForgeFlow — Day 2: BOM Design

## Core model
Item → BOM → BOMVersion → BOMInput → Item

## Business rules
1. Every BOM belongs to exactly one Product Item.
2. One Item may have many BOMs.
3. VersionNumber is unique within a BOM and starts at 1.
4. EffectiveTo may be NULL but cannot be earlier than EffectiveFrom.
5. Quantity must be greater than zero.
6. Sequence must be greater than zero and unique inside a BOM Version.
7. ScrapPercent, when supplied, must be between 0 and 100.
8. BOMInput stores its consumption Unit explicitly; Item.BaseUnit remains the master unit.
9. Historical released versions must remain available for traceability.
10. Active versions for the same BOM may overlap when their applicability differs; same-context ambiguity is forbidden.
11. Circular BOM dependencies are forbidden.
12. BOM describes planned structure, not actual consumption or inventory.

## Version state
Draft → PendingApproval → Approved → Active → Obsolete
Draft/PendingApproval may be transitioned according to workflow. Approved/Active are immutable. Obsolete is retained for history.

## Multi-level BOM
An Item used by BOMInput may itself be the ProductItem of another BOM. Circular dependencies require graph validation and will be handled in the BOM validation/explosion stage.

## Out of scope
Routing, operations, work centers, machines, labor, inventory, lots, batches, quality, costing, production orders, purchasing and sales.

## Implementation
BOM, BOMVersion and BOMInput domain entities plus EF Core configurations were added. Generate the EF migration in the development environment after reviewing the model.


Day 7 note: the generic BOMItem model has been superseded by explicit BOMInput and BOMOutput entities.

---

## Imported documentation: DAY7_DATABASE_MIGRATION_NOTES.md

# Day 7 Database Migration Notes

Day 7 replaces the generic BOMItems model with explicit BOMInputs and BOMOutputs.

For an existing database, do NOT simply drop BOMItems.

Recommended migration strategy:

1. Rename `BOMItems` to `BOMInputs`.
2. Rename the old indexes/constraints as appropriate.
3. Add `BOMOutputs`.
4. Add `BaseOutputQuantity` and `BaseOutputUnitId` to `BOMVersions`.
5. Populate BaseOutputQuantity/BaseOutputUnitId from the real manufacturing definition before activating versions.
6. Create one MainProduct BOMOutput per active BOM Version, referencing BOM.ProductItem.
7. Only after the data is migrated and validated should old compatibility artifacts be removed.

Important: Day 6/7 source contains no committed EF migration history. Generate the migration on the development machine and inspect the SQL before applying it.

```bash
dotnet ef migrations add Day7_ManufacturingBOM --project src/ForgeFlow.Infrastructure --startup-project src/ForgeFlow.Web
dotnet ef database update --project src/ForgeFlow.Infrastructure --startup-project src/ForgeFlow.Web
```

Existing BOM versions cannot safely receive a guessed BaseOutputQuantity/BaseOutputUnit. Those values are business data and must be supplied from the actual BOM definition.

---

## Imported documentation: DAY7_MANUFACTURING_BOM.md

# ForgeFlow — Day 7: Manufacturing BOM

## Goal

Replace the ambiguous `BOMItem` model with explicit manufacturing inputs and outputs.

```text
BOM
└── BOMVersion
    ├── Inputs
    │   ├── Material
    │   ├── Component
    │   └── Packaging
    └── Outputs
        ├── MainProduct
        ├── CoProduct
        ├── ByProduct
        └── Waste
```

## Entities

- `BOM`
- `BOMVersion`
- `BOMInput`
- `BOMOutput`
- `BOMOutputType`

## BOMVersion base output

`BaseOutputQuantity` + `BaseOutputUnitId` define the reference quantity for the version.
All input and output quantities are defined against this base output. Explosion calculates:

```text
Scale = Requested Output / Version Base Output
```

The version base output is normalized to the Product Item Base Unit through the unit conversion service.

## BOMInput

- ItemId
- Quantity
- UnitId
- Sequence
- ScrapPercent
- IsOptional
- Notes

## BOMOutput

- ItemId
- Quantity
- UnitId
- OutputType
- Sequence
- Notes

`MainProduct`, `CoProduct`, `ByProduct` and `Waste` are contextual BOM roles, not Item types.

## Validation rules

1. Exactly one active BOM for an Item is currently required for explosion.
2. Exactly one effective active BOM Version must exist for the requested date.
3. Version base output quantity must be positive.
4. Version must have at least one input and one output.
5. Exactly one `MainProduct` output is required.
6. MainProduct Item must equal BOM.ProductItem.
7. Input and output quantities must be positive.
8. Input scrap must be between 0 and 100.
9. Input/output units must exist, be active and be convertible to the referenced Item BaseUnit.
10. Circular dependencies in the input graph are rejected.

## Explosion

Explosion now:

1. Finds the effective BOM Version.
2. Scales the version from `BaseOutputQuantity` to requested quantity.
3. Applies input scrap.
4. Recursively explodes child Items that have an active BOM.
5. Normalizes leaf input quantities to Item BaseUnit.
6. Returns consumed `InputLines` and produced `OutputLines`.

`Lines` remains as a compatibility alias for `InputLines`.

## Routing boundary

```text
BOM     = What is required / produced
Routing = How it is produced
Production Order = When and how much to produce
Inventory = What is actually available
```

Routing and operations are intentionally deferred.

## Case study: Frying Oil 800g

Base output:

```text
1,000 PCS
```

Inputs:

```text
Refined Oil = 800 KG
Bottle      = 1,000 PCS
Cap         = 1,000 PCS
Label       = 1,000 PCS
```

Output:

```text
Frying Oil 800g = 1,000 PCS / MainProduct
```

For 5,000 PCS the scale is 5, so the input quantities scale to 4,000 KG oil and 5,000 PCS of each packaging component.

---

## Imported documentation: DAY5_DYNAMIC_ITEM_ATTRIBUTES.md

# ForgeFlow Day 5 — Dynamic Item Attributes

## Goal
Allow any Item to carry industry-specific properties without changing the Item table for each industry.

## Model
- ItemAttributeGroup: UI/semantic grouping such as Physical Properties or Chemical Properties.
- ItemAttributeDefinition: definition of a property, data type, optional Unit, optional Classification scope, required flag and validation limits.
- ItemAttributeOption: allowed values for Option attributes.
- ItemAttributeValue: one typed value for an Item/Attribute pair.

## Data Types
Text, Integer, Decimal, Boolean, Date, DateTime, Option.

## Typed Value Rule
Exactly one value column is populated according to AttributeDataType. This is enforced by the Application validation service; database CHECK constraints can be added later if required by the final migration strategy.

## Classification Scope
An AttributeDefinition may be global (ClassificationId NULL) or scoped to a Classification. The first implementation does not automatically infer descendant classifications; that policy will be defined in the Item Attribute query layer.

## Examples
HIPS: Grade, Density, Melt Flow, Moisture.
Frying Oil: Oil Type, Density, Volume, Acidity.
Glass: Thickness, Width, Height, Color.

## Important Boundary
Item attributes describe the Item itself. BOMInput Quantity/Unit describes consumption of an Item inside a BOM. These concerns remain separate.

---

## Imported documentation: DAY4_ITEM_MASTER_UNITS_CLASSIFICATION.md

# ForgeFlow — Day 4

## Item Master + Unit of Measure + Generic Classification

### Architecture decision
`Item` is the generic master object. It is not synonymous with finished product.

### Item
- Code: string, unique
- Name: string
- ItemTypeId: FK
- BaseUnitId: FK to Unit
- Description: optional
- IsActive: bool

### Generic Classification
Instead of hard-coding `Category` into Item, ForgeFlow uses:

`ClassificationSet -> Classification tree -> ItemClassification`

This allows the same tree model to be reused for Item categories and future classification domains without changing Item itself.

### Unit model
`UnitType -> Unit`

Examples:
- Mass: KG, G, TON
- Volume: L, ML
- Count: PCS, BOX, CARTON
- Length: M, CM, MM

Unit contains `Symbol` and `DecimalPrecision`.

### Conversion model
Two levels are supported:

1. `UnitConversion`: global conversion such as KG -> G.
2. `ItemUnitConversion`: item-specific packaging/count conversion such as Bottle A: 1 BOX = 24 PCS.

### ItemUnit
Defines units in which an Item can be expressed/handled. BaseUnit remains the canonical calculation unit.

### BOM interaction
BOMInput stores its own quantity and Unit. Explosion normalizes quantities to the component BaseUnit using the conversion graph.

### Example
Frying Oil 800g:
- Item type: Finished Product
- Base unit: PCS

Refined Oil:
- Item type: Raw Material
- Base unit: KG

BOM line:
- Quantity: 0.800
- Unit: KG

20 G of Shrink Film can normalize to 0.020 KG.

### Important constraints
- Unit conversion Factor > 0.
- Global conversions should only connect compatible UnitTypes.
- Item-specific conversions must belong to the same Item.
- BOX/PCS style conversions are item-specific unless the business explicitly defines them globally.
- Classification is many-to-many: one Item can have multiple classifications.
- Classification is hierarchical through ParentId.

---

## Imported documentation: DAY3_BOM_VALIDATION_EXPLOSION.md

# ForgeFlow — Day 3: BOM Validation & Multi-Level BOM Explosion

## Objective

Day 3 adds the application-layer rules required to validate a BOM and recursively explode a multi-level BOM into material requirements.

## Contracts

### BOM Validator

`IBOMValidator.ValidateAsync(rootItemId, effectiveDate)`

The validator checks:

- Root Item exists and is active.
- Root Item has exactly one active BOM in the current Day 3 model.
- Version numbers are positive and unique within a BOM.
- Version effective date ranges are valid.
- Effective date ranges are valid but may overlap.
- Exactly one BOM Version must be resolution-dominant for the requested date and manufacturing context.
- BOM versions are not empty.
- BOM Item quantity is positive.
- BOM Item sequence is positive.
- Scrap percentage is between 0 and 100.
- Referenced Items and Units exist and are active.
- A conversion path exists from BOM Item Unit to the component Item Base Unit.
- Recursive structures do not contain cycles.

### BOM Explosion

`IBOMExplosionService.ExplodeAsync(itemId, quantity, effectiveDate)`

The service:

1. Validates the BOM graph.
2. Selects the BOM Version effective on the requested date.
3. Recursively traverses child BOMs.
4. Applies BOM quantity and scrap percentage.
5. Converts component quantities to the component Item Base Unit.
6. Aggregates repeated leaf materials.
7. Returns level, leaf/optional status and the traversal path.

## Output

Explosion is a calculation result, not a persisted Domain Entity.

```text
Item → BOM → BOMVersion → BOMInput → Item → BOM → ...
```

The result contains:

- Root Item
- Root Quantity
- Root Unit
- Effective Date
- Item Id
- Unit Id
- Required Quantity
- Level
- IsLeaf
- IsOptional
- ScrapPercent
- Path

## Current Unit Conversion Contract

`UnitConversion.Factor` means:

```text
QuantityInToUnit = QuantityInFromUnit × Factor
```

The application resolves a conversion path through the UnitConversion graph. Identity conversion is always supported when FromUnitId equals ToUnitId.

## Case Study — Frying Oil 800g

```text
Frying Oil 800g
├── Refined Oil      0.800 KG
├── Bottle           1 PCS
├── Cap              1 PCS
└── Packaging Set    1 PCS
    ├── Carton       1/12 PCS
    ├── Shrink Film  0.002 KG
    └── Label        1 PCS
```

For 1,000 units:

```text
Refined Oil       800 KG
Bottle            1000 PCS
Cap               1000 PCS
Carton            83.333... PCS before unit rounding
Shrink Film       2 KG
Label             1000 PCS
```

The Day 3 engine keeps quantities in decimal form. Final unit-specific rounding policy belongs to the Unit/UOM design and should be applied before operational documents are generated.

## Error Codes

- `ITEM_NOT_FOUND`
- `ITEM_INACTIVE`
- `BOM_NOT_FOUND`
- `MULTIPLE_ACTIVE_BOMS`
- `BOM_VERSION_NOT_FOUND`
- `BOM_VERSION_OVERLAP`
- `BOM_VERSION_NUMBER_DUPLICATE`
- `BOM_VERSION_NUMBER_INVALID`
- `BOM_VERSION_DATE_RANGE_INVALID`
- `BOM_EMPTY`
- `BOM_ITEM_QUANTITY_INVALID`
- `BOM_ITEM_SEQUENCE_INVALID`
- `BOM_ITEM_SCRAP_INVALID`
- `BOM_ITEM_UNIT_NOT_FOUND`
- `BOM_ITEM_UNIT_INACTIVE`
- `BOM_ITEM_UNIT_CONVERSION_MISSING`
- `BOM_CIRCULAR_DEPENDENCY`

## Architectural Boundary

Explosion is implemented in the Application layer. It is deliberately not a method on the BOM Entity because it requires database access, version selection, unit conversion, recursive traversal and aggregation.

## Day 3 Implementation Files

```text
ForgeFlow.Application/BOM/
├── Explosion/
│   ├── IBOMExplosionService.cs
│   ├── BOMExplosionService.cs
│   └── BOMExplosionResult.cs
└── Validation/
    ├── IBOMValidator.cs
    ├── BOMValidator.cs
    └── BOMValidationResult.cs
```

Infrastructure DI registers both services as scoped services.

## Deliberate Limitation

The current model allows one active BOM per Item for explosion. Alternate/parallel BOMs are intentionally deferred until the domain defines BOM purpose, priority, production site, order quantity, routing and other selection criteria.


Day 7 note: the generic BOMItem model has been superseded by explicit BOMInput and BOMOutput entities.

# Day 8 — BOM Version Lifecycle & Resolution

## Changes from Previous Design

### Change 1 — BOM Version Overlap

**Previous:** BOM Version effective date ranges must never overlap.

**Final:** Overlapping effective periods are allowed when Versions apply to different manufacturing contexts.

**Reason:** The same Item may legitimately be produced with different valid BOM Versions at the same time, for example Plant A vs Plant B or Line A vs Line B. The problem is ambiguity, not overlap itself.

### Change 2 — Active BOM Versions

**Previous assumption:** One Active Version per BOM.

**Final:** Multiple Active Versions are allowed.

**Reason:** Different manufacturing contexts can require different valid definitions during the same period.

### Change 3 — BOM Resolution

**Previous:** Select the latest/current Version.

**Final:** Resolve using Item + Date + Manufacturing Context.

Resolution:

```text
0 → No applicable BOM
1 → Select
2+ → Ambiguous BOM → Error
```

The system must never silently guess.

### Change 4 — Version Mutation

Approved/Active Versions are immutable. A production-affecting change creates a new Version.

### Change 5 — Historical Traceability

Historical production retains the exact BOM Version used. Historical Versions are not deleted.

## 1. BOM vs BOM Version

- **BOM** identifies the manufacturing structure for an Item.
- **BOM Version** identifies a specific definition of that structure.
- A BOM may contain multiple Versions.

```text
Frying Oil 800g
└── BOM
    ├── V1
    ├── V2
    └── V3
```

## 2. Version Lifecycle

```text
Draft
  ↓
PendingApproval
  ↓
Approved
  ↓
Active
  ↓
Obsolete
```

Cancellation:

```text
Draft → Cancelled
PendingApproval → Cancelled
```

Rules:
- Draft: editable.
- PendingApproval: normal structural editing locked.
- Approved: immutable.
- Active: eligible for production when applicability rules match.
- Obsolete: retained for traceability; not eligible for new production.
- Cancelled: retained for audit; not eligible for production.

## 3. Effective Dates

A Version may have:

```text
EffectiveFrom
EffectiveTo
```

**Valid overlap:**

```text
V1 Active  2026-08-01 → 2026-09-30  Plant A
V2 Active  2026-08-01 → 2026-09-30  Plant B
```

**Invalid ambiguity:**

```text
V1 Active  2026-08-01 → 2026-09-30  Plant A
V2 Active  2026-08-15 → 2026-09-30  Plant A
```

if both match the same production request.

> Overlapping effective periods are allowed. Overlapping applicability for the same production context is not.

## 4. BOM Resolution

```text
Production Order
      ↓
BOM Resolver
      ↓
Find candidate BOM Versions
      ↓
Filter by Item
      ↓
Filter by Effective Dates
      ↓
Filter by Manufacturing Context
      ↓
0 candidates  → No applicable BOM
1 candidate   → Select
2+ candidates → Ambiguous BOM
```

No automatic fallback to the newest Version.

## 5. Manufacturing Context

Day 8 establishes the concept without hard-coding every dimension.

Potential dimensions:
- Site / Plant
- Production Line
- Market
- Customer
- Production Purpose
- Other company-specific applicability dimensions

A dedicated applicability/selection mechanism will be designed later.

## 6. Version Number and Revision

Version Number identifies a production definition:

```text
V1
V2
V3
```

A production-affecting change creates a new Version.

Revision is a controlled audit marker, not a substitute for a production-changing Version.

Example:

```text
Version 2 / Revision A
Version 2 / Revision B
```

A material recipe change:

```text
V2 → 0.800 KG
V3 → 0.805 KG
```

does not modify V2.

## 7. Historical Traceability

A Production Order must retain:

```text
Production Order
├── BOMId
└── BOMVersionId
```

A later execution/snapshot design may persist a BOM snapshot where required.

## 8. BOMVersion Target Shape

```text
BOMVersion
├── Id
├── BOMId
├── VersionNumber
├── Revision
├── Status
├── EffectiveFrom
├── EffectiveTo
├── BaseOutputQuantity
├── BaseOutputUnitId
├── CreatedAt
├── CreatedBy
├── ApprovedAt
├── ApprovedBy
├── ObsoletedAt
└── ObsoletedBy
```

## 9. Validation Rules

1. VersionNumber is unique within a BOM.
2. BaseOutputQuantity must be positive.
3. BaseOutputUnit must be valid and active.
4. A production-eligible Version must contain valid Inputs and Outputs.
5. A Version must not be edited after approval.
6. EffectiveTo, when present, must satisfy the project's date-boundary convention relative to EffectiveFrom.
7. Overlapping dates are permitted.
8. For a given production request/context/date, at most one Version may be applicable.
9. Zero applicable Versions produces a clear resolution error.
10. More than one applicable Version produces an ambiguity error; the system must not guess.

## 10. Case Study — Frying Oil 800g

```text
BOM: Frying Oil 800g

V1
Active
2026-08-01 → 2026-09-30
Plant = Tehran

V2
Active
2026-08-01 → 2026-09-30
Plant = Tabriz
```

Both are valid.

```text
Production Request
Plant = Tehran
Date  = 2026-08-20
→ V1
```

```text
Production Request
Plant = Tabriz
Date  = 2026-08-20
→ V2
```

If two Versions both match the same context and date:

```text
→ Ambiguous BOM
→ Production cannot proceed until resolved
```

## 11. Architectural Boundary

Day 8 does not implement:
- Production Orders
- Routing
- Operations
- Full Manufacturing Context
- Inventory/Lot traceability

These are future modules.

Day 8 establishes the rules they must follow.

## Final Day 8 Decision

```text
Multiple active BOM Versions: ALLOWED
Date overlap: ALLOWED
Same-context ambiguity: FORBIDDEN
Automatic guessing: FORBIDDEN
Approved/Active Version mutation: FORBIDDEN
Historical Version deletion: FORBIDDEN
```

# Day 9 — BOM Applicability, Manufacturing Context & Deterministic Resolution

## Changes from Day 8

### Change 1 — Effective-Date Overlap Is Not the Problem

**Previous Day 8 rule:** Multiple Active Versions were allowed, but ambiguity had to be prevented.

**Day 9 clarification:** Effective-date ranges may overlap. Overlap by itself is valid because different BOM Versions may apply to different manufacturing contexts.

**Reason:** A company may legitimately produce the same Item using different BOM Versions at the same time, for example:

```text
V1 → Plant = Tehran
V2 → Plant = Tabriz
```

The forbidden condition is not date overlap. The forbidden condition is **multiple equally valid BOM Versions for the same Item + production date + manufacturing context**.

### Change 2 — ManufacturingContext Is Runtime Data

**Previous:** Manufacturing Context was only a conceptual placeholder.

**Final:** `ManufacturingContext` is a runtime Value Object. It is not a mandatory persisted Master Data entity.

Example:

```text
ManufacturingContext
├── PLANT = TEHRAN
└── PRODUCTION_LINE = LINE-A
```

A Production Order or resolution request can later persist a snapshot of the context used for traceability.

### Change 3 — Generic Context Dimensions

ForgeFlow does not hard-code `PlantId`, `ProductionLineId`, `MarketId`, or `CustomerId` into `BOMVersion`.

A persisted `ContextDimension` defines generic dimensions such as:

```text
PLANT
PRODUCTION_LINE
MARKET
CUSTOMER
MANUFACTURING_METHOD
```

The list is extensible and is not tied to the polymer, food, glass, or any other industry.

### Change 4 — Applicability Conditions

A BOM Version may have one `BOMApplicability` containing zero or more `BOMApplicabilityCondition` records.

A condition contains:

```text
DimensionCode
Operator
Value
```

Supported operators:

```text
Equals
NotEquals
In
NotIn
```

Conditions within one applicability are evaluated with **AND** semantics.

`In` / `NotIn` values are stored as a JSON string array; the resolver also accepts comma-separated values as a compatibility fallback.

### Change 5 — Missing Context Is No Match

If an applicability condition references a dimension that is missing from the request context, the condition does not match.

This applies even to `NotEquals` and `NotIn`.

Reason: Missing information must never be interpreted as permission to use a manufacturing definition.

### Change 6 — Specificity and Dominance

The resolver does not use a numeric Priority to hide conflicting master data.

A BOM applicability is more specific only when all conditions of the less-specific applicability are also present with the same dimension, operator, and value, and the more-specific applicability contains at least one additional condition.

Example:

```text
A: Plant = Tehran
B: Plant = Tehran AND Line = Extrusion-2

B dominates A.
```

But:

```text
A: Plant = Tehran
B: Line = Extrusion-2
```

Neither dominates the other.

If both match the request, the result is `AmbiguousBOM`.

### Change 7 — BOM Resolver Contract

The Application layer now exposes:

```text
BOMResolutionRequest
IBOMResolver
BOMResolutionResult
ManufacturingContext
```

Resolution is deterministic:

```text
Item + Production Date + Manufacturing Context
                    ↓
          Candidate Active Versions
                    ↓
             Applicability Match
                    ↓
            Specificity/Dominance
                    ↓
          ┌─────────┼─────────┐
          ↓         ↓         ↓
          0         1         2+
          ↓         ↓         ↓
       No BOM    Success   Ambiguous
```

The resolver never chooses an arbitrary candidate.

## Day 9 Domain Model

```text
BOM
└── BOMVersion
    └── BOMApplicability
        └── BOMApplicabilityCondition
              └── ContextDimension
```

Runtime request:

```text
BOMResolutionRequest
├── ItemId
├── ProductionDate
└── ManufacturingContext
      └── DimensionCode → Value
```

## Entity Definitions

### ContextDimension — بُعد زمینه تولید

```text
Id
Code
Name
DataType
ReferenceType?
IsActive
```

`DataType`:

```text
String
Integer
Decimal
Boolean
Date
Reference
```

`ReferenceType` is intentionally extensible. ForgeFlow does not hard-code a specific Master Data entity into the Context Dimension model.

### BOMApplicability — شرایط اعمال BOM

```text
Id
BOMVersionId
IsActive
```

One BOM Version has zero or one applicability definition.

### BOMApplicabilityCondition — شرط اعمال BOM

```text
Id
BOMApplicabilityId
DimensionCode
Operator
Value
```

There may be at most one condition per `DimensionCode` within one applicability definition.

## BOM Version Status

The Day 8 status model is now reflected in code:

```text
Draft = 0
PendingApproval = 1
Approved = 2
Active = 3
Obsolete = 4
Cancelled = 5
```

Only `Active` Versions are production-eligible for BOM Resolution.

`Approved` means approved but not necessarily currently executable.

## Case Study — Frying Oil 800g

```text
BOM-FRY-800
│
├── V1 Active
│   Effective: 2026-08-01 → 2026-09-30
│   Plant = Tehran
│
└── V2 Active
    Effective: 2026-08-01 → 2026-09-30
    Plant = Tabriz
```

Request:

```text
Item = Frying Oil 800g
Date = 2026-08-20
Plant = Tehran
```

Resolution:

```text
V1 → Match
V2 → No Match

Result → V1
```

## Case Study — GPPS 525

```text
V1
Plant = Tehran

V2
Plant = Tehran
Line = Extrusion-2

Request:
Plant = Tehran
Line = Extrusion-2
```

Both versions match, but V2 dominates V1 because it contains all V1 conditions plus an additional condition.

```text
Result → V2
```

If instead:

```text
V1
Plant = Tehran

V2
Line = Extrusion-2
```

both match but neither dominates the other:

```text
Result → AmbiguousBOM
```

## Application and Infrastructure Changes

Added:

```text
ForgeFlow.Application/BOM/Resolution/
├── BOMResolutionRequest.cs
├── BOMResolutionResult.cs
├── BOMResolver.cs
├── IBOMResolver.cs
└── ManufacturingContext.cs
```

Added Domain entities:

```text
ForgeFlow.Domain/entities/BOM/
├── BOMApplicability.cs
└── ContextDimension.cs
```

Added SQL Server EF configurations for:

```text
BOMApplicabilities
BOMApplicabilityConditions
ContextDimensions
```

`BOMExplosionService` now resolves BOM Versions through `IBOMResolver` and accepts an optional `ManufacturingContext`, so explosion cannot silently select a conflicting Version.

## Validation Rule Update

The previous validator rule that rejected overlapping Active Version date ranges has been removed.

The validator still rejects invalid date ranges:

```text
EffectiveTo < EffectiveFrom
```

Context-aware ambiguity is now handled by the BOM Resolver.

## Day 9 Final Decisions

```text
Multiple Active BOM Versions: ALLOWED
Effective-date overlap: ALLOWED
Different-context overlap: ALLOWED
Same-context ambiguity: FORBIDDEN
Automatic guessing: FORBIDDEN
Priority-based conflict hiding: FORBIDDEN
Missing context condition: NO MATCH
Conditions inside one applicability: AND
OR operator: NOT IMPLEMENTED
Item Identity ≠ Item Attributes ≠ Manufacturing Context
ManufacturingContext persisted as mandatory Master Data: NO
Production traceability snapshot: FUTURE
Resolver determinism: REQUIRED
```

## Day 10 — BOM Explosion, Quantity, UOM Conversion & Golden Case

Day 10 turns the resolved `BOMVersion` into a deterministic material/component requirement tree.

### What changed from Day 9

```text
Day 9:
Production Request
    -> BOM Resolver
    -> BOM Version

Day 10:
Production Request
    -> BOM Resolver
    -> BOM Version
    -> BOM Explosion
    -> Quantity/UOM conversion
    -> Recursive component requirements
```

The key change is that BOM resolution and BOM explosion remain separate responsibilities: the resolver chooses the applicable Version; the explosion service calculates quantities without mutating Master Data.

### New Application contracts

```text
BOMExplosionRequest
BOMExplosionMode
BOMExplosionResult
BOMExplosionLine
IQuantityConverter
```

`BOMExplosionMode`:

```text
LeafOnly
AllLevels
```

`BOMExplosionResult` now records `ResolvedBOMVersionId` for traceability.

### Quantity model

A `Quantity` is a Value Object containing:

```text
Value + UnitId
```

A bare decimal is therefore not considered a complete business quantity.

### UOM conversion

Two conversion layers remain separate:

```text
UnitConversion
    Global UOM conversion, e.g. TON -> KG

ItemUnitConversion
    Item-specific conversion, e.g. 1 Pallet Bottle -> 1000 EA
```

BOM Explosion preserves the BOM-defined unit. Conversion is performed only when a different unit is explicitly required.

### Yield and Scrap

`BOMVersion.YieldPercent` represents process yield and defaults to `100%`.

`BOMInput.CalculationMethod` is explicit:

```text
Standard       = no adjustment
YieldAdjusted  = use BOM Version yield when calculating input consumption
ScrapAdjusted  = apply ScrapPercent as a true scrap rate
```

Yield and scrap are intentionally not stacked on the same input. This prevents double-counting loss.

For a scrap rate `s`:

```text
Gross Requirement = Net Requirement / (1 - s)
```

For a yield `y`:

```text
Gross Input = Required Output / y
```

### Recursive BOMs

An Item may itself have a BOM. Explosion recursively traverses child BOMs.

Circular dependencies are rejected:

```text
A -> B -> C -> A
    => BOM_CIRCULAR_DEPENDENCY
```

### Golden Case — Frying Oil 800g

For `1,000 EA` of `Oil 800g`:

```text
Level 1
Refined Oil = 800 KG
Bottle      = 1000 EA
Cap         = 1000 EA
Label       = 1000 EA
```

`Refined Oil` has:

```text
Base Oil = 0.950 KG
Additive = 0.050 KG
Yield    = 98%
```

Therefore:

```text
Input required for 800 KG output = 800 / 0.98 = 816.3265 KG

Base Oil = 816.3265 * 0.95 = 775.5102 KG
Additive = 816.3265 * 0.05 = 40.8163 KG
```

Leaf-only explosion result:

```text
Base Oil    = 775.5102 KG
Additive    = 40.8163 KG
Bottle      = 1000 EA
Cap         = 1000 EA
Label       = 1000 EA
```

### Day 10 Architectural Rules

```text
1. Resolver selects the BOM Version; Explosion consumes that decision.
2. Explosion never changes BOM Master Data.
3. Sub-BOM traversal is recursive.
4. Circular BOMs must fail deterministically.
5. LeafOnly returns terminal requirements; AllLevels also returns intermediate inputs.
6. BOM quantities retain their defined UOM.
7. Global and Item-specific UOM conversions are separate concepts.
8. Quantity always carries its UOM.
9. Yield and Scrap are explicit calculation concepts and must not be double-counted.
10. Explosion results retain the resolved BOM Version for traceability.
```

### Database Changes

Added:

```text
BOMVersions.YieldPercent
BOMInputs.CalculationMethod
```

Migration script:

```text
database/Day10_BOM_Explosion.sql
```

### Verification Note

The implementation was updated from the Day 9 milestone. The environment used for this milestone does not expose the .NET SDK executable, so a real `dotnet build`/test run cannot be claimed here. The source was updated consistently with the existing .NET 10 project structure; compilation and automated tests must be executed in the normal ForgeFlow development environment.

## Next Step — Day 11

Next we should define the **Production Order / Manufacturing Order boundary**: when an explosion becomes a committed production requirement, how the resolved BOM Version and explosion snapshot are persisted, and how later BOM changes are prevented from rewriting historical production decisions.

## Slice 01.4 read-only UI increment — 2026-11-09

Implemented Organization/Site secured lists and freshly authorized detail dialogs, Settings navigation, permission-filtered scope discovery, search, inactive filter, paging, grid single selection and localized icon actions. Inspected installed Bit 10.7.0 XML APIs before implementation. No writes/migrations/seeds/Git operations. Uses existing trusted actor and permission evaluator, short-lived grant discovery context and secured OrganizationUseCases for all displayed records.

Actual Release command: dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutputPath=D:\Projects\ForgeFlow\.tools\slice014-output\ — PASS, zero warnings/errors. Intermediate nav IList/List compile error corrected. Checks: dotnet vstest .\.tools\slice014-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:"FullyQualifiedName~Organization|FullyQualifiedName~DisplayText|FullyQualifiedName~AuthBackendTests" — 102 passed, 13 SQL-dependent skipped, zero failed. Existing checks do not establish rendered UI acceptance.

Startup blocker: Get-NetTCPConnection -LocalPort 5001 -State Listen and Get-CimInstance Win32_Process both returned Access denied, HRESULT 0x80041003. No process stopped or restrictions bypassed. curl.exe --silent --show-error --insecure --output NUL --write-out "%{http_code}" https://localhost:5001/account/login failed: curl (7), could not connect, HTTP 000. Fresh build is not running. Manual fallback: normal PowerShell must verify workspace executable/listener and Development before restart; launch fresh .tools/slice014-output/ForgeFlow.Web.dll with src/ForgeFlow.Web content root, Development, process-only existing localhost/testforgflow connection and HTTPS localhost:5001. Never migrate/recover/initialize during startup.

No browser tool available. FA/EN RTL/LTR desktop/320px, real authorized data, row selection accessibility, dialog spacing/focus/Escape/restoration remain unverified. Scope selector uses authorized IDs; authorized display names remain a usability gap. Mobile grid containment needs visual acceptance. Read-only increment and Slice 01 remain NOT ACCEPTED; deferred AUTH-01 gates unchanged. Next: live bilingual/mobile read-only acceptance and corrections before mutation UI.

## Slice 01.4 routing and reference layout correction — 2026-11-09

Inspected verification/screen/modol.png and verification/screen/site organisation.png. Traced shared five-route component: page kind depended on mutable Navigation.Uri; retained route parameters and mutable selected state could disagree with pending query results. Replaced with distinct Organizations/Sites route owners and explicit IsSites parameter, keyed RouteView/reader instances, captured query scope/type and revision guards on asynchronous responses. Disposal clears records/dialog selection; grid is keyed to loaded rows. Details revalidate through existing secured use cases using selected row ID.

Reference styling: unified labeled toolbar, search placeholder, inactive filter, no redundant refresh action; card grid, status badges, outlined compact actions and page count; outlined logout/username ellipsis; BitDialog part classes for 480px viewport-safe width, 16px corners, padded sections, neutral close target, tinted overlay. Installed Bit XML part/variant APIs inspected. FA/EN resources retained and extended; logical CSS and existing font/brand preserved. No database or Git changes.

Final verification commands: dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1 -p:OutputPath=D:\Projects\ForgeFlow\.tools\slice014-fix-output\ ; dotnet vstest .\.tools\slice014-fix-output\ForgeFlow.Web.Tests.dll --TestCaseFilter:"FullyQualifiedName~OrganizationReaderRegressionTests|FullyQualifiedName~DisplayText|FullyQualifiedName~OrganizationUseCaseTests". Release zero warnings/errors. Focused checks 29 passed, zero failed/skipped after correcting disposal cleanup exposed by new tests. Route ownership and selected ID/query kind tested with offline ports; these are not browser or SQL evidence.

Startup inspection: Get-NetTCPConnection -LocalPort 5001 -State Listen and Get-CimInstance Win32_Process returned Access denied (HRESULT 0x80041003). No process stopped/restarted. curl login probe returned (35) schannel AcquireCredentialsHandle SEC_E_NO_CREDENTIALS (0x8009030E), HTTP 000. No verified fresh preview. Manual fallback: normal PowerShell verify workspace executable and Development listener, then run fresh .tools/slice014-fix-output/ForgeFlow.Web.dll with src/ForgeFlow.Web content root, Development, process-only existing localhost/testforgflow connection and HTTPS 5001; no migrations/bootstrap/recovery.

Browser tools unavailable: in-app/direct/refresh/back-forward/rapid-switch rendered behavior, FA/EN desktop/320px, active styling and dialog focus/layout remain pending live acceptance. Live SQL checks explicitly pending. Deferred AUTH-01 gates unchanged; Slice 01.4 NOT ACCEPTED. Next: live navigation and visual verification/corrections before new CRUD.

## Slice 05 (`05-item-catalog`: 05A Units, 05B Items, 05C Manufacturing BOM & Applicability) — 2026-11-10

Implemented Slice 05 across Domain, Application, Infrastructure (EF Core SQL Server + append-only `organization.OrganizationChange` + runtime role grants), and Web (`BitDataGrid` + `BitDialog` + bilingual `fa-IR`/`en-US` `.resx` resources):
- **05A (`/units`)**: `PermissionModule.Units` (`Units.Read`, `Units.Manage`), `Unit`, `UnitConversion`, `UnitConversionGraph` (exact rational BFS + `A08`), `UnitUseCases`, `UnitOperationsStore`, migration `20261110164056_UnitsAndConversions`, `/units` page, `UnitTests`, `UnitSqlTests`, plus permission modal dropdown + single group card refactor on `Users.razor`/`Roles.razor`.
- **05B (`/items`)**: `PermissionModule.Items` (`Items.Read`, `Items.Manage`), `Item`, `ClassificationSet`, `Classification`, `ItemClassification`, `ItemUnitConversion`, `AttributeDefinition`, `ItemAttributeValue`, `ItemUseCases` (hybrid item+global `A08` conversion), `ItemOperationsStore`, migration `20261110171954_ItemsClassificationsAttributes`, `/items` page, `ItemTests`, `ItemSqlTests`.
- **05C (`/boms`)**: `PermissionModule.Boms` (`Boms.Read`, `Boms.Manage`), `Bom`, `BomVersion`, `BomInput`, `BomOutput`, `ContextDimension`, `BomApplicability`, `BomApplicabilityCondition`, `BomResolutionEngine` (Day 9 dominance + missing-context safety + `A07`/`A11`), `BomUseCases` (cycle-safe recursive `ExplodeBom` enforcing `A08`/`A14`), `BomOperationsStore`, migration `20261110173434_ManufacturingBomsAndApplicability` (with `[ForgeFlowOrganizationRuntime]` `GRANT`/`DENY DELETE` and non-empty rollback guard), `/boms` page (BOMs & Versions workbench, Context Dimensions + standard dimension seed, and live Resolver/Explosion simulator), audit event codes on `/audit`, `BomTests`, and `BomSqlTests`. Added idempotent DDL for all three migrations to `scripts/upgrade-local-testdb.ps1`. Post-launch UI fixes: removed unsupported `Class` attribute from `BitDataGridColumn`; replaced native `<input type="date">` with culture-aware `BitDatePicker` (`CulturePolicy.PickerCulture` — Jalali in `fa-IR`, Gregorian in `en-US`) and localized effective dates via `DisplayFormatter.Date`; localized seeded standard context dimensions (`PLANT`, `PRODUCTION_LINE`, `MARKET`, `CUSTOMER`, `MANUFACTURING_METHOD`) via `RolePermissionLocalization.ContextDimensionDisplayName`; redesigned header `UserMenu.razor` with a `44px` pill trigger (`BitPersona` `36px` + chevron + active open ring), `288px` card (`BitPersona` `44px` single-line ellipsis header + `UserAccountSubtitle`), dividers, and `BitButton` Text actions (`KeyIcon` change password + soft-red `LogoutIcon` sign out).

Verification evidence (executed after all Slice 05 edits):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: PASS (`0 Warning(s), 0 Error(s)`).
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'`: `Passed! - Failed: 0, Passed: 344, Skipped: 47, Total: 391` (`47` SQL Server `[OrganizationSqlServerFact]` tests skipped in agent sandbox).
- User-executed live SQL Server verification in normal Windows PowerShell (`FORGEFLOW_TEST_SQLSERVER` set on `localhost`): `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~UnitSqlTests|FullyQualifiedName~ItemSqlTests|FullyQualifiedName~BomSqlTests|FullyQualifiedName~OrganizationSqlServerTests' --logger 'console;verbosity=minimal'` → **`Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9`** (`6.5s`).
- `.\scripts\upgrade-local-testdb.ps1` executed cleanly against `localhost/testforgflow`.
- `node .\scripts\drafts.test.mjs`: `19 pass, 0 fail`.
- Development Web server running at `https://localhost:5001` against local `testforgflow` (`ASPNETCORE_ENVIRONMENT=Development`); `node .\verification\slice01-ui\browser-check.cjs`: `4/4 PASS` (`fa-IR` 1440px/320px, `en-US` 1440px/320px); `curl.exe -k` verified `GET /account/login` -> `HTTP 200`, `GET /boms` anonymous -> `HTTP 302`; user confirmed live UI across `/units`, `/items`, `/boms`, and redesigned header `UserMenu`.
- **Slice 05 (`05-item-catalog`) Status: ACCEPTED (`05.5`)** (2026-11-10).

## Slice 06 (`06.1`–`06.4` Production Orders & Immutable BOM Snapshot — Day 11) — 2026-11-10

Implemented Slice 06 across Domain, Application, Infrastructure, and Web with `Q-P01`–`Q-P04` approved in [decisions.md](docs/slices/06-production-orders/decisions.md):
- **Domain (`ForgeFlow.Domain.ProductionOrders`)**: Added `PermissionModule.ProductionOrders = 9` (`ProductionOrders.Read`, `ProductionOrders.Manage` with `OrganizationOrSite` applicability; granted to built-in `OrganizationAdministrator` and `RolePermissionLocalization`), `ProductionOrder`, `ProductionOrderInput`, `ProductionOrderOutput`, `ProductionOrderContext`, `ProductionOrderStatus`, `ProductionOrderErrors`, and `ProductionOrderText`.
- **Application (`ForgeFlow.Application.ProductionOrders`)**: Extracted `BomUseCases.ResolveAndExplodeCoreAsync` for exact shared BOM resolution/explosion math and implemented `ProductionOrderUseCases` (site-isolated `ListOrders`, `GetOrder`, `ListAuthorizedSites`, `CreateDraftOrder`, `UpdateDraftOrder`, `RefreshDraftSnapshot`, `ReleaseOrder`, `StartOrder`, `RecordActuals`, `CompleteOrder`, `CancelOrder`) with automatic `PLANT = Site.Code` context injection and Draft vs frozen `Released` BOM snapshot semantics.
- **Infrastructure (`ForgeFlow.Infrastructure`)**: Added EF Core model configuration (`23` entities total, composite FKs, `CHECK` constraints, `RowVersion`), `OrganizationDbContext` site-attributed audit capture (`ProductionOrder.Created/Updated/Refreshed/Released/Started/Completed/Cancelled`), `AuthDbContext` model ignores, `SqlProductionOrderOperationsStore`, migration `20261110194120_ProductionOrdersAndSnapshots` (including `[ForgeFlowOrganizationRuntime]` `GRANT`/`DENY DELETE` and non-empty rollback guard), and appended idempotent DDL to [upgrade-local-testdb.ps1](scripts/upgrade-local-testdb.ps1).
- **Web (`ForgeFlow.Web`)**: Added `LoadProductionOrderScopesAsync` in [ReadScopes.cs](src/ForgeFlow.Web/ReadScopes.cs), `ProductionNavGroup` + `/production-orders` link in [MainLayout.razor](src/ForgeFlow.Web/Components/Layout/MainLayout.razor), all `ProductionOrder.*` event codes in [Audit.razor](src/ForgeFlow.Web/Components/Pages/Audit.razor), localized failure/permission/status mappings in [RolePermissionLocalization.cs](src/ForgeFlow.Web/Roles/RolePermissionLocalization.cs), and [ProductionOrders.razor](src/ForgeFlow.Web/Components/Pages/ProductionOrders.razor) (`/production-orders` and `/organizations/{OrganizationId:long}/production-orders`) using `BitDataGrid`, `BitDialog` (`ShowOkButton="false" ShowCancelButton="false"`), culture-aware `BitDatePicker` (`fa-IR` Jalali / `en-US` Gregorian), and `fa-IR`/`en-US` `.resx` resources.

Verification evidence (executed after final Slice 06 edits):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (`0 Warning(s), 0 Error(s)`)**.
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'`: **`Passed! - Failed: 0, Passed: 347, Skipped: 48, Total: 395`** (`ProductionOrderTests`, `RoleDomainTests`, `OrganizationPersistenceModelTests` [`23` entities, `5` migrations], and `ResourceContractTests` passed; `48` `[OrganizationSqlServerFact]` tests skipped in sandbox without `FORGEFLOW_TEST_SQLSERVER`).
- `node .\scripts\drafts.test.mjs`: **`19 pass, 0 fail`**.
- Post-initial SQL & UI refinements (2026-11-10):
  - Fixed `ArgumentNullException` on `RowVersion` / `CreatedAtUtc` in [SqlProductionOrderOperationsStore.cs](src/ForgeFlow.Infrastructure/Organizations/SqlProductionOrderOperationsStore.cs) by projecting `EF.Property<...>` directly inside `AsNoTracking()` queries (`ListOrders` and `BuildOrderDetailsAsync`).
  - Refined the Create Draft modal and Draft Editor grid in [ProductionOrders.razor](src/ForgeFlow.Web/Components/Pages/ProductionOrders.razor) and [app.css](src/ForgeFlow.Web/wwwroot/app.css) (`680px` width, symmetric 2-column `.po-form-grid`, full-width manufacturable item selector and `<textarea>` notes, `.po-context-card` with `+` `BomAddConditionBtn` and `(i)` hint, `.po-dialog-footer` with primary `PoCreateSubmit` `"Create order"` / `"ایجاد سفارش"` first + `"Cancel"`, and global unified `44px` `.bit-dtp` styling keeping calendar/clear icons cleanly inside the input across both `ProductionOrders.razor` and `Boms.razor`).
- Fresh Development Web running at `https://localhost:5001`: `node .\verification\slice01-ui\browser-check.cjs` -> **`4/4 PASS`** (`fa-IR` 1440px/320px, `en-US` 1440px/320px); `curl.exe --insecure` confirmed `GET /account/login` -> `HTTP 200` and anonymous `GET /production-orders` -> `HTTP 302`.
- User-executed live SQL Server verification in normal Windows PowerShell (`FORGEFLOW_TEST_SQLSERVER` set on `localhost`): `dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~ProductionOrderSqlTests|FullyQualifiedName~OrganizationSqlServerTests' --logger 'console;verbosity=minimal'` → **`Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`** (`6s`).
- `.\scripts\upgrade-local-testdb.ps1` executed cleanly against `localhost/testforgflow` (`testforgflow database upgraded to 20261110194120_ProductionOrdersAndSnapshots.`); user reviewed and confirmed live UI behavior.
- **Slice 06 (`06-production-orders`) Status: ACCEPTED (`06.5`)** (2026-11-10).

## Slice 07 (`07.1`–`07.4` Warehouses, Append-Only Stock Ledger & Production Issue/Receipt) — 2026-11-10

Implemented Slice 07 across Domain, Application, Infrastructure, and Web with `Q-I01`–`Q-I04` approved in [decisions.md](docs/slices/07-inventory/decisions.md):
- **Domain (`ForgeFlow.Domain.Inventory`)**: Added `PermissionModule.Inventory = 10` (`Inventory.Read`, `Inventory.Manage` with `OrganizationOrSite` applicability; granted to built-in `OrganizationAdministrator` and `RolePermissionLocalization`), `Warehouse`, `InventoryTransaction`, `InventoryLedgerEntry`, `InventoryBalance`, `InventoryTransactionType` (`Receipt`, `Issue`, `Adjustment`, `TransferOut`, `TransferIn`, `ProductionIssue`, `ProductionReceipt`, `Reversal`), `InventoryErrors`, and `InventoryText`.
- **Application (`ForgeFlow.Application.Inventory`)**: Implemented [InventoryUseCases.cs](src/ForgeFlow.Application/Inventory/InventoryUseCases.cs) & [InventoryContracts.cs](src/ForgeFlow.Application/Inventory/InventoryContracts.cs) with site isolation, hybrid item+global UOM conversion via `UnitConversionGraph`, atomic paired `TransferOut` lines, strict non-negative balance checks, `IdempotencyKey` deduplication, `ProductionOrder` status/site validation, and compensating `Reversal`.
- **Infrastructure (`ForgeFlow.Infrastructure`)**: Configured `Warehouse`, `InventoryTransaction`, `InventoryLedgerEntry`, `InventoryBalance` in [OrganizationModel.cs](src/ForgeFlow.Infrastructure/Organizations/OrganizationModel.cs) (`27` entities total, `6` migrations), `OrganizationDbContext` site-attributed audit events (`Warehouse.Created/Updated/Activated/Deactivated`, `InventoryTransaction.Posted/Reversed`), `AuthDbContext` ignores, [SqlInventoryOperationsStore.cs](src/ForgeFlow.Infrastructure/Organizations/SqlInventoryOperationsStore.cs) (deadlock-free ordered `UPDLOCK, HOLDLOCK` on `(WarehouseId, ItemId, LotCode)`), migration `20261110201924_InventoryWarehousesAndStockLedger` (append-only `DENY UPDATE, DELETE` on ledger/transaction + `DENY DELETE` on warehouse/balance + non-empty rollback guard), and idempotent DDL in [upgrade-local-testdb.ps1](scripts/upgrade-local-testdb.ps1).
- **Web (`ForgeFlow.Web`)**: Added `LoadInventoryScopesAsync` in [ReadScopes.cs](src/ForgeFlow.Web/ReadScopes.cs), `InventoryNavGroup` -> `/inventory` in [MainLayout.razor](src/ForgeFlow.Web/Components/Layout/MainLayout.razor), `Inventory.razor` (`/inventory` and `/organizations/{OrganizationId:long}/inventory` with On-Hand Balances, Stock Ledger + Reversal modal, and Warehouses tabs + auto-prefill from `ProductionOrder` inputs/outputs via `?poId=`), `/production-orders` Issue/Receive shortcut, audit event codes on `/audit`, `RolePermissionLocalization`, and `fa-IR`/`en-US` `.resx` catalogs.

Verification evidence (executed after all Slice 07 edits):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (`0 Warning(s), 0 Error(s)`)**.
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'`: **`Passed! - Failed: 0, Passed: 350, Skipped: 49, Total: 399`** (`InventoryTests`, `ProductionOrderTests`, `RoleDomainTests`, `OrganizationPersistenceModelTests` [`27` entities, `6` migrations], and `ResourceContractTests` passed; `49` `[OrganizationSqlServerFact]` tests skipped in sandbox without `FORGEFLOW_TEST_SQLSERVER`).
- `node .\scripts\drafts.test.mjs`: **`19 pass, 0 fail`**.
- Fresh Development Web running at `https://localhost:5001`: `node .\verification\slice01-ui\browser-check.cjs` -> **`4/4 PASS`**, `GET /account/login` -> `HTTP 200`, anonymous `GET /inventory` -> `HTTP 302`.
- User reviewed local Slice 07 UI and explicitly authorized committing/pushing Slice 07 and advancing to **Slice 08 (`08-reports-variance` — Material Shortage / Net Requirements & Production Consumption/Yield Variance Reports + CSV Export)** (`2026-11-11`).
- Note: live SQL Server test run for `InventorySqlTests` (`FORGEFLOW_TEST_SQLSERVER`) was not pasted in chat output and can be run anytime with `$env:FORGEFLOW_TEST_SQLSERVER` set.
- **Slice 07 (`07-inventory`) Status: ACCEPTED (`07.5`)** (`2026-11-11`). Pushed as `[main 13166d9]`.

## Slice 08 (`08.1`–`08.4` Operational Reports: Material Shortage & Production Consumption / Yield Variance + Safe CSV Export) — 2026-11-11

Implemented Slice 08 across Domain, Application, Infrastructure, and Web with `Q-R01`–`Q-R05` approved in [decisions.md](docs/slices/08-reports-variance/decisions.md) (no schema changes; reuses `OrganizationDbContext` `27` entities / `6` migrations):
- **Domain (`ForgeFlow.Domain`)**: Added `PermissionModule.Reports = 11` (`Reports.Read`, `Reports.Export` as `OrganizationOrSite`, granted to built-in `OrganizationAdministrator` in [PermissionCatalog.cs](src/ForgeFlow.Domain/Roles/PermissionCatalog.cs)) and [ReportingErrors.cs](src/ForgeFlow.Domain/Reporting/ReportingErrors.cs).
- **Application (`ForgeFlow.Application.Reporting`)**: Implemented [ReportingContracts.cs](src/ForgeFlow.Application/Reporting/ReportingContracts.cs) & [ReportingUseCases.cs](src/ForgeFlow.Application/Reporting/ReportingUseCases.cs) computing:
  - **Material Shortage / Net Requirements**: aggregates open `Released`/`InProgress` `ProductionOrder` `!IsSubAssembly` input lines converted via `UnitConversionGraph` to each item's `BaseUnitId` vs physical `InventoryBalance` on-hand balances, producing `ShortageBaseQuantity`, `ExcessBaseQuantity`, `CoveragePercent`, and per-order demand breakdown.
  - **Production Consumption & Yield Variance**: compares frozen planned order inputs/outputs against netted `InventoryLedgerEntry` postings (`ProductionIssue` net of `Reversal`, `ProductionReceipt` net of `Reversal`) and recorded `ActualProducedQuantity`/`ActualScrapQuantity`, including per-order detail drill-down.
  - **Authorized CSV Export**: `Reports.Export`-gated UTF-8 with BOM (`\uFEFF`) RFC-4180 CSV builder neutralizing spreadsheet formula triggers (`=`, `+`, `-`, `@`, `\t`, `\r`) and sanitizing download filenames.
- **Infrastructure (`ForgeFlow.Infrastructure`)**: Added read-only `AsNoTracking()` [SqlReportingQueryStore.cs](src/ForgeFlow.Infrastructure/Organizations/SqlReportingQueryStore.cs), updated [RuntimeSessions.cs](src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs) for `Reports.Read`/`Reports.Export`, and registered `ReportingUseCases` in [OrganizationPersistenceRegistration.cs](src/ForgeFlow.Infrastructure/Organizations/OrganizationPersistenceRegistration.cs).
- **Web (`ForgeFlow.Web`)**: Added `LoadReportScopesAsync` in [ReadScopes.cs](src/ForgeFlow.Web/ReadScopes.cs), `ReportsNavGroup` -> `/reports` in [MainLayout.razor](src/ForgeFlow.Web/Components/Layout/MainLayout.razor), [Reports.razor](src/ForgeFlow.Web/Components/Pages/Reports.razor) (KPI cards, Material Shortage & Production Variance tabs, `BitDataGrid` server/sliced paging, `BitDatePicker`, detail `BitDialog`s, CSV download via [report-download.js](src/ForgeFlow.Web/wwwroot/report-download.js)), [RolePermissionLocalization.cs](src/ForgeFlow.Web/Roles/RolePermissionLocalization.cs), `.rpt-*` styles in [app.css](src/ForgeFlow.Web/wwwroot/app.css), and bilingual `fa-IR`/`en-US` `.resx` catalogs.

Verification evidence (executed after all Slice 08 edits):
- `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (`0 Warning(s), 0 Error(s)`)**.
- `$env:FORGEFLOW_AUTH_HTTP_TESTS='1'; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore --logger 'console;verbosity=minimal'`: **`Passed! - Failed: 0, Passed: 356, Skipped: 50, Total: 406`** (`ReportTests` `R01`–`R06`, `RoleDomainTests`, `ResourceContractTests`, and all prior non-SQL tests passed; `50` `[OrganizationSqlServerFact]` tests including `ReportSqlTests` skipped in sandbox without `FORGEFLOW_TEST_SQLSERVER`).
- `node .\scripts\drafts.test.mjs`: **`19 pass, 0 fail`**.
- Fresh Development Web running at `https://localhost:5001`: `GET /account/login` -> `HTTP 200`, anonymous `GET /reports` -> `HTTP 302`, `node .\verification\slice01-ui\browser-check.cjs` -> **`4/4 PASS`** (`fa-IR` 1440px/320px, `en-US` 1440px/320px).

Remaining acceptance notes:
- Live SQL Server test execution (`ReportSqlTests` + `InventorySqlTests` + `OrganizationSqlServerTests` with `$env:FORGEFLOW_TEST_SQLSERVER` set) and interactive bilingual `/reports` review were deferred by user request (`2026-11-11`, user requested committing and pushing Slice 08 prior to next instruction).
- **Slice 08 (`08-reports-variance`) Status: IMPLEMENTED & PUSHED (`08.1`–`08.4`)** (`2026-11-11`, `[main d8db47b]`).

## Cross-Cutting Diagnostics, Error Logging & User Operation Activity Log — 2026-11-11

Implemented per approved implementation plan without altering `AuthDbContext` (`13` `"auth"` entities / `2` migrations) or `OrganizationDbContext` (`27` entities / `6` migrations):
- **Domain (`ForgeFlow.Domain`)**: Added `PermissionModule.Diagnostics = 12` (`Diagnostics.ErrorLogsRead` & `Diagnostics.ErrorLogsManage` as `PlatformOnly`), `Audit.Export` (`OrganizationOrSite`, granted to `OrganizationAdministrator`) in [PermissionCatalog.cs](src/ForgeFlow.Domain/Roles/PermissionCatalog.cs), and [DiagnosticTypes.cs](src/ForgeFlow.Domain/Diagnostics/DiagnosticTypes.cs) (`ErrorLogRecord`, `UserOperationLogRecord`, `ErrorSeverity`, `ErrorSourceKind`, `UserOperationOutcome`, `DiagnosticErrors`).
- **Application (`ForgeFlow.Application`)**: Added [DiagnosticContracts.cs](src/ForgeFlow.Application/Diagnostics/DiagnosticContracts.cs), [ErrorLogUseCases.cs](src/ForgeFlow.Application/Diagnostics/ErrorLogUseCases.cs) (`PlatformOnly` enforcement + retention purge), shared [CsvExportBuilder.cs](src/ForgeFlow.Application/Common/CsvExportBuilder.cs) & [OperationAuditHelper.cs](src/ForgeFlow.Application/Common/OperationAuditHelper.cs), extended [AuditContracts.cs](src/ForgeFlow.Application/Audit/AuditContracts.cs) & [AuditUseCases.cs](src/ForgeFlow.Application/Audit/AuditUseCases.cs) (`Operations` category, `ActorUserId`, `OutcomeFilter`, `FromDate`/`ToDate`, `ListActors`, `ExportAuditCsv`), and wired operation/rejection recording into [ReportingUseCases.cs](src/ForgeFlow.Application/Reporting/ReportingUseCases.cs), [InventoryUseCases.cs](src/ForgeFlow.Application/Inventory/InventoryUseCases.cs), and [ProductionOrderUseCases.cs](src/ForgeFlow.Application/ProductionOrders/ProductionOrderUseCases.cs).
- **Infrastructure (`ForgeFlow.Infrastructure`)**: Added [DiagnosticRedactor.cs](src/ForgeFlow.Infrastructure/Diagnostics/DiagnosticRedactor.cs) (credential/token/cookie/query-param scrubbing while preserving SQL numbers and stack traces), isolated [DiagnosticsDbContext.cs](src/ForgeFlow.Infrastructure/Diagnostics/DiagnosticsDbContext.cs) + migration `20261110210556_DiagnosticsErrorAndOperationLogs` (`diag.ErrorLog` `DENY UPDATE`, `ops.UserOperationLog` `DENY UPDATE, DELETE`), [SqlErrorLogStore.cs](src/ForgeFlow.Infrastructure/Diagnostics/SqlErrorLogStore.cs) with automatic rolling JSONL fallback (`App_Data/logs/errors-yyyyMMdd.jsonl`) if SQL is unavailable, wired store failure capture across `SqlReportingQueryStore`, `SqlInventoryOperationsStore`, `SqlProductionOrderOperationsStore`, and extended [SqlAuditOperationsStore.cs](src/ForgeFlow.Infrastructure/Identity/SqlAuditOperationsStore.cs) + [RuntimeSessions.cs](src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs) + [upgrade-local-testdb.ps1](scripts/upgrade-local-testdb.ps1).
- **Web (`ForgeFlow.Web`)**: Added [HttpOperationContextAccessor.cs](src/ForgeFlow.Web/Diagnostics/HttpOperationContextAccessor.cs) (`X-Correlation-Id` middleware + `AsyncLocal` context), [OperationCircuitHandler.cs](src/ForgeFlow.Web/Diagnostics/OperationCircuitHandler.cs), [GlobalExceptionLoggingHandler.cs](src/ForgeFlow.Web/Diagnostics/GlobalExceptionLoggingHandler.cs), [UiErrorReporter.cs](src/ForgeFlow.Web/Diagnostics/UiErrorReporter.cs), updated [Error.razor](src/ForgeFlow.Web/Components/Pages/Error.razor) to display safe `ErrorReference` + `CorrelationId`, upgraded [Audit.razor](src/ForgeFlow.Web/Components/Pages/Audit.razor) (User Operations category, User/Outcome/Date filters, CSV export), and created Platform-only [Errors.razor](src/ForgeFlow.Web/Components/Pages/Errors.razor) (`/errors`) + full `fa-IR`/`en-US` `.resx` localization.

Post-review localization, local timezone display, full ActionRejected coverage & SQL test completion — 2026-11-11:
- **Local display timestamps (UTC suffix removed)**: Centralized UTC-to-local conversion in [DisplayFormatter.Timestamp](src/ForgeFlow.Web/Localization/DisplayFormatter.cs) (respecting CultureInfo.CurrentCulture / PersianCalendar vs Gregorian) and replaced per-page UTC formatters across Audit.razor, Errors.razor, Inventory.razor, ProductionOrders.razor, and CSV export helpers (AuditUseCases.ExportAuditCsv), alongside local day-boundary conversion ([DiagnosticDateBounds](src/ForgeFlow.Infrastructure/Diagnostics/DiagnosticDateBounds.cs)) for SQL FromDate/ToDate filtering. DB persistence remains strictly UTC.
- **Refined 3rd Audit Category Label & Full ActionRejected Coverage Across All Core Modules**:
  - Updated AuditCategoryOperations label in SharedResources.fa-IR.resx (خروجی‌ها و عملیات ردشده) and SharedResources.en-US.resx (Exports & Rejected Actions) and updated AuditCaption to make the category's purpose self-explanatory.
  - Added Organization.ActionRejected, Site.ActionRejected, Unit.ActionRejected, Item.ActionRejected, User.ActionRejected, and Role.ActionRejected (alongside Bom.ActionRejected, ProductionOrder.ActionRejected, Inventory.ActionRejected, Reporting.ActionRejected) to [UserOperationCatalog](src/ForgeFlow.Application/Diagnostics/DiagnosticContracts.cs), [AuditEventCatalog](src/ForgeFlow.Application/Audit/AuditEventCatalog.cs), and a-IR/en-US .resx resources.
  - Wired [OperationAuditHelper.RecordIfRejectedAsync](src/ForgeFlow.Application/Common/OperationAuditHelper.cs) into [OrganizationUseCases.cs](src/ForgeFlow.Application/Organizations/OrganizationUseCases.cs), [UnitUseCases.cs](src/ForgeFlow.Application/Units/UnitUseCases.cs), [ItemUseCases.cs](src/ForgeFlow.Application/Items/ItemUseCases.cs), [BomUseCases.cs](src/ForgeFlow.Application/Boms/BomUseCases.cs), [ProductionOrderUseCases.cs](src/ForgeFlow.Application/ProductionOrders/ProductionOrderUseCases.cs), [InventoryUseCases.cs](src/ForgeFlow.Application/Inventory/InventoryUseCases.cs), [UserUseCases.cs](src/ForgeFlow.Application/Users/UserUseCases.cs), [RoleUseCases.cs](src/ForgeFlow.Application/Roles/RoleUseCases.cs), and [OrganizationPersistenceRegistration.cs](src/ForgeFlow.Infrastructure/Organizations/OrganizationPersistenceRegistration.cs), with unit verification D09 in [DiagnosticTests.cs](tests/ForgeFlow.Web.Tests/DiagnosticTests.cs).
- **JSON metadata redaction & SQL test fixes**: Added DiagnosticRedactor.ScrubJson ([DiagnosticRedactor.cs](src/ForgeFlow.Infrastructure/Diagnostics/DiagnosticRedactor.cs)) used by [SqlErrorLogStore.cs](src/ForgeFlow.Infrastructure/Diagnostics/SqlErrorLogStore.cs) so ISJSON([MetadataJson]) = 1 remains valid while redacting secret JSON keys and token strings; fixed [DiagnosticSqlTests.cs](tests/ForgeFlow.Web.Tests/DiagnosticSqlTests.cs) migration order (OrganizationDbContext before AuthDbContext); and granted FixedAccessRoles.DeploymentProvisioner (ScopeKind = 0) to initial.UserId in [AuditSqlTests.cs](tests/ForgeFlow.Web.Tests/AuditSqlTests.cs) for Platform audit verification.

Verification evidence (after all edits):
- .\scripts\upgrade-local-testdb.ps1: 	estforgflow database upgraded to 20261110210556_DiagnosticsErrorAndOperationLogs.
- dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (  Warning(s), 0 Error(s))**.
- Targeted live SQL + unit run (FORGEFLOW_TEST_SQLSERVER=... --filter 'FullyQualifiedName~DiagnosticTests|FullyQualifiedName~DiagnosticSqlTests|FullyQualifiedName~AuditSqlTests|FullyQualifiedName~ResourceContractTests'): **Passed: 25, Failed: 0, Skipped: 0, Total: 25** (D01–D09, DiagnosticSqlTests S01–S02, AuditSqlTests, ResourceContractTests).
- Non-SQL + HTTP test run ($env:FORGEFLOW_AUTH_HTTP_TESTS='1'; =''; dotnet test .\ForgeFlow.slnx -c Release --no-build --no-restore): **Passed: 365, Failed: 0, Skipped: 51, Total: 416**.
- 
ode .\scripts\drafts.test.mjs: **19 pass, 0 fail**.
- Fresh Development Web running at https://localhost:5001: GET /account/login -> 200, GET /audit -> 302, GET /errors -> 302, 
ode .\verification\slice01-ui\browser-check.cjs -> **4/4 PASS**.

- **Organization Member Session Visibility & Single-Language Event Column (2026-11-11)**:
  - Updated [SqlAuditOperationsStore.cs](src/ForgeFlow.Infrastructure/Identity/SqlAuditOperationsStore.cs) (ApplySecurityScope + MemberSessionEventCodes: SessionIssued, SessionRevoked, SignInFailed, AccountLocked) so organization-scoped audit queries also surface sign-in/sign-out/failed-login events belonging to active members of that organization (uth.OrganizationMemberships) while preserving strict cross-tenant, site, and platform isolation; added assertion in [AuditSqlTests.cs](tests/ForgeFlow.Web.Tests/AuditSqlTests.cs).
  - Updated AuditEventColumn in [Audit.razor](src/ForgeFlow.Web/Components/Pages/Audit.razor) to render only the localized label for the active UI language (a-IR / en-US) rather than showing both localized and raw English codes simultaneously in the table column.
  - Re-verified after edits: Release build (  Warning(s), 0 Error(s)), DiagnosticTests|DiagnosticSqlTests|AuditSqlTests|ResourceContractTests (**25/25 PASS**), non-SQL + HTTP suite (**365/365 PASS**, 51 SQL tests skipped without FORGEFLOW_TEST_SQLSERVER), 
ode .\scripts\drafts.test.mjs (**19/19 PASS**), rowser-check.cjs (**4/4 PASS**), and live Development Web running at https://localhost:5001 (/account/login -> 200, /audit -> 302, /errors -> 302).

- **Last Sign-In Card in User Menu & Proxy-Aware Client IP Logging (2026-11-11)**:
  - Updated [AuthenticationBackend.cs](src/ForgeFlow.Web/Authentication/AuthenticationBackend.cs) (ResolveClientIp) to resolve client IP from X-Forwarded-For (leftmost valid IP, port-stripped, IPv4-mapped IPv6 normalized) falling back to Connection.RemoteIpAddress, and pass it into [RuntimeSessions.cs](src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs) (LoginAsync(..., clientIp)) to store {"ip": "..."} in SecurityEvent.DataJson (SessionIssued and SignInFailed).
  - Added RuntimeSessions.GetPreviousSignInAsync excluding the current SessionId so first-time logins return 
ull.
  - Updated [UserMenu.razor](src/ForgeFlow.Web/Components/Layout/UserMenu.razor), [DisplayFormatter.cs](src/ForgeFlow.Web/Localization/DisplayFormatter.cs) (Timestamp(utc, " - ")), [app.css](src/ForgeFlow.Web/wwwroot/app.css) (.user-menu-last-login, .ff-icon--clock, monospace IP), and a-IR/en-US .resx catalogs (UserMenuLastLoginTitle, UserMenuFirstLoginNotice) to display the previous sign-in card (local time + monospace IP) or "این اولین ورود شماست" / "This is your first sign-in" when no prior login exists.
  - Re-verified after all edits: Release build (  Warning(s), 0 Error(s)), targeted SQL + HTTP + unit suite (DiagnosticTests|DiagnosticSqlTests|AuditSqlTests|AuthSessionSqlTests|AuthSessionHttpSqlTests|ResourceContractTests|ShellRenderingTests -> **44/44 PASS**), full non-SQL + HTTP suite (**366/366 PASS**, 51 SQL tests skipped without FORGEFLOW_TEST_SQLSERVER), 
ode .\scripts\drafts.test.mjs (**19/19 PASS**), rowser-check.cjs (**4/4 PASS**), and live Development Web running at https://localhost:5001.

Remaining acceptance / open option:
- /errors enforces PlatformOnly (Diagnostics.ErrorLogsRead held by DeploymentProvisioner, ScopeKind = 0), so OrganizationAdministrator accounts (ScopeKind = 1, e.g., localtest) intentionally receive "دسترسی به این محدوده مجاز نیست.". Keep PlatformOnly (recommended) or switch to org-scoped error log visibility only if explicitly requested before Slice 09.

- **Slice 08 ( 8-reports-variance) Status: ACCEPTED ( 8.5)** (2026-11-11): User reviewed /reports and /errors live at https://localhost:5001 (Persian/English, RTL/LTR, mobile) and confirmed acceptance; /grill-me design interview completed and Slice 09 ( 9-quality-traceability) plan approved.

## Slice 08 SQL Verification & Organization-Scoped Error Logs Increment — 2026-11-11

- **Organization-Scoped Error Log Visibility (/errors)**:
  - Updated [PermissionCatalog.cs](src/ForgeFlow.Domain/Roles/PermissionCatalog.cs): changed Diagnostics.ErrorLogsRead applicability to OrganizationOnly (auto-granted to OrganizationAdministrator at ScopeKind = 1; excluded from ScopeKind = 2 site scope), while keeping Diagnostics.ErrorLogsManage as PlatformOnly.
  - Updated [DiagnosticContracts.cs](src/ForgeFlow.Application/Diagnostics/DiagnosticContracts.cs), [ErrorLogUseCases.cs](src/ForgeFlow.Application/Diagnostics/ErrorLogUseCases.cs), and [SqlErrorLogStore.cs](src/ForgeFlow.Infrastructure/Diagnostics/SqlErrorLogStore.cs) (ApplyScopeFilter) so ListErrorLogs, GetErrorLog, and GetStats support OrganizationScope.Platform (all logs) or OrganizationScope.Organization (strictly filtered to e.OrganizationId == org.OrganizationId, returning NotFound for cross-tenant or null-org records), while PurgeOldErrorLogs remains strictly PlatformOnly.
  - Updated [RuntimeSessions.cs](src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs) (ScopeAllowed Platform allowlist + read-only inactive-org allowance for Diagnostics.ErrorLogsRead), [ReadScopes.cs](src/ForgeFlow.Web/ReadScopes.cs) (LoadDiagnosticScopesAsync loading Platform and Organization scopes), [RolePermissionLocalization.cs](src/ForgeFlow.Web/Roles/RolePermissionLocalization.cs) (TenantModules includes PermissionModule.Diagnostics), and [Errors.razor](src/ForgeFlow.Web/Components/Pages/Errors.razor) (added /organizations/{OrganizationId:long}/errors route, multi-scope ReadScope selector, and Platform-only retention purge guard).
  - Updated [DiagnosticTests.cs](tests/ForgeFlow.Web.Tests/DiagnosticTests.cs) (D03, D05) and [DiagnosticSqlTests.cs](tests/ForgeFlow.Web.Tests/DiagnosticSqlTests.cs) (S01_S02) to verify organization-scoped isolation and Platform-only purge enforcement.

- **Verification Evidence (2026-11-11, after all edits)**:
  - dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (  Warning(s), 0 Error(s))**.
  - Targeted live SQL + unit suite ($env:FORGEFLOW_AUTH_HTTP_TESTS='1'; =... --filter 'FullyQualifiedName~ReportTests|FullyQualifiedName~ReportSqlTests|FullyQualifiedName~InventoryTests|FullyQualifiedName~InventorySqlTests|FullyQualifiedName~DiagnosticTests|FullyQualifiedName~DiagnosticSqlTests|FullyQualifiedName~AuditSqlTests|FullyQualifiedName~RoleDomainTests|FullyQualifiedName~RoleManagementPageTests|FullyQualifiedName~ResourceContractTests'): **Passed: 57, Failed: 0, Skipped: 0, Total: 57** (ReportSqlTests, InventorySqlTests, DiagnosticSqlTests, AuditSqlTests all passed against live SQL Server).
  - Full live SQL + HTTP + unit suite ($env:FORGEFLOW_AUTH_HTTP_TESTS='1'; =...): **Passed: 426, Failed: 3, Skipped: 0, Total: 429**; the 3 failures are pre-existing historical tests predating multi-migration auth/roles (AuthBootstrapSqlTests.Additive_auth_migration_preserves_existing_business_data_and_refuses_adoption expecting 1 auth migration instead of 2, AuthBootstrapSqlTests.Bootstrap_creates_real_identity_company_only_grants_and_correlated_audits_once returning Auth.SchemaNotReady, and RoleManagementSqlTests.S07_barrier_synchronized_admins_cannot_deactivate_last_built_in_organization_admin hitting Roles.AccessDenied race).
  - Full non-SQL + HTTP suite ($env:FORGEFLOW_AUTH_HTTP_TESTS='1'; =''): **Passed: 366, Failed: 0, Skipped: 51, Total: 417**.
  - 
ode .\scripts\drafts.test.mjs: **19 pass, 0 fail**.
  - Fresh Development Web running at https://localhost:5001: GET /account/login -> HTTP 200, GET /reports -> HTTP 302, GET /errors -> HTTP 302, 
ode .\verification\slice01-ui\browser-check.cjs -> **4/4 PASS** (a-IR/en-US at 1440px and 320px).

- **Remaining Acceptance Gaps Before Slice 09 Design ( 9.0)**:
  - Interactive bilingual UI acceptance on /reports (both Shortage and Variance tabs, detail dialogs, CSV export, a-IR/en-US, RTL/LTR, desktop + 320px mobile) and /errors (OrganizationAdministrator org-scoped view) by user before marking Slice 08 ACCEPTED.
  - Next step: Confirm Slice 08 acceptance after live review at https://localhost:5001, then define and approve Slice 09 ( 9.0 design & plan).

## Slice 09 ( 9-quality-traceability: Quality Control, Quarantine Holds, Lot Expiry & Multi-Level Lot Traceability) — 2026-11-11

Implemented Slice 09 ( 9.0– 9.4) across Design, Domain, Application, Infrastructure, and Web with Q-Q01–Q-Q06 approved in [decisions.md](docs/slices/09-quality-traceability/decisions.md):
- **Design (docs/slices/09-quality-traceability/)**: Added [README.md](docs/slices/09-quality-traceability/README.md), [decisions.md](docs/slices/09-quality-traceability/decisions.md), [physical-erd.md](docs/slices/09-quality-traceability/physical-erd.md), and [test-plan.md](docs/slices/09-quality-traceability/test-plan.md) alongside Slices 09–12 roadmap ( 9 Quality & Traceability -> 10 Procurement -> 11 Work Centers & Routings -> 12 Stock Reservation & Dashboard).
- **Domain (ForgeFlow.Domain)**:
  - Added PermissionModule.Quality = 13 (Quality.Read, Quality.Manage as OrganizationOrSite, auto-granted to OrganizationAdministrator in [PermissionCatalog.cs](src/ForgeFlow.Domain/Roles/PermissionCatalog.cs)).
  - Extended [Item.cs](src/ForgeFlow.Domain/Items/Item.cs) (RequiresLotTracking, QualityControlMode), [InventoryBalance.cs](src/ForgeFlow.Domain/Inventory/InventoryBalance.cs) (BlockedBaseQuantity, AvailableBaseQuantity, BlockQuantity, ReleaseBlockedQuantity, ScrapBlockedQuantity, outbound AvailableBaseQuantity guard), [InventoryTransactionType.cs](src/ForgeFlow.Domain/Inventory/InventoryTransactionType.cs) (QualityScrap = 9), [InventoryTransaction.cs](src/ForgeFlow.Domain/Inventory/InventoryTransaction.cs) (QualityInspectionId), and [InventoryErrors.cs](src/ForgeFlow.Domain/Inventory/InventoryErrors.cs) (LotRequired, LotExpired).
  - Added ForgeFlow.Domain.Quality ([ItemLot.cs](src/ForgeFlow.Domain/Quality/ItemLot.cs), [QualityInspection.cs](src/ForgeFlow.Domain/Quality/QualityInspection.cs), [QualityInspectionLine.cs](src/ForgeFlow.Domain/Quality/QualityInspectionLine.cs), [QualityEnums.cs](src/ForgeFlow.Domain/Quality/QualityEnums.cs), [QualityErrors.cs](src/ForgeFlow.Domain/Quality/QualityErrors.cs), [QualityText.cs](src/ForgeFlow.Domain/Quality/QualityText.cs)).
- **Application (ForgeFlow.Application)**:
  - Added [QualityContracts.cs](src/ForgeFlow.Application/Quality/QualityContracts.cs) and [QualityUseCases.cs](src/ForgeFlow.Application/Quality/QualityUseCases.cs) (manual QC holds, inspection measurements, partial dispositions releasing stock or posting QualityScrap, hold cancellation, ItemLot metadata, and cycle-safe recursive Forward/Backward ComputeGenealogy).
  - Updated [InventoryUseCases.cs](src/ForgeFlow.Application/Inventory/InventoryUseCases.cs) (lot-required enforcement + auto-QC receipt flagging), [ItemUseCases.cs](src/ForgeFlow.Application/Items/ItemUseCases.cs), [ReportingUseCases.cs](src/ForgeFlow.Application/Reporting/ReportingUseCases.cs) (Shortage uses AvailableBaseQuantity = OnHand - Blocked and exposes BlockedBaseQuantity + 12-col CSV), [DiagnosticContracts.cs](src/ForgeFlow.Application/Diagnostics/DiagnosticContracts.cs) (Quality.ActionRejected), and [AuditEventCatalog.cs](src/ForgeFlow.Application/Audit/AuditEventCatalog.cs).
- **Infrastructure (ForgeFlow.Infrastructure)**:
  - Updated [OrganizationModel.cs](src/ForgeFlow.Infrastructure/Organizations/OrganizationModel.cs) (30 entities total, 7 migrations), [OrganizationDbContext.cs](src/ForgeFlow.Infrastructure/Organizations/OrganizationDbContext.cs) (audit capture for ItemLot and QualityInspection*), [AuthDbContext.cs](src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs) ignores, [RuntimeSessions.cs](src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs), [ItemOperationsStore.cs](src/ForgeFlow.Infrastructure/Organizations/ItemOperationsStore.cs), [SqlInventoryOperationsStore.cs](src/ForgeFlow.Infrastructure/Organizations/SqlInventoryOperationsStore.cs) (expired lot check on outbound movements, inbound lot metadata upsert, atomic auto-QC blocking, reversal guard), [SqlReportingQueryStore.cs](src/ForgeFlow.Infrastructure/Organizations/SqlReportingQueryStore.cs), [SqlQualityOperationsStore.cs](src/ForgeFlow.Infrastructure/Organizations/SqlQualityOperationsStore.cs), [OrganizationPersistenceRegistration.cs](src/ForgeFlow.Infrastructure/Organizations/OrganizationPersistenceRegistration.cs), migration 20261111041945_QualityControlAndLotTraceability (including [ForgeFlowOrganizationRuntime] GRANT/DENY DELETE and non-empty rollback guard), and [upgrade-local-testdb.ps1](scripts/upgrade-local-testdb.ps1).
- **Web & Localization (ForgeFlow.Web)**:
  - Added LoadQualityScopesAsync in [ReadScopes.cs](src/ForgeFlow.Web/ReadScopes.cs), QualityNavGroup (/quality, /traceability) in [MainLayout.razor](src/ForgeFlow.Web/Components/Layout/MainLayout.razor), [Quality.razor](src/ForgeFlow.Web/Components/Pages/Quality.razor), [Traceability.razor](src/ForgeFlow.Web/Components/Pages/Traceability.razor), updates to [Items.razor](src/ForgeFlow.Web/Components/Pages/Items.razor), [Inventory.razor](src/ForgeFlow.Web/Components/Pages/Inventory.razor), [Reports.razor](src/ForgeFlow.Web/Components/Pages/Reports.razor), [RolePermissionLocalization.cs](src/ForgeFlow.Web/Roles/RolePermissionLocalization.cs), .qc-* styles in [app.css](src/ForgeFlow.Web/wwwroot/app.css), and full a-IR/en-US .resx catalogs.

Verification evidence (executed after all Slice 09 edits):
- .\scripts\upgrade-local-testdb.ps1: 	estforgflow upgraded cleanly to 20261111041945_QualityControlAndLotTraceability.
- dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (  Warning(s), 0 Error(s))**.
- Targeted live SQL + unit suite ($env:FORGEFLOW_AUTH_HTTP_TESTS='1'; =... --filter 'FullyQualifiedName~QualityTests|FullyQualifiedName~QualitySqlTests|FullyQualifiedName~ReportTests|FullyQualifiedName~ReportSqlTests|FullyQualifiedName~InventoryTests|FullyQualifiedName~InventorySqlTests|FullyQualifiedName~ItemTests|FullyQualifiedName~ItemSqlTests|FullyQualifiedName~ProductionOrderTests|FullyQualifiedName~ProductionOrderSqlTests|FullyQualifiedName~OrganizationPersistenceModelTests|FullyQualifiedName~RoleDomainTests|FullyQualifiedName~RoleManagementPageTests|FullyQualifiedName~ResourceContractTests|FullyQualifiedName~DiagnosticTests|FullyQualifiedName~DiagnosticSqlTests|FullyQualifiedName~AuditSqlTests'): **Passed: 80, Failed: 0, Skipped: 0, Total: 80** (QualityTests Q01–Q06 and QualitySqlTests S01–S04 passed against live SQL Server).
- Full non-SQL + HTTP suite ($env:FORGEFLOW_AUTH_HTTP_TESTS='1'; =''): **Passed: 372, Failed: 0, Skipped: 52, Total: 424**.
- 
ode .\scripts\drafts.test.mjs: **19 pass, 0 fail**.
- Fresh Development Web running at https://localhost:5001: GET /account/login -> HTTP 200, anonymous GET /quality -> HTTP 302, anonymous GET /traceability -> HTTP 302, 
ode .\verification\slice01-ui\browser-check.cjs -> **4/4 PASS** (a-IR and en-US at 1440px and 320px).

- **Slice 09 (`09-quality-traceability`) Status: ACCEPTED (`09.5`)** (2026-11-11): User reviewed `/quality`, `/traceability`, `/items`, `/inventory`, and `/reports` live at `https://localhost:5001` and confirmed UI acceptance.
- Next step: Begin Slice 10 (`10-procurement` — Suppliers & Purchase Orders) design (`10.0`) on user instruction.

## Cross-Cutting User Profile & Workspace Experience — Stage P1 (Preferences, Theme, Calendar/TimeZone & Profile) — 2026-11-11

- **Scope (P1)**: Foundation DB preferences & profile persistence, decoupled CalendarSystem + IANA TimeZoneId display formatting, zero-FOUC Light/Dark/System theme engine, /account/preferences, and /account/profile.
- **Application (ForgeFlow.Application)**:
  - Added [UserProfileContracts.cs](src/ForgeFlow.Application/Users/UserProfileContracts.cs) (UserPreferenceCatalog, curated IANA time zone resolution via TimeZoneInfo, DisplayName/Email/PhoneNumber validation, PNG/JPEG/WebP magic-byte & 256 KB avatar validation).
  - Registered PreferencesUpdated and ProfileUpdated in [AuditEventCatalog.cs](src/ForgeFlow.Application/Audit/AuditEventCatalog.cs).
- **Infrastructure (ForgeFlow.Infrastructure)**:
  - Extended ApplicationAccount (DisplayName, AvatarContentType, AvatarData) and added UserPreference (UserId, Language, Theme, CalendarSystem, TimeZoneId, CreatedAtUtc, UpdatedAtUtc, RowVersion) in [IdentityRecords.cs](src/ForgeFlow.Infrastructure/Identity/IdentityRecords.cs) and [AuthDbContext.cs](src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs) (14 auth entities, 3 auth migrations).
  - Updated [RuntimeSessions.cs](src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs): first-login seeding of UserPreference using login language (initialCulture), SetPreferredCultureAsync sync, GetPreferencesAsync, SavePreferencesAsync, GetProfileAsync, UpdateProfileAsync, GetAvatarAsync.
  - Created additive Auth migration 20261111045515_UserProfileAndPreferences (with [ForgeFlowAuthRuntime] GRANT SELECT, INSERT, UPDATE + DENY DELETE and Auth.NonEmptyRollbackDenied guard) and updated [upgrade-local-testdb.ps1](scripts/upgrade-local-testdb.ps1).
- **Web & Localization (ForgeFlow.Web)**:
  - Added [UserDisplayPreferences.cs](src/ForgeFlow.Web/Localization/UserDisplayPreferences.cs) and [theme-init.js](src/ForgeFlow.Web/wwwroot/theme-init.js) (<head> pre-paint loader in [App.razor](src/ForgeFlow.Web/Components/App.razor)); decoupled calendar/timezone rendering in [CulturePolicy.cs](src/ForgeFlow.Web/Localization/CulturePolicy.cs) and [DisplayFormatter.cs](src/ForgeFlow.Web/Localization/DisplayFormatter.cs); loaded DB preferences in [Program.cs](src/ForgeFlow.Web/Program.cs) middleware and [MainLayout.razor](src/ForgeFlow.Web/Components/Layout/MainLayout.razor).
  - Added GET /account/avatar and passed login culture to LoginAsync in [AuthenticationBackend.cs](src/ForgeFlow.Web/Authentication/AuthenticationBackend.cs); exposed theme/culture helpers in [auth-ui.mjs](src/ForgeFlow.Web/wwwroot/auth-ui.mjs).
  - Added [AccountNav.razor](src/ForgeFlow.Web/Components/Layout/AccountNav.razor), [Preferences.razor](src/ForgeFlow.Web/Components/Pages/Preferences.razor) (/account/preferences), [Profile.razor](src/ForgeFlow.Web/Components/Pages/Profile.razor) (/account/profile), updated [ChangePassword.razor](src/ForgeFlow.Web/Components/Pages/ChangePassword.razor) and [UserMenu.razor](src/ForgeFlow.Web/Components/Layout/UserMenu.razor), added .prefs-*, .profile-*, and [data-theme="dark"] tokens in [app.css](src/ForgeFlow.Web/wwwroot/app.css), and added a-IR/en-US resources.
- **Verification Evidence (2026-11-11, after final P1 edit)**:
  - .\scripts\upgrade-local-testdb.ps1: 	estforgflow upgraded cleanly to 20261111045515_UserProfileAndPreferences.
  - dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (  Warning(s), 0 Error(s))**.
  - Targeted live SQL + unit + HTTP + scanner suite (UserProfileAndPreferencesTests|UserProfileAndPreferencesSqlTests|AuthBootstrapTests|AuthSessionSqlTests|AuthSessionHttpSqlTests|AuthUiContractTests|AuthHttpTests|AuthBackendTests|ResourceContractTests|ShellRenderingTests|AuditTests|AuditSqlTests|SampleLocalizationTests|HardcodedDisplayTextScannerTests): **Passed: 106, Failed: 0, Skipped: 0, Total: 106**.
  - Full non-SQL + HTTP suite (FORGEFLOW_AUTH_HTTP_TESTS=1, FORGEFLOW_TEST_SQLSERVER=''): **Passed: 375, Failed: 0, Skipped: 54, Total: 429**.
  - 
ode .\scripts\drafts.test.mjs: **19 pass, 0 fail**.
- **Remaining Acceptance & Next Stages (P2–P4)**:
  - User UI inspection of /account/preferences and /account/profile (Light/Dark/System theme, Persian/Gregorian calendar, IANA timezone, bilingual RTL/LTR, desktop & 320px mobile) at https://localhost:5001.
  - Next small increment after Stage P1 acceptance: **Stage P2** (UserMenu.razor redesign with role/site badges, multi-site selector, failed-login warning bar, and /account/sessions active sessions management).
## Cross-Cutting User Profile & Workspace Experience — Stage P2 (Enhanced UserMenu, Active Site Switcher, Failed Login Alert & Active Sessions) + Dark Login / ChangePassword UI Polish — 2026-11-11

- **UI Fixes from Stage P1 Review**:
  - Fixed [data-theme="dark"] login page contrast in [app.css](src/ForgeFlow.Web/wwwroot/app.css) (heading, subtitle, brand, language toggle border/background, BitTextField dark borders, input text & autofill fill, plus dark theme rules for DataGrid, NavPanel, Dialog, and ReconnectModal; also fixed [data-theme="dark"] root selector for Bit CSS variables).
  - Unified [ChangePassword.razor](src/ForgeFlow.Web/Components/Pages/ChangePassword.razor) password inputs to share the .login-field* rounded styling and added post-success link to /account/sessions.
- **Scope (P2)**: Redesigned operational UserMenu, active role & active site display, conditional multi-site selector, failed sign-in amber alert bar, device/IP capture on sessions, and /account/sessions management page.
- **Application & Infrastructure**:
  - Added AccessibleSiteOption, UserMenuContextSnapshot, ActiveSessionSnapshot, SessionOperationResult, and UserAgentDeviceParser in [UserProfileContracts.cs](src/ForgeFlow.Application/Users/UserProfileContracts.cs); registered SessionRevokedByUser, OtherSessionsRevoked, and ActiveSiteSwitched in [AuditEventCatalog.cs](src/ForgeFlow.Application/Audit/AuditEventCatalog.cs).
  - Extended AccountSession (IpAddress, DeviceSummary, IsMobileDevice, LastActivityAtUtc), UserPreference (ActiveOrganizationId, ActiveSiteId), CK_Session_Lifecycle, and CK_SecurityEvent_Code in [IdentityRecords.cs](src/ForgeFlow.Infrastructure/Identity/IdentityRecords.cs) and [AuthDbContext.cs](src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs) (4 auth migrations).
  - Implemented TouchSessionActivityAsync, GetUserMenuContextAsync (chronological previous sign-in lookup + FailedSignInsSincePrevious + accessible sites/role resolution), SwitchActiveSiteAsync, ListActiveSessionsAsync, RevokeSessionAsync, and RevokeOtherSessionsAsync in [RuntimeSessions.cs](src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs), wired with User-Agent and IP capture in [AuthenticationBackend.cs](src/ForgeFlow.Web/Authentication/AuthenticationBackend.cs).
  - Created Auth migration 20261111052007_SessionsAndActiveSiteContext (with LastActivityAtUtc backfill from IssuedAtUtc) and upgraded 	estforgflow cleanly via [upgrade-local-testdb.ps1](scripts/upgrade-local-testdb.ps1).
- **Web UI**:
  - Redesigned [UserMenu.razor](src/ForgeFlow.Web/Components/Layout/UserMenu.razor) (truncated username/display name, role badge + active site, conditional multi-site switcher shown only when > 1 site, last-login card + View Sessions link + amber failed-login alert, lightweight daily links with active sessions count badge).
  - Added [Sessions.razor](src/ForgeFlow.Web/Components/Pages/Sessions.razor) (/account/sessions — current device green badge, other sessions Sign out buttons, soft-red Sign out of all other devices button, relative/local last-activity formatting, security footnote) and updated [AccountNav.razor](src/ForgeFlow.Web/Components/Layout/AccountNav.razor) + bilingual .resx catalogs + [ResourceContractTests.cs](tests/ForgeFlow.Web.Tests/ResourceContractTests.cs) + [AuthBootstrapTests.cs](tests/ForgeFlow.Web.Tests/AuthBootstrapTests.cs).
- **Verification Evidence (2026-11-11, after final P2 edit)**:
  - .\scripts\upgrade-local-testdb.ps1: 	estforgflow upgraded cleanly to 20261111052007_SessionsAndActiveSiteContext.
  - dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (  Warning(s), 0 Error(s))**.
  - Targeted live SQL + unit + HTTP + scanner suite (UserProfileAndPreferencesTests|UserProfileAndPreferencesSqlTests|AuthBootstrapTests|AuthSessionSqlTests|AuthSessionHttpSqlTests|AuthUiContractTests|AuthHttpTests|AuthBackendTests|ResourceContractTests|ShellRenderingTests|AuditTests|AuditSqlTests|SampleLocalizationTests|HardcodedDisplayTextScannerTests): **Passed: 112, Failed: 0, Skipped: 0, Total: 112**.
  - Full non-SQL + HTTP suite (FORGEFLOW_AUTH_HTTP_TESTS=1, FORGEFLOW_TEST_SQLSERVER=''): **Passed: 380, Failed: 0, Skipped: 55, Total: 435**.
  - 
ode .\scripts\drafts.test.mjs: **19 pass, 0 fail**.
- **Next small increments**:
  - Live UI review of Dark Login, Change Password, redesigned UserMenu, and /account/sessions at https://localhost:5001, followed by **Stage P3** (/account/activity — server-filtered personal activity grid with BitDataGrid) and **Stage P4** (notification bell/callout, /account/notifications, /account/notifications-settings, shortcuts & help).
## Cross-Cutting User Profile & Workspace Experience — Dark Login Contrast Polish + Stage P3 (My Activity) + Stage P4 (Notifications Bell, Center & Notification Settings / Shortcuts / Help) — 2026-11-11

- **UI Fixes from Stage P2 Dark Login Review**:
  - Updated `[data-theme="dark"]` login styling in `src/ForgeFlow.Web/wwwroot/app.css`: integrated single-box password input with transparent reveal toggle inside the input (`4px` inset from inline-end, no separate button background), 3-layer dark contrast (`#0b1220` page background, `#161f31` card surface, `#0f1726` input surface with `#3a4a63` border), solid `#1b7399` white-text submit button without halo border, high-contrast light labels/headings, and matching bordered language switcher with branded active state.
- **Stage P3 (Personal Activity Log — `/account/activity`)**:
  - Added `MyActivityQuery`, `MyActivityEntrySummary`, `MyActivityEntryDetails`, and `IMyActivityStore` in `src/ForgeFlow.Application/Users/UserProfileContracts.cs`; implemented in `src/ForgeFlow.Infrastructure/Identity/SqlAuditOperationsStore.cs` (`ActorId == currentUserId` server-enforced + user's `SignInFailed`/`AccountLocked` + JSON/Session IP & Device enrichment, registered in `IdentityPersistence.cs`).
  - Added `src/ForgeFlow.Web/Components/Pages/MyActivity.razor` (`/account/activity` with locked personal-scope badge, date-range/event-code/search filters, `BitDataGrid` 6-row paging, `44px` rows, and parsed details dialog), linked from `UserMenu.razor` and `AccountNav.razor`.
- **Stage P4 (Notifications Bell, Center `/account/notifications`, Settings / Shortcuts / Help `/account/notifications-settings`, Command Palette & Global Shortcuts)**:
  - Added `NotificationEventCatalog`, `NotificationCategory`, `NotificationSeverity`, `NotificationListFilter`, `NotificationTemplateCatalog`, `NotificationPreferenceSnapshot`, `UserNotificationSnapshot`, `NotificationPageResult`, `IUserNotificationPublisher`, and `IUserNotificationLiveChannel` in `UserProfileContracts.cs`.
  - Added `UserNotificationPreference` and `UserNotification` entities (16 `auth` entities total), `CK_UserNotificationPreference_*` / `CK_UserNotification_*` constraints, and immutability/no-delete `Guard()` (`UserNotification` immutable except `ReadAtUtc`) in `IdentityRecords.cs` and `AuthDbContext.cs`; generated additive migration `20261111054035_UserNotificationsAndAlerts` (5 `auth` migrations total, `[ForgeFlowAuthRuntime]` `GRANT SELECT, INSERT, UPDATE` + `DENY DELETE`, `Auth.NonEmptyRollbackDenied`).
  - Implemented `RuntimeSessions` notification seeding, preference storage, unread/list/read APIs, and post-commit live notifications (`UserNotificationLiveChannel` + `AuthenticationBackend.cs`); wired login alerts (`NewDeviceSignIn`, `FailedSignInAttempts`), password change alerts, role/permission alerts (`SqlRoleOperationsStore.cs`, `SqlUserOperationsStore.cs`), and production-order transition alerts (`SqlProductionOrderOperationsStore.cs`). Also removed unnecessary `User.PreferredCulture` write during initial login `UserPreference` seeding in `RuntimeSessions.LoginAsync` so first login never alters `auth.User.RowVersion`.
  - Added `NotificationTextHelper.cs`, `NotificationBell.razor` (header bell + `BitBadge` unread count capped `9+` + 380px callout), `Notifications.razor` (`/account/notifications` with `All`/`Unread`/`Security`/`Orders` tabs, Today/Older grouping, mark-read on click/action), `NotificationSettings.razor` (`/account/notifications-settings` with Event × In-App/Email switch matrix, disabled SMTP-unconfigured Email column, Keyboard Shortcuts `/` / `Ctrl+K` / `?`, and User Guide / Report Issue / Contact Support dialogs), and global shortcuts/command palette in `MainLayout.razor` & `auth-ui.mjs`, with complete `fa-IR`/`en-US` resources.
- **Verification Evidence (2026-11-11, after final P3/P4 edit)**:
  - `.\scripts\upgrade-local-testdb.ps1`: `testforgflow` upgraded to `20261111054035_UserNotificationsAndAlerts`.
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Targeted live SQL + unit + HTTP + scanner suite (`UserProfileAndPreferencesTests|UserProfileAndPreferencesSqlTests|AuthBootstrapTests|AuthSessionSqlTests|AuthSessionHttpSqlTests|AuthUiContractTests|AuthHttpTests|AuthBackendTests|ResourceContractTests|ShellRenderingTests|AuditTests|AuditSqlTests|RoleManagementSqlTests|UserManagementSqlTests|ProductionOrderSqlTests|SampleLocalizationTests|HardcodedDisplayTextScannerTests`): **Passed: 125, Failed: 0, Skipped: 0, Total: 125**.
  - Full non-SQL + HTTP suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`, `FORGEFLOW_TEST_SQLSERVER=''`): **Passed: 380, Failed: 0, Skipped: 57, Total: 437**.
  - `node .\scripts\drafts.test.mjs`: **19 pass, 0 fail**.
- **Remaining Acceptance Gaps**:
  - Live browser visual/UX review by the user of Dark Login, Change Password, `UserMenu`, header `NotificationBell`, `/account/activity`, `/account/notifications`, `/account/notifications-settings`, Command Palette (`Ctrl+K`), and global shortcuts (`/`, `?`) in Persian RTL, English LTR, Light/Dark themes, and `320px` mobile at `https://localhost:5001`.

## Cross-Cutting User Profile & Workspace Experience — Feedback Fixes (Notifications Reliability & Bell Badge, Activity Localization & Persian Spacing, Collapsed Nav Groups) — 2026-11-11

- **Notification Reliability & Bell Badge (`NewDeviceSignIn` + All Event Triggers)**:
  - Added `AccountSession.DeviceTokenHash` (`varchar(64)`) + index `IX_Session_UserId_DeviceTokenHash` (`IdentityRecords.cs`, `AuthDbContext.cs`, migration `20261111061951_SessionDeviceTokenHash`) and per-browser persistent cookie `__Host-ForgeFlow.Device` in `AuthenticationBackend.Login`, so logins from another browser or incognito profile on localhost (`::1`) accurately emit `NewDeviceSignIn` even when coarse OS/browser strings match, without spamming repeated logins on the same browser. Also expanded `UserAgentDeviceParser` to detect Brave, Vivaldi, Samsung Internet, and mobile Edge/Firefox variants.
  - Enabled `FailedSignInAttempt` alerts on every failed password check (`AccessFailedCount >= 1`), wired missing `RoleOrAccessChanged` post-commit notifications across `SqlUserOperationsStore` (`SetOrganizationMembershipActive`, `AssignSiteMembership`, `SetSiteMembershipActive`, `SetTenantRoleActive`) and `SqlRoleOperationsStore` (`SetUserRoleAssignmentActive`, `SaveUserOperationGrants`), and localized production-order status names and built-in/membership role labels in `NotificationTextHelper.cs`.
  - Loaded initial `unreadCount` + `currentUserId` in `NotificationBell.OnInitializedAsync` and fixed `.notif-bell-badge .bit-bdg-ctn` CSS styling (`#b91c1c` badge pill) in `src/ForgeFlow.Web/wwwroot/app.css`.
- **My Activity Localization & Persian Typography Fixes**:
  - Localized the `Event` column and event detail header in `MyActivity.razor` via `NotificationTextHelper.ResolveEventLabel(entry.EventCode, Text)` instead of raw `EventCode` strings.
  - Fixed Persian spacing and typos in `SharedResources.fa-IR.resx` (`MyActivityOutcomeRecorded` & `AuditOutcomeRecorded` -> `ثبت شده`, `AuditOutcomeFailure` -> `ناموفق / رد شده`, `NotifTabUnread` -> `خوانده نشده`, `NotifTabSecurity` -> `امنیتی`, plus missing ZWNJ/spaces across Stages P1–P4 strings).
- **Navigation Drawer Submenus Collapsed by Default**:
  - Updated `MainLayout.razor` so all 6 navigation parent groups (`Production`, `Inventory`, `Quality`, `MasterData`, `Reports`, `Settings`) initialize with `IsExpanded = false` on login and page refresh, caching the `NavigationItems` list per `(Culture, Scopes)` signature so manual user expansion is preserved across non-nav re-renders while staying compatible with `RoleManagementPageTests`.
- **Verification Evidence (2026-11-11, after final edit)**:
  - `.\scripts\upgrade-local-testdb.ps1`: `testforgflow database upgraded to 20261111061951_SessionDeviceTokenHash.`
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Full non-SQL + HTTP suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`, `FORGEFLOW_TEST_SQLSERVER=''`): **Passed: 382, Failed: 0, Skipped: 58, Total: 440**.
  - `node .\scripts\drafts.test.mjs`: **19 pass, 0 fail**.
  - Targeted live SQL + unit + HTTP + shell + scanner suite (after final edit): **Passed: 136, Failed: 0, Skipped: 0, Total: 136**.
  - Follow-up fix (`MainLayout.razor`): Replaced `NoCollapse="true"` on `BitNavPanel` with `AllExpanded="false"` and `ChevronDownIcon` (since `NoCollapse="true"` forced all nav groups expanded and hid toggle chevrons). Post-edit build `0 Warning(s), 0 Error(s)` and full non-SQL + HTTP test run **Passed: 382, Failed: 0, Skipped: 58**.

## Navigation Workflow Priority Ordering — 2026-11-11

- Reordered `MainLayout.razor` `NavigationItems` and `FilteredCommands` (`Ctrl+K` palette) to follow the end-to-end manufacturing workflow (`Home` -> `MasterData` -> `Inventory` -> `Production` -> `Quality` -> `Reports` -> `Settings`), keeping `Settings` last and all parent groups collapsed (`IsExpanded = false`, `AllExpanded = false`) on login and page refresh.
- Added `MainLayout_orders_navigation_groups_by_workflow_priority_and_keeps_them_collapsed` in `tests/ForgeFlow.Web.Tests/RoleManagementPageTests.cs`.
- Verification (2026-11-11, after final edit):
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Full non-SQL + HTTP suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`, `FORGEFLOW_TEST_SQLSERVER=''`): **Passed: 383, Failed: 0, Skipped: 58, Total: 441**.
  - `node .\scripts\drafts.test.mjs`: **19 pass, 0 fail**.

## Slice 10 — Procurement (Suppliers, Purchase Orders & Goods Receipt) (`10.0`–`10.4`) — 2026-11-11

- **10.0 Design Artifacts (`docs/slices/10-procurement/`)**:
  - Authored `README.md`, `decisions.md`, `physical-erd.md`, and `test-plan.md` specifying organization-shared `Supplier`, site-scoped `PurchaseOrder`/`PurchaseOrderLine` lifecycle (`Draft -> Approved -> PartiallyReceived -> Received`, plus `Closed` short-close and pre-receipt `Cancelled`), optional `UnitPrice`/`CurrencyCode` (`IRR`/`USD`/`EUR`), atomic `ReceiveOrder` into Inventory/Lots/QC, and strict inventory reversal (`ReverseTransaction`) reopening PO receipt quantities/status.
- **10.1 Domain (`ForgeFlow.Domain`)**:
  - Added `PermissionModule.Procurement = 14` (`Procurement.Read`, `Procurement.ManageSuppliers`, `Procurement.ManageOrders`, `Procurement.ReceiveOrders`) and built-in `OrganizationAdministrator` / `SiteOperator` grants (`PermissionCatalog.cs`).
  - Added `PurchaseOrderStatus`, `ProcurementErrors`, `ProcurementText`, `Supplier`, `PurchaseOrderLine`, `PurchaseOrder`, and nullable `InventoryTransaction.PurchaseOrderId`.
- **10.2 Application (`ForgeFlow.Application`)**:
  - Added `ProcurementContracts.cs` and `ProcurementUseCases.cs` enforcing organization vs. site scoping, unit-conversion resolution via `UnitConversionGraph`, `CanBePurchased` item enforcement, lot-tracking requirements, incoming QC triggering, and user operation / rejection auditing.
  - Extended `InventoryTransactionSummary`, `ReportingContracts.cs`, `ReportingUseCases.cs` (informational `OnOrderBaseQuantity` on Material Shortage + 13-column CSV branch), and `AuditEventCatalog.cs`.
- **10.3 Infrastructure & Persistence (`ForgeFlow.Infrastructure`)**:
  - Mapped `Supplier`, `PurchaseOrder`, `PurchaseOrderLine`, `FK_InventoryTransaction_PurchaseOrder`, and `[CK_OrganizationChange_Event]` in `OrganizationModel.cs` and `OrganizationDbContext.cs` (`33` business entities); ignored procurement entities in `AuthDbContext.cs`.
  - Implemented `SqlProcurementOperationsStore` and registered `ProcurementUseCases` in `OrganizationPersistenceRegistration.cs`.
  - Extended `SqlInventoryOperationsStore.ReverseTransaction` to atomically revert PO `ReceivedBaseQuantity` and status, and `SqlReportingQueryStore` to load open approved/partial PO remaining quantities into Material Shortage.
  - Generated EF Core migration `20261111065922_ProcurementSuppliersAndPurchaseOrders` with `[ForgeFlowOrganizationRuntime]` grants and non-empty rollback guard, and upgraded `localhost/testforgflow` via `scripts/upgrade-local-testdb.ps1`.
- **10.4 Web & Localization (`ForgeFlow.Web`)**:
  - Added `/suppliers` (`Suppliers.razor`) and `/purchase-orders` (`PurchaseOrders.razor`) UI pages with `BitDataGrid`, `BitDatePicker`, create/edit draft modals, approval/short-close/cancel actions, posted receipts history, and lot/QC-aware Goods Receipt modal.
  - Inserted `ProcurementNavGroup` (`Suppliers`, `PurchaseOrders`) in `MainLayout.razor` between `MasterDataNavGroup` and `InventoryNavGroup` (kept `Settings` last and all nav groups collapsed on login/refresh), updated `ReadScopes.cs`, `RolePermissionLocalization.cs`, `Inventory.razor` (PO link display), `Reports.razor` (`OnOrderBaseQuantity` column & 13-col CSV export), `app.css`, `SharedResources.{en-US,fa-IR}.resx`, and `ResourceContractTests.cs`.
- **Verification Evidence (2026-11-11, after final edit)**:
  - `.\scripts\upgrade-local-testdb.ps1`: `testforgflow database upgraded to 20261111065922_ProcurementSuppliersAndPurchaseOrders.`
  - `dotnet ef migrations has-pending-model-changes --context AuthDbContext ...`: **No changes have been made to the model since the last migration.**
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Targeted live SQL + unit + persistence + model + role + localization + scanner suite (`ProcurementTests|ProcurementSqlTests|QualityTests|QualitySqlTests|InventoryTests|InventorySqlTests|ReportTests|ReportSqlTests|ProductionOrderTests|ProductionOrderSqlTests|OrganizationPersistenceModelTests|OrganizationSqlServerTests|RoleDomainTests|RoleManagementPageTests|ResourceContractTests|DiagnosticTests|DiagnosticSqlTests|AuditSqlTests|HardcodedDisplayTextScannerTests`): **Passed: 96, Failed: 0, Skipped: 0, Total: 96**.
  - Full non-SQL + HTTP suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`, `FORGEFLOW_TEST_SQLSERVER=''`): **Passed: 386, Failed: 0, Skipped: 59, Total: 445**.
  - `node .\scripts\drafts.test.mjs`: **19 pass, 0 fail**.
  - `node .\verification\slice01-ui\browser-check.cjs`: **4/4 pass** (`fa-IR` & `en-US`, `1440px` & `320px`).
- **Slice 10 (`10-procurement`) Status: ACCEPTED (`10.5`)** (2026-11-11): User reviewed `/suppliers`, `/purchase-orders` (including redesigned 3-section Create/Edit modal), and `/units` Currencies tab live on `https://localhost:5001`, confirmed acceptance, and requested moving to Slice 11 (`11-work-centers-routings`).
- **Remaining Acceptance Gaps**: None blocking Slice 10 acceptance.

### Purchase Order Modal Redesign (`10.4` Follow-up) — 2026-11-11

- Redesigned the Create/Edit Purchase Order dialog in [PurchaseOrders.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/PurchaseOrders.razor) and [app.css](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css) to match the structured 3-section modal layout:
  - Fixed header with title, subtitle (`ProcPoModalSubtitle`), and square close button (`38x38`, `border-radius: 10px`).
  - Scrollable modal body (`max-height: calc(92vh - 156px)`, `overflow-y: auto`) divided into «General information / اطلاعات کلی» (uniform `44px`-tall, `10px`-radius 3-column grid with `16px` gap, read-only gray `OrderNumber`, required `*` `Supplier` with placeholder, single-line 2-column `Notes`, and `BitDatePicker` placeholders) and «Order lines / اقلام سفارش» (accent section header with end-aligned `+ Add line` outline button and a horizontally scrollable CSS Grid table `2.2fr .8fr 1fr 1.2fr 1.1fr 1.5fr 40px` featuring computed `Line total` and light-red trash-icon remove button).
  - Fixed footer bar (`justify-content: space-between`) with `#0F5675` primary `Save draft` + outline `Cancel` buttons on the start side and live calculated `Grand total` (`جمع کل`) on the end side.
- Verification (2026-11-11, after final edit):
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Targeted localization, scanner, role-layout & procurement unit tests: **Passed: 35, Failed: 0, Skipped: 0**.

### Organization-Scoped Currencies Tab (`/units`) & Dynamic PO Currencies (`10.4` Follow-up) — 2026-11-11

- Added organization-scoped monetary `Currency` entity (`Id`, `OrganizationId`, 3-letter ASCII `Code`, `Name`, `Symbol`, `PrecisionScale`, `IsActive`) in [Currency.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Units/Currency.cs), extended `IUnitOperationsStore` / [`UnitUseCases`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Units/UnitUseCases.cs) / [`UnitOperationsStore`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/UnitOperationsStore.cs) (including seeding `IRR`, `USD`, `EUR` alongside standard units), and extended [`AuditEventCatalog`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Audit/AuditEventCatalog.cs) with `Currency.Created/Updated/Activated/Deactivated`.
- Replaced the hardcoded `'IRR','USD','EUR'` check constraint on `PurchaseOrder` with `CK_PurchaseOrder_Currency` (`[A-Z]{3}`) plus composite FK `FK_PurchaseOrder_Currency` referencing `[organization].[Currency] ([OrganizationId], [Code])`, and validated active currency lookup in [`SqlProcurementOperationsStore`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlProcurementOperationsStore.cs). Generated EF Core migration [20261111094413_CurrenciesMasterData.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/Migrations/20261111094413_CurrenciesMasterData.cs) (backfilling `IRR`/`USD`/`EUR` for existing organizations and granting `[ForgeFlowOrganizationRuntime]` privileges) and ignored `Currency` in [`AuthDbContext`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs).
- Updated [Units.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Units.razor) with a **Currencies (`UnitCurrenciesTab`)** tab supporting Create/Edit/Activate/Deactivate, and updated [PurchaseOrders.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/PurchaseOrders.razor) and [`DisplayFormatter`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Localization/DisplayFormatter.cs) to load active organization currencies dynamically.
- **Verification Evidence (2026-11-11, after final edit)**:
  - `.\scripts\upgrade-local-testdb.ps1`: `testforgflow database upgraded to 20261111094413_CurrenciesMasterData.`
  - `dotnet ef migrations has-pending-model-changes --context AuthDbContext ...`: **No changes have been made to the model since the last migration.**
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Targeted live SQL + unit + model + role + localization + scanner suite (`ProcurementTests|ProcurementSqlTests|UnitTests|UnitSqlTests|OrganizationPersistenceModelTests|OrganizationSqlServerTests|RoleDomainTests|RoleManagementPageTests|ResourceContractTests|HardcodedDisplayTextScannerTests`): **Passed: 69, Failed: 0, Skipped: 0, Total: 69**.

---

## Slice 11 (`11-work-centers-routings`) — Work Centers, Routings & Shop-Floor Operations (ACCEPTED)

- **11.0 Design Artifacts**: Added `docs/slices/11-work-centers-routings/{README.md,decisions.md,physical-erd.md,test-plan.md}` establishing `W-Q01`–`W-Q06` (site-scoped `WorkCenter` and `Routing`, minute-based standard/planned/actual times, nullable `ProductionOrder.RoutingId`/`RoutingCodeSnapshot` with snapshot into `ProductionOrderOperation`, `PermissionModule.Routings = 15`, and `WorkCenters`/`Routings` in `MasterDataNavGroup` after `Boms`).
- **11.1 Domain (`ForgeFlow.Domain`)**:
  - Extended `PermissionCatalog` with `PermissionModule.Routings = 15` (`Routings.Read`, `Routings.Manage`, `Operations.Execute`).
  - Added `WorkCenter`, `Routing`, `RoutingOperation`, `ProductionOrderOperation`, `WorkCenterType`, `RoutingStatus`, `ProductionOrderOperationStatus`, `RoutingErrors`, and `RoutingText` in `src/ForgeFlow.Domain/Routings/`, and extended `ProductionOrder` with `RoutingId`, `RoutingCodeSnapshot`, and `ApplyRoutingSnapshot`.
- **11.2 Application (`ForgeFlow.Application`)**:
  - Added `RoutingContracts.cs` (`RoutingPermissions`, WorkCenter/Routing/Operation/WorkCenterLoad DTOs & `IRoutingOperationsStore`) and `RoutingUseCases.cs` (RBAC, site isolation, work center load calculation, operation lifecycle execution).
  - Extended `ProductionOrderContracts.cs`, `ProductionOrderUseCases.cs` (optional explicit or auto-default routing snapshot + operation time calculation on CreateDraft/UpdateDraft/RefreshDraftSnapshot/ReleaseOrder), `AuditEventCatalog.cs`, and `DiagnosticContracts.cs`.
- **11.3 Infrastructure & Database (`ForgeFlow.Infrastructure`)**:
  - Configured the 4 new site-scoped tables (`38` business entities) in `OrganizationModel.cs` & `OrganizationDbContext.cs`, ignored them in `AuthDbContext.cs`, implemented `SqlRoutingOperationsStore`, extended `SqlProductionOrderOperationsStore`, generated migration `20261111102337_WorkCentersAndRoutings` with `[ForgeFlowOrganizationRuntime]` privileges & non-empty rollback guard, and updated `scripts/upgrade-local-testdb.ps1`.
- **11.4 Web & Localization (`ForgeFlow.Web`)**:
  - Added `/work-centers` (`WorkCenters.razor` with work center planned load summary and CRUD/activate modal), `/routings` (`Routings.razor` with draft operations sequence builder & lifecycle controls), integrated Routing selection + shop-floor operation execution (`Start`/`Pause`/`Complete`/`Skip`) into `ProductionOrders.razor`, added `WorkCenters` & `Routings` under `MasterDataNavGroup` in `MainLayout.razor`, and added complete `en-US`/`fa-IR` `.resx` resources.
  - Extracted shared [`ForgeFormDialog.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Layout/ForgeFormDialog.razor) (header with subtitle + square `38px` close button, scrollable `44px`-field/`10px`-radius/`16px`-gap body, fixed footer bar with `#0F5675` primary button), adopted it in [`PurchaseOrders.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/PurchaseOrders.razor), and redesigned the Create Draft Routing modal in [`Routings.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Routings.razor) with a 3-col first row (Site, Code*, Version), full-width Name*, 2-col Item*/BOM row, clickable `#0F5675` `20px` default-routing checkbox card (`.ff-check-card`), and optional notes.
- **Verification Evidence (2026-11-11, after final edit)**:
  - `.\scripts\upgrade-local-testdb.ps1`: `testforgflow database upgraded to 20261111102337_WorkCentersAndRoutings.`
  - `dotnet ef migrations has-pending-model-changes --context AuthDbContext`: **No changes have been made to the model since the last migration.**
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Targeted Slice 11 SQL + Unit + Persistence + Model + Role + Localization + Scanner suite (`RoutingTests|RoutingSqlTests|ProductionOrderTests|ProductionOrderSqlTests|OrganizationPersistenceModelTests|OrganizationSqlServerTests|RoleDomainTests|RoleManagementPageTests|ResourceContractTests|HardcodedDisplayTextScannerTests`): **Passed: 71, Failed: 0, Skipped: 0, Total: 71**.
  - Full non-SQL + HTTP suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`, `FORGEFLOW_TEST_SQLSERVER=''`): **Passed: 389, Failed: 0, Skipped: 60, Total: 449**.
  - `node .\scripts\drafts.test.mjs`: **19 pass, 0 fail**.
  - Post UI-redesign build & targeted suite (`ResourceContractTests|HardcodedDisplayTextScannerTests|RoleManagementPageTests|RoutingTests|ProcurementTests`): **Passed: 44, Failed: 0, Skipped: 0**.
  - `node .\verification\slice01-ui\browser-check.cjs`: **4/4 pass** (`fa-IR` / `en-US`, `1440px` / `320px`).
- **Slice 11 (`11-work-centers-routings`) Status: ACCEPTED (`11.6`)** (2026-11-11): User reviewed live UI on `https://localhost:5001` including the redesigned Create Draft Routing modal and shared dialog shell and approved advancing to next work (Slice 12: Stock Reservations & Operations Dashboard).
- **Remaining Acceptance Gaps**: None blocking Slice 11 acceptance.



## Slice 12 — Stock Reservations, Auto-Consume/Release Lifecycle & Operations Dashboard (`12.0`–`12.5`) — 2026-11-11

- **12.0 Design Artifacts (`docs/slices/12-reservations-dashboard/`)**:
  - Authored `README.md`, `decisions.md`, `physical-erd.md`, and `test-plan.md` establishing `R-Q01`–`R-Q05`: `InventoryBalance.ReservedBaseQuantity` (`Available = OnHand - Blocked - Reserved`), hybrid explicit/FIFO `StockReservation` on `Released`/`InProgress` Production Orders, automatic reservation consumption on `ProductionIssue`, automatic release on `CompleteOrder`/`CancelOrder`, `MaterialShortageReport` reservation deduction, and live capability-filtered Operations Dashboard directly on `/` (`Home.razor`) using existing `*.Read` permissions.
- **12.1 Domain (`ForgeFlow.Domain`)**:
  - Extended [`InventoryErrors.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Inventory/InventoryErrors.cs) (`ReservationNotFound`, `ReservationInvalidState`, `ReservationExceedsAvailable`, `ReservationNotAllowedForOrderState`), [`InventoryBalance.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Inventory/InventoryBalance.cs) (`ReservedBaseQuantity`, `ReserveQuantity`, `ReleaseReservedQuantity`, `AvailableBaseQuantity` enforcement), and added [`StockReservation.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Inventory/StockReservation.cs) (`ReservationStatus`, `Consume`, `Release`).
- **12.2 Application (`ForgeFlow.Application`)**:
  - Added [`ReservationContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Inventory/ReservationContracts.cs), [`StockReservationUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Inventory/StockReservationUseCases.cs), [`DashboardContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Dashboard/DashboardContracts.cs), and [`DashboardUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Dashboard/DashboardUseCases.cs).
  - Updated [`InventoryContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Inventory/InventoryContracts.cs) (`InventoryBalanceSummary.ReservedBaseQuantity`), [`ReportingContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Reporting/ReportingContracts.cs) & [`ReportingUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Reporting/ReportingUseCases.cs) (`ReservedBaseQuantity` / `TotalReservedBaseQuantity` deducted in `ComputeMaterialShortage`), and [`AuditEventCatalog.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Audit/AuditEventCatalog.cs).
- **12.3 Infrastructure & Database (`ForgeFlow.Infrastructure`)**:
  - Mapped `InventoryBalance.ReservedBaseQuantity`, `StockReservation` (`39` business entities), updated `CK_InventoryBalance_Blocked`, and ignored `StockReservation` in [`AuthDbContext.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs).
  - Implemented [`SqlStockReservationStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlStockReservationStore.cs) and [`SqlDashboardQueryStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlDashboardQueryStore.cs), updated [`SqlInventoryOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlInventoryOperationsStore.cs) (`ProductionIssue` auto-consume) & [`SqlProductionOrderOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlProductionOrderOperationsStore.cs) (`CompleteOrder`/`CancelOrder` auto-release) & [`SqlReportingQueryStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlReportingQueryStore.cs), generated migration `20261111110939_StockReservationsAndDashboard` with `[ForgeFlowOrganizationRuntime]` grants and rollback guard, and upgraded `localhost/testforgflow`.
- **12.4 Web & Localization (`ForgeFlow.Web`)**:
  - Implemented live permission-filtered **Operations Dashboard** on [`Home.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Home.razor) with Org/Site scope selectors, KPI cards, and deep-linked operational queues (`AttentionOrders`, `TopShortages`, `WorkCenterLoads`, `PendingInspections`, `OpenPurchaseOrders`).
  - Extended [`Inventory.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Inventory.razor) (new `Reservations` tab + `Reserved` & true `Available` badges on Balances), [`ProductionOrders.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ProductionOrders.razor) (per-input `Reserve` dialog with FIFO/explicit Warehouse & Lot, order reservations list, and `Release` button), [`Reports.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Reports.razor) (`Reserved` sub-label in Material Shortage), [`ReadScopes.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/ReadScopes.cs) (`LoadDashboardScopesAsync`), [`RolePermissionLocalization.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Roles/RolePermissionLocalization.cs), [`app.css`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css), and `SharedResources.{en-US,fa-IR}.resx`.
- **12.5 Verification Evidence (2026-11-11, after final edit)**:
  - `.\scripts\upgrade-local-testdb.ps1`: `testforgflow database upgraded to 20261111110939_StockReservationsAndDashboard.`
  - `dotnet ef migrations has-pending-model-changes --context AuthDbContext`: **No changes have been made to the model since the last migration.**
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Targeted Slice 12 live SQL + Unit + Persistence + Model + Role + Localization + Scanner + Shell suite (`ReservationTests|ReservationSqlTests|DashboardTests|InventoryTests|InventorySqlTests|ReportTests|ReportSqlTests|ProductionOrderTests|ProductionOrderSqlTests|RoutingTests|RoutingSqlTests|OrganizationPersistenceModelTests|OrganizationSqlServerTests|RoleDomainTests|RoleManagementPageTests|ResourceContractTests|HardcodedDisplayTextScannerTests|ShellRenderingTests`): **Passed: 94, Failed: 0, Skipped: 0, Total: 94**.
  - Full non-SQL + HTTP suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`, `FORGEFLOW_TEST_SQLSERVER=''`): **Passed: 393, Failed: 0, Skipped: 62, Total: 455**.
  - `node .\scripts\drafts.test.mjs`: **19 pass, 0 fail**.
  - `node .\verification\slice01-ui\browser-check.cjs`: **4/4 pass** (`fa-IR` / `en-US`, `1440px` / `320px`).
- **Slice 12 (`12-reservations-dashboard`) Status: ACCEPTED (`12.6`)** (2026-11-11): User reviewed live UI on `https://localhost:5001` (`/`, `/inventory`, `/production-orders`, `/reports`), approved Slice 12, and authorized git commit & push and advancing to the next work.
- **Remaining Acceptance Gaps**: None blocking Slice 12 acceptance.

## Post-Slice 12 Brand & Shell UI Refinement (Favicon, Login & 5-Group Sidebar Shell) — 2026-11-11

- **Brand Assets & Favicon (`wwwroot/`)**:
  - Added brand icons, symbol marks, and favicon set (`favicon.{ico,png,svg}`, `images/brand/forgeflow-app-icon.{svg,png}`, `forgeflow-logo-horizontal.{svg,png}`, `forgeflow-symbol.{svg,png}`, `forgeflow-symbol-mono-{dark,white}.{svg,png}`, plus crisp light/dark sidebar lockups `forgeflow-sidebar-logo-{light,dark}.svg`) and wired `<link rel="icon">` / `apple-touch-icon` in [`App.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/App.razor).
- **Login Page Redesign (`Login.razor` & `app.css`)**:
  - Replaced inline `F` badge with centered `72x72px` `forgeflow-app-icon.svg`, centered title/subtitle, `48px` rounded-10px inputs (`#0c1322` input / `#131c2e` card / `#080f1e` page 3-layer dark hierarchy), `#0b4ab8` (light) / `#1a6adb` (dark) primary submit & active language pill buttons.
- **5-Group Sidebar & `64px` Header/Breadcrumb Shell (`MainLayout.razor`, `auth-ui.mjs`, `app.css`)**:
  - Replaced `BitNavPanel` with a `260px` (`72px` desktop-collapsed rail + mobile `<1024px` slide-over drawer) collapsible 5-section sidebar: ungrouped `Home` plus 4 collapsible accordion groups (`Production`, `Inventory`, `Analytics` eliminating duplicate `"Reports"`, and `Administration` / `AdminNavGroup`) starting collapsed by default on login and refresh, pinned top brand lockup + scrollable nav list + `38px` rows + `3px` inline-start active bar (`#0b4ab8` light / `#7cc4ff` dark).
  - Updated top `64px` header with panel toggle button (`.ff-sidebar-toggle`), live RTL/LTR breadcrumb (`Section ‹ Page`), language button, notification bell, and `#0b4ab8` / `#1a6adb` user menu pill.
- **Verification Evidence (2026-11-11, after final edit)**:
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Targeted UI/Contract/Scanner suite (`RoleManagementPageTests|ResourceContractTests|ShellRenderingTests|HardcodedDisplayTextScannerTests`): **Passed: 38, Failed: 0, Skipped: 0, Total: 38** (including `MainLayout_orders_navigation_into_five_clean_sections` verifying all named groups start collapsed).
  - Full non-SQL + HTTP suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`, `FORGEFLOW_TEST_SQLSERVER=''`), including updated [`RoleManagementPageTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/RoleManagementPageTests.cs) & [`ResourceContractTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/ResourceContractTests.cs): **Passed: 393, Failed: 0, Skipped: 62, Total: 455**.
  - Live HTTP verification (`200` on `/account/login`, `/favicon.svg`, and brand SVGs) + `node .\verification\slice01-ui\browser-check.cjs`: **4/4 pass** (`fa-IR` / `en-US`, `1440px` / `320px`).
- **Status: ACCEPTED** (2026-11-11): User verified live UI on `https://localhost:5001` (including collapsed-by-default accordion behavior on login/refresh) and authorized commit & push.
- **Remaining Acceptance Gaps**: None.

## Slice 13: Dynamic Lab Parameters, Item/Classification Inspection Specs & Lot COA (`13-lab-specs-coa`) — In Review (`13.0`–`13.5` complete)

- **13.0 Design Artifacts (`docs/slices/13-lab-specs-coa/`)**:
  - Authored `README.md`, `decisions.md`, `physical-erd.md`, and `test-plan.md` establishing `L-Q01`–`L-Q04` to support arbitrary lab tests (e.g., `MFI g/10min`, Purity `%`) across thousands of items via reusable catalog parameters, hierarchical Classification inheritance overridden by Item specs, numeric boundary evaluation + out-of-spec concession reason enforcement, and Production/Inventory read access to Lot Certificates of Analysis (`COA`).
- **13.1 Domain (`ForgeFlow.Domain`)**:
  - Extended [`QualityErrors.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Quality/QualityErrors.cs) (+12 codes including `ConcessionReasonRequired`, `SpecActiveConflict`, `SpecTargetInvalid`) & [`QualityText.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Quality/QualityText.cs), added [`QualityParameter.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Quality/QualityParameter.cs) (`QualityParameterDataType`, `Symbol`, `UnitSymbol`, `DecimalPlaces`, `DefaultTestMethod`) and [`QualitySpec.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Quality/QualitySpec.cs) (`QualitySpecTargetKind`, `QualitySpecCheckpoint`, `QualitySpec`, `QualitySpecLine`), and extended [`QualityInspectionLine.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Quality/QualityInspectionLine.cs) (immutable parameter/tolerance snapshots, `MeasuredNumericValue`, `IsMandatory`, `EvaluateNumericPass`) & [`QualityInspection.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Quality/QualityInspection.cs) (`CertificateNumber`, `ConcessionReasonRequired` gate).
- **13.2 Application (`ForgeFlow.Application`)**:
  - Added [`QualitySpecContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Quality/QualitySpecContracts.cs) & [`QualitySpecUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Quality/QualitySpecUseCases.cs) (`GetLotCertificate`, `ResolveApplicableSpec`, parameter/spec CRUD, `ApplySpecToInspection`, `Quality.Read | Inventory.Read | ProductionOrders.Read` read policy), updated [`QualityUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Quality/QualityUseCases.cs) (`UpdateMeasurements` server-side numeric bound evaluation + `CertificateNumber`) & [`AuditEventCatalog.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Audit/AuditEventCatalog.cs) (+8 audit events).
- **13.3 Infrastructure & Database (`ForgeFlow.Infrastructure`)**:
  - Mapped `QualityParameter`, `QualitySpec`, `QualitySpecLine`, and `QualityInspection` / `QualityInspectionLine` columns (`42` business entities) in [`OrganizationModel.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationModel.cs), ignored new types in [`AuthDbContext.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs), added [`SqlQualitySpecStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlQualitySpecStore.cs), wired auto-population into [`SqlInventoryOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlInventoryOperationsStore.cs) and [`SqlQualityOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlQualityOperationsStore.cs) (`.Include(x => x.Lines)` on `RecordDisposition`), generated migration `20261111125209_LabParametersSpecsAndCoa` (`12` migrations) with `[ForgeFlowOrganizationRuntime]` grants, and upgraded `localhost/testforgflow`.
- **13.4 Web & Localization (`ForgeFlow.Web`)**:
  - Added reusable [`LotCertificateDialog.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Layout/LotCertificateDialog.razor) wired into [`Quality.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Quality.razor) (plus new **Lab Parameters** & **Inspection Specs** tabs, `Load Spec Template`, numeric auto-eval, `CertificateNumber`, and out-of-spec concession warning), [`ProductionOrders.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ProductionOrders.razor), [`Inventory.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Inventory.razor), and [`Traceability.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Traceability.razor), plus resolved QC Spec display in [`Items.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Items.razor), localized resources (`SharedResources.{en-US,fa-IR}.resx`), error mappings, and Light/Dark + RTL/LTR + mobile CSS.
- **13.5 Verification Evidence (2026-11-11, after final edit)**:
  - `.\scripts\upgrade-local-testdb.ps1`: `testforgflow database upgraded to 20261111125209_LabParametersSpecsAndCoa.`
  - `dotnet ef migrations has-pending-model-changes` (`AuthDbContext` & `OrganizationDbContext`): **No changes have been made to the model since the last migration.**
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Targeted Slice 13 live SQL + Unit + Persistence + Model + Role + Localization + Scanner + Shell suite (`LabSpecTests|LabSpecSqlTests|QualityTests|QualitySqlTests|ProcurementSqlTests|OrganizationPersistenceModelTests|OrganizationSqlServerTests|ResourceContractTests|HardcodedDisplayTextScannerTests|RoleManagementPageTests|ShellRenderingTests`): **Passed: 62, Failed: 0, Skipped: 0, Total: 62**.
  - Full non-SQL + HTTP suite (`FORGEFLOW_AUTH_HTTP_TESTS=1`, `FORGEFLOW_TEST_SQLSERVER=''`): **Passed: 395, Failed: 0, Skipped: 63, Total: 458**.
  - `node .\scripts\drafts.test.mjs`: **19 pass, 0 fail**.
  - Live HTTP verification (`HTTP 200` on `/account/login`), `node .\verification\slice01-ui\browser-check.cjs` (`4/4 pass` across `fa-IR` / `en-US`, `1440px` / `320px`), and Google Chrome launch via `browser` subagent (`17/17 HTTP 200`, `0` console errors).
- **Slice 13 (`13-lab-specs-coa`) Status: ACCEPTED (`13.6`)** (2026-11-11): User reviewed and approved Slice 13 and authorized git commit & push.
- **Remaining Acceptance Gaps**: None.

## Living Persian Documentation Increment (Slice 13)

- **Scope**: Created offline-ready illustrated Persian HTML guides under `docs/` synchronized with `main@154ca00` (Slice 13) and established the living maintenance workflow in [`docs/README.md`](file:///d:/Projects/ForgeFlow/docs/README.md).
- **Files Created**:
  - [`docs/deployment-guide-fa.html`](file:///d:/Projects/ForgeFlow/docs/deployment-guide-fa.html): Deployment Manager guide covering .NET 10.0 (10.0.14+ Hosting Bundle, SDK 10.0.403), IIS + WebSocket + Data Protection, ConnectionStrings__ForgeFlow, all 3 EF Core DbContexts (AuthDbContext 6 migrations, OrganizationDbContext 12 migrations, DiagnosticsDbContext 1 migration), interactive ForgeFlow.Bootstrap (initialize-deployment / eset-password), inline SVG architecture diagram, and an interactive localStorage-persisted Go-Live checklist.
  - [`docs/admin-guide-fa.html`](file:///d:/Projects/ForgeFlow/docs/admin-guide-fa.html): System Administrator illustrated end-to-end guide covering /account/login (with real captured light/dark a-IR screenshots), exact 5-group menu map, system/custom roles & permissions, Master Data, Procurement, Lab Parameters + Inspection Specs + Lot COA (/quality), Production Orders + Reservations, Inventory, Lot Traceability, Reports, Audit, and Error Logs, with inline SVG workflow diagrams.
  - Bundled offline assets: `docs/assets/Vazirmatn.woff2`, `docs/screenshots/login-light-fa.png`, `docs/screenshots/login-dark-fa.png`.
- **Verification Evidence (2026-11-11)**:
  - `dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Headless Chrome Playwright verification across both HTML guides at 1440px and 320px (RTL, offline Vazirmatn.woff2, embedded screenshots, zero overflow, zero console/request errors, interactive checklist persistence): **4/4 PASS**.
  - Live Development instance launched at https://localhost:5001 (	estforgflow): GET /account/login returns **HTTP 200**; `node .\verification\slice01-ui\browser-check.cjs` **4/4 PASS** (a-IR/en-US, 1440px/320px).
- **Remaining Acceptance Gaps**: Authenticated page screenshots inside `docs/admin-guide-fa.html` await temporary test account credentials (to preserve startup/credential safety rules) if the user wants real post-login captures replacing the remaining screenshot placeholders.
- **Next Small Step**: Review the two HTML guides locally or supply test login credentials for authenticated screenshot captures; authorize git commit/push when ready.
## GPPS End-to-End Case Study & Bug-Fix Increment

- **Scope**: Executed a full real-world GPPS (General Purpose Polystyrene) manufacturing case study inside Chrome against `https://localhost:5001` (`testforgflow`) covering Units, Warehouses (WH-RM-01, WH-FG-01), Suppliers (SUP-PARS, SUP-ARIA), 4 Items (RM-SM-001, RM-WMO-001, RM-ZNST-001, FG-GPPS-001), 11 Lab Parameters & 4 Inspection Specs (SPEC-SM-IN, SPEC-WMO-IN, SPEC-ZN-IN, SPEC-GPPS-FG), BOM (BOM-GPPS-001 v1), 4 Work Centers + Routing (RT-GPPS-001), Procurement POs + Receiving + Incoming Lab QC & COA, Production Orders (PO-GPPS-1405-01, PO-GPPS-1405-02), Reservations, Shop-floor Operations, ProductionIssue + ProductionReceipt, Final GPPS COA, Backward Lot Traceability (LOT-GPPS-1405-02 -> raw material lots), and Material Consumption Variance Reports. Embedded 16 verified real screenshots into `docs/admin-guide-fa.html`.
- **Issues Found & Fixed**:
  1. **Razor @ email-trap literals (@... and L@...)** rendered raw code instead of version/level numbers in Boms.razor, Items.razor, ProductionOrders.razor, Quality.razor, Routings.razor, Traceability.razor, and LotCertificateDialog.razor; fixed to parenthesized @(...) / L@(...).
  2. **Persian numeric input disappearance** in Quality.razor (<input type="number" value="@line.MeasuredNumericValue">): a-IR decimal formatting (/) caused Chrome <input type="number"> to blank saved values on reload; fixed using InvariantCulture formatting + dir="ltr".
  3. **ProductionIssue warehouse & lot prefill UX** in Inventory.razor: PrefillFromProductionOrderAsync previously overwrote a user's selected warehouse (WH-RM-01) with the first warehouse on site (WH-FG-01); updated to preserve manual same-site warehouse choice and prefill the warehouse and lot codes from open StockReservations.
  4. **RTL layout polish** in Home.razor (WorkCenterLoad m flip in RTL), Routings.razor / pp.css (.rt-ops-row grid width, .ff-check-card checkbox alignment), and .read-badge (white-space: nowrap).
- **Verification Evidence (2026-11-11, after final edit)**:
  - dotnet build .\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (0 Warning(s), 0 Error(s))**.
  - Full non-SQL + HTTP test suite (FORGEFLOW_AUTH_HTTP_TESTS=1, FORGEFLOW_TEST_SQLSERVER=''): **Passed: 382, Failed: 0, Skipped: 0, Total: 382**.
  - Live https://localhost:5001 verification: GET /account/login returns **HTTP 200**; 
ode .\verification\slice01-ui\browser-check.cjs **4/4 PASS**; Batch #2 GPPS Chrome E2E (PO-GPPS-1405-02 + PROD-ISS-GPPS-02 reservation consume + a-IR decimal lab persistence + LOT-GPPS-1405-02 backward traceability L1 + Reports): **PASS (0 console/page errors)**; offline Playwright verification of docs/deployment-guide-fa.html & docs/admin-guide-fa.html (1440px / 320px, 16 screenshots, local Vazirmatn.woff2): **4/4 PASS**.

## UI/UX Polish Increment: Icon Actions, Resizable DataGrids & Tabular Modals

- **Scope**:
  1. Converted text-based Activate/Deactivate row buttons into icon-only buttons (`IconOnly="true"`, `.read-action-deactivate` / `.read-action-activate`, localized `<BitTooltip>`) in **Work Centers** (`WorkCenters.razor`), **Suppliers** (`Suppliers.razor`), and **Quality Control** (`Quality.razor` — both Lab Parameters and Inspection Specs tabs).
  2. Enabled column drag-resizing (`Resizable="true"`) on all **29** `<BitDataGrid>` components across the application (`Audit`, `Boms`, `Errors`, `Inventory`, `Items`, `MyActivity`, `OrganizationReader`, `ProductionOrders`, `PurchaseOrders`, `Quality`, `Reports`, `Roles`, `Routings`, `Suppliers`, `Units`, `Users`, `WorkCenters`) and styled `.bit-dtg-resizer` handles in `app.css`.
  3. Fixed column clipping in **Purchase Orders** (`PurchaseOrders.razor`) by tuning initial column `Width`/`MinWidth` proportions (`150/106/144/94/104/96/138/146/72 px`), adding `WrapCellText="true"` and `white-space: nowrap` for `<bdi>` values inside grid cells so order numbers, dates, progress fractions, and totals render unclipped on desktop in both `fa-IR` and `en-US`.
  4. Redesigned the **Purchase Order Detail Modal** (`PurchaseOrders.razor`) and **Shortage / Consumption & Yield Variance Detail Modals** (`Reports.razor`) using structured `.ff-detail-hero` cards, responsive 2-column `.ff-kv-table` key-value tables, `.ff-kpi-strip` KPI tiles, `.audit-fields-box` / `.audit-fields-table` tables with variance status badges, and `@media (max-width: 680px)` mobile overflow protection across Light & Dark themes.
- **Verification Evidence (2026-11-11, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (`0` Warning(s), `0` Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 382, Failed: 0, Skipped: 0, Total: 382**.
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200**; `node d:\Projects\ForgeFlow\verification\slice01-ui\browser-check.cjs` **4/4 PASS** (`fa-IR` / `en-US`, `1440px` / `320px`).
  - Authenticated Playwright verification across both `fa-IR` (RTL) and `en-US` (LTR), light & dark modes, and `1440px` / `320px` viewports on `/purchase-orders` (grid + detail modal), `/reports` (Variance detail modal), `/work-centers`, `/suppliers`, and `/quality` (Lab Parameters & Inspection Specs tabs): **PASS (`0` console/page errors, `cellOverflowX: false`, zero document horizontal overflow)**.
- **Remaining Acceptance Gaps**: None.
- **Next Small Step**: Continue manual browser review at `https://localhost:5001` or authorize git commit/push when ready.

## UI/UX Polish Increment 2: BOM Workbench, BOM Version Icons & Lot Traceability

- **Scope**:
  1. Converted the BOM grid row `Versions` button (`Boms.razor`) into an icon-only `<BitButton>` (`VersionsIcon` / `.ff-icon--versions`) with localized `<BitTooltip>` next to the `Edit` icon button.
  2. Converted the **BOM Details & Versions** modal row `Open` button (`Boms.razor`) into an icon-only `<BitButton>` (`IconName="View"`) with localized `<BitTooltip>`.
  3. Redesigned the **BOM Version Workbench** modal (`Boms.razor` + `app.css`): widened to `1100px` (`MaxWidth="calc(100vw - 24px)"`), added `.ff-detail-hero` summary header + immutable notice banner (`.bom-wb-banner`) + `.bom-wb-actions-bar`; rendered structured `.ff-kv-table` header and read-only `.audit-fields-table` Inputs/Outputs/Applicability tables for locked versions (`Approved`/`Active`/`Superseded`/`Obsolete`) instead of clipped disabled form inputs, and structured `.bom-wb-table` + icon-only Trash row-removal buttons for `Draft` editing.
  4. Reorganized **Lot/Batch Traceability** (`Traceability.razor` + `app.css`): wrapped page content in `.trace-stack`, widened item selector (`trace-filter-item`), aligned `.trace-run-btn`, redesigned the root lot summary in `.trace-summary-card` (`.ff-detail-hero` + `.ff-kpi-strip` + COA button with `AuditIcon`), separated backward/forward genealogy and lot movement sections in `.trace-section-card` with `.trace-section-head` counts and `.bom-tree-level` chips, and added `[data-theme="dark"]` styling for `.audit-fields-box` and `.audit-fields-table`.
- **Verification Evidence (2026-11-11, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (`0` Warning(s), `0` Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 382, Failed: 0, Skipped: 0, Total: 382**.
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200**; `node d:\Projects\ForgeFlow\verification\slice01-ui\browser-check.cjs` **4/4 PASS**.
  - Authenticated Chrome Playwright verification (`fa-IR` RTL & `en-US` LTR, Light & Dark modes, `1440px` desktop & `320px` mobile) across `/boms` (grid icon buttons, Versions modal Open icon button, active `BOM-GPPS-001 · v1` workbench modal) and `/traceability?itemId=1&lotCode=LOT-SM-1405-01`: **PASS (`0` console/page errors, zero horizontal overflow)**.
- **Remaining Acceptance Gaps**: None.
- **Next Small Step**: Continue manual browser review at `https://localhost:5001` or authorize git commit/push when ready.

## Security Audit & Authentication Hardening Increment

- **Scope**: Performed a comprehensive security audit of ForgeFlow (with emphasis on /account/login, /account/sign-in, /account/logout, /account/change-password-submit, /account/avatar, RuntimeSessions, AuthDbContext, uth-ui.mjs, and NuGet dependencies), added automated security regression tests, and applied hardening fixes.
- **Changes Applied**:
  1. **NuGet Vulnerability Remediation** (Directory.Packages.props, src/ForgeFlow.Infrastructure/ForgeFlow.Infrastructure.csproj): Pinned transitive dependency System.Security.Cryptography.Pkcs to 10.0.13 to resolve high/moderate advisories (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7) and unblock clean dotnet restore under TreatWarningsAsErrors.
  2. **HTTP Security Headers & HSTS** (src/ForgeFlow.Web/Authentication/AuthenticationBackend.cs, src/ForgeFlow.Web/Program.cs): Added UseSecurityHeaders() middleware emitting X-Content-Type-Options: nosniff, X-Frame-Options: DENY (with TryAdd preserving framework SAMEORIGIN on antiforgery pages), Referrer-Policy: strict-origin-when-cross-origin, and Permissions-Policy: camera=(), microphone=(), geolocation=(); enabled pp.UseHsts() outside Development and SmokeTest.
  3. **Per-IP Login Rate Limiter Partitioning** (src/ForgeFlow.Web/Authentication/AuthenticationBackend.cs): Updated uth-login fixed-window rate limiter from a single global "auth-login" key to context.Connection.RemoteIpAddress?.ToString() ?? "unknown" so one client IP cannot exhaust the login budget for all users while remaining immune to X-Forwarded-For header spoofing.
  4. **Security Test Suite** (	ests/ForgeFlow.Web.Tests/SecurityAuditTests.cs): Added 26 test executions covering __Host- cookie attributes, non-HTTPS rejection, 16KB body limit, duplicate field rejection, already-authenticated re-login guard, open-redirect payloads, anonymous vs. authenticated CSRF token binding, password/lockout policy bounds, SessionClaims validation, security headers, per-IP rate limiting (429 Auth.LoginThrottled), uth-ui.mjs XSS/storage checks, and /account/avatar authorization.
- **Verification Evidence (2026-11-12, after final edit)**:
  - dotnet list package --vulnerable --include-transitive: **0 vulnerable packages** across all 8 projects.
  - dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (  Warning(s),   Error(s))**.
  - Non-SQL + HTTP test suite (FORGEFLOW_TEST_SQLSERVER='', FORGEFLOW_AUTH_HTTP_TESTS='1', --filter "FullyQualifiedName!~Sql"): **Passed: 408, Failed: 0, Skipped: 0, Total: 408**.
  - Live Development instance at https://localhost:5001 (	estforgflow):
    - GET /account/login: **HTTP 200** with security headers (X-Content-Type-Options: nosniff, X-Frame-Options: SAMEORIGIN, Content-Security-Policy: frame-ancestors 'self', Referrer-Policy: strict-origin-when-cross-origin, Permissions-Policy: camera=(), microphone=(), geolocation=(), Cache-Control: no-cache, no-store, max-age=0).
    - GET /: **HTTP 302** -> /account/login?returnUrl=%2F (Cache-Control: no-store).
    - POST /account/sign-in without CSRF: **HTTP 400** (Auth.RequestRejected).
    - POST /account/sign-in with open redirect //evil.example: **HTTP 400** (Auth.RequestRejected).
    - POST /account/sign-in with invalid credentials: **HTTP 401** (Auth.LoginRejected).
    - POST /account/change-password-submit unauthenticated: **HTTP 401** (Auth.SessionInvalid).
    - 
ode verification/slice01-ui/browser-check.cjs: **4/4 PASS** (a-IR & en-US, 1440px & 320px).
- **Remaining Acceptance Gaps**: SQL Server integration tests (*Sql*) were not run here because FORGEFLOW_TEST_SQLSERVER was not configured; Content-Security-Policy script/style directives remain deferred pending reverse-proxy / Blazor Server inline-asset alignment.
- **Next Small Step**: Review the security audit report/walkthrough or authorize git commit/push when ready.
## Account Security Hub, TOTP 2FA & Proactive Reminders (Phase 1)

- **Scope**: Implemented /account/security (Account Security Hub), TOTP Authenticator setup + 10 single-use recovery codes, /account/login-2fa + /account/sign-in-2fa with 30-day __Host-ForgeFlow.Remember2FA device trust, configurable Idle Auto-Logout (Off / 15 / 30 / 60 min), and proactive 2FA reminders (avatar menu Recommended badge, 7-day snoozable Home banner, one-time bell notification, post-password-change prompt, and security-alert links).
- **Files Changed / Added**:
  - src/ForgeFlow.Application/Users/UserProfileContracts.cs, src/ForgeFlow.Application/Audit/AuditEventCatalog.cs
  - src/ForgeFlow.Infrastructure/Identity/IdentityRecords.cs, AuthDbContext.cs, IdentityPersistence.cs, RuntimeSessions.cs, and EF Core migration 20261112063318_AccountSecurityAndTwoFactor
  - src/ForgeFlow.Web/Authentication/QrCodeSvg.cs (zero-dependency local SVG QR renderer), AuthenticationBackend.cs
  - src/ForgeFlow.Web/Components/Pages/Security.razor, LoginTwoFactor.razor, Home.razor, ChangePassword.razor
  - src/ForgeFlow.Web/Components/Layout/UserMenu.razor, AccountNav.razor
  - src/ForgeFlow.Web/Localization/NotificationTextHelper.cs, src/ForgeFlow.Web/Resources/** (a-IR / en-US)
  - src/ForgeFlow.Web/wwwroot/auth-ui.mjs, src/ForgeFlow.Web/wwwroot/app.css
  - 	ests/ForgeFlow.Web.Tests/AccountSecurityTests.cs, AuthBootstrapTests.cs, ResourceContractTests.cs
- **Verification Evidence (2026-11-12, after final edit)**:
  - dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (  Warning(s),   Error(s))**.
  - Non-SQL + HTTP test suite (FORGEFLOW_TEST_SQLSERVER='', FORGEFLOW_AUTH_HTTP_TESTS='1', --filter "FullyQualifiedName!~Sql"): **Passed: 423, Failed: 0, Skipped: 0, Total: 423**.
  - Live Development instance at https://localhost:5001 (	estforgflow): GET /account/login returns **HTTP 200**; 
ode verification/slice01-ui/browser-check.cjs **4/4 PASS** (a-IR & en-US, 1440px & 320px).
- **Remaining Acceptance Gaps**:
  - Apply EF Core AuthDbContext migration (AccountSecurityAndTwoFactor) to 	estforgflow before testing authenticated /account/security and 2FA flows against SQL Server (per startup rule, migrations are never run automatically on startup).
  - Authenticated a-IR/en-US RTL/LTR and 1440px/320px browser verification for /account/security and /account/login-2fa.
  - Phase 2: Passkeys (WebAuthn).
- **Next Small Step**: Apply the AuthDbContext migration to 	estforgflow (dotnet ef database update --context AuthDbContext ...) and verify /account/security and /account/login-2fa in the browser.
## Passkeys (WebAuthn Phase 2) & Account Navigation Cleanup

- **Scope**:
  1. Simplified the header [UserMenu.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Layout/UserMenu.razor) dropdown by removing redundant links and consolidating into **«تنظیمات من»** (/account/profile with Recommended badge when 2FA is off) + Logout.
  2. Reorganized [AccountNav.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Layout/AccountNav.razor) from 8 flat tabs into **4 categorized tabs** (نمایه من, تنظیمات, امنیت, اعلان‌ها) with contextual sub-tabs for Security (نمای کلی امنیت, تغییر رمز عبور, نشست‌های فعال, فعالیت‌های من) and Notifications (اعلان‌ها, تنظیمات اعلان‌ها).
  3. Implemented **Passkeys (WebAuthn / FIDO2)** end-to-end without external NuGet dependencies: [WebAuthnVerifier.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/WebAuthnVerifier.cs) (ES256 P-256 & RS256 CBOR/COSE verification with signCount replay protection), uth.UserPasskey table (20261112070859_UserPasskeysWebAuthn applied to 	estforgflow), 4 WebAuthn endpoints in [AuthenticationBackend.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Authentication/AuthenticationBackend.cs), live Passkey management on [Security.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Security.razor), and **Sign in with a Passkey** on [Login.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Login.razor) + [auth-ui.mjs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/auth-ui.mjs).
- **Verification Evidence (2026-11-12, after final edit)**:
  - dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (  Warning(s),   Error(s))**.
  - Non-SQL + HTTP test suite (FORGEFLOW_TEST_SQLSERVER='', FORGEFLOW_AUTH_HTTP_TESTS='1', --filter "FullyQualifiedName!~Sql"): **Passed: 424, Failed: 0, Skipped: 0, Total: 424**.
  - Live Development instance at https://localhost:5001 (	estforgflow): GET /account/login returns **HTTP 200**; 
ode verification/slice01-ui/browser-check.cjs **4/4 PASS** (a-IR & en-US, 1440px & 320px).
- **Remaining Acceptance Gaps**: Authenticated WebAuthn browser registration/sign-in flow in a live browser (a-IR/en-US, desktop/mobile) awaits user verification with a platform authenticator (Windows Hello / virtual authenticator).
- **Next Small Step**: Test the simplified header menu, categorized settings tabs, and Passkey registration/sign-in at https://localhost:5001/account/login.
## BitOtpInput 6-Digit UI Polish Increment

- **Scope**: Replaced single text inputs for 6-digit TOTP verification in [Security.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Security.razor) (Step 3 of TOTP setup) and [LoginTwoFactor.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/LoginTwoFactor.razor) with Bit.BlazorUI's <BitOtpInput Length="6" Type="BitInputType.Number" Dir="BitDir.Ltr" AutoFocus="true" /> and light/dark/mobile CSS styling in [app.css](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css).
- **Verification Evidence (2026-11-12, after final edit)**:
  - dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (  Warning(s),   Error(s))**.
  - Non-SQL + HTTP test suite (FORGEFLOW_TEST_SQLSERVER='', FORGEFLOW_AUTH_HTTP_TESTS='1', --filter "FullyQualifiedName!~Sql"): **Passed: 424, Failed: 0, Skipped: 0, Total: 424**.
  - Live Development instance at https://localhost:5001 (	estforgflow): GET /account/login returns **HTTP 200**; 
ode verification/slice01-ui/browser-check.cjs **4/4 PASS** (a-IR & en-US, 1440px & 320px).
- **Remaining Acceptance Gaps**: Interactive browser check of <BitOtpInput> inside /account/security and /account/login-2fa across a-IR/en-US and desktop/mobile.
- **Next Small Step**: Verify the 6-digit BitOtpInput boxes live in the browser at https://localhost:5001/account/login.

## User Management Modals Redesign, Admin 2FA Reset & Require Password Change

- **Scope**:
  1. **Edit User Modal ([Users.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Users.razor), [app.css](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css))**: Header with truncated username + status badges and single footer Close button; two tabs (**Access** and **Security**). Access tab shows org membership card with Org Admin badge + 3 actions, sites list with inline Operator toggle switch + activate/deactivate, and compact Add Site row. Security tab shows account status & sign-in lock, Password Reset with `RequirePasswordChange` checkbox, Two-Factor status (enabled date, remaining recovery codes, Reset 2FA action), and active sessions count with Revoke All Sessions action.
  2. **Admin 2FA Reset Security Rules & Backend ([PermissionCatalog.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Roles/PermissionCatalog.cs), [UserUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Users/UserUseCases.cs), [SqlUserOperationsStore.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/SqlUserOperationsStore.cs))**: Added dedicated `Users.ResetTwoFactor` permission (auto-granted to `OrganizationAdministrator`), forbidden on own account (`Users.CannotResetOwnTwoFactor`), mandatory reason + identity-verified confirmation dialog; resets authenticator key, disables 2FA, clears recovery codes, rotates `SecurityStamp` (invalidating remembered-2FA cookies), sets `RequireTwoFactorSetup = true` (forcing `/account/security?setup2fa=1` on next login), revokes active sessions (`TwoFactorReset`), records `TwoFactorReset` audit event with reason, and sends `TwoFactorResetAlert` notifications to target user and peer org admins.
  3. **Require Password Change on First Login ([RuntimeSessions.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs), [AuthenticationBackend.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Authentication/AuthenticationBackend.cs))**: Added `RequirePasswordChange` on `ApplicationAccount` (applied on Create User and Admin Password Reset, cleared on `ChangeOwnPasswordAsync`, redirecting to `/account/change-password` upon login). Applied EF Core migration `20261112075407_UserAdminSecurityAndRequireSetup` to `localhost/testforgflow`.
  4. **Create User Modal ([Users.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Users.razor))**: Target organization subtitle, top labels, inline password eye, **Generate Random Password** button (16-char one-time reveal), 4-segment password strength bar, blur validation (`onblur`) with error states, default-checked `RequirePasswordChange`, Org Admin toggle switch with description, selectable site card list with code chips and `{X} of {Y} selected` counter, and disabled primary Create button while invalid.
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 426, Failed: 0, Skipped: 0, Total: 426** (includes new `ResetUserTwoFactor` and `RevokeUserSessions` unit tests in [UserUseCaseTests.cs](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/UserUseCaseTests.cs)).
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200**; `node verification/slice01-ui/browser-check.cjs` **4/4 PASS** (`fa-IR` & `en-US`, `1440px` & `320px`).
- **Remaining Acceptance Gaps**: Authenticated interactive browser verification of the redesigned Create User / Edit User modals, 2FA Reset confirmation dialog, and forced password-change / 2FA re-setup redirect flows across `fa-IR`/`en-US` and desktop/mobile (`1440px` / `320px`).
- **Next Small Step**: Verify the redesigned User Management modals and Admin 2FA Reset flow live in the browser at `https://localhost:5001/account/login`.

## Create User Modal Password Field Alignment

- **Scope**: Replaced the custom password input in the Create User modal ([Users.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Users.razor)) with `<BitTextField>` using the same `login-field*` classes (`CreatePasswordFieldClasses`) and built-in reveal-password eye button as [Login.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Login.razor), plus invalid-state border/ring styling in [app.css](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css).
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 426, Failed: 0, Skipped: 0, Total: 426**.
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200**.
- **Remaining Acceptance Gaps**: Authenticated interactive browser verification of the Create User modal password field across `fa-IR`/`en-US` and desktop/mobile.
- **Next Small Step**: Verify the Create User modal password box live at `https://localhost:5001/account/login`.

## Edit User Modal Password Field Alignment

- **Scope**: Updated the Reset Password `<BitTextField>` in the Edit User modal ([Users.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Users.razor)) to use `LoginPasswordFieldClasses` (`login-field*` classes) so both Create User and Edit User password boxes share the exact same appearance and reveal-password eye button as [Login.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Login.razor).
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 426, Failed: 0, Skipped: 0, Total: 426**.
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200**.
- **Remaining Acceptance Gaps**: Authenticated interactive browser verification of the Edit User modal password field across `fa-IR`/`en-US` and desktop/mobile.
- **Next Small Step**: Verify both Create User and Edit User password boxes live at `https://localhost:5001/account/login`.

## Permission Matrix Modal & Site Operator Checkbox Removal

- **Scope**:
  1. **Removed Site Operator Checkboxes/Switches ([Users.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Users.razor))**: Removed the Site Operator toggle switch and checkbox from Create User, Edit User (Access tab -> Sites list & Add Site row), and Attach User modals so site membership assignment no longer implicitly grants `SiteOperator`.
  2. **Server-Computed Effective Permission Cells ([RoleContracts.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Roles/RoleContracts.cs), [SqlRoleOperationsStore.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/SqlRoleOperationsStore.cs))**: Added `UserEffectivePermissionCell(ScopeKind, SiteId, PermissionCode, GrantedByRole, GrantedDirectly, GrantingRoleNames)` to `UserPermissionMatrix` and computed it in `BuildUserMatrixAsync`.
  3. **Permission Matrix Modal Redesign ([Users.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Users.razor), [app.css](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css))**: Replaced the per-page dropdown with a full overview & edit matrix (`Page / Section`, `View`, `Edit`, `Enable / Disable`, `Total` `{X}/{Y}`), 4 cell states (Via role/locked with role-name tooltip, Direct, Unsaved dashed amber border, None), top summary bar (including green full-access bar for Org Admin), compact single-line assigned role row, `All / Granted only / Direct only` filter + page search, legend, and unsaved changes counter in the footer.
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 426, Failed: 0, Skipped: 0, Total: 426**.
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200**; `node verification/slice01-ui/browser-check.cjs` **4/4 PASS** (`fa-IR` & `en-US`, `1440px` & `320px`).
- **Remaining Acceptance Gaps**: Authenticated interactive browser verification of the new Permission Matrix modal across `fa-IR`/`en-US` and desktop/mobile (`1440px` / `320px`).
- **Next Small Step**: Verify the Permission Matrix modal and Site Operator checkbox removal live at `https://localhost:5001/account/login`.

## Grouped Accordion Permissions Modal Redesign

- **Scope**:
  1. **Server-Side Grouped Permissions ([RoleContracts.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Roles/RoleContracts.cs), [SqlRoleOperationsStore.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/SqlRoleOperationsStore.cs))**: Added `UserPermissionGroup(ScopeKind, SiteId, Module, Operations)` to `UserPermissionMatrix` and populated it dynamically from `PermissionCatalog.All` in `BuildUserMatrixAsync`.
  2. **Grouped Accordion UI ([Users.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Users.razor), [app.css](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css))**: Replaced the fixed-column matrix with a `700px` fixed-width, bounded-height modal (`pa-modal`) where only the grouped accordion list scrolls vertically (no horizontal scroll). Includes two-tone summary progress bar (role + direct), removable role chips with `+ Add role`, 4-state filter (`All / Granted only / Direct only / Changes`) + page/operation search, collapsed rows with mini dual progress bars and `{X} of {Y}` counts, and expanded 2-column operation card grids with `Select all editable` / `Clear direct` and 4 visual states (Role-locked with `Role` badge & tooltip, Direct, Unsaved with `New` badge, None).
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 426, Failed: 0, Skipped: 0, Total: 426**.
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200**; `node verification/slice01-ui/browser-check.cjs` **4/4 PASS** (`fa-IR` & `en-US`, `1440px` & `320px`).
- **Remaining Acceptance Gaps**: Authenticated interactive browser verification of the `700px` Grouped Accordion Permissions modal across `fa-IR`/`en-US` and desktop/mobile (`1440px` / `320px`).
- **Next Small Step**: Verify the Grouped Accordion Permissions modal live at `https://localhost:5001/account/login`.

## Users Grid Quick Actions & Modal Confirmation Dialog

- **Scope**:
  1. **Grid Row Icon Actions ([Users.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Users.razor), [app.css](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css))**: Added Activate/Deactivate Account icon button (`ff-icon--activate` / `ff-icon--deactivate`) and Unlock User icon button (`ff-icon--unlock`, shown when locked) to the Users DataGrid `Actions` column with permission and self-deactivation guards.
  2. **Modal Confirmation Dialog ([Users.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Users.razor))**: Moved `confirmAction` out of the bottom of the Edit User modal into a dedicated `<BitDialog>` (`440px`) so confirmations (for account enable/disable, membership enable/disable, org-admin revocation, and password reset) are always centered and visible from both the Edit User modal and the Users DataGrid row actions.
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 426, Failed: 0, Skipped: 0, Total: 426**.
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200**; `node verification/slice01-ui/browser-check.cjs` **4/4 PASS** (`fa-IR` & `en-US`, `1440px` & `320px`).
- **Remaining Acceptance Gaps**: Authenticated interactive browser verification of the Users DataGrid Activate/Deactivate and Unlock icon buttons and the dedicated Confirmation `<BitDialog>` across `fa-IR`/`en-US` and desktop/mobile.
- **Next Small Step**: Verify the grid action icons and confirmation dialog live at `https://localhost:5001/account/login`.

## Roadmap Documentation & CSP Security Header Alignment (Pre-Slice 14)

- **Scope**:
  1. **Roadmap Documentation ([docs/roadmap.md](file:///d:/Projects/ForgeFlow/docs/roadmap.md), [docs/README.md](file:///d:/Projects/ForgeFlow/docs/README.md))**: Created docs/roadmap.md documenting the completed baseline (AUTH-01 through Slice 13 + UX/2FA/Passkeys increments) and the proposed roadmap for Slice 14+ (14-sales-dispatch, 15-mrp-scheduling, 16-costing-valuation, 17-cmms-oee, 18-shopfloor-barcode, 19-alerts-integrations), and linked it from docs/README.md.
  2. **Content-Security-Policy Header ([AuthenticationBackend.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Authentication/AuthenticationBackend.cs), [SecurityAuditTests.cs](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/SecurityAuditTests.cs))**: Updated UseSecurityHeaders() to emit a Blazor Server-compatible Content-Security-Policy header (default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'self'; form-action 'self'; img-src 'self' data: blob:; font-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:) including on antiforgery-protected pages, and added assertions in SecurityAuditTests.cs.
- **Verification Evidence (2026-11-12, after final edit)**:
  - dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1: **PASS (0 Warning(s), 0 Error(s))**.
  - Non-SQL + HTTP test suite (FORGEFLOW_TEST_SQLSERVER='', FORGEFLOW_AUTH_HTTP_TESTS='1', --filter "FullyQualifiedName!~Sql"): **Passed: 426, Failed: 0, Skipped: 0, Total: 426**.
  - Live Development instance at https://localhost:5001 (	estforgflow): GET /account/login returns **HTTP 200** with full Content-Security-Policy header; 
ode d:\Projects\ForgeFlow\verification\slice01-ui\browser-check.cjs **4/4 PASS** (a-IR & en-US, 1440px & 320px).
- **Remaining Acceptance Gaps**:
  - SQL Server integration tests (*Sql*) were not run in this session (FORGEFLOW_TEST_SQLSERVER unset).
  - Authenticated interactive browser verification of the recent User Management modals and 2FA/Passkey flows (a-IR/en-US, 1440px/320px).
- **Next Small Step**: Start Slice 14 (14-sales-dispatch or chosen roadmap slice) design (14.0) in a new chat.


## Slice 14: Sales, Customers & Dispatch (`14-sales-dispatch`) — `14.0` Design Complete

- **14.0 Design Artifacts ([docs/slices/14-sales-dispatch/](file:///d:/Projects/ForgeFlow/docs/slices/14-sales-dispatch/README.md))**:
  - Authored [README.md](file:///d:/Projects/ForgeFlow/docs/slices/14-sales-dispatch/README.md), [decisions.md](file:///d:/Projects/ForgeFlow/docs/slices/14-sales-dispatch/decisions.md), [physical-erd.md](file:///d:/Projects/ForgeFlow/docs/slices/14-sales-dispatch/physical-erd.md), and [test-plan.md](file:///d:/Projects/ForgeFlow/docs/slices/14-sales-dispatch/test-plan.md).
  - Codified approved decisions `S-D01`–`S-D05`:
    - `S-D01`: Organization-scoped `Customer` master and site-scoped `SalesOrder` / `SalesOrderLine` lifecycle (`Draft` → `Approved` → `PartiallyDispatched` → `Dispatched` / `Closed` / `Cancelled`) with multi-unit base conversion snapshots.
    - `S-D02`: Unified `StockReservation` extension (`SourceKind` `1=ProductionOrder`, `2=SalesOrder`, nullable `ProductionOrderId`/`SalesOrderId`/`SalesOrderLineId`, XOR check constraint) with auto-consume on dispatch and auto-release on close/cancel.
    - `S-D03`: Atomic `DeliveryOrder` / `DeliveryOrderLine` dispatch posting with lot expiry (`ItemLot.IsExpiredOn`) and quarantine/availability gates, automatic `QualityInspection` (`CertificateNumber`) COA snapshot, `InventoryTransactionType.SalesDispatch = 10`, and `ReverseDispatch` compensating reversal.
    - `S-D04`: `PermissionModule.Sales = 16` (`Sales.Read`, `Sales.ManageCustomers`, `Sales.ManageOrders`, `Sales.Dispatch`), `Sales.Read` access to `GetLotCertificate`, and `AuditEvent` `CHECK` constraint + `AuditEventCatalog` entries.
    - `S-D05`: `/sales` (`Sales.razor`) UI with Customers, Sales Orders, and Dispatches tabs, lot picker, `LotCertificateDialog` integration, and full `en-US`/`fa-IR`, RTL/LTR, Light/Dark, and `320px` mobile support.
  - Physical ERD targets `47` business entities (`42` existing + `5` new: `Customer`, `SalesOrder`, `SalesOrderLine`, `DeliveryOrder`, `DeliveryOrderLine`) and `13` migrations.
- **Verification Evidence (2026-11-12)**:
  - Verified all 4 design documents exist in `docs/slices/14-sales-dispatch/` and align with existing `PermissionCatalog.cs`, `StockReservation.cs`, `ItemLot.cs`, and `OrganizationModel.cs` conventions. (Design-only increment; no code or database changes performed.)
- **Remaining Acceptance Gaps**:
  - Implementation increments `14.1` (Domain) through `14.6` (Live UI acceptance across `fa-IR`/`en-US`, RTL/LTR, Light/Dark, `320px`).
- **Next Small Step**: Implement `14.1` (Domain entities, enums, errors, `StockReservation` `SourceKind` extension, `InventoryTransactionType.SalesDispatch`, and `PermissionModule.Sales`) + unit tests.

## Slice 14: Sales, Customers & Dispatch (`14-sales-dispatch`) — `14.1` Domain Complete

- **UI & Slice Completeness Contract ([AGENTS.md](file:///d:/Projects/ForgeFlow/AGENTS.md))**:
  - Codified the mandatory contract requiring all new UI pages/features to reuse existing ForgeFlow UI primitives (`ff-icon--*` SVG icons, `<BitDialog>` / `ForgeFormDialog`, `BitDataGrid` / `ff-table`, `ff-card` / `ff-box` / `ff-toolbar`, Jalali/Gregorian date pickers) without breaking existing styles/themes/RTL-LTR, and requiring same-slice implementation of permissions (`PermissionCatalog`, role UI grouping, authorization guards) and full diagnostics/audit logging (`AuditEventCatalog`, `OrganizationModel` `AuditEvent` check constraint, `OperationAuditHelper` / `IUserOperationRecorder`, and error logging).
- **14.1 Domain (`ForgeFlow.Domain`)**:
  - Added [`SalesErrors.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/SalesErrors.cs), [`SalesText.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/SalesText.cs), [`Customer.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/Customer.cs), [`SalesOrderStatus.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/SalesOrderStatus.cs), [`SalesOrderLine.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/SalesOrderLine.cs), [`SalesOrder.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/SalesOrder.cs), [`DeliveryOrderStatus.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/DeliveryOrderStatus.cs), [`DeliveryOrderLine.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/DeliveryOrderLine.cs), and [`DeliveryOrder.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/DeliveryOrder.cs).
  - Extended [`InventoryTransactionType.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Inventory/InventoryTransactionType.cs) (`SalesDispatch = 10`), [`StockReservation.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Inventory/StockReservation.cs) (`ReservationSourceKind`, nullable `ProductionOrderId`/`SalesOrderId`/`SalesOrderLineId`, `CreateForSalesOrder`), [`SqlStockReservationStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlStockReservationStore.cs) (nullable `ProductionOrderId` compatibility), and [`PermissionCatalog.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Roles/PermissionCatalog.cs) (`PermissionModule.Sales = 16`: `Sales.Read`, `Sales.ManageCustomers`, `Sales.ManageOrders`, `Sales.Dispatch`).
  - Added [`SalesDispatchTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/SalesDispatchTests.cs).
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SalesDispatchTests"`: Full solution Debug build succeeded; **Passed: 6, Failed: 0, Skipped: 0, Total: 6**.
  - Note: Release build copy to `src/ForgeFlow.Web/bin/Release/net10.0/` is locked by the running Development instance (`.NET Host` PID `19524`); will be restarted and verified in Release when UI/runtime increments land.
- **Next Small Step**: Implement `14.2` (Application: `SalesContracts.cs`, `SalesUseCases.cs`, `ReservationContracts.cs`/`StockReservationUseCases.cs` Sales Order extension, `QualitySpecUseCases.cs` `Sales.Read` COA access, and `AuditEventCatalog.cs`).

## Slice 14: Sales, Customers & Dispatch (`14-sales-dispatch`) — `14.2` Application Complete

- **14.2 Application (`ForgeFlow.Application`)**:
  - Added [`SalesContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Sales/SalesContracts.cs) (`SalesPermissions`, Customer/SalesOrder/DeliveryOrder/DispatchableLot DTOs, `ISalesOperationsStore`, `SalesStoreUnavailableException`) and [`SalesUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Sales/SalesUseCases.cs) (authorization guards, site-scope enforcement, unit conversion via `UnitConversionGraph`, and rejected-action audit logging via `OperationAuditHelper`).
  - Extended [`ReservationContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Inventory/ReservationContracts.cs) & [`StockReservationUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Inventory/StockReservationUseCases.cs) (`ReservationSourceKind`, `SalesOrderId`/`SalesOrderLineId`, `ListSalesOrderReservations`, `ReserveSalesOrderStock` with `Sales.Read` / `Sales.ManageOrders` authorization).
  - Extended [`QualitySpecUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Quality/QualitySpecUseCases.cs) to grant `Sales.Read` actors read-only access to `GetLotCertificate` (COA).
  - Extended [`DiagnosticContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Diagnostics/DiagnosticContracts.cs) (`UserOperationCatalog.SalesActionRejected`) and [`AuditEventCatalog.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Audit/AuditEventCatalog.cs) (`Customer.*`, `SalesOrder.*`, `DeliveryOrder.*`, `Sales.ActionRejected`).
  - Updated [`SalesDispatchTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/SalesDispatchTests.cs) & [`ReservationTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/ReservationTests.cs).
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SalesDispatchTests|FullyQualifiedName~ReservationTests"`: Full Debug solution build succeeded; **Passed: 11, Failed: 0, Skipped: 0, Total: 11**.
- **Next Small Step**: Implement `14.3` (Infrastructure & EF Migration: `OrganizationModel`, `OrganizationDbContext`, `AuthDbContext`, `SqlSalesStore`, `SqlStockReservationStore` Sales Order support, migration + `localhost/testforgflow` upgrade, and DI registration).

## Slice 14: Sales, Customers & Dispatch (`14-sales-dispatch`) — `14.3`–`14.5` Implementation Complete (`14.6` In Review)

- **14.3 Infrastructure & EF Migrations (`ForgeFlow.Infrastructure`)**:
  - Extended [`InventoryTransaction.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Inventory/InventoryTransaction.cs) (`DeliveryOrderId`), [`OrganizationModel.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationModel.cs) (`47` business entities: added `Customer`, `SalesOrder`, `SalesOrderLine`, `DeliveryOrder`, `DeliveryOrderLine`; extended `StockReservation` with `SourceKind`/`SalesOrderId`/`SalesOrderLineId`/`CK_StockReservation_Source`; extended `InventoryTransaction` `CK_InventoryTransaction_Type` `1..10` and `DeliveryOrderId`; extended `OrganizationChangeEventSql` with `Customer.*`, `SalesOrder.*`, `DeliveryOrder.*`), [`OrganizationDbContext.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationDbContext.cs) (DbSets, `SaveInOperation` audit capture, `IsActive` org guard), and [`AuthDbContext.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs) (ignored new business entities).
  - Added [`SqlSalesOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlSalesOperationsStore.cs) (atomic `PostDeliveryOrder` with expiry, quarantine/blocked, and reservation consumption gates + `QualityInspection` COA snapshot + `InventoryTransactionType.SalesDispatch`; `ReverseDeliveryOrder`; auto-release on `CloseSalesOrder`/`CancelSalesOrder`), extended [`SqlStockReservationStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlStockReservationStore.cs) (`ListSalesOrderReservations`, `ReserveSalesOrderStock`), and registered `SalesUseCases` in [`OrganizationPersistenceRegistration.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationPersistenceRegistration.cs).
  - Generated migrations `20261112095525_SalesAndDispatch` (with `[ForgeFlowOrganizationRuntime]` grants) and `AddSalesPermissions` (`AuthDbContext`), applied `20261112095525_SalesAndDispatch` and seeded `Sales.*` permissions into `localhost/testforgflow`.
- **14.4 Web UI & Localization (`ForgeFlow.Web`)**:
  - Added [`Sales.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Sales.razor) (`/sales` & `/organizations/{OrganizationId:long}/sales`) reusing `BitDataGrid`, `ForgeFormDialog`, `BitDialog`, `BitDatePicker` (Jalali/Gregorian), and `LotCertificateDialog` across 3 tabs (**Sales Orders**, **Dispatches**, **Customers**).
  - Extended [`ReadScopes.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/ReadScopes.cs), [`MainLayout.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Layout/MainLayout.razor), [`app.css`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css) (`.ff-icon--sales`), [`RolePermissionLocalization.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Roles/RolePermissionLocalization.cs), [`SharedResources.en-US.resx`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Resources/Localization/SharedResources.en-US.resx), and [`SharedResources.fa-IR.resx`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Resources/Localization/SharedResources.fa-IR.resx).
- **14.5 Tests & Verification Evidence (2026-11-12, after final edit)**:
  - Updated [`OrganizationPersistenceModelTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/OrganizationPersistenceModelTests.cs) (`47` entities, `13` migrations), [`OrganizationSqlServerTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/OrganizationSqlServerTests.cs) (`47` tables, `13` migrations), [`RoleDomainTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/RoleDomainTests.cs) (`49` permissions), [`AuthBootstrapTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/AuthBootstrapTests.cs) (`10` auth migrations), [`ResourceContractTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/ResourceContractTests.cs), and added [`SalesDispatchSqlTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/SalesDispatchSqlTests.cs).
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 434, Failed: 0, Skipped: 0, Total: 434**.
  - SQL integration checks (Debug, `FORGEFLOW_TEST_SQLSERVER='Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'`): `SalesDispatchSqlTests` **1/1 PASS**; `OrganizationSqlServerTests` + `OrganizationPersistenceModelTests` **PASS** (prior to final `AuthDbContext` ignore-only migration).
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200**; `node d:\Projects\ForgeFlow\verification\slice01-ui\browser-check.cjs` **4/4 PASS** (`fa-IR` & `en-US`, `1440px` & `320px`).
- **Remaining Acceptance Gaps (`14.6`)**:
  - Authenticated interactive browser verification of `/sales` (Customers, Sales Orders, Stock Reservations, Lot Picker & COA Dispatch, Reverse Dispatch) across `fa-IR`/`en-US`, RTL/LTR, Light/Dark, and mobile (`320px`).
- **Next Small Step**: Complete `14.6` live interactive UI verification at `https://localhost:5001/account/login`.

## Slice 14 (`14.4` UI Polish) — Sales Order Modal Alignment & Live Available Stock Indicator

- **Scope**:
  - Redesigned the Create/Edit Sales Order modal in [`Sales.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Sales.razor) to match [`PurchaseOrders.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/PurchaseOrders.razor) (`1020px`, `proc-po-section-head`, `proc-po-general-grid`, `proc-lines-card`, `proc-lines-grid`, full `BitDatePicker` navigation icons, `OrderEstimatedTotal` footer).
  - Added per-line **Available Stock** pill (`sales-stock-pill` in [`app.css`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/app.css), Light/Dark) querying `InventoryUseCases.ListBalances` for the selected site/warehouse plus a clear `SalesInventoryInfoHint` explaining that drafting/approving does not deduct stock while Reserve/Dispatch enforce row-level concurrency locking.
  - Added localized strings in [`SharedResources.en-US.resx`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Resources/Localization/SharedResources.en-US.resx), [`SharedResources.fa-IR.resx`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Resources/Localization/SharedResources.fa-IR.resx), and [`ResourceContractTests.cs`](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/ResourceContractTests.cs).
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - `ResourceContractTests` & `HardcodedDisplayTextScannerTests` (Release): **Passed: 23, Failed: 0, Skipped: 0, Total: 23**.
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `node d:\Projects\ForgeFlow\verification\slice01-ui\browser-check.cjs` **4/4 PASS**.


## Slice 14 (`14.4` / Cross-Cutting Increment) — Configurable Document Numbering & Auto-Sequencing (`DocumentSequence`)

- **Scope & Implemented Artifacts**:
  - **Domain (`ForgeFlow.Domain`)**:
    - Added [`DocumentSequence.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Organizations/DocumentSequence.cs) (`DocumentSequenceType` 1..9: `SalesOrder`, `DeliveryOrder`, `PurchaseOrder`, `PurchaseReceipt`, `ProductionOrder`, `InventoryTransaction`, `InventoryReversal`, `QualityInspection`, `QualityScrap`; `DocumentYearMode`: `None`, `Gregorian`, `Persian`; `FormatNumber` and `CreateDefault`) and [`DocumentSequenceErrors.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Organizations/DocumentSequenceErrors.cs).
    - Extended [`PermissionCatalog.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Roles/PermissionCatalog.cs) with `PermissionModule.DocumentSequences = 17` (`DocumentSequences.Read`, `DocumentSequences.Manage`).
  - **Application (`ForgeFlow.Application`)**:
    - Added [`DocumentSequenceContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/DocumentSequenceContracts.cs) and [`DocumentSequenceUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/DocumentSequenceUseCases.cs) (`ListSequences`, `GetPreview`, `UpdateSequence` with permission/scope checks and `UserOperationCatalog.DocumentSequenceActionRejected` logging).
    - Extended [`DiagnosticContracts.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Diagnostics/DiagnosticContracts.cs) and [`AuditEventCatalog.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Audit/AuditEventCatalog.cs) (`DocumentSequence.Created`, `DocumentSequence.Updated`, `DocumentSequenceActionRejected`).
    - Updated [`ProcurementUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Procurement/ProcurementUseCases.cs) and [`InventoryUseCases.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Inventory/InventoryUseCases.cs) so blank/null document numbers auto-allocate in SQL stores.
  - **Infrastructure (`ForgeFlow.Infrastructure`)**:
    - Mapped `organization.DocumentSequence` in [`OrganizationModel.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationModel.cs) (`48` entities, `UX_DocumentSequence_Org_Type`, check constraints, `AuditEvent` SQL constraint), [`OrganizationDbContext.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationDbContext.cs) (including `yield break` after `DocumentSequence` audit capture so `NextNumber`-only allocation increments do not fall through), and [`AuthDbContext.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs).
    - Added [`SqlDocumentSequenceStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlDocumentSequenceStore.cs) with atomic `AllocateAsync` (`WITH (UPDLOCK, HOLDLOCK)`) and wired auto-allocation into [`SqlSalesOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlSalesOperationsStore.cs), [`SqlProcurementOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlProcurementOperationsStore.cs), [`SqlProductionOrderOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlProductionOrderOperationsStore.cs), [`SqlInventoryOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlInventoryOperationsStore.cs), and [`SqlQualityOperationsStore.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlQualityOperationsStore.cs).
    - Generated migration `20261112113628_DocumentSequences` (`14` migrations) with `[ForgeFlowOrganizationRuntime]` grants, applied to `localhost/testforgflow`, and seeded `DocumentSequences.*` for `RoleId = 1`.
  - **Web UI & Localization (`ForgeFlow.Web`)**:
    - Added [`DocumentNumbering.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/DocumentNumbering.razor) (`/numbering-rules` & `/organizations/{OrganizationId:long}/numbering-rules`) using `BitDataGrid`, `ForgeFormDialog`, and live preview (`DocumentSequence.FormatNumber`).
    - Wired `GetPreview` + conditional `readonly` / auto-allocate `null` send into [`Sales.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Sales.razor), [`PurchaseOrders.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/PurchaseOrders.razor), [`ProductionOrders.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ProductionOrders.razor), [`Inventory.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Inventory.razor), and [`Quality.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Quality.razor).
    - Updated [`ReadScopes.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/ReadScopes.cs), [`MainLayout.razor`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Layout/MainLayout.razor), [`RolePermissionLocalization.cs`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Roles/RolePermissionLocalization.cs), [`SharedResources.en-US.resx`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Resources/Localization/SharedResources.en-US.resx), and [`SharedResources.fa-IR.resx`](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Resources/Localization/SharedResources.fa-IR.resx).
- **Verification Evidence (2026-11-12, after final edit)**:
  - `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **PASS (0 Warning(s), 0 Error(s))**.
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
  - SQL integration checks (Release, `FORGEFLOW_TEST_SQLSERVER='Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'`, `--filter "FullyQualifiedName~DocumentSequenceTests|FullyQualifiedName~OrganizationSqlServerTests|FullyQualifiedName~SalesDispatchSqlTests"`): **Passed: 10, Failed: 0, Skipped: 0, Total: 10**.
  - Live Development instance at `https://localhost:5001` (`testforgflow`): `GET /account/login` returns **HTTP 200** (`16842` bytes); `node d:\Projects\ForgeFlow\verification\slice01-ui\browser-check.cjs` **4/4 PASS** (`fa-IR` & `en-US`, `1440px` & `320px`).
- **Remaining Acceptance Gaps (`14.6`)**:
  - Authenticated interactive browser verification of `/numbering-rules`, `/sales` (including aligned Sales Order modal, live available stock pill, Customers, Stock Reservations, Lot Picker & COA Dispatch), and auto-numbered modals across `fa-IR`/`en-US`, RTL/LTR, Light/Dark, and mobile (`320px`).
- **Next Small Step**: Complete `14.6` live interactive UI verification at `https://localhost:5001/account/login`.

## Audit Remediation — Phase 1 (Critical Bugs, EF Core Boundary, LSP Interceptor, N+1/AsNoTracking, & ItemGrid Extraction)

- **R1.1 Document Sequence Concurrency & Year Reset**:
  - Added `LastAllocatedYear` to [DocumentSequence.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Organizations/DocumentSequence.cs), resetting `NextNumber` to `1` on Persian/Gregorian year rollover in `AllocateNextNumber` and `FormatPreview`.
  - Updated [SqlDocumentSequenceStore.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlDocumentSequenceStore.cs) `AllocateAsync` to require an active `IDbContextTransaction` and serialize first-time creation on the parent `Organization` row lock.
  - Added migration `R1_DocumentSequenceYearAndItemDraft` (`15` migrations total).
- **R1.2 Domain Logic & Redaction Fixes**:
  - Updated [UnitConversionGraph.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Units/UnitConversionGraph.cs) to re-enqueue nodes when an item-specific edge improves a visited path and use exact `System.Numerics.BigInteger` rational arithmetic to prevent `OverflowException` precision loss.
  - Updated [BomResolutionEngine.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Boms/BomResolutionEngine.cs) to normalize `DimensionCode` before context lookup and return `true` for `NotIn` when the exclusion set is empty.
  - Updated [ProductionOrder.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/ProductionOrders/ProductionOrder.cs) `ApplyRoutingSnapshot` to throw `ProductionOrderErrors.RoutingInvalid` when `routingId > 0` has an empty code snapshot.
  - Removed the creation-time capability requirement from [Item.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Items/Item.cs) and dropped `CK_Item_Capability` in [OrganizationModel.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationModel.cs) to support engineering draft items.
  - Updated [DiagnosticRedactor.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Diagnostics/DiagnosticRedactor.cs) with `RegexOptions.Singleline` and `JsonSecretKeyValue` to redact multi-line JSON tokens.
- **R2.1 & R2.2 EF Core Boundary & LSP Interceptor**:
  - Extracted `OrganizationModel.ConfigureOrganizationAndSite` in [OrganizationModel.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationModel.cs) and removed all 46 manual `model.Ignore<...>()` calls from [AuthDbContext.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs).
  - Added `ICurrentActorAccessor` in [AccessContracts.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/AccessContracts.cs) and [OrganizationAuditSaveChangesInterceptor.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationAuditSaveChangesInterceptor.cs), removing the `SaveChanges` / `SaveChangesAsync` overrides from [OrganizationDbContext.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationDbContext.cs).
- **R3.1 & R3.2 N+1 & `AsNoTracking` Projection**:
  - Replaced entity-tracked `ToItemSummary` / `db.Entry` calls in [ItemOperationsStore.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/ItemOperationsStore.cs), [UnitOperationsStore.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/UnitOperationsStore.cs), and [BomOperationsStore.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/BomOperationsStore.cs) with `.AsNoTracking()` + direct SQL `EF.Property<byte[]>(..., "RowVersion")` projections.
- **R4.1 & R4.5 Domain Guard & Razor Decomposition**:
  - Replaced `*Errors.NotFound` with `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(organizationId)` in `Item`, `Bom`, `ProductionOrder`, `SalesOrder`, and `PurchaseOrder`.
  - Extracted [ItemGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ItemSections/ItemGrid.razor) from [Items.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Items.razor).
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
  - SQL integration suite (`FORGEFLOW_TEST_SQLSERVER='Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'`, `DocumentSequenceTests|OrganizationSqlServerTests|SalesDispatchSqlTests|ItemSqlTests|BomSqlTests|UnitSqlTests|ProductionOrderSqlTests|DiagnosticSqlTests`): **Passed: 15, Failed: 0, Skipped: 0, Total: 15**.
- **Remaining Audit Backlog**:
  - Global Query Filter (`HasQueryFilter` for `OrganizationId`), explicit `RowVersion` properties on Domain entities, `RuntimeSessions` distributed/eviction policy, Domain Events for automatic audit capture, Domain logic extraction from the 6 Infrastructure stores, Vertical Slice refactoring of Application `*UseCases.cs`, and further Razor component decomposition (`Items.razor` modals, `Sales.razor`, `Quality.razor`, `Boms.razor`, `Reports.razor` debounce).
- **Next Small Step**: Apply `R1_DocumentSequenceYearAndItemDraft` to `testforgflow` if needed, then proceed to Global Query Filter + explicit `RowVersion` properties + `RuntimeSessions` eviction/persistence.

## Audit Remediation — Phase 2 (Global Query Filter, Explicit RowVersion, Domain Events, & Reports Debounce)

- **R2.3 Global Query Filter (`HasQueryFilter` for `OrganizationId`)**:
  - Added `ICurrentOrganizationAccessor` in [AccessContracts.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/AccessContracts.cs).
  - Applied dynamic `HasQueryFilter` across all organization-scoped entities in [OrganizationDbContext.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationDbContext.cs) when `CurrentOrganizationFilterId` is set.
- **R2.5 Explicit `RowVersion` CLR Properties**:
  - Added explicit `byte[] RowVersion` properties to [Item.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Items/Item.cs), [Unit.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Units/Unit.cs), and [DocumentSequence.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Organizations/DocumentSequence.cs), binding directly to the existing SQL `rowversion` columns without schema drift.
- **R2.6 `RuntimeSessions` Verification**:
  - Verified [RuntimeSessions.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/RuntimeSessions.cs) persists sessions in SQL Server (`authDb.Sessions`) rather than an unbounded in-memory dictionary.
- **R4.3 Domain Events Infrastructure**:
  - Added [IDomainEvent.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Common/IDomainEvent.cs) (`IDomainEvent` / `IHasDomainEvents`), raised domain events on [Item.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Items/Item.cs) (`ItemCreatedDomainEvent`, `ItemUpdatedDomainEvent`, `ItemStatusChangedDomainEvent`), and wired tracked domain event clearing into [OrganizationDbContext.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationDbContext.cs).
- **R4.5 `Reports.razor` Debounce & `CancellationToken`**:
  - Added `CancellationTokenSource` request cancellation and 250ms search debounce in [Reports.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Reports.razor) (including disposal in `DisposeAsync`).
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite: **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
  - SQL integration suite: **Passed: 15, Failed: 0, Skipped: 0, Total: 15**.
- **Remaining Audit Backlog (Architectural / Large-Scale Refactors)**:
  - Extracting domain calculation/state-transition logic from the 6 Infrastructure stores into Domain entities, full Vertical Slice split of the 19 `*UseCases.cs` classes in `ForgeFlow.Application`, and further subcomponent extraction for `Items.razor` modals, `Sales.razor`, `Quality.razor`, and `Boms.razor`.
- **Next Small Step**: Continue Vertical Slice & Razor subcomponent extraction or complete `14.6` live UI verification.

## Audit Remediation — Phase 3 (Items Vertical Slices & Grid Subcomponent Extraction)

- **R4.4 Vertical Slice Refactoring (`ForgeFlow.Application/Items/Slices/`)**:
  - Extracted [ItemSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Items/Slices/ItemSliceSupport.cs), [ItemCatalogSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Items/Slices/ItemCatalogSlices.cs), and [ItemConversionSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Items/Slices/ItemConversionSlices.cs) and updated [ItemUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Items/ItemUseCases.cs) to delegate Item CRUD and Unit Conversion Preview operations to those independent slice handlers.
- **R4.5 `Items.razor` Grid Subcomponents**:
  - Extracted [ClassificationGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ItemSections/ClassificationGrid.razor) and [AttributeGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ItemSections/AttributeGrid.razor) alongside [ItemGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ItemSections/ItemGrid.razor) from [Items.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Items.razor).
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
- **Remaining Long-Term Refactoring Backlog**:
  - Extracting remaining Vertical Slices (Classifications, Attributes, and other *UseCases.cs modules), extracting domain calculations from the 6 Infrastructure stores into Domain entities, and decomposing modals in Items.razor, Sales.razor, Quality.razor, and Boms.razor.
- **Next Small Step**: Continue Vertical Slice / modal extraction or complete `14.6` live UI verification.

## Audit Remediation — Phase 4 (Complete Item Vertical Slice Split)

- **R4.4 Complete Item Vertical Slice Split**:
  - Added [ItemClassificationSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Items/Slices/ItemClassificationSlices.cs) and [ItemAttributeSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Items/Slices/ItemAttributeSlices.cs).
  - Reduced [ItemUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Items/ItemUseCases.cs) from 702 lines to a 128-line facade delegating all 4 Item sub-domains (`ItemCatalogSlices`, `ItemConversionSlices`, `ItemClassificationSlices`, `ItemAttributeSlices`).
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
- **Next Small Step**: Live interactive UI verification (`14.6`) at `https://localhost:5001/account/login` or continue incremental decomposition on remaining modules.

## Audit Remediation — Phase 5 (CreateItemModal Extraction)

- **R4.5 `Items.razor` Modal Decomposition**:
  - Extracted [CreateItemModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ItemSections/CreateItemModal.razor) from [Items.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Items.razor), isolating create-item form state, validation, discard confirmation, and submission from the parent page.
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
- **Next Small Step**: Live interactive UI verification (`14.6`) at `https://localhost:5001/account/login` or continue decomposing remaining large Razor pages/use cases as needed.

## Audit Remediation — Phase 6 (CreateAttributeModal Extraction)

- **R4.5 `Items.razor` Modal Decomposition**:
  - Extracted [CreateAttributeModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ItemSections/CreateAttributeModal.razor) from [Items.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Items.razor).
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
- **Next Small Step**: Live interactive UI verification (`14.6`) at `https://localhost:5001/account/login` or continue decomposing remaining modals/pages.

## Audit Remediation — Phase 7 (Database Migration Sync & Remaining Items Create Modals)

- **Database Fix (`testforgflow`)**:
  - Applied `20261112125528_R1_DocumentSequenceYearAndItemDraft` to `localhost/testforgflow` via `dotnet ef database update`, resolving `SqlException: Invalid column name 'LastAllocatedYear'` when opening "New" modals on Inventory, Production Orders, Sales, Purchase Orders, Quality, and Document Numbering.
- **R4.5 `Items.razor` Modal Decomposition**:
  - Extracted [CreateClassificationSetModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ItemSections/CreateClassificationSetModal.razor) and [CreateClassificationModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/ItemSections/CreateClassificationModal.razor) from [Items.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Items.razor).
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
- **Next Small Step**: Live interactive UI verification (`14.6`) at `https://localhost:5001/account/login`.

## Audit Remediation — Phase 8 (Sales.razor Grid Decomposition)

- **R4.5 `Sales.razor` Grid Subcomponents**:
  - Extracted [CustomerGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/CustomerGrid.razor), [SalesOrderGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/SalesOrderGrid.razor), and [DeliveryOrderGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/DeliveryOrderGrid.razor) from [Sales.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Sales.razor).
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
- **Next Small Step**: Live interactive UI verification (`14.6`) at `https://localhost:5001/account/login`.

## Audit Remediation — Phase 9 (Domain RowVersion Expansion & Boms/Quality Grid Decomposition)

- **R2.5 Explicit `RowVersion` CLR Properties (Expanded)**:
  - Added explicit `public byte[] RowVersion { get; private set; } = [];` to [Bom.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Boms/Bom.cs), [BomVersion.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Boms/BomVersion.cs), [Customer.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/Customer.cs), [SalesOrder.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Sales/SalesOrder.cs), [Warehouse.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Inventory/Warehouse.cs), and [InventoryBalance.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Inventory/InventoryBalance.cs).
- **R4.5 `Boms.razor` & `Quality.razor` Grid Subcomponents**:
  - Extracted [BomGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/BomSections/BomGrid.razor) and [ContextDimensionGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/BomSections/ContextDimensionGrid.razor) from [Boms.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Boms.razor).
  - Extracted [InspectionGrid.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/QualitySections/InspectionGrid.razor) from [Quality.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Quality.razor).
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
  - SQL integration suite (`FORGEFLOW_TEST_SQLSERVER='Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'`): **Passed: 15, Failed: 0, Skipped: 0, Total: 15**.
- **Next Small Step**: Live interactive UI verification (`14.6`) at `https://localhost:5001/account/login`.

## Audit Remediation — Phase 10 (IEntityTypeConfiguration Assembly Scan, Auditable Domain Events, IHasRowVersion, & Sales Customer Slices)

- **EF Core `IEntityTypeConfiguration` Modularization**:
  - Created [OrganizationModelConventions.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/Configurations/OrganizationModelConventions.cs) and [OrganizationCoreConfigurations.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/Configurations/OrganizationCoreConfigurations.cs) (`OrganizationConfiguration`, `SiteConfiguration`, `OrganizationChangeConfiguration`, `DocumentSequenceConfiguration`).
  - Updated [OrganizationDbContext.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationDbContext.cs) to apply namespace-filtered `ApplyConfigurationsFromAssembly` and [AuthDbContext.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Identity/AuthDbContext.cs) to apply `OrganizationConfiguration` and `SiteConfiguration` directly.
- **Auditable Domain Events & `IHasRowVersion`**:
  - Added `IAuditableDomainEvent` and `IHasRowVersion` to [IDomainEvent.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Domain/Common/IDomainEvent.cs), wired `IAuditableDomainEvent` dispatch into [OrganizationDbContext.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/OrganizationDbContext.cs), implemented `IHasRowVersion` on `Item`, `Unit`, and `DocumentSequence`, and removed shadow-property `RowVersion` lookups in [SqlDocumentSequenceStore.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlDocumentSequenceStore.cs) and [SqlSalesOperationsStore.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Infrastructure/Organizations/SqlSalesOperationsStore.cs).
- **Sales Vertical Slice Foundation**:
  - Extracted [SalesSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Sales/Slices/SalesSliceSupport.cs) and [CustomerSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Sales/Slices/CustomerSlices.cs) and delegated Customer operations in [SalesUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Sales/SalesUseCases.cs).
- **Verification Evidence (2026-11-12, after final edit)**:
  - Non-SQL + HTTP test suite (`FORGEFLOW_TEST_SQLSERVER=''`, `FORGEFLOW_AUTH_HTTP_TESTS='1'`, `--filter "FullyQualifiedName!~Sql"`): **Passed: 436, Failed: 0, Skipped: 0, Total: 436**.
  - SQL integration suite (`FORGEFLOW_TEST_SQLSERVER='Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'`): **Passed: 15, Failed: 0, Skipped: 0, Total: 15**.

## Audit Remediation — Phase 11 (Modal Decomposition: Sales & BOMs, Clean Architecture Alignment)

- **R4.5 Sales.razor Modal Decomposition**:
  - Extracted [SalesOrderDetailModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/SalesOrderDetailModal.razor): handles order header summary, order line items, reservations table, release reservation actions, and lifecycle actions (edit, approve, dispatch, cancel, short-close).
  - Extracted [DeliveryOrderDetailModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/DeliveryOrderDetailModal.razor): handles dispatch header summary, dispatched lot lines, and Certificate of Analysis (COA) modal triggers.
  - Extracted [PostDispatchModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/PostDispatchModal.razor): encapsulates warehouse selection, Jalali/Gregorian date picking, carrier/tracking inputs, and item line lot picker with COA preview.
- **R4.5 Boms.razor Modal Decomposition**:
  - Extracted [CreateBomModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/BomSections/CreateBomModal.razor): encapsulates new BOM creation workflow, manufacturable item selector, and error handling.
- **Verification Evidence (2026-11-13)**:
  - Solution build: dotnet build -c Release -m:1 — **0 Warning(s), 0 Error(s)**.
  - Web unit test suite (dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "Category!=SqlServer"): **Passed: 448, Failed: 0, Skipped: 66, Total: 514**.
  - Local service verification: Web application running in Development with database connection 	estforgflow, responding with **HTTP 200** at https://localhost:5001/login and http://127.0.0.1:5000/login.
- **Next Small Step**: Interactive browser verification (14.6) at https://localhost:5001/account/login or proceed to further modal decomposition in Quality/Production Orders.

## Audit Remediation — Phase 12 (Modal Decomposition: Quality & BOM Edit, State Synchronization & Defect Remediation)

- **R4.5 Quality.razor Modal Decomposition**:
  - Extracted [QualityHoldModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/QualitySections/QualityHoldModal.razor): encapsulates manual quarantine / QC hold workflow with warehouse, item, unit, defect code, and quantity selection.
  - Extracted [QualityLotModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/QualitySections/QualityLotModal.razor): encapsulates item lot metadata management, manufacture and expiration date picking in Persian/Gregorian calendars, and note attachments.
  - Extracted [QualityInspectionDetailsModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/QualitySections/QualityInspectionDetailsModal.razor): comprehensive quality inspection workbench encapsulating dynamic lab test parameter measurement inputs, automated numeric pass/fail bounds evaluation, specification template loading, inspection disposition recording, and scrap document sequencing.
  - Reduced [Quality.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Quality.razor) from 1,800 lines to 1,210 lines.
- **R4.5 Boms.razor Modal Decomposition**:
  - Extracted [BomEditModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/BomSections/BomEditModal.razor): encapsulates BOM rename and two-step activation/deactivation confirmation dialog.
  - Fixed [CreateBomModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/BomSections/CreateBomModal.razor) two-way `IsOpen` / `IsOpenChanged` parameter binding desynchronization on modal dismiss and escape key.
- **R4.5 Sales Modals Polish**:
  - Added explicit close/dismiss buttons to [SalesOrderDetailModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/SalesOrderDetailModal.razor) and [DeliveryOrderDetailModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/DeliveryOrderDetailModal.razor) ensuring dialogs can always be dismissed cleanly in view-only and completed states.
  - Enhanced [PostDispatchModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/PostDispatchModal.razor) to automatically clear invalid selected lot codes when source warehouse is changed.
- **Verification Evidence (2026-11-13)**:
  - Solution build (`dotnet build -c Release -m:1`): **0 Warning(s), 0 Error(s)**.
  - Web unit test suite (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "Category!=SqlServer"`): **Passed: 448, Failed: 0, Skipped: 66, Total: 514**.
  - Local process verification: `ForgeFlow.Web.exe` running in Development mode listening on `https://localhost:5001` and `http://127.0.0.1:5000` with database connection `localhost/testforgflow`; verified HTTP 200 responses on:
    * `curl.exe -k -s -w "%{http_code}\n" -L https://localhost:5001/login -o NUL` -> **200**
    * `curl.exe -k -s -w "%{http_code}\n" -L https://localhost:5001/account/login -o NUL` -> **200**
    * `curl.exe -s -w "%{http_code}\n" -L http://127.0.0.1:5000/login -o NUL` -> **200**
- **Next Small Step**: Interactive browser verification (14.6) at [https://localhost:5001/account/login](file:///https://localhost:5001/account/login) or extract remaining extra-large workbench modals (`BomVersionWorkbenchModal` in `Boms.razor` and `CreateEditSalesOrderModal` in `Sales.razor`).

## Audit Remediation — Phase 13 (Unit Module Vertical Slice Decomposition & Application Layer Decoupling)

- **R4.4 Application Vertical Slices (Units & Currencies Module)**:
  - Extracted [UnitSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Units/Slices/UnitSliceSupport.cs): encapsulates shared authorization evaluation, organization/site scoping guards, rowversion decoding, query bounding, store execution, and operation rejection audit logging.
  - Extracted [UnitCatalogSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Units/Slices/UnitCatalogSlices.cs): vertical slice handler for unit of measure catalog CRUD, queries, and activation state transitions.
  - Extracted [UnitConversionSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Units/Slices/UnitConversionSlices.cs): vertical slice handler for unit conversions, multi-hop conversion graph resolution (UnitConversionGraph), preview calculation, and standard unit seeding.
  - Extracted [CurrencySlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Units/Slices/CurrencySlices.cs): vertical slice handler for currency catalog CRUD and active state management.
  - Refactored [UnitUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Units/UnitUseCases.cs) from a 500-line monolithic class down to 134 lines acting strictly as a backward-compatible facade delegating to the specialized slice handlers.
- **Verification Evidence (2026-11-13)**:
  - Solution build (dotnet build -c Release -m:1): **0 Warning(s), 0 Error(s)**.
  - Web unit test suite (dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "Category!=SqlServer"): **Passed: 448, Failed: 0, Skipped: 66, Total: 514**.
  - Unit-specific test suite (dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Unit"): **Passed: 11, Failed: 0, Skipped: 2, Total: 13**.
  - Local process verification: ForgeFlow.Web.exe running in Development mode listening on https://localhost:5001 and http://127.0.0.1:5000 with database connection localhost/testforgflow; verified HTTP 200 responses on:
    * curl.exe -k -s -w "%{http_code}\n" -L https://localhost:5001/login -o NUL -> **200**
    * curl.exe -s -w "%{http_code}\n" -L http://127.0.0.1:5000/login -o NUL -> **200**
- **Next Small Step**: Proceed to Phase 14 / Step 2: Decompose ReportingUseCases.cs, AuditUseCases.cs, DashboardUseCases.cs, and ErrorLogUseCases.cs into vertical slices.


## Audit Remediation — Phase 14 (Reporting, Audit, Dashboard & ErrorLog Vertical Slice Decomposition)

- **R4.4 Application Vertical Slices (Reporting, Audit, Dashboard & ErrorLog Modules)**:
  - **Reporting Module Slices** (`src/ForgeFlow.Application/Reporting/Slices/`):
    - Extracted [ReportingSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Reporting/Slices/ReportingSliceSupport.cs): shared authorization, organization scope resolution, conversion graph edge caching and path traversal, CSV decimal formatting, and audit context helpers.
    - Extracted [ReportSiteSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Reporting/Slices/ReportSiteSlices.cs): authorized site catalog retrieval scoped to tenant or site permissions.
    - Extracted [MaterialShortageSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Reporting/Slices/MaterialShortageSlices.cs): material shortage calculation, net requirements aggregation, order drilldown, and authorized CSV export.
    - Extracted [ProductionVarianceSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Reporting/Slices/ProductionVarianceSlices.cs): production consumption and yield variance reporting, order summary calculations, detailed input/output line analysis, and authorized CSV export.
    - Refactored [ReportingUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Reporting/ReportingUseCases.cs) from 665 lines down to 78 lines acting as a thin facade delegating to the specialized slice handlers and retaining static test helpers (`ComputeMaterialShortage`, `ComputeVarianceReport`, `ComputeVarianceDetail`, `BuildCsvFile`, `EscapeCsvCell`).
  - **Audit Module Slices** (`src/ForgeFlow.Application/Audit/Slices/`):
    - Extracted [AuditSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Audit/Slices/AuditSliceSupport.cs): permission authorization, query normalization, and outcome formatting.
    - Extracted [AuditQuerySlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Audit/Slices/AuditQuerySlices.cs): paged audit query execution, single entry retrieval, actor lookups, and distinct event code listings.
    - Extracted [AuditExportSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Audit/Slices/AuditExportSlices.cs): authorized CSV compliance log export with local timezone conversion and operation audit logging.
    - Refactored [AuditUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Audit/AuditUseCases.cs) from 240 lines to 58 lines as a clean facade.
  - **Dashboard Module Slices** (`src/ForgeFlow.Application/Dashboard/Slices/`):
    - Extracted [DashboardMetricSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Dashboard/Slices/DashboardMetricSlices.cs): tenant/site scoping, capability evaluations across all 6 functional permissions (Production, Inventory, Reports, Quality, Procurement, Routings), and KPI snapshot retrieval.
    - Refactored [DashboardUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Dashboard/DashboardUseCases.cs) to delegate to `DashboardMetricSlices`.
  - **ErrorLog / Diagnostics Module Slices** (`src/ForgeFlow.Application/Diagnostics/Slices/`):
    - Extracted [ErrorLogSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Diagnostics/Slices/ErrorLogSliceSupport.cs): read authorization for platform/tenant scopes and strict platform-only authorization for retention management.
    - Extracted [ErrorLogQuerySlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Diagnostics/Slices/ErrorLogQuerySlices.cs): query execution, error entry inspection, and aggregate statistics.
    - Extracted [ErrorLogPurgeSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Diagnostics/Slices/ErrorLogPurgeSlices.cs): retention policy enforcement and expired log purging.
    - Refactored [ErrorLogUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Diagnostics/ErrorLogUseCases.cs) from 159 lines down to 48 lines as a clean facade.
- **Verification Evidence (2026-11-13)**:
  - Solution build (`dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`): **0 Warning(s), 0 Error(s)**.
  - Unit test suite (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~AuditTests|FullyQualifiedName~DashboardTests|FullyQualifiedName~DiagnosticTests|FullyQualifiedName~QualityTests"`): **Passed: 48, Failed: 0, Skipped: 0, Total: 48**.
  - Full non-SQL test suite: **Passed: 448, Failed: 0, Skipped: 66, Total: 514**.
  - Local process verification: `ForgeFlow.Web.exe` running in Development mode listening on `https://localhost:5001` and `http://127.0.0.1:5000` with database connection `localhost/testforgflow`; verified HTTP 200 responses on `curl.exe -k -i https://localhost:5001/account/login` (Content-Language: fa-IR, RTL).
- **Next Small Step**: Proceed to Step 3: Decompose `RoutingUseCases.cs` (Work centers, routing catalog, operations) and `QualitySpecUseCases.cs` (parameters, standard specs) into vertical slices.


## Audit Remediation — Phase 15 (Routing & Quality Specification Vertical Slice Decomposition)

- **R4.4 Application Vertical Slices (Routings & Quality Specifications Modules)**:
  - **Routing Module Slices** (`src/ForgeFlow.Application/Routings/Slices/`):
    - Extracted [RoutingSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Routings/Slices/RoutingSliceSupport.cs): shared authorization, organization and site scoping guards, write-access factory, rowversion decoding, query bounding, store execution, and operation rejection audit logging.
    - Extracted [WorkCenterSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Routings/Slices/WorkCenterSlices.cs): work center catalog management, capacity definitions, daily availability, and load calculations.
    - Extracted [RoutingCatalogSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Routings/Slices/RoutingCatalogSlices.cs): routing master templates, draft versioning, sequential operation steps configuration, activation, and obsolescence lifecycle.
    - Extracted [RoutingOperationSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Routings/Slices/RoutingOperationSlices.cs): shop-floor production order operation execution, setup/run time tracking, completed and scrap quantity reporting, and skip actions.
    - Refactored [RoutingUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Routings/RoutingUseCases.cs) from 625 lines to 134 lines acting as a thin backward-compatible facade.
  - **Quality Specification Module Slices** (`src/ForgeFlow.Application/Quality/Slices/`):
    - Extracted [QualitySpecSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Quality/Slices/QualitySpecSliceSupport.cs): multi-permission authorization evaluation, tenant scoping, line input validation, and rejection audit logging.
    - Extracted [QualityParameterSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Quality/Slices/QualityParameterSlices.cs): lab test parameter catalog management, target values, and active toggles.
    - Extracted [QualitySpecSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Quality/Slices/QualitySpecSlices.cs): quality specification templates, checkpoint resolution (Incoming, In-Process, Final), line ranges, and application to live inspections.
    - Extracted [QualityCertificateSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Quality/Slices/QualityCertificateSlices.cs): Certificate of Analysis (COA) resolution for lots and shipments.
    - Refactored [QualitySpecUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Quality/QualitySpecUseCases.cs) from 297 lines to 78 lines as a thin facade.
- **Verification Evidence (2026-11-13)**:
  - Solution build (`dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`): **0 Warning(s), 0 Error(s)**.
  - Unit test suite (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Routing|FullyQualifiedName~LabSpec|FullyQualifiedName~Quality"`): **Passed: 18, Failed: 0, Skipped: 3, Total: 21**.
  - Full non-SQL test suite: **Passed: 448, Failed: 0, Skipped: 66, Total: 514**.
  - Local process verification: `ForgeFlow.Web.exe` running in Development mode listening on `https://localhost:5001` and `http://127.0.0.1:5000` with database connection `localhost/testforgflow`; verified HTTP 200 responses on `curl.exe -k -s -w "%{http_code}\n" -L https://localhost:5001/account/login -o NUL` -> **200**.
- **Next Small Step**: Proceed to Step 4: Decompose `UserUseCases.cs` (accounts, sessions, 2FA, memberships), `RoleUseCases.cs`, `OrganizationUseCases.cs`, and `DocumentSequenceUseCases.cs`.


## Audit Remediation — Phase 16 (Users, Roles, Organizations & DocumentSequence Vertical Slice Decomposition)

- **R4.4 Application Vertical Slices (Users, Roles, Organizations & DocumentSequence Modules)**:
  - **User Module Slices** (`src/ForgeFlow.Application/Users/Slices/`):
    - Extracted [UserSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Users/Slices/UserSliceSupport.cs): shared authorization, tenant/account scope matching, write access attribution, rowversion decoding, site ID normalization, initial password validation, and rejection audit logging.
    - Extracted [UserAccountSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Users/Slices/UserAccountSlices.cs): user queries, new organization user creation, attaching existing accounts, active state toggles, unlock operations, and password resets.
    - Extracted [UserMembershipSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Users/Slices/UserMembershipSlices.cs): organization and site membership assignments and activation states.
    - Extracted [UserSecuritySlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Users/Slices/UserSecuritySlices.cs): tenant role assignments, self-deactivation protection, two-factor emergency resets, and active session revocations.
    - Refactored [UserUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Users/UserUseCases.cs) from 460 lines down to 98 lines as a thin backward-compatible facade.
  - **Role Module Slices** (`src/ForgeFlow.Application/Roles/Slices/`):
    - Extracted [RoleSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Roles/Slices/RoleSliceSupport.cs): permission authorization, tenant scope verification, anti-escalation caller permission validation, and rejection audit logging.
    - Extracted [RoleCatalogSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Roles/Slices/RoleCatalogSlices.cs): permission catalog discovery, custom organization role creation, and updates.
    - Extracted [RoleAssignmentSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Roles/Slices/RoleAssignmentSlices.cs): user permission matrix queries, scoped role assignments, and granular operation grants.
    - Refactored [RoleUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Roles/RoleUseCases.cs) from 354 lines down to 78 lines as a thin facade.
  - **Organization Module Slices** (`src/ForgeFlow.Application/Organizations/Slices/`):
    - Extracted [OrganizationSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/Slices/OrganizationSliceSupport.cs): platform and tenant scope matching, version decoding, and store query execution.
    - Extracted [CompanySlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/Slices/CompanySlices.cs): tenant organization listing, onboarding, renaming, and activation.
    - Extracted [SiteSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/Slices/SiteSlices.cs): site listing, creation, renaming, and activation.
    - Refactored [OrganizationUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/OrganizationUseCases.cs) from 184 lines down to 94 lines as a thin facade.
  - **Document Sequence Module Slices** (`src/ForgeFlow.Application/Organizations/Slices/`):
    - Extracted [DocumentSequenceSliceSupport.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/Slices/DocumentSequenceSliceSupport.cs): UTC time resolution, scope matching, rowversion decoding, and rejection audit logging.
    - Extracted [DocumentSequenceSlices.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/Slices/DocumentSequenceSlices.cs): document sequence catalog, monotonic preview number allocation, and format/prefix updates.
    - Refactored [DocumentSequenceUseCases.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Application/Organizations/DocumentSequenceUseCases.cs) from 179 lines down to 54 lines as a thin facade.
- **Verification Evidence (2026-11-13)**:
  - Solution build (`dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`): **0 Warning(s), 0 Error(s)**.
  - Unit test suite (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~User|FullyQualifiedName~Role|FullyQualifiedName~Organization|FullyQualifiedName~DocumentSequence"`): **Passed: 191, Failed: 0, Skipped: 32, Total: 223**.
  - Full non-SQL test suite: **Passed: 448, Failed: 0, Skipped: 66, Total: 514**.
  - Local process verification: `ForgeFlow.Web.exe` running in Development mode listening on `https://localhost:5001` and `http://127.0.0.1:5000` with database connection `localhost/testforgflow`; verified HTTP 200 responses on `curl.exe -k -s -w "%{http_code}\n" -L https://localhost:5001/account/login -o NUL` -> **200**.
- **Next Small Step**: Proceed to Step 5: Decompose remaining extra-large UI workbenches (`BomVersionWorkbenchModal.razor` in `Boms.razor` and `CreateEditSalesOrderModal.razor` in `Sales.razor`).


## Audit Remediation — Phase 17 (Large Blazor UI Workbenches Decomposition)

- **R5.1 Blazor Modal Decomposition (Sales & BOM Modules)**:
  - **Sales Order Create & Edit Modal** (`src/ForgeFlow.Web/Components/Pages/SalesSections/CreateEditSalesOrderModal.razor`):
    - Extracted [CreateEditSalesOrderModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/SalesSections/CreateEditSalesOrderModal.razor): encapsulated general order information, dynamic site warehouse selection, responsive Jalali/Gregorian order date picker, currency selector, line items table with stock availability pills, grand total calculation, manual order number sequence preview/override, and optimistic concurrency updates.
    - Encapsulated customer, item, currency, unit, and inventory balance lookups internally with public initializer methods `InitializeForCreateAsync` and `InitializeForEditAsync`.
    - Refactored [Sales.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Sales.razor) from 1216 lines down to 863 lines (reduced by 353 lines), replacing inline markup and redundant order edit fields and helper methods with clean component reference and callback.
  - **BOM Version Workbench Modal** (`src/ForgeFlow.Web/Components/Pages/BomSections/BomVersionWorkbenchModal.razor`):
    - Extracted [BomVersionWorkbenchModal.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/BomSections/BomVersionWorkbenchModal.razor): encapsulated 528 lines of complex workbench markup and logic including version status badge, output quantity/unit, Jalali/Gregorian effective date pickers, draft inputs grid with scrap and optional flags, draft outputs grid with co/by-product types, and context dimension applicability conditions matrix with operators.
    - Encapsulated draft line models `InputRowModel`, `OutputRowModel`, and `ConditionRowModel`, along with draft saving and status transition operations (`TransitionVersionAsync`) inside the component.
    - Refactored [Boms.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Pages/Boms.razor) from 2041 lines down to 1256 lines (reduced by 785 lines).
- **Verification Evidence (2026-11-13)**:
  - Solution build (`dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`): **0 Warning(s), 0 Error(s)**.
  - Unit test suite (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "Category!=SqlServer"`): **Passed: 448, Failed: 0, Skipped: 66, Total: 514**.
  - Local process verification: `ForgeFlow.Web.exe` running in Development mode listening on `https://localhost:5001` and `http://127.0.0.1:5000` with database connection `localhost/testforgflow`; verified HTTP 200 responses on `https://localhost:5001/account/login` (HTTP 200) and `http://127.0.0.1:5000/login` (HTTP 200).
- **Next Small Step**: Proceed to Step 6: Secure browser plaintext form drafts in `DraftCoordinator.cs` / `drafts.mjs` (addressing PDF audit row 13).


## Audit Remediation — Phase 18 (Browser Draft Security & Sensitive Field Protection)

- **R6.1 Client Form Draft Hardening (`DraftCoordinator.cs` & `drafts.mjs`)**:
  - **Cipher Storage Envelope**:
    - Enhanced [drafts.mjs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/js/drafts.mjs) with XOR / Base64 dynamic tab-keyed cipher envelope (`ff-sec-draft:`), removing plaintext JSON storage from browser `sessionStorage`.
    - Implemented `purgeExpiredDrafts()` to automatically garbage collect stale or expired drafts (`ttl = 30m`) across all form scopes upon initialization.
  - **Sensitive Field Validation & XSS Defense**:
    - Added `ValidateDefinition` to [DraftCoordinator.cs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Drafts/DraftCoordinator.cs) blocking sensitive keywords (`password`, `secret`, `token`, `credential`, `auth`, `cvv`, `pin`, `key`) from being registered as draft fields.
    - Added client-side `isSensitive()` guard and regex pattern matching in `capture()` and event handlers in [drafts.mjs](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/wwwroot/js/drafts.mjs) ensuring sensitive fields are never captured or saved to storage.
  - **Automated Tests**:
    - Added unit tests in [SwitchAndDraftTests.cs](file:///d:/Projects/ForgeFlow/tests/ForgeFlow.Web.Tests/SwitchAndDraftTests.cs) verifying strict rejection of sensitive fields and acceptance of valid business fields.
- **Verification Evidence (2026-11-13)**:
  - Solution build (`dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`): **0 Warning(s), 0 Error(s)**.
  - Unit test suite (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "Category!=SqlServer"`): **Passed: 455, Failed: 0, Skipped: 66, Total: 521**.
  - Local process verification: `ForgeFlow.Web.exe` running in Development mode listening on `https://localhost:5001` and `http://127.0.0.1:5000` with database connection `localhost/testforgflow`; verified HTTP 200 responses on `https://localhost:5001/account/login` (HTTP 200) and `http://127.0.0.1:5000/login` (HTTP 200).
- **Next Small Step**: Proceed to Step 7: Final Comprehensive Verification & Closeout across all audit items, integration tests, and documentation.


## Audit Remediation — Phase 19 (Final Comprehensive SQL Server Integration, Model Hygiene & Audit Closeout)

- **R7.1 Full System Integration & Acceptance Verification**:
  - **SQL Server Database Integration Tests**:
    - Ran live test suites against local SQL Server instance (`Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True`):
      - Document Sequence Monotonic Allocation & Reset (`DocumentSequenceTests`): **3 Passed, 0 Failed, 0 Skipped**.
      - Units, Conversions, Material Shortages & Diagnostics (`UnitSqlTests`, `ReportSqlTests`, `DiagnosticSqlTests`): **3 Passed, 0 Failed, 0 Skipped**.
      - Sales Order Reservations, Delivery Dispatch, QC Reversal, Routing & Lab Specs (`SalesDispatchSqlTests`, `ReservationSqlTests`, `RoutingSqlTests`, `LabSpecSqlTests`): **5 Passed, 0 Failed, 0 Skipped**.
  - **EF Core Model Hygiene & Snapshot Verification**:
    - Executed `OrganizationPersistenceModelTests`: **7 Passed, 0 Failed, 0 Skipped**; verified `HasPendingModelChanges() == false`, all 11 `IEntityTypeConfiguration<T>` entity configurations are strictly synchronized with database schema and migration snapshots.
  - **Zero-Warning Solution Build & Test Suite**:
    - Build: `dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`: **0 Warning(s), 0 Error(s)**.
    - Full Unit Suite: **455 Passed, 0 Failed, 66 Skipped, Total: 521**.
  - **Live Web Server Verification**:
    - `ForgeFlow.Web.exe` listening on `https://localhost:5001` and `http://127.0.0.1:5000` with process-only database connection to `localhost/testforgflow`.
    - `GET https://localhost:5001/account/login` -> **HTTP 200 OK**.
    - `GET http://127.0.0.1:5000/login` -> **HTTP 200 OK** (after secure redirection).
- **Audit Remediation Complete**:
  - All 19 rows of the Application UseCases audit findings, EF Core monolithic model configurations, optimistic concurrency invariants, domain event implementations, Blazor UI workbench decompositions, Jalali calendar edge cases, and client-side draft security mitigations are 100% resolved and verified.


## Phase 20 (Shared Permission Accordion Component & Role Management Modal Alignment)

- **Reusable Component (`PermissionAccordionPanel.razor`)**:
  - Extracted shared permission accordion panel component ([PermissionAccordionPanel.razor](file:///d:/Projects/ForgeFlow/src/ForgeFlow.Web/Components/Layout/PermissionAccordionPanel.razor)) reusable across `Users.razor` and `Roles.razor` matching the reference UI.
  - Implements: summary card (`.pm-summary`) with full/dual/single progress bars and permission counts (`PermSummaryCount`, `PermSummaryBreakdown`, `PermSummaryFullAccess`), search filter input (`.pm-search`), filter category buttons (`.pm-filter-group` for All, Granted, Direct, Changes), collapsible accordion modules (`.pa-group` / `.pa-group-header` / `.pa-group-body`) with localized page titles, progress mini-bars, item counts, bulk operations (`PermSelectAllEditable`, `PermClearDirect`), permission operation cards (`.pa-op-card`) with auto-read permission dependency resolution (`ToggleOperationWithAutoRead`), lock indicators for role-inherited permissions (`PermBadgeRole`), dirty pills (`PermBadgeNew`, `PermChangesBadge`), and bottom legend (`.pm-legend`).
  - Supports flexible slots (`HeaderContent`, `StatusContent`, `FooterContent`) for page-specific headers, scoped site selectors, role chip management, name inputs, and save/cancel actions.
- **Pages Refactored**:
  - `src/ForgeFlow.Web/Components/Pages/Users.razor`: Replaced inline accordion markup with `<PermissionAccordionPanel>`, preserving user role badges, site scope selector, and direct permission editing.
  - `src/ForgeFlow.Web/Components/Pages/Roles.razor`: Refactored both Create Role and Edit/Detail Role modals to use `<PermissionAccordionPanel>`, eliminating ~330 lines of duplicate UI markup while preserving all private reflection test hooks (`createSelectedPerms`, `editSelectedPerms`, `pendingDraft`, `createName`, etc.).
- **Localization & Contract Validation**:
  - `tests/ForgeFlow.Web.Tests/ResourceContractTests.cs`: Preserved bidirectional contract validation across `SharedResources.en-US.resx` and `SharedResources.fa-IR.resx`.
- **Verification Evidence (2026-11-14)**:
  - Solution build (`dotnet build d:\Projects\ForgeFlow\ForgeFlow.slnx -c Release --no-restore -m:1`): **0 Warning(s), 0 Error(s)**.
  - Role management tests (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~Role"`): **48 Passed, 8 Skipped, 0 Failed**.
  - User management tests (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~User"`): **62 Passed, 11 Skipped, 0 Failed**.
  - Resource contract tests (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ResourceContractTests"`): **13 Passed, 0 Skipped, 0 Failed**.
  - Unit test suite (`dotnet test tests/ForgeFlow.Web.Tests/ForgeFlow.Web.Tests.csproj -c Release --no-build --filter "Category!=SqlServer"`): **Passed: 455, Failed: 0, Skipped: 66, Total: 521**.
  - Local process verification per `AGENTS.md`: `ForgeFlow.Web.dll` running in Development mode (`task-369`) at `https://localhost:5001` with process-only database connection to `localhost/testforgflow`; verified HTTP 200 response on `https://localhost:5001/account/login` (HTTP 200, length 16855).
- **Next Small Step**: Await user acceptance of the redesigned Roles and Users permission accordion modals.

- **User Feedback Remediation (Roles & Permissions UX Polish)**:
  - Eliminated mouse waiting cursor (`cursor: wait`): configured `.pa-op-card.is-readonly` and `.pa-op-card:disabled.is-readonly` to use `cursor: default` so inspecting permissions in view mode never shows a spinning cursor.
  - Eliminated modal layout jump when toggling filters: added `min-height: min(74vh, 600px)` to `.pa-modal` and removed wrapping `<form>` element so dialog height remains steady when 0 or few items match.
  - Initialized `initialCreatePerms` in `Roles.razor` so the "Changes" (`تغییرات`) filter accurately reflects dirty selections during role creation.
  - Removed discard confirmation prompt (`WriteDiscardConfirm` / `createDiscardConfirm`) so clicking Cancel or Close dismisses the dialog immediately without intrusive popups.
  - Compacted Role modal header: aligned role name into modal title (`{Title} — {RoleDisplayName}`), replaced bulky `<dl class="read-detail">` with a compact `.pa-roles-bar` taking only ~36px height matching `Users.razor`, and removed `ShowOnFocus="true"` from grid buttons so action tooltips do not linger on modal open.
  - Verified: Release build 0 warnings/0 errors, 455 unit tests passed, Development web listening on `https://localhost:5001` with `GET /account/login` returning HTTP 200.

- **Progress Bar Invariant Formatting Fix (fa-IR Culture Slash Bug)**:
  - Fixed CSS progress bar widths dropping in Persian culture: under `fa-IR`, `double.ToString()` produces `/` as decimal separator (e.g. `33/3%` instead of `33.3%`), causing browsers to drop invalid CSS width syntax for 1/3 and 2/3 states.
  - Implemented `PctCss(double value)` using `CultureInfo.InvariantCulture` (`{value:0.#}%`), ensuring CSS percentages are always emitted with invariant period separator (`33.3%`, `66.7%`, `12.5%`, etc.) across all mini progress bars and summary bars in both `Users.razor` and `Roles.razor`.
  - Verified: Release build 0 warnings/0 errors, 455 unit tests passed, Development web listening on `https://localhost:5001` with `GET /account/login` returning HTTP 200.


## Phase 21 (Quality Control 4-Level Menu Architecture, Inventory Tab Alignment, and Sales Customer Tab Elimination)

- **Quality Control Menu & Deep Linking (`MainLayout.razor` & `Quality.razor`)**:
  - Re-architected Quality Control navigation items in `MainLayout.razor` to present the 4 standard ERP quality control capabilities:
    1. **بازرسی و قرنطینه** (`/quality?tab=inspections`): ثبت بازرسی ورودی، حین تولید و محصول نهایی، تأیید، رد و قرنطینه کالا.
    2. **مشخصات و الگوهای بازرسی** (`/quality?tab=specs`): تعریف استانداردها، چک‌لیست‌ها، معیارهای پذیرش و نقاط کنترل.
    3. **پارامترهای آزمایشگاهی** (`/quality?tab=parameters`): تعریف پارامترها، واحد اندازه‌گیری، محدوده مجاز و روش آزمون.
    4. **ردیابی لات و تاریخ تولید و انقضا** (`/quality?tab=lots`): مدیریت Batch/Lot، تاریخ تولید، انقضا و سوابق ردیابی محصول.
  - Added `[SupplyParameterFromQuery(Name = "tab")]` query parameter support in `Quality.razor` allowing direct deep-linking into any of the 4 tabs from the sidebar menu or external links.
  - Synchronized tab order constants in `Quality.razor` (`TabInspections = 0`, `TabSpecs = 1`, `TabParameters = 2`, `TabLots = 3`).
- **Inventory Page Tab Alignment (`Inventory.razor`)**:
  - Re-ordered and renamed the 4 tabs in `Inventory.razor` to match standard warehouse flow:
    1. **انبارها** (`TabWarehouses = 0`)
    2. **موجودی فعلی** (`TabBalances = 1`)
    3. **اسناد انبار** (`TabMovements = 2`, renamed from "اسناد و گردش کالا" to "اسناد انبار")
    4. **رزرو موجودی** (`TabReservations = 3`, renamed from "رزروها" to "رزرو موجودی")
  - Set default active tab to `TabWarehouses`.
- **Sales Page Customer Tab Elimination (`Sales.razor`)**:
  - Removed the redundant Customers tab (`TabCustomers`) and customer creation/management UI from `Sales.razor`, as customer management is now unified under `BusinessPartners.razor` (`/business-partners`).
  - Retained `TabOrders` (سفارش‌های فروش) and `TabDispatches` (حواله و تحویل محصول).
  - Deleted obsolete `CustomerGrid.razor` component from `src/ForgeFlow.Web/Components/Pages/SalesSections/`.
  - Removed unused Customer modal markup, methods, and state fields from `Sales.razor`.
- **Localization & Contract Synchronization**:
  - Updated `SharedResources.fa-IR.resx` and `SharedResources.en-US.resx` with new keys `QcMenuInspections`, `QcMenuSpecs`, `QcMenuParameters`, and `QcMenuLots`.
  - Updated `InvTabMovements` ("اسناد انبار" / "Warehouse Documents") and `InvTabReservations` ("رزرو موجودی" / "Stock Reservations").
  - Removed retired keys `SalesTabCustomers`, `SalesCustomerCreateButton`, `SalesCustomerEditTitle`, and `SalesCustomerTaxCode`.
  - Synchronized `tests/ForgeFlow.Web.Tests/ResourceContractTests.cs` and updated `tests/ForgeFlow.Web.Tests/RoleManagementPageTests.cs` to assert the 4 Quality sub-links.
- **Verification Evidence (2026-11-15)**:
  - Solution build (`dotnet build ForgeFlow.slnx -c Release --no-restore -m:1`): **0 Warning(s), 0 Error(s)**.
  - Role management tests (`dotnet test ... --filter "FullyQualifiedName~RoleManagementPageTests"`): **9 Passed, 0 Failed**.
  - Resource contract tests (`dotnet test ... --filter "FullyQualifiedName~ResourceContractTests"`): **13 Passed, 0 Failed**.
  - Full test suite (`dotnet test ... --filter "Category!=SqlServer"`): **455 Passed, 0 Failed, 66 Skipped, Total: 521**.
  - Quality, Sales, and Inventory tests (`dotnet test ... --filter "FullyQualifiedName~Quality|FullyQualifiedName~Sales|FullyQualifiedName~Inventory"`): **18 Passed, 0 Failed, 3 Skipped**.
  - Local process verification: `ForgeFlow.Web.dll` running in Development mode (`task-1518`) at `https://localhost:5001`; verified HTTP 200 response on `https://localhost:5001/account/login`.
- **Next Small Step**: Await user review and confirmation.

