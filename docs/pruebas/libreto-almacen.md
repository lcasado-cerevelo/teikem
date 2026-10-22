# Libreto de pruebas manuales — Almacén

**Para quién es:** cualquier persona que vaya a probar el almacén de Teikem, aunque no haya visto el sistema antes.
**Qué cubre:** avisos de llegada y de salida, recibir (web y aparato móvil), recibir por partes y acomodar, recibir varios avisos juntos, despachar (web y aparato) y recolección y empaque.
**Dónde se prueba:** en el ambiente de **pruebas** (`https://test.advancelogisticspr.com`) o en staging. **Nunca en producción**: estos libretos crean, reciben y despachan mercancía de verdad.
**Fecha del libreto:** 2026-10-21. Los nombres de pantallas y botones salen del código y del manual de esa fecha; si en pantalla ves otro texto, anótalo como hallazgo (ver el anexo C).

---

## 0. Cómo usar este libreto

1. Haz primero la **Preparación (sección 1)**. Sin ella los libretos no se pueden seguir.
2. Cada libreto es una tabla de pasos. En cada fila: **qué haces**, **con qué datos** y **qué debes ver**.
3. Marca la última columna: ✔ si lo que ves coincide con «Resultado esperado»; ✘ si no. Si pones ✘, apunta en el anexo C qué viste en realidad y toma una captura.
4. Los libretos van **en orden**: lo que se recibe en los libretos 2 a 7 es la mercancía que se despacha en los libretos 8 a 10. No los reordenes la primera vez.
5. Los textos entre «comillas angulares» son letra exacta de la pantalla.
6. Los números de documento se asignan solos (**LLE-**#####, **REC-**#####, **RCM-**#####, **SAL-**#####, **EMP-**#####). Anota en la tabla de la sección 1.4 el que te toque en cada paso; el libreto los llama por su letra (A1, A2…).

### Mapa de libretos

| # | Libreto | Dónde | Qué aprendes a verificar |
|---|---|---|---|
| 1 | Avisos de llegada | Web | Crear, editar, asignar, tomar y cancelar |
| 2 | Recibir un aviso completo | Web | Recibo, confirmar, acomodo |
| 3 | Recibir por partes (tandas) | Web | Estatus «Descargando», «Falta N», sobrante y cierre con faltante |
| 4 | Recibir varios avisos juntos | Web | «Recibir juntos», Cargas (RCM) |
| 5 | Recibir un aviso y acomodar | Aparato | Recibir, confirmar, sincronizar, acomodar |
| 6 | Recibir por partes y acomodar | Aparato | «Recibir una parte», aviso de staging lleno, seguir recibiendo |
| 7 | Recibir varios avisos juntos | Aparato | «Seleccionar varios» |
| 8 | Avisos de salida | Web | Crear, reserva y pendiente, cambiar cantidad, cancelar, despachar |
| 9 | Despachar | Aparato | Un aviso, parcial, varios juntos, sin documento |
| 10 | Recolección y empaque | Web | Recolectar, empacar, eliminar, recolectar juntos |

---

## 1. Preparación (se hace una sola vez)

### 1.1 Ambiente y usuarios

| Necesitas | Detalle |
|---|---|
| Ambiente | `https://test.advancelogisticspr.com` (o staging). Verifica que la cinta de color/etiqueta del ambiente diga TEST o STAGING. |
| Compañía y módulo | Una compañía con el módulo **Inventario por lote y serie** (`WMS_LOTSERIAL`) encendido. |
| Usuario de la web | Un usuario con estos permisos: `warehouse.receive`, `warehouse.pick`, `warehouse.manage`, `exitnotice.create`, `orders.create` (para empacar), `inventory.view`. El rol **Admin de compañía** los tiene todos. |
| Usuario del aparato | La misma persona (o una segunda) con su **PIN de 4 a 6 dígitos**, definido en la web en **Mi cuenta**. |
| Almacén | **ALM-DEPOT** (Advance Depot): modo de recepción **con acomodo**, con una posición de recepción en una zona de staging: **R1**. |
| Cliente dueño de la mercancía | **AxisCare** (el mismo que aparece en los recibos de Advance Depot). Si tu base de pruebas no lo tiene, usa cualquier cliente con tres productos y anótalo en 1.2. |

### 1.2 Productos de la prueba

Todos deben ser **del mismo cliente dueño** y **sin lote ni serie** (si tienen, la pantalla te pedirá lote o serie y eso es otra prueba). Anota el SKU real de P2 y P3.

| Código en este libreto | Producto | SKU | Código de barras |
|---|---|---|---|
| **P1** | EXERCISE PEDDLE SILVER VEIN | `10270KDRSV-1` | el del producto; si no tiene, se teclea el SKU |
| **P2** | (otro producto del mismo cliente) | ______________ | ______________ |
| **P3** | (un tercer producto del mismo cliente) | ______________ | ______________ |

> Consejo: en el aparato el campo de escaneo no abre el teclado solo. Toca el botón **⌨**, escribe el SKU y toca **Aceptar**. Escanear con un lector hace lo mismo.

### 1.3 Preparar el aparato móvil

| # | Qué haces | Resultado esperado | ✔/✘ |
|---|---|---|---|
| 1 | En la web: **Sistema → Aparatos** → crear un código de registro. | Aparece un código de **8 caracteres**, de un solo uso, que vence en 24 horas. | |
| 2 | En el aparato (primera vez): pantalla «Registrar este aparato». Escribe la **Dirección del API** del ambiente de pruebas y el **Código de registro**. Toca **Registrar**. | Pasa a «¿Quién eres?». Si el código ya se usó o venció: «El código de registro no es válido o venció.» | |
| 3 | «¿Quién eres?» → elige tu nombre → escribe tu PIN → **Entrar**. | Entras al menú «Teikem Almacén». Si te equivocas: «PIN incorrecto.» (tras varios intentos: «PIN bloqueado por 15 minutos.») | |
| 4 | Si te pide compañía: «¿En qué compañía vas a trabajar?» → elige la compañía de pruebas. Verifica la fila «Almacén: …» y, si hay más de uno, toca **Cambiar** y elige **ALM-DEPOT**. | El menú muestra los mosaicos: Mis tareas, Consultar, Recibir, Acomodar, Despacho, Conteo (y Ajustar / Daño / Devoluciones según tus permisos). | |

### 1.4 Hoja de seguimiento (anota los números que vayan saliendo)

| Letra | Documento | Número real |
|---|---|---|
| A1 | Aviso de llegada del libreto 1 | LLE-_____ |
| A1x | Aviso de llegada que se cancela | LLE-_____ |
| A2 | Aviso del libreto 3 (por partes) | LLE-_____ |
| A3 | Aviso del libreto 3 (sobrante) | LLE-_____ |
| A4 | Aviso del libreto 3 (cierre con faltante) | LLE-_____ |
| M1, M2, M3 | Avisos del libreto 4 | LLE-_____ / LLE-_____ / LLE-_____ |
| N1 | Aviso del libreto 5 | LLE-_____ |
| N2 | Aviso del libreto 6 | LLE-_____ |
| N3, N4 | Avisos del libreto 7 | LLE-_____ / LLE-_____ |
| S1…S6 | Avisos de salida de los libretos 8 y 9 | SAL-_____ (uno por letra) |

---

## Libreto 1 — Avisos de llegada (web)

**Objetivo:** crear un aviso de llegada y comprobar que se puede editar, asignar, tomar y cancelar.
**Pantalla:** menú **Almacén → Llegadas y salidas** → pestaña **Avisos de llegada**.

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 1.1 | Abre **Almacén → Llegadas y salidas**. | — | Título «Llegadas y salidas», con dos pestañas: «Avisos de llegada» (la primera) y «Avisos de salida». | |
| 1.2 | Pulsa **Nuevo aviso**. | — | Se abre el modal «Nuevo aviso». | |
| 1.3 | Intenta **Guardar** con todo vacío. | — | Mensajes: «Elija el almacén.», «Indique el cliente dueño de la mercancía del aviso de llegada.» y «Indique al menos una línea.» No se guarda. | |
| 1.4 | Llena el encabezado. | Almacén **ALM-DEPOT** · Cliente dueño **AxisCare** · Referencia **PRUEBA-A1** · Llegada esperada: la fecha de hoy | Al elegir el cliente, el selector de producto deja de decir «Elija primero el cliente.» | |
| 1.5 | En «Líneas», agrega dos líneas con **Agregar línea**. | Línea 1: **P1**, cantidad **15** · Línea 2: **P2**, cantidad **8** | Solo aparecen productos del cliente elegido. El precio unitario se precarga con el costo del producto. | |
| 1.6 | Prueba la validación de cantidad: escribe **0** en la cantidad de P2 y pulsa **Guardar**. | cantidad 0 | «La cantidad esperada debe ser mayor que cero.» Vuelve a poner **8**. | |
| 1.7 | Pulsa **Guardar**. | — | Mensaje «Aviso de llegada creado.» En la lista aparece **LLE-0000N · PRUEBA-A1** con estatus **Esperada**, «Recibido 0 / 23», 2 líneas. **Anótalo como A1.** | |
| 1.8 | Pulsa **Editar** en la fila de A1. | — | Se abre el mismo formulario; el cliente no se puede cambiar. | |
| 1.9 | Cambia la cantidad de P2 de 8 a **10**. **Guardar**. | P2 = 10 | «Aviso de llegada actualizado.» La lista dice «Recibido 0 / 25». **A1 queda con: P1 = 15 y P2 = 10.** | |
| 1.10 | Pulsa **Asignar** en la fila de A1. | Elige tu usuario | El modal se titula «Asignar LLE-…» y ofrece también «Sin asignar». Al guardar, la columna «Asignado a» muestra tu nombre. | |
| 1.11 | Marca el filtro **Solo los asignados a mí**. | — | Solo aparece A1. Desmarca el filtro. | |
| 1.12 | Crea un segundo aviso para cancelarlo. | ALM-DEPOT · AxisCare · Referencia **PRUEBA-A1X** · línea **P1 × 1** | Se crea. **Anótalo como A1x.** | |
| 1.13 | Pulsa **Cancelar aviso** en A1x. | — | Diálogo «Cancelar aviso de llegada»: «¿Cancelar el aviso de llegada #… ? Ya no se podrá recibir contra él.» (muestra el número interno, no el LLE). | |
| 1.14 | Confirma. | — | «Aviso de llegada cancelado.» A1x pasa a **Cancelada** y ya no ofrece Recibir/Editar. | |

---

## Libreto 2 — Recibir un aviso completo (web)

**Objetivo:** recibir **todo** lo que dice un aviso, confirmar el recibo y acomodar.
**Datos:** aviso **A1** (P1 = 15, P2 = 10).
**Pantalla:** **Almacén → Recibo** (y el botón «Recibir» de la fila del aviso).

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 2.1 | En **Llegadas y salidas → Avisos de llegada**, pulsa **Recibir** en la fila de A1. | — | Te lleva a **Recibo** y se abre el modal «Nuevo recibo» con el aviso ya elegido (origen «Contra aviso de llegada», almacén ALM-DEPOT). | |
| 2.2 | Revisa «Posición de recepción». | — | Si está vacía dice «Primera posición de una zona STAGING»: el sistema usa la primera posición de staging (R1). Déjalo así. | |
| 2.3 | Escribe en Transporte y Referencia. | Transporte **CAMION-01** · Referencia **BL-A1** | — | |
| 2.4 | Pulsa **Crear recibo**. | — | «Recibo REC-0000N creado.» En el panel «Detalle del recibo · REC-…» ves las líneas con **Esperado 15 / 10** y **Recibido 15 / 10** (viene precargado con lo esperado). Estatus **Descargando**. La lista del aviso muestra el enlace al REC. | |
| 2.5 | Mira el aviso A1 en «Llegadas y salidas». | — | Su estatus sigue **Esperada** mientras el recibo no se confirma. | |
| 2.6 | Vuelve al recibo. Mira los botones bajo las líneas. | — | Hay «Confirmar tanda» y «Confirmar recibo». «Confirmar tanda» está activo (hay unidades nuevas). | |
| 2.7 | Pulsa **Confirmar recibo**. | — | Diálogo: «Se confirma el recibo REC-… completo: se asienta el inventario y se crean las tareas de acomodo. Ya no se podrá modificar.» | |
| 2.8 | Confirma. | — | «Recibo REC-… confirmado.» Estatus **Completado**. El aviso A1 pasa a **Recibida**. Aparece el panel **Tareas de acomodo** con una tarea por producto (P1 × 15, P2 × 10) de «R1 → posición sugerida», en estatus **Pendiente**. | |
| 2.9 | Pulsa **Asignar todas** (o **Iniciar todas** directamente). | Tu usuario | Las tareas pasan a **En proceso** («Se iniciaron 2 tareas.»). | |
| 2.10 | Pulsa **Completar todas**. | — | Diálogo «Completar todas las tareas del recibo»: «Se completarán 2 tareas con la posición sugerida de cada una…». Confirma. «Se completaron 2 tareas con su posición sugerida.» | |
| 2.11 | Mira el estatus del recibo. | — | **Acomodado** (cuando se cierra la última tarea). Las tareas dicen **Completada**. | |
| 2.12 | En **Productos e inventario**, busca P1 y P2. | — | P1 tiene **15** y P2 tiene **10** en las posiciones donde se acomodaron (más lo que ya hubiera). | |
| 2.13 | Prueba una confirmación bloqueada: en **Recibo → Nuevo recibo → Contra aviso de llegada**, elige otra vez A1. | — | El aviso ya no está disponible o el sistema responde «El aviso de llegada no está pendiente de recibir.» | |

---

## Libreto 3 — Recibir por partes (tandas) y ver los estatus (web)

**Objetivo:** recibir un aviso en varias tandas, ver cómo cambia el estatus en pantalla, probar el sobrante y cerrar con faltante.
**Idea clave:** mientras el recibo está abierto, que **falten** unidades **no es un problema**: el estatus es **«Descargando»**. **«Discrepancia»** solo aparece cuando se recibe **más** de lo esperado.

### 3A. Recibir en dos tandas y completar

**Datos:** crea el aviso **A2**: ALM-DEPOT · AxisCare · Referencia **PRUEBA-A2** · líneas **P1 × 15** y **P3 × 6**. (Se crea como en 1.2–1.7.)

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 3.1 | **Recibir** el aviso A2 (igual que en 2.1) y **Crear recibo**. Transporte **CAMION-02**. | — | Recibo creado. Estatus **Descargando**. | |
| 3.2 | En la línea de P1, cambia **Recibido** de 15 a **5** (sal del campo o pulsa Enter para guardarlo). Deja P3 en **6**. | P1 = 5 · P3 = 6 | La columna Diferencia de P1 dice **«Falta 10»** en color neutro (no rojo). Debajo aparece el aviso: «Faltan unidades por recibir. Sigue descargando y captúralas; si cierras con «Confirmar recibo», lo que falta queda como diferencia («Completado con diferencia»).» Estatus del recibo: **Descargando** (no «Discrepancia»). | |
| 3.3 | Mira el botón «Confirmar tanda» y su ayuda. | — | Activo. Ayuda: «11 unidades nuevas por confirmar. La confirmación final cierra el recibo.» | |
| 3.4 | Pulsa **Confirmar tanda**. | — | Diálogo: «Se confirman las 11 unidades recibidas hasta ahora del recibo REC-…: entran al inventario y se crea su acomodo. El recibo sigue abierto para lo que falte.» Confirma. «Tanda del recibo REC-… confirmada.» | |
| 3.5 | Mira el recibo. | — | Aparece «**Tandas confirmadas**»: «Tanda 1 · fecha · tu nombre · 11 u. en 2 línea(s)». El estatus **sigue en Descargando**. El panel «Tareas de acomodo» tiene tareas pendientes de **P1 × 5** y **P3 × 6**. | |
| 3.6 | Mira «Confirmar tanda». | — | **Apagado.** La ayuda dice: «Todo lo recibido ya está confirmado en tandas. Captura lo que siga llegando (sube «Recibido») para hacer otra tanda.» (Esto es lo normal: todavía no has capturado más.) | |
| 3.7 | **Acomoda lo de la tanda 1**: en «Tareas de acomodo» pulsa **Iniciar todas** y luego **Completar todas**. | — | Las dos tareas quedan **Completada**. El recibo **sigue en Descargando** (no se cierra: falta confirmar el resto). | |
| 3.8 | «Siguió llegando mercancía»: cambia **Recibido** de P1 de 5 a **10**. | P1 = 10 | «Falta 5». Confirmar tanda se activa: «5 unidades nuevas por confirmar.» | |
| 3.9 | Pulsa **Confirmar tanda** → confirma. | — | Aparece «Tanda 2 · … · 5 u. en 1 línea(s)». Nueva tarea de acomodo de **P1 × 5**. Estatus: **Descargando**. | |
| 3.10 | Intenta **bajar** Recibido de P1 a 8. | P1 = 8 | Mensaje: «No se puede dejar lo recibido por debajo de lo ya confirmado en tandas.» Vuelve a 10. | |
| 3.11 | Acomoda la tarea de la tanda 2 (Iniciar todas → Completar todas). | — | Tarea **Completada**. El recibo sigue **Descargando**. | |
| 3.12 | Llegó lo último: cambia Recibido de P1 a **15**. | P1 = 15 | Diferencia **0** en todas las líneas. Desaparece el aviso de faltante. | |
| 3.13 | Pulsa **Confirmar recibo** → confirma. | — | «Recibo REC-… confirmado.» Estatus **Completado** (sin diferencia). El aviso A2 pasa a **Recibida**. Hay una última tarea de acomodo de **P1 × 5**. | |
| 3.14 | Completa esa tarea. | — | El recibo pasa a **Acomodado**. En la lista de recibos, el filtro «Estatus» lo encuentra como Acomodado. | |
| 3.15 | Revisa el total en inventario. | — | P1 + **15** y P3 + **6** en total, repartidos en tres acomodos (5 + 5 + 5 para P1). | |

### 3B. Sobrante (más de lo esperado) → «Discrepancia»

**Datos:** aviso **A3**: ALM-DEPOT · AxisCare · Referencia **PRUEBA-A3** · línea **P2 × 4**.

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 3.16 | Recibir A3 y **Crear recibo**. | — | Estatus **Descargando**; Recibido 4. | |
| 3.17 | Cambia Recibido de P2 a **6**. | P2 = 6 | Diferencia **+2** (en rojo). Aparece el aviso «Lo recibido difiere de lo esperado — al confirmar, la diferencia queda registrada como ajuste de inventario.» Estatus del recibo: **Discrepancia**. No hay ningún error: el sistema **permite** recibir de más. | |
| 3.18 | Regresa Recibido de P2 a **4**. | P2 = 4 | El aviso desaparece y el estatus vuelve **solo** a **Descargando**. | |
| 3.19 | Pulsa **Historial** del recibo (icono de reloj arriba a la derecha). | — | El historial muestra los cambios de estatus: Esperado → Descargando → Discrepancia → Descargando. | |
| 3.20 | Deja el recibo A3 abierto sin confirmar (lo usarás en el libreto 4 si quieres) o elimínalo con el icono de eliminar. | — | Si lo eliminas, el aviso A3 vuelve a **Esperada**. | |

### 3C. Cerrar con faltante (el aviso nunca terminó de llegar)

**Datos:** aviso **A4**: ALM-DEPOT · AxisCare · Referencia **PRUEBA-A4** · línea **P2 × 10**.

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 3.21 | Recibir A4 y **Crear recibo**. Cambia Recibido de P2 a **7**. | P2 = 7 | «Falta 3». Estatus **Descargando**. | |
| 3.22 | Pulsa **Confirmar recibo** (sin tandas previas). | — | Se puede cerrar con faltante. Estatus: **Completado con diferencia**. El Kárdex recibe **7** unidades. El aviso A4 pasa a **Recibida**. | |
| 3.23 | Completa las tareas de acomodo. | — | El recibo pasa a **Acomodado**. | |

---

## Libreto 4 — Recibir varios avisos juntos (web)

**Objetivo:** abrir un solo recibo por documento a la vez, con el mismo transporte y referencia, y confirmarlos todos juntos («Carga» **RCM-#####**).
**Datos:** crea tres avisos, todos del **mismo cliente (AxisCare)** y del **mismo almacén (ALM-DEPOT)**:

| Aviso | Referencia | Línea |
|---|---|---|
| **M1** | PRUEBA-M1 | P1 × 10 |
| **M2** | PRUEBA-M2 | P2 × 6 |
| **M3** | PRUEBA-M3 | P3 × 4 |

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 4.1 | Con un solo aviso por recibir, mira el botón **Recibir juntos** (en Recibo o en Llegadas y salidas). | — | (Si solo existe uno) está apagado con el texto «Hace falta al menos dos documentos por recibir…». Con los tres avisos ya creados, está activo. | |
| 4.2 | Pulsa **Recibir juntos**. | — | Diálogo «Recibir juntos» con una lista de casillas, cada una marcada «Aviso» u «Orden». | |
| 4.3 | Marca **solo M1**. Pulsa **Recibir juntos (1)**. | — | «Elija al menos dos documentos.» | |
| 4.4 | Marca **M1, M2 y M3**. Si existe un aviso de **otro cliente** o de **otro almacén**, comprueba que se bloquea. | — | Los de otro dueño dicen «De otro dueño: reciba por separado lo de dueños distintos.»; los de otro almacén, «De otro almacén.» | |
| 4.5 | Escribe Transporte **CAMION-03**, Referencia **CARGA-PRUEBA-1**, Nota «Prueba de recibo conjunto». Pulsa **Recibir juntos (3)**. | — | «Carga RCM-0000N abierta.» Se abre la ficha «Carga recibida» con: Dueño AxisCare · Almacén ALM-DEPOT · Avance «0 de 3 recibos confirmados» · tabla «Recibos (3)». **Anota el RCM.** | |
| 4.6 | Ve a **Recibo → pestaña Cargas**. | — | La carga aparece con estatus **Abierta**, 3 documentos y «Recibido / esperado». | |
| 4.7 | En la ficha de la carga, pulsa **Abrir recibo** en la fila de M2. | — | Abre el recibo de M2 con la etiqueta «Carga RCM-…». Está **Descargando**; Recibido = 6. | |
| 4.8 | En el recibo de M2, cambia Recibido de P2 a **5** (llegó incompleto). | P2 = 5 | «Falta 1». Estatus **Descargando**. Vuelve a la ficha de la carga (botón **Cerrar** y reabre la Carga). | |
| 4.9 | Pulsa **Confirmar todo**. | — | Diálogo «¿Confirmar todos los recibos?»: «Se confirman juntos los recibos de RCM-… que aún no lo están, con lo capturado en cada uno. Si uno no se puede confirmar, no se confirma ninguno.» Confirma. | |
| 4.10 | Revisa los tres recibos. | — | Recibo de M1: **Completado**. Recibo de M3: **Completado**. Recibo de M2: **Completado con diferencia** (faltó 1). La carga pasa a **Confirmada**; el avance dice «3 de 3 recibos confirmados». Los tres avisos pasan a **Recibida**. | |
| 4.11 | Ve a **Recibo → Acomodo pendiente**. | — | Aparecen los tres recibos con acomodo por hacer. Elige cada uno y pulsa **Iniciar todas** y **Completar todas**. | |
| 4.12 | Prueba eliminar una carga: crea otros dos avisos (M4: P1 × 1, M5: P2 × 1), ábrelos con **Recibir juntos** y, **sin confirmar**, pulsa **Eliminar** en la ficha. | — | Diálogo «¿Eliminar la carga?»: «Se elimina RCM-… y todos sus recibos (ninguno está confirmado). Los avisos quedan libres para recibirse de nuevo.» Los avisos M4 y M5 vuelven a **Esperada**. | |

---

## Libreto 5 — Recibir un aviso y acomodar (aparato móvil)

**Objetivo:** recibir **todo** un aviso con el aparato, mandarlo al servidor y acomodar.
**Datos:** crea en la web el aviso **N1**: ALM-DEPOT · AxisCare · Referencia **PRUEBA-N1** · líneas **P1 × 12** y **P2 × 6**. Opcionalmente **Asígnalo** a tu usuario (así sale en «Mis tareas»).

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 5.1 | En el aparato, abre **Mis tareas**. | — | Si N1 está asignado a ti, sale en «Por recibir (1)» con «Llega {fecha}». | |
| 5.2 | Vuelve al menú y toca **Recibir**. | — | Título «Recibir». Campo «Escanea la orden o el aviso». Lista de avisos asignados a ti (con «Asignado a ti»). Si N1 no está asignado: toca «Ver lo que nadie tiene asignado» y aparece con «Sin asignar (al recibirlo queda a tu nombre)». | |
| 5.3 | Escribe la referencia: toca **⌨**, escribe **PRUEBA-N1** (o el número LLE-) y **Aceptar**. | PRUEBA-N1 | Abre el recibo de N1. (Si te pregunta el modo: toca **Con acomodo**: «Entra a la zona de recepción y se acomoda después con tareas».) | |
| 5.4 | Mira la pantalla. | — | Título «Aviso …». Bloque «Lo que dice el documento»: «P1 … Esperado 12 · Recibido 0 · Falta 12», «P2 … Esperado 6 · Recibido 0 · Falta 6». «Ya recibido de este documento» vacío o en cero. | |
| 5.5 | Toca el renglón de P1 (o escanea / escribe su SKU). | SKU de P1 | Se abre la captura de la línea: «Cantidad» viene en **1**. | |
| 5.6 | Cambia la **Cantidad** a **12**. Toca **Agregar**. | 12 | Sale en «Líneas capturadas»: «12 × 10270KDRSV-1». El renglón del documento muestra ✓ (completo). | |
| 5.7 | Toca el renglón de P2, cantidad **6**, **Agregar**. | 6 | «Líneas capturadas»: dos líneas. Resumen: «Esperado 18 · Recibido 18 · Dañado 0». | |
| 5.8 | Prueba «Quitar»: en la línea de P2 toca **Quitar**. | — | Diálogo «¿Quitar esta línea?» / «Se quita 6 {sku} de este recibo. Las demás líneas no cambian.» Toca **Quitar**. Resumen baja a «Recibido 12». Vuelve a agregar P2 × 6. | |
| 5.9 | Prueba un producto que el aviso **no** trae: escanea/teclea el SKU de otro producto que no esté en N1 (ej. un producto de otro cliente). | — | «Ese producto ({sku}) no está en {aviso}: solo se recibe lo que dice el documento.» o «No hay un producto con ese código.» No se agrega. | |
| 5.10 | Toca **Confirmar recibo**. | — | Regresa al inicio. Aviso «Recibo enviado a la cola» / «Se guardó en el aparato; se enviará cuando haya señal.» | |
| 5.11 | En el menú toca **Sincronizar ahora** (si no se envió solo). | — | «Todo enviado.» | |
| 5.12 | En la **web**, **Recibo**: busca el recibo de N1. | — | Estatus **Completado**. Aviso N1 **Recibida**. Panel «Tareas de acomodo» con P1 × 12 y P2 × 6 **Pendiente**. | |
| 5.13 | En el aparato toca **Acomodar**. | — | Lista de tareas: «Asignada a ti» / «Sin asignar (al acomodarla queda a tu nombre)», con «Almacén ALM-DEPOT» y «Desde R1». (Acomodar necesita señal.) | |
| 5.14 | Toca la tarea de P1. | — | Muestra «Posición sugerida» y el campo «Escanea la posición destino»; «Pendiente de acomodar: 12». | |
| 5.15 | Toca la **posición sugerida** (o escanéala/escribe su código). | — | «Listo: {sku} quedó en {posición}». | |
| 5.16 | Repite con P2. | — | «Listo: …». La lista queda vacía: «No hay tareas de acomodo pendientes.» | |
| 5.17 | En la web, vuelve a mirar el recibo. | — | Estatus **Acomodado**; tareas **Completada**; asignadas a ti. | |
| 5.18 | **Variante — repartir en varias posiciones**: con otro aviso (N1b: P1 × 12), en 5.14 escribe «Cantidad por posición (opcional)» = **5** y escanea una posición; luego «Escanea la siguiente posición». | cantidad por posición 5 | Muestra «Repartido: 5 · quedan 7 sin acomodar». Sigue con otra posición (5) y otra (2); al terminar toca **Confirmar reparto**. «Listo: {sku}, 12 en 3 posición(es)». | |

---

## Libreto 6 — Recibir por partes, acomodar y seguir recibiendo (aparato móvil)

**Objetivo:** reproducir el caso real: se descarga, **no cabe en staging**, se recibe una parte, se acomoda y se vuelve a recibir lo que falta. Ver el estatus **Descargando** en la web mientras tanto.
**Datos:** aviso **N2**: ALM-DEPOT · AxisCare · Referencia **PRUEBA-N2** · línea **P1 × 15**.

**Preparar el aviso de «staging lleno» (opcional pero recomendado):** en la web, **Almacén → Posiciones**, abre la posición **R1** (zona de staging) y ponle un **cupo máximo de 8 unidades** (campo de capacidad máxima de la posición). Sin cupo, el aviso amarillo no aparece. Anota el cupo original para devolverlo al terminar.

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 6.1 | En el aparato: **Recibir** → abre N2 (por referencia **PRUEBA-N2**). Si pregunta, modo **Con acomodo**. | — | «Lo que dice el documento»: P1 «Esperado 15 · Recibido 0 · Falta 15». | |
| 6.2 | Captura P1 con cantidad **10**. | 10 | Sale la **advertencia amarilla**: «Lo capturado (10) no cabe en el staging (libre: 8). Reciba una parte y acomode antes de seguir.» No bloquea. | |
| 6.3 | Quita la línea (**Quitar**) y captura P1 con cantidad **5**. | 5 | La advertencia desaparece (5 caben en 8). Resumen «Esperado 15 · Recibido 5 · Dañado 0». | |
| 6.4 | Mira los botones. | — | De arriba abajo: **Recibir una parte** (con su ayuda: «Confirma lo capturado hasta ahora: entra al staging y ya se puede acomodar. El recibo sigue abierto para lo que falte; «Confirmar recibo» lo cierra.»), **Confirmar recibo** (con su ayuda: «Cierra el recibo y crea las tareas de acomodo.») y **Cancelar recibo** (rojo). | |
| 6.5 | Toca **Recibir una parte**. | — | Regresa al inicio. Se encola la parte; **Sincronizar ahora**. | |
| 6.6 | En la **web**: abre el recibo de N2 (Almacén → Recibo, filtra por el aviso). | — | Estatus **Descargando**. Línea de P1: Esperado **15**, Recibido **5**, **«Falta 10»**. En «Tandas confirmadas»: «Tanda 1 … 5 u. en 1 línea(s)». Tarea de acomodo de **P1 × 5** pendiente. El aviso N2 sigue **Esperada**. | |
| 6.7 | En el aparato: **Acomodar** → tarea de P1 × 5 → posición sugerida. | — | «Listo: {sku} quedó en {posición}». | |
| 6.8 | «Sigue llegando mercancía»: **Recibir** → N2 de nuevo. | — | El bloque «Ya recibido de este documento» muestra «10270KDRSV-1: 5 de 15» y el renglón del documento «Esperado 15 · Recibido 5 · Falta 10». | |
| 6.9 | Captura P1 con cantidad **5** y toca **Recibir una parte**. **Sincronizar ahora.** | 5 | En la web: «Tanda 2 … 5 u.», **Recibido 10**, **«Falta 5»**, estatus **Descargando**. Nueva tarea de acomodo de P1 × 5. | |
| 6.10 | Acomoda esa tarea en el aparato. | — | «Listo…». El recibo sigue **Descargando**. | |
| 6.11 | **Recibir** → N2 → captura P1 × **5** (lo último) → **Confirmar recibo**. **Sincronizar ahora.** | 5 | «Recibo enviado a la cola». En la web: estatus **Completado**; aviso N2 **Recibida**; tandas 1 y 2 más la confirmación final; tarea de acomodo de P1 × 5. | |
| 6.12 | Acomoda la última tarea en el aparato. | — | Recibo **Acomodado**. P1 total sumó **15**. | |
| 6.13 | Prueba **Cancelar recibo**: crea otro aviso N2b (P1 × 3), ábrelo en el aparato, captura 1 unidad y toca **Cancelar recibo** (rojo). | — | Diálogo «¿Cancelar este recibo?» / «Se pierde todo lo capturado. No se puede deshacer.» Al confirmar, vuelve al menú y **no** se crea ningún recibo en la web. | |
| 6.14 | **Limpieza:** devuelve el cupo de R1 a su valor original. | — | — | |

---

## Libreto 7 — Recibir varios avisos juntos (aparato móvil)

**Objetivo:** con el aparato, seleccionar dos avisos del mismo dueño y recibirlos en una sola pantalla.
**Datos:** avisos **N3** (ALM-DEPOT · AxisCare · PRUEBA-N3 · P2 × 6) y **N4** (PRUEBA-N4 · P3 × 4).

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 7.1 | En el aparato: **Recibir**. Ve a «Ver lo que nadie tiene asignado» si no los asignaste. | — | Se ven N3 y N4. Con más de un documento a la vista aparece **Seleccionar varios**. | |
| 7.2 | Toca **Seleccionar varios** y marca **N3** y **N4**. | — | Los dos quedan marcados. Aparece el botón **Recibir 2 seleccionados** y **Cancelar selección**. | |
| 7.3 | Toca **Recibir 2 seleccionados**. | — | Una sola pantalla de recibo, con título «LLE-… + LLE-…». «Lo que dice el documento» lista P2 y P3. | |
| 7.4 | Captura P2 × **6** y P3 × **4**. | — | Líneas capturadas: «6 × P2» y «4 × P3». Resumen «Esperado 10 · Recibido 10». | |
| 7.5 | Toca **Confirmar recibo**; **Sincronizar ahora**. | — | Se envía. | |
| 7.6 | En la web: **Recibo → Cargas**. | — | Aparece una **Carga RCM-…** con 2 documentos (N3 y N4), con los dos recibos **Completado**. Cada recibo lleva la etiqueta «Carga RCM-…». Los avisos N3 y N4 pasan a **Recibida**. | |
| 7.7 | Acomoda las tareas en el aparato (**Acomodar**). | — | Ambos recibos pasan a **Acomodado**. | |
| 7.8 | Prueba el rechazo por dueño distinto (solo si tienes un aviso de otro cliente): en **Seleccionar varios** intenta marcar un aviso de otro dueño. | — | El aviso sale atenuado o se rechaza: «Ese documento es de otro dueño que los de esta tanda. Termina los de la tanda para recibirlo.» | |

---

## Libreto 8 — Avisos de salida (web)

**Objetivo:** pedir mercancía con un aviso de salida; ver cómo se reserva lo que hay y queda pendiente lo que falta; cambiar cantidades; cancelar; despachar desde la web.
**Requisito:** que el inventario de **P1, P2 y P3** ya esté acomodado (libretos 2 a 7).
**Pantalla:** **Almacén → Llegadas y salidas → pestaña Avisos de salida**.

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 8.1 | Pestaña **Avisos de salida** → **Nuevo aviso de salida**. | — | Modal con Almacén, Cliente dueño, Referencia, Fecha requerida, Destino, Notas y «Productos que se piden». Ayuda: «No se rechaza por falta de existencia: se reserva lo que hay y el resto queda pendiente. Sin precio: no es una venta.» | |
| 8.2 | Intenta **Guardar** vacío. | — | «Escoja el cliente dueño de la mercancía.» y el error de almacén. No se guarda. | |
| 8.3 | Llena el aviso **S1**. | Almacén **ALM-DEPOT** · Cliente **AxisCare** · Referencia **PRUEBA-S1** · Destino «Cliente de prueba, San Juan» · líneas **P1 × 5**, **P2 × 3** | — | |
| 8.4 | Guarda. | — | «Aviso de salida SAL-… creado.» En la lista: estatus **Solicitado**, Pedido 8, **Despachado 0**. **Anótalo como S1.** | |
| 8.5 | Abre la ficha de S1 (clic en la fila). | — | Título «Aviso de salida SAL-…». Columnas Producto, Lote, Pedido, **Reservado**, **Pendiente**, Despachado. Reservado = Pedido (hay existencia); Pendiente = 0. | |
| 8.6 | Crea el aviso **S2** para ver el pendiente: Referencia **PRUEBA-S2**, línea **P3 × 500** (mucho más de lo que hay). | P3 = 500 | Mensaje «Aviso de salida SAL-… creado; 1 línea(s) con pendiente.» No hay error por falta de existencia. | |
| 8.7 | Abre S2. | — | **Reservado** = lo que había de P3; **Pendiente** = el resto. La lista de avisos tiene el filtro **Solo con pendiente** (actívalo: aparece S2). | |
| 8.8 | En S2, línea de P3: pulsa **Cambiar cantidad**. | Cantidad pedida **8** | Diálogo «Cambiar la cantidad de {sku}»; mensaje «Cantidad actualizada.» Si había lo suficiente: Pendiente = 0. | |
| 8.9 | Crea **S3**: Referencia **PRUEBA-S3**, línea **P1 × 1000**. Abre la ficha y pulsa **Cerrar con faltante** en la línea de P1. | — | «La línea de {sku} se cerró con faltante.» La línea muestra la marca «Cerrada». Si el aviso no despachó nada y todas sus líneas se cierran, pasa a **Cancelado**. | |
| 8.10 | Crea **S4**: Referencia **PRUEBA-S4**, línea **P2 × 1**. Pulsa **Cancelar aviso** en su fila. | — | Diálogo «Cancelar el aviso de salida»: «Se cancela el aviso SAL-…: se libera lo que tenía reservado y se borra el pendiente. No se puede deshacer.» Al confirmar: «Aviso de salida cancelado.» Estatus **Cancelado**. | |
| 8.11 | Despachar desde la web: en S1 pulsa **Despachar**. | — | Ventana «Despachar contra SAL-…»: «Se despachará todo lo reservado: 8 unidades en 2 líneas.» Botón **Despachar todo lo reservado**. Hay también **Ajustar cantidades**. | |
| 8.12 | Pulsa **Ajustar cantidades**. En P2 escribe **0** (esa línea no sale) y deja P1 en **5**. Pulsa **Despachar**. | P1 = 5 · P2 = 0 | «Despacho … registrado.» (con el número que le asigne el sistema). Estatus de S1: **Despachado parcial**. «Despachado: 5». | |
| 8.13 | Prueba el límite: vuelve a **Despachar** en S1 → **Ajustar cantidades** → en P2 escribe **4** (hay reservado 3). | P2 = 4 | «No puede despachar más de lo reservado.» | |
| 8.14 | Pon P2 en **3** y despacha. | P2 = 3 | Despacho registrado. S1 pasa a **Despachado**. En la ficha, sección «Despachos» lista los dos despachos. | |
| 8.15 | En **Recolección y empaque → Por documento**, busca S1. | — | Aparece el aviso con «Sacado 8 de 8» y una o dos recolecciones **EMP-…** enlazadas. | |

---

## Libreto 9 — Despachar con el aparato móvil

**Objetivo:** despachar un aviso de salida con el aparato (completo y parcial), despachar varios juntos y probar el despacho sin documento.
**Datos:** crea en la web estos avisos de salida, del cliente **AxisCare** y almacén **ALM-DEPOT**:

| Aviso | Referencia | Líneas |
|---|---|---|
| **S5** | PRUEBA-S5 | P1 × 4 · P2 × 2 |
| **S6** | PRUEBA-S6 | P1 × 6 |
| **S7** | PRUEBA-S7 | P2 × 3 |
| **S8** | PRUEBA-S8 | P3 × 2 |

Asígnalos a tu usuario (o búscalos en «Ver lo que nadie tiene asignado»).

### 9A. Despachar un aviso completo

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 9.1 | En el aparato, toca **Despacho**. | — | Pantalla única: campo «Número del aviso» (ayuda «Escanea o escribe el número del aviso (SAL-…), o toca uno de la lista.»), lista de avisos asignados («Por despachar: …», «requerido el …») y «Ver lo que nadie tiene asignado». Si la compañía no exige documento, también «Despachar sin documento». | |
| 9.2 | Escribe el número de **S5** (**SAL-…**) y **Aceptar** (o tócalo en la lista). | — | Abre «Despachar aviso de salida». Lista «Falta por sacar» con P1 y P2. | |
| 9.3 | Toca el renglón de P1 (o escanea / teclea su SKU en «Producto a sacar»). | — | Pide «Posición de donde sacas», mostrando la posición que reservó el sistema. | |
| 9.4 | Escanea (o teclea) esa posición. | — | «Reservado: 4. Cantidad que sacas:» | |
| 9.5 | Prueba el límite: escribe **9**. | 9 | «No puedes sacar más de lo reservado (4).» | |
| 9.6 | Escribe **4**. | 4 | La línea queda en «Ya sacado» (con «Toca para deshacer»). | |
| 9.7 | Repite con P2: posición y cantidad **2**. | 2 | «Listo: no quedan más posiciones. Toca Terminar.» | |
| 9.8 | Toca **Terminar**. | — | Título «Terminar SAL-…», «2 posiciones · 6 piezas» y la ayuda «Se descuenta del inventario y se registra el despacho contra el aviso. Lo que no saques queda reservado.» | |
| 9.9 | Toca **Despachar**. | — | «Despacho enviado: …» (o «Despacho guardado: se envía cuando haya señal.»). **Sincronizar ahora.** | |
| 9.10 | En la web: abre S5. | — | Estatus **Despachado**. Sección «Despachos»: el despacho nuevo. Inventario: P1 − 4, P2 − 2. En «Recolección y empaque → Por documento» se ve la recolección **EMP-…** sin empacar. | |

### 9B. Despacho parcial

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 9.11 | Abre **S6** (P1 × 6) en el aparato. Saca P1 con cantidad **4** (de 6) y toca **Terminar** → **Despachar**. | 4 de 6 | Despacho enviado. | |
| 9.12 | En la web mira S6. | — | Estatus **Despachado parcial**; Despachado **4**; lo que no se sacó **sigue reservado** (Reservado 2). | |
| 9.13 | En el aparato, abre S6 otra vez y saca los **2** restantes. | 2 | Al sincronizar, S6 pasa a **Despachado**. | |

### 9C. Despachar varios avisos juntos (recolección maestra)

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 9.14 | En **Despacho**, toca **Seleccionar varios**. | — | Ayuda: «Toca los avisos y las órdenes de venta que salen juntos (mínimo 2, del mismo cliente). Se recoge todo en una sola ruta y queda una recolección maestra con un solo envío.» | |
| 9.15 | Marca **S7** y **S8**. | — | Los dos marcados. (Uno de otro cliente saldría atenuado: «Es de otro cliente: una recolección maestra es de un solo cliente.») Botón **Despachar 2 seleccionados**. | |
| 9.16 | Toca **Despachar 2 seleccionados**. | — | Banner «Recolección maestra · AxisCare». «Producto a sacar» sirve para cualquiera de los dos avisos. | |
| 9.17 | Saca P2 × 3 (de S7) y P3 × 2 (de S8), cada uno escaneando producto, posición y cantidad. | — | Las líneas se marcan «Ya sacado». | |
| 9.18 | Toca **Terminar** → **Despachar**. | — | «Recolección maestra … enviada.» (con señal). **Sincronizar ahora.** | |
| 9.19 | En la web, **Recolección y empaque → vista Cargas**. | — | Aparece una **carga recolectada** con S7 y S8, en estatus **Recolectada**. | |

### 9D. Despacho sin documento (solo si la compañía no lo exige)

> En **Ajustes de la compañía → Operación**, la opción «El despacho exige documento» define si existe «Despachar sin documento». Si en la pantalla de Despacho **no** aparece ese botón, anota «no aplica: la compañía exige documento» y sigue.

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 9.20 | En **Despacho** toca **Despachar sin documento**. | — | Pantalla «Despacho» con «Escanea el producto» (código de barras o SKU). | |
| 9.21 | Escribe **Cantidad 2** y luego escanea P1; después «Escanea de qué posición sale». | P1 × 2 | La línea aparece en «Líneas recolectadas». Si escaneas primero sin cantidad: «Escribe la cantidad primero y luego escanea la posición.» | |
| 9.22 | Toca **Completar despacho**. | — | Pide «¿Por qué sale sin entrega?» con los motivos: Muestra, Uso interno, Retiro del dueño, Retiro del cliente, Venta, Otro. Elige **Muestra**. | |
| 9.23 | Confirma. | — | «Despacho enviado a la cola». Tras sincronizar queda un despacho manual **DMA-…**. | |

---

## Libreto 10 — Recolección y empaque (web)

**Objetivo:** recolectar inventario, empacarlo para crear un envío, eliminar una recolección y recolectar documentos juntos.
**Pantalla:** **Almacén → Recolección y empaque** (pestaña «Recolecciones»; a la izquierda el panel «Recolección» y a la derecha la lista).
**Requisito:** inventario suficiente de P1, P2 y P3.

| # | Qué haces | Datos | Resultado esperado | ✔/✘ |
|---|---|---|---|---|
| 10.1 | Abre la pantalla. | — | Título «Recolección y empaque». Pestañas «Recolecciones» y «Reabasto». Lista con las vistas «Por documento» (por omisión), «Cargas» y «Por movimiento». | |
| 10.2 | En el panel «Recolección», elige el almacén. | **ALM-DEPOT** | Aparece la grilla «Líneas a recolectar» y la nota «Una recolección admite productos de un solo dueño. Sin posición ni lote, el sistema elige por FEFO.» | |
| 10.3 | Agrega una línea: producto **P1**, cantidad **3**, posición «Automático (FEFO)». | — | El selector de posición solo ofrece posiciones con existencia de P1 y sugiere la primera. | |
| 10.4 | Pulsa **Recolectar (bajar de inventario)**. | — | «Recolección EMP-0000N creada.» Estatus **Recolectada**. El inventario de P1 baja en 3. **Anótala como E1.** | |
| 10.5 | Ve a la vista **Por movimiento**. | — | E1 aparece con su producto, cantidad y de qué posición salió. | |
| 10.6 | Prueba un error: nueva recolección sin líneas, pulsa **Recolectar**. | — | «Indique al menos una línea a recolectar.» | |
| 10.7 | Prueba existencia insuficiente: **P1 × 99999**. | — | «Inventario insuficiente de {sku} en {posición}: disponible …, solicitado 99999.» | |
| 10.8 | Prueba dos dueños: agrega P1 y un producto de **otro cliente** en la misma recolección. | — | «Una recolección solo puede tener productos de un mismo dueño.» | |
| 10.9 | **Empacar** E1: en su fila (o ficha) pulsa **Empacar**. | — | Modal «Empacar EMP-…» con: Cliente del envío, Tipo de servicio, Consignatario («Del directorio» / «Nuevo»), Número de envío, Factura del cliente, Paquetes (Tipo, Descripción, Piezas, Peso kg) y Notas. | |
| 10.10 | Llena el empaque. | Cliente **AxisCare** · un tipo de servicio · un consignatario del directorio · Factura **FAC-PRUEBA-1** · 1 paquete: Piezas **3**, Peso **2** kg | — | |
| 10.11 | Pulsa **Empacar y crear envío**. | — | «Empacada: se creó el envío {orden}.» E1 pasa a **Empacada**. | |
| 10.12 | Ve a **Envíos** y busca el envío creado. | — | Existe, con el consignatario y el paquete que pusiste. | |
| 10.13 | Intenta **Eliminar** E1 (ya empacada). | — | Si el envío ya avanzó de estatus: «La orden de la recolección … ya avanzó a '…'; la recolección ya no se puede eliminar.» Si no avanzó, se elimina y la orden se cancela (requiere el permiso `orders.cancel`). | |
| 10.14 | Crea otra recolección **E2**: **P2 × 2** y **no la empaques**. En su fila pulsa **Eliminar**. | — | Diálogo «¿Eliminar la recolección EMP-…? El inventario vuelve a su posición original.» Al confirmar: «Recolección EMP-… eliminada.» El inventario de P2 vuelve a subir en 2. | |
| 10.15 | **Recolectar juntos**: crea dos avisos de salida del mismo cliente (**S9**: P1 × 1; **S10**: P2 × 1). En **Por documento** pulsa **Recolectar juntos**. | — | Diálogo con la ayuda «Elija los documentos que salen juntos, en un solo envío. Deben ser del mismo cliente…». | |
| 10.16 | Marca **S9** y **S10**. Pulsa **Recolectar juntos (2)**. | — | «Carga recolectada … creada.» La vista **Cargas** muestra la carga con los dos documentos y sus movimientos («de dónde salió»). | |
| 10.17 | En la ficha de la carga pulsa **Empacar**. | — | Se arma **un solo envío** para toda la carga. | |
| 10.18 | **Eliminar** otra carga (crea S11 y S12 y recolécalos juntos primero). | — | «Se deshacen todas las recolecciones de …: el inventario vuelve a su posición y cada documento recupera su reserva.» | |
| 10.19 | Mira la pestaña **Reabasto** (opcional). | — | Lista de tareas de reabasto y el botón «Correr reabasto». | |

---

## Anexo A — Estatus que debes ver (referencia rápida)

| Qué | Estatus (en pantalla) | Cuándo |
|---|---|---|
| **Aviso de llegada** | Esperada · Recibida · Cancelada | Recibida al **confirmar** el recibo (no antes). |
| **Aviso de salida** | Solicitado · Despachado parcial · Despachado · Cancelado | «Con pendiente» es una marca, no un estatus. |
| **Recibo** | **Esperado** (sin líneas) | Recibo ciego/devolución solo con encabezado. |
| | **Descargando** | Con líneas; todo cuadra o **solo falta recibir**. Es el estatus de «estoy recibiendo y acomodando por partes». |
| | **Discrepancia** | Solo si algo trae **más** de lo esperado. Se quita sola al corregir. |
| | **Completado** | Confirmado sin diferencia. |
| | **Completado con diferencia** | Confirmado con faltante o sobrante. |
| | **Acomodado** | Se cerró la última tarea de acomodo. |
| **Tarea de acomodo** | Pendiente · En proceso · Completada | — |
| **Carga recibida (RCM)** | Abierta · Confirmada · Eliminada | — |
| **Recolección (EMP)** | Recolectada · Empacada · Eliminada | — |

**Regla que se verifica en el libreto 3:** una tanda **no cambia el estatus** del recibo; solo «Confirmar recibo» lo cierra.

## Anexo B — Mensajes de error que conviene provocar a propósito

| Dónde | Cómo provocarlo | Mensaje esperado |
|---|---|---|
| Nuevo aviso de llegada | Guardar vacío | «Elija el almacén.» / «Indique el cliente dueño…» / «Indique al menos una línea.» |
| Aviso de llegada | Cantidad 0 | «La cantidad esperada debe ser mayor que cero.» |
| Aviso de llegada | Cancelar uno que ya tiene recibo abierto | «El aviso de llegada tiene un recibo abierto; elimínelo antes de cancelar.» |
| Recibo (tandas) | Bajar Recibido por debajo de lo confirmado | «No se puede dejar lo recibido por debajo de lo ya confirmado en tandas.» |
| Recibo | Eliminar un recibo con tandas | «El recibo ya tiene tandas confirmadas: no se puede eliminar.» |
| Recibir juntos | Elegir un solo documento | «Elija al menos dos documentos.» |
| Aparato, Recibir | Producto que el aviso no trae | «Ese producto ({sku}) no está en {doc}: solo se recibe lo que dice el documento.» |
| Aparato, Recibir | Capturar más de lo que cabe en staging | «Lo capturado (N) no cabe en el staging (libre: M). Reciba una parte y acomode antes de seguir.» |
| Aviso de salida | Despachar más de lo reservado | «No puede despachar más de lo reservado.» |
| Aviso de salida | Cancelar uno ya despachado | «Solo se puede cancelar un aviso de salida solicitado que no se haya despachado.» |
| Aparato, Despacho | Sacar más de lo reservado | «No puedes sacar más de lo reservado ({qty}).» |
| Recolección | Existencia insuficiente | «Inventario insuficiente de {sku} en {posición}: disponible …, solicitado ….» |
| Recolección | Dos dueños | «Una recolección solo puede tener productos de un mismo dueño.» |

## Anexo C — Registro de hallazgos (llénalo mientras pruebas)

| # | Libreto y paso | Qué esperaba | Qué pasó (con captura) | Gravedad (bloquea / molesta / texto) |
|---|---|---|---|---|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |

## Anexo D — Qué este libreto **no** prueba (y por qué)

- **Productos con lote o serie** y **unidades dañadas**: tienen sus propios campos («Lote y series», «Vinieron unidades dañadas»). Conviene un libreto aparte.
- **Recibos contra orden de compra**: exigen el módulo de Compras y el permiso `purchasing.receive`; el rol «Operador de almacén» no los tiene.
- **Recibo directo a posición** (almacenes de modo «Directo», como Advance Solutions): cada línea lleva su posición destino y no hay tareas de acomodo ni tandas. Si quieres probarlo, usa un almacén en ese modo.
- **Recibo y despacho exigiendo documento**: en **Ajustes de la compañía → Operación** puedes cambiar «El recibo exige documento» y «El despacho exige documento»; con eso cambian los botones «Recibo ciego» y «Despachar sin documento».
- **Conteo cíclico, transferencias y ajustes**: otra área del almacén.

## Anexo E — Si algo no sale como dice el libreto

1. **No ves «Descargando»** sino «Recibiendo»: la base no tiene aplicada la actualización del 2026-10-21 (`db-update`). Avisa al equipo técnico.
2. **El aparato no recibe/despacha lo que acabas de crear en la web:** toca **Sincronizar ahora** en el menú.
3. **El aviso no aparece en «Recibir» del aparato:** puede estar asignado a otra persona o ya tener un recibo; revisa en la web su columna «Asignado a» y «Recibo».
4. **No aparece el botón «Recibir una parte»:** solo existe con un documento, en almacenes con acomodo y sin modo directo.
5. **No aparece la advertencia de staging lleno:** ninguna posición de staging tiene cupo máximo configurado.
6. **No ves un botón de la web (Empacar, Asignar, Cancelar…):** falta el permiso del usuario (ver la tabla de la sección 1.1).
