# Plan — Migración de datos de Advance Depot y Advance Solutions (QuickBooks + WDSS/MSWM)

Fecha del análisis: 2026-09-28. Fuente: `TeikemData.zip` (2 exportaciones de QuickBooks Desktop y el script completo de la
base de datos `MSWM` del WMS actual). Este documento es el plan para revisión del dueño; no se ha escrito código.

---

## 1. Qué hay en los datos (hallazgos)

### 1.1 QuickBooks "Depot" (3 hojas Excel)

| Archivo | Filas | Qué contiene | Observaciones |
|---|---|---|---|
| `Depot Products.xlsx` | 576 ítems | Maestro de artículos (575 *Inventory Part*, 1 *Inventory Assembly*) | 1 inactivo. **Sin costo ni precio** (todos en 0). 59 sin descripción. 10 SKU con espacios (`"1040 P"`, `"TAPE CLEAR"`, `"SF-80 CLEAR"`…). Columnas *Lot* = TRUE en 500 y *Serial* = TRUE en 3 (parecen valores por defecto, no una política real). |
| `Depot Customers.xlsx` | 2 | `AxisCare` (Toa Baja) y `prueba2` | Prácticamente no hay clientes en Depot. |
| `Depot Vendor.xlsx` | 10 | Proveedores de equipo médico (RC Imports, Drive Medical, Labotek, Tele-Made, Dynarex, …) | 3 con dirección/teléfono. |

Distribución de la columna **Category** de `Depot Products` (la que define a qué compañía va cada ítem):

| Category | Ítems | Destino según la regla del dueño |
|---|---|---|
| (vacía) | 530 | Advance **Depot**, categoría **AxisCare** |
| `CARTONES` | 20 | Advance **Depot**, categoría **CARTONES** (cajas corrugadas, tape, stretch film, labels, sobres) |
| `SOLUTIONS` | 25 | Advance **Solutions** (todos tienen *Brand*: GLOBAL, PRODIGY, AMERICAN RED CROSS, PADIMONT, A. BELLE, YARDLEY) |
| `NATIONAL GUARD` | 1 | SKU `12525556` "frexzer": basura/prueba. **Se descarta** (decisión 5). |

Cruce Depot ↔ Solutions: de los 25 ítems `SOLUTIONS`, 20 existen en `Solutions Items.CSV` con el mismo SKU; los otros 5 son
`00050-7` (no está en Solutions) y `NECH1001..1004` (en Solutions se llaman `NECH 1001..1004`, con espacio). Las 3 cajas
`101010`, `121212`, `979` existen en **ambas** compañías (Depot se las vende a Solutions; factura "CORRUGADOS"). Eso es
correcto: mismo SKU, dos compañías distintas.

### 1.2 QuickBooks "Solutions" (7 CSV)

| Archivo | Filas útiles | Qué contiene | Observaciones |
|---|---|---|---|
| `Solutions Items.CSV` | 62 | 41 *Inventory Part*, 2 *Non-inventory*, 2 *Service*, 6 *Group* (ofertas), 6 *Discount*, 5 impuestos | 36 con existencia ≠ 0; **una negativa** (`24-081028` = −208). Costo en 39 y precio en 48. Marcas: GLOBAL, PRODIGY, MedPride (`MPR-*`), American Red Cross (`ARC*`), PADIMONT, DEPOT (cajas). |
| `Solutions Customers.CSV` | 695 | Farmacias y hospitales de PR | Todos activos. Términos: CHEQUE 374, Net 30 191, Net 15 37, CASH 22, Net 45 21, ACH 20, Consignment 15, Net 20 4, PK BY REP 1, vacío 10. Representante (`Rep`): RA, KDF, COD, MRG, ASG, SLV. Correo en 693, teléfono en 95. *Ship to* distinto de *Bill to* en 62 (50 sin distinguir mayúsculas: en 12 solo cambian las mayúsculas). Balance ≠ 0 en 265 (cuentas por cobrar). 681 de 695 direcciones tienen una línea "Ciudad, PR 00xxx" parseable. Dos clientes de prueba: `1` y `Alec`. |
| `Solutions Vendors.CSV` | 38 | Proveedores de QuickBooks | Solo ~6 son proveedores de mercancía (ADVANCE LOGISTICS LLC, GLOBAL IMPORT SALES, PADIMONT HOLDINGS, SHIELD LINE, ADVANCE DEPOT SOLUTIONS, FARMACIA CENTRAL DRUG por un préstamo de monitores). El resto son nómina, Hacienda, IRS, bancos, seguros, servicios. |
| `Solutions Sales Items.CSV` | 11,078 líneas | 3,991 facturas / 23 notas de crédito, 2025-12-19 → 2026-09-28, 693 clientes, 48 ítems | Historial de ventas (contabilidad). |
| `Solutions Purchases.CSV` + `Soutions Purchases by Item.CSV` | 83 líneas | 18 facturas de compra, 2026-04-08 → 2026-09-22 | Historial de compras. |
| `Solutions Trxns.CSV` | 32,767 | Libro mayor completo 2026 (facturas, pagos, nómina, depósitos, cheques…) | Contabilidad pura; no aplica a Teikem. |

### 1.3 WMS actual (`MSWM.sql`, 398 MB, UTF-16; 150 tablas, 458 vistas/procedimientos)

Es el script completo "con datos" de la base `MSWM` (WDSS). Lo relevante:

| Tabla | Filas | Qué contiene |
|---|---|---|
| `Company` | 1 | `Advance` — "Advance Logistics PR", Av. Shuford PR-784, Caguas |
| `Warehouse` | 2 | `Main` ("AxisCare") y `TrussPR` ("Truss PR") |
| `Item` | 810 | 618 en `Main` (equipo médico = Depot) y 192 en `TrussPR` (shampoos y productos de peluquería = cliente Truss) |
| `Location` | 3,988 | `Main`: 3,890 posiciones — 2,327 "Picking Location" (`01-A-01`…`26-E-xx`, mayúsculas), 1,477 en minúsculas (`01-a-24`… pasillos hasta 36, mezcla de "Picking"/"REGULAR"), 74 "ADVANCE LOGISTICS M" (pasillo 25), y especiales: `R1` recibo, `S1` embarque, `W1` wave, `Z1` conteo, `PISO`, `FLOOR`, `CARTONES`, `MATTRESS PISO`, `Holding`. |
| `Inventory` | 1,477 | **`Main`: 1,310 filas, 75 SKU con existencia, 367,329 unidades en 1,239 posiciones.** `TrussPR`: 166 filas, 163 SKU, 67,049 unidades. |
| `ItemUPC` | 186 | Códigos de barras (4 ítems con más de uno) |
| `ItemUnitOfMeasure` | 2,706 | Todos `EACH` (cantidad) y `LBS` (peso); factor 1 → no hay empaques reales |
| `CustomerBillTo/ShipTo` | 763 | Salones de belleza (clientes de **Truss**, no de Depot ni Solutions; 0 coinciden con Solutions) |
| `PickingOrder` | 3,949 | 3,842 de `TrussPR`, 107 de `Main` (2021-06 → 2025-11) |
| `Vendor` | 12 | Los mismos 10 de `Depot Vendor.xlsx` + pruebas |
| `Users` | 10 | Cuentas del WMS (no se migran; Teikem tiene su propia identidad) |

Perfil del inventario de `Main` (el que pertenece a Depot):

- **264,593 unidades (72 %) están en la posición única `CARTONES`** (las cajas). 74,567 en pasillos mayúsculas, 28,143 en
  pasillos minúsculas, 26 en `PISO`.
- Sin series. La columna *lote* vale `§` o `0` (nada). Las fechas de vencimiento **no son fiables**: 163 filas tienen el
  default `2030-06-27` y el resto son fechas de recibo desplazadas (camas y bastones "vencidos" en 2024). Once ítems tienen
  más de una fecha. Conclusión: no hay lotes reales que migrar.
- 73 de los 75 SKU con existencia están en `Depot Products`; los otros 2 no están en ningún QuickBooks: `886-PS4` (cama
  eléctrica UCI, 10 u.) y `402-596123302` (blood collection, 375 u.).
- **Las existencias de QuickBooks Depot no coinciden con las del WMS en 185 de 576 ítems** (ej. `101010`: QB 3,925 vs WMS
  5,275; `121212`: QB 987 vs WMS 4,775; `166-10401-DK-1`: QB −446 vs WMS 829). QB Depot suma 134,966 u.; el WMS 367,329 u.
- Los 25 ítems `SOLUTIONS` tienen **0 existencia en el WMS**; su existencia solo está en QuickBooks Solutions (sin ubicación).
- El cruce de SKU debe hacerse **sin distinguir mayúsculas y recortando espacios**: el WMS tiene `171-ac-426-b` y Depot
  `171-AC-426-B` (2,481 u. en QB), `smsoxwl-30` vs `SMSOXWL-30`, etc. Unos 40 SKU del WMS son variantes huérfanas
  (`10401dk1`, `166-10401BR-2`, `32 1/2X12X12`, `prueba1`) sin existencia: se ignoran y se listan en el reporte.

---

## 2. Cómo encaja en Teikem

### 2.1 "Compañía" en Teikem = `Tenant`

Hoy Teikem no tiene un nivel "organización" por encima de la compañía. La entidad que la interfaz llama **Compañía** es
`Tenant` (`EntityType.TENANT` = "Compañía"; `auth/switch-tenant` = "Cambio de compañía"). Advance Logistics es el tenant
demo aprovisionado por `DemoTenantSeeder`.

**Lo que ya se comparte entre compañías (sin hacer nada):** todos los catálogos `LookupCode` y `StatusCode` son globales,
con override opcional por tenant (`LookupCodeOverride`/`StatusCodeOverride`). Unidades de medida, términos de pago,
tipos de seguimiento, motivos de ajuste, países, tipos de zona, etc., son los mismos para las tres compañías. Esto responde
la duda: **los catálogos son compartidos**; cada compañía solo puede renombrar/apagar valores.

**Lo que es propio de cada compañía (por diseño):** `ProductCategory`, `Product`, `Client`, `Supplier`, `Warehouse` (zonas,
posiciones, muelles), `StockBalance`, secuencias de numeración, roles, campos personalizados, módulos encendidos.

**Personas que trabajan en más de una compañía:** `UserTenant` (un usuario, varias compañías) ya existe; el selector de
compañía aparece en el encabezado solo si el usuario tiene más de una membresía. `ProvisioningService.EnsureAdminUserAsync`
reutiliza un usuario existente si el correo ya está registrado, así que el mismo administrador puede quedar en las tres.

### 2.2 Dos formas de modelarlo (recomendación: A)

| | **A. Una compañía (tenant) por empresa** — recomendada | B. Depot y Solutions como *clientes 3PL* dentro de Advance Logistics |
|---|---|---|
| Cómo | Aprovisionar `Advance Depot` y `Advance Solutions` con `POST /api/v1/platform/tenants` (admin de plataforma, AAL2) | `Client` + `Product.ClientId` (dueño del SKU) + `InventoryScope` del usuario |
| Catálogos | Compartidos (globales) | Compartidos |
| Categorías de producto, almacenes, proveedores, clientes | Propios de cada compañía | Todo mezclado en un tenant; los 695 clientes de Solutions serían "clientes" del 3PL y las farmacias serían… consignatarios |
| Aislamiento | Total (filtro por `TenantId`) | Parcial (`ClientId`); Solutions vería el almacén de Depot |
| Coincide con la separación de QuickBooks | Sí: una compañía QB = un tenant | No |
| Costo | Cero cambios de esquema; la plataforma ya lo soporta | Requiere forzar semántica de 3PL donde no la hay |

Lo que dijo el dueño ("son compañías bajo una misma organización") se cumple con A: las tres compañías comparten la
plataforma, los catálogos y los usuarios que tengan membresía en varias. Si más adelante hace falta una etiqueta
"grupo/organización" sobre los tenants (p. ej., para reportes consolidados), se agrega como un campo a `Tenant` en un lote
aparte; no bloquea la migración.

### 2.3 Cómo hacer "los scripts": comando de importación, no T-SQL directo

Teikem exige que toda escritura pase por los servicios: `StatusService.TransitionAsync` (historial de estatus), auditoría
por interceptor, `InventoryLedger` como **única** vía de escritura del inventario (el manual: "nadie escribe `StockBalance`
fuera de él"), `PublicId`, validaciones de SKU/código, y la regla de "un solo set de scripts SQL" en `Diseño/`. Un script
T-SQL que inserte en `Product`, `Client`, `StockBalance`… rompería todo eso (sin `EntityStatusHistory`, sin `AuditLog`, sin
`InventoryTransaction`, la conciliación fallaría de entrada).

Propuesta: **un comando CLI del API**, al estilo de `db-init`:

```
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.depot.json --dry-run
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.depot.json
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.solutions.json --dry-run
dotnet run --project src/Teikem.Api -- import-legacy docs/migracion/import.solutions.json
```

- Implementado en `src/Teikem.Infrastructure/Migration/` (`LegacyImportService` + un lector por fuente:
  `QuickBooksCsvReader`, `MswmReader`). Llama a los servicios existentes (`ProvisioningService`, `ProductService`,
  `ClientService`, `LocationService`, `ContactPointService`, `SupplierService`, `WarehouseService`, `InventoryLedger`).
- **Idempotente**: la clave de cada entidad es su código natural (SKU, código de cliente, código de posición). Se puede
  correr varias veces; lo que ya existe se actualiza o se salta, y se informa.
- **`--dry-run`**: lee todo, valida contra las mismas reglas que los servicios y produce un reporte (`.md` + `.csv`) con
  filas aceptadas, rechazadas y por qué, mapeos aplicados (SKU normalizados, términos de pago, direcciones parseadas) y
  totales por entidad. **Nada se escribe hasta que el dueño apruebe el reporte.**
- Fuente del WMS: **no** se parsea el script de 398 MB; se restaura `MSWM` en el mismo SQL Server (`sqlcmd -i MSWM.sql`)
  y el importador lee `Item`, `Inventory`, `Location`, `ItemUPC`, `Warehouse` por una segunda cadena de conexión
  (`ConnectionStrings:LegacyMswm`). Solo lectura.
- Fuente de QuickBooks: los CSV. Las tres hojas Excel de Depot se exportan a CSV (QuickBooks lo hace directo, o se usa la
  conversión ya hecha en este análisis). Los archivos **no se suben al repositorio** (contienen correos y teléfonos de
  clientes): viven en una carpeta fuera de git referenciada por el JSON de configuración.
- Trazabilidad: un campo personalizado `Código QuickBooks` (`CustomFieldDefinition`) en Cliente y Producto, o `Notes`
  en Proveedor, guarda el nombre/código original para poder cruzar con QuickBooks después.

---

## 3. Mapeo fuente → Teikem

### 3.1 Compañías

| Teikem (`TenantProvisionRequest`) | Advance Depot | Advance Solutions |
|---|---|---|
| `Name` | Advance Depot | Advance Solutions |
| `LegalName` | Advance Depot Solutions | Advance Solutions Group (PO Box 5007, Caguas) |
| `TaxId` | vacío (se completa desde Ajustes) | vacío (se completa desde Ajustes) |
| Módulos | WMS_LOTSERIAL, PURCHASING, CATALOG, ANALYTICS, SYSTEM, CUSTOM_FIELDS | Igual (sin transporte por ahora) |
| Admin | `teikem+admin@cerevelo.com`, el mismo de Advance Logistics (membresía en las tres) | igual |

### 3.2 Advance Depot

| Fuente | Teikem | Regla |
|---|---|---|
| `Depot Products` Category vacía / `CARTONES` | `ProductCategory` **AxisCare** / **CARTONES** | Dos categorías raíz. El ítem `NATIONAL GUARD` se descarta. |
| `Depot Products` (551 ítems, sin los 25 `SOLUTIONS`) | `Product` | `Sku` = Item sin espacios ni caracteres de control (Teikem los rechaza; 10 casos), `Name` = Description (si vacía: el SKU; 59 casos, se listan), `BaseUom` = `UN`, `TrackingType` = **`NONE`** (decisión 4), `Barcode` = `ItemUPC` del WMS (primer UPC; los 4 con varios se listan), `PurchaseCost`/`SalePrice` = null (QB Depot no los tiene), `IsActive` según *Active Status*. |
| WMS `Item` de `Main` sin equivalente en Depot con existencia (`886-PS4`, `402-596123302`) | `Product` en AxisCare | Se crean para no perder 385 u.; el reporte los marca para revisión. |
| `Depot Vendor` (10) | `Supplier` | Nombre, contacto, teléfono, dirección en `Notes`. |
| `Depot Customers` (`AxisCare`) | `Client` + `Location` DELIVERY + `ContactPoint` PHONE | Pepsi Ind. Pk., PR-2 km 19.5, Toa Baja PR 00949; tel. 787-249-8344. `prueba2` se descarta. |
| WMS `Warehouse.Main` | `Warehouse` **ALM-DEPOT** "Almacén Depot" (Caguas, PR) | El demo `ALM-01` pertenece a Advance Logistics y no se toca. |
| WMS `Location` de `Main` | `WarehouseZone` + `WarehouseBin` | Zonas: **PCK** (PICKING: "Picking Location"), **RSV** (RESERVE: "REGULAR"), **ALM** (RESERVE: "ADVANCE LOGISTICS M", pasillo 25), **PISO** (RESERVE: `PISO`, `FLOOR`, `CARTONES`, `MATTRESS PISO*`, `15006 MATTRESS PISO`), **STG** (STAGING: `R1`), **SHP** (STAGING: `S1`). `W1`, `Z1`, `Holding`, `R-1` no se migran. Código de posición = `LocationId` en mayúsculas (`01-a-24` → `01-A-24`; si colisiona con uno mayúscula existente se conserva uno y se informa); `Aisle`/`Level`/`Position` a partir del patrón `PP-N-XX`. Se crean las 3,887 posiciones activas para que la operación siga con las mismas etiquetas. |
| WMS `Inventory` de `Main` (1,310 filas) | `InventoryLedger` `ADJUSTMENT` con motivo **`OPENING_BALANCE`** por (SKU, posición) | Cantidad = `OnHandQuantity` (todas positivas). Sin lote ni serie. `Notes` = "Migración WMS 2026-09-28". Después se corre `GET /inventory/reconciliation` → `mismatches: []`. **Fuente de verdad = WMS**, no QB Depot (decisión 3). |

### 3.3 Advance Solutions

| Fuente | Teikem | Regla |
|---|---|---|
| `Solutions Items` *Inventory Part* (41 − 3 cajas = 38) + `00050-7` de Depot `SOLUTIONS` (no está en Solutions Items; `NECH1001-1004` quedan cubiertos al normalizar espacios) | `Product` | `Sku` normalizado (`NECH 1001` → `NECH1001`), `Name` = Description, `BaseUom` = `UN`, `TrackingType` = `NONE`, `PurchaseCost` = Cost, `SalePrice` = Price, `Barcode` = UPC del WMS si existe. Las cajas `101010`, `121212`, `979` **no** se crean en Solutions (decisión 5). *Non-inventory* `001 ALLOWANCE` y `53310/53350` (combo), *Service*, *Group*, *Discount* e impuestos **no** son productos: se descartan y se listan. |
| Categorías | `ProductCategory` única **PRODUCTOS** | Decisión 5: una sola categoría general; se subdivide después desde la pantalla. |
| `Solutions Vendors` | `Supplier` | Solo la lista de mercancía (6, configurable en el JSON). El resto (nómina, Hacienda, bancos) no son proveedores de Teikem. |
| `Solutions Customers` (695) | `Client` | `Code` autogenerado por `ClientService` (o derivado del nombre, ≤ 30), `Name` = Customer, `LegalName` = Company, `PaymentTerm` mapeado (tabla 3.4), `Currency` = USD, estatus `ACTIVE`. `Balance` **no** se migra (cuentas por cobrar viven en QB). `Rep` → campo personalizado **Representante** (lista RA/KDF/COD/MRG/ASG/SLV). `1` y `Alec` se descartan. |
| `Ship to 1..5` (691) / `Bill to 1..5` (695) | `Location` tipo **DELIVERY** (ship-to; si falta, bill-to) y **BILLING** solo cuando difiere sin distinguir mayúsculas (50; otros 12 difieren solo en mayúsculas y no generan BILLING) | Parseo: línea 1 = nombre, líneas intermedias = `Line1`/`Line2`, línea "Ciudad, PR 00xxx" = `City`/`State`/`PostalCode`, última con dígitos y puntos = teléfono. 14 direcciones no parsean: `City` = "SIN CIUDAD" y se listan. `Country` = PR. |
| `Main Email` (693; varios separados por `;`), `Main Phone` (95), teléfono de la dirección | `ContactPoint` EMAIL / PHONE sobre `Client` | El primero es principal. |
| Existencias QB (`Quantity On Hand`, 36 SKU) | `Warehouse` **ALM-SOL** "Almacén Solutions" con una zona **GEN** (RESERVE) y una posición **GENERAL**; `ADJUSTMENT` `OPENING_BALANCE` | QB no tiene ubicaciones y el WMS tiene 0 para estos SKU. Cantidad negativa (`24-081028` = −208) → no se carga y se informa. |
| Ventas, compras, libro mayor | **No se migran** | Son contabilidad; Teikem no es el libro. Si se quiere "último pedido" por cliente, es un lote posterior de órdenes/facturas. |

### 3.4 Catálogos compartidos que hay que ampliar en `Diseño/logistica-db-seed.sql` (MERGE idempotente)

| Entity | Nuevos `InternalCode` | Motivo |
|---|---|---|
| `PaymentTerm` | `CHEQUE` (Cheque), `CASH` (Efectivo), `ACH`, `NET20`, `NET45`, `CONSIGNMENT` (Consignación), `PK_BY_REP` (Cobra el representante) | Términos de QB Solutions. Existen ya `COD`, `NET15`, `NET30`, `NET60`. |
| `AdjustmentReason` | `OPENING_BALANCE` (Saldo inicial / migración) | Motivo del asiento inicial; reservado al sistema como `RECEIPT_VARIANCE` (no seleccionable en pantalla). |

`UnitOfMeasure.UN` cubre "Each (EA)". `Country.PR` existe. No hace falta ningún permiso nuevo: el comando corre como el
admin de plataforma fuera del pipeline HTTP, igual que `db-init`.

---

## 4. Fases

| # | Fase | Entregable | Cómo se verifica |
|---|---|---|---|
| 0 | **Decisiones del dueño** (sección 5) | Este documento aprobado + `import.depot.json` / `import.solutions.json` | — |
| 1 | **Preparación de fuentes** | `MSWM` restaurada en el SQL Server local (`sqlcmd -i MSWM.sql`); CSV de Depot; carpeta `F:\Download\TeikemMigracion\` fuera de git | `SELECT COUNT(*)` de `Item`/`Inventory`/`Location` = 810 / 1,477 / 3,988 |
| 2 | **Seed** | Nuevos `PaymentTerm` y `AdjustmentReason` en el seed; `db-init` en BD existente | CI verde; `GET /catalogs/PaymentTerm` los devuelve |
| 3 | **Importador** (`Teikem.Infrastructure/Migration`, comando `import-legacy`) | Lectores QB/MSWM, normalizador, mapeos, `--dry-run` con reporte, escritura vía servicios, idempotencia | Pruebas unitarias xunit de: normalización de SKU, parseo de direcciones, mapeo de términos, derivación de zona/pasillo, agrupación de inventario por posición |
| 4 | **Dry-run Depot y Solutions** | `docs/migracion/reporte-depot.md`, `reporte-solutions.md` con totales y rechazos | Revisión del dueño línea por línea de los rechazos |
| 5 | **Carga real** (BD de desarrollo primero) | Dos tenants aprovisionados y poblados | Totales esperados (sección 6); `GET /inventory/reconciliation` sin diferencias; recorrido en la web: cambiar de compañía, ver productos por categoría, saldos por posición, clientes con consignatario |
| 6 | **Documentación** (regla del repo) | `docs/loteN-decisiones.md` (qué se construyó, cómo se probó, decisiones a revisar), capítulo `docs/manual/10-migracion-de-datos.md` (comando, JSON, reporte, mensajes de error exactos) y FAQ | — |
| 7 | **Corte en producción** | Congelar el WMS y QB (inventario) → dry-run con datos del día → carga → verificación → usuarios de Depot/Solutions con membresía | Conciliación y conteo cíclico de arranque en `CARTONES` y pasillos con más movimiento |

Fuera de alcance, por decisión del dueño (no se migra nunca): el almacén **TrussPR** (163 SKU, 67,049 u., 3,842 órdenes
históricas, 763 salones), que pertenece a Advance Logistics como cliente 3PL. El importador lo ignora explícitamente.

---

## 5. Decisiones tomadas por el dueño (2026-09-28)

1. **Modelo**: una compañía (tenant) por empresa (2.2-A). Advance Logistics queda intacta.
2. **Datos de las compañías**: `Advance Depot` (legal "Advance Depot Solutions") y `Advance Solutions` (legal "Advance
   Solutions Group"); `TaxId` vacío, se completa después desde Ajustes. Administrador: el mismo de Advance Logistics
   (`teikem+admin@cerevelo.com`), con membresía en las tres compañías. **Módulos** de ambas: WMS_LOTSERIAL, PURCHASING,
   CATALOG, ANALYTICS, SYSTEM, CUSTOM_FIELDS. Sin transporte por ahora.
3. **Fuente de verdad de existencias de Depot**: el WMS por posición. Los 2 SKU con existencia que no están en QuickBooks
   (`886-PS4`, `402-596123302`) se crean en AxisCare y se marcan en el reporte.
4. **Lotes y vencimientos**: todo se carga con seguimiento `NONE`; el lote se activa por producto cuando haga falta.
5. **Categorías de Solutions**: **una sola categoría general** (`PRODUCTOS`). Las cajas (`101010`, `121212`, `979`) **solo
   existen en Depot** (categoría CARTONES); no se crean en Solutions ni se carga su existencia de QuickBooks Solutions.
   El ítem `NATIONAL GUARD` (`12525556` "frexzer") se descarta.
6. **Proveedores de Solutions**: solo los 6 de mercancía (lista fija en el JSON).
7. **Limpieza**: se descartan los clientes de prueba (`1`, `Alec`, `prueba2`); los 59 ítems sin descripción se cargan con el
   SKU como nombre y quedan listados en el reporte; los SKU con espacios se normalizan quitándolos (`1040 P` → `1040P`,
   `NECH 1001` → `NECH1001`) y cada cambio se lista.
8. **Historial** de ventas, compras y libro mayor de Solutions: no se migra; QuickBooks sigue siendo el libro.
9. **Truss PR**: no se migra, ni ahora ni después.
10. **Solutions sin ubicaciones**: almacén `ALM-SOL` con una zona y una posición `GENERAL`; el saldo inicial entra ahí y se
    reubica después desde la pantalla. El SKU con existencia negativa (`24-081028` = −208) no se carga y se informa.
11. **Cliente de Depot**: se carga `AxisCare` (Toa Baja) como cliente con consignatario de entrega y teléfono.

---

## 6. Totales esperados tras la carga (para la verificación)

| Entidad | Advance Depot | Advance Solutions |
|---|---|---|
| Categorías de producto | 2 (AxisCare, CARTONES) | 1 (PRODUCTOS) |
| Productos | 550 (551 menos `frexzer`) + 2 del WMS (`886-PS4`, `402-596123302`) = 552 | 39 (38 de Solutions Items + `00050-7`) |
| Proveedores | 10 | 6 |
| Clientes | 1 (AxisCare) | 693 |
| Consignatarios (`Location`) | 1 DELIVERY | ~693 DELIVERY + 50 BILLING (743) |
| Puntos de contacto | 1 teléfono | ~700 correos + ~100 teléfonos |
| Almacenes / zonas / posiciones | 1 / 6 / 3,887 | 1 / 1 / 1 |
| Asientos de saldo inicial (`InventoryTransaction`) | 1,310 (75 SKU, 367,329 u.) | 32 (36 SKU con existencia menos las 3 cajas y el negativo) |
| Conciliación | `mismatches: []` | `mismatches: []` |

---

## 7. Cupo máximo de las posiciones de Depot (decisión del dueño, 2026-09-30)

Decisión: llenar el cupo máximo (`WarehouseBin.MaxCapacityQty`, unidades de producto; ver `docs/lote11-decisiones.md`) de
las posiciones de Advance Depot **estimándolo desde el historial de MSWM**, y dejar una herramienta para corregirlo en
bloque (`POST /api/v1/warehouses/{publicId}/bins/capacity`, capítulo 6 del manual).

**Por qué no se usan los campos de capacidad del WMS.** `Location.PalletCapacity` no sirve: 2.193 posiciones tienen el
valor por defecto 50 con existencias de hasta 1.212 unidades y el resto vale 0. `Item.FullPalletQty` y `Item.Cube` están
vacíos.

**Fuentes (solo lectura, almacén `Main`)** — `MswmReader.BinHistoryQueries`, una fila por "foto" y posición, ya sumada en
SQL y solo con totales > 0:

| Tabla | Foto | Cantidad |
|---|---|---|
| `Inventory` | una (el inventario actual) | `SUM(OnHandQuantity)` por `LocationId` |
| `Inventory_Old` | una (el inventario anterior) | `SUM(OnHandQuantity)` por `LocationId` |
| `CycleCountInventory` | una por `Request` + `Iteration` | `SUM(CountQuantity)` por `LocationId` |
| `CycleCountHistory` | una por `Request` | `SUM(OnHandQuantity + AdjustmentQuantity)` por `LocationIdCounted` (o `LocationId` si viene vacío); sin filas revertidas. En MSWM `LocationId` viene vacío y `OnHandQuantity` es siempre 0: lo contado está en `AdjustmentQuantity` |
| `PutAwayHistory` | una por día (`CAST(TransDate AS date)`) | `SUM(Quantity)` hacia `LocationTo` |

**Regla** (pura, `Teikem.Domain.Migration.BinCapacityRules`, pruebas en `BinCapacityTests`):

1. Máximo histórico de una posición = el mayor total > 0 entre todas sus fotos (los ids del WMS se normalizan con
   `LegacyImportRules.ParseBinCode`, así `01-a-24` y `01-A-24` son la misma posición).
2. Con historial → cupo = máximo histórico redondeado **hacia arriba a la decena**, mínimo 10. Origen `HISTORIAL`.
3. Sin historial → mediana de los cupos `HISTORIAL` de su **pasillo** (`PASILLO`); si el pasillo no tiene ninguno, de su
   **zona** (`ZONA`); si tampoco, del **almacén** (`ALMACEN`). Mediana con cantidad par = promedio de los dos centrales;
   siempre redondeada hacia arriba a la decena. Los cupos heredados no alimentan otras medianas. **Una mediana de pasillo o
   de zona solo se usa si tiene al menos 5 cupos con historial** (`BinCapacityRules.MinSamples`); con menos se pasa al
   nivel siguiente — agregado tras la validación de abajo: con dos datos (30 y 264.600) la "mediana" de la zona PISO era
   132.320, que no representa a nadie. El pasillo es el que el
   importador ya deriva del `LocationId` (`NN-L-NN` → `NN`); las posiciones especiales (PISO, R1, S1…) no tienen pasillo y
   van directo a su zona.
4. Solo para almacenes que vienen de MSWM (`sources.mswm` y sin `warehouse.singleBin`): Advance Solutions no aplica.
5. Escritura: una posición nueva nace con su cupo (`CreateBinAsync` con `MaxCapacityQty`); una existente con cupo lo
   conserva siempre; una existente sin cupo solo se llena con `--update` (con la asignación en bloque y
   `onlyWithoutCapacity: true`, que dentro de su transacción vuelve a excluir las que ya tengan cupo). `--dry-run`
   calcula y reporta sin escribir.
6. Reporte: CSV `reporte-depot-{fecha}-cupos.csv` (posición, zona, pasillo, máximo histórico, cupo, origen, resultado) y
   sección "Cupos de posición estimados" en el `.md` (posiciones, mínimo, mediana y máximo por origen; conteo por
   resultado).

**Resultado al validar contra la base MSWM real (2026-09-30, lectura, plan puro sin escribir en Teikem; ANTES de la regla
de los 5 datos — la distribución final queda en el reporte `reporte-depot-*-cupos.csv` de la importación):** 3.886
posiciones del plan, 8.243 fotos (Inventory 1.239, Inventory_Old 1.668, CycleCountInventory 1.153, CycleCountHistory
1.127, PutAwayHistory 3.056).

| Origen | Posiciones | Cupo mínimo | Mediana | Cupo máximo |
|---|---:|---:|---:|---:|
| HISTORIAL | 2.709 | 10 | 50 | 264.600 (`CARTONES`) |
| PASILLO | 717 | 10 | 10 | 120 |
| ZONA | 459 | 50 | 50 | 132.320 |
| ALMACEN | 1 (`S1`) | 50 | 50 | 50 |
| **Total** | **3.886** | 10 | 50 | 264.600 |

Cobertura con historial: pasillos 01–24 entre 76 % y 95 % de sus posiciones; 25–30 entre 5 % y 37 %; 31–36 ninguna (sus
456 posiciones toman la mediana de la zona PCK, 50).

**A revisar con el dueño:**

- **Zona PISO (resuelto con la regla de los 5 datos: `FLOOR` y las demás de piso toman la mediana del almacén):** solo `CARTONES` (264.593 u., cupo 264.600) y `PISO` (cupo 30) tienen historial; la mediana de esos dos
  cupos es su promedio,
  así que `FLOOR`, `MATTRESS-PISO-DEPOT` y `15006-MATTRESS-PISO` reciben **132.320**. Es un artefacto de la mediana con dos
  valores muy distintos: conviene corregirlos a mano o quitarles el cupo (`{ "binIds": [...], "clear": true }`).
- **Pasillos 25–30 con poco historial:** su mediana (10) se aplica a las demás posiciones del pasillo (p. ej. en el 28 solo
  4 de 76 tienen historial).
- **Posiciones de preparación:** `R1` (recepción, zona STG) sale con 410 por historial y `S1` (embarque, SHP) con 50 del
  almacén. Si no se quiere cupo en zonas de preparación, quitarlo en bloque por zona.
