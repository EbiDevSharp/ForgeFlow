namespace ForgeFlow.Domain.Items;

/// <summary>Typed attribute value assigned to an organization Item.</summary>
public sealed class ItemAttributeValue
{
    private ItemAttributeValue() { }

    private ItemAttributeValue(
        long organizationId,
        long itemId,
        long attributeDefinitionId,
        string? textValue,
        decimal? decimalValue,
        bool? booleanValue)
    {
        if (organizationId <= 0 || itemId <= 0 || attributeDefinitionId < 0)
            throw new ItemRuleException(ItemErrors.NotFound);

        OrganizationId = organizationId;
        ItemId = itemId;
        AttributeDefinitionId = attributeDefinitionId;
        TextValue = textValue;
        DecimalValue = decimalValue;
        BooleanValue = booleanValue;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long ItemId { get; private set; }
    public long AttributeDefinitionId { get; private set; }
    public string? TextValue { get; private set; }
    public decimal? DecimalValue { get; private set; }
    public bool? BooleanValue { get; private set; }

    public static ItemAttributeValue CreateValidated(
        AttributeDefinition definition,
        long itemId,
        string? rawText,
        decimal? rawDecimal,
        bool? rawBoolean)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var (t, d, b) = NormalizeValue(definition, rawText, rawDecimal, rawBoolean);
        return new ItemAttributeValue(definition.OrganizationId, itemId, definition.Id, t, d, b);
    }

    public bool UpdateValidated(AttributeDefinition definition, string? rawText, decimal? rawDecimal, bool? rawBoolean)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var (t, d, b) = NormalizeValue(definition, rawText, rawDecimal, rawBoolean);
        if (TextValue == t && DecimalValue == d && BooleanValue == b)
            return false;

        TextValue = t;
        DecimalValue = d;
        BooleanValue = b;
        return true;
    }

    private static (string? Text, decimal? Decimal, bool? Boolean) NormalizeValue(
        AttributeDefinition definition,
        string? rawText,
        decimal? rawDecimal,
        bool? rawBoolean)
    {
        switch (definition.DataType)
        {
            case AttributeDataType.Text:
                var normalized = ItemText.OptionalText(rawText, ItemText.AttributeTextMaxLength, ItemErrors.AttributeValueInvalid, "Value");
                if (definition.IsRequired && normalized is null)
                    throw new ItemRuleException(ItemErrors.AttributeRequired, ("code", definition.Code));
                return (normalized, null, null);

            case AttributeDataType.Decimal:
                if (rawDecimal is null)
                {
                    if (definition.IsRequired)
                        throw new ItemRuleException(ItemErrors.AttributeRequired, ("code", definition.Code));
                    return (null, null, null);
                }
                if (Math.Abs(rawDecimal.Value) > 999_999_999_999.999999m
                    || decimal.Round(rawDecimal.Value, 6, MidpointRounding.AwayFromZero) != rawDecimal.Value)
                {
                    throw new ItemRuleException(ItemErrors.AttributeValueInvalid, ("code", definition.Code));
                }
                return (null, rawDecimal, null);

            case AttributeDataType.Boolean:
                if (rawBoolean is null)
                {
                    if (definition.IsRequired)
                        throw new ItemRuleException(ItemErrors.AttributeRequired, ("code", definition.Code));
                    return (null, null, null);
                }
                return (null, null, rawBoolean);

            default:
                throw new ItemRuleException(ItemErrors.AttributeDataTypeInvalid);
        }
    }
}
