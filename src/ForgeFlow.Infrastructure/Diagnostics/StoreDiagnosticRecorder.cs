using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Infrastructure.Diagnostics;

internal static class StoreDiagnosticRecorder
{
    public static async Task RecordUnavailableAsync(
        IErrorLogRecorder? recorder,
        IOperationContextAccessor? opContext,
        Exception exception,
        string componentName,
        long? actorId = null,
        ErrorSeverity severity = ErrorSeverity.Error)
    {
        if (recorder is null) return;
        try
        {
            var snap = opContext?.GetCurrent();
            await recorder.RecordExceptionAsync(exception, new ErrorLogRecordContext(
                severity,
                ErrorSourceKind.InfrastructureStore,
                snap?.RequestPath,
                componentName,
                actorId ?? snap?.ActorUserId,
                snap?.OrganizationId,
                snap?.SiteId,
                snap?.CorrelationId,
                snap?.UserAgent), CancellationToken.None);
        }
        catch
        {
            // Never mask the primary infrastructure failure.
        }
    }
}
