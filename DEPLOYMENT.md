# DEPLOYMENT.md

## Reverse Geocode API - Deployment Guide

This document describes the recommended deployment process for the Reverse Geocode API in IIS / Windows environments.

---

## 1. Deployment target

Recommended target:

- Windows Server
- IIS
- ASP.NET Core Hosting Bundle installed
- HTTPS enabled
- Public DNS pointing to the API host

Typical public URLs:

- `https://your-domain/`
- `https://your-domain/login.html`
- `https://your-domain/health`

---

## 2. Publish profile

Publish the application in **Release** mode.

Recommended Visual Studio publish target:

- Folder publish first, then copy to IIS site folder  
or
- Direct IIS publish if the server is correctly configured

Recommended output should include:

- application binaries
- `wwwroot/`
- `Data/`
- `appsettings.json`

It should **not** include:

- `appsettings.Development.json`

---

## 3. Required server components

Install on the target server:

- IIS
- ASP.NET Core Runtime / Hosting Bundle for .NET 8
- Valid HTTPS certificate
- URL binding in IIS

Confirm that ASP.NET Core apps run correctly in IIS before first deployment.

---

## 4. IIS site setup

Recommended IIS configuration:

- Create a dedicated site or application
- Point physical path to the publish folder
- Configure HTTPS binding
- Use a dedicated application pool

Recommended application pool:

- .NET CLR version: **No Managed Code**
- Managed pipeline mode: **Integrated**
- Start mode: **AlwaysRunning** (optional but useful)

---

## 5. Required folders

The application uses these runtime folders:

- `App_Data/`
- `Logs/`
- `Data/`
- `wwwroot/`

Important notes:

### `App_Data/`
Stores:
- Data Protection keys
- SQLite token database (`clienttokens.db`)

### `Logs/`
Stores:
- Serilog log files

### `Data/`
Stores:
- CAOP dataset files required by the API

### `wwwroot/`
Stores:
- `login.html`
- `tokens.html`
- `legal.html`
- static assets
- `robots.txt`

---

## 6. File and folder permissions

The IIS application pool identity must have **read/write** access to:

- `App_Data`
- `Logs`

The application must have **read** access to:

- `Data`
- `wwwroot`

If permissions are wrong, common symptoms are:

- token generation fails
- logs are not written
- Data Protection errors
- startup/runtime failures

---

## 7. Configuration files

### Published config
Expected on server:

- `appsettings.json`

### Not expected on server
Must not be published:

- `appsettings.Development.json`

This is already controlled in the project file using `CopyToPublishDirectory=Never`.

---

## 8. OAuth secrets

For production, keep OAuth secrets out of normal source-controlled configuration whenever possible.

Recommended options:

- IIS environment variables
- server-level configuration
- secure deployment secrets

Required values:

- Google ClientId
- Google ClientSecret
- Microsoft TenantId
- Microsoft ClientId
- Microsoft ClientSecret

Startup behavior:

- Google and Microsoft ClientId/ClientSecret are validated at startup.
- Missing required values cause startup failure (fail-fast).

---

## 9. Database behavior

The application uses SQLite for API client token storage.

Database file:

- `App_Data/clienttokens.db`

Behavior:

- created/used automatically by the application
- stores client tokens
- stores token lifecycle information
- supports revocation and usage tracking

Before go-live, verify that the file can be created and updated.

---

## 10. Dataset requirements

The API depends on the CAOP dataset files being present in the publish output.

Verify that the publish folder includes:

- `Data/CAOP2025/`
- `freguesias.tsv.gz`
- metadata file(s)

If dataset files are missing:

- `/health` may still respond
- reverse-geocode requests will fail when dataset loading is needed

---

## 11. Security model in production

The application uses two authentication layers:

### Portal authentication
- Google OAuth
- Microsoft OAuth

### API authentication
- HTTP Basic
- username = email
- password = GUID client token

Token behavior:

- one active token per email
- revoked tokens immediately become invalid
- revoked tokens remain stored for audit/history

---

## 12. Rate limiting and logging

Production already includes:

- rate limiting: **300 requests per minute**
- Serilog file logging
- `/health` monitoring endpoint

Verify after deployment:

- rate limiting returns `429` when exceeded
- logs are being written to `Logs/`
- `/health` returns valid JSON
- rate-limit logs should not contain raw Authorization header values

---

## 13. Recommended post-publish checks

After publishing, run these checks in order:

### Site / static pages
- `GET /`
- `GET /login.html`
- `GET /legal.html`

### Monitoring
- `GET /health`

### Authentication flow
- login with Google
- login with Microsoft
- redirect to `tokens.html`

### Token flow
- view authenticated user email
- generate token
- copy token

### API flow
- call `/api/v1/datasets`
- call `/api/v1/reverse-geocode`
- test with valid Basic auth
- test with invalid Basic auth

### Security checks
- no auth => `401`
- bad token => `401`
- revoked token => `401`
- excessive requests => `429`
- missing `lat` or `lon` => `400`
- out-of-range `lat` / `lon` => `400`

### Portal security checks
- authenticated `GET /auth/antiforgery-token` => `200`
- portal `POST /auth/client-token` without antiforgery token => `400`
- portal `POST /logout` without antiforgery token => `400`

---

## 14. Logs to verify

Expected useful log events:

- application startup
- SQLite token store initialization
- successful login flow
- token issuance
- API authentication success/failure
- dataset loading
- reverse geocode request results
- rate limit rejections

Log directory:

- `Logs/`

Note:

- In HTTP-only local runs you may see warning:
  `Failed to determine the https port for redirect.`
- This warning should not appear in correctly configured HTTPS production.

---

## 15. Backup recommendations

Minimum recommended backup targets:

- `App_Data/clienttokens.db`
- `Logs/` (optional depending on retention policy)

Recommended practice:

- backup token database regularly
- keep rolling log retention
- include `App_Data` in operational backup plans

---

## 16. Production checklist

Before going live, confirm all of the following:

- [ ] HTTPS binding is configured
- [ ] IIS site is running
- [ ] ASP.NET Core Hosting Bundle is installed
- [ ] `appsettings.Development.json` is not published
- [ ] OAuth production secrets are configured correctly
- [ ] Startup validation for OAuth settings passes
- [ ] `App_Data` is writable
- [ ] `Logs` is writable
- [ ] `Data/CAOP2025` exists in publish output
- [ ] `/health` responds correctly
- [ ] login works
- [ ] token generation works
- [ ] API requests work with Basic auth
- [ ] reverse-geocode validation (`400` for missing/out-of-range coordinates) works
- [ ] antiforgery protection is active on portal POST endpoints
- [ ] logs are being written
- [ ] `robots.txt` is present
- [ ] `legal.html` is reachable
- [ ] contact email is correct

---

## 17. Go-live note

Swagger is intentionally available only in **Development**.

This is acceptable because:

- the public API already has usage information in `tokens.html`
- production attack surface is slightly reduced
- development environments still have full interactive documentation

---

## 18. Support contact

Operational / service contact:

- `info@iteracao.pt`

---

## 19. Final recommendation

Perform the first production deployment with:

- fresh publish output
- explicit verification of dataset files
- explicit verification of writable runtime folders
- one complete end-to-end token + API test

Once these checks pass, the Reverse Geocode API is ready for public use.
