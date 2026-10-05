# Rentas (submódulo de Almacén) — informe de avance

Plan: `docs/rentas-plan-de-ejecucion.md`. Una sección por bloque, en el orden en que se cierran; al final, las **decisiones por defecto a
confirmar** por el dueño (lista numerada que se acumula bloque a bloque). El "Informe final" (sección 7 del plan) se agrega al terminar todos
los bloques.

## Bloque R0 — Convertir un producto a serie (Lote 26)

### Qué se hizo

- `POST /api/v1/products/{publicId}/convert-to-serial` (`ProductsController.ConvertToSerial`, `inventory.manage`, módulo `WMS_LOTSERIAL`) y
  `ProductSerialConversionService` (exige además `inventory.adjust`): en **una transacción**, bloquea el producto y sus saldos, verifica y, por cada
  posición con existencia, asienta por el **ledger** un `ADJUSTMENT −` del saldo sin serie y un `ADJUSTMENT +1` por cada serie capturada (motivo de
  sistema `TRACKING_CONVERSION`, neto cero), y cambia el seguimiento `NONE → SERIAL` (auditado). Es la única vía para cambiar el seguimiento de un
  producto con movimientos; el `PATCH` sigue en 409 (D25).
- Mensajes del plan tal cual: 400 `Capture {n} número(s) de serie para {bin} (hay {m}).` y 409 `El producto {sku} tiene unidades reservadas;
  libérelas antes de convertirlo.`; más los nuevos de la tabla del manual (documentos abiertos 409; ya es serie, es por lote, existencia fraccionaria,
  existencia sin posición o con lote 422).
- Seed: `AdjustmentReason` `TRACKING_CONVERSION` "Conversión a serie" (solo del sistema). Contratos regenerados (`web-app/openapi.json`, `schema.d.ts`
  de la web y de la app). La web oculta el motivo en el selector de ajuste.
- Documentación: `docs/lote26-decisiones.md`, capítulo 06 §2.1 (`docs/manual/06-inventario-y-almacen.md`), FAQ (sección "Lote 26") e índice del manual.
- Archivos principales: `src/Teikem.Infrastructure/Services/ProductSerialConversionService.cs`, `src/Teikem.Domain/Wms/SerialConversionRules.cs`,
  `src/Teikem.Infrastructure/Services/ProductOpenDocuments.cs` (criterio de documentos abiertos, compartido con la baja del producto),
  `tests/Teikem.Tests/ProductSerialConversionTests.cs`, paso nuevo en `scripts/smoke.sh`.

**Verificación de la sección 3 del plan contra el código** (todo coincide, con estos matices):
- D25: el texto exacto es `No se puede cambiar el tipo de seguimiento de un producto que ya tiene movimientos.` (el plan lo abrevia).
- `InventoryLedger` es el único que escribe saldos, Kárdex y estatus/ubicación de series (`WmsWriteConfinementTests`); `SerialRules.AlreadyInStock`,
  `CycleCountRules.IsInStock` (AVAILABLE/RESERVED) y el KPI "series por capturar" de `ProductService` (AVAILABLE/RESERVED) existen como dice el plan.
- El ledger **no** valida el seguimiento del producto en los asientos (solo en las reservas): la conversión puede asentar la salida sin serie y las
  entradas con serie en el mismo lote de asientos. Dato útil para R1.
- `[RequirePermission]`: las pruebas de seguridad de WMS exigen **exactamente uno** por acción, por eso el segundo permiso va en el servicio.
- Capa 16C vieja (`RentalAsset`, `RentalContract`, `RentalAssetMaintenance`, `RentalBillingRule`, `RentalCharge`) desde la línea ~2714 del SQL y FK
  diferida a `Invoice` (~2850): confirmado. Permisos `rental.*` en `PermissionCatalog` y en la plantilla WarehouseOperator: confirmado.
- `WarehouseTaskStatusEffect` resuelve por `IServiceProvider`: confirmado.

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `dotnet test tests/Teikem.Tests -o .tmp-testout` | **3027 pasan, 0 fallan** (antes 3014: +9 `ProductSerialConversionTests`, +3 firmas en `WmsContractsTests`, +1 acción en `WmsControllerSecurityTests`) |
| `scripts/dev-sqlserver.sh` + `dotnet .tmp-testout/Teikem.Api.dll db-init` ×2 sobre la base nueva `TeikemR0Smoke` | Inicialización completada las dos veces (la segunda, rama "ya existe") |
| `scripts/smoke.sh http://localhost:5180` (con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` del build de `.tmp-testout`) | **SMOKE OK**: 134 pasos, 136 `ok`, incluido el paso nuevo "convertir a serie (Lote 26, Rentas R0)" |
| `cd web-app && npm run check` | tipos, tsc, oxlint, **vitest 1250 pasan** (124 archivos), build |
| `cd app-almacen && npm run check` | tipos, typecheck, lint, **jest 388 pasan** (69 suites) |

Nota: la primera corrida del smoke falló **antes** del paso nuevo, en una aserción vieja del Lote 25 (texto de `allowEmpty`: el servidor ya dice "a uno o
ningún producto"); se corrigió el texto en el smoke y la corrida sobre una base nueva quedó verde.

### Qué NO se probó

- Pantalla: no hay botón "Convertir a serie" todavía (F-R1); sin Playwright.
- Concurrencia real en SQL Server (dos conversiones, o un movimiento del producto durante la conversión): se usa el orden de bloqueo de `ProductService`
  (producto U → saldos HOLDLOCK → ledger), pero el smoke va en serie.
- Reversa de la transacción a mitad del ledger con SQL Server real (InMemory no es transaccional; el ledger valida todo antes de escribir).
- Productos `NONE` que ya tenían filas de serie registradas (los 409 `ya está en inventario` / `fue dada de baja` del ledger).
- Datos reales de Depot (no se tocó la base del dueño).

### Decisiones tomadas por defecto en este bloque

Ver los puntos 1 a 9 de la lista de abajo.

## Bloque R1 — Renta hasta el despacho, extensiones y cancelación (Lote 27)

### Qué se hizo

- **Esquema** (`Diseño/logistica-db-estructura.sql`, capa 16C reescrita con guardas `IF OBJECT_ID … IS NULL`): `Rental`, `RentalLine`
  (`UX_RentalLine_OpenSerial`), `RentalExtension`, `RentalLineRate`, `RentalReturn`, `RentalReturnLine`, `RentalProcess` y `RentalCharge`
  por equipo (sin mapear). El bloque es autosuficiente para una base existente: retira la capa vieja solo si está vacía (si no, `PRINT` de
  aviso), recrea `CK_NumberSequence_Kind` con `RENTAL`/`RENTALRETURN` y agrega las FKs a `Invoice`.
- **Seed**: módulo "Rentas" (Almacén, depende de `WMS_LOTSERIAL`), ZoneType `RENTAL`, SerialStatus `ON_RENT`/`IN_PROCESS`, `RentalStatus`,
  `RentalProcessStatus`, motivos y condiciones de devolución, `DAILY` y "Fija", EntityTypes `RENTAL`/`RENTAL_RETURN`/`RENTAL_PROCESS`
  (los viejos desactivados), regla lateral de CANCELLED, permisos `rental.extend` y `rental.return` (Operador de almacén y, una vez, todo
  rol de compañía con `rental.manage`). Espejo en `PermissionCatalog` (68 permisos).
- **Ledger** (plan 4.1): `ExpectedSerialStatus`, `TargetSerialStatus` y `ReserveAtDestination` en el asiento; estatus esperado y destino en
  las reservas. Zona RENTAL excluida de asignación, recolección, "solo con disponible", acomodo y recibo directo. `IsInStock` y el KPI de
  series cuentan ON_RENT e IN_PROCESS. Kárdex con referencias RENTAL/RENTAL_RETURN/RENTAL_PROCESS.
- **Servicio y API**: `RentalService` (lista con "por vencer"/"vencidas", ficha, alta, edición, equipos, tarifa, programar, despachar,
  cancelar, extender y bitácora), `RentalBinResolver` (zona RENT/posición EN-RENTA a demanda), `RentalStatusEffect`, resolvers de pertenencia
  y `RentalsController` (`/api/v1/rentals`, 12 acciones). Mensajes del plan tal cual (ver el capítulo 11).
- **Contrato** regenerado (`web-app/openapi.json` + `schema.d.ts` de la web y la app; solo adiciones: 9 rutas y 15 esquemas).
- **Documentación**: `docs/lote27-decisiones.md`, capítulo nuevo `docs/manual/11-rentas.md`, sección "Lote 27" de la FAQ e índice del manual.
- Archivos principales: `src/Teikem.Domain/Wms/Rental.cs`, `src/Teikem.Domain/Wms/RentalRules.cs`, `src/Teikem.Infrastructure/Services/RentalService.cs`,
  `RentalBinResolver.cs`, `RentalStatusEffect.cs`, `RentalOwnedEntityResolvers.cs`, `Persistence/Configurations/RentalConfigurations.cs`,
  `Contracts/RentalContracts.cs`, `src/Teikem.Api/Controllers/RentalsController.cs`, `Wms/InventoryLedger.cs`, `Wms/WmsSeams.cs`; pruebas
  `RentalRulesTests`, `RentalLedgerTests`, `RentalServiceTests`; paso nuevo en `scripts/smoke.sh`.

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `dotnet test tests/Teikem.Tests -o .tmp-testout` | **3100 pasan, 0 fallan** (antes 3029: +3 archivos nuevos de rentas y ajustes en 14 archivos de prueba existentes) |
| `scripts/dev-sqlserver.sh` + `db-init` ×2 sobre la base nueva `TeikemR1Smoke` (desde `.tmp-testout`, `ASPNETCORE_ENVIRONMENT=Development`) | Completada las dos veces; la segunda omite los scripts por hash, 68 permisos (0 nuevos), tenant demo "ya existe" |
| Consultas por `sqlcmd` a `TeikemR1Smoke` | 8 tablas `Rental*`; CHECK de contadores con RENTAL/RENTALRETURN; `rental.extend`/`rental.return` en plantillas y clones de Admin y Operador; módulo "Rentas" (Almacen, WMS_LOTSERIAL); FKs a Invoice |
| Bloque 16C sobre una base con la estructura ANTERIOR (`sqlcmd -I`), vacía y con una fila en `RentalAsset`, aplicado dos veces | Vacía: capa vieja retirada y tablas nuevas creadas. Con datos: capa vieja conservada con `AVISO (Lote 27, Rentas)…`, tablas nuevas creadas sin recrear `RentalCharge`. Reaplicar: sin errores |
| `scripts/smoke.sh http://localhost:5180` (con `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` del build de `.tmp-testout`) | **SMOKE OK: 135 pasos, 137 `ok`** a la primera, incluido el paso nuevo "rentas (Lote 27, Rentas R1)" (smoke 1–4 del plan) |
| `cd web-app && npm run check` | tipos, tsc, oxlint, **vitest 1251 pasan** (124 archivos), build |
| `cd app-almacen && npm run check` | tipos, typecheck, lint, **jest 388 pasan** (69 suites) |

### Qué NO se probó

- Pantallas (F-R1); sin Playwright.
- Concurrencia real en SQL Server (dos despachos creando EN-RENTA a la vez, dos rentas tomando la misma serie): cubierta por diseño (bloqueo
  del almacén, índice único), el smoke va en serie.
- Conteo cíclico de EN-RENTA o de una serie en renta (D7): bloque R2.
- El esquema nuevo sobre la base REAL de Depot (solo se simuló con la estructura anterior).
- Una compañía que desactive estatus de la renta o de la serie (documentado como 422).

### Decisiones tomadas por defecto en este bloque

Ver los puntos 10 a 27 de la lista de abajo.

## Decisiones por defecto a confirmar

1. **(R0) Segundo permiso en el servicio.** El endpoint pide `inventory.manage` y el servicio exige `inventory.adjust` (403 `Falta el permiso
   'inventory.adjust'.`). Alternativa: dos atributos en el controlador (rompe la regla de "uno por acción" de las pruebas de seguridad). Consecuencia: igual
   para el usuario.
2. **(R0) Lectura del mensaje del plan** `Capture {n} número(s) de serie para {bin} (hay {m}).`: `{n}` = unidades en mano en la posición, `{m}` = series
   capturadas. Alternativa: `{n}` = las que faltan y `{m}` = las unidades. Consecuencia: solo el texto que ve el usuario.
3. **(R0) Conversión de todo el producto en una solicitud**: hay que capturar las series de **todas** las posiciones con existencia a la vez (el seguimiento es del
   producto). Alternativa: convertir posición por posición (dejaría el producto con existencia sin serie mientras tanto). Consecuencia: con muchos equipos repartidos, la
   captura es una sola y grande.
4. **(R0) Solo `NONE` → `SERIAL`**; un producto por lote no se convierte (422). Alternativa: permitir `LOT → SERIAL` conservando el lote en cada serie.
5. **(R0) Bloqueo por documentos abiertos** (recibos, tareas, recolecciones eliminables, conteos abiertos; 409, mismo criterio que la baja del producto), además del
   bloqueo por reservas del plan. Alternativa: permitirlo (esos documentos moverían el producto sin series después de convertido).
6. **(R0) Existencia no convertible → 422**: fraccionaria, sin posición o con lote. Alternativa: redondear o asignar una posición por defecto (no recomendado).
7. **(R0) Producto inactivo**: se permite convertirlo (solo cambia el seguimiento: por la regla de baja no tiene existencia). Alternativa: exigir que esté activo.
8. **(R0) Movimientos sin documento de referencia** y nota por defecto "Conversión a serie". Alternativa: referenciar al producto (el Kárdex mostraría `PRODUCT·id`).
9. **(R0) Reporte de ajustes de la web**: los ajustes de la conversión se **incluyen** (neto 0). Alternativa: excluirlos como el saldo inicial de la migración.
10. **(R1, plan §2) Una renta = un cliente, una localidad, varios equipos**, y **solo equipos propios con serie** (una línea por serie).
    Alternativa: varias localidades por renta o equipos por cantidad. Consecuencia: para dos localidades se hacen dos rentas.
11. **(R1, plan §2) Cancelar con `rental.manage` y solo antes del despacho** (Borrador o Programada); después se termina con la devolución.
    Alternativa: permiso propio de cancelación o cancelar también En renta (habría que devolver el equipo igual).
12. **(R1, plan §2) "Programada" reserva las series**; "Borrador" no reserva. Alternativa: reservar desde el Borrador (el equipo dejaría de
    estar disponible desde el primer borrador).
13. **(R1, plan §2) Tarifa opcional para programar y despachar**. Alternativa: exigir tarifa a cada equipo antes de programar.
14. **(R1, plan §2) "Vencida" es dato calculado, no estatus**, e incluye las rentas **Programadas** además de las En renta (abiertas). Alternativa:
    solo En renta. Consecuencia: una renta programada que no salió a tiempo aparece como vencida.
15. **(R1, plan §2) Una posición EN-RENTA por almacén** (zona RENT, creada a demanda en el primer despacho). Alternativa: una posición por
    cliente o por renta (más posiciones; reportes por posición).
16. **(R1, plan §2) Permiso nuevo `rental.return`** sembrado ya (se usa en R2) y **`rental.extend`** para extender (D4). Ambos en la plantilla
    del Operador de almacén y, una sola vez, en todo rol de compañía con `rental.manage`. Alternativa: dárselos solo a los clones de plantilla.
17. **(R1, plan §2) El enlace a la factura va en el encabezado** (`Rental.InvoiceId`, con FK) y el del envío también (`DeliveryShipmentId`, sin
    FK). Para cobro por tiempo habrá varias facturas: irán en `RentalCharge` (por equipo, sin mapear).
18. **(R1) Cliente dado de baja = 409** con el mensaje reutilizado (el plan decía 422; en toda la plataforma ese mensaje es 409). También bloquea
    programar y despachar.
19. **(R1) Despachar exige Programada** (422 desde Borrador; no hay "programar y despachar" en un paso).
20. **(R1) Tarifa**: antes del despacho se corrige la vigente en su lugar; una extensión con tarifa distinta cierra la vigente y abre otra
    **desde el día siguiente al recogido anterior** (`EffectiveTo` exclusivo, como `RateComponent`); si la tarifa no cambia no hay versión nueva.
    La moneda por defecto es la de la compañía.
21. **(R1) Fechas**: sin extensiones el PATCH mueve inicio y recogido (la fecha pactada sigue a la vigente); con extensiones, solo otra
    extensión mueve la fecha (422). Alternativa: permitir acortar la renta con una "extensión" hacia atrás.
22. **(R1) El cliente de la renta no se cambia** (400; cancelar y crear otra) y el almacén de origen solo sin equipos (409).
23. **(R1) Serie elegible** = AVAILABLE en una posición recolectable (no cuarentena, cruce de muelle ni En renta) del almacén de origen; al
    programar se reserva donde esté en ese momento. Alternativa: permitir rentar desde cuarentena.
24. **(R1) Renta cancelada**: sus equipos quedan inactivos (la serie se libera para otra renta) y la ficha los muestra inactivos.
25. **(R1) Códigos RENT/EN-RENTA ocupados → 409** (hay que renombrar la zona o posición existente); una EN-RENTA desactivada a mano no se
    reactiva sola (422 al despachar).
26. **(R1) Módulo "Rentas" depende de Inventario y trazabilidad**: apagar `WMS_LOTSERIAL` apaga Rentas en cascada y volver a encenderlo no
    reenciende Rentas. Los estatus de la renta se pueden renombrar/reordenar pero **no** desactivar (422).
27. **(R1) Esquema de R2 creado ya** (`RentalReturn`, `RentalReturnLine`, `RentalProcess` mapeados, sin servicio) con una columna extra no
    listada en el plan: `RentalReturn.TransportCurrencyLookupId` (moneda del costo de recogido). La capa vieja se retira **solo si está vacía**.
