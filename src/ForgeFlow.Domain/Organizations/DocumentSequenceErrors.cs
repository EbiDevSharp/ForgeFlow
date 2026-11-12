namespace ForgeFlow.Domain.Organizations;

public static class DocumentSequenceErrors
{
    public const string ActorRequired = "DocumentSequence.ActorRequired";
    public const string ScopeRequired = "DocumentSequence.ScopeRequired";
    public const string AccessDenied = "DocumentSequence.AccessDenied";
    public const string NotFound = "DocumentSequence.NotFound";
    public const string PersistenceUnavailable = "DocumentSequence.PersistenceUnavailable";
    public const string ConcurrencyConflict = "DocumentSequence.ConcurrencyConflict";

    public const string OrganizationInvalid = "DocumentSequence.OrganizationInvalid";
    public const string DocumentTypeInvalid = "DocumentSequence.DocumentTypeInvalid";
    public const string PrefixInvalid = "DocumentSequence.PrefixInvalid";
    public const string SeparatorInvalid = "DocumentSequence.SeparatorInvalid";
    public const string YearModeInvalid = "DocumentSequence.YearModeInvalid";
    public const string PaddingInvalid = "DocumentSequence.PaddingInvalid";
    public const string NextNumberInvalid = "DocumentSequence.NextNumberInvalid";
    public const string ManualOverrideDisabled = "DocumentSequence.ManualOverrideDisabled";
}

public sealed class DocumentSequenceRuleException(string code, params (string Key, object Value)[] parameters) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object> Parameters { get; } =
        parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
}
