using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ForgeFlow.Infrastructure.Organizations;

internal static class OrganizationModel
{
    internal const string Schema = "organization";
    internal const string CodeCollation = "Latin1_General_100_BIN2";
    private static readonly ValueConverter<DateTime, DateTime> Utc = new(
        value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private const string CodeCheck = "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";
    private const string NameCheck = "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0";
    private const string SymbolCheck = "DATALENGTH([Symbol]) BETWEEN 2 AND 64 AND DATALENGTH([Symbol]) = DATALENGTH(LTRIM(RTRIM([Symbol]))) AND DATALENGTH(LTRIM(RTRIM([Symbol]))) > 0";
    private const string MetadataCheck = "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))";

    internal const string OrganizationChangeEventSql =
        "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ("
        + "'Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated',"
        + "'Unit.Created','Unit.Updated','Unit.Activated','Unit.Deactivated',"
        + "'UnitConversion.Created','UnitConversion.Updated','UnitConversion.Activated','UnitConversion.Deactivated',"
        + "'Currency.Created','Currency.Updated','Currency.Activated','Currency.Deactivated',"
        + "'Item.Created','Item.Updated','Item.Activated','Item.Deactivated',"
        + "'ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated',"
        + "'ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated',"
        + "'AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated',"
        + "'ItemAttributeValue.Updated',"
        + "'ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated',"
        + "'Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated',"
        + "'BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved',"
        + "'ItemLot.Updated','Supplier.Created','Supplier.Updated','Supplier.Activated','Supplier.Deactivated')) "
        + "OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled','Warehouse.Created','Warehouse.Updated','Warehouse.Activated','Warehouse.Deactivated','InventoryTransaction.Posted','InventoryTransaction.Reversed','QualityInspection.Created','QualityInspection.Updated','QualityInspection.Disposed','QualityInspection.Cancelled','PurchaseOrder.Created','PurchaseOrder.Updated','PurchaseOrder.Approved','PurchaseOrder.Received','PurchaseOrder.Closed','PurchaseOrder.Cancelled')))";

    internal static void Configure(ModelBuilder model)
    {
        var organization = model.Entity<Organization>();
        Business(organization, "Organization");
        organization.Property(o => o.Id).UseIdentityColumn();
        organization.HasKey(o => o.Id).HasName("PK_Organization");
        organization.HasIndex(o => o.Code).IsUnique().HasDatabaseName("UX_Organization_Code");
        organization.HasIndex(o => new { o.IsActive, o.Code, o.Id }).HasDatabaseName("IX_Organization_IsActive_Code");

        var site = model.Entity<Site>();
        Business(site, "Site");
        site.Property(s => s.Id).UseIdentityColumn();
        site.HasKey(s => s.Id).HasName("PK_Site");
        site.Property(s => s.OrganizationId).IsRequired();
        site.HasAlternateKey(s => new { s.OrganizationId, s.Id }).HasName("AK_Site_Organization_Id");
        site.HasIndex(s => new { s.OrganizationId, s.Code }).IsUnique().HasDatabaseName("UX_Site_Organization_Code");
        site.HasIndex(s => new { s.OrganizationId, s.IsActive, s.Code, s.Id }).HasDatabaseName("IX_Site_Organization_IsActive_Code");
        site.HasOne(s => s.Organization).WithMany(o => o.Sites).HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Site_Organization");
        organization.Navigation(o => o.Sites).HasField("sites").UsePropertyAccessMode(PropertyAccessMode.Field);
        organization.Navigation(o => o.Sites).AutoInclude();
        site.Navigation(s => s.Organization).HasField("organization").UsePropertyAccessMode(PropertyAccessMode.Field);

        var audit = model.Entity<OrganizationChange>();
        audit.ToTable("OrganizationChange", Schema, table =>
        {
            table.HasCheckConstraint("CK_OrganizationChange_Actor", "[ActorId] > 0");
            table.HasCheckConstraint("CK_OrganizationChange_Json", "ISJSON([ChangesJson]) = 1");
            table.HasCheckConstraint("CK_OrganizationChange_Event", OrganizationChangeEventSql);
        });
        audit.HasKey(a => a.Id).HasName("PK_OrganizationChange");
        audit.Property(a => a.Id).UseIdentityColumn();
        audit.Property(a => a.EventCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        audit.Property(a => a.ChangesJson).HasMaxLength(2000).IsRequired();
        audit.Property(a => a.OccurredAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        audit.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_OrganizationChange_Organization");
        audit.HasOne<Site>().WithMany().HasForeignKey(a => new { a.OrganizationId, a.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_OrganizationChange_Site");
        audit.HasIndex(a => new { a.OrganizationId, a.OccurredAtUtc, a.Id }).HasDatabaseName("IX_OrganizationChange_Organization_Time");
        audit.HasIndex(a => new { a.OrganizationId, a.SiteId, a.OccurredAtUtc, a.Id }).HasDatabaseName("IX_OrganizationChange_Site_Time");

        var unit = model.Entity<Unit>();
        unit.ToTable("Unit", Schema, table =>
        {
            table.HasCheckConstraint("CK_Unit_Code", CodeCheck);
            table.HasCheckConstraint("CK_Unit_Name", NameCheck);
            table.HasCheckConstraint("CK_Unit_Symbol", SymbolCheck);
            table.HasCheckConstraint("CK_Unit_Dimension", "[Dimension] BETWEEN 1 AND 6");
            table.HasCheckConstraint("CK_Unit_PrecisionScale", "[PrecisionScale] BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_Unit_Metadata", MetadataCheck);
        });
        unit.HasKey(u => u.Id).HasName("PK_Unit");
        unit.Property(u => u.Id).UseIdentityColumn();
        unit.Property(u => u.OrganizationId).IsRequired();
        unit.Property(u => u.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        unit.Property(u => u.Name).HasMaxLength(200).IsRequired();
        unit.Property(u => u.Symbol).HasMaxLength(32).IsRequired();
        unit.Property(u => u.Dimension).HasConversion<byte>().IsRequired();
        unit.Property(u => u.PrecisionScale).IsRequired();
        unit.Property(u => u.IsActive).IsRequired();
        AddAuditMetadata(unit);
        unit.HasAlternateKey(u => new { u.OrganizationId, u.Id }).HasName("AK_Unit_Organization_Id");
        unit.HasIndex(u => new { u.OrganizationId, u.Code }).IsUnique().HasDatabaseName("UX_Unit_Organization_Code");
        unit.HasIndex(u => new { u.OrganizationId, u.IsActive, u.Code, u.Id }).HasDatabaseName("IX_Unit_Organization_IsActive_Code");
        unit.HasOne<Organization>().WithMany().HasForeignKey(u => u.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Unit_Organization");

        var conversion = model.Entity<UnitConversion>();
        conversion.ToTable("UnitConversion", Schema, table =>
        {
            table.HasCheckConstraint("CK_UnitConversion_DifferentUnits", "[FromUnitId] <> [ToUnitId]");
            table.HasCheckConstraint("CK_UnitConversion_PositiveFactor", "[Factor] > 0");
            table.HasCheckConstraint("CK_UnitConversion_Metadata", MetadataCheck);
        });
        conversion.HasKey(c => c.Id).HasName("PK_UnitConversion");
        conversion.Property(c => c.Id).UseIdentityColumn();
        conversion.Property(c => c.OrganizationId).IsRequired();
        conversion.Property(c => c.FromUnitId).IsRequired();
        conversion.Property(c => c.ToUnitId).IsRequired();
        conversion.Property(c => c.Factor).HasPrecision(18, 6).IsRequired();
        conversion.Property(c => c.IsActive).IsRequired();
        AddAuditMetadata(conversion);
        conversion.HasIndex(c => new { c.OrganizationId, c.FromUnitId, c.ToUnitId }).IsUnique().HasDatabaseName("UX_UnitConversion_Organization_From_To");
        conversion.HasOne<Unit>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.FromUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_UnitConversion_FromUnit");
        conversion.HasOne<Unit>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.ToUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_UnitConversion_ToUnit");

        const string currencyCodeCheck = "DATALENGTH([Code]) = 3 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'";
        var currency = model.Entity<Currency>();
        currency.ToTable("Currency", Schema, table =>
        {
            table.HasCheckConstraint("CK_Currency_Code", currencyCodeCheck);
            table.HasCheckConstraint("CK_Currency_Name", NameCheck);
            table.HasCheckConstraint("CK_Currency_Symbol", SymbolCheck);
            table.HasCheckConstraint("CK_Currency_PrecisionScale", "[PrecisionScale] BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_Currency_Metadata", MetadataCheck);
        });
        currency.HasKey(c => c.Id).HasName("PK_Currency");
        currency.Property(c => c.Id).UseIdentityColumn();
        currency.Property(c => c.OrganizationId).IsRequired();
        currency.Property(c => c.Code).HasMaxLength(3).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        currency.Property(c => c.Name).HasMaxLength(200).IsRequired();
        currency.Property(c => c.Symbol).HasMaxLength(32).IsRequired();
        currency.Property(c => c.PrecisionScale).IsRequired();
        currency.Property(c => c.IsActive).IsRequired();
        AddAuditMetadata(currency);
        currency.HasAlternateKey(c => new { c.OrganizationId, c.Id }).HasName("AK_Currency_Organization_Id");
        currency.HasAlternateKey(c => new { c.OrganizationId, c.Code }).HasName("AK_Currency_Organization_Code");
        currency.HasIndex(c => new { c.OrganizationId, c.Code }).IsUnique().HasDatabaseName("UX_Currency_Organization_Code");
        currency.HasIndex(c => new { c.OrganizationId, c.IsActive, c.Code, c.Id }).HasDatabaseName("IX_Currency_Organization_IsActive_Code");
        currency.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Currency_Organization");

        var classSet = model.Entity<ClassificationSet>();
        classSet.ToTable("ClassificationSet", Schema, table =>
        {
            table.HasCheckConstraint("CK_ClassificationSet_Code", CodeCheck);
            table.HasCheckConstraint("CK_ClassificationSet_Name", NameCheck);
            table.HasCheckConstraint("CK_ClassificationSet_Metadata", MetadataCheck);
        });
        classSet.HasKey(s => s.Id).HasName("PK_ClassificationSet");
        classSet.Property(s => s.Id).UseIdentityColumn();
        classSet.Property(s => s.OrganizationId).IsRequired();
        classSet.Property(s => s.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        classSet.Property(s => s.Name).HasMaxLength(200).IsRequired();
        classSet.Property(s => s.IsMandatory).IsRequired();
        classSet.Property(s => s.AllowMultiple).IsRequired();
        classSet.Property(s => s.IsActive).IsRequired();
        AddAuditMetadata(classSet);
        classSet.HasAlternateKey(s => new { s.OrganizationId, s.Id }).HasName("AK_ClassificationSet_Org_Id");
        classSet.HasIndex(s => new { s.OrganizationId, s.Code }).IsUnique().HasDatabaseName("UX_ClassificationSet_Org_Code");
        classSet.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ClassificationSet_Organization");

        var classification = model.Entity<Classification>();
        classification.ToTable("Classification", Schema, table =>
        {
            table.HasCheckConstraint("CK_Classification_Code", CodeCheck);
            table.HasCheckConstraint("CK_Classification_Name", NameCheck);
            table.HasCheckConstraint("CK_Classification_NoSelfParent", "[ParentClassificationId] IS NULL OR [ParentClassificationId] <> [Id]");
            table.HasCheckConstraint("CK_Classification_Metadata", MetadataCheck);
        });
        classification.HasKey(c => c.Id).HasName("PK_Classification");
        classification.Property(c => c.Id).UseIdentityColumn();
        classification.Property(c => c.OrganizationId).IsRequired();
        classification.Property(c => c.ClassificationSetId).IsRequired();
        classification.Property(c => c.ParentClassificationId);
        classification.Property(c => c.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        classification.Property(c => c.Name).HasMaxLength(200).IsRequired();
        classification.Property(c => c.IsActive).IsRequired();
        AddAuditMetadata(classification);
        classification.HasAlternateKey(c => new { c.OrganizationId, c.Id }).HasName("AK_Classification_Org_Id");
        classification.HasIndex(c => new { c.OrganizationId, c.ClassificationSetId, c.Code }).IsUnique().HasDatabaseName("UX_Classification_Org_Set_Code");
        classification.HasOne<ClassificationSet>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.ClassificationSetId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Classification_Set");
        classification.HasOne<Classification>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.ParentClassificationId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Classification_Parent");

        var item = model.Entity<Item>();
        item.ToTable("Item", Schema, table =>
        {
            table.HasCheckConstraint("CK_Item_Code", CodeCheck);
            table.HasCheckConstraint("CK_Item_Name", NameCheck);
            table.HasCheckConstraint("CK_Item_Capability", "[CanBePurchased] = 1 OR [CanBeManufactured] = 1 OR [CanBeSold] = 1 OR [CanBeConsumed] = 1");
            table.HasCheckConstraint("CK_Item_QualityControlMode", "[QualityControlMode] BETWEEN 0 AND 3");
            table.HasCheckConstraint("CK_Item_Metadata", MetadataCheck);
        });
        item.HasKey(i => i.Id).HasName("PK_Item");
        item.Property(i => i.Id).UseIdentityColumn();
        item.Property(i => i.OrganizationId).IsRequired();
        item.Property(i => i.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        item.Property(i => i.Name).HasMaxLength(200).IsRequired();
        item.Property(i => i.Description).HasMaxLength(500);
        item.Property(i => i.BaseUnitId).IsRequired();
        item.Property(i => i.CanBePurchased).IsRequired();
        item.Property(i => i.CanBeManufactured).IsRequired();
        item.Property(i => i.CanBeSold).IsRequired();
        item.Property(i => i.CanBeConsumed).IsRequired();
        item.Property(i => i.RequiresLotTracking).HasDefaultValue(false).IsRequired();
        item.Property(i => i.QualityControlMode).HasConversion<byte>().HasDefaultValue(QualityControlMode.None).IsRequired();
        item.Property(i => i.IsActive).IsRequired();
        AddAuditMetadata(item);
        item.HasAlternateKey(i => new { i.OrganizationId, i.Id }).HasName("AK_Item_Organization_Id");
        item.HasIndex(i => new { i.OrganizationId, i.Code }).IsUnique().HasDatabaseName("UX_Item_Organization_Code");
        item.HasIndex(i => new { i.OrganizationId, i.IsActive, i.Code, i.Id }).HasDatabaseName("IX_Item_Organization_IsActive_Code");
        item.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Item_Organization");
        item.HasOne<Unit>().WithMany().HasForeignKey(i => new { i.OrganizationId, i.BaseUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Item_BaseUnit");

        var itemClass = model.Entity<ItemClassification>();
        itemClass.ToTable("ItemClassification", Schema, table =>
        {
            table.HasCheckConstraint("CK_ItemClassification_Metadata", MetadataCheck);
        });
        itemClass.HasKey(ic => ic.Id).HasName("PK_ItemClassification");
        itemClass.Property(ic => ic.Id).UseIdentityColumn();
        itemClass.Property(ic => ic.OrganizationId).IsRequired();
        itemClass.Property(ic => ic.ItemId).IsRequired();
        itemClass.Property(ic => ic.ClassificationId).IsRequired();
        AddAuditMetadata(itemClass);
        itemClass.HasIndex(ic => new { ic.OrganizationId, ic.ItemId, ic.ClassificationId }).IsUnique().HasDatabaseName("UX_ItemClassification_Org_Item_Class");
        itemClass.HasOne<Item>().WithMany().HasForeignKey(ic => new { ic.OrganizationId, ic.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ItemClassification_Item");
        itemClass.HasOne<Classification>().WithMany().HasForeignKey(ic => new { ic.OrganizationId, ic.ClassificationId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ItemClassification_Classification");

        var itemConv = model.Entity<ItemUnitConversion>();
        itemConv.ToTable("ItemUnitConversion", Schema, table =>
        {
            table.HasCheckConstraint("CK_ItemUnitConversion_DifferentUnits", "[FromUnitId] <> [ToUnitId]");
            table.HasCheckConstraint("CK_ItemUnitConversion_PositiveFactor", "[Factor] > 0");
            table.HasCheckConstraint("CK_ItemUnitConversion_Metadata", MetadataCheck);
        });
        itemConv.HasKey(c => c.Id).HasName("PK_ItemUnitConversion");
        itemConv.Property(c => c.Id).UseIdentityColumn();
        itemConv.Property(c => c.OrganizationId).IsRequired();
        itemConv.Property(c => c.ItemId).IsRequired();
        itemConv.Property(c => c.FromUnitId).IsRequired();
        itemConv.Property(c => c.ToUnitId).IsRequired();
        itemConv.Property(c => c.Factor).HasPrecision(18, 6).IsRequired();
        itemConv.Property(c => c.IsActive).IsRequired();
        AddAuditMetadata(itemConv);
        itemConv.HasIndex(c => new { c.OrganizationId, c.ItemId, c.FromUnitId, c.ToUnitId }).IsUnique().HasDatabaseName("UX_ItemUnitConversion_Org_Item_From_To");
        itemConv.HasOne<Item>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ItemUnitConversion_Item");
        itemConv.HasOne<Unit>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.FromUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ItemUnitConversion_FromUnit");
        itemConv.HasOne<Unit>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.ToUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ItemUnitConversion_ToUnit");

        var attrDef = model.Entity<AttributeDefinition>();
        attrDef.ToTable("AttributeDefinition", Schema, table =>
        {
            table.HasCheckConstraint("CK_AttributeDefinition_Code", CodeCheck);
            table.HasCheckConstraint("CK_AttributeDefinition_Name", NameCheck);
            table.HasCheckConstraint("CK_AttributeDefinition_DataType", "[DataType] BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_AttributeDefinition_Metadata", MetadataCheck);
        });
        attrDef.HasKey(a => a.Id).HasName("PK_AttributeDefinition");
        attrDef.Property(a => a.Id).UseIdentityColumn();
        attrDef.Property(a => a.OrganizationId).IsRequired();
        attrDef.Property(a => a.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        attrDef.Property(a => a.Name).HasMaxLength(200).IsRequired();
        attrDef.Property(a => a.DataType).HasConversion<byte>().IsRequired();
        attrDef.Property(a => a.UnitId);
        attrDef.Property(a => a.IsRequired).IsRequired();
        attrDef.Property(a => a.IsActive).IsRequired();
        AddAuditMetadata(attrDef);
        attrDef.HasAlternateKey(a => new { a.OrganizationId, a.Id }).HasName("AK_AttributeDefinition_Org_Id");
        attrDef.HasIndex(a => new { a.OrganizationId, a.Code }).IsUnique().HasDatabaseName("UX_AttributeDefinition_Org_Code");
        attrDef.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_AttributeDefinition_Organization");
        attrDef.HasOne<Unit>().WithMany().HasForeignKey(a => new { a.OrganizationId, a.UnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_AttributeDefinition_Unit");

        var attrVal = model.Entity<ItemAttributeValue>();
        attrVal.ToTable("ItemAttributeValue", Schema, table =>
        {
            table.HasCheckConstraint("CK_ItemAttributeValue_Metadata", MetadataCheck);
        });
        attrVal.HasKey(v => v.Id).HasName("PK_ItemAttributeValue");
        attrVal.Property(v => v.Id).UseIdentityColumn();
        attrVal.Property(v => v.OrganizationId).IsRequired();
        attrVal.Property(v => v.ItemId).IsRequired();
        attrVal.Property(v => v.AttributeDefinitionId).IsRequired();
        attrVal.Property(v => v.TextValue).HasMaxLength(400);
        attrVal.Property(v => v.DecimalValue).HasPrecision(18, 6);
        attrVal.Property(v => v.BooleanValue);
        AddAuditMetadata(attrVal);
        attrVal.HasIndex(v => new { v.OrganizationId, v.ItemId, v.AttributeDefinitionId }).IsUnique().HasDatabaseName("UX_ItemAttributeValue_Org_Item_Def");
        attrVal.HasOne<Item>().WithMany().HasForeignKey(v => new { v.OrganizationId, v.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ItemAttributeValue_Item");
        attrVal.HasOne<AttributeDefinition>().WithMany().HasForeignKey(v => new { v.OrganizationId, v.AttributeDefinitionId })
            .HasPrincipalKey(d => new { d.OrganizationId, d.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ItemAttributeValue_Definition");

        var dim = model.Entity<ContextDimension>();
        dim.ToTable("ContextDimension", Schema, table =>
        {
            table.HasCheckConstraint("CK_ContextDimension_Code", CodeCheck);
            table.HasCheckConstraint("CK_ContextDimension_Name", NameCheck);
            table.HasCheckConstraint("CK_ContextDimension_Metadata", MetadataCheck);
        });
        dim.HasKey(d => d.Id).HasName("PK_ContextDimension");
        dim.Property(d => d.Id).UseIdentityColumn();
        dim.Property(d => d.OrganizationId).IsRequired();
        dim.Property(d => d.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        dim.Property(d => d.Name).HasMaxLength(200).IsRequired();
        dim.Property(d => d.IsActive).IsRequired();
        AddAuditMetadata(dim);
        dim.HasAlternateKey(d => new { d.OrganizationId, d.Id }).HasName("AK_ContextDimension_Org_Id");
        dim.HasIndex(d => new { d.OrganizationId, d.Code }).IsUnique().HasDatabaseName("UX_ContextDimension_Org_Code");
        dim.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ContextDimension_Organization");

        var bom = model.Entity<Bom>();
        bom.ToTable("Bom", Schema, table =>
        {
            table.HasCheckConstraint("CK_Bom_Code", CodeCheck);
            table.HasCheckConstraint("CK_Bom_Name", NameCheck);
            table.HasCheckConstraint("CK_Bom_Metadata", MetadataCheck);
        });
        bom.HasKey(b => b.Id).HasName("PK_Bom");
        bom.Property(b => b.Id).UseIdentityColumn();
        bom.Property(b => b.OrganizationId).IsRequired();
        bom.Property(b => b.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        bom.Property(b => b.Name).HasMaxLength(200).IsRequired();
        bom.Property(b => b.PrimaryOutputItemId).IsRequired();
        bom.Property(b => b.IsActive).IsRequired();
        AddAuditMetadata(bom);
        bom.HasAlternateKey(b => new { b.OrganizationId, b.Id }).HasName("AK_Bom_Org_Id");
        bom.HasIndex(b => new { b.OrganizationId, b.Code }).IsUnique().HasDatabaseName("UX_Bom_Org_Code");
        bom.HasIndex(b => new { b.OrganizationId, b.PrimaryOutputItemId, b.IsActive }).HasDatabaseName("IX_Bom_Org_PrimaryItem");
        bom.HasOne<Organization>().WithMany().HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Bom_Organization");
        bom.HasOne<Item>().WithMany().HasForeignKey(b => new { b.OrganizationId, b.PrimaryOutputItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Bom_PrimaryItem");

        var ver = model.Entity<BomVersion>();
        ver.ToTable("BomVersion", Schema, table =>
        {
            table.HasCheckConstraint("CK_BomVersion_Number", "[VersionNumber] > 0");
            table.HasCheckConstraint("CK_BomVersion_Status", "[Status] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_BomVersion_PositiveBaseQty", "[BaseOutputQuantity] > 0");
            table.HasCheckConstraint("CK_BomVersion_EffectiveDates", "[EffectiveToDate] IS NULL OR [EffectiveToDate] >= [EffectiveFromDate]");
            table.HasCheckConstraint("CK_BomVersion_Metadata", MetadataCheck);
        });
        ver.HasKey(v => v.Id).HasName("PK_BomVersion");
        ver.Property(v => v.Id).UseIdentityColumn();
        ver.Property(v => v.OrganizationId).IsRequired();
        ver.Property(v => v.BomId).IsRequired();
        ver.Property(v => v.VersionNumber).IsRequired();
        ver.Property(v => v.Status).HasConversion<byte>().IsRequired();
        ver.Property(v => v.BaseOutputQuantity).HasPrecision(18, 6).IsRequired();
        ver.Property(v => v.BaseOutputUnitId).IsRequired();
        ver.Property(v => v.EffectiveFromDate).HasColumnType("date").IsRequired();
        ver.Property(v => v.EffectiveToDate).HasColumnType("date");
        ver.Property(v => v.Notes).HasMaxLength(500);
        AddAuditMetadata(ver);
        ver.HasAlternateKey(v => new { v.OrganizationId, v.Id }).HasName("AK_BomVersion_Org_Id");
        ver.HasIndex(v => new { v.OrganizationId, v.BomId, v.VersionNumber }).IsUnique().HasDatabaseName("UX_BomVersion_Org_Bom_Ver");
        ver.HasOne<Bom>().WithMany().HasForeignKey(v => new { v.OrganizationId, v.BomId })
            .HasPrincipalKey(b => new { b.OrganizationId, b.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomVersion_Bom");
        ver.HasOne<Unit>().WithMany().HasForeignKey(v => new { v.OrganizationId, v.BaseOutputUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomVersion_BaseUnit");

        var bomInput = model.Entity<BomInput>();
        bomInput.ToTable("BomInput", Schema, table =>
        {
            table.HasCheckConstraint("CK_BomInput_Sequence", "[Sequence] > 0");
            table.HasCheckConstraint("CK_BomInput_PositiveQty", "[Quantity] > 0");
            table.HasCheckConstraint("CK_BomInput_ScrapBounds", "[ScrapPercentage] >= 0 AND [ScrapPercentage] < 100");
            table.HasCheckConstraint("CK_BomInput_Metadata", MetadataCheck);
        });
        bomInput.HasKey(i => i.Id).HasName("PK_BomInput");
        bomInput.Property(i => i.Id).UseIdentityColumn();
        bomInput.Property(i => i.OrganizationId).IsRequired();
        bomInput.Property(i => i.BomVersionId).IsRequired();
        bomInput.Property(i => i.Sequence).IsRequired();
        bomInput.Property(i => i.ItemId).IsRequired();
        bomInput.Property(i => i.Quantity).HasPrecision(18, 6).IsRequired();
        bomInput.Property(i => i.UnitId).IsRequired();
        bomInput.Property(i => i.ScrapPercentage).HasPrecision(5, 2).IsRequired();
        bomInput.Property(i => i.IsOptional).IsRequired();
        bomInput.Property(i => i.Notes).HasMaxLength(500);
        AddAuditMetadata(bomInput);
        bomInput.HasIndex(i => new { i.OrganizationId, i.BomVersionId, i.Sequence }).HasDatabaseName("IX_BomInput_Org_Ver_Seq");
        bomInput.HasOne<BomVersion>().WithMany().HasForeignKey(i => new { i.OrganizationId, i.BomVersionId })
            .HasPrincipalKey(v => new { v.OrganizationId, v.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomInput_Version");
        bomInput.HasOne<Item>().WithMany().HasForeignKey(i => new { i.OrganizationId, i.ItemId })
            .HasPrincipalKey(it => new { it.OrganizationId, it.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomInput_Item");
        bomInput.HasOne<Unit>().WithMany().HasForeignKey(i => new { i.OrganizationId, i.UnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomInput_Unit");

        var bomOutput = model.Entity<BomOutput>();
        bomOutput.ToTable("BomOutput", Schema, table =>
        {
            table.HasCheckConstraint("CK_BomOutput_Sequence", "[Sequence] > 0");
            table.HasCheckConstraint("CK_BomOutput_Type", "[OutputType] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_BomOutput_PositiveQty", "[Quantity] > 0");
            table.HasCheckConstraint("CK_BomOutput_Metadata", MetadataCheck);
        });
        bomOutput.HasKey(o => o.Id).HasName("PK_BomOutput");
        bomOutput.Property(o => o.Id).UseIdentityColumn();
        bomOutput.Property(o => o.OrganizationId).IsRequired();
        bomOutput.Property(o => o.BomVersionId).IsRequired();
        bomOutput.Property(o => o.Sequence).IsRequired();
        bomOutput.Property(o => o.ItemId).IsRequired();
        bomOutput.Property(o => o.OutputType).HasConversion<byte>().IsRequired();
        bomOutput.Property(o => o.Quantity).HasPrecision(18, 6).IsRequired();
        bomOutput.Property(o => o.UnitId).IsRequired();
        AddAuditMetadata(bomOutput);
        bomOutput.HasIndex(o => new { o.OrganizationId, o.BomVersionId, o.Sequence }).HasDatabaseName("IX_BomOutput_Org_Ver_Seq");
        bomOutput.HasOne<BomVersion>().WithMany().HasForeignKey(o => new { o.OrganizationId, o.BomVersionId })
            .HasPrincipalKey(v => new { v.OrganizationId, v.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomOutput_Version");
        bomOutput.HasOne<Item>().WithMany().HasForeignKey(o => new { o.OrganizationId, o.ItemId })
            .HasPrincipalKey(it => new { it.OrganizationId, it.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomOutput_Item");
        bomOutput.HasOne<Unit>().WithMany().HasForeignKey(o => new { o.OrganizationId, o.UnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomOutput_Unit");

        var app = model.Entity<BomApplicability>();
        app.ToTable("BomApplicability", Schema, table =>
        {
            table.HasCheckConstraint("CK_BomApplicability_Name", NameCheck);
            table.HasCheckConstraint("CK_BomApplicability_Metadata", MetadataCheck);
        });
        app.HasKey(a => a.Id).HasName("PK_BomApplicability");
        app.Property(a => a.Id).UseIdentityColumn();
        app.Property(a => a.OrganizationId).IsRequired();
        app.Property(a => a.BomVersionId).IsRequired();
        app.Property(a => a.Name).HasMaxLength(200).IsRequired();
        app.Property(a => a.IsActive).IsRequired();
        AddAuditMetadata(app);
        app.HasAlternateKey(a => new { a.OrganizationId, a.Id }).HasName("AK_BomApplicability_Org_Id");
        app.HasIndex(a => new { a.OrganizationId, a.BomVersionId }).IsUnique().HasDatabaseName("UX_BomApplicability_Org_Version");
        app.HasOne<BomVersion>().WithMany().HasForeignKey(a => new { a.OrganizationId, a.BomVersionId })
            .HasPrincipalKey(v => new { v.OrganizationId, v.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomApplicability_Version");

        var cond = model.Entity<BomApplicabilityCondition>();
        cond.ToTable("BomApplicabilityCondition", Schema, table =>
        {
            table.HasCheckConstraint("CK_BomApplicabilityCondition_Operator", "[Operator] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_BomApplicabilityCondition_Value", "DATALENGTH([Value]) > 0");
            table.HasCheckConstraint("CK_BomApplicabilityCondition_Metadata", MetadataCheck);
        });
        cond.HasKey(c => c.Id).HasName("PK_BomApplicabilityCondition");
        cond.Property(c => c.Id).UseIdentityColumn();
        cond.Property(c => c.OrganizationId).IsRequired();
        cond.Property(c => c.BomApplicabilityId).IsRequired();
        cond.Property(c => c.ContextDimensionId).IsRequired();
        cond.Property(c => c.Operator).HasConversion<byte>().IsRequired();
        cond.Property(c => c.Value).HasMaxLength(400).IsRequired();
        AddAuditMetadata(cond);
        cond.HasIndex(c => new { c.BomApplicabilityId, c.ContextDimensionId, c.Operator }).IsUnique().HasDatabaseName("UX_BomApplicabilityCondition_Dim_Op");
        cond.HasOne<BomApplicability>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.BomApplicabilityId })
            .HasPrincipalKey(a => new { a.OrganizationId, a.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomApplicabilityCondition_Applicability");
        cond.HasOne<ContextDimension>().WithMany().HasForeignKey(c => new { c.OrganizationId, c.ContextDimensionId })
            .HasPrincipalKey(d => new { d.OrganizationId, d.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BomApplicabilityCondition_Dimension");

        var po = model.Entity<ProductionOrder>();
        po.ToTable("ProductionOrder", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductionOrder_Number", "DATALENGTH([OrderNumber]) BETWEEN 1 AND 32 AND DATALENGTH([OrderNumber]) = LEN([OrderNumber]) AND [OrderNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OrderNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_ProductionOrder_BomCodeSnapshot", "DATALENGTH([BomCodeSnapshot]) BETWEEN 1 AND 32 AND DATALENGTH([BomCodeSnapshot]) = LEN([BomCodeSnapshot]) AND [BomCodeSnapshot] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([BomCodeSnapshot], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_ProductionOrder_Status", "[Status] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_ProductionOrder_RequestedQty", "[RequestedQuantity] > 0");
            table.HasCheckConstraint("CK_ProductionOrder_Dates", "[PlannedDueDate] >= [PlannedStartDate]");
            table.HasCheckConstraint("CK_ProductionOrder_BaseOutputQty", "[BaseOutputQuantitySnapshot] > 0 AND [BomVersionNumberSnapshot] > 0");
            table.HasCheckConstraint("CK_ProductionOrder_Actuals", "[ActualProducedQuantity] >= 0 AND [ActualScrapQuantity] >= 0");
            table.HasCheckConstraint("CK_ProductionOrder_Metadata", MetadataCheck);
        });
        po.HasKey(x => x.Id).HasName("PK_ProductionOrder");
        po.Property(x => x.Id).UseIdentityColumn();
        po.Property(x => x.OrganizationId).IsRequired();
        po.Property(x => x.SiteId).IsRequired();
        po.Property(x => x.OrderNumber).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        po.Property(x => x.Status).HasConversion<byte>().IsRequired();
        po.Property(x => x.ItemId).IsRequired();
        po.Property(x => x.RequestedQuantity).HasPrecision(18, 6).IsRequired();
        po.Property(x => x.RequestedUnitId).IsRequired();
        po.Property(x => x.PlannedStartDate).HasColumnType("date").IsRequired();
        po.Property(x => x.PlannedDueDate).HasColumnType("date").IsRequired();
        po.Property(x => x.BomId).IsRequired();
        po.Property(x => x.BomCodeSnapshot).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        po.Property(x => x.BomVersionId).IsRequired();
        po.Property(x => x.BomVersionNumberSnapshot).IsRequired();
        po.Property(x => x.BaseOutputQuantitySnapshot).HasPrecision(18, 6).IsRequired();
        po.Property(x => x.BaseOutputUnitIdSnapshot).IsRequired();
        po.Property(x => x.ActualProducedQuantity).HasPrecision(18, 6).IsRequired();
        po.Property(x => x.ActualScrapQuantity).HasPrecision(18, 6).IsRequired();
        po.Property(x => x.Notes).HasMaxLength(500);
        po.Property(x => x.CancellationReason).HasMaxLength(500);
        po.Property(x => x.ReleasedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        po.Property(x => x.StartedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        po.Property(x => x.CompletedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        po.Property(x => x.CancelledAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        AddAuditMetadata(po);
        po.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_ProductionOrder_Org_Id");
        po.HasIndex(x => new { x.OrganizationId, x.OrderNumber }).IsUnique().HasDatabaseName("UX_ProductionOrder_Org_Number");
        po.HasIndex(x => new { x.OrganizationId, x.SiteId, x.Status, x.PlannedStartDate }).HasDatabaseName("IX_ProductionOrder_Org_Site_Status_Date");
        po.HasIndex(x => new { x.OrganizationId, x.BomVersionId }).HasDatabaseName("IX_ProductionOrder_Org_BomVersion");
        po.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ProductionOrder_Organization");
        po.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrder_Site");
        po.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrder_Item");
        po.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.RequestedUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrder_RequestedUnit");
        po.HasOne<Bom>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BomId })
            .HasPrincipalKey(b => new { b.OrganizationId, b.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrder_Bom");
        po.HasOne<BomVersion>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BomVersionId })
            .HasPrincipalKey(v => new { v.OrganizationId, v.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrder_BomVersion");
        po.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BaseOutputUnitIdSnapshot })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrder_BaseOutputUnitSnapshot");

        var poInput = model.Entity<ProductionOrderInput>();
        poInput.ToTable("ProductionOrderInput", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductionOrderInput_LevelSeq", "[Level] >= 1 AND [Sequence] >= 1");
            table.HasCheckConstraint("CK_ProductionOrderInput_Quantities", "[NetQuantity] > 0 AND [GrossQuantity] > 0");
            table.HasCheckConstraint("CK_ProductionOrderInput_Scrap", "[ScrapPercentage] >= 0 AND [ScrapPercentage] < 100");
            table.HasCheckConstraint("CK_ProductionOrderInput_Metadata", MetadataCheck);
        });
        poInput.HasKey(x => x.Id).HasName("PK_ProductionOrderInput");
        poInput.Property(x => x.Id).UseIdentityColumn();
        poInput.Property(x => x.OrganizationId).IsRequired();
        poInput.Property(x => x.ProductionOrderId).IsRequired();
        poInput.Property(x => x.Level).IsRequired();
        poInput.Property(x => x.Sequence).IsRequired();
        poInput.Property(x => x.ParentItemId).IsRequired();
        poInput.Property(x => x.ItemId).IsRequired();
        poInput.Property(x => x.NetQuantity).HasPrecision(18, 6).IsRequired();
        poInput.Property(x => x.ScrapPercentage).HasPrecision(5, 2).IsRequired();
        poInput.Property(x => x.GrossQuantity).HasPrecision(18, 6).IsRequired();
        poInput.Property(x => x.UnitId).IsRequired();
        poInput.Property(x => x.IsOptional).IsRequired();
        poInput.Property(x => x.IsSubAssembly).IsRequired();
        AddAuditMetadata(poInput);
        poInput.HasIndex(x => new { x.OrganizationId, x.ProductionOrderId, x.Level, x.Sequence }).HasDatabaseName("IX_ProductionOrderInput_Org_Order_LvlSeq");
        poInput.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ProductionOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderInput_Order");
        poInput.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ParentItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderInput_ParentItem");
        poInput.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderInput_Item");
        poInput.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.UnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderInput_Unit");
        poInput.HasOne<BomVersion>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ResolvedSubBomVersionId })
            .HasPrincipalKey(v => new { v.OrganizationId, v.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderInput_SubBomVersion");

        var poOutput = model.Entity<ProductionOrderOutput>();
        poOutput.ToTable("ProductionOrderOutput", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductionOrderOutput_LevelSeq", "[Level] >= 1 AND [Sequence] >= 1");
            table.HasCheckConstraint("CK_ProductionOrderOutput_Type", "[OutputType] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_ProductionOrderOutput_Qty", "[PlannedQuantity] > 0");
            table.HasCheckConstraint("CK_ProductionOrderOutput_Metadata", MetadataCheck);
        });
        poOutput.HasKey(x => x.Id).HasName("PK_ProductionOrderOutput");
        poOutput.Property(x => x.Id).UseIdentityColumn();
        poOutput.Property(x => x.OrganizationId).IsRequired();
        poOutput.Property(x => x.ProductionOrderId).IsRequired();
        poOutput.Property(x => x.Level).IsRequired();
        poOutput.Property(x => x.Sequence).IsRequired();
        poOutput.Property(x => x.ItemId).IsRequired();
        poOutput.Property(x => x.OutputType).HasConversion<byte>().IsRequired();
        poOutput.Property(x => x.PlannedQuantity).HasPrecision(18, 6).IsRequired();
        poOutput.Property(x => x.UnitId).IsRequired();
        AddAuditMetadata(poOutput);
        poOutput.HasIndex(x => new { x.OrganizationId, x.ProductionOrderId, x.Level, x.Sequence }).HasDatabaseName("IX_ProductionOrderOutput_Org_Order_LvlSeq");
        poOutput.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ProductionOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderOutput_Order");
        poOutput.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderOutput_Item");
        poOutput.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.UnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderOutput_Unit");

        var poCtx = model.Entity<ProductionOrderContext>();
        poCtx.ToTable("ProductionOrderContext", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductionOrderContext_DimCode", "DATALENGTH([DimensionCode]) BETWEEN 1 AND 32 AND DATALENGTH([DimensionCode]) = LEN([DimensionCode]) AND [DimensionCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([DimensionCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_ProductionOrderContext_Value", "DATALENGTH([DimensionValue]) > 0");
            table.HasCheckConstraint("CK_ProductionOrderContext_Metadata", MetadataCheck);
        });
        poCtx.HasKey(x => x.Id).HasName("PK_ProductionOrderContext");
        poCtx.Property(x => x.Id).UseIdentityColumn();
        poCtx.Property(x => x.OrganizationId).IsRequired();
        poCtx.Property(x => x.ProductionOrderId).IsRequired();
        poCtx.Property(x => x.DimensionCode).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        poCtx.Property(x => x.DimensionValue).HasMaxLength(400).IsRequired();
        AddAuditMetadata(poCtx);
        poCtx.HasIndex(x => new { x.ProductionOrderId, x.DimensionCode }).IsUnique().HasDatabaseName("UX_ProductionOrderContext_Order_Dim");
        poCtx.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ProductionOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderContext_Order");

        const string docNumberCheck = "DATALENGTH([DocumentNumber]) BETWEEN 1 AND 32 AND DATALENGTH([DocumentNumber]) = LEN([DocumentNumber]) AND [DocumentNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([DocumentNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";
        const string lotCodeCheck = "([LotCode] = '' OR (DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'))";

        var wh = model.Entity<Warehouse>();
        Business(wh, "Warehouse");
        wh.Property(x => x.Id).UseIdentityColumn();
        wh.HasKey(x => x.Id).HasName("PK_Warehouse");
        wh.Property(x => x.OrganizationId).IsRequired();
        wh.Property(x => x.SiteId).IsRequired();
        wh.Property(x => x.IsDefaultForSite).IsRequired();
        wh.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_Warehouse_Org_Id");
        wh.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique().HasDatabaseName("UX_Warehouse_Org_Code");
        wh.HasIndex(x => new { x.OrganizationId, x.SiteId }).IsUnique()
            .HasFilter("[IsDefaultForSite] = 1 AND [IsActive] = 1")
            .HasDatabaseName("UX_Warehouse_Org_Site_Default");
        wh.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Warehouse_Organization");
        wh.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Warehouse_Site");

        var txn = model.Entity<InventoryTransaction>();
        txn.ToTable("InventoryTransaction", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryTransaction_DocNum", docNumberCheck);
            table.HasCheckConstraint("CK_InventoryTransaction_Type", "[TransactionType] BETWEEN 1 AND 9");
            table.HasCheckConstraint("CK_InventoryTransaction_Metadata", MetadataCheck);
        });
        txn.HasKey(x => x.Id).HasName("PK_InventoryTransaction");
        txn.Property(x => x.Id).UseIdentityColumn();
        txn.Property(x => x.OrganizationId).IsRequired();
        txn.Property(x => x.SiteId).IsRequired();
        txn.Property(x => x.WarehouseId).IsRequired();
        txn.Property(x => x.DocumentNumber).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        txn.Property(x => x.TransactionType).HasConversion<byte>().IsRequired();
        txn.Property(x => x.QualityInspectionId);
        txn.Property(x => x.PurchaseOrderId);
        txn.Property(x => x.ReferenceNote).HasMaxLength(500);
        txn.Property(x => x.IdempotencyKey).HasMaxLength(64).IsUnicode(false).UseCollation(CodeCollation);
        txn.Property(x => x.PostedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        AddAuditMetadata(txn);
        txn.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_InventoryTransaction_Org_Id");
        txn.HasIndex(x => new { x.OrganizationId, x.DocumentNumber }).IsUnique().HasDatabaseName("UX_InventoryTransaction_Org_DocNum");
        txn.HasIndex(x => new { x.OrganizationId, x.IdempotencyKey }).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL").HasDatabaseName("UX_InventoryTransaction_Org_Idempotency");
        txn.HasIndex(x => new { x.OrganizationId, x.ReversedTransactionId }).IsUnique().HasFilter("[ReversedTransactionId] IS NOT NULL").HasDatabaseName("UX_InventoryTransaction_Org_Reversal");
        txn.HasIndex(x => new { x.OrganizationId, x.SiteId, x.PostedAtUtc }).HasDatabaseName("IX_InventoryTransaction_Org_Site_Date");
        txn.HasIndex(x => new { x.OrganizationId, x.PurchaseOrderId }).HasDatabaseName("IX_InventoryTransaction_Org_PurchaseOrder");
        txn.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_InventoryTransaction_Organization");
        txn.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryTransaction_Site");
        txn.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryTransaction_Warehouse");
        txn.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DestinationWarehouseId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryTransaction_DestWarehouse");
        txn.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ProductionOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryTransaction_Order");
        txn.HasOne<InventoryTransaction>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ReversedTransactionId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryTransaction_Reversal");
        txn.HasOne<QualityInspection>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.QualityInspectionId })
            .HasPrincipalKey(q => new { q.OrganizationId, q.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryTransaction_QualityInspection");
        txn.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PurchaseOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryTransaction_PurchaseOrder");

        var ledger = model.Entity<InventoryLedgerEntry>();
        ledger.ToTable("InventoryLedgerEntry", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryLedgerEntry_Qty", "[EnteredQuantity] <> 0 AND [BaseQuantityDelta] <> 0");
            table.HasCheckConstraint("CK_InventoryLedgerEntry_LotCode", lotCodeCheck);
            table.HasCheckConstraint("CK_InventoryLedgerEntry_Metadata", MetadataCheck);
        });
        ledger.HasKey(x => x.Id).HasName("PK_InventoryLedgerEntry");
        ledger.Property(x => x.Id).UseIdentityColumn();
        ledger.Property(x => x.OrganizationId).IsRequired();
        ledger.Property(x => x.InventoryTransactionId).IsRequired();
        ledger.Property(x => x.SiteId).IsRequired();
        ledger.Property(x => x.WarehouseId).IsRequired();
        ledger.Property(x => x.LineNumber).IsRequired();
        ledger.Property(x => x.ItemId).IsRequired();
        ledger.Property(x => x.LotCode).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).HasDefaultValue(string.Empty).IsRequired();
        ledger.Property(x => x.EnteredQuantity).HasPrecision(18, 6).IsRequired();
        ledger.Property(x => x.EnteredUnitId).IsRequired();
        ledger.Property(x => x.BaseQuantityDelta).HasPrecision(18, 6).IsRequired();
        ledger.Property(x => x.BaseUnitId).IsRequired();
        ledger.Property(x => x.OccurredAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        ledger.Property(x => x.Notes).HasMaxLength(300);
        AddAuditMetadata(ledger);
        ledger.HasIndex(x => new { x.OrganizationId, x.InventoryTransactionId, x.LineNumber }).IsUnique().HasDatabaseName("UX_InventoryLedgerEntry_Org_Txn_Line");
        ledger.HasIndex(x => new { x.OrganizationId, x.WarehouseId, x.ItemId, x.LotCode, x.OccurredAtUtc }).HasDatabaseName("IX_InventoryLedgerEntry_Org_Wh_Item_Lot");
        ledger.HasOne<InventoryTransaction>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.InventoryTransactionId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryLedgerEntry_Txn");
        ledger.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryLedgerEntry_Site");
        ledger.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryLedgerEntry_Warehouse");
        ledger.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryLedgerEntry_Item");
        ledger.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.EnteredUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryLedgerEntry_EnteredUnit");
        ledger.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BaseUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryLedgerEntry_BaseUnit");

        var bal = model.Entity<InventoryBalance>();
        bal.ToTable("InventoryBalance", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryBalance_NonNegative", "[OnHandBaseQuantity] >= 0");
            table.HasCheckConstraint("CK_InventoryBalance_Blocked", "[BlockedBaseQuantity] >= 0 AND [BlockedBaseQuantity] <= [OnHandBaseQuantity]");
            table.HasCheckConstraint("CK_InventoryBalance_LotCode", lotCodeCheck);
            table.HasCheckConstraint("CK_InventoryBalance_Metadata", MetadataCheck);
        });
        bal.HasKey(x => x.Id).HasName("PK_InventoryBalance");
        bal.Property(x => x.Id).UseIdentityColumn();
        bal.Property(x => x.OrganizationId).IsRequired();
        bal.Property(x => x.SiteId).IsRequired();
        bal.Property(x => x.WarehouseId).IsRequired();
        bal.Property(x => x.ItemId).IsRequired();
        bal.Property(x => x.LotCode).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).HasDefaultValue(string.Empty).IsRequired();
        bal.Property(x => x.BaseUnitId).IsRequired();
        bal.Property(x => x.OnHandBaseQuantity).HasPrecision(18, 6).IsRequired();
        bal.Property(x => x.BlockedBaseQuantity).HasPrecision(18, 6).HasDefaultValue(0m).IsRequired();
        bal.Ignore(x => x.AvailableBaseQuantity);
        bal.Property(x => x.LastMovedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        AddAuditMetadata(bal);
        bal.HasIndex(x => new { x.OrganizationId, x.WarehouseId, x.ItemId, x.LotCode }).IsUnique().HasDatabaseName("UX_InventoryBalance_Org_Wh_Item_Lot");
        bal.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryBalance_Site");
        bal.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryBalance_Warehouse");
        bal.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryBalance_Item");
        bal.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BaseUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryBalance_BaseUnit");

        const string requiredLotCodeCheck = "DATALENGTH([LotCode]) BETWEEN 1 AND 32 AND DATALENGTH([LotCode]) = LEN([LotCode]) AND [LotCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([LotCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";
        const string inspectionNumCheck = "DATALENGTH([InspectionNumber]) BETWEEN 1 AND 32 AND DATALENGTH([InspectionNumber]) = LEN([InspectionNumber]) AND [InspectionNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([InspectionNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";

        var itemLot = model.Entity<ItemLot>();
        itemLot.ToTable("ItemLot", Schema, table =>
        {
            table.HasCheckConstraint("CK_ItemLot_LotCode", requiredLotCodeCheck);
            table.HasCheckConstraint("CK_ItemLot_Dates", "[ManufactureDate] IS NULL OR [ExpirationDate] IS NULL OR [ExpirationDate] >= [ManufactureDate]");
            table.HasCheckConstraint("CK_ItemLot_Metadata", MetadataCheck);
        });
        itemLot.HasKey(x => x.Id).HasName("PK_ItemLot");
        itemLot.Property(x => x.Id).UseIdentityColumn();
        itemLot.Property(x => x.OrganizationId).IsRequired();
        itemLot.Property(x => x.ItemId).IsRequired();
        itemLot.Property(x => x.LotCode).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        itemLot.Property(x => x.ManufactureDate).HasColumnType("date");
        itemLot.Property(x => x.ExpirationDate).HasColumnType("date");
        itemLot.Property(x => x.Notes).HasMaxLength(500);
        AddAuditMetadata(itemLot);
        itemLot.HasIndex(x => new { x.OrganizationId, x.ItemId, x.LotCode }).IsUnique().HasDatabaseName("UX_ItemLot_Org_Item_Lot");
        itemLot.HasIndex(x => new { x.OrganizationId, x.ExpirationDate }).HasDatabaseName("IX_ItemLot_Org_Expiration");
        itemLot.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ItemLot_Organization");
        itemLot.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ItemLot_Item");

        var qi = model.Entity<QualityInspection>();
        qi.ToTable("QualityInspection", Schema, table =>
        {
            table.HasCheckConstraint("CK_QualityInspection_Number", inspectionNumCheck);
            table.HasCheckConstraint("CK_QualityInspection_LotCode", lotCodeCheck);
            table.HasCheckConstraint("CK_QualityInspection_SourceType", "[SourceType] BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_QualityInspection_Status", "[Status] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_QualityInspection_Quantities", "[InspectedBaseQuantity] > 0 AND [AcceptedBaseQuantity] >= 0 AND [RejectedBaseQuantity] >= 0 AND [RemainingBlockedBaseQuantity] >= 0 AND ([AcceptedBaseQuantity] + [RejectedBaseQuantity] + [RemainingBlockedBaseQuantity]) <= [InspectedBaseQuantity]");
            table.HasCheckConstraint("CK_QualityInspection_Metadata", MetadataCheck);
        });
        qi.HasKey(x => x.Id).HasName("PK_QualityInspection");
        qi.Property(x => x.Id).UseIdentityColumn();
        qi.Property(x => x.OrganizationId).IsRequired();
        qi.Property(x => x.SiteId).IsRequired();
        qi.Property(x => x.WarehouseId).IsRequired();
        qi.Property(x => x.ItemId).IsRequired();
        qi.Property(x => x.LotCode).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).HasDefaultValue(string.Empty).IsRequired();
        qi.Property(x => x.InspectionNumber).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        qi.Property(x => x.SourceType).HasConversion<byte>().IsRequired();
        qi.Property(x => x.SourceTransactionId);
        qi.Property(x => x.ProductionOrderId);
        qi.Property(x => x.BaseUnitId).IsRequired();
        qi.Property(x => x.InspectedBaseQuantity).HasPrecision(18, 6).IsRequired();
        qi.Property(x => x.AcceptedBaseQuantity).HasPrecision(18, 6).IsRequired();
        qi.Property(x => x.RejectedBaseQuantity).HasPrecision(18, 6).IsRequired();
        qi.Property(x => x.RemainingBlockedBaseQuantity).HasPrecision(18, 6).IsRequired();
        qi.Property(x => x.Status).HasConversion<byte>().IsRequired();
        qi.Property(x => x.DefectReasonCode).HasMaxLength(64).IsUnicode(false).UseCollation(CodeCollation);
        qi.Property(x => x.Notes).HasMaxLength(1000);
        qi.Property(x => x.InspectedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        qi.Property(x => x.InspectedByActorId);
        AddAuditMetadata(qi);
        qi.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_QualityInspection_Org_Id");
        qi.HasIndex(x => new { x.OrganizationId, x.InspectionNumber }).IsUnique().HasDatabaseName("UX_QualityInspection_Org_Number");
        qi.HasIndex(x => new { x.OrganizationId, x.SiteId, x.Status, x.Id }).HasDatabaseName("IX_QualityInspection_Org_Site_Status");
        qi.HasIndex(x => new { x.OrganizationId, x.ItemId, x.LotCode }).HasDatabaseName("IX_QualityInspection_Org_Item_Lot");
        qi.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_QualityInspection_Organization");
        qi.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualityInspection_Site");
        qi.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualityInspection_Warehouse");
        qi.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualityInspection_Item");
        qi.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BaseUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualityInspection_BaseUnit");
        qi.HasOne<InventoryTransaction>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SourceTransactionId })
            .HasPrincipalKey(t => new { t.OrganizationId, t.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualityInspection_SourceTxn");
        qi.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ProductionOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualityInspection_Order");
        qi.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        var qiLine = model.Entity<QualityInspectionLine>();
        qiLine.ToTable("QualityInspectionLine", Schema, table =>
        {
            table.HasCheckConstraint("CK_QualityInspectionLine_LineNum", "[LineNumber] > 0");
            table.HasCheckConstraint("CK_QualityInspectionLine_Param", "DATALENGTH(LTRIM(RTRIM([ParameterName]))) > 0");
            table.HasCheckConstraint("CK_QualityInspectionLine_Metadata", MetadataCheck);
        });
        qiLine.HasKey(x => x.Id).HasName("PK_QualityInspectionLine");
        qiLine.Property(x => x.Id).UseIdentityColumn();
        qiLine.Property(x => x.OrganizationId).IsRequired();
        qiLine.Property(x => x.QualityInspectionId).IsRequired();
        qiLine.Property(x => x.LineNumber).IsRequired();
        qiLine.Property(x => x.ParameterName).HasMaxLength(100).IsRequired();
        qiLine.Property(x => x.ExpectedSpec).HasMaxLength(100);
        qiLine.Property(x => x.MeasuredValue).HasMaxLength(100);
        qiLine.Property(x => x.IsPassed).IsRequired();
        qiLine.Property(x => x.Notes).HasMaxLength(300);
        AddAuditMetadata(qiLine);
        qiLine.HasIndex(x => new { x.OrganizationId, x.QualityInspectionId, x.LineNumber }).IsUnique().HasDatabaseName("UX_QualityInspectionLine_Org_Insp_Line");
        qiLine.HasOne<QualityInspection>().WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.OrganizationId, x.QualityInspectionId })
            .HasPrincipalKey(q => new { q.OrganizationId, q.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualityInspectionLine_Inspection");

        var supplier = model.Entity<Supplier>();
        supplier.ToTable("Supplier", Schema, table =>
        {
            table.HasCheckConstraint("CK_Supplier_Code", CodeCheck);
            table.HasCheckConstraint("CK_Supplier_Name", NameCheck);
            table.HasCheckConstraint("CK_Supplier_Metadata", MetadataCheck);
        });
        supplier.HasKey(x => x.Id).HasName("PK_Supplier");
        supplier.Property(x => x.Id).UseIdentityColumn();
        supplier.Property(x => x.OrganizationId).IsRequired();
        supplier.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        supplier.Property(x => x.Name).HasMaxLength(200).IsRequired();
        supplier.Property(x => x.ContactName).HasMaxLength(150);
        supplier.Property(x => x.Phone).HasMaxLength(64).IsUnicode(false);
        supplier.Property(x => x.Email).HasMaxLength(256);
        supplier.Property(x => x.Address).HasMaxLength(500);
        supplier.Property(x => x.Notes).HasMaxLength(500);
        supplier.Property(x => x.IsActive).IsRequired();
        AddAuditMetadata(supplier);
        supplier.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_Supplier_Org_Id");
        supplier.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique().HasDatabaseName("UX_Supplier_Org_Code");
        supplier.HasIndex(x => new { x.OrganizationId, x.IsActive, x.Code, x.Id }).HasDatabaseName("IX_Supplier_Org_IsActive_Code");
        supplier.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Supplier_Organization");

        var purchaseOrder = model.Entity<PurchaseOrder>();
        purchaseOrder.ToTable("PurchaseOrder", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrder_Number", "DATALENGTH([OrderNumber]) BETWEEN 1 AND 32 AND DATALENGTH([OrderNumber]) = LEN([OrderNumber]) AND [OrderNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OrderNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_PurchaseOrder_Status", "[Status] BETWEEN 1 AND 6");
            table.HasCheckConstraint("CK_PurchaseOrder_Dates", "[ExpectedDeliveryDate] IS NULL OR [ExpectedDeliveryDate] >= [OrderDate]");
            table.HasCheckConstraint("CK_PurchaseOrder_Currency", "DATALENGTH([CurrencyCode]) = 3 AND DATALENGTH([CurrencyCode]) = LEN([CurrencyCode]) AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
            table.HasCheckConstraint("CK_PurchaseOrder_Metadata", MetadataCheck);
        });
        purchaseOrder.HasKey(x => x.Id).HasName("PK_PurchaseOrder");
        purchaseOrder.Property(x => x.Id).UseIdentityColumn();
        purchaseOrder.Property(x => x.OrganizationId).IsRequired();
        purchaseOrder.Property(x => x.SiteId).IsRequired();
        purchaseOrder.Property(x => x.SupplierId).IsRequired();
        purchaseOrder.Property(x => x.OrderNumber).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        purchaseOrder.Property(x => x.Status).HasConversion<byte>().IsRequired();
        purchaseOrder.Property(x => x.OrderDate).HasColumnType("date").IsRequired();
        purchaseOrder.Property(x => x.ExpectedDeliveryDate).HasColumnType("date");
        purchaseOrder.Property(x => x.TargetWarehouseId);
        purchaseOrder.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        purchaseOrder.Property(x => x.Notes).HasMaxLength(500);
        purchaseOrder.Property(x => x.CancellationReason).HasMaxLength(500);
        purchaseOrder.Property(x => x.ApprovedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        purchaseOrder.Property(x => x.ClosedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        purchaseOrder.Property(x => x.CancelledAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        AddAuditMetadata(purchaseOrder);
        purchaseOrder.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_PurchaseOrder_Org_Id");
        purchaseOrder.HasIndex(x => new { x.OrganizationId, x.OrderNumber }).IsUnique().HasDatabaseName("UX_PurchaseOrder_Org_Number");
        purchaseOrder.HasIndex(x => new { x.OrganizationId, x.SiteId, x.Status, x.OrderDate }).HasDatabaseName("IX_PurchaseOrder_Org_Site_Status");
        purchaseOrder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_PurchaseOrder_Organization");
        purchaseOrder.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PurchaseOrder_Site");
        purchaseOrder.HasOne<Supplier>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SupplierId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PurchaseOrder_Supplier");
        purchaseOrder.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.TargetWarehouseId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PurchaseOrder_TargetWarehouse");
        purchaseOrder.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CurrencyCode })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Code }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PurchaseOrder_Currency");
        purchaseOrder.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        var poLine = model.Entity<PurchaseOrderLine>();
        poLine.ToTable("PurchaseOrderLine", Schema, table =>
        {
            table.HasCheckConstraint("CK_PurchaseOrderLine_Num", "[LineNumber] > 0");
            table.HasCheckConstraint("CK_PurchaseOrderLine_Qty", "[OrderedQuantity] > 0 AND [OrderedBaseQuantity] > 0 AND [ReceivedBaseQuantity] >= 0 AND [ReceivedBaseQuantity] <= [OrderedBaseQuantity]");
            table.HasCheckConstraint("CK_PurchaseOrderLine_Price", "[UnitPrice] IS NULL OR [UnitPrice] >= 0");
            table.HasCheckConstraint("CK_PurchaseOrderLine_Metadata", MetadataCheck);
        });
        poLine.HasKey(x => x.Id).HasName("PK_PurchaseOrderLine");
        poLine.Property(x => x.Id).UseIdentityColumn();
        poLine.Property(x => x.OrganizationId).IsRequired();
        poLine.Property(x => x.PurchaseOrderId).IsRequired();
        poLine.Property(x => x.LineNumber).IsRequired();
        poLine.Property(x => x.ItemId).IsRequired();
        poLine.Property(x => x.OrderedQuantity).HasPrecision(18, 6).IsRequired();
        poLine.Property(x => x.OrderedUnitId).IsRequired();
        poLine.Property(x => x.BaseUnitIdSnapshot).IsRequired();
        poLine.Property(x => x.OrderedBaseQuantity).HasPrecision(18, 6).IsRequired();
        poLine.Property(x => x.ReceivedBaseQuantity).HasPrecision(18, 6).IsRequired();
        poLine.Property(x => x.UnitPrice).HasPrecision(18, 4);
        poLine.Property(x => x.Notes).HasMaxLength(300);
        AddAuditMetadata(poLine);
        poLine.HasIndex(x => new { x.OrganizationId, x.PurchaseOrderId, x.LineNumber }).IsUnique().HasDatabaseName("UX_PurchaseOrderLine_Org_Order_Line");
        poLine.HasIndex(x => new { x.OrganizationId, x.ItemId }).HasDatabaseName("IX_PurchaseOrderLine_Org_Item");
        poLine.HasOne<PurchaseOrder>().WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.OrganizationId, x.PurchaseOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PurchaseOrderLine_Order");
        poLine.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PurchaseOrderLine_Item");
        poLine.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.OrderedUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PurchaseOrderLine_OrderedUnit");
        poLine.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BaseUnitIdSnapshot })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_PurchaseOrderLine_BaseUnitSnapshot");
    }

    private static void Business<T>(EntityTypeBuilder<T> entity, string tableName) where T : class
    {
        entity.ToTable(tableName, Schema, table =>
        {
            table.HasCheckConstraint($"CK_{tableName}_Code", CodeCheck);
            table.HasCheckConstraint($"CK_{tableName}_Name", NameCheck);
            table.HasCheckConstraint($"CK_{tableName}_Metadata", MetadataCheck);
        });
        entity.Property<string>("Code").HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        entity.Property<string>("Name").HasMaxLength(200).IsRequired();
        entity.Property<bool>("IsActive").IsRequired();
        AddAuditMetadata(entity);
    }

    private static void AddAuditMetadata<T>(EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        entity.Property<long>("CreatedByActorId").IsRequired();
        entity.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        entity.Property<long?>("UpdatedByActorId");
        entity.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
    }
}
