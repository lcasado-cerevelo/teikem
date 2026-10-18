# Lote 110 — Número de la orden nuevo y escaneo por pieza (2026-10-18)

Luis dictó: el número de la orden se arma como el de la pieza pero con un secuencial del día por consignatario en vez de la hora; lo que se escanea son las piezas (el número de la orden «no se encuentra»); si no se escanearon todas las piezas en un estatus, la orden está incompleta, y la pieza que falta lo es cuando la orden pasa a la siguiente acción sin ella o al cerrar el conteo; el audio es Sí / No / Ok.

## Qué se construyó
- **Conduce nuevo** (`WaybillCodes.DayPrefix` / `Next`): `{Code cliente}{Code consignatario o su ID}{aammdd}{secuencial del día del consignatario}`; el siguiente secuencial sale de los conduces que ya empiezan con ese prefijo (el índice único `UX_Order_Waybill` cubre la carrera: la colisión se reintenta). Ya no usa el contador `WAYBILL`; las órdenes anteriores conservan su `CND-#######`.
- **Escaneo por pieza** (`ScanActionService`, update.sql (ar): tabla `OrderPieceScan` y columnas `OrderPiece.MissingStatus/MissingAtUtc`): se busca la pieza por su número (el número de orden/empaque/factura da «No se encontró la pieza.»); una fila por pieza y acción; el primer escaneo de una acción mueve la orden (con los estatus brincados de siempre), los siguientes cuentan (`COUNTED`), repetir una pieza es `DUPLICATE`; la respuesta trae `scanned`/`total`/`missing` y la palabra (Sí / No / Ok).
- **Faltantes «Por verificar»:** al pasar la orden a la acción siguiente, las piezas que no estuvieron en la acción en que iba (ni en una posterior) quedan `TO_VERIFY`; **«Cerrar conteo»** (`POST /api/v1/scan/close-count`) lo hace sin mover la orden; una pieza que aparece deja de ser faltante. Enrutar sin ruta deshace todo (no registra la pieza ni deja piezas marcadas).
- **Web:** la estación muestra «N de M piezas», los faltantes y el botón «Cerrar conteo»; el audio de «duplicado» pasó de «Ya» a «Ok»; el texto de ayuda dice que se escanea el número de cada pieza.

## Cómo se probó
- xunit `ScanActionServiceTests` (15, reescrito para piezas) y `OrderSubmissionLifecycleTests` (conduce); suite completa. vitest de la estación (+1). Playwright FE1 (escritorio y móvil), F-G2, y los recorridos de órdenes.
- Contra SQL real: una orden de 5 piezas: el conduce no se encuentra, 4 piezas «Llegó» (1/5 … 4/5), duplicado, «Salir» con la primera marca como faltante a la 5.ª que nunca llegó, «Cerrar conteo» y la pieza que aparece se cuenta.

## Decisiones a revisar con Luis
1. **Con «Cerrar conteo» después de «Salir» con una sola pieza escaneada**, las otras 4 también quedan «Por verificar» (no se escanearon en «Salir»): es la regla que dictaste aplicada al pie de la letra; si prefieres que cerrar el conteo solo marque lo que faltó respecto de la acción anterior, se cambia.
2. **Piezas que aparecen después de estar «Perdido» o «Reclamada»** (la orden nueva al cerrar el conteo, el aviso sin detenerse en la app móvil): pertenecen a las reclamaciones (lote 111); hoy solo existe «Por verificar».
3. **La página pública y el portal** buscan por conduce: aceptan el formato nuevo (hasta 40 caracteres). El enlace con token propio llega con los avisos al consignatario (diseño `docs/p12-avisos-consignatario-diseno.md`).
