# Ajuste múltiple: poner al día el inventario por producto y posición (diseño, 2026-10-12)

> **Estado: CONSTRUIDO (2026-10-12) en `Depot-Implementation`** (commits `[ajuste-multiple]`). Pedido de Luis, a partir de lo que pidió el supervisor del almacén de Advance Depot. Decisiones en la sección 12; lo construido y lo que quedó distinto en la sección 13.

## 1. Por qué
En el almacén de Depot hoy todo se trabaja por **ajuste**: no se registra cuándo entró ni cuándo salió algo. Luis quiere cambiar eso y que se usen recibo y despacho, pero mientras se ponen al día, el supervisor pidió que el ajuste permita **varias posiciones a la vez**. Dani hace el inventario **semanal** y ajusta para sacar el reporte de inventario.

Dos intenciones que hay que sostener a la vez:
- **Facilitar** la puesta al día de Dani (muchos productos, muchas posiciones).
- **No** facilitar el ajuste para todo el mundo ni para siempre: el objetivo sigue siendo que dejen de usar ajustes y pasen a recibo y despacho. Por eso es una pantalla aparte y con un **permiso propio** que el administrador asigna (y quita) a quien quiera desde la pantalla de Roles. **El ajuste normal no se toca.**

Descartado (y por qué): el desplegable de posición del ajuste normal con selección múltiple y «la misma cantidad a cada posición». Las posiciones casi nunca tienen la misma cantidad (se llegaría a negativos o a ajustes falsos) y haría más cómodo el ajuste para todos para siempre. La alternativa que sigue vigente para el inventario masivo es el **conteo cíclico** (cada posición con su cantidad real, con cierre automático de los que cuadran), que es el camino formal.

## 2. Cómo es hoy (lo que se reutiliza)
- El ajuste normal (`POST /inventory/adjustments`, permiso `inventory.adjust`, `InventoryAdjustmentService.AdjustAsync`) registra **un movimiento tipo Ajuste por producto y posición** (y lote si aplica), con cantidad **con signo**, **motivo** del catálogo `AdjustmentReason` y **nota obligatoria** (máx. 300). Los motivos manuales son Dañado, Pérdida, Encontrado, Vencido y Otro; RECEIPT_VARIANCE, COUNT_VARIANCE, PICK_BATCH_REVERSAL, OPENING_BALANCE y TRACKING_CONVERSION los asigna el sistema (el ajuste normal los rechaza con 400).
- Los movimientos pueden llevar un **documento de origen** (`RefEntityType` + `RefId`); el Kárdex lo muestra como «origen legible» (`RefLabel`, p. ej. «Pick batch EMP-00777») y abre el documento. Los ajustes de un **conteo cíclico** ya apuntan al conteo como origen.
- Precedente directo para el diseño: el **Despacho manual (DMA-#####)** — documento con cabecera, numeración por `NumberSequence` (Kind `MANUALISSUE`), motivo de catálogo y salida de inventario por el libro (`PickBatchService`).
- El Pulso/Actividad cuenta un ajuste como `INVENTORY_ADJUSTED` si su motivo está en `ActivityRules.ManualAdjustmentReasons` o si no lleva documento de origen.
- Hay `Idempotency-Key` en escrituras (el doble clic no duplica) y auditoría de entidades.

## 3. Qué se construye (resumen)
Un botón **«Ajuste múltiple»** en *Productos e inventario*, con **permiso propio** (el botón solo lo ve quien tenga ese permiso), que abre una pantalla donde se:
1. escoge el **almacén**,
2. se agregan **varios productos**; de cada uno aparecen **todas las posiciones donde hay existencia**, con la cantidad del sistema,
3. se escribe, por posición, **lo que hay ahora mismo**; el sistema calcula si sube o baja,
4. se aplica **todo junto** con **un motivo («Inventario físico») y una nota**.

Cada línea se registra **como hoy**: un movimiento de tipo Ajuste por producto y posición (y lote), con la misma validación del ajuste normal. Lo nuevo es que todos llevan el **mismo documento de origen «Ajuste múltiple AJM-######»**.

## 4. La pantalla
- **Almacén** (obligatorio, uno por documento: las posiciones pertenecen a un almacén; el inventario semanal se hace almacén por almacén).
- **Agregar producto**: buscador por SKU o nombre, con selección de **varios** (como los filtros). Cada producto agrega un bloque con sus posiciones con existencia (en ese almacén):

| Posición | Lote (si aplica) | En sistema | **Hay ahora** | Diferencia |
|---|---|---|---|---|
| A-01-03 | | 12 | *(editable)* | −3 (rojo) / +4 (verde) / «=» |

- **«Hay ahora» vacío = no se toca esa línea.** Si el valor coincide con el sistema, tampoco genera movimiento. Solo las líneas con diferencia crean movimientos.
- **Agregar posición**: botón por producto para sumar una posición donde el sistema no tiene nada (se encontró mercancía en un lugar vacío); selector de posiciones del almacén.
- **Quitar producto** del documento; recorrer con Tab/Enter de una cantidad a la siguiente.
- **Pie fijo:** motivo (fijo: «Inventario físico»), **nota obligatoria** (máx. 300) para todo el documento, y un resumen vivo («12 líneas · +40 · −65 unidades · 5 productos»). Botón **«Aplicar ajuste»** con confirmación que muestra ese resumen.
- Productos con **serie**: no entran (contar series por cantidad no tiene sentido); se ajustan con el ajuste normal. Productos con **lote**: una fila por posición y lote.

## 5. Reglas al aplicar (todo o nada)
- **Una sola transacción**: si una línea falla, **no se aplica ninguna**; la respuesta indica qué líneas fallaron y la pantalla las marca.
- **La diferencia se calcula en el servidor, al guardar**, contra el saldo de ese momento (con los saldos bloqueados), no con el número que se vio al abrir. Cada línea manda la cantidad del sistema que vio la pantalla; si cambió (entró o salió mercancía mientras se contaba), se **rechaza con 409** listando las líneas cambiadas; la pantalla actualiza «En sistema» y la diferencia y pide confirmar de nuevo. Nunca se aplica en silencio sobre un saldo distinto.
- Cada línea pasa por **las mismas validaciones del ajuste normal**: producto y posición válidos del tenant y del almacén, cantidad dentro de la unidad de medida, la existencia final no puede quedar **negativa ni por debajo de lo reservado** en esa posición, reglas de tipo de zona/posición.
- Sin líneas con diferencia → no se crea documento.
- Idempotente (`Idempotency-Key`): reenviar la misma petición no duplica el documento.

## 6. Cómo se registra y se identifica
- **Movimientos:** exactamente como hoy (tipo `ADJUSTMENT`, uno por producto/posición/lote, con cantidad con signo, nota y usuario). Se sigue viendo todo en saldos, Kárdex, reportes y analítica sin tocarlos. **No se crea un tipo de movimiento nuevo.**
- **Origen:** `RefEntityType = MULTI_ADJUSTMENT`, `RefId = id del documento`. En el Kárdex el origen se lee «**Ajuste múltiple AJM-000123**» y abre el documento. Se registra el tipo de origen en la resolución de números de documento del Kárdex (`RefLabel`/`RefNumbers`), como el resto de los encabezados.
- **Motivo nuevo:** `PHYSICAL_COUNT`, «Inventario físico» / «Physical inventory», en el catálogo `AdjustmentReason`. Lo **asigna el sistema** desde esta pantalla: el ajuste normal lo rechaza con 400 (igual que COUNT_VARIANCE). Va en `ActivityRules.ManualAdjustmentReasons` (o la regla equivalente) para que **siga contando como ajuste en el Pulso y la Actividad** aunque lleve documento de origen.
- **Documento (cabecera)** `MultiAdjustment`: número `AJM-######` (nueva clase de `NumberSequence`: `MULTIADJUST`; requiere ajustar el CHECK de `NumberSequence.Kind`, como se hizo con `MANUALISSUE`), almacén, usuario, fecha, nota, cantidad de líneas, unidades que suben y que bajan.
- **Líneas del documento** `MultiAdjustmentLine`: producto, posición, lote, **cantidad en sistema al guardar**, **cantidad contada («hay ahora»)**, diferencia y el id del movimiento generado. Así queda lo que Dani contó, no solo el resultado, y el detalle no depende de reconstruir saldos.
- **Auditoría** normal de ambas entidades.

## 7. Dónde se ve
- **Kárdex:** el origen «Ajuste múltiple AJM-…» (clic abre el documento); **filtro nuevo por tipo de origen** y, en el reporte de ajustes, columna **«Origen»**. Así se ve cuánto se movió por ajuste múltiple y cuánto por ajuste normal.
- **Pestaña «Historial»** de la pantalla: pasadas anteriores (fecha, usuario, almacén, líneas, unidades) con filtros; el detalle muestra cada línea con *En sistema / Hay ahora / Diferencia*. Se puede exportar.
- El **Reporte de ajustes** que ya existe en *Productos e inventario* incluye estos movimientos (son ajustes), separables por motivo «Inventario físico» y por origen.

## 8. Permiso (sin interruptor aparte)
- **Un permiso nuevo** para ver y aplicar el ajuste múltiple (propuesto: `inventory.adjust.multi`, grupo WAREHOUSE, «Ajuste múltiple de inventario»). Se registra en el catálogo de permisos y **no** va en ningún rol por defecto.
- **El permiso es el encendido y el apagado.** El administrador de la compañía (o el de plataforma) entra a la aplicación, a **Roles**, y le asigna el permiso al rol que quiera; para apagarlo para alguien, se lo quita. Sin el permiso el botón no aparece y el API rechaza la escritura (403). **No hay un interruptor aparte**: no se agrega ninguna columna a la compañía ni un endpoint de plataforma. (En una versión anterior de este diseño había un interruptor por compañía que solo cambiaba el administrador de plataforma; se descartó por innecesario.)
- Asignar o quitar el permiso ya exige MFA reciente y queda en la bitácora de seguridad (cambio de rol/permisos), como cualquier otro permiso.
- No se crea un rol nuevo en esta entrega: Luis asigna el permiso al rol que corresponda.

## 9. Qué queda fuera de esta fase
- Productos con **serie**.
- **Reversa** de un ajuste múltiple (se corrige con otro ajuste múltiple; el historial lo deja a la vista).
- **App del aparato** (solo web).
- **Pegar desde Excel** / carga por archivo: **descartado por Luis** (2026-10-12).
- **Reporte de uso** («ajustes por usuario y por día»): **descartado por Luis** (2026-10-12).
- Varios almacenes en un mismo documento.

## 10. Piezas a construir (cuando se apruebe)
1. **Base de datos** (sección nueva y idempotente al final de `Diseño/logistica-db-update.sql`; estructura y seed están congelados): tablas `MultiAdjustment` y `MultiAdjustmentLine`; clase `MULTIADJUST` en `NumberSequence` (y su CHECK); motivo `PHYSICAL_COUNT` en el catálogo; permiso nuevo y su registro en el catálogo de permisos.
2. **Dominio e infraestructura:** entidades y configuración, `MultiAdjustmentService` (armar el documento, recalcular diferencias contra el saldo actual bloqueado, validar cada línea con las reglas del ajuste normal, aplicar los movimientos en una transacción y numerar), tipo de origen `MULTI_ADJUSTMENT` y su resolución en el Kárdex, motivo `PHYSICAL_COUNT` en las reglas de actividad y como motivo de sistema.
3. **API:** `GET` de posiciones con existencia por producto y almacén (para llenar la pantalla), `POST` del documento (409 con las líneas cambiadas), `GET` historial y detalle; permiso `inventory.adjust.multi` (en todos los endpoints); contratos en `openapi.json` y `schema.d.ts`.
4. **Web:** botón y pantalla (agregar productos, bloques con posiciones, «Hay ahora», diferencia, agregar posición, pie con motivo/nota/resumen, confirmación), pestaña Historial, filtro de origen y columna «Origen» en Kárdex y reporte de ajustes; textos es/en.
5. **Pruebas:** servicio (todo o nada, saldo que cambió → 409, no bajar de lo reservado ni negativo, líneas sin diferencia, lote, serie excluida, idempotencia, numeración), permisos (sin el permiso: 403 y el botón no aparece), que el ajuste normal rechace el motivo `PHYSICAL_COUNT`, que siga contando en el Pulso, Kárdex con el origen, y pantalla (cálculo de diferencias, resumen, errores por línea).
6. **Manual, FAQ y decisiones:** capítulo de inventario, y este documento pasa a «decisiones».

## 11. Ramas y entrega
Igual que la IP preferida: en **`Depot-Implementation`** (producción corre de ahí) como **commits pequeños y aislados** (base de datos, servicio, API, web, pruebas), sin el permiso asignado a ningún rol (nadie lo ve hasta que Luis lo asigne); se prueba en **staging** (con la base restaurada de producción) y luego se pasan a `master` con `cherry-pick`, corriendo la suite de master encima. Los puntos donde master también se mueve y puede haber choques: `logistica-db-update.sql` (sección nueva al final), `InventoryController`, `ActivityRules`, `es.json`/`en.json`, `openapi.json`/`schema.d.ts`. No se crean ramas nuevas sin que Luis lo pida.

## 12. Decisiones
**Tomadas (Luis, 2026-10-12):**
1. **Sin interruptor aparte.** El permiso es el encendido y el apagado: lo asigna el administrador de la compañía (o de plataforma) a los roles desde la pantalla de Roles de la aplicación (sección 8). No se usa el mecanismo de módulos ni se agrega campo ni endpoint de plataforma.
2. **Sin tope de líneas por documento.** Si en las pruebas un documento muy grande resulta lento, se vuelve a ver entonces.
3. **Sin «pegar desde Excel».** Descartado.
4. **Sin reporte de uso.** Descartado.
5. **El permiso lo asigna Luis**, desde Roles, al rol que corresponda; no se crea un rol nuevo.

**Abiertas:** ninguna por ahora.

## 13. Lo construido
- **Base de datos** (sección al final de `logistica-db-update.sql`): `MultiAdjustment`, `MultiAdjustmentLine`, contador `MULTIADJUST` (CHECK ampliado), motivo `PHYSICAL_COUNT`, tipo de origen `MULTI_ADJUSTMENT` y el permiso `inventory.adjust.multi` (sin asignar a ningún rol).
- **API** (`MultiAdjustmentsController`, `api/v1/inventory/multi-adjustments`, todo con `inventory.adjust.multi`): `GET stock`, `POST` (todo o nada; 409 `stale_system_qty` con `lines[i]`; por debajo de lo reservado 409 `insufficient_stock`), `GET` historial, `GET {publicId}` y `GET by-id/{id}`. `MultiAdjustmentService` bloquea los saldos en el orden del ledger, recalcula con el saldo bloqueado y registra un movimiento Ajuste por línea con diferencia, con el documento como origen. Kárdex: el origen se lee «Ajuste múltiple AJM-…» y el detalle del movimiento muestra el documento.
- **Web** (`MultiAdjustScreen`, ruta `/warehouse/multi-adjustment`): botón **Ajuste múltiple** en *Transferencias y ajustes*, después de «Ajustar» (decisión de Luis; no va en Productos e inventario). Nuevo ajuste (bloques por producto, «Hay ahora», diferencia, agregar posición, pie con nota y resumen, confirmación) e Historial con detalle.
- **Diferencias con el diseño:** el filtro por motivo «Inventario físico» (que ya existe en la lista de ajustes) hace de filtro de origen y la columna «Origen» ya existía; no se agregó un filtro nuevo por tipo de origen. El historial no tiene filtros propios todavía ni exportación.
- **Pruebas:** 16 del servicio y las reglas (InMemory con el ledger real) y 9 de la lógica de la pantalla; se actualizaron los conteos fijos de permisos, auditoría y aislamiento por tenant.
