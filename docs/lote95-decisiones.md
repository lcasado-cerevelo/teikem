# Lote 95 — Exportar la liquidación a choferes (P9, 3 de 4) (2026-10-17)

Tercer lote de P9: la exportación con plantillas. Falta la pantalla (lote 96).

## Qué se construyó
- Alcance `DRIVER_SETTLEMENT` en el motor de plantillas de exportación (`ExportCodes`, `ExportFields.DriverSettlement`, 19 campos), con «Formato estándar» automático.
- `DriverSettlementService.PreviewExportAsync` / `ExportAsync` (una fila por línea; exportar pasa una aprobada a Exportada con quién y cuándo; se puede repetir) y los endpoints `export-preview` y `export` (`driverpay.export`).
- Las plantillas se leen con `billing.view`, `cod.view` o `driverpay.view` (antes solo `billing.view`); se cambian con `billing.export`.
- Manual 22 §3, FAQ y este documento.

## Cómo se probó
- xunit (`DriverSettlementTests`, 12): el estado exigido, la vista previa sin cambiar estatus, el CSV (encabezado, fila, motivo entrecomillado, monto ajustado), exportar pasa a Exportada y se puede repetir. Suite completa verde.

## Decisiones a revisar con Luis
1. **Las plantillas siguen en el módulo Facturación** (el controlador de plantillas exige el módulo `BILLING`): una compañía sin Facturación no podría configurar plantillas de liquidación. Si hace falta, se separa.
2. **Quien exporta no necesariamente configura** plantillas: cambiarlas sigue siendo `billing.export`.
3. Formato definitivo para nómina: como en Facturación, se ajusta con un archivo de muestra real.
