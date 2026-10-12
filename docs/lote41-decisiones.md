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
