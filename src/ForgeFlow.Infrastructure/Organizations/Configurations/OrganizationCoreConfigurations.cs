using ForgeFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> organization)
    {
        Business(organization, "Organization");
        organization.Property(o => o.Id).UseIdentityColumn();
        organization.HasKey(o => o.Id).HasName("PK_Organization");
        organization.HasIndex(o => o.Code).IsUnique().HasDatabaseName("UX_Organization_Code");
        organization.HasIndex(o => new { o.IsActive, o.Code, o.Id }).HasDatabaseName("IX_Organization_IsActive_Code");
        organization.Navigation(o => o.Sites).HasField("sites").UsePropertyAccessMode(PropertyAccessMode.Field);
        organization.Navigation(o => o.Sites).AutoInclude();
    }
}

internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> site)
    {
        Business(site, "Site");
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

internal sealed class OrganizationChangeConfiguration : IEntityTypeConfiguration<OrganizationChange>
{
    public void Configure(EntityTypeBuilder<OrganizationChange> audit)
    {
        audit.ToTable("OrganizationChange", Schema, table =>
        {
            table.HasCheckConstraint("CK_OrganizationChange_Actor", "[ActorId] > 0");
            table.HasCheckConstraint("CK_OrganizationChange_Json", "ISJSON([ChangesJson]) = 1");
            table.HasCheckConstraint("CK_OrganizationChange_Event", OrganizationModel.OrganizationChangeEventSql);
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
}

internal sealed class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> docSeq)
    {
        docSeq.ToTable("DocumentSequence", Schema, table =>
        {
            table.HasCheckConstraint("CK_DocumentSequence_Type", "[DocumentType] BETWEEN 1 AND 9");
            table.HasCheckConstraint("CK_DocumentSequence_YearMode", "[YearMode] BETWEEN 0 AND 2");
            table.HasCheckConstraint("CK_DocumentSequence_Padding", "[PaddingDigits] BETWEEN 3 AND 8");
            table.HasCheckConstraint("CK_DocumentSequence_NextNumber", "[NextNumber] >= 1");
            table.HasCheckConstraint("CK_DocumentSequence_Metadata", MetadataCheck);
        });
        docSeq.HasKey(x => x.Id).HasName("PK_DocumentSequence");
        docSeq.Property(x => x.Id).UseIdentityColumn();
        docSeq.Property(x => x.OrganizationId).IsRequired();
        docSeq.Property(x => x.DocumentType).HasConversion<byte>().IsRequired();
        docSeq.Property(x => x.Prefix).HasMaxLength(12).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        docSeq.Property(x => x.Separator).HasMaxLength(2).IsUnicode(false).IsRequired();
        docSeq.Property(x => x.YearMode).HasConversion<byte>().IsRequired();
        docSeq.Property(x => x.PaddingDigits).IsRequired();
        docSeq.Property(x => x.NextNumber).IsRequired();
        docSeq.Property(x => x.LastAllocatedYear);
        docSeq.Property(x => x.AllowManualOverride).IsRequired();
        docSeq.Property(x => x.IsActive).IsRequired();
        AddAuditMetadata(docSeq);
        docSeq.HasIndex(x => new { x.OrganizationId, x.DocumentType }).IsUnique().HasDatabaseName("UX_DocumentSequence_Org_Type");
        docSeq.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_DocumentSequence_Organization");
    }
}
