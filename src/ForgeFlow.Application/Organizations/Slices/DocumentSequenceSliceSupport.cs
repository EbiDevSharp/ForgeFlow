using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Domain.Organizations;

namespace ForgeFlow.Application.Organizations.Slices;

/// <summary>
/// Shared authorization, scoping, version decoding, and error translation support for document sequence slices.
/// </summary>
public sealed class DocumentSequenceSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    public IUserOperationRecorder? OperationRecorder => operationRecorder;
    public IOperationContextAccessor? OpContext => opContext;
    public IOrganizationPermissionEvaluator? Permissions => permissions;
    public DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async ValueTask<(TrustedActor? Actor, string? Error)> AuthorizeAsync(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, DocumentSequenceErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, DocumentSequenceErrors.ActorRequired);
        if (scope is null) return (null, DocumentSequenceErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, DocumentSequenceErrors.AccessDenied);
        return (actor, null);
    }

    public async Task<TrustedActor?> ResolveActorAsync(CancellationToken ct)
        => actors is null ? null : await actors.ResolveAsync(ct);

    public OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await AuthorizeAsync(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    public static bool MatchesOrgOnly(OrganizationScope? scope, long organizationId)
        => scope is OrganizationScope.Organization org && org.OrganizationId == organizationId && organizationId > 0;

    public static bool MatchesOrgOrSite(OrganizationScope? scope, long organizationId)
        => organizationId > 0 && scope switch
        {
            OrganizationScope.Organization org => org.OrganizationId == organizationId,
            OrganizationScope.Site site => site.OrganizationId == organizationId,
            _ => false
        };

    public static byte[]? DecodeVersion(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        try
        {
            var bytes = Convert.FromBase64String(raw);
            return bytes.Length == 8 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
