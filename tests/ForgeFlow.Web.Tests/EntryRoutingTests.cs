using System.Security.Claims;
using ForgeFlow.Web.Authentication;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ForgeFlow.Web.Tests;

public sealed class EntryRoutingTests
{
    [Theory]
    [InlineData("/", "GET", false, true)]
    [InlineData("/", "GET", true, false)]
    [InlineData("/account/login", "GET", false, false)]
    [InlineData("/account/login", "POST", false, false)]
    [InlineData("/", "POST", false, false)]
    public void Entry_redirect_is_anonymous_root_only(string path, string method, bool authenticated, bool redirected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path; context.Request.Method = method;
        context.Request.QueryString = new QueryString("?returnUrl=https://untrusted.example");
        if (authenticated) context.User = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        Assert.Equal(redirected, EntryRouting.RedirectAnonymousRoot(context));
        if (redirected)
        {
            Assert.Equal(302, context.Response.StatusCode);
            Assert.Equal("/account/login?returnUrl=%2F", context.Response.Headers.Location.ToString());
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        }
    }
}
