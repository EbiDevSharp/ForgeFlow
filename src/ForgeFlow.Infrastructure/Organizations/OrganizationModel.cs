using ForgeFlow.Domain.Organizations;
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
    private const string MetadataCheck = "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))";

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
            table.HasCheckConstraint("CK_OrganizationChange_Event", "DATALENGTH([EventCode]) = LEN([EventCode]) AND (([SiteId] IS NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Organization.Created','Organization.Renamed','Organization.Activated','Organization.Deactivated')) OR ([SiteId] IS NOT NULL AND [EventCode] COLLATE Latin1_General_100_BIN2 IN ('Site.Created','Site.Renamed','Site.Activated','Site.Deactivated')))");
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
        entity.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        entity.Property<long>("CreatedByActorId").IsRequired();
        entity.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        entity.Property<long?>("UpdatedByActorId");
        entity.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
    }
}
