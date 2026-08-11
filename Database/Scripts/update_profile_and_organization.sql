-- Migración e Implementación de Perfil de Usuario y Organización

-- 1. Agregar columna Organization a la tabla Users si no existe
IF COL_LENGTH('dbo.Users', 'Organization') IS NULL
BEGIN
    ALTER TABLE dbo.Users
    ADD Organization NVARCHAR(200) NULL;
END;
GO

-- 2. Actualizar usp_Users_GetAll para seleccionar Organization
IF OBJECT_ID('dbo.usp_Users_GetAll', 'P') IS NULL
    EXEC('CREATE PROCEDURE dbo.usp_Users_GetAll AS SELECT 1;');
GO

ALTER PROCEDURE dbo.usp_Users_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, Organization, AvatarUrl, Provider, ProviderUserId, Role,
           CASE WHEN PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasLocalPassword,
           CASE WHEN TemporaryPasswordHash IS NOT NULL AND TemporaryPasswordExpires_at > SYSUTCDATETIME()
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PasswordChangeRequired,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Deleted_at IS NULL
    ORDER BY Id;
END;
GO

-- 3. Actualizar usp_Users_GetById para seleccionar Organization
IF OBJECT_ID('dbo.usp_Users_GetById', 'P') IS NULL
    EXEC('CREATE PROCEDURE dbo.usp_Users_GetById AS SELECT 1;');
GO

ALTER PROCEDURE dbo.usp_Users_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, Organization, AvatarUrl, Provider, ProviderUserId, Role,
           CASE WHEN PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasLocalPassword,
           CASE WHEN TemporaryPasswordHash IS NOT NULL AND TemporaryPasswordExpires_at > SYSUTCDATETIME()
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PasswordChangeRequired,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Id = @Id AND Deleted_at IS NULL;
END;
GO

-- 4. Stored Procedure usp_Users_UpdateSelf para que el usuario edite su perfil
IF OBJECT_ID('dbo.usp_Users_UpdateSelf', 'P') IS NULL
    EXEC('CREATE PROCEDURE dbo.usp_Users_UpdateSelf AS SELECT 1;');
GO

ALTER PROCEDURE dbo.usp_Users_UpdateSelf
    @UserId INT,
    @Name NVARCHAR(200),
    @Organization NVARCHAR(200) = NULL,
    @AvatarUrl NVARCHAR(2048) = NULL
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
        SET Name = @Name,
            Organization = @Organization,
            AvatarUrl = COALESCE(@AvatarUrl, AvatarUrl),
            Updated_at = SYSUTCDATETIME()
        OUTPUT inserted.Id INTO @Updated (Id)
        WHERE Id = @UserId
          AND Deleted_at IS NULL;

        IF NOT EXISTS (SELECT 1 FROM @Updated)
        BEGIN
            ROLLBACK TRAN;
            RETURN;
        END

        INSERT INTO AuditEvents (UserId, EventType, Description, Created_at)
        VALUES (@UserId, 'UserProfileUpdated', 'User updated their personal profile information.', SYSUTCDATETIME());

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRAN;
        THROW;
    END CATCH

    EXEC usp_Users_GetById @Id = @UserId;
END;
GO
