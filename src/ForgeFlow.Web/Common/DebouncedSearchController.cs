using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace ForgeFlow.Web.Common;

/// <summary>
/// Reusable search debouncing and cancellation controller.
/// Debounces rapid keystroke inputs, cancels obsolete in-flight requests,
/// prevents out-of-order UI race conditions, and handles cancellation cleanly.
/// </summary>
public sealed class DebouncedSearchController : IDisposable, IAsyncDisposable
{
    private readonly int _defaultDebounceMs;
    private readonly Lock _syncLock = new();
    private CancellationTokenSource? _currentCts;
    private long _currentSequence;
    private bool _disposed;

    public DebouncedSearchController(int defaultDebounceMs = 300)
    {
        _defaultDebounceMs = Math.Max(0, defaultDebounceMs);
    }

    /// <summary>
    /// Sequential monotonic revision ID of the latest search/filter action.
    /// </summary>
    public long CurrentSequence => Interlocked.Read(ref _currentSequence);

    /// <summary>
    /// Whether a search or filter query is currently pending (waiting debounce or executing).
    /// </summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// Dispatches a debounced query action. Cancels any in-flight previous action,
    /// waits for the debounce delay, and executes the action with the active CancellationToken.
    /// </summary>
    public Task RunDebouncedAsync(
        Func<CancellationToken, Task> action,
        int? debounceMs = null,
        Action<bool>? onBusyChanged = null,
        Action<Exception>? onError = null)
    {
        var delay = debounceMs ?? _defaultDebounceMs;
        return ExecuteAsync(action, delay, onBusyChanged, onError);
    }

    /// <summary>
    /// Dispatches an immediate query action (zero debounce delay). Useful for select dropdowns,
    /// radio buttons, and checkboxes, while still cancelling any prior in-flight search requests.
    /// </summary>
    public Task RunImmediateAsync(
        Func<CancellationToken, Task> action,
        Action<bool>? onBusyChanged = null,
        Action<Exception>? onError = null)
    {
        return ExecuteAsync(action, 0, onBusyChanged, onError);
    }

    /// <summary>
    /// Helper to bind directly to a Blazor input @oninput event.
    /// Updates the target string value and triggers debounced execution.
    /// </summary>
    public Task OnInputDebouncedAsync(
        ChangeEventArgs e,
        Action<string> updateValue,
        Func<CancellationToken, Task> action,
        int? debounceMs = null,
        Action<bool>? onBusyChanged = null,
        Action<Exception>? onError = null)
    {
        var text = e.Value?.ToString() ?? string.Empty;
        updateValue(text);
        return RunDebouncedAsync(action, debounceMs, onBusyChanged, onError);
    }

    /// <summary>
    /// Cancels any currently pending debounce delay or in-flight query execution.
    /// </summary>
    public void CancelCurrent()
    {
        lock (_syncLock)
        {
            if (_currentCts is not null)
            {
                try
                {
                    if (!_currentCts.IsCancellationRequested)
                    {
                        _currentCts.Cancel();
                    }
                }
                catch (ObjectDisposedException) { }
                _currentCts.Dispose();
                _currentCts = null;
            }
        }
    }

    private async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        int delayMs,
        Action<bool>? onBusyChanged,
        Action<Exception>? onError)
    {
        if (_disposed) return;

        CancellationTokenSource cts;
        long sequence;

        lock (_syncLock)
        {
            CancelCurrent();
            _currentCts = new CancellationTokenSource();
            cts = _currentCts;
            sequence = Interlocked.Increment(ref _currentSequence);
        }

        var ct = cts.Token;
        SetBusy(true, onBusyChanged);

        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, ct);
            }

            if (ct.IsCancellationRequested || _disposed || Interlocked.Read(ref _currentSequence) != sequence)
            {
                return;
            }

            await action(ct);
        }
        catch (OperationCanceledException)
        {
            // Clean cancellation - ignore to prevent false errors or noisy alerts
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && !_disposed && Interlocked.Read(ref _currentSequence) == sequence)
            {
                onError?.Invoke(ex);
            }
        }
        finally
        {
            if (Interlocked.Read(ref _currentSequence) == sequence)
            {
                SetBusy(false, onBusyChanged);
            }
        }
    }

    private void SetBusy(bool busy, Action<bool>? callback)
    {
        if (IsBusy != busy)
        {
            IsBusy = busy;
            callback?.Invoke(busy);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelCurrent();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
