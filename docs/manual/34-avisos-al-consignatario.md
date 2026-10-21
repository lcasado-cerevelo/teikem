# 34 — Avisos al consignatario

Lote 116 (2026-10-18, dictado por Luis; diseño en `docs/p12-avisos-consignatario-diseno.md`). Lo que **recibe el consignatario** (el que recibe el paquete) sobre sus envíos. **Lo prende y lo marca el cliente** (no la compañía), desde el portal.

## 1. Quién y dónde
- **Cliente (portal):** un administrador u operador del cliente **prende** el aviso, escoge su **idioma** (el de los avisos al consignatario) y **marca, por consignatario**, si acepta recibir avisos por **correo** y/o por **texto**. Quien solo consulta no puede cambiarlo. (Módulo CLIENT_PORTAL.)
- **Consignatario (sin sesión):** abre el enlace del correo para ver el avance de su paquete o para **darse de baja**.

## 1.1 Las pantallas (app del cliente)
- **Ajustes → Avisos a mis consignatarios → Configurar avisos** (`/consignee-notices`): casilla **«Avisar a mis consignatarios»**, **Idioma de los avisos** (Español / English) y la lista de consignatarios con búsqueda por nombre o ciudad. Cada consignatario trae dos casillas: **Avisar por correo: <correo>** y **Avisar por texto: <teléfono>** (deshabilitadas si ese medio no está registrado o el aviso está apagado; el texto se guarda pero todavía no sale). Marcar muestra la **advertencia** de privacidad con **Continuar / Cancelar**; desmarcar no la pide. Quien solo consulta lo ve sin poder cambiarlo.
- **Seguimiento por enlace** (`/track/<token>`): pública; la barra de progreso del envío y el último movimiento. Ver capítulo 28.
- **Darse de baja** (`/unsubscribe/<token>`): pública; abrir el enlace **no** da de baja: hay que pulsar **«Sí, darme de baja»** (así un filtro de correo no lo hace por usted).

## 2. Cuándo se avisa
| Qué pasó con el envío | ¿Se avisa? |
|---|---|
| **Sale En ruta** (En tránsito) — sale el enlace de seguimiento | Sí |
| **Intento fallido**, otra salida En ruta, otro intento… | Sí |
| **Estatus final**: Entregada, Devuelta, Cancelada, En almacén | Sí |
| Recogida, Sin asignar, Asignada (movimientos dentro del almacén) | **No** |
| Un estatus **brincado** por el escaneo | **No** |
Solo si el cliente tiene el aviso **prendido**, el consignatario está **marcado** para ese medio, tiene ese medio **registrado** (correo o teléfono en su ficha) y **no se dio de baja**. El correo lleva el **idioma del cliente**, el enlace de seguimiento del envío y el de baja.

## 3. Marcar a un consignatario
Al marcarlo sale una **advertencia** (parte de la política de privacidad): *«Si desea continuar, confirma que el consignatario aceptó recibir avisos…»*; hay que aceptarla. Entonces:
- queda en la **bitácora** (quién, cuándo, qué medio, con qué correo o teléfono y que aceptó la advertencia);
- **se le avisa al consignatario**, por el medio marcado, que el cliente lo marcó (el correo incluye cómo darse de baja). El texto (SMS) se guarda pero **no sale** hasta contratar el proveedor.
Desmarcar también queda en la bitácora y no avisa. Marcar o desmarcar lo mismo otra vez no hace nada.

## 4. Enlace de seguimiento y baja
- **Seguimiento:** `…/track/<token>`. El token es propio de cada envío, largo y **imposible de adivinar**; la página solo muestra el avance (no recibe datos ni permite buscar por número). API: `GET /api/v1/public/track/link/{token}` (sin sesión; 404 *«No encontramos ese enlace de seguimiento.»* si no existe).
- **Baja:** `…/unsubscribe/<token>` (cada aviso trae el suyo). API: `POST /api/v1/public/consignee-unsubscribe/{token}` (sin sesión; idempotente; 404 *«El enlace de baja no es válido.»*). La baja es **definitiva** para ese correo o teléfono en la compañía: queda en la bitácora, el consignatario deja de estar marcado y **el cliente no puede volver a marcarlo** (409).
- Toda comunicación al consignatario trae la opción de darse de baja (enlace en el correo; «STOP» en el texto cuando exista).

## 5. Envío
Un trabajo cada minuto (`CONSIGNEE_NOTICE_DISPATCH`, ver Trabajos) manda los avisos pendientes y reintenta los fallidos a **1, 5 y 30 minutos**; al cuarto intento queda **fallido**. Hace falta el correo configurado (Brevo) y la dirección de la app del cliente en `ConsigneeNotices:ClientAppBaseUrl` (sin ella o sin correo, el aviso queda *omitido* con el motivo).

## 6. Mensajes
| Mensaje | HTTP | Cuándo |
|---|---|---|
| Su rol en el portal es de solo consulta: no puede cambiar los avisos al consignatario. | 403 | Un usuario de solo consulta cambia el aviso o marca a alguien. |
| Primero prenda el aviso al consignatario. | 422 | Marcar sin tener el aviso prendido. |
| Para marcar que el consignatario recibe avisos debe aceptar la advertencia de privacidad. | 400 | Marcar sin `warningAccepted`. |
| Indique el medio del aviso: correo o texto. | 400 | Medio desconocido. |
| El consignatario no tiene un correo registrado: agréguelo antes de marcarlo. / … un teléfono registrado … | 400 | Falta el correo o teléfono en la ficha del consignatario. |
| El consignatario se dio de baja de los avisos por ese medio: no se puede volver a marcar. | 409 | Marcar a quien se dio de baja. |
| Indique el idioma de los avisos: español (es) o inglés (en). | 400 | Idioma distinto de es/en. |
| Consignatario no encontrado. | 404 | El consignatario no es de este cliente. |

## 7. Casos frecuentes
- *El consignatario no recibió nada:* revise que el cliente tenga el aviso prendido, que esté marcado para ese medio, que el correo esté en su ficha, que no se haya dado de baja y que el estatus sea de los que se avisan (§2).
- *El consignatario quiere dejar de recibir avisos:* que use el enlace «darse de baja» de cualquier correo; no se puede deshacer desde el portal.
