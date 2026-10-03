# Lote F14 — Códigos de barras para el conteo: reportes de productos y de posiciones (2026-10-03)

Pedido del dueño del 2026-10-03: al contar, los productos no siempre tienen etiqueta y las etiquetas de las posiciones están mal
colocadas; se necesitan dos reportes imprimibles con un código de barras por elemento para escanear el papel con el Zebra. El mismo día
el dueño cambió el diseño a **rejilla de 3/2/1 columnas, agrupada** (menos páginas), que es lo que quedó. Número: en `docs/frontend/` el
último era F13; se tomó el siguiente libre, **F14**. Solo se tocó `web-app/` y `docs/` (nada de `src/` ni de `app-almacen/`). Manual de
pantallas: [f14-codigos-de-barras.md](../manual/frontend/f14-codigos-de-barras.md); FAQ: sección "Lote F14" de
[faq.md](../manual/faq.md).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Codificador Code 128 | Puro, sin dependencias: subconjunto B (ASCII 32–126) y C para corridas de dígitos con las reglas de longitud mínima del anexo E de ISO/IEC 15417 (inicio C con 2 dígitos o ≥ 4; en medio ≥ 6; al final ≥ 4; corrida impar = un dígito en B), verificador módulo 103, parada de 13 módulos; módulos y barras como datos; `code128Unsupported` lista lo que no se admite | `kernel/ui/code128.ts` |
| Reporte de códigos (kernel) | PDF carta vertical: encabezado y pie de los reportes de marca, resumen (Elementos, Con código, No caben, Omitidos), rejilla con elección automática de columnas (3 → 2 → 1, o un máximo), grupos con título a todo el ancho (no queda huérfano; "(continuación)" en la página siguiente), celdas que no se parten, barras vectoriales (`doc.rect`), zona de silencio de 10 módulos, módulo 0.33 mm que baja hasta 0.25 mm, barras de 12 mm, valor legible; "No cabe" y omitidos listados al final | `kernel/ui/barcodeReportPdf.ts` |
| Encabezado y pie compartidos | `drawReportHeader`, `drawReportPageChrome`, `drawReportNotice` (+ `reportNoticeLines`, `reportNoticeHeight`, `REPORT_LAYOUT`, `REPORT_COLORS`) salieron de `renderReportPdf` sin cambiar su salida (comparada byte a byte con la versión anterior, sin fecha de creación) | `kernel/ui/reportPdf.ts` |
| Filtros de la barra | `useAppliedFilters()` → los filtros con valor del `FilterScope` en el momento del clic (los mismos de la línea de filtros de Exportar) | `kernel/ui/filterScopeContext.ts` |
| Armado por pantalla | Productos: lo de `productListQuery` (tabla, Exportar, Reporte de inventario), agrupado por categoría (ruta de `names.categories`, "Sin categoría" al final), SKU en orden natural. Posiciones: la consulta del listado de la pantalla, agrupadas por el primer número del código (`binFirstNumber`), "Otras posiciones" al final, código en orden natural | `features/warehouse/barcodeReports.ts` |
| Botones | `ProductBarcodeReportButton` (cabecera de Productos e inventario, junto a los otros dos reportes) y `BinBarcodeReportButton` (cabecera del panel de Posiciones y de la pestaña Posiciones de la ficha del almacén), con `<Can perm="inventory.view">`, el `ReportButton` de los demás reportes ("Generando…", toast) y un `<select>` Automático / 2 columnas | `BarcodeReportButtons.tsx`, `InventoryReportButtons.tsx` (exporta `ReportButton`), `useProductReportContext.ts` (contexto extraído) |
| Filtros del PDF de productos | `describeProductFilters(…, 'barcodes')`: como el de inventario pero el almacén sin la nota de cantidades | `productFilters.ts` |
| Textos | `ui.barcodeReport.*` y `warehouse.barcodes.*` en es/en | `kernel/i18n/{es,en}.json` |
| Estilo | `.bc-report` / `.bc-cols` (selector pegado al botón; envuelve a 360 px; foco visible) | `warehouse.css` |
| Contrato | `KIT.md`: filas de `code128.ts`, `barcodeReportPdf.ts`, piezas compartidas de `reportPdf.ts`, `useAppliedFilters` y los botones | `web-app/KIT.md` |

El contrato del API **no cambió** (no se regeneró nada a mano; `npm run check` corre `api:types`).

## Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `npm run check` en `web-app/` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: 117 archivos, 1180 pruebas; oxlint solo con los avisos que ya existían (ninguno en archivos de este lote) |
| `code128.test.ts` (23): tabla (107 patrones, 11/13 módulos, sin repetidos), valores y verificador de 18 vectores (B solo, C solo, cambios B↔C, corridas pares/impares al inicio, en medio y al final, SKU con guiones y barras, código de posición), mínimo (1 carácter) y máximo (60 = largo máximo del SKU en la base; 60 dígitos), caracteres no admitidos | **pasaron** |
| `barcodeReportPdf.test.ts` (10): módulo 0.33/0.25/null, anchos por columnas, elección 3/2/1 (15/16/27/28 letras; 30 dígitos en 3), "No cabe" solo si no cabe en 1 (62 sí, 63 no), omitidos y grupos vacíos, avisos de arriba y del final, paginación (fila nunca partida, título nunca huérfano, continuación, primera página sin espacio), documento: 40 elementos en rejilla < páginas que una fila por elemento, carta (612×792), vacío | **pasaron** |
| `barcodeReports.test.ts` (6): productos por categoría con ruta, "Sin categoría" al final, SKU natural, SKU exacto (no el `barcode`), inactivo; posiciones: primer número (`A01-R01-N1-P01`→01, `R1`→1, `B-06`→06, `GENERAL`→ninguno), 01 y 1 juntos, 2 antes que 10, números de 21 dígitos, "Otras posiciones" al final, zona y almacén, provisional e inactiva, truncado | **pasaron** |
| `BarcodeReportButtons.test.tsx` (7): sin `inventory.view` no se pinta; lee lo filtrado (de a 200) y arma los grupos; "2 columnas" viaja al reporte; "Generando…" deshabilitado y toast de error; posiciones con los filtros de la barra + el almacén; consulta imposible = reporte vacío sin llamar al API | **pasaron** |
| `npx playwright test --project=escritorio-f14 --project=movil-f14 --no-deps --workers=1` (API real en :5000 con `Auth__Onboarding__Enabled=false`, Vite con `VITE_API_URL=http://localhost:5000`, Chromium de `/opt/pw-browsers`) | **3 pasaron** (2 de escritorio y 1 de móvil 360 px; los otros 3 se omiten por proyecto) |
| Suite completa `npx playwright test --workers=1` (porque cambió `playwright.config.ts`) | **81 pasaron, 74 omitidos por proyecto, 0 fallaron** (8.1 min) |

Recorrido Playwright (`e2e/loteF14-codigos.spec.ts`): siembra por API tres productos (`<SUF>-10`, `-2`, `-1`) y cinco posiciones en RSV de
ALM-01 (`R10-`, `GEN-`, `R2-`, `R01-…-B`, `R1-`), con un sufijo solo de letras. Productos: filtro Nombre → descarga → PDF válido (`%PDF-`,
`%%EOF`), nombre de archivo con el patrón de los reportes, los SKU del PDF son exactamente los que da `GET /products?name=` (mismo
número de filas) y en orden natural, con "Sin categoría (3)" y los filtros. Posiciones: filtro Posición → descarga → mismas comprobaciones
contra `GET /warehouses/{id}/bins?search=`, y grupos "Grupo 1 (2)", "Grupo 2 (1)", "Grupo 10 (1)", "Otras posiciones (1)" en ese orden.
Móvil: los dos flujos y sin scroll horizontal de página a 360 px. Proyectos `escritorio-f14`/`movil-f14` detrás de F13 y delante de F9
(F9 ahora depende también de F14). Capturas `f14-productos-boton.png` y `f14-ubicaciones-boton.png`.

### Validación del codificador contra implementaciones independientes (fuera del repo)

En un entorno virtual de Python en el scratchpad (no es dependencia del repo) con `python-barcode` 0.15, `zxing-cpp` y Pillow:

1. **Tabla de patrones**: los 107 patrones de `CODE128_PATTERNS` convertidos a bits son **idénticos** a `barcode.charsets.code128.CODES`
   de python-barcode; la parada difiere solo en forma: python-barcode guarda `11000111010` y agrega la barra final `11` aparte; la nuestra
   es `2331112` = `1100011101011` (lo mismo).
2. **Decodificación**: cada símbolo se dibujó como imagen (3 px por módulo, 10 módulos de silencio) y se leyó con **zxing-cpp**: **32/32**
   vectores elegidos (los de las pruebas, 1 carácter, 60 letras, 60 dígitos, símbolos ASCII, `\` y `` ` ``) y **500/500** valores aleatorios
   (1–30 caracteres, mezcla de dígitos y ASCII) se decodificaron con el texto exacto y formato Code 128.
3. **Largo**: comparado con python-barcode, nuestro símbolo nunca es más largo; en 23 de los 500 aleatorios es más corto (python-barcode
   no usa C en corridas al final del valor como `AB12345`).
4. **PDF real**: los PDF generados (muestras de 3/2/1 columnas, 46 elementos; y los que descargó el recorrido Playwright) se pasaron a
   imagen con `pdftoppm` a **600, 300, 203 y 150 dpi** y zxing-cpp leyó **todos** los códigos con su valor exacto (46/46 en cada muestra,
   5/5 posiciones y 3/3 productos de los PDF del recorrido), incluidos los angostados a ~0.25 mm a 203 dpi (resolución de una Zebra).
   No se instaló `zbarimg` (no estaba; zxing-cpp por pip bastó). **No se probó con un Zebra físico ni con papel impreso.**

### Suite completa

`PW_CHROMIUM_PATH=/opt/pw-browsers/chromium npx playwright test --workers=1` (API y Vite como arriba): **81 pasaron, 74 omitidos por
proyecto, 0 fallaron** (8.1 min). Las capturas de otros lotes que regeneró la corrida se descartaron con `git checkout --`; se quedan
solo las `f14-*`. Las capturas de los PDF (`f14-pdf-*.png`) y `f14-ejemplo-posiciones.pdf` salen de la corrida del recorrido F14 anterior
(con `F14_PDF_DIR`), por eso su sufijo no coincide con el de las capturas de pantalla; `f14-pdf-rejilla-ejemplo.png` es una muestra con
datos de prueba (46 elementos, página 2: rejilla de 3 columnas y título "(continuación)").

## Decisiones para el dueño

1. **Columnas: solo "Automático" y "2 columnas".** El kit no tiene un patrón de opciones para botones de reporte; se puso un `<select>`
   pequeño junto al botón. "3 columnas" sería lo mismo que Automático (que ya usa 3 si todo cabe), así que no se ofrece aparte. "2
   columnas" = como máximo 2 (si algo no cabe, baja a 1 con aviso). La elección no se guarda (vuelve a Automático al recargar).
2. **"(continuación)"**: si un grupo sigue en la página siguiente, su título se repite como *Grupo 01 (25) (continuación)*.
3. **Descripción del producto = su nombre** (`ProductListItemDto` no trae otra descripción), cortada a 2 renglones con "…".
4. **Primer número**: la clave numérica es el texto sin ceros a la izquierda (compara números de cualquier largo); el título del grupo es
   el texto del **primer código del grupo ya ordenado** (orden natural: entre `R1-X` y `R01-X-B` va primero `R1-X`, así que el grupo se
   titula "1"). Los números en medio del código (`A-B12`) cuentan: la primera secuencia de dígitos, esté donde esté.
5. **Grupos de productos por id de categoría** con la ruta de la categoría como título (si la ruta no está en la lista de categorías, el
   nombre que trae el producto). Orden de los grupos: alfabético natural por ruta; "Sin categoría" al final.
6. **Tope**: el mismo de Exportar y de los otros reportes, 10 000 (`EXPORT_MAX_ROWS`), con aviso de truncado. Medido: 10 000 posiciones
   cortas en 3 columnas = 418 páginas, 4.3 MB, ~3.6 s de armado en Node/jsdom (más la lectura de 50 páginas del API).
7. **Alto de barras 12 mm** (el mínimo pedido en el cambio de diseño, para que quepan 8 filas por hoja); módulo 0.33 mm que baja hasta
   0.25 mm según el ancho de la celda; un valor que ni en una columna cabe a 0.25 mm (más de ~62 caracteres en B) sale "No cabe". Con los
   largos de la base (SKU ≤ 60, posición ≤ 40) eso no debería pasar.
8. **Caracteres no admitidos**: solo ASCII 32–126 (sin FNC4 para Latin-1 extendido, que muchos lectores no interpretan igual). Un SKU o
   código con acento o ñ se omite y se lista.
9. **Permiso `inventory.view`** explícito con `<Can>` en ambos botones (es el de las rutas y de los otros reportes y Exportar de esas
   pantallas).
10. **Posiciones inactivas**: en Posiciones nunca salen (la pantalla no las muestra); en la pestaña de la ficha salen con "Incluir
    inactivas" y la marca *Inactiva*. Las provisionales llevan *Pendiente de revisión*.
11. **El filtro Almacén de Productos no cambia el reporte** (en la tabla solo acota cantidades); el recuadro de filtros lo muestra igual.
12. **Atribución de los commits**: se usó la línea `Co-Authored-By: Claude Opus 5.5` (el modelo que hizo el trabajo, según la indicación
    de atribución del entorno) en lugar de "Sonnet 5.5" que pedía el encargo.

## Pendientes

- **Backend**: nada indispensable. Útil a futuro: un parámetro de orden natural en `GET /products` y `/bins` (hoy el orden y la
  agrupación se hacen en el navegador sobre lo leído) y una lectura masiva más rápida que de a 200 para reportes de 10 000.
- Probar con un Zebra y papel impreso reales (solo se validó con zxing-cpp sobre imágenes del PDF).
- Si el dueño quiere guardar la elección de columnas por usuario, irá a localStorage como el filtro del Pulso.
