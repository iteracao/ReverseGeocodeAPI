using System.Security.Claims;
using System.Text;

namespace ReverseGeocodeApi.Security;

public sealed class BasicClientTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BasicClientTokenMiddleware> _logger;

    public BasicClientTokenMiddleware(RequestDelegate next, ILogger<BasicClientTokenMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx, IClientTokenStore store)
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
            await ctx.Response.WriteAsync("Missing Authorization header.");
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
            await ctx.Response.WriteAsync("Invalid Basic token.");
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
            await ctx.Response.WriteAsync("Invalid Basic format.");
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
            await ctx.Response.WriteAsync("Invalid GUID.");
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
            await ctx.Response.WriteAsync("Invalid email or token.");
            return;
        }

        await store.TouchAsync(email, guid, ctx.RequestAborted);

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
}
