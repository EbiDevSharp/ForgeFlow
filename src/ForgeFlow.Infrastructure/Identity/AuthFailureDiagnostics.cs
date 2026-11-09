using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ForgeFlow.Infrastructure.Identity;

public sealed class AuthDiagnosticsOptions
{
    public bool Enabled { get; init; }
}

public static class AuthFailureDiagnostics
{
    public static string Describe(Exception exception, params string[] secrets)
    {
        var output = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            foreach (var secret in secrets.Where(value => !string.IsNullOrEmpty(value)).OrderByDescending(value => value.Length))
                message = message.Replace(secret, "[redacted]", StringComparison.Ordinal);
            message = Regex.Replace(message, "(?i)(password|pwd|token|hash|stamp|cookie|authorization)\\s*[:=]\\s*[^;\\r\\n]+", "$1=[redacted]");
            message = Regex.Replace(message, "(['\"])(.*?)\\1", match =>
            {
                var value = match.Groups[2].Value;
                return value is "SqlServerRetryingExecutionStrategy" or "DbContext.Database.CreateExecutionStrategy()" || Regex.IsMatch(value, @"^auth\.[A-Za-z_]+$")
                    ? match.Value : "[redacted]";
            });
            message = Regex.Replace(message, @"[A-Za-z0-9_+/=-]{32,}", match => match.Value == "SqlServerRetryingExecutionStrategy" ? match.Value : "[redacted]");
            output.AppendLine(current.GetType().FullName);
            if (current is SqlException sql) output.AppendLine($"SQL number={sql.Number}; state={sql.State}; class={sql.Class}");
            output.AppendLine(message);
            // StackTrace contains method/source locations, never exception.Data, arguments or entity values.
            output.AppendLine(current.StackTrace ?? "[no stack available]");
        }
        return output.ToString();
    }

    public static async Task InspectStoreAsync(IServiceProvider services)
    {
        if (services.GetService<AuthDiagnosticsOptions>()?.Enabled != true) return;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ForgeFlow.AuthDiagnostics");
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var connection = db.Database.GetDbConnection();
            var settings = new SqlConnectionStringBuilder(connection.ConnectionString);
            var strategy = db.Database.CreateExecutionStrategy();
            string Target(string value) => Regex.IsMatch(value, @"^[A-Za-z0-9_.\\,\-]{1,128}$") ? value : "[redacted]";
            logger.LogWarning("AUTH-DIAGNOSTIC configKey=ConnectionStrings:ForgeFlow server={Server} database={Database} windowsAuth={WindowsAuth} encrypt={Encrypt} localTrustException={Trust} strategy={Strategy} retries={Retries}",
                Target(settings.DataSource), Target(settings.InitialCatalog), settings.IntegratedSecurity, settings.Encrypt.ToString(), settings.TrustServerCertificate, strategy.GetType().Name, strategy.RetriesOnFailure);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT OBJECT_ID(N'auth.User'),OBJECT_ID(N'auth.Session'),OBJECT_ID(N'auth.SecurityEvent'),COL_LENGTH(N'auth.Session',N'SecurityStampSnapshot'),COL_LENGTH(N'auth.Session',N'RevokedAtUtc'),COL_LENGTH(N'auth.SecurityEvent',N'CorrelationId'),COL_LENGTH(N'auth.SecurityEvent',N'DataJson')";
            await using (var reader = await command.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                string[] fields = ["auth.User", "auth.Session", "auth.SecurityEvent", "Session.SecurityStampSnapshot", "Session.RevokedAtUtc", "SecurityEvent.CorrelationId", "SecurityEvent.DataJson"];
                for (var index = 0; index < fields.Length; index++) logger.LogWarning("AUTH-DIAGNOSTIC schema={Field} present={Present}", fields[index], !reader.IsDBNull(index));
            }
            var pending = await db.Database.GetPendingMigrationsAsync();
            logger.LogWarning("AUTH-DIAGNOSTIC pendingAuthMigrations={Count}", pending.Count());
        }
        catch (Exception exception)
        { logger.LogError("AUTH-DIAGNOSTIC stage=store-schema details={Details}", Describe(exception)); }
    }
}
