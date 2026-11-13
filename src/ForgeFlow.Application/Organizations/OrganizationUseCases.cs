using System.Collections.ObjectModel;
using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations.Slices;

namespace ForgeFlow.Application.Organizations;

public sealed record OrganizationDetails(long Id, string Code, string Name, bool IsActive, string RowVersion);
public sealed record SiteDetails(long Id, long OrganizationId, string Code, string Name, bool IsActive, string RowVersion);
public sealed record OrganizationPage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record OrganizationListQuery(string? Search = null, bool IncludeInactive = false, int Page = 1, int PageSize = 25);
public sealed record CreateOrganization(string? Code, string? Name);
public sealed record CreateSite(long OrganizationId, string? Code, string? Name);
public sealed record RenameOrganization(long Id, string? Name, string? ExpectedRowVersion);
public sealed record SetOrganizationActive(long Id, bool IsActive, string? ExpectedRowVersion);
public sealed record RenameSite(long OrganizationId, long Id, string? Name, string? ExpectedRowVersion);
public sealed record SetSiteActive(long OrganizationId, long Id, bool IsActive, string? ExpectedRowVersion);

public sealed record OrganizationFailure(string Code, IReadOnlyDictionary<string, object> Parameters);
public sealed class OrganizationStoreUnavailableException(Exception inner) : Exception(OrganizationAccessErrors.PersistenceUnavailable, inner);
public sealed record OrganizationResult<T>(T? Value, OrganizationFailure? Error)
{
    public bool Succeeded => Error is null;
    public static OrganizationResult<T> Success(T value) => new(value, null);
    public static OrganizationResult<T> Failure(string code, IReadOnlyDictionary<string, object>? parameters = null)
        => new(default, new(code, new ReadOnlyDictionary<string, object>(
            parameters is null ? new Dictionary<string, object>() : new Dictionary<string, object>(parameters))));
}

public enum OrganizationMutation { Rename, Activate, Deactivate }

/// <summary>Attribution plus revalidation callback from the secured service; not a caller request DTO.</summary>
public sealed record OrganizationWriteAccess(TrustedActor Actor, Func<CancellationToken, ValueTask<bool>> Revalidate);

/// <summary>Specific persistence port, not a generic repository. Implementation must enforce owner predicates.</summary>
public interface IOrganizationOperationsStore
{
    Task<OrganizationPage<OrganizationDetails>> ListOrganizations(long organizationId, OrganizationListQuery query, CancellationToken ct);
    Task<OrganizationDetails?> GetOrganization(long organizationId, CancellationToken ct);
    Task<OrganizationPage<SiteDetails>> ListSites(long organizationId, long? siteId, OrganizationListQuery query, CancellationToken ct);
    Task<SiteDetails?> GetSite(long organizationId, long siteId, CancellationToken ct);
    Task<OrganizationResult<OrganizationDetails>> CreateOrganization(CreateOrganization request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SiteDetails>> CreateSite(CreateSite request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<OrganizationDetails>> UpdateOrganization(long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SiteDetails>> UpdateSite(long organizationId, long id, byte[] version, OrganizationMutation mutation, string? name, OrganizationWriteAccess access, CancellationToken ct);
}

/// <summary>
/// Secured facade for organization tenant companies and sites.
/// Delegates business logic to specialized vertical slices in <see cref="ForgeFlow.Application.Organizations.Slices"/>.
/// </summary>
public sealed class OrganizationUseCases
{
    private readonly CompanySlices companySlices;
    private readonly SiteSlices siteSlices;

    public OrganizationUseCases(
        IOrganizationOperationsStore store,
        ITrustedActorAccessor? actors = null,
        IOrganizationPermissionEvaluator? permissions = null,
        IUserOperationRecorder? operationRecorder = null,
        IOperationContextAccessor? opContext = null)
    {
        var support = new OrganizationSliceSupport(actors, permissions, operationRecorder, opContext);
        companySlices = new CompanySlices(store, support);
        siteSlices = new SiteSlices(store, support);
    }

    public Task<OrganizationResult<OrganizationPage<OrganizationDetails>>> ListOrganizations(OrganizationScope? scope, OrganizationListQuery query, CancellationToken ct = default)
        => companySlices.ListOrganizationsAsync(scope, query, ct);

    public Task<OrganizationResult<OrganizationDetails>> GetOrganization(OrganizationScope? scope, long id, CancellationToken ct = default)
        => companySlices.GetOrganizationAsync(scope, id, ct);

    public Task<OrganizationResult<OrganizationPage<SiteDetails>>> ListSites(OrganizationScope? scope, long organizationId, OrganizationListQuery query, CancellationToken ct = default)
        => siteSlices.ListSitesAsync(scope, organizationId, query, ct);

    public Task<OrganizationResult<SiteDetails>> GetSite(OrganizationScope? scope, long organizationId, long id, CancellationToken ct = default)
        => siteSlices.GetSiteAsync(scope, organizationId, id, ct);

    public Task<OrganizationResult<OrganizationDetails>> CreateOrganization(OrganizationScope? scope, CreateOrganization request, CancellationToken ct = default)
        => companySlices.CreateOrganizationAsync(scope, request, ct);

    public Task<OrganizationResult<SiteDetails>> CreateSite(OrganizationScope? scope, CreateSite request, CancellationToken ct = default)
        => siteSlices.CreateSiteAsync(scope, request, ct);

    public Task<OrganizationResult<OrganizationDetails>> RenameOrganization(OrganizationScope? scope, RenameOrganization request, CancellationToken ct = default)
        => companySlices.RenameOrganizationAsync(scope, request, ct);

    public Task<OrganizationResult<OrganizationDetails>> SetOrganizationActive(OrganizationScope? scope, SetOrganizationActive request, CancellationToken ct = default)
        => companySlices.SetOrganizationActiveAsync(scope, request, ct);

    public Task<OrganizationResult<SiteDetails>> RenameSite(OrganizationScope? scope, RenameSite request, CancellationToken ct = default)
        => siteSlices.RenameSiteAsync(scope, request, ct);

    public Task<OrganizationResult<SiteDetails>> SetSiteActive(OrganizationScope? scope, SetSiteActive request, CancellationToken ct = default)
        => siteSlices.SetSiteActiveAsync(scope, request, ct);
}
