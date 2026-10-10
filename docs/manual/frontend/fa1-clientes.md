# F-A1 — Clientes: lista, alta y ficha del cliente

Pantalla **Catálogo → Clientes** (`/catalog/clients`). En una sola pantalla: a la izquierda la lista de clientes y a la derecha la ficha
(expediente) del cliente elegido. Backend: capítulo 02 del manual (Clientes y contratos). Decisiones del frontend:
`docs/frontend/loteFA1-decisiones.md`.

> Capturas pendientes: este capítulo se escribió sin capturas de pantalla; se agregarán en el cierre del bloque de contratos.

**Quién puede.** Ver la pantalla: permiso **`clients.read`** con el módulo **Catálogo** (`CATALOG`) encendido (sin el permiso el menú no la muestra y la
dirección lleva a «Sin permiso»; con el módulo apagado, a «Módulo apagado»). Cada acción pide su permiso y, sin él, el botón no se pinta y los campos
quedan de solo lectura:

| Acción | Permiso |
|---|---|
| Crear cliente | `clients.create` |
| Editar perfil, numeración, personas de contacto, cambiar estatus, dar de baja y reactivar, guardar campos personalizados | `clients.update` |
| Agregar, editar o quitar teléfonos y correos (del cliente o de una persona) | `contacts.manage` **y** `clients.update` |

## 1. La lista (izquierda)

- Panel **Clientes** con el contador de clientes mostrados. Cada fila muestra el nombre, el código, el estatus (chip de color del catálogo de la compañía), el chip
  **Inactivo** si el cliente está dado de baja y el resumen de facturación del contrato vigente.
- **Buscador**: escriba parte del código, el nombre o la razón social; la búsqueda la hace el servidor (espera un instante después de la última tecla).
- **Mostrar inactivos**: por omisión solo se ven los clientes activos; encienda el interruptor para ver también los dados de baja (salen atenuados).
- Tocar una fila abre su ficha a la derecha y deja el cliente en la dirección (`?client=…`): se puede copiar el enlace. Sin cliente en la dirección se abre el primero.
- La barra entre la lista y la ficha se arrastra (o se mueve con ← →) para cambiar el ancho; la proporción se recuerda. En celular (menos de 720 px) la ficha queda
  debajo de la lista.

## 2. Nuevo cliente

Botón **Nuevo cliente** (`clients.create`). Campos:

| Campo | Regla | Mensaje (HTTP) |
|---|---|---|
| Nombre | obligatorio, máx. 200 | `El nombre es obligatorio.` (400) · `No puede exceder 200 caracteres.` (400) |
| Código | opcional, máx. 30; vacío = se genera del nombre (`FARMACIA-LAS-MARIAS`; si ya existe, `-2`, `-3`…); se guarda en mayúsculas | `El código no puede exceder 30 caracteres.` (400) · `Ya existe un cliente con ese código.` (409) |
| Razón social | opcional, máx. 250 | `No puede exceder 250 caracteres.` (400) |
| Identificación fiscal | opcional, máx. 50 | `No puede exceder 50 caracteres.` (400) |
| Término de pago, Moneda | catálogos de la compañía | — |
| Límite de crédito | número, no negativo | `El límite de crédito no puede ser negativo.` (400) |
| Crear contrato inicial | encendido por omisión: abre un contrato en **borrador** | `Ya existe un contrato con ese número.` (409) |
| Cliente desde | fecha de inicio del contrato (hoy en la zona de la compañía) | `Indique desde cuándo es cliente.` (en pantalla) |
| Título del contrato | opcional; vacío = «Contrato marco»; máx. 200 | `El título no puede exceder 200 caracteres.` (400) |

No se pide el SLA: es por tipo de servicio y en horas, y se configura en el contrato (bloque siguiente). Al guardar, el cliente nace en la etapa inicial de su
estatus, la lista se actualiza y la ficha del nuevo cliente queda abierta. Los mensajes del servidor salen debajo del campo; el 409 de código duplicado sale también
debajo de **Código** y el modal sigue abierto.

## 3. La ficha (derecha), panel por panel

### Cabecera: estatus y baja
- Nombre y código del cliente, y las **etapas de su estatus** (pipeline). Los botones ofrecen solo las transiciones que el servidor aceptaría («Avanzar a …»,
  «Pasar a …»); piden un comentario opcional (máx. 500). Requiere `clients.update`. Si el servidor rechaza (422), el mensaje queda en el diálogo, p. ej.
  `Salto ilegal: de 'ACTIVE' solo se puede avanzar a 'REVIEW'.`
- **Dar de baja** (`clients.update`): confirma «{nombre} quedará inactivo: no se podrá elegir en órdenes nuevas…». Es una baja lógica; el historial se conserva.
  **Reactivar** lo devuelve a la lista de activos. Un cliente dado de baja muestra el aviso «Este cliente está dado de baja…».

### Perfil del cliente
- El **nombre** se ve pero no se cambia aquí. Se editan razón social, identificación fiscal, límite de crédito, término de pago, moneda y **punto de recogido por
  defecto** (un almacén activo del cliente de tipo recogido; «Dirección corporativa» lo quita).
- **Guardar** solo se activa si hay cambios. El límite de crédito no se puede dejar vacío una vez que tiene valor (escriba 0 si no aplica).
- Las **direcciones física y postal** son de solo lectura; si no hay postal se lee «La dirección postal es la misma que la física». Se administran en Localizaciones.
- Mensajes: `El registro fue modificado por otro usuario; recargue e intente de nuevo.` (409: otra persona guardó antes; la ficha se vuelve a leer y lo que usted
  escribió se conserva, vuelva a **Guardar**) · `El punto de recogido debe ser un almacén activo del cliente (tipo PICKUP o BOTH).` (400) ·
  `El límite de crédito no puede ser negativo.` (400) · `Cliente no encontrado.` (404).

### Teléfonos y correos
Los medios de contacto **del propio cliente**. Los teléfonos se ven con la máscara de la compañía (`(787) 555-0142`) y se guardan solo con dígitos; los correos, en
minúsculas. **Agregar teléfono / Agregar correo**, lápiz para editar y papelera para quitar (pide confirmar). **Principal**: solo uno por tipo; marcar otro quita la marca
del anterior, y el primero que se agrega de un tipo queda como principal.
Mensajes: `Teléfono inválido.` · `Correo inválido.` · `El valor es obligatorio.` (400).

### Personas de contacto
Tabla **Nombre · Puesto · Teléfono · Correo · Principal**. **Agregar contacto** y el lápiz abren un modal (nombre, puesto, teléfono, correo, principal). **Quitar** pide
confirmar y da de baja a la persona (no se borra). Solo hay **una persona principal activa**: marcar a otra quita la marca a la anterior.
Mensajes: `El nombre del contacto es obligatorio.` (400) · `No puede exceder 150 caracteres.` / `No puede exceder 80 caracteres.` (400) ·
`Ya existe un contacto principal activo para este cliente.` (409, si dos personas marcan el principal a la vez; repita).
Sin `contacts.manage` el modal guarda nombre, puesto y principal, y avisa que no se pueden cambiar teléfonos ni correos.

### Numeración
- **¿Quién asigna el número de orden / de factura?**: *El cliente* (lo escribe al capturar) o *Teikem (automático)*.
- Los tres patrones (**orden, factura, paquete**) definen cómo se ve el número que Teikem genera: `#` = un dígito del consecutivo, `@` = letra, el resto se copia tal cual
  (`AX-#####` genera `AX-00001`, `AX-00002`…). Bajo cada patrón se ve el **Ejemplo** con el consecutivo 1, calculado por el servidor mientras escribe. **Vacío = el patrón por
  defecto del sistema** (`ORD-#####`, `FAC-#####`, `PQT-#####`). El número de paquete siempre lo genera Teikem.
- Reglas (400, bajo el campo): `El patrón no puede exceder 40 caracteres.` · `El patrón debe incluir al menos un '#' para el consecutivo.` ·
  `Carácter no permitido en el patrón: 'x'. Use letras, dígitos y - _ / . # @.` (letras sin acento, dígitos y `- _ / . # @`).

### Campos personalizados
Aparece solo si el módulo de campos personalizados está encendido y la compañía definió campos para clientes. Se guardan con **Guardar** del panel (`clients.update`).

### Contratos (solo lectura)
Los contratos del cliente (número y título, estatus, vigencia, marca **Vigente**) y el resumen de facturación. Aquí no se editan: **los contratos, tarifas, cargos por COD y
servicios especiales se administran en el siguiente bloque**.

### Historial de estatus
Cada cambio de estatus del cliente: de qué a qué, quién, cuándo y el comentario.

## Casos frecuentes
- **No veo el botón Nuevo cliente / Guardar / Dar de baja**: falta el permiso (`clients.create` / `clients.update`); con solo `clients.read` la ficha es de consulta.
- **No veo Agregar teléfono**: pide `contacts.manage` y `clients.update` a la vez.
- **Di de baja un cliente y desapareció de la lista**: la lista oculta los inactivos; encienda **Mostrar inactivos** y use **Reactivar**.
- **El ejemplo de la numeración no cambia**: espere un instante tras la última tecla; si el patrón es inválido el motivo sale en rojo en lugar del ejemplo.
- **¿Dónde pongo el SLA o el cliente desde después del alta?**: son del contrato; se editan en el bloque de contratos.
- **No puedo cambiar la dirección**: las direcciones se administran en Localizaciones; en el perfil son de solo lectura.
