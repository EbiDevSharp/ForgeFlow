using Microsoft.AspNetCore.Identity;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class ApplicationAccount : IdentityUser<long>
{
    public bool IsActive { get; internal set; } = true;
    public string? PreferredCulture { get; internal set; }
    public string? DisplayName { get; internal set; }
    public string? AvatarContentType { get; internal set; }
    public byte[]? AvatarData { get; internal set; }
    public DateTime CreatedAtUtc { get; internal set; }
    public DateTime? UpdatedAtUtc { get; internal set; }
    public DateTime? PasswordChangedAtUtc { get; internal set; }
    public DateTime? TwoFactorEnabledAtUtc { get; internal set; }
    public bool RequireTwoFactorSetup { get; internal set; }
    public bool RequirePasswordChange { get; internal set; }
}

public sealed class UserPreference
{
    public long UserId { get; internal set; }
    public string Language { get; internal set; } = null!;
    public string Theme { get; internal set; } = null!;
    public string CalendarSystem { get; internal set; } = null!;
    public string TimeZoneId { get; internal set; } = null!;
    public long? ActiveOrganizationId { get; internal set; }
    public long? ActiveSiteId { get; internal set; }
    public int? IdleTimeoutMinutes { get; internal set; }
    public DateTime? TwoFactorReminderSnoozedUntilUtc { get; internal set; }
    public DateTime? TwoFactorOneTimeNotifiedAtUtc { get; internal set; }
    public DateTime CreatedAtUtc { get; internal set; }
    public DateTime? UpdatedAtUtc { get; internal set; }
}

public sealed class UserNotificationPreference
{
    public long UserId { get; internal set; }
    public string EventCode { get; internal set; } = null!;
    public bool InAppEnabled { get; internal set; }
    public bool EmailEnabled { get; internal set; }
    public DateTime CreatedAtUtc { get; internal set; }
    public DateTime? UpdatedAtUtc { get; internal set; }
}

public sealed class UserNotification
{
    public long Id { get; internal set; }
    public long UserId { get; internal set; }
    public byte Category { get; internal set; }
    public byte Severity { get; internal set; }
    public string TemplateCode { get; internal set; } = null!;
    public string ParamsJson { get; internal set; } = null!;
    public string? LinkUrl { get; internal set; }
    public DateTime CreatedAtUtc { get; internal set; }
    public DateTime? ReadAtUtc { get; internal set; }
}

public sealed class UserPasskey
{
    public long Id { get; internal set; }
    public long UserId { get; internal set; }
    public byte[] CredentialId { get; internal set; } = null!;
    public byte[] PublicKeyCose { get; internal set; } = null!;
    public int Algorithm { get; internal set; }
    public long SignCount { get; internal set; }
    public string DisplayName { get; internal set; } = null!;
    public DateTime CreatedAtUtc { get; internal set; }
    public DateTime? LastUsedAtUtc { get; internal set; }
    public DateTime? RevokedAtUtc { get; internal set; }
}

public static class FixedAccessRoles
{
    public const long OrganizationAdministrator = 1;
    public const long SiteOperator = 2;
    public const long DeploymentProvisioner = 3;
}

public sealed class RoleDefinition
{
    public long Id { get; internal set; }
    public string Name { get; internal set; } = null!;
    public string NormalizedName { get; internal set; } = null!;
    public bool IsSystem { get; internal set; }
    public byte ScopeKind { get; internal set; }
    public long? OrganizationId { get; internal set; }
    public bool IsActive { get; internal set; }
}

public sealed class RolePermission
{
    public long RoleId { get; internal set; }
    public string PermissionCode { get; internal set; } = null!;
    public bool IsActive { get; internal set; }
}

public sealed class UserPermissionGrant
{
    public long Id { get; internal set; }
    public long UserId { get; internal set; }
    public byte ScopeKind { get; internal set; }
    public long OrganizationId { get; internal set; }
    public long? SiteId { get; internal set; }
    public string PermissionCode { get; internal set; } = null!;
    public bool IsActive { get; internal set; }
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
    public long RoleId { get; internal set; }
    public byte ScopeKind { get; internal set; }
    public long? OrganizationId { get; internal set; }
    public long? SiteId { get; internal set; }
    public bool IsActive { get; internal set; }
}
public sealed class AccountSession : ForgeFlow.Domain.Common.IHasRowVersion
{
    public Guid Id { get; internal set; }
    public long UserId { get; internal set; }
    public string SecurityStampSnapshot { get; internal set; } = null!;
    public string? IpAddress { get; internal set; }
    public string? DeviceSummary { get; internal set; }
    public string? DeviceTokenHash { get; internal set; }
    public bool IsMobileDevice { get; internal set; }
    public DateTime IssuedAtUtc { get; internal set; }
    public DateTime LastActivityAtUtc { get; internal set; }
    public DateTime ExpiresAtUtc { get; internal set; }
    public DateTime? RevokedAtUtc { get; internal set; }
    public string? RevokeReason { get; internal set; }
    public byte[] RowVersion { get; internal set; } = [];
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
