# AI_CONTEXT.md

## Purpose

Internal technical context for maintenance and code updates.

## System summary

ReverseGeocodeApi resolves Portugal administrative boundaries from CAOP data.

Core response fields:

- `dataset`
- `datasetCreatedAtUtc`
- `dicofre`
- `freguesia`
- `concelho`
- `distrito`
- `areaHa`
- `descricao`

## Stack

- .NET 8 / ASP.NET Core
- NetTopologySuite
- SQLite (`Microsoft.Data.Sqlite`)
- Serilog
- Cookie auth + OAuth (Google/Microsoft)
- Basic auth middleware for `/api/*`

## Key runtime files/folders

- `Program.cs`: app bootstrap and orchestration
- `Extensions/ServiceCollectionExtensions.cs`: service/auth/security registration
- `Extensions/WebApplicationExtensions.cs`: middleware + endpoint mapping
- `Services/CaopDatasetService.cs`: dataset load and spatial lookup
- `Security/BasicClientTokenMiddleware.cs`: Basic auth validation
- `Security/SqliteClientTokenStore.cs`: token persistence and lifecycle
- `Data/CAOP2025/`: CAOP dataset files
- `App_Data/`: DP keys + `clienttokens.db`
- `Logs/`: Serilog rolling files

## Endpoint model

Public:

- `GET /health`

Portal/auth (cookie-based):

- `GET /auth/me`
- `GET /auth/client-token`
- `POST /auth/client-token` (antiforgery required)
- `GET /auth/antiforgery-token`
- `POST /logout` (antiforgery required)

API (Basic auth via middleware):

- `GET /api/v1/datasets`
- `GET /api/v1/reverse-geocode`

## Important invariants

- `lat` and `lon` are required query params for reverse geocode.
- Missing/invalid coordinate input returns `400`.
- One active client token per email.
- `LastSeenAtUtc` updates once per UTC day.
- API rate limit policy: fixed window 100/minute.
- Swagger UI only in Development.

## Security notes

- Portal POST operations are CSRF-protected with `X-CSRF-TOKEN`.
- API auth expects Basic `base64(email:guid)`.
- Logs should not include raw Basic credentials.

## Configuration requirements

Required at startup (validated):

- `Authentication:Google:ClientId`
- `Authentication:Google:ClientSecret`
- `Authentication:Microsoft:ClientId`
- `Authentication:Microsoft:ClientSecret`

Optional:

- `Authentication:Microsoft:TenantId` (defaults to `common`)

## Testing shortcuts

- Build: `dotnet build`
- Local run: `dotnet run`
- Health: `GET /health`

For deployment-specific checks, use `DEPLOYMENT.md`.
