# Lote 87 — Pestaña Remesas del COD (P8, 4 de 4) (2026-10-17)

Cierra P8 (salvo lo pendiente): la web de la remesa al cliente del Lote 86.

## Qué se construyó
- **Pestaña «Remesas»** en `/money/cod`: lista (buscador, filtro de estatus, exportación completa), comisión por omisión de la compañía (editable con `cod.remit`), **Nueva remesa** (cliente, período y comisión opcional; los cobros que entrarían, los totales y de dónde sale la comisión, antes de crear), **ficha** con el flujo cuadrar → aprobar (la reautenticación AAL2 la abre sola el cliente del API) → remitir (con la referencia del pago), cancelar con confirmación y **exportar** con plantilla (con permisos de facturación se elige; sin ellos, la estándar).
- `openapi.json` y el cliente generado (9 rutas y 7 esquemas nuevos de remesas y ajustes); textos es/en; reglas puras en `remittanceRules.ts`; hooks en `codApi.ts`; `RemittanceParts.tsx`.
- Manual de pantallas L87 con 5 capturas, capítulo 21, FAQ y este documento.

## Cómo se probó
- vitest: reglas (10) y pantalla con fetch simulado (8): lista y filtro, nueva remesa (candidatos, comisión inválida y propia, cuerpo), mensaje del servidor sin cobros, flujo cuadrar → aprobar → remitir con la referencia, cancelar (y una aprobada ya no se cancela), exportar con plantilla y sin permisos de facturación, comisión por omisión con y sin permiso. `npm run check` completo (3022 pruebas).
- Playwright contra el API real (SQL Server), escritorio y móvil (360 px, sin scroll horizontal): cliente con dos órdenes cobradas y cuadradas por API; nueva remesa (3 cobros, 150 / 12 / 138), crear, cuadrar, aprobar con reautenticación, **descargar el archivo**, remitir con «CHK-9001», remesa Remitida y el COD de las órdenes Remitido en la lista.

## Decisiones a revisar con Luis
1. **Sin edición de líneas**: una remesa mal armada se cancela y se vuelve a crear (más simple y auditable).
2. **El campo de comisión del contrato sigue sin pantalla**: la remesa ya usa `Contract.CodCommissionPct` si viene; falta el campo en la pantalla de contratos (el diseño lo había quitado por confundirse con el cargo por COD de la facturación). Propuesta: agregarlo en un lote chico de contratos con el nombre «Comisión de remesa COD (%)».
3. **Plantillas de remesa**: el administrador de plantillas (columnas, formato) sigue siendo el de facturación y solo con alcance de facturación; un selector de alcance en esa pantalla es el siguiente paso si se quiere personalizar el archivo de remesas.
4. Los **indicadores de COD** (pendiente, cobrado, remitido) del módulo de dashboards quedan para el lote de dashboards (P12): las órdenes, los cobros y las remesas ya traen los datos.

## Pendiente de P8
- Anular un cobro; cobro desde el POD de la app de choferes (P7); una hoja real de muestra para afinar la lectura del código de barras; comisión del contrato en pantalla.
