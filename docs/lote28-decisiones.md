# Lote 28 — Rentas R2: devolución de renta, proceso del equipo devuelto y conteo cíclico (2026-10-05)

Tercer bloque del submódulo **Rentas** (`docs/rentas-plan-de-ejecucion.md`, bloque R2). Sobre el esquema que R1 ya creó y mapeó
(`RentalReturn`, `RentalReturnLine`, `RentalProcess`), construye la **devolución de renta** (D2: registro propio de la renta con enlace
nulo al envío futuro de recogido), el **proceso configurable** del equipo devuelto con sus efectos en el inventario y la regla del
**conteo cíclico** para los equipos en renta (D7). Sin cambios de esquema, de seed ni de permisos.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Devolución | `RentalReturnService.CreateAsync`: solo una renta En renta (422); motivo por devolución (`OTHER` exige notas, 400); equipos por **número de serie**, cada uno despachado y sin devolver de ESA renta (409 `La serie {s} no está en renta en {n}.`); condición por equipo (GOOD por defecto); destino por equipo, por encabezado o, sin indicar, la posición de donde salió (puede ser otro almacén; nunca la zona En renta, 400); por equipo una **TRANSFER** desde EN-RENTA (`FromReserved`, serie esperada `ON_RENT`) con referencia `RENTAL_RETURN`: con proceso (por defecto) `ReserveAtDestination` y serie `IN_PROCESS`, sin proceso serie `AVAILABLE`; un `RentalProcess` por equipo con proceso, en el estatus inicial de `RentalProcessStatus`; cuando vuelve el último equipo, la renta pasa a **RETURNED** por `StatusService.TransitionAsync` (el efecto de R1 sella el cierre). Número `DRN-#####`. Lista (renta, cliente, motivo, fechas, anticipadas, búsqueda) y ficha | `Services/RentalReturnService.cs` |
| Proceso | `RentalProcessService`: cola paginada (abiertos primero), **Advance** a cualquier estatus habilitado con las reglas de `StatusService` (a `SCRAPPED` exige además `inventory.adjust`), **Complete** a READY con traslado opcional dentro del mismo almacén (TRANSFER con la reserva, serie sigue `IN_PROCESS`, referencia `RENTAL_PROCESS`), **Scrap** a SCRAPPED (exige `inventory.adjust` con `PermissionService.EnsureAsync`). Proceso terminado → 422 `El proceso ya terminó; solo se consulta.` | `Services/RentalProcessService.cs` |
| Efecto | `RentalProcessStatusEffect`: READY libera la reserva (`ReleaseAsync` IN_PROCESS → AVAILABLE); SCRAPPED hace un `ADJUSTMENT −1` desde la posición del proceso con `FromReserved`, motivo `DAMAGE`, referencia `RENTAL_PROCESS` y serie `SCRAPPED`; los dos sellan `CompletedAtUtc`. Depende de los terminales, no de los pasos. Resuelve el ledger con `IServiceProvider` (sin el ciclo `StatusService ↔ InventoryLedger`) | `Services/RentalProcessStatusEffect.cs` |
| Bloqueo | Sentencia 19 de `InventoryQueries`: `LockRentalProcessAsync` (UPDLOCK con `TenantId =`). Orden del proceso: RentalProcess (U) → saldos → series. Orden de la devolución: Rental (U) → NumberSequence (`RENTALRETURN`) → saldos → series | `Wms/InventoryQueries.cs` |
| Conteo cíclico (D7) | La zona RENTAL no se cuenta: pedida de forma explícita (posición o zona al crear, zona de "lo cambiado", línea agregada a mano) → 422 `La posición {bin} es de equipos en renta; no se cuenta.`; las selecciones amplias y `product-bins` la saltan. Una serie `ON_RENT` capturada en otra posición deja la línea con el error 409 `La serie {s} está en renta ({n}); registre su devolución antes de reconciliar el conteo.` (vista previa, reconciliar y cierre en bloque) | `Services/CycleCountService.cs` |
| Kárdex | Detalle del movimiento para `RENTAL_RETURN` (número, fecha, cliente, número de la renta) y `RENTAL_PROCESS` ("Proceso #id", estatus y la devolución como documento padre) | `Services/InventoryReadService.cs` |
| Reglas y mensajes | Mensajes del plan tal cual (devolución, proceso, conteo) y los nuevos del lote; normalización de motivo y condición, fecha, anticipada, estatus de la serie al volver, notas de los movimientos | `Domain/Wms/RentalRules.cs`, `RentalReturnReasons.All` y `RentalReturnConditions.All` en `CatalogDomains.cs` |
| API | `RentalReturnsController` (`POST /api/v1/rentals/{publicId}/returns` `rental.return`; `GET /api/v1/rental-returns[/{publicId}]` `rental.view`) y `RentalProcessesController` (`GET /api/v1/rental-processes` `rental.view`; `POST …/{id}/advance|complete|scrap` `rental.maintenance`), los dos con `[RequireModule(RENTAL_EQUIPMENT)]` y un solo `[RequirePermission]` por acción | `src/Teikem.Api/Controllers/` |
| Contratos | `RentalReturnLineInput`, `RentalReturnCreateRequest`, `RentalReturnQuery`, `RentalReturn*Dto`, `RentalProcessQuery`, `RentalProcessDto`, `RentalProcessPageDto`, `RentalProcessAdvanceRequest`, `RentalProcessCompleteRequest` (dar de baja reutiliza `RentalStatusRequest`) | `Contracts/RentalContracts.cs`; `web-app/openapi.json` y `schema.d.ts` (web y app) regenerados: solo adiciones, 7 rutas y 10 esquemas |
| DI | `RentalReturnService`, `RentalProcessService` y el efecto del proceso | `DependencyInjection.cs` |

Ciclo de la serie de un equipo rentado: AVAILABLE → RESERVED (programar) → ON_RENT (despachar, EN-RENTA reservada) → IN_PROCESS (devolver
con proceso, reservada en el destino) → AVAILABLE (READY) o SCRAPPED (baja); sin proceso, ON_RENT → AVAILABLE.

## Cómo se probó

- `dotnet test tests/Teikem.Tests -o .tmp-testout`: **3133 pruebas, 0 fallas** (antes 3104 en `9457d00`; +29). Nuevas:
  `RentalReturnServiceTests` (4: devolución completa con y sin proceso —EN-RENTA vacía, en mano/disponible, series, Kárdex con la
  referencia y la nota, renta Devuelta con historial, proceso Pendiente—; **devolución anticipada por daño** parcial y luego el resto;
  validaciones con el texto exacto sin escribir nada; **aislamiento por compañía** de devoluciones y procesos), `RentalProcessEffectTests`
  (3: **READY** con pasos y lateral, traslado, el disponible vuelve, 422 al terminar y la serie se vuelve a rentar; el efecto depende del
  terminal con un paso desactivado y READY por "avanzar", destinos inválidos; **SCRAPPED** con 403 sin `inventory.adjust`, ADJUSTMENT −
  DAMAGE, serie dada de baja), en `CycleCountServiceTests` **una serie rentada en un conteo** (D7: 422 por posición, zona y línea,
  EN-RENTA saltada, `product-bins`, vista previa y 409 sin escribir, reconcilia al quitarla). Ajustadas: `WmsControllerSecurityTests`
  (**151 acciones**), `WmsContractsTests` (12 firmas), `OwnedEntityResolverCoverageTests` (servicios y efecto desde el contenedor),
  `PermissionPropagationTests` (R2 sin permisos nuevos; la plantilla del Operador los trae), `RawSqlConfinementTests` (19 sentencias),
  `WmsFixture` (estatus del proceso, motivos y condiciones).
- SQL Server 2022 local: `db-init` **dos veces** sobre la base **nueva** `TeikemR2Smoke` (la segunda: scripts omitidos por hash, 68
  permisos, 0 nuevos, tenant demo "ya existe"). Se borraron las bases de simulación viejas `TeikemR0Smoke` y `TeikemR1Smoke`.
- `scripts/smoke.sh http://localhost:5180` (API de `.tmp-testout`, `Auth__Onboarding__Enabled=false`, `SMOKE_SQL` y `SMOKE_MIGRATION_RUN`
  con una envoltura del build de `.tmp-testout`): **SMOKE OK, 136 pasos, 138 `ok`**, a la primera. Paso nuevo "devolución y proceso de
  rentas (Lote 28, Rentas R2)": D7 (EN-RENTA 422; serie en renta en un conteo: vista previa y 409, sin mover nada); 403 sin
  `rental.return`, sin `rental.extend` (smoke 7), sin `rental.maintenance` y dar de baja sin `inventory.adjust`; 400 'Otro' sin notas y
  destino EN-RENTA; 409 serie fuera de la renta; **smoke 5** devolución anticipada por daño (DRN, TRANSFER desde EN-RENTA, serie En proceso
  reservada, disponible sin ella, renta sigue En renta, lista y ficha, AuditLog); **smoke 6** proceso Pendiente → Inspección → Limpieza →
  Reparación → Pruebas → Lista con traslado (el disponible vuelve, Kárdex "Proceso #id", historial), 422 al terminar; la segunda devolución
  cierra la renta (Devuelta); dar de baja con SQL Server real (ADJUSTMENT − DAMAGE, serie dada de baja); **otra compañía con Rentas
  encendido: 404** en renta, devolución, devolver, avanzar y terminar, listas vacías; sin descuadre Kárdex ↔ saldo. Verificado por SQL: 3
  devoluciones, 3 equipos devueltos, 2 procesos (READY y SCRAPPED con fecha de fin).
- Web `npm run check`: tipos generados, tsc, oxlint, **vitest 1252** (124 archivos) y build. App `npm run check`: tipos, typecheck,
  lint, **jest 410** (72 suites).

## Decisiones a revisar

1. **Equipos por número de serie** en la devolución (no por id de línea): es lo que se escanea y lo que dice el mensaje del plan.
2. **Devolución parcial** permitida; la renta pasa a Devuelta con la devolución que trae el **último** equipo. No hay estatus
   "parcialmente devuelta" (la ficha muestra qué equipos volvieron).
3. **Destino por defecto** = la posición de donde salió el equipo; se puede indicar uno para todos o por equipo, en cualquier almacén de la
   compañía (plan: "se puede devolver a otro almacén"), nunca la zona En renta. Cuarentena sí.
4. **Proceso por equipo y "sí" por defecto** (plan §2); **motivo por devolución** y **condición por equipo** (GOOD por defecto).
5. **Fecha de devolución**: hoy por defecto, no futura ni anterior al inicio de la renta. **Anticipada** = antes de la fecha de recogido
   vigente (dato calculado, `isEarly`).
6. **Devolver no exige el cliente activo** (un cliente dado de baja igual devuelve su equipo).
7. **Avanzar respeta el motor de estatus** (siguiente paso habilitado, laterales Reparación/Esperando piezas desde cualquier paso,
   terminales); no permite saltar entre pasos del pipeline fuera de orden. Avanzar a READY = terminar sin traslado; a SCRAPPED pide
   también `inventory.adjust`.
8. **Terminar con traslado solo dentro del almacén del proceso** (400 si es de otro almacén); a otro almacén, con una transferencia normal
   después.
9. **Dar de baja** = un ajuste de salida con el motivo existente **DAMAGE** (no un motivo nuevo) y la referencia `RENTAL_PROCESS`; el
   segundo permiso (`inventory.adjust`) lo exige el servicio (un solo `[RequirePermission]` por acción, como R0).
10. **D7 en el conteo**: EN-RENTA pedida de forma explícita → 422; los conteos amplios la saltan sin error. La serie en renta capturada en
    otra posición es un **error de línea** (409 al reconciliar, visible en la vista previa; el cierre en bloque no cierra ese conteo). Las
    series En proceso se cuentan como las reservadas (sin regla nueva).
11. **Orden de bloqueo de la devolución**: Rental → NumberSequence → saldos → series (el número va antes del ledger porque el movimiento
    lleva el id de la devolución). Sin ciclo: el contador `RENTALRETURN` solo lo toma una devolución que ya bloqueó su renta.
12. **Mensajes nuevos** no previstos en el plan (todos en el capítulo 11 y la FAQ): renta no En renta, motivo y condición, equipos y serie,
    fechas, costo de recogido, notas del equipo, destino de otro almacén, estatus vacío, devolución simultánea.
13. **Smoke 7**: para que "otra compañía" dé **404** (y no 403 por módulo apagado) el smoke enciende Inventario y Rentas en la compañía de
    prueba.

## No probado

- Pantallas (bloque F-R2); sin Playwright.
- Concurrencia real en SQL Server (dos devoluciones del mismo equipo, dos usuarios avanzando el mismo proceso): cubierta por diseño
  (bloqueo de la renta y del proceso, `UQ_RentalReturnLine_Line`, RowVersion del proceso), el smoke va en serie.
- Una compañía que desactive `READY`, `SCRAPPED`, `RETURNED` o el estatus inicial del proceso (documentado: 422 del motor de estatus).
- Conteo de "lo cambiado" con la zona RENT pedida (cubierto por el mismo código que el alta; sin prueba propia).
- Datos reales de Depot.
