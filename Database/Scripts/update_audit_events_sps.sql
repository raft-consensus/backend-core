-- ============================================================================
-- SCRIPT DE MIGRACIÓN PARA DBEAVER DE UNA SOLA EJECUCIÓN (Ctrl + Enter)
-- Actualización de Stored Procedures del Módulo de Auditoría y Eventos
-- Incluye soporte para todos los servicios: Auth, Bases de Datos, IA, DNS, N8N y Perfil
-- No utiliza 'GO'; usa EXEC sp_executesql para independizar cada bloque.
-- ============================================================================

USE [RaftDb];

-- 1. Stored Procedure: usp_AuditEvents_GetAll
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditEvents_GetAll]
    @Limit INT = 1000
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Limit)
        Id,
        UserId,
        EventType,
        Description,
        IpAddress,
        AdditionalData,
        Created_at AS CreatedAt,
        Deleted_at AS DeletedAt
    FROM dbo.AuditEvents
    WHERE Deleted_at IS NULL
    ORDER BY Id DESC;
END;
';

-- 2. Stored Procedure: usp_AuditEvents_GetById
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditEvents_GetById]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        Id,
        UserId,
        EventType,
        Description,
        IpAddress,
        AdditionalData,
        Created_at AS CreatedAt,
        Deleted_at AS DeletedAt
    FROM dbo.AuditEvents
    WHERE Id = @Id AND Deleted_at IS NULL;
END;
';

-- 3. Stored Procedure: usp_AuditEvents_GetByUserId (Para actividad del usuario en Dashboard)
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditEvents_GetByUserId]
    @UserId INT,
    @Limit INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Limit)
        Id,
        UserId,
        EventType,
        Description,
        IpAddress,
        AdditionalData,
        Created_at AS CreatedAt,
        Deleted_at AS DeletedAt
    FROM dbo.AuditEvents
    WHERE UserId = @UserId AND Deleted_at IS NULL
    ORDER BY Id DESC;
END;
';

-- 4. Stored Procedure: usp_AuditEvents_Create (Soporta todos los módulos y tipos de evento)
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditEvents_Create]
    @UserId INT = NULL,
    @EventType NVARCHAR(100),
    @Description NVARCHAR(2000),
    @IpAddress NVARCHAR(50) = NULL,
    @AdditionalData NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NewId BIGINT;

    INSERT INTO dbo.AuditEvents (
        UserId,
        EventType,
        Description,
        IpAddress,
        AdditionalData,
        Created_at
    )
    VALUES (
        @UserId,
        @EventType,
        @Description,
        @IpAddress,
        @AdditionalData,
        SYSUTCDATETIME()
    );

    SET @NewId = SCOPE_IDENTITY();

    EXEC dbo.usp_AuditEvents_GetById @Id = @NewId;
END;
';

-- 5. Stored Procedure: usp_AuditEvents_Update
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditEvents_Update]
    @Id BIGINT,
    @UserId INT = NULL,
    @EventType NVARCHAR(100),
    @Description NVARCHAR(2000),
    @IpAddress NVARCHAR(50) = NULL,
    @AdditionalData NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AuditEvents
    SET UserId = @UserId,
        EventType = @EventType,
        Description = @Description,
        IpAddress = @IpAddress,
        AdditionalData = @AdditionalData,
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC dbo.usp_AuditEvents_GetById @Id = @Id;
END;
';

-- 6. Stored Procedure: usp_AuditEvents_SoftDelete
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_AuditEvents_SoftDelete]
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AuditEvents
    SET Deleted_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Deleted_at IS NULL;
END;
';
