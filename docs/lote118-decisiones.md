# Lote 118 — Registrar la entrega desde la web (dictado de Luis, 2026-10-18)

## Qué se construyó
- **Tabla `DeliveryProof`** (sección (bb) de `logistica-db-update.sql`): el comprobante (quién recibió, cuándo, nota, quién lo registró, firma automática del usuario, estado de la ubicación y coordenadas), un comprobante vigente por orden (índice único filtrado). EntityType `DELIVERY_PROOF` para la bitácora.
- **Permiso `orders.deliver`** («Registrar la entrega de un envío desde la web»): sembrado y propagado a los TenantAdmin ya clonados; Luis designa a quién más se lo da. `PermissionCatalog` pasa de 100 a 101 permisos.
- **`DeliveryRegistrationService`**: valida, lleva la orden a Entregada con `AdvanceSkippingAsync` (brincando lo que falte, como el escaneo; desde Intento fallido se cuenta desde Recogida), crea el comprobante con `SignatureKind = USER` y decide la ubicación (de la parada, en cola o ninguna). Una orden Pick up, una en estatus final o una con entrega ya registrada se rechazan.
- **Ubicación con Google, asíncrona** (`GoogleGeocoder`, `DeliveryGeocodeProcessor`, `DeliveryGeocodeWorker`): un solo intento, 30 s, fuera de la petición del usuario; el trabajo reclama cada comprobante con un UPDATE atómico (varias copias del API no lo buscan dos veces) y da por fallido un «en curso» de más de 2 minutos. Tomado de `RentComparable` (Geocoding API, `components=country:PR`, confianza por `location_type`, recuadro de Puerto Rico), más lo que ese código no tenía: tiempo máximo y un solo intento sin reintentos.
- **API**: `POST` y `GET /api/v1/orders/{publicId}/delivery` (`OrderDeliveryController`); `openapi.json` y el cliente de la web regenerados.
- **Web**: botón «Registrar entrega» y su diálogo en la ficha de la orden, y el panel «Comprobante de entrega» (es/en).

## Cómo se probó
- **xunit** (`DeliveryRegistrationTests`, 15): entrega desde En tránsito, desde Sometida (brinca y queda registrado), desde Intento fallido; doble registro (409); estatus final (422); validaciones; Google encendido (queda PENDING) y apagado o con marcador (NONE); reglas de confianza y recuadro de la isla; respuestas de Google (encontrada, ZERO_RESULTS, OVER_QUERY_LIMIT, REQUEST_DENIED, respuesta inválida, poca precisión, fuera de Puerto Rico); seguridad del controlador. Suite completa: 4498 pasan, 0 fallan.
- **vitest** (6 nuevas en la ficha de la orden): permiso, estatus final, diálogo, validación, POST y panel. Suite completa: 3153 pasan.
- **Playwright** (`lote118-entrega-web.spec.ts`, escritorio y móvil) contra el API y SQL Server reales: la ficha, el diálogo, la validación, el registro, el comprobante, el 409 y la entrega desde Sometida con estatus brincados.
- **Verificación con SQL real del trabajo de ubicación** (con una llave falsa): el registro respondió en ~100 ms con la ubicación «Buscando…» y el trabajo la resolvió en ~2 s como «No se pudo buscar»; la entrega quedó registrada igual. Esa prueba encontró un error real: el registro estándar del cliente HTTP escribía en el log **la URL completa con la llave y la dirección del consignatario**; se quitó (`RemoveAllLoggers`) y se comprobó que ya no aparecen.

## Decisiones a revisar
1. **Sin fotos todavía.** Luis aprobó «fotos opcionales», pero no existe un lugar donde guardar archivos (D10 del plan). Entran al mismo comprobante cuando se decida.
2. **«Si la orden exige firma»:** hoy ninguna orden ni contrato tiene una marca de «exige firma/foto». La firma automática del usuario se guarda siempre; cuando exista la marca (app del chofer) solo cambiará quién firma allá.
3. **Se entrega la orden completa.** No marca piezas faltantes (eso es del escaneo y de Reclamaciones). Si una orden se entrega incompleta, la pieza que falte se maneja por Reclamaciones.
4. **Una orden ya entregada por otra vía** (cambio de estatus manual) **no admite** el comprobante después (estatus final). Si hace falta, se abre el caso aparte.
5. **La ubicación no se guarda en la parada** (`OrderStop.GeoPoint`), solo en el comprobante; así una dirección mal encontrada no mueve el mapa de las rutas.
6. **Google apagado por omisión** (`GoogleMaps:Enabled=false`): hasta que Luis ponga la llave en el servidor, el comprobante dice «Sin ubicación».
7. El comprobante todavía **no se muestra en el portal del cliente**: es el siguiente paso de P10.

## Qué quedó fuera
Fotos, el comprobante en el portal del cliente y la app del chofer (sus botones «Recoger» y «En tránsito», y el comprobante con la firma del consignatario).
