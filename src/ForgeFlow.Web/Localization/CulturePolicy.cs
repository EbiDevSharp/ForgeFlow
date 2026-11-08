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
}
