using ReverseGeocodeApi.Extensions;
using ReverseGeocodeApi.Pathing;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var appDataPath = AppDataPaths.GetAppDataPath(builder.Environment.ContentRootPath);
var keysPath = AppDataPaths.GetKeysPath(builder.Environment.ContentRootPath);

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
