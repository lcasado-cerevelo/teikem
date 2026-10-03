# Lote A4 (app de almacén) — "Contar por producto" — decisiones

Fecha: 2026-10-03. Rama `claude/company-settings-screen-plan-uajc2i`. Parte 5c (app del Zebra) del conteo cíclico por producto.
Número: `loteA1` y `loteA2` están reservados en `docs/plan-de-trabajo.md`, `loteA3` es el de las mejoras del Zebra; este es el
**A4**, el siguiente libre en `docs/mobile/` (no confundir con la "pieza A4 Conteo" de la tabla de `app-almacen-plan.md` §4, que
fue parte de la segunda entrega del Lote 8A-app).

Especificación: [conteo-por-producto-diseno.md](../conteo-por-producto-diseno.md) (sección "App móvil — Contar por producto",
aprobada por el dueño) sobre el contrato del servidor del [Lote 21](../lote21-decisiones.md) ("Receta para la app"). No toca el
servidor (`src/`), la web (`web-app/`) ni la maqueta. Manual: [capítulo 9 §7](../manual/09-app-almacen.md#7-conteo) y
[FAQ](../manual/faq.md#lote-a4--app-de-almacén-contar-por-producto).

## Qué se construyó

1. **Dos caminos en Conteo** (`src/app/count.tsx`): arriba, "¿Cómo vas a contar?" con dos opciones del mismo ancho (caben en
   360 px), **Por posición** (sin cambios: escanear la posición abre el conteo) y **Por producto**. La elección se recuerda en el
   aparato (kv `countEntryMode` de la base de la compañía); la primera vez, por posición. Si en "Por posición" se escanea un código
   que no es una posición pero sí un producto, el aviso dice "Ese código es de un producto. Para contarlo así, toca «Por producto»."
2. **Por producto** (`features/count/countApi.ts#startProductCountOnline`, `ProductCountView.tsx`):
   - Se escanea o teclea el producto en el `ScanField` (escanear = Aceptar), resuelto contra el catálogo local
     (`findProductByCode`).
   - **Guarda de serie** (`countLogic.ts#productCountBlocker`): con `trackingTypeCode = SERIAL` el aviso es "Este producto se cuenta
     por número de serie; cuéntalo desde la web por ahora." y **no se llama al servidor** (no se crea ningún conteo).
   - Si no, `POST /api/v1/cycle-counts { warehousePublicId, productPublicIds:[id] }` (sin `binIds`, necesita señal) y se guardan
     **todas** las líneas en la base local, en blanco. La lista muestra **una fila por posición** (código grande) y, si el producto
     lleva lote, el **número de lote** en la fila ("Lote L-3"); solo se muestra. Cada fila tiene **un espacio para la cantidad**
     (teclado numérico, 96 dp, el texto de la fila se envuelve y nunca empuja el espacio fuera de la pantalla de 360 px).
   - **Esperado**: "Esperado: N" (con los separadores de la compañía) solo si el servidor lo trajo (conteo no a ciegas); a ciegas,
     el aviso de siempre y nada en su lugar. No cambia quién ve las cantidades.
   - **Sin existencia**: el servidor responde 400 con `errors.filters` ("Los filtros no seleccionan inventario en mano para contar;
     amplíe los filtros o agregue líneas a mano."): se muestra tal cual en el bloque rojo, no queda nada abierto y se ofrece
     **Contar por posición** (ver decisión 3 y pendientes: "Otra posición" no se puede ofrecer ahí).
3. **Reglas de captura** (decididas por el dueño, `countLogic.ts`):
   - Un espacio **en blanco es cero** (`blankAsZero`); al confirmar viajan **todas** las filas, las en blanco como `countedQty: 0`
     (el servidor borra la captura si llega vacía).
   - **Confirmar** es el que cierra. Mientras haya blancos, justo encima del botón se lee la línea de resumen "{n} posiciones en
     blanco se toman como 0." (o "1 posición en blanco se toma como 0."): tocar Confirmar la acepta, sin diálogo ni paso extra.
     Algo que no es una cantidad marca el espacio en rojo y apaga Confirmar ("Hay cantidades que no son un número; corrígelas para
     confirmar.").
   - **No hay botón "todo aquí".**
   - **Buscador** ("Buscar posición o lote") solo con **más de 6** posiciones: `PRODUCT_COUNT_SEARCH_THRESHOLD = 6` en
     `countLogic.ts`. Filtra por código de posición o número de lote; el resumen sigue contando todas las filas.
4. **"Otra posición"** (enlace al final de la lista, `OtherBinForm.tsx`):
   - **Zonas**: `GET /api/v1/warehouses/{publicId}/zones` (activas). Pide `inventory.view`, que ya hace falta para entrar con el PIN.
     Si la llamada falla (sin señal, 403…), se usan las zonas de las posiciones sincronizadas (tabla local `bin`, `GET /sync/bins`;
     solo trae zonas que ya tienen posiciones) con el aviso "Sin respuesta del servidor: se muestran las zonas guardadas en el
     aparato.". Una sola zona queda elegida sola.
   - Datos: **código** de la posición o **pasillo/rack/nivel/posición** (dos columnas), y si el producto lleva lote, **número de
     lote** (obligatorio: el servidor lo exige para una línea nueva) y **vencimiento** opcional en el orden de fecha de la compañía
     (`LotInput` no pide más: la fecha de fabricación no se pide). Escanear una etiqueta de posición escribe su código en el campo
     (no la crea sola: falta la zona).
   - Antes de crear: si esa posición (y ese lote) **ya está en la lista**, aviso "La posición {bin} ya está en la lista: escribe la
     cantidad ahí." (mandar dos renglones de la misma línea haría que el servidor rechazara el lote de captura entero, que ya va en
     la cola). Si la posición **ya existe** en las posiciones sincronizadas, se usa tal cual sin crear nada (no necesita señal).
   - Si no: `POST /api/v1/cycle-counts/{id}/bins { zoneId, code | aisle/rack/level/position }` (necesita señal). La fila entra a la
     lista con "Pendiente de revisión" y el aviso verde "Posición {bin} agregada (pendiente de revisión)."; viaja en el lote de
     captura como línea nueva `{ binId, productPublicId, lot:{ number, expiryDate? }, countedQty }`.
   - Errores del servidor tal cual (`apiErrorMessage`: los mensajes por campo si los hay, si no el título): 400 (zona, código,
     caracteres), 404 (`Conteo no encontrado.` / `Zona no encontrada.`), 422 (zona o almacén inactivo, conteo reconciliado) y
     **409** `Ya existe una posición con ese código en el almacén.`; con el 409 la app busca la posición en el servidor y, si la
     encuentra, ofrece **"Usar {bin}, que ya existe"** (entra sin la marca provisional). Sin señal: "No se pudo crear la posición:
     necesita señal. Intenta de nuevo cuando haya señal.".
   - Una fila de "Otra posición" se puede quitar con ✕ (las del servidor no: en blanco son 0). Quitarla no borra la posición
     provisional creada en el servidor (la revisa el supervisor).
5. **Persistencia y cola de salida** (`kernel/db/schema.ts` v4, `features/count/localCount.ts`):
   - `local_count`: `bin_id` pasa a ser opcional; columnas nuevas `mode` (`BIN`/`PRODUCT`), `product_public_id`, `sku`,
     `product_name`, `tracking_type_code`.
   - `local_count_line`: cada línea guarda su posición (`bin_id`, `bin_code`), su lote (`lot_id`, `lot_number`, `lot_expiry_date`) y
     `is_provisional_bin`; `counted_qty` admite NULL (= en blanco).
   - **Migración v4**: SQLite no quita un `NOT NULL` con `ALTER`, así que se reconstruyen las dos tablas copiando todo, con las
     llaves foráneas apagadas (si no, borrar la tabla vieja borraría en cascada las líneas) y dentro de una transacción que también
     fija `user_version = 4`: o queda migrada entera o no se toca. Un conteo por posición en curso queda `mode = 'BIN'` y sus líneas
     **heredan la posición** del conteo.
   - `buildBatchItems(entries)` usa la posición de **cada** línea (antes recibía la del conteo). Terminar sigue encolando, en
     orden, `countBatch` (`PUT /cycle-counts/{id}/lines/batch`) y `countFinish` (`POST /cycle-counts/{id}/finish`): capturar y
     confirmar funcionan sin señal; abrir el conteo y crear una posición necesitan señal.
   - **Un conteo a la vez por aparato** (de posición o de producto; Inicio sigue avisando "Termina o cancela el conteo en curso…").
   - **Retomar**: el conteo por producto guarda sus filas al abrirse y cada cambio de cantidad se guarda al escribirlo; al volver a
     abrir la app, Conteo muestra la lista tal como quedó **sin pedir nada al servidor**.
6. **Cancelar**: "Cancelar conteo" (con confirmación) llama `DELETE /cycle-counts/{id}` y libera el aparato, igual que el de
   posición (necesita señal y el permiso completo `warehouse.count`, regla de antes).

Archivos: `src/app/count.tsx`, `src/features/count/{countLogic,countApi,localCount}.ts`, `ProductCountView.tsx`,
`OtherBinForm.tsx`, `src/kernel/db/{schema,kv}.ts`, `src/kernel/api/{problem,client}.ts` (`apiErrorMessage`),
`src/kernel/i18n/{es,en}.json`; pruebas en `src/features/count/*.test.ts`, `src/kernel/db/database.test.ts` y
`src/__routeTests__/count*.test.tsx` (con `countKit.ts`).

## Cómo se probó (resultados reales de esta sesión)

| Comando (en `app-almacen/`) | Resultado |
|---|---|
| `npm ci --legacy-peer-deps` (lo que corre el CI; el `npm ci` normal falla por el lock desincronizado, anterior a este lote) | Pasó. |
| `npm run check` antes de los cambios | Pasó: 45 suites, 236 pruebas. |
| `npm run check` al final (`api:types` + `tsc -b` + `oxlint` + `jest`) | **Pasó**: `api:types` sin diferencias en `schema.d.ts`, `tsc` sin errores, `oxlint` sin hallazgos, **51 suites, 266 pruebas**. |
| Prueba de mutación a mano (blanco = 1 en vez de 0 y umbral `>=` en vez de `>`) | 5 pruebas fallaron, como debían; se restauró el código. |

Pruebas nuevas o ampliadas (Jest):
- **Lógica pura** (`countLogic.test.ts`): blanco = cero, resumen de blancos e inválidos, cuándo se puede confirmar, umbral del
  buscador (6 → no, 7 → sí, ajustable), filtro por posición o lote, guarda de serie, código de la posición nueva (escrito o
  compuesto como el servidor), posición ya listada (con lote), y armado del lote con **varias posiciones y filas nuevas** (con
  lote y vencimiento, sin lote, en 0).
- **Base local** (`database.test.ts`, `localCount.test.ts`): esquema v4 (columnas y nulos), **migración v3 → v4 con un conteo por
  posición en curso** (se conserva, queda `BIN`, sus líneas heredan la posición, la llave foránea sigue en cascada, el
  `AUTOINCREMENT` sigue después de las copiadas); conteo por producto: filas en blanco, retomar, guardar/limpiar cantidades,
  "Otra posición", lo que viaja con blancos como 0, un conteo a la vez, descartar.
- **API** (`countApi.test.ts`): cuerpo del alta por producto y mapeo de líneas (lote, provisional), 400 de "sin existencia"
  reconocido, zonas del servidor y respaldo local (403), cuerpo de `POST .../bins` solo con lo escrito, 409 con su mensaje,
  posición sincronizada por código, cola con varias posiciones.
- **Pantallas** (`expo-router/testing-library` contra la base SQL real y un `fetch` falso por ruta):
  `countEntryModesScreen` (dos caminos, se recuerda, serie → aviso sin llamar al servidor, producto inexistente),
  `countProductListScreen` (producto con lote: una fila por posición y lote con "Esperado" y separadores; sin buscador con 3;
  resumen 3 → 2 blancos; inválido apaga Confirmar; Confirmar encola lote con blancos = 0 y cierre), `countProductResumeScreen`
  (retoma sin señal con lo ya escrito, buscador con 7, a ciegas sin "Esperado", cancelar con `DELETE`), `countOtherBinScreen`
  (zona única elegida, lote obligatorio, vencimiento inválido y válido, posición provisional en la lista y en el lote de captura),
  `countOtherBinConflictScreen` (posición ya listada sin llamar al servidor, 409 en claro y "Usar R-01-02, que ya existe"),
  `countProductNoStockScreen` (400 en claro, nada abierto, "Contar por posición"). Las pruebas de Conteo existentes
  (`countScreen`, `countPrefillScreen`) pasan sin cambios.

**Lo que NO se pudo comprobar aquí**: no hay emulador ni SDK de Android (no se compiló el APK ni se probó en el Zebra; el árbitro
es el job `android` del CI); no se tomaron capturas. **Maestro**: no hay recorrido de Conteo en `e2e-maestro/` (los existentes son
registrar/entrar, recibir, despacho y recibo sin señal; ninguno pasa por Conteo), así que no hubo nada que ajustar; no se agregó uno
nuevo porque no se puede correr aquí.

## Lista de comprobación para el aparato (la ejecuta Luis con el APK del CI)

Instalar el APK del job `android` encima de la versión actual (no hace falta desinstalar).

1. **Actualizar con un conteo por posición a medias**: antes de instalar, dejar un conteo por posición abierto con algo capturado.
   Tras instalar, Conteo lo retoma igual (misma posición y lo capturado); terminarlo y revisar en la web que llegó todo.
2. **Dos caminos**: Conteo muestra "Por posición" y "Por producto"; elegir uno, salir y volver: sigue elegido.
3. **Producto con 1 posición**: Por producto → escanear (gatillo, sin tocar la pantalla) → una sola fila; escribir la cantidad →
   Confirmar → vuelve a Inicio; en la web, el conteo (origen "Por producto") queda Contado con esa cantidad.
4. **Producto con 3 posiciones**: escribir en una sola; encima de Confirmar debe leerse "2 posiciones en blanco se toman como 0.";
   un toque en Confirmar cierra; en la web las otras dos quedan en 0.
5. **Producto con más de 6 posiciones**: aparece "Buscar posición o lote"; escribir parte de un código filtra; el resumen sigue
   contando todas. Con 6 o menos no aparece.
6. **Blanco = 0 sin escribir nada**: Confirmar con todo en blanco funciona y todas quedan en 0 (ver decisión 8).
7. **Otra posición**: tocar "Otra posición", elegir la zona, escribir un código nuevo (o pasillo/rack/nivel/posición) →
   "Agregar posición" → la fila aparece con "Pendiente de revisión"; escribir la cantidad y Confirmar. En la web, la posición sale
   en "pendientes de revisión" y la línea en el conteo. Repetir con un código que **ya existe** → "Ya existe una posición con ese
   código en el almacén." y el botón "Usar …, que ya existe". Escanear una etiqueta de posición en ese paso escribe su código.
8. **Producto con lote**: las filas muestran "Lote …"; en "Otra posición" se pide el número de lote (sin él no deja agregar) y el
   vencimiento opcional en `MM/DD/AAAA` (Puerto Rico).
9. **Producto con serie**: Por producto → escanear → aviso rojo "Este producto se cuenta por número de serie; cuéntalo desde la web
   por ahora." y en la web **no** aparece ningún conteo nuevo.
10. **Producto sin existencia**: escanear uno sin existencia → el mensaje del servidor y el botón "Contar por posición".
11. **Sin señal**: abrir un conteo por producto con señal, poner modo avión, escribir cantidades, cerrar la app (quitarla de
    recientes), abrirla: Conteo muestra la lista con lo escrito. Confirmar sin señal → Inicio dice "2 pendientes de enviar"; quitar el modo
    avión → se envían y el conteo queda Contado en la web. "Otra posición" sin señal dice que necesita señal.
12. **Cancelar**: con un usuario con `warehouse.count`, abrir un conteo por producto y "Cancelar conteo" → confirma → en la web el
    conteo queda cancelado. Con un usuario solo con `warehouse.count.capture`, cancelar devuelve el error de permiso (regla de antes).
13. **Retomar**: con un conteo por producto abierto, ir a Inicio: Recibir y Despacho avisan "Termina o cancela el conteo en curso
    antes de usar esto."; Conteo lo retoma.
14. **Pantalla**: en la lista y en "Otra posición", ningún texto cortado ni desplazamiento a los lados con la fuente normal y grande;
    el espacio de cantidad siempre visible aunque el código de la posición sea largo.

## Decisiones para el dueño

1. **Por posición de entrada y se recuerda la última forma elegida** (por aparato y compañía, kv `countEntryMode`). Si se prefiere
   que siempre empiece en "Por producto" (las posiciones están mal etiquetadas), es cambiar el valor por defecto en `count.tsx`.
2. **La línea de resumen está siempre a la vista encima de Confirmar** mientras haya blancos (no aparece solo después de tocar):
   así, tocar Confirmar es aceptarla, en un solo toque y sin diálogo. Va en ámbar y en negrita.
3. **Producto sin existencia: no se ofrece "Otra posición"** (se pidió). El servidor no crea un conteo sin líneas (400) y "Otra
   posición" necesita un conteo abierto (`POST /cycle-counts/{id}/bins`), así que no hay cómo ofrecerla sin cambiar el servidor. En
   su lugar se muestra el mensaje del servidor, la ayuda "Si lo encontraste en una posición que tiene otros productos, cuéntala «Por
   posición» y agrégalo ahí. Si no, avisa al supervisor." y el botón "Contar por posición". Ver pendientes de backend.
4. **"Otra posición" con un código que ya existe usa la posición existente** (no crea una provisional ni la marca): si está en las
   posiciones sincronizadas del aparato, directo; si el servidor responde 409, con el botón "Usar …, que ya existe". El caso real es
   "lo encontré en una posición que existe donde el sistema no tenía este producto"; crear otra no es posible (código único) y
   detenerse con el 409 dejaría al operario sin salida.
5. **Una fila de "Otra posición" dejada en blanco también viaja como 0** (misma regla para todas las filas): crea en el conteo una
   línea en 0 en esa posición. Se puede quitar con ✕ antes de confirmar; quitarla no borra la posición provisional del servidor.
6. **Lote nuevo**: número obligatorio y vencimiento opcional; no se pide fecha de fabricación (el servidor no la exige). El número
   de lote se manda tal como se escribe (sin pasar a mayúsculas), porque el servidor busca el lote existente por su número.
7. **Lo escrito se guarda a cada cambio** en la base local (para poder retomar); lo que no es una cantidad no se guarda y apaga
   Confirmar.
8. **Confirmar con todas las filas en blanco está permitido** (todo en 0: "no encontré nada"). Riesgo: un operario a ciegas que abrió
   un producto por error y no puede cancelarlo (cancelar exige `warehouse.count`) lo cerraría en 0; el ajuste nunca es automático
   (la reconciliación es del supervisor en la web), así que el riesgo es una revisión de más. Si se prefiere exigir al menos una
   cantidad escrita, es una línea en `canConfirmProductCount`.
9. **Zonas**: se piden al servidor y, si no responde, se usan las de las posiciones sincronizadas (solo zonas con posiciones).
   No se filtran por tipo (recepción, cruce de muelle…): cualquier zona activa se puede elegir.
10. **Buscador**: busca por código de posición o por número de lote (el lote solo se muestra; buscarlo por texto en la lista no es
    "búsqueda por lote" del servidor). Umbral 6 en `PRODUCT_COUNT_SEARCH_THRESHOLD`.
11. **Orden de la lista**: el del servidor (por código de posición, luego lote); las filas de "Otra posición" al final.
12. **Escanear un producto en "Por posición"** (cuando el código no es de una posición) sugiere cambiar a "Por producto" en vez de solo
    "No hay una posición con ese código.".

## Pendientes

- **Backend (no se parcheó)**: permitir abrir un conteo **por producto sin existencia** (vacío, solo para registrar "Otra posición"),
  p. ej. un indicador `allowEmpty` en `POST /cycle-counts` cuando viene solo `productPublicIds`. Con eso la app ofrecería "Otra
  posición" también en el caso de la decisión 3.
- **App**: la tabla local `bin` no guarda `isProvisional` de `sync/bins` (la descarga no lo copia): una posición provisional ya
  sincronizada que se usa desde "Otra posición" (decisión 4) entra a la lista sin la marca "Pendiente de revisión" (el servidor sí la
  conoce). Requiere una columna más (esquema v5) y copiarla en `kernel/sync/download.ts`.
- **App (mejora posible, no pedida)**: escanear la etiqueta de una posición en la lista del conteo por producto podría llevar a su
  fila (hoy el lector no hace nada en la lista; sí escribe el código en "Otra posición").
- **Comprobación en el Zebra** (lista de arriba), compilación en el job `android` y recorrido Maestro de Conteo (no existe; el plan de
  la app lo prevé en `app-almacen-plan.md` §4).
- **Capturas** de la pantalla: no se pudieron tomar (sin emulador).
- Lo de antes sigue igual: `package-lock.json` desincronizado para `npm ci` sin `--legacy-peer-deps`.
