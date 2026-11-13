using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> wh)
    {
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
    }
}

internal sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> txn)
    {
        txn.ToTable("InventoryTransaction", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryTransaction_DocNum", DocNumberCheck);
            table.HasCheckConstraint("CK_InventoryTransaction_Type", "[TransactionType] BETWEEN 1 AND 10");
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
        txn.Property(x => x.DeliveryOrderId);
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
        txn.HasIndex(x => new { x.OrganizationId, x.DeliveryOrderId }).HasDatabaseName("IX_InventoryTransaction_Org_DeliveryOrder");
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
        txn.HasOne<DeliveryOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DeliveryOrderId })
            .HasPrincipalKey(d => new { d.OrganizationId, d.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_InventoryTransaction_DeliveryOrder");
    }
}

internal sealed class InventoryLedgerEntryConfiguration : IEntityTypeConfiguration<InventoryLedgerEntry>
{
    public void Configure(EntityTypeBuilder<InventoryLedgerEntry> ledger)
    {
        ledger.ToTable("InventoryLedgerEntry", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryLedgerEntry_Qty", "[EnteredQuantity] <> 0 AND [BaseQuantityDelta] <> 0");
            table.HasCheckConstraint("CK_InventoryLedgerEntry_LotCode", LotCodeCheck);
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
    }
}

internal sealed class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> bal)
    {
        bal.ToTable("InventoryBalance", Schema, table =>
        {
            table.HasCheckConstraint("CK_InventoryBalance_NonNegative", "[OnHandBaseQuantity] >= 0");
            table.HasCheckConstraint("CK_InventoryBalance_Blocked", "[BlockedBaseQuantity] >= 0 AND [ReservedBaseQuantity] >= 0 AND ([BlockedBaseQuantity] + [ReservedBaseQuantity]) <= [OnHandBaseQuantity]");
            table.HasCheckConstraint("CK_InventoryBalance_LotCode", LotCodeCheck);
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
        bal.Property(x => x.ReservedBaseQuantity).HasPrecision(18, 6).HasDefaultValue(0m).IsRequired();
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
    }
}

internal sealed class ItemLotConfiguration : IEntityTypeConfiguration<ItemLot>
{
    public void Configure(EntityTypeBuilder<ItemLot> itemLot)
    {
        itemLot.ToTable("ItemLot", Schema, table =>
        {
            table.HasCheckConstraint("CK_ItemLot_LotCode", RequiredLotCodeCheck);
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
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> sr)
    {
        sr.ToTable("StockReservation", Schema, table =>
        {
            table.HasCheckConstraint("CK_StockReservation_Qty", "[ReservedBaseQuantity] > 0 AND [ConsumedBaseQuantity] >= 0 AND [ConsumedBaseQuantity] <= [ReservedBaseQuantity]");
            table.HasCheckConstraint("CK_StockReservation_Status", "[Status] BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_StockReservation_Source", "([SourceKind] = 1 AND [ProductionOrderId] IS NOT NULL AND [SalesOrderId] IS NULL AND [SalesOrderLineId] IS NULL) OR ([SourceKind] = 2 AND [SalesOrderId] IS NOT NULL AND [ProductionOrderId] IS NULL AND [ProductionOrderInputId] IS NULL)");
            table.HasCheckConstraint("CK_StockReservation_LotCode", LotCodeCheck);
            table.HasCheckConstraint("CK_StockReservation_Metadata", MetadataCheck);
        });
        sr.HasKey(x => x.Id).HasName("PK_StockReservation");
        sr.Property(x => x.Id).UseIdentityColumn();
        sr.Property(x => x.OrganizationId).IsRequired();
        sr.Property(x => x.SiteId).IsRequired();
        sr.Property(x => x.WarehouseId).IsRequired();
        sr.Property(x => x.SourceKind).HasConversion<byte>().HasDefaultValue(ReservationSourceKind.ProductionOrder).IsRequired();
        sr.Property(x => x.ProductionOrderId);
        sr.Property(x => x.ProductionOrderInputId);
        sr.Property(x => x.SalesOrderId);
        sr.Property(x => x.SalesOrderLineId);
        sr.Property(x => x.ItemId).IsRequired();
        sr.Property(x => x.LotCode).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).HasDefaultValue(string.Empty).IsRequired();
        sr.Property(x => x.BaseUnitId).IsRequired();
        sr.Property(x => x.ReservedBaseQuantity).HasPrecision(18, 6).IsRequired();
        sr.Property(x => x.ConsumedBaseQuantity).HasPrecision(18, 6).HasDefaultValue(0m).IsRequired();
        sr.Property(x => x.Status).HasConversion<byte>().IsRequired();
        sr.Property(x => x.Notes).HasMaxLength(500);
        sr.Property(x => x.ReleasedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        sr.Ignore(x => x.RemainingReservedBaseQuantity);
        sr.Ignore(x => x.IsOpen);
        AddAuditMetadata(sr);
        sr.HasIndex(x => new { x.OrganizationId, x.ProductionOrderId, x.Status }).HasDatabaseName("IX_StockReservation_Org_Order_Status");
        sr.HasIndex(x => new { x.OrganizationId, x.SalesOrderId, x.Status }).HasDatabaseName("IX_StockReservation_Org_SalesOrder_Status");
        sr.HasIndex(x => new { x.OrganizationId, x.SiteId, x.WarehouseId, x.ItemId, x.LotCode, x.Status }).HasDatabaseName("IX_StockReservation_Org_Site_Wh_Item_Lot_Status");
        sr.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_StockReservation_Organization");
        sr.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_StockReservation_Site");
        sr.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.WarehouseId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_StockReservation_Warehouse");
        sr.HasOne<ProductionOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ProductionOrderId })
            .HasPrincipalKey(p => new { p.OrganizationId, p.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_StockReservation_Order");
        sr.HasOne<SalesOrder>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SalesOrderId })
            .HasPrincipalKey(so => new { so.OrganizationId, so.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_StockReservation_SalesOrder");
        sr.HasOne<SalesOrderLine>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SalesOrderLineId })
            .HasPrincipalKey(sol => new { sol.OrganizationId, sol.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_StockReservation_SalesOrderLine");
        sr.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_StockReservation_Item");
        sr.HasOne<Unit>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BaseUnitId })
            .HasPrincipalKey(u => new { u.OrganizationId, u.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_StockReservation_Unit");
    }
}
