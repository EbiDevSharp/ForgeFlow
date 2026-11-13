using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Routings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class WorkCenterConfiguration : IEntityTypeConfiguration<WorkCenter>
{
    public void Configure(EntityTypeBuilder<WorkCenter> workCenter)
    {
        workCenter.ToTable("WorkCenter", Schema, table =>
        {
            table.HasCheckConstraint("CK_WorkCenter_Code", CodeCheck);
            table.HasCheckConstraint("CK_WorkCenter_Name", NameCheck);
            table.HasCheckConstraint("CK_WorkCenter_Type", "[WorkCenterType] BETWEEN 1 AND 5");
            table.HasCheckConstraint("CK_WorkCenter_Capacity", "[CapacityUnitsPerHour] > 0 AND [DailyAvailableHours] > 0 AND [DailyAvailableHours] <= 24 AND ([HourlyCostRate] IS NULL OR [HourlyCostRate] >= 0)");
            table.HasCheckConstraint("CK_WorkCenter_Metadata", MetadataCheck);
        });
        workCenter.HasKey(x => x.Id).HasName("PK_WorkCenter");
        workCenter.Property(x => x.Id).UseIdentityColumn();
        workCenter.Property(x => x.OrganizationId).IsRequired();
        workCenter.Property(x => x.SiteId).IsRequired();
        workCenter.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        workCenter.Property(x => x.Name).HasMaxLength(200).IsRequired();
        workCenter.Property(x => x.WorkCenterType).HasConversion<byte>().IsRequired();
        workCenter.Property(x => x.CapacityUnitsPerHour).HasPrecision(18, 6).IsRequired();
        workCenter.Property(x => x.DailyAvailableHours).HasPrecision(18, 6).IsRequired();
        workCenter.Property(x => x.HourlyCostRate).HasPrecision(18, 6);
        workCenter.Property(x => x.Notes).HasMaxLength(500);
        workCenter.Property(x => x.IsActive).IsRequired();
        AddAuditMetadata(workCenter);
        workCenter.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_WorkCenter_Org_Id");
        workCenter.HasAlternateKey(x => new { x.OrganizationId, x.SiteId, x.Id }).HasName("AK_WorkCenter_Org_Site_Id");
        workCenter.HasIndex(x => new { x.OrganizationId, x.SiteId, x.Code }).IsUnique().HasDatabaseName("UX_WorkCenter_Org_Site_Code");
        workCenter.HasIndex(x => new { x.OrganizationId, x.SiteId, x.IsActive, x.Code }).HasDatabaseName("IX_WorkCenter_Org_Site_Active");
        workCenter.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_WorkCenter_Organization");
        workCenter.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_WorkCenter_Site");
    }
}

internal sealed class RoutingConfiguration : IEntityTypeConfiguration<Routing>
{
    public void Configure(EntityTypeBuilder<Routing> routing)
    {
        routing.ToTable("Routing", Schema, table =>
        {
            table.HasCheckConstraint("CK_Routing_Code", CodeCheck);
            table.HasCheckConstraint("CK_Routing_Name", NameCheck);
            table.HasCheckConstraint("CK_Routing_Version", "[VersionNumber] >= 1");
            table.HasCheckConstraint("CK_Routing_Status", "[Status] BETWEEN 1 AND 3");
            table.HasCheckConstraint("CK_Routing_Metadata", MetadataCheck);
        });
        routing.HasKey(x => x.Id).HasName("PK_Routing");
        routing.Property(x => x.Id).UseIdentityColumn();
        routing.Property(x => x.OrganizationId).IsRequired();
        routing.Property(x => x.SiteId).IsRequired();
        routing.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        routing.Property(x => x.Name).HasMaxLength(200).IsRequired();
        routing.Property(x => x.ItemId).IsRequired();
        routing.Property(x => x.BomVersionId);
        routing.Property(x => x.VersionNumber).IsRequired();
        routing.Property(x => x.Status).HasConversion<byte>().IsRequired();
        routing.Property(x => x.IsDefault).IsRequired();
        routing.Property(x => x.Notes).HasMaxLength(500);
        routing.Property(x => x.ActivatedAtUtc).HasColumnType("datetime2(7)").HasConversion(Utc);
        AddAuditMetadata(routing);
        routing.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_Routing_Org_Id");
        routing.HasAlternateKey(x => new { x.OrganizationId, x.SiteId, x.Id }).HasName("AK_Routing_Org_Site_Id");
        routing.HasIndex(x => new { x.OrganizationId, x.SiteId, x.Code, x.VersionNumber }).IsUnique().HasDatabaseName("UX_Routing_Org_Site_Code_Version");
        routing.HasIndex(x => new { x.OrganizationId, x.SiteId, x.ItemId }).IsUnique().HasFilter("[Status] = 2 AND [IsDefault] = 1").HasDatabaseName("UX_Routing_Org_Site_Item_Default");
        routing.HasIndex(x => new { x.OrganizationId, x.SiteId, x.ItemId, x.Status }).HasDatabaseName("IX_Routing_Org_Site_Item_Status");
        routing.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Routing_Organization");
        routing.HasOne<Site>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SiteId })
            .HasPrincipalKey(s => new { s.OrganizationId, s.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Routing_Site");
        routing.HasOne<Item>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId })
            .HasPrincipalKey(i => new { i.OrganizationId, i.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Routing_Item");
        routing.HasOne<BomVersion>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.BomVersionId })
            .HasPrincipalKey(v => new { v.OrganizationId, v.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Routing_BomVersion");
        routing.Navigation(x => x.Operations).HasField("_operations").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RoutingOperationConfiguration : IEntityTypeConfiguration<RoutingOperation>
{
    public void Configure(EntityTypeBuilder<RoutingOperation> routingOp)
    {
        routingOp.ToTable("RoutingOperation", Schema, table =>
        {
            table.HasCheckConstraint("CK_RoutingOperation_Seq", "[Sequence] >= 1");
            table.HasCheckConstraint("CK_RoutingOperation_Code", "DATALENGTH([OperationCode]) BETWEEN 1 AND 32 AND DATALENGTH([OperationCode]) = LEN([OperationCode]) AND [OperationCode] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([OperationCode], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
            table.HasCheckConstraint("CK_RoutingOperation_Name", "DATALENGTH([OperationName]) BETWEEN 2 AND 400 AND DATALENGTH([OperationName]) = DATALENGTH(LTRIM(RTRIM([OperationName]))) AND DATALENGTH(LTRIM(RTRIM([OperationName]))) > 0");
            table.HasCheckConstraint("CK_RoutingOperation_Times", "[SetupMinutes] >= 0 AND [RunMinutesPerUnit] > 0 AND [QueueMinutes] >= 0 AND [WaitMinutes] >= 0");
            table.HasCheckConstraint("CK_RoutingOperation_Metadata", MetadataCheck);
        });
        routingOp.HasKey(x => x.Id).HasName("PK_RoutingOperation");
        routingOp.Property(x => x.Id).UseIdentityColumn();
        routingOp.Property(x => x.OrganizationId).IsRequired();
        routingOp.Property(x => x.SiteId).IsRequired();
        routingOp.Property(x => x.RoutingId).IsRequired();
        routingOp.Property(x => x.Sequence).IsRequired();
        routingOp.Property(x => x.OperationCode).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        routingOp.Property(x => x.OperationName).HasMaxLength(200).IsRequired();
        routingOp.Property(x => x.WorkCenterId).IsRequired();
        routingOp.Property(x => x.SetupMinutes).HasPrecision(18, 2).IsRequired();
        routingOp.Property(x => x.RunMinutesPerUnit).HasPrecision(18, 4).IsRequired();
        routingOp.Property(x => x.QueueMinutes).HasPrecision(18, 2).IsRequired();
        routingOp.Property(x => x.WaitMinutes).HasPrecision(18, 2).IsRequired();
        routingOp.Property(x => x.RequiresQualityCheck).IsRequired();
        routingOp.Property(x => x.Instructions).HasMaxLength(500);
        AddAuditMetadata(routingOp);
        routingOp.HasIndex(x => new { x.OrganizationId, x.RoutingId, x.Sequence }).IsUnique().HasDatabaseName("UX_RoutingOperation_Routing_Seq");
        routingOp.HasIndex(x => new { x.OrganizationId, x.SiteId, x.WorkCenterId }).HasDatabaseName("IX_RoutingOperation_Org_Site_WC");
        routingOp.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_RoutingOperation_Organization");
        routingOp.HasOne<Routing>().WithMany(r => r.Operations)
            .HasForeignKey(x => new { x.OrganizationId, x.SiteId, x.RoutingId })
            .HasPrincipalKey(r => new { r.OrganizationId, r.SiteId, r.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_RoutingOperation_Routing");
        routingOp.HasOne<WorkCenter>().WithMany()
            .HasForeignKey(x => new { x.OrganizationId, x.SiteId, x.WorkCenterId })
            .HasPrincipalKey(w => new { w.OrganizationId, w.SiteId, w.Id }).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_RoutingOperation_WorkCenter");
    }
}
