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

## Adenda 2026-10-11 (b) — Motivo ya puesto: Completar → Despachar

Decisión del dueño: **el despacho manual no debe complicar el aparato.** El servidor ya marca el motivo por default de la compañía (`isDefault` en
`GET /api/v1/manual-issues/reasons`, `docs/lote31-decisiones.md` adenda (b)) y el POST sigue exigiendo motivo. En la app (solo `app-almacen/`):

- **Copia de motivos** (`features/dispatch/manualIssueReasons.ts`): `mapReason` guarda `isDefault`; la copia lleva la marca `withDefault` y una copia de una
  versión anterior (sin la marca) se vuelve a bajar en la siguiente pasada sin esperar los 30 min.
- **Último usado** (`readLastReason` / `saveLastReason`, kv `manualIssueLastReason` en la base de la compañía, JSON `{userId: código}`): se guarda al
  despachar (enviado o en cola), **no** con un rechazo inmediato; sin sesión ni lee ni guarda; un valor dañado o un código que ya no existe se ignoran.
- **Regla pura** `initialReason` (`features/dispatch/manualIssueLogic.ts`): el que ya estaba escogido (tras un rechazo) → el último usado por el operario
  → el default de la compañía → ninguno; cada uno solo si sigue entre los motivos que se ofrecen (los de fábrica si nunca se bajaron, que no tienen default).
- **Pantalla** (`src/app/dispatch.tsx`): «Completar despacho» resuelve las posiciones y, con motivo puesto, abre **directo la confirmación**
  `¿Despachar sin entrega?` con `Se despacha por: {motivo}`, de dónde salió (último usado / default), **Cambiar** (chico: abre la lista, tocar uno vuelve
  a la confirmación), **Agregar nota** (chico: la nota está escondida y vacía), **Despachar** (grande) y **Volver**. Sin motivo puesto se abre primero la
  lista, como antes. Se quitó el `Alert` de confirmación: la pantalla es la confirmación. Con default o último usado: **2 toques, sin teclado**.
- Sin cambios: el `POST /api/v1/manual-issues` (mismo cuerpo), la cola `manualIssue`, el rechazo inmediato (el despacho vuelve abierto con su motivo y
  su nota) y el aviso con el número DMA. «Empacar» y las demás pantallas no se tocaron.

Cómo se probó: `npm run check` de la app: **650 pruebas** (antes 639; +4 de `initialReason`, +2 de la copia y del último usado, +5 de pantalla:
preselección por default en 2 toques sin teclado, último usado gana al default, último deshabilitado → default, sin default → lista, Cambiar/Volver,
y las de envío, cola y rechazo ajustadas al flujo nuevo). No se probó en el Zebra ni contra el API real.

Decisiones a revisar:
5. **El último usado gana al default** (orden pedido por el dueño). Consecuencia: si un operario cambia el motivo una vez, los siguientes despachos abren
   con ese hasta que vuelva a cambiarlo; un cambio del default en Ajustes no se nota en un aparato donde el operario ya despachó.
6. **«Último usado» por operario** (no por aparato): dos usuarios en el mismo aparato no se pisan.
7. **Un rechazo inmediato no cuenta como usado** (el motivo igual queda puesto en la pantalla para reintentar).
8. **Sin Alert de confirmación**: el riesgo de un toque accidental en Despachar se acepta a cambio de los 2 toques (la pantalla de confirmación
   muestra motivo, líneas y unidades antes del toque).
