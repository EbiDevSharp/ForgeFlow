using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> customer)
    {
        customer.ToTable("Customer", Schema, table =>
        {
            table.HasCheckConstraint("CK_Customer_Code", CodeCheck);
            table.HasCheckConstraint("CK_Customer_Name", NameCheck);
            table.HasCheckConstraint("CK_Customer_Metadata", MetadataCheck);
        });
        customer.HasKey(x => x.Id).HasName("PK_Customer");
        customer.Property(x => x.Id).UseIdentityColumn();
        customer.Property(x => x.OrganizationId).IsRequired();
        customer.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        customer.Property(x => x.Name).HasMaxLength(200).IsRequired();
        customer.Property(x => x.ContactName).HasMaxLength(120);
        customer.Property(x => x.Phone).HasMaxLength(32).IsUnicode(false);
        customer.Property(x => x.Email).HasMaxLength(200);
        customer.Property(x => x.ShippingAddress).HasMaxLength(500);
        customer.Property(x => x.TaxCode).HasMaxLength(40);
        customer.Property(x => x.Notes).HasMaxLength(1000);
        customer.Property(x => x.IsActive).IsRequired();
        AddAuditMetadata(customer);
        customer.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_Customer_Org_Id");
        customer.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique().HasDatabaseName("UX_Customer_Org_Code");
        customer.HasIndex(x => new { x.OrganizationId, x.IsActive, x.Code, x.Id }).HasDatabaseName("IX_Customer_Org_IsActive_Code");
        customer.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Customer_Organization");
    }
}

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> salesOrder)
    {
        salesOrder.ToTable("SalesOrder", Schema, table =>
        {
            table.HasCheckConstraint("CK_SalesOrder_Number", "DATALENGTH([OrderNumber]) BETWEEN 1 AND 32 AND DATALENGTH([OrderNumber]) = LEN([OrderNumber]) AND [OrderNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OrderNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_SalesOrder_Status", "[Status] BETWEEN 1 AND 6");
            table.HasCheckConstraint("CK_SalesOrder_Dates", "[RequestedDeliveryDate] IS NULL OR [RequestedDeliveryDate] >= [OrderDate]");
            table.HasCheckConstraint("CK_SalesOrder_Currency", "DATALENGTH([CurrencyCode]) = 3 AND DATALENGTH([CurrencyCode]) = LEN([CurrencyCode]) AND [CurrencyCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'");
            table.HasCheckConstraint("CK_SalesOrder_Metadata", MetadataCheck);
        });
        salesOrder.HasKey(x => x.Id).HasName("PK_SalesOrder");
        salesOrder.Property(x => x.Id).UseIdentityColumn();
        salesOrder.Property(x => x.OrganizationId).IsRequired();
        salesOrder.Property(x => x.SiteId).IsRequired();
        salesOrder.Property(x => x.CustomerId).IsRequired();
        salesOrder.Property(x => x.OrderNumber).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        salesOrder.Property(x => x.Status).HasConversion<byte>().IsRequired();
        salesOrder.Property(x => x.OrderDate).HasColumnType("date").IsRequired();
        salesOrder.Property(x => x.RequestedDeliveryDate).HasColumnType("date");
        salesOrder.Property(x => x.SourceWarehouseId);
        salesOrder.Property(x => x.CurrencyCode).HasMaxLength(3).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        salesOrder.Property(x => x.ShippingAddressSnapshot).HasMaxLength(500);
        salesOrder.Property(x => x.Notes).HasMaxLength(1000);
        salesOrder.Property(x => x.CancellationReason).HasMaxLength(300);
        salesOrder.Property(x => x.ApprovedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        salesOrder.Property(x => x.ClosedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        salesOrder.Property(x => x.CancelledAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        AddAuditMetadata(salesOrder);
        salesOrder.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_SalesOrder_Org_Id");
        salesOrder.HasIndex(x => new { x.OrganizationId, x.OrderNumber }).IsUnique().HasDatabaseName("UX_SalesOrder_Org_Number");
        salesOrder.HasIndex(x => new { x.OrganizationId, x.SiteId, x.Status, x.OrderDate }).HasDatabaseName("IX_SalesOrder_Org_Site_Status");
        salesOrder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SalesOrder_Organization");
        salesOrder.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SalesOrder_Site");
        salesOrder.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CustomerId })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SalesOrder_Customer");
        salesOrder.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SourceWarehouseId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SalesOrder_SourceWarehouse");
        salesOrder.HasOne<Currency>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.CurrencyCode })
            .HasPrincipalKey(c => new { c.OrganizationId, c.Code }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SalesOrder_Currency");
        salesOrder.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> soLine)
    {
        soLine.ToTable("SalesOrderLine", Schema, table =>
        {
            table.HasCheckConstraint("CK_SalesOrderLine_Num", "[LineNumber] > 0");
            table.HasCheckConstraint("CK_SalesOrderLine_Qty", "[OrderedQuantity] > 0 AND [OrderedBaseQuantity] > 0 AND [DispatchedBaseQuantity] >= 0 AND [DispatchedBaseQuantity] <= [OrderedBaseQuantity]");
            table.HasCheckConstraint("CK_SalesOrderLine_Price", "[UnitPrice] IS NULL OR [UnitPrice] >= 0");
            table.HasCheckConstraint("CK_SalesOrderLine_Metadata", MetadataCheck);
        });
        soLine.HasKey(x => x.Id).HasName("PK_SalesOrderLine");
        soLine.Property(x => x.Id).UseIdentityColumn();
        soLine.Property(x => x.OrganizationId).IsRequired();
        soLine.Property(x => x.SalesOrderId).IsRequired();
        soLine.Property(x => x.LineNumber).IsRequired();
        soLine.Property(x => x.ItemId).IsRequired();
        soLine.Property(x => x.OrderedQuantity).HasPrecision(18, 6).IsRequired();
        soLine.Property(x => x.OrderedUnitId).IsRequired();
        soLine.Property(x => x.BaseUnitIdSnapshot).IsRequired();
        soLine.Property(x => x.OrderedBaseQuantity).HasPrecision(18, 6).IsRequired();
        soLine.Property(x => x.DispatchedBaseQuantity).HasPrecision(18, 6).IsRequired();
        soLine.Property(x => x.UnitPrice).HasPrecision(18, 4);
        soLine.Property(x => x.Notes).HasMaxLength(300);
        AddAuditMetadata(soLine);
        soLine.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_SalesOrderLine_Org_Id");
        soLine.HasIndex(x => new { x.OrganizationId, x.SalesOrderId, x.LineNumber }).IsUnique().HasDatabaseName("UX_SalesOrderLine_Org_Order_Line");
        soLine.HasIndex(x => new { x.OrganizationId, x.ItemId }).HasDatabaseName("IX_SalesOrderLine_Org_Item");
        soLine.HasOne<SalesOrder>().WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.OrganizationId, x.SalesOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SalesOrderLine_Order");
        soLine.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SalesOrderLine_Item");
        soLine.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.OrderedUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SalesOrderLine_OrderedUnit");
        soLine.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BaseUnitIdSnapshot })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_SalesOrderLine_BaseUnit");
    }
}

internal sealed class DeliveryOrderConfiguration : IEntityTypeConfiguration<DeliveryOrder>
{
    public void Configure(EntityTypeBuilder<DeliveryOrder> deliveryOrder)
    {
        deliveryOrder.ToTable("DeliveryOrder", Schema, table =>
        {
            table.HasCheckConstraint("CK_DeliveryOrder_Number", "DATALENGTH([DispatchNumber]) BETWEEN 1 AND 32 AND DATALENGTH([DispatchNumber]) = LEN([DispatchNumber]) AND [DispatchNumber] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([DispatchNumber], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_DeliveryOrder_Status", "[Status] IN (1,2)");
            table.HasCheckConstraint("CK_DeliveryOrder_Metadata", MetadataCheck);
        });
        deliveryOrder.HasKey(x => x.Id).HasName("PK_DeliveryOrder");
        deliveryOrder.Property(x => x.Id).UseIdentityColumn();
        deliveryOrder.Property(x => x.OrganizationId).IsRequired();
        deliveryOrder.Property(x => x.SiteId).IsRequired();
        deliveryOrder.Property(x => x.SalesOrderId).IsRequired();
        deliveryOrder.Property(x => x.WarehouseId).IsRequired();
        deliveryOrder.Property(x => x.DispatchNumber).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        deliveryOrder.Property(x => x.Status).HasConversion<byte>().IsRequired();
        deliveryOrder.Property(x => x.DispatchDate).HasColumnType("date").IsRequired();
        deliveryOrder.Property(x => x.CarrierName).HasMaxLength(120);
        deliveryOrder.Property(x => x.TrackingNumber).HasMaxLength(64);
        deliveryOrder.Property(x => x.Notes).HasMaxLength(1000);
        deliveryOrder.Property(x => x.PostedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        deliveryOrder.Property(x => x.ReversedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        deliveryOrder.Property(x => x.ReversalReason).HasMaxLength(300);
        AddAuditMetadata(deliveryOrder);
        deliveryOrder.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_DeliveryOrder_Org_Id");
        deliveryOrder.HasIndex(x => new { x.OrganizationId, x.DispatchNumber }).IsUnique().HasDatabaseName("UX_DeliveryOrder_Org_Number");
        deliveryOrder.HasIndex(x => new { x.OrganizationId, x.SiteId, x.SalesOrderId, x.DispatchDate }).HasDatabaseName("IX_DeliveryOrder_Org_Site_Order");
        deliveryOrder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_DeliveryOrder_Organization");
        deliveryOrder.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_DeliveryOrder_Site");
        deliveryOrder.HasOne<SalesOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SalesOrderId })
            .HasPrincipalKey(so => new { so.OrganizationId, so.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_DeliveryOrder_SalesOrder");
        deliveryOrder.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_DeliveryOrder_Warehouse");
        deliveryOrder.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class DeliveryOrderLineConfiguration : IEntityTypeConfiguration<DeliveryOrderLine>
{
    public void Configure(EntityTypeBuilder<DeliveryOrderLine> doLine)
    {
        doLine.ToTable("DeliveryOrderLine", Schema, table =>
        {
            table.HasCheckConstraint("CK_DeliveryOrderLine_Num", "[LineNumber] > 0");
            table.HasCheckConstraint("CK_DeliveryOrderLine_Qty", "[DispatchedBaseQuantity] > 0");
            table.HasCheckConstraint("CK_DeliveryOrderLine_LotCode", LotCodeCheck);
            table.HasCheckConstraint("CK_DeliveryOrderLine_Metadata", MetadataCheck);
        });
        doLine.HasKey(x => x.Id).HasName("PK_DeliveryOrderLine");
        doLine.Property(x => x.Id).UseIdentityColumn();
        doLine.Property(x => x.OrganizationId).IsRequired();
        doLine.Property(x => x.DeliveryOrderId).IsRequired();
        doLine.Property(x => x.SalesOrderLineId).IsRequired();
        doLine.Property(x => x.LineNumber).IsRequired();
        doLine.Property(x => x.ItemId).IsRequired();
        doLine.Property(x => x.LotCode).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        doLine.Property(x => x.DispatchedBaseQuantity).HasPrecision(18, 6).IsRequired();
        doLine.Property(x => x.BaseUnitIdSnapshot).IsRequired();
        doLine.Property(x => x.QualityInspectionId);
        doLine.Property(x => x.CertificateNumberSnapshot).HasMaxLength(64);
        doLine.Property(x => x.Notes).HasMaxLength(300);
        AddAuditMetadata(doLine);
        doLine.HasIndex(x => new { x.OrganizationId, x.DeliveryOrderId, x.LineNumber }).IsUnique().HasDatabaseName("UX_DeliveryOrderLine_Org_Order_Line");
        doLine.HasIndex(x => new { x.OrganizationId, x.ItemId, x.LotCode }).HasDatabaseName("IX_DeliveryOrderLine_Org_Item_Lot");
        doLine.HasOne<DeliveryOrder>().WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.OrganizationId, x.DeliveryOrderId })
            .HasPrincipalKey(d => new { d.OrganizationId, d.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_DeliveryOrderLine_Order");
        doLine.HasOne<SalesOrderLine>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.SalesOrderLineId })
            .HasPrincipalKey(sol => new { sol.OrganizationId, sol.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_DeliveryOrderLine_SalesOrderLine");
        doLine.HasOne<Item>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_DeliveryOrderLine_Item");
        doLine.HasOne<Unit>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.BaseUnitIdSnapshot })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_DeliveryOrderLine_BaseUnit");
        doLine.HasOne<QualityInspection>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.QualityInspectionId })
            .HasPrincipalKey(q => new { q.OrganizationId, q.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_DeliveryOrderLine_QualityInspection");
    }
}
