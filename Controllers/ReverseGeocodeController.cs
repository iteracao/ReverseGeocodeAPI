namespace ReverseGeocodeApi.Controllers;

using Microsoft.AspNetCore.Mvc;
using ReverseGeocodeApi.Services;

[ApiController]
[Route("api/v1")]
public sealed class ReverseGeocodeController : ControllerBase
{
    private readonly CaopDatasetService _service;
    private readonly ILogger<ReverseGeocodeController> _logger;

    public ReverseGeocodeController(CaopDatasetService service, ILogger<ReverseGeocodeController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Reverse geocoding: converts GPS coordinates (lat/lon) into Distrito/Concelho/Freguesia.
    /// V1: File-based, no database.
    /// </summary>
    [HttpGet("reverse-geocode")]
    public IActionResult ReverseGeocode([FromQuery] double lat, [FromQuery] double lon)
    {
        var email = User?.Identity?.Name ?? HttpContext.Items["ClientEmail"]?.ToString() ?? "anonymous";

        if (lat is < -90 or > 90)
        {
            _logger.LogWarning("Invalid reverse geocode latitude {Latitude} requested by {Email}", lat, email);
            return BadRequest("Invalid 'lat'.");
        }

        if (lon is < -180 or > 180)
        {
            _logger.LogWarning("Invalid reverse geocode longitude {Longitude} requested by {Email}", lon, email);
            return BadRequest("Invalid 'lon'.");
        }

        var result = _service.ReverseGeocode(lat, lon);
        if (result == null)
        {
            _logger.LogInformation(
                "Reverse geocode returned no match for lat {Latitude}, lon {Longitude}, requested by {Email}",
                lat,
                lon,
                email);

            return NotFound();
        }

        _logger.LogInformation(
            "Reverse geocode resolved lat {Latitude}, lon {Longitude} to {Dicofre} ({Freguesia}, {Concelho}, {Distrito}) for {Email}",
            lat,
            lon,
            result.Dicofre,
            result.Freguesia,
            result.Concelho,
            result.Distrito,
            email);

        return Ok(result);
    }

    /// <summary>
    /// Lists available datasets under the configured DataRoot.
    /// </summary>
    [HttpGet("datasets")]
    public IActionResult ListDatasets()
    {
        var email = User?.Identity?.Name ?? HttpContext.Items["ClientEmail"]?.ToString() ?? "anonymous";
        var list = _service.ListDatasets();
        var active = _service.GetActiveOrLoad();

        _logger.LogInformation(
            "Datasets requested by {Email}. Active dataset: {Dataset}. Available count: {Count}",
            email,
            active.DatasetName,
            list.Count);

        return Ok(new
        {
            active = active.DatasetName,
            activeCreatedAtUtc = active.CreatedAtUtc,
            available = list
        });
    }
}
