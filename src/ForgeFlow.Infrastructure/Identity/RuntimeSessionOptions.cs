namespace ForgeFlow.Infrastructure.Identity;

/// <summary>
/// Configuration options for runtime session tracking and concurrency management.
/// </summary>
public sealed class RuntimeSessionOptions
{
    /// <summary>
    /// Maximum number of concurrent active sessions allowed per user. 0 means unlimited.
    /// </summary>
    public int MaxConcurrentSessionsPerUser { get; set; } = 0;

    /// <summary>
    /// If true, when the concurrent session limit is reached on sign-in, the oldest session(s)
    /// will be evicted/revoked cleanly. If false, the sign-in attempt is rejected with Auth.ConcurrentSessionLimitReached.
    /// </summary>
    public bool EvictOldestOnLimitReached { get; set; } = true;
}
