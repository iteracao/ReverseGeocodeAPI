using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using ReverseGeocodeApi.Security;

namespace ReverseGeocodeApi.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseReverseGeocodePipeline(this WebApplication app)
    {
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
            KnownProxies = { System.Net.IPAddress.Loopback, System.Net.IPAddress.IPv6Loopback }
        });

        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["X-Frame-Options"] = "DENY";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

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
            app.UseSwaggerUI(options =>
            {
                options.DocumentTitle = "Reverse Geocode API";
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Reverse Geocode API v1");
            });
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

        app.UseAuthentication();

        app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), apiBranch =>
        {
            apiBranch.UseRateLimiter();
            apiBranch.UseMiddleware<BasicClientTokenMiddleware>();
            apiBranch.UseMiddleware<PostAuthClientRateLimitMiddleware>();
        });

        app.UseAuthorization();

        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/tokens") &&
                ctx.User?.Identity?.IsAuthenticated != true)
            {
                ctx.Response.Redirect("/login.html");
                return;
            }

            await next();
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                ctx.Context.Response.Headers["Pragma"] = "no-cache";
                ctx.Context.Response.Headers["Expires"] = "0";
            }
        });

        return app;
    }

    public static WebApplication MapReverseGeocodeEndpoints(this WebApplication app)
    {
        app.MapControllers();

        app.MapGet("/health", (Services.CaopDatasetService datasetService) =>
        {
            var loaded = datasetService.IsLoaded;

            return Results.Ok(new
            {
                status = loaded ? "ok" : "starting",
                dataset = datasetService.ActiveDatasetName,
                loaded,
                records = datasetService.LoadedRecordCount,
                datasetCreatedAtUtc = datasetService.LoadedDatasetCreatedAtUtc,
                timeUtc = DateTime.UtcNow
            });
        });

        app.MapMethods("/error", new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" }, (HttpContext ctx) =>
        {
            var traceId = Activity.Current?.Id ?? ctx.TraceIdentifier;

            return Results.Problem(
                title: "Unexpected error",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = traceId
                });
        });

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

        app.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Invalid antiforgery token.");
            }

            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignOutAsync("External");

            http.Response.Cookies.Delete(".ReverseGeocode.External");

            return Results.NoContent();
        }).RequireAuthorization();

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

        app.MapGet("/auth/antiforgery-token", (HttpContext http, IUserContext userCtx, IAntiforgery antiforgery) =>
        {
            if (!userCtx.IsAuthenticated) return Results.Unauthorized();

            var tokens = antiforgery.GetAndStoreTokens(http);
            if (string.IsNullOrWhiteSpace(tokens.RequestToken))
                return Results.Problem("Unable to create antiforgery token.");

            return Results.Ok(new
            {
                requestToken = tokens.RequestToken
            });
        }).RequireAuthorization();

        app.MapPost("/auth/client-token", async (HttpContext http, IUserContext userCtx, IClientTokenStore store, IAntiforgery antiforgery) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest("Invalid antiforgery token.");
            }

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

        return app;
    }
}
