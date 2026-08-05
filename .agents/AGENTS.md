# Reglas y Contexto del Proyecto: Backend Core (Raft-DB / Célula SQL Server)

## 📌 Descripción General del Proyecto
Este backend es el **Core de la Plataforma de Hosting DB & Servicios para Desarrolladores**. Actúa como la célula responsable del aprovisionamiento, autenticación, gestión de instancias, credenciales y ciclo de vida de bases de datos **SQL Server**, además de integrarse con otras células (como MySQL).

---

## 🏛️ Filosofía de Arquitectura (Database-Centric)
> **⚠️ Regla de Oro:** El backend en .NET es "tonto" (middleware de paso) y la base de datos SQL Server es "inteligente".
> Ninguna regla de validación compleja, cálculo, asignación de permisos o flujo de negocio se escribe en C# en el servidor de aplicaciones.

- **SQL Server:** Ejecuta el 100% de la lógica de negocio mediante Stored Procedures (`usp_*`), Views y Functions.
- **Backend (.NET 10 Web API):** Expone endpoints HTTP, maneja autenticación/autorización JWT + OAuth, aplica Rate Limiting, invoca Stored Procedures y retorna respuestas JSON estandarizadas.

---

## 🛠️ Stack Tecnológico
- **Framework:** .NET 10.0 Web API (`raft-backend.csproj`)
- **Lenguaje:** C# 12+
- **Base de Datos:** Microsoft SQL Server (vía `ConnectionStrings:RaftDb`)
- **Documentación API:** Scalar (OpenAPI) (`/scalar/v1`)
- **Autenticación:** JWT Bearer + OAuth (Google & GitHub) + BCrypt para passwords locales
- **Contenedores:** Docker & Docker Compose (`Dockerfile`, `docker-compose.yml`)

---

## 📐 Patrones de Diseño y Estructura del Código
1. **Dependency Inversion Principle (DIP):** Los controladores y servicios dependen estrictamente de abstracciones (`Interfaces/`).
2. **Repository / Service Pattern:**
   - `Controllers/`: Reciben las peticiones HTTP, validan modelo de entrada básico y retornan respuestas con `ApiResponse<T>`.
   - `Services/`: Implementan la lógica de transporte e invocan la BD utilizando Stored Procedures parametrizados.
   - `DTOs/`: Definición estricta de objetos de transferencia de datos de entrada y salida.
   - `Models/`: Modelos de entidad representativos de la BD.
   - `Middleware/`: Manejo global de excepciones, logging y seguridad.

---

## 🗄️ Matriz Principales Stored Procedures
- **Usuarios & Auth:** `usp_Users_GetAll`, `usp_Users_GetById`, `usp_Users_RegisterWithPassword`, `usp_Users_GetByEmailForLogin`, `usp_Users_UpsertFromOAuth`.
- **Aprovisionamiento SQL Server:** `usp_Users_GetSharedSqlServerProvisioningState`, `usp_DatabaseInstances_Create`, `usp_AccessCredentials_Create`.
- **Instancias DB:** `usp_DatabaseInstances_GetAll`, `usp_DatabaseInstances_GetById`, `usp_DatabaseInstances_SoftDelete`, `usp_DatabaseInstances_UpdateStatus`.
- **Credenciales:** `usp_AccessCredentials_GetDecryptableByOwner`, `usp_AccessCredentials_Create`, `usp_AccessCredentials_Update`.
- **Lifecycle & Background Services:** `usp_DatabaseInstances_GetDueForPause`, `usp_DatabaseInstances_GetDueForDelete`, `usp_DatabaseInstances_UpdateUsedSpace`, `usp_DatabaseInstances_TouchActivityByDatabaseName`.
- **Auditoría & Métricas:** `usp_AuditEvents_Create`, `usp_PlatformMetrics_Get`, `usp_UserDashboard_GetByUserId`.

---

## 🔒 Directrices de Seguridad y Calidad
1. **Prevención SQL Injection:** Prohibida la concatenación de strings para SQL. Toda consulta DEBE ser mediante parámetros en Stored Procedures.
2. **Manejo de Credenciales:** La verificación de propiedad para descifrar passwords de BD reside en SQL Server (`usp_AccessCredentials_GetDecryptableByOwner`). El backend únicamente aplica el descifrado seguro.
3. **No Aprovisionamiento Implícito:** La autenticación/registro (`AuthService`) solo valida tokens o usuarios; NO crea bases de datos automáticamente. La creación de bases de datos es exclusiva de `POST /api/me/databases`.

---

## 💻 Comandos del Proyecto
- **Construir el proyecto:** `dotnet build`
- **Ejecutar servidor en desarrollo:** `dotnet run` o `dotnet watch`
- **Probar endpoints HTTP:** Usar `raft-backend.http` o interfaz Scalar en `http://localhost:5000/scalar/v1`

---

## 🤖 Indicaciones para el Asistente IA (Antigravity)
- **Mantener la Regla Database-Centric:** No agregar validaciones de negocio complejas en C#. Consultar o proponer Stored Procedures cuando se requiera nueva lógica.
- **Mantener Tipado Estricto:** Reutilizar o extender DTOs existentes y mantener respuestas unificadas.
- **Respetar Rutas de Archivos:** Las interfaces están en `Interfaces/`, servicios en `Services/`, controladores en `Controllers/`.
