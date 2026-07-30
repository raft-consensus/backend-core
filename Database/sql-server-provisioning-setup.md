# SQL Server - Cuenta de aprovisionamiento

SQL Server no necesita un esquema fijo para la capa de provisión: esta célula crea bases de datos y logins dinámicamente en tiempo de ejecución desde `SqlServerProvisioningService`, usando una cuenta con privilegios acotados.

Ejecutar una sola vez, contra la instancia de SQL Server que esta célula expone como servicio público:

```sql
CREATE LOGIN raft_provisioner WITH PASSWORD = 'REEMPLAZAR_CON_PASSWORD_FUERTE';
GO

GRANT CREATE DATABASE TO raft_provisioner;
GRANT ALTER ANY LOGIN TO raft_provisioner;
GRANT VIEW SERVER STATE TO raft_provisioner;
GO
```

La cuenta debe conectarse con un `ConnectionStrings:SqlServerProvisioning` separado del `RaftDb` del core.

## Qué hace el backend con esta cuenta

Por cada base nueva:

```sql
CREATE DATABASE [raft_u{userId}_{sufijo}];
CREATE LOGIN [raft_u{userId}_{sufijo}] WITH PASSWORD = '<password generado>';
USE [raft_u{userId}_{sufijo}];
CREATE USER [raft_u{userId}_{sufijo}] FOR LOGIN [raft_u{userId}_{sufijo}];
ALTER ROLE [db_owner] ADD MEMBER [raft_u{userId}_{sufijo}];
```

Para pausarla:

```sql
ALTER LOGIN [raft_u{userId}_{sufijo}] DISABLE;
```

Para reanudarla:

```sql
ALTER LOGIN [raft_u{userId}_{sufijo}] ENABLE;
```

Para eliminarla:

```sql
ALTER DATABASE [raft_u{userId}_{sufijo}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [raft_u{userId}_{sufijo}];
DROP LOGIN [raft_u{userId}_{sufijo}];
```

## Permisos

- `CREATE DATABASE` permite crear la base.
- `ALTER ANY LOGIN` permite crear, deshabilitar y eliminar logins.
- `VIEW SERVER STATE` permite al job de ciclo de vida detectar sesiones activas.

En SQL Server 2022 o superior, Microsoft cambió parte de la superficie de permisos sobre DMVs y puede requerirse `VIEW SERVER PERFORMANCE STATE` para consultar `sys.dm_exec_sessions`. En SQL Server 2019 y anteriores, `VIEW SERVER STATE` es suficiente.
