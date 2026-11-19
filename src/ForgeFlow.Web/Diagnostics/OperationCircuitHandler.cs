using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace ForgeFlow.Web.Diagnostics;

/// <summary>
/// Wraps Blazor Server circuit inbound activities with a fresh per-interaction CorrelationId
/// and persists unexpected exceptions before allowing standard circuit handling.
/// </summary>
public sealed class OperationCircuitHandler(
    IOperationContextAccessor operationContext,
    IErrorLogRecorder errorRecorder,
    ITrustedActorAccessor? actors = null)
    : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
    {
        return async context =>
        {
            var correlationId = Guid.NewGuid();
            operationContext.SetCorrelation(correlationId);
            try
            {
                await next(context);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var snap = operationContext.GetCurrent();
                long? actorId = null;
                if (actors is not null)
                {
                    try { actorId = (await actors.ResolveAsync(CancellationToken.None))?.ActorId; }
                    catch { /* never mask original exception */ }
                }
                string? circuitId = null;
                try { circuitId = context?.Circuit?.Id; }
                catch { /* context.Circuit may be uninitialized in unit tests */ }

                var postSnap = operationContext.GetCurrent();
                await errorRecorder.RecordExceptionAsync(ex, new ErrorLogRecordContext(
                    ErrorSeverity.Critical,
                    ErrorSourceKind.BlazorCircuit,
                    snap.RequestPath ?? postSnap.RequestPath,
                    snap.ComponentName ?? postSnap.ComponentName ?? circuitId,
                    actorId ?? snap.ActorUserId ?? postSnap.ActorUserId,
                    snap.OrganizationId ?? postSnap.OrganizationId,
                    snap.SiteId ?? postSnap.SiteId,
                    correlationId,
                    snap.UserAgent ?? postSnap.UserAgent), CancellationToken.None);

                throw;
            }
        };
    }
}
