# Optimizer.Server

Backend service for Optimizer's cloud features: user accounts, profile sync, marketplace.

## Quick start

```bash
cd Optimizer.Server
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -hex 32)"
dotnet ef database update
dotnet run
```

Server runs on `http://localhost:5050` (or whatever Kestrel picks).

## Authentication flow

1. Client POSTs `/api/auth/request-magic-link` with email
2. Server emails the magic link (console output in dev)
3. User opens link, client POSTs `/api/auth/verify` with the token from the URL
4. Server returns `{accessToken, refreshToken, user}`
5. Client uses `Authorization: Bearer {accessToken}` for protected endpoints
6. When access token expires, client POSTs `/api/auth/refresh` with refresh token

## Endpoints

- `GET /api/health` — server status (no auth)
- `POST /api/auth/request-magic-link` — request login link
- `POST /api/auth/verify` — exchange magic-link token for tokens
- `POST /api/auth/refresh` — get new access token
- `POST /api/auth/logout` — revoke refresh token
- `GET /api/me` — current user (requires auth)
- `GET /openapi/v1.json` — OpenAPI spec

## Database

SQLite by default. Run `dotnet ef migrations add MigrationName` and `dotnet ef database update` to apply schema changes.

To switch to Postgres for production, update `appsettings.json` connection string and call `UseNpgsql` instead of `UseSqlite` in `Program.cs`.

## Production deployment

- Set `Jwt:Secret` via environment variable or user-secrets (never commit)
- Configure SMTP via `Smtp:Host`, `Smtp:Port`, `Smtp:Username`, `Smtp:Password`, `Smtp:FromEmail`, `Smtp:FromName`
- Use HTTPS termination at load balancer / reverse proxy
- Run behind nginx/Caddy with rate limiting on `/api/auth/*`
