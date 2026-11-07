# Pedido de los vendedores de Solutions: del formulario Formsite a Teikem (análisis, 2026-11-06)

Pedido de Luis (2026-11-06): los vendedores de Advance Solutions someten hoy las órdenes de venta con un formulario de Formsite
(`https://fs21.formsite.com/Z2d4VF/yp6rtvbvaj/index`, título «AdvanceMedical»). Se va a retirar y sustituir por Teikem. La meta no es
copiarlo igual, sino que a los vendedores se les haga fácil cambiar. Este documento estudia el formulario completo (campos, reglas de
visibilidad y precios leídos del formulario el 2026-11-06), lo compara con lo que Teikem ya tiene y propone cómo hacerlo. **No se
construyó nada.** Al final van las decisiones que le tocan a Luis.

## 1. Qué tiene el formulario, paso a paso

### 1.1 Cabecera
| Campo del formulario | Obligatorio | Nota |
|---|---|---|
| Fecha | sí | Se prellena con hoy. |
| Nombre del Negocio | sí | El cliente que compra. |
| Teléfono (10 dígitos, sin espacios ni guiones) + Extensión | sí / no | |
| Customer # | no | El número de cliente (QuickBooks). |
| Urgent Note (Sí / No) | sí | Con «Sí» aparece un cuadro de texto «Urgent Note». |

### 1.2 ¿Nuevo cliente? (Nuevo / Existente) — lo que se abre con cada respuesta
- **Nuevo cliente** abre: Registro de Comerciante «SURI» (texto, obligatorio), correo electrónico (obligatorio), Nombre del Negocio / DBA,
  Nombre del contacto (obligatorio), Teléfono #2, Dirección física (dos líneas, obligatoria), Código postal (obligatorio), Pueblo (lista de
  los 78 pueblos de PR, obligatorio), Dirección postal (dos líneas), Estado (solo «PR»), y **Opciones de pago** (ver 1.3).
- **Existente** abre: «¿Cambio de dirección?» (Sí / No; con Sí se abren los mismos campos de DBA, contacto, dirección física, código
  postal y estado) y «¿Cambio en opciones de pago?» (Sí / No; con Sí se abre Opciones de pago).
- Dos adjuntos: **Foto del Registro de Comerciante** y **Foto del Certificado de Revendedor**. La regla de visibilidad del formulario los
  muestra cuando «¿Nuevo cliente?» no tiene respuesta (valor −1): en la práctica se ven al abrir el formulario y se esconden al
  contestar. Parece un error del formulario; la intención evidente es pedirlos al cliente nuevo.

### 1.3 Opciones de pago
- COD → Cheque / Cash / ACH.
- CREDIT → 15 días / 20 días / 30 días / 45 días / «Vendedor recoge cheque».
- CONSIGNACIÓN.

### 1.4 Vendedor
«Rep» (lista: Carlos Planas, Neda L. Ramos, Marilyn Rodríguez, Santa Vélez, Cindy Olivas, Indhyra Ortiz, Radames Santiago) y «Email»
(lista con sus correos; también `almacen@advancesolutionspr.com`). El vendedor se identifica a sí mismo en cada pedido.

### 1.5 Marcas y productos (el corazón del formulario)
«Marcas» es una lista de casillas (se pueden marcar varias): **Prodigy, Padimont Holdings, Cartones, Global Personal Care, American Red
Cross, MedPride**. Cada marca marcada abre su «Client Type»:

| Marca | Client Type que ofrece | Qué abre cada uno |
|---|---|---|
| Prodigy | Individual · Ofertas · Custom Pricing | Individual: lista de 4 productos con precio y cantidad. Ofertas: **OFERTA1 — 6 cajas de tirillas No Coding + 1 monitor, $41.94** (cantidad). Custom Pricing: casillas Monitor / Tirillas; cada una abre «cantidad» y «precio custom». |
| Padimont Holdings | Custom Pricing · Individual | Individual: 2 underpads ($28.99 c/u). Custom Pricing: casilla «Guantes» → cantidad y precio custom de las 4 tallas (NECH 1001–1004). **Los guantes solo se venden con precio custom.** |
| Cartones | Individual · Custom Pricing | Individual: 3 cajas con precio. Custom: cantidad y precio custom por caja. |
| Global Personal Care | Individual | 7 productos. |
| American Red Cross | Individual | 3 productos + 4 soportes ($1.93) + hot/cold pack ($1.93). |
| MedPride | Individual | 7 productos + 2 petroleum jelly. |

Lista de precios tal como la tiene el formulario (2026-11-06):

| SKU | Producto | Precio |
|---|---|---|
| 070120 | Prodigy Auto Code Monitor | $7.99 |
| 52800 | Prodigy Test Strips 50 | $6.99 |
| 24081028 | Prodigy Twist Top Lancets 28g | $1.25 |
| 24083225 | Prodigy Lancing Device | $2.50 |
| OFERTA1 | 6 cajas de tirillas No Coding + 1 monitor | $41.94 |
| PD-23-36-45gms15/10 · PD-23-36-45gms3/50 | Padimont underpads | $28.99 |
| NECH 1001–1004 | Padimont gloves S/M/L/XL | solo custom |
| 979 · 101010 · 121212 | Cajas 9×7×9 · 10×10×10 · 12×12×12 | $0.95 · $1.05 · $1.46 |
| 10152-5 · 00614-1 · 00052-1 · 00804-6 | Global wipes / cotton | $1.09 |
| 00802-2 · YD869999 · FOB-AB885961 | Global flushable wipes · Yardley · A.Belle sanitizer | $1.29 · $1.93 · $23.16 |
| ARC609517 · ARC609513 · ARC573775 | ARC bandages · antiseptic spray · peroxide | $1.41 · $4.50 · $1.62 |
| ARC609522/29/30 · ARC521387 · ARC521386 · ARC609521 | ARC supports · hot pack · cold pack | $1.93 |
| MPR-30453 · 30244 · 40310 · 30221 · 30212 · 30503 · 49002 | MedPride | $1.00 · $1.25 · $1.61 · $1.48 · $1.45 · $2.04 · $1.53 |
| MPR-10105 · MPR-10103 | Petroleum jelly 13 oz · 8 oz | $2.78 · $1.76 |

Los SKU del formulario no coinciden letra por letra con los de Teikem: `24081028` es `24-081028`, `NECH 1001` es `NECH-1001`,
`PD-23-36-45gms15/10` es `PD-23-36/15/10`. Los precios sí coinciden con el precio de venta cargado desde QuickBooks (p. ej. $1.09).

### 1.6 Envío, total y autorización
- **Cargo de envío**: «gratis para pedidos superiores a $120; todo lo demás se cobra $12.00 después del IVU». El vendedor lo **escoge a
  mano** (Envío $12.00 / Envío gratis $0.00): el formulario no lo calcula.
- **¿Entrega o recogido?** (casillas ENTREGA / RECOGIDO).
- **Total del pedido** con un botón «Calculate» (suma cantidad × precio + envío).
- **Nombre y apellido de quien autoriza** y **firma** (dibujada en pantalla, obligatoria).

## 2. Qué tiene Teikem ya (y sirve tal cual)

| Del formulario | En Teikem hoy | Estado |
|---|---|---|
| Orden con cliente, productos, cantidad, precio, total, IVU | Orden de venta `OV-#####` (módulo `SALES_ORDERS`, cap. 18 del manual): borrador → solicitar (reserva inventario) → despacho por almacén | **Listo** |
| Cliente existente por número / nombre | `Client` (código = Customer # de QuickBooks, campo personalizado «Código QuickBooks»), buscador | **Listo** |
| Ofertas (OFERTA1) | **Kits** (Lote 68/69): un producto tipo KIT con componentes y su propio precio; se vende como una línea y el almacén despacha los componentes | **Listo**: OFERTA1 = kit con 6 × 52800 + 1 × 070120 a $41.94. Es dato, no código. |
| Custom pricing | El precio de la línea se puede cambiar con el permiso `sales.price` (hoy solo lo tiene el Admin de compañía) | **A medias**: existe la puerta, falta decidir quién la cruza (ver §4). |
| Opciones de pago | `Client.PaymentTermLookupId` (catálogo PaymentTerm: 15/20/30/45 días, COD…), límite de crédito y comprobación de crédito al solicitar | **A medias**: está en el cliente, no en la orden; falta cheque/cash/ACH y consignación. |
| Entrega / recogido | La orden lleva consignatario solo si habrá entrega; el despacho crea la orden de transporte | **Listo por dentro**; falta hacerlo explícito en pantalla. |
| Teléfono 10 dígitos | Los teléfonos de Teikem ya se guardan solo con dígitos | **Listo** |
| Pueblo, dirección física y postal | `Location` del cliente (CORPORATE = física, BILLING = postal) con ciudad y código postal | **Listo** |
| Vendedor | El usuario que entra (cada vendedor con su usuario); además el cliente tiene el campo personalizado «Rep» | **Listo** (no hace falta escoger «Rep»: lo dice el inicio de sesión). |
| Urgent note | Notas de la orden (500 caracteres) | **A medias**: falta la marca «urgente» que el almacén vea de un vistazo. |
| Firma de quien autoriza | Hay firmas en la entrega del chofer (`StoredFile` SIGNATURE), no en la orden de venta | **Falta** |
| Cliente nuevo desde el pedido (SURI, certificado de revendedor, fotos) | El alta de cliente es otra pantalla y exige `clients.create`; el rol Ventas solo tiene `clients.read`; no hay campo SURI ni adjuntos del cliente | **Falta** |
| Cargo de envío ($12 / gratis desde $120) | No existe en la orden de venta | **Falta** |
| Catálogo por marca con toda la lista a la vista | El selector de producto de la orden busca uno por uno; `Product.Brand` existe | **Falta** la presentación |

## 3. Propuesta: «Pedido» (modo vendedor de la orden de venta)

No una pantalla aparte con otra base: **la misma orden de venta**, con una entrada guiada pensada para el celular del vendedor, que
produce una `OV-#####` normal. Así el almacén, los backorders, el despacho, las devoluciones y el análisis siguen iguales. Cuatro pasos,
en el orden en que el vendedor ya está acostumbrado:

1. **Cliente.** Buscar por número, nombre o teléfono. Botón «Cliente nuevo» con solo lo que pide el formulario (negocio, DBA, contacto,
   teléfonos, correo, SURI, dirección física, pueblo, código postal, dirección postal, forma de pago, fotos del registro y del certificado).
   Casilla **Urgente** + nota. Forma de pago **prellenada con la del cliente**; si el vendedor la cambia, la orden lo marca para la oficina.
2. **Productos.** Las **marcas como pestañas** (Prodigy, Padimont, Cartones, Global, ARC, MedPride: salen de `Product.Brand`). Cada
   pestaña muestra **toda la lista de la marca** con su precio y una casilla de cantidad (como la matriz del formulario), las **ofertas**
   (kits) en su propio bloque arriba, y, si el vendedor puede, un precio especial por línea. Lo tecleado se acumula en el carrito de abajo.
3. **Entrega.** «Entrega» (dirección del cliente por defecto; se puede escoger otra) o «Recogido». **Cargo de envío calculado solo**
   según la regla de la compañía ($12 si el total con IVU no llega a $120, configurable), con el total a la vista.
4. **Autorización.** Nombre de quien autoriza y firma en pantalla. «Enviar pedido» = solicitar la orden (valida inventario y crédito,
   reserva); si falta algo sale el diálogo de faltante que ya existe.

Lo que la pantalla actual de Órdenes de venta (Operación → Órdenes de venta) sigue haciendo igual: ver, editar, cancelar, backorders.

### 3.1 Lo que hay que construir (por lote)

**Lote A — datos de la orden (backend + SQL idempotente en `logistica-db-update.sql`).**
- `SalesOrder`: `IsUrgent`, `DeliveryMode` (DELIVERY / PICKUP), `PaymentMethod` y `PaymentTerm` de la orden (prellenados del cliente;
  catálogos `SalesPaymentMethod` COD_CHECK / COD_CASH / COD_ACH / CREDIT / CONSIGNMENT y el `PaymentTerm` existente), `PaymentChanged`
  (bandera «cambió la forma de pago»), `ShippingCharge`, `AuthorizedByName`, `SignatureFileId` (StoredFile SIGNATURE).
- Regla de envío por compañía (Ajustes → Ventas): monto del cargo, umbral y si se mide antes o después del IVU. Se aplica al solicitar.
- El cargo de envío en el total y en la factura (línea de servicio, no inventario).
- Permiso nuevo `sales.special_price` **o** reusar `sales.price` (decisión §4). Nuevo permiso ⇒ propagarlo a los roles ya clonados.
- Alta de cliente desde Ventas: campo `MerchantRegistration` (SURI) y adjuntos del cliente (registro de comerciante, certificado de
  revendedor) como `StoredFile` KindDocument; permiso `clients.create_basic` para el rol Ventas (alta mínima, sin contrato).

**Lote B — pantalla guiada en `web-app` (responsive, celular primero).** Los 4 pasos, catálogo por marca, kits como ofertas, firma con el
mismo componente del chofer, tema claro/oscuro y es/en como todo lo demás. Se abre desde Órdenes de venta («Nuevo pedido») y por una ruta
directa que el vendedor guarda en su celular.

**Lote C — datos.** Kit OFERTA1; marcas en los productos de Solutions (ya vienen de QuickBooks: GLOBAL, PRODIGY, PADIMONT, DEPOT para
cartones, AMERICAN RED CROSS, MEDPRIDE); un usuario por vendedor (7) con el rol Ventas; los clientes con su forma de pago.

**Lote D — manual + FAQ** (capítulo 18 y uno de pantallas) y retiro del formulario.

### 3.2 Lo que NO conviene imitar
- Escoger el vendedor y su correo a mano: lo da el inicio de sesión (y queda en la bitácora).
- Escoger «envío $12 / gratis» a mano: se calcula; el vendedor solo lo ve.
- «Client Type» por marca (Individual / Ofertas / Custom): en Teikem la lista de la marca, las ofertas y el precio especial están
  juntos; no hay que elegir un «tipo» antes de ver los productos.
- Cambios de dirección o de pago escondidos en un pedido: se cambian en el cliente (si el vendedor puede) o quedan marcados para que
  la oficina los aplique; nunca se pierden en el texto de una nota.

## 4. Decisiones para Luis

1. **Precio especial (custom pricing).** ¿Los vendedores pueden escribir el precio (hoy `sales.price` es solo del Admin)? Opciones:
   (a) darles `sales.price` a los 7; (b) permiso nuevo solo para los productos «de precio negociado» (guantes, cajas, monitor,
   tirillas) con tope mínimo; (c) **lista de precios por cliente** que se aplica sola y no deja al vendedor teclear nada (un lote más).
   Recomiendo (b) ahora y (c) después.
2. **Cliente nuevo desde el pedido.** ¿Se crea de una vez como cliente activo, o nace «pendiente de aprobación» y la oficina lo
   confirma (SURI, certificado) antes de que la orden se pueda despachar? Recomiendo pendiente.
3. **Cargo de envío.** Confirmar la regla: $12.00 si el total **después del IVU** es menor de $120.00; gratis desde $120.00; solo en
   ENTREGA (en RECOGIDO no aplica). ¿Y en la factura va como línea aparte?
4. **Forma de pago en la orden.** ¿Se guarda en la orden (lo que se acordó para ese pedido) además de en el cliente? Recomiendo sí,
   prellenada y con la marca «cambió».
5. **Consignación.** ¿Qué significa operativamente (inventario en el cliente que se factura al venderse)? Hoy Teikem no la maneja;
   si solo es una forma de pago, basta el catálogo.
6. **Firma.** ¿Es obligatoria para solicitar la orden (como en el formulario) o solo para la entrega?
7. **Urgente.** ¿Qué hace el almacén con un pedido urgente: va primero en la lista de despacho, aviso al almacén, o solo se ve?
8. **Pedidos por teléfono/oficina.** `almacen@advancesolutionspr.com` está en la lista de vendedores: ¿la oficina también captura pedidos?
   Entonces la pantalla guiada también les sirve a ellos.
9. **SKU distintos entre el formulario y Teikem** (`24081028` / `24-081028`, `NECH 1001` / `NECH-1001`, underpads): se queda el de
   Teikem; solo conviene saberlo para los vendedores que se saben los números de memoria.

## 5. Dictado de Luis (2026-11-06, primera ronda) — lo que ya quedó decidido

Este documento es la bitácora del análisis: cada ronda de conversación se anota aquí.

1. **Precio especial = lista de precios, no precios tecleados.** Una **lista de precios** con productos, kits y ofertas, que **la establece el
   supervisor de ventas**. Es **por temporada** (tiene vigencia: no son precios fijos del producto). Las ofertas llevan **imagen** (la foto
   es la promoción) y los productos individuales también pueden llevar foto. La oferta es **para el cliente que la quiera**: el vendedor la
   escoge en el pedido, y puede combinar oferta y productos sueltos (también kits). **Por definir:** cómo se registra la venta de un kit u
   oferta (si la venta se asienta por el combo o por cada componente). Instrucción: inventar algo razonable y consultarlo.
2. **Cliente nuevo.** Si existe un estatus «pendiente» en los clientes, debe usarse (hoy el dominio tiene PROSPECT → ACTIVE → SUSPENDED;
   PROSPECT puede hacer de «pendiente»). Lo importante no es el nombre del estatus sino **qué implica**: qué puede y qué no puede pasar con
   ese cliente mientras está pendiente. Debe haber una **verificación** en la oficina (crédito y supervisor de ventas) con **constancia** de
   que ocurrió, y **al menos una notificación** cuando entra un cliente nuevo. El resto, propuesta del analista.
3. **Forma de pago guardada en cada orden: sí.** Los clientes sí manejan **crédito** (Teikem ya tiene límite y plazo por cliente).
4. **Consignación** = «te lo doy, tú lo vendes y me lo pagas en la medida que lo vendes».
5. **Firma para someter: en la app móvil.** La «firma» del vendedor es su **contraseña o la huella / Face ID del teléfono** (la mayoría
   tiene iPhone; debe funcionar en ambos). Con instalar la app de Teikem y darle permisos debería bastar. Pregunta de Luis: ¿hace falta una
   app exclusiva para esto? (respuesta en §6).
6. **Urgente:** el **almacén y el transporte** deben tener constancia. El transporte de Solutions lo hace **Advance Logistics**, otra
   compañía que también vive en Teikem.
7. **La oficina también captura pedidos: sí.**
8. **Las tres compañías son del mismo dueño:** *Advance Logistics* (transporte), *Advance Depot* (almacenaje / empaque) y *Advance
   Solutions* (vende a farmacias y despacha a través de Advance Logistics). Hay que pensarlo con esa estructura.

## 6. Respuestas del analista a la primera ronda y puntos nuevos

**La pregunta del envío, dicha más simple.** El formulario cobra $12.00 de envío si el pedido es menor de $120 y nada si es mayor; hoy
el vendedor lo marca a mano. La pregunta era solo: ¿Teikem lo **calcula solo** con esa regla ($120 medido después del IVU, y solo cuando
es entrega, no recogido) y lo pone como una línea más del pedido y de la factura? Si la respuesta es «sí, así», no hay nada más que decidir.

**¿Una app exclusiva? No.** La app móvil del personal (`app-movil`, Expo, iOS y Android) ya existe, ya entra con contraseña y MFA y ya
tiene bloqueo con **Face ID / huella** (`LockScreen`, permiso `faceIDPermission`). Lo que falta es una **sección «Pedidos»** dentro de ella
con los 4 pasos de §3; «someter» exige volver a autenticarse con huella/Face ID o contraseña, y eso queda registrado como la firma del
vendedor (quién, cuándo, desde qué teléfono). La misma pantalla, responsive, va en la web para la oficina. La firma dibujada de quien
autoriza (el cliente) es otra cosa: queda **opcional** salvo que Luis la quiera obligatoria.

**Kits y ofertas: cómo se registra la venta (propuesta razonable, a consultar).**
- Una **oferta** es un **kit con vigencia y foto**: producto tipo KIT (ya existe) + «desde / hasta» + imagen + «es promoción». Fuera de
  vigencia no se puede vender, pero las órdenes ya hechas no cambian.
- La **venta se asienta por el kit** (una línea: OFERTA1 × n a $41.94) y **el inventario y el costo salen por componente** (ya es así en
  Teikem: el kit no tiene existencia, se reservan y despachan sus componentes). La factura muestra el kit y, debajo, sus componentes
  informativos. Para el análisis de ventas por producto, cada componente recibe su parte del precio del kit **a prorrata del precio de
  lista** de cada componente (6 tirillas a $6.99 = $41.94 y 1 monitor a $7.99 → total de lista $49.93; el kit vale $41.94 → cada
  componente lleva el 84%). Así «cuántas tirillas vendí y a cuánto» sale bien aunque se vendan en combo.
- La **lista de precios** es una entidad nueva: nombre, vigencia, estado, renglones (producto o kit → precio, foto opcional), y a quién
  aplica (todos los clientes, o una lista por cliente/grupo para los precios negociados como los guantes). Al pedir, el precio sale de la
  lista vigente; el vendedor **no teclea precios**. `sales.price` se queda como está (solo el admin, para excepciones).

**Cliente nuevo «pendiente»: qué implica (propuesta).** Nace como **PROSPECT** con los datos y adjuntos del pedido. Mientras esté
pendiente: se le pueden armar y solicitar pedidos (se reserva inventario), pero **no se despacha** ni se factura. La oficina recibe un
**aviso** («Cliente nuevo pendiente de verificación: X, vendedor Y») y lo aprueba (ACTIVE, con su límite de crédito y plazo) o lo rechaza
(se cancelan sus pedidos). Todo queda en el historial de estatus del cliente (ya existe) y en la bitácora.

**Urgente (propuesta).** Marca en la orden de venta; el almacén la ve arriba de la lista de despacho con el distintivo; al crear la
entrega, la orden de transporte nace con la prioridad urgente para el chofer y aparece en los avisos.

**El punto grande: Solutions despacha por Advance Logistics.** Hoy cada compañía está aislada en Teikem: una orden de venta de
Solutions crea su entrega **dentro de Solutions**, no en Logistics. Para que el transporte lo vea Logistics hace falta un **puente entre
compañías**: Solutions es un **cliente** de Advance Logistics y, al despachar la orden de venta en Solutions, Teikem crea la **orden de
transporte en Logistics** a nombre de ese cliente (con el consignatario = la farmacia), y el estatus de la entrega vuelve a Solutions. Es un
lote propio y no bloquea la pantalla del pedido, pero hay que decidirlo pronto porque define dónde se ve «urgente» y la prueba de entrega.

**Consignación (propuesta).** No es solo una forma de pago: es inventario que sale del almacén pero sigue siendo de Solutions hasta que
el cliente lo vende. Teikem no lo maneja. Propuesta mínima: la orden en consignación se despacha como siempre (sale del inventario) y se
factura **cuando el cliente informa la venta** (parcial o total), con un reporte de «lo que está en consignación por cliente». Si hace falta
verlo como inventario propio «en casa del cliente», es un lote más grande (almacén virtual por cliente).

## 7. Decisiones abiertas para la segunda ronda

1. Envío: ¿lo calcula Teikem con la regla de arriba, sí o no?
2. Firma dibujada del cliente: ¿opcional u obligatoria?
3. Kits/ofertas: ¿vale el prorrateo por precio de lista para el análisis por producto?
4. Cliente pendiente: ¿vale «se puede pedir y reservar, pero no despachar ni facturar»? ¿Quién aprueba (supervisor de ventas, crédito, o ambos)?
5. Puente Solutions → Logistics para el transporte: ¿se hace así (Solutions como cliente de Logistics)? ¿Y Depot entra en el flujo
   (almacenaje de Solutions) o Solutions tiene su propio almacén, como hoy ALM-SOLUTIONS?
6. Consignación: ¿basta facturar cuando el cliente informa la venta, o hay que ver ese inventario como propio en casa del cliente?

## 8. Dictado de Luis (2026-11-06, segunda ronda)

1. **Envío:** hay que manejar un **costo por envío** y un **límite para envío gratis**. El límite ($120) aplica al pedido completo, sea de
   ofertas o de productos individuales: «llega a 120 y te lo envío gratis».
2. **Kits y ofertas:** de acuerdo con la propuesta de §6 (venta por el kit, inventario por componente, prorrateo para el análisis).
3. **Cliente pendiente:** nace como **PROSPECTO** y **avisa al responsable**, que es el **supervisor de ventas**. Debe haber un **permiso
   específico** para eso; si existe uno de supervisor de ventas se reutiliza, si no se crea.
4. **Puente entre compañías: es un módulo opcional.** Teikem se vende por módulos: *Almacenaje*, *Transporte*, los dos, o **multitenant**.
   Quien compra multitenant debe poder hacer que sus compañías **se hablen entre sí**. Advance es ese caso. Se diseña como algo que se
   prende: un **módulo de integración entre compañías**.
5. **Consignación:** sigue el **proceso normal** de la orden, pero con un **indicador de venta en consignación** para que **no salga en
   los reportes de lo vendido hasta que se registre un pago**. Hace falta un proceso de **registrar el pago del cliente** (manual, alguien
   lo entra; no hay que automatizarlo), sin dañar lo que ya hay.
6. **Firma dibujada del cliente:** **parámetro por cliente** (si le hace sentido al analista).

## 9. Opinión del analista sobre la segunda ronda (anotada para construir)

1. **Envío.** Dos ajustes por compañía en Ajustes → Ventas: **cargo de envío** (monto) y **mínimo para envío gratis**. El mínimo se
   compara con el total del pedido (todo lo que lleve: productos, kits y ofertas) **después del IVU**, como dice el formulario, y solo
   cuando es **entrega**. El cargo entra como una línea de servicio en el pedido y la factura. Detalle que hay que confirmar con el contador:
   si el cargo de envío **lleva IVU** (en PR normalmente sí cuando se cobra aparte); se deja como opción en el mismo ajuste.
2. **Supervisor de ventas.** No existe ningún permiso de supervisor (hay `sales.view/create/cancel/price/backorder/return` y
   `sales.return.authorize`). Propongo **dos permisos nuevos** y un **rol nuevo «Supervisor de ventas»** que los tenga (de fábrica en las compañías nuevas; en Solutions lo crea el administrador): `sales.approve_client`
   (aprobar o rechazar clientes prospecto que entran por un pedido; recibe el aviso) y `sales.pricelist` (crear y vigenciar listas de precios,
   ofertas y sus fotos). Separados, porque la casa es un permiso por acción y así el admin puede repartirlos. Permiso nuevo ⇒ también a los
   roles ya clonados (regla del proyecto). El rol **Ventas** (vendedor) suma `clients.create_basic` (alta mínima del prospecto).
3. **Puente entre compañías = módulo `INTERCOMPANY` (nombre de trabajo).** La forma más limpia es **no inventar semántica nueva**: una
   compañía que despacha por otra se comporta como un **cliente** de esa otra, igual que cualquier cliente del portal. Lo que hace el módulo:
   - **Enlace** entre dos compañías del mismo dueño: «Solutions es cliente de Logistics» (el admin de Logistics lo acepta y lo asocia a un
     `Client` suyo; el admin de Solutions lo acepta de su lado). Sin enlace aceptado por ambos, nada cruza.
   - **Qué cruza**: al despachar una orden de venta en Solutions con entrega, Teikem crea la **orden de transporte en Logistics** a nombre
     de ese cliente (consignatario = la farmacia, urgente = urgente, paquetes del despacho), y devuelve a Solutions el número, el estatus,
     la prueba de entrega y los avisos. Logistics lo ve como una orden normal de un cliente normal.
   - **Aislamiento intacto**: cada compañía sigue viendo solo lo suyo; el puente copia únicamente lo que hace falta para la entrega, por
     el mismo canal que ya usa el API de clientes. Por eso también sirve si un día Logistics y Solutions estuvieran en instalaciones distintas.
   - Lo mismo valdría para **Depot** si Solutions quisiera que Depot le guarde y le empaque (Solutions como cliente 3PL de Depot). Hoy
     Solutions tiene su propio almacén (ALM-SOLUTIONS), así que no hace falta.
   - Es un lote aparte y grande; la pantalla del pedido **no lo necesita** para arrancar: mientras tanto la entrega se crea en Solutions como hoy.
4. **Consignación.** Bandera **«en consignación»** en la orden (y en la factura que sale del despacho, como hoy). La factura se emite igual,
   pero los reportes de ventas la cuentan **solo cuando se registra el pago**; hasta entonces sale en un reporte aparte: «Consignado por
   cliente» (qué se le dejó, qué pagó, qué queda). **Registrar pago de consignación**: pantalla sencilla en Contabilidad, cliente → factura en
   consignación → monto (parcial o total) y fecha; cada pago mueve lo pagado a «vendido». Devoluciones de lo no vendido usan la devolución
   de venta que ya existe. Esto no cambia el despacho ni la facturación.
5. **Firma del cliente por parámetro: sí hace sentido.** El cliente ya tiene «Exige la firma de quien recibe» para la entrega del chofer;
   se agrega **«Exige la firma de quien autoriza el pedido»** al lado. Con la casilla encendida, someter el pedido pide nombre y firma
   dibujada; apagada, no. La huella / Face ID del vendedor es aparte y va siempre.

### 9b. Envío por oferta y por cliente (pregunta de Luis, tercera ronda)

Luis: el cargo y el mínimo por compañía están bien; ¿se pone también en la oferta (como parte de la promoción) y en el cliente (para
tratar a uno en particular)? Opinión: **sí a los dos, pero cada uno con una sola cosa**, para que no haya tres sitios donde ajustar
montos:

| Nivel | Qué se configura | Para qué |
|---|---|---|
| Compañía | Cargo de envío + mínimo para envío gratis (+ si lleva IVU) | La regla general. |
| Cliente | **Envío gratis siempre** (sí/no) y, opcionalmente, **su propio mínimo** | La farmacia grande que negoció envío gratis, o un mínimo distinto. |
| Oferta | **«Incluye envío gratis»** (sí/no) | La promoción se vende con «y te lo llevo gratis»; nada de montos ni mínimos por oferta. |

**Corrección de Luis (misma ronda): en la oferta no va nada.** Solo compañía y cliente, **todo opcional**, y **el cliente gana**. El cliente
nuevo asume lo de la compañía; si alguien le cambia el suyo, manda el del cliente. Si se cambia el de la compañía, debe cambiar para todos
los clientes que no tengan uno propio. Preocupación de Luis: que ese cambio en cadena traiga problemas.

**Cómo se hace sin problemas: el cliente no guarda una copia, guarda «usa la de la compañía».** Es el mismo patrón que Teikem ya usa en el
cliente para la hora del resumen de avisos y el servicio por defecto (`NULL` = el de la compañía). Así no hay que recorrer clientes ni
copiar nada cuando la compañía cambia: los que están en «usa la de la compañía» ven el valor nuevo al instante, y los que tienen regla
propia no se tocan.

| Nivel | Opciones |
|---|---|
| Compañía (Ajustes → Ventas) | **Sin cargo de envío** (nada configurado) · **Cargo de $X, gratis desde $Y** (el mínimo es opcional: sin mínimo, siempre se cobra) · si lleva IVU |
| Cliente (ficha) | **Usa la regla de la compañía** (por omisión; es lo que recibe un cliente nuevo) · **No se le cobra envío** · **Regla propia**: cargo de $X, gratis desde $Y (mínimo opcional) |

Regla al pedir, solo en entrega: se toma la regla del cliente si tiene una propia, si no la de la compañía. Con esa regla: si es «sin
cargo» o «no se le cobra», envío $0; si el total después del IVU llega al mínimo, $0; si no, el cargo. El pedido y la factura muestran por
qué («sin cargo por acuerdo con el cliente» / «gratis por monto» / «cargo de envío»).

## 9c. Y QuickBooks: cómo se entera la oficina (cuarta ronda)

**Hoy:** Formsite manda el pedido por correo; una persona lo recibe y **lo entra a mano en QuickBooks**, y ahí se trabaja. Al retirar el
formulario se va ese correo. Luis: esa persona puede tener acceso a Teikem; lo bueno sería **generar un archivo para importar la venta a
QuickBooks** y no depender de la entrada de datos. Dos formas: le damos acceso para que saque el archivo, o Teikem le manda el archivo.

**Opinión: las dos, porque son la misma pieza con dos salidas.** Teikem genera el archivo de QuickBooks y (a) se **descarga** desde la
pantalla de órdenes de venta («Exportar a QuickBooks», una orden o las del día) y (b) **sale solo** por la integración de salida que ya
existe (Lote 137b: por evento o por horario), agregándole dos cosas que hoy no tiene: que cubra **órdenes de venta** (hoy solo envíos de
transporte) y el conector **correo** (hoy SFTP/FTP/API). La persona de la oficina recibe cada mañana el archivo del día adjunto, lo importa
en QuickBooks en un minuto, y además tiene usuario en Teikem (`sales.view`) para ver lo pendiente. Si un día QuickBooks se conecta
directo, se cambia el conector, no la pantalla.

**Lo que hay que resolver para que el archivo importe sin errores (esto es lo delicado, no el archivo):**
1. **Qué QuickBooks es.** El `ItemList.CSV` que me diste es un export de **QuickBooks Desktop**; ahí lo que se importa es un archivo **IIF**
   (facturas, órdenes de venta y hasta clientes nuevos). Si fuera QuickBooks **Online**, no hay IIF: se usaría su importación CSV de facturas o
   su API. Hay que confirmarlo.
2. **Qué entra hoy en QuickBooks: ¿una orden de venta o una factura?** Y **en qué momento** (al recibir el pedido, o al despacharlo). Eso
   decide qué transacción lleva el archivo y qué evento lo dispara (solicitada vs despachada). Mi recomendación: la **factura al despachar**
   (es lo que de verdad se vendió, con backorders ya resueltos); el pedido solicitado solo vive en Teikem.
3. **Los nombres tienen que coincidir letra por letra**, porque QuickBooks empareja por nombre: el **Item** de QuickBooks con el SKU de
   Teikem (hoy difieren: `NECH 1001` vs `NECH-1001`, `24081028` vs `24-081028`, underpads; hay que igualarlos de un lado o guardar en el
   producto de Teikem un campo «nombre en QuickBooks»), el **Customer** con el número/nombre del cliente, los **términos**, el **ítem de
   IVU** y las cuentas. Lo mismo para los clientes nuevos: el archivo puede crearlos en QuickBooks con sus datos (SURI, dirección, término).
4. **Kits y ofertas no existen en QuickBooks.** O se crean allá como *Group items* con el mismo nombre, o el archivo los manda
   **desglosados por componente con el precio prorrateado** (lo mismo que el análisis de §6). Recomiendo el desglose: no hay que tocar QuickBooks.
5. **Lista de precios: no hace falta sincronizarla.** El archivo lleva el precio de cada línea tal como se vendió (lista, oferta o acuerdo);
   QuickBooks lo toma sin chistar. Los precios de lista de QuickBooks ya llegaron a Teikem con la migración y se refrescan con
   `import-legacy --update` cuando cambien allá.

**Corrección de Luis (misma ronda): el correo no es de fiar.** Teme que el correo no llegue (se agota el tope de 300 diarios de Brevo, o
cualquier otra razón) y el pedido se pierda. Prefiere que **la persona entre a Teikem**, pero hay que **avisarle**, y eso implica un rol nuevo.

**Opinión: de acuerdo, y así nada se puede perder.** El archivo **no viaja**: las órdenes viven en Teikem y el archivo se genera cuando la
persona entra y pulsa «Exportar a QuickBooks» (lo pendiente de pasar, marcado como exportado al generarlo; se puede volver a sacar). Si el
aviso falla, lo pendiente sigue ahí a la vista. El aviso es solo un empujón, y por eso no depende del correo:
- **Dentro de Teikem**: la campana de avisos que ya existe (los avisos de ventas del Lote 66 ya avisan por permiso) y un panel en el Pulso
  del día «Pedidos por pasar a QuickBooks: N» para quien tenga el permiso. Lo ve al entrar, sin correo.
- **Correo, uno solo al día** (el resumen diario que ya existe), nunca uno por pedido: con 300 al día no hay riesgo, y si ese único correo
  no llega, no se pierde nada.
- **Rol:** no hace falta inventar un rol fijo; hace falta un **permiso** nuevo, `sales.export_quickbooks` (sacar el archivo y recibir el
  aviso), y el administrador se lo da a quien le toque (un rol «Oficina de ventas» con `sales.view` + ese permiso; lo crea el administrador,
  no hace falta código). Si la misma persona entra también los pagos de consignación, lleva ese permiso en el mismo rol.

**Quinta ronda (Luis):** ¿a qué hora sale ese único correo, si no se sabe cuándo «se acaba el día»? Decisiones: QuickBooks es **Desktop**;
**los productos se sincronizan**: se crean en Teikem y se importan a QuickBooks; **los clientes nuevos también entran a QuickBooks desde
Teikem**. Teikem pasa a ser el maestro de productos y clientes; QuickBooks recibe.

**El correo del día no espera a que «se acabe el día»: sale a una hora fija.** Teikem ya tiene la hora del resumen diario configurable
por compañía (por omisión las 18:00) y lleva «todo lo que está pendiente en ese momento», no «lo del día». Para la oficina conviene la
**mañana** (p. ej. 7:00): el correo dice «hay N pedidos, M clientes y K productos por pasar a QuickBooks» y la persona entra y los saca.
Lo que entre después de esa hora se ve igual en Teikem (campana y panel del Pulso al instante) y sale en el correo del día siguiente.
La hora se ajusta en Ajustes; el correo no se manda si no hay nada pendiente.

**Productos y clientes hacia QuickBooks (archivo IIF de Desktop).** El mismo botón «Exportar a QuickBooks» saca **un solo archivo** con, en
este orden, los **productos nuevos** (`INVITEM`: nombre = SKU, descripción, precio, costo y las cuentas), los **clientes nuevos** (`CUST`: nombre,
número, dirección física y postal, teléfonos, correo, término de pago, SURI como número de registro) y las **facturas** del período. Cada
cosa queda marcada como exportada al salir, y si algo cambió en Teikem después (precio, dirección) vuelve a salir como actualización.
Consecuencias:
- **Cuentas de QuickBooks:** el IIF necesita las cuentas de cada producto. En el `ItemList.CSV` todos usan `Cost of Goods Sold` e
  `Inventory Asset`, pero la cuenta de ventas varía por ítem (`Sales:Global Cotton Balls 100ct`, `Sales:Monitors`…). Se pone en Ajustes →
  QuickBooks una **cuenta de ventas por omisión** (p. ej. `Sales`) y, si Luis quiere, una por categoría; el IVU con el nombre del ítem de impuesto de QuickBooks.
- **Nombres de ítem = SKU de Teikem, de aquí en adelante.** Los 7 que hoy difieren (`NECH 1001`→`NECH-1001`, `24081028`→`24-081028`,
  `24083225`→`24-083225`, los dos underpads, `Boost Immune`) se **renombran una vez en QuickBooks** (QuickBooks conserva el historial al renombrar);
  es más barato que mantener dos nombres por producto para siempre.
- **Kits y ofertas:** no se mandan como ítems; en la factura van desglosados por componente con el precio prorrateado (§6).
- **Clientes:** el nombre en QuickBooks = nombre del negocio; el `Customer #` = código del cliente en Teikem (el mismo que ya se migró).
- `import-legacy --update` deja de ser el camino de los precios: los precios de lista viven en Teikem (lista de precios del supervisor).

**Sexta ronda (Luis):** no recuerda si hoy entra la orden o la factura en QuickBooks; entrará **cuando la persona de la oficina decida**.
Pide una forma de **hacer constar que se pasó a QuickBooks**, para que el aviso de «pendiente» sea coherente: exportar no significa que entró,
pero tampoco quiere algo tan rígido que obligue a confirmar cada importación. Lo más fácil posible, sin que caiga en silencio.

**Propuesta: tres estados y un solo clic, por tanda.**

| Estado de una factura (o cliente / producto) | Cómo se llega | Qué ve la oficina |
|---|---|---|
| **Por pasar** | Al despachar (factura), al crearse (cliente, producto) | Cuenta en el Pulso y en el correo de la mañana: «Por exportar: N». |
| **Exportado** (tanda #, fecha, quién, archivo) | Al pulsar «Exportar a QuickBooks»: todo lo «por pasar» sale en una tanda numerada y queda **exportado sin confirmar** | «Exportados sin confirmar: M (tanda #12, ayer)». Se puede volver a bajar el mismo archivo. |
| **En QuickBooks** | Un clic en la tanda: **«Ya se importó»**. Si algo falló, se desmarcan esas líneas antes de confirmar y vuelven a «por pasar» para la próxima tanda | Desaparece de los pendientes. |

- Un solo botón por tanda, no por factura. En el caso normal: exportar, importar en QuickBooks, volver y pulsar «Ya se importó». Diez segundos.
- Si no confirma, **no pasa nada grave**: la tanda sigue «exportada sin confirmar» y el aviso de la mañana lo recuerda sin estorbar. Nada se
  vuelve a exportar solo, así que no hay riesgo de duplicar en QuickBooks.
- Si la importación falló en QuickBooks, «Volver a exportar la tanda» genera el mismo archivo (o solo las líneas desmarcadas).
- Todo queda en el historial: quién exportó, quién confirmó y cuándo. QuickBooks Desktop no puede avisar de vuelta, así que no hay forma
  automática de saber que entró; este clic es lo mínimo que lo deja constar sin ser una carga.

**Séptima ronda (Luis): lo anterior es confuso.** Lo que importa es la **factura**. Clientes y productos no tanto: van en el archivo
porque Teikem es el maestro, y si faltan QuickBooks revienta, y ya. El caso que le preocupa: exporté, no llegué a importar nada, entraron
más facturas y quiero volver a exportar **todas** (las de antes más las nuevas).

**Versión simple (sustituye a la tabla de tres estados).** Una factura tiene **una sola marca: «en QuickBooks» (sí / no)**. Nada más.

- **Exportar a QuickBooks** saca **todas las facturas que no están en QuickBooks**, siempre. Da igual si ya salieron en un archivo anterior:
  si no están confirmadas, vuelven a salir. Ese es exactamente el caso de Luis: el archivo nuevo trae las de antes y las nuevas, y el
  anterior se tira a la basura.
- Al generar el archivo, Teikem pregunta en la misma pantalla: **«¿Ya importaste estas N en QuickBooks?»** con la lista marcada. Si la
  persona lo importa y vuelve a pulsar «Sí, ya están», quedan en QuickBooks. Si cierra sin contestar, quedan sin marcar y saldrán otra vez
  la próxima. Si una falló en QuickBooks, la desmarca y las demás sí.
- **Seguro contra duplicados:** al exportar, si alguna de las facturas ya había salido en un archivo anterior (Teikem recuerda la fecha
  del último archivo), lo dice arriba: «N de estas ya salieron el (fecha); si ya las importaste, márcalas como en QuickBooks antes de
  volver a importar, para no duplicarlas». Es un aviso, no una traba.
- **Clientes y productos:** el archivo lleva los clientes y productos **de esas facturas** que todavía no se han mandado nunca (una fecha
  «mandado a QuickBooks» en cada uno, que se pone al confirmar las facturas). Sin pantalla propia ni estados: van de acompañantes de la
  factura, y si falta uno QuickBooks lo dice.
- **Pulso y correo de la mañana:** un solo número, «Facturas por pasar a QuickBooks: N».

**Octava ronda (Luis): clientes y productos nuevos, a la vista y a elección.** Teikem sabe cuándo un cliente o un producto sale por
primera vez en una factura (después de esa, nunca más será nuevo). Entonces:
- Al **exportar**, Teikem avisa: «Estas facturas traen 2 clientes nuevos y 1 producto nuevo. ¿Exportarlos también?» Si la persona dice sí,
  van en el archivo; si dice no, el archivo lleva solo las facturas y ella los entra a mano en QuickBooks.
- En la **pantalla** (la lista de facturas por pasar, y la orden de venta misma) se ve la marca **«cliente nuevo» / «producto nuevo»**, con
  una opción para **ver cuáles son y sus datos** (nombre, número, dirección, término; SKU, descripción, precio, costo), por si prefiere
  escribirlos en QuickBooks mirando la pantalla.
- Al confirmar las facturas como «en QuickBooks», esos clientes y productos quedan como ya mandados (sea que fueron en el archivo o a mano).

**Novena ronda (Luis): dos permisos.** Uno **solo para ver el aviso** de que hay facturas sin pasar a QuickBooks (campana, panel del Pulso,
correo de la mañana). Otro para **marcar a mano «ya se entró en QuickBooks»** (sin exportar ningún archivo): quien entra la factura tecleando
en QuickBooks la marca y el sistema deja de recordársela. Quedan así:

| Permiso | Qué permite |
|---|---|
| `sales.quickbooks.view` | Ver «Facturas por pasar a QuickBooks» (lista, campana, Pulso, correo de la mañana) y ver los clientes y productos nuevos con sus datos. |
| `sales.quickbooks.mark` | Marcar facturas como «en QuickBooks» a mano (una, varias o todas), con quién y cuándo en el historial. |
| `sales.quickbooks.export` | Exportar el archivo IIF (facturas, y opcionalmente clientes y productos nuevos) y confirmar las que ya importó. |

Quien solo ve, no puede marcar ni exportar; quien marca a mano no necesita exportar. El administrador los reparte en un rol nuevo «Oficina de
ventas» con los tres (en Solutions lo crea el administrador en Roles y usuarios; las compañías nuevas lo reciben de fábrica). Son permisos nuevos ⇒ también a los roles ya clonados.

## 10. Lo que queda por decidir / siguiente paso

- ¿El cargo de envío lleva IVU? (pregunta para el contador; se deja configurable).
- Nombre y alcance del enlace entre compañías: ¿solo transporte, o también almacenaje (Depot)?
- Con esto el análisis ya alcanza para **planear los lotes** (§3.1 más: lista de precios y ofertas, supervisor de ventas, consignación,
  firma por cliente, módulo entre compañías). Siguiente paso: un plan de lotes con orden y tamaño, para que Luis apruebe por dónde empezar.
