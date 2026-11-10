using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace ForgeFlow.Web.Diagnostics;

/// <summary>
/// Captures unhandled HTTP pipeline exceptions into sanitized SQL / fallback logs,
/// attaches only a non-sensitive ErrorReference and CorrelationId to HttpContext,
/// and delegates rendering to /error.
/// </summary>
public sealed class GlobalExceptionLoggingHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var recorder = httpContext.RequestServices.GetService<IErrorLogRecorder>();
        if (recorder is null)
            return false;

        var actors = httpContext.RequestServices.GetService<ITrustedActorAccessor>();
        long? actorId = null;
        if (actors is not null)
        {
            try { actorId = (await actors.ResolveAsync(cancellationToken))?.ActorId; }
            catch { /* keep recording even if actor resolution fails */ }
        }

        var correlationId = httpContext.Items[HttpOperationContextAccessor.CorrelationItemKey] is Guid g
            ? g
            : Guid.NewGuid();
        httpContext.Items[HttpOperationContextAccessor.CorrelationItemKey] = correlationId;

        var recorded = await recorder.RecordExceptionAsync(exception, new ErrorLogRecordContext(
            ErrorSeverity.Critical,
            ErrorSourceKind.HttpMiddleware,
            httpContext.Request.Path.Value + httpContext.Request.QueryString.Value,
            null,
            actorId,
            null,
            null,
            correlationId,
            httpContext.Request.Headers.UserAgent.ToString()), cancellationToken);

        httpContext.Items[HttpOperationContextAccessor.ErrorRefItemKey] = recorded.ErrorReference;
        return false;
    }
}
