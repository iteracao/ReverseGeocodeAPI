# Reverse Geocode API (Portugal)

Lightweight .NET 8 ASP.NET Core Web API that converts geographic
coordinates (latitude/longitude) into official Portuguese administrative
divisions using the CAOP dataset.

The API returns: - Distrito - Concelho - Freguesia - DICOFRE code

Designed to be fast, lightweight and production-ready.

## Features

-   Reverse geocoding using official CAOP administrative boundaries
-   ASP.NET Core Web API (.NET 8)
-   OAuth login (Google / Microsoft)
-   Client GUID token per user
-   API authentication via HTTP Basic (email:token)
-   Lightweight developer portal
-   SQLite token storage
-   Fast in-memory dataset loading
-   Ready for IIS or Kestrel deployment

## Example Request

GET /api/v1/reverse-geocode?lat=40.3479&lon=-8.5941

curl example:

curl -X GET
"https://localhost:7099/api/v1/reverse-geocode?lat=40.3479&lon=-8.5941"\
-H "Authorization: Basic BASE64(email:guid)"

## Example Response

{ "dataset": "CAOP2025", "datasetCreatedAtUtc":
"2026-03-05T14:27:35.1782843Z", "dicofre": "060225", "freguesia":
"Cantanhede", "concelho": "Cantanhede", "distrito": "Coimbra", "areaHa":
4176.01, "descricao": "Cantanhede" }

## Authentication Flow

1.  Login via OAuth (Google or Microsoft)
2.  Generate a client token (GUID)
3.  Use HTTP Basic authentication for API requests

Authorization: Basic base64(email:token)

## Run locally

dotnet run

Swagger (development only): https://localhost:7099/swagger

## License

MIT
