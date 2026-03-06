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

Active → Revoked

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
