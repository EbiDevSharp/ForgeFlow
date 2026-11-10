using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ForgeFlow.Web.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ResourceContractTests
{
    // An explicit registry makes dynamic keys and parameter counts reviewable.
    private static readonly Dictionary<string, Dictionary<string, int>> Contracts = new()
    {
        [nameof(SampleResources)] = new()
        {
            ["Title"] = 0, ["Introduction"] = 0, ["NameLabel"] = 0, ["NameRequired"] = 0,
            ["ValidateButton"] = 0, ["ValidationSucceeded"] = 1, ["DateLabel"] = 0, ["NumberLabel"] = 0,
            ["FormLegend"] = 0, ["LanguageLabel"] = 0, ["LanguagePersian"] = 0, ["LanguageEnglish"] = 0,
            ["AmountLabel"] = 0, ["AmountInvalid"] = 0, ["AmountPreview"] = 0, ["NotesLabel"] = 0,
            ["DiscardButton"] = 0, ["DraftDiscarded"] = 0, ["DraftRejected"] = 0,
            ["DraftUnavailable"] = 0, ["SwitchFailed"] = 0,
            ["DialogOpen"] = 0, ["DialogTitle"] = 0, ["DialogContent"] = 0, ["DialogClose"] = 0,
            ["TableTitle"] = 0, ["TableCaption"] = 0, ["TableFieldHeading"] = 0, ["TableValueHeading"] = 0,
            ["SyntheticName"] = 0, ["SyntheticAmount"] = 0, ["TooltipButton"] = 0, ["TooltipContent"] = 0,
            ["SampleRefresh"] = 0, ["SampleLoading"] = 0, ["SampleEmpty"] = 0, ["FeedbackTitle"] = 0,
            ["NotificationTrigger"] = 0, ["NotificationTitle"] = 0, ["NotificationContent"] = 0,
            ["MessageDismiss"] = 0, ["IdentityFixtureTitle"] = 0, ["IdentityFixtureScope"] = 0,
            ["IdentityFixtureTrigger"] = 0, ["IdentityErrorDuplicateUserName"] = 1,
            ["DateInputLabel"] = 0, ["DateInputHint"] = 0, ["DateInvalid"] = 0, ["DatePreview"] = 0,
            ["CurrencyLabel"] = 0, ["CurrencyUsd"] = 0, ["CurrencyEur"] = 0, ["CurrencyIrr"] = 0,
            ["CurrencyInvalid"] = 0, ["CurrencyPreview"] = 0, ["ChooseCurrency"] = 0,
            ["SampleModeLabel"] = 0, ["SampleModeSummary"] = 0, ["SampleModeDetailed"] = 0,
            ["SampleModeInvalid"] = 0, ["ChooseMode"] = 0, ["CanonicalValuesTitle"] = 0,
            ["SubmittedDate"] = 0, ["SubmittedAmount"] = 0, ["SubmittedCurrency"] = 0, ["SubmittedMode"] = 0
        },
        [nameof(SharedResources)] = new()
        {
            ["Navigation"] = 0, ["Home"] = 0,
            ["AppTitle"] = 0, ["OpenNavigation"] = 0, ["CloseNavigation"] = 0, ["CollapseNavigation"] = 0,
            ["ErrorTitle"] = 0, ["UnexpectedError"] = 0, ["ServerUnavailable"] = 0,
            ["ReconnectTitle"] = 0, ["ReconnectShowing"] = 0, ["ReconnectRetrying"] = 0,
            ["ReconnectAttemptLabel"] = 0, ["ReconnectSeconds"] = 0,
            ["ReconnectFailedTitle"] = 0, ["ReconnectFailed"] = 0, ["ReconnectRetry"] = 0, ["ReconnectReload"] = 0,
            ["ReconnectPausedTitle"] = 0, ["ReconnectPaused"] = 0, ["ReconnectResume"] = 0, ["ReconnectResumeFailed"] = 0,
            ["Reload"] = 0, ["NotFoundTitle"] = 0, ["NotFoundMessage"] = 0,
            ["AccessDeniedTitle"] = 0, ["AccessDeniedMessage"] = 0,
            ["LoginTitle"] = 0, ["LoginIntroduction"] = 0, ["UsernameLabel"] = 0, ["PasswordLabel"] = 0,
            ["PasswordVisibilityToggle"] = 0, ["LoginSubmit"] = 0, ["LoginLoading"] = 0,
            ["LoginRequired"] = 0, ["LoginUnavailable"] = 0, ["LoginLanguageLabel"] = 0, ["SwitchLanguageTo"] = 1,
            ["LanguagePersian"] = 0, ["LanguageEnglish"] = 0, ["UserMenu"] = 0, ["Logout"] = 0,
            ["LogoutUnavailable"] = 0, ["Sites"] = 0, ["ReadDenied"] = 0, ["ReadActive"] = 0,
            ["ReadOrganizationScope"] = 1, ["ReadClose"] = 0, ["ReadScope"] = 0, ["ReadStatus"] = 0,
            ["ReadInactive"] = 0, ["ReadEmpty"] = 0, ["ReadNotFound"] = 0, ["ReadCode"] = 0, ["ReadInactiveStatus"] = 0,
            ["ReadError"] = 0, ["Organizations"] = 0, ["Settings"] = 0, ["ReadName"] = 0, ["ReadLoading"] = 0,
            ["ReadActions"] = 0, ["ReadDetails"] = 0, ["ReadEdit"] = 0, ["ReadSiteScope"] = 2,
            ["ReadSearch"] = 0, ["ReadSearchPlaceholder"] = 0,
            ["GridPagerRange"] = 3, ["GridPagerPage"] = 2, ["GridPerPage"] = 1, ["GridRowsPerPage"] = 0,
            ["GridFirstPage"] = 0, ["GridPreviousPage"] = 0, ["GridNextPage"] = 0, ["GridLastPage"] = 0,
            ["CreateOrganizationButton"] = 0, ["CreateSiteButton"] = 0, ["CreateOrganizationTitle"] = 0,
            ["CreateSiteTitle"] = 0, ["CreateCodeHint"] = 0, ["CreateSubmit"] = 0, ["CreateCodeInvalid"] = 0,
            ["CreateCodeDuplicate"] = 0, ["CreateOrgSucceeded"] = 1, ["CreateSiteSucceeded"] = 1, ["CreateOrgNoReadScope"] = 0,
            ["WriteSave"] = 0, ["WriteSaving"] = 0, ["WriteSaved"] = 0, ["WriteActivate"] = 0,
            ["WriteDeactivate"] = 0, ["WriteConfirm"] = 0, ["WriteCancel"] = 0, ["WriteConfirmActivate"] = 1,
            ["WriteConfirmDeactivate"] = 1, ["WriteReload"] = 0, ["WriteReloadConfirm"] = 0,
            ["WriteDiscardConfirm"] = 0, ["WriteDiscard"] = 0, ["WriteKeepEditing"] = 0,
            ["WriteNameInvalid"] = 0, ["WriteConflict"] = 0,
            ["WriteOutcomeUnknown"] = 0, ["WriteParentInactive"] = 0, ["WriteHasActiveSites"] = 0, ["WriteUnavailable"] = 0,
            ["Users"] = 0, ["UserSearchPlaceholder"] = 0, ["UserUsername"] = 0, ["UserAccountStatus"] = 0,
            ["UserMembershipStatus"] = 0, ["UserRoleColumn"] = 0, ["UserSitesColumn"] = 0, ["UserSitesCount"] = 1,
            ["UserAccountDisabled"] = 0, ["UserLockedBadge"] = 0, ["UserMembershipActive"] = 0, ["UserMembershipInactive"] = 0,
            ["UserOrgAdminRole"] = 0, ["UserSiteOperatorRole"] = 0, ["UserStandardRole"] = 0,
            ["CreateUserButton"] = 0, ["CreateUserTitle"] = 0, ["AttachUserButton"] = 0, ["AttachUserTitle"] = 0,
            ["UserNameHint"] = 0, ["UserPasswordLabel"] = 0, ["UserPasswordHint"] = 0, ["UserAssignOrgAdmin"] = 0,
            ["UserAssignSitesLabel"] = 0, ["UserAssignSiteOperator"] = 0, ["UserUnlockButton"] = 0,
            ["UserActivateAccount"] = 0, ["UserDeactivateAccount"] = 0, ["UserActivateMembership"] = 0, ["UserDeactivateMembership"] = 0,
            ["UserGrantOrgAdmin"] = 0, ["UserRevokeOrgAdmin"] = 0, ["UserGrantSiteOperator"] = 0, ["UserRevokeSiteOperator"] = 0,
            ["UserAddSiteLabel"] = 0, ["UserAddSiteButton"] = 0, ["UserMultiOrgAccountHint"] = 0,
            ["UserConfirmDisableAccount"] = 1, ["UserConfirmEnableAccount"] = 1,
            ["UserConfirmDisableMembership"] = 1, ["UserConfirmEnableMembership"] = 1,
            ["UserConfirmRevokeOrgAdmin"] = 1, ["UserCreatedSucceeded"] = 1, ["UserAttachedSucceeded"] = 1,
            ["UserErrorInvalidName"] = 0, ["UserErrorDuplicate"] = 0, ["UserErrorPasswordInvalid"] = 0,
            ["UserErrorSelfDeactivation"] = 0, ["UserErrorLastOrgAdmin"] = 0, ["UserErrorLastPlatformAdmin"] = 0,
            ["UserErrorOrgMembershipRequired"] = 0, ["UserErrorSiteMembershipRequired"] = 0,
            ["UserErrorParentSiteInactive"] = 0, ["UserErrorRoleScopeInvalid"] = 0, ["UserEmptySites"] = 0,
            ["ChangePasswordLink"] = 0, ["ChangePasswordTitle"] = 0, ["CurrentPasswordLabel"] = 0,
            ["NewPasswordLabel"] = 0, ["ConfirmPasswordLabel"] = 0, ["ChangePasswordSubmit"] = 0,
            ["ChangePasswordMismatch"] = 0, ["UserResetPasswordTitle"] = 0, ["UserNewPasswordLabel"] = 0,
            ["UserResetPasswordButton"] = 0, ["UserConfirmResetPassword"] = 1,
            ["Roles"] = 0, ["RolesCaption"] = 0, ["RoleCreateButton"] = 0, ["RoleCreateTitle"] = 0, ["RoleCreateSubmit"] = 0,
            ["RoleEditTitle"] = 0, ["RoleDetailTitle"] = 0, ["RoleNameLabel"] = 0, ["RoleNameHint"] = 0,
            ["RoleTypeColumn"] = 0, ["RoleSystemBadge"] = 0, ["RoleCustomBadge"] = 0, ["RolePermissionsColumn"] = 0,
            ["RoleSystemNotice"] = 0, ["RoleConfirmDisable"] = 1, ["RoleConfirmEnable"] = 1,
            ["RoleBuiltInOrgAdmin"] = 0, ["RoleBuiltInSiteOperator"] = 0, ["RoleBuiltInProvisioner"] = 0,
            ["PermPageOrganizations"] = 0, ["PermPageSites"] = 0, ["PermPageUsers"] = 0, ["PermPageRoles"] = 0,
            ["PermOpRead"] = 0, ["PermOpCreate"] = 0, ["PermOpUpdate"] = 0, ["PermOpSetActive"] = 0,
            ["PermOpUnlock"] = 0, ["PermOpResetPassword"] = 0, ["PermOpManageMemberships"] = 0,
            ["PermOpAssignFixedRoles"] = 0, ["PermOpManageRoles"] = 0, ["PermOpAssignPermissions"] = 0, ["PermSelectAllPage"] = 0,
            ["UserPermissionsButton"] = 0, ["UserPermissionsTitle"] = 1, ["UserRolesSectionTitle"] = 0,
            ["UserAssignRoleLabel"] = 0, ["UserAssignRoleButton"] = 0, ["UserDirectOpsSectionTitle"] = 0,
            ["UserPermScopeLabel"] = 0, ["UserPermScopeOrg"] = 0, ["UserPermScopeSite"] = 2, ["UserPermSaveButton"] = 0,
            ["RoleGrantedBadge"] = 0, ["UserAdminFullAccessNotice"] = 0,
            ["RoleErrorInvalidName"] = 0, ["RoleErrorDuplicateName"] = 0,
            ["RoleErrorSystemImmutable"] = 0, ["RoleErrorPermissionUnknown"] = 0,
            ["RoleErrorPermissionScopeInvalid"] = 0,
            ["PermPageAudit"] = 0, ["Audit"] = 0, ["AuditCaption"] = 0,
            ["AuditCategoryLabel"] = 0, ["AuditCategoryAll"] = 0, ["AuditCategoryBusiness"] = 0, ["AuditCategorySecurity"] = 0,
            ["AuditEventCodeLabel"] = 0, ["AuditEventCodeAll"] = 0,
            ["AuditTimeColumn"] = 0, ["AuditEventColumn"] = 0, ["AuditActorColumn"] = 0,
            ["AuditSubjectColumn"] = 0, ["AuditOutcomeColumn"] = 0,
            ["AuditOutcomeSuccess"] = 0, ["AuditOutcomeFailure"] = 0, ["AuditOutcomeRecorded"] = 0,
            ["AuditDetailsButton"] = 0, ["AuditDetailsTitle"] = 0, ["AuditCorrelationLabel"] = 0,
            ["AuditFieldsSection"] = 0, ["AuditRawJsonSection"] = 0,
            ["AuditFieldKeyColumn"] = 0, ["AuditFieldValueColumn"] = 0, ["AuditSystemActor"] = 0
        }
    };

    [Fact]
    public void Explicit_catalogs_have_complete_keys_and_matching_placeholder_contracts()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Catalogs", "Localization");
        var files = Directory.GetFiles(directory, "*.resx");
        Assert.Equal(Contracts.Count * CulturePolicy.Supported.Count, files.Length);
        foreach (var (resource, contract) in Contracts)
        {
            Dictionary<string, string>? baseline = null;
            foreach (var culture in CulturePolicy.Supported)
            {
                var path = Path.Combine(directory, $"{resource}.{culture}.resx");
                Assert.True(File.Exists(path), path);
                var catalog = ReadCatalog(XDocument.Load(path));
                RequireKeys(catalog, contract.Keys);
                foreach (var (key, parameterCount) in contract)
                {
                    var signature = Signature(catalog[key]);
                    Assert.Equal(Enumerable.Range(0, parameterCount), signature.Select(x => x.Index).Distinct().Order());
                    if (baseline is not null)
                        Assert.Equal(Signature(baseline[key]), signature);
                }
                baseline = catalog;
            }
        }
    }

    [Fact]
    public void Every_Razor_key_is_registered_and_every_registered_key_is_used()
    {
        var used = Contracts.ToDictionary(x => x.Key, _ => new HashSet<string>());
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Sources"), "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            var resource = Regex.Match(source, @"IStringLocalizer<(?<resource>\w+)> Text");
            foreach (Match reference in Regex.Matches(source, "Text\\[\"(?<key>[^\"]+)\""))
            {
                Assert.True(resource.Success, path);
                var resourceName = resource.Groups["resource"].Value;
                var key = reference.Groups["key"].Value;
                Assert.Contains(key, Contracts[resourceName].Keys);
                used[resourceName].Add(key);
            }
        }
        // Keys selected by variable or ternary (status/failure/mode mapping) rather than a literal Text["..."] reference.
        used[nameof(SharedResources)].UnionWith(
            ["ReadDenied", "ReadActive", "ReadNotFound", "ReadInactiveStatus", "ReadError", "CreateCodeInvalid",
             "CreateCodeDuplicate", "CreateOrganizationButton", "CreateSiteButton", "CreateOrganizationTitle",
             "CreateSiteTitle", "CreateOrgSucceeded", "CreateSiteSucceeded", "WriteNameInvalid", "WriteConflict",
             "WriteOutcomeUnknown", "WriteParentInactive", "WriteHasActiveSites", "WriteUnavailable",
             "UserAccountDisabled", "UserMembershipActive", "UserMembershipInactive", "UserOrgAdminRole", "UserStandardRole",
             "UserConfirmDisableAccount", "UserConfirmEnableAccount", "UserConfirmDisableMembership",
             "UserConfirmEnableMembership", "UserConfirmRevokeOrgAdmin", "UserConfirmResetPassword",
             "UserErrorInvalidName", "UserErrorDuplicate", "UserErrorPasswordInvalid", "UserErrorSelfDeactivation",
             "UserErrorLastOrgAdmin", "UserErrorLastPlatformAdmin", "UserErrorOrgMembershipRequired",
             "UserErrorSiteMembershipRequired", "UserErrorParentSiteInactive", "UserErrorRoleScopeInvalid",
             "RoleEditTitle", "RoleDetailTitle", "RoleSystemBadge", "RoleCustomBadge", "RoleConfirmDisable", "RoleConfirmEnable",
             "RoleBuiltInOrgAdmin", "RoleBuiltInSiteOperator", "RoleBuiltInProvisioner",
             "PermPageOrganizations", "PermPageSites", "PermPageUsers", "PermPageRoles", "PermPageAudit",
             "PermOpRead", "PermOpCreate", "PermOpUpdate", "PermOpSetActive", "PermOpUnlock", "PermOpResetPassword", "PermOpManageMemberships", "PermOpAssignFixedRoles", "PermOpManageRoles", "PermOpAssignPermissions",
             "RoleErrorInvalidName", "RoleErrorDuplicateName", "RoleErrorSystemImmutable", "RoleErrorPermissionUnknown", "RoleErrorPermissionScopeInvalid"]);
        Assert.Equal(Contracts[nameof(SharedResources)].Keys.Order(), used[nameof(SharedResources)].Order());
    }

    [Fact]
    public void Currency_and_sample_mode_resource_maps_are_exhaustive_for_both_cultures()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Catalogs", "Localization");
        var currencyKeys = DisplayFormatter.SupportedCurrencyCodes.Select(DisplayFormatter.CurrencyResourceKey).ToArray();
        var modeValues = Enum.GetValues<SampleMode>();
        var modeKeys = modeValues.Select(DisplayFormatter.SampleModeResourceKey).ToArray();
        Assert.Equal(DisplayFormatter.SupportedCurrencyCodes.Count, currencyKeys.Distinct().Count());
        Assert.Equal(modeValues.Length, modeKeys.Distinct().Count());
        foreach (var culture in CulturePolicy.Supported)
        {
            var catalog = ReadCatalog(XDocument.Load(Path.Combine(directory, $"SampleResources.{culture}.resx")));
            Assert.All(currencyKeys.Concat(modeKeys), key => Assert.Contains(key, catalog.Keys));
        }
    }

    [Theory]
    [InlineData("<root><data name='A'><value>One</value></data><data name='A'><value>Two</value></data></root>")]
    [InlineData("<root><data name='A'><value> </value></data></root>")]
    [InlineData("<root><data><value>One</value></data></root>")]
    public void Invalid_catalog_fixtures_are_rejected(string xml)
        => Assert.Throws<InvalidDataException>(() => ReadCatalog(XDocument.Parse(xml)));

    [Theory]
    [InlineData("<root><data name='A'><value>One</value></data></root>")]
    [InlineData("<root><data name='A'><value>One</value></data><data name='B'><value>Two</value></data><data name='C'><value>Extra</value></data></root>")]
    public void Missing_or_extra_key_fixtures_are_rejected(string xml)
        => Assert.Throws<InvalidDataException>(() => RequireKeys(ReadCatalog(XDocument.Parse(xml)), ["A", "B"]));

    [Theory]
    [InlineData("{0", "{0}")]
    [InlineData("{0}", "{1}")]
    [InlineData("{0:N2}", "{0:N3}")]
    [InlineData("{0}", "No parameter")]
    public void Malformed_or_incompatible_placeholder_fixtures_are_rejected(string first, string second)
    {
        var rejected = false;
        try { rejected = !Signature(first).SequenceEqual(Signature(second)); }
        catch (FormatException) { rejected = true; }
        Assert.True(rejected);
    }

    [Fact]
    public void Escaped_braces_reordering_and_repeated_parameters_are_supported()
    {
        Assert.Equal(Signature("{{label}} {0:N2} {1} {0:N2}"), Signature("{1} {{{0:N2}}}"));
        Assert.Empty(Signature("{{0}}"));
    }

    private static Dictionary<string, string> ReadCatalog(XDocument document)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in document.Root!.Elements("data"))
        {
            var name = (string?)data.Attribute("name");
            var value = (string?)data.Element("value");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value) || !result.TryAdd(name, value))
                throw new InvalidDataException("Invalid resource entry");
        }
        return result;
    }

    private static void RequireKeys(Dictionary<string, string> catalog, IEnumerable<string> keys)
    {
        if (!catalog.Keys.Order().SequenceEqual(keys.Order()))
            throw new InvalidDataException("Resource key mismatch");
    }

    private static (int Index, string Format)[] Signature(string message)
    {
        _ = CompositeFormat.Parse(message);
        var result = new HashSet<(int Index, string Format)>();
        for (var position = 0; position < message.Length; position++)
        {
            if (message[position] != '{') continue;
            if (position + 1 < message.Length && message[position + 1] == '{')
            {
                position++;
                continue;
            }
            var end = message.IndexOf('}', position);
            var token = message[(position + 1)..end];
            var match = Regex.Match(token, @"^(?<index>\d+)(?:\s*,\s*-?\d+)?(?:\:(?<format>.*))?$");
            if (!match.Success) throw new FormatException();
            result.Add((int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture), match.Groups["format"].Value));
            position = end;
        }
        return result.OrderBy(x => x.Index).ThenBy(x => x.Format, StringComparer.Ordinal).ToArray();
    }
}
