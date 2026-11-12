using ForgeFlow.Domain.Organizations;

namespace ForgeFlow.Application.Organizations;

public static class DocumentSequencePermissions
{
    public const string Read = "DocumentSequences.Read";
    public const string Manage = "DocumentSequences.Manage";
}

public sealed record DocumentSequenceSummary(
    long Id,
    long OrganizationId,
    DocumentSequenceType DocumentType,
    string Prefix,
    string Separator,
    DocumentYearMode YearMode,
    byte PaddingDigits,
    long NextNumber,
    bool AllowManualOverride,
    string NextPreview,
    string RowVersion);

public sealed record UpdateDocumentSequenceRequest(
    string Prefix,
    string Separator,
    DocumentYearMode YearMode,
    byte PaddingDigits,
    long NextNumber,
    bool AllowManualOverride);

public interface IDocumentSequenceStore
{
    Task<IReadOnlyList<DocumentSequenceSummary>> ListSequences(long organizationId, DateTime utcNow, OrganizationWriteAccess? seedAccess, CancellationToken ct);
    Task<DocumentSequenceSummary> GetPreview(long organizationId, DocumentSequenceType documentType, DateTime utcNow, CancellationToken ct);
    Task<OrganizationResult<DocumentSequenceSummary>> UpdateSequence(long organizationId, DocumentSequenceType documentType, UpdateDocumentSequenceRequest request, byte[] version, DateTime utcNow, OrganizationWriteAccess access, CancellationToken ct);
}

public sealed class DocumentSequenceStoreUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
