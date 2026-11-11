using System.Collections.ObjectModel;

namespace ForgeFlow.Domain.Items;

public static class ItemErrors
{
    public const string CodeInvalid = "Items.CodeInvalid";
    public const string CodeDuplicate = "Items.CodeDuplicate";
    public const string NameInvalid = "Items.NameInvalid";
    public const string DescriptionInvalid = "Items.DescriptionInvalid";
    public const string BaseUnitNotFound = "Items.BaseUnitNotFound";
    public const string BaseUnitInactive = "Items.BaseUnitInactive";
    public const string CapabilityRequired = "Items.CapabilityRequired";
    public const string QualityControlModeInvalid = "Items.QualityControlModeInvalid";
    public const string NotFound = "Items.NotFound";
    public const string ItemInactive = "Items.Inactive";
    public const string ParentOrganizationInactive = "Items.ParentOrganizationInactive";

    public const string ClassificationSetNotFound = "Items.ClassificationSetNotFound";
    public const string ClassificationSetCodeDuplicate = "Items.ClassificationSetCodeDuplicate";
    public const string ClassificationNotFound = "Items.ClassificationNotFound";
    public const string ClassificationCodeDuplicate = "Items.ClassificationCodeDuplicate";
    public const string ClassificationParentInvalid = "Items.ClassificationParentInvalid";
    public const string ClassificationCircularParent = "Items.ClassificationCircularParent";
    public const string ClassificationInactive = "Items.ClassificationInactive";

    public const string AttributeDefinitionNotFound = "Items.AttributeDefinitionNotFound";
    public const string AttributeCodeDuplicate = "Items.AttributeCodeDuplicate";
    public const string AttributeDataTypeInvalid = "Items.AttributeDataTypeInvalid";
    public const string AttributeUnitNotFound = "Items.AttributeUnitNotFound";
    public const string AttributeUnitInactive = "Items.AttributeUnitInactive";
    public const string AttributeValueInvalid = "Items.AttributeValueInvalid";
    public const string AttributeRequired = "Items.AttributeRequired";

    public const string ItemUnitConversionDuplicate = "Items.ItemUnitConversionDuplicate";
    public const string ItemUnitConversionNotFound = "Items.ItemUnitConversionNotFound";

    public const string AccessDenied = "Items.AccessDenied";
    public const string ActorRequired = "Items.ActorRequired";
    public const string ScopeRequired = "Items.ScopeRequired";
    public const string ConcurrencyConflict = "Items.ConcurrencyConflict";
    public const string OutcomeUnknown = "Items.OutcomeUnknown";
    public const string AuditUnavailable = "Items.AuditUnavailable";
    public const string PersistenceUnavailable = "Items.PersistenceUnavailable";
}

/// <summary>Stable machine code and safe parameters; presentation supplies the localized message.</summary>
public sealed class ItemRuleException : Exception
{
    internal ItemRuleException(string code, params (string Key, object Value)[] parameters)
        : base(code)
    {
        Code = code;
        Parameters = new ReadOnlyDictionary<string, object>(parameters.ToDictionary(p => p.Key, p => p.Value));
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
}
