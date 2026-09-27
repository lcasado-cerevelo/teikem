# Lote F6 — Almacén e inventario (mínimo) + consulta de órdenes: qué se construyó y decisiones a revisar

Plan aprobado: `docs/frontend/loteF6-plan.md`. Contrato del kit: `web-app/KIT.md` (sección "Almacén" agregada en este
lote). Fecha de cierre: 2026-09-27.

## Mapa de lo construido

### Pantallas

| Pantalla | Ruta | Permiso · módulo | Componentes / código principal |
|---|---|---|---|
| Almacenes (lista) | `/warehouse/warehouses` | `inventory.view` · `WMS_LOTSERIAL` (alta/edición/baja: `warehouse.manage`) | `features/warehouse/WarehouseListScreen.tsx` |
| Almacén (ficha: Datos, Zonas, Posiciones, Muelles) | `/warehouse/warehouses/:publicId` | ídem | `features/warehouse/WarehouseDetailScreen.tsx` |
| Productos (lista, con subpestaña Categorías) | `/warehouse/products` | `inventory.view` · `WMS_LOTSERIAL` (alta/edición/baja: `inventory.manage`) | `features/warehouse/ProductListScreen.tsx`, `ProductCategoriesPanel.tsx` |
| Producto (ficha: Datos, Lotes, Series) | `/warehouse/products/:publicId` | ídem | `features/warehouse/ProductDetailScreen.tsx` |
| Inventario (Saldos, Kárdex, Conciliación) | `/warehouse/inventory` | `inventory.view` · `WMS_LOTSERIAL` (ajustar/transferir/conciliar: `inventory.adjust`) | `features/warehouse/InventoryScreen.tsx`, `InventoryAdjustModal.tsx`, `InventoryTransferModal.tsx` |
| Recepción (Recibos, Avisos de llegada) | `/warehouse/receipts` (+ `/:publicId`) | `inventory.view` · `WMS_LOTSERIAL` (capturar/confirmar/eliminar/ASN: `warehouse.receive`; recibir contra OC exige además `purchasing.receive` + `PURCHASING`) | `features/warehouse/ReceiptListScreen.tsx`, `ReceiptDetailScreen.tsx` |
| Tareas de almacén (cola unificada) | `/warehouse/tasks` | `inventory.view` · `WMS_LOTSERIAL` (asignar/cancelar: `warehouse.manage`; iniciar/completar según tipo: `warehouse.receive`/`warehouse.pick`/`warehouse.count`/`warehouse.crossdock`; correr reabasto: `warehouse.pick`) | `features/warehouse/WarehouseTaskListScreen.tsx` |
| Conteo cíclico (lista + ficha) | `/warehouse/cycle-counts` (+ `/:id`) | lista: `inventory.view`; ficha y todas las acciones: `warehouse.count` | `features/warehouse/CycleCountListScreen.tsx`, `CycleCountDetailScreen.tsx` |
| Recolección y empaque (lista + ficha) | `/warehouse/pick-batches` (+ `/:publicId`) | `inventory.view` · `WMS_LOTSERIAL` (recolectar/eliminar: `warehouse.pick`; empacar exige además `orders.create`; eliminar ya empacada exige además `orders.cancel`) | `features/warehouse/PickBatchListScreen.tsx`, `PickBatchDetailScreen.tsx` |
| Proveedores | `/warehouse/suppliers` | `purchasing.view` · `PURCHASING` (alta/edición/baja: `purchasing.manage`) | `features/warehouse/SupplierListScreen.tsx` |
| Órdenes de compra (lista + ficha) | `/warehouse/purchase-orders` (+ `/:publicId`) | `purchasing.view` · `PURCHASING` (crear/editar/enviar/cancelar/eliminar: `purchasing.manage`; resolver faltante: `inventory.adjust`, con REORDER exigiendo además `purchasing.manage` y MANUAL_ADJUSTMENT el módulo `WMS_LOTSERIAL`) | `features/warehouse/PurchaseOrderListScreen.tsx`, `PurchaseOrderDetailScreen.tsx` |
| Citas de muelle | `/warehouse/dock-appointments` | `inventory.view` · `CROSSDOCK` (agendar/reprogramar/cambiar estatus: `warehouse.crossdock`) | `features/warehouse/DockAppointmentListScreen.tsx` |
| Cruce de muelle: planes (lista + ficha) | `/warehouse/cross-dock-plans` (+ `/:id`) | `inventory.view` · `CROSSDOCK` (crear/asignar/cancelar/mover/completar: `warehouse.crossdock`) | `features/warehouse/CrossDockPlanListScreen.tsx`, `CrossDockPlanDetailScreen.tsx` |
| Consulta de órdenes (lista + ficha, solo lectura) | `/orders` (+ `/:publicId`) | `orders.view` · `LTL_GROUND` | `features/orders/OrderListScreen.tsx`, `OrderDetailScreen.tsx` |
| Panel "Almacén" en Pulso | `/` (debajo de los indicadores del API) | se pinta solo con `inventory.view` y el módulo `WMS_LOTSERIAL` | `features/analytics/Pulse.tsx` (`WarehousePulsePanel`) |

### Núcleo compartido del almacén (sin pantalla propia)

| Pieza | Código | Qué deja listo |
|---|---|---|
| Hooks de datos | `features/warehouse/api.ts` (1052 líneas) | Un hook por lectura (`useWarehouses`, `useProducts`, `useInventoryBalances`, `useReceipts`, `useWarehouseTasks`, `useCycleCounts`, `usePickBatches`, `useSuppliers`, `usePurchaseOrders`, `useDockAppointments`, `useCrossDockPlans`, `useOrdersReadonly`…) con clave `[ruta, params]`; mutaciones con invalidación por prefijo (`warehouseKeys`) |
| Selectores de almacén/producto | `features/warehouse/pickers.tsx` | `WarehousePicker`/`WarehousePickerInput` (select simple) y `ProductPicker`/`ProductPickerInput` (combobox con buscador, como `ClientPicker`); `ProductMultiFilter` |
| Reglas de producto | `features/warehouse/productRules.ts` (+ `.test.ts`) | Esquemas zod de peso/volumen/costo-precio con los mensajes exactos del manual 06 |
| Reglas de captura de líneas | `features/warehouse/lineRules.ts` (+ `.test.ts`) | `receiptLineIssues`, `countLineIssues`, `pickLineIssues`, `parseSerials`, `remapProblemFields`, `lineErrorsByIndex`, formatos y `useDebounced` |
| Rutas | `app/routes.tsx` | 20 entradas nuevas (16 con `nav`) en los grupos `ops` (Órdenes) y `warehouse` (las 11 pantallas del almacén) |
| Pulso: hooks de almacén | `features/analytics/api.ts` (`useWarehousePulse`, `PULSE_TASK_TYPES`) | Saldo, recibos abiertos, tareas pendientes por tipo y conteos abiertos, cada uno con `take=1` (sin traer toda la lista) |
| Textos | `kernel/i18n/es.json` / `en.json` | Sección `warehouse.*` (~1140 líneas) y `orders.*` nuevos; ningún texto suelto en JSX |
| `StatusPipeline` | `kernel/catalogs/StatusPipeline.tsx` (+ prueba) | Se le agregó `manualTargets` en este lote (limita los botones a las transiciones que de verdad dispara el usuario, cuando otras las mueve el sistema: p. ej. una orden de compra solo ofrece SENT/CANCELLED) |

Dominios de estatus usados (con su `entityType`): `WarehouseStatus`/`WAREHOUSE`, `DockStatus`/`WAREHOUSE_DOCK`,
`ReceiptStatus`/`RECEIPT`, `AsnStatus`, `PurchaseOrderStatus`/`PURCHASE_ORDER`, `WarehouseTaskStatus`,
`CycleCountStatus`/`CYCLE_COUNT`, `PickBatchStatus`/`PICK_BATCH`, `AppointmentStatus`/`DOCK_APPOINTMENT`,
`CrossDockStatus`/`CROSSDOCK_PLAN`, `AllocationStatus`, `SerialStatus`, `OrderStatus`/`TRANSPORT_ORDER` (solo lectura).

## Cómo se prueba

1. `cd web-app && npm ci` (ya instalado en este entorno).
2. `npm run check` (`api:types` + `tsc -b` + `oxlint src e2e` + `vitest run` + `vite build`). **Ejecutado en este entorno
   ahora mismo, verde**: 25 archivos de prueba, 184 pruebas, sin errores de tipos ni de lint (solo 5 avisos
   `react(incompatible-library)` de oxlint sobre el uso de `useLookups`/`useForm`, no bloqueantes), build de producción
   generado en `web-app/dist`.
3. Recorrido Playwright (`e2e/f6.spec.ts`, proyectos `escritorio` y `movil` = Pixel 7 a 360 px) contra el API real
   (SQL Server 2022 + `db-init` + `dotnet run --project src/Teikem.Api`, ambos levantados en este entorno) y Vite en
   `:5173`: `npx playwright test`. Cubre los 10 pasos del "Recorrido Playwright" del plan (Pulso con el panel Almacén →
   Almacenes/posiciones sembradas → alta de producto con y sin SKU → ajuste de inventario → Kárdex → proveedor y orden de
   compra Enviar → recibo ciego y tarea PUTAWAY → asignar/iniciar/completar la tarea → recolectar y empacar → Consulta de
   órdenes; más el paso móvil de "sin scroll horizontal"). **Verificado en este entorno**: primera corrida con **1 prueba
   roja** (ver decisión 1 abajo); tras el arreglo, **segunda corrida verde**: 19 pruebas pasadas (10 de escritorio + 9 de
   `loteF1.spec.ts` que comparten el mismo proyecto, más 1 de móvil), 19 saltadas (los proyectos se filtran entre sí por
   `isMobile`), 0 rojas. Cada paso deja una captura en `docs/manual/frontend/img/f6-*.png` (26 archivos, generados en esta
   misma corrida).
4. `.github/workflows/ci.yml` corre `npm run check` en el job `frontend`; el recorrido Playwright se ejecuta localmente con
   el API arriba (no está en CI, igual que en F1).

> **Nota sobre el entorno de verificación**: a diferencia del cierre de F1, aquí no hizo falta instalar SQL Server: ya
> había una instancia corriendo en el contenedor desde una sesión anterior, con las bases de datos de lotes de backend
> previos. Se usó el tenant demo existente (`Advance Logistics`, admin `admin@teikem.local`); `db-init` confirmó que la
> estructura y el seed ya estaban aplicados (no hubo cambios de esquema en este lote: es 100% frontend).

## Decisiones tomadas (revisar)

1. **Hallazgo encontrado y corregido en este cierre: el panel "Almacén" de Pulso se desmontaba y remontaba al terminar de
   cargar los indicadores del API** (`features/analytics/Pulse.tsx`). La pantalla de inicio devuelve, según el estado
   (bienvenida/cargando/error/vacío/cargado), árboles JSX distintos donde el panel de almacén es el último hijo de
   `<div className="wrap">`; sin una `key` estable, React lo reconcilia por posición y, al pasar de "cargando" (2 hijos:
   `Spinner` + panel) a "cargado" (4 hijos: cabecera + indicadores + gráficos + panel), lo desmonta y crea un nodo nuevo.
   Esto se detectó porque el paso 1 del recorrido Playwright falló de forma intermitente con
   `Element is not attached to the DOM` al hacer `scrollIntoViewIfNeeded()` sobre el encabezado "Almacén" (localizado antes
   de que el remontaje ocurriera). Arreglo: `key="pulse-warehouse-panel"` en la `<section>` que envuelve
   `WarehousePulsePanel` (misma variable `warehouse` reutilizada en todos los retornos de la función), para que React lo
   reconozca como el mismo nodo sin importar su posición entre hermanos. Verificado con `npm run check` (sigue verde) y
   una segunda corrida completa de Playwright (verde). Es una corrección de comportamiento real, no solo del test: sin
   ella, un usuario con conexión lenta vería el panel de almacén parpadear (perder el estado de scroll/foco) al terminar
   de cargar Pulso.
2. **`StatusPipeline` ganó `manualTargets`** (prop nueva, con prueba en `StatusPipeline.test.tsx`): varias entidades del
   almacén tienen transiciones que dispara el sistema, no un botón (recibo OPEN→RECEIVED al confirmar, →PUTAWAY al
   terminar el acomodo; conteo OPEN→COUNTED/RECONCILED con "Terminar"/"Reconciliar"; recolección COLLECTED→PACKED/
   CANCELLED con "Empacar"/"Eliminar"). Para no ofrecer botones que el servidor rechazaría, esas pantallas pasan
   `StatusPipeline` sin `onTransition` (o `disabled`) y muestran el historial como solo lectura; la orden de compra sí
   tiene botones pero acotados con `manualTargets={['SENT', 'CANCELLED']}` porque PARTIAL/RECEIVED los pone la
   confirmación de un recibo.
3. **Cruce de muelle (planes) es de solo lectura en cuanto a estatus del plan**: `CrossDockPlanDetailScreen` pasa
   `StatusPipeline` con `disabled` (el estatus del plan —OPEN→ALLOCATED→COMPLETED— lo mueve el servidor según las
   asignaciones, no un botón de pipeline); "Completar plan" es un botón aparte que llama a la acción `complete`.
4. **Panel "Almacén" de Pulso calculado en cliente con `take=1`**: en vez de traer toda la lista de saldos/recibos/tareas
   para contar, cada tarjeta pide una página de 1 y lee `total`/`totalOnHand`/`totalAvailable` del DTO de la primera
   página, tal como pedía el plan (P0). No se creó ningún indicador nuevo en el motor de analítica.
5. **Recibir contra orden de compra oculta la opción sin dos condiciones a la vez** (`purchasing.receive` **y** el módulo
   `PURCHASING` encendidos): con cualquiera de los dos ausentes, "Nuevo recibo" solo ofrece Ciego/Devolución/ASN, en vez de
   mostrar la opción y fallar con 403 al enviar.
6. **`ProductPicker` en la orden de compra se filtra a productos propios** (`ownOnly`): el formulario nunca deja elegir un
   producto de un cliente (el servidor lo rechazaría con "La orden de compra solo admite productos propios..."); es más
   simple que dejar elegirlo y mostrar el 409 después.
7. **La ficha de "Consulta de órdenes" no usa `Tabs`**: aunque el plan describe "Datos generales, Recogida, Entrega,
   Bultos, Historial de estatus" como si fueran pestañas, `OrderDetailScreen.tsx` los pinta como paneles apilados en una
   sola página (más simple para una ficha de solo lectura sin acciones); los mismos textos (`orders.detail.tabGeneral`,
   etc.) se reutilizan como título de cada panel en vez de como pestañas. Documentado aquí porque difiere ligeramente de
   la letra del plan sin cambiar lo que el usuario puede hacer (nada: es de solo lectura).
8. **No se probó el escenario "recibir contra orden de compra ni contra ASN de cliente"** en el recorrido Playwright (el
   plan solo pide el recibo ciego); tampoco se ejercitaron en Playwright: cruce de muelle (citas/planes), conciliación de
   inventario, genealogía de lote, rastro de serie, ni el conteo cíclico completo (crear → contar → terminar →
   reconciliar). Esas pantallas se verificaron por lectura de código y captura manual (`docs/manual/frontend/img/f6-*.png`
   incluye `f6-inventario-conciliacion.png`, `f6-almacen-zonas.png`, etc., tomadas navegando la pantalla fuera del guion
   automatizado) pero no tienen una prueba automatizada que falle si se rompen; es un hueco de cobertura a revisar en un
   lote posterior si el equipo lo considera necesario.
9. **No se modificó ningún archivo de backend** (`src/`, `Diseño/`, `tests/`): este lote es 100% frontend sobre el API ya
   construido en el Lote 6; `dotnet build` y el `db-init` de este entorno se corrieron solo para tener el API real
   disponible para Playwright, no porque hubiera cambios ahí (confirmado con `git status`/`git diff --stat` sobre esas
   carpetas: sin diferencias).

## Lo que queda fuera de este lote (a propósito)

Copiado de "Fuera de alcance" de `docs/frontend/loteF6-plan.md`, con lo verificado en el código:

- Administración de catálogos y de estatus (ya cubierta en F1).
- Impresión de etiquetas y documentos.
- Escaneo por cámara: toda captura de serie/lote en las pantallas de este lote es por teclado (campos de texto o
  `textarea` separados por coma/renglón), confirmado en `ReceiptDetailScreen`, `PickBatchDetailScreen`,
  `CycleCountDetailScreen` y los modales de ajuste/transferencia de inventario.
- Órdenes de transporte: alta, edición, confirmación, cancelación, cotización, chofer, importación — `OrderListScreen`/
  `OrderDetailScreen` no tienen botón "Nuevo" ni acciones de fila, confirmado.
- Viajes, rutas, flota, facturación, COD, contratos, portal de clientes.
- Administración de indicadores/gráficos de Pulso: las tarjetas del panel "Almacén" se calculan en cliente
  (`useWarehousePulse`), no hay una definición nueva en `ReportDefinition`/`IndicatorDefinition`/`ChartDefinition`.
