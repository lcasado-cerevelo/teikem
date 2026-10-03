# Lote 21 — Conteo cíclico por producto, servidor (2026-10-03)

Parte 5a (servidor) del diseño aprobado en [conteo-por-producto-diseno.md](conteo-por-producto-diseno.md). Solo toca `src/`, `tests/`, el SQL,
el smoke y la documentación; la web y la app construyen sobre este contrato en sus propios lotes (no se tocó `web-app/` ni `app-almacen/`
salvo regenerar el contrato). Manual: [capítulo 06, sección 6 "Lote 21"](manual/06-inventario-y-almacen.md); mensajes y preguntas en
[faq.md](manual/faq.md).

## Lo que NO cambia (mecanismos de integridad)

La unidad del conteo sigue siendo la línea (posición × producto × lote); el ajuste se calcula contra la existencia **actual** al reconciliar
(D22) y se asienta por el ledger; reconciliar sigue siendo todo o nada con `warehouse.count`; quién ve las cantidades esperadas sigue
dependiendo del permiso de quien consulta (conteo a ciegas). Los estatus del conteo no cambian.

## Qué se construyó

1. **Captura original y corrección (evidencia).** `CycleCountLine` guarda `CapturedQty`, `CapturedSerialsJson`, `CapturedBy`, `CapturedAtUtc`,
   `CorrectedBy`, `CorrectedAtUtc`. La regla es una sola función pura (`CycleCountRules.ApplyCapture`) que usan la captura por línea, la captura en
   lote, "agregar lo encontrado" y "Refrescar": primera captura fija lo capturado; la misma persona con el conteo Pendiente recaptura
   (reemplaza); otra persona, o cualquiera con el conteo Contado, corrige (`CountedQty` vigente, lo capturado se conserva, se llena `Corrected*`);
   volver al valor capturado limpia la corrección; reenviar el mismo valor no cambia nada; borrar la captura limpia toda la evidencia. En
   productos con serie se compara el conjunto de series. El motivo (`Notes`) del movimiento asentado al reconciliar lleva la evidencia
   (`CycleCountRules.LedgerNotes`). La captura y la corrección quedan en la bitácora por la auditoría automática de `CycleCountLine`.
2. **Vista previa** `GET /cycle-counts/{id}/reconcile-preview`. `ReconcileAsync` se refactorizó: el cálculo de qué se asentaría vive en
   `BuildPlanAsync` (una sola implementación de la regla) y lo comparten la reconciliación real (con los saldos **bloqueados**), la vista previa y el
   cierre en bloque (con los saldos sin bloquear). Orden de bloqueo y mensajes de la reconciliación real no cambian.
3. **Cierre en bloque** `POST /cycle-counts/reconcile-matching`: evalúa con el mismo plan; cada conteo que cuadra se reconcilia en su **propia
   transacción** y vuelve a comprobar con los saldos bloqueados (`CountNoLongerMatchesException` → `Stale`, la transacción de ese conteo se revierte).
4. **Lista "Por revisar"** `GET /cycle-counts/review`: endpoint propio (no se tocó `CycleCountDto` salvo el campo `correctedLines`); calcula el plan de
   toda la página con consultas por lotes (líneas, saldos por almacén, productos, posiciones, usuarios). Decisión: ver "Decisiones para el dueño".
5. **Posición provisional.** `WarehouseBin.IsProvisional/ProvisionalCreatedBy/ProvisionalCreatedAtUtc/ProvisionalCycleCountId`;
   `POST /cycle-counts/{id}/bins` (reutiliza el alta de posición: `WarehouseLayoutService.CreateBinCoreAsync`); `POST .../confirm-provisional`;
   `isProvisional` en el DTO y el filtro del listado de posiciones, en la búsqueda de posiciones, en `sync/bins` y `binIsProvisional` en la línea del conteo.
6. **Contrato para la app.** `CycleCountLineDto` ya traía `binId`, `binCode`, `lotId`/`lotNumber`, `trackingTypeCode` y `barcode`; `POST /cycle-counts`
   con `productPublicIds` + almacén devuelve el conteo con todas sus líneas; origen nuevo `PRODUCT` en el catálogo `CycleCountOrigin`.
7. **Esquema y seed.** `Diseño/logistica-db-estructura.sql` (columnas, `CK_CycleCountLine_Evidence`, `CK_WarehouseBin_Provisional`,
   `IX_WarehouseBin_Provisional`, `FK_WarehouseBin_ProvisionalCount` en un lote posterior a `CycleCount`) y `Diseño/logistica-db-seed.sql`
   (`('CycleCountOrigin','PRODUCT',…)` por el MERGE idempotente). `WarehouseBin` lleva sus columnas nuevas también como `ALTER` guardados; la
   tabla `CycleCountLine` se crea sin guarda (como antes), así que las bases existentes se recrean con `db-reset`.

## Referencia de contrato para la web y la app

Todo bajo el módulo `WMS_LOTSERIAL`. JSON en camelCase; las fechas son UTC ISO-8601. Errores: ProblemDetails (`title` = el mensaje exacto; `errors`
por campo cuando aplica). Una posición se identifica por su `id` entero; el almacén, por `publicId`.

### Cambios en DTOs existentes (campos nuevos, todos opcionales al final)

| DTO | Campos nuevos |
|---|---|
| `CycleCountLineDto` | `capturedQty`, `capturedByName`, `capturedByUserId`, `capturedAtUtc`, `correctedByName`, `correctedByUserId`, `correctedAtUtc`, `wasCorrected`, `binIsProvisional` |
| `CycleCountDto` | `correctedLines` (líneas corregidas); `originCode` ahora puede ser `PRODUCT` |
| `WarehouseBinDto` | `isProvisional`, `provisionalCycleCountId`, `provisionalCreatedAtUtc` |
| `BinSearchItemDto` | `isProvisional` |
| `SyncBinDto` | `isProvisional` |
| `WarehouseBinQuery` / `GET /warehouses/{publicId}/bins` | filtro `isProvisional` (true solo pendientes, false solo confirmadas) |

`countedQty` es **siempre el valor vigente** (el que se reconcilia). Con conteo a ciegas la evidencia **sí** llega; `systemQty`, `varianceQty`,
`currentQty`, `reconciledSystemQty` y `adjustedQty` siguen en `null`.

### `POST /api/v1/cycle-counts` — conteo por producto (`warehouse.count.capture`)

Cuerpo: `{ "warehousePublicId": guid?, "productPublicIds": [guid], "binIds"?, "zoneIds"?, "categoryIds"? }`. Con `productPublicIds` y **sin** `binIds`
ni `zoneIds` el conteo es por producto (`count.originCode = "PRODUCT"`); la respuesta es la ficha completa (`CycleCountDetailDto`, a ciegas si el
usuario no tiene `warehouse.count`) con **una línea por cada posición activa y lote con existencia mayor que cero** del producto en ese almacén
(`lines[]`: `id`, `binId`, `binCode`, `zoneCode`, `lotId`, `lotNumber`, `trackingTypeCode` = `NONE | LOT | SERIAL`, `barcode`, …). Errores: 400
`Los filtros no seleccionan inventario en mano para contar; amplíe los filtros o agregue líneas a mano.` (el producto no tiene existencia en ningún
lado), 400 `El conteo admite como máximo 1000 líneas; acote los filtros.`, 400 `Indique el almacén: la compañía tiene más de uno.` (sin
`warehousePublicId` y con varios almacenes), 404 `Producto no encontrado.`, 422 `El almacén está inactivo.`. Los productos con serie **sí** se crean
(una línea por posición con series esperadas); la app decide avisar y no crear (`trackingTypeCode` viene en el producto).

### Captura y corrección — `PUT /cycle-counts/{id}/lines` y `PUT /cycle-counts/{id}/lines/batch` (`warehouse.count.capture`)

Sin cambios de forma. `batch` acepta renglones por `lineId` o, para lo hallado donde el sistema no tenía nada, `{ binId, productPublicId, lotId | lot:{number,
manufactureDate?, expiryDate?}, countedQty }` (el lote es obligatorio si `trackingTypeCode = LOT`; error 400 `El producto {sku} se controla por lote;
indique el lote.`). Todo o nada. La respuesta es la ficha del conteo con la evidencia de cada línea. Reglas de evidencia: ver arriba. Un renglón con
cantidad vacía **borra** la captura (la app debe mandar `0` para un espacio en blanco). Idempotente ante el mismo valor.

### `POST /api/v1/cycle-counts/{id}/bins` — posición provisional (`warehouse.count.capture`)

Cuerpo: `{ "zoneId": int, "code"?: string, "aisle"?, "rack"?, "level"?, "position"? }` (o `code`, o las partes que componen `A01-R02-N3-P04`).
Respuesta: `WarehouseBinDto` con `id`, `code`, `zoneId`, `zoneCode`, `isProvisional = true`, `provisionalCycleCountId`, `provisionalCreatedAtUtc`. Se
usa de inmediato como `binId` de una línea nueva (`lines/batch`). La zona se elige con `GET /warehouses/{publicId}/zones` (`inventory.view`). Errores:
400 `Indique la zona de la posición.` / `Indique el código de la posición o su pasillo/rack/nivel/posición.` / `El código de la posición solo admite
letras, números, guion y guion bajo (máximo 40).`; 404 `Conteo no encontrado.` (también de otra compañía) / `Zona no encontrada.` (de otro almacén);
409 `Ya existe una posición con ese código en el almacén.`; 422 `La zona está inactiva; reactívela primero.` / `El conteo ya fue reconciliado; no admite
posiciones nuevas.` / `El almacén está inactivo.`.

### `POST /api/v1/warehouses/{publicId}/bins/{binId}/confirm-provisional` (`warehouse.manage`)

Sin cuerpo. Quita la marca; responde el `WarehouseBinDto` (con `isProvisional = false`; `provisionalCycleCountId` y `provisionalCreatedAtUtc` se conservan).
409 `La posición no está pendiente de revisión.`; 404 si la posición no es del almacén o de la compañía.

### `GET /api/v1/cycle-counts/{id}/reconcile-preview` (`warehouse.count`)

Respuesta `ReconcilePreviewDto`:

```json
{
  "count": { "...CycleCountDto..." },
  "lines": [{
    "lineId": 12, "binId": 7, "binCode": "A01", "zoneCode": "PCK", "binIsProvisional": false,
    "productPublicId": "…", "sku": "PN", "productName": "…", "trackingTypeCode": "NONE", "lotId": null, "lotNumber": null,
    "systemQty": 5, "currentQty": 4, "reservedQty": 0, "countedQty": 5, "isPending": false,
    "capturedQty": 3, "capturedByName": "Ana", "capturedAtUtc": "…", "correctedByName": "Beto", "correctedAtUtc": "…", "wasCorrected": true,
    "adjustmentQty": 1, "resultingQty": 5, "movements": 1, "systemQtyChanged": true, "error": null,
    "serials": null
  }],
  "totals": { "lines": 1, "pendingLines": 0, "linesWithDifference": 1, "movements": 1, "errorLines": 0, "matches": false, "resultStatusCode": "RECONCILED_VARIANCE" },
  "blockingError": null, "rowVersion": "…"
}
```

- `currentQty`/`reservedQty` = existencia **hoy** (la base del ajuste); `systemQty` = la foto. `adjustmentQty` = movimiento neto con signo que se asentaría
  en la posición (`countedQty − currentQty`; en serie, altas + traslados − bajas); `resultingQty = currentQty + adjustmentQty`.
- `serials` (solo `SERIAL`): `{ removals: [..], additions: [..], transfers: [..] }`; `movements` = una por serie.
- `error`: el mismo texto con que la confirmación respondería 409 por esa línea (`El conteo de {sku} en {posición} ({contado}) es menor que lo reservado
  ({reservado}); libere la reserva antes de reconciliar.`). `blockingError`: error del conteo entero (serie en dos líneas; la confirmación daría 400).
- `isPending = true` (con `countedQty = null`): línea sin contar; es un dato, no un error.
- `matches`: hay líneas, ninguna pendiente, ninguna con error y ningún movimiento. `resultStatusCode`: `null` mientras no se pueda confirmar.
- Errores: 422 `El conteo ya fue reconciliado; solo se consulta.`; 404 `Conteo no encontrado.`; 403 sin `warehouse.count`.
- Mismo cálculo que `POST /cycle-counts/{id}/reconcile`; no escribe ni bloquea.

### `GET /api/v1/cycle-counts/review` (`warehouse.count`)

Query: `warehousePublicId`, `countedByUserId`, `search`, `includeOpen` (false), `skip` (0), `take` (50, 1–200). Respuesta `{ total, skip, take, items[] }` más
recientes primero; cada item:
`{ count: CycleCountDto, countedByUserId, countedByName, countedByCount, firstProductPublicId, firstProductSku, firstProductName, otherProducts,
positions, lines, pendingLines, differingLines, errorLines, movements, correctedLines, matches }`. Por defecto solo conteos **Contado**; con
`includeOpen=true` también los Pendientes con todas sus líneas capturadas. `matches` y `differingLines` se miden contra la existencia **actual**. Sin
filtro de `matches` en el servidor (la web filtra la página; ver pendientes).

### `POST /api/v1/cycle-counts/reconcile-matching` (`warehouse.count`)

Cuerpo (todo opcional): `{ "warehousePublicId": guid, "ids": [int], "comment": string, "includeOpen": false }`. Respuesta:

```json
{ "examined": 4,
  "closed":  [{ "id": 31, "number": "CC-00031", "statusCode": "RECONCILED", "lines": 2 }],
  "skipped": [{ "id": 32, "number": "CC-00032", "reasonCode": "WouldPost", "reason": "Asentaría 1 movimiento(s); revíselo.", "count": 1 }],
  "truncated": false }
```

`reasonCode`: `WouldPost` (count = movimientos), `Errors` (líneas con error), `Pending` (líneas sin contar), `Stale`, `NotCounted`, `AlreadyReconciled`,
`NotFound`, `NoLines`, `Failed`. Sin `ids`: los Contados del almacén (o de toda la compañía) hasta 200 (`truncated`). Con más de 200 `ids`: 400 `Se
revisan como máximo 200 conteos por vez; acote por almacén o por ids.` (`errors.ids`). 404 `Almacén no encontrado.` si `warehousePublicId` no es de la
compañía. Un conteo ajeno nunca aparece (con `ids` sale como `NotFound`). El comentario queda en el historial de cada conteo cerrado.

### Receta para la app ("Contar por producto")

1. Escanear/teclear el producto → resolver `productPublicId` (`GET /products/by-barcode/...`); si `trackingTypeCode = SERIAL`, avisar y no crear.
2. `POST /cycle-counts { warehousePublicId, productPublicIds:[id] }` → lista posición/lote (`lines`). Todo viaja por la cola de salida con `Idempotency-Key`.
3. Confirmar: `PUT /cycle-counts/{id}/lines/batch` con **todas** las líneas (un espacio en blanco se manda como `countedQty: 0`) y las líneas de "Otra
   posición"; después `POST /cycle-counts/{id}/finish`.
4. "Otra posición": `GET /warehouses/{publicId}/zones` → `POST /cycle-counts/{id}/bins { zoneId, code | aisle/rack/level/position }` → usar el `id`
   devuelto como `binId` de la línea nueva (con `lotId`/`lot.number` si el producto lleva lote).
5. Offline: `sync/bins` trae `isProvisional`; una posición provisional ya sincronizada se usa como cualquier otra.

### Receta para la web (revisión rápida)

1. `GET /cycle-counts/review?warehousePublicId=…` → tabla "Por revisar" (quién contó, producto(s), posiciones, `differingLines`, `matches`).
2. "Cerrar los que cuadran": `POST /cycle-counts/reconcile-matching { warehousePublicId }` → mostrar `closed` y `skipped` con su `reason`.
3. Abrir un conteo: `GET /cycle-counts/{id}` (evidencia por línea) y `GET /cycle-counts/{id}/reconcile-preview` (existencia actual, ajuste, saldo
   resultante, errores). Por defecto mostrar solo las líneas con `adjustmentQty != 0` o `error != null`.
4. Corregir: `PUT /cycle-counts/{id}/lines { lines:[{ lineId, countedQty }] }` (es una corrección si el usuario no es quien capturó o el conteo está Contado).
5. Confirmar: `POST /cycle-counts/{id}/reconcile`. Posiciones por revisar: `GET /warehouses/{publicId}/bins?isProvisional=true` y `confirm-provisional`.

## Cómo se probó

Ver la sección "Verificación" al final de este documento (resultados reales de cada comando).

Pruebas nuevas (xunit): `CycleCountEvidenceRulesTests` (reglas puras de captura/corrección y del motivo del ledger) y `CycleCountByProductTests`
(alta por producto y origen; recaptura del mismo usuario; corrección de otro usuario; corrección tras Contado; vuelta al valor original; evidencia en el
DTO y en las notas del Kárdex; el conteo a ciegas conserva la evidencia sin filtrar lo esperado; **paridad vista previa ↔ reconciliación real** con
reservado, línea movida desde la foto, lote y series; error de reservado idéntico; pendientes como dato; cierre en bloque —cierra solo los que
cuadran contra la existencia actual, omite con motivo, por ids, `includeOpen`, por almacén, aislado por compañía y tope de 200—; "Por revisar"
—quién contó, primer producto y otros, posiciones, diferencias, `matches`, filtros y paginación—; posición provisional —crear, código compuesto,
409 duplicado, zona de otro almacén 404, otra compañía 404, conteo cerrado 422, usarla como línea nueva, listado/filtro y confirmar una sola vez—).
Se actualizaron `WmsContractsTests` (firmas posicionales), `WmsControllerSecurityTests` (mapa de permisos de las 5 acciones nuevas) y las fixtures.

## Decisiones para el dueño

1. **`matches` exige que no haya líneas pendientes.** La definición pedida era "ninguna línea asienta algo y no hay errores"; un conteo con
   líneas sin contar no se puede confirmar, así que `matches` es `false` (y las pendientes salen como dato aparte). Se usa la misma definición en la vista
   previa, "Por revisar" y el cierre en bloque.
2. **Por revisar y cierre en bloque miran solo los conteos Contados** (los que el operario terminó). Los Pendientes con todas las líneas capturadas
   entran solo con `includeOpen=true`: así no se cierra un conteo que el operario todavía puede estar corrigiendo. D8 permite reconciliar desde
   Pendiente, pero el valor más seguro por defecto es el Contado.
3. **Recapturar con el conteo Pendiente reemplaza la captura**, también si otra persona ya la había corregido: la corrección se borra de la línea (la
   bitácora de cambios conserva el rastro). Borrar la captura (cantidad vacía) borra también la evidencia. Si prefiere que una recaptura nunca pise una
   corrección, es un cambio de una línea en `CycleCountRules.ApplyCapture`.
4. **Una corrección usa el mismo permiso que capturar** (`warehouse.count.capture`): no hay un permiso aparte para corregir. Quien puede capturar
   puede corregir lo de otro; queda la evidencia y el motivo del movimiento. Si se quiere restringir (p. ej. solo `warehouse.count`), se decide en la
   web o con un permiso nuevo.
5. **La evidencia no se oculta a quien cuenta a ciegas** (no revela lo que dice el sistema); solo se oculta lo esperado.
6. **La posición provisional se puede crear con el conteo Pendiente o Contado** (no Confirmado), igual que "agregar lo encontrado", y exige que
   la zona sea del almacén del conteo. Al confirmarla se conservan quién, cuándo y el conteo que la creó. Una provisional con existencia no se puede
   desactivar (regla de siempre). El operario con solo `warehouse.count.capture` puede crear posiciones nuevas (antes solo `warehouse.manage`):
   la marca provisional y la bitácora dejan el rastro.
7. **Origen `PRODUCT`** solo cuando el conteo se crea con productos y sin posiciones ni zonas (con categorías sí sigue siendo por producto).
8. **Tope de 200 conteos por llamada** del cierre en bloque y de la página de "Por revisar" (como el máximo de la lista); `truncated` avisa.
9. **Orden de "Por revisar"**: más recientes primero (como las demás listas). Si el dueño prefiere "el más antiguo primero" para la revisión, es un cambio de orden.
10. **Notas del Kárdex**: el motivo de una línea corregida se corta a 300 caracteres (límite de la columna).

## Pendientes y límites

- **`Stale` del cierre en bloque no tiene prueba automática**: InMemory no puede intercalar un movimiento entre la evaluación y el cierre, y el smoke no
  puede forzar esa carrera. La lógica es la misma comprobación (`requireMatching`) que ya se ejercita en el camino feliz; queda para una prueba de concurrencia.
- **Filtro `matches` en el servidor**: "Por revisar" calcula `matches` por página; la web no puede paginar solo los que cuadran. Si hace falta, se agrega un
  parámetro `onlyMatching` que calcule el plan antes de paginar (más caro).
- **Productos con serie y búsqueda por lote o serie**: fuera de alcance (decisión del dueño); el servidor ya admite series por línea, sin cambios.
- **Bases existentes**: `CycleCountLine` se crea sin guarda; se recrean con `db-reset`. El camino `ALTER` de `WarehouseBin` no se ejerció sobre una base
  anterior (solo `db-reset` y `db-init` dos veces).
- Web y app: lotes propios sobre esta referencia de contrato.

## Verificación (resultados reales, 2026-10-03)

| Comando | Resultado |
|---|---|
| `dotnet build Teikem.sln -c Release` | Build succeeded, 0 errores |
| `dotnet test Teikem.sln -c Release` | Passed: 2973, Failed: 0 (incluye las 2 pruebas nuevas del lote y las existentes) |
| `scripts/dev-sqlserver.sh` | SQL Server 2022 ya estaba corriendo en el entorno |
| `dotnet run --project src/Teikem.Api -c Release --no-build -- db-reset --yes` y `db-init` ×2 | Terminaron bien ("Inicialización de BD completada"); comprobadas en la base `IsProvisional`, `CorrectedAtUtc`, el origen `PRODUCT` y las restricciones `CK_CycleCountLine_Evidence`, `CK_WarehouseBin_Provisional` y `FK_WarehouseBin_ProvisionalCount` |
| API en Development (`Auth__Onboarding__Enabled=false`, `ASPNETCORE_URLS=http://localhost:5000`) + `scripts/smoke.sh` con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` | **SMOKE OK** sobre una base recién recreada, incluido el bloque nuevo "conteo por producto (Lote 21)". La primera corrida falló en ese bloque por un error de orden del propio smoke (el movimiento de la existencia se hizo antes de la primera pasada de cierre); se corrigió el smoke, no el servidor |
| `npm run api:types` en `web-app` y `app-almacen`, `tsc -b` en ambos | Sin errores (`web-app/openapi.json` salió del Swagger del API en marcha; `schema.d.ts` generado, nunca a mano) |
| `npm run lint` y `npm run test` en `web-app` | lint con avisos previos (0 errores); 111 archivos de prueba, 1103 pruebas, todas pasan |

No se corrió Playwright (no hay cambios de pantalla en este lote).

## Adenda 2026-10-03 (parte 5d) — conteo por producto vacío (`allowEmpty`)

Cierra el hueco entre servidor y app: "Otra posición" necesita un conteo abierto, pero un producto sin existencia respondía 400 y no abría nada.

**Contrato.** `CycleCountCreateRequest` gana, **al final**, `AllowEmpty` (bool, por defecto `false`; la firma posicional queda como estaba y la prueba
`WmsContractsTests.Positional_signature_is_fixed` la fija con el campo nuevo al final).
- `AllowEmpty = true` + **exactamente un producto** + sin `BinIds`/`ZoneIds`/`CategoryIds`: si el producto existe y no tiene existencia, el conteo se crea
  **vacío** (sin líneas, origen `PRODUCT`, con su tarea COUNT) en vez del 400 `NothingSelected`. Si el producto tiene existencia, el resultado es el de siempre.
- Producto inexistente (o de otra compañía) → 404 `Producto no encontrado.`, como hoy.
- `AllowEmpty = true` con otros filtros, más de un producto o ninguno → 400 en el campo `allowEmpty`: `Crear un conteo vacío (allowEmpty) solo aplica a un único producto, sin posiciones, zonas ni categorías.`
  (`CycleCountRules.AllowEmptyOnlyOneProduct`; se valida antes de tocar la base).
- `AllowEmpty = false`: sin cambios (incluido el 400 `NothingSelected` y el tope de 1000 líneas, que `allowEmpty` no salta).

**Ciclo de un conteo vacío (revisado, sin más cambios de código).** Acepta líneas nuevas por `PUT .../lines/batch` y `POST .../lines` (también en una posición
provisional); **terminar y reconciliar siguen fallando mientras no tenga líneas** (`El conteo no tiene líneas.`, 422); en "Por revisar" con `includeOpen` no
aparece hasta tener todas sus líneas capturadas (la web lo ve como "Faltan líneas"); se borra con `DELETE` mientras esté abierto (la tarea COUNT se cancela); el
cierre en bloque lo omite (`NotCounted` mientras esté abierto; `NoLines` si se incluye lo abierto). `ChangedCount` y los demás orígenes no se tocan. La compañía ajena obtiene 404 en todo.

**Pruebas.** xunit en `CycleCountByProductTests` (sin `allowEmpty` sigue 400; vacío con origen/estatus/tarea; con existencia no cambia; 404 y los cinco 400 de
filtros incompatibles; línea nueva en posición provisional, terminar y reconciliar; vacío no termina ni reconcilia; `AddLineAsync`; borrar; aislamiento por compañía)
y un bloque 13 en `scripts/smoke.sh`. `web-app/openapi.json` y los dos `schema.d.ts` se regeneraron (solo agregan `allowEmpty`).

**Decisiones para el dueño.** (1) `allowEmpty` es un indicador del request, no un permiso aparte: lo abre quien ya puede crear un conteo (`warehouse.count.capture`).
(2) Se exige un solo producto para que "vacío" siempre tenga un propósito claro (registrar lo hallado de ese producto) y no se puedan crear conteos vacíos en masa.
(3) `web-app/` no se tocó salvo regenerar el contrato.

**Verificación de la adenda (resultados reales, 2026-10-03).**

| Comando | Resultado |
|---|---|
| `dotnet build Teikem.sln -c Release` | Build succeeded, 0 errores |
| `dotnet test Teikem.sln -c Release` | Passed: 2979, Failed: 0 |
| `db-reset --yes` y `db-init` ×2 (el esquema SQL no cambió) | Terminaron bien |
| API en Development (`Auth__Onboarding__Enabled=false`) + `scripts/smoke.sh` con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` | **SMOKE OK** sobre una base recién recreada, con el bloque 13 (allowEmpty). Una primera pasada falló en un `DELETE` del bloque nuevo hecho con un usuario sin `warehouse.count` (corregido) y una segunda, sobre la misma base ya usada, en un paso viejo que no se repite ("Ya hay un feriado en esa fecha."); se recreó la base |
| `npm run api:types` en `web-app` y `app-almacen`; `npx tsc -b` y `npm test` en `web-app` | Sin errores; 1131 pruebas pasan |
| `npm run check` en `app-almacen` (api:types, tsc, oxlint, jest) | Sin errores ni avisos; 53 suites y 273 pruebas, todas pasan |

