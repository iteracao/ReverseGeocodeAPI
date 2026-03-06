using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using ReverseGeocodeApi.Security;
using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Ensure App_Data exists (IIS friendly)
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "Logs"));

builder.Services.AddControllers();
builder.Services.Configure<ReverseGeocodeApi.Models.CaopOptions>(builder.Configuration.GetSection("Caop"));
builder.Services.AddSingleton<ReverseGeocodeApi.Services.CaopDatasetService>();

// User context (lightweight, no EF)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();

// --- Auth (cookie-based portal for token issuance) ---
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/login.html";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.Cookie.MaxAge = TimeSpan.FromDays(30);

        // For API calls return 401 instead of redirecting HTML
        options.Events.OnRedirectToLogin = context =>
        {
            var p = context.Request.Path;

            if (p.StartsWithSegments("/api") ||
                p.StartsWithSegments("/auth/me") ||
                p.StartsWithSegments("/auth/client-token"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect($"{context.Request.PathBase}/login.html");
            return Task.CompletedTask;
        };

    })
    .AddCookie("External", options =>
    {
        // short-lived external cookie used only during the handshake
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        options.Cookie.Name = ".ReverseGeocode.External";
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
        options.SignInScheme = "External";

        // Useful if you see "Correlation failed" behind some proxies
        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            context.Response.Redirect(context.RedirectUri + "&prompt=select_account");
            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect("Microsoft", options =>
    {
        var tenantId = builder.Configuration["Authentication:Microsoft:TenantId"] ?? "common";
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.ClientId = builder.Configuration["Authentication:Microsoft:ClientId"]!;
        options.ClientSecret = builder.Configuration["Authentication:Microsoft:ClientSecret"]!;
        options.CallbackPath = "/signin-microsoft";
        options.ResponseType = "code";
        options.SaveTokens = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.SignInScheme = "External";

        options.CorrelationCookie.SameSite = SameSiteMode.None;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Events.OnRedirectToIdentityProvider = context =>
        {
            context.ProtocolMessage.Prompt = "select_account";
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiting");

        logger.LogWarning(
            "Rate limit exceeded for {Method} {Path} from {RemoteIp}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        if (!context.HttpContext.Response.HasStarted)
        {
            context.HttpContext.Response.ContentType = "application/problem+json";
            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                title = "Too many requests",
                status = StatusCodes.Status429TooManyRequests,
                detail = "Rate limit exceeded. Please retry later."
            }, cancellationToken);
        }
    };

    options.AddFixedWindowLimiter("api", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
        limiterOptions.AutoReplenishment = true;
    });
});

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys")))
    .SetApplicationName("ReverseGeocodeApi");

// Client token store (GUID) persisted in App_Data (SQLite)
builder.Services.AddSingleton<IClientTokenStore, SqliteClientTokenStore>();

// Swagger/OpenAPI only in Development
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Mostra apenas endpoints /api/*
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        var path = apiDesc.RelativePath ?? "";
        return path.StartsWith("api/", StringComparison.OrdinalIgnoreCase);
    });

    // Botão Authorize (Basic)
    options.AddSecurityDefinition("Basic", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "basic",
        Description = "Username = email, Password = GUID token"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Basic"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    // Only trust forwarded headers from IIS on localhost
    KnownProxies = { System.Net.IPAddress.Loopback, System.Net.IPAddress.IPv6Loopback }
});

// Security headers (lightweight)
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // CSP (leve)  evita partir login/tokens (inline script/style)
    // Em dev podes optar por não aplicar ao Swagger, mas não é obrigatório.
    if ((!ctx.Request.Path.StartsWithSegments("/swagger")) && (!ctx.Request.Path.StartsWithSegments("/api")))
    {
        ctx.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
            "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline';";
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("RequestLogging");

    var sw = Stopwatch.StartNew();

    try
    {
        await next();
        sw.Stop();

        logger.LogInformation(
            "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0000} ms",
            ctx.Request.Method,
            ctx.Request.Path,
            ctx.Response.StatusCode,
            sw.Elapsed.TotalMilliseconds);
    }
    catch (Exception ex)
    {
        sw.Stop();

        logger.LogError(
            ex,
            "HTTP {Method} {Path} failed after {ElapsedMs:0.0000} ms",
            ctx.Request.Method,
            ctx.Request.Path,
            sw.Elapsed.TotalMilliseconds);

        throw;
    }
});

app.UseRateLimiter();

app.UseAuthentication();

app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), apiBranch =>
{
    apiBranch.UseMiddleware<ReverseGeocodeApi.Security.BasicClientTokenMiddleware>();
});

app.UseAuthorization();

app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;

    if (path.StartsWithSegments("/tokens"))
    {
        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            ctx.Response.Redirect("/login.html");
            return;
        }
    }

    await next();
});

// Static files (login/tokens)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Fast iteration / avoid stale content
        ctx.Context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        ctx.Context.Response.Headers["Pragma"] = "no-cache";
        ctx.Context.Response.Headers["Expires"] = "0";
    }
});

app.MapControllers().RequireRateLimiting("api");

// endpoint que devolve JSON em produção (e também serve em dev se quiseres)
app.MapGet("/error", (HttpContext ctx) =>
{
    var traceId = Activity.Current?.Id ?? ctx.TraceIdentifier;

    // Não expor detalhes em produção
    return Results.Problem(
        title: "Unexpected error",
        statusCode: StatusCodes.Status500InternalServerError,
        extensions: new Dictionary<string, object?>
        {
            ["traceId"] = traceId
        });
});

// --- Portal routes ---
app.MapGet("/", (HttpContext http) =>
{
    if (http.User?.Identity?.IsAuthenticated != true)
        return Results.Redirect($"{http.Request.PathBase}/login.html");

    return Results.Redirect($"{http.Request.PathBase}/tokens.html");
});

app.MapGet("/login", (HttpContext http) =>
{
    http.Response.Redirect($"{http.Request.PathBase}/login.html");
    return Task.CompletedTask;
});

app.MapGet("/auth/google", async (HttpContext http) =>
{
    await http.ChallengeAsync(GoogleDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = $"{http.Request.PathBase}/auth/callback" });
});

app.MapGet("/auth/microsoft", async (HttpContext http) =>
{
    await http.ChallengeAsync("Microsoft",
        new AuthenticationProperties { RedirectUri = $"{http.Request.PathBase}/auth/callback" });
});

// After external provider sign-in, sign-in with local cookie.
app.MapGet("/auth/callback", async (HttpContext http) =>
{
    var externalAuth = await http.AuthenticateAsync("External");
    if (!externalAuth.Succeeded || externalAuth.Principal?.Identity?.IsAuthenticated != true)
        return Results.Redirect($"{http.Request.PathBase}/login.html");

    var external = externalAuth.Principal;

    var email =
        external.FindFirstValue(ClaimTypes.Email)
        ?? external.FindFirstValue("preferred_username")
        ?? external.FindFirstValue("email")
        ?? external.FindFirstValue("upn");

    if (string.IsNullOrWhiteSpace(email))
        return Results.Redirect($"{http.Request.PathBase}/login.html?err=noemail");

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, email),
        new(ClaimTypes.Email, email)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

    await http.SignOutAsync("External");

    return Results.Redirect($"{http.Request.PathBase}/tokens.html");
});

app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignOutAsync("External");

    // hard delete cookie
    http.Response.Cookies.Delete(".AspNetCore.Cookies");
    http.Response.Cookies.Delete(".ReverseGeocode.External");

    return Results.NoContent();
});

// Auth API used by tokens.html
app.MapGet("/auth/me", (IUserContext userCtx) =>
{
    if (!userCtx.IsAuthenticated) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(userCtx.Email)) return Results.Problem("No email claim.");
    return Results.Ok(new { email = userCtx.Email });
}).RequireAuthorization();

app.MapGet("/auth/client-token", async (IUserContext userCtx, IClientTokenStore store) =>
{
    if (!userCtx.IsAuthenticated) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(userCtx.Email)) return Results.Problem("No email claim.");

    var token = await store.TryGetAsync(userCtx.Email);

    return Results.Ok(new
    {
        email = userCtx.Email,
        clientToken = token,
        hasToken = token.HasValue
    });
}).RequireAuthorization();

app.MapPost("/auth/client-token", async (IUserContext userCtx, IClientTokenStore store) =>
{
    if (!userCtx.IsAuthenticated) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(userCtx.Email)) return Results.Problem("No email claim.");

    var token = await store.IssueAsync(userCtx.Email);

    return Results.Ok(new
    {
        email = userCtx.Email,
        clientToken = token,
        hasToken = true
    });
}).RequireAuthorization();



app.Run();