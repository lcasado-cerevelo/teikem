# 57 — Modificaciones de órdenes de venta: cambiar una orden ya solicitada con la autorización del cliente

<!-- kemi:staff -->

Lote 249 (2026-11-26; Lote E del plan de Solutions). Seis cosas nuevas:

1. **La «Modificación de la orden».** Todo cambio a una orden ya solicitada (cantidad, precio, agregar o quitar líneas, IVU manual, forma de pago, entrega o recogido, consignatario y notas) pasa por una modificación que queda guardada con **quién la registró, quién la autorizó en el cliente, cómo, cuándo, el motivo, la evidencia, la foto de antes y después y los totales**.
2. **Lo permitido depende de la etapa real de la mercancía:** sin recolectar (todo), recolectada (se deshace **solo** la recolección necesaria), empacada (se reabre el empaque con permiso especial) y ya salida o facturada (solo se le añade).
3. **La orden interna de adición:** lo que ya no se puede cambiar se añade en una orden aparte que cuelga de la original y puede salir en el mismo envío.
4. **Segunda aprobación** (por compañía o por cliente): otra persona aprueba antes de que se aplique algo.
5. **IVU manual por línea** (permiso aparte, con motivo): afecta solo esa línea; la exención del cliente sigue valiendo para las demás.
6. **Facturación «cuando el usuario lo decida»** (`ON_REQUEST`): una base de facturación nueva, además de al despachar y al entregar.

> **Esto toca inventario y dinero.** Una modificación puede deshacer recolecciones (el inventario vuelve a su posición), reabrir empaques (se da de baja el envío) y cambiar el total de una orden. Lea la sección 3 (qué se puede en cada etapa) antes de dar el permiso `sales.modify` a alguien.

> **Ninguna acción de este capítulo manda un correo por sí sola.** Los avisos (sección 10) salen por las **reglas de aviso**; sin regla, el sistema anota el aviso y no lo entrega a nadie.

Pantallas con capturas: [L249](frontend/l249-modificar-orden.md). Cada mensaje de error está en la [FAQ](faq.md). Completa los capítulos [18 — Órdenes de venta](18-ordenes-de-venta.md), [19 — Facturación](19-facturacion.md) y [56 — Documentos con vigencia, exención de IVU](56-documentos-vigencia-ivu.md).

## 1. Dónde está y quién puede

- **Dónde:**
  - **Operación → Órdenes de venta** → abrir una orden → botón **«Modificar orden»**, panel **«Modificaciones»** (el historial) y botón **«Orden interna de adición»** (`/sales-orders`).
  - **Operación → Órdenes de venta → pestaña «Por aprobar»**: la bandeja de la segunda aprobación (solo con `sales.modify.approve`).
  - **Almacén → Recolección y empaque** → una recolección: **«Reabrir el empaque»** (empacadas) y **«Unir al envío de OV-…»** (recolectadas de una orden de venta).
  - **Sistema → Ajustes → Operación → «Modificaciones de órdenes»** (de la compañía) y **ficha del cliente → Perfil → «Modificaciones de órdenes»** (del cliente).
  - **Pedido guiado** (crear una orden): ahora quien tiene `sales.price` puede escribir el precio de **cualquier** línea, también de productos con precio de lista.
- **Módulos:** Órdenes de venta (`SALES_ORDERS`) para todo; Impuestos (`TAX`) para el IVU manual; Almacén para reabrir y unir. Con un módulo apagado la ruta responde 403 con `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` (con el nombre del módulo que corresponda).
- **Permisos.** Hay **cuatro permisos nuevos** (la compañía tiene ahora 167):

| Qué | Permiso | Módulo |
|---|---|---|
| Ver las opciones de edición, el historial y una modificación | `sales.view` | `SALES_ORDERS` |
| Vista previa, registrar, retirar, subir la evidencia y crear la adición | **`sales.modify`** (nuevo) | `SALES_ORDERS` |
| Cambiar el precio a uno distinto del de lista (de hoy) | además `sales.price` | `SALES_ORDERS` |
| IVU manual de una línea | **`sales.tax`** (nuevo) además de `sales.modify` | `SALES_ORDERS` y `TAX` |
| Segunda aprobación (aprobar, rechazar) y la bandeja «Por aprobar» | **`sales.modify.approve`** (nuevo) | `SALES_ORDERS` |
| Reabrir un empaque (en una modificación o suelto) | **`warehouse.reopen-pack`** (nuevo) | `SALES_ORDERS` |
| Unir una recolección al envío de su orden | `warehouse.pick` | `SALES_ORDERS` |
| Autorizar un crédito excedido al subir el total | `orders.credit_override` | `SALES_ORDERS` |
| Ajustes de la compañía | `admin.tenant` | `SALES_ORDERS` |
| Ajustes de un cliente | `clients.update` | `SALES_ORDERS` |
| Ver los ajustes | `sales.view` (compañía); `sales.view` o `clients.read` (cliente) | `SALES_ORDERS` |
| Leer la fuente de datos «Modificaciones de órdenes de venta» (`SALES_ORDER_CHANGE`) | `sales.view` | `SALES_ORDERS` |

- **Reparto a los roles de cada compañía** (también a los roles **ya clonados**, una sola vez; solo se agrega, nunca se quita): `sales.modify` a TenantAdmin, SalesSupervisor y SalesOffice; `sales.tax` y `sales.modify.approve` a TenantAdmin y SalesSupervisor; `warehouse.reopen-pack` **solo** a TenantAdmin. **El rol «Ventas» deja de poder cambiar cantidades de órdenes ya solicitadas** (antes bastaba `sales.create`): si hace falta, dele `sales.modify`.
- `TenantId` sale de la sesión. Una orden, modificación, archivo, contacto o producto de otra compañía responde 404, sin decir si existe.
- Sin el permiso, la ruta responde 403 con `Falta el permiso '…'.` y queda un evento `PERMISSION_DENIED` en los eventos de seguridad.

## 2. La modificación de la orden

**Qué hace.** Cambia una orden **solicitada, parcial o despachada** con las reglas de la sección 3. Antes de guardar se puede ver la **vista previa** (la diferencia, los efectos en el almacén, el crédito y si pide segunda aprobación) sin escribir nada.

**Cómo se usa (pantalla).** Abra la orden → **«Modificar orden»**. El asistente tiene cuatro pasos:
1. **Líneas.** Cada línea muestra *Pedido · Recolectado · Empacado · Salió · Facturado*, la **cantidad** con su **«Mínimo»** (a lo que se puede bajar), el **precio** (con el «Precio de lista»), **«IVU manual»** y **«Quitar línea»**. Arriba sale la etapa de cada recolección: «Recolectada: se puede deshacer», «Empacada: se puede reabrir» o «Ya salió». **«Agregar un producto»** y el interruptor «Si falta inventario, dejar lo que falte en backorder».
2. **Términos.** Forma de pago, término, entrega o recogido, consignatario y notas.
3. **Autorización.** «Nombre de quien autorizó» (obligatorio), «Cargo», «Contacto del cliente (opcional)», «Cómo autorizó» (por teléfono, por correo, en persona o por mensaje), «Fecha y hora de la autorización» (por omisión, ahora), «Motivo del cambio» y la **«Evidencia»** (imagen o PDF; obligatoria si el cliente o la compañía lo exigen).
4. **Vista previa.** «Total antes → Total después», la diferencia línea por línea, lo que pasa en el almacén, el crédito, si pide segunda aprobación y los bloqueos. **«Guardar la modificación»**.

Al guardar sin segunda aprobación sale «Modificación n.º N aplicada.». Con segunda aprobación sale «Modificación n.º N registrada: espera la segunda aprobación y no se aplicó nada todavía.».

**Cómo se usa (API).** Todas las rutas cuelgan de `/api/v1/sales-orders/{orderPublicId}`.

| Qué | Ruta | Permiso |
|---|---|---|
| Etapa, bloqueos, posición por línea, recolecciones, facturas, ajustes, pendiente y mis permisos | `GET …/edit-options` | `sales.view` |
| Vista previa (no escribe) | `POST …/changes/preview` | `sales.modify` |
| Registrar (y aplicar, o dejar por aprobar) | `POST …/changes` | `sales.modify` |
| Historial (la más reciente primero) | `GET …/changes` | `sales.view` |
| Una modificación con su antes, después, diferencia y efectos | `GET …/changes/{changePublicId}` | `sales.view` |
| Aprobar / rechazar / retirar | `POST …/changes/{changePublicId}/approve`, `/reject`, `/withdraw` | `sales.modify.approve` (aprobar y rechazar); `sales.modify` o quien la registró (retirar) |
| Subir la evidencia (multipart, `file`) | `POST …/change-attachments` | `sales.modify` |
| Orden interna de adición | `POST …/additions` | `sales.modify` |
| Ruta vieja de una línea | `PATCH /api/v1/sales-orders/{id}/lines/{lineId}` | `sales.modify` |

Registrar una modificación (`POST …/changes`). Lo que no viene no cambia.

```json
{
  "ops": [
    { "kind": "QTY",   "lineId": 41, "quantity": 6 },
    { "kind": "ADD",   "productPublicId": "0f8fad5b-d9cb-469f-a165-70867728950e", "quantity": 2 },
    { "kind": "REMOVE","lineId": 42 },
    { "kind": "TAX",   "lineId": 43, "taxPct": 1, "note": "Cliente con acuerdo de exención" }
  ],
  "authorizedByName": "Marta Ríos",
  "authorizedByTitle": "Compras",
  "authorizationChannel": "PHONE",
  "authorizedAtUtc": "2026-11-26T14:10:00Z",
  "reason": "El cliente llamó para cambiar las cantidades.",
  "attachmentFilePublicId": null,
  "reopenPack": false,
  "allowBackorder": false,
  "overrideCredit": false,
  "rowVersion": "AAAAAAAB9E="
}
```

**Operaciones (`ops[].kind`).**

| Kind | Campos | Qué hace |
|---|---|---|
| `ADD` | `productPublicId`, `quantity`, `unitPrice` (opcional) | Agrega un producto con el precio de hoy del cliente; un precio distinto exige `sales.price`. Si el producto estaba quitado, **reactiva su línea** con el mismo número |
| `QTY` | `lineId`, `quantity` | Cambia la cantidad pedida: sube (reserva) o baja (libera, o deshace recolecciones: sección 4) |
| `REMOVE` | `lineId` | Quita la línea: pedido 0 y cerrada (nunca se borra) |
| `PRICE` | `lineId`, `unitPrice` | Cambia el precio; toma el origen resuelto hoy |
| `TAX` | `lineId`, `taxPct`, `note` | IVU manual (sección 6); exige `sales.tax` |
| `CLEAR_TAX` | `lineId` | Quita el IVU manual de la línea |
| `TERMS` | valores en `terms` | Pago, término, entrega (`DELIVERY` o `PICKUP`), consignatario (`consigneePublicId`, `clearConsignee`) y consignación |
| `NOTES` | valor en `notes` | Notas de la orden (hasta 500) |

Mandar `terms` o `notes` agrega solo la operación `TERMS` o `NOTES`.

**Otros campos.** `authorizedByContactId` (contacto activo **del cliente de la orden**; si se escoge, queda registrado), `reopenPack` y `reopenReason` (sección 5), `allowBackorder` (deja en backorder lo que falte al subir o agregar), `overrideCredit` (sección 8), `rowVersion` (el de `edit-options`: si la orden cambió, 409).

**Reglas de los campos.**
- `authorizedByName`: obligatorio, hasta 150. `authorizedByTitle`: opcional, hasta 100.
- `authorizationChannel`: `PHONE`, `EMAIL`, `IN_PERSON` o `MESSAGE` (valores del catálogo «Canal de autorización de la modificación»).
- `authorizedAtUtc`: vacío = ahora; **no puede ser futura** (se toleran 5 minutos de reloj desfasado) **ni anterior a la creación de la orden**.
- `reason`: obligatorio, hasta 500.
- `attachmentFilePublicId`: un archivo subido antes con `change-attachments` (imagen o PDF, con los tamaños y tipos del almacén de archivos). **Un archivo respalda una sola modificación.**
- Una modificación que no cambia nada responde 400 «Indique al menos un cambio.».
- Una línea no puede venir dos veces con la misma operación, ni con `REMOVE` junto a otra, ni con `TAX` junto a `CLEAR_TAX`.
- No se pueden quitar todas las líneas: para eso se cancela la orden.

## 3. Qué se puede cambiar según la etapa

El sistema mira la **etapa real de cada recolección** de la orden. La pantalla y `GET …/edit-options` lo muestran antes de enviar (por línea: `minQuantity`, `minQuantityWithReopen`, `inCollected`, `inPacked`, `out`, `invoiced`, `lockedReason` y `priceLockedReason`).

| Etapa de la mercancía | Qué se puede | Cómo |
|---|---|---|
| **Pedida y reservada, sin recolectar** | Todo | Se reserva y se libera como siempre |
| **Recolectada** (COLLECTED, de una entrega) | Todo | Bajar por debajo de lo recolectado **deshace solo las recolecciones necesarias** (sección 4); el almacén vuelve a despachar |
| **Empacada** (PACKED), con su envío en la etapa inicial y sin nada facturado | Todo, **reabriendo el empaque** en la misma llamada | `reopenPack: true` con el permiso `warehouse.reopen-pack` (sección 5) |
| **Ya salió** | Lo que salió **no se toca** | No se baja ni se quita por debajo de lo salido; el precio y el IVU de esa línea tampoco cambian. Se le puede **añadir** |
| **Facturada** | **Solo se le añade** | Orden interna de adición (sección 7) |

**Qué cuenta como «ya salió»:** lo despachado que no está en una recolección que se pueda deshacer o reabrir. Entran: el **recogido** (`PICKUP`: lo recolectado ya se lo llevó el cliente), lo que **cuenta una compañía enlazada**, un **despacho manual**, un envío que ya pasó de su etapa inicial, anulado o con el flete facturado.

**Qué cuenta como «facturada»:** cualquier línea con cantidad facturada, o el cargo de envío ya facturado. Cuenta también el **borrador** de una corrida abierta y la orden **solo recolectada** que ya se facturó con la base «al despachar». Una orden facturada **solo admite adición**, aunque tenga líneas sin facturar, y no se le cambian ni las notas ni los términos. Quitar o bajar se hace con una nota de crédito.

**Otros bloqueos de toda la orden:**
- **Borrador:** se cambia desde su edición, no con una modificación.
- **Cancelada:** no se modifica.
- **Salió completa:** algo salió y no queda nada en el almacén (ni reservado, ni en backorder abierto, ni recolectado o empacado sin salir): solo se le puede añadir con una orden interna de adición.
- **Ya salió parte:** el pago, la entrega y el consignatario ya no se cambian.
- **Línea cerrada con faltante:** su cantidad ya no se cambia.

**Cuándo hay que reabrir el empaque:** para cambiar la cantidad, el precio o el IVU de una línea que está en un empaque, para quitarla, y para cambiar los términos si existe un empaque (el COD y el envío quedarían viejos). **Agregar líneas nuevas no lo exige:** van en otra recolección.

**Efecto en el estatus de la orden** (siempre por el servicio de estatus, con el comentario «Modificación n.º N»): una orden **despachada** que recibe algo por despachar vuelve a **parcial** (o a **solicitada** si nada quedó despachado); una **parcial** sin nada despachado vuelve a **solicitada**; una solicitada o parcial con todo terminado pasa a **despachada**.

## 4. Bajar lo ya recolectado: la reversa mínima

Si la cantidad nueva queda por debajo de lo que hay en recolecciones, el sistema **deshace recolecciones completas, pero solo las necesarias**:
- Primero las **recolectadas**, de la **más reciente a la más vieja**. Las **empacadas** solo si se pidió reabrir.
- Cada recolección deshecha **devuelve el inventario a su posición** (queda en el Kárdex como reversa de la recolección) y la línea **vuelve a reservarse**; la orden sigue viva. Después el almacén vuelve a despachar.
- **No hay reversa parcial:** una recolección se deshace entera. Si trae otras líneas, esas unidades también regresan.
- **Guarda global:** **no se deshace una recolección cuya mercancía ya se facturó** con la base «al despachar» o «al entregar», porque la factura emitida no se reabre: responde **409** «La recolección … ya está facturada (…): no se puede deshacer.». Se resuelve con una **devolución** o una nota de crédito. Con la base «cuando el usuario lo decida» (`ON_REQUEST`) lo facturado no cuenta (lo devuelto sí). Vale también al eliminar la recolección a mano desde Almacén.

## 5. Reabrir el empaque

**Qué hace.** Da de baja el envío (orden de transporte) de un empaque **mientras siga en su etapa inicial** y la recolección vuelve de **Empacada** a **Recolectada**. El número de empaque (EMP) y el del envío quedan libres y se reutilizan al volver a empacar. No sale aviso de estatus al cliente ni webhooks.

**Quién puede.** `warehouse.reopen-pack` (solo TenantAdmin por omisión), módulo `SALES_ORDERS`.

**Cómo se usa.**
- **Dentro de una modificación:** marque **«Reabrir el empaque …»** (`reopenPack: true`). Es **atómico**: si la modificación falla, el empaque no queda reabierto. Si hace falta reabrir y no se marcó, la modificación responde 422 con el nombre del empaque. Con segunda aprobación, el permiso se revisa a quien registra y el empaque se reabre **al aprobar**.
- **Suelto:** Almacén → Recolección y empaque → la recolección → **«Reabrir el empaque»** (pide el motivo).

| Qué | Ruta | Permiso |
|---|---|---|
| Reabrir una recolección suelta | `POST /api/v1/pick-batches/{id}/reopen-pack` | `warehouse.reopen-pack` |
| Reabrir una maestra (todas sus recolecciones) | `POST /api/v1/pick-batches/groups/{id}/reopen-pack` | `warehouse.reopen-pack` |

```json
{ "reason": "El cliente cambió la cantidad de la orden.", "rowVersion": "AAAAAAAB9E=" }
```

`reason` es obligatorio (hasta 500; va al historial como «Empaque reabierto: {motivo} (envío {ORD} anulado)»). En una modificación, si no viene `reopenReason`, el motivo es «Modificación n.º N de OV-…: {motivo}». `rowVersion` es opcional (409 si la ficha cambió).

**No se reabre** (422, ver la sección 12): una recolección de una maestra (se reabre desde la maestra); envío ya avanzado, anulado o sin envío; compañía enlazada; despacho manual; flete, cotejo o devolución facturados; factura del cliente ya procesada; **productos ya facturados** (con cualquier base de facturación: la adición sale en su propio envío).

**Aviso.** Reabrir manda «Empaque reabierto» (sección 10) por cada orden de venta atendida.

## 6. IVU manual por línea

**Qué hace.** Pone el IVU de **una línea** a un porcentaje que usted escribe, con motivo, en lugar del cálculo normal. **Solo afecta esa línea:** la exención del cliente y la del período por temporada siguen valiendo para las demás.

**Quién puede.** `sales.tax` además de `sales.modify`, con el módulo `TAX` encendido (si no, 422 «El IVU manual exige el módulo de impuestos encendido.»).

**Cómo se usa.** En el paso «Líneas»: **«IVU manual»** (porcentaje y motivo). **«Quitar IVU manual»** devuelve la línea a su cálculo normal, **con la exención del cliente si el documento sigue vigente**. Por API: `TAX` (con `taxPct` y `note`) y `CLEAR_TAX`.

**Reglas.**
- `taxPct`: obligatorio, de 0 a 100, hasta 4 decimales. **0 deja la línea exenta.**
- `note` (el motivo): obligatorio, hasta 250.
- El importe es **cantidad × precio × % / 100**, redondeado al centavo (mitad hacia arriba), y se **recalcula** si cambia la cantidad o el precio.
- La línea queda con la razón `MANUAL_OVERRIDE`, y guarda **lo que el sistema cobraría hoy** (`taxComputedRatePct` y `taxComputedAmount`), quién lo puso y cuándo. La vista previa muestra «Razón del IVU» y «IVU manual».
- **Pisa la exención por documento** (Lote 248) en esa línea; al quitarlo, vuelve el cálculo normal.
- Solo en una **modificación** de una orden ya solicitada: no se puede en el borrador ni en el pedido guiado.
- No se cambia el IVU de una línea que ya salió en parte ni de una quitada. Una línea en un empaque pide reabrirlo.

## 7. La orden interna de adición

**Qué hace.** Cuando una orden ya no admite cambios (facturada, ya salió completa, o empaque que no se reabre), lo que el cliente pide de más va en una **orden aparte** que cuelga de la original: la **adición**.
- Es una **orden normal** (con su número OV, su reserva, su recolección, su envío y su factura) con una referencia a su orden madre. Una adición de una adición cuelga de la **original** (la raíz).
- Se crea y se solicita **en una sola transacción**, con el alta y la solicitud de siempre (reserva, crédito, aviso «Orden solicitada»). **No pide la firma del cliente**: la autorización queda en la modificación de tipo «Adición» de la orden raíz.
- Copia de la madre el cliente, el almacén, el consignatario, la entrega, la forma de pago, la consignación, y el **vendedor y la ruta** (así comisiones y análisis la juntan con la original). Sus notas empiezan con «Adición de OV-…».
- Solo se añade a una orden **solicitada, parcial o despachada**.
- **No se cancela una orden con adiciones abiertas** (ni canceladas ni despachadas): primero se cancelan o se despachan.

**Cómo se usa.** Orden → **«Orden interna de adición»**: productos y cantidades, más la misma autorización del cliente. Por API: `POST …/additions`.

```json
{
  "lines": [ { "productPublicId": "0f8fad5b-d9cb-469f-a165-70867728950e", "quantity": 2 } ],
  "notes": "Entrega con la orden original",
  "authorizedByName": "Marta Ríos",
  "authorizationChannel": "EMAIL",
  "reason": "El cliente pidió dos cajas más.",
  "allowBackorder": false,
  "overrideCredit": false
}
```

**Salir en el mismo envío.** Almacén → la recolección de la adición → **«Unir al envío de OV-…»** (`POST /api/v1/pick-batches/{id}/join-order-shipment`, `warehouse.pick`). Es una **excepción acotada** a la regla «no se suman documentos después»:
- Solo con recolecciones de la **«familia»** de la orden (la misma orden, su original y las adiciones de la original), del mismo cliente y almacén.
- La recolección que se une debe estar **Recolectada**.
- Si el destino ya está **empacado**, pide **«Reabrir el empaque»** (`reopenPack: true`, `reason`, y el permiso `warehouse.reopen-pack`). La maestra que nace **toma el número EMP del envío reabierto**, así sirven las etiquetas que el cliente o el chofer ya vieron.
- Si hay varios destinos posibles, se escoge con `targetBatchPublicId`.

```json
{ "targetBatchPublicId": "7c9e6679-7425-40de-944b-e07fc1f90ae7", "reopenPack": true, "reason": "Sale con la orden original", "note": null }
```

Con documentos sin relación sigue valiendo «no se suman documentos después».

**Facturación de la adición.** Entra a las corridas normales: a la misma factura si cae en la misma corrida que su madre, y a otra si la madre ya se facturó. La referencia viaja en la **descripción de la línea** («OV-00045 (adición de OV-00040 · complementa FAC-00012) · SKU Nombre», recortada a 250), en el **MEMO de la factura del IIF** («OV-00045 (adición de OV-00040 · FAC-00012)», hasta 4000) y en `complements` de la factura. No hay columna nueva en la factura.

## 8. Crédito

- **Solo se revisa si el total sube** (con IVU y envío). Si baja o queda igual, no.
- **Excedido:** 422 `credit_exceeded` con el aviso de siempre a quien autoriza créditos. Para continuar, `overrideCredit: true` con el permiso `orders.credit_override`: queda marcado en la modificación y en los eventos de seguridad.
- Al **aprobar** se vuelve a revisar con la orden de hoy. Si quien registró ya autorizó el crédito, no se le pide de nuevo a quien aprueba.
- En una **adición** se revisa la orden nueva antes de abrir la transacción (excedido = 422 y no queda ni la adición ni el registro) y otra vez al solicitarla.

## 9. Segunda aprobación y evidencia obligatoria

**Qué hace.** Cada compañía (y cada cliente, que **manda** sobre la compañía) decide si una modificación necesita que **otra persona** la apruebe antes de aplicarse, y si la evidencia de la autorización es obligatoria.

| Ajuste | Valores | Dónde |
|---|---|---|
| **Segunda aprobación** | `NONE` («No pide»), `ALWAYS` («Siempre»), `ABOVE_AMOUNT` («Sobre un monto») | Ajustes → Operación → «Modificaciones de órdenes» (compañía); ficha del cliente → Perfil → «Modificaciones de órdenes» (cliente; «Como la compañía» = hereda) |
| **Monto** | Mayor que 0, hasta 2 decimales (con `ABOVE_AMOUNT`) | igual |
| **Evidencia obligatoria** | sí / no (en el cliente: «Obligatoria», «No obligatoria» o «Como la compañía») | igual |

`ABOVE_AMOUNT` compara **|total nuevo − total anterior|** (con IVU y envío) contra el monto: pide aprobación si es **estrictamente mayor**, en la moneda de la compañía.

| Qué | Ruta | Permiso |
|---|---|---|
| Ajustes de la compañía | `GET` y `PATCH /api/v1/sales-order-changes/settings` | `sales.view` o `admin.tenant` (ver); `admin.tenant` (cambiar) |
| Ajustes de un cliente (`null` / `""` = hereda) | `GET` y `PATCH /api/v1/sales-order-changes/settings/clients/{clientPublicId}` | `sales.view` o `clients.read` (ver); `clients.update` (cambiar) |
| Bandeja «Por aprobar» | `GET /api/v1/sales-order-changes/pending?skip=&take=&q=&sort=` | `sales.modify.approve` |

```json
{ "approvalMode": "ABOVE_AMOUNT", "approvalAmount": 500.00, "attachmentRequired": true }
```

La bandeja va **paginada en el servidor** (`skip`/`take`, hasta 200 por página), con buscador libre (orden, cliente, quién autorizó, motivo) y orden por `registeredAt`, `order`, `client` o `delta` (con `-` para descendente).

**Mientras la aprobación está pendiente:**
- **No se aplica nada:** no se tocan reservas, recolecciones, empaques ni totales. **El almacén no se bloquea** (puede despachar).
- Hay **una sola pendiente por orden**. Otra modificación (o adición) da 409 hasta que se apruebe, rechace o retire.
- La orden muestra un aviso «pendiente de segunda aprobación» y la etiqueta «Modificación pendiente».
- Cancelar la orden **retira** la pendiente («La orden se canceló»).

**Quién decide.**
- **Aprobar:** `sales.modify.approve` y **otra persona que quien registró** (403 si es la misma). Al aprobar se compara la **huella de la orden** (estatus, cantidades, precios, términos y recolecciones): si cambió desde el registro (se despachó, se facturó, se empacó o se modificó), **409**; se rechaza y se registra otra. En una **adición** no se compara la huella; se exige que la madre siga abierta y la adición se crea al aprobar.
- **Rechazar:** `sales.modify.approve`, **la nota es obligatoria**. No se aplica nada.
- **Retirar:** quien la registró o quien tenga `sales.modify`. No se aplica nada.

## 10. Avisos y fuente de datos

Cuatro eventos nuevos (módulo `SALES_ORDERS`, grupo «Ventas» de Reglas de aviso). **No se siembra ninguna regla**: el administrador las arma en Sistema → Reglas de aviso.

| Evento | Cuándo | Destinatarios del evento | ¿Se puede silenciar? |
|---|---|---|---|
| **Orden de venta modificada** (`SALES_ORDER_CHANGED`) | Se **aplicó** una modificación (al registrarla o al aprobarla), **siempre**, no solo con recolección o empaque; lleva el impacto en el almacén | Vendedor de la orden, encargado del almacén, y quien la registró (cuando se aplicó al aprobar) | Sí |
| **Adición a una orden de venta** (`SALES_ORDER_ADDITION_CREATED`) | Se creó la adición | Vendedor de la orden y encargado del almacén | Sí |
| **Empaque reabierto** (`SALES_PACK_REOPENED`) | Se reabrió un empaque (uno por orden de venta atendida) | Encargado del almacén (el vendedor, disponible y apagado) | Sí |
| **Modificación de orden de venta por aprobar** (`SALES_CHANGE_APPROVAL_PENDING`) | Una modificación quedó pendiente de la segunda aprobación | Quienes el administrador ponga en su regla (por ejemplo, el permiso `sales.modify.approve`) | **No** |

- **Vendedor de la orden** = el vendedor congelado al solicitarla (Lote 244), no los apoyos de la ruta. Estos avisos **nunca** van al cliente.
- La adición dispara además el aviso «Orden solicitada» del Lote 243 como cualquier orden. Una modificación de la orden original **no** lo vuelve a mandar: manda «Orden de venta modificada».

**Fuente de datos.** `SALES_ORDER_CHANGE` («Modificaciones de órdenes de venta»), una fila por modificación o adición, con campo de fecha `RegisteredAtUtc`: `OrderNumber`, `ChangeNo`, `Kind`, `Status`, `ClientName`, `RegisteredByName`, `AuthorizedByName`, `AuthorizedByTitle`, `Channel`, `AuthorizedAtUtc`, `Reason`, `TotalBefore`, `TotalAfter`, `TotalDelta`, `LinesAdded`, `LinesRemoved`, `LinesChanged`, `HasPriceChange`, `HasTaxOverride`, `HasTermsChange`, `ReopenedPack`, `ReversedBatchCount`, `CreditOverride`, `RequiresApproval`, `DecidedByName`, `DecidedAtUtc` y `AdditionOrderNumber`. `SALES_ORDER` gana `IsAddition` y `ParentOrderNumber`; `SALES_ORDER_LINE` gana `IsRemoved`, `TaxReason` y `HasTaxOverride`.

## 11. Facturación «cuando el usuario lo decida» (`ON_REQUEST`)

La base de facturación de las **órdenes de venta** de la compañía ahora tiene tres valores: **`ON_DISPATCH`** (al despachar, por omisión), **`ON_DELIVERY`** (al entregar) y **`ON_REQUEST`** (cuando el usuario lo decida, ya solicitada la orden). La facturación de **transporte** sigue su propio ajuste por contrato (por el recogido, por la entrega o mixta).

Con `ON_REQUEST`:
- Lo facturable es **lo pedido, menos lo devuelto y lo ya facturado**, sin esperar el despacho. En una línea con backorder cerrado cuenta lo despachado más lo reservado.
- Entran las órdenes solicitadas, parciales o despachadas que **no estén esperando la aprobación del cliente nuevo**. No entran borradores ni canceladas. El período se mide por la fecha en que se solicitó la orden.
- Lo facturado puede ser **mayor** que lo despachado: la comprobación «despachado ≥ facturado + devuelto» solo vale con las otras dos bases.
- La guarda de deshacer recolecciones (sección 4) no cuenta lo facturado.

Se cambia con `PUT /api/v1/billing/settings` (campo `salesBillingTrigger`), como las otras bases. La pantalla de Facturación ofrece hoy `ON_DISPATCH` y `ON_DELIVERY`.

## 12. Estatus y transiciones

### La modificación (dominio `SalesOrderChangeStatus`)

Toda transición va por el servicio de estatus y queda en el historial de estatus de la modificación.

| De → a | Quién | Qué valida | Efectos |
|---|---|---|---|
| (nada) → **Pendiente de aprobar** (`PENDING_APPROVAL`) | `sales.modify` | Autorización, adjunto, bloqueos de etapa, permisos extra, crédito, una sola pendiente | Se guarda con la foto de antes y la proyectada de después. Si no pide segunda aprobación, pasa a Aplicada en la misma transacción |
| Pendiente → **Aplicada** (`APPLIED`) | Quien registra (sin segunda aprobación) o `sales.modify.approve` (otra persona) | Huella de la orden (409), plan vigente, crédito, invariantes de las líneas | Reabre empaques, deshace las recolecciones necesarias, aplica las operaciones, recalcula impuesto y envío, mueve el estatus de la orden, guarda la foto real, los totales y los efectos, avisa «Orden de venta modificada». En una adición crea y solicita la orden nueva |
| Pendiente → **Rechazada** (`REJECTED`) | `sales.modify.approve` | Nota obligatoria | Ninguno (no se aplicó nada) |
| Pendiente → **Retirada** (`WITHDRAWN`) | Quien registró, o `sales.modify`; o el sistema al cancelar la orden | Debe estar pendiente | Ninguno |

Aplicada, Rechazada y Retirada son terminales. Una modificación ya decidida da 409 «La modificación ya no está pendiente de aprobar.».

**Qué queda bloqueado en cada estatus de la modificación:**
- **Pendiente de aprobar:** no se registra otra modificación ni adición de esa orden (409); el almacén sigue trabajando; no se aplica nada.
- **Aplicada, Rechazada, Retirada:** no se vuelven a decidir.

### La orden de venta

La modificación **no agrega estatus** a la orden; mueve los de siempre (lateral PARTIAL ↔ REQUESTED y SHIPPED → PARTIAL o REQUESTED) como en la sección 3.

| Estatus de la orden | ¿Se modifica? |
|---|---|
| Borrador (`DRAFT`) | No: 422, se edita desde su pantalla |
| Solicitada, parcial, despachada | Sí, con las reglas de la sección 3 |
| Cancelada | No (422) |
| Con algo facturado, o que salió completa | Solo adición (422 si se intenta otra cosa) |

### La recolección

| De → a | Quién | Qué valida | Efectos |
|---|---|---|---|
| **Empacada → Recolectada** (reabrir el empaque) | `warehouse.reopen-pack` | Envío activo y en su etapa inicial; sin flete, cotejo, devolución ni productos facturados; sin compañía enlazada; motivo | Da de baja el envío (libera el número de orden y el EMP), quita envío, fecha de empaque y factura del cliente de la recolección, historial «Empaque reabierto: …», aviso «Empaque reabierto». Solo regresa **un** paso del pipeline |
| **Recolectada → (deshecha)** | `sales.modify` (por la modificación) o `warehouse.pick` (a mano) | Guarda global de facturación (409) | El inventario vuelve a su posición, queda en el Kárdex, la línea vuelve a reservarse, la maestra que quede vacía se da de baja |

## 13. Validaciones y mensajes

El mensaje sale tal cual en el campo `title` del error (y en la pantalla). **Los textos del servidor van en español**; la pantalla traduce los conocidos al idioma de la interfaz. Un error 400 trae además el campo en `errors`.

### Autorización y adjunto (`POST …/changes`, `POST …/additions`)

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `authorizedByName` | Obligatorio | `Indique quién autorizó el cambio en el cliente.` | 400 |
| `authorizedByName` | Hasta 150 | `El nombre de quien autorizó no puede pasar de 150 caracteres.` | 400 |
| `authorizedByTitle` | Hasta 100 | `El cargo de quien autorizó no puede pasar de 100 caracteres.` | 400 |
| `authorizationChannel` | Obligatorio | `Indique cómo autorizó el cliente (por teléfono, por correo, en persona o por mensaje).` | 400 |
| `authorizationChannel` | `PHONE`, `EMAIL`, `IN_PERSON` o `MESSAGE` | `El canal de autorización no es válido: use PHONE, EMAIL, IN_PERSON o MESSAGE.` | 400 |
| `authorizedAtUtc` | No futura | `La fecha de la autorización no puede ser futura.` | 400 |
| `authorizedAtUtc` | No anterior a la orden | `La fecha de la autorización no puede ser anterior a la orden.` | 400 |
| `reason` | Obligatorio | `Indique el motivo de la modificación.` | 400 |
| `reason` | Hasta 500 | `El motivo de la modificación no puede pasar de 500 caracteres.` | 400 |
| `attachmentFilePublicId` | Obligatorio si el cliente o la compañía lo exigen | `Adjunte la evidencia de la autorización del cliente (captura del correo o del mensaje): es obligatoria para este cliente.` | 400 |
| `file` (`change-attachments`) | No vacío | `Adjunte el archivo de la evidencia (imagen o PDF).` | 400 |
| `attachmentFilePublicId` | Un archivo respalda una sola modificación | `Ese archivo ya respalda otra modificación: adjunte otro.` | 409 |
| `attachmentFilePublicId` | Es de la orden (o de su madre o raíz, en una adición) | `Archivo no encontrado.` | 404 |
| `authorizedByContactId` | Es un contacto activo del cliente de la orden | `Contacto no encontrado.` | 404 |
| `reopenReason` | Hasta 500 | `El motivo admite como máximo 500 caracteres.` | 400 |

El tamaño y el tipo del archivo los valida el almacén de archivos (ver la [FAQ](faq.md)).

### Operaciones (`ops`)

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `ops` | Al menos un cambio (y que cambie algo) | `Indique al menos un cambio.` | 400 |
| `ops[].kind` | `ADD`, `QTY`, `REMOVE`, `PRICE`, `TAX`, `CLEAR_TAX`, `TERMS` o `NOTES` | `Operación no válida: use ADD, QTY, REMOVE, PRICE, TAX, CLEAR_TAX, TERMS o NOTES.` | 400 |
| `quantity` | Mayor que cero | `La cantidad debe ser mayor que cero.` | 400 |
| `quantity` | Hasta 3 decimales | `La cantidad admite como máximo 3 decimales.` | 400 |
| `quantity` | Dentro del máximo | `La cantidad excede el máximo permitido.` | 400 |
| `unitPrice` | De 0 en adelante | `El precio no es válido.` | 400 |
| `unitPrice` | Hasta 4 decimales | `El precio admite como máximo 4 decimales.` | 400 |
| `unitPrice` (`PRICE`) | Obligatorio | `Indique el precio nuevo de la línea.` | 400 |
| `taxPct` | Obligatorio | `Indique el porcentaje de IVU de la línea.` | 400 |
| `taxPct` | De 0 a 100 | `El IVU de la línea debe estar entre 0 y 100 %.` | 400 |
| `taxPct` | Hasta 4 decimales | `El IVU de la línea admite hasta 4 decimales.` | 400 |
| `note` (`TAX`) | Obligatorio | `Indique el motivo del IVU manual.` | 400 |
| `note` (`TAX`) | Hasta 250 | `El motivo del IVU manual no puede pasar de 250 caracteres.` | 400 |
| `terms` (`TERMS`) | Al menos un término | `Indique los términos que cambian (forma de pago, entrega o consignatario).` | 400 |
| `notes` | Hasta 500 | `Las notas no pueden pasar de 500 caracteres.` | 400 |
| `rowVersion` | Valor base64 de `edit-options` | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 |
| `rowVersion` | La orden no cambió desde que se leyó | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Producto | Es de la compañía | `Producto no encontrado.` | 404 |
| Orden | Es de la compañía | `Orden de venta no encontrada.` | 404 |
| Modificación | Es de esa orden | `Modificación no encontrada.` | 404 |

### Permisos extra (403)

| Cuándo | Mensaje | HTTP |
|---|---|---|
| Precio distinto del de hoy sin `sales.price` | `Cambiar el precio exige el permiso de precios (sales.price).` | 403 |
| IVU manual sin `sales.tax` | `Falta el permiso 'sales.tax'.` | 403 |
| Reabrir el empaque sin `warehouse.reopen-pack` | `Reabrir el empaque exige el permiso warehouse.reopen-pack.` | 403 |
| Crédito excedido con `overrideCredit` sin el permiso | `Falta el permiso 'orders.credit_override'.` | 403 |
| Cualquier ruta sin su permiso | `Falta el permiso '…'.` | 403 |
| Aprobar una modificación que usted registró | `La segunda aprobación la da otra persona: quien registró la modificación no puede aprobarla.` | 403 |
| Retirar sin ser quien la registró ni tener `sales.modify` | `Solo quien registró la modificación o quien puede modificar órdenes la retira.` | 403 |

### Bloqueos por etapa (422)

| Cuándo | Mensaje | HTTP |
|---|---|---|
| La orden tiene algo facturado | `La orden ya está facturada: solo se le puede añadir. Quitar o bajar se hace con una nota de crédito.` | 422 |
| La orden salió completa | `La orden ya salió completa: solo se le puede añadir con una orden interna de adición.` | 422 |
| Borrador | `La orden está en borrador: se cambia desde su edición, no con una modificación.` | 422 |
| Cancelada | `La orden está cancelada: no se puede modificar.` | 422 |
| Quitar todas las líneas | `No se pueden quitar todas las líneas: para eso cancele la orden.` | 422 |
| Términos con parte ya salida | `Ya salió parte de la orden: el pago, la entrega y el consignatario ya no se cambian.` | 422 |
| Bajar por debajo de lo salido | `No se puede bajar {sku} a {cantidad}: ya salieron {salido}.` | 422 |
| Quitar una línea con algo salido | `No se puede quitar {sku}: ya salieron {salido}.` | 422 |
| Precio de una línea con algo salido | `El precio de {sku} ya no se cambia: parte de la línea ya salió.` | 422 |
| IVU de una línea con algo salido | `El IVU de {sku} ya no se cambia: parte de la línea ya salió.` | 422 |
| Línea en un empaque sin marcar reabrir | `{sku} está en el empaque {EMP}: para cambiarla marque «Reabrir el empaque» (permiso warehouse.reopen-pack).` | 422 |
| Términos con un empaque sin marcar reabrir | `La orden está en el empaque {EMP}: para cambiar el pago, la entrega o el consignatario marque «Reabrir el empaque» (permiso warehouse.reopen-pack).` | 422 |
| Línea quitada | `La línea {n} ({sku}) está quitada: para volver a pedirla, agréguela de nuevo.` | 422 |
| Línea cerrada con faltante | `La línea {n} ({sku}) está cerrada: su cantidad ya no se cambia.` | 422 |
| Línea repetida en la modificación | `La línea {n} ({sku}) viene más de una vez en la modificación.` | 422 |
| Línea de otra orden | `La línea {id} no es de esta orden.` | 422 |
| Falta la línea de la operación | `Indique la línea que se cambia.` | 422 |
| Producto ya en la orden | `El producto {sku} ya está en la orden: cambie la cantidad de su línea.` | 422 |
| Falta el producto de `ADD` | `Indique el producto que se agrega.` | 422 |
| Kit con cantidad no entera | `El kit {sku} se pide en piezas enteras.` | 422 |
| `CLEAR_TAX` en una línea sin IVU manual | `La línea {n} ({sku}) no tiene IVU manual.` | 422 |
| `TERMS` o `NOTES` repetidos | `Los términos vienen dos veces en la modificación.` / `Las notas vienen dos veces en la modificación.` | 422 |
| IVU manual con `TAX` apagado | `El IVU manual exige el módulo de impuestos encendido.` | 422 |
| El cambio deja una línea incoherente | `La modificación dejaría la orden incoherente (línea {n}): no se aplicó.` | 422 |
| Crédito excedido | `El cliente excede su límite de crédito: límite …, en curso …, esta orden ….` (sin cifras para quien no tiene `orders.view_price`) | 422 |
| Adición a una orden que no está en curso | `Solo se le puede añadir a una orden solicitada, parcial o despachada.` | 422 |
| Adición sin productos | `Indique los productos de la adición.` | 400 |
| Cancelar una orden con adiciones abiertas | `La orden tiene adiciones abiertas ({OV-…}): cancélelas o despáchelas antes de cancelar esta.` | 422 |

### Conflictos (409)

| Cuándo | Mensaje | HTTP |
|---|---|---|
| Otra modificación mientras hay una pendiente | `La orden ya tiene una modificación por aprobar (n.º {n}): apruébela, recházela o retírela antes de registrar otra.` | 409 |
| Entre la vista previa y guardar cambiaron las recolecciones | `La orden cambió mientras se modificaba; vuelva a intentar.` | 409 |
| La orden cambió al mismo tiempo | `La orden cambió al mismo tiempo. Recárguela e intente de nuevo.` | 409 |
| Al aprobar, la orden ya no es la que se modificó | `La orden cambió desde que se registró la modificación (se despachó, se facturó, se empacó o se modificó): recházela y registre otra.` | 409 |
| La modificación ya se decidió | `La modificación ya no está pendiente de aprobar.` | 409 |
| Deshacer una recolección ya facturada | `La recolección {PK} ya está facturada ({FAC-…}): no se puede deshacer.` | 409 |
| Falta inventario al subir o agregar | El mensaje del primer faltante (`insufficient_stock`) | 409 |

### Decisión y ajustes

| Cuándo | Mensaje | HTTP |
|---|---|---|
| Rechazar sin nota | `Indique por qué se rechaza la modificación.` | 400 |
| Nota de la decisión larga | `La nota de la decisión no puede pasar de 500 caracteres.` | 400 |
| Modo de segunda aprobación | `El modo de segunda aprobación debe ser NONE, ALWAYS o ABOVE_AMOUNT.` | 400 |
| `ABOVE_AMOUNT` sin monto | `Con «Arriba de un monto» (ABOVE_AMOUNT) indique un monto mayor que 0.` | 400 |
| Monto con más de 2 decimales | `El monto de la segunda aprobación admite hasta 2 decimales.` | 400 |
| Base de facturación inválida (`PUT /billing/settings`) | `La base de facturación debe ser ON_DISPATCH (al despachar), ON_DELIVERY (al entregar) u ON_REQUEST (cuando el usuario lo decida, ya solicitada la orden).` | 400 |

### Reabrir el empaque y unir al envío (`/api/v1/pick-batches`)

| Cuándo | Mensaje | HTTP |
|---|---|---|
| Reabrir sin motivo | `Indique el motivo para reabrir el empaque.` | 400 |
| Motivo largo | `El motivo admite como máximo 500 caracteres.` | 400 |
| No está empacada (o es despacho manual) | `La recolección {PK} no está empacada.` | 422 |
| Es de una maestra | `La recolección {PK} está en la recolección maestra {PK-M}: reabra el empaque desde la maestra.` | 422 |
| Se mandó a la compañía enlazada | `La recolección {PK} se mandó a la compañía enlazada: el empaque no se reabre.` | 422 |
| Envío ya avanzado, anulado o sin envío | `El envío {ORD} ya está «{estatus}»: el empaque {PK} no se puede reabrir.` | 422 |
| Flete, cotejo o devolución facturados | `El flete del envío {ORD} ya está facturado: el empaque {PK} no se reabre.` | 422 |
| Factura del cliente ya procesada | `La factura del cliente del envío {ORD} ya se procesó: el empaque {PK} no se reabre.` | 422 |
| Productos ya facturados | `El empaque {PK} tiene productos ya facturados ({FAC-…}): no se reabre. Para añadir, use una orden interna de adición.` | 422 |
| Unir: no es de la familia de la orden | `La recolección {PK} no es de la misma orden ni de una adición de ella: no se une a ese envío.` | 422 |
| Unir: no está recolectada | `La recolección {PK} no está recolectada (está «{estatus}»).` | 422 |
| Unir: ya está en una maestra | `La recolección {PK} ya está en la maestra {PK-M}.` | 422 |
| Unir: varios destinos | `Escoja con qué recolección de {OV} sale (hay varias).` | 400 |
| Unir: el destino ya salió | `El envío de {PK} ya salió («{estatus}»): la recolección sale en su propio envío.` | 422 |
| Unir: el destino está empacado y no se marcó reabrir | `{PK} ya está empacada: marque «Reabrir el empaque» (permiso warehouse.reopen-pack) para unir esta recolección a su envío.` | 422 |
| Unir: no hay otra recolección | `No hay otra recolección de {OV} con la que pueda salir: empaque esta por separado.` | 422 |
| Unir: consigo misma | `Escoja otra recolección: {PK} no se une consigo misma.` | 400 |
| Unir: se mandó a la compañía enlazada | `La recolección {PK} se mandó a la compañía enlazada: no se une a otro envío.` | 422 |
| Unir: otro cliente | `Los documentos deben ser del mismo cliente: «…» y «…» son de clientes distintos. Haga recolecciones separadas.` | 400 |
| Unir: otro almacén | `Los documentos deben ser del mismo almacén: «…» y «…» son de almacenes distintos.` | 400 |

## 14. Preguntas frecuentes del módulo

**¿Por qué no puedo modificar la orden?**
Mire el mensaje. Si dice que está **facturada** o que **ya salió completa**, solo se le puede añadir: use «Orden interna de adición». Si es un borrador, edítela desde su pantalla. Si es la falta de permiso, pida `sales.modify` (el rol Ventas ya no lo trae).

**Quiero bajar la cantidad y dice «está en el empaque …».**
La mercancía ya está empacada. Marque **«Reabrir el empaque»** (necesita `warehouse.reopen-pack`, que por omisión solo tiene el administrador). Se da de baja el envío y la recolección vuelve a «Recolectada»; después hay que volver a empacar. Si no tiene el permiso, lo que quiera añadir puede ir en una orden interna de adición.

**Bajé la cantidad y el almacén dice que se deshizo una recolección.**
Es la reversa mínima: se deshizo solo la recolección necesaria, la más reciente. El inventario volvió a su posición y la línea quedó reservada; el almacén debe volver a despachar. El aviso «Orden de venta modificada» lo dice.

**Dice «La recolección … ya está facturada (…): no se puede deshacer.»**
Con la base de facturación «al despachar» o «al entregar», esa mercancía ya está en una factura emitida y la factura no se reabre. Resuélvalo con una **devolución** o una nota de crédito. Con la base «cuando el usuario lo decida» no pasa.

**La modificación quedó «pendiente de aprobación» y la orden no cambió.**
La compañía o el cliente piden segunda aprobación. Otra persona con `sales.modify.approve` la aprueba desde **Por aprobar**. Mientras tanto no se aplicó nada y el almacén puede seguir trabajando. Si la orden cambió (por ejemplo, se despachó) antes de aprobar, el sistema da 409: rechácela y registre otra.

**No me deja aprobar mi propia modificación.**
Es a propósito: la segunda aprobación la da otra persona.

**Puse un IVU manual y las demás líneas siguen exentas. ¿Es un error?**
No. El IVU manual solo afecta esa línea; la exención del cliente sigue valiendo para las demás. Con «Quitar IVU manual» la línea vuelve a su cálculo normal, con la exención si el documento sigue vigente.

**¿Qué diferencia hay entre modificar y la orden interna de adición?**
Modificar cambia la orden que ya existe (se aplica al instante o tras la segunda aprobación). La adición crea una orden nueva, ligada a la original, para lo que ya no se puede cambiar. Se factura con sus propias corridas y su factura dice «complementa FAC-…» cuando la original ya se facturó.

**La adición salió en un envío aparte.**
Use «Unir al envío de OV-…» en su recolección (solo si la original sigue en la etapa inicial). Si la original estaba empacada, hay que reabrir su empaque (`warehouse.reopen-pack`); un empaque con productos ya facturados no se reabre y la adición sale en su propio envío.

**No puedo cancelar la orden.**
Tiene adiciones abiertas: cancélelas o despáchelas primero. Una modificación pendiente de aprobar se retira sola al cancelar.

**El cliente exige evidencia y no la tengo.**
Si el ajuste está en «Obligatoria» (cliente o compañía), sin archivo la modificación no se registra (400). Cambie el ajuste del cliente a «No obligatoria» o «Como la compañía» si corresponde (`clients.update`).

**No veo «Por aprobar» ni «Modificar orden».**
La pestaña «Por aprobar» pide `sales.modify.approve`; el botón «Modificar orden» pide `sales.modify`.

**Pedí el precio de un producto con precio de lista y no me deja.**
Escribir un precio distinto del de hoy exige `sales.price`. En el pedido guiado y en la modificación, quien lo tiene puede cambiar el precio de cualquier línea.

## 15. Para soporte

- **Tablas:** `SalesOrderChange` (la modificación: `ChangeNo` único por orden, `Kind` `MODIFY` o `ADDITION`, `IsPending`, `BeforeJson`, `AfterJson`, `RequestJson`, `EffectsJson`, `BaseFingerprint`, `AdditionSalesOrderId`); `SalesOrder.ParentSalesOrderId` (la madre de una adición; `FK_SalesOrder_Parent` es compuesta con la compañía); `SalesOrderLine` (`TaxReason`, `TaxOverrideNote`, `TaxComputedRatePct`, `TaxComputedAmount`, `TaxOverrideBy`, `TaxOverrideAtUtc`); `Tenant` y `Client` (`SalesChangeApprovalMode`, `SalesChangeApprovalAmount`, `SalesChangeAttachmentRequired`; en `Client`, `NULL` = hereda).
- **Índices que protegen:** `UX_SalesOrderChange_Pending` (una sola pendiente por orden, garantizada en la base) y `UX_SalesOrderChange_Attachment` (un archivo respalda una sola modificación). Una carrera entre dos personas termina en el 409 de arriba.
- **Línea quitada:** `QtyOrdered = 0` e `IsClosed = 1` (nunca se borra). Volver a agregar el producto la reactiva con el mismo número; los números de línea pueden tener huecos.
- **«¿Quién autorizó este cambio?»:** `GET …/changes/{id}` (o el panel «Modificaciones» de la orden): nombre, cargo, contacto, canal, fecha, motivo, evidencia, antes y después. La bitácora de auditoría registra `SALES_ORDER_CHANGE`; el historial de estatus, las transiciones de la modificación.
- **Crédito autorizado:** la modificación lleva la marca `CreditOverride` y queda un evento de seguridad `credit_override` con la orden y la modificación.
- **Reabrir:** el historial de estatus de la recolección dice «Empaque reabierto: {motivo} (envío {ORD} anulado)»; el envío queda con `IsActive = 0`.
- **Avisos:** en `NotificationOutbox`, las claves son `CHG-{modificación}`, `APPROVAL-{modificación}`, `ADD-{modificación}` y `REOPEN-{recolección o maestra}-{envío}`.
- **Facturación de una adición:** la descripción de la línea de factura y el MEMO del IIF llevan «adición de OV-… · complementa FAC-…».
- **Base de facturación:** `Tenant.SalesBillingTrigger` (`ON_DISPATCH`, `ON_DELIVERY`, `ON_REQUEST`).
- Sin permiso, o con el módulo apagado, los 403 dejan evento de seguridad `PERMISSION_DENIED`.
