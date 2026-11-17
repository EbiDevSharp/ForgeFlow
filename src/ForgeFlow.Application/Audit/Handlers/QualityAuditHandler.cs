using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Events;

namespace ForgeFlow.Application.Audit.Handlers;

/// <summary>
/// Single-responsibility audit handler for Quality domain events.
/// Enqueues standardized, strongly-typed audit entries into the ambient transaction sink.
/// </summary>
public sealed class QualityAuditHandler(IAuditTrailSinkAccessor sinkAccessor) :
    IDomainEventHandler<QualityInspectionRecordedEvent>,
    IDomainEventHandler<LotCoaIssuedEvent>
{
    public Task HandleAsync(QualityInspectionRecordedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }

    public Task HandleAsync(LotCoaIssuedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            domainEvent.SiteId,
            domainEvent.EventCode,
            domainEvent.BuildAuditJson());
        return Task.CompletedTask;
    }
}
