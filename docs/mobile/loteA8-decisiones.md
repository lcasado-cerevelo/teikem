# Lote A8 (app de almacén) — lo que hay en una posición, en vivo, al escanearla en Consultar — decisiones

Fecha: 2026-10-03. Rama `claude/company-settings-screen-plan-uajc2i`. Pedido del dueño: complemento de las hojas de posición (servidor
[Lote 23](../lote23-decisiones.md), web [F15](../manual/frontend/f15-hojas-de-posicion.md)). En racks de hasta 5 pisos (20–30 pies) los
productos no tienen código visible o no se alcanzan a escanear; al escanear la **posición** con el Zebra, la app muestra lo que el sistema
dice que hay en ella, así el operario no depende de que la hoja impresa esté vigente. Número: `loteA7` es la adenda del documento A6; este es
el **A8**, el siguiente libre. No toca el servidor (`src/`), la web (`web-app/`) ni `Diseño/`. Manual: [capítulo 9 §8.1](../manual/09-app-almacen.md#81-lo-que-hay-en-una-posición-lote-a8)
y [FAQ, sección Lote A8](../manual/faq.md#lote-a8--app-de-almacén-lo-que-hay-en-una-posición-consultar).

## Revisión previa: qué pantallas ya mostraban el contenido de una posición

| Pantalla | Qué hace al escanear una posición | ¿Lista de lo que hay? | En este lote |
|---|---|---|---|
| **Consultar** | Mandaba el código como **texto libre** (`search`) a `GET /inventory/balances` | Sí, pero imprecisa: el buscador del servidor compara por **subcadena**, así que `A-01` traía también `A-01-01`, `A-01-02`…; una fila por posición × lote, sin agrupar, título "Qué hay en {código}" aunque no fuera una posición | **Se completó** (abajo) |
| Conteo por posición | Abre el conteo (`POST /cycle-counts`) | Sí: "Lo que se espera aquí" (líneas del conteo; nombre y SKU; tocar llena el campo) | Sin cambios (pedido: no tocar) |
| Conteo por producto | — (se escanea el producto) | Lista por posición del producto | Sin cambios |
| Acomodar | **Completa la tarea** | No | Sin cambios: escanear es la acción |
| Recibir directo | **Agrega la línea** en esa posición | No | Sin cambios: escanear es la acción |
| Despacho | **Agrega la línea** (cantidad primero, A5) | No | Sin cambios: escanear es la acción |

Conclusión: el lugar donde escanear una posición es **consultarla** es Consultar; en las otras pantallas la lectura de la posición ya hace
algo (y en Conteo ya hay lista). Ponerles una lista ahí metería una llamada al servidor y una pantalla intermedia en flujos de
"escanear = Aceptar" que el dueño quiso de un solo paso (A3/A5).

## Qué se construyó

1. **Reconocer la posición** (`features/lookup/lookupApi.ts#resolveLookupBin`): si lo escaneado **no** es un producto sincronizado, se busca
   como posición: primero en las **posiciones sincronizadas** del almacén del aparato (tabla `bin`, código exacto sin distinguir mayúsculas,
   activas; no necesita señal), y si no está, en el servidor con `findBinByCode` (el mismo de Conteo y Acomodar: `GET /warehouses/{id}/bins`
   con coincidencia exacta). Si el servidor no responde, se sigue con la búsqueda libre de siempre (que ya sabe avisar sin señal). Una
   posición que solo existe **dada de baja** en el aparato y el servidor no la encuentra activa → `La posición {bin} está desactivada.`
2. **Saldos por la posición** (`fetchBinContents`): `GET /api/v1/inventory/balances?warehousePublicIds=…&binIds={id}` (no por texto
   libre), de 200 en 200 hasta el `total`, máximo 5 páginas (1,000 renglones; más → aviso). Se guarda en `balance_cache` con su propia clave
   (`{almacén}:BIN:{código}`), así sin señal se ve la última lista de **esa posición** con el aviso de siempre ("Datos de las … sin señal").
3. **Lista** (`features/lookup/BinContentsList.tsx`, lógica pura en `lookupLogic.ts`):
   - `aggregateBinContents`: **una fila por producto**, los lotes juntos (suma de en mano y disponible, lotes distintos en el orden en que
     llegan), ordenada por SKU.
   - Fila: **SKU en grande** (20, negrita), nombre (16), `Lote A, B` (16; con más de tres, `Lote A, B, C y 2 más`: `lotsToShow`,
     `BIN_CONTENT_MAX_LOTS_SHOWN = 3`). El texto se envuelve: nada empuja la pantalla a los lados.
   - Encabezado `Qué hay en {posición}` (clave existente) y `{n} productos en esta posición` / `1 producto en esta posición`.
   - **Vacía**: `No hay productos en esta posición.` (no es un error: sin aviso rojo ni vibración de error).
   - **Buscador** `Buscar producto o lote` solo con **más de 6** productos: se reutiliza `showProductSearch` / `PRODUCT_COUNT_SEARCH_THRESHOLD`
     de `features/count/countLogic.ts` (la regla de la lista de "Contar por producto"); filtra por SKU, nombre o lote; sin coincidencias,
     `Ningún producto coincide con «{texto}».` La lista se desplaza con el `ScrollView` de la pantalla (como la del conteo por producto).
   - Las filas **no se tocan** (ver decisión 3).
4. **Cantidades según el permiso** (`kernel/auth/permissions.ts`): `useMyPermissions()` lee los permisos guardados del usuario y, al abrir
   Consultar, los pide al servidor (`GET /api/v1/me`, campo `permissions`); se guardan en la base de la compañía (kv `myPermissions`, por
   `userId`) para decidir sin señal. `canSeeSystemQty` = tiene `warehouse.count` (código exacto, `Perm.warehouseCount`). Sin saberlos (nunca
   llegaron) → **no** se muestran. Con permiso, cada fila dice `En mano: {n} · Disponible: {n}` con los separadores de la compañía.
5. **Búsqueda libre** (ni producto ni posición): igual que antes, con título `Resultados de «{código}»` (antes decía "Qué hay en {código}"
   aunque no fuera una posición). Si no encuentra nada: `No hay un producto ni una posición con ese código.` (antes `No hay nada con ese
   código.`).
6. i18n es/en: `lookup.notFound` (cambiado), `searchResult`, `binEmpty`, `binCountOne`, `binCountMany`, `binSearchLabel`, `binSearchEmpty`,
   `lots`, `lotsMore`, `binInactive`, `binTruncated`. kv: `KvKeys.myPermissions`.

Archivos: `src/app/lookup.tsx`, `src/features/lookup/{lookupApi,lookupLogic}.ts`, `src/features/lookup/BinContentsList.tsx`,
`src/kernel/auth/permissions.ts`, `src/kernel/db/kv.ts`, `src/kernel/i18n/{es,en}.json`; pruebas abajo.

## Cómo se probó (resultados reales de esta sesión)

| Comando (en `app-almacen/`, los del job `mobile` de `.github/workflows/ci.yml`) | Resultado |
|---|---|
| `npm run check` en el HEAD de partida (copia limpia del árbol en un `git worktree` temporal, ya borrado) | Pasó: **63 suites, 335 pruebas**. |
| `npm run check` al final (`api:types` + `tsc -b` + `oxlint` + `jest`) | **Pasó**: `api:types` sin diferencias en `schema.d.ts`, `tsc` sin errores, `oxlint` sin hallazgos, **68 suites, 374 pruebas**. |
| Mutación a mano: `canSeeSystemQty` siempre `true` | 4 pruebas fallaron (3 de `permissions.test.ts` y la pantalla a ciegas); se restauró. |
| Mutación a mano: agrupar por producto **y lote** (sin juntar lotes) | 3 pruebas fallaron (`aggregateBinContents`, la lista con permiso y la pantalla con `warehouse.count`); se restauró. |

Pruebas nuevas (Jest):
- `features/lookup/lookupLogic.test.ts`: lotes agregados (suma y lista), lote repetido, orden por SKU, vacío; `lotsToShow` (pocos / "y N
  más"); filtro por SKU, nombre o lote; clave de caché de la posición distinta de la búsqueda libre.
- `features/lookup/lookupApi.test.ts`: posición sincronizada sin llamar al servidor; posición del servidor por código exacto; no es posición;
  dada de baja; sin señal (no lanza); otro almacén no cuenta; saldos por `binIds` (no `search`); vacía; paginado hasta el total; tope de 5
  páginas marcado; sin señal con y sin caché; la caché de la posición no se mezcla con la búsqueda libre; error del servidor tal cual.
- `kernel/auth/permissions.test.ts`: solo `warehouse.count` (no `.capture`); sin saber → no; sin sesión no pregunta; guarda por usuario;
  cada usuario los suyos; sin señal conserva los guardados; un permiso quitado se refleja en la siguiente consulta.
- `features/lookup/BinContentsList.test.tsx`: con permiso (SKU, nombre, `Lote A, B`, `En mano: 1,250 · Disponible: 1,150`); **sin permiso
  sin ninguna cantidad**; muchos lotes; sin lote; **vacía**; buscador con 6 no, con 7 sí (filtra por lote, el conteo sigue diciendo 7,
  sin coincidencias); letras (SKU 20, el resto ≥ 16).
- Pantallas (`expo-router/testing-library`, base SQL real, `fetch` falso por ruta): `lookupBinContentsScreen` (con `warehouse.count`:
  posición sincronizada, sin llamar a `/bins`, saldos con `binIds=5` y sin `search`, lotes agregados y cantidades), `lookupBinBlindScreen`
  (solo `warehouse.count.capture`: posición del servidor, 7 productos sin cantidades, buscador por lote), `lookupBinStatesScreen` (vacía,
  no encontrada, dada de baja y sin señal sin caché, con `/me` sin responder). La prueba existente `lookupScreen` pasa sin cambios.

**Lo que NO se pudo comprobar aquí**: no hay emulador, SDK de Android ni Zebra: no se compiló el APK (job `android` del CI) ni se probó el
lector real; no se tomaron capturas. **No se corrió el API real**: que `GET /api/v1/me` responda con el token de una sesión del aparato se
dedujo del código (`MeController` solo pide `[Authorize]`, política por defecto `AccessTokenRequirement`, que acepta el token de acceso del
aparato), no se probó contra el servidor. **Maestro**: no se agregó ningún recorrido (el job `android-e2e` es opcional y hoy falla por su
cuenta); ningún recorrido existente pasa por Consultar, así que no cambia ninguno.

## Lista de comprobación para el aparato (la ejecuta Luis con el APK del CI)

Preparación: una posición con **un producto en dos lotes** y otro sin lote; otra con **más de 6 productos**; una vacía; un usuario con
`warehouse.count` y otro solo con `warehouse.count.capture` (a ciegas).

1. Con el usuario **con** `warehouse.count` y señal: Consultar → escanear la etiqueta de la posición (gatillo, sin tocar la pantalla) → sale
   `Qué hay en {posición}`, `2 productos en esta posición`, el SKU en grande, el nombre, `Lote A, B` en una sola fila y `En mano: … ·
   Disponible: …` con la suma de los lotes. No aparece el teclado.
2. Comparar con la hoja impresa de esa posición (F15): los mismos productos.
3. Cambiar al usuario **a ciegas** → Consultar → escanear la misma posición: la misma lista **sin ninguna cantidad**.
4. Posición con más de 6 productos: aparece `Buscar producto o lote`; escribir parte de un SKU o lote filtra; la lista se desplaza hasta el
   final y se ve el botón Volver completo.
5. Posición vacía → `No hay productos en esta posición.` (sin aviso rojo).
6. Escanear una etiqueta que no es nada → `No hay un producto ni una posición con ese código.`
7. Escanear un **producto** → como siempre (saldo por posición, con en mano/disponible).
8. Modo avión → escanear la posición del paso 1 → la lista con `Datos de las {hora} (hace N min, sin señal ahora)`; una posición nunca
   consultada → `Sin señal y sin una consulta anterior de esto.`
9. En la web, quitarle `warehouse.count` al usuario del paso 1; en el aparato, salir a Inicio y volver a Consultar con señal → la lista ya
   no trae cantidades.
10. Letras: con la fuente del sistema normal y grande, el SKU y el nombre largos se envuelven sin cortarse ni desplazar a los lados.

## Decisiones para el dueño (tomadas con el valor más seguro)

1. **Conflicto entre "operarios no ven cantidades" y "quien las ve hoy las sigue viendo igual" en Consultar.** Antes de este lote,
   Consultar mostraba **en mano y disponible a cualquiera con `inventory.view`** (también al buscar una posición), sin mirar
   `warehouse.count`. En la lista nueva de una posición se aplicó la regla del conteo (solo con `warehouse.count`), como se pidió. Efecto:
   un usuario **sin** `warehouse.count` que antes veía cantidades al buscar una posición **ya no las ve** ahí. **La búsqueda por producto y
   la de texto libre no se tocaron** y siguen mostrando cantidades a cualquiera con `inventory.view`: eso contradice la regla (un operario a
   ciegas puede ver lo esperado de una posición consultando el producto antes de contarla). Si se quiere aplicar la regla también ahí, es
   pasar `showQty` a las filas de producto en `app/lookup.tsx` (una línea por subtítulo). No se hizo porque el pedido era no cambiar quién
   ve las cantidades fuera de esta lista.
2. **La app pregunta los permisos (`GET /api/v1/me`)** porque `/inventory/balances` no omite cantidades a quien no tiene `warehouse.count`
   (el conteo sí lo hace en el servidor). Se piden **al abrir Consultar** (no al entrar con el PIN ni en cada sincronización, para no tocar
   esos flujos); un cambio de permisos en la web se ve al volver a abrir Consultar con señal. **Sin saberlos, no se muestran.** Alternativa
   más firme (servidor, **no se hizo**): que `GET /inventory/balances` omita `qtyOnHand`/`qtyAvailable` a quien no tenga `warehouse.count`
   cuando la llamada viene de un aparato, o un `GET /me/permissions` liviano; hoy `/me` también devuelve membresías, módulos y alcances.
3. **Tocar un producto de la lista no hace nada.** Consultar no tenía ninguna acción sobre una fila y se pidió no inventar flujos. Si se
   quiere, lo natural sería que tocar la fila haga la consulta de ese producto en todo el almacén (como escanearlo).
4. **Solo en Consultar.** En Acomodar, Recibir directo y Despacho escanear la posición ya es la acción (completar o agregar); en Conteo por
   posición ya hay lista. Si se quiere ver el contenido también en Acomodar (antes de dejar el producto), sería otro lote.
5. **Varios lotes**: una fila por producto con la **suma** y hasta 3 lotes nombrados (`Lote A, B, C y 2 más`). Constante
   `BIN_CONTENT_MAX_LOTS_SHOWN`. El lote solo se muestra; el buscador también filtra por lote.
6. **Orden por SKU** (como la hoja impresa ordena por producto), no el del servidor.
7. **Posición antes que búsqueda libre**: si el código es un producto, gana el producto (como antes); si no, se prueba como posición y solo
   después como texto libre (lote, nombre…). Un código que fuera a la vez posición y número de lote se trata como posición.
8. **Vacía no es error**: sale bajo el título de la posición, sin bloque rojo ni vibración de error (sí vibración de "encontrado").
9. **Mensaje cambiado**: `No hay nada con ese código.` → `No hay un producto ni una posición con ese código.` (más claro para el operario;
   ningún recorrido Maestro lo usaba).

## Pendientes

- Comprobación en el Zebra (lista de arriba) y APK del job `android`.
- Decisiones 1 y 2 (regla de cantidades en el resto de Consultar; opción de servidor).
- Advertencia que **ya existía** antes del lote (comprobada en el HEAD de partida): `__routeTests__/lookupScreen.test.tsx` imprime
  "overlapping act() calls" (llama `renderRouter` sin `await`); pasa igual. No se tocó.
- `package-lock.json` desincronizado para `npm ci` sin `--legacy-peer-deps` (anterior; en esta sesión no hizo falta reinstalar).
