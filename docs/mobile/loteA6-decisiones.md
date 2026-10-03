# Lote A6 (app de almacén) — captura de conteo rechazada porque el supervisor ya corrigió una línea — decisiones

Fecha: 2026-10-03. Rama `claude/company-settings-screen-plan-uajc2i`. Resuelve el pendiente "App de almacén" del cambio 2 de
[decisiones-del-dueno-2026-10-03.md](../decisiones-del-dueno-2026-10-03.md) (protección de la corrección del supervisor). Documento propio
(no se amplió `loteA5-decisiones.md`): A5 implementó las decisiones 4 y 5; esto es otro pendiente, de otra pantalla. No toca el servidor
(`src/`), la web (`web-app/`) ni `Diseño/`. Manual: [capítulo 9 §9.1](../manual/09-app-almacen.md) y
[FAQ, sección Lote A6](../manual/faq.md#lote-a6--app-de-almacén-captura-de-conteo-rechazada-porque-el-supervisor-ya-corrigió-una-línea).

## Qué cambió

- `features/count/countRejection.ts` (lógica pura):
  - `classifyCountBatchRejection(row)`: una fila `rejected` de la cola es "el 409 de línea corregida" si `kind === 'countBatch'` y su
    `last_error` empieza con el mensaje exacto del servidor `La línea ya fue corregida por el supervisor; no se puede volver a capturar.`
    (`CORRECTED_LINE_LOCKED`, copia de `CycleCountRules.CorrectedLineLocked`). Devuelve el id del conteo (de la ruta guardada
    `/api/v1/cycle-counts/{id}/lines/batch`), los renglones y el mensaje.
  - `parseLockedRows(message)`: lee `Renglón(es) del lote: 2 (SKU-A), 5 (SKU-B). No se guardó nada.` **solo** si todo el tramo tiene ese
    formato (renglón 1–9999, SKU sin paréntesis; el SKU puede llevar comas y espacios). Cualquier otra cosa → `null` y la pantalla muestra
    el mensaje tal cual. A cada renglón se le agrega lo que este aparato mandó (`countedQty` del cuerpo guardado en la cola, renglón n →
    `lines[n-1]`, el mismo índice que usa el servidor).
  - `countRefreshState(statusCode, isActive)`: `OPEN` → abierto, `COUNTED` → Contado, reconciliado (o un código desconocido) → cerrado,
    `isActive = false` → no existe. `summarizeRefreshedLines`, `countIdFromFinishPath`.
- `features/count/countRejectionApi.ts`: `fetchCountForRejection(countId)` → `GET /api/v1/cycle-counts/{id}`; mapea cada línea a SKU,
  posición, lote, `countedQty` (vigente), `wasCorrected`, `correctedByName`. **No copia `systemQty`** (la cantidad esperada nunca llega a
  la pantalla). 404 → `notFound`; sin red → `offline`; otro error → su `title`.
- `features/count/RejectedCountBatch.tsx`: tarjeta con borde rojo; arriba el aviso grande del kit del Lote A3 (`ScanMessage`, letra 20,
  blanco sobre `#B42318`), el resto en letra 16–18 con `colors.text` sobre el panel. Botones: **Actualizar el conteo** (solo al tocarlo;
  sin id de conteo no aparece y se dice por qué), **Reintentar: countBatch** y **Descartar este envío**. Tras actualizar: estado, resumen
  y hasta 30 líneas (`MAX_REFRESHED_LINES_SHOWN`), "… y N líneas más (míralas en la web)".
- `app/sync.tsx`: las filas rechazadas que clasifican así salen como tarjeta, antes de la lista; las demás siguen en la lista de siempre
  (con "Descartar" y "Reintentar"). Si el cierre (`countFinish`) del mismo conteo también quedó rechazado, la tarjeta lo dice.
- i18n es/en: sección `countRejection` (22 claves). Las de estado son planas (`stateOpen`, …) porque una prueba existente recorre los
  diccionarios con dos niveles.

## Cómo se probó (resultados reales de esta sesión)

| Comando (en `app-almacen/`) | Resultado |
|---|---|
| `npm run check` antes de los cambios (dependencias ya instaladas; no hizo falta `npm ci --legacy-peer-deps`) | Pasó: 56 suites, 285 pruebas. |
| `npm run check` a mitad (primera versión) | Falló `tsc`: `useScanner.test.tsx` convierte los diccionarios a `Record<string, Record<string, string>>` y el subobjeto `countRejection.state` lo rompía. Se aplanaron las claves de estado. |
| `npm run check` al final (`api:types` + `tsc -b` + `oxlint` + `jest`) | **Pasó**: `api:types` sin diferencias en `schema.d.ts`, `tsc` sin errores, `oxlint` sin hallazgos, **60 suites, 316 pruebas**. |
| Mutación a mano: `classifyCountBatchRejection` siempre `null` | 5 pruebas fallaron (lógica y pantalla); se restauró. |
| Mutación a mano: `parseLockedRows` sin validar el formato | 1 prueba falló (formatos inválidos); se restauró. |
| Pruebas nuevas de pantalla corridas 3 veces seguidas | 13/13 cada vez. |

Pruebas nuevas (Jest):
- `countRejection.test.ts`: mensaje igual al del servidor; reconoce el 409 con y sin lista; extrae renglón/SKU (uno, varios, SKU con coma,
  espacio y barra); rechaza formatos ambiguos (SKU con paréntesis, sin cierre, renglón 0 o no numérico, SKU vacío, lista vacía, otro
  separador, otro mensaje); rutas; clasificación con cantidades del cuerpo, cuerpo ilegible, ruta desconocida, otros rechazos; estados;
  resumen; las claves existen en es y en; los textos clave exactos.
- `countRejectionApi.test.ts`: GET por id, mapeo sin `systemQty`, Contado y reconciliado, 404 y dado de baja, sin señal, 403.
- `RejectedCountBatch.test.tsx`: explicación con letra ≥ 16, renglones con lo mandado, sin pedir nada al montar; mensaje ilegible tal cual;
  sin id de conteo; aviso del cierre rechazado; actualizar con abierto (corregida con y sin nombre, sin contar, sin "Esperado"); más de 30
  líneas; `notFound`, `offline`, error del servidor y reconciliado (y que el botón sigue para reintentar); fallo inesperado; Descartar y
  Reintentar.
- `__routeTests__/syncCorrectedLineScreen.test.tsx` (de punta a punta, base SQL real, `fetch` falso): `enqueueFinishCount` → `runOutbox` con
  409 al lote y 422 al cierre → Sincronización muestra la tarjeta (con el renglón y "mandaste 6"), el cierre sigue en la lista con su
  mensaje, no se pide el conteo hasta tocar el botón; primero sin señal, luego el conteo vigente a ciegas; Descartar quita solo esa fila.

**Lo que NO se pudo comprobar aquí**: no hay emulador ni SDK de Android: no se compiló el APK (job `android` del CI) ni se probó en el
Zebra. No hay recorrido Maestro para esto (haría falta un supervisor corrigiendo en la web a mitad del recorrido).

## Lista de comprobación para el aparato (la ejecuta Luis con el APK del CI)

Preparación: un usuario operario con solo `warehouse.count.capture` en el aparato y un supervisor (`warehouse.count`) en la web. Una
posición con **un solo producto** (así el supervisor puede terminar el conteo con una sola captura). Cuándo pasa en la vida real: cuando
el lote del aparato llega **después** de que el supervisor corrigió (por ejemplo, el aparato estuvo sin señal o se reintentó un envío).

1. En el aparato, con señal, Conteo → Por posición → escanear la posición (se crea el conteo). Poner el aparato **en modo avión**,
   capturar el producto con **5** y "Terminar esta posición" (Sincronización: 2 pendientes).
2. En la web, el supervisor abre ese conteo, captura **3**, lo **termina** (Contado) y luego **corrige** la línea a **4** (queda "corregida
   por el supervisor").
3. En el aparato, quitar el modo avión → **Sincronizar ahora** → "Con error (2)": arriba, la tarjeta roja **"El supervisor ya corrigió una
   línea de este conteo."**, "No se guardó nada de este envío…", `• Renglón 1: SKU (mandaste 5)`, "Qué hacer…" y la aclaración, y "El
   cierre de este mismo conteo también quedó con error…"; debajo, el cierre con su mensaje (`El conteo ya se terminó; puede corregir la
   captura o reconciliarlo.`). En la web la línea sigue en 4.
4. Letras: la tarjeta se lee completa en la pantalla de 4" con la fuente normal y con la grande; nada se corta a lo ancho.
5. Modo avión → **Actualizar el conteo** → "Sin señal: no se pudo actualizar el conteo…". Con señal → "El conteo CC-… ya se terminó de
   contar (Contado): pide al supervisor que lo revise.", el resumen y la línea `Corregida por el supervisor (nombre) · Contado: 4`.
   **No aparece ninguna cantidad esperada.**
6. En la web, reconciliar ese conteo → en el aparato, Actualizar el conteo → "ya fue reconciliado… Puedes descartar este envío."
7. **Descartar este envío** → la tarjeta desaparece y "Con error" baja en 1; el cierre se descarta con su propio "Descartar".

## Decisiones para el dueño (tomadas con el valor más seguro)

1. **La app no reabre el conteo.** El texto pedido dice "Vuelve a abrir el conteo y captura de nuevo solo las líneas que el supervisor no
   corrigió (o pide al supervisor que lo revise)", y se muestra tal cual, pero hoy la app **no puede** retomar un conteo ya enviado
   (escanear la posición crea un conteo **nuevo**, y la web no deja capturar a quien solo tiene `warehouse.count.capture`). Por eso la
   tarjeta agrega: "Esta app no reabre un conteo ya enviado: avisa al supervisor; él lo revisa en Conteo cíclico de la web." En la práctica,
   quien resuelve es el supervisor. Si se quiere que el operario lo resuelva desde el aparato, ver el pendiente 1.
2. **No se reenvía nada por su cuenta, ni "sin la línea corregida".** La app conoce los renglones bloqueados y podría mandar el lote sin
   ellos, pero eso es justo lo que el dueño decidió **no** hacer en el servidor (decisión 3 del cambio 2: todo o nada). No se hizo; si se
   quiere, conviene hacerlo en el servidor (`CaptureBatchAsync`), no en la app.
3. **"Reintentar" sigue en la tarjeta** (como en todo rechazo). Manda el mismo lote: falla igual mientras la corrección siga ahí. Se dejó
   para no quitar una acción existente; si confunde, se puede ocultar solo para esta tarjeta.
4. **Se reconoce por el mensaje exacto del servidor.** La cola guarda solo el texto del error (`last_error`), no el código HTTP; cambiar
   la tabla de la cola para guardar el código sería tocar el esquema local por poco. Si el servidor cambia el texto de
   `CycleCountRules.CorrectedLineLocked`, la tarjeta deja de salir (la fila cae en la lista normal con su mensaje: no se pierde nada). Lo
   protege la prueba que compara el texto.
5. **Al actualizar se muestra el valor vigente de cada línea, incluido el de una línea corregida** (`Corregida por el supervisor (Beto) ·
   Contado: 5`), también en conteo a ciegas: el servidor lo manda a quien solo captura (es evidencia, no la cantidad esperada). La cantidad
   esperada (`systemQty`) no se muestra nunca, ni a quien tiene `warehouse.count`. Si prefiere ocultar también el número corregido en
   conteo a ciegas, es una línea en `RejectedCountBatch.tsx`.
6. **"Contado" se trata aparte de "abierto"**: con el conteo Contado el operario ya no termina nada; se le dice que pida la revisión. Un
   estatus desconocido se trata como reconciliado (no invita a capturar).
7. **Hasta 30 líneas** en la tarjeta tras actualizar; el resto se resume y se remite a la web.

## Pendientes

1. **Retomar en la app un conteo ya enviado** (por id, con sus líneas vigentes y sin las corregidas) para que el operario capture de nuevo
   solo las libres desde el aparato. Es una función nueva de Conteo (y de la base local), no de Sincronización.
2. Comprobación en el Zebra (lista de arriba) y compilación del APK en el job `android` del CI.
3. Los demás rechazos de la cola siguen mostrando el tipo técnico (`countBatch`, `receipt`…) como título de la fila; un texto legible por
   tipo sería una mejora aparte.
4. `package-lock.json` desincronizado para `npm ci` sin `--legacy-peer-deps` (anterior; en esta sesión no hizo falta reinstalar).
