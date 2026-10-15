# Lote 53 — Mensajería interna con hilos: diseño propuesto (2026-10-15, para aprobación de Luis)

Escrito por mí (sin agentes). **No se construye hasta que Luis lo apruebe.** Va en el cherry-pick a Depot-Implementation junto con los Lotes 50, 51 y 52 (commit propio, SQL propio, permiso propio, sin depender del Lote 49 ni del motor de trabajos).

## Lo que Luis pidió
- Un mensaje se puede **contestar** y seguir contestando; el **hilo se conserva**. Entre dos personas: hilo 1 a 1.
- Un mensaje a un **rol** o a **varias personas** crea un **hilo de grupo** donde todos pueden contestar.
- No es un chat en vivo, pero el aviso llega **casi al instante** si la persona tiene sesión abierta; si no, **queda pendiente** hasta que entre.
- La puerta es **Kemi**: opciones en botones (sin escribir comandos); «Enviar un mensaje a…» abre una pantallita con el **buscador de personas** (nombre y correo) que ya existe del Lote 52.

## Qué se construye

### Datos (sección nueva al final de `logistica-db-update.sql`, propia)
- `MessageThread` (por compañía): `Kind` (`DIRECT` 1 a 1 | `GROUP`), título (opcional; en grupos), creado por, último mensaje (fecha y vista previa corta, para ordenar la lista).
- `MessageThreadMember`: hilo, usuario, **último leído** (hasta qué mensaje leyó → de ahí salen los no leídos), silenciado sí/no, fecha en que entró y, si salió, cuándo. Único (hilo, usuario).
- `Message`: hilo, quién lo escribió, texto (hasta 2000 caracteres), fecha. **Nunca se edita ni se borra** (queda el historial; igual que el resto del sistema).
- Un hilo `DIRECT` es **único por pareja**: escribirle otra vez a la misma persona vuelve al mismo hilo, no crea otro.
- Permiso `messages.use` (categoría ADMIN en el catálogo para poder asignarlo; **se agrega a TODOS los roles ya existentes y a las plantillas**, porque la idea es que todos se puedan escribir) — ver decisión 4.

### Cómo se arma un hilo
- **A una persona** → hilo DIRECT (o el que ya existe).
- **A varias personas** → hilo GROUP con ellas más quien escribe; el grupo lleva un nombre (el que se escriba o «Fulano, Mengano…»).
- **A un rol** → hilo GROUP con las personas que **tienen ese rol en ese momento** (foto del momento; quien entre al rol después no entra solo — ver decisión 3). El nombre sugerido es el del rol.
- Cualquier miembro puede **contestar**; en un grupo, quien lo creó puede **agregar** más personas (las nuevas ven el historial desde que entran) y cualquiera puede **salirse**.

### Cómo llega (entrega)
- Cada mensaje nuevo crea un aviso en la **campanita** de los demás miembros (el motor de avisos del Lote 51, evento `MESSAGE_RECEIVED`, canal Campanita) y **Kemi** parpadea con el globo **«Te llegó un mensaje de Ana → Ver»**; «Ver» abre el hilo. Los mensajes del mismo hilo se **agrupan** (un aviso por hilo, con la cantidad), no uno por mensaje.
- **Sesión abierta**: la web pregunta por lo nuevo cada pocos segundos (**5 s con la ventana visible**, 30 s oculta); el hilo abierto se refresca solo. Es «casi al instante» y no requiere tecnología nueva en el servidor.
- **Sin sesión**: nada se pierde; el mensaje queda en el hilo y al entrar la persona ve el contador de no leídos y a Kemi saludando.
- Un hilo **silenciado** no manda aviso a la campanita (sí cuenta como no leído).

### Pantalla «Mensajes» (nueva, en la cabecera junto a la campanita y desde Kemi)
- Lista de hilos (nombre o persona, último mensaje, hora, no leídos), buscador, ordenada por el más reciente.
- El hilo: burbujas por persona con hora, caja para escribir (Enter envía, Mayús+Enter salto de línea), «Marcar leído» automático al abrirlo, miembros del grupo (agregar, salirse).
- **Nuevo mensaje**: buscador de personas con nombre y correo, varias a la vez (fichas), o «un rol completo»; escribir y enviar.
- 360 px en adelante; permiso `messages.use` en la ruta.

### Kemi
- Opción nueva en su panel: **«Enviar un mensaje a…»** (abre «Nuevo mensaje» con el buscador) y **«Mis mensajes (N)»**. Los globos distinguen **avisos** de **mensajes**.

### «En línea» (opcional, decisión 2)
- Hoy el sistema **no guarda actividad de la sesión web** (solo el último ingreso, y la actividad de los aparatos móviles). Para mostrar «en línea» en el hilo haría falta una **marca de presencia**: la web avisa cada ~30 s mientras está abierta y el servidor guarda «visto por última vez». Es una tabla chica (`UserPresence`) y un endpoint; con ella la lista de personas puede mostrar un punto verde y el hilo «visto hace 2 min».

## API (todo con `messages.use`; cada quien ve solo sus hilos)
`GET /api/v1/messages/threads` · `POST /api/v1/messages/threads` (a personas o a un rol, con el primer mensaje) · `GET /threads/{id}` (con mensajes, paginado hacia atrás) · `POST /threads/{id}/messages` · `POST /threads/{id}/read` · `POST /threads/{id}/members` (agregar) · `POST /threads/{id}/leave` · `PUT /threads/{id}/mute` · `GET /api/v1/messages/unread-count` · (`POST /api/v1/presence/ping`, si se aprueba la presencia).

## Mensajes de error exactos (propuestos)
- «Escriba el mensaje.» / «El mensaje no puede pasar de 2000 caracteres.» (400)
- «Escoja al menos una persona o un rol.» (400) · «Usuario no encontrado en esta compañía.» (400) · «Rol no encontrado.» (400)
- «No puede escribirse a sí mismo.» (400, si el único destinatario es uno mismo)
- «Conversación no encontrada.» (404; también si no es miembro: no se revela que existe)
- «Ya no es parte de esta conversación.» (409, escribir tras salirse)
- «Solo quien creó el grupo puede agregar personas.» (403/409)

## Pruebas
Reglas puras (hilo único por pareja, miembros de un rol, no leídos, agrupar avisos); **SQL real**: pareja única bajo concurrencia, envío y no leídos, un no miembro no ve ni escribe (404), aislamiento entre compañías, salir y volver, aviso agrupado por hilo, hilo silenciado; API por reflexión (permiso en todo, sin userId/tenantId del request); vitest de la pantalla, Kemi y el buscador de varias personas; Playwright de punta a punta con dos usuarios (uno escribe, el otro lo ve en su campanita, contesta, el primero lo ve).
Manual (capítulo 15 + pantalla `l53`), FAQ, capturas y `docs/lote53-decisiones.md`.

## Decisiones que necesito de Luis (con mi recomendación)
1. **Entrega**: sondeo corto (5 s) ahora; conexión en vivo (SignalR) queda para después si hace falta. *Recomiendo sondeo corto.*
2. **Presencia «en línea»** (punto verde y «visto hace…»): ¿la incluyo en este lote? *Recomiendo sí, es chica.*
3. **Mensaje a un rol**: el grupo se forma con **quienes tienen el rol hoy** (foto) — o el hilo queda ligado al rol y entra quien se sume después. *Recomiendo la foto (más simple y predecible; se puede agregar gente a mano).*
4. **Quién puede escribir**: todos los usuarios de la compañía (permiso `messages.use` en todos los roles) — o solo quienes lo reciban. *Recomiendo todos.*
