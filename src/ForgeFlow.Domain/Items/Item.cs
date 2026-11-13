using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.Items;

public enum QualityControlMode : byte
{
    None = 0,
    IncomingReceipts = 1,
    ProductionReceipts = 2,
    AllReceipts = 3
}

/// <summary>Organization-shared universal Item Master with operational capabilities.</summary>
public sealed class Item : IHasDomainEvents, IHasRowVersion
{
    private readonly List<IDomainEvent> domainEvents = [];

    private Item()
    {
        Code = null!;
        Name = null!;
    }

    private Item(
        long organizationId,
        string? code,
        string? name,
        string? description,
        long baseUnitId,
        bool canBePurchased,
        bool canBeManufactured,
        bool canBeSold,
        bool canBeConsumed,
        bool requiresLotTracking,
        QualityControlMode qualityControlMode)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(organizationId);
        if (baseUnitId <= 0)
            throw new ItemRuleException(ItemErrors.BaseUnitNotFound, ("field", "BaseUnitId"));
        if (!Enum.IsDefined(qualityControlMode))
            throw new ItemRuleException(ItemErrors.QualityControlModeInvalid, ("field", "QualityControlMode"));

        OrganizationId = organizationId;
        Code = ItemText.Code(code);
        Name = ItemText.Name(name);
        Description = ItemText.OptionalText(description, ItemText.DescriptionMaxLength, ItemErrors.DescriptionInvalid, "Description");
        BaseUnitId = baseUnitId;
        CanBePurchased = canBePurchased;
        CanBeManufactured = canBeManufactured;
        CanBeSold = canBeSold;
        CanBeConsumed = canBeConsumed;
        RequiresLotTracking = requiresLotTracking;
        QualityControlMode = qualityControlMode;
        IsActive = true;
        domainEvents.Add(new ItemCreatedDomainEvent(organizationId, Code, DateTime.UtcNow));
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => domainEvents;
    public void ClearDomainEvents() => domainEvents.Clear();

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public long BaseUnitId { get; private set; }
    public bool CanBePurchased { get; private set; }
    public bool CanBeManufactured { get; private set; }
    public bool CanBeSold { get; private set; }
    public bool CanBeConsumed { get; private set; }
    public bool RequiresLotTracking { get; private set; }
    public QualityControlMode QualityControlMode { get; private set; }
    public bool IsActive { get; private set; } = true;
    public byte[] RowVersion { get; private set; } = [];

    public static Item Create(
        long organizationId,
        string? code,
        string? name,
        string? description,
        long baseUnitId,
        bool canBePurchased,
        bool canBeManufactured,
        bool canBeSold,
        bool canBeConsumed,
        bool requiresLotTracking = false,
        QualityControlMode qualityControlMode = QualityControlMode.None)
        => new(organizationId, code, name, description, baseUnitId, canBePurchased, canBeManufactured, canBeSold, canBeConsumed, requiresLotTracking, qualityControlMode);

    public bool Update(
        string? name,
        string? description,
        long baseUnitId,
        bool canBePurchased,
        bool canBeManufactured,
        bool canBeSold,
        bool canBeConsumed,
        bool requiresLotTracking = false,
        QualityControlMode qualityControlMode = QualityControlMode.None)
    {
        if (!IsActive)
            throw new ItemRuleException(ItemErrors.ItemInactive);
        if (baseUnitId <= 0)
            throw new ItemRuleException(ItemErrors.BaseUnitNotFound, ("field", "BaseUnitId"));
        if (!Enum.IsDefined(qualityControlMode))
            throw new ItemRuleException(ItemErrors.QualityControlModeInvalid, ("field", "QualityControlMode"));

        var normalizedName = ItemText.Name(name);
        var normalizedDescription = ItemText.OptionalText(description, ItemText.DescriptionMaxLength, ItemErrors.DescriptionInvalid, "Description");

        if (Name == normalizedName
            && Description == normalizedDescription
            && BaseUnitId == baseUnitId
            && CanBePurchased == canBePurchased
            && CanBeManufactured == canBeManufactured
            && CanBeSold == canBeSold
            && CanBeConsumed == canBeConsumed
            && RequiresLotTracking == requiresLotTracking
            && QualityControlMode == qualityControlMode)
        {
            return false;
        }

        Name = normalizedName;
        Description = normalizedDescription;
        BaseUnitId = baseUnitId;
        CanBePurchased = canBePurchased;
        CanBeManufactured = canBeManufactured;
        CanBeSold = canBeSold;
        CanBeConsumed = canBeConsumed;
        RequiresLotTracking = requiresLotTracking;
        QualityControlMode = qualityControlMode;
        domainEvents.Add(new ItemUpdatedDomainEvent(OrganizationId, Code, DateTime.UtcNow));
        return true;
    }

    public bool Activate()
    {
        if (IsActive) return false;
        IsActive = true;
        domainEvents.Add(new ItemStatusChangedDomainEvent(OrganizationId, Code, true, DateTime.UtcNow));
        return true;
    }

    public bool Deactivate()
    {
        if (!IsActive) return false;
        IsActive = false;
        domainEvents.Add(new ItemStatusChangedDomainEvent(OrganizationId, Code, false, DateTime.UtcNow));
        return true;
    }
}

public sealed record ItemCreatedDomainEvent(long OrganizationId, string ItemCode, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record ItemUpdatedDomainEvent(long OrganizationId, string ItemCode, DateTime OccurredAtUtc) : IDomainEvent;
public sealed record ItemStatusChangedDomainEvent(long OrganizationId, string ItemCode, bool IsActive, DateTime OccurredAtUtc) : IDomainEvent;
