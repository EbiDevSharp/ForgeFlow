using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.Routings;

/// <summary>Site-scoped physical or logical manufacturing work center / station.</summary>
public sealed class WorkCenter : IHasRowVersion
{
    private WorkCenter() { Code = null!; Name = null!; }

    private WorkCenter(
        long organizationId,
        long siteId,
        string? code,
        string? name,
        WorkCenterType workCenterType,
        decimal capacityUnitsPerHour,
        decimal dailyAvailableHours,
        decimal? hourlyCostRate,
        string? notes)
    {
        if (organizationId <= 0)
            throw new RoutingRuleException(RoutingErrors.NotFound);
        if (siteId <= 0)
            throw new RoutingRuleException(RoutingErrors.SiteInvalid);
        if (!Enum.IsDefined(workCenterType))
            throw new RoutingRuleException(RoutingErrors.TypeInvalid);

        OrganizationId = organizationId;
        SiteId = siteId;
        Code = RoutingText.Code(code, RoutingErrors.WorkCenterCodeInvalid);
        Name = RoutingText.RequiredName(name);
        WorkCenterType = workCenterType;
        CapacityUnitsPerHour = RoutingText.PositiveCapacity(capacityUnitsPerHour);
        DailyAvailableHours = RoutingText.DailyHours(dailyAvailableHours);
        HourlyCostRate = RoutingText.OptionalHourlyCost(hourlyCostRate);
        Notes = RoutingText.OptionalNotes(notes);
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long SiteId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public WorkCenterType WorkCenterType { get; private set; }
    public decimal CapacityUnitsPerHour { get; private set; }
    public decimal DailyAvailableHours { get; private set; }
    public decimal? HourlyCostRate { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public byte[] RowVersion { get; private set; } = [];

    public static WorkCenter Create(
        long organizationId,
        long siteId,
        string? code,
        string? name,
        WorkCenterType workCenterType,
        decimal capacityUnitsPerHour,
        decimal dailyAvailableHours,
        decimal? hourlyCostRate,
        string? notes)
        => new(organizationId, siteId, code, name, workCenterType, capacityUnitsPerHour, dailyAvailableHours, hourlyCostRate, notes);

    public bool Update(
        string? name,
        WorkCenterType workCenterType,
        decimal capacityUnitsPerHour,
        decimal dailyAvailableHours,
        decimal? hourlyCostRate,
        string? notes)
    {
        if (!IsActive)
            throw new RoutingRuleException(RoutingErrors.WorkCenterInactive);
        if (!Enum.IsDefined(workCenterType))
            throw new RoutingRuleException(RoutingErrors.TypeInvalid);

        var nextName = RoutingText.RequiredName(name);
        var nextCapacity = RoutingText.PositiveCapacity(capacityUnitsPerHour);
        var nextHours = RoutingText.DailyHours(dailyAvailableHours);
        var nextCost = RoutingText.OptionalHourlyCost(hourlyCostRate);
        var nextNotes = RoutingText.OptionalNotes(notes);

        if (string.Equals(Name, nextName, StringComparison.Ordinal)
            && WorkCenterType == workCenterType
            && CapacityUnitsPerHour == nextCapacity
            && DailyAvailableHours == nextHours
            && HourlyCostRate == nextCost
            && string.Equals(Notes, nextNotes, StringComparison.Ordinal))
        {
            return false;
        }

        Name = nextName;
        WorkCenterType = workCenterType;
        CapacityUnitsPerHour = nextCapacity;
        DailyAvailableHours = nextHours;
        HourlyCostRate = nextCost;
        Notes = nextNotes;
        return true;
    }

    public bool SetActive(bool active)
    {
        if (IsActive == active) return false;
        IsActive = active;
        return true;
    }
}
