namespace ForgeFlow.Domain.Procurement;

public static class ProcurementErrors
{
    public const string ActorRequired = "Procurement.ActorRequired";
    public const string ScopeRequired = "Procurement.ScopeRequired";
    public const string AccessDenied = "Procurement.AccessDenied";
    public const string NotFound = "Procurement.NotFound";
    public const string PersistenceUnavailable = "Procurement.PersistenceUnavailable";
    public const string AuditUnavailable = "Procurement.AuditUnavailable";
    public const string ConcurrencyConflict = "Procurement.ConcurrencyConflict";
    public const string OutcomeUnknown = "Procurement.OutcomeUnknown";

    public const string ParentOrganizationInactive = "Procurement.ParentOrganizationInactive";
    public const string SiteInvalid = "Procurement.SiteInvalid";
    public const string SupplierNotFound = "Procurement.SupplierNotFound";
    public const string SupplierInactive = "Procurement.SupplierInactive";
    public const string SupplierCodeInvalid = "Procurement.SupplierCodeInvalid";
    public const string SupplierCodeDuplicate = "Procurement.SupplierCodeDuplicate";
    public const string SupplierNameInvalid = "Procurement.SupplierNameInvalid";
    public const string ContactInvalid = "Procurement.ContactInvalid";
    public const string EmailInvalid = "Procurement.EmailInvalid";
    public const string PhoneInvalid = "Procurement.PhoneInvalid";
    public const string AddressInvalid = "Procurement.AddressInvalid";

    public const string OrderNumberInvalid = "Procurement.OrderNumberInvalid";
    public const string OrderNumberDuplicate = "Procurement.OrderNumberDuplicate";
    public const string CurrencyInvalid = "Procurement.CurrencyInvalid";
    public const string DatesInvalid = "Procurement.DatesInvalid";
    public const string NotesInvalid = "Procurement.NotesInvalid";
    public const string CancellationReasonInvalid = "Procurement.CancellationReasonInvalid";
    public const string EmptyOrder = "Procurement.EmptyOrder";
    public const string DuplicateItem = "Procurement.DuplicateItem";
    public const string ItemInvalid = "Procurement.ItemInvalid";
    public const string ItemNotPurchasable = "Procurement.ItemNotPurchasable";
    public const string LineUnitInvalid = "Procurement.LineUnitInvalid";
    public const string QuantityInvalid = "Procurement.QuantityInvalid";
    public const string UnitPriceInvalid = "Procurement.UnitPriceInvalid";
    public const string WarehouseInvalid = "Procurement.WarehouseInvalid";
    public const string NotDraft = "Procurement.NotDraft";
    public const string InvalidTransition = "Procurement.InvalidTransition";
    public const string HasReceiptsCannotCancel = "Procurement.HasReceiptsCannotCancel";
    public const string OverReceipt = "Procurement.OverReceipt";
    public const string ReceiptDocumentInvalid = "Procurement.ReceiptDocumentInvalid";
    public const string LineNotFound = "Procurement.LineNotFound";
}

public sealed class ProcurementRuleException(string code, params (string Key, object Value)[] parameters) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object> Parameters { get; } =
        parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
}
