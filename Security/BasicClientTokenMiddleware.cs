using System.Text;

namespace ReverseGeocodeApi.Security;

public sealed class BasicClientTokenMiddleware
{
    private readonly RequestDelegate _next;

    public BasicClientTokenMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, IClientTokenStore store)
    {
        var header = ctx.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
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
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Invalid Basic token.");
            return;
        }

        var idx = decoded.IndexOf(':');
        if (idx <= 0)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Invalid Basic format.");
            return;
        }

        var email = decoded[..idx].Trim().ToLowerInvariant();
        var guidText = decoded[(idx + 1)..].Trim();

        if (!Guid.TryParse(guidText, out var guid))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Invalid GUID.");
            return;
        }

        var ok = await store.IsValidAsync(email, guid);

        if (!ok)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsync("Invalid email or token.");
            return;
        }

        await store.TouchAsync(email, guid);

        await _next(ctx);
    }
}