# Lote 225 — Pedido de vendedores, P5: QuickBooks Desktop por archivo IIF (backend, 2026-11-07)

Parte P5 de [pedido-vendedores-plan-desarrollo.md](pedido-vendedores-plan-desarrollo.md) (§1.1 pestaña QuickBooks, §1.4 Ajustes → QuickBooks, §1.7 avisos/Pulso/correo,
§2, §3 fila P5, §4.1 fila P5) con lo decidido en [pedido-vendedores-formsite-analisis-2026-11-06.md](pedido-vendedores-formsite-analisis-2026-11-06.md) §9c
(séptima a novena ronda: **una sola marca «en QuickBooks»**, exportar saca siempre todas las no marcadas, tres permisos). Solo backend; la pestaña y Ajustes → QuickBooks
van en su parte de frontend.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Datos (`logistica-db-update.sql`, sección 2026-11-07 «Lote 225») | `Invoice.QuickBooksAt` / `QuickBooksBy` (la marca: cuándo y quién; NULL = por pasar), `Invoice.QuickBooksExportedAt` (última vez en un archivo; **no** es la marca), índice filtrado `IX_Invoice_QuickBooksPending`; `Client.QuickBooksSentAt`, `Product.QuickBooksSentAt`; en `Tenant` las cuentas (`QuickBooksReceivableAccount`, `…SalesAccount`, `…CostAccount`, `…InventoryAccount`, `…TaxAccount`, 159), los ítems (`QuickBooksTaxItem`, `QuickBooksShippingItem`, 31) y `QuickBooksTermsJson` (2000). Idempotente (`COL_LENGTH`, `IF NOT EXISTS`), probado dos veces seguidas contra SQL Server 2022. |
| Permisos | `sales.quickbooks.view`, `sales.quickbooks.mark`, `sales.quickbooks.export` (categoría BILLING) en `PermissionCatalog`, `PermissionDisplayOrder` y el SQL; a los roles **ya clonados** `TenantAdmin` y `SalesOffice` una sola vez (marca `L225_SALES_QUICKBOOKS`); la plantilla «Oficina de ventas» los trae de fábrica. Los tres implican `sales.view` (la pestaña vive en Órdenes de venta). Conteo de permisos en las pruebas: 153 → 156. |
| `Domain/Sales/QuickBooks.cs` | `QuickBooksRules` (valores por omisión, límites de QuickBooks, mapa de términos, `Prorate` al centavo, mensajes exactos) y `QuickBooksIifWriter` **puro**: `!INVITEM` (productos nuevos) → `!CUST` (clientes nuevos) → `!TRNS/!SPL/!ENDTRNS` (facturas). Tabulador, CRLF, fechas MM/DD/AAAA, montos con punto; TRNS = total a cuentas por cobrar, cada SPL el ingreso en negativo (cantidad negativa), IVU en una SPL con el ítem de impuesto (`EXTRA = AUTOSTAX`): cada transacción cuadra a cero. Archivo en Windows-1252 (lo que no cabe sale «?»). |
| Kits | No viajan como ítem: cada línea de kit sale **desglosada por componente** (foto `SalesOrderLineComponent` de la orden; si no la tiene, la receta del catálogo) con el monto prorrateado por precio de lista × cantidad por kit, por mayor residuo: **cierra al centavo** (OFERTA1 × 2 = 83.88 → 70.46 + 13.42). Precio unitario a 5 decimales. Sin precios de lista: a partes iguales. |
| `QuickBooksService` | «Por pasar» = factura **emitida** (ISSUED, OVERDUE o PAID) con al menos una línea de orden de venta o su cargo de envío, sin la marca. Lista (sin paginar: «Exportar» las lleva todas), exportar (todas siempre; anota `QuickBooksExportedAt`, no marca), confirmar «sí, ya están» (solo las que salieron en algún archivo), marcar a mano (una, varias o `all`), quitar la marca, histórico paginado (quién y cuándo), ajustes, cifra para el aviso/panel/atención. Al marcar o confirmar, los clientes y productos de esas facturas quedan como mandados (`QuickBooksSentAt`). |
| `QuickBooksController` (`/api/v1/sales-orders/quickbooks`, módulo SALES_ORDERS) | `GET` (lista, view\|mark\|export), `POST export` (export), `POST confirm` (export), `POST mark` / `POST unmark` (mark), `GET history` (view\|mark\|export), `GET settings` (view o admin.tenant), `PATCH settings` (admin.tenant). |
| Avisos | Evento `SALES_QUICKBOOKS_PENDING` («Facturas por pasar a QuickBooks: {count}», variables count/exported/oldest, opcional). Trabajo programado `SALES_QUICKBOOKS_PENDING` diario a las **07:00** de la compañía: lo publica uno al día (clave `QBP-AAAAMMDD`) y solo si hay algo. «Necesita tu atención»: una fila resumen `SALES_QUICKBOOKS_PENDING` (sales.quickbooks.view; warn, danger si la más vieja pasa de 7 días; abre `/sales-orders` con `tab=quickbooks`). Panel Ventas del Pulso: `invoicesToQuickBooks` con la ruta `/sales-orders?tab=quickbooks` solo para quien tiene sales.quickbooks.view (si no, null). |
| Contrato | `Contracts/QuickBooksContracts.cs`; `web-app/openapi.json` (solo adiciones: 7 rutas, 14 esquemas, tomados del swagger del API levantado en Development sin llave de Brevo, sobre una base local aparte); `schema.d.ts` regenerado en `web-app`, `app-almacen` y `app-cliente` (solo adiciones). |

## Mensajes (para el manual y la FAQ)
| Caso | HTTP | Mensaje |
|---|---|---|
| Exportar sin nada por pasar | 422 | «No hay facturas por pasar a QuickBooks.» |
| Marcar/confirmar sin facturas | 400 | «Indique las facturas (invoiceIds) o pida todas (all = true).» |
| Más de 1000 en una llamada | 400 | «No se pueden marcar más de 1000 facturas en una sola llamada; use «todas».» |
| Una que no existe o no es de venta emitida | 400 | «Alguna de las facturas indicadas no existe o no es una factura de venta emitida.» |
| Confirmar una que nunca salió en un archivo | 422 | «La factura {número} no ha salido en ningún archivo de QuickBooks; para marcarla a mano use «Marcar como en QuickBooks».» |
| Sin cuerpo | 400 | «El cuerpo de la solicitud es obligatorio.» |
| Ajustes: cuenta larga / ítem largo / término largo | 400 | «El nombre de una cuenta de QuickBooks no puede pasar de 159 caracteres.» · «El nombre de un ítem de QuickBooks no puede pasar de 31 caracteres.» · «El nombre de un término de QuickBooks no puede pasar de 31 caracteres.» |
| Ajustes: término que no existe | 400 | «El término de pago «{código}» no existe en el catálogo de términos de pago.» |
| Avisos de la lista (no bloquean) | — | «El nombre del cliente «…» pasa de 41 caracteres: QuickBooks no lo acepta como nombre de cliente.» · «El SKU «…» pasa de 31 caracteres: QuickBooks no lo acepta como nombre de ítem.» · «El término «…» no tiene nombre en QuickBooks (Ajustes → Operación → QuickBooks): esas facturas salen sin término.» |

## Decisiones (para que Luis las ratifique)
1. **Qué es «por pasar»**: la factura **emitida** (también vencida o pagada), no el borrador ni la anulada; y solo si trae algo de ventas (línea de orden de venta o cargo de envío). Una factura de solo transporte no aparece.
2. **La factura sale completa**: si la misma factura del cliente trae también flete u otros cargos de transporte, esas líneas salen con el **código del tipo de cargo** como ítem (`FREIGHT`, `HANDLING`…), que debe existir en QuickBooks como ítem de servicio. El cargo de envío sale con el ítem de Ajustes (por omisión `Shipping`).
3. **El IVU va como una sola línea** por factura (el IVU total de Teikem) con el ítem de impuesto de Ajustes (por omisión `IVU`) a la cuenta `Sales Tax Payable`; cada línea lleva su Y/N de gravable. QuickBooks no recalcula: el monto es el de Teikem.
4. **Nombre del cliente en QuickBooks** = el campo «Código QuickBooks» de la migración (es el nombre exacto del cliente en QuickBooks) o, si no lo tiene, el nombre del negocio. Un cliente con ese campo **nunca es nuevo**. El número de cliente de Teikem va en `NOTE`, la razón social en `COMPANYNAME`, el SURI en `RESALENUM`, la dirección postal (BILLING) como «facturar a» y la física (CORPORATE) como «enviar a».
5. **Nombres limpios**: tabuladores y saltos de línea → espacio; comillas dobles → simples; en nombres de cliente e ítem los dos puntos → guion (en QuickBooks «A:B» es subcliente/subítem); las cuentas conservan los dos puntos (`Sales:Monitors`). La lista y «ver datos» muestran el nombre **tal como sale en el archivo** (`quickBooksCustomer`, `quickBooksName`).
6. **El archivo viaja en JSON** (base64 + nombre `teikem-quickbooks-AAAAMMDD-HHmm.iif`) junto con la lista para «¿Ya importaste estas N?»: una sola llamada da el archivo y lo que hay que confirmar. Exportar anota la fecha en cada factura (sale en la bitácora de la factura) y nunca marca.
7. **Clientes y productos nuevos**: `includeNew` (por omisión sí) los mete en el archivo; nuevos = de esas facturas y nunca marcados/confirmados. Quitar la marca a una factura **no** los vuelve nuevos. El kit nunca se manda: sus componentes sí.
8. **Quién ve qué**: la lista y el histórico los ven los tres permisos (quien marca o exporta necesita verlas); el aviso, el panel y «Necesita tu atención» solo `sales.quickbooks.view`. Ajustes: los lee view o admin.tenant y los cambia admin.tenant.
9. **Términos**: mapa código del catálogo `PaymentTerm` → nombre en QuickBooks (`{"NET30":"Net 30"}`); sin nombre, la factura sale sin término y la lista avisa. Un nombre vacío quita el término del mapa.
10. **«El correo de la mañana»**: no hay un resumen diario de compañía para el personal; el aviso es el evento `SALES_QUICKBOOKS_PENDING` que publica el trabajo diario de las 07:00 (la hora se cambia en Sistema → Trabajos programados). Como los demás avisos de ventas, **la regla (a quién y por qué canal: campana, correo) la arma el administrador** en Reglas de aviso: no se siembra ninguna. Uno al día y solo si hay algo.
11. **Cuentas por omisión**: `Accounts Receivable`, `Sales`, `Cost of Goods Sold`, `Inventory Asset`, `Sales Tax Payable`; una sola cuenta de ventas por compañía (sin cuenta por categoría por ahora).

## Cómo se probó
- `QuickBooksIifWriterTests` (puras, 9): archivo exacto de una factura con IVU, término y vencimiento; sin IVU ni término; productos → clientes → facturas con las cuentas de Ajustes; kit OFERTA1 desglosado (70.46 + 13.42) sin el kit como ítem; cada transacción cuadra a cero; prorrateo al centavo en 500 casos al azar, a partes iguales, con signo; caracteres especiales (comillas, tabuladores, saltos, dos puntos, acentos); Windows-1252 y «?»; mapa de términos.
- `QuickBooksExportTests` (servicio, InMemory, 9): solo emitidas de venta; todas las no marcadas salen siempre y el aviso «ya salieron»; confirmar (nunca exportada = 422, sin medias) y clientes/productos solo la primera vez; marcar a mano una/todas, quitar la marca, mensajes exactos; kit por componentes; cliente migrado con su nombre de QuickBooks; ajustes (cuentas, ítem de IVU, términos, «» = por omisión) en el archivo; permisos; «Necesita tu atención», panel Ventas, trabajo de la mañana (uno al día) y el SQL de la sección.
- Contra **SQL Server 2022 local** (base aparte `TeikemL225`, `db-init` dos veces, API en Development **sin llave de Brevo** en :5225): lista, exportar (archivo revisado a ojo), exportar otra vez (aviso), marcar sin nada (400), confirmar, histórico con nombre de quien marcó, quitar la marca, marcar todas, ajustes (GET/PATCH, término inválido 400), panel del Pulso y «Necesita tu atención». La sección del SQL corrida dos veces seguidas con `sqlcmd`: sin errores.
- `dotnet build Teikem.sln` (0 errores) y `dotnet test Teikem.sln` completo: ver el reporte de la parte.

## Ítems a renombrar en QuickBooks (pendiente de confirmar con los datos reales)
El archivo usa **el SKU de Teikem como nombre del ítem**, así que en QuickBooks el ítem tiene que llamarse exactamente igual (QuickBooks conserva el historial al renombrar). Según el análisis (§9c) son 7; con lo que hay en el repositorio solo se pueden nombrar así — **antes de la primera exportación hay que confirmarlos contra el `ItemList.CSV` y los SKU de Solutions en producción** (en `migracion-depot-solutions-plan.md` los guantes quedaron como `NECH1001` sin espacio ni guion: el nombre en QuickBooks debe ser el SKU que de verdad tenga Teikem):

| En QuickBooks hoy | Renombrar a (SKU de Teikem) |
|---|---|
| `NECH 1001` (y, si aplica, `NECH 1002`–`NECH 1004`) | `NECH-1001` … (o `NECH1001` …: el SKU real) |
| `24081028` | `24-081028` |
| `24083225` | `24-083225` |
| `PD-23-36-45gms15/10` (underpad) | `PD-23-36/15/10` |
| `PD-23-36-45gms3/50` (underpad) | el SKU de Teikem (por confirmar) |
| `Boost Immune` | el SKU de Teikem (por confirmar) |

## Pendiente / para otras partes
- **P8 (datos de Solutions), importante**: los productos de Solutions vinieron de QuickBooks y hoy no tienen `QuickBooksSentAt`, así que la primera exportación los traería a todos como «nuevos». Script idempotente sugerido (lo corre Luis): `UPDATE dbo.Product SET QuickBooksSentAt = SYSUTCDATETIME() WHERE TenantId = <Solutions> AND QuickBooksSentAt IS NULL AND IsActive = 1;` (los clientes migrados ya cuentan como mandados por su «Código QuickBooks»). Ajustes → QuickBooks de Solutions (cuentas reales, ítem de IVU, términos `NET15/20/30/45`, `COD`…), y una regla de aviso de `SALES_QUICKBOOKS_PENDING` para el rol «Oficina de ventas» (campana + correo). Recomendar que la primera exportación sea con `includeNew = false` hasta confirmar los nombres.
- Frontend: pestaña QuickBooks en Órdenes de venta (lista, «ver datos», exportar con la pregunta de nuevos, «¿Ya importaste estas N?», marcar, histórico, aviso «N de estas ya salieron el …» y los avisos de la lista), Ajustes → Operación → QuickBooks, claves i18n de `SALES_QUICKBOOKS_PENDING` (atención y evento) y de `invoicesToQuickBooks` del panel.
- La marca «cliente nuevo / producto nuevo» **en la orden de venta misma** no se agregó al DTO de la orden: está en la lista de QuickBooks (por factura). Si la pantalla de la orden la necesita, se suma después.
- Manual (capítulo 18 y el de Ajustes) y FAQ: con los mensajes de arriba (en P8, como las demás partes).

## Frontend (web-app, 2026-11-07)
| Pieza | Qué es |
|---|---|
| `features/sales/QuickBooksTab.tsx` | Pestaña **QuickBooks** de Órdenes de venta (`/sales-orders?tab=quickbooks`): lista de facturas por pasar (fecha, factura con sus órdenes, cliente —y su nombre en QuickBooks si es otro—, total, «Cliente nuevo» / «Producto nuevo» con «ver datos», «Salió el …»), buscador, aviso «N de estas ya salieron el …» y los avisos del servidor. **Exportar a QuickBooks** (export): pregunta «Incluir los clientes y productos nuevos» (marcado por omisión), baja el `.iif` con los bytes exactos del base64 y, en la misma pantalla, «¿Ya importaste estas N?» con la lista marcada (desmarcar = sale en el próximo archivo), «Sí, ya están (N)» → `confirm`, «Bajar el archivo otra vez», «Todavía no». **Marcar como en QuickBooks** (mark): casillas + «Marcar como en QuickBooks (N)», acción por fila y «Marcar todas» (con confirmación, `all = true`). **Ver las ya pasadas** (`&qb=history`): histórico paginado por el servidor (filtro de día de la marca y buscador al API; Exportar saca todo lo filtrado con `fetchAllPages`), con quién y cuándo, y «Quitar la marca» (mark). |
| `features/system/settings/SettingsQuickBooksSection.tsx` | Ajustes → Operación → QuickBooks: las cinco cuentas, ítem de IVU y de envío, y la tabla chica término de pago → nombre en QuickBooks. Guarda solo lo que cambió (vacío = por omisión; término vacío = sin término); `maxLength` 159/31/31 y los mismos mensajes del servidor. |
| `features/analytics/attention.ts` | Texto del aviso `SALES_QUICKBOOKS_PENDING` en «Necesita tu atención» (cifra, ya salieron en un archivo, la más vieja, días; «Revisar» abre la pestaña). La cifra del panel Ventas (`invoicesToQuickBooks`) ya tenía su texto. |

Decisiones del frontend:
1. **La pestaña se ve con cualquiera de los tres permisos** (`sales.quickbooks.view|mark|export`), no solo con view: el API deja leer la lista a quien marca o exporta (decisión 8) y sin la lista no podrían usar sus botones.
2. **Órdenes de venta guarda la pestaña en `?tab=`** (antes era estado local) para que el Pulso y el aviso abran QuickBooks directo; las demás pestañas no cambian de comportamiento.
3. «¿Ya importaste estas N?» va **en la pantalla** (un panel arriba de la lista), no en una ventana: se puede bajar el archivo otra vez y volver más tarde a la misma pregunta mientras no se salga de la pestaña. Si se sale, las facturas siguen por pasar con «Salió el …» y se marcan a mano o salen en el próximo archivo.
4. Marcar varias manda las elegidas por su id (en tandas de 1000); solo «Marcar todas» manda `all = true`.

Pruebas: `QuickBooksTab.test.tsx`, `SettingsQuickBooksSection.test.tsx`, `attentionQuickBooks.test.ts`.
