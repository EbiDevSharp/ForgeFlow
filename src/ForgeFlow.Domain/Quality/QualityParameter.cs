namespace ForgeFlow.Domain.Quality;

public enum QualityParameterDataType : byte
{
    Numeric = 1,
    Text = 2,
    Boolean = 3
}

/// <summary>Organization-wide catalog of reusable laboratory/inspection test parameters with scientific symbols and units.</summary>
public sealed class QualityParameter
{
    private QualityParameter() { }

    private QualityParameter(
        long organizationId,
        string? code,
        string? name,
        string? symbol,
        string? unitSymbol,
        QualityParameterDataType dataType,
        byte decimalPlaces,
        string? defaultTestMethod)
    {
        if (organizationId <= 0)
            throw new QualityRuleException(QualityErrors.ParentOrganizationInactive);
        if (!Enum.IsDefined(dataType))
            throw new QualityRuleException(QualityErrors.ParameterInvalid);

        OrganizationId = organizationId;
        Code = QualityText.Code(code, QualityErrors.ParameterInvalid);
        Name = QualityText.SpecName(name);
        Symbol = QualityText.Symbol(symbol);
        UnitSymbol = QualityText.OptionalUnitSymbol(unitSymbol);
        DataType = dataType;
        DecimalPlaces = dataType == QualityParameterDataType.Numeric ? QualityText.ValidateDecimalPlaces(decimalPlaces) : (byte)0;
        DefaultTestMethod = QualityText.OptionalTestMethod(defaultTestMethod);
        IsActive = true;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Symbol { get; private set; } = string.Empty;
    public string? UnitSymbol { get; private set; }
    public QualityParameterDataType DataType { get; private set; }
    public byte DecimalPlaces { get; private set; }
    public string? DefaultTestMethod { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static QualityParameter Create(
        long organizationId,
        string? code,
        string? name,
        string? symbol,
        string? unitSymbol,
        QualityParameterDataType dataType,
        byte decimalPlaces,
        string? defaultTestMethod) =>
        new(organizationId, code, name, symbol, unitSymbol, dataType, decimalPlaces, defaultTestMethod);

    public bool Update(
        string? name,
        string? symbol,
        string? unitSymbol,
        byte decimalPlaces,
        string? defaultTestMethod)
    {
        var nextName = QualityText.SpecName(name);
        var nextSymbol = QualityText.Symbol(symbol);
        var nextUnit = QualityText.OptionalUnitSymbol(unitSymbol);
        var nextPlaces = DataType == QualityParameterDataType.Numeric ? QualityText.ValidateDecimalPlaces(decimalPlaces) : (byte)0;
        var nextMethod = QualityText.OptionalTestMethod(defaultTestMethod);

        if (Name == nextName && Symbol == nextSymbol && UnitSymbol == nextUnit
            && DecimalPlaces == nextPlaces && DefaultTestMethod == nextMethod)
            return false;

        Name = nextName;
        Symbol = nextSymbol;
        UnitSymbol = nextUnit;
        DecimalPlaces = nextPlaces;
        DefaultTestMethod = nextMethod;
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
