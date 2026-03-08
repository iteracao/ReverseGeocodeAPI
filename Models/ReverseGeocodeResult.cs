namespace ReverseGeocodeApi.Models;

/// <summary>
/// Reverse-geocoding response containing the administrative division identified for the supplied coordinates.
/// </summary>
public sealed class ReverseGeocodeResult
{
    /// <summary>Dataset name used to resolve the coordinates.</summary>
    public string Dataset { get; set; } = "";

    /// <summary>Dataset creation timestamp in UTC, when available from the dataset metadata.</summary>
    public string? DatasetCreatedAtUtc { get; set; }

    /// <summary>Official DICOFRE code of the matched freguesia.</summary>
    public string Dicofre { get; set; } = "";

    /// <summary>Freguesia name.</summary>
    public string Freguesia { get; set; } = "";

    /// <summary>Concelho name.</summary>
    public string Concelho { get; set; } = "";

    /// <summary>Distrito name.</summary>
    public string Distrito { get; set; } = "";

    /// <summary>Administrative area in hectares.</summary>
    public double AreaHa { get; set; }

    /// <summary>Human-readable description of the matched administrative area.</summary>
    public string Descricao { get; set; } = "";
}