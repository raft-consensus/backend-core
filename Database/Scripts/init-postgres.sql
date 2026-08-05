-- Script de inicialización nativo para el contenedor de PostgreSQL (Raft Platform)
CREATE OR REPLACE FUNCTION fn_create_database_and_user(
    p_dbname TEXT,
    p_dbuser TEXT,
    p_dbpass TEXT
) RETURNS VOID AS $$
BEGIN
    -- Crear usuario si no existe
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = p_dbuser) THEN
        EXECUTE format('CREATE ROLE %I WITH LOGIN PASSWORD %L', p_dbuser, p_dbpass);
    END IF;

    -- Crear base de datos asignando al usuario como propietario
    IF NOT EXISTS (SELECT FROM pg_database WHERE datname = p_dbname) THEN
        EXECUTE format('CREATE DATABASE %I OWNER %I', p_dbname, p_dbuser);
    END IF;

    -- Otorgar todos los privilegios sobre la BD al estudiante
    EXECUTE format('GRANT ALL PRIVILEGES ON DATABASE %I TO %I', p_dbname, p_dbuser);
END;
$$ LANGUAGE plpgsql;
