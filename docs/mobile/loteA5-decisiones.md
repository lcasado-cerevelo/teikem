# Lote A5 (app de almacén) — al menos una cantidad al contar por producto; en Despacho, la cantidad primero — decisiones

Fecha: 2026-10-03. Rama `claude/company-settings-screen-plan-uajc2i`. Implementa los números **4** y **5** de
[decisiones-del-dueno-2026-10-03.md](../decisiones-del-dueno-2026-10-03.md). Número: `loteA3` y `loteA4` ya existen en `docs/mobile/`;
este es el **A5**, el siguiente libre (no confundir con la "pieza A5 Inicio + Sincronización" de `app-almacen-plan.md` §4, que fue
parte del Lote 8A-app). No toca el servidor (`src/`), la web (`web-app/`) ni `Diseño/`. Manual: [capítulo 9](../manual/09-app-almacen.md)
§6 y §7.1, y [FAQ](../manual/faq.md#lote-a5--app-de-almacén-al-menos-una-cantidad-al-contar-por-producto-en-despacho-la-cantidad-primero).

## Qué cambió

### 4. Contar por producto: Confirmar exige al menos un número

- `countLogic.ts`: el resumen (`summarizeProductCount`) cuenta también `filled` (filas con un número escrito; **0 cuenta**). Nuevas
  `isAllBlank`, `productCountConfirmBlock(summary)` → `invalid` / `empty` / `allBlank` / `null` y `hasAnyCountedQty` (misma regla
  sobre lo guardado en la base local).
- `ProductCountView.tsx`: Confirmar sigue encendido mientras no haya cantidades inválidas; al tocarlo con **todas** las filas en blanco
  no llama a `onConfirm` y muestra, encima de Confirmar, el bloque rojo grande (`ScanMessage`) **"Escribe al menos una cantidad. Si no
  hay nada de este producto, escribe 0 en una posición."** El aviso se quita al escribir una cantidad, al abrir o agregar "Otra
  posición". Una fila de "Otra posición" con cantidad cuenta como número escrito (todas las filas entran al resumen).
- `count.tsx#finishProduct`: guarda adicional — no encola si no hay ninguna fila o ninguna cantidad guardada (por si se llegara sin
  pasar por la vista).
- Sin cambios: blancos restantes = 0 al viajar, la línea "N posiciones en blanco se toman como 0." encima de Confirmar, el aviso del
  conteo vacío (sin filas) y "Cancelar conteo".
- i18n: `count.confirmAllBlank` (es/en).

### 5. Despacho: la cantidad antes de escanear la posición

- `dispatchLogic.ts`: se **eliminó** `DISPATCH_ADD_ON_BIN_SCAN` (nada más dependía de ella) y `canAddPickLine` (ya no hay botón que
  apagar). `newPickLineDraft` arranca con la cantidad **vacía** (antes `'1'`). Nuevas `pickQtyState(qtyText)` → `missing` / `invalid`
  / `ok` y `binScanOutcome(draft, binCode)` → `add` (con la línea armada) / `needQty` / `invalidQty` / `noBin`.
- `dispatch.tsx`: la lectura de la posición (o Aceptar del campo) con cantidad > 0 **agrega al instante** con el aviso verde de
  siempre. Sin cantidad: no agrega, bloque rojo **"Escribe la cantidad primero y luego escanea la posición."**, vibración de error y
  el cursor pasa a la cantidad. Con 0, negativa o texto: **"La cantidad debe ser un número mayor que 0. Corrígela y vuelve a escanear
  la posición."** La posición **no se guarda** en esos casos (se vuelve a escanear). Se quitó el botón **"Agregar"** (con la posición
  sin guardar quedaría siempre apagado); queda "Cancelar". Debajo de la cantidad: "Escribe la cantidad y luego escanea la posición: la
  línea se agrega sola."
- `ScanField.tsx`: prop opcional `testID` (para Maestro). Cantidad con `testID="dispatch-qty"`, posición `dispatch-from-bin`.
- i18n: `dispatch.qtyFirst`, `dispatch.qtyInvalid`, `dispatch.qtyFirstHelp` (es/en). `dispatch.addLine` queda sin uso en el diccionario.
- Maestro `e2e-maestro/03-despacho.yaml`: tras escanear el producto, escanea la posición sin cantidad y comprueba el aviso (y que no
  hay líneas), toca la cantidad (`id: dispatch-qty`), escribe `2`, toca la posición (`id: dispatch-from-bin`), la escribe + Enter y
  comprueba `Agregado: 2 E2E-SKU-DISP desde A01-R01-N1-P01`. Ningún otro recorrido pasa por Despacho (01 solo comprueba que el botón
  existe; 02, 04a y 04b son de Recibir).

## Cómo se probó (resultados reales de esta sesión)

| Comando (en `app-almacen/`) | Resultado |
|---|---|
| `npm run check` antes de los cambios (dependencias ya instaladas; no hizo falta `npm ci --legacy-peer-deps`) | Pasó: 53 suites, 273 pruebas. |
| `npm run check` al final (`api:types` + `tsc -b` + `oxlint` + `jest`) | **Pasó**: `api:types` sin diferencias en `schema.d.ts`, `tsc` sin errores, `oxlint` sin hallazgos, **56 suites, 285 pruebas**. |
| Mutación a mano: `isAllBlank` siempre `false` | 3 pruebas fallaron (lógica y las dos de pantalla); se restauró. |
| Mutación a mano: cantidad por omisión de vuelta en `'1'` | 4 pruebas fallaron (lógica y pantallas de Despacho); se restauró. |

Pruebas nuevas o ajustadas (Jest):
- `countLogic.test.ts`: todo en blanco → `allBlank` (1 y 3 filas); solo ceros → permitido; un número y el resto en blanco → permitido;
  sin filas → `empty`; inválido antes que todo en blanco; `hasAnyCountedQty`; el resumen con `filled`.
- Pantallas (`expo-router/testing-library`, base SQL real, `fetch` falso): `countProductAllBlankScreen` (3 filas: Confirmar con todo en
  blanco → aviso rojo, cola vacía, conteo abierto, resumen a la vista; un `0` en una → aviso fuera, Confirmar encola `[0,0,0]` y cierre),
  `countProductAllBlankOtherBinScreen` (conteo vacío → "Otra posición" en blanco → aviso de todo en blanco, no el de conteo vacío; con
  su cantidad → termina). Las de A4 (`countProductListScreen`, `countProductNoStockScreen`, etc.) pasan sin cambios de comportamiento.
- `dispatchLogic.test.ts`: cantidad vacía al empezar; `pickQtyState`; `binScanOutcome` con cantidad (agrega con la posición
  recortada), sin cantidad (`needQty`, nunca 1), 0/texto/negativa (`invalidQty`), lectura vacía.
- Pantallas: `dispatchQtyFirstScreen` (sin cantidad → aviso y nada agregado; `0` → aviso de inválida; `dos` → igual; `3` y volver a
  escanear → línea con 3 y aviso verde; la siguiente línea vuelve a empezar vacía y escanear de una vez no agrega), y
  `dispatchScanAddScreen` ajustado (cantidad vacía al abrir, sin botón "Agregar", con `1250` agrega "1,250"). En la prueba de pantalla
  el reloj falso de Jest avanza 1 s antes de cada lectura, porque `ScanField` toma una vez la misma lectura dentro de 400 ms.

**Lo que NO se pudo comprobar aquí**: no hay emulador ni SDK de Android: no se compiló el APK (lo hace el job `android` del CI), no se
probó en el Zebra y **no se corrió el recorrido Maestro** (job `android-e2e`). Riesgos a vigilar en ese recorrido: que `tapOn: id:`
encuentre los `testID` (`dispatch-qty`, `dispatch-from-bin`) y que, con el teclado en pantalla abierto tras escribir la cantidad, el
campo de la posición quede visible (hay un `scrollUntilVisible` antes de tocarlo).

## Lista de comprobación para el aparato (la ejecuta Luis con el APK del CI)

1. **Contar por producto, todo en blanco**: Conteo → Por producto → escanear un producto con varias posiciones → sin escribir nada,
   Confirmar → bloque rojo "Escribe al menos una cantidad. Si no hay nada de este producto, escribe 0 en una posición."; la app sigue en
   la lista y en la web el conteo **no** pasa a Contado.
2. Escribir **0** en una sola posición → el aviso desaparece y encima de Confirmar se lee "N posiciones en blanco se toman como 0." →
   Confirmar → vuelve a Inicio; en la web todas las líneas quedan en 0.
3. **Otra posición en blanco**: con un producto sin existencia, agregar una posición con "Otra posición", dejarla en blanco y Confirmar
   → el mismo aviso rojo; escribir la cantidad → Confirmar termina.
4. **Despacho, sin cantidad**: escanear un producto → la cantidad aparece **vacía** → escanear la posición (gatillo) → bloque rojo
   "Escribe la cantidad primero y luego escanea la posición.", **no** aparece ninguna línea y el cursor queda en la cantidad.
5. Escribir la cantidad (por ejemplo 2) → escanear la posición → la línea se agrega al instante con "Agregado: 2 … desde …".
6. **Cantidad 0**: escribir 0 → escanear la posición → aviso "La cantidad debe ser un número mayor que 0…" y no se agrega nada.
7. A mano: con la cantidad escrita, ⌨ → escribir el código de la posición → Aceptar → se agrega igual. No hay botón "Agregar".
8. Letras: los avisos rojos se leen completos en la pantalla de 4" con la fuente normal y grande.

## Decisiones para el dueño

1. **El botón "Agregar" de Despacho se quitó.** Con la posición sin guardar cuando falta la cantidad, el botón quedaría siempre
   apagado; la lectura (o Aceptar del campo) es la que agrega, como en Recibir directo y Acomodar. Si se quiere de vuelta, habría que
   volver a guardar la posición sin cantidad (y entonces "volver a escanear" no sería necesario).
2. **Sin cantidad, la posición escaneada no se guarda**: tras escribir la cantidad hay que volver a escanearla (el aviso lo dice). Así
   nunca queda una línea a medias ni una posición vieja pegada al siguiente producto.
3. **Cantidad 0 o inválida en Despacho** tiene su propio aviso ("La cantidad debe ser un número mayor que 0…"), distinto del de "sin
   cantidad"; se aceptan coma o punto como decimal, como antes.
4. **Al avisar en Despacho, el cursor pasa a la cantidad**: en el Zebra eso abre el teclado numérico en pantalla (el campo de cantidad
   es un campo normal, no de escaneo). El lector sigue funcionando con el cursor ahí.
5. **Contar por producto: Confirmar no se apaga con todo en blanco**; se deja tocar para explicar por qué no termina (igual que el
   conteo vacío del A4). La línea "N posiciones en blanco se toman como 0." sigue a la vista también con todo en blanco (se pidió no
   cambiarla); el aviso rojo aparece al tocar Confirmar.
6. **Recibir sigue con la cantidad en 1 por omisión** (`newLineDraft` de `receiveLogic.ts`): allí la línea se agrega con "Agregar"
   después de ver la cantidad, así que no tiene el problema de Despacho. No se tocó; si se quiere igual que Despacho, es otro cambio.
7. El documento A4 pedía marcar "la decisión 7"; la de "todo en blanco" es la **decisión 8** de `loteA4-decisiones.md` (la 7 es
   "lo escrito se guarda a cada cambio"). Se marcó la 8 como resuelta y el punto 6 de su lista de comprobación como reemplazado.

## Pendientes

- Comprobación en el Zebra (lista de arriba), compilación en el job `android` y recorrido Maestro `03-despacho.yaml` en `android-e2e`
  (sigue con `continue-on-error`).
- Sigue sin existir un recorrido Maestro de Conteo (ver A4).
- La clave `dispatch.addLine` quedó sin uso en los diccionarios; se puede borrar en una limpieza.
- **Historial**: por un commit simultáneo de otro agente en el mismo árbol, una versión intermedia de `dispatch.tsx`,
  `dispatchLogic.ts`, `ScanField.tsx` y los diccionarios de la app quedó dentro del commit `42088c3` ("Conteo cíclico: la línea
  corregida por el supervisor…"); el estado final está en el commit siguiente de Despacho. No se reescribió el historial.
- `package-lock.json` desincronizado para `npm ci` sin `--legacy-peer-deps` (anterior; en esta sesión no hizo falta reinstalar).
