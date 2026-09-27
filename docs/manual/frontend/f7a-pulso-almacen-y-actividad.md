# F7A — Pulso: panel Almacén con filtro y Actividad reciente

Capítulo del manual de pantallas del Lote F7A (frontend), sobre la pantalla de inicio **Pulso del día** (`/`), ya
descrita en el capítulo F1. Este lote agrega un filtro al panel "Almacén" (Lote F6) y un panel nuevo, "Actividad
reciente". Ninguna pantalla tiene ruta propia: las dos viven debajo de los indicadores y gráficos de Pulso.
Capturas en `img/f7a-*.png`.

## Pulso del día: panel "Almacén" con filtro

**Para qué sirve.** Vistazo del estado actual del almacén (saldo, documentos abiertos), con la opción de acotarlo a
un solo almacén y, para el saldo, a una categoría o un producto concreto — por ejemplo, para revisar de un vistazo si
un producto puntual está bajo su mínimo, sin ir a la pantalla de Inventario.

**Cómo se llega.** Aparece automáticamente debajo de los indicadores y gráficos de Pulso (`/`), sin pedir nada aparte.

**Qué se ve.**

![Panel Almacén: selector de almacén, control Categoría o producto y seis tarjetas](img/f7a-pulso-almacen.png)

Una fila de filtros y, debajo, seis tarjetas: **En mano**, **Disponible** (con "Reservado: en mano − disponible"),
**Bajo mínimo**, **Recibos abiertos**, **Tareas pendientes** (con el desglose por tipo: Putaway, Reabasto, Conteo,
Cruce de muelle) y **Conteos abiertos**. Las tres últimas llevan la marca **almacén**: solo reciben el filtro de
almacén, nunca el de categoría o producto (el texto de la cabecera del panel lo aclara, y pasar el mouse o el foco
sobre la marca muestra "Solo aplica el filtro de almacén (no el de categoría o producto)."). Estas tarjetas no
tienen el botón "Rango" de las demás: son saldo actual, no de un período.

**Qué hace cada control.**

- **Almacén**: lista desplegable con "Todos los almacenes" y los almacenes activos de su compañía; se aplica a las
  seis tarjetas.
- **Categoría o producto**: un combobox con buscador, dos secciones (Categorías y Productos) y un botón "✕" para
  quitar la selección. Solo afecta a En mano, Disponible y Bajo mínimo.

  ![Control 'Categoría o producto' abierto, escribiendo el nombre de una categoría](img/f7a-pulso-almacen-buscador.png)

  - Escriba el nombre de una categoría o de un producto (por SKU o nombre); las categorías se filtran al momento,
    los productos se buscan en el servidor una cuarta parte de segundo después de dejar de escribir.
  - Elija una **categoría**: "En mano" muestra su subtítulo con el nombre y cuántos productos tiene (contando sus
    subcategorías); "Bajo mínimo" cuenta los productos de esa categoría por debajo de su mínimo y ofrece el enlace
    **"Ver en Inventario ›"**.

    ![Categoría elegida: En mano con el nombre y la cantidad de productos, Bajo mínimo con el enlace](img/f7a-pulso-almacen-categoria.png)
  - Elija un **producto**: "En mano" muestra su SKU (y su mínimo, si tiene); "Bajo mínimo" pasa a **Sí**/**No** con
    el enlace **"Ver Kárdex de {SKU} ›"**.

    ![Producto elegido: En mano con el SKU, Bajo mínimo en 'Sí' con el enlace al Kárdex](img/f7a-pulso-almacen-producto.png)
  - Teclado: flechas ↑/↓ recorren las opciones, Enter elige, Escape cierra sin elegir.
- **Enlaces "Ver en Inventario ›" / "Ver Kárdex de… ›"**: llevan a la pantalla Inventario (capítulo 06) ya con el
  mismo almacén y la misma categoría o producto de la tarjeta, para que lo que vea ahí cuadre con la cifra que traía
  Pulso.

  ![Enlace 'Ver en Inventario' desde una categoría: Saldos ya filtrado](img/f7a-inventario-saldos-desde-pulso.png)
  ![Enlace 'Ver Kárdex' desde un producto: Kárdex ya filtrado](img/f7a-inventario-kardex-desde-pulso.png)

**La selección se recuerda.** Almacén y categoría/producto elegidos se guardan para su usuario en este mismo
navegador y se restauran la próxima vez que abra Pulso (incluida una recarga de la página); si mientras tanto ese
almacén, categoría o producto se dio de baja o se eliminó, la selección se descarta sola, sin ningún mensaje de
error, y el panel vuelve a "Todos los almacenes" / sin categoría ni producto.

**Permiso.** Se pinta solo con `inventory.view` **y** el módulo **Almacén y lote/serie** (`WMS_LOTSERIAL`)
encendido; sin alguno de los dos, Pulso se ve igual que en F1, sin este panel y sin pedir nada al servidor.

![Sin inventory.view: Pulso no muestra el panel Almacén](img/f7a-pulso-sin-almacen.png)

**Mensajes que puede ver.** Mientras carga cada tarjeta, muestra "…"; si una consulta falla (por ejemplo, un 403 al
cambiar de compañía a una sin permiso), muestra "—" en esa tarjeta en vez de sacarlo de la pantalla de inicio. Si no
puede consultar productos (sin `inventory.view` en una compañía secundaria, caso raro), la sección "Productos" del
control muestra "Su usuario no puede consultar productos." en vez de la lista.

## Pulso del día: panel "Actividad reciente"

**Para qué sirve.** Muestra, en un solo lugar, lo último que pasó en su compañía (recibos, conteos, ajustes,
recolecciones, órdenes de compra, altas y bajas…), agrupado por módulo de negocio, para no tener que ir pantalla por
pantalla a buscarlo.

**Cómo se llega.** Aparece automáticamente debajo del panel "Almacén" en Pulso (`/`).

**Qué se ve.**

![Panel Actividad reciente: pestañas, ventana, interruptor 'Solo obligatorios' y la tabla de eventos](img/f7a-pulso-actividad.png)

Una pestaña por módulo de negocio que usted puede ver (por ahora, solo **Almacén**; Operación y Contabilidad
aparecerán solas cuando su compañía las tenga disponibles), un selector de **ventana de tiempo** (Últimas 24 h /
Últimas 48 h / Hoy), el interruptor **"Solo obligatorios"**, un buscador libre y una tabla con: **Hora** (la de su
propio computador), **Evento** (una píldora de color por familia, con la marca **oblig.** si es un evento que
siempre se muestra), **Referencia** (con enlace a la ficha correspondiente, cuando su usuario tiene permiso para
verla), **Detalle** y **Quién**. Al pie, "N eventos · [ventana]" y el botón **"Ver más"** si hay más por cargar.

**Qué hace cada control.**

- **Pestañas de módulo**: cambian qué eventos se listan; si su compañía o su usuario solo ven un módulo, no hay
  pestañas que elegir (aparece igual, ya en ese módulo).
- **Ventana de tiempo**: acota los eventos a las últimas 24 h, 48 h u "Hoy" (desde la medianoche).
- **"Solo obligatorios"**: oculta los eventos opcionales (por ejemplo, "Recolección creada") y deja solo los que la
  plataforma considera que siempre deben verse (por ejemplo, un ajuste de inventario).

  ![Con 'Solo obligatorios' activo: solo queda el ajuste de inventario, marcado 'oblig.'](img/f7a-pulso-actividad-obligatorios.png)
- **Buscador**: filtra lo que ya está cargado en pantalla (no vuelve a preguntar al servidor); busca en el evento, la
  referencia, el detalle y quién lo hizo.
- **Encabezados de columna** (Hora, Evento, Referencia, Quién): ordenan la tabla; por defecto viene ordenada por
  Hora, la más reciente arriba.
- **"Ver más"**: pide la siguiente página de eventos y la agrega debajo de la ya cargada.
- **Referencia** (enlace): abre la ficha del recibo, la orden de compra, el conteo cíclico, la recolección, el
  producto o el almacén según corresponda; si su usuario no tiene permiso para esa ficha, el texto se ve pero sin
  enlace. Los ajustes y transferencias de inventario no llevan enlace (no hay una ficha de "movimiento" a la que
  ir).

**Idioma.** Los textos de los eventos (nombre, detalle) los traduce el servidor según el idioma que tenga elegido;
cambiar de idioma con el selector de la cabecera no recarga la página ni pierde la ventana de tiempo elegida.

![El mismo panel en inglés, tras cambiar el idioma sin recargar](img/f7a-pulso-actividad-ingles.png)

**Permiso.** Se pinta solo con `analytics.view` **y** el módulo **Análisis** (`ANALYTICS`) encendido; si su
compañía o su usuario no ven ningún módulo de negocio (por ejemplo, sin `inventory.view` para Almacén), el panel
directamente no aparece.

**Mensajes que puede ver.**

- Mientras carga: un indicador de carga con el título y subtítulo del panel.
- Sin ningún evento en la ventana elegida: **"Sin actividad en esta ventana"**.
- El buscador sin ninguna coincidencia: **"Sin resultados"** (el genérico de las tablas del sistema).
- Un error al consultar (por ejemplo, de conexión): el mensaje del servidor en el lugar de la tabla.

## En el celular (360 px)

Las tarjetas del panel Almacén se apilan en una sola columna y la tabla de Actividad reciente se ve como una lista
de tarjetas (una por evento), sin perder ninguna columna ni desbordar la pantalla; el control "Categoría o producto"
ocupa todo el ancho y su lista, al abrirse, también cabe sin desbordar.

![Pulso en un celular: panel Almacén y Actividad reciente en tarjetas](img/f7a-pulso-movil.png)

## Preguntas frecuentes de este capítulo

**¿Por qué "Bajo mínimo" dice "Sí" en vez de un número?** Porque eligió un producto puntual en el filtro "Categoría o
producto": para un solo producto la pregunta es sí/no está bajo su mínimo, no una cantidad. Quite la selección (✕)
para volver a ver la cantidad de productos bajo mínimo de todo el almacén (o de la categoría elegida).

**Elegí un almacén y una categoría, y al entrar a Inventario desde "Ver en Inventario" veo más filtros aplicados
de los que esperaba, ¿por qué?** El enlace lleva los mismos filtros que la tarjeta (almacén y categoría o producto),
para que lo que vea en Inventario cuadre exactamente con la cifra que traía Pulso.

**¿Por qué al recargar la página mi almacén o mi producto elegido desaparecieron?** Puede ser que ese almacén, esa
categoría o ese producto se haya dado de baja o eliminado mientras tanto: la selección guardada se limpia sola, sin
avisar, y el panel vuelve a "Todos los almacenes" sin categoría ni producto.

**¿Por qué no veo la pestaña "Operación" o "Contabilidad" en Actividad reciente?** Esos módulos todavía no están
disponibles (llegan en lotes posteriores); solo aparecen las pestañas de los módulos que su compañía tiene
encendidos y que usted puede ver.

**¿Qué quiere decir la marca "oblig." junto a un evento?** Que es un evento que la plataforma considera que siempre
debe verse en Actividad reciente, aunque tenga activado "Solo obligatorios"; el resto de los eventos se puede ocultar
con ese interruptor.

**Busqué algo en el buscador de Actividad reciente y no encontré un evento que sé que pasó, ¿por qué?** El buscador
solo revisa lo que ya está cargado en pantalla (según la ventana de tiempo y la pestaña elegidas); si el evento pasó
fuera de esa ventana, cambie a "48 h" o cargue más con "Ver más" antes de buscar.
