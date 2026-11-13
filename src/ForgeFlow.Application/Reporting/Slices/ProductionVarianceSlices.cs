using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Reporting;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Reporting.Slices;

/// <summary>
/// Operational analytics and CSV export slice for production order consumption and yield variance.
/// </summary>
public sealed class ProductionVarianceSlices(
    IReportingQueryStore store,
    ReportingSliceSupport support)
{
    private const int Scale = ReportingSliceSupport.Scale;

    public async Task<OrganizationResult<ProductionVarianceReport>> GetProductionVarianceReportAsync(
        OrganizationScope? scope,
        long organizationId,
        ProductionVarianceQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ReportingPermissions.ReportsRead, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionVarianceReport>.Failure(auth.Error);
        if (!ReportingSliceSupport.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<ProductionVarianceReport>.Failure(ReportingErrors.AccessDenied);

        try
        {
            var data = await store.GetVarianceDataset(
                organizationId, forcedSiteId, null, query.Status, query.DueFrom, query.DueTo, ReportingSliceSupport.NormalizeSearch(query.Search), ct);
            return OrganizationResult<ProductionVarianceReport>.Success(ComputeVarianceReport(data));
        }
        catch (ReportingStoreUnavailableException)
        {
            return OrganizationResult<ProductionVarianceReport>.Failure(ReportingErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<ProductionVarianceDetail>> GetProductionVarianceDetailAsync(
        OrganizationScope? scope,
        long organizationId,
        long productionOrderId,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ReportingPermissions.ReportsRead, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionVarianceDetail>.Failure(auth.Error);
        if (!ReportingSliceSupport.TryResolveScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<ProductionVarianceDetail>.Failure(ReportingErrors.AccessDenied);
        if (productionOrderId <= 0)
            return OrganizationResult<ProductionVarianceDetail>.Failure(ReportingErrors.ProductionOrderNotFound);

        try
        {
            var data = await store.GetVarianceDataset(organizationId, forcedSiteId, productionOrderId, null, null, null, null, ct);
            if (data.Orders.Count != 1)
                return OrganizationResult<ProductionVarianceDetail>.Failure(ReportingErrors.ProductionOrderNotFound);

            var detail = ComputeVarianceDetail(data.Orders[0], data);
            return OrganizationResult<ProductionVarianceDetail>.Success(detail);
        }
        catch (ReportingStoreUnavailableException)
        {
            return OrganizationResult<ProductionVarianceDetail>.Failure(ReportingErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<CsvExportFile>> ExportProductionVarianceCsvAsync(
        OrganizationScope? scope,
        long organizationId,
        ProductionVarianceQuery query,
        CsvExportColumnHeaders columns,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ReportingPermissions.ReportsExport, ct);
        if (auth.Error is not null)
        {
            await OperationAuditHelper.RecordAsync(support.OperationRecorder, support.OpContext, UserOperationCatalog.ReportingActionRejected,
                OperationAuditHelper.MapFailureOutcome(auth.Error), auth.Actor, scope, organizationId, query.SiteId,
                "ProductionVariance", null, auth.Error);
            return OrganizationResult<CsvExportFile>.Failure(auth.Error);
        }
        if (!ReportingSliceSupport.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.AccessDenied);
        if (columns.Headers.Count != 12 || string.IsNullOrWhiteSpace(columns.FileName))
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.ExportInvalid);

        try
        {
            var data = await store.GetVarianceDataset(
                organizationId, forcedSiteId, null, query.Status, query.DueFrom, query.DueTo, ReportingSliceSupport.NormalizeSearch(query.Search), ct);
            var report = ComputeVarianceReport(data);
            var rows = report.Orders.Select(o => (IReadOnlyList<string>)
            [
                o.OrderNumber,
                o.SiteCode,
                o.Status.ToString(),
                o.ItemCode,
                o.ItemName,
                o.BaseUnitSymbol,
                ReportingSliceSupport.FormatCsvDecimal(o.PlannedBaseQuantity),
                ReportingSliceSupport.FormatCsvDecimal(o.ActualProducedBaseQuantity),
                ReportingSliceSupport.FormatCsvDecimal(o.ActualScrapBaseQuantity),
                ReportingSliceSupport.FormatCsvDecimal(o.YieldPercent),
                ReportingSliceSupport.FormatCsvDecimal(o.TotalPlannedInputBaseQuantity),
                ReportingSliceSupport.FormatCsvDecimal(o.TotalIssuedInputBaseQuantity)
            ]);

            var file = CsvExportBuilder.Build(columns.FileName, columns.Headers, rows);
            await OperationAuditHelper.RecordAsync(support.OperationRecorder, support.OpContext, UserOperationCatalog.ReportsVarianceExported,
                UserOperationOutcome.Succeeded, auth.Actor, scope, organizationId, forcedSiteId,
                "ProductionVariance", file.FileName, null,
                new { OrderCount = report.Orders.Count });
            return OrganizationResult<CsvExportFile>.Success(file);
        }
        catch (ReportingStoreUnavailableException)
        {
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.PersistenceUnavailable);
        }
    }

    public static ProductionVarianceReport ComputeVarianceReport(VarianceDataset data)
    {
        var itemsById = data.Items.ToDictionary(x => x.ItemId);
        var edgeCache = ReportingSliceSupport.BuildEdgeCache(data.Conversions);
        var inputsByOrder = data.Inputs.GroupBy(i => i.ProductionOrderId).ToDictionary(g => g.Key, g => g.ToArray());
        var ledgerByOrder = data.LedgerRows.GroupBy(l => l.ProductionOrderId).ToDictionary(g => g.Key, g => g.ToArray());

        var summaries = data.Orders
            .Select(o => BuildSummary(
                o,
                itemsById,
                edgeCache,
                inputsByOrder.TryGetValue(o.ProductionOrderId, out var ins) ? ins : [],
                ledgerByOrder.TryGetValue(o.ProductionOrderId, out var leds) ? leds : []))
            .OrderByDescending(s => s.ProductionOrderId)
            .ToArray();

        var overConsumed = summaries.Count(s => s.TotalInputVarianceBaseQuantity > 0.000001m);
        var underYield = summaries.Count(s => s.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.InProgress && s.YieldPercent < 100m);
        var avgYield = summaries.Length > 0
            ? decimal.Round(summaries.Average(s => s.YieldPercent), 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new ProductionVarianceReport(
            summaries.Length,
            overConsumed,
            underYield,
            avgYield,
            summaries);
    }

    public static ProductionVarianceDetail ComputeVarianceDetail(ReportVarianceOrderRow order, VarianceDataset data)
    {
        var itemsById = data.Items.ToDictionary(x => x.ItemId);
        var edgeCache = ReportingSliceSupport.BuildEdgeCache(data.Conversions);
        var orderInputs = data.Inputs.Where(i => i.ProductionOrderId == order.ProductionOrderId).ToArray();
        var orderOutputs = data.Outputs.Where(o => o.ProductionOrderId == order.ProductionOrderId).ToArray();
        var orderLedger = data.LedgerRows.Where(l => l.ProductionOrderId == order.ProductionOrderId).ToArray();

        var summary = BuildSummary(order, itemsById, edgeCache, orderInputs, orderLedger);

        var issuedByItem = orderLedger
            .Where(l => l.RootTransactionType == InventoryTransactionType.ProductionIssue)
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => decimal.Round(-g.Sum(x => x.BaseQuantityDelta), Scale, MidpointRounding.AwayFromZero));

        var receivedByItem = orderLedger
            .Where(l => l.RootTransactionType == InventoryTransactionType.ProductionReceipt)
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => decimal.Round(g.Sum(x => x.BaseQuantityDelta), Scale, MidpointRounding.AwayFromZero));

        var plannedInputByItem = new Dictionary<long, decimal>();
        foreach (var line in orderInputs.Where(i => !i.IsSubAssembly))
        {
            if (!itemsById.TryGetValue(line.ItemId, out var mat))
                continue;
            var qty = ReportingSliceSupport.ConvertToBase(line.GrossQuantity, line.UnitId, mat.ItemId, mat.BaseUnitId, edgeCache);
            plannedInputByItem[line.ItemId] = plannedInputByItem.TryGetValue(line.ItemId, out var cur) ? cur + qty : qty;
        }

        var allInputItems = new HashSet<long>(plannedInputByItem.Keys);
        foreach (var issuedItemId in issuedByItem.Keys)
            allInputItems.Add(issuedItemId);

        var inputLines = allInputItems
            .Where(id => itemsById.ContainsKey(id))
            .Select(id =>
            {
                var mat = itemsById[id];
                var planned = plannedInputByItem.TryGetValue(id, out var p) ? decimal.Round(p, Scale, MidpointRounding.AwayFromZero) : 0m;
                var issued = issuedByItem.TryGetValue(id, out var iss) ? decimal.Round(iss, Scale, MidpointRounding.AwayFromZero) : 0m;
                var variance = decimal.Round(issued - planned, Scale, MidpointRounding.AwayFromZero);
                var remaining = planned > issued ? decimal.Round(planned - issued, Scale, MidpointRounding.AwayFromZero) : 0m;
                var pct = planned > 0m ? decimal.Round((issued / planned) * 100m, 2, MidpointRounding.AwayFromZero) : (issued > 0m ? 100m : 0m);
                return new ProductionInputVarianceLine(
                    mat.ItemId, mat.ItemCode, mat.ItemName,
                    mat.BaseUnitId, mat.BaseUnitCode, mat.BaseUnitSymbol,
                    planned, issued, variance, remaining, pct);
            })
            .OrderBy(l => l.ItemCode, StringComparer.Ordinal)
            .ToArray();

        var plannedOutputGroups = new Dictionary<(long ItemId, BomOutputType Type), decimal>();
        foreach (var line in orderOutputs)
        {
            if (!itemsById.TryGetValue(line.ItemId, out var outItem))
                continue;
            var qty = ReportingSliceSupport.ConvertToBase(line.PlannedQuantity, line.UnitId, outItem.ItemId, outItem.BaseUnitId, edgeCache);
            var key = (line.ItemId, line.OutputType);
            plannedOutputGroups[key] = plannedOutputGroups.TryGetValue(key, out var cur) ? cur + qty : qty;
        }

        var outputLines = plannedOutputGroups
            .Where(kv => itemsById.ContainsKey(kv.Key.ItemId))
            .Select(kv =>
            {
                var outItem = itemsById[kv.Key.ItemId];
                var planned = decimal.Round(kv.Value, Scale, MidpointRounding.AwayFromZero);
                var received = receivedByItem.TryGetValue(outItem.ItemId, out var rec) ? rec : 0m;
                var variance = decimal.Round(received - planned, Scale, MidpointRounding.AwayFromZero);
                var achievement = planned > 0m ? decimal.Round((received / planned) * 100m, 2, MidpointRounding.AwayFromZero) : 0m;
                return new ProductionOutputVarianceLine(
                    outItem.ItemId, outItem.ItemCode, outItem.ItemName, kv.Key.Type,
                    outItem.BaseUnitId, outItem.BaseUnitCode, outItem.BaseUnitSymbol,
                    planned, received, variance, achievement);
            })
            .OrderBy(o => o.OutputType)
            .ThenBy(o => o.ItemCode, StringComparer.Ordinal)
            .ToArray();

        return new ProductionVarianceDetail(summary, inputLines, outputLines);
    }

    private static ProductionVarianceSummary BuildSummary(
        ReportVarianceOrderRow order,
        IReadOnlyDictionary<long, ReportItemRef> itemsById,
        Dictionary<long, IReadOnlyList<UnitConversionEdge>> edgeCache,
        IReadOnlyList<ReportVarianceInputRow> inputs,
        IReadOnlyList<ReportOrderLedgerRow> ledger)
    {
        var fgItem = itemsById.TryGetValue(order.ItemId, out var item)
            ? item
            : new ReportItemRef(order.ItemId, $"#{order.ItemId}", $"#{order.ItemId}", order.RequestedUnitId, "-", "-");

        var plannedBase = ReportingSliceSupport.ConvertToBase(order.RequestedQuantity, order.RequestedUnitId, fgItem.ItemId, fgItem.BaseUnitId, edgeCache);
        var actualProducedBase = order.ActualProducedQuantity > 0m
            ? ReportingSliceSupport.ConvertToBase(order.ActualProducedQuantity, order.RequestedUnitId, fgItem.ItemId, fgItem.BaseUnitId, edgeCache)
            : 0m;
        var actualScrapBase = order.ActualScrapQuantity > 0m
            ? ReportingSliceSupport.ConvertToBase(order.ActualScrapQuantity, order.RequestedUnitId, fgItem.ItemId, fgItem.BaseUnitId, edgeCache)
            : 0m;

        var postedReceivedBase = decimal.Round(
            ledger.Where(l => l.RootTransactionType == InventoryTransactionType.ProductionReceipt && l.ItemId == order.ItemId)
                .Sum(l => l.BaseQuantityDelta),
            Scale, MidpointRounding.AwayFromZero);

        var outputVariance = decimal.Round(actualProducedBase - plannedBase, Scale, MidpointRounding.AwayFromZero);
        var yieldPercent = plannedBase > 0m
            ? decimal.Round((actualProducedBase / plannedBase) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var plannedInputsByItem = new Dictionary<long, decimal>();
        foreach (var line in inputs.Where(i => !i.IsSubAssembly))
        {
            if (!itemsById.TryGetValue(line.ItemId, out var mat))
                continue;
            var baseQty = ReportingSliceSupport.ConvertToBase(line.GrossQuantity, line.UnitId, mat.ItemId, mat.BaseUnitId, edgeCache);
            plannedInputsByItem[line.ItemId] = plannedInputsByItem.TryGetValue(line.ItemId, out var cur) ? cur + baseQty : baseQty;
        }

        var issuedByItem = ledger
            .Where(l => l.RootTransactionType == InventoryTransactionType.ProductionIssue)
            .GroupBy(l => l.ItemId)
            .ToDictionary(g => g.Key, g => decimal.Round(-g.Sum(x => x.BaseQuantityDelta), Scale, MidpointRounding.AwayFromZero));

        var totalPlannedInputs = decimal.Round(plannedInputsByItem.Values.Sum(), Scale, MidpointRounding.AwayFromZero);
        var totalIssuedInputs = decimal.Round(issuedByItem.Values.Sum(), Scale, MidpointRounding.AwayFromZero);
        var totalInputVariance = decimal.Round(totalIssuedInputs - totalPlannedInputs, Scale, MidpointRounding.AwayFromZero);
        var unissuedCount = plannedInputsByItem.Count(kv => (issuedByItem.TryGetValue(kv.Key, out var iss) ? iss : 0m) + 0.000001m < kv.Value);

        return new ProductionVarianceSummary(
            order.ProductionOrderId,
            order.OrganizationId,
            order.SiteId,
            order.SiteCode,
            order.SiteName,
            order.OrderNumber,
            order.Status,
            fgItem.ItemId,
            fgItem.ItemCode,
            fgItem.ItemName,
            fgItem.BaseUnitId,
            fgItem.BaseUnitCode,
            fgItem.BaseUnitSymbol,
            order.PlannedStartDate,
            order.PlannedDueDate,
            decimal.Round(plannedBase, Scale, MidpointRounding.AwayFromZero),
            decimal.Round(actualProducedBase, Scale, MidpointRounding.AwayFromZero),
            decimal.Round(actualScrapBase, Scale, MidpointRounding.AwayFromZero),
            postedReceivedBase,
            outputVariance,
            yieldPercent,
            totalPlannedInputs,
            totalIssuedInputs,
            totalInputVariance,
            unissuedCount);
    }
}
