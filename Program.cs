using ReverseGeocodeApi.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var home = Environment.GetEnvironmentVariable("HOME");

var appDataPath = !string.IsNullOrWhiteSpace(home)
    ? Path.Combine(home, "data")
    : Path.Combine(builder.Environment.ContentRootPath, "App_Data");

var keysPath = Path.Combine(appDataPath, "keys");

Directory.CreateDirectory(appDataPath);
Directory.CreateDirectory(keysPath);
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
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
