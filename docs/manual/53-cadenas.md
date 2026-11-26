# 53 — Cadenas de clientes (agrupar clientes, alta masiva por nombre, filtros y análisis por cadena)

<!-- kemi:staff -->

Lote 245 (2026-11-25). Una **cadena** agrupa a los clientes que son del mismo grupo comercial (Walgreens, CVS…). Sirve para filtrar clientes y órdenes por cadena, para medir la venta de cada cadena y para darle precios y ofertas propios (Lote 247: ver el [capítulo 55](55-precios-especiales-y-ofertas.md); gana siempre el precio más bajo entre el del cliente, el de su cadena, la oferta y la lista general, y todos los precios son antes del IVU).

> **Una cadena no es una ruta de venta.** No cambia la ruta del cliente ni a quién se le cuenta su venta. Cambiar la cadena de un cliente no toca su ruta, su representante ni su vendedor.

> **Un cliente pertenece a una sola cadena, o a ninguna.** Cambiarlo de cadena es **moverlo**.

Capturas de las pantallas: [L245](frontend/l245-cadenas.md). Cada mensaje de error está explicado en la [FAQ](faq.md). Este capítulo continúa el [51 — Análisis de ventas](51-analisis-de-ventas.md) y el [52 — Rutas de venta](52-rutas-de-venta.md).

## 1. Qué es, dónde está y quién puede

- **Dónde:**
  - Menú **Listas → Cadenas** (`/catalog/chains`): la lista y, al elegir una cadena, su ficha en un panel a la derecha (`?id=` en la dirección).
  - **Listas → Clientes y contratos:** el filtro **«Cadena»** de la lista, la etiqueta de la cadena en cada fila y el panel **«Cadena»** de la ficha del cliente.
  - **Contabilidad → Ventas (Órdenes de venta):** la columna y el filtro **«Cadena»**.
  - **Análisis → Análisis de ventas:** la pestaña **«Por cadena»**.
- **Módulo:** **Órdenes de venta (`SALES_ORDERS`)**. Con el módulo apagado, la pantalla Cadenas, sus endpoints y la pestaña «Por cadena» responden 403 con `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` El filtro y la etiqueta de cadena en Clientes solo salen con el módulo encendido.
- **Permisos:**

| Qué | Permiso | Módulo |
|---|---|---|
| Ver la lista de cadenas, su ficha y sus clientes (y que funcionen los filtros de Clientes y de Órdenes) | `sales.chain` **o** `clients.read` **o** `sales.view` | `SALES_ORDERS` |
| Crear, editar, desactivar y reactivar cadenas; asignar y quitar clientes; cambiar la cadena de un cliente desde su ficha; alta masiva por nombre (simular y aplicar) | `sales.chain` | `SALES_ORDERS` |
| Ver la pestaña «Por cadena», la sección «Venta» de la ficha de la cadena | `sales.analysis.view` o `sales.analysis.manage` | `SALES_ORDERS` |
| Ver la fuente de datos «Cadenas de clientes» (`CLIENT_CHAIN`) en Vistas, indicadores y gráficos | `clients.read` | `SALES_ORDERS` |

- `sales.chain` está en la categoría de permisos de Facturación (`BILLING`) y trae `clients.read` con él (quien administra cadenas ve los clientes).
- Los roles que lo reciben: **administrador de la compañía** y **supervisor de ventas**. Los roles **Sales** y **oficina de ventas** no lo reciben; el administrador puede dárselo en Roles.
- Sin permiso, el API responde 403 y queda un evento `PERMISSION_DENIED` en los eventos de seguridad.
- El `TenantId` sale de la sesión: cada compañía ve solo lo suyo. Una cadena o un cliente de otra compañía responde 404, sin decir si existe.
- **No hay estatus ni flujo de transiciones:** la cadena está **activa** o **inactiva** (sección 8).
- **Ni el portal ni la app del cliente muestran la cadena.**

## 2. Lista y ficha de la cadena

**Qué hace.** La lista muestra las cadenas con **Nombre**, **Clientes** (solo los **activos**), **Notas** y **Activa**. Tiene buscador por nombre y el interruptor **«Ver inactivas»**. La ficha se abre en el panel de la derecha y trae, en paneles independientes: **Datos**, **Clientes de la cadena**, **Agregar por nombre** y **Venta**.

**Cómo se usa (pantalla).** Listas → Cadenas. Elija una cadena de la lista. Con `sales.chain` aparece **«+ Nueva cadena»**; sin él la ficha es de solo lectura.

**API.**

| Qué | Llamada | Permiso |
|---|---|---|
| Lista | `GET /api/v1/client-chains?includeInactive=false&q=walg` | Ver |
| Ficha | `GET /api/v1/client-chains/{publicId}` | Ver |
| Clientes de la cadena | `GET /api/v1/client-chains/{publicId}/clients?q=&includeInactive=false&skip=0&take=50&all=false` | Ver |

- La lista trae `publicId`, `name`, `notes`, `isActive` y `clientCount` (clientes activos).
- La ficha trae además `inactiveClientCount`, `createdAtUtc`, `updatedAtUtc` y `rowVersion`. La pantalla resume: «N clientes activos · N inactivos · N en total».
- Clientes de la cadena: por nombre; `take` de 1 a 500 (por omisión 50); `all=true` los trae todos (es lo que usa «Exportar»). Cada fila trae `code`, `name`, `status`, `statusLabel`, `isActive` y `salesRepName`. Por omisión no incluye a los clientes inactivos.

## 3. Crear y editar una cadena

**Qué hace.** Crea una cadena con nombre y notas, o cambia esos datos. **«Guardar»** está apagado mientras no haya cambios.

**Quién puede.** `sales.chain`, módulo `SALES_ORDERS`.

**Cómo se usa.** «+ Nueva cadena» (ventana con Nombre y Notas) o, en la ficha, el panel **Datos**.

**API.**

| Qué | Llamada | Cuerpo |
|---|---|---|
| Crear | `POST /api/v1/client-chains` | `{ "name": "Walgreens", "notes": "Farmacias de la cadena" }` |
| Editar | `PATCH /api/v1/client-chains/{publicId}` | `{ "name": "Walgreens PR", "notes": "…", "clearNotes": false, "rowVersion": "…" }` (todo opcional) |

- En el `PATCH`, un campo que no se manda **no cambia**. `clearNotes: true` borra las notas. `rowVersion` (el de la ficha) hace que un cambio hecho por otra persona dé 409 en vez de pisarse.
- Cada cambio de nombre, notas o activa deja un registro en la bitácora con el tipo `CLIENT_CHAIN`.

**Campos y reglas.**

| Campo | Regla |
|---|---|
| `name` | Obligatorio al crear; se recorta; hasta 100 caracteres. **Único entre las cadenas activas** de la compañía: no distingue mayúsculas, acentos, puntos ni comas («Walgreens.» y «WALGREENS» son el mismo nombre). Una cadena inactiva puede repetir el nombre de una activa |
| `notes` | Opcional; se recorta; hasta 500 caracteres; vacío = sin notas |
| `rowVersion` | Opcional; si no coincide con el actual, 409 |

## 4. Asignar y quitar clientes

**Qué hace.** Agrega clientes a la cadena o los saca de ella. Un cliente que **ya está en otra cadena no se mueve solo**: se salta y se avisa, salvo que usted pida **moverlo**.

**Quién puede.** `sales.chain`, módulo `SALES_ORDERS`.

**Cómo se usa (pantalla).** Ficha de la cadena → **Clientes de la cadena**:
1. **Agregar clientes:** escoja clientes uno por uno en «Elegir clientes…» (se acumulan) y pulse **«Agregar N»**. Si algunos ya están en otra cadena, la pantalla pregunta **«N clientes ya están en otra cadena: ¿moverlos a esta?»**; **«Moverlos»** repite la llamada con `moveFromOtherChains`.
2. **Quitar:** marque casillas en la tabla y pulse **«Quitar N»** (pide confirmar). Los clientes quedan **sin cadena**.
3. **«Ver inactivos»** muestra también a los clientes inactivos; **«Exportar»** baja toda la consulta.

**API.**

| Qué | Llamada | Cuerpo |
|---|---|---|
| Agregar | `POST /api/v1/client-chains/{publicId}/clients` | `{ "clientPublicIds": ["…"], "moveFromOtherChains": false }` |
| Quitar | `POST /api/v1/client-chains/{publicId}/clients/remove` | `{ "clientPublicIds": ["…"] }` |

La respuesta trae los conteos `assigned`, `moved`, `alreadyInChain`, `removed`, `notInChain` y `skippedOtherChain` (lista con el cliente y la cadena en la que está).

- Repetir la misma llamada es seguro: los que ya están cuentan en `alreadyInChain`; al quitar, los que no están en esa cadena cuentan en `notInChain`.
- Hasta **2000** clientes por llamada. Si alguno no existe (o es de otra compañía), la llamada completa falla con 404 y no se cambia nada.
- **Agregar** exige que la cadena esté **activa**; **quitar** no.
- La baja lógica de un cliente **conserva** su cadena; los cambios de estatus del cliente (prospecto, activo, suspendido) tampoco la tocan.
- Cada cliente cambiado deja su registro de bitácora `CLIENT` con `ChainId`.

## 5. Cadena del cliente (ficha del cliente)

**Qué hace.** En la ficha del cliente (Listas → Clientes y contratos → el cliente → panel **«Cadena»**) se ve la cadena actual (o la etiqueta **«Sin cadena»**; «Inactiva» si la cadena fue desactivada). Con `sales.chain` se **cambia** (incluso de una cadena a otra, en un solo paso) o se **quita**. El texto de ayuda dice: «Un cliente pertenece a una sola cadena. Cambiarla no cambia su ruta de venta.»

**Quién puede.** Ver: `sales.chain`, `clients.read` o `sales.view`. Cambiar: `sales.chain`. Módulo `SALES_ORDERS`.

**API.** `PUT /api/v1/client-chains/by-client/{clientPublicId}` con `{ "chainPublicId": "…" }`; con `{ "chainPublicId": null }` quita la cadena. Responde `clientPublicId`, `chainPublicId`, `chainName` y `chainIsActive`.

- La cadena **no** se cambia en «Editar perfil» del cliente (`PATCH /clients/{id}/profile`): se hace aquí con `sales.chain`, porque la cadena va a decidir precios.
- Si el cliente ya está en esa cadena, o ya está sin cadena, no pasa nada.
- La cadena destino debe estar activa.
- El modal **«+ Nuevo cliente»** no pide cadena: se asigna después.
- `GET /clients/{id}` trae `chainPublicId`, `chainName` y `chainIsActive`.

## 6. Alta masiva por nombre («Agregar por nombre»)

**Qué hace.** Agrega **de una vez** todos los clientes cuyo nombre empieza con un texto (por ejemplo «Walgreens»: «Walgreens 1», «Walgreens #2», «Walgreens-3»). Siempre se **simula primero**: la simulación no escribe nada y devuelve una **huella**; **aplicar** exige esa huella.

**Quién puede.** `sales.chain`, módulo `SALES_ORDERS`.

**Reglas.**
- El texto se compara **sin mayúsculas, acentos, puntos ni comas** («Farmacia Caño» y «FARMACIA CANO» son lo mismo; «Farmacia Caño» no toma «FARMACIA CAÑÓN» con palabra completa).
- Mínimo **3 letras o dígitos**; hasta 100 caracteres.
- **«Palabra completa»** (encendida por omisión): lo que sigue al texto no puede ser una letra ni un dígito. «Walgreens» toma «WALGREENS #1», «walgreens 2» y «Walgreens-3», pero **no** «Walgreensboro» ni «Walgreens1». Apagada, toma también esos dos. Si el texto termina en un carácter que no es letra ni dígito (por ejemplo «Walg-»), se respeta tal cual. Un espacio al final del texto se ignora.
- Solo entran clientes **activos**, en cualquier estatus.
- Cada cliente recibe una acción:

| Acción | Significa | Qué pasa al aplicar |
|---|---|---|
| **Asignar** (`ASSIGN`) | No tiene cadena | Queda en esta cadena |
| **Mover** (`MOVE`) | Está en otra cadena y se encendió «Mover también los que están en otra cadena» | Se mueve a esta cadena |
| **Ya está** (`ALREADY`) | Ya está en esta cadena | Nada |
| **Se salta (otra cadena)** (`SKIP_OTHER_CHAIN`) | Está en otra cadena y no se pidió moverlo | Nada: se informa |
| **Excluido** (`EXCLUDED`) | Usted lo desmarcó | Nada |

- Se aplican **hasta 2000 clientes**. La simulación avisa cuando hay más (`tooMany`) y muestra las primeras 2000 filas.
- **Excluir no obliga a volver a simular:** desmarque la casilla y la fila pasa a «Excluido».
- **La huella cambia** si cambia el texto, «Palabra completa», «Mover…», la cadena destino, los clientes que coinciden o la cadena de alguno de ellos. Si cambia entre la simulación y aplicar, **no se escribe nada** y hay que volver a simular.
- Es **idempotente**: simular y aplicar otra vez deja 0 asignados y 0 movidos.

**Cómo se usa (pantalla).** Ficha de la cadena → **Agregar por nombre**:
1. Escriba el texto en **«El nombre empieza con»**.
2. Ajuste **«Palabra completa»** y **«Mover también los que están en otra cadena»**.
3. Pulse **«Simular»**. Aparece el resumen («N coincidencias · N por asignar · N por mover · N ya están · N en otra cadena (se saltan) · N excluidos») y la tabla con **Incluir**, **Código**, **Cliente**, **Cadena actual** y **Acción**.
4. Desmarque a los clientes que no quiera.
5. Pulse **«Aplicar a N clientes»** y confirme. Si cambia el texto o un interruptor, la simulación deja de valer: **vuelva a simular** (el botón «Aplicar» se apaga). Si el servidor responde 409 por la huella, la pantalla simula de nuevo conservando sus exclusiones y avisa «Se simuló de nuevo con los clientes de ahora; revise y aplique otra vez.».

**API.**

| Qué | Llamada | Cuerpo |
|---|---|---|
| Simular | `POST /api/v1/client-chains/{publicId}/bulk-assign/preview` | `{ "prefix": "Walgreens", "wholeWord": true, "moveFromOtherChains": false, "excludeClientPublicIds": ["…"] }` |
| Aplicar | `POST /api/v1/client-chains/{publicId}/bulk-assign/apply` | el mismo cuerpo más `"fingerprint": "…"` (la huella de la simulación) |

- La simulación responde `prefix` (el texto ya normalizado), `matched`, `toAssign`, `toMove`, `alreadyInChain`, `skippedOtherChain`, `excluded`, `tooMany`, `maxBulk`, `fingerprint` y `rows` (con `action`, `currentChainName` por cliente).
- Aplicar responde `assigned`, `moved`, `alreadyInChain`, `skippedOtherChain` y `excluded`.
- La cadena destino debe estar **activa** (en simular y en aplicar).

## 7. Filtro y columna «Cadena» en Clientes y en Órdenes de venta

**Clientes** (`GET /api/v1/clients`, módulo `CATALOG`; la pantalla lo muestra con `SALES_ORDERS` encendido):
- `chainPublicId=…`: solo los clientes de esa cadena. Si la cadena no existe o es de otra compañía, la lista sale **vacía** (sin 404).
- `noChain=true`: solo los clientes **«Sin cadena»**.
- Los dos a la vez: 400. En pantalla, el select **«Cadena»** ofrece «Todas las cadenas», «Sin cadena» y cada cadena activa.
- Cada cliente de la lista trae `chainPublicId` y `chainName` (la lista muestra la cadena como etiqueta junto al estatus).

**Órdenes de venta** (`GET /api/v1/sales-orders?chainPublicId=…&chainPublicId=…`, módulo `SALES_ORDERS`):
- El filtro **«Cadena»** acepta varias; vale cualquiera de ellas. Si ninguna es de la compañía, el resultado es vacío.
- La columna **«Cadena»** (`chainName`) muestra «—» si el cliente no tiene cadena.
- La cadena que se ve es la **actual del cliente**, calculada al momento: la orden **no guarda copia**. Si usted mueve un cliente de cadena, **sus órdenes viejas pasan a verse con la cadena nueva**.

## 8. Activa e inactiva (no hay estatus)

La cadena no tiene estatus: solo está **activa** o **inactiva**. Nunca se borra.

| De → a | Quién puede | Qué valida | Efectos | Qué queda bloqueado |
|---|---|---|---|---|
| Activa → Inactiva (**«Desactivar»**, `POST …/deactivate`) | `sales.chain` | **Cero clientes activos** en la cadena (409 con el conteo, en singular o plural). Los clientes inactivos no cuentan y conservan su cadena. La cuenta se hace dentro de la misma transacción: si alguien asignó un cliente a la vez, uno de los dos recibe 409 «El registro fue modificado por otro usuario; recargue e intente de nuevo.» | La cadena sale de las listas de elegir en los clientes y de «Agregar por nombre»; deja de aparecer en la lista salvo con «Ver inactivas». Queda en la bitácora | Agregar clientes (400), asignar desde la ficha del cliente (400), alta masiva (400). Siguen permitidos: ver, editar nombre y notas, quitar clientes, reactivar |
| Inactiva → Activa (**«Reactivar»**, `POST …/reactivate`) | `sales.chain` | Que **otra cadena activa** no tenga ya ese nombre (409) | Vuelve a poder recibir clientes | — |

- Pedir el estado que la cadena ya tiene no cambia nada y no da error.
- Una cadena **inactiva con venta** en el mes sigue saliendo en «Por cadena».
- Una cadena inactiva cuenta como «sin cadena» para los precios: sus clientes dejan de recibir los precios y las ofertas de la cadena (capítulo 55).
- Desactivar una cadena también da 409 si tiene **listas de precios** (incluidos sus precios especiales) publicadas y no vencidas u **ofertas «Incluir»** publicadas y no vencidas con ella: «La cadena tiene precios especiales u ofertas publicados y vigentes ({N} listas de precios y {M} ofertas): quítela de ellos antes de desactivarla.». Primero se revisan los clientes activos. Las ofertas «Excluir» no bloquean. La ficha de la cadena trae ahora los paneles «Precios especiales» y «Ofertas»: ver el [capítulo 55](55-precios-especiales-y-ofertas.md).

## 9. Análisis de ventas → «Por cadena»

**Qué hace.** Dice cuánto vendió **cada cadena** en un mes contra otro mes, con el desglose por cliente. Es la pestaña **«Por cadena»** de Análisis → Análisis de ventas (al lado de «Por ruta» y «Por representante»).

**Quién puede.** `sales.analysis.view` o `sales.analysis.manage`, módulo `SALES_ORDERS`.

**Qué se ve.**
- **Mes** y **Comparar con** (por omisión el último mes completo contra el anterior).
- Tres tarjetas: venta del mes, cambio contra el mes de comparación y cuántas cadenas hay (con lo que vendió «Sin cadena»).
- **Comparativo por cadena:** una fila por cadena con su venta en los dos meses, **Cambio $**, **Cambio %** y **Participación**; el triángulo de cada fila despliega a sus clientes; **«Desplegar todas» / «Plegar todas»**. La fila **«Sin cadena»** agrupa a los clientes sin cadena y a las ventas del historial que no se emparejaron con ningún cliente (por nombre).
- **Conciliación:** *Suma de las cadenas + «Sin cadena» = Venta neta del mes*, con **Diferencia** (debe dar $0.00).
- Un gráfico **Venta por cadena**.

**Reglas.**
- La venta se atribuye a la **cadena actual** del cliente, también la del historial importado de QuickBooks: **al mover un cliente de cadena, su historial se reagrupa** con la cadena nueva.
- **Venta = producto + descuentos.** El envío y otros cargos no cuentan. Las notas de crédito restan. Se respeta la fecha de corte del [capítulo 51](51-analisis-de-ventas.md).
- Se suma sin redondear y se redondea a 2 decimales (hacia fuera del cero) al final.
- Salen todas las cadenas activas (aunque no vendan) y las inactivas que vendieron en alguno de los dos meses.

**API.**

| Qué | Llamada |
|---|---|
| Vista | `GET /api/v1/sales-analysis/by-chain?month=2026-10&compareMonth=2026-09&chainPublicId=…` (todo opcional; con `chainPublicId` trae solo esa cadena, sin «Sin cadena», y la conciliación sigue siendo la del mes completo) |
| Serie de una cadena | `GET /api/v1/sales-analysis/by-chain/series?chainPublicId=…&fromMonth=2025-11&toMonth=2026-10` (`chainPublicId` obligatorio; por omisión, los 12 meses que terminan en el último mes completo; hasta 36) |

La serie trae la venta neta por mes y los **5 clientes principales** con su participación. La ficha de la cadena la usa en su sección **Venta** («Venta neta por mes» y «Clientes de la cadena en el mes»).

**Vistas, indicadores y gráficos.** Se pueden agrupar y filtrar por cadena con el campo **«Cadena»** (`ChainName`) de las fuentes **Clientes**, **Líneas del análisis de ventas** (con «Sin cadena» cuando no hay) y **Órdenes de venta** (la cadena actual del cliente, en vivo). Además existe la fuente **«Cadenas de clientes»** (`CLIENT_CHAIN`) con nombre, notas, activa, clientes activos e inactivos y fecha de creación; en Vistas, las demás fuentes que traen al cliente pueden usar la relación «Cadena».

## 10. Validaciones

Los mensajes salen del servidor tal cual. En las respuestas de error 400 el mensaje viaja en el título y en `errors.<campo>`.

| Dónde | Campo | Regla | Mensaje de error exacto | HTTP |
|---|---|---|---|---|
| Crear / editar | `name` | Obligatorio al crear; con espacios solos cuenta como vacío | `Escriba el nombre de la cadena.` | 400 |
| Crear / editar | `name` | Hasta 100 caracteres | `El nombre no puede pasar de 100 caracteres.` | 400 |
| Crear / editar | `notes` | Hasta 500 caracteres | `Las notas no pueden pasar de 500 caracteres.` | 400 |
| Crear / editar / reactivar | `name` | Único entre las activas (sin mayúsculas, acentos, puntos ni comas) | `Ya hay una cadena activa con ese nombre.` | 409 |
| Todas las que reciben cuerpo | cuerpo | No puede faltar | `El cuerpo de la solicitud es obligatorio.` | 400 |
| Ficha, editar, desactivar, reactivar, clientes, alta masiva, análisis | `publicId` / `chainPublicId` | Debe ser una cadena de la compañía | `Cadena no encontrada.` | 404 |
| Agregar / quitar / cambiar cadena de un cliente | `clientPublicIds` / cliente | Todos deben existir en la compañía | `Cliente no encontrado.` | 404 |
| Editar / desactivar / reactivar / asignar / quitar / alta masiva | — | Otra persona cambió la cadena o sus clientes a la vez | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Desactivar | — | Con 1 cliente activo | `La cadena tiene 1 cliente activo: quítelo de la cadena antes de desactivarla.` | 409 |
| Desactivar | — | Con N clientes activos | `La cadena tiene N clientes activos: quítelos de la cadena antes de desactivarla.` (N es el número) | 409 |
| Desactivar | — | Sin clientes activos, pero con listas de precios (incluidos sus precios especiales) publicadas y vigentes u ofertas «Incluir» publicadas y vigentes (se revisa **después** de los clientes) | `La cadena tiene precios especiales u ofertas publicados y vigentes ({N} listas de precios y {M} ofertas): quítela de ellos antes de desactivarla.` (N y M son los números) | 409 |
| Agregar clientes / cambiar la cadena de un cliente / alta masiva | cadena destino | Debe estar activa | `La cadena está desactivada: actívela o escoja otra.` | 400 |
| Precios especiales, listas de precios y ofertas de la cadena | cadena | Debe estar activa para escribir | `La cadena está desactivada: actívela o escoja otra.` | 422 (los mensajes del capítulo 55) |
| Agregar / quitar | `clientPublicIds` | Al menos uno | `Indique los clientes (clientPublicIds).` | 400 |
| Agregar / quitar | `clientPublicIds` | Hasta 2000 por llamada | `No se pueden mandar más de 2000 clientes en una sola llamada.` | 400 |
| Alta masiva | `prefix` | Al menos 3 letras o dígitos | `Escriba al menos 3 letras del inicio del nombre.` | 400 |
| Alta masiva | `prefix` | Hasta 100 caracteres | `El prefijo no puede pasar de 100 caracteres.` | 400 |
| Alta masiva (simular y aplicar) | `excludeClientPublicIds` | Cada excluido debe estar entre los clientes que encontró la simulación | `Un cliente excluido no está entre los que encontró la simulación.` | 400 |
| Alta masiva (aplicar) | `fingerprint` | Obligatoria | `Falta la huella de la simulación (fingerprint): simule antes de aplicar.` | 400 |
| Alta masiva (aplicar) | `fingerprint` | Debe ser la de los clientes de ahora | `Los clientes que coinciden cambiaron desde la simulación: vuelva a simular antes de aplicar.` | 409 |
| Alta masiva (aplicar) | `prefix` | A lo sumo 2000 coincidencias | `La simulación encontró N clientes y el máximo por aplicación es 2000: use un prefijo más largo.` (N es el número) | 400 |
| `GET /clients` | `chainPublicId`, `noChain` | No las dos a la vez | `Escoja una cadena o «Sin cadena», no las dos.` | 400 |
| `GET /sales-analysis/by-chain` | `month`, `compareMonth` | Formato `AAAA-MM` | `El mes debe tener la forma AAAA-MM (por ejemplo 2026-10).` | 400 |
| `GET /sales-analysis/by-chain/series` | `chainPublicId` | Obligatorio | `Indique la cadena (chainPublicId).` | 400 |
| `GET /sales-analysis/by-chain/series` | `fromMonth`, `toMonth` | `toMonth` no antes de `fromMonth` | `El mes final no puede ser anterior al inicial.` | 400 |
| `GET /sales-analysis/by-chain/series` | rango | Hasta 36 meses | `El rango no puede pasar de 36 meses.` | 400 |
| Cualquier endpoint | módulo | `SALES_ORDERS` encendido | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 |
| Cualquier endpoint | permiso | Ver el permiso de cada sección | respuesta 403 (sin texto propio; queda `PERMISSION_DENIED`) | 403 |

**Avisos de la pantalla** (se validan antes de llamar al servidor, sin HTTP): `El nombre de la cadena es obligatorio.`, `El nombre admite hasta 100 caracteres.` y `Las notas admiten hasta 500 caracteres.` Y en «Agregar por nombre»: `Hay N coincidencias y el máximo es 2000: use un texto más específico.`, `Cambió el texto o un interruptor: vuelva a simular para poder aplicar.` y `Ningún cliente empieza con ese texto.`

## 11. Casos frecuentes

**«Walgreens 1, 2 y 3» y «Walgreensboro».** Escriba «Walgreens» en «Agregar por nombre» con «Palabra completa» encendida y simule: entran «Walgreens 1», «Walgreens 2» y «Walgreens #3»; «Walgreensboro» no aparece. Si de verdad quería «Walgreensboro», apague «Palabra completa».

**La ruta «Cadenas» no es una cadena.** En Solutions existe una **ruta de venta** llamada «Cadenas» (capítulo [52](52-rutas-de-venta.md)). Es una ruta como las demás y este lote **no la toca ni crea cadenas solo**. Para agrupar clientes por cadena hay que crear las cadenas aquí y asignarles clientes.

**La simulación cambió: vuelva a simular.** Entre «Simular» y «Aplicar» alguien renombró un cliente, lo dio de baja, lo pasó a otra cadena o creó uno nuevo que coincide. Vuelva a simular, revise y aplique.

**Excluir antes de aplicar.** Desmarque la casilla del cliente: pasa a «Excluido» y no se toca. No hace falta simular de nuevo.

**No deja desactivar.** La cadena todavía tiene clientes **activos** (quítelos o muévalos a otra cadena; los inactivos no estorban) o tiene **precios especiales, listas de precios u ofertas «Incluir» publicados y vigentes** (el mensaje dice cuántos: quítelos en la ficha de la cadena o en Contabilidad → Precios y ofertas). Se revisan primero los clientes.

**¿Qué precio recibe un cliente de la cadena?** El más bajo entre el suyo propio, el de su cadena, la oferta que le aplica y la lista general, siempre **antes del IVU** (capítulo 55 §2). Si lo cambia de cadena, recibe los precios de la cadena nueva desde ese momento; si la cadena se desactiva, cuenta como «sin cadena».

**Moví un cliente y su historial cambió de cadena.** Es lo esperado: la venta, también la importada de QuickBooks, se cuenta en la cadena **actual** del cliente. Las órdenes viejas también se ven con la cadena nueva.

**Un cliente no entró a la cadena.** O ya estaba en otra cadena (sale «Se salta (otra cadena)»; encienda «Mover…» si quiere cambiarlo), o está inactivo (el alta masiva solo toma clientes activos; agréguelo a mano).

**No veo «Cadenas» en el menú.** Falta el permiso (`sales.chain` o `clients.read`) o el módulo Órdenes de venta está apagado.

**La cadena no cuadra en «Por cadena».** Mire la **Diferencia** de la conciliación: debe ser $0.00. Las ventas del historial sin emparejar caen en «Sin cadena»; empareje los nombres en Análisis de ventas → Configuración.
