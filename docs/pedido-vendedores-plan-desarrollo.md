# Pedido de vendedores (Solutions): diseño y plan de desarrollo

Fecha: 2026-11-06. Dueño: Luis. Base: el análisis y las nueve rondas de dictado en
[pedido-vendedores-formsite-analisis-2026-11-06.md](pedido-vendedores-formsite-analisis-2026-11-06.md). Este documento dice **cómo queda
todo en pantalla, dónde se pone, con qué datos, en qué partes se construye, quién construye cada parte, qué pruebas lleva cada una y cómo
se cierra**. Nada de esto está construido. Regla de forma: **ningún menú nuevo a la izquierda**; todo entra como pestañas, botones o
secciones de pantallas que ya existen. La orden de venta se queda como está: el pedido guiado es otra puerta a la misma orden.

## 0. Para la sesión que ejecuta este plan (sesión de Claude en la nube)

Este plan **lo ejecuta una sesión de Claude Code en la nube**, no la máquina de Luis. Instrucciones para esa sesión:
- **Lee primero** `CLAUDE.md` (raíz y `web-app/KIT.md`), este plan y el análisis enlazado arriba. Las reglas de `CLAUDE.md` mandan sobre
  este plan si chocan.
- **Rama: `master`** (Luis, 2026-11-06). Todo se hace y se sube en `master`; si hay conflictos se resuelven ahí.
  No crees ramas ni PR por tu cuenta.
- **Nada de correo real:** `Brevo__ApiKey` vacía en todo API que levantes; jamás Playwright, smoke ni pruebas de punta a punta contra un
  API con correo configurado, ni contra TEST ni producción.
- **No tienes la base de producción ni la de TEST, ni QuickBooks, ni la carpeta `F:\Download`**: todo se prueba contra una base local
  recreada (`db-init`, permitido en desarrollo: SQL Server con `docker compose up -d sqlserver` o `scripts/dev-sqlserver.sh`). Lo que haya que
  aplicar en producción (datos de Solutions, ajustes, usuarios) se entrega como **scripts idempotentes y pasos escritos** para que los corra
  Luis; tú no los corres en ningún servidor.
- **SQL congelado:** `Diseño/logistica-db-estructura.sql` y `logistica-db-seed.sql` no se tocan; todo va al final de
  `Diseño/logistica-db-update.sql` en secciones idempotentes con fecha. `FrozenSqlTests` lo vigila.
- **Trabaja sin preguntar** dentro de lo que este plan y el análisis deciden; lo que no esté decidido, decídelo tú con criterio razonable y
  **anótalo** en el `docs/loteN-decisiones.md` de esa parte. No interrumpas a Luis por decisiones chicas; acumúlalas para el reporte final.
- **Una corrida pesada a la vez** (build, test, check): no lances varias en paralelo. Agentes en paralelo sí, pero que no compitan por
  compilaciones a la vez; como máximo 2 piezas construyéndose al mismo tiempo.
- **Cada parte se cierra** con su compuerta (§4.1) y su `docs/loteN-decisiones.md`; al final de todo, la prueba completa (§4.2), el push y
  **CI verde** en GitHub Actions es el árbitro si algo no se puede correr en la nube.
- **Al terminar, un solo reporte** (§5). Antes de eso, no mandes reportes parciales.

---

## 1. Diseño: cómo queda todo y dónde

### 1.1 Web — Contabilidad → Órdenes de venta (`/sales-orders`, se queda igual)
Pestañas de hoy: **Órdenes · Backorders · Devoluciones**. Cambios:
- Botón nuevo junto a «Nueva orden»: **«Nuevo pedido»** → abre el **pedido guiado** (§1.5) a pantalla completa. «Nueva orden» sigue abriendo
  la ventana de siempre, para la oficina que ya la conoce.
- La orden (ventana y lista) muestra los datos nuevos: distintivo **Urgente**, **Entrega / Recogido**, **forma de pago** (con la marca «cambió
  respecto al cliente»), línea **Cargo de envío** con su porqué, **En consignación**, **firma de quien autoriza** (si la hay) y el distintivo
  **Cliente nuevo** cuando el comprador sigue como prospecto.
- Pestaña nueva **QuickBooks** (visible con `sales.quickbooks.view`): la lista de **facturas por pasar a QuickBooks** (fecha, número, cliente,
  total, «cliente nuevo» / «producto nuevo» con el enlace «ver datos»), el botón **Exportar a QuickBooks** (`sales.quickbooks.export`: pregunta
  si incluye clientes y productos nuevos, baja el archivo `.iif`, y en la misma pantalla «¿Ya importaste estas N?» con la lista marcada) y el
  botón **Marcar como en QuickBooks** (`sales.quickbooks.mark`, una, varias o todas). Arriba, el aviso «N de estas ya salieron el (fecha)…».
  Un enlace «Ver las ya pasadas» abre el histórico con quién y cuándo.

### 1.2 Web — Almacén → Productos e inventario (`/warehouse/products`)
Pestañas de hoy: **Productos · Categorías · Daños · Valor del inventario**. Pestaña nueva: **Precios y ofertas** (`sales.pricelist` para
editar; `sales.view` para ver).
- **Listas de precios**: nombre, vigencia desde/hasta, estado (borrador / vigente / vencida), «aplica a»: todos los clientes, o clientes
  escogidos (para los precios negociados: guantes, cajas, monitor, tirillas). Renglones: producto o kit → precio, foto opcional.
- **Ofertas**: son **kits** (ya existen, se crean en el editor de producto marcando «Kit») que además llevan **foto**, **vigencia** y la
  marca «Es promoción». En esta pestaña se ven como tarjetas con su foto (lo que verá el vendedor), y desde aquí se abre el editor del kit.
- Resolución del precio al pedir (en el servidor): lista vigente del cliente > lista vigente general > precio de venta del producto. El
  vendedor **no teclea precios**; `sales.price` sigue siendo la excepción del admin.

### 1.3 Web — Listas → Clientes, ficha del cliente (paneles apilados, como hoy)
- **Perfil**: campos nuevos **Registro de comerciante (SURI)**; **Envío**: «Usa la regla de la compañía» / «No se le cobra envío» / «Regla
  propia (cargo, mínimo opcional)»; **«Exige la firma de quien autoriza el pedido»** (al lado de «Exige la firma de quien recibe»).
- **Documentos del cliente** (panel nuevo, reusa `StoredFile` KindDocument): foto del registro de comerciante, certificado de revendedor.
- **Estatus**: un cliente creado desde un pedido nace **PROSPECT**; en la ficha, con `sales.approve_client`, botones **Aprobar** (→ ACTIVE,
  pide límite de crédito y término) y **Rechazar** (→ SUSPENDED y cancela sus órdenes abiertas), con comentario. El historial de estatus ya existe.
- **Vendedor**: sigue siendo el campo personalizado «Rep» (nada nuevo).

### 1.4 Web — Sistema → Ajustes (pestañas de hoy: General · Operación · Calendario · Región · Marca · Módulos)
En **Operación**, dos secciones nuevas:
- **Ventas**: cargo de envío (monto, opcional), mínimo para envío gratis (opcional), el cargo lleva IVU (sí/no), el envío se mide después
  del IVU (fijo, informativo).
- **QuickBooks**: cuenta de ventas por omisión, cuenta de costo, cuenta de inventario, nombre del ítem de IVU, términos de pago → nombre en
  QuickBooks (tabla chica). La **hora del correo de la mañana** es la hora del resumen diario que ya existe en General.

### 1.5 El pedido guiado (web responsive y app móvil: la misma secuencia)
Pantalla completa, 4 pasos con barra de progreso, se puede volver atrás; se guarda como **borrador** de orden de venta en cada paso (si el
celular se apaga, el pedido está en Órdenes de venta como borrador).
1. **Cliente**: buscador por número, nombre o teléfono (lista con nombre, pueblo, número). «Cliente nuevo» abre el alta mínima: negocio,
   DBA, contacto, teléfono (10 dígitos) y extensión, teléfono 2, correo, SURI, dirección física (2 líneas, pueblo de la lista, código postal),
   dirección postal (opcional), forma de pago (COD cheque/cash/ACH · crédito 15/20/30/45 días · vendedor recoge cheque · consignación), fotos
   del registro y del certificado (cámara o archivo). Abajo: casilla **Urgente** + nota (500). Forma de pago del pedido **prellenada con la
   del cliente**, editable (si cambia, queda la marca).
2. **Productos**: pestañas por **marca** (de `Product.Brand`, solo marcas con productos activos). Arriba de cada marca, sus **ofertas** vigentes
   como tarjetas con foto y precio (+ / −). Debajo, la **lista completa de la marca**: SKU, nombre, precio (de la lista vigente del cliente o
   general) y casilla de cantidad; teclear una cantidad agrega la línea. Un **carrito** fijo abajo con el conteo y el subtotal; tocarlo muestra
   las líneas para corregir o quitar. Un kit ajustable muestra sus piezas (ya existe `KitLineBlock`).
3. **Entrega**: **Entrega** (consignatario: por omisión la dirección física del cliente; se puede escoger otra o crear una) o **Recogido**.
   Resumen: subtotal, IVU, **cargo de envío** (calculado, con su porqué), **total**. Casilla **En consignación** (solo si la forma de pago es
   consignación). Fecha requerida opcional.
4. **Autorización y envío**: nombre de quien autoriza y **firma dibujada** (obligatoria solo si el cliente lo exige; si no, opcional).
   Botón **Enviar pedido** → en la app pide **Face ID / huella o contraseña** (reautenticación), en la web pide la contraseña; luego hace
   **solicitar** (validar inventario y crédito, reservar). Si falta inventario, el diálogo de faltante que ya existe. Si el cliente es
   prospecto: se solicita igual (reserva) pero la orden queda marcada «espera aprobación del cliente» y no se puede despachar.
   Pantalla final: número `OV-#####`, total, y «Nuevo pedido».

### 1.6 App móvil del personal (`app-movil`)
En el **Home** (lista de accesos) entra **«Pedidos»** (visible con `sales.create`): **Mis pedidos** (los míos, por estatus, con buscador) y
**Nuevo pedido** (los 4 pasos). Firma con el dedo (componente de firma que ya usa el chofer). Reautenticación biométrica al enviar con el
`LockScreen` existente. Sin modo sin señal en la primera versión (el pedido se guarda como borrador en el servidor en cada paso).

### 1.7 Avisos, Pulso y correo
- **Avisos** (campana, ya existe): «Cliente nuevo pendiente de aprobación: X (vendedor Y)» → `sales.approve_client`; «Pedido urgente OV-… de
  X» → almacén (`warehouse.pick`) y, si hay puente, transporte; «Facturas por pasar a QuickBooks: N» → `sales.quickbooks.view`.
- **Pulso del día**: panel **Ventas** (`pulse.sales`, nuevo): pedidos de hoy, urgentes abiertos, clientes por aprobar, facturas por pasar a
  QuickBooks, consignación sin pagar. Cada cifra abre su lista.
- **Correo de la mañana**: entra en el **resumen diario** que ya existe (un correo al día, a la hora de la compañía), solo si hay algo pendiente.

### 1.8 Contabilidad → Facturación (`/money/billing`): pestaña **Consignación**
Lista de facturas en consignación por cliente (dejado, pagado, saldo). **Registrar pago** (`billing.consignment.pay`, nuevo): cliente →
factura → monto (parcial o total), fecha, referencia. Los reportes de ventas **excluyen** lo consignado hasta su pago (se cuenta lo pagado en
la fecha del pago). Las devoluciones de lo no vendido usan la devolución de venta que ya existe.

### 1.9 Puente entre compañías (módulo opcional `INTERCOMPANY`, al final)
Sistema → Integraciones / API → tipo nuevo **«Compañía enlazada»**: «Solutions es cliente de Advance Logistics». Lo propone el admin de una y lo
acepta el de la otra, eligiendo el `Client` de su lado. Con el enlace activo: al crear la entrega de un despacho en Solutions, Teikem crea la
**orden de transporte en Logistics** a nombre de ese cliente (consignatario, paquetes, urgente), por el mismo camino que el API de clientes,
y trae de vuelta número, estatus, prueba de entrega y avisos. Sin enlace, la entrega se crea en Solutions como hoy.

---

## 2. Datos (todo en `Diseño/logistica-db-update.sql`, secciones idempotentes al final; entidades 1:1 en Configurations)

| Tabla | Cambio |
|---|---|
| `SalesOrder` | `IsUrgent bit`, `DeliveryMode` (DELIVERY/PICKUP), `PaymentMethodLookupId` (catálogo nuevo `SalesPaymentMethod`: COD_CHECK, COD_CASH, COD_ACH, CREDIT, CONSIGNMENT), `PaymentTermLookupId`, `PaymentChanged bit`, `ShippingCharge decimal`, `ShippingReason` (código), `IsConsignment bit`, `AuthorizedByName`, `SignatureFileId`, `SubmittedWithAuth` (BIOMETRIC/PASSWORD), `AwaitingClientApproval bit` |
| `PriceList`, `PriceListItem`, `PriceListClient` | nuevas: lista (nombre, desde, hasta, estatus, aplica a todos), renglón (producto o kit → precio, `ImageFileId`), clientes de la lista |
| `Product` | `PromoImageFileId`, `PromoFrom`, `PromoTo`, `IsPromotion bit` (solo kits) |
| `Client` | `MerchantRegistration` (SURI), `ShippingMode` (NULL = compañía / FREE / OWN), `ShippingCharge`, `ShippingFreeFrom`, `RequireOrderSignature bit` |
| `ClientDocument` | nueva: cliente → `StoredFile` (tipo: MERCHANT_REGISTRATION, RESELLER_CERTIFICATE, OTHER) |
| `TenantSetting` (donde viven los ajustes de Operación) | envío: cargo, mínimo, IVU; QuickBooks: cuentas, ítem de IVU, mapa de términos |
| `Invoice` | `QuickBooksAt`, `QuickBooksBy`, `QuickBooksExportedAt`; `Client.QuickBooksSentAt`, `Product.QuickBooksSentAt` |
| `ConsignmentPayment` | nueva: factura → monto, fecha, referencia, quién |
| Permisos (`PermissionCatalog` + seed + roles clonados) | `sales.pricelist`, `sales.approve_client`, `clients.create_basic`, `sales.quickbooks.view`, `sales.quickbooks.mark`, `sales.quickbooks.export`, `billing.consignment.pay`, `pulse.sales` |
| Roles nuevos (de fábrica para compañías nuevas; en Solutions los crea el admin) | **Supervisor de ventas** (`sales.pricelist`, `sales.approve_client`, `sales.view`, `clients.read`, `clients.update`), **Oficina de ventas** (`sales.view`, `sales.create`, `sales.quickbooks.*`, `billing.consignment.pay`) ; **Ventas** suma `clients.create_basic` |
| Catálogos | `SalesPaymentMethod`; `ClientDocumentType`; motivo de envío (`ShippingReason`: COMPANY_RULE, CLIENT_FREE, CLIENT_RULE, AMOUNT, PICKUP) |
| Módulo | `INTERCOMPANY` (parte 7) + tablas `TenantLink` (compañía A, compañía B, cliente en B, estado) y `TenantLinkOrder` (despacho en A ↔ orden en B) |

---

## 3. Partes, orden y quién hace qué

Convenciones del proyecto que aplican: identificadores en inglés, mensajes y comentarios en español; `TenantId` del principal;
`[RequirePermission]` + `[RequireModule(SALES_ORDERS)]`; estatus solo por `StatusService`; SQL congelado → solo `logistica-db-update.sql`;
permiso nuevo ⇒ a los roles clonados; `t('clave')` es/en; claro/oscuro; responsive 360 px; cliente TS generado de `openapi.json`.
**Rama: la que Luis indique en la sesión** (no se crea ninguna). **Una corrida pesada a la vez.** **Sin Brevo**: todo API de prueba con la
llave vacía (`scripts/api-para-pruebas.ps1`).

Modelos / agentes (los definidos en `.claude/agents`): **architect (opus)** = diseño de detalle de cada parte a partir de este plan;
**implementer (opus)** = backend (entidades, SQL, servicios, controladores, pruebas xunit); **fe-implementer-core (opus)** = pantallas con
interacción compleja (pedido guiado web y móvil, pestaña QuickBooks); **fe-implementer (sonnet)** = pestañas y paneles sencillos (precios y
ofertas, ajustes, ficha del cliente, consignación); **reviewer (opus)** con lentes (compilación/EF, fuga entre tenants, conformidad con
este plan, pruebas) y **verifier (opus)** solo para hallazgos altos; **scribe (sonnet)** = manual y FAQ (es y en) y `docs/loteN-decisiones.md`.
Orquestación: la habilidad `lote-implementar` por parte (piezas en paralelo → integración → build/test → revisión → corrección → decisiones),
y `fe-implementar` para las partes de frontend. **La sesión en la nube** es la coordinadora: lanza los agentes, integra los archivos
compartidos (`PermissionCatalog`, `logistica-db-update.sql`, `DependencyInjection`, `routes.tsx`, `es.json`/`en.json`, `openapi.json` y los
`schema.d.ts` regenerados), corre las compuertas y escribe el reporte final. Si en la nube no están disponibles las habilidades o los
agentes nombrados, hace el mismo flujo con los agentes que tenga (un implementador por pieza, un revisor por lente), sin saltarse la revisión.

| # | Parte | Qué incluye | Backend (implementer) | Frontend | Depende de | Tamaño |
|---|---|---|---|---|---|---|
| **P1** | **Lista de precios y ofertas** | Entidades `PriceList*`, promo en el kit (foto, vigencia), resolución del precio al crear/editar línea de orden (`SalesOrderService`), endpoints `/price-lists`, `/products/{id}/promo`, catálogo de marcas con productos y ofertas para el pedido (`GET /sales-orders/catalog?clientId=`) | sí | pestaña **Precios y ofertas** (fe-implementer), editor del kit con foto y vigencia | — | M |
| **P2** | **La orden: datos nuevos, envío, cliente prospecto, permisos** | Columnas de `SalesOrder`/`Client`, catálogo `SalesPaymentMethod`, regla de envío compañía/cliente (`ShippingRules` puro + ajustes), `clients.create_basic` + alta mínima + `ClientDocument`, aprobación/rechazo del prospecto, `AwaitingClientApproval` bloquea despacho, permisos y roles, avisos (cliente nuevo, urgente), panel del Pulso `pulse.sales`, cambios en el PDF/factura (envío, consignación, firma) | sí | ventana de la orden con los campos nuevos; ficha del cliente (SURI, envío, firma, documentos, aprobar/rechazar); Ajustes → Operación → Ventas (fe-implementer) | — | L |
| **P3** | **Pedido guiado (web)** | solo lo que falte en el API (borrador por pasos, firma como `StoredFile`, reautenticación por contraseña al enviar) | poco | `features/sales/wizard/*` (fe-implementer-core): 4 pasos, catálogo por marca, carrito, envío calculado, firma, enviar; responsive 360 px; entrada desde «Nuevo pedido» y ruta `/sales-orders/new-order` | P1, P2 | L |
| **P4** | **Pedido en la app móvil** | reautenticación biométrica en el envío (`SubmittedWithAuth`) | — | `app-movil`: Home → Pedidos (Mis pedidos, Nuevo pedido con los 4 pasos, firma, Face ID/huella) (fe-implementer-core) | P3 (reusa el contrato) | L |
| **P5** | **QuickBooks** | marcas `QuickBooksAt/By/ExportedAt`, generador **IIF** (`QuickBooksIifWriter`, puro: facturas, clientes, productos, kits desglosados con prorrateo, IVU, términos), endpoints listar/exportar/marcar/confirmar, ajustes QuickBooks, aviso + Pulso + resumen diario, permisos `sales.quickbooks.*` | sí | pestaña **QuickBooks** en Órdenes de venta (fe-implementer-core); Ajustes → QuickBooks (fe-implementer) | P2 | M |
| **P6** | **Consignación** | `ConsignmentPayment`, exclusión en reportes/indicadores de ventas hasta el pago, reporte consignado por cliente, permiso | sí | pestaña **Consignación** en Facturación (fe-implementer) | P2 | S |
| **P7** | **Puente entre compañías** | módulo `INTERCOMPANY`, `TenantLink`, creación de la orden de transporte en la compañía enlazada al crear la entrega, retorno de estatus/prueba/avisos, urgente | sí | Integraciones → «Compañía enlazada» (aceptar/rechazar), enlace en la orden a la orden de transporte de la otra compañía (fe-implementer) | P2 | L |
| **P8** | **Datos, cierre y reporte** | kit OFERTA1 (6 × 52800 + 1 × 070120, $41.94, foto), marcas revisadas en los productos de Solutions, roles «Supervisor de ventas» y «Oficina de ventas» en Solutions, un usuario por vendedor (7) con rol Ventas, forma de pago en los clientes, lista de precios inicial (precios del formulario) y lista negociada (guantes/cajas), ajustes de envío ($12 / $120) y QuickBooks; lista de los 7 ítems a renombrar en QuickBooks; manual + FAQ (es y en) con capturas; **prueba completa**; reporte final | scripts SQL idempotentes de datos (no código) | capturas con Playwright para el manual | todas | M |

Orden: **P1 → P2 → P3 → P5 → P6 → P4 → P7 → P8**. P1 y P2 se pueden construir en paralelo (archivos distintos) si Luis lo permite con «2
workers». P7 es el más grande y el único que toca otra compañía: va al final y se puede posponer sin afectar el resto.

---

## 4. Pruebas

### 4.1 Por parte (solo lo del área; no se corre la suite completa en cada parte)
| Parte | Unitarias backend (xunit, `tests/Teikem.Tests`, `--filter`) | Frontend (vitest; compuerta `npm run check` del proyecto tocado) |
|---|---|---|
| P1 | `PriceListRulesTests` (vigencia, solapes, lista por cliente gana a general, kit en lista), `SalesPriceResolutionTests` (precio resuelto al crear/editar línea; `sales.price` sigue siendo excepción), `KitPromoTests` (fuera de vigencia no se vende; orden ya hecha no cambia) | `PriceListsTab.test.tsx`, `priceListRules.test.ts` |
| P2 | `ShippingRulesTests` (compañía/cliente/NULL = compañía, mínimo opcional, después del IVU, solo entrega, porqué), `SalesPaymentTests` (prellenado, marca de cambio, consignación solo con esa forma), `ClientProspectTests` (alta mínima con `clients.create_basic`, PROSPECT, aprobar/rechazar, despacho bloqueado, órdenes canceladas al rechazar), `ClientDocumentTests`, `PermissionSeederTests` (roles clonados reciben los nuevos), `SalesNoticesTests` (cliente nuevo, urgente), `PulseSalesPanelTests` | `OrderModal.test.tsx` (campos nuevos), `ClientProfilePanel.test.tsx` (envío, firma, SURI), `SettingsSalesSection.test.tsx`, `salesRules.test.ts` (envío en el resumen) |
| P3 | `SalesDraftStepTests` (borrador por pasos; firma como archivo; enviar exige reautenticación) | `wizard/*.test.tsx` por paso + `wizardRules.test.ts` (carrito, totales, envío, validaciones por paso, cliente nuevo), prueba de 360 px |
| P4 | — | `app-movil`: pruebas de las reglas compartidas con la web (`shared/` si se extrae) y de las pantallas (jest/expo); `tsc`, lint |
| P5 | `QuickBooksIifWriterTests` (archivo exacto: factura, cliente, producto, kit desglosado con prorrateo que cierra al centavo, IVU, términos, caracteres especiales), `QuickBooksExportTests` (todas las no marcadas salen siempre; aviso de «ya salieron»; marcar/confirmar; clientes y productos solo la primera vez; permisos) | `QuickBooksTab.test.tsx`, `SettingsQuickBooksSection.test.tsx` |
| P6 | `ConsignmentTests` (pago parcial/total, saldo, exclusión de ventas hasta el pago, fecha del pago) | `ConsignmentTab.test.tsx` |
| P7 | `TenantLinkTests` (proponer/aceptar/rechazar, aislamiento: nada cruza sin enlace, idempotencia de la orden creada, retorno de estatus) | `TenantLinkPanel.test.tsx` |

Cada parte termina con: `dotnet build Teikem.sln` sin errores, sus pruebas filtradas en verde, `npm run check` del frontend tocado, revisión
por lentes, hallazgos altos refutados o corregidos, y su `docs/loteN-decisiones.md` corto.

### 4.2 Al final (P8), la prueba completa, en este orden y una cosa a la vez
1. `dotnet build Teikem.sln -c Release` y `dotnet test Teikem.sln` **completos** (todas las pruebas, incluida `FrozenSqlTests`).
2. `npm run check` completo en `web-app`, `app-cliente` y `app-almacen` (tipos regenerados), y la compuerta de `app-movil` (`tsc`, lint, pruebas).
3. Base local **recreada** con `db-init` desde cero (prueba que estructura + seed + update aplican limpios) y, sobre una base creada con
   el `db-init` **del commit anterior al arranque**, `db-update --dry-run` y luego `db-update` (prueba que las secciones nuevas son
   idempotentes sobre una base que ya existía, como pasará en producción). Correr `db-update` dos veces debe dejar «nada que aplicar».
4. API levantado en la nube **sin Brevo** (`Brevo__ApiKey=` vacía; `Portal:ReturnInviteTokenInResponse` y demás banderas de desarrollo
   según `scripts/api-para-pruebas.ps1`, replicadas a mano en Linux) con los datos de demostración, y recorrido Playwright: pedido guiado de
   punta a punta (cliente nuevo → oferta + productos → entrega con envío calculado → firma → enviar → aprobación del cliente → despacho →
   factura → pestaña QuickBooks: exportar, abrir el IIF y comparar con el esperado → marcar), consignación, y las capturas del manual (es y en).
5. Push a la rama indicada y **CI verde** en GitHub Actions. Si algún paso de arriba no se puede correr en la nube (p. ej. Docker), se
   dice en el reporte y el CI es el árbitro.

---

## 5. Reporte final a Luis (un solo reporte, al terminar P8)
- Qué quedó construido por parte y qué **no** (lo pendiente del listado, no lo terminado).
- Las decisiones que el analista tomó solo (acumuladas en los `loteN-decisiones.md`) para que Luis las ratifique o cambie.
- Resultado literal de cada prueba de §4.2 (cantidades, fallos si los hubo).
- Lo que le toca a Luis en producción, como **lista de pasos con los scripts ya escritos** (la sesión en la nube no toca producción):
  armar el paquete e instalarlo (`db-update` lo hace el instalador), correr los scripts de datos de Solutions (roles, usuarios de los 7
  vendedores, kit OFERTA1 con su foto, listas de precios, forma de pago de los clientes, ajustes de envío y QuickBooks), renombrar los 7 ítems
  en QuickBooks, y apagar el formulario de Formsite.

---

## 6. Antes de arrancar, Luis decide (y lo dice en el mensaje de arranque de la sesión en la nube)
1. **Aprobar este diseño y el orden** (o cambiar el orden; P7 se puede dejar para después).
2. **Rama: `master`** (decidido por Luis, 2026-11-06). Todo va a `master`; no se crea ninguna rama.
3. **Paralelismo**: ¿P1 y P2 a la vez (2 piezas) o una tras otra?
4. **Renombrar los 7 ítems en QuickBooks** al SKU de Teikem (lo hace Luis o la oficina, cuando P5 esté listo; la sesión solo entrega la lista).
5. **Nombre visible del módulo** del puente («Compañías enlazadas») y si se construye ahora o después.

Mensaje de arranque sugerido para la sesión en la nube: «Ejecuta `docs/pedido-vendedores-plan-desarrollo.md` completo en `master`,
partes P1 a P8 en ese orden (P7 sí/no), con 2 piezas a la vez como máximo; sin preguntarme; un solo reporte al final».
