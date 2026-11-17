using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Events;

namespace ForgeFlow.Application.Audit.Handlers;

/// <summary>
/// Single-responsibility audit handler for Sales Order domain events.
/// Enqueues standardized, strongly-typed audit entries into the ambient transaction sink.
/// </summary>
public sealed class SalesOrderAuditHandler(IAuditTrailSinkAccessor sinkAccessor) :
    IDomainEventHandler<SalesOrderApprovedEvent>,
    IDomainEventHandler<SalesOrderDispatchedEvent>
{
    public Task HandleAsync(SalesOrderApprovedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }

    public Task HandleAsync(SalesOrderDispatchedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }
}
