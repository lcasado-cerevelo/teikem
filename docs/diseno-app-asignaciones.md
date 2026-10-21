# Diseño — Recibir, Acomodar y Despachar en la app (propuesta, 2026-10-21)

Estado: **propuesta para aprobación de Luis. Nada de esto está construido todavía.** Cada pantalla se diseña por separado; no se asume que una sea como otra.

## 1. Recibir
**Pantalla de entrada**
1. Al tocar **Recibir**, arriba: el campo del **número del aviso/orden** (escanear o escribir), el botón del teclado y **Aceptar**. Los botones de modo **no** aparecen aquí.
2. Debajo, la lista de **avisos de llegada y órdenes de compra asignados a mí**.
3. Debajo, **«Ver lo que nadie tiene asignado (N)»**: despliega lo que no tiene dueño. Ahí se escoge **uno o varios** («Seleccionar varios»).

**Pantalla de recibo (una sola; lo de abajo cambia, sin pantallas nuevas)**
1. Tras escoger uno o varios (o escanear), arriba quedan **Con acomodo / Directo a posición**. Lo de abajo cambia según lo que se escoja.
2. Debajo: **producto** — campo para escanear o escribir, botón del teclado y **Aceptar**. Solo se pueden escanear, y solo salen en «Buscar en lista», los **productos del documento (o de los documentos) que se trabajan**.
3. Al aceptar el producto se abre el paso de **posición** en la misma pantalla: campo para escanear o escribir la posición, teclado, **Aceptar** y **«Buscar en lista»**.
   - **Con acomodo:** la lista ofrece solo las posiciones de **staging** (recepción).
   - **Directo a posición:** la lista ofrece **todas las posiciones menos las de staging**, y se escogen **una o varias**.
4. Cada posición escogida se agrega a un **listado** con un campo de **cantidad** (lo que llegó ahí). Si se escribe la cantidad en el **primer** campo y los demás están en blanco, **los demás se llenan solos con ese mismo número**; cada uno se puede cambiar a mano.
5. Abajo, siempre, un **resumen: esperado vs. recibido vs. fallado** (dañado).
6. Varios documentos del mismo dueño y almacén: carga automática y recepción maestra; de dueños distintos: recibos separados. «Recibo ciego» al final si la compañía lo permite.
7. Empezar a recibir algo sin dueño lo asigna a quien recibe (la bitácora dice por qué).

## 2. Acomodar
1. Empieza como hoy: la lista de **acomodos asignados a mí**.
2. Debajo, **«Ver lo que nadie tiene asignado (N)»** (como en Recibir y en Mis tareas). Al tocarlo salen **todos los acomodos sin asignar** y se escoge el que sea.
3. Al escoger uno, **queda asignado a mí** y se abre como cualquier acomodo (la bitácora dice por qué).

## 3. Despacho
**Pantalla 1 — escoger documento**
1. Arriba: el campo del **número del aviso de salida o de la orden de venta** (escanear o escribir), teclado y **Aceptar**.
2. Debajo, los **avisos de salida y órdenes de venta asignados a mí**.
3. Debajo, **«Ver lo que nadie tiene asignado (N)»**, que despliega lo que no tiene dueño.
4. En esa lista hay **«Seleccionar varios»**; al marcar varios aparece **Continuar** y se pasa a la pantalla 2. Con un solo documento también se continúa.

**Pantalla 2 — sacar producto por producto** (un solo campo activo a la vez)
1. **Producto:** campo para escanear o escribir, botón del teclado y **Aceptar**. Escondido, aún sin mostrar, el paso de posición.
2. Al aceptar el producto, el campo del producto se **deshabilita** y se abre **Posición de donde lo saco**: muestra la **sugerida** (o la **obligatoria** si el lote la exige), con **«Buscar en lista»**, campo con teclado y **Aceptar**.
3. Al aceptar la posición se abre la **Cantidad**, con **Aceptar** y **Volver** (para atrás).
4. Al aceptar la cantidad se oculta todo lo demás y queda **solo el campo del producto**, listo para el siguiente.
5. Con varios documentos, las líneas de todos se atienden en una sola ruta y se manda **una recolección maestra** (mismo cliente, como hoy). Empezar un documento sin dueño lo asigna a quien despacha.

## 4. Mis tareas
No se toca en este diseño (se queda como está: Recibir, Acomodar, Despachar y «ver lo que nadie tiene asignado»).

## 5. Pendiente de definir (permisos)
Luis pidió dos permisos: **«ver todo»** (lo asignado a otras personas además de lo mío y lo sin asignar, para el supervisor del almacén) y **«reasignar desde el aparato»** (exige el de ver todo). Falta definir **dónde** se ve cada uno en estas pantallas y qué pasa con `warehouse.pick.any`; se diseña aparte, después de aprobar lo anterior.

## 6. Preguntas
- **D1.** Si el almacén tiene un modo fijo (Con acomodo o Directo), ¿se salta la pregunta de modo en Recibir?
- **D4.** Recibir con acomodo: ¿también se pueden escoger varias posiciones de staging con su cantidad, o una sola (la de staging por omisión de la compañía, ya rellena)?
- **D5.** El número que se copia a los campos en blanco es la **cantidad por posición** (no se reparte el total entre ellas). ¿Correcto?
- **D2.** En Despacho, ¿«Buscar en lista» abre una lista de las posiciones con existencia de ese producto (y lote), igual que hoy?
- **D3.** En Despacho con varios documentos, ¿el campo de producto sirve para cualquiera de los documentos marcados (la app asigna la línea que toca por orden de selección)?
