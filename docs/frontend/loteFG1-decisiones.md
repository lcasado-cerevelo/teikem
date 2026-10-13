# Lote F-G1 — Impresoras y labels (pantalla, motor del label e impresión directa con QZ Tray) (2026-10-12)

Servidor: `docs/lote43-decisiones.md`. Manual: `docs/manual/frontend/fg1-impresoras.md` (pantallas, con capturas) y `docs/manual/12-impresoras-y-labels.md`
(reglas, mensajes y la sección de soporte de QZ Tray).

## Qué se construyó

- **Sistema → Impresoras y labels** (`/system/printers`, `printers.manage` + SYSTEM; deja de ser pendiente). Igual a la maqueta `printers()`:
  título y subtítulo, nota «cada estación de trabajo guarda su propia configuración», los dos paneles de la estación lado a lado (labels y
  documentos: impresora por defecto, template o formato, «Imprimir al guardar», «Confirmar antes», Prueba y Guardar) y «Construir el label»
  (tabla Mostrar · Elemento · Posición · Tamaño · Como código de barras con los 9 elementos, vista previa al momento, «Actualizar construcción
  del label»). Agregados que la maqueta no tiene y hacen falta: panel **«Esta computadora — impresión directa»** (estado de QZ Tray, **Detectar
  impresoras de esta computadora**, **Instalar QZ Tray**), selector de plantillas con Nueva/Duplicar/Hacer predeterminada/Desactivar, «Ajustes de
  la compañía» con **«Imprimir labels directo a la impresora»** y la tabla **Impresoras de la compañía** con su modal.
- **Motor del label** (`kernel/ui/orderLabelLayout.ts`, puro, y `orderLabelPdf.ts`): plan por renglón y celda, códigos de barras a todo el ancho
  (el del **número del paquete** con «Paquete i de N»), COD en caja negra solo si > 0, PDF vectorial de una página del tamaño exacto por paquete,
  achicar si no cabe y aviso si ni así. La vista previa HTML pinta el mismo plan.
- **Imprimir** (`kernel/ui/printPdf.ts`, `qzTray.ts`): directo con QZ Tray (interfaz `QzClient`, cliente real sobre la librería oficial servida
  de `/vendor/qz-tray.js`, certificado y firma del servidor) y respaldo con el diálogo del navegador (iframe oculto) o descarga en teléfonos,
  con el motivo del respaldo en el aviso.
- **Estación** (`features/printing/stationConfig.ts`): localStorage `teikem.print.station.v1.{tenantId}`, resolución estación → compañía →
  integrado con aviso de impresora o plantilla inactiva.
- **Cliente**: interruptor «Imprimir la factura automáticamente» en el Perfil del cliente (solo la preferencia).
- Kit: `IconPrint`, y las piezas de arriba anotadas en `web-app/KIT.md`.

## Cómo se probó

- `npm run check` equivalente: tipos generados (`api:types` de la web y de la app), `tsc -b`, `oxlint` (sin errores; sin advertencias en los
  archivos nuevos), **vitest 2.643 pruebas en verde** (`--maxWorkers=3`; nuevas: `orderLabelLayout`, `orderLabelPdf`, `printPdf`, `qzTray` con una
  librería falsa, `stationConfig`, `PrintersScreen` con un QZ Tray falso, la bandera del cliente y la ruta en `navigation.test`) y `vite build`.
- Playwright `e2e/fg1-impresoras.spec.ts` (escritorio y móvil a 360 px, `--workers=2`, Chrome instalado) contra el API real en :5102 y Vite en
  :5176 sobre `Teikem_fg1`: pantalla y estado «QZ Tray no detectado», alta de «Zebra ZD420 — Mostrador 1» 4×6 directa con nombre en el sistema y
  predeterminada, plantilla «4×6 — COD» (sin factura, COD grande abajo a la derecha, la vista previa cambia) que persiste al recargar, estación
  guardada que persiste al recargar, Prueba → vista → **Descargar PDF de 1 página de 288×432 pt**, despachador sin el menú y con «Sin permiso»,
  sin scroll horizontal a 360 px. Capturas `docs/manual/frontend/img/fg1-*.png`.
- El PDF 4×6, 4×4 y 4×2 del label integrado se abrió en el visor de Chrome y se revisó a ojo (barras, número legible, COD en caja negra; en 4×2
  se achica y cabe).

## Defectos encontrados y arreglados

- InMemory no genera `PublicId` (el `DEFAULT NEWID()` es de SQL): el alta de impresoras y plantillas ahora pone `PublicId = Guid.NewGuid()` como
  los demás servicios.
- El e2e encontró que la opción «Predeterminada de la compañía: X» y la propia «X» tenían el mismo texto; el recorrido elige por valor.
- `setState` dentro de efectos (aviso de oxlint) en el constructor y en los paneles de la estación: el borrador ahora se deriva de lo guardado
  (guardar un panel no pisa lo que se está editando en el otro).

## Diferencias con la maqueta (todas con su razón)

- «Impresora por defecto» ofrece las impresoras reales de la compañía, «Predeterminada de la compañía» y «PDF del navegador» (la maqueta tenía
  nombres fijos y «PDF»).
- «Formato de documento» solo «Carta» (D-FG7: Factura, BOL y Recibo no existen todavía).
- El label se previsualiza con la proporción de la etiqueta (4×6 alto) y en blanco y negro (la maqueta: 340×190 y COD naranja), porque es lo
  que sale en la impresora térmica.
- Los códigos de barras ocupan todo el ancho de su renglón (en un tercio de 4 in no caben legibles).
- Paneles nuevos (QZ Tray, ajuste de la compañía, impresoras de la compañía) debajo o arriba de lo de la maqueta, sin cambiar su orden.

## Decisiones tomadas por default para presentar al dueño al final de P1

| | Decisión | Alternativa |
|---|---|---|
| D-FG18 | La librería `qz-tray.js` no va en el paquete: se sirve de `web-app/public/vendor/qz-tray.js`, que soporte copia (no se pudo descargar nada aquí). Sin ella la pantalla lo dice y todo sale con el PDF | Dependencia npm `qz-tray` en el paquete |
| D-FG21b | «Imprimir al guardar» de la estación viene **encendido**: el interruptor maestro es el de la compañía (apagado por defecto), y la estación solo se excluye (reemplaza D-FG13) | Apagado hasta configurar la estación |
| D-FG24 | Con QZ Tray el giro lo hace QZ (`rotation: 90`) y el PDF va sin `/Rotate`; con el navegador el PDF lleva `/Rotate 90` | Siempre `/Rotate` en el PDF |
| D-FG25 | «Confirmar antes» muestra la vista HTML del label (la misma del PDF) con Imprimir y Descargar PDF, no el PDF incrustado | Visor de PDF incrustado |
| D-FG26 | Recorrido y servidores en :5102 (API) y :5176 (Vite) porque :5100 y :5174 estaban ocupados por procesos de otra sesión (no se tocaron) | — |

## Qué no se pudo verificar

- Impresión real con **QZ Tray** y una **Zebra** física (no hay QZ Tray ni impresora en este entorno): el cliente se probó con una librería
  falsa que cumple la API de qz-tray.js 2.x (`websocket.connect`, `printers.find`, `configs.create`, `print` con `{ type: 'pixel', format: 'pdf',
  flavor: 'base64' }`, `security.*`). **Falta instalar**: QZ Tray en cada computadora, `qz-tray.js` en `web-app/public/vendor/` y el certificado
  (`Printing:Qz` en el servidor y `override.crt` en cada computadora) — manual 12 §9.
- El diálogo de impresión del navegador (no se puede pulsar en un recorrido automatizado); el recorrido usa «Descargar PDF».
