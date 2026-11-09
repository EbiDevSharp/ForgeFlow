using Bit.BlazorUI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class BitLoginRenderingTests
{
    [Theory]
    [InlineData("username", BitInputType.Text)]
    [InlineData("password", BitInputType.Password)]
    public async Task Installed_Bit_fields_render_named_native_inputs(string name, BitInputType type)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IJSRuntime, NoBrowserJs>();
        services.AddBitBlazorUIServices();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<BitTextField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Name"] = name, ["Type"] = type,
                ["InputHtmlAttributes"] = new Dictionary<string, object> { ["id"] = "login-" + name }
            }));
            return output.ToHtmlString();
        });
        Assert.Matches("<input[^>]*name=\"" + name + "\"", html);
        Assert.Matches("<input[^>]*type=\"" + name.Replace("username", "text") + "\"", html);
    }

    private sealed class NoBrowserJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
