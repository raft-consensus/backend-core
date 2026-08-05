# 📖 Auditoría y Diagnóstico de Stored Procedures: Backend Core (Raft-DB)

> **Fecha de Auditoría:** 2026-08-04  
> **Servidor BD:** `49.13.85.216` (Base de Datos: `RaftDb`)  
> **Backend Framework:** .NET 10 Web API (`raft-backend.csproj`)  
> **Total Procedimientos Analizados:** 35 SPs

---

## 🏛️ Resumen de Filosofía Operativa
En cumplimiento de la regla **Database-Centric**, toda la lógica de validación, estados de provisión y reglas de negocio residen en **SQL Server**. El backend en C# actúa exclusivamente como middleware de paso, exponiendo los endpoints HTTP, aplicando JWT / Rate Limiting, y realizando la invocación parametrizada de estos procedimientos.

---

## 📊 Matriz Detallada de Diagnóstico y Auditoría

### 1. Dominio: Usuarios y Autenticación

| Stored Procedure | Servicio C# Invocador | Endpoint HTTP / Origen | Descripción y Lógica Interna (SQL Server) | Información Complementaria y Contexto |
|---|---|---|---|---|
| `usp_Users_GetAll` | `UserService.GetAllAsync` | `GET /api/users` | Recupera el listado completo de usuarios registrados en el sistema cuyo estado no esté marcado como eliminado (`IsDeleted = 0`). | Utilizado en paneles administrativos. Excluye hashes de contraseñas de las respuestas devueltas al cliente. |
| `usp_Users_GetById` | `UserService.GetByIdAsync` | `GET /api/users/{id}` | Busca y retorna la información detallada de un usuario específico mediante su `UserId`. | Permite a los controladores verificar perfiles y validar existencia de usuarios en consultas directas. |
| `usp_Users_Create` | `UserService.CreateAsync` | `POST /api/users` | Inserta un nuevo registro de usuario manualmente en la tabla `Users` asignando email, rol y estado. | Operación estrictamente administrativa. **No aprovisiona bases de datos automáticamente** cumpliendo el desacoplamiento del sistema. |
| `usp_Users_Update` | `UserService.UpdateAsync` | `PUT /api/users/{id}` | Modifica los datos básicos del perfil de usuario (Nombre, Email, Rol, Estado) en la base de datos. | ⚠️ **Precaución:** Se debe evitar alterar campos de identidad federada (`Provider` y `ProviderUserId`) para no romper la asociación con OAuth. |
| `usp_Users_SoftDelete` | `UserService.SoftDeleteAsync` | `DELETE /api/users/{id}` | Realiza un borrado lógico del usuario estableciendo `IsDeleted = 1` y registrando la fecha de modificación. | Preserva la trazabilidad histórica de auditoría y relaciones de bases de datos pasadas sin destruir datos físicos. |
| `usp_Users_RegisterWithPassword` | `AuthService.RegisterWithPasswordAsync` | `POST /api/auth/register` | Inserta un usuario de autenticación local recibiendo su correo y el hash BCrypt de la contraseña. | El hash de contraseña es generado previamente en C# con `BCrypt.Net-Next`. Este procedimiento no aprovisiona recursos de base de datos. |
| `usp_Users_GetByEmailForLogin` | `AuthService.LoginWithPasswordAsync` | `POST /api/auth/login` | Busca y retorna el usuario activo correspondiente a un email específico para el flujo de login por contraseña. | Devuelve el hash de contraseña almacenado para que el backend verifique la clave introducida y emita el JWT correspondiente. |
| `usp_Users_UpsertFromOAuth` | `AuthService.CompleteExternalLoginAsync` | `GET /api/auth/external-callback` | Registra un nuevo usuario federado (GitHub / Google) o actualiza su perfil (Avatar, Nombre) si ya existía. | Garantiza la unicidad basada en la combinación `Provider` + `ProviderUserId`. Asigna el rol por defecto de 'Estudiante' y registra auditoría interna. |

---

### 2. Dominio: Aprovisionamiento de SQL Server e Instancias

| Stored Procedure | Servicio C# Invocador | Endpoint HTTP / Origen | Descripción y Lógica Interna (SQL Server) | Información Complementaria y Contexto |
|---|---|---|---|---|
| `usp_Users_GetSharedSqlServerProvisioningState` | `SqlServerProvisioningService.ProvisionDatabaseAsync` | `POST /api/me/databases` | Evalúa si el usuario ya posee un Login SQL Server compartido o si requiere la creación de uno nuevo en el servidor. | **Inteligencia en BD:** Implementa la regla de reuso de credenciales principales cuando un estudiante crea múltiples bases de datos. |
| `usp_DatabaseInstances_GetAll` | `DatabaseInstanceService.GetAllAsync` | `GET /api/database-instances` | Obtiene la lista global de todas las instancias de bases de datos creadas en la plataforma. | Utilizado por la vista administrativa para monitoreo global de infraestructura y almacenamiento consumido. |
| `usp_DatabaseInstances_GetById` | `DatabaseInstanceService.GetByIdAsync` | `GET /api/database-instances/{id}` | Retorna el detalle técnico de una instancia específica consultada mediante su `DatabaseInstanceId`. | Muestra el estado operacional (Active, Paused, Deleted), cuotas de espacio y usuario asignado. |
| `usp_DatabaseInstances_Create` | `SqlServerProvisioningService.ProvisionDatabaseAsync` | `POST /api/me/databases` | Registra la nueva instancia en la tabla `DatabaseInstances` asociando cuotas de almacenamiento y estado 'Active'. | Se ejecuta tras el aprovisionamiento físico exitoso del archivo de base de datos en SQL Server mediante comandos DDL. |
| `usp_DatabaseInstances_Update` | `DatabaseInstanceService.UpdateAsync` | `PUT /api/database-instances/{id}` | Permite actualizar parámetros administrativos de una instancia (nombre de BD, cuota máxima, estado). | Utilizado por administradores del sistema para reajustes de cuota o correcciones de infraestructura. |
| `usp_DatabaseInstances_SoftDelete` | `SqlServerProvisioningService.DeleteDatabaseAsync` | `DELETE /api/me/databases/{id}` | Marca la instancia como eliminada (`IsDeleted = 1`) y registra la fecha de baja en el sistema. | Se ejecuta en conjunto con la desconexión o eliminación física del archivo `.mdf` / `.ldf` de la base de datos. |
| `usp_DatabaseInstances_UpdateStatus` | `SqlServerProvisioningService.Pause/Resume` | `PUT /api/me/databases/{id}/pause` y `/resume` | Cambia el estado operacional de la instancia de base de datos (ejemplo: 'Active' ↔ 'Paused'). | Invocado directamente por las acciones self-service del estudiante o automáticamente por el Background Job de inactividad. |
| `usp_DatabaseInstances_GetSharedLoginCleanupState` | `SqlServerProvisioningService.DeleteDatabaseAsync` | `DELETE /api/me/databases/{id}` | Verifica si al eliminar una base de datos el usuario se queda sin instancias activas para determinar si borrar el Login de SQL Server. | **Inteligencia en BD:** Previene dejar Logins huérfanos en la instancia de SQL Server cuando un usuario elimina su última base de datos. |

---

### 3. Dominio: Ciclo de Vida y Background Services

| Stored Procedure | Servicio C# Invocador | Endpoint HTTP / Origen | Descripción y Lógica Interna (SQL Server) | Información Complementaria y Contexto |
|---|---|---|---|---|
| `usp_DatabaseInstances_GetDueForPause` | `DatabaseLifecycleBackgroundService` | ⚙️ **Background Worker** (cada 15 min) | Selecciona las instancias de base de datos 'Active' que no hayan registrado actividad reciente en los últimos 7 días. | Permite al trabajador en segundo plano pausar automáticamente bases inactivas y liberar memoria RAM/CPU en el servidor. |
| `usp_DatabaseInstances_GetDueForDelete` | `DatabaseLifecycleBackgroundService` | ⚙️ **Background Worker** (cada 15 min) | Identifica las instancias que han permanecido pausadas o inactivas por más de 30 días para su purga definitiva. | Automatiza la recuperación de espacio en disco eliminando recursos abandonados. |
| `usp_DatabaseInstances_UpdateUsedSpace` | `DatabaseLifecycleBackgroundService` | ⚙️ **Background Worker** (cada 15 min) | Actualiza los bytes consumidos en disco por cada base de datos en la tabla `DatabaseInstances`. | El servicio en segundo plano mide el peso físico del archivo en el servidor y ejecuta este SP para mantener las métricas actualizadas. |
| `usp_DatabaseInstances_TouchActivityByDatabaseName` | `DatabaseLifecycleBackgroundService` | ⚙️ **Background Worker** (cada 15 min) | Actualiza la fecha de última actividad buscando conexiones activas en `sys.dm_exec_sessions` filtradas por el nombre de la BD. | ⚡ **Actualizado (31-Jul-2026):** Reemplazó la búsqueda por usuario para soportar adecuadamente el esquema de logins compartidos por estudiante. |
| `usp_DatabaseInstances_TouchActivityByDatabaseUser` | *Ninguno (Desconectado)* | *Ninguno* | Registraba la actividad filtrando únicamente por el nombre de usuario de la base de datos. | 🟡 **Obsoleto / Legacy:** Permaneció en el motor tras la migración del 31 de Julio. Se recomienda su depuración eventual en la BD. |

---

### 4. Dominio: Gestión de Credenciales y Cifrado

| Stored Procedure | Servicio C# Invocador | Endpoint HTTP / Origen | Descripción y Lógica Interna (SQL Server) | Información Complementaria y Contexto |
|---|---|---|---|---|
| `usp_AccessCredentials_GetAll` | `AccessCredentialService.GetAllAsync` | `GET /api/access-credentials` | Retorna el listado de credenciales registradas omitiendo los secretos o contraseñas en texto plano. | Uso administrativo. Retorna parámetros de conexión (Host, Puerto, Engine, Username) manteniendo la seguridad de los secretos. |
| `usp_AccessCredentials_GetById` | `AccessCredentialService.GetByIdAsync` | `GET /api/access-credentials/{id}` | Obtiene los datos de una credencial específica según su identificador único `AccessCredentialId`. | Utilizado en validaciones internas de conectividad por el equipo de administración. |
| `usp_AccessCredentials_GetByDatabaseInstanceId` | `AccessCredentialService.GetByDatabaseInstanceIdAsync` | `GET /api/access-credentials/by-instance/{id}` | Recupera la credencial vinculada directamente a una instancia de base de datos determinada (Relación 1:1). | Permite consultar la configuración de acceso asociada a la base de datos de un estudiante. |
| `usp_AccessCredentials_Create` | `SqlServerProvisioningService.ProvisionDatabaseAsync` | `POST /api/me/databases` | Almacena los parámetros de acceso y la contraseña previamente cifrada en la tabla `AccessCredentials`. | La contraseña es cifrada en el backend mediante `DataProtection` antes de ser enviada como parámetro a este Stored Procedure. |
| `usp_AccessCredentials_Update` | `AccessCredentialService.UpdateAsync` | `PUT /api/access-credentials/{id}` | Actualiza la información de conexión o renueva el secreto cifrado de una credencial existente. | Permite el restablecimiento o rotación de contraseñas de las bases de datos asignadas. |
| `usp_AccessCredentials_SoftDelete` | `SqlServerProvisioningService.DeleteDatabaseAsync` | `DELETE /api/me/databases/{id}` | Marca la credencial asociada como eliminada (`IsDeleted = 1`). | Invocado automáticamente durante el flujo de eliminación de la instancia de base de datos. |
| `usp_AccessCredentials_GetDecryptableByOwner` | `AccessCredentialService.RevealPasswordAsync` | `GET /api/me/databases/{id}/credentials` | Valida en SQL Server que el `UserId` sea el dueño legítimo de la instancia antes de retornar la contraseña cifrada. | 🔒 **Control de Seguridad Core:** SQL Server verifica la propiedad del recurso. Si es válido, el backend en C# descifra el secreto y lo entrega en texto plano. |

---

### 5. Dominio: Auditoría de Eventos

| Stored Procedure | Servicio C# Invocador | Endpoint HTTP / Origen | Descripción y Lógica Interna (SQL Server) | Información Complementaria y Contexto |
|---|---|---|---|---|
| `usp_AuditEvents_GetAll` | `AuditEventService.GetAllAsync` | `GET /api/audit-events` | Retorna la bitácora completa de eventos de auditoría registrados en la plataforma. | Permite a los administradores revisar el historial global de operaciones del sistema. |
| `usp_AuditEvents_GetById` | `AuditEventService.GetByIdAsync` | `GET /api/audit-events/{id}` | Recupera la información detallada de un evento de auditoría específico por `AuditEventId`. | Incluye metadata detallada (IP de origen, ID del usuario ejecutor, payload JSON y timestamp). |
| `usp_AuditEvents_Create` | `AuditEventService.CreateAsync` | Interno (Provisioning y acciones Self-Service) | Inserta un nuevo registro de evento de auditoría en la tabla `AuditEvents`. | Se llama automáticamente tras acciones sensibles como `DATABASE_PROVISIONED`, `USER_REGISTERED` o `PASSWORD_REVEALED`. |
| `usp_AuditEvents_Update` | `AuditEventService.UpdateAsync` | `PUT /api/audit-events/{id}` | Permite actualizar anotaciones o metadatos de un registro de auditoría existente. | Operación administrativa secundaria para enriquecimiento de bitácoras. |
| `usp_AuditEvents_SoftDelete` | `AuditEventService.SoftDeleteAsync` | `DELETE /api/audit-events/{id}` | Aplica borrado lógico (`IsDeleted = 1`) a un registro de auditoría. | Preserva la información en la base de datos sin destruir la evidencia histórica. |

---

### 6. Dominio: Métricas y Dashboards

| Stored Procedure | Servicio C# Invocador | Endpoint HTTP / Origen | Descripción y Lógica Interna (SQL Server) | Información Complementaria y Contexto |
|---|---|---|---|---|
| `usp_PlatformMetrics_Get` | `PlatformMetricsService.GetAsync` | `GET /api/metrics` | Ejecuta consultas agregadas para calcular métricas globales (Total Usuarios, BDs Activas, Espacio Usado). | **Endpoint Público:** Alimenta los contadores y estadísticas en tiempo real expuestos en la landing page del proyecto. |
| `usp_UserDashboard_GetByUserId` | `UserDashboardService.GetByUserIdAsync` | `GET /api/me/dashboard` | Compila el resumen de recursos del estudiante (bases creadas, espacio consumido, límites restantes y listado de BDs). | Consumido por el panel principal del usuario estudiante al iniciar sesión en la plataforma. |

---

## 📌 Hallazgos Operativos y Recomendaciones

1. **Alineación del Backend (.NET 10) con la BD:**
   - 34 de los 35 Stored Procedures se encuentran integrados activamente en la capa de servicios de .NET (`Services/`).
   - Todos los flujos críticos de negocio y seguridad cumplen con la premisa de validación en base de datos.

2. **Recomendación de Limpieza:**
   - Se sugiere eliminar o archivar el procedimiento `usp_DatabaseInstances_TouchActivityByDatabaseUser` en la base de datos para mantener la consistencia con las migraciones del 31 de Julio de 2026.
