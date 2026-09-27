# App de almacén para terminales con escáner (Zebra MC3300): diseño y plan

**Estado: plan armado, pendiente de la orden de Luis para ejecutar.** Decisiones de Luis (2026-09-27): aplicación instalable
(no web), trabajo sin señal como norma, primera versión con Recibir, Acomodar, Despacho (recolectar y empacar), Conteo
(inventario físico), consulta rápida y sincronización. No es la app de choferes (módulo 8) ni el portal de clientes (que es web).
El diseño resumido vive en el maestro (`Diseño/logistica-funcionalidades-maestro.md`, "8B. App de almacén"); este archivo es el plan.

> **El contrato vigente del API es `web-app/openapi.json` y `docs/lote8A-decisiones.md`; si difieren de este plan, manda
> el contrato.** Las rutas y parámetros de §1, §2, §3 y §7 ya están alineados con el backend construido en el Lote 8A
> (decisiones 2, 8 y 25 de `docs/lote8A-decisiones.md`).

## 1. Arquitectura

- **Proyecto**: `app-almacen/` en este repo (Expo SDK actual, React Native, TypeScript), *development build* nativo (no Expo Go).
  Numeración: **Lote 8A = app de almacén** (backend previo + app, este plan); **Lote 8B = app de choferes** (diseñada en el
  módulo 8 del maestro; se construye después, reutilizando el núcleo, el motor de sincronización y el módulo de escáner de 8A).
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
  Clientes y consignatarios **no** están en la base local: el backend del Lote 8A no los sincroniza (ver "Lo que queda fuera
  de este lote" en `docs/lote8A-decisiones.md`); el empaque de inventario propio los busca en línea.
- **Sincronización** (`sync/engine.ts`), en dos direcciones y siempre reanudable:
  1. *Bajada por diferencia*: `GET .../sync/...?since=&cursor=&take=` por tabla (`take` ≤ 500), con marca de agua guardada
     por tabla: `since` = el `serverTimeUtc` de la primera página de la pasada anterior **menos 5 minutos**; se sigue
     `nextCursor` hasta que llegue `null`. El parámetro se llama exactamente `since` (otro nombre se ignora sin error y la
     pasada sería una carga completa sin las bajas); con `since` llegan también las filas con `isActive=false` para borrarlas.
  2. *Subida en orden*: cada operación de la `outbox` lleva `Idempotency-Key` (UUID generado al crearla), se envía en el orden
     en que se hizo, se marca `enviada` con la respuesta del servidor (números definitivos) y las siguientes operaciones del
     mismo documento se re-escriben con el id real. Un rechazo definitivo (4xx de negocio) marca la operación `requiere revisión`
     con el mensaje del API; la cola no se detiene, salta a otros documentos.
  3. Dispara: al abrir la app, cada 60 s con red, al recuperar conectividad (`NetInfo`), y con el botón "Sincronizar ahora".
- **Sesión y seguridad (ajustada a aparatos que no salen del almacén)**: el administrador registra cada terminal una vez
  desde la web ("aparato de confianza": `UserDevice` con un código que se teclea en la app la primera vez); desde entonces
  el aparato queda enlazado al tenant. Cada almacenista **elige su nombre en la lista de usuarios del almacén y teclea su PIN de 4 a 6 dígitos** (el PIN pertenece a ese usuario; no hay PIN compartido) (lo define en Mi cuenta de la
  web o se lo asigna el administrador; se guarda cifrado en el servidor y una copia cifrada en el aparato para desbloquear sin
  red). No hay contraseña ni MFA en el aparato. Cambiar de usuario en el mismo aparato es teclear otro PIN, así cada
  operación viaja con el token de ese usuario y queda a su nombre en `AuditLog`, `EntityStatusHistory.ChangedBy` y en la columna
  "Quién" de Actividad reciente, exactamente igual que si la hiciera desde la web. La sesión del aparato dura `Tenant.DeviceSessionDays` días (30 por
  defecto, configurable en `/tenant/settings`) y se renueva en cada refresh; si un aparato se pierde, el administrador lo desactiva en la web y deja de sincronizar en el acto (los
  datos locales viven en el almacenamiento privado de la app, borrado al desinstalar). La app también guarda el almacén elegido.
- **Vocabulario del piso**: todas las etiquetas por i18n; "Posición" es la palabra para lo que ellos llaman "almacén" en su
  sistema actual; el nombre del almacén grande se muestra como "Almacén · ALM-01".

## 2. Pantallas (primera versión)

| # | Pantalla | Flujo con el escáner | API |
|---|---|---|---|
| 1 | Registrar aparato y entrar | la primera vez, teclear el código de registro del aparato; después, elegir usuario en la lista y teclear su PIN (sin contraseña ni MFA); lista de almacenes con permiso | `POST /devices/enroll`, `POST /auth/device/users`, `POST /auth/device/login`, `/auth/refresh`, `POST /devices/heartbeat`, `GET /me`, `GET /warehouses` |
| 2 | Inicio | 5 botones grandes (Recibir, Acomodar, Despacho, Conteo, Consultar) + estado de sincronización (pendientes, última vez, errores) | — |
| 3 | Recibir | escanear un código de orden de compra o aviso (crea el recibo contra él) o tocar "Recibo ciego" (lo crea sin documento); luego escanear producto → cantidad (por defecto 1; tecla + repite) → lote o series si aplica → siguiente; "Confirmar" cierra | desde la cola, en una llamada: `POST /receipts` con `lines` y `confirm: true`; por pasos: `POST /receipts`, `POST /receipts/{id}/lines`, `PUT .../lines/{lineId}`, `POST .../confirm` |
| 4 | Acomodar | lista de tareas PUTAWAY del almacén (mías primero); abrir una: escanear posición destino (la sugerida se muestra grande) → completar | `GET /warehouse-tasks`, `GET .../putaway-suggestions`, `POST .../start`, `.../complete` |
| 5 | Despacho (recolectar y empacar) | escanear producto → cantidad → posición de la que sale (FEFO sugerido); al terminar "Empacar": cliente y consignatario (en 3PL el cliente es el dueño de los productos, ya en la base local; con inventario propio se buscan **en línea** con `GET /clients` y `GET /locations`, así que ese empaque necesita señal) y datos mínimos de la orden; el servidor crea la orden EMP-##### | desde la cola, en una llamada: `POST /pick-batches/collect-and-pack` (decisión 2 de `docs/lote8A-decisiones.md`); por pasos: `POST /pick-batches`, `POST /pick-batches/{id}/pack` |
| 6 | Conteo (inventario físico) | escanear posición → la app lista lo que el sistema espera ahí (sin mostrar cantidades, conteo a ciegas, opción por rol) → escanear producto o teclear cantidad por producto → siguiente posición; "Terminar" envía; la reconciliación se hace en la web con `warehouse.count` | `POST /cycle-counts`, `PUT /cycle-counts/{id}/lines` (o varias líneas en una llamada con `PUT /cycle-counts/{id}/lines/batch`), `POST /cycle-counts/{id}/lines` (lo encontrado), `POST .../finish` |
| 7 | Consultar | escanear producto: saldo por posición; escanear posición: qué hay ahí (de la base local, marca "datos de hace N min") | local; `GET /inventory/balances` si hay red |
| 8 | Sincronización | pendientes por tipo, errores con el mensaje del API y botón "Reintentar" o "Descartar" (con permiso), "Sincronizar ahora" | — |

Reglas de captura comunes: un solo campo enfocado; el escaneo escribe y avanza; Enter físico equivale a escanear; errores del
API bajo el campo con el mensaje exacto; sonido y vibración distintos para ok y error (el MC3300 los tiene).

## 3. Backend previo (Lote 8A-backend, `lote-implementar`)

| Pieza | Qué deja |
|---|---|
| P0 base compartida | PIN de usuario (`UserPin` cifrado en `AspNetUsers` o tabla aparte, alta desde Mi cuenta y desde Usuarios), registro de aparato de confianza con código de un solo uso, `POST /api/v1/auth/device/users` (lista de usuarios del aparato) y `POST /api/v1/auth/device/login` (aparato + secreto + usuario + PIN → tokens); middleware de **idempotencia** (módulo 13 del maestro): cabecera `Idempotency-Key` en POST/PUT/DELETE, respuesta guardada en `IntegrationMessageLog` por (tenant, usuario, clave) 7 días, misma clave → misma respuesta, misma clave con cuerpo distinto → 409 "La clave de idempotencia ya se usó con otro contenido."; tabla `UserDevice` (aparato de almacén; `DriverDevice` del Lote 4 se conserva) y `UserDeviceActivity` (último contacto, último usuario, versión de la app) en estructura y seed; vida de la sesión del aparato `Tenant.DeviceSessionDays` (30 días por defecto, configurable de 1 a 365 en `/tenant/settings`); la sesión web no cambia |
| P1 sincronización | `GET /api/v1/sync/products`, `/sync/bins?warehousePublicId=`, `/sync/purchase-orders`, `/sync/asns`, `/sync/warehouse-tasks`, `/sync/product-categories` con `since` (serverTimeUtc anterior − 5 min), `cursor` y `take` ≤ 500, incluyendo borrados lógicos (`isActive=false`); `GET /api/v1/products/by-barcode/{code}` (código de barras o SKU); `POST /api/v1/devices/enroll` y `POST /api/v1/devices/heartbeat`. Sin sincronización de clientes ni consignatarios (pendiente; ver `docs/lote8A-decisiones.md`) |
| P2 operaciones desde el aparato | Ajustes para que cada operación de la cola sea una sola llamada atómica: `POST /receipts` acepta líneas completas y `confirm=true`; recolectar y empacar en una llamada con `POST /pick-batches/collect-and-pack` (mandar `pack` a `POST /pick-batches` → 400; decisión 2 de `docs/lote8A-decisiones.md`); `PUT /cycle-counts/{id}/lines/batch` acepta un lote de líneas; todas devuelven los números definitivos. Conteo a ciegas: `GET /cycle-counts/{id}` omite cantidades esperadas si el usuario no tiene `warehouse.count` |
| Pruebas y humo | pruebas de idempotencia (misma clave, cuerpo distinto), del cursor de sincronización y del recibo en una llamada; paso `sync` e `idempotency` en `scripts/smoke.sh` |

Sin cambios de comportamiento para la web; todo es aditivo.

## 4. App (Lote 8A-app, workflow nuevo `app-implementar` = `fe-implementar` apuntando a `app-almacen/`)

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
| 8A backend (3 piezas, 4 lentes, smoke, docs) | 3 a 4 M |
| 8A app (6 piezas, motor sin señal, Maestro, docs) | 8 a 11 M |
| **Total 8A (app de almacén)** | **11 a 15 M** |
| 8B app de choferes (después; reutiliza el núcleo de 8A) | estimación aparte cuando se planifique |

Sugerencia de orden: 8A-backend completo → A0 + A1 (Recibir) y probar en el aparato → A2 a A5. 8B después.

## 7. Decisiones que debe ratificar Luis

1. Seguridad del aparato: registro único por el administrador + PIN por almacenista, sin contraseña ni MFA en el aparato (MFA solo en la web). Riesgo aceptado porque los aparatos no salen del almacén y el APK solo se instala en ellos.
2. Conteo a ciegas por defecto para quien no tiene `warehouse.count`; la reconciliación se hace en la web.
3. El empaque desde el aparato pide solo lo mínimo de la orden (cliente, consignatario, bultos); el resto se completa en la web Con inventario propio, elegir cliente y consignatario requiere señal (no se sincronizan en el Lote 8A) y un rol con `orders.create`, `clients.read` y `locations.read`, que la plantilla Operador de almacén no trae.
4. Orden de compra: no se crea desde el aparato; el recibo ciego basta para recibir, y si contabilidad la necesita se genera
   desde el recibo confirmado en la web (acción nueva, fuera de este plan).
5. Actualizaciones JavaScript sin reinstalar (`expo-updates`) servidas por el API.
6. Vida de la sesión del aparato: 30 días (`Tenant.DeviceSessionDays`, configurable de 1 a 365); la sesión web no cambia.
7. Tema claro u oscuro: el aparato trae un tema por defecto (lo fija el administrador al registrarlo; claro para almacenes
   iluminados) y cada usuario puede cambiarlo desde la cabecera; la app recuerda la elección por usuario en ese aparato.
   Mock aprobado por Luis: `docs/mobile/mock-app-almacen.html`.

## 8. Cómo apuntar la app al API (staging o desarrollo local)

La app guarda la URL del API como configuración del aparato: se teclea (o se escanea de un código QR) una sola vez en la
pantalla de registro del aparato y se puede cambiar después desde Sincronización → "Servidor". Nunca hay que recompilar.

**Staging (caso normal)**: la URL pública del staging con HTTPS (por ejemplo `https://staging.teikem.example/`). Android
bloquea el tráfico HTTP sin cifrar por defecto, así que el staging debe tener certificado válido. La base de datos es la del
staging: lo hecho desde el aparato aparece en la web del staging y viceversa.

**Desarrollo local (el API en tu computadora)**: el celular o el Zebra deben estar en la misma red WiFi que la computadora y
el API debe escuchar en todas las interfaces, no solo en `localhost`:

```
# en la carpeta del repo, Windows o Linux
set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://0.0.0.0:5000
dotnet run --project src/Teikem.Api
```

Luego, en el aparato, la URL es `http://<IP de tu computadora>:5000/` (la IP se ve con `ipconfig` en Windows o `ip addr`
en Linux; por ejemplo `http://192.168.1.20:5000/`). Como es HTTP sin cifrar, el APK de desarrollo lleva la excepción
`usesCleartextTraffic` activada; el APK de producción no la lleva y exige HTTPS. Si el firewall de Windows pregunta, hay que
permitir el puerto 5000 en redes privadas.

Para probar el modo sin señal en cualquiera de los dos casos: sincronizar una vez con el API alcanzable, poner el aparato en
modo avión, operar (recibir, contar), ver la cola en "pendientes de enviar", quitar el modo avión y ver cómo la cola se
vacía y las operaciones aparecen en la web.
