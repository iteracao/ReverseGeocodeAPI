namespace ReverseGeocodeApi.Controllers;

using Microsoft.AspNetCore.Mvc;
using ReverseGeocodeApi.Models;
using ReverseGeocodeApi.Services;

/// <summary>
/// Provides reverse-geocoding endpoints backed by official CAOP freguesia boundaries.
/// </summary>
/// <remarks>
/// All endpoints under <c>/api/v1</c> require HTTP Basic authentication.
/// Use the account e-mail address as the Basic username and the generated GUID client token as the Basic password.
/// </remarks>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Tags("Reverse Geocode")]
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
    /// Resolves GPS coordinates to distrito, concelho and freguesia.
    /// </summary>
    /// <param name="lat">Latitude in decimal degrees. Valid range: -90 to 90.</param>
    /// <param name="lon">Longitude in decimal degrees. Valid range: -180 to 180.</param>
    /// <returns>The matched administrative division for the supplied coordinates.</returns>
    /// <response code="200">Coordinates were resolved successfully.</response>
    /// <response code="400">The latitude or longitude values are invalid.</response>
    /// <response code="401">The request is missing valid HTTP Basic credentials.</response>
    /// <response code="404">No Portuguese administrative area was found for the supplied coordinates.</response>
    /// <response code="429">The API rate limit was exceeded.</response>
    [HttpGet("reverse-geocode")]
    [ProducesResponseType(typeof(ReverseGeocodeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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
    /// Lists the active dataset and the datasets available under the configured data root.
    /// </summary>
    /// <returns>Dataset metadata used by the API.</returns>
    /// <response code="200">Dataset information was returned successfully.</response>
    /// <response code="401">The request is missing valid HTTP Basic credentials.</response>
    /// <response code="429">The API rate limit was exceeded.</response>
    [HttpGet("datasets")]
    [ProducesResponseType(typeof(DatasetListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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

        return Ok(new DatasetListResponse
        {
            Active = active.DatasetName,
            ActiveCreatedAtUtc = active.CreatedAtUtc,
            Available = list
        });
    }
}
