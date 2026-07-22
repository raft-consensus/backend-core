Autenticación y Aprovisionamiento Automático de Bases de Datos
Entrega #2

Objetivo
Implementar el primer flujo funcional de la plataforma permitiendo que cualquier usuario pueda autenticarse mediante proveedores OAuth y obtener acceso a crear una o varias base de datos MySQL lista para utilizar.

Requerimientos Funcionales
Autenticación
Permitir el registro mediante Google.
Permitir el registro mediante GitHub.
Permitir el inicio de sesión utilizando Google.
Permitir el inicio de sesión utilizando GitHub.
No duplicar usuarios cuando ya exista una cuenta registrada con el mismo proveedor.
Almacenar la información básica del usuario (nombre, correo, avatar y proveedor de autenticación).
Registrar la fecha de creación de la cuenta.
Registrar el último inicio de sesión.
Aprovisionamiento de Base de Datos
Al iniciar sesión por primera vez deberá crearse automáticamente una base de datos MySQL.
Crear automáticamente un usuario MySQL asociado a dicha base de datos.
Generar automáticamente una contraseña segura.
Asignar permisos únicamente sobre la base de datos creada.
Registrar toda la información del aprovisionamiento.
Entregar al usuario las credenciales de conexión.
Información que debe visualizar el usuario
Una vez creada la base de datos deberá visualizarse:

Host.
Puerto.
Nombre de la base de datos.
Usuario.
Contraseña.
Motor de base de datos.
Fecha de creación.
Estado de la base de datos.
Dashboard
El usuario deberá contar con un panel donde pueda consultar:

Información de conexión.
Estado de la base de datos.
Espacio utilizado.
Espacio máximo permitido.
Fecha de creación.
Última actividad.
Landing Page
La página principal deberá mostrar estadísticas generales de la plataforma.

Como mínimo deberá visualizar:

Cantidad total de usuarios registrados.
Cantidad total de bases de datos creadas.
Cantidad de bases de datos activas.
Cantidad total de inicios de sesión.
Usuarios activos.
Disponibilidad del servicio.
Backend
El backend deberá:

Exponer los endpoints REST.
Gestionar la autenticación OAuth.
Validar los JWT.
Aplicar Rate Limiting.
Invocar los Stored Procedures correspondientes.
Retornar respuestas HTTP estructuradas.
Registrar eventos y errores.
El backend no deberá implementar reglas de negocio relacionadas con el aprovisionamiento.

Base de Datos SQL Server
Toda la lógica de negocio deberá implementarse mediante:

Stored Procedures.
Views.
Functions.
Desde la base de datos deberá administrarse:

Registro de usuarios.
Validación de usuarios.
Registro del aprovisionamiento.
Auditoría.
Consulta de métricas.
Consulta de credenciales.
Seguridad
Utilizar autenticación OAuth2.
Utilizar HTTPS.
No permitir consultas SQL concatenadas.
Utilizar parámetros en todas las consultas.
Limitar la cantidad de peticiones por usuario.
Registrar auditoría de operaciones importantes.
Almacenar las contraseñas de forma segura.