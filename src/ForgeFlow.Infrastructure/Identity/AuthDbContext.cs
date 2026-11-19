using System.Runtime.CompilerServices;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Identity.Configurations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class AuthDbContext(
    DbContextOptions<AuthDbContext> options,
    IPersonalDataProtector? personalDataProtector = null) : IdentityUserContext<ApplicationAccount, long>(options)
{
    internal IPersonalDataProtector? PersonalDataProtector => personalDataProtector;
    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionGrant> UserPermissionGrants => Set<UserPermissionGrant>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<SiteMembership> SiteMemberships => Set<SiteMembership>();
    public DbSet<ScopedRoleAssignment> ScopedRoleAssignments => Set<ScopedRoleAssignment>();
    public DbSet<AccountSession> Sessions => Set<AccountSession>();
    public DbSet<BootstrapState> BootstrapStates => Set<BootstrapState>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<UserNotificationPreference> UserNotificationPreferences => Set<UserNotificationPreference>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<UserPasskey> Passkeys => Set<UserPasskey>();

    // Approved password-only support shape; no passkey tables/features in this increment.
    protected override Version SchemaVersion => IdentitySchemaVersions.Version1;

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.HasDefaultSchema(AuthModelConventions.Schema);
        model.ApplyConfigurationsFromAssembly(
            typeof(AuthDbContext).Assembly,
            t => typeof(IAuthEntityConfiguration).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var fk in model.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            fk.DeleteBehavior = DeleteBehavior.NoAction;
        }

        if (personalDataProtector is not null)
        {
            var tokenConverter = new ValueConverter<string?, string?>(
                v => v == null ? null : personalDataProtector.Protect(v),
                v => v == null ? null : personalDataProtector.Unprotect(v));
            model.Entity<IdentityUserToken<long>>()
                .Property(t => t.Value)
                .HasConversion(tokenConverter);
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, AuthModelCacheKeyFactory>();
        var coreExt = optionsBuilder.Options.FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>();
        if (coreExt?.Interceptors?.OfType<ForgeFlow.Infrastructure.Persistence.Interceptors.AuthImmutableHistorySaveChangesInterceptor>().Any() != true)
            optionsBuilder.AddInterceptors(new ForgeFlow.Infrastructure.Persistence.Interceptors.AuthImmutableHistorySaveChangesInterceptor());
    }

    internal void Guard() => ForgeFlow.Infrastructure.Persistence.Interceptors.AuthImmutableHistorySaveChangesInterceptor.Guard(ChangeTracker);

    private sealed class AuthModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
        {
            var protector = (context as AuthDbContext)?.PersonalDataProtector;
            var protectorKey = protector is null ? 0 : RuntimeHelpers.GetHashCode(protector);
            return (context.GetType(), designTime, protectorKey);
        }
    }
}
