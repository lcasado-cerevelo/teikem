# L118 — Registrar la entrega desde la web (web del personal)

Pantallas del Lote 118 en **Operación → Órdenes → ficha de la orden**. Capturas tomadas por el recorrido `web-app/e2e/lote118-entrega-web.spec.ts` contra el API real (escritorio y móvil a 360 px sin scroll horizontal). Reglas, API y mensajes: [capítulo 36](../36-registrar-entrega-web.md).

## La ficha de una orden en tránsito
Con el permiso `orders.deliver` y la orden en curso aparece el botón **Registrar entrega**.

![Ficha en tránsito](img/l118-ficha-en-transito.png)

## El diálogo
Avisa que **el comprobante se firma automáticamente a su nombre** y que el consignatario no firma. **Quién recibió** es obligatorio; la fecha y hora y la nota son opcionales.

![Diálogo](img/l118-dialogo.png)

Sin quién recibió no se manda nada:

![Sin receptor](img/l118-sin-receptor.png)

## La orden entregada y su comprobante
La orden queda **Entregada** (los estatus que faltaban quedan «brincados» en el historial) y la columna derecha muestra el **Comprobante de entrega**: quién recibió, cuándo, quién lo registró, «Firmada automáticamente por [usuario]», la ubicación y el aviso «El consignatario no firmó».

![Comprobante](img/l118-comprobante.png)
