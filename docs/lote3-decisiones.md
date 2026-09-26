# Lote 3 — Órdenes de transporte: qué se construyó y decisiones a revisar

Fecha: 2026-09-25. Plan aprobado: `docs/lote3-plan.md` (y su espejo `docs/lote3-plan.json`), con los ajustes de Luis del
2026-09-25 (cliente dado de baja, portal multi-cliente, crédito con permiso, importador de órdenes). FAQ del lote:
`docs/manual/faq.md`, sección "Lote 3 — Órdenes de transporte". Manual funcional: `docs/manual/03-ordenes-de-transporte.md`
(enlazado en `docs/manual/README.md`); el capítulo 02 recibió dos correcciones que este lote volvió necesarias (cliente dado de
baja y efectos de estatus del portal multi-cliente).

## Mapa de lo construido

| Área | Tablas (nuevas o extendidas) | Código principal | Endpoints (módulo · permiso) |
|---|---|---|---|
| Órdenes | `TransportOrder` (+ `ClientInvoiceNumber` NOT NULL, `ClientInvoiceNumberTyped`, `PackBatchNumber` NOT NULL, `IsSpecialDelivery`/`SpecialServiceId`, `SourceEntityTypeLookupId`/`SourceEntityId`, `QuotedAtUtc`/`ConfirmedAtUtc`, `CK_Order_Amounts`, `CK_Order_Special`; `UX_Order_Number` por cliente reemplaza `UQ_Order_Number`, `UX_Order_PackBatch`, `IX_Order_Invoice`, `IX_Order_Client`), `OrderStop` (+ `IX_OrderStop_Location`), `CargoLine` (+ `PackageTypeLookupId`, `PackageNumber`, `CK_CargoLine_Quantity`, `IX_CargoLine_Order`), `OrderReference` | `OrderRules`, `ConsigneeMatch`, `OrderQueries` (`OrderScope`), `OrderService`, `OrderReadService`, `OrderQuoteService`, `OrderStatusEffect`, `OrderStatusService`, `QuotedOrdersBalanceProvider`, `TransportOrderDataSource`, resolvers `TRANSPORT_ORDER`/`ORDER_STOP`/`ORDER_COD` | `GET/POST /api/v1/orders`, `GET /orders/lookup`, `GET/PATCH/DELETE /orders/{publicId}`, `GET .../quote`, `POST .../confirm|reprice|cancel|status` (LTL_GROUND · `orders.view/create/edit/cancel`) |
| Numeración | `NumberSequence` (nueva; contador atómico `TenantId, Kind, ClientId`) | `NumberingRules` (puro), `NumberSequenceService` (`EnsureAsync` autocommit + `NextAsync` `UPDATE…OUTPUT` dentro de la transacción) | — (interno de `OrderService`) |
| Crédito y cotización | — (usa `TransportOrder.QuotedAmount`/`QuotedAtUtc`) | `CreditRules` (puro), `OrderQuoteLines` (puro), `IClientBalanceProvider`/`QuotedOrdersBalanceProvider` | `GET /orders/{id}/quote`; `POST /orders/{id}/confirm` con `overrideCredit` (permiso `orders.credit_override`, ver decisión 8) |
| Importador de órdenes | `ImportTemplate` (nueva, `UQ_ImportTemplate`), `ImportBatch` (nueva) | `CsvParser`/`ImportMapping` (puros), `ImportTemplateService`, `OrderImportService`, resolvers `IMPORT_TEMPLATE`/`IMPORT_BATCH` | `GET/POST /api/v1/import-templates`, `GET/PATCH .../{publicId}`, `POST .../deactivate|reactivate`; `POST /api/v1/orders/import/validate`, `GET .../{batchPublicId}`, `POST .../{batchPublicId}/confirm|discard` (LTL_GROUND · `orders.edit`/`orders.view`/`orders.create`) |
| Cliente dado de baja (ajuste A) | — | `ClientQueries.EnsureClientActive` (helper único) en `OrderService.CreateAsync`, `PortalUserService.InviteAsync/ReinviteAsync`, `ContractService.CreateAsync`, `RateService.AddComponentAsync/AddTierAsync`, `SpecialServiceService.AddAsync` | mismos endpoints del Lote 2 y `POST /orders`; 409 `ClientQueries.ClientInactiveMessage` |
| Portal multi-cliente (ajuste B) | `PortalUser` (`UQ_PortalUser` pasa de `(TenantId, Email)` a `(TenantId, ClientId, Email)`) | `PortalUserService` (invitar agrega cliente a la cuenta), `PortalUserStatusEffect` (desactiva/reactiva la cuenta solo si no queda/vuelve a haber otra fila ACTIVE), `PortalUserContracts` (+ `HasPassword`, `ClientsCount`) | mismos endpoints de portal del Lote 2 |
| Transversal | seed: `EntityType` `ORDER_COD`/`ORDER_STOP`/`IMPORT_TEMPLATE`/`IMPORT_BATCH`; `StatusCode` `ImportBatchStatus` (VALIDATED/CONFIRMED/DISCARDED); `StatusCapability` por defecto de `TRANSPORT_ORDER` (`EDIT_CARGO` solo en DRAFT, `REPRICE` solo en CONFIRMED, `CANCEL`/`ASSIGN_TRIP` apagados en terminales); permiso `orders.credit_override` (categoría `ORDERS`, 49 en total) | `SystemAnalyticsSeeder` (vista "Órdenes", indicadores "Órdenes en curso"/"COD por cobrar", gráfico "Órdenes por estatus"); `PermissionCatalog` (`OwnerRead/WritePermission` para `TRANSPORT_ORDER`/`ORDER_STOP`/`ORDER_COD`/`IMPORT_BATCH`/`IMPORT_TEMPLATE`) | — |

Todo `TenantId` sale del principal; `OrderStop`/`CargoLine`/`OrderReference` (sin `TenantId`) se alcanzan solo a través de la
orden resuelta bajo el filtro global (`OrderQueries.ResolveOrderForRead/WriteAsync`). Todas las lecturas y escrituras de
Órdenes reciben un `OrderScope(int? ClientId)`; el controlador interno pasa `OrderScope.Any` (el portal del Lote 8 pasará el
`ClientId` del principal).

## Cómo se prueba

1. `dotnet build Teikem.sln` → compila sin errores ni warnings.
2. `dotnet test Teikem.sln` → **261 pruebas**, todas en verde (catálogo/contratos por reflexión, `NumberingRules`,
   `OrderRules`, `ConsigneeMatch`, `OrderQuoteLines`, `CreditRules`, `CsvParser`, `ImportMapping`, más
   `Lote3HardeningTests`/`CreditExceededExceptionTests`/`DuplicateInvoiceExceptionTests` de la ronda de verificación).
3. `dotnet run --project src/Teikem.Api -- db-init` **dos veces** sobre la misma BD: la primera aplica estructura y seed
   (permisos sembrados: **49** en catálogo), la segunda es idempotente (0 permisos nuevos propagados, tenant demo "ya existe").
4. Con el API arriba, `scripts/smoke.sh http://localhost:5000` recorre los Lotes 1 y 2 y, antes del paso de sesiones, el
   bloque del Lote 3 (20 pasos + el paso extra del importador): prerrequisitos (cliente con contrato/tarifas, servicio
   especial, consignatarios), entrada rápida con defaults del tenant y numeración/snapshot de paradas, concurrencia del
   contador (8 altas simultáneas → 8 números consecutivos, cero 409/500), quién asigna cada número y colisión
   automático/tecleado, validaciones de captura y estado del cliente, multi-paquete y consignatario coincidente/al vuelo,
   factura repetida (R36) al crear y al cambiar de consignatario, vista previa y confirmar (cotiza y congela), crédito
   (aviso + autorización con permiso) y tarifa faltante, edición (solo DRAFT, campos fijos, re-cotización con REPRICE),
   reprecio y estatus laterales, cancelar y eliminar, buscar/escanear y listado paginado, entrega especial, aislamiento
   entre tenants, RBAC de `orders.*` y rutas polimórficas, fuentes de datos/contenido de sistema/auditoría, cliente dado de
   baja (ajuste A), portal multi-cliente (ajuste B) e importador de órdenes (ajuste D, con crédito por fila del ajuste C).

> Ejecución real (2026-09-25, este cierre): `dotnet build` (0 errores), `dotnet test` (261/261), `db-init` dos veces sobre
> SQL Server 2022 local (contenedor de trabajo, sin Docker: `scripts/dev-sqlserver.sh`) y `scripts/smoke.sh
> http://localhost:5000` completo, terminando en `SMOKE OK` (exit 0), sobre una BD persistente (no recreada desde cero en
> esta corrida puntual; el runner de esquema/seed y la doble ejecución de `db-init` ya se verificaron idempotentes). No se
> hizo push en esta sesión, así que **no hay una corrida de CI de GitHub Actions que citar** para este cierre (a diferencia
> de los Lotes 1 y 2); el árbitro formal sigue siendo `.github/workflows/ci.yml` en el primer push.

### `scripts/smoke.sh`

Ya contiene, en el árbol de trabajo (sin commit), los pasos de Órdenes/ajustes A-D del plan (verificado línea por línea
contra el arreglo `"smoke"` del plan aprobado: los 21 `step` de Órdenes + los de cliente dado de baja, portal multi-cliente
e importador están todos presentes, más varios refuerzos que no pedía el plan literal — p. ej. país ISO-2, topes de
columnas numéricas, catálogo deshabilitado, `contacts.manage` sin `orders.edit`, entidades `ORDER_STOP`/`ORDER_COD`/
`IMPORT_*` sin resolver público de contactos salvo el propio). No fue necesario agregar nada al final del archivo para
este cierre.

## Ajustes de Luis (2026-09-25) aplicados

- **(A) Cliente dado de baja**: se bloquea solo lo nuevo con 409 `"El cliente está dado de baja; solo se consulta su
  historial."` (`ClientQueries.EnsureClientActive`, `OrderService.ClientInactiveMessage` es el mismo texto). Lecturas,
  cancelaciones y cierres siguen permitidos.
- **(B) Portal multi-cliente**: `UQ_PortalUser (TenantId, ClientId, Email)`; invitar desde otro cliente agrega una fila
  (activa de inmediato si la cuenta ya tiene contraseña, `SecurityEvent` `portal_client_added`); `PortalUserStatusEffect`
  solo desactiva/reactiva la cuenta cuando no queda/vuelve a haber otra fila ACTIVE del tenant.
- **(C) Crédito excedido = aviso + autorización con permiso**: permiso `orders.credit_override` (49 permisos en total);
  `POST /orders/{id}/confirm` sin `overrideCredit` exige `orders.edit`; con `overrideCredit=true` exige
  `orders.credit_override` y, si el crédito no se excedía de verdad, exige además `orders.edit` (`OrderStatusController.
  Confirm`, ver código citado abajo en la decisión 8).
- **(D) Importador de órdenes**: `ImportTemplate`/`ImportBatch`, validar → confirmar en dos pasos, reutiliza
  `OrderService.CreateAsync` fila por fila (una transacción por fila, con *savepoint* cuando corre dentro de la transacción
  ambiente de una fila anterior fallida).
- **(E) Ratificado sin cambios**: numeración por cliente, R36 por `AllowDupInvoice`, edición de carga solo en DRAFT por
  defecto, eliminar solo en la etapa inicial, Recolección y empaque/DriverTrip fuera del lote.

## Decisiones tomadas (revisar)

Las decisiones 1-28 y las cuatro del ajuste de Luis vienen resumidas de `docs/lote3-plan.md` (sección "Decisiones");
las que siguen (29 en adelante) salieron de la ronda de verificación sobre el código entregado.

1. **Recolección y empaque (PickBatch ad hoc) fuera del lote**: exige `Product`/`StockBalance`/`InventoryTransaction` sin
   mapear. Queda preparado `PackBatchNumber` siempre poblado y `SourceEntityTypeLookupId`/`SourceEntityId` (RefEntity/RefId)
   para que un lote futuro enlace la orden con el lote que la origine vía `OrderCreationOptions` (solo invocable desde
   código interno, nunca desde el DTO HTTP).
2. **Entrega especial en la orden** (`IsSpecialDelivery`, `SpecialServiceId` del catálogo del cliente vigente hoy, una
   `CargoLine` sin `PackageType`, sin paquetes ni COD); asignación de chofer y `DriverTrip` quedan para el lote 11A.
3. **Contador atómico `NumberSequence`** consumido con `UPDATE … OUTPUT` **dentro** de la transacción del alta (bloqueo de
   fila hasta el commit: sin huecos); la fila se asegura antes en autocommit (`EnsureAsync`); orden fijo de dibujo
   ORDER→INVOICE→PACKBATCH→PACKAGE; colisión automático/tecleado se resuelve saltando el valor con `NextAsync` en
   autocommit y reintentando (máximo 5).
4. **Número tecleado que "lo asigna Teikem" se rechaza con 400**, no se ignora en silencio; `packBatchNumber`/`codType` en
   la captura se rechazan con 400 vía `[JsonExtensionData]`.
5. **R36 (factura repetida) por consignatario** en el mismo `POST /orders`: bloqueado (`AllowDupInvoice=0`) o confirmable
   (`confirmDuplicateInvoice=true`, código `duplicate_invoice_confirmable`), con `errors.existingOrderNumber`/
   `existingOrderPublicId` sobre `TeikemException.Errors`.
6. **"Eliminar" una orden** (solo en la etapa inicial) es baja lógica `IsActive=0` sin transición de estatus: libera el
   número de orden y de empaque (índices únicos filtrados por `IsActive=1`).
7. **El ciclo COD nace bajo `EntityType` propio `ORDER_COD`** y las paradas bajo `ORDER_STOP` (no bajo `TRANSPORT_ORDER`)
   porque `StatusService` resuelve el regreso desde un lateral por `(EntityType, EntityId)` sin filtrar dominio; riesgo
   documentado y **no corregido en este lote** (queda para un lote futuro filtrar por dominio).
8. **Verificación de crédito como efecto de salir de la etapa inicial** hacia el pipeline (`OrderStatusEffect`), leyendo el
   saldo pendiente de `IClientBalanceProvider` (hoy `QuotedOrdersBalanceProvider` = Σ `QuotedAmount` de órdenes en curso).
   **Ajuste C**: el exceso no bloquea, avisa (422 `credit_exceeded`) y se autoriza con `overrideCredit=true` +
   `orders.credit_override`; verificado en `OrderStatusController.Confirm` (permiso condicional) y en
   `OrderStatusService`/`OrderStatusEffect` (defensa en profundidad: el permiso se revisa dos veces).
9. **Confirmar exige tarifa base vigente** para cada línea consolidada (422 si falta, nunca un `QuotedAmount` de $0);
   `QuotedAmount`/`QuotedAtUtc`/`ContractId` se congelan al confirmar; editar una orden ya cotizada re-cotiza y exige
   `REPRICE`. `GET /orders/{id}/quote` no persiste nada.
10. **Capacidades por defecto de `TRANSPORT_ORDER`**: `EDIT_CARGO` solo en DRAFT, `REPRICE` solo en CONFIRMED, `CANCEL`
    apagado en terminales, `ASSIGN_TRIP` apagado en DRAFT/terminales; sin entradas laterales por defecto (el tenant las
    restringe en `/status/lateral-entries/TRANSPORT_ORDER`).
11. **`POST /orders/{id}/status` solo registra laterales y el regreso al pipeline**; `CONFIRMED`/`CANCELLED` tienen sus
    propias acciones; los avances PICKUP…DELIVERED quedan reservados a Trips/POD (422 explícito).
12. **Todos los endpoints de Órdenes llevan `[RequireModule(LtlGround)]`** (núcleo, no apagable); sin permisos nuevos de
    lectura/escritura (siguen `orders.view/create/edit/cancel`).
13. **Como mucho dos paradas al nacer** (PICKUP opcional, DELIVERY obligatoria); consignatario resuelto por `PublicId`,
    por coincidencia normalizada nombre+línea 1 (`ConsigneeMatch`, nunca solo por nombre, nunca contra compartidas) o
    creado al vuelo asignado al cliente; snapshot completo (incluye `DeliveryNotes` y ventana horaria si hay
    `RequestedDate`, sin conversión de zona horaria — decisión 27).
14. **El paquete es una línea de la orden** (`CargoLine`, `PackageNumber` por línea); varias líneas del mismo tipo se
    permiten y se consolidan solo para mostrar/cotizar; PATCH reemplaza dando de baja las anteriores (`IsActive=0`).
15. **Cliente, número de orden, número de factura y número de empaque se fijan al crear**; el PATCH los rechaza con 400
    (`[JsonExtensionData]`), verificado por reflexión en `OrderContractsTests`.
16. **No se crean órdenes para un cliente dado de baja (409) ni suspendido (422)**, tampoco se confirma con el cliente
    suspendido. Extendido por el ajuste A a contratos, tarifas, servicios especiales e invitaciones de portal.
17. **Moneda de la orden y del COD**: la del cliente, si no la del contrato vigente, si no `NULL`.
18. **Listado paginado** (`skip`/`take`, 1..500, default 100) y `GET /orders/lookup` exacto (CI, sin parciales) sobre los
    tres identificadores (el número de orden puede repetirse entre clientes; `PackBatchNumber` es único por tenant).
19. **`OrderScope(int? ClientId)` en toda lectura/escritura** para dejar preparado el portal (Lote 8) sin reescribir
    consultas; el controlador interno pasa `OrderScope.Any`.
20. **`OrderReference` se administra por reemplazo completo**; `OrderDocument` no se mapea (sin proveedor de blob);
    `GeoPoint`/`GeocodeAccuracy` de la parada quedan `NULL`.
21. **Importador de órdenes por plantillas de posición de columna** (ajuste D, reemplaza la decisión original "sin
    importación CSV"): dos pasos validar→confirmar, reutiliza `OrderRules`/numeración/consignatario de `OrderService`.
22. **Fuente de datos `TRANSPORT_ORDER`** con relaciones `Client`/`Consignee` y contenido de sistema (vista "Órdenes",
    indicadores "Órdenes en curso"/"COD por cobrar", gráfico "Órdenes por estatus").
23. **Número de orden único por cliente** (`UX_Order_Number (TenantId, ClientId, OrderNumber)`), no por tenant; el
    identificador inequívoco de escaneo es `PackBatchNumber` (único por tenant).
24. **El efecto de cotización+crédito se dispara al salir de la etapa inicial hacia PIPELINE**, no atado al código
    `CONFIRMED`: sigue funcionando si el tenant deshabilita `CONFIRMED` (avanza a la siguiente etapa PIPELINE habilitada,
    verificado en el smoke: `CONFIRMED` deshabilitado → confirma a `PICKUP`).
25. **`ClientInvoiceNumber` `NOT NULL`** (string no anulable en los DTOs) porque Teikem siempre lo genera si queda en
    blanco.
26. **`confirmNow=true` en `POST /orders`** crea y confirma en una transacción; un 422/409 revierte también la creación.
27. **Ventanas de la parada** = `RequestedDate.Date` + `DefaultWindowStart/End` de la `Location`, sin conversión de zona
    horaria (columnas `*Utc` "naive"; el lote de rutas decidirá la conversión).
28. **`GET /orders/{id}/quote`** devuelve cotización + chequeo de crédito sin persistir, con los mismos errores (422/409)
    que confirmar.

Decisiones nuevas de la ronda de verificación (hallazgos corregidos y cerrados; el total reportado de hallazgos de esta
ronda es **54** — los que siguen son los que quedaron con evidencia verificable en el diff y en las pruebas nuevas, no una
enumeración exhaustiva de los 54):

29. **`ClientInvoiceNumberTyped` (columna nueva, no estaba en el plan)**: la orden recuerda si su número de factura se
    tecleó al crearla. Sin esto, R36 se re-evaluaría con el ajuste *actual* de "quién asigna la factura" al editar el
    consignatario, y una orden creada con factura tecleada podría dejar de chequearse si el cliente cambia después esa
    configuración. Documentado también en la FAQ del lote ("Cambié el consignatario... y no me avisó de factura
    repetida").
30. **Números automáticos que no caben en `NVARCHAR(40)`** (patrón largo del cliente + consecutivo con más dígitos que
    `#`; `Resolve` nunca trunca) ya no terminan en 500: `NumberingRules` expone `GeneratedTooLongMessage(kind)` y el
    servicio responde 409 dentro de la transacción del alta/edición (el rollback también devuelve los consecutivos
    dibujados). Cubierto en la FAQ.
31. **Topes de precisión numérica** (`packages[i].weightKg/volumeM3`, totales de cabecera, `codAmount`) validados con 400
    por campo antes de llegar a SQL, en vez de que un valor fuera de rango de la columna `DECIMAL` termine en 500.
32. **País de la parada (`newConsignee.country`) validado como ISO 3166-1 alfa-2** (`CountryCode.IsValid`, archivo nuevo
    `src/Teikem.Domain/Common/CountryCode.cs`): sin esto, un código largo desbordaba la columna `CHAR(2)` del snapshot y
    terminaba en 500. La misma guarda se agregó, de forma transversal, a `LocationService` (alta/edición de
    localizaciones) y a `LookupService` (alta de un valor del catálogo global `Country`): afecta también al Lote 2.
33. **`StatusService.TransitionAsync` valida el largo del comentario** (`EntityStatusHistory.Comment NVARCHAR(500)`, 400
    `"El comentario admite como máximo 500 caracteres."`) antes de guardar: es transversal (aplica a todo cambio de
    estatus de cualquier lote, no solo a `POST /orders/{id}/cancel`/`.../status`), corrigiendo un truncado silencioso en
    SQL que existía desde el Lote 1.
34. **Contactos de un dueño polimórfico exigían solo `contacts.manage`**, sin el permiso de escritura del módulo dueño
    (`orders.edit`, `clients.update`, etc.): un usuario con `contacts.manage` a secas podía escribir contactos de
    cualquier entidad. `ContactPointService.AddAsync/UpdateAsync/DeactivateAsync` ahora exige `EnsureOwnerWriteAsync`
    antes (con `enforceOwnerWrite=false` para el importador, que ya exigió `orders.create` para crear la orden dueña).
    Hallazgo transversal (afecta también a Clientes/Contratos del Lote 2), corregido en este lote porque se descubrió al
    construir los contactos de la orden.
35. **Resolvers de pertenencia y `OwnerRead/WritePermission` para las cuatro entidades nuevas** (`ORDER_STOP`,
    `ORDER_COD`, `IMPORT_BATCH`, `IMPORT_TEMPLATE`), más allá de lo que pedía el plan (solo `TRANSPORT_ORDER`): cierra
    contactos/campos personalizados/historial de esas entidades a `orders.view`/`orders.edit` en vez de dejarlas sin
    resolver (lo que las habría hecho inalcanzables por esas rutas, no inseguras, pero verificado explícitamente en el
    smoke con 404).
36. **`ImportResultDto` agrega `Skipped`** (filas del lote no elegidas al confirmar un subconjunto), campo que el plan no
    especificaba en el contrato pero que el smoke y la UI necesitan para distinguir "no se intentó" de "falló".
37. **Savepoints dentro de la importación por fila**: cuando `OrderService.CreateAsync` corre dentro de la transacción
    ambiente de `OrderImportService` (una transacción por fila del lote), cada intento de numeración se envuelve en un
    savepoint propio para que un choque de número automático/tecleado se pueda reintentar sin perder el resto de la fila
    ni contaminar filas ya confirmadas antes en el mismo lote.
38. **R36 al cambiar de consignatario por PATCH** usa el `ClientInvoiceNumberTyped` de la orden (decisión 29), no el
    ajuste actual del cliente, y una orden no cuenta como duplicado de sí misma.
39. **Indicador "COD por cobrar" excluía mal las órdenes canceladas** en una versión anterior del filtro (sumaba el COD
    PENDING de una orden `CANCELLED`); corregido y cubierto por `Lote3HardeningTests.Cod_pending_indicator_excludes_
    cancelled_orders` (con el filtro viejo conservado como `CodPendingFilterV1` solo para la prueba de regresión).
40. **Campo `PackageType`/`PackageTypeCode` en la fuente de datos `TRANSPORT_ORDER`** ("tipo de paquete principal": el de
    más piezas entre las líneas de la orden, desempate por la primera línea) no estaba en el plan original de P5;
    `TransportOrderDataSource.MainPackageTypeId` cubierto por prueba unitaria.

## Lo que queda fuera de este lote (a propósito)

- Recolección y empaque (`Product`/`StockBalance`/`InventoryTransaction`), asignación de chofer y `DriverTrip` (módulo 11A).
- Adjuntos de la orden (`OrderDocument`), multi-parada real y geocodificación/`GeoPoint`.
- Login del portal y filtro por `ClientId` del principal (`OrderScope` ya queda preparado; lo activa el Lote 8). El
  bloqueo de acceso de un usuario de portal a un cliente dado de baja también se aplica ahí.
- `StatusService` resolviendo el regreso lateral por dominio en vez de solo por `(EntityType, EntityId)` (riesgo
  documentado en la decisión 7, no corregido en este lote).
- Zonas/recargos/`MinCharge` de tarifa y evaluación con el DSL en la cotización (heredado del Lote 2).
- Prueba de integración end-to-end de la propagación de permisos a roles clonados con el permiso nuevo
  `orders.credit_override` (heredado del pendiente del Lote 2, decisión 35 de ese lote).
- Enumeración exhaustiva de los 54 hallazgos de la ronda de verificación: este documento detalla los que quedaron con
  evidencia verificable en el diff (decisiones 29-40); el resto no se pudo re-derivar de forma confiable en este cierre
  sin inventar detalle no verificado en el código.

---

**Verificación del cierre (2026-09-26, sobre BD recreada desde cero):** `dotnet build` sin errores, `dotnet test` 261/261,
`db-init` dos veces (49 permisos, idempotente) y `scripts/smoke.sh` completo de los Lotes 1, 2 y 3 en `SMOKE OK` (50 pasos).
Corrida de CI: ver enlace al final.

**Corrida de CI del cierre del Lote 3:** https://github.com/lcasado-cerevelo/teikem/actions/runs/36210048909 (run 18, verde: build, 261 pruebas, db-init ×2 sobre BD limpia y smoke de los Lotes 1, 2 y 3).
