# Lote 70 — Recolectar kits (P4-T, 3 de 4) (2026-10-16)

## Qué se construyó
- **Explosión del kit al recolectar** (`PickBatchService.ExplodeKitsAsync`): una línea de kit se reemplaza por una línea por componente (cantidad por kit × kits) y sigue el mismo camino de siempre (rotación FEFO/FIFO, un solo dueño, 409 sin efecto parcial). Sirve a `pick-batches`, `collect-and-pack` y el despacho manual.
- **Kárdex** con la nota «KIT × n · EMP/DMA»; las líneas de la recolección anotan `KitProductId` y `KitQuantity` (una sola vez por kit).
- Un kit y un componente suyo en la misma recolección comparten el saldo.
- Manual 06 §8b.5 y FAQ.

## Pruebas
- xunit (4 nuevas): recolectar un kit (componentes, rotación, nota, kits anotados una vez), kit + componente suelto con el mismo saldo, rechazos (posición, decimales) y faltante sin dejar nada, deshacer devuelve todo. Suite completa verde.

## Decisiones por omisión (a revisar con Luis)
1. La pantalla de Recolección **no ofrece kits** en su selector (solo productos con existencia); los kits se usan desde las órdenes de venta o el API. Se puede abrir después si hace falta.
2. En una recolección el kit **no admite posición/lote/series** (sale por rotación de cada componente).

## Sigue
- **Lote 71:** el aparato despacha líneas de kit de las órdenes de venta (hoy el servidor no se las manda).
