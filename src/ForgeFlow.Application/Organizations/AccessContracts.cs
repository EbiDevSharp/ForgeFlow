namespace ForgeFlow.Application.Organizations;

public enum ActorProvenance { AuthenticatedSession = 1, ControlledBootstrap = 2 }

/// <summary>
/// Server adapter output, never a bindable request DTO or proof of authentication by construction.
/// AUTH-01 must establish identity/session trust before returning this context.
/// ProvenanceReference identifies a server-side session/bootstrap operation; it is not a token.
/// </summary>
public sealed class TrustedActor
{
    public TrustedActor(long actorId, ActorProvenance provenance, Guid provenanceReference, Guid correlationId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actorId);
        if (!Enum.IsDefined(provenance)) throw new ArgumentOutOfRangeException(nameof(provenance));
        if (provenanceReference == Guid.Empty) throw new ArgumentException(nameof(provenanceReference));
        if (correlationId == Guid.Empty) throw new ArgumentException(nameof(correlationId));
        ActorId = actorId;
        Provenance = provenance;
        ProvenanceReference = provenanceReference;
        CorrelationId = correlationId;
    }

    public long ActorId { get; }
    public ActorProvenance Provenance { get; }
    public Guid ProvenanceReference { get; }
    public Guid CorrelationId { get; }
}

public interface ITrustedActorAccessor
{
    /// <summary>Null means no trusted actor; callers must deny, never substitute an anonymous/system actor.</summary>
    ValueTask<TrustedActor?> ResolveAsync(CancellationToken cancellationToken);
}

public interface ICurrentActorAccessor
{
    /// <summary>Current ambient trusted actor for standard DbContext SaveChanges interception, or null when unattributed.</summary>
    TrustedActor? CurrentActor { get; }
}

public interface ICurrentOrganizationAccessor
{
    /// <summary>When non-null, OrganizationDbContext enforces a global OrganizationId query filter.</summary>
    long? CurrentOrganizationId { get; }
}

/// <summary>Untrusted requested scope, not an authorization grant. No null/default platform fallback.</summary>
public abstract class OrganizationScope
{
    private OrganizationScope() { }

    public sealed class Platform : OrganizationScope { }

    public sealed class Organization : OrganizationScope
    {
        public Organization(long organizationId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(organizationId);
            OrganizationId = organizationId;
        }
        public long OrganizationId { get; }
    }

    public sealed class Site : OrganizationScope
    {
        public Site(long organizationId, long siteId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(organizationId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(siteId);
            OrganizationId = organizationId;
            SiteId = siteId;
        }
        public long OrganizationId { get; }
        public long SiteId { get; }
    }
}

public static class OrganizationPermissions
{
    public const string OrganizationsRead = "Organizations.Read";
    public const string OrganizationsCreate = "Organizations.Create";
    public const string OrganizationsUpdate = "Organizations.Update";
    public const string OrganizationsSetActive = "Organizations.SetActive";
    public const string SitesRead = "Sites.Read";
    public const string SitesCreate = "Sites.Create";
    public const string SitesUpdate = "Sites.Update";
    public const string SitesSetActive = "Sites.SetActive";
}

public static class OrganizationAccessErrors
{
    public const string ActorRequired = "OrganizationSite.ActorRequired";
    public const string ScopeRequired = "OrganizationSite.ScopeRequired";
    public const string AccessDenied = "OrganizationSite.AccessDenied";
    public const string ConcurrencyConflict = "OrganizationSite.ConcurrencyConflict";
    public const string AuditUnavailable = "OrganizationSite.AuditUnavailable";
    public const string PersistenceUnavailable = "OrganizationSite.PersistenceUnavailable";
    public const string OutcomeUnknown = "OrganizationSite.OutcomeUnknown";
}

/// <summary>Default value denies access. A decision is valid only for the evaluated request.</summary>
public readonly record struct PermissionDecision(bool IsAllowed);

public interface IOrganizationPermissionEvaluator
{
    /// <summary>
    /// AUTH-01 must revalidate actor/session, active membership, known permission and exact scope.
    /// Verify Site belongs to Organization; site membership never implies sibling/organization-wide access.
    /// Unknown permissions deny. Explicit platform authority is required for organization creation.
    /// Lifecycle grants can authorize organization reactivation without opening ordinary inactive-org access.
    /// Absence/failure of an adapter must deny at the use-case boundary. No implementation is supplied in 01.1.
    /// </summary>
    ValueTask<PermissionDecision> EvaluateAsync(TrustedActor actor, OrganizationScope requestedScope,
        string permission, CancellationToken cancellationToken);
}
