# Lote 104 — Mensajes del portal con el equipo de servicio al cliente (portal de clientes, 8) (2026-10-17)

Luis pidió: mensajes entre el portal y el equipo de servicio al cliente, con **equipo designado por cliente** y, si no lo hay, **quien tenga el permiso/rol «Servicio al cliente»**. Decisiones suyas (preguntadas): **una conversación por persona** (nadie de su empresa ve la de otro) y **el equipo puede escribirle primero a una persona** desde el expediente del cliente.

## Qué se construyó
- **Dominio y SQL** (`Domain/Messaging/ClientMessaging.cs`, `update.sql` (am)): `ClientConversation` (única por cliente + persona, con último mensaje, vista previa y hasta dónde leyó el equipo y la persona), `ClientMessage` (inmutable), `ClientServiceTeamMember`; permisos `clientmessages.reply` y `clientmessages.manage` (categoría CLIENTS, propagados al administrador de la compañía) y la plantilla de rol **Servicio al cliente**.
- **Servicio** (`ClientMessagingService`): lado portal (la conversación sale de la sesión: persona y cliente nunca vienen del request; 2000 caracteres; 30 por hora → 429; marcar leído) y lado equipo (visibilidad: administrador todo, si no, clientes donde está designado + clientes sin equipo; lista, no leídos, detalle, responder —marca leído para todo el equipo—, escribir primero, personas del cliente, designar equipo con validación de candidatos, fallback).
- **API:** `/api/v1/portal/messages*` (4) y `/api/v1/client-messages/*` (10, módulo CLIENT_PORTAL); un usuario de portal recibe 403 en estos últimos.
- **app-cliente:** pestaña «Mensajes» (burbujas, cargar anteriores, contador, 429, sondeo 15 s, marcar leído) con **insignia de no leídos** (sondeo 30 s), tema claro/oscuro y es/en.
- **Web de la compañía:** bandeja «Mensajes de clientes» (`/portal/messages`) y, en la ficha del cliente, panel «Equipo de servicio al cliente» (designar equipo, nota de fallback) y «Escribirle a una persona».

## Cómo se probó
- xunit `ClientMessagingTests` (13): conversación por persona, aislamiento entre personas del mismo cliente, validaciones y 429, equipo/fallback/visibilidad, responder marca leído, escribir primero (persona de otro cliente → 400), candidatos, alcance por cliente; pines de permisos (96) y plantillas (8) actualizados. Suite completa.
- Contra SQL real (`Teikem_fg2`): dos personas del mismo cliente no se ven; el equipo ve las dos; responder y no leídos; portal → `/client-messages` 403; designar/quitar equipo; escribir primero.
- jest (+6) en app-cliente; vitest y Playwright en la web (ver manual de pantallas L104).

## Decisiones a revisar con Luis
1. **Sin aviso al equipo ni al cliente**: no hay correo ni notificación en vivo; el equipo ve el contador de no leídos en la bandeja (sondeo) y el cliente en la pestaña. El correo «el equipo le contestó» queda para cuando se decida la plantilla.
2. **Fallback = quien tenga `clientmessages.reply`**: para compañías existentes solo el administrador lo tiene hasta que creen el rol «Servicio al cliente» (la plantilla solo siembra compañías nuevas).
3. **Sin adjuntos** (solo texto, 2000 caracteres) y **sin editar ni borrar** mensajes.
4. Tope de **30 mensajes por hora por persona** (contra abuso); cambiar el número es una constante.
5. Contestar marca la conversación leída para **todo el equipo** (no hay «leído por cada uno»).
