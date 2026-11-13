using Microsoft.JSInterop;

namespace ForgeFlow.Web.Drafts;

public sealed record DraftField(string Name, string Id, int MaxLength, string Kind = "text", string[]? AllowedValues = null);
public sealed record DraftDefinition(string FormId, string Scope, int Version, DraftField[] Fields, IReadOnlyList<string> Cultures);
public sealed record DraftValue(string Raw, string? InputCulture);
public sealed class DraftSnapshot
{
    public Dictionary<string, DraftValue> Fields { get; set; } = [];
    public string[] Dirty { get; set; } = [];
    public string[] Touched { get; set; } = [];
    public bool ValidationAttempted { get; set; }
    public string? FocusId { get; set; }
}
public sealed record DraftReadResult(string Status, DraftSnapshot? Snapshot);

// The page supplies its non-sensitive field allowlist; storage and switching do
// not know about sample models, validation messages, or business data.
public sealed class DraftCoordinator(IJSRuntime js) : IAsyncDisposable
{
    private static readonly HashSet<string> DisallowedKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "secret", "token", "credential", "auth", "cvv", "pin", "key"
    };

    private static readonly string[] SensitiveKeywords =
    [
        "password", "secret", "token", "credential", "auth", "cvv", "pin", "key"
    ];

    private IJSObjectReference? module;
    private DotNetObjectReference<DraftCoordinator>? reference;
    public event Action? StorageUnavailable;

    public static void ValidateDefinition(DraftDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        foreach (var field in definition.Fields)
        {
            if (DisallowedKinds.Contains(field.Kind) ||
                SensitiveKeywords.Any(k => field.Name.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                           field.Id.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Sensitive field '{field.Name}' is not allowed in draft storage.");
            }
        }
    }

    public async Task<DraftReadResult> InitializeAsync(DraftDefinition definition)
    {
        ValidateDefinition(definition);
        module = await js.InvokeAsync<IJSObjectReference>("import", "./js/drafts.mjs");
        reference = DotNetObjectReference.Create(this);
        return await module.InvokeAsync<DraftReadResult>("initialize", definition, reference);
    }

    public async Task<string> SwitchAsync(string culture) => await module!.InvokeAsync<string>("switchCulture", culture);
    public async Task<bool> ClearAsync() => await module!.InvokeAsync<bool>("clear");
    public async Task FocusAsync(string? id) => await module!.InvokeVoidAsync("restoreFocus", id);
    [JSInvokable] public void ReportStorageFailure() => StorageUnavailable?.Invoke();

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try { await module.InvokeVoidAsync("dispose"); await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        reference?.Dispose();
    }
}
