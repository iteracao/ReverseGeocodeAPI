# Reverse Geocode API (Portugal)

![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![API](https://img.shields.io/badge/API-Reverse%20Geocoding-blue)
![Dataset](https://img.shields.io/badge/Data-CAOP%202025-green)
![Hosting](https://img.shields.io/badge/Hosting-Azure%20App%20Service-blue)
![License](https://img.shields.io/badge/License-MIT-green)

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
-   Developer portal for token management
-   **SQLite token storage**
-   Fast **in-memory dataset loading**
-   Structured logging with **Serilog**
-   Built-in **rate limiting**
-   Health endpoint for monitoring
-   Suitable for **IIS, Kestrel or Azure App Service**

------------------------------------------------------------------------

# API Endpoint

GET /api/v1/reverse-geocode

### Parameters

  Parameter   Description
  ----------- -------------
  lat         Latitude
  lon         Longitude

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

------------------------------------------------------------------------

# Dataset

Source: **CAOP --- Carta Administrativa Oficial de Portugal**

Location:

Data/CAOP2025/

The dataset is loaded **in memory at runtime** for fast spatial lookup
using **NetTopologySuite**.

------------------------------------------------------------------------

# Developer Portal

Static pages available for users:

wwwroot/login.html\
wwwroot/tokens.html\
wwwroot/legal.html

These pages allow users to authenticate and obtain their API token.

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
  "records": 3049
}
```

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
