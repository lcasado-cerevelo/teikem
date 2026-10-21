# Lote 188 — Logo de la compañía en los PDF (pedido de Luis, 2026-10-21)

## Qué se construyó
- `kernel/ui/pdfLogo.ts`: toma el logo que la compañía subió (Ajustes → Marca; lockup, si no la marca cuadrada; variante según el fondo), lo rasteriza a PNG conservando su proporción y lo cachea. Sin logo subido no hace nada.
- **Reportes de marca** (`reportPdf`, `barcodeReportPdf`, y por tanto los documentos de impresión de `usePrinting`/Órdenes, que antes salían sin logo): el logo de la compañía va en la banda azul y reemplaza el símbolo, «TEIKEM» y el lema. Si no hay logo de la compañía, queda el de Teikem.
- **Exportaciones a PDF de tablas** (`DataTable`/`ListPager`, normales y agrupadas): el logo va arriba del encabezado (compañía, título, fecha y filtros). Sin logo subido, igual que antes.
- Si la variante elegida no es la del fondo (solo hay la de fondo claro sobre la banda azul, o la inversa sobre la hoja blanca), se dibuja sobre una placa para que se vea.

## Cómo se probó
- vitest `pdfLogo.test.ts` (el reporte con logo de la compañía no lleva «TEIKEM» y sin logo sí; la tabla dibuja la imagen; ajuste de proporción), toda la carpeta `kernel/ui` (454) y `tsc`.
- No se probó en un navegador con un logo real subido (rasterizado SVG/PNG con un canvas).

## Decisiones a revisar
1. Sin logo subido: los reportes de marca conservan el de Teikem; las tablas salen sin logo.
2. Excel y CSV no llevan logo; las etiquetas (posiciones, envíos) tampoco (no son informes).
3. El nombre de la compañía sigue a la derecha de la banda, aunque el lockup ya lo traiga.
