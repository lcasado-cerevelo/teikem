# Lote A9 (app de almacén) — pantalla chica del Zebra: PIN desplazable, teclado que no tapa el campo y calculadora legible — decisiones

Fecha: 2026-10-06. Rama `claude/company-settings-screen-plan-uajc2i`. Origen: el dueño probó el APK en el Zebra (pantalla chica) y reportó cuatro
problemas. Número: `loteA7` es la adenda del A6 y `loteA8` es Consultar (lo que hay en una posición); este es el **A9**, el siguiente libre. No
toca el servidor (`src/`), la web (`web-app/`), `Diseño/` ni el API (`schema.d.ts` sin cambios). Sin dependencias nuevas (el APK se compila en
CI). Manual: [capítulo 9 §2.2 y §7.3](../manual/09-app-almacen.md#73-calculadora-de-cantidad-2026-10-05) y
[FAQ, sección Lote A9](../manual/faq.md#lote-a9--app-de-almacén-pantalla-chica-pin-teclado-en-pantalla-y-calculadora).

## Qué se arregló

### 1. Pantalla de poner el PIN: Volver y Entrar cortados a la mitad y sin desplazamiento

- **Qué pantalla es.** En la app no existe una pantalla para *crear* o *cambiar* el PIN: el PIN se define en la web (Mi cuenta) y la app solo lo
  pide para entrar (`app/login.tsx`, paso "elegir usuario → PIN"). Se tomó esa pantalla como la reportada ("poner el PIN"). No hay pantallas
  hermanas de "crear PIN" ni de "cambiar PIN" que revisar; las hermanas del flujo son Registrar el aparato y Elegir compañía/usuario (abajo).
- **Causa (no era el margen inferior).** La pantalla del PIN **sí** está dentro de `app/_layout.tsx`, que ya aplica `max(insets.bottom, 32) + 8`
  abajo. El defecto era de la propia pantalla: un `View` fijo con `flex: 1` y `justifyContent: 'center'` con nombre, "PIN", puntos, el teclado
  de 4 × 72 dp (324 dp con sus separaciones), el error y la fila Volver/Entrar. En un Zebra de 4" (~533 dp de alto, menos la barra de estado y
  el margen inferior: ~470 dp útiles) el contenido mide ~516 dp: al centrarlo, se salía por arriba y por abajo, y un `View` no se desplaza.
- **Arreglo.** La pantalla es un `ScrollView` (`testID="pin-scroll"`, `keyboardShouldPersistTaps="handled"`) con `contentContainerStyle`
  `flexGrow: 1` + `justifyContent: 'center'` (centrado cuando cabe, desplazable cuando no) y `paddingBottom` para que el último botón quede
  completo. Además, con ventanas de menos de **640 dp** de alto (`PIN_COMPACT_HEIGHT` en `kernel/ui/NumericKeypad.tsx`) el teclado del PIN
  usa teclas de **60 dp** con 8 dp entre ellas (`NumericKeypad` acepta `keySize`; nunca baja de los 56 dp de `touchTarget`) y la pantalla usa
  menos aire: así en el Zebra de 4" todo cabe (~464 dp) sin desplazar; el desplazamiento queda como red de seguridad (letra grande del sistema,
  mensaje de error). Volver y Entrar pasaron a ser del mismo ancho (mitad y mitad).

### 2. Botón de la calculadora: icono oscuro sobre fondo oscuro

- **Causa.** El botón usaba el emoji `🧮` (un ábaco de colores marrón y negro, dibujado por la fuente de emojis del aparato, que no toma el
  color del texto) sobre el fondo oscuro de la app (`colors.bg` `#0B1220`) con solo un borde azul.
- **Arreglo.** Icono nuevo del kit `kernel/ui/CalculatorIcon.tsx`: una calculadora **dibujada con trazos** (marco, pantalla y 3 × 3 teclas) en un
  solo color, por defecto `colors.onStrong` (blanco). El botón tiene fondo `colors.brandDark` (`#154FA6`) y borde `colors.brand`: blanco sobre
  `#154FA6` da un contraste de ~8:1, y el azul se distingue del campo y del fondo de la app. Área táctil: **56 × 56 dp** junto al campo de la
  cantidad (`QuantityField`) y **48 × 48 dp** en cada fila del conteo por producto (`ProductCountView`, antes 44). El tema no tiene un
  `onPrimary`: se usó `onStrong` (blanco, ya definido para texto sobre fondos fuertes).

### 3. Cabecera de la calculadora: "Calcula-dora" partido y enlace de texto "Cantidad directa"

- **Causa.** La fila de la cabecera tenía el título (`flex: 1`), el botón ⌨ (44 dp) y el **enlace de texto** "Cantidad directa" (~140 dp a 16 de
  letra): en 360 dp de ancho (menos márgenes del panel, ~300 dp útiles) al título le quedaban ~100 dp y se partía en dos líneas.
- **Arreglo.** Cabecera nueva: **[←] Calculadora [⌨]**.
  - El enlace de texto se reemplazó por un **botón con icono** (flecha `←` blanca sobre `colors.brandDark`, 56 × 56 dp, `accessibilityRole="button"`,
    `accessibilityLabel` = `calc.direct`: "Cantidad directa" / "Direct quantity", claves que ya existían en `kernel/i18n/{es,en}.json`). Hace lo
    mismo que el enlace: vuelve a la cantidad directa con el total ya puesto.
  - El título va en un contenedor `flex: 1, flexShrink: 1, minWidth: 0` y el `Text` con `numberOfLines={1}`, `adjustsFontSizeToFit` y
    `minimumFontScale={0.7}`: **nunca se parte**; si no cupiera (letra del sistema muy grande) se achica antes que cortarse. Ahora le quedan
    ~190 dp, así que sale a 20 (`fontSize.title`, antes 16).
- **La calculadora de una fila** (conteo de un producto en todas sus posiciones) era una **ventana** (`Modal`). Se cambió para que ocupe la pantalla
  **en el mismo lugar** (como "Otra posición"), dentro del `KeyboardScreen` del conteo, con el título "Cantidad en {posición}", la calculadora y
  **Cancelar** / **Usar {total}** (mismo orden que antes). Motivo: dentro de un `Modal` de Android el teclado en pantalla tapaba "Sueltas" y la
  ventana no se podía desplazar (punto 4). La flecha ← en esa calculadora cierra sin cambiar la fila (como antes "Cantidad directa").

### 4. El teclado en pantalla tapa el campo que se escribe ("Sueltas")

- **Causa.** Desde Expo SDK 54 la app de Android es **de borde a borde (edge-to-edge) obligatorio** (comprobado en
  `@expo/prebuild-config/.../withEdgeToEdge.js`: "Android 16 makes edge-to-edge mandatory"). `app.config.ts` no fija
  `android.softwareKeyboardLayoutMode`, así que el manifiesto que genera Expo trae el valor por defecto `adjustResize` (comprobado en
  `@expo/config-plugins/build/android/WindowSoftInputMode.js`). Con edge-to-edge, `adjustResize` **ya no encoge la ventana** cuando sale el
  teclado; las pantallas eran un `ScrollView` suelto (y Registrar el aparato un `KeyboardAvoidingView` con `behavior` **sin valor en Android**),
  así que nada movía el contenido y el campo quedaba debajo del teclado. En la calculadora de una fila, además, el `Modal` no se desplazaba.
- **Arreglo compartido** (no parches por pantalla), en `kernel/ui`:
  - `KeyboardScreen` (`KeyboardScreen.tsx`): reemplaza a `<ScrollView contentContainerStyle={…} keyboardShouldPersistTaps="handled">` con las
    mismas propiedades. Es un `KeyboardAvoidingView` con **`behavior="padding"` en las dos plataformas** + el `ScrollView`. Con la ventana sin
    encoger (edge-to-edge) agrega abajo el alto que tapa el teclado; si un aparato sí encoge la ventana, el cálculo de RN da 0 (no se duplica).
  - `useKeyboardScroll` (`keyboardScroll.ts`): al salir el teclado (`keyboardDidShow`), cuando `KeyboardAvoidingView` encoge el `ScrollView`
    (`onLayout`) y cuando se enfoca otro campo con el teclado ya fuera (de "Filas" a "Sueltas" no llega un evento nuevo del teclado), mide el
    campo enfocado dentro del contenido (`measureLayout` contra el contenido del `ScrollView`, mismas coordenadas, sin depender de la barra de
    estado) y desplaza lo justo para dejarlo completo con 24 dp de aire (`scrollDeltaToShow`, lógica pura; si el campo es más alto que lo visible,
    gana su principio). Sin teclado en pantalla no hace nada.
  - Los campos del kit avisan solos al enfocarse (`KeyboardScrollContext` / `useFieldFocus`): `KeyboardInput`, `ScanField` y el nuevo
    `KeyboardScreenInput` (un `TextInput` normal, con el teclado del sistema al enfocar, para Registrar el aparato). Fuera de un `KeyboardScreen`
    no cambian nada. El `onFocus` propio de cada campo se sigue llamando.
- **Se respeta el Zebra**: `ScanField` y `KeyboardInput` siguen con `showSoftInputOnFocus={false}` (el teclado en pantalla solo sale con ⌨); con el
  lector o el teclado físico no hay evento de teclado y la pantalla no se mueve.
- **`softwareKeyboardLayoutMode` no se cambió** (sigue `adjustResize` por defecto). `pan` movería la ventana entera (con el título) y pelearía con
  `KeyboardAvoidingView`; con edge-to-edge, `resize` + `KeyboardAvoidingView` es la combinación que recomienda la guía de Expo
  (docs.expo.dev/guides/keyboard-handling). No se editó nada de `android/` (no existe: lo genera Expo en CI).
- `react-native-keyboard-controller` **no** está en las dependencias: no se usó (sería una dependencia nativa nueva).

## Pantallas revisadas (todas las de `app-almacen/src/app` y `src/features` con `TextInput`, `KeyboardInput`, `ScanField` o `Modal`)

| Pantalla / paso | Campos | ¿Podía quedar tapado? | Qué se hizo |
|---|---|---|---|
| Registrar el aparato (`enroll.tsx`) | Servidor y código (`TextInput` con el teclado del sistema al enfocar) | **Sí**: el código y "Registrar" van abajo; `KeyboardAvoidingView` sin `behavior` en Android | `KeyboardScreen` + `KeyboardScreenInput` (mismos `testID` para Maestro) |
| Elegir compañía / elegir usuario (`login.tsx`) | Ninguno (listas desplazables, botones fijos abajo) | No | Sin cambio (la lista ya cede espacio a los botones) |
| Poner el PIN (`login.tsx`) | Teclado propio (`NumericKeypad`), sin teclado del sistema | No hay teclado; sí se cortaba (punto 1) | `ScrollView` + teclado compacto en pantallas bajas |
| Inicio, Sincronización (`home.tsx`, `sync.tsx`) | Ninguno | No | Sin cambio (ya desplazables) |
| Recibir, paso 1 (`receive.tsx`) | `ScanField` del documento | Con ⌨: sí (Volver abajo) | `KeyboardScreen` |
| Recibir, posición destino (directo) | `ScanField` | Con ⌨ | `KeyboardScreen` |
| Recibir, cantidad / lote / vencimiento | Hasta 3 `KeyboardInput` (+ `ScanField` de series) | **Sí**: el vencimiento queda cerca del fondo | `KeyboardScreen` |
| Recibir, recibo abierto | `ScanField` | Con ⌨ | `KeyboardScreen` |
| Acomodar, lista de tareas (`putaway.tsx`) | Ninguno (`FlatList`) | No | Sin cambio |
| Acomodar, tarea elegida | `ScanField` destino | Con ⌨ | `KeyboardScreen` |
| Despacho, captura (`dispatch.tsx`) | Cantidad (`KeyboardInput`) + `ScanField` de la posición | **Sí** (la posición va debajo de la sugerencia) | `KeyboardScreen` |
| Despacho, cliente | Ninguno | No | `KeyboardScreen` (misma vista base; inofensivo) |
| Despacho, consignatario | Piezas (`KeyboardInput`) | Con ⌨ | `KeyboardScreen` |
| Despacho abierto | `ScanField` | Con ⌨ | `KeyboardScreen` |
| Conteo, inicio (`count.tsx`) | `ScanField` posición/producto | Con ⌨ | `KeyboardScreen` |
| Conteo por posición: cantidad encontrada y corrección | `QuantityField` (+ calculadora: Filas, Columnas, **Sueltas**) | **Sí** (el caso reportado) | `KeyboardScreen` |
| Conteo por posición abierto | `ScanField` | Con ⌨ | `KeyboardScreen` |
| Conteo de un producto en todas sus posiciones (`ProductCountView`) | Buscador + una cantidad por fila + calculadora por fila (**`Modal`**) | **Sí** (filas de abajo; el `Modal` no se desplazaba) | Dentro del `KeyboardScreen` de `count.tsx`; la calculadora pasó del `Modal` a la misma pantalla |
| "Otra posición" (`OtherBinForm`, desde el conteo por producto y el abierto) | Hasta 7 `KeyboardInput` (código, pasillo, rack, nivel, posición, lote, vencimiento) | **Sí**, el más expuesto | Dentro de `KeyboardScreen` (los dos lugares) |
| Conteo abierto con varios productos (`OpenCountView`): producto, otra posición, lista | Lote, `QuantityField`, `ScanField` "otra posición", `ScanField` producto | **Sí** | `KeyboardScreen` en sus 3 vistas |
| Consultar (`lookup.tsx` + `BinContentsList`) | `ScanField` + buscador (más de 6 productos, puede quedar abajo) | Con ⌨: sí | `KeyboardScreen` |
| Avisos (`Alert.alert`) | Diálogos nativos sin campos | No | — |

Único `Modal` de la app: el de la calculadora por fila (quitado). No hay hojas inferiores.

## Archivos

Nuevos: `src/kernel/ui/{KeyboardScreen.tsx, keyboardScroll.ts, CalculatorIcon.tsx}`; pruebas `src/kernel/ui/keyboardScroll.test.tsx`,
`src/__routeTests__/{loginPinScreen,countRowCalculatorScreen}.test.tsx`. Cambiados: `src/app/{login,enroll,count,receive,putaway,dispatch,lookup}.tsx`,
`src/features/count/{QuantityField,ProductCountView,OpenCountView}.tsx`, `src/kernel/ui/{KeyboardInput,ScanField,NumericKeypad}.tsx`, pruebas
`QuantityField.test.tsx` y `NumericKeypad.test.tsx`. Docs: este archivo, `docs/manual/09-app-almacen.md` (§2.2 y §7.3), `docs/manual/faq.md`
(sección Lote A9) y `docs/manual/README.md`. i18n: sin claves nuevas (se reutiliza `calc.direct` como nombre accesible del botón ←).

Contrato del kit (para quien construya una pantalla nueva de la app):
- `KeyboardScreen` (props de `ScrollView`; `keyboardShouldPersistTaps` ya en `"handled"`): toda pantalla con campos de captura.
  `<KeyboardScreen contentContainerStyle={styles.fill}>…campos…</KeyboardScreen>`.
- `KeyboardScreenInput` (props de `TextInput`): solo si el campo debe mostrar el teclado del sistema al enfocar (registro); los del almacén usan
  `KeyboardInput` / `ScanField`.
- `useKeyboardScroll({ getFocused?, settleMs? })` → `{ scrollRef, onScroll, onLayout, onFieldFocus, ensureVisible }` y `useFieldFocus(getField, onFocus?)`:
  para un contenedor desplazable propio o un campo propio fuera del kit.
- `CalculatorIcon` (`color?`, `size?`): icono decorativo; el nombre lo pone el botón (`accessibilityLabel`).
- `NumericKeypad` acepta `keySize` (mínimo 56); `PIN_COMPACT_HEIGHT = 640`.

## Cómo se probó (resultados reales de esta sesión)

| Comando (en `app-almacen/`) | Resultado |
|---|---|
| `npm run check` en el HEAD de partida (`d3977a2`, árbol limpio) | Pasó: **74 suites, 422 pruebas**. |
| `npm run check` al final (`api:types` + `tsc -b` + `oxlint` + `jest`) | **Pasó**: `schema.d.ts` sin diferencias, `tsc` sin errores, `oxlint` sin hallazgos, **77 suites, 440 pruebas**. Las salidas de consola de jest son las mismas 8 de antes (advertencias ya existentes). |
| Mutación: quitar `numberOfLines={1}` del título | Falla 1 prueba (`QuantityField.test.tsx`, cabecera); se restauró. |
| Mutación: quitar `behavior="padding"` de `KeyboardScreen` | Falla 1 prueba (`keyboardScroll.test.tsx`); se restauró. |
| Mutación: quitar la condición "teclado fuera" en `onFieldFocus` | **Ninguna** falla: `ensureVisible` vuelve a comprobarlo (doble guarda); se restauró. |

Pruebas nuevas o cambiadas (Jest):
- `kernel/ui/keyboardScroll.test.tsx` (12): `scrollDeltaToShow` (ya visible, tapado abajo, arriba de lo visible, campo más alto que lo visible, sin
  medir); `KeyboardScreen` es un `ScrollView` con `keyboardShouldPersistTaps="handled"` y su `KeyboardAvoidingView` agrega el alto del teclado
  (`paddingBottom: 300`); al salir el teclado desplaza hasta el campo tapado (con `Keyboard.addListener` simulado y relojes falsos); cuenta lo ya
  desplazado y no se mueve si el campo se ve; vuelve a medir cuando la pantalla se encoge; con el teclado fuera, enfocar "Sueltas" desplaza y sin
  teclado o tras esconderlo no; `ScanField` y `KeyboardScreenInput` avisan y conservan su `onFocus`; fuera de un `KeyboardScreen` no falla.
- `features/count/QuantityField.test.tsx` (+2, y las de antes buscan el botón de volver por rol y nombre): icono dibujado blanco sobre
  `brandDark`, sin emoji, ≥ 48 dp; título `numberOfLines={1}` + `adjustsFontSizeToFit` en un contenedor que cede; no hay texto "Cantidad directa"
  pero sí un botón con ese nombre, ≥ 48 dp, flecha blanca; volver deja la cantidad.
- `kernel/ui/NumericKeypad.test.tsx` (+1): teclas de 72 por defecto, 60 si se piden, nunca menos de 56.
- `__routeTests__/loginPinScreen.test.tsx` (2, pantalla real con `expo-router/testing-library`): con 320 × 533 dp la pantalla del PIN es un
  `ScrollView` con `flexGrow: 1` (no `flex: 1`), `paddingBottom`, Volver y Entrar dentro y teclas de 60; con 412 × 915 teclas de 72.
- `__routeTests__/countRowCalculatorScreen.test.tsx` (1): la calculadora de una fila se abre en el mismo lugar dentro del `KeyboardScreen` (la
  lista ya no está debajo), 5 × 3 + 10 → **Usar 25** llena la fila y la base local; la flecha ← cierra sin tocar la fila.

## Lo que NO se pudo probar aquí

No hay emulador, SDK de Android ni Zebra: no se compiló el APK (job `android` del CI) ni se vio nada en pantalla. En jest el entorno es iOS
(`KeyboardAvoidingView` escucha `keyboardWillShow` allí y `keyboardDidShow` en Android) y las medidas (`measureLayout`, `onLayout`) son simuladas:
**que el teclado real ya no tape el campo en el Zebra no está comprobado**, solo la lógica. Tampoco se vio el icono dibujado ni el tamaño real del
título. Maestro: el recorrido `01-registrar-y-entrar.yaml` toca "Entrar"; en el emulador del CI la pantalla es alta y debería verse sin desplazar
(no se corrió).

### Lista de comprobación en el Zebra (APK del CI)

Preparación: letra del sistema normal; un producto con existencia en 2 posiciones; un usuario con PIN.

1. **PIN.** Salir → elegir el usuario → la pantalla del PIN muestra el nombre, los puntos, las 12 teclas y **Volver** y **Entrar completos** sin
   desplazar. Teclear un PIN malo: sale `PIN incorrecto.` y se puede deslizar para ver los botones. Repetir con la letra del sistema en grande:
   se puede deslizar hasta Entrar.
2. **Registrar el aparato** (Registrar otra compañía): tocar el campo del código → sale el teclado del sistema y el campo queda **encima** del
   teclado; tocar luego la dirección del servidor y volver al código: los dos quedan a la vista; "Registrar" se alcanza deslizando.
3. **Botón de la calculadora**: en Conteo por posición, al escanear un producto, junto a la cantidad se ve un **cuadro azul con una calculadora
   blanca**, fácil de distinguir con el tema oscuro y con luz de bodega; se toca bien con guantes.
4. **Cabecera**: al tocarlo sale **[←] Calculadora [⌨]** con "Calculadora" en **una sola línea**; ya no hay enlace "Cantidad directa".
5. **Volver**: escribir 5 × 3 + 10 → tocar **←** → la cantidad queda en 25.
6. **Sueltas**: en la calculadora, tocar ⌨ (teclado en pantalla) y luego tocar **Sueltas** → el campo sube y queda **encima del teclado**; pasar
   de Filas a Sueltas con el teclado abierto: Sueltas sube. Con "+ Otro bloque" dos o tres veces, Sueltas sigue quedando visible.
7. **Calculadora de una fila** (conteo de un producto en todas sus posiciones, si hay alguno abierto de antes): tocar la calculadora de la fila
   de abajo → se abre en la misma pantalla; con ⌨, Sueltas queda a la vista; **Usar 25** vuelve a la lista con 25 en esa fila.
8. **Otra posición** (conteo abierto por producto → Cambiar posición → Otra posición): con ⌨, tocar **Posición** y **Vencimiento** (lote): cada
   uno queda encima del teclado.
9. **Recibir** (producto con lote): con ⌨, tocar **Vencimiento** → visible. **Despacho**: con ⌨ en la cantidad y en la posición → visibles.
   **Consultar** una posición con más de 6 productos: con ⌨ en "Buscar producto o lote" → visible.
10. **Lector**: en todas esas pantallas, escanear con el gatillo **no** saca el teclado y la pantalla **no** se mueve.
11. Con el teclado abierto, tocar un botón (Agregar, Usar 25): responde al primer toque (no solo cierra el teclado).
12. Si en algún paso el teclado tapa el campo: anotar modelo y versión de Android, y si deslizando con el teclado abierto se alcanza el campo.

## Decisiones para el dueño (tomadas con la opción más conservadora)

1. **"Crear el PIN" = la pantalla de poner el PIN para entrar.** La app no crea ni cambia PIN (se hace en la web, Mi cuenta). Si el reporte era de
   otra pantalla, decirlo.
2. **Teclado del PIN compacto** por debajo de 640 dp de alto (teclas 60 dp, separación 8). Si se prefieren siempre las teclas de 72 (y desplazar),
   es quitar `keySize` en `login.tsx`.
3. **Flecha ← para volver** (no "✕"): la acción es volver a la cantidad directa **conservando** el total; una ✕ sugiere descartar. En la
   calculadora de una fila la ← cancela (como antes el enlace), y para aplicar está **Usar {total}**.
4. **La calculadora de una fila dejó de ser ventana** (ocupa la pantalla en el mismo lugar). Es lo que permite desplazar hasta "Sueltas" con el
   teclado; el título "Cantidad en {posición}" dice de qué fila es. Si se quiere volver a la ventana, habría que darle su propio desplazamiento y
   probar el teclado dentro de un `Modal` en el Zebra.
5. **`KeyboardAvoidingView` con `padding` también en Android** y **sin tocar `softwareKeyboardLayoutMode`** (queda `adjustResize`). Si en el Zebra
   el contenido saltara el doble (aparato que sí encoge la ventana), la salida sería `behavior={undefined}` en Android, a decidir con la prueba
   del punto 2/6.
6. **Icono dibujado con trazos** en vez de una librería de iconos: `@expo/vector-icons` no está en las dependencias y no se agregan dependencias
   en este lote; el dibujo no depende de la fuente de emojis del aparato.
7. **Aire de 24 dp** entre el campo y el teclado y **120 ms** de espera antes de medir (`KEYBOARD_FIELD_MARGIN`, `settleMs`): ajustables si en el
   Zebra el movimiento se ve tarde o corto.
8. **Despacho "elegir cliente"** quedó dentro de `KeyboardScreen` aunque no tiene campos (comparte la vista con la captura); no cambia nada visible.
9. Elegir compañía / usuario no se tocaron: no tienen campos y sus listas ya ceden espacio a los botones de abajo.

## Pendientes

- Lista de comprobación en el Zebra (arriba) y APK del job `android`.
- `docs/lote25-decisiones.md` (adenda 4) todavía dice "ventana por fila" para la calculadora del conteo por producto; queda como registro de esa
  fecha: el estado actual está en este documento y en el manual §7.3.
