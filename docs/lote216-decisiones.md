# Lote 216 — App móvil: estación de escaneo de rutas, con trabajo sin señal

## Decisión de Luis (2026-10-24)
El personal usa un Android con un mango y gatillo para disparar la lectura del código de barras. Con él escanea los paquetes de la ruta y el sistema va asignando y cambiando de estatus los envíos. Es una pantalla de la app móvil del personal y **debe funcionar sin señal**: se bajan al teléfono todos los envíos no completados (incluidos los «En tránsito»), **hasta 5000, los más recientes** (los viejos casi siempre son un error). Fuera de lo bajado y sin señal, la pieza no se encuentra. Las acciones son **las mismas de la web** (Recoger, Llegó, Enrutar, Salir y Devolver). Sobre Enrutar sin señal dijo que no sea «como en la web» pero no decidió cómo: se hizo lo más simple (ver decisiones).

## Qué se construyó
- **Servidor**
  - `GET /api/v1/mobile/scan/snapshot?skip=&take=` (`trips.scan` + módulo de transporte): la descarga por páginas de hasta 500. Todos los envíos **no terminales** de la compañía (incluidos los fallidos y «En tránsito»), los **5000 más recientes** por fecha de creación, cada uno con estatus, ruta vigente, si es de pick up y sus **piezas con las acciones ya escaneadas** y su faltante. La primera página trae además el pipeline de estatus y las rutas abiertas. `GET /api/v1/mobile/scan/routes` refresca solo las rutas.
  - **Enrutar a la ruta escogida**: `POST /api/v1/scan/action` y `/scan/outbound` aceptan `tripPublicId`. Con ella el envío libre y elegible va a **esa** ruta (planeada, o ya despachada o en curso, como «asignar en la calle») en vez de a la ruta de su zona. Una ruta cerrada/cancelada/llena o inexistente deja el envío sin asignar con su mensaje (`NO_OPEN_ROUTE`) y la acción Enrutar se deshace como siempre. Sin `tripPublicId`, todo igual que antes.
- **App** (pantalla **Escanear** en el inicio, con `trips.scan`):
  - Un campo de entrada siempre enfocado y **sin teclado en pantalla**: el lector con gatillo escribe como un teclado, termina con Enter y la pantalla vuelve a enfocar el campo (la cámara queda como respaldo). Cada lectura se contesta **al instante** con color, vibración y voz «Sí / No / Ok» (se puede apagar), con el conteo «4 de 5 piezas» y el envío.
  - Acciones: Recoger, Llegó, Enrutar, Salir y, con `orders.return` y señal, Devolver. Para Enrutar se puede escoger la ruta (la lista baja con la descarga); si no se escoge, el servidor usa la de la zona.
  - **Sin señal**: la descarga vive en SQLite del teléfono (tablas por compañía, en una "generación" que solo reemplaza a la anterior si llegó completa) y la pieza se resuelve ahí con la **misma regla del servidor** (final no se toca, duplicado es Ok, la primera pieza de una acción mueve el envío brincando estatus y las demás solo cuentan, pick up no sale a ruta, desde un lateral se cuenta desde Recogida). Cada lectura que cuenta se **encola** (la misma cola de las entregas) y sale sola al volver la señal contra `scan/action`, que es idempotente por pieza y acción. La cola ahora se vacía **de 25 en 25 hasta terminar**.
  - **Lo que el servidor no acepte** (p. ej. no hay ruta que reciba el envío, ya se entregó) aparece en **«Con problema»** con el mensaje del servidor.
  - Pieza fuera de lo bajado: con señal se le pregunta al servidor; sin señal, «No se encontró la pieza». Un número repetido en varios envíos no se confirma sin señal.
  - **Cerrar conteo** (con señal) y **Sincronizar** (descarga manual; al abrir se baja sola si falta o tiene más de 30 minutos y hay señal).
- **CI**: el trabajo de la app móvil instalaba con `npm ci` pero el `package-lock.json` está en `.gitignore`, así que fallaba desde el Lote 213; ahora usa `npm install`.

## Cómo se probó
- Servidor: 3 pruebas nuevas (`TripStatusEffectTests.ScanSnapshot.cs`): la descarga trae solo los no completados, más recientes primero, con piezas y acciones; el tope (3 de 5, paginado) conserva los más recientes y estatus/rutas solo en la primera página; escanear con ruta escogida la pone ahí aunque ya esté despachada y una ruta cerrada o inexistente deja el envío sin asignar. Se ajustó la prueba de contrato de las firmas. Suite completa en verde.
- App: `tsc`, lint de lo tocado, pruebas puras nuevas (`npm run test:scan`: la regla local contra todos los casos del servidor) y `expo export`. **No probado en teléfono, ni con el gatillo real, ni contra el API real, ni con 5000 envíos de verdad** (el rendimiento de la descarga en SQLite y de las búsquedas por pieza es lo primero que hay que medir).

## Decisiones a revisar
- **Enrutar sin señal**: como no se definió, se escoge **la ruta en la pantalla** antes de escanear (o ninguna = la de la zona del envío al sincronizar). Si quieres otra cosa (por ejemplo, asignar por zona en el teléfono con las rutas bajadas), es un ajuste en la regla local.
- **Hora del escaneo**: el servidor marca el historial con la hora de la **sincronización**, no la del escaneo. Para guardar la hora real hay que cambiar el servidor (el estatus se escribe con la hora del servidor).
- **Dos teléfonos escaneando lo mismo sin señal**: cada uno ve su propia descarga; al sincronizar el servidor resuelve (el primero mueve el envío; el otro cuenta o aparece en «Con problema»).
- **Más de 5000 no completados**: se bajan los 5000 más recientes y la pantalla lo dice; el resto solo se encuentra con señal.
- **Devolver** no funciona sin señal (crea la orden de vuelta en el servidor).
- La voz usa la voz del teléfono (`expo-speech`); si el Android del gatillo no tiene voz instalada, quedan la vibración y el color.
