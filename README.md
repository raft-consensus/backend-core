# raft-backend

Raft backend API. The backend uses:

- SQL Server for the system core.
- MySQL for provisioned database infrastructure.
- `ServiceResponse<T>` as the standard response wrapper.

## Configuration

Define these connection strings in `appsettings.json` or environment variables:

```json
{
  "ConnectionStrings": {
    "RaftDb": "Server=...;Database=...;Trusted_Connection=True;TrustServerCertificate=True;",
    "MySqlProvisioning": "server=...;port=3306;database=...;user=...;password=..."
  },
  "Jwt": {
    "Issuer": "raft-backend",
    "Audience": "raft-clients",
    "SigningKey": "replace-with-a-long-random-secret",
    "ExpirationMinutes": 60
  },
  "OAuth": {
    "GoogleClientId": "google-client-id",
    "GoogleClientSecret": "google-client-secret",
    "GitHubClientId": "github-client-id",
    "GitHubClientSecret": "github-client-secret"
  },
  "Frontend": {
    "BaseUrl": "https://raft.andrescortes.dev",
    "CallbackPath": "/auth/callback"
  },
  "MySqlProvisioning": {
    "PublicHost": "db.andrescortes.dev",
    "PublicPort": 3306,
    "DefaultMaxUserConnections": 5,
    "DefaultMaxSpaceBytes": 20971520,
    "PasswordLength": 24
  },
  "DataProtection": {
    "KeysPath": "/app/keys"
  }
}
```

Important note: `appsettings.json` still contains sample values and is not yet wired to GitHub Secrets or secure environment variables. Before deploying to production, move `ConnectionStrings`, `Jwt`, and `OAuth` into environment secrets or a secret mounted by the pipeline.

`ConnectionStrings:MySqlProvisioning` must point at the `raft_provisioner` account (see "MySQL provisioning account" below) on your existing MySQL server — never at `root`.

`Frontend:BaseUrl` drives two things: it's the only origin allowed by CORS (`Program.cs`, policy `"Frontend"`), and `AuthController` redirects there (`{BaseUrl}{CallbackPath}#access_token=...`) after a successful OAuth login instead of returning JSON — the callback is reached via a full browser redirect chain, not a `fetch` call, so a JSON body would never reach the SPA's JS. See [`API.md`](API.md) for the exact contract.

## Roles and authorization

`Users.Role` is `"User"` by default; it is never settable via OAuth claims (`usp_Users_UpsertFromOAuth` never touches `Role` on an existing row). Admin CRUD endpoints (`UsersController`, `DatabaseInstancesController`, `AccessCredentialsController`, `AuditEventsController`, and the `{userId}`-route form of the dashboard) require the `AdminOnly` policy (`RequireRole("Admin")`). There is no self-service way to become an Admin — promote the first admin manually after their first OAuth login:

```sql
UPDATE Users SET Role = 'Admin' WHERE Email = 'you@example.com';
```

## Infrastructure

`docker-compose.yml` only declares the backend service (image/build, port, `appsettings.json` mount, and the Data Protection keys volume). It does **not** stand up a database engine: MySQL, SQL Server, Postgres and MongoDB all run as independently managed containers on the VPS, outside this repo's lifecycle. Point `ConnectionStrings:MySqlProvisioning` (and `RaftDb`) at wherever those containers are reachable — same host/port a student would use, since MySQL's port is already public for direct student connections.

## Database — DB-first

This project is **DB-first**: there is no EF Core Migrations and no runtime schema-apply step. All schema, views and stored procedures are hand-run once against the real servers:

- [`Database/sql-server-schema.md`](Database/sql-server-schema.md) — full ordered script for `RaftDb` (tables, views, all stored procedures). Run it top to bottom in SSMS/Azure Data Studio/`sqlcmd`.
- [`Database/mysql-provisioning-setup.md`](Database/mysql-provisioning-setup.md) — creates the least-privilege `raft_provisioner` account on your existing MySQL server. MySQL has no static schema beyond that — student databases are created dynamically at runtime by `MySqlProvisioningService`.

`RaftDbContext`'s fluent configuration (`Database/RaftDbContext.cs`) documents the same shape in C#, but it is never used to generate or apply schema — only as the connection source for `ISqlStoredProcedureExecutor`.

## Endpoints

### Users

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/users` | JWT | Lists active users. |
| `GET` | `/api/users/{id}` | JWT | Gets a user by id. |
| `POST` | `/api/users` | JWT | Creates an OAuth user. |
| `PUT` | `/api/users/{id}` | JWT | Updates a user. |
| `DELETE` | `/api/users/{id}` | JWT | Soft deletes the user. |

Example payload for `POST` / `PUT`:

```json
{
  "name": "Jane Doe",
  "email": "jane@correo.com",
  "avatarUrl": "https://example.com/avatar.png",
  "provider": "Google",
  "providerUserId": "google-sub-123",
  "lastLogin": "2026-07-17T10:00:00Z"
}
```

### Database Instances

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/database-instances` | JWT | Lists active instances. |
| `GET` | `/api/database-instances/{id}` | JWT | Gets an instance by id. |
| `POST` | `/api/database-instances` | JWT | Creates a database instance. |
| `PUT` | `/api/database-instances/{id}` | JWT | Updates an instance. |
| `DELETE` | `/api/database-instances/{id}` | JWT | Soft deletes the instance. |

Example payload:

```json
{
  "userId": 1,
  "host": "mysql-01.internal",
  "port": 3306,
  "databaseName": "cell_001",
  "databaseUser": "cell_001_user",
  "engine": "MySQL",
  "status": "Active",
  "usedSpaceBytes": 0,
  "maxSpaceBytes": 20971520,
  "lastActivity": null
}
```

### Access Credentials

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/access-credentials` | JWT | Lists active credentials without exposing the secret. |
| `GET` | `/api/access-credentials/{id}` | JWT | Gets a credential by id. |
| `GET` | `/api/access-credentials/by-database-instance/{databaseInstanceId}` | JWT | Gets the credential associated with an instance. |
| `POST` | `/api/access-credentials` | JWT | Creates an encrypted credential. |
| `PUT` | `/api/access-credentials/{id}` | JWT | Updates the encrypted credential. |
| `DELETE` | `/api/access-credentials/{id}` | JWT | Soft deletes the credential. |

Example payload:

```json
{
  "databaseInstanceId": 1,
  "encryptedPassword": "base64-or-encrypted-value"
}
```

Note: the API treats `encryptedPassword` as an encrypted value. Reads do not return the secret.

### Audit Events

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/audit-events` | JWT | Lists audit events. |
| `GET` | `/api/audit-events/{id}` | JWT | Gets an event by id. |
| `POST` | `/api/audit-events` | JWT | Registers an event. |
| `PUT` | `/api/audit-events/{id}` | JWT | Updates an existing event. |
| `DELETE` | `/api/audit-events/{id}` | JWT | Soft deletes the event. |

Example payload:

```json
{
  "userId": 1,
  "eventType": "Login",
  "description": "User logged in successfully.",
  "ipAddress": "127.0.0.1",
  "additionalData": "{\"provider\":\"Google\"}"
}
```

### Platform Metrics

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/metrics/platform` | Public | Returns global platform metrics. |

Typical response:

```json
{
  "success": true,
  "message": "Platform metrics retrieved successfully.",
  "data": {
    "totalUsers": 12,
    "totalDatabases": 18,
    "activeDatabases": 17,
    "totalLogins": 34,
    "activeUsers": 9,
    "serviceAvailability": 100.0
  }
}
```

### User Dashboard (admin)

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/users/{userId}/dashboard` | AdminOnly | Returns the databases for an arbitrary user. Support/admin use only. |

### My Databases (self-service)

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/me/databases` | JWT | Returns the caller's own databases (user id from the JWT claim, never from the URL). |
| `GET` | `/api/me/databases/{databaseInstanceId}/password` | JWT (rate-limited: `credential-reveal`) | Decrypts and returns the password, only if the instance belongs to the caller. Every call is audited (`CredentialRevealed`). |

### Auth

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/api/auth/login/google` | Public | Starts OAuth with Google. |
| `GET` | `/api/auth/login/github` | Public | Starts OAuth with GitHub. |
| `GET` | `/api/auth/callback/google` | Public | Completes Google login and returns a JWT. |
| `GET` | `/api/auth/callback/github` | Public | Completes GitHub login and returns a JWT. |

Example response:

```json
{
  "success": true,
  "message": "OAuth authentication completed successfully.",
  "data": {
    "accessToken": "jwt-here",
    "tokenType": "Bearer",
    "expiresAt": "2026-07-17T15:00:00Z",
    "provider": "Google",
    "user": {
      "id": 1,
      "name": "Jane Doe",
      "email": "jane@correo.com",
      "avatarUrl": "https://...",
      "provider": "Google",
      "providerUserId": "google-sub-123",
      "createdAt": "2026-07-17T14:00:00Z",
      "updatedAt": null,
      "deletedAt": null,
      "lastLogin": "2026-07-17T14:10:00Z"
    }
  }
}
```

## Responses

All responses use this structure:

```json
{
  "data": {},
  "message": "text",
  "success": true
}
```

## Implementation Notes

- Entities with `Deleted_at` use soft delete.
- `Users` prevents duplicates by `Provider + ProviderUserId`, enforced inside `usp_Users_UpsertFromOAuth` (not in C#).
- On first login (`IsNewUser` from the upsert SP), `AuthService` triggers `IMySqlProvisioningService.ProvisionDatabaseAsync` to create a real MySQL database + scoped user automatically. A provisioning failure never blocks login — it's logged as a `ProvisioningFailed` audit event and the JWT is still issued.
- `UserDashboard` is a read projection, not a domain table.
- `serviceAvailability` is temporarily fixed at `100.0` until real monitoring is integrated.
- `AuthController` only accepts Google and GitHub.
- Business endpoints are protected with JWT; admin CRUD endpoints additionally require the `AdminOnly` policy (`Users.Role = 'Admin'`).
- The backend creates and validates JWTs locally; Google and GitHub are the external providers.
- `ExceptionHandlingMiddleware` catches unhandled exceptions and returns a `ServiceResponse<object>` with status 500.
- `AccessCredentials.EncryptedPassword` is encrypted with the ASP.NET Data Protection API (`DataProtectionPurposes.AccessCredentialPassword`) — reversible on purpose, since the password must be shown back to the owner via `/api/me/databases/{id}/password`. Never change that purpose string; it invalidates every previously-encrypted password.

## Services and SPs

Controllers do not query `RaftDbContext` directly. They delegate to application services that execute stored procedures in SQL Server through a shared layer (`ISqlStoredProcedureExecutor`). The SPs themselves live in [`Database/sql-server-schema.md`](Database/sql-server-schema.md), applied manually — see "Database — DB-first" above.

Expected SPs by convention:

- `usp_Users_GetAll`
- `usp_Users_GetById`
- `usp_Users_Create`
- `usp_Users_Update`
- `usp_Users_SoftDelete`
- `usp_Users_UpsertFromOAuth`
- `usp_DatabaseInstances_GetAll`
- `usp_DatabaseInstances_GetById`
- `usp_DatabaseInstances_Create`
- `usp_DatabaseInstances_Update`
- `usp_DatabaseInstances_SoftDelete`
- `usp_DatabaseInstances_UpdateStatus`
- `usp_AccessCredentials_GetAll`
- `usp_AccessCredentials_GetById`
- `usp_AccessCredentials_GetByDatabaseInstanceId`
- `usp_AccessCredentials_Create`
- `usp_AccessCredentials_Update`
- `usp_AccessCredentials_SoftDelete`
- `usp_AccessCredentials_GetDecryptableByOwner`
- `usp_AuditEvents_GetAll`
- `usp_AuditEvents_GetById`
- `usp_AuditEvents_Create`
- `usp_AuditEvents_Update`
- `usp_AuditEvents_SoftDelete`
- `usp_PlatformMetrics_Get`
- `usp_UserDashboard_GetByUserId`
