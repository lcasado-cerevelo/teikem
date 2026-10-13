# Lote F-G2 — Labels y documentos de las órdenes (2026-10-12)

Servidor: `docs/lote44-decisiones.md`. Motor de impresión: F-G1 (`docs/frontend/loteFG1-decisiones.md`). Manual:
`docs/manual/frontend/fg2-labels-y-documentos.md` y el capítulo 12 §10.

## Qué se construyó

- **Labels** (`features/printing/orderLabels.ts`, puro): de la lista o de la ficha a los labels, **uno por pieza** con el número del paquete de
  su línea y «Paquete i de N»; canceladas y piezas sin número se omiten con aviso; desde 500 se avisa (sin tope).
- **`usePrinting()`** (`features/printing/usePrinting.tsx`): imprime labels y documentos con la configuración de la estación (QZ Tray directo o el
  navegador), con la vista previa de «Confirmar antes» (Imprimir / Descargar PDF / Cancelar) y los avisos; un fallo nunca rompe la pantalla.
- **Lista de Órdenes**: columna **Elegir** (marcas que se recuerdan entre páginas) y, en el pie de la tabla como la maqueta, **Waybill summary**,
  **Packing list**, **Imprimir label** (marcadas) y **Todos los labels** (todo lo filtrado, `exportOrders`, sin tope).
- **Ficha**: **Imprimir label** en la cabecera.
- **«Label al guardar»**: en la leyenda de la entrada rápida y en el ticket de la detallada (con la nota de la maqueta). Lo imprime la lista (que
  sigue montada aunque el formulario se cierre), después del 201, con la ficha devuelta; un fallo avisa que la orden sí se guardó.
- **Documentos** (`features/orders/orderDocuments.ts`): Waybill summary (carta apaisada, por cliente, sin precio) y Packing list (carta). Kit:
  `ReportSpec.paper` ('a4' por omisión, 'letter').
- **Escaneo**: **Informe de faltantes** (`features/scan/missingReport.ts`) con todos los pendientes del filtro agrupados por zona; el resultado
  muestra «Encontrada por número de paquete.».
- Correcciones de textos que decían «no disponible»: `fc3-ordenes-rapida-importacion.md`, `fe1-escaneo.md`, `faq.md` y las notas diferidas de
  `loteFC1/FC2/FC3/FE1-decisiones.md`; fila del plan cerrada.

## Cómo se probó

- `tsc`, `oxlint` (0 errores), **vitest 2.658 en verde** (`--maxWorkers=3`; nuevas: `orderLabels`, `orderDocuments` —incluido el PDF en carta—,
  `missingReport`, la lista con marcas, «Imprimir label», «Todos los labels» con avisos, Waybill/Packing; «Label al guardar» en la rápida y la
  detallada; el informe de faltantes en Escaneo; pruebas ajustadas: columnas de la lista, botones de la ficha y del escaneo) y `vite build`.
- Playwright `e2e/fg2-labels.spec.ts` (escritorio y móvil a 360 px) contra el API real (:5102) y Vite (:5176) sobre la base nueva `Teikem_fg2`:
  marcar dos órdenes → **PDF de 4 páginas de 288×432 pt**, Todos los labels, Waybill summary **792×612** y Packing list **612×792**, ficha (3
  labels), entrada rápida con «Label al guardar», escaneo del número de paquete e informe de faltantes. Regresión: `fg1-impresoras`,
  `loteFC1`, `loteFC2`, `loteFC3`, `loteFE1` en verde (se actualizaron las aserciones que decían que no había labels ni informe).
- **Suite completa de Playwright** (`--workers=2`, todos los proyectos): **137 en verde, 0 fallas** (86 omitidas por proyecto, como siempre). En
  corridas anteriores fallaron una vez `f6` (aviso «Producto creado.» que no se alcanzó a ver; pasa al repetirlo) y `fg2` (cambié un texto a
  mitad de la corrida); en vitest, una vez la prueba del filtro «Pasillo» de Posiciones con la máquina cargada (pasa al repetirla). Ninguna es
  del lote.
- `scripts/smoke.sh` completo en verde sobre `Teikem_fg2` (SMOKE OK, 144 pasos).

## Defectos encontrados y arreglados

- En un teléfono (≤ 480 px) la leyenda de la entrada rápida estaba oculta y con ella «Label al guardar»: ahora solo se ocultan las teclas.
- «Imprimir 1 labels» → «Imprimir 1 label».

## Diferencias con la maqueta

- «Guardar e imprimir label» de la detallada son los dos botones de siempre (Guardar borrador / Guardar y confirmar) más el interruptor
  «Label al guardar» (la detallada ya tenía dos formas de guardar desde F-C1).
- Los botones del pie actúan sobre las **marcadas** (la maqueta no tenía casillas): sin marcas, los documentos y «Todos los labels» usan todo lo
  filtrado.

## Decisiones tomadas por default para presentar al dueño al final de P1

| | Decisión | Alternativa |
|---|---|---|
| D-FG13 (revisada) | «Label al guardar» viene encendido solo si la compañía tiene «Imprimir labels directo» y la estación no se excluyó; vale para borradores también | Encendido siempre, como la maqueta |
| D-FG14 | Waybill summary sin precio | Incluir «Cotizado» con `orders.view_price` |
| D-FG15 | Informe de faltantes agrupado por zona | Por cliente («Faltantes por cliente» de Kemi) |
| D-FG16 | Etiquetas de posición y de producto (F16/F17b) sin cambios | Precargar su tamaño de la impresora de la estación |
| D-FG17 (Luis) | Cantidad de la línea = número de labels; mismo número de paquete en cada pieza | Un número por pieza |
| D-FG29 | Documentos y «Todos los labels» sin marcas = todo lo filtrado; «Imprimir label» solo marcadas | Pedir confirmación del alcance cada vez |
| D-FG30 | El «Label al guardar» lo imprime la lista con la ficha devuelta por el alta (sin otra lectura), aunque el formulario se cierre | Imprimir desde el formulario |
