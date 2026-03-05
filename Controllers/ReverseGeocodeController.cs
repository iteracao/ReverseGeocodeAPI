namespace ReverseGeocodeApi.Controllers;

using Microsoft.AspNetCore.Mvc;
using ReverseGeocodeApi.Services;

[ApiController]
[Route("api/v1")]
public sealed class ReverseGeocodeController : ControllerBase
{
    private readonly CaopDatasetService _service;

    public ReverseGeocodeController(CaopDatasetService service)
    {
        _service = service;
    }

    /// <summary>
    /// Reverse geocoding: converts GPS coordinates (lat/lon) into Distrito/Concelho/Freguesia.
    /// V1: File-based, no database.
    /// </summary>
    [HttpGet("reverse-geocode")]
    public IActionResult ReverseGeocode([FromQuery] double lat, [FromQuery] double lon)
    {
        if (lat is < -90 or > 90) return BadRequest("Invalid 'lat'.");
        if (lon is < -180 or > 180) return BadRequest("Invalid 'lon'.");

        var result = _service.ReverseGeocode(lat, lon);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Lists available datasets under the configured DataRoot.
    /// </summary>
    [HttpGet("datasets")]
    public IActionResult ListDatasets()
    {
        var list = _service.ListDatasets();
        var active = _service.GetActiveOrLoad();

        return Ok(new
        {
            active = active.DatasetName,
            activeCreatedAtUtc = active.CreatedAtUtc,
            available = list
        });
    }
}
