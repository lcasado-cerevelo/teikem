# L133 — Integración por correo (web del personal)

Captura del recorrido `web-app/e2e/lote133-correo.spec.ts` contra el API real (escritorio y móvil a 360 px sin scroll horizontal). Reglas y errores: [capítulo 39, §6](../39-integraciones.md).

## Formulario (conector Correo)
**Buzón de correo** (IMAP, usuario, contraseña que no se vuelve a mostrar, carpeta y procesados), **qué correos se toman** (asunto y remitentes originales permitidos) y **qué se lee** (adjuntos por modo, nombre y tipo; el cuerpo con campos a leer por etiqueta, expresión regular o bloque de renglones, cada uno a su columna de la plantilla). En la lista, **Probar conexión**; **Subir archivo** acepta `.eml` para probar la configuración sin buzón.

![Correo](img/l133-correo.png)

## Lectores en tarjetas (2026-10-24)
Cada lector es una tarjeta con su marco y cuatro pasos: qué documento es (condiciones en filas), qué se hace con él, qué datos se leen y las revisiones. Detalle: [capítulo 39, §23](../39-integraciones.md).

![Lectores en tarjetas](img/l133-lectores.png)

El **asistente de lectura** arma las expresiones a partir de un documento de muestra: se toca un renglón, se dice de qué parte sale cada dato y muestra cuántos renglones del documento coinciden.

![Asistente de lectura](img/l133-asistente.png)

## Microsoft 365 (2026-10-24)
Tipo de buzón «Microsoft 365 (aplicación de Entra ID)»: Tenant ID, Client ID, dirección del buzón y secreto de la aplicación en lugar de servidor, puerto y contraseña. Pasos y mensajes: [capítulo 39, §24](../39-integraciones.md).

![Buzón de Microsoft 365](img/l133-microsoft365.png)
