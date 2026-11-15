namespace ForgeFlow.Domain.BusinessPartners;

public sealed class IranCity
{
    private IranCity()
    {
        Name = string.Empty;
    }

    public IranCity(int id, int provinceId, string name, int displayOrder)
    {
        Id = id;
        ProvinceId = provinceId;
        Name = name;
        DisplayOrder = displayOrder;
    }

    public int Id { get; private set; }
    public int ProvinceId { get; private set; }
    public string Name { get; private set; }
    public int DisplayOrder { get; private set; }
}
