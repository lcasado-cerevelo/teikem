# Transferir desde el aparato — decisiones (2026-10-10)

## Qué se construyó
- **Servidor**: permiso `warehouse.transfer` (implicado por `inventory.adjust`, plantilla *WarehouseOperator*), `POST /api/v1/inventory/transfers/in-warehouse` (`TransferInWarehouseAsync`): solo dentro del almacén y sin posiciones de cuarentena, en renta ni cross-dock; reutiliza `TransferAsync` (lo reservado no se mueve). Archivo de cambios `Diseño/cambios/0001-permiso-warehouse-transfer.sql` (idempotente; el seed y la estructura siguen congelados).
- **App**: tile *Transferir* (con permiso), pantalla de 5 pasos, botón *Mover* en Consultar (por posición y por producto), `BalanceRow` trae `binId/lotId/zoneTypeCode`; menú principal en el orden Consultar, Transferir, Recibir, Acomodar, Despacho, Conteo, Daño.

## Cómo se probó
`dotnet test` (3304, incl. servicio con cuarentena y otro almacén, mapa de seguridad y conteo de permisos 70), `tsc` y `jest` de la app (568: lógica, tile y orden de Inicio, pantalla de transferencia, «Mover» en Consultar). No se probó contra SQL Server ni en el Zebra.

## Decisiones a revisar
1. **En línea, sin cola** (como Daño): el operario necesita saber ya si se pudo (lo reservado, existencia que cambió). Sin señal no transfiere.
2. **Series**: fuera de esta primera versión (se transfieren desde la web).
3. **Entre almacenes**: fuera (pedido del dueño); el endpoint lo rechaza.
4. El permiso nuevo no requiere tocar el seed: el `PermissionSeeder` lo siembra y propaga a los roles de los tenants al correr `db-update`; `inventory.adjust` lo implica en tiempo de ejecución, así que quien ya ajusta puede transferir sin cambiar roles.
