# 54 — Compras: importar con la plantilla del usuario y pasar las órdenes de compra a QuickBooks (archivo IIF)

<!-- kemi:staff -->

Lote 246 (2026-11-25). Dos cosas nuevas en **Compras**:

1. **Importar órdenes de compra con una plantilla que usted define** (qué columna de su archivo es el proveedor, el número de documento, el SKU, la cantidad y el costo), además de los dos orígenes que ya existían (QuickBooks y archivo genérico, Lote 239).
2. **Pasar las órdenes de compra a QuickBooks Desktop** con un archivo IIF: una, varias o todas las que faltan; después se contesta si QuickBooks las recibió.

> **Aviso importante: el archivo IIF de órdenes de compra todavía NO se ha probado contra un QuickBooks Desktop real.** Se comprobó que el archivo sea consistente (cada orden suma 0.00 por dentro), no que QuickBooks lo acepte tal cual. Falta probarlo en una copia de la compañía de Solutions. Hasta entonces, ver la sección 10: marque lo histórico con «Marcar todas» y exporte sin proveedores ni productos nuevos hasta confirmar los nombres.

> **Exportar no marca nada.** El archivo sale, la orden queda **«Exportada, por confirmar»** y usted dice después si QuickBooks la recibió. Ninguna acción de QuickBooks cambia el estatus de la orden de compra.

> **Una orden que vino de QuickBooks nunca se exporta ni se marca.** Ya está allá.

Capturas de las pantallas: [L246](frontend/l246-compras.md). Cada mensaje de error está explicado en la [FAQ](faq.md). Este capítulo continúa el [06 — Inventario y almacén, §8a (Importar órdenes de compra)](06-inventario-y-almacen.md#8a-importar-órdenes-de-compra-lote-239) y se parece al [49 — Facturas a QuickBooks](49-quickbooks.md), que explica el mismo flujo para las facturas de venta.

## 1. Dónde está y quién puede

- **Dónde:**
  - **Contabilidad → Compras** (`/warehouse/purchase-orders`): la lista (con las columnas y filtros **«Origen»** y **«QuickBooks»**), el botón **«Importar»** (ahora con el origen **«Plantilla (la del usuario)»**) y el botón **«QuickBooks»**.
  - La **ficha** de la orden de compra: la franja de origen y estado de QuickBooks, con quién y cuándo, y sus acciones.
  - **Sistema → Plantillas**, pestaña **«Órdenes de compra»**.
  - **Ajustes → Operación → QuickBooks:** el campo **«Cuenta de órdenes de compra»**.
- **Módulos:**
  - **Compras (`PURCHASING`)** para todo lo de este capítulo. Con el módulo apagado, las rutas responden 403 con `El módulo 'PURCHASING' no está habilitado para esta compañía.`
  - **Integraciones (`INTEGRATIONS`)** para abrir **Sistema → Plantillas**. En una base nueva lo enciende solo el administrador de la plataforma. **No hace falta** para crear o editar la plantilla desde el importador de Compras.
  - **Órdenes de venta (`SALES_ORDERS`)** para el campo «Cuenta de órdenes de compra» (vive en los ajustes de QuickBooks de ventas).
- **Permisos:**

| Qué | Permiso | Módulo |
|---|---|---|
| Ver la lista, la ficha, el origen y el estado frente a QuickBooks; ver las plantillas de órdenes de compra y sus campos | `purchasing.view` | `PURCHASING` |
| Importar con plantilla (o con los otros orígenes); crear, editar, desactivar y reactivar plantillas | `purchasing.manage` | `PURCHASING` |
| Ver lo que falta por pasar, exportar, «Sí, ya están», «No pasó», marcar y quitar la marca | `purchasing.quickbooks` | `PURCHASING` |
| Ver la cuenta de órdenes de compra en Ajustes | `sales.quickbooks.view` o `admin.tenant` | `SALES_ORDERS` |
| Cambiar la cuenta de órdenes de compra | `admin.tenant` | `SALES_ORDERS` |

- `purchasing.quickbooks` está en la categoría de permisos de Facturación (`BILLING`) y trae `purchasing.view` con él. Lo reciben el **administrador de la compañía** y **Facturación** (`Billing`); también se agregó a esos dos roles en las compañías que ya existían. Otros roles no lo tienen: el administrador puede dárselo en Roles.
- Sin permiso, el API responde 403 con `Falta el permiso 'purchasing.quickbooks'.` (o el que corresponda) y queda un evento `PERMISSION_DENIED` en los eventos de seguridad. En pantalla, el botón «QuickBooks» y las acciones de QuickBooks de la ficha no salen.
- El `TenantId` sale de la sesión: cada compañía ve solo lo suyo. Una plantilla de otra compañía responde 404 y una orden de otra compañía responde 400 «Alguna de las órdenes de compra indicadas no existe.», sin decir cuál de las dos cosas pasó.
- **Ni el portal ni la app del cliente muestran nada de esto.**

## 2. La plantilla de importación de órdenes de compra

**Qué hace.** Dice cómo leer un archivo de compras que no tiene el formato de QuickBooks ni el genérico: qué columna es cada dato. Se crea una plantilla por proveedor o por formato de archivo.

**Quién puede.** Ver: `purchasing.view`. Crear, editar, desactivar y reactivar: `purchasing.manage`. Módulo `PURCHASING`.

**Cómo se usa (pantalla).**
- Compras → **Importar** → origen **«Plantilla (la del usuario)»** → **«Nueva plantilla»** (o **«Editar»** sobre la elegida). La ventana se abre ahí mismo y, al guardar, la plantilla queda elegida.
- O **Sistema → Plantillas → «Órdenes de compra» → «Nueva plantilla»**. La pestaña no tiene columna «Cliente»: las plantillas de compras no llevan cliente.
- La ventana pide **Nombre**, **Delimitador** (coma, punto y coma, tabulador…), si **la primera fila es el encabezado**, y una tabla «Columna N → Campo destino». Los **valores por defecto** se usan cuando la celda viene vacía o el campo no tiene columna.

**Campos de la plantilla.**

| Campo | Qué es | ¿Obligatorio? | ¿Valor fijo (por defecto)? |
|---|---|---|---|
| `supplier` | Proveedor (nombre como está en Teikem) | Sí: en columna **o** como valor fijo | **Sí** (proveedor fijo de la plantilla) |
| `documentNumber` | Número de documento: las filas con el mismo número forman una orden | **Sí, en columna** | No |
| `orderDate` | Fecha de la orden (`AAAA-MM-DD` o `MM/DD/AAAA`) | No (sin fecha = hoy) | No |
| `expectedDate` | Fecha esperada de entrega | No | No |
| `sku` | SKU del producto | **Sí, en columna** | No |
| `description` | Descripción (solo sirve para «Descargar lo que falta»; no se guarda en la orden) | No | No |
| `quantity` | Cantidad | **Sí, en columna** | No |
| `unitCost` | Costo unitario (hasta 4 decimales) | No (sin costo = el del producto) | No |

- El **almacén** no es un campo de la plantilla: se elige en la ventana de importar y vale para todo el archivo.
- Una plantilla de compras **no admite cliente** ni cliente por defecto, ni «varias líneas» ni variantes.
- Los nombres de plantilla no se repiten (sin distinguir mayúsculas) **entre las del mismo tipo**: puede haber una de compras y una de envíos con el mismo nombre.
- Una plantilla **inactiva** no se puede usar para importar; reactívela o elija otra.

**API.**

| Qué | Llamada | Permiso |
|---|---|---|
| Campos disponibles | `GET /api/v1/purchase-orders/import-templates/fields` | `purchasing.view` |
| Lista | `GET /api/v1/purchase-orders/import-templates?includeInactive=false` | `purchasing.view` |
| Una plantilla | `GET /api/v1/purchase-orders/import-templates/{publicId}` | `purchasing.view` |
| Crear | `POST /api/v1/purchase-orders/import-templates` | `purchasing.manage` |
| Editar | `PATCH /api/v1/purchase-orders/import-templates/{publicId}` | `purchasing.manage` |
| Desactivar / reactivar | `POST .../{publicId}/deactivate` y `POST .../{publicId}/reactivate` | `purchasing.manage` |

Ejemplo de cuerpo para crear una plantilla con punto y coma, sin encabezado y proveedor fijo:

```json
{
  "name": "Compras semanales",
  "delimiter": ";",
  "hasHeader": false,
  "columns": [
    { "position": 1, "field": "documentNumber" },
    { "position": 2, "field": "sku" },
    { "position": 3, "field": "quantity" },
    { "position": 4, "field": "unitCost" },
    { "position": 5, "field": "orderDate" }
  ],
  "defaults": { "supplier": "Proveedor Ejemplo" }
}
```

- La ruta de las plantillas de envíos (`/api/v1/import-templates`) **no** acepta este tipo: pedir `kind=PURCHASE_ORDER` ahí responde 400 «Tipo de plantilla desconocido: PURCHASE_ORDER.».
- Cada alta, cambio o baja de plantilla queda en la bitácora como las demás plantillas.

**Validaciones (campo → regla → mensaje → HTTP).** Los errores de la definición salen por campo en `errors` del cuerpo de error.

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `columns` | Debe haber al menos una columna | `Indique al menos una columna.` | 400 |
| `columns` | Máximo 100 columnas | `Máximo 100 columnas.` | 400 |
| `columns` | Falta la columna `documentNumber` | `La plantilla debe incluir la columna documentNumber.` | 400 |
| `columns` | Falta la columna `sku` | `La plantilla debe incluir la columna sku.` | 400 |
| `columns` | Falta la columna `quantity` | `La plantilla debe incluir la columna quantity.` | 400 |
| `columns` | Sin columna `supplier` ni proveedor fijo | `La plantilla debe indicar el proveedor: en la columna supplier o como valor fijo.` | 400 |
| `columns[i].position` | 1 o mayor | `La posición debe ser 1 o mayor.` | 400 |
| `columns[i].position` | No más de 500 | `La posición no puede exceder 500.` | 400 |
| `columns[i].position` | Sin repetir | `La posición {n} está repetida.` | 400 |
| `columns[i].field` | Uno de los 8 campos | `Campo desconocido: {campo}.` | 400 |
| `columns[i].field` | Sin repetir | `El campo {campo} está repetido.` | 400 |
| `defaults.{campo}` | Solo `supplier` admite valor fijo | `El campo {campo} no admite valor por defecto.` | 400 |
| `clientPublicId`, `defaultClientPublicId` | Compras no lleva cliente | `Este tipo de plantilla no lleva cliente.` | 400 |
| `name` | Obligatorio | `El nombre de la plantilla es obligatorio.` | 400 |
| `name` | Hasta 120 caracteres | `Máximo 120 caracteres.` | 400 |
| `name` | Sin repetir entre las plantillas del tipo | `Ya existe una plantilla de importación con ese nombre.` | 409 |
| `delimiter` | Un solo carácter, distinto de comilla y salto de línea (`tab` = tabulador) | `El delimitador debe ser un solo carácter (p. ej. ',' o ';'), distinto de comilla y salto de línea.` | 400 |
| `publicId` | La plantilla existe, es de compras y es de su compañía | `Plantilla de importación no encontrado.` | 404 |
| desactivar | Ya estaba inactiva | `La plantilla ya está inactiva.` | 409 |
| reactivar | Ya estaba activa | `La plantilla ya está activa.` | 409 |

- Si faltan varias columnas obligatorias, el mensaje de `columns` muestra **una a la vez**, en este orden: `documentNumber`, `sku`, `quantity`, proveedor. Corrija y guarde otra vez.
- «Plantilla de importación no encontrado.» es el texto exacto del sistema.

## 3. Importar órdenes de compra con la plantilla

**Qué hace.** Lee el archivo con la plantilla, junta las filas que tienen el mismo **proveedor y número de documento** en una orden de compra y las crea. Una orden de compra dice que se compró: **no mueve inventario** (eso lo hace el recibo).

**Quién puede.** `purchasing.manage`, módulo `PURCHASING`.

**Cómo se usa (pantalla).** Compras → **Importar** → **Origen del archivo: «Plantilla (la del usuario)»** → elegir la **Plantilla** → **Estatus de las órdenes importadas** (Borrador o Completada = enviada) → **Almacén** → **Archivo** (CSV o `.xlsx`) → **«Revisar»** (no guarda nada) → **«Importar N»**. Las bloqueadas no se importan; el detalle dice por qué. Se corrige el archivo y se vuelve a importar **el mismo**: lo ya importado no se repite.

**API.** `POST /api/v1/purchase-orders/import`, `multipart/form-data` (hasta 4 MB, 5.000 filas o 2 MB de texto):

| Campo | Qué es |
|---|---|
| `file` | El archivo (obligatorio) |
| `source` | `GENERIC` (por omisión), `QUICKBOOKS` o `TEMPLATE`. Con `templatePublicId` y sin `source`, es `TEMPLATE` |
| `templatePublicId` | La plantilla de compras; obligatoria con `TEMPLATE` y solo con `TEMPLATE` |
| `status` | `DRAFT` (por omisión) o `SENT` |
| `warehousePublicId` | El almacén (opcional si la compañía tiene uno solo activo) |
| `commit` | `false` = vista previa sin guardar; `true` = crea las órdenes listas |

La respuesta trae el resumen (`totalDocuments`, `ready`, `blocked`, `duplicates`, `skipped`, `created`, `failed`), cada documento con su estado (`READY`, `CREATED`, `BLOCKED`, `ERROR`, `DUPLICATE`, `SKIPPED`) y sus avisos, los productos y proveedores que faltan y, con plantilla, `templatePublicId` y `templateName`.

**Cómo se leen los datos de la plantilla.**
- Las **fechas** son `AAAA-MM-DD` o `MM/DD/AAAA`. Una orden sin fecha toma hoy.
- Los **números** usan punto decimal; la **coma es separador de miles** («1,250.50»). Se quitan `$` y espacios.
- El **SKU** se toma tal cual (no se separa «SKU (Descripción)»); un SKU que solo difiere en guiones, espacios o signos se toma como el de Teikem y avisa con `SKU_ALIAS`.
- Una celda vacía toma el valor fijo de la plantilla, si lo hay.
- Un costo con más de 4 decimales se redondea y avisa (`COST_ROUNDED`). Una línea de cantidad 0 se omite y avisa (`ZERO_QTY`). Si dos líneas de la misma orden traen el **mismo producto con el mismo costo**, se suman y avisa (`MERGED_LINES`).

**Avisos del resultado.** Los que **bloquean** la orden impiden crearla; los demás solo informan.

| Código | ¿Bloquea? | Cuándo sale |
|---|---|---|
| `BAD_VALUE` | **Sí** (solo con plantilla) | Una fecha o un número ilegible, o una fila sin número de documento |
| `COST_CONFLICT` | **Sí** (todos los orígenes) | El mismo producto en varias líneas de la orden con costos distintos |
| `MISSING_PRODUCT`, `AMBIGUOUS_PRODUCT` | Sí | El SKU no existe, o coincide con más de un producto |
| `MISSING_SUPPLIER`, `INACTIVE_SUPPLIER` | Sí | El proveedor no existe, o está dado de baja |
| `DUPLICATE` | No crea | La orden ya se importó, o es una de Teikem que volvió de QuickBooks |
| `SKU_ALIAS`, `COST_ROUNDED`, `ZERO_QTY`, `MERGED_LINES`, `EXPECTED_IGNORED` | No | Avisos informativos |

**Una orden con un producto o proveedor que no existe no se importa**: se bloquea y se avisa por qué. «Descargar lo que falta» baja la lista de productos y proveedores para darlos de alta.

**Orden creada.**
- Nace en el estatus escogido. Con **Completada** (`SENT`), el alta y el paso a «Enviada» se hacen en **una sola operación**: si el paso falla, no queda un borrador a medias.
- Su **origen** es **«Excel / archivo»** (con plantilla o con el formato genérico) o **«QuickBooks»**.
- Sus notas dicen «Importada con la plantilla «{nombre}» {número}.» seguida de la marca de importación entre corchetes (`[IMP:…]` o `[QB:…]`).

**No duplica.** Cada orden importada guarda una **clave de importación** (proveedor + número de documento). Una clave ya existente entre las órdenes **activas** sale `DUPLICATE`, aunque alguien haya borrado la marca de las notas y aunque el documento haya entrado antes por otro formato (genérico o plantilla comparten la clave). Si dos personas importan el mismo archivo nuevo a la vez, **una crea y la otra recibe `DUPLICATE`** con «Esa orden ya se importó (otra importación la creó al mismo tiempo).». Si la orden importada se da de baja, el mismo archivo la vuelve a crear.

**«Ir y venir» (solo con el origen QuickBooks).** Si usted exportó una orden de Teikem a QuickBooks y después importa un archivo de QuickBooks que la trae, el documento **no se crea otra vez**: sale `DUPLICATE` con «La orden {n} nació en Teikem y ya se pasó a QuickBooks; no se vuelve a importar.». Se reconoce porque el `Num` o el «P. O. #» es el número de una orden de Teikem **del mismo proveedor, ya exportada o marcada**. Una factura de compra (*Bill*) nacida de una orden exportada solo se reconoce si trae «P. O. #».

**Con el origen QuickBooks,** el proveedor cuyo nombre es **exactamente** el del archivo (sin distinguir mayúsculas) queda como **ya mandado a QuickBooks**: no vuelve a salir como proveedor nuevo en el archivo IIF.

**Validaciones de la importación (campo → regla → mensaje → HTTP).**

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `file` | Obligatorio | `Elija el archivo a importar.` | 400 |
| `file` | Máximo 5.000 filas o 2 MB | `El archivo supera el límite de 5.000 filas o 2 MB.` | 400 |
| `file` | Debe traer órdenes | `El archivo no trae órdenes de compra.` | 400 |
| `file` | Hasta 2000 órdenes por archivo | `El archivo trae más de 2000 órdenes; divídalo e impórtelo por partes.` | 400 |
| `source` | `GENERIC`, `QUICKBOOKS` o `TEMPLATE` | `El origen de la importación no es válido: use GENERIC, QUICKBOOKS o TEMPLATE.` | 400 |
| `templatePublicId` | Obligatorio con `TEMPLATE` | `Elija la plantilla de importación.` | 400 |
| `templatePublicId` | Solo con `TEMPLATE` | `La plantilla solo se usa con el origen TEMPLATE.` | 400 |
| `templatePublicId` | Debe estar activa | `La plantilla está inactiva; reactívela o elija otra.` | 400 |
| `templatePublicId` | Existe, es de compras y de su compañía | `Plantilla de importación no encontrado.` | 404 |
| `status` | `DRAFT` o `SENT` | `El estatus de las órdenes importadas debe ser DRAFT (borrador) o SENT (enviada).` | 400 |

Avisos por orden (salen dentro del resultado, con HTTP 200):

| Aviso | Mensaje |
|---|---|
| `BAD_VALUE` (fecha) | `La fecha «{texto}» ({campo}) no es válida: use AAAA-MM-DD o MM/DD/AAAA.` |
| `BAD_VALUE` (número) | `«{texto}» ({campo}) no es un número: use punto decimal, sin letras.` |
| `BAD_VALUE` (sin número) | `La fila {línea} no trae número de documento (documentNumber); sin él no se sabe a qué orden va.` |
| `COST_CONFLICT` | `El producto {sku} viene en varias líneas de la orden con costos distintos ({costos}); corrija el archivo para que todas lleven el mismo costo.` |
| `DUPLICATE` | `Ya se importó antes (orden de compra {número}); no se vuelve a crear.` |
| `DUPLICATE` (ir y venir) | `La orden {n} nació en Teikem y ya se pasó a QuickBooks; no se vuelve a importar.` |
| `DUPLICATE` (a la vez) | `Esa orden ya se importó (otra importación la creó al mismo tiempo).` |
| `MISSING_PRODUCT` | `Falta el producto {sku}: dé de alta el producto con ese SKU y vuelva a importar.` (con «SKU parecidos en Teikem» si los hay) |
| `MISSING_SUPPLIER` | `Falta el proveedor «{proveedor}»: dé de alta el proveedor con ese nombre y vuelva a importar.` |

> **`COST_CONFLICT` cambió el Lote 239.** Antes, si un archivo traía el mismo producto con costos distintos, se sumaban y se tomaba el primer costo sin avisar. Ahora la orden **se bloquea** (también con QuickBooks y con el formato genérico), porque ese costo ahora viaja a QuickBooks. Corrija el archivo para que todas las líneas de ese producto lleven el mismo costo.

## 4. De dónde nació la orden (Origen)

Cada orden de compra tiene un **origen**. Se ve en la columna **«Origen»** de la lista, en la ficha y se filtra con **«Origen»**:

| Origen | Cuándo |
|---|---|
| **Teikem** | Creada a mano, por «Reordenar» un faltante o por cualquier otro camino. Una orden sin origen guardado se lee como Teikem |
| **Excel / archivo** | Importada con la plantilla o con el formato genérico |
| **QuickBooks** | Importada de un archivo de QuickBooks. **Nunca se exporta ni se marca** |

**Las órdenes que ya existían** al instalar este lote se clasificaron **una sola vez** por la marca de sus notas: `[QB:…]` → QuickBooks, `[IMP:…]` → Excel / archivo, el resto → Teikem. Las enviadas, recibidas en parte y recibidas de esas quedan **«Por pasar»** (ver la sección 10).

API: `GET /api/v1/purchase-orders?origin=EXCEL&origin=TEIKEM` (repetible o separado por comas; `TEIKEM` incluye las que no tienen origen guardado). Un valor distinto: 400 «El origen «{valor}» no es válido: use TEIKEM, EXCEL o QUICKBOOKS.».

## 5. Estatus frente a QuickBooks y sus transiciones

Cada orden está en uno de **cinco estados frente a QuickBooks**, que son independientes del estatus de la orden de compra (Borrador, Enviada, Recibida en parte, Recibida, Cancelada). Se ven con un chip en la lista y la ficha, y se filtran con **«QuickBooks»** (`GET /api/v1/purchase-orders?quickBooks=PENDING`, repetible o separado por comas).

| Estado (chip) | Código | Qué significa |
|---|---|---|
| **Por pasar** | `PENDING` | Se puede pasar y todavía no ha salido en ningún archivo ni está marcada |
| **Exportada, por confirmar** | `EXPORTED` | Salió en un archivo y falta decir si QuickBooks lo recibió |
| **En QuickBooks** | `IN_QUICKBOOKS` | Marcada como ya en QuickBooks (al confirmar un archivo o a mano) |
| **Vino de QuickBooks** | `FROM_QUICKBOOKS` | Se importó de QuickBooks |
| **No se pasa** | `NOT_EXPORTABLE` | Por su estatus no se pasa (Borrador o Cancelada) y nunca salió ni se marcó |

**Se pasan** las órdenes **Enviada, Recibida en parte y Recibida**, con las cantidades **pedidas**. **No se pasan** Borrador ni Cancelada.

**Prioridad cuando hay varias condiciones** (el estado es el primero que se cumpla): Vino de QuickBooks > En QuickBooks > Exportada, por confirmar > Por pasar > No se pasa. Por eso **una orden exportada y luego cancelada sigue «Exportada, por confirmar»**: se puede confirmar o mandar a «No pasó», pero ya no se puede exportar de nuevo.

**Transiciones.** Todas necesitan `purchasing.quickbooks`. Ninguna cambia el estatus de la orden ni dispara efectos sobre inventario, recibos o pagos; solo escriben quién y cuándo.

| De | A | Acción | Qué valida | Qué efecto tiene |
|---|---|---|---|---|
| Por pasar | Exportada, por confirmar | **Exportar** | Orden activa, que no vino de QuickBooks y en estatus Enviada, Recibida en parte o Recibida | Guarda la fecha del archivo y quién lo sacó (no marca) |
| Exportada, por confirmar | Exportada, por confirmar | **Exportar** otra vez | Igual que arriba; avisa «N de estas ya salieron…» | Reemplaza la fecha y quién del archivo anterior |
| Exportada, por confirmar | En QuickBooks | **«Sí, ya están»** (confirmar) | La orden salió en un archivo (en cualquier estatus, también cancelada) | Guarda quién y cuándo marcó; el proveedor y los productos de la orden quedan como ya mandados |
| Exportada, por confirmar | Por pasar | **«No pasó»** | No está marcada ni vino de QuickBooks | Borra la fecha y quién del archivo. Si la orden se canceló, queda «No se pasa» |
| Exportada, por confirmar | Exportada, por confirmar | **«Todavía no»** | — | Ninguno: no llama al API; la fecha se conserva |
| Por pasar | En QuickBooks | **Marcar** (a mano o «Marcar todas») | Orden elegible (ver Exportar) | Guarda quién y cuándo; proveedor y productos quedan como ya mandados |
| Exportada, por confirmar | En QuickBooks | **Marcar** | No vino de QuickBooks | Igual que confirmar |
| En QuickBooks | Exportada, por confirmar (o Por pasar si nunca salió) | **Quitar la marca** | No vino de QuickBooks | Borra quién y cuándo de la marca. No borra nada en QuickBooks ni «desmanda» proveedores y productos |

**Qué queda bloqueado en cada estado** (el mensaje exacto está en la sección 9):

| Estado | Exportar | «Sí, ya están» | «No pasó» | Marcar | Quitar la marca |
|---|---|---|---|---|---|
| **Por pasar** | Sí | 422 «no ha salido en ningún archivo» | Sin cambio | Sí | Sin cambio |
| **Exportada, por confirmar** | Sí (avisa); 422 si ya se canceló | Sí | Sí | Sí | Sin cambio |
| **En QuickBooks** | **409** «ya está en QuickBooks» | Sin cambio (no repite quién ni cuándo) | **409** «ya está marcada» | Sin cambio | Sí |
| **Vino de QuickBooks** | **422** | **422** | **422** | **422** | **422** |
| **No se pasa** (Borrador o Cancelada) | **422** por el estatus | **422** «no ha salido…» | Sin cambio | **422** por el estatus | Sin cambio |

«Sin cambio» no es un error: la respuesta cuenta la orden como «ya marcada» o «sin cambio» y no toca nada.

**Editar, enviar o cancelar la orden no cambia su estado de QuickBooks.** Una orden «En QuickBooks» o «Exportada» que se edita o se cancela **sigue igual**, y el cambio **no pasa solo a QuickBooks**. La ficha lo avisa: «Esta orden ya salió hacia QuickBooks: editarla o cancelarla aquí no cambia nada allá. Ciérrela o corríjala en QuickBooks.».

## 6. Pasar las órdenes a QuickBooks (pantalla)

**Quién puede.** `purchasing.quickbooks`, módulo `PURCHASING`.

### 6.1 La lista «por pasar»

Compras → **«QuickBooks»** abre la ventana **«Pasar órdenes de compra a QuickBooks»**: todas las órdenes **Enviada, Recibida en parte o Recibida** que están por pasar (no las marcadas ni las que vinieron de QuickBooks), de la más vieja a la más nueva.

- Cada fila: casilla, **Fecha**, **Número**, **Proveedor**, **Total**, **Nuevo en QuickBooks** (chips «Proveedor nuevo» y «N productos nuevos», con **«ver datos»**) y **Archivo** (si ya salió en un archivo: «Archivo del {fecha}» y «por {quién}»).
- Arriba: el buscador por número o proveedor, **«Todas las de la vista (N)»**, **«Marcar todas»** y **«Incluir proveedores y productos nuevos»** (encendido por omisión).
- **«ver datos»** muestra, de esa orden, el proveedor nuevo (nombre en QuickBooks, contacto, teléfono, correo y término de pago) y los productos nuevos (ítem en QuickBooks, descripción y costo).
- Si algunas ya salieron en un archivo, un aviso lo dice: «N de estas ya salieron en un archivo (la última vez el {fecha}). Si ya las importó, confírmelas o márquelas; si no, exportarlas otra vez las repite en QuickBooks.».
- **Avisos (no impiden exportar):** nombre de proveedor de más de 41 caracteres, SKU de más de 31, término del proveedor sin nombre en QuickBooks, diferencia de redondeo, moneda distinta a la de la compañía y caracteres que QuickBooks no admite. Los textos están en la sección 9.

### 6.2 Exportar y contestar «¿Ya las importaste en QuickBooks?»

1. Elija las órdenes (por omisión van todas las de la vista) y pulse **«Exportar N»**. Se baja un archivo `teikem-quickbooks-compras-AAAAMMDD-HHMM.iif`. **Aquí no se marca nada.**
2. La ventana cambia a **«¿Ya las importaste en QuickBooks? (N)»**, con el nombre del archivo, el número de órdenes y el total, cuántos proveedores y productos nuevos llevó, los avisos, y la lista de las órdenes del archivo con casillas.
3. Importe el archivo en QuickBooks (**Archivo → Utilidades → Importar → Archivos IIF**) y conteste. **Solo las marcadas cuentan; las desmarcadas no cambian:**
   - **«Sí, ya están (N)»:** quedan **En QuickBooks**.
   - **«No pasó (N)»:** QuickBooks no las recibió; vuelven a **Por pasar** y pierden la fecha del archivo.
   - **«Todavía no»:** cierra la pregunta sin cambiar nada. Siguen **Exportadas, por confirmar** (en la lista aparece «Archivo del {fecha}») y se puede contestar después.
   - **«Bajar otra vez»:** descarga el mismo archivo.
4. **«Marcar N como en QuickBooks»** (abajo, junto a «Cerrar»): las deja en QuickBooks **sin sacar archivo**, para las que ya están allá porque se capturaron a mano. **«Marcar todas»** hace lo mismo con todas las por pasar, con una confirmación («Se marcarán N órdenes como ya pasadas a QuickBooks, sin sacar archivo. Úselo para las órdenes históricas que ya están allá. ¿Continuar?»).

### 6.3 La ficha de la orden

Bajo el título de la orden, una franja muestra el **origen** y el **estado de QuickBooks**, y:
- **En QuickBooks:** «Marcada el {fecha} por {persona}» y el botón **«Quitar la marca»** (pide confirmación; la orden vuelve a «por pasar», o a «exportada» si salió en un archivo; no se borra nada en QuickBooks).
- **Exportada, por confirmar:** «Exportada el {fecha} por {persona}; falta confirmar que se importó» y los botones **«Sí, ya está»**, **«No pasó»** y **«Pasar a QuickBooks…»** (abre la ventana con esa orden).
- **Por pasar:** **«Pasar a QuickBooks…»** y **«Marcar como en QuickBooks»** (con confirmación, sin sacar archivo).
- En la lista, una orden «En QuickBooks» tiene en su fila la acción **«Quitar la marca»**.

## 7. Pasar las órdenes a QuickBooks (API)

Todas llevan `purchasing.quickbooks` y el módulo `PURCHASING`. Los cuerpos usan los `publicId` de las órdenes.

| Qué | Llamada | Cuerpo |
|---|---|---|
| Lo que falta por pasar | `GET /api/v1/purchase-orders/quickbooks` | — |
| Exportar | `POST /api/v1/purchase-orders/quickbooks/export` | `{ "purchaseOrderIds": ["…"], "all": false, "includeNew": true }` |
| «Sí, ya están» | `POST /api/v1/purchase-orders/quickbooks/confirm` | `{ "purchaseOrderIds": ["…"] }` |
| «No pasó» | `POST /api/v1/purchase-orders/quickbooks/not-passed` | `{ "purchaseOrderIds": ["…"] }` |
| Marcar a mano | `POST /api/v1/purchase-orders/quickbooks/mark` | `{ "purchaseOrderIds": ["…"] }` o `{ "all": true }` |
| Quitar la marca | `POST /api/v1/purchase-orders/quickbooks/unmark` | `{ "purchaseOrderIds": ["…"] }` |

- **Lo que falta por pasar** (`GET`) trae `count`, `total`, `alreadyExportedCount`, `lastExportedAtUtc`, las `orders`, los `newSuppliers`, los `newProducts`, los `warnings` y `purchaseOrderAccount`. No está paginado: «Exportar todas» las lleva todas.
- **Exportar:** hasta 1000 órdenes por llamada (para más, `"all": true`). `includeNew` (por omisión `true`) agrega al archivo los proveedores y productos nuevos. Responde `fileName`, `contentType`, `contentBase64` (el archivo en Windows-1252, en base64), `exportedAtUtc`, `orderCount`, `total`, `alreadyExportedCount`, `lastExportedAtUtc`, `includedNew`, `newSupplierCount`, `newProductCount`, `warnings` y `orders`. **Anota** la fecha del archivo y quién lo sacó en cada orden, pero **no la marca**.
- **Confirmar, «No pasó» y quitar la marca** exigen `purchaseOrderIds` (no admiten `all`). **Marcar** y **exportar** sí admiten `all`.
- **Resultados:** confirmar y marcar responden `marked`, `alreadyMarked`, `suppliersSent`, `productsSent` y `atUtc`. «No pasó» responde `reset`, `unchanged` y `atUtc`. Quitar la marca responde la misma forma que marcar: `marked` es cuántas **perdieron** la marca y `alreadyMarked` cuántas **no la tenían**.
- Cada llamada que escribe corre en una sola operación y **bloquea las órdenes** indicadas mientras trabaja: si dos personas exportan, marcan o confirman la misma orden a la vez, trabajan **una después de la otra** y la segunda ve el resultado de la primera (por ejemplo, 409 «ya está en QuickBooks» o el aviso «ya salieron»).

## 8. El archivo IIF de órdenes de compra

El archivo es de texto en **Windows-1252**, con tabuladores y fines de línea de Windows, y fechas `MM/DD/AAAA`. Se importa en QuickBooks Desktop en **Archivo → Utilidades → Importar → Archivos IIF**. Cada bloque sale **solo si trae algo**, en este orden:

1. **Productos nuevos (`!INVITEM`):** el mismo bloque que las facturas (nombre, tipo `INVENTORY`, descripción, cuentas de ventas, inventario y costo, precio, costo e impuesto). Solo con «Incluir proveedores y productos nuevos».
2. **Proveedores nuevos (`!VEND`):** nombre, empresa, contacto, teléfono (con prefijo), correo, término de pago (con el nombre que se le dio en Ajustes → Operación → QuickBooks) y nota. Solo con «Incluir proveedores y productos nuevos».
3. **Las órdenes (`!TRNS` / `!SPL` / `!ENDTRNS`),** una transacción por orden de compra:
   - **`TRNS` de tipo `PURCHORD`:** fecha de la orden, cuenta **«Purchase Orders»** (la de Ajustes), proveedor, **monto en negativo** (la suma de las líneas, ya redondeadas a centavos), `DOCNUM` = número de la orden en Teikem (`PO-#####`), `MEMO` = las notas **sin** la marca de importación, y `DUEDATE` = fecha esperada (vacía si no hay).
   - **Una `SPL` por línea:** cuenta de inventario, monto **en positivo**, `DOCNUM`, memo = nombre del producto, cantidad (hasta 4 decimales), costo (hasta 4 decimales) e `INVITEM` = SKU del producto.
   - Cada orden **cuadra a 0.00 por construcción**.
- **Qué no lleva:** moneda (QuickBooks usa la de la compañía), impuestos, envío ni descuentos (la orden de compra de Teikem no los guarda), ni descripción propia de cada línea. Los **kits** se compran como el producto que son (no se desglosan).
- **Limpieza de textos:** tabuladores y saltos de línea pasan a espacio; las comillas dobles, a simples; y en los **nombres** (proveedor, ítem) los dos puntos pasan a guion (en QuickBooks «A:B» sería un subproveedor o subítem). Los caracteres que Windows-1252 no tiene salen como «?» y se avisa.
- **Redondeo:** QuickBooks redondea cada línea a centavos; Teikem redondea al final. Si el total del archivo difiere del de Teikem, sale un aviso. El archivo manda a QuickBooks lo redondeado.

**Cuenta de órdenes de compra.** Está en **Ajustes → Operación → QuickBooks → «Cuenta de órdenes de compra»** (por omisión **«Purchase Orders»**, una cuenta no contable de QuickBooks). Un valor vacío vuelve a la de omisión. El máximo es de 159 caracteres. API: `GET` y `PATCH /api/v1/sales-orders/quickbooks/settings` con `purchaseOrderAccount` (leer: `sales.quickbooks.view` o `admin.tenant`; cambiar: `admin.tenant`; módulo Órdenes de venta). El cambio vale desde el siguiente archivo.

## 9. Validaciones y mensajes

### 9.1 Exportar, confirmar, «No pasó», marcar y quitar la marca

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `purchaseOrderIds` / `all` | Indicar órdenes o pedir todas (exportar y marcar) | `Indique las órdenes de compra (purchaseOrderIds) o pida todas (all = true).` | 400 |
| `purchaseOrderIds` | Hasta 1000 por llamada | `No se pueden marcar más de 1000 órdenes de compra en una sola llamada; use «todas».` | 400 |
| `purchaseOrderIds` | Todas deben existir, estar activas y ser de su compañía | `Alguna de las órdenes de compra indicadas no existe.` | 400 |
| exportar, marcar con `all` | Debe haber algo por pasar | `No hay órdenes de compra por pasar a QuickBooks.` | 422 |
| exportar, confirmar, «No pasó», marcar, quitar la marca | La orden no vino de QuickBooks | `La orden de compra {n} vino de QuickBooks: no se exporta ni se marca.` | 422 |
| exportar, marcar | La orden está Enviada, Recibida en parte o Recibida (marcar también acepta una ya exportada) | `La orden de compra {n} está en {estatus}: solo se pasan a QuickBooks las enviadas, recibidas en parte o recibidas.` | 422 |
| exportar | La orden no está ya marcada | `La orden de compra {n} ya está en QuickBooks; para volver a exportarla, quítele la marca.` | 409 |
| confirmar | La orden salió en algún archivo | `La orden de compra {n} no ha salido en ningún archivo de QuickBooks; para marcarla a mano use «Marcar como en QuickBooks».` | 422 |
| «No pasó» | La orden no está marcada | `La orden de compra {n} ya está marcada como en QuickBooks: quítele la marca en vez de usar «No pasó».` | 409 |
| cualquiera | Permiso `purchasing.quickbooks` | `Falta el permiso 'purchasing.quickbooks'.` | 403 |
| cualquiera | Módulo `PURCHASING` encendido | `El módulo 'PURCHASING' no está habilitado para esta compañía.` | 403 |

`{n}` es el número de la orden (por ejemplo `PO-00012`) y `{estatus}` su estatus en el idioma del usuario. Con varias órdenes, el mensaje es el de **la primera** que falla y **no se cambia ninguna**.

### 9.2 Avisos que no bloquean

Salen en la lista «por pasar» y en el resultado de exportar:

| Aviso | Texto |
|---|---|
| Nombre de proveedor largo | `El nombre del proveedor «{nombre}» pasa de 41 caracteres: QuickBooks no lo acepta como nombre de proveedor.` |
| SKU largo | `El SKU «{sku}» pasa de 31 caracteres: QuickBooks no lo acepta como nombre de ítem.` |
| Término sin nombre | `El término «{código}» no tiene nombre en QuickBooks (Ajustes → Operación → QuickBooks): el proveedor nuevo sale sin término.` |
| Redondeo | `La orden de compra {n} suma {Teikem} en Teikem y {archivo} en el archivo: QuickBooks redondea cada línea a centavos.` |
| Moneda distinta | `La orden de compra {n} está en {moneda} y la compañía en {monedaCompañía}: el archivo no lleva moneda y QuickBooks la toma en la de la compañía.` |
| Caracteres perdidos | `{dónde} tiene caracteres que QuickBooks no admite ({caracteres}); en el archivo salen como «?».` (con «dónde» = «El memo de la orden de compra {n}», «El proveedor «{nombre}»», «El SKU «{sku}»» o «La descripción del producto {sku}») |

### 9.3 Filtros de la lista

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `origin` | `TEIKEM`, `EXCEL` o `QUICKBOOKS` | `El origen «{valor}» no es válido: use TEIKEM, EXCEL o QUICKBOOKS.` | 400 |
| `quickBooks` | `PENDING`, `EXPORTED`, `IN_QUICKBOOKS`, `FROM_QUICKBOOKS` o `NOT_EXPORTABLE` | `El estado de QuickBooks «{valor}» no es válido: use PENDING, EXPORTED, IN_QUICKBOOKS, FROM_QUICKBOOKS o NOT_EXPORTABLE.` | 400 |

### 9.4 Ajustes

| Campo | Regla | Mensaje | HTTP |
|---|---|---|---|
| `purchaseOrderAccount` | Hasta 159 caracteres | `El nombre de una cuenta de QuickBooks no puede pasar de 159 caracteres.` | 400 |

Los mensajes de la plantilla y de la importación están en las secciones 2 y 3. Cada uno está explicado en la [FAQ](faq.md).

## 10. Antes de la primera exportación

1. **Pruebe el archivo en una copia de la compañía de QuickBooks** (la de Solutions) antes de usarlo en la real. Es lo único que falta para darlo por bueno. Si QuickBooks lo rechaza o lo asienta distinto, avise a soporte: lo primero que se ajusta son los signos, las cuentas y la fecha de entrega, y se ajustan en un solo lugar del código.
2. **Marque lo histórico.** Las órdenes que ya existían quedaron **«Por pasar»** si estaban Enviadas, Recibidas en parte o Recibidas. Las que ya están en QuickBooks se marcan con **«Marcar todas»** (o escogiendo y «Marcar N como en QuickBooks»), **sin sacar archivo**. Si no, la primera exportación las mandaría otra vez y quedarían duplicadas en QuickBooks.
3. **Apague «Incluir proveedores y productos nuevos»** hasta confirmar que los nombres coinciden con los de QuickBooks. Sin esa opción el archivo solo trae las órdenes, y QuickBooks las rechaza si el proveedor o el producto no existe allá.
4. **Proveedores que ya están en QuickBooks.** El sistema ya marcó como «ya mandados» a los proveedores que tienen órdenes importadas de QuickBooks o que salen en el historial de compras de QuickBooks (Análisis de ventas), para que el primer archivo no los mande como nuevos. Revise en **«ver datos»** quién sale como nuevo.
5. **Dé nombre en QuickBooks a los términos de pago** (Ajustes → Operación → QuickBooks); si no, el proveedor nuevo sale sin término y se avisa.

## 11. Preguntas frecuentes del módulo

**¿Exportar marca la orden como pasada?**
No. Solo anota la fecha del archivo y quién lo sacó. Queda «Exportada, por confirmar» hasta que usted conteste «Sí, ya están» o «No pasó».

**Contesté «Todavía no». ¿Qué pasó?**
Nada. Las órdenes siguen «Exportadas, por confirmar» y se pueden confirmar o mandar a «No pasó» cuando sepa. Si las exporta otra vez, el sistema avisa «N de estas ya salieron…».

**¿Qué diferencia hay entre «No pasó» y «Todavía no»?**
«No pasó» quiere decir que QuickBooks **no** las recibió: borra la fecha del archivo y las regresa a «Por pasar». «Todavía no» no cambia nada.

**Edité una orden que ya estaba en QuickBooks. ¿Se actualiza allá?**
No. El estado no cambia y el cambio no pasa solo a QuickBooks; ciérrela o corríjala allá. Si quiere volver a exportarla, quítele la marca y exporte (en QuickBooks quedaría una orden más).

**Cancelé una orden que ya estaba en QuickBooks.**
Sigue «En QuickBooks»; hay que cancelarla también allá. Si estaba «Exportada, por confirmar», puede confirmarla o mandarla a «No pasó», pero no exportarla otra vez.

**Reimporté de QuickBooks una orden que yo había mandado desde Teikem y salió «ya se pasó a QuickBooks; no se vuelve a importar».**
Es lo esperado («ir y venir»). La orden ya existe en Teikem. Solo se reconoce si el número o el «P. O. #» es el de la orden de Teikem, del mismo proveedor, y esa orden ya se exportó o se marcó.

**Dos personas exportaron la misma orden al mismo tiempo.**
El sistema las pone en fila. Las dos reciben su archivo, pero la segunda ve el aviso «ya salieron» (o un 409 si la primera ya la confirmó). Lo que importe en QuickBooks es responsabilidad de quien lo importe: importe **solo uno** de los dos archivos.

**¿Por qué no puedo exportar una orden que vino de QuickBooks?**
Porque ya está allá. Nunca se exporta ni se marca.

**Mi archivo dice «costos distintos» y antes importaba.**
Desde este lote, el mismo producto con costos distintos en una orden bloquea la orden (antes se tomaba el primero en silencio). Corrija el archivo.

**¿Dónde se ve el origen de una orden?**
En la columna «Origen» de la lista y en la franja de la ficha. Las notas de una orden importada traen además la marca entre corchetes (`[IMP:…]`); no la borre a propósito aunque ya no sea necesaria para no duplicar.

**No veo el botón «QuickBooks».**
Falta el permiso `purchasing.quickbooks` o el módulo Compras está apagado.

**No veo Sistema → Plantillas.**
Esa pantalla es del módulo Integraciones, que en una base nueva solo enciende el administrador de la plataforma. Mientras tanto, cree y edite la plantilla desde Compras → Importar.

## 12. Para soporte

- **Estado y datos de una orden:** `GET /api/v1/purchase-orders/{publicId}` trae `originCode`, `origin`, `quickBooksState`, `quickBooksExportedAtUtc`, `quickBooksExportedByName`, `quickBooksAtUtc` y `quickBooksByName`.
- **Qué falta y qué salió:** la lista con el filtro **«QuickBooks»** (`PENDING`, `EXPORTED`, `IN_QUICKBOOKS`). No hay un histórico aparte de archivos: la ficha dice quién y cuándo exportó o marcó cada orden.
- **«Pasé una orden por error»:** «Quitar la marca» (si estaba marcada) o «No pasó» (si estaba solo exportada). No se borra nada en QuickBooks.
- **Un proveedor sale como nuevo y ya existe en QuickBooks:** márquele una orden suya como «En QuickBooks» (el proveedor y los productos de la orden quedan como ya mandados) o exporte sin «Incluir proveedores y productos nuevos».
- **Una plantilla ya no sirve:** desactívela (no se borra) y cree otra. Las órdenes ya importadas con ella conservan su nombre en las notas.
- **El archivo no entró en QuickBooks:** guarde el archivo y avise a soporte con el mensaje de QuickBooks; recuerde que el formato **todavía no se ha probado contra un QuickBooks real**.
- Las acciones de QuickBooks quedan en la bitácora de la orden (quién y cuándo se exportó o marcó) y los permisos denegados en los eventos de seguridad.
