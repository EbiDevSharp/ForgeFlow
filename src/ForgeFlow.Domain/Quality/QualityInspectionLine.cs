namespace ForgeFlow.Domain.Quality;

public sealed record QualityInspectionLineWriteData(
    string ParameterName,
    string? ExpectedSpec,
    string? MeasuredValue,
    bool IsPassed,
    string? Notes = null,
    long? QualityParameterId = null,
    string? ParameterCodeSnapshot = null,
    string? SymbolSnapshot = null,
    string? UnitSymbolSnapshot = null,
    string? TestMethodSnapshot = null,
    QualityParameterDataType DataType = QualityParameterDataType.Text,
    decimal? MinValueSnapshot = null,
    decimal? MaxValueSnapshot = null,
    decimal? TargetValueSnapshot = null,
    decimal? MeasuredNumericValue = null,
    bool IsMandatory = false);

public sealed class QualityInspectionLine
{
    private QualityInspectionLine() { }

    internal QualityInspectionLine(
        long organizationId,
        long qualityInspectionId,
        int lineNumber,
        string parameterName,
        string? expectedSpec,
        string? measuredValue,
        bool isPassed,
        string? notes,
        long? qualityParameterId = null,
        string? parameterCodeSnapshot = null,
        string? symbolSnapshot = null,
        string? unitSymbolSnapshot = null,
        string? testMethodSnapshot = null,
        QualityParameterDataType dataType = QualityParameterDataType.Text,
        decimal? minValueSnapshot = null,
        decimal? maxValueSnapshot = null,
        decimal? targetValueSnapshot = null,
        decimal? measuredNumericValue = null,
        bool isMandatory = false)
    {
        if (organizationId <= 0) throw new QualityRuleException(QualityErrors.ParentOrganizationInactive);
        if (lineNumber <= 0) throw new QualityRuleException(QualityErrors.LineNotFound);
        if (!Enum.IsDefined(dataType)) throw new QualityRuleException(QualityErrors.ParameterInvalid);
        if (minValueSnapshot.HasValue && maxValueSnapshot.HasValue && maxValueSnapshot.Value < minValueSnapshot.Value)
            throw new QualityRuleException(QualityErrors.SpecRangeInvalid);

        OrganizationId = organizationId;
        QualityInspectionId = qualityInspectionId;
        LineNumber = lineNumber;
        ParameterName = QualityText.ParameterName(parameterName);
        ExpectedSpec = QualityText.OptionalSpecOrValue(expectedSpec);
        MeasuredValue = QualityText.OptionalSpecOrValue(measuredValue);
        IsPassed = isPassed;
        Notes = QualityText.OptionalNote(notes, QualityText.MaxLineNoteLength);
        QualityParameterId = qualityParameterId is > 0 ? qualityParameterId : null;
        ParameterCodeSnapshot = string.IsNullOrWhiteSpace(parameterCodeSnapshot) ? null : QualityText.Code(parameterCodeSnapshot, QualityErrors.ParameterInvalid);
        SymbolSnapshot = string.IsNullOrWhiteSpace(symbolSnapshot) ? null : QualityText.Symbol(symbolSnapshot);
        UnitSymbolSnapshot = QualityText.OptionalUnitSymbol(unitSymbolSnapshot);
        TestMethodSnapshot = QualityText.OptionalTestMethod(testMethodSnapshot);
        DataType = dataType;
        MinValueSnapshot = minValueSnapshot.HasValue ? decimal.Round(minValueSnapshot.Value, 6, MidpointRounding.AwayFromZero) : null;
        MaxValueSnapshot = maxValueSnapshot.HasValue ? decimal.Round(maxValueSnapshot.Value, 6, MidpointRounding.AwayFromZero) : null;
        TargetValueSnapshot = targetValueSnapshot.HasValue ? decimal.Round(targetValueSnapshot.Value, 6, MidpointRounding.AwayFromZero) : null;
        MeasuredNumericValue = measuredNumericValue.HasValue ? decimal.Round(measuredNumericValue.Value, 6, MidpointRounding.AwayFromZero) : null;
        IsMandatory = isMandatory;
    }

    public long Id { get; private set; }
    public long OrganizationId { get; private set; }
    public long QualityInspectionId { get; private set; }
    public int LineNumber { get; private set; }
    public string ParameterName { get; private set; } = string.Empty;
    public string? ExpectedSpec { get; private set; }
    public string? MeasuredValue { get; private set; }
    public bool IsPassed { get; private set; }
    public string? Notes { get; private set; }

    public long? QualityParameterId { get; private set; }
    public string? ParameterCodeSnapshot { get; private set; }
    public string? SymbolSnapshot { get; private set; }
    public string? UnitSymbolSnapshot { get; private set; }
    public string? TestMethodSnapshot { get; private set; }
    public QualityParameterDataType DataType { get; private set; } = QualityParameterDataType.Text;
    public decimal? MinValueSnapshot { get; private set; }
    public decimal? MaxValueSnapshot { get; private set; }
    public decimal? TargetValueSnapshot { get; private set; }
    public decimal? MeasuredNumericValue { get; private set; }
    public bool IsMandatory { get; private set; }

    public static bool EvaluateNumericPass(decimal? min, decimal? max, decimal? measured)
    {
        if (!measured.HasValue) return false;
        if (min.HasValue && measured.Value < min.Value) return false;
        if (max.HasValue && measured.Value > max.Value) return false;
        return true;
    }
}
