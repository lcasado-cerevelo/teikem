# 43 — Contabilización de compras y de despachos

Lote 181 (2026-10-21). La **contabilización** lleva a su sistema contable **el costo de lo propio que entró y salió del almacén**. Hay dos pantallas en **Contabilidad**: **Contabilización de compras**
(lo que entró, por recibos confirmados) y **Contabilización de despachos** (lo que salió, por recolecciones y despachos). Cada una trabaja por **corridas**: junta lo del período, usted la revisa, la
**acepta** y la **exporta** con la plantilla de su compañía. Teikem **junta y exporta**; no emite el asiento en su sistema contable.

> **Regla acordada con el dueño (por confirmar).** Se contabiliza **lo que es de la compañía**: el inventario **propio** (productos sin cliente dueño). Lo que pertenece a un **cliente** (3PL) **no** se contabiliza, sin
> importar el documento por el que llegó (orden de compra, aviso de llegada o recibo a ciegas). Se usa el **costo ya congelado** de cada línea (la capa de costo del recibo, Lote 147; el costo de la salida, Lote 148): nada se recalcula.

## 1. Quién puede

| Qué | Compras | Despachos |
|---|---|---|
| Módulo | Compras (`PURCHASING`) | Inventario por lote y serie (`WMS_LOTSERIAL`) |
| Entrar a la pantalla | `purchaseaccounting.view` | `dispatchaccounting.view` |
| Ver corridas | `purchasing.view` | `billing.view` |
| Generar, aceptar y cancelar | `purchasing.manage` | `billing.generate` |
| Exportar y configurar la plantilla | `purchasing.manage` | `billing.export` |

Las plantillas de exportación (Contabilidad → la misma ventana de «Configurar…» de Facturación) se leen con cualquiera de los permisos de ver y se cambian con el permiso de exportar del tipo. El Admin de
compañía y Facturación ya traen estos permisos. Las plantillas necesitan que esté encendido **alguno** de los módulos de Facturación, COD, Liquidación a choferes, Compras o Inventario con lotes.

## 2. Qué entra en cada corrida

**Compras** — una línea por cada línea de **recibo confirmado** (con fecha de recepción) del período:
- solo productos **propios**; lo de un cliente queda fuera;
- cuenta lo **recibido** (lo dañado también: costó igual);
- **no** entran las devoluciones de una venta (no son una compra);
- el costo es el que la línea **congeló al confirmarse el recibo**: el de la línea de la orden de compra, si no el precio del aviso de llegada, si no el costo del producto (columna «Origen del costo»: `PO`, `ASN` o `PRODUCT`). Sin ninguno la línea va **sin costo** (en blanco) y la corrida lo avisa;
- un recibo que se **cierra por tandas** (Lote 174) cuenta cuando se cierra el recibo, no tanda por tanda.

**Despachos** — una línea por cada línea de **recolección o despacho vigente** del período (incluye el despacho manual y los despachos contra orden de venta o aviso de salida):
- solo productos propios;
- no entran las recolecciones **eliminadas** ni las líneas **revertidas**;
- el costo es el que la salida **congeló** al sacarse (según el método de valuación de la compañía).

**El período** son días de la compañía (zona horaria de Ajustes → Región y formatos): «desde» y «hasta» incluidos. Si no hay nada propio por contabilizar el servidor responde 422 y la pantalla no deja generar.

**Una línea nunca está en dos corridas vivas.** Lo que ya está en una corrida generada, aceptada o exportada no se vuelve a ofrecer; al **cancelar** una corrida sus líneas quedan libres.

## 3. Pantalla

**Contabilidad → Contabilización de compras / de despachos** (`/money/purchases`, `/money/dispatch`). La lista muestra número (`CMP-#####` / `DSP-#####`), período, líneas, líneas **sin costo**, total, estatus y cuándo se generó; filtro por estatus, buscador por número y exportación completa de la tabla.

1. **Generar corrida**: elija «Desde» y «Hasta» (por omisión, los últimos 30 días) y una nota opcional. Antes de generar se ve **lo que entraría**: total, número de líneas, aviso de líneas sin costo y el detalle (hasta 200 líneas en pantalla; el archivo trae todas). **Generar** crea la corrida (queda **Generada**) y abre su ficha.
2. **La ficha** muestra las fechas, quién generó, aceptó y exportó, el total y las líneas (fecha, recibo o recolección, orden o aviso, proveedor o cliente, producto con lote, cantidad, costo unitario y total; las líneas sin costo se marcan).
3. **Aceptar** congela la corrida (Generada → Aceptada).
4. **Exportar** (solo aceptada o exportada): elija la **plantilla**, ajústela con **Configurar…** (formato CSV/TXT, delimitador, formato de fecha, encabezado, columnas y su orden), mire el archivo con **Ver archivo** y **Descargar**. La **primera descarga** pasa la corrida a **Exportada**; se puede volver a descargar (otra plantilla, otra copia).
5. **Cancelar corrida** (generada o aceptada): sus líneas quedan libres para otra corrida.

## 4. Estatus y transiciones

| De → a | Quién | Efecto | Qué la bloquea |
|---|---|---|---|
| (nueva) → **Generada** | gestor del tipo | junta las líneas del período con su costo congelado y toma el número | nada por contabilizar (422) |
| Generada → **Aceptada** | gestor | queda congelada; guarda quién y cuándo | solo desde Generada |
| Aceptada → **Exportada** | exportador | primera descarga del archivo; guarda quién y cuándo | solo desde Aceptada |
| Generada o Aceptada → **Cancelada** | gestor | las líneas quedan libres | no se cancela una exportada |

## 5. Archivo de exportación

Una fila por línea, con los datos de la corrida repetidos. Cada alcance (`PURCHASE_ACCOUNTING`, `DISPATCH_ACCOUNTING`) tiene su catálogo de campos y su «Formato estándar» creado la primera vez:
corrida, período, línea, fecha, documento, orden o aviso, proveedor o cliente, almacén, producto, lote, cantidad, costo unitario, costo total, origen del costo (solo compras), moneda y total de la corrida. Dinero con dos decimales y punto; el costo unitario con hasta cuatro. El mismo archivo con la misma plantilla sale siempre igual.

## 6. Endpoints

Compras: `/api/v1/purchase-accounting`. Despachos: `/api/v1/dispatch-accounting`. Misma forma:

| Qué | Endpoint |
|---|---|
| Lista (`status`, `q`, `skip`, `take`) | `GET /api/v1/{purchase\|dispatch}-accounting` |
| Ficha con líneas | `GET /api/v1/{…}-accounting/{publicId}` |
| Ver lo que entraría (no guarda) | `POST /api/v1/{…}-accounting/preview` con `{periodStart, periodEnd}` |
| Generar | `POST /api/v1/{…}-accounting` con `{periodStart, periodEnd, note?}` |
| Aceptar | `POST /api/v1/{…}-accounting/{publicId}/accept` |
| Cancelar | `POST /api/v1/{…}-accounting/{publicId}/cancel` con `{comment?}` |
| Archivo sin cambiar nada | `GET /api/v1/{…}-accounting/{publicId}/export-preview?templatePublicId=` |
| Exportar | `POST /api/v1/{…}-accounting/{publicId}/export?templatePublicId=` |

Una corrida de compras no se ve desde despachos ni al revés (404 «Corrida no encontrada.»).

## 7. Mensajes de error

| Mensaje | HTTP | Cuándo |
|---|---|---|
| «El cuerpo de la solicitud es obligatorio.» | 400 | se mandó la solicitud sin cuerpo |
| «Indique el período (desde y hasta).» | 400 (`periodStart`) | falta una de las dos fechas |
| «El período no es válido: la fecha final no puede ser anterior a la inicial.» | 400 (`periodEnd`) | «hasta» antes de «desde» |
| «La nota no puede pasar de 500 caracteres.» | 400 (`note`) | nota muy larga |
| «No hay nada por contabilizar en el período.» | 422 | no hay líneas propias libres en el período |
| «Solo se acepta una corrida generada.» | 422 | aceptar una que ya no es Generada |
| «Solo se cancela una corrida generada o aceptada.» | 422 | cancelar una exportada o ya cancelada |
| «Solo se exporta una corrida aceptada o exportada.» | 422 | exportar una generada o cancelada |
| «Corrida no encontrada.» | 404 | no existe o es del otro tipo |
| «La corrida cambió mientras se guardaba. Revise y vuelva a intentar.» | 409 | otra persona la cambió a la vez |
| «Plantilla de exportación no encontrada.» · «La plantilla no es de facturación.» | 404 · 400 | plantilla inexistente o de otro alcance |

## 8. Casos frecuentes

- **«Mis compras salen con líneas sin costo».** La capa del recibo no tenía costo (ni la orden de compra, ni el aviso, ni el producto lo traían al confirmar). Se contabilizan igual, con el costo en blanco, para que contabilidad las complete. Poner el costo en el producto **después** no cambia una capa ya congelada.
- **«Eliminé una recolección que ya estaba en una corrida».** La corrida no se corrige sola (lo asentado se asentó): cancele la corrida si aún no está exportada y genérela de nuevo. Si ya se exportó, el ajuste se hace en el sistema contable.
- **«Recibí mercancía del cliente y no sale».** Es la regla: lo de un cliente no se contabiliza.
- **«Quiero exportar otra vez con otro formato».** Abra la ficha de la corrida exportada → Exportar → otra plantilla.
