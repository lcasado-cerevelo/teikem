# Lote 13 — Cambios de Almacén, tanda 3: Recibo y Recolección y empaque; qué se construyó y decisiones a revisar

> **Equivalencia:** este documento = Lote 3 del plan de cambios (plan 0+1 = 11, plan 2 = 12, plan 3 = 13, plan 4 = 14, plan 5 = 15).
> El repositorio ya tenía lote1…lote12, así que el cierre de cada lote del plan usa el siguiente número libre.

Fecha: 2026-09-30. Origen: el documento de cambios del dueño del producto sobre las pantallas de Almacén
(`F:\Download\Cambios.pdf`, páginas de Recibo y Recolección y empaque). Plan: `C:\Users\Luis\.claude\plans\fuzzy-sleeping-cocoa.md`,
secciones "Decisiones del dueño del producto (2026-09-30)" y "Lote 3". Cierra el backend y el frontend web de Recibo y de
Recolección y empaque, y trae los ajustes del Lote 12 que el dueño decidió el 2026-09-30 (ver más abajo). Los lotes 4 y 5 del
plan (Transferencias, Conteo y Kárdex; Pulso) siguen pendientes.

Como los lotes 11 y 12, este documento cubre también el frontend web: no hay un `docs/frontend/loteF…-decisiones.md` aparte. El
manual funcional está en [`docs/manual/06-inventario-y-almacen.md`](manual/06-inventario-y-almacen.md) (secciones 4, 5 y 7); el
de pantallas, en [`docs/manual/frontend/f6-almacen-e-inventario.md`](manual/frontend/f6-almacen-e-inventario.md) (Recepción,
Tareas de almacén y Recolección y empaque); los mensajes de error, en [`docs/manual/faq.md`](manual/faq.md) (sección "Lote 13").

## Mapa de lo construido

| Capa | Tablas / columnas | Código principal | Endpoints |
|---|---|---|---|
| Esquema | `ReceiptHeader`: `DefaultStagingBinId INT NULL` (posición de recepción por defecto; FK compuesta `FK_Receipt_StagingBin` contra `WarehouseBin(WarehouseBinId, WarehouseId)`, del mismo almacén), `Carrier NVARCHAR(80) NULL` y `Reference NVARCHAR(80) NULL`. El bloque de la tabla quedó guardado (`IF OBJECT_ID` / `COL_LENGTH`: crea la tabla o agrega las tres columnas a una existente) y la FK va en su propio lote con guarda | `Diseño/logistica-db-estructura.sql`, `Receiving.cs` (`ReceiptHeader`), `WmsDocumentConfigurations.cs` (`HasMaxLength(80)` y la FK) | — |
| Seed | `StatusCode` de `ReceiptStatus`: seis estatus (`EXPECTED`, `RECEIVING`, `DISCREPANCY`, `RECEIVED`, `RECEIVED_VARIANCE`, `PUTAWAY`); `OPEN` ya no se siembra. `StatusLateralEntry` de `RECEIPT`: `DISCREPANCY` desde `RECEIVING`; `RECEIVED_VARIANCE` desde `RECEIVING`, `DISCREPANCY` y `EXPECTED`; `PUTAWAY` desde `RECEIVED_VARIANCE`. Bloque idempotente para bases ya sembradas: reetiqueta `RECEIVED` ("Completado") y `PUTAWAY` ("Acomodado") con su orden y color, pasa los recibos `OPEN` a `RECEIVING` (con líneas) o `EXPECTED` (sin líneas) con una fila de `EntityStatusHistory` ("Lote 13: nuevo ciclo de estatus del recibo.") y retira `OPEN` (`IsActive = 0`, `IsInitial = 0`) | `Diseño/logistica-db-seed.sql` | — |
| Dominio | (sin tablas) | `ReceiptStatusRules` (nueva, pura: `OpenTarget`, `ConfirmedTarget`, `Path`, `IsOpen`, `IsConfirmed`, `IsPosted`, `PhaseCodes`, `ParseVariance`), `ReceiptStatuses` (`Expected`, `Receiving`, `Discrepancy`, `Received`, `ReceivedWithVariance`, `Putaway`, `OpenCodes`, `ConfirmedCodes`; se eliminó `Open`), `ReceiptRules` (mensajes nuevos, `ValidateManualExpectedQty`, `PatchText`, `CreateText`), `ClientQueries.StatusIdsAsync` | — |
| API — recibo | (usa `ReceiptHeader`, `ReceiptLine`) | `ReceiptService`: `SyncOpenStatusAsync` (lleva el estatus abierto a lo que dicen las líneas, siempre por `StatusService`, con historial por paso), `UpdateHeaderAsync` (nuevo), alta sin líneas, `expectedQty` en ciegos y devoluciones, cambio de producto en líneas sin documento, filtros `phase` y `variance`, `HeadersAsync` (transporte, referencia, llegada esperada, posición por defecto, `isOpen`, `pendingPutawayCount`), `ConfirmCoreAsync` (destino `RECEIVED` o `RECEIVED_VARIANCE`); `ReceivingContracts.cs` | `GET /api/v1/receipts` (`variance[]`, `phase`), `GET /api/v1/receipts/{publicId}` (`canDelete` y campos nuevos), `POST /api/v1/receipts` (`carrier`, `reference`, encabezado sin líneas), **`PATCH /api/v1/receipts/{publicId}`** (nuevo, `warehouse.receive`), `POST` y `PUT /api/v1/receipts/{publicId}/lines` (`expectedQty`, `productPublicId`), `POST /confirm`, `DELETE` |
| API — avisos | (usa `Asn`) | `AsnService.ListAsync`, `AsnQuery` | `GET /api/v1/asns` (`reference`, `expectedFrom`, `expectedTo`) |
| Retiro de `OPEN` en el resto del backend | (usa `ReceiptHeader`) | `WarehouseTaskStatusEffect` (el acomodo cierra desde `RECEIVED` o `RECEIVED_VARIANCE`), `CrossDockService` (abiertos = modo (a); confirmados o `PUTAWAY` = modo (b)), `ProductService.DeactivateAsync`, `WarehouseService` (baja de almacén), `PurchaseOrderService.ReceiptFlagsAsync`, `PurchaseShortageService` (confirmado incluye `RECEIVED_VARIANCE`), `ActivityRules` y `WarehouseActivityProvider` (`RECEIVED_VARIANCE` también genera `RECEIPT_CONFIRMED`), `ReceiptDataSource` (campos `Carrier` y `Reference`) | — |
| Ajustes del Lote 12 (backend) | (usa `PurchaseOrder`, `StockBalance`) | `PurchaseOrderService.UpdateAsync` (proveedor y almacén en Borrador), `AdjustmentRules.NotesRequired` y `RequiresNotes`, `PurchaseShortageService` (nota obligatoria con `MANUAL_ADJUSTMENT`), `ProductService` (`onlyOnHand`), `InventoryReadService` (`activeProductsOnly`) | `PATCH /api/v1/purchase-orders/{publicId}` (`supplierId`, `warehousePublicId`), `POST /api/v1/inventory/adjustments` (nota obligatoria), `GET /api/v1/products` (`onlyOnHand`), `GET /api/v1/inventory/balances` (`activeProductsOnly`) |
| Contratos web y móvil | Sin tablas | `web-app/openapi.json`, `web-app/src/kernel/api/schema.d.ts` y `app-almacen/src/kernel/api/schema.d.ts` regenerados | — |
| Kit (web) | Sin tablas | `SplitPane.tsx` y `splitRatio.ts` (dos paneles con barra arrastrable), `ListPager.tsx` y `pageSize.ts` (pie de lista con Exportar), `useElementWidth.ts`, `DataTable.forceCards`, `Field.hideLabel`, cuatro íconos de acción nuevos en `actionIcons.tsx` (`IconUserPlus`, `IconPlay`, `IconCheckCircle`, `IconXCircle`), `ui.css`, `KIT.md` | — |
| Frontend — Recibo | — | `ReceiptListScreen.tsx` (reescrita), `ReceiptFilterBar.tsx`, `ReceiptMasterList.tsx`, `ReceiptDetailPanel.tsx`, `ReceiptLinesEditor.tsx`, `useReceiptLineRows.ts`, `receiptLineEdit.ts`, `ReceiptLineCaptureModal.tsx`, `ReceiptHeaderModal.tsx`, `ReceiptPutawayTasks.tsx`, `PutawayPendingTab.tsx`, `AsnsTab.tsx`, `AsnCreateModal.tsx`, `receiptFilters.ts`, `useReceiptList.ts`, `taskActions.tsx` (acciones como íconos). Se borró `ReceiptDetailScreen.tsx` | — |
| Frontend — Recolección y empaque | — | `PickBatchListScreen.tsx` (dos paneles), `CollectPanel.tsx` y `collectForm.ts`, `PickBatchesPanel.tsx`, `OrderNumberFilter.tsx`, `PickBatchDetailModal.tsx` y `PickBatchDetailBody.tsx` (también usado por `PickBatchDetailScreen.tsx`), `PackModal.tsx`, `DeletePickBatchDialog.tsx`, `pickBatchView.ts` | — |
| Frontend — rutas, menú y enlaces | — | `routes.tsx` (`redirectWithParams`, `legacyReceiptSearch`; `/warehouse/receipts/:publicId` redirige; orden del menú Recibo 60, Recolección y empaque 70, Ajustes de inventario 80, Conteo 90, Cruce 100, Kárdex 110), `activity.ts` (el evento de recibo abre `?receipt=`), `analytics/api.ts` (Pulso "Recibos abiertos" usa `phase=OPEN`) | — |
| Frontend — ajustes del Lote 12 | — | `PurchaseOrderDetailScreen.tsx` y `purchaseOrderEdit.ts` (proveedor y almacén editables en Borrador), `InventoryAdjustModal.tsx` y `ResolveShortageModal.tsx` (nota obligatoria vía `adjustNotesSchema`), `ProductListScreen.tsx` y `productFilters.ts` (KPI "Unidades totales"), `PackModal.tsx` con `useTenantSettings` (predeterminados de la compañía) | — |
| Humo | — | `scripts/smoke.sh`: ciclo completo del recibo (encabezado sin líneas en `EXPECTED`, `DISCREPANCY`, `RECEIVING`, `RECEIVED_VARIANCE`, `PUTAWAY` con su historial), filtros `phase` y `variance`, `PATCH` del encabezado, esperado y tipo con documento, avisos por referencia y fecha, y los `jq` de estatus ajustados | — |

Todo bajo el filtro de tenant existente: `ReceiptHeader` sigue con `TenantId`; `Carrier`, `Reference` y la posición por defecto no
cambian su aislamiento (la FK compuesta obliga a que la posición sea del mismo almacén). No hay permisos ni módulos nuevos: el
`PATCH` del recibo usa `warehouse.receive`, como el resto de la captura. La prueba de seguridad de controladores
(`WmsControllerSecurityTests`) ya declara la acción nueva (de 107 a 108).

## Qué se construyó

### Backend del Recibo

- **Ciclo de estatus.** Esperado → Recibiendo → Discrepancia → Completado / Completado con diferencia → Acomodado. Los seis son
  `StatusCode` reales: nada se deriva al vuelo, así que funcionan el filtro por estatus, los overrides por compañía, el chip, la
  fuente `RECEIPT` y el historial. Todas las transiciones van por `StatusService.TransitionAsync`.
  - Los tres abiertos (Esperado, Recibiendo, Discrepancia) los sincroniza el servicio dentro de la transacción de cada alta,
    cambio o baja de línea. Un recibo nunca vuelve a Esperado.
  - Completado y Completado con diferencia son el destino de la confirmación. Acomodado se conserva como paso final: lo pone el
    efecto de las tareas al cerrar la última de acomodo, o la propia confirmación si no quedó nada por acomodar.
  - `OPEN` se retira. Sus recibos pasan a Recibiendo o Esperado (ver el seed).
- **Recibos contra orden de compra o aviso nacen en Recibiendo** (o Discrepancia si lo capturado en `lines` difiere). Esperado
  es solo para ciegos y devoluciones sin líneas.
- **Encabezado editable** (`PATCH`, solo abierto): Transporte y Referencia (hasta 80), posición de recepción por defecto, muelle, tipo
  Ciego ↔ Devolución (solo sin documento) y almacén (solo sin documento y sin líneas; limpia posición y muelle). Ciegos y
  devoluciones se crean solo con el encabezado y sus líneas se agregan después.
- **Esperado en ciegos y devoluciones.** Cada línea admite `expectedQty` (≥ 0). **Lo recibido es lo que entra al inventario**: un solo
  `RECEIPT` por lo recibido, sin `ADJUSTMENT`; la diferencia solo marca el estatus (Discrepancia, Completado con diferencia). Con
  aviso u orden de compra sigue D4 (`RECEIPT` por lo esperado + `ADJUSTMENT RECEIPT_VARIANCE`).
- **Filtros de la lista.** `variance` (`SHORT`, `OVER`, `NONE`, varias con "o") y `phase` (`OPEN`, `PENDING_PUTAWAY`, `DONE`). La
  fila trae ahora transporte, referencia, llegada esperada, posición por defecto, `isOpen` y `pendingPutawayCount`; la ficha, `canDelete`.
- **Avisos de llegada.** Filtros `reference` (contiene, sin distinguir mayúsculas) y `expectedFrom`/`expectedTo` (inclusive).

### Frontend del Recibo

- **Recibo en una sola pantalla**, maestro-detalle: lista de recibos a la izquierda (340 px) y detalle del elegido a la derecha. El
  elegido va en `?receipt=<publicId>`; la ruta vieja `/warehouse/receipts/:publicId` redirige. En celular (bajo 720 px) una columna.
- **Pestañas Recibos / Avisos de llegada / Acomodo pendiente** (`?tab=`).
- **Encabezado.** Doble clic en un recibo de la lista, o el lápiz del detalle (la vía con teclado), abre el modal del encabezado. Si el
  recibo ya está confirmado (o el usuario no tiene `warehouse.receive`), el modal es de solo lectura, con un aviso si está confirmado.
- **Captura de líneas en la rejilla del detalle.** Sin documento: producto, esperado, recibido y una fila vacía al final; lo
  recibido se copia a lo esperado mientras se teclea. Con OC o aviso: lo esperado queda fijo y **la web no ofrece "Añadir ítem"**.
  Guardado por fila (al salir del campo o con Enter). "Confirmar recibo" se bloquea con el motivo debajo.
- **Acomodo pendiente**: lista de recibos Completados o Completados con diferencia con tareas de acomodo abiertas (mismos filtros que
  Recibos) y, a la derecha, las tareas del recibo elegido: asignar, iniciar, completar y cancelar.

### Frontend de Recolección y empaque

- **Dos paneles con barra arrastrable** (`SplitPane`): Recolección a la izquierda (captura) y Recolecciones a la derecha (lista). Arranca
  60/40, recuerda su posición en `localStorage` (`teikem.split.pick-batches`), Enter (o doble clic) vuelve a 60/40, flechas ±5 %. En
  celular, un panel debajo del otro, sin barra.
- **Captura de recolección en rejilla** de varias líneas (hasta 100) y un solo botón "Recolectar (bajar de inventario)"; no navega a
  la ficha: limpia las líneas, avisa y resalta la recolección nueva.
- **Empacar y Eliminar como íconos en la fila** de la recolección. **El detalle se abre en un modal** al hacer clic en la fila (la ruta
  `/warehouse/pick-batches/:publicId` se conserva).
- Sin `warehouse.pick` no hay panel izquierdo: la lista ocupa todo el ancho.

### Menú

Orden del grupo Almacén: … Recibo → Recolección y empaque → Ajustes de inventario → Conteo cíclico → Cruce de muelle → Kárdex de
movimientos. El Lote 14 insertará "Transferencias y ajustes".

## Ajustes del Lote 12 que entraron con este lote

Decisiones del dueño del producto del 2026-09-30 (las reglas del backend están en la sección "Ajustes decididos tras el cierre" de
[`lote12-decisiones.md`](lote12-decisiones.md); aquí, lo que quedó completo con la pantalla):

1. **Orden de compra: proveedor y almacén editables solo en Borrador.** El API acepta `supplierId` y `warehousePublicId` en el
   `PATCH`; fuera de Borrador, un valor distinto da 409. La ficha los muestra como campos editables solo en Borrador (proveedor con
   buscador sobre los activos, almacén con el selector del alta) y manda solo lo que cambió.
2. **Nota obligatoria en todo ajuste manual, también en el API.** `POST /api/v1/inventory/adjustments` responde 400 en `errors.notes`
   (`Escriba una nota que explique el ajuste.`) si falta. Los ajustes que hace el sistema no la exigen. **También es obligatoria al
   resolver un faltante con `MANUAL_ADJUSTMENT`** (Cerrar y Reordenar la dejan opcional). La pantalla ya la exige en el modal de producto,
   en el modal de Ajustes del Kárdex (`InventoryAdjustModal`) y en el modal de faltantes.
3. **KPI "Unidades totales".** Su filtro muestra los productos **activos con existencia en mano mayor que cero** (`onlyOnHand`, sin
   restar lo reservado ni excluir cuarentena) y **su cifra suma solo productos activos** (`activeProductsOnly` en
   `GET /api/v1/inventory/balances`), así que cifra y tabla coinciden. Esto cierra la diferencia que quedó abierta en la decisión 4 del Lote 12.
4. **Empacar oculta "Predeterminado de la compañía" cuando la compañía no tiene tipo predeterminado**, y entonces el campo es
   obligatorio ("Elija el tipo de servicio." / "Elija el tipo de paquete."). Los predeterminados se leen de `GET /api/v1/tenant/settings`.
5. **Marca del producto importada desde QuickBooks en la migración.** La columna `Brand` de QuickBooks pasa a `Product.Brand` al crear
   el producto (`import-legacy`). El código ya estaba en el commit del Lote 12 (`LegacyImportService`); este lote no lo cambia. Se
   documenta en [`manual/10-migracion-de-datos.md`](manual/10-migracion-de-datos.md) (sección 1).

## Ajustes visuales de la verificación en navegador

- **Acciones de las tareas de almacén como íconos con tooltip** en todas las colas (Acomodo pendiente y el detalle del recibo,
  Reabasto, Conteo, Cruce de muelle): Asignar (`IconUserPlus`), Iniciar (`IconPlay`), Completar (`IconCheckCircle`) y Cancelar
  (`IconXCircle`). El nombre accesible sigue siendo el de la acción.
- **Tabla de tareas de acomodo compacta**, y sus encabezados solo se parten entre palabras (`.rcp-tasks .lst th`: sin partir dentro de una palabra).

## Cómo se prueba

Corridas del 2026-09-30 sobre el código de este lote.

1. `dotnet build Teikem.sln && dotnet test Teikem.sln` — **2.524 pruebas en verde**, compilación sin avisos (el Lote 12 cerró con 2.483 tras sus ajustes).
   Pruebas nuevas o ajustadas:
   - `ReceiptStatusRulesTests` (nuevo): destino abierto por líneas y estatus actual, destino de la confirmación, camino escalonado,
     respaldos con estatus apagados por la compañía, fases, filtro de diferencia y los mensajes nuevos.
   - `ReceiptLifecycleTests` (nuevo): el ciclo completo sobre el servicio con historial por paso; ciego solo con encabezado; esperado
     sin documento; cambio de producto; `PATCH` del encabezado; filtros `phase` y `variance` (en memoria y su traducción a SQL Server) y
     filtros de avisos. `ReceiptStatusSeed` (ayudante) siembra las entradas laterales como el seed.
   - `ProductOnHandFilterTests`, `PurchaseOrderSupplierWarehouseEditTests` y `TenantDefaultsForPackingTests` (nuevos): los ajustes del Lote 12.
   - Ajustadas: `ReceiptServiceTests`, `WarehouseTaskStatusEffectTests` (desde `RECEIVED_VARIANCE`), `ActivityRulesTests`,
     `CrossDockServiceTests`, `PurchaseShortageServiceTests`, `SyncRulesTests`, `AnalyticsSeedFieldsTests`, `WmsCatalogTests` (filas del seed
     y reglas laterales), `WmsFixture`, `WmsContractsTests`, `WmsControllerSecurityTests` (108 acciones), `InventoryAdjustmentServiceTests`,
     `AdjustmentRulesTests` e `InventoryReadServiceTests` (`activeProductsOnly`).
2. Web (`cd web-app && npm run check`): `tsc` limpio, oxlint sin errores, vitest **80 archivos / 712 pruebas en verde**. Archivos de
   prueba nuevos: `SplitPane.test.tsx`, `splitRatio.test.ts`, `ListPager.test.tsx`, `receiptFilters.test.ts`, `receiptLineEdit.test.ts`,
   `ReceiptScreen.test.tsx`, `collectForm.test.ts`, `PickBatchScreen.test.tsx`, `PickBatchPack.test.tsx`, `pickBatchView.test.ts`,
   `purchaseOrderEdit.test.ts`, `InventoryAdjustModal.test.tsx`, `tenantSettings.test.tsx` y `redirects.test.tsx`. Ajustados:
   `navigation.test.ts`, `Pulse.test.tsx`, `ActivityPanel.test.tsx`, `warehouseScreens.test.tsx`, `DataTable.test.tsx`, `Form.test.tsx`,
   `ProductListScreen.test.tsx`, `productFilters.test.ts`, `PurchaseOrders.test.tsx` e `InventoryAdjustmentsScreen.test.tsx`.
3. App móvil (`cd app-almacen`, `npm ci --legacy-peer-deps` como el CI): `tsc` limpio, oxlint limpio y Jest **33 suites / 145 pruebas en
   verde** con el `schema.d.ts` regenerado. La app no lee el estatus del recibo (solo manda
   `POST /receipts` con `confirm: true`).
4. Recorridos Playwright (`web-app/e2e`, contra el API real, con el Chrome instalado porque la descarga del navegador de Playwright falla
   en esta red): **40 pasaron, 0 fallaron**.
   - `f6.spec.ts`, pasos 7 a 9 reescritos: recibo ciego en el detalle (lo recibido copia lo esperado, Recibiendo, Completado, nace la tarea
     `PUTAWAY`); Acomodo pendiente (asignar, iniciar y completar → Acomodado); recolección en el panel y empaque desde la fila.
   - `lote13.spec.ts` (nuevo): recibo con diferencia 12/10 → Discrepancia con aviso → Completado con diferencia y tarea de acomodo por 10;
     barra de Recolección con teclado, recuerdo al recargar y Enter a 60/40; móvil (360 px) sin scroll horizontal en las tres pestañas de
     Recibo y en Recolección, con los paneles apilados y sin barra.
   - `web-app/playwright.local-chrome.config.ts` (sin seguimiento) es una configuración temporal para usar el Chrome local; su propio
     comentario dice que no se commitea.
5. Verificación manual en el navegador, en la compañía demo Advance Logistics, a 1440 y a 360 px: esperado copiado automáticamente,
   Recibiendo, Discrepancia con −2 y su aviso, Completado con diferencia, tarea completada → Acomodado, un recibo sin diferencia →
   Completado, y el modal del encabezado en solo lectura con su aviso.
6. Las capturas del manual (`docs/manual/frontend/img/f6-recepcion.png`, `f6-recibo-nuevo.png`, `f6-recibo-ficha.png`, `f6-tareas.png`,
   `f6-tarea-completar.png`, `f6-recolecciones.png`, `f6-recoleccion-nueva.png`, `f6-recoleccion-ficha.png`, `f6-empacar.png`) las regeneró el
   recorrido `f6.spec.ts` después del ajuste de encabezados de la tabla de acomodo.

7. **Base desde cero, repetible** (con los archivos de `docs/migracion/`): `db-reset --yes` y luego `import-legacy` de
   `import.depot.json` e `import.solutions.json`: sin rechazos en Depot (163 advertencias, 1.310 asientos de saldo inicial) ni en
   Solutions (35 advertencias, 32 asientos).
8. **`scripts/smoke.sh` completo contra esa base: `SMOKE OK`.** Para llegar ahí se corrigieron seis cosas del humo; solo una era
   de este lote, las otras venían del checkpoint del 29-sep (Lote 10) y hacían que el humo no llegara al final:
   - `req()` terminaba con `[[ -n "$bodyfile" ]] && rm …`, que devuelve 1 sin cuerpo: con `set -e`, cualquier `VAR=$(req GET …)`
     cortaba el script sin mensaje (se cortaba en el aislamiento de rutas del Lote 5).
   - `AdjustmentReason` esperaba 9 motivos; con `OPENING_BALANCE` (Lote 10) son 10.
   - **De este lote:** resolver un faltante con `MANUAL_ADJUSTMENT` ahora exige nota; se agregó al humo y un paso nuevo comprueba
     el 400 `errors.notes` sin ella.
   - `activity()` acumulaba los eventos con `--argjson`: en una base ya usada supera el límite de argumentos de Windows; ahora
     acumula en un archivo temporal.
   - El paso de MFA por usuario pide AAL2 reciente; si el humo tarda más que la ventana del tenant, vencía: ahora se reautentica
     antes.
   - Migración con `--update` en dry-run: el humo esperaba "el saldo inicial no se toca", pero la regla D51 dice que en la
     primera carga de la compañía sí se carga (la prueba de xunit ya lo exigía); el paso se alineó con D51.

### Qué no se probó

Los reportes recibidos no cubren lo siguiente; nada de esto se probó al escribir este documento:

- **El CI de GitHub Actions** con este código: se confirma al hacer push (el humo local ya pasó completo).
- **El seed sobre una base ya existente** (el bloque que pasa `OPEN` a Recibiendo o Esperado y reetiqueta `RECEIVED` y `PUTAWAY`). Según
  el plan, ni `import-legacy` ni el tenant demo traían recibos antes de este lote, así que ese `INSERT` y ese `UPDATE` no vieron filas
  con estatus `OPEN`.
- **En el navegador**: recibos contra orden de compra y contra aviso (con lo esperado fijo), la pestaña Avisos de llegada y sus filtros,
  el alta de un aviso, el modal de lote y series de una línea, borrar un recibo, cambiar tipo, almacén o muelle desde el modal de
  encabezado, y la captura de Recolección con productos por lote o por serie (columna Lote, botón "Series (n)").
- **El arrastre de la barra con el ratón o el dedo.** El recorrido cubre el teclado; el arrastre lo cubren las pruebas de `SplitPane`.
- **Compañías que apagan Recibiendo, Discrepancia o Completado con diferencia.** Lo cubren pruebas unitarias del servicio, no un
  recorrido real (sí se cubre el caso con todo encendido).
- **El 400 de la nota** (`Escriba una nota que explique el ajuste.`) y el 409 del proveedor y almacén de la orden se cubren con pruebas de
  xunit, de pantalla y del humo.

## Decisiones a revisar

1. **Lo esperado que la pantalla copia queda guardado.** En un ciego o devolución, mientras se teclea lo recibido la pantalla copia el valor
   a lo esperado, y al guardar la fila manda `expectedQty` con ese valor. Ya guardada, la fila tiene un esperado distinto de vacío o 0 y
   **deja de copiar**: si después se corrige lo recibido, lo esperado no lo sigue y aparece la diferencia (Discrepancia). Así lo lee el
   código (`receiptLineEdit.ts`: `mergeSaved`, `rowFromLine`); no se probó en el navegador con esa secuencia. Alternativa: no mandar
   `expectedQty` cuando coincide con lo recibido.
2. **En ciegos y devoluciones el Kárdex no deja rastro de la diferencia.** Entra un solo `RECEIPT` por lo recibido y `AdjustmentTxnId`
   queda vacío; la diferencia se ve en el estatus y en su historial. Consecuencia: el indicador "Recepciones con diferencia"
   (`HasVariance` de la fuente `RECEIPT`) ahora también cuenta ciegos con un esperado capturado distinto de lo recibido; antes un
   ciego nunca tenía diferencia.
3. **Un recibo nunca vuelve a Esperado.** Si se borran todas sus líneas queda en Recibiendo con 0 líneas; confirmar da 422 `El recibo no
   tiene líneas; agregue al menos una antes de confirmar.` y en la pantalla el botón queda deshabilitado con "Agregue al menos una línea
   para confirmar.". Borrar el recibo sigue siendo posible mientras esté abierto.
4. **Se sembró de más una entrada lateral:** `RECEIVED_VARIANCE` también desde `EXPECTED` (el plan decía solo desde `RECEIVING` y
   `DISCREPANCY`). Solo se usa si la compañía apaga Recibiendo (el recibo se queda en Esperado hasta confirmarse y se confirma directo).
5. **Se cambiaron las etiquetas de `RECEIVED` ("Recibida" → "Completado") y de `PUTAWAY` ("En putaway" → "Acomodado")** con un `UPDATE` en el
   seed sobre las filas globales. Una compañía que haya personalizado esas etiquetas conserva la suya (no se verificó). El código
   `RECEIVED` no cambia: sigue siendo el que usan el historial, las reglas de actividad, el cruce de muelle y el humo.
6. **`OPEN` ya no existe.** Un cliente del API que filtraba `status=OPEN` recibe la lista vacía (no un error); debe usar `phase=OPEN`. La
   app móvil no lee el estatus del recibo. El Pulso ("Recibos abiertos") ya usa `phase=OPEN`. Falta revisar integraciones externas, si las hay.
7. **La web no ofrece "Añadir ítem" en un recibo con orden de compra o aviso** (decisión del dueño); el API sí sigue admitiendo líneas
   extra (entran como ajuste `RECEIPT_VARIANCE` y no cuentan contra la orden) y la app móvil las manda. Desde la web, un sobrante de un producto
   que no venía en el documento no se puede registrar.
8. **Un ciego o una devolución creado desde la web nace sin líneas**, y quien cierra la pestaña antes de capturar deja un recibo en Esperado
   (borrable). Antes el API respondía 400 en ese caso; una integración que dependía del 400 ahora recibe 200 (ver el FAQ del Lote 13).
9. **Doble clic para abrir el encabezado.** En pantallas táctiles el doble clic no es fiable; el lápiz del detalle hace lo mismo y es la
   vía con teclado. No hay otro gesto táctil (por ejemplo, mantener pulsado).
10. **"Acomodo pendiente" se define por el estatus** (Completado o Completado con diferencia), no por contar tareas. Un recibo confirmado sin nada
    que acomodar pasa directo a Acomodado y no aparece. Asignar y cancelar tareas exigen `warehouse.manage`; iniciar y completar,
    `warehouse.receive`: un usuario con `warehouse.receive` sin `warehouse.manage` ve la lista y trabaja las tareas, pero no las asigna.
11. **No existe ya una cola única de "Tareas de almacén".** Cada tipo se trabaja en su pantalla (acomodo en Recibo, reabasto en Recolección y
    empaque, conteo en Conteo cíclico, cruce en Cruce de muelle). Ya era así desde la reconciliación con la maqueta; `/warehouse/tasks`
    redirige a Acomodo pendiente, así que por esa dirección no se llega a las tareas de reabasto. El manual de pantallas todavía
    describía la cola única y se corrigió aquí.
12. **El filtro de Almacén de la lista de recibos es de uno solo** (el API filtra por un almacén); los demás filtros son múltiples. Los
    avisos de llegada siguen saliendo como arreglo de hasta 200 (la app móvil lo descarga así): con más de 200 que cumplan los filtros no hay
    aviso en pantalla de que falten; hay que afinar los filtros.
13. **La sugerencia de posición FEFO y la pista "N disp." se calculan en el navegador** (réplica de `PickBatchRules.Eligible` sobre
    `GET /inventory/balances?onlyAvailable=true&take=200`). Con un producto de más de 200 saldos la sugerencia y la pista pueden quedar
    incompletas. Es solo una pista: con la posición vacía el servidor elige por FEFO. Si la réplica y el servidor difieren, la pista puede no
    coincidir con la posición que el servidor elija.
14. **La posición de la barra se guarda por navegador** (`localStorage`), no por usuario ni por compañía. Si el navegador no permite guardarla,
    vale solo en esa visita. La barra respeta un mínimo de 35 % y un máximo de 75 % y un ancho mínimo de 420 y 320 px por panel; si el espacio
    no alcanza para los dos, o la ventana mide 900 px o menos, los paneles se apilan sin barra.
15. **Predeterminados de la compañía al empacar.** Mientras cargan (o si no se pueden leer) se tratan como ausentes: el campo es obligatorio y el
    API siempre acepta el valor explícito. Si el valor predeterminado ya no está en el catálogo (deshabilitado), la opción muestra su código en
    lugar de la etiqueta.
16. **Detalle menor del empaque:** el error "Indique al menos una línea de paquete." se pinta bajo el campo Tipo de servicio (así está
    declarado en `PackModal.tsx`), no bajo los paquetes.
17. **Comentarios de código desactualizados.** `ProductListScreen.tsx` (cabecera) y `productFilters.ts` todavía dicen que la cifra de
    "Unidades totales" incluye productos inactivos; ya no es así (`api.ts` usa `activeProductsOnly` y el aviso del indicador dice "La
    cifra suma la existencia en mano de los productos activos."). Solo comentarios; no afecta el comportamiento.
18. **El script de estructura sigue sin ser idempotente fuera de lo guardado** (decisión 17 del Lote 12). `ReceiptHeader` ahora lleva
    guarda y agrega las tres columnas a una base existente sin tocar sus datos; el resto de las tablas sigue con `CREATE TABLE` a secas.
    Aplicar el cambio a una base con datos que no salga de la migración: `db-init` sobre la base existente (no probado, ver arriba) o `db-reset`
    + `import-legacy` (el dueño aceptó recrear la base).

## Qué queda fuera de este lote (a propósito)

- **Lote 4 del plan** (Transferencias, Conteo y Kárdex; incluye "Transferencias y ajustes" en el menú) y **Lote 5** (Pulso). El Lote 14 y el 15 del repositorio.
- **Motor de trabajos programados** (diferido): no hay ninguna tarea que corra sola; el reabasto sigue siendo manual ("Correr reabasto").
- **Indicadores de Contabilidad** (diferidos): el Pulso de Contabilidad no cambió en este lote.
- **Líneas extra en la web con orden de compra o aviso** (decisión 6 del dueño): solo por la app o por el API.
- **Orden por columna en el servidor** de la lista de recibos y de recolecciones: el orden por encabezado solo reacomoda la página visible
  (decisión 4 del Lote 11). La lista maestra de recibos siempre llega por fecha de creación descendente.
- **Filtro de varios almacenes en Recibo**: el API filtra por uno.
- **Capturas nuevas** de la pestaña Avisos de llegada, del modal del encabezado en edición, de la captura con lote y series y de la barra
  de Recolección arrastrada (quedan notas "Captura pendiente" en el manual de pantallas).
- **Corrida del CI** con este código (se confirma al hacer push).
