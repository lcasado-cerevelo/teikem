# Datos de Advance Solutions para el pedido de vendedores (lote 228, P8)

Scripts SQL para dejar a **Advance Solutions** lista para que los vendedores pidan en Teikem y se apague el formulario de Formsite.
Los corre **Luis** (o quien él diga) en el servidor; ninguna sesión de Claude los corre en TEST ni en producción.

- Son **datos**, no estructura: la estructura de las tablas ya la pone el instalador (`db-update`) con la versión de los lotes 221–226, 229 (ofertas) y 231 (biblioteca de kits de oferta).
  No van en `Diseño/logistica-db-update.sql` porque son de UNA compañía.
- Cada script busca la compañía por su nombre (`@Compania = N'Advance Solutions'`, arriba de cada archivo) y **aborta** con un mensaje
  claro si no existe. Si en Teikem se llama distinto, cambie ese valor en todos.
- Todos son **idempotentes**: correrlos dos veces no duplica nada (la segunda corrida dice `Filas cambiadas: 0`).
- Todos **simulan por omisión** (`@Aplicar = 0`): hacen todo dentro de una transacción, muestran el informe y lo deshacen. Para guardar,
  se cambia a `@Aplicar = 1` y se vuelve a correr. Al final cada uno dice `SIMULACIÓN: se habrían cambiado N filas` o `APLICADO. Filas cambiadas: N`.
- Solo agregan o completan: no borran filas, no quitan permisos, no tocan inventario ni facturas.
- Cambios hechos por script **no salen en la bitácora** de la pantalla (Seguridad y auditoría). Lo que sea uno o dos registros, mejor en la pantalla.

## Antes de empezar
1. **Respaldo** de la base de producción (como antes de cualquier `db-update`).
2. Instalar la versión con el pedido de vendedores (el instalador corre `db-update`) y **arrancar el API una vez** (crea las plantillas de
   rol «Supervisor de ventas» y «Oficina de ventas»).
3. Lo ideal: correr todo primero en **TEST** (copia de producción) y revisar el pedido guiado ahí con un vendedor de prueba. **Ojo:** el
   script 09 crea reglas de aviso con correo; en una base de prueba con la llave de Brevo puesta, ese correo sale de verdad.
4. Abrir SQL Server Management Studio, conectarse al servidor, escoger la base de Teikem (la del API) y abrir cada archivo. F5 lo corre.
   Con `sqlcmd`: `sqlcmd -S <servidor> -d <base> -E -I -b -i <archivo>.sql` (`-I` es necesario; los scripts lo ponen igual al inicio).
   Requiere SQL Server 2017 o más nuevo (`STRING_AGG`).

## Orden y qué revisar

| # | Script | Qué hace | Qué revisar / llenar antes de `@Aplicar = 1` |
|---|---|---|---|
| 00 | `00-verificar.sql` | Solo lee: compañía, estructura y permisos de los lotes 221–225, plantillas de rol, roles, **módulos**, conteos. | Que diga `OK: la estructura y los permisos…`. Módulos **SALES_ORDERS**, **TAX** y **BILLING** encendidos (si no: Sistema → Ajustes → Módulos). |
| 01 | `01-marcas.sql` | Marca de cada producto del formulario (pestañas del pedido: PRODIGY, PADIMONT, CARTONES, GLOBAL, AMERICAN RED CROSS, MEDPRIDE) y uniforma la escritura. Yardley y A. Belle → GLOBAL (así los vendía el formulario). | La tabla «SKU del formulario que NO existe»: las cajas `979`, `101010`, `121212` no están en Solutions (la migración las dejó en Depot); el underpad `PD-23-36-45gms3/50` hay que confirmar su SKU en Teikem y agregarlo como `Candidato`. «Pestaña del pedido» = cómo lo verá el vendedor. |
| 02 | `02-kit-oferta1.sql` | Crea el **kit OFERTA1** en la **biblioteca de kits de oferta** (lote 231: 6 × `52800` + 1 × `070120`, marca PRODIGY, no ajustable; no aparece en Productos e inventario) y la **oferta «OFERTA1»** (desde hoy, **sin fecha de fin**, para todos los clientes, publicada) que lo usa a **$41.94**. El kit solo se vende mientras esté en alguna oferta publicada y vigente; otra oferta (p. ej. la de enero) lo puede volver a usar con otro precio. Si el kit ya se usó y tiene otras piezas, aborta (su composición no se cambia: kit nuevo con otro SKU). | Que muestre las dos piezas y la oferta «sin fecha de fin». **Paso manual después:** la **foto** — Contabilidad → Precios y ofertas → «Ofertas» → OFERTA1 → «Poner foto». Si la oferta debe terminar, ponerle la fecha «hasta» ahí mismo. |
| 03 | `03-roles.sql` | Crea en Solutions **Supervisor de ventas** y **Oficina de ventas** (los de fábrica), asegura los permisos nuevos a **Ventas** y al **Admin**. | Si avisa de un rol «Oficina de ventas»/«Supervisor de ventas» hecho a mano: pase sus usuarios al nuevo y desactive el viejo (Sistema → Roles y usuarios). |
| — | **Pantalla** | Crear las cuentas (ver «Usuarios» abajo). | — |
| 04 | `04-usuarios-vendedores.sql` | A cada vendedor con cuenta le asegura el rol **Ventas**; a la oficina **Oficina de ventas**; al supervisor **Supervisor de ventas**. Informa quién falta. | **Llenar los correos** en `@Personas` (el análisis no los trae; solo `almacen@advancesolutionspr.com` de la oficina) y el **nombre y correo del supervisor de ventas**. |
| 05 | `05-forma-de-pago-clientes.sql` | Informa la forma de pago (término) de los clientes y aplica correcciones si las lista. | Los clientes **sin término** (en QuickBooks eran ~10): o se dejan (el vendedor escoge en cada pedido), o se les pone uno con `@TerminoSinTermino`, o uno a uno en `@Cambios`. |
| 06 | `06-lista-precios-inicial.sql` | Lista de precios **general**, vigente desde hoy y publicada, con los precios del formulario (sin guantes; **sin OFERTA1**: su precio va en su oferta, script 02). | Los precios que difieren del precio de venta de QuickBooks (informativo). Si aborta por cruce con otra lista general, termine la otra o corra con `@Publicar = 0`. |
| 07 | `07-lista-negociada.sql` | Lista **«Precios negociados: guantes y cajas»** en **borrador** (guantes NECH 1001–1004 y cajas), de clientes escogidos. | Precios negociados en `@Productos` (vacío = el precio de venta actual como punto de partida) y clientes en `@Clientes`. Lo normal: dejarla en borrador y que el **supervisor** ponga precios y clientes y la publique en «Precios y ofertas». Una lista por grupo de clientes con el mismo precio. |
| 08 | `08-ajustes-envio-quickbooks.sql` | Envío de la compañía: **$12.00**, **gratis desde $120.00** (después del IVU, solo entrega). QuickBooks: nombres de los términos (CHEQUE, CASH, ACH, Net 15/20/30/45, Consignment, PK BY REP). | `@EnvioLlevaIvu` (pregunta para el contador; NULL = no se toca, hoy no lleva). Las **cuentas y el ítem de IVU/envío** con los nombres EXACTOS de QuickBooks (o en Ajustes → Operación → QuickBooks). El ítem de envío (por omisión `Shipping`) debe existir en QuickBooks como servicio. |
| 09 | `09-reglas-de-aviso.sql` | Reglas de aviso: «Facturas por pasar a QuickBooks» → Oficina de ventas (campana + **un correo al día**, 07:00); «Cliente nuevo por aprobar» → Supervisor de ventas (campana); «Pedido urgente» → Operador de almacén (campana). | Quién debe recibir correo (`@CorreoQuickBooks`, `@CorreoClienteNuevo`). |
| 10 | `10-quickbooks-ya-mandados.sql` | Marca los productos (y clientes con «Código QuickBooks») como **ya en QuickBooks**, para que la primera exportación no los traiga todos como nuevos. | Comparar la lista de productos con la Lista de ítems de QuickBooks; lo que se creó en Teikem y no está allá va en `@ExcluirProductos`. Clientes creados en Teikem: `@IncluirClientesSinCodigo`. |

## Usuarios de los 7 vendedores (sin correos masivos)
Vendedores del formulario: Carlos Planas, Neda L. Ramos, Marilyn Rodríguez, Santa Vélez, Cindy Olivas, Indhyra Ortiz, Radames Santiago.
1. En Teikem, compañía Advance Solutions: **Sistema → Roles y usuarios → Usuarios → «Nuevo usuario»**: correo, nombre, rol **Ventas**,
   contraseña **vacía** → Guardar. Esta alta **no manda correo**: muestra **una sola vez** una contraseña temporal. Cópiela y désela al
   vendedor en persona o por teléfono. Al entrar, pone su contraseña y activa el MFA. Uno por uno; nada de invitaciones en lote.
2. No use «¿Olvidó su contraseña?» para dar acceso (eso sí manda correo).
3. Igual para la persona de la oficina (rol **Oficina de ventas**) y el supervisor (rol **Supervisor de ventas**).
4. Corra `04-usuarios-vendedores.sql` con los correos llenos: confirma que cada uno tiene su rol y dice quién falta.
5. En el teléfono: instalar la app de Teikem (personal), entrar, aceptar Face ID / huella. «Pedidos» aparece en el inicio.

## QuickBooks: renombrar los ítems (una sola vez)
El archivo IIF usa **el SKU de Teikem como nombre del ítem**; QuickBooks empareja por nombre. Los que difieren se **renombran en QuickBooks**
(Lista → Lista de artículos → editar el artículo → Nombre; QuickBooks conserva el historial). Lista de `docs/lote225-decisiones.md`:

| En QuickBooks hoy | Renombrar a (SKU de Teikem) |
|---|---|
| `NECH 1001`, `NECH 1002`, `NECH 1003`, `NECH 1004` | el SKU real en Teikem (la migración los dejó **`NECH1001`…`NECH1004`**, sin espacio) |
| `24081028` | `24-081028` |
| `24083225` | `24-083225` |
| `PD-23-36-45gms15/10` (underpad) | `PD-23-36/15/10` |
| `PD-23-36-45gms3/50` (underpad) | el SKU de Teikem (por confirmar) |
| `Boost Immune` | el SKU de Teikem (por confirmar; Teikem no admite espacios en el SKU) |

**La lista que manda es el reporte de la migración** (`F:\Download\TeikemMigracion\reporte-solutions-*.md`): cada advertencia
«SKU '…' normalizado a '…'» es un ítem cuyo nombre en QuickBooks no es igual al SKU de Teikem. Confírmelo contra Almacén → Productos.
Si en QuickBooks el ítem ya se llama como en Teikem, no hay nada que renombrar: según el plan de la migración, en `Solutions Items.CSV` ya
se llamaba `24-081028` (el `24081028` sin guion era del formulario), así que puede que solo falten los que tenían espacio (`NECH 1001`…, `Boost Immune`).

La **primera exportación**: Órdenes de venta → pestaña QuickBooks → «Exportar a QuickBooks» → **desmarcar** «Incluir los clientes y
productos nuevos» → «Exportar y bajar el archivo»; importar el `.iif` en QuickBooks (Archivo → Utilidades → Importar → Archivos IIF); si entró
bien, «Sí, ya están». Con eso confirmado, las siguientes ya pueden llevar los nuevos.

## Apagar Formsite
1. Cuando los 7 vendedores y la oficina ya entren a Teikem y hayan hecho al menos un pedido real cada uno, avisarles la fecha de corte.
2. En Formsite, formulario «AdvanceMedical»: exportar los resultados que se quieran guardar (Results → Export), y luego cerrar el
   formulario (Settings → Form Status: **Closed**) con un mensaje como «Los pedidos ahora se hacen en Teikem (app Teikem → Pedidos)».
   Quitar el correo de notificación del formulario. Cancelar la suscripción cuando ya no haga falta consultarlo.
3. Quitar el enlace de Formsite de donde lo tengan los vendedores (favoritos del teléfono) y poner el de Teikem.

## Si algo sale mal
- Cualquier script: si da error, la transacción se deshace sola (no queda a medias). Si aplicó y no era lo que se quería:
  - Marcas, término de pago, ajustes de envío y QuickBooks: corregir en la pantalla (ficha del producto / del cliente, Ajustes).
  - OFERTA1: Contabilidad → Precios y ofertas → «Ofertas» → OFERTA1 → «Volver a borrador» o «Desactivar» (el kit deja de venderse si no está en otra oferta vigente; queda en la biblioteca de kits y las órdenes ya hechas no cambian).
  - Listas de precios: «Precios y ofertas» → la lista → «Volver a borrador» o «Desactivar».
  - Roles: Sistema → Roles y usuarios (quitar permisos o desactivar el rol).
  - Reglas de aviso: Sistema → Reglas de aviso → desactivar.
  - «Ya en QuickBooks»: no hay pantalla para quitarla; si un producto debía salir como nuevo, en SQL:
    `UPDATE dbo.Product SET QuickBooksSentAt = NULL WHERE TenantId = <id> AND Sku = N'<SKU>';` (el id lo da `00-verificar.sql`).
- Los permisos nuevos de un usuario con sesión abierta se ven en hasta 5 minutos o al volver a entrar.
