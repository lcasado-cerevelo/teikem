# F-C1 — Órdenes: la lista y el alta detallada

Pantalla **Operación → Órdenes** (`/orders`). Aquí se consultan todas las órdenes de transporte y se **captura una orden nueva** con el formulario detallado (cliente, consignatario, paquetes y COD, o una
entrega especial). Backend: capítulo 03 del manual (§1 captura, §3 cotización y crédito, §3.5 quién ve los precios). Decisiones del frontend: `docs/frontend/loteFC1-decisiones.md`.
Esta entrega no incluye todavía la edición de una orden, sus acciones (confirmar, cancelar, asignar chofer…), la entrada rápida ni la importación por CSV: llegan en los siguientes sub-lotes.

> **Capturas.** El recorrido `web-app/e2e/loteFC1-ordenes.spec.ts` genera `img/fc1-lista.png`, `img/fc1-alta.png`, `img/fc1-lista-filas.png`, `img/fc1-alta-especial.png` e `img/fc1-eliminar.png`.
> **Todavía no se han generado** (el recorrido está escrito y no se ejecutó en esta sesión): las referencias de abajo quedan pendientes hasta correrlo.

**Quién puede.** Ver la pantalla: permiso **`orders.view`** con el módulo **LTL_GROUND** (núcleo: siempre encendido). Cada acción pide su permiso y, sin él, el botón no se pinta:

| Acción | Permiso |
|---|---|
| Ver la lista y abrir una orden | `orders.view` |
| «Nueva orden», Guardar borrador, Guardar y confirmar | `orders.create` |
| Eliminar una orden (papelera) | `orders.cancel` |
| Autorizar un crédito excedido al guardar y confirmar | `orders.credit_override` |
| Ver la columna **Cotizado** (el precio) | `orders.view_price` |
| Elegir cliente / consignatario del directorio | `clients.read` / `locations.read` |
| «+» (crear un consignatario desde la orden) | `locations.create` |
| «Es una entrega especial» | `contracts.read` (módulo Catálogo) |

![Lista de órdenes](img/fc1-lista.png)

## 1. La lista

- **Filtros** (en este orden): *Fecha* (desde / hasta, el día «hasta» se incluye), *N.º de orden*, *Consignatario*, *Cliente*, *Servicio*, *Tipo de paquete*, *Pueblo* (cada uno acepta **varios** valores,
  con buscador y «Todos / Ninguno»), *ZIP* (prefijo: `009` trae `00901`, `00961`…), *Factura*, *Empaque* y *Estatus* (varios). Todos se combinan; **Limpiar** los quita. Los filtros de texto esperan un instante
  después de la última tecla. El buscador de la tabla busca en número, factura, empaque y consignatario.
- **Tabla**: Empaque, N.º orden, Fecha, Consignatario, Pueblo, ZIP, Servicio, Paquete (con las descripciones debajo), Piezas, COD, Estatus; con `orders.view_price` también **Cotizado**. Cada encabezado
  ordena, y el orden vale para **todo lo que dice el filtro**, no solo para la página en pantalla.
- **Una fila por tipo de paquete.** Una orden con una caja y un sobre ocupa dos filas: la primera lleva todos los datos de la orden y la segunda solo «↳», el otro tipo de paquete y sus piezas. Es **una** orden:
  el contador y las páginas cuentan órdenes, y el clic en cualquiera de sus filas la abre.
- **Papelera** (solo en la primera fila): quita la orden. Solo se ofrece mientras la orden está en su **estatus inicial** (Entrada) y con el permiso `orders.cancel`; pide confirmación. Si el servidor no lo
  permite (p. ej. «Solo se puede eliminar una orden en su estatus inicial; use la cancelación.») lo dice en el mismo diálogo. Eliminar libera el número de orden.
- **Flecha** (primera fila): abre la orden. En celular (menos de 720 px) cada orden es una tarjeta con todos sus tipos de paquete.
- **Exportar** (pie): Excel, CSV o PDF con **todas** las filas del filtro; además de las columnas de la tabla lleva Cliente y Factura.

## 2. Nueva orden (formulario detallado)

El botón **Nueva orden** (arriba a la derecha) despliega el formulario sobre los filtros; **Ocultar formulario** lo cierra. A la izquierda los pasos 01-02-03; a la derecha el **Ticket de la orden**.

![Formulario de alta](img/fc1-alta.png)

### 01 Cliente y servicio

- **Cliente** (obligatorio): buscador de clientes. Al cambiarlo se limpian el consignatario, el servicio especial y la factura.
- **Servicio** (obligatorio): arranca con el predeterminado de la compañía, si lo hay.
- **Lista** y **Límite**: las fechas en que la orden está lista y su fecha límite (hoy por omisión). El límite no puede ser anterior a la fecha lista.
- **N.º de factura**: queda **deshabilitado con «(automático)»** cuando Teikem asigna la numeración del cliente. Solo se puede teclear si el cliente asigna sus propias facturas.

### 02 Consignatario

- **Nombre**: buscador sobre los consignatarios **del cliente y los compartidos**; cada opción muestra «dirección · pueblo» y marca los que **admiten factura repetida**. Hay que elegir uno del directorio.
- Al elegirlo se llenan, solo para leer, el número, la dirección, el pueblo, el ZIP y el teléfono. Debajo se avisa si ese consignatario admite o no repetir el número de factura.
- **+** (con `locations.create`): abre el mismo formulario de «Nuevo consignatario» del Catálogo (con teléfono; nace con el cliente de la orden como dueño). Al guardarlo queda **elegido** en la orden.
  Crear el consignatario **no** guarda la orden.

### 03 Paquetes y COD

- Una o más líneas con **Tipo de paquete** (obligatorio; arranca con el predeterminado), **Descripción** (hasta 200 caracteres) y **Piezas** (entero, mínimo 1). **Añadir paquete** agrega una línea; el bote quita la
  línea (siempre queda al menos una). Los tipos distintos salen como filas distintas en la lista.
- **Monto COD**: lo que se cobra al entregar (no puede ser negativo). *El tipo de cobro (efectivo, cheque…) se registra al momento de entregar, no al capturar la orden.*

### Ticket de la orden

Resume lo capturado: cliente, servicio, consignatario, pueblo, paquetes (`Caja ×2 + Sobre ×1`) y COD. **No muestra precio**: el precio se ve después, en la lista y la ficha, y solo con `orders.view_price`.

### Guardar

| Botón | Qué hace |
|---|---|
| **Guardar borrador** | Crea la orden en **Entrada** (estatus inicial, editable). No cotiza ni revisa crédito. |
| **Guardar y confirmar** | Crea la orden, la **cotiza** con la tarifa del cliente, revisa su **crédito** y la confirma, todo en una operación: si algo falla, la orden no llega a existir. |

Al guardar bien: el formulario se cierra y se limpia, sale el aviso «Orden ORD-… guardada.» y, sobre los filtros, «Orden … guardada como borrador / y confirmada» con el botón **Abrir la orden**.
Si el servidor responde con un error, se muestra su texto exacto, el formulario **sigue abierto con lo capturado** y se puede corregir y reintentar.

### Entrega especial

![Entrega especial](img/fc1-alta-especial.png)

El interruptor **Es una entrega especial** cambia la sección 03: en vez de paquetes y COD se pide **N.º de orden** (habilitado solo si el cliente asigna sus números; si no, «(automático)») y el **Servicio
especial** del catálogo del cliente (`nombre · tarifa`). Si el cliente no tiene, dice *«Este cliente no tiene servicios especiales configurados»*. La orden se crea sin paquetes ni COD y **sin chofer**: el
chofer se asigna después. Sin el permiso `contracts.read` el interruptor está deshabilitado y lo explica.

## 3. Avisos que detienen el guardado

| Aviso | Qué ocurre | Qué hacer |
|---|---|---|
| Campos obligatorios (cliente, servicio, consignatario, tipo de paquete, piezas…) | Se marcan bajo el campo; no se llama al servidor | Completar y guardar de nuevo |
| **Factura repetida para este consignatario** (409, solo si se tecleó la factura y el consignatario admite repetidas) | Ventana con «No crear la orden» y «Crear de todos modos» (más un enlace a la orden que ya usa ese número) | Elegir; «Crear de todos modos» reenvía confirmando la repetición |
| Factura repetida **no permitida** (409) | El mensaje del servidor bajo el N.º de factura; no se crea | Cambiar el número o elegir otro consignatario |
| **El cliente excede su límite de crédito** (422, solo en «Guardar y confirmar») | Con `orders.credit_override`: ventana con el mensaje del servidor y **Autorizar y confirmar** (queda registrado quién autorizó). Sin el permiso: solo el mensaje y la orden **no** se crea | Autorizar, o pedirlo a quien tenga el permiso, o guardar como borrador |
| Sin tarifa vigente / cliente suspendido o dado de baja | El texto del servidor arriba del formulario | Configurar la tarifa en el contrato del cliente, o corregir el cliente; el formulario conserva lo capturado |

![Eliminar una orden](img/fc1-eliminar.png)

Las preguntas frecuentes de estos mensajes están en el [FAQ](../faq.md) (pendiente de integrar desde `docs/frontend/_faq-fc1.md`).
