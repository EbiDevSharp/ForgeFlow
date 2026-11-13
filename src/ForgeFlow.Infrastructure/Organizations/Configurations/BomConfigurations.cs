using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class BomConfiguration : IEntityTypeConfiguration<Bom>
{
    public void Configure(EntityTypeBuilder<Bom> bom)
    {
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
    }
}

internal sealed class BomVersionConfiguration : IEntityTypeConfiguration<BomVersion>
{
    public void Configure(EntityTypeBuilder<BomVersion> ver)
    {
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
    }
}

internal sealed class BomInputConfiguration : IEntityTypeConfiguration<BomInput>
{
    public void Configure(EntityTypeBuilder<BomInput> bomInput)
    {
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
    }
}

internal sealed class BomOutputConfiguration : IEntityTypeConfiguration<BomOutput>
{
    public void Configure(EntityTypeBuilder<BomOutput> bomOutput)
    {
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
    }
}

internal sealed class BomApplicabilityConfiguration : IEntityTypeConfiguration<BomApplicability>
{
    public void Configure(EntityTypeBuilder<BomApplicability> app)
    {
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
    }
}

internal sealed class BomApplicabilityConditionConfiguration : IEntityTypeConfiguration<BomApplicabilityCondition>
{
    public void Configure(EntityTypeBuilder<BomApplicabilityCondition> cond)
    {
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
    }
}
