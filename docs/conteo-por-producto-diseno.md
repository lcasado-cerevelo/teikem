# Conteo cíclico por producto (app móvil y revisión rápida en la web) — diseño acordado con el dueño

Estado: **diseño aprobado, sin implementar**. Fecha: 2026-10-03. Origen: pedido de Luis tras probar la app. Los números de lote son tentativos.

## Por qué
Las posiciones del almacén no están bien etiquetadas y arreglarlo tomaría semanas; el proyecto no puede esperar. Hoy el conteo arranca escaneando una **posición**. Se necesita poder contar **por producto**: escaneo el producto, la app me lista las posiciones donde el sistema dice que está, yo anoto lo que encuentro en cada una y el sistema calcula la diferencia. Hoy en papel hacen al revés (una hoja con lo de ayer y anotan la diferencia); no es lo que se quiere.

## Lo que NO cambia (mecanismos de integridad)
- La unidad del conteo sigue siendo la línea (posición × producto × lote); el ajuste se calcula contra el saldo **actual** de cada línea al reconciliar (D22) y se asienta por el ledger.
- Reconciliar sigue siendo **todo o nada**, con el permiso `warehouse.count`. Quien solo captura (`warehouse.count.capture`) no ajusta nada.
- Quién ve las cantidades del sistema **no cambia**: sigue dependiendo del permiso `warehouse.count` de quien consulta (conteo a ciegas sin él). La reconciliación siempre muestra lo esperado.
- El servidor ya soporta crear un conteo por producto (`POST /cycle-counts` con `productPublicIds`, sin `binIds`: una línea por cada posición con existencia) y capturar líneas de varias posiciones en un mismo envío (`PUT /cycle-counts/{id}/lines/batch`). La app no lo usa.

## App móvil — "Contar por producto"
1. En Conteo, dos entradas: **por posición** (como hoy) y **por producto**.
2. Se escanea o teclea el producto. La app crea el conteo y muestra una lista: **posición — espacio para la cantidad**, una fila por posición donde el sistema dice que hay existencia. Si el producto lleva lote, cada fila muestra además el **número de lote** (solo se muestra; no hay búsqueda ni captura por lote en esta entrega).
3. **Un espacio en blanco es cero.** El botón Confirmar es el que cierra. Al confirmar, una línea de resumen avisa cuántas posiciones en blanco se toman como 0 y se acepta con el mismo toque (no es un paso extra).
4. **No hay botón "todo aquí".**
5. **Buscador** dentro de la lista: solo aparece cuando hay más de 6 posiciones (ajustable).
6. Enlace **"Otra posición"** al final, para lo hallado donde el sistema no tenía nada: se escoge la zona y se capturan los datos de la posición (código, pasillo, rack, nivel, posición). Si el producto lleva lote, también se pide el número de lote, porque el servidor lo exige para una línea nueva.
   - **Opción A (aprobada):** la posición se crea desde el conteo y queda **marcada "pendiente de revisión"**; el supervisor la confirma, la corrige o la desactiva al revisar. El operario no se detiene. (Hoy crear una posición exige `warehouse.manage`; el rol "Operador de almacén" configurado en Advance ya lo tiene, pero la marca deja el rastro igual.)
7. **Productos con serie:** fuera de alcance. Se cuentan por número de serie (el servidor lo exige) y la app no tiene captura de series. Para no crear un conteo que no se pueda terminar, la entrada por producto responde con un aviso claro y no crea nada. No se hace nada más con series ni con búsqueda por lote en esta entrega.
8. Todo se guarda local y viaja por la cola de salida, igual que el conteo por posición.

## Web — revisión rápida y corrección del supervisor
1. **Lista "Por revisar"** (conteos ya contados): quién contó, producto, cuántas posiciones y cuántas difieren.
2. Botón **"Cerrar los que cuadran"**: cierra de una vez los conteos que no asentarían ningún movimiento. "Cuadra" se mide contra la existencia **actual**, no contra la foto; un conteo cuya línea se movió desde la foto no se cierra solo y queda para revisar. Una línea que coincide no asienta nada; un conteo sin movimientos termina en "Concordancia".
3. Al abrir un conteo con diferencias, por defecto se ven **solo las líneas que fallan**, con la cantidad editable.
4. **Corrección del supervisor** (no es un ajuste ni una transferencia; no mueve inventario por sí misma): si el operario puso 0 donde iba 1 y 1 donde iba 0, el supervisor lo arregla en el mismo paso y queda evidencia en la línea: *Contó X (quién, cuándo) · Corregido a Y (quién, cuándo)*, también visible en el motivo del movimiento que se asiente.
5. **Vista previa del efecto** antes de confirmar, por posición: existencia actual, cantidad contada, ajuste que se asentaría y saldo resultante, con los errores que hoy salen al reconciliar (contar menos de lo reservado). La calcula el mismo código que reconcilia, para no duplicar la regla.

## Cambios de servidor (mínimos)
- `CycleCountLine`: guardar la cantidad **capturada originalmente**, quién y cuándo la capturó, y quién y cuándo la corrigió. Editar una línea de un conteo ya contado conserva la original.
- Endpoint de **vista previa de reconciliación** (sin escribir) que reutilice el cálculo de `ReconcileAsync`.
- Endpoint para **cerrar en bloque los conteos que cuadran** (reconcilia solo los que no asientan nada y devuelve los que quedaron para revisar).
- Posición **provisional**: marca en `WarehouseBin` y alta desde el conteo con el permiso de capturar; el supervisor la revisa.
- Esquema: se edita `Diseño/logistica-db-estructura.sql` (sin migraciones EF); base se recrea.
- Manual funcional, FAQ y documento de decisiones del lote, como exige el `CLAUDE.md`.

## Fuera de alcance de este diseño
- Captura por serie y búsqueda por lote o serie (decisión del dueño: todavía no).
- Convertir el par −1/+1 de un mismo producto en una transferencia (se evaluará con datos reales).
- Cambiar quién ve las cantidades del sistema (se queda como está).
- Pasar los ~25 servicios que cuentan "hoy" en UTC al reloj de la compañía: **lote aparte** (el servicio de conteo ya usa el reloj de la compañía; la web del conteo lo recibe con el proveedor de formatos de la fase 3).

## Lotes propuestos
1. **Lote 19 — Conteo por producto, servidor** (puede empezar ya; solo toca `src/`, `tests/`, SQL y docs).
2. **Lote 19 — web**: revisión rápida, corrección y vista previa (después de la fase 3, que toca `web-app/`).
3. **Lote 19 — app**: "Contar por producto" (toca `app-almacen/`).
4. **Lote 20 — "hoy" en la zona de la compañía** en los servicios restantes.

## Pendiente de decidir
- El rol "Operador de almacén" de Advance tiene `warehouse.count` (y por eso la app le muestra la cantidad esperada al contar). Si se quiere que el operario cuente sin ver, se le quita `warehouse.count` y se deja `warehouse.count.capture`: es configuración del rol, no código.
- Umbral del buscador (6 posiciones).
