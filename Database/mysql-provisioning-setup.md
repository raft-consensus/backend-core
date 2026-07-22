# MySQL — Cuenta de aprovisionamiento

MySQL no tiene un "schema" fijo que crear de antemano: cada base de datos de estudiante se crea dinámicamente en tiempo de ejecución (`CREATE DATABASE` / `CREATE USER` / `GRANT`) desde `MySqlProvisioningService` en el backend, usando la cuenta que se crea aquí.

Ejecutar **una sola vez**, directamente contra el contenedor/servidor MySQL ya existente (no root, no `ALL PRIVILEGES`):

```sql
CREATE USER IF NOT EXISTS 'raft_provisioner'@'%' IDENTIFIED BY 'REEMPLAZAR_CON_PASSWORD_FUERTE';

-- PROCESS solo expone metadata de conexión/hilos (performance_schema.threads), usada por el
-- job de TTL para saber qué usuarios están conectados. NO da acceso a los datos de ningún
-- estudiante.
GRANT CREATE, DROP, ALTER, CREATE USER, GRANT OPTION, PROCESS ON *.* TO 'raft_provisioner'@'%';

FLUSH PRIVILEGES;
```

El password que pongas ahí es el que va en `ConnectionStrings:MySqlProvisioning` del `appsettings.json` del backend (usuario `raft_provisioner`, no `root`).

## Por qué esta cuenta y no root

Si esta cuenta se compromete, el radio de daño debe quedar acotado a: crear/borrar bases de datos y usuarios, y ver quién está conectado. Nunca debe poder leer los datos de ninguna base de datos de estudiante — por eso no lleva `SELECT` ni `ALL PRIVILEGES`.

## Qué hace el backend con esta cuenta, en tiempo de ejecución

Por cada estudiante que hace login por primera vez (`MySqlProvisioningService.ProvisionDatabaseAsync`):

```sql
CREATE DATABASE `raft_u{userId}_{sufijo}`;
CREATE USER 'raft_u{userId}_{sufijo}'@'%' IDENTIFIED BY '<password generado>';
GRANT ALL PRIVILEGES ON `raft_u{userId}_{sufijo}`.* TO 'raft_u{userId}_{sufijo}'@'%';
ALTER USER 'raft_u{userId}_{sufijo}'@'%' WITH MAX_USER_CONNECTIONS <configurable>;
FLUSH PRIVILEGES;
```

El nombre de base de datos y de usuario siempre se genera del lado del servidor (nunca a partir de input del usuario), y el password se genera con `RandomNumberGenerator` antes de cifrarse con la Data Protection API y guardarse en SQL Server.

El job de ciclo de vida (pausa por inactividad a los 7 días, elimina a los 30) usa la misma cuenta para `ALTER USER ... ACCOUNT LOCK/UNLOCK` y `DROP DATABASE/USER`.
