# Lote 146 — App de almacén: despachar contra un aviso de salida (4d) (pedido de Luis, 2026-10-20)

## Qué se construyó
- **Servidor:** `GET /api/v1/sync/exit-notices` (módulo `WMS_LOTSERIAL`, `inventory.view`): avisos solicitados o parciales con algo reservado, con sus líneas y de qué posición y lote reservó el sistema; con `since` llegan también los cerrados o cancelados como `isActive = false`. `GET /inventory/exit-options` trae además `dispatchRequirement` (campo nuevo al final).
- **App:** migración local v17 (`exit_notice`, `exit_notice_line`, `exit_notice_reservation`); bajada `downloadExitNotices` (reemplaza la copia del almacén, 403 se salta, se reaplican los despachos pendientes de la cola); pantalla **Avisos de salida** (`/exit-dispatch`, tile en Inicio solo si hay avisos): lista/escaneo del número SAL-…, despacho guiado por posición y cantidad (igual que ventas, sin kits ni entrega), cola de salida con tipo `exitDispatch` (`POST /exit-notices/{id}/dispatch`), efecto inmediato en el aviso y en los saldos locales, y rechazo del servidor con su mensaje.
- **«¿Contra qué?»:** con «el despacho exige documento» activo (copia local de `dispatchRequirement`), Despacho abre un selector (aviso de salida y/o orden de venta según lo que exija la compañía) en vez del despacho libre. Si ya hay un despacho libre abierto en el aparato, se deja terminar. El servidor también lo exige (409).
- `schema.d.ts` de la app con el endpoint y los campos nuevos (parche manual: regenerarlo completo produce un diff enorme).

## Cómo se probó
- xunit: `ExitNoticeTests` (+1: la sincronización manda los abiertos con sus reservas y marca los cancelados como inactivos).
- jest: `localExit.test.ts` (copia local, búsqueda por número, cola con la ruta y el cuerpo, efecto en saldos, reaplicado tras bajar, lectura/guardado de la exigencia); listas de recursos de las pruebas de descarga/sincronización; suite completa de la app (756) y `tsc` limpios.
- No hay prueba de ruta ni recorrido Maestro del selector «¿Contra qué?» ni de la pantalla nueva (se probó la lógica; la pantalla es una copia de la de ventas).

## Decisiones a revisar
1. La pantalla nueva es **copia** de la de ventas (sin kits ni entrega) en vez de parametrizar esa; si cambia una, hay que cambiar la otra.
2. La exigencia se lee de `exit-options`, así que el aparato la conoce tras la primera sincronización de existencias; antes es «libre» (el servidor decide igual).
3. La **recolección para empacar** del aparato no se bloquea (Empacar queda fuera de la regla).
