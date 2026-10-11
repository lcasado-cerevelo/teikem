# Diseño PENDIENTE — Ajustar en el menú y quitar Transferir del menú (app del almacén)

> **Estado: SOLO DISEÑO. No implementado (2026-10-11).** El dueño lo pospone hasta que lleguen a `Depot-Implementation` los cherry-pick de otra sesión que está
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
