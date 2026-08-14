
API para células socias — guía de integración
Para: equipos de otras células que van a consumir aprovisionamiento de bases MySQL a través de nuestra plataforma.

Qué es esto
Un endpoint HTTP para que el backend de tu célula (nunca tu frontend) pida bases de datos MySQL para tus propios usuarios finales, de la misma forma en que nosotros mismos consumimos SQL Server como servicio externo. Vos nunca tocás MySQL directo ni ProxySQL — nosotros hacemos el aprovisionamiento real y te devolvemos las credenciales.

Usuario final
Pide una base desde tu app
→
Tu backend
Llama a nuestra API con tu API key
→
Nuestra plataforma
Aprovisiona en MySQL real y devuelve credenciales
→
Tu usuario final
Se conecta directo con DBeaver, Workbench, etc.
El request server-to-server real, en detalle:

Request real
POST https://api.aba.andrescortes.dev/partners/databases
Authorization: Bearer <tu-api-key></tu>

Requisito de seguridad no negociable
No negociable
La API key la usa exclusivamente tu backend, nunca tu frontend ni el navegador del usuario final. Si la key queda expuesta en el navegador de cualquiera de tus usuarios, cualquiera puede aprovisionar y eliminar bases a tu nombre. Tratala como tratarías la contraseña de tu propia base de datos.

Autenticación
Cada request lleva la key en el header estándar:

Header
Authorization: Bearer <tu-api-key></tu>

Si falta el header, es inválido, o la key no existe/está desactivada → 401 Unauthorized con un mensaje genérico. No distinguimos el motivo exacto en la respuesta (por seguridad — evita darle pistas a quien esté probando keys al azar).
Una vez autenticado, las respuestas de error sí usan el nombre real de tu célula (por ejemplo, "La célula 'alpha' alcanzó el límite de bases de datos activas."), porque a ese punto ya confirmamos que sos vos.
Base URL
Base URL
https://api.aba.andrescortes.dev

Endpoints
POST
/partners/databases
Crear una base
Sin body. El motor siempre es MySQL (es el único que ofrecemos para células socias).

curl
curl -X POST https://api.aba.andrescortes.dev/partners/databases 
  -H "Authorization: Bearer <tu-api-key></tu>"

Respuesta 201 Created:

201 Created
{
  "baseDeDatosId": 42,
  "nombreBD": "alpha_7f3a9c1e2b",
  "usuarioBD": "alpha_7f3a9c1e",
  "passwordTemporal": "K9#mQ2xR7pL4vN8w",
  "host": "db.aba.andrescortes.dev",
  "puerto": 3306,
  "motor": "MySQL"
}

Se entrega una sola vez
passwordTemporal se entrega UNA SOLA VEZ, en esta respuesta. No hay endpoint para recuperarla después — nosotros tampoco podemos verla (queda cifrada de nuestro lado). Guardala en tu propia base inmediatamente.

Guardá el baseDeDatosId — no hay otra forma de recuperarlo
Guardá baseDeDatosId asociado a cuál de tus usuarios finales la pidió. Nosotros no tenemos ninguna noción de tus usuarios finales — es tu propia base de auth, completamente separada de la nuestra. Si no guardás esa asociación en el momento de crear, no hay forma de recuperarla después buscando por tu usuario: nuestro único endpoint de búsqueda es por id, y listar tus bases te devuelve todas pero sin ningún dato que las vincule a cuál de tus usuarios la pidió. Es el error más común al integrar — guardar las credenciales y olvidarse del id.

El nombre de base y de usuario siempre empiezan con tu prefijo asignado (alpha_ en el ejemplo) — vos nunca elegís el nombre, lo genera el sistema para garantizar que no choque con el de otra célula.

Tu usuario final se conecta directo con esas credenciales, desde cualquier gestor de bases de datos (DBeaver, MySQL Workbench, línea de comandos, lo que uses) — no hay restricción geográfica ni de herramienta.

Errores posibles:

HTTP	Causa
401	API key inválida, ausente, o desactivada.
409	Alcanzaste el límite de bases activas para tu célula (hoy 500, contactanos si lo necesitás más alto).
422	Error de aprovisionamiento en nuestro lado (raro; reintentá).
503	El motor MySQL falló al crear la base — reintentá en unos minutos.
GET
/partners/databases
Listar tus bases
Todas las bases de tu célula, en cualquier estado (incluye las que ya eliminaste — las guardamos para auditoría, nunca se borran de verdad de nuestro lado, ver la sección de eliminar más abajo). Pensado para que gestiones vos mismo el ciclo de vida completo sin escribirnos: crear, revisar cuáles tenés activas, regenerar credenciales, dar de baja las que tus usuarios finales pidan.

curl
curl https://api.aba.andrescortes.dev/partners/databases 
  -H "Authorization: Bearer <tu-api-key></tu>"

Respuesta 200 OK:

200 OK
[
  {
    "id": 42,
    "nombreBD": "alpha_7f3a9c1e2b",
    "usuarioBD": "alpha_7f3a9c1e",
    "host": "db.aba.andrescortes.dev",
    "puerto": 3306,
    "estado": "ACTIVA",
    "espacioMaximoMB": 20,
    "espacioUtilizadoMB": 17.8,
    "porcentajeUsado": 89,
    "fechaCreacion": "2026-08-06T22:15:00Z"
  },
  {
    "id": 37,
    "nombreBD": "alpha_1a2b3c4d5e",
    "usuarioBD": "alpha_1a2b3c4d",
    "host": "db.aba.andrescortes.dev",
    "puerto": 3306,
    "estado": "ELIMINADA",
    "espacioMaximoMB": 20,
    "espacioUtilizadoMB": 0,
    "porcentajeUsado": 0,
    "fechaCreacion": "2026-07-30T10:02:11Z"
  }
]

Sin paginación por ahora (el límite de 500 bases activas por célula lo hace innecesario en la práctica) — si tu volumen real lo necesita, avisanos.

Cuota de espacio y el estado PAUSADA
espacioMaximoMB / espacioUtilizadoMB reflejan el uso real medido cada 10 minutos contra el motor (no un valor aproximado). porcentajeUsado es el mismo dato ya calculado — usalo para tu propia alerta ("se está por llenar") en vez de hacer la cuenta vos mismo con espacioMaximoMB, por si ese límite cambia más adelante.

Bloqueo y reactivación automáticos
Si una base supera espacioMaximoMB, pasa automáticamente a PAUSADA y le revocamos el permiso de escritura real en MySQL — tu usuario final puede seguir leyendo, pero cualquier INSERT/UPDATE falla del lado del motor hasta que el uso vuelva a bajar del límite, momento en el que el propio job la reactiva sola (volvés a ver ACTIVA). No hace falta que llames a ningún endpoint para pausar o reactivar — es automático de nuestro lado.

No lo uses como gate de cada escritura
El rate limit (ver § Rate limit) es compartido con crear/listar/eliminar bases y se agota rápido si consultás el espacio antes de cada escritura de tu usuario final. Lo pensado es que polees este endpoint (o el de consultar una base) cada tanto — minutos u horas, según te convenga — para reflejar el uso en tu propio dashboard.

GET
/partners/databases/{id}
Consultar una base
curl
curl https://api.aba.andrescortes.dev/partners/databases/42 
  -H "Authorization: Bearer <tu-api-key></tu>"

Respuesta 200 OK:

200 OK
{
  "id": 42,
  "nombreBD": "alpha_7f3a9c1e2b",
  "usuarioBD": "alpha_7f3a9c1e",
  "host": "db.aba.andrescortes.dev",
  "puerto": 3306,
  "estado": "ACTIVA",
  "espacioMaximoMB": 20,
  "espacioUtilizadoMB": 3.4,
  "porcentajeUsado": 17,
  "fechaCreacion": "2026-08-06T22:15:00Z"
}

No incluye la contraseña (esa solo se entrega una vez, al crear). estado es uno de PENDIENTE · ACTIVA · PAUSADA · ELIMINADA — ver Cuota de espacio y el estado PAUSADA para qué significa PAUSADA y cómo se sale de ese estado.

404 Not Found si el id no existe o no es tuyo (nunca podés ver bases de otra célula).

DELETE
/partners/databases/{id}
Eliminar una base
Para cuando uno de tus usuarios se da de baja. Esto borra la base de datos de verdad (DROP DATABASE + el usuario MySQL) — no es un soft-delete de MySQL, así que no hay vuelta atrás del lado del motor. De nuestro lado en ABA_Control el registro queda marcado ELIMINADA para auditoría — nunca se borra la fila.

curl
curl -X DELETE https://api.aba.andrescortes.dev/partners/databases/42 
  -H "Authorization: Bearer <tu-api-key></tu>"

204 No Content si se borró. 404 Not Found si el id no existe, no es tuyo, o ya estaba borrado.

POST
/partners/databases/{id}/credenciales/reset
Rotar la contraseña
Para cuando perdiste o filtraste el passwordTemporal que te dimos al crear (que, recordá, se entrega una sola vez). Genera una contraseña nueva y la aplica de una — no hace falta borrar y recrear la base para recuperar el acceso.

curl
curl -X POST https://api.aba.andrescortes.dev/partners/databases/42/credenciales/reset 
  -H "Authorization: Bearer <tu-api-key></tu>"

Respuesta 200 OK:

200 OK
{
  "baseDeDatosId": 42,
  "nombreBD": "alpha_7f3a9c1e2b",
  "usuarioBD": "alpha_7f3a9c1e",
  "passwordNueva": "T4#kR9mL2xQ7vN3w",
  "host": "db.aba.andrescortes.dev",
  "puerto": 3306
}

Se entrega una sola vez
passwordNueva también se entrega UNA SOLA VEZ — mismo criterio que la contraseña original. La contraseña vieja deja de servir apenas esta operación responde 200; actualizá tu propia base inmediatamente.

Errores posibles:

HTTP	Causa
401	API key inválida, ausente, o desactivada.
404	El id no existe, no es tuyo, o la base no está ACTIVA (p.ej. sigue PENDIENTE o ya fue eliminada).
503	El motor MySQL falló al aplicar la contraseña nueva — reintentá; es seguro (la operación es idempotente: cada reintento genera y sincroniza una contraseña nueva otra vez).
Rate limit
10 requests de ráfaga, recarga de 1 cada 2 minutos, por célula (no por IP — identificado por tu API key). Si lo superás, 429 Too Many Requests. Alcanza de sobra para operar normalmente (crear/listar/eliminar bases + consultar el estado de cuota de vez en cuando, ver Cuota de espacio y el estado PAUSADA) — avisanos si tu volumen real lo necesita más alto y lo ajustamos.

Lo que todavía no existe
Whitelist de IP opcional (mencionada en el diseño original): no implementada. Tus bases no tienen restricción de IP de origen — cualquiera con la contraseña correcta se conecta desde donde sea. Si tu caso de uso la necesita, avisanos antes de que la construyamos, para priorizarla bien.
PUT /partners/databases/{id}/ip-whitelist: no existe todavía (depende de lo anterior).
Endpoint para regenerar tu propia API key: no es self-service todavía — si la tuya se compromete o la perdés, escribinos y te generamos una nueva (ver siguiente sección). La vieja deja de funcionar apenas se aplica el cambio.
Si tu API key se compromete o la perdés
Escribinos y te generamos una nueva de nuestro lado. No podemos recuperar la que ya tenías — guardamos solo su hash, nunca el valor en texto plano — así que si la perdiste, rotar es la única salida. Coordiná con nosotros el momento del cambio para no tener un corte de servicio inesperado.
