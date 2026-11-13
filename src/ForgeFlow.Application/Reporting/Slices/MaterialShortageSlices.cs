using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Reporting;

namespace ForgeFlow.Application.Reporting.Slices;

/// <summary>
/// Operational analytics and CSV export slice for material shortages and net inventory requirements.
/// </summary>
public sealed class MaterialShortageSlices(
    IReportingQueryStore store,
    ReportingSliceSupport support)
{
    private const int Scale = ReportingSliceSupport.Scale;

    public async Task<OrganizationResult<MaterialShortageReport>> GetMaterialShortageReportAsync(
        OrganizationScope? scope,
        long organizationId,
        MaterialShortageQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ReportingPermissions.ReportsRead, ct);
        if (auth.Error is not null) return OrganizationResult<MaterialShortageReport>.Failure(auth.Error);
        if (!ReportingSliceSupport.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
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

    public async Task<OrganizationResult<CsvExportFile>> ExportMaterialShortageCsvAsync(
        OrganizationScope? scope,
        long organizationId,
        MaterialShortageQuery query,
        CsvExportColumnHeaders columns,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, ReportingPermissions.ReportsExport, ct);
        if (auth.Error is not null)
        {
            await OperationAuditHelper.RecordAsync(support.OperationRecorder, support.OpContext, UserOperationCatalog.ReportingActionRejected,
                OperationAuditHelper.MapFailureOutcome(auth.Error), auth.Actor, scope, organizationId, query.SiteId,
                "MaterialShortage", null, auth.Error);
            return OrganizationResult<CsvExportFile>.Failure(auth.Error);
        }
        if (!ReportingSliceSupport.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.AccessDenied);
        if (columns.Headers.Count is not (10 or 12 or 13) || string.IsNullOrWhiteSpace(columns.FileName))
            return OrganizationResult<CsvExportFile>.Failure(ReportingErrors.ExportInvalid);

        try
        {
            var data = await store.GetShortageDataset(organizationId, forcedSiteId, query.DueFrom, query.DueTo, ct);
            var report = ComputeMaterialShortage(data, query);
            var rows = columns.Headers.Count switch
            {
                13 => report.Items.Select(i => (IReadOnlyList<string>)
                [
                    i.ItemCode,
                    i.ItemName,
                    i.BaseUnitSymbol,
                    ReportingSliceSupport.FormatCsvDecimal(i.RequiredBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.OnHandBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.BlockedBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.AvailableBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.OnOrderBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.ShortageBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.ExcessBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.CoveragePercent),
                    i.OpenOrderCount.ToString(CultureInfo.InvariantCulture),
                    string.Join(" | ", i.Orders.Select(o => $"{o.OrderNumber}({o.SiteCode})"))
                ]),
                12 => report.Items.Select(i => (IReadOnlyList<string>)
                [
                    i.ItemCode,
                    i.ItemName,
                    i.BaseUnitSymbol,
                    ReportingSliceSupport.FormatCsvDecimal(i.RequiredBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.OnHandBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.BlockedBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.AvailableBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.ShortageBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.ExcessBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.CoveragePercent),
                    i.OpenOrderCount.ToString(CultureInfo.InvariantCulture),
                    string.Join(" | ", i.Orders.Select(o => $"{o.OrderNumber}({o.SiteCode})"))
                ]),
                _ => report.Items.Select(i => (IReadOnlyList<string>)
                [
                    i.ItemCode,
                    i.ItemName,
                    i.BaseUnitSymbol,
                    ReportingSliceSupport.FormatCsvDecimal(i.RequiredBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.OnHandBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.ShortageBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.ExcessBaseQuantity),
                    ReportingSliceSupport.FormatCsvDecimal(i.CoveragePercent),
                    i.OpenOrderCount.ToString(CultureInfo.InvariantCulture),
                    string.Join(" | ", i.Orders.Select(o => $"{o.OrderNumber}({o.SiteCode})"))
                ])
            };

            var file = CsvExportBuilder.Build(columns.FileName, columns.Headers, rows);
            await OperationAuditHelper.RecordAsync(support.OperationRecorder, support.OpContext, UserOperationCatalog.ReportsShortageExported,
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

    public static MaterialShortageReport ComputeMaterialShortage(ShortageDataset data, MaterialShortageQuery query)
    {
        var itemsById = data.Items.ToDictionary(x => x.ItemId);
        var ordersById = data.Orders.ToDictionary(x => x.ProductionOrderId);
        var edgeCache = ReportingSliceSupport.BuildEdgeCache(data.Conversions);

        var demandByItemOrder = new Dictionary<(long ItemId, long OrderId), decimal>();
        foreach (var line in data.Inputs)
        {
            if (!ordersById.ContainsKey(line.ProductionOrderId) || !itemsById.TryGetValue(line.ItemId, out var item))
                continue;

            var baseQty = ReportingSliceSupport.ConvertToBase(line.GrossQuantity, line.UnitId, item.ItemId, item.BaseUnitId, edgeCache);
            var key = (line.ItemId, line.ProductionOrderId);
            demandByItemOrder[key] = demandByItemOrder.TryGetValue(key, out var cur) ? cur + baseQty : baseQty;
        }

        var onHandByItem = data.Balances
            .GroupBy(b => b.ItemId)
            .ToDictionary(g => g.Key, g => (
                OnHand: decimal.Round(g.Sum(x => x.OnHandBaseQuantity), Scale, MidpointRounding.AwayFromZero),
                Blocked: decimal.Round(g.Sum(x => x.BlockedBaseQuantity), Scale, MidpointRounding.AwayFromZero),
                Reserved: decimal.Round(g.Sum(x => x.ReservedBaseQuantity), Scale, MidpointRounding.AwayFromZero)));

        var onOrderByItem = (data.OpenPurchaseLines ?? [])
            .GroupBy(p => p.ItemId)
            .ToDictionary(g => g.Key, g => decimal.Round(g.Sum(x => x.RemainingBaseQuantity), Scale, MidpointRounding.AwayFromZero));

        var demandGrouped = demandByItemOrder
            .GroupBy(kv => kv.Key.ItemId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var candidateItemIds = new HashSet<long>(demandGrouped.Keys);
        if (!query.OnlyShortages)
        {
            foreach (var stockItemId in onHandByItem.Keys)
                candidateItemIds.Add(stockItemId);
            foreach (var orderItemId in onOrderByItem.Keys)
                candidateItemIds.Add(orderItemId);
        }

        var search = ReportingSliceSupport.NormalizeSearch(query.Search);
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
            var stock = onHandByItem.TryGetValue(itemId, out var bal) ? bal : (OnHand: 0m, Blocked: 0m, Reserved: 0m);
            var onHand = stock.OnHand;
            var blocked = Math.Min(onHand, Math.Max(0m, stock.Blocked));
            var reserved = Math.Min(Math.Max(0m, onHand - blocked), Math.Max(0m, stock.Reserved));
            var available = decimal.Round(Math.Max(0m, onHand - blocked - reserved), Scale, MidpointRounding.AwayFromZero);
            var onOrder = onOrderByItem.TryGetValue(itemId, out var oo) ? Math.Max(0m, oo) : 0m;
            var shortage = required > available ? decimal.Round(required - available, Scale, MidpointRounding.AwayFromZero) : 0m;
            var excess = available > required ? decimal.Round(available - required, Scale, MidpointRounding.AwayFromZero) : 0m;

            if (query.OnlyShortages && shortage <= 0m)
                continue;
            if (required <= 0m && onHand <= 0m && onOrder <= 0m)
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
                available,
                onOrder,
                reserved));
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
            decimal.Round(ordered.Sum(r => r.AvailableBaseQuantity), Scale, MidpointRounding.AwayFromZero),
            decimal.Round(ordered.Sum(r => r.OnOrderBaseQuantity), Scale, MidpointRounding.AwayFromZero),
            decimal.Round(ordered.Sum(r => r.ReservedBaseQuantity), Scale, MidpointRounding.AwayFromZero));
    }
}
