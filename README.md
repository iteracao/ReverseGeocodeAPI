# Reverse Geocode API (Portugal)

Reverse Geocode API is a lightweight ASP.NET Core Web API that converts
geographic coordinates (latitude / longitude) into official Portuguese
administrative divisions using the CAOP dataset.

The API resolves coordinates into: - Distrito - Concelho - Freguesia -
DICOFRE (INE administrative code)

The service is designed to be simple, fast and production-ready.

------------------------------------------------------------------------

## Key Features

-   Reverse geocoding using official **CAOP administrative boundaries**
-   Built with **ASP.NET Core (.NET 8)**
-   OAuth authentication (Google / Microsoft)
-   Client GUID token per user
-   API authentication using **HTTP Basic (email:token)**
-   Developer portal for token management
-   SQLite token storage
-   Fast in-memory dataset loading
-   Logging and rate limiting
-   Health endpoint for monitoring
-   Suitable for **IIS or Kestrel deployment**

------------------------------------------------------------------------

## API Endpoint

GET /api/v1/reverse-geocode

### Parameters

lat -- Latitude\
lon -- Longitude

### Example

GET /api/v1/reverse-geocode?lat=40.3479&lon=-8.5941

------------------------------------------------------------------------

## Example Response

{ "dataset": "CAOP2025", "datasetCreatedAtUtc": "2026-03-05T14:27:35Z",
"dicofre": "060334", "freguesia": "União das freguesias de Coimbra (Sé
Nova, Santa Cruz, Almedina e São Bartolomeu)", "concelho": "Coimbra",
"distrito": "Coimbra" }

------------------------------------------------------------------------

## Authentication Model

Two authentication layers exist.

User authentication via Google or Microsoft OAuth.

API authentication uses: Authorization: Basic base64(email:token)

email = OAuth authenticated email\
token = GUID client token

------------------------------------------------------------------------

## Client Tokens

Tokens stored in SQLite table ApiClientTokens.

Fields: Token\
Email\
CreatedAtUtc\
LastSeenAtUtc\
RevokedAtUtc

Only **one active token per user** is allowed.

------------------------------------------------------------------------

## Dataset

Source: CAOP (Carta Administrativa Oficial de Portugal)

Location: Data/CAOP2025/

Dataset loads into memory at runtime for fast spatial lookup.

------------------------------------------------------------------------

## Developer Portal

Static pages:

wwwroot/login.html\
wwwroot/tokens.html\
wwwroot/legal.html

------------------------------------------------------------------------

## Rate Limiting

100 requests per minute.

------------------------------------------------------------------------

## Logging

Structured logging using Serilog.

Logs stored in: Logs/

------------------------------------------------------------------------

## Health Endpoint

GET /health

Example: { "status": "ok", "dataset": "CAOP2025", "records": 3049 }

------------------------------------------------------------------------

## Deployment

Supports IIS or Kestrel.

Important folders: App_Data/ Logs/

SQLite database: App_Data/clienttokens.db

------------------------------------------------------------------------

## Contact

info@iteracao.pt

------------------------------------------------------------------------

## License

MIT License
