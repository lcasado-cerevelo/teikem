# Lote F-E2b — Plano de ruta, arrastrar y pin manual (2026-10-12)

Tercer sub-lote de F-E (F-E1 escaneo, F-E2a Sala de despacho sin mapa, **F-E2b** este, F-E3 Monitoreo pendiente). Se agrega a la ficha de la ruta de la Sala de despacho (`/ops/dispatch`, `trips.dispatch`, módulo `LTL_GROUND`) un **plano esquemático de la ruta** (componente reutilizable `RouteMap`), reordenar las paradas **arrastrando** y el **pin manual** de una parada. Manual: sección «El plano de la ruta», «Ordenar y quitar órdenes» y «Poner un pin manual» de [fe2a-despacho.md](../manual/frontend/fe2a-despacho.md) (no hay capítulo aparte); preguntas: sección «Lote F-E2b» de [faq.md](../manual/faq.md). Referencia: `Diseño/teikem-mockups.html` (`dispatch()`). Lote hermano: [loteFE2a-decisiones.md](loteFE2a-decisiones.md).

## Qué se construyó

| Pieza | Qué hace | Dónde |
|---|---|---|
| `RouteMap` | Plano SVG propio (sin librerías, sin teselas, sin servidores externos): pines numerados con la secuencia, exacto = círculo sólido, aproximado = círculo punteado con halo, manual = rombo; estados entregada/fallida/en curso por color; línea punteada que une la secuencia (continua entre entregadas); cuadrícula tenue, norte y escala; «Sin ubicación: N» aparte; vacío con indicación; leyenda; punto vivo, origen y pin en prueba opcionales; clic en el plano (`onPick`); foco por teclado y `aria-label` «Parada 3 · Consignatario · pueblo» | `web-app/src/kernel/ui/RouteMap.tsx`, `ui.css` |
| Reglas del plano | `fitProjection` (equirectangular ajustada al rectángulo, con cos(latitud media), centrada, `unproject` exacta), `spreadOverlaps` (pines encimados en abanico), `niceScale`, `accuracyOf`, `stateOf`, `isGeoPoint` | `kernel/ui/routeMapRules.ts` |
| Integración | El plano va arriba de la ruta activa (como la maqueta), con un resumen de la parada elegida y su «Poner pin» / «Mover pin»; la selección pin ↔ fila es mutua; el orden mostrado (plano y lista) es el pedido mientras viaja la petición | `dispatch/ActiveRoutePanel.tsx` |
| Arrastrar | Asa en cada orden con Pointer Events propios (ratón, lápiz y dedo; `setPointerCapture`, `touch-action:none`, desplazamiento automático al borde de la lista); ↑ ↓ en el asa y ▲ ▼ siguen; solo con `trips.plan` y ruta editable | `dispatch/RouteStopsList.tsx`, `dispatchRules.ts` (`reorderIds`, `rowIndexAt`) |
| Orden optimista | Lista y plano cambian al instante; si el servidor niega, vuelven al orden anterior y se muestra su texto exacto; un 409 además recarga la ruta (`PendingOrder` solo vale mientras la versión (`rowVersion`) sea la de la petición) | `ActiveRoutePanel.tsx` |
| Pin manual | Ventana con Latitud y Longitud (punto o coma), validadas con los mismos textos del servidor; con otras paradas ubicadas, plano de referencia para tomar una posición aproximada; desde la fila, desde el resumen del plano o desde el aviso `APPROXIMATE_PINS` | `PinModal.tsx`, `TripIssues.tsx`, `dispatchApi.ts` (`useSetStopLocation`), `dispatchRules.ts` (`parseCoordinate`, `pinSchema`, `buildPinRequest`) |
| Kit | Sección «RouteMap» de `KIT.md` con su contrato de props; claves `routeMap.*`, `dispatch.map.*`, `dispatch.pin.*` y `dispatch.stops.*` nuevas en es/en | `KIT.md`, `kernel/i18n/{es,en}.json` |

Servidor, `openapi.json` y `schema.d.ts`: **no cambiaron** (ver «Defectos»). Se comprobó por API cada mensaje del manual 05 §4.2–§4.3 (400 con el texto y el campo, 404 de parada ajena, 409 de versión, 422 de ruta despachada): todos coinciden.

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `routeMapRules.test.ts` | proyección y ajuste al rectángulo (margen, centrado, un solo punto, corrección con cos(latitud)), inversa exacta, pines encimados, escala, claves de precisión y estado |
| `RouteMap.test.tsx` | pines por precisión y estado, línea, vacío y «Sin ubicación», selección y teclado (Enter, espacio, `aria-pressed`), punto vivo y origen, borrador, clic en el plano, sin imágenes ni servidores |
| `dispatchMapRules.test.ts` | reordenar y fila bajo el puntero, validación de latitud/longitud con los textos del servidor (coma y punto, rangos, no número), request, paradas del plano, paridad es/en |
| `DispatchMap.test.tsx` (25) | plano dentro de «Ruta activa» arriba de las órdenes, vacío con indicación, selección pin ↔ fila, arrastrar (PUT con la secuencia completa y `rowVersion`, estado optimista, hacia arriba, cancelar, vuelta atrás con el texto del servidor, 409 con recarga), teclado y ▲ ▼, pin manual (errores en pantalla, coma decimal, error 422/400/404 dentro de la ventana, «Mover pin», desde el plano, plano de referencia, sin referencias, desde el aviso), permisos (sin `trips.plan`) y ruta despachada o en curso |
| `npm run check` | tipos generados, tsc, oxlint (sin avisos nuevos), vitest **193 archivos / 2408 pruebas**, build: todo pasa |
| `e2e/loteFE2b-mapa.spec.ts` | **ejecutado** contra el API real (Chrome del equipo): escritorio **1 de 1** y móvil (360 px, sin scroll horizontal en cada paso) **1 de 1**. Capturas `docs/manual/frontend/img/fe2b-*.png` (6) |
| `dotnet test` | no se corrió: no se tocó el servidor |

El recorrido siembra por API (cliente, zona, chofer, vehículo, 4 órdenes en una ruta) y cubre: plano vacío con su mensaje; pines tras ponerlos por API; elegir pin ↔ fila (también con teclado); pin manual por pantalla con error de rango, plano de referencia y guardado (la API devuelve `MANUAL` y la coordenada); arrastrar con el ratón dos veces, ▲ ▼ y teclado en el asa (el orden se verifica por API cada vez); ruta despachada sin acciones de edición (y el 422 del servidor al intentar poner un pin).

## Defectos encontrados y arreglados

Los encontró el recorrido de Playwright; las pruebas de componente (jsdom, sin diseño ni eventos reales) no los veían. Todos eran de código de este mismo lote, antes de entregarlo:

1. **El plano se pintaba de 16 px.** La regla global `svg{width:16px;height:16px}` de la aplicación gana a los atributos `width`/`height` del SVG. `.rmap-svg` ahora fija `width:100%;height:100%`. (Prueba: el recorrido verifica que los pines caen dentro del plano; regla anotada en `KIT.md`.)
2. **Un clic en el plano no llegaba al fondo.** Un relleno de patrón (la cuadrícula) solo recibe el puntero sobre sus líneas; el fondo que recibe el clic es ahora un rectángulo sólido y la cuadrícula va encima con `pointer-events:none`. (Recorrido: clic en el plano de referencia del pin manual.)
3. **Elegir un pin reordenaba el DOM** (el elegido se dibujaba al final para quedar encima), lo que hacía perder el foco del teclado y cambiaba el orden de Tab. Los pines mantienen siempre el orden de la secuencia; la selección se marca con un aro. (Prueba de pantalla: selección con teclado.)
4. **Filas de «Órdenes en esta ruta» ilegibles en la columna central (≈ 330 px a 1280 px de pantalla):** con el asa y el botón del pin, el texto se partía letra por letra y cada fila medía 175 px. Las filas ahora pasan la hora y los botones a una segunda línea (`flex-wrap`).

5. **Reordenaciones seguidas (seguimiento, intermitente en el recorrido): la segunda salía con la versión (`rowVersion`) vieja.** Un segundo movimiento (flecha, ▲▼ o arrastre) antes de que la pantalla recibiera la respuesta del primero mandaba `PUT route/sequence` con la versión anterior (409 espurio y vuelta atrás que perdía el movimiento); además el asa y los botones se deshabilitaban mientras viajaba la petición (las flechas rápidas se perdían) y una lectura de la ficha en vuelo podía pisar la ficha nueva con una anterior. Arreglo en la pantalla: las reordenaciones van **en cola, una petición a la vez**; lo pedido mientras viaja una se junta en UNA con la última secuencia y la versión que devolvió la anterior (`ActiveRoutePanel.applyOrder`), el asa ya no se bloquea, y toda escritura de la ficha cancela antes las lecturas en vuelo (`putDetail` en `dispatchApi.ts`). El error real del servidor sigue deshaciendo el orden optimista con su texto exacto. Pruebas: ráfaga y error en cola en `DispatchMap.test.tsx`; el recorrido ahora incluye tres flechas seguidas. (Otra causa de fallos del recorrido con varios workers era del propio spec: los códigos de cliente/zona se recortan y chocaban; ahora el azar va primero.)

Del servidor: sin defectos (ver arriba).

## Diferencias con la maqueta (todas con su razón)

1. **El mapa es un plano esquemático sin teselas, sin calles ni nombres** (la maqueta pinta un mapa de fondo con `mapBackdrop()`): ver la decisión 1 de la lista de abajo.
2. **Sin conmutador 2D / Satélite**: no hay imagen de satélite; no existe el botón (no hay botones falsos).
3. **Sin «tracking en vivo» ni el punto verde de la ruta despachada** (la maqueta lo pone fijo en el 43 % / 40 %): el dato en vivo es de F-E3; el componente ya acepta `live`.
4. **Pines en una cuadrícula con escala y norte** en lugar de posiciones fijas en porcentaje: la posición sale de las coordenadas reales de la parada.
5. **Tres tipos de pin**: exacto (círculo sólido, el pin de gota de la maqueta se cambió por un círculo para poder numerarlo con claridad), aproximado (círculo punteado con halo, como la maqueta) y **manual** (rombo; no existe en la maqueta).
6. **La leyenda** dice «Pin exacto», «Aproximado (centro del CP o del pueblo)» y «Pin manual», más los estados que aparezcan, y una línea que avisa que el plano es esquemático.
7. **El plano abre la ficha** (arriba del chofer) igual que la maqueta; se agrega un resumen de la parada elegida con «Poner pin» / «Mover pin» y la lista «Sin ubicación: N».
8. **Asa de arrastre, botón del pin, selección de fila y marcas «Sin pin» / «Pin manual»** en «Órdenes en esta ruta» (la maqueta solo trae el número, el consignatario y quitar).
9. **Pin manual con ventana, latitud/longitud y plano de referencia**: no existe en la maqueta (es pedido del manual 05 §4.3).

## Decisiones tomadas por default para presentar al dueño al final de P1

- **El mapa es un plano esquemático sin teselas; para un mapa con calles reales haría falta una librería (p. ej. Leaflet) y un proveedor de teselas (p. ej. OpenStreetMap): dependencia nueva y llamadas a un servidor externo; decidir.** Mientras tanto no se instaló ni se descargó nada.
- Sin conmutador 2D / Satélite (no hay satélite); no se agregó botón falso.
- La ruta se dibuja uniendo las paradas con líneas rectas en el orden de la secuencia; el detalle de la ruta no trae el trazo de la optimización (`polyline` solo viene en la respuesta de «Optimizar»), así que no se depende de él.
- Proyección equirectangular ajustada al rectángulo de los puntos (con corrección cos(latitud media)), margen de 28 px y alcance mínimo de ≈ 1 km: un plano de un solo pin no se «acerca» sin fin. El tamaño del plano es el del panel (alto 300 px).
- **Precisión del pin**: `MANUAL` → rombo, `EXACT` → círculo sólido, todo lo demás (centroide de CP o de pueblo, o sin dato) → aproximado; igual que la regla `IsApproximatePin` del servidor. Una parada sin punto no se dibuja y se lista en «Sin ubicación: N».
- **Estados**: `COMPLETED`/`DELIVERED` verde, `FAILED` rojo, `ON_THE_WAY`/`ARRIVED` (y `IN_PROGRESS`) en curso; hoy todas las paradas están `PENDING` (la app del chofer, P7, las moverá). La línea continua verde solo une paradas entregadas.
- El origen («almacén/salida») solo se pinta si el servidor lo da; hoy el detalle de la ruta no lo trae y **no se inventa**.
- Pines en el mismo punto se abren en abanico (≤ 26 px alrededor del centro) para que se puedan tocar; el pin elegido no cambia de lugar en el DOM (el foco por teclado manda).
- Selección pin ↔ fila mutua; el pin elegido recibe un aro; al elegir desde el plano la lista se desplaza dentro de su propia caja (sin mover la página).
- **Arrastrar**: asa propia con Pointer Events (no HTML5 drag-and-drop: no funciona con el dedo); la fila bajo el puntero es el destino; ↑ ↓ en el asa y ▲ ▼ siguen; el desplazamiento de la lista al acercarse a su borde es automático. Solo con `trips.plan` y ruta editable (`isEditable`).
- **Orden optimista con vuelta atrás**: lista y plano cambian al instante; si el servidor niega, vuelven al orden anterior con su texto exacto; un 409 también recarga. `PutSequence` siempre manda **todas** las paradas de la versión vigente y su `rowVersion`.
- **Pin manual**: latitud y longitud escritas son el camino principal (punto o coma, validadas en pantalla con los textos del servidor, más «debe ser un número» para lo que no es número, texto propio porque el servidor no puede recibirlo). **Tomar la posición del plano** es una ayuda secundaria: solo si hay al menos otra parada con punto, y siempre avisando «Posición aproximada tomada del plano». «Usar el centro del plano» **no** existe. El error del servidor sale dentro de la ventana, que no se cierra.
- Al guardar el pin la ruta **no se reoptimiza sola**; la ventana lo recuerda («vuelva a optimizarla»). El servidor recalcula las llegadas.
- El aviso `APPROXIMATE_PINS` trae «Poner pin manual» (abre la primera parada aproximada) solo con `trips.plan` y ruta editable.
- El plano va **arriba** de la ruta activa, como la maqueta (antes del chofer); el resumen de la parada elegida va debajo.
- Los textos propios del componente están en `routeMap.*` (genéricos, para que F-E3 los reutilice) y los de la pantalla en `dispatch.map.*` y `dispatch.pin.*`.
- El manual **no tiene capítulo aparte**: se amplió `fe2a-despacho.md` (la maqueta y la pantalla son la misma).

## Qué no se pudo verificar

- **Arrastre con un dedo real** (pantalla táctil): el recorrido móvil arrastra con el ratón de Playwright a 360 px; el asa tiene `touch-action:none` y usa Pointer Events (que el navegador también manda con el dedo), pero no se probó en un teléfono.
- **Pines exactos y aproximados con datos reales**: la base de pruebas no geocodifica; en el recorrido todos los pines son manuales (los pone la prueba). Exacto y aproximado se verificaron con respuestas simuladas.
- **Rutas con decenas de paradas y pines casi encimados** (más de 3): solo con datos simulados.
- **Dos usuarios a la vez** reordenando la misma ruta: el 409 y la vuelta atrás se probaron con respuestas simuladas.
- **Pantallas muy angostas con el plano de referencia del pin** en un teléfono: se verificó que no hay scroll horizontal a 360 px, pero la facilidad de tocar el plano con el dedo no se probó.
