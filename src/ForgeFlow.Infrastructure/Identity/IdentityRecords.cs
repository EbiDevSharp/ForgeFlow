using Microsoft.AspNetCore.Identity;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class ApplicationAccount : IdentityUser<long>
{
    public bool IsActive { get; internal set; } = true;
    public string? PreferredCulture { get; internal set; }
    public DateTime CreatedAtUtc { get; internal set; }
    public DateTime? UpdatedAtUtc { get; internal set; }
}

public static class FixedAccessRoles
{
    public const byte OrganizationAdministrator = 1;
    public const byte SiteOperator = 2;
    public const byte DeploymentProvisioner = 3;
}

public sealed class OrganizationMembership
{
    public long UserId { get; internal set; }
    public long OrganizationId { get; internal set; }
    public bool IsActive { get; internal set; }
}
public sealed class SiteMembership
{
    public long UserId { get; internal set; }
    public long OrganizationId { get; internal set; }
    public long SiteId { get; internal set; }
    public bool IsActive { get; internal set; }
}
public sealed class ScopedRoleAssignment
{
    public long Id { get; internal set; }
    public long UserId { get; internal set; }
    public byte RoleId { get; internal set; }
    public byte ScopeKind { get; internal set; }
    public long? OrganizationId { get; internal set; }
    public long? SiteId { get; internal set; }
    public bool IsActive { get; internal set; }
}
public sealed class AccountSession
{
    public Guid Id { get; internal set; }
    public long UserId { get; internal set; }
    public string SecurityStampSnapshot { get; internal set; } = null!;
    public DateTime IssuedAtUtc { get; internal set; }
    public DateTime ExpiresAtUtc { get; internal set; }
    public DateTime? RevokedAtUtc { get; internal set; }
    public string? RevokeReason { get; internal set; }
}
public sealed class BootstrapState
{
    public byte Id { get; internal set; }
    public byte State { get; internal set; }
    public Guid? OperationId { get; internal set; }
    public DateTime? CompletedAtUtc { get; internal set; }
    public long? FirstUserId { get; internal set; }
    public long? OrganizationId { get; internal set; }
    public long? SiteId { get; internal set; }
    public string? HostOperatorReference { get; internal set; }
}
public sealed class SecurityEvent
{
    public long Id { get; internal set; }
    public string EventCode { get; internal set; } = null!;
    public DateTime OccurredAtUtc { get; internal set; }
    public Guid CorrelationId { get; internal set; }
    public byte ActorKind { get; internal set; }
    public long? ActorUserId { get; internal set; }
    public long? SubjectUserId { get; internal set; }
    public Guid? SessionId { get; internal set; }
    public string? HostOperatorReference { get; internal set; }
    public long? OrganizationId { get; internal set; }
    public long? SiteId { get; internal set; }
    public byte Outcome { get; internal set; }
    public string DataJson { get; internal set; } = null!;
}
