# Lote 203 — App del chofer, etapa 1: servidor (2026-10-23)

## Qué se construyó
- `Trip.AcceptedAtUtc/AcceptedBy`, tabla `TripOrderDecline` (envíos no aceptados con motivo y nota), lista «Motivos de no aceptación» (`TripDeclineReason`), tipo de bitácora `TRIP_ORDER_DECLINE` y permiso `driverapp.use` (rol Driver y roles ya clonados) en `logistica-db-update.sql`.
- `DriverAppService` + `DriverAppController` (`/api/v1/driver-app`): manifiestos del chofer, detalle con envíos en orden de ruta, motivos y **aceptar** (= la salida con `TripLifecycleService.StartTrackedAsync`; los no aceptados salen de la ruta con `RouteWriter.ReleaseOrderFromDispatchedAsync`, vuelven a «Sin asignar» y se guardan con su motivo).
- `app-movil/`: copia fiel de la app actual (Colavora) con las recogidas primero en el inicio; aún no está conectada a Teikem.

## Cómo se probó
7 pruebas nuevas (`TripStatusEffectTests.DriverApp`: aceptar todo, rechazar con motivo, rechazar todo, doble aceptación, validaciones, ajeno/sin chofer, listado) sobre los efectos reales de viaje, más las pruebas de catálogo, permisos, aislamiento por compañía y SQL.

## Decisiones a revisar
- Aceptar es **por manifiesto** (un solo envío del chofer: aceptar todo o dejar fuera los que tengan motivo); no hay aceptación envío por envío en pasos separados.
- Si no acepta ninguno, la ruta queda despachada y vacía (la oficina decide). Falta el aviso a la oficina de «envío no aceptado» (evento de notificación) y verlo en el monitoreo.
- Solo entregas (`Kind = DELIVERY`). Los manifiestos de recogida necesitan el viaje con tipo (siguiente etapa).
