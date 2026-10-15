# Lote 57 — Órdenes de venta: borrador (P4, parte S1 / servidor) (2026-10-15)

Primera parte de P4. Diseño: `Diseño/logistica-funcionalidades-maestro.md`, sección 13C (aprobado por Luis el 2026-10-10). Este lote deja lista la base; solicitar/reservar, backorders y despacho vienen después.

## Qué se construyó
- **Tablas** (update.sql sección **2026-10-15 (k)**): `SalesOrder`, `SalesOrderLine` (con la copia del impuesto) y `SalesOrderReservation` (qué saldo reservó cada línea; la usa «Solicitar»). Contador `SALESORDER` (OV-#####), dominio de estatus `SalesOrderStatus` (DRAFT → REQUESTED → PARTIAL → SHIPPED; CANCELLED), EntityType `SALES_ORDER`.
- **Permisos** `sales.view`, `sales.create`, `sales.cancel`, `sales.price`, `sales.backorder` (86 permisos). Admin de compañía: todos (plantilla y roles ya clonados); Operador de almacén: `sales.view` y `sales.backorder`. Nueva **plantilla de rol «Ventas»** (`sales.view/create/cancel`, `inventory.view`, `clients.read`).
- **Servidor**: `SalesRules` (reglas puras y mensajes), `SalesOrderService` (listar con filtros, ver, crear, editar el borrador reemplazando líneas, cancelar) y `SalesOrdersController` (`/api/v1/sales-orders`, módulo SALES_ORDERS; sin PUT ni DELETE).
- **Precio**: el de venta del producto; otro precio exige `sales.price`; un producto sin precio obliga a escribirlo.
- **Impuesto**: con el módulo TAX encendido cada línea calcula con `TaxService` y guarda la copia (tasa, porcentaje, monto, exento); sin el módulo es cero. Cambiar el cliente del borrador recalcula.
- Manual: capítulo 18 y FAQ «Lote 57».

## Pruebas
- xunit: reglas puras (cantidad, precio, notas, backorder), sección de SQL, permisos y plantillas, API sin DELETE/PUT; con el arnés de servicios (InMemory): alta con número y totales, precio y permiso, **todas las validaciones con su mensaje exacto y sin dejar nada guardado**, edición del borrador (reemplazo de líneas, `clearRequiredDate`, no editable fuera de borrador), cancelación por el servicio de estatus con su comentario en el historial, impuesto copiado por línea con cliente exento y recálculo al cambiar de cliente, aislamiento entre compañías y almacén único por omisión.
- `db-update --dry-run` contra Teikem_verif: OK (se halló que `LineNo` es palabra reservada de T-SQL: va entre corchetes).

## Decisiones por omisión (a revisar con Luis)
1. **Solo productos propios** (`Product.ClientId` vacío) y activos: igual que las órdenes de compra; los productos de un cliente de paso no se venden desde aquí.
2. **Las compañías existentes no reciben automáticamente el rol «Ventas»**: la plantilla es del sistema y se clona al crear compañías nuevas; en las existentes el administrador crea el rol y le asigna los permisos.
3. **`sales.price` solo para el Admin de compañía** (el rol Ventas no lo trae): si Ventas debe poder bajar precios, el administrador se lo asigna.
4. **Editar = reemplazar las líneas** (como las órdenes de compra), solo en borrador; las órdenes solicitadas se modificarán línea por línea con su reserva en el lote de «Solicitar».
5. El impuesto con el módulo TAX encendido pero sin tasa configurada **bloquea** guardar la orden con el mensaje de configuración (en vez de guardar con cero en silencio).
6. El número `OV-#####` es consecutivo por compañía y se toma en la misma transacción que la orden.
