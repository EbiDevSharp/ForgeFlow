using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Boms;
using ForgeFlow.Domain.Units;

namespace ForgeFlow.Application.Boms;

public static class BomPermissions
{
    public const string BomsRead = "Boms.Read";
    public const string BomsManage = "Boms.Manage";
}

public sealed record BomSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    long PrimaryOutputItemId,
    string PrimaryOutputItemCode,
    string PrimaryOutputItemName,
    string PrimaryOutputBaseUnitSymbol,
    bool IsActive,
    int VersionCount,
    string RowVersion);

public sealed record BomVersionSummary(
    long Id,
    long OrganizationId,
    long BomId,
    int VersionNumber,
    BomVersionStatus Status,
    decimal BaseOutputQuantity,
    long BaseOutputUnitId,
    string BaseOutputUnitCode,
    string BaseOutputUnitSymbol,
    DateOnly EffectiveFromDate,
    DateOnly? EffectiveToDate,
    string? Notes,
    bool HasApplicability,
    bool ApplicabilityIsActive,
    int ConditionCount,
    string RowVersion);

public sealed record BomInputSummary(
    long Id,
    int Sequence,
    long ItemId,
    string ItemCode,
    string ItemName,
    decimal Quantity,
    long UnitId,
    string UnitCode,
    string UnitSymbol,
    decimal ScrapPercentage,
    bool IsOptional,
    string? Notes);

public sealed record BomOutputSummary(
    long Id,
    int Sequence,
    long ItemId,
    string ItemCode,
    string ItemName,
    BomOutputType OutputType,
    decimal Quantity,
    long UnitId,
    string UnitCode,
    string UnitSymbol);

public sealed record BomApplicabilityConditionSummary(
    long ContextDimensionId,
    string DimensionCode,
    string DimensionName,
    bool DimensionIsActive,
    ApplicabilityOperator Operator,
    string Value);

public sealed record BomApplicabilityDetails(
    string Name,
    bool IsActive,
    IReadOnlyList<BomApplicabilityConditionSummary> Conditions);

public sealed record BomVersionDetails(
    BomVersionSummary Summary,
    IReadOnlyList<BomInputSummary> Inputs,
    IReadOnlyList<BomOutputSummary> Outputs,
    BomApplicabilityDetails? Applicability);

public sealed record BomDetails(
    BomSummary Summary,
    IReadOnlyList<BomVersionSummary> Versions);

public sealed record BomListQuery(
    string? Search = null,
    long? PrimaryOutputItemId = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateBomRequest(
    long OrganizationId,
    string? Code,
    string? Name,
    long PrimaryOutputItemId);

public sealed record UpdateBomRequest(
    long OrganizationId,
    long Id,
    string? Name,
    string? ExpectedRowVersion);

public sealed record SetBomActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record CreateBomVersionRequest(
    long OrganizationId,
    long BomId,
    int VersionNumber,
    decimal BaseOutputQuantity,
    long BaseOutputUnitId,
    DateOnly EffectiveFromDate,
    DateOnly? EffectiveToDate,
    string? Notes);

public sealed record UpdateDraftBomVersionRequest(
    long OrganizationId,
    long BomVersionId,
    decimal BaseOutputQuantity,
    long BaseOutputUnitId,
    DateOnly EffectiveFromDate,
    DateOnly? EffectiveToDate,
    string? Notes,
    string? ExpectedRowVersion);

public sealed record BomInputLineInput(
    int Sequence,
    long ItemId,
    decimal Quantity,
    long UnitId,
    decimal ScrapPercentage,
    bool IsOptional,
    string? Notes);

public sealed record BomOutputLineInput(
    int Sequence,
    long ItemId,
    BomOutputType OutputType,
    decimal Quantity,
    long UnitId);

public sealed record SaveDraftBomLinesRequest(
    long OrganizationId,
    long BomVersionId,
    IReadOnlyList<BomInputLineInput> Inputs,
    IReadOnlyList<BomOutputLineInput> Outputs,
    string? ExpectedRowVersion);

public sealed record BomApplicabilityConditionInput(
    long ContextDimensionId,
    ApplicabilityOperator Operator,
    string? Value);

public sealed record SaveDraftBomApplicabilityRequest(
    long OrganizationId,
    long BomVersionId,
    bool Enabled,
    string? Name,
    bool IsActive,
    IReadOnlyList<BomApplicabilityConditionInput> Conditions,
    string? ExpectedRowVersion);

public sealed record TransitionBomVersionStatusRequest(
    long OrganizationId,
    long BomVersionId,
    BomVersionStatus TargetStatus,
    string? ExpectedRowVersion);

public sealed record ContextDimensionSummary(
    long Id,
    long OrganizationId,
    string Code,
    string Name,
    bool IsActive,
    string RowVersion);

public sealed record ContextDimensionListQuery(
    string? Search = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record CreateContextDimensionRequest(
    long OrganizationId,
    string? Code,
    string? Name);

public sealed record UpdateContextDimensionRequest(
    long OrganizationId,
    long Id,
    string? Name,
    string? ExpectedRowVersion);

public sealed record SetContextDimensionActiveRequest(
    long OrganizationId,
    long Id,
    bool IsActive,
    string? ExpectedRowVersion);

public sealed record SeedContextDimensionsResult(int CreatedCount);

public sealed record ResolveBomRequest(
    long OrganizationId,
    long ItemId,
    DateOnly ProductionDate,
    IReadOnlyDictionary<string, string>? ContextValues = null);

public sealed record BomResolutionResultDto(
    long BomId,
    string BomCode,
    string BomName,
    long BomVersionId,
    int VersionNumber,
    decimal BaseOutputQuantity,
    long BaseOutputUnitId,
    string BaseOutputUnitCode,
    string BaseOutputUnitSymbol,
    DateOnly EffectiveFromDate,
    DateOnly? EffectiveToDate);

public sealed record ExplodeBomRequest(
    long OrganizationId,
    long ItemId,
    DateOnly ProductionDate,
    decimal RequestedQuantity,
    long? RequestedUnitId = null,
    IReadOnlyDictionary<string, string>? ContextValues = null);

public sealed record BomExplosionLineDto(
    int Level,
    long ParentItemId,
    string ParentItemCode,
    long ItemId,
    string ItemCode,
    string ItemName,
    decimal NetQuantity,
    decimal ScrapPercentage,
    decimal GrossQuantity,
    long UnitId,
    string UnitCode,
    string UnitSymbol,
    bool IsOptional,
    bool IsSubAssembly,
    long? ResolvedBomVersionId);

public sealed record BomExplosionOutputLineDto(
    int Level,
    long ItemId,
    string ItemCode,
    string ItemName,
    BomOutputType OutputType,
    decimal ProducedQuantity,
    long UnitId,
    string UnitCode,
    string UnitSymbol);

public sealed record BomExplosionResultDto(
    long RootItemId,
    string RootItemCode,
    string RootItemName,
    decimal RequestedQuantity,
    long RequestedUnitId,
    string RequestedUnitCode,
    string RequestedUnitSymbol,
    long RootBomVersionId,
    int RootVersionNumber,
    IReadOnlyList<BomExplosionLineDto> ConsumedInputs,
    IReadOnlyList<BomExplosionOutputLineDto> ProducedOutputs);

public sealed record BomResolutionDataset(
    long ItemId,
    string ItemCode,
    string ItemName,
    long BaseUnitId,
    string BaseUnitCode,
    string BaseUnitSymbol,
    bool ItemIsActive,
    IReadOnlyList<BomVersionCandidateRecord> Candidates,
    IReadOnlyList<UnitConversionEdge> GlobalUnitEdges);

public sealed record BomVersionCandidateRecord(
    long BomId,
    string BomCode,
    string BomName,
    long PrimaryOutputItemId,
    BomVersionDetails Details,
    IReadOnlyList<UnitConversionEdge> ItemUnitEdges);

public sealed class BomStoreUnavailableException(Exception inner)
    : Exception(BomErrors.PersistenceUnavailable, inner);

public interface IBomOperationsStore
{
    Task<OrganizationPage<BomSummary>> ListBoms(long organizationId, BomListQuery query, CancellationToken ct);
    Task<BomDetails?> GetBom(long organizationId, long id, CancellationToken ct);
    Task<BomVersionDetails?> GetBomVersion(long organizationId, long bomVersionId, CancellationToken ct);
    Task<OrganizationResult<BomDetails>> CreateBom(CreateBomRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<BomDetails>> UpdateBom(long organizationId, long id, byte[] version, string? name, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<BomDetails>> SetBomActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationResult<BomVersionDetails>> CreateBomVersion(CreateBomVersionRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<BomVersionDetails>> UpdateDraftBomVersion(long organizationId, long bomVersionId, byte[] version, UpdateDraftBomVersionRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<BomVersionDetails>> SaveDraftBomLines(long organizationId, long bomVersionId, byte[] version, IReadOnlyList<BomInputLineInput> inputs, IReadOnlyList<BomOutputLineInput> outputs, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<BomVersionDetails>> SaveDraftBomApplicability(long organizationId, long bomVersionId, byte[] version, SaveDraftBomApplicabilityRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<BomVersionDetails>> TransitionBomVersionStatus(long organizationId, long bomVersionId, byte[] version, BomVersionStatus targetStatus, OrganizationWriteAccess access, CancellationToken ct);

    Task<OrganizationPage<ContextDimensionSummary>> ListContextDimensions(long organizationId, ContextDimensionListQuery query, CancellationToken ct);
    Task<IReadOnlyList<ContextDimensionSummary>> ListActiveContextDimensions(long organizationId, CancellationToken ct);
    Task<ContextDimensionSummary?> GetContextDimension(long organizationId, long id, CancellationToken ct);
    Task<OrganizationResult<ContextDimensionSummary>> CreateContextDimension(CreateContextDimensionRequest request, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ContextDimensionSummary>> UpdateContextDimension(long organizationId, long id, byte[] version, string? name, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<ContextDimensionSummary>> SetContextDimensionActive(long organizationId, long id, byte[] version, bool isActive, OrganizationWriteAccess access, CancellationToken ct);
    Task<OrganizationResult<SeedContextDimensionsResult>> SeedStandardContextDimensions(long organizationId, OrganizationWriteAccess access, CancellationToken ct);

    Task<BomResolutionDataset?> LoadItemBomResolutionDataset(long organizationId, long itemId, CancellationToken ct);
    Task<IReadOnlyList<UnitConversionEdge>> ListItemUnitEdges(long organizationId, long itemId, CancellationToken ct);
}
