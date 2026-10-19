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
