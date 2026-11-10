using System.Text.Json;

namespace ForgeFlow.Domain.Audit;

public static class AuditErrors
{
    public const string NotFound = "Audit.NotFound";
    public const string InvalidFilter = "Audit.InvalidFilter";
    public const string InvalidQuery = "Audit.InvalidQuery";
    public const string AccessDenied = "Audit.AccessDenied";
    public const string ActorRequired = "Audit.ActorRequired";
    public const string ScopeRequired = "Audit.ScopeRequired";
    public const string PersistenceUnavailable = "Audit.PersistenceUnavailable";
}

/// <summary>Safe, deterministic parser for allowlisted JSON audit payloads.</summary>
public static class AuditPayloadParser
{
    public static IReadOnlyList<KeyValuePair<string, string>> ParseFields(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return [];

            var fields = new List<KeyValuePair<string, string>>();
            FlattenObject(doc.RootElement, prefix: string.Empty, fields);
            return fields;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void FlattenObject(JsonElement element, string prefix, List<KeyValuePair<string, string>> fields)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                FlattenObject(prop.Value, key, fields);
                continue;
            }

            var valueText = FormatElement(prop.Value);
            fields.Add(new KeyValuePair<string, string>(key, valueText));
        }
    }

    private static string FormatElement(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => "-",
        JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(FormatElement)),
        _ => value.GetRawText()
    };
}
