USE master;
GO

-- 1. Garantizar que el backend (raft_backend) pueda ver y administrar TODAS las bases de datos
GRANT VIEW ANY DATABASE TO [raft_backend];
GO

-- 2. Asegurar que el rol public (usuarios regulares) NO pueda ver las bases de datos de otros
REVOKE VIEW ANY DATABASE FROM [public];
GO

-- 3. Actualizar la propiedad (owner) de las bases de datos existentes a su usuario correspondiente
--    para que cada usuario solo vea sus propias bases de datos en DBeaver / SSMS
DECLARE @dbName NVARCHAR(128);
DECLARE @loginName NVARCHAR(128);
DECLARE @sql NVARCHAR(MAX);

DECLARE db_cursor CURSOR FOR
SELECT name
FROM sys.databases
WHERE name LIKE 'raft_u%_%';

OPEN db_cursor;
FETCH NEXT FROM db_cursor INTO @dbName;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Extrae el login (ejemplo: 'raft_u11' de 'raft_u11_b871bee2')
    SET @loginName = SUBSTRING(@dbName, 1, CHARINDEX('_', @dbName, CHARINDEX('_', @dbName) + 1) - 1);
    
    IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @loginName)
    BEGIN
        SET @sql = 'ALTER AUTHORIZATION ON DATABASE::[' + @dbName + '] TO [' + @loginName + '];';
        BEGIN TRY
            EXEC sp_executesql @sql;
            PRINT 'Actualizado con éxito: ' + @dbName + ' -> Dueño: ' + @loginName;
        END TRY
        BEGIN CATCH
            PRINT 'Error actualizando ' + @dbName + ': ' + ERROR_MESSAGE();
        END CATCH
    END
    ELSE
    BEGIN
        PRINT 'Login no encontrado para: ' + @dbName + ' (Login: ' + @loginName + ')';
    END

    FETCH NEXT FROM db_cursor INTO @dbName;
END

CLOSE db_cursor;
DEALLOCATE db_cursor;
GO
