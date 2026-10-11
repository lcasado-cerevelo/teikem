# Ajustar en el menú y quitar Transferir del menú (app del almacén) — implementado 2026-10-11

> **Estado: IMPLEMENTADO (2026-10-11).** Estuvo pospuesto hasta que llegaron a `Depot-Implementation` los cherry-pick de otra sesión que estaba
> cambiando la app móvil y la web. **Hasta entonces no se toca código de inventario** (Consultar, Transferir, Ajustar, Daño, menú principal).
> Al retomarlo: partir del código ya con los cherry-pick dentro, y volver a revisar los puntos marcados «verificar».

## Problema
- El menú principal tiene **Transferir** (`/transfer`), pero **Consultar → «Mover»** abre la misma pantalla con la posición de origen y el producto ya puestos. Mismos pasos,
  mismo permiso (`warehouse.transfer`), mismo endpoint (`POST /inventory/transfers/in-warehouse`). El botón del menú es redundante. (Consultar por producto es incluso mejor
  para mover: lista todas las posiciones donde está.)
- **Ajustar no tiene botón en el menú.** Solo se llega desde Consultar → «Ajustar», con `fromBinId`, `fromBinCode` y `productPublicId` ya puestos. Si el sistema dice que de un
  producto hay 0 en una posición y físicamente está ahí, Consultar no lo lista y no hay camino a Ajustar (ni «Mover», que con 0 disponible tampoco sale). Hoy hay que ir a la web.
  El servidor no lo impide: `AdjustQuantityAsync` no exige un saldo previo, y existe el motivo *Encontrado* para subir.

## Cambio propuesto
1. **Quitar el botón Transferir del menú principal** (`app-almacen/src/app/home.tsx`, `home-transfer`). La pantalla `/transfer` y el permiso `warehouse.transfer` se quedan: «Mover» los usa.
2. **Agregar el botón Ajustar al menú**, visible solo con `warehouse.adjust` (el permiso sigue sin venir en ninguna plantilla de rol; lo da el administrador a un rol propio).
   Quedan 7 botones, igual que ahora.
3. **Dos pasos de inicio nuevos en `/adjust`** cuando se entra sin parámetros: (a) escanear la **posición**; (b) escanear el **producto** (y elegir lote si tiene varios).
   De ahí en adelante se reutiliza el flujo actual: **Subir / Bajar** → cantidad → motivo → nota obligatoria → confirmar. Con *Bajar* y existencia 0 la pantalla ya avisa
   «Solo se puede bajar hasta 0».
4. Manual (`09-app-almacen.md`: «Transferir», «Ajustar cantidad», orden del menú) y `faq.md`: actualizar. Es cambio de app, sin cambio de API ni de base.

## Verificar al retomar
- Que subir desde cero en una posición sin saldo previo cree el saldo (servidor: `AdjustAsync`); es lo esperable de un ajuste de entrada, pero no se probó.
- Que Consultar siga mandando las cantidades reales a quien tiene `inventory.view` aunque no vea cantidades en pantalla (`showQty` = `warehouse.count`): hoy «Mover» depende de eso (`/api/v1/inventory/balances`).
- Que los cherry-pick de la otra sesión no hayan cambiado `home.tsx`, `transfer.tsx`, `adjust.tsx` ni `lookup.tsx` de forma que choque con esto.


## Cómo quedó (2026-10-11)
- **Menú** (`app-almacen/src/app/home.tsx`): sale «Transferir»; entra «Ajustar» (`home-adjust`, solo con `warehouse.adjust`) en el segundo lugar. Orden: Consultar, Ajustar, Recibir, Acomodar, Despacho, Conteo y Daño. La pantalla `/transfer` y su permiso se quedan: «Mover» en Consultar los usa.
- **Ajustar** (`app-almacen/src/app/adjust.tsx`): sin parámetros pide la **posición** y luego el **producto**; con parámetros (desde Consultar) funciona como antes. Un producto **sin saldo en la posición** se trata como 0 (se puede subir; bajar da «Solo se puede bajar hasta 0»). Un producto **con lote** sin saldo ahí ofrece los lotes que ya tiene registrados (el API ajusta por `lotId`); sin ninguno, remite a la web. Serie: igual que antes (desde la web). Tras ajustar desde el menú se queda en Ajustar listo para la siguiente posición.
- **Verificado:** subir en una posición sin saldo crea el saldo (lo prueba `InventoryAdjustmentServiceTests.Adjust_plus_and_minus_are_signed_in_the_ledger`, que sube 3 «Encontrado» en una posición vacía) y el aparato lo refleja al instante (`balanceProjection.bump` inserta la fila local). Consultar manda las cantidades reales a quien tiene `inventory.view` (`/inventory/balances`); «Mover» no depende de `showQty`.
- **Pruebas:** `homeAdjustTile.test.tsx` (orden del menú, sin Transferir, lleva al primer paso), `adjustFromMenu.test.tsx` (subir con 0, bajar con 0, posición y producto inexistentes, serie, lote con un lote registrado, lote sin lotes, empezar de nuevo). La suite de la app: 119 archivos, 659 pruebas.
- **Límite conocido:** al escanear una posición **vacía** la app no puede saber si es de cuarentena, en renta o de cross-dock (solo lo sabe por las filas de saldo); en ese caso el **servidor** rechaza el ajuste (422) y queda «requiere revisión» en Sincronización.
