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
