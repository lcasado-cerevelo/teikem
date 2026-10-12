# Lote 37 — Cierre de P0.4: almacén (resumen de recibos, rotación en pantallas, despacho sin señal) (2026-10-11)

Cierra lo que quedaba del almacén en P0.4 de `docs/plan-de-desarrollo-pendiente.md`.

## Qué se construyó

| Pieza | Resultado | Dónde |
|---|---|---|
| Resumen de recibos por almacén | `GET /api/v1/receipts/summary-by-warehouse` (`inventory.view`): recibos abiertos y por acomodar de **todos** los almacenes en una llamada. Antes la pantalla hacía dos consultas por almacén | `ReceiptService.SummaryByWarehouseAsync`, `ReceiptsController`, `OperationsTab.tsx` |
| Selector de rotación FEFO/FIFO (web) | Ajustes de la compañía → Operación → «Rotación del inventario» (`admin.tenant`) y excepción por producto en su ficha («Igual que la compañía» la quita). La app no necesita selector: ya respeta el método que calcula el servidor | `OperationsTab.tsx` (`RotationPanel`), `ProductEditorModal.tsx`, i18n es/en |
| Clientes y consignatarios en el aparato | `GET /api/v1/sync/clients` y `GET /api/v1/sync/consignee-locations` (`warehouse.pick`, módulo WMS_LOTSERIAL, por diferencia con `since`/`cursor`/`take`). Las bajas (`isActive=false`) se borran del aparato | `SyncService`, `SyncController` |
| Despachar y empacar sin señal | La base local migra a la **v13** (tablas `client`, `consignee_location`). «Empacar» en un despacho de inventario propio lee clientes y consignatarios del aparato; si nunca los bajó (o el 403 por falta de `warehouse.pick`), los pide al servidor como antes | `schema.ts`, `download.ts`, `dispatchApi.ts` |
| Recorridos Maestro nuevos | `03b-despacho-sin-senal` (sin red: empaca eligiendo consignatario) y `03c-despacho-enviado` (con red: «Todo enviado») | `app-almacen/e2e-maestro/`, `scripts/e2e-mobile-maestro.sh` |

## Cómo se probó

- Servidor: **3415 pruebas verdes** (nuevas: `ReceiptSummaryByWarehouseTests`, clientes/consignatarios en `SyncRulesTests`; cuenta de permisos de `WmsControllerSecurityTests` en 162).
- App (`npm run check`): **692 pruebas, 121 suites verdes**. Web (`npm run check`): **1542 pruebas, 152 archivos verdes**, build correcto. El selector de rotación se verificó además en el navegador.
- Emulador Android local (`teikem_e2e`, API local contra `Teikem_verif`): **Maestro 01 → 02 → 03 → 04a/04b → 05a/05b → 03b/03c, todos verdes**, cortando y devolviendo la red con adb.
- Nota: con el emulador abierto, un worker de vitest murió por falta de memoria (código 3221225477); sin emulador el `check` de la web pasa completo.

## Decisiones a revisar

1. Quien despacha ya no necesita `clients.read` ni `locations.read` para elegir a quién: basta `warehouse.pick`. Si prefieres exigir también esos permisos, es un cambio en `SyncController`.
2. Un consignatario dado de baja después de la última sincronización se ofrece hasta que el aparato vuelva a sincronizar; si el servidor lo rechaza, el despacho queda en «requiere revisión» con el motivo.
3. La app no tiene selector de rotación (no tiene pantallas de ajustes); se decidió no agregarlo.

## Qué quedó fuera (es de Luis)

- Correr `scripts/revisar-tablas-viejas-de-rentas.sql` en producción (solo lectura).
- Probar en un Zebra físico.
- Volver a desplegar lo de P0.2.

Manual: `docs/manual/06` (§2.2 y recibos), `08` (sync), `09` (§6.z), `frontend/f9-ajustes-de-la-compania.md` y `faq.md`.
