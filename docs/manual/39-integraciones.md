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

## 8. API para los clientes (Lote 135)

> Dictado de Luis (2026-10-19): el cliente (o su sistema) puede hablar con Teikem sin pantalla, **solo sobre lo suyo**: sus órdenes, consignatarios, perfil y —según los módulos de la compañía— su almacén y sus órdenes de compra y de venta. Se activa con un conector de tipo **API para el cliente** y una o más **llaves**.

**Cómo se configura** (permiso `integrations.create` + el de cada familia; mismo formulario que las demás integraciones, módulo `INTEGRATIONS`):
- En **Sistema → Integraciones / API → Nueva integración** se elige el conector **API para el cliente**. No lleva plantilla, carpeta ni horario. Se escogen los **alcances** (solo se ofrece lo que quien crea puede dar y lo que la compañía tiene contratado). La cuenta de servicio de la integración recibe exactamente los permisos de esos alcances.
- **Cliente:** obligatorio solo si se elige algún alcance de **transporte** (órdenes, facturas, consignatarios, perfil): la llave verá únicamente lo de ese cliente (otro cliente «no existe» para ella). Los alcances de almacén, ventas y compras no lo piden.
- En la lista, la acción **Llaves de API** (`integrations.manage`) abre la gestión: **crear** (nombre y límite por minuto opcional), **cambiar el límite**, **revocar** y ver la **bitácora** de cada llave. Hasta **10 llaves activas** por integración.
- **El secreto se muestra una sola vez**, al crear la llave (con botones Copiar). En Teikem solo se guarda su hash SHA-256; después solo se ve la llave `tk_…` y los últimos 4 caracteres. Si se pierde, se revoca y se crea otra.

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

**Eventos** (`events`, al menos uno): `order.status_changed` (cada cambio de estatus de una orden del cliente; una entrega que salta estatus manda uno por cada estatus), `order.delivered` (la orden se entregó) e `invoice.issued` (se emitió una factura del cliente). Solo salen eventos de **lo del cliente** de la integración. Más `webhook.ping` (la prueba), que no se suscribe.

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

**Casos frecuentes:** *«No me llegan avisos»* → el webhook está suspendido o apagado, la integración está apagada, el evento no está marcado o la orden es de otro cliente; revise la pestaña Salidas. *«La firma no coincide»* → use el cuerpo **crudo** (sin reformatear) y el secreto vigente (¿lo rotaron?). *«Recibo el mismo aviso dos veces»* → es un reintento (la primera respuesta no llegó a tiempo): descarte por `X-Teikem-Delivery`. *«Probar dice que no se pudo conectar»* → la URL debe ser pública y con https.

**Fuera de este lote:** la salida de **archivos** por evento o por horario (SFTP, correo o API del cliente) necesita el mapeo de salida; llega con el Lote 137.
