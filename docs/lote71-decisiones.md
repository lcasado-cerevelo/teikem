# Lote 71 — Kits en el aparato (P4-T, 4 de 4) (2026-10-16)

Cierra los kits: 68 catálogo, 69 órdenes de venta, 70 recolección, 71 aparato.

## Qué se construyó
- **Servidor:** `GET /sync/sales-orders` manda las líneas de kit con `isKit`, `components` (piezas por kit) y, por reserva, el componente (`productPublicId`, `sku`, `productName`).
- **Aparato (esquema 15):** `sales_order_line.is_kit`, `sales_order_component` y los datos del componente en cada reserva. Cada reserva de un componente es una tarea del despacho guiado; al terminar se manda **una línea por kit** con los kits completos (menor cociente recogido ÷ piezas por kit), sin posición ni lote.
- Efecto local inmediato: la existencia de cada componente baja por lo que sale de sus reservas (mismo orden que el servidor) y la orden descuenta los kits; se deshace si el servidor rechaza; se vuelve a reflejar sobre la copia que llega.
- Aviso y bloqueo si lo recogido no completa ningún kit. Varias reservas iguales (producto, posición, lote) suman en una tarea (también para productos normales).
- Manual 09 §12.1, 18 §17 y FAQ.

## Pruebas
- xunit: el sync con líneas de kit (componentes y producto por reserva; una línea normal no lleva producto). Suite completa verde contra SQL real.
- Jest (app): lógica de kits (tareas, kits completos, cuerpo sin posición ni lote, resumen), copia local (línea de kit, despachar kits, reaplicar pendientes) y la pantalla de punta a punta (recoger pieza por pieza y mandar los kits; bloqueo cuando no completa ninguno). `npm run check` verde.

## Decisiones por omisión (a revisar con Luis)
1. **El aparato manda los kits completos y no escoge posición**: las piezas sobrantes quedan reservadas.
2. **Una pieza de más no se descuenta ni se advierte por pieza**, solo en el resumen de kits completos.
3. Un kit con una línea de otro producto en la misma orden: cada línea se despacha por su camino.
