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
- **Escanear** (Lote 216): estación de escaneo para el Android con gatillo, **con trabajo sin señal**: ver §2d.
- Lo que sigue: **Lote 217**: clientes y almacén en consulta.

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

## 2d. Escanear (Lote 216) — con trabajo sin señal
- **Quién**: `trips.scan` en alguna compañía (módulo de transporte). Acceso «Escanear» en el inicio. **Devolver** pide además `orders.return`.
- **Dos pasos antes de escanear** (decidido por Luis). La pantalla no deja escanear nada (el campo y la cámara ni aparecen) hasta completarlos, y se repiten cada vez que se entra:
  1. **¿Qué vas a escanear?** El proceso: Recoger, Llegó, Enrutar, Salir o Devolver. No hay uno por omisión.
  2. **¿Cómo lo vas a escanear?** **Todo** (sin escoger nada más: «voy a escanear»), **Por ruta**, **Por ciudad** o **Por chofer**. Con ruta, ciudad o chofer aparece la lista de **todas** las opciones con **buscador por escrito** y **selección múltiple** (se pueden marcar varias). Hay que marcar al menos una.
  Entonces **Empezar a escanear**. Arriba queda el resumen («Enrutar · Por chofer: Juan +1») y **Cambiar** vuelve a los pasos.
- **Para qué**: es la estación de escaneo del capítulo 05 §7b, en el teléfono. Se escanea el **número de cada pieza** (el del label) con el lector con gatillo —que escribe como un teclado— o con la cámara. Cada acción lleva el envío a su estatus:
  | Acción | Estatus | Notas |
  |---|---|---|
  | Recoger | Recogida | |
  | Llegó | Sin asignar (En almacén si es pick up) | |
  | Enrutar | Asignada | además lo pone en una ruta: la que escojas arriba, o la de su zona |
  | Salir | En tránsito | |
  | Devolver | Devuelta | solo con señal y `orders.return` |
  La primera pieza de una acción mueve el envío (brinca los estatus de en medio); las siguientes solo cuentan («4 de 5 piezas»). Un estatus final no se toca.
- **El escaneo es a la PIEZA, no al envío** (Luis): la que dice Sí, Ok o No es la pieza. El número del envío, del empaque o de la factura **no se escanea** ("No se encontró la pieza."). Lo mismo vale en la estación de la web, que usa el mismo servicio (y también tiene «Todo / Por ruta / Por ciudad / Por chofer»; capítulo 05 §7b).
- **Qué dice la pantalla** (voz del teléfono, vibración y color):
  | Pasa | Voz | Color |
  |---|---|---|
  | La pieza se encuentra y es **nueva** para esa acción (mueva el envío o no: si otra pieza ya lo había movido, igual dice Sí y se cuenta) | **Sí** | verde |
  | **Esa misma pieza** ya se había escaneado con esa acción (se repite el escaneo) | **Ok** | ámbar |
  | La pieza no se encuentra; el envío está **cancelado o en un estatus final** (entregado…); es de pick up y la acción no le aplica; o **no es de lo escogido** (otra ruta, otra ciudad u otro chofer, aunque exista en el sistema) | **No** | rojo |
  | El envío ya va **más adelante** (y no es que ya estuviera en ese mismo estatus) que el proceso que escaneas (p. ej. escaneas Enrutar y ya está En tránsito) | **Atención**, y el sistema **para** y dice qué está pasando y qué se está escaneando; **«Cancelar»** no hace nada; **«Regresar el estatus»** hace que el envío **regrese** al estatus de lo que escaneas (escanear hacia atrás es la forma de regresar un estatus); si ese estatus es anterior a Asignada, el envío **sale de su ruta** (y al chofer se le avisa), y los escaneos de acciones posteriores de sus piezas se olvidan para poder volver a avanzar | naranja |
- **La lupa** (icono de lupa junto al campo): escribe el número de una pieza y muestra **lo que haya en la base**: si está cancelada, en estatus final, dada de baja, perdida, «Por verificar» o en una reclamación; en qué ruta y con qué chofer va y con qué acciones se escaneó. Sirve cuando un escaneo dijo «No»: ahí se ve por qué. **No cuenta como escaneo.** Con señal consulta la base completa; **sin señal solo muestra lo bajado** (envíos no completados) y lo avisa.
- **Por ruta / ciudad / chofer**: solo se aceptan **piezas** de lo marcado. La ruta de un envío es su ruta vigente; su chofer es el de esa ruta, de cualquier tipo (entrega o recogida) y estado (planeada, despachada o en curso), así que por chofer salen **todos los paquetes asignados a ese chofer** (un envío sin ruta, o que ya salió de ella, no tiene chofer); su ciudad es la de la parada de entrega. Con **Enrutar y UNA sola ruta marcada**, los envíos libres se ponen en esa ruta; con varias rutas marcadas, solo se aceptan los que ya están en alguna.
- **La voz**: usa la voz del teléfono (texto a voz). Si «No» no se oye claro en el Android, se le puede poner un audio propio.
- **Sin señal**: al abrir la pantalla (y con el botón **Sincronizar**) el teléfono **baja todos los envíos no completados, hasta 5000, los más recientes**, con sus piezas. Con eso contesta cada lectura **al instante**, sin señal, con color, vibración y voz **Sí / No / Ok** (interruptor «Voz»). Lo que cuenta queda en la cola («Por enviar: N») y sale solo cuando vuelve la señal.
- **Pieza que no está en lo bajado**: con señal se le pregunta al servidor; sin señal, «No se encontró la pieza.» (aplica a lo que quedó fuera de los 5000 o es más nuevo que la descarga).
- **Con problema**: si el servidor no acepta un escaneo ya hecho en el teléfono (por ejemplo «no hay ruta abierta que reciba el envío» al Enrutar, o el envío ya se entregó), aparece en «Con problema: N» con el mensaje del servidor; se quita con «Quitar».
- **Cerrar conteo**: con señal, sobre el último envío escaneado; muestra qué piezas quedarán «Por verificar» y pide confirmación.
- **Hora**: el historial del envío marca la hora en que **sincronizó**, no la del escaneo.
- Endpoints: `GET /api/v1/mobile/scan/snapshot?skip=&take=`, `GET /api/v1/mobile/scan/routes`, y los de siempre `POST /api/v1/scan/action` (ahora con `tripPublicId` opcional para Enrutar a una ruta escogida) y `POST /api/v1/scan/close-count`.

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
| Enrutar a una ruta escogida que ya no está abierta | `La ruta {código} ya no está abierta; queda sin asignar.` | 200 (`NO_OPEN_ROUTE`) |
| Enrutar a una ruta escogida que no existe | `La ruta escogida ya no existe; queda sin asignar.` | 200 (`NO_OPEN_ROUTE`) |
| Escanear una pieza que no está en lo bajado, sin señal | `No se encontró la pieza en lo bajado y no hay señal para preguntar.` | (en el teléfono) |
| Asignar sin orden / sin ruta / sin chofer / sin vehículo | `Indique la orden.` / `Indique la ruta.` / `Indique el chofer.` / `Indique el vehículo.` | 400 |
| Mandar una orden de entrega a una ruta de recogida (o al revés) | `La orden {orden} es de entrega: elija una ruta de entrega.` / `La orden {orden} es de recogida: elija una ruta de recogida.` | 422 |
| Mover una orden entregada, fallida, cancelada… | `La orden {orden} está «{estatus}»: ya no se puede mover de ruta.` | 422 |
| La orden ya está en esa ruta | `El envío ya está en esta ruta.` | 409 |
| Sacar una orden que no está en ninguna ruta | `La orden {orden} no está en ninguna ruta.` | 422 |
| Ruta cerrada, cancelada o en borrador sin editar | `La ruta {código} ya fue despachada; no se puede editar ni eliminar.` (el texto cambia según su estatus) | 422 |
| Ruta rápida con entregas y recogidas juntas | `No se pueden juntar entregas y recogidas en la misma ruta.` | 422 |
| Orden no elegible para la ruta (estatus, sin parada pendiente…) | `Hay órdenes que no se pueden asignar a la ruta.` con el motivo por orden | 422 |
| Las demás validaciones de entregar / no entregar | Las del chofer (capítulo 04): nombre de quien recibió, firma y foto exigidas por el cliente, motivo, nota, fotos | 400 |
