using System.Threading.RateLimiting;
using System.Security.Claims;
using ReverseGeocodeApi.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace ReverseGeocodeApi.Security;

public sealed class PostAuthClientRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PostAuthClientRateLimitMiddleware> _logger;
    private readonly IMemoryCache _cache;

    public PostAuthClientRateLimitMiddleware(
        RequestDelegate next,
        ILogger<PostAuthClientRateLimitMiddleware> logger,
        IMemoryCache cache)
    {
        _next = next;
        _logger = logger;
        _cache = cache;
    }

    public async Task InvokeAsync(HttpContext ctx, ProblemFactory problemFactory)
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

        var limiter = GetOrCreateLimiter(key);

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

        ctx.Response.Headers["Retry-After"] = "60";
        await problemFactory.WriteAsync(
            ctx,
            StatusCodes.Status429TooManyRequests,
            "Too many requests",
            "Per-client rate limit exceeded. Please retry later.",
            "platform",
            "rate_limit_client_exceeded",
            ctx.RequestAborted);
    }

    private FixedWindowRateLimiter GetOrCreateLimiter(string key)
    {
        return _cache.GetOrCreate($"post-auth-limiter:{key}", entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(2);
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
            entry.RegisterPostEvictionCallback(static (_, value, _, _) =>
            {
                (value as IDisposable)?.Dispose();
            });

            return new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
        })!;
    }
}
