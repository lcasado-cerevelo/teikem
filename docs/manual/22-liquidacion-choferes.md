# 22 — Liquidación a choferes

Lotes 93 a 95 (P9, 2026-10-17). A cada chofer se le paga por lo que hizo en la calle: por entrega, por intento y por viaje. Este capítulo crece con cada lote de P9: la base (los intentos de entrega, §1) y la **liquidación** (§2). Las tarifas y la política de pago ya existen (Choferes y tarifas, capítulo 04).

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
| Sin orden | `Indique el envío del intento.` | 400 |
| Sin chofer | `Indique el chofer del intento.` | 400 |
| Entregado con razón de fallo | `Un intento entregado no lleva razón de fallo.` | 400 |
| Razón que no existe | `La razón de fallo '{código}' no existe.` | 400 |
| Nota muy larga | `La nota no puede pasar de 400 caracteres.` | 400 |
| Fecha futura | `La fecha del intento no puede ser futura.` | 400 |
| Número repetido | `El envío ya tiene un intento número {n}.` | 400 |
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

## 2. La liquidación (Lote 94)

Una **liquidación** (`LIQ-#####`) junta, para un chofer o para todos, lo que hay por pagar en un período: las **entregas** (con sus intentos) y los **viajes**. Calcula el pago con las tarifas y la fórmula vigentes y lo **congela**. Sigue el mismo flujo de aprobación humana que una corrida de facturación: **Generada → Revisada → Aprobada → Exportada** (o Cancelada). Teikem calcula y exporta; **no emite el pago**.

### Quién puede

| Acción | Permiso |
|---|---|
| Ver liquidaciones y la vista previa | `driverpay.view` |
| Generar, revisar, ajustar una línea y cancelar | `driverpay.manage` |
| Aprobar (pide reautenticación, AAL2) | `driverpay.approve` |
| Exportar | `driverpay.export` |

`driverpay.approve` y `driverpay.export` los trae por omisión solo el administrador de la compañía (quien genera no aprueba). Módulo `LTL_GROUND`.

### Qué entra en una liquidación

- **Entregas:** los intentos vigentes y sin liquidar de cada orden y chofer, con fecha dentro del período (días de la compañía). El motor de pago los convierte en dinero con la **fórmula** de la política:
  1. *Entrega + cada intento* (la de fábrica): la tarifa de entrega una vez (si hubo entrega) más la de cada intento, incluido el exitoso.
  2. *La entrega incluye el 1.er intento*: la entrega más los intentos desde el 2.º.
  3. *Intento fallido reemplaza a entrega*: cada intento fallido paga su tarifa; el exitoso paga solo la entrega.
  La **tarifa de entrega** es la del chofer para el servicio de la orden y el tipo de paquete de su **primera línea de carga**, vigente el día de la entrega; las de **intento** son las del chofer por nivel (un intento mayor al último nivel usa el último nivel). Una tarifa que falta deja la línea en **$0 con la nota «sin tarifa configurada»**; nunca se inventa un monto.
- **Viajes:** los viajes pagados (`DriverTrip`) abiertos con fecha del viaje dentro del período, con el monto que se congeló al crearlos (los que no tenían tarifa entran en $0 con la misma nota).
- **Quedan fuera:** las entregas especiales (se pagan por su viaje), los intentos anulados o ya liquidados, y los intentos **sin chofer** (se cuentan en «sin chofer» de la liquidación para que la oficina los complete).

### Pasos y reglas

| Paso | Qué hace | Mensaje si no se puede (422) |
|---|---|---|
| **Vista previa** `POST /driver-settlements/preview` | Muestra lo que entraría, sin guardar nada. | — |
| **Generar** `POST /driver-settlements` (`driverPublicId` opcional, `periodStart`, `periodEnd`, `note`) | Crea la liquidación con sus líneas, **congela** la fórmula y los montos y **reserva** los intentos y viajes (nada se paga dos veces). | `No hay nada por liquidar en el período.` |
| **Revisar** `POST …/review` | Generada → Revisada. | `Solo se revisa una liquidación generada.` |
| **Ajustar una línea** `PATCH …/lines/{id}` (`amount`, `note`) | Cambia el monto de una línea (conserva el calculado); el motivo es obligatorio. Volver al monto calculado quita el ajuste. Solo generada o revisada. | `Solo se ajusta una liquidación generada o revisada.` |
| **Aprobar** `POST …/approve` (AAL2) | Revisada → Aprobada; los viajes de la liquidación pasan a **Liquidado**. | `Solo se aprueba una liquidación revisada.` |
| **Cancelar** `POST …/cancel` (`comment`) | Generada o Revisada → Cancelada; los intentos y viajes reservados quedan libres. | `Solo se cancela una liquidación generada o revisada.` |

Validaciones del cuerpo (400): `Indique el período (desde y hasta).` · `El período no es válido: la fecha final no puede ser anterior a la inicial.` · `El chofer no existe.` · `La nota no puede pasar de 500 caracteres.` · del ajuste: `Indique el monto del ajuste.` · `El monto debe estar entre 0 y 1,000,000.00 con dos decimales como máximo.` · `Indique el motivo del ajuste.` · `El motivo no puede pasar de 250 caracteres.`

### Congelado

La liquidación guarda la fórmula y el desglose de cada línea (entrega e intentos, con su nota). **Si después cambian las tarifas o la política, las liquidaciones ya generadas no se recalculan.** Para recalcular hay que cancelarla y generar otra.

### Casos frecuentes

- **Una entrega salió en $0:** falta la tarifa del chofer para ese servicio y tipo de paquete (o la de algún intento). Complétela en Choferes y tarifas, cancele la liquidación y vuelva a generarla; o ajuste la línea con su motivo.
- **«Hay N intentos sin chofer»:** la ruta de esas órdenes no tenía chofer. Anule el intento y regístrelo con el chofer (§1) y genere de nuevo.
- **No encuentro una entrega:** revise que tenga intentos vigentes en el período, que no sea una entrega especial y que no esté ya en otra liquidación.
- **Aprobar pide reautenticarse:** es normal (AAL2), igual que aprobar una corrida de facturación.

## 3. Exportar la liquidación (Lote 95)

Una liquidación **aprobada** se exporta a un archivo (CSV o TXT) para nómina o contabilidad, con el mismo motor de **plantillas de exportación** de Facturación y de la remesa COD: formato, delimitador, formato de fecha, encabezado y columnas con su orden. El alcance de las plantillas es **`DRIVER_SETTLEMENT`** y trae «Formato estándar» con todas las columnas la primera vez.

| Acción | Endpoint | Permiso |
|---|---|---|
| Vista previa del archivo (no cambia nada) | `GET /api/v1/driver-settlements/{id}/export-preview?templatePublicId=` | `driverpay.export` |
| Exportar: arma el archivo y pasa la liquidación aprobada a **Exportada** (quién y cuándo) | `POST /api/v1/driver-settlements/{id}/export?templatePublicId=` | `driverpay.export` |
| Ver las plantillas y sus campos (`?scope=DRIVER_SETTLEMENT`) | `GET /api/v1/export-templates` y `/fields` | `billing.view`, `cod.view` o `driverpay.view` |
| Crear, cambiar o desactivar plantillas | `/api/v1/export-templates` | `billing.export` |

Si no se manda `templatePublicId` se usa la primera plantilla activa del alcance. Una liquidación **exportada** se puede volver a exportar (otra plantilla, otra copia). Error: `Solo se exporta una liquidación aprobada o exportada.` (422).

**Una fila por línea** de la liquidación (primero por fecha del servicio). Campos disponibles: `SETTLEMENT_NUMBER`, `PERIOD_START`, `PERIOD_END`, `DRIVER_CODE`, `DRIVER_NAME`, `CURRENCY`, `LINE_NO`, `LINE_KIND` (entrega o viaje), `SERVICE_DATE`, `ORDER_NUMBER`, `DESCRIPTION`, `ATTEMPTS`, `DELIVERY_AMOUNT` (lo que paga la entrega o el viaje), `ATTEMPTS_AMOUNT` (lo que pagan los intentos), `CALCULATED_AMOUNT`, `AMOUNT` (lo que se paga, con el ajuste si lo hay), `ADJUSTMENT_NOTE`, `PAYOUT_FORMULA` y `RUN_TOTAL`. El archivo se llama `liquidacion-LIQ-#####`. El mismo archivo sale siempre para la misma liquidación y plantilla (es reproducible).

Teikem **no emite el pago**: el archivo se lleva a nómina o contabilidad.

## 4. La pantalla (Lote 96)

Todo lo anterior se usa desde **Contabilidad → Liquidación a choferes** (dos pestañas: Liquidaciones e Intentos de entrega). Cómo se ve y qué botón sale con cada estatus y permiso: [manual de pantallas, L96](frontend/l96-liquidacion-choferes.md).

El API de intentos devuelve también el **nombre del cliente** de la orden (`clientName`), porque el número de envío se repite entre clientes; la descripción de una línea de entrega dice «Orden {número} · {cliente}».
