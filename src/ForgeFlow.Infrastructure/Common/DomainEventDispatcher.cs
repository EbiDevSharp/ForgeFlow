using System.Collections.Concurrent;
using System.Reflection;
using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Common;

/// <summary>
/// Deterministic in-memory domain event dispatcher resolving IDomainEventHandler{T} from DI.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider serviceProvider) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandlerMethods = new();

    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return DispatchInternalAsync(domainEvent, cancellationToken);
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);
        foreach (var evt in domainEvents)
        {
            await DispatchInternalAsync(evt, cancellationToken);
        }
    }

    private async Task DispatchInternalAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventType = domainEvent.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handlers = serviceProvider.GetServices(handlerType);

        var method = HandlerMethods.GetOrAdd(eventType, t =>
            typeof(IDomainEventHandler<>).MakeGenericType(t).GetMethod("HandleAsync", [t, typeof(CancellationToken)])!);

        foreach (var handler in handlers)
        {
            if (handler is null) continue;
            var task = (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
            await task;
        }
    }
}
