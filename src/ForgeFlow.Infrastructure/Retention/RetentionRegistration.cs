using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeFlow.Infrastructure.Retention;

public static class RetentionRegistration
{
    public static IServiceCollection AddRetentionServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<RetentionPolicyOptions>? configure = null)
    {
        var section = configuration.GetSection(RetentionPolicyOptions.SectionName);
        services.Configure<RetentionPolicyOptions>(options =>
        {
            section.Bind(options);
            configure?.Invoke(options);
        });

        services.AddHostedService<RetentionBackgroundService>();
        return services;
    }
}
