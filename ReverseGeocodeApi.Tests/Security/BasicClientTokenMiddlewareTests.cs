using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ReverseGeocodeApi.Extensions;
using ReverseGeocodeApi.Security;
using Xunit.Abstractions;

namespace ReverseGeocodeApi.Tests.Security;

public sealed class BasicClientTokenMiddlewareTests
{
    private readonly ITestOutputHelper _output;

    public BasicClientTokenMiddlewareTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task MissingHeader_Returns401_WithExpectedProblemCode()
    {
        var store = new StubTokenStore();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateContext();

        await middleware.InvokeAsync(ctx, store, new ProblemFactory());

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        var code = await ReadProblemCodeAsync(ctx);
        _output.WriteLine($"Status: {ctx.Response.StatusCode}, Code: {code}");
        Assert.Equal("auth_missing_header", code);
    }

    [Fact]
    public async Task BadEncoding_Returns401_WithExpectedProblemCode()
    {
        var store = new StubTokenStore();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateContext();
        ctx.Request.Headers.Authorization = "Basic ###notbase64###";

        await middleware.InvokeAsync(ctx, store, new ProblemFactory());

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        var code = await ReadProblemCodeAsync(ctx);
        _output.WriteLine($"Status: {ctx.Response.StatusCode}, Code: {code}");
        Assert.Equal("auth_invalid_basic", code);
    }

    [Fact]
    public async Task InvalidGuid_Returns401_WithExpectedProblemCode()
    {
        var store = new StubTokenStore();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateContext();

        var invalidPair = "user@example.com:not-a-guid";
        ctx.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(invalidPair));

        await middleware.InvokeAsync(ctx, store, new ProblemFactory());

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        var code = await ReadProblemCodeAsync(ctx);
        _output.WriteLine($"Status: {ctx.Response.StatusCode}, Code: {code}");
        Assert.Equal("auth_invalid_basic", code);
    }

    [Fact]
    public async Task ValidCredentials_SetsUserAndCallsNext()
    {
        var store = new StubTokenStore { IsValidResult = true };
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var ctx = CreateContext();
        var guid = Guid.NewGuid();
        var pair = $"user@example.com:{guid}";
        ctx.Request.Headers.Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(pair));

        await middleware.InvokeAsync(ctx, store, new ProblemFactory());
        _output.WriteLine($"Next called: {nextCalled}, Email: {ctx.User.FindFirstValue(ClaimTypes.Email)}, TouchCalled: {store.TouchCalled}");

        Assert.True(nextCalled);
        Assert.Equal("user@example.com", ctx.User.FindFirstValue(ClaimTypes.Email));
        Assert.True(store.TouchCalled);
    }

    private static BasicClientTokenMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new BasicClientTokenMiddleware(
            next,
            NullLogger<BasicClientTokenMiddleware>.Instance,
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static DefaultHttpContext CreateContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Get;
        ctx.Request.Path = "/api/v1/reverse-geocode";
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var sr = new StreamReader(ctx.Response.Body, Encoding.UTF8, leaveOpen: true);
        var content = await sr.ReadToEndAsync();

        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.GetProperty("code").GetString();
    }

    private sealed class StubTokenStore : IClientTokenStore
    {
        public bool IsValidResult { get; set; }
        public bool TouchCalled { get; private set; }

        public Task<Guid> IssueAsync(string email, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<bool> IsValidAsync(string email, Guid token, CancellationToken ct = default)
            => Task.FromResult(IsValidResult);

        public Task TouchAsync(string email, Guid token, CancellationToken ct = default)
        {
            TouchCalled = true;
            return Task.CompletedTask;
        }

        public Task RevokeAsync(string email, Guid token, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<Guid?> TryGetAsync(string email, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);
    }
}

