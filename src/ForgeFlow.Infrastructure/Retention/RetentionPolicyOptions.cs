namespace ForgeFlow.Infrastructure.Retention;

/// <summary>
/// Strongly-typed configuration options for periodic background data retention and cleanup.
/// </summary>
public sealed class RetentionPolicyOptions
{
    public const string SectionName = "RetentionPolicy";

    /// <summary>Whether the background retention cleanup service is enabled. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Interval between background retention cycles. Defaults to 1 hour.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Initial delay before the first cleanup cycle execution. Defaults to 30 seconds.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Retention period in days for database ErrorLog records. Defaults to 30 days (min 7 days per Domain rules).</summary>
    public int ErrorLogRetentionDays { get; set; } = 30;

    /// <summary>Retention period in days for fallback JSONL error log files. Defaults to 14 days.</summary>
    public int FallbackLogRetentionDays { get; set; } = 14;

    /// <summary>Threshold in days after expiration/revocation before an AccountSession row is purged. Defaults to 7 days.</summary>
    public int SessionEvictionThresholdDays { get; set; } = 7;
}
