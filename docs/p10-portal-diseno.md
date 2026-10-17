# P10 — Portal de clientes: diseño y orden de construcción (2026-10-17)

Hecho a partir de lo que dictó Luis el 2026-10-17 y de sus respuestas a tres preguntas. Es el plan vivo del portal: cada lote lo actualiza.

## Lo que Luis decidió
1. **El portal es una aplicación aparte**, no vive dentro de la plataforma interna: **web y móvil a la vez con React Native + Expo** (un solo código con Expo Router y `react-native-web`). Carpeta nueva `app-cliente/`. Con su propio dominio y su propio login. (Cierra la decisión D14 del plan.)
2. **Notificaciones al cliente** (el `sender`, quien envía; al consignatario solo con su aceptación, ver abajo):
   - Son **resúmenes**, no el detalle de cada orden: «se recogieron 14 cajas de 6 órdenes». Se arman **por cada evento de ruta** (respuesta de Luis): al terminar la recogida de una ruta, cuando la ruta **sale a entregar** y cuando se **entregan** las órdenes del cliente; más **COD procesado** y **factura emitida** (dos avisos distintos).
   - Llegan **por correo y a la bandeja del portal** (app). Hay un interruptor para **SMS** en las pantallas, apagado hasta contratar Twilio (todavía no está).
   - Preconfeccionadas: ya vienen hechas con su texto.
   - **Se configuran por cliente (por sender), y las configura la compañía** (corrección de Luis): para cada cliente, la compañía decide si quiere cada aviso y por qué canal (correo, bandeja, SMS). Por omisión todos apagados: un cliente no recibe nada hasta que la compañía lo encienda. La pantalla vive en el expediente del cliente (Clientes y contratos).
   - **Avisos al consignatario (corrección de Luis, mismo día):** sí se pueden mandar, por **correo y por mensaje de texto (SMS)**, pero **solo si el consignatario aceptó recibirlos**. Esa opción (recibir correos y/o textos) **la maneja el cliente desde su portal**, no la compañía: **la aplicación de la compañía no tiene pantalla ni permiso para cambiarla**, porque quien contacta al consignatario es el cliente, no la compañía. Sin aceptación registrada, no se le manda nada. (Lote propio, después del portal: el cliente administra a qué consignatarios avisar y por qué canal; SMS espera a Twilio.)
3. **«¿Cómo va mi orden?»**: una página pública (sin cuenta) para el **consignatario**. Una búsqueda sencilla por **un solo número**: el **número de conduce** (`waybill number` en Advance), un número **único por orden de entrega** que es el identificador de la orden para el cliente y para el consignatario. No se busca por teléfono, nombre ni otra cosa.
4. **Mensajes**: el cliente puede escribir desde el portal; el mensaje llega a un **equipo de servicio al cliente designado por cliente** (un grupo de personas, como un chat). Si el cliente no tiene equipo, al rol «Servicio al cliente» de la compañía.
5. El portal es solo de consumo para el usuario: **no se autoadministra** (lo da de alta la compañía desde el expediente del cliente; ya existe `PortalUser`). Seguridad al nivel del documento maestro (módulo 10): login separado, claim del cliente, segundo filtro por `ClientId` (BOLA), MFA por política, bitácora de acceso.

## Lo que ya existe y se reutiliza
- `PortalUser` (alta/estatus desde el expediente del cliente), `UserKind = PORTAL`; el login interno lo rechaza.
- Avisos (Lotes 51–52): cola con reintentos, canales EMAIL / APP / SMS / PUSH, bandeja; **mensajería** con hilos y grupos (Lote 53) y presencia; `messages.use` (no se da por omisión al portal).
- Facturación, COD, rutas, labels, etc.: lo que el portal muestra.

## Orden de construcción
| Lote | Qué | Por qué en este orden |
|---|---|---|
| **97** | **Número de conduce** (único por orden de entrega, generado por Teikem), en Órdenes, labels y búsquedas; las órdenes viejas lo reciben | Es el identificador que usan el cliente, el consignatario y las notificaciones: va primero |
| 98 | **Avisos al cliente** (servidor y pantalla en el expediente del cliente): resúmenes por evento de ruta, COD procesado y factura emitida; **configuración por cliente y canal, la decide la compañía (todo apagado por omisión)**; correo y bandeja del portal; interruptor de SMS | No necesita el login del portal para el correo |
| 99 | **Portal: acceso por cliente** (servidor): login de portal, claim del cliente, segundo filtro `ClientId`, invitación, MFA, eventos de seguridad; endpoints de lectura (inicio, órdenes, ficha, facturas, COD) con pruebas de acceso cruzado | El corazón de seguridad |
| 100 | **app-cliente** (Expo web+móvil): login, inicio, mis órdenes, ficha, bandeja de avisos | La primera pantalla del portal |
| 101 | **«¿Cómo va mi orden?»** público por conduce (con tope de consultas y sin datos personales) | |
| 102 | **Mensajes** del portal con el equipo de servicio al cliente (designado por cliente) | |
| 103+ | Entrada de órdenes, consignatarios, configuración, perfil, indicadores del cliente; seguimiento en vivo cuando exista P7 | |

## Decisiones propias a revisar con Luis
- El número de conduce lo genera Teikem (patrón configurable, por omisión `CND-#######`); sigue habiendo número de orden del cliente (puede repetirse entre clientes) y número de empaque (EMP-#####). El label pasa a mostrar el conduce como «Waybill» (antes mostraba el número de orden).
- La página pública solo contesta estatus y última actualización (nunca nombres, direcciones ni teléfonos), con tope de consultas por dirección para que nadie recorra todos los números.
