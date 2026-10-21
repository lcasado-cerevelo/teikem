# Lote 159 — Pulido de pantallas de almacén (2026-10-21)

## Qué se hizo
- **Reparto en varias posiciones (Acomodo):** el selector «Agregar posición» salía como una caja diminuta porque la clase `.split` del editor chocaba con la del panel redimensionable (3 columnas); el editor usa ahora `.split.binsplit` (una columna).
- **Recibo:** «Asignar todas», «Iniciar todas» y «Completar todas» a nivel del recibo (sobre sus tareas de acomodo; completar usa la posición sugerida de cada una y avisa cuáles fallaron; las que necesitan repartirse o llevar series se completan aparte).
- **Ajustes → Operación → Rotación:** los seis ajustes en dos columnas parejas (sin dividir entradas/salidas); cada fila alinea sus campos por arriba.
- **Avisos de salida y de llegada:** barra de filtros arriba (como Llegadas) con filtro de **estatus de selección múltiple con buscador** en ambos; avisos de salida también cliente, fechas y «solo con pendiente». El servidor acepta varios estatus separados por coma.
- **Despachar contra un aviso:** tabla (producto, lote, reservado, cantidad a despachar) en vez de campos sueltos.
- **Kárdex:** filtro de **zona** (`zoneIds`) entre almacén y posición, y barra pareja de columnas iguales.
- Textos que salían como clave (`common.close`, plantilla de importación de consignatarios) ya tienen traducción.

## Cómo se probó
Suite .NET completa contra SQL Server real (4987) y `npm run check` (3294).

## Decisiones a revisar
- «Completar todas» no reparte ni captura series: pensado para el caso común (una posición sugerida por tarea).
- El filtro de zona solo aplica al Kárdex.
- Pendiente de opinión del dueño: color de encabezado de las tarjetas (ver respuesta en la conversación).
