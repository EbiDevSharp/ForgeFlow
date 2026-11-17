using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ForgeFlow.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Enforces actor attribution, timestamp stamping (CreatedAtUtc, UpdatedAtUtc),
/// delta change tracking, and post-commit audit logging flushes.
/// </summary>
public sealed class AuditingSaveChangesInterceptor(
    ICurrentActorAccessor? actorAccessor = null,
    TimeProvider? timeProvider = null)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not OrganizationDbContext orgDb || orgDb.IsSavingAuditBatch)
            return base.SavingChanges(eventData, result);

        PrepareAudit(orgDb);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not OrganizationDbContext orgDb || orgDb.IsSavingAuditBatch)
            return base.SavingChangesAsync(eventData, result, cancellationToken);

        PrepareAudit(orgDb);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is OrganizationDbContext orgDb && !orgDb.IsSavingAuditBatch)
            orgDb.FlushPendingAuditChangesSync();

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is OrganizationDbContext orgDb && !orgDb.IsSavingAuditBatch)
            await orgDb.FlushPendingAuditChangesAsync(cancellationToken);

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is OrganizationDbContext orgDb)
            orgDb.MarkFailed();

        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is OrganizationDbContext orgDb)
            orgDb.MarkFailed();

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void PrepareAudit(OrganizationDbContext orgDb)
    {
        orgDb.ChangeTracker.DetectChanges();
        var entries = orgDb.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        var actor = orgDb.CurrentActor ?? actorAccessor?.CurrentActor;
        if (orgDb.Failed || (entries.Length > 0 && actor is null))
            throw new InvalidOperationException(OrganizationAccessErrors.ActorRequired);

        if (entries.Length == 0)
            return;

        ArgumentNullException.ThrowIfNull(actor);

        // Pre-register any pending auditable domain event codes so change capture knows about them
        foreach (var entry in orgDb.ChangeTracker.Entries<ForgeFlow.Domain.Common.IHasDomainEvents>())
        {
            foreach (var evt in entry.Entity.DomainEvents.OfType<ForgeFlow.Domain.Common.IAuditableDomainEvent>())
            {
                orgDb.DispatchedDomainEventCodes.Add(evt.EventCode);
            }
        }

        var now = (timeProvider ?? orgDb.Clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var changes = OrganizationDbContext.CaptureAllChanges(entries, orgDb.DispatchedDomainEventCodes).ToArray();
        OrganizationDbContext.ApplyAuditMetadata(entries, actor, now);
        orgDb.PendingAudit = (actor, now, changes);
    }
}
