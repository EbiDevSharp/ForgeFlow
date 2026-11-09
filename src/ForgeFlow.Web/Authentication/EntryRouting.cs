namespace ForgeFlow.Web.Authentication;

public static class EntryRouting
{
    public static bool RedirectAnonymousRoot(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Path != "/" ||
            context.User.Identity?.IsAuthenticated == true) return false;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect("/account/login?returnUrl=%2F");
        return true;
    }
}
