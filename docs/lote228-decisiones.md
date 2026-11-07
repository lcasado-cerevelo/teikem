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

## Recorrido y capturas (§4.2 punto 4)

### Qué se corrió
- **Base local nueva `TeikemE2E`** con `db-init` (Release ya compilado) y el API en `http://localhost:5000` con `ASPNETCORE_ENVIRONMENT=Development`,
  **`Brevo__ApiKey=` vacía**, `Auth__Onboarding__Enabled=false`, `Portal__ReturnInviteTokenInResponse=true`, `Auth__TrustedBrowsers__ReturnLinkInResponse=true` y
  `Auth__PasswordReset__ReturnLinkInResponse=true` (lo de `scripts/api-para-pruebas.ps1`, a mano en Linux); la web con `vite` en `:5173`
  (`VITE_API_URL=http://localhost:5000`). `e2e/globalSetup.ts` corrió y no encontró correo configurado. Ningún correo salió.
- **Recorrido** `web-app/e2e/lote228-pedido-vendedores.spec.ts` (`--trace off`, un worker). Siembra todo por API en un `beforeAll`: módulos `SALES_ORDERS`,
  `TAX` y `BILLING` (los enciende el **administrador de la plataforma**, `teikem+support@…`: el administrador de la compañía recibe 403 «Solo el administrador de la
  plataforma puede encender o apagar módulos.»), IVU 11.5 % por omisión, envío $12 / gratis desde $120 (sin IVU), mapa de términos de QuickBooks, productos con
  marca PRODIGY y GLOBAL, el kit **OFERTA1** (6 × 52800 + 1 × 070120, $41.94, promoción vigente con foto), la lista general publicada, una lista negociada
  (la crema a $2.99) para un cliente con forma de pago Consignación, existencia, y dos usuarios: **vendedor** (rol Ventas) y **supervisor** (rol Supervisor de ventas).
  1. *Precios y ofertas*: tarjeta de la oferta con foto, la ventana de la lista negociada, el editor del kit en promoción.
  2. *Pedido guiado completo, todo por la pantalla*: el vendedor da de alta el cliente nuevo (con las dos fotos) → oferta (+) y 4 cremas de otra marca → entrega a la
     dirección física, resumen **$54.94 + IVU $6.32 + envío $12.00 = $73.26** → nombre y firma dibujada → «Enviar pedido» con contraseña mala (marca el error) y luego
     la buena → «Pedido enviado», «espera la aprobación del cliente»; por API se confirma `awaitingClientApproval=true` y que **despachar se rechaza**. El supervisor
     (otra sesión) aprueba al cliente en su ficha (límite $1,000, 30 días) y la orden deja de esperar. La oficina (administrador) **despacha desde la ventana de la
     orden**, **factura por la pantalla** (Nueva corrida → Marcar como revisada → Aprobar y emitir con reautenticación) y en **QuickBooks**: «ver datos», Exportar
     (baja el `.iif`), **abre el archivo y lo compara renglón por renglón** con lo esperado (armado con lo tecleado y los montos de la factura: los 3 INVITEM, el CUST
     completo y el bloque TRNS/SPL/ENDTRNS exacto, con el kit desglosado a prorrata que cierra al centavo), «Sí, ya están», histórico → «Quitar la marca» → vuelve con
     «Salió el…» y el aviso → «Marcar como en QuickBooks» por fila. Ajustes → Operación (Ventas y QuickBooks).
  3. *Consignación*: el vendedor arma el pedido (precio negociado $2.99 gana a la lista; Recogido, «En consignación», sin envío). **Despacho y factura encadenados por
     API** (la misma secuencia ya se probó por la pantalla en el punto 2). En Facturación → Consignación: pago parcial $10.00 → saldo baja; pago del resto →
     `balance = 0`, `paid = total` ($33.34).
  4. *360 px* (proyecto `movil-impuestos`): los cuatro pasos y el envío, sin desplazamiento horizontal de la página.
- El recorrido entra en los proyectos `escritorio-impuestos` / `movil-impuestos` de `playwright.config.ts` (enciende impuestos, como los lotes 56 y 60).
- **Capturas**: 42 en `docs/manual/frontend/img/l228-*.png` y 42 en `docs/manual/en/frontend/img/` (`MANUAL_LANG=en`), `deviceScaleFactor: 2`, claro y oscuro
  (`-oscuro`) en las pantallas principales; cada idioma sobre una base recién creada (datos limpios). Referenciadas en los capítulos **47, 48 y 49** (es y en), y
  también en 02 §1.8, 18 y 19 §4e, que tenían su nota «Capturas pendientes (P8)»; quedan pendientes (dicho en cada nota) las de la **app móvil**, el diálogo de
  faltante, los distintivos de la lista de órdenes, los paneles «Ventas»/«Documentos del cliente», la ventana de rechazar, la vista «Consignado por cliente» y el
  detalle de consignación.
- Al terminar: API y web apagados, base `TeikemE2E` borrada.

### Resultado literal (corrida final, base limpia)
```
es: ✓ 1 [escritorio-impuestos] Precios y ofertas … (6.5s)
    ✓ 2 [escritorio-impuestos] Pedido guiado: cliente nuevo → … → QuickBooks (50.0s)
    ✓ 3 [escritorio-impuestos] Consignación: pedido en consignación, pago parcial y total, saldo en cero (19.7s)
    ✓ 8 [movil-impuestos] Pedido guiado a 360 px … (6.4s)
    4 skipped · 4 passed (1.6m)          (los 4 «skipped» son cada prueba en el proyecto que no le toca)
en: los mismos 4 passed, 4 skipped (2.0m)
```
Bloque de la factura en el IIF (tabuladores como `→`):
```
TRNS→→INVOICE→11/07/2026→Accounts Receivable→Farmacia Nueva DKAHADG→73.26→FAC-00001→OV-00001→N→N→Y→12/07/2026→Net 30
SPL→→INVOICE→11/07/2026→Sales→→-33.41→FAC-00001→Tirillas Prodigy No Coding (50) (OFERTA1-DKAHADG)→N→-6→5.56833→52800-DKAHADG→Y→
SPL→→INVOICE→11/07/2026→Sales→→-8.53→FAC-00001→Monitor Prodigy AutoCode (OFERTA1-DKAHADG)→N→-1→8.53→070120-DKAHADG→Y→
SPL→→INVOICE→11/07/2026→Sales→→-13.00→FAC-00001→Crema Global 4 oz→N→-4→3.25→GL-100-DKAHADG→Y→
SPL→→INVOICE→11/07/2026→Sales→→-12.00→FAC-00001→OV-00001 — Cargo de envío→N→-1→12.00→Shipping→N→
SPL→→INVOICE→11/07/2026→Sales Tax Payable→→-6.32→FAC-00001→IVU→N→→→IVU→N→AUTOSTAX
ENDTRNS
```

### Errores que destapó y se corrigieron (solo frontend, cambios mínimos)
1. **Pedido guiado, paso 2: el buscador de la marca medía ~200 px de alto** (más a 360 px): `.qbox` trae `flex: 1 1 200px` y en la columna del paso esa base se
   volvía alto. `wizard.css`: `.wz-step > .qbox { flex: none; }`. Lo vigila el recorrido a 360 px (alto < 60 px).
2. **Tema oscuro: casillas sueltas blancas** (fuera de `.f` no reciben los colores del tema): cantidades del pedido guiado (`.wz-qty`), cantidades de la ventana de la
   orden (`.sales-qty`), precio de un renglón de lista (`.pl-price`) y el buscador de cliente en la fila de filtros de Consignación (`.tax-filters .cpick input`).
   Ahora usan `--bg-2`/`--panel`, `--line` y `--text`. El recorrido comprueba que `.wz-qty` no sea blanca en oscuro.
3. **Editor del kit: el botón «×» de cada componente caía a un renglón propio de todo el ancho** (`.r2` es de dos columnas): `.r2.kit-comp-row` de tres columnas.
4. **La firma en la ventana de la orden se salía de su celda**: `.sales-sign img { max-width: min(240px, 100%) }`.
5. **«Precios y ofertas»: «Mostrar desactivadas» descuadrado** (casilla arriba, texto abajo): usa el `ToggleFilter` de los filtros del almacén.
6. **«1 clientes»** en la lista de listas y en la ventana: el texto pasa a «Clientes escogidos: {count}» / «Selected clients: {count}» (el i18n no tiene plurales).

Compuerta tras los cambios: `tsc -b` limpio, `npm run lint` sin errores, `vitest` de warehouse/sales/billing/tax/i18n **100 archivos, 896 pruebas, todas pasan**,
`npm run build` ✓. La corrida completa de `npm run check` dio **3715 pasan, 1 falla**: `DispatchMap.test.tsx › plano de la ruta activa › va dentro de «Ruta
activa»…` («Unable to find … /Plano esquemático/»); falla igual corrida sola, el archivo no se ha tocado desde el lote 216 y no tiene que ver con este lote:
**no se arregló, se reporta**.

### Hallazgos que no se arreglaron (para decidir)
- **Tema oscuro sin `color-scheme: dark`**: las casillas de verificación nativas (p. ej. «Elegir» en la pestaña QuickBooks) y los selectores de fecha del navegador
  salen claros en oscuro. Se arregla con una línea en `src/styles/tokens.css`, pero cambia el aspecto de toda la aplicación: se deja a decisión.
- **`e2e/staffHelpers.ts › enableModule`** usa al administrador de la compañía y el API responde 403 (solo la plataforma enciende módulos): los recorridos que lo
  llaman (p. ej. `lote60-ordenes-de-venta`) fallarían en una base nueva. Este recorrido usa su propio ayudante con la plataforma; no se cambió el compartido.
- La ficha del cliente muestra «Teléfonos y correos: Sin teléfonos ni correos» para el cliente dado de alta en el pedido: el alta mínima guarda teléfono y correo
  en la **persona de contacto** (por diseño, lote 222); el IIF sí los lleva (`7875551234 x12`). Puede confundir a la oficina.
- El supervisor de ventas, además de **Aprobar**, ve en la ficha el avance de estatus «Avanzar a Activo» (el de siempre), que aprueba sin pedir límite ni término.
