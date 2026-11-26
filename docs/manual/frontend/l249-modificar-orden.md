# Modificar una orden de venta, adición, «Por aprobar», reabrir el empaque y unir al envío (pantallas, Lote 249)

Para el personal. Las reglas y los mensajes de error exactos están en el [capítulo 57](../57-modificaciones-ordenes-venta.md); aquí se ve dónde está cada cosa. Todas las pantallas se revisaron en claro y oscuro, en español e inglés, a 1440 px y a 390 px, sin scroll horizontal. Las capturas en inglés y las de celular están en `img/` con el mismo nombre y el sufijo `-movil-` o `-en-` (por ejemplo `l249-lineas-escritorio-en-dark.png` y `l249-lineas-movil-es-dark.png`).

## Operación › Órdenes de venta › la ficha de la orden
Una orden solicitada, parcial o despachada muestra los botones **«Modificar orden»** y **«Orden interna de adición»** (con el permiso `sales.modify`), el panel **«Recolecciones de la orden»** con la etapa de cada una y el panel **«Modificaciones»** (el historial). Una orden que es adición lleva la etiqueta **«Adición»** y «Adición de OV-…»; una orden con una modificación esperando la segunda aprobación, **«Modificación pendiente»**.

![Ficha de la orden](img/l249-ficha-escritorio-es-light.png)

## «Modificar orden»: paso 1, Líneas
Cada línea muestra Pedido, Recolectado, Empacado, Salió y Facturado; la cantidad con su **«Mínimo»**, el precio con el «Precio de lista», y los botones **«IVU manual»** y **«Quitar línea»**. Arriba sale la etapa de las recolecciones («Recolectada: se puede deshacer», «Empacada: se puede reabrir» o «Ya salió»). Una cantidad en cero muestra «Escriba una cantidad mayor que 0…» y frena el «Siguiente».

![Líneas](img/l249-lineas-escritorio-es-light.png)

**«IVU manual»** abre el porcentaje y el motivo de esa línea (permiso `sales.tax`); con el IVU puesto el botón dice «Cambiar IVU manual» y aparece «Quitar IVU manual».

![IVU manual en una línea](img/l249-lineas-iv-escritorio-es-light.png)

## Paso 2, Términos
Forma de pago, término, entrega o recogido, consignatario y notas. Si hay un empaque que se puede reabrir, aparece el interruptor **«Reabrir el empaque …»**; sin el permiso `warehouse.reopen-pack` el programa lo dice y sugiere la orden interna de adición.

![Términos](img/l249-terminos-escritorio-es-light.png)

## Paso 3, Autorización
Nombre de quien autorizó (obligatorio), cargo, contacto del cliente (opcional), cómo autorizó, fecha y hora (por omisión, ahora), motivo y evidencia (imagen o PDF; obligatoria si el cliente o la compañía lo exigen). Sin nombre, «Siguiente» muestra «Indique quién autorizó el cambio en el cliente.».

![Autorización con el error](img/l249-autorizacion-error-escritorio-es-light.png)
![Autorización](img/l249-autorizacion-escritorio-es-light.png)

## Paso 4, Vista previa
«Total antes → Total después», la diferencia línea por línea (Agregada, Quitada, Cambia), lo que pasa en el almacén (la recolección que se deshace y el empaque que se reabre), el crédito y si pide segunda aprobación. Los bloqueos salen en una lista con el texto exacto del servidor. **«Guardar la modificación»** aplica el cambio (o lo deja por aprobar). En la diferencia, la razón del IVU sale con su código (por ejemplo `TAXABLE`) y «IVU manual».

![Vista previa](img/l249-vista-previa-escritorio-es-light.png)

## El historial y el detalle
El panel **«Modificaciones»** de la ficha lista N.º, tipo, cuándo, quién registró, quién autorizó (cliente) y el cambio del total; cada fila mide a lo más 56 px. Un clic abre **«Modificación n.º N de OV-…»**: quién autorizó, cómo, el motivo, la evidencia, los efectos en el almacén y la diferencia. Cuando está pendiente, el detalle trae **«Aprobar y aplicar»**, **«Rechazar»** (con nota obligatoria) y **«Retirar»**.

![Historial](img/l249-historial-escritorio-es-light.png)
![Detalle de una modificación](img/l249-detalle-escritorio-es-light.png)

## Una modificación esperando la segunda aprobación
La ficha muestra el aviso «pendiente de segunda aprobación», que dice que la aprueba otra persona, y no se aplicó nada todavía.

![Pendiente](img/l249-pendiente-escritorio-es-light.png)

## Pestaña «Por aprobar»
La bandeja de la segunda aprobación (`sales.modify.approve`): orden, cliente, quién autorizó, motivo y diferencia del total. Paginada por el servidor, con buscador libre y orden por columna.

![Por aprobar](img/l249-bandeja-escritorio-es-light.png)

## La orden que solo admite adición
Una orden facturada o que ya salió completa muestra que «solo se le puede añadir» y ofrece **«Orden interna de adición»**. La orden original lista sus adiciones con su estatus y total.

![Ficha con la adición](img/l249-ficha-con-adicion-escritorio-es-light.png)
![Solo adición](img/l249-solo-adicion-escritorio-es-light.png)
![Orden interna de adición](img/l249-adicion-escritorio-es-light.png)

## Almacén › Recolección y empaque
- **«Reabrir el empaque»** (una recolección o una maestra empacada; `warehouse.reopen-pack`): pide el motivo y avisa que el envío se da de baja y que no se reabre si el flete o los productos ya están facturados.
- **«Unir al envío de OV-…»** (una recolección recolectada de una orden de venta): el selector «Unir con», el interruptor «Reabrir el empaque de …» si el destino ya está empacado, y el motivo.

![Unir al envío](img/l249-unir-envio-escritorio-es-light.png)

## Sistema › Ajustes › Operación › «Modificaciones de órdenes»
**Segunda aprobación** («No pide», «Siempre» o «Sobre un monto», con su monto) y **«La evidencia de la autorización es obligatoria»**. Se cambian con `admin.tenant`.

![Ajustes de la compañía](img/l249-ajustes-escritorio-es-light.png)

## Ficha del cliente › Perfil › «Modificaciones de órdenes»
Los mismos ajustes por cliente («Como la compañía» hereda), con la línea «Vigente para este cliente: …». Se cambian con `clients.update`.

![Ajustes del cliente](img/l249-cliente-escritorio-es-light.png)

## Pedido guiado
Quien tiene `sales.price` puede escribir el precio de cualquier línea, también de productos con precio de lista; el precio de lista queda al lado y la línea se marca como precio manual. La app del vendedor (`app-movil`) no cambia.
