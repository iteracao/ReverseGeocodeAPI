using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ReverseGeocodeApi.Models;
using ReverseGeocodeApi.Security;

namespace ReverseGeocodeApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddReverseGeocodeServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddControllers();
        services.AddSingleton<ProblemFactory>();
        services.Configure<CaopOptions>(configuration.GetSection("Caop"));
        services.AddSingleton<Services.CaopDatasetService>();

        services.AddOptions<GoogleAuthOptions>()
            .BindConfiguration("Authentication:Google")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Authentication:Google:ClientId is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "Authentication:Google:ClientSecret is required.")
            .ValidateOnStart();

        services.AddOptions<MicrosoftAuthOptions>()
            .BindConfiguration("Authentication:Microsoft")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Authentication:Microsoft:ClientId is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "Authentication:Microsoft:ClientSecret is required.")
            .ValidateOnStart();

        var googleAuth = configuration.GetSection("Authentication:Google").Get<GoogleAuthOptions>() ?? new();
        var microsoftAuth = configuration.GetSection("Authentication:Microsoft").Get<MicrosoftAuthOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(microsoftAuth.TenantId))
        {
            microsoftAuth.TenantId = "common";
        }

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<IUserContext, HttpUserContext>();

        services
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
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;

                options.Events.OnRedirectToLogin = context =>
                {
                    var p = context.Request.Path;
                    if (p.StartsWithSegments("/api") ||
                        p.StartsWithSegments("/auth/me") ||
                        p.StartsWithSegments("/auth/client-token") ||
                        p.StartsWithSegments("/auth/antiforgery-token"))
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
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                options.Cookie.Name = ".ReverseGeocode.External";
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            })
            .AddGoogle(options =>
            {
                options.ClientId = googleAuth.ClientId;
                options.ClientSecret = googleAuth.ClientSecret;
                options.SignInScheme = "External";
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
                options.Authority = $"https://login.microsoftonline.com/{microsoftAuth.TenantId}/v2.0";
                options.ClientId = microsoftAuth.ClientId;
                options.ClientSecret = microsoftAuth.ClientSecret;
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

        services.AddAuthorization();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var ipKey = $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ipKey,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting");
                var problemFactory = context.HttpContext.RequestServices.GetRequiredService<ProblemFactory>();

                var rateLimitKey = context.HttpContext.Items.TryGetValue(HttpContextItemKeys.ApiRateLimitKey, out var keyObj)
                    ? keyObj?.ToString()
                    : null;

                logger.LogWarning(
                    "Rate limit exceeded for {Method} {Path} by {RateLimitKey} from {RemoteIp}",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path,
                    rateLimitKey ?? "unknown",
                    context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                if (!context.HttpContext.Response.HasStarted)
                {
                    context.HttpContext.Response.Headers["Retry-After"] = "60";
                    await problemFactory.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status429TooManyRequests,
                        "Too many requests",
                        "Rate limit exceeded. Please retry later.",
                        "platform",
                        "rate_limit_ip_exceeded",
                        cancellationToken);
                }
            };
        });

        services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(environment.ContentRootPath, "App_Data", "Keys")))
            .SetApplicationName("ReverseGeocodeApi");

        services.AddSingleton<IClientTokenStore, SqliteClientTokenStore>();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Reverse Geocode API",
                Version = "v1",
                Description = "Reverse geocoding API for Portugal based on official CAOP administrative boundaries. Use HTTP Basic authentication with e-mail as username and the generated GUID client token as password."
            });

            options.DocInclusionPredicate((_, apiDesc) =>
            {
                var path = apiDesc.RelativePath ?? "";
                return path.StartsWith("api/", StringComparison.OrdinalIgnoreCase);
            });

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            options.AddSecurityDefinition("Basic", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "basic",
                Description = "Username = account e-mail. Password = generated GUID client token."
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

        return services;
    }
}
