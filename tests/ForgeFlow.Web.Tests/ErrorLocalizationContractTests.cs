using System.Globalization;
using System.Reflection;
using ForgeFlow.Application.Audit;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Application.Reporting;
using ForgeFlow.Application.Sales;
using ForgeFlow.Domain.Audit;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.BusinessPartners;
using ForgeFlow.Domain.Diagnostics;
using ForgeFlow.Domain.Inventory;
using ForgeFlow.Domain.Items;
using ForgeFlow.Domain.Organizations;
using ForgeFlow.Domain.Procurement;
using ForgeFlow.Domain.ProductionOrders;
using ForgeFlow.Domain.Quality;
using ForgeFlow.Domain.Reporting;
using ForgeFlow.Domain.Roles;
using ForgeFlow.Domain.Routings;
using ForgeFlow.Domain.Sales;
using ForgeFlow.Domain.Units;
using ForgeFlow.Domain.Users;
using ForgeFlow.Web.Localization;
using ForgeFlow.Web.Roles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class ErrorLocalizationContractTests
{
    private static readonly Type[] AllErrorClasses =
    [
        typeof(AuditErrors),
        typeof(BomErrors),
        typeof(BusinessPartnerErrors),
        typeof(DiagnosticErrors),
        typeof(InventoryErrors),
        typeof(ItemErrors),
        typeof(DocumentSequenceErrors),
        typeof(OrganizationSiteErrors),
        typeof(OrganizationAccessErrors),
        typeof(ProcurementErrors),
        typeof(ProductionOrderErrors),
        typeof(QualityErrors),
        typeof(ReportingErrors),
        typeof(RolePermissionErrors),
        typeof(RoutingErrors),
        typeof(SalesErrors),
        typeof(UnitErrors),
        typeof(UserManagementErrors)
    ];

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void All_domain_and_application_error_codes_are_mapped_and_localized_in_both_cultures(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        using var scope = new CultureScope(culture);

        using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(o => o.ResourcesPath = "Resources")
            .BuildServiceProvider();

        var localizer = services.GetRequiredService<IStringLocalizer<SharedResources>>();

        var totalChecked = 0;
        foreach (var errorClass in AllErrorClasses)
        {
            var stringConstants = errorClass.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();

            Assert.NotEmpty(stringConstants);

            foreach (var code in stringConstants)
            {
                var resourceKey = RolePermissionLocalization.FailureKey(code);

                Assert.False(string.IsNullOrWhiteSpace(resourceKey),
                    $"FailureKey returned null/whitespace for {errorClass.Name}.{code}");

                Assert.NotEqual(code, resourceKey);

                var localized = localizer[resourceKey];
                Assert.False(localized.ResourceNotFound,
                    $"Resource key '{resourceKey}' mapped from '{errorClass.Name}.{code}' was not found in culture '{cultureName}'");
                Assert.False(string.IsNullOrWhiteSpace(localized.Value),
                    $"Resource value for '{resourceKey}' in culture '{cultureName}' is empty");

                totalChecked++;
            }
        }

        Assert.True(totalChecked >= 440, $"Expected >= 440 error codes verified, actual: {totalChecked}");
    }

    [Fact]
    public void Sales_domain_errors_map_to_standardized_display_keys()
    {
        Assert.Equal("SalesErrorCustomerNotFound", RolePermissionLocalization.FailureKey(SalesErrors.CustomerNotFound));
        Assert.Equal("SalesErrorCustomerInactive", RolePermissionLocalization.FailureKey(SalesErrors.CustomerInactive));
        Assert.Equal("SalesErrorItemNotSellable", RolePermissionLocalization.FailureKey(SalesErrors.ItemNotSellable));
        Assert.Equal("SalesErrorEmptyOrder", RolePermissionLocalization.FailureKey(SalesErrors.EmptyOrder));
        Assert.Equal("SalesErrorEmptyDispatch", RolePermissionLocalization.FailureKey(SalesErrors.EmptyDispatch));
        Assert.Equal("SalesErrorCarrierInvalid", RolePermissionLocalization.FailureKey(SalesErrors.CarrierInvalid));
        Assert.Equal("SalesErrorTrackingNumberInvalid", RolePermissionLocalization.FailureKey(SalesErrors.TrackingNumberInvalid));
        Assert.Equal("SalesErrorLotQuarantinedOrInsufficient", RolePermissionLocalization.FailureKey(SalesErrors.LotQuarantinedOrInsufficient));
        Assert.Equal("SalesErrorNotDraft", RolePermissionLocalization.FailureKey(SalesErrors.NotDraft));
        Assert.Equal("SalesErrorHasDispatchesCannotCancel", RolePermissionLocalization.FailureKey(SalesErrors.HasDispatchesCannotCancel));
        Assert.Equal("SalesErrorOverDispatch", RolePermissionLocalization.FailureKey(SalesErrors.OverDispatch));
    }

    [Fact]
    public void DocumentSequence_domain_errors_map_to_standardized_display_keys()
    {
        Assert.Equal("DocSeqErrorDocumentTypeInvalid", RolePermissionLocalization.FailureKey(DocumentSequenceErrors.DocumentTypeInvalid));
        Assert.Equal("DocSeqErrorPrefixInvalid", RolePermissionLocalization.FailureKey(DocumentSequenceErrors.PrefixInvalid));
        Assert.Equal("DocSeqErrorSeparatorInvalid", RolePermissionLocalization.FailureKey(DocumentSequenceErrors.SeparatorInvalid));
        Assert.Equal("DocSeqErrorYearModeInvalid", RolePermissionLocalization.FailureKey(DocumentSequenceErrors.YearModeInvalid));
        Assert.Equal("DocSeqErrorPaddingInvalid", RolePermissionLocalization.FailureKey(DocumentSequenceErrors.PaddingInvalid));
        Assert.Equal("DocSeqErrorNextNumberInvalid", RolePermissionLocalization.FailureKey(DocumentSequenceErrors.NextNumberInvalid));
        Assert.Equal("DocSeqOverrideLocked", RolePermissionLocalization.FailureKey(DocumentSequenceErrors.ManualOverrideDisabled));
    }

    [Fact]
    public void BusinessPartner_domain_errors_map_to_standardized_display_keys()
    {
        Assert.Equal("BpRoleRequired", RolePermissionLocalization.FailureKey(BusinessPartnerErrors.RoleRequired));
        Assert.Equal("BpNationalCodeLengthError", RolePermissionLocalization.FailureKey(BusinessPartnerErrors.NationalCodeInvalid));
        Assert.Equal("BpCompanyNameRequired", RolePermissionLocalization.FailureKey(BusinessPartnerErrors.CompanyNameInvalid));
        Assert.Equal("BpNationalLegalIdFormatError", RolePermissionLocalization.FailureKey(BusinessPartnerErrors.NationalLegalIdInvalid));
        Assert.Equal("BpEconomicCodeFormatError", RolePermissionLocalization.FailureKey(BusinessPartnerErrors.EconomicCodeInvalid));
        Assert.Equal("BpMobileFormatError", RolePermissionLocalization.FailureKey(BusinessPartnerErrors.MobileInvalid));
        Assert.Equal("BpPhoneFormatError", RolePermissionLocalization.FailureKey(BusinessPartnerErrors.PhoneInvalid));
        Assert.Equal("BpPersonTypeInvalid", RolePermissionLocalization.FailureKey(BusinessPartnerErrors.PersonTypeInvalid));
        Assert.Equal("BpWebsiteInvalid", RolePermissionLocalization.FailureKey(BusinessPartnerErrors.WebsiteInvalid));
    }

    [Fact]
    public void Lower_layer_store_and_diagnostic_exceptions_use_standardized_error_codes()
    {
        var inner = new InvalidOperationException("sql failure");

        var auditEx = new AuditStoreUnavailableException(inner);
        Assert.Equal(AuditErrors.PersistenceUnavailable, auditEx.Message);

        var diagEx = new DiagnosticStoreUnavailableException(inner);
        Assert.Equal(DiagnosticErrors.PersistenceUnavailable, diagEx.Message);

        var reportEx = new ReportingStoreUnavailableException(inner);
        Assert.Equal(ReportingErrors.PersistenceUnavailable, reportEx.Message);

        var docSeqEx = new DocumentSequenceStoreUnavailableException();
        Assert.Equal(DocumentSequenceErrors.PersistenceUnavailable, docSeqEx.Message);

        var salesEx = new SalesStoreUnavailableException();
        Assert.Equal(SalesErrors.PersistenceUnavailable, salesEx.Message);

        var errRefEx = Assert.Throws<ArgumentException>(() => new ErrorLogRecord(
            string.Empty, Guid.NewGuid(), DateTime.UtcNow, ErrorSeverity.Error, ErrorSourceKind.HttpMiddleware,
            "Exception", null, "message", "stack", null, null, null, null, null, null));
        Assert.StartsWith(DiagnosticErrors.InvalidErrorReference, errRefEx.Message);

        var opCodeEx = Assert.Throws<ArgumentException>(() => new UserOperationLogRecord(
            DateTime.UtcNow, string.Empty, UserOperationOutcome.Succeeded, Guid.NewGuid(),
            null, null, null, null, null, null, "{}"));
        Assert.StartsWith(DiagnosticErrors.InvalidOperationCode, opCodeEx.Message);

        var siteOrgEx = Assert.Throws<ArgumentException>(() => new UserOperationLogRecord(
            DateTime.UtcNow, "Valid.Op", UserOperationOutcome.Succeeded, Guid.NewGuid(),
            null, null, 10, null, null, null, "{}"));
        Assert.StartsWith(DiagnosticErrors.SiteRequiresOrganization, siteOrgEx.Message);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUi = CultureInfo.CurrentUICulture;

        public CultureScope(CultureInfo culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
