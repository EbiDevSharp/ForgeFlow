namespace ForgeFlow.Domain.Units;

/// <summary>Organization-scoped currency master for financial & procurement documents.</summary>
public sealed class Currency
{
    private Currency() { Code = string.Empty; Name = string.Empty; Symbol = string.Empty; }

    private Currency(long organizationId, string code, string name, string symbol, byte precisionScale)
    {
        if (organizationId <= 0)
            throw new UnitRuleException(UnitErrors.ParentOrganizationInactive, ("organizationId", organizationId));

        OrganizationId = organizationId;
        Code = code;
        Name = name;
        Symbol = symbol;
        PrecisionScale = precisionScale;
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string Symbol { get; private set; }
    public byte PrecisionScale { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Currency Create(
        long organizationId,
        string? code,
        string? name,
        string? symbol,
        byte precisionScale)
    {
        if (precisionScale > UnitText.MaxPrecisionScale)
            throw new UnitRuleException(UnitErrors.PrecisionInvalid, ("maxPrecision", UnitText.MaxPrecisionScale));

        return new Currency(
            organizationId,
            UnitText.CurrencyCode(code),
            UnitText.Name(name),
            UnitText.Symbol(symbol),
            precisionScale);
    }

    public bool Update(string? name, string? symbol, byte precisionScale)
    {
        if (!IsActive)
            throw new UnitRuleException(UnitErrors.CurrencyInactive);
        if (precisionScale > UnitText.MaxPrecisionScale)
            throw new UnitRuleException(UnitErrors.PrecisionInvalid, ("maxPrecision", UnitText.MaxPrecisionScale));

        var normalizedName = UnitText.Name(name);
        var normalizedSymbol = UnitText.Symbol(symbol);
        if (Name == normalizedName && Symbol == normalizedSymbol && PrecisionScale == precisionScale)
            return false;

        Name = normalizedName;
        Symbol = normalizedSymbol;
        PrecisionScale = precisionScale;
        return true;
    }

    public bool SetActive(bool active)
    {
        if (IsActive == active) return false;
        IsActive = active;
        return true;
    }
}
