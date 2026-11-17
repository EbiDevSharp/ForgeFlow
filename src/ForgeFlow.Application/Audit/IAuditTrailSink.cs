namespace ForgeFlow.Application.Audit;

/// <summary>
/// Unit-of-work audit sink allowing domain event handlers to append audit records directly
/// into the active database transaction without external store dependencies.
/// </summary>
public interface IAuditTrailSink
{
    void Record(
        long organizationId,
        long? siteId,
        string eventCode,
        string detailsJson);
}

/// <summary>
/// Ambient accessor to the active unit-of-work audit sink.
/// </summary>
public interface IAuditTrailSinkAccessor
{
    IAuditTrailSink? CurrentSink { get; }
}
