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
- `tsc` limpio en web y app; Vitest de ventas (168 + 4 de `entryMode`) y de almacén (791 de 797; el resto sin correr por un cierre inesperado de un worker de Vitest en Windows, no por una prueba); Jest de la app de almacén 825/825; prueba nueva de preload (`pickDocumentPreload.test.ts`).
- Servidor: compila en Release; `UserPreferenceKeysTests` escrita y **sin correr** (Visual Studio tenía bloqueados los binarios de Debug).
- Sin recorrido en pantalla ni capturas de este lote.

## Decisiones a revisar
1. **El modo no se guarda en la orden.** Un borrador se abre en el último modo de la persona, no en el que se usó al empezar. Guardarlo en la orden exige una columna nueva.
2. **Preferencias por persona y compañía** (`UserPreference` lleva `TenantId`), no globales por persona.
3. **«Última compañía» reutiliza `DefaultTenantId`**: se sobrescribe cualquier predeterminada fijada por un administrador.
4. **El último almacén no se aplica en la web** (no tiene «almacén activo»); solo en la app de almacén. El hook `useUserPreference('warehouse.last')` queda listo.
5. **Cambiar de modo con la orden a medias** no bloquea: retoma lo último guardado (se pierde lo no guardado).
6. **La sugerencia de recolección es la reserva** (`SalesOrderReservation` / `ExitNoticeReservation`): posición y lote exactos; las series no se precargan.
7. En un envío empacado (`ORDER`) la ficha no trae renglones.

## Qué quedó fuera
Capturas del recorrido de Playwright; manual en inglés (`docs/manual/en`, aún no conectado); aplicar los scripts 11 y 12 en TEST o producción (los corre Luis).
