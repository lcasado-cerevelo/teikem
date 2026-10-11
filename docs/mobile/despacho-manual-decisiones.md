# Despacho manual en la app — decisiones (2026-10-11)

Decisión del dueño: en Despacho, **«Completar despacho» pasa a ser el despacho manual** del servidor (lote 31, `docs/lote31-decisiones.md`;
`POST /api/v1/manual-issues`, permiso `warehouse.issue`, documento `DMA-#####`). **«Empacar» no cambia.** No es a ciegas: la existencia y la
posición sugerida se siguen mostrando. Manual: `docs/manual/09-app-almacen.md` §6.y y `docs/manual/faq.md` («Despacho manual en la app»).

## Qué se construyó (solo `app-almacen/`)
- **Pantalla** (`src/app/dispatch.tsx`): «Completar despacho» resuelve las posiciones y abre el paso `¿Por qué sale sin entrega?`: motivos como
  botones grandes de un renglón (estilo de los motivos de Ajustar), nota opcional (≤ 500, con contador), **Despachar** → confirmación → cola.
  Aviso verde con el número `DMA-…` si se envió en los ~3 s de `flushNow`, o «en cola» si no. Sin `warehouse.issue` el botón no aparece y se lee
  un aviso (otro si el aparato aún no conoce los permisos del usuario).
- **Cola** (`kernel/sync/outbox.ts`): kind `manualIssue` (POST, `Idempotency-Key`, FIFO). `outboxResult(id)` lee la respuesta guardada (número DMA).
  `collect` se conserva para las filas que ya estén en la cola de un aparato.
- **Saldo local** (`kernel/warehouse/balanceProjection.ts`): `issueDeltas` resta lo que sale de cada posición; como el cuerpo no lleva lote (el
  servidor saca por FEFO dentro de la posición), el lote se decide al encolar con la misma regla y el **efecto exacto se guarda con la fila**
  (`outbox.projection_json`, **SQLite v11**). Deshacer (rechazo), reintentar y la bajada de saldos usan ese efecto guardado.
- **Atómico**: encolar, restar del saldo local y cerrar el despacho del aparato van en **una** transacción (`queueManualIssue`): nunca quedan a la vez el
  despacho abierto y su envío pendiente. Si el servidor lo rechaza dentro de la espera, se quita la fila y el despacho se reabre igual
  (`restoreLocalPick`) con el mensaje exacto del servidor.
- **Motivos** (`features/dispatch/manualIssueReasons.ts`): copia en kv `manualIssueReasons` de `GET /api/v1/manual-issues/reasons`, bajada en la pasada de
  sincronización como mucho cada 30 min y **solo si el usuario tiene `warehouse.issue`** (un 403 deja `PERMISSION_DENIED` en el servidor); 403/404 se
  saltan sin borrar la copia. Sin copia: los cinco de fábrica (i18n).
- **Cola, corrección general**: un rechazo se decide también por el **estatus HTTP** (400/403/404/409/422), no solo por `code`. Antes un 409
  `insufficient_stock` (o un 403 `module_disabled`) dejaba la fila **pendiente para siempre** y el saldo local sin deshacer; afectaba también a
  Transferir, Ajustar y Empacar.
- `schema.d.ts` regenerado desde `web-app/openapi.json` (`npm run api:types`).

## Cómo se probó
`npm run check` de la app (tipos, `tsc`, oxlint, jest): **623 pruebas** (antes 598): reglas puras, motivos (bajada, permiso, 403/404, caducidad),
cola (`manualIssue`, `outboxResult`, rechazo por estatus), saldo local (FEFO por lote, deshacer exacto, reintento, bajada con pendientes),
migración v11, `restoreLocalPick` y la pantalla (envío con DMA, en cola, rechazo con mensaje exacto y despacho reabierto, motivos de la compañía,
sin permiso, posición inexistente). No se probó en el Zebra ni contra el API real.

## Decisiones a revisar
1. **Rechazo inmediato = el despacho vuelve a la pantalla** (no queda en Sincronización), para corregir sin volver a escanear. Uno que se rechaza
   más tarde sí queda en Sincronización.
2. Tras completar, la pantalla **se queda en Despacho** (antes volvía a Inicio) para que se lea el número DMA.
3. Un 403 sin cuerpo (permiso retirado y el aparato no lo sabe) se lee como `Ocurrió un error. Intente de nuevo.`: la app no tiene un mensaje propio
   para «sin permiso» en `kernel/api/problem.ts`.
4. La copia del orden de salida (`stock_exit`, la posición sugerida) no se descuenta al encolar (igual que con Empacar); se vuelve a bajar en cuanto
   el despacho llega al servidor.
