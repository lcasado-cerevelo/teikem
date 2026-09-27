# App de almacén para terminales con escáner (Zebra MC3300): diseño y plan

**Estado: plan armado, pendiente de la orden de Luis para ejecutar.** Decisiones de Luis (2026-09-27): aplicación instalable
(no web), trabajo sin señal como norma, primera versión con Recibir, Acomodar, Despacho (recolectar y empacar), Conteo
(inventario físico), consulta rápida y sincronización. No es la app de choferes (módulo 8) ni el portal de clientes (que es web).
El diseño resumido vive en el maestro (`Diseño/logistica-funcionalidades-maestro.md`, "8B. App de almacén"); este archivo es el plan.

## 1. Arquitectura

- **Proyecto**: `app-almacen/` en este repo (Expo SDK actual, React Native, TypeScript), *development build* nativo (no Expo Go).
  **Es la única app móvil que se construye**; la app de choferes del módulo 8 no se hace.
  Android únicamente en esta versión. Versión mínima Android 7.0 (API 24), que cubre toda la serie MC3300.
- **Comparte con `web-app/`** (copiando módulos puros, no importando entre proyectos): cliente del API generado desde
  `openapi.json` (`openapi-fetch`), `applyProblemDetails`, i18n es/en, evaluador de reglas y utilidades de fechas. Los
  componentes visuales son propios (pantalla pequeña, botones grandes, un campo enfocado).
- **Escáner**: DataWedge de Zebra en modo *intent* (perfil creado por la app al arrancar vía API de DataWedge). Un módulo
  nativo pequeño (config plugin de Expo + receptor Android) entrega cada lectura a JavaScript como evento `{barcode, symbology}`.
  Sin EMDK ni licencias. En emulador y en teléfonos sin escáner, la app acepta el mismo código por teclado y por cámara
  (`expo-camera`) para pruebas.
- **Base local**: `expo-sqlite`. Tablas: `product` (sku, nombre, código de barras, seguimiento), `bin` (posiciones y zonas del
  almacén elegido), `purchase_order` y `asn` abiertos con líneas, `warehouse_task` pendientes, `receipt_open`, documentos
  locales en curso (`local_receipt`, `local_pick`, `local_count` con sus líneas) y `outbox` (operaciones pendientes de enviar).
- **Sincronización** (`sync/engine.ts`), en dos direcciones y siempre reanudable:
  1. *Bajada por diferencia*: `GET .../sync/...?modifiedSinceUtc=` por tabla, con marca de agua guardada por tabla.
  2. *Subida en orden*: cada operación de la `outbox` lleva `Idempotency-Key` (UUID generado al crearla), se envía en el orden
     en que se hizo, se marca `enviada` con la respuesta del servidor (números definitivos) y las siguientes operaciones del
     mismo documento se re-escriben con el id real. Un rechazo definitivo (4xx de negocio) marca la operación `requiere revisión`
     con el mensaje del API; la cola no se detiene, salta a otros documentos.
  3. Dispara: al abrir la app, cada 60 s con red, al recuperar conectividad (`NetInfo`), y con el botón "Sincronizar ahora".
- **Sesión y seguridad (ajustada a aparatos que no salen del almacén)**: el administrador registra cada terminal una vez
  desde la web ("aparato de confianza": `UserDevice` con un código que se teclea en la app la primera vez); desde entonces
  el aparato queda enlazado al tenant. Cada almacenista entra con su **PIN de 4 a 6 dígitos** (lo define en Mi cuenta de la
  web o se lo asigna el administrador; se guarda cifrado en el servidor y una copia cifrada en el aparato para desbloquear sin
  red). No hay contraseña ni MFA en el aparato. Cambiar de usuario en el mismo aparato es teclear otro PIN, así cada
  operación queda a nombre de quien la hizo (auditoría y Actividad reciente). El token del aparato dura 30 días y se renueva
  al sincronizar; si un aparato se pierde, el administrador lo desactiva en la web y deja de sincronizar en el acto (los
  datos locales viven en el almacenamiento privado de la app, borrado al desinstalar). La app también guarda el almacén elegido.
- **Vocabulario del piso**: todas las etiquetas por i18n; "Posición" es la palabra para lo que ellos llaman "almacén" en su
  sistema actual; el nombre del almacén grande se muestra como "Almacén · ALM-01".

## 2. Pantallas (primera versión)

| # | Pantalla | Flujo con el escáner | API |
|---|---|---|---|
| 1 | Entrar y elegir almacén | usuario/contraseña, luego PIN; lista de almacenes con permiso | `POST /auth/login`, `/auth/refresh`, `GET /me`, `GET /warehouses` |
| 2 | Inicio | 5 botones grandes (Recibir, Acomodar, Despacho, Conteo, Consultar) + estado de sincronización (pendientes, última vez, errores) | — |
| 3 | Recibir | escanear un código de orden de compra o aviso (crea el recibo contra él) o tocar "Recibo ciego" (lo crea sin documento); luego escanear producto → cantidad (por defecto 1; tecla + repite) → lote o series si aplica → siguiente; "Confirmar" cierra | `POST /receipts`, `POST /receipts/{id}/lines`, `PUT .../lines/{lineId}`, `POST .../confirm` |
| 4 | Acomodar | lista de tareas PUTAWAY del almacén (mías primero); abrir una: escanear posición destino (la sugerida se muestra grande) → completar | `GET /warehouse-tasks`, `GET .../putaway-suggestions`, `POST .../start`, `.../complete` |
| 5 | Despacho (recolectar y empacar) | escanear producto → cantidad → posición de la que sale (FEFO sugerido); al terminar "Empacar": cliente (buscador local), consignatario y datos mínimos de la orden; el servidor crea la orden EMP-##### | `POST /pick-batches`, `POST /pick-batches/{id}/pack` |
| 6 | Conteo (inventario físico) | escanear posición → la app lista lo que el sistema espera ahí (sin mostrar cantidades, conteo a ciegas, opción por rol) → escanear producto o teclear cantidad por producto → siguiente posición; "Terminar" envía; la reconciliación se hace en la web con `warehouse.count` | `POST /cycle-counts`, `PUT /cycle-counts/{id}/lines`, `POST .../finish` |
| 7 | Consultar | escanear producto: saldo por posición; escanear posición: qué hay ahí (de la base local, marca "datos de hace N min") | local; `GET /inventory/balances` si hay red |
| 8 | Sincronización | pendientes por tipo, errores con el mensaje del API y botón "Reintentar" o "Descartar" (con permiso), "Sincronizar ahora" | — |

Reglas de captura comunes: un solo campo enfocado; el escaneo escribe y avanza; Enter físico equivale a escanear; errores del
API bajo el campo con el mensaje exacto; sonido y vibración distintos para ok y error (el MC3300 los tiene).

## 3. Backend previo (Lote 8, `lote-implementar`)

| Pieza | Qué deja |
|---|---|
| P0 base compartida | PIN de usuario (`UserPin` cifrado en `AspNetUsers` o tabla aparte, alta desde Mi cuenta y desde Usuarios), registro de aparato de confianza con código de un solo uso, `POST /api/v1/auth/device-login` (aparato + PIN → tokens); middleware de **idempotencia** (módulo 13 del maestro): cabecera `Idempotency-Key` en POST/PUT/DELETE, respuesta guardada en `IntegrationMessageLog` por (tenant, usuario, clave) 7 días, misma clave → misma respuesta, misma clave con cuerpo distinto → 409 "La clave de idempotencia ya se usó con otro contenido."; tabla `UserDevice` (generaliza `DriverDevice`: TenantId, UserId, DriverId NULL, Platform, PushToken, AppVersion, Model, LastSeenUtc, IsActive) en estructura y seed; vida del *refresh token* configurable (`RefreshTokenDays`, por defecto 14 para dispositivos registrados, 1 para la web) |
| P1 sincronización | `GET /api/v1/sync/products`, `/sync/bins?warehousePublicId=`, `/sync/purchase-orders`, `/sync/asns`, `/sync/warehouse-tasks` con `modifiedSinceUtc` y `take` (paginado por cursor), incluyendo borrados lógicos (`isActive=false`); `GET /api/v1/products/by-barcode/{code}` (código de barras o SKU); `POST /api/v1/devices/register` y `.../heartbeat` |
| P2 operaciones desde el aparato | Ajustes para que cada operación de la cola sea una sola llamada atómica: `POST /receipts` acepta líneas completas y `confirm=true`; `POST /pick-batches` acepta `pack` en la misma llamada; `PUT /cycle-counts/{id}/lines` acepta lote de líneas; todas devuelven los números definitivos. Conteo a ciegas: `GET /cycle-counts/{id}` omite cantidades esperadas si el usuario no tiene `warehouse.count` |
| Pruebas y humo | pruebas de idempotencia (misma clave, cuerpo distinto), del cursor de sincronización y del recibo en una llamada; paso `sync` e `idempotency` en `scripts/smoke.sh` |

Sin cambios de comportamiento para la web; todo es aditivo.

## 4. App (Lote A1, workflow nuevo `app-implementar` = `fe-implementar` apuntando a `app-almacen/`)

| Pieza | Orden | Agente | Qué deja |
|---|---|---|---|
| A0 núcleo | 1 | core | proyecto Expo, cliente del API, auth + PIN, SQLite y esquema, motor de sincronización con `outbox`, módulo DataWedge, kit de captura (campo de escaneo, teclado numérico grande, lista de líneas), i18n; pruebas unitarias del motor (orden, reintentos, rechazo definitivo, re-escritura de ids) |
| A1 Recibir | 2 | core | pantalla 3 completa con lote y series |
| A2 Acomodar + Consultar | 2 | pantalla | pantallas 4 y 7 |
| A3 Despacho | 2 | core | pantalla 5 con el empaque mínimo |
| A4 Conteo | 2 | core | pantalla 6 con conteo a ciegas |
| A5 Inicio + Sincronización | 2 | pantalla | pantallas 2 y 8 |

Compuerta determinista: `npm run check` en `app-almacen/` (tipos generados, `tsc`, lint, `jest` con `@testing-library/react-native`).
Recorrido automatizado con Maestro sobre emulador Android en CI (flujo recibir → acomodar → despacho → conteo, en modo sin red
con el API apagado a mitad del recorrido y sincronización al final). Prueba en el aparato real: la hace Luis con el APK.

## 5. Compilación y entrega

- Job `android` en `.github/workflows/ci.yml`: `expo prebuild` + Gradle → APK firmado con una llave de desarrollo guardada como
  secreto del repo, publicado como artefacto de la corrida (y como *release* al etiquetar).
- Instalación: por USB desde la base (ADB) o StageNow si son varios equipos. Actualizaciones de la parte JavaScript sin
  reinstalar con `expo-updates` apuntando a una carpeta estática servida por el API (`/updates/`): decisión a ratificar.

## 6. Estimación

| Parte | Tokens |
|---|---|
| Lote 8 backend (3 piezas, 4 lentes, smoke, docs) | 3 a 4 M |
| Lote A1 app (6 piezas, motor sin señal, Maestro, docs) | 8 a 11 M |
| **Total (solo la app de almacén; la de choferes no se construye)** | **11 a 15 M** |

Sugerencia de orden: Lote 8 completo → A0 + A1 (Recibir) y probar en el aparato → A2 a A5.

## 7. Decisiones que debe ratificar Luis

1. Seguridad del aparato: registro único por el administrador + PIN por almacenista, sin contraseña ni MFA en el aparato (MFA solo en la web). Riesgo aceptado porque los aparatos no salen del almacén y el APK solo se instala en ellos.
2. Conteo a ciegas por defecto para quien no tiene `warehouse.count`; la reconciliación se hace en la web.
3. El empaque desde el aparato pide solo lo mínimo de la orden (cliente, consignatario, bultos); el resto se completa en la web.
4. Orden de compra: no se crea desde el aparato; el recibo ciego basta para recibir, y si contabilidad la necesita se genera
   desde el recibo confirmado en la web (acción nueva, fuera de este plan).
5. Actualizaciones JavaScript sin reinstalar (`expo-updates`) servidas por el API.
6. Vida del *refresh token* para dispositivos: 14 días.
