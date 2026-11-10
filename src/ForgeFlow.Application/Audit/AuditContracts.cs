using ForgeFlow.Application.Organizations;

namespace ForgeFlow.Application.Audit;

public static class AuditPermissions
{
    public const string AuditRead = "Audit.Read";
}

public enum AuditCategory : byte
{
    All = 0,
    Business = 1,
    Security = 2
}

public enum AuditSourceKind : byte
{
    Business = 1,
    Security = 2
}

public sealed record AuditListQuery(
    AuditCategory Category = AuditCategory.All,
    string? EventCode = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

public sealed record AuditEntrySummary(
    AuditSourceKind Source,
    long Id,
    DateTime OccurredAtUtc,
    string EventCode,
    long? OrganizationId,
    long? SiteId,
    long? ActorUserId,
    string? ActorUserName,
    string? HostOperatorReference,
    long? SubjectUserId,
    string? SubjectUserName,
    byte? Outcome,
    Guid CorrelationId,
    string RawJson);

public sealed record AuditEntryDetails(
    AuditEntrySummary Summary,
    IReadOnlyList<KeyValuePair<string, string>> ParsedFields);

public sealed class AuditStoreUnavailableException(Exception inner) : Exception("Audit persistence is unavailable.", inner);

public interface IAuditOperationsStore
{
    Task<OrganizationPage<AuditEntrySummary>> ListAuditEntries(
        OrganizationScope scope,
        AuditListQuery query,
        CancellationToken ct = default);

    Task<AuditEntryDetails?> GetAuditEntry(
        OrganizationScope scope,
        AuditSourceKind source,
        long entryId,
        CancellationToken ct = default);
}
