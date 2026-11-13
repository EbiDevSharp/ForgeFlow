using ForgeFlow.Domain.Common;

namespace ForgeFlow.Domain.Units;

/// <summary>Organization-owned Unit of Measure aggregate.</summary>
public sealed class Unit : IHasRowVersion
{
    private Unit()
    {
        Code = null!;
        Name = null!;
        Symbol = null!;
    }

    private Unit(long organizationId, string? code, string? name, string? symbol, UnitDimension dimension, byte precisionScale)
    {
        if (organizationId <= 0)
            throw new UnitRuleException(UnitErrors.NotFound);
        if (!Enum.IsDefined(dimension))
            throw new UnitRuleException(UnitErrors.DimensionInvalid, ("field", "Dimension"));
        if (precisionScale > UnitText.MaxPrecisionScale)
            throw new UnitRuleException(UnitErrors.PrecisionInvalid, ("field", "PrecisionScale"), ("maxScale", UnitText.MaxPrecisionScale));

        OrganizationId = organizationId;
        Code = UnitText.Code(code);
        Name = UnitText.Name(name);
        Symbol = UnitText.Symbol(symbol);
        Dimension = dimension;
        PrecisionScale = precisionScale;
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string Symbol { get; private set; }
    public UnitDimension Dimension { get; private set; }
    public byte PrecisionScale { get; private set; }
    public bool IsActive { get; private set; } = true;
    public byte[] RowVersion { get; private set; } = [];

    public static Unit Create(long organizationId, string? code, string? name, string? symbol, UnitDimension dimension, byte precisionScale)
        => new(organizationId, code, name, symbol, dimension, precisionScale);

    public bool Update(string? name, string? symbol, byte precisionScale)
    {
        if (!IsActive)
            throw new UnitRuleException(UnitErrors.UnitInactive);
        if (precisionScale > UnitText.MaxPrecisionScale)
            throw new UnitRuleException(UnitErrors.PrecisionInvalid, ("field", "PrecisionScale"), ("maxScale", UnitText.MaxPrecisionScale));

        var normalizedName = UnitText.Name(name);
        var normalizedSymbol = UnitText.Symbol(symbol);
        if (Name == normalizedName && Symbol == normalizedSymbol && PrecisionScale == precisionScale)
            return false;

        Name = normalizedName;
        Symbol = normalizedSymbol;
        PrecisionScale = precisionScale;
        return true;
    }

    public bool Activate()
    {
        if (IsActive) return false;
        IsActive = true;
        return true;
    }

    public bool Deactivate()
    {
        if (!IsActive) return false;
        IsActive = false;
        return true;
    }
}
