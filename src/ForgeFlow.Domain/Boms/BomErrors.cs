using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.Boms;

public static class BomErrors
{
    public const string CodeInvalid = "Boms.CodeInvalid";
    public const string CodeDuplicate = "Boms.CodeDuplicate";
    public const string NameInvalid = "Boms.NameInvalid";
    public const string NotFound = "Boms.NotFound";
    public const string BomInactive = "Boms.Inactive";
    public const string ParentOrganizationInactive = "Boms.ParentOrganizationInactive";
    public const string PrimaryItemInvalid = "Boms.PrimaryItemInvalid";
    public const string PrimaryItemNotManufacturable = "Boms.PrimaryItemNotManufacturable";

    public const string VersionNotFound = "Boms.VersionNotFound";
    public const string VersionDuplicate = "Boms.VersionDuplicate";
    public const string VersionNumberInvalid = "Boms.VersionNumberInvalid";
    public const string VersionImmutable = "Boms.VersionImmutable";
    public const string VersionTransitionInvalid = "Boms.VersionTransitionInvalid";
    public const string BaseOutputQuantityInvalid = "Boms.BaseOutputQuantityInvalid";
    public const string EffectiveDatesInvalid = "Boms.EffectiveDatesInvalid";
    public const string MainOutputRequired = "Boms.MainOutputRequired";
    public const string InputsRequired = "Boms.InputsRequired";
    public const string QuantityInvalid = "Boms.QuantityInvalid";
    public const string ScrapInvalid = "Boms.ScrapInvalid";
    public const string UnitInvalid = "Boms.UnitInvalid";
    public const string InputItemInvalid = "Boms.InputItemInvalid";
    public const string OutputItemInvalid = "Boms.OutputItemInvalid";
    public const string DirectSelfCycle = "Boms.DirectSelfCycle";
    public const string CircularDependency = "Boms.CircularDependency";

    public const string ContextDimensionNotFound = "Boms.ContextDimensionNotFound";
    public const string ContextDimensionCodeDuplicate = "Boms.ContextDimensionCodeDuplicate";
    public const string ApplicabilityInvalid = "Boms.ApplicabilityInvalid";

    public const string NoActiveBomFound = "Boms.NoActiveBomFound";
    public const string AmbiguousResolution = "Boms.AmbiguousResolution";

    public const string AccessDenied = "Boms.AccessDenied";
    public const string ActorRequired = "Boms.ActorRequired";
    public const string ScopeRequired = "Boms.ScopeRequired";
    public const string ConcurrencyConflict = "Boms.ConcurrencyConflict";
    public const string OutcomeUnknown = "Boms.OutcomeUnknown";
    public const string AuditUnavailable = "Boms.AuditUnavailable";
    public const string PersistenceUnavailable = "Boms.PersistenceUnavailable";
}

/// <summary>Stable machine code and safe parameters; presentation supplies the localized message.</summary>
public sealed class BomRuleException : Exception
{
    public BomRuleException(string code, params (string Key, object Value)[] parameters)
        : base(code)
    {
        Code = code;
        Parameters = new ReadOnlyDictionary<string, object>(parameters.ToDictionary(p => p.Key, p => p.Value));
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
}
