# Lote 90 — Fotos diarias de indicadores (P12, 3) (2026-10-17)

Tercer lote de P12 (plan en [lote88-decisiones.md](lote88-decisiones.md)): historial de indicadores para las barritas del Pulso y los gráficos. Siguen 91 (indicadores/gráficos de COD, facturación y ventas + Actividad) y 92 (informes por correo).

## Qué se construyó
- **Datos:** la tabla `KpiDailySnapshot` ya existía en el DDL original; se mapea (`KpiSnapshotConfiguration`) y `update.sql` (aa) siembra las 10 claves `KpiKey` (órdenes y COD).
- **Servicios:** `KpiSnapshotService` (calcula con los ríos y el Radar, guarda/reemplaza la foto de hoy, historial) y `KpiHistory` (serie de N días). Solo guarda los módulos encendidos.
- **Trabajo** `KPI_SNAPSHOT` (`KpiSnapshotJob`): diario 23:55, habilitado.
- **Ríos:** `PulseRiverService` rellena `spark` con el historial en En almacén, Por despachar, En ruta y los 4 nodos COD (6 días previos + hoy en vivo).
- **Fuente `KPI_DAILY`** (`analytics.view`) para indicadores y gráficos con rango de fechas.
- **Web:** `RiversPanel` dibuja las barritas del dinero en naranja y formato de dinero.
- Manual 07 §8, cap. 13, FAQ y este documento.

## Cómo se probó
- xunit `KpiSnapshotTests` (6): reglas, captura y reemplazo, módulos encendidos, historial + barritas de los ríos, fuente `KPI_DAILY` (rango, tenant, permiso) y el trabajo. Suite completa y `npm run check`.

## Decisiones a revisar con Luis
1. **Hora 23:55** para quedarse con el cierre del día (si el servidor está apagado ese día no hay foto; cuenta 0).
2. **Sin dimensiones todavía** (por cliente/chofer): el esquema ya las trae, no se llenan.
3. **Sin relleno retroactivo:** el historial empieza el día que corre el trabajo.
4. **Día sin foto = 0** en las barritas.
