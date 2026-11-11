using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Roles;
using ForgeFlow.Application.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ForgeFlow.Infrastructure.Identity;

public static class IdentityPersistence
{
    // Host-only composition. Deliberately not called by Web's runtime registration.
    public static IServiceCollection AddBootstrapIdentity(this IServiceCollection services, Action<DbContextOptionsBuilder> options)
        => AddCore(services, options, isolatedHost: true);

    public static IServiceCollection AddRuntimeIdentity(this IServiceCollection services, Action<DbContextOptionsBuilder> options)
        => AddCore(services, options, isolatedHost: false);

    public static IServiceCollection AddUserManagement(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IUserOperationsStore, SqlUserOperationsStore>();
        services.AddScoped<UserUseCases>();
        return services;
    }

    public static IServiceCollection AddRoleManagement(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IRoleOperationsStore, SqlRoleOperationsStore>();
        services.AddScoped<RoleUseCases>();
        return services;
    }

    public static IServiceCollection AddAuditManagement(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ForgeFlow.Application.Users.IUserNotificationLiveChannel, UserNotificationLiveChannel>();
        services.AddScoped<IAuditOperationsStore, SqlAuditOperationsStore>();
        services.AddScoped<ForgeFlow.Application.Users.IMyActivityStore, SqlAuditOperationsStore>();
        services.AddScoped<AuditUseCases>();
        return services;
    }

    private static IServiceCollection AddCore(IServiceCollection services, Action<DbContextOptionsBuilder> options, bool isolatedHost)
    {
        services.AddLogging(logging => { if (isolatedHost) logging.ClearProviders(); });
        services.TryAddSingleton<ForgeFlow.Application.Users.IUserNotificationLiveChannel, UserNotificationLiveChannel>();
        services.AddDbContext<AuthDbContext>(options);
        services.AddIdentityCore<ApplicationAccount>(identity => {
            identity.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
            identity.Password.RequiredLength = 12;
            identity.Password.RequireDigit = false; identity.Password.RequireNonAlphanumeric = false;
            identity.Password.RequireUppercase = false; identity.Password.RequireLowercase = false;
            identity.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-@+";
            identity.Lockout.MaxFailedAccessAttempts = 5;
            identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AuthDbContext>().AddPasswordValidator<BootstrapPasswordValidator>();
        return services;
    }
}

public sealed class BootstrapPasswordValidator : IPasswordValidator<ApplicationAccount>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationAccount> manager, ApplicationAccount user, string? password)
        => Task.FromResult(password is null || password.Length > 128
            ? IdentityResult.Failed(new IdentityError { Code = "PasswordTooLong", Description = "" }) : IdentityResult.Success);
}

public sealed class UserNotificationLiveChannel : ForgeFlow.Application.Users.IUserNotificationLiveChannel
{
    public event Action<long>? UserNotificationsChanged;

    public void NotifyUser(long userId)
    {
        if (userId > 0)
            UserNotificationsChanged?.Invoke(userId);
    }
}

public sealed class AuthDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AuthDbContext>()
        .UseSqlServer("Server=invalid.invalid;Database=ForgeFlow_AuthDesignOnly;Integrated Security=true;Encrypt=true;Connect Timeout=1",
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "auth")).Options);
}
