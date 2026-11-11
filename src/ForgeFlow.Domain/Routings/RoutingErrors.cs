namespace ForgeFlow.Domain.Routings;

public static class RoutingErrors
{
    public const string ActorRequired = "Routings.ActorRequired";
    public const string ScopeRequired = "Routings.ScopeRequired";
    public const string AccessDenied = "Routings.AccessDenied";
    public const string NotFound = "Routings.NotFound";
    public const string PersistenceUnavailable = "Routings.PersistenceUnavailable";
    public const string AuditUnavailable = "Routings.AuditUnavailable";
    public const string ConcurrencyConflict = "Routings.ConcurrencyConflict";
    public const string OutcomeUnknown = "Routings.OutcomeUnknown";

    public const string ParentOrganizationInactive = "Routings.ParentOrganizationInactive";
    public const string SiteInvalid = "Routings.SiteInvalid";
    public const string WorkCenterNotFound = "Routings.WorkCenterNotFound";
    public const string WorkCenterInactive = "Routings.WorkCenterInactive";
    public const string WorkCenterCodeInvalid = "Routings.WorkCenterCodeInvalid";
    public const string WorkCenterCodeDuplicate = "Routings.WorkCenterCodeDuplicate";
    public const string NameInvalid = "Routings.NameInvalid";
    public const string TypeInvalid = "Routings.TypeInvalid";
    public const string CapacityInvalid = "Routings.CapacityInvalid";
    public const string DailyHoursInvalid = "Routings.DailyHoursInvalid";
    public const string CostRateInvalid = "Routings.CostRateInvalid";

    public const string RoutingNotFound = "Routings.RoutingNotFound";
    public const string RoutingCodeInvalid = "Routings.RoutingCodeInvalid";
    public const string RoutingCodeDuplicate = "Routings.RoutingCodeDuplicate";
    public const string VersionInvalid = "Routings.VersionInvalid";
    public const string VersionDuplicate = "Routings.VersionDuplicate";
    public const string ItemInvalid = "Routings.ItemInvalid";
    public const string ItemNotManufacturable = "Routings.ItemNotManufacturable";
    public const string BomVersionInvalid = "Routings.BomVersionInvalid";
    public const string RoutingNotDraft = "Routings.RoutingNotDraft";
    public const string RoutingInvalidTransition = "Routings.RoutingInvalidTransition";
    public const string EmptyOperations = "Routings.EmptyOperations";
    public const string DuplicateSequence = "Routings.DuplicateSequence";
    public const string SequenceInvalid = "Routings.SequenceInvalid";
    public const string OperationCodeInvalid = "Routings.OperationCodeInvalid";
    public const string OperationTimeInvalid = "Routings.OperationTimeInvalid";
    public const string RunTimeInvalid = "Routings.RunTimeInvalid";
    public const string NotesInvalid = "Routings.NotesInvalid";

    public const string OperationNotFound = "Routings.OperationNotFound";
    public const string OperationTransitionInvalid = "Routings.OperationTransitionInvalid";
    public const string QuantityInvalid = "Routings.QuantityInvalid";
}

public sealed class RoutingRuleException(string code, params (string Key, object Value)[] parameters) : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, object> Parameters { get; } =
        parameters.ToDictionary(p => p.Key, p => p.Value);
}
