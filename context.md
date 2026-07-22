Plataforma de Hosting DB & Servicios para Desarrolladores
El objetivo de este proyecto es diseñar e implementar un sistema de información robusto y seguro que actúe como un proveedor de servicios de bases de datos gratuitas (y herramientas de prueba) para estudiantes y desarrolladores.

La particularidad de este reto radica en su arquitectura: toda la lógica de negocio debe residir y ejecutarse en la base de datos, delegando al backend un rol exclusivamente de mediador, despachador y asegurador de la comunicación.

Filosofía de la Arquitectura
En este proyecto romperemos el paradigma tradicional donde el backend procesa las reglas de negocio. Aquí adoptaremos un enfoque Database-Centric:

⚠️ Regla de Oro: El Backend es "tonto" y la Base de Datos es "inteligente". Ninguna regla de validación compleja, cálculo, asignación de permisos o flujo de negocio se escribe en el código del servidor de aplicaciones. El backend solo recibe la petición, la traslada a la base de datos, y retorna la respuesta estructurada.

Roles de cada componente:
Motor de Base de Datos (SQL Server): Almacena los datos y ejecuta el 100% de la lógica de negocio mediante Stored Procedures (SPs) para operaciones de escritura/modificación, Views para consultas complejas, y Functions para cálculos o transformaciones de datos.
Backend Framework (A elección): Actúa como un middleware de paso. Se encarga de exponer los endpoints HTTP, gestionar la autenticación/autorización, aplicar políticas de tráfico (Rate Limiting) y mapear los resultados de la base de datos hacia el cliente.
Requerimientos Técnicos
Para garantizar la mantenibilidad y el desacoplamiento en el backend, se deberán seguir estrictamente los siguientes lineamientos:

Patrón Repositorio con Inversión de Dependencias (Dependency Inversion):
El backend debe definir interfaces claras (interfaces o contracts) para las operaciones de persistencia.
La implementación concreta del repositorio solo se encargará de invocar a los Stored Procedures o Views correspondientes en SQL Server.
El controlador o servicio del backend dependerá únicamente de la abstracción (la interfaz), cumpliendo con el principio DIP (Dependency Inversion Principle) de SOLID.
Tecnologías:
Base de Datos: Microsoft SQL Server.
Backend: Framework a elección del equipo (se recomienda un stack fuertemente tipado como .NET Web API o soluciones ágiles como Laravel / NestJS).
Viabilidad del Servicio y Políticas de Seguridad
Dado que expondremos un servicio gratuito de aprovisionamiento de bases de datos para desarrollo, es mandatorio mitigar exploits, abusos de recursos y ataques maliciosos. El sistema debe implementar y controlar las siguientes directrices:

Control de Seguridad	Nivel de Aplicación	Descripción
Rate Limiting	Backend / Gateway	Limitar la cantidad de peticiones HTTP por minuto por IP/Usuario para evitar denegación de servicio (DoS) en la API de creación de bases de datos.
Límite de Almacenamiento	SQL Server / Backend	Cada base de datos aprovisionada para un estudiante tendrá un peso máximo estricto (ej. 20 MB). Se debe validar este espacio antes de permitir nuevas escrituras.
Conexiones Concurrentes	SQL Server (User Policy)	Restricción estricta de usuarios concurrentes por base de datos aprovisionada para evitar el agotamiento del pool de conexiones del servidor principal.
Prevención de Inyecciones SQL	Backend & DB	Prohibición absoluta de concatenación de cadenas para consultas. El uso de parámetros en los Stored Procedures es obligatorio.
Ciclo de Vida (TTL)	SQL Server / Jobs	Las bases de datos creadas tendrán una duración máxima de actividad. Si no detectan actividad en un periodo determinado, serán pausadas o eliminadas automáticamente mediante tareas programadas.
Estructura y Aprovisionamiento de Subdominios
Para facilitar el despliegue e integración de los proyectos de cada equipo (célula), el sistema ofrecerá un esquema de direccionamiento dinámico basado en el dominio principal andrescortes.dev.

Jerarquía de Direccionamiento
Cada célula tendrá un espacio de nombres aislado tanto para su interfaz de usuario como para sus servicios de backend independientes (permitiendo
N
cantidad de microservicios o APIs):

Frontend de la Célula: https://[nombre_de_la_celula].andrescortes.dev (Ejemplo: https://alpha.andrescortes.dev)
Servicios / Backends (
N
cantidad): https://[nombre_servicio].[nombre_de_la_celula].andrescortes.dev (Ejemplos: https://api.alpha.andrescortes.dev, https://auth.alpha.andrescortes.dev, https://payments.alpha.andrescortes.dev)