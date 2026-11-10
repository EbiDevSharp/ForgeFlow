using System.Text.RegularExpressions;

namespace ForgeFlow.Domain.Diagnostics;

public enum ErrorSeverity : byte
{
    Warning = 1,
    Error = 2,
    Critical = 3
}

public enum ErrorSourceKind : byte
{
    HttpMiddleware = 1,
    BlazorCircuit = 2,
    BlazorComponent = 3,
    InfrastructureStore = 4,
    BackgroundHost = 5
}

public enum UserOperationOutcome : byte
{
    Succeeded = 1,
    BusinessRuleRejected = 2,
    AccessDenied = 3,
    ConcurrencyConflict = 4,
    UnexpectedError = 5
}

public sealed class ErrorLogRecord
{
    private ErrorLogRecord() { }

    public ErrorLogRecord(
        string errorReference,
        Guid correlationId,
        DateTime occurredAtUtc,
        ErrorSeverity severity,
        ErrorSourceKind source,
        string exceptionType,
        int? sqlErrorNumber,
        string redactedMessage,
        string redactedStackTrace,
        string? requestPath,
        string? componentName,
        long? actorUserId,
        long? organizationId,
        long? siteId,
        string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(errorReference) || errorReference.Length > 32)
            throw new ArgumentException("Invalid error reference.", nameof(errorReference));
        if (!Enum.IsDefined(severity))
            throw new ArgumentOutOfRangeException(nameof(severity));
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));

        ErrorReference = errorReference.Trim();
        CorrelationId = correlationId;
        OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        Severity = severity;
        Source = source;
        ExceptionType = Truncate(string.IsNullOrWhiteSpace(exceptionType) ? "System.Exception" : exceptionType.Trim(), 256);
        SqlErrorNumber = sqlErrorNumber;
        RedactedMessage = Truncate(string.IsNullOrWhiteSpace(redactedMessage) ? ExceptionType : redactedMessage.Trim(), 2000);
        RedactedStackTrace = Truncate(redactedStackTrace ?? string.Empty, 16000);
        RequestPath = string.IsNullOrWhiteSpace(requestPath) ? null : Truncate(requestPath.Trim(), 512);
        ComponentName = string.IsNullOrWhiteSpace(componentName) ? null : Truncate(componentName.Trim(), 128);
        ActorUserId = actorUserId is > 0 ? actorUserId : null;
        OrganizationId = organizationId is > 0 ? organizationId : null;
        SiteId = OrganizationId.HasValue && siteId is > 0 ? siteId : null;
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : Truncate(userAgent.Trim(), 256);
    }

    public long Id { get; private set; }
    public string ErrorReference { get; private set; } = string.Empty;
    public Guid CorrelationId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public ErrorSeverity Severity { get; private set; }
    public ErrorSourceKind Source { get; private set; }
    public string ExceptionType { get; private set; } = string.Empty;
    public int? SqlErrorNumber { get; private set; }
    public string RedactedMessage { get; private set; } = string.Empty;
    public string RedactedStackTrace { get; private set; } = string.Empty;
    public string? RequestPath { get; private set; }
    public string? ComponentName { get; private set; }
    public long? ActorUserId { get; private set; }
    public long? OrganizationId { get; private set; }
    public long? SiteId { get; private set; }
    public string? UserAgent { get; private set; }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

public sealed class UserOperationLogRecord
{
    private static readonly Regex CodePattern = new("^[A-Za-z][A-Za-z0-9_.]{1,63}$", RegexOptions.Compiled);

    private UserOperationLogRecord() { }

    public UserOperationLogRecord(
        DateTime occurredAtUtc,
        string operationCode,
        UserOperationOutcome outcome,
        Guid correlationId,
        long? actorUserId,
        long? organizationId,
        long? siteId,
        string? targetEntityType,
        string? targetEntityRef,
        string? failureCode,
        string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(operationCode) || !CodePattern.IsMatch(operationCode.Trim()))
            throw new ArgumentException("Invalid operation code.", nameof(operationCode));
        if (!Enum.IsDefined(outcome))
            throw new ArgumentOutOfRangeException(nameof(outcome));
        if (siteId.HasValue && !organizationId.HasValue)
            throw new ArgumentException("Site requires Organization.", nameof(siteId));

        OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        OperationCode = operationCode.Trim();
        Outcome = outcome;
        CorrelationId = correlationId;
        ActorUserId = actorUserId is > 0 ? actorUserId : null;
        OrganizationId = organizationId is > 0 ? organizationId : null;
        SiteId = OrganizationId.HasValue && siteId is > 0 ? siteId : null;
        TargetEntityType = string.IsNullOrWhiteSpace(targetEntityType) ? null : Truncate(targetEntityType.Trim(), 64);
        TargetEntityRef = string.IsNullOrWhiteSpace(targetEntityRef) ? null : Truncate(targetEntityRef.Trim(), 128);
        FailureCode = string.IsNullOrWhiteSpace(failureCode) ? null : Truncate(failureCode.Trim(), 128);
        MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : Truncate(metadataJson.Trim(), 2000);
    }

    public long Id { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string OperationCode { get; private set; } = string.Empty;
    public UserOperationOutcome Outcome { get; private set; }
    public Guid CorrelationId { get; private set; }
    public long? ActorUserId { get; private set; }
    public long? OrganizationId { get; private set; }
    public long? SiteId { get; private set; }
    public string? TargetEntityType { get; private set; }
    public string? TargetEntityRef { get; private set; }
    public string? FailureCode { get; private set; }
    public string MetadataJson { get; private set; } = "{}";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

public static class DiagnosticErrors
{
    public const string ActorRequired = "DiagnosticErrors.ActorRequired";
    public const string ScopeRequired = "DiagnosticErrors.ScopeRequired";
    public const string AccessDenied = "DiagnosticErrors.AccessDenied";
    public const string NotFound = "DiagnosticErrors.NotFound";
    public const string InvalidQuery = "DiagnosticErrors.InvalidQuery";
    public const string InvalidRetentionDays = "DiagnosticErrors.InvalidRetentionDays";
    public const string PersistenceUnavailable = "DiagnosticErrors.PersistenceUnavailable";
}
