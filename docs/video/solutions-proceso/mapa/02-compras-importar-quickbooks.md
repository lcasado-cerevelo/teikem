# Escena 2 - Órdenes de compra e importación desde QuickBooks: mapa para el video

Investigación de solo lectura del 2026-11-12 (rama `master`, base local `Teikem`, TenantId 2 = Advance Solutions). No se grabó nada, no se levantó ningún servidor y no se cambió ningún dato. Lo que sale de leer el código y no de ver la pantalla está dicho así en cada punto.

**En cuatro líneas**

- La pantalla está en **Contabilidad → Compras** (el título de la pantalla es «Órdenes de compra»). El botón **«Importar»** abre una ventana que ya trae **QuickBooks** como origen.
- **Hoy la importación no pasaría**: Advance Solutions tiene **0 proveedores** y las dos órdenes saldrían «Bloqueada». Hay que crear dos proveedores antes de grabar.
- Archivo de demostración propuesto: **dos facturas del archivo real** (12 líneas del CSV original): `PDC-04292026` de ADVANCE LOGISTICS LLC (tiras 52800 y monitor 070120) y `PDC-05282026` de PADIMONT HOLDINGS CORP (guantes NECH 1001 a 1004).
- Duración en pantalla: unos **70 s** con lo recomendado; **45 s** el mínimo.

---

## 1. Qué es y dónde está (menú, ruta, permiso, módulo)

**Qué es.** La lista de órdenes de compra a proveedores de la compañía y, desde el Lote 239, un importador que crea órdenes de compra a partir de un archivo (QuickBooks o genérico). Una orden de compra dice «se compró»; **no mueve el inventario** (eso lo hace el recibo, escena 3).

| Dato | Valor | Fuente |
|---|---|---|
| Menú | Grupo **«Contabilidad»** (`nav.groups.money`) → ítem **«Compras»** (`nav.purchaseOrders.title`) | `routes.tsx:239-244`, `es.json:238`, `es.json:301` |
| Título de la pantalla | **«Órdenes de compra»** (`warehouse.purchaseOrders.title`), subtítulo «Órdenes de compra a proveedores: líneas, estatus y faltantes.» (`warehouse.purchaseOrders.subtitle`) | `PurchaseOrderListScreen.tsx:350-351`, `es.json:6092-6093` |
| Ruta | `/warehouse/purchase-orders`; ficha en `/warehouse/purchase-orders/:publicId` | `routes.tsx:239`, `routes.tsx:246` |
| Permiso para ver | `purchasing.view` | `routes.tsx:241` |
| Permiso para importar y crear | `purchasing.manage` (los botones «Importar» y «Nueva orden de compra» solo salen con él) | `PurchaseOrderListScreen.tsx:354-363`, `PurchaseOrderImportController.cs:27` |
| Módulo | `PURCHASING` (Compras) | `routes.tsx:242`, `PurchaseOrderImportController.cs:20` |
| Servicio que usa la ventana | `POST /api/v1/purchase-orders/import` (multipart: `file`, `source`, `status`, `warehousePublicId`, `commit`) | `PurchaseOrderImportModal.tsx:19-31`, `PurchaseOrderImportController.cs:27-37` |
| Pantalla hermana (proveedores) | Grupo **«Almacén»** → **«Proveedores»** (`nav.suppliers.title`), `/warehouse/suppliers`, permiso `suppliers.view` para verla y `purchasing.manage` para crear, módulo `PURCHASING` | `routes.tsx:230-236`, `SupplierListScreen.tsx:284-291` |

**Ojo con el manual.** El capítulo 06 §8a y el manual de pantallas dicen «Almacén → Órdenes de compra» (`06-inventario-y-almacen.md:3060`, `frontend/f6-almacen-e-inventario.md:1413`). Eso quedó viejo: desde el Lote 150 el ítem está en **Contabilidad** y se llama **«Compras»** (`routes.tsx:243`; la prueba `navigation.test.ts:66-74` fija el orden del grupo). La narración debe decir «Contabilidad, Compras».

### 1.1 La lista

- Arriba a la derecha: **«Importar»** (`poImport.button`) y **«Nueva orden de compra»** (`warehouse.purchaseOrders.new`) (`PurchaseOrderListScreen.tsx:354-363`).
- Filtros (`PurchaseOrderListScreen.tsx:377-383`): **«Estatus»**, **«Proveedor»** y **«Almacén»** (selección múltiple con buscador), **«Producto»**, **«Fecha»** (desplegable de rangos: «Todas las fechas», «Este mes», «Personalizado»…), el interruptor **«Solo los asignados a mí»** (`warehouse.assign.mineOnly`) y **«Limpiar»** (`ui.filters.clear`).
- Panel «Órdenes de compra» con el total en la esquina (`PurchaseOrderListScreen.tsx:386`).
- Columnas (`PurchaseOrderListScreen.tsx:297-319`): **«Número»** (PO-#####), **«Proveedor»**, **«Almacén»** (el código del almacén), **«Estatus»** (chip de color), **«Fecha esperada»**, **«Asignado a»** (`warehouse.assign.column`) y **«Fecha»** (la de la orden). **No hay columna de costo ni de total.**
- Orden de las filas: por fecha de la orden, la más reciente arriba (`PurchaseOrderService.cs:101-102`).
- Un clic en la fila abre la ficha (`PurchaseOrderListScreen.tsx:408`). En las órdenes «Enviada» o «Recibida parcial» salen dos íconos al final de la fila: **«Tomar»** (`purchasing.receive`, si nadie la tiene) y **«Asignar»** (`warehouse.manage`) (`PurchaseOrderListScreen.tsx:325-342`).
- Al pie: paginación y **«Exportar»** (Excel, CSV, PDF) (`PurchaseOrderListScreen.tsx:407`, `es.json:2991`).

**Estatus de la orden de compra** (las etiquetas vienen del catálogo de la base, no de `es.json`; Solutions no las tiene cambiadas): **Borrador** (DRAFT, inicial) → **Enviada** (SENT) → **Recibida parcial** (PARTIAL) → **Recibida** (RECEIVED); **Cancelada** (CANCELLED). Solo se recibe contra una orden **Enviada** o **Recibida parcial** (`logistica-db-seed.sql:526`, `PurchaseOrderRules.cs:116-117`, `06-inventario-y-almacen.md:2527-2531`).

### 1.2 La ficha de la orden

- Título: **«PO-00002 · PADIMONT HOLDINGS CORP»** (número · proveedor); debajo el chip del estatus, la fecha de la orden y «Fecha esperada: —» (`PurchaseOrderDetailScreen.tsx:405-411`).
- Arriba a la derecha, botón rojo **«Eliminar»** (`warehouse.purchaseOrders.delete`) mientras la orden no tenga recepciones (`PurchaseOrderDetailScreen.tsx:414-420`).
- Barra de etapas: 1 Borrador, 2 Enviada, 3 Recibida parcial; renglón **«Cierres»** con Recibida y Cancelada. En una orden **Enviada** el único botón de la barra dice **«Avanzar a Cancelada»** y es el botón azul principal (`PurchaseOrderDetailScreen.tsx:57`, `:425-437`; `StatusPipeline.tsx:69-70`, `:117-121`; se ve así en la captura vieja `docs/manual/frontend/img/f6-orden-compra-ficha.png`).
- Pestañas **«Líneas»** y **«Faltantes»** (`PurchaseOrderDetailScreen.tsx:441-449`).
- Pestaña «Líneas»: **«Proveedor»**, **«Almacén»**, **«Fecha esperada»**, **«Notas»** y, por cada línea, **«Producto»** (SKU · nombre), **«Cantidad ordenada»** y **«Costo unitario»** (`PurchaseOrderDetailScreen.tsx:213-269`). En una orden Enviada todo sale deshabilitado (solo lectura) y no hay «Guardar» ni «Agregar línea» (`PurchaseOrderDetailScreen.tsx:203`, `:292-304`; la capacidad de editar está negada en SENT y Solutions no la cambió).
- Pestaña «Faltantes» antes de recibir: «No hay faltantes en esta orden de compra.» (`es.json:6154`).

### 1.3 La ventana «Importar órdenes de compra»

Todo sale de `PurchaseOrderImportModal.tsx` y de las claves `poImport.*` (`es.json:12606-12634`). **Nadie la ha mirado todavía en una captura** (el Lote 239 no tomó capturas ni hizo recorrido: `lote239-decisiones.md:28` y `:31`).

| Elemento | Etiqueta en pantalla | Clave |
|---|---|---|
| Título | «Importar órdenes de compra» | `poImport.title` |
| Texto de ayuda | «Una orden de compra dice que se compró; no mueve el inventario (eso lo hace el recibo). «Revisar» no guarda nada. Una orden con un producto o proveedor que no existe no se importa: se bloquea y se dice por qué.» | `poImport.help` |
| Origen | «Origen del archivo»: **«QuickBooks»** (viene elegido) o «Archivo genérico (CSV o Excel)» | `poImport.source`, `poImport.sourceQuickBooks`, `poImport.sourceGeneric` |
| Ayuda del origen QuickBooks | «Exportación de compras de QuickBooks Desktop (por ejemplo «Transaction Detail» con las columnas Type, Date, Num, Name, Item, Qty y Cost Price). Se importan las órdenes de compra y las facturas de compra; los recibos y créditos se omiten.» | `poImport.helpQuickBooks` |
| Estatus | «Estatus de las órdenes importadas»: **«Borrador»** (viene elegido) o **«Completada (enviada, lista para recibir)»** | `poImport.status`, `poImport.statusDraft`, `poImport.statusSent` |
| Ayuda del estatus | «Completada no recibe nada: solo marca la orden como enviada para que después se reciba contra ella.» | `poImport.statusHelp` |
| Almacén | «Almacén»: **solo sale si la compañía tiene más de un almacén**. Solutions tiene uno: no sale. | `poImport.warehouse` (`PurchaseOrderImportModal.tsx:71-72`, `:127-134`) |
| Archivo | «Archivo» con el botón **«Elegir archivo»** (después dice «Cambiar archivo») y el nombre del archivo al lado | `poImport.file`, `poImport.choose`, `poImport.changeFile` |
| Botones del pie | «Cancelar», **«Revisar»**, **«Importar {n}»** (apagado hasta revisar y mientras n sea 0). Después de importar solo queda **«Cerrar»** | `common.cancel`, `poImport.review`, `poImport.confirm`, `poImport.close` |
| Resumen al revisar | «{total} órdenes en el archivo: {ready} listas, {blocked} bloqueadas, {duplicates} ya importadas, {skipped} omitidas.» | `poImport.reviewSummary` |
| Resumen al importar | «Se crearon {created} órdenes. Bloqueadas: {blocked}. Ya importadas: {duplicates}. Omitidas: {skipped}.» y el aviso «{n} órdenes de compra importadas.» | `poImport.doneSummary`, `poImport.doneToast` |
| Nota roja si hay bloqueadas | «{n} órdenes NO se importan porque les falta un producto o un proveedor. Dé de alta lo que falta y vuelva a importar el mismo archivo: lo ya importado no se repite.» | `poImport.blockedNote` |
| Botón de lo que falta | **«Descargar lo que falta»** (baja `missing-products.csv`: SKU, descripción, cantidad, costo y los proveedores que faltan) | `poImport.downloadMissing` (`PurchaseOrderImportModal.tsx:40-46`, `:166-170`) |
| Columnas de la tabla | «Proveedor», «Número», «Fecha», «Líneas», «Estado», «Detalle» | `poImport.col.*` |
| Estados | **«Lista»** (READY), **«Creada»** (CREATED), **«Bloqueada»** (BLOCKED), «Error» (ERROR), **«Ya importada»** (DUPLICATE), **«Omitida»** (SKIPPED) | `poImport.docStatus.*` |

**Reglas que decide el servidor** (`PurchaseOrderImportService.cs`, `PurchaseOrderImportRules.cs`):

- De QuickBooks se importan los tipos **Purchase Order** y **Bill**; **Item Receipt** y **Credit** salen «Omitida» (`PurchaseOrderImportRules.cs:158-159`, `PurchaseOrderImportService.cs:290-297`).
- Las filas de proveedor y las de «Total …» del archivo se ignoran (no traen `Type`) (`QuickBooksCsvReader.cs:211-213`, `PurchaseOrderImportService.cs:231-232`). El proveedor se lee de la columna `Name` (o `Source Name`), el número de `Num`, la fecha de `Date`, la fecha esperada de `Deliv Date`, y el SKU de la celda `Item`, que trae «SKU (Descripción)» (`PurchaseOrderImportService.cs:233-237`, `PurchaseOrderImportRules.cs:95-103`).
- El producto se busca por SKU sin mayúsculas, espacios, guiones ni signos: «NECH 1001» = «NECH-1001» (`PurchaseOrderImportRules.cs:80-87`). Solo cuentan los productos propios de la compañía (`PurchaseOrderImportService.cs:320`).
- **Producto que no existe → la orden se bloquea** con «Falta el producto {sku}: dé de alta el producto con ese SKU y vuelva a importar.» (`PurchaseOrderImportRules.cs:58-61`). **Proveedor que no existe → se bloquea** con «Falta el proveedor «{nombre}»: dé de alta el proveedor con ese nombre y vuelva a importar.» (`PurchaseOrderImportRules.cs:64`). El nombre del proveedor se compara con el mismo criterio del SKU (`PurchaseOrderImportRules.cs:90`, `PurchaseOrderImportService.cs:71`, `:95`).
- Una línea con cantidad 0 se omite; un producto repetido en la misma orden se suma en una línea; un costo con más de 4 decimales se redondea; un costo vacío toma el costo de compra del producto (`PurchaseOrderImportService.cs:131-145`, `06-inventario-y-almacen.md:3085`).
- La orden nace Borrador y, si se pidió «Completada», enseguida se envía (queda **Enviada**) con el comentario «Importada: se marca como enviada al importar.» (`PurchaseOrderImportService.cs:190-194`). No toca el inventario (prueba `PurchaseOrderImportTests.cs:131-136`).
- **No duplica**: la orden guarda en sus notas «Importada de QuickBooks {número}. [QB:PROVEEDOR|NÚMERO]» y el mismo archivo la reconoce (`PurchaseOrderImportRules.cs:138-155`, `PurchaseOrderImportService.cs:85-90`, `:334-352`).
- «Revisar» no guarda nada (`PurchaseOrderImportService.cs:182`; prueba `PurchaseOrderImportTests.cs:112`).
- Con origen QuickBooks el archivo se lee **solo como CSV** (texto), aunque el selector de archivos acepte también `.xlsx` (`PurchaseOrderImportService.cs:219-227`, `PurchaseOrderImportModal.tsx:144`). Tope: 2 MB y 2000 órdenes (`CsvParser.cs:19`, `PurchaseOrderImportRules.cs:45`).

---

## 2. Qué conviene mostrar, en orden (lo que pidió el dueño y lo que ayuda a entenderlo)

Luis pidió: enseñar cómo se importa el archivo de QuickBooks (una o dos órdenes), ver las órdenes ya creadas y pasar al recibo.

1. **Llegar a la pantalla**: Contabilidad → Compras. La lista sale vacía (Solutions no tiene órdenes de compra). *Pedido por Luis.*
2. *(Opcional, 5 a 8 s)* **El archivo de QuickBooks**: un plano fijo del CSV abierto, para que se vea que es el mismo que sale de QuickBooks. Es un recurso aparte (captura de Excel); en la web no hay vista previa del archivo. *Ayuda a entender.*
3. **«Importar»**: la ventana, con **QuickBooks** como origen, y el estatus **«Completada (enviada, lista para recibir)»**. *Pedido por Luis.*
4. **«Elegir archivo»** y **«Revisar»**: «2 órdenes en el archivo: 2 listas…» y las dos filas con el chip «Lista». Aquí va la idea de fondo: revisar no guarda nada. *Pedido por Luis.*
5. *(Opcional, 8 a 10 s)* **Una orden «Bloqueada» con su motivo**. Ver la decisión 3 de la sección 8.
6. **«Importar 2»**: las filas pasan a «Creada» con sus números PO-00001 y PO-00002. *Pedido por Luis.*
7. **«Cerrar»** y la **lista con las dos órdenes** en estatus «Enviada». *Pedido por Luis.*
8. **Abrir una orden** (recomiendo la de PADIMONT): proveedor, estatus Enviada, sus cuatro líneas con el SKU de Teikem (NECH-1001…) y la nota «Importada de QuickBooks PDC-05282026». *Ayuda a entender.* Aquí se ven los costos: decisión 4 de la sección 8.
9. **Frase de puente** a la escena 3: las órdenes están enviadas, el inventario no se ha movido; falta recibirlas.

Qué **no** hace falta mostrar: los filtros de la lista, «Nueva orden de compra» (alta manual), la pestaña «Faltantes», el origen «Archivo genérico», «Descargar lo que falta».

---

## 3. Paso a paso exacto del recorrido

Supone: sesión abierta en Advance Solutions con `purchasing.manage`, interfaz en español, los dos proveedores ya creados (sección 4.4) y el archivo de demostración de dos órdenes (sección 4.3). `dialog` = `page.getByRole('dialog', { name: 'Importar órdenes de compra' })`. Los localizadores salen del código (no se probaron en pantalla).

| # | Acción del usuario | Etiqueta en pantalla (es) y clave | Cómo localizarlo en Playwright | Qué debe verse después |
|---|---|---|---|---|
| 1 | Abrir el grupo del menú | «Contabilidad» — `nav.groups.money` | `page.getByRole('button', { name: 'Contabilidad' })` | Se despliegan sus ítems (el grupo que estaba abierto se cierra: solo hay uno abierto a la vez) |
| 2 | Entrar a la pantalla | «Compras» — `nav.purchaseOrders.title` | `page.getByRole('link', { name: 'Compras', exact: true })` (con `exact`: también existe «Contabilización de compras») | URL `/warehouse/purchase-orders`; título «Órdenes de compra»; botones «Importar» y «Nueva orden de compra»; tabla vacía con «Sin resultados» (`ui.table.empty`) |
| 3 | Pulsar el botón de importar | «Importar» — `poImport.button` | `page.getByRole('button', { name: 'Importar', exact: true })` | Se abre la ventana «Importar órdenes de compra» (`poImport.title`) con el texto de ayuda |
| 4 | Comprobar el origen (no hay que tocarlo) | «Origen del archivo» = «QuickBooks» — `poImport.source`, `poImport.sourceQuickBooks` | `dialog.getByLabel('Origen del archivo')` debe tener el valor `QUICKBOOKS` | Debajo, la ayuda del origen QuickBooks |
| 5 | Elegir el estatus | «Estatus de las órdenes importadas» → «Completada (enviada, lista para recibir)» — `poImport.status`, `poImport.statusSent` | `dialog.getByLabel('Estatus de las órdenes importadas').selectOption('SENT')` | El desplegable muestra «Completada (enviada, lista para recibir)»; debajo: «Completada no recibe nada: solo marca la orden como enviada para que después se reciba contra ella.» |
| 6 | Elegir el archivo | «Elegir archivo» — `poImport.choose` | `const [fc] = await Promise.all([page.waitForEvent('filechooser'), dialog.locator('label.cs-pick').click()]); await fc.setFiles(ruta)` (mismo patrón de `lote132-integraciones.spec.ts:201-202`). Alternativa sin clic: `dialog.locator('#poi-file').setInputFiles(ruta)` | El botón pasa a decir «Cambiar archivo» y a su lado sale el nombre del archivo |
| 7 | Revisar | «Revisar» — `poImport.review` | `dialog.getByRole('button', { name: 'Revisar' })` | `dialog.getByTestId('poi-summary')`: «2 órdenes en el archivo: 2 listas, 0 bloqueadas, 0 ya importadas, 0 omitidas.» Tabla con 2 filas (`tr[data-status="READY"]`): ADVANCE LOGISTICS LLC · PDC-04292026 · 2026-05-12 · 2 · «Lista», y PADIMONT HOLDINGS CORP · PDC-05282026 · 2026-06-09 · 4 · «Lista». El botón del pie dice «Importar 2» |
| 8 | Importar | «Importar 2» — `poImport.confirm` | `dialog.getByRole('button', { name: 'Importar 2' })` | Aviso flotante «2 órdenes de compra importadas.» (`page.getByTestId('toast')`, dura 3.5 s); resumen «Se crearon 2 órdenes. Bloqueadas: 0. Ya importadas: 0. Omitidas: 0.»; las filas pasan a «Creada» (`tr[data-status="CREATED"]`) y la columna «Número» ahora muestra **PO-00001** y **PO-00002**; en el pie solo queda «Cerrar» |
| 9 | Cerrar la ventana | «Cerrar» — `poImport.close` | `dialog.locator('footer').getByRole('button', { name: 'Cerrar' })` (hay otros dos «Cerrar»: la ✕ de la ventana y la del aviso) | La lista se refresca sola y muestra 2 órdenes; el panel marca 2 |
| 10 | Mirar la lista | Columnas «Número», «Proveedor», «Almacén», «Estatus», «Fecha esperada», «Asignado a», «Fecha» — `warehouse.purchaseOrders.columns.*`, `warehouse.assign.column` | `page.getByRole('table', { name: 'Órdenes de compra' })` | Fila 1: PO-00002 · PADIMONT HOLDINGS CORP · ALM-SOLUTIONS · chip «Enviada» · (vacío) · — · 06/09/2026. Fila 2: PO-00001 · ADVANCE LOGISTICS LLC · ALM-SOLUTIONS · «Enviada» · (vacío) · — · 05/12/2026 |
| 11 | Abrir una orden | Clic en la fila (no hay botón) | `page.getByRole('table', { name: 'Órdenes de compra' }).getByRole('row').filter({ hasText: 'PADIMONT HOLDINGS CORP' }).click()` | URL `/warehouse/purchase-orders/<id>`; título «PO-00002 · PADIMONT HOLDINGS CORP»; chip «Enviada»; etapas Borrador ✓ → Enviada → Recibida parcial; pestaña «Líneas» con Proveedor, Almacén ALM-SOLUTIONS, Notas «Importada de QuickBooks PDC-05282026. [QB:PADIMONTHOLDINGSCORP\|PDC-05282026]» y 4 líneas: NECH-1001 · Padimont Gloves Small 1/100ct. (1800), NECH-1002 (2700), NECH-1003 (2700), NECH-1004 (1800), cada una con «Costo unitario» 3.5 |
| 12 | *(Opcional)* Ver que no hay faltantes | «Faltantes» — `warehouse.purchaseOrders.tabShortages` | `page.getByRole('tab', { name: 'Faltantes' })` | «No hay faltantes en esta orden de compra.» |
| 13 | Volver a la lista | «Compras» — `nav.purchaseOrders.title` | `page.getByRole('link', { name: 'Compras', exact: true })` | La lista con las 2 órdenes; de aquí se pasa a la escena 3 (Almacén → Recibo) |

**Variante con una orden «Bloqueada»** (opcional; se graba **antes** de crear el proveedor PADIMONT, y «Revisar» no deja rastro):

| # | Acción | Etiqueta y clave | Playwright | Qué debe verse |
|---|---|---|---|---|
| B1 | Pasos 3 a 7 con un solo proveedor creado (ADVANCE LOGISTICS LLC) | — | igual | Resumen «2 órdenes en el archivo: 1 listas, 1 bloqueadas, 0 ya importadas, 0 omitidas.»; nota roja «1 órdenes NO se importan porque les falta un producto o un proveedor…» (`poImport.blockedNote`); botón «Descargar lo que falta»; fila de PADIMONT con el chip «Bloqueada» (`tr[data-status="BLOCKED"]`) y en «Detalle», en rojo: «Falta el proveedor «PADIMONT HOLDINGS CORP»: dé de alta el proveedor con ese nombre y vuelva a importar.» |
| B2 | Cancelar | «Cancelar» — `common.cancel` | `dialog.getByRole('button', { name: 'Cancelar' })` | Se cierra sin guardar nada |
| B3 | Crear el proveedor (sección 4.4) | «Nuevo proveedor» — `warehouse.suppliers.new` | ver 4.4 | «Proveedor creado.» |
| B4 | Repetir los pasos 1 a 9 | — | — | Ahora las dos salen «Lista» |

**Comprobación opcional de «no duplica»** (después del paso 9; no guarda nada): repetir los pasos 3 a 7 con el mismo archivo. Debe verse «2 órdenes en el archivo: 0 listas, 0 bloqueadas, 2 ya importadas, 0 omitidas.», las filas con el chip «Ya importada», el número PO de cada una y el detalle «Ya se importó antes (orden de compra PO-00001); no se vuelve a crear.»; el botón queda en «Importar 0», apagado (`PurchaseOrderImportService.cs:85-90`, `PurchaseOrderImportRules.cs:72`).

---

## 4. Requisitos de datos y de configuración para que funcione con Advance Solutions

Todas las consultas se corrieron en solo lectura contra la base local `Teikem` con `TenantId = 2`.

### 4.1 Tabla de requisitos

| # | Requisito | Estado HOY | Consulta que lo prueba (resumen del resultado) | Si falta, cómo se arregla |
|---|---|---|---|---|
| 1 | Módulo Compras encendido | **Cumple** | `SELECT ModuleKey, IsEnabled FROM TenantModule WHERE TenantId = 2` → `PURCHASING = 1` (y `WMS_LOTSERIAL = 1`) | — |
| 2 | **Proveedores de las órdenes del archivo** | **FALTA: hay 0** | `SELECT COUNT(*) FROM Supplier WHERE TenantId = 2` → 0 | Crear **ADVANCE LOGISTICS LLC** y **PADIMONT HOLDINGS CORP** (sección 4.4) |
| 3 | Productos de las órdenes | **Cumple** | `SELECT Sku, IsActive, ClientId, PurchaseCost … FROM Product WHERE TenantId = 2` → 42 productos, todos propios y activos, sin lote ni serie. Existen `52800` (costo 3.05), `070120` (5.25) y `NECH-1001` a `NECH-1004` (3.50). Ninguna clave de SKU se repite (42 claves distintas) | — |
| 4 | Un almacén activo (para que la ventana no pida almacén) | **Cumple** | `SELECT Code, Name, IsActive FROM Warehouse WHERE TenantId = 2` → 1 fila: `ALM-SOLUTIONS`, activo | — |
| 5 | Sin órdenes de compra previas (para que salgan PO-00001 y PO-00002) | **Cumple** | `SELECT COUNT(*) FROM PurchaseOrder WHERE TenantId = 2` → 0; `SELECT Kind, NextValue FROM NumberSequence WHERE TenantId = 2` → solo `SALESORDER` (el contador de compras todavía no existe: el primer número será el 1) | Cada ensayo gasta números (los PO no se reutilizan): ver riesgo 9 |
| 6 | Estatus con sus etiquetas normales | **Cumple** | `SELECT s.InternalCode, s.LabelJson, o.CustomLabelJson FROM StatusCode s LEFT JOIN StatusCodeOverride o … TenantId = 2 WHERE s.Entity = 'PurchaseOrderStatus'` → Borrador, Enviada, Recibida parcial, Recibida, Cancelada; sin cambios de Solutions | — |
| 7 | Cuenta con permiso para importar | **Existe, con una traba** | Roles de Solutions con `purchasing.manage`: `TenantAdmin` (1 persona) y `Sales Supervisor` (1 persona). Las dos tienen la verificación en dos pasos encendida (`TwoFactorEnabled = 1`). La cuenta de administrador de los recorridos (`staffHelpers.ts:11`) existe, es administradora de plataforma, entra por omisión a la compañía 2 y **también** tiene dos pasos encendidos | Asunto común a todas las escenas: definir con qué cuenta se graba y cómo se pasa el segundo paso (no lo resolví aquí) |
| 8 | Archivo de demostración con dos órdenes | **No existe todavía** | — | Armarlo con las 12 líneas de la sección 4.3 |
| 9 | Interfaz en español y fechas de la compañía | **Cumple** | `SELECT DefaultLangCode, DateOrder, DateSeparator FROM Tenant WHERE TenantId = 2` → `es`, `MDY`, `/` (las fechas de la lista salen como 05/12/2026) | En el recorrido, fijar el idioma como hacen los recorridos (`test.use({ locale: 'es-PR' })`, `lote240-fotos-de-productos.spec.ts:11`) |
| 10 | API de pruebas sin correo y la web levantados | **No hay nada corriendo** (no levanté nada) | — | Asunto común: `scripts/api-para-pruebas.ps1` (sin llave de Brevo) y Vite; nunca el API real del dueño |
| 11 | La ventana revisada en pantalla antes de grabar | **Pendiente** | El Lote 239 no tomó capturas ni hizo recorrido del importador (`lote239-decisiones.md:28`, `:31`) | Un ensayo previo con captura y mirarla (regla de pantallas del 2026-11-12) |

### 4.2 Análisis de `F:/Download/Solutions Purchases.CSV`

El archivo tiene 97 líneas (ASCII, fin de línea CRLF, 29 columnas con la primera vacía): 1 de encabezado, 6 filas de proveedor, 6 de «Total proveedor», 1 «TOTAL» y 83 de detalle. Agrupado como lo agrupa el importador (proveedor + número) da **19 documentos: 11 facturas (Bill) y 8 que se omiten** (7 recibos «Item Receipt» y 1 crédito). No trae ningún documento de tipo «Purchase Order».

Las 11 facturas, comparadas con los productos de Solutions en la base (clave de SKU sin guiones ni espacios):

| # | Proveedor | Número | Fecha | Filas del CSV | Líneas (archivo → orden) | Total del archivo | Resultado contra los productos de Teikem |
|---|---|---|---|---|---|---:|---|
| 1 | ADVANCE DEPOT SOLUTIONS | CORRUGADOS | 06/10/2026 | 3-5 | 3 → — | 268.00 | **Se bloquea**: falta el producto `101010` (`979` y `121212` sí existen) |
| 2 | ADVANCE LOGISTICS LLC | PDC04132026 | 04/20/2026 | 8-14 | 7 → — | 57,050.00 | **Se bloquea**: falta `53310/53350` (un renglón de QuickBooks para dos productos; sugiere 53310 y 53350). Además el monitor viene en dos líneas, una sin costo |
| 3 | ADVANCE LOGISTICS LLC | **PDC-04292026** | 05/12/2026 | **15-16** | 2 → 2 | 20,500.00 | **Limpia**: `52800` × 5000 a 3.05 y `070120` × 1000 a 5.25 |
| 4 | FARMACIA CENTRAL DRUG | prestamo de lanzetas | 04/08/2026 y 05/04/2026 | 43 y 45 | 2 → 1 | 1,500.00 | Limpia, pero son **dos facturas con el mismo número**: el importador las junta en una sola orden (`24-081028` × 2000) |
| 5 | FARMACIA CENTRAL DRUG | prestamo monitores | 04/17/2026 | 44 | 1 → 1 | 2,100.00 | Limpia: `070120` × 400 |
| 6 | GLOBAL IMPORT SALES LLC | INV/2026/04239 | 08/06/2026 | 48-64 | 17 → 17 | 11,418.73 | Limpia |
| 7 | GLOBAL IMPORT SALES LLC | inv/2026/04641 | 08/25/2026 | 65 | 1 → 1 | 424.44 | Limpia |
| 8 | GLOBAL IMPORT SALES LLC | INV/2026/04808 | 09/02/2026 | 67-72 | 6 → 6 | 5,401.04 | Limpia |
| 9 | GLOBAL IMPORT SALES LLC | INV/2025/04947 | 09/10/2026 | 73-78 | 6 → 6 | 5,242.56 | Limpia |
| 10 | PADIMONT HOLDINGS CORP | **PDC-05282026** | 06/09/2026 | **81-84** | 4 → 4 | 31,500.00 | **Limpia, con SKU igualado**: `NECH 1001`…`NECH 1004` del archivo son `NECH-1001`…`NECH-1004` de Teikem; 1800, 2700, 2700 y 1800 a 3.50 |
| 11 | SHIELD LINE | 1060366 | 09/17/2026 | 87-95 | 9 → 9 | 27,920.71 | Limpia, pero 8 costos traen 5 decimales: se redondean a 4 y quedan distintos del costo guardado del producto (en la ficha saldría el aviso «El producto tiene guardado X; este precio es distinto.») |

Omitidos: 7 recibos de ADVANCE LOGISTICS LLC (filas 17 a 40) y el crédito `RINV/2026/00085` de GLOBAL IMPORT SALES LLC (fila 66).

**Resultado de importar el archivo completo:**

- Por productos: **9 facturas entrarían limpias y 2 se bloquean** (las mismas que encontró el Lote 239: `lote239-decisiones.md:18`).
- **Hoy, con 0 proveedores, las 11 se bloquean** por «Falta el proveedor…»: el resumen diría «19 órdenes en el archivo: 0 listas, 11 bloqueadas, 0 ya importadas, 8 omitidas.»

El análisis se hizo con una réplica de las reglas del importador en un script temporal fuera del repositorio; no se llamó al API. Los seis nombres de proveedor del archivo existen tal cual, activos, en la lista de proveedores de QuickBooks (`F:/Download/TeikemMigracion/Solutions Vendors.csv`, columna `Vendor`, 39 proveedores).

### 4.3 Archivo de demostración propuesto (solo la especificación; no lo creé)

**Las dos facturas: la 3 y la 10 de la tabla.** Razones: traen justo los productos que se venden en la escena 4 (tiras `52800` y monitor `070120`, las dos piezas del kit; y los guantes), entran limpias, su costo es igual al guardado en el producto (no sale ningún aviso de precio), son cortas (2 y 4 líneas) y la de guantes enseña que el SKU de QuickBooks con espacio se iguala al de Teikem con guion. La de ADVANCE LOGISTICS tiene solo 2 líneas: es la más rápida de recibir en la escena 3.

Líneas del CSV original que se conservan, **en este orden y sin tocar el contenido de las líneas de detalle** (12 líneas, unos 2.4 KB; ASCII, fin de línea CRLF como el original):

| Línea original | Qué es | Contenido |
|---|---|---|
| 1 | Encabezado | La fila de columnas completa (empieza con coma: `,"Type","Date","Num","Source Name",…,"Amount Paid"`) |
| 7 | Fila de proveedor | `"ADVANCE LOGISTICS LLC",,,,…` |
| 15 | Detalle | `Bill` · 05/12/2026 · PDC-04292026 · `52800 (PRODIGY STRIPS 1/50CT.)` · 5000 · 3.05 · 15250.00 |
| 16 | Detalle | `Bill` · 05/12/2026 · PDC-04292026 · `070120 (PRODIGY MONITOR 1EA.)` · 1000 · 5.25 · 5250.00 |
| 41 | Total del proveedor | `"Total ADVANCE LOGISTICS LLC",…` (ver la nota de los totales) |
| 80 | Fila de proveedor | `"PADIMONT HOLDINGS CORP",,,,…` |
| 81 | Detalle | `Bill` · 06/09/2026 · PDC-05282026 · `NECH 1001 (Padimont Gloves Small 1/100ct.)` · 1800 · 3.50 · 6300.00 |
| 82 | Detalle | `Bill` · 06/09/2026 · PDC-05282026 · `NECH 1002 (Padimont Gloves Medium 1/100ct.)` · 2700 · 3.50 · 9450.00 |
| 83 | Detalle | `Bill` · 06/09/2026 · PDC-05282026 · `NECH 1003 (Padimont Gloves Large 1/100ct.)` · 2700 · 3.50 · 9450.00 |
| 84 | Detalle | `Bill` · 06/09/2026 · PDC-05282026 · `NECH 1004 (Padimont Gloves X-Large 1/100ct.)` · 1800 · 3.50 · 6300.00 |
| 85 | Total del proveedor | `"Total PADIMONT HOLDINGS CORP",,,,,,,,,,,,,,,,,,9000,,31500.00,0.00,31500.00,31500.00,,,,,` (ya cuadra: ese proveedor solo tiene esa factura) |
| 97 | Total general | `"TOTAL",…` (ver la nota de los totales) |

**Nota de los totales.** El importador ignora las filas de proveedor y las de total, así que el resultado es el mismo con ellas o sin ellas. Pero las líneas 41 y 97 originales traen los totales del archivo completo (133,960 unidades y 422,375.00 para ADVANCE LOGISTICS; 195,748 y 508,150.48 en total), que no cuadran con dos facturas y además son cifras reales de compras. Si el archivo se va a ver en pantalla, hay tres salidas:

- **(a) recomendada si se puede**: pedirle a la oficina el mismo reporte de QuickBooks filtrado a esas dos facturas. Sale un archivo real, con sus totales bien.
- **(b)** recalcular las dos filas: línea 41 → `"Total ADVANCE LOGISTICS LLC",,,,,,,,,,,,,,,,,,6000,,20500.00,0.00,20500.00,20500.00,,,,,` y línea 97 → `"TOTAL",,,,,,,,,,,,,,,,,,15000,,52000.00,0.00,52000.00,52000.00,,,,,`. (En un reporte real, la columna `Balance` de las líneas 15 y 16 sería 15250.00 y 20500.00; el original trae 72300.00 y 77550.00 porque arrastra la factura anterior.)
- **(c)** dejar las 12 líneas tal cual y **no enseñar el archivo abierto**.

Con ese archivo y los dos proveedores creados, «Revisar» debe dar: 2 órdenes, 2 listas, 0 bloqueadas, 0 ya importadas, 0 omitidas. Las órdenes quedan con las marcas `[QB:ADVANCELOGISTICSLLC|PDC-04292026]` y `[QB:PADIMONTHOLDINGSCORP|PDC-05282026]`.

**Si Luis quiere además una bloqueada por producto** (en vez de la variante por proveedor de la sección 3): añadir al principio las líneas originales 2 a 6 (ADVANCE DEPOT SOLUTIONS, factura CORRUGADOS: `979`, `101010` y `121212`) y crear también el proveedor ADVANCE DEPOT SOLUTIONS. Saldría «Bloqueada» con «Falta el producto 101010: dé de alta el producto con ese SKU y vuelva a importar.» y el resumen «3 órdenes en el archivo: 2 listas, 1 bloqueadas…». Esa orden se quedaría sin importar (el producto 101010 no existe en Solutions).

Lugar sugerido para el archivo: fuera de git (trae costos reales), por ejemplo una carpeta del video dentro de `F:\Download\TeikemMigracion\`. Luis decide si se versiona.

### 4.4 Proveedores que hay que crear

Para el archivo de dos órdenes, exactamente estos dos, **con el nombre tal como viene en el archivo** (columna `Name`):

1. `ADVANCE LOGISTICS LLC`
2. `PADIMONT HOLDINGS CORP`

(Y `ADVANCE DEPOT SOLUTIONS` solo si se usa la variante de la bloqueada por producto.) La comparación no distingue mayúsculas, espacios ni signos, pero el nombre tiene que tener las mismas letras: «Advance Logistics» sin «LLC» **no** casa.

Cómo crearlos, de mejor a peor:

- **Alta manual en la pantalla (recomendada: son dos)**. Almacén → Proveedores → **«Nuevo proveedor»** (`warehouse.suppliers.new`) → **«Nombre»** (`warehouse.suppliers.fields.name`, el único obligatorio) → **«Guardar»** (`ui.form.save`) → aviso «Proveedor creado.» (`warehouse.suppliers.created`). En Playwright, como en `f6.spec.ts:353-359`: `page.goto('/warehouse/suppliers')`, `page.getByRole('button', { name: 'Nuevo proveedor' })`, `page.getByRole('dialog').getByLabel(/^Nombre/).fill(…)`, `getByRole('button', { name: 'Guardar' })`. Queda en la bitácora de auditoría. También puede hacerse por el API (`POST /api/v1/suppliers` con el nombre), que es lo mismo.
- **Importación de proveedores con plantilla** (Proveedores → «Importar», `entityImport.open`). Solutions **no tiene** plantilla de proveedores (`SELECT Kind, Name FROM ImportTemplate WHERE TenantId = 2` → solo `BIN` y `PRODUCT`): habría que crearla primero («Crear formato estándar») y preparar un archivo. Para dos proveedores no compensa. El archivo de proveedores de QuickBooks trae 39 y la mayoría no son de compra de mercancía.
- **Script**: no existe ninguno para proveedores de Solutions (ni en `scripts/solutions-pedido-vendedores/` ni en `scripts/demo/`), y lo que se cambia por script no sale en la bitácora (`scripts/solutions-pedido-vendedores/LEEME.md:14`). No lo recomiendo. Tampoco la herramienta de migración del Lote 10: según su propio reporte normaliza los SKU de otra forma («NECH1001», sin guion) y no comprobé qué haría con los productos que ya existen.

Los proveedores se crean **fuera de cámara**, como preparación, salvo que Luis quiera enseñar el alta (decisión 5).

---

## 5. Riesgos al grabar (validaciones, ventanas, avisos, esperas, cosas que se ven feas, datos sensibles en pantalla)

1. **Sin los proveedores, todo sale «Bloqueada»** y el botón queda en «Importar 0», apagado. Es el estado de hoy.
2. **La ventana del importador nunca se ha visto en una captura** y no hay constancia de una importación confirmada contra SQL Server real (las pruebas del servicio corren en memoria: `PurchaseOrderImportTests.cs:35-39`). Hay que ensayarla y mirarla antes de la toma buena.
3. **El estatus viene en «Borrador»**. Si no se cambia a «Completada», las órdenes nacen en Borrador y la escena 3 no puede recibirlas sin pasar antes cada una a Enviada.
4. **Cambiar el origen, el estatus o el archivo borra el resultado de «Revisar»** (`PurchaseOrderImportModal.tsx:113`, `:121`, `:150-151`). Orden correcto: origen, estatus, archivo, Revisar, Importar.
5. **Lo que el video de Playwright no capta**: la lista desplegada de un `<select>` nativo (Origen y Estatus cambian de valor «de golpe») y la ventana del sistema para escoger el archivo. Conviene acercar la imagen al campo o marcarlo en la edición. Usar el patrón `filechooser` para que el clic en «Elegir archivo» se vea sin que se abra la ventana del sistema.
6. **Avisos y botones repetidos**: el aviso «2 órdenes de compra importadas.» dura 3.5 s (`toastStore.ts:41`) y tiene su propia ✕ llamada «Cerrar»; la ventana tiene otra ✕ «Cerrar» (`Modal.tsx:72`) y el pie el botón «Cerrar». Usar el del pie.
7. **Datos sensibles en pantalla**:
   - La **ficha de la orden muestra el «Costo unitario» real** de cada línea (3.05, 5.25, 3.5). La lista y la ventana de importar **no** muestran costos ni totales.
   - El **CSV**, si se enseña abierto, trae costo, importe y saldo por línea, y los totales de compra por proveedor.
   - Los **nombres de los proveedores** son reales.
   - No sale ningún dato de personas en esta escena.
8. **Cosas que se ven raras o feas** (todas confirmadas en el código):
   - En la ventana, la columna «Fecha» sale en formato técnico **2026-05-12**; en la lista, la misma fecha sale 05/12/2026 (`PurchaseOrderImportModal.tsx:188`).
   - Las **notas** de la orden muestran la marca técnica `[QB:PADIMONTHOLDINGSCORP|PDC-05282026]`.
   - En la ficha de una orden Enviada, el único botón azul dice **«Avanzar a Cancelada»** y al lado hay un **«Eliminar»** rojo. No pulsarlos y no detenerse ahí.
   - Los campos de la ficha salen apagados (grises) porque la orden Enviada no se edita.
   - Se escoge **«Completada»** y la lista dice **«Enviada»**: la narración tiene que unir las dos palabras.
   - El menú dice **«Compras»** y la pantalla **«Órdenes de compra»**.
   - **Sin plurales**: con un solo documento se lee «1 órdenes en el archivo: 1 listas…» y «1 órdenes NO se importan…» (`i18n.ts:81-91` solo sustituye el número). Por eso conviene el archivo de **dos** órdenes; la variante de la bloqueada enseña ese «1 bloqueadas».
   - **El aviso de que el SKU se igualó no se ve**: el servidor manda «El SKU NECH 1001 del archivo se tomó como NECH-1001 de Teikem…», pero la ventana solo pinta los avisos que bloquean (y los de las omitidas y ya importadas) (`PurchaseOrderImportModal.tsx:192`). El manual dice que «se avisa en la fila» (`06-inventario-y-almacen.md:3081`) y hoy no es así. Para enseñarlo hay que comparar el archivo («NECH 1001») con la ficha («NECH-1001»).
   - En una orden bloqueada, la columna «Líneas» no cuenta la línea que falta (`PurchaseOrderImportService.cs:147`, `:175-176`).
   - La ventana tiene tres textos de ayuda largos: mucha letra en pantalla.
   - «Fecha esperada» queda vacía en las dos (el archivo no trae `Deliv Date`) y «Asignado a» muestra «—».
9. **Repetir la toma**: los números PO no se reutilizan. Si se borran las dos órdenes y se vuelve a importar, salen PO-00003 y PO-00004 (borrar una orden importada sí permite volver a crearla con el mismo archivo: `06-inventario-y-almacen.md:3095`). Para que la toma buena muestre PO-00001 y PO-00002 hay que partir de la base en su estado inicial (copia o restauración de la base de grabación).
10. **Fechas del archivo**: las facturas son de mayo y junio de 2026; las órdenes quedarán con esa fecha y se «recibirán» en noviembre.
11. **Con origen QuickBooks, un `.xlsx` no sirve** aunque el selector lo deje escoger: daría «El archivo no tiene la columna 'Type'.». El archivo tiene que ser CSV.
12. **Idioma**: grabar con la interfaz en español; las etiquetas de este mapa son las de `es.json`.
13. **No tocar «Tomar» ni «Asignar»** en las filas de la lista: asignan la orden a una persona y cambian lo que verá la escena 3.

---

## 6. Datos para la narración (hechos verificados, en frases cortas y en lenguaje de usuario)

- Las compras no se teclean: entran con el mismo archivo que sale de QuickBooks.
- Está en Contabilidad, Compras. Arriba, el botón Importar.
- El origen ya viene en QuickBooks. También acepta un archivo genérico.
- Se escoge cómo entran las órdenes: en Borrador, o Completadas.
- Completada quiere decir enviada, lista para recibir. No recibe nada.
- Revisar no guarda nada. Dice, orden por orden, si está lista o qué le falta.
- Si falta un producto o un proveedor, esa orden se bloquea y dice por qué.
- Teikem nunca crea un producto a medias. Se da de alta lo que falta y se vuelve a importar el mismo archivo.
- Lo que ya se importó no se repite: sale como «Ya importada».
- De QuickBooks entran las órdenes y las facturas de compra. Los recibos y los créditos se omiten.
- Importar crea las órdenes y les da su número: PO-00001 y PO-00002.
- En la lista quedan en estatus Enviada.
- Una orden de compra dice que se compró. No mueve el inventario. El inventario sube cuando se recibe.
- No importa si en QuickBooks el código tiene un espacio: «NECH 1001» de QuickBooks es el «NECH-1001» de Teikem.
- Cada orden guarda de qué documento de QuickBooks vino.
- En este ejemplo son dos compras: una a Advance Logistics, con 5,000 tiras Prodigy y 1,000 monitores; y una a Padimont, con guantes en cuatro tallas: 1,800 Small, 2,700 Medium, 2,700 Large y 1,800 X-Large.
- El siguiente paso es recibirlas en el almacén.

Para no decir de más: el archivo real de Solutions no trae «órdenes de compra» de QuickBooks sino **facturas de compra** (tipo Bill); Teikem las convierte en órdenes de compra (`lote239-decisiones.md:22`). La frase segura es «las compras de QuickBooks».

---

## 7. Duración estimada en pantalla (segundos) y qué se puede recortar si hay que acortar

| Tramo | Segundos |
|---|---:|
| Menú Contabilidad → Compras y la lista vacía | 5 |
| Botón Importar y la ventana (origen QuickBooks) | 7 |
| Estatus «Completada» | 5 |
| Elegir archivo | 4 |
| Revisar y leer el resultado (2 listas) | 10 |
| Importar 2 (aviso, «Creada», números PO) | 7 |
| Cerrar y la lista con las 2 órdenes Enviadas | 8 |
| Abrir la orden de PADIMONT y recorrer sus líneas | 12 |
| Volver a la lista y frase de puente al recibo | 5 |
| **Núcleo** | **63** |
| Opcional: plano del archivo de QuickBooks al principio | +7 |
| Opcional: la orden «Bloqueada» con su motivo (solo revisar) | +10 |
| Opcional: «Ya importada» (no duplica) | +8 |

**Estimado recomendado: 70 s** (núcleo más el plano del archivo). Con todo lo opcional, unos 90 s.

Si hay que acortar, en este orden: (1) quitar «Ya importada»; (2) quitar la bloqueada; (3) quitar el plano del archivo; (4) no abrir la ficha y quedarse en la lista (ahorra 12 s y además evita enseñar los costos); (5) acelerar en edición la lectura de la ventana. **Mínimo que todavía cuenta la historia: 45 s** (Importar → Completada → archivo → Revisar → Importar 2 → lista).

---

## 8. Dudas y decisiones para Luis

**Decisiones**

1. **¿Estas dos compras para el ejemplo?** `PDC-04292026` de ADVANCE LOGISTICS LLC (5,000 tiras y 1,000 monitores) y `PDC-05282026` de PADIMONT HOLDINGS CORP (guantes). Recomiendo que sí: son las de los productos que luego se venden.
2. **¿Se importan como «Completada (enviada, lista para recibir)»?** Recomiendo que sí: así la escena del recibo empieza directo.
3. **¿Se enseña unos segundos una orden «Bloqueada»?** Recomiendo que sí, 8 a 10 s y solo en «Revisar»: es la regla que más protege (no entra nada a medias y dice qué falta). La forma más natural es la del proveedor que todavía no existe (no pide otro archivo). Costo: 10 s más y el texto «1 órdenes NO se importan…», que se lee mal. Si se prefiere un video corto, se quita.
4. **¿Pueden verse en el video los costos reales y los nombres reales de los proveedores?** La ficha de la orden muestra el costo unitario (3.05, 5.25, 3.50). Opciones: enseñarla tal cual; taparla en la edición; o no abrir la ficha. Recomiendo abrirla y tapar la columna de costo si el video lo van a ver vendedores o almacén.
5. **¿Los dos proveedores se crean fuera de cámara?** Recomiendo que sí (preparación), salvo que se use la variante de la bloqueada, donde crear PADIMONT es parte de la historia.
6. **¿Se enseña el archivo de QuickBooks abierto?** Si sí, hay que decidir entre pedirle a la oficina un reporte filtrado a esas dos facturas o recortar el archivo y recalcular los totales (sección 4.3). Y dónde se guarda el archivo, que trae costos reales.
7. **¿Cómo se le llama al archivo en la narración?** El archivo trae facturas de compra (Bill), no órdenes de compra. Recomiendo decir «las compras de QuickBooks».
8. **¿Fechas originales (mayo y junio de 2026) o fechas de esta semana?** Recomiendo dejar las originales: es el archivo real y al importador le da igual.
9. **Dos detalles de pantalla que hoy se ven mal: ¿se arreglan antes de grabar o se dejan?** (a) el aviso de que el SKU se igualó no sale en las órdenes listas, aunque el manual dice que sí; (b) el botón azul «Avanzar a Cancelada» en la ficha de una orden Enviada. Los dos son cambios chicos de la web, pero son cambios: hoy no se tocó nada. Recomiendo grabar sin tocarlos y apuntarlos como pendientes.

**Dudas que no pude confirmar**

- **Qué reporte de QuickBooks produce ese archivo** (nombre y filtros). La pantalla dice «por ejemplo Transaction Detail». Hace falta para que la narración le diga a la oficina qué exportar, y **en CSV**.
- **Si en producción Solutions ya tiene proveedores.** En la copia local (TEST del 2026-11-11) hay 0; si en producción también, la oficina tendrá que crearlos antes de su primera importación y eso merece una frase en el video.
- **Cómo se ve de verdad la ventana** (anchos, cortes de texto, altura de las filas): solo leí el código.
- **El paso de la verificación en dos pasos** para entrar a grabar con una cuenta de Solutions (requisito 7).
- En el archivo completo hay dos cosas de datos que no afectan al ejemplo pero conviene que Luis sepa: dos facturas distintas con el mismo número («prestamo de lanzetas», de abril y de mayo) se juntarían en una sola orden; y la factura `PDC04132026` trae 600 monitores sin costo que, al sumarse con los 2,000 de la otra línea, quedarían al costo de 5.25.
- El manual (capítulo 06 §8a y el de pantallas) todavía dice «Almacén → Órdenes de compra»; hoy es Contabilidad → Compras. No lo corregí (solo lectura).
- En la base local el kit que se vende no se llama OFERTA1 sino `KIT-001`, dentro de la oferta «OFERTA DEL DIA» (6 × `52800` + 1 × `070120`); el script `02-kit-oferta1.sql` no está aplicado ahí. Las piezas son las mismas, así que el ejemplo de compras no cambia; es asunto de la escena 4.

---

## 9. Fuentes (archivo:línea)

**Web**

- `web-app/src/app/routes.tsx:229-250` (Proveedores y Compras: ruta, permiso, módulo, grupo del menú); `:392-400` (los demás ítems de Contabilidad)
- `web-app/src/app/navigation.ts:16-23`; `web-app/src/app/navigation.test.ts:66-74`; `web-app/src/app/AppShell.tsx:129-130`, `:240`, `:259-285`
- `web-app/src/features/warehouse/PurchaseOrderListScreen.tsx:297-319`, `:325-342`, `:350-363`, `:377-386`, `:392-409`, `:414`
- `web-app/src/features/warehouse/PurchaseOrderDetailScreen.tsx:57`, `:84-87`, `:203-232`, `:245-269`, `:292-304`, `:405-449`
- `web-app/src/features/warehouse/PurchaseOrderImportModal.tsx:19-36`, `:40-57`, `:64-87`, `:96-108`, `:110-156`, `:158-199`
- `web-app/src/features/warehouse/SupplierListScreen.tsx:178-180`, `:284-291`, `:355`; `web-app/src/features/imports/EntityImportModal.tsx:13-18`, `:136-140`
- `web-app/src/features/warehouse/ProductPriceNote.tsx:7-8`, `:16-20`; `web-app/src/features/warehouse/api.ts:160-161`
- `web-app/src/kernel/ui/Modal.tsx:63-72`, `:84`; `web-app/src/kernel/ui/Toaster.tsx:14-25`; `web-app/src/kernel/ui/toastStore.ts:41`; `web-app/src/kernel/ui/SearchSelect.tsx:142-182`; `web-app/src/kernel/ui/Filters.tsx:29-36`; `web-app/src/kernel/ui/DataTable.tsx:507`, `:552-554`; `web-app/src/kernel/ui/Tabs.tsx:18-23`
- `web-app/src/kernel/catalogs/StatusPipeline.tsx:69-70`, `:84-100`, `:108-125`; `web-app/src/kernel/catalogs/transitions.ts:41-49`
- `web-app/src/kernel/i18n/i18n.ts:81-91`; `web-app/src/kernel/format/format.ts:131-139`
- `web-app/src/features/consignees/consignees.css:41-45`
- `web-app/src/kernel/i18n/es.json:4`, `:237-238`, `:297`, `:301`, `:2788-2795`, `:2978-2979`, `:2991`, `:3007`, `:3046`, `:3056`, `:4624-4628`, `:6057-6090`, `:6091-6154`, `:6935`, `:12606-12634`, `:12636`, `:12642`
- `web-app/openapi.json` (esquemas `PurchaseOrderImportDocDto`: `date` con formato `date`; `PurchaseOrderImportResultDto`; `PurchaseOrderDto`)

**Servidor**

- `src/Teikem.Api/Controllers/PurchaseOrderImportController.cs:17-37`
- `src/Teikem.Infrastructure/Services/PurchaseOrderImportService.cs:52-73`, `:85-101`, `:104-149`, `:173-177`, `:182-214`, `:219-240`, `:283-308`, `:312-331`, `:334-352`
- `src/Teikem.Domain/Wms/PurchaseOrderImportRules.cs:19-30`, `:45`, `:48-72`, `:80-90`, `:95-103`, `:138-159`
- `src/Teikem.Domain/Wms/PurchaseOrderRules.cs:38`, `:51`, `:116-117`, `:248-255`
- `src/Teikem.Infrastructure/Services/PurchaseOrderService.cs:101-102`, `:128-161`, `:171-199`, `:313-332`, `:365-379`, `:431-438`
- `src/Teikem.Infrastructure/Migration/QuickBooksCsvReader.cs:155-172`, `:187-217`
- `src/Teikem.Infrastructure/Wms/WmsResolve.cs:38-48`; `src/Teikem.Infrastructure/Orders/NumberSequenceService.cs:24-38`
- `src/Teikem.Domain/Wms/ProductPriceRules.cs:16-31`; `src/Teikem.Domain/Orders/CsvParser.cs:19-21`
- `Diseño/logistica-db-seed.sql:526` (estatus de la orden de compra y sus etiquetas)
- `tests/Teikem.Tests/PurchaseOrderImportTests.cs:20-31`, `:35-39`, `:83-113`, `:116-142`, `:145-159`

**Manual y documentos**

- `docs/manual/06-inventario-y-almacen.md:2433-2444`, `:2527-2536`, `:2800-2806`, `:3056-3095`
- `docs/manual/faq.md:12240-12255`
- `docs/manual/frontend/f6-almacen-e-inventario.md:1363-1397`, `:1408-1461`; capturas viejas `docs/manual/frontend/img/f6-ordenes-compra.png` y `f6-orden-compra-ficha.png`
- `docs/lote239-decisiones.md:8-12`, `:17-19`, `:22-28`, `:31`; `docs/lote238-decisiones.md`; `docs/lote240-decisiones.md`
- `scripts/solutions-pedido-vendedores/LEEME.md:14`, `:32`, `:59`

**Recorridos de Playwright que sirven de modelo**

- `web-app/e2e/f6.spec.ts:351-391` (proveedor nuevo y orden de compra: localizadores de la lista, la ficha y los avisos)
- `web-app/e2e/lote132-integraciones.spec.ts:201-202` (subir un archivo con `filechooser`)
- `web-app/e2e/staffHelpers.ts:10-11`, `:19-33`, `:52-63`; `web-app/e2e/lote240-fotos-de-productos.spec.ts:5`, `:11`; `web-app/playwright.config.ts:13-19`

**Datos**

- `F:/Download/Solutions Purchases.CSV`: líneas 1, 2-6, 7, 8-16, 41, 43-45, 48-78, 80-85, 86-97
- `F:/Download/TeikemMigracion/Solutions Vendors.csv` (columna `Vendor`: los seis nombres existen, activos)
- `F:/Download/TeikemMigracion/reporte-solutions-20261109-1626.md` (reporte de la migración: SKU normalizados sin guion)
- Base local `Teikem`, TenantId 2, solo `SELECT`: tablas `Supplier`, `PurchaseOrder`, `NumberSequence`, `Warehouse`, `TenantModule`, `Product`, `StockBalance`, `StatusCode`, `StatusCodeOverride`, `StatusCapability`, `ImportTemplate`, `Role`, `RolePermission`, `Permission`, `UserRole`, `UserTenant`, `Tenant`, `Offer`, `OfferItem`, `ProductKitComponent`
