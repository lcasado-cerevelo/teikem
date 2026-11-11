# Lote 238 — Modo de captura por persona, preferencias en el servidor, última compañía y detalle de lo que hay que recolectar (2026-11-11)

Pedidos de Luis en la sesión del 2026-11-11. Web, API, app de almacén, manual y FAQ. Commit `9e5130f6` en `master`.

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Orden de venta: dos modos de una captura | Un botón «Nueva orden» (abre el último modo de la persona) y un botón pequeño con el otro modo. Un borrador se abre en el modo de la persona; «Cambiar al asistente» (ventana, arriba) y «Cambiar al formulario completo» (asistente, arriba) guardan si está completo y, si no, cambian igual. Archivos: `features/sales/entryMode.ts`, `SalesOrdersScreen.tsx`, `OrderModal.tsx`, `wizard/NewOrderScreen.tsx`. |
| Preferencias por persona | Tabla `dbo.UserPreference` (sección «2026-11-11» de `logistica-db-update.sql`, idempotente), `UserPreferenceService`, `GET/PUT /api/v1/me/preferences/{key}`. Claves permitidas en código: `sales.entryMode`, `warehouse.last`. Web: `kernel/prefs/useUserPreference.ts`. |
| Último almacén (app de almacén) | `rememberLastWarehouse` al elegir y `restoreLastWarehouse` al abrir Inicio, una vez por persona. |
| Última compañía | `SwitchTenantAsync` guarda la compañía escogida en `DefaultTenantId` (UPDATE dirigido); el login ya la usaba (`LoginTenantRules.Pick`). Sin cambios en la web ni en la base. |
| Recolección y empaque | `PickDocumentDto` trae `warehousePublicId` y `lines` (renglones con ubicaciones reservadas, tipo de seguimiento y dueño). La ficha muestra «Lo que hay que recolectar» y el botón «Recolectar» llena el formulario (`pickDocumentView.preloadFromDocument`, `CollectPanel` prop `preload`). |
| Scripts de Advance Solutions | `12-lista-precios-quickbooks.sql` (lista general con los precios de QuickBooks, simula por omisión), `11-impuesto-productos.sql` (Exento a los once «Non»), `docs/migracion/import.solutions-clientes.json` (importar solo clientes). |
| Contrato | `web-app/openapi.json` (+1 ruta, +2 esquemas, `PickDocumentDto` ampliado) y los tipos de web y app regenerados. |

## Cómo se probó
- `tsc` y `oxlint` limpios en web y app; Vitest de almacén y ventas 969/969 (107 archivos, con `--maxWorkers=2`; incluye `entryMode.test.ts` y `pickDocumentPreload.test.ts`); Jest de la app de almacén 825/825.
- Servidor (Release): `UserPreferenceKeysTests`, `FrozenSqlTests`, contratos y OpenAPI 394/394. `FrozenSqlTests` encontró que el `CREATE TABLE dbo.UserPreference` debía ir en la misma línea del `IF OBJECT_ID(...) IS NULL`: corregido en `logistica-db-update.sql` (db-update vuelve a pasar en la base local).
- Recorrido de Playwright `web-app/e2e/lote238-modo-captura-y-recoleccion.spec.ts` contra un API **sin correo** (Brevo vacío) y la base `Teikem_e2e`: 3/3. Cubre un solo botón «Nueva orden» y el modo guardado en el servidor, el cambio asistente ⇄ formulario (también con la orden vacía), la ficha «Lo que hay que recolectar» con la posición reservada y «Recolectar» llenando el formulario sin recolectar, y la última compañía al volver a entrar. Capturas `l238-*` en `docs/manual/frontend/img/`.
- **Error encontrado por el recorrido y corregido:** al pulsar «Cambiar al formulario completo» el asistente volvía a poner su modo como «asistente» (el efecto de entrada dependía de `setEntryMode`); ahora solo corre al montar.

## Decisiones a revisar
1. **El modo no se guarda en la orden.** Un borrador se abre en el último modo de la persona, no en el que se usó al empezar. Guardarlo en la orden exige una columna nueva.
2. **Preferencias por persona y compañía** (`UserPreference` lleva `TenantId`), no globales por persona.
3. **«Última compañía» reutiliza `DefaultTenantId`**: se sobrescribe cualquier predeterminada fijada por un administrador.
4. **El último almacén no se aplica en la web** (no tiene «almacén activo»); solo en la app de almacén. El hook `useUserPreference('warehouse.last')` queda listo.
5. **Cambiar de modo con la orden a medias** no bloquea: retoma lo último guardado (se pierde lo no guardado).
6. **La sugerencia de recolección es la reserva** (`SalesOrderReservation` / `ExitNoticeReservation`): posición y lote exactos; las series no se precargan.
7. En un envío empacado (`ORDER`) la ficha no trae renglones.

## Qué quedó fuera
Aplicar los scripts 11 y 12 en TEST o producción (los corre Luis).
