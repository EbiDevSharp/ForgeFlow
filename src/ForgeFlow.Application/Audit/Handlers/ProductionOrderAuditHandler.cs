using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Events;

namespace ForgeFlow.Application.Audit.Handlers;

/// <summary>
/// Single-responsibility audit handler for Production Order domain events.
/// Enqueues standardized, strongly-typed audit entries into the ambient transaction sink.
/// </summary>
public sealed class ProductionOrderAuditHandler(IAuditTrailSinkAccessor sinkAccessor) :
    IDomainEventHandler<ProductionOrderReleasedEvent>,
    IDomainEventHandler<ProductionOrderCompletedEvent>,
    IDomainEventHandler<ProductionOrderStartedEvent>,
    IDomainEventHandler<ProductionOrderCancelledEvent>
{
    public Task HandleAsync(ProductionOrderReleasedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }

    public Task HandleAsync(ProductionOrderCompletedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }

    public Task HandleAsync(ProductionOrderStartedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }

    public Task HandleAsync(ProductionOrderCancelledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }
}
