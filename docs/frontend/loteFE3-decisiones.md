# Lote F-E3 — Monitoreo de ruta (2026-10-12)

Cuarto y último sub-lote de F-E (F-E1 escaneo, F-E2a Sala de despacho, F-E2b plano de ruta, **F-E3** este). Pantalla `/ops/monitor` (Operación → Monitoreo de ruta, `trips.view`, módulo `LTL_GROUND`), **solo lectura**, que reutiliza el `RouteMap` de F-E2b con su punto vivo. Manual: [fe3-monitoreo.md](../manual/frontend/fe3-monitoreo.md); preguntas: sección «Lote F-E3» de [faq.md](../manual/faq.md); API: capítulo [05 §9](../manual/05-trips-y-rutas.md). Referencia visual: `Diseño/teikem-mockups.html` (`monitoreo()`).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Pantalla | Título con insignia EN VIVO / «Sin señal de choferes», fecha, Actualizar, «Actualizado hace N s», río de totales, dos columnas (300 px + resto; se apilan bajo 900 px) | `features/monitor/MonitorScreen.tsx`, `monitor.css` |
| Lista | «Rutas activas»: buscador al servidor (300 ms), zona, «Incluir completadas», tarjetas con código, chofer, zona, estatus real, % y barra reales, ⚠, «Último ping hace N s» / «Sin ping» | `MonitorRoutesList.tsx` |
| Ruta elegida | `RouteMap` (pines por secuencia con el estado real, punto vivo del ping), Velocidad, Rumbo con punto cardinal, último ping, «Siguiendo en vivo», avisos y «Paradas de la ruta» con el estatus del catálogo | `MonitorRoutePanel.tsx` |
| Datos | `useMonitor` (`GET /trips/monitor`), `useMonitorTrip` (`GET /trips/{id}`, misma caché que el despacho), sondeo propio con pausa por visibilidad, reloj | `monitorApi.ts` |
| Reglas | Edad del ping y «hace N s», EN VIVO, progreso, próxima llegada, rumbo, selección, consulta, interruptor recordado | `monitorRules.ts` |
| Menú | `/ops/monitor` pasó de `pending(...)` a pantalla (misma ruta, permiso, módulo y orden) | `app/routes.tsx` |
| Pruebas de ruta | `Placeholder.test` y `navigation.test` usan ahora `/portal/profile` como ejemplo de ruta pendiente y comprueban que `/ops/monitor` ya no lo es | `app/*.test.*` |

Servidor, `openapi.json` y `schema.d.ts`: **no cambiaron**.

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `monitorRules.test.ts` (19) | edad del ping (UTC sin zona, reloj atrasado, sin hora), «hace N s/min/h/d» es/en, umbral de 120 s, EN VIVO, progreso y total ponderado, próxima llegada, rumbo (8 vientos, vueltas y negativos), velocidad, selección, consulta, interruptor guardado con almacenamiento roto, paridad es/en de `monitor.*`, mapeo de estados |
| `MonitorScreen.test.tsx` (31) | totales y tarjetas, selección con `?trip=` (y ruta que ya no está), parada ↔ pin, búsqueda con retardo y totales intactos, completadas, zona, fecha, Actualizar, sondeo cada 30 s, pausa con pestaña oculta y refresco al volver, apagar el seguimiento (recordado), error de red conservando datos y recuperación, primera carga fallida, sin ping / con ping / ping envejecido / ping no ligado a la ruta, paradas completadas-fallida-en camino (datos simulados), vacíos, permisos (con y sin `trips.dispatch`, sin `fleet.view`, sin `trips.view`), solo lectura, inglés |
| `e2e/loteFE3-monitoreo.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio **1 de 1** y móvil (360 px, sin scroll horizontal en cada paso) **1 de 1**. Siembra por API cliente, zona, dos choferes, dos vehículos y seis órdenes; una ruta con 4 pines manuales, optimizada, despachada e **iniciada**, otra despachada. Capturas `docs/manual/frontend/img/fe3-*.png` (5) |
| `npm run check` | tipos generados, tsc, oxlint (sin avisos nuevos), vitest, build: ver el reporte |
| `dotnet test` | no se corrió: no se tocó el servidor |

## Defectos encontrados y arreglados

1. **El «río» se partía en dos filas a 1280 px** (las dos tarjetas menores quedaban solas y anchas en la segunda): las tarjetas menores tienen ancho propio (`flex: 0 1 150px`; en móvil, mitad de la fila). Visto en el recorrido de escritorio.
2. **Avisos de lint propios** (`Date.now` durante el render y `setState` dentro de un efecto en los ganchos de tiempo): corregidos antes de entregar.
3. **Texto engañoso del aviso de pines**: el servidor cuenta en `approximateStops` también las paradas **sin coordenada** (`MonitorRules.IsApproximate`); el aviso dice «Sin pin exacto: N» (no «aproximados») y el capítulo 05 §9 lo aclara.
4. Del servidor: sin defectos (los totales, la búsqueda y los estatus coincidieron con la pantalla en el recorrido).

## Diferencias con la maqueta (todas con su razón)

1. **Sin «Señal GPS» ni «Batería del dispositivo»**: no hay dato en el API; se omiten (la maqueta los inventa al azar).
2. **Sin interruptor 2D / Satélite**: no hay satélite; no se agrega botón falso (igual que F-E2b).
3. **El plano es el esquemático de F-E2b** (sin calles); el punto del chofer sale del último ping real, no de una posición simulada sobre la ruta.
4. **Insignia EN VIVO solo con un ping de menos de 2 minutos**; si no, «Sin señal de choferes» (la maqueta la pone siempre).
5. **El río suma «Sin ping» y «Sobre el máximo de paradas»** (los da el servidor); la maqueta trae 4 nodos.
6. **«Próxima en completar»** muestra la llegada planeada más próxima y su ruta (la maqueta, la ETA de la ruta más avanzada).
7. **Cada tarjeta lleva el estatus real** (chip del catálogo) y el nombre del chofer en la línea de abajo; la maqueta no trae el estatus.
8. **Paradas con consignatario, pueblo, ventana, llegada estimada y el chip del catálogo** (En camino, Llegó, Completada…) en lugar de «Parada N» + entregada/en curso/pendiente calculados por fracción.
9. **Refresco cada 30 s** (la maqueta, cada 4 s con datos al azar), con Actualizar y «Actualizado hace N s».
10. **«Siguiendo en vivo» enciende o apaga el refresco automático** (la maqueta lo guardaba pero no hacía nada); se recuerda en el navegador.
11. **Controles que la maqueta no tiene**: fecha, zona, «Incluir completadas», Actualizar, selección parada ↔ pin y «Abrir en Sala de despacho».
12. **Subtítulo sin «en vivo»**: «Lo que está pasando ahora mismo en la calle» (no se promete tiempo real mientras no haya pings).

## Decisiones tomadas por default para presentar al dueño al final de P1

- **«Progreso promedio» = paradas atendidas (entregadas + fallidas) sobre el total de paradas, con los totales del servidor (ponderado por paradas)**, no el promedio simple de los porcentajes de cada ruta de la maqueta: así es estable al buscar y es el mismo número que el servidor calcula. Decidir si se prefiere el promedio simple.
- **«Próxima en completar» = la `nextEtaUtc` más temprana de las rutas MOSTRADAS** (el servidor no da un total de ETA; con búsqueda activa depende de la búsqueda). Es lo único del río que no es total del servidor.
- **EN VIVO = ping de menos de 120 segundos**, medido con el reloj del navegador contra `capturedAtUtc`: un navegador con el reloj mal puesto da una edad errónea. El umbral es una constante (`LIVE_WINDOW_SECONDS`).
- **Insignia y «Sin ping» honestos**: hoy ningún endpoint escribe pings (los envía la app del chofer, P7); la pantalla lo explica en un aviso y en el manual. El caso con ping y paradas completadas se probó con datos simulados (vitest) y en el navegador con una respuesta simulada del monitor para la captura de ejemplo.
- **Refresco**: 30 s mientras la pestaña esté visible (`visibilitychange`; oculta no hay temporizador, al volver refresca al instante si pasó más de 30 s), apagable, recordado en `localStorage` (`teikem.monitor.follow`). El detalle de la ruta elegida se refresca junto con el monitor. Un error de red no vacía la pantalla.
- **Por omisión no incluye rutas completadas** (se manda `includeCompleted=false` explícito; el API las incluye por omisión). El río cambia a «Rutas (con completadas)» cuando se activa.
- **Solo lectura**: único enlace de salida «Abrir en Sala de despacho» (`/ops/dispatch?date=&trip=`), con `trips.dispatch`. No se muestran teléfonos ni datos de contacto del chofer.
- **Sin `fleet.view` + CATALOG**: no hay filtro de zona y se explica; la pantalla sigue funcionando.
- **Un ping con punto cuenta para el ajuste del plano**: una ruta sin paradas con coordenadas pero con ping se ve con el punto del chofer (no con el aviso de plano vacío).
- **Fecha por omisión = hoy de la compañía**; `?date=` y `?trip=` en la dirección. Una ruta de `?trip=` que ya no está en la lista (por la búsqueda o por el filtro) cae a la primera.
- **Estatus de parada = chip del catálogo `RouteStopStatus`** con el nombre que use la compañía; los pines usan color por estado (`stateOf`): azul pendiente, azul con aro en curso, verde entregada, rojo fallida.

## Qué no se pudo verificar

- **Pings reales y paradas en movimiento**: no hay endpoint que los escriba; solo con datos simulados.
- **Más de unas pocas rutas activas** (decenas de tarjetas y rutas con muchas paradas) y el cambio de rutas mientras se mira la pantalla durante horas.
- **Reloj del navegador desfasado** respecto al servidor para la edad del ping.
- **Una pestaña real en segundo plano** durante minutos (la pausa se probó con `visibilitychange` simulado).
