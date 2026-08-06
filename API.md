# API Reference — Raft Backend

Guía para el equipo de frontend de Raft. Cubre todos los endpoints expuestos hoy, con forma exacta de request/response.

## Convenciones generales

**Base URL:** la que corresponda por entorno (local: `http://localhost:5133` según `Properties/launchSettings.json`; producción: el dominio detrás de nginx).

**Todas las respuestas** usan el mismo sobre, sin excepción:

```json
{
  "success": true,
  "message": "texto legible",
  "data": { }
}
```

- `success: false` con `data: null` en errores de negocio (404, 400, 409).
- Errores no controlados devuelven `success: false`, `message: "An unexpected error occurred."`, status `500`.
- `429 Too Many Requests` cuando se excede un límite de rate limiting (ver tabla más abajo) — sin cuerpo `ServiceResponse`, es la respuesta estándar del middleware de rate limiting de ASP.NET.

**Autenticación:** `Authorization: Bearer <jwt>` en cada endpoint que no diga "Público". El JWT se obtiene del flujo de OAuth (sección 2) y expira en 60 minutos (`Jwt:ExpirationMinutes`).

**CORS:** el API acepta los orígenes configurados en `Frontend:Origins`; si esa lista viene vacía, usa `Frontend:BaseUrl` como fallback. En ambos casos, el backend normaliza el valor a origen puro (`scheme + host + port`) antes de aplicarlo.

**Arquitectura por células:** este backend administra la célula de Raft. SQL Server sigue siendo el core compartido, pero MySQL, PostgreSQL y MongoDB ya están integrados como motores soportados por este backend cuando su conexión externa está disponible. El camino principal no se mezcla con contratos futuros: ya existe un catálogo dinámico de motores y cada uno declara su disponibilidad en runtime.

**Code organization:** `Program.cs` ahora solo compone módulos. La aplicación real vive en `Modules/Platform`, `Modules/Data`, `Modules/Domain`, `Modules/Provisioning` y `Modules/Hosting`.

**Rate limiting:**

| Alcance | Límite | Aplica a |
| --- | --- | --- |
| Global (por IP) | 120 req/min | Todo el API |
| `auth` (por IP) | 10 req/min | `/api/auth/*` |
| `credential-reveal` (por usuario) | 5 req/min | `GET /api/me/databases/{id}/password` |
| `database-management` (por usuario) | 10 req/min | `POST /api/me/databases/{id}/pause`, `POST /api/me/databases/{id}/resume`, `DELETE /api/me/databases/{id}` |
| `n8n-management` (por usuario) | 30 req/min | `GET /api/me/n8n` |
| `n8n-provisioning` (por usuario) | 5 req/min | `POST /api/me/n8n/provision` |
| `admin-ops` (por admin) | 30 req/min | `GET/POST/PUT/DELETE` de `/api/users`, `/api/database-instances`, `/api/access-credentials`, `/api/audit-events`, `GET /api/users/{userId}/dashboard`, `GET/POST /api/n8n/accounts*` |

---

## 1. Landing page (público)

### `GET /api/metrics/platform`

Sin autenticación. Estadísticas globales para la página principal.

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

`serviceAvailability` se calcula en el backend como porcentaje de respuestas no-5xx observadas en una ventana móvil de 24 horas sobre el API. Si no hay muestras todavía, retorna `100.0` por convención.

### `GET /api/engines`

Sin autenticación. Devuelve el catálogo de motores/capacidades registrados en el backend. `supportedByThisCell` depende de si el servicio está realmente disponible en runtime.

```json
{
  "success": true,
  "message": "Engine catalog retrieved successfully.",
  "data": [
    {
      "name": "SQL Server",
      "supportedByThisCell": true,
      "status": "Available",
      "notes": "This cell provisions and manages SQL Server databases."
    },
    {
      "name": "MySQL",
      "supportedByThisCell": true,
      "status": "Available",
      "notes": "This cell provisions and manages MySQL databases."
    },
    {
      "name": "PostgreSQL",
      "supportedByThisCell": true,
      "status": "Available",
      "notes": "This cell can provision and manage PostgreSQL databases."
    },
    {
      "name": "MongoDB",
      "supportedByThisCell": true,
      "status": "Available",
      "notes": "This cell can provision and manage MongoDB databases."
    }
  ]
}
```

---

## 2. Autenticación (OAuth)

Todo este flujo es **navegación de navegador completa** (redirects HTTP), no llamadas `fetch`/AJAX — ni el login ni el callback se deben llamar con `fetch`.

### Paso 1 — `GET /api/auth/login/{provider}`

`provider` = `google` | `github`. Público. El frontend dispara esto con una navegación real, no un `fetch`:

```js
window.location.href = "https://<api-host>/api/auth/login/google";
```

Redirige al proveedor (`302`), que al terminar redirige a `/api/auth/callback/{provider}` (interno, el frontend nunca llama esto directo).

### Paso 2 — el backend redirige de vuelta al frontend

Tras procesar el login, `GET /api/auth/callback/{provider}` **no devuelve JSON** — redirige (`302`) a:

```
http://localhost:4200/auth/callback#access_token=<jwt>&expires_at=<iso8601>&provider=<Google|GitHub>
```

El frontend tiene que tener una ruta montada en **`/auth/callback`** que, al cargar, lea `window.location.hash` (no query params — van después del `#`, a propósito, para que nunca se manden a ningún servidor ni queden en logs), extraiga `access_token`, y guarde la sesión.

Si algo falla, el redirect trae `#error=<código>` en vez de `access_token`:

| Código | Cuándo |
| --- | --- |
| `oauth_failed` | El proveedor externo no completó la autenticación |
| `unsupported_provider` | El `{provider}` de la URL no era `google`/`github` |
| `login_failed` | Error inesperado al persistir el login |

Ejemplo de handler típico en el frontend:

```js
// en la ruta /auth/callback
const params = new URLSearchParams(window.location.hash.slice(1));
const error = params.get("error");
if (error) {
  // mostrar mensaje según el código, redirigir a /login
} else {
  const accessToken = params.get("access_token");
  const expiresAt = params.get("expires_at");
  // guardar accessToken, redirigir a la pantalla principal
}
```

**El `user` (nombre, email, avatar, `role`, etc.) no viaja en la URL.** El JWT ya es un estándar (header.payload.signature) — decodificar la parte `payload` (base64) del lado del cliente da acceso a los claims (`sub`, `name`, `email`, `role`, `provider`) sin otra llamada. Si preferís no decodificar el JWT a mano, decime y agrego un endpoint `GET /api/me` que devuelva el usuario como JSON normal (ya autenticado con el Bearer token).

**Qué hacer con el `access_token`:** guardarlo (localStorage/sessionStorage, a definir con el equipo) y mandarlo como `Authorization: Bearer <token>` en el resto de las llamadas — esas sí son `fetch` normales, con CORS habilitado para el origin configurado en `Frontend:Origins` o `Frontend:BaseUrl`.

---

## 3. Mis bases de datos (autoservicio — requiere JWT)

El flujo principal del producto: lo que ve un estudiante logueado sobre sus propias bases de datos.

### `GET /api/me/databases`

Devuelve las bases de datos del usuario autenticado (el id sale del JWT, nunca de la URL — no hace falta ni existe un parámetro para pedir las de otro usuario).

```json
{
  "success": true,
  "message": "Databases retrieved successfully.",
  "data": [
    {
      "databaseInstanceId": 5,
      "host": "localhost",
      "port": 3306,
      "databaseName": "raft_u1_a1b2c3d4",
      "databaseUser": "raft_u1_a1b2c3d4",
      "engine": "SQL Server",
      "status": "Active",
      "usedSpaceBytes": 40960,
      "maxSpaceBytes": 20971520,
      "lastActivity": "2026-07-22T13:45:00Z",
      "createdAt": "2026-07-20T10:00:00Z"
    }
  ]
}
```

`status` puede ser `Active`, `Suspended` (pausada por 7 días de inactividad o por exceder `maxSpaceBytes`) o, si ya no aparece en la lista, fue eliminada (30 días de inactividad).

Si el usuario todavía no tiene ninguna BD, `data` viene como lista vacía — no es un error, es un estado real a manejar en la UI ("aún no tienes una base de datos").

### `POST /api/me/databases`

Crea una base del usuario autenticado. El request acepta `engine` y enruta a `SQL Server`, `MySQL` o `PostgreSQL` según el valor recibido. Alias aceptados: `sqlserver`, `mysql`, `postgres`, `postgresql`.

Nota técnica: en PostgreSQL el backend reutiliza un login compartido por usuario (`raft_u{userId}`) y lo crea de forma idempotente. Si el rol ya existe, actualiza la contraseña en lugar de abortar con `42710: role already exists`, lo que permite aprovisionar la segunda y siguientes bases del mismo usuario sin error.

### `POST /api/me/databases/{databaseInstanceId}/pause`

Pausa una base propia del usuario autenticado. Si la base no le pertenece, responde `404`.

### `POST /api/me/databases/{databaseInstanceId}/resume`

Reanuda una base propia del usuario autenticado. Si la base no le pertenece, responde `404`.

### `DELETE /api/me/databases/{databaseInstanceId}`

Elimina una base propia del usuario autenticado. La eliminación real sigue pasando por el flujo de provisioning/lifecycle del backend, pero la acción puede iniciarla el usuario sobre su propio recurso.

### `GET /api/me/databases/{databaseInstanceId}/password`

Revela la contraseña de una instancia — solo si pertenece al usuario autenticado. Rate limit propio: **5 req/min**. Cada llamada queda auditada del lado del servidor.

```json
{
  "success": true,
  "message": "Password retrieved successfully.",
  "data": {
    "databaseInstanceId": 5,
    "password": "K7mP2xQw9vLnR4tYbZs8"
  }
}
```

`404` si el `databaseInstanceId` no existe o no le pertenece al usuario del token (mismo mensaje en ambos casos, a propósito — no revela si la instancia existe pero es de otro).

Con esto más lo que ya trae `GET /api/me/databases`, el frontend tiene todos los campos que pide el entregable: host, puerto, nombre de BD, usuario, contraseña (bajo demanda), motor, fecha de creación y estado.

### N8N como servicio de autoservicio

El backend expone un flujo propio para que el usuario pida su cuenta de N8N desde la plataforma. El contrato local guarda estado en SQL Server y luego llama a la célula externa de N8N con la API key configurada en `N8nProvisioning`.

### `GET /api/me/n8n`

Devuelve el historial local de cuentas/provisiones de N8N del usuario autenticado.

```json
{
  "success": true,
  "message": "N8N accounts retrieved successfully.",
  "data": [
    {
      "id": 1,
      "userId": 5,
      "externalUserRef": "5",
      "email": "alumno@correo.com",
      "accountId": "n8n-acc-123",
      "status": "Active",
      "createdAt": "2026-08-05T12:00:00Z",
      "updatedAt": "2026-08-05T12:01:30Z",
      "provisionedAt": "2026-08-05T12:01:30Z",
      "revokedAt": null,
      "lastSyncedAt": "2026-08-05T12:01:30Z",
      "lastErrorMessage": null
    }
  ]
}
```

### `POST /api/me/n8n/provision`

Inicia el aprovisionamiento de la cuenta de N8N para el usuario autenticado. El backend usa el id interno del usuario como `external_user_ref` y el email del perfil como `email`.

La respuesta distingue dos casos:

- `Created = true`: se creó una cuenta nueva.
- `Created = false`: ya existía una cuenta `Pending` o `Active` para ese usuario y no se reprovisiona.

Ejemplo:

```json
{
  "success": true,
  "message": "N8N account provisioned successfully.",
  "data": {
    "created": true,
    "account": {
      "id": 1,
      "userId": 5,
      "externalUserRef": "5",
      "email": "alumno@correo.com",
      "accountId": "n8n-acc-123",
      "status": "Active",
      "createdAt": "2026-08-05T12:00:00Z",
      "updatedAt": "2026-08-05T12:01:30Z",
      "provisionedAt": "2026-08-05T12:01:30Z",
      "revokedAt": null,
      "lastSyncedAt": "2026-08-05T12:01:30Z",
      "lastErrorMessage": null
    }
  }
}
```

Si la célula externa responde con error, el backend marca la cuenta local como `Failed`, guarda el error en SQL Server y responde `502`.

#### Administración de cuentas N8N

| Método | Ruta | Auth | Uso |
| --- | --- | --- | --- |
| `GET` | `/api/n8n/accounts` | AdminOnly | Lista todas las cuentas N8N registradas en SQL Server. |
| `GET` | `/api/n8n/accounts/{id}` | AdminOnly | Devuelve una cuenta N8N por id. |
| `POST` | `/api/n8n/accounts/{id}/revoke` | AdminOnly | Revoca localmente la cuenta N8N y la marca como `Revoked`. |

La revocación es local porque el contrato externo de la célula de N8N no expone un endpoint de revocación. El backend deja trazabilidad en `AuditEvents` y en `N8nAccounts.Status`.

Configuración:

- `N8nProvisioning:BaseUrl`
- `N8nProvisioning:ApiKey`
- `N8nProvisioning:RequestTimeoutSeconds`

Si prefieres variables de entorno, usa:

- `N8nProvisioning__BaseUrl`
- `N8nProvisioning__ApiKey`
- `N8nProvisioning__RequestTimeoutSeconds`

### IA con API-Key

La IA se maneja en dos capas:

- una capa de administración de claves, protegida con JWT;
- una capa pública de generación, protegida con `X-API-Key`.

#### Capa 1 — gestión de claves

| Método | Ruta | Auth | Uso |
| --- | --- | --- | --- |
| `GET` | `/api/me/ai-keys` | JWT | Lista las API-Keys del usuario autenticado, con métricas acumuladas. |
| `POST` | `/api/me/ai-keys` | JWT | Crea una API-Key nueva y devuelve el secreto solo una vez. |
| `POST` | `/api/me/ai-keys/{id}/rotate` | JWT | Regenera la clave y devuelve el nuevo secreto solo una vez. |
| `DELETE` | `/api/me/ai-keys/{id}` | JWT | Revoca la clave. |

Ejemplo de creación:

```http
POST /api/me/ai-keys
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "name": "Mi clave de pruebas"
}
```

Respuesta:

```json
{
  "success": true,
  "message": "AI API key created successfully. Save the secret now; it will not be shown again.",
  "data": {
    "key": {
      "id": 1,
      "userId": 5,
      "name": "Mi clave de pruebas",
      "keyPrefix": "AbCd1234",
      "status": "Active",
      "createdAt": "2026-08-05T12:00:00Z",
      "updatedAt": null,
      "revokedAt": null,
      "lastUsedAt": null,
      "totalRequests": 0,
      "totalPromptTokens": 0,
      "totalCompletionTokens": 0,
      "totalTokens": 0,
      "approxCostUsd": 0
    },
    "secret": "clave-que-solo-se-muestra-una-vez"
  }
}
```

#### Capa 2 — generación de IA

| Método | Ruta | Auth | Uso |
| --- | --- | --- | --- |
| `POST` | `/api/ai/generate` | `X-API-Key` | Genera una respuesta de IA, registra consumo y actualiza métricas por clave. |

La llamada se hace con header `X-API-Key`:

```http
POST /api/ai/generate
Content-Type: application/json
X-API-Key: <secret>
```

Body:

```json
{
  "provider": "cell-a",
  "prompt": "Genera una consulta para listar usuarios con bases activas",
  "mode": "sql",
  "context": "Usamos SQL Server"
}
```

`provider` es opcional. Si lo mandas, el backend intenta usar ese proveedor primero cuando existe en la lista configurada; si no, recorre los proveedores por prioridad.

Modos soportados:

- `sql`
- `summary`
- `recommendation`
- `general` o vacío

El campo `provider` de la respuesta indica la célula/proveedor que terminó atendiendo la solicitud después del failover.

Ejemplo de respuesta:

```json
{
  "success": true,
  "message": "AI response generated successfully.",
  "data": {
    "provider": "local",
    "model": "heuristic-ai",
    "mode": "sql",
    "keyId": 1,
    "keyPrefix": "AbCd1234",
    "result": "Puedes usar una consulta como esta: ...",
    "promptTokens": 42,
    "completionTokens": 120,
    "totalTokens": 162,
    "approxCostUsd": 0,
    "createdAt": "2026-08-05T12:30:00Z"
  }
}
```

Uso recomendado desde frontend o curl:

```bash
curl -X POST "https://<api-host>/api/ai/generate" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: <secret>" \
  -d '{"prompt":"Resume este texto","mode":"summary","context":"..."}'
```

Si `AiService:Providers` está configurado, el backend intenta esos proveedores en orden de prioridad y usa el primero que responda correctamente. Si no hay lista de proveedores, usa el proveedor legacy definido por `AiService:Endpoint`, `ApiKey` y `Model`. Si nada está configurado, responde con un generador local heurístico para que el flujo siga siendo funcional en demo.

Rate limiting:

- gestión de claves: `ai-key-management`
- generación: `ai-api`

Cada request válida actualiza:

- `TotalRequests`
- `TotalPromptTokens`
- `TotalCompletionTokens`
- `TotalTokens`
- `ApproxCostUsd`
- `LastUsedAt`

---

## 4. Administración (requiere rol `Admin`)

Todo lo de esta sección devuelve `403 Forbidden` si el usuario autenticado no tiene `role: "Admin"` en su token. No hay forma de auto-promoverse — el primer admin se marca a mano en la base de datos. Usar solo para un panel interno de soporte, no para las pantallas que ve un estudiante normal.

### Users — `/api/users`

| Método | Ruta | Body | Notas |
| --- | --- | --- | --- |
| `GET` | `/api/users` | — | Lista usuarios activos |
| `GET` | `/api/users/{id}` | — | |
| `POST` | `/api/users` | `UserCreateDto` | Crea un usuario manualmente (no es el flujo de login) |
| `PUT` | `/api/users/{id}` | `UserUpdateDto` | |
| `DELETE` | `/api/users/{id}` | — | Soft delete |

`UserCreateDto` / `UserUpdateDto`:
```json
{
  "name": "Jane Doe",
  "email": "jane@correo.com",
  "avatarUrl": "https://example.com/avatar.png",
  "provider": "Google",
  "providerUserId": "google-sub-123",
  "lastLogin": "2026-07-22T10:00:00Z"
}
```

`UserReadDto` (respuesta): igual que el objeto `user` de la sección 2, incluye `role`.

### Database Instances — `/api/database-instances`

| Método | Ruta | Body |
| --- | --- | --- |
| `GET` | `/api/database-instances` | — |
| `GET` | `/api/database-instances/{id}` | — |
| `POST` | `/api/database-instances` | `DatabaseInstanceCreateDto` |
| `PUT` | `/api/database-instances/{id}` | `DatabaseInstanceUpdateDto` |
| `DELETE` | `/api/database-instances/{id}` | — |

Mismo shape que los objetos de `GET /api/me/databases` (sección 3) más `userId`. Crear/editar acá **no** dispara el aprovisionamiento real en otro motor — es solo el registro de metadata. El aprovisionamiento real hoy lo dispara el autoservicio `POST /api/me/databases` y aprovisiona SQL Server desde Raft.

### Access Credentials — `/api/access-credentials`

| Método | Ruta | Body |
| --- | --- | --- |
| `GET` | `/api/access-credentials` | — |
| `GET` | `/api/access-credentials/{id}` | — |
| `GET` | `/api/access-credentials/by-database-instance/{databaseInstanceId}` | — |
| `POST` | `/api/access-credentials` | `AccessCredentialCreateDto` |
| `PUT` | `/api/access-credentials/{id}` | `AccessCredentialUpdateDto` |
| `DELETE` | `/api/access-credentials/{id}` | — |

**Nunca devuelve la contraseña**, ni en admin. Solo `id`, `databaseInstanceId`, `createdAt`, `updatedAt`, `deletedAt`. Para ver la contraseña, incluso como admin, hoy solo existe el camino de autoservicio (sección 3) — no hay un endpoint admin equivalente todavía.

### Audit Events — `/api/audit-events`

| Método | Ruta | Body |
| --- | --- | --- |
| `GET` | `/api/audit-events` | — |
| `GET` | `/api/audit-events/{id}` | — |
| `POST` | `/api/audit-events` | `AuditEventCreateDto` |
| `PUT` | `/api/audit-events/{id}` | `AuditEventUpdateDto` |
| `DELETE` | `/api/audit-events/{id}` | — |

```json
{
  "id": 101,
  "userId": 1,
  "eventType": "Login",
  "description": "OAuth login completed with Google.",
  "ipAddress": null,
  "additionalData": "{\"provider\":\"Google\",\"providerUserId\":\"...\"}",
  "createdAt": "2026-07-22T14:00:00Z",
  "deletedAt": null
}
```

`eventType` conocidos hoy: `Login`, `Provisioning`, `ProvisioningFailed`, `CredentialRevealed`, `DatabasePaused`, `DatabaseResumed`, `DatabaseDeleted`, `DatabasePausedForInactivity`, `DatabaseDeletedForInactivity`, `DatabasePausedForQuota`, `DatabasePausedForConnections`, `AdminUserCreated`, `AdminUserUpdated`, `AdminUserDeleted`, `AdminDatabaseInstanceCreated`, `AdminDatabaseInstanceUpdated`, `AdminDatabaseInstanceDeleted`, `AdminAccessCredentialCreated`, `AdminAccessCredentialUpdated`, `AdminAccessCredentialDeleted`, `AdminAuditEventCreated`, `AdminAuditEventUpdated`, `AdminAuditEventDeleted`.

Los endpoints administrativos de `Users`, `DatabaseInstances`, `AccessCredentials`, `AuditEvents` y `UserDashboard` también quedan auditados en backend cuando hacen create/update/delete, con eventos tipo `AdminUserCreated`, `AdminDatabaseInstanceUpdated`, `AdminAccessCredentialDeleted`, etc.

### User Dashboard (admin) — `GET /api/users/{userId}/dashboard`

Igual shape que `GET /api/me/databases` (sección 3), pero para cualquier `userId` — uso exclusivo de soporte/admin, ya que expone datos de otros usuarios.

---

## Apéndice — códigos de estado a manejar en el frontend

| Código | Cuándo | Sugerencia UI |
| --- | --- | --- |
| `200` / `201` | Éxito | — |
| `400` | Body inválido / provider no soportado en login | Mostrar el `message` |
| `401` | Falta token, token vencido, o falló el OAuth externo | Redirigir a login |
| `403` | Sin rol `Admin` para una ruta admin | Ocultar la sección, no solo bloquear la llamada |
| `404` | Recurso no existe o no pertenece al usuario | "No encontrado" |
| `409` | Conflicto (ej. creación de usuario duplicado por admin) | Mostrar el `message` |
| `429` | Rate limit excedido | "Probá de nuevo en un minuto" |
| `500` | Error no controlado | Mensaje genérico, no exponer detalles |
