using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.BusinessPartners;

namespace ForgeFlow.Application.BusinessPartners;

public sealed class BusinessPartnerUseCases
{
    private readonly IBusinessPartnerStore _store;
    private readonly ITrustedActorAccessor? _actors;
    private readonly IOrganizationPermissionEvaluator? _permissions;
    private readonly IUserOperationRecorder? _operationRecorder;
    private readonly IOperationContextAccessor? _opContext;
    private readonly TimeProvider _clock;

    public BusinessPartnerUseCases(
        IBusinessPartnerStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null,
        TimeProvider? clock = null)
    {
        _store = store;
        _actors = actors;
        _permissions = permissions;
        _operationRecorder = operationRecorder;
        _opContext = opContext;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<OrganizationResult<OrganizationPage<BusinessPartnerSummary>>> List(
        OrganizationScope? scope,
        long organizationId,
        BusinessPartnerListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BusinessPartnerPermissions.Read, ct);
        if (auth.Error is not null) return OrganizationResult<OrganizationPage<BusinessPartnerSummary>>.Failure(auth.Error);
        if (!MatchesOrg(scope, organizationId))
            return OrganizationResult<OrganizationPage<BusinessPartnerSummary>>.Failure(BusinessPartnerErrors.AccessDenied);

        try
        {
            var bound = new BusinessPartnerListQuery(
                query.Search?.Trim(),
                query.RoleFilter,
                query.IncludeInactive,
                Math.Clamp(query.Page, 1, 1_000_000),
                Math.Clamp(query.PageSize, 1, 100));
            return OrganizationResult<OrganizationPage<BusinessPartnerSummary>>.Success(await _store.List(organizationId, bound, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OrganizationResult<OrganizationPage<BusinessPartnerSummary>>.Failure("BusinessPartner.PersistenceUnavailable");
        }
    }

    public async Task<OrganizationResult<BusinessPartnerDetail>> Get(
        OrganizationScope? scope,
        long organizationId,
        long id,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BusinessPartnerPermissions.Read, ct);
        if (auth.Error is not null) return OrganizationResult<BusinessPartnerDetail>.Failure(auth.Error);
        if (!MatchesOrg(scope, organizationId))
            return OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.AccessDenied);
        if (id <= 0)
            return OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.NotFound);

        try
        {
            var found = await _store.Get(organizationId, id, ct);
            return found is null
                ? OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.NotFound)
                : OrganizationResult<BusinessPartnerDetail>.Success(found);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OrganizationResult<BusinessPartnerDetail>.Failure("BusinessPartner.PersistenceUnavailable");
        }
    }

    public async Task<IReadOnlyList<BusinessPartnerRoleDto>> ListRoleDefinitions(CancellationToken ct = default)
    {
        try
        {
            return await _store.ListRoleDefinitions(ct);
        }
        catch
        {
            return
            [
                new(BusinessPartnerRoleDefinition.RoleSupplier, "SUPPLIER", "Supplier"),
                new(BusinessPartnerRoleDefinition.RoleCustomer, "CUSTOMER", "Customer"),
                new(BusinessPartnerRoleDefinition.RoleContractor, "CONTRACTOR", "Contractor")
            ];
        }
    }

    public async Task<OrganizationResult<BusinessPartnerDetail>> Create(
        OrganizationScope? scope,
        CreateBusinessPartnerRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BusinessPartnerPermissions.Manage, ct);
        OrganizationResult<BusinessPartnerDetail> result;
        if (auth.Error is not null) result = OrganizationResult<BusinessPartnerDetail>.Failure(auth.Error);
        else if (!MatchesOrganizationOnly(scope, request.OrganizationId))
            result = OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.AccessDenied);
        else
        {
            try
            {
                result = await _store.Create(request, Access(auth.Actor!, scope!, BusinessPartnerPermissions.Manage), ct);
            }
            catch (BusinessPartnerRuleException ex)
            {
                result = OrganizationResult<BusinessPartnerDetail>.Failure(ex.Code);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = OrganizationResult<BusinessPartnerDetail>.Failure("BusinessPartner.PersistenceUnavailable");
            }
        }

        return await AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, "BusinessPartner", request.Code ?? "AUTO", nameof(Create));
    }

    public async Task<OrganizationResult<BusinessPartnerDetail>> Update(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        UpdateBusinessPartnerRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BusinessPartnerPermissions.Manage, ct);
        OrganizationResult<BusinessPartnerDetail> result;
        if (auth.Error is not null) result = OrganizationResult<BusinessPartnerDetail>.Failure(auth.Error);
        else if (!MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.NotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<BusinessPartnerDetail>.Failure(BusinessPartnerErrors.Conflict);
        else
        {
            try
            {
                result = await _store.Update(organizationId, id, ver, request, Access(auth.Actor!, scope!, BusinessPartnerPermissions.Manage), ct);
            }
            catch (BusinessPartnerRuleException ex)
            {
                result = OrganizationResult<BusinessPartnerDetail>.Failure(ex.Code);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = OrganizationResult<BusinessPartnerDetail>.Failure("BusinessPartner.PersistenceUnavailable");
            }
        }

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, "BusinessPartner", id.ToString(), nameof(Update));
    }

    public async Task<OrganizationResult<BusinessPartnerSummary>> SetActive(
        OrganizationScope? scope,
        long organizationId,
        long id,
        string? expectedVersion,
        bool isActive,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, BusinessPartnerPermissions.Manage, ct);
        OrganizationResult<BusinessPartnerSummary> result;
        if (auth.Error is not null) result = OrganizationResult<BusinessPartnerSummary>.Failure(auth.Error);
        else if (!MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<BusinessPartnerSummary>.Failure(BusinessPartnerErrors.AccessDenied);
        else if (id <= 0)
            result = OrganizationResult<BusinessPartnerSummary>.Failure(BusinessPartnerErrors.NotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] ver)
            result = OrganizationResult<BusinessPartnerSummary>.Failure(BusinessPartnerErrors.Conflict);
        else
        {
            try
            {
                result = await _store.SetActive(organizationId, id, ver, isActive, Access(auth.Actor!, scope!, BusinessPartnerPermissions.Manage), ct);
            }
            catch (BusinessPartnerRuleException ex)
            {
                result = OrganizationResult<BusinessPartnerSummary>.Failure(ex.Code);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = OrganizationResult<BusinessPartnerSummary>.Failure("BusinessPartner.PersistenceUnavailable");
            }
        }

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, "BusinessPartner", id.ToString(), nameof(SetActive));
    }

    private static bool MatchesOrg(OrganizationScope? scope, long orgId) => scope switch
    {
        OrganizationScope.Organization o => o.OrganizationId == orgId,
        OrganizationScope.Site s => s.OrganizationId == orgId,
        _ => false
    };

    private static bool MatchesOrganizationOnly(OrganizationScope? scope, long orgId) =>
        scope is OrganizationScope.Organization o && o.OrganizationId == orgId;

    private static byte[]? DecodeVersion(string? raw)
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

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (scope is null) return (null, BusinessPartnerErrors.AccessDenied);
        if (_actors is null) return (null, BusinessPartnerErrors.AccessDenied);

        var actor = await _actors.ResolveAsync(ct);
        if (actor is null) return (null, BusinessPartnerErrors.AccessDenied);
        if (_permissions is null || !(await _permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, BusinessPartnerErrors.AccessDenied);

        return (actor, null);
    }

    private async Task<OrganizationResult<T>> AuditFailureIfRejected<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long orgId,
        string targetType,
        string? targetRef,
        string actionName)
    {
        if (result.Succeeded || result.Error is null)
            return result;

        await OperationAuditHelper.RecordAsync(
            _operationRecorder,
            _opContext,
            UserOperationCatalog.BusinessPartnerActionRejected,
            OperationAuditHelper.MapFailureOutcome(result.Error.Code),
            actor,
            scope,
            orgId,
            null,
            targetType,
            targetRef,
            result.Error.Code,
            new { Action = actionName });
        return result;
    }
}
