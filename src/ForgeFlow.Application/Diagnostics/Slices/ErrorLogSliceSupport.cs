using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Diagnostics.Slices;

/// <summary>
/// Shared authorization and validation support for diagnostic error log vertical slices.
/// </summary>
public sealed class ErrorLogSliceSupport(
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null)
{
    public Task<(bool Ok, string? Error)> AuthorizeReadAsync(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (scope is OrganizationScope.Platform or OrganizationScope.Organization)
            return AuthorizeAuthorizedScopeAsync(scope, permission, ct);

        if (scope is null)
            return Task.FromResult<(bool, string?)>((false, DiagnosticErrors.ScopeRequired));

        return Task.FromResult<(bool, string?)>((false, DiagnosticErrors.AccessDenied));
    }

    public Task<(bool Ok, string? Error)> AuthorizePlatformAsync(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (scope is null)
            return Task.FromResult<(bool, string?)>((false, DiagnosticErrors.ScopeRequired));
        if (scope is not OrganizationScope.Platform)
            return Task.FromResult<(bool, string?)>((false, DiagnosticErrors.AccessDenied));

        return AuthorizeAuthorizedScopeAsync(scope, permission, ct);
    }

    private async Task<(bool Ok, string? Error)> AuthorizeAuthorizedScopeAsync(
        OrganizationScope scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null || permissions is null)
            return (false, DiagnosticErrors.ActorRequired);

        TrustedActor? actor;
        try { actor = await actors.ResolveAsync(ct); }
        catch { return (false, DiagnosticErrors.PersistenceUnavailable); }
        if (actor is null)
            return (false, DiagnosticErrors.ActorRequired);

        PermissionDecision decision;
        try { decision = await permissions.EvaluateAsync(actor, scope, permission, ct); }
        catch { return (false, DiagnosticErrors.PersistenceUnavailable); }

        return decision.IsAllowed ? (true, null) : (false, DiagnosticErrors.AccessDenied);
    }
}
