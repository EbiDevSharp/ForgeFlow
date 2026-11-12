using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.Users;

public static class UserManagementErrors
{
    public const string UserNameInvalid = "Users.UserNameInvalid";
    public const string UserNameDuplicate = "Users.UserNameDuplicate";
    public const string PasswordInvalid = "Users.PasswordInvalid";
    public const string NotFound = "Users.NotFound";
    public const string SelfDeactivationForbidden = "Users.SelfDeactivationForbidden";
    public const string CannotResetOwnTwoFactor = "Users.CannotResetOwnTwoFactor";
    public const string TwoFactorReasonRequired = "Users.TwoFactorReasonRequired";
    public const string LastOrganizationAdminRequired = "Users.LastOrganizationAdminRequired";
    public const string LastPlatformAdminRequired = "Users.LastPlatformAdminRequired";
    public const string ParentOrganizationInactive = "Users.ParentOrganizationInactive";
    public const string ParentSiteInactive = "Users.ParentSiteInactive";
    public const string OrganizationMembershipRequired = "Users.OrganizationMembershipRequired";
    public const string SiteMembershipRequired = "Users.SiteMembershipRequired";
    public const string RoleScopeInvalid = "Users.RoleScopeInvalid";
    public const string ScopeRequired = "Users.ScopeRequired";
    public const string AccessDenied = "Users.AccessDenied";
    public const string ActorRequired = "Users.ActorRequired";
    public const string ConcurrencyConflict = "Users.ConcurrencyConflict";
    public const string AuditUnavailable = "Users.AuditUnavailable";
    public const string PersistenceUnavailable = "Users.PersistenceUnavailable";
    public const string OutcomeUnknown = "Users.OutcomeUnknown";
}

/// <summary>Stable machine code and safe parameters; presentation supplies the localized message.</summary>
public sealed class UserRuleException : Exception
{
    internal UserRuleException(string code, params (string Key, object Value)[] parameters)
        : base(code)
    {
        Code = code;
        Parameters = new ReadOnlyDictionary<string, object>(parameters.ToDictionary(p => p.Key, p => p.Value));
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
}
