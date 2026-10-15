# 16 — Enviar avisos (avisos manuales)

Lote 54 (2026-10-15). Además de los avisos automáticos de las reglas (capítulo [14](14-avisos.md)), una persona con permiso puede **enviar un aviso a mano** a una o varias personas, a uno o varios roles, o a todas las
personas de la compañía. Es una **notificación**: **no se responde** (para conversar existe la [mensajería](15-mensajeria.md)). La pantalla está descrita en [L54 — Enviar avisos](frontend/l54-enviar-avisos.md).

## 1. Quién puede

- Permiso **`notifications.send`** («Enviar avisos a otras personas»). Se agregó al **Admin de compañía** (plantilla y roles ya clonados). **No se da a otros roles por omisión**: el administrador se lo asigna a quien quiera.
- Módulo **Sistema** (siempre encendido). Todo el API pide el permiso; no existe PATCH, PUT ni DELETE de avisos.
- Quien **recibe** un aviso no necesita ningún permiso: es de su propia campanita.

## 2. Cómo se envía

- **A quién**: personas (`userIds`), roles (`roleIds`: quienes tienen el rol **hoy**) o **todas** (`all = true`: todas las personas activas de la compañía; entonces `userIds` y `roleIds` se ignoran). Siempre **sin repetir**, solo personas **activas de la compañía** y **sin avisarse a sí mismo**.
- **Qué**: título (obligatorio, hasta 150) y texto (opcional, hasta 1000).
- **Cómo llega**: cada persona recibe un aviso en su **campanita** con «De {quien lo envió}». Si tiene Teikem abierto lo ve **al instante** (conexión en vivo, igual que los mensajes) y Kemi avisa; si no, **queda pendiente**.
- **Historial** (`Announcement`): a quién se envió (texto legible: «Gerencia, Ana Rivera, +2» o «Todas las personas»), cuántas personas lo recibieron y **cuántas lo han leído**. Nunca se edita ni se borra.

## 3. API (todo con `notifications.send`)

`GET /api/v1/announcements/people?q=&skip=&take=` (personas a quienes se puede avisar, con el total; hasta 500 por página) · `GET /announcements/roles` (roles con cuántas personas activas tienen) · `POST /announcements` · `GET /announcements?skip=&take=` (historial).

## 4. Validaciones y mensajes

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Sin título | `Escriba el título del aviso.` | 400 (`title`) |
| Título de más de 150 caracteres | `El título del aviso no puede pasar de 150 caracteres.` | 400 (`title`) |
| Texto de más de 1000 caracteres | `El texto del aviso no puede pasar de 1000 caracteres.` | 400 (`text`) |
| Sin personas, roles ni «todos» | `Escoja al menos una persona, un rol o a todos.` | 400 (`userIds`) |
| Persona que no es de la compañía (o inactiva) | `Usuario no encontrado en esta compañía.` | 400 (`userIds`) |
| Rol que no es de la compañía | `Rol no encontrado.` | 400 (`roleIds`) |
| Nadie a quien enviar (rol sin personas activas, o solo uno mismo) | `No hay personas a quienes enviar el aviso.` | 400 (`userIds`) |
| Sin el permiso | «Sin permiso» | 403 |

## 5. Para soporte

- Tabla `Announcement` y la columna `UserNotification.AnnouncementId` (de ahí sale «De …»); sección **2026-10-15 (i)** de `Diseño/logistica-db-update.sql`. Los avisos manuales llevan `EventCode = ANNOUNCEMENT` en la bandeja y **no son silenciables**.
- El empuje en vivo (`NoticeReceived`) usa la misma conexión que los mensajes (`/hubs/messages`); la tiene toda persona con sesión, tenga o no el permiso de mensajería. Si la conexión cae, el contador de la campanita se actualiza igual con su sondeo de 30 s.
- Un aviso a **todas** las personas crea una fila de bandeja por persona (en tandas de 500, en una sola transacción).
