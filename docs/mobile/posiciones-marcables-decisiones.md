# App: sugerida cliqueable y listado de posiciones marcables (2026-10-07)

## Qué se construyó
- Campos numéricos con el valor seleccionado al enfocar (`KeyboardInput`), línea «cantidad × SKU».
- `features/positions` (`binMarks.ts`, `BinMarkList.tsx`): reglas puras de marcado y listado reutilizable. Integrado en Despacho (reemplaza el «Plan de salida» con Cambiar/escanear, unido al listado) y en Recibo directo (posiciones de `putaway-suggestions` con `freeQty`).
- La tarjeta «Sugerida» es un botón: llena el campo de la posición (prefill) y enciende Aceptar.

## Cómo se probó
jest (513): reglas de marcado, Despacho (marcar sugeridas, marcar a mano, recorte por cantidad, sugerida→Aceptar, falta de existencia), Recibo directo (60 en 30+30, sugerida→Aceptar), selección al enfocar. Sin probar en el Zebra.

## Decisiones a revisar
- Se pidió: unir con el plan de salida, no pasarse del total, lote = una sola posición (se mantuvo).
- En Recibo el tope de cada marca es el espacio libre de la posición (si hay cupo); el aviso de cupo sin bloqueo sigue existiendo al escanear a mano.
- El listado solo aparece si no cabe en la sugerida o con «Ver otras posiciones».
- El antiguo cambio de una posición del plan escaneando otra se retiró (se marca otra en el listado).
