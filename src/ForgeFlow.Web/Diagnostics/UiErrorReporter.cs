using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Web.Diagnostics;

/// <summary>
/// Safe UI helper allowing Razor pages to log caught unexpected failures
/// and receive a non-sensitive ErrorReference for display.
/// </summary>
public sealed class UiErrorReporter(
    IErrorLogRecorder recorder,
    IOperationContextAccessor operationContext,
    ITrustedActorAccessor? actors = null)
{
    public async Task<string> ReportAsync(
        Exception exception,
        string componentName,
        long? organizationId = null,
        long? siteId = null,
        ErrorSeverity severity = ErrorSeverity.Error)
    {
        long? actorId = null;
        if (actors is not null)
        {
            try { actorId = (await actors.ResolveAsync(CancellationToken.None))?.ActorId; }
            catch { /* never throw during error reporting */ }
        }

        var snap = operationContext.GetCurrent();
        var recorded = await recorder.RecordExceptionAsync(exception, new ErrorLogRecordContext(
            severity,
            ErrorSourceKind.BlazorComponent,
            snap.RequestPath,
            componentName,
            actorId ?? snap.ActorUserId,
            organizationId ?? snap.OrganizationId,
            siteId ?? snap.SiteId,
            snap.CorrelationId,
            snap.UserAgent), CancellationToken.None);

        return recorded.ErrorReference;
    }
}
