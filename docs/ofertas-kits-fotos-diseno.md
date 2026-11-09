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
