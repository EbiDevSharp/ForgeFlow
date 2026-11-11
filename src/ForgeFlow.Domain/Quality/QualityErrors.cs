using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.Quality;

public static class QualityErrors
{
    public const string InspectionNotFound = "Quality.InspectionNotFound";
    public const string InspectionNumberInvalid = "Quality.InspectionNumberInvalid";
    public const string InspectionNumberDuplicate = "Quality.InspectionNumberDuplicate";
    public const string InspectionAlreadyTerminal = "Quality.InspectionAlreadyTerminal";
    public const string InspectionHasDispositions = "Quality.InspectionHasDispositions";
    public const string QuantityInvalid = "Quality.QuantityInvalid";
    public const string DispositionExceedsRemaining = "Quality.DispositionExceedsRemaining";
    public const string InsufficientAvailableStock = "Quality.InsufficientAvailableStock";
    public const string DefectReasonInvalid = "Quality.DefectReasonInvalid";
    public const string ParameterInvalid = "Quality.ParameterInvalid";
    public const string NotesInvalid = "Quality.NotesInvalid";
    public const string LineNotFound = "Quality.LineNotFound";
    public const string LotCodeRequired = "Quality.LotCodeRequired";
    public const string LotCodeInvalid = "Quality.LotCodeInvalid";
    public const string LotDatesInvalid = "Quality.LotDatesInvalid";
    public const string LotNotFound = "Quality.LotNotFound";
    public const string WarehouseInvalid = "Quality.WarehouseInvalid";
    public const string ItemInvalid = "Quality.ItemInvalid";
    public const string UnitInvalid = "Quality.UnitInvalid";

    public const string SiteInvalid = "Quality.SiteInvalid";
    public const string SiteInactive = "Quality.SiteInactive";
    public const string ParentOrganizationInactive = "Quality.ParentOrganizationInactive";
    public const string AccessDenied = "Quality.AccessDenied";
    public const string ActorRequired = "Quality.ActorRequired";
    public const string ScopeRequired = "Quality.ScopeRequired";
    public const string ConcurrencyConflict = "Quality.ConcurrencyConflict";
    public const string OutcomeUnknown = "Quality.OutcomeUnknown";
    public const string AuditUnavailable = "Quality.AuditUnavailable";
    public const string PersistenceUnavailable = "Quality.PersistenceUnavailable";
}

/// <summary>Stable machine code and safe parameters; presentation supplies the localized message.</summary>
public sealed class QualityRuleException : Exception
{
    public QualityRuleException(string code, params (string Key, object Value)[] parameters)
        : base(code)
    {
        Code = code;
        Parameters = new ReadOnlyDictionary<string, object>(parameters.ToDictionary(p => p.Key, p => p.Value));
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
}
