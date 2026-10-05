# Lote F15 — Hojas de posición en la web (2026-10-03)

Pedido del dueño del 2026-10-03: imprimir, para pegar en cada posición del rack, una hoja con la lista de productos de esa posición y el
código de barras de cada uno, para escanearlo con el Zebra (los productos no tienen código visible o están a 20–30 pies de altura); poder
imprimir **las posiciones que él quiera**, no todas; y saber qué hojas pegadas ya no sirven con un aviso **acumulado** (insignia +
contador), **sin ventanas emergentes** en cada movimiento. El servidor ya estaba (Lote 23, `docs/lote23-decisiones.md`, manual 06 §1.5).
Solo se tocó `web-app/` y `docs/` (nada de `src/` ni de `app-almacen/`). Manual de pantallas:
[f15-hojas-de-posicion.md](../manual/frontend/f15-hojas-de-posicion.md); FAQ: sección "Lote F15" de [faq.md](../manual/faq.md).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Hoja de posición (kernel) | PDF vectorial carta vertical: una posición por hoja (siempre página nueva); encabezado con el código grande, su Code 128 y "Zona · Pasillo · Rack · Nivel · Posición" (lo que exista); hasta 10 productos por hoja (más → hojas de 10 con "Hoja 2 de 2"); cada producto = SKU grande, nombre recortado con "..." y el Code 128 del código de barras del producto (o del SKU); tamaño adaptable (el cuerpo se reparte entre los N productos, con alto mínimo/máximo por bloque y tres acomodos: `stacked`, `side`, `wide`); "Sin productos" con el interruptor; pie "Impresa el …" + almacén; aviso al pie de la hoja para productos sin código. Reúsa `code128.ts` (codificador), `BARCODE_MIN_MODULE_MM`, `PT_PER_MM`, `barcodeEndNotices`, `clampLines` (ahora exportada) de `barcodeReportPdf.ts` y `REPORT_LAYOUT`, `REPORT_COLORS`, `drawReportNotice`, `reportFileName` de `reportPdf.ts` | `kernel/ui/binSheetPdf.ts` |
| Flujo de impresión | Alcances (filtro actual, marcadas en tandas de 200 `binIds`, desactualizadas = filtros sin "Hoja" + STALE/NEVER_PRINTED); lectura de `GET .../bin-sheets` de a 200 con `skip`; tope de 500 posiciones (`tooMany` sin generar); orden natural por código; descarga; SOLO después `POST .../mark-printed` con las posiciones que salieron en el PDF y el `generatedAtUtc` más antiguo de las tandas; cancelar o un error al leer/generar no marca nada | `features/warehouse/binSheets.ts` |
| Pantalla | Casilla **Elegir** por fila + "Seleccionar todas las de la página" (indeterminada) + "N marcadas" + "Quitar marcas"; columna y filtro **Hoja** (insignia con texto y color + "Impresa <fecha y hora>"; `title` con el último cambio); aviso acumulado con `staleCount` + "Imprimir las desactualizadas"; botón **Hojas de posición** junto a **Códigos de barras**; modal con qué imprimir, "Incluir posiciones vacías", progreso y cancelar | `LocationsScreen.tsx`, `BinSheetsPanel.tsx`, `locations.ts` (`sheetStatus`), `warehouse.css` |
| API | `fetchBinSheets`, `markBinSheetsPrinted` y los tipos `BinSheetPageDto`, `BinSheetDto`, `BinSheetStateDto`, `BinSheetQuery` (del cliente generado; el contrato **no cambió**: `npm run api:types` deja `schema.d.ts` igual) | `features/warehouse/api.ts` |
| Textos | `ui.binSheet.*` (PDF) y `warehouse.binSheets.*` (pantalla), es/en | `kernel/i18n/{es,en}.json` |
| Contrato | `KIT.md`: fila de `binSheetPdf.ts` (componentes UI), `clampLines`, y las piezas de Ubicaciones del Lote F15 | `web-app/KIT.md` |
| Recorrido | `e2e/loteF15.spec.ts` + proyectos `escritorio-f15`/`movil-f15` (después de F14, antes de F9) | `playwright.config.ts` |

## Cómo se probó (resultados reales de esta sesión)

| Comando | Resultado |
|---|---|
| `npm run check` en `web-app/` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: tipos generados sin cambios, tsc, oxlint (9 avisos que ya existían; ninguno en archivos de este lote), vitest **120 archivos / 1217 pruebas** (3 archivos nuevos con 15 + 13 + 7 pruebas, y 2 expectativas más en `locations.test.ts`), build |
| `binSheetPdf.test.ts` (15): módulo (1 mm / llena / null bajo 0.25 mm), elección del código (código de barras → SKU si falta, si no se admite o si no cabe → "No cabe" → solo texto), código de la posición (≤ 0.5 mm; no admitido), una posición por hoja y partes de 10 (1, 11 → 10+1, 20 → 10+10, 21 → 10+10+1, 10), vacías omitidas/incluidas, avisos por hoja, cuerpo con y sin aviso, reparto (1 = máximo, 2–5 > 1.8 × el de 10, nunca bajo el mínimo y siempre cabe con 0/1/4 renglones de aviso), acomodo por N (`stacked` ×2, `side` ×8; módulo y barras en sus límites; texto ≥ 180 pt; tamaños no crecen al subir N), `wide` y sin símbolo, documento (3 hojas carta 612×792, "Hoja 1 de 2"/"Hoja 2 de 2", detalle, "...", "Impresa el 10/03/2026 2:05 p. m.", almacén, nombre de archivo, "Sin productos", aviso al pie, número exacto de barras dibujadas) | **pasaron** |
| `binSheets.test.ts` (13): estado (4 códigos, desconocido), alcances (filtro, desactualizadas sin "Hoja", marcadas en 200/200/50 ordenadas y sin repetir), detalle y productos, `earliestUtc`; flujo: 431 posiciones = 3 lecturas (0/200/400 de 200), orden natural, descarga ANTES de marcar, una sola marca con las 431 y el `generatedAtUtc` de la 1.ª tanda; vacías no se marcan (con el interruptor sí; todo vacío = "nada" sin PDF); >10 productos = varias hojas y una marca; >500 = `tooMany` tras una lectura; marcadas en 2 lecturas; PDF que falla / lectura que falla / cancelar (y fetch abortado) no marcan; marca que falla = `markFailed`; texto del aviso final | **pasaron** |
| `LocationsSheets.test.tsx` (7): columna Hoja (insignias con texto, "—" con `aria-label`, "Impresa 10/01/2026 9:30 a. m.", `title`), aviso con `staleCount` y "Imprimir las desactualizadas" abre el modal con ese alcance, filtro Hoja (`sheetStatus=STALE`; el aviso se cuenta aparte con `take=1` sin él), casillas (una, página, indeterminada, contador, quitar; con marcas abre en "marcadas"), imprimir marcadas (lectura con `binIds` y `take=200`, PDF antes de marcar, cuerpo `{ binIds, generatedAtUtc }`, lista "Al día", marcas quitadas), PDF que falla no marca, vacías sin/con el interruptor, sin `inventory.view` no hay botones | **pasaron** |
| `locations.test.ts` (+2 expectativas de `sheetStatus`), `warehouseScreens.test.tsx`, `BinCapacityModal.test.tsx`, `BarcodeReportButtons.test.tsx` (Ubicaciones existentes) | **pasaron** sin cambios de comportamiento |
| `npx playwright test --project=escritorio-f15 --project=movil-f15 --no-deps --workers=1` (API real en :5000, Release, `Auth__Onboarding__Enabled=false`, base recreada con `db-reset --yes` + `db-init`; Vite con `VITE_API_URL=http://localhost:5000`; Chromium de `/opt/pw-browsers`) | **3 pasaron** (2 de escritorio, 1 de móvil a 360 px), 3 omitidos por proyecto |
| Suite completa `npx playwright test --workers=1` (porque cambió `playwright.config.ts`), con el orden del CI: `db-reset --yes` + `db-init` → `scripts/smoke.sh` (con `SMOKE_SQL` local y `SMOKE_MIGRATION_RUN`: **SMOKE OK**) → Playwright | **84 pasaron, 77 omitidos por proyecto, 0 fallaron** (7.9 min), incluidos los de Ubicaciones del F14. Una primera corrida SIN el smoke antes falló en `lote15.spec.ts` 6 ("Valor de inventario por categoría" sin total: con la base recién creada todo el inventario vale $0.00); es una dependencia previa de ese recorrido con los datos del smoke (el CI lo corre antes), no de este lote. Las capturas de otros lotes que regeneró la corrida se descartaron con `git checkout --`; solo quedan las `f15-*` |

Recorrido `e2e/loteF15.spec.ts`: siembra por API un producto con código de barras de 13 dígitos y dos posiciones en RSV de ALM-01, y un
ajuste +5 en la primera. Pantalla: "Sin hoja impresa" y "—", aviso "1 posición…", modal con "filtro actual (2)", descarga
`hojas-de-posicion-advance-logistics-AAAA-MM-DD.pdf` válido (`%PDF-`/`%%EOF`) con la posición, el SKU, el código de barras, "Impresa el" y
"Zona RSV" (la vacía no sale) → "Al día" con su fecha, aviso "Todas … al día" y el API dice `CURRENT`/`EMPTY`. Transferencia de todo por
API → "Desactualizada" (vacía) y "Sin hoja impresa", aviso "2 posiciones…", ningún diálogo abierto → "Imprimir las desactualizadas" con
"Incluir posiciones vacías" → PDF con "Sin productos" y la otra → "—" y "Al día" (API `EMPTY`/`CURRENT`). Segundo caso de escritorio:
marcar una casilla e imprimir solo esa. Móvil: el mismo recorrido en tarjetas, sin scroll horizontal (lista y modal). Capturas
`f15-ubicaciones-sin-hoja.png`, `f15-modal.png`, `f15-al-dia.png`, `f15-desactualizada.png`, `f15-movil-desactualizada.png`; imágenes del PDF
`f15-pdf-hoja.png` (PDF real del recorrido), `f15-pdf-diez-productos.png` y `f15-pdf-hoja-2-de-2.png` (muestra con datos de prueba).

### Validación de los códigos del PDF (fuera del repo)

Como en el Lote F14, en un entorno virtual de Python en el scratchpad (no es dependencia del repo) con **zxing-cpp** y Pillow, y
`pdftoppm` para pasar el PDF a imagen en gris:

1. **Muestra de 11 hojas** (posiciones con 1, 2, 3, 4, 5, 6, 10 y 11 productos —la de 11 en dos hojas—, una vacía, y una con un SKU de 45
   letras en acomodo `wide`, un producto con "Ñ" y 5 más; códigos de barras de 12 dígitos y SKU): **59/59** códigos esperados (el de cada
   posición y el de cada producto con código) leídos con su valor exacto a **600, 300, 203 y 150 dpi**, y ningún código de más.
2. **PDF reales del recorrido** (8 archivos de escritorio y móvil): en cada hoja se leyeron exactamente el código de la posición y el código
   de barras de 13 dígitos del producto (la hoja "Sin productos", solo el de la posición) a **300, 203 y 150 dpi**.
**No se probó con un Zebra físico ni con papel impreso.**

## Decisiones para el dueño (valor más seguro)

1. **Sin la banda de marca en la hoja.** La hoja va pegada en el rack: el encabezado es el código de la posición (lo más grande posible),
   su código de barras y el detalle; la compañía y el almacén van en el pie con "Impresa el …". Ahorra tinta y deja más alto para los
   productos.
2. **Código de cada producto: su código de barras; si no tiene, el SKU** (pedido). También se usa el SKU si el código de barras tiene
   caracteres que Code 128 no admite o es demasiado largo para la hoja. Diferente del reporte F14, que siempre codifica el SKU.
3. **Tamaños** (constantes con nombre en `binSheetPdf.ts`): bloque entre 44 y 480 pt; 1–2 productos con el código debajo del texto a todo
   el ancho (SKU hasta 64 pt, barras hasta 60 mm); 3–10 con el código a la derecha (texto ≥ 180 pt, barras hasta 30 mm, para que con menos
   productos nunca salga más chico que con más); módulo de 0.25 mm (el mínimo del F14) hasta 1 mm; barras de al menos 8 mm. El código de la
   posición: módulo ≤ 0.5 mm, barras de 12 mm. Con 10 productos de SKU corto el código sale de ~12 mm de alto y ~0.8 mm de módulo.
4. **Avisos al pie de la hoja**, no en una página final: cada hoja es de una sola posición, así que los productos sin código se avisan en
   la hoja donde están (hasta 4 renglones) y en el aviso de la pantalla ("J producto(s) sin código de barras legible…").
5. **"Impresa el …" = la hora de los datos del servidor** (`generatedAtUtc` de la lectura más antigua), la misma que queda guardada como
   última impresión: el papel y la insignia dicen la misma fecha.
6. **Marcar impresas: una sola llamada (todo o nada) con el `generatedAtUtc` más antiguo de las tandas**. Es conservador: si una posición
   de la 2.ª o 3.ª tanda cambió entre la 1.ª lectura y la suya, queda "Desactualizada" aunque su hoja sí traiga el cambio (pide reimprimir
   de más, nunca de menos). Por eso el tope es **500 posiciones por impresión** (= el máximo de "marcar impresas"): nunca quedan marcas a
   medias.
7. **Posiciones vacías apagadas por defecto también en "Imprimir las desactualizadas"** (pedido: por defecto se omiten). Consecuencia: una
   posición que se vació después de imprimir sigue "Desactualizada" hasta imprimirla con "Incluir posiciones vacías" (su hoja "Sin
   productos"). Si el dueño prefiere, ese botón podría encender el interruptor solo.
8. **El contador cuenta con los filtros de la tabla menos el filtro "Hoja"** (si no, filtrando "Al día" diría 0). Sin filtros es todo el
   almacén; con filtros dice "(con los filtros actuales)". Es el mismo subconjunto que imprime "Imprimir las desactualizadas".
9. **Marcas**: sobreviven a cambiar de página o de filtro dentro del mismo almacén; se pierden al cambiar de almacén; se quitan solas al
   imprimir "las marcadas". No hay casilla en el encabezado de la tabla (el encabezado de `DataTable` es texto y en tarjetas no existe):
   "Seleccionar todas las de la página" va en la franja de arriba, que funciona igual en el celular.
10. **Cancelar** solo mientras se leen las posiciones; mientras se arma el PDF y se marca no se puede (el PDF ya se está descargando).
    Si la marca falla después de descargar, el PDF queda en el equipo sin marcar: el modal lo dice y pide generarlo de nuevo.
11. **Orden por código natural** (A-2 antes que A-10), como el F14; el servidor ordena ordinal y la pantalla reordena lo leído.
12. **Columna "Hoja" al final** de la tabla y **"Elegir" al principio** (en tarjetas, "Elegir: [casilla]" como un renglón). La posición vacía
    sin hoja vieja se ve "—" (con `aria-label` "Sin productos: no necesita hoja"), como pide el dueño.
13. **Atribución de los commits**: se usó `Co-Authored-By: Claude Opus 5.5` (el modelo que hizo el trabajo, según la indicación de
    atribución del entorno; igual que en el F14) en lugar de "Sonnet 5.5" que pedía el encargo.

## Pendientes

- Probar con un Zebra y papel impreso reales (solo se validó con zxing-cpp sobre imágenes del PDF).
- App de almacén: ver/avisar la hoja desactualizada (lote de la app).
- Ver el CI de GitHub Actions (no se hizo push).
- Si el dueño quiere quién imprimió cada hoja: evento de actividad en el servidor (hoy la marca no se audita, Lote 23 decisión 9).
