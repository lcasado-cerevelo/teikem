# Migración de Colavora Courier a Teikem — análisis de la fuente real

Fecha: 2026-10-18. Fuente: servidor `172.31.53.133` (SQL Server 2016), bases **`colavora_courier`** (operación) y
**`colavora_courier_login`** (usuarios y compañías), leídas **en vivo y solo lectura** con el usuario `advancelogistics`.
Las credenciales viven en las variables de entorno de usuario `ConnectionStrings__ColavoraCourier` y
`ConnectionStrings__ColavoraCourierLogin` (las crea `scripts/crear-variable-colavora.ps1`; nunca se escriben en el repo).

Este documento es el insumo para escribir los scripts de migración. **La sección 6 son las dudas que hay que cerrar con Luis
antes de programar.**

---

## 1. Qué hay en la fuente (números reales del 2026-10-18)

### 1.1 `colavora_courier_login`

| Tabla | Filas | Qué es |
|---|---|---|
| `Company` | 3 | **10029 Advance Logistics** (Caguas), **10030 De La Paz** (San Juan), **10036 Transport Demo** (demo, sin envíos). |
| `Users` | 252 | Todos los usuarios: staff, choferes y usuarios del portal del cliente. `SecGroup` 4 = chofer (138), 1 = staff/admin (109), 2 y 3 = 5 usuarios. `DeptID` = compañía + departamento (1002**9**3 = choferes de 10029, 1003**0**3 = choferes de 10030). `TypeID` = 1 marca 40 usuarios (parecen usuarios de portal de cliente). `Username` de los choferes = su código (`812`, `2006@delapaz`). `Password` es un hash propio (`SHA` bit) y 191 tienen `Auth0`: **las contraseñas no se pueden migrar**. |
| `UsersSender` | 569 | Usuario ↔ clientes que ve (usuarios de portal y staff con varios clientes). |

### 1.2 `colavora_courier`

| Tabla | Filas | Qué es |
|---|---|---|
| `Shipments` | **1,262,945** | Las órdenes. 10029: 479,935 · 10030: 783,010. Desde **2023-08-15** hasta hoy (2023: 114k, 2024: 403k, 2025: 422k, 2026: 324k). Lleva **snapshot** del remitente y del consignatario (nombre, dirección, ZIP, teléfono), estatus, chofer de recogido (`AssignPK`) y de entrega (`AssignDL`), manifiestos (`ManifestPk`/`ManifestDL` = código de chofer + aaaammdd), COD, división, razón, referencia, códigos de barra. |
| `Shipments_Events` | **9,822,722** | Bitácora de la orden (promedio 7.8 por orden): estatus, asignaciones, manifiestos, comentarios, precios y «paperwork back». 12,340 eventos tienen `ShipmentID` no numérico (basura) y 97 apuntan a órdenes que no existen. |
| `Shipments_Pieces` | **4,687,959** | Piezas con código de barras y tipo de paquete; 1,268 órdenes sin piezas. En 10029 el `Comments` trae la descripción del ítem AxisCare (`P D N N AXH Y 00001 Y BATH BENCH…`). |
| `Shipments_POD` | 25,291 | Comprobantes desde **2024-09**: `PODType` 2 = firma (23,738; PNG base64 ~18 KB), 1 = foto (1,553; JPEG base64 ~150 KB). `PODName` = quién recibió. Solo 2,730 con GPS. ≈ 1 GB en base64. |
| `Consignees` | 44,485 | Consignatarios por cliente (`SenderId`). **38,669 sin `ConsigneeNumber`**; 77 números repetidos dentro del mismo cliente; 722 con GPS; 37,876 con zona (01–15). País/estado sucios (`PR`, `00926`, `P.R.`, vacío). |
| `Customers` | 99 | Clientes (`Sender` = código, `SenderName`), con dirección de recogido, chofer de recogido por defecto, banderas (factura, foto/firma en entrega, excluir de la app). Incluye **copias de prueba** (`-DEV`, `-PCK`, `SAMPLE:`, `CUSTOMER DUMMY`). 80 códigos tienen órdenes; 2 órdenes huérfanas (códigos `10100` y `99999` en 10030, 5 órdenes). |
| `Customers_Configuration` | 63 | Impresoras, factura automática, etiquetas, Shopify, factura duplicada, tipo de paquete/servicio por defecto. |
| `Customers_Service_Price` | 567 | **Tarifas** por cliente × servicio × tipo de paquete × rango de ítems, con cargo COD y cargo de despacho. |
| `Customers_Divisions` | 11 | Divisiones de AxisCare (GLT, GLC, OXY, GAM…) que la orden lleva en `Division`. |
| `Shipments_Status` | 19 | Catálogo de estatus por compañía (ver §3). |
| `Events` | 23 | Catálogo de tipos de evento manual (Comment, Llamada con 9 motivos, Inbound/Outbound, PWBack…). |
| `Shipments_Reasons` | 27 | Razones de entrega/no entrega por compañía (ver §3). |
| `ServiceType` 14 · `PackageType` 31 · `CODType` 19 · `TypeCode` 126 | | Catálogos por compañía (TypeCode = etiquetas es/en de los anteriores). |
| `Zip` | 531 | ZIP → ciudad, **zona METRO/ISLA**, chofer de recogido y de entrega por defecto. |
| `Shipments_Manifests` | 4,247 | Manifiestos **solo hasta 2024-01** (ya no se usa; hoy el manifiesto va en la orden). |
| `Drivers` | 1 | Vacía en la práctica. Los choferes son `Users` con `SecGroup` 4. |
| `AL*`, `Ontime360_Shipments`, `Ontime_Colavora_Users_Mapping`, `Imp_*`, `Temp_*` | 65k | **Restos de OnTime360 e importaciones** (2022–2024). No se migran. |
| `ApiIdempotency`, `ApiErrorLog`, `Gs1*`, `Consignees_Walgreens`, `PWBack`, `AppConfig` | | Técnicas o vacías. No se migran. |

### 1.3 Calidad de datos que condiciona el diseño

- **Estatus basura en 4,600 órdenes** (jun–jul 2026): `AssignDL`, `PWBackAD`, `PWBACK`, `Outbound`, `Comment`, `Llamada`, `Price - COD`, `Manifest-DL`, vacío. Un bug escribió el tipo de evento en `Status`. El estatus real se recalcula desde el último evento de estatus.
- **381,819 órdenes en `Pending`**: 10029 88k (2023–2026), 10030 **294k, 228k solo de 2025**. En 10030 nunca se cierran. Son órdenes que pueden seguir «abiertas» para siempre.
- `Completed` (420k) = entregada **y** con «paperwork back» (PWBack) conciliado. `Delivered` (396k) = entregada sin PWBack. En 10030 casi todo termina en `Completed`.
- `Qty` ≠ número de piezas en 1.14M órdenes: `Qty` cuenta paquetes; las piezas incluyen la factura (tipo `Invoice`) y los ítems de AxisCare.
- `ConsigneeNum` vacío en 811k órdenes; en AxisCare el «número» es el **teléfono** del paciente. **796,808 órdenes no casan con un consignatario** por (cliente, número). La orden es autosuficiente por su snapshot.
- Consignatarios: país/estado/ciudad inconsistentes; direcciones en mayúsculas; `Address1` a veces vacío y la dirección en `Address2`.
- Nombres de chofer con acentos corrompidos en los eventos (`VillafaÃ±e`), pero correctos en `Users`.
- `Weight` y `PONumber` vacíos en el 99.99 %. `ReferenceNo` lleno en el 81 % (en AxisCare = número de factura/orden del cliente, ej. `2936248-00`).
- `Waybill` único (1,262,945 distintos). Formatos: 10029 `0079306702936248AXH` (19–21, sufijo de división) y `23081410100001` (14, fecha+cliente+secuencia); 10030 `000000155001` (12) y `230819KP001` (11).

---

## 2. Catálogos de la fuente (completos)

### 2.1 `Shipments_Status`

| ID | Compañía | Estatus | Final | Factura |
|---|---|---|---|---|
| 1 | 10029 | Created | | sí |
| 2 | 10029 | PickedUp | | sí |
| 3 | 10029 | Inbound | | sí |
| 4 | 10029 | Assigned | | sí |
| 5 | 10029 | Our for Delivery | | sí |
| 6 / 12 | 10029 / 10030 | Delivered | sí | sí |
| 7 | 10029 | Partial Delivery | sí | sí |
| 8 | 10029 | Roundtrip | | sí |
| 9 | 10029 | Canceled | sí | no |
| 10 | 10029 | Completed | sí | sí |
| 11 | 10029 | Unassign | | sí |
| 13 / 14 | 10029 / 10030 | Pending | | sí |
| 16 / 18 | 10029 / 10030 | Delivered_ReOpen | | sí |
| 17 / 19 | 10029 / 10030 | Completed_ReOpen | | sí |

Estatus **realmente usados** en `Shipments`: Completed 420k, Delivered 396k, Pending 382k, Assigned 47k, Canceled 12k, Unassign 742 (+ los 4,600 basura). `Created`, `PickedUp`, `Inbound`, `Our for Delivery`, `Roundtrip`, `Partial Delivery` no aparecen como estatus actual (solo en eventos).

### 2.2 Tipos de evento (`Shipments_Events.EventType`), con cuántos hay y cómo se leen

| EventType | Filas | Qué significa / formato del texto |
|---|---|---|
| Import | 907k | `Colavora API Import` (AxisCare crea la orden por API). |
| Created | 557k | `Order Created by <usuario o cliente>` o `1 -`. |
| Pending | 342k | `Shipment marked as Pending automatically by System Administrator because ReadyDate…` (job). |
| AssignPk / AssignDL | 1.28M / 1.44M | `Assign PickUp Driver: 1003 - Customer Pickup Driver`, `Assign Delivery Driver: 2006 - Hector Hiraldo`, `Re-Assign Delivery Driver: …`. |
| Manifest-Pk / Manifest-DL | 1.28M / 1.48M | Número de manifiesto (`100320261017` = chofer 1003 + fecha). |
| Inbound / Outbound / In Transit | 59k / 232k / 88k | `6 - Outbound registered using system`, `2 - Aceptado en el portal Web de los choferes`. In Transit dejó de usarse en 2025-02. |
| Delivered | 468k | `312 - DELIVERED by: Carrier Axis (correo) Comment: …` o `3 -`. El código inicial es la razón (§2.3). |
| Completed | 120k | `13 - Completada por PWBack`, `370 - PWBACK COMPLETED`. |
| Comment / BOL Comment | 87k / 12k | `306 - PATIENT IS NOT IN HOUSE… - 1st by: … Comment: …` → **intentos fallidos** y notas. |
| Roundtrip / Pickup | 23k / 11k | Devolución al remitente / recogido. |
| Canceled | 13k | `Cancelled By User In Order Page`. |
| PWBack / PWBackAD / `*_del` | 628k / 177k / 4k | Conciliación de papeles (lote `n1989177`); `_del` = deshecho. |
| Price - Item / Extra Item / COD | 354k / 229k / 31k | Precio aplicado a la orden (texto = monto, `1.00`). Desde 2024-09. |
| Edit | 2k | `Status CHANGED FROM Assigned TO Pending`. |
| Llamada | 816 | Llamada al consignatario con motivo del catálogo `Events`. |
| Delivered_ReOpen / Completed_ReOpen / Unassigned | 298 / 62 / 69 | Reaperturas. |

`CreatedSource`: Colavora 6.0M, SQL 907k, PWBack 754k, Axiscare 737k, Web Portal 346k, SQL Job 341k, OnTime365 316k, User/Web/System/Dispatch/Mobil.

### 2.3 Razones (`Shipments_Reasons`)

| Compañía | Código | Descripción | Cierra la orden |
|---|---|---|---|
| 10029 | 306 / 307 | PATIENT IS NOT IN HOUSE ON DELIVERY SCHEDULED - 1st / 2nd | no |
| 10029 | 312 | (DELIVERED; no está en la tabla pero es la razón de entrega en 316k eventos) | — |
| 10029 | 314 | PICKUP | sí |
| 10029 | 315 | NOT DELIVERED | sí |
| 10029 | 330 | ORDER ON ERROR, RE-SENT | sí |
| 10029 | 335 | PATIENT- REFUSED | sí |
| 10029 | 340 | PATIENT- DECEASED | sí |
| 10029 | 355 | EVALUATED AND REPAIRED | no |
| 10029 | 360 | NOT DELIVERED, RETURNED TO WAREHOUSE | no |
| 10029 | 370 | PWBACK COMPLETED | sí |
| 10029 | 101 / 102 / 103 / 104 | OTHER / DEVOLUCION PARCIAL / MARKED DELIVERED / RETURN | no / no / sí / sí |
| 10030 | 01 … 13 | Entrega Parcial, Mal despacho, Orden Rechazada, Establecimiento Cerrado, No COD, Carretera Cerrada, Vehiculo Averiado, Cliente no contesta, Dirección no coincide, Cliente solicitó reprogramar, No tiene COD, Devolucion Parcial, Completada por PWBack | 01–03, 12, 13 sí |

### 2.4 Servicios, paquetes y COD

- **ServiceType** 10029: NEXT DAY (1), SAME DAY (2), PICK UP (3, recogido), Roundtrip (7), WALKEDIN (9), NEXT DAY ISLAND (10), PICKUP ISLAND (11), SAME DAY ISLAND (12), POSTAL SERVICE (13), PICK UP INVOICE (24). 10030: NEXT DAY (19), SAME DAY (20), PICK UP (21), ROUNDTRIP (22). Uso real: 1 → 711k, 19 → 468k, 3 → 24k, 20 → 23k, 2 → 13k, 7 → 10.5k, 21 → 5k, 24 → 4k, 10 → 2.7k.
- **PackageType** (por pieza y por orden): Box, Envelope, Package, Invoice, Drum, Pallet, Other, Cheque, Tower, Suitcase, Cart, 5 GAL; en 10030 además Oversize, Bumper, Bonete, Dron, Battery Core/Warranty, Core, Warranty, Fridge, Multi Package, Tire, Oil. En piezas de 10029 domina `Drum` (5; 1.46M, es el ítem AxisCare), `Box` (1) y `Invoice` (4).
- **CODType** en la orden es texto libre: `None` 1.2M, `Cash or check` 43k, `Cash Only` 14k, `Check` 1.8k, `Cash` 205, `Credit Card` 23. 90,432 órdenes con monto COD > 0. El catálogo `CODType` (Cash, Check, Money Order, Mixed, ATH Móvil, Credit Card, Authorized, Return, Cancelled…) se usa en los eventos de cobro.

---

## 3. Mapeo propuesto hacia Teikem

| Colavora | Teikem | Notas |
|---|---|---|
| `Company` 10029 / 10030 | `Tenant` (uno por compañía) | Ver duda D1. 10036 no se migra. |
| `Customers` (con órdenes, sin copias de prueba) | `Client` (`Code` = `Sender`, `Name` = `SenderName`) + `Location` de recogido por defecto (dirección del cliente) + `ClientContact` (`Contact`) + `ContactPoint` (teléfono, fax, `NotifyEmail`) | `Invoice`, `PhotoOnDelivery`, `SignatureOnDelivery`, `DuplicateInvoice` → banderas del cliente (`AutoPrintInvoice`, `AutoPrintLabels`, `AllowDupInvoice`); lo que no exista en Teikem se anota en el reporte. `AssignPk` → chofer de recogido por defecto (no existe en Teikem: duda D5). |
| `Customers_Divisions` + `Shipments.Division` | Campo personalizado de la orden (`CustomFieldDefinition` «División» con opciones) | Duda D6. |
| `Consignees` | `Location` del cliente, tipo DELIVERY (`Code` = `ConsigneeNumber`, `Name` = `CompanyName` o `ContactName`, `ContactName`, lat/long) + `ContactPoint` (teléfono, correo) | Solo los que tengan número único por cliente; los demás según duda D7. `Zone` 01–15 → `DeliveryNotes` o zona de despacho (D12). |
| `Shipments` | `TransportOrder` + 2 `OrderStop` (recogido con el snapshot del remitente; entrega con el snapshot del consignatario) + `CargoLine` + `OrderPiece` + `OrderReference` | `Waybill` → `WaybillNumber` y `OrderNumber` (formato propio del cliente: `ClientAssignsOrderNumber`); `ReferenceNo` → `ClientInvoiceNumber` (AxisCare) o referencia; `ReadyDate` → `RequestedDate`; `DueDate` → `PromisedDate`; `CODType`/`CODAmount` → `CodTypeLookupId`/`CodAmount`; `OrderNotes` → `Notes`; `CreatedUserID`/`CreatedDate` → `CreatedBy`/`CreatedAtUtc` (hora local PR → UTC); `Qty` → `TotalPieces`; `AssignDL` → chofer en la asignación (`Trip`/`DriverTrip` o `DeliveryAttempt.DriverId`, duda D5); `Barcode` (lista separada por comas) ya está pieza a pieza en `Shipments_Pieces`. |
| `Shipments.Status` | `StatusCodeId` (OrderStatus) | Ver tabla de estatus abajo. Para los 4,600 con basura, el último evento de estatus manda. |
| `Shipments_Pieces` | `OrderPiece` (`PackageNumber` = `Barcode`, ≤ 40) agrupadas en `CargoLine` por tipo de paquete (`PackageTypeLookupId`, `Description` = `Comments`) | Piezas tipo `Invoice` (4 / 28): no son piezas; su barcode es el número de factura → `ClientInvoiceNumber`. Duda D8. |
| `Shipments_Events` (Created, Pending, PickedUp/Pickup, Inbound, Unassigned, AssignDL, Outbound/In Transit, Delivered, Completed, Roundtrip, Canceled, *_ReOpen) | `EntityStatusHistory` con `ChangedAtUtc` = fecha original y `ChangedBy` = usuario migrado | Las transiciones se escriben directo (no por `StatusService`, que pondría la fecha de hoy y dispararía avisos). |
| `Shipments_Events` Comment/BOL Comment con razón 306, 307, 315, 360, 335, 340, 04–11 | `DeliveryAttempt` (fallido, `FailureReasonLookupId` = razón, `DriverId` = `AssignDL` de ese momento, `AttemptedAtUtc`) | Las razones van al catálogo `DeliveryFailureReason` (por tenant). |
| `Shipments_Events` Delivered (312, 3, 13) | `DeliveryAttempt` (`Delivered` = true) + `EntityStatusHistory` → Entregada | |
| `Shipments_Events` Comment/Llamada/Edit/PWBack/Price | Comentario en el historial (`EntityStatusHistory.Comment` sin cambio de estatus) o se descartan | Duda D9: ¿se conserva la bitácora completa (9.8M) o solo estatus + intentos? |
| `Shipments_POD` | `DeliveryProof` (`RecipientName` = `PODName`, `DeliveredAtUtc`, lat/long, `Source` = DRIVER_APP, `SignatureKind` = RECIPIENT) | **Teikem no guarda hoy la imagen de la firma ni la foto** (`DeliveryProof` no tiene campo binario; `ProofOfDelivery`/`ProofOfDeliveryPhoto` existen en el esquema pero no las usa el Lote 118). Duda D10. |
| `Users` SecGroup 4 | `Driver` (`EmployeeCode` = código del username, `FullName`) sin usuario de login | Duda D11: ¿se crean también como usuarios de la app de choferes (invitación)? |
| `Users` SecGroup 1–3 (staff) | `ApplicationUser` + `UserTenant` + rol, **por invitación** (sin contraseña) | Solo activos. Duda D11. |
| `Users` TypeID 1 + `UsersSender` | `PortalUser` del cliente, por invitación | Duda D11. |
| `Zip` | `DispatchZone` METRO / ISLA con sus ZIP (`DispatchZoneMember`) + `DriverZone` (chofer por defecto) | Duda D12. |
| `Customers_Service_Price` | `Contract` + `RateCard`/`RateComponent`/`RateTier` por cliente (servicio × paquete × rango de ítems; `CodFee`, `DispatchFee`) | Duda D13. Alternativa: solo congelar el precio en cada orden (`QuotedAmount`) desde los eventos Price. |
| `ServiceType`, `PackageType`, `CODType`, `Shipments_Reasons` | `LookupCode` (ServiceType, PackageType, CodType, DeliveryFailureReason) con sus etiquetas es/en de `TypeCode` | Los que ya existen en Teikem (STANDARD, SAMEDAY, BOX, ENVELOPE, PALLET, CASH, CHECK…) se reutilizan; los demás se crean por tenant (`LookupCodeOverride`) o globales (D14). |

### 3.1 Estatus: Colavora → Teikem (flujo decidido por Luis el 2026-10-17)

| Colavora (estatus o último evento) | Teikem `OrderStatus` | Observación |
|---|---|---|
| Created, Pending, Import | **SUBMITTED** (Sometida) | Duda D3 sobre las 382k Pending viejas. |
| PickedUp, Pickup, AssignPk + Manifest-Pk | **PICKUP** (Recogida) | |
| Inbound, Unassign/Unassigned | **UNASSIGNED** (Sin asignar) | |
| Assigned, AssignDL, Manifest-DL | **ASSIGNED** (Asignada) | |
| Our for Delivery, Outbound, In Transit | **IN_TRANSIT** (En tránsito) | |
| Comment con razón de no entrega (306, 307, 315, 360, 04–11) | **FAILED** (Intento fallido) + `DeliveryAttempt` | La orden vuelve a UNASSIGNED con el siguiente Inbound. |
| Delivered, Completed, Delivered_ReOpen→cerrada, PWBack* | **DELIVERED** (Entregada) | `Completed` = Entregada + PWBack: se marca en una referencia/campo «Paperwork back» (D4). |
| Delivered con servicio PICK UP (3, 21, 11, 24) | **IN_WAREHOUSE** (En almacén) | Flujo de pick up de Teikem. Duda D4b. |
| Roundtrip (estatus/evento) | **RETURNED** (Devuelta) | Teikem crea orden de vuelta; en migración solo se marca. |
| Canceled | **CANCELLED** (Cancelada) con razón `Cancelled By User In Order Page` | |
| Partial Delivery | DELIVERED + reclamación | No hay órdenes con ese estatus hoy; solo eventos 102/12 «Devolución parcial» (D4). |

---

## 4. Volumen y forma de ejecutar

- Filas destino estimadas (todo el histórico): 1.26M órdenes + 2.5M paradas + ~3.5M líneas/piezas + 4.7M piezas + 6–10M historial + 0.5M intentos ≈ **20 millones de filas**. Por EF en lotes de 1,000 órdenes: **varias horas**; por eso el importador debe ser **reanudable por rangos de `ShipmentID`** y tener `--dry-run` con reporte.
- Propuesta (sigue el patrón del Lote 10, `import-legacy`): un verbo CLI nuevo **`import-colavora <config.json> [--dry-run] [--desde <ShipmentID>] [--hasta <ShipmentID>] [--solo catalogos|clientes|consignatarios|usuarios|ordenes|pod]`** en `src/Teikem.Api` + `src/Teikem.Infrastructure/Migration/Colavora/`, con un `ColavoraReader` (solo `SELECT` sobre las dos bases por `ConnectionStrings:ColavoraCourier` y `ColavoraCourierLogin`) y un escritor por EF que respeta auditoría, `PublicId`, filtro de tenant y catálogos por FK. Nada de T-SQL de datos (regla del proyecto). Un JSON por compañía en `docs/migracion/colavora/import.advance.json` e `import.delapaz.json`. Reporte `.md` + `.csv` de rechazos fuera del repo (`F:\Download\TeikemMigracion\colavora\`).
- Fechas: Colavora guarda hora local de Puerto Rico (sin zona). Se convierten a UTC con `America/Puerto_Rico`.
- Idempotencia: la orden se identifica por (tenant, `Waybill`) y el evento por `ShipmentEventID` guardado en `EntityStatusHistory.Comment`/referencia; volver a correr no duplica.

---

## 5. Lo que NO se migra (propuesta)

`AL*`, `Ontime360_Shipments`, `Ontime_Colavora_Users_Mapping`, `Imp_*`, `Temp_*`, `Shipments_Manifests` (2023–2024, ya no se usa), `Drivers` (vacía), `ApiIdempotency`, `ApiErrorLog`, `Gs1*`, `Consignees_Walgreens`, `PWBack`, `AppConfig`, `CompanyConf` (rutas de logos), impresoras y Shopify de `Customers_Configuration`, los 12,340 eventos con `ShipmentID` no numérico, los clientes de prueba (`-DEV`, `-PCK`, `SAMPLE:`) y la compañía 10036.

---

## 6. Dudas para Luis (hay que cerrarlas antes de programar)

- **D1 — Compañías destino.** ¿**Advance Logistics (10029)** es el mismo tenant que **Advance Depot** (el que ya está en producción) o un tenant nuevo «Advance Logistics»? ¿**De La Paz Transport (10030)** es tenant nuevo? ¿Mismo administrador (`teikem+admin@cerevelo.com`)?
- **D2 — Horizonte.** ¿Todo el histórico desde 2023-08 (1.26M órdenes, 9.8M eventos, varias horas de carga) o solo las órdenes **abiertas** más los últimos N meses cerradas (por ejemplo 12)? Esto cambia el tamaño del trabajo más que nada.
- **D3 — Las 381,819 `Pending`.** En De La Paz hay 228k de 2025 que nunca se cerraron. ¿Se migran como **Sometida** (quedan abiertas en Teikem), se cierran como **Cancelada** con razón «sin movimiento en Colavora», o se dejan fuera si son anteriores a cierta fecha?
- **D4 — Completed vs Delivered.** Teikem no tiene «Completada». ¿Entregada + una marca «paperwork back conciliado» (campo personalizado o referencia) sirve? ¿Y «Devolución parcial» (eventos 102/12) → Entregada + reclamación, o Devuelta?
- **D4b — Pick up entregados.** Las órdenes de servicio PICK UP que Colavora marca Delivered/Completed: ¿en Teikem terminan **En almacén** (flujo de pick up) o **Entregada**?
- **D5 — Choferes en la orden.** Teikem registra el chofer por viaje/ruta (`Trip`, `DriverTrip`) e intento (`DeliveryAttempt.DriverId`). ¿Basta con el chofer en cada intento y en el historial (texto), o quieres **un `Trip` por manifiesto** (chofer + fecha) para reconstruir las rutas del pasado? Lo segundo son ~1.5M viajes más.
- **D6 — Divisiones** (GLT, GLC, OXY, GAM… solo AxisCare): ¿campo personalizado de la orden, referencia, o clientes distintos (`AXISCARE-OXY`)?
- **D7 — Consignatarios sin número** (38,669 de 44,485) y las 797k órdenes que no casan. Propuesta: crear `Location` por (cliente, número) cuando hay número; para el resto, la orden conserva el snapshot y **no** se crea consignatario. Alternativa: deduplicar por (cliente, teléfono, dirección). ¿Cuál?
- **D8 — Piezas tipo `Invoice`** (487k en 10029, 279k en 10030): ¿se descartan como pieza y su código pasa a `ClientInvoiceNumber`? ¿Y el campo `Comments` de la pieza AxisCare (descripción del equipo) → descripción de la línea de carga?
- **D9 — Bitácora.** ¿Migrar los 9.8M eventos completos como historial (estatus + comentarios + precios + PWBack) o solo los que cambian estatus, los intentos y los comentarios humanos (≈ 3.5M)?
- **D10 — Firmas y fotos de los POD** (≈ 1 GB en base64, 25k comprobantes desde 2024-09). `DeliveryProof` no guarda imágenes hoy. Opciones: (a) agregar `DeliveryProofFile` (archivo en disco/blob + hash) en el `update.sql` y migrarlas; (b) migrar solo nombre, fecha y GPS; (c) exportarlas a carpeta por waybill fuera de la base.
- **D11 — Usuarios.** Las contraseñas no se pueden migrar (hash propio / Auth0). Propuesta: choferes activos → `Driver` (y usuario de la app por invitación), staff activo → usuario por invitación con rol equivalente (`SecAdmin` → administrador de la compañía, resto → operador), usuarios de portal (`TypeID` 1 + `UsersSender`) → `PortalUser` por invitación. ¿Solo activos (`Active` = 1: 163 de 252)? ¿Se crean ya o solo los choferes?
- **D12 — Zonas.** `Zip` da METRO/ISLA y chofer por ZIP; los consignatarios traen zona 01–15. ¿Zonas de despacho `METRO`/`ISLA` con `DriverZone`, y la zona 01–15 como nota, o ignorar?
- **D13 — Tarifas.** `Customers_Service_Price` (567 filas) → ¿contratos y tarifas por cliente en Teikem, o solo el precio congelado por orden desde los eventos `Price - *` (para 2024-09 en adelante)? En Colavora no hay facturas: no hay facturación que migrar.
- **D14 — Catálogos.** Servicios, paquetes y razones que no existen en Teikem (`NEXT DAY ISLAND`, `WALKEDIN`, `Drum`, `Bumper`, `PATIENT- DECEASED`…): ¿se crean **por tenant** (override) o globales en el `update.sql`?
- **D15 — Base destino y orden.** Primero contra la base local (`db-init` + `import-colavora --dry-run` + carga), luego producción con respaldo. ¿Se migra a producción en una sola corrida de corte (parar Colavora, migrar, arrancar Teikem) o en dos (histórico antes, delta de los últimos días el día del corte, con `--desde`)?
---

## 7. Decisiones de Luis (2026-10-18) — cierran las dudas de §6

| Duda | Decisión |
|---|---|
| D1 | **Dos tenants nuevos**: Advance Logistics (10029) y De La Paz Transport (10030). 10036 no se migra. |
| D2 | **Todo el histórico** desde 2023-08 (1.26 M órdenes, 9.8 M eventos). |
| D3 | Las `Pending` con **más de 5 días** (contra la fecha de la migración) se cierran como **Entregada**; las de 5 días o menos quedan **Sometida**. |
| D4 | `Completed` = **Entregada + marca**: la marca son los campos del Lote 126 (`InvoiceProcessedAtUtc`, `InvoiceProcessedBy`, `InvoiceProcessRunId`): el «paperwork back» de Colavora ES el procesamiento de la factura. |
| D4b | Los PICK UP entregados terminan **Entregada** (estas compañías no son de almacén). |
| D5 | **Un viaje (`Trip`) por manifiesto** (chofer + fecha) para reconstruir las rutas; además el chofer en cada intento. |
| D6 | División de AxisCare → **campo personalizado** de la orden. |
| D7 | Consignatarios: **todos tal cual** (una `Location` por cada fila de `Consignees`, tengan número o no). |
| D8 | La pieza tipo `Invoice` no es pieza: su código → `ClientInvoiceNumber`; el evento PWBack → campos de factura procesada (Lote 126). La descripción del ítem AxisCare (`Shipments_Pieces.Comments`) → descripción de la línea de carga. |
| D9 | **Todos los eventos** van al historial: los de estatus como transición (adaptada al flujo de Teikem, §3.1); los demás (PWBack, Price, manifiestos, Llamada, Edit, Import, Comment) como **comentario del historial** con fecha, usuario y texto, sin cambiar el estatus. PWBack además llena los campos de factura procesada y `Price - *` el precio de la orden. |
| D10 | Firmas y fotos: **archivos en disco**. Se agregan `SignaturePath` y `PhotoPath` a `DeliveryProof` (update.sql), las imágenes se guardan como archivos en una carpeta del servidor y la web las muestra en el comprobante. |
| D11 | Se crean **choferes** (activos e inactivos; `Driver` + usuario de la app por invitación) y **staff** (activos e inactivos, por invitación; `SecAdmin` → administrador, resto operador). Los inactivos nacen suspendidos para que el historial muestre su nombre. **No** se crean usuarios de portal. |
| D12 | **Zonas de ruta por ZIP**: zonas de despacho por chofer/ruta (01–15 en De La Paz, códigos de chofer en Advance) con sus ZIP y su chofer asignado. |
| D13 | **Las dos cosas**: contratos y tarifarios por cliente desde `Customers_Service_Price`, y el precio congelado en cada orden histórica desde los eventos `Price - *`. |
| D14 | Catálogos faltantes **por compañía** (override por tenant). |
| D15 | **Una sola corrida** el día del corte (parar Colavora, migrar, arrancar Teikem). **Yo solo escribo los scripts; no corro nada contra producción.** |

### 7.1 Ajustes del 2026-10-19 (Lote 129 y regla P11)

- **Piezas (P11, dictado de Luis):** en el archivo de AxisCare no todo es pieza. Hallazgo en los datos: el «código de barras» de la pieza tipo 5 `Drum` es el **código del producto** (`310200100` aparece en hasta 304 órdenes el mismo día); la pieza real es la caja tipo 1 `Box` (label de 20 dígitos que lleva adentro la referencia de la orden; 3 % se repite entre órdenes por ida y vuelta) y el tipo 4 `Invoice` es la factura. Decisión: no se agrega campo de label; el label es `PackageNumber` (único por compañía). La migración recibe `options.descriptionPackageTypes` (Advance: `[5]`): esos renglones van como descripción y cantidad de la línea de carga, sin pieza; la factura al número de factura; el resto son piezas (label repetido → sufijo `~id`). De La Paz: todo es pieza (`fecha+cliente+secuencia+pieza`, única salvo órdenes creadas dos veces).
- **Zonas (Lote 129):** `Zip.Zone` (METRO/ISLA) → dos niveles (`ZoneGroup`) con chofer por omisión; cada zona de ruta recibe el nivel mayoritario de sus ZIP; la orden migrada guarda `DeliveryZoneId` por el ZIP del consignatario. Los servicios ISLAND se migran tal cual (D14).
- **Comprobantes (D10 + Lote 127):** la firma y la foto de cada POD se registran en el almacén de archivos de Teikem (`StoredFile` cifrado, dueño = el comprobante) y la web las muestra en el comprobante; `options.podFilesDir` es solo una exportación opcional a disco.
