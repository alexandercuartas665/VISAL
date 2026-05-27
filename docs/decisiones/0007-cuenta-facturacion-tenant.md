# ADR-0007: Cuenta y Facturacion del Tenant (autogestion del cliente)

**Fecha:** 2026-05-20
**Estado:** Aceptado
**Corresponde a:** Nuevo modulo 1.6 (Capa 1). Complementa el lado Super Admin (0.2 Planes / 0.3 Facturacion Wompi).

## Contexto

La documentacion modela toda la facturacion y el ciclo de planes desde la perspectiva del **Super Admin** (Capa 0): planes, suscripciones, cobros Wompi y el cambio de plan (seccion 15.2 del doc de Capa 1, ejecutado por el Super Admin). El **inventario de modulos del tenant** (1.1 a 1.5) no contempla un area de **autogestion** donde el administrador de la agencia vea su propio producto.

Esto deja un vacio: el cliente no tiene donde consultar su **plan activo, limites, consumo y facturas**, ni donde **cambiar de plan**. Las agencias esperan un portal de cuenta tipo SaaS.

## Decision

1. **Nuevo modulo 1.6 "Cuenta y Facturacion del Tenant"** (Capa 1, lado agencia). Area de autogestion para el administrador de la agencia con: plan activo (nombre, precio, frecuencia, estado, fecha de corte), limites del plan, listado de sus facturas (solo lectura) y cambio de plan.

2. **Cambio de plan: autoservicio inmediato.** El cliente elige un plan disponible y el cambio **aplica de inmediato**: se crea una suscripcion activa con el nuevo plan y se recalcula la fecha de corte segun la frecuencia. Queda auditado (`subscription.change`). Se aparta de la seccion 15.2 (que lo pone en manos del Super Admin) por decision de producto: priorizar la agilidad del cliente.

3. **El cobro/prorrateo real se difiere a Wompi (Fase 3).** Por ahora el cambio de plan no ejecuta un cobro en linea ni calcula prorrateo monetario; solo cambia la suscripcion y los limites. El cobro real, la nota de prorrateo y la confirmacion por webhook se implementaran cuando exista el cliente HTTP a Wompi y el procesamiento idempotente de eventos.

4. **Datos globales, no tenant-scoped.** `TenantSubscription` y `TenantPayment` son entidades globales (administradas a nivel plataforma), por lo que la pagina las consulta por `tenantId` explicito mediante los servicios admin existentes (`ISubscriptionAdminService`, `IPaymentAdminService`, `IPlanAdminService`).

5. **Auth / contexto de tenant (provisional).** Aun no existe login de usuarios de tenant; la consola actual solo autentica Super Admin. La pagina se construye en el menu del lado tenant y, mientras tanto, el operador de plataforma selecciona la agencia a previsualizar. Cuando exista login de tenant, la agencia quedara fijada al usuario autenticado y el selector desaparecera.

## Diferido (no en este commit)

- Cobro real y prorrateo via Wompi (Fase 3) y confirmacion por webhook idempotente.
- Consumo real por limite (usuarios/lineas/IA usados vs cupo); por ahora se muestran los cupos del plan.
- Login y politicas de usuario de tenant (modulo 1.2) para fijar la agencia automaticamente.

## Consecuencias

- El cliente gana visibilidad y control sobre su producto (plan, limites, facturas) y puede cambiar de plan sin intervencion del Super Admin.
- Se introduce un modulo fuera del inventario original; por eso este ADR y la actualizacion del INVENTARIO GENERAL.
- El cambio de plan inmediato sin cobro en linea es un estado intermedio consciente: la suscripcion cambia ya, el dinero se concilia cuando entre Wompi (Fase 3).
