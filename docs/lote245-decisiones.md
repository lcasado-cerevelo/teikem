# Lote 245 — Cadenas de clientes (Lote B del plan de rutas, cadenas y precios): qué se construyó y decisiones a revisar

Fecha: 2026-11-25. Módulo: Órdenes de venta (`SALES_ORDERS`). Permiso nuevo: `sales.chain`. Fuente funcional: `docs/plan-rutas-cadenas-precios-ivu-modificaciones.md` §3.2 (el documento maestro no habla de cadenas y no se editó).

> Este documento lo escribió un agente que **no corrió** build, pruebas, servidores ni el smoke: solo leyó el diff sin commitear y el plan aprobado. Los resultados de «Cómo se prueba» son los que le pasó quien encargó el cierre. Lo que no se probó está dicho abajo.

## Mapa de lo construido

| Capa | Tablas | Código principal | Endpoints |
|---|---|---|---|
| Cadena (catálogo) | **ClientChain** (nueva): nombre único entre las activas, notas, `IsActive`, `RowVersion`; `UQ_ClientChain_IdTenant`, `UX_ClientChain_Name` (filtrado por `IsActive = 1`), `UX_ClientChain_PublicId` | `ClientChain.cs` (+ `ClientChainCodes`), `ClientChainConfigurations.cs`, `ClientChainService` (lista, ficha, crear, editar, `SetActiveAsync`), `ClientChainRules` (reglas puras y mensajes) | `GET/POST /api/v1/client-chains`, `GET/PATCH /client-chains/{id}`, `POST .../deactivate`, `POST .../reactivate` |
| Cadena del cliente | `Client.ChainId` (NULL = sin cadena), `FK_Client_Chain` **compuesta** `(ChainId, TenantId)`, `IX_Client_Chain` filtrado | `ClientChainService.AssignAsync / RemoveAsync / SetClientChainAsync`, `ClientChainQueries` (único punto de lectura de la cadena) | `GET /client-chains/{id}/clients`, `POST .../clients`, `POST .../clients/remove`, `PUT /client-chains/by-client/{clientId}` |
| Alta masiva por nombre | (usa las anteriores; no usa `ImportBatch`) | `ClientChainService.PreviewBulkAsync / ApplyBulkAsync`, `ClientChainRules.PrefixKey / MatchesPrefix / Classify / Fingerprint` | `POST /client-chains/{id}/bulk-assign/preview`, `POST .../bulk-assign/apply` |
| Filtro y columna «Cadena» | — | `ClientService` (lista y ficha), `SalesOrderService` (lista), `ClientListItemDto`, `ClientDetailDto`, `SalesOrderListItemDto` | `GET /clients?chainPublicId=&noChain=`, `GET /sales-orders?chainPublicId=` (repetible) |
| Análisis por cadena | — (lee historial, facturas y notas de crédito del 241) | `ClientChainAnalysisService`, `ClientChainRules.SalesByChain / Series`, `SalesAnalysisChainController` (controlador nuevo, mismo prefijo que `SalesAnalysisController`, que no se tocó) | `GET /api/v1/sales-analysis/by-chain`, `GET /sales-analysis/by-chain/series` |
| Fuentes de datos | — | `ClientChainDataSource` (`CLIENT_CHAIN`, se lee con `clients.read`); `ChainId`, `ChainName` y la relación `Chain` en `CLIENT`; `ChainName` en `SALES_ANALYSIS_LINE` («Sin cadena» si no hay); `ChainId` y `ChainName` en vivo en `SALES_ORDER` | Vistas, indicadores y gráficos |
| Permisos y bitácora | `LookupCode` `EntityType` = `CLIENT_CHAIN` (orden **198**), `Permission` `sales.chain` (categoría `BILLING`) | `PermissionCatalog.SalesChain`, plantilla `SalesSupervisor`, `MenuPermissions.EditImplies[sales.chain] = clients.read`, `[AuditEntity("CLIENT_CHAIN")]` | — |
| Web | — | `web-app/src/features/chains/` (`ChainsScreen`, `ChainDetailPanel`, `ChainFormModal`, `ChainClientsPanel`, `ChainBulkAssign`, `ChainSalesSection`, `ClientChainPanel`, `ChainSalesTab`, `chainRules.ts`, `chainsApi.ts`, `chains.css`), integraciones en Clientes, Órdenes de venta y Análisis de ventas, i18n es/en | — |
| Migración de datos | — | Sección «Lote 245» al final de `Diseño/logistica-db-update.sql` (solo agrega, idempotente, con `GO` por sentencia; marca `LB_CLIENT_CHAIN_PERMS`) | — |

`TenantId` sale de la sesión (`RequireTenantId()`); `ClientChain` es `ITenantScoped` y recibe el filtro global. Toda resolución va por `PublicId` sobre DbSets filtrados: lo que no es de la compañía da 404 sin decir cuál. La FK compuesta impide en SQL que un cliente apunte a la cadena de otra compañía; EF mapea solo la FK simple.

Reglas que se aplicaron:
- **Una cadena por cliente.** Cambiar de cadena es mover.
- **Una cadena no es una ruta.** No se tocó `SalesRoute` ni se creó ninguna cadena sola: todos los clientes quedan «Sin cadena».
- **Sin estatus.** La cadena solo tiene activa/inactiva; no pasa por `StatusService`.
- **Guardas de activar y desactivar en un solo lugar** (`SetActiveAsync`, con `DeactivationBlockersAsync`): desactivar exige cero clientes **activos**; reactivar revisa el nombre.
- **Carrera desactivar ↔ asignar:** toda asignación «toca» la cadena (`UpdatedAtUtc`) para que el UPDATE viaje con `RowVersion`; desactivar cuenta dentro de su transacción. Gana el primero; el segundo recibe 409. `UpdatedBy`/`UpdatedAtUtc` van con `[NotAudited]`, así que el toque no llena la bitácora.
- **Alta masiva:** el prefijo y los nombres se comparan en memoria con `NameKey` (sin acentos, mayúsculas, puntos y comas como espacio), no con la intercalación de SQL Server. Palabra completa por omisión, mínimo 3 letras, tope 2000, solo clientes activos. Aplicar exige la **huella** de la simulación y la recalcula dentro de la transacción. Las exclusiones **no** entran en la huella.
- **Análisis:** la venta se atribuye a la cadena **actual** del cliente (también la del historial importado); venta = producto + descuentos; la suma de las cadenas más «Sin cadena» es la venta neta del mes (`Difference` = 0).
- **Sin Brevo ni correos:** ningún paso del lote manda correo.

### Cambios respecto al plan aprobado (los vio el diff)
- La ruta del menú quedó con `order: 20` (el plan decía 15) y permiso `sales.chain|clients.read` (el plan decía lo mismo para la ruta; `sales.view` solo cuenta para los endpoints de lectura, no para el menú).
- El `EntityType CLIENT_CHAIN` quedó con orden **198** (el plan decía 197, «el siguiente libre»).
- `Lote243SqlTests.cs` **no** aparece en el diff de este lote: el Lote 244 ya lo había acotado a su propio encabezado, así que no hizo falta tocarlo.
- La web trae dos componentes que el plan no nombraba: `ChainFormModal.tsx` y `ChainClientsPanel.tsx`.

## Cómo se prueba

Resultados reportados (no corridos por quien escribe este documento):

| Qué | Resultado |
|---|---|
| `dotnet build Teikem.sln` | OK |
| `dotnet test Teikem.sln` con SQL Server | 6269 pruebas, 0 fallas |
| `npm run check` en `web-app` | **3 fallos iniciales:** 2 de `navigation.test.ts` por el ítem nuevo «Cadenas» del menú (corregidos en el lote: catálogo +1 ítem y +1 ruta con menú) y 1 **preexistente y ajeno** al lote, `DispatchMap.test.tsx` (sigue fallando; ya fallaba en el Lote 244) |
| Playwright `web-app/e2e/lote245-cadenas.spec.ts`, 4 recorridos, API de pruebas **sin correo** | En verde: escritorio 1440 px (es/claro y en/oscuro) y celular 390 px (es/oscuro y en/claro), con capturas vistas |
| `scripts/smoke-cadenas.sh` | **No se corrió.** Queda para el CI (paso nuevo en `.github/workflows/ci.yml`) |
| `db-update` dos veces seguidas con el CLI | **No se probó aquí.** Sí hay una prueba dotnet (`ClientChainSqlTests`) que corre la sección SQL dos veces sobre SQL Server; se omite sin `TEIKEM_TEST_SQLSERVER`. Quien reportó dice que corrió con SQL Server |
| Prueba de volumen con 700 clientes (copia de la base de Solutions) | **No se hizo.** La hará el dueño (ver decisión 24) |

Pruebas dotnet nuevas (en `tests/Teikem.Tests`): `ClientChainRulesTests`, `ClientChainCatalogTests`, `ClientChainQueriesTests`, `ClientChainServiceTests`, `ClientChainSqlTests`, `ClientChainsControllerSecurityTests`, `ClientChainAnalysisTests`, `SalesAnalysisChainControllerSecurityTests`, `ClientChainListFilterTests`, `ClientChainListFilterSqlTests`. Cambiadas: los conteos de permisos 161 → 162 en `FleetCatalogTests`, `OrderCatalogTests`, `TripCatalogTests`, `WmsCatalogTests` y `Analytics/PulseLayoutTests`. Web: `chainRules.test.ts`, `ChainsScreen.test.tsx`, `chainsEmbeds.test.tsx` y `navigation.test.ts` (ajustado).

Recorrido Playwright (`lote245-cadenas.spec.ts`): crea o reutiliza «Walgreens P7B» y «CVS P7B» con sus clientes, importa un historial de septiembre y octubre de 2026, y mide: sin scroll horizontal de página, filas de la lista de cadenas de 56 px o menos, y que la ficha no se desborde. Las capturas del manual están en `docs/manual/frontend/img/l245-*.png` (6 de las que toma el recorrido: lista y ficha, simulación, filtro de Clientes, filtro de Órdenes y «Por cadena»).

> **Captura de Órdenes de venta:** repetida esperando a que carguen las filas; muestra el filtro y la columna «Cadena», y el recorrido ahora comprueba ambos.

Para repetirlo a mano (API **sin** llave de Brevo: `scripts/api-para-pruebas.ps1`, con `Auth__Onboarding__Enabled=false`):

1. `dotnet run --project src/Teikem.Api -- db-update` dos veces. La segunda no debe cambiar nada.
2. Encender `SALES_ORDERS` en la compañía (viene apagado por omisión; depende de `WMS_LOTSERIAL`).
3. Listas → Cadenas → «+ Nueva cadena» «Walg1». Repetir con «walg1»: 409 «Ya hay una cadena activa con ese nombre.».
4. Crear los clientes «Walg1 1», «Walg1 2», «Walg1boro» y asignar «Walg1 2» a otra cadena.
5. Ficha de la cadena → «Agregar por nombre» → «Walg1» → «Simular». «Walg1boro» no aparece (palabra completa); «Walg1 2» sale «Se salta (otra cadena)». Desmarcar una casilla: pasa a «Excluido» sin volver a simular. Comprobar que `GET /clients?chainPublicId=` sigue en 0 (la simulación no escribe).
6. «Aplicar a N clientes». Simular y aplicar otra vez: 0 asignados (idempotente).
7. Desactivar con clientes activos: 409 con el conteo.
8. Clientes y contratos: filtro «Cadena» (una cadena o «Sin cadena»). Ficha del cliente → panel «Cadena».
9. Análisis → Análisis de ventas → «Por cadena»: «Diferencia» debe dar $0.00.
10. Con un usuario sin `sales.chain`: no salen los botones de escritura y las llamadas de escritura dan 403.

## Decisiones a revisar

Vienen del plan aprobado; las marcadas «(código)» se comprobaron además en el diff.

1. **Un cliente, una cadena** (`Client.ChainId`). Alternativa: tabla puente para varias cadenas (complica el «gana el más bajo» del Lote C). (código)
2. **Sin código de cadena.** Nombre de hasta 100 caracteres, único entre las **activas** sin distinguir mayúsculas, acentos, puntos ni comas; una inactiva puede repetir nombre y reactivarla con el nombre tomado da 409. Notas hasta 500. (código)
3. **Sin estatus**, solo activa/inactiva (como `SalesRoute`). Alternativa: dominio `ChainStatus` con `StatusService`. (código)
4. **Desactivar exige cero clientes activos.** Los inactivos conservan su cadena. Para el Lote C, una cadena inactiva cuenta como «sin cadena». Las guardas viven en `SetActiveAsync`. (código)
5. **`sales.chain` administra** (crear, editar, activar, desactivar, asignar, quitar, cambiar desde la ficha del cliente, alta masiva). **Ver** queda con `sales.chain|clients.read|sales.view`. `sales.chain` implica `clients.read`. Alternativa: dos permisos. (código)
6. **`sales.chain` va a `TenantAdmin` y `SalesSupervisor`** y se propaga en SQL a los roles ya clonados. `SalesOffice` y `Sales` no lo reciben. (código)
7. **Módulo `SALES_ORDERS`** para los endpoints de cadenas y de análisis por cadena; el filtro y la columna en `GET /clients` siguen en `CATALOG` y la web los muestra solo con `SALES_ORDERS` encendido. (código)
8. **Menú Listas → «Cadenas»** justo después de Clientes y contratos, con la ficha en panel lateral. (código; `order: 20`)
9. **El alta masiva es un endpoint propio**, no usa `ImportBatch`. Coincide por el inicio del nombre normalizado, palabra completa por omisión, mínimo 3 letras, tope 2000, solo clientes activos (en cualquier estatus), los de otra cadena se saltan salvo «mover», y aplicar exige la huella. (código)
10. **Las exclusiones no entran en la huella.** Se marcan y desmarcan sin volver a simular; al aplicar se valida que cada excluido esté en el conjunto simulado. (código)
11. **Columna y filtro «Cadena» solo en Órdenes de venta**, con la cadena **actual** del cliente en vivo (sin copia en la orden): si el cliente cambia de cadena, sus órdenes viejas salen con la nueva. Alternativa: copiar `ChainId` en la orden al solicitar, como el 244 hace con la ruta. (código)
12. **La venta se atribuye a la cadena actual**, también la del historial de QuickBooks; al mover un cliente, su historial se reagrupa. Venta = producto + descuentos. (código)
13. **El análisis vive en `/api/v1/sales-analysis/by-chain`** con un controlador nuevo, para no tocar `SalesAnalysisController`. (código)
14. **La ficha de la cadena no muestra «Precios especiales» ni «Ofertas»**: las agrega el Lote C como paneles nuevos.
15. **La cadena del cliente se cambia en `PUT /client-chains/by-client/{id}` con `sales.chain`**, no en `PATCH /clients/{id}/profile` (`clients.update`), porque a partir del Lote C decide precios. (código)
16. **Sin campos personalizados ni contactos** en la cadena; la fuente `CLIENT_CHAIN` se lee con `clients.read`. (código)
17. **El modal «+ Nuevo cliente» no pide cadena**; se asigna después.
18. **La cadena no se ve en el portal ni en la app del cliente**, ni la conoce Kemi del portal: el capítulo del manual lleva `kemi:staff`, no `kemi:portal`.
19. **«Toque» optimista para la carrera desactivar ↔ asignar**, con `UpdatedBy`/`UpdatedAtUtc` `[NotAudited]`. Alternativa: solo recontar al desactivar, o aislamiento SERIALIZABLE. (código)
20. **FK compuesta solo en SQL** (`FK_Client_Chain`); EF mapea la simple. Alternativa: mapear la compuesta en EF (sin precedente en el proyecto). (código)
21. **El documento maestro no se edita** (precedente: el 241 tampoco).
22. **Número del lote y marca SQL.** El lote es el 245; la marca SQL no lleva número (`LB_CLIENT_CHAIN_PERMS`) y la sección se ancla por texto. `EntityType CLIENT_CHAIN` con orden 198. (código)
23. **`SalesAnalysisLineDataSource` recibe ahora `TeikemDbContext`** en el constructor (para el mapa cliente → cadena); las pruebas que lo construyen a mano ya van con el cambio. `SalesOrderDataSource` no cambió de constructor. (código)
24. **Prueba de volumen: la hace el dueño.** El plan (§5) pide probar la simulación con unos 700 clientes de una copia de la base de Solutions. Sí hay una prueba xunit con 2500 clientes sintéticos (`Bulk_preview_with_2500_clients_marks_too_many_and_is_fast` en `ClientChainServiceTests`: 2001 coincidencias marcan `TooMany`). El dueño corre la prueba con 700 clientes reales en su máquina de desarrollo, o entrega la copia. Nunca contra TEST ni producción. Nota de diseño: la simulación trae **todos los clientes activos de la compañía** a memoria y compara ahí; con unos 700 es poco, pero crece con la cartera.
25. **Cada alta masiva aplica a lo sumo 2000 clientes**; con más coincidencias hay que usar un prefijo más largo (400).

## Contrato para el Lote C

- `Client.ChainId`: una cadena por cliente. Una cadena inactiva cuenta como «sin cadena» para precios.
- `ClientChainQueries` (`src/Teikem.Infrastructure/Clients`) es el punto de lectura de la cadena: `ChainsByIdAsync`, `ClientChainMapAsync`, `ChainIdByPublicIdAsync`, `ChainIdsByPublicIdsAsync`.
- `ClientChainService.SetActiveAsync` / `DeactivationBlockersAsync` es donde se agrega «tiene precios u ofertas publicadas».
- La ficha de la cadena se arma con paneles independientes: el Lote C agrega «Precios especiales» y «Ofertas» sin rehacerla.

## Lo que queda fuera de este lote (a propósito)

- **Precios especiales y ofertas por cadena** (`PriceListChain`, `OfferChain`): Lote C.
- **Portal y app del cliente:** la cadena no se muestra ahí.
- **Copia de la cadena en la orden de venta** y columna de cadena en envíos o facturas.
- **Cadena en el alta del cliente** (modal «+ Nuevo cliente»).
- **Varias cadenas por cliente** y **código de cadena**.
- **Campos personalizados y contactos** de la cadena.
- **Manual en inglés** (`docs/manual/en/`) y las secciones nuevas en los capítulos [02](manual/02-clientes-y-contratos.md) y [51](manual/51-analisis-de-ventas.md): este cierre solo trae el capítulo [53](manual/53-cadenas.md), la FAQ y las pantallas ([L245](manual/frontend/l245-cadenas.md)). El plan los pedía; quedan pendientes.
- **`smoke-cadenas.sh`** y la **prueba de volumen**: ver arriba.
