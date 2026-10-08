# App de almacén — varios almacenes por aparato y compañía (2026-10-07)

## Qué se construyó
- El aparato puede trabajar en **cualquier almacén activo de su compañía**, no solo en el «por defecto» que fija la web.
- `kernel/warehouse/activeWarehouse.ts`: almacén **activo** = el elegido en el aparato (`DeviceIdentity.selectedWarehouse`, guardado en SecureStore junto
  al registro de la compañía) o, si no hay elección, el por defecto. Guarda la lista de almacenes activos en el kv de la compañía (`warehouseOptions`)
  para poder elegir sin señal.
- Inicio muestra «Almacén: <nombre>» y, con más de un almacén, **«Cambiar»** (`WarehousePickerModal`). Al elegir se sincroniza al momento.
- Recibir, Acomodar, Despacho, Conteo, Consultar, el buscador de listas (`ScanField`) y `downloadForReceiving` usan el almacén activo; el modo de
  recepción (directo / con acomodo) sale del almacén activo, no del por defecto.
- Servidor: **sin cambios** (ninguna llamada exigía el almacén por defecto; `GET /warehouses` pide `inventory.view`).

## Cómo se probó
- `activeWarehouse.test.ts` (6): por defecto sin elección, lista solo con activos y legible sin red, elegir/volver al por defecto, almacén desactivado,
  heartbeat que cambia el por defecto (descarta la elección) y heartbeat sin cambio (la conserva).
- `homeWarehouse.test.tsx` (muestra el actual y cambia) y `homeWarehouseLock.test.tsx` (bloqueado con un recibo en curso).
- `npx tsc --noEmit` limpio; `npx jest` 98 suites / 522 pruebas.

## Decisiones a revisar
1. **Bloqueo:** no se cambia de almacén con un recibo, despacho o conteo abierto (mismo criterio de «un documento a la vez»). Las capturas ya en la cola de
   salida no bloquean: cada una lleva su almacén.
2. **La web manda:** si se cambia el almacén por defecto del aparato desde la web, se descarta la elección hecha en el aparato.
3. **Quién ve «Cambiar»:** se apoya en `inventory.view` (el permiso de la lista de almacenes); sin él el aparato queda en su por defecto. No se creó un permiso nuevo.
4. **Sin restricción por almacén asignado a cada usuario:** cualquier usuario del aparato con ese permiso puede elegir cualquier almacén activo de la compañía.
   Si hace falta limitar usuarios a ciertos almacenes, es un lote aparte (modelo de datos nuevo).
5. Los datos locales ya estaban separados por almacén (`warehouse_public_id` en cada tabla), por eso volver a un almacén ya usado no baja todo otra vez.
