# 30 — Nueva orden desde el portal del cliente

Lote 105 (portal de clientes, 2026-10-17). Las personas del cliente crean órdenes desde la app del cliente (pestaña **Órdenes → Nueva orden**). Decidido con Luis: la orden **entra directo al flujo normal** (nace igual que una creada en la web de la compañía), y el cliente solo escribe **lo básico**.

## 1. Quién puede
| Rol de portal | Crear órdenes |
|---|---|
| `CLIENT_ADMIN` (administrador) | Sí |
| `CLIENT_OPERATOR` (operador) | Sí |
| `CLIENT_READONLY` (solo consulta) | No: no ve el botón «Nueva orden»; por API, **403** «Su usuario solo puede consultar; pídale a un administrador de su empresa que le dé permiso para crear envíos.» |

Módulo **CLIENT_PORTAL** encendido. El cliente **sale de la sesión** (nunca del cuerpo): lo que se mande como cliente se ignora, y la orden siempre queda en el cliente de quien entró.

## 2. Qué se escribe
- **Consignatario:** uno de los del cliente (se ven solo los del cliente de la sesión, activos) **o** uno nuevo (nombre, dirección y ciudad; línea 2, estado y código postal opcionales). Si el nuevo coincide con uno que ya existe, se usa el existente. No las dos cosas.
- **Recogida:** opcional; por omisión, la recogida habitual del cliente.
- **Paquetes:** de 1 a 50; cada uno con descripción, piezas (entero ≥ 1) y peso en kg (opcional).
- **COD** (cobro a la entrega), opcional; **fecha pedida** (AAAA-MM-DD, de hoy en adelante); **notas** (hasta 500 caracteres).
- **Número de factura del cliente:** el campo **solo aparece si el cliente numera sus propias facturas**; si Teikem las numera, no se muestra (y mandarlo da 400 «El número de factura lo asigna Teikem para este cliente; déjelo en blanco.»).
- **No se puede** escoger precios, prioridad, tipo de servicio ni entrega especial; eso lo completa la compañía.

Al crear, se abre la ficha de la orden con su conduce y su barra de progreso. Valen las mismas reglas de la web de la compañía: numeración, conduce, crédito, cliente suspendido (422) o dado de baja (409).

## 3. Mensajes de error
| Situación | Código | Mensaje |
|---|---|---|
| Rol de solo consulta | 403 | «Su usuario solo puede consultar; pídale a un administrador de su empresa que le dé permiso para crear envíos.» |
| Sin consignatario | 400 | «Escoja el consignatario o escriba uno nuevo (nombre, dirección y ciudad).» |
| Consignatario y nuevo a la vez | 400 | «Escoja un consignatario o escriba uno nuevo, no las dos cosas.» |
| Sin paquetes | 400 | «Agregue al menos un paquete.» |
| Más de 50 paquetes | 400 | «Un envío puede llevar hasta 50 paquetes.» |
| Notas largas | 400 | «Las notas no pueden pasar de 500 caracteres.» |

## 4. Endpoints
`GET /api/v1/portal/order-form` (si puedo crear, mis consignatarios y recogidas, y si el cliente teclea su factura) y `POST /api/v1/portal/orders` (crear; responde conduce, número de envío y estatus). Ver las órdenes: `GET /api/v1/portal/orders` (capítulo 26).

## 5. Casos frecuentes
- **No veo el botón «Nueva orden»:** su rol es de solo consulta; que la compañía cambie su rol (ficha del cliente → Usuarios de portal, en la web de la compañía).
- **Mi orden aparece «Sometida»:** así nacen todas las órdenes (Lote 106): ya está cotizada si su cuenta tiene contrato y tarifa, y mientras siga Sometida se puede corregir; después la recogen y avanza sola.
