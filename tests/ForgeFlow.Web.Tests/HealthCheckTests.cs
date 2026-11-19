using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Infrastructure.Organizations;
using ForgeFlow.Web.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class HealthCheckTests
{
    [Fact]
    public async Task DiskSpaceHealthCheck_Returns_Healthy_Or_Degraded_With_Drive_Metrics()
    {
        var config = new ConfigurationBuilder().Build();
        var check = new DiskSpaceHealthCheck(config);
        var context = new HealthCheckContext();

        var result = await check.CheckHealthAsync(context);

        Assert.True(result.Status is HealthStatus.Healthy or HealthStatus.Degraded);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.ContainsKey("AvailableFreeSpaceMB"));
        Assert.True(result.Data.ContainsKey("DriveName"));
    }

    [Fact]
    public async Task DbContextHealthCheck_Returns_Unhealthy_When_Cannot_Connect()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AuthDbContext>(options =>
            options.UseSqlServer("Server=invalid.invalid;Database=invalid;Connect Timeout=1;"));
        var provider = services.BuildServiceProvider();

        var check = new AuthDbContextHealthCheck(provider);
        var context = new HealthCheckContext();

        var result = await check.CheckHealthAsync(context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
