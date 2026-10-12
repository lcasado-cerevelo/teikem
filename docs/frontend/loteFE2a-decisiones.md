# Lote F-E2a — Sala de despacho, sin mapa (2026-10-12)

Segundo sub-lote de F-E (F-E1 escaneo, **F-E2a** este, F-E2b mapa esquemático + arrastrar + pin manual, F-E3 Monitoreo; F-E2b y F-E3 siguen pendientes). Pantalla Operación → **Sala de despacho** (`/ops/dispatch`, `trips.dispatch`, módulo `LTL_GROUND`). Manual: [fe2a-despacho.md](../manual/frontend/fe2a-despacho.md); preguntas: sección «Lote F-E2a» de [faq.md](../manual/faq.md). Referencia: `Diseño/teikem-mockups.html` (`dispatch()`, `renderDispatchModal`). Lote hermano: [loteFE1-decisiones.md](loteFE1-decisiones.md).

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/features/dispatch/`) |
|---|---|---|
| Pantalla | Cabecera (fecha, Planificar el día, Nueva ruta, Reasignar por zona, Optimizar, Despachar ruta), barra de filtros y tres columnas con `SplitPane` anidado (se apilan bajo 900 px) | `DispatchScreen.tsx`, `dispatch.css` |
| Órdenes sin asignar | Contador, «Marcar todas», buscador, «Añadir a la ruta (N)», filas con casilla, 422 con cada número y motivo | `UnassignedPanel.tsx` |
| Ruta activa | Estatus real, zona · paradas, banner de máximo, chofer y vehículo, avance, avisos, última optimización, «No cupieron», órdenes con ▲ ▼ y quitar, Iniciar / Cerrar / Eliminar | `ActiveRoutePanel.tsx`, `RouteStopsList.tsx`, `TripIssues.tsx` |
| Choferes y Rutas de hoy | Panel con «≤máx» y marca «No disponible»; tarjetas con barra (avance del monitor) y ⚠ | `DispatchSidePanels.tsx` |
| Ventanas | Nueva ruta, Planificar el día (con resultado), Reasignar por zona (con resultado), Rutas por despachar (lote) | `TripCreateModal.tsx`, `PlanDayModal.tsx`, `ReassignZoneModal.tsx`, `DispatchModal.tsx` |
| Reglas y datos | Acciones por permiso Y estatus, ▲ ▼, avisos, lote, comparación de optimizaciones, formularios con los mensajes del servidor; hooks con `Idempotency-Key` | `dispatchRules.ts`, `dispatchApi.ts`, `fleetChoices.ts` |
| Kit | `SearchMultiSelectInput` (selección múltiple dentro de un `Field`, con prueba); sección «Despacho» de `KIT.md` | `kernel/ui` |

Ruta: `/ops/dispatch` dejó de ser `pending` (`routes.tsx`, mismo permiso, módulo y orden de menú); `/ops/monitor` sigue `pending`. Servidor y contrato: **no cambiaron** (ver defecto 4).

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `dispatchRules.test.ts` (31) | acciones por permiso y estatus, ▲ ▼, avisos/bloqueantes, lote, comparación de optimizaciones, errores por orden, fechas, formularios con los mensajes del servidor, filtros, paridad es/en |
| `DispatchScreen.test.tsx` (65) | tres columnas sin mapa, cada acción con su error del servidor (422 al añadir, 409, 409 de chofer, 422 al cerrar), bloqueantes vs avisos, confirmación con avisos, lote con resultados mixtos, planificar el día, reasignar por zona (incluido cero), fecha, filtros al servidor, y permisos con/sin `trips.plan`/`optimize`/`dispatch`/`close`/`view` y sin `fleet.view` o CATALOG |
| `SearchMultiSelectInput.test.tsx`, `problem.test.ts` | el control nuevo del kit; claves de error que no son campos |
| `e2e/loteFE2a-despacho.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio y móvil (360 px, sin scroll horizontal). Capturas `docs/manual/frontend/img/fe2a-*.png` |
| `dotnet test Teikem.sln` | 3565 pruebas, todas pasan (sin cambios de servidor) |

(Los conteos totales de vitest y de Playwright van en el reporte del lote.)

## Defectos encontrados y arreglados

1. **Cliente: `applyProblemDetails` mutilaba las claves de error que no son campos.** El 422 de «añadir órdenes» usa el número de la orden como clave (`OT-0001`) y salía `oT-0001`; los códigos de aviso (`NO_DRIVER`) igual. `normalizeField` ahora solo pasa a minúscula la primera letra de nombres de campo en PascalCase (`ClientPublicId`, `Lines[0].Qty`) y deja intactas las claves con guion o en mayúsculas (prueba en `problem.test.ts` y en la pantalla).
2. **Manual 05**: decía que sin fecha el listado, el selector de despacho y el monitor usan «hoy UTC»; el servidor usa el día de la compañía. Corregido.
3. **Pantalla (hallazgo en la maqueta)**: las rutas sin coordenadas no tienen distancia (15 min fijos por tramo); la comparación «-12 % distancia» no puede darse siempre. Se compara cada medida solo si las dos corridas la traen y, si no hay nada comparable, la pantalla lo dice (antes habría afirmado «primera optimización»).
4. **COD en la fila de «sin asignar»**: se intentó agregar `CodAmount` a `UnassignedOrderDto`, pero la prueba `No_trip_dto_exposes_amounts_or_rates` (regla del capítulo 05: «sin dinero» en las rutas) lo prohíbe, con razón. Se revirtió el cambio de servidor y contrato; el COD no se pinta (diferencia con la maqueta).

Del servidor, lo demás (planificar el día idempotente, elegibilidad al añadir, 409 de chofer sin licencia, despacho con avisos y lote, cierre con 422, eliminar, reasignar con cero rutas) se recorrió por API y por pantalla y no mostró defectos.

## Diferencias con la maqueta (todas con su razón)

1. **Sin mapa, sin selector 2D/Sat, sin pines ni leyenda de direcciones, sin «tracking en vivo»**: son de F-E2b y F-E3; no hay botones ni espacios vacíos.
2. **Sin COD en las filas de sin asignar** (ver defecto 4).
3. **Reasignar por zona es un botón de la cabecera con ventana** (zonas múltiples + chofer + fecha), no controles dentro de la barra de filtros; la barra solo trae los filtros de las órdenes.
4. **Filtros**: CP y Pueblo son texto (al servidor), Cliente y Zona buscan/seleccionan; en la maqueta eran selectores de valores. Van al servidor y sirven con más de 500 órdenes.
5. **Estatus reales del servidor** (Borrador, Planificado, Despachado, En curso, Completado, Cancelado: nombres del catálogo de la compañía) en lugar de los tres de la maqueta.
6. **▲ ▼ en la lista de órdenes de la ruta** (la maqueta no ordenaba a mano); papelera «Quitar» con su nombre accesible.
7. **Planificar el día, Nueva ruta, Iniciar, Cerrar y Eliminar** no existen en la maqueta (solo Eliminar); sus botones y ventanas son reales.
8. **«Rutas por despachar»**: bloqueantes (rojo, no se marcan) y avisos (ámbar, confirmación en dos pasos), comentario opcional y resultado por ruta (la maqueta marcaba todas como despachadas sin más).
9. **Panel Choferes**: agrega la marca «No disponible» del día; muestra todos los activos (la maqueta atenuaba a los sin zona; se conserva).
10. **Ruta activa**: agrega el resumen de distancia/duración/peso/volumen, avisos, «No cupieron» y la salida; «ETA» es la hora estimada de fin de la ruta.

## Decisiones tomadas por default para presentar al dueño al final de P1

- Selección en `?trip=` y fecha en `?date=` (hoy de la compañía por omisión; se omite del enlace si es hoy). Sin `?trip`, la primera ruta de la lista (no se escribe en el enlace).
- Rutas del día: `GET /trips?date=` (arreglo, sin paginación); el avance de las tarjetas sale de `GET /trips/monitor` (si falla, la barra queda en 0).
- Sin asignar: se piden hasta 500 con los filtros al servidor; el buscador filtra lo cargado; con más, avisa «Se muestran las primeras N de M». Un máximo de 200 por vez al añadir (se bloquea el botón con el mensaje del servidor).
- Chofer y vehículo se cambian al elegir (PATCH mínimo con `rowVersion`); lo no disponible se marca «no disponible» **pero se puede elegir** y el servidor contesta con su 409 exacto; el select vuelve al valor de la ruta. En ruta despachada con `EDIT_TRIP` no se ofrece «Sin chofer/vehículo» (el servidor no deja quitarlos).
- Toda escritura lleva `Idempotency-Key` por intento y, también ante un error, vuelve a pedir lista, ficha, sin asignar, despachables, avance y disponibilidad.
- «Optimizar» está en la cabecera y actúa sobre la ruta activa; se deshabilita (con su razón) sin paradas o si la ruta no está abierta. La comparación usa `optimization-runs`: solo si hay una corrida anterior `OK`.
- «Despachar ruta» abre el selector con la ruta activa marcada si se puede; con avisos el primer clic pide confirmar («Despachar con avisos (N)»); el lote usa `POST /trips/dispatch`.
- Iniciar, Cerrar y Eliminar usan el diálogo con comentario opcional (≤ 500); el 422 de cierre se muestra dentro del diálogo, que no se cierra.
- Sin `fleet.view` o sin CATALOG no se piden choferes, vehículos, disponibilidad ni zonas: se explica en lugar de romper, y Planificar el día planifica todas las zonas.
- El botón «Listo» (y no «Cerrar») cierra las ventanas de resultado, para no repetir el nombre de la «X» de la ventana.
- El origen del máximo de paradas («default de la compañía» / «override del chofer») solo se dice si se conoce al chofer (`fleet.view`).

## Qué no se pudo verificar

- **Roles reales sin `trips.view`/`fleet.view`/`trips.optimize`/`trips.close`**: cubierto con pruebas de componente, no con Playwright.
- **Más de 500 órdenes sin asignar y rutas con cientos de paradas**: probado solo con respuestas simuladas.
- **Dos usuarios editando la misma ruta a la vez**: el 409 y la recarga se probaron con respuestas simuladas; en Playwright solo se probó el 409 por «orden asignada por otra persona» (ruta auxiliar creada por API).
- **Optimización con coordenadas reales** (la base de pruebas no geocodifica): la comparación de distancia se probó con respuestas simuladas; con el API real solo hubo comparación de duración.
- **Un tenant con `EDIT_TRIP` habilitada en Despachado**: probado con respuestas simuladas.
