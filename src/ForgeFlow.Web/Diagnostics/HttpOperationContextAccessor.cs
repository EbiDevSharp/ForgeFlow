using ForgeFlow.Application.Diagnostics;

namespace ForgeFlow.Web.Diagnostics;

/// <summary>
/// Tracks the active logical operation CorrelationId and tenant context across HTTP requests
/// and Blazor Server circuit inbound activities.
/// </summary>
public sealed class HttpOperationContextAccessor(IHttpContextAccessor? httpAccessor = null)
    : IOperationContextAccessor
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string CorrelationItemKey = "ForgeFlow.CorrelationId";
    public const string ErrorRefItemKey = "ForgeFlow.ErrorReference";

    private readonly AsyncLocal<OperationContextSnapshot?> current = new();
    private OperationContextSnapshot? scopedFallback;

    public OperationContextSnapshot GetCurrent()
    {
        var snap = current.Value;
        var fb = scopedFallback;
        if (snap is not null && fb is not null)
        {
            return snap with
            {
                RequestPath = snap.RequestPath ?? fb.RequestPath,
                ComponentName = snap.ComponentName ?? fb.ComponentName,
                ActorUserId = snap.ActorUserId ?? fb.ActorUserId,
                OrganizationId = snap.OrganizationId ?? fb.OrganizationId,
                SiteId = snap.SiteId ?? fb.SiteId,
                UserAgent = snap.UserAgent ?? fb.UserAgent
            };
        }
        if (snap is not null)
            return snap;
        if (fb is not null)
            return fb;

        var ctx = httpAccessor?.HttpContext;
        if (ctx is not null)
        {
            var corr = ctx.Items[CorrelationItemKey] is Guid g ? g : Guid.NewGuid();
            var ua = ctx.Request.Headers.UserAgent.ToString();
            return new OperationContextSnapshot(
                corr,
                ctx.Request.Path.Value,
                null,
                null,
                null,
                null,
                string.IsNullOrWhiteSpace(ua) ? null : ua);
        }

        var fresh = new OperationContextSnapshot(Guid.NewGuid());
        current.Value = fresh;
        scopedFallback = fresh;
        return fresh;
    }

    public void SetContext(long? organizationId, long? siteId, string? componentName = null)
    {
        var baseSnap = GetCurrent();
        var updated = baseSnap with
        {
            OrganizationId = organizationId ?? baseSnap.OrganizationId,
            SiteId = siteId ?? baseSnap.SiteId,
            ComponentName = string.IsNullOrWhiteSpace(componentName) ? baseSnap.ComponentName : componentName
        };
        current.Value = updated;
        scopedFallback = updated;
    }

    public void SetCorrelation(Guid correlationId, string? requestPath = null, string? userAgent = null)
    {
        var existing = current.Value ?? scopedFallback;
        var updated = new OperationContextSnapshot(
            correlationId == Guid.Empty ? Guid.NewGuid() : correlationId,
            requestPath ?? existing?.RequestPath,
            existing?.ComponentName,
            existing?.ActorUserId,
            existing?.OrganizationId,
            existing?.SiteId,
            userAgent ?? existing?.UserAgent);
        current.Value = updated;
        scopedFallback = updated;
    }
}
