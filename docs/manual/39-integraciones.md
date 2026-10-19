# 39 — Integraciones (Lote 132, P11)

> Dictado de Luis (2026-10-19). Una integración mueve datos entre Teikem y otro sistema **por configuración, sin programar cada vez**. Este lote trae las integraciones de **entrada** por **carpeta** y por **subida manual**; correo, SFTP, API y webhooks llegan en los lotes siguientes (ver `docs/p11-diseno-integraciones.md`).

## 1. Qué es y quién puede
- **Módulo** `INTEGRATIONS` (apagado; se enciende por compañía en Sistema → Módulos). Pantalla: **Sistema → Integraciones / API** (`/system/integrations`).
- **Permisos:** `integrations.view` (ver y el historial) · `integrations.create` (crear; base) **más** el de cada familia de alcances: `integrations.create.files` (carpeta, SFTP, correo), `.transport` y `.warehouse` (llegan con la API) · `integrations.manage` (editar, apagar, correr, subir archivo) · `integrations.inbox` (la bandeja). TenantAdmin los trae todos; el administrador se los da a los roles que quiera.
- **Cuenta de servicio propia por integración:** al crear la integración se crea un usuario de clase SERVICE (no inicia sesión nunca) con un rol «Integración: nombre» que solo trae los permisos de los alcances elegidos. Lo que entra queda **atribuido a esa cuenta**, no a quien configuró. **Quien crea no puede dar más de lo que tiene:** solo se le ofrecen los alcances de familias que él puede crear, de módulos que la compañía tiene y cuyos permisos él posee.

## 2. Crear y editar una integración
Pantalla: **Nueva integración** (nombre, descripción, cliente, plantilla de importación, conector, horario, alcances, a quién avisar y retención) · API: `POST /api/v1/integrations` (`integrations.create`), `PATCH /{publicId}` (`integrations.manage`), `GET /options`, `GET`, `GET /{publicId}`, `POST /{publicId}/deactivate` y `/reactivate` (apagar y prender: la cuenta de servicio, su rol y la regla de avisos siguen a la integración; nunca se borra).
- **Cliente y plantilla:** las órdenes que entran se crean para ese cliente con la plantilla de importación elegida (la misma de Órdenes → Importar; con **varias líneas por orden** y reglas de «no pieza» del capítulo 03 §7.5). Como la orden la numera el archivo, el cliente debe tener marcado que numera sus órdenes.
- **Conector:** **Carpeta de entrada** (el nombre de la carpeta, solo letras, números, guion y guion bajo; los `.csv` y `.txt` que se dejen ahí se procesan y pasan a `procesados/` o `errores/`; **nunca se borran**) o **Subida manual** (el archivo se sube desde la pantalla). La carpeta vive en `Integrations:FolderRoot/{compañía}/{carpeta}`.
- **Horario:** solo a mano o cada N minutos (1 a 10080; la carpeta se revisa sola; una subida manual no se programa).
- **A quién avisar:** usuarios de la compañía + **«Incluirme»**. Cada corrida con algo que reportar (órdenes creadas, filas a la bandeja o una falla) manda un **aviso del sistema** (campanita y correo) a esas personas; no se avisa a quien la creó si no lo marcó.
- **Retención:** días que se guardan los datos crudos de la bandeja (30 por omisión, 1 a 365); pasado el plazo se purga el detalle y queda el motivo.

| Mensaje (400 salvo que se diga) | Cuándo |
|---|---|
| «El nombre de la integración es obligatorio.» · «Máximo 120 caracteres.» | nombre vacío o largo |
| «Ya existe una integración con ese nombre.» (409) | nombre repetido |
| «Escoja al menos un alcance para la integración.» · «El alcance 'X' no existe.» | alcances |
| «No puede dar el alcance «X»: le falta el permiso 'integrations.create.files'.» (400) · «…usted no tiene los permisos …» · «La compañía no tiene el módulo que necesita «X».» | sin escalada de privilegios |
| «Indique el cliente de las órdenes de esta integración.» · «Escoja la plantilla de importación con la que se leen los archivos.» | cliente / plantilla |
| «Ese tipo de conector todavía no está disponible.» · «Tipo de conector desconocido: X.» | conector |
| «Indique el nombre de la carpeta de entrada.» · «El nombre de la carpeta solo admite letras, números, guion y guion bajo (sin rutas).» | carpeta |
| «El intervalo debe estar entre 60 y 604800 segundos.» · «Una integración de subida manual no se programa por horario.» | horario |
| «Los días de retención deben estar entre 1 y 365.» · «Uno de los usuarios a avisar no existe o está inactivo.» | retención / avisos |

## 3. Correr una integración
- **Carpeta:** por horario (el trabajo programado «Integraciones por horario», cada minuto) o con **Correr ahora** (`POST /{publicId}/run`, `integrations.manage`): lee los archivos nuevos de la carpeta. **409** «La integración ya se está ejecutando; espere a que termine.» · **422** «La integración está apagada; enciéndala para correrla.»
- **Subida manual** (cualquier integración): **Subir archivo** (`POST /{publicId}/upload`, multipart, hasta 4 MB): se procesa igual.
- **Cada archivo:** se calcula su **hash SHA-256**; si el mismo contenido ya se procesó, queda «Repetido» y no se vuelve a procesar. Se lee (UTF-8; si no, Latin-1 de Excel), pasa por el importador **en nombre de la cuenta de servicio**, las órdenes buenas se crean y las demás van a la **bandeja**. Un archivo vacío o más grande del límite (`Integrations:MaxFileMegabytes`, 2) queda «Con error».
- **Resultado de la corrida:** *Bien*, *Con pendientes* (algo fue a la bandeja) o *Falló*; con archivos leídos, órdenes creadas y enviadas a la bandeja. **Historial:** `GET /{publicId}/runs` (hasta 100) con sus archivos.

## 4. La bandeja (`integrations.inbox`)
Lo que entró y no se pudo procesar solo. **Una orden con problemas nunca se crea a medias.** Cada ítem trae el **motivo exacto** (el mismo mensaje de la vista previa de importación: «La orden no trae ninguna pieza…», «El número de pieza X se repite…», «Fila 4 · …») y los **datos tal como llegaron**.
- **Pantalla:** pestaña **Bandeja** (con el contador de pendientes) con filtros por integración y estatus; **Corregir** (las mismas celdas del archivo, tal como llegaron), **Reprocesar**, **Descartar** (con nota), **Reprocesar todo** de una integración y **Descargar pendientes (CSV)**.
- **API:** `GET /inbox` · `GET /inbox/{id}` · `PATCH /inbox/{id}` (corregir celdas: mismas filas y columnas) · `POST /inbox/{id}/reprocess` · `POST /inbox/{id}/discard` · `POST /{publicId}/inbox/reprocess-all` (hasta 200) · `GET /inbox/export` (CSV).
- **Reprocesar** vuelve a pasar el ítem por el importador: si ya cuadra nace la orden y el ítem queda *Reprocesado* (con el enlace a la orden); si no, sigue *Pendiente* con el motivo nuevo. Errores: **409** «Este ítem ya se resolvió (reprocesado o descartado).» · **409** «Los datos de este ítem ya no se guardan (pasó la retención o el archivo no se pudo leer)…» · **400** «Las filas no coinciden con las del ítem…» · «Cada fila debe traer la misma cantidad de celdas que tenía.»
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
  - **Transformación** opcional: mayúsculas, solo dígitos (teléfonos) o monto (quita `$` y comas). **Obligatorio:** si no se encuentra, el correo va a la bandeja con «No se encontró el campo obligatorio de la columna N (…).»
- **Repetidos:** el mismo contenido (hash del adjunto o del cuerpo leído) no se procesa dos veces; el número de orden repetido ya lo rechaza el importador.

**Probar sin buzón:** en la lista, **Probar conexión** (`POST /{id}/email/test`) comprueba servidor, usuario, contraseña y carpeta y dice cuántos correos sin leer hay (sin tocarlos; el mensaje nunca trae la contraseña). Y **Subir archivo** acepta un **`.eml`**: se procesa EXACTAMENTE igual que uno recibido por IMAP (mismas reglas de remitente, asunto, adjuntos y cuerpo), así que se puede probar la configuración antes de apuntar el buzón.

**Correr:** por horario (cada N minutos) o **Correr ahora**. Si el buzón no responde, la corrida queda *Falló* con «No se pudo conectar con el buzón: …» y no pasa nada más (los correos siguen sin leer).

| Mensaje (400) | Cuándo |
|---|---|
| «Indique el servidor del buzón (IMAP).» · «El puerto debe estar entre 1 y 65535.» · «Indique el usuario del buzón.» · «Indique la contraseña del buzón.» · «Indique la carpeta del buzón que se lee (p. ej. INBOX).» | buzón |
| «No se pueden guardar contraseñas de conectores: falta la llave maestra del servidor (Files:MasterKey).» | el servidor no tiene la llave maestra |
| «Condición desconocida: X. Use EQUALS, CONTAINS, STARTS, ENDS, WILDCARD o REGEX.» · «Indique el texto de la condición.» · «La expresión regular 'X' no es válida.» | asunto y nombre del adjunto |
| «El remitente permitido 'X' no es un correo ni un dominio válido.» · «Máximo 50 remitentes permitidos.» | remitentes |
| «Modo de adjuntos desconocido: X. Use ALL, FIRST o BY_NAME.» · «Con «por nombre» indique la condición del nombre del adjunto.» | adjuntos |
| «Con «leer el cuerpo» indique al menos un campo a leer.» · «La columna debe estar entre 1 y 500.» · «Tipo de campo desconocido: X. Use LABEL, REGEX o BLOCK.» · «Indique la etiqueta que se busca en el correo.» · «Indique la expresión regular.» · «Un bloque necesita el texto de inicio y el de fin.» · «Un bloque necesita al menos una columna (posición y número de columna del renglón).» · «Transformación desconocida: X. Use NONE, UPPER, DIGITS o MONEY.» | campos del cuerpo |

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

**Qué pasa con cada archivo:** se baja, se calcula su hash (el mismo contenido no se procesa dos veces: «Repetido»), pasa por el importador **en nombre de la cuenta de servicio** y las órdenes con problemas van a la **bandeja**; el archivo se mueve a «procesados» (o a «errores» si no se pudo procesar). Si no se puede mover, queda donde estaba y el hash lo protege. Si el servidor no responde, la corrida queda *Falló* con «No se pudo conectar con el servidor: …» y no pasa nada más.

| Mensaje (400) | Cuándo |
|---|---|
| «Indique el servidor (host) del conector.» · «El puerto debe estar entre 1 y 65535.» · «Indique el usuario del conector.» | servidor |
| «Indique la contraseña del conector.» · «Indique la llave privada del conector.» · «Autenticación desconocida: X. Use PASSWORD o KEY.» · «La llave privada solo se usa con SFTP.» | secreto |
| «Indique la carpeta remota donde llegan los archivos.» · «Las carpetas no pueden contener «..» ni saltos de línea.» | carpetas |
| «La huella debe ser la SHA-256 del servidor (SHA256:… en base64).» | huella |
| «Condición desconocida: X. Use EQUALS, CONTAINS, STARTS, ENDS, WILDCARD o REGEX.» · «Indique el texto de la condición.» | filtro de nombre |
| «No se pueden guardar contraseñas de conectores: falta la llave maestra del servidor (Files:MasterKey).» | el servidor no tiene la llave maestra |

**Casos frecuentes:** *«Falta confirmar la huella del servidor SFTP»* → use Probar conexión, compare y pegue la huella. *«La huella del servidor no coincide»* → el proveedor la cambió o es otro servidor; confírmela con el proveedor antes de actualizarla. *«0 listos para leer» con archivos en la carpeta* → no cumplen el tipo o el nombre, o se modificaron hace menos de 10 s. *FTP que lista pero no baja* → modo pasivo bloqueado por un firewall; use SFTP o abra el rango pasivo. *El archivo sigue en la carpeta* → no se pudo mover (permisos de escritura); el hash evita duplicar las órdenes, pero conviene dar permiso de escritura.
