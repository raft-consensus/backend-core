-- ============================================================================
-- Script DDL y Stored Procedures para Gestión de Registros DNS en RaftDB
-- ============================================================================

USE [RaftDb];
GO

-- 1. Tabla dbo.DnsRecords
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DnsRecords' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE [dbo].[DnsRecords] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [UserId] INT NOT NULL CONSTRAINT FK_DnsRecords_Users REFERENCES [dbo].[Users]([Id]),
        [Label] NVARCHAR(100) NOT NULL,
        [RecordName] NVARCHAR(200) NOT NULL,
        [Fqdn] NVARCHAR(255) NOT NULL,
        [RecordType] VARCHAR(10) NOT NULL CONSTRAINT DF_DnsRecords_RecordType DEFAULT ('A'),
        [Content] NVARCHAR(255) NOT NULL,
        [Comment] NVARCHAR(500) NULL,
        [RecordTtl] INT NOT NULL CONSTRAINT DF_DnsRecords_RecordTtl DEFAULT (1),
        [Proxied] BIT NOT NULL CONSTRAINT DF_DnsRecords_Proxied DEFAULT (0),
        [CloudflareZoneId] VARCHAR(100) NULL,
        [CloudflareRecordId] VARCHAR(100) NULL,
        [Status] VARCHAR(50) NOT NULL CONSTRAINT DF_DnsRecords_Status DEFAULT ('Pending'),
        [LastError] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2 NOT NULL CONSTRAINT DF_DnsRecords_CreatedAt DEFAULT (GETUTCDATE()),
        [UpdatedAt] DATETIME2 NULL,
        [RevokedAt] DATETIME2 NULL
    );

    CREATE INDEX IX_DnsRecords_UserId ON [dbo].[DnsRecords]([UserId]);
    CREATE INDEX IX_DnsRecords_Fqdn ON [dbo].[DnsRecords]([Fqdn]);
END
GO

-- Agregar columna Comment si la tabla ya existía previamente sin ella
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'DnsRecords' AND schema_id = SCHEMA_ID('dbo'))
   AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DnsRecords') AND name = 'Comment')
BEGIN
    ALTER TABLE [dbo].[DnsRecords] ADD [Comment] NVARCHAR(500) NULL;
END
GO

-- 2. Stored Procedure: usp_DnsRecords_Create
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_Create]
    @UserId INT,
    @Label NVARCHAR(100),
    @RecordName NVARCHAR(200),
    @Fqdn NVARCHAR(255),
    @RecordType VARCHAR(10) = 'A',
    @Content NVARCHAR(255),
    @Comment NVARCHAR(500) = NULL,
    @RecordTtl INT = 1,
    @Proxied BIT = 0,
    @CloudflareZoneId VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[DnsRecords] (
        [UserId], [Label], [RecordName], [Fqdn], [RecordType],
        [Content], [Comment], [RecordTtl], [Proxied], [CloudflareZoneId],
        [Status], [CreatedAt]
    )
    VALUES (
        @UserId, @Label, @RecordName, @Fqdn, ISNULL(@RecordType, 'A'),
        @Content, @Comment, ISNULL(@RecordTtl, 1), ISNULL(@Proxied, 0), @CloudflareZoneId,
        'Pending', GETUTCDATE()
    );

    DECLARE @NewId INT = SCOPE_IDENTITY();

    SELECT 
        [Id], [UserId], [Label], [RecordName], [Fqdn], [RecordType],
        [Content], [Comment], [RecordTtl], [Proxied], [CloudflareZoneId],
        [CloudflareRecordId], [Status], [LastError], [CreatedAt],
        [UpdatedAt], [RevokedAt]
    FROM [dbo].[DnsRecords]
    WHERE [Id] = @NewId;
END
GO

-- 3. Stored Procedure: usp_DnsRecords_MarkProvisioned
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_MarkProvisioned]
    @Id INT,
    @CloudflareRecordId VARCHAR(100),
    @CloudflareZoneId VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[DnsRecords]
    SET [Status] = 'Active',
        [CloudflareRecordId] = @CloudflareRecordId,
        [CloudflareZoneId] = ISNULL(@CloudflareZoneId, [CloudflareZoneId]),
        [LastError] = NULL,
        [UpdatedAt] = GETUTCDATE()
    WHERE [Id] = @Id;

    SELECT 
        [Id], [UserId], [Label], [RecordName], [Fqdn], [RecordType],
        [Content], [Comment], [RecordTtl], [Proxied], [CloudflareZoneId],
        [CloudflareRecordId], [Status], [LastError], [CreatedAt],
        [UpdatedAt], [RevokedAt]
    FROM [dbo].[DnsRecords]
    WHERE [Id] = @Id;
END
GO

-- 4. Stored Procedure: usp_DnsRecords_MarkFailed
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_MarkFailed]
    @Id INT,
    @LastError NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[DnsRecords]
    SET [Status] = 'Failed',
        [LastError] = @LastError,
        [UpdatedAt] = GETUTCDATE()
    WHERE [Id] = @Id;
END
GO

-- 5. Stored Procedure: usp_DnsRecords_Update
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_Update]
    @Id INT,
    @UserId INT,
    @Label NVARCHAR(100),
    @RecordName NVARCHAR(200),
    @Fqdn NVARCHAR(255),
    @Content NVARCHAR(255),
    @Comment NVARCHAR(500) = NULL,
    @RecordTtl INT = 1,
    @Proxied BIT = 0
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[DnsRecords]
    SET [Label] = @Label,
        [RecordName] = @RecordName,
        [Fqdn] = @Fqdn,
        [Content] = @Content,
        [Comment] = @Comment,
        [RecordTtl] = ISNULL(@RecordTtl, 1),
        [Proxied] = ISNULL(@Proxied, 0),
        [UpdatedAt] = GETUTCDATE()
    WHERE [Id] = @Id AND [UserId] = @UserId AND [Status] <> 'Revoked';

    SELECT 
        [Id], [UserId], [Label], [RecordName], [Fqdn], [RecordType],
        [Content], [Comment], [RecordTtl], [Proxied], [CloudflareZoneId],
        [CloudflareRecordId], [Status], [LastError], [CreatedAt],
        [UpdatedAt], [RevokedAt]
    FROM [dbo].[DnsRecords]
    WHERE [Id] = @Id AND [UserId] = @UserId;
END
GO

-- 6. Stored Procedure: usp_DnsRecords_GetAllByUserId
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_GetAllByUserId]
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        [Id], [UserId], [Label], [RecordName], [Fqdn], [RecordType],
        [Content], [Comment], [RecordTtl], [Proxied], [CloudflareZoneId],
        [CloudflareRecordId], [Status], [LastError], [CreatedAt],
        [UpdatedAt], [RevokedAt]
    FROM [dbo].[DnsRecords]
    WHERE [UserId] = @UserId AND [Status] <> 'Revoked'
    ORDER BY [CreatedAt] DESC;
END
GO

-- 7. Stored Procedure: usp_DnsRecords_GetAll
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_GetAll]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        [Id], [UserId], [Label], [RecordName], [Fqdn], [RecordType],
        [Content], [Comment], [RecordTtl], [Proxied], [CloudflareZoneId],
        [CloudflareRecordId], [Status], [LastError], [CreatedAt],
        [UpdatedAt], [RevokedAt]
    FROM [dbo].[DnsRecords]
    ORDER BY [CreatedAt] DESC;
END
GO

-- 8. Stored Procedure: usp_DnsRecords_GetById
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_GetById]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        [Id], [UserId], [Label], [RecordName], [Fqdn], [RecordType],
        [Content], [Comment], [RecordTtl], [Proxied], [CloudflareZoneId],
        [CloudflareRecordId], [Status], [LastError], [CreatedAt],
        [UpdatedAt], [RevokedAt]
    FROM [dbo].[DnsRecords]
    WHERE [Id] = @Id;
END
GO

-- 9. Stored Procedure: usp_DnsRecords_GetByIdAndUserId
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_GetByIdAndUserId]
    @Id INT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        [Id], [UserId], [Label], [RecordName], [Fqdn], [RecordType],
        [Content], [Comment], [RecordTtl], [Proxied], [CloudflareZoneId],
        [CloudflareRecordId], [Status], [LastError], [CreatedAt],
        [UpdatedAt], [RevokedAt]
    FROM [dbo].[DnsRecords]
    WHERE [Id] = @Id AND [UserId] = @UserId AND [Status] <> 'Revoked';
END
GO

-- 10. Stored Procedure: usp_DnsRecords_GetActiveByUserIdAndFqdn
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_GetActiveByUserIdAndFqdn]
    @UserId INT,
    @Fqdn NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP 1
        [Id], [UserId], [Label], [RecordName], [Fqdn], [RecordType],
        [Content], [Comment], [RecordTtl], [Proxied], [CloudflareZoneId],
        [CloudflareRecordId], [Status], [LastError], [CreatedAt],
        [UpdatedAt], [RevokedAt]
    FROM [dbo].[DnsRecords]
    WHERE [UserId] = @UserId AND [Fqdn] = @Fqdn AND [Status] <> 'Revoked';
END
GO

-- 11. Stored Procedure: usp_DnsRecords_Revoke
CREATE OR ALTER PROCEDURE [dbo].[usp_DnsRecords_Revoke]
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[DnsRecords]
    SET [Status] = 'Revoked',
        [RevokedAt] = GETUTCDATE()
    WHERE [Id] = @Id AND [Status] <> 'Revoked';

    SELECT @@ROWCOUNT;
END
GO
