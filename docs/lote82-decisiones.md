# Lote 82 — Pantallas de devoluciones de venta (P6, 4 de 5) (2026-10-16)

Cuarto lote de P6: la web de las devoluciones del Lote 81. Sigue el **83**: la app del almacén (recibir una devolución con buenas y dañadas).

## Qué se construyó
- **Pestaña «Devoluciones»** en Órdenes de venta (no se agregó ruta: `navigation.test.ts` pina las 47): lista con buscador, filtro por estatus, orden y exportación completa (`exportRows`), y el botón **Pedir devolución** (`sales.return`).
- **Ficha** con las acciones por estatus y permiso: **Autorizar** (nota opcional) y **Rechazar** (motivo obligatorio) con `sales.return.authorize`; **Cancelar** con `sales.return`; **Recibir** con `warehouse.receive`; **Acreditar** con `billing.credit`.
- **Editor de pedir**: elige la orden despachada (o llega desde la ficha de la orden con el nuevo botón **Pedir devolución**, `OrderModal`), muestra despachado/devuelto/pendiente/devolvible por línea, bloquea kits y series, valida antes de enviar y maneja «fuera de la ventana» (solo quien autoriza puede confirmarla).
- **Recibir**: por línea bueno, dañado, lote, descripción del daño y desechar; «cerrar lo pendiente».
- **Ventana de devolución** editable (0–3650 días) con `sales.return.authorize`.
- `openapi.json` y el cliente generado con las 9 rutas y los 17 esquemas; `returnsApi.ts`, `returnRules.ts` (reglas puras), `ReturnsParts.tsx`; textos `salesReturns.*` es/en y estilos en `sales.css`.
- Manual de pantallas `docs/manual/frontend/l82-devoluciones.md` con 5 capturas.

## Cómo se probó
- vitest: reglas (10) y la pantalla con fetch simulado (7): lista y filtro, autorizar con nota y recibir con daño (descripción obligatoria, cuerpo), rechazar con motivo obligatorio, acreditar con y sin `billing.credit`, pedir (tope y kit bloqueado), fuera de la ventana y cambio de la ventana. `npm run check` y toda la suite (2990 pruebas).
- Playwright contra el API real (SQL Server), escritorio y móvil (360 px, sin scroll horizontal): orden despachada y facturada por API, pedir desde la ficha de la orden (pasarse del tope da el aviso), autorizar, recibir 2 buenas y 1 dañada (aviso de la descripción), el recibo REC-, acreditar (NC- en borrador) y la lista.

## Decisiones a revisar con Luis
1. **Pestaña dentro de Órdenes de venta** y no una pantalla aparte, para no romper el menú ni las rutas fijadas.
2. **Recibir desde la web** además de la app (Lote 83): el almacén normalmente usará la app, pero el supervisor puede registrar desde el escritorio.
3. **La ventana de devolución** se edita en la misma pestaña, visible a todos y editable solo con `sales.return.authorize`.
4. Lote, serie y posición destino al recibir: la web captura el lote; la posición la elige el servidor (la de recibo del almacén). La app tendrá más control.

## Fuera de este lote
- La app del almacén (Lote 83); avisos y análisis de devoluciones; kits y series en devoluciones.
