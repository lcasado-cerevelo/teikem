# Lote 54 — Enviar avisos (avisos manuales) (2026-10-15)

Pedido de Luis al cerrar P2: «la capacidad, a través de un permiso, de enviar avisos» a uno o varios usuarios, a un rol o a todos; el aviso **no es respondible**; y en los campos para escoger usuarios o roles, un **«Seleccionar todo»** que marque solo lo que está filtrado.
**Va en el cherry-pick a Depot-Implementation** (después del Lote 53).

## Qué se construyó
- **Permiso `notifications.send`** (categoría ADMIN): solo el Admin de compañía (plantilla y roles clonados); **no se da a otros roles por omisión**.
- **Servidor**: `AnnouncementService` (destinatarios = personas activas de la compañía, roles «hoy» o todas; sin repetir y sin avisarse a sí mismo; transacción única con tandas de 500; empuje en vivo DESPUÉS de guardar), `AnnouncementsController` (people con total y paginado hasta 500, roles, enviar, historial; sin PATCH/PUT/DELETE), tabla `Announcement` (historial: a quién y cuántas personas) y `UserNotification.AnnouncementId` (de ahí sale «De Ana» en la campanita). SQL sección **2026-10-15 (i)**.
- **Web**: pantalla **Sistema → Enviar avisos** (interruptor «a todas las personas» con confirmación y cantidad, campo «Para» de personas Y roles, título, texto, historial con «Leídos X de N» y exportación completa); la campanita muestra «De …»; el empuje `NoticeReceived` actualiza la campanita al instante (la conexión en vivo ahora la tiene toda persona con sesión).
- **«Seleccionar todo (N)»** en el buscador (`UserPicker`): escoge **todas las que coinciden con lo escrito** (no solo las 8 que caben en la lista) y nada más; en «Enviar avisos» incluye personas y roles; en «Nuevo mensaje» agrega las personas (los roles se escogen de uno en uno).
- Manual: capítulo 16 y pantalla `frontend/l54-enviar-avisos.md` (capturas `l54-*`), FAQ «Lote 54». Menú: «Enviar avisos» (orden 70 en Sistema; Trabajos programados 80, Seguridad y auditoría 90, Ajustes 100).

## Pruebas
- xunit: `AnnouncementsTests` (texto del destino, SQL, permiso solo Admin, API por reflexión sin PATCH/PUT/DELETE) y 5 pruebas contra **SQL Server real**: a varias personas (sin repetir, ella misma no, «De Ana» en su campanita, empuje en vivo, historial con leídos), a roles y a todos (activos de la compañía, All pisa a UserIds, otra compañía nada), todas las validaciones con su mensaje y **nada guardado si falla**, historial por compañía y paginado, directorio con filtro/paginado/total sin quien envía ni inactivos.
- vitest: «Seleccionar todo» (escoge todas las filtradas, no las 8 mostradas; personas y roles; con ya escogidos excluidos), pantalla (buscador, enviar, validaciones, a todos con confirmación, historial), campanita «De …» y evento `NoticeReceived`. **Suite completa vitest verde** (230 archivos). Playwright `lote54-enviar-avisos.spec.ts` (escritorio y móvil) con DOS personas: Carlos sin permiso (403 y sin menú), buscador y «Seleccionar todo», aviso a una persona y a todos EN VIVO con globo de Kemi y «De Administrador Advance», historial con «1 de 1».

## Decisiones por omisión (a revisar con Luis)
1. **Varios roles a la vez** en un aviso (en mensajes, un solo rol): un aviso a la vez es una notificación, no un grupo.
2. **Texto opcional** (solo el título es obligatorio); sin enlaces ni adjuntos.
3. **Enviar a todos pide confirmación** (con la cantidad); a personas o roles no.
4. Los avisos manuales **no se silencian** y no se editan ni se borran; quien ya lo recibió lo conserva.
5. La búsqueda trae hasta **500 personas** (con aviso para afinar); «Enviar a todas las personas» no tiene ese tope.
6. «Seleccionar todo» en **Nuevo mensaje** agrega solo a las personas (el mensaje admite un solo rol).
7. La conexión en vivo ahora se abre para **toda persona con sesión** (antes solo con el permiso de mensajería), porque los avisos le llegan a cualquiera.
