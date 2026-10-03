# Lote A3 (app de almacén) — mejoras de uso del Zebra y formatos de la compañía — decisiones

Fecha: 2026-10-03. Rama `claude/company-settings-screen-plan-uajc2i` (parte 1 del cierre del lote "Región y formatos de la
compañía": la app). Número: `loteA1` y `loteA2` están reservados en `docs/plan-de-trabajo.md` para las dos primeras entregas de
la app (que se documentaron en `docs/lote8A-app-decisiones.md`), así que este es el **A3**, el primero libre en `docs/mobile/`.

Especificación: `docs/mobile/mejoras-ux-zebra.md` (aprobada por el dueño) y el backend/web de Región y formatos (lote 18 y F9).
No toca el servidor, la web ni la maqueta. Manual: [capítulo 9](../manual/09-app-almacen.md) y [FAQ](../manual/faq.md#lote-a3--app-de-almacén-mejoras-del-zebra-y-formatos-de-la-compañía).

## Qué se construyó

### A. Mejoras de uso (docs/mobile/mejoras-ux-zebra.md)

1. **Margen inferior** (`src/app/_layout.tsx`, `src/kernel/ui/insets.ts`): `paddingBottom = max(insets.bottom, 32) + spacing.sm`,
   una sola vez en la raíz. 32 dp es más de la mitad del botón de 56 dp. La barra del aparato **no** se oculta.
2. **Lector**:
   - `modules/datawedge/.../DatawedgeModule.kt`: el perfil `TeikemAlmacen` ahora **apaga el plugin KEYSTROKE**
     (`keystroke_output_enabled=false`), pide el resultado de la creación (`SEND_RESULT=true`, `COMMAND_IDENTIFIER`) y, si
     DataWedge contesta SUCCESS, pregunta el **perfil activo** (`GET_ACTIVE_PROFILE`). Todo queda en logcat con la etiqueta
     `TeikemDataWedge` y se expone a JS: `getProfileStatus()` y el evento `onProfileStatus` (`ready` / `noProfile` /
     `unconfirmed` / `unavailable`), más `refreshProfileStatus()`.
   - **Hallazgo adicional** (no estaba en la especificación): los receptores se registraban con `RECEIVER_NOT_EXPORTED`; en
     Android 13+ eso solo deja pasar broadcasts de la propia app o del sistema, y **DataWedge es otra app**: es otra causa
     probable de que la lectura no llegara por intent (y, con KEYSTROKE encendido, llegara como teclas). Ahora se registran
     `RECEIVER_EXPORTED` (ver decisión 1). Además la salida por intent declara la categoría `DEFAULT` y el filtro la acepta,
     y los broadcasts a DataWedge van con `setPackage("com.symbol.datawedge")`.
   - `ScanField`: `showSoftInputOnFocus={false}` (el teclado en pantalla no aparece al enfocar ni al escanear); botón
     pequeño **"⌨"** (texto accesible "Mostrar teclado" / "Esconder teclado") que lo muestra para escribir a mano; Enter y
     **Aceptar** siguen igual. Una misma lectura que llega dos veces en menos de 400 ms se toma una vez (por si el aparato
     manda teclas e intent a la vez con un perfil viejo).
   - **Sincronización**: indicador **"Lector: listo / sin perfil / sin confirmar / no es un Zebra (se usa el teclado)"**.
     Solo dice "listo" si DataWedge contestó que el perfil activo de la app es `TeikemAlmacen`; sin respuesta dice
     **"sin confirmar"**. Con "sin perfil" o "sin confirmar" muestra el detalle (p. ej. `activo: Profile0`) y el botón
     **"Volver a configurar el lector"** (vuelve a mandar el perfil).
   - **Escanear = Aceptar, pantalla por pantalla** (revisado en el código):

     | Pantalla / paso | Qué hace la lectura | ¿Queda un "Continuar" después? |
     |---|---|---|
     | Recibir: orden o aviso | abre el recibo y pasa a escanear productos | No |
     | Recibir: producto | pasa a la cantidad/lote/series | Paso de cantidad (manual, como pide la especificación) |
     | Recibir: series | agrega la serie a la lista | "Agregar" cierra la línea (las series son la cantidad) |
     | Recibir directo: posición destino | valida y **agrega la línea** | No |
     | Acomodar: posición destino | **completa la tarea** (y aviso verde en la lista) | No |
     | Despacho: producto | pasa a la cantidad y la posición | Paso de cantidad (manual) |
     | Despacho: posición de donde sale | **antes** solo la anotaba y había que tocar "Agregar"; **ahora agrega la línea** si la cantidad ya es válida (decisión 2; desde el lote A5, sin cantidad no agrega y avisa) | No |
     | Conteo: posición | abre el conteo | No |
     | Conteo: producto | pasa a la cantidad encontrada | Paso de cantidad (manual) |
     | Consultar | busca y muestra el saldo | No |
3. **Conteo**: tocar un producto de "Lo que se espera aquí" **pone su código (SKU) en el campo del producto** y deja el
   cursor al final, sin enviarlo; se confirma con Aceptar. `ScanField` recibe `prefill={{ value, seq }}` (el contador
   permite repetir el mismo valor). `LineList` recibe `onPressItem` y `pressLabel`. **No cambia quién ve las cantidades
   esperadas** (siguen llegando del servidor solo con `warehouse.count`; la lista no muestra cantidades).
4. **Inicio y letras**: acciones en **dos columnas** de botones de **88 dp** (icono arriba, texto debajo; `BigButton
   layout="tile"`), la quinta a todo el ancho, pantalla **desplazable**, Sincronizar y Cambiar de usuario debajo. Letras
   (`theme.fontSize`): títulos de lista 18, subtítulos 15, ayuda/error/cualquier mensaje 16 como mínimo (también en
   Registrar y Entrar), sincronización 16, etiqueta de botón 20. El **aviso de la lectura** sale en un bloque grande
   (`ScanMessage`, 20 pt en negrita, blanco sobre rojo `#B42318` o verde `#0E7A4F`, contraste ≥ 5.5:1) y **se queda hasta
   la siguiente lectura o acción** (sin temporizador). Avisos verdes nuevos: "Agregado: {qty} {sku} desde {bin}" (Despacho)
   y "Listo: {sku} quedó en {bin}" (Acomodar).
5. Recorridos Maestro (`e2e-maestro/02`, `03`, `04a`): sin `hideKeyboard` (ya no hay teclado que cerrar; en Android manda
   "Atrás") y Despacho sin tocar "Agregar".

### B. Formatos de la compañía en la app

- `src/kernel/format/`: `settings.ts` (tipo, Puerto Rico/EE. UU., normalización campo por campo igual que la web),
  `zone.ts` (partes de fecha/hora en una zona IANA: `Intl` solo para convertir de zona), `format.ts` (número, cantidad,
  dinero, fecha, día-mes, hora 12/24 con "a. m./p. m." o "AM/PM" según el idioma, fecha-hora, "cuándo" corto, **hoy en la
  zona de la compañía**, ejemplo y lectura de una fecha escrita a mano), `phone.ts` (solo dígitos para guardar, máscara para
  mostrar), `store.ts` (vigentes por compañía), `useFormat.ts` (hook), `tenantFormatApi.ts` (`GET /api/v1/tenant/settings`).
  Sin `Intl` para el texto final (el motor de RN no siempre trae los datos de idioma), como ya hacía la app.
- **Cuándo se piden**: al entrar con el PIN (sin esperar) y en cada pasada de sincronización con señal, después del heartbeat.
  Un cambio de región en la web llega en la siguiente pasada (≤ 60 s con Inicio abierto). Si la llamada falla se siguen usando
  los guardados.
- **Dónde se guardan**: en la **base local de la compañía** (kv `tenantFormat`, solo los campos de formato: nada de nombre legal
  ni identificación fiscal). Funcionan sin señal, sobreviven a cerrar la app y cada compañía del teléfono tiene los suyos.
  Mientras nunca han llegado: **Puerto Rico**.
- **Lo que se reemplazó**: `src/kernel/i18n/numberFormat.ts` (coma de miles fija) se eliminó; los números dentro de los textos
  (`{count}`, `{qty}`…) llevan los separadores de la compañía; Acomodar pinta las cantidades con ellos; Consultar pinta en
  mano/disponible con ellos y el aviso de datos guardados dice la **hora** en la zona de la compañía ("Datos de las 9:30 p. m.
  (hace 12 min, sin señal ahora)"); Sincronización pinta la última pasada con la hora de la compañía (con la fecha si no fue
  hoy) en vez de `toLocaleTimeString()`; Recibir pide el **vencimiento** en el orden de fecha de la compañía (ejemplo en el
  campo: `MM/DD/AAAA` en Puerto Rico) y lo manda en ISO. `grep -rn "toLocale\|Intl\.\|toFixed" app-almacen/src` ya solo
  encuentra el módulo de formatos.

## Cómo se probó (resultados reales de esta sesión)

| Comando (en `app-almacen/`) | Resultado |
|---|---|
| `npm ci` | **Falló** antes de tocar nada: `package-lock.json` no está en sincronía con los peers (`@react-native/metro-config@0.86.3` y otros faltan en el lock). No es de este lote; el CI usa `npm ci --legacy-peer-deps`. |
| `npm ci --legacy-peer-deps` (lo que corre el CI) | Pasó. |
| `npm run check` antes de los cambios | Pasó: 39 suites, 196 pruebas. |
| `npm run check` al final (`api:types` + `tsc -b` + `oxlint` + `jest`) | **Pasó**: `api:types` sin diferencias en `schema.d.ts`, `tsc` sin errores, `oxlint` sin hallazgos, **45 suites, 236 pruebas**. |

Pruebas nuevas o ampliadas (Jest): `kernel/format/format.test.ts` (PR, EE. UU., DMY/MDY/YMD, 12/24 h, separadores, dinero
antes/después, teléfono, fecha escrita a mano, horario de verano de Nueva York y **"hoy" a la 1:30 UTC del 3 de octubre = 2 de
octubre en Puerto Rico**), `kernel/format/store.test.ts` (persistencia en la base local contra SQL real, sin datos fiscales,
lectura tras "reabrir", una por compañía, sin señal no cambia, **cambio de región que llega en la siguiente sincronización**),
`kernel/ui/ScanField.test.tsx` (sin teclado al enfocar, botón ⌨, Enter, Aceptar, **la lectura del lector acepta sola**,
lectura duplicada, bloque de error/aviso, prefill), `kernel/ui/insets.test.ts` (margen), `kernel/ui/BigButton.test.tsx` (tile
de 88 dp), `kernel/ui/LineList.test.tsx` (fila tocable, letras), `kernel/scanner/useScanner.test.tsx` (indicador: nunca "listo"
sin confirmación de DataWedge), recorridos de pantalla con `expo-router/testing-library`: `homeLayout` (margen de 40 en la raíz,
grilla de dos columnas, desplazable), `countPrefillScreen` (tocar producto → campo lleno → Aceptar → cantidad; a ciegas sin
"Esperado"), `dispatchScanAddScreen` (la lectura de la posición agrega la línea con "1,250"), `syncScreen` (indicador del
lector y "Última vez: nunca"), `receiveLogic` (vencimiento en orden de la compañía e inválido).

**Lo que NO se pudo comprobar aquí**: el módulo nativo `DatawedgeModule.kt` no se compiló (no hay SDK de Android en el
entorno; el árbitro es el job `android` de `.github/workflows/ci.yml`, que genera el APK); el lector real, la barra de
navegación y las letras solo se comprueban en el Zebra; los recorridos Maestro (`android-e2e`) no se corrieron; no hay
capturas de 4" (sin emulador). La conversión de zona usa `Intl` de Hermes: en Jest (Node) funciona; en el aparato lo confirma
la lista de abajo (punto 9).

## Lista de comprobación para el aparato (la ejecuta Luis con el APK del CI)

Instalar el APK del job `android` encima de la versión actual (no hace falta desinstalar: se conservan registro y pendientes).

1. **Barra de navegación**: con la barra de 3 botones y luego con gestos, entrar a Recibir, Acomodar, Despacho, Conteo,
   Consultar y Sincronización: el último botón de cada pantalla se ve **completo** por encima de la barra.
2. **Indicador del lector**: Sincronización → debe decir **"Lector: listo"** (en verde). Si dice "sin perfil" o "sin confirmar",
   anotar el "Detalle", tocar "Volver a configurar el lector", salir a Inicio, volver y revisar de nuevo. Si sigue igual: en
   DataWedge (app de Zebra) → Perfiles, revisar que exista `TeikemAlmacen`, asociado a `com.teikem.almacen`, con Keystroke
   output apagado e Intent output encendido (acción `com.teikem.almacen.SCAN`, broadcast), y que ningún otro perfil esté
   asociado a la app.
3. **Lector sin teclado y avance solo**, en cada pantalla, apretando el gatillo **sin tocar la pantalla**:
   - Recibir: escanear una orden/aviso → abre el recibo; escanear un producto → pasa a la cantidad. En un almacén directo,
     tras "Siguiente", escanear la posición → la línea se agrega.
   - Acomodar: abrir una tarea, escanear la posición → aviso verde "Listo: …" y vuelve a la lista.
   - Despacho: escanear producto → **escribir la cantidad** → escanear la posición → la línea se agrega con aviso verde "Agregado: …".
     (Lote A5: la cantidad viene vacía; escanear la posición sin cantidad no agrega nada y avisa "Escribe la cantidad primero y luego
     escanea la posición." — ver la lista de `loteA5-decisiones.md`.)
   - Conteo: escanear la posición → abre el conteo; escanear un producto → pasa a la cantidad.
   - Consultar: escanear un producto o una posición → muestra el saldo.
   En ningún caso debe aparecer el teclado en pantalla ni el código escrito en el campo de cantidad.
4. **Botón ⌨**: en cualquier campo de escaneo, tocar "⌨" → aparece el teclado; escribir un código y tocar Aceptar (o Enter)
   → avanza igual; tocar "⌨" otra vez → se esconde. El teclado físico del MC3300 también escribe en el campo sin abrir el de
   pantalla.
5. **Tocar un producto en el conteo**: con un conteo abierto, tocar un producto de "Lo que se espera aquí" → su SKU queda en
   el campo; tocar Aceptar → pasa a la cantidad de ese producto.
6. **Pantalla principal**: las cinco acciones en dos columnas, sin cambiar la resolución del aparato; Sincronizar y Cambiar
   de usuario visibles (desplazando si hace falta).
7. **Letras**: con la fuente del sistema en tamaño normal y en grande, ningún texto cortado en Inicio, Recibir, Conteo y
   Sincronización; los avisos rojos/verdes al escanear se leen bien a la distancia de trabajo.
8. **Formatos**: en la web, Ajustes de la compañía → Región y formatos, cambiar a 24 h y día/mes/año; en la app, Sincronizar
   ahora → Sincronización muestra la hora en 24 h; en Recibir (producto con lote) el ejemplo del vencimiento dice
   `DD/MM/AAAA`. Volver los valores a como estaban.
9. **Zona horaria**: a cualquier hora, la hora de "Última vez" en Sincronización coincide con el reloj de Puerto Rico.
   Después de las 8:00 p. m. (00:00 UTC), "Última vez" muestra solo la hora (sigue siendo "hoy" en Puerto Rico).
10. **Sin señal**: con el aparato en modo avión, cerrar y abrir la app: los formatos siguen siendo los de la compañía.

## Decisiones para el dueño

1. **Receptores del lector "exportados"** (`RECEIVER_EXPORTED`). Es lo que permite recibir las lecturas de DataWedge (otra
   app) en Android 13+. Costo: otra app instalada en el aparato podría mandar una "lectura" falsa a la acción
   `com.teikem.almacen.SCAN`. En un Zebra administrado (solo apps de la compañía) el riesgo es bajo. Alternativa si se
   prefiere cerrarlo: volver a `NOT_EXPORTED` y comprobar en el Zebra si DataWedge igual entrega (algunas versiones corren
   como sistema). Está en un solo método (`register` en `DatawedgeModule.kt`).
2. **Resuelta (2026-10-03, decisión del dueño 5): se exige la cantidad primero.** Implementado en el lote A5
   (`loteA5-decisiones.md`): la cantidad viene vacía (ya no en "1"), escanear la posición con cantidad mayor que 0 agrega la
   línea al instante y sin cantidad (o con 0 o algo inválido) no agrega nada y avisa. Se quitaron la constante
   `DISPATCH_ADD_ON_BIN_SCAN` y el botón "Agregar". *Texto original:* la lectura de la posición agregaba la línea si la
   cantidad ya era válida; como la cantidad venía en "1", escanear la posición antes de escribirla metía la línea con 1.
3. **Vencimiento del lote en el orden de fecha de la compañía** (Puerto Rico: `MM/DD/AAAA`), aceptando también el ISO
   `AAAA-MM-DD` que se pedía antes. Una fecha que no existe deja "Agregar" apagado y muestra "La fecha no es válida.
   Escríbela así: MM/DD/AAAA" (el servidor rechazaría el recibo entero y en la cola ya no se corrige).
4. **Lecturas repetidas**: la misma lectura dentro de 400 ms se toma una sola vez (constante `DUPLICATE_WINDOW_MS` en
   `ScanField.tsx`). Escanear el mismo código dos veces a propósito toma más que eso.
5. **El botón ⌨ recuerda su estado** mientras se está en el mismo campo; al pasar a otro paso o pantalla vuelve a "sin
   teclado".
6. **Tocar un producto pone su SKU** (no el código de barras), que la búsqueda local acepta igual.
7. **Respaldo de la zona**: si el motor del aparato no pudiera convertir la zona de la compañía (sin `Intl` o zona
   desconocida), se usa la hora del aparato (en los Zebra de la compañía coincide). La comprobación 9 lo confirma.
8. **Cantidades escritas a mano**: se siguen aceptando punto o coma como decimal (como antes), sin importar los separadores de
   la compañía: el teclado numérico del aparato muestra uno u otro según su idioma, y en una cantidad no se escriben miles.
9. Lo **opcional** de la especificación **no se hizo**: modo inmersivo (ocultar la barra del aparato) y "tocar para llenar" en
   otras listas que no sean el conteo.

## Pendientes

- **Backend (sugerencia, no bloquea)**: la app usa `GET /api/v1/tenant/settings`, que también devuelve nombre legal,
  identificación fiscal y marca a cualquier usuario con sesión del aparato. La app solo guarda los campos de formato, pero un
  `GET /api/v1/tenant/format` (solo los campos de Región y formatos) sería más ajustado. No se parcheó el servidor.
- **Teléfonos**: hoy ninguna pantalla de la app muestra ni pide teléfonos; las funciones (`formatPhone`, `normalizePhone`)
  quedan listas en `kernel/format/phone.ts` para cuando haga falta.
- **"Hoy" en la zona de la compañía** (`todayIso`) queda listo; ninguna pantalla de la app filtra por día todavía.
- **"Contar por producto"** (`docs/conteo-por-producto-diseno.md`) aún no existe en la app: cuando se haga, su lista debe usar el
  mismo `onPressItem` + `prefill` del conteo por posición.
- **Comprobación en el Zebra** (lista de arriba), compilación del módulo nativo en el job `android` y recorridos Maestro del job
  `android-e2e` (que sigue con `continue-on-error`). Riesgo a vigilar en Maestro: que `inputText` escriba en un campo con
  `showSoftInputOnFocus={false}`.
- **Capturas** de 4" y de pantalla grande: no se pudieron tomar (sin emulador).
- `package-lock.json` desincronizado para `npm ci` sin `--legacy-peer-deps` (anterior a este lote).
