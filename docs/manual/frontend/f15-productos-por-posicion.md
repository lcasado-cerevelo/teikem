# F15 — Productos por posición (Ubicaciones)

Un PDF para imprimir **todos los productos de una posición**, con el **código de barras de cada uno**, **una posición por página**: así quien
trabaja en un rack alto (hasta 30 pies) escanea los productos **desde el papel** sin alcanzar el producto ni su etiqueta. Pedido del dueño del
2026-10-05; reemplaza a las "hojas de posición" del Lote F15 original (2026-10-03), que se quitaron porque nadie les encontró uso: llevaban un
estado (impresa / desactualizada) y un aviso que no hacían falta. Decisiones en `docs/lote24-decisiones.md`.

**Dónde.** Almacén → **Posiciones** (`/warehouse/locations`), en la cabecera de la tabla de posiciones del almacén elegido arriba, junto a
**Códigos de barras** y **Etiquetas de posición**.

**Quién puede.** Módulo **WMS_LOTSERIAL** y permiso **`inventory.view`** (el de la pantalla). Sin el permiso no se ve el botón.

## 1. Filtrar y elegir qué imprimir

- Filtro **Pasillo** (arriba, junto a **Posición**): texto que va al servidor (`aisle`, la posición debe **contener** ese texto en su pasillo),
  con una pausa de 300 ms para no consultar por tecla. Sirve para sacar un pasillo completo sin acordarse de cómo se arma el código.
  **Posición** sigue buscando en código, pasillo, rack, nivel y posición. **Limpiar** quita los dos.
- Casillas **Elegir** por fila (primera columna): **Seleccionar todas las de la página** marca o desmarca la página visible (queda a medias
  si solo algunas están marcadas); el contador dice **N marcadas** y **Quitar marcas** las borra. Las marcas se conservan al cambiar de página o
  de filtro dentro del mismo almacén. Marcar no abre la posición.

Hay dos maneras de elegir qué imprimir:

1. **Las posiciones del filtro actual**: exactamente las de la tabla con todos sus filtros (Posición, Pasillo, Zona, Tipo, Producto, Estatus
   o un recuadro de zona), todas las páginas.
2. **Las posiciones marcadas**: las que tengan casilla.

Pulse **Productos por posición**: el modal abre con *marcadas* si hay marcas, si no con *filtro actual*.

- **Incluir posiciones vacías** (apagado por defecto): apagado, las posiciones sin productos no se imprimen; encendido, salen con una página
  **Sin productos**.
- **Sin tope** (desde 2026-10-10): se imprimen **todas** las posiciones del alcance. Pasando de **500** el modal solo **avisa** que el PDF es grande
  (*Son {N}: el PDF será grande y puede tardar un poco. Se imprimen todas; no hay límite.*); **Generar PDF** sigue habilitado.

## 2. Generar el PDF

Pulse **Generar PDF**: se leen las posiciones del servidor de **200 en 200** (`GET /api/v1/warehouses/{almacén}/bin-products`: *Leyendo
posiciones… 200 de 431*; **Cancelar** detiene todo), se arma el PDF en el navegador y se descarga, por ejemplo
`productos-por-posicion-advance-logistics-2026-10-05.pdf`. Las posiciones van **por código en orden natural** (A-2 antes que A-10). Un aviso
dice *Se generaron N página(s) de M posición(es).*, más las vacías omitidas y los productos sin código legible si aplica. **No se marca ni se
guarda nada**: reimprimir es volver a generarlo.

Imprima el PDF **al 100 %** ("Tamaño real", sin "Ajustar a la página"), en negro sobre papel blanco.

## 3. Cómo es la página

![Página de una posición con un producto](img/f15-pdf-hoja.png)

- **Carta vertical, una posición por página, siempre**: cada posición empieza página nueva.
- **Encabezado**: el **código de la posición en grande** con su **código de barras** (Code 128 del código exacto) y debajo *Zona RSV · Pasillo 01 ·
  Rack R1 · Nivel N1 · Posición P01* (solo las partes que existen; *Inactiva* si la posición está dada de baja).
- **Productos (hasta 10 por página)**: cada uno en un recuadro con el **SKU** en grande, el **nombre** (si es largo se recorta con "...") y su
  **código de barras**: el **código de barras del producto**; si no tiene (o el lector no lo admite, o no cabe), el **SKU**. Debajo de las barras va el
  valor legible. Solo los productos con **existencia** en la posición, sumando lotes (un renglón por producto), **por SKU**.
- **Tamaño adaptable**: el alto de la página se reparte entre sus productos: con **1 o 2** el código sale lo más grande posible y va debajo del
  texto a todo el ancho; con **3 a 10** va a la derecha del texto y todo se achica para que quepan. Nunca baja de lo legible (barra fina de al
  menos 0.25 mm).

![Página con 10 productos (datos de prueba)](img/f15-pdf-diez-productos.png)

- **Más de 10 productos**: la posición sigue en otra página con el mismo encabezado y *Hoja 2 de 2* arriba a la derecha (11 productos = 10 + 1).

![Segunda página de una posición con 11 productos (datos de prueba)](img/f15-pdf-hoja-2-de-2.png)

- **Posición sin productos** (solo con *Incluir posiciones vacías*): el encabezado y, en el centro, **Sin productos**.
- **Pie**: *Impresa el 10/05/2026 2:05 p. m.* (formatos y hora de la compañía; es la hora de los datos leídos) y *Almacén ALM-01 · Almacén
  principal · Advance Logistics*.
- **Sin código**: si ni el código de barras ni el SKU de un producto se pueden imprimir como Code 128 (acentos, ñ…), el recuadro lleva el valor y
  *Sin código de barras: Code 128 no admite Ñ*; si es tan largo que no cabe legible, *No cabe: demasiado largo para un código legible*. En los dos
  casos un aviso al pie de esa página los lista (los mismos textos del reporte de códigos de barras, F14).

## 4. Mensajes

| Mensaje | Dónde | Qué hacer |
|---|---|---|
| *Son {N}: el PDF será grande y puede tardar un poco. Se imprimen todas; no hay límite.* (desde 500) | Modal (aviso, no error) | Nada que hacer: se imprimen todas. Si el PDF le parece demasiado grande, filtre por zona, pasillo o texto, o marque menos posiciones |
| *No hay posiciones en lo que eligió.* | Modal | El alcance elegido no tiene posiciones; elija otro o cambie los filtros |
| *No hay nada para imprimir: las posiciones elegidas no tienen productos. Active «Incluir posiciones vacías» para imprimirlas.* | Modal (rojo); no se genera PDF | Encienda el interruptor si quiere páginas *Sin productos* |
| *Impresión cancelada.* | Aviso abajo | Nada: no se descargó |
| *No se pudo generar el PDF de productos por posición. {mensaje del servidor}* | Modal (rojo) | Si el servidor respondió 400/404 (p. ej. *Almacén no encontrado.*), el mensaje va al final tal cual; si no, vuelva a intentar |
| *Se generaron N página(s) de M posición(es).* | Aviso abajo (verde) | — |

Los mensajes del servidor (con su código HTTP) están en el [FAQ, sección Productos por posición](../faq.md#productos-por-posición-informe-reemplaza-a-las-hojas-de-posición-del-lote-23-y-del-f15).

## 5. Exportar la tabla

El botón **Exportar** de la tabla saca **todo lo filtrado** (todas las páginas, sin tope de cantidad) con los mismos filtros, incluido **Pasillo**; no solo
la página que se ve.

## 6. Casos frecuentes

- **Imprimir todo un pasillo**: escriba el pasillo en **Pasillo**, **Productos por posición** → *Las posiciones del filtro actual* → **Generar PDF**.
- **Imprimir solo algunas posiciones**: marque sus casillas → **Productos por posición** → *Las posiciones marcadas*.
- **Mercancía movida después de imprimir**: el papel no avisa; vuelva a generarlo.
- **El lector no lee el código**: imprima al 100 %; los códigos se validaron decodificando el PDF a 150–600 dpi, pero no con un Zebra físico.
