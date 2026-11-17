using ForgeFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ForgeFlow.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Enforces immutability of security audit logs, completed bootstrap states, system roles,
/// and core identity credentials without overriding SaveChanges / SaveChangesAsync on AuthDbContext.
/// </summary>
public sealed class AuthImmutableHistorySaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is AuthDbContext authDb)
        {
            Guard(authDb.ChangeTracker);
        }

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AuthDbContext authDb)
        {
            Guard(authDb.ChangeTracker);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public static void Guard(ChangeTracker changeTracker)
    {
        changeTracker.DetectChanges();
        if (changeTracker.Entries().Any(e => e.State == EntityState.Deleted ||
            (e.Entity is SecurityEvent && e.State == EntityState.Modified) ||
            (e.Entity is BootstrapState && e.State == EntityState.Modified && (byte)e.OriginalValues[nameof(BootstrapState.State)]! == 1) ||
            (e.Entity is ApplicationAccount && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "UserName" or "NormalizedUserName" && ValueChanged(p))) ||
            (e.Entity is ScopedRoleAssignment && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "UserId" or "RoleId" or "ScopeKind" or "OrganizationId" or "SiteId" && ValueChanged(p))) ||
            (e.Entity is RoleDefinition && e.State == EntityState.Modified &&
                ((bool)e.OriginalValues[nameof(RoleDefinition.IsSystem)]!
                 || e.Properties.Any(p => p.Metadata.Name is "IsSystem" or "ScopeKind" or "OrganizationId" && ValueChanged(p)))) ||
            (e.Entity is RolePermission && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "RoleId" or "PermissionCode" && ValueChanged(p))) ||
            (e.Entity is UserPermissionGrant && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "UserId" or "ScopeKind" or "OrganizationId" or "SiteId" or "PermissionCode" && ValueChanged(p))) ||
            (e.Entity is UserPreference && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "UserId" or "CreatedAtUtc" && ValueChanged(p))) ||
            (e.Entity is UserNotificationPreference && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "UserId" or "EventCode" or "CreatedAtUtc" && ValueChanged(p))) ||
            (e.Entity is UserNotification && e.State == EntityState.Modified && e.Properties.Any(p => p.Metadata.Name is "UserId" or "Category" or "Severity" or "TemplateCode" or "ParamsJson" or "LinkUrl" or "CreatedAtUtc" && ValueChanged(p)))))
        {
            throw new InvalidOperationException("Auth.ImmutableHistory");
        }
    }

    private static bool ValueChanged(PropertyEntry property)
        => property.IsModified && !Equals(property.OriginalValue, property.CurrentValue);
}
