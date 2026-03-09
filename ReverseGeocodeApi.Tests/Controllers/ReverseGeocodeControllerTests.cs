using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ReverseGeocodeApi.Controllers;
using ReverseGeocodeApi.Extensions;
using ReverseGeocodeApi.Models;
using ReverseGeocodeApi.Services;

namespace ReverseGeocodeApi.Tests.Controllers;

public sealed class ReverseGeocodeControllerTests
{
    [Fact]
    public void ReverseGeocode_MissingLat_ReturnsMissingLatProblemCode()
    {
        var controller = CreateController();

        var result = controller.ReverseGeocode(null, -8.4);

        AssertProblem(result, StatusCodes.Status400BadRequest, "missing_lat");
    }

    [Fact]
    public void ReverseGeocode_MissingLon_ReturnsMissingLonProblemCode()
    {
        var controller = CreateController();

        var result = controller.ReverseGeocode(40.2, null);

        AssertProblem(result, StatusCodes.Status400BadRequest, "missing_lon");
    }

    [Fact]
    public void ReverseGeocode_InvalidLatRange_ReturnsInvalidLatRangeProblemCode()
    {
        var controller = CreateController();

        var result = controller.ReverseGeocode(120, -8.4);

        AssertProblem(result, StatusCodes.Status400BadRequest, "invalid_lat_range");
    }

    [Fact]
    public void ReverseGeocode_InvalidLonRange_ReturnsInvalidLonRangeProblemCode()
    {
        var controller = CreateController();

        var result = controller.ReverseGeocode(40.2, -200);

        AssertProblem(result, StatusCodes.Status400BadRequest, "invalid_lon_range");
    }

    private static ReverseGeocodeController CreateController()
    {
        var controller = new ReverseGeocodeController(
            new StubCaopDatasetService(),
            NullLogger<ReverseGeocodeController>.Instance,
            new ProblemFactory());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static void AssertProblem(IActionResult result, int expectedStatus, string expectedCode)
    {
        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(expectedStatus, content.StatusCode);
        Assert.Equal("application/problem+json", content.ContentType);
        Assert.False(string.IsNullOrWhiteSpace(content.Content));

        using var doc = JsonDocument.Parse(content.Content!);
        var root = doc.RootElement;
        Assert.Equal(expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
    }

    private sealed class StubCaopDatasetService : ICaopDatasetService
    {
        public LoadedDataset GetActiveOrLoad() => throw new NotImplementedException();
        public IReadOnlyList<string> ListDatasets() => Array.Empty<string>();
        public ReverseGeocodeResult? ReverseGeocode(double lat, double lon) => null;
    }
}
