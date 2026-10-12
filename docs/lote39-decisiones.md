# Lote 39 — Precios de las órdenes (`orders.view_price`) y filtros de la lista de órdenes (servidor de F-C1) (2026-10-12)

Servidor de la pantalla web F-C1 (lista de Órdenes): un permiso nuevo para ver los precios de las órdenes y seis filtros
nuevos en `GET /api/v1/orders`. La pantalla es otro trabajo; aquí se regeneró `web-app/openapi.json` y los tipos de
`web-app` y `app-almacen` para que la consuman. Decisiones del dueño del 2026-10-12.

## Qué se construyó

| Pieza | Resultado | Dónde |
|---|---|---|
| Permiso nuevo | `orders.view_price` «Ver precios de las órdenes» / «View order prices», categoría `ORDERS`. Catálogo: 73 → **74** códigos. Plantillas **TenantAdmin** (todos) y **Billing** | `PermissionCatalog` |
| Base de datos | Sección **«2026-10-12 (a)»** al final de `Diseño/logistica-db-update.sql`: el permiso (`IF NOT EXISTS`) y su asignación a las plantillas **y** a los roles ya clonados de cada compañía llamados `TenantAdmin` o `Billing` (`INSERT … WHERE NOT EXISTS`, sin filtrar por `TenantId`). Solo agrega | `logistica-db-update.sql` |
| Una sola decisión | `OrderPriceVisibility` (scoped, recibe el principal vía `PermissionService`): `CanViewAsync` / `EnsureAsync`; sin el servicio (null) se oculta (falla cerrado). Las reglas puras de qué se oculta, en `OrderPriceRules` | `Infrastructure/Orders/OrderPriceVisibility.cs` |
| Ficha, lista y lookup | `quotedAmount: null` sin el permiso (lista, `lookup`, ficha y, por salir de la ficha, las respuestas de crear, editar, confirmar, reprecio, cancelar, estatus y entrega especial). El COD (`codAmount`) sigue visible | `OrderReadService` |
| Cotización completa | `GET /orders/{id}/quote`: además de `orders.view`, `orders.view_price` en código → **403** `Falta el permiso 'orders.view_price'.` + `PERMISSION_DENIED` | `OrderStatusController.PreviewQuote` |
| 422 `credit_exceeded` | Sin el permiso: `El cliente excede su límite de crédito.` y `errors` vacío; mismo código y estatus. Aplica en confirmar, `confirmNow`, entrega especial con chofer e importación (todo pasa por el efecto) | `OrderStatusEffect.CreditExceededAsync`, `CreditExceededException(check, available, showAmounts)`, `CreditRules.RedactedMessage` |
| Historial de la orden | El comentario del override (`Crédito excedido autorizado por X (límite …, en curso …, esta orden …)`) pierde el paréntesis de cifras al leerlo sin el permiso (se guarda completo) | `StatusService.GetHistoryAsync` |
| Importación de órdenes | El error de crédito de una fila se guarda completo en el lote y al leerlo (vista previa, consulta, resultado de confirmar) pierde las cifras sin el permiso | `OrderImportService` |
| Análisis | Campo `QuotedAmount` de la fuente `TRANSPORT_ORDER` en `null` sin el permiso (vistas, exportaciones, indicadores, gráficos y Pulso). El campo sigue en el contrato | `TransportOrderDataSource` |
| Filtros de lista | `serviceTypes`, `packageTypes` (alguna línea **activa** del tipo), `cities` (igualdad sin mayúsculas ni tildes, espacios colapsados), `postalCodePrefix`, `clientIds` (∪ `clientId`) y `statuses` (∪ `status`). Repetibles en la query; cada lista = «cualquiera de», AND entre filtros; total y paginación sobre todo lo filtrado; filtro de tenant y alcance de datos del usuario intactos | `OrdersController.List`, `OrderListQuery`, `OrderReadService.GetListAsync`, `OrderListFilterRules` (Domain) |
| Contrato | `web-app/openapi.json` regenerado (solo los 6 parámetros nuevos) y `schema.d.ts` de `web-app` y `app-almacen` | — |
| Smoke | Pasos «órdenes (Lote 39): ver precios…» y «órdenes (Lote 39): filtros de lista…»; ajustados dos pasos del Lote 3 que asumían ver precios con roles sin el permiso | `scripts/smoke.sh` |

Mensajes de 400 de los filtros (reutilizan los existentes): `Tipo de servicio desconocido: X.` (campo `serviceTypes`),
`Tipo de paquete desconocido: X.` (`packageTypes`), `Estatus desconocido: X.` (`statuses`; `status` conserva el suyo).
Un `clientIds` ajeno o fuera del alcance → 404 `Cliente no encontrado.` (como `clientId`).

`OrderListItemDto` no cambió: ya trae todo lo que pide la lista de la maqueta (empaque, número, factura, fecha, cliente,
consignatario, pueblo, ZIP, servicio, `packages[]` con tipo/descripción/piezas, piezas, COD y estatus).

## Superficies de precio de la orden encontradas

Ocultadas (con y sin el permiso, cada una con prueba unitaria):

1. `quotedAmount` de `OrderListItemDto` — `GET /orders` y `GET /orders/lookup` (la estación de escaneo usa el lookup pero no
   devuelve la fila).
2. `quotedAmount` de `OrderDetailDto` — ficha y todas las respuestas que la devuelven (crear, editar, confirmar, reprecio,
   cancelar, estatus, despacho de entrega especial).
3. `GET /orders/{id}/quote` (`OrderQuotePreviewDto`: total, líneas, `dispatchFee`, `codFee`, bloque de crédito) → 403.
4. 422 `credit_exceeded` (título y `errors`).
5. Comentario del override de crédito en `GET /status/history/TRANSPORT_ORDER/{id}`.
6. Error de crédito de una fila del lote de importación (`ImportPreviewDto` / `ImportResultDto`).
7. Campo `QuotedAmount` de la fuente de datos `TRANSPORT_ORDER` (Análisis).

No se tocaron (se reportan):

8. **Bitácora** (`AuditLog` de `TRANSPORT_ORDER`, cambios de `QuotedAmount`) y **eventos de seguridad** del override (`limit`,
   `exposure`, `amount`): exigen `admin.audit`.
9. **`POST /api/v1/billing/contract-rate-quote`** (`BillingQuoteController`): cotiza líneas sueltas contra el contrato de un
   cliente; exige `contracts.read`. Es precio del contrato, no de una orden.
10. Límite de crédito del cliente (`ClientDetailDto.CreditLimit`, `clients.read`) y tarifas del contrato (`contracts.read`).
11. `quotedAtUtc`, `currency` y `capabilities.canReprice` siguen visibles: dicen que la orden está cotizada, no cuánto.

## Cómo se probó

- `dotnet build Teikem.sln` sin errores y `dotnet test Teikem.sln`: **3555 pruebas verdes** (61 nuevas):
  `OrderViewPriceTests` (catálogo, plantillas, propagación del `PermissionSeeder` a roles clonados, sección SQL, reglas puras,
  ficha/lista/lookup/quote/422/historial/importación/Análisis con y sin el permiso, administrador de plataforma, falla cerrado),
  `OrderListFilterTests` (cada filtro, mayúsculas/tildes, uniones con `clientId`/`status`, combinaciones, total y paginación,
  400/404, alcance de datos por cliente, otro tenant, `OrderScope` del portal) y `OrderControllerSecurityTests` (permiso y
  módulo por acción de `OrdersController` y `OrderStatusController`, binding de los filtros desde la query). Conteo de
  permisos 73 → 74 en `OrderCatalogTests`, `TripCatalogTests`, `FleetCatalogTests`, `WmsCatalogTests` y `PulseLayoutTests`.
- `db-update` sobre `Teikem_verif` (local, autenticación Windows) dos veces: la primera aplicó la sección (permiso + 6
  asignaciones: plantillas y compañías 1 y 2, `TenantAdmin` y `Billing`; ningún otro rol), la segunda «base de datos al
  día». El archivo completo se volvió a correr a mano con `sqlcmd -I`: sin errores y sin duplicados (1 permiso, 6 filas).
- `web-app/openapi.json` bajado de `/swagger/v1/swagger.json` (API en Development contra `Teikem_verif`) con CRLF;
  `npm run api:types` en `web-app` y `app-almacen`.
- Smoke completo contra una base nueva y desechable (`Teikem_l39_smoke`, `db-init`, API en Development con
  `Auth__Onboarding__Enabled=false`, `SMOKE_SQL` y `SMOKE_MIGRATION_RUN` a esa base, `TMPDIR` en forma `C:/…`): **SMOKE OK,
  143 pasos `ok`, 951 s (unos 16 minutos)**; la base se borró al terminar. Los dos pasos nuevos cubren: ficha/lista/lookup
  sin precio para solo `orders.view` y para Despacho (COD visible), `/quote` 403 con el título exacto y `PERMISSION_DENIED`,
  Facturación y un rol propio con el permiso ven precio y cotización, 422 sin cifras para Despacho y con cifras para el
  administrador, historial del override sin cifras; y cada filtro (incluidos pueblo sin tilde/mayúsculas y codificado con
  `%C3%8D`), uniones, combinación con paginación, 400 por código desconocido, 404 por cliente ajeno, otro tenant y un usuario
  con alcance de datos por cliente (`PUT /users/{id}/data-scopes`).

## Desviaciones

1. El smoke venía roto por el commit del motivo «Retiro del dueño» (`OWNER_PICKUP`, d8f0111): esperaba 5 motivos de despacho
   manual. Se corrigieron las tres aserciones (6 motivos, 5 con `SALE` deshabilitado); no es de este lote.
2. Dos pasos del smoke del Lote 3 asumían ver precios con roles sin el permiso (solo `orders.view` leía `/quote`; solo
   `orders.create` veía `quotedAmount` al confirmar): ahora esperan 403 y `quotedAmount: null` (con `quotedAtUtc` lleno).
3. El historial de estatus y la importación también ocultan cifras (no estaban en el pedido explícito; caben en «otras
   superficies… si es sencillo y coherente»).

## Decisiones a revisar

1. **`errors` vacío** (`{}`) en el 422 sin cifras. Alternativa: una llave sin montos (p. ej. `credit: [mensaje]`).
2. **Falla cerrado**: un servicio construido sin `OrderPriceVisibility` (pruebas viejas, llamadores internos sin principal,
   migración) oculta los precios. En producción la DI siempre lo inyecta.
3. **Lote de importación**: el error se guarda con cifras y se oculta al leer. Quien confirma con el permiso y quien lo lee
   sin él ven cosas distintas del mismo lote (a propósito).
4. **Bitácora y eventos de seguridad conservan montos** (solo `admin.audit`). Si un auditor no debe ver precios, habría que
   filtrar `AuditLog` por campo.
5. **`cities` compara igualdad completa**, no «contiene»: se resuelven primero los pueblos guardados en las paradas de entrega
   de la compañía (una consulta de valores distintos) y luego se filtra por esa lista exacta. Con muchos pueblos distintos
   (miles) convendría una columna normalizada.
6. **`packageTypes` y `serviceTypes` aceptan códigos inactivos o deshabilitados por la compañía** (para filtrar órdenes viejas);
   solo un código inexistente es 400.
7. **Uniones** `clientId ∪ clientIds` y `status ∪ statuses` (no intersección), para que la pantalla vieja y la nueva convivan.

## Qué quedó fuera

- La pantalla web F-C1 (consume los filtros y debe mostrar «—» cuando `quotedAmount` es `null`).
- `docs/plan-de-desarrollo-pendiente.md` (no se tocó, por instrucción).

Manual: `docs/manual/03-ordenes-de-transporte.md` (§1.2, §1.3 filtros, §3.1, §3.3, §3.5 nueva, §7.3, §8, §9),
`docs/manual/01-plataforma-y-seguridad.md` (§3.2, permiso nuevo) y `docs/manual/faq.md` («Lote 39 — Precios de las órdenes
y filtros de la lista»).
