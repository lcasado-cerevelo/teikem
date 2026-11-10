# Lote 232 — Kit ARMADO: producto con inventario propio; armar y desarmar (backend, 2026-11-10)

Parte de [ofertas-kits-fotos-diseno.md](ofertas-kits-fotos-diseno.md) §10, §11 y §12 («Lote 232»), acordado con Luis. Solo backend: la pantalla («Nuevo producto» con
«¿Es un kit?», la tabla de composición, Armar / Desarmar, el historial) va en su lote de frontend, con el manual y la FAQ. En paralelo, el lote 231 rehace el kit de
OFERTA (biblioteca); los dos comparten `ProductKitComponent`, `Product.cs`, `DependencyInjection` y el final de `logistica-db-update.sql` (cada uno con su sección).

## Qué se construyó
| Pieza | Qué es |
|---|---|
| Datos (`logistica-db-update.sql`, sección «2026-11-10 — Lote 232») | Catálogo `ProductType` + `ASSEMBLED_KIT` («Kit armado»). Motivos de ajuste **de sistema** `KIT_ASSEMBLY` («Armado de kit») y `KIT_DISASSEMBLY` («Desarmado de kit»). `EntityType` `KIT_ASSEMBLY` / `KIT_DISASSEMBLY`. Contadores por compañía `KITASSEMBLY` (ARM-#####) y `KITDISASSEMBLY` (DES-#####) (se amplía `CK_NumberSequence_Kind`). Tablas `KitAssembly`, `KitAssemblyLine`, `KitDisassembly`, `KitDisassemblyLine`. Sin permisos nuevos. Solo agrega; idempotente (probado dos veces seguidas sobre una base recién hecha con `db-init`). |
| Dominio | `Domain/Wms/AssembledKit.cs`: entidades (`[AuditEntity]`, `ITenantScoped`), `AssembledKitCodes` y `AssembledKitRules` (mensajes exactos, rastreo por las piezas, vencimiento del lote del kit, cuántos kits alcanzan, reparto del regreso). `ProductTypes.AssembledKit`. `AdjustmentReasons.KitAssembly/KitDisassembly` en `SystemAssigned` (un ajuste manual no los puede usar). `NumberingRules` conoce los dos contadores. `KardexRules`: «Armado de kit» / «Desarmado de kit». |
| Servicio | `Infrastructure/Services/AssembledKitService.cs` (alta, composición, armar, desarmar, consultas). `ValuationService`: los armados son capas de costo del kit y los desarmados capas de costo de las piezas; `ConsumedValueAsync` (valor de lo que salió). `InventoryReadService`: el Kárdex muestra el documento ARM/DES como origen. `ProductService`: `isAssembledKit` en la lista y el detalle, `kitPieces` en el detalle, rastreo y dueño fijos en el PATCH. |
| API | `POST /api/v1/products` con `isAssembledKit=true` (ProductsController lo pasa a `AssembledKitService`). `AssembledKitsController`: `/api/v1/products/{publicId}/assembled-kit` (GET, PUT), `/assemblies` (GET, POST) y `/assemblies/{assemblyPublicId}/disassemble` (POST). Módulo `WMS_LOTSERIAL` (el mismo de Productos). |
| Contratos | `Contracts/AssembledKitContracts.cs`; `ProductCreateRequest` + `isAssembledKit`, `kitPieces`, `assembleQuantity`, `assembleWarehousePublicId`, `assembleToBinId`; `ProductListItemDto` + `isAssembledKit`; `ProductDetailDto` + `kitPieces`. `web-app/openapi.json` y los tres `schema.d.ts` regenerados (solo se agregó lo del lote: 3 rutas, 15 esquemas, 3 esquemas ampliados). |

## Contrato para el frontend
Todos con `[RequireModule(WMS_LOTSERIAL)]`. Productos y armados por `PublicId`; posiciones y lotes por id entero (de su almacén / producto). Costos solo con `inventory.value` (sin él viajan `null`).

| Método y ruta | Permiso | Cuerpo → respuesta |
|---|---|---|
| `POST /products` | `inventory.manage` (+ `inventory.adjust` si trae `assembleQuantity`) | `ProductCreateRequest` con `isAssembledKit: true`, `kitPieces: [{ productPublicId, quantity }]` y, opcional, `assembleQuantity` (entero), `assembleToBinId` (obligatorio si se arma), `assembleWarehousePublicId` (o el único almacén activo). `trackingType` se puede omitir: lo deciden las piezas (si se manda distinto → 400). → `ProductDetailDto` (con `product.isAssembledKit=true` y `kitPieces`). Si el armado falla no se crea nada. |
| `GET /products/{id}/assembled-kit` | `inventory.view` | → `AssembledKitDto`: `isAssembledKit` (false si no lo es: el resto vacío), `trackingTypeCode`, `compositionLocked`, `pieces[]` (`productPublicId, sku, name, quantity, sortOrder, isActive, trackingTypeCode`), `availability[]` por almacén activo (`canAssemble` = en UN armado, un lote por pieza; `canAssembleTotal` = juntando lotes en varios armados), `qtyOnHand`, `qtyAvailable` del kit. |
| `PUT /products/{id}/assembled-kit` | `inventory.manage` | `{ pieces: [{ productPublicId, quantity }] }` → `AssembledKitDto`. Reemplaza la composición (y el rastreo que resulta) solo si el kit no tiene armados, movimientos ni existencia. |
| `GET /products/{id}/assembled-kit/assemblies?skip&take` | `inventory.view` | → `KitAssemblyPageDto { total, skip, take, items: KitAssemblyDto[] }` (más reciente primero; `take` ≤ 200). Cada armado: número, cantidad, `disassembledQty`, `remaining`, almacén, posición destino, lote del kit (`kitLotId`, `kitLotNumber`, `kitLotExpiry`), costo, notas, quién y cuándo, `lines[]` (pieza, lote, vencimiento, posición de donde salió, `qtyPerKit`, `quantity`, `returnedQty`, `unitCost`) y `disassemblies[]` (con sus `lines[]`). |
| `POST /products/{id}/assembled-kit/assemblies` | `inventory.adjust` | `KitAssembleRequest { quantity, toBinId, warehousePublicId?, notes?, pieces?: [{ productPublicId, lotId?, binId? }] }` → `KitAssemblyResultDto { assembly, movements: { transactions, balances } }`. Sin `pieces`, cada pieza sale por la rotación de siempre (FEFO/FIFO de la pieza o de la compañía). |
| `POST /products/{id}/assembled-kit/assemblies/{assemblyId}/disassemble` | `inventory.adjust` | `KitDisassembleRequest { quantity, warehousePublicId?, fromBinId?, toBinId?, notes? }` → `KitDisassemblyResultDto { assembly, disassembly, movements }`. Sin `toBinId`, cada pieza vuelve a la posición de donde salió (si los kits están en otro almacén, `toBinId` es obligatorio). |

En el Kárdex los movimientos llevan motivo `KIT_ASSEMBLY` / `KIT_DISASSEMBLY`, origen `KIT_ASSEMBLY` / `KIT_DISASSEMBLY` (documento ARM-##### / DES-#####, referencia «SKU · almacén») y la nota «Armado de kit {SKU} · ARM-00001» (o «Desarmado de kit {SKU} · DES-00001 · ARM-00001»), más la nota del usuario si la hay.

## La regla (lo que valida el servidor)
- **Tipo nuevo, aparte del KIT.** `ASSEMBLED_KIT` tiene existencia propia; el KIT de siempre (hoy solo el kit de oferta) sigue sin existencia. Todo el código que explota kits, sustituye piezas, rechaza existencia propia (`InventoryLedger`: «… es un kit: no tiene existencia propia …»), calcula la foto del kit o manda las piezas a QuickBooks pregunta por el tipo `KIT`, así que el kit armado pasa por el camino de un producto normal en reservas, recolección, despacho, conteos, transferencias, ajustes, valuación, sync del aparato, devoluciones, avisos de salida, listas de precios e importación/exportación.
- **Composición**: 1 a 50 piezas; cada una producto de la compañía, cantidad > 0 (3 decimales), sin repetir, no el propio kit, no un kit (ni KIT ni kit armado), activa, del mismo dueño de inventario que el kit, sin serie (no se admiten piezas por serie en esta versión).
- **Rastreo**: lo deciden las piezas: `LOT` si alguna va por lote, `NONE` si ninguna. No se cambia por el PATCH del producto; se recalcula al cambiar la composición (si la excepción de rotación del kit ya no aplica al nuevo rastreo, se quita).
- **Dueño**: el de las piezas; el PATCH no lo cambia.
- **Composición inmutable** en cuanto el kit tiene algún armado, algún movimiento o existencia/reserva (`compositionLocked`): se crea un kit nuevo.
- **Armar N** (entero, 1 a 100,000): en UNA transacción y por el `InventoryLedger` (única vía de escritura). Bloquea el encabezado del kit (dos armados del mismo kit van en fila) y el ledger bloquea los saldos y revalida (dos armados que comparten piezas no dejan existencia negativa: el segundo recibe 409 insuficiente). Las piezas salen de lo **disponible** (en mano − reservado), en posiciones activas y no de cuarentena, en renta ni cross-dock, del almacén del armado. **Un solo lote por pieza** en cada armado (puede salir de varias posiciones del mismo lote); si el lote no alcanza, el error dice para cuántos kits sí alcanza. Entran N kits a `toBinId` (no puede ser de cuarentena, renta ni cross-dock). **Nada queda reservado.**
- **Lote del kit**: si el kit va por lote, cada armado crea su lote con el mismo número del armado (ARM-#####) y vence con la pieza que vence primero (sin vencimiento si ninguna vence). Las líneas del armado recuerdan el lote, la posición, la cantidad y el costo de cada pieza.
- **Costo**: costo del kit = valor de las piezas que salieron según el método de valuación de la compañía (promedio o capas), calculado como «valor antes − valor después» de cada pieza; por eso el **valor total del inventario no cambia** al armar ni al desarmar (probado en memoria y en SQL Server con los dos métodos). Si ninguna pieza tenía costo, el costo del armado queda `null`.
- **Desarmar N** de un armado concreto: hasta lo que quede de ese armado (`remaining`) y solo kits **disponibles** de ese armado (su lote; sin lote, los kits sin lote) en el almacén: los reservados por una orden o ya despachados no se desarman. Parcial permitido. Cada pieza vuelve a **su mismo lote**, a `toBinId` o a la posición de donde salió, al **costo con que salió** (es una capa de costo de la pieza).
- **Permisos**: armar y desarmar = `inventory.adjust` (como el ajuste de inventario de la web); la composición = `inventory.manage`; consultas = `inventory.view`. Solo web (la app del almacén no lo ofrece).

## Mensajes (para el manual y la FAQ)
| Caso | HTTP | Mensaje |
|---|---|---|
| Sin piezas | 400 | «Un kit necesita al menos un componente.» |
| Más de 50 piezas | 400 | «Un kit lleva como máximo 50 componentes.» |
| Pieza sin producto | 400 | «Indique el producto del componente.» |
| Cantidad no positiva | 400 | «La cantidad de cada componente debe ser mayor que cero.» |
| Pieza repetida | 400 | «El componente B está repetido.» |
| El propio kit como pieza | 400 | «El kit KIT-1 no puede ser componente de sí mismo.» |
| Pieza que es un kit | 400 | «KIT-A es un kit: un kit armado no lleva otro kit como pieza.» |
| Pieza inactiva | 400 | «El componente I está inactivo.» |
| Pieza de otro dueño | 400 | «El componente O es de otro dueño de inventario que el kit.» |
| Pieza por serie | 400 (alta / composición) · 422 (armar) | «La pieza S se rastrea por serie: un kit armado no lleva piezas con serie en esta versión.» |
| Pieza que no existe | 404 | «Componente no encontrado.» |
| `trackingType` distinto del de las piezas (alta) | 400 | «El rastreo de un kit armado lo deciden sus piezas: debe ser LOT (LOT si alguna pieza se rastrea por lote; NONE si ninguna).» |
| Cambiar el rastreo por el PATCH | 422 | «El rastreo de un kit armado lo deciden sus piezas: no se cambia aquí (cambie la composición mientras el kit no tenga armados, o cree un kit nuevo).» |
| Cambiar el dueño por el PATCH | 422 | «El dueño de un kit armado es el de sus piezas: no se cambia (cree un kit nuevo con las piezas del otro dueño).» |
| Composición con historia | 409 | «El kit KIT-1 ya tiene armados, movimientos o existencia: su composición no se puede cambiar. Cree un kit nuevo.» |
| No es kit armado (PUT, armar, desarmar) | 422 | «El producto A no es un kit armado.» |
| Kit sin piezas al armar | 422 | «El kit KIT-1 no tiene piezas: agregue su composición antes de armar.» |
| Kit inactivo | 422 | «El producto KIT-1 está inactivo; no admite entradas de inventario.» |
| Sin cantidad | 400 | «Indique cuántos kits.» |
| Cantidad no entera o ≤ 0 | 400 | «La cantidad de kits debe ser un número entero mayor que cero.» |
| Más de 100,000 | 400 | «Se arman o desarman como máximo 100,000 kits a la vez.» |
| Sin posición destino (armar) | 400 | «Indique la posición donde quedan los kits.» |
| Armar al crear sin posición | 400 | «Para armar kits al crear el producto indique la posición donde quedan.» |
| Posición de cuarentena / renta / cross-dock | 422 | «La posición Q-01 es de cuarentena, en renta o de cross-dock: no se usa para armar ni desarmar kits.» |
| Posición inactiva | 422 | «La posición K-01 está inactiva; no admite movimientos de inventario.» (del ledger) |
| Posición / lote / producto / armado que no existe | 404 | «Posición no encontrada.» · «Lote no encontrado.» · «Producto no encontrado.» · «Armado no encontrado.» |
| Escoger un producto que no es pieza | 400 | «El producto indicado no es pieza de este kit.» |
| La misma pieza dos veces en la selección | 400 | «La pieza A está repetida en la selección.» |
| Lote en una pieza sin lote | 400 | «La pieza B no se rastrea por lote: no indique lote.» |
| Lote que no alcanza (un lote por pieza) | 409 `insufficient_stock` | «El lote A2 de A solo alcanza para 2 kit(s): hay 4 disponible y cada kit lleva 2. Arme 2 con ese lote y el resto en otro armado con otro lote.» · «El lote A1 de A no alcanza para ningún kit: hay 0 disponible y cada kit lleva 2.» |
| Pieza sin lote que no alcanza | 409 `insufficient_stock` | «No hay suficiente B disponible en W1 para 3 kit(s): hay 10 y se necesitan 12; alcanza para 2.» |
| Dos armados a la vez sobre la misma pieza (el segundo) | 409 `insufficient_stock` | el del ledger sobre la pieza (saldo bloqueado) |
| Notas de más de 300 | 400 | «Las notas admiten como máximo 300 caracteres.» |
| Varios almacenes y sin `warehousePublicId` | 400 | «Indique el almacén: la compañía tiene más de uno.» |
| Desarmar más de lo que queda del armado | 409 | «Del armado ARM-00001 quedan 1 kit(s) por desarmar; pidió 2.» |
| Desarmar kits reservados / no disponibles | 409 `insufficient_stock` | «Solo hay 0 kit(s) disponible(s) del armado ARM-00001 en W1 y pidió desarmar 1. Los kits reservados por una orden o ya despachados no se desarman.» |
| Desarmar en otro almacén sin `toBinId` | 400 | «Indique la posición donde vuelven las piezas: los kits están en otro almacén que el del armado.» |
| Ajuste manual con `KIT_ASSEMBLY` / `KIT_DISASSEMBLY` | 400 | «El motivo KIT_ASSEMBLY lo asigna el sistema.» |
| Sin `inventory.adjust` (armar, desarmar, armar al crear) | 403 | «No tiene permiso para esta acción.» |

## Cómo se probó
- `dotnet build Teikem.sln`: sin errores.
- `AssembledKitTests` (InMemory con el `InventoryLedger` real, 14 pruebas): alta con composición y rastreo por las piezas; rechazos de piezas; armar al crear; armar (salen las piezas por FEFO, entran los kits a su lote ARM-00001 con el vencimiento menor, nada reservado, costo = suma, valor total igual, Kárdex con los dos lados y el motivo); lote que no alcanza y escoger otro; solo lo disponible (ni cuarentena ni reservado) y validaciones; permiso `inventory.adjust`; desarmar parcial a su lote y posición al costo con que salió, no si está reservado, valor total igual, historial; vender, reservar y despachar el kit como producto normal (sin explotar; se reserva el kit y no las piezas) y backorder sin existencia; sin sustitución; composición inmutable con historia, rastreo y dueño fijos; transferir y ajustar el kit como cualquier producto con lote; la importación actualiza un kit armado como producto pero no su rastreo; aislamiento por compañía; reglas puras.
- `AssembledKitSqlTests` (SQL Server real, `TEIKEM_TEST_SQLSERVER` como el CI, 4 pruebas; una es Theory con los dos métodos de valuación = 5 casos): las consultas se traducen; armar y desarmar con existencias, lotes y valor; costo por promedio y por capas con el valor total igual; **dos armados a la vez que comparten pieza nunca dejan existencia negativa**; otra compañía no ve nada.
- `TenantIsolationModelTests`: 45 entidades WMS (se agregan las cuatro del lote) e índices únicos `UQ_KitAssembly_Number` / `UQ_KitDisassembly_Number` espejados.
- `dotnet test Teikem.sln` completo con `TEIKEM_TEST_SQLSERVER` (2026-11-10, con el lote 231 a medias en el mismo árbol): «Passed!  - Failed: 0, Passed: 5846, Skipped: 0, Total: 5846».
- `db-init` sobre una base nueva (`Teikem_L232`): bien; la sección «Lote 232» aplicada dos veces más con `sqlcmd -I`: sin errores ni cambios; `db-update` después: «base de datos al día».

## Decisiones a revisar
1. **Módulo**: `WMS_LOTSERIAL` (el de Productos), no uno nuevo.
2. **Armar = tipo de movimiento Ajuste** con motivos de sistema propios (no un tipo nuevo `ASSEMBLY`): así los reportes, el Kárdex y la app ya lo entienden; el origen (ARM/DES) lo distingue.
3. **Número del lote del kit = número del armado** (ARM-#####): único por compañía y fácil de rastrear al revés. El lote no se edita por esta vía.
4. **Un solo lote por pieza en cada armado** (pedido de Luis): si no alcanza, el usuario hace dos armados. `canAssemble` (en uno) y `canAssembleTotal` (en varios) se muestran por separado.
5. **El costo sale del método de la compañía** («valor antes − valor después» de cada pieza) en vez del `PurchaseCost` suelto: es lo único que deja el valor total igual con promedio y con capas.
6. **Kits que entran por un ajuste manual o un conteo** (sin armado) se valúan como cualquier producto sin capa (su `PurchaseCost`, o sin costo). Se permite porque «se cuenta como cualquier producto»; las piezas no se tocan en ese caso.
7. **Desarmar sale del lote del armado**: los kits de distintos armados no se mezclan aunque sean del mismo producto (con rastreo por lote). Sin lote, los kits son intercambiables y se descuentan del armado escogido.
8. **Composición bloqueada también con existencia que no vino de un armado** (p. ej. un ajuste): la regla de Luis es «cambiarla es crear un kit nuevo» en cuanto hubo historia.
9. **Un kit armado puede ser pieza de un kit de oferta** (y una pieza puede estar en los dos): hoy `KitService` solo rechaza como pieza el tipo `KIT`; al pedir el kit de oferta se reservaría el kit armado como cualquier producto. Lo decide el lote 231 si quiere otra cosa; aquí no se bloquea ni se probó.

## Riesgos y lo que no se probó
- No hay frontend ni manual todavía (lote siguiente); el contrato puede necesitar algún campo más cuando se dibuje la pantalla.
- No se probó contra SQL Server la recolección/despacho de un kit armado con lote por `PickBatch` (en memoria sí, vía la orden de venta) ni un conteo cíclico de kits armados: usan el camino normal del producto con lote, sin código nuevo.
- La sync del aparato (`app-almacen`) no se probó con un kit armado: lo trata como producto con lote; armar/desarmar no se ofrece ahí (solo web, por decisión de Luis).
- `canAssemble` se calcula por almacén leyendo saldos sin bloquear: es orientativo; el armado revalida con el saldo bloqueado.
- El costo de un armado con piezas parcialmente sin costo suma solo las que tienen (las unidades sin costo no suman); si ninguna tiene costo queda `null`.
- Si la compañía cambia de método de valuación después de armar, los costos guardados de armados y desarmados no se recalculan (igual que los recibos).
- La exportación de productos y el reporte de valuación muestran el kit armado como un producto más (sin columna «kit armado»).
