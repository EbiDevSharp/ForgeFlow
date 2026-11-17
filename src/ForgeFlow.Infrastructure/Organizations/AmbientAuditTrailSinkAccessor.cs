using ForgeFlow.Application.Audit;

namespace ForgeFlow.Infrastructure.Organizations;

/// <summary>
/// Thread-safe ambient accessor for the active transaction's IAuditTrailSink using AsyncLocal.
/// </summary>
public sealed class AmbientAuditTrailSinkAccessor : IAuditTrailSinkAccessor
{
    private static readonly AsyncLocal<IAuditTrailSink?> AmbientSink = new();

    public IAuditTrailSink? CurrentSink => AmbientSink.Value;

    public IDisposable BeginScope(IAuditTrailSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var previous = AmbientSink.Value;
        AmbientSink.Value = sink;
        return new ScopeDisposable(previous);
    }

    private sealed class ScopeDisposable(IAuditTrailSink? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            AmbientSink.Value = previous;
            _disposed = true;
        }
    }
}
