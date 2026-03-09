namespace ReverseGeocodeApi.Controllers;

using Microsoft.AspNetCore.Mvc;
using ReverseGeocodeApi.Extensions;
using ReverseGeocodeApi.Models;
using ReverseGeocodeApi.Services;
using ReverseGeocodeApi.Security;

/// <summary>
/// Provides reverse-geocoding endpoints backed by official CAOP freguesia boundaries.
/// </summary>
/// <remarks>
/// All endpoints under <c>/api/v1</c> require HTTP Basic authentication.
/// Use the account e-mail address as the Basic username and the generated GUID client token as the Basic password.
/// Error responses use RFC 7807 Problem Details JSON with <c>category</c> and <c>code</c> extensions.
/// </remarks>
[ApiController]
[Route("api/v1")]
[Produces("application/json", "application/problem+json")]
[Tags("Reverse Geocode")]
public sealed class ReverseGeocodeController : ControllerBase
{
    private readonly Services.ICaopDatasetService _service;
    private readonly ILogger<ReverseGeocodeController> _logger;
    private readonly ProblemFactory _problemFactory;

    public ReverseGeocodeController(
        Services.ICaopDatasetService service,
        ILogger<ReverseGeocodeController> logger,
        ProblemFactory problemFactory)
    {
        _service = service;
        _logger = logger;
        _problemFactory = problemFactory;
    }

    /// <summary>
    /// Resolves GPS coordinates to distrito, concelho and freguesia.
    /// </summary>
    /// <param name="lat">Latitude in decimal degrees. Valid range: -90 to 90.</param>
    /// <param name="lon">Longitude in decimal degrees. Valid range: -180 to 180.</param>
    /// <returns>The matched administrative division for the supplied coordinates.</returns>
    /// <response code="200">Coordinates were resolved successfully.</response>
    /// <response code="400">Invalid input. Returns Problem Details with category <c>api</c> and code <c>missing_lat</c>, <c>missing_lon</c>, <c>invalid_lat_range</c> or <c>invalid_lon_range</c>.</response>
    /// <response code="401">Missing or invalid API credentials. Returns Problem Details with category <c>platform</c>.</response>
    /// <response code="404">No Portuguese administrative area matched. Returns Problem Details with category <c>api</c> and code <c>outside_portugal</c>.</response>
    /// <response code="429">API rate limit exceeded. Returns Problem Details with category <c>platform</c>.</response>
    [HttpGet("reverse-geocode")]
    [ProducesResponseType(typeof(ReverseGeocodeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public IActionResult ReverseGeocode([FromQuery] double? lat, [FromQuery] double? lon)
    {
        var email = User?.Identity?.Name ?? HttpContext.Items[HttpContextItemKeys.ClientEmail]?.ToString() ?? "anonymous";

        if (lat is null)
        {
            _logger.LogWarning("Missing reverse geocode latitude requested by {Email}", email);
            return _problemFactory.CreateActionResult(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "Invalid request",
                "Missing required query parameter 'lat'.",
                "api",
                "missing_lat");
        }

        if (lon is null)
        {
            _logger.LogWarning("Missing reverse geocode longitude requested by {Email}", email);
            return _problemFactory.CreateActionResult(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "Invalid request",
                "Missing required query parameter 'lon'.",
                "api",
                "missing_lon");
        }

        if (lat is < -90 or > 90)
        {
            _logger.LogWarning("Invalid reverse geocode latitude {Latitude} requested by {Email}", lat, email);
            return _problemFactory.CreateActionResult(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "Invalid latitude",
                "Query parameter 'lat' must be between -90 and 90.",
                "api",
                "invalid_lat_range");
        }

        if (lon is < -180 or > 180)
        {
            _logger.LogWarning("Invalid reverse geocode longitude {Longitude} requested by {Email}", lon, email);
            return _problemFactory.CreateActionResult(
                HttpContext,
                StatusCodes.Status400BadRequest,
                "Invalid longitude",
                "Query parameter 'lon' must be between -180 and 180.",
                "api",
                "invalid_lon_range");
        }

        var result = _service.ReverseGeocode(lat.Value, lon.Value);
        if (result == null)
        {
            _logger.LogInformation(
                "Reverse geocode returned no match for lat {Latitude}, lon {Longitude}, requested by {Email}",
                lat,
                lon,
                email);

            return _problemFactory.CreateActionResult(
                HttpContext,
                StatusCodes.Status404NotFound,
                "No match found",
                "No Portuguese administrative area was found for the supplied coordinates.",
                "api",
                "outside_portugal");
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
    /// <response code="401">Missing or invalid API credentials. Returns Problem Details with category <c>platform</c>.</response>
    /// <response code="429">API rate limit exceeded. Returns Problem Details with category <c>platform</c>.</response>
    [HttpGet("datasets")]
    [ProducesResponseType(typeof(DatasetListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public IActionResult ListDatasets()
    {
        var email = User?.Identity?.Name ?? HttpContext.Items[HttpContextItemKeys.ClientEmail]?.ToString() ?? "anonymous";
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
