using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ForgeFlow.Infrastructure.Diagnostics;

public static class DiagnosticsRegistration
{
    public static IServiceCollection AddDiagnostics(
        this IServiceCollection services,
        IConfiguration configuration,
        string? fallbackDirectory = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(new DiagnosticsStorageOptions
        {
            FallbackLogDirectory = string.IsNullOrWhiteSpace(fallbackDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "App_Data", "logs")
                : fallbackDirectory
        });

        services.AddDbContextFactory<DiagnosticsDbContext>(options =>
        {
            var connection = configuration.GetConnectionString("ForgeFlow");
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("DiagnosticErrors.PersistenceUnavailable");
            options.UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "diag"));
        });
        services.RemoveAll<DiagnosticsDbContext>();

        services.AddSingleton<SqlErrorLogStore>(provider => new SqlErrorLogStore(
            provider.GetRequiredService<IDbContextFactory<DiagnosticsDbContext>>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SqlErrorLogStore>>(),
            provider.GetService<DiagnosticsStorageOptions>(),
            provider.GetService<TimeProvider>()));

        services.AddSingleton<IErrorLogRecorder>(provider => provider.GetRequiredService<SqlErrorLogStore>());
        services.AddSingleton<IErrorLogStore>(provider => provider.GetRequiredService<SqlErrorLogStore>());
        services.AddSingleton<IUserOperationRecorder>(provider => provider.GetRequiredService<SqlErrorLogStore>());

        services.AddScoped<ErrorLogUseCases>(provider => new ErrorLogUseCases(
            provider.GetRequiredService<IErrorLogStore>(),
            provider.GetService<ITrustedActorAccessor>(),
            provider.GetService<IOrganizationPermissionEvaluator>(),
            provider.GetService<TimeProvider>()));

        return services;
    }
}
