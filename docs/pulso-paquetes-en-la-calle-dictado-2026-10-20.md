# Pulso del día — «Paquetes en la calle» y «Almacén hoy»: dictado de Luis (2026-10-20)

> Solo anotado; **nada implementado todavía**. Toca la pantalla Pulso (otra sesión trabaja en Pulse: `PulsePanels.cs`, `AnalyticsService.cs`, `Pulse*.tsx`) y la migración de Colavora; coordinar antes de escribir.

## 1. Los cinco indicadores de «Paquetes en la calle», en este orden

| # | Indicador | Qué cuenta | Notas |
|---|---|---|---|
| 1 | **Entrada** | Órdenes **sometidas** (nuevas) | Se queda como está. |
| 2 | **En almacén** | Lo **escaneado que entró al almacén**: por recogido (pick up escaneado) **o** por el escaneo de enrutamiento en el almacén | **Sin duplicar**: una orden cuenta una vez aunque tenga los dos escaneos. **Debe coincidir con Entrada** (las sometidas entran al almacén). |
| 3 | **Rutas** (con barras de datos) | Cantidad de **rutas de entrega que salieron ese día** | Luis duda de que tenga sentido porque es casi constante; se deja y se revisa al verlo. Va **antes** de Entregado. |
| 4 | **Entregado** | Entregadas, **con histórico de todos los días** (barras con «tanto, tanto, tanto…») | **Ojo con la migración**: hoy el indicador parece hablar solo de «hoy» y las barras salen casi vacías; con la migración debe verse entrega diaria en todos los días. |
| 5 | **Lo que pasa ahora mismo** | **En ruta** (grande) y, abajo y más pequeño, **Completada** | Sustituye al «En ruta» actual. |

Detalles de pantalla:
- «Almacén hoy» → la tarjeta **«Productos bajo mínimo»** no tiene nada en la parte de abajo: ponerle **«Productos en backorder»** para llenar ese espacio.
- Sobre el enlace de la última tarjeta de cada fila (dictado en dos sentidos): Luis dijo «olvídate de eso», **no se cambia nada** con el enlace.

## 2. Lo que debe tomar en cuenta la migración y la fuente de datos

- **En almacén** se alimenta del escaneo por recogido y del escaneo de enrutamiento en el almacén; la migración debe generar esos escaneos/eventos (o la fuente debe poder derivarlos del historial de estatus) **y la fuente no puede contarlos dos veces**.
- **Entregado**: la migración debe dejar fechas de entrega reales por orden (`deliveredAt` del historial) para que las barras diarias tengan datos todos los días.
- **Rutas del día**: viene de los viajes (`Trip`, uno por manifiesto `M-<manifiesto>`); verificar que cada viaje migrado tenga su fecha de salida.

## 3. «4,238 envíos sin chofer… la más antigua 92 días» — hallazgo

Luis tiene razón en que no tiene sentido (son órdenes viejas que ya se entregaron). Lo que muestran los datos (consulta de solo lectura, 2026-10-20):

- En la base local hay 4,235 órdenes de Advance Logistics y **30,837 de De La Paz** en estatus **Asignada** (todas de jul–oct 2026, repartidas por mes), más 772 «Sin asignar». El panel suma las que no tienen ruta con chofer.
- **No es un error de la migración: Colavora mismo las tiene así.** En Colavora, desde 2026-07-01, el estatus actual es `Assigned` en 4,277 órdenes de Advance y **31,421 de De La Paz** (frente a 47,135 `Completed` en De La Paz). De esas 31,421 de De La Paz: **0 tienen comprobante de entrega (POD) y 0 tienen evento de entrega**. Es decir, en Colavora nunca se cerraron: o se entregaron sin registrarlo, o De La Paz las cierra en lote (Completed) y estas quedaron atrás.
- La migración respeta el estatus de Colavora (`Assigned` → Asignada). Solo cierra solas las `Pending` de más de 5 días (D3).

**Decisión pendiente de Luis:** ¿aplicar a las `Assigned` viejas la misma regla que a las `Pending` (D3: si pasaron N días sin movimiento se migran como Entregada)? Si sí, ¿cuántos días (¿5, 10, 30?) y se marca de alguna forma que se cerraron por la migración? Mientras tanto, el panel «Por despachar» (que cuenta las asignadas sin ruta) estará inflado por esas órdenes. Además, a futuro el panel debería **excluir lo que tenga más de X días sin movimiento** o mostrarlo aparte como «atascadas».
