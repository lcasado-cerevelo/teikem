# 44 — Recepción maestra («Recibir juntos»)

Lotes 182 y 183 (2026-10-21). Cuando un mismo camión (o un mismo grupo de llegadas) trae **varios documentos** —varios avisos de llegada y/o órdenes de compra— se pueden recibir **juntos**: se abre una
**recepción maestra** (`RCM-#####`) que reúne un recibo por documento. Cada documento **conserva su recibo de siempre** (`REC-#####`, con sus líneas, tandas, estatus y acomodo); la maestra los abre con el mismo
transporte y referencia, muestra el avance de todos y los **confirma juntos**.

> **Reglas decididas (mismas que la recolección maestra, 2026-10-21):** la maestra se arma **al abrir** los recibos (no se suman documentos después); es de **un solo dueño** (un cliente, o lo propio de la
> compañía) y de **un solo almacén**; para otro dueño u otro almacén, otra recepción. La maestra no hace nada nuevo con el inventario: cada recibo lo asienta como siempre.

## 1. Quién puede

- Módulo **Inventario por lote y serie (`WMS_LOTSERIAL`)**. Un documento de orden de compra además exige **`purchasing.receive`** y el módulo de compras (como un recibo normal contra orden de compra).
- Ver las maestras: **`inventory.view`**. Abrir, confirmar y eliminar: **`warehouse.receive`**.

## 2. Pantalla (Almacén → Recibo → pestaña «Maestros»)

La pestaña **Maestros** lista las recepciones maestras: número, dueño, documentos, recibido / esperado, estatus (**Abierta**, **Confirmada**, **Eliminada**) y fecha; filtro por estatus y buscador por número de la
maestra, de uno de sus recibos, avisos u órdenes de compra; se exporta completa.

1. **Recibir juntos**: lista los avisos de llegada esperando y sin recibo. Al elegir el primero, los de **otro dueño** o **otro almacén** se bloquean (con la razón al pasar el cursor). Hay que elegir **al menos dos** (hasta 20). Se puede escribir un transporte y una referencia (se copian a todos los recibos) y una nota de la maestra. **Recibir juntos (n)** abre un recibo por aviso y la ficha de la maestra.
2. **La ficha** muestra el dueño, el almacén, quién y cuándo la abrió, el avance («n de m recibos confirmados · recibido / esperado») y la tabla de recibos con su documento, estatus y cantidades. **Abrir recibo** lleva a la pantalla de siempre de ese recibo para capturar (cantidades, lotes, series, daño, posiciones).
3. **Confirmar todo**: confirma juntos los recibos que aún no lo están, con lo capturado en cada uno. Es **todo o nada**: si uno no se puede confirmar, no se confirma ninguno y se muestra su mensaje.
4. **Eliminar**: elimina la maestra y sus recibos mientras **ninguno esté confirmado**; los avisos quedan libres para recibirse de nuevo (un recibo con tandas ya asentadas no se elimina).

Estatus de la maestra (derivado de sus recibos): **Abierta** si algún recibo no está confirmado, **Confirmada** si todos lo están, **Eliminada** si se eliminó.

## 3. Endpoints (`/api/v1/receipts/groups`)

| Qué | Endpoint | Permiso |
|---|---|---|
| Lista (`search`, `status`, `skip`, `take` ≤ 100) | `GET /receipts/groups` | `inventory.view` |
| Ficha con sus recibos | `GET /receipts/groups/{publicId}` | `inventory.view` |
| Abrir (2 a 20 documentos) | `POST /receipts/groups` con `{documents:[{asnId \| purchaseOrderPublicId}], dockId?, stagingBinId?, carrier?, reference?, receivingMode?, note?}` | `warehouse.receive` |
| Confirmar todo | `POST /receipts/groups/{publicId}/confirm` con `{comment?}` | `warehouse.receive` |
| Eliminar | `DELETE /receipts/groups/{publicId}` → 204 | `warehouse.receive` |

## 4. Mensajes de error

| Mensaje | HTTP | Cuándo |
|---|---|---|
| «Elija al menos dos documentos para recibir juntos; para uno solo use el recibo normal.» | 400 (`documents`) | menos de dos |
| «Una recepción maestra admite como máximo 20 documentos.» | 400 (`documents`) | más de veinte |
| «Cada documento lleva un aviso de llegada o una orden de compra, uno solo.» | 400 (`documents[i]`) | un documento sin origen o con los dos |
| «El mismo documento está repetido.» | 400 (`documents[i]`) | un documento dos veces |
| «Los documentos deben ser del mismo dueño: «A» y «B» son de dueños distintos. Reciba por separado lo de dueños distintos.» | 400 (`documents[i]`) | un cliente distinto, o lo propio con lo de un cliente |
| «Los documentos deben ser del mismo almacén: «A» y «B» son de almacenes distintos.» | 400 (`documents[i]`) | otro almacén |
| «La nota no puede pasar de 500 caracteres.» | 400 (`note`) | nota larga |
| los de un recibo normal (aviso ya recibido o con recibo abierto, orden de compra sin pendiente, el recibo exige documento…) | 409 / 422 | el documento no se puede recibir; no queda nada abierto (sin efecto parcial) |
| «Todos los recibos de esta recepción maestra ya están confirmados.» | 422 | confirmar de nuevo |
| «No se puede eliminar la recepción maestra: alguno de sus recibos ya está confirmado.» | 422 | eliminar con algún recibo confirmado |
| «Recepción maestra no encontrada.» | 404 | no existe o es de otra compañía |

## 5. Casos frecuentes

- **«Un aviso no aparece en la lista de Recibir juntos».** Solo se ofrecen los avisos **esperando y sin recibo**: uno ya recibido, cancelado o con un recibo abierto (suelto o de otra maestra) no sale.
- **«Un recibo de la maestra tiene diferencia».** Cada recibo se confirma como siempre (con su diferencia, su aviso al cliente, su acomodo). La maestra solo los reúne.
- **«Quiero recibir por tandas un documento de la maestra».** Se abre ese recibo y se usan las tandas de siempre (capítulo 6); **Confirmar todo** cierra los que quedan abiertos.
- **Órdenes de compra:** «Recibir juntos» en la web ofrece también las órdenes de compra por recibir (enviadas o con recibido parcial). Son **lo propio** de la compañía: no se juntan con avisos de un cliente.

## 6. Desde la app de almacén («Recibir juntos / carga»)

En **Recibir**, antes de elegir el documento, **Recibir juntos (abrir carga)** abre una carga (un camión, un vagón, un contenedor… lo que llegue). Mientras esté abierto, se recibe **un documento tras otro como siempre** (escaneando o eligiendo de la lista, también por tandas) y **cada recibo viaja a la cola con la llave del camión**: el servidor los reúne en una **recepción maestra** (`RCM-#####`) que nace con el primero.

- **El primer documento fija el dueño** (el cliente del aviso, o lo propio si es una orden de compra o un aviso nacido de una). Un documento de otro dueño **no entra al camión** (la app lo avisa antes de empezar su recibo); para recibirlo hay que **Terminar carga**.
- La carga muestra cuántos documentos se han enviado. **Terminar carga** lo cierra; los recibos ya enviados quedan en su maestra.
- Funciona **sin señal**: la llave va con cada envío de la cola. Si un envío llega y la carga ya se eliminó en la web («La recepción maestra ya se eliminó; abra una nueva.»), ese envío se rechaza y se vuelve a recibir con un camión nuevo.
- Lo ya descrito para la web (avance de todos, «Abrir recibo», «Confirmar todo» de los que queden abiertos) se ve en la pestaña **Maestros**. Un recibo a ciegas no se puede meter a un camión (solo documentos).

Mensajes nuevos de la API (`groupKey` en `POST /receipts` y `POST /receipts/part`): «Este documento es de otro dueño que los de la recepción maestra RCM-#####: reciba por separado lo de dueños distintos.» y «Este documento es de otro almacén que los de la recepción maestra RCM-#####.» (400, campo `groupKey`); «Recibir juntos es solo de documentos: indique un aviso de llegada o una orden de compra.» (400); «La recepción maestra ya se eliminó; abra una nueva.» (409).

## Cómo se ve la maestra desde el recibo y desde «Llegadas y salidas» (Lote 186)

- Cada recibo que pertenece a una recepción maestra muestra una etiqueta **«Maestra RCM-…»** junto a su estatus, tanto en la lista de recibos como en su ficha.
- En **Llegadas y salidas** (encabezado de la pantalla) hay un botón **«Recepciones maestras»** (permiso `receipts.view`) que lleva directo a la pestaña de maestras de Recibo.
