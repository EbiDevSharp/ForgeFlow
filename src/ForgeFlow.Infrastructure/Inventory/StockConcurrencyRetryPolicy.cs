using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Infrastructure.Inventory;

/// <summary>
/// Resilient retry policy for transient SQL Server concurrency contention, deadlocks, and optimistic concurrency token races
/// during inventory reservation, release, consumption/issue, transfer, and sales dispatch operations.
/// </summary>
public static class StockConcurrencyRetryPolicy
{
    public const int DefaultMaxRetries = 5;
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMilliseconds(25);
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Checks if the exception or any of its inner exceptions is a transient concurrency or lock conflict:
    /// - <see cref="DbUpdateConcurrencyException"/> (optimistic concurrency token mismatch)
    /// - <see cref="SqlException"/> error 1205 (deadlock victim)
    /// - <see cref="SqlException"/> error 1222 (lock request time out)
    /// - <see cref="SqlException"/> error 2601 or 2627 (unique constraint / index race collision on initial balance row creation)
    /// </summary>
    public static bool IsTransient(Exception ex)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is DbUpdateConcurrencyException)
                return true;

            if (current is SqlException sql && IsTransientSqlError(sql.Number))
                return true;

            current = current.InnerException;
        }

        return false;
    }

    /// <summary>
    /// Identifies transient SQL Server error numbers for deadlock victim (1205), lock timeout (1222),
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
