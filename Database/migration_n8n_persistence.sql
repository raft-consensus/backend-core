-- ============================================================================
-- SCRIPT DE MIGRACIÓN PARA DBEAVER DE UNA SOLA EJECUCIÓN (Ctrl + Enter)
-- Utiliza EXEC() para aislar cada Stored Procedure en un batch independiente
-- ============================================================================

USE RaftDb;

-- 1. MIGRACIÓN DE COLUMNAS EN TABLE dbo.N8nAccounts
IF COL_LENGTH('dbo.N8nAccounts', 'Credential') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD Credential NVARCHAR(2048) NULL;
END;

IF COL_LENGTH('dbo.N8nAccounts', 'AccessType') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD AccessType NVARCHAR(50) NULL;
END;

IF COL_LENGTH('dbo.N8nAccounts', 'ActiveWorkflowsCount') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD ActiveWorkflowsCount INT NOT NULL CONSTRAINT DF_N8nAccounts_ActiveWorkflows DEFAULT (0);
END;

IF COL_LENGTH('dbo.N8nAccounts', 'TotalWorkflowsCount') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD TotalWorkflowsCount INT NOT NULL CONSTRAINT DF_N8nAccounts_TotalWorkflows DEFAULT (0);
END;

IF COL_LENGTH('dbo.N8nAccounts', 'TotalExecutions') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD TotalExecutions BIGINT NOT NULL CONSTRAINT DF_N8nAccounts_TotalExecutions DEFAULT (0);
END;

IF COL_LENGTH('dbo.N8nAccounts', 'SuccessfulExecutions') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD SuccessfulExecutions BIGINT NOT NULL CONSTRAINT DF_N8nAccounts_SuccessExecutions DEFAULT (0);
END;

IF COL_LENGTH('dbo.N8nAccounts', 'FailedExecutions') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD FailedExecutions BIGINT NOT NULL CONSTRAINT DF_N8nAccounts_FailedExecutions DEFAULT (0);
END;

IF COL_LENGTH('dbo.N8nAccounts', 'MonthlyExecutions') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD MonthlyExecutions INT NOT NULL CONSTRAINT DF_N8nAccounts_MonthlyExecutions DEFAULT (0);
END;

IF COL_LENGTH('dbo.N8nAccounts', 'MaxMonthlyExecutions') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD MaxMonthlyExecutions INT NOT NULL CONSTRAINT DF_N8nAccounts_MaxMonthlyExecutions DEFAULT (1000);
END;

IF COL_LENGTH('dbo.N8nAccounts', 'MonthlyResetDate') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD MonthlyResetDate DATETIME2 NULL;
END;

IF COL_LENGTH('dbo.N8nAccounts', 'LastExecutionAt') IS NULL
BEGIN
    ALTER TABLE dbo.N8nAccounts ADD LastExecutionAt DATETIME2 NULL;
END;

-- 2. ACTUALIZACIÓN DE STORED PROCEDURES (VÍA EXEC PARA UN SOLO BATCH)

EXEC('
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetAllByUserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE UserId = @UserId
    ORDER BY Id DESC;
END;
');

EXEC('
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    ORDER BY Id DESC;
END;
');

EXEC('
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetById
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE Id = @Id;
END;
');

EXEC('
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetByExternalUserRef
    @ExternalUserRef NVARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE ExternalUserRef = @ExternalUserRef;
END;
');

EXEC('
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_GetActiveByUserId
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) Id, UserId, ExternalUserRef, Email, AccountId, Status, Credential, AccessType,
           ActiveWorkflowsCount, TotalWorkflowsCount, TotalExecutions,
           SuccessfulExecutions, FailedExecutions, MonthlyExecutions, MaxMonthlyExecutions,
           MonthlyResetDate, LastExecutionAt,
           Created_at AS CreatedAt, Updated_at AS UpdatedAt,
           Provisioned_at AS ProvisionedAt, Revoked_at AS RevokedAt,
           LastSyncedAt, LastErrorMessage
    FROM dbo.N8nAccounts
    WHERE UserId = @UserId
      AND Status IN (''Pending'', ''Active'')
      AND Revoked_at IS NULL
    ORDER BY Id DESC;
END;
');

EXEC('
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_MarkProvisioned
    @Id INT,
    @UserId INT,
    @AccountId NVARCHAR(128),
    @Credential NVARCHAR(2048) = NULL,
    @AccessType NVARCHAR(50) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.N8nAccounts
    SET AccountId = @AccountId,
        Credential = COALESCE(@Credential, Credential),
        AccessType = COALESCE(@AccessType, AccessType),
        Status = ''Active'',
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
END;
');

EXEC('
CREATE OR ALTER PROCEDURE dbo.usp_N8nAccounts_UpdateMetrics
    @Id INT,
    @ActiveWorkflowsCount INT = 0,
    @TotalWorkflowsCount INT = 0,
    @TotalExecutions BIGINT = 0,
    @SuccessfulExecutions BIGINT = 0,
    @FailedExecutions BIGINT = 0,
    @MonthlyExecutions INT = 0,
    @LastExecutionAt DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.N8nAccounts
    SET ActiveWorkflowsCount = @ActiveWorkflowsCount,
        TotalWorkflowsCount = @TotalWorkflowsCount,
        TotalExecutions = @TotalExecutions,
        SuccessfulExecutions = @SuccessfulExecutions,
        FailedExecutions = @FailedExecutions,
        MonthlyExecutions = @MonthlyExecutions,
        LastExecutionAt = COALESCE(@LastExecutionAt, LastExecutionAt),
        LastSyncedAt = SYSUTCDATETIME(),
        Updated_at = SYSUTCDATETIME()
    WHERE Id = @Id AND Revoked_at IS NULL;

    IF @@ROWCOUNT = 0
    BEGIN
        RETURN;
    END

    EXEC dbo.usp_N8nAccounts_GetById @Id = @Id;
END;
');
