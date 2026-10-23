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
