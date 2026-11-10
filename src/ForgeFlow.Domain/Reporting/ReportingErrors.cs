namespace ForgeFlow.Domain.Reporting;

public static class ReportingErrors
{
    public const string ActorRequired = "ReportingErrors.ActorRequired";
    public const string ScopeRequired = "ReportingErrors.ScopeRequired";
    public const string AccessDenied = "ReportingErrors.AccessDenied";
    public const string SiteInvalid = "ReportingErrors.SiteInvalid";
    public const string ProductionOrderNotFound = "ReportingErrors.ProductionOrderNotFound";
    public const string ExportInvalid = "ReportingErrors.ExportInvalid";
    public const string PersistenceUnavailable = "ReportingErrors.PersistenceUnavailable";
}

public sealed class ReportingRuleException(string code, params (string Key, string Value)[] parameters)
    : Exception(code)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string> Parameters { get; } = parameters.ToDictionary(p => p.Key, p => p.Value);
}
