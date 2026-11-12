# Escena 1 - Productos e inventario: mapa para el video

Investigación de solo lectura (2026-11-12). Código: `web-app/src/features/warehouse/`. Datos: base local `Teikem`, `TenantId = 2` (Advance Solutions). Nada se ejecutó fuera de `SELECT`.

## 1. Qué es y dónde está (menú, ruta, permiso, módulo)

- **Menú:** grupo «Almacén» (`nav.groups.warehouse`) → «Productos e inventario» (`nav.products.title`). Ruta `/warehouse/products` (`web-app/src/app/routes.tsx:217-222`, orden 30 del grupo almacén).
- **Permiso y módulo de la ruta:** `products.view` + módulo `WMS_LOTSERIAL` (`routes.tsx:219-220`). Alta, edición, «Importar» y ajuste exigen `inventory.manage` (`ProductListScreen.tsx:356`) y `inventory.adjust` para «Añadir ajuste» (`ProductEditorModal.tsx:802`). La foto, `inventory.manage` (`ProductPhotoBlock.tsx:98`). El impuesto, `tax.manage` + módulo `TAX` (`ProductEditorModal.tsx:246-248`). WMS_LOTSERIAL está encendido en Solutions (según la lista de módulos del encargo).
- **Una sola pantalla** reúne catálogo y existencias. Pestañas (`ProductListScreen.tsx:444-454`): «Productos» (`warehouse.products.tabProducts`), «Categorías» (`tabCategories`), «Daños» (`warehouse.damage.tab`, solo con `warehouse.damage`) y «Valor del inventario» (`warehouse.valuation.tab`, solo con `inventory.value`).
- **Ficha de un producto:** un clic en la fila abre el modal «Editar producto» (`ProductEditorByIdModal`, `ProductListScreen.tsx:413,421`). Existe también `/warehouse/products/:publicId` (lotes y series), pero solo se llega desde el editor cuando el rastreo es Lote o Serie; los 42 productos de Solutions son «Ninguno», así que no aplica.
- Discrepancia a tener presente: el manual (`docs/manual/06-inventario-y-almacen.md:413`) dice `inventory.view`; la ruta real pide `products.view`. Manda el código.

## 2. Qué conviene mostrar, en orden

Pedido del dueño: ver inventario y balance, buscar, abrir y editar, añadir. Orden propuesto (60-90 s):

1. **Entrar a Almacén → Productos e inventario** y dejar que cargue. Señalar el río de indicadores de arriba: SKUs activos, Unidades totales, Bajo mínimo, No disponibles (Con número de serie sale en 0; se puede mencionar o ignorar).
2. **Leer la tabla como balance:** columnas Disponible, Reservado y Total (Disponible = Total − Reservado). Señalar un producto con reservado (070120 con 5 o 52800 con 30) y la miniatura de foto.
3. **Buscar:** filtro «SKU o nombre» (una búsqueda, p. ej. «PRODIGY MONITOR» o «070120»). Opcional, 3 s: filtro Marca.
4. **Abrir un producto** (clic en la fila → «Editar producto»): recorrer los bloques Foto, Identificación, Inventario, Precios (Costo de compra, Precio de venta, Impuesto) y, abajo, «Dónde está (existencia por posición)» (se abre al clic). Hacer **una edición pequeña y real** (p. ej. subir el «Punto de reorden» o corregir el precio) y «Guardar» → aviso «Producto actualizado.».
5. **Añadir producto nuevo:** «Nuevo producto» → SKU `101010`, nombre `CAJA CORRUGADA 10 X 10 X 10` → «Guardar» → «Producto creado.». Es el puente narrativo con la escena 2: esa caja viene en las compras de QuickBooks y, sin el producto, la importación de la orden de compra se bloquea (Lote 239, `docs/lote239-decisiones.md`).
6. (Opcional, si sobra tiempo) volver a la lista, buscar `101010` y mostrar que aparece con existencia 0 y estatus «Bajo mínimo» o «OK» según el mínimo; eso adelanta «ahora lo recibimos».

Kardex y Valor del inventario son otras pestañas/pantallas: no se piden en esta escena. La existencia por posición se ve dentro del editor (ver paso 11) y el Kardex tiene su propio menú («Kárdex de movimientos», `nav.kardex.title`).

## 3. Paso a paso exacto del recorrido

Playwright: todas las etiquetas salen de `es.json`; el idioma de la web debe estar en español (`test.use({ locale: 'es-PR' })`, como `e2e/lote240-fotos-de-productos.spec.ts:13`). Antes hay que iniciar sesión y elegir **Advance Solutions** en `/select-tenant` (el ayudante `login()` de `e2e/staffHelpers.ts:52-60` toma la compañía «Predeterminada», que puede no ser Solutions; ver dudas).

| # | Acción | Etiqueta (es) y clave | Cómo localizarlo en Playwright | Qué debe verse después |
|---|---|---|---|---|
| 1 | Abrir la pantalla | Menú «Almacén» (`nav.groups.warehouse`) → «Productos e inventario» (`nav.products.title`); o `page.goto('/warehouse/products')` | `getByRole('link', { name: 'Productos e inventario' })` (o `goto`) | Título «Productos e inventario» (`warehouse.products.title`), subtítulo «Lista con existencias disponibles, reservadas y rastreo por lote/serie»; pestaña «Productos» activa |
| 2 | Esperar los datos | Indicadores `warehouse.products.kpis.*` | `getByRole('group', { name: 'Resumen de la lista' })` (`kpis.aria`); esperar a que no haya «…» en los botones | Botones: «SKUs activos» 42, «Unidades totales» 69.900, «Bajo mínimo» 3, «No disponibles» 4, «Con número de serie» 0. Nombre accesible de cada botón: `SKUs activos: 42`, etc. (`ProductListScreen.tsx:96`) |
| 3 | Señalar el balance | Columnas «Disponible», «Reservado», «Total» (`columns.available/reserved/total`) | `getByRole('columnheader', { name: 'Disponible' })`; para resaltar, `locator('tr', { hasText: '070120' })` | Fila `070120`: Disponible 2.332, Reservado 5, Total 2.337. Reservado en 0 se pinta «—» (`:312`) |
| 4 | (Opcional) clic en un indicador | «Bajo mínimo» (`kpis.belowMin`) | `getByRole('button', { name: /Bajo mínimo/ })` (`aria-pressed`) | La tabla se reduce a 3 productos; otro clic lo quita. Se ve el filtro en la URL `?kpi=low` |
| 5 | Buscar | Filtro «SKU o nombre» (`warehouse.products.filters.sku`) | Es un desplegable con búsqueda (`ProductSearchSelect`), no un input suelto: abrirlo con `getByText('SKU o nombre')`/su botón y escribir en el cuadro de búsqueda; el e2e existente usa `getByPlaceholder(/SKU o nombre/i)` (`lote240...spec.ts:84`) | Se elige el producto de la lista; la tabla queda con 1 fila. **Verificar el selector exacto al grabar** (ver riesgos) |
| 6 | Limpiar filtro | «Limpiar» del bloque de filtros (`Filters onClear`) | `getByRole('button', { name: /Limpiar/i })` (texto exacto no verificado) | Vuelve a 25 filas, paginación visible |
| 7 | Abrir un producto | Clic en la fila (`onRowClick`) | `page.locator('tr', { hasText: '070120' }).click()` (evitar clic sobre la miniatura: ese clic la amplía) | Modal «Editar producto» (`editor.editTitle`), tamaño xl, con bloques Foto, Identificación, Inventario, Precios, Medidas y peso, Campos personalizados |
| 8 | Mirar Identificación | «SKU» (solo lectura), «Nombre», «Unidad», «Categoría», «Marca», «Modelo» (`editor.*`) | `getByLabel('Nombre')` | SKU en gris (deshabilitado), Unidad «Unidad», Categoría «MONITORS», Marca «PRODIGY» |
| 9 | Mirar Inventario | «Rastreo», «Dueño del inventario», «Punto de reorden» (`editor.reorder`), «Total» (solo lectura) | `getByLabel('Punto de reorden')` | Con movimientos, Unidad/Rastreo/Dueño quedan bloqueados con el aviso `editor.lockedByMovements`: «Unidad, rastreo y dueño no se pueden cambiar: el producto ya tiene movimientos.» |
| 10 | Mirar Precios | «Costo de compra», «Precio de venta», «Impuesto» (`editor.purchaseCost/salePrice/taxCategory`) | `getByLabel('Precio de venta')` | 070120: costo 5,25, venta 7,99, Impuesto «Exento» |
| 11 | Ver existencia por posición | «Dónde está (existencia por posición)» (`editor.where`) | `getByText('Dónde está (existencia por posición)')` (es un `<details>`, clic para abrir) | Tabla con columnas Posición, Lote, En mano, Reservado, Disponible, Total: una fila `PISO`, 2.337 / 5 / 2.332 (el almacén único de Solutions tiene una sola posición con existencia) |
| 12 | Editar | Cambiar «Punto de reorden» (p. ej. 800 → 900) o el precio | `getByLabel('Punto de reorden').fill('900')` | Campo con el nuevo valor |
| 13 | Guardar | «Guardar» (`editor.save`) | `getByRole('button', { name: 'Guardar' })` | Aviso «Producto actualizado.» (`editor.updated`); el modal se cierra y la lista se refresca |
| 14 | Nuevo producto | «Nuevo producto» (`warehouse.products.new`) | `getByRole('button', { name: 'Nuevo producto' })` | Modal «Nuevo producto» (`editor.newTitle`); Foto dice «Guarde el producto para ponerle foto.»; Unidad ya viene «Unidad», Rastreo «Ninguno» e Impuesto «Gravable» por omisión; no sale la pregunta de kits (ajuste de la compañía apagado) |
| 15 | Escribir el SKU | «SKU» (`editor.sku`), placeholder `SKU-000`, obligatorio | `getByPlaceholder('SKU-000').fill('101010')` | Campo con `101010` |
| 16 | Nombre | «Nombre» (`editor.name`), obligatorio | `getByLabel('Nombre').fill('CAJA CORRUGADA 10 X 10 X 10')` | |
| 17 | Categoría, marca | «Categoría» (`editor.category`) = «BOXES»; «Marca» (`editor.brand`) = «DEPOT» (placeholder «Escriba o elija una marca») | Categoría es un combo (`role="combobox"`): `getByRole('combobox', { name: 'Categoría' })`, escribir «BOXES» y Enter; marca `getByLabel('Marca').fill('DEPOT')` | Valores puestos; marca DEPOT aparece en las sugerencias |
| 18 | Punto de reorden, costo y precio | «Punto de reorden», «Costo de compra», «Precio de venta» | `getByLabel(...)`.fill | Costo 0,42 (el de QuickBooks); precio de venta a decidir (propuesta 1,20) |
| 19 | Guardar | «Guardar» | igual que 13 | Aviso «Producto creado.» (`warehouse.products.created`); modal cerrado |
| 20 | Comprobar en la lista | Filtro «SKU o nombre» | repetir 5 con `101010` | Fila `101010` con Disponible 0, Reservado «—», Total 0, Rastreo «Ninguno». El estatus será «Bajo mínimo» si se le dio mínimo > 0 (regla: activo, hay mínimo y disponible < mínimo, `Teikem.Domain/Wms/ProductRules.cs:247`); con mínimo vacío sale «OK» |

Validaciones del alta (`ProductEditorModal.tsx:276-325`, textos en `warehouse.products.errors.*`): SKU obligatorio («El SKU es obligatorio.»), sin espacios ni caracteres de control («El SKU no admite espacios ni caracteres de control.»), máximo 60 («El SKU no puede exceder 60 caracteres.»); nombre obligatorio («El nombre del producto es obligatorio.»), máximo 200. SKU repetido: 409 «Ya existe un producto con ese SKU para ese dueño.» (`docs/manual/06-...md:509`). Los demás campos son opcionales (el recuadro «Unidad de las medidas» y «Unidad del peso» llevan marca de obligatorio pero traen valor por omisión sugerido por región).

## 4. Requisitos de datos y de configuración para que funcione con Advance Solutions

| Requisito | Estado HOY (base local, TenantId = 2) | Si falta |
|---|---|---|
| Columna `Tenant.ProductPhotosEnabled` (db-update del Lote 240) | **Existe** (`COL_LENGTH('dbo.Tenant','ProductPhotosEnabled')` = 1). Valor 1 en Depot y en Solutions → columna de miniatura y bloque «Foto» visibles | Si en otra base falta: `Diseño/logistica-db-update.sql:7857-7861`, aplicado por `db-update` |
| Productos activos | 42 de 42 (`SELECT COUNT(*), SUM(CAST(IsActive AS INT))`) | — |
| Marcas | AMERICAN RED CROSS 9, MEDPRIDE 9, PRODIGY 7, GLOBAL 6, PADIMONT 4, DEPOT 2, ABELLE 1, YARDLEY 1, sin marca 3 (KIT-001 y dos «Underpads») | — |
| Existencia | 38 productos con existencia > 0; 69.900 unidades en total; 38 filas de `StockBalance`, todas en la posición `PISO` del único almacén («Almacen Solutions»). Reservado total 35 (070120: 5; 52800: 30). Sin existencia: 00607-3, 00802-2, 00804-6, KIT-001 | — |
| Fotos (`Product.ImageFileId`) | 34 de 42 con foto, y los 34 apuntan a un archivo en `StoredFile` (PNG de 6 KB a 1 MB). Sin foto: 00607-3, 00802-2, 00804-6, 121212, 53310, 53350, 979 y KIT-001. Las cajas (979 y 121212) no tienen foto | Las fotos están cifradas en el almacén de archivos del API (`StorageKey` `2/2026/11/....bin`, `IsEncrypted = 1`): comprobar al grabar que el API local las sirve. Origen: `F:/Download/FotosSolutions` (30 PNG) y `scripts/` de fotos de Solutions |
| Mínimos | 41 de 42 con mínimo (`MinQty > 0`). Bajo mínimo hoy: 3 (00607-3 con 70, 00802-2 con 180, 00804-6 con 60, todos con 0 disponibles) | — |
| Tipo de rastreo | Los 42 «Ninguno» (`NONE`). Cero con lote o serie | Si se quiere enseñar Lote/Serie hay que crear un producto aparte: no hace falta para el video |
| Unidad | Los 42 en `UN` («Unidad») | — |
| Precio de venta y costo | Precio en los 42; costo > 0 en 40 (051885 tiene costo 0; KIT-001 sin costo). Rango de venta 0,95 a 28,99 | — |
| Impuesto | 11 «Exento» (los Prodigy de glucosa, p. ej. 070120, 52800) y 31 sin categoría, que la pantalla trata como «Gravable» (valor por omisión del editor, `ProductEditorModal.tsx:354`). Módulo TAX encendido, así que el campo «Impuesto» se ve (editable solo con `tax.manage`) | Si el usuario del video no tiene `tax.manage`, el campo sale gris con «Solo lo cambia quien administra los impuestos.» |
| Categorías | 8 raíz, todas activas: PERSONAL CARE, BEAUTY, MONITORS, BOXES, DIABETES, PHARMACY, SUPPORTERS, GLOVES. 40 productos con categoría | — |
| Dueño | Ningún producto con dueño cliente (todos «Propio»): la columna Dueño sale con «Propio» en todo; es ruido visual | — |
| Kits armados | `Tenant.UsesAssembledKits = 0` en Solutions: el alta no pregunta por kits (mejor para el video). Hay un `KIT-001` (kit de oferta, `IsOfferKit`) que sale con la etiqueta «Kit» | — |
| Caja 101010 | **No existe** en Teikem (`WHERE Sku IN ('101010')` = 0). Sí existen 979 (9x7x9, venta 0,95, costo 0,43, mín. 4) y 121212 (12x12x12, venta 1,46, costo 0,49, mín. 7), ambas en categoría BOXES, marca DEPOT. En las compras de QuickBooks (`F:/Download/Solutions Purchases.CSV`) aparece como `101010 (CAJA CORRUGADA 10 X 10 X 10)`, 200 unidades a 0,42 | Se da de alta en esta escena (paso 14-19) |
| Almacén con posición | 1 almacén, posición `PISO`; sin ella la sección «Dónde está» saldría «Sin existencia en ninguna posición.» | — |
| Usuario con permisos | No se consultó (no se copian datos de personas). Necesita `products.view`, `inventory.manage` (alta/edición) y, para ver el impuesto editable, `tax.manage` | Probar con el administrador de Solutions antes de grabar |

## 5. Riesgos al grabar

- **El alta no se puede repetir:** un producto con movimientos o dado de alta no se borra (solo se desactiva), y el SKU `101010` quedaría ocupado en una segunda toma («Ya existe un producto con ese SKU para ese dueño.», 409). Para repetir la toma hay que restaurar la base o usar otro SKU de ensayo (p. ej. `101010-T`) y dejar el real solo para la toma buena.
- **Orden entre escenas:** si se graba la escena 2 (importar compras) antes de la 1, la importación se bloquea por `101010` inexistente (Lote 239). Grabar 1 antes de 2, o dar de alta la caja de antemano en la toma de ensayo y reservar el alta visible para el video final.
- **Miniatura diminuta:** `.pl-rowphoto` mide 24 × 24 px (`warehouse.css:561`); en un video 1080p casi no se aprecia. Si se quiere mostrar la foto, hacer clic en ella (amplía con `aria-label` «Ampliar la foto de {nombre}», `es.json:9`) o abrir el editor, donde el bloque «Foto» es grande. El clic en la fila no debe caer sobre la miniatura.
- **Fotos cifradas:** si el API local no tiene la `Files:MasterKey` correcta o falta el disco de archivos, la columna sale con cuadros punteados vacíos (`pl-rowphoto-none`) y el bloque Foto con «Sin foto». Mirar la lista antes de grabar.
- **8 productos sin foto** (incluidas las cajas): evitar mostrarlos arriba si se quiere un aspecto cuidado; la lista se ordena por SKU y los primeros (`00052-1`, `00607-3`…) mezclan con y sin foto. Se puede ordenar por «Disponible» (clic en el encabezado) para que arriben los de más existencia.
- **Orden de columnas solo de la página:** el encabezado ordena únicamente las 25 filas visibles (`ProductListScreen.tsx:270` comentario; la regla de `exportRows` aplica al exportar). Con 42 productos hay dos páginas; poner «Mostrar 50» si el selector de tamaño existe, o aceptar la paginación.
- **Selector de búsqueda:** «SKU o nombre» es un desplegable con búsqueda de selección múltiple (`ProductSearchSelect`), no una caja de texto. El e2e existente asume `getByPlaceholder(/SKU o nombre/i)`. Grabar un ensayo para fijar el selector y los tiempos de espera.
- **Indicadores en «…»:** los cinco KPI hacen llamadas separadas y muestran «…» mientras cargan; esperar a que desaparezcan antes de narrar las cifras.
- **Número formateado:** en español la web usa punto de millares (69.900, 2.337); decirlo así en la narración.
- **Bloqueos al editar:** en 070120 Unidad, Rastreo y Dueño están deshabilitados por movimientos, y «Producto activo» no se puede apagar con existencia. Es correcto, pero se verá gris: conviene narrarlo en una frase.
- **«Añadir ajuste»** cambia el inventario de verdad (Kárdex). No usarlo en esta escena.
- **Datos sensibles:** el nombre de la compañía aparece en el encabezado y no hay datos personales en esta pantalla. El modal no muestra clientes (Dueño «Propio»). Cuidado con la barra de direcciones (puerto/host local).
- **Idioma/tema:** grabar en español y en tema claro (la web tiene tema claro y oscuro con selector).
- **Brevo/correo:** esta escena no envía correo; el API de grabación debe arrancar igual con `scripts/api-para-pruebas.ps1` (regla del dueño).

## 6. Datos para la narración (hechos verificados)

- «Aquí está todo el inventario de Advance Solutions: 42 productos activos y casi 70 mil unidades en el almacén.» (42 y 69.900)
- «Cada producto muestra tres cifras: Disponible, Reservado y Total. Lo disponible es lo que queda después de apartar lo reservado para pedidos.»
- «El Prodigy Monitor tiene 2.337 unidades en total, 5 reservadas, y 2.332 disponibles.»
- «El indicador Bajo mínimo avisa cuando un producto tiene menos unidades disponibles que su punto de reorden. Hoy hay 3 productos en esa situación.»
- «Cada fila trae su foto; con un clic se amplía.»
- «Para buscar, escribo el SKU o parte del nombre en SKU o nombre.»
- «Al abrir un producto veo su identificación, su categoría y marca, el punto de reorden, el costo de compra, el precio de venta y el impuesto.»
- «Abajo, Dónde está muestra en qué posición del almacén está la existencia.»
- «Al guardar, el sistema confirma: Producto actualizado.»
- «Para añadir uno nuevo, el SKU y el nombre son obligatorios; lo demás se puede completar ahora o después.»
- «Una caja corrugada de 10 por 10 por 10, que llega en la compra de QuickBooks: la damos de alta ahora para poder recibirla después.»
- «La foto de un producto nuevo se pone después de guardarlo.»

## 7. Duración estimada en pantalla (segundos) y qué se puede recortar

| Tramo | Segundos |
|---|---|
| Entrar y lista con indicadores y balance (pasos 1-3) | 15 |
| Buscar un producto (5-6) | 8 |
| Abrir y recorrer la ficha, incluido «Dónde está» (7-11) | 20 |
| Editar y guardar (12-13) | 10 |
| Nuevo producto, llenar y guardar (14-19) | 25 |
| Comprobar en la lista (20) | 5 |
| **Total** | **83** |

Recortes si hay que acortar a ~60 s: quitar el clic en el indicador (paso 4), la apertura de «Dónde está» (paso 11) y la comprobación final (paso 20); escribir menos campos en el alta (solo SKU, nombre, categoría, costo y precio).

## 8. Dudas y decisiones para Luis

1. **¿Ejemplo para añadir?** Propuesta: `101010`, «CAJA CORRUGADA 10 X 10 X 10», categoría BOXES, marca DEPOT, costo 0,42 (el de QuickBooks), precio de venta 1,20 (entre 0,95 de la 9x7x9 y 1,46 de la 12x12x12), punto de reorden 5, unidad «Unidad», rastreo «Ninguno», impuesto el que traiga por omisión. Falta que confirme el precio de venta y el punto de reorden.
2. **¿Qué producto abrir y editar?** Propuesta: `070120` PRODIGY MONITOR 1EA (tiene foto, reservado de 5, existencia 2.337 y es el que sirve para hablar de disponible vs reservado). Edición inofensiva sugerida: subir el punto de reorden de 800 a 900 (sigue «OK»). Alternativa: `MPR-10103`, producto sin reservado ni exento, más "limpio".
3. **¿Compañía y usuario del video?** El ayudante `login()` de los e2e toma la compañía «Predeterminada»; debe quedar claro que se graba con Advance Solutions (TenantId 2) y con un usuario que tenga `inventory.manage` y `tax.manage`.
4. **¿Grabar la escena 1 antes de la 2 sobre la misma base?** Sí, y sobre una copia que se pueda restaurar si hay que repetir (el alta es irreversible; ver riesgos).
5. **Miniaturas de 24 px:** ¿aceptar que el video muestre las fotos pequeñas y ampliar solo una, o conviene mostrar la foto grande desde el editor?
6. **Texto exacto no verificado:** el botón de limpiar filtros y el selector final de «SKU o nombre»; salen del componente `Filters`/`ProductSearchSelect` que no se releyó. Se fijan en un ensayo.
7. **Discrepancia manual/código:** el manual dice `inventory.view`, la ruta pide `products.view`. No afecta al video (el administrador tiene ambos), pero conviene corregir el manual aparte.
8. **Valor del inventario / Kardex:** no se incluyeron. Si el dueño quiere un plano de 5 s del Kardex de `101010` después de recibirlo, se mostraría en la escena 3.

## 9. Fuentes (archivo:línea)

- `web-app/src/app/routes.tsx:214-228` (ruta, permiso, módulo, ficha por id).
- `web-app/src/features/warehouse/ProductListScreen.tsx:1-16` (descripción), `:56-68` (estado de fila), `:110-183` (KPI), `:186-247` (filtros y paginación PAGE_SIZE = 25), `:252-333` (columnas), `:344-421` (cabecera, filtros, tabla, modales), `:427-459` (pestañas).
- `web-app/src/features/warehouse/ProductEditorModal.tsx:100-196` (modales alta/edición), `:267-334` (validaciones), `:336-366` (valores por omisión), `:440-500` (guardado del alta), `:596-819` (bloques y botones), `:836-925` (existencia por posición y ajuste).
- `web-app/src/features/warehouse/ProductPhotoBlock.tsx:1-60,90-110`, `ProductRowPhoto.tsx:1-10`, `StoredImage.tsx:5-22`; `warehouse.css:561-562`.
- `web-app/src/kernel/i18n/es.json` (claves citadas en las tablas; `nav.products.title`, `warehouse.products.*`, `tax.categories`, `zoomPhoto` en línea 9).
- `web-app/e2e/lote240-fotos-de-productos.spec.ts:13-14,81-110`; `web-app/e2e/staffHelpers.ts:39-60`.
- `src/Teikem.Domain/Wms/ProductRules.cs:243-248` (disponible y bajo mínimo); `src/Teikem.Domain/Tenancy/Tenant.cs:126`; `Diseño/logistica-db-update.sql:7857-7861`.
- `docs/manual/06-inventario-y-almacen.md:409-433,509,775-806`; `docs/lote240-decisiones.md`; `docs/lote239-decisiones.md`.
- Consultas de solo lectura sobre la base local `Teikem` con `TenantId = 2` (`Product`, `StockBalance`, `StoredFile`, `ProductCategory`, `LookupCode`, `Warehouse`, `WarehouseBin`, `InventoryTransaction`, `Tenant`) y `F:/Download/Solutions Purchases.CSV` (línea de la caja 101010).
