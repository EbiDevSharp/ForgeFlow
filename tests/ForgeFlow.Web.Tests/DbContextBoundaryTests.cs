using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Identity.Configurations;
using ForgeFlow.Infrastructure.Organizations;
using ForgeFlow.Infrastructure.Organizations.Configurations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DbContextBoundaryTests
{
    private static readonly Type[] ApprovedAuthEntityTypes =
    [
        typeof(ApplicationAccount),
        typeof(IdentityUserClaim<long>),
        typeof(IdentityUserLogin<long>),
        typeof(IdentityUserToken<long>),
        typeof(RoleDefinition),
        typeof(RolePermission),
        typeof(UserPermissionGrant),
        typeof(OrganizationMembership),
        typeof(SiteMembership),
        typeof(ScopedRoleAssignment),
        typeof(AccountSession),
        typeof(BootstrapState),
        typeof(SecurityEvent),
        typeof(UserPreference),
        typeof(UserNotificationPreference),
        typeof(UserNotification),
        typeof(UserPasskey)
    ];

    private static readonly Type[] OperationalDomainEntityTypes =
    [
        typeof(Item),
        typeof(ItemLot),
        typeof(ItemClassification),
        typeof(ItemUnitConversion),
        typeof(ItemAttributeValue),
        typeof(ClassificationSet),
        typeof(Classification),
        typeof(AttributeDefinition),
        typeof(Bom),
        typeof(BomVersion),
        typeof(BomInput),
        typeof(BomOutput),
        typeof(BomApplicability),
        typeof(BomApplicabilityCondition),
        typeof(SalesOrder),
        typeof(SalesOrderLine),
        typeof(DeliveryOrder),
        typeof(DeliveryOrderLine),
        typeof(Customer),
        typeof(PurchaseOrder),
        typeof(PurchaseOrderLine),
        typeof(Supplier),
        typeof(ProductionOrder),
        typeof(ProductionOrderInput),
        typeof(ProductionOrderOutput),
        typeof(ProductionOrderContext),
        typeof(ProductionOrderOperation),
        typeof(Warehouse),
        typeof(InventoryTransaction),
        typeof(InventoryLedgerEntry),
        typeof(InventoryBalance),
        typeof(StockReservation),
        typeof(QualityInspection),
        typeof(QualityInspectionLine),
        typeof(QualityParameter),
        typeof(QualitySpec),
        typeof(QualitySpecLine),
        typeof(WorkCenter),
        typeof(Routing),
        typeof(RoutingOperation),
        typeof(BusinessPartner),
        typeof(BusinessPartnerRoleDefinition),
        typeof(BusinessPartnerRoleAssignment),
        typeof(IranProvince),
        typeof(IranCity),
        typeof(Unit),
        typeof(UnitConversion),
        typeof(Currency),
        typeof(ContextDimension),
        typeof(OrganizationChange),
        typeof(DocumentSequence)
    ];

    [Fact]
    public void AuthDbContext_Contains_Strictly_Identity_And_Auth_Entities_And_No_Erp_Domain_Entities()
    {
        using var authDb = new AuthDesignTimeFactory().CreateDbContext([]);
        var model = authDb.GetService<IDesignTimeModel>().Model;
        var entityTypes = model.GetEntityTypes().ToList();

        // 17 auth tables + 2 tenant anchors (Organization, Site) excluded from migrations
        Assert.Equal(19, entityTypes.Count);

        var authSchemaEntities = entityTypes
            .Where(e => e.GetSchema() == "auth")
            .Select(e => e.ClrType)
            .ToList();

        Assert.Equal(ApprovedAuthEntityTypes.Length, authSchemaEntities.Count);
        foreach (var approvedType in ApprovedAuthEntityTypes)
        {
            Assert.Contains(approvedType, authSchemaEntities);
        }

        // Organization and Site must be strictly excluded from migrations in AuthDbContext
        var orgEntity = model.FindEntityType(typeof(Organization));
        Assert.NotNull(orgEntity);
        Assert.True(orgEntity.IsTableExcludedFromMigrations());
        Assert.Equal("organization", orgEntity.GetSchema());

        var siteEntity = model.FindEntityType(typeof(Site));
        Assert.NotNull(siteEntity);
        Assert.True(siteEntity.IsTableExcludedFromMigrations());
        Assert.Equal("organization", siteEntity.GetSchema());

        // Strictly verify that zero operational ERP domain entities bled into AuthDbContext
        foreach (var erpType in OperationalDomainEntityTypes)
        {
            Assert.Null(model.FindEntityType(erpType));
        }
    }

    [Fact]
    public void OrganizationDbContext_Contains_Strictly_Erp_Domain_Entities_And_No_Identity_Or_Auth_Entities()
    {
        using var orgDb = new OrganizationDesignTimeFactory().CreateDbContext([]);
        var model = orgDb.Model;
        var entityTypes = model.GetEntityTypes().ToList();

        // All entities must belong to organization schema
        Assert.All(entityTypes, e => Assert.Equal("organization", e.GetSchema()));

        // Strictly verify that zero identity/auth entities bled into OrganizationDbContext
        foreach (var authType in ApprovedAuthEntityTypes)
        {
            Assert.Null(model.FindEntityType(authType));
        }

        // Verify that all operational ERP entities are registered in OrganizationDbContext
        foreach (var erpType in OperationalDomainEntityTypes)
        {
            Assert.NotNull(model.FindEntityType(erpType));
        }

        Assert.NotNull(model.FindEntityType(typeof(Organization)));
        Assert.NotNull(model.FindEntityType(typeof(Site)));
    }

    [Fact]
    public void MarkerInterfaces_Strictly_Partition_All_EntityConfigurations_Without_Overlap()
    {
        var infraAssembly = typeof(AuthDbContext).Assembly;
        var configurationTypes = infraAssembly.GetTypes()
            .Where(t => !t.IsAbstract && t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>)))
            .ToList();

        Assert.NotEmpty(configurationTypes);

        foreach (var configType in configurationTypes)
        {
            var isAuth = typeof(IAuthEntityConfiguration).IsAssignableFrom(configType);
            var isOrg = typeof(IOrganizationEntityConfiguration).IsAssignableFrom(configType);

            // Exactly one marker interface must be implemented (never none, never both)
            Assert.True(isAuth ^ isOrg,
                $"Configuration type '{configType.FullName}' must implement exactly one of IAuthEntityConfiguration or IOrganizationEntityConfiguration.");
        }

        var authConfigs = configurationTypes.Where(t => typeof(IAuthEntityConfiguration).IsAssignableFrom(t)).ToList();
        var orgConfigs = configurationTypes.Where(t => typeof(IOrganizationEntityConfiguration).IsAssignableFrom(t)).ToList();

        Assert.Equal(19, authConfigs.Count);
        Assert.Equal(53, orgConfigs.Count);
    }
}
