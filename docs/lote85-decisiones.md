# Lote 85 — Pantalla COD: cobro y reconciliación con hojas escaneadas (P8, 2 de 3) (2026-10-17)

Segundo lote de P8: la web del COD del Lote 84, incluida la **subida de archivos** que pidió Luis para la reconciliación («le doy unas hojas escaneadas con un código de barras que es el número de la factura»).

## Qué se construyó
- **Pantalla `/money/cod`** (antes «pendiente» en el menú; se conserva en su lugar de Contabilidad): pestañas **Órdenes con COD** (lista con filtro de estatus, buscador y exportación completa; ficha con los cobros; **Registrar cobro** con validación de tope y del número de cheque, `cod.collect`), **Reconciliar** (`cod.reconcile`) y **Corridas** (lista y detalle).
- **Reconciliar:** cliente de las hojas opcional, zona para arrastrar o elegir archivos (PDF e imágenes), avance por archivo y por página, páginas sin código avisadas, códigos escritos a mano, lista de códigos con «Quitar», **vista previa** (`dryRun`, no cambia nada), **Cuadrar** con confirmación y resumen de la corrida; un archivo inválido o dañado no detiene a los demás.
- **Lectura de códigos de barras en el navegador** (`barcodeReader.ts`): **pdf.js** (cada página del PDF, a escala 2 y, si no hay código, a 3.5) y **zxing-wasm** (Code 128/39/93, EAN, ITF, QR, DataMatrix, PDF417…, con giro, inversión y reducción), cargados **solo al leer el primer archivo** (chunk aparte; el .wasm y el worker de pdf.js salen del propio sitio, no de una red de distribución). Los archivos **no salen del equipo**: al API solo llegan los códigos con su origen.
- `openapi.json` y el cliente generado (6 rutas y 12 esquemas de `/cod`); textos es/en; estilos `cod.css`; manual de pantallas L85 con capturas, capítulo 21 §5 y FAQ.

## Cómo se probó
- vitest: reglas puras (cobro, códigos manuales, cuerpo y origen de la corrida, resultados, tipos de archivo; 10) y la pantalla con fetch simulado y el lector simulado (7): lista y filtro, ficha, cobro (tope, número de cheque, cuerpo), reconciliar con archivos + manual + vista previa + cuadrar + nueva corrida, quitar un código descarta la vista previa, archivo inválido, archivo roto con otro bueno, sin `cod.reconcile` y corridas. `npm run check` completo (3007 pruebas).
- **Playwright contra el API real y lectura REAL de códigos de barras**, escritorio y móvil (360 px, sin scroll horizontal): se genera un código Code 128 con el número de la factura (bwip-js), un **PDF de 2 páginas** (la 2.ª sin código; jsPDF) y una **imagen PNG**; el navegador los lee, la vista previa dice «Se cuadra» y «Repetido», se cuadra y el COD queda Cuadrado; la corrida aparece en Corridas. Después de dos cobros (60 en efectivo y 40 con cheque). Requiere `E2E_SQL_DB` (deja la orden Entregada con `sqlcmd`, porque confirmarla pide una tarifa de contrato): sin esa variable el recorrido se salta.

## Decisiones a revisar con Luis
1. **Código de barras = número de la factura del cliente** (`FAC-#####`), que se cuenta por cliente: por eso la pantalla deja elegir el **cliente de las hojas**. Con una hoja real de muestra se confirma el formato (¿Code 128? ¿lleva prefijo o ceros?) y, si hace falta, se limpia el código antes de buscarlo.
2. **Todo se lee en el navegador**: nada de archivos en el servidor ni dependencias nativas. Costo: las dos librerías (~430 KB de pdf.js, ~0.4 MB del .wasm de zxing) se bajan una vez al primer uso de Reconciliar; no pesan en el resto de la aplicación.
3. **Páginas con varios códigos:** se mandan todos; los que no son facturas salen «No encontrado» y se pueden quitar. Si las hojas reales traen más de un código, se puede filtrar por tipo de código.
4. **Vista previa obligatoria** antes de cuadrar (el botón se habilita solo con una vista previa que cuadre algo).
5. Dependencias nuevas: `zxing-wasm`, `pdfjs-dist` y, solo para el recorrido, `bwip-js` (desarrollo).

## Sigue (P8)
- **Lote 86:** remesa al cliente (`CodRemittanceBatch`, comisión del contrato, neto, aprobación y exportación con plantillas) y KPIs de COD.
- Pendientes: anular un cobro; cobro desde el POD de la app de choferes (P7); una hoja real de muestra para afinar la lectura.
