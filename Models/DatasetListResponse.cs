namespace ReverseGeocodeApi.Models;

/// <summary>
/// Lists the active dataset currently loaded by the API and the dataset folders available under the configured data root.
/// </summary>
public sealed class DatasetListResponse
{
    /// <summary>
    /// Active dataset name currently used for reverse geocoding.
    /// </summary>
    public string Active { get; set; } = "";

    /// <summary>
    /// Dataset creation timestamp in UTC, when available from the dataset metadata.
    /// </summary>
    public string? ActiveCreatedAtUtc { get; set; }

    /// <summary>
    /// Available dataset folder names detected under the configured data root.
    /// </summary>
    public IReadOnlyList<string> Available { get; set; } = Array.Empty<string>();
}
