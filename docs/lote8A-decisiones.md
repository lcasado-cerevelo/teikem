# Lote 8A — Backend previo de la app de almacén: qué se construyó y decisiones a revisar

Fecha: 2026-09-27. Plan aprobado: `docs/lote8A-plan.json`. Diseño: `Diseño/logistica-funcionalidades-maestro.md` sección
8A y `docs/mobile/app-almacen-plan.md` §1 y §3. Este documento cierra solo el **backend previo** (idempotencia, aparatos
y PIN, sincronización por diferencia, código de barras, operaciones atómicas). La app instalable (`app-almacen/`, Expo)
y la app de choferes (Lote 8B) no se tocan en este lote.

## Mapa de lo construido

| Pieza | Tablas / columnas | Código principal | Endpoints |
|---|---|---|---|
| P0 — base compartida | `UserDevice` (código, plataforma, código de registro con hash y vencimiento, secreto con hash, almacén y tema por defecto, último usuario/visto), `UserPin` (hash, intentos, bloqueo; único por `TenantId+UserId`), `Tenant.DeviceSessionDays` (30 por defecto), `IntegrationMessageLog` ampliada (`UserId`, `RequestHash`, `ResponseJson`, `Method` + índice único filtrado `UX_IntegrationLog_Idem`), `RefreshToken.UserDeviceId`; seed: `LookupCode.UiTheme` (LIGHT/DARK), `EntityType.USER_DEVICE`, permisos `devices.manage` y `warehouse.count.capture` | `IdempotencyRules`, `PinRules` (puros, con pruebas), `IdempotencyMiddleware`, configuraciones EF de `UserDevice`/`UserPin` | Sin endpoints propios; el middleware de idempotencia es transversal (después de autenticación y del contexto de tenant) |
| P1 — aparatos, PIN, login por aparato | (usa las tablas de P0) | `DeviceService`, `PinService`, `AuthService` (ampliado con login por aparato y `did` en el JWT), `DeviceRateLimits` | `GET/POST/PATCH /api/v1/devices`, `.../deactivate`, `.../reactivate`, `.../enroll-code`; anónimos: `POST /devices/enroll`, `POST /auth/device/users`, `POST /auth/device/login`, `POST /devices/heartbeat`; `PUT/DELETE /api/v1/me/pin`, `PUT/DELETE /api/v1/users/{id}/pin` |
| P2 — sincronización, código de barras, operaciones atómicas | Sin tablas nuevas (la sincronización por diferencia usa `AuditLog`/`EntityStatusHistory` existentes, no `UpdatedAtUtc`; ver decisión 1) | `SyncService` + `SyncRules` (puro, con pruebas: cursor opaco, tope de página, `since`), `ProductService.GetByBarcodeAsync`, `ReceiptService` (`Confirm` atómico), `PickBatchService` (`collect-and-pack`), `CycleCountService` (captura en lote y conteo a ciegas) | `GET /api/v1/sync/products|bins|purchase-orders|asns|warehouse-tasks|product-categories`, `GET /api/v1/products/by-barcode/{code}`, `POST /api/v1/receipts` (`confirm:true`), `POST /api/v1/pick-batches/collect-and-pack`, `PUT /api/v1/cycle-counts/{id}/lines/batch` |

Todo bajo el filtro de tenant existente (`ITenantScoped`); `USER_DEVICE` es un recurso cerrado (sin campos personalizados,
resolver que siempre da 404). El middleware de idempotencia cubre POST/PUT/PATCH/DELETE salvo los prefijos que devuelven
secretos en claro (decisión 6 ter).

## Cómo se prueba

1. `dotnet build Teikem.sln` — compiló sin errores (2 advertencias `xUnit2012` preexistentes en `RouteWriterTests.cs`,
   ajenas a este lote; verificado en este entorno).
2. `dotnet test Teikem.sln` — **1981 pruebas, 0 fallidas** (verificado en este entorno). Del lote: `IdempotencyRulesTests`
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
   cuerpo → 409, clave inválida → 400, en vuelo → 409); `GET /sync/products|bins|purchase-orders` (sin `purchasing.view`
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

10. **Datos técnicos del aparato sin control de concurrencia.** `LastSeenUtc`, `LastUserId` y `AppVersion` se escriben
    con `ExecuteUpdate` (sin RowVersion ni auditoría) y el login los toca ANTES de emitir los tokens: un heartbeat y un
    login simultáneos daban 500 y dejaban refresh tokens huérfanos. A revisar: aun así, cualquier `UPDATE` sube la
    `ROWVERSION`, de modo que un `PATCH`/desactivar del administrador sin `rowVersion` puede recibir 409 si un heartbeat
    cae entre su lectura y su guardado (ventana de milisegundos; reintentar basta).

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

## Lo que queda fuera de este lote (a propósito)

- **La app instalable de almacén** (`app-almacen/`, Expo/React Native): este lote es solo el backend previo
  (`docs/mobile/app-almacen-plan.md` §3); la app en sí (pantallas, escáner DataWedge, base local SQLite, cola de salida)
  no se construye aquí.
- **Lote 8B (app de choferes)**: reutiliza este backend (aparatos, PIN, idempotencia, sincronización) pero no se toca en
  este cierre. `DriverDevice` se conserva sin cambios; `UserDevice` es la tabla nueva para aparatos de almacén.
- **Columna `UpdatedAtUtc`** en `Product`, `ProductCategory`, `WarehouseBin`, `WarehouseZone`, `PurchaseOrder`, `Asn` y
  `WarehouseTask`: no se agregó (decisión 1); la sincronización usa `AuditLog`/`EntityStatusHistory` en su lugar.
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
