# Lote 41 — Interbloqueos al escribir rutas con varios despachadores a la vez (2026-10-12)

Defecto real del servidor: con varias personas (o procesos) escribiendo rutas a la vez en la misma compañía, SQL Server
elegía víctimas de interbloqueo («Transaction (Process ID n) was deadlocked on lock resources … chosen as the deadlock
victim») y, tras los 3 reintentos de `SqlServerRetryingExecutionStrategy`, el cliente recibía **500 «Error interno.»**
(`RetryLimitExceededException`). Evidencia: la corrida de regresión de Playwright (log `api29.log`, ~63 líneas «deadlocked»,
3 peticiones en 500: `PUT /trips/{id}/stops/{routeStopId}/location` con correlación `60cb7b2e-8336-4057-a19b-1127da12bf8b`,
una ficha `GET /trips/{id}` y un `GET /trips/monitor`). Sin cambios de base de datos ni de contrato.

## Causa raíz

Se leyeron los informes `xml_deadlock_report` de la sesión `system_health` de SQL Server para esa corrida (base `Teikem_pw`,
**579 interbloqueos** entre las 15:00 y las 17:00 UTC) y se reprodujeron en una base desechable:

- **Todas** las esperas del ciclo eran pedidos de bloqueo **S (lectura)** sobre la llave clúster de **RouteStop** (799) y de
  **OrderStop** (713) —más 16 X del `UPDATE` del pin manual y 3 S sobre `TripOrder`—, y en **todos** los ciclos había al
  menos **dos transacciones de escritura de rutas sobre rutas DISTINTAS**. No era el patrón de «leer sin UPDLOCK y luego
  subir el bloqueo»: era «escribir y DESPUÉS recorrer toda la tabla».
- Cada escritura de rutas (pin manual, reordenar, optimizar, agregar o quitar órdenes, despachar…) corre en una transacción:
  bloquea su Trip (`UPDLOCK`) → escribe (X sobre sus `RouteStop`, o el `UPDATE` de `OrderStop.GeoPoint` del pin) →
  `RouteWriter.RecomputeAsync` vuelve a leer para recalcular las ETAs: las paradas de la versión
  (`SELECT … FROM RouteStop WHERE RouteId = @id`), las ventanas (`OrderStop JOIN TransportOrder WHERE OrderStopId IN
  (OPENJSON …)`) y las coordenadas (`StopPointsAsync`).
- Con pocas filas (una compañía que empieza, y las bases de prueba), SQL Server resuelve esas lecturas con **Clustered Index
  Scan** (comprobado con `SET STATISTICS PROFILE` en la base de prueba: `Clustered Index Scan(… RouteStop …) WHERE RouteId =
  @p` y `Clustered Index Scan(… OrderStop …)` en una semi-join). La base usa READ COMMITTED **con bloqueo**
  (`is_read_committed_snapshot_on = 0`), así que el recorrido pide S fila por fila, **también las de otras rutas**, y se
  detiene en las filas X de la otra transacción, que a su vez está recorriendo y se detuvo en las nuestras → ciclo. El
  `UPDLOCK` del Trip no lo evitaba porque cada una tenía un Trip distinto.
- Las dos operaciones que se cruzaban, en el orden de frecuencia: recálculo de una ruta contra recálculo de otra
  (`LoadStopsAsync` contra `LoadStopsAsync` sobre RouteStop; ventanas contra ventanas sobre OrderStop), con cualquiera de
  pin / reordenar / optimizar / agregar / quitar como origen. Las lecturas de la ficha y del monitoreo entraban como tercer
  participante: al leer GEOGRAPHY (`os.GeoPoint.Lat`), SQL Server **conserva los S de las filas leídas hasta el fin de la
  sentencia**, y recorriendo toda la tabla retenían paradas de otras rutas. Cuando una lectura era la víctima 4 veces
  seguidas, también salía 500 (los dos GET del log).

## Qué se construyó

| Pieza | Resultado | Dónde |
|---|---|---|
| Orden de bloqueo único, paso 1 para todas | `LockTripAsync(int)` toma la **fila Tenant** (`LockTenantPlanningAsync`, U lock) **antes** que el Trip. Así TODA escritura de rutas de una compañía (pin, reordenar, optimizar, agregar/quitar, despachar, iniciar/cerrar, editar/eliminar, reasignar por zona, escaneo de salida, liberar por cancelar la orden) va en fila: la segunda espera a que la primera confirme, sin ciclo. Las sobrecargas por `PublicId` y `LockTripsAsync` pasan por ella. Pedirla otra vez en la misma transacción no espera. Las **lecturas** no la piden | `TripQueries.LockTripAsync` |
| Alta de rutas | `CreateTrackedAsync` (POST /trips y «Planificar el día») toma la fila Tenant antes del número y del INSERT | `TripService` |
| Lecturas después de escribir: solo sus filas | `TripQueries.LoadRouteStopsAsync` (RouteStop/Route/Trip `WITH (FORCESEEK)`, con `TenantId =`) y `TripQueries.StopTimingsAsync` (OrderStop/TransportOrder `WITH (FORCESEEK)`) reemplazan el LINQ de `RouteWriter.LoadStopsAsync` y de las ventanas de `RecomputeAsync`. `StopPointsAsync` y `SetStopPointAsync` (pin) llevan `FORCESEEK` también. Con esto las escrituras de rutas de compañías distintas tampoco se tocan, y la ficha/el monitoreo retienen solo las paradas de su ruta | `TripQueries`, `RouteWriter` |
| Red de seguridad | Un interbloqueo (1205) que sobreviva a los reintentos —directo, o dentro de `RetryLimitExceededException`, `DbUpdateException` o `AggregateException`— ya no es 500: **409 `concurrent_edit`** «Otra persona está modificando esta ruta al mismo tiempo; vuelva a intentarlo.» en `/api/v1/trips…` y «Otra persona está modificando estos datos al mismo tiempo; vuelva a intentarlo.» en el resto; se registra como advertencia con la correlación. El resto de los errores no cambia (otros agotamientos de reintentos siguen en 500; las excepciones de dominio, con su código) | `DeadlockRules` (Infrastructure/Persistence), `ExceptionHandlingMiddleware` |
| Pruebas | `DeadlockRulesTests` (15): detección con una `SqlException` real (1205 sí; 2627, 1222 y −2 no), dentro de los envoltorios, tope de profundidad, mensaje por ruta, cuerpo exacto del 409, otros errores intactos, cancelación no traducida; guardas sobre el código: fila Tenant antes que el Trip en `LockTripAsync` y antes del número en `CreateTrackedAsync`, `RouteWriter` usa las lecturas con `FORCESEEK`. `RawSqlConfinementTests`: 7 → 9 sentencias en `TripQueries`, todas con `TenantId =` | `tests/Teikem.Tests` |

**Por qué la traducción vive en el middleware y no en `RunInTransactionAsync`**: `IdempotencyMiddleware` guarda las
respuestas de las excepciones de dominio (un 409 incluido); si el interbloqueo se convirtiera en `ConflictException` dentro
del servicio, el reintento con la misma `Idempotency-Key` (la cola del aparato) recibiría para siempre el mismo 409 en vez de
volver a ejecutar. Como excepción no de dominio, la clave se libera y el reintento sí se ejecuta.

**El `rowVersion` y sus 409 siguen igual**: `EnsureRowVersion` se valida después de bloquear el Trip y `SaveGuardedAsync`
traduce los choques. Comprobado contra la API de prueba: reordenar con el `rowVersion` vigente → 200; con el viejo → 409
`conflict` «El registro fue modificado por otro usuario; recargue e intente de nuevo.» (también en el pin); optimizar con la
ruta cambiada entre fases → 409 «La ruta cambió mientras se optimizaba; vuelva a optimizar.».

No hizo falta ningún índice ni cambio de datos: `Diseño/logistica-db-update.sql` no se tocó. El contrato no cambió (el 409
es una respuesta del middleware, sin endpoints ni DTOs nuevos): no se regeneró `openapi.json`.

## Cómo se probó

**Prueba de concurrencia contra SQL Server real** (no InMemory): base nueva y desechable `Teikem_dl_test` (`db-init`), API en
Development (`Auth__Onboarding__Enabled=false`) en `:5077`, y un programa Node que crea sus datos (cliente con contrato y
tarifa, consignatarios, órdenes confirmadas, N rutas con sus paradas en un día futuro) y por ronda lanza **en paralelo**:
2 escrituras distintas por cada ruta (pin manual, reordenar, optimizar, quitar y volver a agregar una orden), SAME escrituras
a la vez sobre **la misma** ruta, un alta de ruta y lecturas (ficha, monitoreo, lista). Interbloqueos contados en
`system_health` por ventana de cada corrida.

| Corrida | Rutas × paradas, misma ruta, rondas | Peticiones | Interbloqueos | 500 | Duración |
|---|---|---|---|---|---|
| Antes A (×3) | 8 × 6, 6, 5 | 229–230 | **102, 132, 105** | 0 (los reintentos los absorbieron) | 44–60 s |
| Antes B (×2) | 12 × 8, 10, 6 | 400–401 | **362, 348** | **2 y 2** (pin, quitar orden, optimizar) | 94–124 s |
| Después A (×3) | 8 × 6, 6, 5 | 229–230 | 1, 0, 0 | 0 | 1,6–4,9 s |
| Después B (×11) | 12 × 8, 10, 6 | 399–402 | 0–1 por corrida (5 en total) | 0 | 2,3–7,0 s |
| Después C (×5) | 20 × 10, 16, 10 | 1036–1047 | 0–2 por corrida (4 en total) | 0 | 7,2–11,1 s |

Después del arreglo: **0 respuestas 5xx y 0 «deadlocked» en el log del API** en ~9.900 peticiones (19 corridas); `system_health` registró
10 interbloqueos residuales, todos de una **lectura** (GET ficha/lista/monitoreo, `RouteStop JOIN OrderStop`) contra el
`SaveChanges` de un pin; la víctima fue siempre la lectura y ninguno llegó al cliente. Una de las 11 corridas B se cortó del lado del programa de prueba (sin resumen; el API registró 7
optimizaciones canceladas por la desconexión, sin 500 ni interbloqueos) y se repitió. Los 409 que quedan en la prueba son
los diseñados (optimizar una ruta que otra petición cambió entre fases; ids de paradas viejos tras optimizar → 400/404).

- `dotnet build Teikem.sln` sin errores; `dotnet test Teikem.sln`: **3586 pruebas verdes** (15 nuevas).
- Recorridos Playwright contra la API y la base de prueba (Vite propio en `:5178` con `VITE_API_URL` a `:5077`):
  `loteFE2a-despacho`, `loteFE2b-mapa` y `loteFE3-monitoreo` en **escritorio, en paralelo: 3/3 verdes**. Con escritorio y
  móvil a la vez (6 trabajadores, dos veces) FE2b y FE3 pasan y FE2a falla en ambos proyectos porque los dos recorridos
  eligen **el mismo día** (`offset` por segundo) y cada uno ve las rutas del otro en «Rutas de hoy» (choque de datos del
  recorrido, no del servidor); en esas corridas, 0 interbloqueos y 0 errores 500. Las capturas del manual se restauraron.
- La base `Teikem_dl_test` se borró al terminar.

## Costo

Una ida y vuelta más por transacción de escritura de rutas (el U lock de la fila Tenant por llave primaria; menos de un
milisegundo) y que las escrituras de rutas de una misma compañía van en fila. En la prueba, la fila no se nota: antes cada
interbloqueo costaba ~5 s de detección más el reintento completo; después las mismas rondas tardan 10–20 veces menos.

## Decisiones a revisar

1. **Toda escritura de rutas de una compañía va en fila** (incluye el escaneo de salida y la liberación al cancelar una orden).
   Una operación larga («Planificar el día» con cientos de órdenes) hace esperar a los demás hasta que termine (con el tope del
   tiempo de espera del comando). Las lecturas no esperan.
2. **Se reusa la fila Tenant** (el bloqueo que ya tenían «Planificar el día» y la importación de consignatarios) en lugar de un
   `sp_getapplock` propio: menos piezas y el mismo orden de bloqueo documentado; a cambio, una importación de consignatarios
   (fila por fila, transacciones cortas) y una edición de la ficha de la compañía esperan brevemente detrás de una escritura de
   rutas, y al revés.
3. **READ_COMMITTED_SNAPSHOT (RCSI)** es la solución de fondo para lecturas contra escrituras en toda la aplicación y no se
   activó: necesita `ALTER DATABASE … SET READ_COMMITTED_SNAPSHOT ON` fuera de una transacción (el runner de `db-update` corre
   cada script en una sola transacción) y acceso exclusivo (`WITH ROLLBACK IMMEDIATE`) en producción, y cambia la semántica de
   lectura de todos los módulos. Recomendado evaluarlo como decisión del dueño.
4. **Residuales lectura contra escritura** (10 en ~9.900 peticiones con carga sintética extrema): la lectura es la víctima y EF
   la repite sola; si se agotaran los reintentos, la ficha respondería 409 `concurrent_edit` en lugar de 500.
5. **Las ventanas para recalcular las ETAs ahora se leen solo por compañía** (`StopTimingsAsync`), no con el alcance de datos
   por cliente del usuario: antes, un despachador con alcance por cliente recalculaba ignorando las ventanas de las órdenes de
   otros clientes de la misma ruta; ahora cuentan todas (como ya pasaba con las coordenadas).
6. **Mensaje según la ruta del request**: «esta ruta» en `/api/v1/trips…`, «estos datos» en lo demás.
7. **Otros recorridos dentro de una escritura de rutas no se forzaron** (`TripOrder` por ruta u orden, `Route` por Trip,
   `PendingDeliveryStopsAsync`, `CurrentTripsOfOrdersAsync`, `LockTripAsync(Guid)` resuelve el `PublicId` del Trip sin índice):
   dentro de una compañía ya no chocan por el paso 1; entre compañías distintas podrían, y para eso queda la red de seguridad.
   Un índice `UX_Trip_PublicId` es candidato si aparecen esperas en producción.

## Qué quedó sin verificar

- Concurrencia **entre compañías** (la prueba usa la compañía demo).
- Planes de ejecución con el volumen de producción (con muchas filas el optimizador ya buscaba; `FORCESEEK` lo garantiza).
- Smoke completo y regresión Playwright completa (solo los tres recorridos de despacho).

Manual: `docs/manual/faq.md` («Lote 41 — Rutas editadas por varias personas a la vez»).

## Verificación con varias compañías y con volumen sintético (2026-10-12)

Se verificaron los dos puntos que habían quedado abiertos: **(A) concurrencia entre compañías distintas** y **(B) rendimiento
con un volumen parecido al de producción**. Aclaración: no se usaron datos de producción ni se tocó ninguna base real (SEPHAS,
`Teikem`, `Teikem_verif`, `Teikem_pw`). Todo corrió en una base **desechable** propia (`Teikem_vol_test`, `db-init`) con datos
**sintéticos**; los números son una **aproximación** (planes, lecturas lógicas, comportamiento bajo carga), no cifras de producción.

### Metodología

- **Entorno**: SQL Server 2022 Developer (16.0.1200) local, 32 núcleos, 64 GB, la base cabe completa en memoria (caché caliente);
  READ COMMITTED **de bloqueo** (`is_read_committed_snapshot_on = 0`), igual que producción. API compilada en **Release** fuera del
  repositorio, en Development con `Auth__Onboarding__Enabled=false`: una API en `:5077` (configuración **S**, la real: todas las
  compañías en un proceso) y, para atribuir cada espera a su compañía, **una API por compañía** en `:5081-5084` con
  `Application Name=vol-T0…T3` (configuración **M**; la base es la misma, así que el bloqueo en SQL es idéntico).
- **Compañías**: 4 aprovisionadas por la API de plataforma (además de la demo y 8 casi vacías de intentos previos) (`POST /api/v1/platform/tenants`, módulos LTL_GROUND y CATALOG), cada
  una con 6 zonas (miembro por CP), 22 choferes con licencia y vehículos, cliente con contrato y tarifa activos, consignatarios con CP,
  rutas con chofer y vehículo y órdenes confirmadas, todo creado por la API (`mt.mjs setup`).
- **Carga A** (`mt.mjs load`, extensión de `stress.mjs`): en las 4 compañías **a la vez** y con 3 sesiones por compañía, por ronda:
  2 escrituras por ruta (pin, reordenar, optimizar, quitar y volver a agregar), 4 escrituras simultáneas sobre la misma ruta, alta de
  ruta, «Planificar el día» de una zona, 2 órdenes nuevas, despacho de una ruta, 2 escaneos de salida y lecturas (monitoreo, lista del
  día, «Sin asignar» take 500, lista de órdenes con filtros, ficha). ~1.310 peticiones por corrida. **Operaciones largas**
  (`mt.mjs long`): la compañía 0 hace «Planificar el día» (todas sus zonas, 556 a 1.800 órdenes), confirma una importación de
  **5.000 consignatarios** o edita su ficha (`PUT /tenant/settings` en bucle 8 s) mientras la compañía 3 escribe rutas sin parar; se
  compara la latencia de la compañía 3 durante y fuera de la operación. **Alta de órdenes** (`mt.mjs orders`): 4 compañías × 2
  sesiones × 25 órdenes confirmadas a la vez.
- **Medición**: interbloqueos con una sesión XE propia (`xml_deadlock_report`, latencia 1 s; `system_health` escribe con hasta 120 s
  de retraso); un **vigía** que cada 10 ms guarda las esperas `LCK_*` con quién espera, quién bloquea (su `Application Name` = su
  compañía), recurso y sentencias; lecturas lógicas y CPU **por sentencia** con otra sesión XE (`rpc_completed`) — la métrica
  determinista; las latencias de la API varían hasta ×2 entre corridas iguales porque el SQL Server es compartido con otras sesiones.
- **Volumen (B)**: generador **SET-BASED** en SQL (`gen.sql`, solo en la base desechable) que clona los valores de las filas creadas
  por la API (cliente, contrato, consignatario, zonas, choferes: respeta FKs y catálogos) y agrega órdenes históricas (95 % entregadas,
  5 % canceladas) con su parada, 1-3 líneas, historial de estatus (3-5 filas) y bitácora (2 filas); rutas históricas COMPLETED de
  5 a 25 paradas con `Route`, `RouteStop`, `TripOrder`, historial y bitácora; y el **conjunto de operación constante** en las tres
  escalas: cartera sin asignar, rutas de hoy (1/3 despachadas) y de mañana de 50-75 paradas en varias zonas, y tres rutas de 60, 150 y
  300 paradas para optimizar. Reparto: una compañía grande (60 %), dos medianas (20 % y 12 %) y una chica (8 %). Escalas acumuladas:

| Escala | Órdenes | Rutas | Paradas de ruta | Historial de estatus | Bitácora | Base |
|---|---|---|---|---|---|---|
| Pequeña | 18.475 (10 mil históricas) | 378 (200 históricas) | 8.235 | 68.589 | 44.098 | — |
| Media | 108.619 | 2.254 | 33.383 | 521.458 | 239.523 | — |
| Grande | **508.619** (305 mil / 102 mil / 61 mil / 41 mil) | **10.254** | **155.403** | 2.522.205 | 1.058.545 | 4,2 GB |

  En cada escala se corrió el mismo banco (`bench-b.mjs`, 7 repeticiones tras 1 calentamiento, secuencial) en la compañía grande
  (lecturas y escrituras) y en la chica (lecturas).

### Hallazgo de fondo: el filtro global de tenant impide buscar por `TenantId`

El filtro global de EF (`e => TenantFilterDisabled || e.TenantId == CurrentTenantId`) llega a SQL Server como
`(@__ef_filter__TenantFilterDisabled_0 = CAST(1 AS bit) OR [t].[TenantId] = @__ef_filter__CurrentTenantId_1)`. Como el plan en caché
debe servir también cuando el bypass vale 1, **SQL Server no puede buscar por ningún índice que empiece por `TenantId`**: con
`WITH (FORCESEEK)` responde «Query processor could not produce a query plan»; sin el OR, sí busca. Consecuencias medidas:

- Toda consulta cuyo único filtro selectivo es la compañía **recorre la tabla completa con las filas de todas las compañías**. A
  508 mil órdenes, la compañía chica (8 % de las filas) lee las mismas **17.291 páginas** de `TransportOrder` que la grande en cada lista,
  COUNT, búsqueda por `PublicId` o saldo de crédito; su «Sin asignar» tarda lo mismo que el de la grande (615 vs 643 ms).
- Con READ COMMITTED de bloqueo, esos recorridos piden **S de página** y chocan con la última página del índice clúster, donde
  **insertan todas las compañías** (llave `IDENTITY`): una compañía espera a la transacción de otra, y si la otra también recorre, hay
  **interbloqueo entre compañías**.
- Los planes dependen de quién compila primero («sniffing»): la búsqueda libre de órdenes de la compañía chica lee 31 mil páginas
  (1,3 s) con el plan que compiló la grande y 249 mil páginas (0,1 s) con el suyo; la de la grande, 1,9 M páginas con el de la chica.

### A — Concurrencia entre compañías: resultados

| Prueba (escala) | Peticiones | 5xx | 409 `concurrent_edit` | Interbloqueos | Esperas ENTRE compañías (vigía, M) | p95 por compañía |
|---|---|---|---|---|---|---|
| Alta de datos de 4 compañías por la API, una petición a la vez por compañía (base vacía) | ~1.000 | 0 | **10** (y 2 en otra corrida) | **92 en 65 s** | — | — |
| Carga A, M, antes (pequeña) | 1.315 | 0 | 0 | 44 | 25 episodios, hasta 4,4 s | 10,4–13,5 s |
| Carga A, S, antes (pequeña, ×2) | 1.312–1.314 | 0 | 0 | 11 y 7 | — | 1,6–8,6 s |
| Carga A, M, antes (grande) | 1.314 | 0 | **26** | **303** | 66 episodios, hasta 6,5 s | 28,9–30,9 s |
| Carga A, S, antes (grande) | 1.313 | 0 | 0 | 29 | — | 16,6–17,4 s |
| **Carga A, M, después (grande, ×2)** | 1.313–1.315 | 0 | **0** | **7 y 4** | 9 y 4 episodios, hasta **0,43 s**, solo lecturas | 3,6–13,6 s |
| **Carga A, S, después (grande, ×2)** | 1.310–1.311 | 0 | **0** | **4 y 4** | — | 3,7–8,2 s |
| Alta de órdenes, 4 compañías × 2 sesiones × 25, sin el índice del saldo (grande, ×2) | 200 | 0 | **15 y 22** | **175 y 202** | — | 7,4–13,2 s (60–64 s en total) |
| **Alta de órdenes, con el índice del saldo (grande, ×2)** | 200 | 0 | **0 y 0** | **0 y 0** | — | **87–399 ms** (8,6–9,2 s en total) |

- **Qué se cruzaba entre compañías** (antes): S de **página** sobre el índice clúster de `TransportOrder` (251 de los recursos en
  interbloqueo), `OrderStop` y `RouteStop`, pedidos por recorridos: el **saldo de crédito** (`QuotedOrdersBalanceProvider`, dentro de
  la transacción del alta/confirmación de la orden — la causa de los 92/175/202 interbloqueos al crear órdenes), la búsqueda por
  `PublicId` de órdenes y rutas, la lista de órdenes, «Sin asignar» y `TripIssueBuilder` (otras rutas del mismo chofer/vehículo el
  mismo día, `Trip`). **No** se encontró ningún ciclo por tablas globales, numeración (`NumberSequence` tiene fila por compañía),
  catálogos, caché de permisos (en memoria; solo un `SemaphoreSlim` global al cargar `LookupCache`), `IdempotencyRecord`, `AuditLog`
  ni `EntityStatusHistory` (inserciones sin recorrer), ni por la fila `Tenant` (cada compañía bloquea solo la suya).
- **Después**: quedan (1) los residuales ya descritos arriba —lectura de la ficha contra el `SaveChanges` de **la misma** ruta, misma
  compañía (confirmado con `Application Name`), víctima la lectura que EF repite—, y (2) **esperas de lectura** de hasta 0,43 s de una
  compañía detrás del alta de una orden de otra: el COUNT de la lista de órdenes y el pool «Sin asignar» siguen recorriendo
  `TransportOrder` por el filtro; no son ciclos y ninguna llegó al cliente como error.
- Los p95 de varios segundos de la carga A son de esta carga sintética extrema (~55 peticiones simultáneas por compañía): la mayor
  parte es la **fila de escrituras de rutas de cada compañía** (decisión 1) y las lecturas de 0,5–0,8 s de «Sin asignar».

**Operaciones largas** (escala grande; B = la compañía 3 escribiendo rutas; latencia de B durante / fuera de la operación de A):

| Operación de la compañía 0 | Duración | Antes: espera de B | Después: espera de B |
|---|---|---|---|
| Editar la ficha de la compañía (8 s en bucle) | 8 s | ninguna (p95 46 / 45 ms) | ninguna (p95 49 / 98 ms) |
| Confirmar importación de 5.000 consignatarios | 57 s / 41 s | ninguna (p95 46 / 66 ms) | ninguna (p95 51 / 52 ms) |
| «Planificar el día», todas las zonas | 2,8 s (944 órdenes) / 3,7 s (1.800) | **1 espera de 1,3 s** (S de página en `Trip`, `TripIssueBuilder`) | **ninguna** (p95 65 / 56 ms, máx. 101 ms) |
| Lo mismo dentro de **la misma** compañía (B = 0) | 2,8 s / 44 s / 8 s | — | «Planificar el día»: la escritura espera **2,8 s** (toda la operación, por diseño); importación: p95 55 ms (se intercala fila por fila); ficha: sin espera |

Conclusión A: el bloqueo por la fila `Tenant` es **solo por compañía** (confirmado: ninguna operación de una compañía hace esperar a
otra por esa fila). Lo que cruzaba compañías eran los recorridos completos de tabla; con los índices de abajo desaparecen los
interbloqueos y las esperas largas entre compañías en las escrituras; quedan esperas cortas de algunas lecturas.

### B — Volumen: resultados

Latencia de la API, p50/p95 en ms (T0 = compañía grande, T3 = chica; «después» = con los 7 índices). Por la variación del servidor
compartido, comparar sobre todo las lecturas lógicas de la tabla siguiente.

| Consulta | Pequeña T0 | Media T0 | Grande T0 antes | Grande T0 después | Media T3 | Grande T3 antes | Grande T3 después |
|---|---|---|---|---|---|---|---|
| GET /orders (sin filtros, take 100) | 44/53 | 69/79 | 150/183 | 150/168 | 46/50 | 69/83 | 90/104 |
| … serviceTypes | 44/75 | 81/113 | 191/217 | 208/225 | 48/54 | 107/120 | 110/131 |
| … packageTypes | 70/76 | 139/155 | 246/275 | 250/306 | 116/142 | 174/213 | 221/275 |
| … cities | 66/98 | 254/262 | 277/318 | 237/255 | 192/196 | 207/255 | 226/252 |
| … postalCodePrefix | 62/64 | 146/164 | 262/1.558 | 212/272 | 121/145 | 411/645 | 178/285 |
| … clientIds | 39/47 | 57/62 | 224/273 | 227/297 | 32/38 | 102/124 | 113/151 |
| … statuses | 42/44 | 97/110 | 189/261 | 192/251 | 48/63 | 103/143 | 114/239 |
| … search | 270/277 | 932/965 | 2.068/2.336 | 2.334/2.446 | 370/384 | 1.451/1.529 | 1.435/1.529 |
| … from/to (30 días) | 26/29 | 55/62 | 102/104 | 101/107 | 40/48 | 86/94 | 83/97 |
| … skip 5.000 take 200 | 44/46 | 76/92 | 160/176 | 154/182 | 51/52 | 79/94 | 83/97 |
| Ordenar todo lo filtrado (`exportRows`, 50 páginas = 10.000 filas) | 962 | 3.947 | 8.229 (15.766 en otra corrida) | 20.490 | 2.284 | 4.614 | 4.635 |
| GET /trips?date / rango 30 días | 7 / 6 | 5 / 5 | 9 / 10 | 7 / 8 | 4 / 5 | 10 / 9 | 8 / 8 |
| GET /trips/unassigned-orders (take 500) | 107/123 | 528/550 | 643/707 | 643/740 | 533/588 | 615/697 | 796/814 |
| GET /trips/monitor | 13/14 | 15/16 | 18/18 | 28/28 | 8/11 | 12/12 | 11/12 |
| GET /trips/{id} (ruta de ~75 paradas) | 34/37 | 22/29 | 25/32 | 29/32 | 25/28 | 22/23 | 22/26 |
| POST /scan/outbound | 18/41 | 60/142 | 54/130 | **9/10** | 37/83 | 54/88 | **9/98** |
| PUT pin / PUT secuencia (75 paradas) | 52 / 134 | 52 / 125 | 66 / 148 | 83 / 154 | — | — | — |
| POST agregar 5 órdenes / DELETE quitar | 64 / 51 | 71 / 38 | 76 / 59 | **54 / 51** | — | — | — |
| POST optimizar 60 / 150 / 300 paradas | 75 / 127 / 175 | 76 / 109 / 166 | 78 / 118 / 180 | 100 / 129 / 200 | — | — | — |
| POST despachar | 345 | 320 | 425 | 619 | — | — | — |

Lecturas lógicas por ejecución (sesión XE, escala grande, mismo texto de EF):

| Sentencia | Antes | Después | Índice |
|---|---|---|---|
| Orden por `PublicId` (ficha, quitar de ruta, cancelar…) | 17.484–17.516 | **16–121** | `IX_TransportOrder_PublicId` |
| Órdenes por lista de `PublicId` (agregar a la ruta) | 17.291 | **30** | `IX_TransportOrder_PublicId` |
| Ruta por `PublicId` (`LockTripAsync(Guid)`, ficha; 10 mil rutas) | 163–204 | **4–5** | `IX_Trip_PublicId` |
| Saldo de crédito al crear/confirmar orden (cliente chico / cliente con 290 mil órdenes) | 17.291 / 17.291 | **156 / 1.145** | `IX_Order_Client_Balance` |
| Búsqueda exacta del escaneo (`LookupAsync`: número, empaque o factura) | 17.494 (97 ms) | **12–73 (2,6 ms)** | `IX_Order_OrderNumber/PackBatchNumber/ClientInvoiceNumber` |
| Otras rutas del mismo chofer/vehículo el mismo día (`TripIssueBuilder`) / rutas abiertas de la zona (escaneo) | 204 / 47 | **18 / 6** | `IX_Trip_PlanDate` |
| `LockTenantPlanningAsync` (fila Tenant, sin contención) | 2 (0,1 ms) | 2 (0,1 ms) | — |
| `LoadRouteStopsAsync` / `StopTimingsAsync` / `StopPointsAsync` / pin (`FORCESEEK`, rutas de 75 paradas) | 345 / 662 / 907 / 47 | igual (≤1,3 ms) | — (ya buscan; no dependen del volumen) |

Las 10 más caras (escala grande, antes y después iguales salvo las de arriba): COUNT y página de la **búsqueda libre** de órdenes
(1,4–2,2 M y 0,9 M lecturas; 0,8–1,5 s y 0,3 s; plan según quién compila), **página y COUNT de la lista de órdenes** con cualquier
filtro (17,5–35 mil lecturas cada una: recorrido completo + ordenamiento; con derrames a tempdb de 5–9 mil páginas en algunas
ejecuciones a escala grande; la página profunda lee lo mismo), y el **pool «Sin asignar»** (47,5 mil lecturas, 0,7 s y 4,6–5,4 s de CPU
por llamada: recorre `TransportOrder`, `OrderStop` y `TripOrder` completos). Las consultas de escritura de rutas con `FORCESEEK`, la fila
Tenant y el bloqueo de la ruta no aparecen: cuestan lo mismo a 10 mil que a 500 mil.

**Costo en escritura** de los 5 índices nuevos de `TransportOrder`: insertar 20.000 órdenes (revertido) pasa de ~0,85 s y ~23 MB de log a
~1,2 s y ~37 MB (≈ +17 µs por orden, frente a 60–300 ms de crear una orden por la API). `Trip` recibe una fila por ruta.
`db-update` sobre la base existente de 4,2 GB: la simulación lista los 7 índices, aplicarlos tarda 12 s y la segunda corrida informa
0 cambios.

**Índices probados y descartados** (medidos, sin mejora clara o con regresión):
- `(CreatedAtUtc DESC, TransportOrderId DESC) INCLUDE (TenantId, ClientId)`: la primera página de la lista bajaba de 17.291 a ~325
  lecturas, pero la búsqueda libre pasaba a recorrerlo en orden con búsquedas de llave (*row goal*): 1,24 M lecturas y 2,6 veces más
  lenta, y las páginas profundas de `exportRows` empeoraban.
- El saldo sin el filtro `QuotedAmount IS NOT NULL`: el optimizador lo usaba como índice angosto para los COUNT de la lista (menos
  lecturas y CPU, pero planes en serie más lentos y una página que pasó de 17 mil a 75 mil lecturas). Con el filtro igual al de la
  consulta, solo el saldo lo usa (verificado: los planes de la lista no cambian).
- `(StatusCodeId) INCLUDE (…)` para «Sin asignar»: `TransportOrder` 17.510 → 271 lecturas, pero `OrderStop` sigue recorriéndose (29 mil):
  1,5 → 1,07 s, no llega a 3×.
- Se comprobó con la llamada exacta (caché limpia en cada caso) que `IX_TransportOrder_PublicId` y los índices de código **no
  cambian** el plan de la búsqueda libre.

### Qué se cambió

| Pieza | Dónde |
|---|---|
| Sección nueva **«2026-10-12 (b)»** al final, idempotente, solo agrega 7 índices no únicos: `IX_TransportOrder_PublicId`, `IX_Trip_PublicId`, `IX_Order_Client_Balance (ClientId, StatusCodeId) INCLUDE (TenantId, QuotedAmount) WHERE IsActive = 1 AND QuotedAmount IS NOT NULL`, `IX_Order_OrderNumber`, `IX_Order_PackBatchNumber`, `IX_Order_ClientInvoiceNumber` (los tres `WHERE IsActive = 1`) e `IX_Trip_PlanDate (PlanDate) INCLUDE (TenantId, DriverId, VehicleId, DispatchZoneId, StatusCodeId) WHERE IsActive = 1`. Cada uno empieza por una columna que ya identifica la fila (no por `TenantId`), por eso sirve con el filtro actual. No son únicos a propósito: los únicos por compañía ya existen y un `UNIQUE` sobre `PublicId` haría fallar `db-update` ante un duplicado inesperado | `Diseño/logistica-db-update.sql` |
| El modelo EF los declara con el mismo nombre, columnas, INCLUDE y filtro | `OrderConfigurations.cs`, `TripConfigurations.cs` |
| `VolumeIndexesTests` (9): la sección existe después de la (a), cada `CREATE INDEX` con su guarda, sin DROP/DELETE/UNIQUE; el modelo EF (de diseño) refleja cada índice; el filtro del índice del saldo coincide con la consulta del saldo | `tests/Teikem.Tests` |

Sin cambios de código de aplicación ni de contrato (no se regeneró `openapi.json`). `dotnet build` sin errores; `dotnet test Teikem.sln`:
**3.595 pruebas verdes** (9 nuevas). `logistica-db-estructura.sql` y `logistica-db-seed.sql` intactos (`FrozenSqlTests`).

### Decisiones para el dueño

1. **Hacer buscable el filtro de tenant (recomendado, lote propio).** Propuesta preparada y **no aplicada**: un `DbCommandInterceptor`
   que, justo antes de ejecutar, quita del texto el término `@__ef_filter__TenantFilterDisabled_N = CAST(1 AS bit) OR ` **solo si ese
   parámetro vale `false`** y solo cuando lo precede `(`, `WHERE`, `ON`, `WHEN`, `HAVING` u `OR` (un término falso de un OR: el
   resultado es idéntico; con el bypass encendido —aprovisionamiento, seeders, migración— no se toca). Medido a nivel SQL con el mismo
   texto de EF (compañía chica, 508 mil órdenes): COUNT de la lista **17.291 → 137 lecturas** (253 → 15 ms). Pero no basta solo: la
   página ordenada por fecha sigue recorriendo sin un índice `(TenantId, CreatedAtUtc DESC, TransportOrderId DESC)` y el saldo empeoró
   (17.291 → 124.903) sin su índice; hay que revisar los planes consulta por consulta. Toca el control de aislamiento entre compañías,
   por eso necesita la aprobación del dueño (en esta sesión no se permitió ejecutar la API con ese cambio). Es la solución de fondo para
   el volumen de las listas y para las esperas de lectura entre compañías que quedan.
2. **READ_COMMITTED_SNAPSHOT** (ya recomendado arriba): eliminaría las esperas y los interbloqueos de lectura contra escritura que
   quedan (las lecturas de 0,4 s detrás de un alta de otra compañía y los residuales de la ficha), sin tocar el filtro.
3. **Ordenar todo lo filtrado (`exportRows`)** a 500 mil órdenes tarda 8–20 s para 10.000 filas: 50 páginas de 200, cada una con su
   COUNT y su ordenamiento completo. Opciones: páginas más grandes para exportar, no repetir el COUNT después de la primera página, o
   paginación por llave; con la decisión 1 y el índice por fecha bajaría a lecturas por rango.
4. **Búsqueda libre de órdenes** (`search`, `LIKE '%…%'` en número, factura, empaque y nombre del consignatario): 1,4–2,3 s a escala
   grande y plan inestable entre compañías. Un índice B-tree no ayuda con el comodín inicial; candidatos: búsqueda de texto completo o
   limitar la búsqueda por fecha.
5. **«Sin asignar»** (0,6–0,8 s y ~5 s de CPU por llamada, lo pide la pantalla de despacho con frecuencia) recorre `OrderStop` completo;
   conviene reescribir la consulta (parada pendiente por `TransportOrderId` con su índice) junto con la decisión 1.
6. `OptimizationRun` crece con cada optimización con JSON grandes (1.219 corridas ya ocupan 3.067 páginas) y la ficha lee la última
   corrida de la ruta por `TripId` con el filtro de tenant (hoy 28–30 lecturas porque la busca desde el final); vigilarlo en
   producción.

### Límites de la prueba (qué NO representa de producción)

- **Hardware y servidor**: SQL Server Developer local con 32 núcleos y la base completa en memoria (caché caliente); producción
  tiene otro hardware, otra memoria y puede tener otra edición (sin `ONLINE` para crear índices: se crean en segundos, bloqueando la
  tabla brevemente). El servidor de prueba era compartido con otras sesiones: las latencias absolutas varían hasta ×2 entre corridas.
- **Datos**: sintéticos. Distribuciones simples (un cliente por compañía, 20 pueblos, ventanas en el 25 %, sin movimientos de
  inventario ni saldos, sin pings de GPS, sin documentos ni fotos), sin la forma real de los datos de Advance Depot y Solutions. Las
  estadísticas se actualizaron con `sp_updatestats` después de generar.
- **Concurrencia**: carga sintética extrema y simultánea, no la de usuarios reales (que piensan, navegan y no repiten la misma
  escritura en ráfagas). Una sola máquina cliente; sin red real entre API y SQL.
- **Escalas**: las tres escalas se midieron antes de los índices; el «después» solo a escala grande (los datos son acumulados).
- Versión: SQL Server 2022 (16.0.1200), compatibilidad 160; la de producción puede diferir.

La base `Teikem_vol_test`, sus sesiones XE (`vol_dl`, `vol_rpc`) y las APIs de prueba se eliminaron al terminar. Los programas
(`mt.mjs`, `bench-b.mjs`, `gen.sql`, vigía y resúmenes XE) quedaron en el directorio temporal de la sesión.
