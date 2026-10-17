# Lote 86 — Remesa COD al cliente (P8, 3 de 4) (2026-10-17)

Tercer lote de P8 (servidor): el dinero cobrado y cuadrado se devuelve al cliente menos la comisión. Sigue el **Lote 87**: la pantalla web de remesas.

## Qué se construyó
- Entidades `CodRemittanceBatch` y `CodRemittanceLine` (las tablas ya existían en el esquema original; `NetRemitted`/`NetAmount` son columnas calculadas) y la sección **2026-10-17 (y)** de `update.sql`: comisión por omisión de la compañía (`Tenant.CodDefaultCommissionPct`, 8 %), EntityType `COD_REMITTANCE` y contador `CODREMIT` (REM-#####, también en la lista de tipos conocidos de `NumberingRules`).
- `CodRemittanceService` y `CodRemittancesController` (`/api/v1/cod/remittances`): lista y ficha, **candidatos** (vista previa sin crear), **crear**, **cuadrar**, **aprobar** (AAL2), **remitir**, **cancelar** y **exportar**; ajustes `GET/PUT /cod/settings`.
- **Comisión:** la pedida, si no la del contrato activo del cliente (`Contract.CodCommissionPct`, que ya existía sin usarse) y, si no, la de la compañía; por línea monto × % redondeado a 2 decimales; neto = bruto − comisión.
- **Remitir** pasa los cobros a REMITIDO y, si todos los cobros de una orden se remitieron, su COD a REMITIDO (StatusService, historial ORDER_COD).
- **Exportación con plantillas:** nuevo alcance `COD_REMITTANCE` del motor del Lote 77 con sus 19 campos (una fila por cobro); sin plantilla se usa «Formato estándar», que se crea sola.
- Manual 21 §6 (flujo, comisión, exportación, mensajes exactos y soporte), FAQ y este documento.

## Cómo se probó
- xunit (`CodRemittanceTests`, 7): reglas; crear (solo cobros cuadrados del cliente, bruto/comisión/neto por línea, cada cobro en una sola remesa, período sin cobros); comisión pedida > contrato > compañía y sus validaciones; flujo completo con los mensajes de cada paso en falso, remitir cierra el COD de las órdenes y el historial; cancelar libera los cobros (REM-00002 con otro %); exportación (una fila por cobro, comisión y neto) y filtros de la lista, otra compañía sin acceso; alcance de plantillas, permisos y AAL2 del controlador. Suite completa: 4142 pruebas verdes.
- Recorrido por la API contra **SQL Server real**: dos órdenes cuadradas (3 cobros: 60 en efectivo, 40 con cheque y 50), candidatos (150 / 12 / 138), remesa REM-0000N abierta, aprobar sin reautenticación (403) y con ella, archivo CSV con el «Formato estándar», remitir, órdenes en REMITIDO, y cancelar una remesa libera sus cobros. Encontró un error que las pruebas en memoria no ven: el contador `CODREMIT` faltaba en la lista de tipos conocidos del servicio de numeración (corregido).
- Hallazgo de las pruebas en memoria: `StatusService.TransitionAsync` suelta lo rastreado de EF; el servicio vuelve a cargar lo que cambia después de cada transición (en SQL real no hace falta, pero así se prueba igual).

## Decisiones a revisar con Luis
1. **Período por fecha de cobro**, en la zona horaria de la compañía (no por fecha de cuadre).
2. **8 % por omisión de la compañía** (el mockup decía «8 % fijo del tenant»); el contrato manda si trae `CodCommissionPct`. **Falta un campo en la pantalla de contratos** para editarlo (el diseño había quitado el viejo por confundirse con el cargo por COD de la facturación); se decide cuando se arme la pantalla de remesas.
3. **Cancelar** solo abierta o cuadrada y desactiva la remesa (no hay estatus «cancelada» en el dominio); aprobada o remitida ya no se cancela.
4. **Aprobar con reautenticación (AAL2)**, como las corridas de facturación.
5. **No se anula todavía un cobro** ni hay ajustes manuales por línea de la remesa (se cancela y se vuelve a crear).
6. Las plantillas de remesa se administran con los permisos de facturación (el administrador de plantillas es el de la facturación); un rol con solo `cod.remit` exporta con la plantilla estándar.

## Sigue (P8)
- **Lote 87:** pestaña **Remesas** en la pantalla COD (candidatos, crear con período y comisión, ficha, cuadrar, aprobar, remitir, cancelar, exportar con plantilla), ajuste de la comisión por omisión, y los indicadores de COD (pendiente, cobrado, remitido).
- Pendientes de P8: anular un cobro; cobro desde el POD de la app de choferes (P7); una hoja real de muestra.
