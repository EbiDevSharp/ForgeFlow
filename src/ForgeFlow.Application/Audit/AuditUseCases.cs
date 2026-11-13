using ForgeFlow.Application.Audit.Slices;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Audit;

namespace ForgeFlow.Application.Audit;

/// <summary>
/// Secured facade for organization audit trail queries, actor lookups, event codes, and CSV exports.
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Audit.Slices"/>.
/// </summary>
public sealed class AuditUseCases
{
    private readonly AuditQuerySlices querySlices;
    private readonly AuditExportSlices exportSlices;

    public AuditUseCases(
        IAuditOperationsStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null)
    {
        var support = new AuditSliceSupport(actors, permissions, operationRecorder, opContext);
        querySlices = new AuditQuerySlices(store, support);
        exportSlices = new AuditExportSlices(store, support);
    }

    public Task<OrganizationResult<OrganizationPage<AuditEntrySummary>>> ListAuditEntries(
        OrganizationScope scope,
        AuditListQuery query,
        CancellationToken ct = default)
        => querySlices.ListAuditEntriesAsync(scope, query, ct);

    public Task<OrganizationResult<AuditEntryDetails>> GetAuditEntry(
        OrganizationScope scope,
        AuditSourceKind source,
        long entryId,
        CancellationToken ct = default)
        => querySlices.GetAuditEntryAsync(scope, source, entryId, ct);

    public Task<OrganizationResult<IReadOnlyList<AuditActorOption>>> ListActors(
        OrganizationScope scope,
        CancellationToken ct = default)
        => querySlices.ListActorsAsync(scope, ct);

    public Task<OrganizationResult<IReadOnlyList<string>>> ListEventCodes(
        OrganizationScope scope,
        AuditCategory category = AuditCategory.All,
        CancellationToken ct = default)
        => querySlices.ListEventCodesAsync(scope, category, ct);

    public Task<OrganizationResult<CsvExportFile>> ExportAuditCsv(
        OrganizationScope scope,
        AuditListQuery query,
        CsvExportColumnHeaders columns,
        CancellationToken ct = default)
        => exportSlices.ExportAuditCsvAsync(scope, query, columns, ct);
}
