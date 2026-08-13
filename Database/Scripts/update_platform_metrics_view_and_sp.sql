-- ============================================================================
-- SCRIPT DE MIGRACIÓN PARA DBEAVER DE UNA SOLA EJECUCIÓN (Ctrl + Enter)
-- Diseñado específicamente para ejecución directa en DBeaver (Sin comandos GO)
-- Utiliza EXEC sp_executesql para independizar los lotes de creación.
-- ============================================================================

USE [RaftDb];

-- 1. Actualizar Vista vw_PlatformMetrics con las 6 métricas reales
EXEC sp_executesql N'
CREATE OR ALTER VIEW [dbo].[vw_PlatformMetrics]
AS
SELECT
    -- 1. Usuarios Registrados
    (SELECT COUNT(*) FROM dbo.Users WHERE Deleted_at IS NULL) AS TotalUsers,

    -- 2. Bases de Datos Activas y Totales
    (SELECT COUNT(*) FROM dbo.DatabaseInstances WHERE Deleted_at IS NULL) AS TotalDatabases,
    (SELECT COUNT(*) FROM dbo.DatabaseInstances WHERE Deleted_at IS NULL AND Status = ''Active'') AS ActiveDatabases,

    -- 3. Subdominios DNS Activos
    (SELECT COUNT(*) FROM dbo.DnsRecords WHERE Status = ''Active'' AND RevokedAt IS NULL) AS TotalSubdomains,

    -- 4. Consultas IA Generadas
    (
        SELECT ISNULL(
            (SELECT COUNT(*) FROM dbo.AiUsageLogs),
            (SELECT ISNULL(SUM(TotalRequests), 0) FROM dbo.AiApiKeys)
        )
    ) AS TotalAiRequests,

    -- 5. Flujos n8n Ejecutados
    (
        SELECT ISNULL(
            (SELECT SUM(TotalExecutions) FROM dbo.N8nAccounts WHERE Status IN (''Active'', ''Provisioned'')),
            (SELECT ISNULL(SUM(TotalWorkflowsCount), 0) FROM dbo.N8nAccounts WHERE Status IN (''Active'', ''Provisioned''))
        )
    ) AS TotalN8nExecutions,

    -- 6. Operaciones Seguras Auditadas
    (SELECT COUNT(*) FROM dbo.AuditEvents WHERE Deleted_at IS NULL) AS TotalSecureOperations,

    -- Campos de compatibilidad secundaria
    (SELECT COUNT(*) FROM dbo.AuditEvents WHERE Deleted_at IS NULL AND EventType = ''Login'') AS TotalLogins,
    (SELECT COUNT(*) FROM dbo.Users WHERE Deleted_at IS NULL AND LastLogin >= DATEADD(DAY, -30, SYSUTCDATETIME())) AS ActiveUsers,
    CAST(100.0 AS DECIMAL(5, 2)) AS ServiceAvailability;
';

-- 2. Actualizar Stored Procedure usp_PlatformMetrics_Get
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_PlatformMetrics_Get]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        TotalUsers, 
        TotalDatabases,
        ActiveDatabases, 
        TotalSubdomains, 
        TotalAiRequests, 
        TotalN8nExecutions, 
        TotalSecureOperations,
        TotalLogins,
        ActiveUsers,
        ServiceAvailability
    FROM [dbo].[vw_PlatformMetrics];
END;
';
