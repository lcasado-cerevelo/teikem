# Búsqueda global («Buscar o ejecutar») y cambios asociados — dictado de Luis, 2026-10-20

Estado: **solo anotado, nada construido todavía.** Este documento junta lo que Luis dictó y lo que se averiguó en el código, para que no se pierda.
Lo marcado **(interpretación)** es lo que se entendió de un dictado ambiguo y se confirma antes de construir.

## 1. Qué se pidió

La paleta de arriba («Buscar o ejecutar…») hoy solo filtra el menú. Debe buscar **registros por sus números y nombres**, en todo lo que tenga código, y llevar a la pantalla que corresponde:

- **Un solo resultado exacto → va directo** a esa pantalla (envío, cliente, almacén, producto, etc.).
- **Uno exacto y otros parecidos, o varios → se muestran en la paleta, agrupados por tipo,** para que el usuario escoja.
- Debe ser **rápida y eficiente** (coincidencia exacta o por prefijo donde haya índice; nada de recorrer tablas enteras).
- Respeta permisos y módulos: cada tipo se busca solo si el usuario lo puede ver.

### Qué se busca y a dónde lleva

| Se busca por | Lleva a |
|---|---|
| Envío: número de envío, número de empaque (EMP), número de conduce, número de factura del cliente, número de paquete/pieza, referencia del cliente, **número de parte (= SKU del producto, interpretación)** | Ficha del envío |
| Número de ruta (el que Luis llama número de ruta; ver §4) | La ruta (monitor / sala de despacho) |
| Número de reclamación | Reclamaciones, filtrada (ver §5). También por **nombre del cliente** (interpretación) |
| Cliente: número (código) y nombre | Ficha del cliente |
| Almacén: código y nombre; posición; zona | Almacén / posiciones |
| Producto: código (SKU), **nombre o parte del nombre** | Ficha del producto |
| Proveedor: **por nombre** (no tienen número) | Proveedores, filtrada |
| Recibo (`REC-`), recolección/empaque (`EMP-`, `DMA-`), aviso de salida (`SAL-`) | Su pantalla |
| Aviso de llegada: cuando tenga código (ver §3) | Avisos, filtrada |
| Factura, orden de compra, orden de venta | Su pantalla (factura: lista filtrada) |
| Teléfono | El contacto o su dueño (ver §6) |
| Número de serie | **Fuera por ahora** |

## 2. Nombre «Envíos» en lugar de «Órdenes»

- **Donde diga «orden/órdenes» y sea de transporte, debe decir «envío/envíos»**: pantalla de Envíos (título y textos), Sala de despacho («Envíos sin asignar» → «Envíos sin asignar»), escaneo, reclamaciones, y **también el portal del cliente** (`app-cliente`).
- **No cambian** las órdenes de compra, de venta ni de trabajo (mantenimiento): se siguen buscando y mostrando como órdenes (interpretación; falta confirmar).
- Alcance conocido (mapa del 2026-10-20): el título del menú ya dice «Envíos» (`nav.orders.title`). Quedan unas **236 cadenas** en `es.json` con «orden/órdenes» sin ser de compra/venta/trabajo, en su mayoría `orders.*`, `dispatch.*`, `scan.*`, `claims.*`, `cod.*`, `integrations.*`; algunas están mezcladas (`warehouse.pickBatches.*`, `analytics.attention.*`, `system.settings.*`) y se revisan una por una. Hay que hacerlo en **es y en** (inglés: «Shipments»), y en `app-cliente`.
- «Envíos sin asignar» está en `dispatch.unassigned.title` (y `added`, `empty`, `emptyFiltered`, `error`).

> ✅ **Paso 1 hecho (2026-10-21).** Confirmado por Luis: «Envíos» solo en transporte; orden de compra, de venta y de trabajo se quedan como están. Al revisar, el web (`es.json`/`en.json`) y el portal del cliente (`app-cliente`) ya decían «Envío/Shipment» en transporte; solo faltaba «El envío hereda el nivel de su zona» (`fleet.levels.help`). Los mensajes de error del servidor que dicen «la orden» (unos 60) no se tocaron: llevan su texto exacto en el manual, el FAQ y las pruebas; se cambian en un paso aparte si Luis lo pide. §8 hecho: en la lista de Envíos los números de paquete salen cada uno en su renglón y sin partirse. Prefijo de reclamaciones confirmado por Luis: **`RCL-`** (paso 2).

## 3. Avisos de llegada con código

Luis ya se lo pidió a **otra sesión**: los avisos de llegada (ASN) tendrán código. Esta sesión no lo toca; cuando exista, la búsqueda lo incluye con el formato que esa sesión defina. **El campo se llama `Number`** (decidido por Luis, 2026-10-20; igual que `Receipt.Number`, `ExitNotice.Number`, `PickBatch.Number`), y la búsqueda lo usará con ese nombre. Cuidar que el prefijo no choque con `REC-`, `SAL-`, `EMP-`, `DMA-` ni el nuevo de reclamación.

## 4. Número de ruta

- En la pantalla del monitor, Luis ve `M-00120261020 · Chofer 1` y **ese** es el número de ruta para él.
- El mapa del código decía que el número era `Trip.Code` (formato `AAAA-####`); **no coincide con lo que Luis ve.** Antes de construir hay que averiguar de dónde sale el `M-001…` y buscar por ese.
- Las rutas se abren en `/ops/dispatch?date=…&trip=<publicId>` o `/ops/monitor?trip=<publicId>`; la sala necesita la fecha.

## 5. Reclamaciones: prefijo propio

- Hoy recibo y reclamación usan `REC-#####`: un `REC-00012` puede ser las dos cosas.
- **El recibo se queda con `REC-`. La reclamación cambia a tres letras distintas.** Propuesta: **`RCL-#####`** (falta que Luis confirme).
- Trabajo: sección nueva y idempotente en `Diseño/logistica-db-update.sql` que renombre las reclamaciones ya creadas (el índice único `UX_Claim_Number` se mantiene); cambiar el prefijo en `ClaimRules.NumberOf`, pruebas, manual y FAQ; revisar avisos y plantillas que muestren el número.
- Los archivos `logistica-db-estructura.sql` y `logistica-db-seed.sql` están congelados: no se editan.

## 6. Teléfonos: solo dígitos, prefijo internacional y formato por compañía

Dictado de Luis:

1. **El teléfono se guarda solo con números.**
2. **Un campo aparte para el prefijo internacional** (código de país, el «+1», etc.), aunque hoy todos sean de Estados Unidos: «para que sea full».
3. **El formato de despliegue se configura por compañía** en Ajustes (cómo se muestra el número, p. ej. `(787) 555-1234`). El número guardado no lleva formato; el formato se aplica solo al mostrarlo.
4. Hace falta un **análisis más a fondo** antes de tocarlo.

**Nombres definidos en la base de datos** (Luis aprobó `CountryCallingCode`, 2026-10-20; construido en el Lote 162, ver `docs/lote162-decisiones.md`):

| Dónde | Columna | Tipo | Qué guarda |
|---|---|---|---|
| `ContactPoint` | **`CountryCallingCode`** | `VARCHAR(4) NULL` | Prefijo internacional **sin el «+»**, solo dígitos (`1` para EE. UU. y Puerto Rico, `593` para Ecuador). Es el término técnico (ITU: *country calling code*). Se llena siempre al guardar un teléfono; `NULL` solo en los correos y en datos antiguos sin limpiar. |
| `ContactPoint` | `Value` (ya existía) | `NVARCHAR(200)` | Pasa a guardar **solo los dígitos nacionales** (p. ej. `7875551234`), sin formato. |
| `ContactPoint` | `Extension` (ya existía) | | Se queda aparte. |
| `Supplier` | `PhoneCountryCallingCode` | `VARCHAR(4) NULL` | Igual, para su columna `Phone`. |
| `Tenant` | `PhoneCountryCode` y `PhoneMask` (**ya existían**, Lote F9) | `VARCHAR(5)` y `VARCHAR(30)` | Prefijo por omisión de la compañía (`+1`) y plantilla de despliegue (`(###) ###-####`), en Ajustes → Región y formatos. No hubo que crear campos nuevos de compañía. |

Por qué `CountryCallingCode` y no `CountryCode`: «country code» se confunde con el código ISO del país (`US`, `PR`, `EC`).

Hechos del código que el análisis debe cubrir:
- Los teléfonos están en `ContactPoint` (polimórfico: `Value`, `Extension`, `Label`, tipo `PHONE`/`MOBILE`/`FAX`/`WHATSAPP`…). Se guarda el texto tal cual se tecleó; `ContactPointService.ValidateValue` usa `PhoneRegex`. No hay índice sobre `Value`.
- Hay también `Supplier.Phone` (columna directa) y datos de contacto de consignatarios.
- Afecta: formularios de contacto, importaciones (CSV/integraciones/API de clientes), API pública, portal, apps, y **los datos ya guardados** (limpieza y migración idempotente en `logistica-db-update.sql`).
- La búsqueda por teléfono depende de esto: con dígitos y un índice, es exacta y rápida; el dueño se resuelve con `IOwnedEntityResolver` (hay que traducir `OwnerId` a `publicId` en el servidor).
- Se configura por compañía como los demás formatos de «Región y formatos» (revisar `Tenant` y `TenantSettingsPage`).

## 7. Abrir lo que no tiene ficha directa

Reclamaciones, proveedores, facturas (y avisos de llegada cuando tengan código) **no tienen enlace directo**. Decisión de Luis: se abre **su pantalla de lista ya filtrada por ese número o nombre**, con el filtro en la dirección (p. ej. `?q=RCL-00012`). Hay que revisar que cada una de esas pantallas lea el filtro de la dirección y lo aplique (hoy ninguna lo hace).

## 8. Arreglo suelto: códigos largos en la tabla de Envíos

En la tabla de Envíos, empaque, conduce y número de envío (p. ej. `0079372102937839AXH`) se parten en tres renglones y no se leen ni se copian bien. Corregir: que el código no se parta (ancho fijo, sin envolver). Es independiente de la búsqueda.

## 9. Lo que ya existe (mapa del código, 2026-10-20)

- No hay búsqueda global en el servidor; la paleta solo filtra el menú (`CommandPalette.tsx`, `commandPaletteStore.ts`, montada en `AppShell.tsx`). `QBox` es solo del frontend.
- `GET /orders/lookup?code=` ya hace coincidencia exacta con precedencia conduce → orden → empaque → factura → paquete/pieza y usa índices: base para la parte de envíos. `GET /orders?search=` busca con `Contains`; **no** busca la referencia del cliente (`OrderReference.RefValue`).
- Otros endpoints con `search`: clientes, consignatarios, productos (SKU/nombre/código de barras), proveedores, órdenes de compra, recibos, avisos, EMP/DMA, reclamaciones, rutas, facturas, rentas y `warehouses/bins/search` (posición, zona, código y nombre del almacén).
- Nombres y números:
  - Envío: `OrderNumber`, `PackBatchNumber` (EMP), `WaybillNumber` (CND-), `ClientInvoiceNumber`, `CargoLine/OrderPiece.PackageNumber`. No existe «número de envío (tracking)» ni «número de parte» como campos.
  - Reclamación `Number`; cliente `Code`/`Name`; almacén/zona/posición `Code`; producto `Sku`/`Name`; recibo `Number`; EMP/DMA `PickBatch.Number`; salida `ExitNotice.Number` (SAL-); factura `InvoiceNumber` (FAC-); compra `PurchaseOrder.Number` (PO-); venta `SalesOrder.Number` (OV-); renta `Rental.Number` (REN-).
  - Proveedor y aviso de llegada: sin número hoy.
- Permisos: el menú usa los códigos `*.view` de `MenuPermissions`; el API exige el permiso viejo de cada módulo (`inventory.view`, `purchasing.view`, `clients.read`, …). La búsqueda global debe aplicar **permiso y módulo por tipo**.
- Rendimiento: usar coincidencia exacta o prefijo (`LIKE 'x%'`) donde hay índice; el contenido con `%x%` solo en tablas chicas (clientes, proveedores, productos por nombre). Zona/posición, lote/serie y teléfono no tienen índice utilizable hoy.
- Hay que agregar aperturas directas que hoy no existen para los registros que sí tienen ficha pero no enlace (p. ej. `/orders/:publicId` existe; `?claim=` no).

## 10. Pendientes por confirmar con Luis

1. Prefijo de reclamación: **`RCL-`** (propuesto).
2. «Envíos» solo en transporte, no en compra/venta/trabajo.
3. «Número de parte» = SKU del producto.
4. Búsqueda por nombre del cliente: ¿en envíos y reclamaciones, o también en otras pantallas? (interpretación actual: en ambas).
5. Origen de `M-00120261020` (§4).
6. Formato de despliegue de teléfono por compañía: opciones de formato a ofrecer (análisis de §6).

## 11. Orden de trabajo propuesto

1. Cambio de nombre a «Envíos» (web, portal del cliente, es/en) y arreglo de los códigos largos (§2, §8): independiente y rápido.
2. Prefijo `RCL-` y migración (§5).
3. Endpoint `GET /api/v1/search` y paleta con resultados, salto directo y filtros por dirección en las listas (§1, §7).
4. Análisis y cambio de teléfonos (§6); luego la búsqueda por teléfono.
5. Avisos de llegada (cuando la otra sesión termine) y serie global (más adelante).

Cada paso va con su documento `docs/loteN-decisiones.md`, capítulo del manual y FAQ, como manda el `CLAUDE.md`.
