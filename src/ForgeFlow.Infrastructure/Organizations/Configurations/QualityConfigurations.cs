using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class QualityInspectionConfiguration : IEntityTypeConfiguration<QualityInspection>
{
    public void Configure(EntityTypeBuilder<QualityInspection> qi)
    {
        qi.ToTable("QualityInspection", Schema, table =>
        {
            table.HasCheckConstraint("CK_QualityInspection_Number", InspectionNumCheck);
            table.HasCheckConstraint("CK_QualityInspection_LotCode", LotCodeCheck);
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
        qi.Property(x => x.CertificateNumber).HasMaxLength(64);
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
    }
}

internal sealed class QualityInspectionLineConfiguration : IEntityTypeConfiguration<QualityInspectionLine>
{
    public void Configure(EntityTypeBuilder<QualityInspectionLine> qiLine)
    {
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
        qiLine.Property(x => x.QualityParameterId);
        qiLine.Property(x => x.ParameterCodeSnapshot).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation);
        qiLine.Property(x => x.SymbolSnapshot).HasMaxLength(24);
        qiLine.Property(x => x.UnitSymbolSnapshot).HasMaxLength(24);
        qiLine.Property(x => x.TestMethodSnapshot).HasMaxLength(64);
        qiLine.Property(x => x.DataType).HasConversion<byte>().HasDefaultValue(QualityParameterDataType.Text).IsRequired();
        qiLine.Property(x => x.MinValueSnapshot).HasPrecision(18, 6);
        qiLine.Property(x => x.MaxValueSnapshot).HasPrecision(18, 6);
        qiLine.Property(x => x.TargetValueSnapshot).HasPrecision(18, 6);
        qiLine.Property(x => x.MeasuredNumericValue).HasPrecision(18, 6);
        qiLine.Property(x => x.IsMandatory).HasDefaultValue(false).IsRequired();
        AddAuditMetadata(qiLine);
        qiLine.HasIndex(x => new { x.OrganizationId, x.QualityInspectionId, x.LineNumber }).IsUnique().HasDatabaseName("UX_QualityInspectionLine_Org_Insp_Line");
        qiLine.HasOne<QualityInspection>().WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.OrganizationId, x.QualityInspectionId })
            .HasPrincipalKey(q => new { q.OrganizationId, q.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualityInspectionLine_Inspection");
        qiLine.HasOne<QualityParameter>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.QualityParameterId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualityInspectionLine_Parameter");
    }
}

internal sealed class QualityParameterConfiguration : IEntityTypeConfiguration<QualityParameter>
{
    public void Configure(EntityTypeBuilder<QualityParameter> qParam)
    {
        qParam.ToTable("QualityParameter", Schema, table =>
        {
            table.HasCheckConstraint("CK_QualityParameter_Code", CodeCheck);
            table.HasCheckConstraint("CK_QualityParameter_Name", "DATALENGTH([Name]) BETWEEN 2 AND 240 AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0");
            table.HasCheckConstraint("CK_QualityParameter_Symbol", "DATALENGTH(LTRIM(RTRIM([Symbol]))) > 0");
            table.HasCheckConstraint("CK_QualityParameter_Type", "[DataType] IN (1,2,3) AND [DecimalPlaces] BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_QualityParameter_Metadata", MetadataCheck);
        });
        qParam.HasKey(x => x.Id).HasName("PK_QualityParameter");
        qParam.Property(x => x.Id).UseIdentityColumn();
        qParam.Property(x => x.OrganizationId).IsRequired();
        qParam.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        qParam.Property(x => x.Name).HasMaxLength(120).IsRequired();
        qParam.Property(x => x.Symbol).HasMaxLength(24).IsRequired();
        qParam.Property(x => x.UnitSymbol).HasMaxLength(24);
        qParam.Property(x => x.DataType).HasConversion<byte>().IsRequired();
        qParam.Property(x => x.DecimalPlaces).IsRequired();
        qParam.Property(x => x.DefaultTestMethod).HasMaxLength(64);
        qParam.Property(x => x.IsActive).IsRequired();
        AddAuditMetadata(qParam);
        qParam.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_QualityParameter_Org_Id");
        qParam.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique().HasDatabaseName("UX_QualityParameter_Org_Code");
        qParam.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_QualityParameter_Organization");
    }
}

internal sealed class QualitySpecConfiguration : IEntityTypeConfiguration<QualitySpec>
{
    public void Configure(EntityTypeBuilder<QualitySpec> qSpec)
    {
        qSpec.ToTable("QualitySpec", Schema, table =>
        {
            table.HasCheckConstraint("CK_QualitySpec_Code", CodeCheck);
            table.HasCheckConstraint("CK_QualitySpec_Target", "([TargetKind] = 1 AND [ItemId] IS NOT NULL AND [ClassificationId] IS NULL) OR ([TargetKind] = 2 AND [ClassificationId] IS NOT NULL AND [ItemId] IS NULL)");
            table.HasCheckConstraint("CK_QualitySpec_Checkpoint", "[Checkpoint] IN (1,2,3)");
            table.HasCheckConstraint("CK_QualitySpec_Version", "[Version] >= 1");
            table.HasCheckConstraint("CK_QualitySpec_Metadata", MetadataCheck);
        });
        qSpec.HasKey(x => x.Id).HasName("PK_QualitySpec");
        qSpec.Property(x => x.Id).UseIdentityColumn();
        qSpec.Property(x => x.OrganizationId).IsRequired();
        qSpec.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        qSpec.Property(x => x.Name).HasMaxLength(120).IsRequired();
        qSpec.Property(x => x.TargetKind).HasConversion<byte>().IsRequired();
        qSpec.Property(x => x.ItemId);
        qSpec.Property(x => x.ClassificationId);
        qSpec.Property(x => x.Checkpoint).HasConversion<byte>().IsRequired();
        qSpec.Property(x => x.Version).IsRequired();
        qSpec.Property(x => x.IsActive).IsRequired();
        qSpec.Property(x => x.Notes).HasMaxLength(1000);
        AddAuditMetadata(qSpec);
        qSpec.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_QualitySpec_Org_Id");
        qSpec.HasIndex(x => new { x.OrganizationId, x.Code, x.Version }).IsUnique().HasDatabaseName("UX_QualitySpec_Org_Code_Version");
        qSpec.HasIndex(x => new { x.OrganizationId, x.ItemId, x.Checkpoint }).IsUnique().HasFilter("[IsActive] = 1 AND [TargetKind] = 1").HasDatabaseName("UX_QualitySpec_ActiveItem");
        qSpec.HasIndex(x => new { x.OrganizationId, x.ClassificationId, x.Checkpoint }).IsUnique().HasFilter("[IsActive] = 1 AND [TargetKind] = 2").HasDatabaseName("UX_QualitySpec_ActiveClass");
        qSpec.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_QualitySpec_Organization");
        qSpec.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualitySpec_Item");
        qSpec.HasOne<Classification>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ClassificationId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualitySpec_Classification");
        qSpec.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class QualitySpecLineConfiguration : IEntityTypeConfiguration<QualitySpecLine>
{
    public void Configure(EntityTypeBuilder<QualitySpecLine> qSpecLine)
    {
        qSpecLine.ToTable("QualitySpecLine", Schema, table =>
        {
            table.HasCheckConstraint("CK_QualitySpecLine_Seq", "[Sequence] > 0");
            table.HasCheckConstraint("CK_QualitySpecLine_Range", "[MinValue] IS NULL OR [MaxValue] IS NULL OR [MaxValue] >= [MinValue]");
            table.HasCheckConstraint("CK_QualitySpecLine_Metadata", MetadataCheck);
        });
        qSpecLine.HasKey(x => x.Id).HasName("PK_QualitySpecLine");
        qSpecLine.Property(x => x.Id).UseIdentityColumn();
        qSpecLine.Property(x => x.OrganizationId).IsRequired();
        qSpecLine.Property(x => x.QualitySpecId).IsRequired();
        qSpecLine.Property(x => x.Sequence).IsRequired();
        qSpecLine.Property(x => x.QualityParameterId).IsRequired();
        qSpecLine.Property(x => x.MinValue).HasPrecision(18, 6);
        qSpecLine.Property(x => x.MaxValue).HasPrecision(18, 6);
        qSpecLine.Property(x => x.TargetValue).HasPrecision(18, 6);
        qSpecLine.Property(x => x.ExpectedTextSpec).HasMaxLength(100);
        qSpecLine.Property(x => x.IsMandatory).IsRequired();
        qSpecLine.Property(x => x.Notes).HasMaxLength(300);
        AddAuditMetadata(qSpecLine);
        qSpecLine.HasIndex(x => new { x.OrganizationId, x.QualitySpecId, x.Sequence }).IsUnique().HasDatabaseName("UX_QualitySpecLine_Spec_Seq");
        qSpecLine.HasIndex(x => new { x.OrganizationId, x.QualitySpecId, x.QualityParameterId }).IsUnique().HasDatabaseName("UX_QualitySpecLine_Spec_Param");
        qSpecLine.HasOne<QualitySpec>().WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.OrganizationId, x.QualitySpecId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualitySpecLine_Spec");
        qSpecLine.HasOne<QualityParameter>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.QualityParameterId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_QualitySpecLine_Param");
    }
}
