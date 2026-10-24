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
- Lo que sigue (Lotes 215-216): rutas de hoy y asignar en la calle, clientes y almacén en consulta.

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
| Las demás validaciones de entregar / no entregar | Las del chofer (capítulo 04): nombre de quien recibió, firma y foto exigidas por el cliente, motivo, nota, fotos | 400 |
