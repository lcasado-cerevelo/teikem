# 22 — Liquidación a choferes

Lote 93 (P9, 1 de 4, 2026-10-17). A cada chofer se le paga por lo que hizo en la calle: por entrega, por intento y por viaje. Este capítulo crece con cada lote de P9; hoy trae la **base**: el registro de intentos de entrega. Las tarifas y la política de pago ya existen (Choferes y tarifas, capítulo 04).

## 1. Intentos de entrega

Cada vez que un chofer intenta entregar una orden hay un **intento**: su número (1, 2, 3…), el chofer, la fecha y hora, si **entregó** o **falló** (con la razón del fallo) y una nota. Es la **fuente de verdad** de la liquidación: nada se le paga a un chofer fuera de este registro.

### De dónde salen

| Origen (`source`) | Cuándo |
|---|---|
| `STATUS` | Solo: cuando una orden pasa a **Entregada** (intento entregado) o **Fallida** (intento fallido). Lleva el chofer de la **ruta vigente** de la orden y la ruta; el comentario del cambio de estatus queda de nota. |
| `MANUAL` | La oficina lo registra a mano (hasta que exista la app del chofer, P7). |
| `APP` | Reservado para la app del chofer. |

Reglas del registro automático: una **entrega especial** (la que tiene un viaje pagado, `DriverTrip`) no genera intentos, porque se paga por viaje; si la orden ya tiene un intento entregado vigente no se anota otro; si la ruta no tiene chofer, el intento queda **sin chofer** y la oficina lo completa (anulándolo y registrándolo con chofer) antes de liquidar.

### Quién puede

Lectura con `driverpay.view`; registrar y anular con `driverpay.manage`. Módulo `LTL_GROUND` encendido.

### Endpoints

| Endpoint | Para qué |
|---|---|
| `GET /api/v1/delivery-attempts` | Lista paginada. Filtros: `driverPublicId`, `orderPublicId`, `from`/`to` (días de la compañía), `delivered`, `settled`, `withoutDriver`, `includeVoided`, `skip`, `take` (máx. 200). Más reciente primero. |
| `POST /api/v1/delivery-attempts` | Registra un intento a mano: `orderPublicId`, `driverPublicId`, `delivered`, `attemptNumber` (vacío = el siguiente), `attemptedAtUtc` (vacío = ahora), `failureReason` (código del catálogo, solo si no entregó), `notes`. |
| `POST /api/v1/delivery-attempts/{publicId}/void` | Anula un intento (`reason`). El número anulado se puede volver a usar. |

### Validaciones

| Situación | Mensaje | Código |
|---|---|---|
| Sin orden | `Indique la orden del intento.` | 400 |
| Sin chofer | `Indique el chofer del intento.` | 400 |
| Entregado con razón de fallo | `Un intento entregado no lleva razón de fallo.` | 400 |
| Razón que no existe | `La razón de fallo '{código}' no existe.` | 400 |
| Nota muy larga | `La nota no puede pasar de 400 caracteres.` | 400 |
| Fecha futura | `La fecha del intento no puede ser futura.` | 400 |
| Número repetido | `La orden ya tiene un intento número {n}.` | 400 |
| Dos entregados | `Solo un intento puede ser la entrega.` | 400 |
| Entregado que no es el último | `La entrega debe ser el último intento.` | 400 |
| Entrega especial | `Es una entrega especial: se paga por viaje, no por intentos.` | 422 |
| Orden o chofer inexistente | 404 | 404 |
| Anular sin motivo | `Indique el motivo de la anulación.` | 400 |
| Anular uno ya anulado | `El intento ya está anulado.` | 409 |
| Anular uno ya liquidado | `El intento ya está en una liquidación y no se puede anular.` | 409 |

La regla de la secuencia (a lo sumo un entregado y debe ser el último) es la misma del motor de pago, así que todo lo que se registra se puede liquidar.

### Casos frecuentes

- **Una orden entregada no tiene intentos:** es una entrega especial (se paga por viaje) o se entregó antes de este lote. Regístrelos a mano.
- **Un intento salió «sin chofer»:** la ruta de la orden no tenía chofer. Anúlelo y regístrelo con el chofer correcto.
- **Me equivoqué de chofer:** anule el intento (con motivo) y regístrelo de nuevo; si ya está en una liquidación no se puede anular.
