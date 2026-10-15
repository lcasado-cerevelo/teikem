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
