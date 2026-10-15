# Lote 65 — Despachar una orden de venta en el aparato (P4, S2) (2026-10-15)

Cierra la parte de la app del diseño [p4-s2-despacho-diseno.md](p4-s2-despacho-diseno.md); usa el servidor del Lote 64.

## Qué se construyó
- **Datos del aparato (esquema 14):** `sales_order`, `sales_order_line`, `sales_order_reservation`. `downloadSalesOrders` baja `GET /sync/sales-orders` completo (reemplaza la copia del almacén; los 403 se saltan) y se engancha a `downloadForReceiving`.
- **Cola:** tipo `salesDispatch` (ruta de la orden, etiqueta «Despacho de orden de venta» en Sincronización). El efecto en la existencia local se guarda con la fila y se vuelve a sumar en cada bajada de saldos (`pendingDeltas`) y se deshace si el servidor rechaza.
- **Pantalla `/sales-dispatch`:** lista de órdenes + escaneo del número; despacho guiado por tarea (posición reservada, cantidad ≤ reservado, otra posición permitida con aviso, omitir, deshacer); al terminar Retiro / Entrega (bultos) / Dejar para después. Botón en Inicio solo si hay órdenes locales.
- **Lógica pura** (`features/salesDispatch/salesDispatchLogic.ts`) y acceso local (`localSales.ts`).
- Manual (18 §14, 09 §12) y FAQ.

## Pruebas
- Jest: lógica pura, copia local (guardar, buscar por número completo o final, inactivas, efecto inmediato en la orden y en la existencia, reaplicar pendientes), descarga (reemplazo, 403), y la pantalla completa (retiro, entrega con bultos, sin señal, rechazo del servidor con el saldo devuelto, validaciones). `npm run check` verde: 125 suites, 715 pruebas.
- El servidor ya estaba probado contra SQL real en el Lote 64 (despacho + entrega atómicos).

## Decisiones por omisión (a revisar con Luis)
1. **El despacho en pantalla no sobrevive a cerrar la app** (no hay «documento abierto» ni bloqueo en Inicio): nada ha salido hasta enviar y se evita otro bloqueo más; lo enviado a la cola sí persiste.
2. **«Retiro» y «Dejar para después» mandan lo mismo** al servidor; solo cambia el aviso.
3. **La entrega del aparato usa servicio `STANDARD` y paquete `BOX`** (como el empaque de Despacho); cliente y consignatario salen de la orden.
4. **No se puede sacar más de lo reservado en una tarea**; para más, otro despacho tras surtir el backorder desde la web.
5. Tras un rechazo, la orden se baja de nuevo (sin esperar a la sincronización siguiente).

## Fuera de este lote
- Capturas del aparato (el aparato real/emulador no está en este entorno); la prueba de pantalla de Jest cubre el recorrido.
- S3 (avisos de ventas) y S4 (análisis) de P4.
