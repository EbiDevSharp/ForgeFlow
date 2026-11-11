using ForgeFlow.Application.Dashboard;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Routings;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Infrastructure.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ForgeFlow.Infrastructure.Organizations;

public sealed class SqlDashboardQueryStore(
    Func<IDbContextFactory<OrganizationDbContext>> getFactory,
    IErrorLogRecorder? errorRecorder = null,
    IOperationContextAccessor? opContext = null)
    : IDashboardQueryStore
{
    private IDbContextFactory<OrganizationDbContext> Factory => getFactory();
    public SqlDashboardQueryStore(IDbContextFactory<OrganizationDbContext> factory) : this(() => factory) { }

    public async Task<DashboardSnapshot> GetSnapshot(
        long organizationId,
        long? siteId,
        DateOnly today,
        DashboardCapabilities capabilities,
        CancellationToken ct)
    {
        try
        {
            await using var db = await Factory.CreateDbContextAsync(ct);

            DashboardProductionStats? prodStats = null;
            IReadOnlyList<DashboardOrderItemSummary> attentionOrders = [];
            if (capabilities.Production)
            {
                var openOrdersBase = db.ProductionOrders.AsNoTracking()
                    .Where(p => p.OrganizationId == organizationId
                        && (p.Status == ProductionOrderStatus.Draft
                            || p.Status == ProductionOrderStatus.Released
                            || p.Status == ProductionOrderStatus.InProgress));
                if (siteId is long sid && sid > 0)
                    openOrdersBase = openOrdersBase.Where(p => p.SiteId == sid);

                var statusCounts = await openOrdersBase
                    .GroupBy(p => p.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

                var overdueCount = await openOrdersBase.CountAsync(p => p.PlannedDueDate < today, ct);
                prodStats = new DashboardProductionStats(
                    statusCounts.GetValueOrDefault(ProductionOrderStatus.Draft),
                    statusCounts.GetValueOrDefault(ProductionOrderStatus.Released),
                    statusCounts.GetValueOrDefault(ProductionOrderStatus.InProgress),
                    overdueCount);

                attentionOrders = await (from p in openOrdersBase
                                         join s in db.Sites.AsNoTracking() on new { p.OrganizationId, SiteId = p.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                                         join it in db.Items.AsNoTracking() on new { p.OrganizationId, ItemId = p.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                                         join u in db.Units.AsNoTracking() on new { p.OrganizationId, UnitId = p.RequestedUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                                         orderby p.PlannedDueDate, p.Id descending
                                         select new DashboardOrderItemSummary(
                                             p.Id,
                                             p.OrderNumber,
                                             p.SiteId,
                                             s.Code,
                                             it.Code,
                                             it.Name,
                                             p.RequestedQuantity,
                                             u.Symbol,
                                             p.PlannedDueDate,
                                             p.Status,
                                             p.PlannedDueDate < today))
                    .Take(6)
                    .ToListAsync(ct);
            }

            DashboardShortageStats? shortageStats = null;
            IReadOnlyList<DashboardShortageItem> topShortages = [];
            if (capabilities.Reports || capabilities.Inventory)
            {
                var reportStore = new SqlReportingQueryStore(getFactory, errorRecorder, opContext);
                var dataset = await reportStore.GetShortageDataset(organizationId, siteId, null, null, ct);
                var report = ReportingUseCases.ComputeMaterialShortage(dataset, new MaterialShortageQuery(SiteId: siteId, OnlyShortages: true));
                var totalReserved = dataset.Balances.Sum(b => b.ReservedBaseQuantity);
                shortageStats = new DashboardShortageStats(
                    report.OpenOrderCount,
                    report.ShortageMaterialCount,
                    totalReserved,
                    report.TotalShortageBaseQuantity);
                topShortages = report.Items
                    .Take(6)
                    .Select(i => new DashboardShortageItem(
                        i.ItemId, i.ItemCode, i.ItemName, i.RequiredBaseQuantity,
                        i.AvailableBaseQuantity, i.ReservedBaseQuantity, i.ShortageBaseQuantity, i.BaseUnitSymbol))
                    .ToArray();
            }

            DashboardQualityStats? qualityStats = null;
            IReadOnlyList<DashboardQualityQueueItem> pendingInspections = [];
            if (capabilities.Quality)
            {
                var qiQuery = db.QualityInspections.AsNoTracking()
                    .Where(q => q.OrganizationId == organizationId
                        && (q.Status == QualityInspectionStatus.Pending || q.Status == QualityInspectionStatus.InReview));
                if (siteId is long sid && sid > 0)
                    qiQuery = qiQuery.Where(q => q.SiteId == sid);

                var pendingCnt = await qiQuery.CountAsync(q => q.Status == QualityInspectionStatus.Pending, ct);
                var reviewCnt = await qiQuery.CountAsync(q => q.Status == QualityInspectionStatus.InReview, ct);
                var blockedSum = await qiQuery.Select(q => (decimal?)q.RemainingBlockedBaseQuantity).SumAsync(ct) ?? 0m;
                qualityStats = new DashboardQualityStats(pendingCnt, reviewCnt, blockedSum);

                pendingInspections = await (from q in qiQuery
                                            join s in db.Sites.AsNoTracking() on new { q.OrganizationId, SiteId = q.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                                            join it in db.Items.AsNoTracking() on new { q.OrganizationId, ItemId = q.ItemId } equals new { it.OrganizationId, ItemId = it.Id }
                                            join u in db.Units.AsNoTracking() on new { q.OrganizationId, UnitId = q.BaseUnitId } equals new { u.OrganizationId, UnitId = u.Id }
                                            orderby q.Id descending
                                            select new DashboardQualityQueueItem(
                                                q.Id, q.InspectionNumber, s.Code, it.Code, it.Name, q.LotCode,
                                                q.RemainingBlockedBaseQuantity, u.Symbol, q.Status))
                    .Take(6)
                    .ToListAsync(ct);
            }

            DashboardProcurementStats? procStats = null;
            IReadOnlyList<DashboardPurchaseOrderItem> openPos = [];
            if (capabilities.Procurement)
            {
                var poQuery = db.PurchaseOrders.AsNoTracking()
                    .Where(p => p.OrganizationId == organizationId
                        && (p.Status == PurchaseOrderStatus.Approved || p.Status == PurchaseOrderStatus.PartiallyReceived));
                if (siteId is long sid && sid > 0)
                    poQuery = poQuery.Where(p => p.SiteId == sid);

                var appCnt = await poQuery.CountAsync(p => p.Status == PurchaseOrderStatus.Approved, ct);
                var partCnt = await poQuery.CountAsync(p => p.Status == PurchaseOrderStatus.PartiallyReceived, ct);
                var overduePoCnt = await poQuery.CountAsync(p => p.ExpectedDeliveryDate != null && p.ExpectedDeliveryDate < today, ct);
                procStats = new DashboardProcurementStats(appCnt, partCnt, overduePoCnt);

                openPos = await (from p in poQuery
                                 join s in db.Sites.AsNoTracking() on new { p.OrganizationId, SiteId = p.SiteId } equals new { s.OrganizationId, SiteId = s.Id }
                                 join sup in db.Suppliers.AsNoTracking() on new { p.OrganizationId, SupplierId = p.SupplierId } equals new { sup.OrganizationId, SupplierId = sup.Id }
                                 orderby p.ExpectedDeliveryDate ?? DateOnly.MaxValue, p.Id descending
                                 select new DashboardPurchaseOrderItem(
                                     p.Id, p.OrderNumber, s.Code, sup.Name, p.ExpectedDeliveryDate, p.Status,
                                     p.ExpectedDeliveryDate != null && p.ExpectedDeliveryDate < today))
                    .Take(6)
                    .ToListAsync(ct);
            }

            IReadOnlyList<WorkCenterLoadSummary> wcLoads = [];
            if (capabilities.Routings)
            {
                var routingStore = new SqlRoutingOperationsStore(getFactory, errorRecorder, opContext);
                wcLoads = await routingStore.GetWorkCenterLoads(organizationId, siteId, ct);
            }

            return new DashboardSnapshot(
                capabilities,
                prodStats,
                attentionOrders,
                shortageStats,
                topShortages,
                qualityStats,
                pendingInspections,
                procStats,
                openPos,
                wcLoads);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            await StoreDiagnosticRecorder.RecordUnavailableAsync(errorRecorder, opContext, ex, nameof(SqlDashboardQueryStore));
            throw new ReportingStoreUnavailableException(ex);
        }
    }

    private static bool Unavailable(Exception exception) => exception switch
    {
        SqlException sql => sql.Number is -2 or 53 or 64 or 233 or 1205 or 4060 or 18456 or 40197 or 40501 or 40613,
        TimeoutException => true,
        DbUpdateException or RetryLimitExceededException when exception.InnerException is not null => Unavailable(exception.InnerException),
        InvalidOperationException when exception.Message == OrganizationAccessErrors.PersistenceUnavailable => true,
        _ => false
    };
}
