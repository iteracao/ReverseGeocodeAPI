# ReverseGeocodeApi

Reverse geocoding API for Portugal using official CAOP boundaries.

## What it does

Given latitude and longitude, the API returns:

- `distrito`
- `concelho`
- `freguesia`
- `dicofre`
- dataset metadata (`dataset`, `datasetCreatedAtUtc`)

## Current architecture

- Platform: ASP.NET Core (.NET 8)
- Dataset: CAOP TSV/TSV.GZ loaded in memory
- Spatial lookup: NetTopologySuite with STRtree index
- API auth: HTTP Basic (`email:guid-token`)
- Portal auth: Google or Microsoft OAuth (cookie session)
- Token storage: SQLite (`App_Data/clienttokens.db`)
- Logging: Serilog file + console
- Rate limit: 100 requests/min per authenticated API client key

## Endpoints

### Public

- `GET /health`
  - Returns service status and dataset load state.

### API (Basic auth required)

- `GET /api/v1/datasets`
- `GET /api/v1/reverse-geocode?lat={lat}&lon={lon}`

`lat` and `lon` are required.

Validation rules:

- missing `lat` or `lon` -> `400`
- `lat` outside `[-90, 90]` -> `400`
- `lon` outside `[-180, 180]` -> `400`
- no boundary match -> `404`

## Authentication model

Two layers are used:

1. Portal login (Google or Microsoft) to identify the user.
2. API access with Basic auth:
   - username: OAuth email
   - password: issued GUID client token

One active token per email is enforced.

## Security behavior

- Portal POST actions use antiforgery validation (`X-CSRF-TOKEN`).
- Protected portal endpoints require authenticated cookie session.
- API requests require valid Basic credentials.
- Rate-limit logging does not store raw Basic headers.

## Local run

1. Configure auth settings (environment/app settings):
   - `Authentication:Google:ClientId`
   - `Authentication:Google:ClientSecret`
   - `Authentication:Microsoft:ClientId`
   - `Authentication:Microsoft:ClientSecret`
   - `Authentication:Microsoft:TenantId` (optional, default `common`)
2. Ensure dataset exists in `Data/CAOP2025/`.
3. Run:

```bash
dotnet run
```

Open:

- `/login.html` for portal login
- `/tokens.html` for token management

## Operational notes

- First reverse-geocode request after startup is slower (dataset load).
- Subsequent requests are significantly faster (in-memory + spatial index).
- `LastSeenAtUtc` is updated at most once per UTC day per active token.

## Deployment

See [DEPLOYMENT.md](DEPLOYMENT.md) for production setup and verification.

## License

MIT
