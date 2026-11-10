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

    public OperationContextSnapshot GetCurrent()
    {
        var snap = current.Value;
        if (snap is not null)
            return snap;

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
        return fresh;
    }

    public void SetContext(long? organizationId, long? siteId, string? componentName = null)
    {
        var baseSnap = GetCurrent();
        current.Value = baseSnap with
        {
            OrganizationId = organizationId,
            SiteId = siteId,
            ComponentName = string.IsNullOrWhiteSpace(componentName) ? baseSnap.ComponentName : componentName
        };
    }

    public void SetCorrelation(Guid correlationId, string? requestPath = null, string? userAgent = null)
    {
        var existing = current.Value;
        current.Value = new OperationContextSnapshot(
            correlationId == Guid.Empty ? Guid.NewGuid() : correlationId,
            requestPath ?? existing?.RequestPath,
            existing?.ComponentName,
            existing?.ActorUserId,
            existing?.OrganizationId,
            existing?.SiteId,
            userAgent ?? existing?.UserAgent);
    }
}
