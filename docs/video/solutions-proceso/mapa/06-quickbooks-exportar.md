# Escena 6 - QuickBooks en ventas: mapa para el video

## 1. Qué es y dónde está (menú, ruta, permiso, módulo)

- **Qué es.** Teikem arma un archivo **IIF** (QuickBooks Desktop) con las **facturas de venta emitidas** y la oficina lo importa en QuickBooks. No hay conexión directa: se baja el archivo y se importa (docs/manual/49-quickbooks.md:5).
- **Dónde.** Menú **Contabilidad → Ventas** (clave `nav.items.salesOrders`; la pantalla se titula «Órdenes de venta»), ruta `/sales-orders`, pestaña **QuickBooks** (`sales.tabs.quickbooks`), también directo con `/sales-orders?tab=quickbooks` (web-app/src/app/routes.tsx:394; SalesOrdersScreen.tsx:56-59, 305-310, 360).
- **Permisos.** `sales.quickbooks.view` (ver), `sales.quickbooks.export` (exportar y «Sí, ya están») y `sales.quickbooks.mark` (marcar/quitar la marca). La pestaña se ve con cualquiera de los tres. Los traen Admin de compañía y Oficina de ventas (manual 49:11-16; PermissionCatalog.cs:466-472, 1228).
- **Módulo de la pestaña:** `SALES_ORDERS` (QuickBooksController.cs:18). **Pero la factura sale de Facturación (`BILLING`)**, ver sección 4.
- **Respuesta a la pregunta clave: la exportación NO exporta órdenes de venta, exporta FACTURAS.** «Por pasar» = factura de venta emitida (Emitida, Vencida o Pagada) con al menos una línea de orden de venta o su cargo de envío, sin la marca «en QuickBooks» (QuickBooksService.cs:30, 286-290; manual 49:21). Una orden despachada, por sí sola, **no aparece**.

## 2. Qué conviene mostrar, en orden

Pedido del dueño: exportar el archivo que la oficina usa para entrar las facturas en QuickBooks. Para que se entienda, en orden:

1. **El puente (corto):** la orden de la escena 4, ya despachada (escena 5), se convierte en factura: **Contabilidad → Facturación → Nueva corrida → Generar → Marcar como revisada → Aprobar y emitir** (pide contraseña). Sin este paso la pestaña QuickBooks está vacía.
2. **Pestaña QuickBooks:** la factura aparece en «Facturas por pasar a QuickBooks» con su cliente, total y, si aplica, «Producto nuevo» / «ver datos».
3. **Exportar a QuickBooks:** ventana con cuántas facturas salen y su total; casilla «Incluir los clientes y productos nuevos».
4. **Exportar y bajar el archivo:** baja `teikem-quickbooks-AAAAMMDD-HHmm.iif`; aparece «¿Ya importaste estas N?».
5. **Mostrar el archivo abierto unos 5-8 s** (Bloc de notas, texto con tabuladores): se ven los renglones `TRNS` / `SPL` / `ENDTRNS` de la factura. Conviene, porque es «lo que la oficina ya no teclea».
6. **Sí, ya están (N):** la factura sale de la lista; **Ver las ya pasadas** (histórico) muestra quién y cuándo. Opcional.
7. **Contraste con hoy:** hoy Leandra recibe un correo con la orden y la teclea a mano en QuickBooks; con esto, un clic baja el archivo y QuickBooks lo importa (narración, sin pantalla).
8. **Lo que NO se puede grabar:** QuickBooks Desktop. Alternativa: una lámina con los pasos (Archivo → Utilidades → Importar → Archivos IIF) o una grabación corta que haga Luis aparte (sección 8).

## 3. Paso a paso exacto del recorrido

Etiquetas literales de web-app/src/kernel/i18n/es.json. Existe un recorrido que ya hace casi todo esto: web-app/e2e/lote228-pedido-vendedores.spec.ts:464-571 (reutilizar sus localizadores).

| # | Acción | Etiqueta en pantalla (es) y clave | Cómo localizarlo en Playwright | Qué debe verse después |
|---|---|---|---|---|
| 1 | Ir a Facturación | Menú Contabilidad → «Facturación» (`nav.items.billing.title`); título `billing.title` | `page.goto('/money/billing')`; `getByRole('heading',{level:1,name:'Facturación'})` | Pestañas «Corridas», «Facturas», «Cobros», «Notas de crédito», «Consignación», «Ajustes» (`billing.tabs.*`); lista de corridas (vacía: «Todavía no hay corridas. Genere una con «Nueva corrida».» `billing.runs.empty`) |
| 2 | Abrir la nueva corrida | «Nueva corrida» (`billing.new.title`) | `getByRole('button',{name:'Nueva corrida'})` | Ventana «Nueva corrida» con Cliente, Desde, Hasta, «Venta de producto despachado», «Flete de los envíos de transporte» (`billing.new.*`) |
| 3 | Escoger el cliente de la venta | «Cliente» (`billing.new.client`); ayuda «Sin cliente: todos los que tengan algo por facturar.» | en el diálogo `getByRole('combobox').first().fill(<inicio del nombre>)` y `getByRole('option').filter({hasText:<cliente>})` | Cliente elegido. Desde/Hasta ya traen un período; debe incluir la fecha de la recolección |
| 4 | Generar | «Generar» (`billing.new.generate`) | `dialog.getByRole('button',{name:'Generar'})` | Ventana «Corrida #N» (`billing.run.title`) con la factura en borrador del cliente y «Total de la corrida: …» |
| 5 | Marcar revisada | «Marcar como revisada» (`billing.run.review`) | `getByRole('dialog',{name:/^Corrida #/}).getByRole('button',{name:'Marcar como revisada'})` | Estatus «Revisada»; mensaje «Corrida #N revisada.» |
| 6 | Aprobar y emitir | «Aprobar y emitir» (`billing.run.approve`) | `...getByRole('button',{name:'Aprobar y emitir'})`; luego diálogo «Confirme su identidad»: `getByLabel('Contraseña',{exact:true})` + botón «Confirmar» (spec:132-139) | «Corrida #N aprobada: facturas emitidas.»; estatus «Aprobada»; la factura queda «Emitida» |
| 7 | Cerrar la corrida | «Cerrar» (`billing.close`) | `getByRole('button',{name:'Cerrar'}).first()` | Vuelve a la lista de corridas |
| 8 | Ir a Ventas, pestaña QuickBooks | «Ventas» (menú) → pestaña «QuickBooks» (`sales.tabs.quickbooks`) | `page.goto('/sales-orders?tab=quickbooks')` o `getByRole('tab',{name:'QuickBooks'}).click()` | Panel «Facturas por pasar a QuickBooks» (`sales.quickbooks.panel`) con la fila de la factura: fecha, factura (con «Órdenes: OV-…»), cliente, total |
| 9 | (Opcional) Ver qué es nuevo | «ver datos» (`sales.quickbooks.seeData`) | `getByRole('button',{name:'Ver los datos nuevos de la factura <número>'})` | Ventana «Datos nuevos para QuickBooks · <número>»: nombre en QuickBooks, términos, productos nuevos (ítem = SKU, precio, costo, «Lleva IVU») |
| 10 | Exportar | «Exportar a QuickBooks» (`sales.quickbooks.export`) | `getByRole('button',{name:'Exportar a QuickBooks'})` | Ventana «Exportar a QuickBooks»: «Salen todas las facturas por pasar: N ($total). Exportar no las marca…» y casilla «Incluir los clientes y productos nuevos» (marcada por omisión) |
| 11 | Bajar el archivo | «Exportar y bajar el archivo» (`sales.quickbooks.exportDialog.go`) | `Promise.all([page.waitForEvent('download'), dialog.getByRole('button',{name:'Exportar y bajar el archivo'}).click()])` | Descarga `teikem-quickbooks-AAAAMMDD-HHmm.iif`; aviso «Se bajó el archivo {file}.»; aparece «¿Ya importaste estas N?» (`sales.quickbooks.ask.title`) con «Importe el archivo en QuickBooks (Archivo → Utilidades → Importar → Archivos IIF)…» |
| 12 | Mostrar el archivo | (fuera de la web) | Abrirlo en Bloc de notas con montaje aparte; en Playwright solo se guarda la descarga (`download.saveAs`) | Renglones con tabuladores: `!INVITEM` / `!CUST` (si se incluyeron nuevos) y `TRNS`, `SPL`…, `ENDTRNS` |
| 13 | Confirmar | «Sí, ya están (N)» (`sales.quickbooks.ask.confirm`) | `getByRole('button',{name:/^Sí, ya están/})` | La factura desaparece de la lista; mensaje de marcadas. Alternativas en el mismo panel: «Bajar el archivo otra vez», «Todavía no» |
| 14 | (Opcional) Histórico | «Ver las ya pasadas» (`sales.quickbooks.historyLink`) | `getByRole('button',{name:'Ver las ya pasadas'})` | Panel «Ya pasadas a QuickBooks» con quién, cuándo y último archivo; botón «Quitar la marca» (no usarlo en el video) |

Otras acciones de la pestaña (no necesarias): «Marcar como en QuickBooks», «Marcar todas», casillas «Elegir todas (N)», buscador «Buscar factura, cliente u orden».

**Camino mínimo desde el despacho hasta el archivo:** recolección/despacho de la orden (escena 5; crea el `PickBatch` y sube `QtyShipped`) → Facturación: Nueva corrida → Generar → Marcar como revisada → Aprobar y emitir (contraseña) → Ventas → QuickBooks → Exportar a QuickBooks → Exportar y bajar el archivo. No hay otro camino: no existe factura manual ni se crea sola al despachar (BillingService.cs:66-113; las únicas rutas que crean facturas están en BillingController.cs:41-52).

## 4. Requisitos de datos y de configuración para que funcione con Advance Solutions

Consultas con `sqlcmd` solo lectura, TenantId = 2, base local Teikem.

| Requisito | Estado HOY | Cómo se arregla |
|---|---|---|
| Módulo `BILLING` encendido (sin él, `/money/billing` y `/api/v1/billing/*` no existen: BillingController.cs:20; routes.tsx:397) | **FALTA.** `SELECT ModuleKey,IsEnabled FROM TenantModule WHERE TenantId=2` → BILLING = 0 (los demás de Solutions = 1) | Sistema → Ajustes → Módulos (el script 00 lo pide encendido, LEEME.md:30). Encenderlo antes de grabar |
| Permisos del usuario que graba: `billing.view`, `billing.generate`, `billing.approve` + `sales.quickbooks.*` | No comprobado por rol; Admin de compañía los trae (manual 19:10-11; PermissionCatalog.cs:1228) | Grabar con el Admin de compañía de Solutions |
| Base de facturación | `Tenant.SalesBillingTrigger = ON_DISPATCH`, `InvoiceDueDays = 30` → factura lo recolectado, sin esperar la entrega (BillingService.cs:85-113). Cumple | Nada. Con `ON_DELIVERY`, una orden con entrega no se factura hasta que el transporte la entregue |
| Algo despachado y sin facturar | Solutions tiene 2 órdenes (1 Solicitada, 1 Cancelada), 0 despachadas; la de la escena 5 sería la primera | La escena 5 debe recolectar/despachar de verdad (crea `PickBatchLine` con `SalesOrderLineId`, requisito de BillingService.cs:90-100) |
| Facturas existentes | `SELECT COUNT(*) FROM Invoice WHERE TenantId=2` → **0**. La lista de QuickBooks arranca vacía | Se crea la de la escena 4 con la corrida (pasos 1-7) |
| Cuentas e ítems en Tenant | `QuickBooksReceivableAccount`, `…SalesAccount`, `…CostAccount`, `…InventoryAccount`, `…TaxAccount`, `QuickBooksTaxItem`, `QuickBooksShippingItem` = **NULL todas** → valen los de omisión: Accounts Receivable, Sales, Cost of Goods Sold, Inventory Asset, Sales Tax Payable, ítem `IVU`, ítem `Shipping` (manual 49:80-91) | Aceptable para la demo. Para cambiar: Sistema → Ajustes → Operación → QuickBooks (`SettingsQuickBooksSection.tsx`) o script `08-ajustes-envio-quickbooks.sql` (simula con `@Aplicar = 0`) |
| Mapa de términos de pago | `QuickBooksTermsJson` = **NULL**. 692 de 701 clientes tienen término. Sin mapa la factura sale **sin término** y la lista avisa «El término «NET30» no tiene nombre en QuickBooks…» (manual 49:76) | Script 08 (NET15/20/30/45, CHEQUE, CASH, ACH, Consignment, PK BY REP) o Ajustes → QuickBooks → Términos de pago. Hacerlo antes de grabar para evitar el aviso |
| Clientes ya enviados | `Client.QuickBooksSentAt` NULL en los 701, **pero** los 701 tienen «Código QuickBooks» (`qb_code`, 701 valores) y un cliente con ese código **nunca sale como nuevo** (manual 49:69) | Nada. El cliente de la escena 4 debe ser uno migrado (con código); si se crea uno en el video saldría «Cliente nuevo» |
| Productos ya enviados (script 10) | `Product.QuickBooksSentAt` NULL en los **42** activos (0 marcados). La primera exportación los traería como «Producto nuevo» con renglones `INVITEM` | Correr `10-quickbooks-ya-mandados.sql` (simula y luego aplica) o exportar **sin** «Incluir los clientes y productos nuevos» (lo recomienda el manual 49:101). Más limpio: script 10 y archivo solo con la factura |
| Nombre de ítem = SKU | Ningún SKU activo tiene espacios (`LIKE '% %'` = 0) | Nada. Pendiente de confirmar con Leandra los renombres en QuickBooks (LEEME.md:53-72) |
| Impuesto | Una tasa: `IVU11-5` «IVU 11.5%». El IVU sale como **una sola línea** por factura con el ítem de impuesto (manual 49:64) | Que los productos de la orden tengan impuesto (script 11) para que la línea IVU tenga monto |
| Cargo de envío | El script 08 pone $12 (gratis desde $120); si la orden lo trae sale con el ítem `Shipping` (manual 49:65) | Opcional; depende de la entrega en la escena 4 |
| Reglas de aviso | `SELECT ... FROM NotificationRule WHERE TenantId=2` → **0 reglas**. El aviso diario (07:00, trabajo `SALES_QUICKBOOKS_PENDING`) no llegaría a nadie; la cifra del Pulso y la fila de «Necesita tu atención» salen solas con `sales.quickbooks.view` (manual 49:93) | Script `09-reglas-de-aviso.sql` (manda correo diario: no correrlo para el video, regla Brevo de CLAUDE.md). Para el video basta mencionar o mostrar el Pulso |
| Reautenticación al aprobar | La corrida exige AAL2 (BillingController.cs:48): sale el diálogo «Confirme su identidad» con contraseña | Reautenticar justo antes de la grabación (la vigencia exacta no se verificó) o recortar ese trozo |

## 5. Riesgos al grabar

- **BILLING apagado**: sin encenderlo, la pantalla de Facturación ni existe. Es el principal bloqueo.
- **Contraseña en pantalla** al aprobar la corrida («Confirme su identidad»). El campo oculta los caracteres, pero se ve el tecleo; reautenticar antes o cortar el plano.
- **Sin factura no hay nada**: la pestaña mostraría «No hay facturas por pasar a QuickBooks.» (`sales.quickbooks.empty`) y «Exportar» daría 422 con el mismo texto.
- **Descarga del navegador**: en Playwright no se abre ninguna ventana; hay que guardar el archivo y abrirlo aparte para mostrarlo.
- **Codificación**: el archivo es Windows-1252 con tabuladores; un editor que asuma UTF-8 puede mostrar caracteres raros. Usar el Bloc de notas de Windows.
- **Todos los productos «nuevos»** si no se corre el script 10: aviso «N productos nuevos» y renglones `INVITEM` extra.
- **Avisos de la lista (no bloquean):** término sin nombre; nombre de cliente de más de 41 caracteres (hay nombres migrados largos, escoger uno corto); SKU de más de 31.
- **«Cliente nuevo»** si el cliente no tiene `qb_code`.
- **Datos personales**: «ver datos» y el IIF muestran correo, teléfono y SURI de un cliente nuevo; no abrirlo con un cliente real.
- **Rango de fechas de la corrida**: si no incluye la fecha de la recolección, sale vacía.
- **Confirmar «Sí, ya están» cambia datos**: repetir la toma exige «Quitar la marca» o reponer la base.
- **Celular**: la lista usa tarjetas; grabar en escritorio.

## 6. Datos para la narración (hechos verificados)

- Hoy la oficina recibe un correo con la orden y teclea la factura en QuickBooks a mano.
- Teikem prepara la factura y arma un archivo para QuickBooks; la oficina ya no la teclea.
- Primero, la venta despachada se convierte en factura: se genera, se revisa y se aprueba.
- En Ventas, pestaña QuickBooks, están las facturas que faltan por pasar.
- Un clic en «Exportar a QuickBooks» baja el archivo, con la fecha y la hora en el nombre.
- El archivo es de QuickBooks Desktop (formato IIF): lleva el cliente, los productos, el impuesto IVU y el envío, y cada factura cuadra a cero.
- Si el pedido llevaba un kit, el archivo lo desglosa en sus productos.
- Exportar no da la factura por pasada: Teikem pregunta «¿Ya importaste estas?» y se confirman solo las que QuickBooks aceptó.
- Las que no entraron salen otra vez en el próximo archivo.
- En QuickBooks, la oficina usa Archivo, Utilidades, Importar, Archivos IIF y escoge el archivo.
- Un histórico dice quién pasó cada factura y cuándo.
- Si quedan facturas sin pasar, el Pulso y «Necesita tu atención» lo avisan (el correo diario se arma con una regla de aviso).
- No hay conexión directa con QuickBooks: es un archivo, a propósito.

## 7. Duración estimada en pantalla (segundos)

Total: **~85 s** (rango 70-100).

| Bloque | Seg |
|---|---|
| Factura desde la orden despachada (pasos 1-7, acelerado) | 20 |
| Pestaña QuickBooks y fila de la factura | 10 |
| Exportar a QuickBooks y descarga (pasos 10-11) | 12 |
| Archivo abierto en Bloc de notas | 8 |
| «¿Ya importaste?» y «Sí, ya están» | 10 |
| Lámina o grabación de QuickBooks Desktop (importar) | 15 |
| Histórico / Pulso (opcional) | 10 |

**Recortable:** «ver datos» (9), histórico (14) y Pulso; los pasos de Facturación se resumen con un corte (solo Generar y Aprobar); el archivo abierto baja a 4 s. Sin lámina de QuickBooks serían ~65 s.

## 8. Dudas y decisiones para Luis

1. **Encender BILLING** en la base local (y en TEST si se graba ahí). Recomendación: sí, es indispensable; sin eso la escena no existe.
2. **QuickBooks Desktop no se puede grabar.** Opciones: (a) lámina con los pasos Archivo → Utilidades → Importar → Archivos IIF y el resultado; (b) que Luis grabe 15-20 s en su QuickBooks importando el archivo del video. Recomendación: (a), y (b) solo si quiere el efecto real.
3. **Script 10 (productos ya enviados) o exportar sin «Incluir los clientes y productos nuevos».** Recomendación: correr el script 10 en la base de grabación para que el archivo lleve solo la factura.
4. **Términos y cuentas (script 08):** aplicar con los nombres que Leandra usa en QuickBooks. Los términos conviene aplicarlos para evitar el aviso.
5. **¿Mostrar el archivo abierto?** Recomendación: sí, 5-8 s, en Bloc de notas y con una factura de pocas líneas. No se probó cómo se ve; queda para el ensayo.
6. **Reautenticación al aprobar:** hacerla antes y recortar, para no mostrar la contraseña.
7. **Confirmar con Leandra** que usa QuickBooks Desktop (IIF) y que los ítems de QuickBooks se llaman como los SKU (renombres pendientes en LEEME.md:53-72); si no, el archivo no empareja. No verificable desde el código.
8. **Avisos:** Solutions no tiene reglas de aviso. Decidir si se muestra solo el Pulso o se menciona el aviso diario (el correo no se prueba, regla Brevo).
9. **No verificado:** que el Admin de Solutions tenga `billing.*` en su rol clonado; la vigencia de la reautenticación; el contenido real del IIF con la venta de la escena 4.

## 9. Fuentes (archivo:línea)

- docs/manual/49-quickbooks.md:5, 11-16, 19-31, 59-76, 78-101, 104-132
- docs/manual/19-facturacion.md:10-24, 30-35 (corridas, AAL2, base de facturación)
- docs/manual/18-ordenes-de-venta.md:133-139 (despachar crea la recolección y baja el inventario)
- docs/lote225-decisiones.md:11, 15, 39, 65
- scripts/solutions-pedido-vendedores/LEEME.md:30, 39-41, 53-72; script 08 (líneas 12, 26-48, 61-121); script 10 (líneas 10-35)
- web-app/src/app/routes.tsx:394, 397
- web-app/src/features/sales/SalesOrdersScreen.tsx:56-59, 305-310, 360-368
- web-app/src/kernel/i18n/es.json: `sales.tabs.quickbooks` (10453), `sales.quickbooks.*` (10455-10553), `billing.*` (9395-9520: tabs, new, run, runs, settings), `nav.items.salesOrders` y `nav.items.billing`
- src/Teikem.Api/Controllers/QuickBooksController.cs:16-60; BillingController.cs:18-52
- src/Teikem.Infrastructure/Services/QuickBooksService.cs:30, 49-116, 179-190, 283-290
- src/Teikem.Infrastructure/Services/BillingService.cs:66-113
- src/Teikem.Domain/Sales/QuickBooks.cs:136 (nombre del archivo `teikem-quickbooks-{yyyyMMdd-HHmm}.iif`)
- src/Teikem.Domain/Constants/PermissionCatalog.cs:186-188, 466-472, 762-766, 1228
- web-app/e2e/lote228-pedido-vendedores.spec.ts:132-139 (reautenticación), 464-571 (corrida, QuickBooks, descarga, histórico), 520-548 (forma esperada del IIF)
- Base local (solo lectura, TenantId = 2): Tenant (columnas QuickBooks y SalesBillingTrigger), TenantModule, Invoice (0), Product.QuickBooksSentAt (0 de 42), Client.QuickBooksSentAt (0 de 701), CustomFieldValue `qb_code` (701), TaxRate (`IVU11-5`), NotificationRule (0), SalesOrder (2: Solicitada y Cancelada)
