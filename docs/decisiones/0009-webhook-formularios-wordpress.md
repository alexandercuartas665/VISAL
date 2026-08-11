# ADR-0009: Webhook publico de formularios web (WordPress) -> tarjeta PQRS

**Fecha:** 2026-08-07
**Estado:** Aceptado
**Corresponde a:** Intake de "Contactanos" y "PQRS" del sitio ipsvisalrt.com hacia el embudo del tenant VISAL RT. Reutiliza el modelo Lead/PipelineStage y complementa el intake existente `POST /api/public/leads`.

## Contexto

El sitio WordPress (Elementor Forms) debe enviar cada envio de dos formularios ("Contactanos" y
"PQRS") a VISAL por HTTP, y VISAL debe crear con cada envio una **tarjeta (Lead)** en el embudo del
tenant, etiquetada/enrutada como **PQRS**.

Restricciones del entorno existente:

1. Ya existe un intake publico de leads (`POST /api/public/leads`) autenticado por header
   `X-Api-Key` (resuelve el tenant contra `TenantApiConfig.ApiKeyHash`), pero **el webhook de
   Elementor no envia headers custom de forma fiable**: necesitamos el secreto en la URL.
2. No existen "tableros" con nombre; hay **un solo embudo por tenant** (`PipelineStage` ordenadas por
   `SortOrder`). "PQRS" no era representable como tablero independiente.
3. `CreateLeadAsync` siempre usaba la **primera** etapa por `SortOrder` y no aceptaba destino.
4. El Lead tiene campos nativos (`ContactName` obligatorio, `ContactPhone`, `Destination`,
   `EstimatedValue`, `Currency`); no hay campo nativo para email ni mensaje: van en campos
   configurables (`PipelineFieldDefinition` + `Lead.FieldValuesJson`). Claves reservadas que no
   pueden usarse como `FieldKey`: `nombre, telefono, destino, valor, moneda`.

## Decision

Se adopta la **Opcion B**: enrutar la tarjeta a una etapa **PQRS** del embudo existente, sin construir
un modulo de tableros nuevo.

1. **Endpoint nuevo con token EN LA URL** en `Visal.SuperAdmin/Program.cs`:
   `POST /webhooks/formularios/{token}` (`.AllowAnonymous().DisableAntiforgery()`). Lee el cuerpo
   crudo y acepta `application/x-www-form-urlencoded` (Elementor) **y** `application/json`. El token
   **no se loggea**. Rate limiting basico: ventana fija por IP (60/min) con `IMemoryCache`.

2. **Token dedicado por tenant** (no se reutiliza el API key de leads). Nueva entidad
   `TenantFormWebhookConfig` (global, lleva `TenantId` pero no es `ITenantScoped`; espeja
   `TenantApiConfig` y `WhatsAppLine.InboundToken`): `TokenHash` (SHA-256 hex, para resolver el
   tenant), `TokenEncrypted` (ISecretProtector, para mostrarlo en Mi cuenta), `IsEnabled`,
   `LastUsedAt`. Prefijo del token: `vfw_`. **Motivo de no reutilizar el API key de leads:** el
   secreto de la URL queda en logs de acceso y en la config de WordPress; separarlo del header
   `X-Api-Key` permite rotarlo de forma independiente sin romper el otro intake. Se genera/muestra
   en **Mi cuenta** (`/mi-cuenta`) junto al API key, con boton Copiar de la URL completa.

3. **Etapa PQRS ensure-on-first-use.** En la primera recepcion se crea (idempotente, sin GUIDs por
   entorno) la `PipelineStage` "PQRS" con `SortOrder` alto (para no volverse la etapa por defecto) y
   sus `PipelineFieldDefinition`: `email` (Text), `asunto` (Text), `mensaje` (TextArea), `tipo`
   (Text: pqrs|contacto), `pagina_origen` (Text). `telefono` usa el campo nativo `ContactPhone`
   (clave reservada). Ambos formularios caen en la MISMA etapa PQRS, diferenciados por `tipo`.

4. **Ruteo por etapa destino.** Se extiende `ApiCreateLeadRequest` con `StageName` opcional (por
   nombre, case-insensitive) y `CreateLeadAsync` lo respeta; si es null cae a la primera etapa
   (comportamiento historico de `/api/public/leads`, no se rompe).

4b. **Etapa destino configurable por el tenant.** En "Configuracion de Empresa" (`/cfg-empresa`,
   tarjeta "Formularios web") el admin elige a que etapa del embudo cae cada tipo: PQRS y Contactanos
   pueden ir a la MISMA etapa o a etapas distintas. Se guarda en `TenantConfiguration` con las claves
   `formularios.etapa_pqrs` y `formularios.etapa_contacto` (via `IConfiguracionClinicaService`). El
   webhook las lee por TenantId; si no hay config, ambos caen en "PQRS" (default). Si la etapa elegida
   no existe aun, se crea al recibir el primer envio (ensure-on-first-use, `EnsureStageAndFieldsAsync`).

5. **Idempotencia por hash + ventana.** Nueva entidad `FormWebhookEvent` (global): `DedupHash`
   (SHA-256 del tenant + campos mapeados), `LeadId`, `ReceivedAt`. Si llega el mismo payload dentro
   de **10 minutos** se devuelve la tarjeta existente (200, `duplicate:true`) en vez de crear otra
   (evita dobles por reintentos de Elementor). El mismo payload despues de la ventana crea una nueva
   tarjeta (envio legitimo).

6. **Mapeo del payload** (acepta claves `nombre` y `form_fields[nombre]` de Elementor):
   `nombre -> ContactName` (400 si falta) - `telefono -> ContactPhone` - `email/asunto/mensaje/tipo
   -> Fields[...]` - `pagina -> Fields["pagina_origen"]`. Solo se leen claves conocidas, asi que un
   payload no puede inyectar claves reservadas. Se registra `LeadActivity` con origen `web:{tipo}`.

7. **Contrato publico (fijo):**
   - `POST https://axon.ipsvisalrt.com/webhooks/formularios/{token}`
   - Content-Type: `application/x-www-form-urlencoded` (Elementor) o `application/json`.
   - Campos: `nombre` (obligatorio), `email`, `telefono`, `asunto`, `mensaje`, `tipo` (pqrs|contacto), `pagina`.
   - Respuesta: `201 {ok:true,cardId}` | `200 {ok:true,cardId,duplicate:true}` | `400 {ok:false,error}` | `401 {error}` | `429`.

## Pruebas

- xUnit (`FormWebhookServiceTests`): creacion de tarjeta, idempotencia por ventana, token
  invalido/deshabilitado (401), `nombre` faltante (400), normalizacion `form_fields[..]`, JSON, y
  ruteo a PQRS aunque exista otra etapa como primera.
- La migracion `WebhookFormularios` crea solo `tenant_form_webhook_configs` y `form_webhook_events`
  (sin FKs de BD, para no arrastrar drift al snapshot). Se aplica en el arranque via `MigrateAsync`.

## Consecuencias

- El tenant genera su token en Mi cuenta y pega la URL en WordPress; puede rotarlo o desactivarlo sin
  afectar el API key de leads.
- La etapa PQRS aparece sola en la primera recepcion, por entorno, sin seed manual con GUIDs.
- El intake `POST /api/public/leads` no cambia de comportamiento (`StageName` es opcional).
