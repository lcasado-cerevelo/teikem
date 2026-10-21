# 32 — Devolución de envíos

Lote 112 (2026-10-18, dictado por Luis). Una orden que no se pudo entregar vuelve al almacén y alguien con permiso **decide devolverla**: el envío pasa a **«Devuelto»** (estatus final) y se crea el **envío de vuelta** (round trip) con las piezas que se devuelven.

**Quién puede:** `orders.return` (los administradores de la compañía ya lo traen) con el módulo **LTL_GROUND**. **Dónde:** la ficha del envío (botón **Devolver**) y la estación de escaneo (acción **Devolver**, que solo ven quienes tienen el permiso).

## 1. Qué pasa al devolver
- El envío queda **Devuelto** (final: ya no se edita, ni se cancela, ni se asigna). Reabrirla exige `orders.reopen` **y que su orden de vuelta esté cancelada**: una orden que ya salió a devolución no se reabre mientras la vuelta siga viva (primero se cancela la vuelta).
- Se crea el **envío de vuelta**: un envío de entrega nueva del **mismo cliente** y el mismo tipo de servicio; su **recogido es el consignatario** (la entrega de la original) y su **entrega es la dirección del cliente que puso el envío**: el recogido de la original o, si no tenía, el recogido del cliente y, si tampoco, la dirección que el cliente tenga registrada (recogido, corporativa o facturación). Conduce y número de envío nuevos.
- Viajan **solo las piezas que se devuelven**: las mismas piezas con **sus mismos números** (los labels siguen valiendo); salen de la original y entran al envío de vuelta. Las que no se devuelven siguen su flujo (si son faltantes, en Reclamaciones; ver capítulo 31).
- **Sin facturación de flete:** el envío de vuelta no se cotiza ni se cobra. Si el contrato cobra **por entrega**, a la original se le quitan las piezas devueltas (lo que no se entregó no se cobra); si cobra **por recogido**, se cobra todo (capítulo 31, §4.2).
- Se puede devolver un envío **Recogido, Sin asignar, Asignado, En tránsito o con Intento fallido**. Una **Sometido** no se devuelve (todavía no se recoge: se cancela). Un envío ya final (Entregado, Cancelado, En almacén…) tampoco.

## 2. Desde la ficha: botón «Devolver»
Se marcan las piezas que se devuelven (por omisión todas; las faltantes no se pueden devolver) y una nota opcional. El envío de vuelta **nace Sometido** y se trabaja como cualquier orden (recoger, llegó, enrutar, salir). Endpoint: `POST /api/v1/orders/{publicId}/return` con `{ pieceNumbers, notes }` (sin números, todas las que no son faltantes).

## 3. Desde la estación: acción «Devolver»
El guarda escoge **Devolver** y escanea **las piezas que devuelve**, una por una:
- La **primera** pieza deja el envío **Devuelto** y crea el envío de vuelta, que **nace Sometido pero «brincado»** hasta **Recogido** (el estatus de la escaneada: ya la tenemos en el almacén). Las **siguientes** piezas de el mismo envío se suman a ese envío de vuelta mientras siga en Sometido o Recogido.
- Repetir una pieza es **duplicado** («Ok»). Una pieza faltante responde «No».
- Al **Cerrar conteo** del envío devuelto, la estación muestra la **lista de las piezas que no se escanearon** (no se devolvieron) y pide **«Confirmar y cerrar el conteo»**; al confirmar quedan «Por verificar» (de ahí siguen en Reclamaciones).

## 4. Cargo por devolución (opcional, por contrato)
Cada contrato puede cobrar un **cargo por devolución** (**Cliente → Contrato → Cargo por devolución**, `contracts.update`; por omisión **sin cargo**). Tres modos:
| Modo | Qué cobra |
|---|---|
| **Por orden** | Un monto por orden devuelta (el mismo para todos los tipos de paquete); se cobra una sola vez aunque se devuelvan más piezas después. |
| **Por tipo de paquete** | El monto de cada tipo de paquete que se devuelve (un tipo sin monto no cobra). |
| **Por pieza devuelta** | Un monto por cada pieza que viaja en el envío de vuelta. |
- El cargo se **le suma al envío devuelto** al devolverla (botón o escaneo; con el modo por pieza o por tipo crece al sumar piezas) y se **factura aparte del flete** como su propia línea **«ORD-… · Cargo por devolución»** (recargo, con su impuesto), cuando el envío está Devuelto; entra en la corrida de facturación del flete (**Incluir flete**). Cancelar la corrida o anular la factura lo libera.
- El **envío de vuelta** sigue sin flete ni cargo. Un envío sin contrato o con contrato sin cargo no cobra nada.
- API: `PATCH /api/v1/contracts/{publicId}/return-fee` con `{ mode, amount, byType:[{packageType, amount}] }` (modo vacío = sin cargo; la lista por tipo se reemplaza completa).

## 5. «Devolver tras N intentos» (sugerencia)
Cada compañía define con cuántos **intentos fallidos** se **sugiere** devolver un envío (**Ajustes de la compañía → General → Devolución**, `admin.tenant`; por omisión **2**; **0** = nunca sugerir; de 0 a 20). Es **solo una sugerencia y una marca**: nunca bloquea y el envío se puede seguir reintentando.
- **En la ficha del envío** aparece «Se sugiere devolverlo: N intento(s) fallido(s)» junto al estatus, y en un envío de vuelta se lee «Vuelta de {envío}».
- **En la estación de escaneo** el mensaje del escaneo de un envío con esos intentos termina con «Lleva N intento(s) fallido(s): se sugiere devolverlo.» (el escaneo se hace igual).
- Cuentan los intentos de entrega **vigentes y no entregados** (los anulados no cuentan). No se sugiere en un envío Sometido ni en una que ya terminó (Entregado, Cancelado, En almacén, Devuelto).
- API: `GET/PUT /api/v1/tenant/return-settings` (`{ suggestAfterAttempts }`; 400 «Indique con cuántos intentos fallidos se sugiere devolver: de 0 (sin sugerencia) a 20.»).

## 6. Mensajes de error
| Mensaje | HTTP | Cuándo |
|---|---|---|
| Un envío en «Sometido» no se devuelve: todavía no se recoge. Cancélelo. | 422 | Devolver un envío en Sometido. |
| El envío {n} está en un estatus final ({estatus}): no se devuelve. | 422 | Devolver un envío Entregado, Cancelado, En almacén… (por escaneo el resultado es «final»). |
| Indique al menos una pieza que se devuelve. | 400 | No quedó ninguna pieza que devolver. |
| La pieza {n} no es de ese envío. | 400 | Un número de pieza ajeno al envío. |
| La pieza {n} figura como faltante: no se puede devolver. | 422 (ficha) / «No» (escaneo) | La pieza está Por verificar, Perdido o Reclamada. |
| El cliente no tiene ninguna dirección registrada (de recogido, corporativa o de facturación) a la que regresar el envío: regístrela en su ficha. | 422 | El envío no tiene recogido y el cliente no tiene ninguna dirección registrada. |
| El envío ya salió a devolución: cancele primero el envío de vuelta {n} para poder reabrirlo. | 422 | Reabrir un envío Devuelto cuya vuelta sigue viva. |
| No tiene permiso para esta acción. | 403 | Falta `orders.return`. |
| Envío no encontrado. | 404 | Identificador inexistente (o de otra compañía). |
| Indique el cargo por devolución: por envío, por tipo de paquete o por pieza. | 400 | Modo desconocido en el contrato. |
| Indique el monto del cargo por devolución. | 400 | Modo por orden o por pieza sin monto. |
| El cargo por devolución no puede ser negativo. | 400 | Monto negativo. |
| Indique el monto de al menos un tipo de paquete. | 400 | Modo por tipo de paquete sin ningún monto. |
| El tipo de paquete {t} está repetido. / Tipo de paquete desconocido: '{t}'. | 400 | Lista por tipo con repetidos o con un tipo que no existe. |

## 7. Casos frecuentes
- *Devolví por error:* primero se **cancela el envío de vuelta**; después el envío Devuelto se reabre con `orders.reopen`. Mientras la vuelta siga viva no se reabre.
- *El envío tenía 4 piezas y solo escaneé 3 con «Devolver»:* al cerrar el conteo, la 4.ª queda «Por verificar» y se atiende en Reclamaciones.
