# Lote 212 — Mensajería con el chofer, parte A (servidor)

## Qué se construyó
Primera de tres partes (A servidor base · B fotos con retención + push + grupo vivo por rol · C app del chofer y web). El chat de personas del Lote 53 (hilos entre dos y de grupo, no leídos, silencio, en línea) **ya existía**; esto lo prepara para el chofer y para hablar de una orden:
- **Ajuste «Mensajería» por usuario y compañía** (`UserTenant.MessagingMode`: `FULL` / `OFFICE` / `NONE`). La lista de personas, crear hilos, agregar a un grupo y escribirle a un rol respetan quién ve a quién. Los usuarios ligados a un chofer pasan a `OFFICE` (al crear o ligar el chofer, y los existentes en el SQL).
- **Referencia a orden o ruta en el mensaje** (`Message.RefEntity` / `RefPublicId`): tarjeta en el hilo, vista previa `[código] texto`, `GET /messages/about` (mensajes de mis hilos sobre una orden o ruta). La tarjeta da el detalle solo a quien tiene permiso de verla.
- **Responsables genéricos** (`EntityResponsible`, patrón polimórfico, varios por entidad): hoy ruta y zona de despacho. La ruta nace con quien la creó como responsable.
- **Sugeridos** (`GET /messages/suggested`): responsables de la ruta → de la zona → roles con `trips.dispatch` → conversaciones recientes, sin repetir y respetando la visibilidad. Nunca abre nada solo.
- SQL: sección 2026-10-23 (i) de `Diseño/logistica-db-update.sql` (idempotente). Manual: capítulo 15 §8; FAQ: «Lote 212».

## Cómo se probó
6 pruebas nuevas (`TripStatusEffectTests.Messaging.cs`, base en memoria): tabla de visibilidad y cambio de modo, tarjeta/vista previa/«about» (y que quien no es parte no ve nada), referencias inválidas, orden de los sugeridos con visibilidad, reglas puras. Suite completa del servidor: ver el cierre del commit. **No probado en SQL Server real** (las pruebas `MessagingSqlTests` corren en el CI).

## Decisiones a revisar
- «Despachadores» se identifica por el **permiso `trips.dispatch`**, no por el nombre del rol (cada compañía puede llamarlo distinto).
- Para una orden, los sugeridos usan la **ruta activa** donde va; una orden sin ruta solo recibe roles y recientes.
- La tarjeta da `canOpen` solo con `orders.view` / `trips.view`. El chofer no tiene esos permisos: la app del chofer abrirá su propio envío por el manifiesto, no por la tarjeta del servidor.
- Falta (partes B y C): fotos adjuntas con retención (30 días, borrar/archivar), push al recibir un mensaje, grupo «vivo» que sigue al rol, pantalla de Mensajes en la app del chofer, y en la web: responsables en la ficha de la ruta/zona, ajuste en la ficha del usuario, «Hablar sobre esta orden» y la pestaña «Mensajes» de la orden.

---

# Lote 212 — Parte B (servidor): fotos con retención, aviso al teléfono y grupo vivo por rol

## Qué se construyó
- **Fotos en el chat**: `MessageAttachment` (referencia) + contenido y miniatura cifrados en el almacén de archivos (`IFileStore`, que ganó `DeleteAsync`). Hasta 4 por mensaje, JPG/PNG/WEBP, 3 MB. Un mensaje puede ser solo fotos. Se bajan con `GET /messages/attachments/{id}` (solo quien es parte del hilo). Sección 2026-10-23 (j) del SQL.
- **Retención**: `Tenant.MessagePhotoDays` (30) y `MessagePhotoAction` (`DELETE` | `ARCHIVE`); `GET/PUT /messaging-settings` (`admin.tenant`); trabajo `MESSAGE_ATTACHMENT_PURGE` (03:45). Borrar quita el contenido; archivar lo conserva y deja de mostrarlo. El texto nunca se borra.
- **Aviso emergente por mensaje**: `DriverPushService.SendToUsersAsync` (FCM) a los choferes que no silenciaron el hilo; si falla, el mensaje igual queda guardado.
- **Grupo vivo de un rol**: `MessageThread.FollowsRoleId` (uno por rol y compañía) y `MessageThreadMember.ViaRole`; se sincroniza al escribir y al abrir. Quien entra al escribirle (el chofer) se queda; quien entró por el rol y lo pierde sale.

## Cómo se probó
8 pruebas nuevas (`TripStatusEffectTests.MessagingB.cs`) + 2 de controlador/trabajo: foto con miniatura y acceso solo de miembros, validaciones con mensajes exactos, vencimiento con DELETE y ARCHIVE (el texto queda, el archivo sale o se conserva en disco), ajustes validados, push (se manda, no se manda si está silenciado, token muerto se desactiva), grupo de rol (uno solo, entra y sale quien corresponde, el chofer se queda, no se agregan personas a mano).
Suite completa: ver el cierre del commit. **No probado**: Firebase real, SQL Server real, ni miniaturas en Linux.

## Decisiones a revisar
- **Miniaturas en Linux**: dependen de `libSkiaSharp`, que viaja con el paquete en Windows pero **no** en Linux (aquí no está disponible). Si el servidor fuera Linux hay que agregar `SkiaSharp.NativeAssets.Linux.NoDependencies`. Sin ella el mensaje se guarda igual y se sirve la foto completa.
- **Archivar** hoy es «ocultar y conservar» en el mismo disco; el traslado a un almacén en frío llega con el almacenamiento en la nube.
- El aviso al teléfono es solo para **choferes** (los dispositivos están ligados al chofer); la app del personal necesitará su propio registro de dispositivos por usuario.
- Una foto subida antes de que falle el mensaje deja un archivo huérfano en el almacén (se sube antes de la transacción). Es poco probable y de pocos KB; si molesta, se limpia con un barrido.
- El grupo de rol no admite agregar personas a mano; para una conversación a medida se crea un grupo normal.

---

# Lote 212 — Parte C (app del chofer): mensajes, fotos y teléfonos pulsables

## Qué se construyó
- **Pestaña «Mensajes»** (insignia de no leídos; todas las compañías del chofer): lista, **Nueva**, conversación con burbujas, fotos (cámara/galería, hasta 4; miniaturas con la firma de la compañía; visor a pantalla completa), tarjeta de la orden o ruta, «Sin enviar» para lo que espera conexión. Escribir en un hilo existente va por la **cola sin conexión** (reusa `Idempotency-Key`, no se duplica); empezar una conversación necesita conexión.
- **«Hablar sobre este envío»** en el detalle del envío → **Nueva conversación** con la orden adjunta y los **sugeridos** del servidor (ruta, zona, despachadores, recientes). Un grupo de despachadores se crea/reusa con `followRole`.
- **Aviso emergente**: tocar un mensaje abre su conversación (el servidor ahora manda también `tenant` en el aviso).
- **Enlaces**: `LinkText` vuelve pulsables teléfonos (`tel:` normalizado a +1), correos y direcciones web en los mensajes, los avisos y el teléfono del detalle del envío.
- SQL: sección 2026-10-23 (l) (todo rol con `driverapp.use` recibe `messages.use`). Manual 04 y FAQ.

## Cómo se probó
- `npm test` (nuevo `test:chat`: 25 comprobaciones de detección de enlaces y reglas del chat), `tsc --noEmit`, `eslint` de lo tocado (sin errores ni advertencias), contraste AA de la paleta y `expo export --platform web`. En el servidor: la prueba del aviso ahora exige `tenant`.
- **No probado en un teléfono ni contra el API real**: el recorrido completo (cola sin conexión, cámara, toque del aviso) lo debe hacer Luis con el APK.

## Decisiones a revisar
- Un mensaje con **error permanente** del servidor (p. ej. una foto que el servidor rechaza) hoy desaparece de «Sin enviar» sin avisar: la cola no muestra los fallidos. Las validaciones de la app (máx. 4 fotos, fotos reducidas) lo hacen raro; si molesta, se agrega una lista de «no enviados».
- La **foto de la miniatura** se pide al servidor cada vez (no se guarda en el teléfono); el sistema puede dejarla en su caché temporal. Si la señal es mala, verá el texto «Foto no disponible».
- **Tablet**: los teléfonos son enlaces `tel:`; sin llamadas el sistema no hace nada.
- El visor de la foto usa el fondo del tema (no negro): se decidió por la regla de colores del proyecto.
- **Falta** (parte D, web): responsables en la ficha de ruta/zona, ajuste «Mensajería» en la ficha del usuario, retención de fotos, fotos en el chat web, «Hablar sobre esta orden», pestaña «Mensajes» de la orden y los teléfonos pulsables de la web.
