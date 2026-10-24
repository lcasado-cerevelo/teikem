# Lote 215 — App móvil: rutas de hoy y asignar en la calle

## Decisión de Luis (2026-10-23/24)
El personal en la calle debe poder **ver las rutas de hoy** y **asignar** órdenes a rutas (mandar, pasar de una ruta a otra, sacar), con un **permiso propio** (`trips.streetassign`), porque `trips.dispatch` es del almacén. En esta parte de la app **no se trabaja sin señal**: solo lo del chofer se baja y funciona sin conexión.

## Qué se construyó
- **Permiso `trips.streetassign`** («Asignar órdenes a rutas desde la calle (app móvil)», categoría Transporte; el total de permisos pasa a 149). Sección 2026-10-24 (n) del SQL: solo se agrega a los roles **TenantAdmin** ya clonados; cada compañía se lo da después a quien quiera en Sistema → Roles. **No** se reparte por `trips.dispatch` ni va en la plantilla Despachador (ver decisiones).
- **Rutas de hoy** (app, `trips.view`): usa los endpoints de la web (`GET /trips`, `GET /trips/{id}`); no hay servidor nuevo. Lista de rutas del día por compañía con chofer, vehículo, estatus y paradas; la ficha de la ruta muestra las paradas en orden y la última ubicación del chofer; tocar una parada abre la ficha de la orden.
- **Asignar en la calle** (servidor, `StreetAssignService`, endpoints `/api/v1/mobile/street/*`, `trips.streetassign` + módulo de transporte):
  - `GET orders/{id}/targets`: las rutas abiertas de hoy (planeadas, despachadas o en curso) **del mismo tipo** que la orden, ordenadas por **cercanía** al domicilio (distancia en línea recta desde la última ubicación que mandó el chofer; sin ubicación, sin distancia), y los choferes y vehículos activos para la ruta rápida. Una orden en su etapa inicial es de recogida; las demás, de entrega.
  - `POST assign`: manda la orden a la ruta; si estaba en otra, la **pasa** (la saca de la anterior y avisa a su chofer). En una ruta **despachada** la orden queda «Asignada»; en una **en curso**, «En tránsito»; en una de recogida no cambia de estatus. Al chofer se le deja un aviso (`STOP_ADDED`) que le llega al teléfono.
  - `POST unassign`: la saca de la ruta; si era de entrega vuelve a «Sin asignar» con su historial; avisa al chofer (`STOP_REMOVED`).
  - `POST quick-route`: crea la ruta de hoy para el chofer y vehículo elegidos con la(s) orden(es), la **despacha** (el chofer recibe «Nuevo manifiesto» y la acepta en su app). Si algo falla después de crearla, la ruta se cancela.
  - Reglas de siempre (`RouteWriter`: elegibilidad, tope de paradas, una sola ruta vigente por orden). Nuevo: `AddOrdersLiveAsync` / `ReleaseOrderLiveAsync` (rutas despachadas o en curso; la versión vigente se modifica en el sitio, igual que cuando el chofer no acepta un envío).
- **App**: acceso «Rutas de hoy» en el inicio (con `trips.view`); en la pestaña **Acciones** de la orden el botón **«Asignar a una ruta»** (con `trips.streetassign`) abre la pantalla con las rutas (distancia, última ubicación, «Mandar / Pasar a esta ruta»), «Sacar de la ruta» y la ruta rápida (chofer + vehículo). Cada cambio pide confirmación y avisa si el chofer fue notificado.
- Sin tablas nuevas.

## Cómo se probó
- Servidor: 6 pruebas nuevas (`TripStatusEffectTests.Street.cs`): mandar a una ruta despachada (parada agregada, orden «Asignada», aviso al chofer, historial), a una en curso («En tránsito»), rechazos (ruta de otro tipo, ya está en esa ruta, orden cerrada, datos vacíos, orden inexistente), sacar de la ruta (vuelve a «Sin asignar», aviso), pasar de una ruta a otra (avisa a los dos choferes) y la lista de destinos. Suite completa **5496 en verde**; web `npm run check` 3509 en verde.
- App: `tsc`, lint de lo tocado, prueba pura nueva (`npm run test:street`) y `expo export`. **No probado en teléfono ni contra el API real.**
- La ruta rápida compone servicios ya probados (crear, agregar órdenes, despachar) pero **no tiene prueba propia** (el fixture de estas pruebas no los trae). La distancia y la ubicación del chofer necesitan SQL Server: tampoco se probaron en las pruebas de memoria (la regla de distancia sí).

## Decisiones a revisar
- **Quién recibe el permiso**: solo TenantAdmin por omisión; la plantilla Despachador **no** lo trae porque dijiste que `trips.dispatch` es del almacén. Si el despachador de calle es ese rol, se agrega a la plantilla (y al SQL) en un cambio pequeño.
- **Mover una orden «En tránsito»** (el chofer ya salió con ella) se permite: vuelve a «Sin asignar» y entra a la otra ruta con el estatus que corresponda. Si ya tenía un intento de entrega fallido, no es movible (está «Fallida»).
- **Ruta rápida**: pide chofer y vehículo (no hay vehículo por omisión del chofer) y avisa si el chofer ya tiene otra ruta abierta; no impide que tenga más de una.
- La **distancia** es en línea recta desde la última ubicación del chofer en una ruta **en curso**; las rutas despachadas aún sin salir no tienen ubicación.
- En esta parte de la app el personal **no trabaja sin señal** (decidido): sin conexión no hay rutas, destinos ni cambios.
