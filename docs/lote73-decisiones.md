# Lote 73 — Pantalla de Facturación (P5, 2 de 4) (2026-10-16)

Frontend del Lote 72. Sin cambios de servidor ni de SQL.

## Qué se construyó
- Ruta `/money/billing` real (`billing.view`, módulo `BILLING`), en lugar del marcador de pendiente.
- `web-app/src/features/billing/`: `billingApi.ts` (hooks sobre el cliente generado), `billingRules.ts` (validación del formulario de la corrida, del ajuste y qué admite cada estatus), `BillingScreen.tsx` (pestañas Corridas, Facturas y Ajustes; ventanas de nueva corrida, corrida, factura, ajuste y anular).
- Textos `billing.*` en `es.json` y `en.json`.
- `approve` y `void` piden AAL2: el cliente del API reautentica solo (ventana «Confirme su identidad») y repite la llamada.
- Manual de pantallas `docs/manual/frontend/l73-facturacion.md` (con capturas) y entrada en el README del manual.

## Cómo se probó
- vitest: `billingRules.test.ts` (reglas puras) y `BillingScreen.test.tsx` (13 en total).
- Playwright contra el API real (SQL Server): orden despachada por API → nueva corrida del cliente → ajuste con motivo → revisar → aprobar con reautenticación → factura Emitida → anular con reautenticación → Ajustes; escritorio y móvil (360 px, sin scroll horizontal). Capturas `l73-*.png`.

## Decisiones a revisar
1. Aprobar y anular reautentican con el modal global, sin pantalla propia.
2. La fecha inicial por omisión es el primer día del mes y la final es hoy.
3. Los ajustes los edita solo quien tiene `billing.approve`.

## Fuera de este lote
Flete de los envíos de transporte (74), pagos, estado de cuenta y plantillas de exportación (75).
