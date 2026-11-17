using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using ForgeFlow.Web.Localization;
using Microsoft.AspNetCore.RateLimiting;

namespace ForgeFlow.Web.Hosting;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddForgeFlowRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new RateLimitingOptions();
        configuration.GetSection(RateLimitingOptions.SectionName).Bind(options);
        configuration.GetSection("ForgeFlow:RateLimiting").Bind(options);

        services.AddSingleton(options);

        if (!options.Enabled)
        {
            return services;
        }

        services.AddRateLimiter(limiterOptions =>
        {
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Auth policy (sliding window partitioned by client IP)
            ConfigureAuthPolicy(limiterOptions, RateLimitingPolicyNames.Auth, options.Auth);
            ConfigureAuthPolicy(limiterOptions, RateLimitingPolicyNames.LegacyAuth, options.Auth);

            // Export policy (fixed window partitioned by User ID / Actor ID / IP)
            limiterOptions.AddPolicy(RateLimitingPolicyNames.Export, context =>
            {
                var partitionKey = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? context.User.FindFirst("actor_id")?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.Export.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.Export.WindowSeconds),
                        QueueLimit = options.Export.QueueLimit,
                        AutoReplenishment = true
                    });
            });

            limiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                var retryAfterSeconds = options.Auth.WindowSeconds;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retrySpan))
                {
                    retryAfterSeconds = (int)Math.Max(1, Math.Ceiling(retrySpan.TotalSeconds));
                }
                httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                var isJson = httpContext.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                    || httpContext.Request.Path.StartsWithSegments("/account")
                    || httpContext.Request.Path.StartsWithSegments("/api");

                var isPersian = CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase)
                    || CultureInfo.CurrentCulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

                var endpoint = httpContext.GetEndpoint();
                var policyAttr = endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>();
                var isExport = policyAttr?.PolicyName == RateLimitingPolicyNames.Export
                    || httpContext.Request.Path.Value?.Contains("export", StringComparison.OrdinalIgnoreCase) == true;

                var (code, enMessage, faMessage) = isExport
                    ? (
                        "Export.RateLimitExceeded",
                        $"Too many export requests. Please try again in {retryAfterSeconds} seconds.",
                        $"تعداد درخواست‌های دریافت گزارش و خروجی بیش از حد مجاز است. لطفاً {retryAfterSeconds} ثانیه دیگر دوباره تلاش کنید.")
                    : (
                        "Auth.RateLimitExceeded",
                        $"Too many sign-in attempts. Please try again in {retryAfterSeconds} seconds.",
                        $"تعداد تلاش‌های ورود بیش از حد مجاز است. لطفاً {retryAfterSeconds} ثانیه دیگر دوباره تلاش کنید.");

                var message = isPersian ? faMessage : enMessage;

                if (isJson)
                {
                    httpContext.Response.ContentType = "application/json; charset=utf-8";
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        code,
                        message,
                        retryAfterSeconds
                    }, cancellationToken);
                }
                else
                {
                    httpContext.Response.ContentType = "text/plain; charset=utf-8";
                    await httpContext.Response.WriteAsync(message, cancellationToken);
                }
            };
        });

        return services;
    }

    private static void ConfigureAuthPolicy(RateLimiterOptions limiterOptions, string policyName, AuthRateLimiterOptions authOptions)
    {
        limiterOptions.AddPolicy(policyName, context =>
        {
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
            return RateLimitPartition.GetSlidingWindowLimiter(
                clientIp,
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = authOptions.PermitLimit,
                    Window = TimeSpan.FromSeconds(authOptions.WindowSeconds),
                    SegmentsPerWindow = Math.Max(1, authOptions.SegmentsPerWindow),
                    QueueLimit = authOptions.QueueLimit,
                    AutoReplenishment = true
                });
        });
    }
}
