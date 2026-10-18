# 35 — Acceso del consignatario

Lote 117 (117a acceso, perfil y bandeja; 117b mensajes con su cliente; 2026-10-18, dictado por Luis; diseño en `docs/p10-portal-diseno.md`). Un **consignatario** (quien recibe el paquete) puede tener **su propio acceso** al portal. **Solo el cliente se lo da**, por **invitación a un correo**; la mayoría de los consignatarios no tendrá acceso y seguirá recibiendo los avisos con el enlace de seguimiento (capítulo 34).

## 1. Quién y dónde
- **Cliente (portal):** un administrador u operador del cliente **invita**, **reenvía** o **quita** el acceso de un consignatario suyo (módulo CLIENT_PORTAL). Quien solo consulta ve quién tiene acceso, sin cambiarlo.
- **Consignatario:** entra al mismo portal con su correo y contraseña, pero a **su propio espacio**: solo ve y cambia **lo suyo**.
- **Compañía (staff):** no invita consignatarios. Si intenta darle el rol «Consignatario» a una persona del cliente desde el expediente, responde **400 `El rol de consignatario solo se da desde el consignatario.`**

## 2. La invitación
1. El cliente escribe el correo del consignatario (si lo deja vacío se usa el principal que el consignatario ya tiene registrado) y, si quiere, el nombre.
2. Llega un correo con un **enlace que vence en 48 horas (configurable) y se usa una sola vez**. **Reenviar** deja sin efecto el enlace anterior.
3. Con el enlace, el consignatario **escoge su contraseña**. Su acceso queda **Activo**.
4. Al primer ingreso confirma la **computadora** por correo (queda de confianza 30 días sin uso, igual que el resto del portal), acepta el **documento legal** y entra. **El consignatario no usa verificación en dos pasos (MFA)**: contraseña + computadora de confianza.
- Una misma persona puede ser consignatario de varios clientes (una invitación por cliente). Un correo no puede ser a la vez del staff y del portal.

| Situación | Respuesta |
|---|---|
| Sin correo escrito y el consignatario no tiene uno registrado | **400 `Escriba el correo del consignatario: no tiene uno registrado.`** |
| Ese correo no está disponible (ya tiene acceso, o es del staff, o de otra compañía) | **409 `Ese correo no está disponible para el portal de esta compañía.`** (el mismo mensaje en todos los casos: no revela dónde está) |
| Quien invita solo consulta | **403 `Su rol en el portal es de solo consulta: no puede dar acceso a un consignatario.`** |
| Consignatario de otro cliente o que no existe | **404** (igual para ambos) |
| Reenviar a quien ya aceptó | **409 `Solo se puede reenviar la invitación a un usuario que todavía no la ha aceptado (estatus INVITED).`** |
| Enlace vencido, usado o de una invitación reenviada | **400 `Invitación inválida o vencida.`** |

**Quitar el acceso** es la baja definitiva de esa persona: su sesión deja de renovarse y ya no entra. El consignatario sigue existiendo y puede seguir recibiendo avisos por enlace.

## 3. El espacio del consignatario
Todo sale de su sesión; nunca de lo que escriba en la pantalla. De las rutas del portal solo alcanza las suyas (`/api/v1/portal/consignee/*`); las del cliente (órdenes, mensajes con el equipo, avisos a consignatarios, dar acceso) le responden **403 `Esta ruta no está disponible para usuarios del portal.`** Y una persona del cliente no entra a su espacio: **403 `Este espacio es solo para consignatarios con acceso.`**

### 3.1 Su perfil y cómo recibe los avisos
Muestra su nombre, el cliente que lo atiende y, por **correo** y por **texto**, el contacto, si hoy lo recibe y si puede volver a prenderlo.
- **El cliente prende** los avisos y el medio (capítulo 34) y se le notifica al consignatario por ese medio.
- **El consignatario puede apagarlo cuando quiera** (queda en la bitácora, origen «consignatario»).
- **Volver a prender:** solo el medio que **apagó él mismo**, y solo si el cliente sigue con el aviso prendido y él no se dio de baja con el enlace de un aviso. Lo que el cliente nunca prendió, o lo apagó el cliente, **solo el cliente lo prende**.

| Situación | Respuesta |
|---|---|
| Prender un medio que no puede | **409 `Solo su cliente puede prender este aviso: usted puede volver a prender únicamente el que apagó.`** |
| Prender un medio sin contacto registrado | **400 `No tiene registrado ese medio de contacto: pídale a su cliente que lo agregue.`** |
| Medio que no es correo ni texto | **400 `Indique el medio del aviso: correo o texto.`** |
| No indicar si lo quiere o no | **400 `Indique si desea recibir o no los avisos por ese medio.`** |

### 3.2 Su bandeja
Los avisos que **se le enviaron** (más nuevos primero, hasta 100 por página): «Su paquete va en camino», «fue entregado», «tuvo un intento de entrega fallido»…, el conduce, el estado del envío del aviso (enviado, omitido, fallido) y el enlace de seguimiento de la orden. Solo ve los suyos.

### 3.3 Mensajes con su cliente (Lote 117b)
El consignatario **le escribe a su cliente** y el cliente le contesta. Es aparte de los mensajes del cliente con el equipo de la compañía (capítulo 29).
- **Del lado del consignatario:** una sola conversación con su cliente, que **abre con su primer mensaje**; ve lo que el cliente le contesta, cuántos mensajes nuevos tiene y los marca leídos. Nadie más de su empresa ve la suya.
- **Del lado del cliente:** los **administradores y operadores** ven las conversaciones de sus consignatarios (la más reciente primero; filtro «sin leer»; buscar por consignatario, persona, correo o texto del último mensaje) y **contestan**; quien **solo consulta** las lee, sin contestar. La lectura es **compartida**: cuando uno la abre o contesta, queda leída para todos los de su cliente. Nunca ven las conversaciones de otro cliente.
- Los mensajes **no se editan ni se borran**. Tope de **2000 caracteres** y **30 mensajes por hora** por persona.

| Situación | Respuesta |
|---|---|
| Mensaje vacío | **400 `Escriba el mensaje.`** |
| Más de 2000 caracteres | **400 `El mensaje no puede pasar de 2000 caracteres.`** |
| Más de 30 en una hora | **429 `Demasiados mensajes seguidos; espere un rato para volver a escribir.`** |
| Contesta quien solo consulta | **403 `Su rol en el portal es de solo consulta: puede leer los mensajes de los consignatarios, no contestarlos.`** |
| Conversación de otro cliente o que no existe | **404** (igual para ambos) |
| Un consignatario entra a la bandeja del cliente, o una persona del cliente escribe como consignatario | **403** |

## 4. Endpoints
| Endpoint | Para qué | Quién |
|---|---|---|
| `GET /api/v1/portal/consignee-access/{locationId}` | Quién tiene acceso a ese consignatario | Persona del cliente |
| `POST /api/v1/portal/consignee-access/{locationId}` | Invitar (`email`, `fullName`) | Admin/operador del cliente |
| `POST /api/v1/portal/consignee-access/{locationId}/{id}/resend` | Reenviar la invitación | Admin/operador del cliente |
| `DELETE /api/v1/portal/consignee-access/{locationId}/{id}` | Quitar el acceso | Admin/operador del cliente |
| `GET /api/v1/portal/consignee/me` | Su perfil y sus medios de aviso | Consignatario |
| `PUT /api/v1/portal/consignee/me/notices/{EMAIL o SMS}` | Apagar o volver a prender (`enabled`) | Consignatario |
| `GET /api/v1/portal/consignee/notices` | Su bandeja | Consignatario |
| `GET /api/v1/portal/consignee/messages?before=&take=` | Mi conversación con mi cliente | Consignatario |
| `POST /api/v1/portal/consignee/messages` | Escribirle a mi cliente (`body`) | Consignatario |
| `POST /api/v1/portal/consignee/messages/read` · `GET .../messages/unread` | Marcar leído · cuántos sin leer | Consignatario |
| `GET /api/v1/portal/consignee-messages?unreadOnly=&q=&skip=&take=` | Las conversaciones de mis consignatarios | Persona del cliente |
| `GET /api/v1/portal/consignee-messages/unread` | Cuántas conversaciones tienen mensajes sin leer | Persona del cliente |
| `GET /api/v1/portal/consignee-messages/{id}?before=&take=` | Abrir una conversación | Persona del cliente |
| `POST /api/v1/portal/consignee-messages/{id}` · `POST .../{id}/read` | Contestar · marcar leída | Admin/operador (contestar) |

## Casos frecuentes
- **«El consignatario no recibe la invitación»:** revise el correo escrito y la carpeta de no deseados; **Reenviar** manda un enlace nuevo (el anterior deja de servir).
- **«Mi enlace dice que es inválido o venció»:** el enlace dura 48 horas y sirve una sola vez; pida que el cliente lo reenvíe.
- **«Entró pero no ve nada de las órdenes»:** correcto: el consignatario solo tiene su perfil, su bandeja y los mensajes con su cliente.
- **«No puedo volver a prender los avisos por texto»:** solo el cliente puede prender un medio que el consignatario no apagó él mismo.
