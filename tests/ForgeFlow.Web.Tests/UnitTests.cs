using System.Reflection;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Roles;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class UnitTests
{
    private sealed class ActorStub(TrustedActor? actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct) => ValueTask.FromResult(actor);
    }

    private sealed class PermissionStub(Func<OrganizationScope, string, bool> predicate) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor,
            OrganizationScope requestedScope,
            string permission,
            CancellationToken ct)
            => ValueTask.FromResult(new PermissionDecision(predicate(requestedScope, permission)));
    }

    private sealed class UnitStoreStub : IUnitOperationsStore
    {
        public List<UnitSummary> Units { get; } = [];
        public List<UnitConversionSummary> Conversions { get; } = [];
        public List<UnitConversionEdge> Edges { get; } = [];
        public bool CreateCalled { get; private set; }

        public Task<OrganizationPage<UnitSummary>> ListUnits(long organizationId, UnitListQuery query, CancellationToken ct)
        {
            var filtered = Units.Where(u => u.OrganizationId == organizationId && (query.IncludeInactive || u.IsActive)).ToArray();
            return Task.FromResult(new OrganizationPage<UnitSummary>(filtered, filtered.Length, query.Page, query.PageSize));
        }

        public Task<IReadOnlyList<UnitSummary>> ListActiveUnits(long organizationId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<UnitSummary>>(Units.Where(u => u.OrganizationId == organizationId && u.IsActive).ToArray());

        public Task<UnitSummary?> GetUnit(long organizationId, long id, CancellationToken ct)
            => Task.FromResult(Units.FirstOrDefault(u => u.OrganizationId == organizationId && u.Id == id));

        public Task<OrganizationResult<UnitSummary>> CreateUnit(CreateUnitRequest request, OrganizationWriteAccess access, CancellationToken ct)
        {
            CreateCalled = true;
            var unit = new UnitSummary(100, request.OrganizationId, request.Code!.Trim().ToUpperInvariant(), request.Name!.Trim(), request.Symbol!.Trim(), request.Dimension, request.PrecisionScale, true, Convert.ToBase64String(new byte[8]));
            Units.Add(unit);
            return Task.FromResult(OrganizationResult<UnitSummary>.Success(unit));
        }

        public Task<OrganizationResult<UnitSummary>> UpdateUnit(long organizationId, long id, byte[] version, string? name, string? symbol, byte precisionScale, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UnitSummary>> SetUnitActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationPage<UnitConversionSummary>> ListConversions(long organizationId, UnitConversionListQuery query, CancellationToken ct)
            => Task.FromResult(new OrganizationPage<UnitConversionSummary>(Conversions.ToArray(), Conversions.Count, query.Page, query.PageSize));

        public Task<UnitConversionSummary?> GetConversion(long organizationId, long id, CancellationToken ct)
            => Task.FromResult(Conversions.FirstOrDefault(c => c.OrganizationId == organizationId && c.Id == id));

        public Task<IReadOnlyList<UnitConversionEdge>> ListActiveConversionEdges(long organizationId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<UnitConversionEdge>>(Edges);

        public Task<OrganizationResult<UnitConversionSummary>> CreateConversion(CreateUnitConversionRequest request, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UnitConversionSummary>> UpdateConversionFactor(long organizationId, long id, byte[] version, decimal factor, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<UnitConversionSummary>> SetConversionActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<OrganizationResult<SeedStandardUnitsResult>> SeedStandardUnits(long organizationId, OrganizationWriteAccess access, CancellationToken ct)
            => Task.FromResult(OrganizationResult<SeedStandardUnitsResult>.Success(new SeedStandardUnitsResult(14, 7)));

        public Task<OrganizationPage<CurrencySummary>> ListCurrencies(long organizationId, CurrencyListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<CurrencySummary>> ListActiveCurrencies(long organizationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<CurrencySummary?> GetCurrency(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<CurrencySummary>> CreateCurrency(CreateCurrencyRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<CurrencySummary>> UpdateCurrency(long organizationId, long id, byte[] version, string? name, string? symbol, byte precisionScale, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<CurrencySummary>> SetCurrencyActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    [Fact]
    public void Unit_create_normalizes_code_and_rejects_invalid_inputs()
    {
        var unit = Unit.Create(1, "  kg_01 ", "  کیلوگرم  ", " kg ", UnitDimension.Mass, 3);
        Assert.Equal("KG_01", unit.Code);
        Assert.Equal("کیلوگرم", unit.Name);
        Assert.Equal("kg", unit.Symbol);
        Assert.True(unit.IsActive);

        Assert.Equal(UnitErrors.CodeInvalid, Assert.Throws<UnitRuleException>(() => Unit.Create(1, "bad code", "Name", "N", UnitDimension.Mass, 2)).Code);
        Assert.Equal(UnitErrors.SymbolInvalid, Assert.Throws<UnitRuleException>(() => Unit.Create(1, "KG", "Kilogram", " ", UnitDimension.Mass, 2)).Code);
        Assert.Equal(UnitErrors.PrecisionInvalid, Assert.Throws<UnitRuleException>(() => Unit.Create(1, "KG", "Kilogram", "kg", UnitDimension.Mass, 7)).Code);
        Assert.Equal(UnitErrors.DimensionInvalid, Assert.Throws<UnitRuleException>(() => Unit.Create(1, "KG", "Kilogram", "kg", (UnitDimension)99, 2)).Code);
    }

    [Fact]
    public void Inactive_unit_cannot_be_updated_until_reactivated()
    {
        var unit = Unit.Create(1, "KG", "Kilogram", "kg", UnitDimension.Mass, 3);
        Assert.True(unit.Deactivate());
        Assert.Equal(UnitErrors.UnitInactive, Assert.Throws<UnitRuleException>(() => unit.Update("New", "kg", 2)).Code);
        Assert.True(unit.Activate());
        Assert.True(unit.Update("کیلوگرم", "کیلوگرم", 2));
    }

    [Fact]
    public void Unit_conversion_validates_positive_decimal_and_distinct_units()
    {
        Assert.Equal(UnitErrors.ConversionSameUnit, Assert.Throws<UnitRuleException>(() => UnitConversion.Create(1, 5, 5, 10m)).Code);
        Assert.Equal(UnitErrors.ConversionFactorInvalid, Assert.Throws<UnitRuleException>(() => UnitConversion.Create(1, 5, 6, 0m)).Code);
        Assert.Equal(UnitErrors.ConversionFactorInvalid, Assert.Throws<UnitRuleException>(() => UnitConversion.Create(1, 5, 6, -2m)).Code);
        Assert.Equal(UnitErrors.ConversionFactorInvalid, Assert.Throws<UnitRuleException>(() => UnitConversion.Create(1, 5, 6, 1.1234567m)).Code);

        var ok = UnitConversion.Create(1, 5, 6, 1000.123456m);
        Assert.Equal(1000.123456m, ok.Factor);
    }

    [Fact]
    public void Conversion_graph_resolves_multi_hop_inverse_exact_and_hybrid_item_specific_paths()
    {
        // Multi-hop global: TON(1) -> KG(2) -> G(3) and HR(10) -> MIN(11) -> SEC(12)
        UnitConversionEdge[] globalEdges =
        [
            new(1, 2, 1000m),
            new(2, 3, 1000m),
            new(10, 11, 60m),
            new(11, 12, 60m)
        ];

        var tonToG = UnitConversionGraph.ConvertQuantity(2.5m, 1, 3, globalEdges);
        Assert.Equal(1_000_000m, tonToG.Factor);
        Assert.Equal(2_500_000m, tonToG.ConvertedQuantity);
        Assert.Equal(2, tonToG.Hops);

        // Exact inverse multi-hop without intermediate rounding drift: 7200 SEC -> HR = 2 HR
        var secToHr = UnitConversionGraph.ConvertQuantity(7200m, 12, 10, globalEdges);
        Assert.Equal(2m, secToHr.ConvertedQuantity);
        Assert.Equal(2, secToHr.Hops);

        // A08: Chained ItemUnitConversion (BOX(20) -> BOTTLE(21) = 12) + global (BOTTLE(21) -> ML(22) = 500, L(23) -> ML(22) = 1000)
        // Expected: 1 BOX = 12 * 500 / 1000 = 6 L
        UnitConversionEdge[] hybridEdges =
        [
            new(21, 22, 500m, IsItemSpecific: true),
            new(23, 22, 1000m, IsItemSpecific: false),
            new(20, 21, 12m, IsItemSpecific: true)
        ];
        var boxToLiter = UnitConversionGraph.ConvertQuantity(3m, 20, 23, hybridEdges);
        Assert.Equal(6m, boxToLiter.Factor);
        Assert.Equal(18m, boxToLiter.ConvertedQuantity);
    }

    [Fact]
    public void Conversion_graph_rejects_disconnected_and_conflicting_paths()
    {
        UnitConversionEdge[] conflicting =
        [
            new(1, 2, 1000m),
            new(2, 3, 1000m),
            new(1, 3, 500m)
        ];
        Assert.Equal(UnitErrors.ConversionConflictingPath, Assert.Throws<UnitRuleException>(() => UnitConversionGraph.ResolveFactor(1, 3, conflicting)).Code);
        Assert.Equal(UnitErrors.ConversionPathNotFound, Assert.Throws<UnitRuleException>(() => UnitConversionGraph.ResolveFactor(1, 99, conflicting)).Code);
    }

    [Fact]
    public async Task Unit_use_cases_fail_closed_and_enforce_scope_boundaries()
    {
        var store = new UnitStoreStub();
        var actor = new TrustedActor(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
        var unauthenticated = new UnitUseCases(store, new ActorStub(null), new PermissionStub((_, _) => true));
        Assert.Equal(UnitErrors.ActorRequired, (await unauthenticated.ListUnits(new OrganizationScope.Organization(1), 1, new())).Error!.Code);

        var siteManageDenied = new UnitUseCases(store, new ActorStub(actor), new PermissionStub((scope, perm) => scope is OrganizationScope.Site && perm == UnitPermissions.UnitsManage));
        Assert.Equal(UnitErrors.NotFound, (await siteManageDenied.CreateUnit(new OrganizationScope.Site(1, 10), new CreateUnitRequest(1, "KG", "Kilogram", "kg", UnitDimension.Mass, 3))).Error!.Code);
        Assert.False(store.CreateCalled);

        var siteReadAllowed = new UnitUseCases(store, new ActorStub(actor), new PermissionStub((scope, perm) => scope is OrganizationScope.Site s && s.OrganizationId == 1 && perm == UnitPermissions.UnitsRead));
        store.Units.Add(new UnitSummary(1, 1, "KG", "Kilogram", "kg", UnitDimension.Mass, 3, true, Convert.ToBase64String(new byte[8])));
        store.Units.Add(new UnitSummary(2, 1, "G", "Gram", "g", UnitDimension.Mass, 3, true, Convert.ToBase64String(new byte[8])));
        store.Edges.Add(new UnitConversionEdge(1, 2, 1000m));

        var preview = await siteReadAllowed.PreviewConversion(new OrganizationScope.Site(1, 10), new ConvertQuantityRequest(1, 1, 2, 2.5m));
        Assert.True(preview.Succeeded);
        Assert.Equal(2500m, preview.Value!.ConvertedQuantity);

        var crossOrg = await siteReadAllowed.ListUnits(new OrganizationScope.Site(1, 10), 2, new());
        Assert.False(crossOrg.Succeeded);
        Assert.Equal(UnitErrors.NotFound, crossOrg.Error!.Code);
    }

    [Fact]
    public void Units_page_routes_and_built_in_admin_permissions_are_configured()
    {
        var routes = typeof(Units).GetCustomAttributes<RouteAttribute>().Select(r => r.Template).OrderBy(x => x).ToArray();
        Assert.Equal(["/organizations/{OrganizationId:long}/units", "/units"], routes);

        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Organization(1), UnitPermissions.UnitsRead));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Organization(1), UnitPermissions.UnitsManage));
        Assert.True(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Site(1, 1), UnitPermissions.UnitsRead));
        Assert.False(FixedRolePolicy.Allows(FixedAccessRoles.OrganizationAdministrator, new OrganizationScope.Site(1, 1), UnitPermissions.UnitsManage));
        Assert.Equal("UnitErrorConversionPathNotFound", RolePermissionLocalization.FailureKey(UnitErrors.ConversionPathNotFound));
    }
}
