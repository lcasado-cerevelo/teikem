# 52 — Rutas de venta (personas de la ruta, vendedor preferido, vendedor de la orden, cuota por ruta y análisis por ruta)

<!-- kemi:staff -->

Lote 244 (2026-11-25). Una **ruta de venta** agrupa a los clientes y a las personas que los atienden: un **titular** y, si hace falta, **apoyos**. Con esto el análisis de ventas dice cuánto vendió **cada ruta** (y cada persona dentro de ella) contra su cuota, y cada orden de venta guarda **quién la vendió**.

> **No es la ruta de entrega ni el viaje.** La ruta de venta no mueve mercancía: solo decide de quién es una venta. Las rutas de entrega y los viajes están en los capítulos de Transporte.

> **«Cadenas» es una ruta más.** No tiene reglas especiales: se crea, se le ponen personas y se mide como cualquier otra.

Este capítulo continúa el [51 — Análisis de ventas](51-analisis-de-ventas.md) (que sigue valiendo: «Por representante» se conserva). Capturas de las pantallas: [L244](frontend/l244-rutas-de-venta.md). Cada mensaje de error está explicado en la [FAQ](faq.md).

## 1. Qué es, dónde está y quién puede

- **Dónde:** menú **Análisis → Análisis de ventas** (`/analytics/sales`). La pestaña **«Por ruta»** es la que abre por omisión; al lado están **Por representante**, **Movimiento de productos**, **Gráficos** y **Configuración**. En Configuración: **Rutas** (con el botón **«Personas»** de cada ruta y **«Pasar rutas desde representantes»**), **Representantes**, **Cuotas**, **Clientes sin representante** (arriba, **«Clientes sin ruta de venta»**) y **Ajustes** («La cuota se mide por»). La ficha del cliente (Listas → Clientes y contratos) tiene el panel **Venta**; la orden de venta tiene el campo **Vendedor**.
- **Módulo:** **Órdenes de venta (`SALES_ORDERS`)**. Con el módulo apagado todo responde 403 con `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.`
- **Permisos.** No hay permisos nuevos:

| Qué | Permiso | Módulo |
|---|---|---|
| Ver rutas, sus personas, el análisis por ruta, la venta de un cliente y las cuotas de una ruta | `sales.analysis.view` o `sales.analysis.manage` | `SALES_ORDERS` |
| Agregar, cerrar y anular personas de una ruta; escoger la ruta y el preferido de un cliente (uno a uno o en bloque); «Pasar rutas desde representantes»; cuotas por ruta; «La cuota se mide por»; crear y cambiar representantes; buscar personas para ponerlas en una ruta | `sales.analysis.manage` | `SALES_ORDERS` |
| Ver el campo «Vendedor» de un borrador y las opciones que ofrece | `sales.create` | `SALES_ORDERS` |
| Escoger el vendedor en el borrador | Los mismos permisos con los que se crea o edita el borrador (`sales.create`) | `SALES_ORDERS` |
| Los filtros «Ruta de venta» y «Vendedor» de la lista de órdenes | Solo salen a quien tiene `sales.analysis.view` o `manage` (las listas son del análisis) | `SALES_ORDERS` |
| Ver la fila «Ruta de venta sin titular» de «Necesita tu atención» | `sales.analysis.manage` | `SALES_ORDERS` |

- Sin permiso, el API responde 403 y queda un evento `PERMISSION_DENIED` en los eventos de seguridad.
- El `TenantId` sale de la sesión: cada compañía ve solo lo suyo.
- Las rutas y las personas de la ruta también están como **fuentes de datos** en Vistas, indicadores y gráficos: **«Rutas de venta»** y **«Personas de la ruta (titular y apoyo)»** (se ven con `sales.analysis.view`).

## 2. Rutas

Ya existían desde el Lote 241 ([51 §2.1](51-analisis-de-ventas.md#21-rutas)): nombre único entre las activas, orden, desactivar y reactivar. Lo nuevo:

- La lista (Configuración → Rutas) trae **Titular** (o la etiqueta «Sin titular»; «Titular inactivo» si ya no es persona activa), **Apoyos**, **Clientes** (activos con esa ruta propia o, si no tienen, la de su representante) y **Cuota vigente** del mes en curso.
- **Desactivar** una ruta falla si tiene representantes activos o clientes activos con esa ruta **propia** (409). Si pasa, **cierra hoy** a sus personas vigentes. **Reactivar no las reabre**: hay que volver a ponerlas.
- API: `GET /api/v1/sales-analysis/routes`, `POST …/routes/{id}/deactivate`, `…/reactivate`, `GET …/routes/{id}/quotas` (cuotas propias de la ruta, mes por mes).

## 3. Personas de la ruta (titular y apoyo)

**Qué hace.** Dice quién atiende la ruta y desde cuándo. Una persona es siempre un **usuario de Teikem** activo en la compañía.

- **Titular:** uno solo vigente por ruta. Es a quien se le cuenta la venta de la ruta que no tenga otro vendedor.
- **Apoyo:** los que hagan falta. Pueden vender (y ser vendedor de una orden) igual que el titular.
- **Vigencia:** de la fecha «Desde» hasta la fecha «Hasta» **sin incluirla** (el día de cierre ya no cuenta). Sin fecha de cierre = **vigente**. No se programan fechas a futuro.

**Cómo se usa (pantalla).** Configuración → Rutas → botón **«Personas»** de la ruta → ventana **«Personas de {ruta}»**:

1. **Vigentes** lista a cada persona con su papel, «Desde», «Hasta» y estado (Vigente, Cerrada, Anulada; «Persona inactiva» si el usuario ya no está activo).
2. **«Agregar persona»**: escoja la **Persona** (buscador por nombre o correo, hasta 50 resultados), el **Papel** (Titular o Apoyo) y **Desde** (por omisión hoy). Si agrega un titular, **el anterior se cierra** solo con el inicio del nuevo; si esa persona era apoyo, esa fila también se cierra.
3. **«Cerrar»** (la persona deja la ruta): pide la **Fecha de cierre** (por omisión hoy). El historial se conserva.
4. **«Anular»**: para un registro hecho **por error**. Deja de contar en el historial y en la atribución de la venta. Para que alguien deje la ruta, se cierra, no se anula.
5. **«Mostrar el historial»** trae las cerradas; **«Mostrar las anuladas»** (solo con `manage`) también las anuladas.

**API.**

| Qué | Llamada | Cuerpo |
|---|---|---|
| Ver personas | `GET /api/v1/sales-analysis/routes/{id}/people?history=false&includeVoided=false` | — |
| Agregar | `POST /api/v1/sales-analysis/routes/{id}/people` | `{ "userId": 12, "role": "PRIMARY", "validFrom": "2026-11-01" }` (`role` = `PRIMARY` o `SUPPORT`; `validFrom` opcional) |
| Cerrar | `POST …/routes/{id}/people/{assignmentId}/close` | `{ "validTo": "2026-11-24", "rowVersion": "…" }` (todo opcional) |
| Anular | `POST …/routes/{id}/people/{assignmentId}/void` | — |
| Buscar personas | `GET /api/v1/sales-analysis/sellers/candidates?q=ana&take=20` | — (`take` de 1 a 50) |

Cada escritura responde con las personas de la ruta ya actualizadas (con historial).

**Campos y reglas.**

| Campo | Regla |
|---|---|
| `role` | `PRIMARY` o `SUPPORT` (sin distinguir mayúsculas) |
| `userId` | Obligatorio; miembro **activo** de esta compañía (usuario activo y membresía activa) |
| `validFrom` | Por omisión hoy; no futura; para un titular nuevo, no anterior al inicio del titular actual |
| `validTo` | Por omisión hoy; no anterior al inicio; no futura |
| La ruta | Debe existir y estar activa para agregar personas |

## 4. Representantes: el usuario es obligatorio y el titular automático

Un **representante** ahora siempre lleva un **usuario de Teikem**: el vendedor es siempre un usuario.

- **Crear:** el usuario es obligatorio. Un usuario solo puede ser el de **un** representante activo.
- **Cambiar:** el usuario no se puede quitar. Un representante heredado **sin usuario** se puede renombrar o cambiar de ruta sin tocar el usuario; se le pone uno cuando se pueda (la lista tiene el filtro **«Representantes sin usuario»** y un enlace **«Ir a Usuarios para crear el usuario»** con `users.view`).
- **Titular automático.** Si, al crear el representante, ponerle usuario, cambiarle la ruta o reactivarlo, su ruta está **activa y sin titular**, su usuario queda de **titular desde hoy**. La respuesta trae `becamePrimary = true` y la pantalla avisa «{nombre} quedó como titular de su ruta.». Si la ruta ya tiene titular, no se cambia nada: el administrador decide.
- API: `GET /api/v1/sales-analysis/reps?includeInactive=false&withoutUser=false`, `POST …/reps` con `{ "name": "Ana", "routeId": 3, "userId": 12 }`, `PATCH …/reps/{id}`, `…/deactivate`, `…/reactivate`.

## 5. «Pasar rutas desde representantes»

**Qué hace.** Es la misma regla de la migración que corrió una sola vez con el lote, pero a pedido y **con simulación**:

1. Cada **cliente sin ruta** recibe la de su representante (si el representante está activo y su ruta está activa).
2. En cada **ruta activa sin titular**: si tiene **exactamente un** representante activo con usuario (persona activa), ese usuario queda de **titular**. Si tiene **varios**, todos quedan de **apoyo** y la ruta **sigue sin titular**: el administrador escoge.
3. Lista los **representantes activos sin usuario** (no se pueden pasar) y las rutas con varios representantes.

Las asignaciones nuevas empiezan el **primer día del mes más antiguo** con historial, facturas o cuotas de la compañía (sin datos, hoy), nunca en el futuro y nunca antes del cierre de una asignación anterior de la misma ruta o persona. Correrla otra vez no cambia nada más.

**Quién puede.** `sales.analysis.manage`, módulo `SALES_ORDERS`.

**Cómo se usa.** Configuración → Rutas → **«Pasar rutas desde representantes»** → **«Simular»** (muestra «Clientes con ruta propia: N», «Titulares nuevos: N», «Apoyos nuevos: N», «Representantes sin usuario (no se pueden pasar)» y «Rutas con varios representantes»; dice «Simulación: no se guardó nada.») → **«Aplicar»** (pide confirmar: «¿Aplicar los cambios?»). API: `POST /api/v1/sales-analysis/routes/from-reps?dryRun=true` (por omisión simula; con `dryRun=false` guarda). Responde `{ "dryRun", "clientsRouted", "primariesSet", "supportsSet", "validFrom", "repsWithoutUser": [...], "severalReps": [...] }`.

## 6. Ficha del cliente: panel «Venta» (ruta y vendedor preferido)

**Qué hace.** En la ficha (Listas → Clientes y contratos → el cliente → **Venta**) se ve y se cambia:

- **Ruta de venta:** la **propia** del cliente (etiqueta «Ruta propia») o, si no tiene, la **heredada del representante** («Heredada del representante {rep}»). Sin ninguna de las dos: «Sin ruta de venta». «Quitar la ruta propia» vuelve a heredar la del representante.
- **Equipo de la ruta:** el **Titular** y los **Apoyos** vigentes.
- **Vendedor preferido:** el que se propone primero en las órdenes del cliente.
- **Vendedor propuesto:** el que saldría hoy en una orden nueva y de dónde sale («preferido del cliente», «titular de la ruta» o «apoyo de la ruta»).
- **Representante:** el heredado, solo lectura (se cambia en Configuración).

**Quién puede.** Ver: `sales.analysis.view` o `manage`. Cambiar: `sales.analysis.manage`. Módulo `SALES_ORDERS`.

**Reglas del vendedor preferido.**

- Debe ser una **persona activa de la ruta del cliente**: titular o apoyo **vigente**, con usuario activo en la compañía.
- Un cliente **sin ruta** (propia o heredada) **no admite** vendedor preferido.
- Se valida **al asignarlo**. Si después esa persona deja la ruta (se cierra o se anula) o se desactiva, **no se bloquea nada**: la ficha muestra la etiqueta **«El vendedor preferido ya no pertenece a la ruta del cliente.»** con la ayuda «No se bloquea nada: elija otro vendedor o déjelo así.».
- Mandar el mismo preferido que ya tiene no se vuelve a validar. **«Quitar el vendedor preferido»** siempre se puede.
- Si en la misma operación se cambia la ruta y el preferido, el preferido se valida contra la ruta **nueva**.

**API.**

| Qué | Llamada | Cuerpo |
|---|---|---|
| Ver | `GET /api/v1/sales-analysis/clients/{clientId}/sales` | — |
| Cambiar | `PUT /api/v1/sales-analysis/clients/{clientId}/sales` | `{ "routeId": 3, "clearRoute": false, "preferredUserId": 12, "clearPreferred": false, "rowVersion": "…" }` (todo opcional; `null` = sin cambio) |
| Ruta de varios clientes | `POST /api/v1/sales-analysis/clients/assign-route` | `{ "clientIds": [12, 13], "routeId": 3 }` (`routeId: null` quita la ruta propia; hasta 2000 clientes) |
| Lista de clientes | `GET /api/v1/sales-analysis/clients?routeId=3&noRoute=false&q=&skip=0&take=100` | — |

La respuesta de `GET`/`PUT …/sales` trae `routeId`, `routeFromRep`, `directRouteId`, `primary`, `supports`, `preferred`, `preferredOutOfRoute`, `proposedUserId`, `proposedKind` y el representante heredado.

**«Clientes sin ruta de venta» (en bloque).** Configuración → Clientes sin representante → panel **«Clientes sin ruta de venta»** (clientes sin ruta propia ni del representante): marque con las casillas, escoja «Asignar a la ruta…» (o «Quitar la ruta propia») y pulse **«Asignar ruta»** → «Se asignó la ruta a N clientes.». Asignar la ruta en bloque **no toca** el vendedor preferido de esos clientes: si ya no es de la nueva ruta, la ficha lo señala.

## 7. Vendedor de la orden de venta

**Qué hace.** Cada orden guarda **la ruta de venta** y **el vendedor**. Cómo se decide:

1. **En borrador** el campo **Vendedor** ofrece a las personas activas que se pueden escoger (el titular, los apoyos de la ruta del cliente y, al final, el preferido si no es de la ruta) y **propone** una: el **preferido activo** → el **titular activo** → **quien crea la orden**, solo si es persona vigente de la ruta → ninguno («Sin vendedor»). Se rotula «Propuesto». Quien tiene `sales.create` puede escoger otra de la lista.
2. **Al solicitar** (Borrador → Solicitada) se **congela una copia**: la ruta efectiva del cliente en ese momento, el vendedor (la elección si sigue valiendo y es persona activa; si no, la propuesta) y el momento. **Esa copia no cambia después:** ni cambiar la ruta o el preferido del cliente, ni cancelar, ni devolver un despacho a «Solicitada».
3. Las órdenes solicitadas **antes del lote** no tienen ruta ni vendedor (se ven «—»); no se rellenaron.

**Dónde se ve.** En la orden (borrador: campo «Vendedor» y la línea «Ruta de venta»; solicitada: «Ruta de venta · Vendedor»), en la **lista** de Órdenes de venta (columnas **Ruta de venta** y **Vendedor**; en un borrador salen «—» porque la copia se toma al solicitar) y en el **correo** de «Orden de venta solicitada» (Lote 243): el vendedor es el copiado y el correo le llega solo si sigue siendo persona activa; si no, el envío queda `SKIPPED`.

**Filtros de la lista.** **Ruta de venta** (incluye «Sin ruta de venta») y **Vendedor** (si tiene `sales.analysis.view|manage`). API: `GET /api/v1/sales-orders?routeId=3&sellerUserId=12`.

**API.**

| Qué | Llamada | Cuerpo |
|---|---|---|
| Opciones del campo | `GET /api/v1/sales-orders/seller-options?client={publicId}` (permiso `sales.create`) | — |
| Escoger al crear o editar el borrador | `POST`/`PATCH /api/v1/sales-orders` | `{ …, "sellerUserId": 12 }`; `"clearSeller": true` vuelve a la propuesta |

`seller-options` responde `{ "routeId", "routeName", "routeFromRep", "proposedUserId", "proposedName", "proposedKind", "options": [{ "userId", "name", "kind", "isPreferred", "isProposed" }] }` (`kind`: `PRIMARY`, `SUPPORT` o `PREFERRED`). **No trae correos.**

**Reglas.** La elección debe ser el preferido activo del cliente o una persona vigente y activa de su ruta. Si cambia el cliente del borrador y la elección ya no vale, vuelve a la propuesta **sin error**. El vendedor solo se cambia en borrador.

## 8. Cuota por ruta o por persona

**Qué hace.** La cuota mensual se puede llevar **por ruta** (una cuota para cada ruta, sin importar quién la atienda) o **por persona (representante)**, como antes. Las dos clases **conviven**: se pueden cargar las dos; el ajuste decide cuál **cuenta** para el análisis. Por omisión cuenta la de **persona** (nada cambia hasta que se cambie el ajuste). La herencia mensual es la de siempre: un mes sin cuota propia vale la del mes anterior más reciente; quitar la de un mes (monto vacío) vuelve a valer la anterior.

**Quién puede.** Ver: `sales.analysis.view|manage`. Cambiar el ajuste y las cuotas: `sales.analysis.manage`.

**Cómo se usa.**
- Configuración → **Ajustes** → **«La cuota se mide por»** → «Ruta de venta» o «Persona (representante)» → Guardar («Se guardó cómo se mide la cuota.»).
- Configuración → **Cuotas**: escoja «Cuotas por ruta de venta» o «Cuotas por persona»; la que no cuenta se marca «no es la que cuenta para el análisis». Muestra «Cuota vigente total», «Total por rutas» y «Total por personas».
- Al abrir una ruta se ven sus cuotas propias («Cuotas de la ruta»).

**API.**

| Qué | Llamada | Cuerpo |
|---|---|---|
| Ajuste | `PUT /api/v1/sales-analysis/settings` | `{ "quotaBy": "ROUTE" }` (`ROUTE` o `REP`) |
| Cuotas de un mes | `GET /api/v1/sales-analysis/quotas?month=2026-11&by=ROUTE` | — (`by` opcional; por omisión, el ajuste) |
| Cargar en bloque | `PUT /api/v1/sales-analysis/quotas/2026-11` | `{ "items": [{ "routeId": 3, "amount": 1500.00 }, { "repId": 5, "amount": 800 }, { "routeId": 4, "amount": null }] }` |
| De una ruta | `GET /api/v1/sales-analysis/routes/{id}/quotas` | — |

Cada renglón lleva `repId` **o** `routeId` (uno de los dos). `amount: null` quita la cuota propia de ese mes. Los montos son cero o mayores, con hasta 2 decimales.

## 9. Análisis «Por ruta» y «Por representante»

**Qué hace.** Pestaña **«Por ruta»** (la de por omisión): venta neta (producto más descuentos; envío y otros cargos no cuentan) de cada ruta en el mes contra el mes de comparación. **«Por representante»** sigue como estaba (capítulo 51).

**Quién puede.** `sales.analysis.view` o `manage`; módulo `SALES_ORDERS`. API: `GET /api/v1/sales-analysis/by-route?month=2026-10&compareMonth=2026-09` (por omisión, el último mes completo contra el anterior).

**Qué trae.**
- **Cifras del mes:** venta del mes, cambio contra el mes de comparación y **cumplimiento de la cuota** (de una cuota de …). Debajo dice si «La cuota se mide por ruta de venta» o «por persona».
- **Comparativo por ruta de venta:** una fila por ruta con **Titular** (o «Sin titular»), venta del mes de comparación, venta del mes, Cambio $, Cambio %, Cuota $ y Cumplimiento. Cada ruta se **despliega** con sus personas (titular o apoyo, y «Sin vendedor» cuando la venta no tiene persona); hay **«Desplegar todas»** y **«Plegar todas»**. Con cuota por **ruta**, la cuota está en la fila de la ruta; con cuota por **persona**, en la fila de cada persona (su cumplimiento es su venta **total**, de todas sus rutas, entre su cuota).
- **Por persona:** la venta total de cada persona en todas las rutas donde vendió.
- **Totales y conciliación:** total de las rutas + «Sin ruta ni vendedor» + «Envío y otros cargos» = **Total del mes**; «Diferencia por revisar» debe dar 0.
- **Líneas**, **Producto por ruta**, **Transacciones de la ruta** (`GET …/transactions?month=&routeId=` con `routeId=0` = sin ruta; `repId=0` = la persona no es un representante) y **«Exportar a Excel»**.

**De quién es cada venta (atribución).**
- **Ruta:** la copia guardada en la orden → la ruta propia del cliente → la de su representante.
- **Persona:** el vendedor copiado en la orden → el representante del cliente (si está activo y es de **esa** ruta) → el **titular de la ruta el día de la venta** → «Sin vendedor». Un usuario que además es representante cuenta como ese representante.
- Las líneas que no traen copia de la orden (por ejemplo, el historial importado) siguen esta misma regla con los datos de hoy: ruta del cliente y su representante, o el titular de la ruta ese día.

**Avisos del análisis** (no son errores; salen arriba en «Para que el análisis cuadre falta» y en `warnings`):

| Aviso | Qué hacer |
|---|---|
| `N clientes activos sin ruta: asígneles una ruta.` / `1 cliente activo sin ruta: asígnele una ruta.` | Clientes sin ruta de venta: su venta sale en «Sin ruta». Asigne ruta (sección 6) |
| `N rutas activas sin titular: A, B y C.` | Ponga un titular (sección 3) |
| `N rutas con el titular inactivo: …` | Su titular ya no es persona activa: cámbielo |
| `N representantes sin usuario: …. El vendedor siempre es un usuario de Teikem: el administrador crea el usuario y se le pone al representante.` | Cree el usuario en Usuarios y póngaselo al representante |
| `El usuario «X» está en varios representantes activos (A y B): su venta se cuenta en el primero que se creó.` | Deje al usuario en un solo representante activo |

**Gráficos.** «Venta contra cuota por ruta de venta» (con cuota por ruta) o por persona, según el ajuste. Pulso: la cifra «Venta del mes vs cuota» suma las cuotas de las rutas activas o de los representantes activos, según el mismo ajuste.

## 10. «Necesita tu atención»: ruta de venta sin titular

Aparece en **«Necesita tu atención»** (Pulso del día) una fila **«Ruta de venta sin titular: {ruta}»** por cada ruta **activa** sin titular vigente (motivo **«No tiene titular vigente»**) o cuyo titular **ya no es persona activa** (motivo **«Su titular ya no está activo»**).

- La ven quienes tienen `sales.analysis.manage` con el módulo `SALES_ORDERS`.
- Tono de advertencia; **de peligro** si la ruta tiene clientes activos (se muestra la cifra «Clientes»). Primero las que tienen clientes. La fecha «desde» es el cierre del último titular, si lo hubo.
- **«Revisar la ruta de venta {ruta}»** abre Configuración → Rutas (`/analytics/sales?tab=setup&sec=routes`). Es solo un aviso: no bloquea solicitar órdenes (el vendedor propuesto pasa a la siguiente persona de la cadena).
- Se calcula al leer; no hay nada que «cerrar»: desaparece cuando la ruta tiene un titular activo.

## 11. Estatus y transiciones

**Una persona en una ruta (asignación).**

| De | A | Quién | Qué valida | Efectos | Qué queda bloqueado |
|---|---|---|---|---|---|
| (nada) | **Vigente** (titular) | `sales.analysis.manage` | Ruta activa; persona activa; `validFrom` no futura ni anterior al titular actual; no es ya titular | **Cierra al titular anterior** con `validTo` = el inicio del nuevo; si era apoyo, cierra esa fila | — |
| (nada) | **Vigente** (apoyo) | `manage` | Ruta activa; persona activa; no es el titular ni ya es apoyo | Aparece como apoyo | — |
| Vigente | **Cerrada** | `manage` | `validTo` no anterior al inicio ni futura | Fija `validTo`, `closedAtUtc` y `closedBy`; desde esa fecha ya no cuenta como vigente | No se puede cerrar de nuevo ni reabrir |
| Vigente o Cerrada | **Anulada** | `manage` | No estar ya anulada | `IsActive = 0`; si no tenía cierre, `validTo` = `validFrom`; deja de contar en historial y atribución | No se puede cerrar ni anular otra vez ni «desanular» |
| Anulada | (ninguna) | — | — | — | Todo |

**Una ruta de venta.**

| De | A | Quién | Qué valida | Efectos | Qué queda bloqueado |
|---|---|---|---|---|---|
| Activa | **Desactivada** | `manage` | Sin representantes activos ni clientes activos con esa ruta propia | Cierra hoy a las personas vigentes | No se agregan personas ni se escoge como ruta de un cliente (400 «La ruta está desactivada: actívela o escoja otra.») |
| Desactivada | **Activa** | `manage` | El nombre no lo usa otra ruta activa | No reabre a las personas | — |

**Una orden de venta, respecto al vendedor.**

| Estatus | Vendedor y ruta | Qué se puede |
|---|---|---|
| **Borrador** | Elección (o la propuesta de hoy); la ruta es la efectiva de hoy y no está guardada | Escoger o quitar la elección (`sellerUserId`, `clearSeller`) |
| **Solicitada** (Borrador → Solicitada) | Se **congela la copia**: ruta, vendedor y momento | Nada de esto cambia ya |
| Despachada, Despachada en parte, Cancelada, o devuelta a Solicitada | La copia sigue igual | Cambiar el vendedor da 422 «Solo se puede editar una orden en borrador.» |

## 12. Validaciones y mensajes

El API responde en español; el mensaje va en el `title` del error. Los de validación traen además `errors` con el campo.

| Campo o situación | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| Cuerpo | Obligatorio | `El cuerpo de la solicitud es obligatorio.` | 400 (`body`) |
| Papel (`role`) | `PRIMARY` o `SUPPORT` | `El papel debe ser PRIMARY (titular) o SUPPORT (apoyo).` | 400 (`role`) |
| Persona (`userId`) | Obligatoria | `Escoja el usuario.` | 400 (`userId`) |
| Persona | De esta compañía | `El usuario no pertenece a esta compañía.` | 400 (`userId`) |
| Persona | Activa (no suspendida, invitada o inactiva) | `El usuario no está activo en esta compañía: actívelo o escoja otro.` | 400 (`userId`) |
| Ruta al agregar personas, o al escogerla para un cliente | Activa | `La ruta está desactivada: actívela o escoja otra.` | 400 (`routeId`) |
| Fecha de inicio | No futura | `La fecha de inicio no puede ser futura.` | 400 (`validFrom`) |
| Fecha de inicio de un titular nuevo | No anterior a la del titular actual | `La fecha de inicio no puede ser anterior a la del titular actual (AAAA-MM-DD).` | 400 (`validFrom`) |
| Fecha de cierre | No anterior al inicio | `La fecha de cierre no puede ser anterior a la de inicio.` | 400 (`validTo`) |
| Fecha de cierre | No futura | `La fecha de cierre no puede ser futura.` | 400 (`validTo`) |
| `rowVersion` | El valor base64 de la ficha | `rowVersion inválido: se espera el valor base64 devuelto por la ficha.` | 400 (`rowVersion`) |
| Ruta no existe | — | `Ruta no encontrada.` | 404 |
| Asignación no es de esa ruta | — | `Asignación no encontrada.` | 404 |
| Cliente no existe (venta del cliente, asignar ruta, opciones de vendedor) | — | `Cliente no encontrado.` | 404 |
| Representante no existe (cuotas) | — | `Representante no encontrado.` | 404 |
| Persona ya es titular | — | `La persona ya es titular de esta ruta.` | 409 |
| Persona ya es apoyo | — | `La persona ya es apoyo de esta ruta.` | 409 |
| Titular que se quiere poner de apoyo | Cerrar antes | `La persona es la titular de esta ruta: cierre su asignación antes de ponerla de apoyo.` | 409 |
| Cerrar una cerrada | — | `La asignación ya está cerrada.` | 409 |
| Cerrar o anular una anulada | — | `La asignación ya está anulada.` | 409 |
| Dos personas cambian la ruta a la vez (o `rowVersion` viejo en una asignación) | Recargar | `Otra persona cambió las personas de la ruta al mismo tiempo: recargue e intente de nuevo.` | 409 |
| Cliente cambiado por otro (`rowVersion` viejo en la ficha) | Recargar | `El registro fue modificado por otro usuario; recargue e intente de nuevo.` | 409 |
| Desactivar ruta con clientes | Pasarlos antes | `La ruta tiene clientes asignados: páselos a otra ruta antes de desactivarla.` | 409 |
| Desactivar ruta con representantes | Pasarlos antes | `La ruta tiene representantes activos: páselos a otra ruta antes de desactivarla.` | 409 |
| Nombre de ruta repetido (al reactivar) | — | `Ya hay una ruta activa con ese nombre.` | 409 |
| Representante sin usuario (alta, o `clearUser`) | Obligatorio | `Escoja el usuario del representante: el vendedor siempre es un usuario de Teikem.` | 400 (`userId`) |
| Usuario ya es de otro representante activo (alta, cambio o reactivar) | Uno por usuario | `El usuario ya es el representante «Nombre».` | 409 |
| Nombre de representante repetido | — | `Ya hay un representante activo con ese nombre.` | 409 |
| Desactivar representante con clientes | Pasarlos antes | `El representante tiene clientes asignados: páselos a otro representante antes de desactivarlo.` | 409 |
| Vendedor preferido en cliente sin ruta | Necesita ruta | `El cliente necesita una ruta antes de asignarle un vendedor preferido.` | 400 (`preferredUserId`) |
| Vendedor preferido fuera de la ruta | Persona activa de la ruta | `El vendedor preferido debe ser una persona activa de la ruta del cliente.` | 400 (`preferredUserId`) |
| Asignar ruta a varios clientes | Al menos uno | `Indique los clientes (clientIds).` | 400 (`clientIds`) |
| Asignar ruta a varios clientes | Hasta 2000 | `No se pueden mandar más de 2000 renglones en una sola llamada.` | 400 (`clientIds`) |
| Cuota: `quotaBy` o `by` | `ROUTE` o `REP` | `La cuota se lleva por ruta (ROUTE) o por persona (REP).` | 400 (`quotaBy`, `by`) |
| Renglón de cuota | `repId` o `routeId`, uno de los dos | `Cada renglón lleva el representante (repId) o la ruta (routeId), uno de los dos.` | 400 (`items`) |
| Monto de cuota | Cero o más, hasta 2 decimales | `La cuota debe ser cero o mayor, con hasta 2 decimales.` | 400 (`items`) |
| Renglones de cuotas | Hasta 2000 | `No se pueden mandar más de 2000 renglones en una sola llamada.` | 400 (`items`) |
| Mes (`month`, `compareMonth`) | `AAAA-MM` | `El mes debe tener la forma AAAA-MM (por ejemplo 2026-10).` | 400 |
| Opciones de vendedor sin cliente | Obligatorio | `Escoja el cliente que compra.` | 400 (`client`) |
| Vendedor de la orden | Preferido activo del cliente o persona vigente y activa de su ruta | `El vendedor debe ser el vendedor preferido del cliente o una persona vigente de su ruta.` | 400 (`sellerUserId`) |
| Editar la orden con el vendedor, fuera de borrador | Solo borrador | `Solo se puede editar una orden en borrador.` | 422 |
| Módulo apagado | `SALES_ORDERS` encendido | `El módulo 'SALES_ORDERS' no está habilitado para esta compañía.` | 403 |
| Sin permiso | — | (sin mensaje propio; queda `PERMISSION_DENIED`) | 403 |

Los mensajes de la pantalla de estos casos (por ejemplo «El usuario es obligatorio.» al guardar un representante sin usuario) los pone la propia pantalla antes de llamar al servidor; el servidor responde con los de la tabla.

## 13. Preguntas frecuentes

**¿Cómo cambio al titular de una ruta?** Configuración → Rutas → «Personas» → «Agregar persona» con papel «Titular». El anterior se cierra solo. No hace falta cerrarlo antes.

**¿Cómo sé quién era el titular en octubre?** «Personas» → «Mostrar el historial». Cada fila trae «Desde» y «Hasta». El análisis de un mes usa al titular **de cada día** de venta.

**Puse a alguien por error. ¿Cerrar o anular?** **Anular**: queda como si no se hubiera hecho. Cerrar es para quien dejó la ruta de verdad.

**La ruta salió «Sin titular» después de crear el representante.** El titular automático solo actúa si el representante tiene usuario, ese usuario es persona activa, la ruta está activa y no tenía titular. Si la ruta tiene varios representantes, queda sin titular a propósito (los demás son apoyo): escoja uno.

**La ficha del cliente dice «El vendedor preferido ya no pertenece a la ruta del cliente.»** Cambió la ruta del cliente o la persona dejó la ruta. No bloquea nada. Escoja otro preferido (de la ruta actual) o quítelo. Mientras tanto, si esa persona sigue activa en la compañía, la orden nueva la sigue proponiendo.

**No me deja poner un vendedor preferido.** El cliente necesita una ruta (propia o del representante) y la persona debe ser titular o apoyo vigente de esa ruta. Primero póngala en la ruta (sección 3).

**¿Puedo vender como alguien que no es de la ruta?** En una orden, solo el preferido del cliente o una persona vigente de su ruta. Si necesita otra persona, agréguela como apoyo de la ruta.

**Cambié la ruta del cliente y la orden vieja no cambió.** Es a propósito: al solicitar, la orden guarda su ruta y vendedor para siempre. Las órdenes anteriores al Lote 244 no tienen ninguno.

**Una venta sale en «Sin ruta ni vendedor».** El cliente no tiene ruta (ni propia ni por representante) y no hay vendedor, o es del historial importado y no se emparejó con un cliente. Asigne ruta al cliente o empareje el nombre ([51 §2.8](51-analisis-de-ventas.md#28-emparejar-a-mano)).

**Una venta sale con la ruta pero «Sin vendedor».** La ruta no tenía titular ese día y el cliente no tiene representante activo de esa ruta. Ponga un titular; el historial usa al titular que haya tenido la ruta ese día, así que solo se corrige hacia atrás con un titular que empiece antes.

**¿Qué cambia si paso la cuota a «Ruta de venta»?** El cumplimiento se mide contra la cuota de la ruta, y el total de cuotas es la suma de las rutas. Las cuotas por persona no se borran: siguen guardadas y se pueden volver a usar.

**¿Dónde se ven las rutas y sus personas en Vistas e indicadores?** Fuentes «Rutas de venta» y «Personas de la ruta (titular y apoyo)».

**«Pasar rutas desde representantes» dice «0» en todo.** No hay clientes sin ruta con representante activo y todas las rutas activas tienen titular o ningún representante con usuario. Es normal después de la primera vez.

## 14. Para soporte

- **Tablas:** `SalesRouteAssignment` (personas de la ruta: `IsPrimary`, `ValidFrom`, `ValidTo`, `IsActive`, `ClosedAtUtc/By`, `VoidedAtUtc/By`), `Client.SalesRouteId` y `Client.PreferredSalesUserId`, `SalesOrder.SalesRouteId`, `SellerUserId` y `SalesCopyAtUtc`, `SalesQuota.SalesRouteId`, `SalesAnalysisSetting.QuotaBy`. Una asignación **vigente** = `IsActive = 1` y `ValidTo` vacío.
- La base garantiza **un titular vigente por ruta** y **una fila vigente por persona y ruta**: un doble clic de dos administradores da el 409 «Otra persona cambió las personas de la ruta…», nunca dos titulares.
- **Auditoría:** cada alta, cierre y anulación queda en la bitácora con el tipo `SALES_ROUTE_ASSIGNMENT`.
- **Migración del lote:** la sección «Lote 244» de `Diseño/logistica-db-update.sql` corre una vez (marca `L244_SALES_ROUTES_FROM_REPS`) e imprime cuántos clientes, titulares y apoyos quedaron y qué representantes activos no tienen usuario. Si hay usuarios repetidos en representantes activos, avisa y el índice `UX_SalesRep_User` se crea en la siguiente corrida.
- **«Persona activa»** = usuario activo **y** membresía **ACTIVE** en la compañía. Un usuario del portal nunca lo es.
- Decisiones y lo que quedó fuera: [lote244-decisiones.md](../lote244-decisiones.md).
