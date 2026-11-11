using System.Globalization;
using System.Text;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Reporting;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Reporting;

/// <summary>
/// Read-only operational analytics and authorized CSV exports:
/// Material Shortage / Net Requirements and Production Consumption / Yield Variance.
/// </summary>
public sealed class ReportingUseCases(
    IReportingQueryStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    private const int Scale = 6;

    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, ReportingErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, ReportingErrors.ActorRequired);
        if (scope is null) return (null, ReportingErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, ReportingErrors.AccessDenied);
        return (actor, null);
    }

    private static bool TryResolveScope(OrganizationScope? scope, long requestedOrgId, long? requestedSiteId, out long? forcedSiteId)
    {
        forcedSiteId = null;
        if (requestedOrgId <= 0) return false;
        switch (scope)
        {
            case OrganizationScope.Organization org when org.OrganizationId == requestedOrgId:
                forcedSiteId = requestedSiteId;
                return true;
            case OrganizationScope.Site site when site.OrganizationId == requestedOrgId:
                if (requestedSiteId.HasValue && requestedSiteId.Value != site.SiteId)
                    return false;
                forcedSiteId = site.SiteId;
                return true;
            default:
                return false;
        }
    }

    public async Task<OrganizationResult<IReadOnlyList<ReportSiteInfo>>> ListAuthorizedSites(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ReportingPermissions.ReportsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<ReportSiteInfo>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<ReportSiteInfo>>.Failure(ReportingErrors.AccessDenied);

        try
        {
            var sites = await store.ListAuthorizedSites(organizationId, forcedSiteId, ct);
            return OrganizationResult<IReadOnlyList<ReportSiteInfo>>.Success(sites);
        }
        catch (ReportingStoreUnavailableException)
        {
            return OrganizationResult<IReadOnlyList<ReportSiteInfo>>.Failure(ReportingErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<MaterialShortageReport>> GetMaterialShortageReport(
        OrganizationScope? scope,
        long organizationId,
        MaterialShortageQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ReportingPermissions.ReportsRead, ct);
        if (auth.Error is not null) return OrganizationResult<MaterialShortageReport>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<MaterialShortageReport>.Failure(ReportingErrors.AccessDenied);

        try
        {
            var data = await store.GetShortageDataset(organizationId, forcedSiteId, query.DueFrom, query.DueTo, ct);
            return OrganizationResult<MaterialShortageReport>.Success(ComputeMaterialShortage(data, query));
        }
        catch (ReportingStoreUnavailableException)
        {
            return OrganizationResult<MaterialShortageReport>.Failure(ReportingErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<ProductionVarianceReport>> GetProductionVarianceReport(
        OrganizationScope? scope,
        long organizationId,
        ProductionVarianceQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ReportingPermissions.ReportsRead, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionVarianceReport>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<ProductionVarianceReport>.Failure(ReportingErrors.AccessDenied);

        try
        {
            var data = await store.GetVarianceDataset(
                organizationId, forcedSiteId, null, query.Status, query.DueFrom, query.DueTo, NormalizeSearch(query.Search), ct);
            return OrganizationResult<ProductionVarianceReport>.Success(ComputeVarianceReport(data));
        }
        catch (ReportingStoreUnavailableException)
        {
            return OrganizationResult<ProductionVarianceReport>.Failure(ReportingErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<ProductionVarianceDetail>> GetProductionVarianceDetail(
        OrganizationScope? scope,
        long organizationId,
        long productionOrderId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ReportingPermissions.ReportsRead, ct);
        if (auth.Error is not null) return OrganizationResult<ProductionVarianceDetail>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId))
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

    public async Task<OrganizationResult<CsvExportFile>> ExportMaterialShortageCsv(
        OrganizationScope? scope,
        long organizationId,
        MaterialShortageQuery query,
        CsvExportColumnHeaders columns,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ReportingPermissions.ReportsExport, ct);
        if (auth.Error is not null)
        {
            await OperationAuditHelper.RecordAsync(operationRecorder, opContext, UserOperationCatalog.ReportingActionRejected,
                OperationAuditHelper.MapFailureOutcome(auth.Error), auth.Actor, scope, organizationId, query.SiteId,
                "MaterialShortage", null, auth.Error);
            return OrganizationResult<CsvExportFile>.Failure(auth.Error);
        }
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.AccessDenied);
        if (columns.Headers.Count is not (10 or 12) || string.IsNullOrWhiteSpace(columns.FileName))
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.ExportInvalid);

        try
        {
            var data = await store.GetShortageDataset(organizationId, forcedSiteId, query.DueFrom, query.DueTo, ct);
            var report = ComputeMaterialShortage(data, query);
            var rows = columns.Headers.Count == 12
                ? report.Items.Select(i => (IReadOnlyList<string>)
                [
                    i.ItemCode,
                    i.ItemName,
                    i.BaseUnitSymbol,
                    FormatCsvDecimal(i.RequiredBaseQuantity),
                    FormatCsvDecimal(i.OnHandBaseQuantity),
                    FormatCsvDecimal(i.BlockedBaseQuantity),
                    FormatCsvDecimal(i.AvailableBaseQuantity),
                    FormatCsvDecimal(i.ShortageBaseQuantity),
                    FormatCsvDecimal(i.ExcessBaseQuantity),
                    FormatCsvDecimal(i.CoveragePercent),
                    i.OpenOrderCount.ToString(CultureInfo.InvariantCulture),
                    string.Join(" | ", i.Orders.Select(o => $"{o.OrderNumber}({o.SiteCode})"))
                ])
                : report.Items.Select(i => (IReadOnlyList<string>)
                [
                    i.ItemCode,
                    i.ItemName,
                    i.BaseUnitSymbol,
                    FormatCsvDecimal(i.RequiredBaseQuantity),
                    FormatCsvDecimal(i.OnHandBaseQuantity),
                    FormatCsvDecimal(i.ShortageBaseQuantity),
                    FormatCsvDecimal(i.ExcessBaseQuantity),
                    FormatCsvDecimal(i.CoveragePercent),
                    i.OpenOrderCount.ToString(CultureInfo.InvariantCulture),
                    string.Join(" | ", i.Orders.Select(o => $"{o.OrderNumber}({o.SiteCode})"))
                ]);

            var file = BuildCsvFile(columns.FileName, columns.Headers, rows);
            await OperationAuditHelper.RecordAsync(operationRecorder, opContext, UserOperationCatalog.ReportsShortageExported,
                UserOperationOutcome.Succeeded, auth.Actor, scope, organizationId, forcedSiteId,
                "MaterialShortage", file.FileName, null,
                new { MaterialCount = report.Items.Count, ShortageCount = report.ShortageMaterialCount });
            return OrganizationResult<CsvExportFile>.Success(file);
        }
        catch (ReportingStoreUnavailableException)
        {
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<CsvExportFile>> ExportProductionVarianceCsv(
        OrganizationScope? scope,
        long organizationId,
        ProductionVarianceQuery query,
        CsvExportColumnHeaders columns,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, ReportingPermissions.ReportsExport, ct);
        if (auth.Error is not null)
        {
            await OperationAuditHelper.RecordAsync(operationRecorder, opContext, UserOperationCatalog.ReportingActionRejected,
                OperationAuditHelper.MapFailureOutcome(auth.Error), auth.Actor, scope, organizationId, query.SiteId,
                "ProductionVariance", null, auth.Error);
            return OrganizationResult<CsvExportFile>.Failure(auth.Error);
        }
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.AccessDenied);
        if (columns.Headers.Count != 12 || string.IsNullOrWhiteSpace(columns.FileName))
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.ExportInvalid);

        try
        {
            var data = await store.GetVarianceDataset(
                organizationId, forcedSiteId, null, query.Status, query.DueFrom, query.DueTo, NormalizeSearch(query.Search), ct);
            var report = ComputeVarianceReport(data);
            var rows = report.Orders.Select(o => (IReadOnlyList<string>)
            [
                o.OrderNumber,
                o.SiteCode,
                o.Status.ToString(),
                o.ItemCode,
                o.ItemName,
                o.BaseUnitSymbol,
                FormatCsvDecimal(o.PlannedBaseQuantity),
                FormatCsvDecimal(o.ActualProducedBaseQuantity),
                FormatCsvDecimal(o.ActualScrapBaseQuantity),
                FormatCsvDecimal(o.YieldPercent),
                FormatCsvDecimal(o.TotalPlannedInputBaseQuantity),
                FormatCsvDecimal(o.TotalIssuedInputBaseQuantity)
            ]);

            var file = BuildCsvFile(columns.FileName, columns.Headers, rows);
            await OperationAuditHelper.RecordAsync(operationRecorder, opContext, UserOperationCatalog.ReportsVarianceExported,
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

    public static MaterialShortageReport ComputeMaterialShortage(ShortageDataset data, MaterialShortageQuery query)
    {
        var itemsById = data.Items.ToDictionary(x => x.ItemId);
        var ordersById = data.Orders.ToDictionary(x => x.ProductionOrderId);
        var edgeCache = BuildEdgeCache(data.Conversions);

        var demandByItemOrder = new Dictionary<(long ItemId, long OrderId), decimal>();
        foreach (var line in data.Inputs)
        {
            if (!ordersById.ContainsKey(line.ProductionOrderId) || !itemsById.TryGetValue(line.ItemId, out var item))
                continue;

            var baseQty = ConvertToBase(line.GrossQuantity, line.UnitId, item.ItemId, item.BaseUnitId, edgeCache);
            var key = (line.ItemId, line.ProductionOrderId);
            demandByItemOrder[key] = demandByItemOrder.TryGetValue(key, out var cur) ? cur + baseQty : baseQty;
        }

        var onHandByItem = data.Balances
            .GroupBy(b => b.ItemId)
            .ToDictionary(g => g.Key, g => (
                OnHand: decimal.Round(g.Sum(x => x.OnHandBaseQuantity), Scale, MidpointRounding.AwayFromZero),
                Blocked: decimal.Round(g.Sum(x => x.BlockedBaseQuantity), Scale, MidpointRounding.AwayFromZero)));

        var demandGrouped = demandByItemOrder
            .GroupBy(kv => kv.Key.ItemId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var candidateItemIds = new HashSet<long>(demandGrouped.Keys);
        if (!query.OnlyShortages)
        {
            foreach (var stockItemId in onHandByItem.Keys)
                candidateItemIds.Add(stockItemId);
        }

        var search = NormalizeSearch(query.Search);
        var rows = new List<MaterialShortageItemSummary>();
        foreach (var itemId in candidateItemIds)
        {
            if (!itemsById.TryGetValue(itemId, out var item))
                continue;

            if (search is not null
                && !item.ItemCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                && !item.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var orderDetails = demandGrouped.TryGetValue(itemId, out var rawOrders)
                ? rawOrders
                    .Select(kv =>
                    {
                        var o = ordersById[kv.Key.OrderId];
                        return new MaterialShortageOrderDetail(
                            o.ProductionOrderId,
                            o.OrderNumber,
                            o.SiteId,
                            o.SiteCode,
                            o.PlannedDueDate,
                            o.Status,
                            decimal.Round(kv.Value, Scale, MidpointRounding.AwayFromZero));
                    })
                    .OrderBy(o => o.PlannedDueDate ?? DateOnly.MaxValue)
                    .ThenBy(o => o.OrderNumber, StringComparer.Ordinal)
                    .ToArray()
                : [];

            var required = decimal.Round(orderDetails.Sum(x => x.RequiredBaseQuantity), Scale, MidpointRounding.AwayFromZero);
            var stock = onHandByItem.TryGetValue(itemId, out var bal) ? bal : (OnHand: 0m, Blocked: 0m);
            var onHand = stock.OnHand;
            var blocked = Math.Min(onHand, Math.Max(0m, stock.Blocked));
            var available = decimal.Round(Math.Max(0m, onHand - blocked), Scale, MidpointRounding.AwayFromZero);
            var shortage = required > available ? decimal.Round(required - available, Scale, MidpointRounding.AwayFromZero) : 0m;
            var excess = available > required ? decimal.Round(available - required, Scale, MidpointRounding.AwayFromZero) : 0m;

            if (query.OnlyShortages && shortage <= 0m)
                continue;
            if (required <= 0m && onHand <= 0m)
                continue;

            var coverage = required > 0m
                ? Math.Min(100m, decimal.Round((available / required) * 100m, 2, MidpointRounding.AwayFromZero))
                : 100m;

            rows.Add(new MaterialShortageItemSummary(
                item.ItemId,
                item.ItemCode,
                item.ItemName,
                item.BaseUnitId,
                item.BaseUnitCode,
                item.BaseUnitSymbol,
                required,
                onHand,
                shortage,
                excess,
                coverage,
                orderDetails.Length,
                orderDetails,
                blocked,
                available));
        }

        var ordered = rows
            .OrderByDescending(r => r.ShortageBaseQuantity)
            .ThenBy(r => r.ItemCode, StringComparer.Ordinal)
            .ToArray();

        return new MaterialShortageReport(
            data.Orders.Count,
            ordered.Length,
            ordered.Count(r => r.ShortageBaseQuantity > 0m),
            decimal.Round(ordered.Sum(r => r.RequiredBaseQuantity), Scale, MidpointRounding.AwayFromZero),
            decimal.Round(ordered.Sum(r => r.OnHandBaseQuantity), Scale, MidpointRounding.AwayFromZero),
            decimal.Round(ordered.Sum(r => r.ShortageBaseQuantity), Scale, MidpointRounding.AwayFromZero),
            ordered,
            decimal.Round(ordered.Sum(r => r.BlockedBaseQuantity), Scale, MidpointRounding.AwayFromZero),
            decimal.Round(ordered.Sum(r => r.AvailableBaseQuantity), Scale, MidpointRounding.AwayFromZero));
    }

    public static ProductionVarianceReport ComputeVarianceReport(VarianceDataset data)
    {
        var itemsById = data.Items.ToDictionary(x => x.ItemId);
        var edgeCache = BuildEdgeCache(data.Conversions);
        var inputsByOrder = data.Inputs.GroupBy(i => i.ProductionOrderId).ToDictionary(g => g.Key, g => g.ToArray());
        var outputsByOrder = data.Outputs.GroupBy(o => o.ProductionOrderId).ToDictionary(g => g.Key, g => g.ToArray());
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
        var edgeCache = BuildEdgeCache(data.Conversions);
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
            var qty = ConvertToBase(line.GrossQuantity, line.UnitId, mat.ItemId, mat.BaseUnitId, edgeCache);
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
            var qty = ConvertToBase(line.PlannedQuantity, line.UnitId, outItem.ItemId, outItem.BaseUnitId, edgeCache);
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

        var plannedBase = ConvertToBase(order.RequestedQuantity, order.RequestedUnitId, fgItem.ItemId, fgItem.BaseUnitId, edgeCache);
        var actualProducedBase = order.ActualProducedQuantity > 0m
            ? ConvertToBase(order.ActualProducedQuantity, order.RequestedUnitId, fgItem.ItemId, fgItem.BaseUnitId, edgeCache)
            : 0m;
        var actualScrapBase = order.ActualScrapQuantity > 0m
            ? ConvertToBase(order.ActualScrapQuantity, order.RequestedUnitId, fgItem.ItemId, fgItem.BaseUnitId, edgeCache)
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
            var baseQty = ConvertToBase(line.GrossQuantity, line.UnitId, mat.ItemId, mat.BaseUnitId, edgeCache);
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

    public static CsvExportFile BuildCsvFile(
        string fileName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows) =>
        CsvExportBuilder.Build(fileName, headers, rows);

    public static string EscapeCsvCell(string? raw) =>
        CsvExportBuilder.EscapeCell(raw);

    private static string FormatCsvDecimal(decimal value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static Dictionary<long, IReadOnlyList<UnitConversionEdge>> BuildEdgeCache(IReadOnlyList<ReportConversionEdgeRow> conversions)
    {
        var global = conversions
            .Where(c => !c.ItemId.HasValue)
            .Select(c => new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, IsItemSpecific: false))
            .ToArray();

        var cache = new Dictionary<long, IReadOnlyList<UnitConversionEdge>> { [0] = global };
        foreach (var group in conversions.Where(c => c.ItemId.HasValue).GroupBy(c => c.ItemId!.Value))
        {
            var specific = group
                .Select(c => new UnitConversionEdge(c.FromUnitId, c.ToUnitId, c.Factor, IsItemSpecific: true))
                .ToArray();
            cache[group.Key] = [.. specific, .. global];
        }
        return cache;
    }

    private static decimal ConvertToBase(
        decimal quantity,
        long fromUnitId,
        long itemId,
        long baseUnitId,
        Dictionary<long, IReadOnlyList<UnitConversionEdge>> edgeCache)
    {
        if (quantity == 0m) return 0m;
        if (fromUnitId == baseUnitId)
            return decimal.Round(quantity, Scale, MidpointRounding.AwayFromZero);

        var edges = edgeCache.TryGetValue(itemId, out var specific) ? specific : edgeCache[0];
        var res = UnitConversionGraph.ConvertQuantity(Math.Abs(quantity), fromUnitId, baseUnitId, edges);
        var signed = quantity < 0m ? -res.ConvertedQuantity : res.ConvertedQuantity;
        return decimal.Round(signed, Scale, MidpointRounding.AwayFromZero);
    }

    private static string? NormalizeSearch(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}
