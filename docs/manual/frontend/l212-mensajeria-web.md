# L212 — Mensajería en la web: fotos, hablar sobre una orden, responsables y ajustes

> Capturas pendientes: se toman con el recorrido de Playwright contra el API real (este lote se verificó con pruebas de Vitest sobre un API simulado). Las pantallas son las del [L53](l53-mensajes.md) con lo que se agrega abajo. El funcionamiento del servidor está en el [capítulo 15](../15-mensajeria.md) (§8 y §9).

## 1. En la conversación (`/messages`)
- **Teléfonos, correos y direcciones web son enlaces**: el teléfono abre el marcador (en un celular, con el número puesto), el correo abre el correo y la dirección web se abre en otra pestaña. El mensaje se guarda tal cual.
- **Tarjeta de la orden o de la ruta**: si el mensaje habla de una orden o ruta, arriba de su texto sale una tarjeta con el número (o código), el cliente (o la zona) y el estatus. **Tocarla abre la ficha** de la orden o la ruta en Despacho. Quien no tiene permiso para ver órdenes o rutas solo ve el número, sin enlace.
- **Fotos**: el botón **📷** junto a la caja de escribir adjunta hasta **4 fotos** (JPG, PNG o WEBP). Se **reducen en el navegador** antes de subirse y se ven como miniaturas con una **✕** para quitarlas antes de enviar. Un mensaje puede ser **solo fotos**. En el hilo, tocar una miniatura la abre completa.
  - Si la foto ya se **borró** o se **archivó** (viven 30 días por omisión), en su lugar se ve «Foto borrada» o «Foto archivada»; el texto del mensaje sigue ahí.
  - Mensajes de error: «Solo se admiten fotos JPG, PNG o WEBP.», «Un mensaje puede llevar hasta 4 fotos.», «No se pudo leer la foto.» Si el envío falla, **no se pierden ni el texto ni las fotos**.

## 2. Hablar sobre una orden o una ruta
- En la **ficha de la orden**: el panel **Mensajes** (columna derecha) lista los mensajes de *sus* conversaciones que hablan de esa orden (tocar uno abre la conversación) y su botón 💬 **Hablar sobre esta orden**.
- En **Despacho → ruta activa**: el botón 💬 de la cabecera de la ruta.
- Abre **Nuevo mensaje** con «Sobre 2026-0001» y, arriba, los **Sugeridos**: el responsable de la ruta, el de la zona, los despachadores y las conversaciones recientes (cada uno dice de dónde sale). Tocar un sugerido lo agrega como destinatario; un sugerido que es un **grupo de despachadores** se manda al grupo «vivo» de ese rol (uno solo por compañía). Debajo sigue el buscador normal de personas.
- El primer mensaje lleva la tarjeta de la orden o ruta.

## 3. Responsables de una ruta o una zona
- En **Despacho → ruta activa**: la línea «Responsables: …» con el botón ✎ (con `trips.plan`). Una ruta nueva nace con quien la creó.
- En **Flota → Zonas de despacho**: la acción de fila **Responsables** (con `fleet.manage`).
- El diálogo lista a los responsables con una ✕ para quitarlos y un buscador para agregar; **Guardar** deja exactamente esa lista. Mensajes: «Responsables guardados.»; sin permiso, «No tiene permiso para cambiar los responsables (trips.plan).» (o `fleet.manage`).

## 4. Quién participa: columna «Mensajería» en Sistema → Usuarios
- Muestra por persona **Completa**, **Solo con la oficina** o **Sin mensajería** (con `admin.users`). Las acciones de fila **Mensajería: …** cambian al modo pedido (no repiten el que ya tiene). Los usuarios ligados a un chofer quedan en «Solo con la oficina».

## 5. Cuánto viven las fotos: Ajustes de la compañía → Operación → «Mensajería: fotos del chat»
- **Días que viven las fotos** (1 a 3650, 30 por omisión) y **Al vencer**: «Borrar la foto» (se quita el contenido del servidor) o «Archivar (ocultar y conservar)». Con `admin.tenant`; sin él es de solo lectura. Validación: «Los días deben estar entre 1 y 3650.»
- El texto de los mensajes nunca se borra. La evidencia (entrega, excepción, daño) no va en el chat.

## 6. Teléfonos que se pueden llamar
- Los teléfonos de la ficha del consignatario, de las personas del cliente y de los contactos son enlaces `tel:` (con el código de país). En una computadora sin llamadas el sistema decide qué hacer con el enlace.
