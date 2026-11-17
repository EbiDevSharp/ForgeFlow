using System.Reflection;
using ForgeFlow.Application.Boms;
using ForgeFlow.Application.BusinessPartners;
using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Items;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Procurement;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Application.Quality;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Routings;
using ForgeFlow.Application.Sales;
using ForgeFlow.Application.Units;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using ForgeFlow.Domain.Users;
using ForgeFlow.Infrastructure.Common;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DbUniqueConstraintExceptionTranslatorTests
{
    [Theory]
    [InlineData(2601, true)]
    [InlineData(2627, true)]
    [InlineData(1205, false)]
    [InlineData(1222, false)]
    [InlineData(547, false)]
    [InlineData(229, false)]
    [InlineData(208, false)]
    public void IsUniqueConstraintViolation_Accurately_Detects_2601_And_2627(int errorNumber, bool expected)
    {
        var sqlEx = CreateSqlException(errorNumber);
        var dbUpdateEx = new DbUpdateException("DB error", sqlEx);

        Assert.Equal(expected, DbUniqueConstraintExceptionTranslator.IsUniqueConstraintViolation(dbUpdateEx));
        Assert.Equal(expected, DbUniqueConstraintExceptionTranslator.IsUniqueConstraintViolation(sqlEx));
    }

    [Fact]
    public void IsUniqueConstraintViolation_Traverses_Inner_Exceptions()
    {
        var sqlEx = CreateSqlException(2601);
        var dbEx = new DbUpdateException("Cannot insert duplicate", sqlEx);
        var outerEx = new InvalidOperationException("Operation failed", dbEx);

        Assert.True(DbUniqueConstraintExceptionTranslator.IsUniqueConstraintViolation(outerEx));
    }

    [Fact]
    public void IsUniqueConstraintViolation_Returns_False_For_Non_Sql_Exceptions()
    {
        var ex = new InvalidOperationException("Plain exception");
        Assert.False(DbUniqueConstraintExceptionTranslator.IsUniqueConstraintViolation(ex));
    }

    [Theory]
    [InlineData("UX_Organization_Code", OrganizationSiteErrors.OrganizationCodeDuplicate)]
    [InlineData("UX_Site_Organization_Code", OrganizationSiteErrors.SiteCodeDuplicate)]
    [InlineData("UX_Site_Org_Code", OrganizationSiteErrors.SiteCodeDuplicate)]
    [InlineData("UX_Item_Organization_Code", ItemErrors.CodeDuplicate)]
    [InlineData("UX_ClassificationSet_Org_Code", ItemErrors.ClassificationSetCodeDuplicate)]
    [InlineData("UX_Classification_Org_Set_Code", ItemErrors.ClassificationCodeDuplicate)]
    [InlineData("UX_AttributeDefinition_Org_Code", ItemErrors.AttributeCodeDuplicate)]
    [InlineData("UX_ItemUnitConversion_Org_Item_From_To", ItemErrors.ItemUnitConversionDuplicate)]
    [InlineData("UX_Unit_Organization_Code", UnitErrors.CodeDuplicate)]
    [InlineData("UX_UnitConversion_Organization_From_To", UnitErrors.ConversionDuplicate)]
    [InlineData("UX_Currency_Organization_Code", UnitErrors.CurrencyCodeDuplicate)]
    [InlineData("UX_ContextDimension_Org_Code", BomErrors.ContextDimensionCodeDuplicate)]
    [InlineData("UX_Bom_Org_Code", BomErrors.CodeDuplicate)]
    [InlineData("UX_BomVersion_Org_Bom_Ver", BomErrors.VersionDuplicate)]
    [InlineData("UX_BusinessPartner_Org_Code", BusinessPartnerErrors.DuplicateCode)]
    [InlineData("UX_Supplier_Org_Code", ProcurementErrors.SupplierCodeDuplicate)]
    [InlineData("UX_Customer_Org_Code", SalesErrors.CustomerCodeDuplicate)]
    [InlineData("UX_Warehouse_Org_Code", InventoryErrors.CodeDuplicate)]
    [InlineData("UX_Warehouse_Org_Site_Default", InventoryErrors.CodeDuplicate)]
    [InlineData("UX_InventoryTransaction_Org_DocNum", InventoryErrors.DocumentNumberDuplicate)]
    [InlineData("UX_InventoryTransaction_Org_Reversal", InventoryErrors.AlreadyReversed)]
    [InlineData("UX_PurchaseOrder_Org_Number", ProcurementErrors.OrderNumberDuplicate)]
    [InlineData("UX_ProductionOrder_Org_Number", ProductionOrderErrors.OrderNumberDuplicate)]
    [InlineData("UX_SalesOrder_Org_Number", SalesErrors.OrderNumberDuplicate)]
    [InlineData("UX_DeliveryOrder_Org_Number", SalesErrors.DispatchNumberDuplicate)]
    [InlineData("UX_QualityInspection_Org_Number", QualityErrors.InspectionNumberDuplicate)]
    [InlineData("UX_QualityParameter_Org_Code", QualityErrors.ParameterCodeDuplicate)]
    [InlineData("UX_QualitySpec_Org_Code_Version", QualityErrors.SpecCodeDuplicate)]
    [InlineData("UX_QualitySpec_ActiveItem", QualityErrors.SpecActiveConflict)]
    [InlineData("UX_QualitySpec_ActiveClass", QualityErrors.SpecActiveConflict)]
    [InlineData("UX_WorkCenter_Org_Site_Code", RoutingErrors.WorkCenterCodeDuplicate)]
    [InlineData("UX_Routing_Org_Site_Code_Version", RoutingErrors.VersionDuplicate)]
    [InlineData("UX_Routing_Org_Site_Item_Default", RoutingErrors.RoutingCodeDuplicate)]
    [InlineData("UX_RoutingOperation_Routing_Seq", RoutingErrors.DuplicateSequence)]
    [InlineData("UX_User_NormalizedUserName", UserManagementErrors.UserNameDuplicate)]
    [InlineData("UX_RoleDefinition_Org_Name", RolePermissionErrors.NameDuplicate)]
    public void Translate_Maps_Index_Names_To_Exact_Domain_Errors(string indexName, string expectedErrorCode)
    {
        var msg = $"Violation of UNIQUE KEY constraint '{indexName}'. Cannot insert duplicate key in object 'table'.";
        var sqlEx = CreateSqlException(2627, msg);
        var dbUpdateEx = new DbUpdateException("DB update failed", sqlEx);

        var translated = DbUniqueConstraintExceptionTranslator.Translate(dbUpdateEx, "Fallback.Error");
        Assert.Equal(expectedErrorCode, translated);

        var canTranslate = DbUniqueConstraintExceptionTranslator.TryTranslate(dbUpdateEx, out var translatedCode);
        Assert.True(canTranslate);
        Assert.Equal(expectedErrorCode, translatedCode);
    }

    [Fact]
    public void Translate_Returns_Fallback_When_Index_Is_Unknown()
    {
        var msg = "Violation of UNIQUE KEY constraint 'UX_Unknown_Custom_Index'. Cannot insert duplicate key.";
        var sqlEx = CreateSqlException(2627, msg);
        var dbUpdateEx = new DbUpdateException("DB update failed", sqlEx);

        var translated = DbUniqueConstraintExceptionTranslator.Translate(dbUpdateEx, "Default.Conflict");
        Assert.Equal("Default.Conflict", translated);

        var canTranslate = DbUniqueConstraintExceptionTranslator.TryTranslate(dbUpdateEx, out var translatedCode, "Default.Conflict");
        Assert.True(canTranslate);
        Assert.Equal("Default.Conflict", translatedCode);
    }

    [Fact]
    public void TryTranslate_Returns_False_For_Non_Unique_Exceptions()
    {
        var sqlEx = CreateSqlException(547, "FK constraint failure");
        var dbUpdateEx = new DbUpdateException("DB update failed", sqlEx);

        var canTranslate = DbUniqueConstraintExceptionTranslator.TryTranslate(dbUpdateEx, out var translatedCode);
        Assert.False(canTranslate);
        Assert.Empty(translatedCode);
    }

    [Fact]
    public async Task SqlBusinessPartnerStore_Catches_DbUpdateException_And_Returns_DuplicateCode()
    {
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var access = new OrganizationWriteAccess(actor, _ => ValueTask.FromResult(true));

        var msg = "Violation of UNIQUE KEY constraint 'UX_BusinessPartner_Org_Code'. Cannot insert duplicate key.";
        var sqlEx = CreateSqlException(2627, msg);
        var dbUpdateEx = new DbUpdateException("Duplicate code", sqlEx);

        var factory = new ThrowingFactory(dbUpdateEx);
        var store = new SqlBusinessPartnerStore(factory);

        var request = new CreateBusinessPartnerRequest(
            1, "BP-DUP", PersonType.Legal, "Duplicate Co", null, null, null, "Duplicate Co", null, null, null, null, null, null, null, null, null, null, null, null, [1]);

        var result = await store.Create(request, access, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(BusinessPartnerErrors.DuplicateCode, result.Error!.Code);
    }

    [Fact]
    public async Task ItemOperationsStore_Catches_DbUpdateException_And_Returns_CodeDuplicate()
    {
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var access = new OrganizationWriteAccess(actor, _ => ValueTask.FromResult(true));

        var msg = "Cannot insert duplicate key row in object 'organization.Item' with unique index 'UX_Item_Organization_Code'.";
        var sqlEx = CreateSqlException(2601, msg);
        var dbUpdateEx = new DbUpdateException("Duplicate item code", sqlEx);

        var factory = new ThrowingFactory(dbUpdateEx);
        var store = new ItemOperationsStore(factory);

        var request = new CreateItemRequest(
            1, "ITEM-DUP", "Item Dup", null, 1, false, false, false, false, false, QualityControlMode.None);

        var result = await store.CreateItem(request, access, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ItemErrors.CodeDuplicate, result.Error!.Code);
    }

    [Fact]
    public async Task UnitOperationsStore_Catches_DbUpdateException_And_Returns_CodeDuplicate()
    {
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var access = new OrganizationWriteAccess(actor, _ => ValueTask.FromResult(true));

        var msg = "Violation of UNIQUE KEY constraint 'UX_Unit_Organization_Code'. Cannot insert duplicate key.";
        var sqlEx = CreateSqlException(2627, msg);
        var dbUpdateEx = new DbUpdateException("Duplicate unit code", sqlEx);

        var factory = new ThrowingFactory(dbUpdateEx);
        var store = new UnitOperationsStore(factory);

        var request = new CreateUnitRequest(1, "KG", "Kilogram", "kg", UnitDimension.Mass, 3);

        var result = await store.CreateUnit(request, access, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(UnitErrors.CodeDuplicate, result.Error!.Code);
    }

    [Fact]
    public async Task BomOperationsStore_Catches_DbUpdateException_And_Returns_CodeDuplicate()
    {
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var access = new OrganizationWriteAccess(actor, _ => ValueTask.FromResult(true));

        var msg = "Violation of UNIQUE KEY constraint 'UX_Bom_Org_Code'. Cannot insert duplicate key.";
        var sqlEx = CreateSqlException(2627, msg);
        var dbUpdateEx = new DbUpdateException("Duplicate bom code", sqlEx);

        var factory = new ThrowingFactory(dbUpdateEx);
        var store = new BomOperationsStore(factory);

        var request = new CreateBomRequest(1, "BOM-DUP", "Duplicate BOM", 10);

        var result = await store.CreateBom(request, access, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(BomErrors.CodeDuplicate, result.Error!.Code);
    }

    [Fact]
    public async Task SqlRoutingOperationsStore_Catches_DbUpdateException_And_Returns_WorkCenterCodeDuplicate()
    {
        var actor = new TrustedActor(1, ActorProvenance.ControlledBootstrap, Guid.NewGuid(), Guid.NewGuid());
        var access = new OrganizationWriteAccess(actor, _ => ValueTask.FromResult(true));

        var msg = "Violation of UNIQUE KEY constraint 'UX_WorkCenter_Org_Site_Code'. Cannot insert duplicate key.";
        var sqlEx = CreateSqlException(2627, msg);
        var dbUpdateEx = new DbUpdateException("Duplicate work center code", sqlEx);

        var factory = new ThrowingFactory(dbUpdateEx);
        var store = new SqlRoutingOperationsStore(factory);

        var request = new CreateWorkCenterRequest(1, 1, "WC-DUP", "Duplicate Work Center", WorkCenterType.Machine, 10m, 8m, 1000m, null);

        var result = await store.CreateWorkCenter(request, access, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(RoutingErrors.WorkCenterCodeDuplicate, result.Error!.Code);
    }

    private static SqlException CreateSqlException(int number, string message = "Simulated SQL Error")
    {
        var collection = (SqlErrorCollection)Activator.CreateInstance(
            typeof(SqlErrorCollection),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            null,
            null)!;

        var errorCtor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var parameters = errorCtor.GetParameters();
        var errorArgs = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (p.Name?.Equals("infoNumber", StringComparison.OrdinalIgnoreCase) == true)
                errorArgs[i] = number;
            else if (p.Name?.Equals("message", StringComparison.OrdinalIgnoreCase) == true)
                errorArgs[i] = message;
            else if (p.ParameterType == typeof(string))
                errorArgs[i] = "MockServer";
            else if (p.ParameterType == typeof(int))
                errorArgs[i] = number;
            else if (p.ParameterType == typeof(byte))
                errorArgs[i] = (byte)16;
            else
                errorArgs[i] = null;
        }

        var error = errorCtor.Invoke(errorArgs);

        var addMethod = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(SqlError)], null);
        addMethod?.Invoke(collection, [error]);

        var exceptionCtor = typeof(SqlException).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var exceptionParams = exceptionCtor.GetParameters();
        var exceptionArgs = new object?[exceptionParams.Length];
        for (var i = 0; i < exceptionParams.Length; i++)
        {
            var p = exceptionParams[i];
            if (p.ParameterType == typeof(string))
                exceptionArgs[i] = message;
            else if (p.ParameterType == typeof(SqlErrorCollection))
                exceptionArgs[i] = collection;
            else if (p.ParameterType == typeof(Exception))
                exceptionArgs[i] = null;
            else if (p.ParameterType == typeof(Guid))
                exceptionArgs[i] = Guid.NewGuid();
            else
                exceptionArgs[i] = null;
        }

        return (SqlException)exceptionCtor.Invoke(exceptionArgs);
    }

    private sealed class ThrowingFactory(Exception exceptionToThrow) : IDbContextFactory<OrganizationDbContext>
    {
        public OrganizationDbContext CreateDbContext() => throw exceptionToThrow;
    }
}
