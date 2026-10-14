# Lote F-B3 — Importar consignatarios: pueblo, estado y ZIP con el catálogo de códigos postales, y variantes de mapeo (2026-10-14)

Mejoras de la pantalla **Catálogo → Consignatarios** (lote F-B). Servidor: [lote46-decisiones.md](../lote46-decisiones.md). Manual de pantallas:
[fb3-variantes-y-codigos-postales.md](../manual/frontend/fb3-variantes-y-codigos-postales.md); FAQ «Lote 46 / F-B3».

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/features/…`) |
|---|---|---|
| Editor con variantes | Con una sola variante, el editor de siempre más «¿El archivo mezcla filas…? **+ Agregar variante**». Con dos o más: pestañas **Variante N[: nombre]** (marca «· con errores»), **Nombre de la variante**, **Se usa cuando** (columna del archivo con su encabezado, operador «es igual a / contiene / no está vacía / está vacía», valor solo cuando aplica, hasta 5), **Mover antes**, **Quitar variante** y el mapeo y las uniones propios de cada una. La nueva variante copia el mapeo de la abierta y trae una condición por llenar. Valores por defecto comunes (ayuda propia). | `consignees/ConsigneeTemplateEditorModal.tsx` (`VariantPanel`, `MappingRow` con prefijo) |
| Reglas puras | Valores: la variante 1 en los campos de siempre (`columns`, `variantName`, `conditions`) y las demás en `moreVariants` (así el request de una plantilla de una sola variante no cambia); `variantsOf`/`withVariants`/`variantPath`; `variantIssues` (espejo de `ConsigneeImportVariants.Validate`) y `variantsWithIssues`; `consigneeTemplateVariants` y los builders (`variants` sin `columns`; `variants: []` al volver a una sola); regla «pueblo **o** ZIP»; `previewFields` (columnas deducidas), `templateUsesField`, `templateFields` con variantes. | `consignees/consigneeRules.ts` |
| Vista previa | Resumen «Mapeo en N variantes…» con condición y columnas de cada una; columna **Variante**; celdas deducidas con **del catálogo**; ZIP ajustado con el original tachado → nuevo y el chip **Código postal ajustado**; la fila sin variante muestra «Celdas de la fila: …»; pueblo/estado/ZIP aparecen como columnas si alguna fila los dedujo. | `consignees/ConsigneeImportModal.tsx` |
| Resultado | Tabla «Consignatarios con pueblo, estado o ZIP completados o ajustados…: N» (fila, nombre, ZIP, avisos). | ídem |
| Alta manual | El selector **Pueblo** del consignatario pide `zipClass=DELIVERY` (sin apartados ni militares). `usePostalLocalities` y `PostalLocalityPicker(Input)` aceptan `zipClass` opcional (el almacén no lo pasa: sigue con todos). | `consignees/ConsigneeFormModal.tsx`, `warehouse/PostalLocalityPicker.tsx`, `warehouse/api.ts` |
| Textos | `consignees.import.*` nuevos (variantes, condiciones, operadores, marcas del catálogo, resultado) y cambiados (`requiredHelp`, `errors.city`) en es/en. | `kernel/i18n` |
| Estilos | Pestañas, condiciones (rejilla que se apila a 360 px), marcas del catálogo. | `consignees/consignees.css` |
| Contrato | `openapi.json` y `schema.d.ts` de la web y de la app regenerados (fusión de los nodos nuevos). | — |

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `tsc -b`, `oxlint src e2e` (0 errores; avisos del mismo tipo que los existentes) | pasan |
| vitest solo del área (regla de Luis): `features/consignees` + `warehouseAdmin.test.tsx` | **107 en verde** (consignees 92: rules 38, importación 28, lista 22, selector 4; warehouse 15) |
| `consigneeRules.test.ts` (6 nuevas) | pueblo o ZIP, plantilla con variantes → editor (la 1 arriba, uniones por variante), requests con `variants` y `variants: []`, reglas de variantes con sus rutas, columnas de la vista previa, textos es/en |
| `ConsigneeImport.test.tsx` (4 nuevas) | crear dos variantes con condición, error de la variante sin condición y **Mover antes**, cuerpo exacto; condición sin columna / sin valor y «no está vacía»; editar una plantilla con variantes y quitar una (PATCH con `variants: []`); vista previa con Variante, «del catálogo», ZIP ajustado, celdas de la fila sin variante y la tabla del resultado |
| Playwright `e2e/loteFB3-variantes-zip.spec.ts` (escritorio y móvil 360 px, `--workers=2`, API propio :5146 y Vite :5179 sobre la base desechable `Teikem_fb3` con el script de ZIP corrido) | **2 en verde**: CSV con dos formatos, editor con el archivo (variante B con su condición y mapeo, A con «está vacía»), vista previa (Toa Baja, 00956, 00734 → 00715, pueblo desconocido, fila sin variante), importar, resultado, lo guardado por API y el selector del alta sin 00732-00734. Capturas `fb3-*` |
| Regresión Playwright `e2e/loteFB2-importacion.spec.ts` | **2 en verde** (sus capturas no se cambiaron en este lote) |

## Decisiones

1. **La variante 1 vive en los campos de siempre** del formulario y las demás en `moreVariants`: las plantillas de una sola variante mandan exactamente el mismo
   cuerpo que antes y todo lo del Lote F-B2 sigue igual.
2. **Una pestaña a la vez**: las etiquetas «Columna N» siguen siendo únicas en la pantalla (las pruebas y los lectores de pantalla las encuentran igual).
3. **Agregar variante copia el mapeo de la abierta** (los formatos suelen compartir columnas) y trae una condición vacía; la 1 no recibe condición automática: la
   validación pide moverla o ponérsela.
4. **Mover antes** (no arrastrar) para cambiar el orden: suficiente con pocas variantes y funciona igual en el teléfono.
5. **Condiciones solo con dos o más variantes**; al quitar hasta quedar una, se borran sus condiciones.
6. **Marca «del catálogo»** en el valor y el ZIP ajustado tachado con chip, en vez de colores solos (se lee igual en móvil y sin color).
7. **Alta manual con `DELIVERY`**: coherente con la regla de la importación; el almacén y las direcciones postales siguen con todos los tipos.

## Defectos encontrados y arreglados

- Ninguno anterior. En las pruebas: el catálogo de campos simulado no tenía `postalCode` (el editor no podía mostrarlo) y se agregó.
