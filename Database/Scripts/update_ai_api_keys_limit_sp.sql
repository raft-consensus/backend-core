-- ============================================================================
-- SCRIPT DE MIGRACIÓN / DOCUMENTACIÓN: usp_AiApiKeys_Create
-- ============================================================================
-- Propósito:
--   Establece una regla de negocio a nivel de Base de Datos (Database-Centric)
--   que limita la cantidad de API Keys que un usuario puede tener en estado ACTIVO.
--
-- Regla de Negocio:
--   - Un usuario puede tener un máximo de 10 (configurable) API Keys ACTIVAS.
--   - Las claves 'Revoked' (revocadas) o eliminadas NO cuentan contra el límite.
--   - Si el usuario alcanza el límite de claves activas, el SP aborta de forma
--     segura sin insertar (retorna vacío), permitiendo que el backend responda
--     un mensaje de error 400 Bad Request correspondiente.
--
-- Ejecución en DBeaver:
--   Compatible con Ctrl + Enter (Sin comandos GO, usa EXEC sp_executesql).
-- ============================================================================

USE [RaftDb];

EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE dbo.usp_AiApiKeys_Create
    @UserId INT,
    @Name NVARCHAR(120),
    @KeyPrefix NVARCHAR(12),
    @KeyHash NVARCHAR(128),
    @MaxActiveKeys INT = 10 -- Límite máximo de claves activas simultáneas
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Validar que no exista ya una clave con el mismo Hash SHA-256
    IF EXISTS (SELECT 1 FROM dbo.AiApiKeys WHERE KeyHash = @KeyHash)
    BEGIN
        RETURN;
    END

    -- 2. Validar que el usuario no supere la cuota de claves ACTIVAS
    DECLARE @ActiveCount INT;
    SELECT @ActiveCount = COUNT(*)
    FROM dbo.AiApiKeys
    WHERE UserId = @UserId
      AND Status = ''Active''
      AND Revoked_at IS NULL;

    IF @ActiveCount >= @MaxActiveKeys
    BEGIN
        -- Rechazo por cuota superada (no inserta ni retorna filas)
        RETURN;
    END

    -- 3. Inserción de la nueva API Key activa
    INSERT INTO dbo.AiApiKeys (UserId, Name, KeyPrefix, KeyHash, Status, Created_at)
    VALUES (@UserId, @Name, @KeyPrefix, @KeyHash, ''Active'', SYSUTCDATETIME());

    DECLARE @NewId INT = SCOPE_IDENTITY();
    EXEC dbo.usp_AiApiKeys_GetById @Id = @NewId;
END;
';
