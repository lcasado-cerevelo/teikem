# Lote 76 — Pantallas de cobros (P5, 5 de 6) (2026-10-16)

Quinto lote de la facturación (la web de los pagos del Lote 75). Sigue el **77**: plantillas de exportación a contabilidad (`ACCT_TEMPLATES`, QuickBooks genérico); después la contabilización de compras y de despachos y el cargo por intento de entrega (P7). Antes de este lote se hizo el 76a (Kemi con vida).

## Qué se construyó
- **Ficha de la factura:** sección **Pagos** (lista, **Registrar pago**, **Anular el pago**) con las guardas de estatus y de permiso del servidor (`billing.generate` para registrar; `billing.approve` + AAL2 para anular, que el cliente del API reautentica solo).
- **Pestaña Cobros:** cuentas por cobrar por cliente con la antigüedad en cinco columnas, total por cobrar y vencido, y el **estado de cuenta** (rango de fechas, saldo inicial y final, movimientos con saldo corrido, antigüedad; un movimiento abre su factura).
- `CollectionsParts.tsx` (nuevo, para no engordar `BillingScreen.tsx`), hooks en `billingApi.ts`, reglas puras en `billingRules.ts`, textos `billing.*` en es/en y `billing.css`. `openapi.json` y el cliente generado con las 4 rutas y los 9 esquemas nuevos.
- Manual de pantallas `docs/manual/frontend/l76-cobros.md` con 4 capturas y entrada en el README.

## Cómo se probó
- vitest: reglas de validación del pago, de estatus y del motivo (5 nuevas) y la pantalla con un fetch simulado (6): registrar con validación y tope del saldo, error del servidor, anular con motivo obligatorio, factura pagada sin acciones, solo lectura con `billing.view`, cuentas por cobrar y estado de cuenta (24 pruebas del módulo en total). `npm run check` completo.
- Playwright contra el API real (SQL Server), escritorio y móvil (360 px, sin scroll horizontal): aprobar la corrida con reautenticación, pago rechazado por pasar del saldo, pago parcial con cheque, anularlo con motivo (reautenticación), pagar por ACH, cuentas por cobrar con el saldo de 35, estado de cuenta con saldo corrido y pagar el resto hasta Pagada.

## Decisiones a revisar con Luis
1. Los pagos se registran desde la ficha de la factura (no hay una pantalla de «registrar cobro» por cliente que reparta un pago entre varias facturas); eso podría venir si cobranza lo pide.
2. El monto por omisión del pago es el saldo; la fecha, hoy.
3. El estado de cuenta se imprime/exporta con el botón Exportar de la tabla; un documento imprimible con membrete queda para cuando lleguen las plantillas (77).
4. La pestaña se llama **Cobros** (y no «Cuentas por cobrar») por el ancho de las pestañas en móvil; el panel sí se titula «Cuentas por cobrar».

## Fuera de este lote
Plantillas de exportación (77), avisos de factura vencida, notas de crédito (P6).
