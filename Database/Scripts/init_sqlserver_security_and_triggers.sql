-- =========================================================================
-- SCRIPT DE CONFIGURACIÓN DE SEGURIDAD Y AISLAMIENTO MULTITENANT SQL SERVER
-- Ejecutar en DBeaver sobre la base de datos 'master' con el usuario sa
-- =========================================================================

USE master;

-- 1. Permitir que el backend de la plataforma (raft_backend) pueda administrar y ver todo
GRANT VIEW ANY DATABASE TO [raft_backend];

-- 2. Revocar la visibilidad global a los usuarios normales (solo verán sus propias bases de datos)
REVOKE VIEW ANY DATABASE FROM [public];

-- 3. Trigger a nivel de Servidor: Evita que cualquier usuario elimine su base de datos por SQL (DROP DATABASE)
--    Obliga a que la eliminación se haga exclusivamente a través de la plataforma Raft.
CREATE OR ALTER TRIGGER trg_PreventUserDropDatabase
ON ALL SERVER
FOR DROP_DATABASE
AS
BEGIN
    SET NOCOUNT ON;
    
    -- sa y raft_backend son los únicos autorizados para ejecutar DROP DATABASE
    IF ORIGINAL_LOGIN() NOT IN ('sa', 'raft_backend')
    BEGIN
        RAISERROR('Operación cancelada: No tienes permisos para borrar la base de datos por SQL. Por favor utiliza el panel de Raft.', 16, 1);
        ROLLBACK;
    END
END;
