using System.Security.Claims;
using System.Text;
using ReverseGeocodeApi.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace ReverseGeocodeApi.Security;

public sealed class BasicClientTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BasicClientTokenMiddleware> _logger;
    private readonly IMemoryCache _touchCache;

    public BasicClientTokenMiddleware(
        RequestDelegate next,
        ILogger<BasicClientTokenMiddleware> logger,
        IMemoryCache touchCache)
    {
        _next = next;
        _logger = logger;
        _touchCache = touchCache;
    }

    public async Task InvokeAsync(HttpContext ctx, IClientTokenStore store, ProblemFactory problemFactory)
    {
        var header = ctx.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "API authorization header missing or not Basic for {Method} {Path}",
                ctx.Request.Method,
                ctx.Request.Path);

            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"ReverseGeocode API\"";
            await problemFactory.WriteAsync(
                ctx,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Authorization header is missing or not using Basic authentication.",
                "platform",
                "auth_missing_header",
                ctx.RequestAborted);
            return;
        }

        string decoded;

        try
        {
            var encoded = header.Substring("Basic ".Length).Trim();
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch
        {
            _logger.LogWarning(
                "Invalid Basic token encoding for {Method} {Path}",
                ctx.Request.Method,
                ctx.Request.Path);

            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"ReverseGeocode API\"";
            await problemFactory.WriteAsync(
                ctx,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Invalid Basic token encoding.",
                "platform",
                "auth_invalid_basic",
                ctx.RequestAborted);
            return;
        }

        var idx = decoded.IndexOf(':');
        if (idx <= 0)
        {
            _logger.LogWarning(
                "Invalid Basic token format for {Method} {Path}",
                ctx.Request.Method,
                ctx.Request.Path);

            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"ReverseGeocode API\"";
            await problemFactory.WriteAsync(
                ctx,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Invalid Basic token format.",
                "platform",
                "auth_invalid_basic",
                ctx.RequestAborted);
            return;
        }

        var email = decoded[..idx].Trim().ToLowerInvariant();
        var guidText = decoded[(idx + 1)..].Trim();

        if (!Guid.TryParse(guidText, out var guid))
        {
            _logger.LogWarning(
                "Invalid GUID in Basic auth for {Email} on {Method} {Path}",
                email,
                ctx.Request.Method,
                ctx.Request.Path);

            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"ReverseGeocode API\"";
            await problemFactory.WriteAsync(
                ctx,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Invalid client token format.",
                "platform",
                "auth_invalid_basic",
                ctx.RequestAborted);
            return;
        }

        var ok = await store.IsValidAsync(email, guid, ctx.RequestAborted);

        if (!ok)
        {
            _logger.LogWarning(
                "API authentication failed for {Email} on {Method} {Path}",
                email,
                ctx.Request.Method,
                ctx.Request.Path);

            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"ReverseGeocode API\"";
            await problemFactory.WriteAsync(
                ctx,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Invalid e-mail or client token.",
                "platform",
                "auth_invalid_credentials",
                ctx.RequestAborted);
            return;
        }

        if (ShouldTouchTokenToday(email, guid))
        {
            await store.TouchAsync(email, guid, ctx.RequestAborted);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, email),
            new(ClaimTypes.Email, email),
            new("client_token", guid.ToString())
        };

        var identity = new ClaimsIdentity(claims, authenticationType: "BasicClientToken");
        ctx.User = new ClaimsPrincipal(identity);
        ctx.Items["ClientEmail"] = email;
        ctx.Items[HttpContextItemKeys.ApiRateLimitKey] = $"email:{email}";

        _logger.LogInformation(
            "API authentication succeeded for {Email} on {Method} {Path}",
            email,
            ctx.Request.Method,
            ctx.Request.Path);

        await _next(ctx);
    }

    private bool ShouldTouchTokenToday(string email, Guid guid)
    {
        var cacheKey = $"token-touch:{DateTime.UtcNow:yyyyMMdd}:{email}:{guid:N}";
        if (_touchCache.TryGetValue(cacheKey, out _))
            return false;

        _touchCache.Set(cacheKey, true, TimeSpan.FromDays(2));
        return true;
    }
}
