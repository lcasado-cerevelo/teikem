# Lote 214 — App móvil: órdenes (buscar, ficha y completar como el chofer)

## Decisión de Luis (2026-10-24)
El personal debe poder **completar órdenes desde la app** igual que el chofer (misma evidencia: quién recibió, firma, fotos, GPS, cobros), sobre **cualquier orden** de la compañía, con el permiso que ya existe (`orders.deliver`) y dejando constancia de **quién lo hizo**. Teléfonos, correos y enlaces salen como enlaces en todas partes.

## Qué se construyó
- **Servidor**
  - `GET /api/v1/mobile/orders?q=` (`orders.view`): primero la coincidencia **exacta** del escáner (conduce, número de orden, empaque, factura, número de paquete: `matchedBy`), si no, búsqueda libre acotada a 30. Sin texto: «Escriba o escanee un número de orden, conduce, empaque o factura.» (400).
  - `GET /api/v1/mobile/orders/{publicId}` (`orders.view`): la **misma ficha «en la puerta» del chofer** (`shipment`: contacto y teléfono, piezas, cobro pendiente, firma/foto exigidas por el cliente, tipo) más empaque, factura, servicio, **ruta vigente y su chofer**, el punto de la parada para el mapa, el **comprobante** (quién recibió, cuándo, a cuántos metros, con firma, fotos) y los **intentos**, y qué acción cabe (`canDeliver` / `canFail` / `canPickup`). 404 si no existe.
  - `POST /api/v1/mobile/orders/{publicId}/deliver|fail|pickup` (`orders.deliver`) y `GET /api/v1/mobile/failure-reasons`: el mismo código del chofer (`DriverAppService`), ahora con un **actor** genérico: el chofer (con su ficha, para el intento y el cobro) o **una persona del personal** (sin chofer en el intento ni en el cobro; su nombre queda en el historial: «Entregado desde la app por Ana Oficina.»). Idempotentes como los del chofer. Si la orden está en una ruta vigente se cierra su parada; sin ruta, una orden en su etapa inicial (o ya «Recogida») se trata como recogida y las demás como entrega.
- **App**
  - Inicio del personal: acceso **«Órdenes»** (con `orders.view`). Pantalla con buscador y **escáner** (el de la etiqueta), selector de compañía si hay más de una, resultados en vivo (no hay búsqueda sin señal).
  - La ficha es **la misma del chofer** (mapa, teléfono como enlace, «Hablar sobre este envío») con un bloque más: empaque y factura, ruta y chofer, comprobante e intentos. El mapa usa el punto de la parada que manda el servidor; si no hay, geocodifica la dirección como el chofer.
  - **Acciones** con las pantallas del chofer (Completar, Excepción, Recoger) según lo que la ficha dice que cabe, y solo con `orders.deliver` en esa compañía; lo que se cierra va a la **misma cola sin señal** (`deliver`/`fail`/`pickup`) y sale a `/mobile/orders/...` en vez de `/driver-app/shipments/...`.
- Sin cambios de base de datos ni de permisos.

## Cómo se probó
- Servidor: 4 pruebas nuevas (`TripStatusEffectTests.Mobile.cs`): el personal (sin ser chofer) entrega una orden de la ruta de otro chofer (comprobante DRIVER_APP, historial con su nombre, parada completada, el otro envío sigue, reintento `alreadyDone`); no entrega con motivo; recoge una orden «Sometida» sin ruta (y el reintento no la vuelve a clasificar como entrega); la ficha dice ruta, chofer y acciones, y el buscador encuentra el número exacto. Suite completa: **5489 en verde**.
- App: `tsc`, lint de lo tocado, pruebas puras nuevas (`npm run test:staff`: mapeos fila→envío y ficha→detalle, endpoints por origen) y `expo export`. **No probado en teléfono ni contra el API real.**
- Contrato regenerado (`web-app/openapi.json` y los `schema.d.ts` de web-app, app-almacen y app-cliente).

## Decisiones a revisar
- **Quién «hizo» la entrega del personal**: el intento y el cobro quedan **sin chofer** (`DriverId` NULL) y el comprobante con `RecordedBy` = la persona; el chofer de la ruta no recibe crédito por una entrega que no hizo. Si Luis prefiere atribuirla al chofer de la ruta, es un cambio pequeño en `StaffActorAsync`.
- «Volver a entregar» una orden **Fallida** se permite desde la app del personal (como en la web); no entregar una fallida otra vez no (ya está fallida).
- La búsqueda del personal es **en vivo**: sin señal no hay resultados (las órdenes buscadas no se bajan de antemano). Lo que ya se abrió queda en caché 24 h.
- Los permisos `orders.view` y `orders.deliver` son los de la web; no se creó ninguno nuevo para la app.
