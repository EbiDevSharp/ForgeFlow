using ForgeFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Identity.Configurations.AuthModelConventions;

namespace ForgeFlow.Infrastructure.Identity.Configurations;

internal sealed class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> membership)
    {
        Access(membership, "OrganizationMembership");
        membership.HasKey(m => new { m.UserId, m.OrganizationId });
        membership.HasOne<ApplicationAccount>().WithMany().HasForeignKey(m => m.UserId);
        membership.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId);
        membership.HasIndex(m => new { m.OrganizationId, m.IsActive, m.UserId });
    }
}

internal sealed class SiteMembershipConfiguration : IEntityTypeConfiguration<SiteMembership>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<SiteMembership> siteMembership)
    {
        Access(siteMembership, "SiteMembership");
        siteMembership.HasKey(m => new { m.UserId, m.OrganizationId, m.SiteId });
        siteMembership.HasOne<OrganizationMembership>().WithMany().HasForeignKey(m => new { m.UserId, m.OrganizationId });
        siteMembership.HasOne<Site>().WithMany().HasForeignKey(m => new { m.OrganizationId, m.SiteId }).HasPrincipalKey(s => new { s.OrganizationId, s.Id });
        siteMembership.HasIndex(m => new { m.OrganizationId, m.SiteId, m.IsActive, m.UserId });
    }
}
