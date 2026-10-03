# Lote F13 — Nuevo conteo "Por producto" desde la web (2026-10-03)

> **Actualizado (segundo bloque de decisiones del dueño, 2026-10-03; ver [lote22-decisiones.md](../lote22-decisiones.md))**: el selector de
> producto **sí** filtra por existencia: switch **Solo con existencia**, activado de entrada (sección "Segundo bloque" al final). La
> "Decisión para el dueño" 1 de este documento (el selector no filtra por existencia) quedó **superada**.

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

1. **[SUPERADA 2026-10-03: ahora hay un switch "Solo con existencia", encendido de entrada; ver abajo]** **El selector de producto no filtra por existencia**: ofrece todos los productos activos (con `onlyAvailable` no se podría elegir uno
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

## Segundo bloque (2026-10-03): switch "Solo con existencia"

Decisión del dueño: junto al selector de producto, un switch **Solo con existencia**, **activado de entrada**, que filtra el selector a
los productos con existencia en el almacén elegido; apagado se ven todos y el 400 del servidor sigue saliendo junto al selector.

| Pieza | Qué hace | Dónde |
|---|---|---|
| Switch | Estado local del modal (`useState(true)`), sin guardar la preferencia (al reabrir vuelve a nacer encendido). Visible solo en la pestaña "Por producto" | `CreateCountModal.tsx` |
| Filtro | Encendido: `ProductPickerInput warehousePublicId={almacén} onlyOnHand` → `GET /api/v1/products?activeOnly=true&warehousePublicId=…&onlyOnHand=true`. Apagado: sin ambos | `CreateCountModal.tsx`, `pickers.tsx` (`ProductPicker`/`ProductPickerInput` ganan la propiedad `onlyOnHand`) |
| Textos | `warehouse.cycleCounts.fields.{onlyWithStock,onlyWithStockHelp}` en es/en | `kernel/i18n/{es,en}.json` |

**Qué filtro del API se usó.** `onlyAvailable` (el que ya tenía el selector) mide el *disponible recolectable* (sin cuarentena ni cross-dock
y sin lo reservado), que no es lo que cuenta un conteo por producto (saldo en mano > 0 de cualquier posición). El API de productos ya
ofrecía `onlyOnHand` (existencia en mano > 0, en el almacén indicado), que coincide con la regla del servidor; se agregó al selector. **No
hizo falta tocar el API ni regenerar el cliente.** Un producto con toda su existencia reservada sí aparece (y el conteo se crea).

### Cómo se probó (resultados reales)

| Comando | Resultado |
|---|---|
| `npm run check` en `web-app/` (api:types, tsc -b, oxlint, vitest, build) | **pasó**: 117 archivos, 1183 pruebas; solo avisos de oxlint ya existentes |
| `CycleCountScreen.test.tsx` (3 pruebas nuevas, 1 ajustada): el switch nace encendido y el selector pide `onlyOnHand=true` con el almacén elegido; apagado no manda ni `onlyOnHand` ni `onlyAvailable` ni almacén y al encender vuelve a filtrar; la preferencia no se guarda (reabre encendido, sin `localStorage`); con el switch apagado el 400 sale junto al selector y el modal sigue abierto | **17 pasaron** |
| `npx playwright test e2e/loteF13-conteo-web.spec.ts --no-deps --workers=1` (API real, Vite) | **2 pasaron** (escritorio y móvil 360 px): con el switch encendido el producto sin existencia **no** se ofrece ("No hay productos que coincidan.") y el que sí tiene existencia sí; apagado, se elige el producto sin existencia y sale el 400; encendido otra vez, se crea el conteo |
| Capturas | `f13-por-producto.png` y `f13-sin-existencia.png` regeneradas (muestran el switch); las demás capturas que regeneró Playwright (F12 y el resto de F13) se descartaron con `git checkout` |

### Decisiones para el dueño (valor más seguro)

1. Se usa `onlyOnHand` (existencia en mano, incluye lo reservado) y no `onlyAvailable`; así el selector ofrece exactamente lo que el servidor
   puede contar. Si prefiere "disponible", es cambiar una propiedad.
2. Sin almacén elegido el switch no filtra por almacén (el filtro de existencia se aplica sobre todos los almacenes hasta que se elige uno).
3. Si el usuario ya eligió un producto y luego enciende el switch, la elección se conserva (si no tiene existencia, el servidor responde el 400 al crear).
