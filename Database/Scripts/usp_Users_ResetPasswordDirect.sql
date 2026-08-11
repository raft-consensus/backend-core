IF OBJECT_ID('dbo.usp_Users_ResetPasswordDirect', 'P') IS NULL
BEGIN
    EXEC('CREATE PROCEDURE dbo.usp_Users_ResetPasswordDirect AS SELECT 1;');
END;
GO

ALTER PROCEDURE dbo.usp_Users_ResetPasswordDirect
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
END;
