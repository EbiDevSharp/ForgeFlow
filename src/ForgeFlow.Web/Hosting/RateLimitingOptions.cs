namespace ForgeFlow.Web.Hosting;

/// <summary>
/// Strongly-typed configuration options for ASP.NET Core rate limiting.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Whether rate limiting is enabled. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Authentication policy options.</summary>
    public AuthRateLimiterOptions Auth { get; set; } = new();

    /// <summary>Export policy options.</summary>
    public ExportRateLimiterOptions Export { get; set; } = new();
}

public sealed class AuthRateLimiterOptions
{
    public int PermitLimit { get; set; } = 15;
    public int WindowSeconds { get; set; } = 60;
    public int SegmentsPerWindow { get; set; } = 6;
    public int QueueLimit { get; set; } = 0;
}

public sealed class ExportRateLimiterOptions
{
    public int PermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; } = 0;
}
