# Lote 109 — Piezas con número propio (2026-10-18)

Luis dictó que **cada pieza (paquete) tiene su propio número único**, distinto al de la orden (reemplaza el supuesto del Lote 44 de repetir el número de la línea en cada pieza), generado por Teikem con los **Codes** del cliente y del consignatario.

## Qué se construyó
- **Entidad `OrderPiece`** (update.sql (aq), `UX_OrderPiece_Number` único por compañía mientras esté activa): una fila por pieza, creada al someter la orden y reemplazada al editar los paquetes de una orden Sometida.
- **Numeración** (`PieceNumbering`): `{Code cliente}{Code consignatario o su ID}{aammdd}{hhmmssfff}{n}` con la fecha y hora de la compañía; Codes tal cual vengan (con rayas si las traen), primeros 10 caracteres si uno ya guardado es más largo; formato propio del cliente (contador) si lo tiene; línea con número tecleado numera sus piezas con ese número (más el de la pieza si hay varias).
- **Límite de 10 caracteres** para los Codes de cliente (también los autogenerados) y de consignatario (alta, edición e importación).
- **Lectura:** la ficha y la lista traen `pieceNumbers` por línea; buscar o escanear el número de una pieza encuentra su orden; los labels (uno por pieza, cada uno con SU número en el código de barras) y la hoja de empaque usan los números de las piezas.

## Cómo se probó
- xunit (+6 en `OrderSubmissionLifecycleTests`: línea de 3 piezas, secuencia sobre varias líneas, número tecleado, edición que reemplaza, búsqueda por pieza, reglas del número; `ClientRulesTests` ajustado a 10 caracteres) y suite completa; vitest (labels por pieza); Playwright FC1, FC2, FC3 y F-G2 (labels y escaneo por número de pieza).
- Contra SQL real: una orden con 3 + 1 piezas (una con número tecleado) crea números únicos de 30 caracteres, la búsqueda por número de pieza la encuentra y un escaneo «Llegó» por número de pieza la mueve.
- Código de barras: 38 caracteres (peor caso) son unos 475 módulos; en 4 pulgadas útiles quedan a unos 8 mil de módulo, legible por cualquier lector (con dígitos seguidos el Code 128 los compacta aún más).

## Decisiones a revisar con Luis
1. Los **Codes con rayas o espacios** salen tal cual dentro del número (decidido por Luis).
2. **Clientes ya migrados con Codes de más de 10 caracteres:** siguen funcionando (se usan sus primeros 10 en el número); conviene corregirlos.
3. Un **cambio de consignatario** en una orden Sometida **no** renumera sus piezas (los números conservan el consignatario de cuando se sometió); solo reemplazar los paquetes las renumera.
4. Falta (110): el número de la **orden** con el mismo estilo, el **escaneo por pieza** con conteo e incompleta, y el audio Sí/No/Ok.
