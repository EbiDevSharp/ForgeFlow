using ForgeFlow.Application.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ForgeFlow.Infrastructure.Organizations;

/// <summary>
/// Enforces trusted-actor attribution, metadata stamping, and OrganizationChange audit capture
/// on every SaveChanges / SaveChangesAsync call without overriding DbContext.SaveChanges (LSP-safe).
/// </summary>
public sealed class OrganizationAuditSaveChangesInterceptor(ICurrentActorAccessor? actorAccessor = null)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not OrganizationDbContext orgDb || orgDb.IsSavingAuditBatch)
            return base.SavingChanges(eventData, result);

        var actor = orgDb.CurrentActor ?? actorAccessor?.CurrentActor;
        orgDb.PrepareAttributedSaveSync(actor);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not OrganizationDbContext orgDb || orgDb.IsSavingAuditBatch)
            return await base.SavingChangesAsync(eventData, result, cancellationToken);

        var actor = orgDb.CurrentActor ?? actorAccessor?.CurrentActor;
        await orgDb.PrepareAttributedSaveAsync(actor, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
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
}
