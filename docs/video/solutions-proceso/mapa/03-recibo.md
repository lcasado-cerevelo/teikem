# Escena 3 - Recibo de las órdenes de compra: mapa para el video

Investigación del 2026-11-12, en solo lectura: código y manual de la rama `master`, y la base local `Teikem` (compañía Advance Solutions, `TenantId = 2`). No se levantó ningún servidor ni se cambió nada.

**En corto.** Recibir una orden de compra se hace completo en la web, en una sola pantalla (Almacén → Recibo), en 8 pasos por orden y sin teclear cantidades si llegó completo. **Hoy no funcionaría con Advance Solutions** por dos razones: (1) su almacén está en modo «Con acomodo» y su zona de recepción no tiene ninguna posición, así que crear el recibo responde un error; y (2) la compañía no tiene proveedores ni órdenes de compra (los trae la escena 2). Las dos se arreglan en pantalla, sin programar nada (sección 4).

---

## 1. Qué es y dónde está (menú, ruta, permiso, módulo)

**Qué es.** «Recibo» es la pantalla donde entra la mercancía al almacén. Una orden de compra solo dice que se compró; el inventario sube cuando se **confirma el recibo** hecho contra esa orden (`es.json:12609`, texto de ayuda del importador; `ReceiptService.cs:28-31`).

| Dato | Valor | Fuente |
|---|---|---|
| Menú | grupo **Almacén** (`nav.groups.warehouse`) → **Recibo** (`nav.receipts.title`) | `es.json:237`, `es.json:284-287`; `routes.tsx:265` |
| Ruta | `/warehouse/receipts`. El recibo elegido va en `?receipt=<publicId>`; pestañas `?tab=putaway` y `?tab=groups` | `routes.tsx:260-266`; `ReceiptListScreen.tsx:33`, `:141-146` |
| Permiso para ver la pantalla | `receipts.view` (ruta de la web); el API lee con `inventory.view` | `routes.tsx:263`; `ReceiptsController.cs:32` |
| Permiso para crear, capturar y confirmar | `warehouse.receive` | `ReceiptListScreen.tsx:184-191`; `ReceiptsController.cs:104`, `:149` |
| Extra para recibir contra orden de compra | `purchasing.receive` **y** el módulo `PURCHASING`. Sin los dos, la opción «Contra orden de compra» no se pinta | `ReceiptHeaderModal.tsx:104-107`; `ReceiptService.cs:291-296` |
| Módulo de la pantalla | `WMS_LOTSERIAL` | `routes.tsx:264`; `ReceiptsController.cs:21` |

**Cómo es la pantalla** (`ReceiptListScreen.tsx:176-225`): título «Recibo», botones **Nuevo recibo** y **Recibir juntos**, y tres pestañas: **Recibos**, **Acomodo pendiente** y **Cargas**. En «Recibos»: filtros arriba, la lista de recibos a la izquierda (340 px) y, a la derecha, el **Detalle del recibo** elegido con sus líneas y el botón ancho **Confirmar recibo** (`ReceiptDetailPanel.tsx:104-233`).

**Pantallas vecinas que tocan esta escena**

| Pantalla | Menú y ruta | Para qué sirve aquí |
|---|---|---|
| Órdenes de compra | Contabilidad → **Compras**, `/warehouse/purchase-orders` (`routes.tsx:239-244`; `es.json:238`, `:300-303`) | De ahí vienen las órdenes (escena 2) y ahí se ve que quedaron «Recibida» |
| Llegadas y salidas | Almacén → **Llegadas y salidas**, `/warehouse/notices` (`routes.tsx:252-258`) | No hace falta para una orden de compra (ver abajo) |
| Productos e inventario | Almacén → **Productos e inventario**, `/warehouse/products` (`routes.tsx:217-222`) | Cierre de la escena: el inventario subió |
| Kárdex de movimientos | Almacén → **Kárdex de movimientos**, `/warehouse/kardex` (`routes.tsx:376-382`) | Opcional: el movimiento «Recepción» de cada línea |
| Almacenes | Almacén → **Almacenes**, `/warehouse/warehouses/:publicId` (`routes.tsx:193-205`) | Solo para la preparación: el modo de recepción |

**Aclaraciones pedidas**

- **¿Se puede hacer completo en la web?** Sí: crear el recibo contra la orden, corregir lo recibido, elegir posición, confirmar y (si hubiera) completar las tareas de acomodo. Ningún paso obliga a usar la app del aparato. Lo único que la web no hace en un recibo con orden de compra es **agregar una línea que no venía en la orden** (solo app o API) (`ReceiptLinesEditor.tsx:2-3`; `f6-almacen-e-inventario.md:874-876`).
- **Relación con «Llegadas y salidas».** Una orden de compra enviada **no aparece** como aviso de llegada. El aviso (`LLE-#####`) lo crea el sistema solo, en el momento en que se abre el recibo contra la orden, por lo pendiente de cada línea (`ReceiptService.cs:341-355`; `AsnService.cs:345-371`). Después sí se ve en Llegadas y salidas → Avisos de llegada, con «OC PO-…» en la columna «Cliente / OC» y el enlace a su recibo (`AsnsTab.tsx:105-110`, `:138-150`). «Nuevo aviso» pide un **Cliente dueño** obligatorio: es para mercancía de clientes, no para compras propias (`AsnCreateModal.tsx:63`, `:148`).
- **Recepción maestra (capítulo 44).** «Recibir juntos» abre un recibo por documento bajo una **Carga** `RCM-#####` y permite «Confirmar todo». Ofrece también las órdenes de compra enviadas (`ReceiptGroupCreateModal.tsx:17-24`; `44-recepcion-maestra.md:58`). No es el camino más corto para este video (sección 3, variante C). Ojo: el manual la llama «Maestros» / «Maestra RCM-…», pero la pantalla hoy dice **«Cargas»** y **«Carga RCM-…»** (`es.json:6427`, `:6751`).
- **Modo del almacén.** Dos modos (`06-inventario-y-almacen.md:224-227`): «Con acomodo» (entra a la posición de recepción y crea tareas de acomodo) y «Directo a posición» (cada línea lleva su posición y entra ahí al confirmar, sin tareas). Advance Solutions está hoy en «Con acomodo» **sin** posición de recepción: es el bloqueo principal (sección 4, requisito R4).
- **Lote, vencimiento y series.** No aplica: los 42 productos activos de Solutions no llevan lote ni serie (sección 4, R6). El ícono «Lote y series» ni aparece (`ReceiptLinesEditor.tsx:391-398`).

---

## 2. Qué conviene mostrar, en orden (lo que pidió el dueño y lo que ayuda a entenderlo)

Lo que pidió Luis: «ir al recibo y demostrar cómo es que se reciben esas órdenes de compra».

1. **Entrar a Recibo** desde el menú Almacén. La lista está vacía: Solutions nunca ha recibido en Teikem (0 recibos hoy).
2. **Nuevo recibo → Origen «Contra orden de compra»**, elegir el almacén y la orden recién importada. Idea a transmitir: el recibo se hace *contra* la orden; no se vuelve a teclear nada.
3. **El recibo nace con las líneas de la orden y «Recibido» igual a «Esperado».** Si llegó completo no se toca nada. Es el momento de decir (solo con la voz) que si llegó menos se corrige la cantidad en «Recibido».
4. **Dónde se guarda:** un clic en **Usar posiciones sugeridas**; el sistema propone la posición donde ya está ese producto (PISO).
5. **Confirmar recibo.** El estatus pasa a «Acomodado» y aparece la columna «Costo unitario» con el costo de la orden.
6. **La segunda orden**, igual pero rápido (acelerado o con cortes).
7. **Compras:** las dos órdenes quedaron «Recibida» sin que nadie les cambiara el estatus.
8. **Productos e inventario:** la existencia subió (cierra el círculo con la escena 1).

Lo que **no** conviene mostrar aquí (existe, pero alarga y distrae): «Confirmar tanda» (recibir por partes), «Reportar daño», «Recibir juntos» / Cargas, la pestaña «Acomodo pendiente», el Historial del recibo y la captura de lotes o series.

---

## 3. Paso a paso exacto del recorrido

**Camino recomendado (variante A): un recibo por orden, con el almacén en «Directo a posición».** Condiciones de partida, todas fuera de cámara (sección 4): almacén `ALM-SOLUTIONS` en «Directo a posición»; dos órdenes de compra en estatus «Enviada»; sesión iniciada en Advance Solutions, interfaz en español, ventana de 1920 × 1080.

Los localizadores de los pasos 2 a 10 salen de los recorridos que ya existen (`lote16.spec.ts:275-335`, `f6.spec.ts:395-425`); los del menú (paso 1) y el del campo «Orden de compra» (paso 5) se dedujeron del marcado y hay que comprobarlos en el primer ensayo.

| # | Acción del usuario | Etiqueta en pantalla (es) y clave i18n | Cómo localizarlo en Playwright | Qué debe verse después |
|---|---|---|---|---|
| 1 | Abrir el grupo Almacén del menú y entrar a Recibo | «Almacén» `nav.groups.warehouse` · «Recibo» `nav.receipts.title` | `page.locator('#app-rail').getByRole('button', { name: 'Almacén', exact: true })` (solo si el grupo está cerrado) y `page.locator('#app-rail').getByRole('link', { name: 'Recibo', exact: true })`. Alternativa segura: `page.goto('/warehouse/receipts')` | Título «Recibo» (`warehouse.receipts.title`) con el subtítulo «Recibos ciegos, de devolución, contra aviso de llegada o contra orden de compra.»; pestañas Recibos · Acomodo pendiente · Cargas; lista vacía («No hay recibos con estos filtros.») y a la derecha «Selecciona un recibo de la lista» |
| 2 | Pulsar Nuevo recibo | «Nuevo recibo» `warehouse.receipts.new` | `page.getByRole('button', { name: 'Nuevo recibo' })` | Ventana «Nuevo recibo» (`warehouse.receipts.header.newTitle`): Origen (sale en «Ciego»), Almacén, Modo de recepción, Plataforma, Posición de recepción, Transporte, Referencia; botones Cancelar y Crear recibo |
| 3 | En Origen elegir Contra orden de compra | «Origen» `warehouse.receipts.header.source` · «Contra orden de compra» `warehouse.receipts.sources.PO` | `const dlg = page.getByRole('dialog', { name: 'Nuevo recibo' })`; `dlg.getByLabel(/^Origen/).selectOption('PO')` | Aparece el campo «Orden de compra» apagado, con la ayuda «Elija primero el almacén.» (`warehouse.receipts.pickWarehouseFirst`) |
| 4 | Elegir el almacén | «Almacén» `warehouse.receipts.fields.warehouse` | `const wh = dlg.getByRole('combobox', { name: /^Almacén/ })`; `wh.click()`; `wh.fill('ALM-SOLUTIONS')`; `dlg.getByRole('option', { name: /^ALM-SOLUTIONS/ }).click()` | El campo dice «ALM-SOLUTIONS · Almacen Solutions»; «Modo de recepción» (`warehouse.receipts.header.mode`) muestra «Directo a posición» y **desaparece** «Posición de recepción»; «Orden de compra» se enciende |
| 5 | Elegir la orden de compra | «Orden de compra» `warehouse.receipts.fields.purchaseOrder` (vacío: «Seleccione…» `warehouse.receipts.choose`) | `const po = dlg.getByRole('combobox', { name: /^Orden de compra/ })`; `po.click()`; `dlg.getByRole('option', { name: /PO-\d+ · ADVANCE LOGISTICS/ }).click()` | El campo muestra «PO-##### · proveedor». Solo salen las órdenes «Enviada» o «Recibida parcial» de ese almacén (`ReceiptHeaderModal.tsx:200-203`, `:221-224`) |
| 6 | (Opcional) escribir la referencia del proveedor | «Referencia» `warehouse.receipts.header.reference` | `dlg.getByLabel('Referencia').fill('PDC-04292026')` | El texto en el campo (máximo 80) |
| 7 | Pulsar Crear recibo | «Crear recibo» `warehouse.receipts.create` | `dlg.getByRole('button', { name: 'Crear recibo' })` | Aviso «Recibo REC-00001 creado.» (`warehouse.receipts.created`); la dirección cambia a `?receipt=…`; en la lista, REC-00001 con el chip **Descargando**, el proveedor, la etiqueta «Orden de compra» + PO-##### y «Directo». A la derecha: «Detalle del recibo · REC-00001», chips «Descargando» y «Directo a posición», «Origen · Orden de compra: PO-#####»; tabla «Líneas del recibo» con SKU · Producto · Esperado · Recibido · Diferencia · Posición destino; cada línea con Recibido = Esperado y Diferencia 0; bajo cada posición vacía, «Sugerida: PISO · Consolidar con el mismo producto»; «Confirmar recibo» apagado con «Falta la posición destino en 2 línea(s).» |
| 8 | Pulsar Usar posiciones sugeridas | «Usar posiciones sugeridas» `warehouse.receipts.detail.useSuggested` | `const det = page.locator('.rcp-side')`; `det.getByRole('button', { name: 'Usar posiciones sugeridas' })` | Aviso «Se asignó posición a 2 línea(s); 0 sin sugerencia.» (`warehouse.receipts.detail.suggestedApplied`); cada línea con «PISO · PCK» en «Posición destino»; «Confirmar recibo» se enciende |
| 9 | Pulsar Confirmar recibo | «Confirmar recibo» `warehouse.receipts.detail.confirm` | `det.getByRole('button', { name: 'Confirmar recibo' })` | Ventana «Confirmar recibo» con el texto «Se confirma el recibo REC-00001 completo: cada línea entra a su posición destino, sin tareas de acomodo. Ya no se podrá modificar.» (`warehouse.receipts.detail.confirmBodyDirect`) |
| 10 | Confirmar en la ventana | «Confirmar recibo» `warehouse.receipts.detail.confirm` · «Cancelar» `common.cancel` | `page.getByRole('dialog').getByRole('button', { name: 'Confirmar recibo' })` | Aviso «Recibo REC-00001 confirmado.» (`warehouse.receipts.detail.confirmed`); chip **Acomodado**; aparece el dato «Confirmado» con fecha y hora; la tabla queda de solo lectura y gana la columna **Costo unitario** (`warehouse.receipts.lines.cost`; al pasar el cursor: «Costo de la orden de compra»); «Confirmar recibo» apagado con «El recibo ya está confirmado.»; no hay panel «Tareas de acomodo» |
| 11-19 | Repetir 2 a 10 con la segunda orden | las mismas | las mismas; en el paso 5, `/PO-\d+ · PADIMONT/` | «Recibo REC-00002 creado.»; con 4 líneas: «Falta la posición destino en 4 línea(s).» y luego «Se asignó posición a 4 línea(s); 0 sin sugerencia.»; «Recibo REC-00002 confirmado.» y chip Acomodado. La primera orden ya no sale en la lista del paso 5 (quedó Recibida) |
| 20 | Ir a Compras | «Contabilidad» `nav.groups.money` · «Compras» `nav.purchaseOrders.title` | `page.goto('/warehouse/purchase-orders')`; comprobar `page.getByRole('row').filter({ hasText: 'PADIMONT' }).locator('.chip', { hasText: 'Recibida' })` | Título «Órdenes de compra» (`warehouse.purchaseOrders.title`); las dos órdenes con el chip **Recibida** en la columna Estatus |
| 21 | Ir a Productos e inventario | «Almacén» · «Productos e inventario» `nav.products.title` | `page.goto('/warehouse/products')`; `page.getByRole('group', { name: 'Resumen de la lista' })` | El indicador «Unidades totales» (`warehouse.products.kpis.totalUnits`) sube de 69,900 a 84,900; en la tabla, las columnas Disponible · Reservado · Total (`warehouse.products.columns.*`) de los productos recibidos con la cantidad nueva |
| 22 | (Opcional) filtrar un producto | «SKU o nombre» `warehouse.products.filters.sku` | desplegable con búsqueda «SKU o nombre»; teclear `52800` y marcar el producto | Una sola fila: 52800 · Disponible 29,100 · Reservado 30 · Total 29,130 |

Las cifras de los pasos 21 y 22 valen solo si se reciben completas las dos órdenes propuestas en la sección 8 (decisión 4).

**Variante B: «Con acomodo»** (si Luis prefiere enseñar las tareas del almacén). Exige crear antes una posición en la zona STG. Cambia así: en el paso 4 aparece «Posición de recepción»; el paso 8 no existe; sobre «Confirmar recibo» sale además el botón «Confirmar tanda» (`warehouse.receipts.batches.confirm`); al confirmar, el recibo queda **Completado** y debajo aparece «Tareas de acomodo» con una tarea «Pendiente» por línea. Para cerrar: **Completar todas** (`warehouse.receipts.bulk.complete`) → ventana «Completar todas las tareas del recibo» → el recibo pasa a «Acomodado» (`ReceiptPutawayTasks.tsx:92-135`). Son 2 clics más por orden y un concepto más que explicar. El inventario ya cuenta desde la confirmación, aunque el acomodo esté pendiente (`ReceiptService.cs:889-909`).

**Variante C: «Recibir juntos»** (`lote182-recepcion-maestra.spec.ts:31-59`). Recibir juntos → marcar las dos órdenes → «Recibir juntos (2)» → se abre la Carga `RCM-00001` en la pestaña Cargas. En directo hay que entrar a cada recibo («Abrir recibo») para poner posiciones antes de «Confirmar todo», así que no ahorra pasos; y la ventana no deja elegir el modo (`receiptGroupView.ts:70-78`), por lo que hoy fallaría con el mismo error del requisito R4.

**Si la revisión se hace con la interfaz en inglés** (la de Luis), las mismas etiquetas en `en.json`: Recibo = *Receiving* · Nuevo recibo = *New receipt* · Origen = *Origin* · Contra orden de compra = *Against purchase order* · Orden de compra = *Purchase order* · Modo de recepción = *Receiving mode* · Directo a posición = *Direct to bin* · Con acomodo = *With put-away* · Crear recibo = *Create receipt* · Usar posiciones sugeridas = *Use suggested bins* · Posición destino = *Target bin* · Confirmar recibo = *Confirm receipt* · Costo unitario = *Unit cost* · Cargas = *Loads* · Compras = *Purchases* · Productos e inventario = *Products & inventory*.

---

## 4. Requisitos de datos y de configuración para que funcione con Advance Solutions

Todas las consultas se corrieron con `sqlcmd -S localhost,1433 -d Teikem -E -C` (solo `SELECT`, `TenantId = 2`).

| # | Requisito | Estado HOY en la base local | Cómo se arregla si falta |
|---|---|---|---|
| R1 | Módulos `WMS_LOTSERIAL` y `PURCHASING` encendidos | **Cumple.** `SELECT ModuleKey, IsEnabled FROM TenantModule WHERE TenantId = 2` → los dos en 1 | — |
| R2 | Usuario con `receipts.view`, `warehouse.receive`, `purchasing.view` y `purchasing.receive` | **Cumple solo con el administrador.** Cruce de `Role` / `RolePermission` / `Permission`: el rol **TenantAdmin** (1 usuario) tiene los cuatro; **WarehouseOperator** (0 usuarios) tiene `warehouse.receive` pero **no** `purchasing.view` ni `purchasing.receive`. La compañía tiene 2 usuarios (1 TenantAdmin, 1 Sales Supervisor) | Grabar con un administrador de Solutions. Si se quiere grabar «como almacén», dar esos dos permisos al rol en Sistema → **Roles y usuarios** (`nav.users.title`) |
| R3 | Dos órdenes de compra en estatus «Enviada» (`SENT`) con su proveedor | **FALTA.** `SELECT COUNT(*)` en `Supplier`, `PurchaseOrder`, `ReceiptHeader`, `Asn` con `TenantId = 2` → 0, 0, 0, 0 | Lo resuelve la escena 2: dar de alta los proveedores e importar con estatus **«Completada (enviada, lista para recibir)»** (`poImport.statusSent`). Importadas como «Borrador» no aparecen en el paso 5 hasta enviarlas |
| R4 | El almacén puede abrir un recibo | **BLOQUEO.** `SELECT w.Code, w.Name, rm.InternalCode, w.DefaultReceivingBinId FROM Warehouse w LEFT JOIN LookupCode rm ON rm.LookupCodeId = w.ReceivingModeLookupId WHERE w.TenantId = 2` → `ALM-SOLUTIONS · Almacen Solutions · PUTAWAY · NULL` (un solo almacén, activo, en Caguas). Zonas con sus posiciones activas: **STG «Recepción» (STAGING): 0** · PCK «Picking» (PICKING): 79 · RSV «Reserva» (RESERVE): 0 · PISO «Piso» (PICKING): 0. Con acomodo y sin posición de recepción, «Crear recibo» responde 422 **«El almacén no tiene una posición de recepción (zona STAGING); indíquela.»** (`ReceiptService.cs:401-403`; `AsnService.cs:520-521`; `ReceiptRules.cs:42`) | **Recomendado:** Almacén → Almacenes → ALM-SOLUTIONS → pestaña **Datos** → sección **Recepción** → «Modo de recepción» = **Directo a posición** → Guardar → ventana «¿Cambiar el modo de recepción?» → **Cambiar el modo** → «Cambios guardados.» (permiso `warehouse.manage`; `WarehouseDetailScreen.tsx:182-246`; mismo recorrido en `lote16.spec.ts:244-262`). Alternativas: crear una posición en la zona STG (pestaña Posiciones → «Nueva posición»), o elegir «Directo a posición» en el campo «Modo de recepción» de cada recibo (funciona sin tocar el almacén, pero hay que explicarlo en cámara) |
| R5 | Posiciones de guardado donde poner lo recibido | **Cumple.** Las 79 posiciones de PCK: 78 de pasillo (`01-A-1` … `09-D-2`) y `PISO`. Todo el inventario actual está en `PISO`: `SELECT COUNT(*), SUM(QtyOnHand), SUM(QtyReserved) FROM StockBalance WHERE TenantId = 2` → 38 productos, 69,900 unidades, 35 reservadas | — |
| R6 | Productos de las órdenes: propios, activos y sin lote ni serie | **Cumple.** `SELECT tt.InternalCode, COUNT(*) FROM Product p LEFT JOIN LookupCode tt ON tt.LookupCodeId = p.TrackingTypeLookupId WHERE p.TenantId = 2 AND p.IsActive = 1 GROUP BY tt.InternalCode` → `NONE`: 42 (41 productos y 1 kit); los 42 con `ClientId` nulo (propios). Incluye 52800, 070120, NECH-1001 a NECH-1004 y los GLOBAL | — |
| R7 | Ajuste «El recibo exige documento» | **Libre** (`FREE`). `SELECT ReceiptRequirement, ValuationMethod FROM Tenant WHERE TenantId = 2` → `FREE · AVERAGE`. Por eso «Origen» ofrece las cuatro opciones y arranca en «Ciego» (`receiptSources.ts:5-23`) | No hace falta. Con «Solo orden de compra» el origen saldría ya puesto, pero cambia la política de la compañía |
| R8 | Etiquetas de estatus sin personalizar | **Cumple.** `StatusCode` + `StatusCodeOverride` (TenantId 2): sin personalizaciones. Recibo: Esperado · **Descargando** · Discrepancia · Completado · Completado con diferencia · Acomodado. Orden de compra: Borrador · Enviada · Recibida parcial · Recibida · Cancelada | — |
| R9 | Costo para la capa del recibo | **Cumple.** La orden trae su costo por línea y ese manda. Costo de compra ya guardado en los productos candidatos: 52800 = 3.05 · 070120 = 5.25 · NECH-1001 a 1004 = 3.50 (iguales a los del archivo de QuickBooks). Método de valuación: `AVERAGE` (Costo promedio) | — |
| R10 | Numeración | Solutions no tiene contadores de recibo, orden de compra ni aviso: `SELECT Kind, NextValue FROM NumberSequence WHERE TenantId = 2` → solo `SALESORDER`. Los primeros serán `PO-00001`, `REC-00001` y `LLE-00001` (`NumberSequenceService.cs:24-30`) | Los ensayos gastan números: restaurar la base antes de la toma buena |
| R11 | Existencias de partida (para el antes y después) | `SELECT p.Sku, s.QtyOnHand, s.QtyReserved, s.QtyAvailable FROM StockBalance s JOIN Product p ON p.ProductId = s.ProductId WHERE s.TenantId = 2` → 52800: 24,130 (30 reservadas) · 070120: 2,337 (5 reservadas) · NECH-1001: 995 · NECH-1002: 1,099 · NECH-1003: 835 · NECH-1004: 959. Sin existencia: 00607-3, 00802-2, 00804-6 y el kit KIT-001 | — |
| R12 | Avisos y correos disparados por el recibo | **Ninguno por regla.** `SELECT COUNT(*) FROM NotificationRule WHERE TenantId = 2` → 0. La confirmación publica el evento «aviso de llegada recibido» (`ReceiptService.cs:921`), pero la compañía no tiene ninguna regla de aviso que lo reparta | El API de la grabación va de todos modos sin la llave de Brevo (regla del proyecto) |
| R13 | Formato de números y fechas | `Tenant`: miles `,` · decimales `.` · fechas `MDY` con `/` · 12 horas · `America/Puerto_Rico`. En pantalla: 5,000 y 3.05 | — |

**Qué pasa con el costo y la valuación.** Al confirmar, cada línea con algo recibido **congela** su costo unitario con esta prioridad: costo de la línea de la orden de compra, luego precio del aviso, luego costo del producto (`ReceiptService.cs:836-854`; `CostLayerRules.cs:32-38`). El costo guardado en el producto **no cambia**. El valor del inventario y el costo de las salidas se calculan de esas capas con el método de la compañía, «Costo promedio» (`ValuationService.cs:26-43`; `ValuationRules.cs:37-46`). El recibo confirmado queda además disponible para Contabilidad → Contabilización de compras (`43-contabilizacion.md:24-29`), que no es parte de este video.

**Cómo queda la orden de compra.** Con todo recibido pasa sola a **Recibida** (en su historial queda el paso por «Recibida parcial») (`PurchaseOrderReceivingService.cs:47-68`; `PurchaseOrderRules.cs:123-140`). Si se recibe menos, queda **Recibida parcial** y admite otro recibo por lo pendiente. Una orden Recibida no se cancela ni se elimina (`PurchaseOrderRules.cs:42`, `:49`) y sus líneas muestran «Esta línea ya tiene recepciones: no se elimina, no baja de lo recibido y su costo no cambia.» (`PurchaseOrderDetailScreen.tsx:280-281`). Su aviso `LLE-#####` pasa a «Recibida» (`ReceiptService.cs:916-922`).

---

## 5. Riesgos al grabar (validaciones, ventanas, avisos, esperas, cosas que se ven feas, datos sensibles en pantalla)

**Mensajes que pueden salir y arruinar la toma**

- «El almacén no tiene una posición de recepción (zona STAGING); indíquela.» al crear el recibo, si el almacén sigue en «Con acomodo» (R4).
- El campo «Orden de compra» vacío, si las órdenes se importaron como «Borrador» o no existen (R3).
- «La orden de compra ya tiene un recibo abierto; confírmelo o elimínelo antes de recibir de nuevo.» (409), si quedó un recibo abierto de un ensayo (`ReceiptService.cs:347-353`; `ReceiptRules.cs:59`).
- «Falta la posición destino en N línea(s).» con «Confirmar recibo» apagado, si se salta el paso 8 (`ReceiptDetailPanel.tsx:226-230`; `receiptLineEdit.ts:550-558`).
- «Se asignó posición a N línea(s); M sin sugerencia.» con M mayor que 0, si la orden trae un producto **sin existencia**: el sistema solo sugiere la posición donde ya hay de ese producto, y la zona de reserva no tiene posiciones (`PutawayRules.cs:66-99`). Pasa con 00607-3, 00802-2 y 00804-6 (vienen en la factura grande de GLOBAL). Habría que elegir la posición a mano en esa línea.
- «Hay líneas sin guardar: salga del campo o pulse Enter para guardarlas.», si se teclea en «Recibido» y se deja el cursor ahí.
- La opción «Contra orden de compra» no aparece si el usuario no tiene `purchasing.receive` (R2).

**Es irreversible.** Un recibo confirmado no se modifica ni se borra («El recibo REC-… ya fue confirmado; no se puede modificar.», `ReceiptRules.cs:39`) y una orden Recibida no se elimina. El mismo archivo de QuickBooks no vuelve a crear una orden ya importada (`06-inventario-y-almacen.md:3095`). **Cada toma necesita la base en el punto de partida:** trabajar sobre una copia y restaurarla entre tomas.

**Esperas y avisos**

- Los avisos (toasts) salen abajo al centro y duran 3.5 s (6 s los de error) (`toastStore.ts:36-37`; `base.css:324`). Esperar a que se vayan antes del siguiente clic, o dejarlos a propósito mientras habla la narración.
- Tras «Crear recibo», la pista «Sugerida: …» de cada línea llega con una consulta aparte por línea: esperar el texto antes de seguir (`ReceiptLinesEditor.tsx:241-243`, `:268-270`).
- Los desplegables se abren solos al recibir el foco (`ComboSelect.tsx:146-162`): no hace falta un clic extra, pero la lista tapa lo que está debajo.

**Cosas que se ven feas o confunden**

- **Ancho de la ventana.** En un recibo directo la tabla de líneas se convierte en tarjetas si el panel mide menos de 760 px (`ReceiptLinesEditor.tsx:281-283`, `:460`). El panel mide el ancho de la ventana menos unos 646 px (menú 250, márgenes 40, lista 340, separación 16). Con los 1280 × 720 del perfil «Desktop Chrome» de Playwright quedan ~634 px: **saldrían tarjetas**. A 1920 × 1080 quedan ~1,274 px. Grabar a 1920 × 1080 (mínimo 1440 de ancho).
- El almacén se llama «Almacen Solutions», sin acento.
- La lista vacía dice «No hay recibos con estos filtros.» aunque no haya ningún filtro puesto.
- En la variante B aparecen dos botones parecidos, «Confirmar tanda» y «Confirmar recibo».
- En el encabezado del detalle sale el botón «Reportar daño», que no se usa.
- La mascota Kemi flota abajo a la derecha y puede tapar parte del detalle (`Kemi.tsx:1-6`): dormirla antes de grabar.
- El reloj de la barra superior muestra la hora real: si se cortan y pegan tomas, la hora salta.
- Las órdenes llevan la fecha de la factura de QuickBooks (abril a junio de 2026) y el recibo la fecha de la grabación.
- El manual 06 dice que el almacén de Solutions es `ALM-SOL`, con una sola posición `GENERAL` y en modo directo (`06-inventario-y-almacen.md:229-230`); la base dice `ALM-SOLUTIONS`, 79 posiciones y «Con acomodo». No usar ese párrafo para el guion.

**Datos sensibles en pantalla**

- **Costos reales de compra**: la columna «Costo unitario» del recibo confirmado (3.05, 5.25, 3.50) y, en Compras, el costo de cada línea.
- Nombres de los proveedores reales.
- Nombre de quien graba: sale en el Historial del recibo, en la columna de usuario del Kárdex y al pasar el cursor por el círculo de iniciales de la barra superior. No abrir Historial ni Kárdex si se graba con un usuario real.

---

## 6. Datos para la narración (hechos verificados, en frases cortas y en lenguaje de usuario)

- La orden de compra dice qué se compró. El inventario no se mueve hasta que se recibe.
- El recibo se hace contra la orden: se escoge el almacén, se escoge la orden y listo.
- El recibo nace con los productos de la orden y con la cantidad recibida igual a la esperada. Si llegó completo, no hay que teclear nada.
- Si llegó menos, se corrige la cantidad en «Recibido». La orden queda «Recibida parcial» y se puede recibir el resto otro día, con otro recibo.
- Cada recibo lleva su número: REC y cinco dígitos.
- Mientras se está recibiendo, el recibo dice «Descargando».
- Cada producto necesita una posición donde guardarse. Con un clic, el sistema propone la posición donde ya está ese mismo producto.
- El recibo se confirma completo, de una vez. Después de confirmar ya no se puede cambiar.
- Al confirmar, el inventario sube en ese mismo momento.
- La orden de compra pasa sola a «Recibida». Nadie tiene que cambiarle el estatus.
- El costo de cada unidad queda guardado con el costo de la orden de compra. Con ese costo se calcula el valor del inventario (Solutions usa costo promedio).
- Los productos de Advance Solutions no llevan lote ni número de serie, así que el recibo no los pide.
- Todo se hace desde la web. No hace falta el aparato del almacén.
- Antes de recibir, Advance Solutions tenía 69,900 unidades en inventario, de 38 productos.
- Si se reciben las dos órdenes propuestas: entran 15,000 unidades y el total queda en 84,900. Tirillas Prodigy (52800): de 24,130 a 29,130. Monitores Prodigy (070120): de 2,337 a 3,337. Guantes Padimont (NECH-1001 a 1004): entran 9,000.
- El recibo confirmado queda guardado con su número, su orden de compra y su proveedor, y se puede buscar por cualquiera de los tres.

Palabras exactas de la pantalla que la voz debe usar: «Recibo», «Nuevo recibo», «Contra orden de compra», «Orden de compra», «Crear recibo», «Esperado», «Recibido», «Diferencia», «Posición destino», «Usar posiciones sugeridas», «Confirmar recibo», «Acomodado», «Costo unitario», «Recibida», «Productos e inventario», «Unidades totales». Decir «Descargando», no «Recibiendo»; y «Cargas», no «Maestros».

---

## 7. Duración estimada en pantalla (segundos) y qué se puede recortar si hay que acortar

| Tramo | Segundos |
|---|---|
| Entrar a Recibo y situar la pantalla (lista vacía) | 6 |
| Nuevo recibo: origen, almacén y orden | 16 |
| Crear recibo y leer el detalle (líneas, esperado = recibido, la sugerida) | 14 |
| Usar posiciones sugeridas | 6 |
| Confirmar recibo: ventana y resultado (Acomodado, Costo unitario) | 12 |
| Segunda orden, acelerada o con cortes | 18 |
| Compras: las dos órdenes «Recibida» | 8 |
| Productos e inventario: «Unidades totales» y una fila | 15 |
| **Total** | **95** |

**Para acortar (hasta unos 60 s):** quitar la visita a Compras y decirlo con la voz (−8); de la segunda orden mostrar solo el resultado, con un corte (−10); no escribir la Referencia; entrar a cada pantalla por dirección en vez de por el menú (−6); en Productos e inventario mostrar solo el indicador, sin filtrar (−6).

**Para alargar, si se quiere:** Kárdex con el movimiento «Recepción» (+10); un faltante de ejemplo en una línea (+15); elegir a mano una posición de pasillo (+10 por línea); variante B con tareas de acomodo (+15 por orden).

---

## 8. Dudas y decisiones para Luis

1. **Modo de recepción del almacén de Solutions.** Hoy está en «Con acomodo» sin posición de recepción y no deja recibir. *Recomendación:* pasarlo a «Directo a posición» en la base del video (un minuto, en pantalla). Duda aparte: la base local es copia de TEST del 2026-11-11, así que TEST y probablemente producción están igual; ¿lo cambia Luis también ahí antes de que Solutions reciba de verdad? (Yo no toco TEST ni producción.)
2. **¿A qué posición entra lo recibido en el video?** *Recomendación:* a PISO, con «Usar posiciones sugeridas» (un clic, y es donde hoy está todo). Si se quiere lucir las 78 posiciones de pasillo hay que elegirlas a mano, línea por línea.
3. **¿Una orden a la vez o «Recibir juntos»?** *Recomendación:* una a la vez, la segunda acelerada. Es más claro y no mete el concepto de «Carga».
4. **¿Cuáles dos órdenes?** *Recomendación:* del archivo de QuickBooks, la factura `PDC-04292026` de ADVANCE LOGISTICS LLC (2 líneas: 52800 × 5,000 a 3.05 y 070120 × 1,000 a 5.25) y la `PDC-05282026` de PADIMONT HOLDINGS CORP (4 líneas: NECH-1001 a 1004, 9,000 en total a 3.50), en un archivo recortado a esas dos. Evitar la factura grande de GLOBAL IMPORT SALES LLC (17 líneas, tres de ellas sin existencia y por eso sin posición sugerida). Nota: el proveedor de la primera es una empresa del mismo grupo; Luis decide si quiere ese nombre en pantalla.
5. **¿Recibo completo o con un faltante?** *Recomendación:* completo, como se pidió; el faltante se menciona con la voz.
6. **¿Se pueden ver los costos reales de compra?** *Recomendación:* sí, si el video es para uso interno (Leandra, almacén); si va a salir de la empresa, no detenerse en «Costo unitario».
7. **Pantalla de cierre.** *Recomendación:* Compras (las dos «Recibida») y luego Productos e inventario. El Kárdex, solo si sobra tiempo.
8. **¿Con qué usuario se graba?** Solo el administrador de la compañía puede recibir contra una orden. *Recomendación:* un usuario administrador de Solutions con un nombre neutro, creado para el video, y no abrir Historial ni Kárdex.
9. **Nombre del almacén sin acento** («Almacen Solutions»). *Recomendación:* corregirlo a «Almacén Solutions» en la base del video; Luis decide si también en producción.
10. **Manual desactualizado.** El manual 06 (§1.4 y §4.1) describe el almacén de Solutions como `ALM-SOL` / `GENERAL` / directo, y el capítulo 44 habla de «Maestros» donde la pantalla dice «Cargas». No bloquea el video; queda como pendiente de documentación.

**Dudas que no pude cerrar sin levantar la aplicación**

- Los localizadores del menú y del campo «Orden de compra» (pasos 1 y 5) no están en ningún recorrido existente. Ningún recorrido de `web-app/e2e` recibe contra una orden de compra por pantalla: el de esta escena será el primero.
- Los números exactos (`PO-00001`, `REC-00001`) dependen de que la base no tenga ensayos previos.
- Las cifras de «Unidades totales» y de las filas dependen de qué órdenes importe la escena 2.
- No verifiqué en ejecución que «Usar posiciones sugeridas» devuelva PISO para estos productos; lo deduje del código (`PutawaySuggester.cs:47-83`; `PutawayRules.cs:66-99`) y de los saldos. Comprobar en el primer ensayo.

---

## 9. Fuentes (archivo:línea)

Rutas relativas a `F:/Visual Studio 2022/Projects/teikem`.

**Web (`web-app/src`)**

- `app/routes.tsx:193-205` (Almacenes), `:217-222` (Productos e inventario), `:239-244` (Compras), `:252-258` (Llegadas y salidas), `:260-268` (Recibo), `:376-382` (Kárdex)
- `app/AppShell.tsx:240-289` (menú: grupos y enlaces), `:334` (reloj), `:336-353` (compañía en la barra), `:366-375` (círculo de iniciales)
- `features/warehouse/ReceiptListScreen.tsx:33`, `:141-155`, `:164-174`, `:176-225`
- `features/warehouse/ReceiptHeaderModal.tsx:104-107`, `:115-117`, `:168-184`, `:196-203`, `:221-224`, `:316-318`, `:332-349`, `:383-443`
- `features/warehouse/receiptSources.ts:5-23`
- `features/warehouse/ReceiptDetailPanel.tsx:76-100`, `:104-140`, `:155-187`, `:202-235`, `:273-298`
- `features/warehouse/ReceiptLinesEditor.tsx:2-3`, `:62-92`, `:234-283`, `:309-386`, `:388-445`, `:460`
- `features/warehouse/receiptLineEdit.ts:118-148`, `:529-558`
- `features/warehouse/receivingMode.ts:14-17`, `:83-93`
- `features/warehouse/ReceiptMasterList.tsx:67-114`
- `features/warehouse/ReceiptPutawayTasks.tsx:92-135`; `PutawayPendingTab.tsx:28-39`, `:79-83`
- `features/warehouse/ReceiptGroupCreateModal.tsx:17-24`, `:32-42`, `:56-58`; `ReceiptGroupModal.tsx:66-72`, `:138-140`; `receiptGroupView.ts:26`, `:70-78`
- `features/warehouse/NoticesScreen.tsx:29-37`, `:48-59`; `AsnsTab.tsx:105-110`, `:138-165`; `AsnCreateModal.tsx:63`, `:148`
- `features/warehouse/PurchaseOrderDetailScreen.tsx:9`, `:56-57`, `:280-281`, `:405-437`
- `features/warehouse/ProductListScreen.tsx:127-181`, `:300-316`, `:381-388`
- `features/warehouse/WarehouseDetailScreen.tsx:182-246`, `:623`, `:974-979`
- `features/warehouse/pickers.tsx:86-225`; `features/warehouse/api.ts:90-99`
- `features/warehouse/warehouse.css:248`, `:265-266`; `styles/base.css:110-111`, `:143`, `:324-325`; `styles/tokens.css:49`
- `features/notifications/Kemi.tsx:1-6`
- `kernel/ui/ComboSelect.tsx:146-162`, `:186`; `kernel/ui/ConfirmDialog.tsx:44-60`; `kernel/ui/Modal.tsx:63`; `kernel/ui/toastStore.ts:36-37`
- `kernel/i18n/es.json:135`, `:237-238`, `:272-287`, `:300-303`, `:4917`, `:4935-4943`, `:5128`, `:5345-5375`, `:5657`, `:5805-5810`, `:6092-6113`, `:6424-6441`, `:6486`, `:6494-6513`, `:6542-6554`, `:6565-6586`, `:6587-6641`, `:6642-6651`, `:6677-6736`, `:6749-6752`, `:7806-7808`, `:12606-12619`; `kernel/i18n/en.json` (mismas claves)

**Servidor (`src`)**

- `Teikem.Api/Controllers/ReceiptsController.cs:18-22`, `:32`, `:66-72`, `:104-105`, `:149-152`, `:158-161`, `:186-189`
- `Teikem.Infrastructure/Services/ReceiptService.cs:18-38`, `:63`, `:223-256`, `:275-296`, `:341-355`, `:390-416`, `:425-427`, `:743-991`, `:1333-1369`, `:1422-1430`, `:1495-1521`, `:1928-1931`, `:2043`
- `Teikem.Infrastructure/Services/PurchaseOrderReceivingService.cs:23-45`, `:47-68`, `:73-76`
- `Teikem.Infrastructure/Services/AsnService.cs:345-371`, `:515-548`
- `Teikem.Infrastructure/Services/ReceiptGroupService.cs:31-76`, `:100-112`
- `Teikem.Infrastructure/Services/PutawayTaskHandler.cs:12-22`, `:48-112`
- `Teikem.Infrastructure/Services/ValuationService.cs:26-43`
- `Teikem.Infrastructure/Wms/PutawaySuggester.cs:47-83`
- `Teikem.Infrastructure/Orders/NumberSequenceService.cs:24-30`
- `Teikem.Domain/Wms/ReceiptRules.cs:39-45`, `:59`, `:196`, `:244-261`
- `Teikem.Domain/Wms/ReceivingModeRules.cs:15`, `:51-57`, `:63-68`, `:74-75`
- `Teikem.Domain/Wms/PurchaseOrderRules.cs:38`, `:42-51`, `:116-117`, `:123-140`; `PurchaseStatusRules.cs:9`
- `Teikem.Domain/Wms/PutawayRules.cs:44-58`, `:66-99`; `CostLayerRules.cs:32-38`; `ValuationRules.cs:22`, `:37-46`
- `Teikem.Domain/Wms/Receiving.cs:10` (`LLE-#####`); `ReceiptGroup.cs:9` (`RCM-#####`); `StockAllocator.cs:27`
- `Teikem.Domain/Constants/CatalogDomains.cs:1024-1027`

**Manual y documentos (`docs`)**

- `manual/06-inventario-y-almacen.md:220-253` (modo de recepción), `:1108-1114`, `:1152-1154`, `:1232-1239`, `:1294-1338` (estatus y efectos de confirmar), `:1384-1389` y `:1421-1429` (recibo directo), `:2931-2939` (capa de costo y valuación), `:2941-2942` (el recibo exige documento), `:2971-2991` (tandas), `:3056-3095` (importar órdenes de compra)
- `manual/44-recepcion-maestra.md:1-25`, `:58`, `:71-74`
- `manual/frontend/f6-almacen-e-inventario.md:811-1013`
- `manual/faq.md:1584-1600`
- `manual/43-contabilizacion.md:3-5`, `:24-29`
- `lote239-decisiones.md:8-12`, `:18`; `lote240-decisiones.md:11`; `lote238-decisiones.md:11`
- `migracion/saldo-inicial-solutions.md:3`

**Recorridos de Playwright (`web-app`)**

- `e2e/lote16.spec.ts:220-224` (ventana de 1600 px para ver la tabla), `:241-271` (cambiar el modo del almacén), `:273-364` (recibo directo completo)
- `e2e/f6.spec.ts:393-438` (recibo con acomodo), `:440-474` (completar una tarea de acomodo)
- `e2e/lote182-recepcion-maestra.spec.ts:31-59` (Recibir juntos y Confirmar todo)
- `e2e/staffHelpers.ts:52-63` (entrada a la web); `e2e/globalSetup.ts:1-2`, `:43-44` (se niega a correr con correo real)
- `playwright.config.ts:31` (perfil «Desktop Chrome», 1280 × 720)

**Datos**

- Base local `Teikem`, `TenantId = 2`: tablas `Warehouse`, `WarehouseZone`, `WarehouseBin`, `WarehouseDock`, `Product`, `StockBalance`, `InventoryTransaction`, `Tenant`, `TenantModule`, `Supplier`, `PurchaseOrder`, `ReceiptHeader`, `Asn`, `WarehouseTask`, `NumberSequence`, `NotificationRule`, `Role`, `RolePermission`, `Permission`, `UserRole`, `StatusCode`, `StatusCodeOverride`, `LookupCode` (consultas resumidas en la sección 4)
- `F:/Download/Solutions Purchases.CSV`: filas 15-16 (`PDC-04292026`), 48-64 (factura grande de GLOBAL), 81-84 (`PDC-05282026`)
