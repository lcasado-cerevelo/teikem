# Etiquetas de producto (Productos e inventario) — 2026-10-09

La **etiqueta de producto** es una etiqueta adhesiva para impresora de etiquetas (Zebra u otra térmica) con el **código de barras del SKU**, el **SKU en letra grande** y el **nombre** del producto (y su categoría). Pedido del dueño: lo mismo que las etiquetas de posición ([F16](f16-etiquetas-de-posicion.md)) pero para productos, en los mismos tamaños 4 × 2, 4 × 4 y 4 × 6, **imprimiendo solo la etiqueta**.

**Dónde.** Almacén → **Productos e inventario** (pestaña Productos), botón **Etiquetas de producto** en la cabecera, junto a **Códigos de barras**.

**Quién puede.** Módulo **WMS_LOTSERIAL** y permiso **`inventory.view`** (el de la pantalla y el de su reporte de códigos de barras). Sin el permiso el botón no se ve.

**Servidor.** No cambió: se lee el listado de productos de siempre (`GET /api/v1/products`, de a 200, sin tope de cantidad) y el PDF se arma en el navegador.

## Qué imprime
EXACTAMENTE los productos que filtra la tabla (almacén, producto, nombre, categoría, marca y KPI), en orden natural de SKU (SKU-2 antes que SKU-10). **Una etiqueta = una página del PDF del tamaño exacto de la etiqueta** (sin hoja carta ni márgenes que recortar). Máximo **500 etiquetas por PDF**.

| Parte de la etiqueta | Qué lleva |
|---|---|
| Código de barras (Code 128) | El **SKU exacto** (la app del lector busca el producto por código de barras o por SKU; el código de barras propio del producto no se usa, igual que en el reporte de códigos) |
| Texto grande | El SKU |
| Texto pequeño (hasta 2 renglones) | Nombre del producto · categoría |

## Cómo se usa
1. Filtre la tabla de Productos como quiera (o déjela completa).
2. **Etiquetas de producto** → elija el **tamaño** (4 × 2 apaisada, 4 × 4 cuadrada, 4 × 6 vertical, con su equivalente en cm) y la **orientación** (Automática o Girar 90° si la impresora saca la etiqueta de lado).
3. **Generar PDF**: baja el archivo `etiquetas-de-producto-4x2-<compañía>-<día>.pdf`. El tamaño y la orientación se recuerdan en este navegador.
4. En el driver de la impresora use el mismo tamaño de etiqueta e imprima al 100 %.

Las etiquetas **no tienen estado**: imprimirlas no marca nada y se pueden reimprimir cuando se quiera.

## Mensajes
| Situación | Mensaje |
|---|---|
| Más de 500 productos en el filtro | Solo un aviso: `Son {n}: el PDF será grande y puede tardar un poco. Se imprimen todas; no hay límite.` (se imprimen todos) |
| El filtro no trae productos | `No hay productos para imprimir con los filtros actuales.` |
| SKU con caracteres que Code 128 no admite, o demasiado largo | Se imprime la etiqueta solo con el SKU en texto y, al terminar: `{n} salieron sin código de barras (solo con el código en texto):` + la lista |
| Éxito | `Se generaron {n} etiquetas de 4 × 2 pulgadas.` |
| Error del servidor al leer | `No se pudieron generar las etiquetas. {mensaje del servidor}` |

## Preguntas frecuentes
**¿En qué se diferencia de «Códigos de barras»?** «Códigos de barras» es una **lista en hojas carta** con muchos códigos por hoja (para imprimir y escanear el papel en el conteo). La **etiqueta de producto** es una etiqueta adhesiva por producto, del tamaño del rollo.

**¿Cómo imprimo solo algunos productos?** Filtre la tabla (por nombre, categoría, marca…) hasta que queden solo esos; se imprime lo filtrado. (No hay casillas de selección en la tabla de productos.)

**¿Puedo imprimir varias copias de la misma etiqueta?** No: sale una por producto. Imprima el PDF varias veces desde el visor.
