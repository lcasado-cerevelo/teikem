# Capítulo 9 — App de almacén (`app-almacen/`)

App instalable (Android, Expo) para operar el almacén desde una pistola o tablet: registrar el aparato, entrar con
un PIN corto, y capturar Recibir, Acomodar, Despacho, Conteo y Consultar sin depender de tener señal todo el tiempo.
Cubre las dos entregas del Lote 8A-app (primera: núcleo + Recibir; segunda: Acomodar, Despacho, Conteo, Consultar y
Sincronización). El backend que la sostiene (aparatos, PIN, sincronización por diferencia, código de barras,
operaciones atómicas) está documentado en el [capítulo 8A](08-aparatos-y-sincronizacion.md); este capítulo cubre las
pantallas reales de la app.

Decisiones de cada entrega, con más detalle técnico: `docs/lote8A-app-decisiones.md`.

Lote 16: **Recibir en un almacén "Directo a posición"** (sección 4: paso "Escanea la posición destino"), descarga de las **posiciones** del almacén y **heartbeat**
en cada pasada de sincronización (sección 9). Los números de la app llevan **coma de miles y punto decimal** (`61,023`; `1,250.5`), como en la web, sin depender de los datos de
idioma del aparato.

---

## 1. Cómo funciona sin señal

La app guarda una copia local de lo que necesita para trabajar (productos, órdenes de compra, avisos de llegada y, desde el Lote 16, las posiciones del
almacén del aparato) y se pone al día sola cada 60 segundos y al abrir (sincronización por diferencia, capítulo 8A §4). Cada pantalla se
comporta de una de estas dos maneras, nunca mezclada:

- **Documento propio del aparato** (Recibir, Despacho al recolectar, Conteo al capturar lo encontrado): se captura
  sin señal y se manda a la **cola de salida** al terminar. La cola manda cada operación en el orden en que se creó,
  con una clave de idempotencia (capítulo 8A §1) para que un reintento no la repita. Si una operación falla porque el
  servidor la rechaza (dato inválido, ya no aplica), queda "con error" para revisarla en Sincronización; si falla por
  falta de señal, se reintenta sola en la próxima pasada.
- **Recurso compartido con la web u otros aparatos** (la lista de tareas de Acomodar, resolver una posición
  escaneada a su id real, el consignatario al empacar un despacho, reclamar una posición para contarla, los saldos de
  Consultar): necesita señal en el momento, porque otra persona podría estar viendo o tocando lo mismo. Estas
  llamadas son directas (nunca van por la cola); sin señal, avisan y se pueden reintentar.

**Un documento a la vez por aparato.** Mientras haya un recibo, un despacho o un conteo en curso, Inicio bloquea las
demás acciones con un aviso (`Termina o cancela el recibo en curso antes de usar esto.` / despacho / conteo) — el
botón de la acción en curso la retoma en vez de bloquear. Acomodar y Consultar no manejan ningún documento propio, así
que cualquiera de los otros tres en curso también los bloquea a ellos. Cerrar y volver a abrir la app no pierde el
documento en curso: sigue guardado tal cual hasta que se confirma o se cancela.

---

## 2. Registrar el aparato y entrar

### 2.1 Registrar este aparato

Quién puede: nadie todavía (pantalla anónima, sin sesión) — necesita el **código de registro** de un solo uso que
un administrador crea en la web (`devices.manage`, capítulo 8A §2.1) y que vence en 24 horas.

Cómo se usa: primera pantalla que ve un aparato nuevo. Se escribe la dirección del API (por ejemplo
`http://192.168.1.20:5000/`, misma red que el servidor) y el código de 8 caracteres (en mayúsculas). "Registrar"
guarda la dirección del servidor y llama a `POST /api/v1/devices/enroll` (capítulo 8A §2.2); si funciona, pasa a
elegir usuario.

### Validaciones

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| Dirección del servidor sin `http://` o `https://` | `Escriba una dirección válida, por ejemplo http://192.168.1.20:5000/` | Local (antes de llamar al API) |
| Código de registro inválido, vencido o de un aparato desactivado | `El código de registro no es válido o venció.` | API, 401 (capítulo 8A §2.2) |
| El teléfono ya está registrado en la compañía de ese código (el código NO se gasta) | `Este teléfono ya está registrado en {compañía} como {código}. Pide al administrador un código de otra compañía.` | API, 409 |
| Sin conexión al servidor | `Ocurrió un error. Intente de nuevo.` | Local (`errors.generic`) |

### 2.2 Elegir usuario y entrar con PIN

Quién puede: cualquier usuario interno con PIN definido, membresía activa en la compañía del aparato y
`inventory.view` (los mismos requisitos de `POST /api/v1/auth/device/users`, capítulo 8A §3.3). El aparato ya
registrado se identifica solo (sin contraseña).

Cómo se usa: la lista de usuarios del aparato se pide sola al entrar a esta pantalla; tocar un nombre lleva al
teclado numérico del PIN (4 a 6 dígitos). "Entrar" llama a `POST /api/v1/auth/device/login`. Si el PIN es
incorrecto, se borra el PIN escrito y hay que volver a teclearlo (no vuelve a la lista).

### 2.3 Un teléfono en varias compañías (2026-09-30)

Un mismo teléfono puede trabajar en varias compañías (por ejemplo Advance Depot y Advance Solutions), **una vez en cada
una**:

- **Registrar otra compañía**: en "¿Quién eres?", el botón "Registrar otra compañía" abre la pantalla de registro; se
  teclea el código que dio el administrador de la otra compañía (Sistema → Aparatos). El registro se **agrega**; los
  anteriores siguen. "Volver" regresa sin registrar.
- **Al entrar**: con dos o más registros, primero sale "¿En qué compañía vas a trabajar?" con el nombre de cada compañía;
  al tocar una salen los usuarios con PIN de esa compañía (su nombre aparece arriba). Con un solo registro no se pregunta.
- **Cambiar de compañía**: botón "Cambiar de compañía" en "¿Quién eres?". Al salir un usuario ("Salir" en Inicio), el
  siguiente vuelve a elegir compañía.
- **Datos separados**: cada compañía tiene su propia base en el teléfono. Lo que quedó sin sincronizar en una compañía
  (conteos, movimientos) se queda en su base y se envía la próxima vez que alguien entre a esa compañía con señal; se puede
  cambiar de compañía aunque haya pendientes. La dirección del servidor, el idioma y el tema son del teléfono (comunes).
- **Repetido**: si el código es de una compañía en la que el teléfono ya está registrado, sale el aviso de la tabla de
  arriba y el código **no se gasta**; pida un código de otra compañía (o use el registro que ya existe).
- **Desactivado**: si el administrador desactiva el aparato en una compañía, solo se quita ese registro del teléfono; los
  de las otras compañías siguen.

### Validaciones

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| No hay usuarios con acceso a este aparato | `No hay usuarios con acceso a este aparato.` | Local, lista vacía |
| PIN incorrecto | `PIN incorrecto.` | API, 401 (capítulo 8A §3.3) |
| PIN bloqueado por intentos fallidos (5 seguidos, 15 minutos) | `PIN bloqueado por 15 minutos.` | API, 423 |
| El PIN lo asignó alguien que ya no tiene esos permisos | `Su PIN lo asignó otra persona que ya no tiene sus permisos; defina su propio PIN en Mi cuenta.` | API, 403 |
| No se pudo cargar la lista de usuarios (sin señal, error del servidor) | El título del error del servidor, o `Ocurrió un error. Intente de nuevo.` | API / local |

Los mensajes de PIN, bloqueo y permisos están explicados con más detalle (por qué pasan, qué hacer) en el
[capítulo 8A §3](08-aparatos-y-sincronizacion.md#3-pin-de-usuario-y-login-por-aparato) y en la
[FAQ del Lote 8A](faq.md#lote-8a--backend-de-la-app-de-almacén-aparatos-pin-idempotencia-sincronización-y-operaciones-atómicas).

---

## 3. Inicio

Qué hace: menú de 5 acciones (Recibir, Acomodar, Despacho, Conteo, Consultar), el estado de la sincronización
("N pendientes de enviar", "Todo enviado", o, en rojo, que la **última sincronización falló**: "No se pudo sincronizar. Toca 'Sincronizar ahora'." si no hay nada pendiente, o "N pendientes · no se pudo sincronizar"; desde 2026-10-01 ya no dice "0 con error", porque no mide elementos rechazados sino el resultado del último intento), un botón "Sincronizar ahora", y "Cambiar de usuario" (cierra la
sesión de este usuario en el aparato, sin desregistrarlo).

Quién puede: cualquiera que entró con PIN (§2.2). El menú no filtra por permiso fino: cada pantalla revisa el suyo
al llamar al API real y, si falta, el error del servidor se muestra tal cual (por ejemplo, sin `warehouse.pick` la
pantalla de Despacho igual abre, pero "Empacar" falla con el error del permiso).

Cómo se usa: tocar una acción navega a su pantalla, salvo que haya otro documento en curso (§1): entonces avisa en
vez de navegar. Tocar el texto del estado de sincronización lleva a la pantalla de Sincronización (§8).

---

## 4. Recibir

**Botón "Volver" (2026-10-01).** En las pantallas de inicio de Recibir, Despacho, Conteo y Consultar (cuando todavía no hay un
documento abierto), "Volver" va **al final de la pantalla**: en Recibir y Despacho pegado al borde inferior (debajo del texto de
ayuda); en Conteo y Consultar, después de todo lo que haya en pantalla (incluido el resultado de una consulta).

Qué hace: captura un recibo completo (documento o "recibo ciego", sin orden ni aviso) y lo manda al confirmarlo en
una sola llamada atómica (`POST /api/v1/receipts` con `confirm: true`, capítulo 8A §6 y
[06 §4](06-inventario-y-almacen.md#4-recepción-avisos-de-llegada-asn-y-recibos)). Funciona sin señal de punta a
punta: la llamada se manda de inmediato si hay conexión, o se encola para cuando la haya.

Quién puede: `inventory.view` (para el aparato en general); recibir contra una orden de compra exige además el
módulo **PURCHASING** y `purchasing.receive`, revisado por el servidor al confirmar (no antes).

Cómo se usa:
1. Se escanea la orden de compra o el aviso de llegada (busca en lo ya sincronizado localmente), o se toca "Recibo
   ciego" para recibir sin documento.
2. Se escanea cada producto (por código de barras o SKU, de lo ya sincronizado); según cómo se rastree
   (`trackingTypeCode`) pide cantidad, o lote (con vencimiento opcional), o números de serie uno por uno.
3. "Agregar" suma la línea a lo capturado; se puede quitar una línea ya agregada.
4. "Confirmar recibo" cierra el recibo y vuelve a Inicio. En un almacén **con acomodo** el servidor crea las tareas de acomodo; en uno **directo a posición**
   cada línea queda en su posición destino y no hay tareas (ver "Recibir en un almacén directo a posición", abajo).
   "Cancelar recibo" (con confirmación) descarta todo lo capturado sin mandar nada.

### Campos y validaciones

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| Código escaneado no es de una orden ni un aviso conocidos | `No se encontró una orden ni un aviso con ese código. Puede seguir con recibo ciego.` | Local (búsqueda en base local) |
| Código de producto no coincide con ninguno sincronizado | `No hay un producto con ese código.` | Local |
| Producto con rastreo por lote, sin número de lote o sin cantidad | Botón "Agregar" deshabilitado (`canAddLine`) | Local, sin mensaje |
| Producto con rastreo por serie, sin ningún número de serie capturado | Botón "Agregar" deshabilitado | Local, sin mensaje |
| Ya hay un recibo en curso en este aparato (intento de empezar otro) | `Ya hay un recibo en curso; hay que confirmarlo o cancelarlo antes de empezar otro.` | Local (`startLocalReceipt` lanza; la pantalla siempre retoma el abierto, nunca llega a mostrarlo) |
| Confirmar sin señal | Se guarda igual, se manda solo cuando haya conexión (`errors.network`) | Cola de salida |
| El servidor rechaza el recibo al confirmarlo (dato inválido, orden ya cerrada, etc.) | El mensaje exacto que el servidor devuelva, ver [06 §4](06-inventario-y-almacen.md#4-recepción-avisos-de-llegada-asn-y-recibos) | API, vía cola |

### Recibir en un almacén directo a posición (Lote 16)

Qué hace: si el almacén del aparato recibe **directo a posición** ([06 §1.4](06-inventario-y-almacen.md#14-modo-de-recepción-y-posición-de-recepción-por-defecto-lote-16)),
cada línea lleva la **posición donde se deja la mercancía** y, al confirmar, el servidor la asienta ahí, sin tareas de acomodo
([06 §4.1](06-inventario-y-almacen.md#41-recibo-directo-a-posición-lote-16)). El aparato sabe el modo del almacén porque el registro y el heartbeat lo traen
(`defaultWarehouseReceivingMode`). **El recibo guarda el modo que tenía el almacén cuando se empezó**: si el modo cambia a mitad de la captura, ese recibo sigue como empezó. Un
aparato que todavía no conoce el modo (no ha sincronizado desde que se actualizó la app) trabaja como antes: **con acomodo**.

Quién puede: `inventory.view`; la pista "Sugerida" también usa las sugerencias de acomodo del servidor. El permiso para recibir (`warehouse.receive`) lo revisa el servidor al confirmar.

Cómo se usa (en un almacén directo):
1. Se escanea el producto y se captura la cantidad, el lote o las series, como siempre. El botón pasa a **Siguiente** (en lugar de "Agregar").
2. Aparece el paso **Escanea la posición destino**, con el nombre del producto y la cantidad. Se escanea (o se escribe) la posición donde se deja la mercancía.
3. Si hay señal, arriba del campo aparece la pista **Sugerida: {posición}** (la primera sugerida por el servidor para ese producto y esa cantidad). **Nada se llena solo**: hay que escanear la
   posición, sea la sugerida o no. Sin señal, la pista no aparece y la captura sigue igual.
4. La app **valida sin señal** la posición contra las posiciones que tiene guardadas del almacén: que exista, que esté activa y que no sea de recepción (`STAGING`) ni de cruce de muelle
   (`CROSSDOCK`). La **cuarentena sí** vale. Si es válida, la línea se agrega y se ve con **→ {posición}** (por ejemplo, `8 SKU-1` con `LOTE-A · → R-02`).
5. **Volver** regresa a la cantidad; **Cancelar** descarta la línea que se estaba capturando.
6. **Confirmar recibo** se apaga mientras alguna línea no tenga posición destino. Bajo el botón dice: "Cierra el recibo y deja cada línea en su posición destino, sin tareas de acomodo."

**Dos destinos para el mismo producto (H11).** En un recibo **con aviso de llegada o con orden de compra**, cada línea del documento entra a **una sola** posición: el servidor rechazaría el recibo completo si el
mismo producto llega con dos posiciones distintas, y un envío rechazado en la cola **ya no se puede editar**. Por eso la app lo **bloquea antes**: al escanear la segunda posición (la línea no se agrega y sale el mensaje) y otra vez
al tocar "Confirmar recibo". Para dejar el mismo producto en dos posiciones, el recibo debe ser **ciego** o de **devolución** (o se transfiere la parte sobrante después).

Campos y validaciones (recibo directo):

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| Etiqueta del campo del paso de destino | `Escanea la posición destino` | Local |
| Pista con señal | `Sugerida: {bin}` | API (`GET /api/v1/warehouse-tasks/putaway-suggestions`, una sola sugerencia) |
| Línea ya agregada | `→ {bin}` | Local |
| La posición no existe en el almacén del aparato | `La posición no existe en este almacén.` | Local (posiciones descargadas) |
| La posición es de recepción o de cruce de muelle | `Esa posición es de recepción o de cruce de muelle; escanea dónde se guarda.` | Local |
| La posición está desactivada | `Esa posición está desactivada; escanea otra.` | Local |
| El aparato no tiene ninguna posición de este almacén guardada | `El aparato todavía no tiene las posiciones de este almacén. Sincroniza con señal e intenta de nuevo.` | Local |
| Mismo producto con dos destinos en un recibo con aviso u orden de compra (título del aviso al confirmar: `No se puede enviar así`) | `Ya se capturó {sku} con destino {bin}; en un recibo con aviso u orden de compra cada línea entra a una sola posición.` | Local (repite la regla del servidor) |
| Ayuda bajo "Confirmar recibo" | `Cierra el recibo y deja cada línea en su posición destino, sin tareas de acomodo.` | Local |
| El servidor rechaza el recibo al confirmarlo (posición dada de baja entre tanto, etc.) | El mensaje exacto del servidor: [06 §4.1](06-inventario-y-almacen.md#41-recibo-directo-a-posición-lote-16) | API, vía cola |

**Recibos abiertos antes de actualizar la app.** Un recibo que se empezó con la app anterior se manda sin modo y sin posiciones. El servidor, en un almacén directo, lo recibe **"Con acomodo"** (con tareas de acomodo): no se pierde.

### Estatus y casos frecuentes

El recibo local no tiene estatus propio: existe ("en curso") o no. Al confirmar, pasa a ser un recibo real del
servidor con su propio flujo de estatus (capítulo 6). Cancelar antes de confirmar no deja ningún rastro ni en el
servidor ni en el historial. Cerrar la app con un recibo en curso y volver a abrirla lo retoma exactamente donde
quedó (mismas líneas capturadas).

---

## 5. Acomodar

Qué hace: lista las tareas de acomodo (`PUTAWAY`) abiertas del almacén (mías primero), sugiere hasta 3 posiciones
por tarea, y completa la tarea al escanear la posición de destino. **Necesita señal de punta a punta**: la lista de
tareas y su asignación son de todo el almacén (recurso compartido, otros aparatos o la web pueden estar viendo o
tomando la misma tarea), así que no encola nada — cada acción es una llamada directa.

Quién puede: `inventory.view` (listar, ver sugerencias, empezar y completar una tarea); asignar o cancelar una tarea
manualmente (no disponible en esta pantalla) exige `warehouse.manage`.

Cómo se usa: tocar una tarea de la lista la marca "en curso" (`POST /warehouse-tasks/{id}/start`) y pide hasta 3
posiciones sugeridas; se escanea la posición real donde se dejó el producto (no tiene que ser la sugerida) y
"Completar" (`POST /warehouse-tasks/{id}/complete`) cierra la tarea. "Cancelar" en esta pantalla solo deja la tarea
sin completar (vuelve a la lista); no la cancela en el servidor.

### Campos y validaciones

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| No hay tareas de acomodo pendientes | `No hay tareas de acomodo pendientes.` | Local, lista vacía |
| Código escaneado no es de una posición del almacén | `No hay una posición con ese código.` | API (`GET /warehouses/{id}/bins?search=`), sin match exacto |
| No se pudo cargar la lista de tareas o completar la tarea (sin señal, error del servidor) | El título del error del servidor, o `No hay conexión con el servidor...` | API |

### Casos frecuentes

Ver la [FAQ](faq.md) para qué hacer si una tarea desaparece de la lista a mitad de acomodarla (otro aparato o la web
la completó primero).

---

## 6. Despacho (recolectar y empacar)

Qué hace: recolecta líneas (producto, cantidad, posición de origen) sin señal, y empaca (elige consignatario y
confirma) en una sola llamada atómica cuando ya hay señal (`POST /api/v1/pick-batches/collect-and-pack`, capítulo
8A §6 y [06 §7](06-inventario-y-almacen.md#7-recolección-y-empaque-ad-hoc-pick--pack)). Un despacho local a la vez
por aparato, con el mismo bloqueo real que Recibir (§1).

**Alcance de esta entrega: solo clientes 3PL.** El producto escaneado tiene que ser de inventario de un cliente 3PL
(`ownerClientPublicId` no vacío, ya sincronizado con el producto); despachar inventario propio del tenant no está
disponible aquí todavía (se completa en la web, decisión 3 de `docs/lote8A-app-decisiones.md`).

Quién puede: `warehouse.pick` para recolectar y empacar. Además, elegir el consignatario (paso "Empacar") pide la
lista de ubicaciones del cliente (`GET /api/v1/locations?clientId=`), que exige por separado `locations.read`: un
rol de aparato que tenga `warehouse.pick` pero no `locations.read` puede recolectar todo el despacho, pero
"Empacar" le fallará con el error de permiso del servidor al buscar consignatarios — conviene que el rol del
dispositivo tenga ambos permisos.

Cómo se usa:
1. Se escanea un producto (de un cliente 3PL); si ya hay un despacho en curso, el producto tiene que ser del mismo
   cliente que el despacho abierto.
2. Se captura cantidad y se escanea la posición de origen (texto libre: no se verifica contra el servidor en este
   paso, para poder recolectar sin señal); "Agregar" suma la línea.
3. "Empacar" resuelve todas las posiciones de origen escaneadas a su id real (una sola llamada por código distinto),
   busca los consignatarios del cliente, y pide la cantidad de bultos.
4. Elegir un consignatario y confirmar manda la recolección y el empaque juntos; si no hay señal en ese instante, se
   encola (misma cola de salida, mismas garantías de FIFO e idempotencia).
5. "Cancelar despacho" (con confirmación) descarta todo lo recolectado.

### Campos y validaciones

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| Código de producto no coincide con ninguno sincronizado | `No hay un producto con ese código.` | Local |
| Producto de inventario propio (no 3PL), o de un cliente distinto al del despacho en curso | `Esta pantalla solo despacha inventario de clientes 3PL por ahora; para inventario propio se completa en la web.` | Local |
| Cantidad o posición de origen vacías | Botón "Agregar" deshabilitado (`canAddPickLine`) | Local, sin mensaje |
| Ya hay un despacho en curso (intento de empezar otro) | `Ya hay un despacho en curso; hay que confirmarlo o cancelarlo antes de empezar otro.` | Local (la pantalla siempre retoma el abierto) |
| Una posición escaneada al recolectar no existe al resolverla en "Empacar" | `No hay una posición con ese código.` + los códigos que no se encontraron | API, al empacar |
| No se pudieron buscar los consignatarios del cliente (sin señal, falta `locations.read`, etc.) | `No se pudo consultar los consignatarios (necesita señal).`, o el título del error del servidor | API |
| El cliente no tiene consignatarios en el sistema | `Este cliente no tiene consignatarios en el sistema.` | API, lista vacía |
| Confirmar sin señal | Se guarda igual, se manda cuando haya conexión | Cola de salida |
| El servidor rechaza el despacho al confirmarlo (crédito excedido, etc.) | El mensaje exacto del servidor, ver [06 §7](06-inventario-y-almacen.md#7-recolección-y-empaque-ad-hoc-pick--pack) | API, vía cola |

### Estatus y casos frecuentes

Igual que Recibir: el despacho local no tiene estatus propio hasta confirmarse; cancelar antes de empacar no deja
rastro en el servidor. Cerrar la app con un despacho en curso lo retoma tal cual, incluidas las líneas ya
recolectadas (la posición de origen sigue guardada como texto, se vuelve a resolver recién al empacar).

---

## 7. Conteo

Qué hace: cuenta una posición del almacén. Escanear la posición **reclama el conteo en el servidor**
(`POST /api/v1/cycle-counts`, capítulo 8A §6): la posición es un recurso compartido (nadie más puede contarla
mientras esté abierta), así que este primer paso necesita señal. De ahí en adelante, capturar lo encontrado es
local y "Terminar esta posición" encola el lote capturado y el cierre, en ese orden (dos filas en la cola de
salida). Un conteo a la vez por aparato.

Quién puede: `warehouse.count.capture` para abrir, capturar y terminar un conteo (implícito en `warehouse.count`).
**Cancelar un conteo en curso** (`DELETE /api/v1/cycle-counts/{id}`) exige el permiso completo `warehouse.count`,
no solo `.capture`: quien solo tiene `warehouse.count.capture` (conteo a ciegas) puede terminarlo con lo que contó,
pero no cancelarlo — tocar "Cancelar conteo" le devolverá el error de permiso del servidor. Conviene que el rol de
conteo a ciegas sepa esto, o que se le dé `warehouse.count` si necesita poder cancelar.

**Conteo a ciegas**: si quien entró no tiene `warehouse.count` (solo `.capture`), el servidor no manda las
cantidades esperadas (`systemQty` llega vacío) y no se puede reconciliar desde aquí; la pantalla muestra el aviso
"Conteo a ciegas: no se muestran las cantidades del sistema." y simplemente no imprime nada donde iría el
esperado. La reconciliación (comparar lo contado contra lo esperado y decidir el ajuste) se hace en la web
([06 §6](06-inventario-y-almacen.md#6-conteo-cíclico-modo-informado)).

Cómo se usa:
1. Se escanea la posición a contar; si no hay conteo abierto en esa posición, se abre uno nuevo y muestra lo
   esperado (o nada, si es a ciegas).
2. Se escanea cada producto encontrado: si coincide con una línea esperada, se captura la cantidad para esa línea
   (se puede volver a escanear y corregir); si no estaba en la lista, se agrega como línea extra.
3. "Terminar esta posición" encola el lote capturado y el cierre del conteo, y vuelve a Inicio. "Cancelar conteo"
   (con confirmación) libera la posición en el servidor sin guardar lo capturado.

Si se cerró y se volvió a abrir la app con un conteo en curso, la lista de líneas esperadas se vuelve a pedir al
servidor (no se guarda localmente, solo el id del conteo y lo ya capturado); esta pantalla ya necesita señal en ese
momento de todas formas.

### Campos y validaciones

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| Código escaneado no es de una posición del almacén | `No hay una posición con ese código.` | API |
| No se pudo abrir el conteo de esa posición (sin señal, posición ya siendo contada por otro) | `No se pudo abrir el conteo de esa posición (necesita señal).`, o el título del error del servidor | API |
| Código de producto no coincide con ninguno sincronizado | `No hay un producto con ese código.` | Local |
| Ya hay un conteo en curso (intento de empezar otro) | `Ya hay un conteo en curso; hay que terminarlo o cancelarlo antes de empezar otro.` | Local |
| No se pudieron volver a pedir las líneas esperadas al reabrir la app (sin señal) | El título del error del servidor, con botón "Reintentar" | API |
| Cancelar sin `warehouse.count` (solo conteo a ciegas) | El error de permiso del servidor (403) | API |
| Cancelar sin señal | `No se pudo cancelar (necesita señal); se puede intentar de nuevo.` | Local, tras error de red |
| Terminar sin señal | Se guarda igual, se manda cuando haya conexión (dos filas en la cola) | Cola de salida |

### Estatus y casos frecuentes

El conteo existe en el servidor desde que se escanea la posición (aunque no se haya capturado nada todavía); por
eso "Cancelar" es una llamada al servidor, no solo un descarte local. Terminar sin conexión dos veces seguidas no
duplica nada: la clave de idempotencia de cada fila de la cola protege el reintento.

---

## 8. Consultar

Qué hace: un solo campo para buscar saldos, por producto (código de barras o SKU, ya sincronizado) o por posición
(código libre): `GET /api/v1/inventory/balances`. Guarda la última respuesta de cada código consultado
(`balance_cache`, local) para poder responder "de hace N minutos" si se repite la misma búsqueda sin señal.

Quién puede: `inventory.view`.

Cómo se usa: se escanea o escribe un código. Si coincide con un producto ya sincronizado, busca el saldo de ese
producto en todo el almacén (por posición, lote); si no, lo manda como texto libre (cubre también un código de
posición) y el servidor decide la coincidencia (SKU, nombre, código de barras, lote o código de posición, capítulo
8A). Cada fila muestra posición, lote (si aplica), lo que hay en mano y lo disponible.

### Campos y validaciones

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| No hay nada con ese código | `No hay nada con ese código.` | API, lista vacía |
| Sin señal y sin una consulta anterior de ese mismo código | `Sin señal y sin una consulta anterior de esto.` | Local (caché vacía para esa clave) |
| Sin señal, pero hay una consulta anterior guardada de ese mismo código | `Datos de hace {N} min (sin señal ahora)`, con los datos de esa consulta anterior | Local (`balance_cache`) |

La clave de caché es por almacén y por código exacto (mayúsculas, sin espacios extra): consultar el mismo producto
por dos códigos distintos (SKU una vez, código de barras otra) guarda dos entradas separadas.

---

## 9. Sincronización

**Órdenes de compra sin permiso (2026-10-01).** La bajada de datos para Recibir incluye las órdenes de compra, que exigen
`purchasing.view` y el módulo Compras. Un usuario sin ese permiso (por ejemplo, el Operador de almacén desde que se le quitaron los
permisos de compras) recibe 403 en ese recurso: la app **lo salta** (no recibe contra órdenes de compra, sí contra avisos y recibo
ciego), borra las órdenes que tuviera guardadas y **sigue** con avisos y posiciones. Antes ese 403 abortaba toda la pasada y
Inicio mostraba "No se pudo sincronizar" siempre. Un error de red o del servidor (500) sí sigue marcando la pasada como fallida.

Qué hace: muestra lo que está en la cola de salida, separado en **pendientes** (se van a mandar solas en la próxima
pasada) y **con error** (el servidor las rechazó; hay que revisarlas), el resultado de la última pasada, y
"Sincronizar ahora" para forzar una pasada de inmediato.

Quién puede: cualquiera que entró con PIN; no depende de ningún permiso fino (solo muestra lo que ya está guardado
en este aparato).

Cómo se usa: cada fila "con error" muestra el tipo de operación y el mensaje que devolvió el servidor; se puede
"Reintentar" (vuelve a quedar pendiente, se manda en la próxima pasada) o "Descartar" (se borra sin mandar nunca).
Las pendientes no tienen esas acciones: se mandan solas cuando haya señal (cada 60 segundos, o al tocar
"Sincronizar ahora").

**Qué pasa en cada pasada con señal (Lote 16).** En orden: (1) se manda la cola de salida; (2) si hubo señal, se manda el **heartbeat** (aviso de vida, capítulo 8A §2.2), que actualiza el
almacén por defecto, el tema y el **modo de recepción** del almacén del aparato; si el servidor responde que el aparato está desactivado, se borra la identidad del aparato (hay que registrarlo de nuevo); (3) se bajan productos, órdenes de compra, avisos de llegada y **las posiciones del almacén por defecto** del aparato. Antes del Lote 16 el heartbeat no lo llamaba nadie.
Las posiciones sirven para validar sin señal la posición destino del recibo directo: la primera vez baja la lista completa (en Advance Depot, unas 3.886 posiciones, en 8 páginas) y después solo los cambios;
las posiciones dadas de baja se conservan marcadas como inactivas (así la app distingue "no existe" de "está desactivada"). Si el almacén por defecto del aparato cambia, el nuevo baja completo la primera vez.

### Casos frecuentes

Una operación queda "con error" cuando el servidor la rechazó por una razón que reintentarla igual no arregla
(dato inválido, un documento que ya no existe, un permiso que cambió) — nunca por falta de señal, eso la deja
"pendiente" en cambio. Antes de reintentar, conviene revisar el mensaje: si el problema sigue (por ejemplo, "esta
posición ya no tiene ese producto"), reintentar va a fallar otra vez con el mismo error.

---

## 10. Permisos usados por la app (resumen)

| Permiso | Qué habilita en la app |
|---|---|
| `inventory.view` | Entrar con PIN (capítulo 8A §3.3), Recibir (incluida la pista "Sugerida" del recibo directo y la descarga de posiciones), Acomodar (listar/completar tareas), Conteo (ver, no reconciliar), Consultar |
| `purchasing.receive` + módulo **PURCHASING** | Recibir contra una orden de compra (sin esto, solo recibo ciego funciona) |
| `warehouse.pick` | Recolectar y empacar en Despacho |
| `locations.read` | Buscar los consignatarios de un cliente al empacar un despacho |
| `warehouse.count.capture` (implícito en `warehouse.count`) | Abrir, capturar y terminar un conteo (a ciegas si falta `warehouse.count`) |
| `warehouse.count` | Ver las cantidades esperadas de un conteo (no a ciegas) y **cancelarlo** |

Todas estas rutas viven bajo el módulo **WMS_LOTSERIAL** (capítulo 8A §7); sin el módulo encendido para la
compañía, el aparato no puede ni listar sus usuarios para entrar.

---

## Marca

Desde el lote F8a la app lleva la marca Teikem: icono (con versión monocroma para los temas de Android 13+), pantalla de arranque azul `#0B2C66` y el logotipo con el lema en el idioma activo arriba de Registrar y Entrar ([icono](frontend/img/f8a-app-icono.png), [arranque](frontend/img/f8a-app-splash.png), [Registrar](frontend/img/f8a-app-registrar.png)); los archivos se regeneran con `npm run brand:icons` desde `Logos/`.

---

## 11. Preguntas frecuentes

Ver la sección **Lote 8A** de [`faq.md`](faq.md) para el backend (aparatos, PIN, idempotencia, sincronización) y
los casos propios de la app agregados en este lote (qué pasa si se pierde la señal a mitad de un recibo, por qué
Despacho solo funciona con clientes 3PL, qué significa "con error" en Sincronización).
