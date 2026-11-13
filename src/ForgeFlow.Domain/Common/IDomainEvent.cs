namespace ForgeFlow.Domain.Common;

/// <summary>Marker interface for immutable domain events raised by aggregate roots.</summary>
public interface IDomainEvent
{
    DateTime OccurredAtUtc { get; }
}

/// <summary>Domain event that carries structured audit trail payload for automatic OrganizationChange persistence.</summary>
public interface IAuditableDomainEvent : IDomainEvent
{
    long OrganizationId { get; }
    long? SiteId { get; }
    string EventCode { get; }
    string BuildAuditJson();
}

/// <summary>Aggregate root capability for buffering and clearing uncommitted domain events.</summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

/// <summary>Explicit optimistic concurrency token contract eliminating EF shadow-property magic strings.</summary>
public interface IHasRowVersion
{
    byte[] RowVersion { get; }
}
