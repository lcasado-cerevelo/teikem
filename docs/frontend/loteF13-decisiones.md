# Lote F13 — Nuevo conteo "Por producto" desde la web (2026-10-03)

Cambio 3 de [decisiones-del-dueno-2026-10-03.md](../decisiones-del-dueno-2026-10-03.md). Número: en `docs/frontend/` el último era F12;
se tomó el siguiente libre, **F13**. Solo se tocó `web-app/` y `docs/`. Manual de pantallas:
[f13-nuevo-conteo-por-producto.md](../manual/frontend/f13-nuevo-conteo-por-producto.md); FAQ: preguntas del conteo por producto en
[faq.md](../manual/faq.md).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| Pestañas del modal | "Nuevo conteo" gana **Por posiciones** (lo de siempre) y **Por producto**; `mode` vive en el formulario (un solo esquema zod con `superRefine`) | `CreateCountModal.tsx` |
| Por producto | Almacén + `ProductPickerInput` (el selector con búsqueda del almacén, solo activos); manda `POST /cycle-counts { warehousePublicId, productPublicIds:[producto] }` sin posiciones, zonas ni `allowEmpty` | `CreateCountModal.tsx` |
| Error 400 | El servidor lo manda bajo `filters`; `remapProblemFields` lo pasa a `productPublicId` y `Form` lo pinta bajo el selector; el modal sigue abierto | `CreateCountModal.tsx` |
| Navegación | Al crear, el aviso *Conteo CC-… creado con N línea(s).* y la pantalla abre el conteo (`?count=<id>`), como el alta de siempre (`onCreated`) | `CycleCountListScreen.tsx` (sin cambios) |
| Textos | `warehouse.cycleCounts.{modes,createProductHelp,fields.product,errors.productRequired}` en es/en | `kernel/i18n/{es,en}.json` |
| Estilo | `.cc-create-tabs` (aire bajo las pestañas) | `warehouse.css` |
| Permisos | los mismos de "Nuevo conteo": `<Can perm="warehouse.count">` y módulo WMS_LOTSERIAL (sin cambios) | `CycleCountListScreen.tsx` |

El contrato del API **no cambió** (`openapi.json` ya traía `productPublicIds`): no hubo que regenerar `schema.d.ts` de la web ni de la app.

## Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `npm run check` en `web-app/` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: 113 archivos, 1134 pruebas; solo avisos de oxlint que ya existían (ninguno en archivos de este cambio) |
| `CycleCountScreen.test.tsx` (3 pruebas nuevas): el modal con las dos opciones y la de siempre intacta; por producto exige producto, manda `productPublicIds` sin `allowEmpty` y abre `?count=15`; el 400 del servidor sale junto al selector y el modal sigue abierto | **pasaron** |
| `npx playwright test e2e/loteF13-conteo-web.spec.ts --no-deps --workers=1` (API real, Vite con `VITE_API_URL=http://localhost:5000`) | **2 pasaron** (escritorio y móvil 360 px; los otros 2 se omiten por proyecto) |
| Suite completa `npx playwright test --workers=1` (porque cambió `playwright.config.ts`) | **78 pasaron, 71 omitidos por proyecto, 0 fallaron** (7.4 min) |

Recorrido Playwright: crea por API un producto con existencia y otro sin ella; en la web abre Nuevo conteo → Por producto; sin producto
no envía; el producto sin existencia da el 400 bajo el selector; el producto con existencia crea el conteo (origen `PRODUCT`, una línea),
queda abierto en el panel y se encuentra en la lista; en 360 px el modal no desplaza la página. En `playwright.config.ts` el recorrido
corre en los proyectos `escritorio-f13` y `movil-f13`, después de los demás y de F12 (como se encadenaron F9–F12); F9 ahora depende
también de F13. Las capturas de otros lotes que regeneró la suite se descartaron.

## Decisiones para el dueño

1. **El selector de producto no filtra por existencia**: ofrece todos los productos activos (con `onlyAvailable` no se podría elegir uno
   sin existencia y el 400 nunca se vería, que es lo pedido). El costo es que se puede elegir un producto que no tiene existencia y
   enterarse al crear. Alternativa: filtrar por almacén y existencia (`warehousePublicId` + `onlyAvailable`).
2. **Un solo producto por conteo desde la web**, aunque el servidor admite varios (`productPublicIds`). Es lo pedido; varios productos
   serían un selector múltiple.
3. **La web no usa `allowEmpty`** (como se pidió): un producto sin existencia no se puede contar desde la web; sí desde la app.
4. **Productos con serie**: el servidor los admite y se crean igual; la captura por serie sigue en la web (sin cambios).
5. **Permisos**: los mismos de "Nuevo conteo" (`warehouse.count`). El servidor acepta crear con `warehouse.count.capture`, pero la web
   sigue exigiendo `warehouse.count` para el botón.

## Pendientes

- Nada bloqueante. Mejora posible: avisar bajo el selector, antes de crear, si el producto no tiene existencia en el almacén elegido.
