using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Quality.Slices;

public sealed class QualityGenealogySlices(
    IQualityOperationsStore store,
    QualitySliceSupport support)
{
    private const int MaxGenealogyDepth = 15;
    private const int MaxGenealogyEdges = 500;
    private DateOnly TodayUtc => DateOnly.FromDateTime(support.UtcNow);

    public async Task<OrganizationResult<OrganizationPage<ItemLotSummary>>> ListItemLotsAsync(
        OrganizationScope? scope,
        long organizationId,
        ItemLotListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, QualityPermissions.QualityRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ItemLotSummary>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, null, out _))
            return OrganizationResult<OrganizationPage<ItemLotSummary>>.Failure(QualityErrors.AccessDenied);

        var bounded = query with
        {
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<ItemLotSummary>>.Success(
                await store.ListItemLots(organizationId, bounded, TodayUtc, ct)));
    }

    public async Task<OrganizationResult<ItemLotSummary>> UpsertItemLotMetadataAsync(
        OrganizationScope? scope,
        UpsertItemLotMetadataRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<ItemLotSummary> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<ItemLotSummary>.Failure(auth.Error);
        }
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out _))
        {
            res = OrganizationResult<ItemLotSummary>.Failure(QualityErrors.AccessDenied);
        }
        else if (request.ItemId <= 0)
        {
            res = OrganizationResult<ItemLotSummary>.Failure(QualityErrors.ItemInvalid);
        }
        else
        {
            res = await support.ExecuteStoreAsync(() =>
            {
                var normalizedLot = QualityText.RequiredLotCode(request.LotCode);
                QualityText.ValidateDates(request.ManufactureDate, request.ExpirationDate);
                var cleanNotes = QualityText.OptionalNote(request.Notes, QualityText.MaxLotNoteLength);
                var normalized = request with { LotCode = normalizedLot, Notes = cleanNotes };
                return store.UpsertItemLotMetadata(normalized, TodayUtc, support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct);
            });
        }

        await support.AuditFailureIfRejected(res, auth.Actor, scope, request.OrganizationId, null, "ItemLot", request.LotCode, "UpsertItemLotMetadata");
        return res;
    }

    public async Task<OrganizationResult<LotTraceabilityReport>> TraceLotAsync(
        OrganizationScope? scope,
        long organizationId,
        LotTraceQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, QualityPermissions.QualityRead, ct);
        if (auth.Error is not null) return OrganizationResult<LotTraceabilityReport>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<LotTraceabilityReport>.Failure(QualityErrors.AccessDenied);
        if (query.ItemId <= 0)
            return OrganizationResult<LotTraceabilityReport>.Failure(QualityErrors.ItemInvalid);

        return await support.ExecuteStoreAsync(async () =>
        {
            var normalizedLot = QualityText.RequiredLotCode(query.LotCode);
            var dataset = await store.GetGenealogyDataset(organizationId, forcedSiteId, TodayUtc, ct);
            var report = ComputeGenealogy(dataset, query.ItemId, normalizedLot, query.Direction);
            if (report is null)
                return OrganizationResult<LotTraceabilityReport>.Failure(QualityErrors.LotNotFound);
            return OrganizationResult<LotTraceabilityReport>.Success(report);
        });
    }

    public static LotTraceabilityReport? ComputeGenealogy(
        LotGenealogyDataset dataset,
        long rootItemId,
        string rootLotCode,
        LotTraceDirection direction = LotTraceDirection.Both)
    {
        var reversedTxIds = new HashSet<long>();
        foreach (var m in dataset.Movements)
        {
            if (m.ReversedTransactionId.HasValue)
            {
                reversedTxIds.Add(m.TransactionId);
                reversedTxIds.Add(m.ReversedTransactionId.Value);
            }
        }

        var ordersById = dataset.Orders.ToDictionary(o => o.ProductionOrderId);
        var lotMetaByKey = dataset.Lots.ToDictionary(l => (l.ItemId, l.LotCode));
        var balancesByKey = dataset.Balances
            .GroupBy(b => (b.ItemId, b.LotCode))
            .ToDictionary(g => g.Key, g => (
                OnHand: decimal.Round(g.Sum(x => x.OnHandBaseQuantity), 6, MidpointRounding.AwayFromZero),
                Blocked: decimal.Round(g.Sum(x => x.BlockedBaseQuantity), 6, MidpointRounding.AwayFromZero),
                Available: decimal.Round(g.Sum(x => x.AvailableBaseQuantity), 6, MidpointRounding.AwayFromZero),
                ItemCode: g.First().ItemCode,
                ItemName: g.First().ItemName,
                UnitSymbol: g.First().BaseUnitSymbol));
        var inspectionsByKey = dataset.Inspections
            .GroupBy(i => (i.ItemId, i.LotCode))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QualityInspectionSummary>)g.OrderByDescending(x => x.Id).ToArray());
        var movementItemInfoByKey = dataset.Movements
            .GroupBy(m => (m.ItemId, m.LotCode))
            .ToDictionary(g => g.Key, g => (g.First().ItemCode, g.First().ItemName));
        var movementsByKey = dataset.Movements
            .GroupBy(m => (m.ItemId, m.LotCode))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LotTraceMovementSummary>)g
                .OrderByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.TransactionId)
                .Select(x => new LotTraceMovementSummary(
                    x.TransactionId,
                    x.DocumentNumber,
                    x.TransactionType,
                    x.SiteId,
                    x.SiteCode,
                    x.WarehouseId,
                    x.WarehouseCode,
                    x.ProductionOrderId,
                    x.ProductionOrderNumber,
                    x.QualityInspectionId,
                    x.BaseQuantityDelta,
                    x.BaseUnitSymbol,
                    x.OccurredAtUtc,
                    reversedTxIds.Contains(x.TransactionId),
                    x.Notes))
                .ToArray());

        var rootKey = (rootItemId, rootLotCode);
        if (!lotMetaByKey.ContainsKey(rootKey)
            && !balancesByKey.ContainsKey(rootKey)
            && !inspectionsByKey.ContainsKey(rootKey)
            && !movementsByKey.ContainsKey(rootKey))
        {
            return null;
        }

        var poInputs = new Dictionary<long, List<(long ItemId, string ItemCode, string ItemName, string LotCode, decimal Qty, string UnitSymbol)>>();
        var poOutputs = new Dictionary<long, List<(long ItemId, string ItemCode, string ItemName, string LotCode, decimal Qty, string UnitSymbol)>>();

        var activePoMovements = dataset.Movements
            .Where(m => !reversedTxIds.Contains(m.TransactionId)
                        && m.ProductionOrderId.HasValue
                        && ordersById.ContainsKey(m.ProductionOrderId.Value)
                        && m.TransactionType is InventoryTransactionType.ProductionIssue or InventoryTransactionType.ProductionReceipt)
            .ToArray();

        foreach (var group in activePoMovements.GroupBy(m => (ProductionOrderId: m.ProductionOrderId!.Value, m.TransactionType, m.ItemId, m.LotCode)))
        {
            var first = group.First();
            if (group.Key.TransactionType == InventoryTransactionType.ProductionIssue)
            {
                var netIssued = decimal.Round(group.Sum(x => -x.BaseQuantityDelta), 6, MidpointRounding.AwayFromZero);
                if (netIssued <= 0m) continue;
                if (!poInputs.TryGetValue(group.Key.ProductionOrderId, out var list))
                    poInputs[group.Key.ProductionOrderId] = list = [];
                list.Add((first.ItemId, first.ItemCode, first.ItemName, first.LotCode, netIssued, first.BaseUnitSymbol));
            }
            else
            {
                var netReceived = decimal.Round(group.Sum(x => x.BaseQuantityDelta), 6, MidpointRounding.AwayFromZero);
                if (netReceived <= 0m) continue;
                if (!poOutputs.TryGetValue(group.Key.ProductionOrderId, out var list))
                    poOutputs[group.Key.ProductionOrderId] = list = [];
                list.Add((first.ItemId, first.ItemCode, first.ItemName, first.LotCode, netReceived, first.BaseUnitSymbol));
            }
        }

        var relatedKeys = new HashSet<(long ItemId, string LotCode)>();

        List<LotGenealogyEdge> backwardEdges = [];
        if (direction is LotTraceDirection.Both or LotTraceDirection.Backward)
        {
            var visited = new HashSet<(long ItemId, string LotCode)> { rootKey };
            var queue = new Queue<((long ItemId, string LotCode) Key, int Level)>();
            queue.Enqueue((rootKey, 1));

            while (queue.Count > 0 && backwardEdges.Count < MaxGenealogyEdges)
            {
                var (curr, level) = queue.Dequeue();
                if (level > MaxGenealogyDepth) continue;

                foreach (var (poId, outputs) in poOutputs)
                {
                    var matchingOutputs = outputs.Where(o => o.ItemId == curr.ItemId && o.LotCode == curr.LotCode).ToArray();
                    if (matchingOutputs.Length == 0 || !poInputs.TryGetValue(poId, out var inputs))
                        continue;

                    var order = ordersById[poId];
                    var outEntry = matchingOutputs[0];
                    foreach (var inp in inputs)
                    {
                        if (backwardEdges.Count >= MaxGenealogyEdges) break;
                        backwardEdges.Add(new LotGenealogyEdge(
                            level,
                            "Backward",
                            poId,
                            order.OrderNumber,
                            order.SiteId,
                            order.SiteCode,
                            order.Status,
                            inp.ItemId,
                            inp.ItemCode,
                            inp.ItemName,
                            inp.LotCode,
                            inp.Qty,
                            inp.UnitSymbol,
                            outEntry.ItemId,
                            outEntry.ItemCode,
                            outEntry.ItemName,
                            outEntry.LotCode,
                            outEntry.Qty,
                            outEntry.UnitSymbol));

                        var nextKey = (inp.ItemId, inp.LotCode);
                        relatedKeys.Add(nextKey);
                        if (visited.Add(nextKey))
                            queue.Enqueue((nextKey, level + 1));
                    }
                }
            }
        }

        List<LotGenealogyEdge> forwardEdges = [];
        if (direction is LotTraceDirection.Both or LotTraceDirection.Forward)
        {
            var visited = new HashSet<(long ItemId, string LotCode)> { rootKey };
            var queue = new Queue<((long ItemId, string LotCode) Key, int Level)>();
            queue.Enqueue((rootKey, 1));

            while (queue.Count > 0 && forwardEdges.Count < MaxGenealogyEdges)
            {
                var (curr, level) = queue.Dequeue();
                if (level > MaxGenealogyDepth) continue;

                foreach (var (poId, inputs) in poInputs)
                {
                    var matchingInputs = inputs.Where(i => i.ItemId == curr.ItemId && i.LotCode == curr.LotCode).ToArray();
                    if (matchingInputs.Length == 0 || !poOutputs.TryGetValue(poId, out var outputs))
                        continue;

                    var order = ordersById[poId];
                    var inEntry = matchingInputs[0];
                    foreach (var outp in outputs)
                    {
                        if (forwardEdges.Count >= MaxGenealogyEdges) break;
                        forwardEdges.Add(new LotGenealogyEdge(
                            level,
                            "Forward",
                            poId,
                            order.OrderNumber,
                            order.SiteId,
                            order.SiteCode,
                            order.Status,
                            inEntry.ItemId,
                            inEntry.ItemCode,
                            inEntry.ItemName,
                            inEntry.LotCode,
                            inEntry.Qty,
                            inEntry.UnitSymbol,
                            outp.ItemId,
                            outp.ItemCode,
                            outp.ItemName,
                            outp.LotCode,
                            outp.Qty,
                            outp.UnitSymbol));

                        var nextKey = (outp.ItemId, outp.LotCode);
                        relatedKeys.Add(nextKey);
                        if (visited.Add(nextKey))
                            queue.Enqueue((nextKey, level + 1));
                    }
                }
            }
        }

        LotTraceNodeSummary BuildNode((long ItemId, string LotCode) key)
        {
            lotMetaByKey.TryGetValue(key, out var meta);
            balancesByKey.TryGetValue(key, out var bal);
            inspectionsByKey.TryGetValue(key, out var insps);
            movementsByKey.TryGetValue(key, out var moves);
            movementItemInfoByKey.TryGetValue(key, out var moveItem);

            var itemCode = meta?.ItemCode ?? bal.ItemCode ?? insps?.FirstOrDefault()?.ItemCode ?? moveItem.ItemCode ?? string.Empty;
            var itemName = meta?.ItemName ?? bal.ItemName ?? insps?.FirstOrDefault()?.ItemName ?? moveItem.ItemName ?? string.Empty;
            var unitSymbol = bal.UnitSymbol ?? insps?.FirstOrDefault()?.BaseUnitSymbol ?? moves?.FirstOrDefault()?.BaseUnitSymbol ?? string.Empty;

            return new LotTraceNodeSummary(
                key.ItemId,
                itemCode,
                itemName,
                key.LotCode,
                unitSymbol,
                meta?.ManufactureDate,
                meta?.ExpirationDate,
                meta?.IsExpired ?? false,
                meta?.IsExpiringSoon ?? false,
                bal.OnHand,
                bal.Blocked,
                bal.Available,
                insps ?? [],
                moves ?? []);
        }

        relatedKeys.Remove(rootKey);
        var rootNode = BuildNode(rootKey);
        var relatedNodes = relatedKeys
            .Select(BuildNode)
            .OrderBy(n => n.ItemCode, StringComparer.Ordinal)
            .ThenBy(n => n.LotCode, StringComparer.Ordinal)
            .ToArray();

        return new LotTraceabilityReport(rootNode, backwardEdges, forwardEdges, relatedNodes);
    }
}
