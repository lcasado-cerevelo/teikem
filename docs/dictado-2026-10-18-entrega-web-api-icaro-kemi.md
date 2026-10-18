# Dictado de Luis, 2026-10-18 — entrega desde la web, portal, app de choferes, API mínima, integraciones tipo Icaro y Kemi

Notas textuales de lo que Luis pidió mientras se discutía P10 / P7 / P11. Es discusión abierta: nada de esto está construido ni decidido en detalle. Lo marcado **(pregunta)** hay que resolverlo con él antes de diseñar.

## 1. Registrar la entrega desde la web (sin depender de la app del chofer)
- Hay que **inventar una forma de registrar la entrega en la aplicación web**. No debe depender de la app de choferes (P7).
- Lo hace el personal desde la web y **requiere un permiso nuevo** («registrar entrega»).
- Si la entrega **exige firma**, la firma va **automática**: como quien registra tiene el permiso, está autorizado; la firma queda **a nombre del usuario que lo hizo, no del cliente**, y así se ve registrado.
- **Geolocalización:** si la entrega no trae ubicación, se usa el **servicio de Google** (geocodificación de la dirección) para obtenerla. La información de cómo hacerlo puede estar en una carpeta de `F:\Visual Studio 2022\Projects` llamada **`RentComparable`** (Luis ya la indicó).
- Con esto, los **documentos y el comprobante de entrega (POD)** ya no dependen de la app del chofer: pueden venir de la pantalla web.
- **(pregunta)** ¿Qué trae el comprobante cuando lo registra el personal: fecha y hora, quién recibió (nombre), nota, fotos opcionales? ¿Se distingue en pantalla «registrada por el personal» de «registrada por el chofer»?

## 2. Portal del cliente (P10) — lo que Luis espera
- **Lista de consignatarios del cliente:** «no veo por qué no». Se hace.
- **Preferencias de aviso del portal:** Luis espera que ya esté hecho, aunque esté vacío, y que haya **una pantalla para configurarlo**. Verificar qué existe (la tabla `PortalNotificationPref` figuraba sin entidad en el plan del 2026-10-10).
- **Facturas en la app del cliente:** se muestran; **no se pueden marcar como pagadas**.
- **Seguimiento en vivo:** sí depende de la app móvil (pings).
- Aclarar «ajustes» del Lote 102: el plan dice que la app del cliente trae «ajustes» (probablemente idioma, tema y huella), no necesariamente las preferencias de aviso. Verificar en el código.

## 3. App de choferes (P7)
- Debe **verse exactamente igual** a la que Luis ya usa: carpeta **`Colavora_Courier`** en `F:\Visual Studio 2022\Projects` (Luis la nombró «Colabora Web Courier»), dentro de la subcarpeta que él llamó «Colabora New», una aplicación **web Expo / React Native** que es la que se usa hoy.
- Cambia solo a qué API llama (el de Teikem); **las pantallas deben ser las mismas**.
- La app de los choferes está en una subcarpeta «New» dentro de `Colavora_Courier` (Luis lo confirmó). **(pregunta)** Revisar esa app antes de diseñar P7.

## 4. API de integraciones (P11): versión mínima
- Luis quiere **dar acceso a información** y quizá **ejecutar algunos procesos**, pero con una **versión mínima y controlada** del API: **no un libro abierto**.
- **Más lecturas que escrituras**; la escritura mínima: **crear órdenes** (lo mismo que hace la pantalla) y poco más.
- Luis lo va a analizar: qué se da y cómo. **(pregunta)** Lista de endpoints candidatos, credenciales por cliente con permisos acotados, límites de uso, y qué procesos se pueden disparar.

## 5. Integraciones tipo «Ícaro» (en Teikem)
- En `F:\Visual Studio 2022\Projects` hay una carpeta **`Icaro_Main`**: un software que Luis hizo como **integrador entre dos sistemas**, moviendo información de un lado al otro (entrada o salida).
- Para las integraciones de Teikem quiere **algo parecido a Ícaro**, con la diferencia de que **una de las dos fuentes es Teikem** (la controla él).
- Importa el **concepto de tipos de comunicación** (archivo, y otros) y poder **definir lo que se quiera hacer** sin programar cada vez.
- Ejemplo: **AxisCare** (Luis dijo «AsicsCare»; en la carpeta de proyectos existe `AxisCareDelivery`, a confirmar), cliente de Advance Logistics, **envía archivos** para actualizar las órdenes que se llevan con ellos; Luis quiere configurarlo en Teikem.
- Encaja con lo ya anotado en `plan-de-desarrollo-pendiente.md` (P11 y «procesos manuales de Advance»: COD por lotes, entregas marcadas por documento, órdenes de Truss).
- **(pregunta)** Revisar Ícaro para ver qué conceptos reutilizar (fuentes, mapeos, programación, bitácora).

## 6. Kemi (asistente) con el manual y la FAQ
- A futuro Kemi trabajará con IA; **por ahora, sin IA**: Kemi **busca en el manual y la FAQ** y **trae las opciones** (los pasajes que mejor responden), sin que el usuario vea de dónde salen.
- Si la persona no queda satisfecha, Kemi **pregunta si le contestó** y busca por otro lado.
- Otra opción que Kemi ofrece: **«Pregúntame cómo usar el sistema»**. Eventualmente Kemi también **ayudará a hacer operaciones** en la aplicación (acciones por el API normal, con permiso y confirmación).
- **Para mostrarlo (demo):** la pregunta debe aparecer en la interfaz de Kemi y, **aunque no haga nada todavía, al hacer clic debe responder algo como «en construcción»**.
- **(pregunta)** Dónde vive el buscador (servidor con índice del manual y la FAQ), qué pasa cuando no hay resultado, e idioma (es/en).

## Orden vigente
Sin cambio: P11 y P12 antes; luego P13; P7 y lo que depende de él al final. Esta discusión puede cambiar el alcance de P7 y P10 (la entrega registrada por la web).

## Hallazgos de la revisión (2026-10-18)

### Geocodificación con Google (carpeta `RentComparable`)
- Usa la **Geocoding API** de Google (REST): `GET https://maps.googleapis.com/maps/api/geocode/json?address=…&components=country:PR&key=…`; la inversa (coordenadas → ZIP) con `latlng=`.
- Lee `status`, `results[0].geometry.location` (lat/lng), `location_type`, `place_id`, `formatted_address` y el `postal_code` de `address_components`.
- Código de referencia: `api/Services/GoogleGeocoder.cs` (con `MapConfidence`), caché en la tabla `dbo.GeocodeCache` (`api/Data/GeocodeRepository.cs`) e ingesta en `api/Services/CloseIngestionService.cs` (el caso más parecido a «entrega sin ubicación»).
- Precisión: `ROOFTOP` = alta; `RANGE_INTERPOLATED` y `GEOMETRIC_CENTER` = media; `APPROXIMATE` = baja; hay un mínimo configurable. Rechaza coordenadas fuera de Puerto Rico (lat 17.8–18.7, lng −68.2 a −65.1).
- La llave va en la configuración `GoogleMaps__ApiKey` (variable de entorno), nunca en el código.
- Lo que ese código **no** tiene y Teikem sí debería: reintentos con espera, tiempo máximo, límite de uso, y una dirección de respaldo menos específica (pueblo + ZIP) marcada como **aproximada**. Prioridad de coordenadas: las que traiga la entrega → las del consignatario → geocodificar.
- Si no se puede ubicar, la entrega se registra igual y la ubicación queda vacía.

### App de choferes (`Colavora_Courier\Colavora New Project\colavora`)
- **Tecnología:** Expo ~56, React Native 0.85, React 19, TypeScript, expo-router. Corre nativa (dev-client) y en web. Multi-marca por `APP_VARIANT` (advance, delapaz). Hay copias viejas (`colavora - Copy`) que no cuentan.
- **Pantallas, en orden:** Login (usuario y contraseña, biometría opcional) → Home (4 tarjetas: Entregas de hoy, Manifiestos de entrega, Recogidas de hoy, Manifiestos de recogida) → Manifiestos → Lista de órdenes (buscador, escáner de código de barras, filtros) → Detalle de la orden con 4 pestañas (Detalles con mapa, Piezas, Comentarios, Acciones) → **Completar** (quién recibe, comentario, cobros COD, foto —obligatoria si la orden la exige—, firma —obligatoria si la exige— y GPS) o **Excepción** (motivo del catálogo, comentario obligatorio, foto si se exige) → Perfil (sincronizar, intervalo de autosync, biometría, cerrar sesión).
- **No tiene** botones «Recoger» ni «En camino»: la recogida solo se distingue en Home y no tiene evento propio. Luis dictó que el chofer escoge «Recoger» o «En tránsito» (flujo de estatus de la orden): **eso es nuevo para esta app**.
- **Offline:** todas las escrituras van a una cola SQLite en orden, con `Idempotency-Key` por entrada (igual que `app-almacen`); las lecturas se guardan en caché; los errores 4xx marcan el elemento como fallido.
- **API que usa hoy:** lecturas `common/me`, `shipment/list`, `shipment/{id}`, `shipment/piece`, `shipment/comment`, catálogos de motivos y de COD; escrituras `shipment/event/dispatch_batch/post` (multipart: piezas, quién recibe, comentario, lat/lng, foto y firma en base64), `shipment/event/exception/post`, `shipment/event/cod/post` y `shipment/comment/post`.
- **Autenticación:** Auth0 (usuario y contraseña directo, refresh token). Con Teikem hay que reescribir `services/auth/*` y el contexto de sesión; el resto solo necesita el `Bearer`.
- **Para apuntar a otro API:** la URL base es una variable (`EXPO_PUBLIC_BASE_URL`) en un solo cliente axios; el acoplamiento de transporte es bajo. El acoplamiento fuerte está en la **forma de los datos** (`types/models.ts`, listas de IDs + un GET por orden, estatus «Created» + `assignPK`). Plan sugerido: una capa adaptadora que convierta las respuestas de Teikem a esos tipos, reescribir `custom-api.ts` y `mutations.ts`, y sustituir `auth0.ts`.
- **GPS:** **no hay rastreo de ruta.** Solo toma una posición puntual al abrir «Completar entrega» y la manda con el despacho; si está a más de 200 m de la guardada del consignatario, pregunta si actualizar la dirección. Nada se envía periódicamente. **El «seguimiento en vivo» del portal exigiría añadir rastreo desde cero** (expo-location en segundo plano y un endpoint de pings): esa app tampoco lo da hoy.
- **Diferencias a decidir:** el mapa usa OpenStreetMap/Leaflet y Nominatim (no Google); no hay notificaciones push.
