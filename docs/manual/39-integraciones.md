# 39 — Integraciones (Lote 132, P11)

> Dictado de Luis (2026-10-19). Una integración mueve datos entre Teikem y otro sistema **por configuración, sin programar cada vez**. Este lote trae las integraciones de **entrada** por **carpeta** y por **subida manual**; correo, SFTP, API y webhooks llegan en los lotes siguientes (ver `docs/p11-diseno-integraciones.md`).

## 1. Qué es y quién puede
- **Módulo** `INTEGRATIONS` (apagado; se enciende por compañía en Sistema → Módulos). Pantalla: **Sistema → Integraciones / API** (`/system/integrations`).
- **Permisos:** `integrations.view` (ver y el historial) · `integrations.create` (crear; base) **más** el de cada familia de alcances: `integrations.create.files` (carpeta, SFTP, correo), `.transport` y `.warehouse` (llegan con la API) · `integrations.manage` (editar, apagar, correr, subir archivo) · `integrations.inbox` (la bandeja). TenantAdmin los trae todos; el administrador se los da a los roles que quiera.
- **Cuenta de servicio propia por integración:** al crear la integración se crea un usuario de clase SERVICE (no inicia sesión nunca) con un rol «Integración: nombre» que solo trae los permisos de los alcances elegidos. Lo que entra queda **atribuido a esa cuenta**, no a quien configuró. **Quien crea no puede dar más de lo que tiene:** solo se le ofrecen los alcances de familias que él puede crear, de módulos que la compañía tiene y cuyos permisos él posee.

## 2. Crear y editar una integración
Pantalla: **Nueva integración** (nombre, descripción, cliente, plantilla de importación, conector, horario, alcances, a quién avisar y retención) · API: `POST /api/v1/integrations` (`integrations.create`), `PATCH /{publicId}` (`integrations.manage`), `GET /options`, `GET`, `GET /{publicId}`, `POST /{publicId}/deactivate` y `/reactivate` (apagar y prender: la cuenta de servicio, su rol y la regla de avisos siguen a la integración; nunca se borra).
- **Cliente y plantilla:** los envíos que entran se crean para ese cliente con la plantilla de importación elegida (la misma de Envíos → Importar; con **varias líneas por orden** y reglas de «no pieza» del capítulo 03 §7.5). Como el envío la numera el archivo, el cliente debe tener marcado que numera sus envíos.
- **Conector:** **Carpeta de entrada** (el nombre de la carpeta, solo letras, números, guion y guion bajo; los `.csv` y `.txt` que se dejen ahí se procesan y pasan a `procesados/` o `errores/`; **nunca se borran**) o **Subida manual** (el archivo se sube desde la pantalla). La carpeta vive en `Integrations:FolderRoot/{compañía}/{carpeta}`.
- **Horario:** solo a mano o cada N minutos (1 a 10080; la carpeta se revisa sola; una subida manual no se programa).
- **A quién avisar:** usuarios de la compañía + **«Incluirme»**. Cada corrida con algo que reportar (envíos creados, filas a la bandeja o una falla) manda un **aviso del sistema** (campanita y correo) a esas personas; no se avisa a quien la creó si no lo marcó.
- **Retención:** días que se guardan los datos crudos de la bandeja (30 por omisión, 1 a 365); pasado el plazo se purga el detalle y queda el motivo.

| Mensaje (400 salvo que se diga) | Cuándo |
|---|---|
| «El nombre de la integración es obligatorio.» · «Máximo 120 caracteres.» | nombre vacío o largo |
| «Ya existe una integración con ese nombre.» (409) | nombre repetido |
| «Escoja al menos un alcance para la integración.» · «El alcance 'X' no existe.» | alcances |
| «No puede dar el alcance «X»: le falta el permiso 'integrations.create.files'.» (400) · «…usted no tiene los permisos …» · «La compañía no tiene el módulo que necesita «X».» | sin escalada de privilegios |
| «Indique el cliente de los envíos de esta integración.» · «Escoja la plantilla de importación con la que se leen los archivos.» | cliente / plantilla |
| «Ese tipo de conector todavía no está disponible.» · «Tipo de conector desconocido: X.» | conector |
| «Indique el nombre de la carpeta de entrada.» · «El nombre de la carpeta solo admite letras, números, guion y guion bajo (sin rutas).» | carpeta |
| «El intervalo debe estar entre 60 y 604800 segundos.» · «Una integración de subida manual no se programa por horario.» | horario |
| «Los días de retención deben estar entre 1 y 365.» · «Uno de los usuarios a avisar no existe o está inactivo.» | retención / avisos |

## 3. Correr una integración
- **Carpeta:** por horario (el trabajo programado «Integraciones por horario», cada minuto) o con **Correr ahora** (`POST /{publicId}/run`, `integrations.manage`): lee los archivos nuevos de la carpeta. **409** «La integración ya se está ejecutando; espere a que termine.» · **422** «La integración está apagada; enciéndala para correrla.»
- **Subida manual** (cualquier integración): **Subir archivo** (`POST /{publicId}/upload`, multipart, hasta 4 MB): se procesa igual.
- **Cada archivo:** se calcula su **hash SHA-256**; si el mismo contenido ya se procesó, queda «Repetido» y no se vuelve a procesar. Se lee (UTF-8; si no, Latin-1 de Excel), pasa por el importador **en nombre de la cuenta de servicio**, los envíos buenas se crean y las demás van a la **bandeja**. Un archivo vacío o más grande del límite (`Integrations:MaxFileMegabytes`, 2) queda «Con error».
- **Resultado de la corrida:** *Bien*, *Con pendientes* (algo fue a la bandeja) o *Falló*; con archivos leídos, envíos creados y enviadas a la bandeja. **Historial:** `GET /{publicId}/runs` (hasta 100) con sus archivos.

## 4. La bandeja (`integrations.inbox`)
Lo que entró y no se pudo procesar solo. **Un envío con problemas nunca se crea a medias.** Cada ítem trae el **motivo exacto** (el mismo mensaje de la vista previa de importación: «El envío no trae ninguna pieza…», «El número de pieza X se repite…», «Fila 4 · …») y los **datos tal como llegaron**.
- **Pantalla:** pestaña **Bandeja** (con el contador de pendientes) con filtros por integración y estatus; **Corregir** (las mismas celdas del archivo, tal como llegaron), **Reprocesar**, **Descartar** (con nota), **Reprocesar todo** de una integración y **Descargar pendientes (CSV)**.
- **API:** `GET /inbox` · `GET /inbox/{id}` · `PATCH /inbox/{id}` (corregir celdas: mismas filas y columnas) · `POST /inbox/{id}/reprocess` · `POST /inbox/{id}/discard` · `POST /{publicId}/inbox/reprocess-all` (hasta 200) · `GET /inbox/export` (CSV).
- **Reprocesar** vuelve a pasar el ítem por el importador: si ya cuadra nace el envío y el ítem queda *Reprocesado* (con el enlace al envío); si no, sigue *Pendiente* con el motivo nuevo. Errores: **409** «Este ítem ya se resolvió (reprocesado o descartado).» · **409** «Los datos de este ítem ya no se guardan (pasó la retención o el archivo no se pudo leer)…» · **400** «Las filas no coinciden con las del ítem…» · «Cada fila debe traer la misma cantidad de celdas que tenía.»
- **Un archivo que no se pudo leer** (columnas, cliente suspendido…) queda en la bandeja sin datos para reprocesar: se descarta y se pide el archivo de nuevo.

## 5. Casos frecuentes
- *«Se creó la integración pero no corre sola».* Falta el horario («Cada…») o la integración está apagada.
- *«Mi carpeta no encuentra el archivo».* Debe ser `.csv` o `.txt`, directamente en la carpeta (no en subcarpetas).
- *«Subí el mismo archivo y no creó nada».* Es el hash: ya se procesó; el historial lo marca «Repetido».

## 6. Integración por correo electrónico (Lote 133)

> Dictado de Luis (2026-10-19): un conector de correo que aplica a **cualquier cliente**. Llega un correo al buzón de la integración y Teikem lee **los adjuntos**, **el cuerpo** o ambos, y los pasa por la **misma plantilla de importación** de la integración. Un buzón por integración; el contenido del correo es **entrada no confiable**: nunca se ejecuta nada y el HTML se reduce a texto.

**Cómo se configura** (conector **Correo** al crear la integración; permisos `integrations.create` + `integrations.create.files`):
- **Buzón (IMAP):** servidor, puerto, conexión cifrada, usuario, **contraseña** (se guarda **cifrada** con la llave de la compañía y no se vuelve a mostrar: al editar, vacía = se conserva; requiere `Files:MasterKey` en el servidor), carpeta que se lee (INBOX) y carpeta de procesados. Teikem lee solo los correos **no leídos**; al atenderlos los marca como leídos y los mueve a «Procesados» (se crea si falta). **Nunca borra.** Hasta 50 correos por corrida.
- **Qué correos se toman:** **asunto** (contiene / es igual / empieza / termina / comodín / expresión regular; el filtro «contiene» corre en el servidor de correo) y **remitentes originales permitidos** (correos o dominios; vacío = cualquiera).
- **El remitente original:** como el correo casi siempre llega **reenviado**, el remitente que cuenta se busca en este orden: `X-Original-Sender`, `X-Original-From`, `Resent-From`, `Reply-To`, la primera línea «De:» / «From:» del bloque reenviado del cuerpo y, si no hay, el `From`. Un correo de otro remitente **no se procesa**: va a la bandeja con «Remitente no permitido: x@y.com.»
- **Adjuntos:** todos los que cumplan el **tipo** (extensiones, `csv, txt` por omisión), solo el primero, o los que cumplan el **nombre** (contiene / comodín `ordenes-*.csv` / expresión regular…). Cada adjunto se procesa como un archivo de la carpeta (hash, importador, bandeja). «El correo debe traer adjunto»: si no trae, va a la bandeja con «El correo no trae ningún adjunto que cumpla el criterio.»
- **Cuerpo:** «Leer también el cuerpo» con una lista de **campos a leer**. Cada campo va a una **columna de la plantilla** (1, 2, 3…): el cuerpo se convierte en una fila de archivo (o varias, con un bloque) y la misma plantilla la lee, con sus reglas (p. ej. varias líneas por orden). Tipos de campo:
  - **Etiqueta:** «Reference #» → el texto que sigue en esa línea («Reference #: 123»; sin importar acentos ni mayúsculas).
  - **Expresión regular:** el primer grupo entre paréntesis (o todo lo que coincida).
  - **Bloque de renglones** (una tabla, p. ej. «Order Summary»): texto de inicio, texto de fin, separador de columnas y qué columna del renglón va a qué columna de la plantilla (`6=1, 7=2`); cada renglón es una línea del archivo y los valores sueltos se repiten en todos.
  - **Bloque con expresión por línea (2026-10-22):** para tablas que el separador de columnas no resuelve (descripciones partidas en varias celdas o varias líneas, el SKU pegado a la descripción, renglones sueltos con «-»). Escriba una **expresión regular por línea**: cada línea del bloque que coincida es una fila y los **grupos entre paréntesis** (1, 2, 3…) van a las columnas de la plantilla (`6=1` = la posición 6 de la plantilla toma el grupo 1). Las líneas que no coinciden (continuaciones, rayas, totales, el descuento) **se ignoran**. Con la expresión no hacen falta el texto de inicio ni el de fin. Ejemplo para un packing list: `^(T-[\w-]+)\t.*?\t([\d,]+)\t([\d,]+)$` con columnas `2=1, 3=3`.
  - **Repetir el bloque en cada página:** un PDF repite el encabezado de la tabla en cada página. Con **«Repetir el bloque»** el bloque se lee cada vez que aparece el texto de inicio (el encabezado) y termina en el texto de fin, y se juntan las filas de todas las páginas. Sin la opción solo se lee la primera aparición. Necesita el texto de inicio.
  - **Etiquetas en una tabla o PDF:** la etiqueta se busca también en **cualquier celda** del renglón (`Ship Date ⇥ 10/7/26 ⇥ Order # ⇥ GRP0001 ⇥ PO ⇥ 990011`): el valor es la celda que sigue (o lo que sigue en la misma celda) y **no arrastra** las demás celdas.
  - **Transformación** opcional: mayúsculas, solo dígitos (teléfonos) o monto (quita `$` y comas). **Obligatorio:** si no se encuentra, el correo va a la bandeja con «No se encontró el campo obligatorio de la columna N (…).»
- **PDF adjuntos (2026-10-22):** el interruptor **«Leer también los PDF adjuntos»** toma los `.pdf` del correo (sin tener que escribir `pdf` en los tipos de adjunto), los convierte en **texto** y los lee con **los mismos campos a leer del cuerpo** (etiqueta, expresión regular, bloque de renglones; la lista de campos es una sola y vale para el cuerpo y para los PDF).
  - **PDF con texto real** (los que genera un sistema): se usa su texto. Cada renglón visual es una línea y, donde hay un hueco de columna, va un **tabulador**: en el bloque de renglones use el **tabulador** como separador de columnas.
  - **PDF escaneado** (una imagen): se dibuja cada página y se pasa por **OCR** (Tesseract, español e inglés). El OCR corre en nuestro servidor; los documentos del cliente no salen a ningún tercero. Necesita la carpeta de idiomas (`Integrations:OcrTessdataPath` con `spa.traineddata` y `eng.traineddata`); sin ella los PDF con texto real se leen igual, pero uno escaneado va a la bandeja con «El PDF es una imagen (escaneado) y la lectura por OCR no está configurada…». Un escaneado se lee con menos precisión que un PDF con texto: revise lo que entra a la bandeja.
  - **Límites:** `Integrations:MaxPdfMegabytes` (15 por omisión) y `Integrations:MaxPdfPages` (30: las demás páginas no se leen). Un PDF con contraseña no se puede leer.
  - **Para ajustar los campos:** cuando un PDF no da datos, el motivo en la bandeja trae **un pedazo del texto leído** («No se encontró ningún dato en el PDF orden.pdf. Texto leído: «…»»): ahí se ve cómo salieron las columnas y se corrige la etiqueta o el bloque. También puede **subir el PDF a mano** (Subir archivo) a la integración de correo con «Leer PDF» para probar sin esperar un correo.
- **Lectores (2026-10-22):** una integración de correo puede tener varios **lectores**; cada uno dice qué hacer con un tipo de documento que llega en el mismo buzón (el pedido en el cuerpo, el packing list en PDF, la factura en PDF, un Excel…).
  - **Qué es un lector:** un nombre, **qué lee** (el cuerpo del correo, un PDF adjunto o un archivo de datos adjunto), sus **condiciones**, sus **campos a leer** (cuerpo y PDF), su **destino**, y si hace falta su **cliente** y su **plantilla**.
  - **Condiciones** (todas deben cumplirse; sin condiciones el lector toma todo lo de su fuente): sobre el **nombre del adjunto**, el **asunto del correo** o el **texto del documento** (el del PDF ya leído, con OCR si es escaneado), con los mismos operadores del asunto (igual, contiene, empieza, termina, comodín, expresión regular). Ejemplo: PDF cuyo texto contiene «Packing List» → lector «Packing list»; PDF cuyo texto contiene «Invoice» → lector «Factura».
  - **Quién toma cada documento:** el **primer** lector de la lista cuyas condiciones cumple (el orden de la lista importa). «Solo el primer documento» limita un lector a un documento por correo. Si ningún lector toma nada del correo, va a la bandeja con «Ningún lector de la integración aplica a este correo.»; un adjunto que ningún lector toma se ignora (firmas, imágenes).
  - **Destino:** lo que crea en el sistema lo leído. Hoy: **Orden de transporte**. El esquema está hecho para agregar más destinos (aviso de llegada, aviso de salida, órdenes de compra y de venta, productos, factura procesada, COD…) sin cambiar lo demás.
  - **Cliente y plantilla propios:** un lector puede usar otro cliente y otra plantilla que los de la integración (un mismo buzón con documentos de varios clientes). Vacíos, usa los de la integración.
  - **Los ajustes de siempre:** si agrega lectores, mandan sobre los ajustes de adjuntos, cuerpo y PDF de más arriba. El botón **«Convertir lo de arriba en lectores»** crea los lectores equivalentes a esos ajustes para seguir desde ahí. Una integración **sin** lectores funciona exactamente como antes.
  - **Subir a mano:** un PDF subido (Subir archivo) a una integración con lectores se evalúa como un correo que trae solo ese adjunto.
- **Repetidos:** el mismo contenido (hash del adjunto o del cuerpo leído) no se procesa dos veces; el número de envío repetido ya lo rechaza el importador.

**Probar sin buzón:** en la lista, **Probar conexión** (`POST /{id}/email/test`) comprueba servidor, usuario, contraseña y carpeta y dice cuántos correos sin leer hay (sin tocarlos; el mensaje nunca trae la contraseña). Y **Subir archivo** acepta un **`.eml`**: se procesa EXACTAMENTE igual que uno recibido por IMAP (mismas reglas de remitente, asunto, adjuntos y cuerpo), así que se puede probar la configuración antes de apuntar el buzón.

**Correr:** por horario (cada N minutos) o **Correr ahora**. Si el buzón no responde, la corrida queda *Falló* con «No se pudo conectar con el buzón: …» y no pasa nada más (los correos siguen sin leer).

| Mensaje (400) | Cuándo |
|---|---|
| «Indique el servidor del buzón (IMAP).» · «El puerto debe estar entre 1 y 65535.» · «Indique el usuario del buzón.» · «Indique la contraseña del buzón.» · «Indique la carpeta del buzón que se lee (p. ej. INBOX).» | buzón |
| «No se pueden guardar contraseñas de conectores: falta la llave maestra del servidor (Files:MasterKey).» | el servidor no tiene la llave maestra |
| «Condición desconocida: X. Use EQUALS, CONTAINS, STARTS, ENDS, WILDCARD o REGEX.» · «Indique el texto de la condición.» · «La expresión regular 'X' no es válida.» | asunto y nombre del adjunto |
| «El remitente permitido 'X' no es un correo ni un dominio válido.» · «Máximo 50 remitentes permitidos.» | remitentes |
| «Modo de adjuntos desconocido: X. Use ALL, FIRST o BY_NAME.» · «Con «por nombre» indique la condición del nombre del adjunto.» | adjuntos |
| «Con «leer el cuerpo» indique al menos un campo a leer.» · «La columna debe estar entre 1 y 500.» · «Tipo de campo desconocido: X. Use LABEL, REGEX o BLOCK.» · «Indique la etiqueta que se busca en el correo.» · «Indique la expresión regular.» · «Un bloque necesita el texto de inicio y el de fin.» · «Un bloque necesita al menos una columna (posición y número de columna del renglón).» · «Transformación desconocida: X. Use NONE, UPPER, DIGITS, MONEY o SPACES.» | campos del cuerpo |

**Casos frecuentes:** *«No se pudo conectar con el buzón»* → revise servidor, puerto (993 con SSL), usuario y la **contraseña de aplicación** del buzón (Microsoft 365: buzón compartido con IMAP habilitado). *«Remitente no permitido»* con un correo que sí es de ellos → agregue el dominio del remitente ORIGINAL (no el de quien reenvía). *El correo no se leyó* → debe estar **no leído** y en la carpeta configurada; el asunto debe cumplir el filtro (si no cumple, se ignora y no deja rastro).

## 7. Integración por SFTP y FTP (Lote 134)

> Dictado de Luis (2026-10-19): conectores **SFTP** y **FTP** para cualquier cliente. Teikem se conecta al servidor del cliente (o al nuestro), baja los archivos nuevos de una carpeta, los pasa por la plantilla de importación de la integración (hash, importador, bandeja) y los **mueve** a «procesados» o «errores» del servidor. **Nunca borra.** Permisos: `integrations.create` + `integrations.create.files`.

**Cómo se configura** (conector **SFTP** o **FTP** al crear la integración):
- **Servidor, puerto** (22 SFTP, 21 FTP) y **usuario**. **Autenticación:** contraseña o, en SFTP, **llave privada** (texto PEM, con su frase si la tiene). La contraseña o la llave se guardan **cifradas** con la llave de la compañía (`Files:MasterKey`) y **no se vuelven a mostrar**: al editar, vacía = se conserva la guardada.
- **Carpeta remota** (p. ej. `/entrada`), **carpeta de procesados** y **carpeta de errores** (dentro de la remota, o una ruta completa que empiece con `/`; se crean si faltan). Procesados vacía = los archivos se quedan donde están y el **hash** evita procesarlos dos veces. Las rutas no admiten «..».
- **SFTP: huella del servidor fijada.** Sin la huella SHA-256 del servidor **no se conecta** (así nunca se habla con un impostor). Con la huella vacía, **Probar conexión** (o una corrida) responde «Falta confirmar la huella del servidor SFTP…» y **muestra la huella que presenta el servidor** (`SHA256:…`): compárela con la que da el proveedor y péguela en el campo. Si después el servidor presenta otra, la conexión se rechaza («La huella del servidor no coincide con la configurada…»).
- **FTP:** **FTPS** (TLS explícito, con validación del certificado) o FTP sin cifrar; sin FTPS el aviso dice «Atención: FTP sin cifrar; usuario, contraseña y archivos viajan en claro.»
- **Qué archivos se toman:** por **tipo** (`csv`, `txt` por omisión) y **nombre** (igual, contiene, empieza, termina, comodín `ordenes-*.csv` o expresión regular), hasta 100 por corrida y en orden de nombre; se ignoran los ocultos (`.x`) y los que se **modificaron hace menos de 10 segundos** (puede estar subiéndose todavía): esperan a la próxima corrida.
- **Horario:** a mano o cada N minutos (1 a 10080), como la carpeta y el correo. **Correr ahora** y **Probar conexión** (`POST /{id}/test-connection`, `integrations.manage`) en la lista; la prueba dice cuántos archivos hay y cuántos están listos para leer, y nunca muestra la contraseña.

**Qué pasa con cada archivo:** se baja, se calcula su hash (el mismo contenido no se procesa dos veces: «Repetido»), pasa por el importador **en nombre de la cuenta de servicio** y los envíos con problemas van a la **bandeja**; el archivo se mueve a «procesados» (o a «errores» si no se pudo procesar). Si no se puede mover, queda donde estaba y el hash lo protege. Si el servidor no responde, la corrida queda *Falló* con «No se pudo conectar con el servidor: …» y no pasa nada más.

| Mensaje (400) | Cuándo |
|---|---|
| «Indique el servidor (host) del conector.» · «El puerto debe estar entre 1 y 65535.» · «Indique el usuario del conector.» | servidor |
| «Indique la contraseña del conector.» · «Indique la llave privada del conector.» · «Autenticación desconocida: X. Use PASSWORD o KEY.» · «La llave privada solo se usa con SFTP.» | secreto |
| «Indique la carpeta remota donde llegan los archivos.» · «Las carpetas no pueden contener «..» ni saltos de línea.» | carpetas |
| «La huella debe ser la SHA-256 del servidor (SHA256:… en base64).» | huella |
| «Condición desconocida: X. Use EQUALS, CONTAINS, STARTS, ENDS, WILDCARD o REGEX.» · «Indique el texto de la condición.» | filtro de nombre |
| «No se pueden guardar contraseñas de conectores: falta la llave maestra del servidor (Files:MasterKey).» | el servidor no tiene la llave maestra |

**Casos frecuentes:** *«Falta confirmar la huella del servidor SFTP»* → use Probar conexión, compare y pegue la huella. *«La huella del servidor no coincide»* → el proveedor la cambió o es otro servidor; confírmelo con el proveedor antes de actualizarla. *«0 listos para leer» con archivos en la carpeta* → no cumplen el tipo o el nombre, o se modificaron hace menos de 10 s. *FTP que lista pero no baja* → modo pasivo bloqueado por un firewall; use SFTP o abra el rango pasivo. *El archivo sigue en la carpeta* → no se pudo mover (permisos de escritura); el hash evita duplicar los envíos, pero conviene dar permiso de escritura.

## 8. API para los clientes (Lote 135)

> Dictado de Luis (2026-10-19): el cliente (o su sistema) puede hablar con Teikem sin pantalla, **solo sobre lo suyo**: sus envíos, consignatarios, perfil y —según los módulos de la compañía— su almacén y sus órdenes de compra y de venta. Se activa con un conector de tipo **API para el cliente** y una o más **llaves**.

**Cómo se configura** (permiso `integrations.create` + el de cada familia; mismo formulario que las demás integraciones, módulo `INTEGRATIONS`):
- En **Sistema → Integraciones / API → Nueva integración** se elige el conector **API para el cliente**. No lleva plantilla, carpeta ni horario. Se escogen los **alcances** (solo se ofrece lo que quien crea puede dar y lo que la compañía tiene contratado). La cuenta de servicio de la integración recibe exactamente los permisos de esos alcances.
- **Cliente:** obligatorio solo si se elige algún alcance de **transporte** (envíos, facturas, consignatarios, perfil): la llave verá únicamente lo de ese cliente (otro cliente «no existe» para ella). Los alcances de almacén, ventas y compras no lo piden.
- En la lista, la acción **Llaves de API** (`integrations.manage`) abre la gestión: **crear** (nombre y límite por minuto opcional), **cambiar el límite**, **revocar** y ver la **bitácora** de cada llave. Hasta **10 llaves activas** por integración.
- **Dónde está:** en la lista de Integraciones, las de tipo «API para el cliente» traen debajo del nombre el botón **«Llaves de API»** (también en el menú de la fila). 
- **El secreto se muestra una sola vez**, al crear la llave (con botones Copiar) y, desde 2026-10-21, junto a un **bloque listo para copiar y pegarle al cliente** con la URL base, la llave, el secreto, los alcances de la integración y los dos comandos de ejemplo (pedir el token y llamar al API); el botón «Copiar todo» lo copia completo. En Teikem solo se guarda su hash SHA-256; después solo se ve la llave `tk_…` y los últimos 4 caracteres. Si se pierde, se revoca y se crea otra.

**Alcances y rutas** (lista cerrada; todo lo demás responde 403 aunque el token sea válido; el único `DELETE` es «quitar un webhook»):

| Alcance | Qué abre |
|---|---|
| `orders:read` | `GET /api/v1/orders`, `/orders/{id}`, `/orders/{id}/delivery`, `/files/{id}` |
| `orders:create` | `POST /api/v1/orders` |
| `invoices:read` | `GET /api/v1/billing/invoices`, `/billing/invoices/{id}` |
| `consignees:read` / `consignees:write` | `GET` / `POST` y `PATCH` sobre `/api/v1/locations` (sin borrar) |
| `client:read` / `client:write` | `GET /api/v1/clients…` (el propio) / `PATCH /clients/{id}/profile` y contactos |
| `warehouse:read` / `warehouse:write` | productos, inventario, recibos y despachos manuales: lectura / crear productos, recibir, despachar, ajustar y transferir |
| `sales:read` / `sales:write` | `/api/v1/sales-orders`: leer / crear, editar y solicitar |
| `purchasing:read` / `purchasing:write` | `/api/v1/purchase-orders`: leer / crear, editar y enviar |
| `webhooks:manage` | `/api/v1/webhooks…`: suscribirse a avisos de su cliente, probar, rotar el secreto y ver sus salidas (ver §9; el único `DELETE` de la API: quitar un webhook) |

Siempre permitidas con cualquier llave: `GET /api/v1/api-auth/me` (quién es la llave) y `GET /api/v1/catalogs…`.

**Cómo se usa** (3 pasos):

```bash
# 1) token (15 minutos): llave y secreto
curl -X POST https://SERVIDOR/api/v1/api-auth/token -H "Content-Type: application/json" \
  -d '{"clientId":"tk_…","clientSecret":"tks_…"}'
# → {"accessToken":"…","tokenType":"Bearer","expiresIn":900,"scopes":["orders:read"],"clientPublicId":"…"}
# 2) llamar con el token
curl https://SERVIDOR/api/v1/orders -H "Authorization: Bearer ACCESS_TOKEN"
# 3) al vencer, pedir otro token con la misma llave
```

- **Límite de uso** por llave (por omisión 60 por minuto; de 1 a 6000): al pasarlo, **429** con `Retry-After` (segundos) y cabeceras `X-RateLimit-Limit` / `X-RateLimit-Remaining`. El intercambio de token también se frena por dirección (30 por minuto).
- **Bitácora** por llave (método, ruta, código y milisegundos), 30 días.
- **Revocar** deja de servir de inmediato (aunque el token siga vivo) y no se puede reactivar. **Apagar la integración** apaga su cuenta de servicio: los tokens vivos dejan de servir.
- Las sesiones de llave de API **no** pasan por la aceptación de documentos legales (no son personas).

| Mensaje | Código | Cuándo |
|---|---|---|
| «Credenciales inválidas.» | 401 | llave o secreto incorrectos, llave revocada o vencida, integración o cuenta apagada (no dice cuál) |
| «Esta llave de API no tiene el alcance «X» que pide esta ruta.» | 403 (`api_scope`) | la ruta existe pero el alcance no está |
| «Esta llave de API no puede usar esta ruta: no forma parte de la API de integraciones.» | 403 (`api_scope`) | ruta fuera de la lista |
| «Superó el límite de uso de la llave; vuelva a intentar en N s.» | 429 | límite por minuto |
| «Indique un nombre para la llave.» · «Máximo 80 caracteres.» | 400 | nombre de la llave |
| «El límite de uso debe estar entre 1 y 6000 solicitudes por minuto.» | 400 | límite |
| «Una integración puede tener hasta 10 llaves activas; revoque alguna.» | 409 | demasiadas llaves |
| «La llave ya está revocada.» | 409 | revocar o editar una revocada |
| «Solo las integraciones de tipo API tienen llaves.» | 400 | llaves en otro conector |
| «Los alcances de transporte de la API necesitan el cliente: la llave solo ve lo de ese cliente.» | 400 | falta el cliente |
| «Una integración de tipo API solo admite alcances de la API.» | 400 | alcances de archivos en una API (y al revés) |

**Casos frecuentes:** *«Mi sistema recibe 403»* → falta el alcance de esa ruta: agréguelo en la integración (se aplica en el siguiente token). *«Veo 429»* → suba el límite de la llave o espacie las llamadas (`Retry-After`). *«Perdí el secreto»* → revoque y cree otra llave. *«La llave ve vacío»* → solo ve lo del cliente de la integración.

## 9. Webhooks de salida y bandeja de salidas (Lote 136)

> Dictado de Luis (2026-10-19): Teikem avisa por HTTP al sistema del cliente cuando pasa algo con lo suyo, con firma, reintentos y una bandeja para lo que no se pudo entregar. Requiere una integración de tipo **API** con **cliente** y el alcance **`webhooks:manage`**.

**Quién y cómo** (módulo `INTEGRATIONS`):
- **El personal** (Sistema → Integraciones / API): la acción **Webhooks** de la integración (`integrations.manage` para crear, probar, rotar, reactivar y quitar; `integrations.view` para ver). Solo aparece en integraciones API con el alcance `webhooks:manage`.
- **El propio cliente**, con su llave: `GET/POST /api/v1/webhooks`, `GET/PATCH/DELETE /api/v1/webhooks/{id}`, `POST /api/v1/webhooks/{id}/test`, `POST /api/v1/webhooks/{id}/rotate-secret`, `GET /api/v1/webhooks/{id}/deliveries`. Solo ve y toca los webhooks de **su** integración. Una persona (no llave) recibe 403 «Esta ruta es para llaves de API; el personal administra los webhooks desde Integraciones.».
- Hasta **10 webhooks** por integración. `DELETE` solo **quita** el webhook (se desactiva y deja de aparecer; el historial queda): es el único `DELETE` de la API.

**Eventos** (`events`, al menos uno): `order.status_changed` (cada cambio de estatus de un envío del cliente; una entrega que salta estatus manda uno por cada estatus), `order.delivered` (el envío se entregó) e `invoice.issued` (se emitió una factura del cliente). Solo salen eventos de **lo del cliente** de la integración. Más `webhook.ping` (la prueba), que no se suscribe.

**Qué recibe el cliente** (POST, `Content-Type: application/json`; cuerpo mínimo: el detalle se consulta por la API con el id):
```json
{"id":"7f0c…","type":"order.delivered","createdAt":"2026-10-19T14:03:11Z",
 "data":{"orderId":"3b1e…","orderNumber":"ORD-000123","status":"DELIVERED","deliveredAt":"2026-10-19T14:03:11Z"}}
```
Cabeceras: `X-Teikem-Event` (tipo), `X-Teikem-Delivery` (el `id` del cuerpo: úselo para no procesar dos veces una repetición), `X-Teikem-Timestamp` (segundos Unix) y `X-Teikem-Signature` = `sha256=` + HMAC-SHA256 en hexadecimal de `«timestamp».«cuerpo»` con el **secreto** del webhook.

**Cómo verificar** (receptor): recalcular la firma con el secreto, compararla en tiempo constante y **rechazar si la marca de tiempo tiene más de 5 minutos** (contra repetición).
```js
const expected = 'sha256=' + crypto.createHmac('sha256', secret).update(`${timestamp}.${rawBody}`).digest('hex')
const ok = crypto.timingSafeEqual(Buffer.from(expected), Buffer.from(signature)) && Math.abs(Date.now()/1000 - Number(timestamp)) < 300
```
Responda **2xx** en menos de **10 segundos**; cualquier otra cosa (o no responder) cuenta como fallo.

**El secreto** (`whsec_…`) sale **una sola vez**, al crear el webhook o al **rotarlo** (el anterior deja de servir ya). Se guarda cifrado; después solo se ve su final.

**Reintentos y bandeja:** un aviso que falla se reintenta a **1 minuto, 5, 30, 2 horas y 12 horas** (6 intentos en total). Agotados, queda **No entregada** en la pestaña **Salidas** (`integrations.inbox`) con el motivo exacto («El servidor respondió 500.», «Tiempo de espera agotado (10 s).», «No se pudo conectar: …»), el cuerpo y los botones **Reintentar** (vuelve a pendiente con los intentos en cero) y **Descartar**. La pestaña muestra el contador de no entregadas. Las salidas se guardan 30 días.

**Suspensión automática:** **5** salidas seguidas sin entregarse suspenden el webhook (deja de generar avisos nuevos); se avisa con un aviso del sistema a quienes se definieron en la integración. Se **reactiva** (personal: **Reactivar**; también editarlo y encenderlo) y mientras esté suspendido no se pueden reintentar sus salidas.

**Seguridad:** la URL debe ser **https**, sin usuario ni contraseña y **pública**: se rechazan `localhost`, nombres sin dominio, `*.local`/`*.internal` y direcciones privadas, de bucle o de enlace local (la conexión revisa además la dirección ya resuelta, contra el rebote por DNS), y nunca se siguen redirecciones. (`Integrations:Webhooks:AllowInsecure` solo se enciende en desarrollo y pruebas.)

| Mensaje | Código | Cuándo |
|---|---|---|
| «Indique la URL a la que se enviarán los avisos.» · «La URL no puede pasar de 500 caracteres.» | 400 | URL |
| «La URL no es válida: debe ser una dirección completa (https://…) sin usuario ni contraseña.» | 400 | URL mal formada |
| «La URL debe usar https.» · «La URL apunta a una dirección interna o local, que no se permite.» | 400 | URL no permitida |
| «Escoja al menos un evento.» · «Evento desconocido: X. Use order.status_changed, order.delivered, invoice.issued.» | 400 | eventos |
| «Los webhooks necesitan una integración con cliente: solo se avisa de lo de ese cliente.» | 400 | integración sin cliente |
| «La integración no tiene el alcance «webhooks:manage».» | 400 | falta el alcance |
| «Solo las integraciones de tipo API tienen webhooks.» | 400 | otro conector |
| «Una integración puede tener hasta 10 webhooks; quite alguno.» | 409 | demasiados |
| «El webhook no está suspendido.» | 409 | reactivar uno que no lo está |
| «Solo se puede reintentar una salida no entregada.» · «El webhook está suspendido: reactívelo antes de reintentar.» | 409 | reintentar |
| «La salida ya fue descartada.» · «La salida ya no está pendiente.» | 409 | descartar |
| «No se pueden guardar secretos de webhooks: falta la llave maestra del servidor (Files:MasterKey).» | 400 | servidor sin llave maestra |
| 404 «Webhook» / «Salida» | 404 | no existe (o es de otra integración, o ya se quitó) |

**Casos frecuentes:** *«No me llegan avisos»* → el webhook está suspendido o apagado, la integración está apagada, el evento no está marcado o el envío es de otro cliente; revise la pestaña Salidas. *«La firma no coincide»* → use el cuerpo **crudo** (sin reformatear) y el secreto vigente (¿lo rotaron?). *«Recibo el mismo aviso dos veces»* → es un reintento (la primera respuesta no llegó a tiempo): descarte por `X-Teikem-Delivery`. *«Probar dice que no se pudo conectar»* → la URL debe ser pública y con https.

**Fuera de este lote:** la salida de **archivos** por evento o por horario (SFTP, correo o API del cliente) necesita el mapeo de salida; llega con el Lote 137.

## 10. Formato del archivo: Excel, JSON y XML (Lote 137)

> Dictado de Luis (2026-10-19): integrar cualquier cosa **por configuración**. Además de CSV, una integración de archivos (carpeta, correo, SFTP, FTP o subida manual) lee **hojas de Excel (.xlsx)** y archivos **JSON** y **XML** con un mapeo por ruta. Todo se convierte al CSV de la **plantilla de importación** de la integración y sigue el camino de siempre: hash (el mismo contenido no se procesa dos veces), importador en nombre de la cuenta de servicio, varias líneas por orden y reglas de «no pieza» (Lote 131), y la bandeja.

**Dónde:** Sistema → Integraciones / API → crear o editar una integración → sección **Formato del archivo** (no aplica a las de tipo API; `integrations.create` / `integrations.manage`).

| Formato | Qué lee |
|---|---|
| **Automático** (por omisión) | Por la extensión: `.xlsx` → Excel, `.json` → JSON, `.xml` → XML, lo demás → CSV. (También reconoce un libro de Excel por su firma.) |
| **CSV** | Como siempre (delimitador y codificación de la plantilla). |
| **Excel (.xlsx)** | Una **hoja**: la que se escriba (sin distinguir mayúsculas) o, vacía, la primera. Cada celda se lee como la vería la persona (enteros sin decimales, ceros a la izquierda según el formato, fechas `aaaa-mm-dd`…). El `.xls` viejo no se admite. |
| **JSON / XML** | Con **mapeo por ruta**: dónde están los envíos, dónde están sus líneas (opcional) y de dónde sale cada columna de la plantilla. |

**Qué archivos toma cada conector:** la **carpeta de entrada** toma las extensiones del formato (Automático: `.csv`, `.txt`, `.xlsx`, `.json`, `.xml`; Excel solo `.xlsx`; JSON solo `.json`; XML solo `.xml`; CSV `.csv` y `.txt`) y deja los demás donde están. En **correo, SFTP y FTP** manda la lista de **tipos de archivo** del conector (por omisión `.csv` y `.txt`): agregue `.xlsx`, `.json` o `.xml` ahí. La **subida manual** acepta cualquiera (el formato lo decide la configuración o la extensión).

**El mapeo por ruta (JSON y XML).** Una ruta es `$.a.b[0].c`:
- `$` = el documento; `.campo` entra a un campo; `[*]` = todos los elementos de una lista; `[0]` = uno. Un solo elemento se trata como una lista de uno (`[*]` sirve con uno o con varios).
- **XML:** la ruta empieza con el nombre del elemento raíz (`$.Pedidos.Orden[*]`); un **atributo** se escribe `@atributo` y el **texto** de un elemento que también tiene atributos, `#text`; los elementos que se repiten forman una lista.
- **Ruta de los envíos** (obligatoria, empieza con `$`): dónde está cada envío. **Ruta de las líneas** (opcional, empieza con `@`, relativa al envío): cada línea es una fila; un envío sin líneas da una fila con lo de la línea vacío.
- **Columnas:** el **nombre** de la columna y la **ruta del valor**, en el **orden que espera la plantilla** (la primera es la columna 1). Con líneas, `@.x` lee de la línea, `^.x` de su orden y `$.x` del documento; sin líneas, `@.x` lee del envío. Un valor es texto, número (`12.5`) o `true`/`false`; un objeto, una lista o un valor ausente dan vacío.

Ejemplo JSON (envíos con líneas): `{"orders":[{"ref":"A-1","to":{"name":"Colmado Uno"},"items":[{"type":"Box","label":"PCS-1"},{"type":"Invoice","label":"INV-1"}]}]}` con ruta de envíos `$.orders[*]`, de líneas `@.items[*]` y columnas `Orden ← ^.ref`, `Tipo ← @.type`, `Label ← @.label`, `Consignatario ← ^.to.name`. Ejemplo XML: `<Pedidos><Orden ref="A-1"><Item tipo="Box"/></Orden></Pedidos>` con `$.Pedidos.Orden[*]`, `@.Item[*]` y columnas `^.@ref`, `@.@tipo`.

**Probar con un archivo** (en la sección): se sube un archivo de muestra y se ve **cómo lo leería la plantilla** (formato detectado, hojas del libro, encabezado y las primeras 20 filas) sin guardar nada ni crear envíos (`POST /api/v1/integrations/format/preview`, multipart: `file`, `format` en JSON, `delimiter`, `hasHeader`).

**Seguridad y límites:** el archivo es entrada no confiable: el XML **no admite DTD ni entidades externas**, el JSON tiene profundidad máxima (64), no se ejecuta nada. Mismos límites que el CSV: **2 MB** y **5.000 filas**.

| Mensaje | Código | Cuándo |
|---|---|---|
| «Formato desconocido: X. Use AUTO, CSV, EXCEL, JSON, XML.» | 400 | formato |
| «El nombre de la hoja no puede pasar de 31 caracteres.» | 400 | hoja |
| «Indique la ruta de los envíos (por ejemplo $.orders[*]).» · «La ruta de los envíos debe empezar con $ (por ejemplo $.orders[*]).» | 400 | ruta de envíos |
| «La ruta de las líneas debe ser relativa al envío: empiece con @ (por ejemplo @.lines[*]).» | 400 | ruta de líneas |
| «La ruta «X» no es válida. Use el formato $.campo.otro[*] (también @.campo o ^.campo).» | 400 | cualquier ruta mal formada |
| «La ruta «^.x» usa ^ (el envío), que solo se puede cuando hay una ruta de líneas.» | 400 | `^` sin líneas |
| «Agregue al menos una columna al mapeo.» · «El mapeo admite hasta 60 columnas.» · «Cada columna necesita un nombre.» · «Hay dos columnas llamadas «X».» | 400 | columnas |
| «Una integración de tipo API no tiene formato de archivo.» | 400 | formato en una integración API |

Errores al **procesar un archivo** (el archivo queda anotado como «Con error» y en la **bandeja** con el motivo «Archivo X: …»; no se crea nada):

| Motivo | Cuándo |
|---|---|
| «El archivo es JSON, pero la integración no tiene el mapeo de rutas: configure «Formato del archivo».» (igual con XML) | llegó un .json/.xml a una integración sin mapeo |
| «No se pudo leer el archivo JSON: …» / «No se pudo leer el archivo XML: …» | archivo roto o mal formado |
| «El archivo XML trae una declaración DTD, que no se admite.» | XML con DTD |
| «La ruta de los envíos «$.orders[*]» no encontró ningún registro en el archivo.» | la ruta no encuentra envíos (¿mapeo equivocado o archivo vacío?) |
| «El libro no tiene la hoja 'X'.» | la hoja escrita no existe |
| «El archivo no es un libro de Excel (.xlsx).» | formato Excel con un archivo que no lo es (o un .xls viejo) |
| «El archivo supera el límite de 5.000 filas o 2 MB.» | límites |

**Casos frecuentes:** *«No encontró ningún registro»* → pruebe el archivo de muestra: en XML la ruta empieza con el nombre del elemento raíz (`$.Pedidos…`). *«Un envío sale con la descripción vacía»* → revise que la columna esté en la posición que usa la plantilla. *«Mi integración de correo/SFTP ignora los .xlsx»* → agregue `.xlsx` a los tipos de archivo del conector.

## 11. Integraciones de salida: archivos por SFTP/FTP y API saliente (Lote 137b)

> Dictado de Luis (2026-10-19): Teikem también **manda** datos, por configuración: los envíos del cliente que llegan a un estatus (p. ej. «Entregada») salen en un archivo (CSV, JSON o XML) por **SFTP o FTP**, o por la **API del sistema del cliente** (HTTP genérico). Es la salida de archivos por evento u horario que faltaba (los webhooks, §9, cubren el aviso mínimo por evento).

**Cómo se crea** (Sistema → Integraciones / API → Nueva integración → **Dirección: Salida**; `integrations.create` + `integrations.create.files`, alcance **«Salida de envíos por archivo o API»**, módulo de transporte y un **cliente**: solo salen envíos de ese cliente). Conectores de salida: **API saliente (HTTP)**, **SFTP** y **FTP** (el correo no es de salida).

**Qué se manda** (sección «Qué se manda»):
- **Estatus que disparan la salida** (uno o varios): Sometida, Recogida, Sin asignar, Asignada, En tránsito, Entregada, Fallida, Cancelada, En almacén, Devuelta. Cada orden que **llega** a uno de ellos entra en la cola (un envío no entra dos veces mientras espera).
- **Cuándo sale:** **Al instante** (cada envío por separado, en menos de un minuto por el trabajo «Salida de integraciones») o **Por horario** (cada N minutos, 1 a 10080, un solo envío con todas las que llegaron desde el anterior; hasta 1.000 envíos por envío).
- **Formato:** **CSV** (delimitador coma, punto y coma o tabulador; encabezado y fin de línea `\r\n`), **JSON** (`{"orders":[{…}]}`; los números salen como números, las fechas en ISO 8601 UTC y lo que falta como `null`) o **XML** (`<orders><order><campo>…</campo></order></orders>`, UTF-8 sin BOM).
- **Columnas:** el **nombre** (en JSON/XML debe empezar con letra o guion bajo y llevar letras, números, guion o guion bajo) y el **campo del envío**: número de envío, conduce, factura del cliente, estatus (código y nombre), fecha de creación, fecha prometida, piezas, peso, monto COD, flete cotizado, notas, consignatario, dirección, pueblo y código postal de entrega, fecha y hora de entrega, quién recibió y notas de la entrega. Por omisión: envío, estatus, fecha de entrega y quién recibió. Hasta 40 columnas.
- **Nombre del archivo** (SFTP/FTP): admite `{date}` (aaaammdd), `{time}` (hhmmss), `{id}` (número del envío) y `{ext}`; por omisión `ordenes-{date}-{time}.{ext}`. El archivo se sube con nombre provisional `.part` y se renombra al terminar (quien lo recoge nunca lo ve a medias) y **nunca pisa** uno existente (le pone la fecha por delante). La carpeta de destino es la «carpeta remota» del conector (se crea si falta); SFTP exige la huella del servidor fijada, como en la entrada (§7).
- **API saliente (HTTP):** URL (**https y pública**, como los webhooks), método **POST** o **PUT**, autenticación **Ninguna**, **Token (Bearer)**, **Usuario y contraseña (Basic)** o **Encabezado con una llave**, y hasta **10 encabezados** adicionales (no Authorization, Host ni Content-Type). El token, la contraseña o el valor del encabezado se guardan **cifrados** y no se vuelven a mostrar. Cada llamada lleva `Idempotency-Key: teikem-output-{id}` y `X-Teikem-Output`; responda **2xx en menos de 10 s**; no se siguen redirecciones.

**Acciones de la lista:** **Enviar ahora** (arma la cola y manda lo que toque), **Vista previa** (ejemplo con los últimos envíos del cliente en esos estatus —si no hay, las últimas del cliente—; no manda nada), **Probar conexión** (SFTP/FTP: conecta y lista la carpeta; HTTP: manda un cuerpo de prueba sin envíos y dice «La API respondió 200.»), **Historial** y **Editar**. Una integración de salida **no recibe archivos** y apagada no manda.

**Reintentos y bandeja:** un envío que falla se reintenta a **1 minuto, 5, 30, 2 h y 12 h** (6 intentos); agotados queda **No entregado** en **Salidas → «Salidas de archivos y API»** con el motivo exacto («El servidor respondió 500.», «Tiempo de espera agotado (10 s).», «No se pudo conectar: …»), el contenido y **Reintentar** / **Descartar**. El contador de la pestaña suma webhooks y envíos. Se guardan 30 días. Si un destino falla 3 veces seguidas en una vuelta, el resto espera a la siguiente.

| Mensaje | Código | Cuándo |
|---|---|---|
| «Indique qué se manda (estatus, formato y columnas).» | 400 | falta la salida |
| «Escoja al menos un estatus que dispare la salida.» · «Estatus desconocido: X.» | 400 | estatus |
| «Disparador desconocido: X. Use EVENT o SCHEDULE.» · «La salida por horario necesita un intervalo (minutos).» | 400 | cuándo sale |
| «Formato de salida desconocido: X. Use CSV, JSON o XML.» · «El delimitador debe ser coma, punto y coma o tabulador.» | 400 | formato |
| «Agregue al menos una columna a la salida.» · «La salida admite hasta 40 columnas.» · «Cada columna necesita un nombre.» · «Hay dos columnas llamadas «X».» · «Campo desconocido: X.» | 400 | columnas |
| «El nombre de la columna «X» debe empezar con una letra o guion bajo y llevar solo letras, números, guion y guion bajo (para JSON y XML).» | 400 | nombre de columna en JSON/XML |
| «El nombre del archivo no puede llevar carpetas ni saltos de línea.» · «Marcador desconocido en el nombre del archivo: {x}. Use {date}, {time}, {id} o {ext}.» | 400 | nombre del archivo |
| «Indique la configuración de la API (URL, método y autenticación).» · «Método desconocido: X. Use POST o PUT.» · «Autenticación desconocida: X. Use NONE, BEARER, BASIC o HEADER.» | 400 | API saliente |
| «Indique el usuario (BASIC) o el nombre del encabezado (HEADER) de la autenticación.» · «Indique el token, la contraseña o el valor del encabezado de la autenticación.» | 400 | autenticación |
| «El encabezado «X» no es válido (nombre no permitido o valor con saltos de línea o de más de 500 caracteres).» · «Se admiten hasta 10 encabezados adicionales.» | 400 | encabezados |
| «La URL debe usar https.» · «La URL apunta a una dirección interna o local, que no se permite.» | 400 | URL (igual que los webhooks) |
| «Una integración de salida solo admite el alcance de salida de envíos.» · «Una integración de entrada no admite el alcance de salida de envíos.» | 400 | alcances |
| «Una integración de salida usa SFTP, FTP o HTTP.» · «El conector HTTP es solo para integraciones de salida.» | 400 | conector |
| «Indique el cliente de los envíos de esta integración.» | 400 | sin cliente |
| «Esta integración es de salida: no recibe archivos.» | 400 | subir un archivo |
| «No hay envíos pendientes de enviar.» | — (mensaje de la corrida) | «Enviar ahora» sin cola |
| «Solo se puede reintentar un envío no entregado.» · «El envío ya fue descartado.» · «El envío ya se entregó.» | 409 | bandeja de salidas |
| «La salida supera el tamaño permitido (2 MB); use una salida más frecuente o menos columnas.» | (envío no entregado) | contenido demasiado grande |

**Casos frecuentes:** *«No sale nada»* → la integración está apagada, el estatus no es el elegido, el envío es de otro cliente, o llegó al estatus **antes** de crear la integración (solo salen las que llegan después). *«Por horario no manda»* → el trabajo de integraciones por horario corre cada minuto; use «Enviar ahora» para probar. *«No entregado: No se pudo conectar»* → revise URL, firewall y, en SFTP, la huella. *«El archivo llegó dos veces»* → es un reintento tras no recibir la respuesta: use el `Idempotency-Key` (API) o el número del envío en el nombre del archivo.

## 14. Pantalla «Plantillas» (desde Integraciones, 2026-10-21)

Dictado de Luis: ver, crear, editar, desactivar y reactivar las plantillas de importación **sin pasar por el asistente de Importar**. Las mismas plantillas sirven a ese asistente y a las integraciones.

- **Dónde:** botón **Plantillas** arriba en Sistema → Integraciones (`/system/templates`; no es un ítem del menú, que se queda como la maqueta). Entra quien tenga `integrations.view` y el módulo Integraciones.
- **Pestañas por entidad:** **Envíos** (órdenes de transporte; ver y cambiar exige `orders.edit`), **Consignatarios** (exige `locations.create`), **Productos** (`inventory.manage`), **Proveedores** (`purchasing.manage`), **Almacenes** y **Posiciones** (`warehouse.manage`). Sin el permiso de la entidad la pestaña solo dice cuál hace falta: «Para ver y cambiar estas plantillas necesitas el permiso {perm}.».
- **Lista:** nombre, cliente (o «Todos los clientes»), el resumen «columna → campo», cuántas columnas y estado (Activa/Inactiva). Las activas salen primero y luego por nombre; las inactivas se muestran siempre.
- **Acciones:** *Nueva plantilla* y *Editar* abren el mismo editor que ya usa cada asistente de Importar (para consignatarios con variantes, uniones de columnas y cliente por defecto); *Desactivar* y *Reactivar* no borran nada. Los mensajes de error son los del servidor de cada editor (ver manual 03 §7 y el de consignatarios).
- **Desde la integración:** al crear o editar una integración de entrada (carpeta, correo, SFTP/FTP o subida manual) hay un botón **«Nueva plantilla»** bajo el selector de plantilla (necesita `orders.edit`): abre el mismo editor, y al guardar la plantilla queda elegida en la integración, sin salir de ella.
- **Todavía no:** plantillas de las demás entidades (órdenes de venta, clientes, choferes, recibos, despachos, ajustes). Siguen en el plan P11 (productos, proveedores, almacenes y posiciones ya están, ver manual 06 §1b, §2b y §8b).

## 15. Salida de productos por Integración con la plantilla (2026-10-21)

Dictado de Luis: la plantilla de la entidad (columna → campo) sirve también para **sacar** los productos por SFTP, FTP o API saliente, **por horario y al cambiar**. Es una integración de **salida** más, con «¿Qué se manda?» = **Productos (catálogo)**.

- **Cómo se crea:** Integraciones → Nueva integración → Dirección **Salida** → «¿Qué se manda?» **Productos (catálogo)** → plantilla de productos (la misma con la que se importan; «Nueva plantilla» la crea ahí mismo) → conector (SFTP, FTP o HTTP), cuándo sale, formato (CSV, JSON o XML), nombre del archivo y destino. **No lleva cliente ni estatus.** Alcance: `files.catalog_export` («Salida de productos por archivo o API», pide `inventory.view` y el módulo de Inventario con lotes y series; la familia Archivos como las demás integraciones de archivos).
- **Qué sale:** las **columnas de la plantilla, en su orden** (el encabezado es el nombre del campo: `sku`, `name`, `salePrice`…). Todos los campos de la importación de productos están disponibles (incluidos código del cliente dueño, unidad base, seguimiento, empaque y rotación). JSON: `{"products":[…]}`; XML: `<products><product>…`.
- **Al cambiar:** al **crear, editar, desactivar o reactivar** un producto, queda anotado para cada integración de salida de productos encendida y sale en su siguiente envío. Un producto que cambia varias veces antes de salir va **una sola vez**. Con disparador **Al instante** sale un envío por producto (en menos de un minuto); con **Por horario** sale una tanda de hasta 1.000 productos cuando toca el horario.
- **Enviar todo el catálogo:** acción **«Enviar todo el catálogo»** (`POST /api/v1/integrations/{publicId}/send-all`, `integrations.manage`) en la fila de la integración: anota **todos** los productos y corre la integración ahora (carga inicial o para volver a sincronizar). Una corrida manda hasta 5.000 productos; lo que sobre sale en la vuelta siguiente.
- **Vista previa y prueba:** «Vista previa» (al editar) arma el archivo con los primeros 3 productos; «Probar» de la API saliente funciona igual que en las de envíos. Reintentos, bitácora y bandeja de «Salidas» son los de siempre (§11).
- **Solo salen cambios hechos después de crear la integración** (como en las de envíos); para lo que ya existe, «Enviar todo el catálogo».
- **Límite:** un envío no pasa de 2 MB (error «La salida supera el tamaño permitido (2 MB); use una salida más frecuente o menos columnas.»).

| Mensaje | HTTP | Qué hacer |
|---|---|---|
| «Escoja la plantilla de la entidad: dice qué columna lleva cada campo.» | 400 | Falta la plantilla de productos (o no tiene columnas conocidas). |
| «Entidad desconocida para la salida: X. Use PRODUCT.» | 400 | `entityKind` inválido (por ahora solo PRODUCT). |
| «Esta integración no manda un catálogo.» | 400 | «Enviar todo» solo existe en las integraciones de productos. |
| «Una integración de salida solo admite el alcance de salida de envíos (o el de salida de productos si manda un catálogo).» | 400 | Cada salida admite su alcance. |
| «Una integración de entrada no admite el alcance de salida de envíos ni de productos.» | 400 | |
| «La integración está apagada; enciéndala para correrla.» | 422 | Encienda la integración. |

**Casos frecuentes:** *«Cambié un producto y no salió»* → la integración debe estar encendida, ser de Productos y haberse creado antes del cambio; con horario, sale cuando toca. *«Quiero todo otra vez»* → «Enviar todo el catálogo». *«Faltan columnas»* → agréguelas a la plantilla de productos (Integraciones → Plantillas): la salida usa la plantilla tal como está en cada envío.

## 16. Avisos de llegada y de salida desde un documento (2026-10-23)

Un lector de una integración por correo puede mandar lo que lee a un **aviso de llegada** (lo que entra al almacén, `LLE-#####`) o a un **aviso de salida** (lo que sale, `SAL-#####`), además de a una orden de transporte. Sirve para documentos que el cliente manda por correo, como el «Despacho de Mercancía» o el «Envío de Mercancía» de Axiscare. Quien puede: `integrations.create` o `integrations.manage` para armar la integración; los avisos se crean a nombre de la cuenta de servicio de la integración.

**Cómo se arma (pantalla Integraciones → correo → Lectores).**
1. Cada tipo de documento es **un lector** de la misma integración. Se distinguen por una condición sobre el **texto del documento** (por ejemplo, que contenga «Despacho de Mercancia»), no por el asunto: documentos con la misma plantilla y distinto título cada uno va por su camino.
2. Los botones **«Usar un modelo»** (junto a «Agregar lector») agregan uno ya armado: *Despacho de mercancía → aviso de salida*, *Envío de mercancía → aviso de llegada* y *Factura de Drive Medical → aviso de llegada*. Después escoja el **cliente** del lector (el dueño de la mercancía).
3. La integración sigue pidiendo una plantilla de importación: use una sin fila de encabezado y con coma (el lector entrega los datos por posición de columna).
4. Para armar un lector propio, la columna de cada campo es: **1** referencia, **2** producto (el SKU del cliente, tal como aparece en el documento), **3** cantidad, **4** destino (solo salida), **5** fecha (la requerida en salida, la esperada en llegada; MM/dd/yyyy o yyyy-MM-dd) y **6** lote (solo llegada).

**Qué hace.**
- Un renglón del documento es una línea del aviso. Los renglones con la misma referencia, destino y fecha forman **un solo aviso** (un despacho con cuatro destinos crea cuatro avisos de salida). Un mismo producto repetido se suma.
- Las cantidades se leen por su número: «400bx» es 400, «1,430» es 1430. En la hoja de Axiscare la cantidad es la de «CANTIDAD EACH» (unidades), no la de paletas.
- El almacén es el único activo de la compañía; si hay varios, el aviso no se crea y se explica en la bandeja.
- **Nunca se crea un aviso a medias:** si una línea del documento no se puede leer (producto que no existe para ese cliente, cantidad ilegible), **ningún** aviso de ese documento se crea y todo queda en la bandeja con el número de fila y el motivo.
- El mismo PDF (mismo contenido) no se procesa dos veces.

**Lectura de PDF escaneados (OCR).** Los PDF con texto real se leen directo; las páginas escaneadas pasan por OCR a 288 ppp (escala 4) y con dos modos de página, quedándose con el que reconoce más. La decisión es **por página**: un PDF con unas páginas de texto y otras escaneadas lee cada una a su manera. Un escaneo con rayas o letra manuscrita puede salir con errores; por eso lo ilegible va a la bandeja en lugar de adivinarse.

**Limitaciones conocidas.** (1) En el despacho, el destino que se guarda es la primera línea del bloque de dirección (el nombre del destinatario), no la dirección completa. (2) Si en la hoja solo hay un número después del ítem, no se sabe si es de «cantidad each» o de «paletas»: se toma como cantidad. (3) Las facturas escaneadas de Drive Medical traen cantidades encerradas a mano que el OCR no siempre lee: esas líneas mandan el documento entero a la bandeja.

## 17. Documentos con varias órdenes: lectores por registro (2026-10-23)

Algunos documentos traen **una orden por página** y, antes de la primera, un encabezado común (la hoja de ruta de CMS: ruta, chofer y fecha; luego una orden por parada). Un lector se vuelve **por registro** con el campo **«Texto que empieza cada registro»** (por ejemplo `Order #:`): cada línea del documento que lo contiene empieza una orden nueva, y esa orden se lee con los campos del lector hasta la siguiente.

- **Campo «del encabezado»:** un campo marcado así se lee una sola vez de lo que viene antes del primer registro (la ruta, el chofer) y vale para todas las órdenes.
- **«Unir al anterior con»:** si dos campos van a la **misma columna**, el segundo se agrega a continuación del primero con ese texto en medio (por ejemplo ` | `). Sirve para juntar varias notas en la columna Notas.
- **Transformación «Junta espacios y tabuladores»:** un OCR parte «ERD Pick Up 1» en celdas; con esta transformación vuelve a un solo texto.
- Un registro al que le falta un campo obligatorio lo dice por su número («Registro 2: …») y todo el documento va a la bandeja; si el texto de inicio no aparece en el documento, el motivo es «No se encontró ningún registro en el documento (el texto de inicio de registro no aparece).».

**Modelo «Hoja de ruta de CMS → órdenes de recogida».** Se agrega con **Usar un modelo**. Condiciones: el documento contiene «ROUTE SHEET #» y «Order Entry Date». Cada orden del documento es una **recogida en la casa del paciente que se lleva a CMS**:

- La **dirección del paciente es la parada de RECOGIDA** de la orden y el **consignatario es CMS**, una dirección fija que se configura en la plantilla (columna «consigneeCode» con valor **por defecto** = el código del consignatario de CMS, creado antes en el directorio del cliente). Si el paciente ya existe en el directorio del cliente (mismo nombre y dirección) se reutiliza; si no, se crea.
- La **ruta** (el nombre del chofer de la hoja, por ejemplo «ERD Pick Up 1») es una **zona de despacho**: cree en Flota → Zonas una zona cuyo **código o nombre** sea exactamente ese texto y asígnela a los choferes que la trabajan (un chofer puede cubrir varias zonas). La zona del envío es esa y **no** la que saldría del ZIP. Una ruta que no existe como zona manda la orden a la bandeja («La zona o ruta «…» no existe o está inactiva en la compañía.»): cree la zona y reprocese.
- El **número de orden y el de envío** son el **Order #** de CMS. Para eso el cliente de CMS debe tener activo **«El cliente asigna el número de envío»**; si no, la fila dice «El número de envío lo asigna Teikem para este cliente.».
- El **tipo de servicio** (el que lleva la recogida) se pone también como valor por defecto de la plantilla.

Columnas de la plantilla de importación (sin encabezado, coma, **varias líneas por envío** activado): **1** orderNumber, **2** waybillNumber, **3** pickupName (el paciente), **4** pickupLine1 (dirección), **5** pickupCity (pueblo), **6** pickupPostalCode (ZIP), **7** contactPhone (teléfono del paciente), **8** requestedDate (fecha de entrega), **9** notes, **10** zone (la ruta) y **11** description (la pieza). Cada equipo «Pick Up» es una línea de la orden (una pieza); la línea «New DME … NOTES» no es pieza: su texto se une a las instrucciones de entrega en las **notas**. La cantidad del OCR no se usa (se lee mal: un «1» sale «4»). Los datos de salud del documento (HIC, fecha de nacimiento, MT ID) **no se leen**: solo entra lo que el lector declara.

## 18. Cerrar órdenes por documento: la cola de propuestas (2026-10-23)

Los **finales de un flujo no se hacen solos**. Un lector con destino **«Cierre de órdenes (propuesta)»** lee de un documento qué órdenes quedaron resueltas (la hoja «Ruta completada» de CMS, por ejemplo) y, por cada orden, deja una **propuesta**: «llevar la orden X a Recogida porque este documento lo dice». Una persona la **confirma** o la **rechaza** en **Integraciones → Propuestas**. Permiso: **integrations.inbox** (el mismo de la bandeja). La pestaña muestra entre paréntesis cuántas esperan.

**Cómo se arma.** En el lector: destino «Cierre de órdenes (propuesta)», **Qué hace con lo que encuentra** (**Sugerir**, por omisión: una persona confirma; **Automático**: cierra la orden en el acto, a nombre de la cuenta de servicio de la integración, y se usa solo cuando ese lector ya demostró que acierta) y **Llevar la orden a** (**Recogida** o **Entregada**). La columna **1** del lector es la clave con que el documento nombra la orden (en CMS, el **Referral #**, que es el **Order #** de la orden); las demás columnas son evidencia. Hay un modelo listo: **Usar un modelo → Ruta completada de CMS → cerrar recogidas (propuesta)**; solo toma los renglones de tipo **PU** (las líneas de notas no cuentan).

**Cómo encuentra la orden.** Por su número de orden o de envío, entre las órdenes **activas del cliente** del lector. Una clave sin orden va a la **bandeja** («No hay una orden «X» del cliente …»). Una orden con una propuesta por confirmar, confirmada o aplicada para ese mismo estatus **no genera otra** (el mismo documento dos veces no duplica propuestas). Varios renglones del documento con la misma clave son **una** propuesta.

**Qué pasa al confirmar.**
- **Recogida:** la orden pasa por el pipeline de estatus (saltando los intermedios que falten) hasta «Recogida» y queda la bitácora «Cierre por documento: …». Si la orden ya estaba en «Recogida» o más adelante, no se toca y la propuesta queda **Confirmada** con «La orden ya estaba en ese estatus.».
- **Entregada:** se registra la entrega con el comprobante («Según documento: …»), como la entrega registrada desde la web.
- La orden cambia **a nombre de quien confirma** y queda guardado quién, cuándo y su nota. Si la orden **ya no lo permite** (cancelada, en un estatus final, el estatus de cierre no está habilitado…), la propuesta queda **Fallida** con el motivo y ya no se puede confirmar otra vez.
- **Rechazar** deja la orden como estaba.
- En modo **Automático** la propuesta nace **Aplicada** (o **Fallida**, y además va a la bandeja con el motivo).

**Estados:** Por confirmar (PENDING), Confirmada (CONFIRMED), Aplicada (APPLIED, la hizo el lector automático), Rechazada (REJECTED) y Fallida (FAILED). API: `GET /api/v1/integrations/proposals?status=`, `POST …/proposals/{id}/confirm` y `…/reject` (cuerpo opcional `{ "note": "…" }`); 409 «La propuesta ya se resolvió.» si ya tenía decisión.
