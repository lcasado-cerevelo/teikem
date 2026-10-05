# Lote 27 — Rentas R1: renta hasta el despacho, extensiones y cancelación (2026-10-05)

Segundo bloque del submódulo **Rentas** (`docs/rentas-plan-de-ejecucion.md`, bloque R1). Construye el esquema completo de rentas (capa 16C
reescrita), los catálogos y permisos, la extensión del ledger para que una serie pueda estar "En renta" sin salir del inventario (D1), y
la renta desde el alta hasta el despacho, con extensiones (D4) y cancelación. La devolución y el proceso configurable son del bloque R2.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Esquema (capa 16C reescrita) | `Rental`, `RentalLine` (serie con FK compuesta e índice único `UX_RentalLine_OpenSerial`), `RentalExtension` (solo inserción, nueva fecha > anterior), `RentalLineRate` (efectivo-fechada), `RentalReturn`, `RentalReturnLine`, `RentalProcess` y `RentalCharge` por equipo (sin mapear). Cada tabla con guarda `IF OBJECT_ID … IS NULL`; el bloque es autosuficiente para aplicarse sobre una base existente: retira la capa vieja **solo si está vacía** (si no, `PRINT` de aviso y la deja), recrea `CK_NumberSequence_Kind` con `RENTAL` y `RENTALRETURN` y agrega las FKs a `Invoice` si ya existe (en una base nueva se agregan en la capa 17, guardadas) | `Diseño/logistica-db-estructura.sql` |
| Seed | Módulo `RENTAL_EQUIPMENT` → **"Rentas"**, categoría Almacén, depende de `WMS_LOTSERIAL`; dominios `RentalStatus`, `RentalProcessStatus`, `RentalReturnReason`, `RentalReturnCondition`; ZoneType `RENTAL`; SerialStatus `ON_RENT` e `IN_PROCESS` (laterales); estatus de renta y de proceso; frecuencia `DAILY` y `ONE_TIME` rotulada "Fija"; motivos y condiciones de devolución; EntityType `RENTAL`, `RENTAL_RETURN`, `RENTAL_PROCESS` (`RENTAL_ASSET` y `RENTAL_CONTRACT` desactivados); regla lateral 3I (CANCELLED solo desde DRAFT y SCHEDULED); permisos `rental.extend` y `rental.return` (plantilla Operador de almacén) y bloque 5b3 que los da UNA vez a los roles de compañía con `rental.manage` | `Diseño/logistica-db-seed.sql` |
| Ledger (plan 4.1) | `InventoryPosting` gana al final `ExpectedSerialStatus`, `TargetSerialStatus` y `ReserveAtDestination`; `StockReservation` gana `ExpectedSerialStatus` y `TargetSerialStatus` (Reserve/Release). Sin ellos, el comportamiento no cambia | `Wms/WmsSeams.cs`, `Wms/InventoryLedger.cs` |
| Zona RENTAL excluida | No se asigna (`StockAllocator`), no se recolecta (`PickBatchRules`), no cuenta para "solo con disponible" (`ProductService`), no se acomoda (`PutawayRules`) ni es destino del recibo directo (`ReceivingModeRules`) | `Domain/Wms/*`, `ProductService.cs` |
| En inventario | `SerialStatuses.InStock` = AVAILABLE, RESERVED, ON_RENT, IN_PROCESS: `CycleCountRules.IsInStock` y el KPI "series por capturar" | `CatalogDomains.cs`, `CycleCountRules.cs`, `ProductService.cs` |
| Kárdex | Referencias `RENTAL` ("Renta REN-#####"), `RENTAL_RETURN` ("Devolución de renta DRN-#####") y `RENTAL_PROCESS` ("Proceso #id"); detalle del movimiento con la renta | `KardexRules.cs`, `InventoryReadService.cs` (`RefNumbersAsync`, `DocumentAsync`) |
| Dominio | Entidades con `[AuditEntity]` (RENTAL; RENTAL_RETURN; RENTAL_PROCESS), reglas puras y mensajes | `Domain/Wms/Rental.cs`, `Domain/Wms/RentalRules.cs`, numeración REN/DRN en `WmsNumbering.cs` y `NumberingRules.cs` |
| EF | Mapeo 1:1 de las 7 tablas (sin `RentalCharge`), DbSets, bloqueo `LockRentalAsync` (sentencia 18 de `InventoryQueries`, con `TenantId =`) | `Persistence/Configurations/RentalConfigurations.cs`, `TeikemDbContext.cs`, `Wms/InventoryQueries.cs` |
| Servicios | `RentalService` (lista con "por vencer"/"vencidas", ficha, alta, edición, equipos, tarifa, programar, despachar, cancelar, extender, bitácora), `RentalBinResolver` (zona RENT y posición EN-RENTA a demanda, bajo bloqueo del almacén), `RentalStatusEffect` (sella fechas; impide cancelar con equipos despachados), resolvers de pertenencia de RENTAL/RENTAL_RETURN/RENTAL_PROCESS | `Services/Rental*.cs`, `DependencyInjection.cs` |
| API | `RentalsController` (`/api/v1/rentals`, módulo RENTAL_EQUIPMENT; un `[RequirePermission]` por acción) | `src/Teikem.Api/Controllers/RentalsController.cs` |
| Contratos | `Rental*Request`, `Rental*Dto`, `RentalQuery` | `Contracts/RentalContracts.cs`; `web-app/openapi.json` y `schema.d.ts` (web y app) regenerados (solo adiciones: 9 rutas, 15 esquemas) |

Flujo en el inventario: **programar** = `ReserveAsync` de cada serie en su posición actual (serie RESERVED; disponible baja); **despachar** =
`TRANSFER` por equipo a EN-RENTA con `FromReserved`, `ReserveAtDestination` y `TargetSerialStatus = ON_RENT`, referencia `RENTAL` (en mano
igual, disponible 0 en EN-RENTA); **cancelar** una Programada = `ReleaseAsync`. Orden de bloqueo: Rental (U) → Warehouse (despacho) →
saldos → series → NumberSequence.

## Cómo se probó

- `dotnet test tests/Teikem.Tests -o .tmp-testout`: **3100 pruebas, 0 fallas** (antes 3029). Nuevas: `RentalRulesTests` (mensajes exactos del
  plan, estatus, fechas/vencidas, tarifas, equipos, Kárdex, numeración y espejo en seed y estructura), `RentalLedgerTests` (despacho con
  reserva en destino y ON_RENT; serie en renta no se mueve, despacha, recibe ni reserva; devolución con `ExpectedSerialStatus` ON_RENT →
  IN_PROCESS reservada y liberación IN_PROCESS → AVAILABLE; defaults sin cambio; zona RENTAL excluida), `RentalServiceTests` (flujo
  completo, validaciones con texto exacto sin escribir, serie en una sola renta, programar sin equipos y con la serie movida, cancelar
  Programada y Borrador, agregar/quitar en Programada, tarifa en su lugar, extensión con bitácora y versión de tarifa, edición y sus
  guardas, filtros con reloj fijo, **aislamiento por compañía**). Ajustadas: `WmsControllerSecurityTests` (143 acciones),
  `WmsContractsTests` (firmas, auditoría y costuras del ledger), `OwnedEntityResolverCoverageTests` (resolvers y servicios de rentas),
  `PermissionPropagationTests` (rental.extend/return), KPI de series (`ProductBrandFilterTests`), `TenantIsolationModelTests`,
  `RawSqlConfinementTests` (18 sentencias), `CycleCountRulesTests`, conteos de permisos 66 → 68 (Fleet/Order/Trip/Wms/Pulse).
- SQL Server 2022 local (`scripts/dev-sqlserver.sh`): `db-init` **dos veces** sobre la base **nueva** `TeikemR1Smoke` (la segunda: scripts
  omitidos por hash, 68 permisos, 0 nuevos, tenant demo "ya existe"). Verificado por SQL: 8 tablas `Rental*`, CHECK de contadores con
  RENTAL/RENTALRETURN, `rental.extend/return` en las plantillas y en los clones de TenantAdmin y Operador de almacén, módulo "Rentas"
  (Almacen, depende de WMS_LOTSERIAL), FKs a Invoice.
- **Bloque 16C sobre una base existente** (simulación de Depot): base creada con la estructura ANTERIOR (`git show HEAD:…`) y el bloque
  nuevo aplicado solo con `sqlcmd -I`: con la capa vieja **vacía** se retira y se crean las 8 tablas (CHECK y FKs al día); con **una fila**
  en `RentalAsset` se conserva la capa vieja, sale el `AVISO (Lote 27, Rentas)…` y se crean las tablas nuevas sin recrear `RentalCharge`;
  aplicarlo **dos veces** no falla en ninguno de los casos. Las dos bases de simulación se borraron.
- `scripts/smoke.sh http://localhost:5180` (API de `.tmp-testout`, `Auth__Onboarding__Enabled=false`, `SMOKE_SQL` y `SMOKE_MIGRATION_RUN`
  con una envoltura del build de `.tmp-testout`): **SMOKE OK, 135 pasos, 137 `ok`**, a la primera. Paso nuevo "rentas (Lote 27, Rentas
  R1)" (smoke 1–4 del plan; el 1, convertir a serie, es el paso de R0 que le precede y le deja las series): módulo, catálogos, 400
  localidad/otra localidad/fechas, 403 Solo lectura, alta REN con contrato/transporte/tarifa sin reservar, 409 serie en otra renta, 422
  despachar en Borrador, programar (disponible 3 → 1, en mano 3), despachar (EN-RENTA a demanda, series ON_RENT, saldo 2/2, Kárdex
  TRANSFER neutra "Renta REN-…", historial y AuditLog), **mover o recolectar la serie rentada 409**, editar/cancelar despachada 422,
  **extender** (400 fecha con el texto exacto, 400 motivo, 403 sin `rental.extend`, tarifa versionada, bitácora), lista por vencer,
  cancelar una Programada libera la reserva, otra compañía 403/404, sin descuadre Kárdex ↔ saldo.
- Web `npm run check`: tipos generados, tsc, oxlint, **vitest 1251** (124 archivos) y build. App `npm run check`: tipos, typecheck, lint,
  **jest 388** (69 suites).

## Decisiones a revisar

1. **Cliente dado de baja = 409** (mensaje reutilizado `El cliente está dado de baja; solo se consulta su historial.`). El plan decía 422,
   pero el mensaje que pide reutilizar responde 409 en toda la plataforma (Lote 3); se dejó 409 por coherencia. También bloquea programar y
   despachar.
2. **Despachar exige Programada**: desde Borrador → 422 `Solo se despacha una renta Programada; programe la renta {n} primero.` (no se
   programa y despacha en un paso).
3. **La tarifa antes del despacho se corrige en su lugar** (una sola versión); después solo cambia con una extensión. La versión nueva de una
   extensión empieza el **día siguiente al recogido anterior** y la vieja se cierra ahí (`EffectiveTo` exclusivo, como `RateComponent`).
4. **Fechas y extensiones**: sin extensiones, el PATCH mueve inicio y recogido (la fecha pactada sigue a la vigente y la tarifa inicial al
   inicio); con extensiones, las fechas solo se mueven con otra extensión (422).
5. **El cliente no se cambia** (400); el almacén solo sin equipos (409); el contacto se valida contra el cliente (404).
6. **Equipos**: la serie debe estar AVAILABLE en una posición **recolectable** (no cuarentena/cruce/En renta) del almacén de origen; al
   programar se reserva donde esté HOY (si se movió, se actualiza la posición de origen).
7. **"Vencida" y "por vencer" incluyen Programadas** (abiertas = Programada o En renta), no solo En renta.
8. **Renta cancelada**: sus equipos quedan inactivos (libera la serie para otra renta) y la ficha los muestra inactivos.
9. **Zona y posición a demanda**: se usa la primera zona `RENTAL` del almacén (o se crea RENT); si los códigos RENT/EN-RENTA los usa otra zona o
   posición → 409. Una EN-RENTA desactivada a mano no se reactiva sola (422 del ledger al despachar).
10. **Mensajes nuevos** no previstos en el plan (todos en el manual y la FAQ): programar fuera de Borrador, despachar fuera de Programada,
    renta cancelada, fechas con extensiones, localidad dada de baja, almacén con equipos, frecuencia/monto de la tarifa, días negativos,
    códigos RENT/EN-RENTA ocupados, campos inmutables.
11. **Esquema de R2 creado en R1**: `RentalReturn`, `RentalReturnLine` y `RentalProcess` ya existen y están mapeados (sin servicio); se agregó
    `RentalReturn.TransportCurrencyLookupId` (moneda del costo de recogido), que el plan no listaba.
12. **Permisos de dueño**: RENTAL lee/escribe con `rental.view`/`rental.manage`; RENTAL_RETURN con `rental.view`/`rental.return`;
    RENTAL_PROCESS con `rental.view`/`rental.maintenance`. Propagación de `rental.extend`/`rental.return`: plantillas TenantAdmin y Operador
    de almacén, y el seed (5b3) a todo rol de compañía con `rental.manage`, una sola vez.
13. **Módulo**: "Rentas" depende ahora de `WMS_LOTSERIAL`: apagar Inventario y trazabilidad apaga Rentas (y Facturación de alquiler) en
    cascada; volver a encender Inventario **no** vuelve a encender Rentas.

## No probado

- Pantallas (bloque F-R1); sin Playwright.
- Concurrencia real en SQL Server (dos despachos en el mismo almacén creando EN-RENTA a la vez; dos rentas tomando la misma serie): el
  diseño la cubre con el bloqueo del almacén y el índice `UX_RentalLine_OpenSerial`, pero el smoke va en serie.
- Conteo cíclico de la posición EN-RENTA o de una serie en renta (D7): es del bloque R2 (hoy el ledger rechaza con 409 el movimiento que
  intentaría la reconciliación).
- Aplicar el esquema nuevo sobre la base REAL de Depot (solo se simuló con la estructura anterior).
- Una compañía que desactive estatus de la renta o `ON_RENT` (documentado: 422).
