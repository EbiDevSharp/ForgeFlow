using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Infrastructure.Diagnostics;
using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class RetentionBackgroundServiceTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan delta) => current += delta;
    }

    private sealed class MockErrorLogStore : IErrorLogStore
    {
        public DateTime? LastPurgeCutoffUtc { get; private set; }
        public int PurgeCountToReturn { get; set; } = 5;
        public int FallbackPurgeCountToReturn { get; set; } = 3;
        public bool ThrowOnPurge { get; set; }

        public Task<OrganizationPage<ErrorLogSummary>> ListAsync(OrganizationScope scope, ErrorLogListQuery query, CancellationToken ct = default)
            => Task.FromResult(new OrganizationPage<ErrorLogSummary>([], 0, 1, 10));

        public Task<ErrorLogDetails?> GetAsync(OrganizationScope scope, long id, CancellationToken ct = default)
            => Task.FromResult<ErrorLogDetails?>(null);

        public Task<ErrorLogStats> GetStatsAsync(OrganizationScope scope, DateTime utcNow, CancellationToken ct = default)
            => Task.FromResult(new ErrorLogStats(0, 0, 0, 0));

        public Task<int> PurgeBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default)
        {
            if (ThrowOnPurge)
                throw new InvalidOperationException("Simulated transient DB error");

            LastPurgeCutoffUtc = cutoffUtc;
            return Task.FromResult(PurgeCountToReturn);
        }

        public int PurgeFallbackFilesBefore(DateTime cutoffUtc)
        {
            if (ThrowOnPurge)
                throw new InvalidOperationException("Simulated fallback IO error");

            return FallbackPurgeCountToReturn;
        }
    }

    [Fact]
    public void RetentionPolicyOptions_Binds_From_Configuration_Properly()
    {
        var configJson = """
        {
            "RetentionPolicy": {
                "Enabled": true,
                "CleanupInterval": "02:00:00",
                "InitialDelay": "00:01:00",
                "ErrorLogRetentionDays": 60,
                "FallbackLogRetentionDays": 20,
                "SessionEvictionThresholdDays": 14
            }
        }
        """;

        var builder = new ConfigurationBuilder();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(configJson));
        builder.AddJsonStream(stream);
        var configuration = builder.Build();

        var services = new ServiceCollection();
        services.AddRetentionServices(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<RetentionPolicyOptions>>().Value;
        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromHours(2), options.CleanupInterval);
        Assert.Equal(TimeSpan.FromMinutes(1), options.InitialDelay);
        Assert.Equal(60, options.ErrorLogRetentionDays);
        Assert.Equal(20, options.FallbackLogRetentionDays);
        Assert.Equal(14, options.SessionEvictionThresholdDays);
    }

    [Fact]
    public void PurgeFallbackFilesBefore_Deletes_Expired_Files_And_Preserves_Recent_Files()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ff_test_logs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create test log files
            var expiredFile1 = Path.Combine(tempDir, "errors-20261001.jsonl");
            var expiredFile2 = Path.Combine(tempDir, "errors-20261015.jsonl");
            var recentFile = Path.Combine(tempDir, "errors-20261115.jsonl");
            var nonMatchingFile = Path.Combine(tempDir, "other-log.txt");

            File.WriteAllText(expiredFile1, "{\"error\":\"expired1\"}");
            File.WriteAllText(expiredFile2, "{\"error\":\"expired2\"}");
            File.WriteAllText(recentFile, "{\"error\":\"recent\"}");
            File.WriteAllText(nonMatchingFile, "ignore me");

            var storageOptions = new DiagnosticsStorageOptions { FallbackLogDirectory = tempDir };

            var unavailableOptions = new DbContextOptionsBuilder<DiagnosticsDbContext>()
                .UseSqlServer("Server=127.0.0.1,1;Database=DoesNotExist;Connect Timeout=1;Encrypt=False")
                .Options;
            var factory = new FixedDiagnosticsFactory(unavailableOptions);

            var store = new SqlErrorLogStore(
                factory,
                new EmptyScopeFactory(),
                NullLogger<SqlErrorLogStore>.Instance,
                storageOptions);

            var cutoff = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            var deletedCount = store.PurgeFallbackFilesBefore(cutoff);

            Assert.Equal(2, deletedCount);
            Assert.False(File.Exists(expiredFile1));
            Assert.False(File.Exists(expiredFile2));
            Assert.True(File.Exists(recentFile));
            Assert.True(File.Exists(nonMatchingFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RetentionBackgroundService_RunCleanupCycleAsync_Executes_Correct_Cutoffs()
    {
        var baseTime = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(baseTime);

        var mockErrorStore = new MockErrorLogStore();

        var services = new ServiceCollection();
        services.AddSingleton<IErrorLogStore>(mockErrorStore);

        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new RetentionPolicyOptions
        {
            ErrorLogRetentionDays = 30,
            FallbackLogRetentionDays = 14,
            SessionEvictionThresholdDays = 7
        });

        var service = new RetentionBackgroundService(
            scopeFactory,
            options,
            NullLogger<RetentionBackgroundService>.Instance,
            clock);

        var result = await service.RunCleanupCycleAsync();

        Assert.Equal(5, result.PurgedDatabaseErrorLogs);
        Assert.Equal(3, result.PurgedFallbackLogFiles);
        Assert.NotNull(mockErrorStore.LastPurgeCutoffUtc);

        var expectedErrorLogCutoff = baseTime.UtcDateTime.AddDays(-30);
        Assert.Equal(expectedErrorLogCutoff, mockErrorStore.LastPurgeCutoffUtc.Value);
    }

    [Fact]
    public async Task RetentionBackgroundService_Catches_Transient_Exceptions_Without_Crashing()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var failingStore = new MockErrorLogStore { ThrowOnPurge = true };

        var services = new ServiceCollection();
        services.AddSingleton<IErrorLogStore>(failingStore);

        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new RetentionPolicyOptions());

        var service = new RetentionBackgroundService(
            scopeFactory,
            options,
            NullLogger<RetentionBackgroundService>.Instance,
            clock);

        // RunCleanupCycleAsync must complete gracefully without throwing
        var result = await service.RunCleanupCycleAsync();

        Assert.Equal(0, result.PurgedDatabaseErrorLogs);
        Assert.Equal(0, result.PurgedFallbackLogFiles);
    }

    [Fact]
    public async Task RetentionBackgroundService_Respects_Disabled_Flag()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new RetentionPolicyOptions
        {
            Enabled = false,
            InitialDelay = TimeSpan.FromHours(10)
        });

        var service = new RetentionBackgroundService(
            scopeFactory,
            options,
            NullLogger<RetentionBackgroundService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // StartAsync should return quickly because service is disabled
        await service.StartAsync(cts.Token);
        await service.StopAsync(cts.Token);
    }

    private sealed class FixedDiagnosticsFactory(DbContextOptions<DiagnosticsDbContext> options)
        : IDbContextFactory<DiagnosticsDbContext>
    {
        public DiagnosticsDbContext CreateDbContext() => new(options);
    }

    private sealed class EmptyScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new EmptyScope();
        private sealed class EmptyScope : IServiceScope
        {
            public IServiceProvider ServiceProvider => EmptyProvider.Instance;
            public void Dispose() { }
        }
        private sealed class EmptyProvider : IServiceProvider
        {
            public static readonly EmptyProvider Instance = new();
            public object? GetService(Type serviceType) => null;
        }
    }
}
