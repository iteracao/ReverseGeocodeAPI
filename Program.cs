using ReverseGeocodeApi.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Ensure App_Data exists (IIS friendly)
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "Logs"));

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddReverseGeocodeServices(
    builder.Configuration,
    builder.Environment);

var app = builder.Build();

app.UseReverseGeocodePipeline();
app.MapReverseGeocodeEndpoints();

try
{
    Log.Information("Starting ReverseGeocodeApi");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
