using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Audit;

public sealed class AuditUseCases(
    IAuditOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null)
{
    private const int MaxExportRows = 2000;

    public async Task<OrganizationResult<OrganizationPage<AuditEntrySummary>>> ListAuditEntries(
        OrganizationScope scope,
        AuditListQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        if (!TryNormalizeQuery(scope, query, out var normalized))
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(AuditErrors.InvalidQuery);

        var (allowed, _, error) = await AuthorizePermissionAsync(scope, AuditPermissions.AuditRead, ct);
        if (!allowed)
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(error!);

        try
        {
            var page = await store.ListAuditEntries(scope, normalized!, ct);
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Success(page);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<OrganizationPage<AuditEntrySummary>>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<AuditEntryDetails>> GetAuditEntry(
        OrganizationScope scope,
        AuditSourceKind source,
        long entryId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(source) || entryId <= 0)
            return OrganizationResult<AuditEntryDetails>.Failure(AuditErrors.NotFound);

        var (allowed, _, error) = await AuthorizePermissionAsync(scope, AuditPermissions.AuditRead, ct);
        if (!allowed)
            return OrganizationResult<AuditEntryDetails>.Failure(error!);

        try
        {
            var details = await store.GetAuditEntry(scope, source, entryId, ct);
            return details is null
                ? OrganizationResult<AuditEntryDetails>.Failure(AuditErrors.NotFound)
                : OrganizationResult<AuditEntryDetails>.Success(details);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<AuditEntryDetails>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<IReadOnlyList<AuditActorOption>>> ListActors(
        OrganizationScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var (allowed, _, error) = await AuthorizePermissionAsync(scope, AuditPermissions.AuditRead, ct);
        if (!allowed)
            return OrganizationResult<IReadOnlyList<AuditActorOption>>.Failure(error!);

        try
        {
            var list = await store.ListActors(scope, ct);
            return OrganizationResult<IReadOnlyList<AuditActorOption>>.Success(list);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<IReadOnlyList<AuditActorOption>>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<IReadOnlyList<string>>> ListEventCodes(
        OrganizationScope scope,
        AuditCategory category = AuditCategory.All,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(category))
            return OrganizationResult<IReadOnlyList<string>>.Failure(AuditErrors.InvalidQuery);

        var (allowed, _, error) = await AuthorizePermissionAsync(scope, AuditPermissions.AuditRead, ct);
        if (!allowed)
            return OrganizationResult<IReadOnlyList<string>>.Failure(error!);

        try
        {
            var observed = await store.ListDistinctEventCodes(scope, category, ct);
            var registered = AuditEventCatalog.All
                .Where(d => category == AuditCategory.All || d.Category == category)
                .Where(d => scope is not OrganizationScope.Platform || d.Category != AuditCategory.Business)
                .Select(d => d.Code);

            var merged = registered
                .Concat(observed)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return OrganizationResult<IReadOnlyList<string>>.Success(merged);
        }
        catch (AuditStoreUnavailableException)
        {
            return OrganizationResult<IReadOnlyList<string>>.Failure(AuditErrors.PersistenceUnavailable);
        }
    }

    public async Task<OrganizationResult<CsvExportFile>> ExportAuditCsv(
        OrganizationScope scope,
        AuditListQuery query,
        CsvExportColumnHeaders columns,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(columns);

        var (allowed, actor, error) = await AuthorizePermissionAsync(scope, AuditPermissions.AuditExport, ct);
        if (!allowed)
        {
            await OperationAuditHelper.RecordAsync(operationRecorder, opContext, UserOperationCatalog.AuditExported,
                OperationAuditHelper.MapFailureOutcome(error), actor, scope, null, null, "Audit", null, error);
            return OrganizationResult<CsvExportFile>.Failure(error!);
        }

        if (columns.Headers.Count != 8 || string.IsNullOrWhiteSpace(columns.FileName) || !TryNormalizeQuery(scope, query with { Page = 1, PageSize = MaxExportRows }, out var normalized, allowExportPageSize: true))
            return OrganizationResult<CsvExportFile>.Failure(AuditErrors.InvalidQuery);

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
                FormatOutcomeForCsv(e),
                e.SiteId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                e.CorrelationId.ToString("D")
            ]);

            var file = CsvExportBuilder.Build(columns.FileName, columns.Headers, rows);
            await OperationAuditHelper.RecordAsync(operationRecorder, opContext, UserOperationCatalog.AuditExported,
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

    private static string FormatOutcomeForCsv(AuditEntrySummary entry)
    {
        if (entry.Source == AuditSourceKind.Business) return "Succeeded";
        return entry.Outcome switch
        {
            0 => "Succeeded",
            1 => "Denied",
            2 => "Failed",
            _ => "Succeeded"
        };
    }

    private static bool TryNormalizeQuery(
        OrganizationScope scope,
        AuditListQuery query,
        out AuditListQuery? normalized,
        bool allowExportPageSize = false)
    {
        normalized = null;
        var maxPageSize = allowExportPageSize ? MaxExportRows : 100;
        if (!Enum.IsDefined(query.Category)
            || !Enum.IsDefined(query.OutcomeFilter)
            || query.Page is < 1 or > 100000
            || query.PageSize < 1 || query.PageSize > maxPageSize
            || (query.ActorUserId.HasValue && query.ActorUserId.Value <= 0)
            || (query.SiteId.HasValue && query.SiteId.Value <= 0)
            || (query.FromDate.HasValue && query.ToDate.HasValue && query.FromDate.Value > query.ToDate.Value))
            return false;

        if (scope is OrganizationScope.Site siteScope && query.SiteId.HasValue && query.SiteId.Value != siteScope.SiteId)
            return false;

        var eventCode = string.IsNullOrWhiteSpace(query.EventCode) ? null : query.EventCode.Trim();
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if ((eventCode is not null && eventCode.Length > 64)
            || (search is not null && search.Length > 100))
            return false;

        normalized = query with { EventCode = eventCode, Search = search };
        return true;
    }

    private async Task<(bool Allowed, TrustedActor? Actor, string? Error)> AuthorizePermissionAsync(
        OrganizationScope scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null || permissions is null)
            return (false, null, AuditErrors.ActorRequired);

        TrustedActor? actor;
        try { actor = await actors.ResolveAsync(ct); }
        catch { return (false, null, AuditErrors.PersistenceUnavailable); }

        if (actor is null || actor.ActorId <= 0)
            return (false, null, AuditErrors.ActorRequired);

        bool allowed;
        try { allowed = (await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed; }
        catch { return (false, actor, AuditErrors.PersistenceUnavailable); }

        return allowed ? (true, actor, null) : (false, actor, AuditErrors.AccessDenied);
    }
}
