# Lote 26 — Rentas R0: convertir un producto a serie (2026-10-05)

Primer bloque del submódulo **Rentas** (`docs/rentas-plan-de-ejecucion.md`, bloque R0; decisión del dueño D5-a). Las rentas solo trabajan con
equipos con número de serie, y los productos de Advance Depot llegaron de la migración **sin seguimiento** (`NONE`) y con saldo inicial
(`OPENING_BALANCE`): la regla D25 impide cambiarles el seguimiento por `PATCH` (409 `No se puede cambiar el tipo de seguimiento de un producto que ya
tiene movimientos.`). Este lote agrega la **única vía autorizada** para hacerlo: "Convertir a serie", por el ledger y en una sola transacción.

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Endpoint | `POST /api/v1/products/{publicId}/convert-to-serial` con `positions: [{ binId, serialNumbers[] }]`, `notes?`, `rowVersion?`; responde la ficha (ya `SERIAL`), `serialCount` y los movimientos y saldos tocados | `ProductsController.ConvertToSerial` (`inventory.manage`, módulo `WMS_LOTSERIAL`) |
| Servicio | Exige además `inventory.adjust` (`PermissionService.EnsureAsync`). En `RunInTransactionAsync`: bloquea el producto (U) y el rango de saldos del producto (HOLDLOCK), verifica (seguimiento `NONE`, nada reservado, sin documentos abiertos, existencia entera por posición sin lote, series = unidades por posición), asienta por el **ledger** un `ADJUSTMENT −` del saldo sin serie y un `ADJUSTMENT +1` por serie en cada posición (motivo `TRACKING_CONVERSION`, neto 0) y cambia `TrackingTypeLookupId` a `SERIAL` (auditado por `[AuditEntity]` de Product) | `Services/ProductSerialConversionService.cs` |
| Reglas puras y mensajes | `Capture {n} número(s) de serie para {bin} (hay {m}).` (400), `El producto {sku} tiene unidades reservadas; libérelas antes de convertirlo.` (409) — textos del plan — y los nuevos: documentos abiertos (409), ya es serie / es por lote / existencia fraccionaria / existencia sin posición o con lote (422) | `Domain/Wms/SerialConversionRules.cs` |
| Motivo de sistema | `AdjustmentReason` `TRACKING_CONVERSION` "Conversión a serie" (orden 11), reservado al sistema (`AdjustmentReasons.SystemAssigned`, `AdjustmentRules.SystemReasons`): un ajuste manual con él → 400 `El motivo TRACKING_CONVERSION lo asigna el sistema.` | `CatalogDomains.cs`, `AdjustmentRules.cs`, `Diseño/logistica-db-seed.sql` (MERGE idempotente existente de `#L`) |
| Documentos abiertos | El criterio de "documentos abiertos que moverían el producto" (recibos abiertos, tareas PENDING/IN_PROGRESS, recolecciones que aún se pueden eliminar, conteos OPEN/COUNTED) sale de `ProductService.DeactivateAsync` a un ayudante común; la baja del producto lo usa igual que antes | `Services/ProductOpenDocuments.cs`, `ProductService.DeactivateAsync` |
| Contratos | `SerialConversionPositionInput`, `ProductSerialConversionRequest`, `ProductSerialConversionResultDto` | `Contracts/ProductContracts.cs`; `web-app/openapi.json` y `schema.d.ts` (web y app) regenerados |
| Web (mínimo) | `TRACKING_CONVERSION` se agrega a los motivos reservados del selector de ajuste (no se ofrece a mano). El botón "Convertir a serie" llega con F-R1 | `web-app/src/features/warehouse/adjustmentReasons.ts` |

Sin cambios de esquema (solo el valor de catálogo en el seed). Sin permisos ni módulos nuevos.

## Cómo se probó

- `dotnet test tests/Teikem.Tests -o .tmp-testout`: **3027 pruebas, 0 fallas** (antes 3014). Nuevas: `ProductSerialConversionTests` (9: conversión por posición
  con los 5 movimientos, motivo, nota por defecto, series `AVAILABLE` en su posición con historial, D25 sigue en 409 y segunda conversión 422; nota capturada y producto sin
  existencia; 400 por conteo —menos series, posición omitida, posición sin existencia, serie repetida entre posiciones, renglón sin posición— sin escribir; 409 por reserva y por
  tarea abierta sin escribir; 422 lote / serie / fracción; 403 sin `inventory.adjust` con `inventory.manage`, 404 de otra compañía y posición ajena, control positivo; motivo
  reservado y sembrado; reglas puras; **confinamiento por texto**: `TrackingTypeLookupId` solo lo escriben `ProductService.cs` y `ProductSerialConversionService.cs`).
  Ajustadas: `WmsControllerSecurityTests` (131 acciones, `ConvertToSerial` → `inventory.manage`), `WmsContractsTests` (3 firmas), `WmsCatalogTests` y
  `LegacyImportCatalogTests` (lista de motivos de sistema), `WmsFixture` (motivo al final del catálogo). `WmsWriteConfinementTests` pasa sin cambios.
- SQL Server 2022 local (`scripts/dev-sqlserver.sh`): `db-init` **dos veces** sobre una base **nueva** (`TeikemR0Smoke`) y `scripts/smoke.sh` completo contra el API
  (puerto 5180, `Auth__Onboarding__Enabled=false`, `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` con el build de `.tmp-testout`): **SMOKE OK** (134 pasos, 136 `ok`). Paso nuevo
  "convertir a serie (Lote 26…)": D25 409, motivo manual 400, 403 Solo lectura, 400 con el texto exacto (menos series y posición omitida), 409 con un recibo abierto, otra
  compañía 403/404, sin escrituras en los rechazos, conversión 2+1 unidades (SERIAL, 3 series `AVAILABLE` en su posición, 5 ajustes con neto 0, en mano intacto), AuditLog del
  seguimiento, segunda conversión 422, el ajuste ya exige series, sin descuadre Kárdex ↔ saldo. El catálogo `AdjustmentReason` del paso del Lote 6 pasa de 10 a 11.
- Web `npm run check`: tipos generados, tsc, oxlint, **vitest 1250** y build. App `npm run check`: tipos, typecheck, lint, **jest 388**.

## Decisiones a revisar

1. **Permisos**: el plan pide `inventory.manage` + `inventory.adjust`. Se dejó **un** `[RequirePermission(inventory.manage)]` en el controlador (la prueba de seguridad
   exige exactamente uno por acción) y `inventory.adjust` lo exige el servicio (patrón de `WmsServicePermissionTests`); el efecto es el mismo (403 y `PERMISSION_DENIED`).
2. **Lectura del mensaje** `Capture {n} número(s) de serie para {bin} (hay {m}).`: `{n}` = unidades en mano de la posición, `{m}` = series capturadas.
3. **Todo el producto de una vez**: la conversión exige series para **todas** las posiciones con existencia en una sola solicitud (el seguimiento es del producto, no de la
   posición). No hay conversión parcial.
4. **Solo `NONE` → `SERIAL`**: un producto por lote no se convierte (422). Un producto inactivo (sin existencia, por la regla de baja) sí se puede convertir.
5. **Bloqueos adicionales** (más conservadores que el plan, con mensajes nuevos): documentos abiertos (409, mismo criterio que la baja del producto), existencia
   fraccionaria y existencia sin posición o con lote (422).
6. **Sin referencia** de documento en los movimientos (el motivo `TRACKING_CONVERSION` los identifica); la nota por defecto es "Conversión a serie".
7. **Reporte de ajustes de la web**: los ajustes de la conversión **se muestran** (neto 0), a diferencia del saldo inicial (`OPENING_BALANCE`), que se excluye.
8. Se corrigió en `scripts/smoke.sh` un texto viejo del Lote 25 (`allowEmpty`: "a uno o ningún producto"), que hacía fallar el smoke antes de llegar a este paso.

## No probado

- La pantalla (botón "Convertir a serie") no existe todavía (bloque F-R1); no hay Playwright.
- Concurrencia real (dos conversiones o un movimiento simultáneo) en SQL Server: el orden de bloqueo es el de `ProductService` (producto U → rango HOLDLOCK → ledger), pero
  el smoke lo recorre en serie. La reversa a mitad de la transacción solo se razona (InMemory no es transaccional; el ledger valida todo antes de escribir).
- Productos con series ya registradas (NONE con filas en `InventorySerial`): los 409 del ledger (`ya está en inventario` / `fue dada de baja`) no se ejercitaron.
