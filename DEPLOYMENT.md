# DEPLOYMENT.md

## Scope

Production deployment guide for ReverseGeocodeApi (IIS/Windows focus).

This document covers runtime prerequisites, required configuration, and verification steps.

## 1. Target environment

Recommended:

- Windows Server + IIS
- ASP.NET Core Hosting Bundle (.NET 8)
- HTTPS certificate and binding
- Public DNS to the site

## 2. Publish output requirements

Publish in `Release` mode.

Publish output must include:

- app binaries
- `wwwroot/`
- `Data/`
- `appsettings.json`

Must not include:

- `appsettings.Development.json`

## 3. Required runtime folders and permissions

Runtime folders:

- `App_Data/` (read/write)
- `Logs/` (read/write)
- `Data/` (read)
- `wwwroot/` (read)

`App_Data/` stores:

- Data Protection keys
- `clienttokens.db`

## 4. Required configuration

Set these values in production configuration (App Settings / environment variables / secret store):

- `Authentication__Google__ClientId`
- `Authentication__Google__ClientSecret`
- `Authentication__Microsoft__ClientId`
- `Authentication__Microsoft__ClientSecret`
- `Authentication__Microsoft__TenantId` (optional, default `common`)

Important:

- Google/Microsoft client ID + secret are startup-validated.
- Missing required values cause app startup failure.

## 5. IIS setup baseline

- App pool: `No Managed Code`, `Integrated`
- Site path -> publish folder
- HTTPS binding configured
- App pool identity has required folder access

## 6. Security model in production

- Portal login: Google/Microsoft OAuth (cookie session)
- API auth: HTTP Basic (`email:guid-token`)
- Portal POST endpoints (`/auth/client-token`, `/logout`) require antiforgery token header `X-CSRF-TOKEN`
- API rate limit: 100 requests/minute

## 7. Dataset requirements

Expected files under publish output:

- `Data/CAOP2025/metadata.json`
- `Data/CAOP2025/freguesias.tsv.gz`

Behavior:

- Dataset loads on first geocode request
- `/health` reports load state

## 8. Post-deploy verification

Run in order:

1. `GET /health` returns `200` JSON.
2. `GET /login.html` and `GET /legal.html` return `200`.
3. Login with Google and Microsoft separately.
4. `GET /auth/antiforgery-token` returns `200` when authenticated.
5. `POST /auth/client-token` succeeds from portal.
6. API calls:
   - valid Basic -> `200`
   - missing/invalid Basic -> `401`
7. Reverse-geocode input checks:
   - missing `lat`/`lon` -> `400`
   - out-of-range `lat`/`lon` -> `400`
8. Rate-limit check: exceed limit and confirm `429`.

## 9. Log verification

Inspect `Logs/` for:

- startup success
- OAuth callbacks
- token issuance
- API auth successes/failures
- dataset load events
- rate-limit rejections

Expected warning during HTTP-only local testing:

- `Failed to determine the https port for redirect.`

This warning should not appear in correctly configured HTTPS production deployments.

## 10. Backup recommendations

At minimum back up:

- `App_Data/clienttokens.db`

Optional (policy-based):

- `Logs/`

## 11. Go-live checklist

- [ ] HTTPS binding active
- [ ] Required auth settings present
- [ ] `App_Data` and `Logs` writable
- [ ] CAOP dataset files present
- [ ] Portal login works
- [ ] Token generation works
- [ ] API Basic auth works
- [ ] Logs are written

## 12. Notes

- Swagger UI is Development-only.
- Keep secrets out of source control.
