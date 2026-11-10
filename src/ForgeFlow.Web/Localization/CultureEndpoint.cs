using ForgeFlow.Infrastructure.Identity;
using ForgeFlow.Web.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;

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
            var reference = SessionClaims.Read(context.User);
            var sessions = reference is null ? null : context.RequestServices?.GetService<RuntimeSessions>();
            if (reference is not null && sessions is not null)
            {
                var outcome = await sessions.SetPreferredCultureAsync(reference, culture, context.RequestAborted);
                if (outcome != "Culture.ProfileSaved")
                {
                    var localizer = context.RequestServices?.GetService<IStringLocalizer<AuthenticationResources>>();
                    var message = localizer?["Culture.ProfileSyncFailed"].Value ?? "Culture.ProfileSyncFailed";
                    return Results.Json(new { cookieApplied = true, profileSaved = false, code = "Culture.ProfileSyncFailed", message }, statusCode: 200);
                }
            }
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
