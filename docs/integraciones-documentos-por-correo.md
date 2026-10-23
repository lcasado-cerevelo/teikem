# Documentos por correo → avisos y facturación (notas de diseño, 2026-10-23)

Notas de lo dictado por Luis para no perderlas. **Nada de esto está construido todavía.** Principio: **genérico**, para cualquier documento que se comporte así, no para estos archivos en específico.

## 1. Lo que ya existe (verificado en el código)
- Integración de correo con hasta 20 **lectores** (`src/Teikem.Domain/Integrations/DocumentReaders.cs`). Cada lector: fuente (BODY/PDF/FILE), condiciones (`ATTACHMENT_NAME`, `SUBJECT`, `TEXT` = texto del PDF), campos a leer, destino, cliente y plantilla propios.
- Cada documento va al **primer** lector que cumple; si ninguno aplica queda el resultado «Ningún lector de la integración aplica a este correo» (visible, no se pierde).
- PDF: texto real con PdfPig y OCR con Tesseract para escaneados (`PdfTextReader`, `IntegrationRunner`).
- Destinos: solo `ORDER`. Agregar uno = clase `IDocumentTarget` registrada (`DocumentTargetRegistry.cs`), sin tocar el runner.
- Servicio de avisos de salida: `ExitNoticeService.CreateAsync`. El aviso de llegada no se verificó.

## 2. Pedido de Luis
1. Una integración por cliente (ej. Axiscare) con **un lector por tipo de documento**; el tipo se sabe por el **título que está dentro** del documento (condición `TEXT`), no por el asunto.
2. Axiscare (Excel impreso a PDF, mismo formato, solo cambia el título):
   - «Despacho de Mercancia» → **aviso de salida** (SAL). Trae varios destinos por hoja (bloque de dirección a la derecha de cada grupo de filas).
   - «ENVIO DE MERCANCIA PARA ADVANCE» → **aviso de llegada** (LLE). Este es el «recibo»: Luis confirmó que es el recibo. Trae columna *Stocking*, sin direcciones.
3. Drive Medical (facturas escaneadas con anotaciones a mano: vagón, total de paletas) = entradas. Hojas de ruta CMS («ROUTE SHEET #») = flujo de rutas, no avisos. Factura de Depot (4627) = se ignora.
4. Logo de Depot extraído en `F:\Download\LogoDepot\` (`depot-mark.png`, `depot-mark-inverted.png`); fuente: escaneo de 150 ppi, pedir original si existe.

## 3. Conduces para facturación (nuevo, 2026-10-23)
Archivos en `F:\Download`: REGULAR 10-12-2026, OXIGENO / ALTAS / AXISMED / RECOGIDOS / VETERANOS 12 AL 17 OCT 2026.
- **La primera página se ignora** (resumen de contabilidad); el conduce empieza en la segunda.
- Es lo que facturación usa hoy para facturar, dividido en **divisiones de facturación** distintas de las divisiones de envío ya creadas. **Las de envío no se tocan.** Las de facturación son aparte (pareen o no con las de envío).
- Algunos son texto real y otros escaneados (OCR); evaluar si un **LLM** sustituye al OCR, montado en un servidor de AWS que se **apaga** (el correo se procesa por lotes, nadie espera).

## 4. Pendiente
- Destinos LLE y SAL; lectura del bloque de direcciones múltiples; comparación sin acentos/mayúsculas/espacios dobles en `TEXT`.
- Análisis de los conduces y diseño de «división de facturación».
- Dimensionar el servidor LLM (ver respuesta en la conversación).

## 5. Prueba de OCR con los conduces (2026-10-23)
- Los 6 archivos (312 páginas) son escaneos sin texto real; Tesseract (spa+eng, 3x) los lee en 0,1–1,2 min por archivo.
- Modo por omisión (3x, página automática): en páginas de conduce de REGULAR salen 5 de 14 números de orden/factura (se pierden las facturas y los guiones de la orden); en OXIGENO 7/14; VETERANOS 7/7.
- **Escala 4x y modo «un solo bloque»: REGULAR 11/14, OXIGENO 14/14, VETERANOS 7/7.** Binarizar (Otsu) no mejoró. Pendiente cambiar el lector (escala 4, probar ambos modos) y validar por reglas (formato de orden/factura, conteo de líneas contra «Cases», sumas del resumen).
- AXISMED (tipografía deformada, confianza 0,82–0,84): errores de letras en códigos («El04941» por E104941) y en importes; candidato al modelo de visión.

## 6. Hecho (2026-10-23)
- Lector de PDF: escala 4x, dos modos de página (se queda con el que reconoce más) y decisión de OCR **por página** (`OcrScale`, `PdfTextReader`).
- Destinos `ARRIVAL_NOTICE` y `EXIT_NOTICE` (`NoticeDocumentTargets.cs`): columnas 1 referencia, 2 SKU, 3 cantidad, 4 destino, 5 fecha, 6 lote; un aviso por referencia/destino/fecha; si una línea falla no se crea ninguno.
- Modelos de lector en `web-app/src/features/integrations/readerPresets.json` (fuente única; la pantalla los ofrece y `NoticeDocumentTargetsTests` los prueba con el texto real de los PDF): despacho → salida, envío (recibo) → llegada, factura Drive Medical → llegada.
- **Sin integración creada aún**: faltan buzón, cliente y almacén de la compañía; con los modelos se arma desde la pantalla.
- **Sin decidir**: hojas de ruta CMS (¿qué crean? no traen dirección: parecen recogidas por referido para marcar), factura de Depot (4627), «cantidad each» vs «paletas» cuando solo hay un número.

## 7. CMS y lectores por registro (2026-10-23)
- Respuestas de Luis: la **ruta de CMS = nombre del chofer de la hoja** («ERD Pick Up 1»), que en Teikem será una **zona** con sus choferes (un chofer puede cubrir varias zonas: ya existe); número de orden y de envío = **Order #** de CMS; es una **recogida**; «New DME … NOTES» va como **nota**, unida a las instrucciones de entrega; las órdenes se crean solas y el **cierre** (ruta completada, COD) va por **propuesta con confirmación** (modo sugerir/automático por lector).
- Hecho: lectores por registro (`RecordStart`, `FromHeader`, `AppendWith`, transformación `SPACES`), modelo «Hoja de ruta de CMS» probado con el texto real del PDF (OCR) en `CmsRouteSheetReaderTests`.
- **Pendiente (pregunta abierta a Luis):** (a) la dirección del paciente debe ser la **parada de recogida** y el consignatario CMS: hoy el importador solo conoce consignatario, la recogida sale del lugar por defecto del cliente y una ruta de recogida (Lote 211) pide una parada de recogida pendiente; (b) la zona se calcula por código postal/pueblo de la parada: «ruta → zona» necesita un campo de zona en el importador; (c) cola de propuestas para «Ruta completada» (Referral # = Order #).
- El OCR de la hoja de CMS lee mal la cantidad (un «1» sale «4»): el modelo no la usa.

## 8. CMS: recogida en la dirección del paciente y ruta como zona (2026-10-23, hecho)
- `OrderCreateRequest.NewPickup` (punto de recogida escrito libre: reutiliza uno del directorio por nombre + línea 1 o lo crea dentro de la transacción) y `DispatchZoneCode` (zona forzada por código o nombre; manda sobre la del ZIP). Importador: campos `pickupName`, `pickupLine1`, `pickupLine2`, `pickupCity`, `pickupState`, `pickupPostalCode` y `zone`.
- Consignatario fijo = código del consignatario de CMS como valor por defecto de la plantilla. Número de orden y de envío = Order # (el cliente debe tener «El cliente asigna el número de envío»).
- Probado con OrderService real (InMemory) y el importador (validar → mapear → crear): `OrderNewPickupZoneTests`, `CmsOrderImportTests`. **No probado:** confirmar el lote completo (ExecuteUpdate no corre en InMemory; lo cubre el smoke con SQL Server) ni el camino real con el correo.
- Falta: cola de propuestas para cerrar con «Ruta completada»; crear la integración real (buzón, cliente, zonas «ERD Pick Up N», consignatario CMS, plantilla).

## 9. Cola de propuestas de cierre (2026-10-23, hecho)
- Destino `ORDER_COMPLETION` (columna 1 = clave de la orden), modo `SUGGEST` (por omisión) o `AUTO` y estatus de cierre `PICKUP`/`DELIVERED` por lector; tabla `dbo.IntegrationProposal` (update.sql 2026-10-23 (k)); pestaña Integraciones → Propuestas; modelo «Ruta completada de CMS».
- La orden cambia solo al confirmar (a nombre de quien confirma) por el pipeline de estatus (recogida) o el registro de entrega (entregada). Probado: `OrderCompletionProposalTests`, `CmsCompletedRouteReaderTests` (texto real), pantalla en `IntegrationsScreen.test.tsx`.
- Pendiente: crear la integración real (buzón, cliente, zonas, consignatario CMS, plantilla), probar con SQL Server (smoke) y decidir cuándo pasar un lector a AUTO.

## 10. Scripts SQL de las integraciones (2026-10-23, hecho)
- `scripts/integraciones/axiscare-depot.sql` y `cms-advance-logistics.sql` (generados por `generar-sql.cjs` desde `readerPresets.json`), idempotentes, aparte de update.sql; integraciones apagadas hasta poner el buzón en la pantalla. Corridos y verificados en la base local (la lectura con el modelo real, los permisos de la cuenta de servicio y la segunda corrida sin duplicar). Lector con almacén propio (`WarehousePublicId`).
- Falta correrlos en las bases reales, poner los buzones y crear los productos y las zonas restantes.
