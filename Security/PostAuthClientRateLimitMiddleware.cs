using System.Threading.RateLimiting;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace ReverseGeocodeApi.Security;

public sealed class PostAuthClientRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PostAuthClientRateLimitMiddleware> _logger;
    private readonly ConcurrentDictionary<string, FixedWindowRateLimiter> _limiters = new();

    public PostAuthClientRateLimitMiddleware(
        RequestDelegate next,
        ILogger<PostAuthClientRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var email = ctx.User?.FindFirstValue(ClaimTypes.Email) ?? ctx.User?.Identity?.Name;
        var token = ctx.User?.FindFirstValue("client_token");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            await _next(ctx);
            return;
        }

        var key = $"client:{email.ToLowerInvariant()}:{token}";
        ctx.Items[HttpContextItemKeys.ApiRateLimitKey] = key;

        var limiter = _limiters.GetOrAdd(key, _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

        using var lease = await limiter.AcquireAsync(permitCount: 1, cancellationToken: ctx.RequestAborted);
        if (lease.IsAcquired)
        {
            await _next(ctx);
            return;
        }

        _logger.LogWarning(
            "Post-auth client rate limit exceeded for {ClientKey} on {Method} {Path}",
            key,
            ctx.Request.Method,
            ctx.Request.Path);

        if (ctx.Response.HasStarted)
            return;

        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.Headers["Retry-After"] = "60";

        await ctx.Response.WriteAsJsonAsync(new
        {
            title = "Too many requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = "Per-client rate limit exceeded. Please retry later."
        });
    }
}
