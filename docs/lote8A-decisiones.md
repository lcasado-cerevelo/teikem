# Lote 8A — Backend previo de la app de almacén: qué se construyó y decisiones a revisar

Fecha: 2026-09-27. Plan aprobado: `docs/lote8A-plan.json`. Diseño: `Diseño/logistica-funcionalidades-maestro.md` sección
8A y `docs/mobile/app-almacen-plan.md` §1 y §3. Este documento cierra solo el **backend previo** (idempotencia, aparatos
y PIN, sincronización por diferencia, código de barras, operaciones atómicas). La app instalable (`app-almacen/`, Expo)
y la app de choferes (Lote 8B) no se tocan en este lote.

## Mapa de lo construido

| Pieza | Tablas / columnas | Código principal | Endpoints |
|---|---|---|---|
| P0 — base compartida | `UserDevice` (código, plataforma, código de registro con hash y vencimiento, secreto con hash, almacén y tema por defecto), `UserDeviceActivity` (1:1 con `UserDevice`, sin `ROWVERSION`: último contacto, último usuario y versión de la app; decisión 10), `UserPin` (hash, intentos, bloqueo; único por `TenantId+UserId`), `Tenant.DeviceSessionDays` (30 por defecto), `IntegrationMessageLog` ampliada (`UserId`, `RequestHash`, `ResponseJson`, `Method` + índice único filtrado `UX_IntegrationLog_Idem`), `RefreshToken.UserDeviceId`; seed: `LookupCode.UiTheme` (LIGHT/DARK), `EntityType.USER_DEVICE`, permisos `devices.manage` y `warehouse.count.capture` | `IdempotencyRules`, `PinRules` (puros, con pruebas), `IdempotencyMiddleware`, configuraciones EF de `UserDevice`/`UserDeviceActivity`/`UserPin` | Sin endpoints propios; el middleware de idempotencia es transversal (después de autenticación y del contexto de tenant) |
| P1 — aparatos, PIN, login por aparato | (usa las tablas de P0) | `DeviceService`, `PinService`, `AuthService` (ampliado con login por aparato y `did` en el JWT), `DeviceRateLimits` | `GET/POST/PATCH /api/v1/devices`, `.../deactivate`, `.../reactivate`, `.../enroll-code`; anónimos: `POST /devices/enroll`, `POST /auth/device/users`, `POST /auth/device/login`, `POST /devices/heartbeat`; `PUT/DELETE /api/v1/me/pin`, `PUT/DELETE /api/v1/users/{id}/pin` |
| P2 — sincronización, código de barras, operaciones atómicas | Sin tablas nuevas (la sincronización por diferencia usa `AuditLog`/`EntityStatusHistory` existentes, no `UpdatedAtUtc`; ver decisión 1) | `SyncService` + `SyncRules` (puro, con pruebas: cursor opaco, tope de página, `since`), `ProductService.GetByBarcodeAsync`, `ReceiptService` (`Confirm` atómico), `PickBatchService` (`collect-and-pack`), `CycleCountService` (captura en lote y conteo a ciegas) | `GET /api/v1/sync/products|bins|purchase-orders|asns|warehouse-tasks|product-categories`, `GET /api/v1/products/by-barcode/{code}`, `POST /api/v1/receipts` (`confirm:true`), `POST /api/v1/pick-batches/collect-and-pack`, `PUT /api/v1/cycle-counts/{id}/lines/batch` |

Todo bajo el filtro de tenant existente (`ITenantScoped`); `USER_DEVICE` es un recurso cerrado (sin campos personalizados,
resolver que siempre da 404). El middleware de idempotencia cubre POST/PUT/PATCH/DELETE salvo los prefijos que devuelven
secretos en claro (decisión 6 ter).

## Cómo se prueba

1. `dotnet build Teikem.sln` — compiló sin errores (2 advertencias `xUnit2012` preexistentes en `RouteWriterTests.cs`,
   ajenas a este lote; verificado en este entorno).
2. `dotnet test Teikem.sln` — **2007 pruebas, 0 fallidas** tras la ronda 5 (1981 al cierre; verificado en este entorno). Del lote: `IdempotencyRulesTests`
   (validación de la clave, huella, decisión replay/conflicto/en vuelo/expirado), `PinRulesTests` (formato, secuencias
   triviales, bloqueo/desbloqueo), `SyncRulesTests` (cursor opaco, tope de página, `since`), `DeviceControllerSecurityTests`
   (permisos y módulo por reflexión), más ajustes a `ReceiptServiceTests`, `WmsCatalogTests`, `WmsContractsTests`,
   `WmsControllerSecurityTests`, `OwnedEntityResolverCoverageTests` y los catálogos de Flota/Órdenes/Viajes por los
   permisos y `EntityType` nuevos.
3. `dotnet run --project src/Teikem.Api -- db-init` sobre una base limpia (SQL Server 2022 local) — aplicó
   `logistica-db-estructura.sql` (153 lotes, ~136 tablas) y `logistica-db-seed.sql` (17 lotes, 60 permisos) sin errores;
   sembró el tenant demo.
4. Con el API arriba, `scripts/smoke.sh http://localhost:5000` **completo, de punta a punta (Lotes 1 a 8A), verde**
   (`SMOKE OK`), incluido el bloque nuevo `aparatos y sincronización (Lote 8A)` y `sesiones de aparato revocadas (Lote
   8A)`: alta de aparato con código de registro de 8 caracteres (vence en 24 h, de un solo uso, regenerable, código
   vencido con `SMOKE_SQL` → 401), enroll con secreto de un solo uso, límite de intentos (10/min) hasta el primer 429;
   PIN desde Mi cuenta (exige la contraseña actual), PIN de otros (permiso, AAL2, no a un usuario con más permisos, no
   al administrador de plataforma); `device/users` y `device/login` (PIN incorrecto 401, bloqueo al 5.º intento 423,
   sesión de 30 días con `did`); `Idempotency-Key` (repetición con el mismo cuerpo → `Idempotent-Replayed: true`, otro
   cuerpo → 409, clave inválida → 400; con `SMOKE_SQL`, registro en vuelo → 409 y clave de más de 7 días que se vuelve a ejecutar con borrado
   perezoso de los vencidos); `GET /sync/products|bins|purchase-orders` (sin `purchasing.view`
   → 403) con `since`/`cursor`/`take`; `GET /products/by-barcode/{code}` (SKU o código de barras, 404 si no existe);
   `POST /receipts` con `confirm:true` contra una orden de compra (lo escaneado manda, la orden queda PARTIAL);
   `POST /pick-batches/collect-and-pack` (atómico: un empaque que falla no deja recolección ni saca inventario; `pack`
   en `POST /pick-batches` → 400); conteo a ciegas (ficha y lista sin `varianceLines`/`netVariance` para quien no tiene
   `warehouse.count`; captura con `warehouse.count.capture`; reconciliar sigue en 403) y captura en lote (renglón
   repetido → 400, posición inexistente → 404, sin guardar nada); `switch-tenant` con sesión de aparato → 403; aparato
   desactivado → token vivo 401 y `device/login` 401; reactivar no revive sesiones revocadas.

> Cómo se ejecutó realmente: este contenedor sí tuvo SQL Server 2022 disponible en `localhost:1433` (script
> `scripts/dev-sqlserver.sh`). Se corrió una vez con la base recién creada (`db-init` limpio) y el API compilado desde el
> código de este cierre (el proceso que ya estaba corriendo era de un build más viejo, sin `DevicesController`; se mató y
> se relanzó desde el build actual antes de correr el smoke). El smoke completo tardó los 8 lotes anteriores más el
> bloque de 8A sin fallos. No se abrió un pull request ni se corrió el CI de GitHub Actions en este cierre; la corrida
> local sustituye a CI como evidencia, y CI la puede repetir al hacer push.

## Decisiones a revisar

1. **Sincronización por diferencia sin `UpdatedAtUtc` (desviación del plan, pendiente de ratificar por Luis).**
   El plan pedía `since` sobre `UpdatedAtUtc` (o `CreatedAtUtc`), y agregar la columna donde faltara. `Product`,
   `ProductCategory`, `WarehouseBin`, `WarehouseZone`, `PurchaseOrder`, `Asn` y `WarehouseTask` no la tienen. En lugar
   de agregarla (habría que tocar la estructura SQL, las entidades y el interceptor, que son compartidos, desde una pieza
   con `tocaCompartidos = false`), `SyncService` detecta los cambios con las bitácoras que ya se escriben en cada cambio:
   `AuditLog` (interceptor, por `EntityType`) y `EntityStatusHistory` (`StatusService`), más `CreatedAtUtc` y
   `CompletedAtUtc` donde existen. Efectos:
   - puede devolver de más: las hijas se auditan bajo el tipo del padre con su propio id, y las tareas abiertas llegan
     siempre;
   - lo que se cambie fuera del interceptor (`ExecuteUpdate` o SQL directo) no se detecta hasta la siguiente carga
     completa;
   - consulta subconsultas sobre `AuditLog` y `EntityStatusHistory` (índices por tenant y fecha existentes).
   El criterio queda documentado en el XML de `SyncController`. Si no se ratifica: agregar `UpdatedAtUtc DATETIME2 NULL`
   con índice `(TenantId, UpdatedAtUtc)` a esas tablas (las que no tienen `TenantId`, por su padre), mantenerlo desde el
   interceptor (`IAuditStamped`) y reemplazar `ChangedAsync` por `UpdatedAtUtc >= since OR CreatedAtUtc >= since`.

2. **Recolectar y empacar en una llamada tiene ruta propia: `POST /api/v1/pick-batches/collect-and-pack`.**
   El plan fijaba `POST /api/v1/pick-batches` con `Pack`, pero una misma acción con dos tipos de respuesta
   (`PickBatchDto` o `PickBatchPackResultDto`) deja el OpenAPI con un solo tipo y el cliente generado (web y aparato)
   tiparía mal la respuesta. La variante atómica vive en su ruta con su tipo; mandar `pack` a `POST /api/v1/pick-batches`
   responde 400 `Para recolectar y empacar en una llamada use POST /api/v1/pick-batches/collect-and-pack.`.

3. **Conteo a ciegas con permiso propio `warehouse.count.capture`.**
   El maestro pide contar "a ciegas para quien no tiene `warehouse.count`", pero todas las acciones de captura exigían
   `warehouse.count`, así que nadie podía contar a ciegas. Nuevo permiso `warehouse.count.capture` (WAREHOUSE, en la
   plantilla del Operador de almacén; 60 códigos) para alta, captura, lo encontrado y terminar; reconciliar, refrescar y
   eliminar siguen con `warehouse.count`. `warehouse.count` implica `warehouse.count.capture`
   (`PermissionCatalog.Implied`, aplicado en `PermissionService`) para que los roles propios que ya contaban no pierdan
   la captura. Quien no tiene `warehouse.count` recibe también a ciegas la respuesta de alta, captura y terminar.

4. **PIN de otros con límite de privilegios.** Asignar o quitar el PIN de otro exige `devices.manage` o `admin.users`,
   AAL2 para asignarlo, y que el destino no tenga más permisos efectivos que quien lo hace (403); el administrador de
   plataforma no es visible (404) y nunca entra por aparato (401 `PIN incorrecto.`, tampoco aparece en la lista del
   aparato). La decisión "sin MFA en el aparato" cubre el PIN que cada usuario define con su contraseña, no un PIN
   impuesto sobre una cuenta con más privilegios.

5. **Corte inmediato al desactivar el aparato.** Además de revocar los refresh tokens, `OnTokenValidated` rechaza los
   access tokens con claim `did` de un aparato inactivo (caché de 60 s que `DeviceService` borra al desactivar o
   reactivar; en otras instancias del API el retraso máximo es ese TTL).

6. **`/api/v1/users` fuera de la idempotencia.** El alta de usuario devuelve la contraseña temporal en claro; guardarla en
   `IntegrationMessageLog.ResponseJson` la dejaría 7 días en la bitácora. Las invitaciones de portal
   (`POST /api/v1/clients/{id}/portal-users/invite` y `.../portal-users/{id}/resend-invite`) quedan fuera por la misma
   razón (devuelven el token de invitación en Development): se excluyen por sufijo bajo `/api/v1/clients/`
   (`IdempotencyRules.ExcludedClientPathSuffixes`). Alternativa a revisar: un atributo `[SkipIdempotency]` en el
   endpoint, más robusto ante rutas futuras.

7. **Idempotencia: desviaciones del plan P0-5 (pendiente de ratificar por Luis).** El plan pedía idempotencia en
   todo POST/PUT/PATCH/DELETE con la cabecera, huella del cuerpo, guardar toda respuesta menor a 500 y 409 siempre para un
   registro en vuelo. El código se aparta en cuatro puntos:
   - (a) **Prefijos excluidos** `/api/v1/auth`, `/api/v1/me`, `/api/v1/devices` y `/api/v1/platform` (además de
     `/api/v1/users` y las invitaciones de portal, decisión 6): devuelven tokens, secretos del aparato, códigos
     de registro o contraseñas temporales, que no deben quedar 7 días en `IntegrationMessageLog.ResponseJson`.
     Alternativa si no se ratifica: un atributo `[SkipIdempotency]` en cada endpoint.
   - (b) **No se guardan 401, 403, 408, 423 ni 429**: son rechazos de acceso o de ritmo, no de negocio; reintentar con la
     misma clave (tras reautenticarse, esperar el bloqueo del PIN o el límite) debe volver a intentarlo. Alternativa: guardarlas
     como las demás (el aparato tendría que cambiar de clave tras un 401).
   - (c) **Un registro en vuelo con más de 10 minutos (`IdempotencyRules.InFlightTimeout`) se da por abandonado** y la
     petición se vuelve a ejecutar; el plan pedía 409 siempre. Motivo: un proceso caído a mitad de la operación dejaría la
     clave bloqueada hasta que venza la retención (7 días). Riesgo: si la operación original sigue en curso a los 10
     minutos (o terminó y no pudo guardar su respuesta), el reintento la duplica. Mitigado en esta revisión: la operación ya
     no se cancela si el cliente se desconecta (decisión 17) y el guardado de la respuesta se reintenta. Alternativa: 409
     siempre y limpiar los registros en vuelo por otra vía.
   - (d) **La huella SHA-256 cubre método, ruta con query y cuerpo**; el plan solo pedía el cuerpo. Efecto: la misma clave
     en otra ruta responde 409 `La clave de idempotencia ya se usó con otro contenido.` en vez de devolver la respuesta de
     otra operación.

8. **`Tenant.DeviceSessionDays` configurable** en `GET/PUT /api/v1/tenant/settings` (`deviceSessionDays`, 1 a 365; 400
   `Entre 1 y 365 días.`).

9. **Contador del PIN atómico.** El fallo se cuenta con un `UPDATE` en SQL (`FailedCount + 1`, bloqueo al 5.º)
   condicionado a que la fila no esté bloqueada; el acierto pone el contador en 0 con la misma condición (si otro
   intento ya bloqueó, 423 también con el PIN correcto). Antes el contador se leía y reescribía en memoria y 20 intentos
   en paralelo dejaban `FailedCount = 1` sin bloqueo. Se prefirió al bloqueo de fila con transacción porque
   `RunInTransactionAsync` limpia el rastreador y desconectaría el `UserDevice` que usa el login.

10. **Datos técnicos del aparato en tabla aparte, sin `ROWVERSION` (`UserDeviceActivity`).** `LastSeenUtc`,
    `LastUserId` y `AppVersion` salieron de `UserDevice` a `dbo.UserDeviceActivity` (PK = `UserDeviceId`, `TenantId`,
    sin `ROWVERSION`, sin auditoría). En SQL Server cualquier `UPDATE` de la fila sube su `ROWVERSION`, que es el token de
    concurrencia del `PATCH` (y de desactivar, reactivar y regenerar el código): con las columnas en `UserDevice`, un
    aparato en línea (heartbeat periódico o login con PIN) dejaba vieja la `rowVersion` que leyó la pantalla y el `PATCH`
    respondía 409 `El registro fue modificado por otro usuario; recargue e intente de nuevo.` sin que nadie hubiera
    editado el aparato. Ahora el heartbeat y el login escriben con `ExecuteUpdate` sobre `UserDeviceActivity` (sin el
    rastreador: un heartbeat y un login simultáneos no chocan) y el login lo hace ANTES de emitir los tokens; el enroll
    crea la fila en la misma transacción. Solo una edición real da 409. El `DeviceDto` no cambia (`appVersion`,
    `lastSeenUtc`, `lastUserId` se leen de la tabla nueva). Se descartó la alternativa de un `EditVersion INT` como token
    (más corta, pero deja el `ROWVERSION` de la fila sin uso real y rompe la convención del proyecto).

11. **Límite de intentos de los endpoints anónimos del aparato** (limitador de .NET 8, sin paquetes): ventana fija de 1
    minuto por IP y ruta; `enroll` 10, `device/users`, `device/login` y `heartbeat` 60 (configurables en
    `RateLimiting:*`); 429 `Demasiados intentos; espere un minuto e intente de nuevo.`. A revisar: detrás de un proxy
    inverso la IP es la del proxy salvo que se configure `UseForwardedHeaders`; y el login con contraseña no se limitó
    (conserva el bloqueo por cuenta de Identity y el smoke/Playwright hacen muchos logins desde la misma IP).

12. **`USER_DEVICE` con resolver cerrado** y `devices.manage` como permiso de dueño (lectura y escritura) en la ruta
    polimórfica de campos personalizados; antes quedaba abierta a cualquier autenticado.

13. **`GET /api/v1/sync/purchase-orders` exige el módulo PURCHASING y `purchasing.view`** (desviación del plan, que
    pedía solo `inventory.view`): con solo `inventory.view` el rol Solo lectura veía órdenes de compra y proveedores que
    la ruta nativa le niega. El Operador de almacén ya trae `purchasing.view`.

14. **Recibo en una llamada contra aviso u orden de compra: lo escaneado manda.** Antes las `lines` se ignoraban en
    silencio y se confirmaba lo esperado. Ahora se aplican sobre las líneas del documento por producto (y lote); lo no
    mencionado queda en 0, lecturas repetidas del mismo producto se suman y un producto fuera del documento entra como
    línea extra. Sin `lines` se recibe lo esperado (R8), como antes.

15. **Conteo a ciegas sin diferencia en el encabezado.** `CycleCountDto.VarianceLines` y `NetVariance` pasan a
    `int?`/`decimal?` y llegan `null` a quien no tiene `warehouse.count` (ficha, respuestas y lista); ponerlos en 0
    diría "todo cuadra", que es falso. `web-app/openapi.json` y los tipos del cliente se regeneraron.
    **Cambio de comportamiento en la web** (el plan decía que la web no cambiaba): en Almacén → Conteos cíclicos los roles
    sin `warehouse.count` (Solo lectura y Facturación) veían la diferencia neta real y, con el API ya cegado, la pantalla
    pintaba 0 en todas las filas. Se corrigió en la web: sin `warehouse.count` la columna "Diferencia neta" no se muestra,
    y `null` se pinta como "—" (lista y ficha), nunca como 0. Queda como cambio visible para esos roles (manual 06 y FAQ).

16. **Vigencia del código de registro (cerrada).** El filtro de vencimiento del enroll se prueba en el smoke: con
    `SMOKE_SQL` se pone `EnrollCodeExpiresUtc` en el pasado, el mismo código (hash correcto) da 401 `El código de registro
    no es válido o venció.` y, al restaurar la vigencia, registra el aparato. Sin `TimeProvider` en `DeviceService`.

17. **Flujos anónimos del aparato sin el usuario de un bearer ajeno.** `TenantContextMiddleware` llena `UserId` con
    cualquier bearer válido aunque el endpoint sea `[AllowAnonymous]`; el registro, la lista de usuarios, el heartbeat y el
    login por aparato cambiaban al tenant del aparato con `As(tenantId)` y conservaban ese usuario, así que un token de
    otra compañía dejaba su `UserId` en el `AuditLog` y en `TOKEN_REVOKED` de la compañía del aparato. Ahora usan
    `TenantContext.AsAnonymous(tenantId)` (tenant del aparato, usuario vacío) y los eventos de fallo de esos flujos se
    escriben sin el tenant ni el usuario del contexto.

18. **La operación con `Idempotency-Key` no se cancela si el cliente se desconecta.** El middleware pone
    `HttpContext.RequestAborted = CancellationToken.None` después de registrar la clave: si el aparato pierde la señal
    después del commit (p. ej. al releer el recibo confirmado en una llamada), la operación termina, su respuesta se guarda
    y el reintento de la cola recibe `Idempotent-Replayed` en vez de crear y confirmar otro recibo. Antes la cancelación
    borraba la clave y el reintento duplicaba la entrada de inventario. Guardar la respuesta se reintenta (3 veces) si
    falla. A revisar: la solución completa es escribir `ResponseCode` en la misma transacción de negocio.

19. **Intentos rechazados del aparato en la bitácora.** `DeviceService.AuthenticateAsync` (device/login y device/users)
    escribe `LOGIN` / `FAILURE` con `stage = device`, `action` y `reason` (`device_invalid`, `device_inactive`,
    `tenant_unusable`) antes del 401, en la compañía del aparato si existe. Antes solo quedaban los fallos de usuario o PIN.

20. **La sesión abierta con PIN en un aparato no cambia de compañía.** `POST /api/v1/auth/switch-tenant` con el refresh
    token de una sesión de aparato responde 403 `La sesión de un aparato no cambia de compañía.` sin revocarla (a
    diferencia de otros 403 de esta ruta, que sí revocan). Esa sesión no tiene contraseña ni MFA, así que no debe poder
    saltar a otra compañía aunque el usuario sea miembro de ambas.

21. **`scripts/smoke.sh` ya trae el bloque de 8A completo** (`aparatos y sincronización (Lote 8A)` y
    `sesiones de aparato revocadas (Lote 8A)`, con el paso opcional de `SMOKE_SQL` para el código de registro vencido).
    No se agregó nada distinto en este cierre porque ya cubre lo pedido (enroll, PIN, device/login, idempotencia
    con replay y 409, sincronización, código de barras, recibo atómico y aparato desactivado) y de más (collect-and-pack,
    captura en lote, conteo a ciegas, límite de intentos, sesiones revocadas). Se corrió tal cual, verde de punta a
    punta, sin tocarlo.

22. **`PUT/DELETE /api/v1/users/{id}/pin` con política "cualquiera de" (`perm:devices.manage|admin.users`).**
    `[RequireAal2]` es un filtro de acción y corría antes que la verificación de permiso del servicio: un usuario sin
    `devices.manage` ni `admin.users` y sin reauth recibía 403 `aal2_required` (le pedía reautenticarse cuando le faltaba
    el permiso) y no quedaba `PERMISSION_DENIED`. `RequirePermissionAttribute` acepta ahora varios códigos separados por
    `|` (basta uno; `PermissionHandler` escribe `PERMISSION_DENIED` con la cadena completa si no tiene ninguno) y
    `UserPinsController` lo usa a nivel de clase; `PinService` conserva su verificación (defensa en profundidad).

23. **Campos de entrada anulables en los contratos del aparato y del PIN.** Con `<Nullable>enable</Nullable>`,
    [ApiController] trataba los `string` no anulables como obligatorios y respondía el 400 genérico de MVC en inglés
    (`The EnrollCode field is required.`) antes de llegar al servicio. `DeviceEnrollRequest.EnrollCode`,
    `DeviceUsersRequest.DeviceSecret`, `DeviceLoginRequest.DeviceSecret`/`Pin`, `PinSetRequest.CurrentPassword`/`Pin`,
    `PinAdminSetRequest.Pin` y `HeartbeatRequest.DeviceSecret` pasan a `string?`: si faltan, responde el servicio con su
    mensaje (401 `El código de registro no es válido o venció.`, 401 `El aparato no está registrado o fue desactivado.`,
    400 `La contraseña actual es incorrecta.`, 400 `El PIN debe tener de 4 a 6 dígitos.`). Nombres y posiciones no
    cambian; el documento OpenAPI generado no cambia (Swashbuckle no marca la anulabilidad de referencia). Los contratos
    anteriores (p. ej. `LoginRequest`) conservan el comportamiento de siempre; la alternativa global
    (`SuppressImplicitRequiredAttributeForNonNullableReferenceTypes`) queda a revisar.

24. **Eliminar un conteo cierra su tarea COUNT con `CompletedAtUtc`.** `CycleCountService.DeleteAsync` cancelaba la
    tarea sin fecha de cierre (a diferencia de `WarehouseTaskWriter.CancelAsync`): su antigüedad (`AgeHours`) seguía
    creciendo y la diferencia de `sync/warehouse-tasks` solo la veía por `EntityStatusHistory`. Ahora llega por las dos
    ramas con `isActive=false` y `statusCode=CANCELLED`.

25. **Contrato de la app: manda el API.** `docs/mobile/app-almacen-plan.md` §1, §2, §3 y §7 se alinearon con lo
    construido (`since`/`cursor`/`take`, `POST /devices/enroll`, `POST /auth/device/users`, `POST /auth/device/login`,
    `POST /pick-batches/collect-and-pack`, `PUT /cycle-counts/{id}/lines/batch`, `Tenant.DeviceSessionDays` de 30 días
    configurable en lugar de `RefreshTokenDays` 14/1) y llevan la nota "el contrato vigente es `web-app/openapi.json` y este
    documento; si difieren, manda el contrato". El documento decía `?modifiedSinceUtc=` (el API lo ignora sin error y
    cada pasada sería una carga completa que nunca trae las bajas), `/auth/device-login` y `/devices/register`.

26. **La sesión abierta con PIN en un aparato no administra el segundo factor** (junto a la decisión 20). `POST
    /api/v1/auth/mfa/totp/enroll` y `/confirm` con un access token que trae el claim `did` responden 403 `La sesión de un
    aparato no administra el segundo factor.` antes de llamar al servicio (guarda en `AuthController`; el challenge token
    del login nunca lleva `did`, así que enrolar durante el login sigue igual). Sin esto, quien viera el PIN en un aparato
    compartido, o quien lo asignó con `devices.manage`, se quedaba con el secreto TOTP y los códigos de recuperación de una
    cuenta sin MFA y el dueño ya no podía entrar a la web con su contraseña. A revisar: `logout-all` y `DELETE
    /auth/sessions/{id}` siguen abiertos a la sesión de aparato (solo cierran sesiones, no toman la cuenta).

27. **Contraseña equivocada con bloqueo por cuenta en todas las rutas que la verifican.** `PUT /api/v1/me/pin`, `POST
    /api/v1/auth/reauth` y `PUT /api/v1/auth/password` usan ahora el mismo bloqueo de Identity que el login
    (`IsLockedOutAsync` → `AccessFailedAsync` → `ResetAccessFailedCountAsync`; 5 fallos → 15 minutos, contados en conjunto
    con el login). Con la cuenta bloqueada responden como una contraseña incorrecta (400 `La contraseña actual es
    incorrecta.`, 401 `Contraseña incorrecta.` y el 400 de Identity en `newPassword`, respectivamente) y escriben
    `LOCKOUT` / `BLOCKED`. Antes, una sesión de aparato abierta solo con PIN podía probar contraseñas sin límite por
    `PUT /me/pin` (y por reauth y el cambio de contraseña), incluso con la cuenta bloqueada en la web. Efecto aceptado:
    quien tenga una sesión puede bloquear 15 minutos la cuenta web de su dueño equivocándose a propósito (igual que ya
    podía cualquiera que supiera el correo por el login).

28. **El límite de privilegios del PIN impuesto se vuelve a comprobar en cada login y refresh del aparato.**
    `PinService.AssignerStillCoversAsync`: si `UserPin.UpdatedBy` es otra persona, sus permisos efectivos actuales deben
    cubrir los del usuario, y debe seguir activa y con membresía ACTIVE en la compañía (se exceptúa el admin de
    plataforma activo). Si no: el login por aparato responde 403 `Su PIN lo asignó otra persona que ya no tiene sus
    permisos; defina su propio PIN en Mi cuenta.` (`LOGIN` / `BLOCKED`, `reason = pin_assigner_lower_privileges`,
    solo después de un PIN correcto para no dar oráculo) y el refresh de una sesión de aparato la revoca con 401 y el
    mismo mensaje (`TOKEN_REVOKED`). Antes solo se revisaba al asignar: subir después los permisos del usuario dejaba a
    quien asignó el PIN entrar como él con más privilegios. A revisar: no se borran los PIN al cambiar roles (el PIN
    queda inservible pero no desaparece; `hasPin` sigue en true hasta que el usuario lo redefina o alguien lo quite).
29. **La repetición idempotente reevalúa las comprobaciones hechas dentro de la operación.** `IdempotencyCheckRecorder`
    (scoped) anota, solo mientras corre la operación con clave, cada `ModuleService.IsEnabledAsync` y
    `PermissionService.HasPermissionAsync` (incluye `EnsureAsync`/`EnsureEnabledAsync` del servicio y el modo a ciegas del
    controlador de conteos) con su resultado; se guardan en `IntegrationMessageLog.ReplayChecksJson` (columna nueva). Antes
    de repetir: cualquier comprobación que cambie, en cualquier sentido (también la que se cumplía y ya no, p. ej. perder
    `warehouse.count` tras un conteo informado), o un registro ilegible → 409 `La operación con esta clave ya no puede
    repetirse con los permisos actuales.`, no 403: la respuesta guardada no se puede servir y una llamada nueva daría otro
    resultado. Los atributos del endpoint (`[RequirePermission]`, `[RequireModule]`, `[RequireAal2]`) siguen respondiendo su
    propio 403 (`module_disabled`, `aal2_required`, falta de permiso). Además `IdempotencyKey` usa
    `COLLATE Latin1_General_100_BIN2` (la clave distingue mayúsculas también en SQL Server, como en las pruebas InMemory).
    A revisar: un permiso consultado dentro de la operación que no influye en la respuesta también bloquea la repetición
    (409) si cambia de false a true.

## Lo que queda fuera de este lote (a propósito)

- **La app instalable de almacén** (`app-almacen/`, Expo/React Native): este lote es solo el backend previo
  (`docs/mobile/app-almacen-plan.md` §3); la app en sí (pantallas, escáner DataWedge, base local SQLite, cola de salida)
  no se construye aquí.
- **Lote 8B (app de choferes)**: reutiliza este backend (aparatos, PIN, idempotencia, sincronización) pero no se toca en
  este cierre. `DriverDevice` se conserva sin cambios; `UserDevice` es la tabla nueva para aparatos de almacén.
- **Columna `UpdatedAtUtc`** en `Product`, `ProductCategory`, `WarehouseBin`, `WarehouseZone`, `PurchaseOrder`, `Asn` y
  `WarehouseTask`: no se agregó (decisión 1); la sincronización usa `AuditLog`/`EntityStatusHistory` en su lugar.
- **Sincronización de clientes y consignatarios** (`sync/clients`, `sync/consignee-locations`): no se construyó; la lista
  de sincronización es la del plan aprobado (productos, posiciones, órdenes de compra, avisos, tareas y categorías). Efecto:
  el despacho de **inventario propio** (`Product.ClientId` nulo) **sin señal** no puede elegir el cliente ni el consignatario
  de la orden que crea `POST /api/v1/pick-batches/collect-and-pack`; en 3PL el cliente queda fijado al dueño, que sí llega en
  `SyncProductDto.OwnerClientPublicId`. Mientras tanto la app los busca **en línea** con `GET /api/v1/clients` (permiso
  `clients.read`) y `GET /api/v1/locations` (permiso `locations.read`, módulo `CATALOG`). La plantilla *Operador de almacén*
  no trae `orders.create` (lo exige collect-and-pack) ni `clients.read`/`locations.read`: quien despache desde el aparato
  necesita un rol que los agregue. Queda para el lote de la app (o un 8A-bis): sincronizar clientes activos aptos para
  órdenes y sus consignatarios con el mismo protocolo `since`/`cursor`/`take`, detrás de `warehouse.pick`.
- **`[SkipIdempotency]` como atributo declarativo**: los prefijos excluidos de la idempotencia están hoy en una lista
  fija en `IdempotencyRules` (decisiones 6 y 7-a), no en un atributo por endpoint.
- **Zona horaria del tenant** para "since menos 5 minutos" u otras ventanas: sigue en UTC, sin `Tenant.TimeZoneId`
  (mismo pendiente que el Lote 7A).
- **CI de GitHub Actions**: no se abrió PR ni se corrió el workflow en este cierre; la evidencia es la corrida local
  (build, test, db-init, smoke completo, todos verdes en este entorno).

## Verificación

Antes de este cierre se corrigieron 53 hallazgos durante la verificación del lote (historial de commits, mensajes
"punto de control — correcciones de revisión"); no se llevó una lista aparte de los 53 por separado de este documento y
del historial de `git log`.

Correcciones posteriores al cierre (revisión de seguridad y cobertura):
- El login por aparato con un `userId` que no es (ni fue) miembro de la compañía del aparato escribe el evento `LOGIN` /
  `FAILURE` sin usuario (`requestedUserId` en el detalle), igual que el login con contraseña: la bitácora de una compañía
  ya no revela el nombre ni el correo de usuarios de otras.
- Cambiar o restablecer un PIN existente (`PUT /me/pin`, `PUT /users/{id}/pin`) revoca las sesiones del usuario en
  aparatos (`TOKEN_REVOKED` con `pin_changed`), como ya hacía quitarlo.
- El refresh de una sesión de aparato exige también la compañía activa y el módulo WMS_LOTSERIAL encendido (401
  `El aparato no está registrado o fue desactivado.` y la sesión queda revocada). Pendiente a revisar: el access token
  vivo (minutos) no se corta al apagar el módulo; habría que revisarlo en `OnTokenValidated` o revocar al apagarlo.
- El capítulo del manual se renombró a `docs/manual/08-aparatos-y-sincronizacion.md` (el índice apuntaba a un archivo
  inexistente) y la FAQ ganó los mensajes que faltaban.
- Smoke: enroll con almacén y tema; login por aparato de Operador 6 (sub, `/me`, `lastUserId`, autoría en AuditLog y
  sus propios permisos); refresh con el módulo apagado 401; PIN restablecido → refresh 401; AuditLog de `USER_DEVICE`
  sin secretos; ramas de BD de la idempotencia (en vuelo, vencida, borrado perezoso) con `SMOKE_SQL`.

Correcciones de la ronda 5 (revisión de concurrencia, contratos, permisos y cobertura):
- Heartbeat y login con PIN ya no invalidan la `rowVersion` del aparato (decisión 10, tabla `UserDeviceActivity`).
- Contratos del aparato y del PIN con campos anulables: mensajes en español del servicio en lugar del 400 de MVC
  (decisión 23).
- PIN de otros: política `perm:devices.manage|admin.users` antes de AAL2, con `PERMISSION_DENIED` (decisión 22).
- `docs/mobile/app-almacen-plan.md` alineado con el API construido (decisión 25).
- Eliminar un conteo fija `CompletedAtUtc` en su tarea COUNT cancelada (decisión 24).
- Pruebas nuevas: `SyncRulesTests` (tarea cancelada sin `CompletedAtUtc` que llega por `EntityStatusHistory` con
  `isActive=false`), `CycleCountFilterTests` (a ciegas `onlyVariance` se ignora y los demás filtros aplican),
  `DeviceServiceTests` (el enroll escribe la actividad en `UserDeviceActivity`), `DeviceControllerSecurityTests` (política
  de la clase de `UserPinsController`). `dotnet test`: **2007 pruebas, 0 fallidas**.
- Smoke (bloque 8A), verde de punta a punta contra una base recién creada con `SMOKE_SQL`: la `rowVersion` no cambia con
  heartbeat ni login y el `PATCH` con ella pasa (con la misma tras una edición real → 409); enroll sin código 401,
  device/login sin secreto 401 y `PUT /me/pin` sin contraseña 400 en español; PIN de otro sin permiso → 403 que no es
  `aal2_required` y deja `PERMISSION_DENIED`; vencido el bloqueo del PIN se vuelve a entrar sin restablecerlo (un fallo
  cuenta desde cero); 10 PIN incorrectos en paralelo → a lo más 4 × 401 y el resto 423, y luego el PIN correcto 423
  (decisión 9); carrera de 4 peticiones con la misma clave → un solo REC (200 con el mismo número o 409 en vuelo) sin
  consumir otro número; desconexión del cliente a mitad de un recibo confirmado → el reintento recibe
  `Idempotent-Replayed`, una sola entrada de inventario y un solo número REC (decisión 18; si la operación termina antes
  del corte, el paso sigue pasando); segunda recepción parcial (PARTIAL→PARTIAL) → `sync/purchase-orders` trae la orden
  con el pendiente nuevo; `onlyVariance` a ciegas devuelve todas las líneas; `PUT` y `POST /cycle-counts/{id}/lines` del
  contador llegan a ciegas; la tarea COUNT del conteo eliminado llega a `sync/warehouse-tasks` con `isActive=false`.
- Pendiente a revisar: la rama `IsUniqueViolation → 409` del middleware (dos inserciones simultáneas de la misma clave)
  la ejercita el smoke en paralelo cuando hay carrera real, pero no de forma determinista; haría falta una prueba de
  integración contra SQL Server.

Correcciones de la ronda 6 (cobertura de reglas que ninguna prueba fijaba; sin cambios de código de producción):
- `PermissionServiceImpliedTests`: `warehouse.count ⇒ warehouse.count.capture` calculado desde los datos del rol (sin la
  caché `perms:`), y la política "cualquiera de" de `PermissionHandler` (`devices.manage|admin.users`) con solo el segundo
  permiso, solo el primero y ninguno.
- `DeviceServiceTests`: `device/users` → 401 con el módulo WMS_LOTSERIAL apagado y con la compañía inactiva; enroll y
  `device/users` con un usuario en el contexto (bearer ajeno) escriben sus eventos y su `SaveChanges` sin usuario y
  restauran el contexto (decisión 17).
- `CycleCountServiceTests`: la tarea COUNT del conteo eliminado queda con `CompletedAtUtc` (decisión 24).
- Web (vitest): lista de conteos sin `warehouse.count` sin la columna "Diferencia neta"; con ella y `netVariance` nulo
  pinta '—'; la ficha a ciegas pinta '—' en el resumen (decisión 15).
- Smoke (bloque 8A): `POST /devices` con `Idempotency-Key` se ejecuta dos veces (200 y 409 por código repetido, sin
  Replay) y no deja registro en `IntegrationMessageLog`; con el módulo apagado `device/login` y `device/users` → 401 y el
  heartbeat → `isActive=false`; un 403 del servicio con clave (recibo contra OC sin `purchasing.receive`) no se guarda y,
  concedido el permiso, el reintento con la misma clave se ejecuta sin `Idempotent-Replayed`; tras reactivar el aparato el
  access token nuevo sincroniza al momento (caché `did` limpiada).
- Sincronización de clientes y consignatarios: anotada en "Lo que queda fuera de este lote" (despacho de inventario propio
  sin señal).
- `dotnet test`: **2014 pruebas, 0 fallidas**.

Correcciones de la ronda 7 (revisión de seguridad de la sesión de aparato y cobertura):
- Decisión 26: enroll y confirm de TOTP con sesión de aparato → 403 (`DeviceControllerSecurityTests` y smoke).
- Decisión 27: bloqueo por cuenta en `PUT /me/pin`, reauth y cambio de contraseña. `PUT /me/pin` lo prueba
  `DeviceServiceTests` (la contraseña equivocada cuenta, un acierto reinicia, al 5.º fallo la cuenta queda bloqueada 15
  minutos y ni la contraseña correcta guarda el PIN); `POST /auth/reauth` y `PUT /auth/password` los prueba
  `AuthLockoutTests` (la contraseña equivocada sube `AccessFailedCount`, un acierto lo deja en 0, tras 5 fallos
  `LockoutEnd` queda a más de 14 minutos y la contraseña correcta da 401 `Contraseña incorrecta.` sin fijar
  `Aal2VerifiedAtUtc` o el 400 en `newPassword` sin cambiar el hash, con `LOCKOUT` stage=reauth/password_change).
- Decisión 12 con prueba: `WmsCatalogTests` fija `devices.manage` como permiso de dueño (lectura y escritura) de
  `USER_DEVICE`; smoke: `GET`/`PUT /custom-fields/values/USER_DEVICE/1` sin `devices.manage` → 403 con el permiso exacto
  y con él `PUT` → 404 (resolver cerrado).
- Decisión 23 con prueba: `DeviceContractsTests` fija por reflexión la firma anulable de los seis contratos de entrada
  del aparato y del PIN; smoke: `device/users` y heartbeat sin `deviceSecret` → 401, `PUT /me/pin` y `PUT
  /users/{id}/pin` sin `pin` → 400 en `errors.pin` con `El PIN debe tener de 4 a 6 dígitos.`.
- Decisión 17 en `device/login`: el smoke repite el login con un `userId` ajeno mandando el bearer de otra compañía y
  exige que el evento `LOGIN` quede sin usuario.
- PIN por compañía: `DeviceServiceTests` (un miembro con PIN solo en otra compañía no sale en `device/users`) y smoke con
  `SMOKE_SQL` (ese PIN no lista al usuario ni abre el aparato: 401 `PIN incorrecto.`).
- "Los 5xx no se guardan": `IdempotencyMiddlewareTests` comprueba que una excepción no controlada, o una de dominio con
  la respuesta ya iniciada, libera la clave del registro insertado y se relanza (con InMemory se verifica el intento de
  borrado por el error que registra `DeleteAsync`; el borrado real lo cubre SQL Server).
- `dotnet test`: **2025 pruebas, 0 fallidas**.

Correcciones de la ronda 8 (privilegios del PIN impuesto y cobertura):
- Decisión 28: `DeviceServiceTests` (`AssignerStillCoversAsync` con permisos subidos, quien asignó desactivado, fuera de
  la compañía o admin de plataforma) y smoke (login 403 y refresh 401 con el mensaje exacto, evento
  `pin_assigner_lower_privileges`). Manual 08 §3.2 y §3.3 y FAQ.
- Decisión 27: `AuthLockoutTests` cubre reauth y cambio de contraseña (antes solo `PUT /me/pin`); ronda 7 corregida.
- Captura de conteo en lote: `CycleCountServiceTests` (serie, lote por número, lote faltante, cantidad en serie,
  posición y producto dados de baja) y smoke contra SQL Server (el lote creado en un renglón se revierte si otro falla).
- Idempotencia: `IdempotencyMiddlewareTests` repite un 204 sin cuerpo (DELETE) y un 200 de PATCH sin llamar a la
  operación; smoke con `DELETE /receipts/{id}` y `PATCH /products/{id}` repetidos con la misma clave.
- Smoke: refresh 200 de la sesión de aparato tras el 403 de switch-tenant (decisión 20); collect-and-pack fallido no
  consume el número EMP; alta de aparato con nombre de 101 y modelo de 81 caracteres → 400 (y pruebas en
  `DeviceServiceTests`, también en la edición).
- `dotnet test`: **2033 pruebas, 0 fallidas**.


## Re-verificación (paso 3b del plan de trabajo, 27 y 28 de septiembre de 2026)

Tras el cierre con tope de 3 rondas, se reanudó la verificación sin tope. La primera reanudación revisaba toda la rama y no
convergía (6 rondas, 7 a 11 hallazgos confirmados por ronda, la mayoría de severidad baja sobre pruebas faltantes). Se acotó
la revisión a los archivos del lote y se redefinió la ronda limpia (sin hallazgos alta ni media confirmados). En la corrida
acotada, 4 rondas: hallazgos graves confirmados y corregidos: (1) el humo fallaba en el bloque de aparatos por un PATCH de
producto sin `Idempotency-Key`; (2) el conteo a ciegas se podía saltar con `inventory.view`; (3) la respuesta repetida por
idempotencia no volvía a aplicar permisos y módulos del endpoint; (4) `UserDevice.RegisteredBy` y `UserPin.UpdatedBy` sin
llave foránea a `AspNetUsers` (corregido a mano por el orquestador). Por decisión de Luis (consumo), el workflow se detuvo
ahí y el cierre se hizo tarea por tarea: `dotnet build`, `dotnet test` (2048 pruebas, 0 fallidas), base limpia con `db-init`
dos veces y `scripts/smoke.sh` completo en verde. **Estado real de la verificación (sin adornos):** la revisión por lentes NO se completó. El criterio acordado (dos rondas
limpias seguidas) no se alcanzó: la última ronda completa aún confirmó un hallazgo medio (corregido a mano), y la ronda
siguiente quedó a medias al detener el workflow (compile-ef y tenant-security terminaron en cero; spec y tests no
terminaron). El Lote 7A tampoco tuvo una ronda limpia después de sus últimas correcciones. Lo que sí está verificado por
ejecución es lo listado arriba (build, pruebas unitarias, db-init, smoke). Además, este documento, el manual 08 y el FAQ se
escribieron antes de las correcciones de las rondas 1 a 4 y no se cotejaron después contra el código: pueden tener puntos
desactualizados (repetición idempotente y permisos, permiso del conteo a ciegas). Pendiente para el próximo lote:
una ronda de revisión acotada de 7A y 8A hasta quedar limpia, y cotejo del manual 08 y el FAQ contra el código.

## Sugerencias de la revisión (severidad baja, no corregidas; decide Luis si entran en un lote posterior)

Recogidas de las rondas de re-verificación acotada del 27 y 28 de septiembre de 2026 (lente de pruebas en su mayoría).

- `IdempotencyMiddleware.cs`: La repetición idempotente responde sin pasar por [RequireModule] ni [RequireAal2]
- `AuthController.cs`: La documentación XML de POST /auth/device/login no menciona el 403 del PIN asignado por otra persona
- `CycleCountService.cs`: Comentario XML duplicado: la sobrecarga nueva ListAsync(q, blind, ct) tiene dos <summary> y la original ListAsync(q, ct) se quedó sin ninguno
- `CycleCountService.cs`: Ninguna prueba ni paso del smoke captura en lote una línea existente identificada por posición + producto (sin lineId)
- `PickBatchService.cs`: No hay prueba de collect-and-pack sin orders.create (403, sin sacar inventario ni consumir el EMP)
- `ReceiptService.cs`: Recibo en una llamada contra aviso de cliente (asnId + confirm:true) y la aplicación de lecturas por lote y por serie, sin prueba
- `DeviceService.cs`: Re-registrar un aparato ya enlazado: no se prueba que el secreto anterior deje de servir ni que se revoquen sus sesiones
- `SyncService.cs`: El aislamiento entre compañías de sync/bins, purchase-orders, asns y warehouse-tasks no tiene prueba con filas de otro tenant
- `IdempotencyMiddleware.cs`: No se prueba la parte TenantId de la clave lógica de idempotencia (el mismo usuario y la misma clave en otra compañía)
- `ProductService.cs`: by-barcode: no hay prueba de «gana el propio» entre dueños ni del aislamiento entre compañías
- `DeviceService.cs`: No se prueba que los usuarios de portal queden fuera del aparato (device/users, device/login) ni del PIN impuesto
- `SyncService.cs`: No se prueban las ramas de diferencia de sync por líneas del aviso ni por resoluciones de faltantes de la OC
- `DeviceService.cs`: No se prueba que un almacén por defecto de otra compañía dé 404 al crear o editar un aparato
- `IdempotencyMiddleware.cs`: Ninguna prueba cubre que la repetición (Replay) vuelva a exigir [RequireAal2]
- `PinService.cs`: Nada verifica los SecurityEvent de PIN asignado o quitado por un administrador ni el TOKEN_REVOKED pin_removed
- `DeviceService.cs`: Reinstalar un aparato ya registrado: no se prueba que el secreto anterior deje de servir ni que las sesiones queden revocadas
- `ProductService.cs`: by-barcode: la regla 'con el mismo código en varios dueños gana el propio' no tiene prueba
- `PinService.cs`: El 409 'El PIN del usuario cambió al mismo tiempo en otra sesión; intente de nuevo.' está documentado pero no tiene prueba
- `PinService.cs`: GET /api/v1/me/pin con el PIN bloqueado: no se prueba que devuelva lockedUntilUtc
- `IdempotencyMiddleware.cs`: En SQL Server la búsqueda de la Idempotency-Key no distingue mayúsculas; las pruebas en InMemory sí
- `PinService.cs`: Ninguna prueba ni paso del smoke cubre la exclusión de usuarios de portal del PIN y del login por aparato
- `IdempotencyMiddleware.cs`: La parte TenantId de la clave de idempotencia (TenantId, UserId, clave) no tiene prueba
- `SyncService.cs`: El filtro warehousePublicId de sync/purchase-orders y sync/asns no tiene prueba que excluya otro almacén
- `SyncRulesTests.cs`: La prueba nueva de by-barcode dice en su nombre 'menor id de cliente', pero el código desempata por menor ProductId y los datos no distinguen las dos reglas
- `AuthService.cs`: El cambio de contraseña con la cuenta bloqueada (rama nueva del lote) responde un mensaje en inglés
- `DeviceService.cs`: Volver a registrar un aparato (código regenerado) revoca sus sesiones y anula el secreto anterior, pero ninguna prueba ni paso del smoke lo cubre
- `PickBatchService.cs`: Falta probar collect-and-pack sin orders.create (403 sin recolectar ni sacar inventario)
- `smoke.sh`: El 403 por falta de purchasing.receive en el recibo atómico contra orden de compra (confirm:true) no tiene prueba
- `smoke.sh`: La aserción de AuditLog de USER_DEVICE solo exige total >= 1; no comprueba que la edición, la baja y la reactivación se auditen, aunque el mensaje ok lo afirma
- `PinService.cs`: La exclusión de usuarios de portal (PIN de otros → 404; fuera de device/users) está documentada pero no tiene prueba
- `SyncService.cs`: Ninguna prueba verifica el dueño 3PL (ownerClientPublicId/ownerName) de sync/products, del que depende el despacho sin señal
- `SyncService.cs`: sync/bins con el almacén dado de baja (rama por historial de estatus del almacén) sin prueba
- `IdempotencyMiddleware.cs`: Nada verifica el contenido del registro de idempotencia insertado (Method, Endpoint, RequestHash, Direction INBOUND, sin cuerpo de la petición)

## Verificación en CI

- Corrida verde de GitHub Actions del cierre del paso 3b (jobs `build-test` y `frontend`): https://github.com/lcasado-cerevelo/teikem/actions/runs/36365377942 (commit `de2f7ce`).
