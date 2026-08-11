# Integración de PolyService IA

Guía para equipos que necesitan consumir el servicio desde sus propios sistemas.

## 1. Datos principales

| Dato | Valor |
| --- | --- |
| Base URL | `https://ia.polyrepo.andrescortes.dev` |
| Endpoint de modelos | `GET /v1/models` |
| Endpoint de chat | `POST /v1/chat/completions` |
| Modelo | `llama-8b-nvidia` |
| Autenticación | `Authorization: Bearer <API_KEY>` |
| `max_tokens` máximo | `1024` |
| Streaming | No disponible |
| Límite actual | 10 solicitudes/minuto y 100/día por key |

## 2. Obtener una API key

1. Entrar a `https://polyrepo.andrescortes.dev`.
2. Iniciar sesión con Google o GitHub.
3. Abrir el Dashboard.
4. En **Servicio de IA → API keys**, escribir un nombre para la integración.
5. Seleccionar **Crear API key**.
6. Guardar inmediatamente la key en el gestor de secretos del sistema.

La key completa solo se muestra una vez. Si se pierde, se debe rotar desde el dashboard.

Para una integración, se recomienda crear una key separada por ambiente:

```text
mi-sistema-development
mi-sistema-staging
mi-sistema-production
```

## 3. Autenticación

Todas las solicitudes al servicio deben incluir:

```http
Authorization: Bearer <API_KEY>
```

Ejemplo:

```http
Authorization: Bearer pr_ai_...
```

No utilizar la cookie de sesión del portal. La cookie sirve únicamente para administrar las keys desde PolyService.

## 4. Consultar modelos

```bash
curl https://ia.polyrepo.andrescortes.dev/v1/models \
  -H "Authorization: Bearer $POLYSERVICE_AI_KEY"
```

La respuesta debe incluir `llama-8b-nvidia`.

## 5. Consumir el modelo

```bash
curl --fail-with-body \
  https://ia.polyrepo.andrescortes.dev/v1/chat/completions \
  -H "Authorization: Bearer $POLYSERVICE_AI_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model":"llama-8b-nvidia",
    "messages":[
      {"role":"user","content":"Explica qué es una API en una frase"}
    ],
    "max_tokens":128,
    "stream":false
  }'
```

Respuesta resumida:

```json
{
  "id": "chatcmpl-...",
  "model": "llama-8b-nvidia",
  "object": "chat.completion",
  "choices": [
    {
      "index": 0,
      "finish_reason": "stop",
      "message": {
        "role": "assistant",
        "content": "Una API permite que dos sistemas se comuniquen."
      }
    }
  ],
  "usage": {
    "prompt_tokens": 12,
    "completion_tokens": 14,
    "total_tokens": 26
  }
}
```

## 6. Parámetros aceptados

```json
{
  "model": "llama-8b-nvidia",
  "messages": [
    {"role": "system", "content": "Responde de forma concisa."},
    {"role": "user", "content": "Resume este texto."}
  ],
  "max_tokens": 256,
  "stream": false
}
```

- `model` es obligatorio y debe ser `llama-8b-nvidia`.
- `messages` es obligatorio.
- Cada mensaje tiene `role` y `content`.
- `max_tokens` es opcional y acepta valores hasta `1024`.
- `stream` debe omitirse o ser `false`.

## 7. Ejemplo de integración backend

### Python

```python
import os
import requests

API_KEY = os.environ["POLYSERVICE_AI_KEY"]

response = requests.post(
    "https://ia.polyrepo.andrescortes.dev/v1/chat/completions",
    headers={
        "Authorization": f"Bearer {API_KEY}",
        "Content-Type": "application/json",
    },
    json={
        "model": "llama-8b-nvidia",
        "messages": [{
            "role": "user",
            "content": "Genera un resumen de este contenido",
        }],
        "max_tokens": 256,
        "stream": False,
    },
    timeout=30,
)

if response.status_code == 429:
    raise RuntimeError("Se alcanzó la cuota de PolyService IA")

response.raise_for_status()
answer = response.json()["choices"][0]["message"]["content"]
print(answer)
```

### Node.js

```javascript
const response = await fetch(
  'https://ia.polyrepo.andrescortes.dev/v1/chat/completions',
  {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${process.env.POLYSERVICE_AI_KEY}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      model: 'llama-8b-nvidia',
      messages: [{
        role: 'user',
        content: 'Genera una recomendación breve',
      }],
      max_tokens: 256,
      stream: false,
    }),
  },
)

if (!response.ok) {
  throw new Error(`PolyService IA respondió ${response.status}`)
}

const body = await response.json()
console.log(body.choices[0].message.content)
```

### C#/.NET

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

var apiKey = Environment.GetEnvironmentVariable("POLYSERVICE_AI_KEY")
    ?? throw new InvalidOperationException("Falta POLYSERVICE_AI_KEY");

using var client = new HttpClient { BaseAddress = new Uri("https://ia.polyrepo.andrescortes.dev/") };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

using var response = await client.PostAsJsonAsync("v1/chat/completions", new
{
    model = "llama-8b-nvidia",
    messages = new[] { new { role = "user", content = "Di hola en una palabra" } },
    max_tokens = 128,
    stream = false
});

response.EnsureSuccessStatusCode();
var body = await response.Content.ReadFromJsonAsync<JsonElement>();
Console.WriteLine(body.GetProperty("choices")[0].GetProperty("message").GetProperty("content"));
```

## 8. Errores y reintentos

| Código | Acción recomendada |
| ---: | --- |
| `400` | Revisar JSON, modelo, `max_tokens` y `stream`. No reintentar sin corregir. |
| `401` | Revisar que la key exista, esté completa y se envíe como Bearer. |
| `403` | La key fue revocada; generar una nueva desde el portal. |
| `429` | Aplicar backoff y respetar 10 solicitudes/minuto y 100/día. |
| `502` | Reintentar con backoff limitado y reportar si persiste. |
| `504` | Aumentar timeout del cliente dentro de límites razonables y reintentar una vez. |
| `5xx` | No realizar reintentos agresivos; registrar solo estado, latencia y trace de su aplicación. |

No reintentar automáticamente una solicitud no idempotente sin una estrategia propia para evitar respuestas duplicadas.

## 9. Seguridad obligatoria

La API key nunca debe:

- incluirse en React, Vue, Angular, HTML o JavaScript del navegador;
- guardarse en `localStorage` o `sessionStorage`;
- escribirse en logs o mensajes de error;
- incluirse en repositorios, Dockerfiles o archivos `.env` versionados;
- aparecer en una URL, query string o captura de pantalla.

La integración debe ejecutarse desde el backend del equipo consumidor o desde un entorno seguro como un worker, función serverless o job protegido.

Usar una variable de entorno o gestor de secretos:

```bash
export POLYSERVICE_AI_KEY='valor-protegido-en-el-entorno'
```

En producción, preferir Secret Manager, AWS Secrets Manager, Azure Key Vault, Kubernetes Secrets u otra solución equivalente.

Si la key se expone:

1. Revocarla inmediatamente desde el portal.
2. Generar una nueva.
3. Actualizar el gestor de secretos del sistema consumidor.
4. Revisar los logs del equipo consumidor.

## 10. Límites actuales

- Máximo de 3 keys por usuario.
- 10 solicitudes por minuto por API key.
- 100 solicitudes por día por API key.
- Máximo de 1024 tokens de salida por solicitud.
- No se admite streaming.
- Solo está habilitado `llama-8b-nvidia`.

Los límites pueden cambiar. El equipo consumidor debe manejar `429` sin asumir que la cuota es ilimitada.

## 11. Prueba inicial recomendada

1. Crear una key de prueba desde el portal.
2. Ejecutar `GET /v1/models`.
3. Ejecutar una solicitud pequeña con `max_tokens: 64`.
4. Confirmar el campo `usage` de la respuesta.
5. Configurar la key en el gestor de secretos del sistema.
6. Revocar la key de prueba cuando termine la validación.

Contacto técnico: equipo PolyService.
