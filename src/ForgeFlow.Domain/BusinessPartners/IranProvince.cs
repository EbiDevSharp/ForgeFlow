namespace ForgeFlow.Domain.BusinessPartners;

public sealed class IranProvince
{
    private readonly List<IranCity> _cities = [];

    private IranProvince()
    {
        Name = string.Empty;
    }

    public IranProvince(int id, string name, int displayOrder)
    {
        Id = id;
        Name = name;
        DisplayOrder = displayOrder;
    }

    public int Id { get; private set; }
    public string Name { get; private set; }
    public int DisplayOrder { get; private set; }
    public IReadOnlyCollection<IranCity> Cities => _cities.AsReadOnly();
}
