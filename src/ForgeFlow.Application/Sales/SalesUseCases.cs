using ForgeFlow.Application.Common;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Sales;

/// <summary>
/// Secured use cases for Organization-shared Customers, Site-scoped Sales Orders,
/// and atomic Delivery Orders (Dispatches) with Expiry, Quarantine, Reservation, and Lot COA gates.
/// </summary>
public sealed class SalesUseCases(
    ISalesOperationsStore store,
    ITrustedActorAccessor? actors = null,
    IOrganizationPermissionEvaluator? permissions = null,
    IUserOperationRecorder? operationRecorder = null,
    IOperationContextAccessor? opContext = null,
    TimeProvider? clock = null)
{
    private DateTime UtcNow => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private async ValueTask<(TrustedActor? Actor, string? Error)> Authorize(
        OrganizationScope? scope,
        string permission,
        CancellationToken ct)
    {
        if (actors is null) return (null, SalesErrors.ActorRequired);
        var actor = await actors.ResolveAsync(ct);
        if (actor is null) return (null, SalesErrors.ActorRequired);
        if (scope is null) return (null, SalesErrors.ScopeRequired);
        if (permissions is null || !(await permissions.EvaluateAsync(actor, scope, permission, ct)).IsAllowed)
            return (null, SalesErrors.AccessDenied);
        return (actor, null);
    }

    private OrganizationWriteAccess Access(TrustedActor actor, OrganizationScope scope, string permission) => new(actor, async ct =>
    {
        var current = await Authorize(scope, permission, ct);
        return current.Error is null
            && current.Actor!.ActorId == actor.ActorId
            && current.Actor.Provenance == actor.Provenance
            && current.Actor.ProvenanceReference == actor.ProvenanceReference;
    });

    private static bool TryResolveSiteScope(OrganizationScope? scope, long requestedOrgId, long? requestedSiteId, out long? forcedSiteId)
    {
        forcedSiteId = null;
        if (requestedOrgId <= 0) return false;
        switch (scope)
        {
            case OrganizationScope.Organization org when org.OrganizationId == requestedOrgId:
                forcedSiteId = requestedSiteId;
                return true;
            case OrganizationScope.Site site when site.OrganizationId == requestedOrgId:
                if (requestedSiteId.HasValue && requestedSiteId.Value != site.SiteId)
                    return false;
                forcedSiteId = site.SiteId;
                return true;
            default:
                return false;
        }
    }

    private static bool MatchesOrganizationOnly(OrganizationScope? scope, long organizationId)
        => scope is OrganizationScope.Organization org && org.OrganizationId == organizationId && organizationId > 0;

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

    private static async Task<OrganizationResult<T>> ExecuteStore<T>(Func<Task<OrganizationResult<T>>> action)
    {
        try { return await action(); }
        catch (SalesRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (InventoryRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (QualityRuleException ex) { return OrganizationResult<T>.Failure(ex.Code, ex.Parameters); }
        catch (UnitRuleException ex)
        {
            return OrganizationResult<T>.Failure(
                ex.Code == UnitErrors.ConversionQuantityInvalid ? SalesErrors.QuantityInvalid : SalesErrors.LineUnitInvalid,
                ex.Parameters);
        }
        catch (SalesStoreUnavailableException) { return OrganizationResult<T>.Failure(SalesErrors.PersistenceUnavailable); }
    }

    private async Task<OrganizationResult<T>> AuditFailureIfRejected<T>(
        OrganizationResult<T> result,
        TrustedActor? actor,
        OrganizationScope? scope,
        long? orgId,
        long? siteId,
        string targetType,
        string? targetRef,
        string actionName)
    {
        if (result.Succeeded || result.Error is null)
            return result;

        await OperationAuditHelper.RecordAsync(
            operationRecorder,
            opContext,
            UserOperationCatalog.SalesActionRejected,
            OperationAuditHelper.MapFailureOutcome(result.Error.Code),
            actor,
            scope,
            orgId,
            siteId,
            targetType,
            targetRef,
            result.Error.Code,
            new { Action = actionName, Error = result.Error.Code });

        return result;
    }

    public async Task<OrganizationResult<OrganizationPage<CustomerSummary>>> ListCustomers(
        OrganizationScope? scope,
        long organizationId,
        CustomerListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<OrganizationPage<CustomerSummary>>.Failure(auth.Error);

        if (!TryResolveSiteScope(scope, organizationId, null, out _))
            return OrganizationResult<OrganizationPage<CustomerSummary>>.Failure(SalesErrors.AccessDenied);

        var bounded = query with
        {
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
        };

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<CustomerSummary>>.Success(
                await store.ListCustomers(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<CustomerSummary>> CreateCustomer(
        OrganizationScope? scope,
        CreateCustomerRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.ManageCustomers, ct);
        OrganizationResult<CustomerSummary> result;

        if (auth.Error is not null)
            result = OrganizationResult<CustomerSummary>.Failure(auth.Error);
        else if (!MatchesOrganizationOnly(scope, request.OrganizationId))
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.AccessDenied);
        else
            result = await ExecuteStore(() =>
                store.CreateCustomer(request, UtcNow, Access(auth.Actor!, scope!, SalesPermissions.ManageCustomers), ct));

        return await AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, null, "Customer", request.Code, nameof(CreateCustomer));
    }

    public async Task<OrganizationResult<CustomerSummary>> UpdateCustomer(
        OrganizationScope? scope,
        long organizationId,
        long customerId,
        UpdateCustomerRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.ManageCustomers, ct);
        OrganizationResult<CustomerSummary> result;

        if (auth.Error is not null)
            result = OrganizationResult<CustomerSummary>.Failure(auth.Error);
        else if (!MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.AccessDenied);
        else if (customerId <= 0)
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.CustomerNotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await ExecuteStore(() =>
                store.UpdateCustomer(organizationId, customerId, request, version, UtcNow, Access(auth.Actor!, scope!, SalesPermissions.ManageCustomers), ct));

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, null, "Customer", customerId.ToString(), nameof(UpdateCustomer));
    }

    public async Task<OrganizationResult<CustomerSummary>> SetCustomerActive(
        OrganizationScope? scope,
        long organizationId,
        long customerId,
        bool isActive,
        string? expectedVersion,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.ManageCustomers, ct);
        OrganizationResult<CustomerSummary> result;

        if (auth.Error is not null)
            result = OrganizationResult<CustomerSummary>.Failure(auth.Error);
        else if (!MatchesOrganizationOnly(scope, organizationId))
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.AccessDenied);
        else if (customerId <= 0)
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.CustomerNotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<CustomerSummary>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await ExecuteStore(() =>
                store.SetCustomerActive(organizationId, customerId, isActive, version, UtcNow, Access(auth.Actor!, scope!, SalesPermissions.ManageCustomers), ct));

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, null, "Customer", customerId.ToString(), nameof(SetCustomerActive));
    }

    public async Task<OrganizationResult<OrganizationPage<SalesOrderSummary>>> ListSalesOrders(
        OrganizationScope? scope,
        long organizationId,
        SalesOrderListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<OrganizationPage<SalesOrderSummary>>.Failure(auth.Error);

        if (!TryResolveSiteScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<SalesOrderSummary>>.Failure(SalesErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
        };

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<SalesOrderSummary>>.Success(
                await store.ListSalesOrders(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<SalesOrderDetails>> GetSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<SalesOrderDetails>.Failure(auth.Error);

        if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);

        if (salesOrderId <= 0)
            return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);

        return await ExecuteStore(async () =>
        {
            var details = await store.GetSalesOrder(organizationId, salesOrderId, ct);
            if (details is null)
                return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
            if (forcedSiteId.HasValue && details.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);

            return OrganizationResult<SalesOrderDetails>.Success(details);
        });
    }

    public async Task<OrganizationResult<SalesOrderDetails>> CreateSalesOrder(
        OrganizationScope? scope,
        CreateSalesOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.ManageOrders, ct);
        OrganizationResult<SalesOrderDetails> result;

        if (auth.Error is not null)
            result = OrganizationResult<SalesOrderDetails>.Failure(auth.Error);
        else if (!TryResolveSiteScope(scope, request.OrganizationId, request.SiteId, out var resolvedSiteId) || resolvedSiteId is not > 0)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);
        else
            result = await ExecuteStore(async () =>
            {
                var linesResult = await ResolveOrderLinesAsync(request.OrganizationId, request.Lines, ct);
                if (!linesResult.Succeeded)
                    return OrganizationResult<SalesOrderDetails>.Failure(linesResult.Error!.Code, linesResult.Error.Parameters);

                return await store.CreateSalesOrder(
                    request with { SiteId = resolvedSiteId.Value },
                    linesResult.Value!,
                    UtcNow,
                    Access(auth.Actor!, scope!, SalesPermissions.ManageOrders),
                    ct);
            });

        return await AuditFailureIfRejected(result, auth.Actor, scope, request.OrganizationId, request.SiteId, "SalesOrder", request.OrderNumber, nameof(CreateSalesOrder));
    }

    public async Task<OrganizationResult<SalesOrderDetails>> UpdateSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        UpdateSalesOrderRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.ManageOrders, ct);
        OrganizationResult<SalesOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<SalesOrderDetails>.Failure(auth.Error);
        else if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);
        else if (salesOrderId <= 0)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await ExecuteStore(async () =>
            {
                var existing = await store.GetSalesOrder(organizationId, salesOrderId, ct);
                if (existing is null)
                    return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                var linesResult = await ResolveOrderLinesAsync(organizationId, request.Lines, ct);
                if (!linesResult.Succeeded)
                    return OrganizationResult<SalesOrderDetails>.Failure(linesResult.Error!.Code, linesResult.Error.Parameters);

                return await store.UpdateSalesOrder(
                    organizationId,
                    salesOrderId,
                    request,
                    linesResult.Value!,
                    version,
                    UtcNow,
                    Access(auth.Actor!, scope!, SalesPermissions.ManageOrders),
                    ct);
            });

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "SalesOrder", salesOrderId.ToString(), nameof(UpdateSalesOrder));
    }

    public Task<OrganizationResult<SalesOrderDetails>> ApproveSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        string? expectedVersion,
        CancellationToken ct = default)
        => TransitionOrderAsync(scope, organizationId, salesOrderId, expectedVersion, nameof(ApproveSalesOrder),
            (ver, access) => store.ApproveSalesOrder(organizationId, salesOrderId, ver, UtcNow, access, ct), ct);

    public Task<OrganizationResult<SalesOrderDetails>> CloseSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        string? expectedVersion,
        CancellationToken ct = default)
        => TransitionOrderAsync(scope, organizationId, salesOrderId, expectedVersion, nameof(CloseSalesOrder),
            (ver, access) => store.CloseSalesOrder(organizationId, salesOrderId, ver, UtcNow, access, ct), ct);

    public Task<OrganizationResult<SalesOrderDetails>> CancelSalesOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        CancelSalesOrderRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
        => TransitionOrderAsync(scope, organizationId, salesOrderId, expectedVersion, nameof(CancelSalesOrder),
            (ver, access) =>
            {
                var reason = SalesText.RequiredReason(request?.Reason);
                return store.CancelSalesOrder(organizationId, salesOrderId, reason, ver, UtcNow, access, ct);
            }, ct);

    public async Task<OrganizationResult<OrganizationPage<DeliveryOrderSummary>>> ListDeliveryOrders(
        OrganizationScope? scope,
        long organizationId,
        DeliveryOrderListQuery query,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<OrganizationPage<DeliveryOrderSummary>>.Failure(auth.Error);

        if (!TryResolveSiteScope(scope, organizationId, query.SiteId, out var forcedSiteId))
            return OrganizationResult<OrganizationPage<DeliveryOrderSummary>>.Failure(SalesErrors.AccessDenied);

        var bounded = query with
        {
            SiteId = forcedSiteId,
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()
        };

        return await ExecuteStore(async () =>
            OrganizationResult<OrganizationPage<DeliveryOrderSummary>>.Success(
                await store.ListDeliveryOrders(organizationId, bounded, ct)));
    }

    public async Task<OrganizationResult<DeliveryOrderDetails>> GetDeliveryOrder(
        OrganizationScope? scope,
        long organizationId,
        long deliveryOrderId,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<DeliveryOrderDetails>.Failure(auth.Error);

        if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);

        if (deliveryOrderId <= 0)
            return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNotFound);

        return await ExecuteStore(async () =>
        {
            var details = await store.GetDeliveryOrder(organizationId, deliveryOrderId, ct);
            if (details is null)
                return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNotFound);
            if (forcedSiteId.HasValue && details.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);

            return OrganizationResult<DeliveryOrderDetails>.Success(details);
        });
    }

    public async Task<OrganizationResult<IReadOnlyList<DispatchableLotSummary>>> ListDispatchableLots(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        long warehouseId,
        DateOnly? asOfDate = null,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.SalesRead, ct);
        if (auth.Error is not null)
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(auth.Error);

        if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.AccessDenied);

        if (salesOrderId <= 0)
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.NotFound);
        if (warehouseId <= 0)
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.WarehouseInvalid);

        return await ExecuteStore(async () =>
        {
            var order = await store.GetSalesOrder(organizationId, salesOrderId, ct);
            if (order is null)
                return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.NotFound);
            if (forcedSiteId.HasValue && order.Summary.SiteId != forcedSiteId.Value)
                return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Failure(SalesErrors.AccessDenied);

            var effectiveDate = asOfDate ?? DateOnly.FromDateTime(UtcNow);
            var lots = await store.ListDispatchableLots(organizationId, salesOrderId, warehouseId, effectiveDate, ct);
            return OrganizationResult<IReadOnlyList<DispatchableLotSummary>>.Success(lots);
        });
    }

    public async Task<OrganizationResult<DeliveryOrderDetails>> PostDeliveryOrder(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        PostDeliveryOrderRequest request,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.Dispatch, ct);
        OrganizationResult<DeliveryOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(auth.Error);
        else if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);
        else if (salesOrderId <= 0)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.NotFound);
        else if (request.WarehouseId <= 0)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.WarehouseInvalid);
        else if (request.Lines is null || request.Lines.Count == 0)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.EmptyDispatch);
        else
            result = await ExecuteStore(async () =>
            {
                var existing = await store.GetSalesOrder(organizationId, salesOrderId, ct);
                if (existing is null)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                var resolvedLines = await ResolveDispatchLinesAsync(organizationId, existing.Lines, request.Lines, ct);
                if (!resolvedLines.Succeeded)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(resolvedLines.Error!.Code, resolvedLines.Error.Parameters);

                return await store.PostDeliveryOrder(
                    organizationId,
                    salesOrderId,
                    request,
                    resolvedLines.Value!,
                    UtcNow,
                    Access(auth.Actor!, scope!, SalesPermissions.Dispatch),
                    ct);
            });

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "DeliveryOrder", request.DispatchNumber, nameof(PostDeliveryOrder));
    }

    public async Task<OrganizationResult<DeliveryOrderDetails>> ReverseDeliveryOrder(
        OrganizationScope? scope,
        long organizationId,
        long deliveryOrderId,
        ReverseDeliveryOrderRequest request,
        string? expectedVersion,
        CancellationToken ct = default)
    {
        var auth = await Authorize(scope, SalesPermissions.Dispatch, ct);
        OrganizationResult<DeliveryOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(auth.Error);
        else if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);
        else if (deliveryOrderId <= 0)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await ExecuteStore(async () =>
            {
                var existing = await store.GetDeliveryOrder(organizationId, deliveryOrderId, ct);
                if (existing is null)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.DispatchNotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<DeliveryOrderDetails>.Failure(SalesErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                var reason = SalesText.RequiredReason(request?.Reason);
                return await store.ReverseDeliveryOrder(
                    organizationId,
                    deliveryOrderId,
                    reason,
                    version,
                    UtcNow,
                    Access(auth.Actor!, scope!, SalesPermissions.Dispatch),
                    ct);
            });

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "DeliveryOrder", deliveryOrderId.ToString(), nameof(ReverseDeliveryOrder));
    }

    private async Task<OrganizationResult<SalesOrderDetails>> TransitionOrderAsync(
        OrganizationScope? scope,
        long organizationId,
        long salesOrderId,
        string? expectedVersion,
        string actionName,
        Func<byte[], OrganizationWriteAccess, Task<OrganizationResult<SalesOrderDetails>>> operation,
        CancellationToken ct)
    {
        var auth = await Authorize(scope, SalesPermissions.ManageOrders, ct);
        OrganizationResult<SalesOrderDetails> result;
        long? siteId = null;

        if (auth.Error is not null)
            result = OrganizationResult<SalesOrderDetails>.Failure(auth.Error);
        else if (!TryResolveSiteScope(scope, organizationId, null, out var forcedSiteId))
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);
        else if (salesOrderId <= 0)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
        else if (DecodeVersion(expectedVersion) is not byte[] version)
            result = OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.ConcurrencyConflict);
        else
            result = await ExecuteStore(async () =>
            {
                var existing = await store.GetSalesOrder(organizationId, salesOrderId, ct);
                if (existing is null)
                    return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.NotFound);
                if (forcedSiteId.HasValue && existing.Summary.SiteId != forcedSiteId.Value)
                    return OrganizationResult<SalesOrderDetails>.Failure(SalesErrors.AccessDenied);

                siteId = existing.Summary.SiteId;
                return await operation(version, Access(auth.Actor!, scope!, SalesPermissions.ManageOrders));
            });

        return await AuditFailureIfRejected(result, auth.Actor, scope, organizationId, siteId, "SalesOrder", salesOrderId.ToString(), actionName);
    }

    private async Task<OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>> ResolveOrderLinesAsync(
        long organizationId,
        IReadOnlyList<SalesOrderLineRequest>? inputs,
        CancellationToken ct)
    {
        if (inputs is null || inputs.Count == 0)
            return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.EmptyOrder);

        var seenItems = new HashSet<long>();
        foreach (var line in inputs)
        {
            if (line.ItemId <= 0)
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.ItemInvalid);
            if (!seenItems.Add(line.ItemId))
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.DuplicateItem);
        }

        var itemIds = inputs.Select(l => l.ItemId).Distinct().ToArray();
        var itemContexts = await store.LoadItemContexts(organizationId, itemIds, ct);

        var requestedUnitIds = inputs.Where(l => l.OrderedUnitId > 0).Select(l => l.OrderedUnitId).Distinct().ToArray();
        var activeUnitIds = requestedUnitIds.Length > 0
            ? await store.LoadActiveUnitIds(organizationId, requestedUnitIds, ct)
            : new HashSet<long>();

        var resolved = new List<ResolvedSalesOrderLine>(inputs.Count);
        foreach (var line in inputs)
        {
            if (!itemContexts.TryGetValue(line.ItemId, out var ctx) || !ctx.ItemIsActive)
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.ItemInvalid);
            if (!ctx.CanBeSold)
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.ItemNotSellable);

            var unitId = line.OrderedUnitId > 0 ? line.OrderedUnitId : ctx.BaseUnitId;
            if (unitId != ctx.BaseUnitId && !activeUnitIds.Contains(unitId))
                return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Failure(SalesErrors.LineUnitInvalid);

            var qty = SalesText.PositiveQuantity(line.OrderedQuantity);
            var baseQty = unitId == ctx.BaseUnitId
                ? qty
                : UnitConversionGraph.ConvertQuantity(qty, unitId, ctx.BaseUnitId, ctx.Edges).ConvertedQuantity;
            var price = SalesText.OptionalUnitPrice(line.UnitPrice);
            var notes = SalesText.OptionalText(line.Notes, SalesText.MaxLineNotesLength, SalesErrors.NotesInvalid);

            resolved.Add(new ResolvedSalesOrderLine(
                line.ItemId,
                qty,
                unitId,
                ctx.BaseUnitId,
                baseQty,
                price,
                notes));
        }

        return OrganizationResult<IReadOnlyList<ResolvedSalesOrderLine>>.Success(resolved);
    }

    private async Task<OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>> ResolveDispatchLinesAsync(
        long organizationId,
        IReadOnlyList<SalesOrderLineDetails> orderLines,
        IReadOnlyList<PostDeliveryOrderLineRequest> inputs,
        CancellationToken ct)
    {
        var orderLineById = orderLines.ToDictionary(l => l.Id);
        var itemIds = orderLines.Select(l => l.ItemId).Distinct().ToArray();
        var itemContexts = await store.LoadItemContexts(organizationId, itemIds, ct);

        var requestedUnitIds = inputs.Where(l => l.UnitId is > 0).Select(l => l.UnitId!.Value).Distinct().ToArray();
        var activeUnitIds = requestedUnitIds.Length > 0
            ? await store.LoadActiveUnitIds(organizationId, requestedUnitIds, ct)
            : new HashSet<long>();

        var resolved = new List<ResolvedDeliveryOrderLine>(inputs.Count);
        foreach (var line in inputs)
        {
            if (!orderLineById.TryGetValue(line.SalesOrderLineId, out var orderLine))
                return OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>.Failure(SalesErrors.LineNotFound);
            if (!itemContexts.TryGetValue(orderLine.ItemId, out var ctx) || !ctx.ItemIsActive)
                return OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>.Failure(SalesErrors.ItemInvalid);

            var lotCode = SalesText.RequiredLotCode(line.LotCode);
            var unitId = line.UnitId is > 0 ? line.UnitId.Value : orderLine.BaseUnitIdSnapshot;
            if (unitId != orderLine.BaseUnitIdSnapshot && !activeUnitIds.Contains(unitId))
                return OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>.Failure(SalesErrors.LineUnitInvalid);

            var qty = SalesText.PositiveQuantity(line.DispatchedQuantity);
            var baseQty = unitId == orderLine.BaseUnitIdSnapshot
                ? qty
                : UnitConversionGraph.ConvertQuantity(qty, unitId, orderLine.BaseUnitIdSnapshot, ctx.Edges).ConvertedQuantity;
            var notes = SalesText.OptionalText(line.Notes, SalesText.MaxLineNotesLength, SalesErrors.NotesInvalid);

            resolved.Add(new ResolvedDeliveryOrderLine(
                orderLine.Id,
                orderLine.ItemId,
                lotCode,
                baseQty,
                orderLine.BaseUnitIdSnapshot,
                notes));
        }

        return OrganizationResult<IReadOnlyList<ResolvedDeliveryOrderLine>>.Success(resolved);
    }
}
