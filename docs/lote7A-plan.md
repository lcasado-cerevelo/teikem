# Lote 7A — Pulso de almacén y Actividad reciente (solo Almacén): plan (backend + frontend)

> El Lote 7 completo es el dashboard de todos los módulos. Este 7A construye únicamente la parte de Almacén (panel con filtro,
> eventos e indicadores de almacén). Quedan en el plan, para después: **7B Operación** (eventos e indicadores de órdenes,
> viajes, flota, alertas de documentos y paradas) y **7C Contabilidad** (facturas, COD, corridas de facturación, crédito).
> Lo que 7A deja de infraestructura (catálogo, servicio, endpoint, panel) lo reutilizan 7B y 7C sin cambios: solo agregan
> un proveedor de eventos, su seed y su permiso de vista.

**Estado: plan armado, pendiente de la orden de Luis para ejecutar.** Diseño aprobado el 2026-09-27 y escrito en
`Diseño/logistica-funcionalidades-maestro.md` (módulo 12, "Pulso del día — diseño consolidado"). Mock aprobado:
`docs/frontend/mock-pulso-almacen.html`.

Archivos que consumen los workflows: `docs/lote7A-plan.json` (backend, `lote-implementar`) y `docs/frontend/loteF7A-plan.json`
(frontend, `fe-implementar`). Orden: primero backend, luego regenerar `web-app/openapi.json`, luego frontend.

## Parte A — Backend (`lote-implementar`, 3 piezas)

| Pieza | Qué deja | Archivos principales |
|---|---|---|
| P0 base compartida | Índice `IX_EntityStatusHistory_TenantDate (TenantId, ChangedAtUtc)`; seed de `LookupCode` `ActivityEventType` con los 23 eventos de Almacén (etiquetas es/en, `ExtraJson` con módulo, obligatorio y encendido por defecto); constantes `ActivityEvents`; contratos `ActivityQuery`, `ActivityEventDto`, `ActivityPageDto`; abstracción `IActivityEventProvider` registrada como colección en DI | SQL estructura y seed, `Domain/Constants`, `Contracts/ActivityContracts.cs`, `DependencyInjection.cs` |
| P1 servicio y endpoint | `WarehouseActivityProvider` (historial de estatus de recibos, ASN, conteos, recolecciones, órdenes de compra, cruce de muelle, almacenes; tareas; ledger de ajustes y transferencias; auditoría de baja de producto y faltantes), `ActivityRules` (ventana, tope 50, mapeos, puro y probado), `ActivityFeedService` (módulos visibles por permiso, 403 si se pide uno no visible), `GET /api/v1/analytics/activity` con `analytics.view`; filtro `belowMin=true` en `GET /api/v1/products`; paso `activity` en `scripts/smoke.sh` | `Services/ActivityFeedService.cs`, `Services/Activity/*`, `Controllers/ActivityController.cs`, `ProductService.cs`, `smoke.sh`, `tests/ActivityRulesTests.cs` |
| P2 seed analítico | Indicadores de sistema *Productos bajo mínimo*, *Unidades recibidas* (7 días), *Conteos con diferencia* (30 días) y gráfico *Movimientos de inventario por tipo* (7 días), módulo Almacén, en Pulso, visibles a todos | `Seeding/SystemAnalyticsSeeder.cs`, `tests/AnalyticsSeedFieldsTests.cs` |

Contrato del endpoint: `GET /api/v1/analytics/activity?module=WAREHOUSE&window=24h|48h|today&onlyMandatory=false&skip=0&take=50`
→ `{ total, visibleModules: ["WAREHOUSE"], items: [{ occurredAtUtc, code, module, mandatory, label, entityType, entityId,
publicId, reference, detail, userId, userName }] }`. Sin `module`, devuelve el primer módulo visible. `take` mayor que 50 →
400 "El máximo por página es 50."; módulo no visible → 403 "No tiene permiso para ver la actividad del módulo {module}.".

Sin tablas nuevas. La base local se recrea (cambia el hash de la estructura por el índice).

## Parte B — Frontend (`fe-implementar`, 2 piezas núcleo en paralelo)

| Pieza | Qué deja |
|---|---|
| P0 panel Almacén con filtro | Selector de almacén (todos o uno) para las cinco tarjetas; `CategoryProductPicker` (un control, dos secciones: categorías en árbol completo y productos por búsqueda) que gobierna solo las tarjetas de saldo: en mano, disponible con reservado, bajo mínimo (con producto: sí/no y enlace al Kárdex); última selección recordada por usuario en el navegador |
| P1 panel Actividad reciente | Pestañas por módulo visible, ventana, interruptor "Solo obligatorios", tabla con hora, evento (chip y marca de obligatorio), referencia enlazada a la ficha, detalle y quién; "Ver más"; tarjetas bajo 720 px; estados vacío y error |

Recorrido Playwright de 7 pasos (en el JSON), incluidos el usuario sin `inventory.view` y el proyecto móvil.

## Cómo se prueba

1. Backend: `dotnet build && dotnet test`; base limpia + `db-init` dos veces; `scripts/smoke.sh` con el paso `activity`.
2. `cd web-app && npm run api:types` (nuevo `openapi.json` desde el API) y `npm run check`; `npx playwright test`.
3. CI verde; `docs/lote7A-decisiones.md` y `docs/frontend/loteF7A-decisiones.md`; manual: `docs/manual/07-pulso-y-actividad.md (sección Almacén; 7B y 7C agregan las suyas)`
   (backend: eventos, mensajes de error) y `docs/manual/frontend/f7a-pulso-almacen.md` (pantalla) + FAQ.

## Estimación de consumo

| Parte | Agentes | Tokens estimados |
|---|---|---|
| Backend (3 piezas, 4 lentes, correcciones, docs) | lote-implementar | 2.5 a 3.5 M |
| Frontend (2 piezas, 2 lentes, Playwright, docs) | fe-implementar | 1.5 a 2.5 M |
| **Total** | | **4 a 6 M** |

## Decisiones tomadas al armar el plan (revisar si no gustan)

1. Sin tabla de eventos: se calcula al leer con ventana corta e índice por fecha. Si el volumen lo pide, después se
   materializa con `IStatusTransitionEffect`, sin cambiar el contrato del endpoint.
2. La bandera obligatorio/opcional y el módulo van en `ExtraJson` del `LookupCode`; no se agrega columna.
3. El permiso `billing.view` no existe todavía; la pestaña Contabilidad llega con el módulo de contabilidad del frontend. En
   este lote solo se construye el proveedor de Almacén; Operación y Contabilidad solo agregan un proveedor y su seed.
4. El endpoint vive bajo `/api/v1/analytics` con `analytics.view` más el permiso del módulo, sin permiso nuevo.
5. La preferencia de apagar eventos opcionales queda como interruptor de pantalla; la preferencia guardada por usuario se
   construye junto con las notificaciones, que reutilizan este catálogo.
6. El contador "Bajo mínimo" por categoría necesita el filtro nuevo `belowMin` en el API de productos (pieza P1 del backend);
   sin él habría que traer la lista completa al navegador.
