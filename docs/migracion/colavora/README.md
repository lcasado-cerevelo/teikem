# Migración de Colavora Courier → Teikem

Carpeta de la migración de las bases de **Colavora Courier** (`colavora_courier` y `colavora_courier_login` en `172.31.53.133`)
a Teikem. Es una migración **aparte** del importador de almacén de Advance Depot (`import-legacy`, `docs/migracion/`): tiene su
propio verbo, su configuración, su reporte y su código (`src/Teikem.Infrastructure/Migration/Colavora/`, reglas en
`src/Teikem.Domain/Migration/Colavora/`).

Estado 2026-10-18: análisis hecho y decidido (`analisis.md` §7), scripts escritos. **No se ha corrido nada contra producción**
(decisión D15: una sola corrida el día del corte; Luis la corre).

| Archivo | Para qué |
|---|---|
| [`analisis.md`](analisis.md) | Inventario real de la fuente, mapeo hacia Teikem y las 15 decisiones de Luis (§7). |
| [`import.advance.json`](import.advance.json) | Configuración de **Advance Logistics** (CompanyID 10029). |
| [`import.delapaz.json`](import.delapaz.json) | Configuración de **De La Paz Transport** (CompanyID 10030). |
| [`../../../scripts/crear-variable-colavora.ps1`](../../../scripts/crear-variable-colavora.ps1) | Guarda las credenciales de solo lectura como variables de entorno de usuario `ConnectionStrings__ColavoraCourier` y `ConnectionStrings__ColavoraCourierLogin` (interactivo, o `-DesdeColavoraApi` para tomarlas del `appsettings.json` del API de Colavora). La contraseña nunca queda en el repo. |
| [`../../../scripts/migrar-colavora.ps1`](../../../scripts/migrar-colavora.ps1) | Corre el verbo `import-colavora` para una compañía (`-Compania advance|delapaz`, `-DryRun`, `-Solo`, `-Desde`, `-Hasta`). |

## El comando

```
dotnet run --project src/Teikem.Api -- import-colavora <config.json> [--dry-run] [--solo fases] [--desde <ShipmentID>] [--hasta <ShipmentID>]
```

| Opción | Qué hace |
|---|---|
| `--dry-run` | Lee todo, cuenta lo que se crearía y escribe el reporte; **no toca la base de Teikem**. |
| `--solo catalogos,usuarios,zonas,clientes,consignatarios,contratos,ordenes` | Solo esas fases (las demás se leen de lo que ya está en Teikem). La compañía y los catálogos van siempre. |
| `--desde N` / `--hasta N` | Rango de `ShipmentID` de las órdenes (para reanudar o para probar con pocas). |
| `--meses N` / `--desde-fecha aaaa-mm-dd` | Para pruebas: solo las órdenes creadas en los últimos N meses o desde esa fecha (los maestros van completos). En el script: `-Meses 3` / `-DesdeFecha 2026-01-01`. |

| Código de salida | Significado |
|---|---|
| `0` | Terminó sin rechazos graves (puede haber rechazos por fila y advertencias: revise el reporte). |
| `1` | Rechazo grave (compañía que no se pudo aprovisionar, configuración, fuente inaccesible). |
| `2` | Sintaxis incorrecta. |

El reporte (`.md` + `.csv` de resumen, rechazos y advertencias) queda en `report.outputDir` (fuera del repositorio:
`F:\Download\TeikemMigracion\colavora\reportes`). La contraseña inicial del administrador viene de la variable
`TEIKEM_IMPORT_ADMIN_PASSWORD` (si no, se genera una temporal que no se muestra).

## Qué hace cada fase (según las decisiones de Luis)

1. **Compañía**: tenant nuevo por compañía de Colavora (D1), con los módulos del JSON y el administrador.
2. **Catálogos** (D14, por compañía): tipos de servicio (NEXT DAY → `STANDARD`, SAME DAY → `SAMEDAY`; el resto se crea para la
   compañía con la marca de pick up cuando aplica), tipos de paquete (Box/Envelope/Pallet globales; el resto por compañía),
   tipos de COD y las razones de entrega como `DeliveryFailureReason`.
3. **Usuarios y choferes** (D11): `SecGroup` 4 = chofer → `Driver` con su código (`812`, `2006`) y usuario por invitación si su
   username es correo (o con `options.driverEmailDomain`); el staff → usuario por invitación (`SecAdmin` → TenantAdmin, resto
   Dispatcher). Activos e inactivos; los inactivos quedan suspendidos. No se crean usuarios de portal.
4. **Zonas** (D12 + Lote 129): los niveles `METRO` e `ISLA` (de `Zip.Zone`, con el chofer de entrega más frecuente como chofer por
   omisión) y, debajo, las zonas de despacho de ruta por ZIP (01–15 en De La Paz, códigos de chofer en Advance) con sus ZIP, su
   chofer y el nivel mayoritario de sus ZIP. Cada orden migrada guarda su zona de entrega por el ZIP del consignatario.
5. **Clientes**: uno por `Customers` (sin los de prueba: `-DEV`, `-PCK`, `SAMPLE:` y `options.excludeSenders`), con su dirección de
   recogido por defecto, teléfono, fax y correo de avisos, y las banderas de impresión.
6. **Consignatarios** (D7): todos tal cual, uno por fila de `Consignees` (número → código; repetidos sin código), con teléfono y correo.
7. **Contratos y tarifas** (D13): un contrato por cliente con sus tarifas por servicio × paquete (primera franja de ítems =
   tarifa por servicio, franjas siguientes = pieza extra por tramos), cargo COD y cargo de despacho.
8. **Campos personalizados**: `division` (D6, lista con las divisiones de AxisCare) y `colavora_id` (ShipmentID, para rastrear).
9. **Órdenes** (D2 todo el histórico, por lotes de `ShipmentID`): la orden (`OrderNumber` = `WaybillNumber` = waybill de
   Colavora), dos paradas con el snapshot del remitente y del consignatario, líneas y piezas (D8 + P11: la pieza `Invoice` pasa a
   número de factura del cliente; los tipos de `options.descriptionPackageTypes` —el ítem `Drum` de AxisCare, un código de producto
   que se repite en cientos de órdenes— van como descripción y cantidad de la línea de carga, sin pieza; todo lo demás es pieza con
   su label como número, y un label repetido en la compañía lleva sufijo `~id`), referencias, estatus final (D3: Pending con más de 5 días → Entregada; D4b: pick up entregados
   → Entregada), **todo el historial** con fechas y usuarios originales (D9; los eventos sin estatus como comentario), intentos
   de entrega (razones 306/307/315/… y 04–11 → Intento fallido), **un viaje por manifiesto** (D5, `Trip` `M-<manifiesto>`), el
   precio congelado desde los eventos `Price - *` (D13), la factura procesada desde PWBack (campos del Lote 126, D4/D8), y el
   comprobante con **la firma y la foto en el almacén de archivos** de Teikem (D10 + Lote 127: `StoredFile` cifrado, clases
   `SIGNATURE` y `DELIVERY_PHOTO`, dueño = el comprobante; la web las muestra en el comprobante y se piden con `GET /files/{id}`).
   Exige `Files:MasterKey` configurada en el API que corre la migración (en Development ya está; en producción la del servidor): sin
   ella se avisa una vez y las imágenes no se registran. `options.podFilesDir`, si se pone, además las exporta a disco por waybill.

Idempotente: volver a correr no duplica (clientes por código, consignatarios por número o dirección, choferes por código,
zonas por código, órdenes por waybill, piezas por número). Las órdenes se escriben directo por EF con la auditoría apagada
(no cabría en la bitácora) y conservan `CreatedAtUtc`/`CreatedBy` originales.

## Orden sugerido

1. Base local al día: `dotnet run --project src/Teikem.Api -- db-update`.
2. `.\scripts\crear-variable-colavora.ps1 -DesdeColavoraApi` (una vez por máquina).
3. Simulación completa: `.\scripts\migrar-colavora.ps1 -Compania advance -DryRun` y leer el reporte.
4. Carga real local por fases: maestros primero (`-Solo catalogos,usuarios,zonas,clientes,consignatarios,contratos`), luego órdenes
   por rangos (`-Solo ordenes -Desde … -Hasta …`); revisar en la web.
5. El día del corte (D15): parar Colavora, `db-update` en producción, correr las dos compañías, arrancar Teikem.

Reglas: la fuente se lee **solo con `SELECT`**; nunca se escribe en Colavora. Los datos reales (reportes, comprobantes) van fuera
del repositorio, en `F:\Download\TeikemMigracion\colavora\`.
