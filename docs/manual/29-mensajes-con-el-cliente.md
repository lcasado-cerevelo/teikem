# 29 — Mensajes del portal con el equipo de servicio al cliente

Lote 104 (portal de clientes, 2026-10-17). Cada persona de un cliente puede escribirle al **equipo de servicio al cliente** de la compañía desde la app del cliente, y el equipo le contesta —o le escribe primero— desde la web de la compañía. Decidido con Luis: **una conversación por persona** (nadie de su empresa ve lo que escribió otro) y **el equipo puede abrir la conversación** escribiéndole primero a una persona desde el expediente del cliente.

## 1. Quién es el equipo
- **Equipo designado por cliente:** en la ficha del cliente, panel **Equipo de servicio al cliente**, quien tenga `clientmessages.manage` pulsa **Designar equipo** y marca a las personas de la compañía que atienden a ese cliente (`PUT /api/v1/client-messages/clients/{id}/team`). Solo pueden estar usuarios activos de la compañía; si no: **400** «Solo pueden estar en el equipo usuarios activos de la compañía.»
- **Sin equipo designado:** lo atiende **quien tenga el permiso `clientmessages.reply`** (el rol «Servicio al cliente»). El panel lo avisa («Este cliente no tiene equipo designado: lo atienden las N personas con el permiso de atender mensajes de clientes»).
- **Administrador de la compañía:** ve y contesta todas las conversaciones de todos los clientes.
- Una persona del equipo ve las conversaciones de los clientes donde está designada **más** las de los clientes que no tienen equipo.

## 2. Permisos y módulo
| Permiso | Qué permite |
|---|---|
| `clientmessages.reply` | Ver la bandeja «Mensajes de clientes», contestar, escribirle primero a una persona, ver el equipo de un cliente |
| `clientmessages.manage` | Designar el equipo de cada cliente |

Módulo **CLIENT_PORTAL**. La plantilla de rol **Servicio al cliente** (para compañías nuevas) trae `clientmessages.reply` y la lectura de clientes, órdenes, ubicaciones y contratos; el administrador de la compañía ya tiene ambos permisos. En una compañía existente, el administrador puede crear el rol con esos permisos.

## 3. Del lado del cliente (app del cliente, pestaña «Mensajes»)
- La persona ve **su** conversación: sus mensajes a la derecha, los del equipo a la izquierda con el nombre de quien contestó. «Cargar mensajes anteriores» trae lo más viejo.
- Máximo **2000 caracteres** por mensaje (hay contador); vacío → **400** «Escriba el mensaje.»; más de 2000 → **400** «El mensaje no puede pasar de 2000 caracteres.»
- Tope de **30 mensajes por hora** por persona: pasado el tope, **429** «Demasiados mensajes seguidos; espere un rato para volver a escribir.»
- La pestaña muestra el **número de mensajes del equipo sin leer** (se consulta cada 30 s); al abrir la pantalla se marcan como leídos. La pantalla se actualiza sola cada 15 s.
- Un usuario de portal **no puede** llamar a `/api/v1/client-messages/*` (403): solo ve la conversación propia por `/api/v1/portal/messages`.

## 4. Del lado de la compañía (web)
- **Portal de clientes → Mensajes de clientes** (`/portal/messages`): lista de conversaciones (búsqueda libre, «Solo sin leer», cliente, persona, última frase y hora, insignia de no leídos) y, a la derecha, la conversación con su compositor. En móvil se ve una u otra con «Volver».
- Al abrir una conversación se marca leída **para todo el equipo**. La lista se actualiza cada ~20 s y la conversación abierta cada ~10 s; no hay aviso en vivo ni por correo al equipo (ver FAQ).
- **Escribirle a una persona** (ficha del cliente → Equipo de servicio al cliente): se escoge la persona (usuario activo de portal de ese cliente) y se escribe el primer mensaje. Si la persona no es de ese cliente o está de baja: **400** «La persona no es un usuario activo de portal de ese cliente.» Si ya había conversación, el mensaje se agrega a ella.
- Los mensajes **nunca se editan ni se borran**.

## 5. Endpoints
| Lado | Endpoint |
|---|---|
| Portal | `GET /api/v1/portal/messages?before&take`, `POST /api/v1/portal/messages`, `POST /api/v1/portal/messages/read`, `GET /api/v1/portal/messages/unread` |
| Compañía | `GET /api/v1/client-messages/conversations`, `GET …/unread`, `GET …/conversations/{id}`, `POST …/conversations/{id}/messages`, `POST …/conversations/{id}/read`, `POST …/conversations` (escribir primero), `GET …/clients/{id}/people`, `GET/PUT …/clients/{id}/team`, `GET …/team-candidates` |

## 6. Casos frecuentes
- **Nadie contesta:** revise que el cliente tenga equipo o que alguien tenga `clientmessages.reply`; el equipo no recibe aviso, tiene que abrir la bandeja.
- **Ana ve los mensajes de Beto (misma empresa):** no es posible; cada conversación es de una persona.
