using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ForgeFlow.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Collects domain events from aggregate roots implementing IHasDomainEvents,
/// dispatches them through IDomainEventDispatcher within an ambient audit sink scope,
/// and clears events before database persistence.
/// </summary>
public sealed class DomainEventsSaveChangesInterceptor(
    IDomainEventDispatcher? dispatcher = null,
    IAuditTrailSinkAccessor? sinkAccessor = null,
    ICurrentActorAccessor? actorAccessor = null)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not OrganizationDbContext orgDb || orgDb.IsSavingAuditBatch)
            return base.SavingChanges(eventData, result);

        var actor = orgDb.CurrentActor ?? actorAccessor?.CurrentActor;
        orgDb.DispatchDomainEventsSync(dispatcher, sinkAccessor, actor);
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
        await orgDb.DispatchDomainEventsAsync(dispatcher, sinkAccessor, actor, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
