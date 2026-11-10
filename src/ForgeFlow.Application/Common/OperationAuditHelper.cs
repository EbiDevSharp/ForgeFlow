using System.Text.Encodings.Web;
using System.Text.Json;
using ForgeFlow.Application.Diagnostics;
using ForgeFlow.Application.Organizations;
using ForgeFlow.Domain.Diagnostics;

namespace ForgeFlow.Application.Common;

internal static class OperationAuditHelper
{
    private static readonly JsonSerializerOptions SafeJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task RecordAsync(
        IUserOperationRecorder? recorder,
        IOperationContextAccessor? opContext,
        string operationCode,
        UserOperationOutcome outcome,
        TrustedActor? actor,
        OrganizationScope? scope,
        long? organizationId = null,
        long? siteId = null,
        string? targetEntityType = null,
        string? targetEntityRef = null,
        string? failureCode = null,
        object? metadata = null)
    {
        if (recorder is null) return;
        try
        {
            var resolvedOrgId = organizationId ?? scope switch
            {
                OrganizationScope.Organization o => o.OrganizationId,
                OrganizationScope.Site s => s.OrganizationId,
                _ => (long?)null
            };
            var resolvedSiteId = siteId ?? (scope is OrganizationScope.Site st ? st.SiteId : null);
            if (!resolvedOrgId.HasValue)
                resolvedSiteId = null;

            var corr = opContext?.GetCurrent().CorrelationId;
            var json = metadata is null ? "{}" : JsonSerializer.Serialize(metadata, SafeJson);

            await recorder.RecordAsync(new UserOperationRecordRequest(
                operationCode,
                outcome,
                actor?.ActorId,
                resolvedOrgId,
                resolvedSiteId,
                corr,
                targetEntityType,
                targetEntityRef,
                failureCode,
                json), CancellationToken.None);
        }
        catch
        {
            // Never disrupt user flows when operation telemetry fails.
        }
    }

    public static UserOperationOutcome MapFailureOutcome(string? errorCode)
    {
        if (string.IsNullOrEmpty(errorCode))
            return UserOperationOutcome.BusinessRuleRejected;
        if (errorCode.EndsWith("AccessDenied", StringComparison.Ordinal)
            || errorCode.EndsWith("ActorRequired", StringComparison.Ordinal))
            return UserOperationOutcome.AccessDenied;
        if (errorCode.EndsWith("ConcurrencyConflict", StringComparison.Ordinal))
            return UserOperationOutcome.ConcurrencyConflict;
        if (errorCode.EndsWith("PersistenceUnavailable", StringComparison.Ordinal)
            || errorCode.EndsWith("OutcomeUnknown", StringComparison.Ordinal))
            return UserOperationOutcome.UnexpectedError;
        return UserOperationOutcome.BusinessRuleRejected;
    }

    public static async Task<OrganizationResult<T>> RecordIfRejectedAsync<T>(
        OrganizationResult<T> result,
        IUserOperationRecorder? recorder,
        IOperationContextAccessor? opContext,
        string operationCode,
        TrustedActor? actor,
        OrganizationScope? scope,
        long? organizationId = null,
        long? siteId = null,
        string? targetEntityType = null,
        string? targetEntityRef = null,
        object? metadata = null)
    {
        if (result.Succeeded || recorder is null)
            return result;

        var code = result.Error!.Code;
        if (code.EndsWith("ActorRequired", StringComparison.Ordinal)
            || code.EndsWith("PersistenceUnavailable", StringComparison.Ordinal)
            || code.EndsWith("OutcomeUnknown", StringComparison.Ordinal))
        {
            return result;
        }

        await RecordAsync(
            recorder,
            opContext,
            operationCode,
            MapFailureOutcome(code),
            actor,
            scope,
            organizationId,
            siteId,
            targetEntityType,
            targetEntityRef,
            code,
            metadata);

        return result;
    }
}
