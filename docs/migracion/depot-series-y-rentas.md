# Series de Advance Depot y Rentas: qué se puede traer de la migración y qué no (Lote 30, Rentas RM)

Bloque **RM** del plan de Rentas (`docs/rentas-plan-de-ejecucion.md`, decisión del dueño **D5-b**: "reimportar la migración de Depot con
seguimiento SERIAL"). Fecha: 2026-10-06. Este documento es la evaluación y el procedimiento para el dueño; el cierre técnico está en
`docs/lote30-decisiones.md`.

## 1. Respuesta corta

- **Ninguna fuente de la migración trae números de serie.** QuickBooks (`Depot Products.csv`) solo tiene dos **casillas** por producto,
  `Serial` y `Lot` (verdadero/falso), sin los números. El WMS anterior (MSWM, almacén `Main`) no tiene series y su lote es `§` o `0`
  (análisis del 2026-09-28, sección 1.3 de `docs/migracion-depot-solutions-plan.md`).
- Por eso una reimportación **no puede** dejar un producto de Depot con existencia **y** con sus series: el alta de las series de los
  equipos que ya están en el almacén va **siempre** por la herramienta **"Convertir a serie"** (bloque R0, capítulo 6 §2.1 del manual).
- Lo que sí hace ahora el importador (opcional, apagado por defecto): usar la casilla `Serial` (y, si se quiere, `Lot`) de QuickBooks para
  **crear con seguimiento SERIAL los productos nuevos que no tienen existencia**, y **listar como "Candidato a Convertir a serie"** los que
  están marcados pero tienen existencia (esos se crean sin seguimiento, `NONE`, como hoy).
- Un producto que **ya existe** en Teikem **nunca** cambia de seguimiento por la migración (regla D25); el reporte lo avisa.
- **Recomendación:** no recrear la base para esto. Con la base de Depot ya cargada, use "Convertir a serie" para cada equipo que se vaya a
  rentar. La opción del importador sirve si algún día se recrea la base desde cero (`scripts\recrear-base.ps1`), y aun así solo afecta a
  productos sin existencia.

## 2. Qué trae cada fuente

### 2.1 `Depot Products.csv` (QuickBooks Desktop, lista de ítems)

- El archivo real **no está en el repositorio** (contiene datos de la compañía; vive en `F:\Download\TeikemMigracion\`). Su formato
  exacto está en la muestra sintética `docs/migracion/sample/Sample Products.csv`: las columnas `Serial` y `Lot` existen y valen `TRUE` o
  `FALSE` por ítem.
- Según el análisis del 2026-09-28 (`docs/migracion-depot-solutions-plan.md` §1.1), de 576 ítems: **`Lot` = TRUE en 500** y **`Serial` =
  TRUE en 3**. El análisis concluyó que "parecen valores por defecto, no una política real". No se pudo ver aquí **cuáles** son los 3 ítems
  marcados de serie ni si tienen existencia en el WMS: el dry-run del paso 4.1 los lista.
- **No trae números de serie** (QuickBooks Desktop no guarda series por unidad en la lista de ítems).
- Hasta el Lote 30 el importador **no leía** esas dos columnas.

### 2.2 MSWM (WMS anterior, almacén `Main`)

- El importador solo lee `dbo.Item`, `dbo.Location`, `dbo.Inventory` (`OnHandQuantity <> 0`), `dbo.ItemUPC` y, para el cupo de las
  posiciones, `Inventory_Old`, `CycleCountInventory`, `CycleCountHistory` y `PutAwayHistory` (`src/Teikem.Infrastructure/Migration/MswmReader.cs`).
  **Ninguna consulta lee series.**
- El análisis del script completo `MSWM.sql` (2026-09-28) dice del inventario de `Main`: "**Sin series.** La columna *lote* vale `§` o `0`
  (nada)". Las fechas de vencimiento tampoco son fiables.
- **No se pudo verificar en este entorno**: `MSWM.sql` (398 MB) no está en el repositorio y no hay una base `MSWM` restaurada aquí. Para
  confirmarlo en su máquina, solo lectura (paso 4.1, punto 1):

  ```sql
  -- ¿Hay alguna columna que parezca de número de serie en MSWM?
  SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE
  FROM MSWM.INFORMATION_SCHEMA.COLUMNS
  WHERE COLUMN_NAME LIKE '%serial%' OR TABLE_NAME LIKE '%serial%'
  ORDER BY TABLE_NAME, COLUMN_NAME;
  ```

  Si aparece una tabla con series por unidad **y con datos de `Main`**, avísele al equipo de desarrollo: sería un lote nuevo (leerla y
  cargar las series con el saldo inicial). Si no aparece nada o está vacía, se confirma lo de este documento.

### 2.3 Conclusión

No hay origen de series individuales. El alta de series de los equipos que ya están en existencia es **por R0 ("Convertir a serie")**, con
los números que se lean de la placa o etiqueta de cada equipo.

## 3. Cómo se fijaba el seguimiento antes del Lote 30 (y el riesgo que había)

- `products.trackingType` del JSON (`NONE` en `import.depot.json`) se aplicaba **igual a todos** los productos que se creaban.
- Al refrescar (`--update`) nunca se cambia el seguimiento de un producto existente.
- **Riesgo latente:** el ledger no exige series en los asientos (solo en las reservas; ver el informe de R0). Si alguien ponía
  `"trackingType": "SERIAL"` en el JSON, todos los productos se creaban SERIAL y el saldo inicial entraba **sin series**: productos con
  existencia que el sistema cree "de serie" pero sin ninguna serie, que no se podrían recolectar, rentar ni convertir (R0 solo convierte
  `NONE`). El Lote 30 lo impide (sección 4).

## 4. Qué cambia en el Lote 30

| Pieza | Qué hace |
|---|---|
| Lector de QuickBooks | Lee las casillas `Serial` y `Lot` de la lista de ítems (si el archivo no las trae, quedan vacías). |
| Opción nueva `products.trackingFromColumns` | `{ "serial": true/false, "lot": true/false }`, las dos en `false` por defecto. Con `serial: true`, un ítem con `Serial = TRUE` pide seguimiento **SERIAL**; con `lot: true`, un ítem con `Lot = TRUE` pide **LOT**; con las dos marcadas gana SERIAL; si no, el de `products.trackingType`. Valores verdaderos: `TRUE`, `YES`, `Y`, `1`. |
| Regla del saldo inicial | Un producto que pide SERIAL o LOT y tiene saldo inicial en el plan se **crea con NONE** (ninguna fuente trae sus series ni lotes); el saldo se carga igual que hoy. Aplica también si el SERIAL o LOT viene de `products.trackingType`. |
| Producto que ya existe | No se toca su seguimiento; si el origen pide otro, advertencia y, si pide SERIAL y está en NONE, "Candidato a Convertir a serie". |
| Reporte | Mapeo **Seguimiento** (producto creado con SERIAL o LOT), mapeo **Candidato a Convertir a serie** (con unidades y posiciones) y las advertencias de la sección 6. Aparecen también en `--dry-run`. |
| Validación del JSON | `products.trackingType` distinto de `NONE`, `LOT` o `SERIAL` detiene la corrida: `products.trackingType debe ser NONE, LOT o SERIAL.` (código de salida 1). |
| Configuraciones del repositorio | **Sin cambios**: `import.depot.json`, `import.solutions.json` y la muestra siguen con `NONE` y la opción apagada (decisión conservadora; ver la decisión 52 del informe de Rentas). |

### Qué se puede y qué no

| Caso | ¿Se puede con la migración? | Cómo |
|---|---|---|
| Producto **nuevo**, marcado `Serial` en QuickBooks, **sin existencia** | Sí | Se crea SERIAL (con `trackingFromColumns.serial`). Los recibos posteriores pedirán las series. |
| Producto **nuevo**, marcado `Serial`, **con existencia** en el WMS (o en QuickBooks para Solutions) | No con series | Se crea NONE con su saldo y queda en "Candidato a Convertir a serie" → **R0** después de la carga. |
| Producto **ya existente** en NONE (todo Depot hoy) | No | La migración nunca cambia el seguimiento (D25) → **R0**. |
| Equipo de renta que en QuickBooks **no** está marcado `Serial` | No (la migración no lo sabe) | **R0** directamente. Es lo más probable para casi todos los equipos de renta: solo 3 ítems tienen la casilla. |
| Producto por **lote** | Solo si es nuevo y sin existencia (con `trackingFromColumns.lot`) | **No se recomienda** encenderlo para Depot: 500 de 576 ítems tienen `Lot = TRUE` por defecto y la decisión 4 de la migración fue "todo NONE". No existe una herramienta "Convertir a lote". |
| Cargar los **números de serie** desde un archivo | No existe | Fuera de alcance; si algún día hay un origen de series por unidad, sería un lote nuevo. Hoy se capturan con R0. |

## 5. Procedimiento para el dueño (paso a paso)

### 5.1 Ver qué haría la opción, sin tocar nada (recomendado primero)

1. (Opcional) Confirme en su máquina que MSWM no tiene series, con la consulta de la sección 2.2.
2. Copie `docs\migracion\import.depot.json` a un archivo fuera del repositorio, por ejemplo `F:\Download\TeikemMigracion\import.depot.series.json`,
   y cambie en la copia **solo** el bloque `products`, agregando:

   ```json
   "trackingFromColumns": { "serial": true, "lot": false }
   ```

   Las rutas de `sources` del JSON original son absolutas (`F:/Download/TeikemMigracion/...`), así que la copia funciona desde cualquier
   carpeta.
3. Simule contra la base actual (no escribe nada):

   ```
   dotnet run --project src/Teikem.Api -- import-legacy F:\Download\TeikemMigracion\import.depot.series.json --dry-run
   ```

4. Abra el `reporte-depot-AAAAMMDD-HHMM.md` que queda en `F:\Download\TeikemMigracion\` y busque:
   - **Advertencias** con *"ya existe con seguimiento NONE; la migración no cambia el seguimiento…"*: con la base ya cargada, serán los
     ítems marcados `Serial` en QuickBooks. **No cambian.**
   - **Mapeos** "Candidato a Convertir a serie": la lista de ítems marcados de serie que hay que convertir con R0.
   - **Mapeos** "Seguimiento": productos que se **crearían** con SERIAL (solo ítems nuevos sin existencia).

### 5.2 Con la base de Depot ya cargada (situación actual) — vía recomendada

No hace falta reimportar. Para **cada equipo que se vaya a rentar** (estén o no en la lista de candidatos):

1. Cuente físicamente las unidades por posición y anote el número de serie de cada una (placa o etiqueta del equipo).
2. Verifique que el producto no tenga unidades reservadas ni documentos abiertos (recibos, tareas, recolecciones o conteos) — si los hay,
   "Convertir a serie" responde 409 y dice cuál.
3. Conviértalo: `POST /api/v1/products/{publicId}/convert-to-serial` con una línea por posición y tantas series como unidades (capítulo 6
   §2.1 del manual; el botón en la ficha del producto llega con la web de Rentas, F-R1). Hace falta `inventory.manage` e `inventory.adjust`.
4. Desde ese momento el producto es SERIAL y sus equipos se pueden rentar (capítulo 11).

### 5.3 Si algún día se recrea la base desde cero (`scripts\recrear-base.ps1`)

1. Ponga `"trackingFromColumns": { "serial": true, "lot": false }` en el bloque `products` de `docs\migracion\import.depot.json` **solo si**
   confirma que la casilla `Serial` de QuickBooks marca equipos de verdad (el análisis dice que parecen valores por defecto). Si no, no lo
   cambie: el resultado es el de hoy.
2. Corra el script como siempre. **Recuerde que recrear borra todo** lo hecho en la base local (rentas, series ya convertidas, ajustes).
3. En el reporte: los ítems marcados sin existencia quedan SERIAL (mapeo "Seguimiento"); los marcados con existencia quedan NONE y en
   "Candidato a Convertir a serie" → siga 5.2 con ellos.
4. **No** encienda `lot` para Depot (sección 4).

### 5.4 Comprobación de solo lectura después de una carga

Ningún producto con seguimiento SERIAL debe tener más unidades en mano que series registradas (la consulta debe devolver 0):

```sql
SELECT COUNT(*) AS ProductosSerialSinSeries
FROM dbo.Product p
JOIN dbo.LookupCode l ON l.LookupCodeId = p.TrackingTypeLookupId AND l.InternalCode = 'SERIAL'
WHERE (SELECT ISNULL(SUM(b.QtyOnHand), 0) FROM dbo.StockBalance b WHERE b.ProductId = p.ProductId)
    > (SELECT COUNT(*) FROM dbo.InventorySerial s WHERE s.ProductId = p.ProductId);
```

## 6. Mensajes del reporte (exactos)

| Dónde | Mensaje | Qué hacer |
|---|---|---|
| Advertencia (Productos) | `El ítem {sku} está marcado como de serie pero tiene saldo inicial ({n} unidades) y la fuente no trae sus números de serie; se crea con seguimiento NONE. Conviértalo después con "Convertir a serie".` | Después de la carga, R0 (5.2). |
| Advertencia (Productos) | `El ítem {sku} está marcado por lote pero tiene saldo inicial ({n} unidades) y la fuente no trae sus lotes; se crea con seguimiento NONE.` | Nada; queda sin seguimiento (no hay "Convertir a lote"). |
| Advertencia (Productos) | `El producto {sku} ya existe con seguimiento {actual}; la migración no cambia el seguimiento de un producto existente (en el origen está marcado {pedido}). Si es un equipo con número de serie, use "Convertir a serie".` (la última frase solo si el origen pide SERIAL) | Si es un equipo de renta, R0 (5.2). |
| Mapeo "Seguimiento" | `{sku}` → `SERIAL` o `LOT` | Producto creado (o que se crearía) con ese seguimiento: sus recibos pedirán series o lote. |
| Mapeo "Candidato a Convertir a serie" | `{sku}` → `{n} unidades en {m} posición(es); se crea con NONE` o `ya existe con NONE; no se cambia` | Lista de trabajo para R0. |
| Error de configuración (código de salida 1) | `products.trackingType debe ser NONE, LOT o SERIAL.` | Corrija el JSON. |
