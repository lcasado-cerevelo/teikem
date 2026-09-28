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
misma clave sin chocar entre sí. La clave **distingue mayúsculas**: `abc-1` y `ABC-1` son dos claves distintas.

Antes de repetir una respuesta guardada, el API vuelve a exigir lo que la ruta exige siempre, repetición o no
(esto no lo agrega la idempotencia): el permiso propio de la ruta (`[RequirePermission]`, ya lo revisa la
autorización de cada petición), el módulo (`[RequireModule]`) y la ventana de reautenticación (`[RequireAal2]`)
cuando la ruta la exige; si algo de esto ya no se cumple, el 403 (o `module_disabled`/`aal2_required`) es igual al
de una llamada nueva. Además, la operación original pudo comprobar por su cuenta, dentro del servicio o el
controlador, otros módulos o permisos que no son los de la ruta (por ejemplo `purchasing.receive` y el módulo
**PURCHASING** al recibir contra una orden de compra, `orders.create` al recolectar y empacar, o `warehouse.count`,
que decide si el conteo se ve a ciegas): antes de repetir se vuelven a evaluar y, si **cualquiera** de ellos cambió,
en **cualquier sentido** (se perdió o se ganó), la respuesta guardada ya no se puede repetir: 409 `La operación con
esta clave ya no puede repetirse con los permisos actuales.`, sin devolverla. No importa si el cambio hizo que la
operación fuera ahora más permisiva o menos: cualquiera de los dos casos da este mismo 409, nunca la respuesta
guardada.

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
| Misma clave y mismo cuerpo, pero el permiso, el módulo o la reautenticación que **exige la ruta** ya no se cumplen | El mismo 403 (o `module_disabled`/`aal2_required`) de una llamada nueva; esto no cambia con la idempotencia |
| Misma clave y mismo cuerpo, y cambió — se ganó **o** se perdió, cualquiera de los dos — un módulo o permiso que la operación revisó **por su cuenta** dentro del servicio o el controlador (p. ej. el conteo a ciegas) | 409 `La operación con esta clave ya no puede repetirse con los permisos actuales.` (ya no importa el sentido del cambio) |
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
| Misma clave y cuerpo, con un módulo o permiso revisado **dentro de la operación** (no el de la ruta) que cambió desde la primera vez, perdido o ganado | `La operación con esta clave ya no puede repetirse con los permisos actuales.` | 409 |

La longitud (1 a 80) se mide sobre el valor **tal como llega** en la cabecera, antes de recortar los espacios de
los extremos: una clave de 81 caracteres con espacios alrededor sigue siendo inválida aunque, ya recortada, quedara
en 80 o menos.

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
  Acepta la `rowVersion` que devolvió el `GET` (concurrencia optimista): responde 409 solo si otro administrador editó,
  desactivó, reactivó o regeneró el código del aparato entretanto. El heartbeat y los logins con PIN **no** cambian la
  `rowVersion` (los datos técnicos "visto por última vez", "último usuario" y "versión de la app" se guardan aparte), así
  que un aparato en línea no hace fallar la edición.
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
| `rowVersion` vieja: otro administrador cambió el aparato después de que usted lo abrió | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |

### 2.2 Registro en el aparato y heartbeat (anónimo, sin sesión)

Estos dos endpoints no llevan `Authorization`: el propio aparato se identifica con su código de registro o con
su secreto. Están limitados a 10 (`enroll`) y 60 (`heartbeat`) peticiones por minuto y por dirección IP (la ruta se
normaliza a minúsculas y sin la barra final antes de contar: `/Enroll/` y `/enroll` comparten el mismo cupo).

Cómo se usa:
- `POST /api/v1/devices/enroll` — `{ "enrollCode": "AB12CD34", "model": "Zebra TC21", "appVersion": "1.0.0" }`.
  Devuelve, **una sola vez**, `deviceSecret` (la app lo guarda cifrado en el aparato; sin él no vuelve a entrar) y
  el `defaultWarehousePublicId`/`theme` con los que arrancar. Instalar de nuevo con un código válido revoca las
  sesiones que el aparato tuviera abiertas y fija de nuevo el sello de sesiones del aparato (como desactivar o
  reactivar): un access token con `did` emitido antes de esta reinstalación deja de servir aunque el aparato siga
  activo. Registrar y desactivar corren en una sola transacción. Dos registros a la vez con el mismo código
  (carrera): el que pierde recibe el mismo 401 `El código de registro no es válido o venció.` (nunca 409), con el
  mismo evento de seguridad que un código inválido (`API_CREDENTIAL` / `FAILURE`, `action = device_enroll`); el que
  gana lo registra con éxito (`API_CREDENTIAL` / `SUCCESS`, `action = device_enrolled`).
- `POST /api/v1/devices/heartbeat` — `{ "devicePublicId": "...", "deviceSecret": "...", "appVersion": "1.0.1" }`.
  Actualiza "visto por última vez" y la versión de la app; responde `{ isActive, defaultWarehousePublicId, theme,
  serverTimeUtc }`. Un aparato desactivado (o cuya compañía perdió el módulo) recibe `isActive: false` **sin
  error**, para que la propia app bloquee la entrada.

### Validaciones

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Código de registro inválido, vencido, **ausente**, de un aparato desactivado o de una compañía sin el módulo | `El código de registro no es válido o venció.` | 401 |
| Secreto que no corresponde al aparato, o sin `deviceSecret` (heartbeat) | `El aparato no está registrado o fue desactivado.` | 401 |
| Demasiadas peticiones en un minuto (por IP y ruta) | `Demasiados intentos; espere un minuto e intente de nuevo.` | 429 |

### Estatus y transiciones

El aparato no tiene un flujo de estatus formal; es activo o no (`IsActive`). Además guarda un sello
(`UserDevice.SessionsNotBeforeUtc`) con el momento de la última baja, reactivación o registro: cualquier access
token con el claim `did` (aparato) emitido **antes** de ese sello (comparado por su `iat`, al segundo, sin
tolerancia de reloj) se rechaza al validarlo, aunque el aparato esté activo hoy; un token sin `iat` también se
rechaza. Cada baja, reactivación o registro que revoque sesiones abiertas escribe `TOKEN_REVOKED` con el motivo
(`device_deactivated`, `device_reactivated` — siempre, aunque no hubiera ninguna sesión viva — o `device_enrolled`).

| De → a | Quién | Qué hace | Qué bloquea |
|---|---|---|---|
| Activo → Desactivado (`.../deactivate`) | `devices.manage` | Invalida el código de registro pendiente; revoca **todas** las sesiones (refresh tokens) emitidas a ese aparato y fija el sello de sesiones, todo en una sola transacción; el corte de los access tokens ya emitidos es inmediato (hasta 60 s de retraso en otra instancia del API) | El aparato deja de sincronizar y de recibir `POST /auth/device/login`; el heartbeat responde `isActive: false` en vez de error |
| Desactivado → Activo (`.../reactivate`) | `devices.manage` | Conserva el secreto ya instalado; revoca las sesiones que hubieran quedado vivas y fija de nuevo el sello de sesiones del aparato | **No** revive las sesiones revocadas ni los access tokens emitidos antes de la baja o de la propia reactivación (siguen en 401): cada usuario vuelve a entrar con su PIN |

---

## 3. PIN de usuario y login por aparato

Qué hace: cada usuario define un PIN corto (4 a 6 dígitos) para entrar en un aparato ya registrado, sin escribir
su contraseña ni pasar por MFA. El PIN es **por compañía**: el mismo usuario puede tener un PIN distinto (o
ninguno) en cada compañía a la que pertenece. Al verificarlo en el login por aparato se recorta igual que al
definirlo: los espacios al inicio o al final no cuentan (` 4826 ` entra igual que `4826`).

### 3.1 Mi cuenta (el propio PIN)

Quién puede: cualquier usuario interno, con el módulo **WMS_LOTSERIAL** encendido.

Cómo se usa:
- `GET /api/v1/me/pin` — estado (`hasPin`, `lockedUntilUtc`, `updatedAtUtc`); el PIN nunca se devuelve.
- `PUT /api/v1/me/pin` — `{ "currentPassword": "...", "pin": "4826" }`. Reinicia el contador de intentos y el
  bloqueo si los había. **Cambiar** un PIN que ya existía cierra las sesiones del usuario en aparatos: su refresh
  responde 401 y debe volver a entrar con el PIN nuevo (evento `TOKEN_REVOKED`, motivo `pin_changed`).
- `DELETE /api/v1/me/pin` — quita el PIN y cierra las sesiones que el usuario tuviera abiertas en aparatos.

La contraseña actual equivocada en `PUT /api/v1/me/pin` **cuenta** en el bloqueo de la cuenta, el mismo del login
web: 5 contraseñas equivocadas seguidas (sumando login, `PUT /me/pin`, `POST /auth/reauth` y `PUT /auth/password`)
bloquean la cuenta 15 minutos, también para entrar en la web. Mientras dure, `PUT /me/pin` responde el mismo 400
`La contraseña actual es incorrecta.` aunque la contraseña sea correcta (la reautenticación responde 401
`Contraseña incorrecta.` y el cambio de contraseña el mismo 400 de contraseña actual incorrecta). Una contraseña
correcta antes del bloqueo reinicia el contador. Así, una sesión abierta con PIN en un aparato no sirve para
adivinar la contraseña de la web.

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Contraseña actual incorrecta o sin `currentPassword` | `La contraseña actual es incorrecta.` (campo `currentPassword`) | 400 |
| Cuenta bloqueada por intentos fallidos de contraseña (5 seguidos entre login, `PUT /me/pin`, reautenticación y cambio de contraseña; dura 15 minutos), aunque la contraseña sea correcta | `La contraseña actual es incorrecta.` (campo `currentPassword`) | 400 |
| PIN con menos de 4 o más de 6 dígitos, o con algo que no sea un dígito | `El PIN debe tener de 4 a 6 dígitos.` | 400 |
| PIN trivial (todos los dígitos iguales, o una secuencia consecutiva como `1234` o `9876`) | `El PIN no puede ser una secuencia trivial.` | 400 |
| El PIN se definió al mismo tiempo en otra sesión | `El PIN del usuario cambió al mismo tiempo en otra sesión; intente de nuevo.` | 409 |

### 3.2 PIN de otro usuario (administración)

Quién puede: `devices.manage` **o** `admin.users`; asignarlo exige además **AAL2** (reautenticación reciente,
igual que cambiar roles). Módulo **WMS_LOTSERIAL**. El permiso se revisa **antes** que la reautenticación: a quien no
tiene ninguno de los dos permisos se le responde 403 de permiso (queda `PERMISSION_DENIED` en la bitácora de
seguridad), nunca "requiere reautenticación"; a quien sí tiene el permiso pero no se reautenticó, 403 `aal2_required`.

Cómo se usa: `PUT /api/v1/users/{id}/pin` — `{ "pin": "4826" }`; `DELETE /api/v1/users/{id}/pin`. Restablecer
(o quitar) el PIN de un usuario que ya tenía uno cierra sus sesiones en aparatos: quien hubiera entrado con el PIN
viejo pierde la sesión en el siguiente refresh (401).

### Validaciones

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Sin `devices.manage` ni `admin.users` | 403 de la política de permisos (código `forbidden`, sin cuerpo propio; evento `PERMISSION_DENIED` con `devices.manage\|admin.users`) | 403 |
| Con el permiso pero sin reautenticación reciente (solo `PUT`) | `Esta acción requiere reautenticación reciente (AAL2).` (código `aal2_required`) | 403 |
| Usuario de otra compañía, de portal, o administrador de plataforma | `Usuario no encontrado.` | 404 |
| El usuario destino tiene permisos que quien llama no tiene | `No puede asignar ni quitar el PIN de un usuario con más permisos que usted.` | 403 |
| PIN inválido (formato o trivial) | igual que en Mi cuenta | 400 |
| El PIN se definió al mismo tiempo en otra sesión | `El PIN del usuario cambió al mismo tiempo en otra sesión; intente de nuevo.` | 409 |

El PIN impuesto por un administrador abre una sesión sin contraseña ni MFA; por eso no se puede asignar (ni
quitar) el PIN de alguien con más permisos efectivos que quien lo hace, y el administrador de plataforma nunca
es visible ni entra por esta vía.

Ese límite **no se revisa solo al asignarlo**: en cada login por aparato y en cada refresh de una sesión de aparato
se vuelve a comprobar contra quien asignó el PIN (`UserPin.UpdatedBy`). Si después al usuario le subieron los
permisos (roles, permisos extra o un rol editado), o quien lo asignó perdió los suyos, se desactivó o dejó la
compañía, el PIN deja de servir: el login responde 403 `Su PIN lo asignó otra persona que ya no tiene sus permisos; defina su propio PIN en Mi cuenta.` (evento `LOGIN` / `BLOCKED` con
`reason = pin_assigner_lower_privileges`) y el refresh de la sesión abierta responde 401 con el mismo mensaje y la
revoca (evento `TOKEN_REVOKED`). Se exceptúa el PIN asignado por un administrador de plataforma activo. El PIN que
el propio usuario define en Mi cuenta nunca se ve afectado; la salida es que lo defina él (o que se lo reasigne
alguien con al menos sus permisos).

### 3.3 Lista de usuarios del aparato y login por PIN (anónimo)

Cómo se usa (ambos sin `Authorization`, autenticados con aparato + secreto; límite de 60 por minuto por IP):
- `POST /api/v1/auth/device/users` — `{ "devicePublicId": "...", "deviceSecret": "..." }`. Devuelve los usuarios
  internos activos, con membresía activa en la compañía del aparato, con **PIN definido** y con `inventory.view`,
  ordenados por nombre (nunca el administrador de plataforma). Quien no tiene nombre completo cargado se muestra
  como `Usuario` (nunca su correo: el aparato es compartido).
- `POST /api/v1/auth/device/login` — `{ "devicePublicId": "...", "deviceSecret": "...", "userId": 12, "pin":
  "4826" }`. Devuelve el mismo par de tokens (`access`/`refresh`) que un login normal; la sesión queda ligada al
  aparato, dura `Tenant.DeviceSessionDays` días (30 por defecto, configurable de 1 a 365 en `PUT
  /api/v1/tenant/settings` con `admin.tenant`) y se renueva en cada refresh mientras el aparato siga activo y la
  compañía siga activa con el módulo **WMS_LOTSERIAL** encendido. Si el aparato se desactiva o se apaga el módulo,
  `POST /api/v1/auth/refresh` de esa sesión responde 401 `El aparato no está registrado o fue desactivado.` y la
  sesión queda revocada (encender otra vez el módulo no la revive: se vuelve a entrar con el PIN). Cada refresh de
  una sesión de aparato revalida además al **usuario** (la misma condición que `device/login` y que el límite de
  privilegios del PIN de 3.2): que siga activo, con membresía activa en esta compañía y con `inventory.view`. Si
  alguna ya no se cumple, el refresh responde 401 `Refresh token inválido.` y revoca la sesión (evento
  `TOKEN_REVOKED`); no basta con que el aparato siga activo, hay que volver a entrar con el PIN.

Lo que la sesión de aparato **no** puede hacer (no tiene contraseña ni MFA): cambiar de compañía (`POST
/auth/switch-tenant` → 403) ni activar o confirmar la verificación en dos pasos de la cuenta (`POST
/auth/mfa/totp/enroll` y `/confirm` → 403 `La sesión de un aparato no administra el segundo factor.`). Quien viera el
PIN en un aparato compartido, o quien lo asignó, se quedaría si no con el secreto TOTP y los códigos de recuperación, y
el dueño ya no podría entrar a la web con su contraseña. El segundo factor se activa desde la web (Mi cuenta).

### Validaciones

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Aparato inválido o desactivado, o sin `deviceSecret` | `El aparato no está registrado o fue desactivado.` | 401 |
| PIN incorrecto, usuario sin PIN, sin membresía activa o administrador de plataforma | `PIN incorrecto.` | 401 |
| Refresh de una sesión de aparato con el aparato desactivado o el módulo WMS_LOTSERIAL apagado | `El aparato no está registrado o fue desactivado.` | 401 |
| PIN bloqueado (ver abajo) | `PIN bloqueado por 15 minutos.` | 423 |
| PIN correcto, pero lo asignó otra persona que ya no cubre los permisos actuales del usuario (ver 3.2) | `Su PIN lo asignó otra persona que ya no tiene sus permisos; defina su propio PIN en Mi cuenta.` | 403 |
| Refresh de una sesión de aparato en ese mismo caso (la sesión queda revocada) | `Su PIN lo asignó otra persona que ya no tiene sus permisos; defina su propio PIN en Mi cuenta.` | 401 |
| Usuario válido y PIN correcto, pero sin `inventory.view` | `Falta el permiso 'inventory.view'.` | 403 |
| `deviceSessionDays` fuera de 1–365 (configuración) | `Entre 1 y 365 días.` | 400 |
| Cambiar de compañía con una sesión de aparato (`POST /auth/switch-tenant`) | `La sesión de un aparato no cambia de compañía.` | 403 |
| Activar o confirmar la verificación en dos pasos con una sesión de aparato (`POST /auth/mfa/totp/enroll` o `/confirm`) | `La sesión de un aparato no administra el segundo factor.` | 403 |
| Refresh de una sesión de aparato cuyo usuario ya no está activo, perdió la membresía activa en la compañía, o ya no tiene `inventory.view` | `Refresh token inválido.` | 401 |

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
   el aparato las borra de su base local. Ejemplos: la tarea COUNT de un conteo eliminado llega `CANCELLED` con
   `isActive: false`; una orden de compra que recibe una segunda entrega parcial (sigue `PARTIAL`) vuelve a llegar con el
   pendiente nuevo.

El parámetro se llama exactamente `since` (UTC). Un parámetro con otro nombre (por ejemplo `modifiedSinceUtc`) se
ignora **sin error**: la respuesta sería una carga completa de lo vigente, que nunca trae las bajas.

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

Desempate (3PL: el mismo código o SKU para varios dueños): gana el producto propio de la compañía; si no hay propio,
entre los de clientes gana el que se dio de alta primero (menor id de producto), no el del cliente con menor id.

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
