# Escena 4 - Orden de venta con el asistente (pedido guiado): mapa para el video

Investigado el 2026-11-12 en solo lectura (código de `master`, manual y base local `Teikem`, compañía 2 = Advance Solutions). No se levantó ningún servidor: lo que se dice de cómo «se ve» sale del código, del manual y de las capturas del lote 228/238; hay que confirmarlo en el ensayo con Playwright mirando la captura.

## 1. Qué es y dónde está (menú, ruta, permiso, módulo)

- **Qué es.** El asistente («pedido guiado») es uno de los dos modos de crear la misma orden de venta: cuatro pasos a pantalla completa (Cliente → Productos → Entrega → Autorización). Cada «Siguiente» guarda la orden como borrador; «Enviar pedido» pide la contraseña y solicita la orden (valida inventario y crédito y reserva).
- **Menú.** Grupo **Contabilidad** (`nav.groups.money`) → ítem **Ventas** (`nav.salesOrders.title`). Ojo: el menú dice «Ventas»; el título de la pantalla es **«Órdenes de venta»** (`sales.title`). El manual escribe «Contabilidad → Órdenes de venta»; en la narración hay que decir lo que se ve: «Contabilidad, Ventas».
- **Rutas.** Lista: `/sales-orders` (permiso `sales.view`). Asistente: `/sales-orders/new-order` (permiso `sales.create`), sin ítem de menú propio. Un borrador se retoma con `/sales-orders/new-order?order=<publicId>`.
- **Módulo.** `SALES_ORDERS` (encendido en Solutions).
- **Cómo se entra (Lote 238).** En Órdenes de venta, arriba a la derecha hay dos botones: **«Nueva orden»** (`sales.new`), que abre el último modo de esa persona, y un botón pequeño con el otro modo: **«Con asistente»** (`sales.modeWizard`) o **«Con formulario»** (`sales.modeFull`). El modo se guarda por persona y compañía en el servidor (`sales.entryMode`: `wizard` o `full`); sin preferencia: asistente hasta 900 px de ancho y formulario en escritorio.
- **Otros permisos que usa el recorrido.** `clients.read` (buscador de clientes y ficha), `locations.read` (escoger «Otra dirección (consignatario)»), `sales.view` (catálogo, disponible y fotos). El rol TenantAdmin de Solutions los tiene todos.

## 2. Qué conviene mostrar, en orden (lo que pidió el dueño y lo que ayuda a entenderlo)

1. **Entrada**: Contabilidad → Ventas → botón «Con asistente» (una frase: hay dos modos, el asistente es el pensado para tableta y celular).
2. **Paso 1, Cliente**: buscar el cliente por nombre, escogerlo, y ver que la **forma de pago viene de su ficha** (p. ej. «Crédito» y «30 días»).
3. **Paso 2, Productos** (lo que pidió Luis: un kit y un producto):
   - Pestañas por marca; la **oferta como tarjeta** con foto, precio y «Sin fecha de fin».
   - El kit dice **«Se pueden armar: N»** con sus piezas (6 tirillas + 1 monitor) y cuál **«Limita»**.
   - «+» en el kit; luego la marca PADIMONT y una cantidad en unos guantes; cada producto dice **«Disponible: N»**.
   - El **carrito** abajo: productos, unidades y subtotal; abierto, el kit enseña sus piezas.
4. **Paso 3, Entrega**: «Entrega» o «Recogido», a dónde, y el **Resumen** (Subtotal, IVU, Cargo de envío, Total) que calcula el sistema.
5. **Paso 4, Autorización**: nombre de quien autoriza y **firma dibujada** (lo que Luis pidió: «se firma al final»).
6. **Enviar pedido**: ventana **«Confirme con su contraseña»** → pantalla **«Pedido enviado»** con el número OV y el total.
7. **Cierre (puente a la escena 5)**: «Ver en Órdenes de venta» → la orden en estatus **Solicitada**, con lo **Reservado** y la firma.

Opcional (se puede cortar): «Pedido urgente» y nota; pedir de más para enseñar el distintivo «Backorder: N»; clic en una foto para ampliarla; «Cambiar» una pieza del kit.

## 3. Paso a paso exacto del recorrido

Supone: sesión abierta en Advance Solutions, interfaz en español, y los arreglos de datos de la sección 4 (kit OFERTA1 con marca PRODIGY; cliente de demostración). Donde los datos de hoy cambian el paso, va la **variante «hoy»**. Los localizadores salen del código y de `web-app/e2e/lote228-pedido-vendedores.spec.ts` y `lote238-modo-captura-y-recoleccion.spec.ts`.

| # | Acción del usuario | Etiqueta en pantalla (es) y clave i18n | Cómo localizarlo en Playwright | Qué debe verse después |
|---|---|---|---|---|
| 1 | Abrir el grupo del menú | «Contabilidad» (`nav.groups.money`) | `page.getByRole('button', { name: 'Contabilidad' })` (botón `.grp-h`, acordeón) | El grupo abierto con sus ítems |
| 2 | Entrar a ventas | «Ventas» (`nav.salesOrders.title`) | `page.getByRole('link', { name: 'Ventas', exact: true })` | Título «Órdenes de venta» (`sales.title`), pestañas Órdenes / Backorders / Devoluciones / QuickBooks y los botones «Nueva orden» + «Con asistente» |
| 3 | Abrir el asistente | «Con asistente» (`sales.modeWizard`). Si la persona ya tiene el modo asistente, el botón grande «Nueva orden» (`sales.new`) abre lo mismo y el pequeño dice «Con formulario» | `page.getByRole('button', { name: 'Con asistente' })` (o `getByRole('button', { name: 'Nueva orden', exact: true })`) | URL `/sales-orders/new-order`; título «Nuevo pedido» (`salesWizard.title`); «Paso 1 de 4: Cliente» (`salesWizard.stepOf`); arriba «Cambiar al formulario completo» y «Salir a Órdenes de venta». El buscador toma el foco y **se despliega la lista de los primeros 30 clientes** |
| 4 | Escribir el nombre del cliente | «Buscar cliente» (`salesWizard.client.search`), ayuda «Número, nombre o teléfono» | `page.getByLabel('Buscar cliente')` + `pressSequentially(texto, { delay })` (espera de 300 ms antes de consultar) | Lista «Clientes encontrados» (`salesWizard.client.results`) con nombre y «Núm. {código}» |
| 5 | Escoger el cliente | Nombre del cliente | `page.getByRole('list', { name: 'Clientes encontrados' }).getByRole('button', { name: /NOMBRE/ })` | Recuadro `.wz-picked` con el nombre, «Núm. {código}» y el botón «Cambiar cliente». Con los clientes de hoy no sale el pueblo (no tienen dirección física) |
| 6 | Mirar la forma de pago (no se toca) | «Datos del pedido» (`salesWizard.client.orderData`), «Forma de pago» (`salesWizard.client.payment`), «Término del crédito» (`salesWizard.client.term`) | `page.getByLabel('Forma de pago')`, `page.getByLabel('Término del crédito')`; esperar a que tenga valor (`toHaveValue('CREDIT')` / `'NET30'`) | Con un cliente de 30 días: «Crédito» y «30 días». Con uno de cheque: «Contra entrega: cheque» |
| 6b | (Opcional) urgente y nota | «Pedido urgente» (`salesWizard.client.urgent`), «Nota del pedido» (`salesWizard.client.notes`) | `page.getByRole('switch', { name: 'Pedido urgente' })`, `page.getByLabel('Nota del pedido')` | Contador «N de 500 caracteres» |
| 7 | Siguiente | «Siguiente» (`salesWizard.next`); mientras guarda «Guardando…» | `page.getByRole('button', { name: 'Siguiente' })` | «Paso 2 de 4: Productos»; bajo el título «Borrador OV-0000N: se guarda en cada paso.» (`salesWizard.draftNumber`); aparece arriba el botón rojo «Borrar orden» (borrador aún sin productos) |
| 8 | Ir a la marca del kit | Pestaña «PRODIGY» (marca del catálogo). **Hoy:** el kit está en la última pestaña, «Sin marca» (`salesWizard.products.noBrand`) | `page.getByRole('tab', { name: 'PRODIGY' })` (hoy: `'Sin marca'`) | «Ofertas» (`salesWizard.products.offers`) con la tarjeta del kit: chip «Oferta», nombre, SKU, nombre de la oferta, «Sin fecha de fin» (`salesWizard.products.noEndDate`), precio; debajo «Todos los productos de la marca» y el buscador «Buscar por SKU o nombre» |
| 9 | Mirar el disponible del kit (pausa) | «Se pueden armar: {n}» (`sales.stock.kitAvailable`), piezas con «× 6», «{n} disp.» (`sales.stock.pieceAvailable`) y «Limita» (`sales.stock.kitLimit`) | `page.locator('li.wz-offer', { hasText: SKU_KIT }).locator('.sales-stock-avail')`, `.sales-kitstock li` | Hoy: «Se pueden armar: 2,332»; 52800 × 6 — 24,100 disp.; 070120 × 1 — 2,332 disp. · Limita |
| 10 | Agregar un kit | Botón «+» (nombre accesible «Agregar uno de {nombre}», `salesWizard.products.more`) | `page.locator('li.wz-offer', { hasText: SKU_KIT }).getByRole('button', { name: /^Agregar uno de / })` | La casilla «Cantidad de {SKU}» queda en 1; la tarjeta se marca; el carrito dice «Carrito: 1 producto(s), 1 unidad(es)» y el subtotal |
| 11 | Ir a los guantes | Pestaña «PADIMONT» | `page.getByRole('tab', { name: 'PADIMONT' })` | Lista «Productos de PADIMONT»: foto, SKU, nombre, «Disponible: N» (`sales.stock.available`), casilla y precio $5.00 |
| 12 | Teclear la cantidad | Casilla «Cantidad de NECH-1002» (`salesWizard.products.qtyOf`), ayuda «Cantidad» | `page.getByLabel('Cantidad de NECH-1002').fill('10')` (solo dígitos) | Renglón resaltado; carrito «Carrito: 2 producto(s), 11 unidad(es)» |
| 12b | (Opcional) pedir de más y corregir | «Backorder: {n}» (`sales.stock.backorderChip`), «Solo hay {x} disponibles en {almacén}; {n} quedan en backorder.» | Mismo campo con una cantidad mayor que el disponible; `.sales-stock-short` | Renglón en rojo con el distintivo; no bloquea |
| 13 | Abrir el carrito | «Toque para ver y corregir las líneas» (`salesWizard.cart.show`) | `page.locator('.wz-cart-toggle').click()`; región `page.getByRole('region', { name: 'Carrito' })` | Las dos líneas con cantidad, importe y «Quitar»; el kit con el chip «Kit», «Oferta «{nombre}»» y sus piezas («Componentes»); el monitor trae el botón «Cambiar» (`sales.kit.change`) porque el 051885 es de la misma categoría y precio |
| 14 | Siguiente | «Siguiente» | `page.getByRole('button', { name: 'Siguiente' })` (en escritorio queda bajo la lista: desplazar `.stage`) | «Paso 3 de 4: Entrega»; desaparece «Borrar orden» |
| 15 | Mirar la entrega | «¿Cómo recibe el cliente el pedido?» (`salesWizard.delivery.how`): «Entrega» (marcada) / «Recogido»; «¿A dónde se entrega?» (`salesWizard.delivery.where`) | `page.getByRole('radio', { name: /^Entrega/ })`, `page.getByRole('radio', { name: /Dirección física del cliente/ })` | Con dirección física: «Dirección física del cliente» ya marcada con la dirección. **Hoy:** sale el texto «El cliente no tiene dirección física activa: escoja otra dirección.» y hay que hacer el paso 15b |
| 15b | (Hoy, obligatorio con «Entrega») escoger el destino | «Otra dirección (consignatario)» (`salesWizard.delivery.other`), ayuda «Buscar consignatario…» (`orders.consignee.search`) | `page.locator('#wz-cons').click()` (combobox) → `page.getByRole('option').first().click()` (cada cliente tiene un solo consignatario) | «Calculando el envío…» un instante y el resumen se actualiza |
| 16 | Mirar el resumen (pausa) | «Resumen» (`salesWizard.summary.title`): «Subtotal», «IVU», «Cargo de envío», «Total»; nota «El envío y el IVU los calcula el sistema con las reglas de la compañía y del cliente.» | `page.locator('.wz-summary')`, total en `.wz-total` | Hoy: Cargo de envío «Sin cargo» (`salesWizard.summary.noShipping`) y sin «Por qué» (la compañía no tiene cargo configurado). Importes en la sección 4 |
| 17 | Siguiente | «Siguiente» | igual | «Paso 4 de 4: Autorización»; título del paso «Autorización y envío» (`salesWizard.auth.title`) |
| 18 | Escribir quién autoriza | «Nombre de quien autoriza» (`salesWizard.auth.name`) | `page.getByLabel('Nombre de quien autoriza')` | El nombre escrito |
| 19 | Dibujar la firma | «Firma de quien autoriza» (`salesWizard.auth.signature`); ayuda «Firme dentro del cuadro con el dedo o el ratón.» (`ui.signature.hint`); botón «Borrar firma» | `const pad = page.locator('svg.sigpad-box')`; `boundingBox()`; `page.mouse.move/down`, varios `mouse.move(..., { steps })`, `mouse.up()` (así lo hace el recorrido 228, líneas 389-395); comprobar `toHaveAttribute('aria-label', /firmado/)` | El trazo en el cuadro; «Firma lista. Use «Borrar firma» para repetirla.»; debajo «La firma es opcional para este cliente.»; resumen final Cliente / Productos / Total |
| 20 | Enviar | «Enviar pedido» (`salesWizard.send`) | `page.getByRole('button', { name: 'Enviar pedido' })` (todavía no hay ventana) | Sube la firma y abre la ventana «Confirme con su contraseña» (`salesWizard.submit.title`) con «Para enviar el pedido escriba la contraseña con la que entra a Teikem.» |
| 21 | Escribir la contraseña | «Contraseña» (`salesWizard.submit.password`); si la cuenta tiene MFA marcado, también «Código de verificación (MFA)» (`salesWizard.submit.mfa`) | `const pwd = page.getByRole('dialog', { name: 'Confirme con su contraseña' })`; `pwd.getByLabel('Contraseña', { exact: true })` (la contraseña llega por variable de entorno; se ve con puntos) | Puntos en el campo; el ojito «Mostrar contraseña» NO se toca |
| 22 | Confirmar | «Enviar pedido» (`salesWizard.submit.confirm`); mientras envía «Enviando…» | `pwd.getByRole('button', { name: 'Enviar pedido' })` | Pantalla «Pedido enviado» (`salesWizard.done.title`): número `OV-0000N` grande (`.wz-done-number`), Cliente, Total y los botones «Nuevo pedido» y «Ver en Órdenes de venta» |
| 23 | Ver la orden | «Ver en Órdenes de venta» (`salesWizard.done.view`) | `page.getByRole('link', { name: 'Ver en Órdenes de venta' })` | Ventana «Orden OV-0000N» (`sales.view.title`): chip **«Solicitada»**, «Entrega», forma de pago, «Quién autoriza el pedido» con la firma; tabla con Pedido / **Reservado** / Backorder / Despachado / Precio / Impuesto / Importe; «Componentes de los kits» con lo reservado de cada pieza; botones «Despachar», «Cancelar orden», «Cerrar» |

Esperas útiles: `page.getByText('Paso N de 4: …')` tras cada «Siguiente»; `li.wz-prod .sales-stock` visible antes de enseñar el disponible; `page.getByRole('heading', { name: 'Pedido enviado' })` con 30 s de margen.

## 4. Requisitos de datos y de configuración para que funcione con Advance Solutions

Todas las consultas se hicieron con `sqlcmd … -d Teikem` y `TenantId = 2`, solo lectura.

| # | Requisito | Estado HOY (consulta → resultado) | Si falta, cómo se arregla |
|---|---|---|---|
| 1 | Módulo y permisos | `TenantModule`: `SALES_ORDERS`=1, `TAX`=1, `BILLING`=0. `RolePermission` de TenantAdmin: `sales.create`, `sales.view`, `sales.price`, `clients.read`, `locations.read`, `locations.create`, `orders.credit_override`, `warehouse.pick`, `sales.quickbooks.*`. El rol «Sales Supervisor» de la compañía NO tiene ningún `sales.*`. No existe un rol «Ventas» en Solutions | Grabar con un usuario TenantAdmin. Para un vendedor real: `scripts/solutions-pedido-vendedores/03-roles.sql` |
| 2 | Usuario para grabar | `UserTenant` de la compañía 2: usuario 2 (TenantAdmin, 2 compañías) y usuario 7 (Sales Supervisor). El usuario 1 es administrador de plataforma. Los tres tienen `AspNetUsers.TwoFactorEnabled = 1` pero `UserMfaFactor` está vacía (0 filas en toda la base). Ninguno está bloqueado | Ver riesgo del campo MFA (sección 5) y decisión 5 |
| 3 | Modo de captura guardado | `UserPreference`: una sola fila en toda la base: usuario 1, compañía 2, `sales.entryMode = wizard`. El usuario 2 no tiene preferencia → en escritorio «Nueva orden» le abre el formulario y el botón pequeño dice «Con asistente» | Fijar el modo antes de la toma (clic previo, o `PUT /api/v1/me/preferences/sales.entryMode` en la preparación del recorrido, como hace el recorrido 238) |
| 4 | Cliente adecuado | `Client`: 701 activos, los 701 en estatus `ACTIVE` (ningún prospecto ni suspendido); término de pago: CHEQUE 374, NET30 202, NET15 40, NET45 21, ACH 21, CASH 20, CONSIGNMENT 11, NET20 3, sin término 9; `RequireOrderSignature` = 0 en los 701; `CreditLimit` NULL en los 701 (sin límite: el crédito nunca frena); `ShippingMode` NULL en los 701. `Location`: 701 de tipo DELIVERY (una por cliente, con el mismo nombre del cliente), 51 BILLING, **0 CORPORATE** (ningún cliente tiene dirección física). Con término que se prellena y dirección de entrega completa (línea 1, pueblo distinto de «SIN CIUDAD», código postal): **687 clientes**. Hay un «TEST CUSTOMER(NOT USE)» sin término y sin pueblo: no sirve | Escoger uno de 30 días con nombre corto para que se vea «Crédito / 30 días» (ejemplos: FARMACIA ARLEEN, de Caguas; FARMACIA APOLO, de San Juan), o crear un cliente de demostración en Listas → Clientes y contratos. Para que el paso 3 traiga marcada «Dirección física del cliente», ponerle dirección física en su ficha |
| 5 | Oferta vigente y kit | `Offer`: 1 sola, «OFERTA DEL DIA», publicada, activa, desde 2026-11-10, sin fecha de fin, para todos, sin foto. `OfferItem`: 1 renglón, el kit `KIT-001` «kIT DE mONITOR gRATIS» a **$8.50**. `ProductKitComponent`: 6 × 52800 (PRODIGY STRIPS 1/50CT.) + 1 × 070120 (PRODIGY MONITOR 1EA.), ninguna «no sustituible», no ajustable. El kit tiene `Brand` NULL (sale en la pestaña «Sin marca»), sin foto propia (se verá la de las tirillas) y `TaxCategoryLookupId` NULL (cobra IVU aunque sus piezas son exentas). **No existe el kit OFERTA1** ($41.94) que describe el script 02 | `scripts/solutions-pedido-vendedores/02-kit-oferta1.sql` crea el kit OFERTA1 (marca PRODIGY, $41.94, exento como las tirillas) y la oferta «OFERTA1»; luego desactivar «OFERTA DEL DIA» en Contabilidad → Precios y ofertas y poner la foto del renglón. Desde la pantalla solo se puede cambiar el nombre y el precio de KIT-001; la marca no (la ventana «Crear un kit nuevo» no tiene ese campo) |
| 6 | Lista de precios | `PriceList`: 1, «Lista general (precios de QuickBooks, 2026-11-10)», publicada, activa, vigente desde 2026-11-11, sin fin, para todos, 39 renglones; en los 39 el precio es igual al precio de venta del producto. Sin lista negociada de cliente. Los 2 underpads no están en la lista y toman su precio de venta ($28.99). Ningún producto queda «Sin precio» | Nada |
| 7 | Inventario disponible | `StockBalance` (todo en la posición PISO, zona de picking activa): 52800 = 24,130 en mano, 30 reservadas → 24,100; 070120 = 2,337, 5 reservadas → 2,332; kits que se pueden armar = 2,332 (limita el monitor). Guantes: NECH-1001 995, NECH-1002 1,099, NECH-1003 835, NECH-1004 959. Un solo almacén activo (ALM-SOLUTIONS): el campo «Almacén» no aparece | Hoy alcanza aunque la escena 3 no reciba nada. Las cifras de «Disponible» del video dependen de lo que se reciba antes |
| 8 | Cargo de envío | `Tenant`: `SalesShippingCharge` NULL, `SalesShippingFreeFrom` NULL, `SalesShippingTaxable` 0 → «Cargo de envío: Sin cargo», sin «Por qué» | Si se quiere enseñar: Sistema → Ajustes → Operación → Ventas, o `08-ajustes-envio-quickbooks.sql` ($12, gratis desde $120 después del IVU) |
| 9 | IVU | `TenantTaxSetting`: tasa por omisión `IVU11-5` (11.5 % = estatal 10.5 + municipal 1), precios sin impuesto, redondeo por línea. Exentos (categoría EXEMPT): los 7 PRODIGY y los 4 guantes PADIMONT. 0 exenciones de cliente | Con kit exento + guantes el IVU sale $0.00; para enseñar un IVU distinto de cero hay que vender algo gravable (p. ej. un MEDPRIDE) |
| 10 | Firma exigida | No hay ajuste de compañía; es por cliente (`Client.RequireOrderSignature`) y ninguno la exige → la firma es **opcional** («La firma es opcional para este cliente.») | Si se quiere ver «Este cliente exige el nombre y la firma…», marcarlo en la ficha del cliente de demostración |
| 11 | Almacén de archivos | La firma se sube como PNG cifrado: `StoredFile` de la compañía tiene 1 firma de orden (2026-11-11) y 64 fotos de producto (2026-11-12), así que en esta máquina está configurado. 34 productos con foto y 7 sin foto (121212, 979, 00607-3, 00802-2, 00804-6, 53310, 53350) | El API de la grabación debe usar la misma llave y carpeta de archivos; si no, «Enviar pedido» falla al subir la firma con «El almacén de archivos no está configurado (Files:MasterKey); avise a soporte.» y las fotos no se ven |
| 12 | Avisos y correos | `NotificationRule` de la compañía: 0 → al solicitar se anota el evento, pero no llega campana ni correo a nadie | Nada (y el API de la grabación va sin llave de Brevo) |
| 13 | Número de la orden | `NumberSequence` SALESORDER: `NextValue` = 5. Órdenes existentes: OV-00001 (cancelada, vacía) y OV-00002 (solicitada, 5 × KIT-001, recogido, firmada). Los números no se reutilizan | La orden del video sería OV-00005 si no se hace ningún ensayo antes; cada ensayo gasta un número |
| 14 | Idioma y moneda | `Tenant`: idioma `es`, zona America/Puerto_Rico, USD, símbolo `$` delante, 2 decimales | Fijar español en el navegador de la grabación (`locale: 'es-PR'` y `localStorage teikem.lang = es`) |

**Importes esperados** (por confirmar en el ensayo):
- Con OFERTA1 y 10 cajas de NECH-1002: subtotal $91.94 (41.94 + 50.00), IVU $0.00, envío sin cargo, total **$91.94**. Con el envío del script 08: $12.00 («Por qué: Cargo de envío») y total $103.94.
- Con los datos de hoy (KIT-001 + 10 NECH-1002): subtotal $58.50, IVU $0.98 (11.5 % de $8.50), total **$59.48**.

**Para que «Enviar pedido» termine bien a la primera:** contraseña correcta del usuario que graba (una mala cuenta para el bloqueo de la cuenta); el kit en una oferta publicada y vigente; inventario disponible para las dos líneas (hoy sobra; si faltara, el asistente envía aceptando backorder cuando la línea ya estaba marcada, y solo sale el diálogo de faltante si el inventario cambió entre la captura y el envío); con «Entrega», un destino escogido; almacén de archivos configurado (firma). El crédito no interviene (ningún cliente tiene límite) y ningún cliente es prospecto.

**En qué queda la orden:** estatus `REQUESTED` («Solicitada»), con `SubmittedWithAuth = PASSWORD`, inventario reservado (del kit se reservan sus piezas: 6 tirillas y 1 monitor por kit) y ya visible para el almacén.

**Escritorio frente a tableta:**
- Más de 900 px de ancho (escritorio o tableta acostada): menú lateral visible; sin preferencia guardada, «Nueva orden» abre el formulario; el carrito y «Atrás / Siguiente» van debajo de la lista (hay que desplazar la página); la lista de productos tiene desplazamiento propio (hasta la mitad del alto de la ventana).
- De 721 a 900 px (tableta de pie): el menú pasa a un cajón que se abre con «Abrir menú»; sin preferencia, «Nueva orden» abre el asistente; el pie sigue debajo de la lista.
- 720 px o menos (celular): el carrito y los botones quedan fijos abajo; con 480 px o menos, la barra de pasos solo muestra el nombre del paso actual y cada producto se apila en dos renglones.
- El recorrido 228 graba el asistente en escritorio a 1280 px y lo dibuja con el ratón; funciona igual.

## 5. Riesgos al grabar (validaciones, ventanas, avisos, esperas, cosas que se ven feas, datos sensibles en pantalla)

1. **Lista de clientes reales a la vista.** Al abrir el asistente el buscador toma el foco y despliega los primeros 30 clientes por nombre (farmacias y centros de salud reales de Solutions). Es la cartera de clientes de la compañía.
2. **Campo MFA vacío.** Con los usuarios de hoy (`TwoFactorEnabled = 1` sin factor inscrito) la ventana de contraseña muestra «Código de verificación (MFA)»; el servidor no lo exige (no hay factor confirmado), así que se dejaría vacío a la vista. Se evita grabando con un usuario que nunca inscribió MFA.
3. **El kit de hoy se ve mal.** Nombre «kIT DE mONITOR gRATIS», pestaña «Sin marca», $8.50 y con IVU. Ver requisito 5.
4. **Paso 3 sin dirección física.** Sale el texto «El cliente no tiene dirección física activa: escoja otra dirección.» y, si se pulsa «Siguiente» sin escoger, el error «Escoja a dónde se entrega el pedido o márquelo como recogido.».
5. **Contraseña.** Va por variable de entorno, nunca en el guion ni en el repositorio; no pulsar el ojito. Con mayúsculas activadas sale el aviso «Las mayúsculas están activadas.».
6. **Botón rojo «Borrar orden»** arriba durante todo el paso 2 (el borrador todavía no tiene productos guardados).
7. **Textos literales poco elegantes**: «Carrito: 2 producto(s), 11 unidad(es)»; códigos de cliente generados («Núm. FARMACI-18»); nombres de producto en mayúsculas de QuickBooks.
8. **Fotos.** 7 productos sin foto dejan el cuadrito vacío (dos de ellos en PRODIGY: 53310 y 53350). El hueco de la tarjeta de oferta sin foto usa la clase `empty`, que hereda un alto mínimo de 220 px (el mismo problema que el Lote 240 corrigió en otra pantalla): con el kit actual no debería verse porque toma la foto de las tirillas, pero hay que mirarlo en la captura.
9. **Desplazamiento en escritorio.** «Siguiente» queda bajo la lista; conviene desplazar `.stage` con suavidad en vez de saltar.
10. **Kemi y la cinta de ambiente.** La mascota flota abajo a la derecha, cerca de «Siguiente», y saluda al entrar (se puede dormir con `teikem.kemi.asleep.<usuario>` y marcar el saludo con `teikem.kemi.greeted`). La cinta roja de ambiente solo sale si el API informa una etiqueta en `/health`.
11. **Reloj «en vivo»** de la cabecera: muestra la hora real de la grabación con segundos.
12. **Números que cambian entre tomas**: OV consecutivo y «Disponible». La narración no debe decir cifras exactas salvo que se restaure la base antes de la toma final.
13. **Esperas**: buscador de clientes (300 ms), carga de la ficha del cliente antes de ver la forma de pago, catálogo y disponible del paso 2, «Calculando el envío…» al cambiar el destino, y hasta unos segundos en «Enviando…».
14. **Buscar por teléfono no funciona.** La ayuda dice «Número, nombre o teléfono», pero el servidor busca solo en código, nombre y razón social. No demostrarlo ni narrarlo.
15. **Dos botones «Enviar pedido»** a la vez (pie del asistente y ventana): acotar siempre al diálogo.
16. **Ancho máximo del asistente: 980 px.** En una grabación de 1920 px quedaría un hueco a la derecha; a 1280-1366 px llena bien (ver captura `l228-paso2-productos.png`).

## 6. Datos para la narración (hechos verificados, en frases cortas y en lenguaje de usuario)

- La venta se hace en Contabilidad, Ventas. El botón Con asistente abre el pedido guiado.
- El pedido se arma en cuatro pasos: Cliente, Productos, Entrega y Autorización.
- Se busca el cliente por su nombre o su número. La forma de pago ya viene de su ficha; si se cambia, la oficina ve la marca.
- Cada vez que se pulsa Siguiente, el pedido queda guardado como borrador con su número. Si se cierra el navegador, se retoma donde iba.
- Los productos están por marca. Las ofertas vigentes salen arriba, como tarjetas con foto.
- El kit es una oferta: seis cajas de tirillas y un monitor. El sistema dice cuántos kits se pueden armar con lo que hay en el almacén y qué pieza limita.
- Cada producto dice cuánto hay disponible. Si se pide más de lo que hay, no se bloquea: queda marcado como backorder.
- El vendedor no escribe precios: salen de la lista de precios o de la oferta, y se cobra el más bajo.
- El carrito, abajo, lleva la cuenta de productos, unidades y subtotal.
- En Entrega se escoge si se le lleva al cliente o si lo recoge. El IVU y el cargo de envío los calcula el sistema.
- En Autorización, quien recibe en el negocio escribe su nombre y firma en la pantalla. La firma es opcional, salvo que la ficha del cliente la exija.
- Para enviar, el sistema pide la contraseña del vendedor: es su firma.
- Al enviar, el sistema valida el inventario y el crédito y reserva el inventario. La orden queda Solicitada y el almacén ya la ve.
- El mismo asistente funciona en la tableta y en el celular.

Lo que NO se debe decir: que al enviar se manda un correo a la oficina (no hay ninguna regla de aviso en Solutions; la oficina trabaja desde la pestaña QuickBooks, escena 6); que se busca por teléfono; cifras de disponible o el número exacto de la orden.

## 7. Duración estimada en pantalla (segundos) y qué se puede recortar si hay que acortar

| Tramo | Segundos |
|---|---|
| Menú → Ventas → «Con asistente» | 10 |
| Paso 1 (buscar, escoger, forma de pago, Siguiente) | 18 |
| Paso 2 (oferta y kit, guantes, carrito, Siguiente) | 32 |
| Paso 3 (entrega, destino, resumen, Siguiente) | 16 |
| Paso 4 (nombre, firma, Enviar pedido) | 16 |
| Contraseña y «Pedido enviado» | 12 |
| «Ver en Órdenes de venta»: la orden Solicitada | 10 |
| **Total** | **≈ 115** |

Versión corta (≈ 75 s): quitar «Pedido urgente» y la nota, la demostración de backorder, abrir el carrito, la ampliación de foto y el «Cambiar» de una pieza; y cerrar en «Pedido enviado» sin abrir la orden (la escena 5 la enseña desde el almacén).

## 8. Dudas y decisiones para Luis

1. **¿Qué kit sale en el video?** Hoy solo existe KIT-001 «kIT DE mONITOR gRATIS» a $8.50 en «OFERTA DEL DIA», sin marca. Recomiendo correr el script 02 en la base de la grabación (kit OFERTA1, PRODIGY, $41.94), desactivar «OFERTA DEL DIA» y ponerle foto.
2. **¿Cliente real o de demostración?** Con un cliente real se ven su nombre y, al abrir el buscador, otros 30 clientes reales. Recomiendo un cliente de demostración con dirección física y 30 días; si el video es solo interno, sirve una farmacia real de 30 días.
3. **¿Entrega o recogido?** Ningún cliente de Solutions tiene dirección física: con «Entrega» hay que escoger el consignatario a mano o ponerle dirección física al cliente de la demostración. Recomiendo «Entrega» con la dirección física puesta; hay que cuadrarlo con la escena 5 (Solutions no tiene encendido ningún módulo de transporte ni el de compañías enlazadas, así que la entrega del despacho hay que decidirla allá).
4. **¿Se enseña cargo de envío e IVU?** Hoy el envío sale «Sin cargo» y, con kit exento y guantes, el IVU sale $0.00. Recomiendo configurar el envío real de Solutions ($12, gratis desde $120) solo si esa es la regla vigente, y dejar kit + guantes diciendo que son exentos; si prefiere ver un IVU, cambiar los guantes por un producto gravable.
5. **¿Con qué usuario se graba?** Los usuarios actuales harían aparecer un campo MFA vacío en la ventana de contraseña. Recomiendo un usuario nuevo de demostración (administrador de Solutions, sin MFA) cuyo nombre es el que saldrá en «Pidió».
6. **¿Se entra con «Con asistente» o directo con «Nueva orden»?** Recomiendo dejar el modo en formulario antes de la toma y pulsar «Con asistente», para explicar los dos modos en una frase.
7. **¿Escena en tamaño escritorio o enmarcada como tableta?** Recomiendo el mismo tamaño de escritorio del resto del video (1280-1366 px de ancho) y decir que funciona igual en tableta y celular.
8. **¿Pedido urgente y nota?** Recomiendo no marcarlo (menos pasos); solo si la escena 5 quiere enseñar que el almacén lo ve primero.
9. **El kit creado desde la pantalla queda sin marca** (la ventana «Crear un kit nuevo» no tiene ese campo) y por eso cae en la pestaña «Sin marca» del asistente. ¿Se corrige (que herede la marca de su primera pieza, como ya hereda categoría e impuesto) antes de grabar? Recomiendo no tocar código para el video y usar el script 02; dejarlo como arreglo aparte.
10. **KIT-001 cobra IVU y sus piezas son exentas** (la orden OV-00002 lo cobró al 11.5 %). ¿Es correcto? Recomiendo dejarlo exento como sus piezas.
11. **La ayuda del buscador promete «teléfono»** y el servidor no busca por teléfono. ¿Se corrige el texto o la búsqueda? Recomiendo corregir el texto; no afecta la grabación.
12. **Dependencia con la escena 6.** El manual dice que a QuickBooks pasan las facturas de venta emitidas, y Solutions tiene el módulo de Facturación apagado. Lo debe confirmar quien investiga la escena 6, pero puede hacer falta un paso de facturación entre el despacho y QuickBooks.
13. **Coherencia con las escenas 2 y 3.** En `Solutions Purchases.CSV` (83 renglones, 20 documentos, 6 proveedores) las piezas del kit vienen en los documentos «PDC…» (p. ej. el recibo PDC-05122026: 52800 × 10,000 y 070120 × 3,500) y los guantes en la factura PDC-05282026 (NECH 1001 a 1004). Recomiendo importar y recibir esos dos para que lo vendido sea lo recién recibido.

No pude confirmar sin levantar la aplicación: cómo cabe cada paso en la ventana elegida, el aspecto real de la tarjeta del kit con los datos de Solutions, los importes exactos del resumen y que las fotos carguen en el API de la grabación.

## 9. Fuentes (archivo:línea)

- Rutas y menú: `web-app/src/app/routes.tsx:394-395`; `web-app/src/app/AppShell.tsx:255-288`; `web-app/src/kernel/i18n/es.json:238, 368-371`.
- Entrada y modos: `web-app/src/features/sales/SalesOrdersScreen.tsx:325-349`; `web-app/src/features/sales/entryMode.ts:13-19, 42-60`; `docs/manual/18-ordenes-de-venta.md:591-612`; `docs/lote238-decisiones.md:8-9, 23-27`.
- Asistente: `web-app/src/features/sales/wizard/NewOrderScreen.tsx:58, 92-97, 150-164, 180-194, 197-272, 287-303, 330-353, 359-374, 413-440`; `ClientStep.tsx:50-66, 88-116, 118-132, 134-173`; `ProductsStep.tsx:80-115, 121-136, 158-177, 181-229, 239-287`; `DeliveryStep.tsx:34-36, 46-86, 102-133`; `AuthorizationStep.tsx:28-55, 60-136`; `wizardRules.ts:245-273, 295-316, 340-352, 425-435`; `wizardApi.ts:24-44, 56-71`; `wizard.css:2, 57-58, 65, 104-132`.
- Disponible, kit y piezas: `web-app/src/features/sales/StockInfo.tsx:27-91`; `stockRules.ts:138-151, 198-205`; `KitLineBlock.tsx:53-90, 133-158`.
- Firma, contraseña, fotos: `web-app/src/kernel/ui/SignaturePad.tsx:38-62, 80-104`; `PasswordBox.tsx:26-63`; `ZoomPhoto.tsx:29-39`; `web-app/src/features/warehouse/StoredImage.tsx:12-22`; `web-app/src/styles/base.css:143, 358-364, 377`.
- Diálogo de faltante y ventana de la orden: `web-app/src/features/sales/OrderModal.tsx:56-155, 637-859`.
- Textos: `web-app/src/kernel/i18n/es.json:2-9, 27-29, 3142-3148, 4392-4397, 10435-10454, 10555-10561, 10607-10651, 10735-10748, 10808-10968`; idioma: `web-app/src/kernel/i18n/i18n.ts:16-27`.
- Servidor: `src/Teikem.Api/Controllers/SalesOrdersController.cs:52-57, 138-161`; `src/Teikem.Infrastructure/Services/SalesDraftService.cs:73-120`; `SalesOrderService.Request.cs:58-145, 335-419, 455-467`; `SalesOrderService.Notifications.cs:18-48`; `SalesOrderService.Terms.cs:99-120`; `SalesCatalogService.cs:108-177`; `OfferService.cs:398-423`; `ClientService.cs:30-41, 183-197`; `AuthService.cs:149-193, 606-640, 791-795`; `UserAdminService.cs:285-286`; `src/Teikem.Domain/Sales/ShippingRules.cs:71-83`; `SalesDraftSteps.cs:24-59`; `SalesOrderExtras.cs:40-41`; `src/Teikem.Domain/Orders/CreditRules.cs:22-28`; `src/Teikem.Domain/Tenancy/Tenant.cs:116-126`; `src/Teikem.Domain/Wms/PickBatchRules.cs:267-270`; `src/Teikem.Domain/Files/StoredFile.cs:60`.
- Kit sin marca desde la pantalla: `web-app/src/features/warehouse/OfferKitModal.tsx:320-360`; `web-app/src/features/warehouse/offerRules.ts:112-114`; `src/Teikem.Infrastructure/Contracts/OfferContracts.cs:29-30`.
- Manual: `docs/manual/48-pedido-guiado.md:7, 13-18, 22-30, 37-54, 61-67, 72-79, 101-108, 127-159, 176-186`; `docs/manual/47-precios-y-ofertas.md:33-49, 141-155, 217-228, 388`; `docs/manual/18-ordenes-de-venta.md:77-85, 199-209, 250-282, 301-323, 418-439, 449-476, 571-583, 614-626`; `docs/manual/49-quickbooks.md:5, 19-23`.
- Recorridos: `web-app/e2e/lote228-pedido-vendedores.spec.ts:312-316, 350-410`; `web-app/e2e/lote238-modo-captura-y-recoleccion.spec.ts:79-115`; `web-app/e2e/lote236-carrito-fijo.spec.ts:203-227, 281-296`; `web-app/e2e/staffHelpers.ts:52-63`; `web-app/playwright.config.ts:13-19, 30-33`.
- Scripts y entorno: `scripts/solutions-pedido-vendedores/LEEME.md:28-41`; `scripts/solutions-pedido-vendedores/02-kit-oferta1.sql:29-41, 110-167`; `scripts/api-para-pruebas.ps1:8-34`; `docs/lote239-decisiones.md:10-11, 18`; `docs/lote240-decisiones.md:8-12, 29`.
- Capturas de referencia: `docs/manual/frontend/img/l228-paso2-productos.png`, `l238-ordenes-botones.png`.
- Base local `Teikem` (compañía 2), tablas consultadas: `Tenant`, `TenantModule`, `TenantTaxSetting`, `TaxRate`, `TaxRateComponent`, `Offer`, `OfferItem`, `OfferClient`, `Product`, `ProductKitComponent`, `PriceList`, `PriceListItem`, `StockBalance`, `Warehouse`, `WarehouseZone`, `Client`, `Location`, `SalesOrder`, `SalesOrderLine`, `NumberSequence`, `NotificationRule`, `UserTenant`, `UserRole`, `Role`, `RolePermission`, `UserPreference`, `UserMfaFactor`, `AspNetUsers` (solo banderas), `StoredFile`, `LookupCode`, `StatusCode`.
- Archivo de QuickBooks: `F:/Download/Solutions Purchases.CSV` (leído como datos).
