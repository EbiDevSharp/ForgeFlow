using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> po)
    {
        po.ToTable("ProductionOrder", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductionOrder_Number", "DATALENGTH([OrderNumber]) BETWEEN 1 AND 32 AND DATALENGTH([OrderNumber]) = LEN([OrderNumber]) AND [OrderNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OrderNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_ProductionOrder_BomCodeSnapshot", "DATALENGTH([BomCodeSnapshot]) BETWEEN 1 AND 32 AND DATALENGTH([BomCodeSnapshot]) = LEN([BomCodeSnapshot]) AND [BomCodeSnapshot] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([BomCodeSnapshot], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_ProductionOrder_Status", "[Status] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_ProductionOrder_RequestedQty", "[RequestedQuantity] > 0");
            table.HasCheckConstraint("CK_ProductionOrder_Dates", "[PlannedDueDate] >= [PlannedStartDate]");
            table.HasCheckConstraint("CK_ProductionOrder_BaseOutputQty", "[BaseOutputQuantitySnapshot] > 0 AND [BomVersionNumberSnapshot] > 0");
            table.HasCheckConstraint("CK_ProductionOrder_Actuals", "[ActualProducedQuantity] >= 0 AND [ActualScrapQuantity] >= 0");
            table.HasCheckConstraint("CK_ProductionOrder_RoutingCodeSnapshot", "[RoutingCodeSnapshot] IS NULL OR (DATALENGTH([RoutingCodeSnapshot]) BETWEEN 1 AND 32 AND DATALENGTH([RoutingCodeSnapshot]) = LEN([RoutingCodeSnapshot]) AND [RoutingCodeSnapshot] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([RoutingCodeSnapshot], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]')");
            table.HasCheckConstraint("CK_ProductionOrder_RoutingPair", "([RoutingId] IS NULL AND [RoutingCodeSnapshot] IS NULL) OR ([RoutingId] IS NOT NULL AND [RoutingCodeSnapshot] IS NOT NULL)");
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
        po.Property(x => x.RoutingId);
        po.Property(x => x.RoutingCodeSnapshot).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation);
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
        po.HasOne<Routing>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId, x.RoutingId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.SiteId, r.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrder_Routing");
    }
}

internal sealed class ProductionOrderInputConfiguration : IEntityTypeConfiguration<ProductionOrderInput>
{
    public void Configure(EntityTypeBuilder<ProductionOrderInput> poInput)
    {
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
    }
}

internal sealed class ProductionOrderOutputConfiguration : IEntityTypeConfiguration<ProductionOrderOutput>
{
    public void Configure(EntityTypeBuilder<ProductionOrderOutput> poOutput)
    {
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
    }
}

internal sealed class ProductionOrderContextConfiguration : IEntityTypeConfiguration<ProductionOrderContext>
{
    public void Configure(EntityTypeBuilder<ProductionOrderContext> poCtx)
    {
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
}

internal sealed class ProductionOrderOperationConfiguration : IEntityTypeConfiguration<ProductionOrderOperation>
{
    public void Configure(EntityTypeBuilder<ProductionOrderOperation> poOp)
    {
        poOp.ToTable("ProductionOrderOperation", Schema, table =>
        {
            table.HasCheckConstraint("CK_ProductionOrderOperation_Seq", "[Sequence] >= 1");
            table.HasCheckConstraint("CK_ProductionOrderOperation_Code", "DATALENGTH([OperationCode]) BETWEEN 1 AND 32 AND DATALENGTH([OperationCode]) = LEN([OperationCode]) AND [OperationCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OperationCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_ProductionOrderOperation_Name", "DATALENGTH([OperationName]) BETWEEN 2 AND 400 AND DATALENGTH([OperationName]) = DATALENGTH(LTRIM(RTRIM([OperationName]))) AND DATALENGTH(LTRIM(RTRIM([OperationName]))) > 0");
            table.HasCheckConstraint("CK_ProductionOrderOperation_Times", "[PlannedSetupMinutes] >= 0 AND [PlannedRunMinutes] >= 0 AND [PlannedQueueMinutes] >= 0 AND [PlannedWaitMinutes] >= 0 AND [ActualSetupMinutes] >= 0 AND [ActualRunMinutes] >= 0");
            table.HasCheckConstraint("CK_ProductionOrderOperation_Qty", "[CompletedQuantity] >= 0 AND [ScrapQuantity] >= 0");
            table.HasCheckConstraint("CK_ProductionOrderOperation_Status", "[Status] BETWEEN 1 AND 6");
            table.HasCheckConstraint("CK_ProductionOrderOperation_Metadata", MetadataCheck);
        });
        poOp.HasKey(x => x.Id).HasName("PK_ProductionOrderOperation");
        poOp.Property(x => x.Id).UseIdentityColumn();
        poOp.Property(x => x.OrganizationId).IsRequired();
        poOp.Property(x => x.SiteId).IsRequired();
        poOp.Property(x => x.ProductionOrderId).IsRequired();
        poOp.Property(x => x.Sequence).IsRequired();
        poOp.Property(x => x.OperationCode).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        poOp.Property(x => x.OperationName).HasMaxLength(200).IsRequired();
        poOp.Property(x => x.WorkCenterId).IsRequired();
        poOp.Property(x => x.PlannedSetupMinutes).HasPrecision(18, 2).IsRequired();
        poOp.Property(x => x.PlannedRunMinutes).HasPrecision(18, 2).IsRequired();
        poOp.Property(x => x.PlannedQueueMinutes).HasPrecision(18, 2).IsRequired();
        poOp.Property(x => x.PlannedWaitMinutes).HasPrecision(18, 2).IsRequired();
        poOp.Property(x => x.ActualSetupMinutes).HasPrecision(18, 2).IsRequired();
        poOp.Property(x => x.ActualRunMinutes).HasPrecision(18, 2).IsRequired();
        poOp.Property(x => x.CompletedQuantity).HasPrecision(18, 6).IsRequired();
        poOp.Property(x => x.ScrapQuantity).HasPrecision(18, 6).IsRequired();
        poOp.Property(x => x.RequiresQualityCheck).IsRequired();
        poOp.Property(x => x.Instructions).HasMaxLength(500);
        poOp.Property(x => x.OperatorNotes).HasMaxLength(500);
        poOp.Property(x => x.Status).HasConversion<byte>().IsRequired();
        poOp.Property(x => x.StartedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        poOp.Property(x => x.CompletedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        poOp.Ignore(x => x.PlannedTotalMinutes);
        AddAuditMetadata(poOp);
        poOp.HasIndex(x => new { x.OrganizationId, x.ProductionOrderId, x.Sequence }).IsUnique().HasDatabaseName("UX_ProductionOrderOperation_Order_Seq");
        poOp.HasIndex(x => new { x.OrganizationId, x.SiteId, x.WorkCenterId, x.Status }).HasDatabaseName("IX_ProductionOrderOperation_Org_Site_WC_Status");
        poOp.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ProductionOrderOperation_Organization");
        poOp.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderOperation_Site");
        poOp.HasOne<ProductionOrder>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ProductionOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderOperation_Order");
        poOp.HasOne<WorkCenter>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.SiteId, x.WorkCenterId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.SiteId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_ProductionOrderOperation_WorkCenter");
    }
}
