using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.ProductionOrders;

public static class ProductionOrderErrors
{
    public const string OrderNumberInvalid = "ProductionOrders.OrderNumberInvalid";
    public const string OrderNumberDuplicate = "ProductionOrders.OrderNumberDuplicate";
    public const string NotFound = "ProductionOrders.NotFound";
    public const string ParentOrganizationInactive = "ProductionOrders.ParentOrganizationInactive";
    public const string SiteInvalid = "ProductionOrders.SiteInvalid";
    public const string SiteInactive = "ProductionOrders.SiteInactive";
    public const string ItemInvalid = "ProductionOrders.ItemInvalid";
    public const string ItemNotManufacturable = "ProductionOrders.ItemNotManufacturable";
    public const string UnitInvalid = "ProductionOrders.UnitInvalid";
    public const string QuantityInvalid = "ProductionOrders.QuantityInvalid";
    public const string DatesInvalid = "ProductionOrders.DatesInvalid";
    public const string OrderImmutable = "ProductionOrders.OrderImmutable";
    public const string TransitionInvalid = "ProductionOrders.TransitionInvalid";
    public const string ActualQuantityInvalid = "ProductionOrders.ActualQuantityInvalid";
    public const string CancellationReasonInvalid = "ProductionOrders.CancellationReasonInvalid";
    public const string NotesInvalid = "ProductionOrders.NotesInvalid";
    public const string NoActiveBomFound = "ProductionOrders.NoActiveBomFound";
    public const string AmbiguousResolution = "ProductionOrders.AmbiguousResolution";
    public const string RoutingInvalid = "ProductionOrders.RoutingInvalid";

    public const string AccessDenied = "ProductionOrders.AccessDenied";
    public const string ActorRequired = "ProductionOrders.ActorRequired";
    public const string ScopeRequired = "ProductionOrders.ScopeRequired";
    public const string ConcurrencyConflict = "ProductionOrders.ConcurrencyConflict";
    public const string OutcomeUnknown = "ProductionOrders.OutcomeUnknown";
    public const string AuditUnavailable = "ProductionOrders.AuditUnavailable";
    public const string PersistenceUnavailable = "ProductionOrders.PersistenceUnavailable";
}

/// <summary>Stable machine code and safe parameters; presentation supplies the localized message.</summary>
public sealed class ProductionOrderRuleException : Exception
{
    public ProductionOrderRuleException(string code, params (string Key, object Value)[] parameters)
        : base(code)
    {
        Code = code;
        Parameters = new ReadOnlyDictionary<string, object>(parameters.ToDictionary(p => p.Key, p => p.Value));
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
}
