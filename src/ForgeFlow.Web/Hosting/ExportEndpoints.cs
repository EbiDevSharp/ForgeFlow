using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Audit.Slices;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Web.Localization;
using Microsoft.Extensions.Localization;

namespace ForgeFlow.Web.Hosting;

/// <summary>
/// HTTP endpoints for streaming CSV exports protected by authentication and export rate limiting policy.
/// </summary>
public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/export");
        group.RequireAuthorization();
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

            var clock = context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
            var localizer = context.RequestServices.GetRequiredService<IStringLocalizer<SharedResources>>();
            var fileName = $"audit-export-{clock.GetUtcNow():yyyyMMdd-HHmm}.csv";
            var headers = new CsvExportColumnHeaders(
                [
                    localizer["AuditTimeColumn"].Value,
                    localizer["AuditCategoryLabel"].Value,
                    localizer["AuditEventColumn"].Value,
                    localizer["AuditActorColumn"].Value,
                    localizer["AuditSubjectColumn"].Value,
                    localizer["AuditOutcomeColumn"].Value,
                    localizer["AuditColSite"].Value,
                    localizer["AuditColCorrelation"].Value
                ],
                fileName);

            await using var deferredStream = new DeferredCsvResponseStream(context.Response, fileName);
            var res = await exportSlices.StreamAuditCsvAsync(deferredStream, scope, query, headers, ct);
            if (!res.Succeeded)
            {
                if (!context.Response.HasStarted)
                {
                    var statusCode = string.Equals(res.Error?.Code, AuditErrors.InvalidQuery, StringComparison.Ordinal)
                        ? StatusCodes.Status400BadRequest
                        : StatusCodes.Status403Forbidden;
                    return Results.Problem(statusCode: statusCode, detail: res.Error?.Code);
                }
                return Results.Empty;
            }

            return Results.Empty;
        });
    }

    private sealed class DeferredCsvResponseStream(HttpResponse response, string fileName) : Stream
    {
        private bool _headersWritten;

        private void EnsureHeaders()
        {
            if (_headersWritten) return;
            _headersWritten = true;
            response.ContentType = "text/csv; charset=utf-8";
            response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => response.Body.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => response.Body.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureHeaders();
            response.Body.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            EnsureHeaders();
            return response.Body.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            EnsureHeaders();
            return response.Body.WriteAsync(buffer, cancellationToken);
        }
    }
}
