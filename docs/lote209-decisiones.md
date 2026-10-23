# Lote 209 — Avisos del chofer (bandeja en la app)

## Qué se construyó
- Tabla `dbo.DriverNotice` (sección 2026-10-23 (d) de `logistica-db-update.sql`; EntityType `DRIVER_NOTICE`), servicio `DriverNoticeService`, endpoints:
  - `GET /api/v1/driver-app/notices` (los últimos 100 y cuántos sin leer), `POST /api/v1/driver-app/notices/read` (`{ids}` o `{all:true}`) — permiso `driverapp.use`.
  - `POST /api/v1/driver-notices` (la oficina escribe un aviso a un chofer) — permiso `trips.dispatch`.
- **Despachar una ruta deja el aviso «Nuevo manifiesto {código}»** al chofer (en la misma transacción, `TripStatusEffect`).
- Los avisos se purgan a los 60 días (en el trabajo nocturno de limpieza `DRIVER_TRACKING_PURGE`).
- App: pestaña **Avisos** con campana e insignia de no leídos; junta los avisos de todas las compañías del chofer (cada uno con su compañía); tocar uno lo marca leído; «Marcar todo como leído»; se refresca cada 5 minutos y al abrir.

## Cómo se probó
3 pruebas nuevas (`TripStatusEffectTests.Notices`): el despacho deja el aviso y leerlo baja el contador; la oficina envía y se validan los mensajes; un no-chofer recibe 403. Pruebas de aislamiento por compañía y de mapa de permisos actualizadas. `tsc` y `expo export` de la app.

## Decisiones a revisar
- **Push** (aviso emergente con el teléfono bloqueado): no está; requiere Firebase (proyecto, `google-services.json`) — por ahora el chofer ve el aviso al abrir la app o a los 5 minutos.
- **Pantalla web para escribir avisos** a un chofer: el endpoint existe, falta el botón en la web (por ejemplo en la ficha del chofer).
- Otros avisos automáticos posibles (envío quitado de su ruta, ruta cancelada): hoy solo «Nuevo manifiesto».
