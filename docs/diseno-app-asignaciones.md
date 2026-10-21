# Diseño — Recibir, Acomodar y Despachar en la app (propuesta, 2026-10-21)

Estado: **propuesta para aprobación de Luis. Nada de esto está construido todavía.** Cada pantalla se diseña por separado; no se asume que una sea como otra.

## 1. Recibir
**Pantalla de entrada**
1. Al tocar **Recibir**, arriba: el campo del **número del aviso/orden** (escanear o escribir), el botón del teclado y **Aceptar**. Los botones de modo **no** aparecen aquí.
2. Debajo, la lista de **avisos de llegada y órdenes de compra asignados a mí**.
3. Debajo, **«Ver lo que nadie tiene asignado (N)»**: despliega lo que no tiene dueño. Ahí se escoge **uno o varios** («Seleccionar varios»).

**Pantalla de recibo (una sola; lo de abajo cambia, sin pantallas nuevas)**
1. Tras escoger uno o varios (o escanear), arriba quedan **Con acomodo / Directo a posición**. Lo de abajo cambia según lo que se escoja. **Si el almacén tiene un modo fijo, esos dos botones no se muestran** y se enseña directamente lo que corresponde a ese modo.
2. Debajo: **producto** — campo para escanear o escribir, botón del teclado y **Aceptar**. Solo se pueden escanear, y solo salen en «Buscar en lista», los **productos del documento (o de los documentos) que se trabajan**.
3. Al aceptar el producto se abre el paso de **posición** en la misma pantalla: campo para escanear o escribir la posición, teclado, **Aceptar** y **«Buscar en lista»**.
   - **Con acomodo:** **una sola** posición de staging, la **de staging por omisión ya rellena** (se puede cambiar con «Buscar en lista», que ofrece solo las de staging).
   - **Directo a posición:** la lista ofrece **todas las posiciones menos las de staging**, y se escogen **una o varias**.
4. **Directo a posición:** cada posición escogida se agrega a un **listado** con un campo de **cantidad por posición**. Si se escribe la cantidad en el **primer** campo y los demás están en blanco, **los demás se llenan solos con ese mismo número**, sin repartir el total; **la última lleva lo que sobre** (18 en 4 posiciones con 5 en la primera: 5, 5, 5 y 3). Cada campo se puede cambiar a mano.
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

## 5. Permisos: «ver todo» y «reasignar» en el aparato (propuesta)

**Dos permisos nuevos**, categoría Almacén, que se dan al **supervisor del almacén**:

| Código | Nombre | Para qué |
|---|---|---|
| `warehouse.app.viewall` | Ver todo en el aparato | Muestra lo **asignado a otras personas**, además de lo mío y lo sin asignar. |
| `warehouse.app.reassign` | Reasignar desde el aparato | Permite cambiar a quién está asignado un documento o acomodo. **Exige `warehouse.app.viewall`**: sin él, el botón no aparece y el servidor lo rechaza. |

### Dónde se ve cada uno (por pantalla)
- **Recibir:** tras «Ver lo que nadie tiene asignado (N)» aparece, solo con *ver todo*, **«Ver lo asignado a otras personas (N)»**; cada fila dice «Asignado a: {nombre}». Se puede abrir y recibir uno de esos (como hoy: la asignación ordena, no bloquea) y, con *reasignar*, cada fila (mía, sin asignar o de otra persona) trae el botón **Reasignar**.
- **Acomodar:** igual: tras la opción de los sin asignar, **«Ver lo asignado a otras personas (N)»**, con nombre; Reasignar por fila.
- **Despacho:** igual, en la primera pantalla, para avisos de salida y órdenes de venta; Reasignar por fila.
- **Mis tareas:** igual: tercera opción y Reasignar por fila.
- **Reasignar** abre una ventana con las personas de la compañía (activas) y la opción **«Sin asignar»**; al aceptar se manda al servidor. **Pide señal**: sin señal avisa «Para reasignar hace falta señal» y no cambia nada.
- Quien no tiene *ver todo* **no ve** nada de esto: sus pantallas son las ya diseñadas (mío → sin asignar).

### Servidor
- Los endpoints `assign` (aviso de llegada, aviso de salida, orden de compra, orden de venta, tarea) hoy exigen `warehouse.manage`; pasan a aceptar `warehouse.manage` **o** `warehouse.app.reassign` (la política ya admite «a|b»); entrando por *reasignar*, el servicio comprueba además *ver todo* (403 «Para reasignar hace falta poder ver todo en el aparato.»).
- Nuevo `GET /api/v1/warehouse/assignees` (miembros activos: id y nombre) con `viewall` **o** `reassign`; el aparato guarda la lista para mostrar «Asignado a: {nombre}» sin señal (los documentos locales solo traen el id).
- Bitácora: `$reason` «Reasignado por {nombre} desde la app móvil» (o «Dejado sin asignar por…»).
- Los permisos se agregan al `PermissionCatalog`, y en una sección nueva de `Diseño/logistica-db-update.sql` se dan, **solo agregando**, a las plantillas y a los roles **ya clonados** que tengan `warehouse.manage` (los supervisores). El operador no los recibe.

### Qué pasa con `warehouse.pick.any` (decisión pendiente)
Hoy lo tienen todos los operadores (plantilla) y decide si el aparato ofrece en Despacho todo lo del almacén. Con este diseño **lo sin asignar lo ve cualquiera** y **lo de otros lo ve quien tenga *ver todo***, así que `pick.any` queda sin uso. Propuesta: el aparato **deja de consultarlo** (no se quita del catálogo ni de los roles: regla «solo agregar»). Efecto para los operadores: pasan de ver todo el despacho del almacén a ver lo suyo y lo sin asignar.

## 6. Preguntas
- **P1.** ¿`pick.any` queda sin uso (el aparato deja de consultarlo) como se propone? Los operadores verían menos en Despacho que hoy.
- **D6.** En Recibir directo, ¿la **cantidad total recibida** del producto se escribe antes de escoger las posiciones (para saber cuánto sobra en la última)? Con acomodo, esa cantidad total va a la posición de staging.

## 7. Decisiones ya tomadas por Luis
- Despacho: «Buscar en lista» abre las posiciones con existencia del producto y lote, como hoy.
- Despacho con varios documentos: el producto sirve para cualquiera; las líneas se toman en el orden en que se marcaron.

## 8. Decisiones de Luis sobre permisos (2026-10-21)
- Reasignar algo que otra persona ya empezó: **se permite** (no se interrumpe lo que está en curso).
- «Asignado a otras personas» muestra **solo el almacén activo**.
- **No** se notifica a la persona al reasignar.
- `warehouse.pick.any`: Luis quiere que el aparato **siga consultándolo**; falta definir qué controla exactamente (ver P1).
