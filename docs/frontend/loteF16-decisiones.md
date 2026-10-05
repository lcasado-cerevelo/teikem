# Lote F16 — Etiquetas de posición en la web (2026-10-04)

Pedido del dueño: *"Una manera de imprimir o reimprimir los labels de las posiciones, que me deje imprimir varios tamaños: 4x2, 4x4, 4x6,
que se ajuste. Un label por posición, con su barcode. En la pantalla de las posiciones, y que se filtre según el filtro de la misma
pantalla."* Solo se tocó `web-app/` y `docs/`: **el servidor no cambió** (se usa `GET /api/v1/warehouses/{id}/bins`, que ya acepta
`binIds`; `npm run api:types` deja `schema.d.ts` igual). Manual de pantallas:
[f16-etiquetas-de-posicion.md](../manual/frontend/f16-etiquetas-de-posicion.md); FAQ: sección "Lote F16" de [faq.md](../manual/faq.md).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Etiqueta (kernel) | PDF vectorial con UNA página por posición del **tamaño exacto** de la etiqueta: 4×2 = 288×144 pt (apaisada), 4×4 = 288×288, 4×6 = 288×432 (vertical); margen interno de seguridad `BIN_LABEL_SAFE_MARGIN_IN` = 0.1 in. Arriba el Code 128 del código exacto, horizontal, a todo el ancho útil con su zona de silencio (módulo entre `BARCODE_MIN_MODULE_MM` = 0.25 mm y `BIN_LABEL_MAX_MODULE_MM` = 1 mm) y con todo el alto que quede (≥ 8 mm); debajo el código en letra grande ajustada al ancho (`fitFontSize`, mínimo 10 pt, 2 renglones si ni así cabe); abajo "Almacén · Zona · Pasillo · Rack · Nivel · Posición" (solo lo que tiene valor, 2 renglones). Orientación `rotate` = `/Rotate 90` en cada página (misma MediaBox). "No cabe" / "Sin código de barras" en lugar de las barras + avisos `barcodeEndNotices`. Reúsa `code128.ts`, `sheetModuleWidth` y `fitFontSize` (`binSheetPdf.ts`), `clampLines`, `PT_PER_MM`, `barcodeEndNotices` (`barcodeReportPdf.ts`), `reportFileName` y `pdfSafeText`; no duplica el codificador | `kernel/ui/binLabelPdf.ts` |
| Flujo | Alcances (filtro actual = la consulta de la tabla tal cual, con todos sus filtros y el buscador; marcadas = tandas de 200 `binIds`); lectura de a 200 con `skip`; tope 500 (`tooMany` sin generar); orden natural; datos de la etiqueta (código del almacén + los de `sheetDetails`); descarga. **No marca nada** | `features/warehouse/binLabels.ts` |
| Pantalla | Botón **Etiquetas de posición** junto a **Hojas de posición** (`<Can perm="inventory.view">`, el del reporte de códigos de barras de posiciones); modal con qué imprimir, tamaño (silueta en su proporción, "4 × 2 pulgadas (10 × 5 cm)", forma), orientación, progreso, cancelar y avisos | `BinLabelsPanel.tsx`, `LocationsScreen.tsx`, `warehouse.css` (`.bl-*`) |
| API | `fetchWarehouseBins(publicId, query, signal?)` (cliente generado) | `features/warehouse/api.ts` |
| Textos | `warehouse.binLabels.*` (es/en); el PDF reúsa `ui.binSheet.noCode` y `ui.barcodeReport.*` | `kernel/i18n/{es,en}.json` |
| Contrato | Fila de `binLabelPdf.ts` y piezas de Ubicaciones del F16 | `web-app/KIT.md` |
| Recorrido | `e2e/loteF16.spec.ts` + proyectos `escritorio-f16`/`movil-f16` (después de F15, antes de F9; F9 depende de ellos) | `playwright.config.ts` |

## Cómo se probó (resultados reales de esta sesión)

| Comando | Resultado |
|---|---|
| `npm run check` en `web-app/` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: tipos generados sin cambios, tsc, oxlint (9 avisos que ya existían; ninguno en archivos de este lote), vitest **123 archivos / 1250 pruebas** (3 archivos nuevos: 17 + 10 + 6 pruebas), build |
| `binLabelPdf.test.ts` (17): páginas exactas 288×144 / 288×288 / 288×432 y `rotate` = `/Rotate 90`; margen 7.2 pt; módulo (tope 1 mm, llena el ancho exacto con la zona de silencio, frontera de 0.25 mm → "No cabe"); no admitidos ("Ñ", vacío); plan y avisos con los textos de F14; acomodo dentro del margen, centrado, sin encimarse y barras ≥ 8 mm en los 3 tamaños; crece de 4×2 a 4×6; letra que llena el ancho, máximo por tamaño, 2 renglones a 10 pt; datos y nombre del archivo; documento: N páginas con su MediaBox (3 tamaños), girada con el mismo contenido, número exacto de barras por página, texto del código y datos, todo en negro, etiquetas sin barras con su motivo | **pasaron** |
| `binLabels.test.ts` (10): permiso; alcances (filtro tal cual con zona/estatus/producto/hoja/buscador, marcadas en 200/200/50 ordenadas y sin repetir); datos (almacén + los que tienen valor); tamaño/orientación guardados; flujo: 431 posiciones = 3 lecturas (skip 0/200/400, take 200) con los filtros, orden natural (A-1, A-2, …, A-10), una descarga y las dependencias sin "marcar impresas"; marcadas en 2 lecturas; 501 = `tooMany` tras 1 lectura y 500 sí; nada; cancelar; errores de lectura y del PDF; avisos de las sin código y el texto final | **pasaron** |
| `LocationsLabels.test.tsx` (6): botón junto a "Hojas de posición" y modal (alcances, 3 tamaños con cm, forma, orientación, 4×2 y Automática por defecto); filtro con el buscador → lectura `take=200&skip=0&search=…&includeInactive=false`, PDF 4×6 girado en orden natural con los datos, ninguna llamada a hojas ni POST, tamaño/orientación recordados; marcadas → `binIds=10&binIds=12` y las marcas se quedan; 501 → aviso y botón deshabilitado; "AÑO-1" → el modal se queda con la lista y "Listo"; sin `inventory.view` no hay botón | **pasaron** |
| `npx playwright test e2e/loteF16.spec.ts --project=escritorio-f16 --project=movil-f16 --no-deps --workers=1` (API real en :5000, Release, `Auth__Onboarding__Enabled=false`, base recreada con `db-reset --yes` + `db-init` + smoke; Vite con `VITE_API_URL=http://localhost:5000`; Chromium de `/opt/pw-browsers`) | **3 pasaron** (2 de escritorio, 1 de móvil a 360 px), 3 omitidos por proyecto |
| Suite completa en el orden del CI: `db-reset --yes` + `db-init` → API → `scripts/smoke.sh` (con `SMOKE_SQL` local y `SMOKE_MIGRATION_RUN`: **SMOKE OK**) → `npx playwright test --workers=1` | **87 pasaron, 80 omitidos por proyecto, 0 fallaron** (8.4 min). Las capturas de otros lotes que regeneró la corrida se descartaron con `git checkout --`; solo quedan las `f16-*` |

Recorrido `e2e/loteF16.spec.ts`: siembra por API tres posiciones vacías en RSV de ALM-01 (creadas fuera de orden; una con pasillo, rack
y nivel). Escritorio: filtro **Posición** = sufijo → modal con *Las posiciones del filtro actual (3)* → 4×2, 4×4 y 4×6: nombre
`etiquetas-de-posicion-<tamaño>-advance-logistics-AAAA-MM-DD.pdf`, PDF válido con **3 páginas** (`/Type /Page`), **las 3 MediaBox =
[0 0 288 144] / [0 0 288 288] / [0 0 288 432]**, sin `/Rotate`, los códigos en orden natural (-01, -02, -10) y los datos
("Almacén ALM-01 · Zona RSV · Pasillo 01 · Rack 02 · Nivel 3"); 4×2 con **Girar 90°** = 3 `/Rotate 90` y la misma MediaBox; una
reimpresión igual; durante todo eso la web **solo hizo GET** y nunca a `bin-sheets`, y el estado de la hoja de las posiciones quedó igual.
Segundo caso: marcar una casilla → *Las posiciones marcadas (1)* → 4×4 de 1 página; la marca se queda. Móvil (360 px): sin scroll
horizontal en la lista y en el modal, PDF 4×6 de 3 páginas 288×432. Capturas `f16-ubicaciones.png`, `f16-modal.png`,
`f16-movil-modal.png` (de la corrida completa); imágenes del PDF `f16-pdf-4x2.png`, `f16-pdf-4x4.png`, `f16-pdf-4x6.png`,
`f16-pdf-4x2-girada.png` (PDF reales de una corrida anterior del recorrido, guardados con `F16_PDF_DIR`, por eso su sufijo no coincide con
el de las capturas) y `f16-pdf-no-cabe.png` (muestra con datos de prueba).

### Validación de los códigos del PDF (fuera del repo)

En el entorno virtual de Python del scratchpad (no es dependencia del repo) con **zxing-cpp** y Pillow, y `pdftoppm -gray` para pasar cada
página a imagen; el valor esperado de cada página es el código en texto de la etiqueta (`pdftotext`):

1. **PDF reales del recorrido** (14 archivos: 4×2, 4×4, 4×6, 4×2 girada, reimpresa, marcadas y móvil 4×6, de dos corridas): **108/108**
   códigos leídos con su valor exacto a **300, 203 y 150 dpi** (36 etiquetas × 3 resoluciones), ningún código de más.
2. **Muestra de prueba** (37 etiquetas por PDF en los 3 tamaños × Automática/Girar 90°: códigos de 1 a 34 caracteres, solo dígitos, uno
   con "Ñ" y los que no caben): a **300 y 203 dpi 32/32** en los 6 PDF (las otras 5 son las 4 "No cabe" y la de "Ñ", sin barras, y no se
   leyó ningún código en ellas). A **150 dpi 27/32**: no se leen los códigos de **24 y de 27 a 30 caracteres**, cuyo módulo queda entre
   0.25 y 0.30 mm = menos de 2 píxeles a 150 dpi (límite físico de esa resolución, no del dibujo). Las impresoras térmicas de etiquetas
   son de 203 o 300 dpi; con esas, todo se leyó.

**No se probó con un Zebra físico ni con papel de etiquetas real.**

## Decisiones para el dueño (valor más seguro)

1. **Página = etiqueta.** Cada etiqueta es una página del tamaño exacto (sin márgenes de página); el driver debe tener configurado el
   mismo tamaño e imprimir al 100 %. Nada se dibuja a menos de 0.1 in del borde.
2. **Orientación "Girar 90°" sí se hizo** (era simple y seguro): agrega `/Rotate 90` al diccionario de cada página con el evento
   `putPage` de jsPDF; la MediaBox y el contenido no cambian (comprobado: mismo texto por página que sin girar; pdfinfo dice `rot: 90`).
   Es para impresoras/drivers que alimentan la etiqueta de lado. Automática es la recomendada.
3. **Código de barras siempre horizontal** y a todo el ancho; en 4×6 las barras quedan muy altas (más de 10 cm): es lo "más grande
   posible" sin girarlas. El módulo se tope a **1 mm** (el mismo máximo de los productos de la hoja de posición): un código de 1 a 4
   caracteres no llena el ancho y queda centrado.
4. **Todo en negro puro** (también los datos chicos y el motivo "No cabe"): la térmica no tiene grises y un gris sale tramado.
5. **"No cabe" / no admitido**: la etiqueta sale igual con el código en texto y el motivo en el lugar de las barras; la lista va **al
   final del proceso en el modal** (que se queda abierto con "Listo"), no como página extra del PDF (sería una etiqueta de más en el rollo).
   Los textos son los del reporte de códigos de barras (F14); el de "no caben" dice "en el ancho de la hoja", aceptable para la etiqueta.
6. **Sin estado ni "marcar impresas"**: las etiquetas identifican la posición y no cambian con su contenido. El recorrido comprueba que
   generar etiquetas solo hace GET del listado y que el estado de la hoja (F15) de las posiciones no cambia. Las **marcas de las casillas
   se quedan** al imprimir etiquetas (en F15 se quitan): así se puede reimprimir el mismo grupo.
7. **Tamaño y orientación se recuerdan por navegador** (localStorage), por defecto 4×2 y Automática.
8. **Datos de la etiqueta**: código del almacén (no el nombre, para que quepa) + zona, pasillo, rack, nivel y posición con valor. Las
   inactivas no salen (el listado de la pantalla no las trae).
9. **Tope de 500 etiquetas por PDF** (como las hojas). Hoja carta con varias etiquetas: no se pidió y no se hizo.
10. **Atribución del commit**: el encargo pedía `Co-Authored-By: Claude Sonnet 5.5`; se usó `Claude Opus 5.5`, el modelo que hizo el
    trabajo según la indicación de atribución del entorno (igual que F14).

## Pendientes

- Probar con un Zebra (203 y 300 dpi) y rollos reales de 4×2, 4×4 y 4×6, con y sin "Girar 90°".
- Ver el CI de GitHub Actions (no se hizo push).
- Si el dueño quiere etiquetas desde la ficha del almacén (pestaña Posiciones) o desde la app, es un lote aparte.
