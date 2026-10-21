# 32 — Devolución de órdenes

Lote 112 (2026-10-18, dictado por Luis). Una orden que no se pudo entregar vuelve al almacén y alguien con permiso **decide devolverla**: la orden pasa a **«Devuelta»** (estatus final) y se crea la **orden de vuelta** (round trip) con las piezas que se devuelven.

**Quién puede:** `orders.return` (los administradores de la compañía ya lo traen) con el módulo **LTL_GROUND**. **Dónde:** la ficha de la orden (botón **Devolver**) y la estación de escaneo (acción **Devolver**, que solo ven quienes tienen el permiso).

## 1. Qué pasa al devolver
- La orden queda **Devuelta** (final: ya no se edita, ni se cancela, ni se asigna). Reabrirla exige `orders.reopen` **y que su orden de vuelta esté cancelada**: una orden que ya salió a devolución no se reabre mientras la vuelta siga viva (primero se cancela la vuelta).
- Se crea la **orden de vuelta**: una orden de entrega nueva del **mismo cliente** y el mismo tipo de servicio; su **recogido es el consignatario** (la entrega de la original) y su **entrega es la dirección del cliente que puso la orden**: el recogido de la original o, si no tenía, el recogido del cliente y, si tampoco, la dirección que el cliente tenga registrada (recogido, corporativa o facturación). Conduce y número de envío nuevos.
- Viajan **solo las piezas que se devuelven**: las mismas piezas con **sus mismos números** (los labels siguen valiendo); salen de la original y entran a la orden de vuelta. Las que no se devuelven siguen su flujo (si son faltantes, en Reclamaciones; ver capítulo 31).
- **Sin facturación de flete:** la orden de vuelta no se cotiza ni se cobra. Si el contrato cobra **por entrega**, a la original se le quitan las piezas devueltas (lo que no se entregó no se cobra); si cobra **por recogido**, se cobra todo (capítulo 31, §4.2).
- Se puede devolver una orden **Recogida, Sin asignar, Asignada, En tránsito o con Intento fallido**. Una **Sometida** no se devuelve (todavía no se recoge: se cancela). Una orden ya final (Entregada, Cancelada, En almacén…) tampoco.

## 2. Desde la ficha: botón «Devolver»
Se marcan las piezas que se devuelven (por omisión todas; las faltantes no se pueden devolver) y una nota opcional. La orden de vuelta **nace Sometida** y se trabaja como cualquier orden (recoger, llegó, enrutar, salir). Endpoint: `POST /api/v1/orders/{publicId}/return` con `{ pieceNumbers, notes }` (sin números, todas las que no son faltantes).

## 3. Desde la estación: acción «Devolver»
El guarda escoge **Devolver** y escanea **las piezas que devuelve**, una por una:
- La **primera** pieza deja la orden **Devuelta** y crea la orden de vuelta, que **nace Sometida pero «brincada»** hasta **Recogida** (el estatus de la escaneada: ya la tenemos en el almacén). Las **siguientes** piezas de la misma orden se suman a esa orden de vuelta mientras siga en Sometida o Recogida.
- Repetir una pieza es **duplicado** («Ok»). Una pieza faltante responde «No».
- Al **Cerrar conteo** de la orden devuelta, la estación muestra la **lista de las piezas que no se escanearon** (no se devolvieron) y pide **«Confirmar y cerrar el conteo»**; al confirmar quedan «Por verificar» (de ahí siguen en Reclamaciones).

## 4. Cargo por devolución (opcional, por contrato)
Cada contrato puede cobrar un **cargo por devolución** (**Cliente → Contrato → Cargo por devolución**, `contracts.update`; por omisión **sin cargo**). Tres modos:
| Modo | Qué cobra |
|---|---|
| **Por orden** | Un monto por orden devuelta (el mismo para todos los tipos de paquete); se cobra una sola vez aunque se devuelvan más piezas después. |
| **Por tipo de paquete** | El monto de cada tipo de paquete que se devuelve (un tipo sin monto no cobra). |
| **Por pieza devuelta** | Un monto por cada pieza que viaja en la orden de vuelta. |
- El cargo se **le suma a la orden devuelta** al devolverla (botón o escaneo; con el modo por pieza o por tipo crece al sumar piezas) y se **factura aparte del flete** como su propia línea **«ORD-… · Cargo por devolución»** (recargo, con su impuesto), cuando la orden está Devuelta; entra en la corrida de facturación del flete (**Incluir flete**). Cancelar la corrida o anular la factura lo libera.
- La **orden de vuelta** sigue sin flete ni cargo. Una orden sin contrato o con contrato sin cargo no cobra nada.
- API: `PATCH /api/v1/contracts/{publicId}/return-fee` con `{ mode, amount, byType:[{packageType, amount}] }` (modo vacío = sin cargo; la lista por tipo se reemplaza completa).

## 5. «Devolver tras N intentos» (sugerencia)
Cada compañía define con cuántos **intentos fallidos** se **sugiere** devolver una orden (**Ajustes de la compañía → General → Devolución**, `admin.tenant`; por omisión **2**; **0** = nunca sugerir; de 0 a 20). Es **solo una sugerencia y una marca**: nunca bloquea y la orden se puede seguir reintentando.
- **En la ficha de la orden** aparece «Se sugiere devolverla: N intento(s) fallido(s)» junto al estatus, y en una orden de vuelta se lee «Vuelta de {orden}».
- **En la estación de escaneo** el mensaje del escaneo de una orden con esos intentos termina con «Lleva N intento(s) fallido(s): se sugiere devolverla.» (el escaneo se hace igual).
- Cuentan los intentos de entrega **vigentes y no entregados** (los anulados no cuentan). No se sugiere en una orden Sometida ni en una que ya terminó (Entregada, Cancelada, En almacén, Devuelta).
- API: `GET/PUT /api/v1/tenant/return-settings` (`{ suggestAfterAttempts }`; 400 «Indique con cuántos intentos fallidos se sugiere devolver: de 0 (sin sugerencia) a 20.»).

## 6. Mensajes de error
| Mensaje | HTTP | Cuándo |
|---|---|---|
| Un envío en «Sometido» no se devuelve: todavía no se recoge. Cancélelo. | 422 | Devolver una orden en Sometida. |
| La orden {n} está en un estatus final ({estatus}): no se devuelve. | 422 | Devolver una orden Entregada, Cancelada, En almacén… (por escaneo el resultado es «final»). |
| Indique al menos una pieza que se devuelve. | 400 | No quedó ninguna pieza que devolver. |
| La pieza {n} no es de esa orden. | 400 | Un número de pieza ajeno a la orden. |
| La pieza {n} figura como faltante: no se puede devolver. | 422 (ficha) / «No» (escaneo) | La pieza está Por verificar, Perdido o Reclamada. |
| El cliente no tiene ninguna dirección registrada (de recogido, corporativa o de facturación) a la que regresar el envío: regístrela en su ficha. | 422 | La orden no tiene recogido y el cliente no tiene ninguna dirección registrada. |
| La orden ya salió a devolución: cancele primero la orden de vuelta {n} para poder reabrirla. | 422 | Reabrir una orden Devuelta cuya vuelta sigue viva. |
| No tiene permiso para esta acción. | 403 | Falta `orders.return`. |
| Orden no encontrada. | 404 | Identificador inexistente (o de otra compañía). |
| Indique el cargo por devolución: por envío, por tipo de paquete o por pieza. | 400 | Modo desconocido en el contrato. |
| Indique el monto del cargo por devolución. | 400 | Modo por orden o por pieza sin monto. |
| El cargo por devolución no puede ser negativo. | 400 | Monto negativo. |
| Indique el monto de al menos un tipo de paquete. | 400 | Modo por tipo de paquete sin ningún monto. |
| El tipo de paquete {t} está repetido. / Tipo de paquete desconocido: '{t}'. | 400 | Lista por tipo con repetidos o con un tipo que no existe. |

## 7. Casos frecuentes
- *Devolví por error:* primero se **cancela la orden de vuelta**; después la orden Devuelta se reabre con `orders.reopen`. Mientras la vuelta siga viva no se reabre.
- *La orden tenía 4 piezas y solo escaneé 3 con «Devolver»:* al cerrar el conteo, la 4.ª queda «Por verificar» y se atiende en Reclamaciones.
