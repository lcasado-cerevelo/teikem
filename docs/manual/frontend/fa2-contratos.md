# F-A2 — Contratos del cliente: Contrato, Tarifas, SLA y Servicios especiales

Dentro de la ficha del cliente (**Catálogo → Clientes**, `/catalog/clients`, ver [F-A1](fa1-clientes.md)) el panel **Contratos** pasó de solo lectura a una sección con cuatro
pestañas: **Contrato · Tarifas · SLA · Servicios especiales**. El resto de la ficha (cabecera, perfil, teléfonos, personas, numeración, campos personalizados e
historial) no cambió. Backend: capítulo 02 del manual, secciones 4 a 6. Decisiones del frontend: `docs/frontend/loteFA2-decisiones.md`.
Capturas: `img/fa2-contrato.png`, `img/fa2-sla.png`, `img/fa2-tarifas.png`, `img/fa2-especiales.png` (y `img/fa1-*.png` de la lista, el alta y la ficha).

**Quién puede.** El módulo **Catálogo** (`CATALOG`) encendido, como toda la pantalla. Cada parte pide su permiso y, sin él, el botón no se pinta y los campos quedan de solo lectura:

| Acción | Permiso |
|---|---|
| Ver la sección (contrato, tarifas, SLA, servicios especiales, historial del contrato) | `contracts.read` |
| Crear un contrato adicional (**Nuevo contrato**) | `contracts.create` |
| Guardar datos, modelo de facturación, SLA, estatus, tarifas, tramos y servicios especiales | `contracts.update` |

Un usuario con `clients.read` pero sin `contracts.read` ve la ficha completa; en **Contratos** solo se lee el resumen de facturación y el aviso «Su usuario no puede ver los
contratos (permiso contracts.read)».

**Solo se escribe si el estatus lo permite.** Con el contrato en un estatus que no admite edición (por omisión **Vencido** o **Cancelado**) la sección muestra «El estatus
actual del contrato no permite editarlo; solo se puede cambiar de estatus.» y todo queda de solo lectura (el servidor lo anticipa con `canEdit`).

## 1. Cabecera de la sección

- Resumen **Facturación:** del contrato vigente (p. ej. «Por servicio, COD»).
- **Nuevo contrato** (`contracts.create`): modal con Título (obligatorio, máx. 200), Número (vacío = lo genera el sistema, `{Código}-C{n}`), **Cliente desde**, Fecha fin,
  Moneda, Disparador de cobro, Renovación automática y Notas. Nace en **Borrador** con «Por servicio» encendido. No pide SLA (tiene su pestaña). Al guardar, el contrato nuevo
  queda elegido.
- **Selector de contrato** (pestañas Contrato, Tarifas y SLA): lista todos los contratos del cliente con su estatus y marca con «(contrato vigente)» el que usan las órdenes y los servicios especiales; por omisión se abre el vigente.
  Los **Servicios especiales** son del cliente y se aplican siempre a su contrato vigente.

## 2. Pestaña Contrato

De arriba abajo:

1. **Estatus del contrato** (etapas **Borrador → Vigente**, con **Vencido** y **Cancelado** como cierres). Los botones ofrecen solo las transiciones válidas («Avanzar a …», «Pasar a …») y piden un
   comentario opcional. Si el servidor rechaza (422) el mensaje queda en el diálogo.
2. **Datos del contrato**: Número (solo lectura), Título, **Cliente desde** (inicio del contrato; decide cuál es el vigente), Fecha fin (vacía = sin fecha fin; se envía
   `clearEndDate`), Renovación automática (informativa: no hay vencimiento ni renovación automáticos), Moneda, Disparador de cobro (Por recogido / Por entrega / Mixto) y Notas.
   **Guardar** solo se activa si hay cambios y envía solo lo que cambió, con el `rowVersion` de la ficha.
3. **Modelo de facturación**: los cinco interruptores **Por servicio, Pieza extra con precio especial, Cargo por despacho, Cargo por COD, Servicios especiales** y su resumen.
   Con **Cargo por despacho** marcado aparece el monto fijo por orden; con **Cargo por COD** marcado, el tipo (**Fijo ($)** o **Porciento (%)**) y el valor (el por ciento se
   toma del monto COD cobrado, no del total de la orden). Desmarcar un interruptor **no borra** lo configurado: se conserva y deja de cobrarse. Un check recién marcado pide
   su monto.
4. **Historial de estatus** del contrato.

Mensajes (salen bajo el campo; el servidor los manda tal cual):

| Caso | Mensaje | HTTP |
|---|---|---|
| Título vacío / largo | `El título del contrato es obligatorio.` · `Máximo 200 caracteres.` | 400 |
| Fin anterior al inicio | `La fecha fin no puede ser anterior a la fecha de inicio.` | 400 |
| Fecha fin y borrar a la vez | `Indique una fecha fin o márquela para borrar, no ambas.` | 400 |
| Otra persona guardó antes | `El contrato fue modificado por otro usuario; recargue e intente de nuevo.` — el contrato se vuelve a leer y lo que usted escribió se conserva; pulse **Guardar** otra vez | 409 |
| Estatus que no permite editar | `El estatus actual no permite la acción 'EDIT_CONTRACT'.` | 422 |
| Despacho negativo | `El cargo por despacho no puede ser negativo.` | 400 |
| COD fijo negativo / por ciento fuera de 0–100 | `El cargo fijo por COD no puede ser negativo.` · `El por ciento del cargo por COD debe estar entre 0 y 100.` | 400 |
| Activar con el cliente suspendido | `No se puede activar el contrato: el cliente está suspendido. Reactive al cliente primero.` | 422 |
| Activar con otro contrato ya activo | `El cliente ya tiene un contrato vigente. Cancele o expire el contrato anterior antes de activar este.` | 422 |
| Número de contrato repetido (alta) | `Ya existe un contrato con el número 'X'.` | 409 |
| Cliente dado de baja (alta) | `El cliente está dado de baja; solo se consulta su historial.` | 409 |
| Contrato no encontrado | `Contrato no encontrado.` | 404 |

## 3. Pestaña SLA

Niveles de servicio del contrato: **un renglón por tipo de servicio** del catálogo con cuatro campos — **Horas máximas de tránsito**, **Ventana de recogido (min)**,
**Meta de puntualidad (%)** y **Penalidad ($)**. Empieza vacío: un tipo con todos los campos vacíos no tiene SLA.

- Es un **borrador local**: nada se envía hasta **Guardar SLA**, que manda la lista completa (`PUT …/service-levels`); los tipos sin datos se dan de baja. **Descartar cambios**
  vuelve a lo guardado.
- Validación en pantalla (y el servidor repite): `Las horas máximas de tránsito deben ser mayores que cero.` · `La ventana de recogido (minutos) no puede ser negativa.` ·
  `La meta de puntualidad debe estar entre 0 y 100.` · `La penalidad no puede ser negativa.` (400, bajo el campo del renglón). Otros: `Tipo de servicio desconocido: 'X'.` (400) ·
  `Solo puede haber un nivel de servicio activo por tipo de servicio.` (409, si dos personas guardan a la vez; repita).

## 4. Pestaña Tarifas

Dos secciones; cada una se ve solo si su interruptor del modelo de facturación está marcado (o si ya tiene filas: entonces se ve pero **sin escrituras** y con un aviso, porque el
servidor las rechaza). Sin ninguna de las dos: «Marque «Por servicio» o «Pieza extra» en el modelo de facturación (pestaña Contrato) para configurar tarifas.»

**Historial.** Las tarifas son efectivo-fechadas: **Editar** crea una *versión nueva* (cierra la vigente en «Vigente desde» y abre otra; nunca se sobrescribe un monto),
**Quitar** *cierra* (nunca se borra). El interruptor **Ver historial** trae también las filas cerradas (atenuadas, sin acciones).

**«Vigente desde».** Por omisión es hoy (el más reciente entre el día de la compañía y el día del servidor, que usa UTC). En el *alta* de una tarifa puede ser una fecha pasada (para cargar
historial); en una *versión nueva* no puede ser anterior a hoy.

### Tarifas por servicio
Tabla **Servicio · Paquete · Tarifa · Vigente desde**. **Agregar tarifa** (servicio, paquete, tarifa, vigente desde); servicio y paquete **no se cambian** después (para cambiarlos,
quite la tarifa y agregue otra).

### Tarifas por pieza extra (por tramo)
Un bloque por servicio + paquete con su tabla de **tramos** (**Piezas** `2–5`, `6+`, **Tarifa**, **Vigente desde**). **Agregar pieza extra** crea el bloque (sin monto); **Agregar tramo**
pide *Desde pieza* (mínimo 2: la pieza 1 va en la tarifa por servicio), *Hasta pieza* (vacío = en adelante), tarifa y vigente desde. Editar un tramo es una versión nueva;
**Quitar** un tramo lo cierra, y **Quitar pieza extra** cierra el bloque con todos sus tramos abiertos.

| Caso | Mensaje | HTTP |
|---|---|---|
| Servicio / paquete / tarifa vacíos | `El tipo de servicio es obligatorio.` · `El tipo de paquete es obligatorio.` · `Indique la tarifa.` | pantalla |
| Tarifa negativa | `La tarifa no puede ser negativa.` · `La tarifa por pieza no puede ser negativa.` | 400 |
| Fecha anterior a hoy en versión nueva o cierre | `La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe.` | pantalla / 400 |
| Ya hay una tarifa vigente del mismo par | `Ya existe una tarifa vigente en esa fecha para ese servicio y tipo de paquete en el contrato; edítela o ciérrela antes de crear otra.` | 409 |
| Componente apagado | `El componente 'Por servicio' está apagado en el modelo de facturación del contrato; enciéndalo antes de trabajar sus tarifas.` (y el de «Pieza extra») | 409 |
| Fila ya cerrada | `La tarifa ya está cerrada; cree una nueva en lugar de editarla.` · `El componente ya está cerrado.` · `El tramo ya está cerrado.` | 409 |
| Rango inválido | `Rango inválido: 'desde' debe ser al menos 2 (la pieza 1 va en la tarifa por servicio) y 'hasta' debe ser mayor o igual que 'desde' o quedar vacío (abierto).` | pantalla / 400 |
| Tramos que se traslapan | `El tramo 4–7 se traslapa con el tramo vigente 2–5.` | 400 |
| Nueva vigencia o cierre anterior al inicio de la fila | `La nueva vigencia (…) no puede ser anterior al inicio de la fila actual (…).` · `La fecha (…) no puede ser anterior al inicio de vigencia de la fila (…).` | 400 |
| Componente o tramo inexistente | `Componente de tarifa '<id>' no encontrado.` · `Tramo '<id>' no encontrado.` | 404 |

## 5. Pestaña Servicios especiales

Tarifa por **tipo de servicio especial** del cliente (se aplica a su contrato vigente). Tabla **Servicio · Tarifa · Vigente desde** con **Ver historial**.

- **Agregar servicio especial**: elija un tipo existente o **+ Nuevo tipo de servicio especial…** (aparece «Nombre del nuevo servicio especial»; el tipo queda disponible para todos
  los clientes de la compañía; si ya existía uno con el mismo nombre —sin mayúsculas, acentos ni espacios dobles— se reutiliza). Más la tarifa y «Vigente desde».
- **Editar** = versión nueva de la tarifa; **Quitar** = cerrar. El tipo de una fila no se cambia. Esta pantalla no da de baja ni reactiva tipos.
- Se escribe solo con contrato vigente, «Servicios especiales» encendido en él y `canEdit`. Si no, las filas se ven y un aviso explica por qué no hay botones.

| Caso | Mensaje | HTTP |
|---|---|---|
| Sin contrato vigente | `El cliente no tiene un contrato vigente; cree o active un contrato antes de configurar servicios especiales.` | 409 |
| Componente apagado | `El componente 'Servicios especiales' está apagado en el contrato vigente; enciéndalo en el modelo de facturación para agregar servicios especiales.` | 409 |
| Tipo y nombre nuevo a la vez, o ninguno | `Indique el tipo de servicio especial: un tipo existente (typeId) o el nombre de uno nuevo (newTypeName), no ambos.` | 400 |
| Nombre vacío / largo | `El nombre del tipo de servicio especial es obligatorio.` · `El nombre del tipo de servicio especial no puede exceder 120 caracteres.` | 400 |
| Tarifa negativa | `La tarifa del servicio especial no puede ser negativa.` | 400 |
| Ya hay tarifa vigente de ese tipo | `El cliente ya tiene una tarifa vigente en esa fecha para el tipo 'X'; edite esa tarifa o ciérrela antes de agregar otra.` | 409 |
| Tipo inexistente / inactivo | `Tipo de servicio especial '<id>' no encontrado.` (404) · `El tipo de servicio especial está inactivo; reactívelo o elija otro.` (400) | 404 / 400 |
| Nombre de tipo duplicado de último momento | `Ya existe un tipo de servicio especial con el nombre 'X'.` | 409 |
| Fila ya cerrada | `La tarifa ya está cerrada; agregue un servicio especial nuevo si necesita volver a cobrarlo.` | 409 |

## Casos frecuentes
- **No veo las pestañas, solo un aviso**: falta `contracts.read`. **No veo Guardar / Agregar / Quitar**: falta `contracts.update`, o el contrato está en un estatus que no permite editar.
- **Guardé y dice «fue modificado por otro usuario»**: otra persona guardó antes; el contrato se releyó solo y su texto sigue ahí. Revise y pulse **Guardar**.
- **La sección de tarifas por pieza extra no aparece**: marque «Pieza extra con precio especial» en el modelo de facturación de la pestaña Contrato.
- **Desmarqué «Cargo por COD» y se perdió el monto**: no se pierde; vuelva a marcarlo y reaparece.
- **No puedo pasar un segundo contrato a Vigente**: un cliente tiene un solo contrato en estatus Vigente a la vez; cancele o expire el anterior primero.
- **La cotización** de una orden no está en esta pantalla: se hará junto con las órdenes de venta.
