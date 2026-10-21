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
