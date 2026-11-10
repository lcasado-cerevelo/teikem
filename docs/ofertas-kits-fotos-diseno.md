# Ofertas, kits y fotos: lo acordado con Luis (2026-11-09)

Estado: **acordado, sin construir**. Cambia el diseño del lote 221 (donde una oferta era un kit con la marca «Es promoción»).
La **sustitución de piezas** (§6) y las **dimensiones** (§6b) tienen la regla acordada y detalles por confirmar.

## 1. Los tres conceptos, separados
- **Producto.** Incluye los kits. Un **kit es un producto más**, formado por otros productos, con su precio normal. Se vende también suelto.
- **Lista de precios.** Los precios de siempre. Puede estar vigente todo el tiempo; se puede manejar una sola o varias (general o por
  cliente). **No lleva ofertas.** Al agregar un renglón se puede escoger un producto o un kit, y crear un kit desde ahí.
- **Oferta.** Cosa aparte, como la circular de una tienda («válido del … al …»): nombre, foto, vigencia y un listado de productos o
  kits, cada uno con su precio de oferta. Aplica a todos los clientes o a clientes escogidos.

## 2. La oferta
- Pantalla en Contabilidad → Precios y ofertas, con dos secciones: «Listas de precios» y «Ofertas». Permiso `sales.pricelist`.
- **Vigencia:** desde / hasta. Si «hasta» queda en blanco, la oferta no vence y **la pantalla se lo dice al usuario** con claridad.
- **Renglones**, de dos maneras:
  - escoger un producto o kit que ya existe y ponerle su precio de oferta;
  - **crear un kit para esa oferta** ahí mismo (nombre, SKU, piezas y cantidades) con su precio. Ejemplo de Luis: el kit normal es
    monitor + agujas; el de la oferta es monitor + agujas + tirillas «al precio de las dos», o las tirillas a mitad de precio.
- **Kit de una oferta:** es un producto de verdad pero queda atado a su oferta. Solo se vende mientras la oferta esté vigente. Al
  terminar la oferta **queda inactivo** (no se borra: lo siguen viendo las órdenes y facturas que lo usaron). Si se extiende la fecha,
  vuelve a estar disponible. En el editor nace **no ajustable**.
- En el pedido guiado las ofertas vigentes salen como tarjetas con foto arriba de su marca. Un producto normal en oferta sale además en
  la lista de su marca con el precio de oferta.

## 3. Qué precio se aplica
1. Entre el **precio negociado del cliente** y la **oferta vigente**, gana **el más bajo** (decisión de Luis).
2. Si no hay ninguno de los dos: la lista general.
3. Si tampoco: el precio de venta del producto.
- Un producto en dos ofertas a la vez: gana la que empezó más tarde. *(Propuesta mía; por confirmar.)*
- Por confirmar: si la oferta sale más cara que la lista general, ¿también gana el más bajo?
- El vendedor no teclea precios; `sales.price` sigue siendo la excepción.

## 4. Fotos
- **El producto guarda su foto** (hoy no tiene): bloque «Foto» en el editor de producto de Almacén → Productos e inventario. Vale
  igual para kits y para kits de oferta.
- **Qué foto se muestra**, en este orden: la del renglón (lista u oferta) si se le puso una → la del producto → si es un kit sin foto,
  la del primer producto de sus piezas → vacío.
- En listas y ofertas el renglón trae **por omisión la foto del producto**; se le puede cambiar, y al quitarla vuelve la del producto.
- En el pedido guiado el cuadrito (y la foto grande al pasar el mouse) sale para todo producto con foto, no solo para las ofertas.
- **Permiso:** quien tiene `sales.view` debe poder ver la foto de un producto aunque no tenga `inventory.view` (hoy no puede).
  Subirla o cambiarla, con el permiso con que se edita el producto.
- Propuestas mías, por confirmar: JPG/PNG/WebP hasta ~5 MB, reducidas a 1200 px; las fotos de promoción que existan se copian a la foto
  del producto; la importación de productos no lleva foto.

## 5. «Ajustable al pedir»: se quita de las ventas
- Hoy (lote 69) un kit marcado deja cambiar las cantidades de sus piezas en una orden **sin cambiar el precio**: es una forma indirecta
  de alterar el precio. Luis: no tiene sentido.
- **Acordado:** el kit es fijo. Si el cliente quiere más de una pieza, la orden lleva el kit a su precio y **lo extra en líneas aparte**
  a su precio normal. El vendedor nunca altera el precio del kit.
- Se quita la casilla del editor y el servidor deja de aceptar piezas cambiadas; los kits ya marcados pasan a fijos; las órdenes viejas
  con piezas ajustadas se quedan como están. Por confirmar si alguien lo usa hoy en Depot o Solutions.

## 6. Sustitución de piezas de un kit (acordada la regla; detalles por confirmar)
Luis: es muy probable que pase (p. ej. guantes del mismo precio en otra talla). Vale para el kit regular y para el de oferta.
- **Regla:** en una orden, una pieza de un kit se puede cambiar por otro producto de la **misma categoría exacta** (la subcategoría, si
  la tiene) y el **mismo precio**, en la **misma cantidad**. El kit vale lo mismo y su precio no se toca.
- **Categorías:** se usa un nivel más, **categoría → subcategoría** (p. ej. Guantes → Nitrilo). La estructura ya lo permite (una
  categoría puede tener padre). La categoría dice **qué es** el producto; la **marca** va en Marca y el **modelo** en Modelo (ya existen).
  Si hoy hay marcas puestas como categoría, hay que recategorizar los productos (se puede por la importación).
- En el pedido, las piezas con sustitutos válidos llevan «Cambiar» con la lista de productos permitidos; el inventario se reserva y sale
  por la pieza que quedó; la orden y la factura muestran la pieza real.
- Por confirmar: qué precio se compara (propuesta: el precio de venta del producto); si la diferencia de costo entre tallas es aceptable;
  si sustituye solo el vendedor al pedir o también el almacén al despachar cuando falta una pieza; precio exactamente igual o con
  tolerancia; si toda pieza es sustituible o el kit decide cuáles.

## 6b. Dimensiones del producto: largo × ancho × alto y volumen (acordado)
Hoy el producto solo guarda peso (kg) y volumen (m³); casi nadie piensa en metros cúbicos para una cajita.
- El producto gana **largo, ancho y alto**, sin quitar el volumen. Los dos sincronizados:
  - con las **tres medidas puestas**, el volumen **se calcula** de ahí y no se puede editar;
  - si **falta cualquiera** de las tres, el volumen se puede escribir a mano;
  - un volumen escrito antes no se pierde por poner una medida suelta: solo se reemplaza cuando están las tres.
- **Unidad:** al poner las dimensiones la pantalla **pregunta en qué unidad** se están escribiendo y **sugiere una** según la región de
  la compañía (pulgadas o centímetros; pies o metros). El usuario la puede cambiar por producto: lo de Solutions es chico (pulgadas /
  centímetros), lo de Depot es grande (pies / metros, y volumen en pies o metros cúbicos).
- Las medidas son de la **unidad base** del producto (lo que se vende), no de la paleta: la pantalla lo dice.
- Propuestas mías, por confirmar: por dentro el volumen se sigue guardando en m³ y se guarda la unidad escogida para mostrarlo igual;
  la importación de productos acepta las columnas nuevas con la misma regla; el peso, ¿kg o también libras?
- Antes de construir hay que revisar quién usa hoy el volumen del producto (posiciones, tarifas, reportes) para no descuadrarlo.

## 7. Lo que hay que cambiar de lo ya construido
- Campos de promoción del producto (`IsPromotion`, `PromoFrom`, `PromoTo`, `PromoImageFileId`) y la sección «Oferta (promoción)» del
  editor del kit: dejan de usarse.
- Botón «Nueva oferta» actual (escoge un producto y lo convierte en kit): se reemplaza por la pantalla de oferta.
- Resolución de precios y catálogo del pedido; el error 409 por cruce de listas se revisa.
- Manual y FAQ (capítulos 47 y 48, y lo que cite «Es promoción»); scripts de Solutions (OFERTA1 pasa a ser una oferta).

## 8. Respuestas de Luis (2026-11-09, tercera ronda)
- **Sustitución — precio:** se compara el **precio de venta** del producto y debe ser **exactamente igual**.
- **Sustitución — quién:** el vendedor, en la pantalla del pedido. **Sin permiso aparte**: lo decide la definición del kit.
- **Sustitución — qué piezas:** por regla **todo es sustituible**; se marca **lo no sustituible** (no al revés). Luis dijo que la marca
  se pone «cuando se crea la lista de precios». *Por confirmar dónde va exactamente la marca: en el renglón de la lista de precios, en la
  pieza del kit, o en el producto.*
- **Ofertas que chocan:** sigue ganando el precio más bajo, pero **con aviso**: en el pedido, si un producto está en dos ofertas a la vez
  o la oferta sale más cara que la lista general; y **al crear la oferta**, se revisa que no choque con otra oferta parecida más barata.
- **Peso:** igual que las dimensiones: la unidad se escoge al escribirlo y se sugiere por la **región** de la compañía (la región define
  métrico o inglés; Puerto Rico es de habla hispana y usa el sistema inglés).
- **«Ajustable al pedir»:** no se sabe si se usa hoy; Luis: «vamos a permitir lo que se use así». *Por confirmar: lo entiendo como que
  NO se quita (cambia lo anotado en §5); lo nuevo es la sustitución.*

## 9. Respuestas de Luis (2026-11-09, cuarta ronda)
- **«Ajustable al pedir» SE QUEDA** (anula lo anotado en §5). Hoy no hay datos de kits ni de ofertas en las bases, así que nada de este
  rediseño afecta datos existentes.
- **«No sustituible» se marca en la pieza del kit** (por omisión toda pieza es sustituible). La marca se puede poner **desde los dos
  lugares donde se trabaja un kit**: el editor del kit (producto) y la pantalla de la oferta cuando se crea o edita un kit ahí.
- **Editor de producto en escritorio:** Luis lo ve muy angosto y con demasiado scroll; se quiere más ancho en pantalla grande (ver la
  conversación; pendiente de su visto bueno).

## 10. Dos clases de kit (Luis, 2026-11-10; acordado en lo general, sin construir)
Cambia lo construido en los lotes 68–70 y 229: hoy todo kit se arma al despachar y no tiene existencia.
- **Kit armado = producto con inventario.** Se crea y se ve en Almacén → Productos e inventario.
  - «Nuevo producto» **pregunta si lo que se crea es un kit** (por omisión, no). Si es kit, ahí mismo sale la tabla para componerlo
    (piezas y cantidades) y **cuántos kits se arman**. Un producto normal no ve nada de esto.
  - **Armar N kits** saca del inventario las piezas de N kits y deja N kits en existencia. **Desarmar** devuelve las piezas.
  - La pantalla de inventario muestra los kits armados disponibles y las piezas ya restadas.
- **Kit de oferta = se arma al pedir** (como se hace en Solutions: las piezas están ahí y se juntan cuando se pide). Solo vive en las
  ofertas; no tiene existencia propia y no aparece como producto en inventario.
  - Los kits creados en ofertas **se guardan** (la composición; el precio no, aunque se recuerda el último) y al crear otra oferta la
    pantalla **sugiere «usar un kit anterior» o «crear un kit nuevo»** (p. ej. el de noviembre se repite en enero sin teclearlo otra vez).
- Por confirmar: ver la conversación (kit de venta permanente que no se arma antes, qué pasa al vender un kit armado sin existencia,
  dónde aplican la sustitución y «Ajustable al pedir», lotes dentro del kit armado, costo del kit armado, app del almacén).

## 11. Respuestas de Luis sobre las dos clases de kit (2026-11-10)
- Un kit que se vende siempre y no se arma antes = **una oferta permanente** (sin fecha de fin).
- Vender un **kit armado sin existencia**: falta inventario y va a **backorder**, como cualquier producto.
- **Sustitución de piezas: solo en el kit de oferta** (el armado ya tiene sus piezas dentro).
- **Costo del kit armado:** la suma del costo de sus piezas al armar.
- **Lotes y vencimientos:** deben funcionar; es lo que más le preocupa a Luis («diseñarla bien»).
- **Armar/desarmar:** solo en la web, con el permiso de ajustar inventario.
- **No quiere que las piezas de un kit armado queden como «reservadas»** (no es verdad: forman parte de otra cosa), ni que el
  inventario se descuadre.
- Luis duda si construir ya el kit armado (no hay cliente que lo necesite) o dejarlo listo ahora que no hay datos: pendiente de decidir.
