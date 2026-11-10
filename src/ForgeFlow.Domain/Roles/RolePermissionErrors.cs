using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.Roles;

public static class RolePermissionErrors
{
    public const string NameInvalid = "Roles.NameInvalid";
    public const string NameDuplicate = "Roles.NameDuplicate";
    public const string NotFound = "Roles.NotFound";
    public const string SystemRoleImmutable = "Roles.SystemRoleImmutable";
    public const string PermissionUnknown = "Roles.PermissionUnknown";
    public const string PermissionScopeInvalid = "Roles.PermissionScopeInvalid";
    public const string EscalationForbidden = "Roles.EscalationForbidden";
    public const string MembershipRequired = "Roles.MembershipRequired";
    public const string ParentOrganizationInactive = "Roles.ParentOrganizationInactive";
    public const string ParentSiteInactive = "Roles.ParentSiteInactive";
    public const string SelfDeactivationForbidden = "Roles.SelfDeactivationForbidden";
    public const string LastOrganizationAdminRequired = "Roles.LastOrganizationAdminRequired";
    public const string ConcurrencyConflict = "Roles.ConcurrencyConflict";
    public const string AccessDenied = "Roles.AccessDenied";
    public const string ActorRequired = "Roles.ActorRequired";
    public const string ScopeRequired = "Roles.ScopeRequired";
    public const string AuditUnavailable = "Roles.AuditUnavailable";
    public const string PersistenceUnavailable = "Roles.PersistenceUnavailable";
    public const string OutcomeUnknown = "Roles.OutcomeUnknown";
}

/// <summary>Stable machine code and safe parameters; presentation supplies localized messages.</summary>
public sealed class RoleRuleException : Exception
{
    internal RoleRuleException(string code, params (string Key, object Value)[] parameters)
        : base(code)
    {
        Code = code;
        Parameters = new ReadOnlyDictionary<string, object>(parameters.ToDictionary(p => p.Key, p => p.Value));
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
}
