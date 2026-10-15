# Lote 60 — Órdenes de venta: la pantalla (P4, S1 / web) (2026-10-15)

Sigue a los Lotes 57 a 59 (servidor). Permisos `sales.*`, módulo SALES_ORDERS. Pantalla propuesta por mí (la maqueta no trae órdenes de venta).

## Qué se construyó
- **Operación → Órdenes de venta** (`/sales-orders`, orden 30 del menú Operación; el menú se renumeró) con dos pestañas:
  - **Órdenes**: lista paginada por el servidor con buscador (número, cliente, producto), filtro de estatus y exportación completa; **Nueva orden**; clic en la fila abre la orden.
  - **Backorders**: lo pendiente, el más antiguo primero, con disponible y «se puede surtir»; **Surtir**, **Surtir todo** y **Cerrar con faltante**.
- **Ventana del borrador**: cliente, almacén, fecha requerida, notas y líneas (producto propio con buscador, cantidad, precio con el de lista como pista, importe y subtotal); guardar borrador, **Solicitar…** y cancelar.
- **Diálogo de solicitar**: SIEMPRE comprueba antes de reservar (`GET availability`): con faltante muestra **Pedido / Disponible / Faltante** y ofrece reservar lo disponible con backorder o volver a editar; con crédito excedido avisa y deja autorizarlo a quien tenga `orders.credit_override`; sin problemas confirma y reserva.
- **Orden solicitada**: lo reservado y lo pendiente por línea, **cambiar cantidad** (con «dejar el resto en backorder»), surtir / cerrar con faltante por línea y cancelar liberando lo reservado.
- API del cliente generada desde `openapi.json` (parcheado como en lotes anteriores), textos es/en, manual `frontend/l60-ordenes-de-venta.md` con 6 capturas.

## Pruebas
- vitest: reglas puras del borrador (números, precio de lista vs escrito, validaciones en el orden del servidor, cuerpo de la solicitud) y la pantalla sobre un fetch simulado (lista y filtro, borrador → solicitar con faltante, sin faltante y con crédito excedido con autorización, orden solicitada cambiar cantidad y cancelar, backorders y surtir todo); menú (47 ítems).
- Playwright `lote60-ordenes-de-venta.spec.ts` (escritorio y móvil a 360 px, sin scroll horizontal) contra el API real con SQL Server: orden de 10 con 6 en existencia → diálogo 10 / 6 / 4 → reservar con backorder → la lista marca «Con backorder» → entra inventario y **Surtir todo** la completa (desaparece de la vista) → cambiar la cantidad a 7 y cancelar; después se comprueba por API que lo reservado quedó libre.

## Decisiones por omisión (a revisar con Luis)
1. **El menú de Operación se renumeró** (Órdenes de venta en el lugar 3; Estación de escaneo, Sala de despacho y Monitoreo bajan un lugar): es solo el orden en que se ven.
2. **Solicitar siempre pasa por el diálogo de comprobación**, aunque alcance todo: una sola puerta, y confirma qué se va a reservar.
3. **Una orden nueva arranca con una línea vacía**; «Guardar borrador» cierra la ventana (el borrador se reabre desde la lista).
4. **Las líneas de un borrador ya guardado envían su precio siempre** (no se sabe si era el de lista), así que quien edite un borrador con un precio especial sin el permiso `sales.price` recibirá el aviso de permisos.
5. **Un cliente de paso o un producto de cliente no aparece** en el buscador de productos (solo propios).
6. El **despacho contra la orden**, los avisos de ventas y los indicadores quedan para S2, S3 y S4.
