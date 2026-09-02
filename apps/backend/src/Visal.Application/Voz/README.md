# Modulo de Voz IA (Retell + Telnyx)

Llamadas de voz salientes con un agente de IA (Retell AI) sobre telefonia Telnyx,
integradas al modulo **Seguimiento** (encuesta SIAU de satisfaccion). El coordinador
lanza en lote llamadas a los pacientes pendientes; el agente hace la encuesta y, via
webhook, se actualiza la tarjeta de Seguimiento con el resultado y la transcripcion.

## Piezas

- `IRetellConfig` / `RetellConfig` (Infra): lee la config de variables de entorno.
- `IRetellClient` / `RetellHttpClient` (Infra): cliente HTTP (crear/consultar llamada).
- `IVozLlamadaService` / `VozLlamadaService`: orquesta el lote y procesa el webhook.
- `RetellWebhookParser`: parseo defensivo del cuerpo del webhook.
- `TelefonoE164`: normaliza telefonos a E.164.
- Entidad `LlamadaVoz` (tabla `llamadas_voz`): bitacora de cada llamada.
- Webhook: `POST /webhooks/retell/{token}` en `Visal.SuperAdmin/Program.cs`.
- UI: boton "Llamar a pendientes (IA)" + "Simular" en `/seguimiento`.

## Configuracion (NUNCA en codigo)

Variables de entorno / user-secrets. **Ningun secreto se versiona.**

| Variable | Descripcion |
|---|---|
| `RETELL_API_KEY` | API key de Retell (dashboard -> API Keys). |
| `RETELL_AGENT_ID` | Id del agente de voz que habla. |
| `RETELL_FROM_NUMBER` | Numero Telnyx ya importado en Retell, en E.164 (`+57...`). |
| `RETELL_WEBHOOK_TOKEN` | Token opaco para la ruta del webhook. Generalo tu (ej. GUID). |
| `RETELL_TELNYX_SIP_USERNAME` | (Opcional) username del trunk Telnyx. Si esta, se envia como header `X-Telnyx-Username` en `custom_sip_headers`. |

### Cargarlas en desarrollo (user-secrets)

```bash
cd apps/backend/src/Visal.SuperAdmin
dotnet user-secrets set "RETELL_API_KEY" "<tu-key>"
dotnet user-secrets set "RETELL_AGENT_ID" "<tu-agent-id>"
dotnet user-secrets set "RETELL_FROM_NUMBER" "+57XXXXXXXXXX"
dotnet user-secrets set "RETELL_WEBHOOK_TOKEN" "<un-token-aleatorio>"
# opcional:
dotnet user-secrets set "RETELL_TELNYX_SIP_USERNAME" "<telnyx-username>"
```

En produccion: variables de entorno del contenedor (docker `.env`), nunca en la imagen.

### Webhook en el dashboard de Retell

Configura la URL del webhook del agente/cuenta apuntando a:

```
https://<tu-dominio>/webhooks/retell/<RETELL_WEBHOOK_TOKEN>
```

El endpoint valida el token de la ruta y responde 200 siempre (para no gatillar
reintentos). Eventos consumidos: `call_started`, `call_ended`, `call_analyzed`.

> Firma `x-retell-signature`: hoy el endpoint se protege con el token opaco de la
> ruta (mismo patron que el webhook de Gupshup). La verificacion HMAC del header
> `x-retell-signature` se puede añadir cuando se confirme el esquema exacto.

## Como probar SIN gastar

- **Unit tests** (mocks, cero costo): `dotnet test --filter FullyQualifiedName~Voz`.
  Cubren cliente HTTP (exito/401/500/excepcion, sin reintento en create), parseo de
  webhook y normalizacion E.164. **Ningun test hace llamadas reales.**
- **Simular** en `/seguimiento`: el boton "Simular" valida cuantas llamadas se
  harian (telefonos validos, no duplicadas) **sin llamar**.

## El unico paso que CUESTA

El boton **"Llamar a pendientes (IA)"** en `/seguimiento` dispara **llamadas
telefonicas reales** (con confirmacion previa que lo advierte). Cada llamada tiene
costo de Retell + Telnyx. Nunca se reintenta una llamada a ciegas.

## Troubleshooting

- **No timbra / error de auth SIP**: revisa que el numero este importado en Retell
  y que, si Telnyx lo exige, `RETELL_TELNYX_SIP_USERNAME` este configurado (viaja
  como `X-Telnyx-Username`).
- **Webhook no llega**: verifica la URL + token en el dashboard de Retell y que el
  dominio sea publico (ngrok en dev). El endpoint responde 401 si el token no coincide.
- **Retell rechaza la key (401)**: revisa `RETELL_API_KEY`.
- **422 al crear**: numeros no E.164 o agente invalido; revisa `RETELL_FROM_NUMBER`.
