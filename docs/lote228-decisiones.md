# Lote 228 — Pedido de vendedores, P8: datos, cierre y reporte (2026-11-07)

Parte P8 de [pedido-vendedores-plan-desarrollo.md](pedido-vendedores-plan-desarrollo.md) (§3 fila P8, §4.2, §5).

## Datos de Advance Solutions (scripts para producción)

### Qué se construyó
Scripts SQL **de datos** en [`scripts/solutions-pedido-vendedores/`](../scripts/solutions-pedido-vendedores/LEEME.md) (no tocan `Diseño/*.sql`, ni
`src/`, ni `tests/`). Los corre Luis; la sesión en la nube solo los probó contra una base local. Cada uno: encabezado (qué hace y cómo correrlo),
busca la compañía por nombre (`@Compania = N'Advance Solutions'`) y aborta con mensaje claro si no existe, **simula por omisión** (`@Aplicar = 0`:
todo en una transacción que se deshace) y termina con `Filas cambiadas: N`. Idempotentes; solo agregan o completan.

| Script | Qué hace |
|---|---|
| `00-verificar.sql` | Solo lee: compañía, estructura y permisos de los lotes 221–225, plantillas de rol, roles, módulos (SALES_ORDERS, TAX, BILLING), conteos. |
| `01-marcas.sql` | Marca de los productos del formulario (PRODIGY, PADIMONT, CARTONES, GLOBAL, AMERICAN RED CROSS, MEDPRIDE) y uniforma la escritura; informa SKU que no existen. |
| `02-kit-oferta1.sql` | Kit OFERTA1 = 6 × 52800 + 1 × 070120, $41.94, «Es promoción». La foto es paso manual (escrito en el script y el LEEME). |
| `03-roles.sql` | `SalesSupervisor` («Supervisor de ventas») y `SalesOffice` («Oficina de ventas») en Solutions con sus permisos; asegura los nuevos a `Sales` y `TenantAdmin`. |
| `04-usuarios-vendedores.sql` | Asigna el rol a los 7 vendedores, la oficina y el supervisor que ya tengan cuenta; informa quién falta. No crea cuentas. |
| `05-forma-de-pago-clientes.sql` | Informa la forma de pago (término) de los clientes; aplica correcciones si se llenan. |
| `06-lista-precios-inicial.sql` | Lista general publicada con los precios del formulario + OFERTA1 (sin guantes); aborta si se cruza con otra general publicada. |
| `07-lista-negociada.sql` | Lista de clientes escogidos «Precios negociados: guantes y cajas», en borrador. |
| `08-ajustes-envio-quickbooks.sql` | Envío $12 / gratis desde $120; mapa de términos de QuickBooks; cuentas e ítems opcionales. |
| `09-reglas-de-aviso.sql` | Reglas: QuickBooks pendiente → Oficina de ventas (campana + correo diario); cliente nuevo → Supervisor de ventas (campana); urgente → Operador de almacén (campana). |
| `10-quickbooks-ya-mandados.sql` | `QuickBooksSentAt` en los productos (no kits) y en los clientes con «Código QuickBooks»; prospectos nunca. |

### Cómo se probó
- Base local **TeikemL228** (SQL Server 2022 de `scripts/dev-sqlserver.sh`) con `db-init` del commit `86137dd` (copia con `git archive` en el
  directorio temporal, para no compilar el árbol a medias de P7), y una compañía de prueba **«Advance Solutions»** creada con `import-legacy` desde CSV
  sintéticos con los SKU, marcas (con sus variantes de mayúsculas) y términos reales de QuickBooks Solutions (35 productos, 6 clientes; las cajas
  excluidas como en la migración real). Luego se dejó «como producción»: sin los roles nuevos, sin `clients.create_basic` en Ventas, un rol «Oficina de
  ventas» hecho a mano, tres usuarios (uno con rol, uno sin rol, uno sin membresía), un prospecto y un producto inactivo.
- Base recreada desde cero y cada script corrido **dos veces** con `@Aplicar = 1` (resultado literal):
  01: 12 → 0 · 02: 3 → 0 · 03: 20 → 0 · 04: 0 → 0 (correos sin llenar) · 05: 0 → 0 (sin cambios llenados) · 06: 33 → 0 · 07: 4 → 0 · 08: 2 → 0 ·
  09: 18 → 0 · 10: 40 → 0. Variantes: 04 con 4 correos llenos 1 → 0; 05 con `@Cambios` y `@TerminoSinTermino = 'COD'` 2 → 0; 07 con clientes y
  `@Publicar = 1` 4 → 0; 08 con un mapa de términos ya puesto por el administrador y `@EnvioLlevaIvu = 1` 3 → 0 (respeta lo del admin); 10 con
  `@IncluirClientesSinCodigo = 1` 1 → 0. Abortos probados: compañía inexistente (`Msg 50001 … no existe la compañía activa «Advance Solucions»`),
  término inexistente (`Msg 50002`), cruce de dos listas generales publicadas (`Msg 50003`); ninguno dejó cambios.
- **El API lee bien lo sembrado** (API local sin llave de Brevo contra TeikemL228): `GET /sales-orders/catalog` muestra las pestañas AMERICAN RED
  CROSS (9), GLOBAL (7), MEDPRIDE (9), PADIMONT (5), PRODIGY (4) con OFERTA1 como oferta a 41.94 (`GENERAL_LIST`); `GET /products/{OFERTA1}/kit` trae
  los dos componentes; `GET /sales-orders/settings` = 12.00 / 120.00; `GET /sales-orders/quickbooks/settings` trae los nombres de los términos;
  `GET /price-lists`, `GET /roles` y `GET /notifications/rules` muestran lo creado.
- La base TeikemL228 se borró al terminar.

### Decisiones tomadas (para que Luis las ratifique)
1. **Scripts fuera de `logistica-db-update.sql`**: son datos de una sola compañía y llevan cosas por llenar; `db-update` corre en todas las bases.
2. **Simulan por omisión** (`@Aplicar = 0`), para que la primera corrida en producción sea siempre un ensayo.
3. **Marcas**: se usa la escritura de QuickBooks en mayúsculas; las cajas como **CARTONES** (el formulario las llama así; en QuickBooks venían como
   DEPOT). **Yardley y A. Belle van en GLOBAL** (el formulario los vendía dentro de «Global Personal Care»). Solo se tocan productos activos.
4. **OFERTA1**: nombre «OFERTA1: 6 cajas de tirillas Prodigy No Coding + 1 monitor Prodigy», vigencia abierta, categoría y categoría de impuesto de las
   tirillas, no ajustable. Si ya existe, el script no le cambia nombre ni precio (los pudo cambiar el supervisor).
5. **Roles**: se crean con el nombre interno de la plantilla (`SalesSupervisor`, `SalesOffice`) para que reciban solos los permisos de futuras
   versiones. A Ventas y al Admin no se les copia la plantilla entera (no se devuelve un permiso quitado a propósito).
6. **Usuarios**: el script no crea cuentas ni contraseñas; se crean en «Nuevo usuario» (no manda correo; da una contraseña temporal). **Faltan los
   correos de los 7 vendedores y el nombre y correo del supervisor de ventas**: el análisis no los trae (tabla `@Personas` por llenar). La oficina es
   `almacen@advancesolutionspr.com` (estaba en el formulario).
7. **Forma de pago**: ya está en el cliente (su término, migrado de QuickBooks); el script solo informa y corrige lo que se le diga. Los clientes sin
   término se quedan así por omisión (el vendedor escoge en cada pedido).
8. **Lista general publicada desde hoy**, sin fecha final, con los precios del formulario; los **guantes no van** (solo precio negociado). El underpad
   `PD-23-36-45gms3/50` y las cajas entran solo si existen en Teikem con ese SKU (en la migración las cajas quedaron en Depot).
9. **Lista negociada en borrador**, con el precio de venta actual como punto de partida; sin clientes (no se sabe cuáles): la termina el supervisor.
10. **Envío**: el script solo pone $12 / $120 si la compañía no tiene regla; «lleva IVU» no se toca (pregunta para el contador).
11. **QuickBooks**: nombres de términos tomados de `Solutions Customers.csv` (CHEQUE, CASH, ACH, Net 15/20/30/45, Consignment, PK BY REP); COD y NET60
    sin nombre. Cuentas e ítems en NULL (= valores por omisión) hasta que Luis ponga los reales.
12. **Avisos**: correo solo en el de QuickBooks (uno al día); el de cliente nuevo y el de urgente, solo campana. El urgente va al rol Operador de almacén.
13. **«Ya en QuickBooks»**: todos los productos no kit (activos e inactivos) salvo `@ExcluirProductos`; clientes solo con «Código QuickBooks» salvo que
    se pida lo contrario; prospectos nunca.
14. La lista de ítems a renombrar en QuickBooks sale del **reporte de la migración** (advertencias «SKU '…' normalizado a '…'»); la tabla de 7 del
    lote 225 queda como guía. Ojo: según `migracion-depot-solutions-plan.md`, en `Solutions Items.CSV` el ítem ya se llamaba `24-081028`; el `24081028` era
    del formulario, así que puede que en QuickBooks solo haya que renombrar los que tenían espacio (`NECH 1001`…`NECH 1004`, `Boost Immune`).

### Pendiente
- Llenar los correos de los vendedores y el supervisor; confirmar el SKU del underpad 3/50; decidir el IVU del envío y las cuentas reales de QuickBooks.
- La foto de OFERTA1 (paso manual).
- Apagar Formsite (pasos en el LEEME).
