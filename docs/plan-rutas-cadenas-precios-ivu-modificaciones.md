# Plan: rutas, cadenas, precios especiales, IVU con documentos, modificación de órdenes e importación de compras

Pedido de Luis (2026-11-24), hablado con Solutions. **Versión 3**: incorpora las respuestas de Luis a las dos rondas de preguntas y las verificaciones hechas en el código. Este documento es el **encargo para otra sesión**: qué hay hoy en el código, qué falta, el diseño decidido y el orden de los lotes. Lo escribió la sesión del video de Solutions después de leer el código (solo lectura; no se construyó nada).

> Reglas que aplican a todo (de `CLAUDE.md`): estructura y datos solo en una sección nueva AL FINAL de `Diseño/logistica-db-update.sql` (idempotente; estructura y seed están congelados); rama la que Luis indique (todo en `master` salvo orden contraria); nunca Brevo ni correo real al probar (API con `scripts/api-para-pruebas.ps1`); toda pantalla se cierra con Playwright MIRANDO la captura; tema claro/oscuro y es/en; manual funcional + FAQ + `docs/loteN-decisiones.md` por lote; permisos nuevos propagados a los roles ya clonados en la misma sección del SQL.

Marcas usadas abajo: **[DECIDIDO]** = lo dictó Luis; **[PROPUESTA]** = idea de la sesión que Luis no ha objetado.

---

## 1. Resumen de lo que se pide

1. **Rutas**: una ruta es un grupo de clientes para atender (normalmente un área, no necesariamente). Es el eje de la **agrupación de ventas**. Tiene personas asignadas (titular y apoyo) que **son usuarios**.
2. **Cadenas**: Walgreens es la cadena; Walgreens 1, 2 y 3 son los clientes. Sirve para **dar precio u oferta a la cadena completa**. Rutas y cadenas son cosas distintas: una ruta puede atender una o varias cadenas.
3. **Precios especiales** a un cliente o a una cadena, con vigencia; **gana siempre el precio más bajo**. **Ofertas** a clientes o cadenas, con **incluir** o **excluir**.
4. **IVU**: exención del **producto** (total, todo o nada, con vigencia: la fija el Estado, a veces por temporada) y exención del **cliente** (estatal, municipal o ambas, con vigencia), **atada a un documento** con fecha de vencimiento. Avisos de vencimiento configurables. Aviso al vendedor al escoger el cliente y en el resumen del pedido.
5. **Orden de venta editable**: los precios al crearla (con permiso) y todo después de creada (hasta el IVU), con un **registro de quién autorizó y cómo**. Empacada: se puede **reabrir el empaque** con permiso especial, o agregar con una **orden interna** vinculada.
6. **Importación de órdenes de compra definida por el usuario** (plantilla de columnas, como las otras importaciones) y **exportación de la orden de compra a QuickBooks**.

---

## 2. Lo que ya existe (no se rehace, se extiende)

| Pieza | Dónde | Estado |
|---|---|---|
| Rutas y representantes | `Teikem.Domain/Sales/SalesAnalysisEntities.cs`: `SalesRoute`, `SalesRep` (nombre, ruta opcional, **usuario opcional**), `SalesQuota` (cuota **por representante**), `Client.SalesRepId` | Lote 241. La ruta se llega por el representante; el cliente no tiene ruta propia |
| Listas de precios | `Sales/PriceLists.cs`: `PriceList` (`ValidFrom`, `ValidTo` opcional, `IsPublished`, `AppliesToAll`), `PriceListItem`, `PriceListClient`. Permiso `sales.pricelist` | Lote 221. Falta la cadena |
| Ofertas | `Sales/Offers.cs`: `Offer` (vigencia, publicada, `AppliesToAll`), `OfferItem`, `OfferClient` (solo **incluir** clientes). `OfferRules.Resolve`: entre precio negociado del cliente y oferta gana el más bajo; sin negociado, entre oferta y lista general gana el más bajo; luego el precio del producto | Lotes 229/231. Falta cadena y excluir. **Hoy un precio negociado más caro que la lista general gana igual** (ver §3.2) |
| Documentos del cliente | `Clients/ClientDocument.cs`: archivo (`StoredFile`), tipo (`LookupCode` `ClientDocumentType`: `MERCHANT_REGISTRATION`, `RESELLER_CERTIFICATE`, `OTHER`), notas | Lote 222. **No tiene número ni vigencia** |
| Impuestos | `Tax/TaxEntities.cs`: `TaxRate` con componentes (estatal, municipal) y vigencia; `Product.TaxCategoryLookupId` (TAXABLE/EXEMPT/ZERO_RATE, todo o nada, **sin vigencia**); `ClientTaxExemption` (tipo, número de certificado, `ValidFrom`, `ValidTo`, `Scope` ALL o COMPONENTS con `ComponentCodes`) | Lotes 55/237. La exención del cliente por componente ya existe, pero **no está atada a un documento** |
| Aviso al vender con exención vencida | `TaxService.cs` (~l. 342): avisa y cobra el impuesto | Solo al calcular; no hay aviso al escoger el cliente ni aviso previo |
| Editar una orden solicitada | `SalesOrderService.Request.cs` `ModifyLineAsync`: cantidad y precio de una línea existente, solo SOLICITADA y sin nada despachado; precio distinto exige `sales.price` | No agrega ni quita líneas, no edita impuesto, **no guarda quién autorizó ni cómo** |
| Motor de avisos | `Teikem.Domain/Notifications`: `NotificationRule` + canales + destinatarios, `NotificationOutbox`; `INotificationPublisher.PublishAsync(evento, valores, claveDeduplicación)`; trabajos programados (`IScheduledJob`, p. ej. `SalesBackorderScanJob`) | Es el que se usa |
| Importación de órdenes de compra | `Wms/PurchaseOrderImportRules.cs`, `PurchaseOrderImportService`, `PurchaseOrderImportController`: orígenes fijos `GENERIC` (columnas con nombre fijo) y `QUICKBOOKS`; simulación, bloqueos, no duplica | Lote 239. **El usuario no define el formato** |
| Plantillas de importación del usuario | `Orders/ImportTemplate.cs`: `Kind` (ORDER, CONSIGNEE), columnas por posición, delimitador, encabezado, valores fijos, variantes | Es el patrón a reutilizar para compras |
| Exportación a QuickBooks | Ventas › pestaña QuickBooks (archivo IIF de facturas) | Solo ventas; no hay de compras |
| Recolección, despacho y empaque | `SalesOrderService.Dispatch.cs`, `PickBatch` | **Verificar las etapas reales** (§7, punto 1) |

---

## 3. Diseño

### 3.1 Rutas (Lote A)

- **[DECIDIDO]** Ruta = grupo de clientes para atender; eje de la agrupación de ventas. El cliente lleva su ruta directa: `Client.SalesRouteId`.
- **[DECIDIDO]** Una ruta tiene **una o varias personas**: un **titular** y cero o más de **apoyo**. Tabla `SalesRouteAssignment` (ruta, usuario, papel TITULAR/APOYO, `ValidFrom`, `ValidTo`): guarda el historial; un solo titular vigente a la vez.
- **[DECIDIDO]** **El vendedor es un usuario, siempre**: titulares y apoyos entran al sistema. La asignación a la ruta es a un **usuario**; `SalesRep.UserId` pasa a ser obligatorio. Los representantes que hoy no tienen usuario (los de Solutions) se listan en la migración y el administrador les crea el usuario (hace falta el correo de cada uno); mientras no lo tengan, la ruta queda «sin titular» y el sistema lo señala. Los usuarios los controlan los administradores, como siempre.
- **[DECIDIDO]** **Cuota configurable por compañía**: por ruta o por persona (`SALES_QUOTA_BY` = ROUTE | REP en los ajustes). `SalesQuota` gana `SalesRouteId` (se llena uno u otro). El análisis de ventas agrupa siempre por ruta y, dentro, muestra la persona; la columna «cumplimiento» usa la cuota según el ajuste. No es complejo: es un ajuste y una columna.
- **[DECIDIDO]** Campo **«Vendedor preferido»** en la sección de ventas del perfil del cliente (`Client.PreferredSalesUserId`, usuario). **[DECIDIDO]** recibe los avisos de ese cliente **además de los vendedores de la ruta** (titular y apoyos). Es también el vendedor que se propone en la orden; vacío = el titular. La venta siempre se agrupa por la **ruta del cliente**, no por el vendedor preferido.
- La orden guarda copia de la ruta y del vendedor del día en que se solicitó, para que el historial no cambie cuando cambie la asignación.
- Migración: ruta del cliente = la del representante que tiene hoy; titular de la ruta = ese representante (si tiene usuario). `Client.SalesRepId` y `SalesRep` no se botan.
- **[DECIDIDO]** La ruta «Cadenas» de Solutions **no se toca**: es una ruta como cualquier otra. La cadena es un dato aparte.

### 3.2 Cadenas, precios especiales y ofertas (Lotes B y C)

- **[DECIDIDO]** Entidad `ClientChain` (nombre, notas, activa) y `Client.ChainId`. **Pantalla nueva «Cadenas»** con su ficha: datos, clientes de la cadena, precios especiales y ofertas que le aplican. Columna y filtro «Cadena» en Clientes y en las órdenes; el análisis de ventas puede agrupar por cadena. **[PROPUESTA]** alta masiva: «poner en la cadena X a todos los clientes cuyo nombre empiece por `Walgreens`», con simulación antes de aplicar.
- **[DECIDIDO]** Los precios especiales **usan las listas de precios que hay** y se les añade la cadena: `PriceListChain`. Una lista aplica a todos, o a clientes y/o cadenas escogidos. Vigencia como hoy: «desde» obligatorio, «hasta» opcional; sin «hasta» se queda; vencida, vuelve el precio normal.
- **[DECIDIDO]** Pantalla amigable **«Precios especiales»** en la ficha del cliente y de la cadena (producto, precio, desde, hasta); por dentro crea y edita una lista publicada dedicada. Quien ya usa las listas (`sales.pricelist`) las sigue usando.
- **[DECIDIDO]** **Gana siempre el precio más bajo negociado**: entre el precio especial del cliente, el de su cadena y las ofertas que le apliquen, el más bajo, sin jerarquía cliente/cadena. Se cambia `PriceListRules.Resolve` y `OfferRules.Resolve`; la orden muestra el origen del precio («Cadena Walgreens», «Oferta X»).
- **[DECIDIDO]** El «más bajo» **incluye la lista general**: hoy un precio negociado más caro que la lista general gana igual; eso cambia (nadie con precio especial paga más que el público) y el sistema avisa «precio especial más caro que la lista general» para que lo corrijan.
- Verificado: el precio se resuelve en un solo sitio, `OfferRules.Resolve` (lo llaman `PriceListQueries` y `SalesCatalogService`; `OfferService` usa `PriceListRules.Resolve` para sus avisos). Cambiar la regla ahí la cambia en el catálogo, el asistente, la orden y el portal a la vez.
- **[DECIDIDO]** **Ofertas a clientes o a cadenas, con incluir o excluir.** Audiencia de la oferta: (a) todos; (b) **solo** los incluidos (clientes y/o cadenas); (c) todos **menos** los excluidos (clientes y/o cadenas). En la pantalla, llenar «incluir» deshabilita «excluir» y al revés. Datos: `Offer.AudienceMode` (ALL / INCLUDE / EXCLUDE), `OfferClient` (ya existe) y `OfferChain` nueva. Las ofertas existentes quedan como ALL o INCLUDE según `AppliesToAll`. Ajustar `AppliesTo`, `AudiencesOverlap` y los avisos de cruce.
- **[PROPUESTA]** Aviso de «precio especial por vencer» con el mismo mecanismo de §3.4.

### 3.3 Documentos con vigencia y exención de IVU (Lote D)

Opinión sobre lo correcto (conocimiento general, **no asesoría contributiva**; confirmar con el contador): el IVU es estatal 10.5 % + municipal 1 %; se declara por separado, por eso la tasa lleva componentes. Hay dos exenciones independientes: la del **producto** (la fija el Estado) y la del **comprador** (certificado). El vendedor responde si vende exento sin tener el certificado vigente, así que la exención debe tener siempre su documento.

**Documentos en general [DECIDIDO en la idea, PROPUESTA en el diseño]:**
- `ClientDocument` gana `DocumentNumber`, `ValidFrom` y `ValidTo` (hasta cuándo vale). Sirve para cualquier documento, no solo el IVU.
- El **tipo de documento** (catálogo `ClientDocumentType`, editable en Catálogos de valores) gana dos marcas: «lleva vigencia» (obliga la fecha «hasta») y «es de impuestos» (puede respaldar una exención). Tipos nuevos sembrados: certificado de exención de IVU estatal, municipal y total; el de revendedor ya existe.
- El aviso de vencimiento (§3.4) cubre **todo documento con «hasta»**, sea o no de impuestos.

**Exención del cliente atada al documento:**
- La exención sigue siendo un registro propio (`ClientTaxExemption`: a qué componentes aplica, estatal, municipal o ambos) porque es la regla que usa el cálculo; se le añade `ClientDocumentId`.
- **Una sola captura**: en la ficha del cliente › Impuestos, «Nueva exención» pide el alcance (estatal / municipal / ambos), el número, la vigencia **con fecha «hasta» obligatoria** y **el archivo del certificado**; el sistema crea el documento y la exención juntos. Al revés también: al subir en Documentos uno de tipo «de impuestos», pregunta «¿Aplicar la exención de IVU?» y abre la misma ventana.
- **Validación**: no se guarda una exención sin su documento («La exención necesita el documento que la respalda.»). La vigencia es una sola: la del documento. Renovar = subir el documento nuevo; la exención anterior queda en el historial.
- Vencida: no se aplica (ya pasa hoy) y se cobra el IVU.

**Exención del producto [DECIDIDO]: total, todo o nada.**
- Se queda la categoría del producto (gravable / exento) y se añade la **exención por temporada**, porque el Estado puede quitar el IVU por un tiempo: `TaxExemptionPeriod` (nombre, `ValidFrom`, `ValidTo` obligatorio, motivo) que aplica a **todos los productos**, a **categorías** o a **productos escogidos**. Dentro del período esos productos son exentos totales; al terminar, vuelven solos a su categoría. Se administra en la pantalla de impuestos.
- **Alcance del certificado del cliente:** Luis cree que ningún certificado exime el IVU estatal; **lo confirma con el contador**. El sistema no lo fija: el alcance (estatal / municipal / ambos) se escoge en cada exención, así sirve cualquiera que sea la respuesta.
- La línea paga lo que no eximen ni el producto ni el cliente; `TaxReasons` dice por qué.

**Aviso al vender [DECIDIDO]:**
- Al **escoger el cliente** en el asistente de la orden (y en la orden normal), si tiene un documento de impuestos **vencido** aparece el aviso ahí mismo («El certificado de exención de IVU de este cliente venció el … : se cobrará el IVU.»); **[PROPUESTA]** también si está por vencer. Se repite en el **resumen** antes de enviar. No bloquea la venta.

**Editar el IVU a mano en una orden [DECIDIDO]:** permiso propio `sales.tax`, motivo obligatorio, razón `MANUAL_OVERRIDE`, se guarda el cálculo original y el manual, y queda en el registro de la modificación (§3.5).

### 3.4 Avisos de vencimiento (Lote D)

- Trabajo programado diario (`DOCUMENT_EXPIRY_SCAN`) que recorre documentos del cliente con «hasta» (y precios especiales con «hasta») y publica un evento por cada umbral cumplido, con clave de deduplicación `documento + umbral`.
- **[DECIDIDO]** **Umbrales configurables por compañía**: una lista libre de días. Positivo = antes de vencer (30, 15, 5); 0 = el día; **negativo = después de vencido** (−1, −7). Además un interruptor «**avisar a diario después de vencido**» (sí/no). Por omisión 30, 15 y 5.
- **[DECIDIDO]** Destinatarios: los **vendedores de la ruta del cliente** (titular y apoyos) y el **vendedor preferido** del cliente, más **los que se configuren** en la regla del aviso (usuarios o roles) y, si la compañía lo activa, **el cliente**.
- **[DECIDIDO]** **El aviso también va al cliente**: al **correo principal** del cliente (contacto principal) y a **los contactos que tengan marcado «Recibe avisos por correo»**. Marca nueva en el contacto del cliente (`ClientContact.ReceivesEmailNotifications`; el correo es su `ContactPoint` de tipo correo). Verificado: hoy los avisos al cliente salen por `ClientNotificationSetting` (por tipo de aviso: correo/app/SMS, a los usuarios del portal más «correos extra» escritos a mano); se añade el tipo de aviso «documento por vencer» y la lista de destinatarios pasa a incluir el correo principal y los contactos marcados. La marca del contacto sirve para todos los avisos al cliente, no solo este. Un contacto marcado sin correo se señala al guardarlo.
- **[DECIDIDO]** En pruebas no se le envía a nadie real: va a una dirección genérica que Luis pueda ver. En TEST eso ya lo hace `Email:RedirectTo` (todo a `teikem@cerevelo.com`); en desarrollo, API sin Brevo y buzón local (Mailpit).
- **[PROPUESTA]** Lista «Documentos por vencer» (cliente, documento, vence, días) para no depender del correo.

### 3.5 Editar la orden de venta (Lote E)

**Al crearla [DECIDIDO]:** en el asistente y en la orden normal se puede **editar el precio** de cada línea con el permiso `sales.price` (queda marcado como precio manual con el precio de lista al lado).

**Después de creada [DECIDIDO]:** todo cambio pasa por una **«Modificación de la orden»** que deja registro.

Registro `SalesOrderChange`: orden; quién la registró (usuario); **quién la autorizó en el cliente** (nombre y cargo), **cómo** (teléfono, correo, en persona, mensaje) y cuándo; motivo; adjunto (captura del correo o mensaje); fotografía de **antes y después** (líneas, cantidades, precios, impuesto, total); estado; segunda aprobación si aplica. Se ve como historial en la ficha de la orden y en la auditoría.

- Qué se cambia: cantidad y precio, **agregar y quitar líneas**, el impuesto (`sales.tax` + motivo), forma de pago, entrega/recogido, notas. Permiso nuevo `sales.modify`.
- **[DECIDIDO]** **Segunda aprobación interna y adjunto: configurables por compañía y por cliente** (el del cliente manda sobre el de la compañía). Por omisión los dos son **opcionales**. La segunda aprobación se puede pedir siempre o por encima de un monto.
- Si el cambio sube el total más allá del crédito, se pide la autorización de crédito de siempre.

**Las etapas reales en el código (verificado):** la orden de venta solo tiene DRAFT, REQUESTED (Solicitada), PARTIAL, SHIPPED (Despachada) y CANCELLED. Lo que Luis llama «recolectada» y «empacada» vive en la **recolección** (`PickBatch`): «Despachar» crea la recolección en COLLECTED, **saca el inventario del Kárdex en ese momento** y deja la orden en parcial o despachada; «Empacar» la pasa a PACKED y crea la orden de transporte (solo con entrega; con «Retiro» no hay empaque). Ya existe eliminar una recolección **con reversa** (devuelve el inventario a su posición y la orden vuelve a pendiente, `OnPickBatchReversedAsync`), y una empacada solo se elimina si su orden de transporte sigue en la etapa inicial. Ya existe también la **recolección maestra** (`PickGroup`): varios documentos del mismo cliente que salen en un solo envío. **No hace falta inventar etapas nuevas**; se usa lo que hay:

**Según la etapa [DECIDIDO]:**
| Lo que dice Luis | En el sistema | Qué se permite | Cómo |
|---|---|---|---|
| Solicitada, nada recolectado | Orden REQUESTED sin recolección | Todo | Se reserva y libera inventario como hoy |
| Recolectada, sin empacar | Recolección COLLECTED | Todo. **Agregar**: la línea nueva queda pendiente y se despacha en otra recolección de la misma orden. **Bajar o quitar** algo ya recolectado: el sistema hace la reversa de esa recolección (el inventario vuelve a su posición, queda en el Kárdex) y se despacha de nuevo | Aviso al almacén |
| **Empacada**, sin salir, **sin facturar** | Recolección PACKED, orden de transporte en su etapa inicial, sin factura emitida | Se edita **reabriendo el empaque**: permiso especial `warehouse.reopen-pack`, motivo obligatorio, todo auditado; devuelve la recolección a recolectada y anula la orden de transporte. Alternativa sin reabrir: **orden interna** vinculada (abajo) | Se vuelve a empacar |
| Ya salió | Orden de transporte más allá de la etapa inicial, o despacho con «Retiro» (el cliente se lo llevó) | No se edita | Devolución (ya existe) u orden nueva |

**Empacada y ya FACTURADA, sin salir del almacén [caso dictado por Luis; solución PROPUESTA]:** la facturación depende del empaque, así que puede haber una orden empacada, facturada (y hasta pasada a QuickBooks) a la que le añaden algo antes de que salga. Regla: **una factura emitida no se reabre nunca**, esté o no en QuickBooks, y por lo tanto **tampoco se reabre ese empaque**. Lo que falta se resuelve con documentos nuevos, que es lo que QuickBooks y el contador esperan:
- **Añadir** → **orden interna de adición** (abajo), vinculada a la original. Se recolecta y se empaca aparte, se une al **mismo envío** (recolección maestra) y genera **su propia factura** (factura complementaria con la referencia de la original), que se exporta a QuickBooks como una factura más. La original no se toca.
- **Quitar, bajar o cambiar precio/IVU de lo ya facturado → FUERA DE ESTE ALCANCE [DECIDIDO, 2026-11-24].** Eso pide notas de crédito y Luis las dejó para otro alcance. En este, una orden facturada **solo admite añadir** (por la orden interna de adición); si el usuario intenta quitar o bajar, el sistema lo dice claro («La orden ya está facturada: solo se le puede añadir. Quitar o bajar se hace con una nota de crédito.») y no construye nada de notas de crédito ni su exportación a QuickBooks.
- El sistema decide solo el camino: al pulsar «Modificar orden» mira si hay factura emitida; si la hay, no ofrece reabrir el empaque y lleva al usuario a la adición, con el mismo registro de quién autorizó y cómo.
- **[DECIDIDO] Proceso «pasada a QuickBooks»**: exportar baja el archivo y deja el documento como «exportado, por confirmar»; después de importarlo en QuickBooks, **el usuario confirma que pasó** y solo entonces queda «en QuickBooks». Para las facturas de venta esto ya existe (Ventas › pestaña QuickBooks: «¿Ya importaste estas N?» → «Sí, ya están»); se usa ese mismo estado como referencia en la modificación de órdenes y se repite **igual para las órdenes de compra** (Lote F) y para las facturas complementarias que nazcan de una adición. Si el usuario dice que no pasó, vuelve a quedar pendiente de exportar.
- Verificar al diseñar el Lote E: en qué momento exacto la corrida de facturación toma la orden (al empacar o al despachar).

**Orden interna de adición [DECIDIDO en la idea]:** para cuando, aunque el sistema no lo permita, hay que añadirle algo a una orden que se va a enviar. Es una **orden normal hecha por el personal, sin firma del cliente**, que lleva **quién lo autorizó y cómo** (los mismos campos del registro de modificación) y queda **vinculada** a la orden original (`ParentSalesOrderId`): sale en la misma entrega usando la recolección maestra que ya existe (`PickGroup`), se factura junto o aparte según el cliente, y se ve en la ficha de la original. **[PROPUESTA]** el sistema la ofrece sola cuando la orden está empacada y el usuario no tiene permiso de reabrir.

Siempre: aviso al almacén y al vendedor cuando cambia una orden que está en recolección o empacada; se recalculan precio, impuesto y envío.

### 3.6 Órdenes de compra: importación definida por el usuario y exportación a QuickBooks (Lote F)

- **[DECIDIDO]** Hoy ellos trabajan la orden de compra en Excel y Teikem solo importa dos formatos fijos. La importación debe poder **definirla el usuario**, como las demás del proyecto.
- Reutilizar `ImportTemplate` con un tipo nuevo `PURCHASE_ORDER`: columnas por posición (proveedor, número de documento, fecha, fecha esperada, SKU, descripción, cantidad, costo), delimitador, encabezado, valores fijos (p. ej. proveedor fijo para todo el archivo), CSV y Excel. Varias líneas por orden agrupadas por número de documento. Se administra en la pantalla de plantillas que ya existe.
- En «Importar» de Compras el origen pasa a ser: QuickBooks, Genérico o **una plantilla del usuario**. Todo lo demás del Lote 239 se conserva (simulación, bloqueo por producto o proveedor que no existe, no duplicar).
- **[DECIDIDO]** **Exportar la orden de compra a QuickBooks**: botón en Compras (una o varias órdenes) que baja el archivo en el formato de QuickBooks, con marca de «ya exportada» y confirmación «¿Ya las importaste?», igual que la exportación de ventas. **Formato: archivo IIF de QuickBooks Desktop**, como las ventas; se reutiliza el generador de `Sales/QuickBooks.cs` (`!TRNS` / `!SPL`) con el tipo de transacción de orden de compra (`PURCHORD`).
- **[DECIDIDO]** La orden de compra puede nacer en **Teikem, en el Excel o en QuickBooks**: se conservan las tres entradas (crear a mano, plantilla del usuario, importar de QuickBooks). Para que no se dupliquen al ir y venir, cada orden guarda su **origen**: las que vinieron de QuickBooks no se ofrecen para exportar; las que nacieron en Teikem o del Excel, sí.
- Riesgo a probar: la importación por IIF de órdenes de compra en QuickBooks Desktop es más delicada que la de facturas (es una transacción que no asienta). Probar el archivo en una copia de la compañía de QuickBooks de Solutions antes de darlo por bueno.

### 3.7 Correo con la orden de venta al crearla, como el de Formsite (Lote G)

- **[DECIDIDO, 2026-11-24]** Para el arranque de Solutions: al **crear (solicitar) una orden de venta**, el sistema envía **automáticamente un correo con la orden completa**, parecido al que hoy manda Formsite, para que el personal siga trabajando como está acostumbrado mientras se implanta el resto. Es una **opción de configuración** (se enciende y se apaga por compañía), no una pantalla nueva.
- Es posible y encaja con lo que hay: ya existe el aviso `OrderRequested` (`SalesOrderService.Notifications.cs`, `NotifyRequestedAsync`), que hoy solo lleva número, cliente, almacén y cantidad de líneas. Se trabaja como un **aviso más del motor**, con una plantilla de correo de contenido completo.
- **Contenido del correo** (tomar de referencia el correo de Formsite analizado en `docs/pedido-vendedores-formsite-analisis-2026-11-06.md` y `docs/plan-de-desarrollo-pendiente.md`, bloque «Order Summary»): número y fecha de la orden; vendedor; cliente con dirección, teléfono y registro de comerciante; entrega o recogido; forma de pago; notas; **tabla de líneas** (SKU, descripción, cantidad, precio, importe; los kits con sus componentes); subtotal, IVU estatal y municipal por separado, y total; quién autorizó y la firma; enlace a la orden en Teikem. **[PROPUESTA]** adjuntar además la orden en PDF.
- **Destinatarios**: los que se configuren en la regla del aviso (usuarios, roles y **correos escritos a mano**, porque hoy llega a buzones como el de almacén), más el vendedor de la orden. Opción aparte para mandarle copia al cliente (correo principal y contactos marcados, §3.4), apagada por omisión.
- Verificar al diseñar: si la regla de aviso ya admite correos externos escritos a mano y adjuntos; si no, añadirlo.
- Pruebas sin correo real (regla de Brevo): buzón local; en TEST se desvía a `teikem@cerevelo.com`.

---

## 4. Lotes propuestos

Cada lote termina con: SQL en una sección nueva de `logistica-db-update.sql`, pruebas, pantallas verificadas con Playwright **mirando la captura**, manual, FAQ y `docs/loteN-decisiones.md`. El último lote hecho es el 242; usar el siguiente número libre.

| # | Lote | Contenido | Depende de |
|---|---|---|---|
| A | **Rutas** | `Client.SalesRouteId`; `SalesRouteAssignment` (titular/apoyo, historial); vendedor = usuario; vendedor preferido en el cliente; cuota por ruta o por persona (ajuste); copia de ruta y vendedor en la orden; análisis por ruta; migración | Lote 241 |
| B | **Cadenas** | `ClientChain`, `Client.ChainId`; pantalla y ficha de cadenas; alta masiva con simulación; columna y filtro; análisis por cadena | — |
| C | **Precios especiales y ofertas** | `PriceListChain`; «gana el más bajo»; pantalla «Precios especiales» en cliente y cadena; ofertas con incluir/excluir y cadenas (`AudienceMode`, `OfferChain`); origen del precio visible en la orden | B |
| D | **Documentos con vigencia, IVU y avisos** | Número y vigencia en `ClientDocument`; marcas del tipo de documento; exención del cliente atada al documento (captura única, validación); `TaxExemptionPeriod` (exención por temporada a todos los productos, categorías o productos); aviso al escoger el cliente y en el resumen; trabajo diario de vencimientos con umbrales (positivos y negativos), diario después de vencido, destinatarios por ruta + configurados + cliente; lista «Documentos por vencer» | A (vendedor de la ruta), Lotes 55/222/237 |
| E | **Edición de la orden con autorización** | Precio editable al crear; `SalesOrderChange`; agregar y quitar líneas; `sales.tax` con `MANUAL_OVERRIDE`; segunda aprobación y adjunto configurables por compañía y cliente; reglas por etapa; reabrir empaque; orden interna de adición vinculada; avisos | D, Lote 242 |
| F | **Compras: plantilla del usuario y exportación a QuickBooks** | `ImportTemplate` tipo `PURCHASE_ORDER`; origen «plantilla» en Importar; exportación de órdenes de compra a QuickBooks | Lote 239. **Independiente**: puede ir en cualquier momento |

| G | **Correo con la orden de venta al crearla** | Opción por compañía; plantilla de correo con la orden completa (como Formsite), PDF adjunto opcional; destinatarios configurables (usuarios, roles, correos a mano, vendedor, copia al cliente) | Lote 66 (avisos de ventas). **Independiente** |

Orden recomendado: **G primero** (es chico y es lo que Solutions necesita para arrancar), luego **A → B → C → D → E**, con **F** en paralelo o cuando Solutions lo necesite (tampoco depende de nada).

---

## 5. Permisos, avisos y datos

- **Permisos nuevos** (propagar a los roles ya clonados, solo agregar): `sales.modify`, `sales.tax`, `sales.chain` (cadenas), `warehouse.reopen-pack`, y el de exportar compras a QuickBooks (ver si se reutiliza el de la exportación de ventas).
- **Eventos de aviso nuevos**: documento por vencer / vencido, precio especial por vencer, orden modificada (al almacén y al vendedor), orden interna de adición creada, empaque reabierto, segunda aprobación pendiente.
- **Ajustes nuevos de la compañía**: cuota por ruta o por persona; umbrales de aviso y «diario después de vencido»; avisar al cliente sí/no; segunda aprobación (no / siempre / sobre un monto) y adjunto (opcional / obligatorio). Los dos últimos también en el cliente.
- **Auditoría**: entidades nuevas con `[AuditEntity]`; `SalesOrderChange` es además registro de negocio.
- **Migración**: ruta del cliente desde su representante; ninguna cadena creada sola; exenciones existentes sin documento quedan marcadas «falta el documento» (siguen valiendo hasta su «hasta», y la lista las muestra para completarlas). Probar sobre una copia de la base de Solutions (700 clientes, 7 representantes), nunca sobre la de uso diario.

---

## 6. Lo que ya quedó contestado por Luis (no volver a preguntar)

| Pregunta de la versión 1 | Respuesta |
|---|---|
| ¿Una persona por ruta o varias? ¿Cuota por ruta? | Una o varias: titular y apoyo. Cuota configurable por compañía: por ruta o por persona |
| ¿La ruta «Cadenas» se queda? | Rutas y cadenas son cosas distintas; no se toca |
| Precio de cadena vs. cliente | Gana el más bajo negociado, sin jerarquía |
| ¿Ofertas a cadenas? | Sí, a clientes o cadenas, con incluir o excluir |
| Umbrales y destinatarios de los avisos | Lista configurable (negativos = después de vencido), opción de diario después de vencido; vendedor de la ruta + los que se configuren + cliente; en pruebas, a una dirección genérica |
| Segunda aprobación y adjunto | Configurables por compañía y por cliente; por omisión opcionales |
| Empacada | Se permite reabrir el empaque con permiso especial, motivo y auditoría; además la orden interna vinculada |
| Exenciones | Las dos: por producto (total, con vigencia/temporada) y por cliente (por componente, con documento y vigencia) |
| Orden de los lotes | El que la sesión entienda (ver §4) |

---

## 7. Verificaciones hechas en el código (2026-11-24)

1. **Etapas del almacén**: verificado; ver la tabla de §3.5. No se necesitan estatus nuevos en la orden de venta.
2. **Dónde se resuelve el precio**: un solo sitio, `OfferRules.Resolve` (§3.2).
3. **Uso de `Client.SalesRepId`**: 53 apariciones en 7 archivos, casi todas en el análisis de ventas (`SalesAnalysisService`, `SalesAnalysisSetupService`) y en `ClientService`/`ClientContracts`. La migración a la ruta toca solo esa zona.
4. **Exportación IIF**: el generador existe para facturas (`Sales/QuickBooks.cs`, `QuickBooksService`); para compras se añade el tipo `PURCHORD`. Falta probarlo contra un QuickBooks real (§3.6).
5. **Sin verificar** (queda para el diseño del Lote D): cómo ve el cliente sus documentos en el portal (`app-cliente`); solo afecta si el cliente debe ver ahí el aviso además de recibirlo.

---

## 8. Lo que queda pendiente fuera del código

1. **Contador**: confirmar qué certificados eximen el IVU estatal y cuáles el municipal. No bloquea la construcción (el alcance se escoge en cada exención), solo la carga de datos reales.
2. **Correos de los vendedores de Solutions** para crearles usuario (Lote A).
3. **Copia de la compañía de QuickBooks** para probar el IIF de órdenes de compra (Lote F).

No quedan preguntas de diseño abiertas con Luis.

---

## 9. Cómo pasar esto a la otra sesión

Darle este archivo y decirle: «lee `docs/plan-rutas-cadenas-precios-ivu-modificaciones.md` y empieza por el lote que Luis indique». Luis dio el plan por cerrado el 2026-11-24. Cada lote se diseña con `lote-diseno` (panel y plan) antes de implementar con `lote-implementar`, como el resto de los lotes de Teikem.
