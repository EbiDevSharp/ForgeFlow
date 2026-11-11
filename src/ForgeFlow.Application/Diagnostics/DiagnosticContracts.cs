using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Diagnostics;

public static class DiagnosticPermissions
{
    public const string ErrorLogsRead = "Diagnostics.ErrorLogsRead";
    public const string ErrorLogsManage = "Diagnostics.ErrorLogsManage";
}

public sealed record OperationContextSnapshot(
    Guid CorrelationId,
    string? RequestPath = null,
    string? ComponentName = null,
    long? ActorUserId = null,
    long? OrganizationId = null,
    long? SiteId = null,
    string? UserAgent = null);

public interface IOperationContextAccessor
{
    OperationContextSnapshot GetCurrent();
    void SetContext(long? organizationId, long? siteId, string? componentName = null);
    void SetCorrelation(Guid correlationId, string? requestPath = null, string? userAgent = null);
}

public sealed record ErrorLogRecordContext(
    ErrorSeverity Severity,
    ErrorSourceKind Source,
    string? RequestPath = null,
    string? ComponentName = null,
    long? ActorUserId = null,
    long? OrganizationId = null,
    long? SiteId = null,
    Guid? CorrelationId = null,
    string? UserAgent = null);

public sealed record RecordedErrorResult(
    string ErrorReference,
    Guid CorrelationId,
    bool SavedToDatabase);

public interface IErrorLogRecorder
{
    Task<RecordedErrorResult> RecordExceptionAsync(
        Exception exception,
        ErrorLogRecordContext context,
        CancellationToken ct = default);
}

public sealed record UserOperationRecordRequest(
    string OperationCode,
    UserOperationOutcome Outcome,
    long? ActorUserId,
    long? OrganizationId,
    long? SiteId = null,
    Guid? CorrelationId = null,
    string? TargetEntityType = null,
    string? TargetEntityRef = null,
    string? FailureCode = null,
    string MetadataJson = "{}");

public interface IUserOperationRecorder
{
    Task RecordAsync(UserOperationRecordRequest request, CancellationToken ct = default);
}

public static class UserOperationCatalog
{
    public const string ReportsShortageExported = "Reports.ShortageExported";
    public const string ReportsVarianceExported = "Reports.VarianceExported";
    public const string AuditExported = "Audit.Exported";
    public const string OrganizationActionRejected = "Organization.ActionRejected";
    public const string SiteActionRejected = "Site.ActionRejected";
    public const string UnitActionRejected = "Unit.ActionRejected";
    public const string ItemActionRejected = "Item.ActionRejected";
    public const string BomActionRejected = "Bom.ActionRejected";
    public const string ProductionOrderActionRejected = "ProductionOrder.ActionRejected";
    public const string InventoryActionRejected = "Inventory.ActionRejected";
    public const string UserActionRejected = "User.ActionRejected";
    public const string RoleActionRejected = "Role.ActionRejected";
    public const string ReportingActionRejected = "Reporting.ActionRejected";
    public const string QualityActionRejected = "Quality.ActionRejected";
    public const string ProcurementActionRejected = "Procurement.ActionRejected";

    public static readonly IReadOnlyList<string> KnownOperationCodes =
    [
        ReportsShortageExported,
        ReportsVarianceExported,
        AuditExported,
        OrganizationActionRejected,
        SiteActionRejected,
        UnitActionRejected,
        ItemActionRejected,
        BomActionRejected,
        ProductionOrderActionRejected,
        InventoryActionRejected,
        UserActionRejected,
        RoleActionRejected,
        ReportingActionRejected,
        QualityActionRejected,
        ProcurementActionRejected
    ];
}

public sealed record ErrorLogListQuery(
    ErrorSeverity? Severity = null,
    ErrorSourceKind? Source = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record ErrorLogSummary(
    long Id,
    string ErrorReference,
    Guid CorrelationId,
    DateTime OccurredAtUtc,
    ErrorSeverity Severity,
    ErrorSourceKind Source,
    string ExceptionType,
    int? SqlErrorNumber,
    string RedactedMessage,
    string? RequestPath,
    string? ComponentName,
    long? ActorUserId,
    string? ActorUserName,
    long? OrganizationId,
    long? SiteId);

public sealed record ErrorLogDetails(
    ErrorLogSummary Summary,
    string RedactedStackTrace,
    string? UserAgent);

public sealed record ErrorLogStats(
    int TotalLast24Hours,
    int CriticalLast24Hours,
    int SqlErrorsLast24Hours,
    int HttpOrCircuitLast24Hours);

public sealed class DiagnosticStoreUnavailableException(Exception inner)
    : Exception("Diagnostic store unavailable.", inner);

public interface IErrorLogStore
{
    Task<OrganizationPage<ErrorLogSummary>> ListAsync(OrganizationScope scope, ErrorLogListQuery query, CancellationToken ct = default);
    Task<ErrorLogDetails?> GetAsync(OrganizationScope scope, long id, CancellationToken ct = default);
    Task<ErrorLogStats> GetStatsAsync(OrganizationScope scope, DateTime utcNow, CancellationToken ct = default);
    Task<int> PurgeBeforeAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
