using ForgeFlow.Domain.Common;

namespace ForgeFlow.Application.Common;

/// <summary>
/// Dispatches domain events deterministically to registered IDomainEventHandler instances.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
