# Daño declarado en el recibo (2026-10-08) — decisiones

Continúa `docs/mobile/danos-decisiones.md`. Pedido del dueño: en la pantalla donde se captura la cantidad recibida, una casilla «vinieron unidades dañadas» que muestre, lado a lado, la cantidad dañada y la razón (catálogo con «Otro» para escribirla); la posición donde se deja lo dañado se sugiere como en las demás pantallas (cuarentena si existe; si no, la de lo bueno); y el menú de Daños dentro de «Productos e inventario».

## Qué se construyó
- **Servidor:** `ReceiptLine` gana `DamagedQty`, `DamageCauseLookupId`, `DamageNote`, `DamageBinId`, `DamageDiscard` y `DamageReportId` (SQL con `COL_LENGTH` y `CK_ReceiptLine_Damaged`: dañadas ≤ recibidas). `ReceiptLineRequest`/`ReceiptLineUpdateRequest`/`ReceiptLineDto` llevan el daño (`ClearDamage` lo quita). Al **confirmar** el recibo, `ReceiptService` llama a `DamageService.ReportReceiptLineAsync` por cada línea con daño: crea el `DAN-#####` (origen Recibo, enlazado al recibo) y mueve las unidades desde donde aterrizó la línea (transferencia a la posición indicada o a la primera de cuarentena; si es la misma, sin movimiento; desechar = ajuste negativo motivo Daño). La tarea de acomodo solo cuenta lo bueno. Reglas puras en `DamageRules.ValidateLineDamage`.
- **App:** casilla + cantidad dañada + razón (desplegable `CauseSelect`) + comentario si es «Otra»; paso nuevo «Unidades dañadas» (sugerida tocable, escáner, lista de posiciones, **Desechar**, **Volver**); migración local v7; el daño viaja en la línea de la cola de salida (funciona sin señal).
- **Web:** ícono «Unidades dañadas» en la fila del recibo abierto (modal con cantidad, razón, «Otra», dejar en posición / desechar, «Quitar daño») y chip «3 dañadas · Q-01 · DAN-…»; «Posición donde queda» en el formulario de Reportar daño; **Daños pasa a ser una pestaña de Productos e inventario** (`?tab=damage`, solo con `warehouse.damage`), sin ítem propio en el menú (`/warehouse/damage` redirige).
- El API de Reportar daño admite ahora **cualquier posición activa** como destino (antes solo de zona Cuarentena; el mensaje `La posición {código} no es de una zona de cuarentena.` desaparece y entra `La posición {código} está desactivada.`).

## Cómo se probó
Servidor: `ReceiptDamageTests` (11: cuarentena, desechar, sin cuarentena, posición cualquiera, recibo directo, validaciones, serie, edición/quitar) + 1 de `DamageServiceTests`; 3287 pruebas verdes. App: `receiveDamageScreen` (6), lógica pura de `receiveLogic` y migración; 555 pruebas verdes. Web: `ReceiptLineDamageModal` (4), `damageForm`, navegación y `npm run check` verde. **No se pudo probar contra SQL Server en este contenedor** (la operación sobre la base fue bloqueada): el esquema (`ALTER … ADD` + `CK_ReceiptLine_Damaged`) lo valida el `db-init` del CI. Sin probar en el Zebra.

## Decisiones tomadas (a revisar)
1. **Las dañadas cuentan dentro de lo recibido** (recibí 100, 10 dañadas → recibidas 100, buenas 90). La recepción, la orden de compra y la diferencia contra lo esperado siguen contando las 100; no hay diferencia que netear a mano (corrige la limitación 3 de `danos-decisiones.md` para este camino).
2. **Las unidades dañadas entran con el resto y salen al confirmar** (no se parte el asiento de recepción): el Kárdex muestra la recepción completa y luego la transferencia o el ajuste Daño con la nota `DAN-…`.
3. **Posición por defecto del daño** (cuando no se indica): primera de cuarentena activa por código; si no hay, donde aterrizó la línea (queda «En cuarentena» ahí, fuera del acomodo). La app sugiere lo mismo con las posiciones que tiene guardadas.
4. **Causa del catálogo + «Otra» con texto obligatorio.** El comentario solo viaja con «Otra».
5. **Un reporte por línea con daño.** Con reparto por posición, las dañadas se asignan en orden a las líneas del reparto (una línea no puede llevar más dañadas que recibidas), así que pueden salir varios `DAN-`.
6. **Productos con serie: no** (igual que en Daños).
7. **Cruce de muelle:** si parte de la línea se reparte a cruce de muelle, el reparto no sabe de las dañadas; en esa combinación rara conviene reportar el daño aparte.
8. **Daños en el menú:** pestaña dentro de Productos e inventario; quien no tiene `warehouse.damage` no la ve.
