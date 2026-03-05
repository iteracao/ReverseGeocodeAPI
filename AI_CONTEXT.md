# AI_CONTEXT.md

This file provides context for AI tools interacting with this
repository.

## Overview

ReverseGeocode API is a lightweight ASP.NET Core Web API that resolves
coordinates into Portuguese administrative divisions using CAOP data.

Outputs: - Distrito - Concelho - Freguesia - DICOFRE

## Technology Stack

-   .NET 8
-   ASP.NET Core
-   SQLite
-   OAuth authentication (Google / Microsoft)
-   HTTP Basic authentication
-   CAOP dataset

## Authentication Model

Two layers exist:

User authentication: OAuth login via Google or Microsoft.

API authentication: Each user generates a GUID token.

Requests must include:

Authorization: Basic base64(email:token)

## Token Storage

SQLite table: ApiClientTokens

Fields: - Token (GUID) - Email - CreatedAtUtc - LastSeenAtUtc -
RevokedAtUtc

## Dataset

Uses CAOP administrative boundaries for Portugal.

Dataset loads in memory at startup for fast spatial lookup.

## Developer Portal

Located in wwwroot/

Pages: - login.html - tokens.html

Functions: - OAuth login - token generation - API usage examples

The portal intentionally uses vanilla JavaScript to remain lightweight.
