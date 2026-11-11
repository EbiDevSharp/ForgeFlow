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
        [nameof(SharedResources)] = new Dictionary<string, int>()
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
            ["LanguagePersian"] = 0, ["LanguageEnglish"] = 0, ["UserMenu"] = 0, ["UserAccountSubtitle"] = 0, ["Logout"] = 0,
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
            ["AuditFieldKeyColumn"] = 0, ["AuditFieldValueColumn"] = 0, ["AuditSystemActor"] = 0,
            ["MasterDataNavGroup"] = 0, ["PermPageUnits"] = 0, ["PermOpManageUnits"] = 0,
            ["PermPageSelectLabel"] = 0, ["PermModuleSummary"] = 3,
            ["Units"] = 0, ["UnitsCaption"] = 0, ["UnitsTab"] = 0, ["UnitConversionsTab"] = 0,
            ["UnitCreateButton"] = 0, ["UnitCreateTitle"] = 0, ["UnitEditTitle"] = 0, ["UnitDetailTitle"] = 0,
            ["UnitConversionCreateButton"] = 0, ["UnitConversionCreateTitle"] = 0, ["UnitConversionEditTitle"] = 0, ["UnitConversionDetailTitle"] = 0,
            ["UnitsSeedButton"] = 0, ["UnitsSeededNotice"] = 2,
            ["UnitConverterButton"] = 0, ["UnitConverterTitle"] = 0, ["UnitConverterQuantityLabel"] = 0,
            ["UnitConverterCalculate"] = 0, ["UnitConverterHops"] = 2,
            ["UnitSymbolLabel"] = 0, ["UnitDimensionLabel"] = 0, ["UnitDimensionAll"] = 0,
            ["UnitDimensionMass"] = 0, ["UnitDimensionVolume"] = 0, ["UnitDimensionLength"] = 0,
            ["UnitDimensionArea"] = 0, ["UnitDimensionCount"] = 0, ["UnitDimensionTime"] = 0,
            ["UnitPrecisionLabel"] = 0, ["UnitConversionFromLabel"] = 0, ["UnitConversionToLabel"] = 0,
            ["UnitConversionFactorLabel"] = 0, ["UnitConversionRelationLabel"] = 0,
            ["UnitFilterAll"] = 0, ["UnitSearchPlaceholder"] = 0,
            ["UnitCreatedSucceeded"] = 1, ["UnitConversionCreatedSucceeded"] = 0,
            ["UnitErrorSymbolInvalid"] = 0, ["UnitErrorDimensionInvalid"] = 0, ["UnitErrorPrecisionInvalid"] = 0,
            ["UnitErrorInactive"] = 0, ["UnitErrorConversionDuplicate"] = 0, ["UnitErrorConversionSameUnit"] = 0,
            ["UnitErrorConversionFactorInvalid"] = 0, ["UnitErrorConversionPathNotFound"] = 0,
            ["UnitErrorConversionConflictingPath"] = 0, ["UnitErrorConversionQuantityInvalid"] = 0,
            ["PermPageItems"] = 0, ["PermOpManageItems"] = 0,
            ["Items"] = 0, ["ItemsCaption"] = 0, ["ItemsTab"] = 0, ["ItemsClassificationsTab"] = 0, ["ItemsAttributesTab"] = 0,
            ["ItemCreateButton"] = 0, ["ItemCreateTitle"] = 0, ["ItemEditTitle"] = 0, ["ItemDetailTitle"] = 0,
            ["ItemDescriptionLabel"] = 0, ["ItemBaseUnitLabel"] = 0, ["ItemCapabilitiesLabel"] = 0,
            ["ItemCapFilterAll"] = 0, ["ItemCapPurchase"] = 0, ["ItemCapManufacture"] = 0, ["ItemCapSell"] = 0, ["ItemCapConsume"] = 0,
            ["ItemSearchPlaceholder"] = 0, ["ItemCreatedSucceeded"] = 1,
            ["ItemSectionGeneral"] = 0, ["ItemSectionClassifications"] = 0, ["ItemSectionConversions"] = 0, ["ItemSectionAttributes"] = 0,
            ["ClassificationSetCreateButton"] = 0, ["ClassificationCreateButton"] = 0,
            ["ClassificationSetLabel"] = 0, ["ClassificationSetAll"] = 0, ["ClassificationSetAllowMultiple"] = 0,
            ["ClassificationParentLabel"] = 0, ["ClassificationFilterAll"] = 0,
            ["AttributeCreateButton"] = 0, ["AttributeDataTypeLabel"] = 0, ["AttributeDataTypeAll"] = 0,
            ["AttributeDataTypeText"] = 0, ["AttributeDataTypeDecimal"] = 0, ["AttributeDataTypeBoolean"] = 0,
            ["AttributeRequiredLabel"] = 0, ["AttributeRequiredYes"] = 0, ["AttributeRequiredNo"] = 0,
            ["AttributeBoolTrue"] = 0, ["AttributeBoolFalse"] = 0,
            ["ItemErrorDescriptionInvalid"] = 0, ["ItemErrorBaseUnitInvalid"] = 0, ["ItemErrorCapabilityRequired"] = 0,
            ["ItemErrorClassificationParentInvalid"] = 0, ["ItemErrorClassificationCircularParent"] = 0,
            ["ItemErrorAttributeValueInvalid"] = 0, ["ItemErrorAttributeRequired"] = 0,
            ["Boms"] = 0, ["BomsCaption"] = 0, ["PermPageBoms"] = 0, ["PermOpManageBoms"] = 0,
            ["BomsTab"] = 0, ["BomDimensionsTab"] = 0, ["BomSimulatorTab"] = 0,
            ["BomCreateButton"] = 0, ["BomDimCreateButton"] = 0, ["BomDimSeedButton"] = 0,
            ["BomPrimaryItemLabel"] = 0, ["BomPrimaryItemAll"] = 0, ["BomSearchPlaceholder"] = 0,
            ["BomVersionCountColumn"] = 0, ["BomVersionsAction"] = 0,
            ["BomSimulatorHeading"] = 0, ["BomSimulatorHint"] = 0, ["BomSelectItemOption"] = 0,
            ["BomProductionDateLabel"] = 0, ["BomRequestedQtyLabel"] = 0, ["BomRequestedUnitLabel"] = 0, ["BomDefaultBaseUnitOption"] = 0,
            ["BomContextPairsLabel"] = 0, ["BomAddContextPairBtn"] = 0, ["BomNoContextPairsHint"] = 0,
            ["BomDimLabel"] = 0, ["BomConditionValuePlaceholder"] = 0, ["BomRemoveRowBtn"] = 0,
            ["BomResolveBtn"] = 0, ["BomExplodeBtn"] = 0, ["BomBaseOutputSummary"] = 4,
            ["BomExplosionInputsHeading"] = 0, ["BomExplosionOutputsHeading"] = 0,
            ["BomLevelColumn"] = 0, ["BomParentItemColumn"] = 0, ["BomLineItemColumn"] = 0,
            ["BomNetQtyColumn"] = 0, ["BomScrapColumn"] = 0, ["BomGrossQtyColumn"] = 0, ["BomLineUnitColumn"] = 0,
            ["BomSubAssemblyBadge"] = 0, ["BomOptionalBadge"] = 0, ["BomOutputTypeColumn"] = 0, ["BomProducedQtyColumn"] = 0,
            ["BomCreateHeading"] = 0, ["BomCreateHint"] = 0, ["BomEditHeading"] = 0, ["BomVersionsHeading"] = 0,
            ["BomNoVersionsEmpty"] = 0, ["BomVersionNumberLabel"] = 0, ["BomBaseOutputColumn"] = 0,
            ["BomEffectiveDatesColumn"] = 0, ["BomApplicabilityColumn"] = 0, ["BomRulesCountBadge"] = 1,
            ["BomUnconditionalLabel"] = 0, ["BomOpenVersionBtn"] = 0, ["BomCreateVersionHeading"] = 0,
            ["BomBaseOutputQtyLabel"] = 0, ["BomBaseOutputUnitLabel"] = 0, ["BomEffectiveFromLabel"] = 0, ["BomEffectiveToLabel"] = 0,
            ["BomNotesLabel"] = 0, ["BomCreateVersionBtn"] = 0, ["BomVersionWorkbenchTitle"] = 0, ["BomVersionImmutableNotice"] = 0,
            ["BomActionApprove"] = 0, ["BomActionActivate"] = 0, ["BomActionSupersede"] = 0, ["BomActionObsolete"] = 0,
            ["BomVersionHeaderSection"] = 0, ["BomSaveHeaderBtn"] = 0, ["BomInputsSection"] = 0, ["BomAddInputBtn"] = 0,
            ["BomQuantityColumn"] = 0, ["BomOutputsSection"] = 0, ["BomAddOutputBtn"] = 0, ["BomSaveLinesBtn"] = 0,
            ["BomAddConditionBtn"] = 0, ["BomEnableApplicabilityLabel"] = 0, ["BomSaveApplicabilityBtn"] = 0,
            ["BomDimCreateHeading"] = 0, ["BomDimEditHeading"] = 0, ["BomDimSeededNotice"] = 1,
            ["BomStatusDraft"] = 0, ["BomStatusApproved"] = 0, ["BomStatusActive"] = 0, ["BomStatusSuperseded"] = 0, ["BomStatusObsolete"] = 0,
            ["BomOutMainProduct"] = 0, ["BomOutCoProduct"] = 0, ["BomOutByProduct"] = 0, ["BomOutWaste"] = 0,
            ["BomOpEquals"] = 0, ["BomOpNotEquals"] = 0, ["BomOpIn"] = 0, ["BomOpNotIn"] = 0,
            ["BomErrorItemInvalid"] = 0, ["BomErrorPrimaryNotManufacturable"] = 0, ["BomErrorVersionNumberInvalid"] = 0,
            ["BomErrorVersionImmutable"] = 0, ["BomErrorVersionTransitionInvalid"] = 0, ["BomErrorQuantityInvalid"] = 0,
            ["BomErrorEffectiveDatesInvalid"] = 0, ["BomErrorMainOutputRequired"] = 0, ["BomErrorInputsRequired"] = 0,
            ["BomErrorScrapInvalid"] = 0, ["BomErrorUnitInvalid"] = 0, ["BomErrorDirectSelfCycle"] = 0,
            ["BomErrorCircularDependency"] = 0, ["BomErrorApplicabilityInvalid"] = 0,
            ["BomErrorNoActiveBomFound"] = 0, ["BomErrorAmbiguousResolution"] = 0,
            ["BomDateToday"] = 0, ["BomDateClear"] = 0,
            ["BomDimStdPlant"] = 0, ["BomDimStdProductionLine"] = 0, ["BomDimStdMarket"] = 0,
            ["BomDimStdCustomer"] = 0, ["BomDimStdManufacturingMethod"] = 0,
            ["ProductionNavGroup"] = 0, ["ProductionOrders"] = 0, ["ProductionOrdersCaption"] = 0,
            ["PermPageProductionOrders"] = 0, ["PermOpManageProductionOrders"] = 0,
            ["PoCreateButton"] = 0, ["PoCreateSubmit"] = 0, ["PoSiteColumn"] = 0, ["PoAllSitesOption"] = 0,
            ["PoStatusColumn"] = 0, ["PoAllStatusesOption"] = 0,
            ["PoStatusDraft"] = 0, ["PoStatusReleased"] = 0, ["PoStatusInProgress"] = 0, ["PoStatusCompleted"] = 0, ["PoStatusCancelled"] = 0,
            ["PoOrderNumberColumn"] = 0, ["PoItemColumn"] = 0, ["PoRequestedQtyColumn"] = 0, ["PoDatesColumn"] = 0,
            ["PoPlannedStartDate"] = 0, ["PoPlannedDueDate"] = 0, ["PoBomSnapshotColumn"] = 0, ["PoActualsColumn"] = 0,
            ["PoNotesLabel"] = 0, ["PoContextSection"] = 0, ["PoContextHint"] = 0,
            ["PoCreateTitle"] = 0, ["PoWorkbenchTitle"] = 0, ["PoEditDraftSection"] = 0,
            ["PoSaveDraftButton"] = 0, ["PoRefreshBomButton"] = 0, ["PoReleaseButton"] = 0,
            ["PoReleaseHint"] = 0, ["PoFrozenSnapshotNotice"] = 0, ["PoStartButton"] = 0,
            ["PoExecutionSection"] = 0, ["PoActualProducedLabel"] = 0, ["PoActualScrapLabel"] = 0,
            ["PoRecordActualsButton"] = 0, ["PoCompleteButton"] = 0,
            ["PoCancelSection"] = 0, ["PoCancelReasonLabel"] = 0, ["PoCancelButton"] = 0,
            ["PoInputsSnapshotHeading"] = 0, ["PoOutputsSnapshotHeading"] = 0, ["PoTimelineHeading"] = 0,
            ["PoReleasedAtLabel"] = 0, ["PoStartedAtLabel"] = 0, ["PoCompletedAtLabel"] = 0, ["PoCancelledAtLabel"] = 0,
            ["PoSavedNotice"] = 0, ["PoCreatedNotice"] = 0,
            ["PoErrorSiteInvalid"] = 0, ["PoErrorSiteInactive"] = 0, ["PoErrorImmutable"] = 0,
            ["PoErrorTransitionInvalid"] = 0, ["PoErrorActualQtyInvalid"] = 0,
            ["PoErrorCancelReasonInvalid"] = 0, ["PoErrorNotesInvalid"] = 0,
            ["PoPostInventoryBtn"] = 0,
            ["InventoryNavGroup"] = 0, ["Inventory"] = 0, ["InventoryCaption"] = 0,
            ["PermPageInventory"] = 0, ["PermOpManageInventory"] = 0,
            ["InvWarehouseCreateBtn"] = 0, ["InvPostMovementBtn"] = 0,
            ["InvTabBalances"] = 0, ["InvTabMovements"] = 0, ["InvTabWarehouses"] = 0,
            ["InvWarehouseLabel"] = 0, ["InvDestWarehouseLabel"] = 0, ["InvAllWarehousesOption"] = 0,
            ["InvTxnTypeLabel"] = 0, ["InvAllTypesOption"] = 0, ["InvIncludeZeroLabel"] = 0,
            ["InvLotCodeLabel"] = 0, ["InvOnHandLabel"] = 0, ["InvLastMovedLabel"] = 0,
            ["InvDocNumberLabel"] = 0, ["InvReversedBadge"] = 0, ["InvDefaultWarehouseBadge"] = 0,
            ["InvDefaultForSiteLabel"] = 0, ["InvWarehouseCreateTitle"] = 0, ["InvWarehouseEditTitle"] = 0,
            ["InvSelectWarehouseOption"] = 0, ["InvSelectOrderOption"] = 0, ["InvSelectItemOption"] = 0,
            ["InvLinesSection"] = 0, ["InvAddLineBtn"] = 0, ["InvPostSubmitBtn"] = 0,
            ["InvTxnDetailsTitle"] = 0, ["InvEnteredQtyLabel"] = 0, ["InvBaseQtyDeltaLabel"] = 0,
            ["InvReverseSection"] = 0, ["InvReverseDocNumLabel"] = 0, ["InvReverseBtn"] = 0,
            ["InvSavedNotice"] = 0, ["InvPostedNotice"] = 0, ["InvReversedNotice"] = 0,
            ["InvTypeReceipt"] = 0, ["InvTypeIssue"] = 0, ["InvTypeAdjustment"] = 0,
            ["InvTypeTransferOut"] = 0, ["InvTypeTransferIn"] = 0, ["InvTypeProdIssue"] = 0,
            ["InvTypeProdReceipt"] = 0, ["InvTypeReversal"] = 0,
            ["InvErrorWarehouseInactive"] = 0, ["InvErrorAlreadyReversed"] = 0, ["InvErrorCannotReverseReversal"] = 0,
            ["InvErrorLinesRequired"] = 0, ["InvErrorQuantityInvalid"] = 0, ["InvErrorLotCodeInvalid"] = 0,
            ["InvErrorDestWarehouseInvalid"] = 0, ["InvErrorTransferSameWarehouse"] = 0, ["InvErrorOrderInvalid"] = 0,
            ["InvErrorOrderStateInvalid"] = 0, ["InvErrorInsufficientStock"] = 0, ["InvErrorIdempotencyInvalid"] = 0,
            ["Reports"] = 0, ["ReportsNavGroup"] = 0, ["ReportsCaption"] = 0,
            ["PermPageReports"] = 0, ["PermOpExportReports"] = 0,
            ["RptErrorOrderNotFound"] = 0, ["RptErrorExportInvalid"] = 0,
            ["RptTabShortage"] = 0, ["RptTabVariance"] = 0, ["RptExportCsvBtn"] = 0,
            ["RptDueFromLabel"] = 0, ["RptDueToLabel"] = 0, ["RptOnlyShortagesLabel"] = 0,
            ["RptKpiOpenOrders"] = 0, ["RptKpiMaterialsEvaluated"] = 0, ["RptKpiShortageItems"] = 0, ["RptKpiTotalShortage"] = 0,
            ["RptKpiEvaluatedOrders"] = 0, ["RptKpiAvgYield"] = 0, ["RptKpiOverConsumed"] = 0, ["RptKpiUnderYield"] = 0,
            ["RptColMaterial"] = 0, ["RptColItemName"] = 0, ["RptColBaseUnit"] = 0,
            ["RptColRequiredQty"] = 0, ["RptColOnHandQty"] = 0, ["RptColShortageQty"] = 0,
            ["RptColExcessQty"] = 0, ["RptColCoveragePct"] = 0, ["RptColOpenOrdersCount"] = 0,
            ["RptColPlannedVsActual"] = 0, ["RptColPlannedQty"] = 0, ["RptColScrapQty"] = 0,
            ["RptColYieldPct"] = 0, ["RptColInputsPlannedVsIssued"] = 0, ["RptColPlannedInput"] = 0,
            ["RptColIssuedQty"] = 0, ["RptColVariance"] = 0, ["RptColRemainingToIssue"] = 0,
            ["RptColConsumptionPct"] = 0, ["RptColReceivedQty"] = 0, ["RptColAchievementPct"] = 0, ["RptColPostedReceipt"] = 0,
            ["RptShortageBreakdownBtn"] = 0, ["RptShortageBreakdownTitle"] = 0, ["RptVarianceDetailTitle"] = 0,
            ["RptOrderInputsTitle"] = 0, ["RptOrderOutputsTitle"] = 0, ["RptNoShortageOrders"] = 0,
            ["RptCsvHeaderDemandOrders"] = 0, ["RptShortageFileName"] = 0, ["RptVarianceFileName"] = 0, ["RptExportSuccessNotice"] = 0,
            ["ErrorReferenceLabel"] = 0, ["ErrorCorrelationLabel"] = 0,
            ["PermPageDiagnostics"] = 0, ["PermOpExportAudit"] = 0,
            ["PermOpReadErrorLogs"] = 0, ["PermOpManageErrorLogs"] = 0,
            ["DiagErrorRetentionInvalid"] = 0,
            ["AuditCategoryOperations"] = 0, ["AuditActorFilterLabel"] = 0, ["AuditAllActorsOption"] = 0,
            ["AuditOutcomeFilterLabel"] = 0, ["AuditFromDateLabel"] = 0, ["AuditToDateLabel"] = 0,
            ["AuditExportCsvBtn"] = 0, ["AuditExportFileName"] = 0, ["AuditExportSuccessNotice"] = 0,
            ["AuditColSite"] = 0, ["AuditColCorrelation"] = 0,
            ["ErrorLogs"] = 0, ["ErrorLogsCaption"] = 0,
            ["ErrorLogsKpi24h"] = 0, ["ErrorLogsKpiCritical"] = 0, ["ErrorLogsKpiSql"] = 0, ["ErrorLogsKpiPipeline"] = 0,
            ["ErrorLogsSeverityLabel"] = 0, ["ErrorLogsSourceLabel"] = 0,
            ["ErrorLogsRefColumn"] = 0, ["ErrorLogsTypeColumn"] = 0, ["ErrorLogsMessageColumn"] = 0,
            ["ErrorLogsSqlColumn"] = 0, ["ErrorLogsPathColumn"] = 0, ["ErrorLogsDetailsTitle"] = 0,
            ["ErrorLogsStackTraceLabel"] = 0, ["ErrorLogsUserAgentLabel"] = 0,
            ["ErrorLogsSevWarning"] = 0, ["ErrorLogsSevError"] = 0, ["ErrorLogsSevCritical"] = 0,
            ["ErrorLogsSrcHttp"] = 0, ["ErrorLogsSrcCircuit"] = 0, ["ErrorLogsSrcComponent"] = 0,
            ["ErrorLogsSrcStore"] = 0, ["ErrorLogsSrcHost"] = 0,
            ["ErrorLogsPurgeBtn"] = 0, ["ErrorLogsPurgeDaysLabel"] = 0, ["ErrorLogsPurgeSuccess"] = 1,
            ["UserMenuLastLoginTitle"] = 0, ["UserMenuFirstLoginNotice"] = 0,
            ["PermPageQuality"] = 0, ["PermOpManageQuality"] = 0,
            ["QualityNavGroup"] = 0, ["QualityControl"] = 0, ["LotTraceability"] = 0, ["QualityCaption"] = 0,
            ["ItemLotTrackedBadge"] = 0, ["ItemQcRequiredBadge"] = 0, ["ItemRequiresLotTrackingLabel"] = 0,
            ["ItemQcModeLabel"] = 0, ["ItemQcModeNone"] = 0, ["ItemQcModeIncoming"] = 0,
            ["ItemQcModeProduction"] = 0, ["ItemQcModeAll"] = 0, ["ItemErrorQcModeInvalid"] = 0,
            ["InvBlockedLabel"] = 0, ["InvAvailableLabel"] = 0, ["InvBlockedShort"] = 1,
            ["InvTypeQualityScrap"] = 0, ["InvErrorLotRequired"] = 0, ["InvErrorLotExpired"] = 0,
            ["RptBlockedSubLabel"] = 2,
            ["QcCreateHoldBtn"] = 0, ["QcLotUpsertBtn"] = 0, ["QcTabInspections"] = 0, ["QcTabLots"] = 0,
            ["QcOnlyExpiringLabel"] = 0, ["QcNumberColumn"] = 0, ["QcSourceColumn"] = 0, ["QcQuantitiesColumn"] = 0,
            ["QcQtyBreakdown"] = 3, ["QcLotMfgDateLabel"] = 0, ["QcLotExpDateLabel"] = 0,
            ["QcLotExpiredBadge"] = 0, ["QcLotExpiringBadge"] = 0, ["QcCreateHoldTitle"] = 0,
            ["QcDefectCodeLabel"] = 0, ["QcDetailsTitle"] = 0, ["QcMeasurementsSection"] = 0, ["QcAddLineBtn"] = 0,
            ["QcParamNamePlaceholder"] = 0, ["QcExpectedSpecPlaceholder"] = 0, ["QcMeasuredValPlaceholder"] = 0,
            ["QcPassedCheck"] = 0, ["QcFailedLabel"] = 0, ["QcSaveMeasurementsBtn"] = 0,
            ["QcDispositionSection"] = 0, ["QcAcceptQtyLabel"] = 0, ["QcRejectScrapQtyLabel"] = 0,
            ["QcScrapDocLabel"] = 0, ["QcRecordDispositionBtn"] = 0, ["QcCancelHoldBtn"] = 0,
            ["QcHoldCreatedNotice"] = 0, ["QcDispositionSavedNotice"] = 0, ["QcHoldCancelledNotice"] = 0,
            ["QcLotUpsertTitle"] = 0, ["QcStatusPending"] = 0, ["QcStatusInReview"] = 0,
            ["QcStatusCompleted"] = 0, ["QcStatusCancelled"] = 0,
            ["QcSourceReceipt"] = 0, ["QcSourceProdReceipt"] = 0, ["QcSourceManualHold"] = 0,
            ["QcErrorTerminal"] = 0, ["QcErrorHasDispositions"] = 0, ["QcErrorDispositionExceeds"] = 0,
            ["QcErrorDefectReasonInvalid"] = 0, ["QcErrorParameterInvalid"] = 0, ["QcErrorLotDatesInvalid"] = 0,
            ["LotTraceCaption"] = 0, ["LotTraceDirectionLabel"] = 0, ["LotTraceDirBoth"] = 0,
            ["LotTraceDirBackward"] = 0, ["LotTraceDirForward"] = 0, ["LotTraceRunBtn"] = 0,
            ["LotTraceBackwardTitle"] = 0, ["LotTraceForwardTitle"] = 0,
            ["LotTraceSourceLotCol"] = 0, ["LotTraceTargetLotCol"] = 0, ["LotTraceMovementsTitle"] = 0,
            ["ProfileNavLabel"] = 0, ["MyProfileTitle"] = 0, ["MyProfileSubtitle"] = 0,
            ["ProfileUsernameLabel"] = 0, ["ProfileUsernameLockedHint"] = 0,
            ["ProfileDisplayNameLabel"] = 0, ["ProfileEmailLabel"] = 0, ["ProfilePhoneLabel"] = 0,
            ["ProfileAvatarLabel"] = 0, ["ProfileAvatarHint"] = 0,
            ["ProfileAvatarChoose"] = 0, ["ProfileAvatarRemove"] = 0,
            ["ProfileSaveSubmit"] = 0, ["ProfileSavedNotice"] = 0,
            ["ProfileDisplayNameInvalidError"] = 0, ["ProfileEmailInvalidError"] = 0,
            ["ProfilePhoneInvalidError"] = 0, ["ProfileAvatarInvalidError"] = 0, ["ProfileSaveFailedError"] = 0,
            ["MyPreferencesTitle"] = 0, ["MyPreferencesMenuSubtitle"] = 0, ["MyPreferencesSubtitle"] = 0,
            ["PreferencesLanguageTitle"] = 0, ["PreferencesLanguageHint"] = 0,
            ["PreferencesCalendarTitle"] = 0, ["PreferencesCalendarSamplePrefix"] = 1,
            ["PreferencesCalendarPersian"] = 0, ["PreferencesCalendarGregorian"] = 0,
            ["PreferencesThemeTitle"] = 0, ["PreferencesThemeHint"] = 0,
            ["PreferencesThemeLight"] = 0, ["PreferencesThemeDark"] = 0, ["PreferencesThemeSystem"] = 0,
            ["PreferencesTimeZoneTitle"] = 0, ["PreferencesTimeZoneHint"] = 0,
            ["PreferencesResetButton"] = 0, ["PreferencesSaveButton"] = 0,
            ["PreferencesSavedNotice"] = 0, ["PreferencesSaveFailedError"] = 0,
            ["UserMenuActiveSiteLabel"] = 0, ["UserMenuViewSessionsLink"] = 0, ["UserMenuFailedAttemptsWarning"] = 1,
            ["MyActivityTitle"] = 0, ["ActiveSessionsTitle"] = 0, ["ActiveSessionsSubtitle"] = 0,
            ["RevokeAllOtherSessionsButton"] = 0, ["SessionIpPrefix"] = 1, ["SessionCurrentDeviceBadge"] = 0,
            ["SessionSignOutOneButton"] = 0, ["SessionsSecurityFootnote"] = 0,
            ["SessionLastActivityNow"] = 0, ["SessionLastActivityMinutes"] = 1,
            ["SessionLastActivityHours"] = 1, ["SessionLastActivityTimestamp"] = 1,
            ["SessionRevokedNotice"] = 0, ["SessionOthersRevokedNotice"] = 1, ["SessionRevokeFailedError"] = 0,
            ["ChangePasswordSessionsLink"] = 0,
            ["MyActivitySubtitle"] = 0, ["MyActivityLockedBadge"] = 0,
            ["MyActivityRangeLabel"] = 0, ["MyActivityRange7Days"] = 0,
            ["MyActivityRange30Days"] = 0, ["MyActivityRange90Days"] = 0, ["MyActivityRangeAll"] = 0,
            ["MyActivitySearchPlaceholder"] = 0, ["MyActivityIpColumn"] = 0, ["MyActivityDeviceColumn"] = 0,
            ["MyActivityOutcomeRecorded"] = 0,
            ["NotificationsTitle"] = 0, ["NotificationsSubtitle"] = 0, ["NotifUnreadAriaCount"] = 1,
            ["NotificationsMarkAllRead"] = 0, ["NotificationsEmpty"] = 0, ["NotificationsViewAll"] = 0,
            ["NotifTabAll"] = 0, ["NotifTabUnread"] = 0, ["NotifTabSecurity"] = 0, ["NotifTabOrders"] = 0,
            ["NotifGroupToday"] = 0, ["NotifGroupOlder"] = 0,
            ["NotifPagerSummary"] = 3, ["NotifPagerPage"] = 2,
            ["NotifTitle_NewDeviceSignIn"] = 0, ["NotifBody_NewDeviceSignIn"] = 2,
            ["NotifTitle_FailedSignInAttempts"] = 1, ["NotifBody_FailedSignInAttempts"] = 1,
            ["NotifTitle_RoleOrAccessChanged"] = 0, ["NotifBody_RoleOrAccessChanged"] = 2,
            ["NotifTitle_MyOrderChanged"] = 1, ["NotifBody_MyOrderChanged"] = 2,
            ["NotifTitle_PasswordChanged"] = 0, ["NotifBody_PasswordChanged"] = 1,
            ["NotifAction_ViewSessions"] = 0, ["NotifAction_ViewActivity"] = 0, ["NotifAction_ViewOrder"] = 0,
            ["NotifTimeNow"] = 0, ["NotifTimeMinutes"] = 1, ["NotifTimeHours"] = 1,
            ["NotifTimeYesterday"] = 1, ["NotifTimeDays"] = 1,
            ["NotificationSettingsTitle"] = 0, ["NotificationSettingsSubtitle"] = 0,
            ["NotifMatrixEventColumn"] = 0, ["NotifMatrixInAppColumn"] = 0, ["NotifMatrixEmailColumn"] = 0,
            ["NotifEmailUnavailableTag"] = 0, ["NotifSmtpUnavailableHint"] = 0,
            ["NotifPref_NewDeviceSignInTitle"] = 0, ["NotifPref_NewDeviceSignInDesc"] = 0,
            ["NotifPref_FailedSignInTitle"] = 0, ["NotifPref_FailedSignInDesc"] = 0,
            ["NotifPref_RoleChangedTitle"] = 0, ["NotifPref_RoleChangedDesc"] = 0,
            ["NotifPref_MyOrdersTitle"] = 0, ["NotifPref_MyOrdersDesc"] = 0,
            ["NotifPrefsSavedNotice"] = 0, ["NotifPrefsSaveFailed"] = 0,
            ["HelpShortcutsTitle"] = 0, ["HelpShortcutSearch"] = 0, ["HelpShortcutCommands"] = 0,
            ["HelpShortcutAll"] = 0, ["HelpSupportTitle"] = 0,
            ["HelpUserGuideLink"] = 0, ["HelpReportIssueLink"] = 0, ["HelpContactSupportLink"] = 0,
            ["HelpUserGuideOverview"] = 0, ["HelpUserGuideShortcuts"] = 0,
            ["HelpUserGuideSessions"] = 0, ["HelpUserGuidePreferences"] = 0,
            ["HelpIssueSummaryLabel"] = 0, ["HelpIssueSummaryPlaceholder"] = 0,
            ["HelpIssueSubmitBtn"] = 0, ["HelpIssueSubmittedNotice"] = 1, ["HelpSupportDescription"] = 0,
            ["CommandPaletteTitle"] = 0, ["CommandPalettePlaceholder"] = 0,
            ["NotifRoleOrgMembership"] = 0, ["NotifRoleSiteMembership"] = 0, ["NotifRoleDirectPermissions"] = 0
        }.Concat(ForgeFlow.Application.Audit.AuditEventCatalog.All.ToDictionary(d => d.ResourceKey, _ => 0))
         .ToDictionary(kv => kv.Key, kv => kv.Value)
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
             "PermPageOrganizations", "PermPageSites", "PermPageUsers", "PermPageRoles", "PermPageAudit", "PermPageUnits", "PermPageItems", "PermPageBoms", "PermPageProductionOrders", "PermPageInventory",
             "PermOpRead", "PermOpCreate", "PermOpUpdate", "PermOpSetActive", "PermOpUnlock", "PermOpResetPassword", "PermOpManageMemberships", "PermOpAssignFixedRoles", "PermOpManageRoles", "PermOpAssignPermissions", "PermOpManageUnits", "PermOpManageItems", "PermOpManageBoms", "PermOpManageProductionOrders", "PermOpManageInventory",
             "RoleErrorInvalidName", "RoleErrorDuplicateName", "RoleErrorSystemImmutable", "RoleErrorPermissionUnknown", "RoleErrorPermissionScopeInvalid",
             "UnitDimensionMass", "UnitDimensionVolume", "UnitDimensionLength", "UnitDimensionArea", "UnitDimensionCount", "UnitDimensionTime",
             "UnitCreateButton", "UnitEditTitle", "UnitDetailTitle",
             "UnitConversionCreateButton", "UnitConversionEditTitle", "UnitConversionDetailTitle",
             "UnitErrorSymbolInvalid", "UnitErrorDimensionInvalid", "UnitErrorPrecisionInvalid", "UnitErrorInactive",
             "UnitErrorConversionDuplicate", "UnitErrorConversionSameUnit", "UnitErrorConversionFactorInvalid",
             "UnitErrorConversionPathNotFound", "UnitErrorConversionConflictingPath", "UnitErrorConversionQuantityInvalid",
             "ItemEditTitle", "ItemDetailTitle",
             "AttributeDataTypeText", "AttributeDataTypeDecimal", "AttributeDataTypeBoolean",
             "AttributeRequiredYes", "AttributeRequiredNo",
             "ItemErrorDescriptionInvalid", "ItemErrorBaseUnitInvalid", "ItemErrorCapabilityRequired",
             "ItemErrorClassificationParentInvalid", "ItemErrorClassificationCircularParent",
             "ItemErrorAttributeValueInvalid", "ItemErrorAttributeRequired",
             "BomStatusDraft", "BomStatusApproved", "BomStatusActive", "BomStatusSuperseded", "BomStatusObsolete",
             "BomOutMainProduct", "BomOutCoProduct", "BomOutByProduct", "BomOutWaste",
             "BomOpEquals", "BomOpNotEquals", "BomOpIn", "BomOpNotIn",
             "BomErrorItemInvalid", "BomErrorPrimaryNotManufacturable", "BomErrorVersionNumberInvalid",
             "BomErrorVersionImmutable", "BomErrorVersionTransitionInvalid", "BomErrorQuantityInvalid",
             "BomErrorEffectiveDatesInvalid", "BomErrorMainOutputRequired", "BomErrorInputsRequired",
             "BomErrorScrapInvalid", "BomErrorUnitInvalid", "BomErrorDirectSelfCycle",
             "BomErrorCircularDependency", "BomErrorApplicabilityInvalid",
             "BomErrorNoActiveBomFound", "BomErrorAmbiguousResolution",
             "BomDimStdPlant", "BomDimStdProductionLine", "BomDimStdMarket",
             "BomDimStdCustomer", "BomDimStdManufacturingMethod",
             "PoSavedNotice",
             "PoErrorSiteInvalid", "PoErrorSiteInactive", "PoErrorImmutable", "PoErrorTransitionInvalid",
             "PoErrorActualQtyInvalid", "PoErrorCancelReasonInvalid", "PoErrorNotesInvalid",
             "InvErrorWarehouseInactive", "InvErrorAlreadyReversed", "InvErrorCannotReverseReversal",
             "InvErrorLinesRequired", "InvErrorQuantityInvalid", "InvErrorLotCodeInvalid",
             "InvErrorDestWarehouseInvalid", "InvErrorTransferSameWarehouse", "InvErrorOrderInvalid",
             "InvErrorOrderStateInvalid", "InvErrorInsufficientStock", "InvErrorIdempotencyInvalid",
             "PermPageReports", "PermOpExportReports", "RptErrorOrderNotFound", "RptErrorExportInvalid",
             "PermPageDiagnostics", "PermOpExportAudit", "PermOpReadErrorLogs", "PermOpManageErrorLogs",
             "DiagErrorRetentionInvalid",
             "PermPageQuality", "PermOpManageQuality",
             "ItemErrorQcModeInvalid", "InvErrorLotRequired", "InvErrorLotExpired",
             "QcErrorTerminal", "QcErrorHasDispositions", "QcErrorDispositionExceeds",
             "QcErrorDefectReasonInvalid", "QcErrorParameterInvalid", "QcErrorLotDatesInvalid",
             "QcFailedLabel",
             "ProfileDisplayNameInvalidError", "ProfileEmailInvalidError", "ProfilePhoneInvalidError",
             "ProfileAvatarInvalidError", "ProfileSaveFailedError",
             "SessionLastActivityNow", "SessionLastActivityMinutes", "SessionLastActivityHours", "SessionLastActivityTimestamp",
             "MyActivityOutcomeRecorded",
             "NotifTitle_NewDeviceSignIn", "NotifBody_NewDeviceSignIn",
             "NotifTitle_FailedSignInAttempts", "NotifBody_FailedSignInAttempts",
             "NotifTitle_RoleOrAccessChanged", "NotifBody_RoleOrAccessChanged",
             "NotifTitle_MyOrderChanged", "NotifBody_MyOrderChanged",
             "NotifTitle_PasswordChanged", "NotifBody_PasswordChanged",
             "NotifAction_ViewSessions", "NotifAction_ViewActivity", "NotifAction_ViewOrder",
             "NotifTimeNow", "NotifTimeMinutes", "NotifTimeHours", "NotifTimeYesterday", "NotifTimeDays",
             "NotifPref_NewDeviceSignInTitle", "NotifPref_NewDeviceSignInDesc",
             "NotifPref_FailedSignInTitle", "NotifPref_FailedSignInDesc",
             "NotifPref_RoleChangedTitle", "NotifPref_RoleChangedDesc",
             "NotifPref_MyOrdersTitle", "NotifPref_MyOrdersDesc",
             "NotifRoleOrgMembership", "NotifRoleSiteMembership", "NotifRoleDirectPermissions"]);
        used[nameof(SharedResources)].UnionWith(ForgeFlow.Application.Audit.AuditEventCatalog.All.Select(d => d.ResourceKey));
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
