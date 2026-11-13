using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Audit.Slices;

/// <summary>
/// Operational slice for authorized CSV export of audit events and compliance logs.
/// </summary>
public sealed class AuditExportSlices(
    IAuditOperationsStore store,
    AuditSliceSupport support)
{
    public async Task<OrganizationResult<CsvExportFile>> ExportAuditCsvAsync(
        OrganizationScope scope,
        AuditListQuery query,
        CsvExportColumnHeaders columns,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(columns);

        var (allowed, actor, error) = await support.AuthorizePermissionAsync(scope, AuditPermissions.AuditExport, ct);
        if (!allowed)
        {
            await OperationAuditHelper.RecordAsync(support.OperationRecorder, support.OpContext, UserOperationCatalog.AuditExported,
                OperationAuditHelper.MapFailureOutcome(error), actor, scope, null, null, "Audit", null, error);
            return OrganizationResult<CsvExportFile>.Failure(error!);
        }

        if (columns.Headers.Count != 8
            || string.IsNullOrWhiteSpace(columns.FileName)
            || !AuditSliceSupport.TryNormalizeQuery(scope, query with { Page = 1, PageSize = AuditSliceSupport.MaxExportRows }, out var normalized, allowExportPageSize: true))
        {
            return OrganizationResult<CsvExportFile>.Failure(AuditErrors.InvalidQuery);
        }

        try
        {
            var page = await store.ListAuditEntries(scope, normalized!, ct);
            var rows = page.Items.Select(e => (IReadOnlyList<string>)
            [
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc), TimeZoneInfo.Local)
                    .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                e.Source.ToString(),
                e.EventCode,
                e.ActorUserName ?? e.HostOperatorReference ?? (e.ActorUserId?.ToString(CultureInfo.InvariantCulture) ?? "System"),
                e.SubjectUserName ?? (e.SubjectUserId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                AuditSliceSupport.FormatOutcomeForCsv(e),
                e.SiteId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                e.CorrelationId.ToString("D")
            ]);

            var file = CsvExportBuilder.Build(columns.FileName, columns.Headers, rows);
            await OperationAuditHelper.RecordAsync(support.OperationRecorder, support.OpContext, UserOperationCatalog.AuditExported,
                UserOperationOutcome.Succeeded, actor, scope, null, normalized!.SiteId,
                "Audit", file.FileName, null,
                new { Category = normalized.Category.ToString(), RowCount = page.Items.Count });
            return OrganizationResult<CsvExportFile>.Success(file);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<CsvExportFile>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }
}
