using System.Globalization;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;

namespace ForgeFlow.Application.Organizations.Slices;

/// <summary>
/// Operational vertical slice for organization tenant listing, retrieval, onboarding, and metadata updates.
/// </summary>
public sealed class CompanySlices(
    IOrganizationOperationsStore store,
    OrganizationSliceSupport support)
{
    public async Task<OrganizationResult<OrganizationPage<OrganizationDetails>>> ListOrganizationsAsync(OrganizationScope? scope, OrganizationListQuery query, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, OrganizationPermissions.OrganizationsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<OrganizationDetails>>.Failure(auth.Error);
        if (scope is not OrganizationScope.Organization org) return OrganizationResult<OrganizationPage<OrganizationDetails>>.Failure(OrganizationAccessErrors.AccessDenied);
        return await OrganizationSliceSupport.Read(async () => OrganizationResult<OrganizationPage<OrganizationDetails>>.Success(await store.ListOrganizations(org.OrganizationId, OrganizationSliceSupport.Bound(query), ct)));
    }

    public async Task<OrganizationResult<OrganizationDetails>> GetOrganizationAsync(OrganizationScope? scope, long id, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAsync(scope, OrganizationPermissions.OrganizationsRead, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationDetails>.Failure(auth.Error);
        if (!OrganizationSliceSupport.OrganizationMatches(scope, id)) return OrganizationResult<OrganizationDetails>.Failure("Organization.NotFound");
        return await OrganizationSliceSupport.Read(async () => {
            var value = await store.GetOrganization(id, ct);
            return value is null ? OrganizationResult<OrganizationDetails>.Failure("Organization.NotFound") : OrganizationResult<OrganizationDetails>.Success(value);
        });
    }

    public async Task<OrganizationResult<OrganizationDetails>> CreateOrganizationAsync(OrganizationScope? scope, CreateOrganization request, CancellationToken ct = default)
    {
        const string permission = OrganizationPermissions.OrganizationsCreate;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<OrganizationDetails> res;
        if (auth.Error is not null) res = OrganizationResult<OrganizationDetails>.Failure(auth.Error);
        else if (scope is not OrganizationScope.Platform) res = OrganizationResult<OrganizationDetails>.Failure(OrganizationAccessErrors.AccessDenied);
        else res = await store.CreateOrganization(request, support.Access(auth.Actor!, scope, permission), ct);
        return await OperationAuditHelper.RecordIfRejectedAsync(res, support.OperationRecorder, support.OpContext,
            UserOperationCatalog.OrganizationActionRejected, auth.Actor, scope, null, null, "Organization", request.Code, new { Action = "CreateOrganization" });
    }

    public Task<OrganizationResult<OrganizationDetails>> RenameOrganizationAsync(OrganizationScope? scope, RenameOrganization request, CancellationToken ct = default)
        => ChangeOrganization(scope, request.Id, request.ExpectedRowVersion, OrganizationMutation.Rename, request.Name, ct);

    public Task<OrganizationResult<OrganizationDetails>> SetOrganizationActiveAsync(OrganizationScope? scope, SetOrganizationActive request, CancellationToken ct = default)
        => ChangeOrganization(scope, request.Id, request.ExpectedRowVersion, request.IsActive ? OrganizationMutation.Activate : OrganizationMutation.Deactivate, null, ct);

    private async Task<OrganizationResult<OrganizationDetails>> ChangeOrganization(OrganizationScope? scope, long id, string? expected, OrganizationMutation mutation, string? name, CancellationToken ct)
    {
        var permission = mutation == OrganizationMutation.Rename ? OrganizationPermissions.OrganizationsUpdate : OrganizationPermissions.OrganizationsSetActive;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<OrganizationDetails> res;
        if (auth.Error is not null) res = OrganizationResult<OrganizationDetails>.Failure(auth.Error);
        else if (!OrganizationSliceSupport.OrganizationMatches(scope, id)) res = OrganizationResult<OrganizationDetails>.Failure("Organization.NotFound");
        else if (OrganizationSliceSupport.Version(expected) is not byte[] version) res = OrganizationResult<OrganizationDetails>.Failure(OrganizationAccessErrors.ConcurrencyConflict);
        else res = await store.UpdateOrganization(id, version, mutation, name, support.Access(auth.Actor!, scope!, permission), ct);
        return await OperationAuditHelper.RecordIfRejectedAsync(res, support.OperationRecorder, support.OpContext,
            UserOperationCatalog.OrganizationActionRejected, auth.Actor, scope, id, null, "Organization",
            id.ToString(CultureInfo.InvariantCulture), new { Action = mutation.ToString() });
    }
}
