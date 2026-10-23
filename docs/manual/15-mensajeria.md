# 15 — Mensajería interna

Lote 53 (P2.1, 2026-10-15). Las personas de la compañía se escriben entre sí dentro de Teikem, con **hilos**: un mensaje se puede **contestar** y el hilo se
conserva. No es un chat con muchas funciones, pero se siente como uno: lo que se escribe llega **en el momento** a quien tiene Teikem abierto, y a quien no,
**le queda pendiente** hasta que entra. La pantalla está descrita en [L53 — Mensajes](frontend/l53-mensajes.md).

## 1. Quién puede usarla

- Permiso **`messages.use`** («Usar la mensajería interna»). Se agregó a **todos los roles que ya existían** (personal de la compañía). **Un rol nuevo no lo recibe solo**:
  el administrador se lo asigna en Sistema → Roles y usuarios.
- **Usuarios del portal de clientes** (cuando exista): no lo tienen por omisión. El dueño se lo asigna **solo a los gerenciales de cada cliente**; los usuarios de
  captura de datos no pueden escribir. Un usuario de cliente solo podrá escribirle a quien se designe y nunca a usuarios de otros clientes (se revisa al construir el portal).
- Cada persona ve y toca **solo sus hilos** de la compañía activa. Si no es parte de un hilo, **«no existe»** para ella (404: no se revela que existe).

## 2. Tipos de hilo

| Tipo | Cómo se crea | Quién puede contestar |
|---|---|---|
| **Entre dos personas** (`DIRECT`) | «Nuevo mensaje» a **una sola persona** | Las dos. Es **único por pareja**: escribirle otra vez a la misma persona vuelve al mismo hilo. No se abandona ni se le agregan personas. |
| **Grupo** (`GROUP`) | «Nuevo mensaje» a **varias personas** o a **un rol** | Todas. Quien lo creó agrega personas; cualquiera se puede **salir** (y deja de ver el hilo). |

- **Mensaje a un rol**: se arma un grupo con las personas **activas que tienen ese rol hoy** («en foto»). Quien entre al rol después **no entra solo**; se le puede agregar a mano.
  El nombre del grupo es el que se escriba, o el del rol, o («Ana, Luis, +2») los nombres de las demás personas.
- **Quien entra a un grupo después** ve el historial **solo desde que entra**.
- Los mensajes **nunca se editan ni se borran**. Hasta 2000 caracteres por mensaje.

## 3. Cómo llega

- **En vivo**: Teikem abierto mantiene una conexión en vivo con el servidor; cada mensaje nuevo se empuja al instante a las demás personas del hilo, y al aparecer en la cabecera
  el **contador de mensajes** sube y **Kemi** avisa «Te llegó un mensaje de Ana → Ver». Si la persona ya está viendo ese hilo, el mensaje se marca leído solo y no se avisa.
- **Sin sesión**: el mensaje queda en el hilo; al entrar, la persona ve su contador de no leídos y Kemi la saluda con lo pendiente.
- **Hilo silenciado**: sigue contando como no leído, pero Kemi no avisa.
- **Respaldo**: si la conexión en vivo cae (red, proxy), la web se reconecta sola (a los 0, 2, 5, 10 y 30 s) y, mientras tanto, pregunta por lo nuevo cada 30 s. No se pierde nada.
- **No leídos**: son los mensajes **de otras personas** más nuevos que lo último que leyó la persona (y que su entrada al grupo). Contestar equivale a haber leído lo anterior.

## 4. Presencia («en línea»)

Teikem abierto avisa cada 30 s que sigue ahí. **En línea** = visto hace menos de 90 segundos; si no, se muestra «Visto hace 5 min», «Visto hace 3 h» o «Visto el 10/12/2026». Se ve en la lista de
conversaciones (punto verde), en el hilo y en el buscador de personas. Solo se muestra la presencia de personas de **la misma compañía**.

## 5. API (todo con `messages.use`)

`GET /api/v1/messages/people?q=&take=` · `GET /roles` · `GET /threads` · `POST /threads` (a personas y/o a un rol, con el primer mensaje) · `GET /threads/{id}?before=&take=` (los últimos 50;
`before` pide los anteriores) · `POST /threads/{id}/messages` · `POST /threads/{id}/read` · `POST /threads/{id}/members` · `POST /threads/{id}/leave` · `PUT /threads/{id}/mute` · `GET /unread`.
Presencia: `POST /api/v1/presence/ping` (cualquier persona con sesión). Conexión en vivo: `/hubs/messages` (SignalR; el token va en el query `access_token`). No existe PATCH ni DELETE de mensajes.

## 6. Validaciones y mensajes

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Mensaje vacío | `Escriba el mensaje.` | 400 (`text`) |
| Más de 2000 caracteres | `El mensaje no puede pasar de 2000 caracteres.` | 400 (`text`) |
| Nombre del grupo de más de 100 caracteres | `El nombre del grupo no puede pasar de 100 caracteres.` | 400 (`title`) |
| Sin personas ni rol | `Escoja al menos una persona o un rol.` | 400 (`userIds`) |
| Solo uno mismo | `No puede escribirse a sí mismo.` | 400 (`userIds`) |
| Persona que no es de la compañía (o inactiva) | `Usuario no encontrado en esta compañía.` | 400 (`userIds`) |
| Rol que no es de la compañía | `Rol no encontrado.` | 400 (`roleId`) |
| Rol sin personas activas | `Ese rol no tiene personas activas a quienes escribir.` | 400 (`roleId`) |
| Hilo inexistente, ajeno o del que ya salió | `Conversación no encontrada.` | 404 |
| Agregar personas sin haber creado el grupo | `Solo quien creó el grupo puede agregar personas.` | 403 |
| Agregar personas a un hilo entre dos | `A una conversación entre dos personas no se pueden agregar más; cree un grupo.` | 409 |
| Salirse de un hilo entre dos | `Una conversación entre dos personas no se abandona.` | 409 |
| Agregar sin indicar a quién | `Indique a quién agregar.` | 400 (`userIds`) |
| Sin el permiso | «Sin permiso» | 403 |

## 7. Para soporte

- Tablas `MessageThread`, `MessageThreadMember`, `Message` y `UserPresence`; sección **2026-10-15 (h)** de `Diseño/logistica-db-update.sql` (agrega `messages.use` a todos los roles existentes).
- **WebSockets**: la conexión en vivo usa WebSockets (y, si el servidor o un proxy no los dejan pasar, SignalR cae solo a otros transportes). En IIS hay que tener habilitado el **WebSocket Protocol**;
  detrás de un proxy inverso (nginx, etc.) la ruta `/hubs/messages` debe permitir `Upgrade`/`Connection`. Sin conexión en vivo todo sigue funcionando con el respaldo de 30 s.
- **Una sola instancia del servidor** por ahora: con varias, el empuje en vivo solo llega a quienes están conectados a la instancia que recibió el mensaje (los demás se enteran con el respaldo de 30 s).
  Si algún día se necesitan varias instancias a la vez, se agrega un «backplane» (SQL Server o Redis) sin cambiar nada más.
- Los empujes en vivo **no llevan datos del mensaje que la persona no pueda pedir por la API**; son solo «algo cambió» + un resumen corto para el globo de Kemi.
- La presencia es un dato por persona (`UserPresence.LastSeenUtc`); no guarda en qué pantalla está.

## 8. Mensajería con el chofer, hablar sobre una orden o ruta y responsables (Lote 212, 2026-10-23)

### 8.1 Quién ve a quién: el ajuste «Mensajería» de cada persona
Cada persona tiene, **por compañía**, uno de tres valores (lo cambia quien administra usuarios, permiso `admin.users`):

| Valor | Qué significa |
|---|---|
| `FULL` — Completa (por omisión) | Ve a todos los de la compañía, y todos la ven. |
| `OFFICE` — Solo con la oficina | Escribe y recibe, pero en su lista solo salen las personas `FULL`. **Los usuarios ligados a un chofer quedan así** (al crear el chofer o al ligarle el usuario; los que ya existían se pasaron a `OFFICE` en la sección 2026-10-23 (i) del SQL). |
| `NONE` — Sin mensajería | No aparece en ninguna lista y no puede abrir ni escribir hilos. |

- Para que dos choferes se escriban entre sí, se le pone `FULL` a los dos (o a quien se quiera). Un grupo lo puede crear la oficina con quien quiera.
- API: `GET /api/v1/messaging-modes` (lista) y `PUT /api/v1/messaging-modes/{userId}` con `{"mode":"FULL|OFFICE|NONE"}`. Ambos con `admin.users`.
- Los hilos que ya existen siguen funcionando si cambia el modo; lo que cambia es a quién se puede escribir de nuevo.

### 8.2 Hablar sobre una orden o una ruta
Un mensaje puede llevar una **referencia** (`ref`: `{"entity":"ORDER"|"TRIP","publicId":"…"}`) al crear el hilo (`POST /messages/threads`) o al enviar (`POST /messages/threads/{id}/messages`). En el hilo se ve como una **tarjeta** (número de orden o código de ruta); en la lista, la vista previa dice `[2026-0001] texto`.
- La referencia va **en el mensaje**, no en el hilo: Ana y Luis tienen un solo hilo entre dos y hoy pueden hablar de una orden y mañana de otra.
- La tarjeta muestra el detalle (cliente / zona y estatus) y `canOpen = true` **solo si quien la ve tiene `orders.view` / `trips.view`**; si no, solo ve el título.
- `GET /messages/about?entity=ORDER|TRIP&publicId=…` lista los mensajes de **mis** hilos que hablan de esa orden o ruta (para la pestaña «Mensajes» de la ficha). No revela hilos de los que no soy parte.

### 8.3 Sugeridos: «¿con quién hablo de esto?»
`GET /messages/suggested?entity=ORDER|TRIP&publicId=…` propone, en este orden y sin repetir: (1) los **responsables de la ruta** (para una orden, la ruta activa donde va), (2) los **responsables de la zona** de esa ruta, (3) los **roles que despachan** (los que tienen el permiso `trips.dispatch`) como grupo, (4) hasta 5 **conversaciones recientes**. Cada sugerido dice de dónde sale. Respeta el ajuste del punto 8.1 (nunca sugiere a quien quien pregunta no puede ver, ni a uno mismo). **No abre nada solo**: la persona escoge.

### 8.4 Responsables
Los responsables de una **ruta** o de una **zona de despacho** (varios por entidad):
- `GET /messages/responsibles?entity=TRIP|DISPATCH_ZONE&key=…` (`key` = `publicId` de la ruta, o el id de la zona). Cualquiera con `messages.use`.
- `PUT /messages/responsibles` con `{"entity","key","userIds":[…]}` deja **exactamente** a esas personas. Una ruta exige `trips.plan`; una zona, `fleet.manage`.
- Una ruta nueva nace con **quien la creó** como responsable.
- La tabla es genérica (`EntityResponsible`): mañana puede colgarse de un cliente o una orden sin cambiar el esquema.

### 8.5 Mensajes exactos y códigos
| Situación | Mensaje | HTTP |
|---|---|---|
| Modo inválido | `La mensajería debe ser FULL (completa), OFFICE (solo con la oficina) o NONE (sin mensajería).` | 400 |
| Escribir sin mensajería (`NONE`) | `Su usuario no tiene habilitada la mensajería en esta compañía.` | 403 |
| Escribirle a alguien que no se ve (por su modo) | `Usuario no encontrado en esta compañía.` | 400 |
| Referencia de otro tipo | `La referencia debe ser una ORDER (orden) o una TRIP (ruta).` | 400 |
| Referencia que no existe | `La orden o ruta a la que se refiere el mensaje no existe en esta compañía.` | 400 |
| Responsables: entidad inválida | `La entidad debe ser TRIP (ruta) o DISPATCH_ZONE (zona de despacho).` | 400 |
| Responsables: clave inválida | `La clave de la entidad no es válida.` | 400 |
| Responsables: usuario ajeno | `Usuario no encontrado en esta compañía.` | 400 |
| Responsables sin permiso | `No tiene permiso para cambiar los responsables (trips.plan).` (o `fleet.manage`) | 403 |

## 9. Fotos del chat, aviso al teléfono del chofer y grupos de rol (Lote 212 B, 2026-10-23)

### 9.1 Fotos en el chat
- Un mensaje puede llevar **hasta 4 fotos** (JPG, PNG o WEBP, de hasta 3 MB cada una; la app las reduce sola). Se mandan en `attachments` (`{fileName, dataBase64}`) al crear el hilo o al enviar. **Un mensaje puede ser solo fotos** (sin texto): en la lista se ve «📷 Foto» o «📷 3 fotos».
- Cada foto se guarda **cifrada en el servidor**, con una miniatura (320 px) para el hilo. Se baja con `GET /api/v1/messages/attachments/{publicId}` (`?thumb=true` = miniatura). **Solo quien es parte del hilo** la puede bajar (404 si no); el teléfono no la guarda: la ve en pantalla.
- **Las fotos del chat viven poco** (30 días por omisión). Una **evidencia** (entrega, excepción, daño) **no** va en el chat: se levanta en su pantalla y queda pegada a la orden.
- Si la plataforma del servidor no puede generar miniaturas, el mensaje se guarda igual y la miniatura se sirve como la foto completa (en Windows lo hace siempre).

### 9.2 Cuánto viven: ajuste de la compañía
`GET/PUT /api/v1/messaging-settings` (permiso `admin.tenant`): `photoDays` (1 a 3650, 30 por omisión) y `photoAction`:
- `DELETE` (por omisión): al vencer, el **contenido se borra** del servidor. En el hilo queda «La foto se borró el 2026-11-22.».
- `ARCHIVE`: al vencer, la foto **deja de mostrarse** en el chat pero **el contenido se conserva** (evidencia). Todavía no se mueve a un almacén en frío: eso llega con el almacenamiento en la nube.
- El **texto de los mensajes nunca se borra**. El trabajo programado «Limpiar las fotos del chat» (`MESSAGE_ATTACHMENT_PURGE`, cada noche a las 03:45; hasta 500 por corrida) hace el trabajo; se ve y se corre desde Trabajos programados.

### 9.3 Aviso emergente (push) al teléfono del chofer
Cada mensaje nuevo le llega al chofer como aviso emergente en su teléfono (título = quien escribe, o el nombre del grupo; texto = el mensaje, en grupo «Ana: …»), **salvo que haya silenciado el hilo**. Al tocarlo, la app abre ese hilo (`data.kind = MESSAGE`, `data.thread`). Sin Firebase configurado (`Push:Fcm:ServiceAccountJsonPath`) no sale nada y el mensaje igual está en el chat. Un token que Firebase rechaza (app desinstalada) se desactiva solo. Hoy el aviso al teléfono es para choferes; el personal lo tendrá con su app.

### 9.4 Grupo de rol «vivo»
`POST /messages/threads` con `roleId` y `followRole: true` crea (o **reusa**: es uno solo por rol y compañía) el grupo de ese rol. Entran las personas que tienen el rol, y la lista se **sincroniza sola** cada vez que alguien escribe o abre el grupo: si alguien recibe el rol, entra (ve el historial solo desde que entra); si lo pierde, sale. **Quien le escribe al grupo sin tener el rol (un chofer) se queda.** A un grupo de rol no se le agregan personas a mano. Sin `followRole`, «escribirle a un rol» sigue como antes (una «foto» de quienes lo tienen hoy).

### 9.5 Mensajes exactos y códigos
| Situación | Mensaje | HTTP |
|---|---|---|
| Más de 4 fotos | `Un mensaje puede llevar hasta 4 fotos.` | 400 |
| Base64 inválido o vacío | `La foto no es válida (base64 de una imagen JPG, PNG o WEBP).` | 400 |
| Tipo distinto de JPG/PNG/WEBP | `Solo se admiten fotos JPG, PNG o WEBP.` | 400 |
| Foto de más de 3 MB | `Cada foto no puede pasar de 3 MB (la app las reduce sola).` | 400 |
| Almacén de archivos sin configurar | `El almacén de archivos no está configurado (Files:MasterKey); avise a soporte.` | 400 |
| Sin texto y sin fotos | `Escriba el mensaje.` | 400 |
| Foto de un hilo del que no soy parte | `Foto no encontrada.` | 404 |
| Foto ya borrada | `La foto se borró el AAAA-MM-DD.` | 409 |
| Foto ya archivada | `La foto se archivó el AAAA-MM-DD y ya no se muestra en el chat.` | 409 |
| Días de las fotos fuera de rango | `Los días de las fotos del chat deben estar entre 1 y 3650.` | 400 |
| Acción al vencer inválida | `Al vencer, la foto se puede DELETE (borrar) o ARCHIVE (archivar).` | 400 |
| `followRole` sin `roleId` | `Para un grupo de rol indique el rol (roleId).` | 400 |
| Agregar personas a un grupo de rol | `A un grupo de rol no se agregan personas: entra quien tiene el rol.` | 409 |
