namespace ReverseGeocodeApi.Models;

using NetTopologySuite.Geometries;

public class FreguesiaRecord
{
    public string Dicofre { get; set; } = "";
    public string Freguesia { get; set; } = "";
    public string Concelho { get; set; } = "";
    public string Distrito { get; set; } = "";
    public double AreaHa { get; set; }
    public string Descricao { get; set; } = "";

    public Geometry Geometry { get; set; } = default!;

    /// <summary>
    /// Cached bounding box (envelope) for quick pre-filtering.
    /// </summary>
    public Envelope Envelope { get; set; } = default!;
}