# 23 — Avisos al cliente

Lote 98 (P10, 2026-10-17). Teikem avisa al **cliente** (el sender, quien envía) lo que va pasando con sus envíos, su COD y sus facturas, con **resúmenes** —no el detalle de cada envío—. **No se le avisa al consignatario** desde aquí (los avisos al consignatario, con su aceptación, los maneja el cliente desde el portal; llegan en un lote posterior).

## 1. Quién decide qué

**La compañía decide, cliente por cliente, qué avisos recibe y por qué canal.** Sin configurar, todo está **apagado**: un cliente no recibe nada hasta que la compañía lo enciende. La configuración vive en la ficha del cliente (**Clientes y contratos → panel «Avisos al cliente»**). Ver pide `clients.read`; cambiar, `clients.update`. Cada cambio queda en la bitácora.

## 2. Los cinco avisos

| Aviso (`kind`) | Cuándo | Qué dice el resumen |
|---|---|---|
| **Recogido** (`PICKED_UP`) | Los envíos del cliente pasan a Recogido | «Recogido: N caja(s) de M envío(es)» (con la ruta si la hay) |
| **Salió a entrega** (`OUT_FOR_DELIVERY`) | La ruta sale a entregar (los envíos pasan a En tránsito) | «Salió a entrega: N caja(s) de M envío(es) en la ruta X» |
| **Entregado** (`DELIVERED`) | Los envíos pasan a Entregado | «Entregado: N caja(s) de M envío(es) en la ruta X. COD cobrado: $Y» |
| **COD procesado** (`COD_PROCESSED`) | Una remesa de COD del cliente se **remite** | «Remesa de COD REM-#####: cobrado, comisión, neto (N envíos)» |
| **Factura emitida** (`INVOICE_ISSUED`) | Se **emite** una factura del cliente | «Factura FAC-##### por $T, vence el AAAA-MM-DD» |

**Un resumen por cliente y ruta** (los tres primeros), por remesa y por factura. El resumen sale cuando el grupo lleva **3 minutos sin hechos nuevos**, así una ruta que va entregando manda un solo aviso. El texto va en el idioma de la compañía.

## 3. Canales

| Canal | Qué hace |
|---|---|
| **Correo** | Llega a los **usuarios activos del portal** del cliente (los invitados que no han entrado no reciben) y a los **correos extra** de ese aviso (hasta 10). Remitente con el nombre de la compañía. |
| **Bandeja del portal** | El resumen queda en la bandeja del portal del cliente (la verá cuando exista el portal, lotes siguientes). |
| **SMS** | Se guarda la preferencia y los **teléfonos extra** (hasta 10); **todavía no sale** hasta contratar el proveedor (Twilio): el envío queda «omitido» con ese motivo. |

Un envío fallido se **reintenta** a 1, 5 y 30 minutos y luego queda «con error». Sin correo configurado en la plataforma, el envío queda «omitido» con el motivo. Si no hay destinatarios (sin usuarios del portal ni correos extra), el resumen igual queda en la bandeja y el registro explica por qué no hubo correo.

## 4. Validaciones (400)

| Situación | Mensaje |
|---|---|
| Aviso que no existe | `El aviso no existe.` |
| Un aviso repetido en la misma solicitud | `Cada aviso solo puede venir una vez.` |
| Correo mal escrito | `El correo '{correo}' no es válido.` |
| Teléfono mal escrito | `El teléfono '{teléfono}' no es válido.` |
| Más de 10 correos o 10 teléfonos | `Máximo 10 correos y 10 teléfonos extra por aviso.` |
| Lista de más de 400 caracteres | `Los correos extra no pueden pasar de 400 caracteres.` / `Los teléfonos extra no pueden pasar de 400 caracteres.` |
| Cliente inexistente (o de otra compañía) | 404 |

## 5. Endpoints

| Endpoint | Para qué | Permiso |
|---|---|---|
| `GET /api/v1/clients/{id}/notifications/settings` | Los cinco avisos con su configuración (apagados si no hay), cuántos usuarios del portal recibirían el correo y si el correo está configurado | `clients.read` |
| `PUT /api/v1/clients/{id}/notifications/settings` | Guarda la configuración (valida todo antes de guardar nada) | `clients.update` |
| `GET /api/v1/clients/{id}/notifications/log` | Los resúmenes mandados, con el resultado de cada envío | `clients.read` |

El trabajo programado `CLIENT_NOTIFICATION_DIGEST` («Avisos a los clientes», cada 2 minutos, activo) hace los resúmenes y los envíos. Los hechos solo se anotan para los clientes que tienen ese aviso encendido (los demás no generan nada).

## 6. La pantalla

Ver [el manual de pantallas, L98](frontend/l98-avisos-cliente.md).

## Casos frecuentes

- **El cliente no recibió el correo:** mire el registro del panel (cada resumen dice enviado, omitido o con error y por qué); revise que el cliente tenga usuarios **activos** en el portal o correos extra, y que el correo esté configurado en la plataforma.
- **Salieron dos avisos de «Entregado»:** son dos rutas distintas (un resumen por ruta) o pasaron más de 3 minutos entre una entrega y otra.
- **Encendí un aviso y no llegó nada de lo que pasó antes:** los hechos solo se anotan desde que el aviso está encendido.
