using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ForgeFlow.Infrastructure.Identity.Configurations;

internal static class AuthModelConventions
{
    internal const string Schema = "auth";
    internal static readonly DateTime SystemSeedUtc = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
    internal static readonly ValueConverter<DateTime, DateTime> Utc = new(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    internal static void Version<T>(EntityTypeBuilder<T> entity) where T : class
        => entity.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();

    internal static void Access<T>(EntityTypeBuilder<T> entity, string name) where T : class
    {
        entity.ToTable(name, Schema, t => t.HasCheckConstraint("CK_" + name + "_Metadata", "[CreatedByActorId]>0 AND (([UpdatedAtUtc] IS NULL AND [UpdatedByActorId] IS NULL) OR ([UpdatedAtUtc] IS NOT NULL AND [UpdatedByActorId] IS NOT NULL AND [UpdatedByActorId]>0 AND [UpdatedAtUtc]>=[CreatedAtUtc]))"));
        entity.Property<DateTime>("CreatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        entity.Property<long>("CreatedByActorId");
        entity.Property<DateTime?>("UpdatedAtUtc").HasColumnType("datetime2(7)").HasConversion(Utc);
        entity.Property<long?>("UpdatedByActorId");
        Version(entity);
    }

    internal static object[] SystemRolePermissionSeeds()
    {
        var orgAdmin = new[]
        {
            OrganizationPermissions.OrganizationsRead, OrganizationPermissions.OrganizationsUpdate, OrganizationPermissions.OrganizationsSetActive,
            OrganizationPermissions.SitesRead, OrganizationPermissions.SitesCreate, OrganizationPermissions.SitesUpdate, OrganizationPermissions.SitesSetActive,
            UserPermissions.UsersRead, UserPermissions.UsersCreate, UserPermissions.UsersSetActive, UserPermissions.UsersUnlock, UserPermissions.UsersResetPassword,
            UserPermissions.MembershipsManage, UserPermissions.RolesAssignFixed,
            ForgeFlow.Application.Roles.RolePermissions.RolesRead,
            ForgeFlow.Application.Roles.RolePermissions.RolesManage,
            ForgeFlow.Application.Roles.RolePermissions.PermissionsAssign
        };
        var siteOperator = new[]
        {
            OrganizationPermissions.SitesRead, OrganizationPermissions.SitesUpdate, OrganizationPermissions.SitesSetActive
        };
        var provisioner = new[]
        {
            OrganizationPermissions.OrganizationsCreate, UserPermissions.UsersAttachOrganization, UserPermissions.UsersSetActive,
            UserPermissions.UsersUnlock, UserPermissions.UsersResetPassword
        };

        return orgAdmin.Select(c => Seed(FixedAccessRoles.OrganizationAdministrator, c))
            .Concat(siteOperator.Select(c => Seed(FixedAccessRoles.SiteOperator, c)))
            .Concat(provisioner.Select(c => Seed(FixedAccessRoles.DeploymentProvisioner, c)))
            .ToArray();

        static object Seed(long roleId, string code)
            => new { RoleId = roleId, PermissionCode = code, IsActive = true, CreatedAtUtc = SystemSeedUtc, CreatedByActorId = 0L };
    }
}
