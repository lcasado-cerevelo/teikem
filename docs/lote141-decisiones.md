# Lote 141 — Avisos de inventario (pedido de Luis, 2026-10-20)

## Qué se construyó
- **Estructura de eventos de inventario** (`InventoryEventProvider`, igual que Ventas e Integraciones): `INVENTORY_LOT_EXPIRING`, `INVENTORY_LOT_EXPIRED`, `INVENTORY_STOCK_LOW`, `INVENTORY_STOCK_OUT`, `INVENTORY_NO_MOVEMENT`. Módulo `WMS_LOTSERIAL`, opcionales (silenciables), sin reglas sembradas.
- **`InventoryAlertService`** (la revisión) y **trabajo diario `INVENTORY_ALERT_SCAN`** (6:00 a. m. hora de la compañía): un aviso por evento con el conjunto completo; clave de no repetición = evento + semana ISO + huella del conjunto (aviso nuevo si cambia el conjunto, recordatorio semanal si sigue vivo).
- **Reglas puras** en `InventoryAlertRules` (vencido / por vencer / bajo mínimo / sin existencia / sin movimiento / clave / texto de renglones).
- **Dos ajustes de la compañía** (`Tenant.InventoryExpiryWarnDays` 30 y `InventoryNoMovementDays` 90) en `PUT/GET /tenant/settings`, sección nueva al final de `Diseño/logistica-db-update.sql` (idempotente, con CHECK), y panel «Avisos de inventario» en Ajustes → Operación.
- Para agregar otro aviso de inventario: un código, su definición y su detección; el motor no cambia.

## Cómo se probó
- xunit `InventoryAlertTests` (25): reglas puras con sus bordes, catálogo (variables conocidas, módulo, opcionales), trabajo (diario 6:00, activo) y la revisión sobre datos reales (vencidos vs por vencer, lote vencido sin existencia ignorado, no repetir, aviso nuevo si entra otro lote, ventana configurable, bajo mínimo con producto inactivo, sin existencia exige movimiento previo, sin movimiento) + prueba de contrato de `TenantSettings*`.
- vitest `TenantSettingsPage.test.tsx` (panel nuevo: valores por omisión, validación y cuerpo del PUT), `npm run check` completo.

## Decisiones a revisar
1. **Un aviso por evento con el conjunto**, no uno por lote (evita inundar). El texto lleva hasta 10 renglones y «… +N».
2. **Recordatorio semanal** mientras el problema siga vivo; si prefieres solo avisar cambios, se quita la semana de la clave.
3. **Saldo bajo** usa el disponible total del producto (todos los almacenes), el mismo cálculo de «bajo mínimo» de la lista de productos.
4. **Sin existencia** exige que el producto haya tenido movimientos (un producto nuevo no avisa); **sin movimiento** usa el último movimiento del Kárdex.
5. No hay tarjeta en «Necesita tu atención» ni indicador del Pulso todavía (los eventos ya existen para armarlos).

## Qué quedó fuera
- Más eventos (exceso de inventario, conteo con diferencias, recibo atrasado…): se agregan sobre esta estructura.
- Avisos al cliente dueño del producto (portal de almacén): lote aparte.
