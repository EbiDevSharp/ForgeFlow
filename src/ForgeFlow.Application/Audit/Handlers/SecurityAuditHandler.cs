using ForgeFlow.Application.Common;
using ForgeFlow.Domain.Events;

namespace ForgeFlow.Application.Audit.Handlers;

/// <summary>
/// Single-responsibility audit handler for Security (Roles and Users) domain events.
/// Enqueues standardized, strongly-typed audit entries into the ambient transaction sink.
/// </summary>
public sealed class SecurityAuditHandler(IAuditTrailSinkAccessor sinkAccessor) :
    IDomainEventHandler<RolePermissionsUpdatedEvent>,
    IDomainEventHandler<UserAccessModifiedEvent>
{
    public Task HandleAsync(RolePermissionsUpdatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId,
            null,
            "RoleDefinitionChanged",
            EventJsonSerializer.Serialize(new
            {
                domainEvent.RoleId,
                domainEvent.RoleName,
                domainEvent.Permissions
            }));
        return Task.CompletedTask;
    }

    public Task HandleAsync(UserAccessModifiedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        sinkAccessor.CurrentSink?.Record(
            domainEvent.OrganizationId ?? 0,
            null,
            "User.Updated",
            EventJsonSerializer.Serialize(new
            {
                domainEvent.UserId,
                domainEvent.Action
            }));
        return Task.CompletedTask;
    }
}
