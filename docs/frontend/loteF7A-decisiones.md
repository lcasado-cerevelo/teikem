# Lote F7A — Pulso de almacén con filtro y Actividad reciente (frontend): qué se construyó y decisiones a revisar

Plan aprobado: `docs/frontend/loteF7A-plan.json` (no hay `loteF7A-plan.md`; el plan de este lote quedó solo en JSON).
Contrato del kit: `web-app/KIT.md` (sección `CategoryProductPicker` ampliada y nota de Inventario agregada en este
lote). Fecha de cierre: 2026-09-27.

## Mapa de lo construido

### Pantallas (sin ruta propia: viven dentro de Pulso, `/`)

| Pieza | Ruta | Permiso · módulo | Código principal |
|---|---|---|---|
| Panel "Almacén" con filtro | `/` (debajo de los indicadores del API) | `inventory.view` · `WMS_LOTSERIAL` | `features/analytics/Pulse.tsx` (`WarehousePulsePanel`), `features/analytics/WarehouseFilter.tsx`, `kernel/ui/CategoryProductPicker.tsx`, `kernel/ui/categoryTree.ts` |
| Panel "Actividad reciente" | `/` (debajo del panel Almacén) | `analytics.view` · `ANALYTICS` (además, cada pestaña de módulo exige que el servidor la traiga en `visibleModules`) | `features/analytics/ActivityPanel.tsx`, `features/analytics/activity.ts` |

### Extensión de una pantalla ya existente (F6)

| Pieza | Ruta | Qué agrega |
|---|---|---|
| Inventario: filtros por URL | `/warehouse/inventory` | Lee `tab`, `warehousePublicIds`, `product`, `categoryIds` de la URL al montar (`InventoryScreen.tsx`, nuevo `useResolvedProducts`); es lo que usan los enlaces "Ver en Inventario ›" / "Ver Kárdex de… ›" del panel Almacén. Nuevo hook `useProductsByPublicId` en `features/warehouse/api.ts` para resolver el SKU de un producto que llega solo con su `publicId`. |

### Núcleo compartido (sin pantalla propia)

| Pieza | Código | Qué deja listo |
|---|---|---|
| Filtro y persistencia del panel Almacén | `features/analytics/api.ts` (`WarehousePulseFilter`, `useWarehouseFilter`, `readWarehouseFilter`/`writeWarehouseFilter`/`sanitizeWarehouseFilter`) | Selección (almacén + categoría/producto) guardada en `localStorage` por compañía y usuario, restaurada al abrir y limpiada sola si el almacén, la categoría o el producto guardados ya no existen |
| `useWarehousePulse` extendido | `features/analytics/api.ts` | Acepta el filtro: el almacén llega a las seis tarjetas; categoría/producto solo a saldo y bajo mínimo (bajo mínimo de un producto puntual se resuelve buscando su SKU con `belowMin=true`, porque el endpoint no filtra por `publicId`) |
| `CategoryProductPicker` | `kernel/ui/CategoryProductPicker.tsx` (+ `categoryTree.ts`) | Combobox con buscador y dos secciones (Categorías/Productos); nueva `categoryProductTotals(cats)` (productos de una categoría contando sus subcategorías, igual que filtra el API) |
| `activity.ts` | `features/analytics/activity.ts` | `useActivity` (`useInfiniteQuery` con `skip`/`take`), `activityTabs`, `activityLink` (ficha por `entityType`), `activityTone` (tono del chip por familia de evento), `formatEventTime` |
| Textos | `kernel/i18n/es.json` / `en.json` | Secciones `analytics.pulse.warehouse.*` y `analytics.activity.*` (nuevas), `warehouse.inventory.productUnavailable` (nueva) |

## Cómo se prueba

1. `cd web-app && npm ci` (ya instalado en este entorno).
2. `npm run check` (`api:types` + `tsc -b` + `oxlint src e2e` + `vitest run` + `vite build`). **Ejecutado en este
   entorno ahora mismo, verde**: 28 archivos de prueba, 222 pruebas, sin errores de tipos ni de lint (mismos 5 avisos
   `react(incompatible-library)` de oxlint que en F6, no bloqueantes, sobre archivos que este lote no tocó), build de
   producción generado en `web-app/dist`.
3. Recorrido Playwright (`e2e/f7a.spec.ts`, proyectos `escritorio` y `movil` = Pixel 7 a 360 px) contra el API real
   (SQL Server 2022 + API en `:5000`, ya arriba en este entorno) y Vite en `:5173`: `npx playwright test
   e2e/f7a.spec.ts`. Cubre los 7 pasos del "recorrido" del plan (panel Almacén con sus controles → categoría con En
   mano/Bajo mínimo y su enlace a Saldos → producto con Bajo mínimo Sí/No y su enlace al Kárdex → recargar conserva
   la selección y ✕ la limpia → Actividad reciente con el ajuste obligatorio y "Solo obligatorios" ocultando la
   recolección → ventana "Hoy" e idioma inglés sin recargar → despacho@ sin el panel Almacén ni la pestaña Almacén →
   móvil sin scroll horizontal). **Verificado en este entorno**: 7 pruebas pasadas (6 de escritorio + 1 de móvil), 7
   saltadas (cada proyecto se salta las del otro por `isMobile`), 0 rojas. Se corrió también la suite completa
   (`npx playwright test`, los tres specs `f7a`/`f6`/`loteF1` juntos) para confirmar que este lote no rompió nada de
   los anteriores: **26 pasadas, 26 saltadas, 0 rojas**. Cada paso deja una captura en
   `docs/manual/frontend/img/f7a-*.png` (11 archivos, generados en esta misma corrida); correr la suite completa
   también volvió a tomar todas las capturas de `f1-*.png` y `f6-*.png` (los specs de esos lotes retoman su propia
   captura en cada paso), así que quedaron con la fecha y los datos de esta corrida en vez de la original; la única
   con un cambio de contenido real (no solo de datos) es `f6-pulso-almacen.png`, por el rediseño del panel Almacén
   de este mismo lote (ver decisión 10).
4. `.github/workflows/ci.yml` corre `npm run check` en el job `frontend`; el recorrido Playwright se ejecuta
   localmente con el API arriba (no está en CI, igual que en F1 y F6). No se disparó una corrida nueva de CI para
   este cierre (no hay push a este repositorio remoto desde este entorno); lo verificado arriba es la corrida local
   completa.

## Decisiones tomadas (revisar)

Este lote llegó a un punto de control (commit `b336770`, "en progreso (2)") con la primera versión de las dos
piezas, seguido de una ronda de revisión con las siguientes correcciones (verificadas contra el código, no una lista
recibida de fuera):

1. **Los enlaces "Ver en Inventario ›" / "Ver Kárdex de… ›" del panel Almacén no llevaban a nada filtrado**: en el
   punto de control, `InventoryScreen` no leía ningún parámetro de la URL, así que el destino siempre abría Saldos
   sin filtros. Corregido enseñando a `InventoryScreen` a leer `tab`/`warehousePublicIds`/`product`/`categoryIds` al
   montar (solo la pestaña que se abre; al cambiar de pestaña se descartan). Cubierto por 7 pruebas nuevas en
   `InventoryScreen.test.tsx` y por el paso 2 del recorrido Playwright (capturas
   `f7a-inventario-saldos-desde-pulso.png`, `f7a-inventario-kardex-desde-pulso.png`).
2. **El enlace no llevaba el almacén elegido en el filtro de Pulso**: si había un almacén elegido, el destino en
   Inventario no cuadraba con la cifra de la tarjeta (mostraba el total de todos los almacenes). Corregido con
   `inventoryLink()` en `Pulse.tsx`, que agrega `warehousePublicIds` a la URL cuando hay un almacén elegido. Probado
   en `WarehouseFilter.test.tsx` y en el paso 2 del recorrido (con almacén elegido, la fila de Saldos muestra el
   mismo saldo que la tarjeta).
3. **`INVENTORY_TRANSACTION` en `activityLink` apuntaba a un enlace que nunca hubiera funcionado**: el API reporta
   los ajustes y transferencias de inventario con `entityType=PRODUCT`, no `INVENTORY_TRANSACTION`, y aunque lo
   reportara así, la búsqueda del Kárdex no compara el documento de origen (`ref`), así que ese parámetro no hubiera
   localizado nada. Se quitó ese caso de `activityLink` (ahora esos eventos no llevan enlace en absoluto, ya que van
   como PRODUCT y ese sí tiene enlace a la ficha del producto) y se descontinuó el parámetro `?ref=` del Kárdex
   (prueba explícita en `InventoryScreen.test.tsx`: "`?ref=` ya no se usa").
4. **El conteo de "N productos" no coincidía con lo que de verdad filtra el API**: tanto el subtítulo de "En mano" en
   Pulso como la cifra junto a cada categoría en `CategoryProductPicker` usaban `productCount` del DTO (productos
   directos de esa categoría); pero el API expande a las subcategorías al filtrar con `categoryIds`, así que una
   categoría padre sin productos propios (solo en sus hijas) se veía con 0 en vez de la suma real. Corregido con
   `categoryProductTotals(cats)` (nueva función pura en `categoryTree.ts`, con prueba de un ciclo de categorías para
   que no se cuelgue).
5. **El desplegable de "Categoría o producto" se recortaba en pantallas angostas**: `ITEM_BOX` (el contenedor del
   picker en `WarehouseFilter.tsx`) tenía una base flexible de 280 px, menor que el propio desplegable del picker
   (340 px, anclado a la izquierda); cuando compartía renglón con el selector de almacén, el panel (con
   `overflow: hidden`) recortaba parte de la lista. Corregido subiendo la base a 340 px (documentado también en
   `KIT.md`, tabla de `CategoryProductPicker`).
6. **Advertencia de React por mezclar el atajo `border` con una variante que solo cambia el color**: `TILE` (estilo
   base de las tarjetas de almacén) usaba el atajo `border: '1px solid var(--line)'`, y `TILE_WARN` lo extendía
   cambiando solo `borderColor`; React avisa de ese patrón porque puede pisar las otras subpropiedades. Se separó
   `TILE` en `borderWidth`/`borderStyle`/`borderColor`.
7. **Al buscador libre de Actividad reciente le faltaba una prueba de que de verdad filtra sin ir al servidor, y a la
   columna Referencia le faltaba `sortValue`** (no era ordenable pese a que las demás columnas de texto sí lo son).
   Se agregó `sortValue` a la columna y una prueba (`ActivityPanel.test.tsx`) que ordena por Referencia y confirma
   que el buscador no dispara peticiones nuevas al API.
8. **El estado vacío de la tabla ("Sin actividad en esta ventana") se mostraba también cuando el buscador filtraba a
   cero coincidencias**, lo cual es engañoso (si hay actividad, pero ninguna coincide con lo buscado). Corregido:
   ese texto ahora solo aparece sin ninguna fila cargada; el buscador sin coincidencias usa el vacío genérico de
   `DataTable` ("Sin resultados").
9. **La prueba de F6 (`f6.spec.ts`, paso 1) quedó desactualizada por el rediseño de las tarjetas** del panel Almacén
   en este mismo lote (las etiquetas pasaron de "En mano total"/"Disponible total" a "En mano"/"Disponible", se
   agregó "Bajo mínimo", y las tarjetas ahora son localizables por `role=group`). Se actualizó el paso 1 para no dar
   un falso verde comparando contra textos que ya no existen.
10. **El capítulo del manual de F6 quedó con una captura desactualizada** (`f6-pulso-almacen.png`, regenerada al
    correr la suite completa de Playwright de este cierre: ahora muestra el filtro y la tarjeta "Bajo mínimo" de este
    lote, aunque el texto del capítulo seguía describiendo las cinco tarjetas originales). Se acortó esa sección del
    capítulo F6 con una nota que remite al capítulo F7A para la descripción y las capturas actuales, dejando en F6
    solo el dato de permiso (que no cambió). Es un ajuste de documentación, no de código.

## Lo que queda fuera de este lote (a propósito)

Copiado de `fueraDeAlcance` de `docs/frontend/loteF7A-plan.json`, con lo verificado en el código:

- Preferencia de eventos opcionales guardada por usuario en Actividad reciente (el interruptor "Solo obligatorios" es
  de sesión, no se recuerda entre visitas; confirmado: `ActivityPanel.tsx` inicializa `onlyMandatory` en `false` con
  `useState`, sin leer ni escribir `localStorage`).
- Pestañas "Operación" y "Contabilidad" en Actividad reciente: `activityTabs` ya las conoce (`ACTIVITY_MODULES`), pero
  solo se pintan cuando el servidor las traiga en `visibleModules` (no hay backend de esos módulos todavía).
- Administración del catálogo de eventos (`ActivityEventType`): esta pantalla es de solo lectura sobre lo que el API
  ya calcula; no hay pantalla para editar qué eventos son obligatorios ni sus tonos.
- No se probó en Playwright el caso de una compañía con más de un módulo visible en Actividad reciente (solo hay
  Almacén en el tenant demo); `activityTabs` (orden fijo Almacén → Operación → Contabilidad) y el cambio de pestaña
  se verificaron solo con las pruebas unitarias de lógica pura en `ActivityPanel.test.tsx` (bloque `describe('activity (lógica pura)')`) y por lectura de código, no con un
  recorrido end-to-end real con varias pestañas.
- No se modificó ningún archivo de backend (`src/`, `Diseño/`, `tests/`) en este cierre: los cambios de este lote son
  100% frontend sobre el API del Lote 7A de backend, ya desplegado (confirmado con `git status`/`git diff --stat`
  sobre esas carpetas: sin diferencias).

## Verificación en CI

No se hizo push a un repositorio remoto desde este entorno en este cierre, así que no hay una corrida de GitHub
Actions que enlazar todavía (a diferencia de F1 y F6, donde sí se referenció una corrida verde). Lo verificado es la
ejecución local descrita en "Cómo se prueba": `npm run check` verde y la suite completa de Playwright verde.

## Verificación en CI

- Corrida verde de GitHub Actions (jobs `build-test` y `frontend`): https://github.com/lcasado-cerevelo/teikem/actions/runs/36321836638 (commit `1b09ece`).
