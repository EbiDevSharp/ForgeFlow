using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Users;

namespace ForgeFlow.Application.Users.Slices;

/// <summary>
/// Operational vertical slice for organization and site membership assignments and activation states.
/// </summary>
public sealed class UserMembershipSlices(
    IUserOperationsStore store,
    UserSliceSupport support)
{
    public async Task<OrganizationResult<UserDetails>> SetOrganizationMembershipActiveAsync(
        OrganizationScope? scope,
        SetOrganizationMembershipActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.MembershipsManage;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!UserSliceSupport.OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else
        {
            try
            {
                if (!request.IsActive)
                    TenantAccessPolicy.EnsureNotSelfDeactivation(auth.Actor!.ActorId, request.UserId);

                if (UserSliceSupport.DecodeVersion(request.ExpectedMembershipRowVersion) is not byte[] version)
                    res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
                else
                    res = await UserSliceSupport.Execute(() => store.SetOrganizationMembershipActive(
                        request.OrganizationId, request.UserId, request.IsActive, version, support.Access(auth.Actor!, scope!, permission), ct));
            }
            catch (UserRuleException ex) { res = UserSliceSupport.FailFromRule<UserDetails>(ex); }
        }
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, null, "OrganizationMembership", request.UserId.ToString(CultureInfo.InvariantCulture), "SetOrganizationMembershipActive");
    }

    public async Task<OrganizationResult<UserDetails>> AssignSiteMembershipAsync(
        OrganizationScope? scope,
        AssignSiteMembership request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.MembershipsManage;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!UserSliceSupport.OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (request.SiteId <= 0)
            res = OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
        else
            res = await UserSliceSupport.Execute(() => store.AssignSiteMembership(request, support.Access(auth.Actor!, scope!, permission), ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null, "SiteMembership", request.UserId.ToString(CultureInfo.InvariantCulture), "AssignSiteMembership");
    }

    public async Task<OrganizationResult<UserDetails>> SetSiteMembershipActiveAsync(
        OrganizationScope? scope,
        SetSiteMembershipActive request,
        CancellationToken ct = default)
    {
        const string permission = UserPermissions.MembershipsManage;
        var auth = await support.AuthorizeAsync(scope, permission, ct);
        OrganizationResult<UserDetails> res;
        if (auth.Error is not null) res = OrganizationResult<UserDetails>.Failure(auth.Error);
        else if (!UserSliceSupport.OrganizationMatches(scope, request.OrganizationId) || request.UserId <= 0)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.NotFound);
        else if (request.SiteId <= 0)
            res = OrganizationResult<UserDetails>.Failure(OrganizationSiteErrors.SiteNotFound);
        else if (UserSliceSupport.DecodeVersion(request.ExpectedMembershipRowVersion) is not byte[] version)
            res = OrganizationResult<UserDetails>.Failure(UserManagementErrors.ConcurrencyConflict);
        else
            res = await UserSliceSupport.Execute(() => store.SetSiteMembershipActive(
                request.OrganizationId, request.UserId, request.SiteId, request.IsActive, version, support.Access(auth.Actor!, scope!, permission), ct));
        return await support.RecordRejectedAsync(res, auth.Actor, scope, request.OrganizationId, request.SiteId > 0 ? request.SiteId : null, "SiteMembership", request.UserId.ToString(CultureInfo.InvariantCulture), "SetSiteMembershipActive");
    }
}
