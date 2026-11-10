namespace ForgeFlow.Domain.Items;

/// <summary>Associates an organization Item with a Classification node in the same organization.</summary>
public sealed class ItemClassification
{
    private ItemClassification() { }

    public ItemClassification(long organizationId, long itemId, long classificationId)
    {
        if (organizationId <= 0 || itemId <= 0 || classificationId <= 0)
            throw new ItemRuleException(ItemErrors.NotFound);

        OrganizationId = organizationId;
        ItemId = itemId;
        ClassificationId = classificationId;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long ItemId { get; private set; }
    public long ClassificationId { get; private set; }
}
