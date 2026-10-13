# Lote 43 — Impresoras y plantillas de label, impresión directa con QZ Tray (servidor de F-G1) (2026-10-12)

Servidor del bloque F-G1 del plan F-G (Impresoras y labels). La pantalla es el lote de frontend F-G1 (`docs/frontend/loteFG1-decisiones.md`).
Durante el lote Luis cambió dos cosas del plan aprobado y se aplicaron:
1. **Impresión directa con QZ Tray** (reemplaza D-FG1 «solo navegador»): se imprime directo a las impresoras locales de cada computadora
   con QZ Tray (gratis, código abierto, websocket a `localhost` desde la página); el PDF del navegador queda de respaldo. El servidor
   sigue sin conectarse a impresoras: solo firma con un certificado propio. Además: ajuste de compañía «Imprimir labels directo a la
   impresora» y bandera del cliente «Imprimir la factura automáticamente».
2. **El código de barras del label es el número de CADA paquete** y sale **un label por paquete** (reemplaza D-FG9 «empaque» y D-FG12).
   El diseño es el de la maqueta: **9 elementos** (no el 10.º «Piezas» del plan; «Paquete i de N» va debajo de las barras).

## Qué se construyó

- **SQL** (sección nueva e idempotente «2026-10-12 (d)» al final de `Diseño/logistica-db-update.sql`; las dos primeras tablas que crea ese
  archivo, con el `IF OBJECT_ID(...) IS NULL CREATE TABLE` en la misma línea por `FrozenSqlTests`):
  - `dbo.Printer` (nombre, nombre en el sistema `SystemName`, modelo, nota, `Purpose` LABEL|DOCUMENT, `ConnectionType` BROWSER|QZ_TRAY,
    `MediaSize` 4X6|4X4|4X2|LETTER con `CK_Printer_Media` según el uso, `RotateMedia`, `IsDefault`, `IsActive`, sellos y `RowVersion`);
    `UX_Printer_Name (TenantId, Name) WHERE IsActive = 1` y `UX_Printer_Default (TenantId, Purpose) WHERE IsDefault = 1 AND IsActive = 1`.
  - `dbo.LabelTemplate` (nombre, `LayoutJson NVARCHAR(2000)` canónico, predeterminada, activa); `UX_LabelTemplate_Name` y `UX_LabelTemplate_Default`.
  - `dbo.Tenant.PrintLabelsDirect BIT NOT NULL DEFAULT 0` y `dbo.Client.AutoPrintInvoice BIT NOT NULL DEFAULT 0`.
  - EntityType `PRINTER` (88) y `LABEL_TEMPLATE` (89); permiso `printers.manage` (ADMIN) y su propagación a la plantilla **y** a los roles
    `TenantAdmin` ya clonados de cada compañía (solo agrega).
- **Dominio**: `Printing/Printer.cs`, `LabelTemplate.cs` (`ITenantScoped`, baja lógica, sellos, `[AuditEntity]`), `PrintingRules.cs` (validaciones
  puras con los mensajes exactos), `LabelLayout.cs` (9 elementos, normalización, al menos un código de barras, JSON canónico); constantes
  `PrinterPurposes`, `PrinterConnections`, `PrintMediaSizes` y `EntityTypes.Printer/LabelTemplate` en `CatalogDomains.cs`; `PermissionCatalog.PrintersManage` (75 permisos).
- **EF**: `PrintingConfigurations.cs` (1:1 con el SQL, índices filtrados con el mismo nombre); DbSets `Printers`, `LabelTemplates`.
- **Servicios**: `PrinterService`, `LabelTemplateService` (alta, PATCH con `rowVersion`, desactivar/reactivar, hacer predeterminada en una
  transacción con dos `SaveChanges`), `PrintingOptionsService` (opciones de la estación y el ajuste de la compañía) y `QzSigningService`
  (certificado y firma SHA512withRSA desde `Printing:Qz`).
- **API** (`PrintingControllers.cs`, módulo SYSTEM): `/api/v1/printers`, `/api/v1/label-templates` (`printers.manage`),
  `/api/v1/printing/station-options` y `/api/v1/printing/qz/certificate|sign` (cualquiera de `orders.view|trips.scan|trips.view|inventory.view|printers.manage`),
  `/api/v1/printing/settings` (`printers.manage`). Cliente: `ClientDetailDto.AutoPrintInvoice` y `ClientProfileUpdateRequest.AutoPrintInvoice`.
- `openapi.json` y los `schema.d.ts` de la web y de la app regenerados (solo agrega).

## Brecha encontrada: el número de paquete en el escaneo (para F-G2)

- Cada línea de carga (`dbo.CargoLine`) es un paquete con su **`PackageNumber NVARCHAR(40)`** (PQT-##### del patrón del cliente si se deja en
  blanco, o tecleado) y su `Quantity` (piezas). El contador PACKAGE es **por cliente** (`NumberingRules.ScopeClientId`): dos clientes de la misma
  compañía pueden tener el mismo PQT-00012, y un número tecleado no se valida como único. No hay índice por `PackageNumber`.
- La estación de escaneo (`OutboundScanService` → `OrderReadService.LookupAsync`) busca **número de orden > empaque > factura**; **no** busca por
  número de paquete. Escanear el label de un paquete hoy daría «no encontrada».
- **Cambio mínimo propuesto para F-G2** (no se hizo aquí porque toca el flujo de escaneo ya cerrado y los labels de órdenes son de F-G2):
  agregar en `LookupAsync` una cuarta precedencia `PACKAGE` (orden con una `CargoLine` activa con ese `PackageNumber`), con su `MatchedBy` y la
  misma regla de ambigüedad (dos órdenes con el mismo número → ambigua, el escaneo ya lo trata), más el índice
  `IX_CargoLine_PackageNumber (PackageNumber) INCLUDE (TransportOrderId) WHERE IsActive = 1 AND PackageNumber IS NOT NULL` en una sección nueva
  del `update.sql`, y pruebas (lookup, escaneo y ambigüedad). Pendiente de decidir: si una línea con varias piezas lleva un label o uno por pieza
  con el mismo número (ver D-FG17).

## Cómo se probó

- `dotnet build` y `dotnet test` (3.659 pruebas en verde, **0 omitidas**: también las de aislamiento contra SQL Server real con
  `TEIKEM_TEST_SQLSERVER`, que recorren solas las entidades nuevas). Pruebas nuevas: `PrintingRulesTests`, `LabelLayoutTests`,
  `PrinterServiceTests` (InMemory con el filtro de tenant real), `PrintingSqlTests` (sección SQL, auditoría, índices del modelo EF, permiso),
  `PrintingControllerSecurityTests` (permiso y módulo exactos por reflexión) y `QzSigningServiceTests` (certificado autofirmado generado en la
  prueba: la firma se verifica con la clave pública; sin material, 409; clave dañada = sin configurar). Conteo de permisos 74 → 75 en las cinco
  pruebas que lo fijan. `clientRules.test.ts` cubre la bandera del cliente en la web.
- Base desechable propia `Teikem_fg1`: `db-init`, la sección (d) corrida **dos veces más** con `sqlcmd` sin errores (idempotente), `db-update`
  sin cambios pendientes; API en :5102 con `Auth__Onboarding__Enabled=false` (como el CI).
- `scripts/smoke.sh` **completo en verde (SMOKE OK, 144 pasos)** sobre una base nueva `Teikem_fg1b` (API :5102, `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` propios). Dos corridas anteriores se cortaron por causas ajenas al lote: `curl (26)` intermitente al subir un logo (paso del Lote 19; el aviso del propio script sobre `/tmp`; se corrió con `TMPDIR` propio) y correr el smoke dos veces sobre la misma base (feriado repetido). Incluye el paso nuevo «Impresoras y plantillas de label (Lote 43, F-G1)»: alta con QZ Tray 4X4 y nombre en el sistema,
  repetida 409, conexión/tamaño/uso 400 exactos, uso inmutable, predeterminada única, `station-options`, plantilla con construcción parcial,
  posición desconocida y sin código de barras 400, desactivar/reactivar, 409 al hacer predeterminada una inactiva, ajuste de la compañía,
  QZ sin certificado 409 y firma sin texto 400, bandera del cliente, despachador 403 al administrar y 200 en `station-options`, desactivar la
  predeterminada la deja sin predeterminada.

## Decisiones tomadas por default (para el informe de fin de P1)

| | Decisión | Alternativa |
|---|---|---|
| D-FG1 (revisada por Luis) | Directo con **QZ Tray** en cada computadora; respaldo PDF + diálogo del navegador; el servidor nunca se conecta a impresoras | Envío RAW 9100 desde el API (F-G3) |
| D-FG2 | Configuración de la estación solo en el navegador (localStorage por compañía) | Tabla `PrintStation` en el servidor |
| D-FG3 | Uso, conexión y tamaño como `VARCHAR` + `CHECK` | Dominios `LookupCode` |
| D-FG4 | `printers.manage` solo para TenantAdmin | También Despachador |
| D-FG5 | Sin fuente de datos de Análisis ni `StatusService` (solo `IsActive`) | Fuente `PRINTER` |
| D-FG6 | Desactivar la predeterminada la deja sin predeterminada; reactivar no la vuelve | 409 hasta elegir otra |
| D-FG7 | Documentos solo en Carta | Factura/BOL/Recibo cuando exista facturación |
| D-FG9 (Luis) | Código de barras = **número de cada paquete** | Número de empaque |
| D-FG12 (Luis) | **Un label por paquete** | Uno por orden |
| D-FG17 | Diseño de **9 elementos** de la maqueta; «Paquete i de N» debajo del código de barras. **Abierto para F-G2**: línea con varias piezas = un label (su número) o uno por pieza con el mismo número | 10.º elemento «Piezas» del plan |
| D-FG18 | Firma QZ con **un certificado propio de la plataforma** (`Printing:Qz`, variables de entorno o archivos), SHA512withRSA, texto ≤ 8192, permiso de la estación; sin material = 409 claro y la impresión sigue | Certificado por compañía; certificado comercial de QZ |
| D-FG19 | «Imprimir labels directo» en `Tenant.PrintLabelsDirect` con endpoint propio `PUT /printing/settings` (`printers.manage`), no en Ajustes de la compañía (`admin.tenant`) | Ponerlo en `PUT /tenant/settings` |
| D-FG20 | `SystemName` opcional (vacío = Nombre); la estación puede elegir el nombre detectado en su computadora | Obligatorio con QZ_TRAY |
| D-FG21 | `Client.AutoPrintInvoice` se edita en el PATCH del perfil (`clients.update`); solo se guarda | Endpoint propio |
| D-FG22 | El alta responde **200** (convención del API), no 201 como decía el plan | 201 con `Location` |
| D-FG23 | Se agregaron `GET /printers/{id}` y `GET /label-templates/{id}` (no estaban en el plan) | Sin lectura individual |

## Qué quedó fuera

- F-G2: labels desde Órdenes (al guardar con el ajuste de la compañía y a mano), waybill summary, packing list, informe de faltantes y el escaneo
  por número de paquete (arriba).
- Imprimir la factura (la bandera del cliente ya existe).
- Probar en una Zebra física con QZ Tray (le toca a Luis: instalar QZ Tray, copiar `qz-tray.js` a `web-app/public/vendor/` y el certificado).
