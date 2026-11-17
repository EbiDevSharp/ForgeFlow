using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Audit.Slices;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Web.Hosting;

/// <summary>
/// HTTP endpoints for streaming CSV exports protected by export rate limiting policy.
/// </summary>
public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/export");
        group.RequireRateLimiting(RateLimitingPolicyNames.Export);

        group.MapGet("/audit", async (
            HttpContext context,
            long? organizationId,
            long? siteId,
            AuditCategory? category,
            string? eventCode,
            string? search,
            long? actorUserId,
            AuditOutcomeFilter? outcomeFilter,
            DateOnly? fromDate,
            DateOnly? toDate,
            CancellationToken ct) =>
        {
            var auditUseCases = context.RequestServices.GetService<AuditUseCases>();
            if (auditUseCases is null)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            var exportSlices = auditUseCases.ExportSlices;
            var orgId = organizationId ?? 0L;
            if (orgId <= 0)
                return Results.BadRequest(new { error = "OrganizationId is required" });

            OrganizationScope scope = siteId is long sid && sid > 0
                ? new OrganizationScope.Site(orgId, sid)
                : new OrganizationScope.Organization(orgId);

            var query = new AuditListQuery(
                Category: category ?? AuditCategory.All,
                EventCode: eventCode,
                Search: search,
                ActorUserId: actorUserId,
                SiteId: siteId,
                OutcomeFilter: outcomeFilter ?? AuditOutcomeFilter.All,
                FromDate: fromDate,
                ToDate: toDate);

            var fileName = $"audit-export-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
            var headers = new CsvExportColumnHeaders(
                ["OccurredAt", "Source", "EventCode", "Actor", "Subject", "Outcome", "SiteId", "CorrelationId"],
                fileName);

            context.Response.ContentType = "text/csv; charset=utf-8";
            context.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

            var res = await exportSlices.StreamAuditCsvAsync(context.Response.Body, scope, query, headers, ct);
            if (!res.Succeeded)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: res.Error?.Code);
            }

            return Results.Empty;
        });
    }
}
