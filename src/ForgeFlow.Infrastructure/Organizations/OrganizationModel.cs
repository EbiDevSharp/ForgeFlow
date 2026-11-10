using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
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
        + "'Item.Created','Item.Updated','Item.Activated','Item.Deactivated',"
        + "'ClassificationSet.Created','Classification.Created','Classification.Updated','Classification.Activated','Classification.Deactivated',"
        + "'ItemClassification.Updated','ItemUnitConversion.Created','ItemUnitConversion.Updated','ItemUnitConversion.Activated','ItemUnitConversion.Deactivated',"
        + "'AttributeDefinition.Created','AttributeDefinition.Updated','AttributeDefinition.Activated','AttributeDefinition.Deactivated',"
        + "'ItemAttributeValue.Updated',"
        + "'ContextDimension.Created','ContextDimension.Updated','ContextDimension.Activated','ContextDimension.Deactivated',"
        + "'Bom.Created','Bom.Renamed','Bom.Activated','Bom.Deactivated',"
        + "'BomVersion.Created','BomVersion.Updated','BomVersion.StatusChanged','BomVersion.LinesSaved','BomVersion.ApplicabilitySaved')) "
        + "OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated','ProductionOrder.Created','ProductionOrder.Updated','ProductionOrder.Refreshed','ProductionOrder.Released','ProductionOrder.Started','ProductionOrder.Completed','ProductionOrder.Cancelled')))";

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
