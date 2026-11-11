using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Quality;

namespace ForgeFlow.Application.Quality;

public sealed record QualityParameterSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    string Symbol,
    string? UnitSymbol,
    QualityParameterDataType DataType,
    byte DecimalPlaces,
    string? DefaultTestMethod,
    bool IsActive,
    string RowVersion);

public sealed record QualityParameterListQuery(
    bool IncludeInactive = false,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateQualityParameterRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    string? Symbol,
    string? UnitSymbol,
    QualityParameterDataType DataType,
    byte DecimalPlaces,
    string? DefaultTestMethod);

public sealed record UpdateQualityParameterRequest(
    long OrganizationId,
    long Id,
    string? Name,
    string? Symbol,
    string? UnitSymbol,
    byte DecimalPlaces,
    string? DefaultTestMethod,
    string? ExpectedRowVersion);

public sealed record SetQualityParameterActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record QualitySpecLineSummary(
    long Id,
    int Sequence,
    long QualityParameterId,
    string ParameterCode,
    string ParameterName,
    string Symbol,
    string? UnitSymbol,
    QualityParameterDataType DataType,
    byte DecimalPlaces,
    string? TestMethod,
    decimal? MinValue,
    decimal? MaxValue,
    decimal? TargetValue,
    string? ExpectedTextSpec,
    bool IsMandatory,
    string? Notes);

public sealed record QualitySpecSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    QualitySpecTargetKind TargetKind,
    long? ItemId,
    string? ItemCode,
    string? ItemName,
    long? ClassificationId,
    string? ClassificationCode,
    string? ClassificationName,
    QualitySpecCheckpoint Checkpoint,
    int Version,
    bool IsActive,
    int LineCount,
    string? Notes,
    string RowVersion);

public sealed record QualitySpecDetails(
    QualitySpecSummary Summary,
    IReadOnlyList<QualitySpecLineSummary> Lines);

public sealed record QualitySpecListQuery(
    QualitySpecTargetKind? TargetKind = null,
    QualitySpecCheckpoint? Checkpoint = null,
    bool IncludeInactive = false,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record QualitySpecLineInput(
    long QualityParameterId,
    decimal? MinValue = null,
    decimal? MaxValue = null,
    decimal? TargetValue = null,
    string? ExpectedTextSpec = null,
    bool IsMandatory = true,
    string? Notes = null);

public sealed record CreateQualitySpecRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    QualitySpecTargetKind TargetKind,
    long? ItemId,
    long? ClassificationId,
    QualitySpecCheckpoint Checkpoint,
    int Version,
    string? Notes,
    IReadOnlyList<QualitySpecLineInput> Lines);

public sealed record UpdateQualitySpecRequest(
    long OrganizationId,
    long Id,
    string? Name,
    QualitySpecCheckpoint Checkpoint,
    string? Notes,
    IReadOnlyList<QualitySpecLineInput> Lines,
    string? ExpectedRowVersion);

public sealed record SetQualitySpecActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record LotCertificateMeasurementSummary(
    long LineId,
    int LineNumber,
    string ParameterName,
    string? ParameterCode,
    string? Symbol,
    string? UnitSymbol,
    string? TestMethod,
    QualityParameterDataType DataType,
    decimal? MinValue,
    decimal? MaxValue,
    decimal? TargetValue,
    string? ExpectedSpec,
    decimal? MeasuredNumericValue,
    string? MeasuredValue,
    bool IsPassed,
    bool IsMandatory,
    string? Notes);

public sealed record LotCertificateInspectionSummary(
    long InspectionId,
    string InspectionNumber,
    QualityInspectionStatus Status,
    QualityInspectionSourceType SourceType,
    string? CertificateNumber,
    string? DefectReasonCode,
    string? Notes,
    DateTime? InspectedAtUtc,
    string? InspectedByActorName,
    IReadOnlyList<LotCertificateMeasurementSummary> Measurements);

public sealed record LotCertificateSummary(
    long OrganizationId,
    long ItemId,
    string ItemCode,
    string ItemName,
    string LotCode,
    DateOnly? ManufactureDate,
    DateOnly? ExpirationDate,
    QualitySpecDetails? ActiveIncomingSpec,
    IReadOnlyList<LotCertificateInspectionSummary> Inspections);

public interface IQualitySpecOperationsStore
{
    Task<OrganizationPage<QualityParameterSummary>> ListParameters(long organizationId, QualityParameterListQuery query, CancellationToken ct);
    Task<QualityParameterSummary?> GetParameter(long organizationId, long id, CancellationToken ct);
    Task<OrganizationResult<QualityParameterSummary>> CreateParameter(CreateQualityParameterRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<QualityParameterSummary>> UpdateParameter(long organizationId, long id, byte[] version, UpdateQualityParameterRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<QualityParameterSummary>> SetParameterActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationPage<QualitySpecSummary>> ListSpecs(long organizationId, QualitySpecListQuery query, CancellationToken ct);
    Task<QualitySpecDetails?> GetSpec(long organizationId, long id, CancellationToken ct);
    Task<QualitySpecDetails?> ResolveApplicableSpec(long organizationId, long itemId, QualitySpecCheckpoint checkpoint, CancellationToken ct);
    Task<OrganizationResult<QualitySpecDetails>> CreateSpec(CreateQualitySpecRequest request, IReadOnlyList<QualitySpecLineInputData> resolvedLines, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<QualitySpecDetails>> UpdateSpec(long organizationId, long id, byte[] version, string name, QualitySpecCheckpoint checkpoint, string? notes, IReadOnlyList<QualitySpecLineInputData> resolvedLines, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<QualitySpecDetails>> SetSpecActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationResult<QualityInspectionDetails>> ApplySpecToInspection(long organizationId, long inspectionId, byte[] version, OrganizationWriteAccess access, CancellationToken ct);
    Task<LotCertificateSummary?> GetLotCertificate(long organizationId, long itemId, string lotCode, CancellationToken ct);
}
