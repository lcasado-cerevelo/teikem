# Lote 47 — Duplicados de consignatarios en la importación: número y nombre, sin dirección (2026-10-14)

Decisión de Luis: dos consignatarios con el mismo nombre y direcciones distintas confunden al capturar una orden («¿cuál nombre voy a escoger?»); hay que distinguirlos por el nombre o por el número. La dirección deja de contar como criterio de duplicado.

## Qué cambió (solo la importación; sin SQL)

| Regla | Antes | Ahora |
|---|---|---|
| Número (código) | repetido en el archivo o ya usado (activo → error; inactivo → error de reactivar) | **igual** |
| Nombre en el archivo | nombre + línea 1 repetidos → error | **nombre repetido** (mismo dueño, sin importar tildes, mayúsculas ni espacios dobles) → error: «El nombre 'X' se repite en la fila N del archivo; agregue un distintivo al nombre (o al número).» |
| Nombre contra el directorio | nombre + línea 1 activo → error; inactivo → aviso; mismo nombre con otra línea 1 → aviso «sucursal distinta» | **nombre** de un consignatario activo del mismo dueño → error «Ya existe un consignatario activo con el mismo nombre. Agregue un distintivo al nombre (o al número) para distinguirlos.»; solo inactivo → error «…inactivo con el mismo nombre; reactívelo en vez de importarlo, o agregue un distintivo…». Desaparece el aviso de sucursal distinta. |

Dónde: `ConsigneeImportMapping` (`FindDuplicatesInFile`, `ConsigneeDirectory.Evaluate`). El «mismo dueño» sigue siendo exacto (un cliente contra los suyos; los compartidos entre sí). La coincidencia por nombre + línea 1 de la importación de **órdenes** (`ConsigneeMatch.Key`) no se toca.

## Pruebas (solo el área)
dotnet filtrado a consignatarios, ubicaciones e importación: 469 en verde (reglas de duplicados con el mismo nombre y otra dirección, inactivo, repetido en el archivo con la fila de la primera aparición, otro dueño no choca; vista previa del servicio). vitest `ConsigneeImport.test.tsx` 28. Manual (§2.1 del capítulo 02, fb-consignatarios), FAQ y `scripts/smoke.sh` al día.

## Para revisar
- **El alta y la edición a mano de un consignatario NO aplican esta regla** (solo la importación). Si Luis quiere que tampoco se pueda crear a mano otro con el mismo nombre (o número) para el mismo dueño, es un cambio aparte en `LocationService` (y habría que decidir qué pasa con los duplicados que ya existan en la base).
- Consignatarios que ya existen repetidos en la base siguen ahí; la regla solo rechaza filas nuevas de la importación.
