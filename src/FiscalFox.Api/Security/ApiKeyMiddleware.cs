namespace FiscalFox.Api.Security;

/// <summary>
/// Optional single-key API guard for self-hosted deployments.
///
/// It is a deliberate no-op unless <c>FiscalFox:ApiKey</c> is configured, so the
/// zero-config demo, tests and CI stay open. When a key IS set, every request to
/// <c>/api/*</c> (except the liveness probe) must present a matching
/// <c>X-Api-Key</c> header — a lightweight way to lock down an exposed instance
/// without standing up a full identity stack.
/// </summary>
public class ApiKeyMiddleware
{
    public const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;
    private readonly string? _configuredKey;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _configuredKey = config["FiscalFox:ApiKey"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Disabled when no key is configured.
        if (string.IsNullOrWhiteSpace(_configuredKey))
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;
        // Keep the liveness probe open so orchestrators can health-check freely,
        // and only guard the data API surface.
        if (!path.StartsWithSegments("/api") || path.StartsWithSegments("/api/health"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), _configuredKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid API key." });
            return;
        }

        await _next(context);
    }

    /// <summary>Length-then-constant-time comparison to avoid leaking the key via timing.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
