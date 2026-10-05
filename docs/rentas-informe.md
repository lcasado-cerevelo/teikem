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
