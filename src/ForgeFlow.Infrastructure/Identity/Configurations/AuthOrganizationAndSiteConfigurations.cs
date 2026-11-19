using ForgeFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Identity.Configurations.AuthModelConventions;

namespace ForgeFlow.Infrastructure.Identity.Configurations;

internal sealed class AuthOrganizationConfiguration : IEntityTypeConfiguration<Organization>, IAuthEntityConfiguration
{
    private const string CodeCheck = "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";
    private const string NameCheck = "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0";
    private const string MetadataCheck = "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))";

    public void Configure(EntityTypeBuilder<Organization> organization)
    {
        organization.ToTable("Organization", "organization", table =>
        {
            table.ExcludeFromMigrations();
            table.HasCheckConstraint("CK_Organization_Code", CodeCheck);
            table.HasCheckConstraint("CK_Organization_Name", NameCheck);
            table.HasCheckConstraint("CK_Organization_Metadata", MetadataCheck);
        });
        organization.Property<string>("Code").HasMaxLength(32).IsUnicode(false).UseCollation("Latin1_General_100_BIN2").IsRequired();
        organization.Property<string>("Name").HasMaxLength(200).IsRequired();
        organization.Property<bool>("IsActive").IsRequired();
        organization.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        organization.Property<long>("CreatedByActorId").IsRequired();
        organization.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        organization.Property<long?>("UpdatedByActorId");
        organization.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();

        organization.Property(o => o.Id).UseIdentityColumn();
        organization.HasKey(o => o.Id).HasName("PK_Organization");
        organization.HasIndex(o => o.Code).IsUnique().HasDatabaseName("UX_Organization_Code");
        organization.HasIndex(o => new { o.IsActive, o.Code, o.Id }).HasDatabaseName("IX_Organization_IsActive_Code");
        organization.Navigation(o => o.Sites).HasField("sites").UsePropertyAccessMode(PropertyAccessMode.Field);
        organization.Navigation(o => o.Sites).AutoInclude();
    }
}

internal sealed class AuthSiteConfiguration : IEntityTypeConfiguration<Site>, IAuthEntityConfiguration
{
    private const string CodeCheck = "DATALENGTH([Code]) BETWEEN 1 AND 32 AND DATALENGTH([Code]) = LEN([Code]) AND [Code] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_-]%' AND LEFT([Code], 1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'";
    private const string NameCheck = "DATALENGTH([Name]) BETWEEN 2 AND 400 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name]))) AND DATALENGTH(LTRIM(RTRIM([Name]))) > 0";
    private const string MetadataCheck = "[CreatedByActorId] > 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId] > 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]))";

    public void Configure(EntityTypeBuilder<Site> site)
    {
        site.ToTable("Site", "organization", table =>
        {
            table.ExcludeFromMigrations();
            table.HasCheckConstraint("CK_Site_Code", CodeCheck);
            table.HasCheckConstraint("CK_Site_Name", NameCheck);
            table.HasCheckConstraint("CK_Site_Metadata", MetadataCheck);
        });
        site.Property<string>("Code").HasMaxLength(32).IsUnicode(false).UseCollation("Latin1_General_100_BIN2").IsRequired();
        site.Property<string>("Name").HasMaxLength(200).IsRequired();
        site.Property<bool>("IsActive").IsRequired();
        site.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc).IsRequired();
        site.Property<long>("CreatedByActorId").IsRequired();
        site.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        site.Property<long?>("UpdatedByActorId");
        site.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();

        site.Property(s => s.Id).UseIdentityColumn();
        site.HasKey(s => s.Id).HasName("PK_Site");
        site.Property(s => s.OrganizationId).IsRequired();
        site.HasAlternateKey(s => new { s.OrganizationId, s.Id }).HasName("AK_Site_Organization_Id");
        site.HasIndex(s => new { s.OrganizationId, s.Code }).IsUnique().HasDatabaseName("UX_Site_Organization_Code");
        site.HasIndex(s => new { s.OrganizationId, s.IsActive, s.Code, s.Id }).HasDatabaseName("IX_Site_Organization_IsActive_Code");
        site.HasOne(s => s.Organization).WithMany(o => o.Sites).HasForeignKey(s => s.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Site_Organization");
        site.Navigation(s => s.Organization).HasField("organization").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
