# SQL Server — Schema, Views y Stored Procedures

Ejecutar en orden, de arriba hacia abajo, contra la base de datos `RaftDb` (o el nombre que le hayan puesto). Cada bloque es idempotente donde es posible (`CREATE OR ALTER`), salvo la creación de tablas, que solo debe correrse una vez.

> No hay migraciones EF ni ejecución automática desde el backend — este script es la única fuente de verdad del esquema. El backend solo consume las tablas/SPs/Views ya creados, vía `ISqlStoredProcedureExecutor`.

> [!WARNING]
> **`SET NOCOUNT ON` en Stored Procedures que usan `ExecuteAsync`:** El backend llama a `SqlStoredProcedureExecutor.ExecuteAsync` (que internamente usa `ExecuteNonQueryAsync` de ADO.NET) para detectar si un UPDATE/DELETE afectó alguna fila (`rows > 0` = éxito, `rows = 0` = no encontrado → 404). Si el SP incluye `SET NOCOUNT ON`, ADO.NET **no recibe el conteo de filas afectadas** y siempre devuelve 0, haciendo que el backend retorne 404 aunque la operación haya funcionado en la BD. **Regla:** solo omitir `SET NOCOUNT ON` en SPs invocados vía `ExecuteAsync`; los SPs usados con `QueryAsync` / `QuerySingleOrDefaultAsync` sí pueden usarlo libremente.

---

## 0. Login de aplicación

Cuenta de mínimo privilegio para que el backend se conecte — nunca `sa`. Mismo criterio que `raft_provisioner` en MySQL: si se compromete, el radio de daño queda acotado a `RaftDb`, no a todo el servidor. Reemplazar el password antes de correr esto.

```sql
CREATE LOGIN raft_backend WITH PASSWORD = 'REEMPLAZAR_CON_PASSWORD_FUERTE';

USE RaftDb;
CREATE USER raft_backend FOR LOGIN raft_backend;

-- db_datareader/db_datawriter cubren las tablas; EXECUTE es necesario aparte para los SPs.
ALTER ROLE db_datareader ADD MEMBER raft_backend;
ALTER ROLE db_datawriter ADD MEMBER raft_backend;
GRANT EXECUTE TO raft_backend;
```

El `User Id`/`Password` de `ConnectionStrings:RaftDb` en `appsettings.json` deben ser este login, no el admin del servidor.

---

## 1. Tablas

### 1.1 Migración segura para tablas existentes

Si `dbo.Users` ya existe en la base, aplica solo esta migración antes de volver a publicar los stored procedures:

```sql
IF COL_LENGTH('dbo.Users', 'PasswordHash') IS NULL
BEGIN
    ALTER TABLE dbo.Users
    ADD PasswordHash NVARCHAR(255) NULL;
END;

IF COL_LENGTH('dbo.Users', 'PasswordUpdated_at') IS NULL
BEGIN
    ALTER TABLE dbo.Users
    ADD PasswordUpdated_at DATETIME2 NULL;
END;

IF COL_LENGTH('dbo.Users', 'TemporaryPasswordHash') IS NULL
BEGIN
    ALTER TABLE dbo.Users
    ADD TemporaryPasswordHash NVARCHAR(255) NULL;
END;

IF COL_LENGTH('dbo.Users', 'TemporaryPasswordExpires_at') IS NULL
BEGIN
    ALTER TABLE dbo.Users
    ADD TemporaryPasswordExpires_at DATETIME2 NULL;
END;
```

Regla operativa:

- `PasswordHash IS NULL` significa que la cuenta no tiene contraseña local.
- Si `Provider` es `Google` o `GitHub` y `PasswordHash IS NULL`, las opciones de recuperar o cambiar contraseña deben permanecer deshabilitadas.
- La contraseña temporal se guarda en `TemporaryPasswordHash` con expiración en `TemporaryPasswordExpires_at`; no reemplaza la contraseña local hasta que el usuario haga el cambio final.
- El backend solo debe permitir acciones de password cuando la cuenta tenga contraseña local efectiva.
- `HasLocalPassword` y `PasswordChangeRequired` no se guardan como estado independiente: se derivan de las columnas de contraseña al consultar.

```sql
CREATE TABLE Users (
    Id INT IDENTITY(1,1) NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    Email NVARCHAR(320) NOT NULL,
    Organization NVARCHAR(200) NULL,
    PasswordHash NVARCHAR(255) NULL,
    PasswordUpdated_at DATETIME2 NULL,
    TemporaryPasswordHash NVARCHAR(255) NULL,
    TemporaryPasswordExpires_at DATETIME2 NULL,
    AvatarUrl NVARCHAR(2048) NULL,
    Provider NVARCHAR(50) NOT NULL,
    ProviderUserId NVARCHAR(200) NOT NULL,
    Role NVARCHAR(20) NOT NULL CONSTRAINT DF_Users_Role DEFAULT ('User'),
    Created_at DATETIME2 NOT NULL CONSTRAINT DF_Users_Created_at DEFAULT (SYSUTCDATETIME()),
    Updated_at DATETIME2 NULL,
    Deleted_at DATETIME2 NULL,
    LastLogin DATETIME2 NULL,
    CONSTRAINT PK_Users PRIMARY KEY (Id)
);

CREATE UNIQUE INDEX IX_Users_Provider_ProviderUserId ON Users (Provider, ProviderUserId);

CREATE INDEX IX_Users_Email ON Users (Email);

CREATE TABLE DatabaseInstances (
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    Host NVARCHAR(255) NOT NULL,
    Port INT NOT NULL,
    DatabaseName NVARCHAR(128) NOT NULL,
    DatabaseUser NVARCHAR(128) NOT NULL,
    Engine NVARCHAR(50) NOT NULL,
    Status NVARCHAR(50) NOT NULL CONSTRAINT DF_DatabaseInstances_Status DEFAULT ('Active'),
    UsedSpaceBytes BIGINT NOT NULL CONSTRAINT DF_DatabaseInstances_UsedSpaceBytes DEFAULT (0),
    MaxSpaceBytes BIGINT NOT NULL CONSTRAINT DF_DatabaseInstances_MaxSpaceBytes DEFAULT (20971520),
    LastActivity DATETIME2 NULL,
    Created_at DATETIME2 NOT NULL CONSTRAINT DF_DatabaseInstances_Created_at DEFAULT (SYSUTCDATETIME()),
    Updated_at DATETIME2 NULL,
    Deleted_at DATETIME2 NULL,
    CONSTRAINT PK_DatabaseInstances PRIMARY KEY (Id),
    CONSTRAINT FK_DatabaseInstances_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id)
);

CREATE INDEX IX_DatabaseInstances_UserId ON DatabaseInstances (UserId);

CREATE TABLE AccessCredentials (
    Id INT IDENTITY(1,1) NOT NULL,
    DatabaseInstanceId INT NOT NULL,
    EncryptedPassword NVARCHAR(1024) NOT NULL,
    Created_at DATETIME2 NOT NULL CONSTRAINT DF_AccessCredentials_Created_at DEFAULT (SYSUTCDATETIME()),
    Updated_at DATETIME2 NULL,
    Deleted_at DATETIME2 NULL,
    CONSTRAINT PK_AccessCredentials PRIMARY KEY (Id),
    CONSTRAINT FK_AccessCredentials_DatabaseInstances_DatabaseInstanceId FOREIGN KEY (DatabaseInstanceId)
        REFERENCES DatabaseInstances (Id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IX_AccessCredentials_DatabaseInstanceId ON AccessCredentials (DatabaseInstanceId);

CREATE TABLE AuditEvents (
    Id BIGINT IDENTITY(1,1) NOT NULL,
    UserId INT NULL,
    EventType NVARCHAR(100) NOT NULL,
    Description NVARCHAR(2000) NOT NULL,
    IpAddress NVARCHAR(45) NULL,
    AdditionalData NVARCHAR(MAX) NULL,
    Created_at DATETIME2 NOT NULL CONSTRAINT DF_AuditEvents_Created_at DEFAULT (SYSUTCDATETIME()),
    Deleted_at DATETIME2 NULL,
    CONSTRAINT PK_AuditEvents PRIMARY KEY (Id),
    CONSTRAINT FK_AuditEvents_Users_UserId FOREIGN KEY (UserId) REFERENCES Users (Id) ON DELETE SET NULL
);

CREATE INDEX IX_AuditEvents_UserId ON AuditEvents (UserId);
```

---

## 2. Stored Procedures — Users (admin CRUD)

Usados por `UsersController` (`AdminOnly`) para CRUD administrativo. El flujo real de login usa `usp_Users_UpsertFromOAuth` (sección 8); además, en este mismo bloque se documenta el contrato de autenticación local y el enlace de contraseña para cuentas OAuth.

```sql
CREATE OR ALTER PROCEDURE usp_Users_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, AvatarUrl, Provider, ProviderUserId, Role,
           CASE WHEN PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasLocalPassword,
           CASE WHEN TemporaryPasswordHash IS NOT NULL AND TemporaryPasswordExpires_at > SYSUTCDATETIME()
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PasswordChangeRequired,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Deleted_at IS NULL
    ORDER BY Id;
END

CREATE OR ALTER PROCEDURE usp_Users_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, AvatarUrl, Provider, ProviderUserId, Role,
           CASE WHEN PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasLocalPassword,
           CASE WHEN TemporaryPasswordHash IS NOT NULL AND TemporaryPasswordExpires_at > SYSUTCDATETIME()
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PasswordChangeRequired,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Id = @Id AND Deleted_at IS NULL;
END

CREATE OR ALTER PROCEDURE usp_Users_Create
    @Name NVARCHAR(200),
    @Email NVARCHAR(320),
    @AvatarUrl NVARCHAR(2048) = NULL,
    @Provider NVARCHAR(50),
    @ProviderUserId NVARCHAR(200),
    @LastLogin DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM Users WHERE Provider = @Provider AND ProviderUserId = @ProviderUserId)
    BEGIN
        RETURN;
    END

    DECLARE @NewId INT;

    INSERT INTO Users (Name, Email, AvatarUrl, Provider, ProviderUserId, Role, Created_at, LastLogin)
    VALUES (@Name, @Email, @AvatarUrl, @Provider, @ProviderUserId, 'User', SYSUTCDATETIME(), @LastLogin);

    SET @NewId = SCOPE_IDENTITY();

    EXEC usp_Users_GetById @Id = @NewId;
END

CREATE OR ALTER PROCEDURE usp_Users_Update
    @Id INT,
    @Name NVARCHAR(200),
    @Email NVARCHAR(320),
    @AvatarUrl NVARCHAR(2048) = NULL,
    @Provider NVARCHAR(50),
    @ProviderUserId NVARCHAR(200),
    @LastLogin DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Users
    SET Name = @Name,
        Email = @Email,
        AvatarUrl = @AvatarUrl,
        Provider = @Provider,
        ProviderUserId = @ProviderUserId,
        LastLogin = @LastLogin,
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC usp_Users_GetById @Id = @Id;
END

CREATE OR ALTER PROCEDURE usp_Users_SoftDelete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Users
    SET Deleted_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;
END

```

## 3. Stored Procedures — autenticación local y password vinculado

Este bloque cubre tres cosas:

- alta de cuenta con contraseña local;
- enlace inicial de contraseña local para cuentas OAuth que sí habilitan password;
- flujo de recuperación con contraseña temporal y cambio final.

Reglas:

- las cuentas OAuth sin contraseña local siguen sin poder usar recuperar/cambiar contraseña;
- `PasswordChangeRequired` se deriva de una contraseña temporal activa;
- `usp_Users_SetTemporaryPassword` solo funciona si la cuenta ya tiene contraseña local;
- `usp_Users_ChangeLocalPassword` limpia la contraseña temporal al cerrar el cambio.
- el backend genera la contraseña temporal y la envía por N8N; la base solo persiste el hash y la expiración;
- el SP no valida la contraseña actual: esa verificación ocurre en el backend antes de llamar al cambio.

Contrato funcional expuesto por el backend:

- `POST /api/auth/forgot-password`:
  - busca la cuenta por email;
  - rechaza la operación si no tiene contraseña local;
  - genera una contraseña temporal segura;
  - guarda hash + expiración;
  - la envía por N8N.
- `POST /api/auth/change-password`:
  - requiere sesión autenticada;
  - valida la contraseña actual contra la local o la temporal vigente;
  - actualiza la contraseña definitiva;
  - limpia la temporal.
- `POST /api/auth/local-password`:
  - habilita contraseña local en una cuenta que todavía no la tiene.

```sql
CREATE OR ALTER PROCEDURE usp_Users_RegisterWithPassword
    @Name NVARCHAR(200),
    @Email NVARCHAR(320),
    @PasswordHash NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM Users WHERE Email = @Email AND Deleted_at IS NULL)
    BEGIN
        RETURN;
    END

    DECLARE @NewId INT;

    INSERT INTO Users
        (Name, Email, PasswordHash, PasswordUpdated_at, TemporaryPasswordHash, TemporaryPasswordExpires_at, AvatarUrl, Provider, ProviderUserId, Role, Created_at, LastLogin)
    VALUES
        (@Name, @Email, @PasswordHash, SYSUTCDATETIME(), NULL, NULL, NULL, 'Password', @Email, 'User', SYSUTCDATETIME(), SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();

    EXEC usp_Users_GetById @Id = @NewId;
END

CREATE OR ALTER PROCEDURE usp_Users_GetByEmailForLogin
    @Email NVARCHAR(320)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, PasswordHash, TemporaryPasswordHash,
           AvatarUrl, Provider, ProviderUserId, Role,
           CASE WHEN PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasLocalPassword,
           CASE WHEN TemporaryPasswordHash IS NOT NULL AND TemporaryPasswordExpires_at > SYSUTCDATETIME()
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PasswordChangeRequired,
           TemporaryPasswordExpires_at AS TemporaryPasswordExpiresAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Email = @Email
      AND Deleted_at IS NULL;
END

CREATE OR ALTER PROCEDURE usp_Users_GetPasswordStateById
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, PasswordHash, TemporaryPasswordHash,
           AvatarUrl, Provider, ProviderUserId, Role,
           CASE WHEN PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasLocalPassword,
           CASE WHEN TemporaryPasswordHash IS NOT NULL AND TemporaryPasswordExpires_at > SYSUTCDATETIME()
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PasswordChangeRequired,
           TemporaryPasswordExpires_at AS TemporaryPasswordExpiresAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Id = @UserId
      AND Deleted_at IS NULL;
END

CREATE OR ALTER PROCEDURE usp_Users_SetLocalPassword
    @UserId INT,
    @PasswordHash NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Updated TABLE
    (
        Id INT NOT NULL
    );

    BEGIN TRY
        BEGIN TRAN;

        UPDATE Users
        SET PasswordHash = @PasswordHash,
            PasswordUpdated_at = SYSUTCDATETIME(),
            TemporaryPasswordHash = NULL,
            TemporaryPasswordExpires_at = NULL,
            Updated_at = SYSUTCDATETIME()
        OUTPUT inserted.Id INTO @Updated (Id)
        WHERE Id = @UserId
          AND Deleted_at IS NULL
          AND PasswordHash IS NULL;

        IF NOT EXISTS (SELECT 1 FROM @Updated)
        BEGIN
            ROLLBACK TRAN;
            RETURN;
        END

        INSERT INTO AuditEvents (UserId, EventType, Description, Created_at)
        VALUES (@UserId, 'LocalPasswordLinked', 'User enabled a local password for this account.', SYSUTCDATETIME());

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRAN;
        THROW;
    END CATCH

    EXEC usp_Users_GetById @Id = @UserId;
END

CREATE OR ALTER PROCEDURE usp_Users_SetTemporaryPassword
    @UserId INT,
    @TemporaryPasswordHash NVARCHAR(255),
    @TemporaryPasswordExpiresAt DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Updated TABLE
    (
        Id INT NOT NULL
    );

    BEGIN TRY
        BEGIN TRAN;

        UPDATE Users
        SET TemporaryPasswordHash = @TemporaryPasswordHash,
            TemporaryPasswordExpires_at = @TemporaryPasswordExpiresAt,
            Updated_at = SYSUTCDATETIME()
        OUTPUT inserted.Id INTO @Updated (Id)
        WHERE Id = @UserId
          AND Deleted_at IS NULL
          AND PasswordHash IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM @Updated)
        BEGIN
            ROLLBACK TRAN;
            RETURN;
        END

        INSERT INTO AuditEvents (UserId, EventType, Description, Created_at)
        VALUES (@UserId, 'TemporaryPasswordRequested', 'User requested a temporary password.', SYSUTCDATETIME());

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRAN;
        THROW;
    END CATCH

    EXEC usp_Users_GetById @Id = @UserId;
END

CREATE OR ALTER PROCEDURE usp_Users_ChangeLocalPassword
    @UserId INT,
    @PasswordHash NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Updated TABLE
    (
        Id INT NOT NULL
    );

    BEGIN TRY
        BEGIN TRAN;

        UPDATE Users
        SET PasswordHash = @PasswordHash,
            PasswordUpdated_at = SYSUTCDATETIME(),
            TemporaryPasswordHash = NULL,
            TemporaryPasswordExpires_at = NULL,
            Updated_at = SYSUTCDATETIME()
        OUTPUT inserted.Id INTO @Updated (Id)
        WHERE Id = @UserId
          AND Deleted_at IS NULL
          AND PasswordHash IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM @Updated)
        BEGIN
            ROLLBACK TRAN;
            RETURN;
        END

        INSERT INTO AuditEvents (UserId, EventType, Description, Created_at)
        VALUES (@UserId, 'PasswordChanged', 'User changed their local password.', SYSUTCDATETIME());

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRAN;
        THROW;
    END CATCH

    EXEC usp_Users_GetById @Id = @UserId;
END

CREATE OR ALTER PROCEDURE usp_Users_ResetPasswordDirect
    @UserId INT,
    @PasswordHash NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Updated TABLE
    (
        Id INT NOT NULL
    );

    BEGIN TRY
        BEGIN TRAN;

        UPDATE Users
        SET PasswordHash = @PasswordHash,
            PasswordUpdated_at = SYSUTCDATETIME(),
            TemporaryPasswordHash = NULL,
            TemporaryPasswordExpires_at = NULL,
            Updated_at = SYSUTCDATETIME()
        OUTPUT inserted.Id INTO @Updated (Id)
        WHERE Id = @UserId
          AND Deleted_at IS NULL
          AND PasswordHash IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM @Updated)
        BEGIN
            ROLLBACK TRAN;
            RETURN;
        END

        INSERT INTO AuditEvents (UserId, EventType, Description, Created_at)
        VALUES (@UserId, 'PasswordResetRequested', 'User requested password reset and a new permanent password was assigned.', SYSUTCDATETIME());

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRAN;
        THROW;
    END CATCH

    EXEC usp_Users_GetById @Id = @UserId;
END
```

---

## 4. Stored Procedures — DatabaseInstances (admin CRUD)

```sql
CREATE OR ALTER PROCEDURE usp_DatabaseInstances_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Host, Port, DatabaseName, DatabaseUser, Engine, Status,
           UsedSpaceBytes, MaxSpaceBytes, LastActivity,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt
    FROM DatabaseInstances
    WHERE Deleted_at IS NULL
    ORDER BY Id;
END

CREATE OR ALTER PROCEDURE usp_DatabaseInstances_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Host, Port, DatabaseName, DatabaseUser, Engine, Status,
           UsedSpaceBytes, MaxSpaceBytes, LastActivity,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt
    FROM DatabaseInstances
    WHERE Id = @Id AND Deleted_at IS NULL;
END

CREATE OR ALTER PROCEDURE usp_DatabaseInstances_Create
    @UserId INT,
    @Host NVARCHAR(255),
    @Port INT,
    @DatabaseName NVARCHAR(128),
    @DatabaseUser NVARCHAR(128),
    @Engine NVARCHAR(50),
    @Status NVARCHAR(50) = 'Active',
    @UsedSpaceBytes BIGINT = 0,
    @MaxSpaceBytes BIGINT = 20971520,
    @LastActivity DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NewId INT;

    INSERT INTO DatabaseInstances
        (UserId, Host, Port, DatabaseName, DatabaseUser, Engine, Status,
         UsedSpaceBytes, MaxSpaceBytes, LastActivity, Created_at)
    VALUES
        (@UserId, @Host, @Port, @DatabaseName, @DatabaseUser, @Engine, @Status,
         @UsedSpaceBytes, @MaxSpaceBytes, @LastActivity, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();

    EXEC usp_DatabaseInstances_GetById @Id = @NewId;
END

CREATE OR ALTER PROCEDURE usp_DatabaseInstances_Update
    @Id INT,
    @UserId INT,
    @Host NVARCHAR(255),
    @Port INT,
    @DatabaseName NVARCHAR(128),
    @DatabaseUser NVARCHAR(128),
    @Engine NVARCHAR(50),
    @Status NVARCHAR(50),
    @UsedSpaceBytes BIGINT,
    @MaxSpaceBytes BIGINT,
    @LastActivity DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE DatabaseInstances
    SET UserId = @UserId,
        Host = @Host,
        Port = @Port,
        DatabaseName = @DatabaseName,
        DatabaseUser = @DatabaseUser,
        Engine = @Engine,
        Status = @Status,
        UsedSpaceBytes = @UsedSpaceBytes,
        MaxSpaceBytes = @MaxSpaceBytes,
        LastActivity = @LastActivity,
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC usp_DatabaseInstances_GetById @Id = @Id;
END

CREATE OR ALTER PROCEDURE usp_DatabaseInstances_SoftDelete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE DatabaseInstances
    SET Deleted_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;

    UPDATE AccessCredentials
    SET Deleted_at = SYSUTCDATETIME()
    WHERE DatabaseInstanceId = @Id AND Deleted_at IS NULL;
END
```

---

## 5. Stored Procedures — AccessCredentials (admin CRUD)

Nunca devuelven `EncryptedPassword`. Leer la contraseña descifrada es un camino aparte, con verificación de dueño (sección 8).

```sql
CREATE OR ALTER PROCEDURE usp_AccessCredentials_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, DatabaseInstanceId,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt
    FROM AccessCredentials
    WHERE Deleted_at IS NULL
    ORDER BY Id;
END

CREATE OR ALTER PROCEDURE usp_AccessCredentials_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, DatabaseInstanceId,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt
    FROM AccessCredentials
    WHERE Id = @Id AND Deleted_at IS NULL;
END

CREATE OR ALTER PROCEDURE usp_AccessCredentials_GetByDatabaseInstanceId
    @DatabaseInstanceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, DatabaseInstanceId,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt
    FROM AccessCredentials
    WHERE DatabaseInstanceId = @DatabaseInstanceId AND Deleted_at IS NULL;
END

CREATE OR ALTER PROCEDURE usp_AccessCredentials_Create
    @DatabaseInstanceId INT,
    @EncryptedPassword NVARCHAR(1024)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NewId INT;

    INSERT INTO AccessCredentials (DatabaseInstanceId, EncryptedPassword, Created_at)
    VALUES (@DatabaseInstanceId, @EncryptedPassword, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();

    EXEC usp_AccessCredentials_GetById @Id = @NewId;
END

CREATE OR ALTER PROCEDURE usp_AccessCredentials_Update
    @Id INT,
    @EncryptedPassword NVARCHAR(1024)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE AccessCredentials
    SET EncryptedPassword = @EncryptedPassword,
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC usp_AccessCredentials_GetById @Id = @Id;
END

CREATE OR ALTER PROCEDURE usp_AccessCredentials_SoftDelete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE AccessCredentials
    SET Deleted_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;
END
```

---

## 6. Stored Procedures — AuditEvents

```sql
CREATE OR ALTER PROCEDURE usp_AuditEvents_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, EventType, Description, IpAddress, AdditionalData,
           Created_at AS CreatedAt, Deleted_at AS DeletedAt
    FROM AuditEvents
    WHERE Deleted_at IS NULL
    ORDER BY Id DESC;
END

CREATE OR ALTER PROCEDURE usp_AuditEvents_GetById
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, EventType, Description, IpAddress, AdditionalData,
           Created_at AS CreatedAt, Deleted_at AS DeletedAt
    FROM AuditEvents
    WHERE Id = @Id AND Deleted_at IS NULL;
END

CREATE OR ALTER PROCEDURE usp_AuditEvents_Create
    @UserId INT = NULL,
    @EventType NVARCHAR(100),
    @Description NVARCHAR(2000),
    @IpAddress NVARCHAR(45) = NULL,
    @AdditionalData NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NewId BIGINT;

    INSERT INTO AuditEvents (UserId, EventType, Description, IpAddress, AdditionalData, Created_at)
    VALUES (@UserId, @EventType, @Description, @IpAddress, @AdditionalData, SYSUTCDATETIME());

    SET @NewId = SCOPE_IDENTITY();

    EXEC usp_AuditEvents_GetById @Id = @NewId;
END

CREATE OR ALTER PROCEDURE usp_AuditEvents_Update
    @Id BIGINT,
    @UserId INT = NULL,
    @EventType NVARCHAR(100),
    @Description NVARCHAR(2000),
    @IpAddress NVARCHAR(45) = NULL,
    @AdditionalData NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE AuditEvents
    SET UserId = @UserId,
        EventType = @EventType,
        Description = @Description,
        IpAddress = @IpAddress,
        AdditionalData = @AdditionalData
    WHERE Id = @Id AND Deleted_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC usp_AuditEvents_GetById @Id = @Id;
END

CREATE OR ALTER PROCEDURE usp_AuditEvents_SoftDelete
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE AuditEvents
    SET Deleted_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;
END
```

---

## 7. Views y métricas (landing page + dashboard)

`ServiceAvailability` queda fija en `100.0` hasta que se integre monitoreo real — es una limitación conocida, no un bug.

```sql
CREATE OR ALTER VIEW vw_PlatformMetrics
AS
SELECT
    (SELECT COUNT(*) FROM Users WHERE Deleted_at IS NULL) AS TotalUsers,
    (SELECT COUNT(*) FROM DatabaseInstances WHERE Deleted_at IS NULL) AS TotalDatabases,
    (SELECT COUNT(*) FROM DatabaseInstances WHERE Deleted_at IS NULL AND Status = 'Active') AS ActiveDatabases,
    (SELECT COUNT(*) FROM AuditEvents WHERE Deleted_at IS NULL AND EventType = 'Login') AS TotalLogins,
    (SELECT COUNT(*) FROM Users WHERE Deleted_at IS NULL AND LastLogin >= DATEADD(DAY, -30, SYSUTCDATETIME())) AS ActiveUsers,
    CAST(100.0 AS DECIMAL(5, 2)) AS ServiceAvailability;

CREATE OR ALTER PROCEDURE usp_PlatformMetrics_Get
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TotalUsers, TotalDatabases, ActiveDatabases, TotalLogins, ActiveUsers, ServiceAvailability
    FROM vw_PlatformMetrics;
END

CREATE OR ALTER VIEW vw_UserDashboard
AS
SELECT
    di.Id AS DatabaseInstanceId,
    di.UserId,
    di.Host,
    di.Port,
    di.DatabaseName,
    di.DatabaseUser,
    di.Engine,
    di.Status,
    di.UsedSpaceBytes,
    di.MaxSpaceBytes,
    di.LastActivity,
    di.Created_at AS CreatedAt
FROM DatabaseInstances di
WHERE di.Deleted_at IS NULL;

CREATE OR ALTER PROCEDURE usp_UserDashboard_GetByUserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT DatabaseInstanceId, Host, Port, DatabaseName, DatabaseUser, Engine, Status,
           UsedSpaceBytes, MaxSpaceBytes, LastActivity, CreatedAt
    FROM vw_UserDashboard
    WHERE UserId = @UserId
    ORDER BY DatabaseInstanceId;
END
```

---

## 8. Autenticación y aprovisionamiento (el flujo real de negocio)

`usp_Users_UpsertFromOAuth` es el único punto de entrada del login: decide crear o actualizar el usuario, evita duplicados por `(Provider, ProviderUserId)`, y registra el audit event de login — todo dentro de la misma transacción. `Role` nunca se toca en un usuario existente, para que no se pueda escalar privilegios vía claims de OAuth.

```sql
CREATE OR ALTER PROCEDURE usp_Users_UpsertFromOAuth
    @Provider NVARCHAR(50),
    @ProviderUserId NVARCHAR(200),
    @Name NVARCHAR(200),
    @Email NVARCHAR(320),
    @AvatarUrl NVARCHAR(2048) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @MergeOutput TABLE (Action NVARCHAR(10) NOT NULL, Id INT NOT NULL);
    DECLARE @UserId INT;
    DECLARE @IsNewUser BIT;

    BEGIN TRY
        BEGIN TRAN;

        MERGE INTO Users WITH (HOLDLOCK) AS target
        USING (SELECT @Provider AS Provider, @ProviderUserId AS ProviderUserId) AS source
            ON target.Provider = source.Provider AND target.ProviderUserId = source.ProviderUserId
        WHEN MATCHED THEN
            UPDATE SET
                Name = @Name,
                Email = @Email,
                AvatarUrl = @AvatarUrl,
                LastLogin = SYSUTCDATETIME(),
                Updated_at = SYSUTCDATETIME(),
                Deleted_at = NULL
                -- Role nunca se toca aquí.
        WHEN NOT MATCHED THEN
            INSERT (Name, Email, AvatarUrl, Provider, ProviderUserId, Role, Created_at, LastLogin)
            VALUES (@Name, @Email, @AvatarUrl, @Provider, @ProviderUserId, 'User', SYSUTCDATETIME(), SYSUTCDATETIME())
        OUTPUT $action, inserted.Id INTO @MergeOutput (Action, Id);

        SELECT TOP 1 @UserId = Id FROM @MergeOutput;
        SET @IsNewUser = CASE WHEN EXISTS (SELECT 1 FROM @MergeOutput WHERE Action = 'INSERT') THEN 1 ELSE 0 END;

        INSERT INTO AuditEvents (UserId, EventType, Description, AdditionalData, Created_at)
        VALUES (
            @UserId,
            'Login',
            CONCAT('OAuth login completed with ', @Provider, '.'),
            (SELECT @Provider AS provider, @ProviderUserId AS providerUserId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
            SYSUTCDATETIME()
        );

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRAN;
        THROW;
    END CATCH

    SELECT Id, Name, Email, AvatarUrl, Provider, ProviderUserId, Role,
           CASE WHEN PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasLocalPassword,
           CASE WHEN TemporaryPasswordHash IS NOT NULL AND TemporaryPasswordExpires_at > SYSUTCDATETIME()
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PasswordChangeRequired,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin,
           @IsNewUser AS IsNewUser
    FROM Users
    WHERE Id = @UserId;
END

CREATE OR ALTER PROCEDURE usp_Users_GetSharedSqlServerProvisioningState
    @UserId INT,
    @Engine NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SharedLoginName = CONCAT('raft_u', @UserId),
        HasExistingDatabases = CASE WHEN EXISTS (
            SELECT 1
            FROM DatabaseInstances di
            WHERE di.UserId = @UserId
              AND (@Engine IS NULL OR di.Engine = @Engine)
              AND di.Deleted_at IS NULL
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END,
        EncryptedPassword = (
            SELECT TOP 1 ac.EncryptedPassword
            FROM AccessCredentials ac
            INNER JOIN DatabaseInstances di ON di.Id = ac.DatabaseInstanceId
            WHERE di.UserId = @UserId
              AND (@Engine IS NULL OR di.Engine = @Engine)
              AND di.Deleted_at IS NULL
              AND ac.Deleted_at IS NULL
            ORDER BY di.Id
        );
END

-- Solo devuelve la fila si @DatabaseInstanceId realmente pertenece a @UserId.
-- La verificación de dueño vive aquí, no en C#.
CREATE OR ALTER PROCEDURE usp_AccessCredentials_GetDecryptableByOwner
    @UserId INT,
    @DatabaseInstanceId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT ac.Id, ac.DatabaseInstanceId, ac.EncryptedPassword
    FROM AccessCredentials ac
    INNER JOIN DatabaseInstances di ON di.Id = ac.DatabaseInstanceId
    WHERE ac.DatabaseInstanceId = @DatabaseInstanceId
      AND di.UserId = @UserId
      AND ac.Deleted_at IS NULL
      AND di.Deleted_at IS NULL;
END
```

---

## 9. Ciclo de vida (TTL y cuota de almacenamiento)

Usados por el `BackgroundService` del backend (pausa a los 7 días de inactividad, elimina a los 30, recalcula espacio usado). La decisión de "quién" está vencido vive aquí; el backend solo ejecuta la acción mecánica en MySQL y llama a estos SPs para reflejarla.

```sql
CREATE OR ALTER PROCEDURE usp_DatabaseInstances_UpdateStatus
    @Id INT,
    @Status NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE DatabaseInstances
    SET Status = @Status,
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;
END

-- Cae en Created_at cuando la instancia nunca tuvo actividad, para que también expire.
CREATE OR ALTER PROCEDURE usp_DatabaseInstances_GetDueForPause
    @InactivityDays INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM DatabaseInstances
    WHERE Deleted_at IS NULL
      AND Status = 'Active'
      AND COALESCE(LastActivity, Created_at) <= DATEADD(DAY, -@InactivityDays, SYSUTCDATETIME());
END

-- Cubre 'Active' y 'Suspended': si el job se cayó y se saltó la pausa, igual elimina al vencer.
CREATE OR ALTER PROCEDURE usp_DatabaseInstances_GetDueForDelete
    @InactivityDays INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id
    FROM DatabaseInstances
    WHERE Deleted_at IS NULL
      AND Status IN ('Active', 'Suspended')
      AND COALESCE(LastActivity, Created_at) <= DATEADD(DAY, -@InactivityDays, SYSUTCDATETIME());
END

CREATE OR ALTER PROCEDURE usp_DatabaseInstances_GetSharedLoginCleanupState
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SharedLoginName = CONCAT('raft_u', @UserId),
        CanDropLogin = CASE WHEN EXISTS (
            SELECT 1
            FROM DatabaseInstances di
            WHERE di.UserId = @UserId
              AND di.Deleted_at IS NULL
        ) THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END;
END

CREATE OR ALTER PROCEDURE usp_DatabaseInstances_UpdateUsedSpace
    @Id INT,
    @UsedSpaceBytes BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE DatabaseInstances
    SET UsedSpaceBytes = @UsedSpaceBytes,
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;
END

-- DatabaseUser ahora es el login compartido del usuario de plataforma.
-- Para conservar TTL por instancia, el job mapea la base de datos activa de vuelta a
-- su fila en DatabaseInstances.
CREATE OR ALTER PROCEDURE usp_DatabaseInstances_TouchActivityByDatabaseName
    @DatabaseName NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE DatabaseInstances
    SET LastActivity = SYSUTCDATETIME()
    WHERE DatabaseName = @DatabaseName AND Deleted_at IS NULL;
END
```

---

## 10. IA — API Keys y consumo

### 10.1 Tabla

```sql
CREATE TABLE dbo.AiApiKeys (
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    Name NVARCHAR(120) NOT NULL,
    KeyPrefix NVARCHAR(12) NOT NULL,
    KeyHash NVARCHAR(128) NOT NULL,
    Status NVARCHAR(20) NOT NULL CONSTRAINT DF_AiApiKeys_Status DEFAULT ('Active'),
    Created_at DATETIME2 NOT NULL CONSTRAINT DF_AiApiKeys_Created_at DEFAULT (SYSUTCDATETIME()),
    Updated_at DATETIME2 NULL,
    Revoked_at DATETIME2 NULL,
    LastUsedAt DATETIME2 NULL,
    TotalRequests BIGINT NOT NULL CONSTRAINT DF_AiApiKeys_TotalRequests DEFAULT (0),
    TotalPromptTokens BIGINT NOT NULL CONSTRAINT DF_AiApiKeys_TotalPromptTokens DEFAULT (0),
    TotalCompletionTokens BIGINT NOT NULL CONSTRAINT DF_AiApiKeys_TotalCompletionTokens DEFAULT (0),
    TotalTokens BIGINT NOT NULL CONSTRAINT DF_AiApiKeys_TotalTokens DEFAULT (0),
    ApproxCostUsd DECIMAL(18,6) NOT NULL CONSTRAINT DF_AiApiKeys_ApproxCostUsd DEFAULT (0),
    CONSTRAINT PK_AiApiKeys PRIMARY KEY (Id),
    CONSTRAINT FK_AiApiKeys_Users_UserId FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
);

CREATE UNIQUE INDEX IX_AiApiKeys_KeyHash ON dbo.AiApiKeys (KeyHash);

CREATE INDEX IX_AiApiKeys_UserId ON dbo.AiApiKeys (UserId);

CREATE INDEX IX_AiApiKeys_UserId_Status ON dbo.AiApiKeys (UserId, Status);
```

### 10.2 Stored Procedures

#### 10.2.1 `usp_AiApiKeys_GetAllByUserId`

```sql
CREATE PROCEDURE dbo.usp_AiApiKeys_GetAllByUserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Name, KeyPrefix, Status,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Revoked_at AS RevokedAt, LastUsedAt,
           TotalRequests, TotalPromptTokens, TotalCompletionTokens, TotalTokens, ApproxCostUsd
    FROM dbo.AiApiKeys
    WHERE UserId = @UserId
    ORDER BY Id DESC;
END
```

#### 10.2.2 `usp_AiApiKeys_GetById`

```sql
CREATE PROCEDURE dbo.usp_AiApiKeys_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Name, KeyPrefix, Status,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Revoked_at AS RevokedAt, LastUsedAt,
           TotalRequests, TotalPromptTokens, TotalCompletionTokens, TotalTokens, ApproxCostUsd
    FROM dbo.AiApiKeys
    WHERE Id = @Id;
END
```

#### 10.2.3 `usp_AiApiKeys_GetByIdAndUserId`

```sql
CREATE PROCEDURE dbo.usp_AiApiKeys_GetByIdAndUserId
    @Id INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Name, KeyPrefix, Status,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Revoked_at AS RevokedAt, LastUsedAt,
           TotalRequests, TotalPromptTokens, TotalCompletionTokens, TotalTokens, ApproxCostUsd
    FROM dbo.AiApiKeys
    WHERE Id = @Id
      AND UserId = @UserId;
END
```

#### 10.2.4 `usp_AiApiKeys_GetActiveByKeyHash`

```sql
CREATE PROCEDURE dbo.usp_AiApiKeys_GetActiveByKeyHash
    @KeyHash NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Name, KeyPrefix, Status,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Revoked_at AS RevokedAt, LastUsedAt,
           TotalRequests, TotalPromptTokens, TotalCompletionTokens, TotalTokens, ApproxCostUsd
    FROM dbo.AiApiKeys
    WHERE KeyHash = @KeyHash
      AND Status = 'Active'
      AND Revoked_at IS NULL;
END
```

#### 10.2.5 `usp_AiApiKeys_Create`

```sql
CREATE PROCEDURE dbo.usp_AiApiKeys_Create
    @UserId INT,
    @Name NVARCHAR(120),
    @KeyPrefix NVARCHAR(12),
    @KeyHash NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM dbo.AiApiKeys WHERE KeyHash = @KeyHash)
    BEGIN
        RETURN;
    END

    INSERT INTO dbo.AiApiKeys (UserId, Name, KeyPrefix, KeyHash, Status, Created_at)
    VALUES (@UserId, @Name, @KeyPrefix, @KeyHash, 'Active', SYSUTCDATETIME());

    DECLARE @NewId INT = SCOPE_IDENTITY();
    EXEC dbo.usp_AiApiKeys_GetById @Id = @NewId;
END
```

#### 10.2.6 `usp_AiApiKeys_Rotate`

```sql
CREATE PROCEDURE dbo.usp_AiApiKeys_Rotate
    @Id INT,
    @UserId INT,
    @KeyPrefix NVARCHAR(12),
    @KeyHash NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AiApiKeys
    SET KeyPrefix = @KeyPrefix,
        KeyHash = @KeyHash,
        Status = 'Active',
        Revoked_at = NULL,
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id
      AND UserId = @UserId
      AND Revoked_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC dbo.usp_AiApiKeys_GetById @Id = @Id;
END
```

#### 10.2.7 `usp_AiApiKeys_Revoke`

```sql
CREATE PROCEDURE dbo.usp_AiApiKeys_Revoke
    @Id INT,
    @UserId INT
AS
BEGIN
    -- ⚠️ IMPORTANTE: NO usar SET NOCOUNT ON aquí.
    -- AiApiKeyService.RevokeAsync usa ExecuteNonQueryAsync (ADO.NET) para determinar
    -- si la clave existía y fue afectada (rows > 0 → éxito, rows = 0 → 404 Not Found).
    -- SET NOCOUNT ON suprime el mensaje "N row(s) affected" que ADO.NET necesita para
    -- retornar el conteo real; sin él, ExecuteNonQueryAsync siempre devuelve -1 o 0
    -- y el backend responde 404 aunque el UPDATE haya funcionado correctamente.

    UPDATE dbo.AiApiKeys
    SET Status = 'Revoked',
        Revoked_at = SYSUTCDATETIME(),
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id
      AND UserId = @UserId
      AND Revoked_at IS NULL;
END
```

#### 10.2.8 `usp_AiApiKeys_RecordUsage`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_AiApiKeys_RecordUsage
    @Id INT,
    @UserId INT = NULL,
    @Provider NVARCHAR(80) = 'default',
    @Model NVARCHAR(100) = 'default',
    @Endpoint NVARCHAR(100) = '/api/ai/generate',
    @Mode NVARCHAR(40) = NULL,
    @PromptTokens BIGINT = 0,
    @CompletionTokens BIGINT = 0,
    @ApproxCostUsd DECIMAL(18,6) = 0,
    @DurationMs INT = NULL,
    @StatusCode INT = 200
AS
BEGIN
    SET NOCOUNT ON;

    IF @UserId IS NULL OR @UserId <= 0
    BEGIN
        SELECT @UserId = UserId FROM dbo.AiApiKeys WHERE Id = @Id;
    END

    IF @UserId IS NULL
    BEGIN
        RETURN;
    END

    BEGIN TRANSACTION;

    BEGIN TRY
        INSERT INTO dbo.AiUsageLogs (
            AiApiKeyId, UserId, Provider, Model, Endpoint, Mode,
            PromptTokens, CompletionTokens, TotalTokens, ApproxCostUsd,
            DurationMs, StatusCode, Created_at
        )
        VALUES (
            @Id, @UserId, ISNULL(@Provider, 'default'), ISNULL(@Model, 'default'),
            ISNULL(@Endpoint, '/api/ai/generate'), @Mode, ISNULL(@PromptTokens, 0),
            ISNULL(@CompletionTokens, 0), ISNULL(@PromptTokens, 0) + ISNULL(@CompletionTokens, 0),
            ISNULL(@ApproxCostUsd, 0), @DurationMs, ISNULL(@StatusCode, 200), SYSUTCDATETIME()
        );

        UPDATE dbo.AiApiKeys
        SET TotalRequests = TotalRequests + 1,
            TotalPromptTokens = TotalPromptTokens + ISNULL(@PromptTokens, 0),
            TotalCompletionTokens = TotalCompletionTokens + ISNULL(@CompletionTokens, 0),
            TotalTokens = TotalTokens + (ISNULL(@PromptTokens, 0) + ISNULL(@CompletionTokens, 0)),
            ApproxCostUsd = ApproxCostUsd + ISNULL(@ApproxCostUsd, 0),
            LastUsedAt = SYSUTCDATETIME(),
            Updated_at = SYSUTCDATETIME()
        WHERE Id = @Id
          AND Revoked_at IS NULL;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
```

---

### 10.3 `dbo.AiUsageLogs` (Registro Histórico Inmutable por Evento / Telemetría)

#### 10.3.1 Estructura de la Tabla `dbo.AiUsageLogs`

```sql
CREATE TABLE dbo.AiUsageLogs (
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    AiApiKeyId INT NOT NULL CONSTRAINT FK_AiUsageLogs_AiApiKeys REFERENCES dbo.AiApiKeys(Id),
    UserId INT NOT NULL CONSTRAINT FK_AiUsageLogs_Users REFERENCES dbo.Users(Id),
    Provider NVARCHAR(80) NOT NULL,
    Model NVARCHAR(100) NOT NULL,
    Endpoint NVARCHAR(100) NOT NULL,
    Mode NVARCHAR(40) NULL,
    PromptTokens BIGINT NOT NULL CONSTRAINT DF_AiUsageLogs_PromptTokens DEFAULT (0),
    CompletionTokens BIGINT NOT NULL CONSTRAINT DF_AiUsageLogs_CompletionTokens DEFAULT (0),
    TotalTokens BIGINT NOT NULL CONSTRAINT DF_AiUsageLogs_TotalTokens DEFAULT (0),
    ApproxCostUsd DECIMAL(18,6) NOT NULL CONSTRAINT DF_AiUsageLogs_ApproxCostUsd DEFAULT (0),
    DurationMs INT NULL,
    StatusCode INT NOT NULL CONSTRAINT DF_AiUsageLogs_StatusCode DEFAULT (200),
    Created_at DATETIME2 NOT NULL CONSTRAINT DF_AiUsageLogs_Created_at DEFAULT (SYSUTCDATETIME())
);

CREATE INDEX IX_AiUsageLogs_UserId_CreatedAt ON dbo.AiUsageLogs(UserId, Created_at DESC);
CREATE INDEX IX_AiUsageLogs_AiApiKeyId_CreatedAt ON dbo.AiUsageLogs(AiApiKeyId, Created_at DESC);
CREATE INDEX IX_AiUsageLogs_CreatedAt ON dbo.AiUsageLogs(Created_at DESC);
```

#### 10.3.2 `usp_AiUsageLogs_GetHistoryByUserId`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_AiUsageLogs_GetHistoryByUserId
    @UserId INT,
    @AiApiKeyId INT = NULL,
    @FromDate DATETIME2 = NULL,
    @ToDate DATETIME2 = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    IF @PageNumber < 1 SET @PageNumber = 1;
    IF @PageSize < 1 SET @PageSize = 50;
    IF @PageSize > 500 SET @PageSize = 500;

    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;

    SELECT 
        l.Id,
        l.AiApiKeyId,
        k.Name AS KeyName,
        k.KeyPrefix,
        l.UserId,
        l.Provider,
        l.Model,
        l.Endpoint,
        l.Mode,
        l.PromptTokens,
        l.CompletionTokens,
        l.TotalTokens,
        l.ApproxCostUsd,
        l.DurationMs,
        l.StatusCode,
        l.Created_at AS CreatedAt
    FROM dbo.AiUsageLogs l
    INNER JOIN dbo.AiApiKeys k ON l.AiApiKeyId = k.Id
    WHERE l.UserId = @UserId
      AND (@AiApiKeyId IS NULL OR l.AiApiKeyId = @AiApiKeyId)
      AND (@FromDate IS NULL OR l.Created_at >= @FromDate)
      AND (@ToDate IS NULL OR l.Created_at <= @ToDate)
    ORDER BY l.Created_at DESC
    OFFSET @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END
```

#### 10.3.3 `usp_AiUsageLogs_GetAnalyticsByUserId`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_AiUsageLogs_GetAnalyticsByUserId
    @UserId INT,
    @FromDate DATETIME2 = NULL,
    @ToDate DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @FromDate IS NULL
        SET @FromDate = DATEADD(DAY, -30, SYSUTCDATETIME());

    IF @ToDate IS NULL
        SET @ToDate = SYSUTCDATETIME();

    -- Resultset 1: Totales
    SELECT 
        COUNT(1) AS TotalEvents,
        ISNULL(SUM(PromptTokens), 0) AS TotalPromptTokens,
        ISNULL(SUM(CompletionTokens), 0) AS TotalCompletionTokens,
        ISNULL(SUM(TotalTokens), 0) AS TotalTokens,
        ISNULL(SUM(ApproxCostUsd), 0) AS TotalCostUsd,
        ISNULL(AVG(CAST(DurationMs AS FLOAT)), 0) AS AvgDurationMs
    FROM dbo.AiUsageLogs
    WHERE UserId = @UserId
      AND Created_at BETWEEN @FromDate AND @ToDate;

    -- Resultset 2: Serie de tiempo diaria
    SELECT 
        CAST(Created_at AS DATE) AS [Date],
        COUNT(1) AS RequestsCount,
        ISNULL(SUM(TotalTokens), 0) AS TotalTokens,
        ISNULL(SUM(ApproxCostUsd), 0) AS CostUsd
    FROM dbo.AiUsageLogs
    WHERE UserId = @UserId
      AND Created_at BETWEEN @FromDate AND @ToDate
    GROUP BY CAST(Created_at AS DATE)
    ORDER BY [Date] ASC;

    -- Resultset 3: Consumo por proveedor y modelo
    SELECT 
        Provider,
        Model,
        COUNT(1) AS RequestsCount,
        ISNULL(SUM(TotalTokens), 0) AS TotalTokens,
        ISNULL(SUM(ApproxCostUsd), 0) AS CostUsd
    FROM dbo.AiUsageLogs
    WHERE UserId = @UserId
      AND Created_at BETWEEN @FromDate AND @ToDate
    GROUP BY Provider, Model
    ORDER BY RequestsCount DESC;
END
```



---

## 11. DNS — Registros aprovisionados para usuarios

### 11.1 Tabla

```sql
CREATE TABLE dbo.DnsRecords (
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    Label NVARCHAR(63) NOT NULL,
    RecordName NVARCHAR(255) NOT NULL,
    Fqdn NVARCHAR(255) NOT NULL,
    RecordType NVARCHAR(20) NOT NULL CONSTRAINT DF_DnsRecords_RecordType DEFAULT ('A'),
    Content NVARCHAR(255) NOT NULL,
    RecordTtl INT NOT NULL CONSTRAINT DF_DnsRecords_RecordTtl DEFAULT (1),
    Proxied BIT NOT NULL CONSTRAINT DF_DnsRecords_Proxied DEFAULT (0),
    CloudflareZoneId NVARCHAR(128) NOT NULL,
    CloudflareRecordId NVARCHAR(128) NULL,
    Status NVARCHAR(20) NOT NULL CONSTRAINT DF_DnsRecords_Status DEFAULT ('Pending'),
    LastError NVARCHAR(4000) NULL,
    Created_at DATETIME2 NOT NULL CONSTRAINT DF_DnsRecords_Created_at DEFAULT (SYSUTCDATETIME()),
    Updated_at DATETIME2 NULL,
    Revoked_at DATETIME2 NULL,
    CONSTRAINT PK_DnsRecords PRIMARY KEY (Id),
    CONSTRAINT FK_DnsRecords_Users_UserId FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
);

CREATE UNIQUE INDEX IX_DnsRecords_Fqdn_Active
ON dbo.DnsRecords (Fqdn)
WHERE Revoked_at IS NULL AND Status IN ('Pending', 'Active');

CREATE INDEX IX_DnsRecords_UserId ON dbo.DnsRecords (UserId);

CREATE INDEX IX_DnsRecords_UserId_Status ON dbo.DnsRecords (UserId, Status);

CREATE INDEX IX_DnsRecords_Status ON dbo.DnsRecords (Status);
```

### 11.2 Stored Procedures

#### 11.2.1 `usp_DnsRecords_GetAllByUserId`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_DnsRecords_GetAllByUserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Label, RecordName, Fqdn, RecordType, Content,
           RecordTtl, Proxied, CloudflareZoneId, CloudflareRecordId, Status,
           LastError, Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Revoked_at AS RevokedAt
    FROM dbo.DnsRecords
    WHERE UserId = @UserId
    ORDER BY Id DESC;
END
```

#### 11.2.2 `usp_DnsRecords_GetAll`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_DnsRecords_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Label, RecordName, Fqdn, RecordType, Content,
           RecordTtl, Proxied, CloudflareZoneId, CloudflareRecordId, Status,
           LastError, Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Revoked_at AS RevokedAt
    FROM dbo.DnsRecords
    ORDER BY Id DESC;
END
```

#### 11.2.3 `usp_DnsRecords_GetById`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_DnsRecords_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Label, RecordName, Fqdn, RecordType, Content,
           RecordTtl, Proxied, CloudflareZoneId, CloudflareRecordId, Status,
           LastError, Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Revoked_at AS RevokedAt
    FROM dbo.DnsRecords
    WHERE Id = @Id;
END
```

#### 11.2.4 `usp_DnsRecords_GetByIdAndUserId`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_DnsRecords_GetByIdAndUserId
    @Id INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, Label, RecordName, Fqdn, RecordType, Content,
           RecordTtl, Proxied, CloudflareZoneId, CloudflareRecordId, Status,
           LastError, Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Revoked_at AS RevokedAt
    FROM dbo.DnsRecords
    WHERE Id = @Id
      AND UserId = @UserId;
END
```

#### 11.2.5 `usp_DnsRecords_GetActiveByUserIdAndFqdn`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_DnsRecords_GetActiveByUserIdAndFqdn
    @UserId INT,
    @Fqdn NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) Id, UserId, Label, RecordName, Fqdn, RecordType, Content,
           RecordTtl, Proxied, CloudflareZoneId, CloudflareRecordId, Status,
           LastError, Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Revoked_at AS RevokedAt
    FROM dbo.DnsRecords
    WHERE UserId = @UserId
      AND Fqdn = @Fqdn
      AND Status IN ('Pending', 'Active')
      AND Revoked_at IS NULL
    ORDER BY Id DESC;
END
```

#### 11.2.6 `usp_DnsRecords_Create`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_DnsRecords_Create
    @UserId INT,
    @Label NVARCHAR(63),
    @RecordName NVARCHAR(255),
    @Fqdn NVARCHAR(255),
    @RecordType NVARCHAR(20),
    @Content NVARCHAR(255),
    @RecordTtl INT,
    @Proxied BIT,
    @CloudflareZoneId NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM dbo.DnsRecords
        WHERE Fqdn = @Fqdn
          AND Status IN ('Pending', 'Active')
          AND Revoked_at IS NULL
    )
    BEGIN
        RETURN;
    END

    INSERT INTO dbo.DnsRecords
        (UserId, Label, RecordName, Fqdn, RecordType, Content, RecordTtl, Proxied,
         CloudflareZoneId, Status, Created_at)
    VALUES
        (@UserId, @Label, @RecordName, @Fqdn, @RecordType, @Content, @RecordTtl, @Proxied,
         @CloudflareZoneId, 'Pending', SYSUTCDATETIME());

    DECLARE @NewId INT = SCOPE_IDENTITY();
    EXEC dbo.usp_DnsRecords_GetById @Id = @NewId;
END
```

#### 11.2.7 `usp_DnsRecords_MarkProvisioned`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_DnsRecords_MarkProvisioned
    @Id INT,
    @CloudflareRecordId NVARCHAR(128),
    @CloudflareZoneId NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.DnsRecords
    SET CloudflareRecordId = @CloudflareRecordId,
        CloudflareZoneId = @CloudflareZoneId,
        Status = 'Active',
        Updated_at = SYSUTCDATETIME(),
        LastError = NULL
    WHERE Id = @Id
      AND Revoked_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC dbo.usp_DnsRecords_GetById @Id = @Id;
END
```

#### 11.2.8 `usp_DnsRecords_MarkFailed`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_DnsRecords_MarkFailed
    @Id INT,
    @LastError NVARCHAR(4000)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.DnsRecords
    SET Status = 'Failed',
        Updated_at = SYSUTCDATETIME(),
        LastError = @LastError
    WHERE Id = @Id
      AND Revoked_at IS NULL;
END
```

#### 11.2.9 `usp_DnsRecords_Revoke`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_DnsRecords_Revoke
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.DnsRecords
    SET Status = 'Revoked',
        Updated_at = SYSUTCDATETIME(),
        Revoked_at = SYSUTCDATETIME(),
        LastError = NULL
    WHERE Id = @Id
      AND Revoked_at IS NULL;
END
```

## 12. N8N — Cuentas aprovisionadas para usuarios

### 12.1 Tabla

```sql
CREATE TABLE dbo.N8nAccounts (
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    ExternalUserRef NVARCHAR(64) NOT NULL,
    Email NVARCHAR(320) NOT NULL,
    AccountId NVARCHAR(128) NULL,
    Status NVARCHAR(20) NOT NULL CONSTRAINT DF_N8nAccounts_Status DEFAULT ('Pending'),
    Credential NVARCHAR(2048) NULL,
    AccessType NVARCHAR(50) NULL,
    ActiveWorkflowsCount INT NOT NULL CONSTRAINT DF_N8nAccounts_ActiveWorkflows DEFAULT (0),
    TotalWorkflowsCount INT NOT NULL CONSTRAINT DF_N8nAccounts_TotalWorkflows DEFAULT (0),
    TotalExecutions BIGINT NOT NULL CONSTRAINT DF_N8nAccounts_TotalExecutions DEFAULT (0),
    SuccessfulExecutions BIGINT NOT NULL CONSTRAINT DF_N8nAccounts_SuccessExecutions DEFAULT (0),
    FailedExecutions BIGINT NOT NULL CONSTRAINT DF_N8nAccounts_FailedExecutions DEFAULT (0),
    MonthlyExecutions INT NOT NULL CONSTRAINT DF_N8nAccounts_MonthlyExecutions DEFAULT (0),
    MaxMonthlyExecutions INT NOT NULL CONSTRAINT DF_N8nAccounts_MaxMonthlyExecutions DEFAULT (1000),
    MonthlyResetDate DATETIME2 NULL,
    LastExecutionAt DATETIME2 NULL,
    Created_at DATETIME2 NOT NULL CONSTRAINT DF_N8nAccounts_Created_at DEFAULT (SYSUTCDATETIME()),
    Updated_at DATETIME2 NULL,
    Provisioned_at DATETIME2 NULL,
    Revoked_at DATETIME2 NULL,
    LastSyncedAt DATETIME2 NULL,
    LastErrorMessage NVARCHAR(4000) NULL,
    CONSTRAINT PK_N8nAccounts PRIMARY KEY (Id),
    CONSTRAINT FK_N8nAccounts_Users_UserId FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
);

CREATE UNIQUE INDEX IX_N8nAccounts_ExternalUserRef ON dbo.N8nAccounts (ExternalUserRef);

CREATE UNIQUE INDEX IX_N8nAccounts_UserId_Active
ON dbo.N8nAccounts (UserId)
WHERE Status IN ('Pending', 'Active');

CREATE INDEX IX_N8nAccounts_UserId ON dbo.N8nAccounts (UserId);

CREATE INDEX IX_N8nAccounts_Status ON dbo.N8nAccounts (Status);
```

### 12.2 Stored Procedures

#### 12.2.1 `usp_N8nAccounts_GetAllByUserId`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetAllByUserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE UserId = @UserId
    ORDER BY Id DESC;
END
```

#### 12.2.2 `usp_N8nAccounts_GetAll`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    ORDER BY Id DESC;
END
```

#### 12.2.3 `usp_N8nAccounts_GetById`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE Id = @Id;
END
```

#### 12.2.4 `usp_N8nAccounts_GetByExternalUserRef`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetByExternalUserRef
    @ExternalUserRef NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE ExternalUserRef = @ExternalUserRef;
END
```

#### 12.2.5 `usp_N8nAccounts_GetActiveByUserId`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetActiveByUserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE UserId = @UserId
      AND Status IN ('Pending', 'Active')
      AND Revoked_at IS NULL
    ORDER BY Id DESC;
END
```

#### 12.2.6 `usp_N8nAccounts_Create`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_Create
    @UserId INT,
    @ExternalUserRef NVARCHAR(64),
    @Email NVARCHAR(320),
    @AccountId NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM dbo.N8nAccounts
        WHERE ExternalUserRef = @ExternalUserRef
           OR (UserId = @UserId AND Status IN ('Pending', 'Active') AND Revoked_at IS NULL)
    )
    BEGIN
        RETURN;
    END

    INSERT INTO dbo.N8nAccounts
        (UserId, ExternalUserRef, Email, AccountId, Status, Created_at)
    VALUES
        (@UserId, @ExternalUserRef, @Email, @AccountId, 'Pending', SYSUTCDATETIME());

    DECLARE @NewId INT = SCOPE_IDENTITY();
    EXEC dbo.usp_N8nAccounts_GetById @Id = @NewId;
END
```

#### 12.2.7 `usp_N8nAccounts_MarkProvisioned`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_MarkProvisioned
    @Id INT,
    @UserId INT,
    @AccountId NVARCHAR(128),
    @Credential NVARCHAR(2048) = NULL,
    @AccessType NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.N8nAccounts
    SET AccountId = @AccountId,
        Credential = COALESCE(@Credential, Credential),
        AccessType = COALESCE(@AccessType, AccessType),
        Status = 'Active',
        Provisioned_at = SYSUTCDATETIME(),
        Updated_at = SYSUTCDATETIME(),
        LastSyncedAt = SYSUTCDATETIME(),
        LastErrorMessage = NULL
    WHERE Id = @Id
      AND UserId = @UserId
      AND Revoked_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC dbo.usp_N8nAccounts_GetById @Id = @Id;
END
```

#### 12.2.8 `usp_N8nAccounts_MarkFailed`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_MarkFailed
    @Id INT,
    @UserId INT,
    @LastErrorMessage NVARCHAR(4000)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.N8nAccounts
    SET Status = 'Failed',
        Updated_at = SYSUTCDATETIME(),
        LastSyncedAt = SYSUTCDATETIME(),
        LastErrorMessage = @LastErrorMessage
    WHERE Id = @Id
      AND UserId = @UserId
      AND Revoked_at IS NULL;
END
```

#### 12.2.9 `usp_N8nAccounts_Revoke`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_Revoke
    @Id INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.N8nAccounts
    SET Status = 'Revoked',
        Revoked_at = SYSUTCDATETIME(),
        Updated_at = SYSUTCDATETIME(),
        LastSyncedAt = SYSUTCDATETIME()
    WHERE Id = @Id
      AND UserId = @UserId
      AND Revoked_at IS NULL;
END
```

#### 12.2.10 `usp_N8nAccounts_UpdateMetrics`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_UpdateMetrics
    @Id INT,
    @ActiveWorkflowsCount INT = 0,
    @TotalWorkflowsCount INT = 0,
    @TotalExecutions BIGINT = 0,
    @SuccessfulExecutions BIGINT = 0,
    @FailedExecutions BIGINT = 0,
    @MonthlyExecutions INT = 0,
    @LastExecutionAt DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.N8nAccounts
    SET ActiveWorkflowsCount = @ActiveWorkflowsCount,
        TotalWorkflowsCount = @TotalWorkflowsCount,
        TotalExecutions = @TotalExecutions,
        SuccessfulExecutions = @SuccessfulExecutions,
        FailedExecutions = @FailedExecutions,
        MonthlyExecutions = @MonthlyExecutions,
        LastExecutionAt = COALESCE(@LastExecutionAt, LastExecutionAt),
        LastSyncedAt = SYSUTCDATETIME(),
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Revoked_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC dbo.usp_N8nAccounts_GetById @Id = @Id;
END
```
