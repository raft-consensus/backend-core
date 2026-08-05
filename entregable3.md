# Entregable 3: Automatización, Inteligencia Artificial y Gestión de DNS

📅 **Fecha de Entrega:** Jueves 6 de agosto

Este entregable amplía la plataforma con tres nuevos servicios de autoservicio para los usuarios finales: **creación de usuarios/instancias de N8N**, **Inteligencia Artificial expuesta como servicio vía API con API-Key**, y **creación de registros DNS** para que los propios usuarios los utilicen.

---

## 1. Creación de Usuarios de N8N

La plataforma debe permitir que **el propio usuario cree su usuario/instancia de N8N** de forma autogestionada, tal como hoy puede aprovisionar su base de datos.

- **Flujo de autoservicio:** Desde el panel de la plataforma, el usuario debe poder solicitar la creación de su cuenta/workspace de N8N sin intervención manual del equipo.
- **Aprovisionamiento:** Definir si cada usuario tendrá su propio workspace dentro de una instancia compartida de N8N, o una instancia aislada (contenedor propio). Documentar y justificar la decisión.
- **Credenciales:** Al crear el usuario, la plataforma debe entregarle sus credenciales de acceso (o un enlace de acceso seguro) a su espacio de N8N.
- **Límites y control:** Definir límites razonables por usuario (número de workflows, ejecuciones, almacenamiento) para evitar abuso de recursos.
- **Documentación:** Documentar en Docusaurus el flujo completo de creación de usuario de N8N y cómo se gestionan sus permisos.

---

## 2. Inteligencia Artificial como Servicio (API + API-Key)

La IA no debe integrarse solo como una funcionalidad interna, sino **exponerse como un servicio propio de la plataforma**, consumible por los usuarios mediante su propia API.

- **API propia:** Construir un endpoint (o conjunto de endpoints) que exponga la funcionalidad de IA (por ejemplo, generación/depuración de consultas SQL, resúmenes, recomendaciones, etc.) para que los usuarios la integren en sus propios proyectos.
- **Autenticación por API-Key:** Cada usuario debe poder **generar su propia API-Key** desde la plataforma para autenticar sus llamadas al servicio de IA.
- **Gestión de claves:** Se debe permitir crear, revocar y regenerar API-Keys, y estas nunca deben quedar expuestas en logs ni en el código fuente.
- **Control de uso:** Registrar el consumo por API-Key (número de solicitudes, tokens/costos aproximados) para poder aplicar límites o cuotas por usuario.
- **Seguridad:** Las solicitudes sin una API-Key válida deben ser rechazadas. Se recomienda aplicar rate limiting por clave para evitar abuso.
- **Documentación:** Documentar en Docusaurus cómo generar una API-Key, los endpoints disponibles, parámetros de entrada/salida y ejemplos de uso.

---

## 3. Creación de Registros DNS por parte de los Usuarios

La gestión de DNS bajo el dominio `coderhivex.com` no debe quedar solo en manos del equipo: **los usuarios deben poder crear sus propios registros/subdominios** para sus proyectos desde la plataforma.

- **Autoservicio de subdominios:** El usuario debe poder solicitar, desde el panel de la plataforma, un subdominio propio que apunte a su servicio o aplicación, siguiendo la estructura:

  ```
  [nombre-elegido-por-el-usuario].[nombre_de_la_celula].coderhivex.com
  ```

  Por ejemplo, si el usuario está desplegando su propia instancia de Airflow, podría solicitar el subdominio `airflow.[nombre_de_la_celula].coderhivex.com`, donde **"airflow" es el nombre que el propio usuario define** al momento de crear el registro (no un valor fijo del sistema).
- **Automatización:** Este proceso debe integrarse con la API del proveedor DNS, de forma que la creación del registro (tipo A, CNAME, etc.) se realice automáticamente al momento de la solicitud, sin intervención manual.
- **Validaciones:** Evitar colisiones de nombres, validar que el subdominio no esté en uso y limitar la cantidad de subdominios que un usuario puede crear.
- **HTTPS:** El subdominio creado debe quedar con certificado SSL/TLS válido de forma automática o guiada.
- **Control administrativo:** El equipo debe poder auditar, listar y revocar/eliminar registros DNS creados por los usuarios cuando sea necesario (por ejemplo, por inactividad o abuso).
- **Documentación:** Documentar en Docusaurus el flujo de creación de subdominios por parte del usuario y el procedimiento de administración/revocación por parte del equipo.

---

## 4. Entregables

Cada equipo deberá entregar, **desplegado en la VPS y documentado en Docusaurus**:

- Evidencia funcional del flujo de creación de usuarios/instancias de N8N desde la plataforma.
- Evidencia del servicio de IA expuesto vía API, incluyendo la generación de una API-Key y una solicitud de ejemplo consumiendo el servicio.
- Evidencia del flujo de creación de un subdominio DNS por parte de un usuario, incluyendo la validación de su correcta propagación y HTTPS.

---

## 5. Publicación en LinkedIn

Todo el equipo debe publicar en LinkedIn un post contando el avance de este entregable (qué se construyó, qué aprendieron, capturas o demo del resultado).

> La publicación **debe etiquetar obligatoriamente** a los Team Leaders: **Janner** y **Robinson**.
> Cada integrante de la célula debe realizar **su propia publicación** (no basta con que un solo miembro publique por el equipo).