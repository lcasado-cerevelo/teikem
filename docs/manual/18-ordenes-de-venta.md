# 18 — Órdenes de venta

Lote 57 (2026-10-15) — **parte S1, borrador**. Una **orden de venta** (OV-#####) es el documento que viaja de **Ventas a Almacén**: Ventas le pide al almacén que despache algo para un cliente. Esta primera parte permite **armar y mantener la orden en borrador**; solicitarla (validar y reservar inventario), los backorders y el despacho contra la orden llegan en los lotes siguientes de la fase.
El diseño completo está en `Diseño/logistica-funcionalidades-maestro.md`, sección 13C.

## 1. Quién puede

- Módulo **Órdenes de venta (`SALES_ORDERS`)**: viene **apagado**; se enciende por compañía (Sistema → Módulos). Con el módulo apagado todo el API responde 403.
- Permisos (categoría Órdenes):
  - **`sales.view`** — ver órdenes. Lo tienen el Admin de compañía, el rol **Ventas** y el Operador de almacén.
  - **`sales.create`** — crear y editar el borrador (y, en el lote siguiente, solicitar). Admin de compañía y rol Ventas.
  - **`sales.cancel`** — cancelar. Admin de compañía y rol Ventas.
  - **`sales.price`** — escribir un precio distinto del precio de venta del producto. **Solo el Admin de compañía**; el administrador se lo asigna a quien quiera.
  - **`sales.backorder`** — surtir y cerrar backorders (lotes siguientes). Admin de compañía y Operador de almacén.
- La plantilla de rol **Ventas** (`sales.view`, `sales.create`, `sales.cancel`, `inventory.view`, `clients.read`) la crea el sistema para las compañías nuevas; en las que ya existen el administrador crea el rol y le asigna esos permisos.

## 2. La orden

- **Número** `OV-#####`, único por compañía, consecutivo y sin saltos (un intento que falla no gasta número).
- **Cliente** (obligatorio): el comprador. **Consignatario** (opcional): solo si habrá entrega. **Almacén**: el que la atiende; si la compañía tiene un solo almacén activo se usa solo, si tiene varios hay que indicarlo.
- **Fecha de solicitud** (hoy, en la zona de la compañía), **fecha requerida** (opcional), **notas** (hasta 500 caracteres) y **quién la pidió**.
- **Líneas**: producto, cantidad y precio. Solo se venden **productos propios de la compañía** (no los de un cliente de paso), activos, **sin repetir** en la misma orden. Hasta 200 líneas.
- **Precio**: si no se escribe, se toma el **precio de venta del producto**. Escribir otro precio exige `sales.price` (si es igual al de lista no hace falta). Un producto **sin precio de venta** obliga a escribir el precio.
- **Impuesto**: con el módulo **Impuestos** encendido, cada línea calcula su impuesto con las reglas de la compañía (capítulo [17](17-impuestos.md)) y **guarda una copia**: la tasa usada, su porcentaje, el monto y si salió exenta (producto exento o cliente con exención vigente). El encabezado guarda el **subtotal** y el **impuesto**; el **total** es su suma. Sin el módulo Impuestos el impuesto es cero. Si se cambia el cliente del borrador, el impuesto se recalcula.
- **Estatus**: `DRAFT` (borrador) → `REQUESTED` → `PARTIAL` → `SHIPPED`; `CANCELLED`. En este lote solo existen en uso el borrador y el cancelado. La marca **con backorder** no es un estatus: la tiene la orden mientras alguna línea tenga cantidad pendiente.

## 3. Qué se puede hacer

| Acción | Cómo | Permiso |
|---|---|---|
| Ver la lista (más reciente primero; filtros: estatus, cliente, buscador por número / cliente / producto, solo con backorder, fechas) | `GET /api/v1/sales-orders` | `sales.view` |
| Ver una orden | `GET /api/v1/sales-orders/{publicId}` | `sales.view` |
| Crear el borrador | `POST /api/v1/sales-orders` | `sales.create` (+ `sales.price` si cambia un precio) |
| Editar el borrador (lo que no se manda no cambia; las **líneas, si se mandan, reemplazan** a las actuales; `clearConsignee` / `clearRequiredDate` quitan esos datos) | `PATCH /api/v1/sales-orders/{publicId}` | `sales.create` |
| Cancelar un borrador (con un comentario opcional que queda en el historial) | `POST /api/v1/sales-orders/{publicId}/cancel` | `sales.cancel` |

Las órdenes **nunca se borran**: se cancelan. Quedan en la bitácora (altas, cambios) y en el historial de estatus.

## 4. Validaciones y mensajes

| Situación | Mensaje exacto | HTTP |
|---|---|---|
| Sin cliente | `Escoja el cliente que compra.` | 400 (`clientPublicId`) |
| Cliente inexistente (o de otra compañía) | `Cliente no encontrado.` | 404 |
| Cliente inactivo | `El cliente está inactivo; no se le puede vender.` | 422 |
| Consignatario inexistente | `Consignatario no encontrado.` | 404 |
| Varios almacenes y no se indicó | `Indique el almacén: la compañía tiene más de uno.` | 400 (`warehousePublicId`) |
| Almacén inexistente / inactivo / la compañía no tiene ninguno | `Almacén no encontrado.` / `El almacén está inactivo.` / `La compañía no tiene almacenes activos.` | 404 / 422 / 422 |
| Sin líneas | `Agregue al menos un producto a la orden.` | 400 (`lines`) |
| Más de 200 líneas | `La orden admite como máximo 200 líneas.` | 400 |
| Línea sin producto | `Escoja el producto de cada línea.` | 400 |
| Producto inexistente | `Producto no encontrado.` | 404 |
| Producto inactivo | `Hay un producto inactivo en la orden.` | 422 |
| Producto de un cliente de paso | `Solo se pueden vender productos propios de la compañía.` | 422 |
| Producto repetido | `Un producto no puede repetirse en la orden.` | 400 |
| Cantidad vacía o cero | `La cantidad debe ser mayor que cero.` | 400 |
| Más de 3 decimales / demasiado grande | `La cantidad admite como máximo 3 decimales.` / `La cantidad excede el máximo permitido.` | 400 |
| Precio negativo o inválido / más de 4 decimales | `El precio no es válido.` / `El precio admite como máximo 4 decimales.` | 400 |
| Producto sin precio de venta | `El producto {SKU} no tiene precio de venta: escriba el precio.` | 400 |
| Precio distinto del de lista sin `sales.price` | `Cambiar el precio exige el permiso de precios (sales.price).` | 403 |
| Lote inexistente | `Lote no encontrado.` | 404 |
| Notas de más de 500 caracteres | `Las notas no pueden pasar de 500 caracteres.` | 400 |
| Orden inexistente (o de otra compañía) | `Orden de venta no encontrada.` | 404 |
| Editar una orden que ya no es borrador | `Solo se puede editar una orden en borrador.` | 422 |
| Cancelar una orden ya cancelada | `La orden ya está cancelada.` | 409 |
| Cancelar una orden que ya no es borrador (en este lote) | `Solo se puede cancelar una orden en borrador o solicitada que no se haya despachado.` | 422 |
| Con Impuestos encendido y sin tasa por omisión / sin tasa vigente | `Escoja la tasa por omisión en Impuestos → Reglas.` / `No hay una tasa de impuesto vigente en esa fecha. Configure una en Impuestos → Tasas.` | 400 |
| Sin el permiso o con el módulo apagado | «Sin permiso» | 403 |

## 5. Para soporte

- Tablas `SalesOrder`, `SalesOrderLine` y `SalesOrderReservation` (esta última se usa al solicitar), contador `SALESORDER`, dominio de estatus `SalesOrderStatus` y EntityType `SALES_ORDER` (sección **2026-10-15 (k)** de `Diseño/logistica-db-update.sql`).
- Cada línea guarda `TaxRateId`, `TaxRatePct`, `TaxAmount` e `IsTaxExempt`: un cambio posterior de la tasa **no** altera una orden ya guardada.
- Un número `OV-` saltado no ocurre: el contador se toma dentro de la misma transacción que crea la orden.
