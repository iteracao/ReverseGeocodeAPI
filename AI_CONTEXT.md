# AI_CONTEXT.md

## Project Overview

Reverse Geocode API converts geographic coordinates into Portuguese
administrative divisions using the CAOP dataset.

Returned data: Distrito, Concelho, Freguesia, DICOFRE

## Technology Stack

.NET 8\
ASP.NET Core Web API\
SQLite\
OAuth authentication\
HTTP Basic API authentication\
CAOP dataset\
Serilog logging

## Authentication Architecture

### OAuth Identity

Users authenticate using Google or Microsoft. The service receives the
user's email address.

### API Token

After authentication the user receives a GUID client token.

API requests must include: Authorization: Basic base64(email:token)

## Token Storage

SQLite table: ApiClientTokens

Columns: Token\
Email\
CreatedAtUtc\
LastSeenAtUtc\
RevokedAtUtc

Unique index ensures one active token per user.

## Token Lifecycle

Active -> Revoked

Revocation sets RevokedAtUtc.

Revoked tokens remain stored for auditing.

## Dataset

Source: CAOP administrative boundaries.

Location: Data/CAOP2025

Dataset loads into memory for fast polygon lookup.

## API Endpoints

GET /api/v1/reverse-geocode\
GET /api/v1/datasets\
GET /health

Diagnostic endpoint (temporary ops use):

GET /api/v1/ops/forwarded-check

## Developer Portal

Located in wwwroot:

login.html\
tokens.html\
legal.html

## Logging

Serilog structured logs.

Directory: Logs/

## Operational Folders

App_Data/ -- SQLite token database\
Logs/ -- application logs\
Data/ -- CAOP dataset\
wwwroot/ -- portal pages

## Monitoring

Health endpoint: GET /health

## Current Internal Structure

- Program.cs: startup orchestration
- Extensions/ServiceCollectionExtensions.cs: service/auth/rate-limit/antiforgery registration
- Extensions/WebApplicationExtensions.cs: middleware and endpoint mapping
- Services/CaopDatasetService.cs: dataset loading + reverse geocode lookup
- Security/BasicClientTokenMiddleware.cs: Basic auth for `/api/*`
- Security/SqliteClientTokenStore.cs: token issue/validate/revoke/touch

## Current Security Details

- Portal POST endpoints require antiforgery validation header: `X-CSRF-TOKEN`
- Antiforgery token endpoint: `GET /auth/antiforgery-token`
- Protected POST endpoints:
  - `POST /auth/client-token`
  - `POST /logout`
- API auth failures return `401` with Problem Details JSON

## Error Contract

Errors return RFC 7807 Problem Details JSON.

Content type:

`application/problem+json`

Base fields:

`type`, `title`, `status`, `detail`, `instance`

Extensions:

`traceId`, `category`, `code`

Category values:

- `api`: input/domain validation and no-match conditions
- `platform`: authentication, rate limiting, antiforgery, and pipeline failures

## Reverse Geocode Validation Rules

- `lat` and `lon` are required query parameters
- Missing `lat`/`lon` -> `400` (`api`: `missing_lat` / `missing_lon`)
- Out-of-range `lat`/`lon` -> `400` (`api`: `invalid_lat_range` / `invalid_lon_range`)
- Valid input with no polygon match -> `404` (`api`: `outside_portugal`)

## Performance Notes

- Dataset is lazily loaded on first geocode request
- Spatial lookup uses NetTopologySuite with STRtree indexing
- First request after startup is slower due dataset/index warm-up
- `LastSeenAtUtc` token touch is limited to once per UTC day

## Configuration Validation

The service validates required OAuth settings at startup:

- Authentication:Google:ClientId
- Authentication:Google:ClientSecret
- Authentication:Microsoft:ClientId
- Authentication:Microsoft:ClientSecret

Optional:

- Authentication:Microsoft:TenantId (defaults to `common`)
