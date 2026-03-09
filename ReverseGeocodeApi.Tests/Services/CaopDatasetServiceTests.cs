using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReverseGeocodeApi.Models;
using ReverseGeocodeApi.Services;
using Xunit.Abstractions;

namespace ReverseGeocodeApi.Tests.Services;

public sealed class CaopDatasetServiceTests
{
    private readonly ITestOutputHelper _output;

    public CaopDatasetServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ReverseGeocode_OutsidePortugalBounds_ReturnsNull_WithoutLoadingDataset()
    {
        var root = CreateTempRoot();
        var service = CreateService(root, new CaopOptions
        {
            ActiveDataset = "CAOP2025",
            DataRoot = root,
            TsvFile = "freguesias.tsv",
            MetadataFile = "metadata.json",
            CoordinateOrder = "LatLon"
        });

        var result = service.ReverseGeocode(51.5, -0.12);

        _output.WriteLine($"Outside precheck result: {(result is null ? "null" : "match")}, IsLoaded={service.IsLoaded}");
        Assert.Null(result);
        Assert.False(service.IsLoaded);
    }

    [Fact]
    public void GetActiveOrLoad_MissingDatasetFolder_ThrowsDirectoryNotFound()
    {
        var root = CreateTempRoot();
        var service = CreateService(root, new CaopOptions
        {
            ActiveDataset = "MISSING",
            DataRoot = root,
            TsvFile = "freguesias.tsv",
            MetadataFile = "metadata.json",
            CoordinateOrder = "LatLon"
        });

        Assert.Throws<DirectoryNotFoundException>(() => service.GetActiveOrLoad());
        _output.WriteLine("Missing dataset folder throws DirectoryNotFoundException (expected).");
    }

    [Fact]
    public void ReverseGeocode_LatLonCoordinateOrder_ResolvesExpectedRecord()
    {
        var root = CreateTempRoot();
        CreateDataset(root, "CAOP2025", "freguesias.tsv", "POLYGON ((40 -8.5, 40.3 -8.5, 40.3 -8.3, 40 -8.3, 40 -8.5))");

        var service = CreateService(root, new CaopOptions
        {
            ActiveDataset = "CAOP2025",
            DataRoot = root,
            TsvFile = "freguesias.tsv",
            MetadataFile = "metadata.json",
            CoordinateOrder = "LatLon"
        });

        var result = service.ReverseGeocode(40.2, -8.4);

        _output.WriteLine($"LatLon result dicofre={result?.Dicofre}, freguesia={result?.Freguesia}");
        Assert.NotNull(result);
        Assert.Equal("060334", result!.Dicofre);
    }

    [Fact]
    public void ReverseGeocode_LonLatCoordinateOrder_ResolvesExpectedRecord()
    {
        var root = CreateTempRoot();
        CreateDataset(root, "CAOP2025", "freguesias.tsv", "POLYGON ((-8.5 40, -8.3 40, -8.3 40.3, -8.5 40.3, -8.5 40))");

        var service = CreateService(root, new CaopOptions
        {
            ActiveDataset = "CAOP2025",
            DataRoot = root,
            TsvFile = "freguesias.tsv",
            MetadataFile = "metadata.json",
            CoordinateOrder = "LonLat"
        });

        var result = service.ReverseGeocode(40.2, -8.4);

        _output.WriteLine($"LonLat result dicofre={result?.Dicofre}, freguesia={result?.Freguesia}");
        Assert.NotNull(result);
        Assert.Equal("060334", result!.Dicofre);
    }

    private static CaopDatasetService CreateService(string root, CaopOptions options)
    {
        Directory.CreateDirectory(root);
        return new CaopDatasetService(Options.Create(options), NullLogger<CaopDatasetService>.Instance);
    }

    private static void CreateDataset(string root, string datasetName, string tsvFile, string polygonWkt)
    {
        var datasetDir = Path.Combine(root, datasetName);
        Directory.CreateDirectory(datasetDir);

        var header = "DICOFRE\tFREGUESIA\tCONCELHO\tDISTRITO\tAREA_HA\tDESCRICAO\tWKT_4326";
        var row = $"060334\tUnião das freguesias de Coimbra (Sé Nova, Santa Cruz, Almedina e São Bartolomeu)\tCoimbra\tCoimbra\t833.48\tCoimbra (Sé Nova, Santa Cruz, Almedina e São Bartolomeu)\t{polygonWkt}";
        File.WriteAllText(Path.Combine(datasetDir, tsvFile), header + Environment.NewLine + row);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReverseGeocodeApi.Tests", "Caop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
