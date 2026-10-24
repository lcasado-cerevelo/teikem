# Lote 216 (plan) — App móvil: estación de escaneo de rutas, con trabajo sin señal

> Anotado a pedido de Luis (2026-10-24). **Todavía no se construye**: queda registrado el requisito y la propuesta de diseño para arrancar cuando él lo diga. Lo que era el Lote 216 (clientes y consignatarios en lectura, almacén solo consulta) pasa a **Lote 217**.

## Requisito (palabras de Luis, resumidas)
- El personal usa un **teléfono Android con un mango y gatillo** que dispara la lectura del código de barras. Con él **escanea los paquetes** de la ruta.
- **Al escanear, el sistema va asignando** los envíos a la ruta (y les va cambiando el estatus: «Asignado» y, cuando la ruta ya está asignada, «En tránsito»), en la medida en que se escanean.
- Debe ser una **pantalla de la app móvil del personal**, fácil de usar con el gatillo.
- **Esta pantalla SÍ funciona sin señal** (a diferencia del resto de lo del personal, que es en vivo): se **bajan al teléfono todos los envíos que no están completados** (incluidos los «En tránsito»), **hasta 5000**. Si hay más de 5000 y no hay señal, lo que quede fuera **no se encuentra**.

## Lo que ya existe y se reutiliza (servidor y web)
La estación de escaneo del capítulo 05 §7-7b (Lotes 5, 107, 110): permiso `trips.scan`, módulo de transporte, `POST /api/v1/scan/action` con acción **Recoger / Llegó / Enrutar / Salir** (y Devolver con `orders.return`), `POST /api/v1/scan/close-count`. Se escanea el **número de cada pieza**; el primer escaneo de una acción mueve el envío a ese estatus (brincando los de en medio, con historial); los siguientes solo cuentan («4 de 5»); repetir la misma pieza con la misma acción es duplicado; lo que falta al pasar a la acción siguiente queda «Por verificar». **Enrutar** pone el envío en la ruta abierta de su zona y lo deja Asignado.

## Propuesta de diseño (por defecto; se ajusta con Luis)
1. **Pantalla «Escanear»** en el inicio del personal (`trips.scan`): se escoge la acción (Enrutar por omisión), se dispara el gatillo y cada lectura responde al instante con el mismo resultado y voz de la web (Sí / No / Ok), el conteo «4 de 5» y los faltantes. El Android con gatillo entrega la lectura como teclado: un campo de entrada siempre enfocado la recibe; la cámara queda como respaldo.
2. **Descarga**: todos los envíos **no terminales** de la compañía con sus **piezas**, su estatus, ruta, zona y cuántas piezas ya se escanearon por acción, **hasta 5000** (los más recientes primero) en SQLite del teléfono. Se baja al entrar, con un botón «Sincronizar» y automáticamente con señal. Un contador dice cuántos hay bajados y desde cuándo.
3. **Resolución local**: sin señal, la pieza se busca en lo bajado; si no está (fuera de los 5000 o más nueva que la descarga) responde «No se encontró la pieza». Con señal y la pieza no encontrada localmente, se pregunta al servidor.
4. **Cola de escaneos** (la misma de las entregas): cada lectura queda guardada con su acción y hora y sale al servidor en orden cuando hay señal, contra `scan/action` (idempotente por pieza y acción). El servidor es la autoridad: si algo cambió (ya entregado, final, sin ruta que la reciba), la respuesta real llega al sincronizar y se muestra en una lista «Pendientes / con problema».
5. **Estatus mostrado**: el teléfono aplica la misma regla de avance que el servidor para mostrar al instante el estatus nuevo; el definitivo es el del servidor.

## Preguntas para Luis antes de construir
1. **Acciones en el teléfono**: ¿las mismas de la web (Recoger, Llegó, Enrutar, Salir y Devolver) o solo **Enrutar y Salir**?
2. **Enrutar sin señal**: asignar a la ruta de la zona necesita saber qué ruta abierta tiene la zona; ¿se baja la lista de rutas abiertas del día y se asigna a la ruta de la zona de la pieza (como la web)? ¿O se escoge **la ruta** en la pantalla antes de escanear?
3. **Hora**: el historial del servidor marcaría la hora de la **sincronización**, no la del escaneo. ¿Basta, o se necesita guardar la hora real del escaneo (cambio en el servidor)?
4. **Más de 5000**: ¿cuáles bajar primero (los más recientes, o los de las rutas de hoy primero)?
