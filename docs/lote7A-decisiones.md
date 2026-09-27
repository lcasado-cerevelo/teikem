# Lote 7A — Pulso de almacén y Actividad reciente (backend): qué se construyó y decisiones a revisar

Plan aprobado: `docs/lote7A-plan.md` (y su espejo `docs/lote7A-plan.json`). Diseño: `Diseño/logistica-funcionalidades-maestro.md`,
módulo 12, subsección "Pulso del día — diseño consolidado".

> Documento en construcción: el cierre del lote (mapa de lo construido, cómo se probó, CI) se completa al terminar el lote.
> Esta sección recoge las decisiones que salieron de la revisión de las piezas P0–P2.

## Decisiones a revisar

1. **La fuente `CYCLE_COUNT` se consulta fila por fila en Análisis con solo `analytics.view` y el módulo ANALYTICS**, sin
   `inventory.view` ni `WMS_LOTSERIAL`, igual que las demás fuentes de almacén del Lote 6 (`RECEIPT`, `WAREHOUSE_TASK`,
   `PICK_BATCH`…): el motor de análisis no revisa permiso ni módulo por fuente. Por ejemplo, un Despachador puede
   previsualizar un reporte de `CYCLE_COUNT` y ver número, almacén, líneas con diferencia y diferencia neta de cada conteo,
   mientras que `GET /api/v1/cycle-counts` le responde 403. El maestro solo declara visibles a toda la organización los
   **agregados** de Pulso (indicador *Conteos con diferencia*), no el detalle. Precedente: Lote 4, decisión #37 (no se
   crearon fuentes de tarifas ni de `DriverTrip` para no exponer compensación con solo `analytics.view`). Alternativa, si se
   quiere cerrar: en `AnalyticsService` (preview, run y alta/edición de reportes) exigir
   `PermissionCatalog.OwnerReadPermission[fuente]` cuando exista, sin tocar indicadores ni gráficos (Pulso sigue igual).
2. **P2 — se creó `CycleCountDataSource` (`CYCLE_COUNT`, `DateField = ReconciledAtUtc`)** porque no existía, aunque el maestro
   decía "solo seed, sin código nuevo". El indicador *Conteos con diferencia* filtra `StatusCode = RECONCILED` y
   `HasVariance` (alguna línea con diferencia o con ajuste enlazado) y **no** `NetVariance ≠ 0`: sobrantes y faltantes de un
   mismo conteo se compensan y el conteo sí tuvo diferencia; `HasVariance` también cuenta la línea con ajuste aunque la
   cantidad cuadre (sustitución de serie). El maestro se corrigió en consecuencia.
3. ***Unidades recibidas* suma RECEIPT + ADJUSTMENT `RECEIPT_VARIANCE`** del ledger (no solo RECEIPT, como decía la letra del
   maestro L544): por D4 del Lote 6 una línea con esperado E se asienta como RECEIPT por E más un ajuste por la diferencia,
   así que solo RECEIPT sumaba lo esperado (10 esperados y 8 recibidos daban 10). El neto con signo de ambos es lo recibido.
   El seeder corrige de forma idempotente el filtro de la primera versión (`ReceiptMovementsFilterV1`) si sigue intacto.
4. **`INVENTORY_ADJUSTED` cubre todo cambio manual de saldo**: un motivo manual (DAMAGE, LOSS, FOUND, EXPIRED, OTHER) con o
   sin documento de origen, y cualquier otro motivo que no asigne el sistema (p. ej. `PO_SHORTAGE` capturado a mano o un
   código nuevo del catálogo) sin documento. Consecuencia: el FOUND con que se resuelve un faltante de compra aparece dos
   veces, como `PO_SHORTAGE_RESOLVED` (opcional) y como `INVENTORY_ADJUSTED` (obligatorio). `PICK_BATCH_REVERSAL` (motivo de
   sistema) y el `PO_SHORTAGE` de un faltante (con Ref) no generan `INVENTORY_ADJUSTED`: ya los cuentan `PICK_CANCELLED` y
   `PO_SHORTAGE_RESOLVED`.
5. **La categoría de producto se audita con su propio `EntityType` (`PRODUCT_CATEGORY`)**, sembrado en
   `logistica-db-seed.sql`. Antes compartía PRODUCT y su baja escribía un `AuditLog` PRODUCT DELETE indistinguible de la baja
   de un producto con el mismo id (los IDENTITY coinciden): el feed mostraba `PRODUCT_DEACTIVATED` falsos. Ahora cada fila
   PRODUCT DELETE con `IsActive` es la baja de un producto (un lote nunca genera DELETE) y el evento se muestra aunque el
   producto se haya reactivado después. Las filas de bitácora anteriores al cambio siguen ambiguas. `PRODUCT_CATEGORY` no se
   agrega a `OwnerReadPermission`/`OwnerWritePermission` (no hay campos personalizados, contactos ni historial de estatus de
   categorías; agregarlo exigiría un `IOwnedEntityResolver`).
