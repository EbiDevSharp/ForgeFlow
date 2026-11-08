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
    private IJSObjectReference? module;
    private DotNetObjectReference<DraftCoordinator>? reference;
    public event Action? StorageUnavailable;

    public async Task<DraftReadResult> InitializeAsync(DraftDefinition definition)
    {
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
