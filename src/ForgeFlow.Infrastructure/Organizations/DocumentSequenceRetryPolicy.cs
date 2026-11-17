using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Infrastructure.Organizations;

/// <summary>
/// Resilient retry policy for SQL Server deadlocks and transient concurrency contention during document sequence allocation.
/// </summary>
public static class DocumentSequenceRetryPolicy
{
    public const int DefaultMaxRetries = 5;
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMilliseconds(25);
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Checks if the exception or any of its inner exceptions is a transient SQL Server concurrency or lock conflict
    /// (1205 Deadlock, 1222 Lock Timeout, or 2601/2627 Unique Constraint race on initial row creation).
    /// </summary>
    public static bool IsTransient(Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is SqlException sql && IsTransientSqlError(sql.Number))
                return true;
            current = current.InnerException;
        }
        return false;
    }

    /// <summary>
    /// Identifies transient SQL Server error numbers for deadlock victim (1205), lock request timeout (1222),
    /// and unique index/constraint race collisions (2601, 2627).
    /// </summary>
    public static bool IsTransientSqlError(int number) => number is 1205 or 1222 or 2601 or 2627;

    /// <summary>
    /// Calculates bounded, jittered exponential backoff delay for the given retry attempt.
    /// </summary>
    public static TimeSpan CalculateDelay(int attempt, TimeSpan? initialDelay = null, TimeSpan? maxDelay = null)
    {
        var baseMs = (initialDelay ?? DefaultInitialDelay).TotalMilliseconds;
        var maxMs = (maxDelay ?? DefaultMaxDelay).TotalMilliseconds;
        var exponentialMs = baseMs * Math.Pow(2, attempt);
        var cappedMs = Math.Min(exponentialMs, maxMs);
        var jitter = Random.Shared.NextDouble() * 0.5 * cappedMs;
        return TimeSpan.FromMilliseconds(cappedMs + jitter);
    }
}
