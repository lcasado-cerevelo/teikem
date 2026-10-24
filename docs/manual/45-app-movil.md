# 45 — App móvil (chofer y personal)

> Lote 213 (2026-10-24). La app del chofer (capítulo 04, Lotes 203-212) es ahora **la app móvil de Teikem**: la misma app para choferes y personal. Lo que ve cada persona lo deciden sus permisos.

## 1. Quién entra
- Entra quien tiene el permiso **«App móvil: usarla»** (`mobile.use`) **o es chofer activo** en alguna compañía. Sin ninguna de las dos: «Su usuario no tiene acceso a la app móvil en ninguna compañía.» (403).
- `mobile.use` lo traen las plantillas **Chofer** y **Despachador** (y TenantAdmin). El administrador se lo da a quien quiera en Sistema → Roles.
- **Una sesión, todas sus compañías**: igual que el chofer, la persona entra una vez y la app trabaja con cada compañía donde tiene acceso (una compañía que exige MFA lo exige en el ingreso).
- **Usuario «Chofer (solo app)»**: ve únicamente lo del chofer, aunque su rol tuviera otros permisos. Un empleado que también maneja es un usuario normal con permisos de chofer: ve las dos cosas.

## 2. Qué ve el personal (v1)
- **Inicio**: «**Necesita tu atención**» (los mismos avisos del Pulso de la web, por compañía, con `pulse.attention`) y «**Pulso del día**» (los indicadores que puede ver, con `analytics.view`). Si además es chofer, debajo «Mi trabajo de chofer» con sus tarjetas de siempre.
- **Mensajes** (chat con fotos, tarjetas de orden, sugeridos), **Avisos** y **Perfil**: lo mismo que el chofer (capítulo 15 §8-9 y capítulo 04).
- **Manifiestos**: solo para choferes.
- **Órdenes** (Lote 214): ver §2a.
- **Rutas de hoy** y **asignar en la calle** (Lote 215): ver §2b y §2c.
- Lo que sigue: **Lote 216** (planeado): estación de **escaneo de rutas** con el Android con gatillo, que funciona **sin señal** (se bajan hasta 5000 envíos no completados; ver `docs/lote216-plan-escaneo.md`); **Lote 217**: clientes y almacén en consulta.

## 2a. Órdenes (Lote 214)
- **Quién**: el acceso «Órdenes» aparece con `orders.view` en alguna compañía; completar exige además `orders.deliver` **en esa compañía**.
- **Buscar**: escribe o **escanea** la etiqueta. Primero se busca la coincidencia exacta (conduce, número de orden, empaque, factura, número de paquete; la lista dice «Coincidencia exacta»); si no hay, búsqueda libre (hasta 30). Con más de una compañía, un selector arriba. La búsqueda es en vivo: **sin señal no hay resultados**.
- **Ficha**: la misma del chofer (mapa, consignatario, teléfono como enlace, «Hablar sobre este envío», estatus, orden, conduce, piezas, COD y notas) más **empaque, factura, tipo de servicio, ruta vigente y su chofer**, el **comprobante** (quién recibió, cuándo, a cuántos metros, con firma, fotos) y los **intentos**.
- **Acciones** (pestaña Acciones): según el estatus y el tipo de la orden:
  | Orden | Botones |
  |---|---|
  | En tránsito (entrega) | Excepción de pedido, Completar pedido |
  | Fallida | Completar pedido (volver a intentar la entrega) |
  | Sometida sin ruta o en ruta de recogida | No recogido, Recoger |
  | Otra (entregada, cancelada, asignada…) | «Esta orden está «X»: no hay nada que completar desde aquí.» |
  Sin `orders.deliver` en esa compañía: «No tienes permiso para completar órdenes en esta compañía.»
- **Qué queda**: lo mismo que cuando entrega el chofer (comprobante con origen DRIVER_APP, firma y fotos, GPS y distancia a la dirección, cobros, intento), con el historial **a nombre de la persona** («Entregado desde la app por Ana Oficina.»). Si la orden iba en una ruta, su parada se cierra y el chofer la deja de ver.
- **«Acreditar al chofer»** (interruptor, **sí por omisión**): sí = el intento y el cobro cuentan para la liquidación del chofer de la ruta; no = quedan sin chofer. La auditoría siempre dice quién lo cerró.
- **Sin señal**: el personal **no** trabaja sin señal. Buscar, abrir la ficha y completar piden conexión; si falla, la pantalla lo avisa y no queda nada guardado. Solo lo del chofer (sus manifiestos y envíos) se baja y se cierra sin señal.
- Endpoints: `GET /api/v1/mobile/orders?q=`, `GET /api/v1/mobile/orders/{id}`, `GET /api/v1/mobile/failure-reasons`, `POST /api/v1/mobile/orders/{id}/deliver|fail|pickup`.

## 2b. Rutas de hoy (Lote 215)
- **Quién**: `trips.view` en alguna compañía (módulo de transporte). Acceso «Rutas de hoy» en el inicio.
- Lista de las rutas del día (sin las canceladas) con chofer, vehículo, estatus, tipo (entrega o recogida) y número de paradas; deslizar hacia abajo para refrescar. Tocar una ruta muestra sus **paradas en orden** y la **última ubicación** del chofer («hace N min»); tocar una parada abre la ficha de la orden.
- En vivo: sin señal no hay lista.

## 2c. Asignar en la calle (Lote 215)
- **Quién**: `trips.streetassign` en esa compañía (permiso propio, distinto de `trips.dispatch`; lo da el administrador en Sistema → Roles). Entra por **Acciones → Asignar a una ruta** en la ficha de la orden.
- **Qué se ve**: la orden (tipo, estatus y ruta actual) y las **rutas abiertas de hoy del mismo tipo** (planeadas, despachadas o en curso; una orden en su etapa inicial es de recogida y las demás de entrega), la más cercana al domicilio primero, con la distancia en línea recta desde la última ubicación del chofer (solo si la ruta ya salió y mandó ubicación).
- **Acciones** (cada una pide confirmación y deja un aviso al chofer en su bandeja, que le llega al teléfono):
  | Acción | Efecto |
  |---|---|
  | Mandar a esta ruta (la orden no tiene ruta) | Entra al final de la ruta. Ruta despachada: la orden queda «Asignada». Ruta en curso: «En tránsito». Ruta de recogida: la orden no cambia. |
  | Pasar a esta ruta (la orden está en otra) | Sale de la anterior (vuelve a «Sin asignar» si era de entrega) y entra a la nueva; se avisa a los dos choferes. |
  | Sacar de la ruta | Vuelve a «Sin asignar» (entrega) y se avisa al chofer. |
  | Ruta rápida (chofer + vehículo) | Crea la ruta de hoy con la orden y la despacha: el chofer recibe «Nuevo manifiesto» y la acepta en su app. Si falla, la ruta se cancela. |
- **Sin señal no se puede**: todo se aplica al instante en el servidor.
- Endpoints: `GET /api/v1/mobile/street/orders/{id}/targets`, `POST /api/v1/mobile/street/assign|unassign|quick-route`.
- **Qué ve el chofer**: un aviso «Nueva parada en {ruta}» o «Parada quitada de {ruta}». La parada nueva aparece en su manifiesto al refrescar.

## 3. Avisos emergentes
- La app registra el teléfono en `POST /api/v1/mobile/device` para cada compañía. Un chofer queda en su ficha (`DriverDevice`); el personal, en su usuario (`UserPushDevice`). Los mensajes del chat y los avisos llegan por cualquiera de los dos.

## 4. Mensajes exactos
| Situación | Mensaje | HTTP |
|---|---|---|
| Sin `mobile.use` y sin ser chofer | `Su usuario no tiene acceso a la app móvil en ninguna compañía.` | 403 |
| Sesión que no es de la app | `Esta sesión no es de la app móvil.` | 403 |
| Registro del teléfono sin token / plataforma inválida | `Falta el token del teléfono.` / `La plataforma debe ser ANDROID o IOS.` | 400 |
| Buscar órdenes sin texto | `Escriba o escanee un número de orden, conduce, empaque o factura.` | 400 |
| Orden que no existe (ficha o completar) | `Orden no encontrada.` | 404 |
| Entregar una orden de recogida / recoger una de entrega | `Este envío es de una ruta de recogida: se recoge, no se entrega.` / `Este envío es de una ruta de entrega: se entrega, no se recoge.` | 422 |
| Recoger una orden que ya no está pendiente | `El envío {orden} ya no está pendiente de recoger.` | 422 |
| Ya entregada desde la web | `El envío {orden} ya tiene registrada su entrega.` | 409 |
| Asignar sin orden / sin ruta / sin chofer / sin vehículo | `Indique la orden.` / `Indique la ruta.` / `Indique el chofer.` / `Indique el vehículo.` | 400 |
| Mandar una orden de entrega a una ruta de recogida (o al revés) | `La orden {orden} es de entrega: elija una ruta de entrega.` / `La orden {orden} es de recogida: elija una ruta de recogida.` | 422 |
| Mover una orden entregada, fallida, cancelada… | `La orden {orden} está «{estatus}»: ya no se puede mover de ruta.` | 422 |
| La orden ya está en esa ruta | `El envío ya está en esta ruta.` | 409 |
| Sacar una orden que no está en ninguna ruta | `La orden {orden} no está en ninguna ruta.` | 422 |
| Ruta cerrada, cancelada o en borrador sin editar | `La ruta {código} ya fue despachada; no se puede editar ni eliminar.` (el texto cambia según su estatus) | 422 |
| Ruta rápida con entregas y recogidas juntas | `No se pueden juntar entregas y recogidas en la misma ruta.` | 422 |
| Orden no elegible para la ruta (estatus, sin parada pendiente…) | `Hay órdenes que no se pueden asignar a la ruta.` con el motivo por orden | 422 |
| Las demás validaciones de entregar / no entregar | Las del chofer (capítulo 04): nombre de quien recibió, firma y foto exigidas por el cliente, motivo, nota, fotos | 400 |
