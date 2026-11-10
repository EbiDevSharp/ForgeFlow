using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.Units;

public static class UnitErrors
{
    public const string CodeInvalid = "Units.CodeInvalid";
    public const string CodeDuplicate = "Units.CodeDuplicate";
    public const string NameInvalid = "Units.NameInvalid";
    public const string SymbolInvalid = "Units.SymbolInvalid";
    public const string DimensionInvalid = "Units.DimensionInvalid";
    public const string PrecisionInvalid = "Units.PrecisionInvalid";
    public const string NotFound = "Units.NotFound";
    public const string UnitInactive = "Units.UnitInactive";
    public const string ConversionNotFound = "Units.ConversionNotFound";
    public const string ConversionDuplicate = "Units.ConversionDuplicate";
    public const string ConversionSameUnit = "Units.ConversionSameUnit";
    public const string ConversionFactorInvalid = "Units.ConversionFactorInvalid";
    public const string ConversionPathNotFound = "Units.ConversionPathNotFound";
    public const string ConversionConflictingPath = "Units.ConversionConflictingPath";
    public const string ConversionQuantityInvalid = "Units.ConversionQuantityInvalid";
    public const string ParentOrganizationInactive = "Units.ParentOrganizationInactive";
    public const string AccessDenied = "Units.AccessDenied";
    public const string ActorRequired = "Units.ActorRequired";
    public const string ScopeRequired = "Units.ScopeRequired";
    public const string ConcurrencyConflict = "Units.ConcurrencyConflict";
    public const string OutcomeUnknown = "Units.OutcomeUnknown";
    public const string AuditUnavailable = "Units.AuditUnavailable";
    public const string PersistenceUnavailable = "Units.PersistenceUnavailable";
}

/// <summary>Stable machine code and safe parameters; presentation supplies the localized message.</summary>
public sealed class UnitRuleException : Exception
{
    internal UnitRuleException(string code, params (string Key, object Value)[] parameters)
        : base(code)
    {
        Code = code;
        Parameters = new ReadOnlyDictionary<string, object>(parameters.ToDictionary(p => p.Key, p => p.Value));
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
}
