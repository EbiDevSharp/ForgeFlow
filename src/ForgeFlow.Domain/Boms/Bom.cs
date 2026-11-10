namespace ForgeFlow.Domain.Boms;

/// <summary>Organization-shared planned manufacturing BOM header tied to a manufacturable PrimaryOutputItem.</summary>
public sealed class Bom
{
    private Bom()
    {
        Code = null!;
        Name = null!;
    }

    private Bom(long organizationId, string? code, string? name, long primaryOutputItemId)
    {
        if (organizationId <= 0)
            throw new BomRuleException(BomErrors.NotFound);
        if (primaryOutputItemId <= 0)
            throw new BomRuleException(BomErrors.PrimaryItemInvalid);

        OrganizationId = organizationId;
        Code = BomText.Code(code);
        Name = BomText.Name(name);
        PrimaryOutputItemId = primaryOutputItemId;
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public long PrimaryOutputItemId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Bom Create(long organizationId, string? code, string? name, long primaryOutputItemId)
        => new(organizationId, code, name, primaryOutputItemId);

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
