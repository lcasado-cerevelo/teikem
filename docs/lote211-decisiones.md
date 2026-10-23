# Lote 211 — Recogidas (ruta con tipo)

## Qué se construyó
- **Decisión de Luis**: una recogida es una **ruta (viaje) con tipo**. `Trip.Kind` = `DELIVERY` (por omisión) o `PICKUP` (columna `Trip.Kind`, sección 2026-10-23 (h) de `Diseño/logistica-db-update.sql`; las rutas existentes quedan como entrega).
- **Web (Despacho)**: «Nueva ruta» tiene el campo **Tipo de ruta** (Entrega / Recogida). Al seleccionar una ruta de recogida, «Órdenes sin asignar» muestra solo los envíos **pendientes de recoger** (`Kind=PICKUP` en `GET /trips/unassigned-orders`) y la ficha muestra «Recogida» junto a la zona.
- **Reglas**: a una ruta de recogida solo entran envíos activos, no especiales, en la **etapa inicial** y con una **parada de recogida pendiente** (mensajes en el manual). Al despachar se vuelve a verificar. **Salir no mueve los envíos de estatus** (en entrega pasan a «En tránsito»).
- **Servidor del chofer**: manifiestos y detalle traen `kind`; `POST /driver-app/shipments/{id}/pickup` (quién lo entregó, notas, fotos, GPS) pasa el envío a «Recogido» y cierra la parada; «No recogido» usa el mismo `fail` de siempre pero **el envío sigue «Sometido»** (queda en el historial de la parada, sin intento de entrega). Entregar en ruta de recogida y recoger en ruta de entrega se rechazan. Cerrar la ruta exige que cada envío esté recogido o marcado no recogido.
- **App del chofer**: Home ya separaba «Recogidas para hoy» / «Manifiesto de recogida»; ahora un envío de recogida está abierto mientras esté «Sometido», sus acciones son **Recoger envío** / **No recogido**, y la recogida va por la cola sin conexión (`enqueuePickup`).

## Cómo se probó
- 67 pruebas de `TripStatusEffectTests` (nuevas en `TripStatusEffectTests.Pickup.cs`: aceptar no mueve estatus, recoger, reintento idempotente, guardas de tipo, no recogido, cierre de ruta, elegibilidad) y la suite completa del servidor: 5390 verdes, 0 fallas.
- App: `tsc --noEmit` limpio. Web: `npm run check`. **No se probó en teléfono.**

## Decisiones a revisar
- «No recogido» no tiene tabla propia: queda como comentario en el historial de la parada (RouteStop FAILED). Si se quiere un reporte de recogidas fallidas hay que crearla.
- El estatus «Recogido» debe estar habilitado en la compañía (si no: «El estatus «Recogido» no está habilitado para esta compañía.»).
- Falta en la web: columna «tipo» en la lista de rutas y en el monitor (hoy solo en la ficha y al crear).
