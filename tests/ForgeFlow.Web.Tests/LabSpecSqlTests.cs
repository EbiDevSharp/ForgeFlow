using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Units;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class LabSpecSqlTests
{
    private static TrustedActor Attribution() => new(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());

    [OrganizationSqlServerFact]
    public async Task L02_styrene_monomer_classification_inheritance_po_receipt_lab_entry_concession_and_production_coa_work_end_to_end_in_sql()
    {
        await using var database = await DisposableLabDatabase.Create();
        var unitStore = new UnitOperationsStore(database.Factory);
        var itemStore = new ItemOperationsStore(database.Factory);
        var invStore = new SqlInventoryOperationsStore(database.Factory);
        var qcStore = new SqlQualityOperationsStore(database.Factory);
        var specStore = new SqlQualitySpecStore(() => database.Factory);
        var procStore = new SqlProcurementOperationsStore(database.Factory);

        long orgId, siteAId;
        await using (var setupDb = await database.Factory.CreateDbContextAsync())
        {
            var org = Organization.Create("ORG-LAB", "Polymer Petrochem");
            var siteA = org.CreateSite("PLANT-A", "Monomer Plant");
            setupDb.Organizations.Add(org);
            await setupDb.SaveChangesAsync(Attribution());
            orgId = org.Id;
            siteAId = siteA.Id;
        }

        var access = new OrganizationWriteAccess(Attribution(), _ => ValueTask.FromResult(true));
        Assert.True((await unitStore.SeedStandardUnits(orgId, access, default)).Succeeded);
        var kg = (await unitStore.ListActiveUnits(orgId, default)).Single(u => u.Code == "KG");

        // Classification hierarchy: CHEM_MONO (parent) -> STYRENE_FAM (child), Item attached to STYRENE_FAM
        var clsSet = (await itemStore.CreateClassificationSet(
            new CreateClassificationSetRequest(orgId, "MATERIAL_FAMILY", "Material Family", false, false), access, default)).Value!;
        var parentClass = (await itemStore.CreateClassification(
            new CreateClassificationRequest(orgId, clsSet.Id, null, "CHEM_MONO", "Monomer Chemicals"), access, default)).Value!;
        var childClass = (await itemStore.CreateClassification(
            new CreateClassificationRequest(orgId, clsSet.Id, parentClass.Id, "STYRENE_FAM", "Styrene Derivatives"), access, default)).Value!;

        var styrene = (await itemStore.CreateItem(new CreateItemRequest(
            orgId, "STYRENE-MON", "Styrene Monomer", null, kg.Id,
            CanBePurchased: true, CanBeManufactured: false, CanBeSold: false, CanBeConsumed: true,
            RequiresLotTracking: true, QualityControlMode: QualityControlMode.IncomingReceipts), access, default)).Value!;

        var attachCls = await itemStore.SaveItemClassifications(
            orgId, styrene.Summary.Id, Convert.FromBase64String(styrene.Summary.RowVersion), [childClass.Id], access, default);
        Assert.True(attachCls.Succeeded);

        var actor = Attribution();
        var allowAll = new AllowAllPermissions();
        var orgScope = new OrganizationScope.Organization(orgId);
        var siteScope = new OrganizationScope.Site(orgId, siteAId);

        var invUseCases = new InventoryUseCases(invStore, new FixedActor(actor), allowAll);
        var qcUseCases = new QualityUseCases(qcStore, new FixedActor(actor), allowAll);
        var specUseCases = new QualitySpecUseCases(specStore, new FixedActor(actor), allowAll);
        var procUseCases = new ProcurementUseCases(procStore, new FixedActor(actor), allowAll);

        var wh = (await invUseCases.CreateWarehouse(siteScope, new CreateWarehouseRequest(orgId, siteAId, "WH-RAW", "Raw Tank Farm", true))).Value!;
        var supplier = (await procUseCases.CreateSupplier(orgScope, new CreateSupplierRequest(orgId, "SUP-PETRO", "Petro Supply"))).Value!;

        // 1. Lab Parameters: MFI (g/10min) & PURITY (%)
        var mfiRes = await specUseCases.CreateParameter(orgScope, new CreateQualityParameterRequest(
            orgId, "MFI", "Melt Flow Index", "MFI", "g/10min", QualityParameterDataType.Numeric, 2, "ASTM D1238"));
        Assert.True(mfiRes.Succeeded);
        var purityRes = await specUseCases.CreateParameter(orgScope, new CreateQualityParameterRequest(
            orgId, "PURITY", "Purity Percentage", "%", "%", QualityParameterDataType.Numeric, 2, "GC-FID"));
        Assert.True(purityRes.Succeeded);

        // 2. Spec attached to ANCESTOR Classification CHEM_MONO
        var familySpecRes = await specUseCases.CreateSpec(orgScope, new CreateQualitySpecRequest(
            orgId, "SPEC-MONO-IN", "Monomer Incoming Spec",
            QualitySpecTargetKind.Classification, null, parentClass.Id,
            QualitySpecCheckpoint.Incoming, 1, "Applies to all monomer subclasses",
            [
                new QualitySpecLineInput(mfiRes.Value!.Id, 2.0m, 6.0m, 4.0m, null, IsMandatory: true),
                new QualitySpecLineInput(purityRes.Value!.Id, 99.0m, 100.0m, 99.8m, null, IsMandatory: false)
            ]));
        Assert.True(familySpecRes.Succeeded);

        // Inheritance check: STYRENE-MON inherits SPEC-MONO-IN via child -> parent classification walk
        var resolved = await specUseCases.ResolveApplicableSpec(orgScope, orgId, styrene.Summary.Id, QualitySpecCheckpoint.Incoming);
        Assert.True(resolved.Succeeded);
        Assert.NotNull(resolved.Value);
        Assert.Equal("SPEC-MONO-IN", resolved.Value!.Summary.Code);
        Assert.Equal(2, resolved.Value.Lines.Count);

        // 3. PO Receipt -> automatic QualityInspection auto-populated with MFI & PURITY lines
        var po = (await procUseCases.CreateDraft(siteScope, new CreatePurchaseOrderRequest(
            orgId, siteAId, supplier.Id, "PO-STY-1", new DateOnly(2026, 11, 1), null, wh.Id, null, null,
            [new PurchaseOrderLineInput(styrene.Summary.Id, 100m, kg.Id, 50m, null)]))).Value!;
        po = (await procUseCases.ApproveOrder(siteScope, orgId, po.Summary.Id, po.Summary.RowVersion)).Value!;

        var rcv = await procUseCases.ReceiveOrder(siteScope, new ReceivePurchaseOrderRequest(
            orgId, po.Summary.Id, wh.Id, "RCV-STY-1", "Tanker delivery", "po-sty-rcv-1", po.Summary.RowVersion,
            [new ReceivePurchaseOrderLineInput(po.Lines[0].Id, 100m, kg.Id, "STY-LOT-01", new DateOnly(2026, 10, 15), new DateOnly(2027, 10, 15), "Tanker 1")]));
        Assert.True(rcv.Succeeded);

        var inspPage = await qcUseCases.ListInspections(siteScope, orgId, new QualityInspectionListQuery(Search: "STY-LOT-01"));
        Assert.True(inspPage.Succeeded);
        var inspSummary = Assert.Single(inspPage.Value!.Items);
        var inspDetails = (await qcUseCases.GetInspection(siteScope, orgId, inspSummary.Id)).Value!;
        Assert.Equal(2, inspDetails.Lines.Count);
        Assert.Equal("MFI", inspDetails.Lines[0].Symbol);
        Assert.Equal("g/10min", inspDetails.Lines[0].UnitSymbol);
        Assert.True(inspDetails.Lines[0].IsMandatory);

        // 4. Lab enters out-of-spec MFI = 8.40 -> auto-evaluates IsPassed = false & enforces concession reason
        var oosRes = await qcUseCases.UpdateMeasurements(siteScope, new UpdateQualityMeasurementsRequest(
            orgId, inspSummary.Id, null, null,
            [
                new QualityInspectionLineInput("Melt Flow Index", inspDetails.Lines[0].ExpectedSpec, "8.40", true, null,
                    mfiRes.Value.Id, "MFI", "MFI", "g/10min", "ASTM D1238", QualityParameterDataType.Numeric, 2.0m, 6.0m, 4.0m, 8.40m, true),
                new QualityInspectionLineInput("Purity Percentage", inspDetails.Lines[1].ExpectedSpec, "99.85", true, null,
                    purityRes.Value.Id, "PURITY", "%", "%", "GC-FID", QualityParameterDataType.Numeric, 99.0m, 100.0m, 99.8m, 99.85m, false)
            ],
            inspDetails.Summary.RowVersion,
            "COA-2026-0881"));
        Assert.True(oosRes.Succeeded);
        Assert.False(oosRes.Value!.Lines[0].IsPassed); // auto-evaluated out-of-spec!
        Assert.True(oosRes.Value.Lines[1].IsPassed);
        Assert.Equal("COA-2026-0881", oosRes.Value.Summary.CertificateNumber);

        var blockedConcession = await qcUseCases.RecordDisposition(siteScope, new RecordQualityDispositionRequest(
            orgId, inspSummary.Id, 100m, 0m, kg.Id, null, null, null, oosRes.Value.Summary.RowVersion));
        Assert.False(blockedConcession.Succeeded);
        Assert.Equal(QualityErrors.ConcessionReasonRequired, blockedConcession.Error!.Code);

        // Update MFI to in-spec 4.25 and release full batch
        var passRes = await qcUseCases.UpdateMeasurements(siteScope, new UpdateQualityMeasurementsRequest(
            orgId, inspSummary.Id, null, "Retested sample B",
            [
                new QualityInspectionLineInput("Melt Flow Index", inspDetails.Lines[0].ExpectedSpec, "4.25", false, null,
                    mfiRes.Value.Id, "MFI", "MFI", "g/10min", "ASTM D1238", QualityParameterDataType.Numeric, 2.0m, 6.0m, 4.0m, 4.25m, true),
                new QualityInspectionLineInput("Purity Percentage", inspDetails.Lines[1].ExpectedSpec, "99.85", false, null,
                    purityRes.Value.Id, "PURITY", "%", "%", "GC-FID", QualityParameterDataType.Numeric, 99.0m, 100.0m, 99.8m, 99.85m, false)
            ],
            oosRes.Value.Summary.RowVersion,
            "COA-2026-0881"));
        Assert.True(passRes.Succeeded);
        Assert.True(passRes.Value!.Lines[0].IsPassed);

        var released = await qcUseCases.RecordDisposition(siteScope, new RecordQualityDispositionRequest(
            orgId, inspSummary.Id, 100m, 0m, kg.Id, null, "Released to production", null, passRes.Value.Summary.RowVersion));
        Assert.True(released.Succeeded);
        Assert.Equal(QualityInspectionStatus.Completed, released.Value!.Summary.Status);

        // 5. Production Operator (ONLY ProductionOrders.Read) reads Lot COA cleanly
        var prodOnlySpecs = new QualitySpecUseCases(specStore, new FixedActor(actor), new ExactPermission(ProductionOrderPermissions.ProductionOrdersRead));
        var coaRes = await prodOnlySpecs.GetLotCertificate(siteScope, orgId, styrene.Summary.Id, "STY-LOT-01");
        Assert.True(coaRes.Succeeded);
        var coa = coaRes.Value!;
        Assert.Equal("STYRENE-MON", coa.ItemCode);
        Assert.Equal("STY-LOT-01", coa.LotCode);
        var coaInsp = Assert.Single(coa.Inspections);
        Assert.Equal("COA-2026-0881", coaInsp.CertificateNumber);
        Assert.Equal(2, coaInsp.Measurements.Count);
        Assert.Equal("MFI", coaInsp.Measurements[0].Symbol);
        Assert.Equal(4.25m, coaInsp.Measurements[0].MeasuredNumericValue);
        Assert.Equal("g/10min", coaInsp.Measurements[0].UnitSymbol);

        // 6. Item-specific active Spec overrides Classification ancestor Spec (L-Q02)
        var itemSpecRes = await specUseCases.CreateSpec(orgScope, new CreateQualitySpecRequest(
            orgId, "SPEC-STY-ITEM", "Styrene Tight Spec",
            QualitySpecTargetKind.Item, styrene.Summary.Id, null,
            QualitySpecCheckpoint.Incoming, 1, null,
            [new QualitySpecLineInput(mfiRes.Value.Id, 3.5m, 4.8m, 4.1m, null, IsMandatory: true)]));
        Assert.True(itemSpecRes.Succeeded);

        var overridden = await prodOnlySpecs.ResolveApplicableSpec(siteScope, orgId, styrene.Summary.Id, QualitySpecCheckpoint.Incoming);
        Assert.True(overridden.Succeeded);
        Assert.Equal("SPEC-STY-ITEM", overridden.Value!.Summary.Code);
        Assert.Single(overridden.Value.Lines);
    }

    private sealed class FixedActor(TrustedActor actor) : ITrustedActorAccessor
    {
        public ValueTask<TrustedActor?> ResolveAsync(CancellationToken ct = default) => ValueTask.FromResult<TrustedActor?>(actor);
    }

    private sealed class AllowAllPermissions : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default) =>
            ValueTask.FromResult(new PermissionDecision(true));
    }

    private sealed class ExactPermission(string code) : IOrganizationPermissionEvaluator
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            TrustedActor actor, OrganizationScope scope, string permission, CancellationToken ct = default) =>
            ValueTask.FromResult(new PermissionDecision(permission == code));
    }

    private sealed class DisposableLabDatabase : IAsyncDisposable
    {
        private readonly SqlConnectionStringBuilder server;
        private readonly string name;
        private bool created;
        public IDbContextFactory<OrganizationDbContext> Factory { get; }

        private DisposableLabDatabase(SqlConnectionStringBuilder server, string name)
        {
            this.server = server;
            this.name = name;
            var dbConn = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
            var options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlServer(dbConn).Options;
            Factory = new TestDbContextFactory(options);
        }

        public static async Task<DisposableLabDatabase> Create()
        {
            var raw = Environment.GetEnvironmentVariable("FORGEFLOW_TEST_SQLSERVER");
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("FORGEFLOW_TEST_SQLSERVER must be set.");
            var builder = new SqlConnectionStringBuilder(raw) { InitialCatalog = "master" };
            var name = $"forgeflow_lab_{Guid.NewGuid():N}";
            var db = new DisposableLabDatabase(builder, name);
            try
            {
                await using var conn = new SqlConnection(db.server.ConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"CREATE DATABASE [{db.name}]";
                await cmd.ExecuteNonQueryAsync();
                db.created = true;
                await using var ctx = await db.Factory.CreateDbContextAsync();
                await ctx.Database.MigrateAsync();
                return db;
            }
            catch
            {
                await db.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!created) return;
            await using var conn = new SqlConnection(server.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await cmd.ExecuteNonQueryAsync();
            created = false;
        }

        private sealed class TestDbContextFactory(DbContextOptions<OrganizationDbContext> options)
            : IDbContextFactory<OrganizationDbContext>
        {
            public OrganizationDbContext CreateDbContext() => new(options);
        }
    }
}
