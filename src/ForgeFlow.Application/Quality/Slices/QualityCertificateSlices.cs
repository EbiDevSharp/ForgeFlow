using ForgeFlow.Application.Inventory;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.ProductionOrders;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Quality.Slices;

/// <summary>
/// Operational vertical slice for Certificate of Analysis (COA) and lot inspection certificate resolution.
/// </summary>
public sealed class QualityCertificateSlices(
    IQualitySpecOperationsStore store,
    QualitySpecSliceSupport support)
{
    public async Task<OrganizationResult<LotCertificateSummary>> GetLotCertificateAsync(
        OrganizationScope? scope, long organizationId, long itemId, string? lotCode, CancellationToken ct = default)
    {
        var auth = await support.AuthorizeAnyAsync(scope, ct, QualityPermissions.QualityRead, InventoryPermissions.InventoryRead, ProductionOrderPermissions.ProductionOrdersRead, "Sales.Read");
        if (auth.Error is not null) return OrganizationResult<LotCertificateSummary>.Failure(auth.Error);
        if (!QualitySpecSliceSupport.TryResolveOrgScope(scope, organizationId)) return OrganizationResult<LotCertificateSummary>.Failure(QualityErrors.AccessDenied);
        try
        {
            var normalizedLot = QualityText.RequiredLotCode(lotCode);
            if (itemId <= 0) return OrganizationResult<LotCertificateSummary>.Failure(QualityErrors.ItemInvalid);
            var cert = await store.GetLotCertificate(organizationId, itemId, normalizedLot, ct);
            return cert is null ? OrganizationResult<LotCertificateSummary>.Failure(QualityErrors.LotNotFound) : OrganizationResult<LotCertificateSummary>.Success(cert);
        }
        catch (QualityRuleException ex) { return OrganizationResult<LotCertificateSummary>.Failure(ex.Code, ex.Parameters.ToDictionary(kv => kv.Key, kv => kv.Value)); }
        catch (QualityStoreUnavailableException) { return OrganizationResult<LotCertificateSummary>.Failure(QualityErrors.PersistenceUnavailable); }
    }
}
