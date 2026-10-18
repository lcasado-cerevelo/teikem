# Lote 105 — Entrada de órdenes del cliente en el portal (portal de clientes, 9) (2026-10-17)

El 105 original juntaba tres cosas; Luis eligió partirlo: **105 = entrada de órdenes del cliente**; **106 = acceso del consignatario** (invitación, primer ingreso, avisos con su aceptación y las órdenes que él crea, con confirmación del cliente). Decisiones suyas (preguntadas): la orden **entra directo al flujo normal** y el cliente escribe **solo lo básico**.

## Qué se construyó
- **Servidor** (`PortalOrderEntryService`, `GET /api/v1/portal/order-form`, `POST /api/v1/portal/orders`): reutiliza `OrderService.CreateAsync` con el alcance del cliente de la sesión (mismas validaciones, numeración, conduce, crédito, duplicados); pueden crear `CLIENT_ADMIN` y `CLIENT_OPERATOR` (`CLIENT_READONLY` → 403); el formulario ofrece solo los consignatarios (DELIVERY) y recogidas (PICKUP/BOTH) activos del cliente; el campo de factura solo si el cliente la teclea; validaciones propias (consignatario, 1–50 paquetes, notas ≤ 500). Sin permiso nuevo ni cambio de esquema.
- **app-cliente:** botón «Nueva orden» (solo si el rol puede) y la pantalla `NewOrderScreen` con `newOrderRules` (validación y armado); español/inglés y tema claro/oscuro.

## Cómo se probó
- xunit `PortalOrderEntryTests` (7): roles que pueden crear, solo lectura (formulario vacío y 403), formulario solo con lo del cliente, otro cliente no puede, las seis validaciones. Suite completa.
- Contra SQL real: operador crea con consignatario nuevo y existente (CND-… en DRAFT), lectura 403, validaciones, el cliente del cuerpo se ignora, la orden aparece en «Mis órdenes».
- jest (+8) y Playwright (escritorio y móvil, dentro del recorrido de la app del cliente: botón, validación vacía, consignatario nuevo, creación y ficha).

## Decisiones a revisar con Luis
1. **Nacen en «Borrador»** (el estatus inicial de la compañía); no se confirman solas (no se manda `ConfirmNow`). Si quieres que las del cliente nazcan ya confirmadas, es un cambio de una línea.
2. **Solo roles Administrador/Operador del cliente** (ya existían); no hay permiso nuevo.
3. **Sin tope de órdenes por día** todavía (el diseño lo pide para el consignatario, lote 106; para el cliente se puede agregar).
4. Los **consignatarios compartidos** de la compañía (almacenes sin cliente) no se ofrecen en el formulario, aunque el servidor los acepta si se conoce el id.
5. El portal **no edita ni cancela** órdenes; solo crea y mira.
