using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace TokenBudget.Api.Security;

/// <summary>Enforces local-by-default API access, bounded requests and explicit AI opt-in.</summary>
internal static class TokenBudgetSecurityMiddleware
{
    public const long MaxRequestBytes = 256 * 1024;

    public static WebApplication UseTokenBudgetSecurity(this WebApplication app)
    {
        app.Use(EnforceRequestBoundAndReviewOptInAsync);
        app.Use(EnforceLocalOrApiKeyAsync);
        return app;
    }

    private static async Task EnforceRequestBoundAndReviewOptInAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.ContentLength > MaxRequestBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        if (context.Request.Path.StartsWithSegments("/v1/agent") &&
            (Environment.GetEnvironmentVariable("TOKENBUDGET_ENABLE_AI_REVIEW") != "true" ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TOKENBUDGET_API_KEY"))))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Agent review requires server opt-in and TOKENBUDGET_API_KEY.");
            return;
        }

        await next(context);
    }

    private static async Task EnforceLocalOrApiKeyAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/v1"))
        {
            await next(context);
            return;
        }

        string? configuredKey = Environment.GetEnvironmentVariable("TOKENBUDGET_API_KEY");
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            string suppliedKey = context.Request.Headers["X-TokenBudget-Key"].ToString();
            byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
            byte[] received = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
            if (!CryptographicOperations.FixedTimeEquals(expected, received))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }
        else if (!IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? IPAddress.None))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Local requests only unless TOKENBUDGET_API_KEY is configured.");
            return;
        }

        await next(context);
    }
}
