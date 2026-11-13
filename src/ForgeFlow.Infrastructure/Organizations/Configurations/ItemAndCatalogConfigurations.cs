using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class ClassificationSetConfiguration : IEntityTypeConfiguration<ClassificationSet>
{
    public void Configure(EntityTypeBuilder<ClassificationSet> classSet)
    {
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
    }
}

internal sealed class ClassificationConfiguration : IEntityTypeConfiguration<Classification>
{
    public void Configure(EntityTypeBuilder<Classification> classification)
    {
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
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> item)
    {
        item.ToTable("Item", Schema, table =>
        {
            table.HasCheckConstraint("CK_Item_Code", CodeCheck);
            table.HasCheckConstraint("CK_Item_Name", NameCheck);
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
    }
}

internal sealed class ItemClassificationConfiguration : IEntityTypeConfiguration<ItemClassification>
{
    public void Configure(EntityTypeBuilder<ItemClassification> itemClass)
    {
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
    }
}

internal sealed class ItemUnitConversionConfiguration : IEntityTypeConfiguration<ItemUnitConversion>
{
    public void Configure(EntityTypeBuilder<ItemUnitConversion> itemConv)
    {
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
    }
}

internal sealed class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> attrDef)
    {
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
    }
}

internal sealed class ItemAttributeValueConfiguration : IEntityTypeConfiguration<ItemAttributeValue>
{
    public void Configure(EntityTypeBuilder<ItemAttributeValue> attrVal)
    {
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
    }
}
