# Lote 51 — Avisos, servidor (P2.1 etapa a, 2026-10-14)

Tercer lote de P2. Hecho en el contexto principal, sin agentes. **Va en el cherry-pick a Depot-Implementation** (junto con 50, 52 y 53); su sección de SQL, permiso y código son propios y no dependen del Lote 49.

## Qué se construyó
- **Cola (outbox)** escrita en la misma transacción que la operación (`INotificationPublisher.PublishAsync` solo anota en el contexto del llamador; con `dedupKey` no repite). Un aviso que falla nunca deshace la operación.
- **Procesador + worker** (cada 10 s): reclamo atómico (`ExecuteUpdate`) para que varias copias no dupliquen; corre como la compañía sin usuario; reglas activas del evento → destinatarios (rol → usuarios activos de la compañía, usuarios, correos y teléfonos sueltos, sin repetir) → texto por canal en el idioma de la compañía → envío; registro **idempotente** por cola × regla × canal × destinatario; reintentos a 1, 5 y 30 min y luego FAILED; reintento solo de lo fallido.
- **Canales** (`INotificationChannelSender`): EMAIL (Brevo, el envío transaccional que ya existe), APP (bandeja `UserNotification`; respeta eventos silenciados), SMS y PUSH «no configurado» (SKIPPED con la razón; no se pueden encender en una regla).
- **Reglas**: evento + nombre único + canales (los cuatro siempre, nacen apagados con los textos por omisión del evento) + destinatarios; validaciones con mensajes exactos; enviar prueba por canal; evento `TEST` por la cola real; registro con filtros y paginado.
- **Bandeja**: lista, contador, marcar leído, marcar todo, silenciar eventos opcionales (cada usuario la suya, sin permiso especial).
- SQL sección **2026-10-14 (g)** (7 tablas, EntityType `NOTIFICATION_RULE`, permiso `notifications.manage` a Admin de compañía y roles clonados), API `/api/v1/notifications/*` y `/api/v1/inbox/*`, contrato en `openapi.json`/`schema.d.ts`.
- Manual: capítulo 14 y FAQ «Lote 51». Sin pantalla (es el Lote 52): sin capturas.

## Pruebas (solo el área)
- xunit: `NotificationsTests` (plantillas, validaciones, reintentos, catálogo, SQL, permiso, auditoría, API por reflexión) y `NotificationsSqlTests` contra **SQL Server real** (15 pruebas): evento por rol/usuario/correo, SMS omitido, idempotencia al reabrir la cola, reintentos 1/5/30 min → FAILED sin deshacer lo ya enviado, 8 procesadores concurrentes → una sola vez, dedup por compañía, regla apagada, aislamiento entre compañías, silenciar, correo no configurado, todas las validaciones del servicio con su mensaje, prueba de envío, evento de prueba por la cola, registro, bandeja ajena → 404. Más SQL/auditoría/permisos/aislamiento/catálogos: 633 en verde. `db-update --dry-run`: 132 lotes sin error. `tsc -b` limpio.

## Decisiones por omisión (a revisar con Luis)
1. **El catálogo de eventos vive solo en código** (sin tabla `NotificationEvent`): cada módulo registra un `INotificationEventProvider`; la regla guarda el código. Evita sembrar y mantener datos.
2. **Sin `TenantChannelSetting`** (configuración de canales por compañía): hoy no hay nada que configurar por compañía (el correo es global y SMS/push no existen). Se agrega cuando se contrate un proveedor.
3. **Sin alcance por almacén** en la regla (el diseño lo mencionaba como opcional): se agrega cuando un evento real lo pida.
4. Reintentos: el primer intento es inmediato y luego **3 reintentos** (1, 5, 30 min); el cuarto fallo deja el aviso FAILED.
5. El **idioma** del aviso es el de la compañía (`DefaultLangCode`), con respaldo es → en; aún no hay idioma por usuario.
6. Las **reglas nunca se borran** (se apagan), pero sus canales y destinatarios SÍ se reemplazan al editar (no tienen historial propio; el registro guarda el destinatario como texto).
7. «Enviar prueba» **no** queda en el registro (es un envío directo a quien la pide).
8. Un destinatario por **rol** usa los roles de la compañía (no las plantillas de plataforma).
