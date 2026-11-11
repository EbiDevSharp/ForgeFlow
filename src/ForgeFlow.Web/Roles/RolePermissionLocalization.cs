using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Reporting;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Units;
using ForgeFlow.Domain.Users;
using ForgeFlow.Web.Localization;
using Microsoft.Extensions.Localization;

namespace ForgeFlow.Web.Roles;

public static class RolePermissionLocalization
{
    public static readonly IReadOnlyList<PermissionModule> TenantModules =
    [
        PermissionModule.Organizations,
        PermissionModule.Sites,
        PermissionModule.Users,
        PermissionModule.Roles,
        PermissionModule.Audit,
        PermissionModule.Units,
        PermissionModule.Items,
        PermissionModule.Boms,
        PermissionModule.Procurement,
        PermissionModule.ProductionOrders,
        PermissionModule.Inventory,
        PermissionModule.Reports,
        PermissionModule.Quality,
        PermissionModule.Diagnostics
    ];

    public static readonly IReadOnlyList<PermissionDescriptor> OrganizationScopePermissions =
        PermissionCatalog.All
            .Where(d => d.Applicability != PermissionScopeApplicability.PlatformOnly)
            .ToArray();

    public static readonly IReadOnlyList<PermissionDescriptor> SiteScopePermissions =
        PermissionCatalog.All
            .Where(d => d.Applicability == PermissionScopeApplicability.OrganizationOrSite)
            .ToArray();

    public static string PageTitleKey(PermissionModule module) => module switch
    {
        PermissionModule.Organizations => "PermPageOrganizations",
        PermissionModule.Sites => "PermPageSites",
        PermissionModule.Users => "PermPageUsers",
        PermissionModule.Roles => "PermPageRoles",
        PermissionModule.Audit => "PermPageAudit",
        PermissionModule.Units => "PermPageUnits",
        PermissionModule.Items => "PermPageItems",
        PermissionModule.Boms => "PermPageBoms",
        PermissionModule.Procurement => "PermPageProcurement",
        PermissionModule.ProductionOrders => "PermPageProductionOrders",
        PermissionModule.Inventory => "PermPageInventory",
        PermissionModule.Reports => "PermPageReports",
        PermissionModule.Quality => "PermPageQuality",
        PermissionModule.Diagnostics => "PermPageDiagnostics",
        _ => "PermPageOrganizations"
    };

    public static string OperationLabelKey(string permissionCode) => permissionCode switch
    {
        "Organizations.Read" or "Sites.Read" or "Users.Read" or "Roles.Read" or "Audit.Read" or "Units.Read" or "Items.Read" or "Boms.Read" or "Procurement.Read" or "ProductionOrders.Read" or "Inventory.Read" or "Reports.Read" or "Quality.Read" => "PermOpRead",
        "Organizations.Create" or "Sites.Create" or "Users.Create" => "PermOpCreate",
        "Organizations.Update" or "Sites.Update" => "PermOpUpdate",
        "Organizations.SetActive" or "Sites.SetActive" or "Users.SetActive" => "PermOpSetActive",
        "Users.Unlock" => "PermOpUnlock",
        "Users.ResetPassword" => "PermOpResetPassword",
        "Memberships.Manage" => "PermOpManageMemberships",
        "Roles.AssignFixed" => "PermOpAssignFixedRoles",
        "Roles.Manage" => "PermOpManageRoles",
        "Permissions.Assign" => "PermOpAssignPermissions",
        "Units.Manage" => "PermOpManageUnits",
        "Items.Manage" => "PermOpManageItems",
        "Boms.Manage" => "PermOpManageBoms",
        "Procurement.ManageSuppliers" => "PermOpManageSuppliers",
        "Procurement.ManageOrders" => "PermOpManageOrders",
        "Procurement.ReceiveOrders" => "PermOpReceiveOrders",
        "ProductionOrders.Manage" => "PermOpManageProductionOrders",
        "Inventory.Manage" => "PermOpManageInventory",
        "Reports.Export" => "PermOpExportReports",
        "Quality.Manage" => "PermOpManageQuality",
        "Audit.Export" => "PermOpExportAudit",
        "Diagnostics.ErrorLogsRead" => "PermOpReadErrorLogs",
        "Diagnostics.ErrorLogsManage" => "PermOpManageErrorLogs",
        _ => "PermOpRead"
    };

    public static string RoleDisplayName(string roleName, bool isSystemRole, IStringLocalizer<SharedResources> text)
    {
        if (!isSystemRole) return roleName;
        return roleName switch
        {
            "OrganizationAdministrator" => text["RoleBuiltInOrgAdmin"],
            "SiteOperator" => text["RoleBuiltInSiteOperator"],
            "DeploymentProvisioner" => text["RoleBuiltInProvisioner"],
            _ => roleName
        };
    }

    public static string ContextDimensionDisplayName(string code, string storedName, IStringLocalizer<SharedResources> text) => (code, storedName) switch
    {
        ("PLANT", "Plant / Site") => text["BomDimStdPlant"],
        ("PRODUCTION_LINE", "Production Line") => text["BomDimStdProductionLine"],
        ("MARKET", "Target Market") => text["BomDimStdMarket"],
        ("CUSTOMER", "Customer") => text["BomDimStdCustomer"],
        ("MANUFACTURING_METHOD", "Manufacturing Method") => text["BomDimStdManufacturingMethod"],
        _ => storedName
    };

    public static string AuditEventLabel(string eventCode, IStringLocalizer<SharedResources> text)
    {
        var key = AuditEventCatalog.ResolveResourceKey(eventCode);
        if (key is null) return eventCode;
        var localized = text[key];
        return localized.ResourceNotFound ? eventCode : localized.Value;
    }

    public static string FailureKey(string code) => code switch
    {
        RolePermissionErrors.NameInvalid => "RoleErrorInvalidName",
        RolePermissionErrors.NameDuplicate => "RoleErrorDuplicateName",
        RolePermissionErrors.SystemRoleImmutable => "RoleErrorSystemImmutable",
        RolePermissionErrors.PermissionUnknown => "RoleErrorPermissionUnknown",
        RolePermissionErrors.PermissionScopeInvalid or UserManagementErrors.RoleScopeInvalid => "RoleErrorPermissionScopeInvalid",
        RolePermissionErrors.EscalationForbidden => "ReadDenied",
        RolePermissionErrors.MembershipRequired or UserManagementErrors.OrganizationMembershipRequired => "UserErrorOrgMembershipRequired",
        RolePermissionErrors.LastOrganizationAdminRequired or UserManagementErrors.LastOrganizationAdminRequired => "UserErrorLastOrgAdmin",
        RolePermissionErrors.SelfDeactivationForbidden or UserManagementErrors.SelfDeactivationForbidden => "UserErrorSelfDeactivation",
        RolePermissionErrors.ParentOrganizationInactive or UserManagementErrors.ParentOrganizationInactive or OrganizationSiteErrors.OrganizationInactive or UnitErrors.ParentOrganizationInactive or ItemErrors.ParentOrganizationInactive or BomErrors.ParentOrganizationInactive or ProductionOrderErrors.ParentOrganizationInactive or InventoryErrors.ParentOrganizationInactive or QualityErrors.ParentOrganizationInactive or ProcurementErrors.ParentOrganizationInactive => "WriteParentInactive",
        RolePermissionErrors.ParentSiteInactive or UserManagementErrors.ParentSiteInactive => "UserErrorParentSiteInactive",
        RolePermissionErrors.ConcurrencyConflict or UserManagementErrors.ConcurrencyConflict or OrganizationAccessErrors.ConcurrencyConflict or UnitErrors.ConcurrencyConflict or ItemErrors.ConcurrencyConflict or BomErrors.ConcurrencyConflict or ProductionOrderErrors.ConcurrencyConflict or InventoryErrors.ConcurrencyConflict or QualityErrors.ConcurrencyConflict or ProcurementErrors.ConcurrencyConflict => "WriteConflict",
        RolePermissionErrors.OutcomeUnknown or UserManagementErrors.OutcomeUnknown or OrganizationAccessErrors.OutcomeUnknown or UnitErrors.OutcomeUnknown or ItemErrors.OutcomeUnknown or BomErrors.OutcomeUnknown or ProductionOrderErrors.OutcomeUnknown or InventoryErrors.OutcomeUnknown or QualityErrors.OutcomeUnknown or ProcurementErrors.OutcomeUnknown => "WriteOutcomeUnknown",
        RolePermissionErrors.AccessDenied or RolePermissionErrors.ActorRequired or RolePermissionErrors.ScopeRequired
            or UserManagementErrors.AccessDenied or UserManagementErrors.ActorRequired or UserManagementErrors.ScopeRequired
            or AuditErrors.AccessDenied or AuditErrors.ActorRequired or AuditErrors.ScopeRequired
            or UnitErrors.AccessDenied or UnitErrors.ActorRequired or UnitErrors.ScopeRequired
            or ItemErrors.AccessDenied or ItemErrors.ActorRequired or ItemErrors.ScopeRequired
            or BomErrors.AccessDenied or BomErrors.ActorRequired or BomErrors.ScopeRequired
            or ProductionOrderErrors.AccessDenied or ProductionOrderErrors.ActorRequired or ProductionOrderErrors.ScopeRequired
            or InventoryErrors.AccessDenied or InventoryErrors.ActorRequired or InventoryErrors.ScopeRequired
            or QualityErrors.AccessDenied or QualityErrors.ActorRequired or QualityErrors.ScopeRequired
            or ProcurementErrors.AccessDenied or ProcurementErrors.ActorRequired or ProcurementErrors.ScopeRequired
            or ReportingErrors.AccessDenied or ReportingErrors.ActorRequired or ReportingErrors.ScopeRequired
            or DiagnosticErrors.AccessDenied or DiagnosticErrors.ActorRequired or DiagnosticErrors.ScopeRequired => "ReadDenied",
        RolePermissionErrors.NotFound or UserManagementErrors.NotFound or OrganizationSiteErrors.OrganizationNotFound or OrganizationSiteErrors.SiteNotFound
            or AuditErrors.NotFound or UnitErrors.NotFound or UnitErrors.ConversionNotFound or UnitErrors.CurrencyNotFound
            or ItemErrors.NotFound or ItemErrors.ClassificationSetNotFound or ItemErrors.ClassificationNotFound
            or ItemErrors.AttributeDefinitionNotFound or ItemErrors.ItemUnitConversionNotFound
            or BomErrors.NotFound or BomErrors.VersionNotFound or BomErrors.ContextDimensionNotFound
            or ProductionOrderErrors.NotFound
            or InventoryErrors.WarehouseNotFound or InventoryErrors.TransactionNotFound
            or QualityErrors.InspectionNotFound or QualityErrors.LotNotFound or QualityErrors.LineNotFound
            or ProcurementErrors.NotFound or ProcurementErrors.SupplierNotFound or ProcurementErrors.LineNotFound
            or DiagnosticErrors.NotFound => "ReadNotFound",
        UnitErrors.CodeInvalid or UnitErrors.CurrencyCodeInvalid or ItemErrors.CodeInvalid or BomErrors.CodeInvalid or ProductionOrderErrors.OrderNumberInvalid or InventoryErrors.CodeInvalid or InventoryErrors.DocumentNumberInvalid or QualityErrors.InspectionNumberInvalid
            or ProcurementErrors.SupplierCodeInvalid or ProcurementErrors.OrderNumberInvalid or ProcurementErrors.ReceiptDocumentInvalid => "CreateCodeInvalid",
        UnitErrors.CodeDuplicate or UnitErrors.CurrencyCodeDuplicate or ItemErrors.CodeDuplicate or ItemErrors.ClassificationSetCodeDuplicate or ItemErrors.ClassificationCodeDuplicate or ItemErrors.AttributeCodeDuplicate
            or BomErrors.CodeDuplicate or BomErrors.VersionDuplicate or BomErrors.ContextDimensionCodeDuplicate
            or ProductionOrderErrors.OrderNumberDuplicate
            or InventoryErrors.CodeDuplicate or InventoryErrors.DocumentNumberDuplicate or QualityErrors.InspectionNumberDuplicate
            or ProcurementErrors.SupplierCodeDuplicate or ProcurementErrors.OrderNumberDuplicate => "CreateCodeDuplicate",
        UnitErrors.NameInvalid or ItemErrors.NameInvalid or BomErrors.NameInvalid or InventoryErrors.NameInvalid or ProcurementErrors.SupplierNameInvalid => "WriteNameInvalid",
        ItemErrors.DescriptionInvalid => "ItemErrorDescriptionInvalid",
        ItemErrors.BaseUnitNotFound or ItemErrors.BaseUnitInactive => "ItemErrorBaseUnitInvalid",
        ItemErrors.CapabilityRequired => "ItemErrorCapabilityRequired",
        ItemErrors.ItemInactive or ItemErrors.ClassificationInactive or BomErrors.BomInactive => "UnitErrorInactive",
        ItemErrors.ClassificationParentInvalid => "ItemErrorClassificationParentInvalid",
        ItemErrors.ClassificationCircularParent => "ItemErrorClassificationCircularParent",
        ItemErrors.AttributeDataTypeInvalid or ItemErrors.AttributeValueInvalid or ItemErrors.AttributeUnitNotFound or ItemErrors.AttributeUnitInactive => "ItemErrorAttributeValueInvalid",
        ItemErrors.AttributeRequired => "ItemErrorAttributeRequired",
        ItemErrors.ItemUnitConversionDuplicate => "UnitErrorConversionDuplicate",
        UnitErrors.SymbolInvalid => "UnitErrorSymbolInvalid",
        UnitErrors.DimensionInvalid => "UnitErrorDimensionInvalid",
        UnitErrors.PrecisionInvalid => "UnitErrorPrecisionInvalid",
        UnitErrors.UnitInactive => "UnitErrorInactive",
        UnitErrors.CurrencyInactive => "UnitErrorCurrencyInactive",
        UnitErrors.ConversionDuplicate => "UnitErrorConversionDuplicate",
        UnitErrors.ConversionSameUnit => "UnitErrorConversionSameUnit",
        UnitErrors.ConversionFactorInvalid => "UnitErrorConversionFactorInvalid",
        UnitErrors.ConversionPathNotFound => "UnitErrorConversionPathNotFound",
        UnitErrors.ConversionConflictingPath => "UnitErrorConversionConflictingPath",
        UnitErrors.ConversionQuantityInvalid => "UnitErrorConversionQuantityInvalid",
        BomErrors.PrimaryItemInvalid or BomErrors.InputItemInvalid or BomErrors.OutputItemInvalid => "BomErrorItemInvalid",
        BomErrors.PrimaryItemNotManufacturable => "BomErrorPrimaryNotManufacturable",
        BomErrors.VersionNumberInvalid => "BomErrorVersionNumberInvalid",
        BomErrors.VersionImmutable => "BomErrorVersionImmutable",
        BomErrors.VersionTransitionInvalid => "BomErrorVersionTransitionInvalid",
        BomErrors.BaseOutputQuantityInvalid or BomErrors.QuantityInvalid => "BomErrorQuantityInvalid",
        BomErrors.EffectiveDatesInvalid => "BomErrorEffectiveDatesInvalid",
        BomErrors.MainOutputRequired => "BomErrorMainOutputRequired",
        BomErrors.InputsRequired => "BomErrorInputsRequired",
        BomErrors.ScrapInvalid => "BomErrorScrapInvalid",
        BomErrors.UnitInvalid => "BomErrorUnitInvalid",
        BomErrors.DirectSelfCycle => "BomErrorDirectSelfCycle",
        BomErrors.CircularDependency => "BomErrorCircularDependency",
        BomErrors.ApplicabilityInvalid => "BomErrorApplicabilityInvalid",
        BomErrors.NoActiveBomFound or ProductionOrderErrors.NoActiveBomFound => "BomErrorNoActiveBomFound",
        BomErrors.AmbiguousResolution or ProductionOrderErrors.AmbiguousResolution => "BomErrorAmbiguousResolution",
        ProductionOrderErrors.SiteInvalid => "PoErrorSiteInvalid",
        ProductionOrderErrors.SiteInactive => "PoErrorSiteInactive",
        ProductionOrderErrors.ItemInvalid => "BomErrorItemInvalid",
        ProductionOrderErrors.ItemNotManufacturable => "BomErrorPrimaryNotManufacturable",
        ProductionOrderErrors.UnitInvalid => "BomErrorUnitInvalid",
        ProductionOrderErrors.QuantityInvalid => "BomErrorQuantityInvalid",
        ProductionOrderErrors.DatesInvalid => "BomErrorEffectiveDatesInvalid",
        ProductionOrderErrors.OrderImmutable => "PoErrorImmutable",
        ProductionOrderErrors.TransitionInvalid => "PoErrorTransitionInvalid",
        ProductionOrderErrors.ActualQuantityInvalid => "PoErrorActualQtyInvalid",
        ProductionOrderErrors.CancellationReasonInvalid => "PoErrorCancelReasonInvalid",
        ProductionOrderErrors.NotesInvalid or InventoryErrors.NotesInvalid => "PoErrorNotesInvalid",
        InventoryErrors.WarehouseInactive => "InvErrorWarehouseInactive",
        InventoryErrors.AlreadyReversed => "InvErrorAlreadyReversed",
        InventoryErrors.CannotReverseReversal => "InvErrorCannotReverseReversal",
        InventoryErrors.LinesRequired => "InvErrorLinesRequired",
        InventoryErrors.ItemInvalid => "BomErrorItemInvalid",
        InventoryErrors.UnitInvalid => "BomErrorUnitInvalid",
        InventoryErrors.QuantityInvalid => "InvErrorQuantityInvalid",
        InventoryErrors.LotCodeInvalid => "InvErrorLotCodeInvalid",
        InventoryErrors.DestinationWarehouseInvalid => "InvErrorDestWarehouseInvalid",
        InventoryErrors.TransferSameWarehouse => "InvErrorTransferSameWarehouse",
        InventoryErrors.ProductionOrderInvalid => "InvErrorOrderInvalid",
        InventoryErrors.ProductionOrderStateInvalid => "InvErrorOrderStateInvalid",
        InventoryErrors.InsufficientStock => "InvErrorInsufficientStock",
        InventoryErrors.LotRequired or QualityErrors.LotCodeRequired => "InvErrorLotRequired",
        InventoryErrors.LotExpired => "InvErrorLotExpired",
        InventoryErrors.IdempotencyKeyInvalid => "InvErrorIdempotencyInvalid",
        InventoryErrors.SiteInvalid or ReportingErrors.SiteInvalid or QualityErrors.SiteInvalid => "PoErrorSiteInvalid",
        InventoryErrors.SiteInactive or QualityErrors.SiteInactive => "PoErrorSiteInactive",
        ItemErrors.QualityControlModeInvalid => "ItemErrorQcModeInvalid",
        QualityErrors.InspectionAlreadyTerminal => "QcErrorTerminal",
        QualityErrors.InspectionHasDispositions => "QcErrorHasDispositions",
        QualityErrors.DispositionExceedsRemaining => "QcErrorDispositionExceeds",
        QualityErrors.InsufficientAvailableStock => "InvErrorInsufficientStock",
        QualityErrors.DefectReasonInvalid => "QcErrorDefectReasonInvalid",
        QualityErrors.ParameterInvalid => "QcErrorParameterInvalid",
        QualityErrors.LotDatesInvalid => "QcErrorLotDatesInvalid",
        QualityErrors.LotCodeInvalid => "InvErrorLotCodeInvalid",
        QualityErrors.QuantityInvalid => "InvErrorQuantityInvalid",
        QualityErrors.NotesInvalid => "PoErrorNotesInvalid",
        QualityErrors.WarehouseInvalid => "InvErrorWarehouseInactive",
        QualityErrors.ItemInvalid => "BomErrorItemInvalid",
        QualityErrors.UnitInvalid => "BomErrorUnitInvalid",
        ProcurementErrors.SupplierInactive => "ProcErrorSupplierInactive",
        ProcurementErrors.ContactInvalid => "ProcErrorContactInvalid",
        ProcurementErrors.EmailInvalid => "ProfileEmailInvalidError",
        ProcurementErrors.PhoneInvalid => "ProfilePhoneInvalidError",
        ProcurementErrors.AddressInvalid => "ProcErrorAddressInvalid",
        ProcurementErrors.CurrencyInvalid => "ProcErrorCurrencyInvalid",
        ProcurementErrors.DatesInvalid => "BomErrorEffectiveDatesInvalid",
        ProcurementErrors.NotesInvalid => "PoErrorNotesInvalid",
        ProcurementErrors.CancellationReasonInvalid => "PoErrorCancelReasonInvalid",
        ProcurementErrors.EmptyOrder => "ProcErrorEmptyOrder",
        ProcurementErrors.DuplicateItem => "ProcErrorDuplicateItem",
        ProcurementErrors.ItemInvalid => "BomErrorItemInvalid",
        ProcurementErrors.ItemNotPurchasable => "ProcErrorItemNotPurchasable",
        ProcurementErrors.LineUnitInvalid => "BomErrorUnitInvalid",
        ProcurementErrors.QuantityInvalid => "BomErrorQuantityInvalid",
        ProcurementErrors.UnitPriceInvalid => "ProcErrorUnitPriceInvalid",
        ProcurementErrors.WarehouseInvalid => "InvErrorWarehouseInactive",
        ProcurementErrors.SiteInvalid => "PoErrorSiteInvalid",
        ProcurementErrors.NotDraft => "ProcErrorNotDraft",
        ProcurementErrors.InvalidTransition => "ProcErrorInvalidTransition",
        ProcurementErrors.HasReceiptsCannotCancel => "ProcErrorHasReceiptsCannotCancel",
        ProcurementErrors.OverReceipt => "ProcErrorOverReceipt",
        ReportingErrors.ProductionOrderNotFound => "RptErrorOrderNotFound",
        ReportingErrors.ExportInvalid => "RptErrorExportInvalid",
        DiagnosticErrors.InvalidRetentionDays => "DiagErrorRetentionInvalid",
        AuditErrors.InvalidQuery or DiagnosticErrors.InvalidQuery => "ReadError",
        _ => "WriteUnavailable"
    };

    public static void ToggleOperationWithAutoRead(
        HashSet<string> selectedCodes,
        PermissionDescriptor descriptor,
        bool isChecked,
        IReadOnlyList<PermissionDescriptor> availableDescriptors)
    {
        var isRead = descriptor.ActionKey == "Read";
        if (isChecked)
        {
            selectedCodes.Add(descriptor.Code);
            if (!isRead)
            {
                var readPerm = availableDescriptors.FirstOrDefault(x => x.Module == descriptor.Module && x.ActionKey == "Read");
                if (readPerm is not null)
                    selectedCodes.Add(readPerm.Code);
            }
        }
        else
        {
            selectedCodes.Remove(descriptor.Code);
            if (isRead)
            {
                foreach (var item in availableDescriptors.Where(x => x.Module == descriptor.Module && x.ActionKey != "Read"))
                {
                    selectedCodes.Remove(item.Code);
                }
            }
        }
    }

    public static void ToggleModuleAll(
        HashSet<string> selectedCodes,
        PermissionModule module,
        bool isChecked,
        IReadOnlyList<PermissionDescriptor> availableDescriptors)
    {
        foreach (var item in availableDescriptors.Where(x => x.Module == module))
        {
            if (isChecked) selectedCodes.Add(item.Code);
            else selectedCodes.Remove(item.Code);
        }
    }
}
