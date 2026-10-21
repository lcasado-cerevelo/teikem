# Lote 168 — Avisos de flota (G1; pedido de Luis, 2026-10-21)

## Qué se construyó
- **Eventos de flota** (`FleetEventProvider`, módulo Catálogos, opcionales y sin reglas sembradas): `FLEET_DOC_EXPIRING`, `FLEET_DOC_EXPIRED`, `FLEET_MAINTENANCE_DUE_SOON`, `FLEET_MAINTENANCE_OVERDUE`. Enlazan a Flota → Documentos por vencer / Mantenimiento.
- **`FleetAlertService`** (la revisión) y **trabajo diario `FLEET_ALERT_SCAN`** (6:15 a. m.): un aviso por evento con el conjunto completo, misma clave de no repetición que inventario (evento + semana ISO + huella). **No reescribe consultas**: lee con `FleetDocumentService.GetExpiringAsync` (dueños activos y no terminales, documento vigente por tipo) y `MaintenanceScheduleService.GetDueAsync` (el cálculo del panel de mantenimiento).
- **Reglas puras** en `FleetAlertRules` (vencido / por vencer / rango del ajuste).
- **Ajuste de la compañía** `Tenant.FleetDocWarnDays` (30; 1 a 365) en `PUT/GET /tenant/settings`, sección nueva al final de `Diseño/logistica-db-update.sql` (idempotente, con CHECK), y panel «Avisos de flota» en Ajustes → Operación.

## Cómo se probó
- xunit `FleetAlertTests` (reglas con sus bordes, catálogo, trabajo, vencidos vs por vencer, documento sin vencimiento, documento renovado, licencia de chofer, no repetir, aviso nuevo si entra otro documento, ventana configurable, mantenimiento próximo vs vencido) + prueba de contrato de `TenantSettings*` actualizada (campo nuevo al final).
- vitest `TenantSettingsPage.test.tsx` (panel nuevo: 30 por omisión, validación y cuerpo del PUT); `npm run check` completo.

## Decisiones a revisar
1. **Un solo ajuste de días** para todos los documentos (seguro, registro, licencia…); si quieres días distintos por tipo, se agrega después.
2. **Mantenimiento sin ajuste propio**: «próximo» = el criterio ya existente (queda el 10 % del intervalo).
3. **Recordatorio semanal** mientras el problema siga vivo (igual que inventario).
4. El trabajo corre bajo el módulo **Catálogos** (donde vive la flota); no hay módulo de flota aparte.

## Qué quedó fuera
- G2: orden de trabajo abierta demasiado tiempo, combustible fuera de rango, vehículo/chofer sin ruta N días (esperar datos reales).
- Tarjeta en «Necesita tu atención» / indicador del Pulso para estos avisos.
