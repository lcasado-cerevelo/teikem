# Capítulo 01 — Plataforma y seguridad (Lote 1)

Este capítulo cubre lo que el Lote 1 deja construido: acceso y sesión, doble factor (MFA), mi perfil, usuarios
y roles del tenant, catálogos, estatus, puntos de contacto, campos personalizados, vistas/indicadores/gráficos
(análisis), auditoría y seguridad, módulos por tenant, configuración de la compañía y administración de
plataforma.

Convenciones de esta guía:
- Cada acción indica el **permiso** exacto que exige (formato `recurso.accion`) y, si aplica, el **módulo**
  que debe estar encendido para el tenant.
- Los mensajes de error citados son el texto exacto que devuelve la API (`title` del error). El código HTTP va
  junto al mensaje.
- Toda respuesta de error tiene esta forma (JSON):
  ```json
  { "type": "https://teikem.app/errors/<code>", "title": "<mensaje>", "status": <http>, "code": "<code>", "errors": null, "correlationId": "<guid>" }
  ```
  El `correlationId` también viene en la cabecera de respuesta `X-Correlation-Id`; sirve para buscar la acción
  en auditoría/seguridad.

---

## 1. Acceso y sesión

### 1.1 Inicio de sesión

Qué hace: valida correo y contraseña; si el usuario tiene más de una compañía activa sin una marcada como
default, o si el llamado no indica `tenantId`, puede pedir que se elija compañía; si el tenant exige MFA o el
usuario ya lo tiene activo, pide el segundo factor antes de emitir tokens.

Quién puede: cualquier usuario interno activo (`UserKind = INTERNAL`). Los usuarios de portal no pueden entrar
por aquí.

Cómo se usa: `POST /api/v1/auth/login`
```json
{ "email": "admin@teikem.local", "password": "Teikem_Admin_2026!", "deviceInfo": "smoke" }
```
Respuesta con `status`: `ok` (incluye `tokens`), `mfa_required` (incluye `mfaChallengeToken` como Bearer para
el paso 2), o `tenant_selection` (incluye `tenants` para elegir con `tenantId`).

Validaciones:

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| Usuario no existe o inactivo | no se revela cuál falló | `Credenciales inválidas.` | 401 |
| Cuenta bloqueada (5 intentos fallidos) | lockout de 15 min | `Credenciales inválidas.` | 401 |
| Contraseña incorrecta | — | `Credenciales inválidas.` | 401 |
| Usuario de portal | `UserKind = PORTAL` | `Los usuarios de portal se autentican en el portal de clientes.` | 401 |
| `tenantId` pedido pero sin membresía activa ahí | — | `No pertenece a esa compañía.` | 403 |
| Usuario sin ninguna compañía activa | — | `El usuario no tiene ninguna compañía activa.` | 403 |
| Compañía inactiva | `Tenant.IsActive = false` | `La compañía está inactiva.` | 403 |
| Segundo factor incorrecto (paso `mfa/verify`) | — | `Código MFA inválido.` | 401 |

FAQ:
- **¿Por qué no me dice si el correo o la contraseña estaba mal?** Por seguridad: revelar cuál de los dos
  falló facilita adivinar cuentas válidas. El mensaje siempre es el mismo (`Credenciales inválidas.`).
- **Cada intento fallido queda registrado.** Sí, en el evento de seguridad `LOGIN` (fallo) o `LOCKOUT`
  (bloqueo), visible en `/api/v1/audit/security-events` para quien tenga `admin.audit`.

### 1.2 Verificación MFA (paso 2 del login)

Qué hace: completa el login cuando el paso 1 devolvió `mfa_required`, con el código TOTP o un código de
recuperación de un solo uso.

Quién puede: el usuario que recibió el `mfaChallengeToken` (se envía como `Authorization: Bearer`).

Cómo se usa: `POST /api/v1/auth/mfa/verify` con `{ "code": "123456", "deviceInfo": "..." }`.

Validaciones: código vacío o incorrecto → `Código MFA inválido.` (401).

### 1.3 Refresh de sesión, cambio de compañía y cierre de sesión

- Refrescar tokens: `POST /api/v1/auth/refresh` `{ "refreshToken": "..." }`. El refresh token es de un solo
  uso: cada llamada lo rota (revoca el anterior y entrega uno nuevo). Si se reutiliza uno ya usado, se revoca
  toda la cadena de sesión (protección contra robo de token).
- Cambiar de compañía activa sin volver a poner contraseña: `POST /api/v1/auth/switch-tenant`
  `{ "refreshToken": "...", "tenantId": 2 }`. Recarga menú, permisos y datos con el nuevo tenant.
- Cerrar esta sesión: `POST /api/v1/auth/logout` `{ "refreshToken": "..." }` (204, no requiere estar
  autenticado porque el propio refresh token identifica la sesión a cerrar).
- Cerrar todas las sesiones propias: `POST /api/v1/auth/logout-all` (autenticado). Invalida también los
  access tokens ya emitidos (rota el `SecurityStamp`).

Validaciones:

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Refresh vacío | `Refresh token inválido.` | 401 |
| Refresh no existe / ya usado / revocado | `Refresh token inválido.` | 401 |
| Refresh expirado | `Sesión expirada.` | 401 |
| Membresía ya no activa en el tenant del refresh | `La membresía ya no está activa.` | 403 |
| `switch-tenant` a un tenant sin membresía | `No pertenece a esa compañía.` | 403 |

### 1.4 Reautenticación reciente (AAL2 / step-up)

Qué hace: algunas acciones sensibles (cambiar permisos de un rol, encender/apagar módulos, aprovisionar un
tenant, etc.) exigen haberse reautenticado hace poco. Este endpoint confirma la contraseña (y el MFA si el
usuario lo tiene) y marca la sesión como "verificada AAL2" por la ventana configurada del tenant
(`Aal2WindowMinutes`, entre 5 y 240 minutos, 30 por defecto).

Cómo se usa: `POST /api/v1/auth/reauth` `{ "password": "...", "mfaCode": "123456" }` (autenticado). Devuelve
un access token nuevo con el sello AAL2.

Validaciones: contraseña incorrecta → `Contraseña incorrecta.` (401); con MFA activo y código omitido o
incorrecto → `Código MFA inválido.` (401).

Si se intenta una acción marcada `[RequireAal2]` sin haber hecho este paso (o si ya venció la ventana), la API
responde **403** con `Esta acción requiere reautenticación reciente (AAL2).` (`code: aal2_required`). El
cliente debe llamar a `reauth` y reintentar con el token nuevo.

### 1.5 Sesiones activas y su revocación

Qué hace: lista los dispositivos/sesiones abiertas (refresh tokens vivos) del usuario y permite cerrar una en
particular (por ejemplo, un dispositivo perdido).

Cómo se usa:
- `GET /api/v1/auth/sessions` (autenticado): cada fila trae `deviceInfo`, `issuedAtUtc`, `expiresAtUtc`,
  `aal2VerifiedAtUtc` e `isCurrent`.
- `DELETE /api/v1/auth/sessions/{id}` (autenticado): revoca esa sesión.

Validaciones: revocar la sesión actual desde esta lista → `La sesión actual no se revoca desde la lista; use logout.`
(409). Sesión inexistente o de otro usuario → `Sesión '<id>' no encontrado.` (404).

Un administrador puede además cerrar **todas** las sesiones de otro usuario: `DELETE /api/v1/users/{id}/sessions`
con `admin.users`.

### 1.6 Cambio de contraseña

Qué hace: cambia la contraseña propia, verificando la actual.

Cómo se usa: `PUT /api/v1/auth/password` `{ "currentPassword": "...", "newPassword": "..." }` (autenticado).

Reglas de contraseña (no hay reglas de composición, siguiendo NIST 800-63B):

| Regla | Valor |
|---|---|
| Longitud mínima | 12 caracteres |
| Caracteres distintos mínimos | 4 |
| Mayúsculas/minúsculas/dígitos/símbolos obligatorios | No se exige ninguno |
| Intentos fallidos antes de bloqueo | 5 |
| Duración del bloqueo | 15 minutos |

Validaciones:

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Contraseña actual incorrecta | `Contraseña incorrecta.` | 401 |
| Nueva contraseña muy corta / con pocos caracteres distintos | mensaje de Identity (ej. `Passwords must be at least 12 characters.`) | 400 |
| Nueva contraseña en una brecha conocida | `Esta contraseña aparece en brechas conocidas; elija otra.` | 400 |

Nota: la verificación contra brechas conocidas (HIBP) está implementada como una interfaz (`IPasswordBreachChecker`)
cuya versión actual **no hace nada** (siempre dice que no está comprometida) porque el entorno no tiene salida
a Internet; el mensaje de arriba existe en el código pero hoy no se dispara en la práctica.

### 1.7 MFA por aplicación (TOTP): activar, confirmar, códigos de recuperación, desactivar

Qué hace: segundo factor por app autenticadora (Google Authenticator, Authy, etc.), con códigos de recuperación
de un solo uso por si se pierde el dispositivo.

Cómo se usa:
1. `POST /api/v1/auth/mfa/totp/enroll` (autenticado, o con el `mfaChallengeToken` si el tenant exige MFA y el
   usuario aún no lo tiene) → devuelve `secret` y `otpAuthUri` para escanear en la app.
2. `POST /api/v1/auth/mfa/totp/confirm` `{ "code": "123456" }` con el código que muestra la app → devuelve
   `recoveryCodes` (mostrarlos una sola vez; no se pueden volver a ver).
3. Desde el siguiente login, el usuario recibe `mfa_required` y debe pasar por `POST /api/v1/auth/mfa/verify`.
4. Desactivar: `DELETE /api/v1/auth/mfa/totp` (autenticado, **requiere AAL2**).

Validaciones:

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| TOTP ya confirmado y se intenta enrolar de nuevo | `TOTP ya está enrolado y confirmado; desactívelo primero (requiere AAL2).` | 409 |
| Confirmar sin haber enrolado antes | `Primero inicie el enrolamiento TOTP.` | 409 |
| Código de confirmación incorrecto | `Código inválido.` | 400 |
| Desactivar sin AAL2 reciente | `Esta acción requiere reautenticación reciente (AAL2).` | 403 |

FAQ:
- **Perdí mi teléfono con la app de MFA. ¿Qué hago?** Usa uno de los códigos de recuperación que se mostraron
  al confirmar el MFA (cada uno sirve una sola vez) en `POST /auth/mfa/verify`. Si también los perdiste, pide
  a un administrador de tu compañía (permiso `admin.users`) que revise tu cuenta; el Lote 1 no tiene un flujo
  de "restablecer MFA" propio: hoy la salida es desactivar el TOTP desde una sesión ya autenticada con AAL2
  vigente, o que soporte investigue caso por caso.
- **¿Hay SMS o biometría?** No en este lote; solo TOTP (app autenticadora) y códigos de recuperación.

---

## 2. Mi perfil (`/me`) y permisos efectivos

Qué hace: devuelve, para la sesión actual, el tenant activo, las compañías a las que pertenece (con estatus y
cuál es la default), los **permisos efectivos** (los del rol o roles asignados más los permisos extra
concedidos directamente al usuario), los **módulos encendidos** (con eso se arma el menú), los ámbitos de
datos (`UserDataScope`) y si tiene MFA activo.

Quién puede: cualquier usuario autenticado, sobre sí mismo.

Cómo se usa: `GET /api/v1/me`.

FAQ:
- **No veo una opción de menú que debería tener.** Puede ser por permiso (revisa `permissions` en `/me`) o
  porque el módulo del que depende esa pantalla está apagado para tu compañía (revisa `enabledModules`).

---

## 3. Roles y usuarios del tenant

### 3.1 Catálogo de permisos (solo lectura)

`GET /api/v1/permissions` (autenticado): lista los permisos disponibles en la plataforma (código, categoría,
etiqueta). El catálogo se define en código (`PermissionCatalog`) y no lo edita el tenant.

### 3.2 Roles: listar, crear, editar, eliminar

Qué hace: un rol agrupa permisos; asignar el rol a un usuario le da todos esos permisos. Hay 6 plantillas de
sistema clonables (`TenantAdmin`, `Dispatcher`, `Billing`, `WarehouseOperator`, `Driver`, `ReadOnly`) que se
copian al aprovisionar un tenant.

Quién puede: ver roles, cualquier autenticado (`GET /api/v1/roles?includeTemplates=`); crear, `admin.roles`;
editar/eliminar, `admin.roles` **+ AAL2** (cambiar los permisos de un rol afecta a todos los que lo tengan).

Cómo se usa:
- `POST /api/v1/roles` `{ "name": "Supervisor", "permissions": ["orders.view","orders.edit"] }`
- `PUT /api/v1/roles/{id}` (mismo cuerpo)
- `DELETE /api/v1/roles/{id}`

Validaciones:

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Nombre vacío | `El nombre es obligatorio.` | 400 |
| Nombre repetido en el tenant | `Ya existe el rol '<name>'.` | 409 |
| Permiso desconocido en la lista | `Permisos desconocidos: <lista>.` | 400 |
| Eliminar un rol con usuarios asignados | `El rol tiene usuarios asignados; reasígnelos antes de eliminarlo.` | 409 |
| Sin `admin.roles` | `Falta el permiso 'admin.roles'.` | 403 |

### 3.3 Usuarios: listar, crear/invitar, editar, roles, permisos extra, membresía, ámbitos de datos, sesiones

Qué hace: alta y administración de usuarios internos del tenant. Al crear, si no se envía contraseña, la API
genera una temporal y la devuelve **una sola vez** en la respuesta (para que soporte/el admin la comparta por
un canal seguro; no se puede volver a consultar).

Quién puede: todo este bloque exige `admin.users`; cambiar roles y permisos extra exige además **AAL2**.

Cómo se usa:
- `GET /api/v1/users`, `GET /api/v1/users/{id}`
- `POST /api/v1/users` `{ "email": "...", "fullName": "...", "roles": ["Dispatcher"] }` → respuesta incluye
  `temporaryPassword` si no se envió `password`.
- `PUT /api/v1/users/{id}` `{ "fullName": "...", "isActive": true }`
- `PUT /api/v1/users/{id}/roles` `{ "roles": ["Dispatcher","Billing"] }` (AAL2)
- `PUT /api/v1/users/{id}/permissions` `{ "permissions": ["analytics.dates"] }` — permiso puntual al usuario,
  además de lo que le da el rol (AAL2)
- `PUT /api/v1/users/{id}/membership` `{ "status": "SUSPENDED" }` — estatus de la membresía en el tenant
- `GET /PUT /api/v1/users/{id}/data-scopes` — ámbitos de datos adicionales (ver 3.4)
- `DELETE /api/v1/users/{id}/sessions` — cierra todas las sesiones de ese usuario

Validaciones:

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Correo vacío | `El correo es obligatorio.` | 400 |
| Alta de usuario de portal | `Los usuarios de portal se administran desde el expediente del cliente (Lote 2).` | 400 |
| Contraseña inválida (Identity) | mensaje de Identity, ej. `Passwords must be at least 12 characters.` | 400 |
| Usuario ya pertenece al tenant | `El usuario ya pertenece a esta compañía.` | 409 |
| Desactivarse a sí mismo | `No puede desactivarse a sí mismo.` | 409 |
| Roles desconocidos | `Roles desconocidos: <lista>.` | 400 |
| Permisos desconocidos | `Permisos desconocidos: <lista>.` | 400 |
| Cambiar la propia membresía | `No puede cambiar su propia membresía.` | 409 |
| Usuario no existe | `Usuario '<id>' no encontrado.` | 404 |

Dos mecánicas distintas para dar permisos: **por rol** (afecta a todos los que tengan ese rol) vs. **permiso
extra por usuario** (excepción puntual, solo para esa persona). Revisar siempre ambos al auditar accesos.

### 3.4 Membresía y sus estatus

Cada usuario tiene, por tenant, una membresía con estatus `ACTIVE`, `SUSPENDED` o `INVITED`. Solo con
`ACTIVE` puede operar en ese tenant. El cambio se hace con `PUT /api/v1/users/{id}/membership` y no pasa por
el motor genérico de transiciones de estatus (Capítulo 5): es un campo simple, no tiene historial de
`EntityStatusHistory` ni valida pipeline.

### 3.5 Ámbitos de datos (`UserDataScope`)

Qué hace: restringe a un usuario a ver solo ciertos almacenes/clientes/rutas (según el `ScopeEntity`). Se
guarda y se expone en `/me`, pero **el filtro real todavía no se aplica** porque las entidades de negocio
(almacenes, clientes) llegan en lotes posteriores (2 y 6). Por ahora es solo dato guardado, sin efecto en
consultas.

---

## 4. Catálogos (LookupCode / CatalogDomain)

Qué hace: valores de clasificación reutilizables (tipos de servicio, tipos de contacto, etc.), agrupados en
"dominios". Cada valor tiene etiqueta en español/inglés (`labels`), puede estar deshabilitado y puede tener
override por tenant (cambiar etiqueta, color extra, orden o habilitación sin tocar el catálogo global). Un
tenant también puede crear **sus propias listas** (por ejemplo, para usarlas en un campo personalizado tipo
lista).

Quién puede: ver, cualquier autenticado; crear/editar/override/desactivar/listas propias, `admin.catalogs`.

Cómo se usa:
- `GET /api/v1/catalogs/domains` — dominios disponibles (`?scope=` filtra sistema/tenant).
- `GET /api/v1/catalogs/{entity}` — valores resueltos para el tenant (cabecera `X-Lang: en` cambia idioma).
- `PUT /api/v1/catalogs/{entity}/{code}/override` `{ "labels": {"es": "Exprés 24h"} }`
- `DELETE /api/v1/catalogs/{entity}/{code}/override` — quita el override
- `POST /api/v1/catalogs/{entity}` — nuevo valor en un dominio (no de sistema, o siendo administrador de
  plataforma)
- `PUT /api/v1/catalogs/{entity}/{code}` — editar valor
- `DELETE /api/v1/catalogs/{entity}/{code}` — desactivar (no borra; sigue en el historial, deja de ofrecerse)
- `POST /api/v1/catalogs/{entity}/{code}/restore` — reactivar
- `POST /api/v1/catalogs/lists` `{ "name": "Zona de entrega", "values": [{"code":"NORTE","labels":{"es":"Norte","en":"North"}}] }`
- `DELETE /api/v1/catalogs/lists/{domainKey}`

Validaciones:

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Dominio inexistente | `Dominio de catálogo '<domainKey>' no encontrado.` | 404 |
| Valor inexistente | `Valor <entity> '<code>' no encontrado.` | 404 |
| Código repetido en el dominio | `Ya existe el valor '<code>' en <entity>.` | 409 |
| Etiqueta vacía | `La etiqueta es obligatoria.` | 400 |
| Desactivar un valor de sistema globalmente | `Un valor de sistema no se desactiva globalmente; use el override del tenant (IsEnabled=false).` | 409 |
| Nombre de lista vacío | `El nombre es obligatorio.` | 400 |
| Nombre de lista inválido (no produce clave) | `Nombre inválido.` | 400 |
| Clave de lista repetida | `Ya existe una lista con la clave '<key>'.` | 409 |
| Eliminar lista de sistema | `Las listas de sistema no se eliminan.` | 409 |
| Eliminar lista en uso por un campo personalizado | `La lista está en uso por un campo personalizado; desactívela en vez de eliminarla.` | 409 |
| Editar catálogo global sin ser admin. de plataforma | `Los catálogos globales solo los edita el administrador de plataforma; use overrides o cree una lista propia.` | 403 |
| Código de valor vacío | `El código es obligatorio.` | 400 |
| Código de valor inválido (>40 o no alfanumérico) | `Código inválido (máx. 40 caracteres alfanuméricos).` | 400 |

FAQ:
- **Cambié la etiqueta de un valor y no veo el cambio.** Revisa que estés usando el idioma esperado
  (`X-Lang`); el override solo aplica en tu tenant, no cambia el catálogo global.

---

## 5. Estatus y transiciones (StatusCode)

Qué hace: cada entidad de negocio con ciclo de vida (por ejemplo, una orden) avanza por un **pipeline** de
etapas (`PIPELINE`), puede tener salidas **laterales** o **terminales** (`LATERAL`/`TERMINAL`, ej. cancelada) y
cada compañía puede habilitar/deshabilitar etapas, cambiar su color/orden/etiqueta, y definir qué acciones
(`StatusCapability`) están permitidas en cada estatus. Todo cambio de estatus real (cuando existan las
entidades de negocio en próximos lotes) se hace únicamente vía `StatusService.TransitionAsync`, que valida el
pipeline y escribe historial.

Quién puede: ver pipeline/capacidades/laterales/historial, cualquier autenticado; configurar (overrides,
capacidades, entradas laterales), `admin.statusconfig`.

Cómo se usa:
- `GET /api/v1/status/{entity}` — etapas resueltas para el tenant (ej. `entity=OrderStatus`).
- `GET /api/v1/status/{entity}/validate` — valida el pipeline actual del tenant.
- `PUT /api/v1/status/{entity}/{code}/override` `{ "isEnabled": false }`
- `GET /PUT /api/v1/status/capabilities/{entityType}?statusDomain=OrderStatus`
  `[{"statusCode":"IN_TRANSIT","capability":"EDIT_CARGO","isAllowed":false}]`
- `GET /PUT /api/v1/status/lateral-entries/{entityType}?statusDomain=OrderStatus`
  `[{"lateralStatusCode":"CANCELLED","fromStatusCode":"PICKUP","isAllowed":true}]`
- `GET /api/v1/status/history/{entityType}/{entityId}` — historial de cambios de estatus de un registro.

Reglas de transición (`StatusService.TransitionAsync`, aplican cuando un lote de negocio las invoque):
- Un registro nuevo debe nacer en una etapa **inicial** del pipeline.
- Desde una etapa de pipeline solo se avanza a la siguiente habilitada por orden (`SortOrder`), salvo que el
  destino sea lateral/terminal permitido por `StatusLateralEntry` (regla del tenant pisa la default; sin
  reglas definidas, se permite).
- Un estatus **terminal** no admite más transiciones.
- Desde un estatus **lateral** solo se puede volver a la etapa desde la que se desvió, o avanzar a la
  siguiente etapa de esa.
- Antes de ejecutar una acción del negocio se valida `StatusCapability`: si no hay una configurada para esa
  combinación estatus×acción, se permite por defecto; si hay una y dice `isAllowed=false`, se bloquea.

Validaciones:

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Dominio de estatus inexistente | `Dominio de estatus '<entity>' no encontrado.` | 404 |
| Estatus inexistente | `Estatus <entity> '<code>' no encontrado.` | 404 |
| Override deja el pipeline sin etapa inicial habilitada | `Pipeline inválido: <detalle>` | 422 |
| Acción bloqueada por `StatusCapability` | `El estatus actual no permite la acción '<capability>'.` | 422 |
| Entrada lateral hacia una etapa que es de pipeline | `'<code>' es una etapa del pipeline, no un lateral/terminal.` | 400 |
| Pipeline sin etapa inicial al transicionar | `El pipeline '<statusDomain>' no tiene etapa inicial habilitada.` | 422 |
| Estatus destino no existe o deshabilitado en el tenant | `El estatus '<code>' no existe o no está habilitado para esta compañía.` | 422 |
| Alta que no empieza en etapa inicial | `Un registro nuevo debe empezar en una etapa inicial; '<code>' no lo es.` | 422 |
| Transición al mismo estatus | `El registro ya está en ese estatus.` | 422 |
| Transición desde un terminal | `'<code>' es terminal: no admite más transiciones.` | 422 |
| Salto de pipeline fuera de orden sin regla lateral | `Salto ilegal: de '<from>' solo se puede avanzar a '<next>'.` | 422 |
| Transición no permitida por el modelo del tenant | `No se permite pasar a '<to>' desde '<from>' según el modelo de esta compañía.` | 422 |
| Desde un lateral, destino no permitido | `Desde el lateral '<from>' solo se puede regresar a '<last>' o avanzar a '<next>'.` | 422 |

FAQ:
- **Deshabilité una etapa y ahora nada avanza.** Revisa `GET /api/v1/status/{entity}/validate`: si el pipeline
  se quedó sin etapa inicial habilitada, la propia API rechazó el cambio con 422 antes de guardarlo (no debería
  quedar en ese estado).

---

## 6. Puntos de contacto (ContactPoint)

Qué hace: guarda varios contactos (teléfono, correo, etc.) para cualquier entidad del sistema que los necesite
(hoy: usuarios; cada módulo de negocio registra las suyas). La pertenencia (que el `ownerId` exista y sea del
tenant activo) se valida contra un "resolver" por tipo de entidad, no por llave foránea directa.

Quién puede: ver, cualquier autenticado; crear/editar/desactivar, `contacts.manage`.

Cómo se usa:
- `GET /api/v1/contacts/{ownerEntity}/{ownerId}` (ej. `ownerEntity=USER`)
- `POST /api/v1/contacts/{ownerEntity}/{ownerId}` `{ "contactType": "EMAIL", "value": "a@b.com", "isPrimary": true }`
- `PUT /api/v1/contacts/{id}`
- `DELETE /api/v1/contacts/{id}` (desactiva, no borra)

Validaciones:

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `ownerEntity` sin resolver registrado | módulo dueño aún no construido | `No hay resolver de pertenencia para '<ownerEntity>' (el módulo dueño aún no está construido).` | 400 |
| `ownerId` no existe en el tenant | — | `<ownerEntity> '<ownerId>' no encontrado.` | 404 |
| Valor vacío | — | `El valor es obligatorio.` | 400 |
| Correo mal formado (tipo EMAIL) | debe tener `@` y `.` válidos | `Correo inválido.` | 400 |
| Teléfono mal formado (tipo PHONE) | `^\+?[0-9][0-9\s\-().]{6,24}$` | `Teléfono inválido.` | 400 |
| Contacto inexistente | — | `Contacto '<id>' no encontrado.` | 404 |

---

## 7. Campos personalizados (Custom Fields)

Requiere el módulo **CUSTOM_FIELDS** encendido (`[RequireModule]`); ese módulo a su vez depende de **ANALYTICS**.

Qué hace: permite agregar campos propios a una entidad (texto, número, fecha, fecha y hora, sí/no, lista de
selección simple/múltiple, o referencia a una lista de catálogo) sin necesidad de desarrollo. Cada campo puede
ser obligatorio, único, tener valor por defecto, reglas de validación (JSON con `regex`, `min`, `max`,
`minLength`, `maxLength`) y mostrarse en listados (`showInList`).

Quién puede: ver definiciones y valores, cualquier autenticado; crear/editar/desactivar/reactivar definiciones,
`admin.customfields`; poner valores en un registro, cualquier autenticado (el permiso real de "quién puede
editar ese registro" lo controla el módulo dueño de la entidad).

Cómo se usa:
- `GET /api/v1/custom-fields/definitions?entityType=USER`
- `POST /api/v1/custom-fields/definitions/{entityType}`
  ```json
  { "fieldKey":"cost_center","labels":{"es":"Centro de costo","en":"Cost center"},"dataType":"TEXT",
    "isRequired":false,"isUnique":true,"showInList":true,"validationJson":"{\"regex\":\"^CC-\\\\d{3}$\"}" }
  ```
- `PUT /api/v1/custom-fields/definitions/{id}`
- `DELETE /api/v1/custom-fields/definitions/{id}` (desactiva) / `POST .../restore`
- `GET /api/v1/custom-fields/values/{entityType}/{entityId}`
- `PUT /api/v1/custom-fields/values/{entityType}/{entityId}` `{ "values": { "cost_center": "CC-100" } }`

Tipos de dato (`dataType`): `TEXT`, `NUMBER`, `DATE`, `DATETIME`, `BOOL`, `SELECT`, `MULTISELECT`, `LOOKUP_REF`.

Validaciones:

| Campo / caso | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `fieldKey` con formato inválido | minúsculas, dígitos, `_`, 2-60, empieza con letra | `Clave inválida: minúsculas, dígitos y '_' (2-60), empezando por letra.` | 400 |
| `fieldKey` repetido en la entidad | — | `Ya existe el campo '<key>' en <entityType>.` | 409 |
| Cambiar el tipo de un campo con valores guardados | — | `No se puede cambiar el tipo de un campo que ya tiene valores guardados.` | 409 |
| Etiqueta vacía | — | `La etiqueta es obligatoria.` | 400 |
| `LOOKUP_REF` sin `refEntity` | — | `LOOKUP_REF requiere el dominio de catálogo referenciado.` | 400 |
| `refEntity` no existe | — | `La lista '<refEntity>' no existe.` | 400 |
| `SELECT`/`MULTISELECT` sin opciones ni `refEntity` | — | `Un campo de selección necesita opciones o una lista del catálogo (refEntity).` | 400 |
| `validationJson` no es JSON válido | — | `ValidationJson no es JSON válido.` | 400 |
| Definición inexistente | — | `Campo personalizado '<id>' no encontrado.` | 404 |
| Registro dueño no existe en el tenant | — | `<entityType> '<entityId>' no encontrado.` | 404 |
| Valor NUMBER no numérico | — | `Se esperaba un número.` (por campo) | 400 |
| Valor BOOL no booleano | — | `Se esperaba sí/no.` (por campo) | 400 |
| Valor DATE con formato erróneo | — | `Se esperaba una fecha (yyyy-MM-dd).` (por campo) | 400 |
| Valor DATETIME con formato erróneo | — | `Se esperaba una fecha y hora ISO.` (por campo) | 400 |
| Valor MULTISELECT que no es lista | — | `Se esperaba una lista de valores.` (por campo) | 400 |
| Regla de validación incumplida (regex/min/max/etc.), requerido faltante, duplicado en campo único | — | mensaje generado por campo, agrupado en `errors` | 400 |

FAQ:
- **Guardé un valor y me devolvió 400 con varios campos.** El cuerpo `errors` trae, por cada `fieldKey` que
  falló, el mensaje puntual (regla de validación, requerido, o duplicado si el campo es único).

---

## 8. Vistas, indicadores, gráficos y Pulso del día (Análisis)

Requiere el módulo **ANALYTICS**. Todo este bloque exige además el permiso base `analytics.view` (ya que el
controlador lo declara a nivel de clase); crear o editar exige `analytics.manage`.

### 8.1 Cómo funciona el motor

Las fuentes de datos (`IDataSource`) se registran por módulo con una clave de `EntityType` (en el Lote 1:
`AUDIT_LOG`, `SECURITY_EVENT`, `USER`). Cada fuente puede declarar un `DateField` (para poder filtrar por
rango de fechas) y relaciones hacia otras fuentes ("secundarias", para combinar muchos-a-uno, ej. traer
`User.FullName` en una vista de `AUDIT_LOG`). Los filtros usan un **DSL propio** (no SQL) con operadores
`eq, ne, gt, gte, lt, lte, contains, startsWith, endsWith, in, notIn, between, isNull, notNull, isTrue, isFalse`
combinables con `and/or/not`. El motor evalúa todo en memoria, sobre un tope de 20.000 filas por fuente.

`GET /api/v1/analytics/data-sources` lista las fuentes disponibles con sus campos, relaciones y campos
personalizados.

### 8.2 Vistas (reportes)

Qué hace: una tabla configurable (columnas, agrupación, orden, filtros, combinación con otra fuente) que se
puede guardar, compartir y ejecutar.

Cómo se usa:
- `GET /api/v1/analytics/reports?baseEntityType=AUDIT_LOG`, `GET .../reports/{id}`
- `POST /api/v1/analytics/reports/{baseEntityType}` (`analytics.manage`)
  ```json
  { "name": "Actividad diaria", "columns": ["CreatedAtUtc","Action","User.FullName"], "secondary": ["User"] }
  ```
- `PUT /api/v1/analytics/reports/{id}` / `DELETE /api/v1/analytics/reports/{id}` (`analytics.manage`)
- `POST /api/v1/analytics/reports/{id}/run` — ejecuta la vista guardada
- `POST /api/v1/analytics/reports/{baseEntityType}/preview` — vista previa sin guardar, útil en el constructor

Validaciones:

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Rango de fecha con modo inválido | `Modo de rango inválido.` | 400 |
| Modo `CUSTOM` sin ambas fechas | `El rango personalizado necesita Desde y Hasta.` | 400 |
| `dateFrom` > `dateTo` | `Desde no puede ser mayor que Hasta.` | 400 |
| Fuente base no registrada | `La fuente '<key>' no está registrada como EntityType.` | 400 |
| Nombre vacío | `El nombre es obligatorio.` | 400 |
| Nombre de vista repetido en la fuente | `Ya existe una vista '<name>' para <source>.` | 409 |
| Sin columnas ni agrupación | `Una vista necesita al menos una columna o una agrupación.` | 400 |
| Fuente secundaria no combinable | `La fuente <source> no se combina con '<sec>'.` | 400 |
| `filterJson` no es JSON válido | `FilterJson no es JSON válido.` | 400 |
| Vista inexistente o sin visibilidad para el usuario | `Vista '<id>' no encontrado.` | 404 |
| Editar/eliminar un elemento de sistema (`IsSystem`) | `Los elementos por default de la plataforma no se editan ni se eliminan.` | 403 |
| Editar/eliminar sin ser dueño ni comparta con edición | `Solo el dueño puede editar o eliminar este elemento.` | 403 |
| Compartir con usuario/rol que no es del tenant | `El usuario <id> no pertenece a esta compañía.` / `El rol <id> no pertenece a esta compañía.` | 400 |

### 8.3 Indicadores

Qué hace: un número agregado (conteo, suma, promedio, etc.) sobre una fuente, con su propio rango de fecha.

Cómo se usa: `GET /api/v1/analytics/indicators`, `GET .../{id}`, `GET .../{id}/value`,
`POST /api/v1/analytics/indicators` (`analytics.manage`), `PUT`/`DELETE` igual.

Rango de fecha y Pulso, dos niveles:
- **Rango/Pulso por defecto de la definición** (afecta a todos): `PUT .../{id}/default-date-range`. El dueño
  puede cambiarlo libremente; si es ajeno o de sistema, exige el permiso `analytics.dates`.
- **Preferencia personal** (no toca la definición): `PUT .../{id}/my-date-range` y `PUT .../{id}/my-pulse` —
  cualquier usuario puede fijar su propio rango o si lo quiere ver en su Pulso, sin permiso especial.

Validaciones adicionales: nombre vacío → `El nombre es obligatorio.` (400); nombre repetido →
`Ya existe el indicador '<name>'.` (409); cambiar el rango por defecto sin ser dueño/sistema y sin el permiso →
`Cambiar el rango por defecto de un indicador ajeno o de sistema requiere 'analytics.dates'.` (403); indicador
inexistente/sin visibilidad → `Indicador '<id>' no encontrado.` (404).

### 8.4 Gráficos

Qué hace: igual que un indicador pero agrupado, mostrado como barra/dona (top 8 categorías) o línea (serie
cronológica, últimos 30 puntos).

Cómo se usa: mismos verbos que indicadores en `/api/v1/analytics/charts`, más `GET .../{id}/data` para los
puntos a graficar.

Validaciones: `chartType` debe ser `BAR`, `DONUT` o `LINE` (`Tipo de gráfico: BAR, DONUT o LINE.`, 400);
`groupByField` obligatorio (`El campo de agrupación es obligatorio.`, 400); función de agregación distinta de
`COUNT` sin campo (`La función <fn> requiere un campo.`, 400); campo que no existe en la fuente
(`La fuente <source> no tiene el campo '<field>'.`, 400); nombre vacío/repetido, igual que indicadores pero con
`gráfico` (`Ya existe el gráfico '<name>'.`, 409).

### 8.5 Pulso del día

Qué hace: `GET /api/v1/analytics/pulse` trae los indicadores y gráficos que el usuario marcó para ver a diario
(o que vienen así por default) y que puede ver según su visibilidad (TENANT por defecto, PRIVATE o compartido
explícitamente).

---

## 9. Auditoría y seguridad

Todo este bloque exige el permiso `admin.audit`.

Qué hace: registra automáticamente los cambios en entidades marcadas `[AuditEntity]` (bitácora de cambios,
`AuditLog`), los eventos de acceso/seguridad (`SecurityEvent`: login, lockout, MFA, permiso denegado, etc.), y
ofrece una vista de "actividad" que combina ambos con búsqueda de texto, además de exportarla a CSV.

Cómo se usa:
- `GET /api/v1/audit/changes?entityType=&entityId=&userId=&from=&to=&skip=&take=`
- `GET /api/v1/audit/security-events?eventType=&userId=&from=&to=&skip=&take=`
- `GET /api/v1/audit/activity?kind=all|changes|security&text=&from=&to=&skip=&take=`
- `GET /api/v1/audit/activity/export.csv` (mismos filtros) — descarga `actividad-YYYYMMDD-HHmm.csv`

Cada fila de cambio trae `correlationId`: el mismo valor que trajo la cabecera `X-Correlation-Id` de la
llamada que produjo el cambio, útil para reconstruir "qué pasó en esa petición".

Entidades que **no** generan bitácora de cambios por diseño (no tienen `[AuditEntity]`): `RefreshToken`,
`MfaRecoveryCode`, `AuditLog`, `SecurityEvent`, `EntityStatusHistory`, `CustomFieldValue`,
`UserAnalyticsPreference`.

FAQ:
- **Un usuario intentó algo sin permiso, ¿queda registro?** Sí: cada intento fallido por falta de permiso
  genera un evento `PERMISSION_DENIED` (visible con `admin.audit` en `/audit/security-events` o `/audit/activity`).

---

## 10. Módulos por tenant

Qué hace: cada compañía enciende solo los módulos de negocio que usa (por ejemplo, Advance usa última milla,
COD, almacén con lote/serie y equipos en alquiler, pero no cross-dock ni marítimo). El menú y los endpoints
protegidos con `[RequireModule]` solo funcionan si el módulo está encendido.

Catálogo de 13 módulos (`ModuleKeys`): `LTL_GROUND` (núcleo), `COD`, `WMS_LOTSERIAL`, `CROSSDOCK`,
`RENTAL_EQUIPMENT`, `RENTAL_BILLING` (depende de `RENTAL_EQUIPMENT`), `MARITIME`, `CLIENT_PORTAL`,
`CUSTOM_FIELDS` (depende de `ANALYTICS`), `PURCHASING`, `CATALOG`, `ANALYTICS`, `SYSTEM` (núcleo). Los módulos
**núcleo** (`LTL_GROUND`, `SYSTEM`) no se pueden apagar.

Quién puede: ver el catálogo, cualquier autenticado; encender/apagar, `admin.tenant` **+ AAL2**.

Cómo se usa:
- `GET /api/v1/modules`
- `PUT /api/v1/modules/{moduleKey}` `{ "isEnabled": true }`

Reglas: encender exige que la dependencia (si tiene) ya esté encendida; apagar un módulo apaga en cascada a
los que dependen de él; un módulo sin fila para el tenant se considera apagado (default seguro).

Validaciones:

| Caso | Mensaje exacto | HTTP |
|---|---|---|
| Módulo desconocido | `Módulo '<moduleKey>' no encontrado.` | 404 |
| Encender sin la dependencia encendida | `'<módulo>' depende de '<dependencia>', que debe encenderse primero.` | 409 |
| Apagar un módulo núcleo | `'<módulo>' es un módulo núcleo y no se puede apagar.` | 409 |
| Endpoint de un módulo apagado | `El módulo '<moduleKey>' no está habilitado para esta compañía.` | 403 |
| Sin AAL2 reciente | `Esta acción requiere reautenticación reciente (AAL2).` | 403 |

FAQ:
- **No veo la pantalla de Campos personalizados / Análisis.** Revisa que el módulo `ANALYTICS` (y si aplica,
  `CUSTOM_FIELDS`) esté encendido para tu compañía en `GET /api/v1/modules`, y que tu usuario tenga
  `analytics.view`.

---

## 11. Configuración de la compañía (Tenant)

Qué hace: ajustes generales del tenant: nombre/razón social/identificación fiscal, idioma por defecto, máscara
de días laborales, tope de paradas por ruta por defecto, tipos de servicio/paquete por defecto, si el MFA es
obligatorio, ventana de AAL2, días de duración de sesión, marca (branding) y feriados/calendario laboral.

Quién puede: ver, cualquier autenticado; editar, `admin.tenant`.

Cómo se usa:
- `GET /api/v1/tenant/settings`
- `PUT /api/v1/tenant/settings` (campos parciales, ej. `{ "aal2WindowMinutes": 60 }`)
- `GET /api/v1/tenant/holidays?year=2026`
- `POST /api/v1/tenant/holidays` `{ "date": "2026-01-01", "name": "Año Nuevo", "isRecurring": true }`
- `DELETE /api/v1/tenant/holidays/{id}`
- `GET /api/v1/tenant/work-days?n=7` — últimos N días hábiles (sin fines de semana ni feriados), para
  tendencias/SLA

Validaciones:

| Campo | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| `defaultLangCode` | 2 letras | `Código de idioma de 2 letras.` | 400 |
| `workDaysMask` | 1-127 | `Máscara inválida (1-127).` | 400 |
| `maxStopsPerRouteDefault` | ≥ 1 | `Debe ser ≥ 1.` | 400 |
| `aal2WindowMinutes` | 5-240 | `Entre 5 y 240 minutos.` | 400 |
| `sessionDays` | 1-365 | `Entre 1 y 365 días.` | 400 |
| `brandingJson` | JSON válido | `BrandingJson no es JSON válido.` | 400 |
| `brandingJson` | ≤ 200.000 caracteres | `BrandingJson demasiado grande (los logos van a blob storage).` | 400 |
| Nombre de feriado vacío | — | `El nombre es obligatorio.` | 400 |
| Feriado inexistente | — | `Feriado '<id>' no encontrado.` | 404 |

---

## 12. Administración de plataforma (solo administradores de Teikem)

Qué hace: un usuario marcado `IsPlatformAdmin` opera cualquier tenant sin necesidad de membresía, tiene todos
los permisos en cualquier compañía, y sus acciones quedan atribuidas a su usuario en la auditoría/eventos de
esa compañía (visibles para el propio tenant). Solo él edita catálogos globales y da de alta compañías nuevas
(aprovisionamiento).

Quién puede: exige la política `PlatformAdmin` (claim `IsPlatformAdmin=1` en el JWT), no un permiso de tenant.

Cómo se usa:
- `GET /api/v1/platform/tenants` — lista de compañías
- `POST /api/v1/platform/tenants` (**requiere AAL2**)
  ```json
  { "name": "Nueva Compañía", "adminEmail": "admin@nueva.com", "adminFullName": "Admin" }
  ```
  Crea el tenant, enciende los módulos núcleo y default (`LTL_GROUND`, `SYSTEM`, `CATALOG`, `ANALYTICS`,
  `CUSTOM_FIELDS`) más los pedidos explícitamente (con sus dependencias), clona las 6 plantillas de rol, crea
  el usuario administrador con el rol `TenantAdmin`, y clona las vistas/indicadores/gráficos de sistema.

Validaciones:

| Campo / caso | Mensaje exacto | HTTP |
|---|---|---|
| Nombre vacío | `El nombre es obligatorio.` | 400 |
| Correo de administrador vacío | `El correo del administrador es obligatorio.` | 400 |
| Nombre de compañía repetido | `Ya existe la compañía '<name>'.` | 409 |
| Módulo pedido desconocido | `Módulo desconocido: <key>.` | 400 |
| Contraseña de admin inválida (Identity) | mensaje de Identity | 400 |
