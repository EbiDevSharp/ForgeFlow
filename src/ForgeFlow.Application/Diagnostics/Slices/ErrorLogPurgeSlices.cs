using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Diagnostics.Slices;

/// <summary>
/// Platform-level maintenance slice for purging expired diagnostic error logs according to retention policy.
/// </summary>
public sealed class ErrorLogPurgeSlices(
    IErrorLogStore store,
    ErrorLogSliceSupport support,
    TimeProvider? clock = null)
{
    public async Task<OrganizationResult<int>> PurgeOldErrorLogsAsync(
        OrganizationScope? scope,
        int retainDays,
        CancellationToken ct = default)
    {
        var auth = await support.AuthorizePlatformAsync(scope, DiagnosticPermissions.ErrorLogsManage, ct);
        if (!auth.Ok) return OrganizationResult<int>.Failure(auth.Error!);
        if (retainDays is < 7 or > 3650)
            return OrganizationResult<int>.Failure(DiagnosticErrors.InvalidRetentionDays);

        try
        {
            var cutoff = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime.AddDays(-retainDays);
            var deleted = await store.PurgeBeforeAsync(cutoff, ct);
            return OrganizationResult<int>.Success(deleted);
        }
        catch (DiagnosticStoreUnavailableException)
        {
            return OrganizationResult<int>.Failure(DiagnosticErrors.PersistenceUnavailable);
        }
    }
}
