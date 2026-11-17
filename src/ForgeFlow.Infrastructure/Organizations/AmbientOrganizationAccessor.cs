using System.Threading;
using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Infrastructure.Organizations;

/// <summary>
/// Thread-safe ambient accessor for the active OrganizationId using AsyncLocal.
/// Provides context for dynamic EF Core HasQueryFilter multi-tenancy enforcement (Guardrail FFA-010).
/// </summary>
public sealed class AmbientOrganizationAccessor : ICurrentOrganizationAccessor
{
    private static readonly AsyncLocal<long?> AmbientOrgId = new();

    public static long? Current
    {
        get => AmbientOrgId.Value;
        set => AmbientOrgId.Value = value;
    }

    public long? CurrentOrganizationId => AmbientOrgId.Value;

    public static IDisposable BeginScope(long organizationId)
    {
        var previous = AmbientOrgId.Value;
        AmbientOrgId.Value = organizationId;
        return new ScopeDisposable(previous);
    }

    public static void Reset() => AmbientOrgId.Value = null;

    private sealed class ScopeDisposable(long? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            AmbientOrgId.Value = previous;
            _disposed = true;
        }
    }
}
