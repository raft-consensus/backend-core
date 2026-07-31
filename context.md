Plataforma de Hosting DB & Servicios para Desarrolladores
El objetivo de este proyecto es diseñar e implementar un sistema de información robusto y seguro que actúe como un proveedor de servicios de bases de datos gratuitas (y herramientas de prueba) para estudiantes y desarrolladores.

La particularidad de este reto radica en su arquitectura: toda la lógica de negocio debe residir y ejecutarse en la base de datos, delegando al backend un rol exclusivamente de mediador, despachador y asegurador de la comunicación.

Filosofía de la Arquitectura
En este proyecto romperemos el paradigma tradicional donde el backend procesa las reglas de negocio. Aquí adoptaremos un enfoque Database-Centric:

⚠️ Regla de Oro: El Backend es "tonto" y la Base de Datos es "inteligente". Ninguna regla de validación compleja, cálculo, asignación de permisos o flujo de negocio se escribe en el código del servidor de aplicaciones. El backend solo recibe la petición, la traslada a la base de datos, y retorna la respuesta estructurada.

Roles de cada componente:
Motor de Base de Datos (SQL Server): Almacena los datos y ejecuta el 100% de la lógica de negocio mediante Stored Procedures (SPs) para operaciones de escritura/modificación, Views para consultas complejas, y Functions para cálculos o transformaciones de datos.
Backend Framework (A elección): Actúa como un middleware de paso. Se encarga de exponer los endpoints HTTP, gestionar la autenticación/autorización, aplicar políticas de tráfico (Rate Limiting) y mapear los resultados de la base de datos hacia el cliente.
Requerimientos Técnicos
Para garantizar la mantenibilidad y el desacoplamiento en el backend, se deberán seguir estrictamente los siguientes lineamientos:

Patrón Repositorio con Inversión de Dependencias (Dependency Inversion):
El backend debe definir interfaces claras (interfaces o contracts) para las operaciones de persistencia.
La implementación concreta del repositorio solo se encargará de invocar a los Stored Procedures o Views correspondientes en SQL Server.
El controlador o servicio del backend dependerá únicamente de la abstracción (la interfaz), cumpliendo con el principio DIP (Dependency Inversion Principle) de SOLID.
Tecnologías:
Base de Datos: Microsoft SQL Server.
Backend: Framework a elección del equipo (se recomienda un stack fuertemente tipado como .NET Web API o soluciones ágiles como Laravel / NestJS).
Viabilidad del Servicio y Políticas de Seguridad
Dado que expondremos un servicio gratuito de aprovisionamiento de bases de datos para desarrollo, es mandatorio mitigar exploits, abusos de recursos y ataques maliciosos. El sistema debe implementar y controlar las siguientes directrices:

Control de Seguridad	Nivel de Aplicación	Descripción
Rate Limiting	Backend / Gateway	Limitar la cantidad de peticiones HTTP por minuto por IP/Usuario para evitar denegación de servicio (DoS) en la API de creación de bases de datos.
Límite de Almacenamiento	SQL Server / Backend	Cada base de datos aprovisionada para un estudiante tendrá un peso máximo estricto (ej. 20 MB). Se debe validar este espacio antes de permitir nuevas escrituras.
Conexiones Concurrentes	SQL Server (User Policy)	Restricción estricta de usuarios concurrentes por base de datos aprovisionada para evitar el agotamiento del pool de conexiones del servidor principal.
Prevención de Inyecciones SQL	Backend & DB	Prohibición absoluta de concatenación de cadenas para consultas. El uso de parámetros en los Stored Procedures es obligatorio.
Ciclo de Vida (TTL)	SQL Server / Jobs	Las bases de datos creadas tendrán una duración máxima de actividad. Si no detectan actividad en un periodo determinado, serán pausadas o eliminadas automáticamente mediante tareas programadas.
Estructura y Aprovisionamiento de Subdominios
Para facilitar el despliegue e integración de los proyectos de cada equipo (célula), el sistema ofrecerá un esquema de direccionamiento dinámico basado en el dominio principal andrescortes.dev.

Jerarquía de Direccionamiento
Cada célula tendrá un espacio de nombres aislado tanto para su interfaz de usuario como para sus servicios de backend independientes (permitiendo
N
cantidad de microservicios o APIs):

Frontend de la Célula: https://[nombre_de_la_celula].andrescortes.dev (Ejemplo: https://alpha.andrescortes.dev)
Servicios / Backends (
N
cantidad): https://[nombre_servicio].[nombre_de_la_celula].andrescortes.dev (Ejemplos: https://api.alpha.andrescortes.dev, https://auth.alpha.andrescortes.dev, https://payments.alpha.andrescortes.dev)

---

## Contrato de la celda SQL Server

Este backend consume SQL Server como fuente de verdad para la lógica de negocio del core de plataforma. La configuración en `appsettings.json` define la conexión principal `ConnectionStrings:RaftDb`, mientras que `ConnectionStrings:MySqlProvisioning` apunta al servicio de otra célula. Además, `Jwt`, `OAuth`, `Frontend`, `DataProtection` y `LifecycleJob` controlan la ejecución del backend, pero no sustituyen la lógica de base de datos.

La regla operativa es esta:

- esta célula expone el servicio core de usuarios, instancias, credenciales, auditoría, métricas y lifecycle de SQL Server;
- otras células consumen este servicio para su propia lógica de negocio;
- esta célula también consume servicios de otras células, como MySQL, para aprovisionamiento y operación;
- `AuthService` solo autentica y emite JWT; no aprovisiona bases durante registro o login;
- la creación de bases queda exclusivamente en `POST /api/me/databases`.

## Matriz SP

| Stored Procedure | Dominio | Uso en backend | Cumple con la idea de negocio | Observación estricta |
| --- | --- | --- | --- | --- |
| `usp_Users_GetAll` | Usuarios | `UserService.GetAllAsync` | Sí | Lectura admin del core de usuarios. |
| `usp_Users_GetById` | Usuarios | `UserService.GetByIdAsync`, `AuthService` | Sí | Contrato de lectura correcto. |
| `usp_Users_Create` | Usuarios | `UserService.CreateAsync` | Parcial | Permite crear usuarios externos manualmente; revisar si `Provider` y `ProviderUserId` deben ser mutables por admin. |
| `usp_Users_Update` | Usuarios | `UserService.UpdateAsync` | Parcial | Mismo riesgo que `Create`: cambia identidad federada y puede romper unicidad semántica. |
| `usp_Users_SoftDelete` | Usuarios | `UserService.SoftDeleteAsync` | Sí | Soft delete correcto. |
| `usp_Users_UpsertFromOAuth` | Auth / Usuarios | `AuthService.CompleteExternalLoginAsync` | Sí | Es el flujo real de login. Mantiene rol y registra auditoría. |
| `usp_Users_RegisterWithPassword` | Auth / Usuarios | `AuthService.RegisterWithPasswordAsync` | Sí | Encaja con login local por contraseña. |
| `usp_Users_GetByEmailForLogin` | Auth / Usuarios | `AuthService.LoginWithPasswordAsync` | Sí | Necesario para autenticación local. |
| `usp_Users_GetSharedSqlServerProvisioningState` | Provisioning | `SqlServerProvisioningService.ProvisionDatabaseAsync` | Sí | La regla de reuso de login/password vive en SQL Server, no en el backend. |
| `usp_DatabaseInstances_GetAll` | Instancias | `DatabaseInstanceService.GetAllAsync` | Sí | Lectura administrativa. |
| `usp_DatabaseInstances_GetById` | Instancias | `DatabaseInstanceService.GetByIdAsync` | Sí | Contrato base correcto. |
| `usp_DatabaseInstances_Create` | Instancias | `DatabaseInstanceService.CreateAsync`, `SqlServerProvisioningService` | Sí | Registra el recurso maestro de SQL Server. |
| `usp_DatabaseInstances_Update` | Instancias | `DatabaseInstanceService.UpdateAsync` | Sí | Correcto para administración interna. |
| `usp_DatabaseInstances_SoftDelete` | Instancias | `DatabaseInstanceService.SoftDeleteAsync`, `SqlServerProvisioningService.DeleteAsync` | Sí | Borra instancia y credencial asociada. |
| `usp_DatabaseInstances_UpdateStatus` | Lifecycle | `SqlServerProvisioningService.UpdateStatusAsync` | Sí | Refleja pausa/activación. |
| `usp_DatabaseInstances_GetDueForPause` | Lifecycle | `DatabaseLifecycleBackgroundService` | Sí | Decide qué instancias pausar por inactividad. |
| `usp_DatabaseInstances_GetDueForDelete` | Lifecycle | `DatabaseLifecycleBackgroundService` | Sí | Decide qué instancias eliminar por TTL. |
| `usp_DatabaseInstances_GetSharedLoginCleanupState` | Lifecycle | `SqlServerProvisioningService.DeleteAsync` | Sí | La decisión de borrar el login compartido vive en SQL Server. |
| `usp_DatabaseInstances_UpdateUsedSpace` | Cuota | `DatabaseLifecycleBackgroundService` | Sí | Mantiene el uso de espacio como dato del core. |
| `usp_DatabaseInstances_TouchActivityByDatabaseName` | Lifecycle | `DatabaseLifecycleBackgroundService` | Sí | Mapea sesiones activas a actividad de la instancia por nombre de base. |
| `usp_AccessCredentials_GetAll` | Credenciales | `AccessCredentialService.GetAllAsync` | Sí | No expone el secreto. |
| `usp_AccessCredentials_GetById` | Credenciales | `AccessCredentialService.GetByIdAsync` | Sí | Lectura administrativa sin secreto. |
| `usp_AccessCredentials_GetByDatabaseInstanceId` | Credenciales | `AccessCredentialService.GetByDatabaseInstanceIdAsync` | Sí | Útil para relación 1:1 con instancia. |
| `usp_AccessCredentials_Create` | Credenciales | `AccessCredentialService.CreateAsync`, `SqlServerProvisioningService` | Sí | Persiste secreto cifrado. |
| `usp_AccessCredentials_Update` | Credenciales | `AccessCredentialService.UpdateAsync` | Sí | Actualiza secreto cifrado. |
| `usp_AccessCredentials_SoftDelete` | Credenciales | `AccessCredentialService.SoftDeleteAsync` | Sí | Soft delete correcto. |
| `usp_AccessCredentials_GetDecryptableByOwner` | Credenciales / Self-service | `AccessCredentialService.RevealPasswordAsync` | Sí | La verificación de dueño vive en SQL Server; el backend solo descifra. |
| `usp_AuditEvents_GetAll` | Auditoría | `AuditEventService.GetAllAsync` | Sí | Lectura administrativa. |
| `usp_AuditEvents_GetById` | Auditoría | `AuditEventService.GetByIdAsync` | Sí | Contrato correcto. |
| `usp_AuditEvents_Create` | Auditoría | `AuditEventService.CreateAsync`, `SqlServerProvisioningService`, `MyDatabasesController` | Sí | Registro central de eventos de negocio. |
| `usp_AuditEvents_Update` | Auditoría | `AuditEventService.UpdateAsync` | Sí | CRUD administrativo, sin lógica adicional. |
| `usp_AuditEvents_SoftDelete` | Auditoría | `AuditEventService.SoftDeleteAsync` | Sí | Soft delete correcto. |
| `usp_PlatformMetrics_Get` | Métricas | `PlatformMetricsService.GetAsync` | Sí | Lectura agregada para dashboard/landing. |
| `usp_UserDashboard_GetByUserId` | Dashboard | `UserDashboardService.GetByUserIdAsync` | Sí | Vista de lectura por usuario dueño. |

## Conclusión operativa

El conjunto de SPs cubre bien el alcance de esta célula como servicio core de SQL Server. La separación correcta es:

- SQL Server: identidad base, instancias, credenciales, auditoría, métricas, lifecycle.
- Backend: autenticación HTTP, orquestación entre células, cifrado/descifrado, ejecución de SPs.
- Otras células: infraestructura externa o lógica de motores alternos como MySQL.

Los puntos que merecen control estricto no son de cobertura funcional, sino de contrato:

- `Provider` y `ProviderUserId` en usuarios son campos sensibles y conviene tratarlos como identidad, no como datos libres.
- `Email` debería tener una decisión explícita de unicidad si va a usarse para login por contraseña.
- `appsettings.json` solo debe contener valores de despliegue; secretos reales deben salir de ahí.

## Decisión de acceso a bases nuevas

Si el equipo quiere que un usuario entre a una base nueva con sus mismas credenciales, entonces no se debe crear un login nuevo por base en SQL Server. El login pasaría a ser una identidad compartida de la cuenta del usuario, y la base de datos solo registraría el recurso y sus permisos.

La implicación técnica es esta:

- el secreto de acceso deja de ser distinto por base;
- `DatabaseInstances.DatabaseUser` pasa a guardar el login compartido del usuario de plataforma;
- `AccessCredentials` sigue existiendo, pero almacena el mismo secreto reutilizado para todas las bases activas de ese usuario;
- el provisioning deja de crear un login nuevo por cada base y pasa a reutilizar un login compartido por usuario;
- pausar o reanudar una base deja de tocar el login a nivel servidor y pasa a bloquear o permitir `CONNECT` en esa base concreta.

En otras palabras: hoy el sistema está modelado como “una identidad SQL por base”. Si se adopta “una identidad SQL por usuario”, el contrato de la celda cambia de forma importante y hay que reescribir la provisión, los SPs y parte del modelo de datos.

## Implementación adoptada en este backend

Para no romper el acceso directo a SQL Server, este backend adopta el login compartido por usuario.
Además, el registro y el login ya no aprovisionan bases de datos. El aprovisionamiento queda solo para el autoservicio `POST /api/me/databases`.

Eso significa:

- un usuario de la plataforma tiene un solo login SQL Server;
- cada base nueva creada para ese usuario reutiliza ese mismo login;
- la contraseña se genera una sola vez y se reusa en las bases siguientes;
- el ciclo de vida sigue siendo por base, pero la actividad se detecta por base de datos, no por login.
- la decisión de reuso y de limpieza del login compartido se consulta en SQL Server mediante SPs, no por escaneo manual de tablas en C#.

## Flujo de negocio actualizado

El modelo operativo quedó así:

- Registro y login:
  - solo validan credenciales;
  - insertan o actualizan el usuario;
  - devuelven JWT;
  - no crean bases.

- Autoservicio:
  - el usuario autenticado solicita la creación en `POST /api/me/databases`;
  - ahí sí se ejecuta el provisioning de SQL Server;
  - ese flujo crea o reutiliza el login compartido del usuario, crea la base y persiste la credencial cifrada.

- Lifecycle:
  - pausa y eliminación siguen siendo automáticas;
  - la actividad se marca por `DatabaseName`;
  - el login compartido solo se borra cuando ya no quedan bases activas del usuario.

## Stored procedures a modificar

| SP | Cambio requerido | Motivo |
| --- | --- | --- |
| `usp_DatabaseInstances_TouchActivityByDatabaseName` | Renombrar desde `usp_DatabaseInstances_TouchActivityByDatabaseUser` y filtrar por `DatabaseName` en vez de `DatabaseUser`. | Con login compartido, el job de actividad ya no puede distinguir instancias por login. |
| `usp_DatabaseInstances_UpdateStatus` | Sin cambio de lógica. | El backend sigue usándolo para marcar `Active` / `Suspended`. |
| `usp_DatabaseInstances_GetDueForPause` | Sin cambio de lógica. | La decisión de qué bases pausar sigue siendo por `LastActivity` y `Status`. |
| `usp_DatabaseInstances_GetDueForDelete` | Sin cambio de lógica. | La decisión de qué bases borrar sigue siendo por TTL. |
| `usp_DatabaseInstances_SoftDelete` | Sin cambio de lógica. | La limpieza física del login compartido la resuelve el backend cuando ya no quedan bases activas del usuario. |
| `usp_DatabaseInstances_Create` / `usp_DatabaseInstances_Update` | Sin cambio de SQL; solo cambian los valores persistidos por el backend. | `DatabaseUser` ahora guarda el login compartido del usuario. |
| `usp_AccessCredentials_*` | Sin cambio de SQL. | La credencial sigue existiendo por instancia, pero el secreto es el mismo para las bases activas del usuario. |

## Propuesta recomendada

La propuesta más sólida para esta plataforma es separar identidad de plataforma e identidad de acceso a infraestructura:

### Opción recomendada: cuenta técnica de SQL Server por base, acceso de usuario por la aplicación

**Idea**

- El usuario inicia sesión en la plataforma con su cuenta normal.
- El backend mantiene una cuenta técnica de SQL Server para operar el servicio.
- Cada base aprovisionada sigue teniendo su aislamiento propio.
- El usuario no recibe un login SQL Server nuevo por cada base.
- El acceso a sus bases se resuelve desde la aplicación, no entregando credenciales SQL directas.

**Ventajas**

- Reduce exposición de credenciales al usuario final.
- Evita gestionar múltiples logins SQL por persona.
- Encaja mejor con una arquitectura de células: el core SQL Server controla el catálogo y el ciclo de vida, mientras otras células manejan sus propios motores.
- Simplifica revocación, rotación y auditoría.

**Desventajas**

- Si el usuario necesita conectarse directamente desde un cliente SQL externo, esta opción no le sirve tal cual.
- Requiere que el backend o una capa intermedia exponga el acceso necesario.

### Opción alternativa: un login SQL compartido por usuario

**Idea**

- Se crea un solo login SQL Server por usuario de plataforma.
- Todas sus bases usan ese mismo login.
- No se genera password por instancia.

**Ventajas**

- Más simple que crear un login por base.
- El usuario recuerda una sola credencial.

**Desventajas**

- Menor aislamiento operativo.
- Cambio de contraseña o revocación afecta todas las bases.
- `AccessCredentials` deja de representar una credencial por instancia y el modelo actual pierde claridad.
- `DatabaseUser` deja de ser un identificador único por base, así que varios SPs y jobs hay que reescribirlos.

## Recomendación final

Para esta plataforma, recomiendo la primera opción: identidad de plataforma separada de la infraestructura, con acceso mediado por el backend.

Motivo:

- esta célula es dueña del servicio core, no de entregar credenciales SQL individuales como producto principal;
- el modelo actual ya apunta a un backend que orquesta y audita, no a un sistema que delega credenciales directas al usuario;
- evita una deuda técnica innecesaria en `DatabaseInstances`, `AccessCredentials`, aprovisionamiento y lifecycle.

Si el equipo decide que el usuario sí debe conectarse directo a SQL Server, entonces la segunda opción es aceptable, pero exige rediseñar el modelo de datos y los SPs antes de implementarla.
