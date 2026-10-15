# Lote 56 — Impuestos (IVU): la pantalla (2026-10-15)

P3 del plan, parte web. Sigue al Lote 55 (servidor): [lote55-decisiones.md](lote55-decisiones.md). Permiso `tax.manage`, módulo TAX.

## Qué se construyó
- **Contabilidad → Impuestos** (`/money/tax`, orden 60 en el menú de Contabilidad), con cinco pestañas:
  - **Tasas**: historial con porcentaje, componentes, vigencia y estado; **Nueva tasa** (componentes con total en vivo); **Cerrar vigencia** y **Nueva tasa desde esta** (trae código, nombre y componentes, propone empezar el día siguiente).
  - **Reglas**: tasa por omisión, precios con impuesto incluido, redondeo y cargos gravables.
  - **Productos**: categoría de impuesto por producto (se guarda al escoger), buscador y filtro; trae hasta 500.
  - **Exenciones**: certificados de clientes, con editar, dar de baja y reactivar.
  - **Simulador**: el mismo cálculo que usarán ventas y facturación, sin guardar nada.
- API: se agregó `GET /api/v1/tax/products` (listado paginado de productos con su categoría, para la pantalla).
- Manual: `frontend/l56-impuestos.md` (7 capturas `l56-*`) y FAQ.

## Pruebas
- vitest: reglas puras (porcentajes con coma, suma sin errores de coma flotante, validaciones, día siguiente), pantalla completa sobre un fetch simulado (tasas, cierre, creación con mensaje del servidor, reglas, productos, exenciones, simulador) y menú (46 ítems). **Suite completa verde (232 archivos, 2873 pruebas).**
- xunit: listado de productos por categoría (filtros, buscador, aislamiento entre compañías, mensaje).
- Playwright `lote56-impuestos.spec.ts` (escritorio y móvil a 360 px, sin scroll horizontal) contra el API real: crea el IVU 11.5 % (10.5 + 1), rechaza un traslape, cierra la vigencia el 14 y crea el 12.5 % el 15, fija la tasa por omisión y el flete gravable, cambia un producto a Exento y lo devuelve, crea y da de baja una exención y comprueba en el simulador que el 14 sale 11.50, el 15 sale 12.50 y con la exención vigente sale 0.00 con el certificado.

## Decisiones por omisión (a revisar con Luis)
1. **La pantalla vive en Contabilidad** (grupo «money»), no en Sistema: es dinero, no configuración técnica.
2. **Una sola pantalla con pestañas** en vez de cinco pantallas; el simulador hace de «prueba» para el contador.
3. El simulador calcula con **tipos de cargo** (venta de producto gravable, flete, manejo…) y no con productos concretos: la categoría de cada producto se cambia en la pestaña Productos y la cubren las pruebas del servidor.
4. **Productos**: trae hasta 500 y se afina con el buscador (sin paginar en el servidor).
5. La fecha del simulador y las de las tasas usan el selector de fecha del navegador.
6. El **informe de impuestos por período** sigue pendiente hasta que existan órdenes de venta y facturas (P4 y facturación).
