# Capítulo 8A — Backend de la app de almacén (aparatos, PIN, idempotencia, sincronización, código de barras)

Este capítulo cubre lo que el Lote 8A deja construido en el API para que, más adelante, exista una app instalable
de almacén: aparatos de confianza registrados por código, un PIN corto para entrar sin contraseña, sincronización
por diferencia de catálogos y tareas, búsqueda de producto por código de barras, y llamadas "todo en una" para la
cola de trabajo del aparato. **La app instalable en sí (pantallas, escáner, base local, cola de salida) no existe
todavía**; este lote es solo su backend. El Lote 8B (app de choferes) reutiliza esta misma base pero no se toca
aquí; la tabla `DriverDevice` del Lote 4 sigue igual.

Las operaciones "todo en una llamada" para recibos, recolección y conteo (recibo con `confirm: true`,
`collect-and-pack`, captura en lote y conteo a ciegas) se documentan junto con su funcionalidad de origen en el
[capítulo 06 — Inventario y almacén](06-inventario-y-almacen.md) (secciones 4, 6 y 7); aquí solo se resumen con un
enlace. El PIN de otros usuarios también aparece en el [capítulo 01](01-plataforma-y-seguridad.md) (sección 3.3,
junto a la administración de usuarios).

Convenciones: mismo formato de error (ProblemDetails) y `correlationId` del capítulo 01. Todo lo de este capítulo
vive bajo el módulo **WMS_LOTSERIAL** salvo donde se indique lo contrario.

---

## 1. Idempotencia de escrituras (`Idempotency-Key`)

Qué hace: evita que una escritura (alta, edición, baja) se repita por accidente cuando quien llama reintenta la
misma operación (por ejemplo, la cola de salida del aparato reintentando tras perder la señal). Se activa
enviando la cabecera `Idempotency-Key` en `POST`, `PUT`, `PATCH` o `DELETE` autenticados; sin la cabecera, todo
funciona exactamente igual que antes.

Quién puede: cualquier usuario autenticado, además del permiso propio de la operación que está llamando (la
idempotencia no agrega ni quita permisos).

Cómo se usa:
```
POST /api/v1/receipts
Idempotency-Key: 6f2b6b0e-6e2a-4e9b-9c7e-9a8f7b6c5d4e
Content-Type: application/json

{ "purchaseOrderPublicId": "...", "confirm": true, "lines": [...] }
```
Si la llamada se repite con la **misma clave y el mismo cuerpo** (mismo método, misma ruta con su query, mismo
JSON), el API no vuelve a ejecutar la operación: responde el mismo código y cuerpo que la primera vez, con la
cabecera `Idempotent-Replayed: true`. La clave es **por compañía y por usuario**: dos usuarios pueden usar la
misma clave sin chocar entre sí.

No aplica (la cabecera se ignora, aunque venga) en rutas que devuelven un secreto en claro, para no dejarlo 7
días en la bitácora de idempotencia: `/api/v1/auth`, `/api/v1/me`, `/api/v1/devices`, `/api/v1/platform`,
`/api/v1/users` (el alta de usuario devuelve la contraseña temporal) y las invitaciones de portal
(`/api/v1/clients/{id}/portal-users/invite` y `.../portal-users/{id}/resend-invite`).

### Qué decide el API según lo que ya exista con esa clave

| Situación | Resultado |
|---|---|
| No hay ningún registro con esa clave | Se ejecuta la operación (normal) |
| Misma clave, mismo cuerpo, con respuesta ya guardada | Se repite esa respuesta (mismo código y cuerpo), cabecera `Idempotent-Replayed: true`; no se vuelve a ejecutar |
| Misma clave, mismo cuerpo, **sin** respuesta todavía (otra petición en curso) | 409 `La operación con esta clave todavía se está procesando.` |
| Misma clave, **otro** cuerpo o **otra** ruta | 409 `La clave de idempotencia ya se usó con otro contenido.` |
| El registro tiene más de 7 días, o quedó "en curso" más de 10 minutos sin respuesta (proceso caído) | Se descarta y se ejecuta como si fuera nueva |

Qué se guarda para repetir: solo respuestas con código menor a 500, salvo 401, 403, 408, 423 y 429 (esos son
rechazos de acceso o de ritmo: reintentar con la misma clave después de corregirlos debe volver a intentar la
operación, no repetir el rechazo viejo). Los errores 5xx nunca se guardan. El cuerpo de la petición **nunca** se
guarda (podría traer un PIN o una contraseña), solo su huella.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Cabecera vacía, repetida, con más de 80 caracteres o con caracteres de control | `La clave de idempotencia no es válida.` | 400 |
| Misma clave con otro cuerpo o ruta | `La clave de idempotencia ya se usó con otro contenido.` | 409 |
| Misma clave sin respuesta todavía | `La operación con esta clave todavía se está procesando.` | 409 |

### Preguntas frecuentes

Ver [FAQ — Lote 8A](faq.md#lote-8a--backend-de-la-app-de-almacén-aparatos-pin-idempotencia-sincronización-y-operaciones-atómicas)
para más detalle sobre cada mensaje, por qué `/api/v1/users` queda fuera y qué pasa si se reenvía una operación
que ya falló.

---

## 2. Aparatos de confianza (`UserDevice`)

Qué hace: registra cada terminal física (pistola de radiofrecuencia, tablet) que va a usar la app de almacén,
para que un usuario pueda entrar en ella con un **PIN corto** en vez de su contraseña. El aparato se da de alta
desde la administración (con un código de registro de un solo uso) y luego se "instala" en el aparato con ese
código, que genera su propio secreto.

### 2.1 Administración del aparato

Quién puede: `devices.manage`. Módulo **WMS_LOTSERIAL**.

Cómo se usa:
- `GET /api/v1/devices` (`?includeInactive=true` para ver también los desactivados), `GET /api/v1/devices/{publicId}`.
- `POST /api/v1/devices` — `{ "code": "ZB-01", "name": "Pistola almacén 1", "defaultWarehousePublicId": "...", "theme": "DARK" }`.
  Devuelve el aparato y, **una sola vez**, `enrollCode` (8 caracteres, vence en 24 h): apúntelo o compártalo con
  quien va a instalar la app, porque no se puede volver a consultar.
- `PATCH /api/v1/devices/{publicId}` — edita nombre, almacén por defecto (o lo quita) y tema; el código no cambia.
- `POST /api/v1/devices/{publicId}/enroll-code` — genera un código de registro nuevo (el anterior deja de servir).
- `POST /api/v1/devices/{publicId}/deactivate` / `.../reactivate`.

### Validaciones (alta y edición)

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Código vacío (o solo espacios) | `El código del aparato es obligatorio.` | 400 |
| Código de más de 30 caracteres | `El código del aparato admite hasta 30 caracteres.` | 400 |
| Código ya usado por otro aparato de la compañía (activo o no) | `Ya existe un aparato con ese código.` | 409 |
| Nombre de más de 100 caracteres | `El nombre admite hasta 100 caracteres.` | 400 |
| Modelo de más de 80 caracteres | `El modelo admite hasta 80 caracteres.` | 400 |
| Tema distinto de `LIGHT`/`DARK` | `El tema no es válido; use LIGHT o DARK.` | 400 |
| Almacén por defecto dado de baja | `El almacén por defecto está dado de baja.` | 422 |
| Regenerar el código de un aparato desactivado | `El aparato está desactivado; reactívelo antes de generar un código de registro.` | 422 |
| Aparato de otra compañía o inexistente | `Aparato no encontrado.` | 404 |

### 2.2 Registro en el aparato y heartbeat (anónimo, sin sesión)

Estos dos endpoints no llevan `Authorization`: el propio aparato se identifica con su código de registro o con
su secreto. Están limitados a 10 (`enroll`) y 60 (`heartbeat`) peticiones por minuto y por dirección IP.

Cómo se usa:
- `POST /api/v1/devices/enroll` — `{ "enrollCode": "AB12CD34", "model": "Zebra TC21", "appVersion": "1.0.0" }`.
  Devuelve, **una sola vez**, `deviceSecret` (la app lo guarda cifrado en el aparato; sin él no vuelve a entrar) y
  el `defaultWarehousePublicId`/`theme` con los que arrancar. Instalar de nuevo con un código válido revoca las
  sesiones que el aparato tuviera abiertas.
- `POST /api/v1/devices/heartbeat` — `{ "devicePublicId": "...", "deviceSecret": "...", "appVersion": "1.0.1" }`.
  Actualiza "visto por última vez" y la versión de la app; responde `{ isActive, defaultWarehousePublicId, theme,
  serverTimeUtc }`. Un aparato desactivado (o cuya compañía perdió el módulo) recibe `isActive: false` **sin
  error**, para que la propia app bloquee la entrada.

### Validaciones

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Código de registro inválido, vencido, de un aparato desactivado o de una compañía sin el módulo | `El código de registro no es válido o venció.` | 401 |
| Secreto que no corresponde al aparato (heartbeat) | `El aparato no está registrado o fue desactivado.` | 401 |
| Demasiadas peticiones en un minuto (por IP y ruta) | `Demasiados intentos; espere un minuto e intente de nuevo.` | 429 |

### Estatus y transiciones

El aparato no tiene un flujo de estatus formal; es activo o no (`IsActive`).

| De → a | Quién | Qué hace | Qué bloquea |
|---|---|---|---|
| Activo → Desactivado (`.../deactivate`) | `devices.manage` | Invalida el código de registro pendiente; revoca **todas** las sesiones (refresh tokens) emitidas a ese aparato; el corte de los access tokens ya emitidos es inmediato (hasta 60 s de retraso en otra instancia del API) | El aparato deja de sincronizar y de recibir `POST /auth/device/login`; el heartbeat responde `isActive: false` en vez de error |
| Desactivado → Activo (`.../reactivate`) | `devices.manage` | Conserva el secreto ya instalado | **No** revive las sesiones revocadas: cada usuario vuelve a entrar con su PIN |

---

## 3. PIN de usuario y login por aparato

Qué hace: cada usuario define un PIN corto (4 a 6 dígitos) para entrar en un aparato ya registrado, sin escribir
su contraseña ni pasar por MFA. El PIN es **por compañía**: el mismo usuario puede tener un PIN distinto (o
ninguno) en cada compañía a la que pertenece.

### 3.1 Mi cuenta (el propio PIN)

Quién puede: cualquier usuario interno, con el módulo **WMS_LOTSERIAL** encendido.

Cómo se usa:
- `GET /api/v1/me/pin` — estado (`hasPin`, `lockedUntilUtc`, `updatedAtUtc`); el PIN nunca se devuelve.
- `PUT /api/v1/me/pin` — `{ "currentPassword": "...", "pin": "4826" }`. Reinicia el contador de intentos y el
  bloqueo si los había.
- `DELETE /api/v1/me/pin` — quita el PIN y cierra las sesiones que el usuario tuviera abiertas en aparatos.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Contraseña actual incorrecta | `La contraseña actual es incorrecta.` (campo `currentPassword`) | 400 |
| PIN con menos de 4 o más de 6 dígitos, o con algo que no sea un dígito | `El PIN debe tener de 4 a 6 dígitos.` | 400 |
| PIN trivial (todos los dígitos iguales, o una secuencia consecutiva como `1234` o `9876`) | `El PIN no puede ser una secuencia trivial.` | 400 |

### 3.2 PIN de otro usuario (administración)

Quién puede: `devices.manage` **o** `admin.users`; asignarlo exige además **AAL2** (reautenticación reciente,
igual que cambiar roles). Módulo **WMS_LOTSERIAL**.

Cómo se usa: `PUT /api/v1/users/{id}/pin` — `{ "pin": "4826" }`; `DELETE /api/v1/users/{id}/pin`.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Sin `devices.manage` ni `admin.users` | `Falta el permiso 'devices.manage' o 'admin.users'.` | 403 |
| Usuario de otra compañía, de portal, o administrador de plataforma | `Usuario no encontrado.` | 404 |
| El usuario destino tiene permisos que quien llama no tiene | `No puede asignar ni quitar el PIN de un usuario con más permisos que usted.` | 403 |
| PIN inválido (formato o trivial) | igual que en Mi cuenta | 400 |

El PIN impuesto por un administrador abre una sesión sin contraseña ni MFA; por eso no se puede asignar (ni
quitar) el PIN de alguien con más permisos efectivos que quien lo hace, y el administrador de plataforma nunca
es visible ni entra por esta vía.

### 3.3 Lista de usuarios del aparato y login por PIN (anónimo)

Cómo se usa (ambos sin `Authorization`, autenticados con aparato + secreto; límite de 60 por minuto por IP):
- `POST /api/v1/auth/device/users` — `{ "devicePublicId": "...", "deviceSecret": "..." }`. Devuelve los usuarios
  internos activos, con membresía activa en la compañía del aparato, con **PIN definido** y con `inventory.view`,
  ordenados por nombre (nunca el administrador de plataforma).
- `POST /api/v1/auth/device/login` — `{ "devicePublicId": "...", "deviceSecret": "...", "userId": 12, "pin":
  "4826" }`. Devuelve el mismo par de tokens (`access`/`refresh`) que un login normal; la sesión queda ligada al
  aparato, dura `Tenant.DeviceSessionDays` días (30 por defecto, configurable de 1 a 365 en `PUT
  /api/v1/tenant/settings` con `admin.tenant`) y se renueva en cada refresh mientras el aparato siga activo.

### Validaciones

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Aparato inválido o desactivado | `El aparato no está registrado o fue desactivado.` | 401 |
| PIN incorrecto, usuario sin PIN, sin membresía activa o administrador de plataforma | `PIN incorrecto.` | 401 |
| PIN bloqueado (ver abajo) | `PIN bloqueado por 15 minutos.` | 423 |
| Usuario válido y PIN correcto, pero sin `inventory.view` | `Falta el permiso 'inventory.view'.` | 403 |
| `deviceSessionDays` fuera de 1–365 (configuración) | `Entre 1 y 365 días.` | 400 |
| Cambiar de compañía con una sesión de aparato (`POST /auth/switch-tenant`) | `La sesión de un aparato no cambia de compañía.` | 403 |

### Estatus y transiciones — bloqueo del PIN

| De → a | Qué lo dispara | Efecto |
|---|---|---|
| Sin bloqueo → Sin bloqueo (con un fallo) | PIN incorrecto (fallo 1.º a 4.º) | Cuenta el intento; responde 401 `PIN incorrecto.` |
| Sin bloqueo → Bloqueado 15 minutos | 5.º fallo seguido (cuenta también los que llegan en paralelo) | Responde 423 `PIN bloqueado por 15 minutos.`; el PIN correcto **también** recibe 423 mientras dure |
| Bloqueado → Sin bloqueo | Pasan los 15 minutos, **o** alguien redefine el PIN (Mi cuenta o un administrador) | Vuelve a aceptar intentos con el contador en cero |
| Cualquiera → Sin bloqueo (con acierto) | PIN correcto fuera de bloqueo | Reinicia el contador a cero |

---

## 4. Sincronización por diferencia

Qué hace: permite que el aparato mantenga una base local (para trabajar sin red) y la ponga al día pidiendo solo
lo que cambió desde la última vez, en páginas de hasta 500 filas.

Quién puede: `inventory.view` en todos los recursos; el de órdenes de compra exige **además** el módulo
**PURCHASING** y `purchasing.view` (las mismas reglas que la pantalla nativa de órdenes de compra). Módulo
**WMS_LOTSERIAL**.

Cómo se usa: `GET /api/v1/sync/products`, `/sync/bins?warehousePublicId=`, `/sync/purchase-orders`, `/sync/asns`,
`/sync/warehouse-tasks?warehousePublicId=`, `/sync/product-categories`, todos con `since`, `cursor` y `take`.
Protocolo:
1. Primera vez: sin `since`, trae todo lo vigente.
2. Mientras la respuesta traiga `nextCursor`, pedir la siguiente página con ese cursor tal cual (sin cambiar
   `since` ni los filtros).
3. Cuando `nextCursor` sea `null`, guardar el `serverTimeUtc` de la **primera** página de esa pasada. En la
   siguiente pasada, pedir `since` = ese `serverTimeUtc` **menos 5 minutos** (margen de seguridad).
4. Con `since`, además de lo nuevo llegan las filas dadas de baja, cerradas o canceladas con `isActive: false`:
   el aparato las borra de su base local.

### Validaciones

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| `take` mayor a 500 | `El máximo por página es 500.` | 400 |
| `cursor` que no salió de una respuesta de este API | `El cursor no es válido.` | 400 |
| Sincronizar órdenes de compra sin `purchasing.view` | (403 de la política de permisos, sin cuerpo) | 403 |
| Sincronizar órdenes de compra con el módulo PURCHASING apagado | `El módulo 'PURCHASING' no está habilitado para esta compañía.` | 403 |
| Almacén filtrado de otra compañía o inexistente | `Almacén no encontrado.` | 404 |

---

## 5. Búsqueda por código de barras

Qué hace: encuentra un producto escaneando su código de barras (o, si no coincide ninguno, su SKU exacto), solo
entre productos activos. Pensado para el escáner del aparato.

Quién puede: `inventory.view`. Módulo **WMS_LOTSERIAL**.

Cómo se usa: `GET /api/v1/products/by-barcode/{código}` → misma ficha que `GET /api/v1/products/{publicId}`.

### Validaciones

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Ningún producto activo con ese código de barras ni ese SKU | `No hay un producto con ese código.` | 404 |

---

## 6. Operaciones atómicas de la cola del aparato

Estas llamadas existen para que el aparato (sin red confiable) haga en **una sola petición** lo que en la web son
varios pasos, todo o nada dentro de una transacción. Documentadas en detalle en el capítulo 06:

- **Recibo en una llamada**: `POST /api/v1/receipts` con `"confirm": true` — crea el recibo, captura sus líneas y
  lo confirma; ver [06 §4](06-inventario-y-almacen.md#4-recepción-avisos-de-llegada-asn-y-recibos).
- **Recolectar y empacar en una llamada**: `POST /api/v1/pick-batches/collect-and-pack` — ver
  [06 §7](06-inventario-y-almacen.md#7-recolección-y-empaque-ad-hoc-pick--pack).
- **Captura de conteo en lote**: `PUT /api/v1/cycle-counts/{id}/lines/batch` (varias líneas en una llamada; renglón
  repetido → 400 `La línea se repite en la solicitud.`) — ver
  [06 §6](06-inventario-y-almacen.md#6-conteo-cíclico-modo-informado).
- **Conteo a ciegas**: quien tiene `warehouse.count.capture` (implícito en `warehouse.count`) pero no
  `warehouse.count` cuenta sin ver lo esperado (`isBlind: true`; cantidades esperadas en `null`) y no puede
  reconciliar — ver [06 §6 — Lote 8A](06-inventario-y-almacen.md#lote-8a--conteo-a-ciegas-app-de-almacén).

Todas respetan `Idempotency-Key` (sección 1): un reintento con la misma clave y el mismo cuerpo no vuelve a crear
el recibo, la recolección ni las líneas de conteo.

---

## 7. Permisos y módulos (resumen del lote)

| Permiso | Categoría | Qué habilita |
|---|---|---|
| `devices.manage` | SECURITY | Alta, edición, desactivar/reactivar y regenerar código de los aparatos; asignar o quitar el PIN de otros usuarios |
| `warehouse.count.capture` | WAREHOUSE | Contar a ciegas (alta, captura, lo encontrado, terminar) sin reconciliar; implícito en `warehouse.count` |

Módulo **WMS_LOTSERIAL** cubre todo este capítulo (aparatos, PIN, login por aparato, sincronización, código de
barras y las operaciones atómicas). La sincronización de órdenes de compra exige además el módulo **PURCHASING**.

---

## 8. Preguntas frecuentes

Este capítulo resume las validaciones con su mensaje exacto; para la explicación en lenguaje llano de cada una
(qué hacer, por qué pasa) ver la sección **Lote 8A** de [`faq.md`](faq.md), que incluye además casos de uso
(por qué no aparezco en la lista del aparato, qué pasa si desactivo un aparato con sesiones abiertas, por qué
`Idempotency-Key` no aplica en `/api/v1/users`, etc.).
