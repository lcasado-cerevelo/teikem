# Lote 53 — Mensajería interna con hilos (P2.1, 2026-10-15)

Quinto lote de P2. Hecho en el contexto principal, sin agentes. Diseño presentado a Luis antes de construir (`docs/p2-lote53-mensajeria-diseno.md`) y sus cuatro decisiones aprobadas: **conexión en vivo (SignalR)**, **presencia «en línea» incluida**, **mensaje a un rol = foto del momento**, **todos los usuarios pueden escribir** (permiso `messages.use`).
**Va en el cherry-pick a Depot-Implementation** (con 50, 51 y 52).

## Qué se construyó
- **Servidor**: `MessagingService` (hilos DIRECT únicos por pareja y GROUP por personas o rol «en foto», enviar/contestar, leído por persona, no leídos, silenciar, agregar personas solo quien creó el grupo, salirse, historial por página, historial de un grupo solo desde que se entra), `PresenceService` («visto por última vez»; en línea < 90 s), `MessagesController` (11 endpoints, todos `messages.use`; sin PATCH ni DELETE) y `PresenceController`.
- **En vivo**: hub SignalR `/hubs/messages` (token por query `access_token`, grupo por persona × compañía, el hub no recibe mensajes ni devuelve datos; solo avisa «algo cambió» + resumen para el globo); `IMessageLiveNotifier` (nulo en infraestructura, SignalR en el API); el empuje ocurre DESPUÉS de guardar.
- **SQL** sección **2026-10-15 (h)**: `MessageThread` (CHECK DIRECT/GROUP, índice único por pareja), `MessageThreadMember`, `Message`, `UserPresence`, permiso `messages.use` agregado a TODOS los roles existentes (solo agrega).
- **Web**: pantalla **Mensajes** estilo chat (lista con avatar/en línea/no leídos, burbujas, separadores de día, «Cargar anteriores», Enter envía, panel de personas), **Nuevo mensaje** (buscador de nombre y correo, varias personas o rol, nombre de grupo), botón con contador en la cabecera, **Kemi**: globo «Te llegó un mensaje de X → Ver», «Enviar un mensaje a…» y «Mis mensajes»; presencia cada 30 s; conexión en vivo con reconexión y respaldo por sondeo de 30 s.
- Manual: capítulo 15 y pantalla `frontend/l53-mensajes.md` (capturas `l53-*`), FAQ «Lote 53».

## Pruebas
- xunit: `MessagingTests` (reglas puras, SQL, permiso en todas las plantillas, API/hub por reflexión) y `MessagingSqlTests` contra **SQL Server real** (15): hilo entre dos con respuesta, **seis envíos simultáneos a la misma persona → un solo hilo**, grupo por personas y por rol (foto, inactivos fuera, quien entra después no entra solo), validaciones con mensaje exacto, no leídos (contestar = leído), silenciado, aislamiento (no miembro / otra compañía → 404 en todo), creador/agregar/salirse/volver, páginas, empujes en vivo (a quién, silenciado, ThreadRead, sin empuje si falla), directorio con presencia, roles con conteo. Encontró un fallo real: `RunInTransactionAsync` limpia el ChangeTracker y el hilo se cargaba fuera.
- vitest: reglas puras + conexión en vivo con conexión falsa (reconexión, reintento, filtro de compañía), pantalla (lista, abrir/marcar leído, Enter/Mayús+Enter, anteriores, grupo y agregar, salirse, silenciar, Nuevo mensaje, en vivo) y Kemi (globo, silenciado, opciones). **Suite completa vitest verde.**
- Playwright `lote53-mensajes.spec.ts` (escritorio y móvil 360 px) con **DOS personas a la vez** contra el API real: escribir con buscador → recibo EN VIVO con globo de Kemi → contestar → el otro lo ve en vivo y «En línea» → grupo por rol → API (404/400).
- Se corrigió de paso un fallo real del buscador de personas: un cierre diferido de la lista (por el desenfoque) podía cerrarla después de volver a enfocarla.

## Decisiones por omisión (a revisar con Luis)
1. **Los mensajes tienen su propio ícono y contador en la cabecera** (no se mezclan con la campanita de avisos); Kemi avisa de ambos.
2. **Una sola instancia del servidor**: el empuje en vivo solo llega a quienes están conectados a la instancia que recibió el mensaje; el sondeo de respaldo (30 s) cubre el resto. Para varias instancias a la vez habría que agregar un «backplane» (SQL o Redis).
3. **Salirse de un grupo** deja de mostrar el hilo a esa persona (404 si lo busca); si la vuelven a agregar, no ve lo escrito mientras estuvo fuera.
4. **No hay «escribiendo…»**, confirmaciones de lectura por persona ni adjuntos; los mensajes no se editan ni se borran.
5. El texto del aviso de Kemi dice el nombre de quien escribió; con varios mensajes, «Ana te escribió (3 mensajes)».
6. **Portal de clientes**: `messages.use` no se da por omisión a roles nuevos ni a los del portal; Luis lo asigna a los gerenciales de cada cliente. Pendiente al construir el portal: que un usuario de cliente solo pueda escribir a la persona o equipo designado y nunca a otros clientes.
7. En la web el avatar usa **iniciales** (no hay fotos de perfil todavía).
