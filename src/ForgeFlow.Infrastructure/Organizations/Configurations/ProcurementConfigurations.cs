using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> supplier)
    {
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
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> purchaseOrder)
    {
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
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> poLine)
    {
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
}
