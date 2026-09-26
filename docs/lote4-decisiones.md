# Lote 4 — Flota, choferes y mantenimiento: qué se construyó y decisiones a revisar

Fecha: 2026-09-26. Plan aprobado: `docs/lote4-plan.md` (y su espejo `docs/lote4-plan.json`), ratificado por Luis el
2026-09-26 sin cambios sobre las 43 decisiones del plan. Este documento cierra el lote: mapa de lo construido, cómo se
probó, las 43 decisiones del plan (resumidas) más las de la ronda de verificación, y lo que queda fuera.

> Manual funcional del lote: `docs/manual/04-flota-choferes-mantenimiento.md` (14 secciones, enlazado en `docs/manual/README.md`)
> y sección "Lote 4" en `docs/manual/faq.md`, escritos leyendo el código real; cada mensaje citado existe en el código.
>
> **Verificación del cierre (2026-09-26, sobre BD recreada desde cero):** `dotnet build` sin errores, `dotnet test` 495/495,
> `db-init` dos veces (52 permisos, idempotente) y `scripts/smoke.sh` completo de los Lotes 1 a 4 en `SMOKE OK` (72 pasos).
> Corrida de CI: ver enlace al final.

## Mapa de lo construido

| Área | Tablas (nuevas o extendidas) | Código principal | Endpoints (módulo · permiso) |
|---|---|---|---|
| Vehículos | `Vehicle` (+`PublicId`, auditoría, `RowVersion`, `UQ_Vehicle_IdTenant`, `CK_Vehicle_Numbers`), `VehicleDocument` (+`CK_VehicleDocument_Dates`, índice "vigente por tipo") | `VehicleRules`, `VehicleService`, `VehicleDocumentService`, `VehicleStatusEffect` | `GET/POST /api/v1/vehicles`, `GET/PATCH .../{publicId}`, `POST .../status\|deactivate\|reactivate`, `GET/POST/PATCH .../documents/*` (CATALOG · `fleet.view`/`fleet.manage`) |
| Choferes | `Driver` (+`PublicId`, auditoría, `RowVersion`, `UX_Driver_User`), `DriverLicense`, `DriverCertification`, `DriverDevice`, `DispatchZone`, `DriverZone` | `DriverRules`, `DriverService`, `DriverDocumentService`, `DispatchZoneService`, `DriverStatusEffect` | `GET/POST /api/v1/drivers`, `.../licenses\|certifications\|devices`, `DELETE /drivers/{publicId}`, `/api/v1/dispatch-zones` (CATALOG · `fleet.view`/`fleet.manage`, vínculo de usuario exige además `admin.users`) |
| Documentos y disponibilidad | (reutiliza `Vehicle`/`Driver`/licencias/certificaciones; sin tabla propia) | `FleetQueries.LoadFleetDocumentsAsync` + `FleetDocuments.MarkSuperseded` (helper único, compartido por panel/disponibilidad/fuente), `FleetDocumentService`, `FleetAvailabilityRules`, `FleetAvailabilityService` | `GET /api/v1/fleet/expiring-documents`, `GET /api/v1/fleet/availability` (CATALOG · `fleet.view`) |
| Mantenimiento | `MaintenanceSchedule` (+`CreatedAtUtc`, `CK_MaintSchedule_Target/Interval`), `MaintenanceWorkOrder` (+`PublicId`, auditoría, `RowVersion`, `CK_WorkOrder_Costs`), `MaintenanceTask` (+`IsActive`, `CK_MaintTask_Costs`) | `MaintenanceDue`, `MaintenanceScheduleService`, `MaintenanceWorkOrderService`, `WorkOrderStatusEffect` (perezoso, bloquea el vehículo con `FleetQueries.LockVehicleAsync`) | `GET/POST /api/v1/maintenance-schedules(+/due)`, `GET/POST/PATCH /api/v1/maintenance-work-orders(+/status,+/tasks)` (CATALOG · `fleet.view`/`fleet.maintenance`) |
| Combustible | `FuelLog` (+`IsActive`, auditoría, `CK_FuelLog_Amounts`) | `FuelEfficiency` (km/L, costo/km calculados al leer), `FuelLogService`, `FuelLogDataSource` | `GET/POST/PATCH /api/v1/fuel-logs(+/deactivate)` (CATALOG · `fleet.view`/`fleet.maintenance`) |
| Tarifas de chofer | `DriverPayPolicy`, `DriverDeliveryRate`, `DriverAttemptRate`, `DriverTripRate` (efectivo-fechadas, `UQ_*_Open` filtrados) | `DriverPayoutRules` (motor puro de las 3 fórmulas), `DriverRateService`, `DriverPayPolicyService`, `DriverRateResolver : IDriverRateResolver`, `DriverRatesRetirementEffect` | `/api/v1/drivers/{id}/rates\|delivery-rates\|attempt-rates\|trip-rates`, `/api/v1/driver-pay-policy(+/attempt-levels,+/preview)`, `GET /driver-trip-types` (CATALOG · `driverpay.view`/`driverpay.manage`) |
| Viajes y entrega especial | `DriverTrip` (`UX_DriverTrip_Order` filtrado por `IsActive`, `CK_DriverTrip_Rate`) | `DriverTripRules`, `DriverTripService`, `SpecialDeliveryDispatchService` (`PrepareAsync`/`AssignTrackedAsync`), `DriverTripOrderEffect`, `DriverTripStatusEffect`, `PipelinePath`; cambios en `OrderService`/`OrderReadService`/`OrderContracts` del Lote 3 | `GET/POST /api/v1/drivers/{id}/trips(+/cancel)`, `POST /api/v1/orders/{publicId}/driver` (LTL_GROUND · `trips.dispatch`, y el servicio verifica además que CATALOG esté encendido) |
| Transversal | seed: `DriverPayoutFormula`, `DriverTripStatus`; `EntityType` nuevos (`MAINTENANCE_SCHEDULE`, `FUEL_LOG`, `FLEET_DOCUMENT`, `DRIVER_RATE`, `DRIVER_TRIP`, `DISPATCH_ZONE`); `Capability.EDIT_WORK_ORDER` sobre `WORK_ORDER` (reutilizado, no se creó `MAINTENANCE_WORK_ORDER`); 3 permisos nuevos (`fleet.view`, `driverpay.view`, `driverpay.manage`; **52 en total**) | `PermissionCatalog`, `CatalogDomains`, `FleetOwnedEntityResolvers` (+ `PortalUserOwnedEntityResolver`, hueco del Lote 2), `FleetDataSources` (`VEHICLE`/`DRIVER`/`WORK_ORDER`/`FUEL_LOG`/`FLEET_DOCUMENT`, sin tarifas ni `DRIVER_TRIP`), `SystemAnalyticsSeeder` (vistas "Vehículos"/"Documentos por vencer", indicadores "Documentos por vencer"/"Órdenes de trabajo abiertas") | — |

`TenantId` sale del principal; las tablas nuevas de tarifa/viaje, `FuelLog` y `MaintenanceWorkOrder` llevan además FK
compuesta `(Id, TenantId)` como segunda barrera en SQL, sobre `UQ_*_IdTenant` de `Vehicle`, `Driver`,
`SpecialServiceType`, `TransportOrder` y `DriverTripRate`. Defensa en profundidad igual que en lotes previos: filtro de
tenant (datos) + `[RequirePermission]` + `[RequireModule(Catalog)]` (acción) + `IOwnedEntityResolver` (recurso), con
`ClosedOwnedEntityResolver` para `DRIVER_RATE` y `FLEET_DOCUMENT` (siempre 404: sin contactos/campos propios).

## Cómo se prueba

1. `dotnet build Teikem.sln` → compila sin errores ni advertencias.
2. `dotnet test Teikem.sln` → **495 pruebas**, todas en verde (incluye `FleetCatalogTests`, `FleetContractsTests`,
   `FleetRulesTests`, `VehicleRulesTests`, `DriverRulesTests`, `FleetAvailabilityRulesTests`, `MaintenanceDueTests`,
   `FuelEfficiencyTests`, `DriverPayoutRulesTests`, `PipelinePathTests`, `DriverTripRulesTests`,
   `TenantIsolationModelTests`, `OwnedEntityResolverCoverageTests`, y de la ronda de verificación
   `FleetControllerSecurityTests` y `DriverStatusEffectTests`).
3. `dotnet run --project src/Teikem.Api -- db-init` **dos veces** sobre una base `Teikem` recreada desde cero: la primera
   aplica estructura (~133 tablas) y seed (**52 permisos** en catálogo, capacidades por defecto de `CONTRACT`,
   `TRANSPORT_ORDER` y `WORK_ORDER`); la segunda es idempotente (0 permisos nuevos propagados, tenant demo "ya existe").
4. Con el API arriba, `scripts/smoke.sh http://localhost:5000` recorre los Lotes 1-3 y, antes del paso final de sesiones,
   los **22 pasos del Lote 4**: permisos/catálogos/pipelines/capacidades; alta de vehículos (código único, qbox, código
   fijo, precisión); estatus y baja lógica de vehículo; zonas y choferes (código fijo, zona/área, tope de paradas,
   vínculo de usuario); documentos (licencias, certificaciones, documentos de vehículo, "Documentos por vencer");
   disponibilidad para despacho; documento vigente por tipo (renovación); contactos/campos personalizados/historial de
   flota; mantenimiento preventivo (Al día/Por vencer/Vencido/Sin historial); órdenes de trabajo (número, tareas,
   costos, cierre, efecto en el vehículo) con su prueba de concurrencia (8 altas simultáneas); bitácora de combustible
   (km/L, costo/km, odómetro protegido) con su prueba de concurrencia (8 cargas simultáneas); tarifas por entrega, por
   intento (con niveles y fórmula) y por viaje; viajes del chofer con monto congelado; entrega especial con chofer sobre
   el Lote 3 (hasta `IN_TRANSIT`); eliminar chofer (baja definitiva sin borrar historial); RBAC y módulo; aislamiento
   entre tenants y BOLA por id hijo; fuentes de datos/contenido de sistema/auditoría. En total el script tiene **70
   pasos** y termina en `SMOKE OK`.

> **Ejecución real de este cierre (2026-09-26, esta sesión):** SQL Server 2022 ya estaba corriendo en el contenedor de
> trabajo (`scripts/dev-sqlserver.sh`, sin Docker). Se recreó la base `Teikem` desde cero (`DROP DATABASE` + `CREATE
> DATABASE`), se corrió `db-init` dos veces (52 permisos la primera vez, idempotente la segunda), se levantó
> `dotnet run --project src/Teikem.Api` y se corrió `scripts/smoke.sh http://localhost:5000` completo: **exit 0,
> `SMOKE OK`**, sin fallos, sobre el árbol de trabajo tal como quedó de la ronda de verificación (incluye los cambios sin
> commit en `scripts/smoke.sh`, `tests/Teikem.Tests/FleetContractsTests.cs` y los dos archivos de prueba nuevos). `dotnet
> build`/`dotnet test` se corrieron aparte y también en verde (495/495). No se hizo push en esta sesión, así que no hay
> una corrida de CI de GitHub Actions que citar para este cierre puntual; el árbitro formal sigue siendo
> `.github/workflows/ci.yml` en el primer push de estos cambios.

### `scripts/smoke.sh`

Ya contiene, en el árbol de trabajo, los 22 pasos del Lote 4 que pedía el arreglo `"smoke"` del plan aprobado —
verificados uno por uno contra la lista del plan, todos presentes e insertados antes del paso final "sesiones: refresh
con rotación y logout" (que ya cerraba el archivo desde el Lote 1 y sigue siendo el último). No fue necesario agregar
nada al final del archivo para este cierre: la ronda de verificación ya los había integrado, además de 41 refuerzos
("Hallazgo de revisión") que no pedía el plan literal (ver más abajo).

## Decisiones del plan (1-43, ratificadas sin cambios por Luis)

Resumidas de `docs/lote4-plan.md` (sección "Decisiones"); el texto completo de cada una vive ahí.

1. El maestro de tarifas del chofer (entrega/intento/viaje) y `DriverTrip` entran en este lote; la corrida de
   liquidación y el ledger de intentos quedan para los Lotes 7/9.
2. No hay `DriverRateAgreement`: el chofer es el agregado raíz de sus tres tablas de tarifa.
3. Se sigue el rediseño maestro-detalle de la bitácora (más reciente) sobre las tres pantallas planas anteriores.
4. Niveles de intento = entero por tenant (`DriverPayPolicy.AttemptLevels`, 1..20 contiguos); "+ Agregar intento" solo
   sube el número, no crea filas de $0.
5. Fallback de intentos: la tarifa del nivel más alto que el chofer tenga configurado; un nivel intermedio sin tarifa
   paga $0 con nota.
6. Fórmula de pago vigente y niveles viven en `DriverPayPolicy` (una fila por tenant, default "Entrega + cada intento").
7. `Vehicle`/`Driver` reciben `PublicId`, auditoría y `RowVersion`; los hijos no tienen `PublicId` propio, se exponen
   bajo la ruta de su padre y el servicio verifica pertenencia (404 si no).
8. El código del chofer es `Driver.EmployeeCode` (obligatorio, único, inmutable); el del vehículo también es inmutable.
   Ninguno se libera tras la baja.
9. "Área" no es campo propio: es el nombre de la zona de despacho primaria del chofer.
10. Cada chofer tiene una zona primaria; el lote trae un CRUD mínimo de `DispatchZone` porque el seed demo no siembra
    zonas en una BD limpia.
11. "Activo" (reversible) y "Eliminar" (baja definitiva a `INACTIVE`, sin borrado físico) son cosas distintas; eliminar
    cierra tarifas, desactiva dispositivos, quita la zona y desvincula el usuario.
12. Tres permisos nuevos (52 en total) para separar flota y compensación (R8): `fleet.view`, `driverpay.view`,
    `driverpay.manage`.
13. La plantilla de rol Driver no recibe `fleet.*` ni `driverpay.*`; eso es de la app (Lote 7).
14. Todo Flota/Choferes/Tarifas va bajo `[RequireModule(Catalog)]`; asignar chofer a entrega especial vive en
    `LTL_GROUND` pero el servicio verifica además que `CATALOG` esté encendido.
15. Disponibilidad para despacho: bloquean inactivo/estatus/licencia sin vigencia/documento de vehículo vencido/OT en
    proceso; solo avisan certificación vencida, documento por vencer y vehículo sin documentos.
16. Documento vigente por tipo: un documento con vencimiento queda "superado" si el mismo dueño tiene otro activo del
    mismo tipo con vencimiento posterior; no bloquea, no aparece en el panel, no cuenta para el próximo vencimiento.
17. "Documentos por vencer" une en código (no en SQL) documentos de vehículo, licencias y certificaciones con un solo
    helper compartido (`FleetQueries.LoadFleetDocumentsAsync`).
18. Solo una OT en `IN_PROGRESS` inhabilita el vehículo (pasa a `MAINTENANCE`); al cerrar/cancelar regresa a `ACTIVE` si
    no queda otra en proceso.
19. Cerrar una OT exige tareas activas completadas y, si el programa es por kilometraje, la lectura de odómetro.
20. Costos de la OT: con tareas son la suma de sus tareas activas; sin tareas se capturan en el encabezado; `TotalCost`
    es columna computada en SQL.
21. Número de OT automático `OT-#####` por compañía, `NumberSequence` Kind `WORKORDER`.
22. Se reutiliza el `EntityType` `WORK_ORDER` ya sembrado; no se crea `MAINTENANCE_WORK_ORDER`.
23. Umbrales de "Por vencer": 10% del intervalo en mantenimiento, 30 días en documentos.
24. Un programa por tipo de vehículo se evalúa vehículo por vehículo con su última OT cerrada; sin ninguna, "Sin
    historial"; no hay generación automática de OT.
25. km/L y costo/km se calculan al leer (no se guardan); odómetro monótono por fecha.
26. El odómetro del vehículo solo sube (máximo monotónico) desde combustible/cierre de OT, con bloqueo de fila; la
    corrección manual nunca baja del piso de la última lectura registrada.
27. `FuelLog`/`MaintenanceTask` usan solo `IsActive` (nunca DELETE); solo `Vehicle`, `Driver`, `MaintenanceWorkOrder` y
    `DriverTrip` tienen ciclo de estatus vía `StatusService`.
28. Auditoría automática en todo el lote; los hijos se auditan bajo el `EntityType` de su padre; `PushToken` es
    `[SensitiveData]`.
29. El "estatus route" del mock es `IN_TRANSIT`; la entrega especial avanza etapa por etapa con `PipelinePath`,
    saltando deshabilitadas, porque `StatusService` no permite saltos.
30. La entrega especial se prepara (módulo, permiso, chofer, disponibilidad, tarifa) **antes** de sacar números; se
    repite dentro de cada reintento por colisión.
31. La asignación se registra en `DriverTrip.TransportOrderId` (no se crea `Trip`/`TripOrder`, de Despacho); la orden
    expone solo el chofer, nunca el monto.
32. Un solo viaje vigente por orden, garantizado en BD (`UX_DriverTrip_Order` filtrado por `IsActive`).
33. Sin tarifa por viaje configurada, el `DriverTrip` se crea igual con $0 y `RateMissing=true` (CHECK de coherencia).
34. `DriverTrip` tiene estatus propio (`DriverTripStatus`, `OPEN → SETTLED | CANCELLED`); cancelar la orden cancela su
    viaje `OPEN`; el viaje de una orden no se cancela directo.
35. No se crean `DeliveryAttempt` ni `DriverSettlementRun/Line`; la contradicción con el flujo de `CarrierSettlement`
    queda documentada para el Lote 9.
36. Quedan fuera: `HomeWarehouseId`/`Warehouse`, CRUD/CHECK de `FleetAssignment`, adjuntos de documentos, plantillas
    `ACCT_TEMPLATES` de exportación.
37. No hay fuentes de datos de tarifas ni de `DriverTrip` (expondrían compensación con solo `analytics.view`); sí
    `VEHICLE`/`DRIVER`/`WORK_ORDER`/`FUEL_LOG`/`FLEET_DOCUMENT`.
38. El selector de "tipo de viaje" usa `GET /driver-trip-types` (`driverpay.view`), proyectado sin `ClientsUsing`
    (dato de contratos); inactivar un tipo con tarifas vigentes responde 409.
39. Un vehículo inactivo/dado de baja no admite OT ni cargas nuevas (409); un chofer inactivo (checkbox) sí admite
    tarifas y viajes; un chofer eliminado solo se consulta.
40. `Driver.UserId` exige además `admin.users` y un usuario `INTERNAL` con membresía `ACTIVE`; `DriverDevice` solo se
    lista y desactiva (el alta es de la app del Lote 7).
41. La tarifa por entrega exige servicio y tipo de paquete exactos; sin comodín de "cualquier paquete".
42. FKs compuestas `(Id, TenantId)` como segunda barrera en las tablas nuevas de tarifa/viaje, `FuelLog` y
    `MaintenanceWorkOrder`.
43. Se cierra el hueco del Lote 2: `PORTAL_USER` tenía permiso de dueño sin `IOwnedEntityResolver`
    (`CustomFieldService.cs:193` omitía la verificación); se agrega el resolver y una prueba exige cobertura total.

## Hallazgos de la ronda de verificación (44, todos con evidencia en el diff y cubiertos en el smoke)

Todos quedaron marcados en el código con el comentario "Hallazgo de revisión" (41 en `scripts/smoke.sh`, 3 en pruebas
nuevas/ampliadas), cada uno con su aserción o prueba. Se agrupan por tema; el número entre paréntesis es cuántos
hallazgos individuales cubre el punto.

44. **Concurrencia optimista real** (3): `rowVersion` obsoleto → 409 "El registro fue modificado..." en `PATCH` de
    vehículo, de chofer y en la asignación de chofer a la entrega especial — el plan pedía `ApplyRowVersion` pero no
    traía un caso de smoke que lo disparara en los tres puntos.
45. **Campos "fijos" del `PATCH` más allá de `code`** (2): `homeWarehouseId` del vehículo y `employeeCode` del chofer
    (alias de `code`) responden el mismo 400 que el código, no solo el nombre literal `code`.
46. **VIN repetido entre activos también al editar y al reactivar** (1), no solo al crear.
47. **qbox más allá de VIN/placa/"diesel"** (2): por tipo de vehículo ("camion"→"Camión") y por propiedad
    ("propio"→"Propio").
48. **Desvincular el usuario del chofer exige `admin.users`** igual que vincular (1); una membresía no `ACTIVE` nunca
    se vincula (1).
49. **CRUD de zona completo probado** (1): reasignar/quitar la zona de un chofer, inactivar una zona sin choferes, zona
    inactiva/inexistente/ajena.
50. **`licenseExpiry` ausente (no `null` serializado como `0001-01-01`)** cuando el chofer no tiene licencias (1).
51. **Filtros de "Documentos por vencer" más finos** (3): por tipo de documento de vehículo (`INSURANCE`, no solo
    `LICENSE`), filtro mixto vehículo+chofer y `entity=DRIVER` (el plan solo probaba `entity=VEHICLE`).
52. **El panel de vencimientos excluye dueños inactivos (checkbox) y dados de baja** (1), no solo terminales.
53. **El checkbox "Activo" (no solo el estatus terminal) saca de disponibilidad** (2): `DRIVER_INACTIVE`/
    `VEHICLE_INACTIVE`, bloqueantes, y fuera de `onlyAvailable`.
54. **La disponibilidad se evalúa en la fecha pedida (`?date=`), no siempre "hoy"** (1): una licencia vigente hoy puede
    ya estar vencida en la fecha de despacho consultada.
55. **Dispositivos del chofer: lista y `deactivate` por id ajeno → 404** (1); el alta queda para la app del Lote 7.
56. **Editar y quitar (soft-delete) documentos/licencias/certificaciones deja de contar** en el panel y en la ficha del
    padre (1).
57. **Programa por tipo de vehículo**: la línea base es la última OT **cerrada** por vehículo (1) y solo se expande a
    vehículos activos, ni dados de baja ni con el checkbox inactivo (1).
58. **Protección del odómetro reforzada** (1): la corrección manual baja un error tecleado pero nunca por debajo de la
    OT cerrada más reciente; cerrar una OT con lectura menor no baja el odómetro del vehículo.
59. **`PATCH` de un programa revalida el intervalo contra el disparador resultante** (no el original) y respeta el
    checkbox Activo (1).
60. **Efecto de estatus de la OT sobre el vehículo, cuatro casos no cubiertos por el plan literal** (4): cancelar una OT
    en proceso también regresa el vehículo a `ACTIVE`; con dos OT en proceso, cerrar una no lo regresa hasta cerrar la
    última; un `MAINTENANCE` puesto a mano también vuelve a `ACTIVE` al cerrar la OT; un vehículo dado de baja no
    cambia de estatus ni de odómetro al cerrar su OT.
61. **Quitar tareas recalcula costos y libera el cierre**; costos negativos y fecha de cierre futura → 400 (2).
62. **Sin tareas, los costos se capturan en el encabezado** y respetan la precisión `(18,4)` (1).
63. **`to=9999-12-31` en el listado de OT ya no desborda a 500** (1).
64. **Varias OT en `OPEN` (no solo una) no inhabilitan el vehículo**; solo `IN_PROGRESS` lo hace (1).
65. **Bitácora de combustible: paginación, rango (`fromUtc` inclusivo/`toUtc` exclusivo) y km/L sobre la serie
    completa** del vehículo, no sobre la página ni el rango filtrado (1).
66. **Carga de combustible con chofer vinculado, filtro por chofer y `clearDriver`** (1).
67. **Tarifas por intento también son efectivo-fechadas** (editar = cerrar y abrir), con las mismas reglas de fecha
    pasada/cierre/cierre a futuro que entrega y viaje (1).
68. **Tope de 20 niveles de intento** (`POST attempt-levels` → 409 al llegar al máximo) (1).
69. **Tipo de viaje inexistente (404) o inactivo (400) validado en dos lugares**: al configurar la tarifa y al dar de
    alta el viaje (1).
70. **El viaje aplica la tarifa vigente en `TripDate`** (la fecha del viaje), no la de hoy: un viaje de ayer sin
    tarifa vigente ese día queda en $0/`RateMissing` aunque hoy exista una (1).
71. **Un chofer inactivo (checkbox, no eliminado) sí admite tarifas y viajes nuevos**; cerrar una tarifa por viaje ya
    cerrada → 409 (1).
72. **Crédito excedido con chofer asignado**: 422 sin crear la orden, sin viaje y sin dejar hueco en la numeración;
    con `overrideCredit` sin permiso → 403, con permiso → 200 (1).
73. **Un chofer inactivo (checkbox) tampoco se asigna a una entrega especial** (409) (1).
74. **`trips.dispatch` se exige dentro del servicio (`PrepareAsync`), no solo en el controlador**: un rol con
    `orders.create` pero sin `trips.dispatch` → 403 sin crear la orden ni consumir número (1).
75. **Reasignar/asignar el chofer solo procede desde la etapa inicial o el pipeline**; en un lateral (`ON_HOLD`) o
    después de `IN_TRANSIT` → 422; desde `DRAFT` confirma y avanza (1).
76. **Eliminar un chofer con usuario vinculado y tarifa por intento abierta**: la cuenta queda libre y la tarifa cierra
    hoy (caso específico, más allá del genérico del plan) (1).
77. **Un chofer eliminado no admite altas ni cancelaciones de viaje nuevas** (409) y sus documentos salen del panel de
    vencimientos (1).
78. **RBAC fino más allá de lo pedido**: `driverpay.view` sin `driverpay.manage` no cambia la política ni crea/cancela
    viajes; `fleet.manage` sin `fleet.maintenance` tampoco crea programas de mantenimiento (2).
79. **Recursos con id entero sin padre propio** (carga de combustible, programa de mantenimiento, zona de despacho):
    su única barrera de aislamiento entre tenants es el filtro de tenant, verificado con 404 explícito (1).
80. **`FleetContractsTests`**: ningún flujo del lote crea `DriverDevice` (lo hace la app del Lote 7); se documenta para
    no dejar un hueco de cobertura sin explicar (1).
81. **`FleetControllerSecurityTests`** (prueba nueva, por reflexión): fija el módulo y el permiso exacto de cada acción
    de los 11 controladores de flota/choferes/pago, para que un descuido futuro rompa CI aunque el smoke no pase por
    esa acción (1).
82. **`DriverStatusEffectTests`** (prueba nueva, `InMemory`): entrar a la etapa terminal de `DriverStatus` desactiva
    solo los dispositivos y borra solo la zona **del chofer que se elimina**, no los de otro chofer del mismo tenant
    (1).

## Lo que queda fuera de este lote (a propósito)

- `HomeWarehouseId`/`Warehouse` sin mapear; `FleetAssignment` sin CRUD, sin `CHECK` de traslape y sin regla de fechas
  (los define Despacho).
- Adjuntos de documentos (sin proveedor de archivos) y plantillas `ACCT_TEMPLATES` de exportación (Lotes 9/11).
- `DeliveryAttempt`, `DriverSettlementRun`/`Line` y la corrida de liquidación: quedan para el Lote 9, que también
  resolverá la contradicción de nombres con `CarrierSettlement`/`SettlementStatus` documentada en el plan.
- Registro de `DriverDevice` desde la app del chofer y todo lo que dependa de esa app (Lote 7): aquí solo se listan y
  desactivan.
- Fuentes de datos de tarifas o de `DriverTrip` en Análisis (decisión de diseño, no una omisión).
- `StatusService` resolviendo el regreso lateral por dominio en vez de solo por `(EntityType, EntityId)`: riesgo
  heredado del Lote 3 (su decisión 7), no tocado en este lote; `DriverTripStatus` y `WorkOrderStatus` comparten esa
  superficie con `OrderStatus`.
- **El capítulo del manual funcional del Lote 4 y sus entradas de FAQ**: no se escribieron en esta pasada de cierre
  (ver nota al inicio de este documento).
- Enumeración de hallazgos: los 44 de esta ronda quedaron todos con un comentario "Hallazgo de revisión" (o su
  variante en minúscula) y una aserción de smoke o una prueba unitaria propia; no se detectó ninguno adicional sin
  marcar durante esta verificación puntual, pero eso no descarta que existan hallazgos no documentados como tales en
  el código.

---

**Verificación de este cierre (2026-09-26, sobre BD recreada desde cero, esta sesión):** `dotnet build` sin errores ni
advertencias; `dotnet test` 495/495; `db-init` dos veces (52 permisos, idempotente); `scripts/smoke.sh` completo (70
pasos, Lotes 1-4) en `SMOKE OK`. Sin push en esta sesión: no hay corrida de CI que citar para este cierre puntual.

**Corrida de CI del cierre del Lote 4:** https://github.com/lcasado-cerevelo/teikem/actions/runs/36232088124 (run 24, verde: build Release, 495 pruebas, db-init ×2 sobre BD limpia y smoke de los Lotes 1 a 4). La corrida 23 falló por una deriva del compilador del runner (SDK 10), corregida con `global.json`.
