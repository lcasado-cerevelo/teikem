# Lote 8A-app — primera entrega (núcleo + Recibir): decisiones

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

## Pruebas (76, sin agentes revisores)

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
5. **APK de esta entrega es `debug`** (firma automática de Gradle, sin secreto del repositorio). Firma de producción o
   `expo-updates` para actualizar el JavaScript sin reinstalar quedan pendientes de una decisión de Luis sobre EAS o un
   secreto propio.
6. **Un recibo local a la vez por aparato** (`startLocalReceipt` descarta el anterior si no se confirmó). Evita el
   riesgo de mezclar dos recibos a medio capturar; si hace falta pausar uno y atender otro, es una mejora futura.
7. **`npm run check` de `app-almacen` no incluye Maestro/emulador** (el plan original lo mencionaba para la segunda
   entrega, con las 5 pantallas). En esta primera entrega el árbitro es Jest + la prueba de navegación con
   `expo-router/testing-library`; no hay emulador Android disponible en este entorno para correr Maestro.

## Qué falta (segunda entrega, Paso 6 del plan de trabajo)

Acomodar, Despacho (recolectar y empacar), Conteo, Consultar, pantalla de Sincronización (pendientes/errores,
reintentar/descartar, NetInfo), manual funcional de la app en `docs/manual/`, y reporte final a Luis.
