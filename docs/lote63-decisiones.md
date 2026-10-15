# Lote 63 — Despachar contra una orden de venta (web) (P4, S2) (2026-10-15)

Sigue a los Lotes 61 y 62 (servidor). Diseño: [p4-s2-despacho-diseno.md](p4-s2-despacho-diseno.md).

## Qué se construyó
- En la ventana de una orden solicitada: **Despachar** (permiso `warehouse.pick`; visible mientras quede algo reservado) con la cantidad por línea (por omisión todo lo reservado), nota opcional y la validación «no más de lo reservado».
- **«¿Qué sigue con lo despachado?»**: Retiro (listo) / Entrega / Decidirlo después.
- **Entrega con el formulario del empaque** (`PackModal`, ahora reutilizable con `forSalesOrder`): cliente y consignatario de la orden de venta ya puestos; guarda por `POST /sales-orders/{id}/delivery` (crea la orden de transporte y la enlaza).
- **Tabla de despachos** de la orden (número, estatus Despachado / Con entrega / Deshecho, unidades, cuándo, orden de transporte) con **Crear entrega** en los que no la tienen.
- Manual `frontend/l63-despachar-orden-de-venta.md` con 4 capturas.

## Pruebas
- vitest (suite de ventas y almacén: 64 archivos): despachar con la validación y el cuerpo enviado, la pregunta «¿qué sigue?» y la apertura del formulario de la entrega, la lista de despachos.
- Playwright `lote63-despacho-orden-de-venta.spec.ts` (escritorio y móvil a 360 px, sin scroll horizontal) contra el API real con SQL Server: despacho de 4 de 6 → Retiro → orden «Despachada parcial» → entrega con consignatario nuevo (el despacho pasa a «Con entrega» con su ORD-) → despacho de lo que falta → «Despachada»; el inventario en mano bajó de 10 a 4 y no quedó nada reservado.
- Se encontró y arregló un detalle de la pantalla: la ventana de la orden se **reiniciaba al cambiar de estatus** (perdía el diálogo «¿qué sigue?»); ahora se identifica solo por la orden.

## Decisiones por omisión (a revisar con Luis)
1. **En la web no se escoge la posición**: se despacha de lo reservado. Tomar de otra posición (con el movimiento de la reserva) queda para el aparato, donde se escanea la posición.
2. La entrega reutiliza el **formulario del empaque** tal cual (incluido el número de orden y la factura del cliente opcionales).
3. «Deshacer un despacho» se hace con la pantalla de Recolecciones de siempre (eliminar la recolección); no hay botón propio en la orden.
