-- =========================================================================
-- SCRIPT DE CONFIGURACIÓN DE SEGURIDAD Y AISLAMIENTO POSTGRESQL
-- Ejecutar en DBeaver sobre la base de datos 'postgres' con el usuario raft_pg_admin / postgres
-- =========================================================================

-- 1. Evitar que nuevas bases de datos hereden permisos públicos de conexión por defecto
REVOKE CONNECT ON DATABASE template1 FROM PUBLIC;

-- 2. Asegurar que raft_pg_admin tenga permisos completos sobre postgres
GRANT ALL PRIVILEGES ON DATABASE postgres TO raft_pg_admin;

-- 3. (Opcional) Aislar bases de datos existentes creadas previamente:
--    Para cada base de datos existente de un usuario:
--    REVOKE ALL ON DATABASE "raft_uXX_XXXXXXXX" FROM PUBLIC;
--    REVOKE CONNECT ON DATABASE "raft_uXX_XXXXXXXX" FROM PUBLIC;
--    ALTER DATABASE "raft_uXX_XXXXXXXX" OWNER TO "raft_uXX";
