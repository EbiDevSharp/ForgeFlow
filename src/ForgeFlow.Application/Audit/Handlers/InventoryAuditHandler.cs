using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Events;

namespace ForgeFlow.Application.Audit.Handlers;

/// <summary>
/// Single-responsibility audit handler for Inventory domain events.
/// Enqueues standardized, strongly-typed audit entries into the ambient transaction sink.
/// </summary>
public sealed class InventoryAuditHandler(IAuditTrailSinkAccessor sinkAccessor) :
    IDomainEventHandler<StockAdjustedEvent>,
    IDomainEventHandler<StockReservedEvent>
{
    public Task HandleAsync(StockAdjustedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }

    public Task HandleAsync(StockReservedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }
}
