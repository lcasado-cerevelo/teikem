# Lote 150 — Menú del personal reestructurado (pedido de Luis, 2026-10-20)

## Qué cambió
- **Pulso del día** sale del grupo y va **solo, en el primer nivel**, encima de los grupos (`nav.top`); lo ven todos, como antes.
- **«Operación» → «Transporte»** (Transport en inglés) con: **Envíos** (antes «Órdenes»), Estación de escaneo, Sala de despacho, Monitoreo de ruta, Reclamaciones.
- **Contabilidad** ahora trae, en este orden: **Ventas** (antes «Órdenes de venta», que salió de Operación), **Compras** (la orden de compra, que salió de Almacén), Contabilización de compras, Contabilización de despachos, Facturación, Procesar entregas (COD), Liquidación a choferes e Impuestos.
- **Almacén:** nuevo ítem **«Llegadas y salidas»** (`/warehouse/notices`, `inventory.view`) con las pestañas **Avisos de llegada** y **Avisos de salida**, antes pestañas de Recibo. **Recibo** conserva Recibos y Acomodo pendiente; las direcciones de antes (`/warehouse/receipts?tab=asns|exit-notices`) redirigen a la nueva. «Recibir» un aviso de llegada lleva a Recibo con el aviso precargado.
- **Se quitó el grupo «Portal de clientes»** del menú del personal (el portal tiene su aplicación, `app-cliente`). La bandeja «Mensajes de clientes» sigue en `/portal/messages`, sin ítem de menú.

## Cómo se probó
- vitest: menú completo (6 grupos, 43 ítems más el Pulso de primer nivel), órdenes, títulos es/en, paleta, idioma sin reiniciar, redirección de las pestañas viejas; `npm run check` verde.

## Decisiones a revisar
1. **Reclamaciones se queda en Transporte** y **Proveedores en Almacén** (se podría llevar junto a Compras).
2. La pantalla de Envíos sigue titulándose «Órdenes» por dentro; solo cambió el nombre en el menú.
3. «Mensajes de clientes» quedó sin acceso desde el menú: falta decidir dónde lo ve el personal (por ejemplo, en la cabecera junto a los mensajes internos).
4. Pendiente (aprobado, otros lotes): los colores de la marca y una lista de permisos de **acceso por ítem de menú**, separada de los de crear/editar.
