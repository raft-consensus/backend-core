# 🛡️ Matriz de Seguridad, Aislamiento Multitenant y Ciclo de Vida por Motor (Raft-DB Core)

## 📌 1. Visión General de la Arquitectura de Motores

La plataforma **Raft-DB Backend Core** gestiona y aprovisiona cuatro motores de bases de datos diferentes, garantizando en cada uno:
1. **Aislamiento Multi-inquilino (Multitenancy):** Ningún estudiante/usuario puede ver, consultar ni manipular bases de datos ajenas ni catálogos administrativos del sistema.
2. **Protección de Integridad:** Prevención de borrado accidental o malicioso de bases de datos (`DROP DATABASE`).
3. **Ciclo de Vida Estandarizado:** Soporte para estados `Active`, `Paused / Suspended`, `Orphaned` (retención de 30 días) y `Deleted` (purga física).
4. **Métricas Reales:** Monitoreo transparente del espacio físico ocupado en disco.

---

## 📊 2. Cuadro Comparativo de Implementación por Motor

| Característica / Operación | Microsoft SQL Server | PostgreSQL | MongoDB | MySQL (Célula ABA) |
| :--- | :--- | :--- | :--- | :--- |
| **Infraestructura** | Servidor local / contenedor | Servidor dedicado `49.13.85.216:5432` | Servidor dedicado `49.13.85.216:27017` | Cluster externo `db.aba.andrescortes.dev:3306` |
| **Rol / Usuario inicial** | `LOGIN` con permisos mínimos a nivel servidor | `ROLE` con `NOCREATEDB NOCREATEROLE` | Usuario limitado a base con `readWrite` | Usuario generado con prefijo por ABA |
| **Aislamiento en Gestores (DBeaver / Compass)** | `REVOKE VIEW ANY DATABASE FROM [public]` | `REVOKE ALL / CONNECT FROM PUBLIC` (filtro con desmarcado de "Show all databases") | Aislamiento nativo de MongoDB (sin rol clusterAdmin) | Aislamiento nativo de MySQL (`SHOW DATABASES`) |
| **Prevención de `DROP DATABASE`** | Server DDL Trigger `trg_PreventUserDropDatabase` en `master` | Bloqueado por permisos / propiedad | Sin rol administrativo | **ProxySQL** con reglas de firewall de consultas |
| **Pausar / Detener** | `ALTER DATABASE ... SET READ_ONLY WITH ROLLBACK IMMEDIATE;` | `ALTER DATABASE ... SET default_transaction_read_only = on;` + `REVOKE CONNECT` | `updateUser` con `{ role: "read" }` (solo lectura) | Administrado por ABA con auto-pausa por cuota (20 MB) |
| **Reanudar / Iniciar** | `ALTER DATABASE ... SET READ_WRITE WITH ROLLBACK IMMEDIATE;` | `ALTER DATABASE ... SET default_transaction_read_only = off;` + `GRANT CONNECT` | `updateUser` con `{ role: "readWrite" }` | Activo / Gestionado por ABA |
| **Eliminar (Orfandad / Soft-Delete)** | Reasignar `OWNER` a `[raft_backend]` + `READ_ONLY` + `Orphaned` | Reasignar `OWNER` a `raft_pg_admin` + `REVOKE ALL/CONNECT` + `Orphaned` | `dropUser` en base de datos + `Orphaned` | **Rotación silenciosa de credenciales:** `POST /credenciales/reset` + `Orphaned` |
| **Purga Física (30 días)** | `DROP DATABASE [...]` | `DROP DATABASE IF EXISTS "..."` + `DROP ROLE` | `DropDatabaseAsync(...)` | `DELETE /partners/databases/{id}` |
| **Medición de Almacenamiento** | `FILEPROPERTY('SpaceUsed') * 8192` | `pg_database_size('nombre_db')` | `dbStats` (`dataSize` / `storageSize`) | Sincronización API ABA (`espacioUtilizadoMB`) |

---

## 🏛️ 3. Detalle Técnico por Motor

### 1. Microsoft SQL Server (`SqlServerProvisioningService.cs`)
- **Aprovisionamiento:**
  ```sql
  CREATE LOGIN [raft_u11] WITH PASSWORD = '...';
  CREATE DATABASE [raft_u11_b871bee2];
  ALTER AUTHORIZATION ON DATABASE::[raft_u11_b871bee2] TO [raft_u11];
  ```
- **Aislamiento:**
  ```sql
  REVOKE VIEW ANY DATABASE FROM [public];
  GRANT VIEW ANY DATABASE TO [raft_backend];
  ```
- **DDL Trigger de Protección:**
  ```sql
  CREATE TRIGGER [trg_PreventUserDropDatabase]
  ON ALL SERVER
  FOR DROP_DATABASE
  AS
  BEGIN
      IF SUSER_NAME() NOT IN ('sa', 'raft_backend')
      BEGIN
          RAISERROR('Operación no permitida. Elimine la base de datos desde el panel de Raft.', 16, 1);
          ROLLBACK;
      END
  END;
  ```
- **Pausa y Reanudación:** Cambio atómico de modo lectura/escritura con `WITH ROLLBACK IMMEDIATE` para evitar bloqueos por sesiones concurrentes.
- **Orfandad:** Se transfiere la propiedad a `[raft_backend]` para que desaparezca del DBeaver del estudiante y se pone en `READ_ONLY`.

---

### 2. PostgreSQL (`PostgresProvisioningService.cs`)
- **Aprovisionamiento:**
  ```sql
  CREATE ROLE "raft_u11" WITH LOGIN PASSWORD '...' NOCREATEDB NOCREATEROLE INHERIT;
  CREATE DATABASE "raft_u11_5debe5e5" OWNER "raft_u11";
  REVOKE ALL ON DATABASE "raft_u11_5debe5e5" FROM PUBLIC;
  REVOKE CONNECT ON DATABASE "raft_u11_5debe5e5" FROM PUBLIC;
  GRANT CONNECT ON DATABASE "raft_u11_5debe5e5" TO "raft_u11";
  ```
- **Aislamiento Global de Servidor:**
  ```sql
  -- Vacunar la plantilla maestra para que ninguna base nueva nazca abierta:
  REVOKE CONNECT ON DATABASE template1 FROM PUBLIC;
  REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
  REVOKE CONNECT ON DATABASE "raft-postgres" FROM PUBLIC;
  REVOKE CONNECT ON DATABASE "raft_olap" FROM PUBLIC;
  ```
- **Pausa:** `ALTER DATABASE "..." SET default_transaction_read_only = on;` y `REVOKE CONNECT`.
- **Reanudación:** `ALTER DATABASE "..." SET default_transaction_read_only = off;` y `GRANT CONNECT`.
- **Orfandad:** `ALTER DATABASE "..." OWNER TO "raft_pg_admin";` + `REVOKE ALL ON DATABASE ... FROM "raft_u11";`.

---

### 3. MongoDB (`MongoProvisioningService.cs`)
- **Aprovisionamiento:**
  ```javascript
  db.runCommand({
    createUser: "raft_mg_u11_cbbb123c",
    pwd: "...",
    roles: [{ role: "readWrite", db: "raft_mg_u11_cbbb123c" }]
  });
  ```
- **Pausa (Solo Lectura):**
  ```javascript
  db.runCommand({
    updateUser: "raft_mg_u11_cbbb123c",
    roles: [{ role: "read", db: "raft_mg_u11_cbbb123c" }]
  });
  ```
- **Reanudación (Lectura y Escritura):**
  ```javascript
  db.runCommand({
    updateUser: "raft_mg_u11_cbbb123c",
    roles: [{ role: "readWrite", db: "raft_mg_u11_cbbb123c" }]
  });
  ```
- **Orfandad (Revocación Inmediata de Credenciales):**
  ```javascript
  db.runCommand({ dropUser: "raft_mg_u11_cbbb123c" });
  ```
- **Métricas:** Consulta mediante comando `dbStats` y `serverStatus.connections.current`.

---

### 4. MySQL — Integración con Célula ABA (`MySqlProvisioningService.cs`)
- **Arquitectura:** Consumo de API REST Server-to-Server (`https://api.aba.andrescortes.dev/partners/databases`) autenticado vía Bearer Token (`MySqlProvisioning:ApiKey`).
- **Aprovisionamiento:** `POST /partners/databases` → Retorna `host: db.aba.andrescortes.dev`, `puerto: 3306`, `nombreBD`, `usuarioBD` y `passwordTemporal`.
- **Protección contra `DROP DATABASE`:** Gestionado por **ProxySQL** en la célula ABA (bloquea sentencias `DROP DATABASE` a nivel de reglas de proxy sin restringir `DROP TABLE`).
- **El Truco de Orfandad (Soft-Delete Diferido):**
  - Al presionar "Eliminar", Raft **no** llama a `DELETE` en ABA.
  - En su lugar, llama a `POST /partners/databases/{id}/credenciales/reset`.
  - La clave vieja del usuario es revocada de inmediato en el cluster de MySQL impidiendo cualquier conexión externa.
  - La base se mantiene como huérfana en `RaftDb` por 30 días.
- **Purga Física (30 días):** `DELETE /partners/databases/{id}` en ABA ejecuta el `DROP DATABASE` físico definitivo.
- **Frontend Flutter:** Botón "Detener/Iniciar" deshabilitado con Tooltip informativo para MySQL, dejando el control de auto-pausa de cuota a ProxySQL de ABA.

---

## 📁 4. Scripts de Inicialización y Migración Disponibles

Los scripts SQL generados para configurar la seguridad en los motores residen en `Database/Scripts/`:
1. [`init_sqlserver_security_and_triggers.sql`](file:///c:/Users/ASUS/Desktop/RIWI/complementos/celulas/raft-db/backend-core/Database/Scripts/init_sqlserver_security_and_triggers.sql): Configura `VIEW ANY DATABASE` y el trigger `trg_PreventUserDropDatabase` en `master` de SQL Server.
2. [`fix_database_authorizations_and_permissions.sql`](file:///c:/Users/ASUS/Desktop/RIWI/complementos/celulas/raft-db/backend-core/Database/Scripts/fix_database_authorizations_and_permissions.sql): Migra las bases de datos existentes de SQL Server asignándoles su dueño y permisos correspondientes.
3. [`init_postgres_security.sql`](file:///c:/Users/ASUS/Desktop/RIWI/complementos/celulas/raft-db/backend-core/Database/Scripts/init_postgres_security.sql): Vacuna `template1` y asegura permisos para `raft_pg_admin`.
4. [`fix_postgres_authorizations_and_isolation.sql`](file:///c:/Users/ASUS/Desktop/RIWI/complementos/celulas/raft-db/backend-core/Database/Scripts/fix_postgres_authorizations_and_isolation.sql): Recorre y aisla bases de datos existentes en PostgreSQL.
