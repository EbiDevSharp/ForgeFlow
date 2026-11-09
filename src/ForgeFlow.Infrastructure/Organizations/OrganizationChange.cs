namespace ForgeFlow.Infrastructure.Organizations;

/// <summary>Module-local append-only audit record; no arbitrary request serialization.</summary>
public sealed class OrganizationChange
{
    private OrganizationChange() { }

    internal OrganizationChange(long organizationId, long? siteId, long actorId, string eventCode,
        DateTime occurredAtUtc, Guid correlationId, string changesJson)
    {
        OrganizationId = organizationId;
        SiteId = siteId;
        ActorId = actorId;
        EventCode = eventCode;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        ChangesJson = changesJson;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long? SiteId { get; private set; }
    public long ActorId { get; private set; }
    public string EventCode { get; private set; } = null!;
    public DateTime OccurredAtUtc { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string ChangesJson { get; private set; } = null!;
}
