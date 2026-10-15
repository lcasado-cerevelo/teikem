# Lote 49 — Módulos SALES_ORDERS, TAX y BILLING (P2.3, 2026-10-14)

Primer lote de P2 (cimientos). Hecho en el contexto principal, sin agentes (orden de Luis).

## Qué se construyó
- `ModuleKeys.SalesOrders` / `Tax` / `Billing` (C#) y su espejo en `web-app/src/kernel/access/modules.ts`.
- Sección «2026-10-14 (e)» al final de `Diseño/logistica-db-update.sql`: `MERGE` idempotente en `dbo.ModuleDefinition`. Dependencias: `SALES_ORDERS` → `WMS_LOTSERIAL`, `BILLING` → `SALES_ORDERS`, `TAX` sin dependencia. Categoría «Dinero», orden 100/110/120.
- Manual: capítulo 01 §10 (lista de 16 módulos) y FAQ «Lote 49».

## Pruebas (solo el área)
`ModuleCatalogP2Tests` (constantes; la sección define los tres con sus dependencias, no enciende nada por compañía, sin DELETE/DROP) y el área SQL/módulos: 148 en verde. `db-update --dry-run` sobre una base real aplicó los 103 lotes de update.sql sin error. vitest `kernel/access` y `features/system`: 137 en verde; `tsc -b` limpio. Sin cambio de pantalla (la lista de módulos sale de la base): sin capturas nuevas.

## Decisiones por omisión (a revisar con Luis)
1. **No se encienden por compañía todavía** (el plan decía «Advance Logistics e Island Wide (ventas)»): no hay forma segura de identificar esas compañías por nombre en producción y no hay nada que ofrecer aún; se encienden desde Sistema → Módulos cuando se construya cada módulo (P3/P4).
2. **`NOTIFICATIONS` no es un módulo**: los avisos van siempre disponibles bajo `SYSTEM` con el permiso `notifications.manage` (lote de avisos).
3. `SALES_ORDERS` depende de `WMS_LOTSERIAL` (descuenta inventario) y `BILLING` de `SALES_ORDERS`; `TAX` no depende de nadie.
