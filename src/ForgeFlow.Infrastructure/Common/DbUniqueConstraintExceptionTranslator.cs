using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using ForgeFlow.Domain.Users;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ForgeFlow.Infrastructure.Common;

/// <summary>
/// Shared database exception translator that catches raw SQL Server unique constraint / duplicate key violations
/// (error numbers 2601 and 2627) across EF Core write stores and translates them into clean, localized domain duplicate errors.
/// </summary>
public static class DbUniqueConstraintExceptionTranslator
{
    private static readonly Dictionary<string, string> IndexToDomainError = new(StringComparer.OrdinalIgnoreCase)
    {
        // Organizations & Sites
        ["UX_Organization_Code"] = OrganizationSiteErrors.OrganizationCodeDuplicate,
        ["UX_Site_Organization_Code"] = OrganizationSiteErrors.SiteCodeDuplicate,
        ["UX_Site_Org_Code"] = OrganizationSiteErrors.SiteCodeDuplicate,

        // Items & Catalog
        ["UX_Item_Organization_Code"] = ItemErrors.CodeDuplicate,
        ["UX_ClassificationSet_Org_Code"] = ItemErrors.ClassificationSetCodeDuplicate,
        ["UX_Classification_Org_Set_Code"] = ItemErrors.ClassificationCodeDuplicate,
        ["UX_AttributeDefinition_Org_Code"] = ItemErrors.AttributeCodeDuplicate,
        ["UX_ItemUnitConversion_Org_Item_From_To"] = ItemErrors.ItemUnitConversionDuplicate,

        // Units & Dimensions
        ["UX_Unit_Organization_Code"] = UnitErrors.CodeDuplicate,
        ["UX_UnitConversion_Organization_From_To"] = UnitErrors.ConversionDuplicate,
        ["UX_Currency_Organization_Code"] = UnitErrors.CurrencyCodeDuplicate,
        ["UX_ContextDimension_Org_Code"] = BomErrors.ContextDimensionCodeDuplicate,

        // BOMs
        ["UX_Bom_Org_Code"] = BomErrors.CodeDuplicate,
        ["UX_BomVersion_Org_Bom_Ver"] = BomErrors.VersionDuplicate,
        ["UX_BomApplicability_Org_Version"] = BomErrors.ConcurrencyConflict,
        ["UX_BomApplicabilityCondition_Dim_Op"] = BomErrors.ConcurrencyConflict,

        // Business Partners, Suppliers, Customers
        ["UX_BusinessPartner_Org_Code"] = BusinessPartnerErrors.DuplicateCode,
        ["UX_Supplier_Org_Code"] = ProcurementErrors.SupplierCodeDuplicate,
        ["UX_Customer_Org_Code"] = SalesErrors.CustomerCodeDuplicate,

        // Inventory & Warehouses
        ["UX_Warehouse_Org_Code"] = InventoryErrors.CodeDuplicate,
        ["UX_Warehouse_Org_Site_Default"] = InventoryErrors.CodeDuplicate,
        ["UX_InventoryTransaction_Org_DocNum"] = InventoryErrors.DocumentNumberDuplicate,
        ["UX_InventoryTransaction_Org_Reversal"] = InventoryErrors.AlreadyReversed,
        ["UX_InventoryTransaction_Org_Idempotency"] = InventoryErrors.ConcurrencyConflict,
        ["UX_InventoryLedgerEntry_Org_Txn_Line"] = InventoryErrors.ConcurrencyConflict,
        ["UX_InventoryBalance_Org_Wh_Item_Lot"] = InventoryErrors.ConcurrencyConflict,
        ["UX_ItemLot_Org_Item_Lot"] = InventoryErrors.ConcurrencyConflict,
        ["UX_StockReservation_Org_Doc_Line"] = InventoryErrors.ConcurrencyConflict,

        // Procurement
        ["UX_PurchaseOrder_Org_Number"] = ProcurementErrors.OrderNumberDuplicate,
        ["UX_PurchaseOrderLine_Org_Order_Line"] = ProcurementErrors.ConcurrencyConflict,

        // Production Orders
        ["UX_ProductionOrder_Org_Number"] = ProductionOrderErrors.OrderNumberDuplicate,
        ["UX_ProductionOrderOperation_Order_Seq"] = ProductionOrderErrors.ConcurrencyConflict,
        ["UX_ProductionOrderContext_Order_Dim"] = ProductionOrderErrors.ConcurrencyConflict,

        // Sales
        ["UX_SalesOrder_Org_Number"] = SalesErrors.OrderNumberDuplicate,
        ["UX_SalesOrderLine_Org_Order_Line"] = SalesErrors.ConcurrencyConflict,
        ["UX_DeliveryOrder_Org_Number"] = SalesErrors.DispatchNumberDuplicate,
        ["UX_DeliveryOrderLine_Org_Order_Line"] = SalesErrors.ConcurrencyConflict,

        // Quality
        ["UX_QualityInspection_Org_Number"] = QualityErrors.InspectionNumberDuplicate,
        ["UX_QualityInspectionLine_Org_Insp_Line"] = QualityErrors.ConcurrencyConflict,
        ["UX_QualityParameter_Org_Code"] = QualityErrors.ParameterCodeDuplicate,
        ["UX_QualitySpec_Org_Code_Version"] = QualityErrors.SpecCodeDuplicate,
        ["UX_QualitySpec_ActiveItem"] = QualityErrors.SpecActiveConflict,
        ["UX_QualitySpec_ActiveClass"] = QualityErrors.SpecActiveConflict,
        ["UX_QualitySpecLine_Spec_Param"] = QualityErrors.SpecRangeInvalid,
        ["UX_QualitySpecLine_Spec_Seq"] = QualityErrors.ConcurrencyConflict,

        // Routings & Work Centers
        ["UX_WorkCenter_Org_Site_Code"] = RoutingErrors.WorkCenterCodeDuplicate,
        ["UX_Routing_Org_Site_Code_Version"] = RoutingErrors.VersionDuplicate,
        ["UX_Routing_Org_Site_Item_Default"] = RoutingErrors.RoutingCodeDuplicate,
        ["UX_RoutingOperation_Routing_Seq"] = RoutingErrors.DuplicateSequence,

        // Identity / Auth
        ["UX_User_NormalizedUserName"] = UserManagementErrors.UserNameDuplicate,
        ["UX_RoleDefinition_Org_Name"] = RolePermissionErrors.NameDuplicate
    };

    /// <summary>
    /// Checks whether the exception or any of its inner exceptions is an EF Core or SQL Server
    /// unique constraint / duplicate key violation (SqlException error numbers 2601 or 2627).
    /// </summary>
    public static bool IsUniqueConstraintViolation(Exception? ex)
    {
        var current = ex;
        while (current is not null)
        {
            if (current is SqlException sql && sql.Number is 2601 or 2627)
                return true;
            current = current.InnerException;
        }
        return false;
    }

    /// <summary>
    /// Extracts the violated index or constraint name from the exception message hierarchy.
    /// </summary>
    public static string? ExtractConstraintName(Exception? ex)
    {
        var current = ex;
        while (current is not null)
        {
            var msg = current.Message;
            if (!string.IsNullOrEmpty(msg))
            {
                foreach (var indexName in IndexToDomainError.Keys)
                {
                    if (msg.Contains(indexName, StringComparison.OrdinalIgnoreCase))
                        return indexName;
                }
            }
            current = current.InnerException;
        }
        return null;
    }

    /// <summary>
    /// Translates a unique constraint violation into the corresponding domain duplicate error code.
    /// If no specific index is matched, returns the supplied fallback error code.
    /// </summary>
    public static string Translate(Exception ex, string fallbackErrorCode)
    {
        var constraint = ExtractConstraintName(ex);
        if (constraint is not null && IndexToDomainError.TryGetValue(constraint, out var domainError))
        {
            return domainError;
        }

        return fallbackErrorCode;
    }

    /// <summary>
    /// Attempts to translate a unique constraint violation into a domain error code.
    /// Returns true if the exception is a unique constraint violation and a mapping or fallback was found.
    /// </summary>
    public static bool TryTranslate(Exception ex, out string errorCode, string? fallbackErrorCode = null)
    {
        if (!IsUniqueConstraintViolation(ex))
        {
            errorCode = string.Empty;
            return false;
        }

        var constraint = ExtractConstraintName(ex);
        if (constraint is not null && IndexToDomainError.TryGetValue(constraint, out var domainError))
        {
            errorCode = domainError;
            return true;
        }

        if (fallbackErrorCode is not null)
        {
            errorCode = fallbackErrorCode;
            return true;
        }

        errorCode = string.Empty;
        return false;
    }
}
