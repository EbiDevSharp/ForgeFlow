using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace ForgeFlow.Web.Localization;

public sealed class CulturePolicy
{
    public const string Persian = "fa-IR";
    public const string English = "en-US";
    public static IReadOnlyList<string> Supported { get; } = [Persian, English];
    public static string? Normalize(string? value) => Supported.FirstOrDefault(culture => string.Equals(culture, value, StringComparison.OrdinalIgnoreCase));

    public CultureInfo Current => CultureInfo.CurrentCulture;
    public string Language => CultureInfo.CurrentUICulture.Name;
    public string Direction => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? "rtl" : "ltr";

    public Calendar Calendar => Current.Name == Persian ? new PersianCalendar() : new GregorianCalendar();

    public CultureInfo PickerCulture
    {
        get
        {
            if (Language != Persian)
                return CultureInfo.GetCultureInfo(English);

            var culture = (CultureInfo)CultureInfo.GetCultureInfo(Persian).Clone();
            if (culture.DateTimeFormat.Calendar is not PersianCalendar)
            {
                var persianCal = culture.OptionalCalendars.OfType<PersianCalendar>().FirstOrDefault() ?? new PersianCalendar();
                culture.DateTimeFormat.Calendar = persianCal;
            }
            culture.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Saturday;
            culture.DateTimeFormat.ShortDatePattern = "yyyy/MM/dd";
            return culture;
        }
    }

    public static void Configure(RequestLocalizationOptions options)
    {
        options.SetDefaultCulture(Persian).AddSupportedCultures([.. Supported]).AddSupportedUICultures([.. Supported]);
        options.ApplyCurrentCultureToResponseHeaders = true;
        options.RequestCultureProviders =
        [
            new ConsistentCookieCultureProvider(),
            new AcceptLanguageHeaderRequestCultureProvider()
        ];
    }

    public sealed class ConsistentCookieCultureProvider : CookieRequestCultureProvider
    {
        public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            var result = await base.DetermineProviderCultureResult(httpContext);
            var requested = result?.Cultures.FirstOrDefault().Value;
            var supported = Supported.FirstOrDefault(value => string.Equals(value, requested, StringComparison.OrdinalIgnoreCase));
            return supported is null ? null : new ProviderCultureResult(supported, supported);
        }
    }

    public static async Task ApplyProfileFallbackAsync(HttpContext context)
    {
        var feature = context.Features.Get<IRequestCultureFeature>();
        if (feature?.Provider is ConsistentCookieCultureProvider) return;
        var reference = ForgeFlow.Web.Authentication.SessionClaims.Read(context.User);
        if (reference is null) return;
        var sessions = context.RequestServices.GetService<ForgeFlow.Infrastructure.Identity.RuntimeSessions>();
        if (sessions is null) return;
        var preferred = Normalize(await sessions.GetPreferredCultureAsync(reference, context.RequestAborted));
        if (preferred is null) return;
        var info = CultureInfo.GetCultureInfo(preferred);
        CultureInfo.CurrentCulture = info;
        CultureInfo.CurrentUICulture = info;
        context.Response.Headers.ContentLanguage = preferred;
    }
}
