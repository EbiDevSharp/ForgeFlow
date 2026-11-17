using ForgeFlow.Domain.Common;

namespace ForgeFlow.Application.Common;

/// <summary>
/// Defines a handler for a specific domain event.
/// Handlers execute deterministically during unit-of-work SaveChanges before final database commit.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
