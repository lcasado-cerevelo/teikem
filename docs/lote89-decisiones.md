# Lote 89 — Radar de envíos abiertos y avisos de operación (P12, 2) (2026-10-17)

Segundo lote de P12: el **Radar** de la maqueta y los avisos «órdenes sin chofer» y «COD sin cuadrar» en «Necesita tu atención». Sigue el plan del [Lote 88](lote88-decisiones.md) (90: fotos diarias; 91: indicadores y Actividad; 92: informes por correo).

## Qué se construyó
- **Servidor:** panel `RADAR` (orden 60; `pulse.orders` + `orders.view` + `LTL_GROUND`, sin permisos nuevos), `RadarRules` (qué es «abierta»), `PulseRadarService` y `GET /api/v1/pulse/radar` (grupos por pueblo de la parada de entrega o por cliente, con el COD).
- **Avisos:** `OrdersNoDriverAttentionProvider` (`trips.dispatch`) y `CodUnreconciledAttentionProvider` (`cod.reconcile`): filas **agregadas** con un nuevo campo `Summary` en `AttentionItemDto`; el feed pone primero las filas resumen y luego las más antiguas. `PulseRiverService.UnassignedQueryAsync` comparte la cuenta de «sin chofer» con el río, el aviso y el Radar.
- **Web:** `RadarPanel.tsx` (cuántas abiertas, selector Pueblo/Cliente, buscador, grupos con enlace), `radarApi.ts`, `radarRules.ts`; **Órdenes** recibe `?city=`, `?client=` y `?statuses=`; textos de los dos avisos en `attention.ts`; textos es/en y estilos.
- Manual 07 §7, manual de pantallas L89 con capturas, FAQ y este documento.

## Cómo se probó
- xunit (`PulseRadarTests`, 7): reglas, el Radar por pueblo y por cliente (solo lo abierto: sin entregadas, borradores, canceladas ni dadas de baja; COD; tope; `groupBy` inválido 400; otra compañía en cero), el aviso sin chofer (fila agregada; sale de la cuenta al entrar a una ruta con chofer), el aviso de COD sin cuadrar (suma, cobros, órdenes y días; baja al cuadrar), las filas resumen primero en el feed y paneles/permisos del endpoint. Suite completa verde.
- vitest: reglas del Radar y filtros de Órdenes (5), textos de los dos avisos (6) y el panel con fetch simulado (5). `npm run check` completo. Playwright contra el API real, escritorio y móvil: el Radar con sus grupos, el cambio a Cliente, la búsqueda por el consignatario de la corrida (con `E2E_SQL_DB` la orden queda En almacén), el clic a Órdenes ya filtrado y el aviso «órdenes sin chofer» con su enlace a la Sala de despacho.
- Hallazgo: el feed solo muestra las 5 filas más antiguas y las rentas vencidas tapaban los avisos nuevos; por eso las filas resumen van primero.

## Decisiones a revisar con Luis
1. **Abierta = confirmada hasta parcial, sin borradores** (la maqueta solo excluía lo entregado). Si se quieren ver los borradores en el Radar, es agregar `DRAFT` a `RadarRules.OpenStatuses`.
2. **«Por cliente» agrupa por el cliente de la orden** (la maqueta decía «Cliente» pero agrupaba por consignatario); por consignatario no hay grupo porque suele repetirse poco.
3. **Los avisos agregados salen antes** que las filas por registro; las rentas y descuadres siguen ordenadas por antigüedad.
4. **SLA en riesgo** queda para cuando exista el POD (P7): hace falta la hora real de entrega.
5. **Urgencia:** rojo desde 2 días (sin chofer) y 3 días (COD sin cuadrar); son constantes del código, se pueden mover a un ajuste por compañía si se quiere.
