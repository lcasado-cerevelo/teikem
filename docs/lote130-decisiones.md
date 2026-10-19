# Lote 130 — Campos personalizados en la búsqueda y en las Vistas (dictado de Luis, 2026-10-18)

## Qué se construyó
- **Una sola opción por campo:** `showInList` se llama ahora «Mostrar en la búsqueda» (es/en). No hay otra bandera para reportes.
- **Filtro en la lista de la entidad, del tipo del campo:** `CustomFieldFilters` (servidor) lee `cf.clave`, `cf.clave.from`, `cf.clave.to` de la dirección y filtra con subconsulta sobre `CustomFieldValue` en Órdenes, Clientes, Consignatarios, Choferes y Vehículos. Campo inexistente o sin la opción = 400 con mensaje exacto. Sin cambios de esquema (ni de `update.sql`) ni de `openapi.json`.
- **Web:** componente `CustomFieldFilters` / `CustomFieldFilterBar` del kit (texto, desde/hasta, rangos de fecha, sí/no, listas con opciones activas o de catálogo); integrado en las 5 listas.
- **Vistas:** toda fuente con entidad ya traía los campos personalizados como `cf.*` (columna y filtro del DSL); ahora la columna sale con la etiqueta en el idioma y el tipo real del campo (`AnalyticsEngine`).

## Cómo se probó
- xunit `CustomFieldSearchTests` (3: parseo de la dirección, filtro por cada tipo y errores, vistas con campo sin la opción como columna y filtro) + `CustomFieldDefinitionTests`.
- vitest `CustomFieldFilters.test.tsx` (3) y ajustes en la pantalla de administración; `npm run check` verde (con el fallo intermitente conocido de workers de vitest bajo carga).
- Playwright `lote130-campos-en-busqueda.spec.ts` (escritorio y móvil) contra API y SQL reales: filtros texto/número/rango por API, 400 de campo no habilitado y desconocido, filtro de clientes, columna en data-sources, y pantalla de Órdenes y Clientes.

## Decisiones a revisar
1. **Las Vistas no exigen la opción:** el pedido dice que todos los reportes admiten los campos; «Mostrar en la búsqueda» solo gobierna los filtros de las listas.
2. **Multiselección:** el filtro es «tiene esta opción» (una a la vez).
3. **Tipos DATETIME** se filtran por día (como las fechas).
4. **Solo las 5 listas con campos personalizados hoy** (órdenes, clientes, consignatarios, choferes, vehículos); otras entidades lo heredan si se les agrega la llamada `ApplyCustomFieldsAsync` y `CustomFieldFilterBar`.
