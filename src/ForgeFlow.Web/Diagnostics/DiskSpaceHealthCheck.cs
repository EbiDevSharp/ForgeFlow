using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ForgeFlow.Web.Diagnostics;

public sealed class DiskSpaceHealthCheck : IHealthCheck
{
    private readonly string _targetPath;
    private const long OneGigabyte = 1024L * 1024L * 1024L;
    private const long TwoHundredFiftyMegabytes = 250L * 1024L * 1024L;

    public DiskSpaceHealthCheck(IConfiguration configuration)
    {
        var logDir = configuration["Logging:File:Path"] ?? configuration["Serilog:WriteTo:0:Args:path"];
        if (string.IsNullOrWhiteSpace(logDir))
        {
            _targetPath = AppContext.BaseDirectory;
        }
        else
        {
            _targetPath = Path.GetDirectoryName(logDir) ?? AppContext.BaseDirectory;
        }
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_targetPath));
            if (string.IsNullOrEmpty(root))
            {
                return Task.FromResult(HealthCheckResult.Healthy("Drive path could not be resolved; defaulting to healthy."));
            }

            var drive = new DriveInfo(root);
            var freeBytes = drive.AvailableFreeSpace;

            var data = new Dictionary<string, object>
            {
                ["DriveName"] = drive.Name,
                ["AvailableFreeSpaceMB"] = freeBytes / (1024 * 1024),
                ["TotalSizeMB"] = drive.TotalSize / (1024 * 1024)
            };

            if (freeBytes < TwoHundredFiftyMegabytes)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy($"Available disk space is critically low: {freeBytes / (1024 * 1024)} MB", data: data));
            }

            if (freeBytes < OneGigabyte)
            {
                return Task.FromResult(HealthCheckResult.Degraded($"Available disk space is low: {freeBytes / (1024 * 1024)} MB", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy($"Available disk space is sufficient: {freeBytes / (1024 * 1024)} MB", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Failed to inspect disk space.", ex));
        }
    }
}
