# 37 — Portal del cliente: facturas, mis avisos, comprobante de entrega y consignatarios

> Lote 119 (P10, 2026-10-18). Es lo que la persona del cliente ve en su app (portal). Módulo **CLIENT_PORTAL** (y **BILLING** para las facturas). Todo es **del cliente de la sesión**: una cosa de otro cliente responde 404 igual que una que no existe.

## 1. El comprobante de entrega en la ficha de la orden
Cuando una orden tiene su entrega registrada (ver [capítulo 36](36-registrar-entrega-web.md)), la ficha de la orden en el portal muestra la tarjeta **Comprobante de entrega**:

- **Recibió** (nombre) y **Entregada** (fecha y hora).
- «**La entrega la registró el personal de la compañía; el consignatario no firmó.**» (si la firma fuera del que recibió diría «Firmada por quien recibió.»).
- **Ver en el mapa**, solo si se encontró la ubicación.

**Lo que el cliente NO ve:** el nombre del usuario que registró la entrega, las notas internas y el estado de la búsqueda de ubicación. `GET /api/v1/portal/orders/{publicId}` trae `deliveryProof` (`deliveredAtUtc`, `recipientName`, `registeredByStaff`, `signedByRecipient`, `latitude`, `longitude`) o nada.

## 2. Mis facturas (solo lectura)
Inicio → **Mis facturas**. Lista con número, estatus, total, saldo y fechas; filtro **Todas / Emitidas / Vencidas / Pagadas**; **Cargar más**. Al tocar una factura se ve el **Detalle** (líneas con la orden de cada una), el subtotal, el impuesto, el total, lo pagado, lo acreditado, el **saldo** y los **pagos recibidos**.

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
