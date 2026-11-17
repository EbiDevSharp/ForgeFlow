namespace ForgeFlow.Domain.Common;

/// <summary>Marker interface for immutable domain events raised by aggregate roots.</summary>
public interface IDomainEvent
{
    DateTime OccurredOnUtc => OccurredAtUtc;
    DateTime OccurredAtUtc => OccurredOnUtc;
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
    void RaiseDomainEvent(IDomainEvent domainEvent);
    void ClearDomainEvents();
}

/// <summary>Base AggregateRoot implementation providing in-memory domain event buffering.</summary>
public abstract class AggregateRoot : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>Base Entity alias for aggregate roots.</summary>
public abstract class Entity : AggregateRoot
{
}

/// <summary>Explicit optimistic concurrency token contract eliminating EF shadow-property magic strings.</summary>
public interface IHasRowVersion
{
    byte[] RowVersion { get; }
}
