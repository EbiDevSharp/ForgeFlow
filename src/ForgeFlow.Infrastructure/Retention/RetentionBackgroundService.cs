using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Infrastructure.Diagnostics;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeFlow.Infrastructure.Retention;

public sealed record RetentionCycleResult(
    int PurgedDatabaseErrorLogs,
    int PurgedFallbackLogFiles,
    int PurgedExpiredSessions);

/// <summary>
/// Resilient background service that periodically purges expired ErrorLogs (in database and fallback files)
/// and stale user sessions to prevent unbounded storage and memory growth.
/// </summary>
public sealed class RetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<RetentionPolicyOptions> options,
    ILogger<RetentionBackgroundService> logger,
    TimeProvider? timeProvider = null,
    IOrganizationClock? organizationClock = null) : BackgroundService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (!config.Enabled)
        {
            logger.LogInformation("RetentionBackgroundService is disabled by configuration.");
            return;
        }

        if (config.InitialDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(config.InitialDelay, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        // Run initial cycle
        try
        {
            await RunCleanupCycleAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transient failure during initial retention cleanup cycle; will retry on next tick.");
        }

        var interval = config.CleanupInterval >= TimeSpan.FromMinutes(1)
            ? config.CleanupInterval
            : TimeSpan.FromMinutes(1);

        using var timer = new PeriodicTimer(interval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;

                await RunCleanupCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transient failure during periodic retention cleanup cycle; continuing background loop.");
            }
        }
    }

    /// <summary>
    /// Executes a single retention purge cycle across database error logs, fallback error log files, and expired sessions.
    /// </summary>
    public async Task<RetentionCycleResult> RunCleanupCycleAsync(CancellationToken ct = default)
    {
        var config = options.Value;
        var effectiveErrorLogDays = Math.Clamp(config.ErrorLogRetentionDays, 7, 3650);
        var effectiveFallbackDays = Math.Max(1, config.FallbackLogRetentionDays);
        var effectiveSessionDays = Math.Max(1, config.SessionEvictionThresholdDays);

        var now = (organizationClock?.UtcNow ?? clock.GetUtcNow().UtcDateTime);
        var errorLogCutoff = now.AddDays(-effectiveErrorLogDays);
        var fallbackLogCutoff = now.AddDays(-effectiveFallbackDays);
        var sessionCutoff = now.AddDays(-effectiveSessionDays);

        int deletedDbLogs = 0;
        int deletedFallbackFiles = 0;
        int deletedSessions = 0;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var errorStore = scope.ServiceProvider.GetService<IErrorLogStore>();
            if (errorStore is not null)
            {
                try
                {
                    deletedDbLogs = await errorStore.PurgeBeforeAsync(errorLogCutoff, ct);
                    deletedFallbackFiles = errorStore.PurgeFallbackFilesBefore(fallbackLogCutoff);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Error occurred while purging error logs during retention cycle.");
                }
            }

            var runtimeSessions = scope.ServiceProvider.GetService<RuntimeSessions>();
            if (runtimeSessions is not null)
            {
                try
                {
                    deletedSessions = await runtimeSessions.PurgeExpiredSessionsAsync(sessionCutoff, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Error occurred while purging expired sessions during retention cycle.");
                }
            }
        }

        if (deletedDbLogs > 0 || deletedFallbackFiles > 0 || deletedSessions > 0)
        {
            logger.LogInformation(
                "Retention cleanup completed: {DeletedLogs} error logs, {DeletedFallback} fallback log files, {DeletedSessions} expired sessions purged.",
                deletedDbLogs, deletedFallbackFiles, deletedSessions);
        }

        return new RetentionCycleResult(deletedDbLogs, deletedFallbackFiles, deletedSessions);
    }
}
