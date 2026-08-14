-- =========================================================================
-- SCRIPT DE AISLAMIENTO Y SEGURIDAD TOTAL PARA POSTGRESQL (EXISTENTES Y NUEVAS)
-- Ejecutar en DBeaver sobre la base de datos 'postgres' con el usuario raft_pg_admin / postgres
-- =========================================================================

-- 1. Evitar que cualquier usuario se conecte por defecto a template1 o a postgres
REVOKE CONNECT ON DATABASE template1 FROM PUBLIC;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
GRANT ALL PRIVILEGES ON DATABASE postgres TO raft_pg_admin;

-- 2. Aislar TODAS las bases de datos de usuarios existentes (raft_uX_XXXX)
DO $$
DECLARE
    r RECORD;
    v_login_name TEXT;
BEGIN
    FOR r IN (
        SELECT datname 
        FROM pg_database 
        WHERE datname LIKE 'raft_u%_%'
    ) LOOP
        -- Extrae el nombre del rol (ejemplo: 'raft_u11' de 'raft_u11_5debe5e5')
        v_login_name := split_part(r.datname, '_', 1) || '_' || split_part(r.datname, '_', 2);
        
        -- Revocar TODO acceso al rol PUBLIC (ningún otro usuario podrá entrar)
        EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC;', r.datname);
        EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC;', r.datname);
        
        -- Si el rol de usuario existe, asignarle la propiedad exclusiva y permiso de conexión
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = v_login_name) THEN
            EXECUTE format('ALTER DATABASE %I OWNER TO %I;', r.datname, v_login_name);
            EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I;', r.datname, v_login_name);
            RAISE NOTICE 'Base de datos "%" aislada exitosamente para "%"', r.datname, v_login_name;
        ELSE
            EXECUTE format('ALTER DATABASE %I OWNER TO raft_pg_admin;', r.datname);
            RAISE NOTICE 'Base de datos "%" asignada a raft_pg_admin (rol no encontrado)', r.datname;
        END IF;

        -- Garantizar permisos completos de administración al backend
        EXECUTE format('GRANT ALL PRIVILEGES ON DATABASE %I TO raft_pg_admin;', r.datname);
    END LOOP;
END $$;
