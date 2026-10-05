# Rentas (submódulo de Almacén) — plan de ejecución para una sesión en la nube

Documento autosuficiente: quien lo ejecute (una sesión de Claude Code en la nube, sin acceso al dueño mientras trabaja) debe poder construir el
submódulo completo leyendo SOLO este archivo, `CLAUDE.md` y el código. Fecha: 2026-10-05. Dueño del producto: Luis (habla español; todo en español).

## 0. Cómo trabajar (instrucciones del dueño)

1. **Se construye todo** (todos los bloques de la sección 5), **probando y reportando en cada bloque**: al terminar cada bloque, con las pruebas en verde, se
   hace commit y push a `master` (mensaje en español, terminado con la línea `Co-Authored-By` que indique la sesión) y se agrega una entrada al
   **informe de avance** `docs/rentas-informe.md` (qué se hizo, cómo se probó con comandos y conteos reales, qué NO se probó, decisiones tomadas por defecto).
2. **Al final** se entrega el **informe final** (mismo archivo, sección "Informe final"): lo construido, lo probado y no probado, y **todas las decisiones que el
   dueño debe tomar o confirmar** (cada una con la opción que se tomó por defecto y por qué). El dueño NO está disponible durante el trabajo: ante una duda, se toma la
   opción recomendada de este documento (o la más conservadora), se anota en el informe y se sigue. No se pregunta.
3. Se sigue `CLAUDE.md` al pie de la letra (TenantId del JWT, catálogos por FK, estatus solo por `StatusService.TransitionAsync`, soft delete, **un solo set de scripts SQL**
   en `Diseño/logistica-db-estructura.sql` + `Diseño/logistica-db-seed.sql`, sin migraciones EF, permisos en `PermissionCatalog` y `[RequirePermission]`/`[RequireModule]`,
   auditoría con `[AuditEntity]`, excepciones de dominio, identificadores en inglés y comentarios/mensajes/commits en español). Cada bloque de servidor cierra con
   `docs/loteN-decisiones.md` (siguiente número libre: 26 en adelante) y su capítulo del manual + FAQ en `docs/manual/`; cada bloque de web con
   `docs/frontend/loteFN-decisiones.md` y su capítulo en `docs/manual/frontend/` (con capturas si el entorno puede correr Playwright; si no, se dice).
4. **Verificación** (de `CLAUDE.md`): `dotnet build Teikem.sln && dotnet test Teikem.sln`; con SQL Server: `db-init` sobre una base NUEVA y vacía y `scripts/smoke.sh`;
   web: `cd web-app && npm run check`. Si el entorno no puede descargar el SDK o levantar SQL Server, **el árbitro es el CI de GitHub Actions** al hacer push (esperar a que quede
   verde y corregir hasta lograrlo; decirlo en el informe).
5. **Trucos aprendidos en este proyecto:**
   - Tests del servidor: `dotnet test tests/Teikem.Tests -o ".tmp-testout"` (nunca compilar a `bin/`: el API puede estar corriendo desde Visual Studio). `.tmp-testout/` está en `.gitignore`.
   - Regenerar el contrato: levantar el API de `.tmp-testout` en `http://localhost:5180` (`ASPNETCORE_ENVIRONMENT=Development`, `Auth__Onboarding__Enabled=false`), bajar `/swagger/v1/swagger.json`
     a `web-app/openapi.json`, matar el proceso, y correr `npm run api:types` en `web-app/` y en `app-almacen/`.
   - Windows es insensible a mayúsculas: no crear archivos web que difieran solo en mayúsculas de otro (hay una prueba guardiana `fileNames.test.ts`).
   - `db-init` falla sobre una base existente (los `CREATE TABLE` no están guardados): para el esquema NUEVO de este submódulo sí se usan guardas `IF OBJECT_ID(...) IS NULL` para que
     se pueda aplicar sobre la base de Depot ya creada; la base local de desarrollo la recrea el dueño con `scripts\recrear-base.ps1`.
   - Para editar archivos con CRLF desde scripts, normalizar y restaurar los saltos de línea; los archivos del repo son CRLF.
6. No tocar `app-almacen/` salvo regenerar `schema.d.ts` (la app móvil queda fuera de alcance; las series se capturan en la web).

## 1. Qué pidió el dueño

Un **submódulo "Rentas" dentro de Almacén**, atado a envíos y a facturación (que todavía no existen en pantalla). Depot **renta equipos** caros y sensibles, con número de serie:

- Una **orden de renta** despacha equipos a una **localidad** del cliente. Hay un **contrato**. Hay una **fecha de recogido** (fin de la renta) que se puede **extender** "con su debido proceso".
- Al terminar (o a mitad de camino, p. ej. se dañó) el equipo regresa como una **devolución de un tipo particular "renta"**, con motivo, para poder **reportar** luego las devoluciones por renta.
- Al volver, algunos equipos pasan por un **proceso configurable/dinámico** (inspección, limpieza, pruebas, reparación…) antes de estar disponibles de nuevo.
- Costos: habrá **costo de transporte** (el módulo de envíos no existe: **dejar el camino preparado**; la entrega y la recogida saldrán de un envío futuro). La renta se factura **fija o por tiempo**
  (día/semana/mes); el módulo de facturación no existe: **solo guardar condiciones**, sin calcular ni cobrar, y dejar el enlace preparado.

## 2. Decisiones ya tomadas por el dueño (no se reabren)

| # | Decisión |
|---|---|
| D1 | Mientras está rentado, el equipo **sigue siendo nuestro**: no sale del inventario; estado **"En renta"**; su ubicación lógica es la localidad del cliente; **no cuenta como disponible**. |
| D2 | El despacho y la devolución son un **registro propio de la renta** (fechas, equipos, series, costo de transporte estimado) con **enlace opcional (nulo)** al envío futuro. Funciona hoy sin envíos. |
| D3 | Facturación: **solo guardar condiciones** (fija o por tiempo con unidad día/semana/mes, tarifa, moneda por equipo) + enlace nulo a la factura futura. Sin cálculos. |
| D4 | Extender una renta: **registro con permiso y bitácora** (nueva fecha, motivo, tarifa si cambia), historial completo, **sin** aprobación de un segundo usuario. |
| D5 | Series de Depot: se ofrecen **las dos vías** — (a) herramienta del sistema "**Convertir a serie**" (bloque R0) y (b) **reimportar** la migración de Depot con seguimiento SERIAL (bloque RM, ver 5). |
| D6 | Contrato = **número y fecha del contrato dentro de la renta** (no una entidad aparte). |
| D7 | Una serie en renta que aparece en un conteo cíclico: **se bloquea; primero se registra la devolución** (la posición "En renta" no se cuenta). |

Decisiones por defecto tomadas en este documento (el informe final las lista para que el dueño las confirme): una renta = **un cliente, una localidad, varios equipos**; **solo equipos con serie**
(no por cantidad); cancelar = `rental.manage` y **solo antes del despacho**; el estatus **Programada reserva** las series; quién decide si un equipo pasa por proceso = **el receptor al registrar la
devolución, por defecto "sí"**; el motivo de devolución es **por devolución** (no por equipo); tarifa **opcional** para programar; se puede devolver **a otro almacén**; "vencida" es **dato calculado**
(no estatus); una posición `EN-RENTA` **por almacén**; permiso nuevo `rental.return` para devoluciones; el enlace a la factura va **en el encabezado** (para cobro por tiempo habrá varias facturas: irán en `RentalCharge`, fuera de alcance).

## 3. Lo que ya existe (hallazgos de la lectura del código — verificar al empezar, pueden haber cambiado)

1. **Ya hay un esquema de rentas en el SQL que NO encaja**: capa 16C de `Diseño/logistica-db-estructura.sql` (~líneas 2713–2796): `RentalAsset` (equipo fuera del inventario), `RentalContract` (un equipo por contrato, FK a
   `TransportOrder`), `RentalAssetMaintenance`, `RentalBillingRule`, `RentalCharge` (FK diferida a `Invoice`). No hay entidades C#. En el seed hay: módulos `RENTAL_EQUIPMENT` y `RENTAL_BILLING`; permisos
   `rental.view/manage/maintenance/billing` (ya en `PermissionCatalog` y en la plantilla WarehouseOperator); catálogos `RentalBillingFrequency` (WEEKLY/MONTHLY/ONE_TIME), `RentalAssetStatus`,
   `RentalContractStatus`, `RentalChargeStatus`; EntityType `RENTAL_ASSET` y `RENTAL_CONTRACT`.
2. **Series**: `InventorySerial` (`src/Teikem.Domain/Wms/Product.cs`) guarda estatus (`SerialStatus`: AVAILABLE, RESERVED, SHIPPED, SCRAPPED) y ubicación; **solo** lo escribe `src/Teikem.Infrastructure/Wms/InventoryLedger.cs`
   (lo vigila `WmsWriteConfinementTests`). Una salida exige AVAILABLE (o RESERVED con `FromReserved`); una entrada rechaza una serie que ya tiene ubicación (`SerialRules.AlreadyInStock`). **Por eso un recibo RETURN normal
   NO sirve para devolver un equipo rentado**: la serie nunca salió.
3. **"Disponible" = en mano − reservado**, sumado en todas las posiciones (`ProductService`, tabla web, tarjeta "No disponibles"). Las zonas QUARANTINE y CROSSDOCK solo se excluyen al asignar/recolectar. Para que "En renta"
   no cuente como disponible en ninguna pantalla, la unidad tiene que quedar **reservada**.
4. `Contract` es el contrato 3PL de transporte (no sirve para rentas). `Location` (con `ClientId`) es la localidad del cliente.
5. `StatusCode` y `LookupCode` son **globales**: la compañía solo renombra, desactiva, reordena y configura laterales/capacidades; **agregar valores lo hace solo el administrador de plataforma**
   (`LookupService.EnsureCanEditDomain`) → los valores nuevos se siembran en el seed.
6. **D25 bloquea a Depot**: el seguimiento no se puede cambiar después del primer movimiento; los productos de Depot (NONE, con OPENING_BALANCE) dan 409 `No se puede cambiar seguimiento de un producto que ya tiene movimientos.`
7. Las series "en existencia" se definen en `CycleCountRules.IsInStock` y en el KPI "series por capturar" de `ProductService`: hay que sumar los estatus nuevos.
8. Restricciones técnicas: `[RequireModule]` no admite dos módulos en un mismo controlador; un efecto de estatus que use el ledger debe resolverlo con `IServiceProvider` (como `WarehouseTaskStatusEffect`) o se forma el ciclo
   `StatusService ↔ InventoryLedger`; **no existe tabla de envíos**; `Invoice` existe en el SQL (capa 17) pero no está mapeada.
9. Patrón a copiar para un lote: `docs/lote21-decisiones.md`, `docs/lote25-decisiones.md`, y los capítulos de `docs/manual/` (el manual de Almacén es `06-inventario-y-almacen.md`; este submódulo tendrá el suyo: `docs/manual/11-rentas.md`).

## 4. Diseño

### 4.1 Cómo vive "En renta" en el inventario

- **Zona nueva**: ZoneType `RENTAL`. En cada almacén se crea **a demanda** la zona `RENT` con la posición `EN-RENTA` (`RentalBinResolver`).
- **Despacho**: TRANSFER (neutra en el Kárdex) desde la posición de origen hasta `EN-RENTA`, **con reserva en destino**. La serie pasa a `ON_RENT` con referencia `RENTAL`. Sigue en mano (es nuestra); su disponible queda en 0.
- **Devolución**: TRANSFER desde `EN-RENTA` (`FromReserved`, la serie debe estar ON_RENT) a la posición destino. La serie queda `IN_PROCESS` y reservada si requiere proceso; si no, `AVAILABLE`.
- **Fin del proceso**: se libera la reserva (IN_PROCESS → AVAILABLE) con traslado opcional. Una baja = ADJUSTMENT − con motivo DAMAGE (la serie queda SCRAPPED).
- **Cambios al ledger** (todo dentro de `InventoryLedger.cs`): `InventoryPosting` gana, al final y con valor por defecto, `ExpectedSerialStatus`, `TargetSerialStatus` y `ReserveAtDestination`; `StockReservation`/`ReleaseAsync` ganan el estatus esperado y el destino.
- **Excluir la zona RENTAL** en: `StockAllocator.IsExcludedZone`, `PickBatchRules.IsPickableZone`, `ProductService.NonPickableZoneTypes`, `PutawayRules.Rank`, `ReceivingModeRules`.
- **Otros ajustes**: `CycleCountRules.IsInStock` y el KPI de series suman ON_RENT/IN_PROCESS; `KardexRules` e `InventoryReadService.RefNumbersAsync` reconocen las referencias RENTAL, RENTAL_RETURN y RENTAL_PROCESS.

### 4.2 SQL (`Diseño/logistica-db-estructura.sql`) — reescribir la capa 16C, con guardas `IF OBJECT_ID` para aplicarla sobre la base de Depot

| Tabla | Columnas clave |
|---|---|
| `Rental` | PublicId, TenantId, Number `REN-#####` (UQ), ClientId, LocationId, ClientContactId NULL, WarehouseId de origen, StartDate, **PickupDate** (vigente), OriginalPickupDate, ContractNumber NVARCHAR(80) NULL, ContractSignedOn NULL, EstimatedDeliveryCost DECIMAL(18,4) NULL, TransportCurrencyLookupId NULL, **DeliveryShipmentId INT NULL** (sin FK hasta que exista Envíos), **InvoiceId NULL** con FK a Invoice, StatusCodeId, DispatchedAtUtc, ClosedAtUtc, Notes, auditoría, RowVersion. Índice (TenantId, StatusCodeId, PickupDate). |
| `RentalLine` | RentalId, ProductId, SerialId (FK compuesta con ProductId), LotId, FromBinId, DispatchTxnId, DispatchedAtUtc, ReturnedAtUtc, IsActive. `UX_RentalLine_OpenSerial(SerialId) WHERE ReturnedAtUtc IS NULL AND IsActive=1`. |
| `RentalLineRate` (efectivo-fechado, como `RateComponent`) | RentalLineId, BillingFrequencyLookupId, RateAmount ≥ 0, CurrencyLookupId, EffectiveFrom, EffectiveTo NULL, RentalExtensionId NULL. |
| `RentalExtension` (solo inserción) | RentalId, PreviousPickupDate, NewPickupDate (CK: mayor que la anterior), Reason NVARCHAR(300), CreatedAtUtc/By. |
| `RentalReturn` | Number `DRN-#####`, RentalId, ReturnedOn, ReasonLookupId, Notes, EstimatedPickupCost, **PickupShipmentId NULL**. |
| `RentalReturnLine` | RentalLineId (UQ), ConditionLookupId, ToWarehouseId/ToBinId, RequiresProcess, ReturnTxnId, Notes. |
| `RentalProcess` | SerialId, ProductId, WarehouseId, BinId, RentalReturnLineId NULL, StatusCodeId, StartedAtUtc, CompletedAtUtc, Notes. |

- Ampliar `CK_NumberSequence_Kind` con `RENTAL` y `RENTALRETURN`.
- Quitar `RentalAsset`, `RentalContract`, `RentalAssetMaintenance` y `RentalBillingRule`. `RentalCharge` se apunta a `RentalLineId` y queda **sin mapear** (cobro por período futuro).
- **En bases existentes**: borrar las tablas viejas solo si están vacías (si no, dejarlas y avisarlo en el informe).

### 4.3 Seed (`Diseño/logistica-db-seed.sql`, MERGE idempotente)

- Módulo `RENTAL_EQUIPMENT` pasa a llamarse **"Rentas"**, categoría `Almacen`, `DependsOnModuleKey='WMS_LOTSERIAL'`.
- ZoneType `RENTAL`. SerialStatus nuevos: `ON_RENT` "En renta" e `IN_PROCESS` "En proceso" (laterales).
- `RentalStatus`: `DRAFT` (inicial) → `SCHEDULED` (reserva las series) → `ON_RENT` → `RETURNED` (terminal); `CANCELLED` terminal con `StatusLateralEntry` solo desde DRAFT o SCHEDULED.
- `RentalProcessStatus` (configurable: renombrar/desactivar/reordenar): `PENDING` → `INSPECTION` → `CLEANING` → `TESTING` → `READY` (terminal); `REPAIR` y `AWAITING_PARTS` laterales; `SCRAPPED` terminal.
- Lookups: `RentalReturnReason` (END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT, OTHER), `RentalReturnCondition` (GOOD, DAMAGED, INCOMPLETE), `RentalBillingFrequency` (+ `DAILY`; `ONE_TIME` se rotula "Fija").
- EntityType: `RENTAL`, `RENTAL_RETURN`, `RENTAL_PROCESS` (RENTAL_ASSET y RENTAL_CONTRACT se desactivan). AdjustmentReason `TRACKING_CONVERSION` (solo del sistema).
- Permisos nuevos: `rental.extend` y `rental.return` (también en la plantilla WarehouseOperator), espejados en `PermissionCatalog`.

### 4.4 Dominio, EF y contratos

Entidades en `src/Teikem.Domain/Wms/Rental.cs` con `[AuditEntity(RENTAL | RENTAL_RETURN | RENTAL_PROCESS)]`, `ITenantScoped` e `IHasStatus`; reglas puras en `RentalRules.cs`; numeración en `WmsNumbering.cs`; constantes en `CatalogDomains.cs`
(RentalStatuses, RentalProcessStatuses, SerialStatuses.OnRent/InProcess, ZoneTypes.Rental, NumberKinds); `PermissionCatalog.cs` (RentalExtend, RentalReturn y los mapas OwnerRead/OwnerWrite); EF en
`Persistence/Configurations/RentalConfigurations.cs` + DbSets en `TeikemDbContext.cs`; DTOs en `Contracts/RentalContracts.cs`.

### 4.5 Servicios (`src/Teikem.Infrastructure/Services/`, todo dentro de `RunInTransactionAsync`)

- **`RentalService`**: List, Get, Create, Update, AddLine, RemoveLine, SetLineRate, Schedule (`ReserveAsync`), Dispatch, Cancel (libera la reserva), Extend (bitácora + nueva versión de tarifa si cambia).
- **`RentalReturnService.CreateAsync`**: valida que cada serie esté en renta; mueve cada unidad; abre un `RentalProcess` si requiere proceso; cuando vuelven todas, la renta pasa a RETURNED.
- **`RentalProcessService`**: Advance (a cualquier estatus habilitado), Complete (a READY, con posición opcional), Scrap (además exige `inventory.adjust`).
- **Efectos de estatus**: `RentalStatusEffect` (sella DispatchedAtUtc y ClosedAtUtc; impide CANCELLED si ya hay líneas despachadas); `RentalProcessStatusEffect` (al llegar a READY libera la serie; a SCRAPPED hace el ajuste de salida; depende
  de los terminales, no de los pasos, para que los pasos intermedios se configuren libremente).
- **`ProductSerialConversionService`** (bloque R0): por posición, ADJUSTMENT − del saldo sin serie, ADJUSTMENT + por cada serie y cambio de seguimiento, en una sola transacción.

**Mensajes exactos (HTTP)** — usar tal cual:

- Renta: 400 `Indique la localidad del cliente donde estará el equipo.` · 400 `La localidad no pertenece al cliente de la renta.` · 422 `El cliente está dado de baja; solo se consulta su historial.` (reutilizar) · 400 `La fecha de recogido no puede ser anterior a la de inicio.` ·
  400 `Solo se rentan equipos propios; {sku} pertenece a un cliente.` · 400 `El producto {sku} no se controla por serie; solo se rentan equipos con número de serie.` · 409 `La serie {s} ya está en la renta {REN-n}.` ·
  409 `La serie {s} no está disponible en {bin}.` (reutilizar) · 422 `La renta {n} ya fue despachada; no se puede modificar.` · 422 `La renta no tiene equipos; agregue al menos uno.` · 400 `La tarifa no puede ser negativa.` ·
  422 `Solo se cancela una renta en Borrador o Programada; para terminarla registre la devolución.`
- Extensión: 422 `Solo se extiende una renta Programada o En renta.` · 400 `La nueva fecha de recogido debe ser posterior a la actual ({fecha}).` · 400 `Indique el motivo de la extensión.`
- Devolución: 409 `La serie {s} no está en renta en {n}.` · 400 `Con el motivo 'Otro' describa la devolución en las notas.` · 400 `La posición de destino no puede ser de la zona En renta.`
- Proceso: 422 `El proceso ya terminó; solo se consulta.`
- Conteo cíclico: 409 `La serie {s} está en renta ({n}); registre su devolución antes de reconciliar el conteo.` · 422 `La posición {bin} es de equipos en renta; no se cuenta.`
- Conversión a serie: 400 `Capture {n} número(s) de serie para {bin} (hay {m}).` · 409 `El producto {sku} tiene unidades reservadas; libérelas antes de convertirlo.`

### 4.6 Endpoints (`RentalsController`, `RentalReturnsController`, `RentalProcessesController` en `src/Teikem.Api/Controllers/`, todos `[RequireModule(ModuleKeys.RentalEquipment)]`)

| Endpoint | Permiso |
|---|---|
| `GET /rentals` (estatus, cliente, `dueWithinDays`, `overdue`, búsqueda), `GET /rentals/{publicId}`, `GET /rentals/{publicId}/extensions` | `rental.view` |
| `POST /rentals`, `PATCH /rentals/{publicId}`, `POST/DELETE …/lines`, `PUT …/lines/{id}/rate`, `POST …/schedule`, `POST …/dispatch`, `POST …/cancel` | `rental.manage` |
| `POST /rentals/{publicId}/extensions` | `rental.extend` |
| `POST /rentals/{publicId}/returns`; `GET /rental-returns[/{publicId}]` | `rental.return` / `rental.view` |
| `GET /rental-processes`; `POST /rental-processes/{id}/advance`, `…/complete`, `…/scrap` | `rental.view` / `rental.maintenance` (scrap también `inventory.adjust`) |
| `POST /products/{publicId}/convert-to-serial` | `inventory.manage` + `inventory.adjust`, módulo WMS |

El historial de estatus usa el endpoint existente `/status/history/RENTAL/{id}`.

### 4.7 Fuentes de datos, indicadores y reportes

`IDataSource` (registradas en `DependencyInjection.cs`, clave = código EntityType): `RentalDataSource` (DateField StartDate; PickupDate, DaysToPickup, IsOverdue, Units), `RentalReturnDataSource` (DateField ReturnedOn; Reason, Condition,
si fue anticipada), `RentalProcessDataSource`. Aviso en "Necesita tu atención": `RentalDueAttentionProvider` (vencen en ≤ 7 días y vencidas). Contenido de sistema en `SystemAnalyticsSeeder`: "Rentas por vencer (7 días)",
"Rentas vencidas", "Devoluciones de renta por motivo". Reportes mínimos: en renta hoy por cliente, por vencer, vencidas, devoluciones por motivo, equipos en proceso.

### 4.8 Pruebas

Nuevas: `RentalRulesTests` (reglas puras), `RentalLedgerTests` (parámetros nuevos y reserva en destino), `RentalServiceTests` (InMemory; incluye aislamiento por compañía), `RentalProcessEffectTests` (READY y SCRAPPED),
`ProductSerialConversionTests`, y en `CycleCountServiceTests` una serie rentada encontrada en un conteo. Ajustar: `WmsControllerSecurityTests`, `WmsContractsTests`, `OwnedEntityResolverCoverageTests`, `PermissionPropagationTests` y el KPI de series.
`scripts/smoke.sh`: (1) convertir un producto a serie; (2) crear, programar y despachar una renta (el disponible baja, el en mano no); (3) mover o recolectar esa serie da 409; (4) extender (con el 400 de fecha); (5) devolver anticipadamente por daño;
(6) recorrer el proceso hasta READY (el disponible vuelve); (7) 403 sin `rental.extend`; 404 desde otra compañía.

## 5. Bloques de trabajo (en este orden; cada uno cierra con pruebas verdes + commit/push + entrada en `docs/rentas-informe.md`)

| Bloque | Contenido | Depende de |
|---|---|---|
| **R0** | Conversión de productos a serie (`ProductSerialConversionService`, endpoint, mensajes, pruebas, manual/FAQ). Es la base: sin esto no hay series en Depot. | — |
| **R1** | SQL (reescribir capa 16C con guardas), seed, extensión del ledger, entidades/EF/contratos, `RentalService` hasta despacho + extensiones, endpoints, permisos, pruebas, smoke 1–4, manual `11-rentas.md` + FAQ. | R0 |
| **R2** | Devolución de renta y proceso configurable con sus efectos; conteo cíclico (D7); pruebas; smoke 5–7. | R1 |
| **R3** | Fuentes de datos, aviso "Necesita tu atención", contenido de sistema, reportes; manual y FAQ completos. | R1, R2 |
| **RM** | Reimportación de Depot con seguimiento SERIAL (D5-b): evaluar `docs/migracion/import.depot.json` y el importador (`src/Teikem.Infrastructure/Migration/`); `Depot Products.csv` trae columnas `Serial` y `Lot` por producto: usarlas para fijar el seguimiento al CREAR productos nuevos; las series individuales NO están en el CSV (investigar si el MSWM de Depot las tiene; si no, el alta de series va por R0). Documentar qué se puede y qué no; no tocar la base del dueño. | R0 |
| **F-R1** | Web: pantallas de rentas (lista, ficha, alta con equipos por serie, despacho, extensión, historial) y botón "Convertir a serie" en el producto; sigue `web-app/KIT.md` y `CLAUDE.md` (kit, `t('clave')` es/en, `<Can>`/`<ModuleGate>`, responsive 360 px, `npm run check`). | R1 |
| **F-R2** | Web: devoluciones de renta, proceso (cola/avance), reportes e indicadores de rentas. | R2, R3 |

Fuera de alcance: app móvil (series), módulo de envíos, módulo de facturación, cálculo de montos, `RentalCharge` por período.

## 6. Riesgos a vigilar

- D25 y la carga de series de Depot: sin R0 no hay rentas.
- Si una compañía desactiva el estatus `ON_RENT`, el despacho falla con 422 (documentarlo).
- Retirar la capa 16C vieja con cuidado de las bases existentes (solo si están vacías).
- El enlace a factura en el encabezado no alcanza para cobro por tiempo (varias facturas) → `RentalCharge` en el futuro.
- El ledger es el único que escribe series: todo cambio de estatus de serie pasa por él y por `WmsWriteConfinementTests`.

## 7. Informe final (qué debe contener `docs/rentas-informe.md` al terminar)

1. Resumen por bloque: qué se construyó, archivos principales, commits.
2. Cómo se probó: comandos y resultados reales (conteos de pruebas), CI, smoke; y **lo que no se pudo probar** (aparato, Playwright, SQL Server real, etc.).
3. **Decisiones que debe tomar o confirmar el dueño** (una lista numerada): cada una con la opción tomada por defecto, la alternativa y la consecuencia; incluir las de la sección 2 ("por defecto en este documento") y cualquier otra que surja.
4. Pendientes y siguientes pasos (p. ej. envíos, facturación, series en la app, series de Depot).
5. Qué debe hacer el dueño para ver el trabajo: `git pull`, recrear la base local con `scripts\recrear-base.ps1`, y aplicar el esquema nuevo en producción antes de desplegar el API nuevo (las tablas de rentas son nuevas; la capa vieja se retira solo si está vacía).
