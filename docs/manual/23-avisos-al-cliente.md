# 23 — Avisos al cliente

Lote 98 (P10, 2026-10-17). Teikem avisa al **cliente** (el sender, quien envía) lo que va pasando con sus envíos, su COD y sus facturas, con **resúmenes** —no el detalle de cada envío—. **No se le avisa al consignatario** desde aquí (los avisos al consignatario, con su aceptación, los maneja el cliente desde el portal; llegan en un lote posterior).

## 1. Quién decide qué

**La compañía decide, cliente por cliente, qué avisos recibe y por qué canal.** Sin configurar, todo está **apagado**: un cliente no recibe nada hasta que la compañía lo enciende. La configuración vive en la ficha del cliente (**Clientes y contratos → panel «Avisos al cliente»**). Ver pide `clients.read`; cambiar, `clients.update`. Cada cambio queda en la bitácora.

## 2. Los siete avisos

| Aviso (`kind`) | Cuándo | Qué dice el resumen |
|---|---|---|
| **Recogido** (`PICKED_UP`) | Los envíos del cliente pasan a Recogido | «Recogido: N caja(s) de M envío(es)» (con la ruta si la hay) |
| **Salió a entrega** (`OUT_FOR_DELIVERY`) | La ruta sale a entregar (los envíos pasan a En tránsito) | «Salió a entrega: N caja(s) de M envío(es) en la ruta X» |
| **Entregado** (`DELIVERED`) | Los envíos pasan a Entregado | «Entregado: N caja(s) de M envío(es) en la ruta X. COD cobrado: $Y» |
| **Mercancía recibida** (`WH_RECEIVED`) | Un aviso de llegada del cliente se **recibe** en el almacén (pasa a Recibido) | «Recibido en el almacén: N aviso(s) de llegada (LLE-…), U unidad(es)» |
| **Mercancía despachada** (`WH_DISPATCHED`) | Un aviso de salida del cliente se **despacha** (parcial o total; el mismo aviso despachado en dos partes se cuenta una vez) | «Despachado del almacén: N aviso(s) de salida (SAL-…), U unidad(es)» |
| **COD procesado** (`COD_PROCESSED`) | Una remesa de COD del cliente se **remite** | «Remesa de COD REM-#####: cobrado, comisión, neto (N envíos)» |
| **Factura emitida** (`INVOICE_ISSUED`) | Se **emite** una factura del cliente | «Factura FAC-##### por $T, vence el AAAA-MM-DD» |

**Un solo resumen al día por cliente** (los cinco primeros: transporte y almacén). Dentro del correo cada aviso que el cliente tenga encendido es **una línea**: «Recogido…», «Salió a entrega… en 3 ruta(s) (RUT-1, RUT-2, RUT-3)», «Entregado…», «Recibido en el almacén…», «Despachado del almacén…». **Si ese día no pasó nada, no se manda nada.** Lo que ocurre después de la hora del corte espera al resumen del día siguiente.

**La hora la escoge el cliente** (la configura la compañía en su ficha): **«Hora del resumen del día»**, de 00:00 a 23:00 en la hora de la compañía; por omisión **18:00**. El trabajo programado revisa cada 2 minutos y manda lo que ocurrió hasta la última vez que el reloj marcó esa hora; si el trabajo estuvo apagado o se atrasó, no se pierde nada: sale en la pasada siguiente.

**Las remesas de COD y las facturas salen aparte**, una por documento, cuando el grupo lleva **3 minutos sin hechos nuevos** (no esperan a la hora del resumen). El texto va en el idioma de la compañía.

**El correo del resumen llega a quien recibiría cualquiera de los avisos incluidos** (usuarios activos del portal que no apagaron esos avisos, más los correos extra de cada uno); la bandeja del portal lo muestra si al menos uno de los avisos incluidos tiene la bandeja encendida. Una persona del portal que apagó la bandeja de **todos** los avisos del resumen no lo ve.

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
| Hora del resumen fuera de 0–23 | `La hora del resumen debe estar entre 0 y 23.` |
| Cliente inexistente (o de otra compañía) | 404 |

## 5. Endpoints

| Endpoint | Para qué | Permiso |
|---|---|---|
| `GET /api/v1/clients/{id}/notifications/settings` | Los siete avisos con su configuración (apagados si no hay), la hora del resumen del día (`digestHour`), cuántos usuarios del portal recibirían el correo y si el correo está configurado | `clients.read` |
| `PUT /api/v1/clients/{id}/notifications/settings` | Guarda la configuración y, si viene `digestHour` (0–23), la hora del resumen (valida todo antes de guardar nada) | `clients.update` |
| `GET /api/v1/clients/{id}/notifications/log` | Los resúmenes mandados, con el resultado de cada envío | `clients.read` |

El trabajo programado `CLIENT_NOTIFICATION_DIGEST` («Avisos a los clientes», cada 2 minutos, activo) hace los resúmenes y los envíos. Los hechos solo se anotan para los clientes que tienen ese aviso encendido (los demás no generan nada).

## 6. La pantalla

Ver [el manual de pantallas, L98](frontend/l98-avisos-cliente.md).

## Casos frecuentes

- **El cliente no recibió el correo:** mire el registro del panel (cada resumen dice enviado, omitido o con error y por qué); revise que el cliente tenga usuarios **activos** en el portal o correos extra, y que el correo esté configurado en la plataforma.
- **El cliente quiere recibir el resumen a otra hora:** en su ficha, panel «Avisos al cliente», «Hora del resumen del día», y guardar. Vale para el siguiente corte.
- **Hoy no llegó el resumen:** puede ser que no pasó nada ese día (no se manda un resumen vacío), que todavía no sea la hora, o que lo ocurrido después de la hora del corte salga mañana. Mire el registro del panel.
- **La factura no viene en el resumen del día:** es a propósito; las facturas y las remesas de COD salen aparte, una por documento.
- **Encendí un aviso y no llegó nada de lo que pasó antes:** los hechos solo se anotan desde que el aviso está encendido.
