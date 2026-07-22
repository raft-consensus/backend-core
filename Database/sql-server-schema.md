# SQL Server — Schema, Views y Stored Procedures

Ejecutar en orden, de arriba hacia abajo, contra la base de datos `RaftDb` (o el nombre que le hayan puesto). Cada bloque es idempotente donde es posible (`CREATE OR ALTER`), salvo la creación de tablas, que solo debe correrse una vez.

> No hay migraciones EF ni ejecución automática desde el backend — este script es la única fuente de verdad del esquema. El backend solo consume las tablas/SPs/Views ya creados, vía `ISqlStoredProcedureExecutor`.

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

```sql
CREATE TABLE Users (
    Id INT IDENTITY(1,1) NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    Email NVARCHAR(320) NOT NULL,
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
GO

CREATE UNIQUE INDEX IX_Users_Provider_ProviderUserId ON Users (Provider, ProviderUserId);
GO

CREATE INDEX IX_Users_Email ON Users (Email);
GO

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
GO

CREATE INDEX IX_DatabaseInstances_UserId ON DatabaseInstances (UserId);
GO

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
GO

CREATE UNIQUE INDEX IX_AccessCredentials_DatabaseInstanceId ON AccessCredentials (DatabaseInstanceId);
GO

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
GO

CREATE INDEX IX_AuditEvents_UserId ON AuditEvents (UserId);
GO
```

---

## 2. Stored Procedures — Users (admin CRUD)

Usados por `UsersController` (`AdminOnly`). El flujo real de login usa `usp_Users_UpsertFromOAuth` (sección 7), no estos.

```sql
CREATE OR ALTER PROCEDURE usp_Users_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, AvatarUrl, Provider, ProviderUserId, Role,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Deleted_at IS NULL
    ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE usp_Users_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, AvatarUrl, Provider, ProviderUserId, Role,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Id = @Id AND Deleted_at IS NULL;
END
GO

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
GO

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
GO

CREATE OR ALTER PROCEDURE usp_Users_SoftDelete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE Users
    SET Deleted_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;
END
GO
```

---

## 3. Stored Procedures — DatabaseInstances (admin CRUD)

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
GO

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
GO

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
GO

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
GO

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
GO
```

---

## 4. Stored Procedures — AccessCredentials (admin CRUD)

Nunca devuelven `EncryptedPassword`. Leer la contraseña descifrada es un camino aparte, con verificación de dueño (sección 7).

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
GO

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
GO

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
GO

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
GO

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
GO

CREATE OR ALTER PROCEDURE usp_AccessCredentials_SoftDelete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE AccessCredentials
    SET Deleted_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;
END
GO
```

---

## 5. Stored Procedures — AuditEvents

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
GO

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
GO

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
GO

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
GO

CREATE OR ALTER PROCEDURE usp_AuditEvents_SoftDelete
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE AuditEvents
    SET Deleted_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;
END
GO
```

---

## 6. Views y métricas (landing page + dashboard)

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
GO

CREATE OR ALTER PROCEDURE usp_PlatformMetrics_Get
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TotalUsers, TotalDatabases, ActiveDatabases, TotalLogins, ActiveUsers, ServiceAvailability
    FROM vw_PlatformMetrics;
END
GO

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
GO

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
GO
```

---

## 7. Autenticación y aprovisionamiento (el flujo real de negocio)

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
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin,
           @IsNewUser AS IsNewUser
    FROM Users
    WHERE Id = @UserId;
END
GO

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
GO
```

---

## 8. Ciclo de vida (TTL y cuota de almacenamiento)

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
GO

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
GO

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
GO

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
GO

-- DatabaseUser es único por instancia, así que el job mapea el usuario MySQL que vio
-- conectado de vuelta a su fila en DatabaseInstances.
CREATE OR ALTER PROCEDURE usp_DatabaseInstances_TouchActivityByDatabaseUser
    @DatabaseUser NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE DatabaseInstances
    SET LastActivity = SYSUTCDATETIME()
    WHERE DatabaseUser = @DatabaseUser AND Deleted_at IS NULL;
END
GO
```
