using System.Globalization;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Routings;

namespace ForgeFlow.Application.Routings.Slices;

/// <summary>
/// Operational vertical slice for shop-floor ProductionOrderOperation execution and status progression.
/// </summary>
public sealed class RoutingOperationSlices(
    IRoutingOperationsStore store,
    RoutingSliceSupport support)
{
    public Task<OrganizationResult<ProductionOrderOperationSummary>> StartOperationAsync(
        OrganizationScope? scope,
        StartProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => ExecuteOperationCommandAsync(
            scope,
            request.OrganizationId,
            request.OperationId,
            request.ExpectedRowVersion,
            "StartOperation",
            (version, access) =>
            {
                _ = RoutingText.OptionalNotes(request.Notes);
                return store.StartOperation(request.OrganizationId, request.OperationId, version, request.Notes, DateTime.UtcNow, access, ct);
            },
            ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> PauseOperationAsync(
        OrganizationScope? scope,
        PauseProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => ExecuteOperationCommandAsync(
            scope,
            request.OrganizationId,
            request.OperationId,
            request.ExpectedRowVersion,
            "PauseOperation",
            (version, access) =>
            {
                _ = RoutingText.NonNegativeMinutes(request.ActualSetupMinutes);
                _ = RoutingText.NonNegativeMinutes(request.ActualRunMinutes);
                _ = RoutingText.NonNegativeQuantity(request.CompletedQuantity);
                _ = RoutingText.NonNegativeQuantity(request.ScrapQuantity);
                _ = RoutingText.OptionalNotes(request.Notes);
                return store.PauseOperation(request.OrganizationId, request.OperationId, version,
                    request.ActualSetupMinutes, request.ActualRunMinutes, request.CompletedQuantity, request.ScrapQuantity,
                    request.Notes, access, ct);
            },
            ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> CompleteOperationAsync(
        OrganizationScope? scope,
        CompleteProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => ExecuteOperationCommandAsync(
            scope,
            request.OrganizationId,
            request.OperationId,
            request.ExpectedRowVersion,
            "CompleteOperation",
            (version, access) =>
            {
                _ = RoutingText.NonNegativeMinutes(request.ActualSetupMinutes);
                _ = RoutingText.NonNegativeMinutes(request.ActualRunMinutes);
                _ = RoutingText.NonNegativeQuantity(request.CompletedQuantity);
                _ = RoutingText.NonNegativeQuantity(request.ScrapQuantity);
                _ = RoutingText.OptionalNotes(request.Notes);
                return store.CompleteOperation(request.OrganizationId, request.OperationId, version,
                    request.ActualSetupMinutes, request.ActualRunMinutes, request.CompletedQuantity, request.ScrapQuantity,
                    request.Notes, DateTime.UtcNow, access, ct);
            },
            ct);

    public Task<OrganizationResult<ProductionOrderOperationSummary>> SkipOperationAsync(
        OrganizationScope? scope,
        SkipProductionOrderOperationRequest request,
        CancellationToken ct = default)
        => ExecuteOperationCommandAsync(
            scope,
            request.OrganizationId,
            request.OperationId,
            request.ExpectedRowVersion,
            "SkipOperation",
            (version, access) =>
            {
                _ = RoutingText.OptionalNotes(request.Notes);
                return store.SkipOperation(request.OrganizationId, request.OperationId, version, request.Notes, DateTime.UtcNow, access, ct);
            },
            ct);

    private async Task<OrganizationResult<ProductionOrderOperationSummary>> ExecuteOperationCommandAsync(
        OrganizationScope? scope,
        long organizationId,
        long operationId,
        string? expectedRowVersion,
        string actionName,
        Func<byte[], OrganizationWriteAccess, Task<OrganizationResult<ProductionOrderOperationSummary>>> callback,
        CancellationToken ct)
    {
        var auth = await support.AuthorizeAsync(scope, RoutingPermissions.OperationsExecute, ct);
        OrganizationResult<ProductionOrderOperationSummary> res;
        if (auth.Error is not null) res = OrganizationResult<ProductionOrderOperationSummary>.Failure(auth.Error);
        else if (!RoutingSliceSupport.TryResolveScope(scope, organizationId, null, out var forcedSiteId) || operationId <= 0)
            res = OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.OperationNotFound);
        else if (RoutingSliceSupport.DecodeVersion(expectedRowVersion) is not byte[] version)
            res = OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.ConcurrencyConflict);
        else
            res = await RoutingSliceSupport.ExecuteStore(async () =>
            {
                var existing = await store.GetProductionOrderOperation(organizationId, operationId, ct);
                if (existing is null) return OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.OperationNotFound);
                if (forcedSiteId.HasValue && existing.SiteId != forcedSiteId.Value)
                    return OrganizationResult<ProductionOrderOperationSummary>.Failure(RoutingErrors.AccessDenied);

                return await callback(version, support.Access(auth.Actor!, scope!, RoutingPermissions.OperationsExecute));
            });

        await support.RecordRejectedAsync(res, auth.Actor, scope, organizationId, null,
            "ProductionOrderOperation", operationId.ToString(CultureInfo.InvariantCulture), actionName);
        return res;
    }
}
