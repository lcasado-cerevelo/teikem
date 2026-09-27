# Lote 8A — Backend previo de la app de almacén: decisiones a revisar

Plan aprobado: `docs/lote8A-plan.json`. Diseño: `Diseño/logistica-funcionalidades-maestro.md` sección 8A y
`docs/mobile/app-almacen-plan.md` §1 y §3. Este documento arranca con las decisiones que cambiaron durante la
verificación del lote; el cierre del lote lo completa con el mapa de lo construido y cómo se probó.

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
   `IntegrationMessageLog.ResponseJson` la dejaría 7 días en la bitácora.

6 bis. **Invitaciones de portal fuera de la idempotencia.** `POST /api/v1/clients/{id}/portal-users/invite` y
   `.../portal-users/{id}/resend-invite` devuelven el token de invitación (en Development): se excluyen por sufijo bajo
   `/api/v1/clients/` (`IdempotencyRules.ExcludedClientPathSuffixes`). Alternativa a revisar: un atributo
   `[SkipIdempotency]` en el endpoint, más robusto ante rutas futuras.

6 ter. **Idempotencia: desviaciones del plan P0-5 (pendiente de ratificar por Luis).** El plan pedía idempotencia en
   todo POST/PUT/PATCH/DELETE con la cabecera, huella del cuerpo, guardar toda respuesta menor a 500 y 409 siempre para un
   registro en vuelo. El código se aparta en cuatro puntos:
   - (a) **Prefijos excluidos** `/api/v1/auth`, `/api/v1/me`, `/api/v1/devices` y `/api/v1/platform` (además de
     `/api/v1/users` y las invitaciones de portal, decisiones 6 y 6 bis): devuelven tokens, secretos del aparato, códigos
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

7. **`Tenant.DeviceSessionDays` configurable** en `GET/PUT /api/v1/tenant/settings` (`deviceSessionDays`, 1 a 365; 400
   `Entre 1 y 365 días.`).

8. **Contador del PIN atómico.** El fallo se cuenta con un `UPDATE` en SQL (`FailedCount + 1`, bloqueo al 5.º)
   condicionado a que la fila no esté bloqueada; el acierto pone el contador en 0 con la misma condición (si otro
   intento ya bloqueó, 423 también con el PIN correcto). Antes el contador se leía y reescribía en memoria y 20 intentos
   en paralelo dejaban `FailedCount = 1` sin bloqueo. Se prefirió al bloqueo de fila con transacción porque
   `RunInTransactionAsync` limpia el rastreador y desconectaría el `UserDevice` que usa el login.

9. **Datos técnicos del aparato sin control de concurrencia.** `LastSeenUtc`, `LastUserId` y `AppVersion` se escriben
   con `ExecuteUpdate` (sin RowVersion ni auditoría) y el login los toca ANTES de emitir los tokens: un heartbeat y un
   login simultáneos daban 500 y dejaban refresh tokens huérfanos. A revisar: aun así, cualquier `UPDATE` sube la
   `ROWVERSION`, de modo que un `PATCH`/desactivar del administrador sin `rowVersion` puede recibir 409 si un heartbeat
   cae entre su lectura y su guardado (ventana de milisegundos; reintentar basta).

10. **Límite de intentos de los endpoints anónimos del aparato** (limitador de .NET 8, sin paquetes): ventana fija de 1
    minuto por IP y ruta; `enroll` 10, `device/users`, `device/login` y `heartbeat` 60 (configurables en
    `RateLimiting:*`); 429 `Demasiados intentos; espere un minuto e intente de nuevo.`. A revisar: detrás de un proxy
    inverso la IP es la del proxy salvo que se configure `UseForwardedHeaders`; y el login con contraseña no se limitó
    (conserva el bloqueo por cuenta de Identity y el smoke/Playwright hacen muchos logins desde la misma IP).

11. **`USER_DEVICE` con resolver cerrado** y `devices.manage` como permiso de dueño (lectura y escritura) en la ruta
    polimórfica de campos personalizados; antes quedaba abierta a cualquier autenticado.

12. **`GET /api/v1/sync/purchase-orders` exige el módulo PURCHASING y `purchasing.view`** (desviación del plan, que
    pedía solo `inventory.view`): con solo `inventory.view` el rol Solo lectura veía órdenes de compra y proveedores que
    la ruta nativa le niega. El Operador de almacén ya trae `purchasing.view`.

13. **Recibo en una llamada contra aviso u orden de compra: lo escaneado manda.** Antes las `lines` se ignoraban en
    silencio y se confirmaba lo esperado. Ahora se aplican sobre las líneas del documento por producto (y lote); lo no
    mencionado queda en 0, lecturas repetidas del mismo producto se suman y un producto fuera del documento entra como
    línea extra. Sin `lines` se recibe lo esperado (R8), como antes.

14. **Conteo a ciegas sin diferencia en el encabezado.** `CycleCountDto.VarianceLines` y `NetVariance` pasan a
    `int?`/`decimal?` y llegan `null` a quien no tiene `warehouse.count` (ficha, respuestas y lista); ponerlos en 0
    diría "todo cuadra", que es falso. `web-app/openapi.json` y los tipos del cliente se regeneraron.
    **Cambio de comportamiento en la web** (el plan decía que la web no cambiaba): en Almacén → Conteos cíclicos los roles
    sin `warehouse.count` (Solo lectura y Facturación) veían la diferencia neta real y, con el API ya cegado, la pantalla
    pintaba 0 en todas las filas. Se corrigió en la web: sin `warehouse.count` la columna "Diferencia neta" no se muestra,
    y `null` se pinta como "—" (lista y ficha), nunca como 0. Queda como cambio visible para esos roles (manual 06 y FAQ).

15. **Vigencia del código de registro (cerrada).** El filtro de vencimiento del enroll se prueba en el smoke: con
    `SMOKE_SQL` se pone `EnrollCodeExpiresUtc` en el pasado, el mismo código (hash correcto) da 401 `El código de registro
    no es válido o venció.` y, al restaurar la vigencia, registra el aparato. Sin `TimeProvider` en `DeviceService`.

16. **Flujos anónimos del aparato sin el usuario de un bearer ajeno.** `TenantContextMiddleware` llena `UserId` con
    cualquier bearer válido aunque el endpoint sea `[AllowAnonymous]`; el registro, la lista de usuarios, el heartbeat y el
    login por aparato cambiaban al tenant del aparato con `As(tenantId)` y conservaban ese usuario, así que un token de
    otra compañía dejaba su `UserId` en el `AuditLog` y en `TOKEN_REVOKED` de la compañía del aparato. Ahora usan
    `TenantContext.AsAnonymous(tenantId)` (tenant del aparato, usuario vacío) y los eventos de fallo de esos flujos se
    escriben sin el tenant ni el usuario del contexto.

17. **La operación con `Idempotency-Key` no se cancela si el cliente se desconecta.** El middleware pone
    `HttpContext.RequestAborted = CancellationToken.None` después de registrar la clave: si el aparato pierde la señal
    después del commit (p. ej. al releer el recibo confirmado en una llamada), la operación termina, su respuesta se guarda
    y el reintento de la cola recibe `Idempotent-Replayed` en vez de crear y confirmar otro recibo. Antes la cancelación
    borraba la clave y el reintento duplicaba la entrada de inventario. Guardar la respuesta se reintenta (3 veces) si
    falla. A revisar: la solución completa es escribir `ResponseCode` en la misma transacción de negocio.

18. **Intentos rechazados del aparato en la bitácora.** `DeviceService.AuthenticateAsync` (device/login y device/users)
    escribe `LOGIN` / `FAILURE` con `stage = device`, `action` y `reason` (`device_invalid`, `device_inactive`,
    `tenant_unusable`) antes del 401, en la compañía del aparato si existe. Antes solo quedaban los fallos de usuario o PIN.
