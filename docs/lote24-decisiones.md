# Lote 24 — Productos por posición (reemplaza a las hojas de posición) y filtro Pasillo (2026-10-05)

Pedido del dueño: quitar por completo la funcionalidad "Hojas de posición" (botón, estado, aviso, columna y filtro "Hoja"), que no le encontró uso y
fallaba en su base local, y tener en su lugar **un PDF para imprimir todos los productos de una posición** (como el reporte de códigos de barras de
productos, pero por posición) para que quien trabaja en un rack alto (hasta 30 pies) escanee los códigos **desde el papel**. Además: un filtro
**Pasillo** en la pantalla de Posiciones, y que **Exportar** saque todo lo filtrado y no solo la página.

Decisiones del dueño (2026-10-05): quitar **todo** lo del estado de la hoja (pantalla, columna y filtro, avisos, API, las dos columnas, el código del
ledger); el informe va **solo en Posiciones**; **una posición por página**. "Etiquetas de posición" (F16) no se toca.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Quitado (servidor) | `BinSheetService`, `BinSheetRules`, `GET .../bin-sheets`, `POST .../bin-sheets/mark-printed`, `sheetStatus` (filtro y campos), `staleCount`, `MarkSheetContentChangesAsync` del ledger, `WarehouseBin.SheetPrintedAtUtc` y `SheetContentChangedAtUtc` (entidad, mapeo y script de estructura) | `src/` y `Diseño/logistica-db-estructura.sql` |
| `GET /api/v1/warehouses/{id}/bin-products` | Los mismos filtros del listado de posiciones; por posición, sus productos con existencia (uno por producto, sin repetir lotes, por SKU) con `sku`, `name` y `barcode`; `take` ≤ 200 (400 si más) | `BinProductsService`, `BinProductsRules`, `WarehousesController.BinProducts` (`inventory.view`) |
| Web: `Productos por posición` | Botón junto a "Códigos de barras"; modal (filtro actual / marcadas, incluir vacías); PDF de una posición por página con el código de barras de cada producto (reusa `kernel/ui/binSheetPdf.ts`); no guarda estado | `BinProductsPanel.tsx`, `binProducts.ts` |
| Web: filtro **Pasillo** | Texto con pausa de 300 ms que va al servidor como `aisle` (ya existía en el API); entra a la consulta del informe, de Exportar, de "Códigos de barras" y de "Etiquetas" | `LocationsScreen.tsx` |
| Web: quitado | Columna y filtro "Hoja", aviso de desactualizadas, "Imprimir las desactualizadas", el marcado de impresas. Las casillas (Elegir) se quedan: las usan Etiquetas y el informe | `LocationsScreen.tsx`, `binSheets.ts` → `binProducts.ts` |
| Exportar | Ya sacaba **todo lo filtrado** en todas las tablas paginadas por el servidor (`fetchAllPages`, hasta 10 000 filas, páginas de 200); se verificó que ninguna pantalla con paginación de servidor exporta solo la página | `DataTable`, `fetchAllPages` |
| Manual | Capítulo `frontend/f15-productos-por-posicion.md` (reemplaza a f15-hojas), 06 §1.5 reescrito, FAQ "Productos por posición", README | `docs/manual/` |

## Cómo se probó

- Servidor: `dotnet test tests/Teikem.Tests`: **3008 pruebas pasan** (nuevas `BinProductsTests`: agregación sin repetir lotes, solo con existencia, por SKU,
  filtros, paginación, tope de 200, 404 de otro tenant, traducción a SQL Server y que las columnas ya no existen; ajustadas las de contratos,
  seguridad de controladores —129 acciones— y confinamiento de escrituras).
- Web: `tsc -b`, oxlint (sin avisos nuevos) y vitest (todas pasan): `binProducts.test.ts` (flujo sin marcado), `LocationsProducts.test.tsx` (casillas, filtro
  Pasillo, impresión, errores, permiso), las de etiquetas y de Ubicaciones ajustadas. Recorrido Playwright `productosPorPosicion.spec.ts` (reemplaza a
  `loteF15.spec.ts`; no corrido en esta sesión: pide el API con la base recreada).
- `scripts/smoke.sh`: el paso del Lote 23 se reescribió para `bin-products` (no corrido aquí).

## Decisiones a revisar

1. **Base de datos existente.** Las dos columnas quedaron fuera del script y del modelo. Una base que las tenga puede dejarlas (nulables, nadie las lee) o
   quitarlas con `ALTER TABLE dbo.WarehouseBin DROP COLUMN SheetPrintedAtUtc, SheetContentChangedAtUtc;`. La base local de desarrollo se recrea con
   `scripts\recrear-base.ps1`. En producción, desplegar el API nuevo no exige tocar la base.
2. **El PDF reusa el módulo de las hojas** (`binSheetPdf.ts`): ya tenía una posición por página, el código de barras de cada producto, tamaño adaptable y los
   avisos de "no cabe". Solo cambió el título ("Productos por posición") y, con él, el nombre del archivo. El pie y "Hoja 2 de 2" se conservan.
3. **Sin opciones de columnas** (Automático / 2 columnas): no aplican, porque la página se reparte sola entre los productos de la posición (1 o 2 grandes;
   3 a 10 en lista).
4. **Tope de 500 posiciones por PDF** (el mismo de antes). Para más, imprimir por partes (por pasillo).
5. No se agregó el informe a Productos e inventario (el dueño eligió solo Posiciones).

## Qué quedó fuera

- Control de impresión / aviso de papel desactualizado (se quitó a propósito).
- La app de almacén no cambió (no usaba las hojas).
