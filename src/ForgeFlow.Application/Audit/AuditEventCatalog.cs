using ForgeFlow.Application.Diagnostics;

namespace ForgeFlow.Application.Audit;

public sealed record AuditEventDescriptor(
    string Code,
    AuditCategory Category,
    string ResourceKey);

/// <summary>
/// Central extensible registry of all business audit, security, and operational event codes.
/// Avoids hardcoded string lists inside Razor pages and enables automatic resource verification.
/// </summary>
public static class AuditEventCatalog
{
    public static string BuildResourceKey(string eventCode) =>
        "AuditEv_" + eventCode.Replace(".", string.Empty, StringComparison.Ordinal);

    private static AuditEventDescriptor Biz(string code) =>
        new(code, AuditCategory.Business, BuildResourceKey(code));

    private static AuditEventDescriptor Sec(string code) =>
        new(code, AuditCategory.Security, BuildResourceKey(code));

    private static AuditEventDescriptor Op(string code) =>
        new(code, AuditCategory.Operations, BuildResourceKey(code));

    public static readonly IReadOnlyList<AuditEventDescriptor> All =
    [
        // Business mutations (organization.OrganizationChange)
        Biz("Organization.Created"),
        Biz("Organization.Renamed"),
        Biz("Organization.Activated"),
        Biz("Organization.Deactivated"),
        Biz("Site.Created"),
        Biz("Site.Renamed"),
        Biz("Site.Activated"),
        Biz("Site.Deactivated"),
        Biz("Unit.Created"),
        Biz("Unit.Updated"),
        Biz("Unit.Activated"),
        Biz("Unit.Deactivated"),
        Biz("UnitConversion.Created"),
        Biz("UnitConversion.Updated"),
        Biz("UnitConversion.Activated"),
        Biz("UnitConversion.Deactivated"),
        Biz("Item.Created"),
        Biz("Item.Updated"),
        Biz("Item.Activated"),
        Biz("Item.Deactivated"),
        Biz("ClassificationSet.Created"),
        Biz("Classification.Created"),
        Biz("Classification.Updated"),
        Biz("Classification.Activated"),
        Biz("Classification.Deactivated"),
        Biz("ItemClassification.Updated"),
        Biz("ItemUnitConversion.Created"),
        Biz("ItemUnitConversion.Updated"),
        Biz("ItemUnitConversion.Activated"),
        Biz("ItemUnitConversion.Deactivated"),
        Biz("AttributeDefinition.Created"),
        Biz("AttributeDefinition.Updated"),
        Biz("AttributeDefinition.Activated"),
        Biz("AttributeDefinition.Deactivated"),
        Biz("ItemAttributeValue.Updated"),
        Biz("ContextDimension.Created"),
        Biz("ContextDimension.Updated"),
        Biz("ContextDimension.Activated"),
        Biz("ContextDimension.Deactivated"),
        Biz("Bom.Created"),
        Biz("Bom.Renamed"),
        Biz("Bom.Activated"),
        Biz("Bom.Deactivated"),
        Biz("BomVersion.Created"),
        Biz("BomVersion.Updated"),
        Biz("BomVersion.StatusChanged"),
        Biz("BomVersion.LinesSaved"),
        Biz("BomVersion.ApplicabilitySaved"),
        Biz("ProductionOrder.Created"),
        Biz("ProductionOrder.Updated"),
        Biz("ProductionOrder.Refreshed"),
        Biz("ProductionOrder.Released"),
        Biz("ProductionOrder.Started"),
        Biz("ProductionOrder.Completed"),
        Biz("ProductionOrder.Cancelled"),
        Biz("Warehouse.Created"),
        Biz("Warehouse.Updated"),
        Biz("Warehouse.Activated"),
        Biz("Warehouse.Deactivated"),
        Biz("InventoryTransaction.Posted"),
        Biz("InventoryTransaction.Reversed"),

        // Security & authentication events (auth.SecurityEvent)
        Sec("BootstrapCompleted"),
        Sec("AccountCreated"),
        Sec("SignInFailed"),
        Sec("AccountLocked"),
        Sec("SessionIssued"),
        Sec("SessionRevoked"),
        Sec("AccessDenied"),
        Sec("PasswordResetCompleted"),
        Sec("AccountEligibilityChanged"),
        Sec("MembershipChanged"),
        Sec("ScopedRoleChanged"),
        Sec("RoleDefinitionChanged"),
        Sec("DirectPermissionChanged"),
        Sec("CulturePreferenceChanged"),

        // Non-mutating operations & rejections (ops.UserOperationLog)
        Op(UserOperationCatalog.ReportsShortageExported),
        Op(UserOperationCatalog.ReportsVarianceExported),
        Op(UserOperationCatalog.AuditExported),
        Op(UserOperationCatalog.OrganizationActionRejected),
        Op(UserOperationCatalog.SiteActionRejected),
        Op(UserOperationCatalog.UnitActionRejected),
        Op(UserOperationCatalog.ItemActionRejected),
        Op(UserOperationCatalog.BomActionRejected),
        Op(UserOperationCatalog.ProductionOrderActionRejected),
        Op(UserOperationCatalog.InventoryActionRejected),
        Op(UserOperationCatalog.UserActionRejected),
        Op(UserOperationCatalog.RoleActionRejected),
        Op(UserOperationCatalog.ReportingActionRejected)
    ];

    private static readonly Dictionary<string, AuditEventDescriptor> ByCode =
        All.ToDictionary(d => d.Code, StringComparer.Ordinal);

    public static bool TryGet(string code, out AuditEventDescriptor descriptor) =>
        ByCode.TryGetValue(code, out descriptor!);

    public static string? ResolveResourceKey(string code) =>
        ByCode.TryGetValue(code, out var d) ? d.ResourceKey : null;
}
