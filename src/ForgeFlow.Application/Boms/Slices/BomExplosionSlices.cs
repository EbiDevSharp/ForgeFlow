using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Boms.Slices;

public sealed class BomExplosionSlices(IBomOperationsStore store, BomSliceSupport support)
{
    private const int MaxExplosionDepth = 32;

    public async Task<OrganizationResult<BomResolutionResultDto>> ResolveAsync(
        OrganizationScope? scope,
        ResolveBomRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<BomResolutionResultDto>.Failure(auth.Error);
        if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: true, out _) || request.ItemId <= 0)
            return OrganizationResult<BomResolutionResultDto>.Failure(BomErrors.PrimaryItemInvalid);

        return await support.ExecuteStoreAsync(async () =>
        {
            var dataset = await store.LoadItemBomResolutionDataset(request.OrganizationId, request.ItemId, ct);
            if (dataset is null || !dataset.ItemIsActive)
                return OrganizationResult<BomResolutionResultDto>.Failure(BomErrors.PrimaryItemInvalid);

            var winner = ResolveFromDataset(dataset, request.ProductionDate, new ManufacturingContext(request.ContextValues));
            ValidateCandidateStructure(winner);

            var summary = winner.Details.Summary;
            return OrganizationResult<BomResolutionResultDto>.Success(new BomResolutionResultDto(
                winner.BomId,
                winner.BomCode,
                winner.BomName,
                summary.Id,
                summary.VersionNumber,
                summary.BaseOutputQuantity,
                summary.BaseOutputUnitId,
                summary.BaseOutputUnitCode,
                summary.BaseOutputUnitSymbol,
                summary.EffectiveFromDate,
                summary.EffectiveToDate));
        });
    }

    public async Task<OrganizationResult<BomExplosionResultDto>> ExplodeAsync(
        OrganizationScope? scope,
        ExplodeBomRequest request,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, BomPermissions.BomsRead, ct);
        if (auth.Error is not null) return OrganizationResult<BomExplosionResultDto>.Failure(auth.Error);
        if (!support.TryScopeOrganizationId(scope, request.OrganizationId, allowSite: true, out _) || request.ItemId <= 0)
            return OrganizationResult<BomExplosionResultDto>.Failure(BomErrors.PrimaryItemInvalid);

        if (request.RequestedQuantity <= 0m)
            return OrganizationResult<BomExplosionResultDto>.Failure(BomErrors.QuantityInvalid);

        return await support.ExecuteStoreAsync(async () =>
        {
            var core = await ResolveAndExplodeCoreAsync(
                request.OrganizationId,
                request.ItemId,
                request.ProductionDate,
                request.RequestedQuantity,
                request.RequestedUnitId,
                request.ContextValues,
                ct);
            return core.Succeeded
                ? OrganizationResult<BomExplosionResultDto>.Success(core.Value!.Explosion)
                : OrganizationResult<BomExplosionResultDto>.Failure(core.Error!.Code, core.Error.Parameters);
        });
    }

    public async Task<OrganizationResult<(BomResolutionResultDto Resolution, BomExplosionResultDto Explosion)>> ResolveAndExplodeCoreAsync(
        long organizationId,
        long itemId,
        DateOnly productionDate,
        decimal requestedQuantity,
        long? requestedUnitIdInput,
        IReadOnlyDictionary<string, string>? contextValues,
        CancellationToken ct)
    {
        if (requestedQuantity <= 0m)
            return OrganizationResult<(BomResolutionResultDto, BomExplosionResultDto)>.Failure(BomErrors.QuantityInvalid);

        var rootDataset = await store.LoadItemBomResolutionDataset(organizationId, itemId, ct);
        if (rootDataset is null || !rootDataset.ItemIsActive)
            return OrganizationResult<(BomResolutionResultDto, BomExplosionResultDto)>.Failure(BomErrors.PrimaryItemInvalid);

        var context = new ManufacturingContext(contextValues);
        var rootCandidate = ResolveFromDataset(rootDataset, productionDate, context);
        ValidateCandidateStructure(rootCandidate);

        var rootSummary = rootCandidate.Details.Summary;
        var requestedUnitId = requestedUnitIdInput ?? rootDataset.BaseUnitId;
        var requestedQtyInBaseOutput = requestedQuantity;

        if (requestedUnitId != rootSummary.BaseOutputUnitId)
        {
            var resolution = UnitConversionGraph.ConvertQuantity(
                requestedQuantity,
                requestedUnitId,
                rootSummary.BaseOutputUnitId,
                rootCandidate.ItemUnitEdges.Concat(rootDataset.GlobalUnitEdges));
            requestedQtyInBaseOutput = resolution.ConvertedQuantity;
        }

        var consumed = new List<BomExplosionLineDto>();
        var produced = new List<BomExplosionOutputLineDto>();
        var branchItems = new HashSet<long> { itemId };
        var datasetCache = new Dictionary<long, BomResolutionDataset?> { [itemId] = rootDataset };

        await ExplodeRecursiveAsync(
            organizationId,
            productionDate,
            context,
            level: 1,
            parentItemId: itemId,
            parentItemCode: rootDataset.ItemCode,
            candidate: rootCandidate,
            targetBaseOutputQuantity: requestedQtyInBaseOutput,
            globalEdges: rootDataset.GlobalUnitEdges,
            branchItems: branchItems,
            datasetCache: datasetCache,
            consumed: consumed,
            produced: produced,
            ct: ct);

        var reqUnitCode = requestedUnitId == rootSummary.BaseOutputUnitId ? rootSummary.BaseOutputUnitCode : rootDataset.BaseUnitCode;
        var reqUnitSym = requestedUnitId == rootSummary.BaseOutputUnitId ? rootSummary.BaseOutputUnitSymbol : rootDataset.BaseUnitSymbol;

        var resolutionDto = new BomResolutionResultDto(
            rootCandidate.BomId,
            rootCandidate.BomCode,
            rootCandidate.BomName,
            rootSummary.Id,
            rootSummary.VersionNumber,
            rootSummary.BaseOutputQuantity,
            rootSummary.BaseOutputUnitId,
            rootSummary.BaseOutputUnitCode,
            rootSummary.BaseOutputUnitSymbol,
            rootSummary.EffectiveFromDate,
            rootSummary.EffectiveToDate);

        var explosionDto = new BomExplosionResultDto(
            rootDataset.ItemId,
            rootDataset.ItemCode,
            rootDataset.ItemName,
            requestedQuantity,
            requestedUnitId,
            reqUnitCode,
            reqUnitSym,
            rootSummary.Id,
            rootSummary.VersionNumber,
            consumed,
            produced);

        return OrganizationResult<(BomResolutionResultDto, BomExplosionResultDto)>.Success((resolutionDto, explosionDto));
    }

    private async Task ExplodeRecursiveAsync(
        long organizationId,
        DateOnly productionDate,
        ManufacturingContext context,
        int level,
        long parentItemId,
        string parentItemCode,
        BomVersionCandidateRecord candidate,
        decimal targetBaseOutputQuantity,
        IReadOnlyList<UnitConversionEdge> globalEdges,
        HashSet<long> branchItems,
        Dictionary<long, BomResolutionDataset?> datasetCache,
        List<BomExplosionLineDto> consumed,
        List<BomExplosionOutputLineDto> produced,
        CancellationToken ct)
    {
        if (level > MaxExplosionDepth)
            throw new BomRuleException(BomErrors.CircularDependency);

        var scaleFactor = targetBaseOutputQuantity / candidate.Details.Summary.BaseOutputQuantity;

        foreach (var output in candidate.Details.Outputs.OrderBy(o => o.Sequence))
        {
            var qty = decimal.Round(output.Quantity * scaleFactor, 6, MidpointRounding.AwayFromZero);
            produced.Add(new BomExplosionOutputLineDto(
                level,
                output.ItemId,
                output.ItemCode,
                output.ItemName,
                output.OutputType,
                qty,
                output.UnitId,
                output.UnitCode,
                output.UnitSymbol));
        }

        foreach (var input in candidate.Details.Inputs.OrderBy(i => i.Sequence))
        {
            if (branchItems.Contains(input.ItemId))
                throw new BomRuleException(BomErrors.CircularDependency);

            var netQty = decimal.Round(input.Quantity * scaleFactor, 6, MidpointRounding.AwayFromZero);
            var scrapMultiplier = 1m + (input.ScrapPercentage / 100m);
            var grossQty = decimal.Round(input.Quantity * scaleFactor * scrapMultiplier, 6, MidpointRounding.AwayFromZero);

            if (!datasetCache.TryGetValue(input.ItemId, out var childDataset))
            {
                childDataset = await store.LoadItemBomResolutionDataset(organizationId, input.ItemId, ct);
                datasetCache[input.ItemId] = childDataset;
            }

            BomVersionCandidateRecord? childWinner = null;
            if (childDataset is not null && childDataset.ItemIsActive && childDataset.Candidates.Count > 0)
            {
                try
                {
                    childWinner = ResolveFromDataset(childDataset, productionDate, context);
                    ValidateCandidateStructure(childWinner);
                }
                catch (BomRuleException ex) when (ex.Code == BomErrors.NoActiveBomFound)
                {
                    childWinner = null;
                }
            }

            consumed.Add(new BomExplosionLineDto(
                level,
                parentItemId,
                parentItemCode,
                input.ItemId,
                input.ItemCode,
                input.ItemName,
                netQty,
                input.ScrapPercentage,
                grossQty,
                input.UnitId,
                input.UnitCode,
                input.UnitSymbol,
                input.IsOptional,
                childWinner is not null,
                childWinner?.Details.Summary.Id));

            if (childWinner is not null)
            {
                var childBaseQty = grossQty;
                if (input.UnitId != childWinner.Details.Summary.BaseOutputUnitId)
                {
                    var edges = childWinner.ItemUnitEdges.Concat(globalEdges);
                    var converted = UnitConversionGraph.ConvertQuantity(
                        grossQty,
                        input.UnitId,
                        childWinner.Details.Summary.BaseOutputUnitId,
                        edges);
                    childBaseQty = converted.ConvertedQuantity;
                }

                branchItems.Add(input.ItemId);
                try
                {
                    await ExplodeRecursiveAsync(
                        organizationId,
                        productionDate,
                        context,
                        level + 1,
                        input.ItemId,
                        input.ItemCode,
                        childWinner,
                        childBaseQty,
                        globalEdges,
                        branchItems,
                        datasetCache,
                        consumed,
                        produced,
                        ct);
                }
                finally
                {
                    branchItems.Remove(input.ItemId);
                }
            }
        }
    }

    private static BomVersionCandidateRecord ResolveFromDataset(
        BomResolutionDataset dataset,
        DateOnly productionDate,
        ManufacturingContext context)
    {
        var domainCandidates = dataset.Candidates.Select(c => new BomCandidateVersion(
            c.BomId,
            c.Details.Summary.Id,
            c.Details.Summary.VersionNumber,
            c.Details.Summary.Status,
            c.Details.Summary.EffectiveFromDate,
            c.Details.Summary.EffectiveToDate,
            c.Details.Applicability is not null,
            c.Details.Applicability?.IsActive ?? false,
            c.Details.Applicability?.Conditions
                .Select(x => new BomCandidateCondition(x.DimensionCode, x.DimensionIsActive, x.Operator, x.Value))
                .ToArray() ?? []));

        var resolved = BomResolutionEngine.Resolve(productionDate, context, domainCandidates);
        return dataset.Candidates.Single(c => c.Details.Summary.Id == resolved.BomVersionId);
    }

    private static void ValidateCandidateStructure(BomVersionCandidateRecord candidate)
    {
        if (candidate.Details.Inputs.Count == 0)
            throw new BomRuleException(BomErrors.InputsRequired);

        var hasMain = candidate.Details.Outputs.Any(o =>
            o.OutputType == BomOutputType.MainProduct && o.ItemId == candidate.PrimaryOutputItemId);
        if (!hasMain)
            throw new BomRuleException(BomErrors.MainOutputRequired);

        if (candidate.Details.Inputs.Any(i => i.ItemId == candidate.PrimaryOutputItemId))
            throw new BomRuleException(BomErrors.DirectSelfCycle);
    }
}
