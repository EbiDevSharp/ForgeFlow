using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Quality;

/// <summary>
/// Secured Quality Control, Quarantine Hold, Lot Metadata, and Multi-Level Lot Genealogy use cases.
/// Separates QC release/scrap authority (<c>Quality.Manage</c>) from Warehouse operations.
/// </summary>
public sealed class QualityUseCases(
    IQualityOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private const int MaxGenealogyDepth = 15;
    private const int MaxGenealogyEdges = 500;

    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, QualityErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, QualityErrors.ActorRequired);
        if (scope is null) return (null, QualityErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, QualityErrors.AccessDenied);
        return (actor, null);
    }

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

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

    private static byte[]? DecodeVersion(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            var bytes = Convert.FromBase64String(raw);
            return bytes.Length == 8 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(MapInventoryError(ex.Code), ex.Parameters); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(MapUnitError(ex.Code), ex.Parameters); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<T>.Failure(QualityErrors.PersistenceUnavailable); }
    }

    private static string MapInventoryError(string code) => code switch
    {
        InventoryErrors.InsufficientStock => QualityErrors.InsufficientAvailableStock,
        InventoryErrors.WarehouseNotFound or InventoryErrors.WarehouseInactive => QualityErrors.WarehouseInvalid,
        InventoryErrors.SiteInactive => QualityErrors.SiteInactive,
        InventoryErrors.ParentOrganizationInactive => QualityErrors.ParentOrganizationInactive,
        InventoryErrors.LotCodeInvalid => QualityErrors.LotCodeInvalid,
        _ => QualityErrors.QuantityInvalid
    };

    private static string MapUnitError(string code) => code switch
    {
        UnitErrors.ConversionQuantityInvalid => QualityErrors.QuantityInvalid,
        _ => QualityErrors.UnitInvalid
    };

    private DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);

    public async Task<OrganizationResult<OrganizationPage<QualityInspectionSummary>>> ListInspections(
        OrganizationScope? scope,
        long organizationId,
        QualityInspectionListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, QualityPermissions.QualityRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<QualityInspectionSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<QualityInspectionSummary>>.Failure(QualityErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<QualityInspectionSummary>>.Success(
                await store.ListInspections(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> GetInspection(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, QualityPermissions.QualityRead, ct);
        if (auth.Error is not null) return OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);

        return await ExecuteStore(async () =>
        {
            var details = await store.GetInspection(organizationId, id, ct);
            if (details is null)
                return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
            if (forcedSiteId.HasValue && details.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.AccessDenied);
            return OrganizationResult<QualityInspectionDetails>.Success(details);
        });
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> CreateManualHold(
        OrganizationScope? scope,
        CreateManualQualityHoldRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        }
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId))
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.AccessDenied);
        }
        else if (request.WarehouseId <= 0)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.WarehouseInvalid);
        }
        else if (request.ItemId <= 0)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ItemInvalid);
        }
        else
        {
            res = await ExecuteStore(async () =>
            {
                var inspNum = QualityText.Code(request.InspectionNumber);
                var lotCode = QualityText.OptionalLotCode(request.LotCode);
                _ = QualityText.OptionalDefectReason(request.DefectReasonCode);
                _ = QualityText.OptionalNote(request.Notes);
                var qty = QualityText.ValidatePositiveQuantity(request.Quantity);

                var target = await store.GetHoldTargetLookup(request.OrganizationId, request.WarehouseId, request.ItemId, lotCode, ct);
                if (target is null)
                    return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InsufficientAvailableStock);
                if (forcedSiteId.HasValue && target.SiteId != forcedSiteId.Value)
                    return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.AccessDenied);

                var enteredUnitId = request.UnitId ?? target.BaseUnitId;
                var baseQty = enteredUnitId == target.BaseUnitId
                    ? qty
                    : UnitConversionGraph.ConvertQuantity(qty, enteredUnitId, target.BaseUnitId, target.UnitEdges).ConvertedQuantity;
                baseQty = QualityText.ValidatePositiveQuantity(baseQty);

                if (baseQty > target.AvailableBaseQuantity)
                    return OrganizationResult<QualityInspectionDetails>.Failure(
                        QualityErrors.InsufficientAvailableStock,
                        new Dictionary<string, object>
                        {
                            ["available"] = target.AvailableBaseQuantity,
                            ["requested"] = baseQty
                        });

                return await store.CreateManualHold(
                    request,
                    target.SiteId,
                    inspNum,
                    lotCode,
                    target.BaseUnitId,
                    baseQty,
                    UtcNow,
                    Access(auth.Actor!, scope!, QualityPermissions.QualityManage),
                    ct);
            });
        }

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "QualityInspection", request.InspectionNumber, new { Action = "CreateManualHold" });
        return res;
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> UpdateMeasurements(
        OrganizationScope? scope,
        UpdateQualityMeasurementsRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        }
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.InspectionId <= 0)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        }
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);
        }
        else
        {
            res = await ExecuteStore(async () =>
            {
                var existing = await store.GetInspection(request.OrganizationId, request.InspectionId, ct);
                if (existing is null)
                    return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.AccessDenied);

                var cleanDefect = QualityText.OptionalDefectReason(request.DefectReasonCode);
                var cleanNotes = QualityText.OptionalNote(request.Notes);
                var cleanCert = QualityText.OptionalCertificateNumber(request.CertificateNumber);
                var cleanLines = (request.Lines ?? []).Select(l =>
                {
                    var pName = QualityText.ParameterName(l.ParameterName);
                    var symbol = string.IsNullOrWhiteSpace(l.Symbol) ? null : QualityText.Symbol(l.Symbol);
                    var unitSym = QualityText.OptionalUnitSymbol(l.UnitSymbol);
                    var method = QualityText.OptionalTestMethod(l.TestMethod);
                    var pCode = string.IsNullOrWhiteSpace(l.ParameterCode) ? null : QualityText.Code(l.ParameterCode, QualityErrors.ParameterInvalid);
                    var expected = QualityText.OptionalSpecOrValue(l.ExpectedSpec) ?? FormatRangeSpec(l.MinValue, l.MaxValue, l.TargetValue, unitSym);
                    var measured = QualityText.OptionalSpecOrValue(l.MeasuredValue);
                    var numVal = l.MeasuredNumericValue;
                    var passed = l.IsPassed;
                    if (l.DataType == QualityParameterDataType.Numeric && numVal.HasValue)
                    {
                        measured ??= numVal.Value.ToString("0.######", CultureInfo.InvariantCulture);
                        if (l.MinValue.HasValue || l.MaxValue.HasValue)
                            passed = QualityInspectionLine.EvaluateNumericPass(l.MinValue, l.MaxValue, numVal);
                    }
                    return new QualityInspectionLineInput(
                        pName,
                        expected,
                        measured,
                        passed,
                        QualityText.OptionalNote(l.Notes, QualityText.MaxLineNoteLength),
                        l.QualityParameterId,
                        pCode,
                        symbol,
                        unitSym,
                        method,
                        l.DataType,
                        l.MinValue,
                        l.MaxValue,
                        l.TargetValue,
                        numVal,
                        l.IsMandatory);
                }).ToArray();

                return await store.UpdateMeasurements(
                    request.OrganizationId,
                    request.InspectionId,
                    version,
                    cleanDefect,
                    cleanNotes,
                    cleanLines,
                    Access(auth.Actor!, scope!, QualityPermissions.QualityManage),
                    ct,
                    cleanCert);
            });
        }

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "QualityInspection", request.InspectionId.ToString(CultureInfo.InvariantCulture), new { Action = "UpdateMeasurements" });
        return res;
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> RecordDisposition(
        OrganizationScope? scope,
        RecordQualityDispositionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        }
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.InspectionId <= 0)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        }
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);
        }
        else
        {
            res = await ExecuteStore(async () =>
            {
                var existing = await store.GetInspection(request.OrganizationId, request.InspectionId, ct);
                if (existing is null)
                    return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.AccessDenied);

                var acceptRaw = QualityText.ValidateNonNegativeQuantity(request.AcceptQuantity);
                var rejectRaw = QualityText.ValidateNonNegativeQuantity(request.RejectQuantity);
                if (acceptRaw + rejectRaw <= 0m)
                    return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.QuantityInvalid);

                var enteredUnitId = request.UnitId ?? existing.Summary.BaseUnitId;
                decimal acceptBase, rejectBase;
                if (enteredUnitId == existing.Summary.BaseUnitId)
                {
                    acceptBase = acceptRaw;
                    rejectBase = rejectRaw;
                }
                else
                {
                    var edges = await store.LoadItemUnitEdges(request.OrganizationId, existing.Summary.ItemId, ct);
                    acceptBase = acceptRaw == 0m
                        ? 0m
                        : QualityText.ValidatePositiveQuantity(UnitConversionGraph.ConvertQuantity(acceptRaw, enteredUnitId, existing.Summary.BaseUnitId, edges).ConvertedQuantity);
                    rejectBase = rejectRaw == 0m
                        ? 0m
                        : QualityText.ValidatePositiveQuantity(UnitConversionGraph.ConvertQuantity(rejectRaw, enteredUnitId, existing.Summary.BaseUnitId, edges).ConvertedQuantity);
                }

                var cleanDefect = QualityText.OptionalDefectReason(request.DefectReasonCode);
                var cleanNotes = QualityText.OptionalNote(request.Notes);
                string? scrapDoc = null;
                if (rejectBase > 0m)
                {
                    scrapDoc = string.IsNullOrWhiteSpace(request.ScrapDocumentNumber)
                        ? BuildScrapDocNumber(existing.Summary.InspectionNumber, UtcNow)
                        : QualityText.Code(request.ScrapDocumentNumber);
                }

                return await store.RecordDisposition(
                    request.OrganizationId,
                    request.InspectionId,
                    version,
                    acceptBase,
                    rejectBase,
                    cleanDefect,
                    cleanNotes,
                    scrapDoc,
                    UtcNow,
                    Access(auth.Actor!, scope!, QualityPermissions.QualityManage),
                    ct);
            });
        }

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "QualityInspection", request.InspectionId.ToString(CultureInfo.InvariantCulture), new { Action = "RecordDisposition" });
        return res;
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> CancelHold(
        OrganizationScope? scope,
        CancelQualityHoldRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        }
        else if (!TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.InspectionId <= 0)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        }
        else if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);
        }
        else
        {
            res = await ExecuteStore(async () =>
            {
                var existing = await store.GetInspection(request.OrganizationId, request.InspectionId, ct);
                if (existing is null)
                    return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.AccessDenied);

                var cleanNotes = QualityText.OptionalNote(request.Notes);
                return await store.CancelHold(
                    request.OrganizationId,
                    request.InspectionId,
                    version,
                    cleanNotes,
                    UtcNow,
                    Access(auth.Actor!, scope!, QualityPermissions.QualityManage),
                    ct);
            });
        }

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "QualityInspection", request.InspectionId.ToString(CultureInfo.InvariantCulture), new { Action = "CancelHold" });
        return res;
    }

    public async Task<OrganizationResult<OrganizationPage<ItemLotSummary>>> ListItemLots(
        OrganizationScope? scope,
        long organizationId,
        ItemLotListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, QualityPermissions.QualityRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<ItemLotSummary>>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, null, out _))
            return OrganizationResult<OrganizationPage<ItemLotSummary>>.Failure(QualityErrors.AccessDenied);

        var bounded = query with
        {
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<ItemLotSummary>>.Success(
                await store.ListItemLots(organizationId, bounded, TodayUtc, ct)));
    }

    public async Task<OrganizationResult<ItemLotSummary>> UpsertItemLotMetadata(
        OrganizationScope? scope,
        UpsertItemLotMetadataRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<ItemLotSummary> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<ItemLotSummary>.Failure(auth.Error);
        }
        else if (!TryResolveScope(scope, request.OrganizationId, null, out _))
        {
            res = OrganizationResult<ItemLotSummary>.Failure(QualityErrors.AccessDenied);
        }
        else if (request.ItemId <= 0)
        {
            res = OrganizationResult<ItemLotSummary>.Failure(QualityErrors.ItemInvalid);
        }
        else
        {
            res = await ExecuteStore(() =>
            {
                var normalizedLot = QualityText.RequiredLotCode(request.LotCode);
                QualityText.ValidateDates(request.ManufactureDate, request.ExpirationDate);
                var cleanNotes = QualityText.OptionalNote(request.Notes, QualityText.MaxLotNoteLength);
                var normalized = request with { LotCode = normalizedLot, Notes = cleanNotes };
                return store.UpsertItemLotMetadata(normalized, TodayUtc, Access(auth.Actor!, scope!, QualityPermissions.QualityManage), ct);
            });
        }

        await RecordRejected(res, auth.Actor, scope, request.OrganizationId, null, "ItemLot", request.LotCode, new { Action = "UpsertItemLotMetadata", request.ItemId });
        return res;
    }

    public async Task<OrganizationResult<LotTraceabilityReport>> TraceLot(
        OrganizationScope? scope,
        long organizationId,
        LotTraceQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, QualityPermissions.QualityRead, ct);
        if (auth.Error is not null) return OrganizationResult<LotTraceabilityReport>.Failure(auth.Error);
        if (!TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<LotTraceabilityReport>.Failure(QualityErrors.AccessDenied);
        if (query.ItemId <= 0)
            return OrganizationResult<LotTraceabilityReport>.Failure(QualityErrors.ItemInvalid);

        return await ExecuteStore(async () =>
        {
            var normalizedLot = QualityText.RequiredLotCode(query.LotCode);
            var dataset = await store.GetGenealogyDataset(organizationId, forcedSiteId, TodayUtc, ct);
            var report = ComputeGenealogy(dataset, query.ItemId, normalizedLot, query.Direction);
            if (report is null)
                return OrganizationResult<LotTraceabilityReport>.Failure(QualityErrors.LotNotFound);
            return OrganizationResult<LotTraceabilityReport>.Success(report);
        });
    }

    /// <summary>
    /// Computes cycle-safe multi-level Forward and Backward lot genealogy directly from immutable ledger movements,
    /// excluding reversed transactions and enriching each lot node with physical balances, expiry, and QC records.
    /// </summary>
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

        // Build per-PO net input lots & net output lots from active (non-reversed) movements.
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

    private static string? FormatRangeSpec(decimal? min, decimal? max, decimal? target, string? unitSymbol)
    {
        var suffix = string.IsNullOrWhiteSpace(unitSymbol) ? string.Empty : $" {unitSymbol}";
        if (min.HasValue && max.HasValue)
            return $"{min.Value.ToString("0.######", CultureInfo.InvariantCulture)} – {max.Value.ToString("0.######", CultureInfo.InvariantCulture)}{suffix}";
        if (min.HasValue)
            return $">= {min.Value.ToString("0.######", CultureInfo.InvariantCulture)}{suffix}";
        if (max.HasValue)
            return $"<= {max.Value.ToString("0.######", CultureInfo.InvariantCulture)}{suffix}";
        if (target.HasValue)
            return $"{target.Value.ToString("0.######", CultureInfo.InvariantCulture)}{suffix}";
        return null;
    }

    private static string BuildScrapDocNumber(string inspectionNumber, DateTime utcNow)
    {
        var prefix = inspectionNumber.Length > 20 ? inspectionNumber[..20] : inspectionNumber;
        var candidate = $"QS-{prefix}-{utcNow:HHmmss}";
        return candidate.Length <= QualityText.MaxCodeLength ? candidate : candidate[..QualityText.MaxCodeLength];
    }

    private Task RecordRejected<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long organizationId,
        long? siteId,
        string entityType,
        string? entityRef,
        object metadata)
        => OperationAuditHelper.RecordIfRejectedAsync(
            result,
            operationRecorder,
            opContext,
            UserOperationCatalog.QualityActionRejected,
            actor,
            scope,
            organizationId,
            siteId,
            entityType,
            entityRef,
            metadata);
}
