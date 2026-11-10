namespace ForgeFlow.Domain.Boms;

/// <summary>Extensible manufacturing context dimension per organization (e.g., PLANT, PRODUCTION_LINE, MARKET).</summary>
public sealed class ContextDimension
{
    private ContextDimension()
    {
        Code = null!;
        Name = null!;
    }

    private ContextDimension(long organizationId, string? code, string? name)
    {
        if (organizationId <= 0)
            throw new BomRuleException(BomErrors.NotFound);

        OrganizationId = organizationId;
        Code = BomText.Code(code);
        Name = BomText.Name(name);
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static ContextDimension Create(long organizationId, string? code, string? name)
        => new(organizationId, code, name);

    public bool Rename(string? name)
    {
        if (!IsActive)
            throw new BomRuleException(BomErrors.BomInactive);

        var normalized = BomText.Name(name);
        if (Name == normalized) return false;
        Name = normalized;
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
