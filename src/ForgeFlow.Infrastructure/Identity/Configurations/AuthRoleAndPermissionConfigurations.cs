using ForgeFlow.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static ForgeFlow.Infrastructure.Identity.Configurations.AuthModelConventions;

namespace ForgeFlow.Infrastructure.Identity.Configurations;

internal sealed class RoleDefinitionConfiguration : IEntityTypeConfiguration<RoleDefinition>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<RoleDefinition> roleDef)
    {
        roleDef.ToTable("RoleDefinition", Schema, t =>
        {
            t.HasCheckConstraint("CK_RoleDefinition_Invariant", "([IsSystem]=1 AND [OrganizationId] IS NULL AND [Id] IN (1,2,3) AND [IsActive]=1) OR ([IsSystem]=0 AND [OrganizationId] IS NOT NULL AND [Id] >= 100 AND [ScopeKind] IN (1,2))");
            t.HasCheckConstraint("CK_RoleDefinition_Metadata", "(([IsSystem]=1 AND [CreatedByActorId]=0) OR ([IsSystem]=0 AND [CreatedByActorId]>0)) AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
        });
        roleDef.HasKey(r => r.Id);
        roleDef.Property(r => r.Id).UseIdentityColumn(seed: 100, increment: 1);
        roleDef.Property(r => r.Name).HasMaxLength(100).IsRequired();
        roleDef.Property(r => r.NormalizedName).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2").IsRequired();
        roleDef.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId);
        roleDef.HasIndex(r => new { r.OrganizationId, r.NormalizedName }).IsUnique().HasFilter("[IsSystem] = 0").HasDatabaseName("UX_RoleDefinition_Org_Name");
        roleDef.HasIndex(r => new { r.OrganizationId, r.IsActive, r.ScopeKind });
        roleDef.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        roleDef.Property<long>("CreatedByActorId");
        roleDef.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        roleDef.Property<long?>("UpdatedByActorId");
        Version(roleDef);
        roleDef.HasData(
            new { Id = FixedAccessRoles.OrganizationAdministrator, Name = "OrganizationAdministrator", NormalizedName = "ORGANIZATIONADMINISTRATOR", IsSystem = true, ScopeKind = (byte)1, OrganizationId = (long?)null, IsActive = true, CreatedAtUtc = SystemSeedUtc, CreatedByActorId = 0L },
            new { Id = FixedAccessRoles.SiteOperator, Name = "SiteOperator", NormalizedName = "SITEOPERATOR", IsSystem = true, ScopeKind = (byte)2, OrganizationId = (long?)null, IsActive = true, CreatedAtUtc = SystemSeedUtc, CreatedByActorId = 0L },
            new { Id = FixedAccessRoles.DeploymentProvisioner, Name = "DeploymentProvisioner", NormalizedName = "DEPLOYMENTPROVISIONER", IsSystem = true, ScopeKind = (byte)0, OrganizationId = (long?)null, IsActive = true, CreatedAtUtc = SystemSeedUtc, CreatedByActorId = 0L });
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<RolePermission> rolePerm)
    {
        rolePerm.ToTable("RolePermission", Schema, t =>
        {
            t.HasCheckConstraint("CK_RolePermission_Code", "LEN([PermissionCode]) > 0 AND DATALENGTH([PermissionCode]) = LEN([PermissionCode])");
            t.HasCheckConstraint("CK_RolePermission_Metadata", "[CreatedByActorId] >= 0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))");
        });
        rolePerm.HasKey(p => new { p.RoleId, p.PermissionCode });
        rolePerm.Property(p => p.PermissionCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        rolePerm.HasOne<RoleDefinition>().WithMany().HasForeignKey(p => p.RoleId);
        rolePerm.HasIndex(p => new { p.PermissionCode, p.IsActive, p.RoleId });
        rolePerm.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        rolePerm.Property<long>("CreatedByActorId");
        rolePerm.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        rolePerm.Property<long?>("UpdatedByActorId");
        Version(rolePerm);
        rolePerm.HasData(SystemRolePermissionSeeds());
    }
}

internal sealed class ScopedRoleAssignmentConfiguration : IEntityTypeConfiguration<ScopedRoleAssignment>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<ScopedRoleAssignment> role)
    {
        Access(role, "ScopedRoleAssignment");
        role.HasKey(r => r.Id);
        role.Property(r => r.Id).UseIdentityColumn();
        role.ToTable("ScopedRoleAssignment", Schema, t => t.HasCheckConstraint(
            "CK_Role_Scope",
            "([RoleId]=3 AND [ScopeKind]=0 AND [OrganizationId] IS NULL AND [SiteId] IS NULL) OR ([RoleId]=1 AND [ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([RoleId]=2 AND [ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL) OR ([RoleId]>=100 AND (([ScopeKind]=1 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NULL) OR ([ScopeKind]=2 AND [OrganizationId] IS NOT NULL AND [SiteId] IS NOT NULL)))"));
        role.HasOne<ApplicationAccount>().WithMany().HasForeignKey(r => r.UserId);
        role.HasOne<RoleDefinition>().WithMany().HasForeignKey(r => r.RoleId);
        role.HasOne<OrganizationMembership>().WithMany().HasForeignKey(r => new { r.UserId, r.OrganizationId });
        role.HasOne<SiteMembership>().WithMany().HasForeignKey(r => new { r.UserId, r.OrganizationId, r.SiteId });
        role.HasIndex(r => new { r.UserId, r.RoleId }).IsUnique().HasFilter("[ScopeKind] = 0");
        role.HasIndex(r => new { r.UserId, r.RoleId, r.OrganizationId }).IsUnique().HasFilter("[ScopeKind] = 1");
        role.HasIndex(r => new { r.UserId, r.RoleId, r.OrganizationId, r.SiteId }).IsUnique().HasFilter("[ScopeKind] = 2");
        role.HasIndex(r => new { r.UserId, r.IsActive, r.ScopeKind, r.OrganizationId, r.SiteId });
    }
}

internal sealed class UserPermissionGrantConfiguration : IEntityTypeConfiguration<UserPermissionGrant>, IAuthEntityConfiguration
{
    public void Configure(EntityTypeBuilder<UserPermissionGrant> grant)
    {
        Access(grant, "UserPermissionGrant");
        grant.HasKey(g => g.Id);
        grant.Property(g => g.Id).UseIdentityColumn();
        grant.Property(g => g.PermissionCode).HasMaxLength(64).IsUnicode(false).IsRequired();
        grant.ToTable("UserPermissionGrant", Schema, t => t.HasCheckConstraint(
            "CK_UserPermissionGrant_Scope",
            "LEN([PermissionCode]) > 0 AND DATALENGTH([PermissionCode]) = LEN([PermissionCode]) AND (([ScopeKind]=1 AND [SiteId] IS NULL) OR ([ScopeKind]=2 AND [SiteId] IS NOT NULL))"));
        grant.HasOne<ApplicationAccount>().WithMany().HasForeignKey(g => g.UserId);
        grant.HasOne<OrganizationMembership>().WithMany().HasForeignKey(g => new { g.UserId, g.OrganizationId });
        grant.HasOne<SiteMembership>().WithMany().HasForeignKey(g => new { g.UserId, g.OrganizationId, g.SiteId });
        grant.HasIndex(g => new { g.UserId, g.OrganizationId, g.PermissionCode }).IsUnique().HasFilter("[ScopeKind] = 1");
        grant.HasIndex(g => new { g.UserId, g.OrganizationId, g.SiteId, g.PermissionCode }).IsUnique().HasFilter("[ScopeKind] = 2");
        grant.HasIndex(g => new { g.UserId, g.IsActive, g.ScopeKind, g.OrganizationId, g.SiteId, g.PermissionCode });
    }
}
