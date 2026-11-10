using System.Text;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Reporting;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Web.Roles;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ReportTests
{
    private static readonly TrustedActor Actor1 = new(101, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
    private static readonly OrganizationScope OrgScope = new OrganizationScope.Organization(10);
    private static readonly OrganizationScope SiteAScope = new OrganizationScope.Site(10, 1);
    private static readonly OrganizationScope SiteBScope = new OrganizationScope.Site(10, 2);

    [Fact]
    public async Task R01_material_shortage_normalizes_hybrid_units_and_excludes_inactive_orders_and_subassemblies()
    {
        var store = new FakeReportingStore
        {
            ShortageData = new ShortageDataset(
                Orders:
                [
                    new ReportOpenOrderRow(1, "PO-01", 1, "S1", new DateOnly(2026, 11, 20), ProductionOrderStatus.Released),
                    new ReportOpenOrderRow(2, "PO-02", 1, "S1", new DateOnly(2026, 11, 22), ProductionOrderStatus.InProgress),
                    new ReportOpenOrderRow(9, "PO-DRAFT", 1, "S1", new DateOnly(2026, 11, 25), ProductionOrderStatus.Draft)
                ],
                Inputs:
                [
                    // 1 BOX = 12 PCS (item-specific overrides global), plus 5 PCS on PO-02 => Total required 17 PCS vs 10 PCS on hand => Shortage 7 PCS.
                    new ReportOpenOrderInputRow(1, 100, 1m, 2),
                    new ReportOpenOrderInputRow(2, 100, 5m, 1),
                    // Excess item: required 4 KG vs 15 KG on hand => Shortage 0, Excess 11 KG.
                    new ReportOpenOrderInputRow(1, 101, 4m, 3)
                ],
                Balances:
                [
                    new ReportStockBalanceRow(1, 100, 10m),
                    new ReportStockBalanceRow(1, 101, 15m)
                ],
                Items:
                [
                    new ReportItemRef(100, "MAT-BOLT", "Hex Bolt", 1, "PCS", "pcs"),
                    new ReportItemRef(101, "MAT-STEEL", "Steel Bar", 3, "KG", "kg")
                ],
                Conversions:
                [
                    new ReportConversionEdgeRow(null, 2, 1, 10m), // global 1 BOX = 10 PCS
                    new ReportConversionEdgeRow(100, 2, 1, 12m)   // item-specific 1 BOX = 12 PCS
                ])
        };

        var useCases = new ReportingUseCases(
            store,
            new FixedActor(Actor1),
            new FixedPermissions(ReportingPermissions.ReportsRead));

        var allRes = await useCases.GetMaterialShortageReport(OrgScope, 10, new MaterialShortageQuery(OnlyShortages: false));
        Assert.True(allRes.Succeeded);
        var report = allRes.Value!;
        Assert.Equal(3, report.OpenOrderCount);
        Assert.Equal(2, report.TotalMaterialCount);
        Assert.Equal(1, report.ShortageMaterialCount);
        Assert.Equal(7m, report.TotalShortageBaseQuantity);

        var bolt = report.Items.Single(x => x.ItemCode == "MAT-BOLT");
        Assert.Equal(17m, bolt.RequiredBaseQuantity);
        Assert.Equal(10m, bolt.OnHandBaseQuantity);
        Assert.Equal(7m, bolt.ShortageBaseQuantity);
        Assert.Equal(0m, bolt.ExcessBaseQuantity);
        Assert.Equal(58.82m, bolt.CoveragePercent);
        Assert.Equal(2, bolt.OpenOrderCount);

        var steel = report.Items.Single(x => x.ItemCode == "MAT-STEEL");
        Assert.Equal(4m, steel.RequiredBaseQuantity);
        Assert.Equal(15m, steel.OnHandBaseQuantity);
        Assert.Equal(0m, steel.ShortageBaseQuantity);
        Assert.Equal(11m, steel.ExcessBaseQuantity);
        Assert.Equal(100m, steel.CoveragePercent);

        var onlyShortRes = await useCases.GetMaterialShortageReport(OrgScope, 10, new MaterialShortageQuery(OnlyShortages: true));
        Assert.True(onlyShortRes.Succeeded);
        Assert.Single(onlyShortRes.Value!.Items);
        Assert.Equal("MAT-BOLT", onlyShortRes.Value!.Items[0].ItemCode);
    }

    [Fact]
    public async Task R02_production_variance_handles_issues_reversals_and_output_yield()
    {
        var store = new FakeReportingStore
        {
            VarianceData = new VarianceDataset(
                Orders:
                [
                    new ReportVarianceOrderRow(
                        50, 10, 1, "S1", "Plant 1", "PO-050", ProductionOrderStatus.Completed,
                        ItemId: 200, RequestedQuantity: 2m, RequestedUnitId: 2, // 2 BOX = 20 PCS
                        PlannedStartDate: new DateOnly(2026, 11, 1), PlannedDueDate: new DateOnly(2026, 11, 5),
                        ActualProducedQuantity: 1.8m, ActualScrapQuantity: 0.2m) // 18 PCS good, 2 PCS scrap
                ],
                Inputs:
                [
                    new ReportVarianceInputRow(50, 100, 100m, 1, IsSubAssembly: false),
                    new ReportVarianceInputRow(50, 999, 1m, 1, IsSubAssembly: true) // ignored as sub-assembly placeholder
                ],
                Outputs:
                [
                    new ReportVarianceOutputRow(50, 200, BomOutputType.MainProduct, 20m, 1)
                ],
                LedgerRows:
                [
                    // Issued 120 PCS, then reversed 10 PCS => Net Issued 110 PCS (+10 over-consumption vs 100 planned)
                    new ReportOrderLedgerRow(50, InventoryTransactionType.ProductionIssue, 100, -120m),
                    new ReportOrderLedgerRow(50, InventoryTransactionType.ProductionIssue, 100, 10m),
                    // Received 18 PCS of finished product
                    new ReportOrderLedgerRow(50, InventoryTransactionType.ProductionReceipt, 200, 18m)
                ],
                Items:
                [
                    new ReportItemRef(100, "RAW-A", "Raw Material A", 1, "PCS", "pcs"),
                    new ReportItemRef(200, "FG-X", "Finished Box", 1, "PCS", "pcs")
                ],
                Conversions:
                [
                    new ReportConversionEdgeRow(null, 2, 1, 10m)
                ])
        };

        var useCases = new ReportingUseCases(
            store,
            new FixedActor(Actor1),
            new FixedPermissions(ReportingPermissions.ReportsRead));

        var listRes = await useCases.GetProductionVarianceReport(OrgScope, 10, new ProductionVarianceQuery());
        Assert.True(listRes.Succeeded);
        var summary = Assert.Single(listRes.Value!.Orders);
        Assert.Equal(20m, summary.PlannedBaseQuantity);
        Assert.Equal(18m, summary.ActualProducedBaseQuantity);
        Assert.Equal(2m, summary.ActualScrapBaseQuantity);
        Assert.Equal(18m, summary.PostedReceivedBaseQuantity);
        Assert.Equal(-2m, summary.OutputVarianceBaseQuantity);
        Assert.Equal(90m, summary.YieldPercent);
        Assert.Equal(100m, summary.TotalPlannedInputBaseQuantity);
        Assert.Equal(110m, summary.TotalIssuedInputBaseQuantity);
        Assert.Equal(10m, summary.TotalInputVarianceBaseQuantity);
        Assert.Equal(1, listRes.Value!.OverConsumedOrderCount);
        Assert.Equal(1, listRes.Value!.UnderYieldOrderCount);

        var detailRes = await useCases.GetProductionVarianceDetail(OrgScope, 10, 50);
        Assert.True(detailRes.Succeeded);
        var inputLine = Assert.Single(detailRes.Value!.Inputs);
        Assert.Equal(100m, inputLine.PlannedBaseQuantity);
        Assert.Equal(110m, inputLine.IssuedBaseQuantity);
        Assert.Equal(10m, inputLine.VarianceBaseQuantity);
        Assert.Equal(0m, inputLine.RemainingToIssueBaseQuantity);
        Assert.Equal(110m, inputLine.ConsumptionPercent);

        var outputLine = Assert.Single(detailRes.Value!.Outputs);
        Assert.Equal(20m, outputLine.PlannedBaseQuantity);
        Assert.Equal(18m, outputLine.ReceivedBaseQuantity);
        Assert.Equal(-2m, outputLine.VarianceBaseQuantity);
        Assert.Equal(90m, outputLine.AchievementPercent);
    }

    [Fact]
    public async Task R03_site_scoped_actor_is_isolated_from_other_sites_and_organizations()
    {
        var store = new FakeReportingStore();
        var useCases = new ReportingUseCases(
            store,
            new FixedActor(Actor1),
            new FixedPermissions(ReportingPermissions.ReportsRead, ReportingPermissions.ReportsExport));

        // Site A user attempting to query Site B or a different organization fails closed with AccessDenied.
        var crossSiteShortage = await useCases.GetMaterialShortageReport(SiteAScope, 10, new MaterialShortageQuery(SiteId: 2));
        Assert.False(crossSiteShortage.Succeeded);
        Assert.Equal(ReportingErrors.AccessDenied, crossSiteShortage.Error!.Code);

        var crossOrgVariance = await useCases.GetProductionVarianceReport(SiteAScope, 99, new ProductionVarianceQuery());
        Assert.False(crossOrgVariance.Succeeded);
        Assert.Equal(ReportingErrors.AccessDenied, crossOrgVariance.Error!.Code);

        var siteARes = await useCases.GetMaterialShortageReport(SiteAScope, 10, new MaterialShortageQuery());
        Assert.True(siteARes.Succeeded);
        Assert.Equal(1L, store.LastQueriedSiteId);
    }

    [Fact]
    public async Task R04_read_only_actor_cannot_export_csv_without_reports_export_permission()
    {
        var store = new FakeReportingStore();
        var readOnlyUseCases = new ReportingUseCases(
            store,
            new FixedActor(Actor1),
            new FixedPermissions(ReportingPermissions.ReportsRead));

        var headers10 = new CsvExportColumnHeaders(["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"], "shortage.csv");
        var headers12 = new CsvExportColumnHeaders(["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12"], "variance.csv");

        var shortageRead = await readOnlyUseCases.GetMaterialShortageReport(OrgScope, 10, new MaterialShortageQuery());
        Assert.True(shortageRead.Succeeded);

        var expShortage = await readOnlyUseCases.ExportMaterialShortageCsv(OrgScope, 10, new MaterialShortageQuery(), headers10);
        Assert.False(expShortage.Succeeded);
        Assert.Equal(ReportingErrors.AccessDenied, expShortage.Error!.Code);

        var expVariance = await readOnlyUseCases.ExportProductionVarianceCsv(OrgScope, 10, new ProductionVarianceQuery(), headers12);
        Assert.False(expVariance.Succeeded);
        Assert.Equal(ReportingErrors.AccessDenied, expVariance.Error!.Code);
    }

    [Fact]
    public async Task R05_csv_export_neutralizes_formula_injection_and_emits_utf8_bom()
    {
        var store = new FakeReportingStore
        {
            ShortageData = new ShortageDataset(
                Orders:
                [
                    new ReportOpenOrderRow(1, "PO-01", 1, "S1", new DateOnly(2026, 11, 20), ProductionOrderStatus.Released)
                ],
                Inputs:
                [
                    new ReportOpenOrderInputRow(1, 100, 5m, 1)
                ],
                Balances: [],
                Items:
                [
                    new ReportItemRef(100, "IT-1", "=HYPERLINK(\"http://evil\",\"Bolt, Special\")", 1, "PCS", "pcs")
                ],
                Conversions: [])
        };

        var useCases = new ReportingUseCases(
            store,
            new FixedActor(Actor1),
            new FixedPermissions(ReportingPermissions.ReportsExport));

        var cols = new CsvExportColumnHeaders(
            ["Code", "Name", "Unit", "Req", "OnHand", "Shortage", "Excess", "Coverage", "OrdersCount", "Orders"],
            "report/../evil?.csv");

        var res = await useCases.ExportMaterialShortageCsv(OrgScope, 10, new MaterialShortageQuery(), cols);
        Assert.True(res.Succeeded);
        var file = res.Value!;
        Assert.DoesNotContain("/", file.FileName);
        Assert.DoesNotContain("..", file.FileName);
        Assert.EndsWith(".csv", file.FileName);

        // Verify UTF-8 BOM
        Assert.True(file.Bytes.Length > 3);
        Assert.Equal(0xEF, file.Bytes[0]);
        Assert.Equal(0xBB, file.Bytes[1]);
        Assert.Equal(0xBF, file.Bytes[2]);

        var text = Encoding.UTF8.GetString(file.Bytes, 3, file.Bytes.Length - 3);
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\",\"\"Bolt, Special\"\")\"", text);

        // Also assert EscapeCsvCell neutralizes all dangerous triggers: = + - @ \t \r
        Assert.Equal("\"'+SUM(1,2)\"", ReportingUseCases.EscapeCsvCell("+SUM(1,2)"));
        Assert.Equal("'-1+1", ReportingUseCases.EscapeCsvCell("-1+1"));
        Assert.Equal("'@cmd", ReportingUseCases.EscapeCsvCell("@cmd"));
        Assert.Equal("'\tabc", ReportingUseCases.EscapeCsvCell("\tabc"));
    }

    [Fact]
    public void R06_reports_permissions_are_registered_localized_and_granted_to_org_admin()
    {
        Assert.Contains(PermissionModule.Reports, RolePermissionLocalization.TenantModules);
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(ReportingPermissions.ReportsRead, 1));
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(ReportingPermissions.ReportsRead, 2));
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(ReportingPermissions.ReportsExport, 1));
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(ReportingPermissions.ReportsExport, 2));
        Assert.Equal("PermPageReports", RolePermissionLocalization.PageTitleKey(PermissionModule.Reports));
        Assert.Equal("PermOpRead", RolePermissionLocalization.OperationLabelKey(ReportingPermissions.ReportsRead));
        Assert.Equal("PermOpExportReports", RolePermissionLocalization.OperationLabelKey(ReportingPermissions.ReportsExport));
        Assert.Equal("RptErrorOrderNotFound", RolePermissionLocalization.FailureKey(ReportingErrors.ProductionOrderNotFound));
    }

    private sealed class FixedActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class FixedPermissions(params string[] allowed) : IOrganizationPermissionEvaluator
    {
        private readonly HashSet<string> perms = new(allowed, StringComparer.Ordinal);
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor,
            OrganizationScope scope,
            string permission,
            CancellationToken ct = default) =>
            ValueTask.FromResult(new PermissionDecision(perms.Contains(permission)));
    }

    private sealed class FakeReportingStore : IReportingQueryStore
    {
        public long? LastQueriedSiteId { get; private set; }
        public ShortageDataset ShortageData { get; init; } = new([], [], [], [], []);
        public VarianceDataset VarianceData { get; init; } = new([], [], [], [], [], []);

        public Task<IReadOnlyList<ReportSiteInfo>> ListAuthorizedSites(long organizationId, long? forcedSiteId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ReportSiteInfo>>([new ReportSiteInfo(1, "S1", "Plant 1")]);

        public Task<ShortageDataset> GetShortageDataset(long organizationId, long? siteId, DateOnly? dueFrom, DateOnly? dueTo, CancellationToken ct)
        {
            LastQueriedSiteId = siteId;
            return Task.FromResult(ShortageData);
        }

        public Task<VarianceDataset> GetVarianceDataset(long organizationId, long? siteId, long? productionOrderId, ProductionOrderStatus? status, DateOnly? dueFrom, DateOnly? dueTo, string? search, CancellationToken ct)
        {
            LastQueriedSiteId = siteId;
            return Task.FromResult(VarianceData);
        }
    }
}
