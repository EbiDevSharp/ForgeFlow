using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ForgeFlow.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Enforces multi-tenant isolation guardrails (FFA-010), entity mutation rules,
/// delete restrictions, and parent organization/site locking before persisting changes.
/// </summary>
public sealed class TenantEnforcementSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not OrganizationDbContext orgDb || orgDb.IsSavingAuditBatch)
            return base.SavingChanges(eventData, result);

        ValidateEntriesAndTenancy(orgDb);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not OrganizationDbContext orgDb || orgDb.IsSavingAuditBatch)
            return await base.SavingChangesAsync(eventData, result, cancellationToken);

        var entries = ValidateEntriesAndTenancy(orgDb);
        if (entries.Length > 0)
        {
            await orgDb.LockAndValidateParents(entries, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static EntityEntry[] ValidateEntriesAndTenancy(OrganizationDbContext orgDb)
    {
        orgDb.ChangeTracker.DetectChanges();
        var entries = orgDb.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        if (entries.Length == 0)
            return entries;

        OrganizationDbContext.ValidateEntries(entries);

        // Strict Multi-Tenant Isolation Enforcement (Guardrail FFA-010)
        foreach (var entry in entries)
        {
            var entityOrgId = OrganizationDbContext.ResolveEntityOrganizationId(entry.Entity);

            // New Organization entities receive their ID upon SQL Server insert identity generation.
            if (entry.Entity is Organization && entry.State == EntityState.Added)
                continue;

            if (entityOrgId <= 0)
                throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);

            if (orgDb.CurrentOrganizationId.HasValue && entityOrgId != orgDb.CurrentOrganizationId.Value)
                throw new InvalidOperationException(OrganizationAccessErrors.AccessDenied);
        }

        return entries;
    }
}
