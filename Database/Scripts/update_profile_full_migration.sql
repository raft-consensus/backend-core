USE RaftDb;

-- 1. Agregar columnas a la tabla Users
IF COL_LENGTH('dbo.Users', 'Organization') IS NULL
    ALTER TABLE dbo.Users ADD Organization NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.Users', 'Phone') IS NULL
    ALTER TABLE dbo.Users ADD Phone NVARCHAR(50) NULL;

IF COL_LENGTH('dbo.Users', 'Gender') IS NULL
    ALTER TABLE dbo.Users ADD Gender NVARCHAR(20) NULL;

IF COL_LENGTH('dbo.Users', 'BirthDate') IS NULL
    ALTER TABLE dbo.Users ADD BirthDate DATE NULL;

IF COL_LENGTH('dbo.Users', 'Country') IS NULL
    ALTER TABLE dbo.Users ADD Country NVARCHAR(100) NULL;

-- 2. Stored Procedure usp_Users_GetAll
EXEC('
CREATE OR ALTER PROCEDURE usp_Users_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, Organization, Phone, Gender, BirthDate, Country, AvatarUrl, Provider, ProviderUserId, Role,
           CASE WHEN PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasLocalPassword,
           CASE WHEN TemporaryPasswordHash IS NOT NULL AND TemporaryPasswordExpires_at > SYSUTCDATETIME()
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PasswordChangeRequired,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Deleted_at IS NULL
    ORDER BY Id;
END;
');

-- 3. Stored Procedure usp_Users_GetById
EXEC('
CREATE OR ALTER PROCEDURE usp_Users_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, Name, Email, Organization, Phone, Gender, BirthDate, Country, AvatarUrl, Provider, ProviderUserId, Role,
           CASE WHEN PasswordHash IS NULL THEN CAST(0 AS BIT) ELSE CAST(1 AS BIT) END AS HasLocalPassword,
           CASE WHEN TemporaryPasswordHash IS NOT NULL AND TemporaryPasswordExpires_at > SYSUTCDATETIME()
                THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS PasswordChangeRequired,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt, Deleted_at AS DeletedAt, LastLogin
    FROM Users
    WHERE Id = @Id AND Deleted_at IS NULL;
END;
');

-- 4. Stored Procedure usp_Users_UpdateSelf
EXEC('
CREATE OR ALTER PROCEDURE usp_Users_UpdateSelf
    @UserId INT,
    @Name NVARCHAR(200),
    @Organization NVARCHAR(200) = NULL,
    @Phone NVARCHAR(50) = NULL,
    @Gender NVARCHAR(20) = NULL,
    @BirthDate DATE = NULL,
    @Country NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Updated TABLE (Id INT NOT NULL);

    BEGIN TRY
        BEGIN TRAN;

        UPDATE Users
        SET Name = @Name,
            Organization = @Organization,
            Phone = @Phone,
            Gender = @Gender,
            BirthDate = @BirthDate,
            Country = @Country,
            Updated_at = SYSUTCDATETIME()
        OUTPUT inserted.Id INTO @Updated (Id)
        WHERE Id = @UserId AND Deleted_at IS NULL;

        IF NOT EXISTS (SELECT 1 FROM @Updated)
        BEGIN
            ROLLBACK TRAN;
            RETURN;
        END

        INSERT INTO AuditEvents (UserId, EventType, Description, Created_at)
        VALUES (@UserId, ''UserProfileUpdated'', ''User updated personal profile details.'', SYSUTCDATETIME());

        COMMIT TRAN;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRAN;
        THROW;
    END CATCH

    EXEC usp_Users_GetById @Id = @UserId;
END;
');
