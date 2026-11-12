# Lote 242 — Despachar una orden de venta o un aviso de salida desde «Recolección y empaque» (2026-11-12)

Pedido de Luis (2026-11-12): «yo juraba que en la parte web teníamos un despacho… ¿cómo voy a sacar los productos?». Solo web, sin cambios de API ni de base.

## Lo que había (hallazgo)
La web ya tenía despacho, pero repartido: **Ventas → la orden → «Despachar»** (Lote 61/63), **Llegadas y salidas → Salidas → «Despachar»** (aviso de salida, Lote 145) y el **despacho manual** del panel «Recolección» de Recolección y empaque. La ficha de un documento en **Recolección y empaque** (Lote 173/238) mostraba lo que había que recolectar pero su único botón, **«Recolectar»** (Lote 238), solo llenaba el formulario manual: esa recolección **no usa la reserva** de la orden ni cambia su estatus. El almacén no podía sacar una orden desde la pantalla donde la ve.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Botón «Despachar» en la ficha del documento | `PickDocumentModal`: para una **orden de venta** o un **aviso de salida** con algo reservado por sacar (`warehouse.pick`). Abre el despacho de siempre. «Recolectar» queda aparte (botón secundario) y una nota debajo de la tabla explica la diferencia. |
| `SalesDispatchFlow` (nuevo, `features/sales`) | Compone `DispatchModal` y `NextDialog` de `OrderModal` (ahora exportados) más `PackModal` para la entrega: **un solo despacho de órdenes de venta en la web**. Al terminar refresca documentos, recolecciones e inventario. |
| Aviso de salida | `ExitNoticeDispatchModal` gana `onDone` opcional; la ficha lo carga con `useExitNotice`. |
| Textos | `warehouse.pickBatches.docs.dispatch` y `dispatchHelp` (es/en). |

## Cómo se probó
- Vitest `PickDocumentDispatch.test.tsx` (5): el botón y la nota; no sale sin `warehouse.pick`, ni en un envío empacado, ni sin nada reservado; despacha (carga la orden, POST con las líneas, pregunta «¿Qué sigue?», «Retiro» cierra la ficha); cancelar no cierra la ficha ni despacha. Almacén y ventas 974/974.
- **Playwright** `web-app/e2e/lote242-despachar-desde-recoleccion.spec.ts` contra el API real **sin correo** (base `Teikem_e2e`): orden solicitada con 5 reservadas → ficha → «Despachar» (el inventario aún no baja) → «Despachar» → «Retiro» → la orden queda **SHIPPED** con 5 sacadas y el inventario baja de 40 a 35; la lista dice «Sacado 5 de 5» sin recargar y la ficha ya no ofrece «Despachar». Capturas `l242-*` revisadas una por una. El recorrido del Lote 238 (botón «Recolectar») sigue pasando.

## Decisiones a revisar
1. **«Recolectar» se conserva** (pedido del Lote 238), como botón secundario con la nota que lo distingue. Es un despacho manual disfrazado de recolección para un documento con reserva; si prefieres quitarlo de la ficha, es borrar un botón.
2. **El despacho de la orden se hace por la misma ventana de Ventas** (por partes, retiro o entrega): no hay una segunda lógica.
3. Un documento con backorder y nada reservado no ofrece «Despachar» (no hay qué sacar).

## Qué quedó fuera
Manual en inglés y la prueba en móvil (la pantalla es de escritorio de dos paneles).
