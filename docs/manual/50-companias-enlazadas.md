# 50 — Compañías enlazadas (una compañía despacha por otra)

<!-- kemi:staff -->

Lote 227 (2026-11-07) — pedido de vendedores, parte P7. Una compañía de Teikem (por ejemplo **Advance Solutions**) puede ser **cliente de otra** (por ejemplo **Advance Logistics**) y hacer sus entregas por ella. Cuando las dos aceptan el enlace, la entrega de un despacho de Solutions **no crea la orden de transporte en Solutions: la crea en Logistics**, a nombre de un cliente de Logistics que representa a Solutions, y trae de vuelta el número, el estatus y la prueba de entrega. **Sin un enlace aceptado por las dos, nada cruza.** La pantalla está en **Sistema → Integraciones / API → pestaña «Compañías enlazadas»** (`/system/integrations?tab=links`); la entrega se pide desde la orden de venta ([18](18-ordenes-de-venta.md#24-entrega-por-la-compañía-enlazada-lote-227)).

> Capturas pendientes (P8): la pestaña «Compañías enlazadas» (lista de enlaces y entregas), la ventana «Proponer enlace», «Aceptar el enlace con …» (con el cliente que la representa), «Terminar el enlace», la entrega «No se pudo crear» con su mensaje y «Reintentar», y el despacho de la orden de venta con el número y el estatus de la otra compañía y «Ver comprobante».

## 1. Quién puede y dónde

- Módulo **Compañías enlazadas (`INTERCOMPANY`)**: depende de **Integraciones (`INTEGRATIONS`)** y viene **apagado**. Hay que encenderlo en **las dos compañías**. La que da el transporte necesita además el módulo de **Transporte (`LTL_GROUND`)**. Con el módulo apagado todo el API del puente responde 403.
- Dos permisos nuevos, categoría Administración. Los trae el **Admin de compañía** (también en las compañías que ya existían); a cualquier otro rol se los asigna el administrador:
  - **`intercompany.view`** — ver los enlaces y sus entregas. Implica `integrations.view`.
  - **`intercompany.manage`** — proponer, aceptar, rechazar y terminar enlaces. Implica `intercompany.view`.
- **Proponer y aceptar piden además reautenticación reciente (AAL2)**: dan a otra compañía el poder de crear órdenes en la suya o de recibir sus datos.
- **Entregas**: se ven con `intercompany.view` o `sales.view`; **Reintentar** y **Revisar ahora** los hace quien tiene `intercompany.manage` o `warehouse.pick`.
- Quien propone solo puede enlazar con **otras compañías a las que él también pertenece** (el mismo dueño). La plataforma ve todas.

## 2. Cómo funciona, en corto

- Hay dos papeles. La compañía **cliente** (`CLIENT`, la que despacha) y la que **da el transporte** (`PROVIDER`). En el ejemplo: Solutions es cliente de Logistics.
- Lo propone cualquiera de las dos. La propuesta cuenta como la aceptación de quien propone; **acepta la otra**. Si acepta la que da el transporte, **elige su cliente** (el cliente de Logistics a cuyo nombre nacerán las órdenes de Solutions).
- Con el enlace **Activo**: al crear la entrega de un despacho en la compañía cliente, la recolección pasa a empacada, queda un registro «por enviar» y la orden de transporte se crea en la compañía que da el transporte. Si no se puede en ese momento, la manda el trabajo programado en 2 minutos o menos.
- Una compañía cliente tiene **a lo sumo un enlace propuesto o activo** (despacha por una sola). Un cliente de la compañía que da el transporte representa a **una sola** compañía enlazada.
- **Terminado o rechazado, un enlace no se reabre**: se propone otro.

## 3. Funcionalidades

### 3.1 Proponer un enlace

- **Quién:** `intercompany.manage` + AAL2. Módulo `INTERCOMPANY`.
- **Pantalla:** **Proponer enlace** (arriba a la derecha de la lista). **Nuestro papel**, **Compañía**, **Cliente de esta compañía que la representa** (solo si nuestro papel es «Somos quien da el transporte») y **Nota** (opcional, hasta 250 caracteres). La lista de compañías trae solo las otras a las que usted pertenece; las que no tienen el módulo salen como «{nombre} (sin el módulo de compañías enlazadas)».
- **API:** `GET /api/v1/integrations/tenant-links/options` para llenar el formulario y luego `POST /api/v1/integrations/tenant-links`:

```json
{ "companyPublicId": "…", "role": "CLIENT", "clientPublicId": null, "note": "Para las entregas a farmacias." }
```

- `role` es el papel de **nuestra** compañía. Con `PROVIDER`, `clientPublicId` (un cliente nuestro) es obligatorio.
- **Efecto:** el enlace queda **Propuesto** y la otra compañía recibe el aviso `INTEGRATION_LINK_CHANGED` (si tiene el módulo).

### 3.2 Aceptar un enlace

- **Quién:** la **otra** compañía (no la que propuso), con `intercompany.manage` + AAL2.
- **Pantalla:** acción **Aceptar** en la fila (solo visible en un enlace propuesto por la otra) → «Aceptar el enlace con {nombre}». Si nuestra compañía da el transporte, hay que elegir el cliente que representa a la otra; la nota es opcional.
- **API:** `POST /api/v1/integrations/tenant-links/{id}/accept` con `{ "clientPublicId": "…", "note": "…" }` (`clientPublicId` solo cuando quien acepta da el transporte).
- **Efecto:** el enlace pasa a **Activo**; desde ese momento las entregas cruzan. Avisa a la otra compañía.

### 3.3 Rechazar o retirar una propuesta

- **Quién:** `intercompany.manage`. La otra compañía **rechaza**; quien propuso la **retira** (queda como Rechazado).
- **Pantalla:** acción **Rechazar** (o «Retirar propuesta» si la propusimos nosotros), con el **motivo**. **El servidor lo exige** (hasta 250 caracteres): si se deja vacío responde 400 «Escriba el motivo.» aunque la pantalla no lo marque como obligatorio.
- **API:** `POST …/{id}/reject` con `{ "reason": "…" }`.

### 3.4 Terminar un enlace activo

- **Quién:** cualquiera de las dos, con `intercompany.manage`. El **motivo es obligatorio**.
- **Pantalla:** acción **Terminar** → «Terminar el enlace con {nombre}».
- **API:** `POST …/{id}/end` con `{ "reason": "…" }`.
- **Efecto:** desde ese momento **no se manda ni se trae nada**. Lo ya traído se queda como estaba. Las entregas pendientes pasan a «No se pudo crear» con «El enlace con «X» ya no está activo: la orden no se mandó.». Avisa a la otra compañía.

### 3.5 Qué cruza a la otra compañía

La orden que nace en la compañía que da el transporte lleva:

- **Consignatario**: el de la orden de venta (o uno nuevo que se indique en la entrega), como consignatario **nuevo del cliente de allá**; allá se reconoce por nombre y línea 1. No viaja el código de aquí.
- **Paquetes**: tipo, descripción, piezas, peso y volumen. Sin número de pieza (se numera allá). Un tipo de paquete que allá no existe se cambia por el de allá por omisión; la descripción se queda.
- **COD**, fechas (solicitada y prometida), «confirmar al crear» y **prioridad** (una orden de venta **urgente** nace con prioridad alta).
- **Notas**: «Compañía · orden de venta OV-… · despacho EMP-…» y las notas de la entrega (hasta 500 caracteres).
- **No cruza** el tipo de servicio: allá se usa el de la compañía por omisión.
- Si el cliente de allá teclea sus propios números, el número de la orden es el del despacho (EMP-…) y el de la factura es el de la orden de venta (OV-…).
- La orden se crea «como Sistema» (sin usuario) y con la marca de origen del puente. Es el mismo camino que `POST /orders` de la API de clientes: se aplican las **mismas reglas de allá** (cliente suspendido, tarifa vigente, paquetes…).

### 3.6 Entregas por compañía enlazada

- **Quién:** ver, `intercompany.view` o `sales.view`; **Reintentar** y **Revisar ahora**, `intercompany.manage` o `warehouse.pick`. Solo la compañía **cliente** las ve (son suyas).
- **Pantalla:** el segundo panel de la pestaña, **«Entregas por compañía enlazada»**: orden de venta, despacho, **Entrega** (estado, compañía, número y estatus de allá, quién recibió y cuándo, «Ver comprobante»), intentos, última revisión y fecha de creación. Buscador por orden de venta, despacho o número de allá, y filtro por estado. La misma información aparece en el despacho de la orden de venta.
- **Reintentar** (solo en «No se pudo crear»): vuelve a mandar al instante, después de corregir lo que dijo la otra compañía. **Revisar ahora** (solo en «Enviada»): trae el estatus y la entrega sin esperar al trabajo.
- **API:** `GET /api/v1/integrations/tenant-links/deliveries?state=&q=&skip=&take=` · `GET …/deliveries/{id}` · `POST …/deliveries/{id}/retry` · `POST …/deliveries/{id}/sync`.
- **Revisión automática:** el trabajo programado `INTERCOMPANY_SYNC` (cada 2 minutos, por compañía; se cambia en Sistema → Trabajos programados) manda lo pendiente y revisa las entregas que no han terminado.
- **Reintentos solos:** una falla pasajera (sin red, la otra compañía ocupada) se reintenta sola esperando 1, 2, 4… minutos (hasta 60) y hasta **10 intentos**; después queda «No se pudo crear». Si la otra compañía **rechaza** la orden (validación, cliente suspendido, sin tarifa), queda «No se pudo crear» **de una vez** con el mensaje de allá y **no se reintenta sola**.

### 3.7 Prueba de entrega

- **Quién:** `intercompany.view` o `sales.view`.
- **Cómo:** en la entrega, **Ver comprobante** (quién recibió, cuándo, notas, firma y fotos). El comprobante completo **se lee en vivo de la otra compañía**, solo de esa orden; el sistema solo guarda quién recibió y cuándo.
- **API:** `GET …/deliveries/{id}/proof` (responde **204** si todavía no hay) y `GET …/deliveries/{id}/proof/files/{fileId}` para la firma y las fotos.

### 3.8 Facturación «al entregar»

Con la facturación de ventas en «al entregar» ([19](19-facturacion.md)), un despacho entregado por la compañía enlazada cuenta como **entregado** cuando la entrega de allá está **Entregada** o trae comprobante. Antes de eso **no se factura**.

### 3.9 Avisos

Los publica el sistema; **a quién y por qué canal** lo decide una **regla de aviso** que arma el administrador (Sistema → Reglas de aviso): no se siembra ninguna. Se agrupan en Integraciones.

| Aviso | Código | Para quién | Cuándo |
|---|---|---|---|
| Compañía enlazada: cambio en el enlace | `INTEGRATION_LINK_CHANGED` | la **otra** compañía | propusieron, aceptaron, rechazaron o terminaron el enlace (no se puede silenciar) |
| Entrega por compañía enlazada: cambio de estatus | `INTEGRATION_LINK_DELIVERY_STATUS` | la compañía cliente | la orden de allá cambió de estatus (opcional) |
| Entrega por compañía enlazada: no se pudo crear | `INTEGRATION_LINK_DELIVERY_FAILED` | la compañía cliente | la otra no aceptó la orden (no se puede silenciar) |
| Orden urgente de una compañía enlazada | `INTEGRATION_LINK_URGENT_ORDER` | la que da el transporte | entró una orden urgente (no se puede silenciar) |

## 4. Campos y reglas

| Campo | Dónde | Regla |
|---|---|---|
| Nuestro papel (`role`) | Proponer | `CLIENT` (somos el cliente) o `PROVIDER` (la otra es nuestra cliente); obligatorio |
| Compañía (`companyPublicId`) | Proponer | obligatoria; una de las otras a las que usted pertenece, con el módulo encendido; no la propia |
| Cliente (`clientPublicId`) | Proponer con `PROVIDER`; aceptar como proveedor | obligatorio en esos casos; cliente de **su** compañía, existente, activo y en estatus Activo (ni prospecto ni suspendido); uno por compañía enlazada |
| Nota (`note`) | Proponer, aceptar | opcional, hasta 250 caracteres |
| Motivo (`reason`) | Rechazar, terminar | hasta 250 caracteres; **obligatorio al rechazar y al terminar** (el servidor lo exige en los dos) |
| `state` | Lista de entregas | `PENDING`, `CREATED` o `FAILED` |

## 5. Validaciones y mensajes

| Situación | Regla | Mensaje exacto | HTTP |
|---|---|---|---|
| Proponer sin cuerpo | — | `El cuerpo de la solicitud es obligatorio.` | 400 (`body`) |
| Papel inválido | `CLIENT` o `PROVIDER` | `Indique el papel de su compañía: CLIENT (somos el cliente de la otra) o PROVIDER (la otra es nuestro cliente).` | 400 (`role`) |
| Sin compañía | Obligatoria | `Indique la compañía con la que se quiere enlazar.` | 400 (`companyPublicId`) |
| Compañía inexistente, sin el módulo o ajena | No se revela cuál | `No se encontró esa compañía, no tiene encendido el módulo de compañías enlazadas o usted no pertenece a ella.` | 400 (`companyPublicId`) |
| Consigo misma | — | `Una compañía no se puede enlazar consigo misma.` | 400 (`companyPublicId`) |
| El proveedor sin Transporte | `LTL_GROUND` | `La compañía que da el transporte necesita el módulo de Transporte encendido.` | 422 |
| Falta el cliente | Obligatorio para el proveedor | `Indique el cliente de su compañía a cuyo nombre nacerán las órdenes de la compañía enlazada.` | 400 (`clientPublicId`) |
| Cliente inválido | Existente, activo, estatus Activo | `El cliente indicado no existe, está dado de baja o no está activo.` | 400 (`clientPublicId`) |
| Cliente ya usado | Una compañía por cliente | `Ese cliente ya representa a otra compañía enlazada.` | 409 |
| Ya hay un enlace abierto | Uno propuesto o activo por compañía cliente | `Ya hay un enlace propuesto o activo entre estas compañías (o la compañía cliente ya despacha por otra). Termínelo antes de proponer otro.` | 409 |
| Nota larga | Hasta 250 | `La nota no puede pasar de 250 caracteres.` | 400 (`note` o `reason`) |
| Sin motivo | Obligatorio al rechazar y al terminar | `Escriba el motivo.` | 400 (`reason`) |
| Aceptar o rechazar uno que ya no está propuesto | Estatus Propuesto | `El enlace ya no está propuesto: no se puede aceptar ni rechazar.` | 422 (409 si otra persona lo cambió a la vez) |
| Aceptar el propio | Acepta la otra | `Su compañía propuso este enlace: lo acepta o lo rechaza la otra.` | 422 |
| Terminar uno que no está activo | Estatus Activo | `Solo se termina un enlace activo.` | 422 (409 si otra persona lo cambió a la vez) |
| Enlace ajeno o inexistente | El enlace solo existe para sus dos compañías | `Enlace no encontrado.` | 404 |
| Proponer o aceptar sin reautenticación | AAL2 reciente | `Esta acción requiere reautenticación reciente (AAL2).` | 403 |
| Sin el permiso | `intercompany.view` / `.manage` | `Falta el permiso 'intercompany.manage'.` (o el que corresponda) | 403 |
| Módulo apagado | `INTERCOMPANY` | `El módulo 'INTERCOMPANY' no está habilitado para esta compañía.` | 403 |
| Entrega sin consignatario | La otra compañía lo exige | `La entrega por la compañía enlazada necesita el consignatario (de la orden de venta o uno nuevo).` | 400 (`order.consigneeLocationPublicId`) |
| Entrega sin paquetes | Al menos una línea | `Indique al menos una línea de paquete.` (y los demás mensajes de paquetes de siempre) | 400 |
| La otra compañía no recibe | Activa, con `INTERCOMPANY` y `LTL_GROUND` | `La compañía enlazada «X» no está recibiendo órdenes ahora (está desactivada o apagó el módulo de compañías enlazadas o el de Transporte). Revise el enlace en Integraciones.` | 422 al crear la entrega; en una entrega ya guardada queda como error pasajero y se reintenta sola |
| Pendiente con el enlace terminado | — | `El enlace con «X» ya no está activo: la orden no se mandó.` | — (queda «No se pudo crear») |
| El cliente de allá ya no puede recibir | Dado de baja o inactivo | `El cliente que representa a esta compañía en «X» ya no puede recibir órdenes (dado de baja o inactivo).` | — (queda «No se pudo crear») |
| La otra compañía rechazó la orden | Sus reglas | El mensaje de allá, tal cual; por ejemplo `El cliente está suspendido; no se pueden crear ni confirmar envíos.` o `No hay tarifa vigente para STANDARD/BOX; configure la tarifa en el contrato del cliente antes de confirmar.` | — (queda «No se pudo crear») |
| Reintentar una que no está «No se pudo crear» | Estado `FAILED` | `Solo se reintenta una entrega que no se pudo crear en la compañía enlazada.` | 422 |
| Revisar o ver el comprobante sin orden allá | Estado `CREATED` | `La orden todavía no existe en la compañía enlazada.` | 422 |
| Revisar o ver el comprobante con el enlace terminado | Enlace activo | `El enlace con «X» ya no está activo: no se puede consultar la orden de allá.` | 422 |
| Filtro de entregas | `PENDING`, `CREATED` o `FAILED` | `El estado debe ser PENDING, CREATED o FAILED.` | 400 (`state`) |
| Entrega ajena | — | `Entrega por compañía enlazada no encontrada.` | 404 |
| Archivo del comprobante que no es de esa entrega | — | `Archivo no encontrado.` | 404 |
| Deshacer un despacho entregado por la otra compañía | Ya no se puede desde aquí | `El envío de la recolección EMP-… ya avanzó a '—'; la recolección ya no se puede eliminar.` (por lectura del código; no se probó) | 422 |

## 6. Estatus y transiciones

### 6.1 El enlace

No usa el motor de estatus (es de dos compañías): es un texto con cuatro valores. Quién y cuándo queda en el enlace y en la bitácora (`TENANT_LINK`).

| De → a | Quién | Qué valida | Efectos | Qué bloquea |
|---|---|---|---|---|
| (nuevo) → **Propuesto** | `intercompany.manage` + AAL2 | Las reglas de la sección 5 | Aviso a la otra compañía | Nada cruza |
| Propuesto → **Activo** (aceptar) | La **otra** compañía, `intercompany.manage` + AAL2 | Sigue propuesto; no es la que propuso; si da el transporte, tiene Transporte y elige su cliente | Las entregas de la compañía cliente cruzan; aviso a la otra | No se puede aceptar ni rechazar otra vez |
| Propuesto → **Rechazado** (rechazar o retirar) | La otra (rechaza) o quien propuso (retira), `intercompany.manage` | Sigue propuesto; motivo obligatorio, hasta 250 | Aviso a la otra | No se reabre: se propone otro |
| Activo → **Terminado** | Cualquiera de las dos, `intercompany.manage` | Está activo; motivo obligatorio | No se manda ni se trae nada; pendientes → «No se pudo crear»; aviso a la otra | No se reabre: se propone otro; ya no se puede revisar ni ver comprobantes de allá |

### 6.2 La entrega

| De → a | Quién / qué lo hace | Qué valida | Efectos | Qué bloquea |
|---|---|---|---|---|
| (nueva) → **Por enviar** (`PENDING`) | Crear la entrega del despacho (`warehouse.pick`) con enlace activo | Consignatario y paquetes; la otra compañía recibe | La recolección queda empacada **sin orden aquí**; queda el registro (uno por despacho) | Deshacer el despacho desde aquí |
| Por enviar → **Enviada** (`CREATED`) | El envío inmediato, el trabajo o «Reintentar» | Que el enlace siga activo y la otra compañía reciba | Se crea la orden allá (una sola vez; si ya existe por su marca, no se duplica); se trae número y estatus | — |
| Por enviar → **No se pudo crear** (`FAILED`) | La otra compañía rechazó, el enlace terminó o se agotaron los 10 intentos | — | Guarda el mensaje; aviso `INTEGRATION_LINK_DELIVERY_FAILED` | Nada se manda solo hasta que se reintente |
| No se pudo crear → **Por enviar** | «Reintentar» (`intercompany.manage` o `warehouse.pick`) | Que esté en «No se pudo crear» | Intentos en cero; se manda al instante | — |
| Enviada → (sigue **Enviada**, con otro estatus de allá) | El trabajo cada 2 minutos o «Revisar ahora» | Enlace activo | Trae número, estatus, quién recibió y cuándo; un cambio de estatus avisa (`INTEGRATION_LINK_DELIVERY_STATUS`); al llegar a un estatus final deja de revisarse | Con el enlace terminado se deja de traer |

## 7. Antes de usarlo (puesta en marcha)

1. Encender **Integraciones** y **Compañías enlazadas** en las dos compañías (Logistics, además, con **Transporte**).
2. En la compañía que da el transporte, crear el **cliente** que representa a la otra (por ejemplo «Advance Solutions») con **contrato y tarifa vigentes**. Sin tarifa la orden se rechaza.
3. Proponer el enlace desde la compañía cliente y aceptarlo en la otra, **eligiendo ese cliente**.
4. Armar las **reglas de aviso**: entregas en la compañía cliente, urgentes en la que da el transporte.
5. Probar con una orden de venta chica antes de abrirlo a los vendedores.

## 8. Preguntas frecuentes del módulo

- **No veo la pestaña «Compañías enlazadas».** Hace falta `intercompany.view` y el módulo Compañías enlazadas encendido en su compañía.
- **La compañía no aparece al proponer.** Solo salen las otras compañías a las que usted pertenece. Si sale como «(sin el módulo de compañías enlazadas)», hay que encenderlo allá antes.
- **Propuse y no pasa nada.** Falta que la otra compañía lo acepte; hasta entonces el enlace está «Propuesto» y las entregas se crean en su compañía como siempre.
- **La entrega quedó en «No se pudo crear».** Lea el mensaje en la entrega: es el de la otra compañía (tarifa, cliente suspendido, servicio…). Corríjalo **allá** y pulse **Reintentar**.
- **Quedó «Por enviar» y no avanza.** El trabajo la reintenta cada 2 minutos hasta 10 veces. Si la otra compañía está apagada o sin el módulo, vea el mensaje de «no está recibiendo órdenes ahora».
- **¿Por qué no hay orden de transporte en mi compañía?** Por diseño: la entrega es de la otra compañía. Se cancela allá.
- **¿Se puede reabrir un enlace terminado?** No. Se propone otro.
- **¿Por qué no se factura el despacho?** Con facturación «al entregar», espera que la entrega de allá esté Entregada o traiga comprobante. Use **Revisar ahora**.
- **El comprobante dice que no hay.** Responde 204: la otra compañía todavía no registra la entrega.

Los mensajes están en la [FAQ](faq.md#lote-227--compañías-enlazadas).

## 9. Para soporte

- Tablas `TenantLink` (sin `TenantId`: es de las dos compañías) y `TenantLinkOrder` (de la compañía cliente), módulo `INTERCOMPANY`, permisos y EntityType `TENANT_LINK` / `TENANT_LINK_ORDER`: sección «2026-11-07 — Lote 227» de `Diseño/logistica-db-update.sql` (marca `L227_INTERCOMPANY` para los roles clonados de Admin de compañía).
- Índices únicos: un enlace propuesto o activo por compañía cliente (`UX_TenantLink_ClientOpen`), un cliente por compañía enlazada activa (`UX_TenantLink_ProviderClient`) y una entrega por despacho (`UX_TenantLinkOrder_PickBatch`, la que evita duplicar la orden).
- Código: `TenantLinkService` (enlaces), `TenantLinkBridge` (entregas), `LinkedTenantScope` (el único lugar por donde se hace algo en la otra compañía: como esa compañía, sin usuario y acotado al cliente del enlace), `TenantLinksController`, trabajo `INTERCOMPANY_SYNC`.
- La orden de allá lleva la marca de origen `TENANT_LINK_ORDER` + id de la entrega. Solo se lee la orden que nació de esa entrega.
- «¿Quién creó la orden en Logistics?»: aparece como «Sistema» (sin usuario); no hay cuenta de servicio con nombre.
- Depot (Solutions como cliente de almacén de Depot) **no** entra: el puente solo hace entregas de transporte.
