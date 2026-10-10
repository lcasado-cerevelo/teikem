# Señal débil: el aparato trabaja con sus datos locales (decisiones)

*Pedido de Luis (2026-10-10):* en el fondo del almacén, con señal floja, escanear tardaba mucho. Regla acordada: **todo se trabaja desde el
aparato**; la pantalla muestra primero lo local, sincroniza en paralelo con un indicador visible y se actualiza al terminar.
Se construye en bloques probados; el sistema está en producción (solo `Diseño/logistica-db-update.sql` para cambios de base).

## Bloque S1 — servidor: saldos por diferencia (hecho)
- **Qué:** `GET /api/v1/sync/balances` (módulo WMS_LOTSERIAL, `inventory.view`), `SyncService.BalancesAsync`, `SyncBalanceDto`.
- **Sin cambio de base de datos:** `StockBalance` ya tiene `UpdatedAtUtc`, el ledger lo escribe en cada movimiento y reserva
  (`InventoryLedger`, líneas de Post/Reserve/Release/Adjust) y los saldos nunca se borran (quedan en cero). Por eso la diferencia es
  `UpdatedAtUtc >= since`, sin tocar AuditLog.
- **Carga completa:** solo saldos con existencia o reservado. **Con `since`:** todos los cambiados, con `isActive=false` los que quedaron en cero.
- **Pruebas:** `SyncRulesTests` (carga por almacén, lote y vencimiento, saldo que queda en cero solo sale en la diferencia, paginación por
  cursor, almacén de otro tenant → 404) y el control de seguridad por reflexión del controlador; `scripts/smoke.sh` (200 y 400 `take > 500`).
- **Contrato:** `web-app/openapi.json` regenerado y `schema.d.ts` de la web y de la app.

### Hallazgo: el CI de `master` estaba en rojo desde antes de este bloque
- Paso «Smoke test»: dos pruebas **desactualizadas** (no eran fallas de producción): el Kárdex muestra el acomodo como `PUTAWAY`
  (commit a650e56) y el conteo vacío ya admite una sola posición (commit 0928435). Corregidas en `scripts/smoke.sh`; el humo completo
  pasa en una base nueva (`SMOKE OK`).
- Job `android-e2e` (Maestro en emulador): falla tras ~30 minutos sin dejar una causa legible en el registro; no se pudo reproducir aquí
  (sin emulador). **Pendiente de revisar** con los artefactos del job.

## Bloque S2 — app: base local de saldos (hecho)
- **Migración local v10** (`SCHEMA_VERSION` 10, `kernel/db/schema.ts`): tabla `stock_balance` (saldo por posición, producto y lote, con en mano y
  reservado) e índices por almacén+posición y almacén+producto. Los aparatos con la versión 9 migran solos al abrir la app; no se pierde nada.
- **Bajada** (`kernel/sync/download.ts`, `downloadBalances`): por diferencia con marca de agua propia por almacén (`balances:{almacén}`), dentro de
  la misma pasada de sincronización (después de las posiciones). Un saldo en cero se borra del aparato. Sin `inventory.view` (403) se salta.
- **Lectura local** (`kernel/warehouse/localBalances.ts`): saldos de una posición, de un producto y búsqueda libre, uniendo `product` y `bin`
  (nombre, SKU, código y tipo de zona); disponible = en mano − reservado. `balancesSyncedAtUtc` dice cuándo se puso al día por última vez.
- **Pruebas:** descarga con páginas y marca de agua, actualización y borrado, 403, lecturas locales y orden de recursos de la pasada
  (jest con SQL real; 577 pruebas verdes, `tsc` limpio).

## Bloque S3 — app: indicador y pantallas que leen lo local (hecho en parte)
- **Indicador** (`kernel/ui/RefreshNote.tsx`): «Actualizando…» → «✓ Al día» / «Sin conexión o con señal débil…»; en Inicio, `useIsSyncing` (nuevo en `kernel/sync/engine.ts`) lo enciende mientras corre la sincronización.
- **Consultar** (`features/lookup/lookupFlow.ts`): `lookupLocal` (al instante, sin red) + `lookupOnline` (servidor, con la copia guardada de respaldo) + `applyOnline`
  (qué hace la pantalla con la respuesta: el servidor manda; sin red se queda lo local). Un escaneo nuevo descarta la respuesta tardía del anterior.
- **Transferir y Ajustar:** la lista de la posición de origen sale de lo local y se pone al día en segundo plano (si el servidor dice que ya no se puede mover, avisa y reinicia).
- **Conteo y Daño:** `resolveBinLocalFirst` reconoce la posición de lo sincronizado sin esperar a la red (solo pregunta al servidor si no está).
- **Pruebas:** `lookupFlow.test.ts`, `binLookup.test.ts` (590 pruebas de la app verdes, `tsc` limpio).

### Lo que todavía depende del servidor (honesto)
- **Abrir un conteo nuevo** (el conteo se crea en el servidor): pendiente un conteo creado localmente que se concilia después. Es un bloque aparte.
- **Acomodar y Recibir**: usan el espacio libre de la posición (`freeQty`, capacidad), que no está sincronizado; siguen consultando al servidor para eso.
- **Despacho**: el plan de salida ya usaba una copia local (`stock_exit`) desde el lote anterior.

## Bloque S4 — Transferir, Ajustar y Daño a la cola de salida (D2b: «A para las 3», hecho)
- **Cola:** `OutboxKind` ahora incluye `transfer`, `adjust` y `damage` (rutas fijas, POST con `Idempotency-Key`; el middleware del servidor ya cubre esas rutas, sin cambios en el API).
- **Saldo local = servidor + pendientes** (`kernel/warehouse/balanceProjection.ts`): al encolar se aplica el efecto a `stock_balance` (la fila nueva de un destino lleva id
  negativo hasta que llega la verdadera); si el servidor rechaza, se deshace; al reintentar, se vuelve a aplicar; cada bajada de saldos vuelve a sumar lo pendiente a las filas que
  reemplaza y borra la fila provisional. Daño no se proyecta (el servidor decide la posición de cuarentena y las reservas).
- **Respuesta rápida sin bloquear:** `flushNow` (engine) intenta mandar y espera como máximo 3 s; `sent` → «hecho», `rejected` → alerta con el motivo, `pending` → «guardado, se envía solo».
- **Pantallas:** Transferir, Ajustar y Daño ya no esperan al servidor; Ajustar vuelve a Consultar de inmediato.
- **Pruebas:** `balanceProjection.test.ts` (efecto, deshacer, rechazo, bajada con pendientes, fila provisional), pruebas de pantalla de Daño actualizadas (598 de la app verdes).
- **Límites conocidos (a vigilar):** si el servidor ya aplicó una operación pero la respuesta se perdió, el aparato la sigue viendo pendiente hasta el siguiente envío (la clave de idempotencia
  evita duplicarla) y durante ese rato un saldo bajado puede contarla dos veces; dos aparatos moviendo lo mismo sin señal: el segundo en llegar queda «requiere revisión». La validación de
  «no mover más de lo disponible» ahora se hace contra el saldo local (puede estar atrasado); el servidor es quien decide.

## Bloques que siguen
- **S3 (app):** indicador único de sincronización y pantallas leyendo lo local (Consultar, Conteo, Acomodar, Transferir, Ajustar, Daño, Despacho).

## A revisar por Luis
- Tamaño de la descarga inicial en Depot (cuántos saldos con existencia tiene el almacén activo): se medirá al construir S2.
- Prueba en el Zebra al fondo del almacén cuando salgan S2 y S3.
