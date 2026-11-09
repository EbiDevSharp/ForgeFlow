using System.Data.Common;
using ForgeFlow.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuthFailureDiagnosticsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Login_store_failure_remains_generic_but_opt_in_diagnostics_preserve_sanitized_cause_and_stack(bool enabled)
    {
        var password = Guid.NewGuid().ToString();
        var capture = new Capture();
        var services = new ServiceCollection().AddLogging(builder => builder.AddProvider(capture));
        services.AddRuntimeIdentity(options => options.UseSqlServer("Server=unreachable.invalid;Database=NoSql;Integrated Security=True")
            .AddInterceptors(new Failure(password)));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(new AuthDiagnosticsOptions { Enabled = enabled });
        services.AddSingleton<RuntimeSessions>();
        await using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<RuntimeSessions>().LoginAsync("synthetic.operator", password);
        Assert.Equal("Auth.ServiceUnavailable", result.Code);
        Assert.Null(result.Session);
        var diagnostics = capture.Messages.Where(message => message.StartsWith("AUTH-DIAGNOSTIC", StringComparison.Ordinal)).ToArray();
        if (!enabled) Assert.Empty(diagnostics);
        else
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Contains("stage=store-open", diagnostic);
            Assert.Contains("does not support user-initiated transactions", diagnostic);
            Assert.Contains(nameof(Failure.ConnectionOpeningAsync), diagnostic);
            Assert.Contains("inner failure", diagnostic);
            Assert.DoesNotContain(password, diagnostic);
            Assert.DoesNotContain("synthetic.operator", diagnostic);
        }
    }

    [Fact]
    public void Sanitizer_preserves_schema_identifiers_but_removes_quoted_values_and_credential_assignments()
    {
        var safe = AuthFailureDiagnostics.Describe(new InvalidOperationException("Invalid object name 'auth.Session'. Password=private-value; Cookie=private-cookie; quoted 'private-other'."));
        Assert.Contains("auth.Session", safe);
        Assert.DoesNotContain("private-value", safe);
        Assert.DoesNotContain("private-cookie", safe);
        Assert.DoesNotContain("private-other", safe);
    }

    private sealed class Failure(string password) : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The configured execution strategy 'SqlServerRetryingExecutionStrategy' does not support user-initiated transactions.",
                new InvalidOperationException("inner failure: Password=" + password + "; account 'synthetic.operator'"));
    }
    private sealed class Capture : ILoggerProvider, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
        public void Dispose() { }
    }
}
