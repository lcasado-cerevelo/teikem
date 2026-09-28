# Lote 8A-app — decisiones (primera entrega: núcleo + Recibir; segunda entrega: Acomodar, Despacho, Conteo, Consultar, Sincronización)

**Hecho por mí solo, sin workflow ni agentes** (decisión de Luis, 2026-09-28), con Sonnet como modelo de escritura. Sin
recorridos en el aparato real: eso lo prueba Luis con el APK que genera el job `android` de CI.

## Qué se construyó

- **Proyecto**: `app-almacen/` (Expo SDK 57, Expo Router, TypeScript). `expo-sqlite` para la base local, `expo-secure-store`
  para el aparato y la sesión, `openapi-fetch` sobre `web-app/openapi.json` regenerado en `src/kernel/api/schema.d.ts`.
- **Núcleo (A0)**: base local con migraciones (`kernel/db`), i18n es/en persistido en la base (`kernel/i18n`), sesión de
  aparato/usuario en SecureStore con reglas de PIN espejo de `PinRules.cs` (`kernel/auth`), cliente del API con URL de
  servidor configurable en tiempo de ejecución y reintento de 401 una sola vez (`kernel/api/client.ts`), motor de
  sincronización: bajada por diferencia de productos/órdenes de compra/avisos con sus líneas, y cola de salida con
  clave de idempotencia, orden FIFO, rechazo de negocio vs. error transitorio, una sola pasada en vuelo
  (`kernel/sync`). Kit de captura (`kernel/ui`: BigButton, NumericKeypad, ScanField, LineList) y puente a DataWedge en
  modo intent (`modules/datawedge`, módulo local de Expo).
- **Pantallas**: registrar aparato, elegir usuario y PIN, Inicio (5 botones + estado de sincronización), Recibir
  completo (documento o recibo ciego, producto con lote/series, confirmar en una sola llamada atómica).
- **CI**: job `mobile` (tipos generados, `tsc`, `oxlint`, Jest) y job `android` (`expo prebuild` + Gradle
  `assembleDebug`, sube el APK como artefacto).

## Pruebas (78, sin agentes revisores)

Contra SQL real (better-sqlite3 detrás de un mock de `expo-sqlite`, no un doble a mano): esquema y migraciones, kv,
i18n, sesión y auth del aparato, cliente del API (reintento de 401, error de red), motor de sincronización completo
(bajada por diferencia con marca de agua, cola de salida con las 4 rutas: éxito, rechazo de negocio, error
transitorio, sin red), lógica de captura de Recibir (cantidad/lote/series) y sus consultas locales, componentes del
kit (con `@testing-library/react-native` v14, `render`/`fireEvent` async), y una prueba de navegación real
(`expo-router/testing-library`, `renderRouter`) que confirma que Inicio monta con el aparato y la sesión hidratados.

**Lo que no verifiqué yo mismo**: el módulo nativo de DataWedge (`modules/datawedge/android/.../DatawedgeModule.kt`)
no se compiló en esta sesión (sin SDK de Android en el entorno); lo compila el job `android` de Gradle en CI. Tampoco
hay recorrido en un Zebra real: sin DataWedge, la app funciona igual por teclado (`ScanField`), pero el escaneo
automático solo lo prueba Luis con el APK.

## Decisiones tomadas por mí (a revisar)

1. **Escáner por teclado (keystroke) como red de seguridad, DataWedge en modo intent como plan principal.** El módulo
   nativo crea el perfil y escucha el broadcast; si DataWedge no está instalado o falla, `ScanField` sigue funcionando
   por teclado sin ningún cambio de código. Riesgo aceptado: si el modo intent fallara en el MC3300 real, la app sigue
   operable (más lenta) hasta el siguiente ajuste.
2. **Alcance de la sincronización en esta entrega**: solo productos, órdenes de compra y avisos (lo que usa Recibir).
   Almacenes, tareas y categorías (para Acomodar/Conteo) se agregan en la segunda entrega; las tablas locales ya
   existen en el esquema.
3. **Cámara como alternativa al escáner**: no se implementó todavía (`expo-camera` está instalado pero sin pantalla).
   El teclado ya cubre "sin lector" para pruebas; la cámara se agrega si Luis la pide tras probar el APK.
4. **Reconexión inmediata (NetInfo)** para disparar la sincronización al recuperar señal: no está; el respaldo cada
   60 s cubre el caso con un minuto de margen. Se agrega con la pantalla de Sincronización (segunda entrega).
5. **Firma: llave propia del repositorio, no EAS (decisión de Luis, 2026-09-28).** Generé un keystore
   (`teikem-almacen`, RSA 2048, válido hasta 2054) y un plugin (`plugins/withAndroidReleaseSigning.ts`) que agrega un
   `signingConfigs.release` de verdad al `android/app/build.gradle` que genera cada `expo prebuild`, leyendo la llave y
   las contraseñas de variables de entorno (nunca del repositorio). El job `android` de CI ahora compila
   `assembleRelease` en vez de `assembleDebug`: si los secretos `TEIKEM_RELEASE_KEYSTORE_B64`,
   `TEIKEM_RELEASE_KEYSTORE_PASSWORD`, `TEIKEM_RELEASE_KEY_ALIAS` y `TEIKEM_RELEASE_KEY_PASSWORD` ya están en el
   repositorio, firma con la llave propia; si no, cae a la llave de depuración de Gradle sin fallar (así el CI no se
   rompe mientras Luis agrega los secretos). El keystore y las contraseñas se los mandé aparte (nunca al repositorio ni
   al historial de commits); **si se pierden, esa llave no se puede recuperar y una app ya publicada con ella no se
   puede actualizar nunca más** — guardar una copia del archivo fuera de aquí es indispensable. `expo-updates` (para
   actualizar el JavaScript sin reinstalar el APK) sigue pendiente de una decisión aparte.
6. **Un recibo local a la vez por aparato, con bloqueo real (decisión de Luis, 2026-09-28).** `startLocalReceipt` ahora
   lanza un error si ya hay uno abierto (antes lo descartaba en silencio); la única forma de liberar el aparato es
   confirmar el recibo o tocar el nuevo botón "Cancelar recibo" (con una confirmación, porque se pierde lo capturado).
   Mientras haya un recibo en curso, Inicio bloquea Acomodar, Despacho, Conteo y Consultar con un aviso en vez de
   dejarlos entrar; "Recibir" nunca empieza uno nuevo por encima, siempre retoma el que está abierto. Cerrar y volver a
   abrir la app no lo pierde: sigue guardado tal cual. La misma regla ("una operación a la vez, bloqueado hasta
   confirmar o cancelar") queda anotada para Despacho cuando se construya en la segunda entrega.
7. **`npm run check` de `app-almacen` no incluye Maestro/emulador** (el plan original lo mencionaba para la segunda
   entrega, con las 5 pantallas). En esta primera entrega el árbitro es Jest + la prueba de navegación con
   `expo-router/testing-library`; no hay emulador Android disponible en este entorno para correr Maestro.

## Segunda entrega (Paso 6): Acomodar, Despacho, Conteo, Consultar, Sincronización

**Hecho igual que la primera entrega**: yo solo, sin workflow ni agentes, con las mismas convenciones de código
(pantalla = `*Logic.ts` puro + `*Api.ts` con el API real + pantalla en `src/app/`, probado con Jest contra SQL real).
Sin recorridos en el aparato real, mismo motivo que antes.

### Qué se construyó

- **Acomodar** (`features/putaway/`, `app/putaway.tsx`): lista de tareas PUTAWAY abiertas del almacén (mías primero),
  escanear la posición destino, completar. En línea de punta a punta: la tarea es de todo el almacén, no solo de este
  aparato, así que cada acción es una llamada directa, sin cola de salida.
- **Despacho** (`features/dispatch/`, `app/dispatch.tsx`): recolectar (producto, cantidad, posición de origen) sin
  señal, un despacho local a la vez; empacar (resolver las posiciones escaneadas a su id real, elegir consignatario,
  confirmar) necesita señal un momento y de ahí manda la operación completa (`collect-and-pack`) por la cola si no
  hay red en ese instante. Solo clientes 3PL por ahora (decisión 3 abajo).
- **Conteo** (`features/count/`, `app/count.tsx`): escanear la posición reclama el conteo en el servidor
  (`POST /cycle-counts`, en línea: la posición es un recurso compartido, igual que las tareas de Acomodar); de ahí en
  adelante, capturar lo encontrado (por línea esperada o producto extra) es local, y "Terminar esta posición" encola
  el lote y el cierre en la cola de salida, en ese orden. Conteo a ciegas (systemQty null) ya lo decide el servidor
  según el permiso `warehouse.count`; la pantalla solo no muestra lo que no le llega.
- **Consultar** (`features/lookup/`, `app/lookup.tsx`): un solo campo (producto o posición, texto libre), saldos en
  línea (`GET /inventory/balances`) con una copia en `balance_cache` de la última respuesta por código consultado,
  para poder responder "de hace N min" si no hay señal la próxima vez que se pregunta lo mismo.
- **Sincronización** (`app/sync.tsx`): lo que hay en la cola de salida separado en pendientes/con error, reintentar o
  descartar una fila con error, resultado de la última pasada, y "Sincronizar ahora".
- **Inicio**: los 5 botones ya navegan a su pantalla real (nada de "próximamente"); el bloqueo de "una operación a la
  vez" (decisión 6 de la primera entrega) ahora cubre Despacho y Conteo además de Recibir — cualquiera de los tres
  documentos abiertos bloquea los demás, y el que está abierto se retoma tocando su propio botón. Un toque en el
  estado de sincronización lleva a la pantalla de Sincronización.
- **Cola de salida** (`kernel/sync/outbox.ts`): se le agregó ruta dinámica y método configurable (antes solo tenía una
  ruta fija por tipo, `POST`); Conteo la necesita porque su ruta lleva un id de conteo que solo se conoce en línea.
- **Esquema local v2** (`kernel/db/schema.ts`): `local_pick`/`local_pick_line`, `local_count`/`local_count_line`,
  `balance_cache`.
- **Módulos compartidos** (`kernel/warehouse/`): `findBinByCode`/`findProductByCode`, movidos de donde vivían sueltos
  (Recibir) para que Acomodar/Despacho/Conteo/Consultar los usen todos igual.

### Pruebas (142, sin agentes revisores)

Se sumaron 64 pruebas a las 78 de la primera entrega. Igual que antes, contra SQL real (better-sqlite3 detrás del
mock de `expo-sqlite`): lógica pura de cada pantalla nueva, persistencia local (un documento a la vez por bloqueo
real, igual que Recibir), capas de API con el cliente mockeado (incluye la caída a caché de Consultar sin red), la
cola de salida con ruta dinámica, y 5 pruebas de navegación real (`expo-router/testing-library`) — una por pantalla
nueva, cada una en su propio archivo porque `renderRouter()` no aísla del todo su estado global de navegación entre
dos llamadas del mismo archivo (ya documentado en la primera entrega con `homeLock.test.tsx`).

**Lo que no verifiqué yo mismo**: igual que en la primera entrega, nada en un aparato Zebra real ni con Maestro; el
árbitro sigue siendo Jest + las pruebas de navegación.

### Decisiones tomadas por mí (a revisar)

1. **Acomodar y el "empacar" de Despacho necesitan señal por diseño, no por limitación técnica.** Las posiciones del
   almacén no se sincronizan localmente en este lote (solo productos, órdenes de compra y avisos, decisión 2 de la
   primera entrega); resolver un código de posición escaneado a su id real (`GET /warehouses/{id}/bins?search=`) es
   una llamada en línea en las tres pantallas que lo necesitan (Acomodar, Despacho al empacar, Conteo al escanear la
   posición). Se aceptó porque las tres ya necesitan señal en ese mismo momento por otra razón (la tarea es de todo
   el almacén, el consignatario se busca en línea, la posición de un conteo es un recurso compartido), así que no se
   le resta nada a "funciona sin señal" a lo que ya dependía de tenerla.
2. **Despacho resuelve la posición de origen al empacar, no al capturar la línea.** La primera versión intentaba
   resolver el `binId` real apenas se escaneaba la posición de origen (como Acomodar), pero eso habría hecho que
   recolectar también necesitara señal en cada línea. Se corrigió: se guarda el código escaneado tal cual
   (`fromBinCode`, texto libre) y se resuelve todo junto en una sola llamada cuando ya de todas formas hace falta
   señal para elegir el consignatario.
3. **Despacho, alcance de esta entrega: solo clientes 3PL.** El dueño del producto (`ownerClientPublicId`) ya viene
   sincronizado con el producto; despachar inventario propio del tenant necesita buscar cliente/ubicación en línea y
   permisos que el rol de aparato estándar no tiene (decisión 3 del Lote 8A backend, `docs/lote8A-decisiones.md`),
   así que se dejó para la web, igual que ya estaba decidido ahí.
4. **Conteo manda el lote sin `rowVersion`.** El backend hace la validación de concurrencia opcional para conteos
   (`CycleCountService.EnsureRowVersion` no hace nada si `rowVersion` viene vacío); omitirlo a propósito evita tener
   que reescribir un id/versión entre la captura en cola y el cierre en cola, sin perder nada: dos conteos de la
   misma posición ya no pueden coexistir (el servidor la reclama al abrir el conteo, un recurso compartido).
5. **`enqueueFinishCount` manda dos filas en la cola, el lote antes que el cierre**, apoyándose en que la cola ya
   manda todo en el orden en que se encoló (FIFO, decisión de diseño de la primera entrega): no hace falta esperar a
   que el lote se confirme para encolar el cierre.
6. **El bloqueo de "una operación a la vez" (decisión 6 de la primera entrega) se extendió tal cual a Despacho y
   Conteo**: cualquiera de los tres (`getOpenReceipt`/`getOpenPick`/`getOpenCount`) bloquea los otros dos con el
   mismo mecanismo (lanzar en vez de descartar en silencio, Inicio avisa en vez de navegar). Acomodar y Consultar no
   manejan ningún documento propio, así que cualquiera de los tres bloqueados también los bloquea a ellos: "nada más
   mientras haya algo en curso" tal como lo pidió Luis, sin excepciones.
7. **Filas ya enviadas (`status = 'sent'`) se quedan en la tabla `outbox` para siempre** (no se agregó limpieza en
   este lote); no afecta la cola (que solo lee `status = 'pending'`/`'rejected'`) ni la pantalla de Sincronización
   (que las filtra), pero la tabla crece sin límite. Queda para una próxima entrega si llega a importar.
8. **Encontré dos permisos que un rol típico de aparato podría no tener, y que valen la pena revisar en la
   administración de roles**: cancelar un conteo (`DELETE /cycle-counts/{id}`) exige `warehouse.count` completo, no
   solo `warehouse.count.capture` (el conteo a ciegas puede terminarlo, pero no cancelarlo); y buscar los
   consignatarios de un cliente al empacar un despacho exige `locations.read`, aparte de `warehouse.pick`. Ninguno
   de los dos rompe nada (el error del servidor se muestra tal cual, la pantalla no se traba), pero si el rol del
   aparato no tiene alguno de estos dos, esa acción concreta va a fallar con un 403 hasta que se agregue. Documentado
   en el [capítulo 9 del manual](manual/09-app-almacen.md), §10, y en la FAQ.

## Qué falta

Reporte final a Luis, y lo que ya quedó anotado en la primera entrega (cámara, reconexión inmediata NetInfo,
`expo-updates`). El manual funcional de la app ya está en `docs/manual/09-app-almacen.md`.
