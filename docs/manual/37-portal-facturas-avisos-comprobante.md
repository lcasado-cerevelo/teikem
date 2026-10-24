# 37 — Portal del cliente: facturas, mis avisos, comprobante de entrega y consignatarios
<!-- kemi:portal -->

> Lote 119 (P10, 2026-10-18). Es lo que la persona del cliente ve en su app (portal). Módulo **CLIENT_PORTAL** (y **BILLING** para las facturas). Todo es **del cliente de la sesión**: una cosa de otro cliente responde 404 igual que una que no existe.

## 1. El comprobante de entrega en la ficha del envío
Cuando un envío tiene su entrega registrada (ver [capítulo 36](36-registrar-entrega-web.md)), la ficha del envío en el portal muestra la tarjeta **Comprobante de entrega**:

- **Recibió** (nombre) y **Entregada** (fecha y hora).
- «**La entrega la registró el personal de la compañía; el consignatario no firmó.**» (si la firma fuera del que recibió diría «Firmada por quien recibió.»).
- **Ver en el mapa**, solo si se encontró la ubicación.

**Lo que el cliente NO ve:** el nombre del usuario que registró la entrega, las notas internas y el estado de la búsqueda de ubicación. `GET /api/v1/portal/orders/{publicId}` trae `deliveryProof` (`deliveredAtUtc`, `recipientName`, `registeredByStaff`, `signedByRecipient`, `latitude`, `longitude`) o nada.

## 2. Mis facturas (solo lectura)
Inicio → **Mis facturas**. Lista con número, estatus, total, saldo y fechas; filtro **Todas / Emitidas / Vencidas / Pagadas**; **Cargar más**. Al tocar una factura se ve el **Detalle** (líneas con el envío de cada una), el subtotal, el impuesto, el total, lo pagado, lo acreditado, el **saldo** y los **pagos recibidos**.

- **Solo consulta:** no hay ningún botón de pagar ni de marcar como pagada; los pagos los registra la compañía.
- **No se ven** los borradores (una corrida sin aprobar), las notas internas, el detalle de un ajuste de la oficina ni los pagos anulados.
- Si la compañía no tiene el módulo BILLING encendido, la lista responde 403 «El módulo 'BILLING' no está habilitado para esta compañía.».

| Método y ruta | Qué hace |
|---|---|
| `GET /api/v1/portal/invoices?status=&skip=&take=` | La lista (`status`: ISSUED, OVERDUE, PAID o VOID; vacío = todas menos borradores). |
| `GET /api/v1/portal/invoices/{publicId}` | Una factura con líneas y pagos; 404 si es de otro cliente, es un borrador o no existe. |

## 3. Mis avisos (preferencias de la persona)
Ajustes → **Configurar mis avisos**. La **compañía decide** qué avisos recibe el cliente y por qué medio (Sistema/Clientes → Avisos al cliente); aquí cada **persona** del portal solo puede **apagar** lo que no quiera recibir:

- Cada aviso (Paquetes recogidos, Salida a entrega, Entregas, Cobros contra entrega, Factura emitida) tiene dos interruptores: **Recibir por correo** y **Verlo en la bandeja de avisos**.
- Si la compañía **no** encendió un aviso (o un medio) para el cliente, se ve «La compañía no tiene este aviso encendido para su empresa.» / «La compañía no lo tiene encendido.» y **no se puede encender**.
- Por omisión la persona recibe todo lo que la compañía encendió. Se guarda al tocar el interruptor.
- **Efecto:** el **correo** del aviso no se manda a quien lo apagó (los demás usuarios del cliente y los correos extra de la compañía siguen igual); la **bandeja** de avisos no le muestra ese aviso a esa persona.
- El SMS no es por persona (va a los teléfonos que la compañía anote).
- Si el servidor no tiene correo configurado, la pantalla lo avisa: las preferencias se guardan pero no saldrán correos.

| Método y ruta | Qué hace |
|---|---|
| `GET /api/v1/portal/notice-prefs` | Por aviso: `emailAvailable`/`appAvailable` (el techo de la compañía) y `emailEnabled`/`appEnabled` (lo que la persona quiere). |
| `PUT /api/v1/portal/notice-prefs` | Guarda lo que la persona quiere (`prefs`: `kind`, `emailEnabled`, `appEnabled`). 400 «El aviso '{x}' no existe.» si el aviso no existe. |

## 4. Mis consignatarios
Ajustes → **Ver mis consignatarios**: la lista de los consignatarios del cliente (nombre, pueblo, correo y teléfono, con buscador) desde donde se marca a quién se le avisa por correo y se le da acceso a su espacio (capítulos 34 y 35).

## Casos frecuentes
- **No veo «Mis facturas».** Es un botón del Inicio; si la compañía no tiene el módulo de facturación, la lista dice que no está habilitado.
- **Una factura no aparece.** Solo salen las ya emitidas (no borradores) y solo las de su cliente.
- **Apagué un aviso y sigo recibiéndolo.** Los correos que la compañía anotó como «correos extra» no son de una persona: siguen llegando. También el aviso se manda al resto de usuarios del cliente.

## 5. Mi perfil (Lote 120)
Ajustes → **Mi perfil y mi contraseña**.

- **Mis datos:** mi **nombre** (lo único que cambio yo; se guarda también en mi cuenta, así que es el mismo en todos los clientes a los que pertenezco), mi **correo** (con el que entro; no se cambia aquí) y mi **rol** (lo da el administrador de mi empresa o de la compañía).
- **Mi empresa (solo lectura):** nombre, código, razón social, identificación fiscal y dirección corporativa. Los cambia la compañía; si algo está mal, se le escribe desde Mensajes.
- **Cambiar mi contraseña:** contraseña actual, nueva y repetida. Al cambiarla se cierran las demás sesiones. Los mensajes del servidor salen en el idioma de la persona: «La contraseña actual es incorrecta.», «La contraseña debe tener al menos 12 caracteres.»…

| Método y ruta | Qué hace |
|---|---|
| `GET /api/v1/portal/profile` | Mi nombre, correo, rol y los datos de mi empresa. |
| `PUT /api/v1/portal/profile` | Cambia mi nombre. 400 «Escriba su nombre.» si va vacío; 400 «El nombre no puede pasar de 150 caracteres.» |
| `PUT /api/v1/auth/password` | Cambia mi contraseña (el mismo de siempre). |

## 6. Indicadores del cliente (Lote 120)
En el **Inicio**, de **hoy** (Lote 213: desde la medianoche de hoy en la zona de la compañía) o de los últimos **7, 30 o 90 días**:

- **Envíos creados** y **Entregadas** (las que pasaron a Entregada en el período).
- **Entregas a tiempo:** de las entregadas que tenían fecha prometida, cuántas se entregaron hasta ese día (día de la compañía), con su porcentaje; si ninguna tenía fecha, no se muestra.
- **Tiempo promedio de entrega** (horas desde la creación hasta la entrega).
- **Intentos fallidos** del período.
- **En curso ahora** (todo lo que no está en un estatus final) y cuántas van en tránsito.
- **Saldo de facturas** (y cuántas vencidas), solo si la compañía tiene el módulo BILLING; **Cobrado por remesar**, solo si tiene COD y hay dinero cobrado por sus entregas que todavía no se le remite.

`GET /api/v1/portal/indicators?days=30` (0 = hoy, 7, 30 o 90; 400 «Los días deben ser 0 (hoy), 7, 30 o 90.» con otro número). Son solo del cliente de la sesión: nada de otros clientes ni de choferes.

## 7. Factura procesada (Lote 126)

La ficha del envío en el portal muestra **Factura procesada: fecha** cuando la compañía ya procesó la factura del cliente contra ese envío (hoja de COD cuadrada o cotejo desde Procesar entregas). Si no aparece la fila, todavía no se ha procesado. Solo lectura; sin el nombre de quien la procesó. Campo `invoiceProcessedAtUtc` de `GET /api/v1/portal/orders/{publicId}`.
