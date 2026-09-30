# Lote 16 — Recibo directo a posición (almacén con acomodo o directo), sugerencia de posición con cupo, app con posición destino y trabajo adicional del día (selector de Recolección, formato de números, exportaciones); qué se construyó y decisiones a revisar

> **Nota de numeración:** el plan de cambios de Almacén tenía los lotes 0 a 5 (registros 11 a 15) y terminó en el Lote 15. **El Lote 16 no es parte de ese plan**:
> es un pedido nuevo del dueño del producto del 2026-09-30 ("recibir sin pasar por staging"), diseñado aparte. En el mismo commit entra el trabajo adicional del día.

Fecha: 2026-09-30. Origen: el hallazgo H13 del arquitecto (el documento maestro decía que la mercancía recibida "siempre queda en
staging") y las decisiones del dueño del producto del 2026-09-30 (D1 a D13, todas la opción A; ver más abajo). Cierra el backend, el
frontend web y la app de almacén de estas piezas. **Además entra en el mismo commit el trabajo adicional del día** (pedidos del dueño
hechos después del diseño del lote): selector de posiciones de Recolección, formato único de números, exportar Recibos con sus líneas, encabezado y
filtros en las exportaciones y fechas como fecha en Excel y CSV. Va en su propia sección.

Como los lotes 11 a 15, este documento cubre también el frontend web: no hay un `docs/frontend/loteF…-decisiones.md` aparte. El manual
funcional está en [`docs/manual/06-inventario-y-almacen.md`](manual/06-inventario-y-almacen.md) (secciones 1, 4, 5, 7 y 9) y en
[`docs/manual/09-app-almacen.md`](manual/09-app-almacen.md) (sección 4); el de pantallas, en
[`docs/manual/frontend/f6-almacen-e-inventario.md`](manual/frontend/f6-almacen-e-inventario.md) (Almacenes, Recepción, Recolección y
"Exportar"); los mensajes de error nuevos y las preguntas, en [`docs/manual/faq.md`](manual/faq.md) (sección "Lote 16").

## Nota sobre Advance Depot y Advance Solutions

Las dos compañías se recrearon **con el código nuevo** (`db-reset` + `import-legacy`). No se escribió nada en ellas fuera del seed y de la
migración.

- **Advance Solutions (`ALM-SOL`)** nació en modo **DIRECT** por su configuración de migración (`"receivingMode": "DIRECT"`). Tiene una sola
  posición (`GENERAL`, zona RESERVE) y ninguna zona STAGING: sin el modo directo no podía recibir (hallazgo H1, sin verificar contra la base
  antes del lote).
- **Advance Depot (`ALM-DEPOT`)** nació en **PUTAWAY** (con acomodo) con la posición de recepción por defecto **`R1`** (zona `STG`, de tipo
  STAGING). Antes la posición por defecto salía de `DefaultStagingBinAsync`, que elige por código de zona y daba `S1` de la zona "Embarque"
  (hallazgo H2, sin verificar contra la base).
- **La demo (`ALM-01`)** sigue en PUTAWAY, sin posición por defecto.

## Mapa de lo construido

| Capa | Tablas / columnas | Código principal | Endpoints |
|---|---|---|---|
| Esquema | **`dbo.Warehouse.ReceivingModeLookupId`** `INT NULL` (modo de recepción; `NULL` = PUTAWAY) y **`DefaultReceivingBinId`** `INT NULL` (posición de recepción por defecto) con `FK_Warehouse_ReceivingMode` y la FK compuesta `FK_Warehouse_DefaultReceivingBin (DefaultReceivingBinId, WarehouseId) → WarehouseBin(WarehouseBinId, WarehouseId)` (el almacén y la posición son el mismo). **`dbo.ReceiptHeader.ReceivingModeLookupId`** `INT NULL` (copia del modo con que se abrió) con `FK_Receipt_ReceivingMode`. **`dbo.ReceiptLine.TargetBinId`** `INT NULL` (posición destino) con `FK_ReceiptLine_TargetBin`. `Warehouse` y `ReceiptLine` pasan a bloques con guarda (`IF OBJECT_ID … CREATE … ELSE IF COL_LENGTH … ALTER`); las FK, cada una en su lote con `IF OBJECT_ID(…, 'F') IS NULL`; `FK_UserDevice_DefaultWarehouse` también queda guardada. Sin tablas, permisos ni estatus nuevos | `Diseño/logistica-db-estructura.sql`, `Warehouse` y `ReceiptHeader` y `ReceiptLine` (`Warehouse.cs`, `Receiving.cs`), `WarehouseConfigurations.cs`, `WmsDocumentConfigurations.cs` (EF **sin relaciones** para `DefaultReceivingBinId` y `ReceivingModeLookupId`: las FK viven solo en el SQL) | — |
| Seed | Dominio `ReceivingMode` y dos valores: `PUTAWAY` "Con acomodo" y `DIRECT` "Directo a posición". Bloque "Lote 16": los almacenes y los recibos con modo `NULL` pasan a PUTAWAY (idempotente: solo toca los `NULL`) | `Diseño/logistica-db-seed.sql`, `LookupDomains.ReceivingMode` y `ReceivingModes` (`CatalogDomains.cs`), `DemoTenantSeeder.cs` (la demo con PUTAWAY explícito) | — |
| Dominio | (sin tablas) | **`ReceivingModeRules`** (nueva, pura: `ParseMode`, `Normalize`, `Effective`, `CreateMode`, `NeedsTarget`, `ValidateTargetZone`, `FreeQty`, `Exceeds`, `PostingBin`; los mensajes públicos). `PutawayRules` (cupo en unidades, cuarentena primero en devoluciones con la razón `QUARANTINE_RETURN` "Cuarentena (devolución)", `Fits`), `PutawayCandidate`/`PutawayRank` con `MaxCapacityQty` | — |
| API — almacén | (usa las columnas de arriba) | `WarehouseService` (alta y `PATCH` con `receivingMode`; `PATCH` con `defaultReceivingBinId` / `clearDefaultReceivingBin`; el DTO trae código y etiqueta del modo y la posición por defecto), `ReceivingSupport.TryDefaultStagingBinAsync` (la posición por defecto del almacén primero, si sigue activa y de tipo STAGING o CROSSDOCK; si no, la primera STAGING) | `POST /api/v1/warehouses`, `PATCH /api/v1/warehouses/{publicId}` (`warehouse.manage`) |
| API — recibo | (usa las columnas de arriba) | `ReceiptService` (`CreateCoreAsync`: copia del modo y, en directo, sin exigir posición de recepción; `BuildLineAsync`/`ResolveTargetBinAsync`: destino por id o por código; `UpdateLineAsync` con `TargetBinId` y `ClearTargetBin`; `UpdateHeaderAsync` con `ReceivingMode`; `ConfirmCoreAsync`: valida el destino por línea, asienta en él, sin tareas, `Completado → Acomodado` en la misma transacción; `ApplyRequestLinesToAsnAsync`: H11) | `POST /api/v1/receipts` y `PATCH /api/v1/receipts/{publicId}` (`receivingMode`), `POST`/`PUT /api/v1/receipts/{publicId}/lines[/{lineId}]` (`targetBinId`, `targetBinCode`, `clearTargetBin`), `POST …/confirm` (`warehouse.receive`) |
| API — sugerencias | (sin tablas) | `ReceiptService.SuggestTargetsAsync` y `ApplySuggestedTargetsAsync`, `PutawaySuggester.SuggestAsync(PutawaySuggestionOptions)` (cupo contra la existencia de la posición más lo reservado por otras líneas del recibo; `ClaimedSameProductByBin`; `PreferQuarantine`; `IncludeOverCapacity`), `PutawaySuggestionDto` con `MaxCapacityQty` y `FreeQty` | **`GET /api/v1/receipts/{publicId}/lines/{lineId}/target-suggestions?take=`** (`inventory.view`; `take` 1 a 10, por defecto 5), **`POST /api/v1/receipts/{publicId}/targets/suggest`** (`warehouse.receive`), `GET /api/v1/warehouse-tasks/putaway-suggestions` (ahora respeta el cupo) |
| API — lista con líneas | (sin tablas) | `ReceiptService.ListAsync` con `IncludeLines` (las líneas de todos los recibos de la página en un solo lote de consultas), `ReceiptListItemDto.Lines` | `GET /api/v1/receipts?includeLines=true` (`inventory.view`) |
| Aparatos | (sin tablas) | `DeviceService.PreferencesAsync`: `DeviceEnrolledDto` y `DeviceHeartbeatDto` traen `DefaultWarehouseReceivingMode` (PUTAWAY o DIRECT; `null` sin almacén por defecto) | `POST /api/v1/devices/enroll`, `POST /api/v1/devices/heartbeat` (anónimos, como antes) |
| Actividad y análisis | (sin tablas) | `WarehouseActivityProvider` (D13: un recibo directo sin tareas de acomodo no emite "acomodado"), `ReceiptDataSource` y `WarehouseDataSource` con los campos `ReceivingMode` y `ReceivingModeCode` | — |
| Migración | (sin tablas) | `LegacyImportConfig.Warehouse.ReceivingMode` (PUTAWAY o DIRECT, opcional) y `DefaultReceivingBin` (código de posición), solo al crear el almacén: `--update` no los pisa; `LegacyImportService.SetDefaultReceivingBinAsync`; `docs/migracion/import.solutions.json` (`"receivingMode": "DIRECT"`) e `import.depot.json` (`"defaultReceivingBin": "R1"`) | — |
| Contratos web y móvil | Sin tablas | `web-app/openapi.json`, `web-app/src/kernel/api/schema.d.ts` y `app-almacen/src/kernel/api/schema.d.ts` regenerados | — |
| Frontend web — recibo y almacén | — | `receivingMode.ts` (puro), `useReceivingModeOptions.ts`, `WarehouseDetailScreen.tsx` (sección "Recepción" de Datos), `WarehouseListScreen.tsx` (columna y alta), `ReceiptHeaderModal.tsx`, `ReceiptLinesEditor.tsx` (columna "Posición destino"), `ReceiptDetailPanel.tsx`, `receiptLineEdit.ts` y `useReceiptLineRows.ts`, `PutawayPendingTab.tsx`, `ReceiptMasterList.tsx`, `api.ts` (`useReceiptTargetSuggestions`, `useApplyReceiptTargetSuggestions`), kit `pickers.tsx` (`BinPicker` con `excludeZoneTypeCodes`), `KIT.md` | — |
| App de almacén | Base local versión 3 (`local_receipt.receiving_mode`, `local_receipt_line.target_bin_code`, índice `ix_bin_warehouse_code`; usa la tabla `bin` que ya existía) | `receiveLogic.ts` (`canAddLine(draft, direct)`, `validateTargetBin`, `findTargetConflict`, `buildReceiptBody(…, receivingMode)`), `receive.tsx` (paso "Escanea la posición destino"), `receiveApi.ts` (la pista "Sugerida"), `download.ts` (`downloadBins`), `engine.ts` y `deviceAuth.ts` (el heartbeat en cada pasada con señal), `session.ts` | `GET /api/v1/sync/bins` (ya existía; ahora lo usa la app), `GET /api/v1/warehouse-tasks/putaway-suggestions` |
| Trabajo adicional | Sin tablas | Ver la sección "Trabajo adicional del mismo día": `numberFormat.ts` (web y app), `collectForm.ts` y `pickers.tsx` (Recolección), `receiptExport.ts`, `exportChildren.ts`, `exportGrouped.ts`, `exportTable.ts`, `FilterScope.tsx`, `filterRegistry.ts`, `reportPdf.ts` | `GET /api/v1/receipts?includeLines=true` |
| Humo | — | `scripts/smoke.sh`: bloque "Lote 16" tras el del Lote 15 (almacén `W16` directo sin zona STAGING) | — |
| Pruebas | — | Backend (nuevas): `ReceivingModeRulesTests`, `ReceiptDirectModeTests`, `ReceiptTargetSuggestionTests`, `WarehouseReceivingModeTests`, `ReceiptListLinesTests`; ajustadas `PutawaySuggesterTests`, `ReceiptServiceTests`, `WmsContractsTests`, `WmsControllerSecurityTests` (123 acciones), `WmsCatalogTests`, `AnalyticsSeedFieldsTests`, `DeviceServiceTests`, `LegacyImportReadersTests`, `LegacyImportServiceTests`, `WmsFixture`. Web: `receivingMode.test.ts`, `receiptExport.test.ts`, `exportGrouped.test.ts`, `exportDates.test.ts`, `exportHeading.test.ts`, `filterRegistry.test.ts`, `FilterScope.test.tsx` (nuevas) y ajustadas las de recibo, almacén, selectores, Kárdex, Recolección y kit. App: `numberFormat.test.ts`, `receiveDirectScreen.test.tsx`, `receiveDirectConflictScreen.test.tsx`, `receivePutawayScreen.test.tsx` (nuevas) y ajustadas las de sesión, base local, bajada, motor y recibo | — |

Todo bajo el filtro de tenant existente. La prueba de seguridad de controladores (`WmsControllerSecurityTests`) ya declara las dos acciones
nuevas (de 121 a 123). No hay permisos nuevos: el modo del almacén y la posición por defecto los cambia `warehouse.manage`; el modo del
recibo, el destino por línea y "Usar posiciones sugeridas", `warehouse.receive`; las sugerencias por línea, `inventory.view`.

## Decisiones del dueño del producto (2026-09-30)

Mandan sobre las opciones que había propuesto el arquitecto. Todas fueron la opción **A**.

| # | Tema | Elegido | Cómo quedó |
|---|---|---|---|
| D1 | Modo | **A:** por almacén; en un recibo abierto, quien recibe (`warehouse.receive`) lo cambia solo para ese recibo | El almacén tiene su modo (el de los recibos nuevos). Cada recibo guarda una copia. `PATCH /receipts/{id}` con `receivingMode` solo con el recibo abierto (Esperado, Recibiendo o Discrepancia) |
| D2 | Recibos abiertos al cambiar el modo | **A:** conservan el modo con que se abrieron; los acomodos pendientes siguen | Cambiar el modo del almacén no toca ningún recibo. La pantalla pide confirmación con cuántos recibos abiertos y cuántos con acomodo pendiente hay |
| D3 | Sugerencia | **A:** "Sugerida: …" por línea más el botón "Usar posiciones sugeridas"; nada se llena solo | Prioridad: preferida del producto, mismo lote, mismo producto, picking vacía si el producto rota mucho, reserva vacía, reserva con espacio; se salta la posición donde no cabe. El botón solo asigna donde cabe |
| D4 | Cupo | **A:** se puede exceder con aviso | Chip naranja "Excede el cupo de {bin}: caben {free}". No bloquea ni al guardar ni al confirmar |
| D5 | Posición destino | **A:** cualquiera menos recepción (STAGING) y cruce de muelle (CROSSDOCK); la cuarentena sí | 400 `La posición {code} está en una zona {zoneType}; la posición destino debe ser de guardado.` |
| D6 | Cuarentena | **A:** en devoluciones, la primera sugerida es la cuarentena si existe | Razón `QUARANTINE_RETURN` "Cuarentena (devolución)", solo en recibos de tipo devolución. En los demás recibos la cuarentena nunca se sugiere (aunque sí se puede elegir a mano) |
| D7 | Estatus | **A:** Completado o Completado con diferencia → Acomodado en el mismo instante, con los dos pasos en el historial | Sin estatus nuevos: se usan las entradas `RECEIVED → PUTAWAY` y `RECEIVED_VARIANCE → PUTAWAY` que ya existían |
| D8 | Modo inicial | **A:** Depot (`ALM-DEPOT`) y demo (`ALM-01`) con acomodo; Solutions (`ALM-SOL`) directo | Solutions por la configuración de migración, no por el seed |
| D9 | App vieja | **A:** la app nueva escanea la posición destino por línea; un recibo de la app vieja, sin posiciones, entra "Con acomodo" (con tareas) y no se pierde | Regla del servidor: alta atómica (`confirm: true`), sin `receivingMode` y sin ninguna posición destino en un almacén directo → el recibo nace PUTAWAY |
| D10 | Reparto | **A:** una posición por línea; lo que sobre se transfiere después; en ciegos y devoluciones se pueden poner dos líneas del mismo producto | En recibos con aviso u orden de compra el mismo producto con dos destinos da 400 (H11). Repartir una línea en varias posiciones queda fuera |
| D11 | Cruce de muelle | **A:** la línea con cruce entra a recepción como hoy; el remanente genera una tarea hacia su destino; el recibo queda Completado hasta cerrarla | En directo, una línea con cruce asignado no exige destino. Sin tareas el recibo pasa a Acomodado; con tarea, el estatus sigue el flujo de siempre |
| D12 | Posición de recepción por defecto | **A:** "Posición de recepción por defecto" en Almacenes → Datos; para Depot, `R1` | Columna `DefaultReceivingBinId`. La usan los recibos con acomodo y las líneas con cruce de muelle |
| D13 | Actividad | **A:** un solo evento "Recibo … confirmado" en los recibos directos | Se omite "acomodado" si el recibo directo no tuvo tareas |

## Qué se construyó

### Backend

- **Modo de recepción.** El almacén lleva su modo (`PUTAWAY` o `DIRECT`; sin valor, PUTAWAY) y cada recibo guarda la copia con que se abrió.
  Un modo desconocido da 400 `Modo de recepción desconocido: '{x}'. Use PUTAWAY o DIRECT.`.
- **Recibo directo.** En modo DIRECT el recibo se abre sin posición de recepción (no hace falta una zona STAGING). Cada línea que recibe algo
  lleva su **posición destino**, por id o por código (`targetBinCode`, pensado para la app). Al confirmar, cada línea entra a su destino
  (`RECEIPT` y, si hay diferencia con un documento, `RECEIPT_VARIANCE` en esa misma posición), sin tareas de acomodo, y el recibo pasa de
  Completado (o Completado con diferencia) a **Acomodado** en la misma transacción. Un producto por lote sin lote no mueve inventario y no
  pide destino.
- **Validación del destino.** Del almacén del recibo (un id ajeno da 404), de guardado (STAGING y CROSSDOCK dan 400), activa (422) y con su zona
  activa (422). Se revisa al guardar la línea y otra vez al confirmar, porque la posición pudo darse de baja en medio. Al confirmar, el 400 va
  por línea en `errors["lines[i].targetBinId"]`, junto a los errores de seguimiento (lote y series).
- **Sugerencia de posición con cupo.** El acomodo dirigido ahora respeta `MaxCapacityQty` (unidades): suma la existencia de todos los
  productos de la posición y lo que otras líneas del mismo recibo ya destinan. En devoluciones pone primero la cuarentena.
  `GET …/target-suggestions` devuelve las que caben primero y, al final, las que exceden el cupo con `fits: false`. `POST …/targets/suggest`
  asigna a cada línea sin destino que lo necesita la primera sugerida donde cabe, en orden de línea y acumulando lo ya asignado; devuelve el
  recibo, cuántas asignó y cuántas quedaron sin sugerencia. Si se manda `rowVersion`, debe ser el vigente (409 si cambió).
- **Posición de recepción por defecto del almacén.** `PATCH` del almacén con `defaultReceivingBinId` (del almacén: 404; zona STAGING o
  CROSSDOCK: 400; activa: 422) o `clearDefaultReceivingBin`. Sin ella, o si deja de ser válida, vale la primera posición de una zona STAGING.
- **Cambio de modo de un recibo abierto.** De DIRECT a PUTAWAY exige una posición de recepción (la del encabezado o la del almacén; si no hay,
  422 `El almacén no tiene una posición de recepción (zona STAGING); indíquela.`). En un recibo con acomodo, el destino de una línea, si lo
  tiene, es el destino de su tarea de acomodo.
- **Aparatos, actividad y análisis.** Registro y heartbeat traen el modo del almacén por defecto. Actividad reciente omite "acomodado" en los
  recibos directos sin tareas. Las fuentes `RECEIPT` y `WAREHOUSE` exponen el modo.
- **Migración.** `warehouse.receivingMode` (PUTAWAY o DIRECT) y `warehouse.defaultReceivingBin` en la configuración de `import-legacy`, solo al
  crear el almacén.

### Frontend web

- **Almacenes.** Lista: columna **Recepción** con un chip (Con acomodo o Directo a posición) y campo **Modo de recepción** en el alta (por
  defecto Con acomodo). Ficha → Datos → sección **Recepción**: **Modo de recepción** y **Posición de recepción por defecto** (zonas STAGING o
  CROSSDOCK; vaciarla manda `clearDefaultReceivingBin`). Cambiar el modo abre "¿Cambiar el modo de recepción?" con los conteos de recibos
  abiertos y con acomodo pendiente.
- **Recibo.** El encabezado lleva **Modo de recepción** (en el alta sigue al del almacén hasta que se elige otro; en directo se oculta "Posición
  de recepción"). El detalle de un recibo directo trae el chip **Directo a posición**, la columna **Posición destino** (selector sin zonas de
  recepción ni de cruce, con las sugeridas primero), la pista **Sugerida: {bin} · {motivo}**, el chip naranja **Excede el cupo de {bin}:
  caben {free}**, el botón **Usar posiciones sugeridas** y el bloqueo **Falta la posición destino en {n} línea(s).** bajo "Confirmar recibo".
  La confirmación tiene texto propio ("cada línea entra a su posición destino, sin tareas de acomodo"). La lista de recibos marca "Directo".
  **Lote y series** no pide posición de recepción en un recibo directo.
- **Acomodo pendiente.** Con un almacén directo filtrado muestra un aviso: aquí solo aparecen recibos anteriores al cambio o con cruce de muelle.
- **Kit.** `BinPicker` acepta `excludeZoneTypeCodes`. El contrato está en `web-app/KIT.md`.

### App de almacén

- **Sesión y sincronización.** La sesión guarda `defaultWarehouseReceivingMode`. **El motor de sincronización llama ahora al heartbeat en cada
  pasada con señal** (antes no lo llamaba nadie) y, después, baja las posiciones del almacén por defecto (`GET /sync/bins`, marca de agua por
  almacén; las inactivas se guardan con `is_active = 0`). Con Advance Depot, la primera bajada son unas 3.886 posiciones (8 páginas).
- **Recibir en un almacén directo.** Tras la cantidad (y el lote o las series) viene un paso aparte: **Escanea la posición destino**. Valida sin
  señal contra las posiciones locales: que exista en el almacén, que esté activa y que no sea de recepción ni de cruce de muelle (la
  cuarentena sí). Con señal, muestra la pista **Sugerida: {bin}**; sin señal, no la muestra. La línea queda con "→ {bin}". "Confirmar recibo"
  queda apagado si alguna línea no tiene destino.
- **H11 bloquea en el aparato.** En un recibo con aviso u orden de compra, si el mismo producto quedaría con dos destinos, la app **lo bloquea**
  al escanear y otra vez al confirmar (el servidor lo rechazaría, y un envío rechazado en la cola no se edita).
- **Recibos de antes de actualizar.** Un recibo abierto en la app sin modo se manda sin `receivingMode` y sin destinos: el servidor lo recibe
  "Con acomodo" (D9).
- **Base local versión 3.** Agrega el modo al recibo local y el destino a cada línea.

## Trabajo adicional del mismo día (entra en este commit)

Pedidos del dueño del producto, hechos después del plan. Se documentan aparte porque no salen del recibo directo.

1. **Recolección y empaque: el selector de Posición ofrece solo dónde hay.** Antes ofrecía cualquier posición. Ahora, con un producto elegido,
   lista **solo las posiciones donde ese producto tiene disponible**, con su cantidad (`P-01 · PCK · 9 disp.`), en el orden FEFO del servidor y
   con la primera marcada **Sugerida**. Sin producto, el campo está apagado ("Elija primero un producto"). Se limpia al cambiar el producto o el
   lote. Es el mismo criterio del "Bajar" del ajuste. Por qué: elegir una posición sin existencia del producto solo llevaba a un error de
   "Inventario insuficiente". Kit: `BinPickerInput` con `options` y `optionsLoading`, y `filterBinOptions`.
2. **Formato único de números y dinero.** Causa: el idioma `es` a secas usa punto como separador de miles y **no agrupa las cifras de 4 dígitos**.
   Un "61.023" se leía como decimal (sesenta y uno con veintitrés) y un "1250" no se distinguía de un año. Ahora, en español, la interfaz usa el
   formato de Puerto Rico (`es-PR`: coma de miles, punto decimal: `61,023` y `1,250`) y, en inglés, `en-US`. Todo número pasa por
   `src/kernel/i18n/numberFormat.ts` (`numberLocale`, `formatQuantity`, `formatMoney`).
   - El dinero lleva **`$`** y dos decimales (hasta cuatro en costos unitarios): Saldos (costo y valor), Recolección (costo unitario y total),
     Órdenes (COD y cotizado) y los PDF de reportes (`money` y `unitCost`).
   - Las copias sueltas de formato (Kárdex, reportes, atención, actividad) se unificaron en `formatNumber`. `DataTable` formatea cualquier celda
     numérica cruda y `Panel` formatea el número de su insignia.
   - Los parámetros numéricos de los textos (`t('…', { n })`) también llevan coma, **salvo** los que se llaman `id`, `number`, `code`, `order`,
     `ref`, `serial`, `lot` o `sku`, que se pintan tal cual (un número de documento no debe llevar coma).
   - La app móvil usa el mismo criterio **sin `Intl`** (`formatQuantity` propio, porque el motor de React Native no siempre trae los datos de
     idioma), en los textos y en Acomodar.
3. **Exportar Recibos con sus líneas.** `GET /receipts?includeLines=true` trae las líneas de cada recibo de la página en un solo lote de
   consultas (sin N+1) en `ReceiptListItemDto.Lines`. La exportación de la lista de Recibos y de Acomodo pendiente usa el kit
   `exportChildren`: **Excel y CSV, una fila por línea** repitiendo los datos del recibo (un recibo sin líneas es una fila con las columnas de
   línea vacías); **PDF agrupado**, un bloque por recibo con una banda de encabezado y la tablita de sus líneas ("Sin líneas" si no tiene). La
   columna "Líneas" (el conteo) ya no se exporta: se ven las líneas.
4. **Encabezado y filtros en las exportaciones.** Todo PDF de tabla y todo Excel llevan arriba, en este orden: **la compañía**, el título,
   "Generado el …" y una línea **"Filtros: …"** (o **"Sin filtros"**). Sin línea de filtros en las tablas sin barra de filtros y en las que
   están dentro de un modal. En Excel cada renglón va en su fila, luego una fila en blanco y la tabla con autofiltro (la edición comunitaria de
   SheetJS **no escribe negrita ni congela paneles**). El CSV no cambia. Los controles de filtro del kit se anotan solos (`FilterScope`,
   `useRegisterFilter`); un valor "Código · Nombre" se escribe "Código (Nombre)" en la oración, porque " · " separa los filtros. Por qué: un
   archivo exportado no decía de qué compañía era ni con qué filtros se sacó.
5. **Fechas como fecha en Excel y CSV.** Antes salían como texto. Ahora Excel recibe el número de serie con formato `yyyy-mm-dd` (o
   `yyyy-mm-dd hh:mm` con hora) y el CSV, `2026-09-30` o `2026-09-30 14:03:00`, en hora de Puerto Rico. El Kárdex exporta **Fecha con fecha y hora** y
   ya no exporta la columna Hora; Conteos y Descuadres exportan la fecha real. El PDF no cambia (texto legible).
6. **Rotación configurable FIFO / FEFO: diferida por el dueño.** Ver "Qué queda fuera". Quedó anotada en `docs/lote15-decisiones.md`.
7. **Prueba de login con más tiempo** para la primera carga del Pulso: es lentitud en frío, no un error.

## Cómo se prueba

Corridas del 2026-09-30 sobre el código de este lote (las cifras son las reportadas por quien lo construyó).

1. `dotnet build Teikem.sln && dotnet test Teikem.sln` — **2.727 pruebas en verde** (la bitácora dice que antes eran 2.691; el cierre del Lote 15
   dice 2.687). Pruebas nuevas y ajustadas: ver la fila "Pruebas" del mapa. Cubren, entre otras: alta de un recibo directo sin STAGING,
   `RECEIPT` a la posición destino sin tareas y con historial, orden de compra con ajuste en el destino, lote y serie en su destino, errores 400,
   404 y 422 del destino, alta atómica con `targetBinCode` y compatibilidad con la app vieja, un recibo que conserva su modo, cambio a acomodo sin
   STAGING (422), una línea de documento a una sola posición, una línea con cruce de muelle (entra a recepción y su remanente va con tarea),
   las sugerencias (las que caben primero, lo reservado por otras líneas, devoluciones a cuarentena, "usar sugeridas" acumulando y sin espacio),
   la posición por defecto del almacén y la lista con líneas (sin filtrar datos de otro tenant).
2. Web (`cd web-app && npm run check`): `tsc` limpio, lint sin errores, vitest **100 archivos / 937 pruebas en verde** y build OK.
3. App móvil (`cd app-almacen`): `tsc` limpio y jest **37 suites / 184 pruebas en verde**.
4. Recorridos Playwright (`web-app/e2e`, contra el API real):
Playwright (config local con el Chrome instalado): **63 pasaron, 0 fallaron, 63 omitidos** en la corrida final. Por proyecto:
   escritorio 39 (7 omitidos), movil 8 (49), escritorio-f8a 10 (1), escritorio-lote16 5 (1) y movil-lote16 1 (5). Recorrido nuevo
   `e2e/lote16.spec.ts` (ficha del almacén con Recepción y diálogo; recibo ciego directo bloqueado → usar sugeridas → cuarentena →
   Acomodado sin tareas y fuera de Acomodo pendiente; aviso de cupo que no bloquea; recibo cambiado a Con acomodo con tarea; selector
   de posición de Recolección; móvil 360 px sin scroll). Corre en proyectos propios (`escritorio-lote16`/`movil-lote16`, dependientes
   de los generales) porque deja ALM-01 en directo unos segundos. `expect.timeout` sube a 15 s en `playwright.config.ts`: con varios
   workers la primera carga en frío del Pulso pasaba de 5 s. Intermitencias vistas una sola vez bajo carga y no reproducidas: f6 paso 3
   ("Ya existe un producto con ese SKU" tras Guardar: posible doble envío del alta, a vigilar) y lote14 paso 6 (clic en fila del
   Kárdex sin abrir el detalle).
5. **Base desde cero, repetible:** `db-reset` y luego `import-legacy` de Depot (**0 rechazos, 1.310 asientos** de saldo inicial, 3.886 posiciones)
   y de Solutions (**0 rechazos, 32 asientos**); `db-init` dos veces; seed forzado. Resultado: `ALM-01` PUTAWAY sin posición por defecto;
   `ALM-DEPOT` PUTAWAY con `R1` (zona `STG`, tipo STAGING); `ALM-SOL` DIRECT.
6. **Prueba con `curl` en la demo (backend):** sin destino, `Indique la posición destino de {sku}: el recibo entra directo a posición.` (400 exacto);
   "usar sugeridas" asigna; confirmar deja el recibo en Acomodado sin tareas con el historial Esperado → Recibiendo → Completado → Acomodado;
   el Kárdex muestra `RECEIPT` a la posición final; Actividad trae un solo `RECEIPT_CONFIRMED`. `openapi.json` y los dos `schema.d.ts` se
   regeneraron (tsc limpio).
7. **Humo (`scripts/smoke.sh`), bloque "Lote 16":** se corrió con un arnés y pasa. El almacén `W16` es directo, sin zona STAGING, con las zonas
   RSV, QUA y XD (cruce) y las posiciones `R-01` (cupo 5), `R-02`, `Q-01` y `X-01`. Comprueba: `PATCH` con "HALF" da 400; recibo ciego sin posición
   de recepción da 200 EXPECTED DIRECT; una línea de 8 sin destino y confirmar da 400; `PUT` a `X-01` da 400 por zona; una posición de otro
   almacén da 404; las sugerencias dan `R-01` con `fits=false` y `R-02` como la primera que cabe; `PUT` a `R-01` da `targetFreeQty` 5; confirmar
   deja PUTAWAY sin tareas, con historial, `RECEIPT` +8 a `R-01`, existencia 8 y franja `receivedToday` +8; con `confirm: true` y `targetBinCode`
   `Q-01` da 200 y `"ZZ"` da 400; `PATCH` del encabezado a DIRECT, PUTAWAY y un modo desconocido (400), y a PUTAWAY sin STAGING da 422. En `W6` (con
   STAGING): pasa a DIRECT; un recibo abierto **conserva su modo** cuando `W6` vuelve a PUTAWAY (D2); un recibo de la app anterior (`confirm` sin modo ni
   destinos) en el almacén directo entra con acomodo y su tarea se completa (D9); la posición de recepción por defecto `STG-01` se fija y una de reserva
   da 400; las sugerencias de acomodo saltan `R-01` (8 sobre un cupo de 5) y traen `maxCapacityQty` y `freeQty`. Seguridad: `TREAD6` no puede hacer `PATCH`
   de un almacén ni "Usar posiciones sugeridas" (403) y `TD14` no ve las sugerencias de una línea (403).
**`scripts/smoke.sh` completo: `SMOKE OK`, 129 pasos en verde**, con `SMOKE_SQL` (sqlcmd local), sobre una base recreada desde
   cero igual que el CI: `db-reset --yes`, `import-legacy` de Depot (0 rechazos, 1.310 asientos) y Solutions (0 rechazos, 32) y
   `db-init` dos veces. Incluye el bloque "Lote 16" (almacén directo sin staging, 400/404/422 exactos, sugerencias con cupo, confirmar
   sin tareas con historial y Kárdex a la posición final, `targetBinCode` con `confirm: true`, copia del modo y compatibilidad en W6,
   seguridad). El contrato del API servido coincide con `web-app/openapi.json`.
8. **Verificación en el navegador en la demo, por pieza:**
   - Recibo directo: `ALM-01` a directo; `REC-00013` queda bloqueado por falta de posición destino; "Usar posiciones sugeridas"; una línea a
     `Q-01`; confirmar deja el recibo en Acomodado sin tareas. Aviso de cupo en `W16` (`R-01` con cupo 5). A 360 px, sin scroll horizontal.
     `ALM-01` se devolvió a Con acomodo.
   - Trabajo adicional: cada pieza se revisó en la demo por separado (selector de Recolección, formato de números, exportaciones).
9. Las capturas del manual: las siete `docs/manual/frontend/img/l16-*.png` (`almacen-recepcion`, `almacen-recepcion-dialogo`, `recibo-directo`,
   `recibo-directo-sugeridas`, `recibo-directo-cupo`, `recibo-acomodado` y `acomodo-pendiente-aviso`) las genera el recorrido `lote16.spec.ts` y ya
   están en el manual de pantallas.

**Datos de prueba que quedaron en la demo:** los almacenes `W6H…` y `W16…` con sus recibos. Nada en Depot ni en Solutions fuera del seed y la
migración.

### Qué no se probó

- **La app con un aparato real** (pistola o tablet). La app se probó con `tsc` y jest; no hay una corrida en un equipo con lector. No se
  probó el paso "Escanea la posición destino" con el lector, ni la descarga de unas 3.886 posiciones en un aparato.
- **Un recibo de 200 líneas en directo** y sus consultas de sugerencia. La pantalla pide una consulta por línea (`take` 3, con caché de 30 s);
  con 200 líneas son 200 consultas. No se midió.
- **Recibos reales en Advance Solutions** (primer recibo directo con su única posición `GENERAL`) ni en Advance Depot con `R1`. Solo se
  comprobó cómo quedaron los almacenes al migrar.
- **Dos recibos a la vez contra el mismo cupo.** Cada uno ve la existencia actual y lo reservado por **sus propias** líneas; ambos pueden pasar y
  el cupo se excede. Es el comportamiento aceptado (solo avisa).
- **El Pulso con un almacén directo.** El plan dice que el acomodo del panel "Almacén" baja a 0 y que los indicadores "Tareas pendientes" y
  "Movimientos registrados" bajan; no se verificó en pantalla.
- **Los archivos de Excel abiertos en Excel** (fechas con formato, autofiltro, filas de encabezado): los cubren las pruebas de
  `exportDates.test.ts` y `exportHeading.test.ts`; no hay constancia de haberlos abierto en Excel real.
- **Un PDF de Recibos con muchas páginas** y la **vista en inglés** de los textos nuevos.
- **Una posición dada de baja entre que se elige y se confirma** (se prueba en el servicio: 422 al confirmar; no en pantalla).
- **El CI de GitHub Actions** con este código.

## Decisiones a revisar

Las 1 a 15 son del recibo directo; las 16 a 23, del trabajo adicional.

1. **La posición de recepción por defecto solo se fija con `PATCH` (o con la migración).** Al crear un almacén todavía no hay posiciones, así que
   `POST /warehouses` no la acepta. Reusa los mensajes de la posición de recepción de un recibo (`La posición de recepción debe estar en una zona
   STAGING o CROSSDOCK.`, `La posición de recepción está desactivada.`, 404 `Posición no encontrada.`). Si la posición se da de baja, el sistema
   vuelve a la primera STAGING sin avisar.
2. **Esa posición también gobierna los recibos con acomodo.** No es solo para el modo directo: cambia cuál es la posición de recepción por
   defecto de **todos** los recibos con acomodo del almacén. En Depot, desde este lote es `R1` y ya no `S1` de "Embarque".
3. **El cupo por unidades ahora cuenta también en las tareas de acomodo de siempre** (decisión del arquitecto; la alternativa era solo el modo
   directo). `PutawayRules` salta la posición donde la cantidad no cabe por cupo. Advance Depot tiene cupos **estimados** desde el historial,
   así que sus sugerencias de acomodo pueden cambiar. `GET /warehouse-tasks/putaway-suggestions` trae ahora `maxCapacityQty` y `freeQty`.
4. **Las sugerencias ponen primero las que caben.** Las que exceden el cupo van al final con `fits: false` (para poder mostrarlas con aviso).
5. **"Usar posiciones sugeridas" solo asigna donde cabe.** Una línea que no cabe en ninguna sugerida queda sin destino y se cuenta en "sin
   sugerencia"; el usuario la resuelve a mano (con aviso de cupo, D4).
6. **La consolidación mira las otras líneas del mismo producto en el recibo.** Si dos líneas del mismo producto van al mismo recibo, la segunda
   ve la posición de la primera como "mismo producto".
7. **`targetFreeQty` no descuenta la propia línea.** Es cupo − existencia − lo recibido en **otras** líneas del mismo recibo hacia esa posición;
   solo con el recibo abierto y un destino con cupo. El aviso compara lo recibido de la línea con ese espacio.
8. **Una línea directa recibida en 0 y sin destino.** Se asienta el par `RECEIPT`/ajuste (neto 0) en la posición de recepción del almacén si
   existe; si no, se omite (la diferencia sigue visible en la línea y en la orden de compra).
9. **En directo, una línea con cruce de muelle asignado no exige destino** (D11). Entra a la posición de recepción de la línea; si la línea no trae
   una y el almacén no tiene ninguna, el servidor responde 422 (`El almacén no tiene una posición de recepción (zona STAGING); indíquela.`) al confirmar.
10. **EF sin relaciones para `DefaultReceivingBinId` y `ReceivingModeLookupId`.** Las llaves foráneas viven solo en el SQL (un solo juego de
    scripts). Nada las valida en el modelo de EF.
11. **Web: la rejilla de un recibo directo pasa a tarjetas bajo 760 px de panel** (600 px en un recibo con acomodo), y el chip de cupo puede
    partir renglón.
12. **Web: una consulta de sugerencia por línea** (`take` 3, caché 30 s), solo para las líneas guardadas que reciben algo. Con muchas líneas son
    muchas consultas.
13. **App: el heartbeat corre en cada pasada de sincronización con señal** (cada 60 segundos), no solo al abrir. Si responde `isActive: false`,
    la identidad del aparato se borra (como ya hacía `sendHeartbeat`).
14. **App: H11 bloquea, no avisa.** En un recibo con aviso u orden de compra, dos destinos distintos para el mismo producto se bloquean en el
    aparato, al escanear y al confirmar. Un envío rechazado en la cola no se edita, así que se impide antes. La regla repite en la app el reparto
    del servidor (primero la línea del documento del mismo lote, luego la primera libre); si el servidor cambia su reparto, hay que cambiarla en los dos.
15. **App: la pista "Sugerida: {bin}" sale de `putaway-suggestions` (take 1)**, no de las sugerencias del recibo. No descuenta lo que otras líneas
    del mismo recibo ya ocupan en el aparato, y necesita señal y `inventory.view`. Es solo una pista: nada se llena solo. Los textos nuevos de la
    app van en **tú** ("Escanea la posición destino"), para no mezclar usted y tú dentro de la pantalla.
16. **Formato de números por idioma de la interfaz.** Español = `es-PR` y inglés = `en-US`. No hay una opción por compañía ni por usuario.
17. **Moneda fija en dólares.** `TENANT_CURRENCY = 'USD'`: todas las compañías muestran `$`. Cuando haya compañías con otra moneda habrá que
    leerla de la compañía.
18. **La coma en los textos depende del nombre del parámetro.** Un número pasado a `t()` lleva coma salvo que el parámetro se llame `id`,
    `number`, `code`, `order`, `ref`, `serial`, `lot` o `sku`. Un identificador con otro nombre de parámetro recibiría coma.
19. **Los PDF agrupados (Recibos con líneas) salen en carta horizontal; el resto de las tablas, en A4** (vertical, u horizontal con más de 5
    columnas). El manual de pantallas decía A4 para todos: se corrigió.
20. **"Sin filtros" y la línea de filtros solo donde hay barra.** Una tabla sin controles de filtro, o dentro de un modal, exporta sin esa línea.
    El PDF de los reportes de marca (`reportPdf.ts`) conserva su propio recuadro "Filtros aplicados".
21. **Excel sin negrita ni paneles inmovilizados** (límite de la edición comunitaria de SheetJS). Los encabezados se distinguen por el
    autofiltro. Cambiar a una edición que los escriba es una decisión de licencia.
22. **Fechas de Excel y CSV en hora de Puerto Rico**, fija (`TENANT_TIME_ZONE`), no la del equipo del usuario. Un CSV con hora sale como
    `2026-09-30 14:03:00` (texto que Excel lee como fecha según su configuración regional).
23. **El selector de posiciones de Recolección usa la lista que devuelve el servidor en orden FEFO.** Si la rotación pasa a ser configurable
    (ver "Qué queda fuera"), el orden del selector tendrá que seguirla.

Además: **el modo de Advance Solutions solo se fija al crear el almacén** (la migración no pisa un modo en `--update`). Una base de
Solutions que ya existía antes de este lote queda en PUTAWAY por el seed; se cambia a mano en Almacenes → Datos.

## Qué queda fuera de este lote (a propósito)

- **Rotación configurable FIFO / FEFO** por compañía (y excepción por producto o categoría). El dueño lo dejó para después. La asignación sigue
  fija en FEFO (`PickBatchRules`, `ReplenishmentRules`, el orden del selector de Recolección). Falta decidir dónde se configura, qué métodos se
  ofrecen, qué fecha manda en FIFO sin lote (derivarla del Kárdex o guardar la fecha de la primera entrada por saldo), si hay LIFO y qué método
  tienen por defecto las compañías existentes. Anotado también en `docs/lote15-decisiones.md`.
- **Repartir una línea en varias posiciones** (D10). Una línea entra a una sola posición; lo que sobre se transfiere después. En ciegos y
  devoluciones se pueden poner dos líneas del mismo producto.
- **Negrita y paneles inmovilizados en los encabezados de Excel** (límite de SheetJS comunidad, decisión 21).
- **La posición de recepción por defecto al crear el almacén** (solo por `PATCH` o migración).
- **Reglas de cupo que bloqueen.** El cupo solo avisa (D4).
- **Cambios al Pulso** por el modo directo: no se tocó el panel "Almacén" ni sus indicadores.
- **Cambios a `docs/manual/10-migracion-de-datos.md`** por las dos claves nuevas de la configuración (`receivingMode`, `defaultReceivingBin`):
  quedan documentadas en este documento y en los comentarios de `docs/migracion/import.depot.json` e `import.solutions.json`.
- **Corrida del CI** con este código: se confirma al hacer push.

## Pendiente anotado para un lote futuro (decisión del dueño, 2026-09-30)

- **Método de rotación configurable (FIFO / FEFO)**: ver arriba. No está diseñado.
