# Lote 2 — Clientes y contratos: qué se construyó y decisiones a revisar

Plan aprobado: `docs/lote2-plan.md` (y su espejo `docs/lote2-plan.json`). Manual del usuario: `docs/manual/02-clientes-y-contratos.md`;
mensajes de error y qué hacer ante cada uno: `docs/manual/faq.md` (sección "Lote 2").

## Mapa de lo construido

| Área | Tablas (nuevas o extendidas) | Código principal | Endpoints (módulo · permiso) |
|---|---|---|---|
| Clientes | `Client` (+ `ClientAssignsOrderNumber`, `ClientAssignsInvoiceNumber`, `OrderNumberFormat`, `InvoiceNumberFormat`, `PackageNumberFormat`, `DefaultPickupLocationId`, `CK_Client_CreditLimit`), `ClientContact` (+ `UX_ClientContact_Primary`) | `ClientService`, `ClientCode`, `NumberFormat`, `CurrentContractRule`, `ClientQueries`, resolvers `CLIENT`/`CLIENT_CONTACT` | `GET/POST /api/v1/clients`, `GET /clients/number-format/preview`, `GET /clients/{publicId}`, `PATCH .../profile`, `PATCH .../number-settings`, `GET/POST .../contacts`, `PATCH .../contacts/{id}`, `POST .../status`, `POST .../deactivate`, `POST .../reactivate` (CATALOG · `clients.read/create/update`) |
| Consignatarios y localizaciones | `Location` (+ `AllowDupInvoice`) | `LocationService`, resolver `LOCATION` | `GET/POST /api/v1/locations`, `GET/PATCH /locations/{publicId}`, `POST .../deactivate`, `POST .../reactivate` (CATALOG · `locations.read/create/update`) |
| Contratos | `Contract` (+ 5 banderas `Bill*`, `DispatchFee`, `CodFeeTypeLookupId`, `CodFeeValue`, `CK_Contract_Fees`, `CK_Contract_Dates`), `ContractServiceLevel` (+ `UX_ContractServiceLevel`) | `ContractService`, `ContractRules`, `ContractStatusEffect`, `BillingModelSummary`, resolver `CONTRACT` | `GET/POST /api/v1/contracts`, `GET/PATCH /contracts/{publicId}`, `PATCH .../billing-model`, `PATCH .../dispatch-fee`, `PATCH .../cod-fee`, `PUT .../service-levels`, `POST .../status` (CATALOG · `contracts.read/create/update`) |
| Tarifas y cotización | `RateComponent` (+ `TenantId`, `ContractId`, `PackageTypeLookupId`, `EffectiveFrom/To`, `RateCardId` NULL, `UQ_RateComponent_Open`, CHECKs), `RateTier` (+ `EffectiveFrom/To`, CHECKs) | `RateService`, `RateTierRules`, `ContractRateResolver` (motor puro), `EffectiveDated` | `GET/POST /api/v1/contracts/{publicId}/rate-components`, `PATCH .../{id}`, `POST .../{id}/close`, `POST .../{id}/tiers`, `PATCH .../{id}/tiers/{tierId}`, `POST .../{id}/tiers/{tierId}/close`; `POST /api/v1/billing/contract-rate-quote` (CATALOG · `contracts.read/update`) |
| Servicios especiales | `SpecialServiceType` (nueva, `UQ_SpecialServiceType`), `SpecialService` (nueva, `UQ_SpecialService_Open`, CHECKs) | `SpecialServiceService`, `SpecialServiceRules` | `GET/POST /api/v1/clients/{publicId}/special-services`, `PATCH .../{id}`, `POST .../{id}/close`, `GET /api/v1/special-service-types`, `POST /special-service-types/{id}/deactivate|reactivate` (CATALOG · `contracts.read/update`) |
| Usuarios de portal | `PortalUser` (mapeada; seed de `PortalUserStatus` reordenado: INVITED inicial, SUSPENDED lateral nuevo) | `PortalUserService`, `PortalUserStatusEffect`, `IInvitationSender` + `LoggingInvitationSender` | `GET /api/v1/clients/{publicId}/portal-users`, `POST .../invite`, `POST .../{id}/resend-invite`, `.../suspend`, `.../reactivate`, `.../remove` (CLIENT_PORTAL · `portalusers.manage`); `POST /api/v1/portal-users/accept-invite` (anónimo) |
| Análisis | — | `ClientDataSource`, `ContractDataSource`, `LocationDataSource`; `SystemAnalyticsSeeder` (vista "Clientes", indicador "Clientes activos", gráfico "Contratos por estatus") | fuentes `CLIENT`, `CONTRACT`, `LOCATION` en `/api/v1/analytics/*` |
| Transversal | seed: `EntityType` RATE_COMPONENT/SPECIAL_SERVICE, `RateComponentType` EXTRA_PIECE, `PermissionCategory` CLIENTS, `Capability` EDIT_CONTRACT, `StatusCapability` por defecto (EXPIRED/CANCELLED sin EDIT_CONTRACT), 10 permisos (48 en total) y plantillas | `DbExtensions` (transacción bajo la estrategia de reintentos, 409 por unicidad/concurrencia, `rowVersion` opcional), `PermissionSeeder` (propagación a roles clonados), `PermissionCatalog.CodesToPropagate` | — |

Todo `TenantId` sale del principal; las entidades nuevas con `TenantId` reciben el filtro global. Las hijas sin `TenantId`
(`ClientContact`, `ContractServiceLevel`, `RateTier`) se alcanzan solo a través del padre del tenant.

## Cómo se prueba

1. **BD limpia** y `dotnet run --project src/Teikem.Api -- db-init`. La estructura se aplica **una sola vez por hash**
   (`SqlScriptRunner`): una BD creada con el Lote 1 no recibe las columnas/tablas nuevas y debe recrearse. El seed sí se
   reaplica al cambiar (MERGE + `UPDATE` idempotente de `PortalUserStatus`). Un segundo `db-init` es idempotente
   (CI lo ejecuta dos veces: rama "ya existe" de `DemoTenantSeeder`, seeders por nombre, 0 permisos propagados).
2. `dotnet test Teikem.sln` — 119 pruebas de lógica pura: vigencias e intervalos (`EffectiveDated`), resumen de facturación,
   numeración y código de cliente, reglas de contrato (COD, SLA, número, activación, fechas), contrato vigente
   (`CurrentContractRule`), tramos y motor de cotización con los montos de la bitácora ($25 orden mixta; 6.50 + 6.25 + 2.00 = 14.75;
   líneas repetidas), normalización de tipos de servicio especial y propagación de permisos.
3. Con el API arriba: `scripts/smoke.sh http://localhost:5000` recorre el Lote 1 y, antes del paso de sesiones, el bloque
   del Lote 2 (16 pasos): alta compuesta y códigos con sufijo, perfil (un `name` enviado se ignora, R1), numeración (patrón vacío
   = por defecto, las dos preguntas independientes), contactos (principal único, edición), consignatarios (dueño, CORPORATE/BILLING
   únicas en alta **y en reactivación**, almacén por defecto, ventana horaria, `allowDupInvoice` por defecto y por PATCH, baja
   lógica), contrato (5 checkboxes, despacho, COD FIXED cotizado y PERCENT, SLA por reemplazo con baja del ausente y reactivación
   de la misma fila, R7b para despacho, COD, "por servicio" y pieza extra, fechas y "cliente desde", R13/R43 (`autoRenew`,
   `billingTrigger`, moneda, título, notas), lista de contratos por cliente, número explícito duplicado), tarifas por
   servicio y pieza extra (historial, traslape, edición cerrar+abrir, PATCH de tarifa sobre pieza extra y tramos sobre "por
   servicio" → 400), cotización mixta con dos bases de contrato
   7 + 6.25 + 3 + 5 + 5 = 26.25 con `asOf`, cierre conservando historial y guarda por intervalo, servicios especiales (tipos
   compartidos, cierre, baja/reactivación del tipo), estatus (lateral, efectos 422, fecha fin no vence, reemplazo del vigente
   ACTIVE → EXPIRED / ACTIVE → CANCELLED y activación del siguiente, EDIT_CONTRACT=0 en EXPIRED, sin contrato vigente, baja lógica
   de cliente), usuarios de portal (token de un solo uso, el reenvío mata el enlace anterior, 409 neutro en las tres colisiones,
   `POST /users` con correo de portal → 409, `expiresAtUtc` dentro de la ventana configurada, rol desconocido → 404, misma
   respuesta 400 en `accept-invite` exista o no la cuenta o sea interna, eventos PASSWORD_CHANGE de fallo (sin `userId`) y de
   éxito (atribuido), SUSPENDED reversible con TOKEN_REVOKED, DISABLED terminal, reinvitación tras baja), R42, aislamiento entre
   tenants (404 por PublicId ajeno **y por id de hija ajena bajo un padre propio** — contacto, usuario de portal, componente de
   tarifa y tramo —, más la unicidad de correo bidireccional sin oráculo), fuentes de datos y contenido de sistema (vista
   "Clientes" ejecutada, gráfico "Contratos por estatus" con datos, LOCATION con relación Client), RBAC (campos personalizados
   de CLIENT/LOCATION → 403 sin el permiso de edición; contactos, historial y valores → 403 con un rol de solo `orders.view`),
   módulo apagado y auditoría de las 7 entidades nuevas. Validaciones adicionales cubiertas: límite de crédito negativo (alta
   y perfil), COD negativo en la cotización, versión/cierre de servicio especial con fecha anterior al inicio, ficha y filtros
   `locationType`/`search` de localizaciones y buscador libre de clientes. Es re-ejecutable sobre una BD persistente (sufijo `TS`,
   fecha `TODAY` en UTC; la aserción del código autogenerado tolera sufijos hasta `-999`).

> Ejecución real (2026-09-25): `dotnet build`, `dotnet test` (119) y el smoke completo (Lotes 1 y 2) en verde contra SQL Server
> 2022 local, sobre una BD **recreada desde cero** con `db-init` dos veces (la segunda corrida verificó la idempotencia).
> Corrida de CI (`.github/workflows/ci.yml`: build + test + db-init ×2 + smoke sobre BD limpia): ver enlace al final de este documento.

### Ajustes hechos después de la revisión automática (ronda 3, fuera del workflow)

La última ronda de corrección y la de documentación se cortaron por la cuota de la sesión; estos hallazgos se cerraron a mano:

1. `accept-invite` verifica también que la compañía esté activa (`Tenant.IsActive`), con la misma respuesta neutra `400`.
2. Principio #8: una nueva versión o un cierre de tarifa por servicio, tramo o servicio especial no se fecha en el pasado
   (`400` "La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe."). El alta sí admite fechas pasadas.
3. En tarifas, la pertenencia del componente/tramo se comprueba antes que la regla de negocio: un id ajeno responde `404`,
   nunca `409` por "componente apagado" (sin oráculo entre tenants; lo cubre el paso de aislamiento del smoke).
4. `scripts/smoke.sh`: el mensaje de `fail` va a stderr para que no se pierda dentro de una sustitución de comando.
5. Se descartó un añadido al documento maestro hecho por un agente: el documento maestro es de Luis y no se edita desde un lote.

## Decisiones tomadas (revisar)

Las 30 primeras vienen del plan aprobado (`docs/lote2-plan.md`, sección "Decisiones") y se resumen; las siguientes salieron de la
revisión de los jueces sobre el código entregado.

1. **Alta compuesta**: `POST /clients` crea el cliente y, si viene `contract`, su contrato inicial (DRAFT, "por servicio" encendido,
   número `{Code}-C1`, SLA opcional) en una sola transacción. `POST /contracts` es para contratos adicionales.
2. **Código de cliente** opcional, autogenerado desde el nombre (mayúsculas sin acentos, máx. 20) con sufijo `-2`, `-3`… si choca.
   `Name` no se edita nunca (identidad, R1): para corregirlo se da de baja el cliente y se crea otro.
3. **Numeración por cliente**: dos preguntas independientes (`ClientAssignsOrderNumber`, `ClientAssignsInvoiceNumber`) y tres patrones
   (`#` = dígito, `@` = letra de serie); NULL = patrón por defecto. El contador atómico llega con Órdenes.
4. **Direcciones del cliente en `Location`** (ratificado por Luis): CORPORATE = física (una activa), BILLING = postal (una activa;
   sin ella, "misma que la corporativa"), PICKUP/BOTH = almacenes, DELIVERY = consignatarios, `ClientId` NULL = compartida del
   operador. `Client.DefaultPickupLocationId` apunta a un PICKUP/BOTH; NULL = se recoge en la corporativa.
5. **`RateComponent` cuelga del contrato** (`ContractId`, `TenantId`, servicio+paquete); `RateCard`/`RateZone`/`RateRule` no se mapean.
6. **Historial efectivo-fechado por fila** (`DATE`, `EffectiveTo` exclusivo): editar = cerrar + abrir; quitar = cerrar; dos ediciones el
   mismo día dejan una fila de longitud cero. Índices únicos filtrados sobre filas abiertas; CHECKs de rango y fechas.
7. **Tarifa genérica del tenant** = `RateComponent` con `ContractId` NULL (comodines NULL); la lee el resolver, sin endpoints todavía.
8. **Cotización mínima** `POST /billing/contract-rate-quote` multi-línea con `asOf`: base + pieza extra GRADUATED marginal + despacho
   (una vez) + COD; despacho/COD en la primera línea; redondeo a 4 decimales AwayFromZero. Zonas, recargos y MinCharge quedan fuera.
9. **Cargo por COD** (Luis): fijo o por ciento **del monto COD cobrado**; sin "base" configurable; "ninguno" = checkbox apagado.
10. **Contrato vigente** (Luis): ACTIVE con `StartDate <= fecha`; la fecha fin no se evalúa; si no hay ACTIVE, el DRAFT más reciente;
    nunca EXPIRED/CANCELLED. Un solo ACTIVE por cliente y nunca con el cliente SUSPENDED (`ContractStatusEffect`, 422).
11. **Sin vencimiento ni renovación automáticos** (Luis): EXPIRED solo por transición manual.
12. **Tipos de servicio especial en tabla propia por tenant** (`SpecialServiceType`), comparados normalizados.
13. **`PortalUserStatus` reordenado**: INVITED (inicial) → ACTIVE, SUSPENDED (lateral), DISABLED (terminal); `UPDATE` idempotente en el seed.
14. **Suspensión reversible** (Luis): `suspend` → SUSPENDED, `reactivate` → ACTIVE conservando la contraseña; `remove` → DISABLED. Sin `UserTenant` (R39).
15. **Invitación = token de restablecimiento de Identity** (DataProtector, `Portal:InviteHours`, un solo uso); `IInvitationSender` solo escribe en el log
    (nunca el token); el token viaja en la respuesta solo con `Portal:ReturnInviteTokenInResponse=true` (Development/smoke).
16. **`accept-invite` anónimo** con la misma respuesta 400 exista o no la invitación, verificación del módulo CLIENT_PORTAL y `SecurityEvent` FAILURE.
17. **Códigos de permiso** de la especificación (`clients/locations/contracts .read/.create/.update`, `portalusers.manage`, categoría CLIENTS).
18. **Capacidad EDIT_CONTRACT** sembrada como no permitida en EXPIRED/CANCELLED; toda escritura de contrato/tarifas/especiales la exige (422);
    configurable por tenant en `/status/capabilities/CONTRACT`.
19. **Módulos**: clientes/consignatarios/contratos bajo CATALOG; usuarios de portal bajo CLIENT_PORTAL. No se crea `ModuleKeys.Clients`.
20. **Transacciones bajo la estrategia de reintentos** (`DbExtensions.RunInTransactionAsync`), 409 por unicidad/concurrencia y `rowVersion`
    opcional en los PATCH de cliente, contrato y localización.
21. **Guardas de última línea en SQL** (índices únicos filtrados y CHECKs) además del servicio.
22. **Regla tenant-security**: las hijas sin `TenantId` se alcanzan solo por el padre; `IgnoreQueryFilters` solo en `accept-invite` y en la
    revocación de refresh tokens.
23. **Adjuntos de contrato** (R17) fuera: sin proveedor de blob; `ContractDocument` no se mapea.
24. **Fuera por pertenecer a otros módulos**: verificación de crédito, chequeo de factura duplicada (la columna `AllowDupInvoice` sí se entrega),
    entrega especial/DriverTrip, congelación en corridas, claim `cid` y login del portal.
25. **Un correo no puede ser interno y de portal a la vez** (ratificado por Luis; `RequireUniqueEmail`), **en los dos sentidos**: 409 al invitar
    al portal un correo ya registrado (interno o de portal de cualquier tenant) y 409 al dar de alta como interno (`POST /users`) un correo de
    portal (`UserAdminService.CreateUserAsync` comprueba el `UserKind` de la cuenta existente antes de adjuntar la membresía; `EnsureMemberAsync`
    y `GetUsersAsync` excluyen cuentas PORTAL, así que `/users/{id}` responde 404 sobre ellas y R39 se cumple aunque exista una membresía
    espuria). Ver también la decisión 42.
26. **`Location.GeoPoint` no se mapea** (sin NetTopologySuite).
27. **`Contract.CodCommissionPct` se conserva** sin exponerse; `PortalUser` se mapea en este lote sin mover la tabla.
28. **Admin de plataforma atribuido** en AuditLog/SecurityEvent del tenant (R42), sin columnas nuevas.
29. **Contenido de sistema** (vista, indicador, gráfico) sembrado para tenants nuevos y re-sembrado para el demo en la rama "ya existe".
30. **Idioma**: identificadores en inglés; comentarios, mensajes, documentos y commits en español. Manual funcional obligatorio por lote.

Decisiones nuevas de la ronda de revisión (hallazgos confirmados y corregidos):

31. **Guarda de duplicado por intervalo, no solo por "fila abierta"** (documento L639/L640, principio #8). Un alta de tarifa por servicio,
    de tramo o de servicio especial choca con toda fila del mismo par (o tipo) que no haya terminado antes de su `EffectiveFrom`
    (`EffectiveTo` NULL o posterior). Así un cierre con fecha futura o un alta con vigencia pasada ya no dejan dos filas vigentes el mismo
    día (antes la cotización respondía 500 por empate). Los índices `UQ_*_Open` siguen como segunda barrera sobre filas abiertas.
    Mensaje: "Ya existe una tarifa vigente en esa fecha para ese servicio y tipo de paquete…" (409). *Alternativa descartada*: solo abiertas.
32. **"Cliente desde" (`Contract.StartDate`) es editable** por `PATCH /contracts/{id}` (documento L606: "todos editables inline"); se valida
    `EndDate >= StartDate` sobre el valor efectivo (400 en `startDate` o `endDate` según lo que se envíe). El plan lo había omitido.
33. **Cotización: líneas repetidas se rechazan con 400** ("El par servicio/tipo de paquete está repetido; envíe una sola línea…"), en vez de
    consolidarlas: Facturación deberá agrupar `packages[]` por (servicio, tipo de paquete) antes de llamar (documento L1096). *Alternativa*:
    sumar piezas en el motor; descartada para que el llamador no oculte errores de agrupación.
34. **Patrones por defecto con 5 dígitos** (`ORD-#####`, `FAC-#####`, `PQT-#####`), como fijó el plan aprobado, en vez de `FAC-####`/`PQT-####`
    del mock (documento L833): uniformidad con el número de orden y menos riesgo de desbordar. Anotado en la bitácora del documento maestro y
    en el manual (vista previa `FAC-00001`, `PQT-00001`). *Alternativa*: 4 dígitos como el mock.
35. **Propagación de permisos nuevos a roles clonados**: el criterio de "nuevo" ya no es "código que no existía en `dbo.Permission`" (siempre
    vacío, porque el seed SQL espeja el catálogo y corre antes) sino "código que entra a una plantilla de sistema en esta corrida". Solo agrega
    (`PermissionCatalog.CodesToPropagate`, probado con xunit) y no re-agrega códigos viejos que un admin haya quitado de su rol. En BD limpia
    propaga 0 (los clones nacen completos). *Alternativa*: quitar el bloque y ajustar roles a mano al actualizar.
    **Sin verificar bajo prueba**: la rama EF (`PermissionSeeder.cs`, consulta con `IgnoreQueryFilters` + inserción de `RolePermission`) solo corre
    con `newCodes > 0` y roles de tenant existentes, y ninguna corrida de CI (BD limpia ×2) ni el smoke (que arranca después) la alcanza; como el
    seed SQL espeja las plantillas antes del seeder, en el flujo normal `newCodes` es 0 y la rama es una red de seguridad para cuando el seed no
    se actualice. Queda pendiente para el primer lote que agregue permisos: prueba de integración (borrar un `RolePermission` de plantilla y de
    clon, volver a sembrar y comprobar que reaparece) o un tercer paso de CI con `sqlcmd`.
36. **SLA del alta compuesta** usa `ContractRules.ValidateServiceLevels` (mismos mensajes que `PUT /service-levels`, campo `contract.serviceLevels[i].*`);
    un tipo de servicio desconocido es 400 (no 404). Desaparece la copia privada de `ClientService`.
37. **Fechas de contrato** centralizadas en `ContractRules.ValidateDates` (alta, contrato inicial del cliente, edición) con prueba unitaria.
38. **Contrato vigente**: las dos copias en memoria (lista de clientes y fuente de análisis) delegan en `CurrentContractRule.Pick` (Domain,
    probado con xunit); la versión EF de `ClientQueries` queda cubierta por el smoke ("Sin contrato": desempate DRAFT y nunca CANCELLED).
39. **Cliente dado de baja (`IsActive = 0`)**: desaparece de la lista (salvo `includeInactive`), su ficha sigue accesible y se puede reactivar.
    **No se restringen** todavía sus escrituras (invitar usuarios de portal, servicios especiales, cotizar): queda como pendiente de decisión
    para Luis; el cambio mínimo sería un 409 "El cliente está dado de baja." en las rutas de escritura.
40. **Tipos de servicio especial con baja lógica** (documento L214: mismo principio que chofer o vehículo, nunca DELETE):
    `POST /special-service-types/{id}/deactivate` y `.../reactivate` con `contracts.update`. La baja exige que ningún cliente tenga una tarifa
    abierta de ese tipo (409 "El tipo tiene tarifas vigentes en N cliente(s)…"); un tipo inactivo sale del selector (`includeInactive=true` lo
    muestra), no se puede usar por `typeId` (400 "…está inactivo; reactívelo o elija otro.") y `newTypeName` que coincida con él lo reactiva solo
    (coherente con no proliferar variantes). Antes de esta ronda no existía forma de retirar un tipo creado por error.
41. **Smoke**: `GET /audit/activity` acepta `kind=all|changes|security` (el plan decía `change`); la verificación por entidad usa
    `GET /audit/changes?entityType=…`. El paso del CSV del Lote 1 usa `grep -c` (con `grep -q` la tubería se cerraba antes de tiempo en CSV
    grandes y `pipefail` lo marcaba como error en BD persistentes).

42. **409 neutro al invitar al portal** (oráculo entre tenants): las tres colisiones de correo (usuario de portal del propio tenant, usuario de
    portal de otro tenant, usuario interno) responden el mismo `Ese correo no está disponible para el portal de esta compañía.` (antes el
    mensaje distinguía "en esta compañía" de "de la plataforma" y revelaba si un correo existía en otro tenant). Se registra `SecurityEvent`
    ROLE_CHANGE/FAILURE `portal_invite_conflict` (sin la causa) para detectar enumeración masiva. Residual aceptado: 409 vs 200 sigue revelando
    la existencia global del correo, consecuencia de `RequireUniqueEmail`. *Alternativa* (fuera de alcance): aceptar silenciosamente y fallar
    solo al aceptar, o unicidad por `UserKind`/tenant.
43. **Reenviar la invitación invalida el enlace anterior**: `ResendInviteAsync` rota el `SecurityStamp` (`UpdateSecurityStampAsync`) antes de
    generar el token nuevo, porque el token DataProtector de Identity lleva el stamp embebido; antes convivían dos enlaces válidos hasta
    vencer (documento L380/L403: "un solo uso"). En INVITED no hay contraseña ni sesiones, así que no hay otro efecto. Verificado en el smoke
    (el token viejo responde 400 tras el reenvío).
44. **Reinvitación tras baja** (documento L382/L387, flujo "quitar → agregar de nuevo"): invitar un correo cuya fila `PortalUser` está en
    DISABLED **del mismo cliente** reutiliza la fila y la cuenta (`IsActive=1`, `EmailConfirmed=0`, rol y nombre nuevos) y registra un
    nacimiento `null → INVITED` en `EntityStatusHistory` (StatusService solo exige etapa inicial; el historial DISABLED anterior se conserva).
    `SecurityEvent` `portal_reinvite`. Desde otro cliente del tenant sigue siendo 409 (mover a alguien de cliente es decisión de negocio,
    pendiente de Luis). *Alternativa descartada*: baja irreversible por correo, que obligaba a inventar un correo distinto para la misma persona.
45. **Chequeo de contraseñas en brecha**: el manual del lote prometía "no puede estar en brecha" al aceptar la invitación, pero el único
    `IPasswordBreachChecker` registrado es no-op (decisión del Lote 1). Se corrige la documentación (manual 02 §7 y FAQ) en vez de implementar
    HIBP ahora. *Alternativa*: `HibpPasswordBreachChecker` por k-anonimato condicionado por configuración (`Passwords:BreachCheck`).
46. **`ContractRateResolver` usa `PricingTypes.Fixed/Percent`** en lugar de literales, para que validador, lookup y motor compartan la misma
    fuente de verdad (el smoke ahora cotiza COD FIXED end-to-end: $2.00 fijos, bitácora L1113).
47. **Permiso de la entidad dueña en las rutas polimórficas** (hallazgo de revisión): al registrar los resolvers CLIENT/CLIENT_CONTACT/LOCATION/
    CONTRACT, `PUT /custom-fields/values/{entityType}/{id}` se convirtió en la vía oficial para campos personalizados de clientes sin la segunda
    capa de la defensa en profundidad. `PermissionCatalog.OwnerWritePermission` (CLIENT/CLIENT_CONTACT → `clients.update`, LOCATION →
    `locations.update`, CONTRACT → `contracts.update`, PORTAL_USER → `portalusers.manage`) se exige en `CustomFieldService.SetValuesAsync`, y
    `PermissionCatalog.OwnerReadPermission` (los `.read` equivalentes) en `CustomFieldService.GetValuesAsync`, `ContactPointService.GetForOwnerAsync`
    y `StatusService.GetHistoryAsync`, vía `PermissionService.EnsureAsync` (403 + PERMISSION_DENIED). Entidades sin entrada (USER) conservan
    "cualquier autenticado". Se hace en el servicio, no en el controlador, para cubrir cualquier llamador futuro. *Alternativa*: `WritePermission`
    declarado por cada `IOwnedEntityResolver`.
48. **Suelo común de `Portal:InviteHours`**: `PortalUserService` usa `Math.Max(1, …)` igual que `TokenLifespan` en `DependencyInjection`, para que
    `expiresAtUtc` y la vida real del token no puedan divergir. La caducidad real del token no se prueba (ver "Lo que queda fuera").

## Checklist tenant-security del lote (revisión estática)

- [x] Ningún `IgnoreQueryFilters` fuera de `PortalUserService.AcceptInviteAsync` (sin tenant en el principal), de la revocación de refresh
      tokens en `PortalUserStatusEffect` y de los seeders/`AuthService`/`UserAdminService` del Lote 1.
- [x] Una cuenta de portal (de este u otro tenant) no se adjunta como usuario interno ni se edita desde `/users` (decisión 25, verificado en
      el smoke desde el propio tenant y desde otro).
- [x] `ClientContact`, `ContractServiceLevel` y `RateTier` (sin `TenantId`) solo se consultan por `ClientId`/`ContractId`/`RateComponentId`
      del padre resuelto bajo el filtro global (`ClientQueries.ResolveClientAsync`/`ResolveContractAsync`), nunca por id suelto; verificado
      end-to-end en el smoke: padre del tenant nuevo + id de hija del tenant demo (contacto, usuario de portal, componente, tramo) → 404.
- [x] Toda escritura toma `TenantId` de `TenantContext.RequireTenantId()`; las rutas usan `PublicId` (GUID) para cliente, contrato y localización.
- [x] Resolvers de pertenencia registrados para `CLIENT`, `CLIENT_CONTACT`, `LOCATION` y `CONTRACT` (contactos y campos personalizados
      responden 404 para ids de otro tenant; verificado en el smoke) **y** permiso de la entidad dueña en las rutas polimórficas (decisión 47:
      escribir campos personalizados de CLIENT/LOCATION sin `clients.update`/`locations.update` → 403; leer contactos, historial y valores
      con un rol de solo `orders.view` → 403; verificado en el smoke).
- [x] El token de invitación no se registra en logs (`LoggingInvitationSender`), no viaja en `AuditLog` (no es entidad) y solo se devuelve
      en la respuesta con `Portal:ReturnInviteTokenInResponse=true`; el smoke comprueba que ningún `changesJson` de `RATE_COMPONENT` contiene "token".
- [x] Aislamiento verificado end-to-end: un tenant nuevo recibe 404 por `PublicId` ajeno, 404 por id de hija ajena bajo un padre propio y listas vacías.

## Lo que queda fuera de este lote (a propósito)

- Zonas (`RateZone`/`RateZoneMember`), recargos (`RateRule`), `MinCharge` y evaluación con el DSL en la cotización.
- Endpoints de la tarifa genérica del tenant (`ContractId` NULL) — los agrega Facturación.
- Adjuntos de contrato (`ContractDocument`) y proveedor de blob.
- Verificación de crédito (R18) y chequeo de factura duplicada con `AllowDupInvoice` (R36) — Órdenes.
- Entrega especial/`DriverTrip` y tablas de liquidación de choferes (R37).
- Contador atómico de numeración por cliente (siguiente consecutivo) — Órdenes.
- Login del portal, claim `cid`/`ITenantContext.ClientId` y filtro por cliente (R44) — módulo 10.
- Vencimiento/renovación automáticos de contratos (decisión de Luis: no existen); un aviso de "fecha fin pasada" sería un indicador.
- Restricción de escrituras sobre un cliente dado de baja (decisión 39, pendiente de Luis).
- Reinvitar a un usuario de portal dado de baja desde **otro** cliente del mismo tenant (decisión 44, pendiente de Luis).
- Prueba de integración de la propagación de permisos a roles clonados (decisión 35).
- Caducidad real del token de invitación (R41): la valida el `DataProtectorTokenProvider` de Identity con `Portal:InviteHours` (suelo 1 h), no
  comprobable en el smoke; este solo afirma que `expiresAtUtc` anuncia la ventana configurada (48 h).
