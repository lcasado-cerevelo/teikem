# Lote 133 — P11: Integración por correo electrónico, para cualquier cliente (dictado de Luis, 2026-10-19)

## Qué se construyó
- **Reglas puras** (`EmailRules`, dominio): validación de la configuración con mensajes exactos; **remitente original** (X-Original-Sender, X-Original-From, Resent-From, Reply-To, bloque «De:» del cuerpo reenviado, From); remitentes permitidos por correo o dominio; comparación de texto (igual, contiene, empieza, termina, comodín, regex; sin acentos ni mayúsculas); adjuntos por tipo y modo (todos, el primero, por nombre); HTML → texto (sin scripts ni estilos); cuerpo → valores por **etiqueta**, **expresión regular** o **bloque de renglones**, con transformaciones y campos obligatorios; y el cuerpo convertido en CSV para la plantilla de la integración.
- **Lector de correo** (`IMailboxReader`): IMAP con MailKit (solo no leídos; filtro de asunto «contiene» en el servidor; hasta 50 por corrida; al terminar marca como leído y mueve a «Procesados»; nunca borra; probar conexión) y el mapeo de MIME/.eml (`MailMessageMapper`).
- **Contraseña cifrada** (`SecretProtector`): AES-256-GCM con llave por compañía y propósito derivada de `Files:MasterKey`; nunca sale en el API ni en los registros (el DTO solo dice `hasMailPassword`).
- **Ejecutor:** el correo pasa por las reglas y sus adjuntos y/o cuerpo por la MISMA plantilla (varias líneas por orden, reglas de «no pieza»); lo que no cumple va a la bandeja con su motivo; **Subir archivo acepta `.eml`** y lo procesa igual que uno recibido (para probar sin buzón); **Probar conexión** (`POST /{id}/email/test`).
- **Web:** conector «Correo» en el formulario (buzón, qué correos, qué se lee con los campos del cuerpo), «Probar conexión» y subida de `.eml` en la lista.

## Cómo se probó
- xunit `EmailRulesTests` (25: remitente original, remitentes permitidos, operadores de texto, adjuntos, HTML a texto, cuerpo por etiqueta/regex/bloque, CSV, validación con mensajes exactos, lectura de un `.eml` con MimeKit y cifrado de la contraseña por compañía y propósito).
- vitest `emailRules.test.ts` (formulario y cuerpo del API) y las de Integraciones.
- Playwright `lote133-correo.spec.ts` (escritorio y móvil) contra API y SQL reales: validaciones, la contraseña nunca vuelve, probar conexión y correr contra un servidor inexistente (Falló), un correo bueno por remitente original (HTML → etiquetas y bloque → orden con 2 piezas), el mismo (repetido), reenviado de remitente no permitido (bandeja), sin campo obligatorio (bandeja), asunto ajeno (ignorado), editar conservando la contraseña, y la pantalla.
- **No se probó contra un servidor IMAP real** (no hay uno disponible aquí): el lector IMAP es una interfaz delgada sobre MailKit; la lógica completa se probó con `.eml`. Conviene una prueba de humo con el buzón real de la primera integración.

## Decisiones a revisar
1. **Solo IMAP con contraseña** (contraseña de aplicación o buzón compartido con IMAP). Microsoft 365 con OAuth/Graph (sin contraseña) queda como mejora si Microsoft apaga el IMAP básico.
2. **Acuse al remitente** (responder «Orden confirmada»): no se hizo; el acuse es el aviso del sistema, como se decidió. Se agrega como paso de salida en el lote de salida.
3. **PDF y .eml adjuntos** no se leen (solo CSV/texto); un PDF adjunto se ignora por tipo.
4. **Idempotencia** por hash del contenido (adjunto o cuerpo leído) y por el número de envío del importador; no se usa el `Reference #` como clave aparte.
5. Los correos que no cumplen el asunto se **ignoran sin rastro** (no son de la integración).
