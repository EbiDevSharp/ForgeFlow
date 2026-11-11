using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Domain.Quality;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class LabSpecTests
{
    private static readonly TrustedActor Actor1 = new(1, ActorProvenance.AuthenticatedSession, Guid.NewGuid(), Guid.NewGuid());
    private static readonly OrganizationScope.Organization OrgScope = new(10);

    [Fact]
    public void L01_parameter_spec_domain_bounds_and_concession_invariants_hold()
    {
        // Parameter validation
        Assert.Throws<QualityRuleException>(() =>
            QualityParameter.Create(10, "MFI", "Melt Flow Index", "   ", "g/10min", QualityParameterDataType.Numeric, 2, null));
        Assert.Throws<QualityRuleException>(() =>
            QualityParameter.Create(10, "MFI", "Melt Flow Index", new string('S', 25), "g/10min", QualityParameterDataType.Numeric, 2, null));
        Assert.Throws<QualityRuleException>(() =>
            QualityParameter.Create(10, "MFI", "Melt Flow Index", "MFI", new string('U', 25), QualityParameterDataType.Numeric, 2, null));
        Assert.Throws<QualityRuleException>(() =>
            QualityParameter.Create(10, "MFI", "Melt Flow Index", "MFI", "g/10min", QualityParameterDataType.Numeric, 7, null));

        var mfi = QualityParameter.Create(10, "MFI", "Melt Flow Index", "MFI", "g/10min", QualityParameterDataType.Numeric, 2, "ASTM D1238");
        Assert.Equal("MFI", mfi.Symbol);
        Assert.Equal("g/10min", mfi.UnitSymbol);
        Assert.Equal(QualityParameterDataType.Numeric, mfi.DataType);
        Assert.Equal((byte)2, mfi.DecimalPlaces);

        // Spec target mutual exclusion & range checks
        var validLines = new[]
        {
            new QualitySpecLineInputData(1, 2.0m, 6.0m, 4.0m, null, true, null)
        };
        Assert.Throws<QualityRuleException>(() =>
            QualitySpec.Create(10, "SPEC-1", "Bad Target", QualitySpecTargetKind.Item, 100, 200, QualitySpecCheckpoint.Incoming, 1, null, validLines));
        Assert.Throws<QualityRuleException>(() =>
            QualitySpec.Create(10, "SPEC-1", "Bad Range", QualitySpecTargetKind.Item, 100, null, QualitySpecCheckpoint.Incoming, 1, null,
                [new QualitySpecLineInputData(1, 8.0m, 2.0m, null, null, true, null)]));
        Assert.Throws<QualityRuleException>(() =>
            QualitySpec.Create(10, "SPEC-1", "Duplicate Param", QualitySpecTargetKind.Item, 100, null, QualitySpecCheckpoint.Incoming, 1, null,
                [
                    new QualitySpecLineInputData(1, 2.0m, 6.0m, null, null, true, null),
                    new QualitySpecLineInputData(1, 3.0m, 5.0m, null, null, false, null)
                ]));

        // Numeric pass/fail boundary evaluation
        Assert.True(QualityInspectionLine.EvaluateNumericPass(2.0m, 6.0m, 2.0m));
        Assert.True(QualityInspectionLine.EvaluateNumericPass(2.0m, 6.0m, 4.25m));
        Assert.True(QualityInspectionLine.EvaluateNumericPass(2.0m, 6.0m, 6.0m));
        Assert.False(QualityInspectionLine.EvaluateNumericPass(2.0m, 6.0m, 1.99m));
        Assert.False(QualityInspectionLine.EvaluateNumericPass(2.0m, 6.0m, 6.01m));

        // L-Q01 Out-of-spec Concession Rule
        var now = DateTime.UtcNow;
        var qi = QualityInspection.Create(10, 1, 5, 100, "STY-01", "QC-STY-1",
            QualityInspectionSourceType.Receipt, 11, null, 1, 50m, null);
        qi.ReplaceLines(
        [
            new QualityInspectionLineWriteData(
                "Melt Flow Index", "2 – 6 g/10min", "8.4", false, null,
                1, "MFI", "MFI", "g/10min", "ASTM D1238",
                QualityParameterDataType.Numeric, 2.0m, 6.0m, 4.0m, 8.4m, true)
        ]);
        qi.UpdateHeaderNotes(null, null, "COA-STY-01");
        Assert.Equal("COA-STY-01", qi.CertificateNumber);

        // Accepting without defect reason or concession notes must fail with ConcessionReasonRequired
        var ex = Assert.Throws<QualityRuleException>(() => qi.RecordDisposition(50m, 0m, null, null, 1, now));
        Assert.Equal(QualityErrors.ConcessionReasonRequired, ex.Code);

        // Supplying defect reason / concession note allows release under concession
        qi.RecordDisposition(50m, 0m, "CONCESSION-MFI", "Approved by lab manager for blend", 1, now);
        Assert.Equal(QualityInspectionStatus.Completed, qi.Status);
        Assert.Equal(50m, qi.AcceptedBaseQuantity);
    }

    [Fact]
    public async Task L01b_spec_use_cases_rbac_allows_production_and_inventory_readers_for_coa_and_blocks_mutations()
    {
        var store = new StubSpecStore();
        var prodOnlyActor = new FixedActor(Actor1);
        var prodPerms = new ExactPermissionEvaluator(ProductionOrderPermissions.ProductionOrdersRead);
        var useCases = new QualitySpecUseCases(store, prodOnlyActor, prodPerms);

        // L-Q03: ProductionOrders.Read can read COA, parameters, specs, and resolved spec
        var coa = await useCases.GetLotCertificate(OrgScope, 10, 100, "STY-LOT-1");
        Assert.True(coa.Succeeded);
        Assert.Equal("STY-LOT-1", coa.Value!.LotCode);

        var listParams = await useCases.ListParameters(OrgScope, 10, new QualityParameterListQuery());
        Assert.True(listParams.Succeeded);

        var listSpecs = await useCases.ListSpecs(OrgScope, 10, new QualitySpecListQuery());
        Assert.True(listSpecs.Succeeded);

        var resolved = await useCases.ResolveApplicableSpec(OrgScope, 10, 100, QualitySpecCheckpoint.Incoming);
        Assert.True(resolved.Succeeded);

        // Mutations remain strictly restricted to Quality.Manage
        var createParam = await useCases.CreateParameter(OrgScope, new CreateQualityParameterRequest(
            10, "MFI", "Melt Flow Index", "MFI", "g/10min", QualityParameterDataType.Numeric, 2, null));
        Assert.False(createParam.Succeeded);
        Assert.Equal(QualityErrors.AccessDenied, createParam.Error!.Code);

        var createSpec = await useCases.CreateSpec(OrgScope, new CreateQualitySpecRequest(
            10, "S1", "Spec 1", QualitySpecTargetKind.Item, 100, null, QualitySpecCheckpoint.Incoming, 1, null,
            [new QualitySpecLineInput(1, 2m, 6m, null, null, true)]));
        Assert.False(createSpec.Succeeded);
        Assert.Equal(QualityErrors.AccessDenied, createSpec.Error!.Code);

        var applySpec = await useCases.ApplySpecToInspection(OrgScope, 10, 1, Convert.ToBase64String([1, 0, 0, 0]));
        Assert.False(applySpec.Succeeded);
        Assert.Equal(QualityErrors.AccessDenied, applySpec.Error!.Code);

        // Also check Inventory.Read works identically for COA access
        var invUseCases = new QualitySpecUseCases(store, prodOnlyActor, new ExactPermissionEvaluator(InventoryPermissions.InventoryRead));
        Assert.True((await invUseCases.GetLotCertificate(OrgScope, 10, 100, "STY-LOT-1")).Succeeded);
    }

    private sealed class FixedActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class ExactPermissionEvaluator(params string[] allowed) : IOrganizationPermissionEvaluator
    {
        private readonly HashSet<string> _allowed = [.. allowed];
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default) =>
            ValueTask.FromResult(new PermissionDecision(_allowed.Contains(permission)));
    }

    private sealed class StubSpecStore : IQualitySpecOperationsStore
    {
        public Task<OrganizationPage<QualityParameterSummary>> ListParameters(long organizationId, QualityParameterListQuery query, CancellationToken ct) =>
            Task.FromResult(new OrganizationPage<QualityParameterSummary>([], 0, 1, 25));

        public Task<QualityParameterSummary?> GetParameter(long organizationId, long id, CancellationToken ct) =>
            Task.FromResult<QualityParameterSummary?>(null);

        public Task<OrganizationResult<QualityParameterSummary>> CreateParameter(CreateQualityParameterRequest request, OrganizationWriteAccess access, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<OrganizationResult<QualityParameterSummary>> UpdateParameter(long organizationId, long id, byte[] version, UpdateQualityParameterRequest request, OrganizationWriteAccess access, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<OrganizationResult<QualityParameterSummary>> SetParameterActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<OrganizationPage<QualitySpecSummary>> ListSpecs(long organizationId, QualitySpecListQuery query, CancellationToken ct) =>
            Task.FromResult(new OrganizationPage<QualitySpecSummary>([], 0, 1, 25));

        public Task<QualitySpecDetails?> GetSpec(long organizationId, long id, CancellationToken ct) =>
            Task.FromResult<QualitySpecDetails?>(null);

        public Task<QualitySpecDetails?> ResolveApplicableSpec(long organizationId, long itemId, QualitySpecCheckpoint checkpoint, CancellationToken ct) =>
            Task.FromResult<QualitySpecDetails?>(null);

        public Task<OrganizationResult<QualitySpecDetails>> CreateSpec(CreateQualitySpecRequest request, IReadOnlyList<QualitySpecLineInputData> resolvedLines, OrganizationWriteAccess access, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<OrganizationResult<QualitySpecDetails>> UpdateSpec(long organizationId, long id, byte[] version, string name, QualitySpecCheckpoint checkpoint, string? notes, IReadOnlyList<QualitySpecLineInputData> resolvedLines, OrganizationWriteAccess access, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<OrganizationResult<QualitySpecDetails>> SetSpecActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<OrganizationResult<QualityInspectionDetails>> ApplySpecToInspection(long organizationId, long inspectionId, byte[] version, OrganizationWriteAccess access, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<LotCertificateSummary?> GetLotCertificate(long organizationId, long itemId, string lotCode, CancellationToken ct) =>
            Task.FromResult<LotCertificateSummary?>(new LotCertificateSummary(
                organizationId, itemId, "STYRENE-MON", "Styrene Monomer", lotCode,
                null, null, null, []));
    }
}
