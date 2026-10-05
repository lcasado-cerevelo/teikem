# Capítulo 9 — App de almacén (`app-almacen/`)

App instalable (Android, Expo) para operar el almacén desde una pistola o tablet: registrar el aparato, entrar con
un PIN corto, y capturar Recibir, Acomodar, Despacho, Conteo y Consultar sin depender de tener señal todo el tiempo.
Cubre las dos entregas del Lote 8A-app (primera: núcleo + Recibir; segunda: Acomodar, Despacho, Conteo, Consultar y
Sincronización). El backend que la sostiene (aparatos, PIN, sincronización por diferencia, código de barras,
operaciones atómicas) está documentado en el [capítulo 8A](08-aparatos-y-sincronizacion.md); este capítulo cubre las
pantallas reales de la app.

Decisiones de cada entrega, con más detalle técnico: `docs/lote8A-app-decisiones.md`.

Lote 16: **Recibir en un almacén "Directo a posición"** (sección 4: paso "Escanea la posición destino"), descarga de las **posiciones** del almacén y **heartbeat**
en cada pasada de sincronización (sección 9).

Lote A3 (2026-10-03, `docs/mobile/loteA3-decisiones.md`): **mejoras de uso del Zebra** (lector sin teclado y "escanear = Aceptar"
en todas las pantallas, margen para la barra de navegación, Inicio en dos columnas, letras más grandes, tocar un producto en el
conteo) y **formatos de la compañía** (números, fechas, horas y zona horaria de Región y formatos, como en la web): secciones
1.1 y 1.2. Desde este lote los números ya no llevan una coma de miles fija: llevan los separadores de la compañía (Puerto Rico:
`61,023`; `1,250.5`).

Lote A4 (2026-10-03, `docs/mobile/loteA4-decisiones.md`): **Contar por producto** en Conteo (sección 7.1): se escanea el producto,
la app lista las posiciones (y lotes) donde el sistema dice que está, con un espacio para la cantidad (en blanco = 0), y "Otra
posición" para lo hallado donde el sistema no tenía nada.

Lote A5 (2026-10-03, `docs/mobile/loteA5-decisiones.md`, decisiones del dueño 4 y 5): al **contar por producto**, Confirmar exige al
menos una posición con un número escrito (0 vale; sección 7.1, paso 5); en **Despacho**, la cantidad va primero: escanear la posición
sin cantidad no agrega la línea y avisa (sección 6, paso 2).

Lote A6 (2026-10-03, `docs/mobile/loteA6-decisiones.md`, pendiente del cambio 2 de las decisiones del dueño): en **Sincronización**, una
captura de conteo que el servidor rechazó porque **el supervisor ya corrigió una línea** (409) se explica aparte, en grande, con los
renglones y SKU afectados, qué hacer y el botón **Actualizar el conteo** (sección 9.1).

Lote A8 (2026-10-03, `docs/mobile/loteA8-decisiones.md`): en **Consultar**, escanear una **posición** muestra en vivo la lista de lo que el
sistema dice que hay en ella (SKU grande, nombre, lotes; cantidades solo con `warehouse.count`), complemento de la hoja impresa de la
posición (sección 8.1).

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

### 1.1 El lector del Zebra: escanear = Aceptar (Lote A3)

En toda pantalla con campo de escaneo (Recibir, Acomodar, Despacho, Conteo —posición y producto— y Consultar), **escanear
equivale a escribir el código y tocar "Aceptar"**: la pantalla avanza sola, sin un "Continuar" después. Los pasos de **cantidad**
siguen siendo manuales; en **Despacho** la cantidad se escribe **antes** de escanear la posición (Lote A5, §6). Detalle por pantalla
en la tabla de `docs/mobile/loteA3-decisiones.md`.

- **Sin teclado en pantalla**: el campo de escaneo ya no abre el teclado al enfocarse ni al escanear. Para escribir un código a
  mano, el botón pequeño **"⌨"** junto al campo muestra el teclado (tocarlo otra vez lo esconde); luego **Aceptar** o Enter. El
  teclado físico del Zebra escribe en el campo sin abrir el de pantalla.
- **Perfil del lector**: al abrir una pantalla de escaneo, la app configura en DataWedge (el programa del lector de Zebra) el
  perfil **`TeikemAlmacen`**: lector encendido, lecturas entregadas a la app ("intent") y **salida por teclas apagada** (si la
  salida por teclas quedara encendida, cada lectura se escribiría como teclas en el campo enfocado y abriría el teclado). El
  estado se ve en Sincronización (§9: "Lector: …").
- **Avisos al escanear**: "no encontrado" sale en un **bloque rojo grande**; lo agregado o terminado, en un **bloque verde**.
  Se quedan en pantalla hasta la siguiente lectura o acción.
- **Lectura repetida**: si el aparato manda la misma lectura dos veces en menos de 400 ms, se toma una sola vez.
- **Margen inferior**: la app deja abajo un margen de al menos 40 dp para que la barra de navegación del aparato (3 botones o
  gestos) no tape el último botón. La barra no se oculta.

### 1.2 Formatos de la compañía (Lote A3)

Las fechas, horas, números y dinero de la app salen de **Región y formatos** de la compañía (web: Ajustes de la compañía,
[F9](frontend/f9-ajustes-de-la-compania.md)), igual que en la web; el **idioma** del aparato solo decide los textos (por ejemplo
"p. m." o "PM").

- **Cuándo se actualizan**: al entrar con el PIN y en cada sincronización con señal (`GET /api/v1/tenant/settings`; no pide
  permiso fino). Un cambio hecho en la web llega a la app en la **siguiente sincronización** (como mucho un minuto con Inicio
  abierto, o al tocar "Sincronizar ahora").
- **Sin señal**: se guardan en el aparato (en la base de esa compañía), así que siguen valiendo sin señal y al reabrir la app.
  Si el aparato nunca los recibió (recién registrado y sin señal), usa los de **Puerto Rico**: MM/DD/AAAA, 12 horas, coma de
  miles y punto decimal, `$` antes, zona `America/Puerto_Rico`.
- **Zona horaria**: las horas en pantalla (última sincronización, hora de los datos guardados en Consultar) y lo que es "hoy"
  se cuentan en la zona de la compañía, no en UTC ni en la del aparato. Por ejemplo, a la 1:30 UTC del 3 de octubre, en
  Puerto Rico sigue siendo el 2 de octubre a las 9:30 p. m.
- **Varias compañías**: cada compañía del teléfono tiene sus propios formatos.
- **Teléfonos**: hoy la app no muestra ni pide teléfonos; cuando lo haga, se guardarán solo los dígitos y se mostrarán con la
  máscara de la compañía.

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

Qué hace: menú de 5 acciones (Recibir, Acomodar, Despacho, Conteo, Consultar) en **dos columnas** de botones altos (icono
arriba, nombre debajo; la quinta a todo el ancho; Lote A3), el estado de la sincronización
("N pendientes de enviar", "Todo enviado", o, en rojo, que la **última sincronización falló**: "No se pudo sincronizar. Toca 'Sincronizar ahora'." si no hay nada pendiente, o "N pendientes · no se pudo sincronizar"; desde 2026-10-01 ya no dice "0 con error", porque no mide elementos rechazados sino el resultado del último intento), un botón "Sincronizar ahora", y "Cambiar de usuario" (cierra la
sesión de este usuario en el aparato, sin desregistrarlo). La pantalla se desplaza si el aparato es más corto: Sincronizar y
Cambiar de usuario quedan debajo de las acciones.

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
| Vencimiento (opcional) escrito con una fecha que no existe (Lote A3: se escribe en el orden de fecha de la compañía, el ejemplo del campo lo muestra —Puerto Rico `MM/DD/AAAA`—; también se acepta `AAAA-MM-DD`) | `La fecha no es válida. Escríbela así: {formato}` y "Agregar" deshabilitado | Local |
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
posiciones sugeridas; se escanea la posición real donde se dejó el producto (no tiene que ser la sugerida) y la lectura
cierra la tarea (`POST /warehouse-tasks/{id}/complete`); de vuelta en la lista sale el aviso verde `Listo: {sku} quedó en {bin}`
(Lote A3). "Cancelar" en esta pantalla solo deja la tarea
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
2. **Primero la cantidad, después la posición** (Lote A5, decisión del dueño). La cantidad viene **vacía** (ya no viene en 1) y
   debajo se lee `Escribe la cantidad y luego escanea la posición: la línea se agrega sola.` Con la cantidad escrita (mayor que 0),
   **la lectura de la posición agrega la línea al instante** (escanear = Aceptar; escribirla con ⌨ y tocar Aceptar hace lo mismo),
   con el aviso verde `Agregado: {cantidad} {sku} desde {posición}`. La posición es texto libre: no se verifica contra el servidor
   en este paso, para poder recolectar sin señal.
   - **Sin cantidad**: escanear la posición **no agrega nada**; sale el aviso rojo grande `Escribe la cantidad primero y luego
     escanea la posición.` y el cursor pasa a la cantidad. Se escribe la cantidad y se vuelve a escanear la posición.
   - **Cantidad 0 o que no es un número**: tampoco agrega; aviso `La cantidad debe ser un número mayor que 0. Corrígela y vuelve a
     escanear la posición.`
   - Ya no hay botón "Agregar" (lo agrega la lectura); "Cancelar" deja la línea sin agregar. Una línea agregada por error se quita
     con ✕.
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
| Escanear la posición sin cantidad escrita | `Escribe la cantidad primero y luego escanea la posición.` (no se agrega nada; el cursor pasa a la cantidad) | Local |
| Escanear la posición con cantidad 0, negativa o que no es un número | `La cantidad debe ser un número mayor que 0. Corrígela y vuelve a escanear la posición.` (no se agrega nada) | Local |
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

Desde el Lote A4 la pantalla empieza con **"¿Cómo vas a contar?"**: **Por posición** (lo de siempre, descrito abajo) o
**Por producto** (sección 7.1). La app recuerda la última forma elegida; la primera vez, por posición. Si en "Por posición" se
escanea un producto, el aviso dice `Ese código es de un producto. Para contarlo así, toca «Por producto».`

Qué hace (por posición): cuenta una posición del almacén. Escanear la posición **reclama el conteo en el servidor**
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
   (se puede volver a escanear y corregir); si no estaba en la lista, se agrega como línea extra. **Producto sin etiqueta**
   (Lote A3): tocar el producto en "Lo que se espera aquí" pone su código (SKU) en el campo del producto; se confirma con
   **Aceptar** y pasa a la cantidad. Tocarlo no cambia lo que se muestra: las cantidades esperadas siguen saliendo solo a quien
   tiene `warehouse.count`.
3. "Terminar esta posición" encola el lote capturado y el cierre del conteo, y vuelve a Inicio. "Cancelar conteo"
   (con confirmación) libera la posición en el servidor sin guardar lo capturado.

Si se cerró y se volvió a abrir la app con un conteo por posición en curso, la lista de líneas esperadas se vuelve a pedir al
servidor (no se guarda localmente, solo el id del conteo y lo ya capturado); esta pantalla ya necesita señal en ese
momento de todas formas. (Un conteo por producto, en cambio, se retoma sin señal: ver 7.1.)

### 7.1 Contar por producto (Lote A4)

Qué hace: cuenta **un producto en todas las posiciones** donde el sistema dice que está (pensado para cuando las posiciones no
están bien etiquetadas). La app crea el conteo en el servidor (`POST /api/v1/cycle-counts` con el producto, sin posiciones y con `allowEmpty: true`;
origen "Por producto") y lista **una fila por posición** (y por lote, si el producto lleva lote: el número de lote se muestra en la fila; no
se busca ni se captura por lote). El operario anota lo que encuentra en cada una; la diferencia la calcula el sistema al reconciliar
en la web ([06 §6](06-inventario-y-almacen.md#6-conteo-cíclico-modo-informado), revisión rápida "Por revisar").

Quién puede: `warehouse.count.capture` (abrir, capturar, confirmar y crear una posición en "Otra posición"); `inventory.view` para
la lista de zonas (ya hace falta para entrar). Cancelar exige `warehouse.count`, como en el conteo por posición. Módulo
**WMS_LOTSERIAL**.

Cómo se usa:
1. Conteo → **Por producto** → escanear (o escribir con ⌨ y Aceptar) el código de barras o SKU del producto. Necesita señal.
   - Producto con **número de serie**: aviso `Este producto se cuenta por número de serie; cuéntalo desde la web por ahora.` y **no se
     crea ningún conteo** (la app no captura series).
   - Producto **sin existencia** en ningún lado del almacén: el conteo se abre **vacío** y la pantalla muestra el bloque `El sistema no
     tiene existencia de este producto. Si lo encontraste en alguna posición, usa «Otra posición».` con el botón grande **Otra posición**
     (paso 4). Se sigue igual que con cualquier producto: cantidad en la posición agregada y Confirmar.
2. La lista: cada fila dice la **posición**, el **lote** si lo lleva, "Pendiente de revisión" si la posición es provisional y
   "Esperado: N" **solo si** quien cuenta tiene `warehouse.count` (a ciegas no se muestra, como siempre). A la derecha, el espacio
   para la cantidad. **Lo que se deja en blanco se toma como 0.** No hay botón "todo aquí".
3. Con **más de 6 posiciones** aparece **Buscar posición o lote** (filtra la lista; no cambia lo que se manda).
4. **Otra posición** (enlace al final de la lista), para lo hallado donde el sistema no tenía nada: elegir la **zona**, escribir el
   **código** de la posición o sus partes (**pasillo, rack, nivel, posición**: se une como `A01-R02-N3-P04`) y, si el producto lleva
   lote, el **número de lote** (obligatorio) y su **vencimiento** (opcional, en el orden de fecha de la compañía). **Agregar
   posición** la crea en el servidor (`POST /api/v1/cycle-counts/{id}/bins`, necesita señal) como **provisional**: queda "pendiente de
   revisión" hasta que el supervisor la confirma en la web. La fila entra a la lista con su espacio de cantidad. Si el código ya
   existe en el almacén, se usa esa posición (sin marca provisional). Escanear la etiqueta de una posición en este paso escribe su
   código. Una fila de "Otra posición" se puede quitar con ✕ antes de confirmar. Si la posición ya existía y el servidor la tiene
   "pendiente de revisión" (la sincronización trae la marca), la fila también la muestra.
5. **Confirmar**: encima del botón, mientras haya espacios en blanco, se lee `{n} posiciones en blanco se toman como 0.`; un toque en
   Confirmar lo acepta y cierra (sin diálogo). **Hace falta al menos una posición con un número escrito** (Lote A5, decisión del
   dueño; **0 vale**, y también cuenta una fila de "Otra posición" con su cantidad): con **todas** en blanco, Confirmar no manda nada y
   sale el aviso rojo grande `Escribe al menos una cantidad. Si no hay nada de este producto, escribe 0 en una posición.`; el aviso se
   quita al escribir cualquier cantidad. Se encola el lote con **todas** las filas (las en blanco como 0, las de "Otra
   posición" como líneas nuevas con su posición y lote) y el cierre, y vuelve a Inicio. Funciona sin señal. Si el conteo quedó **vacío** (producto
   sin existencia y ninguna fila agregada), Confirmar no manda nada y avisa `No se puede terminar un conteo vacío: agrega la posición donde lo encontraste con «Otra posición» o cancela el conteo.`; se sale con **Cancelar conteo** (paso 7).
6. **Retomar**: lo escrito se guarda en el aparato a cada cambio. Si se cierra la app (o se apaga el aparato), al volver a Conteo
   aparece la misma lista con lo ya escrito, sin necesitar señal. Un conteo a la vez por aparato (de posición o de producto).
7. **Cancelar conteo** (con confirmación): igual que el de posición (`DELETE`, necesita señal y `warehouse.count`).

#### 7.2 Contar varios productos en un conteo (Lote 25)

Desde el 2026-10-05, **Por producto** abre **un solo conteo** al que se le van agregando productos (en vez de crear un conteo por producto). El
conteo de "un producto en todas sus posiciones" de la sección 7.1 ya no se abre desde la pantalla; un conteo de ese tipo que haya quedado abierto
en un aparato se retoma igual que antes.

Quién puede: `warehouse.count.capture`; cancelar exige `warehouse.count`. Módulo **WMS_LOTSERIAL**.

Cómo se usa:
1. Conteo → **Por producto** → escanear el primer producto. Se abre el conteo **vacío** en el servidor (necesita señal) y la pantalla pasa a la lista
   **Lo contado**. Un producto con **serie** no se cuenta desde la app: `Este producto se cuenta por número de serie; cuéntalo desde la web por ahora.`
2. Por cada producto escaneado la app pregunta al servidor **dónde está** (sin cantidades, también a ciegas):
   - **una posición** con existencia: sale grande como **Posición** con el enlace **Cambiar posición**; solo falta la **cantidad** y **Agregar**;
   - **varias**: `El producto está en varias posiciones: elige en cuál lo contaste.` y una lista de botones (posición · lote · zona);
   - **ninguna** (o sin señal): `El sistema no tiene este producto en ninguna posición. Escanea la posición donde lo encontraste.` /
     `Sin señal: no se pudo buscar la posición del producto. Escanea la posición donde lo contaste.` Se escanea la etiqueta (se busca primero en las posiciones
     del aparato, sin señal) o se usa **Otra posición** (crea una provisional, necesita señal; en productos con lote pide el número de lote).
   - **Cambiar posición**: elegir otra de la lista, escanear otra o **Otra posición**. En productos con lote sin lote conocido se escribe el **número de lote**.
3. **Agregar** guarda la línea en el aparato (`SKU agregado en A-01.`). El producto se puede escanear otra vez en **otra** posición (otra línea). En la **misma**
   posición y lote abre la línea ya contada para **corregir** la cantidad (`SKU ya estaba contado en A-01: corrige la cantidad.`): no se duplica ni se suma. Tocar una
   línea de la lista la corrige; ✕ la quita.
4. **Terminar conteo** (se enciende con al menos una línea) encola **un lote** con todas las líneas (posición + producto + cantidad + lote por número) y el cierre;
   funciona sin señal y vuelve a Inicio. La diferencia se calcula al reconciliar en la web.
5. **Retomar**: lo contado se guarda en el aparato; al cerrar y abrir la app el conteo sigue ahí, sin señal. Un conteo a la vez por aparato.
6. **Cancelar conteo** (con confirmación): `DELETE`, necesita señal y `warehouse.count`.

| Caso | Mensaje exacto | Origen |
|---|---|---|
| Código sin producto sincronizado | `No hay un producto con ese código.` | App |
| Producto con serie | `Este producto se cuenta por número de serie; cuéntalo desde la web por ahora.` | App |
| Abrir el conteo sin señal | `No se pudo abrir el conteo de ese producto (necesita señal).` | App |
| Varias posiciones | `El producto está en varias posiciones: elige en cuál lo contaste.` | App |
| Sin existencia en ninguna posición | `El sistema no tiene este producto en ninguna posición. Escanea la posición donde lo encontraste.` | App |
| Sin señal al buscar dónde está | `Sin señal: no se pudo buscar la posición del producto. Escanea la posición donde lo contaste.` | App |
| Posición escaneada que no existe | `No hay una posición con ese código.` | App |
| Mismo producto, posición y lote ya contados | `SKU ya estaba contado en A-01: corrige la cantidad.` (al escanear) / `SKU ya está contado en A-01: toca su línea de la lista para corregir la cantidad.` (al cambiar la posición a una ya contada) | App |
| Lote sin número (producto con lote) | `Este producto lleva lote: escribe el número de lote.` | App |
| Sin líneas | `Todavía no has contado nada. Escanea un producto.` (Terminar apagado) | App |
| Errores del servidor al buscar dónde está | El mensaje del servidor tal cual (404 `Producto no encontrado.`, 404 `Conteo no encontrado.`) | API |

### Campos y validaciones (contar por producto)

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| Producto con número de serie | `Este producto se cuenta por número de serie; cuéntalo desde la web por ahora.` | App (no llama al servidor) |
| Código no es de ningún producto sincronizado | `No hay un producto con ese código.` | App |
| Producto sin existencia en el almacén | No es error: se abre el conteo vacío y se lee `El sistema no tiene existencia de este producto. Si lo encontraste en alguna posición, usa «Otra posición».` | App |
| Confirmar un conteo vacío (sin ninguna fila) | `No se puede terminar un conteo vacío: agrega la posición donde lo encontraste con «Otra posición» o cancela el conteo.` (no se manda nada) | App |
| Abrir sin señal | `No se pudo abrir el conteo de ese producto (necesita señal).` | App, tras error de red |
| Otros errores al abrir | El mensaje del servidor (p. ej. 422 `El almacén está inactivo.`, 400 `El conteo admite como máximo 1000 líneas; acote los filtros.`) | API |
| Un espacio con algo que no es una cantidad | `Hay cantidades que no son un número; corrígelas para confirmar.` (Confirmar apagado) | App |
| Espacios en blanco al confirmar | `{n} posiciones en blanco se toman como 0.` / `1 posición en blanco se toma como 0.` (aviso, no bloquea si hay al menos un número escrito) | App |
| Confirmar con **todas** las posiciones en blanco (Lote A5) | `Escribe al menos una cantidad. Si no hay nada de este producto, escribe 0 en una posición.` (no se manda nada; el conteo sigue abierto) | App |
| Buscador sin coincidencias | `Ninguna posición coincide con «{texto}».` | App |
| Otra posición: sin zonas | `No hay zonas para elegir: sincroniza con señal e intenta de nuevo.` | App |
| Otra posición: zonas del aparato | `Sin respuesta del servidor: se muestran las zonas guardadas en el aparato.` (aviso) | App |
| Otra posición: producto con lote sin número de lote | `Este producto lleva lote: escribe el número de lote.` ("Agregar posición" apagado) | App |
| Otra posición: vencimiento mal escrito | `La fecha no es válida. Escríbela así: MM/DD/AAAA` (según la compañía) | App |
| Otra posición: la posición (y lote) ya está en la lista | `La posición {bin} ya está en la lista: escribe la cantidad ahí.` | App |
| Otra posición: código repetido en el almacén | `Ya existe una posición con ese código en el almacén.` (409) y botón `Usar {bin}, que ya existe` | API |
| Otra posición: datos de la posición | 400 `Indique la zona de la posición.` / `Indique el código de la posición o su pasillo/rack/nivel/posición.` / `El código de la posición solo admite letras, números, guion y guion bajo (máximo 40).` | API |
| Otra posición: zona o conteo | 404 `Zona no encontrada.` / `Conteo no encontrado.`; 422 `La zona está inactiva; reactívela primero.` / `El conteo ya fue reconciliado; no admite posiciones nuevas.` / `El almacén está inactivo.` | API |
| Otra posición sin señal | `No se pudo crear la posición: necesita señal. Intenta de nuevo cuando haya señal.` | App, tras error de red |
| Avisos verdes | `Posición {bin} agregada (pendiente de revisión).` / `La posición {bin} ya existía; se agregó a la lista.` | App |

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
| Al enviarse, el servidor guardó las demás líneas pero omitió las que el supervisor ya corrigió (200 con `skippedLines`, Lote A7) | En Sincronización, bloque `Se guardaron las demás líneas de tu conteo.` con las líneas no guardadas (sección 9.1). No es un error: el envío queda como enviado |
| Al enviarse, TODAS las líneas del lote ya las corrigió el supervisor (409, Lote A6/A7) | En Sincronización, tarjeta `El supervisor ya corrigió todas las líneas de este envío.` con la explicación y "Actualizar el conteo" (sección 9.1). No se guardó ninguna línea de ese envío | API, vía cola |

### Estatus y casos frecuentes

El conteo existe en el servidor desde que se escanea la posición (aunque no se haya capturado nada todavía); por
eso "Cancelar" es una llamada al servidor, no solo un descarte local. Terminar sin conexión dos veces seguidas no
duplica nada: la clave de idempotencia de cada fila de la cola protege el reintento.

---

## 8. Consultar

Qué hace: un solo campo para buscar saldos, por producto (código de barras o SKU, ya sincronizado), por **posición** o por texto
libre: `GET /api/v1/inventory/balances`. Guarda la última respuesta de cada código consultado (`balance_cache`, local) para poder
responder "de hace N minutos" si se repite la misma búsqueda sin señal.

Quién puede: `inventory.view` (módulo **WMS_LOTSERIAL**). Las **cantidades del sistema en la lista de una posición** solo las ve quien
tiene `warehouse.count` (§8.1). La búsqueda por producto y la de texto libre siguen mostrando en mano y disponible a cualquiera con
`inventory.view`, como antes de este lote (decisión pendiente del dueño, `docs/mobile/loteA8-decisiones.md`).

Cómo se usa: se escanea o escribe un código (escanear = Aceptar, §1.1). La app decide, en este orden:
1. **Producto** ya sincronizado: el saldo de ese producto en todo el almacén, una fila por posición y lote, con lo que hay en mano y lo
   disponible (como siempre).
2. **Posición** del almacén: la lista de lo que el sistema dice que hay en ella (§8.1, Lote A8).
3. Ninguna de las dos: se manda como texto libre y el servidor busca por SKU, nombre, código de barras o lote (título `Resultados de
   «{código}»`); cada fila muestra posición, lote (si aplica), en mano y disponible.

### 8.1 Lo que hay en una posición (Lote A8)

Para qué: en racks altos los productos no se alcanzan a escanear o no tienen etiqueta; el operario escanea la **etiqueta de la
posición** (o la teclea con ⌨) y ve **en vivo** qué productos tiene según el sistema, sin depender de que la hoja impresa de la posición
(Lote 23/F15) esté al día.

Cómo funciona:
- La posición se reconoce primero entre las **posiciones sincronizadas** del almacén del aparato (no necesita señal para eso) y, si no
  está, se pregunta al servidor (`GET /api/v1/warehouses/{id}/bins`, código exacto, sin distinguir mayúsculas). Los saldos se piden **por
  la posición** (`binIds`), no por texto libre: una posición `A-01` ya no trae también lo de `A-01-01`.
- Título `Qué hay en {posición}` y debajo `{n} productos en esta posición` (o `1 producto en esta posición`).
- **Una fila por producto**: el **SKU en grande**, el nombre y, si lleva lote, `Lote A, B` (con más de tres: `Lote A, B, C y 2 más`). Los
  lotes solo se muestran. Si el producto está en varios lotes, la fila es una sola (lo de los lotes se suma).
- **Cantidades**: quien tiene `warehouse.count` ve debajo `En mano: {n} · Disponible: {n}` (suma de los lotes, con los separadores de la
  compañía). **Quien no lo tiene no ve ninguna cantidad**, igual que en el conteo a ciegas. La app lo sabe preguntando al servidor quién
  entró (`GET /api/v1/me`) al abrir Consultar; lo guarda en el aparato por usuario para decidir sin señal. Mientras no lo sepa (primera vez
  sin señal), no muestra cantidades.
- Con **más de 6 productos** aparece **Buscar producto o lote** (filtra por SKU, nombre o lote; la misma regla que la lista de "Contar por
  producto"). La lista se desplaza con la pantalla.
- Las filas **no se tocan** (Consultar no tenía ninguna acción sobre una fila; para ver un producto en todo el almacén, escanee su código).
- Sin señal: si esa misma posición ya se consultó antes en el aparato, se muestra esa lista con el aviso `Datos de las {hora} (hace {N}
  min, sin señal ahora)`.

Las demás pantallas que escanean una posición **no cambian**: en Conteo por posición, la lista "Lo que se espera aquí" ya muestra los
productos de la posición (y tocar uno llena el campo, §7); en Acomodar, Recibir directo y Despacho, escanear la posición es la acción misma
(completar la tarea o agregar la línea), no una consulta.

### Campos y validaciones

| Campo / caso | Mensaje exacto | Origen |
|---|---|---|
| No es un producto, ni una posición, ni el servidor encuentra nada con ese texto | `No hay un producto ni una posición con ese código.` (Lote A8; antes `No hay nada con ese código.`) | App + API, lista vacía |
| La posición existe pero no tiene nada | `No hay productos en esta posición.` (no es un error: sale bajo `Qué hay en {posición}`) | API, lista vacía |
| La posición solo existe dada de baja | `La posición {posición} está desactivada.` | App (posiciones sincronizadas) + API |
| Buscador de la lista sin coincidencias | `Ningún producto coincide con «{texto}».` | App |
| Posición con más de 1,000 renglones de saldo | `Se muestran los primeros 1,000 renglones de esta posición; el resto, en la web.` | App |
| Sin señal y sin una consulta anterior de ese mismo código (o de esa posición) | `Sin señal y sin una consulta anterior de esto.` | Local (caché vacía para esa clave) |
| Sin señal, pero hay una consulta anterior guardada | `Datos de las {hora} (hace {N} min, sin señal ahora)` (Lote A3: con la hora en la zona y el formato de la compañía; si no es de hoy, con la fecha), con los datos de esa consulta anterior | Local (`balance_cache`) |
| Otro error del servidor (403, 500…) | El mensaje del servidor tal cual | API |

La clave de caché es por almacén y por código exacto (mayúsculas, sin espacios extra): consultar el mismo producto
por dos códigos distintos (SKU una vez, código de barras otra) guarda dos entradas separadas. La lista de una posición se guarda aparte
de la búsqueda libre del mismo texto.

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

**Última vez** (Lote A3): la hora de la última pasada, en la zona y con el formato de 12 o 24 horas de la compañía (con la fecha
si no fue hoy).

**Lector** (Lote A3): una línea dice cómo quedó el perfil del lector del Zebra (§1.1), según lo que contestó DataWedge:

| Indicador | Qué quiere decir | Qué hacer |
|---|---|---|
| `Lector: listo` (verde) | DataWedge confirmó que el perfil activo de la app es `TeikemAlmacen` | Nada |
| `Lector: sin perfil` (rojo) | DataWedge rechazó crear el perfil o la app está usando otro perfil (el detalle dice cuál, p. ej. `activo: Profile0`) | Tocar `Volver a configurar el lector`; si sigue, revisar el perfil en DataWedge (ver FAQ) |
| `Lector: sin confirmar` | Se pidió el perfil pero DataWedge no contestó (todavía) | Tocar `Volver a configurar el lector`, salir y volver; si sigue, avisar a soporte |
| `Lector: no es un Zebra (se usa el teclado)` | El aparato no tiene DataWedge | Nada: se escribe o se usa el teclado |

**Formatos** (Lote A3): en cada pasada con señal también se traen la región y los formatos de la compañía (§1.2).

**Qué pasa en cada pasada con señal (Lote 16).** En orden: (1) se manda la cola de salida; (2) si hubo señal, se manda el **heartbeat** (aviso de vida, capítulo 8A §2.2), que actualiza el
almacén por defecto, el tema y el **modo de recepción** del almacén del aparato; si el servidor responde que el aparato está desactivado, se borra la identidad del aparato (hay que registrarlo de nuevo); (2b, Lote A3) se traen la región y los formatos de la compañía; (3) se bajan productos, órdenes de compra, avisos de llegada y **las posiciones del almacén por defecto** del aparato. Antes del Lote 16 el heartbeat no lo llamaba nadie.
Las posiciones sirven para validar sin señal la posición destino del recibo directo: la primera vez baja la lista completa (en Advance Depot, unas 3.886 posiciones, en 8 páginas) y después solo los cambios;
las posiciones dadas de baja se conservan marcadas como inactivas (así la app distingue "no existe" de "está desactivada"). Si el almacén por defecto del aparato cambia, el nuevo baja completo la primera vez.

### 9.1 Línea ya corregida por el supervisor: aviso de lote parcial (Lote A7) y rechazo (Lote A6)

Qué pasa: al terminar un conteo, la app encola **el lote capturado** (`countBatch`, `PUT /api/v1/cycle-counts/{id}/lines/batch`) y
**el cierre** (`countFinish`). Si mientras tanto un supervisor **corrigió** en la web una línea que el operario vuelve a mandar con otro
número, desde el segundo bloque de decisiones del dueño (2026-10-03) el servidor **guarda las líneas libres y omite las corregidas**
(capítulo [06](06-inventario-y-almacen.md), protección de la corrección): responde 200 con `skippedLines`. El envío queda **enviado** (no
es un error) y el cierre del conteo sigue su camino. Solo si **todas** las líneas del lote están corregidas el servidor responde **409**
`La línea ya fue corregida por el supervisor; no se puede volver a capturar. Renglón(es) del lote: n (SKU). No se guardó nada.`; entonces
la fila pasa a "Con error" y **no se reintenta sola** (normalmente el cierre del mismo conteo también queda con error justo debajo: 422
`Faltan {n} línea(s) por contar.` si el conteo seguía Pendiente, o `El conteo ya se terminó; puede corregir la captura o reconciliarlo.` si
ya estaba Contado).

Quién lo ve: cualquiera que entró con PIN en ese aparato (Sincronización no depende de permisos). "Actualizar el conteo" consulta
`GET /api/v1/cycle-counts/{id}` con el permiso de quien está dentro (`warehouse.count.capture` o `warehouse.count`, módulo
**WMS_LOTSERIAL**).

**Aviso de lote parcial (el caso común).** Se guarda en el aparato (no se pierde si se cierra la app) y se muestra en Sincronización,
encima de la lista de pendientes, hasta que se toque **Descartar este aviso**:

| Parte | Texto exacto (es) |
|---|---|
| Título | `Se guardaron las demás líneas de tu conteo.` |
| Introducción | `Estas no se guardaron porque el supervisor ya las corrigió:` |
| Una por línea | `• {SKU} · {posición} (mandaste {x} → el supervisor dejó {y})`; con lote: `• {SKU} · lote {L} · {posición} (…)`; sin valor vigente: `(mandaste {x}; el supervisor ya la corrigió)` |
| Aclaración | `No tienes que hacer nada con ellas. Si falta contar algo, crea un conteo nuevo o pide al supervisor que lo revise.` |

Botones: **Actualizar el conteo** (el mismo de abajo) y **Descartar este aviso**. Muestra la cantidad que dejó el supervisor aunque el conteo
sea a ciegas; **nunca** la cantidad esperada. La app **no reabre un conteo ya enviado**: si falta contar algo, se crea un conteo nuevo
(escanear la posición otra vez).

**Rechazo (409 residual: todas las líneas del lote corregidas).** Qué muestra la tarjeta (en lugar de la fila corta de siempre):

| Parte | Texto exacto (es) |
|---|---|
| Aviso rojo grande | `El supervisor ya corrigió todas las líneas de este envío.` |
| Debajo | `No se guardó ninguna línea de este envío porque todas ya las corrigió el supervisor.` |
| Renglones (si el mensaje del servidor trae la lista en el formato esperado) | `Líneas que ya corrigió el supervisor:` y una por renglón: `• Renglón {n}: {SKU} (mandaste {cantidad})` (la cantidad es la que este aparato mandó en ese renglón) |
| Si la lista no se puede leer con seguridad (por ejemplo un SKU con paréntesis) | `Lo que dijo el servidor:` y el mensaje tal cual |
| Qué hacer | `Si falta contar algo, crea un conteo nuevo (escanea la posición otra vez) o pide al supervisor que lo revise.` |
| Aclaración | `Esta app no reabre un conteo ya enviado. Toca «Actualizar el conteo» para ver cómo quedó.` |
| Si el cierre del mismo conteo también tiene error | `El cierre de este mismo conteo también quedó con error: el conteo no se terminó desde este aparato.` |
| Si no se reconoce de qué conteo es | `No se pudo saber de qué conteo es este envío; pide al supervisor que lo revise.` (sin botón de actualizar) |

Botones: **Actualizar el conteo** (nunca se ejecuta solo), **Reintentar: countBatch** (vuelve a mandar el mismo lote; falla igual
mientras la corrección siga ahí) y **Descartar este envío** (borra la fila del aparato sin mandarla; la fila del cierre se descarta
aparte con su propio "Descartar").

Resultado de **Actualizar el conteo**:

| Caso | Texto exacto (es) |
|---|---|
| Sigue abierto (Pendiente) | `El conteo {número} sigue abierto (Pendiente).` + `{total} líneas: {c} corregidas por el supervisor, {s} sin contar.` + la lista de líneas |
| Ya terminado (Contado) | `El conteo {número} ya se terminó de contar (Contado): pide al supervisor que lo revise.` + resumen y líneas |
| Reconciliado | `El conteo {número} ya fue reconciliado: no admite más capturas. Puedes descartar este envío.` |
| Ya no existe (404 o eliminado) | `El conteo ya no existe (se eliminó). Puedes descartar este envío.` |
| Sin señal | `Sin señal: no se pudo actualizar el conteo. Inténtalo de nuevo cuando haya conexión.` |
| Otro error del servidor (403, 5xx…) | El mensaje del servidor tal cual |

Cada línea dice `{SKU} · {posición}` (y el lote si lo lleva) y su estado: `Contado: {n}`, `Sin contar`, o `Corregida por el
supervisor ({nombre}) · Contado: {n}`. **Nunca muestra la cantidad esperada** (lo que el sistema tenía), aunque quien está dentro tenga
`warehouse.count`: solo lo contado vigente y quién lo corrigió. Con más de 30 líneas se ven las primeras 30 y `… y {n} líneas más
(míralas en la web).`

### Casos frecuentes

Una operación queda "con error" cuando el servidor la rechazó por una razón que reintentarla igual no arregla
(dato inválido, un documento que ya no existe, un permiso que cambió) — nunca por falta de señal, eso la deja
"pendiente" en cambio. Antes de reintentar, conviene revisar el mensaje: si el problema sigue (por ejemplo, "esta
posición ya no tiene ese producto"), reintentar va a fallar otra vez con el mismo error.

---

## 10. Permisos usados por la app (resumen)

| Permiso | Qué habilita en la app |
|---|---|
| `inventory.view` | Entrar con PIN (capítulo 8A §3.3), Recibir (incluida la pista "Sugerida" del recibo directo y la descarga de posiciones), Acomodar (listar/completar tareas), Conteo (ver, no reconciliar; zonas de "Otra posición"), Consultar (incluida la lista de lo que hay en una posición, sin cantidades) |
| `purchasing.receive` + módulo **PURCHASING** | Recibir contra una orden de compra (sin esto, solo recibo ciego funciona) |
| `warehouse.pick` | Recolectar y empacar en Despacho |
| `locations.read` | Buscar los consignatarios de un cliente al empacar un despacho |
| `warehouse.count.capture` (implícito en `warehouse.count`) | Abrir, capturar y terminar un conteo, por posición o por producto (a ciegas si falta `warehouse.count`), y crear una posición provisional en "Otra posición" |
| `warehouse.count` | Ver las cantidades esperadas de un conteo (no a ciegas) y **cancelarlo**; en Consultar, ver `En mano` y `Disponible` en la lista de una posición (Lote A8) |

Todas estas rutas viven bajo el módulo **WMS_LOTSERIAL** (capítulo 8A §7); sin el módulo encendido para la
compañía, el aparato no puede ni listar sus usuarios para entrar.

---

## Marca

Desde el lote F8a la app lleva la marca Teikem: icono (con versión monocroma para los temas de Android 13+), pantalla de arranque azul `#0B2C66` y el logotipo con el lema en el idioma activo arriba de Registrar y Entrar ([icono](frontend/img/f8a-app-icono.png), [arranque](frontend/img/f8a-app-splash.png), [Registrar](frontend/img/f8a-app-registrar.png)); los archivos se regeneran con `npm run brand:icons` desde `Logos/`.

---

## 11. Preguntas frecuentes

Ver la sección **Lote 8A** de [`faq.md`](faq.md) para el backend (aparatos, PIN, idempotencia, sincronización) y
los casos propios de la app agregados en este lote (qué pasa si se pierde la señal a mitad de un recibo, por qué
Despacho solo funciona con clientes 3PL, qué significa "con error" en Sincronización), y la sección **Lote A3** (lector del
Zebra, teclado, formatos de la compañía en la app), la sección **Lote A4** (contar por producto) y la sección **Lote A5** (al menos
una cantidad al contar por producto; en Despacho, la cantidad antes de la posición), la sección **Lote A6** (captura de conteo rechazada
porque el supervisor ya corrigió una línea) y la sección **Lote A8** (lo que hay en una posición en Consultar).
