# Reverse Geocode API (Portugal)

![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![API](https://img.shields.io/badge/API-Reverse%20Geocoding-blue)
![Dataset](https://img.shields.io/badge/Data-CAOP%202025-green)
![Hosting](https://img.shields.io/badge/Hosting-Azure%20App%20Service-blue)
![License](https://img.shields.io/badge/License-MIT-green)
![GitHub stars](https://img.shields.io/github/stars/iteracao/ReverseGeocodeAPI)

High-performance **Reverse Geocoding API for Portugal** that converts
geographic coordinates (latitude / longitude) into official Portuguese
administrative divisions using the **CAOP dataset**.

The API resolves coordinates into:

-   Distrito
-   Concelho
-   Freguesia
-   DICOFRE (INE administrative code)

Designed to be **simple, fast and production-ready**.

------------------------------------------------------------------------

# Live API

https://reversegeocodeapi-hsadd5g3bjabbmft.westeurope-01.azurewebsites.net/

Example request:

GET /api/v1/reverse-geocode?lat=40.3479&lon=-8.5941

------------------------------------------------------------------------

# Key Features

-   Reverse geocoding using official **CAOP administrative boundaries**
-   Built with **ASP.NET Core (.NET 8)**
-   OAuth authentication (**Google / Microsoft**)
-   Client **GUID token per user**
-   API authentication using **HTTP Basic (email:token)**
-   Portal POST protection with **antiforgery token** (`X-CSRF-TOKEN`)
-   Developer portal for token management
-   **SQLite token storage**
-   Fast **in-memory dataset loading** with **STRtree spatial index**
-   Structured logging with **Serilog**
-   Built-in **rate limiting**
-   Health endpoint for monitoring
-   Suitable for **IIS, Kestrel or Azure App Service**

------------------------------------------------------------------------

# API Endpoint

GET /api/v1/reverse-geocode

Additional API endpoint:

GET /api/v1/datasets

### Parameters

  Parameter   Description
  ----------- -------------
  lat         Latitude (required, range -90 to 90)
  lon         Longitude (required, range -180 to 180)

### Example

GET /api/v1/reverse-geocode?lat=40.3479&lon=-8.5941

------------------------------------------------------------------------

# Example Response

``` json
{
  "dataset": "CAOP2025",
  "datasetCreatedAtUtc": "2026-03-05T14:27:35Z",
  "dicofre": "060334",
  "freguesia": "União das freguesias de Coimbra (Sé Nova, Santa Cruz, Almedina e São Bartolomeu)",
  "concelho": "Coimbra",
  "distrito": "Coimbra"
}
```

------------------------------------------------------------------------

# Authentication Model

Two authentication layers exist.

### 1. User authentication

Users authenticate using:

-   Google OAuth
-   Microsoft OAuth

### 2. API authentication

API calls require:

Authorization: Basic base64(email:token)

Where:

email = OAuth authenticated email\
token = GUID client token

Important:

- Missing or invalid API credentials return `401`.
- Missing `lat` or `lon` on reverse-geocode returns `400`.
- Out-of-range `lat` or `lon` returns `400`.
- API and pipeline errors return RFC 7807 Problem Details JSON with
  `category` and `code` extensions.

### Error Response Format

Error responses use:

- Content type: `application/problem+json`
- Base fields: `type`, `title`, `status`, `detail`, `instance`
- Extensions: `traceId`, `category`, `code`

Error categories:

- `api` (input/domain-level failures)
- `platform` (auth/rate-limit/pipeline failures)

Example (`400`, missing `lat`):

``` json
{
  "type": "https://api.reversegeocode.pt/problems/missing-lat",
  "title": "Invalid request",
  "status": 400,
  "detail": "Missing required query parameter 'lat'.",
  "instance": "/api/v1/reverse-geocode",
  "traceId": "00-...-...",
  "category": "api",
  "code": "missing_lat"
}
```

------------------------------------------------------------------------

# Client Tokens

Tokens are stored in SQLite table:

ApiClientTokens

Fields:

-   Token
-   Email
-   CreatedAtUtc
-   LastSeenAtUtc
-   RevokedAtUtc

Only **one active token per user** is allowed.

`LastSeenAtUtc` is updated at most once per UTC day for active tokens.

------------------------------------------------------------------------

# Dataset

Source: **CAOP --- Carta Administrativa Oficial de Portugal**

Location:

Data/CAOP2025/

The dataset is loaded **in memory at runtime** for fast spatial lookup
using **NetTopologySuite** with **STRtree** indexing.

------------------------------------------------------------------------

# Developer Portal

Static pages available for users:

wwwroot/login.html\
wwwroot/tokens.html\
wwwroot/legal.html

These pages allow users to authenticate and obtain their API token.

Portal POST endpoints (`/auth/client-token`, `/logout`) are protected with
antiforgery validation using `X-CSRF-TOKEN`.

------------------------------------------------------------------------

# Rate Limiting

Default rate limit:

100 requests per minute

------------------------------------------------------------------------

# Logging

Structured logging using **Serilog**.

Logs stored in:

Logs/

------------------------------------------------------------------------

# Health Endpoint

GET /health

Example response:

``` json
{
  "status": "ok",
  "dataset": "CAOP2025",
  "loaded": true,
  "records": 3049
}
```

------------------------------------------------------------------------

# Configuration Notes

OAuth settings should be supplied via secure configuration
(environment variables / host configuration / secret store).

Required keys:

- Authentication:Google:ClientId
- Authentication:Google:ClientSecret
- Authentication:Microsoft:ClientId
- Authentication:Microsoft:ClientSecret

Optional:

- Authentication:Microsoft:TenantId (defaults to `common`)

The service validates required OAuth settings at startup.

------------------------------------------------------------------------

# Deployment

Supports deployment via:

-   IIS
-   Kestrel
-   Azure App Service

Important folders:

App_Data/\
Logs/

SQLite database:

App_Data/clienttokens.db

------------------------------------------------------------------------

# Contact

info@iteracao.pt

------------------------------------------------------------------------

# License

MIT License
