# Lote 209 — Avisos del chofer (bandeja en la app)

## Qué se construyó
- Tabla `dbo.DriverNotice` (sección 2026-10-23 (d) de `logistica-db-update.sql`; EntityType `DRIVER_NOTICE`), servicio `DriverNoticeService`, endpoints:
  - `GET /api/v1/driver-app/notices` (los últimos 100 y cuántos sin leer), `POST /api/v1/driver-app/notices/read` (`{ids}` o `{all:true}`) — permiso `driverapp.use`.
  - `POST /api/v1/driver-notices` (la oficina escribe un aviso a un chofer) — permiso `trips.dispatch`.
- **Despachar una ruta deja el aviso «Nuevo manifiesto {código}»** al chofer (en la misma transacción, `TripStatusEffect`).
- Los avisos se purgan a los 60 días (en el trabajo nocturno de limpieza `DRIVER_TRACKING_PURGE`).
- App: pestaña **Avisos** con campana e insignia de no leídos; junta los avisos de todas las compañías del chofer (cada uno con su compañía); tocar uno lo marca leído; «Marcar todo como leído»; se refresca cada 5 minutos y al abrir.

## Cómo se probó
3 pruebas nuevas (`TripStatusEffectTests.Notices`): el despacho deja el aviso y leerlo baja el contador; la oficina envía y se validan los mensajes; un no-chofer recibe 403. Pruebas de aislamiento por compañía y de mapa de permisos actualizadas. `tsc` y `expo export` de la app.

## Decisiones a revisar
- **Push** (aviso emergente con el teléfono bloqueado): no está; requiere Firebase (proyecto, `google-services.json`) — por ahora el chofer ve el aviso al abrir la app o a los 5 minutos.
- **Pantalla web para escribir avisos** a un chofer: el endpoint existe, falta el botón en la web (por ejemplo en la ficha del chofer).
- Otros avisos automáticos posibles (envío quitado de su ruta, ruta cancelada): hoy solo «Nuevo manifiesto».

## Ajustes del 2026-10-23 (tarde): MFA con varias compañías y GPS al cerrar un envío
- **MFA en varias compañías**: si el chofer tiene TOTP, el ingreso siempre lo pide y la sesión pasa el segundo factor para todas. Si NO tiene TOTP y alguna de sus compañías lo exige (la compañía o la persona), el ingreso de la app del chofer ahora **pide MFA (e inscribirlo)** aunque la compañía por la que entra no lo exija; antes esa otra compañía quedaba «bloqueada» sin que el chofer pudiera hacer nada. Una compañía que no lo exige no lo pide por sí sola.
- **GPS al entregar y al marcar «no entregado»**: se toma en ese momento y **siempre**, aunque el chofer haya apagado el seguimiento en Perfil (son cosas distintas). Se guarda **aparte de la dirección** (comprobante: `DeliveryProof.Latitude/Longitude`; no entregado: `DeliveryAttempt.Latitude/Longitude`) junto con `DistanceFromStopMeters`, los metros en línea recta a la dirección de entrega. Así la oficina ve cuándo el chofer marcó desde otro lugar (su casa). Sin permiso o sin señal de satélite se registra igual, sin GPS. Umbral de «lejos»: 300 m (`DriverAppRules.FarFromAddressMeters`).
- Pendiente: mostrar la distancia en la web (ficha del comprobante e intentos) y, si se quiere, un aviso a la oficina cuando pase de 300 m.

## Decisiones de Luis (2026-10-23, tarde) ya aplicadas
- **COD en la entrega**: el chofer captura monto y método (efectivo, cheque, cheque de compañía, ATH Móvil, tarjeta; los cheques llevan número), puede cobrar parcial o en varios pagos y no puede pasar de lo que falta. Cada cobro lleva un id propio (`Reference = APP:{id}`) y el servidor lo registra **antes** de la entrega, así un reintento sin señal no cobra dos veces. Queda a nombre del chofer.
- **Firma y foto obligatorias por cliente**: dos interruptores en la ficha del cliente (web, panel «Impresión y portal» → «Entrega con la app del chofer»; columnas `Client.RequireDeliverySignature/RequireDeliveryPhoto`, sección 2026-10-23 (f) del SQL). Si el cliente las exige, el servidor rechaza la entrega sin ellas: «Este cliente exige la firma de quien recibe.» / «Este cliente exige una foto de la entrega.» (400), y la app marca el campo con * y no deja guardar.
- **Aviso al chofer con el GPS apagado**: cuando hay ruta en curso y apagó el envío en Perfil (o le quitaron el permiso), el inicio de la app muestra un recuadro rojo con «Encender ubicación».
- **Permiso de ubicación**: la app no bloquea la entrega si lo niega; queda «sin GPS». Pendiente la opinión del abogado sobre exigirlo.

## Pendiente de estas decisiones
Distancia en la web y aviso a la oficina (>300 m), escribir aviso al chofer y mapa de la ruta (web), recogidas (viaje con tipo) y push con Firebase.

## Distancia a la dirección en la web y aviso a la oficina (2026-10-23)
- **Pago a choferes → Intentos**: columna «Distancia a la dirección» (metros; «(lejos)» en rojo si pasa de 300 m; «Sin GPS» si el intento vino de la app sin ubicación). **Envío → Comprobante de entrega**: línea «Distancia a la dirección» (con «lejos de la dirección de entrega» pasados los 300 m).
- **«Necesita tu atención» → «Entregas del chofer lejos de la dirección»** (`DELIVERY_FAR`): una fila agregada con las entregas y no-entregas hechas desde la app a más de 300 m en los últimos 7 días y la distancia mayor; permiso `driverpay.view`, módulo LTL_GROUND; «Revisar» abre Pago a choferes.
- Pruebas: servidor (`OperationalAlertTests`, 17 verdes) y web (analytics, pago a choferes y órdenes: 550 verdes). El umbral (300 m) está en `DriverAppRules.FarFromAddressMeters` y en los textos de la web.
