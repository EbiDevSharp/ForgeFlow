using ForgeFlow.Application.Common;
using ForgeFlow.Application.Users;
using ForgeFlow.Domain.Common;

namespace ForgeFlow.Infrastructure.Common;

/// <summary>
/// Default implementation of IOrganizationTimeZoneProvider that resolves the system default time zone (Asia/Tehran)
/// or a configured tenant time zone.
/// </summary>
public sealed class DefaultOrganizationTimeZoneProvider : IOrganizationTimeZoneProvider
{
    private readonly TimeZoneInfo defaultZone;
    private readonly string defaultZoneId;

    public DefaultOrganizationTimeZoneProvider(string defaultTimeZoneId = UserPreferenceCatalog.DefaultTimeZoneId)
    {
        defaultZone = UserPreferenceCatalog.ResolveTimeZoneOrDefault(defaultTimeZoneId);
        defaultZoneId = UserPreferenceCatalog.NormalizeTimeZoneId(defaultTimeZoneId) ?? UserPreferenceCatalog.DefaultTimeZoneId;
    }

    public TimeZoneInfo GetTimeZone(long organizationId = 0) => defaultZone;

    public string GetTimeZoneId(long organizationId = 0) => defaultZoneId;
}
