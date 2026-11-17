using ForgeFlow.Web.Common;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class DebouncedSearchControllerTests
{
    [Fact]
    public async Task Debounce_Rapid_Calls_Only_Executes_Final_Action()
    {
        using var controller = new DebouncedSearchController(100);
        var executedList = new List<string>();

        // Simulate 3 rapid typing inputs
        var t1 = controller.RunDebouncedAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executedList.Add("first");
        });

        await Task.Delay(20);

        var t2 = controller.RunDebouncedAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executedList.Add("second");
        });

        await Task.Delay(20);

        var t3 = controller.RunDebouncedAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executedList.Add("third");
        });

        await Task.WhenAll(t1, t2, t3);

        // Allow debounce window (100ms) to elapse
        await Task.Delay(150);

        Assert.Single(executedList);
        Assert.Equal("third", executedList[0]);
    }

    [Fact]
    public async Task Race_Condition_Slow_Query_Does_Not_Overwrite_Fast_Query()
    {
        using var controller = new DebouncedSearchController(50);
        var finalValue = string.Empty;

        // Query 1: Slow query (simulates 200ms DB latency)
        var t1 = controller.RunImmediateAsync(async ct =>
        {
            try
            {
                await Task.Delay(200, ct);
                finalValue = "slow_query_result";
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
        });

        // Small delay to ensure query 1 started
        await Task.Delay(20);

        // Query 2: Fast query replaces query 1 immediately
        var t2 = controller.RunImmediateAsync(async ct =>
        {
            await Task.Delay(30, ct);
            finalValue = "fast_query_result";
        });

        await Task.WhenAll(t1, t2);
        // Additional wait to ensure any residual task would have finished
        await Task.Delay(250);

        Assert.Equal("fast_query_result", finalValue);
    }

    [Fact]
    public async Task Immediate_Action_Cancels_Pending_Debounced_Action()
    {
        using var controller = new DebouncedSearchController(200);
        var executedActions = new List<string>();

        // Debounced action scheduled for 200ms
        _ = controller.RunDebouncedAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executedActions.Add("debounced");
        });

        // 30ms later, an immediate action is triggered (e.g. filter dropdown changed)
        await Task.Delay(30);

        await controller.RunImmediateAsync(async ct =>
        {
            await Task.Delay(10, ct);
            executedActions.Add("immediate");
        });

        // Wait past original debounced window
        await Task.Delay(250);

        Assert.Single(executedActions);
        Assert.Equal("immediate", executedActions[0]);
    }

    [Fact]
    public async Task OnInputDebouncedAsync_Updates_Value_And_Debounces()
    {
        using var controller = new DebouncedSearchController(80);
        var currentValue = string.Empty;
        var executedSearches = new List<string>();

        var changeEvent1 = new ChangeEventArgs { Value = "he" };
        var changeEvent2 = new ChangeEventArgs { Value = "hello" };

        _ = controller.OnInputDebouncedAsync(
            changeEvent1,
            val => currentValue = val,
            async ct =>
            {
                await Task.Delay(10, ct);
                executedSearches.Add(currentValue);
            });

        await Task.Delay(20);

        _ = controller.OnInputDebouncedAsync(
            changeEvent2,
            val => currentValue = val,
            async ct =>
            {
                await Task.Delay(10, ct);
                executedSearches.Add(currentValue);
            });

        // Wait for debounce to settle
        await Task.Delay(150);

        Assert.Equal("hello", currentValue);
        Assert.Single(executedSearches);
        Assert.Equal("hello", executedSearches[0]);
    }

    [Fact]
    public async Task CancelCurrent_Cancels_In_Flight_Action()
    {
        using var controller = new DebouncedSearchController(100);
        var completed = false;

        var task = controller.RunImmediateAsync(async ct =>
        {
            await Task.Delay(1000, ct);
            completed = true;
        });

        await Task.Delay(50);
        controller.CancelCurrent();
        await task;

        Assert.False(completed);
    }

    [Fact]
    public async Task Dispose_Cancels_Pending_Operations_Safely()
    {
        var controller = new DebouncedSearchController(100);
        var completed = false;

        _ = controller.RunDebouncedAsync(async ct =>
        {
            await Task.Delay(200, ct);
            completed = true;
        });

        await Task.Delay(20);
        controller.Dispose();

        await Task.Delay(250);
        Assert.False(completed);
    }
}
