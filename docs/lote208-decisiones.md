# Lote 208 — Seguimiento de la ruta del chofer (GPS)

## Qué se construyó
**Servidor**
- `POST /api/v1/driver-app/tracking` (lote de puntos y avisos), `GET /api/v1/driver-app/tracking/config` y `GET /api/v1/trips/{id}/tracking` (la huella y los avisos de una ruta, permiso `trips.view`, para el monitor).
- Los puntos se guardan en **`DriverLocationPing`** (la tabla que ya lee el monitor), ligados a la ruta **en curso** del chofer en esa compañía. Si el chofer trabaja para dos compañías con rutas en curso, la app manda el lote a las dos con el token de cada una y **cada compañía guarda los suyos**. Idempotente (misma hora de captura no se repite).
- `DriverTrackingEvent` (tabla nueva, sección 2026-10-23 (c) de `logistica-db-update.sql`): STARTED, STOPPED_BY_DRIVER, PERMISSION_LOST, RESUMED — así el monitor distingue «lo apagó» de «sin señal».
- Valores **globales** en appsettings `DriverApp:Tracking`: `MinDistanceMeters` 150, `MaxIntervalSeconds` 90, `UploadEverySeconds` 120, `RetentionDays` 10. Trabajo programado `DRIVER_TRACKING_PURGE` (03:30) borra lo más viejo que la retención.

**App** (`src/tracking/`)
- Tarea de ubicación en segundo plano (`expo-task-manager` + `expo-location`, servicio en primer plano con notificación). Se guarda un punto **por movimiento**: ≥150 m desde el último guardado, o 90 s si se está moviendo (≥30 m); **quieto = nada**. Los puntos esperan en el teléfono (SQLite) y salen **por lotes cada 2 minutos**; sin señal se acumulan (máx. 2,000) y salen al volver.
- Corre solo con una **ruta en curso** (manifiesto aceptado con envíos abiertos) y se detiene sola al terminar. Aviso previo (qué se recoge, para qué, 10 días) antes de pedir el permiso; si no da «todo el tiempo», sigue con la app abierta.
- Perfil → «Enviar mi ubicación durante la ruta»: el chofer puede apagarlo (confirmación; la oficina recibe el aviso en el monitor).

## Cómo se probó
Pruebas de reglas (`DriverTrackingRulesTests`), del servicio (`TripStatusEffectTests.Tracking`: avisos, ruta en curso, validaciones, no-chofer), confinamiento del SQL crudo (`RawSqlConfinementTests`), reglas puras de la app (`npm run test:tracking`). **Los puntos (GEOGRAPHY) y la purga usan SQL crudo: no se ejecutan con InMemory; falta probarlos contra SQL Server (el CI con db-init + smoke) y la tarea en un teléfono Android real.**

## Decisiones a revisar
- **Aviso a la oficina** cuando el chofer apaga el GPS: hoy queda registrado y visible por `GET /trips/{id}/tracking`; falta mostrarlo en el monitor web y, si se quiere, mandar una notificación (hay que decidir a quién).
- Pantalla del **mapa/huella** en el monitor web: pendiente (el endpoint ya existe).
- **Batería**: el chofer debe excluir la app de la optimización de batería (el texto de Perfil lo dice); algunos fabricantes (Xiaomi, Huawei) matan el servicio de todos modos.
- **iPhone**: no configurado (permiso «siempre» y modos de fondo de iOS); Android primero, como se acordó.
- El **aviso de privacidad del chofer** (documento aparte, para revisión legal) está pendiente; el texto del aviso previo ya dice qué se recoge.

## Recorrido y avisos en el monitor web (2026-10-23)
Monitoreo de ruta → ruta elegida: el plano dibuja el **recorrido real** del chofer (línea fina azul bajo los pines; `RouteMap` ganó la propiedad `track`) y un recuadro «Recorrido del chofer» dice cuántos puntos hay guardados y lista los **avisos**: «Empezó a enviar su ubicación», «El chofer apagó el envío de su ubicación», «Perdió el permiso de ubicación», «Volvió a enviar su ubicación», cada uno con su hora. Lee `GET /api/v1/trips/{id}/tracking` (permiso `trips.view`) y se refresca cada minuto. Pruebas: monitor y plano (562 verdes en total con `kernel/ui`).
