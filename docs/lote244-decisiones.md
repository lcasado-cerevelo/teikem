# Lote 244 — Rutas de venta (Lote A del plan de rutas): qué se construyó y decisiones a revisar

Fecha: 2026-11-25. Módulo: Órdenes de venta (`SALES_ORDERS`). Sin permisos nuevos (se reutilizan `sales.analysis.view|manage` y `sales.create`).

> Este documento lo escribió un agente que **no corrió** build, pruebas, servidores ni el smoke: solo leyó el diff sin commitear. Los resultados de "Cómo se prueba" son los que le pasó quien encargó el cierre. Lo que no se probó está dicho abajo.

## Mapa de lo construido

| Capa | Tablas | Código principal | Endpoints |
|---|---|---|---|
| Personas de la ruta (titular y apoyo, con historial) | **SalesRouteAssignment** (nueva), `UQ_SalesRoute_IdTenant` en SalesRoute | `SalesRouteEntities.cs`, `SalesRouteRules.cs` (reglas puras), `SalesRouteAssignmentOps` (escrituras compartidas), `SalesRouteService` | `GET/POST /api/v1/sales-analysis/routes/{id}/people`, `POST .../people/{assignmentId}/close`, `POST .../people/{assignmentId}/void`, `GET /sales-analysis/sellers/candidates` |
| Venta del cliente | `Client.SalesRouteId` (ruta directa), `Client.PreferredSalesUserId` | `SalesRouteService.GetClientSalesAsync / SetClientSalesAsync / AssignClientsRouteAsync`, `SalesSellerQueries.TeamAsync`, `ClientService` (ficha con ruta efectiva y preferido) | `GET/PUT /sales-analysis/clients/{clientId}/sales`, `POST /sales-analysis/clients/assign-route`, `GET /sales-analysis/clients?routeId=&noRoute=` |
| Pasar rutas desde representantes | (usa las anteriores) | `SalesRouteService.RoutesFromRepsAsync` (misma regla que la migración SQL, con simulación) | `POST /sales-analysis/routes/from-reps?dryRun=true` |
| Representantes con usuario y titular automático | `SalesRep` (índice `UX_SalesRep_User`) | `SalesAnalysisSetupService` (usuario obligatorio, `BecamePrimary`) | `GET /sales-analysis/reps?withoutUser=`, `POST/PATCH /sales-analysis/reps`, `.../reactivate` |
| Vendedor de la orden | `SalesOrder.SalesRouteId`, `SellerUserId`, `SalesCopyAtUtc` | `SalesOrderService.Seller.cs` (proponer, escoger, congelar al solicitar), `SalesOrderEmailContent` (el vendedor del correo sale de la copia) | `GET /sales-orders/seller-options?client=`, `sellerUserId` / `clearSeller` en crear y editar el borrador, filtros `routeId` y `sellerUserId` en la lista |
| Cuota por ruta o por persona | `SalesQuota.SalesRouteId` (y `SalesRepId` ahora NULL), `SalesAnalysisSetting.QuotaBy` (REP \| ROUTE) | `SalesAnalysisSetupService` (cuotas, ajustes), `SalesRouteRules.ParseQuotaBy` | `GET /sales-analysis/quotas?by=`, `PUT /sales-analysis/quotas/{month}` (renglón con `repId` o `routeId`), `GET /sales-analysis/routes/{id}/quotas`, `PUT /sales-analysis/settings` (`quotaBy`) |
| Análisis por ruta | (lee todo lo anterior) | `SalesAnalysisRules.Aggregate` (agregador genérico), `SalesRouteRules.Attribute` (atribución), `SalesAnalysisService.GetByRouteAsync` | `GET /sales-analysis/by-route`, `GET /sales-analysis/transactions?routeId=`; `by-rep` se conserva |
| Fuentes de datos | — | `SalesRouteDataSource` («Rutas de venta», `SALES_ROUTE`), `SalesRouteAssignmentDataSource` («Personas de la ruta», `SALES_ROUTE_ASSIGNMENT`), ambas con `sales.analysis.view` | Vistas, indicadores y gráficos |
| Necesita tu atención | — | `SalesRouteNoPrimaryAttentionProvider` (`SALES_ROUTE_NO_PRIMARY`, motivos `NO_PRIMARY` / `INACTIVE_PRIMARY`) | `/api/v1/attention` (el que ya existía) |
| Migración de datos | `AppliedMigration` `L244_SALES_ROUTES_FROM_REPS` | Sección «Lote 244» de `Diseño/logistica-db-update.sql` (solo agrega; idempotente) | — |
| Web | — | `ByRouteTab`, `RoutePeoplePanel`, `RoutesFromRepsDialog`, `ClientsNoRoutePanel`, `ClientSalesPanel` (reemplaza a `ClientSalesRepPanel`, borrado), campo «Vendedor» en `OrderModal`, columnas y filtros en `SalesOrdersScreen`, `attention.ts`, i18n es/en | — |

`TenantId` sale de la sesión; el filtro global de EF aplica a `SalesRouteAssignment` (`ITenantScoped`) y las consultas lo repiten explícito. Auditoría: `SalesRouteAssignment` lleva `[AuditEntity("SALES_ROUTE_ASSIGNMENT")]`; el EntityType se siembra en el SQL. Nunca DELETE: cerrar pone `ValidTo`, anular pone `IsActive = 0`.

Reglas del dueño aplicadas:
- **«Cadenas» es una ruta más.** No hay código ni caso especial para ella.
- **`by-rep` se conserva** y la pestaña **«Por ruta» es la vista por omisión** (sin `?tab=`).
- **Vendedor preferido** = persona activa, titular o apoyo vigente, de la ruta del cliente. Un cliente sin ruta no admite preferido. Mensajes: «El cliente necesita una ruta antes de asignarle un vendedor preferido.» y «El vendedor preferido debe ser una persona activa de la ruta del cliente.». Si después deja de pertenecer a la ruta **no se bloquea nada**: la ficha muestra la etiqueta «El vendedor preferido ya no pertenece a la ruta del cliente.».
- Sin Brevo ni correos: ningún paso de este lote manda correo; el correo de la orden (Lote 243) solo cambió de dónde saca al vendedor.

## Cómo se prueba

Resultados reportados (no corridos por quien escribe este documento):

| Qué | Resultado |
|---|---|
| `dotnet build Teikem.sln` | OK |
| `dotnet test Teikem.sln` con SQL Server | 6215 pruebas, 0 fallas |
| `npm run check` en `web-app` | 3961 de 3962 pasan. Falla `DispatchMap.test.tsx`, que **ya fallaba antes y es ajeno al lote** |
| Playwright (`web-app/e2e`), 4 recorridos, API de pruebas sin correo | En verde, con capturas en claro y oscuro, español e inglés |
| `scripts/smoke-rutas.sh` | **No se corrió.** Queda para el CI (paso nuevo en `.github/workflows/ci.yml`) |
| Idempotencia del SQL | **No se probó aquí.** El CI corre `db-update` dos veces (paso nuevo) |

Pruebas dotnet nuevas (archivos nuevos en `tests/Teikem.Tests`): `SalesRouteRulesTests`, `SalesRouteServiceTests`, `SalesRoutesSqlTests`, `SalesRouteTestSeed` (datos de apoyo), `SalesRouteDataSourceTests`, `SalesRouteAttentionTests`, `SalesAnalysisByRouteTests`, `SalesQuotaByRouteTests`, `SalesRepAutoPrimaryTests`, `SalesSellerQueriesTests`, `SalesOrderSellerTests`, `SalesSellerNoticeContractTests`, `ClientSalesDetailTests`; cambiadas: `Lote243SqlTests`, `PulseSalesPanelTests`, `SalesAnalysisRulesTests`, `SalesAnalysisServiceTests`, `SalesOrderEmailContentTests`. Web: `SetupTab.routes.test.tsx` (nueva) y cambios en `OrderModal.test.tsx`, `ordersFilters.test.ts`, `SalesAnalysisScreen.test.tsx`, `salesAnalysisEmbeds.test.tsx`, `salesAnalysisExcel.test.ts`, `salesAnalysisRules.test.ts`.

Recorridos Playwright: `lote244-rutas-configuracion.spec.ts`, `lote244-ventas-por-ruta.spec.ts` (2 pruebas) y `lote244-cliente-y-orden.spec.ts`. Comprueban, entre otras cosas, fila de ruta ≤ 48 px, fila de orden ≤ 64 px, el campo «Vendedor» dentro de la ventana y sin scroll horizontal. Las capturas usadas en el manual están en `docs/manual/frontend/img/l244-*.png` (regeneradas el 2026-11-25 contra una base limpia).

Para repetirlo a mano (API **sin** llave de Brevo: `scripts/api-para-pruebas.ps1`):

1. `dotnet run --project src/Teikem.Api -- db-update` dos veces. La segunda no debe cambiar nada.
2. Entrar como administrador de la compañía. En Análisis → Análisis de ventas → Configuración → Rutas, crear la ruta «Norte» y pulsar «Personas».
3. Agregar una persona como Titular y otra como Apoyo. Agregar otro Titular: el anterior debe quedar cerrado en el historial.
4. Configuración → Representantes: crear uno **sin** usuario: debe dar «Escoja el usuario del representante: el vendedor siempre es un usuario de Teikem.» (400). Crearlo con usuario y con la ruta «Norte» sin titular: `becamePrimary = true`.
5. «Pasar rutas desde representantes» → «Simular» (no guarda nada: las rutas no cambian) → «Aplicar».
6. Ficha de un cliente → panel «Venta»: sin ruta, intentar un vendedor preferido → 400 con el mensaje de arriba. Con ruta, escoger una persona de la ruta → se guarda. Cerrar a esa persona en la ruta → la ficha muestra la etiqueta de aviso y nada se bloquea.
7. Crear una orden de venta en borrador del cliente: el campo «Vendedor» propone al preferido; escoger a alguien que no es de la ruta → 400 «El vendedor debe ser el vendedor preferido del cliente o una persona vigente de su ruta.». Solicitar: la orden guarda ruta y vendedor; cambiar después la ruta del cliente no la toca.
8. Configuración → Ajustes: «La cuota se mide por» → «Ruta de venta». Cargar cuotas por ruta en Cuotas. Análisis → «Por ruta»: la conciliación debe dar diferencia 0,00.
9. «Por representante» sigue abierta y funcionando.
10. Con un usuario sin `sales.analysis.manage`: los botones de escritura no salen y las llamadas de escritura dan 403.

## Decisiones tomadas (revisar)

1. **Una ruta de venta no es la ruta de entrega ni el viaje.** Es el grupo de clientes y de personas que los atienden. «Cadenas» es una ruta más. Lo que el plan llame «Lote B» (agrupar por cadena) no está: el agregador (`SalesAnalysisRules.Aggregate`) ya recibe la clave de agrupación como función y las líneas traen `ClientKey` para que el Lote B no lo reescriba.
2. **El vendedor siempre es un usuario de Teikem.** El representante ahora **exige usuario**: es obligatorio al crear (400) y no se puede quitar (`clearUser` → 400). Un representante heredado sin usuario se puede renombrar o cambiar de ruta sin mandarlo. Un usuario solo puede estar en **un** representante activo (409; índice `UX_SalesRep_User`). Alternativa: permitir representantes sin usuario y atribuirles venta como antes.
3. **Vigencias en días de la compañía, intervalo medio abierto `[ValidFrom, ValidTo)`.** No se programan asignaciones a futuro (`ValidFrom` futura → 400). Un titular nuevo no puede empezar antes que el actual.
4. **Un solo titular vigente por ruta** y una sola fila vigente por persona y ruta, garantizado por índices únicos filtrados. Poner un titular nuevo **cierra** al anterior con `ValidTo` = el inicio del nuevo; si la persona era apoyo, se cierra esa fila. El titular no puede ser apoyo a la vez (409 «La persona es la titular de esta ruta: cierre su asignación antes de ponerla de apoyo.»). Un choque entre dos administradores da 409 «Otra persona cambió las personas de la ruta al mismo tiempo: recargue e intente de nuevo.».
5. **Cerrar no es anular.** Cerrar = la persona dejó la ruta (el historial cuenta hasta esa fecha). Anular = «se registró por error»: `IsActive = 0`, no cuenta en historial ni en atribución. Se puede anular una cerrada. No hay forma de reabrir una cerrada ni de desanular.
6. **Titular automático.** Al crear un representante, ponerle usuario, cambiar su ruta o reactivarlo, si su ruta está activa y **sin titular**, su usuario queda de titular desde hoy en la misma transacción (`becamePrimary` en la respuesta y aviso en pantalla). Si la ruta ya tiene titular no se toca. Alternativa: que el administrador siempre escoja.
7. **Atribución de la venta** (`SalesRouteRules.Attribute`). Ruta: la copia de la orden → la ruta directa del cliente → la de su representante. Persona: el vendedor copiado en la orden → el representante heredado (activo y de **la misma ruta**) → el titular de la ruta **el día de la línea** → «Sin vendedor». Un usuario que es representante cuenta como ese representante (paridad con el Lote 241).
8. **Vendedor de la orden.** En borrador se **propone**: preferido activo → titular activo → quien crea la orden, solo si es persona vigente de la ruta → ninguno. Se puede escoger solo entre el preferido y las personas vigentes y activas de la ruta (400 si no). Al **solicitar** se congela la copia (ruta efectiva, vendedor, momento). El regreso de un despacho a «Solicitada» y cancelar no la tocan. **Las órdenes solicitadas antes del lote quedan con ruta y vendedor en NULL: no se rellenan.** Alternativa: rellenarlas con la regla de hoy (cambiaría la atribución histórica).
9. **Vendedor preferido** (ajuste del dueño, 2026-11-25). Se valida **al asignarlo**, no después. Detalles que conviene confirmar:
   - Mandar el mismo preferido que ya tiene no se vuelve a validar (así guardar la ficha con otro cambio no falla si ya salió de la ruta).
   - Quitarlo siempre vale.
   - En un mismo `PUT` el preferido se valida contra la ruta **ya cambiada**.
   - «Asignar ruta» en bloque y desactivar una ruta **no tocan** el preferido: la ficha lo señala.
   - Si el preferido ya no es persona activa de la ruta, el vendedor propuesto sigue siendo él mientras sea persona activa de la compañía (`ProposeSeller` solo exige `PreferredActive`). Ver la observación 2 de «Observaciones de las capturas».
10. **Cuota por ruta o por persona** (`SalesAnalysisSetting.QuotaBy`, `REP` por omisión, así que nada cambia para quien no lo toque). Las dos clases de cuota **conviven** en `SalesQuota` (`CK_SalesQuota_Target`: representante o ruta, uno de los dos) y el ajuste escoge cuál cuenta. Con `ROUTE`, el total es la suma de las cuotas de ruta; con `REP`, cada representante una vez. Para crear el índice filtrado `UX_SalesQuota_RepMonth` se quita el índice viejo (sin filtro) y se vuelve a crear **con el mismo nombre**: es lo único que el SQL quita.
11. **`UX_SalesRep_User` se crea solo si hoy no hay usuarios repetidos** en representantes activos; si los hay, el SQL imprime un aviso y lo crea en la siguiente corrida. El servicio ya rechaza el repetido (409) aunque el índice aún no exista.
12. **Migración única + botón.** El SQL pasa una sola vez (marca `L244_SALES_ROUTES_FROM_REPS`) las rutas de los representantes a sus clientes y deja titulares y apoyos. «Pasar rutas desde representantes» hace lo mismo desde la pantalla, **idempotente y con simulación por omisión**. Sin datos de ventas, el SQL pone `ValidFrom` = **ayer** y el servicio pone **hoy** (decisión cerrada por el dueño: «da igual»); con datos, los dos usan el primer día del mes más antiguo con historial, factura o cuota. El servicio además no traslapa el cierre de una asignación anterior.
13. **Desactivar una ruta** da 409 si tiene representantes activos o clientes activos con esa ruta **propia** (los que la heredan por representante quedan cubiertos por la regla de representantes). Si pasa, **cierra hoy** sus personas vigentes; reactivar **no** las reabre.
14. **Cuota de ruta y «Sin ruta ni vendedor».** La conciliación de «Por ruta» es: total de rutas + «Sin ruta ni vendedor» + envío y otros = total del mes. «Sin ruta» (persona sin ruta) sale como una fila más; «Sin ruta ni vendedor» solo en la conciliación.
15. **Contrato con el correo de la orden (Lote G).** Con copia (`SalesCopyAtUtc`), el vendedor del correo es `SellerUserId` y se manda **solo si es persona activa**; si no, el envío queda `SKIPPED`. El preferido y las personas de la ruta no entran ahí. `SalesSellerQueries.NoticeUsersAsync` (preferido + titular + apoyos activos) quedó listo para el Lote D, pero **en este diff nada lo consume todavía**.
16. **Sin permisos nuevos.** No hay nada que propagar a roles ya clonados. Leer con `sales.analysis.view|manage`; escribir con `sales.analysis.manage`; `GET /sales-orders/seller-options` con `sales.create`. El buscador de personas (`sellers/candidates`) devuelve el correo y exige `manage`; `seller-options` no devuelve correos.
17. **CI.** Se agregó `db-update` dos veces (idempotencia de la sección) y `scripts/smoke-rutas.sh` (sin correo). Ninguno se corrió en esta sesión.

### Observaciones de las capturas (a revisar o arreglar)

1. **La etiqueta del campo «Vendedor» de la orden usa de dónde sale la opción, no por qué se propone.** En `l244-orden-en-light.png` se lee «Carlos Rivera · Proposed (route support)» aunque en la ficha del mismo cliente es el **preferido** y la propuesta viene de ahí (`proposedKind = PREFERRED`). Si la persona es a la vez apoyo de la ruta, la opción se rotula por su papel en la ruta. Conviene mostrar «preferido del cliente» cuando `isPreferred` sea cierto.
2. Las capturas se tomaron contra una base limpia creada solo para eso (sin las rutas «E2E» y «P7D» de corridas anteriores). Las vistas de celular (360 px) no tienen scroll horizontal de página; en «Por ruta» la tabla se desplaza dentro de su propio recuadro.
3. **Hallazgo corregido al capturar «Necesita tu atención»:** `SalesRouteNoPrimaryAttentionProvider` no estaba registrado en `DependencyInjection.cs` y la fila «Ruta de venta sin titular: {ruta}» nunca salía (las pruebas lo instancian a mano). Ya se registró y el recorrido de `lote244-rutas-configuracion.spec.ts` pasa; captura en `img/l244-atencion-es-light.png`.
4. La ficha en la captura oscura muestra el botón «Guardar» atenuado (sin cambios pendientes), es lo esperado.

### Cosas del diff que conviene mirar antes del commit

- `web-app/src/kernel/i18n/es.json` y `en.json` aparecen con ~330 líneas borradas cada uno. Se comparó el conjunto de llaves contra `HEAD`: **237 llaves nuevas en cada idioma, ninguna quitada ni cambiada**; el resto es reformato del archivo. Lo mismo, a otra escala, en `openapi.json` y `schema.d.ts` (regenerados). `helpIndex.json` cambia en su única línea (regenerado).
- `ClientSalesRepPanel.tsx` está borrado y en el área de preparación (`git status` lo marca `D `), el resto del lote no.

## Lo que queda fuera de este lote (a propósito o sin probar)

- **Lote B: agrupar por cadena** (o cualquier grupo de clientes). Solo quedó el agregador preparado.
- **Lote D: avisos al preferido y al equipo de la ruta.** Falta conectar `NoticeUsersAsync` a las reglas de aviso.
- **Rellenar** ruta y vendedor en órdenes anteriores al lote.
- **Asignaciones a futuro** y reabrir una asignación cerrada.
- **Crear el usuario desde la pantalla de representantes:** hay un enlace a Usuarios (con `users.view`); el usuario se crea allá.
- **No probado:** `scripts/smoke-rutas.sh`, la idempotencia del SQL en CI, el envío real del correo de la orden con el vendedor copiado (no se usa Brevo; para verlo haría falta un buzón local), y las apps (`app-cliente`, `app-movil`, `app-almacen`: no hay cambios suyos en el diff).
- **Capturas:** completas, incluida la fila de «Necesita tu atención» y las de celular, en `docs/manual/frontend/img/l244-*.png`.
- El fallo de `DispatchMap.test.tsx` es previo y no se tocó.

## Manual

- Capítulo: [52 — Rutas de venta](manual/52-rutas-de-venta.md) (personas de la ruta, ficha del cliente, vendedor de la orden, cuota por ruta, análisis por ruta, «Pasar rutas desde representantes», «Necesita tu atención»).
- Pantallas: [L244](manual/frontend/l244-rutas-de-venta.md).
- FAQ: sección «Lote 244» en [faq.md](manual/faq.md). Índice: [README](manual/README.md).
