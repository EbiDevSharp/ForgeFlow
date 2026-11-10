using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.ProductionOrders;
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
        PermissionModule.ProductionOrders,
        PermissionModule.Inventory
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
        PermissionModule.ProductionOrders => "PermPageProductionOrders",
        PermissionModule.Inventory => "PermPageInventory",
        _ => "PermPageOrganizations"
    };

    public static string OperationLabelKey(string permissionCode) => permissionCode switch
    {
        "Organizations.Read" or "Sites.Read" or "Users.Read" or "Roles.Read" or "Audit.Read" or "Units.Read" or "Items.Read" or "Boms.Read" or "ProductionOrders.Read" or "Inventory.Read" => "PermOpRead",
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
        "ProductionOrders.Manage" => "PermOpManageProductionOrders",
        "Inventory.Manage" => "PermOpManageInventory",
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
        RolePermissionErrors.ParentOrganizationInactive or UserManagementErrors.ParentOrganizationInactive or OrganizationSiteErrors.OrganizationInactive or UnitErrors.ParentOrganizationInactive or ItemErrors.ParentOrganizationInactive or BomErrors.ParentOrganizationInactive or ProductionOrderErrors.ParentOrganizationInactive or InventoryErrors.ParentOrganizationInactive => "WriteParentInactive",
        RolePermissionErrors.ParentSiteInactive or UserManagementErrors.ParentSiteInactive => "UserErrorParentSiteInactive",
        RolePermissionErrors.ConcurrencyConflict or UserManagementErrors.ConcurrencyConflict or OrganizationAccessErrors.ConcurrencyConflict or UnitErrors.ConcurrencyConflict or ItemErrors.ConcurrencyConflict or BomErrors.ConcurrencyConflict or ProductionOrderErrors.ConcurrencyConflict or InventoryErrors.ConcurrencyConflict => "WriteConflict",
        RolePermissionErrors.OutcomeUnknown or UserManagementErrors.OutcomeUnknown or OrganizationAccessErrors.OutcomeUnknown or UnitErrors.OutcomeUnknown or ItemErrors.OutcomeUnknown or BomErrors.OutcomeUnknown or ProductionOrderErrors.OutcomeUnknown or InventoryErrors.OutcomeUnknown => "WriteOutcomeUnknown",
        RolePermissionErrors.AccessDenied or RolePermissionErrors.ActorRequired or RolePermissionErrors.ScopeRequired
            or UserManagementErrors.AccessDenied or UserManagementErrors.ActorRequired or UserManagementErrors.ScopeRequired
            or AuditErrors.AccessDenied or AuditErrors.ActorRequired or AuditErrors.ScopeRequired
            or UnitErrors.AccessDenied or UnitErrors.ActorRequired or UnitErrors.ScopeRequired
            or ItemErrors.AccessDenied or ItemErrors.ActorRequired or ItemErrors.ScopeRequired
            or BomErrors.AccessDenied or BomErrors.ActorRequired or BomErrors.ScopeRequired
            or ProductionOrderErrors.AccessDenied or ProductionOrderErrors.ActorRequired or ProductionOrderErrors.ScopeRequired
            or InventoryErrors.AccessDenied or InventoryErrors.ActorRequired or InventoryErrors.ScopeRequired => "ReadDenied",
        RolePermissionErrors.NotFound or UserManagementErrors.NotFound or OrganizationSiteErrors.OrganizationNotFound or OrganizationSiteErrors.SiteNotFound
            or AuditErrors.NotFound or UnitErrors.NotFound or UnitErrors.ConversionNotFound
            or ItemErrors.NotFound or ItemErrors.ClassificationSetNotFound or ItemErrors.ClassificationNotFound
            or ItemErrors.AttributeDefinitionNotFound or ItemErrors.ItemUnitConversionNotFound
            or BomErrors.NotFound or BomErrors.VersionNotFound or BomErrors.ContextDimensionNotFound
            or ProductionOrderErrors.NotFound
            or InventoryErrors.WarehouseNotFound or InventoryErrors.TransactionNotFound => "ReadNotFound",
        UnitErrors.CodeInvalid or ItemErrors.CodeInvalid or BomErrors.CodeInvalid or ProductionOrderErrors.OrderNumberInvalid or InventoryErrors.CodeInvalid or InventoryErrors.DocumentNumberInvalid => "CreateCodeInvalid",
        UnitErrors.CodeDuplicate or ItemErrors.CodeDuplicate or ItemErrors.ClassificationSetCodeDuplicate or ItemErrors.ClassificationCodeDuplicate or ItemErrors.AttributeCodeDuplicate
            or BomErrors.CodeDuplicate or BomErrors.VersionDuplicate or BomErrors.ContextDimensionCodeDuplicate
            or ProductionOrderErrors.OrderNumberDuplicate
            or InventoryErrors.CodeDuplicate or InventoryErrors.DocumentNumberDuplicate => "CreateCodeDuplicate",
        UnitErrors.NameInvalid or ItemErrors.NameInvalid or BomErrors.NameInvalid or InventoryErrors.NameInvalid => "WriteNameInvalid",
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
        InventoryErrors.IdempotencyKeyInvalid => "InvErrorIdempotencyInvalid",
        InventoryErrors.SiteInvalid => "PoErrorSiteInvalid",
        InventoryErrors.SiteInactive => "PoErrorSiteInactive",
        AuditErrors.InvalidQuery => "ReadError",
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
