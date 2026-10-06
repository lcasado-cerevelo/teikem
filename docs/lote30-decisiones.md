# Lote 30 — Rentas RM: seguimiento de los productos al migrar (series de Advance Depot) (2026-10-06)

Bloque **RM** del submódulo Rentas (`docs/rentas-plan-de-ejecucion.md`, sección 5; decisión del dueño **D5-b**: reimportar la migración de
Depot con seguimiento SERIAL). Es un bloque de **investigación + cambios chicos y seguros**: la evaluación completa y el procedimiento para
el dueño están en `docs/migracion/depot-series-y-rentas.md`.

## Conclusión de la investigación

- **Ninguna fuente trae números de serie.** `Depot Products.csv` (QuickBooks) tiene por ítem las **casillas** `Serial` y `Lot` (TRUE/FALSE;
  según el análisis de 2026-09-28, `Lot` = TRUE en 500 de 576 y `Serial` = TRUE en 3, "parecen valores por defecto"). El MSWM de Depot
  (almacén `Main`) no tiene series y su lote es `§` o `0`; el importador no lee ninguna tabla de series. El CSV real y el `MSWM.sql` no están
  en el repositorio: lo de las fuentes sale del análisis documentado y de la muestra sintética con el formato exacto de QuickBooks.
- Antes de este lote el importador **no leía** esas casillas: `products.trackingType` (NONE) se aplicaba igual a todo producto creado, y
  `--update` nunca toca el seguimiento.
- **Riesgo latente encontrado:** el ledger no exige series en los asientos; con `"trackingType": "SERIAL"` en el JSON, todos los productos
  se habrían creado SERIAL con su saldo inicial **sin series** (inconvertibles por R0, que solo convierte NONE).
- Por eso la **reimportación no puede** dejar con series los productos con existencia: el alta de series de lo que ya está en el almacén
  es siempre por **"Convertir a serie" (R0)**. Recomendación: no recrear la base de Depot para esto.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Lector | `QbItem` gana `Serial` y `Lot` (propiedades `init`, sin romper las llamadas posicionales); `null` si el archivo no tiene la columna | `Migration/QuickBooksCsvReader.cs` |
| Configuración | `products.trackingFromColumns` `{ serial, lot }` (las dos `false` por defecto). `products.trackingType` se normaliza a mayúsculas y se valida: otro valor → `products.trackingType debe ser NONE, LOT o SERIAL.` (error de configuración, código de salida 1) | `Migration/LegacyImportConfig.cs` |
| Reglas puras | `IsQuickBooksTrue` (TRUE/YES/Y/1), `TrackingFromColumns` (Serial → SERIAL; si no Lot → LOT; si no, la configuración; SERIAL gana) y los mensajes `TrackingDowngraded` y `TrackingNotChanged` | `Domain/Migration/LegacyImportRules.cs` |
| Plan | `PlannedProduct.RequestedTracking` (lo que pide el origen) y `TrackingType` (con el que se crea). `LegacyImportPlanner.ApplyTrackingGuard` (después del saldo inicial): todo producto que pediría SERIAL o LOT y tiene saldo inicial en el plan se crea **NONE** (unidades y posiciones guardadas para el reporte). Aplica también al SERIAL/LOT de `products.trackingType` | `Migration/LegacyImportService.cs` |
| Ejecución | El alta usa `p.TrackingType` (antes `cfg.Products.TrackingType`). Producto nuevo: mapeo **"Seguimiento"** (SERIAL/LOT) o advertencia + mapeo **"Candidato a Convertir a serie"** (`{n} unidades en {m} posición(es); se crea con NONE`). Producto existente: nunca se toca su seguimiento; si el origen pide otro, advertencia y, si pide SERIAL y está en NONE, candidato (`ya existe con NONE; no se cambia`). Igual en `--dry-run` | `Migration/LegacyImportService.cs` |
| Configuraciones reales | **Sin cambios**: `import.depot.json`, `import.solutions.json` y la muestra siguen con NONE y la opción apagada (prueba que lo fija) | `docs/migracion/*.json` |

Sin cambios de esquema, seed, permisos, endpoints ni contrato del API. `RawSqlConfinementTests` (19) y `WmsControllerSecurityTests` (151)
sin cambios. La confinación de `TrackingTypeLookupId` (solo `ProductService` y `ProductSerialConversionService` lo escriben) sigue verde:
el importador solo lo lee.

## Cómo se probó

- `dotnet test tests/Teikem.Tests -o .tmp-testout`: **3172 pruebas, 0 fallas** (antes 3145 en `caf41ce`; +27, todas en
  `LegacyImportTrackingTests`): lector con y sin las columnas y la muestra (todas FALSE); casilla verdadera (10 casos); regla de columnas
  (7 casos, incluido "sin la opción = como antes"); defaults y validación de la configuración (mensaje exacto) y las tres configuraciones
  del repositorio con NONE y la opción apagada; plan sin la opción (NONE aunque estén marcados); plan con columnas (SERIAL, LOT, NONE);
  **producto de serie o de lote con saldo del WMS → NONE con su saldo** (3 unidades en 2 posiciones, el saldo no se pierde, invariante
  "ningún producto con seguimiento queda con saldo"); `trackingType: SERIAL` con saldo de QuickBooks → NONE solo los que tienen existencia;
  mensajes exactos; **servicio en dry-run** sobre EF InMemory (compañía nueva: mapeos y advertencia, saldo 10 u., nada escrito); **carga
  real con `--update` sobre una compañía existente** (EF InMemory): el producto existente sigue NONE aunque QuickBooks diga Serial (y su
  nombre sí se actualiza), advertencia y candidato; los nuevos quedan SERIAL, NONE y LOT según sus casillas.
- SQL Server 2022 local, base **nueva** `TeikemRMSmoke` (`db-init` dos veces: la segunda omite scripts por hash, 68 permisos, 0 nuevos):
  **carga real** de una copia de la muestra sintética con 3 ítems agregados (`EQ-RM1` Serial+Lot con 3 u., `EQ-RM2` Serial sin existencia,
  `EQ-RM3` solo Lot) y `trackingFromColumns.serial` encendido, desde el build de `.tmp-testout` (`import-legacy`, código 0): 8 productos
  creados, 0 rechazos, 5 asientos de saldo inicial, conciliación sin diferencias; reporte con la advertencia de `EQ-RM1`, su mapeo
  "Candidato a Convertir a serie" (`3 unidades en 1 posición(es); se crea con NONE`) y "Seguimiento" `EQ-RM2` → `SERIAL`. Por `sqlcmd`:
  `EQ-RM1` NONE con 3 en mano, `EQ-RM2` SERIAL sin existencia, `EQ-RM3` NONE; **0 productos SERIAL con más unidades que series**. Segunda
  corrida con `--update` (código 0): `EQ-RM1` sigue NONE, con la advertencia "ya existe con seguimiento NONE…" y el candidato `ya existe con
  NONE; no se cambia`.
- `scripts/smoke.sh`: ver la entrada RM de `docs/rentas-informe.md` (corrida completa sobre una base nueva).

## Decisiones a revisar

Son los puntos 52 a 60 de "Decisiones por defecto a confirmar" en `docs/rentas-informe.md`:

1. **La opción queda apagada en los JSON del repositorio** (Depot incluido): las 3 casillas `Serial` de Depot parecen valores por defecto y
   el dueño decidió en la migración "todo NONE". Encenderla es una línea en `import.depot.json` (procedimiento en el documento de Depot).
2. **Marcado SERIAL o LOT con saldo inicial → se crea NONE** con su saldo y se informa, en lugar de rechazar el producto, omitir el saldo o
   crearlo SERIAL sin series.
3. **SERIAL gana a LOT** cuando las dos casillas están marcadas.
4. **La regla usa el saldo planeado**, aunque en esa corrida el saldo no se cargue (ya cargado, o `--update` de una compañía existente).
5. **La regla también protege `products.trackingType`** SERIAL/LOT (antes dejaba existencia sin series).
6. **Producto existente con otro seguimiento: solo advertencia** (no rechazo ni cambio; tampoco cuenta como actualizado).
7. **`products.trackingType` se valida al leer el JSON** (código 1), no producto por producto.
8. **Casilla verdadera** = `TRUE`, `YES`, `Y` o `1` (sin distinguir mayúsculas).
9. **No hay "Convertir a lote"** ni carga de números de serie desde archivo (no hay origen).

## No probado

- Datos reales: no se tocó la base `Teikem` del dueño ni se leyó el `Depot Products.csv` real ni el MSWM (no están aquí). No se sabe cuáles
  son los 3 ítems marcados `Serial` ni si tienen existencia: lo dice el dry-run del procedimiento (sección 5.1 del documento de Depot).
- Que MSWM no tenga ninguna tabla de series: se apoya en el análisis de 2026-09-28; la consulta de verificación queda en el documento.
- La conversión R0 de un producto importado concreto (la cubre el paso R0 del smoke con un producto creado por el API).
- `scripts\recrear-base.ps1` con la opción encendida (Windows; no se puede correr aquí).
