using System.Reflection;
using ForgeFlow.Application.Boms;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Units;
using ForgeFlow.Web.Components.Pages;
using ForgeFlow.Web.Roles;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class BomTests
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

    private sealed class BomStoreStub : IBomOperationsStore
    {
        public Dictionary<long, BomResolutionDataset> Datasets { get; } = [];
        public List<UnitConversionEdge> ItemEdges { get; } = [];

        public Task<BomResolutionDataset?> LoadItemBomResolutionDataset(long organizationId, long itemId, CancellationToken ct)
            => Task.FromResult(Datasets.TryGetValue(itemId, out var ds) ? ds : null);

        public Task<IReadOnlyList<UnitConversionEdge>> ListItemUnitEdges(long organizationId, long itemId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<UnitConversionEdge>>(ItemEdges);

        public Task<OrganizationPage<BomSummary>> ListBoms(long organizationId, BomListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<BomDetails?> GetBom(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<BomVersionDetails?> GetBomVersion(long organizationId, long bomVersionId, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomDetails>> CreateBom(CreateBomRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomDetails>> UpdateBom(long organizationId, long id, byte[] version, string? name, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomDetails>> SetBomActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> CreateBomVersion(CreateBomVersionRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> UpdateDraftBomVersion(long organizationId, long bomVersionId, byte[] version, UpdateDraftBomVersionRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> SaveDraftBomLines(long organizationId, long bomVersionId, byte[] version, IReadOnlyList<BomInputLineInput> inputs, IReadOnlyList<BomOutputLineInput> outputs, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> SaveDraftBomApplicability(long organizationId, long bomVersionId, byte[] version, SaveDraftBomApplicabilityRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<BomVersionDetails>> TransitionBomVersionStatus(long organizationId, long bomVersionId, byte[] version, BomVersionStatus targetStatus, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationPage<ContextDimensionSummary>> ListContextDimensions(long organizationId, ContextDimensionListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ContextDimensionSummary>> ListActiveContextDimensions(long organizationId, CancellationToken ct) => throw new NotImplementedException();
        public Task<ContextDimensionSummary?> GetContextDimension(long organizationId, long id, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ContextDimensionSummary>> CreateContextDimension(CreateContextDimensionRequest request, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ContextDimensionSummary>> UpdateContextDimension(long organizationId, long id, byte[] version, string? name, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<ContextDimensionSummary>> SetContextDimensionActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
        public Task<OrganizationResult<SeedContextDimensionsResult>> SeedStandardContextDimensions(long organizationId, OrganizationWriteAccess access, CancellationToken ct) => throw new NotImplementedException();
    }

    [Fact]
    public void Domain_bom_version_lifecycle_and_validation_enforced()
    {
        var ver = BomVersion.CreateDraft(1, 10, 1, 100m, 2, new DateOnly(2026, 1, 1), null, "Initial");
        Assert.Equal(BomVersionStatus.Draft, ver.Status);
        Assert.True(ver.UpdateDraftHeader(200m, 2, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Updated"));

        Assert.True(ver.TransitionTo(BomVersionStatus.Approved));
        Assert.Equal(BomErrors.VersionImmutable, Assert.Throws<BomRuleException>(() => ver.EnsureMutableDraft()).Code);
        Assert.Equal(BomErrors.VersionTransitionInvalid, Assert.Throws<BomRuleException>(() => ver.TransitionTo(BomVersionStatus.Draft)).Code);

        Assert.True(ver.TransitionTo(BomVersionStatus.Active));
        Assert.True(ver.TransitionTo(BomVersionStatus.Superseded));
        Assert.Equal(BomErrors.VersionTransitionInvalid, Assert.Throws<BomRuleException>(() => ver.TransitionTo(BomVersionStatus.Active)).Code);

        Assert.Equal(BomErrors.ScrapInvalid, Assert.Throws<BomRuleException>(() => new BomInput(1, 1, 1, 10, 5m, 2, 100m, false, null)).Code);
        Assert.Equal(BomErrors.ScrapInvalid, Assert.Throws<BomRuleException>(() => new BomInput(1, 1, 1, 10, 5m, 2, -0.1m, false, null)).Code);
        var zeroScrap = new BomInput(1, 1, 1, 10, 5m, 2, 0m, false, null);
        Assert.Equal(0m, zeroScrap.ScrapPercentage);
    }

    [Fact]
    public void Resolver_enforces_status_dates_A11_active_applicability_dominance_and_missing_context()
    {
        var date = new DateOnly(2026, 6, 15);
        var candidates = new[]
        {
            // Draft ignored (A07)
            new BomCandidateVersion(1, 101, 1, BomVersionStatus.Draft, new DateOnly(2026, 1, 1), null, false, false, []),
            // Out-of-date ignored (A07)
            new BomCandidateVersion(1, 102, 2, BomVersionStatus.Active, new DateOnly(2027, 1, 1), null, false, false, []),
            // Inactive applicability record ignored (A11)
            new BomCandidateVersion(1, 103, 3, BomVersionStatus.Active, new DateOnly(2026, 1, 1), null, true, false,
                [new BomCandidateCondition("PLANT", true, ApplicabilityOperator.Equals, "TEH")]),
            // Unconditional fallback
            new BomCandidateVersion(1, 104, 1, BomVersionStatus.Active, new DateOnly(2026, 1, 1), null, false, false, []),
            // Plant-specific
            new BomCandidateVersion(2, 201, 1, BomVersionStatus.Active, new DateOnly(2026, 1, 1), null, true, true,
                [new BomCandidateCondition("PLANT", true, ApplicabilityOperator.Equals, "TEH")]),
            // Plant + Line dominant
            new BomCandidateVersion(2, 202, 2, BomVersionStatus.Active, new DateOnly(2026, 1, 1), null, true, true,
                [
                    new BomCandidateCondition("PLANT", true, ApplicabilityOperator.Equals, "TEH"),
                    new BomCandidateCondition("PRODUCTION_LINE", true, ApplicabilityOperator.In, "L1,L2")
                ]),
            // NotEquals must NOT match when MARKET context key is absent
            new BomCandidateVersion(3, 301, 1, BomVersionStatus.Active, new DateOnly(2026, 1, 1), null, true, true,
                [new BomCandidateCondition("MARKET", true, ApplicabilityOperator.NotEquals, "EXPORT")])
        };

        // No context -> only unconditional 104 matches (NotEquals/In require key present)
        var r1 = BomResolutionEngine.Resolve(date, new ManufacturingContext(), candidates);
        Assert.Equal(104, r1.BomVersionId);

        // PLANT=TEH -> 201 strictly dominates 104
        var r2 = BomResolutionEngine.Resolve(date, new ManufacturingContext([new("PLANT", "TEH")]), candidates);
        Assert.Equal(201, r2.BomVersionId);

        // PLANT=TEH, PRODUCTION_LINE=L2 -> 202 strictly dominates both 201 and 104
        var r3 = BomResolutionEngine.Resolve(date, new ManufacturingContext([new("PLANT", "TEH"), new("PRODUCTION_LINE", "l2")]), candidates);
        Assert.Equal(202, r3.BomVersionId);

        // Conflicting incomparable branches: PLANT=TEH vs MARKET!=EXPORT when both supplied
        var conflicting = new[]
        {
            new BomCandidateVersion(1, 501, 1, BomVersionStatus.Active, new DateOnly(2026, 1, 1), null, true, true,
                [new BomCandidateCondition("PLANT", true, ApplicabilityOperator.Equals, "TEH")]),
            new BomCandidateVersion(2, 502, 1, BomVersionStatus.Active, new DateOnly(2026, 1, 1), null, true, true,
                [new BomCandidateCondition("MARKET", true, ApplicabilityOperator.NotEquals, "EXPORT")])
        };
        var ex = Assert.Throws<BomRuleException>(() =>
            BomResolutionEngine.Resolve(date, new ManufacturingContext([new("PLANT", "TEH"), new("MARKET", "DOMESTIC")]), conflicting));
        Assert.Equal(BomErrors.AmbiguousResolution, ex.Code);
    }

    [Fact]
    public async Task Multi_level_explosion_scales_scrap_converts_units_and_detects_cycles()
    {
        var store = new BomStoreStub();
        var kgId = 1L;
        var gId = 2L;
        var pcsId = 3L;
        var globalEdges = new[] { new UnitConversionEdge(kgId, gId, 1000m, false) };

        // Leaf raw material (Item 30, KG, no BOM)
        store.Datasets[30] = new BomResolutionDataset(30, "RM-GRANULE", "Plastic Granule", kgId, "KG", "kg", true, [], globalEdges);

        // Sub-assembly (Item 20, PCS): 10 PCS requires 2.5 KG of RM-GRANULE with 10% scrap => net 0.25 KG/PCS, gross 0.275 KG/PCS
        store.Datasets[20] = new BomResolutionDataset(
            20, "SA-SHELL", "Molded Shell", pcsId, "PCS", "pcs", true,
            [
                new BomVersionCandidateRecord(
                    2, "BOM-SA", "Shell BOM", 20,
                    new BomVersionDetails(
                        new BomVersionSummary(201, 1, 2, 1, BomVersionStatus.Active, 10m, pcsId, "PCS", "pcs", new DateOnly(2026, 1, 1), null, null, false, false, 0, Convert.ToBase64String(new byte[8])),
                        [new BomInputSummary(1, 1, 30, "RM-GRANULE", "Plastic Granule", 2.5m, kgId, "KG", "kg", 10m, false, null)],
                        [new BomOutputSummary(1, 1, 20, "SA-SHELL", "Molded Shell", BomOutputType.MainProduct, 10m, pcsId, "PCS", "pcs")],
                        null),
                    [])
            ],
            globalEdges);

        // Finished good (Item 10, PCS): 1 PCS requires 2 PCS of SA-SHELL (0% scrap) + 500 G of RM-GRANULE (0% scrap)
        store.Datasets[10] = new BomResolutionDataset(
            10, "FG-CASE", "Finished Case", pcsId, "PCS", "pcs", true,
            [
                new BomVersionCandidateRecord(
                    1, "BOM-FG", "Finished Case BOM", 10,
                    new BomVersionDetails(
                        new BomVersionSummary(101, 1, 1, 1, BomVersionStatus.Active, 1m, pcsId, "PCS", "pcs", new DateOnly(2026, 1, 1), null, null, false, false, 0, Convert.ToBase64String(new byte[8])),
                        [
                            new BomInputSummary(1, 1, 20, "SA-SHELL", "Molded Shell", 2m, pcsId, "PCS", "pcs", 0m, false, null),
                            new BomInputSummary(2, 2, 30, "RM-GRANULE", "Plastic Granule", 500m, gId, "G", "g", 0m, false, null)
                        ],
                        [
                            new BomOutputSummary(1, 1, 10, "FG-CASE", "Finished Case", BomOutputType.MainProduct, 1m, pcsId, "PCS", "pcs"),
                            new BomOutputSummary(2, 2, 30, "RM-GRANULE", "Regrind Runner", BomOutputType.ByProduct, 20m, gId, "G", "g")
                        ],
                        null),
                    [])
            ],
            globalEdges);

        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var scope = new OrganizationScope.Organization(1);
        var useCases = new BomUseCases(store, new ActorStub(actor), new PermissionStub((_, p) => p == BomPermissions.BomsRead));

        // A14: Zero requested quantity rejected before explosion
        var zeroRes = await useCases.ExplodeBom(scope, new ExplodeBomRequest(1, 10, new DateOnly(2026, 6, 1), 0m));
        Assert.False(zeroRes.Succeeded);
        Assert.Equal(BomErrors.QuantityInvalid, zeroRes.Error!.Code);

        // Valid 5 PCS explosion -> needs 10 PCS SA-SHELL -> needs 2.75 KG RM-GRANULE + L1 2500 G RM-GRANULE
        var exp = await useCases.ExplodeBom(scope, new ExplodeBomRequest(1, 10, new DateOnly(2026, 6, 1), 5m));
        Assert.True(exp.Succeeded);
        Assert.Equal(3, exp.Value!.ConsumedInputs.Count);
        var l1Sub = exp.Value.ConsumedInputs[0];
        Assert.Equal(1, l1Sub.Level);
        Assert.True(l1Sub.IsSubAssembly);
        Assert.Equal(10m, l1Sub.GrossQuantity);
        var l2Granule = exp.Value.ConsumedInputs[1];
        Assert.Equal(2, l2Granule.Level);
        Assert.Equal(2.5m, l2Granule.NetQuantity);
        Assert.Equal(2.75m, l2Granule.GrossQuantity);
        var l1Granule = exp.Value.ConsumedInputs[2];
        Assert.Equal(1, l1Granule.Level);
        Assert.Equal(2500m, l1Granule.GrossQuantity);
        Assert.Contains(exp.Value.ProducedOutputs, o => o.OutputType == BomOutputType.ByProduct && o.ProducedQuantity == 100m);

        // Indirect cycle: Item 20 now consumes Item 10
        store.Datasets[20] = new BomResolutionDataset(
            20, "SA-SHELL", "Molded Shell", pcsId, "PCS", "pcs", true,
            [
                new BomVersionCandidateRecord(
                    2, "BOM-SA", "Shell BOM", 20,
                    new BomVersionDetails(
                        new BomVersionSummary(201, 1, 2, 1, BomVersionStatus.Active, 1m, pcsId, "PCS", "pcs", new DateOnly(2026, 1, 1), null, null, false, false, 0, Convert.ToBase64String(new byte[8])),
                        [new BomInputSummary(1, 1, 10, "FG-CASE", "Finished Case", 1m, pcsId, "PCS", "pcs", 0m, false, null)],
                        [new BomOutputSummary(1, 1, 20, "SA-SHELL", "Molded Shell", BomOutputType.MainProduct, 1m, pcsId, "PCS", "pcs")],
                        null),
                    [])
            ],
            globalEdges);
        var cycle = await useCases.ExplodeBom(scope, new ExplodeBomRequest(1, 10, new DateOnly(2026, 6, 1), 5m));
        Assert.False(cycle.Succeeded);
        Assert.Equal(BomErrors.CircularDependency, cycle.Error!.Code);
    }

    [Fact]
    public void Bom_routes_permissions_and_error_localizations_are_wired()
    {
        var routes = typeof(Boms).GetCustomAttributes<RouteAttribute>().Select(a => a.Template).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("/boms", routes);
        Assert.Contains("/organizations/{OrganizationId:long}/boms", routes);

        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(BomPermissions.BomsRead, 2));
        Assert.True(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(BomPermissions.BomsManage, 1));
        Assert.False(PermissionCatalog.IsGrantedToBuiltInOrganizationAdmin(BomPermissions.BomsManage, 2));

        Assert.NotEqual("WriteUnavailable", RolePermissionLocalization.FailureKey(BomErrors.DirectSelfCycle));
        Assert.NotEqual("WriteUnavailable", RolePermissionLocalization.FailureKey(BomErrors.CircularDependency));
        Assert.NotEqual("WriteUnavailable", RolePermissionLocalization.FailureKey(BomErrors.NoActiveBomFound));
        Assert.NotEqual("WriteUnavailable", RolePermissionLocalization.FailureKey(BomErrors.AmbiguousResolution));
        Assert.NotEqual("WriteUnavailable", RolePermissionLocalization.FailureKey(BomErrors.MainOutputRequired));
    }
}
