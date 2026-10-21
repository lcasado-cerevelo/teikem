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

## 137b — Integraciones de salida (archivos por SFTP/FTP y API saliente genérica)

### Qué se construyó
- **SQL:** sección (bo) de `update.sql` — `Integration.OutputJson`, conector `HTTP` en el CHECK, `IntegrationOutput` (cada envío con su contenido ya armado) e `IntegrationOutputOrder` (cola de órdenes por integración; índice único mientras esperan).
- **Reglas puras** (`OutputRules`, dominio): catálogo de 19 campos de la orden, validación con mensajes exactos (estatus, disparador, formato, delimiter, nombre de archivo con marcadores, columnas, API: URL como los webhooks, método, autenticación, encabezados), armado en **CSV, JSON y XML** (números como números, fechas ISO UTC, nulos), nombre del archivo y reintentos (los de los webhooks).
- **Alcance** `files.orders_export` (familia archivos, necesita cliente); entrada y salida no mezclan alcances.
- **Cola:** efecto de estatus `OutputOrderEffect` anota la orden en la misma transacción del cambio, solo en integraciones de salida encendidas del cliente con ese estatus.
- **Envío** (`IntegrationOutputService`): al instante = un envío por orden; por horario = un envío por tanda de hasta 1.000; SFTP/FTP suben con `.part` y renombran (nunca pisan); HTTP con Bearer/Basic/encabezado, encabezados extra, `Idempotency-Key`, 10 s, sin redirecciones y anti-SSRF (la conexión de los webhooks). Reintentos 1 min/5/30/2 h/12 h → «No entregado». «Enviar ahora», vista previa, probar API, bandeja (listar, reintentar, descartar), purga a 30 días y trabajo `INTEGRATION_OUTPUT` (cada minuto; el horario lo dispara el trabajo de integraciones por horario).
- **Web:** dirección Entrada/Salida al crear, sección «Qué se manda» (estatus, cuándo, formato, columnas), «API saliente», carpeta de destino, vista previa, acción «Enviar ahora» y «Salidas de archivos y API» en la pestaña Salidas (el contador suma webhooks y envíos); es/en.

### Cómo se probó
- xunit `OutputRulesTests` (validación exacta, nombres, marcadores, delimitadores, API, CSV/JSON/XML con comillas, escapes y caracteres inválidos, catálogo, alcance) y suite completa.
- vitest `outputRules.test.ts` (validación en cliente, cuerpos de alta y edición, secreto que solo se manda si se escribe otro, horario según el disparador) y las de Integraciones.
- Playwright `lote137b-salida.spec.ts` (escritorio y móvil) contra el API real y un receptor HTTP: opciones, validaciones del alta, API al instante (autenticación, encabezados, solo el cliente de la integración), vista previa y prueba, trabajo de salida, por horario en CSV con PUT y otro tipo de autenticación, editar (XML), fallos con 6 intentos hasta «No entregado», reintentar/descartar y sus 409, apagada no manda, no recibe archivos, y **subida real por SFTP y FTP** a servidores de prueba (archivo completo, sin `.part`); más el recorrido de pantalla con capturas.

### Decisiones a revisar
1. **Solo salen las órdenes que llegan al estatus DESPUÉS de crear la integración** (no hay «enviar lo histórico»): si hace falta, se agrega un botón de carga inicial.
2. **Sin salida por correo** (no hay hoy un envío de correo con adjuntos): SFTP, FTP y HTTP cubren lo pedido.
3. **Campos fijos de la orden** (19): no se mapean campos personalizados ni líneas de carga/piezas (una fila por orden); piezas y descripciones por línea serían una segunda fase.
4. **Reintentos sin aviso del sistema**: el contador de la pestaña Salidas es la señal; no hay aviso por correo/APP cuando un envío queda no entregado (sí lo hay para webhooks suspendidos).
5. **El cuerpo se arma al pasar la cola a envío** (en menos de un minuto) y no se rearma al reintentar (si la orden cambió después, el reintento manda lo armado entonces).
6. **Una vuelta por minuto**: «al instante» es en menos de un minuto; un destino lento puede retrasar a los demás hasta la siguiente vuelta (corta a los 3 fallos seguidos por integración).
7. Una orden que vuelve a llegar al mismo estatus (reabierta y entregada otra vez) **sí** vuelve a salir (cola nueva); el receptor puede deduplicar por número de envío.
