# Lote 144 — Aviso de salida: pantallas web (4b) (pedido de Luis, 2026-10-20)

## Qué se construyó
- **Pestaña «Avisos de salida»** en Recibo (`/warehouse/receipts?tab=exit-notices`, con `?notice=<id>` para abrir una ficha): tabla paginada por el servidor con buscador libre (número, referencia, destino, cliente, producto), filtro de estatus y «Solo con pendiente»; ordenar y exportar usan todo el filtro (`exportRows`).
- **Nuevo aviso de salida** (`exitnotice.create`): cliente dueño, almacén, referencia/destino, fecha requerida y líneas (producto del cliente + cantidad, hasta 200). Avisa con un mensaje si quedó pendiente.
- **Ficha**: líneas con pedido/reservado/pendiente/despachado, cambiar cantidad, reservar el pendiente, cerrar con faltante, cancelar (con confirmación) y la lista de despachos con su estado «Deshecho».
- **«Reservar el pendiente»** en la lista (reserva todo lo pendiente de todos los avisos).
- Textos es/en en `warehouse.exitNotices.*`; tema claro/oscuro por las clases del kit.

## Cómo se probó
- vitest: `exitNoticeRules.test.ts` (cantidades, líneas modificables, pendiente, por despachar) y `ExitNoticesTab.test.tsx` (lista, filtro que viaja al API, botones por permiso, reservar el pendiente, ficha). `npm run check` verde.
- No hay recorrido Playwright ni capturas en este lote (no hay API ni base disponibles en la sesión).

## Decisiones a revisar
1. El aviso vive como pestaña de Recibo, junto a Avisos de llegada (simétrico: llegada ↔ salida).
2. Despachar **no** se hace desde la ficha todavía: llega en el lote 145 (selector «Contra» en Recolección/Despacho manual).
3. Capturas del manual de pantallas pendientes hasta poder correr el API.
