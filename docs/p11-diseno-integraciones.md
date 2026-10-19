# P11 — Diseño de Integraciones y API (para aprobación de Luis, 2026-10-19)

> Estado: **diseño presentado, nada construido.** Parte de lo ya hecho: plantillas de importación (Lotes 3 y 131), `ScheduledJob` + worker (P2.2), outbox de notificaciones (P2.1), `IntegrationMessageLog` (idempotencia), tablas `ApiCredential`, `WebhookSubscription` y `WebhookDelivery` ya creadas en el esquema (sin backend).

## 0. Decisiones de Luis (2026-10-19) — mandan sobre el resto del documento
1. **Orden de los lotes:** da igual; se hacen las dos partes (archivos y API). Se empieza por archivos.
2. **Cuenta de servicio propia por integración, creada al crear la integración.** Para crear una integración el usuario necesita un **permiso propio** (`integrations.create`; Luis se lo da a uno o varios roles). Quien la crea **no puede darle a la integración más permisos de los que él mismo tiene** (sin escalada de privilegios): al crearla solo se le ofrecen los alcances (scopes) que el creador posee y que la compañía tiene contratados.
3. **Bandeja:** los datos crudos se guardan 30 días y luego se purga el detalle (el motivo queda).
4. **La API es para los CLIENTES** y cada llave solo toca lo **de su propio cliente/compañía**. Alcances ofrecidos:
   - **Transporte:** leer órdenes, tracking y facturas; crear órdenes.
   - **Consignatarios (solo los propios):** leer, **agregar y editar**; **nunca borrar** (eso solo se hace en la aplicación).
   - **Remitente (sender) / perfil del cliente:** cada cliente puede leer y editar **sus propios datos**.
   - **Almacén (si la compañía tiene el módulo de almacén):** ver inventario, crear productos, despacho, recibo, ajustes y transferencias — sin órdenes de transporte en esta parte.
   - **Validación por módulo:** si la compañía no tiene el módulo de almacén, esos alcances **no se ofrecen** al crear la integración y los endpoints responden como módulo no contratado (`[RequireModule]`); la documentación de la API de cada cliente solo lista lo que su compañía tiene.
5. **Acuses = avisos del sistema**, no archivo de respuesta. Al crear la integración se define **a quién avisar** (usuarios o roles que se escojan) con la opción **«incluirme»**; no se avisa automáticamente a quien la creó.
6. **Correo:** un buzón por integración.

## 1. Principios (dictados de Luis)
1. **Integrar CUALQUIER cosa con Teikem, de entrada o de salida, por configuración, sin programar cada vez.** AxisCare es solo un ejemplo.
2. **API mínima y controlada:** más lecturas que escrituras; la escritura es **crear órdenes** (lo mismo que hace la pantalla) y poco más.
3. **Una integración = conector + disparador + pasos**, con credenciales cifradas y separadas.
4. **Nada se pierde en silencio:** todo lo que no se pudo procesar va a la **bandeja** con su motivo (sección 4).
5. Lo existente se reutiliza: las **plantillas de importación** son el formato de archivo declarativo; el **worker de trabajos** es el motor de horario; el **outbox** es el de salida.

## 2. Modelo

| Pieza | Qué es | Notas |
|---|---|---|
| **Conector** (`IntegrationConnector`) | Cómo se habla con el otro lado: `SFTP`, `FTP`, `CARPETA`, `CORREO` (buzón IMAP), `API` (HTTP saliente), `WEBHOOK_ENTRADA`, `SUBIDA_MANUAL`. Host, puerto, usuario, ruta. | **Secretos cifrados** con el mismo mecanismo del almacén de archivos (llave por compañía); se muestran una sola vez; nunca viajan al navegador; botón «Probar conexión». |
| **Integración** (`Integration`) | Nombre, cliente (o general), **dirección** (ENTRADA a Teikem / SALIDA de Teikem), conector, disparador, pasos, activa/inactiva. | Una compañía puede tener las que quiera; una por cliente típicamente. |
| **Disparador** | `HORARIO` (intervalo o diario con zona horaria, reutiliza `ScheduledJob`), `VIGILAR` (carpeta/SFTP/buzón: archivo nuevo), `MANUAL` (botón «Ejecutar ahora»), `WEBHOOK` (alguien llama), `EVENTO` (salida: la orden cambió de estatus). | Una sola corrida a la vez por integración (candado atómico como `ScheduledJob`). |
| **Paso** (`IntegrationStep`) | Ordenados: `LEER_ARCHIVO` → `APLICAR_PLANTILLA` → `CREAR_ORDENES`; salida: `ARMAR_ARCHIVO` → `ENVIAR`. | Hoy hay un solo tipo de paso de entrada (importar órdenes) porque es lo que Teikem sabe crear por archivo; los demás se agregan como tipos de paso, no como código por cliente. |
| **Corrida** (`IntegrationRun`) | Bitácora: inicio, fin, resultado, leídas / creadas / rechazadas, mensaje; una línea por archivo y por paso. | Pantalla «Historial». Falla repetida → aviso por correo (ya existe el mecanismo en `ScheduledJob`). |
| **Archivo procesado** (`IntegrationFileLog`) | Nombre, fecha, **hash SHA-256**, corrida. | Evita procesar dos veces el mismo archivo (mismo hash = «ya procesado», sin duplicar órdenes). |
| **Bandeja** (`IntegrationInboxItem`) | Lo que entró y **no se pudo procesar solo**. Ver sección 4. | Es la respuesta a la limitación del Lote 131. |

**Formato del archivo = la plantilla de importación existente** (posición de columna → campo, defaults, variantes y, desde el Lote 131, varias líneas por orden con reglas de «no pieza»). Se le agregan: codificación, hoja de Excel (.xlsx), y para salida un **mapeo campo de Teikem → columna** (el espejo). Para JSON/XML/API: mapeo por ruta (`$.order.id`) en una segunda fase.

## 3. Entrada y salida

**Entrada (a Teikem).** Disparador → leer del conector → por archivo: calcular hash (si ya está, saltar) → aplicar plantilla → **validar** (el mismo importador: errores por fila/orden) → las órdenes válidas **se crean** (`orders.create` de una cuenta de servicio de la integración, nunca de un usuario) y las inválidas **van a la bandeja**. **Acuse = aviso del sistema** a quienes se definieron en la integración (resumen de la corrida: creadas, rechazadas, a la bandeja); un archivo de respuesta hacia el cliente no se hace por ahora.

**Salida (de Teikem).** Dos formas: (a) **por evento/horario**: «cuando una orden pase a Entregada» o «cada noche las entregadas del día» → armar archivo con el mapeo de salida → enviarlo (SFTP, correo, API, webhook); (b) **webhook** de suscripción (sección 5). Una salida fallida se **reintenta con espera creciente** (outbox) y, agotados los reintentos, queda en la bandeja como «salida no entregada» con botón «Reintentar».

## 4. La bandeja (cómo se resuelve la limitación)

Hoy, una orden con error queda solo en la vista previa y desaparece al cerrarla. En una integración automática **nadie está mirando** la vista previa, así que se necesita un lugar persistente:

- **Qué entra:** (1) orden o fila con errores de validación (sin pieza, label repetido, consignatario sin dirección…); (2) fila que el mapeo no logró clasificar; (3) archivo ilegible o con columnas distintas a la plantilla; (4) salida no entregada tras los reintentos; (5) webhook recibido con firma inválida o cuerpo no interpretable (solo se registra, no se procesa).
- **Qué guarda:** integración, corrida, archivo y fila de origen, **los datos tal como llegaron** (cifrados si traen datos personales; se purgan a los N días configurables), el **motivo exacto** (mismo mensaje que da la vista previa) y el estatus: `PENDIENTE` → `CORREGIDA` / `DESCARTADA` / `REPROCESADA`.
- **Qué se puede hacer:** ver el motivo y los datos; **corregir en pantalla** los campos (el mismo editor de la vista previa) y **reprocesar** solo esa orden; **reprocesar todo** al cambiar la plantilla o el catálogo (ej. un consignatario que ya se creó); **descartar** con nota; **descargar** las pendientes como CSV para devolverlas al cliente.
- **Quién se entera:** contador en el menú y aviso del sistema a **quienes se definieron en la integración** (con «incluirme») cuando entran ítems nuevos (resumen por corrida, no uno por fila) y alerta operacional si un ítem lleva más de N horas pendiente (se enchufa a las alertas ya construidas en P12).
- **Reglas que no se rompen:** una orden de la bandeja **nunca crea una orden a medias**; reprocesar es idempotente (por número de orden y hash de archivo); el acuse al cliente lista lo aceptado y lo rechazado con su motivo.
- La **vista previa manual de hoy sigue igual**; es la misma lógica de validación, solo que en la integración el resultado se guarda.

## 5. API de Teikem (entrada por HTTP) y webhooks

**Llave por cliente** (`ApiCredential`): `client_id` + secreto → token de corta vida con **scopes**; el **secreto se muestra una sola vez y se guarda hasheado**; **solo ve lo de su cliente** (el filtro de tenant y el de cliente ya existen como `OrderScope`); **límite de uso** por llave (solicitudes por minuto) y bitácora (`IntegrationMessageLog`); se puede suspender o rotar. Idempotencia con la cabecera `Idempotency-Key` que ya existe.

**Endpoints** (bajo `/api/v1/integration/`; cada uno exige su alcance, que la compañía tenga el módulo y limita todo al cliente de la llave):
| Alcance | Endpoint | Qué hace |
|---|---|---|
| `orders:read` | `GET orders`, `GET orders/{id}`, `GET orders/{id}/tracking` | lista, ficha, historial de estatus y comprobante (firma/foto con enlace de caducidad) |
| `orders:create` | `POST orders` | crear una orden (lo mismo que la pantalla; con `Idempotency-Key`) |
| `invoices:read` | `GET invoices` | facturas del cliente y su estado |
| `consignees:read` / `consignees:write` | `GET/POST/PATCH consignees` | consignatarios propios: leer, agregar, editar. **Sin borrar** |
| `client:read` / `client:write` | `GET/PATCH client` | el cliente lee y edita su propio perfil (remitente) |
| `warehouse:read` | `GET inventory`, `GET products` | ver inventario y productos (solo con módulo de almacén) |
| `warehouse:write` | `POST products`, `POST receipts`, `POST dispatches`, `POST adjustments`, `POST transfers` | crear productos, recibo, despacho, ajustes y transferencias |
| `webhooks:manage` | `POST/GET/DELETE webhooks` | suscribirse a eventos (`order.status_changed`, `order.delivered`, `invoice.issued`) |

*(Se quitó «subir un archivo con la plantilla del cliente» por la API: era la variante de crear órdenes mandando un CSV en vez de JSON; no hace falta si `POST orders` existe y los archivos entran por los conectores de la sección 3. Si algún cliente lo necesita se agrega después.)*

**Webhooks de salida** (`WebhookSubscription`/`WebhookDelivery`): URL del cliente, **firma HMAC-SHA256** con secreto propio (cabecera con marca de tiempo contra repetición), cuerpo mínimo (id + estatus; los detalles se consultan por la API), **reintentos con espera creciente** (1 min, 5, 30, 2 h, 12 h) y registro de cada entrega; agotados, a la bandeja y suspensión automática de la suscripción tras N fallas seguidas.

**Fuera de la API (decidido):** cobros, tarifas, clientes, usuarios, choferes, rutas. «Procesos que se pueden disparar» queda en una lista cerrada (reconciliar COD y procesar facturas **no**; solo importar archivo y reintentar bandeja).

## 6. Seguridad y control
- Secretos de conector y de API: cifrados/hasheados, auditados (quién los vio o rotó), nunca en logs ni en el cliente web.
- Cuenta de servicio por integración con permisos mínimos (`orders.create` + el cliente); no usa usuarios humanos.
- Permisos nuevos: `integrations.view`, **`integrations.create`** (crear la integración y su cuenta de servicio), `integrations.manage`, `integrations.inbox` (y se propagan a los roles ya clonados, regla del proyecto); módulo `INTEGRATIONS`.
- SFTP: huella del servidor fijada; tamaño y filas máximas por archivo (como el CSV de hoy: 5.000 filas / 2 MB, configurable con tope); ningún archivo se ejecuta.
- Todo cambio de integración y toda corrida quedan en la auditoría; rate limit y bitácora por llave.

## 7. Pantallas
1. **Integraciones** (lista): estado, última corrida, pendientes en bandeja, «Ejecutar ahora», activar/desactivar.
2. **Editor** (asistente en 4 pasos): conector (+ probar) → qué hace (entrada/salida) → plantilla/mapeo (la pantalla de plantillas de hoy) → disparador y avisos.
3. **Historial** de corridas y archivos.
4. **Bandeja**: lista con motivo, corregir/reprocesar/descartar/descargar.
5. **Llaves de API y webhooks** (cliente y plantilla de documentación).
Tema claro/oscuro y es/en como todo; Kemi busca en el manual y la FAQ de este capítulo.

## 8. Plan por lotes (cada uno cierra con manual, FAQ, pruebas y recorrido)
| Lote | Contenido | Depende de |
|---|---|---|
| **132** | Modelo y pantalla **Integraciones** + conector **CARPETA** y **SUBIDA MANUAL** + disparador manual/horario + corrida e historial + **bandeja** (entrada) con corregir/reprocesar/descartar | Lotes 3, 131, `ScheduledJob` |
| **133** | Conectores **SFTP/FTP** y **CORREO** (IMAP), vigilar, hash de archivos procesados, acuse por archivo; secretos cifrados + probar conexión | 132 |
| **134** | **Llaves de API** por cliente (secreto una vez, scopes, rate limit) + lecturas de órdenes, tracking y facturas + `POST orders` | 132 (bitácora) |
| **135** | **Webhooks** de salida (HMAC, reintentos, bandeja de salidas no entregadas) y salida por evento/horario con mapeo inverso | 133, 134 |
| **136** | Mapeo por ruta para JSON/XML, hoja de Excel, API saliente genérica | 135 |

## 9. Por confirmar (quedan pocas)
1. **«Órdenes no» en almacén:** entendí que las órdenes de transporte siguen siendo parte de la API del cliente (leer y crear) y que lo que NO va en la parte de almacén son órdenes de venta/salida. ¿Correcto?
2. **Remitente:** entendí «sender» como el propio cliente (sus datos y su remitente), editable solo por sí mismo. ¿Es eso, o es otra entidad?
3. **Alcances por rol:** los alcances (`orders:read`, `warehouse:write`…) los elige quien crea la integración entre los que él tiene; ¿quieres además un permiso por familia (p. ej. `integrations.create.warehouse`) para que un rol pueda crear integraciones de transporte pero no de almacén?
