using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ForgeFlow.Infrastructure.Diagnostics;

public sealed record RedactedExceptionReport(
    string ExceptionType,
    int? SqlErrorNumber,
    string RedactedMessage,
    string RedactedStackTrace);

/// <summary>
/// Scrubs credentials, tokens, cookies, stamps, connection strings, and quoted literals
/// from exception chains, SQL errors, and request URLs before writing diagnostic logs.
/// </summary>
public static class DiagnosticRedactor
{
    private static readonly Regex SecretKeyValue = new(
        @"(?i)(password|pwd|token|hash|stamp|cookie|authorization|secret|apikey|api_key|connectionstring)\s*[:=]\s*[^;\r\n&]+",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]+=*",
        RegexOptions.Compiled);

    private static readonly Regex JsonSecretKeyValue = new(
        @"(?i)""[^""]*(password|pwd|token|hash|stamp|cookie|authorization|secret|apikey|api_key|connectionstring)[^""]*""\s*:\s*""[^""]*""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex QuotedLiteral = new(
        @"(['""])(.*?)\1",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex LongOpaqueToken = new(
        @"[A-Za-z0-9_+/=-]{32,}",
        RegexOptions.Compiled);

    private static readonly Regex SafeQuotedAllowlist = new(
        @"^(SqlServerRetryingExecutionStrategy|DbContext\.Database\.CreateExecutionStrategy\(\)|(?:auth|organization|diag|ops)\.[A-Za-z_]+)$",
        RegexOptions.Compiled);

    private static readonly Regex QueryParamSecret = new(
        @"([?&])([^=]+)=([^&]*)",
        RegexOptions.Compiled);

    public static RedactedExceptionReport Redact(Exception exception, params string[] explicitSecrets)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var primaryType = exception.GetType().FullName ?? exception.GetType().Name;
        int? sqlNumber = null;
        var msgBuilder = new StringBuilder();
        var stackBuilder = new StringBuilder();

        var depth = 0;
        for (var current = exception; current is not null && depth < 12; current = current.InnerException, depth++)
        {
            if (current is SqlException sql && !sqlNumber.HasValue)
                sqlNumber = sql.Number;

            var typeName = current.GetType().FullName ?? current.GetType().Name;
            var cleaned = ScrubText(current.Message, explicitSecrets);

            if (msgBuilder.Length > 0)
                msgBuilder.Append(" | Inner: ");
            msgBuilder.Append(typeName).Append(": ").Append(cleaned);
            if (current is SqlException s)
                msgBuilder.Append($" [SQL number={s.Number}; state={s.State}; class={s.Class}]");

            stackBuilder.AppendLine($"--- {typeName} ---");
            stackBuilder.AppendLine(ScrubText(current.StackTrace ?? "[no stack available]", explicitSecrets));
        }

        return new RedactedExceptionReport(
            Truncate(primaryType, 256),
            sqlNumber,
            Truncate(msgBuilder.ToString(), 2000),
            Truncate(stackBuilder.ToString(), 16000));
    }

    public static string? RedactRequestPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        var trimmed = rawPath.Trim();
        var qIndex = trimmed.IndexOf('?', StringComparison.Ordinal);
        if (qIndex < 0)
            return Truncate(ScrubText(trimmed), 512);

        var path = ScrubText(trimmed[..qIndex]);
        var query = trimmed[qIndex..];
        var sanitizedQuery = QueryParamSecret.Replace(query, m =>
        {
            var key = m.Groups[2].Value;
            return IsSafeQueryKey(key)
                ? $"{m.Groups[1].Value}{key}={ScrubText(m.Groups[3].Value)}"
                : $"{m.Groups[1].Value}{key}=[redacted]";
        });
        return Truncate(path + sanitizedQuery, 512);
    }

    public static string ScrubText(string? raw, params string[] explicitSecrets)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var output = raw;
        foreach (var secret in explicitSecrets.Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s.Length))
            output = output.Replace(secret, "[redacted]", StringComparison.Ordinal);

        output = BearerPattern.Replace(output, "Bearer [redacted]");
        output = JsonSecretKeyValue.Replace(output, "\"[redacted]\":\"[redacted]\"");
        output = SecretKeyValue.Replace(output, "$1=[redacted]");
        output = QuotedLiteral.Replace(output, match =>
        {
            var inner = match.Groups[2].Value;
            return SafeQuotedAllowlist.IsMatch(inner) ? match.Value : "[redacted]";
        });
        output = LongOpaqueToken.Replace(output, match =>
            SafeQuotedAllowlist.IsMatch(match.Value) ? match.Value : "[redacted]");

        return output;
    }

    private static readonly Regex SensitiveJsonKey = new(
        @"(?i)(password|pwd|token|hash|stamp|cookie|authorization|secret|apikey|api_key|connectionstring)",
        RegexOptions.Compiled);

    private static readonly JsonWriterOptions JsonWriterOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Redacts sensitive keys and secret string values inside JSON metadata while keeping valid JSON syntax
    /// so database ISJSON check constraints remain satisfied.
    /// </summary>
    public static string ScrubJson(string? rawJson, params string[] explicitSecrets)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return "{}";

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, JsonWriterOpts))
            {
                WriteRedactedElement(writer, doc.RootElement, explicitSecrets);
            }

            return Truncate(Encoding.UTF8.GetString(ms.ToArray()), 2000);
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    private static void WriteRedactedElement(Utf8JsonWriter writer, JsonElement element, string[] explicitSecrets)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    if (SensitiveJsonKey.IsMatch(prop.Name))
                    {
                        writer.WriteStringValue("[redacted]");
                    }
                    else
                    {
                        WriteRedactedElement(writer, prop.Value, explicitSecrets);
                    }
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteRedactedElement(writer, item, explicitSecrets);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(ScrubStructuredString(element.GetString(), explicitSecrets));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string ScrubStructuredString(string? raw, string[] explicitSecrets)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var output = raw;
        foreach (var secret in explicitSecrets.Where(s => !string.IsNullOrEmpty(s)).OrderByDescending(s => s.Length))
            output = output.Replace(secret, "[redacted]", StringComparison.Ordinal);

        output = BearerPattern.Replace(output, "Bearer [redacted]");
        output = JsonSecretKeyValue.Replace(output, "\"[redacted]\":\"[redacted]\"");
        output = SecretKeyValue.Replace(output, "$1=[redacted]");
        output = LongOpaqueToken.Replace(output, match =>
            SafeQuotedAllowlist.IsMatch(match.Value) ? match.Value : "[redacted]");
        return output;
    }

    private static bool IsSafeQueryKey(string key) =>
        key.Equals("poId", StringComparison.OrdinalIgnoreCase)
        || key.Equals("organizationId", StringComparison.OrdinalIgnoreCase)
        || key.Equals("tab", StringComparison.OrdinalIgnoreCase)
        || key.Equals("page", StringComparison.OrdinalIgnoreCase)
        || key.Equals("errorRef", StringComparison.OrdinalIgnoreCase)
        || key.Equals("correlationId", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
