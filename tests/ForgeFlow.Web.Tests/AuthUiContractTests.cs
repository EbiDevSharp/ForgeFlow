using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class AuthUiContractTests
{
    private static string Source(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));

    [Fact]
    public void Login_uses_localized_Bit_fields_and_the_existing_HTTP_form_contract()
    {
        var login=Source(Path.Combine("Sources","Pages","Login.razor"));
        Assert.Contains("action=\"/account/sign-in\"",login);
        Assert.Contains("method=\"post\"",login);
        Assert.Contains("name=\"returnUrl\" value=\"/\"",login);
        Assert.Contains("Name=\"username\"",login);
        Assert.Contains("Name=\"password\"",login);
        Assert.Contains("CanRevealPassword=\"true\"",login);
        Assert.Contains("RevealPasswordAriaLabel=\"@Text[\"PasswordVisibilityToggle\"]\"",login);
        Assert.DoesNotContain("@bind",login,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DraftCoordinator",login,StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_logout_uses_HTTP_post_and_only_localized_notice_text_is_session_stored()
    {
        var shell=Source(Path.Combine("Sources","Layout","MainLayout.razor"));
        var userMenu=Source(Path.Combine("Sources","Layout","UserMenu.razor"));
        var script=Source(Path.Combine("ApplicationSources","wwwroot","auth-ui.mjs"));
        Assert.Contains("<UserMenu />",shell);
        Assert.Contains("<AuthorizeView",shell);
        Assert.DoesNotContain("action=\"/account/logout\"",shell);
        Assert.Contains("action=\"/account/logout\"",userMenu);
        Assert.Contains("method=\"post\"",userMenu);
        Assert.Contains("<AuthorizeView>",userMenu);
        Assert.Contains("new FormData(form)",script);
        Assert.Contains("forgeflow.draft:",script);
        Assert.Contains("BroadcastChannel",script);
        Assert.Contains("sessionStorage.setItem('auth-ui-notice', message)",script);
        Assert.DoesNotContain("sessionStorage.setItem('password'",script,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage",script,StringComparison.OrdinalIgnoreCase);
    }
}
