namespace ForgeFlow.Domain.Sales;

public static class SalesErrors
{
    public const string ActorRequired = "Sales.ActorRequired";
    public const string ScopeRequired = "Sales.ScopeRequired";
    public const string AccessDenied = "Sales.AccessDenied";
    public const string NotFound = "Sales.NotFound";
    public const string PersistenceUnavailable = "Sales.PersistenceUnavailable";
    public const string AuditUnavailable = "Sales.AuditUnavailable";
    public const string ConcurrencyConflict = "Sales.ConcurrencyConflict";
    public const string OutcomeUnknown = "Sales.OutcomeUnknown";

    public const string ParentOrganizationInactive = "Sales.ParentOrganizationInactive";
    public const string SiteInvalid = "Sales.SiteInvalid";
    public const string CustomerNotFound = "Sales.CustomerNotFound";
    public const string CustomerInactive = "Sales.CustomerInactive";
    public const string CustomerCodeInvalid = "Sales.CustomerCodeInvalid";
    public const string CustomerCodeDuplicate = "Sales.CustomerCodeDuplicate";
    public const string CustomerNameInvalid = "Sales.CustomerNameInvalid";
    public const string ContactInvalid = "Sales.ContactInvalid";
    public const string EmailInvalid = "Sales.EmailInvalid";
    public const string PhoneInvalid = "Sales.PhoneInvalid";
    public const string AddressInvalid = "Sales.AddressInvalid";
    public const string TaxCodeInvalid = "Sales.TaxCodeInvalid";

    public const string OrderNumberInvalid = "Sales.OrderNumberInvalid";
    public const string OrderNumberDuplicate = "Sales.OrderNumberDuplicate";
    public const string DispatchNumberInvalid = "Sales.DispatchNumberInvalid";
    public const string DispatchNumberDuplicate = "Sales.DispatchNumberDuplicate";
    public const string CurrencyInvalid = "Sales.CurrencyInvalid";
    public const string DatesInvalid = "Sales.DatesInvalid";
    public const string NotesInvalid = "Sales.NotesInvalid";
    public const string ReasonInvalid = "Sales.ReasonInvalid";
    public const string CarrierInvalid = "Sales.CarrierInvalid";
    public const string TrackingNumberInvalid = "Sales.TrackingNumberInvalid";
    public const string EmptyOrder = "Sales.EmptyOrder";
    public const string EmptyDispatch = "Sales.EmptyDispatch";
    public const string DuplicateItem = "Sales.DuplicateItem";
    public const string ItemInvalid = "Sales.ItemInvalid";
    public const string ItemNotSellable = "Sales.ItemNotSellable";
    public const string LineUnitInvalid = "Sales.LineUnitInvalid";
    public const string QuantityInvalid = "Sales.QuantityInvalid";
    public const string UnitPriceInvalid = "Sales.UnitPriceInvalid";
    public const string WarehouseInvalid = "Sales.WarehouseInvalid";
    public const string LotCodeInvalid = "Sales.LotCodeInvalid";
    public const string LotExpired = "Sales.LotExpired";
    public const string LotQuarantinedOrInsufficient = "Sales.LotQuarantinedOrInsufficient";
    public const string NotDraft = "Sales.NotDraft";
    public const string InvalidTransition = "Sales.InvalidTransition";
    public const string HasDispatchesCannotCancel = "Sales.HasDispatchesCannotCancel";
    public const string OverDispatch = "Sales.OverDispatch";
    public const string LineNotFound = "Sales.LineNotFound";
    public const string DispatchNotFound = "Sales.DispatchNotFound";
    public const string AlreadyReversed = "Sales.AlreadyReversed";
}

public sealed class SalesRuleException(string code, params (string Key, object Value)[] parameters) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object> Parameters { get; } =
        parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
}
