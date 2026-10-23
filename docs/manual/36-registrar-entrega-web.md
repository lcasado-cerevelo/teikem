# 36 — Registrar la entrega desde la web

> Lote 118 (2026-10-18, dictado de Luis). Permiso: **`orders.deliver`** (lo da el administrador a quien decida; por omisión solo lo tiene el rol TenantAdmin). Módulo: **LTL_GROUND**.

## Qué hace
El personal registra que un envío **se entregó** sin esperar a la app del chofer. El envío pasa a **Entregado** y queda un **comprobante de entrega** que el cliente puede ver más adelante en su portal.

- El envío salta los estatus que le falten (Recogido, Sin asignar, Asignado, En tránsito): quedan en el historial como **brincados**, con la misma fecha y hora, igual que un escaneo. Esos estatus no avisan al cliente.
- Si el envío estaba en **Intento fallido**, también se puede entregar.
- Al llegar a Entregado corren los mismos efectos de siempre (el intento de entrega entregado que alimenta la liquidación al chofer, el cobro, los avisos).
- Un envío **Pick up** no se entrega (termina «En almacén») y un envío en **estatus final** (Entregado, Devuelto, En almacén, Cancelado) tampoco.

## La firma automática
El comprobante **no lo firma el consignatario**: lo firma **automáticamente quien lo registra** (porque tiene el permiso, está autorizado). El comprobante guarda y muestra:

- **Quién recibió** (nombre escrito a mano), la **nota** opcional y la **fecha y hora** de la entrega.
- **Registrada por** el usuario, y la firma «**Firmada automáticamente por [usuario]**».
- El aviso «**El consignatario no firmó: la entrega la registró el personal**». Así el cliente nunca cree que el consignatario firmó cuando no firmó.

*(Fotos: todavía no. Falta decidir dónde se guardan los archivos; cuando se decida entran al mismo comprobante.)*

## Cómo se usa (pantalla)
En la ficha del envío (**Operación → Envíos**), con el permiso y el envío en curso, el botón **Registrar entrega** abre el diálogo:

| Campo | Regla |
|---|---|
| **Quién recibió** | Obligatorio, hasta 150 caracteres. |
| **Fecha y hora de la entrega** | Opcional. Vacío = este momento. No puede ser futura (se toleran 5 minutos del reloj del navegador). |
| **Nota** | Opcional, hasta 500 caracteres. |

Al registrar, la ficha muestra el envío **Entregado** y, en la columna derecha, el panel **Comprobante de entrega**. Ver el manual de pantallas: [L118](frontend/l118-registrar-entrega.md).

## La ubicación (Google), sin hacer esperar
Si la parada de entrega **ya tiene coordenadas**, el comprobante las usa (estado «De la parada»). Si no, y la compañía tiene **Google encendido**, la dirección se busca **aparte, sin que el usuario espere**:

- **Un solo intento**, con **tiempo máximo de 30 segundos**. Si Google no responde, no la encuentra, la encuentra con poca precisión o fuera de Puerto Rico, el comprobante queda **sin ubicación** y la entrega se registra igual. Nadie reintenta.
- Estados del comprobante: **Sin ubicación** (no se intentó: Google apagado o parada sin dirección), **De la parada**, **Buscando…** (en cola o en curso), **Encontrada** (con coordenadas), **No se encontró la dirección**, **No se pudo buscar** (error, tiempo o cuota).
- Se usa la Geocoding API de Google limitada a Puerto Rico. La confianza sale del tipo de resultado de Google: `ROOFTOP` = alta; `RANGE_INTERPOLATED` y `GEOMETRIC_CENTER` = media; `APPROXIMATE` = baja. Se rechazan coordenadas fuera de la isla.

### Configuración (administrador del servidor)
Sección `GoogleMaps` (apagada por omisión):

| Clave | Qué es |
|---|---|
| `GoogleMaps:Enabled` | `true` para buscar ubicaciones. |
| `GoogleMaps:ApiKey` | La llave. **Nunca en el repositorio**: variable de entorno `GoogleMaps__ApiKey`. Un marcador (texto que empieza con `<`) se trata como «sin llave». |
| `GoogleMaps:MonthlyCap` | **Tope mensual de consultas de ubicación de las paradas de ruta** (2026-10-22), por encima del cual no se consulta más ese mes. Por omisión 9.000: la consola de Google Cloud muestra gratis las primeras 10.000 consultas al mes de la Geocoding API. `0` = no se consulta nada. Es de la aplicación (todas las compañías suman al mismo tope, como los cobra Google al proyecto), no una pantalla. |
| `GoogleMaps:MinConfidence` | `LOW` (acepta todo; por omisión), `MEDIUM` o `HIGH`. |
| `GoogleMaps:TimeoutSeconds` | Tiempo máximo de la única consulta (30). |
| `GoogleMaps:PollSeconds` | Cada cuántos segundos el trabajo de fondo revisa lo pendiente (5). |

La llave y la dirección del consignatario **no se escriben en el registro del servidor** (se quitó el registro estándar del cliente HTTP de este servicio).

## API
| Método y ruta | Permiso | Qué hace |
|---|---|---|
| `POST /api/v1/orders/{publicId}/delivery` | `orders.deliver` + módulo LTL_GROUND | Registra la entrega. Cuerpo: `recipientName` (obligatorio), `notes`, `deliveredAtUtc`. Devuelve el comprobante. |
| `GET /api/v1/orders/{publicId}/delivery` | `orders.view` + módulo LTL_GROUND | El comprobante, o **204** si el envío todavía no tiene. |

El comprobante trae: `recipientName`, `notes`, `deliveredAtUtc`, `source` (`STAFF_WEB`), `signatureKind` (`USER` = firma automática del usuario), `registeredByName`, `signedByName`, `geoStatus` (`NONE`, `FROM_STOP`, `PENDING`, `RUNNING`, `OK`, `NOT_FOUND`, `FAILED`), `geoAccuracy`, `latitude`, `longitude` y el estatus del envío.

## Mensajes de error
| Mensaje | HTTP | Cuándo |
|---|---|---|
| Indique quién recibió la entrega. | 400 | `recipientName` vacío. |
| El nombre de quien recibió no puede pasar de 150 caracteres. | 400 | Nombre muy largo. |
| La nota no puede pasar de 500 caracteres. | 400 | Nota muy larga. |
| La fecha y hora de la entrega no pueden ser futuras. | 400 | `deliveredAtUtc` más de 5 minutos adelante. |
| Envío no encontrado. | 404 | El envío no existe (o es de otra compañía). |
| El envío {n} ya tiene registrada su entrega. | 409 | Ya hay un comprobante vigente. |
| El envío {n} está en un estatus final ({estatus}): no se registra su entrega. | 422 | Entregado, Devuelto, En almacén o Cancelado. |
| El envío {n} es Pick up: termina «En almacén» y no se entrega. | 422 | Tipo de servicio Pick up. |
| El estatus «Entregado» no está habilitado para esta compañía. | 422 | La compañía lo desactivó en su modelo de estatus. |
| (sin permiso) | 403 | Falta `orders.deliver`. |

## Casos frecuentes
- **No veo «Registrar entrega».** Falta el permiso `orders.deliver`, o el envío ya está en un estatus final, o está inactiva.
- **El comprobante dice «Sin ubicación».** Google está apagado en este servidor, la parada no tiene dirección o no se pudo encontrar; la entrega vale igual.
- **Quiero que el consignatario firme.** Eso es de la app del chofer (todavía no existe); desde la web la firma es siempre la del usuario.

## Firma y fotos del comprobante (Lote 127)
El panel del comprobante tiene **«Firma y fotos»**: las imágenes y documentos adjuntos al comprobante (la firma del que recibió cuando venga de la app del chofer; las fotos de la entrega). Con `orders.deliver` se puede **Agregar foto** (JPG, PNG, WEBP, GIF o PDF, hasta 10 MB) y **Quitar**. Se guardan cifrados y solo se ven con permiso: ver [capítulo 38](38-almacen-de-archivos.md).
