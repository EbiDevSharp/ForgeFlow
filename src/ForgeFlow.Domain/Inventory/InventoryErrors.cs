using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.Inventory;

public static class InventoryErrors
{
    public const string CodeInvalid = "Inventory.CodeInvalid";
    public const string CodeDuplicate = "Inventory.CodeDuplicate";
    public const string NameInvalid = "Inventory.NameInvalid";
    public const string WarehouseNotFound = "Inventory.WarehouseNotFound";
    public const string WarehouseInactive = "Inventory.WarehouseInactive";

    public const string DocumentNumberInvalid = "Inventory.DocumentNumberInvalid";
    public const string DocumentNumberDuplicate = "Inventory.DocumentNumberDuplicate";
    public const string TransactionNotFound = "Inventory.TransactionNotFound";
    public const string AlreadyReversed = "Inventory.AlreadyReversed";
    public const string CannotReverseReversal = "Inventory.CannotReverseReversal";
    public const string LinesRequired = "Inventory.LinesRequired";
    public const string ItemInvalid = "Inventory.ItemInvalid";
    public const string UnitInvalid = "Inventory.UnitInvalid";
    public const string QuantityInvalid = "Inventory.QuantityInvalid";
    public const string LotCodeInvalid = "Inventory.LotCodeInvalid";
    public const string LotRequired = "Inventory.LotRequired";
    public const string LotExpired = "Inventory.LotExpired";
    public const string DestinationWarehouseInvalid = "Inventory.DestinationWarehouseInvalid";
    public const string TransferSameWarehouse = "Inventory.TransferSameWarehouse";
    public const string ProductionOrderInvalid = "Inventory.ProductionOrderInvalid";
    public const string ProductionOrderStateInvalid = "Inventory.ProductionOrderStateInvalid";
    public const string InsufficientStock = "Inventory.InsufficientStock";
    public const string NotesInvalid = "Inventory.NotesInvalid";
    public const string IdempotencyKeyInvalid = "Inventory.IdempotencyKeyInvalid";
    public const string ReservationNotFound = "Inventory.ReservationNotFound";
    public const string ReservationInvalidState = "Inventory.ReservationInvalidState";
    public const string ReservationExceedsAvailable = "Inventory.ReservationExceedsAvailable";
    public const string ReservationNotAllowedForOrderState = "Inventory.ReservationNotAllowedForOrderState";

    public const string SiteInvalid = "Inventory.SiteInvalid";
    public const string SiteInactive = "Inventory.SiteInactive";
    public const string ParentOrganizationInactive = "Inventory.ParentOrganizationInactive";
    public const string AccessDenied = "Inventory.AccessDenied";
    public const string ActorRequired = "Inventory.ActorRequired";
    public const string ScopeRequired = "Inventory.ScopeRequired";
    public const string ConcurrencyConflict = "Inventory.ConcurrencyConflict";
    public const string OutcomeUnknown = "Inventory.OutcomeUnknown";
    public const string AuditUnavailable = "Inventory.AuditUnavailable";
    public const string PersistenceUnavailable = "Inventory.PersistenceUnavailable";
}

/// <summary>Stable machine code and safe parameters; presentation supplies the localized message.</summary>
public sealed class InventoryRuleException : Exception
{
    public InventoryRuleException(string code, params (string Key, object Value)[] parameters)
        : base(code)
    {
        Code = code;
        Parameters = new ReadOnlyDictionary<string, object>(parameters.ToDictionary(p => p.Key, p => p.Value));
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
}
