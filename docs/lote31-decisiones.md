# Lote 31 — Despacho manual (DMA-#####), servidor (2026-10-11)

Pendiente P0.4 del plan (`docs/plan-de-desarrollo-pendiente.md`; origen: `docs/frontend/loteF8a-decisiones.md` §3). Luis rechazó usar
«Ajustar» como salida de almacén sin documento y pidió una operación propia. Este lote construye **solo el servidor** (API, SQL, pruebas,
humo, contrato y manual); la web y la app lo usan en sus propias fases.

## Decisiones del dueño que se aplicaron

- Nombre **«Despacho manual»**, número **`DMA-#####`**, documento propio con numeración y ficha consultable (además del Kárdex).
- Permiso nuevo **`warehouse.issue`**.
- **Motivo obligatorio** de un catálogo editable + **nota libre**.
- **No es a ciegas**: la existencia se sigue mostrando (corrige lo que decía F8a §3).
- No pide cliente ni consignatario; sirve para inventario propio y de un cliente (**un solo dueño por documento**, como la recolección).
- La **misma operación** para la app y la web (`POST /api/v1/manual-issues`).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Modelo | `PickBatch.ManualIssueReasonId` (FK a `LookupCode` entidad `ManualIssueReason`; **no nulo = despacho manual**) y `PickBatch.Note` (≤ 500). No hay tabla nueva: es la misma recolección | `Domain/Wms/PickBatch.cs`, `Persistence/Configurations/WmsDocumentConfigurations.cs` |
| Numeración | Contador propio `MANUALISSUE` por compañía (ClientId NULL), patrón `DMA-#####` (`WmsNumbering`, `NumberingRules.IsKnownKind`); **no** consume `PACKBATCH`/`EMP` | `Domain/Constants/CatalogDomains.cs` (`NumberKinds.ManualIssue`), `Domain/Wms/WmsNumbering.cs`, `Domain/Orders/NumberingRules.cs` |
| Reglas puras | Motivo obligatorio y normalizado, nota ≤ 500, `CanPack` falso para un manual, nota del Kárdex `DMA-… · {motivo}` (≤ 300), filtro `kind` (MANUAL/PACK/ALL), búsqueda por motivo y nota, mensajes | `Domain/Wms/PickBatchRules.cs`, `Domain/Wms/KardexRules.cs` (referencia «Despacho manual DMA-…») |
| Servicio | `ManualIssueAsync` valida motivo (activo y no deshabilitado por el override de la compañía) y nota **antes** de tocar inventario y sale por el **mismo `CollectCoreAsync`** (FEFO, series, un solo dueño, `ISSUE`, 409 sin efecto parcial) con su contador; `GetManualIssueAsync`/`DeleteManualIssueAsync` (404 propio); `PackAsync` → 422 para un manual; `DeleteAsync` exige `warehouse.issue` para un manual (no `orders.cancel`); lista con `kind` y DTO con `isManual`, `reasonCode`, `reasonLabel`, `note`, `ownerClientName` | `Infrastructure/Services/PickBatchService.cs`, `Contracts/PickBatchContracts.cs` |
| API | `ManualIssuesController` (`/api/v1/manual-issues`: GET lista, GET `reasons`, GET ficha, POST, DELETE), módulo WMS_LOTSERIAL como `/pick-batches`; `GET /pick-batches?kind=` | `Api/Controllers/ManualIssuesController.cs`, `PickBatchesController.cs` |
| Kárdex | Detalle del documento de un manual: etiqueta «Despacho manual», referencia = motivo · nota | `Infrastructure/Services/InventoryReadService.cs` |
| Análisis | Fuente `PICK_BATCH` con `IsManual`, `ReasonCode`, `Reason`, `Note`; `PackBatchNumber` nulo para un manual | `Infrastructure/Analytics/PickBatchDataSource.cs` |
| Bajas | Un manual **no** cuenta como documento abierto para dar de baja producto ni almacén | `Services/ProductOpenDocuments.cs`, `Services/WarehouseService.cs` |
| Permiso | `warehouse.issue` en `PermissionCatalog` (72 códigos) y en las plantillas WarehouseOperator y TenantAdmin | `Domain/Constants/PermissionCatalog.cs` |
| SQL | Sección **2026-10-11** de `Diseño/logistica-db-update.sql`: `CK_NumberSequence_Kind` + `MANUALISSUE`; columnas con `COL_LENGTH`; FK e índice filtrado con `OBJECT_ID`/`sys.indexes`; dominio y 5 motivos con `MERGE`; permiso con `IF NOT EXISTS`; `RolePermission` a WarehouseOperator y TenantAdmin **de la plantilla y de los roles ya clonados** (solo agrega) | `Diseño/logistica-db-update.sql` |
| Contrato | `web-app/openapi.json` regenerado desde Swagger (solo agregados: 3 rutas, `ManualIssueCreateRequest`, `kind` y los campos nuevos de `PickBatchDto`) y `web-app/src/kernel/api/schema.d.ts` con `npm run api:types`. El `schema.d.ts` de la app **no** se tocó (otra fase) | `web-app/` |

## Contrato (para la web y la app)

`POST /api/v1/manual-issues` (`warehouse.issue`, respeta `Idempotency-Key`):

```json
{
  "warehousePublicId": "9c8ff3d1-ea5c-430e-a7b0-2a7319a1c092",
  "lines": [{ "productPublicId": "3b4e80e7-deb7-47e7-9a39-1c7bc0ab492b", "quantity": 2, "binId": 1002 }],
  "reasonCode": "SAMPLE",
  "note": "Feria de salud"
}
```

Respuesta 200 (`PickBatchDto`, recortada):

```json
{
  "id": 1002, "publicId": "6504adeb-d5d9-4946-911a-cd1a8502151c", "number": "DMA-00001",
  "warehouseCode": "WD1", "statusCode": "COLLECTED", "status": "Recolectada", "collectedBy": "Administrador Advance",
  "canPack": false, "canDelete": true, "totalQty": 2.0, "totalCost": 4.0,
  "lines": [{ "sku": "DMA1", "quantity": 2.0, "binId": 1002, "binCode": "P-01", "unitCost": 2.0, "issueTxnId": 10003 }],
  "isActive": true, "rowVersion": "AAAAAAAAJx0=",
  "isManual": true, "reasonCode": "SAMPLE", "reasonLabel": "Muestra", "note": "Feria de salud", "ownerClientName": null
}
```

Mensajes y códigos: capítulo 06 §7b del manual (tabla de validaciones) y `docs/manual/faq.md` («Despacho manual»).

## Cómo se probó

- `dotnet build Teikem.sln` sin errores (las advertencias son las de antes) y `dotnet test Teikem.sln`: **3333 pruebas, 0 fallas** (antes
  3308; +25): `ManualIssueTests` (reglas puras —motivo, nota ≤ 500, número DMA con contador propio, no empacable, nota del Kárdex,
  `kind`, búsqueda—; permiso en el catálogo y las plantillas; espejo SQL con propagación a roles clonados; servicio sobre EF InMemory:
  salida con número, motivo y nota del Kárdex, contador independiente del EMP, 400 sin efecto, 409 sin efecto parcial y sin consumir el
  número, dos dueños, inventario de cliente con su dueño, empacar 422 antes de pedir `orders.create`, eliminar con reversa y
  `warehouse.issue` y 404 al repetir, rutas manuales que no alcanzan una EMP, lista por `kind`), `WmsDeactivationGuardTests` (un manual
  no bloquea la baja de producto ni almacén) y las de catálogo/permisos/firmas actualizadas (71 → 72 permisos, 155 → 160 endpoints de
  WMS, firmas de `PickBatchQuery`/`PickBatchDto`/`ManualIssueCreateRequest`, campos de la fuente `PICK_BATCH`).
- SQL Server 2022 local, base de desarrollo `Teikem` (con las compañías ya existentes): `db-init` aplicó `logistica-db-update.sql`
  (14 lotes); `warehouse.issue` quedó en WarehouseOperator y TenantAdmin de la plantilla **y** de las dos compañías clonadas (el
  `PermissionSeeder` informó 0 propagados: lo hizo el SQL); columnas, FK, `CK_NumberSequence_Kind` con `MANUALISSUE`, dominio y 5 motivos
  verificados con `sqlcmd`; el script corrido otra vez directo con `sqlcmd -b` no cambió nada (idempotente). Prueba real contra el API:
  motivos, 400 sin motivo y con motivo inexistente, 409 `insufficient_stock` (luego el primer éxito fue `DMA-00001`: el número no se
  consumió), POST 200, Kárdex `ISSUE` −2 con nota `DMA-00001 · Muestra` y referencia `Despacho manual DMA-00001`, empacar 422, `kind`
  MANUAL y 400 con otro valor, DELETE 204 con el saldo de vuelta en 5; `NumberSequence` `MANUALISSUE` en 2 y `PACKBATCH` intacto.
- `scripts/smoke.sh` completo sobre una base **nueva** local de desarrollo (`TeikemDMA2`, con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN`):
  **SMOKE OK**. Paso nuevo «despacho manual (2026-10-11)»: 5 motivos (reasons y catálogo), el Operador de almacén clonado trae
  `warehouse.issue` y Solo lectura recibe 403; sin motivo, motivo inexistente y nota de 501 → 400; sin existencia 409 sin efecto (Kárdex
  y saldo iguales); `Idempotency-Key` repite el mismo documento (`Idempotent-Replayed`); Kárdex `DMA-… · Muestra` / «Despacho manual
  DMA-…»; empacar 422; `kind` MANUAL/PACK/otro valor y búsqueda por nota; ficha (una EMP por `/manual-issues` → 404); eliminar con reversa
  (Solo lectura 403). Comprobado aparte: el 403 de la política de `/manual-issues` llega **sin cuerpo** (como el resto de las políticas).

## Decisiones a revisar

1. **Nota del Kárdex** `DMA-00012 · {motivo}` exacta, sin la nota libre (la nota queda en la ficha y en el detalle del documento del
   Kárdex). Si se quiere también la nota en cada movimiento, es una línea en `PickBatchRules.ManualIssueMovementNote` (tope 300).
2. **Estatus**: el despacho manual usa el `PickBatchStatus` de la recolección: queda en **Recolectada** (`COLLECTED`) y al eliminarlo
   **Cancelada**. La web/app pueden mostrar «Despachado» cuando `isManual`; si se quiere un estatus propio hace falta un dominio nuevo.
3. **Bajas**: un despacho manual no bloquea dar de baja el producto ni el almacén (si bloqueara, lo haría para siempre); después de la
   baja ya no se puede eliminar (el Kárdex responde 422).
4. **Motivos**: la compañía los renombra o deshabilita (override, `admin.catalogs`); agregar motivos nuevos lo hace el administrador de
   plataforma (dominio global, igual que «Destino final de lo dañado»).
5. **Lista de recolecciones**: `kind` por omisión `ALL`, así que la pantalla actual de Recolección y empaque ya muestra los DMA (con
   `canPack` falso). Si la web prefiere ocultarlos, que mande `kind=PACK`.
6. **Actividad reciente**: la creación de un DMA se ve como el evento de recolección (`PICK_COLLECTED`) con su número `DMA-…`; no se agregó
   un evento propio.
7. `GET /api/v1/manual-issues/reasons` exige `warehouse.issue` (quien despacha); el catálogo genérico sigue disponible.

## Adenda 2026-10-11 (b) — Motivo por default del despacho manual (servidor)

Decisión del dueño: el despacho manual no debe complicar el aparato → **motivo por default por compañía, preseleccionado** en la app y la
web; el servidor **sigue exigiendo** motivo; la nota sigue opcional y vacía por omisión.

| Pieza | Qué hace | Dónde |
|---|---|---|
| Modelo | `Tenant.DefaultManualIssueReasonLookupId` (INT NULL, FK a `LookupCode`); auditado por el interceptor (`Tenant` es `[AuditEntity]`) | `Domain/Tenancy/Tenant.cs`, `Persistence/Configurations/TenancyConfigurations.cs` |
| Reglas puras | `NormalizeDefaultReason` (null = sin cambio, vacío = quitar, código en mayúsculas) e `IsDefaultReason` (solo si sigue activo y habilitado) | `Domain/Wms/PickBatchRules.cs` |
| Validación | `ManualIssueReasonLookup`: activo, visible para la compañía y no deshabilitado por su override (la misma regla del POST); se lee de la base, no de la caché global | `Infrastructure/Services/ManualIssueReasonLookup.cs` |
| Ajustes | `PUT /api/v1/tenant/settings` acepta `defaultManualIssueReason` (`admin.tenant`); 400 `El motivo {CÓDIGO} no existe o está inactivo.` en `errors.defaultManualIssueReason` sin guardar nada; reenviar el código ya guardado no se revalida. `GET` lo devuelve (código efectivo o `null`) | `Services/TenantService.cs`, `Contracts/TenantContracts.cs` |
| Motivos | `GET /api/v1/manual-issues/reasons` devuelve `ManualIssueReasonDto` = los campos de `LookupValueDto` **más** `isDefault` (sigue siendo un arreglo: compatible) | `Api/Controllers/ManualIssuesController.cs`, `Services/PickBatchService.cs` (`WithDefaultReasonAsync`), `Contracts/PickBatchContracts.cs` |
| SQL | Sección **2026-10-11 (b)** de `Diseño/logistica-db-update.sql`: columna con `COL_LENGTH` y `FK_Tenant_DefaultManualIssueReason` con `OBJECT_ID`; sin datos (nadie nace con default) | `Diseño/logistica-db-update.sql` |
| Contrato | `web-app/openapi.json` (solo agregados: `ManualIssueReasonDto`, `defaultManualIssueReason` en `TenantSettingsDto` y `TenantSettingsUpdateRequest`) y `schema.d.ts` de la web **y** de la app (`npm run api:types` en las dos); `tsc` de las dos sin errores | `web-app/`, `app-almacen/src/kernel/api/schema.d.ts` |

Contrato:

```json
// PUT /api/v1/tenant/settings  (null o ausente = sin cambio; "" = quitar)
{ "defaultManualIssueReason": "SALE" }
// → 200 TenantSettingsDto: { …, "defaultManualIssueReason": "SALE" }

// GET /api/v1/manual-issues/reasons → 200
[
  { "id": 1446, "entity": "ManualIssueReason", "code": "SAMPLE", "label": "Muestra", "labels": {"es":"Muestra","en":"Sample"}, "description": "",
    "sortOrder": 1, "isSystem": true, "isEnabled": true, "isOverridden": false, "isActive": true, "isDefault": false },
  { "id": 1449, "entity": "ManualIssueReason", "code": "SALE", "label": "Venta", "labels": {"es":"Venta","en":"Sale"}, "description": "",
    "sortOrder": 4, "isSystem": true, "isEnabled": true, "isOverridden": false, "isActive": true, "isDefault": true }
]
```

Cómo se probó: `dotnet build` sin errores y `dotnet test`: **3344 pruebas, 0 fallas** (antes 3333; +11 en
`ManualIssueDefaultReasonTests`: reglas, servicio sobre InMemory —guardar, quitar, null sin cambio, 400 sin guardar nada (inexistente, de
otro dominio, inactivo, deshabilitado), default deshabilitado después se ignora y vuelve al rehabilitarlo, override de otra compañía no
afecta, el POST sigue exigiendo motivo—, permisos de los endpoints, auditoría, contrato compatible y espejo SQL). Base de desarrollo
`Teikem`: `db-init` aplicó la sección (16 lotes), columna y FK verificadas con `sqlcmd` y el script corrido otra vez directo
(`sqlcmd -I -b`) sin cambios; prueba real: 400 con `regalo`, guardar `sale` → `SALE`, `isDefault` solo en Venta, deshabilitar Venta →
ninguno marcado y ajustes en `null`, reenviar `SALE` → 200, quitar el override → vuelve, POST sin motivo → 400, `""` → `null`; la bitácora
dejó `{"DefaultManualIssueReasonLookupId":{"from":null,"to":1449}}` y su vuelta. `scripts/smoke.sh` completo sobre una base **nueva** de
desarrollo (`TeikemDMC`, con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN`): **SMOKE OK**, con las líneas nuevas en el paso «despacho manual».

Decisiones a revisar:

8. **Ajustes devuelven el default efectivo**: si el motivo guardado ya no se puede usar, `GET /tenant/settings` devuelve `null` (lo que la
   pantalla verá preseleccionado) aunque la columna conserve el valor; al rehabilitarlo vuelve solo. Si se prefiere mostrar «guardado pero
   deshabilitado», hace falta un campo más.
9. **Faltan la app y la web** (otra fase): la app guarda los motivos con `mapReason` y hoy descarta `isDefault`; debe guardarlo y
   preseleccionar ese motivo en «Completar despacho»; la web debe preseleccionarlo en el panel de Recolección y agregar el selector en
   Ajustes de la compañía.
