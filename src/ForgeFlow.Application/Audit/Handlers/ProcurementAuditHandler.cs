using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Events;

namespace ForgeFlow.Application.Audit.Handlers;

/// <summary>
/// Single-responsibility audit handler for Purchase Order domain events.
/// Enqueues standardized, strongly-typed audit entries into the ambient transaction sink.
/// </summary>
public sealed class ProcurementAuditHandler(IAuditTrailSinkAccessor sinkAccessor) :
    IDomainEventHandler<PurchaseOrderApprovedEvent>,
    IDomainEventHandler<PurchaseOrderClosedEvent>,
    IDomainEventHandler<PurchaseOrderCancelledEvent>
{
    public Task HandleAsync(PurchaseOrderApprovedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderClosedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }

    public Task HandleAsync(PurchaseOrderCancelledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }
}
