# 33 — Tipo de servicio «Pick up» (recojo y traigo)

Lote 114 (2026-10-18, dictado por Luis). Un tipo de servicio puede ser **pick up**: se recoge y se trae al almacén. Es distinto de los de recogido y entrega y se trata distinto.

**Quién lo configura:** `admin.catalogs` (Sistema → Listas → *Tipo de servicio* → **Ajustar**). **Quién escanea:** `trips.scan`, módulo **LTL_GROUND**.

## 1. Las dos marcas del tipo de servicio
En **Listas → Tipo de servicio → Ajustar** un valor tiene dos interruptores (por compañía):
| Marca | Qué hace |
|---|---|
| **Pick up (recojo y traigo)** | El envío termina en **En almacén** (estatus final) y **no sale a ruta ni a chofer**. |
| **Facturable** | Encendido = se cotiza y se cobra. **Apagado = no se cotiza ni se cobra**: el envío se somete sin pedir contrato ni tarifa, queda cotizada en cero y no cuenta contra el crédito del cliente. |

La compañía recibe sembrado el tipo **Pick up** (código `PICK_UP`, pick up y facturable). Las marcas se guardan en el `ExtraJson` del valor: `{"pickUp":true,"billable":true}`; sin marcas, un tipo de servicio no es pick up y sí es facturable.

## 2. El camino del envío
**Sometido → Recogido → En almacén** (final). En la estación de escaneo:
- **Recoger** la lleva a Recogido (como siempre).
- **Llegó** la lleva a **En almacén** (en lugar de «Sin asignar»); si estaba Sometido, Recogido queda registrada «brincado». Las **piezas siguientes** con «Llegó» se **cuentan** («2 de 3 piezas») aunque el envío ya esté En almacén.
- **Enrutar** y **Salir** responden «No»: *El envío {n} es de pick up: se queda en el almacén. Para sacarla cree otro envío.* (Si se quiere sacarla, es otro envío o el cliente viene a buscarla.)
- **Cerrar conteo** de un envío En almacén lista las piezas que nunca llegaron y, al confirmar, quedan «Por verificar» (Reclamaciones, capítulo 31).

## 3. Facturación
Un pick up **facturable** se cobra al llegar a **En almacén**, sea cual sea el modelo de cobro del contrato (por recogido, por entrega o mixto). Uno **no facturable** (cotizado en cero) no se factura nunca.

## 4. Mensajes
| Mensaje | HTTP | Cuándo |
|---|---|---|
| El envío {n} es de pick up: se queda en el almacén. Para sacarla cree otro envío. | 200 (resultado «No») | Escanear con Enrutar o Salir un envío de pick up. |
| No hay tarifa vigente para PICK_UP/… ; configure la tarifa en el contrato del cliente. | 422 | Someter un pick up **facturable** sin tarifa en el contrato (apague «Facturable» si no se cobra). |

## 5. Casos frecuentes
- *Quiero un pick up que no se cobre:* en Listas → Tipo de servicio → Pick up → Ajustar, apague **Facturable**.
- *Quiero un tipo de servicio propio de pick up:* cree un valor en la lista de tipos de servicio de la compañía y encienda **Pick up**.
- *Un envío de pick up se quedó en Recogido:* escanee **Llegó** para llevarla a En almacén.

## 6. Ida y vuelta (2026-10-22)
Un tercer interruptor del tipo de servicio, **Ida y vuelta**, marca los envíos que se **recogen en la tienda y se traen de vuelta** (en Colavora, el tipo «Roundtrip», RT). Cambia a qué dirección va el chofer:

- **La zona** del envío sale del código postal (o pueblo) de la **recogida**, no de la entrega. Así la ruta de zona agrupa lo que el chofer realmente recorre.
- **La parada de la ruta y el mapa** son las de la recogida: dirección, pin y hora estimada son los de la tienda. La entrega (el almacén del cliente, siempre el mismo) no sale como parada.
- **En Despacho se ve qué se visita y de qué tipo es:** cada parada y cada envío de «Sin asignar» lleva una etiqueta **Recoger** (ida y vuelta) o **Entregar**, y el **tipo de servicio** (p. ej. ROUNDTRIP). Pasar el cursor por **Recoger** explica que es un envío de ida y vuelta.
- **Dónde se enciende:** Listas → Tipo de servicio → Ajustar (o Editar) → **Ida y vuelta**. La migración de Colavora lo enciende sola para el tipo RT.
- **Si el envío cambia** de consignatario, de recogido o de tipo de servicio, la zona se recalcula con la regla de arriba. «Recalcular zonas» (Listas → Zonas de despacho) también la respeta.
- **Lo ya cargado** se arregla con `import-colavora --solo reparar,paradas`: pone la zona de la recogida a los envíos de ida y vuelta y pasa las paradas de las rutas vigentes de la entrega a la recogida. Las rutas que ya tenían esos envíos conservan su agrupación (la ruta de la zona de entrega); para reagruparlos por la zona de la recogida, sáquelos de la ruta en Borrador y vuelva a «Planificar el día».

Casos frecuentes: *«Todos los envíos de una ruta parecen ir al mismo cliente»:* eran de ida y vuelta mostrados por su entrega; ahora se muestra la tienda donde se recoge. *«Un envío de ida y vuelta no aparece en la zona que espero»:* su zona es la de la recogida; revise el código postal de la parada de recogida.
