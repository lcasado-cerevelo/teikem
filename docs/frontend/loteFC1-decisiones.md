# Lote F-C1 — Órdenes: lista mejorada, alta detallada y entrega especial (2026-10-12)

Pantalla **Operación → Órdenes** (`/orders`). F-C se parte en tres sub-lotes (decisión del dueño, 2026-10-12): **F-C1** (este) = lista mejorada + alta DETALLADA + entrega especial;
**F-C2** = expediente (edición y acciones); **F-C3** = entrada rápida e importación CSV. Solo se tocó `web-app/` y `docs/`: el servidor y el contrato (`openapi.json`, `schema.d.ts`) ya traían el
lote 39 (`orders.view_price`, filtros nuevos de `GET /orders`) y **no se editaron**. Manual de pantallas: [fc1-ordenes-alta.md](../manual/frontend/fc1-ordenes-alta.md); preguntas del FAQ: sección «Lote F-C1» de [faq.md](../manual/faq.md). Referencia de la maqueta: `Diseño/teikem-mockups.html` (`ordenes()`, `filtersHtml`, `tableHtml`, `detailEditor`, `renderSec3`). Lotes hermanos:
[loteFB-decisiones.md](loteFB-decisiones.md), [loteFA1-decisiones.md](loteFA1-decisiones.md).

## Qué se construyó

| Pieza | Qué hace | Dónde (`web-app/src/`) |
|---|---|---|
| Lista | Filtros de la maqueta (Desde/Hasta, N.º de envío, Consignatario, Cliente, Servicio, Tipo de paquete, Pueblo, ZIP, Factura, Empaque, Limpiar) **más Estatus**; los cinco de selección son múltiples (`SearchSelect`) y viajan como arreglos repetidos (`?serviceTypes=A&serviceTypes=B`). Tabla paginada por el servidor con columnas ordenables Empaque, N.º orden, Fecha, Consignatario, Pueblo, ZIP, Servicio, Paquete, Piezas, COD, [Cotizado], Estatus; **una fila por tipo de paquete** con «↳», papelera y flecha «abrir» solo en la primera fila de la orden. Orden sobre TODO lo filtrado (`exportRows`). | `features/orders/OrderListScreen.tsx` |
| Alta detallada | Formulario desplegable arriba de la lista («Nueva orden» / «Ocultar formulario»): pasos 01 Cliente y servicio, 02 Consignatario, 03 Paquetes y COD (o Servicio especial) y «Ticket del envío». Dos botones: **Guardar borrador** (`confirmNow=false`) y **Guardar y confirmar** (`confirmNow=true`). | `features/orders/OrderEntryForm.tsx`, `orders.css` |
| Reglas | Filtros → consulta, agrupación por tipo de paquete, esquema zod con los mensajes exactos del servidor, request, renombre de errores, detección de `credit_exceeded` y de factura repetida. | `features/orders/orderRules.ts` |
| Hooks | `useOrders`, `useOrder`, `exportOrders`, `useCreateOrder` (con `Idempotency-Key`), `useDeleteOrder`. Se movieron desde `features/warehouse/api.ts` (`useOrdersReadonly`, `useOrderReadonly`, `exportOrders`); `useOrderLookup` se queda (lo usa Cruce de muelle). `OrderDetailScreen` solo cambió su import. | `features/orders/api.ts` |
| Selector de consignatario | Combobox con buscador sobre los consignatarios del cliente **más los compartidos** (`GET /locations?clientId&includeShared=true&locationType=DELIVERY`), con «dirección · pueblo» y «admite factura repetida»; el «+» abre el MISMO `ConsigneeFormModal` de F-B (con teléfono; exige `locations.create`) y deja elegido el recién creado. | `features/consignees/ConsigneePicker.tsx` (+ `useClientConsignees`, `useSelectedConsignee` en `api.ts`; `consigneeOptionLabel`/`consigneeAddress` en `consigneeRules.ts`) |
| Kit | `DataTable.continuationRows` + `DataColumn.continuation` (varias filas por registro en la vista de tabla) con prueba en `DataTable.test.tsx`; `IconChevronRight` («abrir», `actionIcons.tsx`). Anotado en `KIT.md` (sección «Órdenes»). | `kernel/ui` |
| Pequeños ajustes | `useClient(publicId, quiet)` y `useConsignee(publicId, quiet)` (consulta secundaria: un 403 no saca de la pantalla); `useSpecialServices(..., enabled)`. | `features/clients`, `features/consignees` |
| Textos | `orders.list.*` (rehecho), `orders.entry.*` y `orders.consignee.*` en `es.json` y `en.json` (se conserva `orders.detail.*`). | `kernel/i18n/` |

## Cómo se probó

| Comando / archivo | Resultado |
|---|---|
| `npm run check` en `web-app/` | tipos generados sin cambios de contenido, `tsc -b`, oxlint (solo avisos que ya existían; ninguno nuevo en `features/orders` ni `features/consignees`), vitest y build: **pasan**. vitest: **161 archivos / 1678 pruebas** |
| `orderRules.test.ts` | filtros → consulta (arreglos, `to` exclusivo), agrupación por tipo de paquete, esquema (mensajes del servidor), request (borrador, factura tecleada, crédito/duplicada, entrega especial sin paquetes/COD/chofer), renombre de errores, `credit_exceeded`, factura repetida, paridad es/en de `orders.*` |
| `OrderListScreen.test.tsx` (15) | filas por tipo de paquete con «↳» y contador por órdenes, columnas en el orden de la maqueta, filtros como arreglos repetidos y texto, pueblos desde los consignatarios, `Hasta` incluyente, Limpiar, ordenar lee todo lo filtrado (`take=200`), error del servidor, papelera (solo estatus inicial y `orders.cancel`), abrir, eliminar (204 y 422), columna Cotizado con/sin `orders.view_price`, «Nueva orden» con/sin `orders.create` |
| `OrderEntryForm.test.tsx` (27) | predeterminados, factura «(automático)»/habilitada, consignatario existente (solo lectura y `allowDupInvoice`), ticket sin precio, líneas de paquete, validaciones sin llamar al API, borrador vs confirmar (cuerpo e `Idempotency-Key`), 400 por línea, 422 sin tarifa, cliente de baja, factura repetida (confirmable, «No crear», bloqueada), crédito excedido con y sin `orders.credit_override`, entrega especial (servicio, sin servicios, componente apagado, sin `contracts.read`), consignatario nuevo por el modal (no guarda la orden y queda elegido), permisos |
| `ConsigneePicker.test.tsx` (4) | etiquetas, búsqueda por nombre/dirección/pueblo, compartidos, inactivos fuera, elegido que no está en la lista, sin cliente, «+» según `locations.create` |
| `DataTable.test.tsx` (+1) | filas de continuación: cuenta de filas, contenido por columna, clic en una continuación abre el registro, paginación por registros |
| `e2e/loteFC1-ordenes.spec.ts` y `e2e/f6.spec.ts` | **escritos, NO ejecutados** (el dueño no quiere Playwright en esta sesión). `f6.spec.ts` (~L470) ya no exige que falte el botón «Nuev…» y ubica la fila por el número de envío (la tabla ya no lleva columna de cliente). Las capturas `docs/manual/frontend/img/fc1-*.png` no existen todavía |

## Diferencias con la maqueta (todas con su razón)

Decididas por el dueño (2026-10-12):

1. **Sin el segmentado Rápida/Detallada.** La entrada rápida llega en F-C3, que agrega el segmentado; mientras tanto el formulario es siempre el detallado.
2. **Sin «Waybill summary», «Packing list», «Imprimir label» ni «Todos los labels»** (pie de la tabla de la maqueta), ni POD ni documentos: se difieren a F-G.
   *Actualización F-G2 (2026-10-12): ya existen los cuatro botones en el pie de la tabla, la columna «Elegir» y «Label al guardar» junto a los
   botones de guardar (`docs/frontend/loteFG2-decisiones.md`). POD sigue pendiente (P7).* El botón de la maqueta «Guardar e imprimir label» es aquí
   **Guardar borrador / Guardar y confirmar**, y la nota «El label sale automático…» se reemplazó por la explicación de esos dos botones.
3. **El ticket no muestra precio** (cliente, servicio, consignatario, pueblo, paquetes ×piezas y COD). El precio solo se ve después y únicamente con `orders.view_price`: columna **Cotizado** de la lista
   (omitida sin el permiso) y campo de la ficha («—» si viene null).
4. **Consignatario por selector, no por texto libre.** La maqueta deja escribir un nombre nuevo en el mismo campo; aquí se elige uno del directorio del cliente (más compartidos) y el «+» abre el modal de
   alta de F-B. Al elegir uno existente, nombre/número/dirección/pueblo/ZIP/teléfono quedan de solo lectura (la maqueta los dejaba editables y, si se tocaban, no se guardaba nada). La orden se manda con
   `consigneeLocationPublicId` (nunca `newConsignee`).
5. **Entrega especial sin chofer.** La maqueta pide el chofer y arma el viaje al guardar; aquí el POST lleva `isSpecialDelivery` y `specialServiceId`, sin paquetes ni COD, y el chofer se asigna después (F-C2).
   En la sección 03 aparece «N.º de envío» (habilitado solo si el cliente asigna sus números) y «Servicio especial» (`etiqueta · tarifa`, de `GET /clients/{id}/special-services`). El ticket cambia «Paquetes» por
   «Servicio especial» y no muestra COD.
6. **Formulario desplegable** dentro de `/orders` (como la maqueta), no en una ruta aparte.

Del implementador:

7. **Columna Cliente.** La maqueta no la tiene en la tabla (el cliente se filtra y se ve en el expediente); se respetó. La **exportación** sí lleva Cliente y Factura (columnas de exportación aparte) y todas las filas del filtro.
8. **Fecha completa** (`10/12/2026`, formato de la compañía) en lugar de `fmtDayMonth`: una lista que cruza años sería ambigua.
9. **Lista/Límite = requestedDate/promisedDate** del servidor (se mandan como `YYYY-MM-DDT00:00:00`, ambos con «hoy» por omisión como la maqueta). El servidor no valida el orden entre ambas; el formulario sí
   («La fecha límite no puede ser anterior a la fecha lista.»), una validación solo de pantalla.
10. **N.º de envío solo en la entrega especial**, como la maqueta (en el alta normal siempre lo genera Teikem). Si el cliente asigna su número de envío y se necesita teclear el suyo, es una orden especial
    o F-C3 (importación). El N.º de factura sí está en el paso 01 y se habilita cuando `clientAssignsInvoiceNumber`.
11. **Pueblo del filtro**: las opciones son los pueblos de los consignatarios activos del directorio (`GET /locations`), porque no hay un endpoint de «pueblos con órdenes» y una orden siempre entrega a un
    consignatario. Es un filtro múltiple con buscador (`SearchSelect`) en lugar del `<select>` de la maqueta. Sin `locations.read` queda vacío.
12. **Filtros múltiples**: Cliente, Servicio, Tipo de paquete, Pueblo y Estatus usan `SearchSelect` (el `msel` de la maqueta). **Desde/Hasta** es el `DateRangeFilter` del kit (un control de doble ancho), no dos
    `<input type="date">` sueltos; «Hasta» incluye el día elegido. La etiqueta «Sometida desde» de la maqueta es «Fecha» (la fecha de creación).
13. **Tabla**: `DataTable` del kit (orden, paginación del servidor, exportación a Excel/CSV/PDF, tarjetas bajo 720 px) en lugar de la tabla con `overflow:auto; max-height:380px`. En tarjetas todos los tipos de
    paquete van juntos en la tarjeta de la orden (con «×piezas»). Se conserva el `QBox` de búsqueda libre (convención del documento maestro).
14. **Papelera con confirmación** (`ConfirmDialog`); la maqueta borraba al instante. La papelera sale solo si `isInitialStatus` y con `orders.cancel`; si el servidor rechaza (422) muestra su mensaje en el diálogo.
15. **Aviso de «orden guardada»**: al crear, el formulario se cierra y se limpia, sale un toast con el número y un aviso sobre la lista con «Abrir el envío» (la maqueta imprimía el label y repintaba).
16. **Ancho del modal de factura repetida**: `size="sm"` del kit (la maqueta usa 520 px). Agrega un enlace «Ver la orden {número}» a la orden existente.
17. **Paso 1-2-3 estático**, igual que la maqueta (solo decora); se oculta bajo 980 px, donde el ticket pasa debajo de las secciones.

## Supuestos

- `quotedAmount` llega null sin `orders.view_price`: la lista omite la columna, la ficha muestra «—». El `COD` no es un precio y siempre se ve.
- El borrador **no** cotiza ni revisa crédito (es del servidor): «Guardar borrador» nunca pide autorización. Solo «Guardar y confirmar» puede responder `credit_exceeded` y, con `orders.credit_override`,
  mostrar «Autorizar y confirmar» (reenvía con `overrideCredit=true`); sin ese permiso solo se muestra el mensaje (con una nota) y la orden no se crea. Sin `orders.view_price` el servidor manda el mensaje sin cifras.
- Cada intento de envío lleva su propia `Idempotency-Key` (`crypto.randomUUID()`); el reenvío tras «Crear de todos modos» o «Autorizar y confirmar» cambia el cuerpo, así que usa otra clave.
- 409 `duplicate_invoice_confirmable` solo ocurre cuando se tecleó la factura (el cliente la asigna); los datos del modal salen de `errors.existingOrderNumber/existingOrderPublicId`. La variante bloqueante
  (`duplicate_invoice`) deja el mensaje bajo el campo de factura.
- Habilitar «Es una entrega especial» exige `contracts.read` y el módulo CATALOG (lo que pide `GET /clients/{id}/special-services`); sin ellos el interruptor está deshabilitado con la razón. Con el componente
  «Servicios especiales» apagado en el contrato, el select queda vacío con el aviso correspondiente.
- La numeración del cliente (`numberSettings`) sale de `GET /clients/{id}` (necesita `clients.read`, que el selector de cliente ya exige); si no se puede leer, el campo de factura queda en «(automático)».
- Los modales de la pantalla (alta de consignatario, factura repetida, crédito) cuelgan del árbol de React del formulario: el selector corta el `submit` del modal de consignatario para que no guarde la orden
  (`ConsigneePicker.tsx`, probado).
- El teléfono del consignatario se lee de `GET /contacts/LOCATION/{id}` (consulta secundaria); sin permiso queda vacío.

## Qué no se pudo verificar

- **Playwright**: los dos recorridos (`loteFC1-ordenes.spec.ts`, `f6.spec.ts`) están escritos o ajustados y no se ejecutaron. Los selectores y las capturas son de papel hasta correrlos contra el API real.
- El aspecto a 360 px, la rejilla `.detail`/ticket, los colores y el alineado de las filas de continuación (`ui.css`, `orders.css`) no se miraron en un navegador (no se levantó el dev server).
- El **flujo real de confirmar** (cotización con tarifa, crédito excedido de verdad, factura repetida real) depende de datos del servidor: se probó contra respuestas simuladas con los códigos y cuerpos del manual 03.
