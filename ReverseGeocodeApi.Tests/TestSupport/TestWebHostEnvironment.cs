using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace ReverseGeocodeApi.Tests.TestSupport;

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "ReverseGeocodeApi.Tests";
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
