namespace ForgeFlow.Application.Common;

/// <summary>
/// Resolves organization-scoped TimeZoneInfo, defaulting cleanly to the standard tenant time zone (Asia/Tehran).
/// </summary>
public interface IOrganizationTimeZoneProvider
{
    /// <summary>Resolves TimeZoneInfo for an organization (or standard default).</summary>
    TimeZoneInfo GetTimeZone(long organizationId = 0);

    /// <summary>Resolves canonical IANA or Windows TimeZone identifier for an organization (or standard default).</summary>
    string GetTimeZoneId(long organizationId = 0);
}
