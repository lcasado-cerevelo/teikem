# Lote 158 — Error al guardar el aviso de salida, disponible en el selector y edición del cliente (2026-10-20)

## Qué se arregló / construyó
- **Error interno al guardar un aviso de salida** (reproducido con SQL Server real): `NumberingRules.IsKnownKind` no conocía el contador `EXITNOTICE` (SAL-#####); el servicio real lo rechazaba con `ArgumentOutOfRange` → 500. Las pruebas no lo vieron porque usan un contador falso. La restricción SQL ya traía `EXITNOTICE`. Corregido y con prueba.
- El selector de producto del aviso de salida (web y app del cliente) muestra entre paréntesis lo disponible.
- Editar: `PATCH /exit-notices/{publicId}` (datos del aviso solicitado) y, en el portal, `PATCH` de mi aviso de llegada, de los datos de mi aviso de salida y de la cantidad de una línea; botón «Editar» en la app.

## Cómo se probó
SQL Server 2022 local: `db-init` completo y llamadas reales (aviso de salida, edición, aviso de llegada con editar/cancelar y sus avisos en la cola); pruebas de servicio; suites completas.

## Decisiones a revisar
- Ninguna referencia/destino/notas es obligatoria (el servidor ya no las exige); solo almacén, cliente y al menos una línea.
- Lección: las pruebas de servicios que piden contadores deben correr contra el contador real al menos una vez (se corrió a mano aquí).

## Fuera de este lote
El `smoke.sh` completo no se corrió de punta a punta en este entorno (falla antes en un estatus de órdenes; pendiente de revisar si ya fallaba).
