namespace ReverseGeocodeApi.Models;

/// <summary>
/// Configuration for CAOP dataset loading.
/// </summary>
public sealed class CaopOptions
{
    /// <summary>
    /// Root folder containing datasets. Can be relative to the application base directory.
    /// Example: "Data".
    /// </summary>
    public string DataRoot { get; set; } = "Data";

    /// <summary>
    /// Active dataset folder name under <see cref="DataRoot"/>.
    /// Example: "CAOP2025".
    /// </summary>
    public string ActiveDataset { get; set; } = "CAOP2025";

    /// <summary>
    /// TSV filename.
    /// Supports both plain TSV and GZip TSV (".tsv.gz").
    /// </summary>
    public string TsvFile { get; set; } = "freguesias.tsv";

    /// <summary>
    /// Metadata filename.
    /// </summary>
    public string MetadataFile { get; set; } = "metadata.json";

    /// <summary>
    /// Coordinate order used by geometries inside the exported WKT.
    /// For this project the builder exports coordinates as (lat, lon), so default is "LatLon".
    /// Allowed values: "LatLon", "LonLat".
    /// </summary>
    public string CoordinateOrder { get; set; } = "LatLon";
}
