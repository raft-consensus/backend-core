-- ============================================================================
-- SCRIPT DE CORRECCIÓN PARA DBEAVER (Ctrl + Enter)
-- Corrige usp_N8nAccounts_Create para permitir reintentos cuando la API externa falla.
-- Si hay un registro previo en estado 'Failed', lo reutiliza y reactiva a 'Pending'.
-- ============================================================================

USE [RaftDb];

EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_Create
    @UserId INT,
    @ExternalUserRef NVARCHAR(64),
    @Email NVARCHAR(320),
    @AccountId NVARCHAR(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Si ya existe una cuenta ACTIVA o PENDIENTE, retornar esa misma
    DECLARE @ExistingId INT;
    SELECT TOP (1) @ExistingId = Id
    FROM dbo.N8nAccounts
    WHERE (UserId = @UserId OR ExternalUserRef = @ExternalUserRef)
      AND Status IN (''Pending'', ''Active'')
      AND Revoked_at IS NULL
    ORDER BY Id DESC;

    IF @ExistingId IS NOT NULL
    BEGIN
        EXEC dbo.usp_N8nAccounts_GetById @Id = @ExistingId;
        RETURN;
    END

    -- 2. Si existía un intento previo FALLIDO (Failed), reutilizarlo reactivándolo a ''Pending''
    SELECT TOP (1) @ExistingId = Id
    FROM dbo.N8nAccounts
    WHERE (UserId = @UserId OR ExternalUserRef = @ExternalUserRef)
      AND Status = ''Failed''
      AND Revoked_at IS NULL
    ORDER BY Id DESC;

    IF @ExistingId IS NOT NULL
    BEGIN
        UPDATE dbo.N8nAccounts
        SET Status = ''Pending'',
            Email = @Email,
            ExternalUserRef = @ExternalUserRef,
            LastErrorMessage = NULL,
            Updated_at = SYSUTCDATETIME()
        WHERE Id = @ExistingId;

        EXEC dbo.usp_N8nAccounts_GetById @Id = @ExistingId;
        RETURN;
    END

    -- 3. Si no existe ningún registro, insertar nuevo
    INSERT INTO dbo.N8nAccounts
        (UserId, ExternalUserRef, Email, AccountId, Status, Created_at)
    VALUES
        (@UserId, @ExternalUserRef, @Email, @AccountId, ''Pending'', SYSUTCDATETIME());

    DECLARE @NewId INT = SCOPE_IDENTITY();
    EXEC dbo.usp_N8nAccounts_GetById @Id = @NewId;
END;
';