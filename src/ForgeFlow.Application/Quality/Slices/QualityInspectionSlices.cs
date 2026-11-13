using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Quality.Slices;

public sealed class QualityInspectionSlices(
    IQualityOperationsStore store,
    QualitySliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<QualityInspectionSummary>>> ListAsync(
        OrganizationScope? scope,
        long organizationId,
        QualityInspectionListQuery query,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, QualityPermissions.QualityRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<QualityInspectionSummary>>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<QualityInspectionSummary>>.Failure(QualityErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Clamp(query.Page, 1, 1_000_000),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = query.Search?.Trim()
        };

        return await support.ExecuteStoreAsync(async () =>
            OrganizationResult<OrganizationPage<QualityInspectionSummary>>.Success(
                await store.ListInspections(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> GetAsync(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, QualityPermissions.QualityRead, ct);
        if (auth.Error is not null) return OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        if (!support.TryResolveScope(scope, organizationId, null, out var forcedSiteId) || id <= 0)
            return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);

        return await support.ExecuteStoreAsync(async () =>
        {
            var details = await store.GetInspection(organizationId, id, ct);
            if (details is null)
                return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
            if (forcedSiteId.HasValue && details.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.AccessDenied);
            return OrganizationResult<QualityInspectionDetails>.Success(details);
        });
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> CreateManualHoldAsync(
        OrganizationScope? scope,
        CreateManualQualityHoldRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        }
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId))
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
            res = await support.ExecuteStoreAsync(async () =>
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
                    support.UtcNow,
                    support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage),
                    ct);
            });
        }

        await support.AuditFailureIfRejected(res, auth.Actor, scope, request.OrganizationId, null, "QualityInspection", request.InspectionNumber, "CreateManualHold");
        return res;
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> UpdateMeasurementsAsync(
        OrganizationScope? scope,
        UpdateQualityMeasurementsRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        }
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.InspectionId <= 0)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        }
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);
        }
        else
        {
            res = await support.ExecuteStoreAsync(async () =>
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
                    support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage),
                    ct,
                    cleanCert);
            });
        }

        await support.AuditFailureIfRejected(res, auth.Actor, scope, request.OrganizationId, null, "QualityInspection", request.InspectionId.ToString(CultureInfo.InvariantCulture), "UpdateMeasurements");
        return res;
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> RecordDispositionAsync(
        OrganizationScope? scope,
        RecordQualityDispositionRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        }
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.InspectionId <= 0)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        }
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);
        }
        else
        {
            res = await support.ExecuteStoreAsync(async () =>
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
                        ? BuildScrapDocNumber(existing.Summary.InspectionNumber, support.UtcNow)
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
                    support.UtcNow,
                    support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage),
                    ct);
            });
        }

        await support.AuditFailureIfRejected(res, auth.Actor, scope, request.OrganizationId, null, "QualityInspection", request.InspectionId.ToString(CultureInfo.InvariantCulture), "RecordDisposition");
        return res;
    }

    public async Task<OrganizationResult<QualityInspectionDetails>> CancelHoldAsync(
        OrganizationScope? scope,
        CancelQualityHoldRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, QualityPermissions.QualityManage, ct);
        OrganizationResult<QualityInspectionDetails> res;
        if (auth.Error is not null)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(auth.Error);
        }
        else if (!support.TryResolveScope(scope, request.OrganizationId, null, out var forcedSiteId) || request.InspectionId <= 0)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.InspectionNotFound);
        }
        else if (support.DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
        {
            res = OrganizationResult<QualityInspectionDetails>.Failure(QualityErrors.ConcurrencyConflict);
        }
        else
        {
            res = await support.ExecuteStoreAsync(async () =>
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
                    support.UtcNow,
                    support.Access(auth.Actor!, scope!, QualityPermissions.QualityManage),
                    ct);
            });
        }

        await support.AuditFailureIfRejected(res, auth.Actor, scope, request.OrganizationId, null, "QualityInspection", request.InspectionId.ToString(CultureInfo.InvariantCulture), "CancelHold");
        return res;
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
}
