# Planes DB — esquema operativo de SQL Server compartido

Este documento describe cómo funciona el modelo real de Raft, que es la aplicación de esta célula, y cómo se integra con el SQL Server compartido de la VPS. Las demás células tienen sus propios nombres, sus propios backends y sus propios contratos.

La idea base es esta:

- la VPS de ustedes hospeda un contenedor de SQL Server compartido;
- Raft es la app de esta célula;
- cada equipo tiene su propio backend y su propia interfaz;
- todos los backends se conectan al SQL Server compartido usando credenciales separadas;
- la lógica de negocio central vive en stored procedures, vistas y funciones dentro de ese SQL Server;
- cada base aprovisionada para un usuario o un equipo queda aislada del resto por permisos y configuración.

## 1. Qué existe en la infraestructura

En la VPS deben existir, como mínimo, estos componentes:

- SQL Server compartido;
- almacenamiento persistente para los datos;
- red pública o privada accesible desde los backends autorizados;
- un backend por equipo, desplegado por separado.

La VPS no crea la aplicación por sí sola. Solo hospeda los servicios.

## 2. Quién usa qué

### Backends de los equipos

Cada equipo tiene su propio backend.

Ese backend:

- se autentica contra el SQL Server compartido;
- llama a los stored procedures del core;
- puede aprovisionar bases de datos, credenciales y metadatos si tiene permiso para hacerlo;
- no debe usar `sa` ni permisos globales innecesarios.

### Usuarios finales

Los usuarios finales no deberían tener acceso al SQL Server compartido salvo que el diseño del equipo lo requiera explícitamente.

Normalmente:

- entran al frontend;
- el frontend llama al backend de su equipo;
- el backend de su equipo ejecuta la operación contra SQL Server;
- el usuario recibe su base o sus credenciales desde la aplicación.

## 3. Flujo actual y extensibilidad futura

Flujo actual de Raft:

- el core operativo vive en SQL Server compartido;
- el backend de Raft crea, consulta y administra bases SQL Server;
- el aprovisionamiento ocurre de forma explícita y no durante login ni registro;
- los usuarios finales trabajan sobre sus propias bases asignadas.

Extensibilidad futura:

- otras células pueden exponer MySQL, PostgreSQL u otros motores;
- Raft puede coordinar solicitudes hacia esas células cuando el producto lo requiera;
- la misma regla de aislamiento aplica: cada usuario solo ve sus propias bases.

## 4. Esquema de aislamiento entre equipos

La separación correcta entre equipos se hace con:

- logins distintos;
- usuarios distintos dentro de cada base;
- permisos mínimos;
- `TRUSTWORTHY` desactivado;
- `cross db ownership chaining` desactivado;
- ausencia de permisos cruzados entre bases.

La regla es simple:

- el equipo A solo debe poder operar sobre su base o sobre los objetos del core que le correspondan;
- el equipo B no debe poder ver ni modificar la base del equipo A;
- si una base pertenece a un cliente o usuario final, solo el backend autorizado debe poder acceder a ella.

## 5. Flujo general de conexión

Cada backend de equipo se conecta al SQL Server compartido con su propia cadena de conexión.

Ejemplo:

```json
{
  "ConnectionStrings": {
    "RaftDb": "Server=<vps-host>,1433;Database=RaftDb;User Id=<team-login>;Password=<password>;TrustServerCertificate=True;"
  }
}
```

Lo que cambia por equipo es:

- el host, si aplica;
- el usuario;
- la contraseña.

Lo que no debería cambiar si comparten el mismo core:

- el nombre lógico del catálogo central;
- la forma de llamar a los stored procedures;
- el contrato funcional.

## 6. Logins recomendados

### Login por equipo

Lo recomendado es crear un login por backend/equipo.

Ejemplos:

- `raft_team_a_backend`
- `raft_team_b_backend`
- `raft_team_c_backend`

Ventajas:

- aislamiento claro;
- mejor auditoría;
- revocación independiente;
- menos riesgo de mezcla entre equipos.

### Login de aprovisionamiento

Si hace falta separar tareas de creación y mantenimiento, conviene tener un login técnico adicional:

- `raft_team_provisioner`

Este login debe tener permisos acotados para crear bases y objetos necesarios, pero no permisos globales de administración.

### Login de monitoreo

Si alguna herramienta necesita solo lectura:

- `raft_monitor`

Debe tener únicamente permisos de lectura sobre vistas o consultas autorizadas.

## 7. Permisos mínimos

### Para un backend normal

El backend debería tener:

- `CONNECT SQL`
- `EXECUTE` sobre los stored procedures permitidos
- `SELECT` solo sobre vistas de lectura si es necesario

No debería tener:

- `sysadmin`
- `db_owner`
- `db_ddladmin`
- `ALTER ANY LOGIN`
- `ALTER ANY DATABASE`
- `CONTROL SERVER`
- permisos directos amplios sobre tablas sensibles

### Para crear bases

Si una célula necesita crear bases de datos:

- dale el permiso mínimo necesario para crear;
- no le des permisos para eliminar lo que no le corresponde;
- centraliza la eliminación en el backend o proceso que controle el lifecycle.

### Para no ver la base de otro equipo

La regla práctica es:

- no crear usuario en la base ajena;
- no dar permisos cruzados;
- no habilitar trust cruzado;
- no permitir cadenas de propiedad entre bases salvo necesidad estricta.

## 8. Cómo se aísla una base del equipo A frente al equipo B

Supongamos:

- `RaftTeamA_DB`
- `RaftTeamB_DB`

El backend del equipo A solo debe tener usuario y permisos en `RaftTeamA_DB`.
El backend del equipo B solo debe tener usuario y permisos en `RaftTeamB_DB`.

En `RaftTeamA_DB` no debe existir usuario mapeado al login del equipo B.
En `RaftTeamB_DB` no debe existir usuario mapeado al login del equipo A.

Si eso se cumple, el equipo B no puede ver ni modificar la base del equipo A por la vía normal de autenticación/autorización.

## 9. Qué pasa con los stored procedures

Hay dos familias de SP:

### SP del core compartido

Estos viven en el SQL Server compartido y los usan los backends autorizados:

- usuarios;
- instancias;
- credenciales;
- auditoría;
- métricas;
- lifecycle;
- provisioning.

### SP de una base aprovisionada

Estos pertenecen a la base de un usuario o de una aplicación concreta.

- tablas del negocio;
- SPs de negocio de esa aplicación;
- vistas internas;
- funciones de cálculo.

No hay que mezclar ambas cosas.

## 10. Provisión de una nueva base

El flujo recomendado es:

1. el usuario entra al frontend;
2. el frontend llama al backend de su equipo;
3. el backend valida la sesión;
4. el backend consulta el core compartido;
5. el backend crea la base o solicita al servicio de provisioning que la cree;
6. se crea el login o usuario de esa base, según el modelo definido;
7. se guardan los metadatos en el core compartido;
8. se devuelve la conexión al usuario.

Este flujo no debe ocurrir durante login ni registro.
Debe ser una acción explícita de autoservicio.

## 11. Eliminación y lifecycle

La eliminación no debería quedar abierta al backend de cada equipo sin control.

Recomendación:

- el backend puede pedir eliminación;
- el core o el proceso de lifecycle valida si corresponde;
- el core ejecuta la eliminación real;
- luego se marca la instancia como eliminada o inactiva.

Así se evita que un equipo borre recursos que no le pertenecen.

## 12. Reglas de seguridad mínimas

- `TRUSTWORTHY` en OFF;
- `cross db ownership chaining` en OFF;
- sin `sa` en aplicaciones;
- sin usuarios compartidos entre equipos;
- con cuentas separadas por backend;
- con permisos mínimos;
- con auditoría de acciones críticas;
- con rate limiting en la API;
- con contraseñas fuertes y rotables;
- con HTTPS para toda comunicación externa.

## 12.1. Identidad del usuario final y aislamiento entre sus bases

Si un usuario final usa la misma identidad para acceder a varias bases propias, esa identidad solo debe existir dentro de las bases que le pertenecen.

La regla es esta:

- el login o usuario final puede reutilizarse en todas las bases del mismo dueño;
- ese mismo login no debe existir en bases de otros usuarios;
- si el login no existe dentro de una base, no puede entrar a ella;
- el backend o el proceso de provisioning debe crear el usuario solo en las bases autorizadas;
- las bases ajenas no deben tener usuarios ni permisos mapeados a esa identidad.

Ejemplo práctico:

- `raft_user_001` puede existir en `RaftUser001_Db` y en otras bases del mismo usuario;
- `raft_user_001` no debe existir en `RaftUser002_Db`;
- por eso el usuario 001 no puede ver ni modificar la base del usuario 002 por la vía normal de autenticación/autorización.

Este patrón permite que un usuario tenga una sola identidad de acceso sin perder aislamiento entre sus bases y las bases de otros usuarios.

## 12.2. Caso de otras células: MySQL, PostgreSQL u otros motores

Cuando un usuario de Raft crea una base en otro motor administrado por otra célula, el principio es el mismo: la identidad del usuario solo debe existir dentro de las bases que le pertenecen.

Flujo esperado:

1. El usuario pide crear una base MySQL, PostgreSQL u otra soportada por una célula externa.
2. Raft recibe la solicitud y la envía al backend de la célula responsable de ese motor.
3. Ese backend crea la base y el usuario/credencial correspondiente si aplica.
4. La identidad creada solo queda asociada a las bases de ese mismo usuario.
5. Esa identidad no debe existir en las bases de otros usuarios.

Regla de aislamiento:

- un usuario de Raft puede tener acceso a sus bases MySQL/PostgreSQL propias;
- el mismo usuario no debe aparecer en las bases de otros clientes;
- la célula que administra ese motor es la que materializa los permisos;
- Raft solo coordina, registra y expone el resultado al usuario final.

Ejemplo:

- `mysql_user_001` puede existir en `mysql_db_001_a` y `mysql_db_001_b`;
- `mysql_user_001` no debe existir en `mysql_db_002_a`;
- por eso el usuario 001 no puede ver ni modificar las bases del usuario 002.

Este mismo criterio aplica para PostgreSQL u otros motores que se integren más adelante.

## 13. Resumen corto

Lo que necesitas para que esto funcione es:

- un SQL Server compartido en la VPS;
- un login por equipo/backend;
- usuarios mapeados por base;
- permisos mínimos;
- cero acceso cruzado entre bases de distintos equipos;
- stored procedures para el core;
- lifecycle centralizado para creación, pausa y eliminación.

Ese es el modelo correcto para que el equipo A no vea ni modifique la base del equipo B.

## 14. Qué deben otorgarnos los equipos de otras células

Si otros equipos van a usar la VPS compartida o si Raft va a crearles usuarios y bases dentro del SQL Server compartido, ellos deben darnos explícitamente los datos y permisos mínimos para operar.

### Lo que deben entregarnos

- el nombre de la célula o equipo;
- el nombre lógico de su backend;
- el host o dominio al que debe apuntar su backend;
- el puerto del SQL Server o del motor que correspondan;
- el nombre del catálogo o base que van a usar;
- el login técnico que autorizan para su integración;
- la política de permisos que aceptan para ese login;
- si van a usar una base por usuario o una base por aplicación;
- si el acceso será directo por SQL Server o mediado por su backend.

### Lo que deben permitirnos crear

Para que Raft pueda crear usuarios para otras células, normalmente necesitamos que nos autoricen a:

- crear un login técnico para esa célula;
- crear el usuario de base de datos asociado a ese login;
- asignar permisos solo sobre los SPs, vistas o esquemas aprobados;
- registrar la instancia o recurso dentro del core compartido si aplica;
- crear credenciales de acceso para esa célula, si su flujo lo requiere;
- auditar la creación y el uso de ese acceso.

### Lo que no deberían otorgarnos

No deberían darnos:

- `sa`;
- `sysadmin`;
- `CONTROL SERVER`;
- `db_owner` sobre sus bases privadas, salvo justificación explícita;
- permisos para ver o modificar bases de otras células;
- acceso cruzado sin trazabilidad;
- permisos para borrar recursos ajenos sin validación.

### Regla de operación entre células

La idea no es que todas las células tengan acceso total entre sí. La idea es que cada célula publique un contrato mínimo y nosotros consumamos solo ese contrato.

Si una célula nos da acceso a su SQL Server o a su backend, debe ser con un usuario técnico acotado y con el alcance mínimo necesario para el flujo que quieran habilitar.

### Ejemplo de acuerdo mínimo

Para una célula externa que solo necesita que se le creen bases o usuarios:

- login técnico propio;
- permisos para crear lo necesario en su propio scope;
- sin acceso a datos de otras células;
- sin permisos de borrado global;
- sin privilegios administrativos completos.

### Regla final

Cada célula debe darnos lo suficiente para operar su integración, pero nunca más de lo necesario para evitar que una integración comprometida afecte al resto del sistema.
