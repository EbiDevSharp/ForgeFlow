using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Organizations.Configurations.OrganizationModelConventions;

namespace ForgeFlow.Infrastructure.Organizations.Configurations;

internal sealed class BusinessPartnerRoleDefinitionConfiguration : IEntityTypeConfiguration<BusinessPartnerRoleDefinition>
{
    public void Configure(EntityTypeBuilder<BusinessPartnerRoleDefinition> builder)
    {
        builder.ToTable("BusinessPartnerRoleDefinition", Schema);
        builder.HasKey(x => x.Id).HasName("PK_BusinessPartnerRoleDefinition");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsSystem).IsRequired();
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasData(
            new BusinessPartnerRoleDefinition(1, "SUPPLIER", "Supplier", true, 1),
            new BusinessPartnerRoleDefinition(2, "CUSTOMER", "Customer", true, 2),
            new BusinessPartnerRoleDefinition(3, "CONTRACTOR", "Contractor", true, 3)
        );
    }
}

internal sealed class BusinessPartnerConfiguration : IEntityTypeConfiguration<BusinessPartner>
{
    public void Configure(EntityTypeBuilder<BusinessPartner> partner)
    {
        partner.ToTable("BusinessPartner", Schema, table =>
        {
            table.HasCheckConstraint("CK_BusinessPartner_Code", CodeCheck);
            table.HasCheckConstraint("CK_BusinessPartner_Name", NameCheck);
            table.HasCheckConstraint("CK_BusinessPartner_PersonType", "[PersonType] BETWEEN 1 AND 2");
            table.HasCheckConstraint("CK_BusinessPartner_Metadata", MetadataCheck);
        });

        partner.HasKey(x => x.Id).HasName("PK_BusinessPartner");
        partner.Property(x => x.Id).UseIdentityColumn();
        partner.Property(x => x.OrganizationId).IsRequired();
        partner.Property(x => x.Code).HasMaxLength(32).IsUnicode(false).UseCollation(CodeCollation).IsRequired();
        partner.Property(x => x.PersonType).HasConversion<byte>().IsRequired();
        partner.Property(x => x.Name).HasMaxLength(200).IsRequired();
        partner.Property(x => x.FirstName).HasMaxLength(100);
        partner.Property(x => x.LastName).HasMaxLength(100);
        partner.Property(x => x.NationalCode).HasMaxLength(20).IsUnicode(false);
        partner.Property(x => x.CompanyName).HasMaxLength(200);
        partner.Property(x => x.NationalLegalId).HasMaxLength(30).IsUnicode(false);
        partner.Property(x => x.EconomicCode).HasMaxLength(30).IsUnicode(false);
        partner.Property(x => x.Mobile).HasMaxLength(32).IsUnicode(false);
        partner.Property(x => x.Phone).HasMaxLength(32).IsUnicode(false);
        partner.Property(x => x.Email).HasMaxLength(256);
        partner.Property(x => x.Website).HasMaxLength(200);
        partner.Property(x => x.Country).HasMaxLength(100);
        partner.Property(x => x.Province).HasMaxLength(100);
        partner.Property(x => x.City).HasMaxLength(100);
        partner.Property(x => x.Address).HasMaxLength(500);
        partner.Property(x => x.PostalCode).HasMaxLength(20).IsUnicode(false);
        partner.Property(x => x.Notes).HasMaxLength(1000);
        partner.Property(x => x.IsActive).IsRequired();

        AddAuditMetadata(partner);

        partner.HasAlternateKey(x => new { x.OrganizationId, x.Id }).HasName("AK_BusinessPartner_Org_Id");
        partner.HasIndex(x => new { x.OrganizationId, x.Code }).IsUnique().HasDatabaseName("UX_BusinessPartner_Org_Code");
        partner.HasIndex(x => new { x.OrganizationId, x.IsActive, x.Code, x.Id }).HasDatabaseName("IX_BusinessPartner_Org_IsActive_Code");

        partner.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_BusinessPartner_Organization");

        partner.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.BusinessPartnerId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_BusinessPartnerRoleAssignment_BusinessPartner");
    }
}

internal sealed class BusinessPartnerRoleAssignmentConfiguration : IEntityTypeConfiguration<BusinessPartnerRoleAssignment>
{
    public void Configure(EntityTypeBuilder<BusinessPartnerRoleAssignment> builder)
    {
        builder.ToTable("BusinessPartnerRoleAssignment", Schema);
        builder.HasKey(x => new { x.BusinessPartnerId, x.RoleId }).HasName("PK_BusinessPartnerRoleAssignment");
        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.BusinessPartnerId).IsRequired();
        builder.Property(x => x.RoleId).IsRequired();

        builder.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_BusinessPartnerRoleAssignment_Organization");

        builder.HasOne(x => x.RoleDefinition).WithMany()
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_BusinessPartnerRoleAssignment_RoleDefinition");
    }
}
