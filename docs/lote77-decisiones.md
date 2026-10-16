# Lote 77 — Plantillas de exportación a contabilidad (P5, 6 de 7) (2026-10-16)

Sexto lote de la facturación (el servidor del motor de plantillas). Sigue el **78**: la pantalla web (selector de plantilla, «Configurar…», vista previa y descarga). Después: la contabilización de compras y de despachos y la liquidación de choferes con el mismo motor, y el cargo por intento de entrega (P7).

## Qué se construyó
- **Motor puro** (`Domain/Export/ExportTemplates.cs`): `ExportEngine.Render` arma un CSV o TXT con las columnas en su orden, encabezado opcional, delimitador (coma, punto y coma, tabulador, barra vertical) y formato de fecha (`YYYY-MM-DD`, `MM/DD/YYYY`, `DD/MM/YYYY`); dinero a 2 decimales, números a hasta 4 sin ceros de más, CRLF, comillas en CSV y espacios en TXT. Determinista (reproducible). El catálogo de campos por alcance (`ExportFields`); hoy el de Facturación (23 campos); un alcance nuevo (compras, despachos, liquidación) es una lista más.
- **Plantillas** (`ExportTemplate`, update.sql sección **(u)**): por compañía y alcance, nombre único entre las activas (sin mayúsculas ni acentos), se desactivan (no se borran) y siempre queda una; la primera vez la compañía recibe «Formato estándar». `ExportTemplateService` y `/api/v1/export-templates` (lista, campos, ficha, alta, cambio parcial, desactivar).
- **Archivo de la corrida** (`BillingExportService`): `GET runs/{id}/export-preview` (sin cambiar nada) y `POST runs/{id}/export` (arma el archivo y pasa la corrida de Aprobada a Exportada con quién y cuándo; una exportada se puede volver a exportar). Una fila por línea de factura, las anuladas fuera.
- Permisos: ver plantillas con `billing.view`; crear, cambiar, desactivar y exportar con `billing.export` (ya sembrado). Sin permisos nuevos.
- Manual (capítulo 19 §4c y mensajes), FAQ y README.

## Cómo se probó
- xunit (12 nuevas): el motor (encabezado, orden, dinero, delimitadores, TXT, comillas, fechas, números, determinismo y columnas desconocidas), nombres sin mayúsculas ni acentos y las columnas en JSON; plantilla estándar por compañía; alta, cambio y desactivar con todas sus validaciones; aislamiento por compañía; el archivo (exige aprobada, la vista previa no cambia el estatus, exportar la marca una vez, reproducible, una fila por línea con el formato de la plantilla, las anuladas no salen, plantilla de otra compañía rechazada). Suite completa contra SQL Server real: 4158 pasan; el SQL corrió dos veces sin error.
- **De punta a punta contra SQL Server real vía API:** factura emitida (AAL2) → plantilla nueva TXT con tabulador y fechas `MM/DD/YYYY` con encabezado propio → vista previa (la corrida sigue Aprobada) → exportar (queda Exportada con su fecha) → mismo texto → cambiar y desactivar la plantilla; el nombre repetido da 409.

## Decisiones a revisar con Luis
1. **Formato de QuickBooks:** como se acordó (D8), el formato es genérico y configurable; el ajuste al exacto de QuickBooks se hace con una muestra real, solo cambiando la plantilla (columnas, orden, encabezados, fecha).
2. **Una fila por línea de factura**, con los datos de la factura repetidos. Si contabilidad necesita una fila por factura (o encabezado y líneas en registros distintos, como los IIF), hace falta otro «tipo de archivo» además de CSV/TXT.
3. **Encabezados por omisión en inglés** (`InvoiceNo`, `Customer`…), como suelen pedir los programas de contabilidad; cada columna admite su propio encabezado.
4. **Cualquiera con `billing.export` administra las plantillas** (no hay un permiso aparte para configurarlas).
5. Las plantillas se comparten por toda la compañía (no por usuario).

## Fuera de este lote
La pantalla (78), los alcances de compras, despachos y liquidación, y un documento imprimible de la factura.
