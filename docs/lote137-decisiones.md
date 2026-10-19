# Lote 137 — P11: formatos de archivo (Excel, JSON y XML) y salida genérica (dictado de Luis, 2026-10-19)

> El lote se entrega en dos partes: **137a** (formatos de entrada: este apartado) y **137b** (salida de archivos por evento u horario y API saliente genérica), cada una con su manual, pruebas y capturas.

## 137a — Formato del archivo de entrada

### Qué se construyó
- **SQL:** sección (bn) de `update.sql` — `dbo.Integration.FormatJson` (NULL = CSV como siempre).
- **Reglas puras** (`FileFormatRules`, dominio): formatos AUTO/CSV/EXCEL/JSON/XML; resolución por la configuración o la extensión/firma; extensiones que toma la carpeta; validación con mensajes exactos; **rutas** `$ @ ^` con `.nombre`, `[n]`, `[*]` (un solo elemento se trata como lista de uno); lectura de **JSON** (profundidad 64) y de **XML** (sin DTD ni entidades externas; atributos `@x`, texto `#text`, repetidos = lista) a una tabla; columnas desde la línea (`@`), la orden (`^`) y el documento (`$`); orden sin líneas = una fila; límites 2 MB y 5.000 filas; CSV con comillas.
- **Conversor** (`FileFormatConverter`): Excel (reutiliza `ImportFileReader`: hoja por nombre, celdas como las ve la persona), JSON y XML → CSV de la plantilla (delimitador y encabezado de la plantilla). Un CSV no se toca. El resto del camino es el de siempre (hash del archivo original, importador con la cuenta de servicio, varias líneas por orden y reglas de «no pieza», bandeja).
- **Servicio y API:** `format` en alta y edición (no aplica a API), `FormatPreviewDto` y `POST /api/v1/integrations/format/preview` (archivo de muestra, sin guardar); la carpeta toma las extensiones del formato; la subida manual acepta .xlsx/.json/.xml.
- **Web:** sección «Formato del archivo» en el formulario (formato, hoja, rutas, columnas con agregar/quitar, «Probar con un archivo» con tabla de vista previa), es/en.

### Cómo se probó
- xunit `FileFormatRulesTests` (rutas válidas e inválidas, listas de uno, valores, JSON con líneas/sin líneas/orden sin líneas, quoting del CSV, XML con atributos y texto, DTD/XXE, archivos rotos, sin registros, límites, validación con mensajes exactos, resolución por extensión y firma, Excel por nombre de hoja con libro real) y suite completa.
- vitest `formatRules.test.ts` y las de Integraciones.
- Playwright `lote137-formatos.spec.ts` (escritorio y móvil) contra el API real: validaciones del alta, subida de JSON (orden con pieza, descripción y factura), XML con atributos, Excel (hoja «Pedidos» con el peso como número), formato automático (.json, .xlsx y .csv), duplicado por hash, errores exactos que van a la bandeja (sin mapeo, JSON roto, DTD, hoja inexistente, no es Excel, sin registros), carpeta de entrada con JSON (el .csv de al lado se ignora y el .json pasa a «procesados»), vista previa por API y la pantalla con capturas.

### Decisiones a revisar
1. **Un solo mapeo por integración**: un correo con adjuntos de varios formatos distintos necesita una integración por formato (el automático decide entre Excel/JSON/XML/CSV, pero el mapeo JSON/XML es uno).
2. **Los tipos de archivo de correo, SFTP y FTP siguen siendo una lista del conector** (por omisión `.csv` y `.txt`): hay que agregar `.xlsx`, `.json` o `.xml` ahí (no se infieren del formato, para no cambiar el comportamiento de lo ya configurado).
3. **Sin soporte de `.xls` viejo** (el importador de la web tampoco lo admite).
4. **La vista previa usa coma como delimitador y encabezado** salvo que se indique otra cosa por la API; la pantalla no manda el delimitador de la plantilla (en JSON/XML y Excel no se nota; en CSV solo se usa para mostrar).
5. **Una ruta que no encuentra órdenes es un error** (va a la bandeja): un archivo legítimamente vacío (sin órdenes ese día) también cae ahí; si molesta, se puede cambiar a «0 creadas» sin error.
