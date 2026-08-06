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

CREATE OR ALTER PROCEDURE usp_Users_GetSharedSqlServerProvisioningState
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        SharedLoginName = CONCAT('raft_u', @UserId),
        HasExistingDatabases = CASE WHEN EXISTS (
            SELECT 1
            FROM DatabaseInstances di
            WHERE di.UserId = @UserId
              AND di.Deleted_at IS NULL
        ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END,
        EncryptedPassword = (
            SELECT TOP 1 ac.EncryptedPassword
            FROM AccessCredentials ac
            INNER JOIN DatabaseInstances di ON di.Id = ac.DatabaseInstanceId
            WHERE di.UserId = @UserId
              AND di.Deleted_at IS NULL
              AND ac.Deleted_at IS NULL
            ORDER BY di.Id
        );
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
GO
```

---

## 9. IA — API Keys y consumo

### 9.1 Tabla

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

### 9.2 Stored Procedures

#### 9.2.1 `usp_AiApiKeys_GetAllByUserId`

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

#### 9.2.2 `usp_AiApiKeys_GetById`

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

#### 9.2.3 `usp_AiApiKeys_GetByIdAndUserId`

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

#### 9.2.4 `usp_AiApiKeys_GetActiveByKeyHash`

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

#### 9.2.5 `usp_AiApiKeys_Create`

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

#### 9.2.6 `usp_AiApiKeys_Rotate`

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

#### 9.2.7 `usp_AiApiKeys_Revoke`

```sql
CREATE PROCEDURE dbo.usp_AiApiKeys_Revoke
    @Id INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AiApiKeys
    SET Status = 'Revoked',
        Revoked_at = SYSUTCDATETIME(),
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id
      AND UserId = @UserId
      AND Revoked_at IS NULL;
END
```

#### 9.2.8 `usp_AiApiKeys_RecordUsage`

```sql
CREATE PROCEDURE dbo.usp_AiApiKeys_RecordUsage
    @Id INT,
    @PromptTokens BIGINT,
    @CompletionTokens BIGINT,
    @ApproxCostUsd DECIMAL(18,6)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AiApiKeys
    SET TotalRequests = TotalRequests + 1,
        TotalPromptTokens = TotalPromptTokens + @PromptTokens,
        TotalCompletionTokens = TotalCompletionTokens + @CompletionTokens,
        TotalTokens = TotalTokens + (@PromptTokens + @CompletionTokens),
        ApproxCostUsd = ApproxCostUsd + @ApproxCostUsd,
        LastUsedAt = SYSUTCDATETIME(),
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id
      AND Revoked_at IS NULL;
END
```

---

## 10. N8N — Cuentas aprovisionadas para usuarios

### 10.1 Tabla

```sql
CREATE TABLE dbo.N8nAccounts (
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    ExternalUserRef NVARCHAR(64) NOT NULL,
    Email NVARCHAR(320) NOT NULL,
    AccountId NVARCHAR(128) NULL,
    Status NVARCHAR(20) NOT NULL CONSTRAINT DF_N8nAccounts_Status DEFAULT ('Pending'),
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

### 10.2 Stored Procedures

#### 10.2.1 `usp_N8nAccounts_GetAllByUserId`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetAllByUserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE UserId = @UserId
    ORDER BY Id DESC;
END
```

#### 10.2.2 `usp_N8nAccounts_GetAll`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    ORDER BY Id DESC;
END
```

#### 10.2.3 `usp_N8nAccounts_GetById`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE Id = @Id;
END
```

#### 10.2.4 `usp_N8nAccounts_GetByExternalUserRef`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetByExternalUserRef
    @ExternalUserRef NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE ExternalUserRef = @ExternalUserRef;
END
```

#### 10.2.5 `usp_N8nAccounts_GetActiveByUserId`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetActiveByUserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) Id, UserId, ExternalUserRef, Email, AccountId, Status,
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

#### 10.2.6 `usp_N8nAccounts_Create`

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

#### 10.2.7 `usp_N8nAccounts_MarkProvisioned`

```sql
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_MarkProvisioned
    @Id INT,
    @UserId INT,
    @AccountId NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.N8nAccounts
    SET AccountId = @AccountId,
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

#### 10.2.8 `usp_N8nAccounts_MarkFailed`

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

#### 10.2.9 `usp_N8nAccounts_Revoke`

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
