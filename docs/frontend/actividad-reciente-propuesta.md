# Actividad reciente en Pulso — propuesta de eventos (borrador para aprobación de Luis)

Estado: **propuesta, pendiente de aprobación**. Cuando Luis apruebe, la decisión se escribe en
`Diseño/logistica-funcionalidades-maestro.md` (módulo 12, dashboard) como fuente única y se implementa como el
próximo lote (backend + frontend). Mock: `docs/frontend/mock-pulso-almacen.html` (abrir en el navegador; botón "Móvil 360 px").

## 1. Qué es

Un panel "Actividad reciente" al pie de Pulso del día con una pestaña por **módulo de negocio** (catálogo
`BusinessModule`: Almacén, Operación, Contabilidad). Cada fila es un **evento**: hora, evento (chip), referencia
(enlace a la ficha), detalle corto y quién lo hizo. No es la auditoría (`AuditLog`, que exige `admin.audit`): es una
lectura de negocio, filtrada por lo que el usuario puede ver.

Reglas:

- **Visibilidad por permiso, no por rol**: la pestaña Almacén aparece con `inventory.view`; Operación con `orders.view`;
  Contabilidad con `billing.view`. Así cada rol recibe lo suyo sin configurar nada, y el administrador ve todo.
- **Obligatorio u opcional**: un evento obligatorio se muestra siempre a quien ve el módulo (no se puede ocultar). Un
  evento opcional el usuario lo puede apagar (interruptor "Solo obligatorios" en el panel y, más adelante, preferencia
  guardada por usuario). La misma bandera servirá para las notificaciones por correo o push cuando se construyan.
- **Ventana**: últimas 24 h por defecto (48 h y "Hoy" como opciones); máximo 50 filas por pestaña con "Ver más".
- **Fuentes** (todas existen ya): `EntityStatusHistory` (transiciones de estatus), `InventoryLedger` (movimientos de
  inventario), `AuditLog` (altas/bajas de catálogo). No se crea ninguna tabla de eventos: el panel se calcula al leer.

## 2. Catálogo propuesto: `ActivityEventType` (LookupCode)

Columnas: código · módulo · fuente y condición · etiqueta es / en · **obligatorio** · referencia que enlaza.

### Almacén (completo, es el primero que se construye)

| Código | Fuente y condición | Etiqueta es / en | Oblig. | Enlace |
|---|---|---|---|---|
| `RECEIPT_CONFIRMED` | `EntityStatusHistory` RECEIPT → `RECEIVED` | Recibo confirmado / Receipt confirmed | **Sí** | Recibo |
| `RECEIPT_VARIANCE` | `InventoryLedger` ADJUSTMENT con motivo `RECEIPT_VARIANCE` | Diferencia en recibo / Receipt variance | **Sí** | Recibo |
| `RECEIPT_PUTAWAY_DONE` | `EntityStatusHistory` RECEIPT → `PUTAWAY` | Recibo acomodado / Receipt put away | No | Recibo |
| `ASN_CANCELLED` | `EntityStatusHistory` ASN → `CANCELLED` | Aviso de llegada cancelado / ASN cancelled | No | ASN |
| `PUTAWAY_DONE` | `WarehouseTask` tipo PUTAWAY → `DONE` | Acomodo completado / Put-away completed | No | Tarea |
| `REPLENISH_DONE` | `WarehouseTask` tipo REPLENISH → `DONE` | Reabasto completado / Replenishment completed | No | Tarea |
| `TASK_CANCELLED` | `WarehouseTask` → `CANCELLED` | Tarea cancelada / Task cancelled | No | Tarea |
| `COUNT_FINISHED` | `EntityStatusHistory` CYCLE_COUNT → `COUNTED` | Conteo terminado / Count finished | No | Conteo |
| `COUNT_RECONCILED` | `EntityStatusHistory` CYCLE_COUNT → `RECONCILED` | Conteo reconciliado / Count reconciled | **Sí** | Conteo |
| `COUNT_VARIANCE` | `InventoryLedger` ADJUSTMENT con motivo `COUNT_VARIANCE` | Diferencia de conteo aplicada / Count variance applied | **Sí** | Conteo |
| `INVENTORY_ADJUSTED` | `InventoryLedger` ADJUSTMENT con motivo manual (DAMAGE, LOSS, FOUND, EXPIRED, OTHER) | Ajuste de inventario / Inventory adjustment | **Sí** | Kárdex filtrado |
| `INVENTORY_TRANSFERRED` | `InventoryLedger` TRANSFER entre almacenes distintos | Transferencia entre almacenes / Warehouse transfer | No | Kárdex filtrado |
| `BIN_MOVED` | `InventoryLedger` TRANSFER dentro del mismo almacén | Movimiento de posición / Bin move | No (apagado por defecto) | Kárdex filtrado |
| `PICK_COLLECTED` | `EntityStatusHistory` PICK_BATCH → `COLLECTED` | Recolección creada / Pick batch collected | No | Recolección |
| `PICK_PACKED` | `EntityStatusHistory` PICK_BATCH → `PACKED` | Recolección empacada / Pick batch packed | No | Recolección |
| `PICK_CANCELLED` | `EntityStatusHistory` PICK_BATCH → `CANCELLED` | Recolección eliminada / Pick batch cancelled | **Sí** | Recolección |
| `PO_SENT` | `EntityStatusHistory` PURCHASE_ORDER → `SENT` | Orden de compra enviada / Purchase order sent | No | Orden de compra |
| `PO_RECEIVED` | `EntityStatusHistory` PURCHASE_ORDER → `RECEIVED` | Orden de compra recibida completa / Purchase order fully received | No | Orden de compra |
| `PO_CANCELLED` | `EntityStatusHistory` PURCHASE_ORDER → `CANCELLED` | Orden de compra cancelada / Purchase order cancelled | **Sí** | Orden de compra |
| `PO_SHORTAGE_RESOLVED` | `AuditLog` UPDATE de línea de compra con acción de faltante | Faltante resuelto / Shortage resolved | No | Orden de compra |
| `CROSSDOCK_COMPLETED` | `EntityStatusHistory` CROSSDOCK_PLAN → `COMPLETED` (solo con módulo CROSSDOCK) | Cruce de muelle completado / Cross-dock completed | No | Plan |
| `PRODUCT_DEACTIVATED` | `AuditLog` PRODUCT con `IsActive` 1 → 0 | Producto dado de baja / Product deactivated | No | Producto |
| `WAREHOUSE_DEACTIVATED` | `EntityStatusHistory` WAREHOUSE → `INACTIVE` | Almacén dado de baja / Warehouse deactivated | **Sí** | Almacén |

Criterio para "obligatorio": todo lo que **cambia el saldo de inventario sin pasar por el flujo normal** (ajustes,
diferencias, reconciliaciones, eliminación de una recolección) o **cierra un documento** (recibo confirmado, orden de
compra cancelada, almacén dado de baja). Lo demás es operación rutinaria y cada usuario decide si quiere verlo.

### Operación (esbozo; se completa cuando su módulo llegue al frontend)

| Código | Fuente y condición | Etiqueta es | Oblig. |
|---|---|---|---|
| `ORDER_CONFIRMED` | TRANSPORT_ORDER → `CONFIRMED` | Orden confirmada | No |
| `ORDER_DELIVERED` | TRANSPORT_ORDER → `DELIVERED` | Orden entregada | No |
| `ORDER_FAILED` | TRANSPORT_ORDER → `FAILED` | Entrega fallida | **Sí** |
| `ORDER_ON_HOLD` | TRANSPORT_ORDER → `ON_HOLD` | Orden en espera | **Sí** |
| `ORDER_CANCELLED` | TRANSPORT_ORDER → `CANCELLED` | Orden cancelada | **Sí** |
| `TRIP_DISPATCHED` | TRIP → `DISPATCHED` | Viaje despachado | No |
| `TRIP_COMPLETED` | TRIP → `COMPLETED` | Viaje completado | No |
| `TRIP_CANCELLED` | TRIP → `CANCELLED` | Viaje cancelado | **Sí** |
| `ROUTE_OVER_MAX_STOPS` | Ruta supera el máximo de paradas del chofer (alerta ya diseñada en el maestro) | Ruta sobre el máximo de paradas | **Sí** |
| `DOCUMENT_EXPIRING` | `FleetDocument`/licencia con vencimiento ≤ 30 días (alerta ya diseñada) | Documento por vencer | **Sí** |

### Contabilidad (esbozo)

| Código | Fuente y condición | Etiqueta es | Oblig. |
|---|---|---|---|
| `INVOICE_ISSUED` | INVOICE → `ISSUED` | Factura emitida | No |
| `INVOICE_PAID` | INVOICE → `PAID` | Factura pagada | No |
| `INVOICE_OVERDUE` | INVOICE → `OVERDUE` | Factura vencida | **Sí** |
| `INVOICE_VOID` | INVOICE → `VOID` | Factura anulada | **Sí** |
| `COD_COLLECTED` | COD → `COLLECTED` | COD cobrado | No |
| `COD_REMITTED` | COD → `REMITTED` | COD remitido | No |
| `BILLING_RUN_APPROVED` | BILLING_RUN → `APPROVED` | Corrida de facturación aprobada | **Sí** |
| `CREDIT_EXCEEDED` | Orden creada con crédito excedido (alerta ya diseñada, permiso `orders.credit_override`) | Crédito de cliente excedido | **Sí** |

## 3. Lo que ya está programado y no se toca

- **Pulso del día (F1)**: indicadores y gráficos del motor de analítica con "Aparece en Pulso", visibilidad por rol o
  usuario, rango de fecha por elemento. El panel nuevo va debajo; no reemplaza nada.
- **Panel "Almacén" (F6)**: cinco tarjetas de saldo actual calculadas en cliente (en mano, disponible, recibos abiertos,
  tareas pendientes por tipo, conteos abiertos). Se conserva tal cual.
- Indicadores y gráfico marcados "propuesto" en el mock (productos bajo mínimo, unidades recibidas, conteos con
  diferencia, movimientos por tipo) son definiciones de sistema nuevas del motor existente: solo seed, sin código nuevo.

## 4. Impacto estimado del próximo lote

- **Backend**: seed del catálogo `ActivityEventType` (con la bandera de obligatorio en el JSON de descripción o una
  columna nueva en `LookupCode`, a decidir), servicio de lectura `ActivityFeedService` que une las tres fuentes por
  tenant, ventana y permisos, endpoint `GET /api/v1/analytics/activity?module=&window=&onlyMandatory=&skip=&take=`,
  índice `IX_EntityStatusHistory_TenantDate (TenantId, ChangedAtUtc)` en `Diseño/logistica-db-estructura.sql`
  (recrear la base local), seed de los indicadores y el gráfico de sistema propuestos. Sin tablas nuevas.
- **Frontend**: panel `ActivityPanel` en Pulso con pestañas, ventana, interruptor y tarjetas bajo 720 px; hook
  `useActivity`; pruebas; paso Playwright; capítulo del manual.
- **Consumo estimado**: 2 a 3 millones de tokens backend + 0.5 a 1 millón frontend con el modelo de bajo consumo.

## 5. Decisiones que debe tomar Luis

1. Lista de eventos de Almacén y cuáles son obligatorios (tabla de arriba como punto de partida).
2. Ventana por defecto: 24 h (propuesto) o número fijo de filas.
3. Si la preferencia "apagar eventos opcionales" se guarda por usuario en este lote o queda solo como interruptor de
   pantalla (propuesto: solo interruptor ahora; preferencia guardada cuando se hagan las notificaciones).
4. Si los indicadores y el gráfico propuestos entran en este lote o después.
