using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;

namespace ForgeFlow.Web.Localization;

public static class CultureEndpoint
{
    public static async Task<IResult> SwitchAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!context.Request.HasFormContentType)
            return Results.BadRequest(new { code = "CultureSwitchRejected" });
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            var form = await context.Request.ReadFormAsync();
            var culture = CulturePolicy.Normalize(form["culture"].ToString());
            // The only implemented form route is deliberately allowlisted.
            if (culture is null || form["returnUrl"].ToString() != "/")
                return Results.BadRequest(new { code = "CultureSwitchRejected" });
            context.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)), new CookieOptions
                {
                    HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax,
                    IsEssential = true, Path = "/", Expires = DateTimeOffset.UtcNow.AddYears(1)
                });
            return Results.NoContent();
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new { code = "CultureSwitchRejected" });
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { code = "CultureSwitchRejected" });
        }
    }
}
