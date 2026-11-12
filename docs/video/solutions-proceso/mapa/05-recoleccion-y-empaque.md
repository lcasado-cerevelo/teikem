# Escena 5 - Almacén: Recolección y empaque y salida de la orden de venta: mapa para el video

Investigado el 2026-11-12 sobre `master` (commit `e605395b`) y la base local `Teikem`, compañía Advance Solutions (TenantId 2). No se levantó ningún servidor ni se cambió nada: todo sale del código, del manual y de consultas `SELECT`. Lo que no se pudo ejecutar va marcado «según el código».

**Lo más importante, antes de leer el resto**

1. En la web hay **dos salidas distintas** y solo una despacha la orden de venta:
   - **Despachar contra la orden** (`POST /api/v1/sales-orders/{id}/dispatch`): consume la reserva, baja el inventario, crea la recolección `EMP-#####` enlazada a la orden y cambia el estatus de la orden. En la web solo se llega por **Contabilidad → Ventas → abrir la orden → «Despachar»** (o por «Recolectar juntos», que exige 2 o más documentos del mismo cliente).
   - **Recolección suelta** (`POST /api/v1/pick-batches`): es el formulario «Recolección» del panel izquierdo de Recolección y empaque. No lleva ningún enlace a la orden y solo toma existencia **no reservada**.
2. El botón **«Recolectar»** de la ficha del documento (Lote 238) solo **llena** ese formulario suelto. Si después se pulsa **«Recolectar (bajar de inventario)»**, según el código se crea una recolección suelta: la orden **sigue «Solicitada»**, su reserva queda intacta y el inventario bajaría otra vez cuando la orden se despache de verdad. El manual y la FAQ le dicen al usuario que termine con ese botón, y el recorrido de Playwright del Lote 238 se detiene antes de pulsarlo. **Hay que decidirlo antes de grabar** (sección 8, decisión 1).
3. Para Advance Solutions **basta con despachar**: la orden queda «Despachada» sin crear entrega. Crear la entrega («Empacar y crear envío») hoy **fallaría** en Solutions: no hay contratos ni tarifas (0 y 0) y el módulo de Envíos está apagado.

---

## 1. Qué es y dónde está (menú, ruta, permiso, módulo)

| Pantalla | Menú (es) | Ruta | Permiso de la ruta | Módulo | Fuente |
|---|---|---|---|---|---|
| Recolección y empaque | Almacén → «Recolección y empaque» (`nav.groups.warehouse`, `nav.pickBatches.title`) | `/warehouse/pick-batches` (`?view=moves`, `?view=groups`, `?tab=replenish`) | `pickbatches.view` | `WMS_LOTSERIAL` | `web-app/src/app/routes.tsx:271-277`, `PickBatchListScreen.tsx:30-59,108-146` |
| Ficha de una recolección (página) | sin ítem; se llega desde la orden o la lista | `/warehouse/pick-batches/:publicId` | `pickbatches.view` | `WMS_LOTSERIAL` | `routes.tsx:278-283`, `PickBatchDetailScreen.tsx:19-71` |
| Órdenes de venta | Contabilidad → «Ventas» (`nav.groups.money`, `nav.salesOrders.title`); el título de la pantalla es «Órdenes de venta» (`sales.title`) | `/sales-orders` (`?order=<publicId>` abre la orden) | `sales.view` | `SALES_ORDERS` | `routes.tsx:394`, `SalesOrdersScreen.tsx:323,338` |
| Kárdex de movimientos | Almacén → «Kárdex de movimientos» (`nav.kardex.title`) | `/warehouse/kardex` (`?tab=balances` = Saldos) | `kardex.view` | `WMS_LOTSERIAL` | `routes.tsx:376-382`, `InventoryScreen.tsx:1-19` |
| Productos e inventario | Almacén → «Productos e inventario» (`nav.products.title`) | `/warehouse/products` | `products.view` | `WMS_LOTSERIAL` | `routes.tsx:215-222` |

**Qué permiso pide cada acción (lo valida el servidor)**

| Acción | Permiso | Fuente |
|---|---|---|
| Entrar a Recolección y empaque y leer documentos, recolecciones y fichas | `pickbatches.view` + `inventory.view` | `src/Teikem.Api/Controllers/PickBatchesController.cs:20-22,32,44,83` |
| Botón «Recolectar» de la ficha, «Recolectar (bajar de inventario)», «Recolectar juntos» | `warehouse.pick` | `PickBatchesController.cs:67,92`; `PickBatchListScreen.tsx:41,76-83` |
| Ver la orden de venta | `sales.view` (módulo `SALES_ORDERS`) | `SalesOrdersController.cs:20,28,48` |
| «Despachar» la orden | `warehouse.pick` | `SalesOrdersController.cs:96-98`; `OrderModal.tsx:721-725` |
| «Crear entrega» / «Empacar» (crea el envío) | `warehouse.pick` + `orders.create` | `SalesOrdersController.cs:101-102`; `PickBatchService.cs:626-632`; `PickBatchDetailBody.tsx:43-49` |
| «Eliminar» una recolección (deshace el despacho) | `warehouse.pick` (+ `orders.cancel` si ya está empacada) | `PickBatchesController.cs:119-125`; `PickBatchService.cs:703-721` |
| Despacho manual (sin entrega) | `warehouse.issue` | `CollectPanel.tsx:320-326` |
| Kárdex y saldos (datos) | `inventory.view` | `InventoryController.cs:70,90,111,131` |

**El usuario de almacén.** El rol `WarehouseOperator` de Solutions ya trae `pickbatches.view`, `inventory.view`, `warehouse.pick`, `warehouse.issue`, `sales.view`, `kardex.view` y `products.view`: puede ver la ficha y despachar la orden. **No** trae `orders.create` (no puede crear la entrega) ni `orders.cancel`. El rol `TenantAdmin` los trae todos (consulta en la sección 4, requisito 5).

**Lo que solo existe en la app del aparato (no en la web)** — `docs/manual/18-ordenes-de-venta.md:188-197`, `docs/manual/09-app-almacen.md:1308-1328`, `docs/manual/frontend/l63-despachar-orden-de-venta.md:11`:
- La guía **posición por posición** («Ve a A-01»), leyendo la posición con el escáner y marcando ✓.
- **Sacar de otra posición** (la reserva se mueve sola) y **Omitir** una parte.
- El despacho de un kit **pieza por pieza** («Pieza del kit … (2 por kit)») y el conteo de kits completos.
- Despachar **sin señal** (cola de salida) y despacho + entrega en una sola llamada.
- En la web, la ventana «Despachar» solo deja escribir **cuánto** sale de cada línea; no se escoge posición ni lote (`OrderModal.tsx:545,563`).

---

## 2. Qué conviene mostrar, en orden (lo que pidió el dueño y lo que ayuda a entenderlo)

Lo pedido: «que almacén vaya a Recolección y empaque y de ahí gestione la salida de esa orden de venta».

1. **La orden ya está en el almacén.** Almacén → Recolección y empaque: en «Documentos» aparece la orden de la escena 4 con «Orden de venta», «Solicitada», «Sacado 0 de N» y «Por sacar N». Mensaje: Ventas la solicitó y el sistema ya apartó la mercancía.
2. **Qué hay que sacar y de dónde.** Clic en el documento: sección «Lo que hay que recolectar» con Producto, Pedido, Sacado, Por sacar y «Sacar de» (posición y cantidad reservada). El **kit** se ve por sus **piezas**, cada una con la etiqueta «Kit {SKU}».
3. **Despachar.** Hoy, sin tocar código: Contabilidad → Ventas → abrir la orden → «Despachar» → confirmar. Sale el aviso «Despacho EMP-##### creado: el inventario salió del almacén.» y la pregunta «¿Qué sigue con lo despachado?» → «Retiro: el cliente lo recoge (listo)».
4. **La orden cambió de estatus.** En la misma ventana: «Despachada» y la tabla «Despachos de la orden» con el número EMP.
5. **El documento del almacén.** De vuelta en Recolección y empaque: la orden en «Despachada», «Sacado N de N», barra al 100 % y su recolección (número, quién, cuándo, cantidad).
6. **La salida del inventario.** Kárdex de movimientos: renglones «Despacho» en negativo con origen «Recolección EMP-#####». Y Productos e inventario: Disponible, Reservado y Total del producto ya rebajados (cierra el círculo con la escena 1).

Qué no conviene mostrar: el formulario «Recolección» de la izquierda (es la recolección suelta), «Despacho manual (sin entrega)», la pestaña «Reabasto», «Cargas», «Recolectar juntos», «Empacar» y «Eliminar».

---

## 3. Paso a paso exacto del recorrido

`OV-N` = la orden de la escena 4. Con el contador de hoy (`SALESORDER` en 5) sería **OV-00005**; la primera recolección de Solutions sería **EMP-00001** (no hay contador `PACKBATCH` todavía). Las ventanas son `role="dialog"` con el título como nombre accesible (`web-app/src/kernel/ui/Modal.tsx:63-65`); las pestañas son `role="tab"` (`Tabs.tsx:18-24`); los avisos son `.toast.on` (`e2e/staffHelpers.ts:22`).

### Ruta A — la que funciona hoy sin tocar código

| # | Acción del usuario | Etiqueta en pantalla (es) y clave i18n | Cómo localizarlo en Playwright | Qué debe verse después |
|---|---|---|---|---|
| 1 | Abrir Almacén → Recolección y empaque | «Almacén» `nav.groups.warehouse`; «Recolección y empaque» `nav.pickBatches.title` | `page.getByRole('link', { name: 'Recolección y empaque' }).click()` (si el grupo está plegado, antes `page.getByRole('button', { name: 'Almacén' })`); o `page.goto('/warehouse/pick-batches')` | Título «Recolección y empaque» (`warehouse.pickBatches.title`); pestañas «Recolecciones» / «Reabasto» (`…tabBatches`, `…tabReplenish`); a la izquierda el panel «Recolección» (`…collectPanel.title`); a la derecha «Por documento» / «Cargas» / «Por movimiento» (`…docs.viewDocuments`, `…groups.view`, `…docs.viewMoves`) y el panel «Documentos» (`…docs.title`) con su contador |
| 2 | (Opcional) Buscar la orden | Campo «Buscar por número o cliente…» `warehouse.pickBatches.docs.searchPlaceholder` | `page.getByPlaceholder('Buscar por número o cliente…').fill('OV-N')` | Queda una sola fila o tarjeta: `OV-N` con «Orden de venta» (`…docs.kinds.SALES_ORDER`), cliente, «Solicitada», «Sacado 0 de N» (`…docs.shippedOf`), barra en 0 %, «Por sacar N» (`…docs.toCollect`), Movimientos 0 y Última recolección «—» |
| 3 | Clic en la fila del documento | columnas «Documento», «Cliente», «Estatus», «Avance», «Movimientos», «Última recolección» (`…docs.columns.*`) | `page.locator('tr, .dt-cards > li').filter({ hasText: 'OV-N' }).first().click()`; `const ficha = page.getByRole('dialog', { name: /OV-N/ })` | Ficha con título «OV-N · Orden de venta · Solicitada»; arriba «Cliente», «Pedido», «Sacado», «Reservado por sacar» (`…docs.ordered`, `…docs.shipped`, `…docs.reserved`); sección **«Lo que hay que recolectar»** (`…docs.linesTitle`) con columnas «Producto», «Pedido», «Sacado», «Por sacar», «Sacar de» (`…docs.colProduct`, `…docs.colToCollect`, `…docs.colFrom`); cada pieza del kit con la etiqueta «Kit {SKU}» (`…docs.kitOf`) y su posición y cantidad («PISO · 30»); abajo «Recolecciones (0)» y «Todavía no tiene recolecciones.» (`…docs.movesTitle`, `…docs.noMoves`); pie «Cerrar» y «Recolectar» |
| 4 | Cerrar la ficha (no pulsar «Recolectar») | «Cerrar» `warehouse.pickBatches.detail.close` | `ficha.getByRole('button', { name: 'Cerrar' }).last().click()` (la X también se llama «Cerrar», `ui.modal.close`) | Vuelve la lista |
| 5 | Abrir Contabilidad → Ventas | «Contabilidad» `nav.groups.money`; «Ventas» `nav.salesOrders.title` | `page.getByRole('link', { name: 'Ventas', exact: true }).click()` o `page.goto('/sales-orders')` | Título «Órdenes de venta» (`sales.title`); pestañas «Órdenes», «Backorders», «Devoluciones», «QuickBooks» (`sales.tabs.*`); la orden `OV-N` con «Solicitada» |
| 6 | Clic en la fila de la orden | columnas «Número», «Cliente», «Estatus»… (`sales.col.*`) | `page.locator('tr, .dt-cards > li').filter({ hasText: 'OV-N' }).first().click()`; `const orden = page.getByRole('dialog', { name: /^Orden OV-/ })`. Directo: `page.goto('/sales-orders?order=<publicId>')` | Ventana «Orden OV-N» (`sales.view.title`): estatus «Solicitada», insignias («Recogido» `sales.badge.pickup` o «Entrega» `sales.badge.delivery`), tabla «Productos» con «Pedido», «Reservado», «Backorder», «Despachado»; «Componentes de los kits» (`sales.kit.sectionTitle`) con «Por kit», «Pedido», «Reservado», «En backorder», «Despachado»; pie «Despachar», «Cancelar orden», «Cerrar» |
| 7 | Pulsar «Despachar» | «Despachar» `sales.dispatch.button` | `orden.getByRole('button', { name: 'Despachar', exact: true }).click()`; `const dm = page.getByRole('dialog', { name: /Despachar la orden/ })` | Ventana «Despachar la orden OV-N» (`sales.dispatch.title`) con la ayuda «Se despacha de lo que está reservado para esta orden…» (`sales.dispatch.help`); tabla «Producto», «Reservado», «Despachar ahora» (`sales.dispatch.qty`) con lo reservado ya escrito (el kit, en kits); «Nota (opcional)» |
| 8 | Confirmar «Despachar» | «Cantidad a despachar de {sku}» `sales.dispatch.qtyOf`; «Despachar» `sales.dispatch.confirm` | `await expect(dm.getByLabel('Cantidad a despachar de KIT-001')).toHaveValue('5')`; `dm.getByRole('button', { name: 'Despachar', exact: true }).click()` | Aviso «Despacho EMP-00001 creado: el inventario salió del almacén.» (`sales.dispatch.done`) y la ventana «Despacho EMP-00001» (`sales.next.title`) con «¿Qué sigue con lo despachado?» (`sales.next.question`) |
| 9 | Elegir retiro | «Retiro: el cliente lo recoge (listo)» `sales.next.pickup` (las otras: «Entrega: crear la orden de transporte…» `sales.next.delivery`, «Decidirlo después» `sales.next.later`) | `page.getByRole('dialog', { name: /^Despacho EMP-/ }).getByRole('button', { name: /Retiro/ }).click()` | La orden muestra «Despachada»; ya no hay botón «Despachar»; «Despachado» = «Pedido»; tabla «Despachos de la orden» (`sales.dispatches.title`) con «Despacho» EMP-00001, estatus «Despachado» (`sales.dispatches.statuses.COLLECTED`), «Unidades», «Cuándo» y «Entrega» «—»; aparece «Pedir devolución» |
| 10 | (Opcional) Abrir la recolección desde la orden | el número EMP es un enlace | `orden.getByRole('link', { name: /EMP-/ }).click()` → `/warehouse/pick-batches/<publicId>` | Página «EMP-00001 · ALM-SOLUTIONS», chip «Recolectada», «Resumen» (`warehouse.pickBatches.detail.summary`) con «Recolectada», «Orden de venta» (enlace de vuelta), «Cantidad», «Costo total»; «Líneas» con «Producto», «Cantidad», «Posición», «Lote», «Serie», «Costo unitario». Ojo: muestra costos |
| 11 | Volver a Recolección y empaque | igual que el paso 1 | `page.goto('/warehouse/pick-batches')` (mejor que el clic del menú: ver riesgo de la sección 5, punto 6) | El documento `OV-N` ahora en «Despachada», «Sacado N de N», barra al 100 %, Movimientos 1 y la fecha en «Última recolección» |
| 12 | Clic en el documento | — | como el paso 3 | Ficha con «Recolecciones (1)»: EMP-00001, «Recolectada», fecha, «Por» (quién) y «Cantidad» (en piezas). Ya no hay botón «Recolectar» |
| 13 | Abrir Almacén → Kárdex de movimientos | «Kárdex de movimientos» `nav.kardex.title`; columnas «Fecha», «Hora», «Tipo», «SKU», «Producto», «Cantidad», «Almacén/Posición», «Origen», «Usuario» (`warehouse.kardexView.columns.*`) | `page.goto('/warehouse/kardex')`; filas `page.locator('tr').filter({ hasText: 'EMP-00001' })` | Arriba (lo más reciente primero) un renglón por pieza: «Tipo» «Despacho», cantidad en negativo y en color de salida, posición de origen, «Origen» «Recolección EMP-00001». Al abrir uno: «Movimiento #id» con «Nota» «KIT-001 × 5 · EMP-00001 · OV-N» y «Movimientos relacionados (n)» |
| 14 | Abrir Almacén → Productos e inventario y buscar el producto | «Productos e inventario» `nav.products.title`; columnas «Disponible», «Reservado», «Total» (`warehouse.products.columns.*`) | `page.goto('/warehouse/products')` y el buscador de la lista | El producto con «Total» rebajado en lo que salió y «Reservado» sin lo de esta orden |

Localizadores tomados de recorridos que ya existen: `web-app/e2e/lote63-despacho-orden-de-venta.spec.ts:95-141`, `web-app/e2e/lote228-pedido-vendedores.spec.ts:449-459` y `web-app/e2e/lote238-modo-captura-y-recoleccion.spec.ts:117-140`.

### Variante B — el botón «Recolectar» de la ficha (solo si se decide mostrarlo o arreglarlo)

| # | Acción | Etiqueta y clave | Playwright | Qué se ve |
|---|---|---|---|---|
| B1 | En la ficha del documento, pulsar «Recolectar» | «Recolectar» `warehouse.pickBatches.docs.collectThis` | `ficha.getByRole('button', { name: 'Recolectar', exact: true }).click()` | La ficha se cierra; aviso «OV-N: renglones cargados en el formulario. Revíselos y recolecte.» (`…docs.preloaded`); el panel «Recolección» queda con el almacén y una línea por pieza y posición reservada |
| B2 | Revisar las líneas | «Producto de la línea 1», «Cantidad de la línea 1», «Posición de la línea 1» (`…collectPanel.lineProduct`, `lineQty`, `lineBin`) | `page.getByLabel('Cantidad de la línea 1')`, `page.getByLabel('Producto de la línea 1')` | Producto, cantidad reservada y posición; bajo el producto, «N disp.» (lo disponible **sin contar** la reserva) |
| B3 | **No pulsar** «Recolectar (bajar de inventario)» | `warehouse.pickBatches.collectPanel.submit`; para vaciar: «Limpiar» `…collectPanel.clear` | `page.getByRole('button', { name: 'Limpiar' })` | Ver la advertencia de arriba: hoy ese botón crea una recolección suelta |

### Variante C — con entrega (hoy no funciona en Solutions)

| # | Acción | Etiqueta y clave | Playwright | Qué se ve |
|---|---|---|---|---|
| C1 | En «¿Qué sigue con lo despachado?», elegir entrega (o «Crear entrega» en la tabla de despachos) | «Entrega: crear la orden de transporte…» `sales.next.delivery`; «Crear entrega» `sales.dispatches.deliver` | `…getByRole('button', { name: /Entrega/ })`; `orden.getByRole('button', { name: 'Crear entrega' })` | Ventana «Empacar EMP-00001» (`warehouse.pickBatches.pack.title`) con el cliente y el consignatario de la orden ya puestos |
| C2 | Llenar y enviar | «Tipo de servicio», «Consignatario», «Tipo de paquete», «Piezas», «Empacar y crear envío» (`warehouse.pickBatches.pack.*`) | `pack.getByLabel('Tipo de servicio').selectOption({ label: 'Estándar' })`, `pack.getByLabel(/^Tipo de paquete/).selectOption({ label: 'Caja' })`, `pack.getByRole('button', { name: 'Empacar y crear envío' })` | Debería salir «Empacada: se creó el envío {número}.» y el despacho pasar a «Con entrega». **En Solutions hoy**: error de tarifa (sección 4, requisito 6) |

---

## 4. Requisitos de datos y de configuración para que funcione con Advance Solutions

Todas las consultas: `sqlcmd -S localhost,1433 -d Teikem -E -C -W -s "|" -Q "SET NOCOUNT ON; …"`.

**1. Una orden de venta solicitada, con todo reservado (viene de la escena 4).**
- Hoy: hay 2 órdenes. `OV-00001` Cancelada (sin líneas) y `OV-00002` **Solicitada** (Recogido, firmada, 1 línea: kit `KIT-001` × 5, reservado 5, sin backorder). Su reserva: 30 de `52800` y 5 de `070120`, las dos en la posición `PISO`.
- Consulta: `SELECT so.Number, sc.InternalCode, so.DeliveryMode FROM dbo.SalesOrder so JOIN dbo.StatusCode sc ON sc.StatusCodeId = so.StatusCodeId WHERE so.TenantId = 2;` y `SELECT so.Number, COALESCE(rp.Sku, lp.Sku), b.Code, r.Quantity FROM dbo.SalesOrderReservation r JOIN dbo.SalesOrderLine l ON l.SalesOrderLineId = r.SalesOrderLineId JOIN dbo.SalesOrder so ON so.SalesOrderId = l.SalesOrderId JOIN dbo.Product lp ON lp.ProductId = l.ProductId LEFT JOIN dbo.Product rp ON rp.ProductId = r.ProductId LEFT JOIN dbo.WarehouseBin b ON b.WarehouseBinId = r.WarehouseBinId WHERE r.TenantId = 2;`
- Qué exige la escena: que la orden nueva no tenga backorder (si queda algo sin reservar termina en «Despachada parcial», no en «Despachada») y que el cliente no esté «por aprobar» (una orden de cliente nuevo no se despacha: `SalesOrderService.Dispatch.cs:57`).
- Un documento solo aparece en Recolección y empaque si está Solicitado o Despachado parcial **con algo reservado**, o si ya tiene una recolección (`PickBatchDocumentService.cs:84-87`).

**2. Existencia en posiciones de donde se pueda sacar.**
- Hoy: 38 productos con existencia, 69,900 unidades, **todo en una sola posición, `PISO`** (zona `PCK`, tipo Picking). El almacén es uno: `ALM-SOLUTIONS` «Almacen Solutions» (sin acento), con 79 posiciones activas (78 de estante tipo `01-A-1` más `PISO`). Reservado hoy: 35 unidades (las de `OV-00002`).
- Consulta: `SELECT b.Code, COUNT(*), SUM(sb.QtyOnHand), SUM(sb.QtyReserved) FROM dbo.StockBalance sb JOIN dbo.WarehouseBin b ON b.WarehouseBinId = sb.WarehouseBinId WHERE sb.TenantId = 2 AND sb.QtyOnHand > 0 GROUP BY b.Code;` → `PISO | 38 | 69900 | 35`.
- Ningún producto de Solutions lleva lote ni serie (41 productos «Ninguno» más el kit): `SELECT tt.InternalCode, COUNT(*) FROM dbo.Product p LEFT JOIN dbo.LookupCode tt ON tt.LookupCodeId = p.TrackingTypeLookupId WHERE p.TenantId = 2 GROUP BY tt.InternalCode;` → `NONE | 42`. **No habrá columna «Lote» en ninguna pantalla de la escena.**
- Productos activos sin existencia: `00607-3`, `00802-2`, `00804-6` (los tres GLOBAL) y el kit (un kit de oferta nunca tiene existencia propia).

**3. Ajustes de despacho y rotación de la compañía.**
- Hoy: `DispatchRequirement = FREE`, `RotationMethod = FEFO` (con lote), `RotationMethodNoLot = FIFO` (sin lote), `RotationEnforcement = SUGGEST`, `ValuationMethod = AVERAGE`.
- Consulta: `SELECT DispatchRequirement, RotationMethod, RotationMethodNoLot, RotationEnforcement, ValuationMethod FROM dbo.Tenant WHERE TenantId = 2;`
- No hay que cambiar nada. Efecto práctico: como los productos no llevan lote, la reserva sale por FIFO, de la existencia **más vieja** (`RotationRules.cs:92-98`, `SalesOrderService.Request.cs:362-384`). Lo que se reciba en la escena 3 de un producto que ya tiene existencia en `PISO` **no** será lo que se reserve: «Sacar de» dirá `PISO`.
- Dónde se cambian: Sistema → «Ajustes de la compañía» (`nav.settings.title`) → pestaña «Operación» (`system.settings.tabs.ops`): «El despacho exige documento» (`system.settings.ops.requirementLabel`) y «Cómo se aplica en las salidas» (`system.settings.ops.rotationEnforcementLabel`). Ese ajuste solo bloquea el **despacho manual**; no afecta despachar la orden (`RotationRules.cs:52-61`, `PickBatchService.cs:238-240`).

**4. Módulos.**
- Hoy encendidos: ANALYTICS, CATALOG, CUSTOM_FIELDS, INTEGRATIONS, PURCHASING, SALES_ORDERS, SYSTEM, TAX, WMS_LOTSERIAL. BILLING apagado. `LTL_GROUND` (Envíos) no tiene fila: apagado.
- Consulta: `SELECT ModuleKey, IsEnabled FROM dbo.TenantModule WHERE TenantId = 2;`
- Para la ruta A no falta ninguno.

**5. Usuario y permisos.**
- Hoy: 2 cuentas con acceso a Solutions, las dos de personas reales (ninguna cuenta de pruebas): una con rol `TenantAdmin` (159 permisos) y una con `Sales Supervisor` (22). Nadie tiene el rol `WarehouseOperator` (44 permisos).
- Consulta: `SELECT r.Name, (SELECT COUNT(*) FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId), (SELECT COUNT(*) FROM dbo.UserRole ur WHERE ur.RoleId = r.RoleId AND ur.TenantId = 2) FROM dbo.Role r WHERE r.TenantId = 2;`
- Si se quiere que en el video «entre el almacén» con su propia cuenta: Sistema → Roles y usuarios → «Nuevo usuario» con el rol `WarehouseOperator` (esa alta no manda correo: `scripts/solutions-pedido-vendedores/LEEME.md`, apartado «Usuarios»). Si no, se graba todo con el administrador.

**6. Solo para la variante con entrega (C).**
- Hoy: la compañía no tiene «Tipo de servicio por defecto» ni «Tipo de paquete por defecto» (los dos `NULL`), así que en «Empacar» son obligatorios (`PackModal.tsx:91-96`). Tipos visibles: Estándar, Express, Mismo día, Programado, Pick up; Caja, Sobre, Tarima.
- Hoy: **0 contratos y 0 tarifas** en Solutions. Al crear el envío el servidor siempre cotiza (`OrderService.cs:390-391`, `OrderStatusService.cs:79-101`); sin tarifa responde 422 «No hay tarifa vigente para {servicio}/{paquete}; configure la tarifa en el contrato del cliente antes de confirmar.» (`OrderQuoteService.cs:31-32,78-79`; `RateService.cs:271-272,321-324`). Todos los tipos de servicio son facturables (ninguno marcado `billable:false` para Solutions).
- Consultas: `SELECT COUNT(*) FROM dbo.Contract WHERE TenantId = 2;` → 0. `SELECT COUNT(*) FROM dbo.RateComponent WHERE TenantId = 2;` → 0. `SELECT DefaultServiceTypeLookupId, DefaultPackageTypeLookupId FROM dbo.Tenant WHERE TenantId = 2;` → NULL, NULL.
- Además el envío quedaría invisible: la pantalla «Envíos» pide el módulo `LTL_GROUND` (`OrdersController.cs:16-18`). Los 701 clientes sí tienen dirección de entrega (701 de tipo DELIVERY).
- Arreglo si se quisiera: encender Envíos, crear contrato y tarifa del cliente y poner los predeterminados en Ajustes → Operación. Recomendación: no hacerlo para este video.

**7. Kit de oferta.**
- Hoy: `KIT-001` «kIT DE mONITOR gRATIS» (mayúsculas invertidas), kit de oferta, no ajustable, $8.50, piezas 6 × `52800` PRODIGY STRIPS 1/50CT. y 1 × `070120` PRODIGY MONITOR 1EA.; está en la oferta «OFERTA DEL DIA» (publicada, para todos, desde 2026-11-10).
- Consulta: `SELECT kp.Sku, kp.Name, cp.Sku, k.Quantity FROM dbo.ProductKitComponent k JOIN dbo.Product kp ON kp.ProductId = k.KitProductId JOIN dbo.Product cp ON cp.ProductId = k.ComponentProductId WHERE k.TenantId = 2;`
- El script `scripts/solutions-pedido-vendedores/02-kit-oferta1.sql` crea el kit real `OFERTA1` (mismas piezas) a $41.94; no está aplicado en esta base. Lo decide la escena 4, pero el nombre del kit se ve en esta escena (ventana «Despachar» y nota del Kárdex).

**8. Lo que ya hay de salidas.**
- Hoy: 0 recolecciones, 0 cargas, 0 avisos de salida, 0 envíos. El Kárdex de Solutions tiene 38 movimientos, todos «Ajuste» del 2026-11-06 (la carga inicial). 0 reglas de aviso: despachar no dispara ningún correo.
- Consulta: `SELECT (SELECT COUNT(*) FROM dbo.PickBatch WHERE TenantId = 2), (SELECT COUNT(*) FROM dbo.PickGroup WHERE TenantId = 2), (SELECT COUNT(*) FROM dbo.ExitNotice WHERE TenantId = 2), (SELECT COUNT(*) FROM dbo.TransportOrder WHERE TenantId = 2), (SELECT COUNT(*) FROM dbo.NotificationRule WHERE TenantId = 2);` → 0, 0, 0, 0, 0.

---

## 5. Riesgos al grabar (validaciones, ventanas, avisos, esperas, cosas que se ven feas, datos sensibles en pantalla)

1. **«Recolectar» + «Recolectar (bajar de inventario)» no despacha la orden** (según el código, sin ejecutar). El formulario manda solo almacén y líneas (`CollectPanel.tsx:552-561`, `collectForm.ts:91-114`, `api.ts:1475-1481`); el servidor crea la recolección sin orden (`PickBatchService.cs:386-398`) y toma de lo disponible = en mano menos reservado (`PickBatchService.cs:510`, `InventoryLedger.cs:163`). Con los datos de hoy la recolección suelta **sí pasaría** (hay 24,100 y 2,332 disponibles) y dejaría el inventario rebajado dos veces al despachar después la orden. Solo el despacho de la orden pone el enlace, consume la reserva y cambia el estatus (`SalesOrderService.Dispatch.cs:163-196`).
2. **Dos botones «Despachar» y dos «Cerrar».** La orden y su ventana de despacho tienen un botón «Despachar» cada una; cada ventana tiene la X («Cerrar») y el botón «Cerrar». Hay que acotar siempre a la ventana (`getByRole('dialog', { name: … })`) y usar `exact` o `.last()`.
3. **Ventanas apiladas.** Orden → «Despachar la orden…» → «Despacho EMP-…» van una encima de otra. La pregunta «¿Qué sigue…?» sale sola al terminar; si se cierra con la X equivale a «Decidirlo después» (`OrderModal.tsx:624`).
4. **Avisos (toasts).** Duran 3.5 s los de éxito y 6 s los de error, uno a la vez (`web-app/src/kernel/ui/toastStore.ts:41`). El de «Despacho EMP-… creado» aparece al mismo tiempo que la pregunta «¿Qué sigue…?»: conviene una pausa de 2 a 3 s antes del siguiente clic para que se lea.
5. **La lista de documentos sale como tarjetas o como tabla según el ancho.** Bajo 640 px de panel son tarjetas (`PickDocumentsPanel.tsx:17,132`). El reparto de los dos paneles se recuerda en `localStorage` (`teikem.split.pick-batches`, proporción del panel izquierdo; 0.6 por omisión, de 0.35 a 0.75: `SplitPane.tsx:25-33,43-58`). A 1920 px de ancho con 0.6 el panel derecho queda justo en el límite. Recomendación: fijar la proporción antes de grabar (por ejemplo 0.45) para que salga tabla, y usar el mismo localizador `tr, .dt-cards > li` por si acaso.
6. **Datos viejos hasta 30 segundos.** Las consultas se consideran frescas 30 s (`web-app/src/app/queryClient.ts:28`) y despachar desde Ventas solo refresca las consultas de ventas (`salesApi.ts:124-126,162-165`). Si se vuelve a Recolección y empaque por el menú antes de 30 s puede verse todavía «Solicitada». Con `page.goto(...)` (carga completa) no pasa.
7. **Números en kits y en piezas a la vez.** La fila del documento cuenta en unidades de la orden (un kit = 1: «Sacado 5 de 5»); la ficha lista las piezas («Por sacar 30» y «5»); la recolección suma piezas («Cantidad 35»: `PickBatchDocumentService.cs:239-240`); y en Ventas el despacho dice «Unidades 5» (kits: `SalesOrderService.Dispatch.cs:217`). La narración debe explicarlo una vez: 5 kits son 30 cajas de tiras y 5 monitores.
8. **Después de despachar, la ficha de una orden que solo lleva kits dice «Este documento no tiene renglones.»** (`…docs.noLines`): las piezas se listan desde la reserva y ya no queda (`PickBatchDocumentService.cs:182-187`, `PickDocumentModal.tsx:80-81`). Para que la ficha final se vea bien, la orden debe llevar además un producto normal, o se muestra solo la fila de la lista.
9. **«Propio» donde uno espera el cliente.** La recolección de una orden de venta sin entrega muestra «Propio» como subtítulo y en la columna «Cliente» de «Por movimiento» (`PickBatchDetailModal.tsx:64`, `PickBatchesPanel.tsx:148`). Evitar ese encuadre o no detenerse ahí.
10. **Botones peligrosos a la vista.** En la ficha de la recolección: «Eliminar» deshace el despacho (la existencia vuelve y la orden se reabre: `PickBatchService.cs:757-760`, `SalesOrderService.Dispatch.cs:246-292`) y «Empacar» crea un envío que **no** queda enlazado a la orden de venta (ese enlace solo lo pone «Crear entrega» desde la orden: `SalesDeliveryService.cs:71-77` frente a `PickBatchService.cs:634-678`). En el panel izquierdo: el interruptor «Despacho manual (sin entrega)». No tocarlos.
11. **Cosas que se ven feas hoy.** El kit se llama «kIT DE mONITOR gRATIS»; el almacén «Almacen Solutions» (sin acento); todo sale de una sola posición, `PISO`; la lista de documentos trae también la orden vieja `OV-00002`; el subtítulo de la pantalla habla de «FEFO… lote o serie» y Solutions no usa lotes.
12. **Datos sensibles en pantalla.** Nombres reales de clientes (lista de documentos, orden, despachos); **costos** en la ficha de la recolección («Costo unitario», «Costo total»: `PickBatchDetailBody.tsx:90,174-177`); el nombre de la persona en «Por» y en «Usuario» del Kárdex; las iniciales y la compañía en el encabezado.
13. **Listas desplegables nativas.** «Tipo de servicio», «Consignatario» y «Tipo de paquete» son `<select>` del navegador (`web-app/src/kernel/ui/Form.tsx:279-291`): en una grabación de Playwright la lista abierta puede no verse; el valor cambia de golpe. Solo afecta a la variante C.
14. **Continuidad.** El encabezado lleva un reloj «en vivo» con la hora real: delata los cortes entre tomas. El idioma y el tema se recuerdan por navegador: fijar español y el mismo tema en todas las escenas.
15. **Validaciones que pueden salir.** «No se puede despachar más de lo reservado: {sku} tiene {n} reservadas.» (`sales.dispatch.errors.more`); «Escriba la cantidad a despachar de al menos una línea.» (`sales.dispatch.errors.none`); «El kit {SKU} se pide en piezas enteras.»; «Solo se puede despachar una orden solicitada o despachada parcial.» (`SalesRules.cs:60`); «La orden espera la aprobación del cliente nuevo: no se puede despachar hasta que lo aprueben.» (`SalesOrderExtras.cs:28`).
16. **Correo.** Despachar la orden completa publica el aviso «orden despachada» (`SalesOrderService.Dispatch.cs:324-325`). Solutions no tiene reglas de aviso, así que no sale nada; aun así el API de la grabación va sin la llave de Brevo (regla del proyecto).

---

## 6. Datos para la narración (hechos verificados, en frases cortas y en lenguaje de usuario)

- Cuando Ventas solicita la orden, el sistema aparta la mercancía para ella. Desde ese momento el almacén la ve en Recolección y empaque. (`docs/manual/18-ordenes-de-venta.md:79`; `PickBatchDocumentService.cs:84-87`)
- La lista muestra un renglón por documento, con su avance: cuánto se pidió, cuánto se ha sacado y cuánto falta por sacar. (`docs/manual/06-inventario-y-almacen.md:2247`)
- Al abrir el documento se ve qué hay que recolectar y de qué posición. Esa posición es la que el sistema ya reservó, no una sugerencia al azar. (`docs/manual/06-inventario-y-almacen.md:2251`; `docs/lote238-decisiones.md:28`)
- Un kit de oferta se saca por sus piezas. Cinco kits son treinta cajas de tiras y cinco monitores. (`docs/manual/18-ordenes-de-venta.md:274-277`; datos de `KIT-001`)
- El sistema reserva primero lo que entró primero. (`RotationMethodNoLot = FIFO`; `RotationRules.cs:92-98`)
- Despachar siempre baja el inventario, lo entreguen o lo vengan a recoger. (`docs/manual/18-ordenes-de-venta.md:135`)
- Cada despacho crea una recolección con su número, EMP y cinco dígitos, enlazada a la orden. (`docs/manual/18-ordenes-de-venta.md:139`; `NumberingRules.cs:41`)
- Se puede despachar por partes; nunca más de lo reservado. (`docs/manual/18-ordenes-de-venta.md:143`)
- Cuando sale todo, la orden queda «Despachada». Si queda algo pendiente, queda «Despachada parcial». (`SalesOrderService.Dispatch.cs:310-322`; `docs/manual/frontend/l63-despachar-orden-de-venta.md:31`)
- Si el cliente recoge, no hay que hacer nada más: ahí termina. (`docs/manual/18-ordenes-de-venta.md:166`)
- Cada salida queda escrita en el Kárdex con el número de la recolección y de la orden; en un kit, también dice de qué kit era la pieza. (`SalesOrderService.Dispatch.cs:177-179`)
- El balance del producto baja en ese mismo momento: menos total y nada reservado para esa orden. (`web-app/e2e/lote63-despacho-orden-de-venta.spec.ts:143-149`)
- Si alguien se equivoca, eliminar la recolección devuelve la mercancía a su posición y la orden vuelve a quedar por despachar. (`docs/manual/18-ordenes-de-venta.md:168`)
- En el almacén esto mismo se hace con el aparato: guía posición por posición y se confirma con el escáner. (`docs/manual/18-ordenes-de-venta.md:193`) — solo si se quiere mencionar la app.

Frases que **no** se pueden decir con lo que hay hoy: «el almacén pulsa Recolectar y la orden queda despachada» (no es así en la web), «se empaca y sale el envío» (la entrega no funciona en Solutions), «por lote y vencimiento» (Solutions no usa lotes).

---

## 7. Duración estimada en pantalla (segundos) y qué se puede recortar si hay que acortar

Ruta A completa: **unos 85 segundos**.

| Tramo | Segundos |
|---|---|
| Entrar a Recolección y empaque y ver la orden en la lista | 8 |
| Ficha «Lo que hay que recolectar» (kit por piezas, posición) | 14 |
| Ir a Ventas y abrir la orden | 8 |
| «Despachar» y confirmar | 10 |
| «¿Qué sigue…?» → Retiro | 6 |
| Orden «Despachada» y tabla de despachos | 8 |
| Volver a Recolección y empaque: documento al 100 % y su recolección | 12 |
| Kárdex: los renglones «Despacho» | 10 |
| Productos e inventario: balance rebajado | 9 |

Para acortar, en este orden: quitar el regreso a Recolección y empaque (−12), quitar Productos e inventario (−9), dejar el Kárdex en un solo plano corto (−4). Versión mínima: ficha (12) + despachar y retiro (16) + orden despachada (8) + Kárdex (8) = **unos 45 segundos**. Si se arregla el botón «Recolectar» (decisión 1) se ahorra el paso por Ventas (unos 15 s) y la escena queda dentro de una sola pantalla.

---

## 8. Dudas y decisiones para Luis

**Decisiones**

1. **¿Se arregla antes de grabar que la salida de la orden se haga desde Recolección y empaque?** Hoy «Recolectar» solo llena un formulario que, al enviarse, crea una recolección suelta. Opciones: (a) lote corto para que la ficha del documento despache contra la orden (lo más simple: un botón «Despachar» en la ficha que use el mismo despacho de Ventas; el servidor ya lo tiene) y corregir manual y FAQ; (b) no tocar código y grabar el despacho desde Ventas → la orden → «Despachar». Recomendación: (a), porque es lo que dictaste («de ahí gestione la salida») y porque el botón actual es una trampa para el usuario real.
2. **¿Retiro o entrega?** Recomendación: en el video, «Recogido» en el asistente y «Retiro» al despachar. La entrega hoy falla en Solutions (sin contratos, sin tarifas, Envíos apagado). Si en la vida real entrega Advance Depot, eso es el enlace entre compañías (Lote 227), que no está configurado (0 enlaces) y sería otra escena.
3. **¿Con qué cuenta se graba el almacén?** Recomendación: crear en la base local un usuario de almacén (rol `WarehouseOperator`) para que se note el cambio de persona entre Ventas y Almacén; si se prefiere lo simple, todo con el administrador. Hoy no hay ninguna cuenta de pruebas en Solutions.
4. **¿Se limpia lo viejo antes de grabar?** Recomendación: cancelar `OV-00002` (libera sus 35 unidades reservadas) y usar un kit con nombre presentable (el `OFERTA1` del script 02 o renombrar `KIT-001`).
5. **¿Todo desde `PISO` o posiciones de estante?** Hoy todo está en `PISO`. Recomendación: aceptarlo si así trabaja Solutions; si quieres que se vea un estante, hay que vender un producto que solo tenga existencia ahí (por FIFO, lo viejo de `PISO` sale primero).
6. **¿Pueden verse los costos?** La ficha de la recolección muestra costo unitario y total. Recomendación: no abrir esa ficha (saltar el paso 10) si el video lo verá personal que no deba ver costos.
7. **¿Kárdex y balance en esta escena?** Recomendación: sí, cortos (Kárdex 8 a 10 s; balance 5 a 9 s): es la prueba de que el inventario bajó y enlaza con la escena 1.
8. **¿Se menciona la app del aparato?** Recomendación: una sola frase de narración, sin grabarla (el pedido es la web).

**Dudas (no confirmadas)**

- No se ejecutó nada: el efecto de «Recolectar (bajar de inventario)» tras «Recolectar» sale de leer el código. Vale la pena comprobarlo una vez en el entorno de grabación (5 minutos) antes de decidir.
- Cómo llega la orden de la escena 4: qué productos, si es «Entrega» o «Recogido» y si queda sin backorder. De eso depende que termine en «Despachada».
- Si a la resolución de grabación la lista de documentos sale como tabla o como tarjetas (riesgo 5).
- El manual llama «Maestras» a lo que la pantalla llama «Cargas» (`docs/manual/06-inventario-y-almacen.md:3042` frente a `warehouse.pickBatches.groups.view`); no afecta al video si no se toca esa pestaña.

**Avisos para otras escenas (vistos de paso)**

- **Escena 3 (recibo):** el almacén de Solutions está en modo de recepción «Con acomodo» (`PUTAWAY`) y **sin posición de recepción** (`DefaultReceivingBinId` NULL; la zona `STG` no tiene posiciones). El texto de Ajustes dice que un almacén con acomodo necesita una posición de recepción para poder recibir (`system.settings.ops.recvSumHint`). Consulta: `SELECT w.Code, rm.InternalCode, w.DefaultReceivingBinId FROM dbo.Warehouse w LEFT JOIN dbo.LookupCode rm ON rm.LookupCodeId = w.ReceivingModeLookupId WHERE w.TenantId = 2;` → `ALM-SOLUTIONS | PUTAWAY | NULL`.
- **Escena 6 (QuickBooks):** la pestaña QuickBooks pasa **facturas de venta emitidas** (`docs/manual/49-quickbooks.md:21`) y el módulo de Facturación (`BILLING`) está apagado en Solutions. El recorrido que ya existe hace despacho → corrida de facturación → QuickBooks (`web-app/e2e/lote228-pedido-vendedores.spec.ts:448-489`). Entre esta escena y la 6 falta ese paso.
- **Todas:** el script `scripts/demo/solutions-nueva-posicion.sql` busca el almacén `ALM-SOL` y la zona `GEN`, que no existen en esta base (aquí es `ALM-SOLUTIONS` con zonas `PCK`, `PISO`, `RSV`, `STG`): no sirve tal cual.

---

## 9. Fuentes (archivo:línea)

**Web**
- `web-app/src/app/routes.tsx:215-222, 271-283, 376-382, 394`
- `web-app/src/app/AppShell.tsx:263, 280-283` (grupos y enlaces del menú)
- `web-app/src/app/queryClient.ts:28`
- `web-app/src/features/warehouse/PickBatchListScreen.tsx:30-59, 63-104, 108-146`
- `web-app/src/features/warehouse/PickDocumentsPanel.tsx:14-17, 45-86, 100-110, 117-139`
- `web-app/src/features/warehouse/PickDocumentModal.tsx:20, 27-45, 47-75, 77-117, 119-148`
- `web-app/src/features/warehouse/pickDocumentView.ts:17-24, 41-51`
- `web-app/src/features/warehouse/CollectPanel.tsx:320-326, 351-364, 368-373, 552-561, 616-634`
- `web-app/src/features/warehouse/collectForm.ts:91-114`
- `web-app/src/features/warehouse/api.ts:1395-1403, 1435-1441, 1475-1481`
- `web-app/src/features/warehouse/PackModal.tsx:43, 91-96, 104-117, 193-207, 219-228, 293-302`
- `web-app/src/features/warehouse/PickBatchDetailBody.tsx:43-49, 83-99, 111-178`
- `web-app/src/features/warehouse/PickBatchDetailModal.tsx:29-37, 64, 69`
- `web-app/src/features/warehouse/PickBatchDetailScreen.tsx:19-71`
- `web-app/src/features/warehouse/PickBatchesPanel.tsx:144-151, 162-184`
- `web-app/src/features/warehouse/PickGroupCollectModal.tsx:22, 53, 86`
- `web-app/src/features/warehouse/kardexColumns.tsx:35-49, 75-96, 133-159`
- `web-app/src/features/warehouse/InventoryScreen.tsx:1-19`
- `web-app/src/features/warehouse/KardexTransactionModal.tsx:24-53`
- `web-app/src/features/sales/OrderModal.tsx:545-614, 616-635, 646-648, 688-710, 719-738, 795, 800-842`
- `web-app/src/features/sales/KitLineBlock.tsx:186-229`
- `web-app/src/features/sales/SalesOrdersScreen.tsx:88-112, 185, 323, 336-362`
- `web-app/src/features/sales/salesApi.ts:124-126, 162-170`
- `web-app/src/kernel/ui/Modal.tsx:63-72`, `Tabs.tsx:18-24`, `Form.tsx:279-291`, `SplitPane.tsx:25-58`, `DataTable.tsx:475-483, 507, 551-554`, `toastStore.ts:41`
- `web-app/src/kernel/i18n/es.json`: claves `nav.*`, `warehouse.pickBatches.*`, `warehouse.kardexView.*`, `warehouse.kardexDetail.*`, `warehouse.products.columns.*`, `sales.*`, `system.settings.ops.*` citadas en el texto

**Servidor**
- `src/Teikem.Api/Controllers/PickBatchesController.cs:17-22, 32-47, 67-68, 92-94, 110-125`
- `src/Teikem.Api/Controllers/SalesOrdersController.cs:17-21, 28, 45-49, 96-102`
- `src/Teikem.Api/Controllers/OrdersController.cs:16-18`
- `src/Teikem.Api/Controllers/InventoryController.cs:70, 90, 111, 131`
- `src/Teikem.Infrastructure/Services/SalesOrderService.Dispatch.cs:15-23, 54-57, 70-80, 89-107, 163-199, 212-238, 246-292, 310-328`
- `src/Teikem.Infrastructure/Services/SalesOrderService.Request.cs:362-384, 396-409`
- `src/Teikem.Infrastructure/Services/SalesDeliveryService.cs:12-19, 38-79`
- `src/Teikem.Infrastructure/Services/PickBatchService.cs:15-49, 187-194, 238-240, 327-442, 444-483, 499-516, 581-600, 610-678, 688-775, 862-887`
- `src/Teikem.Infrastructure/Services/PickBatchDocumentService.cs:15-20, 62-63, 84-97, 122-130, 151-225, 228-257`
- `src/Teikem.Infrastructure/Services/PickGroupService.cs:40-90, 111-118`
- `src/Teikem.Infrastructure/Services/OrderService.cs:387-393`
- `src/Teikem.Infrastructure/Services/OrderStatusService.cs:79-101`
- `src/Teikem.Infrastructure/Services/OrderQuoteService.cs:31-32, 35-37, 58-82`
- `src/Teikem.Infrastructure/Services/RateService.cs:270-281, 316-324`
- `src/Teikem.Infrastructure/Services/InventoryReadService.cs:170`
- `src/Teikem.Infrastructure/Wms/InventoryLedger.cs:153-167`
- `src/Teikem.Domain/Wms/RotationRules.cs:10-22, 31-62, 64-75, 92-98`
- `src/Teikem.Domain/Wms/PickBatchRules.cs:247-262, 267-270`
- `src/Teikem.Domain/Wms/PickGroupRules.cs:9-10, 16`
- `src/Teikem.Domain/Wms/KardexRules.cs:96-111, 134`
- `src/Teikem.Domain/Sales/SalesRules.cs:13, 57, 60, 62, 65, 69-71`; `src/Teikem.Domain/Sales/SalesOrderExtras.cs:28`
- `src/Teikem.Domain/Orders/NumberingRules.cs:41`; `src/Teikem.Domain/Orders/ServiceTypeMarks.cs:9-15`
- `src/Teikem.Domain/Constants/PermissionCatalog.cs:120, 667, 1198`

**Manual, decisiones y recorridos**
- `docs/lote238-decisiones.md:12, 19, 28-29`
- `docs/manual/06-inventario-y-almacen.md:2127-2139, 2201, 2216-2236, 2240-2263, 2780-2798, 3005-3050`
- `docs/manual/18-ordenes-de-venta.md:26, 41-42, 77-86, 133-161, 163-175, 188-197, 250-299, 363-366`
- `docs/manual/09-app-almacen.md:1308-1328`
- `docs/manual/40-avisos-de-salida.md:120`
- `docs/manual/49-quickbooks.md:21`
- `docs/manual/faq.md:12214-12217`
- `docs/manual/frontend/l63-despachar-orden-de-venta.md:5-33`; capturas `docs/manual/frontend/img/l238-ficha-que-recolectar.png`, `l238-formulario-precargado.png`, `l63-despachar.png`, `l63-que-sigue.png`
- `web-app/e2e/lote238-modo-captura-y-recoleccion.spec.ts:117-140`
- `web-app/e2e/lote63-despacho-orden-de-venta.spec.ts:95-149`
- `web-app/e2e/lote228-pedido-vendedores.spec.ts:448-489`
- `web-app/e2e/staffHelpers.ts:19-33, 52-63`
- `scripts/solutions-pedido-vendedores/LEEME.md` (tabla de scripts 00 y 02; apartado «Usuarios»)

**Base local (solo lectura, TenantId 2)**: tablas `Tenant`, `TenantModule`, `Warehouse`, `WarehouseZone`, `WarehouseBin`, `StockBalance`, `Product`, `ProductKitComponent`, `Offer`, `SalesOrder`, `SalesOrderLine`, `SalesOrderLineComponent`, `SalesOrderReservation`, `PickBatch`, `PickGroup`, `ExitNotice`, `TransportOrder`, `InventoryTransaction`, `NumberSequence`, `Role`, `RolePermission`, `UserRole`, `UserTenant`, `Contract`, `RateComponent`, `Location`, `NotificationRule`, `TenantLink`; catálogos globales `StatusCode` y `LookupCode`.
