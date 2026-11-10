using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Units;

/// <summary>Secured use cases for organization-scoped units of measure and multi-hop conversions.</summary>
public sealed class UnitUseCases(
    IUnitOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null)
{
    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, UnitErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, UnitErrors.ActorRequired);
        if (scope is null) return (null, UnitErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, UnitErrors.AccessDenied);
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

    private static bool TryScopeOrganizationId(OrganizationScope? scope, long requestedOrgId, bool allowSite, out long orgId)
    {
        orgId = scope switch
        {
            OrganizationScope.Organization org => org.OrganizationId,
            OrganizationScope.Site site when allowSite => site.OrganizationId,
            _ => 0
        };
        return orgId > 0 && orgId == requestedOrgId;
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

    private static UnitListQuery Bound(UnitListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static UnitConversionListQuery Bound(UnitConversionListQuery query) => query with
    {
        Page = Math.Clamp(query.Page, 1, 1_000_000),
        PageSize = Math.Clamp(query.PageSize, 1, 100),
        Search = query.Search?.Trim()
    };

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (UnitRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitStoreUnavailableException) { return OrganizationResult<T>.Failure(UnitErrors.PersistenceUnavailable); }
    }

    public async Task<OrganizationResult<OrganizationPage<UnitSummary>>> ListUnits(
        OrganizationScope? scope,
        long organizationId,
        UnitListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<UnitSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<UnitSummary>>.Failure(UnitErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<UnitSummary>>.Success(
                await store.ListUnits(organizationId, Bound(query), ct)));
    }

    public async Task<OrganizationResult<IReadOnlyList<UnitSummary>>> ListActiveUnits(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<IReadOnlyList<UnitSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<IReadOnlyList<UnitSummary>>.Failure(UnitErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<IReadOnlyList<UnitSummary>>.Success(
                await store.ListActiveUnits(organizationId, ct)));
    }

    public async Task<OrganizationResult<UnitSummary>> GetUnit(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<UnitSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);

        return await ExecuteStore(async () =>
        {
            var item = await store.GetUnit(organizationId, id, ct);
            return item is null
                ? OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound)
                : OrganizationResult<UnitSummary>.Success(item);
        });
    }

    public async Task<OrganizationResult<UnitSummary>> CreateUnit(
        OrganizationScope? scope,
        CreateUnitRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsManage, ct);
        if (auth.Error is not null) return OrganizationResult<UnitSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            return OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);

        try
        {
            _ = Unit.Create(request.OrganizationId, request.Code, request.Name, request.Symbol, request.Dimension, request.PrecisionScale);
        }
        catch (UnitRuleException ex)
        {
            return OrganizationResult<UnitSummary>.Failure(ex.Code, ex.Parameters);
        }

        return await ExecuteStore(() => store.CreateUnit(request, Access(auth.Actor!, scope!, UnitPermissions.UnitsManage), ct));
    }

    public async Task<OrganizationResult<UnitSummary>> UpdateUnit(
        OrganizationScope? scope,
        UpdateUnitRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsManage, ct);
        if (auth.Error is not null) return OrganizationResult<UnitSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<UnitSummary>.Failure(UnitErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.UpdateUnit(
            request.OrganizationId,
            request.Id,
            version,
            request.Name,
            request.Symbol,
            request.PrecisionScale,
            Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
            ct));
    }

    public async Task<OrganizationResult<UnitSummary>> SetUnitActive(
        OrganizationScope? scope,
        SetUnitActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsManage, ct);
        if (auth.Error is not null) return OrganizationResult<UnitSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<UnitSummary>.Failure(UnitErrors.NotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<UnitSummary>.Failure(UnitErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.SetUnitActive(
            request.OrganizationId,
            request.Id,
            version,
            request.IsActive,
            Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
            ct));
    }

    public async Task<OrganizationResult<OrganizationPage<UnitConversionSummary>>> ListConversions(
        OrganizationScope? scope,
        long organizationId,
        UnitConversionListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<UnitConversionSummary>>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _))
            return OrganizationResult<OrganizationPage<UnitConversionSummary>>.Failure(UnitErrors.NotFound);

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<UnitConversionSummary>>.Success(
                await store.ListConversions(organizationId, Bound(query), ct)));
    }

    public async Task<OrganizationResult<UnitConversionSummary>> GetConversion(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<UnitConversionSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: true, out _) || id <= 0)
            return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound);

        return await ExecuteStore(async () =>
        {
            var item = await store.GetConversion(organizationId, id, ct);
            return item is null
                ? OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound)
                : OrganizationResult<UnitConversionSummary>.Success(item);
        });
    }

    public async Task<OrganizationResult<UnitConversionSummary>> CreateConversion(
        OrganizationScope? scope,
        CreateUnitConversionRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsManage, ct);
        if (auth.Error is not null) return OrganizationResult<UnitConversionSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _))
            return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.NotFound);

        try
        {
            _ = UnitConversion.Create(request.OrganizationId, request.FromUnitId, request.ToUnitId, request.Factor);
        }
        catch (UnitRuleException ex)
        {
            return OrganizationResult<UnitConversionSummary>.Failure(ex.Code, ex.Parameters);
        }

        return await ExecuteStore(() => store.CreateConversion(request, Access(auth.Actor!, scope!, UnitPermissions.UnitsManage), ct));
    }

    public async Task<OrganizationResult<UnitConversionSummary>> UpdateConversionFactor(
        OrganizationScope? scope,
        UpdateUnitConversionFactorRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsManage, ct);
        if (auth.Error is not null) return OrganizationResult<UnitConversionSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.UpdateConversionFactor(
            request.OrganizationId,
            request.Id,
            version,
            request.Factor,
            Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
            ct));
    }

    public async Task<OrganizationResult<UnitConversionSummary>> SetConversionActive(
        OrganizationScope? scope,
        SetUnitConversionActiveRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsManage, ct);
        if (auth.Error is not null) return OrganizationResult<UnitConversionSummary>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: false, out _) || request.Id <= 0)
            return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConversionNotFound);
        if (DecodeVersion(request.ExpectedRowVersion) is not byte[] version)
            return OrganizationResult<UnitConversionSummary>.Failure(UnitErrors.ConcurrencyConflict);

        return await ExecuteStore(() => store.SetConversionActive(
            request.OrganizationId,
            request.Id,
            version,
            request.IsActive,
            Access(auth.Actor!, scope!, UnitPermissions.UnitsManage),
            ct));
    }

    public async Task<OrganizationResult<UnitConversionPreviewResult>> PreviewConversion(
        OrganizationScope? scope,
        ConvertQuantityRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsRead, ct);
        if (auth.Error is not null) return OrganizationResult<UnitConversionPreviewResult>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, request.OrganizationId, allowSite: true, out _))
            return OrganizationResult<UnitConversionPreviewResult>.Failure(UnitErrors.NotFound);

        return await ExecuteStore(async () =>
        {
            if (request.Quantity <= 0m)
                return OrganizationResult<UnitConversionPreviewResult>.Failure(UnitErrors.ConversionQuantityInvalid);

            var fromUnit = await store.GetUnit(request.OrganizationId, request.FromUnitId, ct);
            var toUnit = await store.GetUnit(request.OrganizationId, request.ToUnitId, ct);
            if (fromUnit is null || toUnit is null)
                return OrganizationResult<UnitConversionPreviewResult>.Failure(UnitErrors.NotFound);
            if (!fromUnit.IsActive || !toUnit.IsActive)
                return OrganizationResult<UnitConversionPreviewResult>.Failure(UnitErrors.UnitInactive);

            var edges = await store.ListActiveConversionEdges(request.OrganizationId, ct);
            var resolution = UnitConversionGraph.ConvertQuantity(request.Quantity, request.FromUnitId, request.ToUnitId, edges);

            return OrganizationResult<UnitConversionPreviewResult>.Success(new UnitConversionPreviewResult(
                request.FromUnitId,
                request.ToUnitId,
                request.Quantity,
                resolution.Factor,
                resolution.ConvertedQuantity,
                resolution.Hops));
        });
    }

    public async Task<OrganizationResult<SeedStandardUnitsResult>> SeedStandardUnits(
        OrganizationScope? scope,
        long organizationId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, UnitPermissions.UnitsManage, ct);
        if (auth.Error is not null) return OrganizationResult<SeedStandardUnitsResult>.Failure(auth.Error);
        if (!TryScopeOrganizationId(scope, organizationId, allowSite: false, out _))
            return OrganizationResult<SeedStandardUnitsResult>.Failure(UnitErrors.NotFound);

        return await ExecuteStore(() =>
            store.SeedStandardUnits(organizationId, Access(auth.Actor!, scope!, UnitPermissions.UnitsManage), ct));
    }
}
