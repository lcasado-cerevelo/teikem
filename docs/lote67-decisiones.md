# Lote 67 — Análisis de órdenes de venta (P4, S4) (2026-10-16)

Cierra P4 (órdenes de venta): S1 y S2 (servidor, web y app), S3 (avisos) y S4 (análisis).

## Qué se construyó
- **Fuentes de datos** `SALES_ORDER` y `SALES_ORDER_LINE` (`SalesDataSources.cs`): permiso `sales.view` y módulo Órdenes de venta (`TenantModule`), tope de filas, `q.Ids`, rango por fecha de creación, aislamiento por compañía. Sin campos personalizados (`EntityTypeCode` nulo; el permiso se busca por la clave).
- **Reglas puras** (`SalesAnalyticsRules`): cumplimiento, días a despachar, atrasada, días esperando, y los filtros/agrupaciones de sistema.
- **Contenido de sistema** sembrado por compañía: 4 indicadores (solo «Órdenes de venta abiertas» en el Pulso), 1 gráfico (estatus, 30 días) y 3 vistas (la de backorders solo si existe el EntityType `SALES_ORDER_LINE`).
- **Aviso del Pulso** `SALES_BACKORDER_READY` (`SalesBackorderReadyAttentionProvider`), con `sales.backorder`; en la web, texto y «Revisar» que abre la orden (`/sales-orders?order=`).
- **Base de datos:** sección `2026-10-16 (o)` de `logistica-db-update.sql` (EntityType `SALES_ORDER_LINE`). Probada con `db-update --dry-run` sobre `Teikem_verif`.
- Manual (07 §4.x, 18 §16) y FAQ.

## Pruebas
- xunit (12 nuevas): reglas (cumplimiento con tope, días, atrasada), filas de órdenes (despachada con cumplimiento 100 y días, abierta con backorder, borrador), filas de líneas (días esperando, ids, rango, otra compañía vacía), campos de los filtros y agrupaciones existen en las fuentes, permiso y módulo de las fuentes, aviso del Pulso (nada sin inventario, una fila con parámetros cuando entra inventario), y el contenido sembrado es idempotente y sus campos existen. Suite completa verde.
- Web: vitest del texto del aviso (es/en) y de abrir la orden con `?order=`; `npm run check` verde (235 archivos, 2887 pruebas).

## Decisiones por omisión (a revisar con Luis)
1. **«Fill rate» se mide por orden despachada** (despachado/pedido, tope 100) y se promedia; no es ponderado por unidades.
2. **Días a despachar** = de solicitar al último cambio a Despachada del historial de estatus.
3. **Solo «Órdenes de venta abiertas» está en el Pulso**; los demás indicadores y el gráfico se encienden por usuario.
4. **El aviso de backorder del Pulso exige `sales.backorder`** (quien puede surtir), no solo `sales.view`.
5. Las fuentes no admiten campos personalizados todavía.

## Fuera de este lote
- Informe de impuestos por período (espera facturación) y el modo AUTO de backorder.
- Capturas de pantalla del Pulso con el aviso (el panel ya existe; no se cambió su diseño).
