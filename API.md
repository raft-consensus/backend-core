# API Reference — Raft Backend

Guía para el equipo de frontend. Cubre todos los endpoints expuestos hoy, con forma exacta de request/response.

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

**CORS:** solo el origen configurado en `Frontend:BaseUrl` (`https://raft.andrescortes.dev`) puede llamar al API desde el navegador. Si el frontend corre en otro dominio (otro entorno, localhost, etc.), avisar para agregarlo — hoy es un único origen permitido, no una lista.

**Rate limiting:**

| Alcance | Límite | Aplica a |
| --- | --- | --- |
| Global (por IP) | 120 req/min | Todo el API |
| `auth` (por IP) | 10 req/min | `/api/auth/*` |
| `credential-reveal` (por usuario) | 5 req/min | `GET /api/me/databases/{id}/password` |

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

`serviceAvailability` está fijo en `100.0` hasta que se integre monitoreo real — no lo trates como un dato en vivo todavía.

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
https://raft.andrescortes.dev/auth/callback#access_token=<jwt>&expires_at=<iso8601>&provider=<Google|GitHub>
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

**Qué hacer con el `access_token`:** guardarlo (localStorage/sessionStorage, a definir con el equipo) y mandarlo como `Authorization: Bearer <token>` en el resto de las llamadas — esas sí son `fetch` normales, con CORS habilitado para `https://raft.andrescortes.dev`.

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
      "host": "49.13.85.216",
      "port": 3306,
      "databaseName": "raft_u1_a1b2c3d4",
      "databaseUser": "raft_u1_a1b2c3d4",
      "engine": "MySQL",
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

Si el usuario todavía no tiene ninguna BD (p. ej. el aprovisionamiento automático falló tras su primer login), `data` viene como lista vacía — no es un error, es un estado real a manejar en la UI ("aún no tienes una base de datos").

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

Mismo shape que los objetos de `GET /api/me/databases` (sección 3) más `userId`. Crear/editar acá **no** dispara el aprovisionamiento real en MySQL — es solo el registro de metadata. El aprovisionamiento real solo lo dispara el login (sección 2).

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

`eventType` conocidos hoy: `Login`, `Provisioning`, `ProvisioningFailed`, `CredentialRevealed`.

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
