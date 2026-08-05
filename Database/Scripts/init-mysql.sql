-- Script de inicialización nativo para el contenedor de MySQL (Raft Platform)
DELIMITER //

CREATE PROCEDURE IF NOT EXISTS sp_create_database_and_user(
    IN p_dbname VARCHAR(64),
    IN p_dbuser VARCHAR(32),
    IN p_dbpass VARCHAR(64)
)
BEGIN
    -- Crear la base de datos
    SET @stmt_db = CONCAT('CREATE DATABASE IF NOT EXISTS `', p_dbname, '`');
    PREPARE stmt1 FROM @stmt_db;
    EXECUTE stmt1;
    DEALLOCATE PREPARE stmt1;

    -- Crear el usuario y asignar contraseña
    SET @stmt_user = CONCAT('CREATE USER IF NOT EXISTS \'', p_dbuser, '\'@\'%\' IDENTIFIED BY \'', p_dbpass, '\'');
    PREPARE stmt2 FROM @stmt_user;
    EXECUTE stmt2;
    DEALLOCATE PREPARE stmt2;

    -- Asignar todos los privilegios sobre la base de datos creada
    SET @stmt_grant = CONCAT('GRANT ALL PRIVILEGES ON `', p_dbname, '`.* TO \'', p_dbuser, '\'@\'%\'');
    PREPARE stmt3 FROM @stmt_grant;
    EXECUTE stmt3;
    DEALLOCATE PREPARE stmt3;

    FLUSH PRIVILEGES;
END //

DELIMITER ;
