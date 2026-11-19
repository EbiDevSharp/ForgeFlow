namespace ForgeFlow.Web.Hosting;

/// <summary>
/// Policy names for rate-limited endpoints across ForgeFlow.Web.
/// </summary>
public static class RateLimitingPolicyNames
{
    /// <summary>Sliding-window policy for sensitive authentication endpoints (login, 2FA, passkey, password change).</summary>
    public const string Auth = "auth-login";

    /// <summary>Policy for resource-heavy CSV/PDF export endpoints.</summary>
    public const string Export = "export-policy";
}
