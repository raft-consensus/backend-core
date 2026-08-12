-- ============================================================================
-- Script DDL y Stored Procedures para Tabla Histórica por Eventos de IA (AiUsageLogs)
-- Diseñado específicamente para ejecución directa en DBeaver (Sin comandos GO)
-- Usa EXEC sp_executesql para independizar los lotes de creación de Stored Procedures.
-- ============================================================================

USE [RaftDb];

-- 1. Crear Tabla dbo.AiUsageLogs e Índices
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AiUsageLogs' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[AiUsageLogs] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [AiApiKeyId] INT NOT NULL CONSTRAINT FK_AiUsageLogs_AiApiKeys REFERENCES [dbo].[AiApiKeys]([Id]),
        [UserId] INT NOT NULL CONSTRAINT FK_AiUsageLogs_Users REFERENCES [dbo].[Users]([Id]),
        [Provider] NVARCHAR(80) NOT NULL,
        [Model] NVARCHAR(100) NOT NULL,
        [Endpoint] NVARCHAR(100) NOT NULL,
        [Mode] NVARCHAR(40) NULL,
        [PromptTokens] BIGINT NOT NULL CONSTRAINT DF_AiUsageLogs_PromptTokens DEFAULT (0),
        [CompletionTokens] BIGINT NOT NULL CONSTRAINT DF_AiUsageLogs_CompletionTokens DEFAULT (0),
        [TotalTokens] BIGINT NOT NULL CONSTRAINT DF_AiUsageLogs_TotalTokens DEFAULT (0),
        [ApproxCostUsd] DECIMAL(18,6) NOT NULL CONSTRAINT DF_AiUsageLogs_ApproxCostUsd DEFAULT (0),
        [DurationMs] INT NULL,
        [StatusCode] INT NOT NULL CONSTRAINT DF_AiUsageLogs_StatusCode DEFAULT (200),
        [Created_at] DATETIME2 NOT NULL CONSTRAINT DF_AiUsageLogs_Created_at DEFAULT (SYSUTCDATETIME())
    );

    CREATE INDEX IX_AiUsageLogs_UserId_CreatedAt ON [dbo].[AiUsageLogs]([UserId], [Created_at] DESC);
    CREATE INDEX IX_AiUsageLogs_AiApiKeyId_CreatedAt ON [dbo].[AiUsageLogs]([AiApiKeyId], [Created_at] DESC);
    CREATE INDEX IX_AiUsageLogs_CreatedAt ON [dbo].[AiUsageLogs]([Created_at] DESC);
END;

-- 2. Stored Procedure: usp_AiApiKeys_RecordUsage
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_AiApiKeys_RecordUsage]
    @Id INT,
    @UserId INT = NULL,
    @Provider NVARCHAR(80) = ''default'',
    @Model NVARCHAR(100) = ''default'',
    @Endpoint NVARCHAR(100) = ''/api/ai/generate'',
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
            @Id, @UserId, ISNULL(@Provider, ''default''), ISNULL(@Model, ''default''),
            ISNULL(@Endpoint, ''/api/ai/generate''), @Mode, ISNULL(@PromptTokens, 0),
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
END;
';

-- 3. Stored Procedure: usp_AiUsageLogs_GetHistoryByUserId
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_AiUsageLogs_GetHistoryByUserId]
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
END;
';

-- 4. Stored Procedure: usp_AiUsageLogs_GetAnalyticsByUserId
EXEC sp_executesql N'
CREATE OR ALTER PROCEDURE [dbo].[usp_AiUsageLogs_GetAnalyticsByUserId]
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

    -- Resultset 1: Totales generales
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

    -- Resultset 2: Series de tiempo por día
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

    -- Resultset 3: Desglose por proveedor y modelo
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
END;
';
