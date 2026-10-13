# Lote 42 — Filtro de tenant buscable por índice (2026-10-12)

Autorizado expresamente por el dueño (Luis, 2026-10-12). Arregla de fondo el hallazgo del Lote 41 («Verificación con varias
compañías y con volumen sintético»): el filtro global de tenant de EF llegaba a SQL Server como
`(@__ef_filter__TenantFilterDisabled_0 = CAST(1 AS bit) OR [t].[TenantId] = @tid)` y, con ese OR sobre un parámetro, SQL Server
**no podía buscar por ningún índice que empiece por `TenantId`** (con `FORCESEEK`: «no hay plan»). Toda lista, COUNT o saldo
recorría la tabla completa con las filas de TODAS las compañías y, con READ COMMITTED de bloqueo, una compañía esperaba a otra.

Regla que guió todo el lote: **cero posibilidad de que una compañía vea o escriba datos de otra, y fallar CERRADO** (si algo no se
reconoce, la consulta queda exactamente como antes, con el filtro completo).

## Qué se construyó

| Pieza | Dónde |
|---|---|
| El filtro de tenant pasa a `e => QueryFilterTenantId == null \|\| e.TenantId == QueryFilterTenantId` (y `\|\| e.TenantId == null` en los opcionales). `QueryFilterTenantId` vale `null` **solo** con el bypass encendido; si no, la compañía activa, o `-1` sin compañía (no se ve nada, como antes). Mismo resultado fila por fila que el filtro anterior | `TeikemDbContext` (`QueryFilterTenantId`, `BuildTenantFilter`) |
| El alcance de datos del usuario (almacén / cliente, Lote 32) con la misma técnica: `ids == null \|\| ids.Contains(col)`, `null` = sin alcance | `TeikemDbContext` (`QueryFilterWarehouseIds`, `QueryFilterClientIds`, `AddScopeFilter`) |
| `TenantFilterEvaluatablePlugin` (punto de extensión **público** de EF, `IEvaluatableExpressionFilterPlugin`) y su `TenantFilterOptionsExtension`, registrados en `TeikemDbContext.OnConfiguring` para que TODO contexto los tenga (API, seeders, migración, pruebas) | `Persistence/TenantQueryFilter.cs` |
| Índice `IX_Order_Tenant_Created` en la sección nueva **«2026-10-12 (c)»** al final de `logistica-db-update.sql` (idempotente, solo agrega) y su espejo en el modelo EF | `Diseño/logistica-db-update.sql`, `OrderConfigurations.cs` |
| La lista de órdenes con texto libre sobre las paradas (búsqueda o consignatario) ordena por una expresión **equivalente** (`CreatedAtUtc.AddMilliseconds(0)`): mismo orden y resultados, pero el índice no la entrega ya ordenada (ver «Índices») | `OrderReadService.GetListAsync` |
| Pruebas de no filtración (24 nuevas) y la de índices del Lote 41 acotada a su sección | `tests/Teikem.Tests` |
| El CI define `TEIKEM_TEST_SQLSERVER` en el paso de pruebas: las pruebas contra SQL Server real corren en cada push (crean y borran su propia base `Teikem_isotest_<guid>`) | `.github/workflows/ci.yml` |

Sin cambios de contrato (no se regeneró `openapi.json`), ni de permisos, ni de los `.sql` congelados (`FrozenSqlTests` verde).

## Enfoques evaluados (medidos)

Mismas sentencias de EF, base sintética del Lote 41 reconstruida (508 mil órdenes, 4 compañías; T0 = grande 60 %, T3 = chica 8 %),
lecturas lógicas por ejecución con caché de planes limpia:

| Sentencia (T0 / T3) | Antes (OR) | (a) Interceptor que quita el término del bypass | (b) Filtro anulable + plugin (**elegido**) | (c) Antes + `OPTION (RECOMPILE)` |
|---|---|---|---|---|
| COUNT de la lista sin filtros | 17.255 / 17.255 | 1.722 / 145 | 941 / 145 | 941 / 145 |
| COUNT por estatus | 17.255 / 17.255 | 17.255 / 17.255 | 898 / 133 | 898 / 133 |
| COUNT por pueblo | 31.547 / 485.083 | 17.097 / 13.833 | 15.284 / 14.445 | 15.284 / 14.445 |
| Costo extra por ejecución | — | regex sobre cada comando | ninguno | compilar CADA vez: +7 ms (página de la lista), +4 ms (COUNT por estatus), +75 ms (página combinada: 6 → 81 ms) |

- **(a) Interceptor** (la propuesta del Lote 41): reescribe el texto SQL con una expresión regular. Frágil (depende del texto exacto de
  EF y de la posición del término), toca la barrera de seguridad con manipulación de cadenas, y no resuelve el OR del **alcance de
  datos** (`@p = 1 OR [t].[ClientId] IN (OPENJSON…)`), que también confunde al optimizador (el COUNT por estatus seguía en 17.255).
- **(c) `OPTION (RECOMPILE)`**: mismas lecturas que (b), pero compila en cada ejecución (medido arriba) y además exige otro interceptor
  para agregar la pista.
- **(b) Elegido**: EF genera el SQL por su camino normal, sin tocar texto. El plugin solo le pide a EF que **no pre-evalúe** la
  comparación exacta `ctx.QueryFilterTenantId == null` (ni las dos del alcance); así llega a la traducción como `@p IS NULL`, que EF
  resuelve con el valor real del parámetro al armar el comando y guarda en su caché de comandos **separada por nulidad de cada
  parámetro** (el mismo mecanismo con el que EF ya traduce `x == @p` cuando `@p` es null; si fallara, fallarían todas las
  comparaciones con null de la aplicación). Resultado:
  - compañía activa: `WHERE [t].[TenantId] = @__ef_filter__QueryFilterTenantId_0` (buscable);
  - opcionales (catálogos con `TenantId` NULL = global): `[t].[TenantId] IS NULL OR [t].[TenantId] = @p`;
  - bypass (plataforma, sembradores, migración): sin condición de tenant, igual que antes;
  - sin alcance de datos: sin término; con alcance: `[x].[ClientId] IN (SELECT … FROM OPENJSON(@ids))`.

Se descartó también tener dos modelos (`IModelCacheKeyFactory`): el bypass se enciende y apaga **dentro** de la vida de un mismo
contexto (`TenantContext.BypassTenantFilter()` en login, aprovisionamiento, seeders; `As()` cambia de compañía), y el modelo de un
contexto es fijo; habría que garantizar que nadie cambie el bypass con un contexto abierto, más frágil que (b).

## Falla cerrada (qué pasa si…)

| Caso | Resultado |
|---|---|
| Una versión de EF deja de consultar el plugin | La comparación vuelve a mandarse como parámetro `bit`: el SQL queda **exactamente como antes** (OR con el filtro completo, solo más lento). `TenantFilterSqlShapeTests` fallan (avisan); `Sin_el_plugin_falla_cerrado…` fija esa forma y verifica que la condición de `TenantId` sigue ahí con el valor de la compañía |
| El plugin no reconoce una forma (otra subclase, otro proveedor) | Igual: forma anterior, filtro completo. El plugin compara por declaración (`DeclaringType` + nombre), así que también sirve en subclases del contexto |
| No hay compañía activa y el bypass está apagado | `QueryFilterTenantId = -1`: no se ve ninguna fila con `TenantId`; en las opcionales, solo las globales (NULL). Igual que antes |
| `null` en `QueryFilterTenantId` | Solo ocurre con `TenantFilterDisabled = true`, la misma condición del bypass anterior |
| `IgnoreQueryFilters()` | Sigue ignorando todo (probado) |
| SQL crudo (`Database.SqlQuery`) | Nunca lo filtró EF (ni antes ni ahora); su uso sigue confinado (`RawSqlConfinementTests`). `FromSql` sigue compuesto con el filtro afuera (probado) |
| InMemory (pruebas) | El plugin también se registra; InMemory evalúa la misma expresión en memoria (mismo resultado) |

## Pruebas de no filtración (cobertura)

Las entidades se enumeran **por reflexión del modelo** (`ITenantScoped` e `IOptionallyTenantScoped`): hoy **76**, incluidas las
opcionales con `TenantId` NULL = global (p. ej. `LookupCode`). Una prueba exige que la lista coincida con toda entidad del dominio con
`TenantId` y filtro (si se agrega una, entra sola). La siembra genérica crea por entidad filas de la compañía A, de la B y (si es
opcional) una global, con llaves foráneas que apuntan **a propósito a filas de la otra compañía**.

| Prueba | Proveedor | Qué demuestra |
|---|---|---|
| `TenantIsolationInMemoryTests` (5) | InMemory, siempre | Para las 76 entidades, con A activa no se ve nada de B en: lista (con y sin seguimiento), proyección, `Count`, `LongCount`, `Any`, `Contains`, `Where TenantId = B` explícito, ordenar desc + `Take`, `Skip`/`Take`, `Distinct`, `GroupBy`, `Max`, `FirstOrDefault`, subconsulta, `Find` por la llave de una fila de B, `Include` de cada navegación (y `AsSplitQuery` en colecciones) — lo cargado tampoco es de otra compañía; conteos exactos; `As(B)` y vuelta; el bypass ve todo **solo dentro de su bloque** y al salir vuelve a filtrar; `IgnoreQueryFilters` ve todo; sin compañía falla cerrado |
| `TenantIsolationSqlServerTests` (5) | **SQL Server real** (base propia `Teikem_isotest_<guid>`, creada y borrada por la prueba; se omite sin `TEIKEM_TEST_SQLSERVER`; el CI la define) | Las mismas formas sobre las 76 entidades; **cada comando SQL ejecutado** (más de 1.000 por compañía) lleva `[x].[TenantId] = @…QueryFilterTenantId_N` en cada tabla con filtro, sin rama de bypass, y el parámetro vale la compañía; **resultados idénticos a los del filtro anterior** (contexto con el filtro viejo completo sobre la misma base) con A, con B, con bypass, sin compañía y con 5 combinaciones de alcance de almacén/cliente (incluido alcance vacío); bypass / `IgnoreQueryFilters` / sin compañía; `FromSql` compuesto y SQL crudo sin cambio; el SQL nuevo admite `FORCESEEK` sobre un índice que empieza por `TenantId` y el viejo da el error 8622 («no hay plan») |
| `TenantFilterSqlShapeTests` (10) | Proveedor SQL Server **sin servidor** (la conexión y la ejecución se suprimen; se captura el comando), siempre | Recorre el SQL de todas las formas de las 76 entidades (A y B) y de consultas como las de la aplicación (lista con filtros e `Include`, saldo, rutas y uniones, inventario, catálogos, alcance de almacén y cliente, `FromSql`, `ExecuteUpdate`): **ningún comando sin la condición de `TenantId`**; SQL exacto (golden) con compañía, bypass, opcional, B y sin compañía; alternar compañía/bypass en el mismo contexto nunca reusa el SQL equivocado; sin plugin falla cerrado; el plugin solo reconoce la comparación exacta; el filtro de cada entidad ya no usa `TenantFilterDisabled` |
| `TenantSeekIndexTests` (4) | — | Sección «2026-10-12 (c)» idempotente y solo agrega; el modelo EF refleja el índice (orden descendente, INCLUDE, filtro); EF manda el orden de la búsqueda como `DATEADD(millisecond, 0, …)` (si una versión lo simplificara, avisa); la lista con búsqueda o consignatario devuelve **el mismo orden y la misma página** que sin ella, con empate de fecha |
| `UserDataScopeFilterTests` y las demás (existentes) | InMemory | Siguen verdes sin cambios: el alcance de datos se comporta igual |

**Mutaciones** (para comprobar que las pruebas detectan una fuga): con `QueryFilterTenantId` devolviendo `null` para la compañía A,
fallan 7 de las 10 pruebas de aislamiento (InMemory y SQL Server); sin el plugin, los resultados siguen idénticos (falla cerrado) y
fallan las de forma del SQL (1.423 comandos con la rama del bypass).

## Índices

Revisados uno por uno los planes que cambiaron, a escala grande (508 mil órdenes; inventario sintético nuevo: 2 millones de
movimientos de Kárdex, 100 mil saldos, 50 mil recibos y 50 mil productos en 4 compañías). Criterio: solo con mejora medida ≥ 3× o que
evite recorrer una tabla grande.

**Agregado — `IX_Order_Tenant_Created`** `ON TransportOrder(TenantId, CreatedAtUtc DESC, TransportOrderId DESC) INCLUDE (ClientId,
StatusCodeId, ServiceTypeLookupId, OrderNumber, ClientInvoiceNumber, PackBatchNumber) WHERE IsActive = 1` (6.504 páginas, frente a
16.426 del clúster):

| Sentencia (T0 / T3) | Antes | Solo el filtro nuevo | Con el índice |
|---|---|---|---|
| Página 1 de la lista (take 100) | 17.255 / 17.255 | 17.255 / 17.255 | **318 / 319** |
| Página por estatus / por estatus múltiple / por servicio | 17.255 | 17.255 | **636 / 318 / 1.819** |
| Página de pueblo / ZIP / tipo de paquete | 31.547–32.214 | 31.547–464.747 | **2.098–2.736** |
| Página con fechas (30 días) / por cliente | 17.255 | 17.255 / 329 | **318 / 329** |
| COUNT de búsqueda (chica) | 264.039 | 264.039 | **14.994** |
| Página profunda (skip 5.000) | 17.255 (+ ordenar 300 mil filas) | igual | 15.963 lecturas, pero CPU 1.254 → 24 ms y 280 → 39 ms |
| Última página de «Ordenar todo lo filtrado» (skip 9.800) | 17.255 | igual | 30.700 lecturas (búsquedas de llave de las filas saltadas), CPU 1.434 → 92 ms; la exportación completa de 10.000 filas pasa de 16,4 s a 2,5 s |

Costo en escritura (20.000 órdenes, revertido, 3 corridas): insertar 0,96–1,05 s → 1,12–1,23 s (≈ +10 µs por orden; log 30 → 41 MB);
cambiar el estatus (columna incluida) 276–284 → 327–360 ms (≈ +3 µs por orden). Frente a 60–300 ms de crear una orden por la API.

**Cambio de código que acompaña al índice.** Con el índice ordenado, la **página de la búsqueda libre** empeoraba: SQL Server no sabe
estimar `LIKE '%…%'`, cree que encontrará 100 coincidencias pronto y recorre toda la compañía **en orden**, probando orden por orden
(búsqueda de un número exacto en la grande: 1,4 s → 5,4 s; la chica 0,3 → 0,8 s). Una pista de consulta o reescribir el SQL habría
vuelto a manipular texto; en cambio, solo cuando hay búsqueda o consignatario, la lista ordena por `CreatedAtUtc.AddMilliseconds(0)`:
el mismo valor (mismo orden, mismo desempate por `TransportOrderId`, mismos resultados, probado), pero el índice ya no lo entrega
ordenado, así que SQL Server filtra primero (en paralelo) y ordena solo las coincidencias: 1,0–1,3 s en la grande (antes 1,4 s), 0,3 s
en la chica (igual). Ninguna otra consulta ordena `TransportOrder` por fecha con `IsActive` (las fuentes de análisis no filtran
`IsActive`, no usan este índice filtrado).

**Probados y descartados:**

- `(TenantId, StatusCodeId, CreatedAtUtc DESC, TransportOrderId DESC) INCLUDE (ClientId, …)`: no cambiaba el COUNT por estatus (lo
  frenaba el OR del alcance, que este lote eliminó; sin ese OR el índice existente `IX_Order_Tenant_Status` ya da 133/898 lecturas).
- `(TenantId, CreatedAtUtc DESC)` sin el desempate (para no tentar a la búsqueda): las páginas vuelven al recorrido completo.
- `OrderStop(TransportOrderId, StopTypeLookupId) INCLUDE (SnapName, SnapCity, SnapPostalCode)` (6.694 páginas): búsqueda, pueblo y
  ZIP ~2× (COUNT de búsqueda de la grande 30.620 → 12.122), pero sigue recorriendo **todo** `OrderStop` (el índice en lugar del
  clúster): no cumple el criterio.
- La misma con todas las columnas de «Sin asignar» (8.524 páginas): 46.680 → 35.979 lecturas, no llega a 3×.
- **Inventario**: con el filtro buscable, los índices existentes que empiezan por `TenantId` ya bastan (Kárdex 22.245 → 649,
  búsqueda de saldos 301 mil → 2,5 mil lecturas en la grande); no se agregó ninguno.

## Resultados antes / después

Base `Teikem_l42` (desechable), datos sintéticos generados con `gen.sql` del Lote 41 (SET-BASED) más un generador de inventario
nuevo; mismas 4 compañías creadas por la API. «Antes» = `master` al empezar (con los 7 índices del Lote 41); «después» = este lote.

**API, p50/p95 en ms** (7 repeticiones tras 1 de calentamiento; T0 con escrituras, T3 solo lecturas):

| Consulta | T0 antes | T0 solo filtro | T0 después | T3 antes | T3 después |
|---|---|---|---|---|---|
| GET /orders (take 100) | 165/187 | 161/228 | **46/51** | 79/100 | **14/35** |
| … statuses | 206/243 | 196/234 | **42/49** | 106/124 | **14/16** |
| … serviceTypes | 202/280 | 191/272 | **48/66** | 92/95 | **34/44** |
| … cities | 237/253 | 228/281 | **111/146** | 1.424/2.154 | **118**/2.214 (1 de 7) |
| … clientIds | 220/315 | 27/31 | **29/44** | 98/110 | **13/16** |
| … from/to 30 días | 89/108 | 70/77 | **10/11** | 91/121 | **10/10** |
| … skip 5.000 | 189/1.718 | 132/177 | **44/56** | 92/110 | **31/34** |
| … search | 2.018/2.871 | 1.909/2.314 | 1.897/2.096 | 234/258 | 195/208 |
| Ordenar todo lo filtrado (10.000 filas) | 16.443 | 7.144 | **2.467** | 5.040 | **1.744** |
| GET /trips/unassigned-orders (take 500) | 629/703 | 332/362 | **340/376** | 565/710 | **292/373** |
| Kárdex (take 200) / 30 días | 187 / 78 | 62 / 16 | **61 / 18** | 90 / 70 | **20 / 7** |
| Saldos con búsqueda | 953/1.010 | 441/490 | **457/502** | 349/362 | **163/169** |
| Recibos con búsqueda | 210/243 | 129/143 | **127/146** | 47/49 | 38/57 |
| Rutas del día, monitoreo, ficha, escaneo, escrituras de rutas, catálogos, flota | sin cambio (± ruido) | | | | |

**Concurrencia entre compañías** (4 compañías a la vez, ~1.900 peticiones de la carga A del Lote 41; alta de órdenes 4 × 2 sesiones × 25):

| Prueba | Antes | Después |
|---|---|---|
| Carga A, S (una API, la real), ×3 | 48,4 / 55,7 / 67,2 s; p50 1,4–2,3 s | **48,1 / 25,1 / 34,8 s; p50 0,6–1,6 s** |
| Carga A, M (una API por compañía + vigía) | 62,5 s; p50 1,7–2,0 s; p95 7,1–7,8 s | **31,8 s; p50 0,8–1,2 s; p95 4,6–4,7 s** |
| Esperas **entre** compañías (vigía, M) | **31 episodios, hasta 3,7 s** (la primera: la última optimización de la ruta, `OptimizationRun`, que recorría la tabla de todas) | **2 episodios, hasta 6 ms** |
| Esperas dentro de la misma compañía (M) | 406 episodios | 147 episodios |
| Alta de órdenes, M | 9,6 s; p50 86–302 ms | **4,4 s; p50 ~60 ms; p95 ~107 ms** |
| Alta de órdenes, S, ×3 | 8,8 / 1,7 / 8,4 s | 11,9 / 1,5 / 2,2 s (la primera, ruido del servidor: p95 1 s en todas las compañías por igual) |
| Errores 5xx / 409 `concurrent_edit` / interbloqueos (sesión XE en `ring_buffer`) | 0 / 0 / 0 | 0 / 0 / 0 |

`db-update` sobre la base existente: la simulación lista «+ Crear índice IX_Order_Tenant_Created en dbo.TransportOrder»; aplicarlo,
21 s con el arranque del proceso; la segunda corrida informa 0 cambios (el modelo EF coincide con la base). `db-init` dos veces sobre
una base nueva y la **prueba de humo completa** (`scripts/smoke.sh`, con `SMOKE_SQL`) contra la API de este lote: verdes («SMOKE OK»).
Nota del entorno: en Windows (Git Bash) hay que exportar `TMPDIR` a una ruta de Windows; con `/tmp` el paso de logos del Lote 19 falla
al subir el archivo (`curl: (26)`, la ruta con `;type=` se convierte mal), sin relación con este lote; en el CI (Linux) no ocurre.

## Riesgos

- **Dependencia de un punto de extensión de EF.** `IEvaluatableExpressionFilterPlugin` es público y estable desde EF Core 2, pero su
  efecto (no pre-evaluar un nodo) depende de cómo el extractor de parámetros recorre el árbol. Si cambia, el resultado es la forma
  anterior (más lenta, igual de segura) y las pruebas de forma fallan.
- **Caché de comandos de EF por nulidad.** La seguridad descansa en que EF no reutiliza el SQL sin filtro (bypass) para una consulta con
  compañía. Es el mecanismo de corrección de EF para `x == @p`; además `Alternar_compania_y_bypass…` y las pruebas contra SQL Server
  alternan los dos casos en el mismo contexto.
- **El alcance de datos cambió de forma** (no de resultado): comparado fila por fila con el filtro anterior en 5 combinaciones.
- **Planes por compañía.** Con el filtro buscable, el primer plan compilado puede venir de la compañía grande o de la chica; ahora
  ambos son búsquedas por índice y la diferencia es mucho menor que antes, pero la búsqueda libre sigue siendo sensible.
- **La búsqueda libre** sigue costando 1,9 s en la compañía grande (`LIKE '%…%'` sobre 300 mil órdenes y sus paradas, ligada a CPU):
  ver decisiones.

## Límites (qué NO representa de producción)

- Datos **sintéticos** (distribuciones simples; un cliente por compañía; inventario sin lotes ni series), **caché caliente**, un solo
  equipo (32 núcleos) y SQL Server **compartido** con otras bases: la instancia tenía solo ~1,8 GB en uso (el sistema le recortó la
  memoria), por eso la caché de planes desalojaba entradas y Query Store quedó en solo-lectura; las estadísticas por sentencia se
  tomaron muestreando la caché cada 2 s y los experimentos con `SET STATISTICS IO`. Las latencias absolutas varían hasta ×2 entre
  corridas iguales; las lecturas lógicas son la métrica determinista.
- SQL Server 2022 Developer (16.0.1200, compatibilidad 160), EF Core 8.0.11. READ COMMITTED de bloqueo (`is_read_committed_snapshot_on = 0`).
- No se probó con datos reales de Advance Depot / Solutions ni con la red entre la API y el servidor.

## Si una actualización de EF cambia el SQL generado

1. Correr `dotnet test`: `TenantFilterSqlShapeTests` (forma exacta y recorrido de >1.000 comandos) y `TenantSeekIndexTests` dicen qué
   cambió. **La seguridad no depende de que pasen**: si EF deja de honrar el plugin, el SQL vuelve al OR de antes (filtro completo).
2. Si la condición de `TenantId` cambió de forma pero sigue presente (otro alias, otros paréntesis), ajustar las expresiones regulares
   de `TenantFilterHarness.CheckFilteredSql` y los golden de `Forma_exacta…`, **nunca** quitar la verificación de que cada tabla con
   filtro lleva su condición y su parámetro con el valor de la compañía.
3. Si EF 9+ ofrece una forma nativa (p. ej. `EF.Constant`/parámetros que se integran al SQL), puede reemplazar el plugin; las pruebas
   de no filtración (InMemory, SQL Server y forma) son el criterio para aceptar el cambio.
4. Si EF simplificara `AddMilliseconds(0)`, `The_free_text_order_reaches_sql…` falla: buscar otra expresión equivalente no indexable.

## Decisiones a revisar (para el dueño)

1. **READ_COMMITTED_SNAPSHOT** (recomendado desde el Lote 41): quedan esperas de lectura contra escritura **dentro** de la misma compañía.
2. **«Sin asignar»** (300–340 ms) y **la búsqueda libre** (1,9 s en la compañía grande) necesitan cambio de consulta, no índice:
   «Sin asignar» calcula la parada pendiente sobre TODO `OrderStop`; la búsqueda, texto completo o limitar por fecha.
3. **Páginas profundas / exportación**: pedir primero solo los ids de la página (cubiertos por el índice: skip 5.000 → 81 lecturas,
   skip 9.800 → 196) y luego las filas bajaría la exportación de 10.000 filas aún más. Es un cambio de código de la lista.
4. `OptimizationRun` (la última optimización de la ruta): ya no cruza compañías; sigue creciendo con JSON grandes (vigilar).

## Cómo se probó

- `dotnet build Teikem.sln` sin errores ni advertencias; `dotnet test Teikem.sln` (con `TEIKEM_TEST_SQLSERVER` local):
  **3.625 pruebas verdes** (24 nuevas).
- Bancos del Lote 41 (`bench-b.mjs`, `mt.mjs`) más uno complementario (`bench-c.mjs`: catálogos, flota, inventario y alta de orden),
  generadores `gen.sql` y `gen-inv.sql`, vigía de esperas y sesión XE de interbloqueos en `ring_buffer` (sin archivos `.xel`). Cada
  medición partió de la misma instantánea de base (`CREATE DATABASE … AS SNAPSHOT`, revertida antes de cada corrida).
- Al terminar se detuvieron las APIs de prueba (:5177, :5178, :5181–5184), se borraron la sesión XE, la instantánea y las bases
  `Teikem_l42` y `Teikem_l42s`. Los programas quedaron en el directorio temporal de la sesión.
