namespace ForgeFlow.Domain.Quality;

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
        string? notes)
    {
        if (organizationId <= 0) throw new QualityRuleException(QualityErrors.ParentOrganizationInactive);
        if (lineNumber <= 0) throw new QualityRuleException(QualityErrors.LineNotFound);

        OrganizationId = organizationId;
        QualityInspectionId = qualityInspectionId;
        LineNumber = lineNumber;
        ParameterName = QualityText.ParameterName(parameterName);
        ExpectedSpec = QualityText.OptionalSpecOrValue(expectedSpec);
        MeasuredValue = QualityText.OptionalSpecOrValue(measuredValue);
        IsPassed = isPassed;
        Notes = QualityText.OptionalNote(notes, QualityText.MaxLineNoteLength);
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
}
