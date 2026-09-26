# Lote 6 — Inventario y almacén: plan de implementación (implementado con los defaults del panel; decisiones pendientes de ratificación)

Resultado del workflow `lote-diseno` (3 lectores, crítico de completitud, 3 arquitectos, 2 jueces y síntesis).
Puntajes de los jueces por diseño: mínimo viable 14, riesgo primero 16.3, extensibilidad 12.4.

## Decisiones pendientes de ratificación por Luis

- D1: las olas de picking por orden (PickWave/PickTask) y el empaque en cartones (Carton) se DIFIEREN; 'Recolección y empaque' ad hoc cubre el flujo de hoy.
- D4/D5: la varianza de recepción se asienta como recepción por lo esperado más un ajuste por la diferencia; el recibo se confirma completo, no línea por línea.
- D9: Compras mínimas (proveedores y órdenes de compra) entran en este lote, adelantadas del Lote 10, porque la recepción las necesita.
- D11/D12: 'Empacar' crea la orden desde la recolección (líneas = paquetes) y esa orden no se borra desde Órdenes (409); cancelarla NO devuelve la mercancía al inventario: regresa con un recibo de devolución.
- D13/D14: eliminar una recolección revierte cada línea a su posición original con un ajuste de entrada; la recolección asigna posición y lote por FEFO (vence primero) y excluye cuarentena y cross-dock.
- D21: todo recibo entra a una posición de una zona STAGING (obligatoria; 422 si el almacén no tiene); no existen saldos 'sin posición'.
- D22: el conteo cíclico se reconcilia contra el saldo ACTUAL bloqueado (no contra la foto), marcando las líneas cuyo saldo cambió desde la foto.
- D26: la baja de un almacén es definitiva y solo procede vacío, sin reservas y sin documentos abiertos.
- D27: 4 permisos nuevos (58 en total): inventory.view, inventory.manage, inventory.adjust, warehouse.manage; costos y precios se ven con inventory.view (D31).
- D29/D30: cross-dock como demo bajo el módulo CROSSDOCK: asignación sobre recibo abierto o sobre inventario, citas de muelle sin solapamiento (60 min por defecto, ARRIVED ocupa el muelle).
- D35: el costo se congela en la compra y en la recolección; el precio de venta NO se congela (Facturación decide al facturar).
- D40: las transferencias entre almacenes son instantáneas (sin estado 'en tránsito').
- D42: el filtro por almacén de UserDataScope sigue diferido; un usuario con alcance de almacén ve todos los almacenes del tenant.

## Enfoque

RIESGO PRIMERO. El Lote 6 construye tres módulos del documento maestro: el 5 (Almacenes/WMS), el 6 (Cross-docking, en modo demo) y el 7 (Inventario y trazabilidad por lote/serie). Suma la parte de Compras (13B) que necesitan la recepción contra PO y la pantalla 'Ajustes de inventario'.

Inventario es dinero. Un saldo que no cuadra con el ledger, un saldo negativo o una serie despachada dos veces son pérdidas reales. Todo el diseño gira alrededor de cinco riesgos. Cada uno lleva una guarda en código, una guarda de última línea en SQL cuando existe, y una prueba unitaria o un paso de smoke.

Este plan parte del plan ganador y le injerta las ideas que señalaron los jueces:
- cantidad con signo en el ledger (maestro L331);
- cancelación de OC desde PARTIAL (L461), con la capacidad EDIT_PURCHASE_ORDER para editar;
- la cabecera de la recolección se inserta antes de los ISSUE, así que nunca hay UPDATE sobre el ledger;
- el conteo lo confirma quien tiene warehouse.count y se ajusta contra el saldo actual (L542);
- la cola de tareas funciona con IWarehouseTaskHandler por tipo;
- costuras IOrderInventoryLines, IPurchaseOrderReceiving, IReceiptConfirmationParticipant e InventoryScope;
- reserva en el ledger (ReserveAsync/ReleaseAsync), que usa el cross-dock;
- cross-dock asignado con el recibo abierto y repartido al confirmar, con el faltante outbound visible (L320);
- rotación de 30 días en el putaway (L301);
- almacén demo sembrado;
- vw_LotGenealogy ampliada;
- vistas del mock (L874);
- pruebas de servicio en InMemory.

(1) INTEGRIDAD DEL LEDGER (el riesgo número uno)
- UNA sola vía de escritura: InventoryLedger (P0). Solo ese archivo escribe StockBalance (QtyOnHand y QtyReserved), InventoryTransaction y el estado/ubicación de InventorySerial. WmsWriteConfinementTests lo verifica por texto sobre src/.
- InventoryTransaction es de solo inserción. Una reversa es siempre un movimiento nuevo (R36). La recolección inserta su cabecera ANTES de contabilizar, así que el Ref PICK_BATCH+id va en el INSERT del ISSUE: nadie hace UPDATE del ledger (D48).
- Convención (D3, maestro L331: 'cada despacho escribe movimiento negativo; cada recepción, positivo'): Quantity CON SIGNO.
  - RECEIPT: + con To.
  - ISSUE y CROSSDOCK: − con solo From.
  - ADJUSTMENT: + con To o − con From, con motivo de catálogo obligatorio (AdjustmentReason).
  - TRANSFER: + con From y To distintos, en una sola fila.
  Los llamadores pasan la MAGNITUD (InventoryPosting.Quantity > 0) y el ledger guarda el signo según la dirección. Nadie puede escribir un signo incoherente.
- Última línea en SQL:
  - CK_StockBalance_Qty: en mano ≥ 0, reservado ≥ 0, reservado ≤ en mano;
  - CK_InvTxn_Quantity: Quantity <> 0;
  - CK_InvTxn_Direction: Quantity > 0 exige To; Quantity < 0 exige From y prohíbe To.
  El ledger traduce la violación 547 a 409 insufficient_stock.
- Reserva: ReserveAsync/ReleaseAsync, más FromReserved en PostAsync. En este lote la usa solo el cross-dock: lo asignado en staging queda reservado, y ninguna recolección, putaway ni transferencia lo toma. Las olas (D1) y el Portal lo reutilizarán sin tocar el ledger.
- Invariante verificable: GET /api/v1/inventory/reconciliation reconstruye el saldo desde el ledger y lo compara con StockBalance. Por clave: + en To, − en From (InventoryRules.Rebuild). Por producto: Σ Quantity sin TRANSFER = Σ QtyOnHand (InventoryRules.NetByProduct, la lectura literal de L331). El smoke exige cero descuadres al final de TODOS los flujos, incluidos los concurrentes.
- QtyAvailable, VarianceQty y LineTotal son columnas computadas de SQL. Se mapean como computadas y NUNCA se usan en lógica, porque InMemory no las calcula: el disponible se calcula en código (InventoryRules.Available).

(2) INTEGRIDAD MULTI-TENANT
- El TenantId sale del principal; ninguna solicitud lo lleva.
- Los encabezados con PublicId (Warehouse, Product, ReceiptHeader, PurchaseOrder, PickBatch) se exponen por PublicId. Las hijas sin TenantId (zona, posición, muelle, lote, línea) se exponen por id int y SIEMPRE se resuelven a través de su padre filtrado (WmsResolve). Así una posición de otro almacén o de otro tenant da 404 'Posición no encontrada.', sin oráculo.
- Última línea en SQL (D18): FKs compuestas en tres cadenas.
  - (Id, TenantId): StockBalance, InventoryTransaction, Product, Asn, ReceiptHeader, PurchaseOrder, WarehouseTask, CycleCount, PickBatch, DockAppointment y CrossDockPlan contra Warehouse, Product, Client, Supplier, Asn, Trip o TransportOrder.
  - (Posición, Almacén): WarehouseBin gana WarehouseId.
  - (Lote/Serie, Producto).
  InventoryLot e InventorySerial no tienen TenantId: heredan la tenencia de Product por FK compuesta, y el SQL lo anota en un comentario.
- Resolvers de pertenencia para las 14 entidades nuevas con permiso de dueño: 11 reales y 3 cerrados (INVENTORY_SERIAL, WAREHOUSE_TASK, CROSSDOCK_ALLOCATION).
- InventoryScope(OwnerClientId) (D44), con el mismo patrón que OrderScope: las lecturas de productos, lotes, series, saldos, Kárdex, genealogía, rastro de serie y ASN lo reciben. Los controladores internos pasan Any; el Portal (Lote 8) pasará el cliente, sin reescribir consultas.
- El filtro adicional por almacén de UserDataScope se difiere explícitamente (D42).
- El SQL crudo vive confinado en InventoryQueries (17 sentencias). Cada una lleva 'TenantId =' explícito; RawSqlConfinementTests lo verifica.
- El smoke prueba BOLA por id hijo, más aislamiento completo con el admin del otro tenant (T3).

(3) CONCURRENCIA
Hay un ORDEN DE BLOQUEO ÚNICO en todo el lote, documentado en InventoryQueries:
1. Encabezado del documento, en este orden: PickBatch < CrossDockPlan < CycleCount < ReceiptHeader < Asn < PurchaseOrder < Product < Warehouse < WarehouseDock.
2. WarehouseTask.
3. StockBalance, por clave ordenada (ProductId, WarehouseId, BinId, LotId), con upsert HOLDLOCK + UPDLOCK.
4. InventorySerial, por SerialId ascendente.
5. NumberSequence, al final.

Excepción documentada (D48): 'Recolectar' toma PACKBATCH como PRIMER bloqueo, para insertar la cabecera con su número EMP antes de los ISSUE. No hay ciclo: ninguna transacción bloquea saldos y después PACKBATCH, y la creación de órdenes no toca saldos. El costo es que las altas de órdenes esperan a lo sumo una recolección (≤ 100 líneas). Una recolección fallida revierte su número, así que los EMP quedan consecutivos y sin huecos.

Reglas derivadas:
- Todo escritor de CrossDockAllocation bloquea el plan y después el recibo de la línea. La confirmación del recibo escribe asignaciones bajo el bloqueo del recibo sin tocar el plan, así que no hay inversión.
- La cola de tareas llama a IWarehouseTaskHandler.LockReferencesAsync ANTES de bloquear la tarea: PUTAWAY bloquea su recibo; CROSSDOCK bloquea plan y recibo.
- Desactivar un producto, almacén o posición sigue este orden: encabezado (U), luego rango de saldos HOLDLOCK, verificación, y SOLO ENTONCES escritura.
- Cancelar una OC bloquea la PO y rechaza si tiene un recibo OPEN. Borrar un recibo OPEN nacido de PO cancela su ASN, así que no quedan ASN huérfanos.
- Los interbloqueos restantes (1205) los reintenta la estrategia de ejecución: todo corre en RunInTransactionAsync.
- Re-verificación dentro de la transacción en: confirmar recibo, completar tarea, reconciliar conteo, empacar/eliminar recolección, resolver faltante y cancelar OC.
- Últimas líneas en BD: UX_Receipt_Asn, UX_PickBatch_Order, UQ_PickBatch_Number, UQ_CycleCountLine, UQ_WarehouseBin_WhCode, UX_Product_Barcode, UQ_Product_Sku y UQ_StockBalance.

El smoke dispara en paralelo:
- 8 recolecciones sobre 5 unidades;
- 2 confirmaciones del mismo recibo;
- 2 empaques y 2 eliminaciones del mismo lote;
- 2 resoluciones del mismo faltante;
- 2 citas solapadas en un muelle;
- 4 recibos;
- desactivar un producto contra confirmar un recibo.
Después verifica la conciliación sin descuadres.

(4) TRANSICIONES DE ESTATUS
Todo estatus pasa por StatusService.TransitionAsync, con historial por EntityType propio; los efectos se registran por dominio.
- (a) WarehouseTaskId pasa de BIGINT a INT (D19).
- (b) SerialStatus: RESERVED y SHIPPED pasan a LATERAL y se agrega SCRAPPED terminal (D16).
- (c) CANCELLED en WarehouseTaskStatus, AppointmentStatus y AllocationStatus; dominio nuevo PickBatchStatus (D20).
- (d) PurchaseOrder:
  - StatusService permite un lateral o terminal SIN reglas desde cualquier etapa. Por eso NO se siembra regla lateral para CANCELLED: se cancela desde DRAFT, SENT y PARTIAL (RECEIVED es terminal), con comentario en el historial (L461, D47).
  - Editar se protege con la capacidad EDIT_PURCHASE_ORDER, negada fuera de DRAFT, más StatusService.EnsureAllowedAsync (D46).
- (e) Un recibo que al confirmar no genera ninguna PUTAWAY (todo a cross-dock o recibido 0) pasa directo a PUTAWAY.

Contenido congelado por regla dura, porque ya está en el ledger:
- un recibo confirmado;
- un conteo reconciliado;
- una recolección empacada, que solo se elimina si su orden sigue en la etapa inicial.
Borrar desde Órdenes una orden nacida de una recolección responde 409 (D12).

(5) DINERO Y NÚMEROS
- Costos y precios en DECIMAL(18,4) con CHECK ≥ 0.
- Cantidades en DECIMAL(16,3): máximo 3 decimales, y enteras para SERIAL.
- LineTotal se calcula en SQL. CostValue y SaleValue se calculan en C# con Round4 AwayFromZero.
- El costo se congela en PurchaseOrderLine.UnitCost y en PickBatchLine.UnitCost (D35).
- Números: REC-#####, CC-#####, XD-#####, PO-##### y EMP-#####. El contador PACKBATCH se comparte con las órdenes, así que el número de la recolección ES el PackBatchNumber de la orden.
- Topes: 200 líneas por recibo, 100 por recolección, 500 series por línea, 1000 líneas por conteo, 200 filas por página.

Toda regla de negocio es pura y se prueba sin BD. Además hay pruebas de servicio en InMemory (WmsFixture, siguiendo el precedente de TripServiceFixture) para:
- recepción, con AdjustmentTxnId, ASN a RECEIVED y el 422 de la doble confirmación;
- ajustes y transferencias;
- lecturas con InventoryScope;
- conteo contra el saldo actual;
- líneas de inventario por orden.

ORGANIZACIÓN
- P0 es la ÚNICA pieza con archivos compartidos y se integra primero. Entrega SQL y seed completos, constantes y permisos, entidades, configuraciones, DbContext, contratos con firma posicional completa, reglas transversales, costuras implementadas (InventoryQueries, InventoryLedger, WmsResolve, WarehouseTaskWriter, PutawaySuggester), interfaces de costura, resolvers, DI, contenido de análisis, el almacén demo en DemoTenantSeeder y esqueletos compilables.
- Las costuras entre piezas tienen la interfaz en P0 y la implementación en su pieza:
  - IWarehouseTaskHandler: P5 (PUTAWAY/REPLENISH), P6 (COUNT) y P9 (CROSSDOCK);
  - IPurchaseOrderReceiving: P8, la consume P4;
  - IReceiptConfirmationParticipant: P9, la consume P4;
  - IOrderInventoryLines: P7, para el Lote 10.
  Los esqueletos de costuras que consume otra pieza devuelven un no-op seguro (diccionario o lista vacíos) en vez de lanzar.
- P1 a P9 van en paralelo, con archivos disjuntos. P7 es la única que toca archivos del Lote 3 (OrderService.cs y OrderRules.cs), para la guarda de borrado.
- P10 cierra: smoke, seguridad de controladores, cobertura de handlers, decisiones, manual 06 y FAQ.

MÓDULOS (D28)
- WMS_LOTSERIAL: almacenes, productos, inventario, recepción, tareas, conteo y recolección.
- PURCHASING: proveedores, PO y faltantes.
- CROSSDOCK: citas y planes. Apagado para Advance; el backend responde 403 module_disabled hasta que el tenant lo enciende.

HALLAZGOS AL VERIFICAR EL CÓDIGO
- OrderCreationOptions(PackBatchNumberOverride, SourceEntityType, SourceEntityId) ya existe, y CreateAsync se une a la transacción ambiente con savepoints.
- La numeración de órdenes (R39-R49) ya vive en OrderService y NumberingRules desde el Lote 3, con Client.ClientAssignsOrderNumber y ClientAssignsInvoiceNumber (BIT; no existen columnas 'OrderNumberBy/InvoiceNumberBy'). Empacar solo la integra.
- CargoLine es 'paquete': lo leen cotización, lectura de la orden, fuente Órdenes y capacidad de rutas, y el PATCH de paquetes da de baja las líneas. Por eso las olas se difieren (D1) y las líneas de producto por orden salen de PickBatchLine vía IOrderInventoryLines (D45).
- OrderService.DeleteAsync hace IsActive=0 sin efectos: se agrega la guarda (D12).
- Tres pruebas (FleetCatalogTests, OrderCatalogTests, TripCatalogTests) y el PRINT del seed fijan 54 permisos: P0 los pasa a 58.
- Warehouse.GeoPoint es GEOGRAPHY: para bloquear se usa 'SELECT WarehouseId AS Value', nunca 'SELECT *'.
- EF agrega 'IS NOT NULL' a los índices únicos con columnas anulables. UQ_StockBalance, UQ_Product_Sku, UQ_CycleCountLine y UX_ProductCategory_Name se mapean con HasFilter(null) o con su filtro exacto.
- StatusService.LateralEntryAllowedAsync: sin reglas, un lateral se permite desde cualquier etapa.
- StatusCapability + EnsureAllowedAsync ya es la convención (EDIT_CONTRACT, EDIT_CARGO, EDIT_WORK_ORDER, EDIT_TRIP).
- UserDataScope se guarda y se expone en /me, pero ningún servicio lo aplica (docs/lote1-decisiones.md:59 lo dejó para los Lotes 2 y 6; el Lote 2 tampoco lo aplicó).
- DemoTenantSeeder no siembra almacén. Sin él, el tenant demo no puede recibir hasta crear a mano una zona STAGING.
- vw_LotGenealogy existe con Quantity, sin posiciones, motivo ni usuario.
- purchasing.view, purchasing.manage y purchasing.receive ya existen. WarehouseOperator ya tiene warehouse.count y purchasing.receive.
- Vehicle/Driver.HomeWarehouseId siguen sin escribirse (D33).

CONVENCIONES DEL LOTE
- Namespace Teikem.Domain.Wms para entidades y reglas; servicios en src/Teikem.Infrastructure/Services; costuras en src/Teikem.Infrastructure/Wms.
- Los mensajes exactos son 'public const' o métodos estáticos en las reglas puras, porque el manual y la FAQ los citan.
- Fechas de filtros en UTC.
- Terminología: 'Ubicación/Posición' = WarehouseBin; 'Recibo' = ReceiptHeader/ReceiptLine.
- 'Dueño del producto' = Product.ClientId: un cliente 3PL de la tabla Client, o NULL = propio del tenant. Nunca se confunde con TenantId, el dueño de los datos.

## Resumen

El Lote 6 entrega Inventario y almacén: WMS, trazabilidad por lote y serie, Compras mínimas y Cross-dock como demo. Es la síntesis del plan ganador con los injertos de los jueces.

**Almacenes (R2-R6)**
- Alta, edición y baja definitiva de almacenes. La baja solo procede con el almacén vacío y sin documentos abiertos.
- Zonas tipadas: PICKING, RESERVE, REFRIGERATED, QUARANTINE, CROSSDOCK y la nueva STAGING.
- Posiciones con código pasillo-rack-nivel-posición, únicas por almacén.
- Muelles tipados con estatus.
- Almacén por defecto cuando el tenant tiene uno solo.
- El tenant demo nace con el almacén ALM-01: zonas STG, PCK, RSV y QUA, posiciones y muelles.

**Productos (R23-R26, R31)**
- SKU propio o de un cliente 3PL, UoM base, seguimiento NONE/LOT/SERIAL, peso y volumen, código de barras único.
- Costo, precio y mínimos.
- Categorías jerárquicas.
- Desactivar solo sin inventario en mano ni documentos abiertos. Seguimiento, UoM y dueño son inmutables tras el primer movimiento.

**Inventario (R1, R27-R29, R32, R33)**
- Saldo por almacén, posición y lote, con filtros.
- Kárdex de solo lectura con cantidad CON SIGNO, como la guarda el ledger (L331): despacho negativo, recepción positiva. Incluye origen legible y usuario.
- Ajuste manual ± con motivo de catálogo.
- Transferencias entre posiciones y entre almacenes.
- Genealogía de lote y rastro de serie.
- Conciliación ledger↔saldo.
- Reserva de saldo en el ledger, que usa el cross-dock.
- Lecturas con InventoryScope(dueño), listas para el Portal.

**Recepción (R7-R9)**
- ASN de cliente, recibo contra PO (vía la costura IPurchaseOrderReceiving), ciego o de devolución.
- La cantidad recibida arranca igual a la esperada.
- Al confirmar:
  - RECEIPT por lo esperado y ADJUSTMENT por la diferencia (RECEIPT_VARIANCE);
  - la PO pasa a PARTIAL o RECEIVED;
  - el cross-dock asignado se reparte;
  - se crean las PUTAWAY del remanente con posición sugerida.

**Tareas (R10, R15, R17)**
- Cola unificada con handlers por tipo (IWarehouseTaskHandler), cada uno con su permiso.
- PUTAWAY, REPLENISH y CROSSDOCK se completan desde la cola; COUNT se completa desde su pantalla.
- Putaway dirigido con rotación por salidas de 30 días.
- Reabasto bajo demanda con FEFO.
- El recibo pasa a PUTAWAY al terminar su último putaway.

**Conteo cíclico en modo informado (R16, L542)**
- Foto del sistema; captura de cantidades o de series.
- Confirmar requiere warehouse.count.
- El ajuste va contra el saldo ACTUAL bloqueado. La línea queda marcada SystemQtyChanged si el saldo se movió desde la foto.
- 409 si lo contado es menor que lo reservado.

**Recolección y empaque ad hoc (R13, R34-R43)**
- FEFO o posición/lote/serie explícitos.
- Número EMP-##### tomado primero; el ISSUE nace con Ref PICK_BATCH.
- Lista con filtros y búsqueda final.
- Empacar crea la orden real.
- Eliminar revierte el inventario.
- Desde Órdenes no se borra una orden nacida de un empaque.
- IOrderInventoryLines deja las líneas de producto por orden listas para PRODUCT_SALE del Lote 10.

**Compras mínimas y Ajustes de inventario (R14)**
- Proveedores.
- PO en borrador, enviada, recibida parcial/completa, cancelada o eliminada.
- La edición se controla con la capacidad EDIT_PURCHASE_ORDER.
- Se cancela también desde PARTIAL, con bitácora.
- Se elimina solo sin recepciones.
- Faltantes por línea resueltos con Cerrar, Reordenar o Ajuste manual, en una tabla nueva.

**Cruce de muelle (demo, módulo CROSSDOCK; R19b-R22)**
- Citas sin solapamiento; la llegada ocupa el muelle.
- Planes XD-##### que asignan líneas de recibos ABIERTOS (se reparten al confirmar, con faltante outbound visible por asignación) o ya confirmados (reduce el putaway).
- Lo asignado queda reservado.
- Mover = CROSSDOCK que sale del inventario, desde la pantalla o desde la cola.

**Análisis**
- 7 fuentes nuevas.
- Vistas del mock: 'Inventario bajo mínimo', 'Productos por cliente dueño' y 'Movimientos por tipo' (agrupada, con suma de cantidad), más las propias.
- 7 indicadores y 6 gráficos.
- vw_LotGenealogy ampliada con posiciones, motivo, usuario y NetQuantity para BI.

**Números del lote**
- 4 permisos nuevos (58 en total).
- 10 EntityTypes nuevos.
- 3 catálogos nuevos y 1 capacidad nueva.
- 3 tablas nuevas.
- Columnas nuevas sobre 19 tablas existentes.

**Queda fuera, con DECISIÓN**
- Olas y cartones (D1).
- Plantillas de exportación y contabilización (Lote 10).
- Conversión de UoM.
- Filtro por almacén de UserDataScope (D42).

## Piezas

### P0 — Base compartida: SQL, seed, constantes, permisos, entidades, configuraciones, DbContext, contratos con firma posicional completa, reglas transversales, costuras implementadas (InventoryQueries, InventoryLedger con signo y reserva, WmsResolve, WarehouseTaskWriter, PutawaySuggester con rotación), interfaces de costura (IWarehouseTaskHandler, IPurchaseOrderReceiving, IReceiptConfirmationParticipant, IOrderInventoryLines, InventoryScope), resolvers, DI, contenido de análisis, almacén demo, esqueletos y pruebas de modelo

- Toca archivos compartidos: sí. Depende de: nada.

Es la ÚNICA pieza con archivos compartidos y se integra primero. Al terminar debe quedar:
- la solución compilando;
- las pruebas previas (con los tres conteos de permisos 54→58 ajustados) y las nuevas de P0 en verde;
- los smokes de los Lotes 1 a 5 en verde sobre una BD recreada.

El script de estructura se aplica una vez por hash, así que P0 entrega TODO el SQL y el seed del lote ('cambiosSql', inline con '-- Lote 6'). Los esqueletos de (13) existen solo para que DI compile; sus archivos pertenecen a la pieza que los reemplaza y NO están en esta lista.

**(1) CatalogDomains.cs**
- LookupDomains: ZoneType, DockType, DockDirection, TrackingType, InventoryTxnType, ReceiptType, WarehouseTaskType, AdjustmentReason, ShortageAction.
- StatusDomains: WarehouseStatus, DockStatus, SerialStatus, AsnStatus, ReceiptStatus, WarehouseTaskStatus, CycleCountStatus, PickBatchStatus, PurchaseOrderStatus, AppointmentStatus, CrossDockStatus, AllocationStatus.
- Clases de códigos:
  - WarehouseStatuses (ACTIVE, INACTIVE)
  - DockStatuses (FREE, OCCUPIED, MAINTENANCE)
  - SerialStatuses (AVAILABLE, RESERVED, SHIPPED, SCRAPPED)
  - AsnStatuses (EXPECTED, RECEIVED, CANCELLED)
  - ReceiptStatuses (OPEN, RECEIVED, PUTAWAY)
  - WarehouseTaskStatuses (PENDING, IN_PROGRESS, DONE, CANCELLED)
  - CycleCountStatuses (OPEN, COUNTED, RECONCILED)
  - PickBatchStatuses (COLLECTED, PACKED, CANCELLED)
  - PurchaseOrderStatuses (DRAFT, SENT, PARTIAL, RECEIVED, CANCELLED)
  - AppointmentStatuses (SCHEDULED, ARRIVED, COMPLETED, NO_SHOW, CANCELLED)
  - CrossDockStatuses (OPEN, ALLOCATED, COMPLETED)
  - AllocationStatuses (PLANNED, MOVED, CANCELLED)
  - ZoneTypes (PICKING, RESERVE, REFRIGERATED, QUARANTINE, CROSSDOCK, STAGING)
  - DockTypes (INBOUND, OUTBOUND, BOTH)
  - DockDirections (INBOUND, OUTBOUND)
  - TrackingTypes (NONE, LOT, SERIAL)
  - InventoryTxnTypes (RECEIPT, ISSUE, TRANSFER, ADJUSTMENT, CROSSDOCK)
  - ReceiptTypes (ASN, BLIND, RETURN)
  - WarehouseTaskTypes (PUTAWAY, PICK, PACK, REPLENISH, COUNT, LOAD, CROSSDOCK)
  - AdjustmentReasons (RECEIPT_VARIANCE, COUNT_VARIANCE, DAMAGE, LOSS, FOUND, EXPIRED, PO_SHORTAGE, PICK_BATCH_REVERSAL, OTHER)
  - ShortageActions (CLOSE, REORDER, MANUAL_ADJUSTMENT)
  - RotationClasses (FAST, SLOW)
- Capabilities.EditPurchaseOrder = 'EDIT_PURCHASE_ORDER'.
- EntityTypes:
  - nuevos: Receipt, Asn, WarehouseDock, InventorySerial, WarehouseTask, PickBatch, DockAppointment, CrossDockAllocation, InventoryTransaction y StockBalance;
  - constantes para códigos ya sembrados: CycleCount, CrossDockPlan, PurchaseOrder, Supplier y ReceiptLine.
- NumberKinds: RECEIPT, CYCLECOUNT, CROSSDOCK y PURCHASE.

**(2) PermissionCatalog.cs** pasa de 54 a 58 permisos.
- Nuevos, en categoría WAREHOUSE: inventory.view, inventory.manage, inventory.adjust y warehouse.manage.
- OwnerReadPermission:
  - inventory.view para WAREHOUSE, WAREHOUSE_DOCK, PRODUCT, INVENTORY_SERIAL, RECEIPT, ASN, WAREHOUSE_TASK, CYCLE_COUNT, PICK_BATCH, DOCK_APPOINTMENT, CROSSDOCK_PLAN y CROSSDOCK_ALLOCATION;
  - purchasing.view para PURCHASE_ORDER y SUPPLIER.
- OwnerWritePermission:
  - WAREHOUSE y WAREHOUSE_DOCK → warehouse.manage
  - PRODUCT → inventory.manage
  - RECEIPT y ASN → warehouse.receive
  - CYCLE_COUNT → warehouse.count
  - PICK_BATCH → warehouse.pick
  - DOCK_APPOINTMENT y CROSSDOCK_PLAN → warehouse.crossdock
  - PURCHASE_ORDER y SUPPLIER → purchasing.manage
  - INVENTORY_SERIAL, WAREHOUSE_TASK y CROSSDOCK_ALLOCATION quedan sin escritura (resolver cerrado).
- RoleTemplates: WarehouseOperator, ReadOnly y Billing reciben inventory.view. Los otros tres permisos nuevos, solo TenantAdmin.

**(3) Numeración**
- NumberingRules.IsKnownKind acepta RECEIPT, CYCLECOUNT, CROSSDOCK y PURCHASE.
- WmsNumbering (puro): REC-#####, CC-#####, XD-##### y PO-#####. La recolección usa NumberingRules.PackBatchPattern con el contador PACKBATCH (D10).

**(4) Entidades** (namespace Teikem.Domain.Wms), 1:1 con el SQL final.

Almacén y producto:
- Warehouse ([AuditEntity(WAREHOUSE)]; ITenantScoped, ISoftDeletable, IHasStatus): WarehouseId, PublicId, TenantId, Code, Name, Line1?, City?, State?, PostalCode?, CountryLookupId, StatusCodeId, IsActive, RowVersion. Navegaciones Zones, Docks y Status. GeoPoint NO se mapea.
- WarehouseZone ([AuditEntity(WAREHOUSE)]; ISoftDeletable): WarehouseZoneId, WarehouseId, Code, Name, ZoneTypeLookupId?, IsActive.
- WarehouseBin ([AuditEntity(WAREHOUSE)]): WarehouseBinId, WarehouseZoneId, WarehouseId, Code, Aisle?, Rack?, Level?, Position?, MaxWeightKg?, IsActive.
- WarehouseDock ([AuditEntity(WAREHOUSE_DOCK)]; IHasStatus): WarehouseDockId, WarehouseId, Code, DockTypeLookupId, StatusCodeId, IsActive.
- ProductCategory ([AuditEntity(PRODUCT)]; ITenantScoped, ISoftDeletable): ProductCategoryId, TenantId, ParentId?, Name, IsActive.
- Product ([AuditEntity(PRODUCT)]; ITenantScoped, ISoftDeletable): ProductId, PublicId, TenantId, ClientId? (dueño del inventario, NO el tenant), Sku, Name, ProductCategoryId?, BaseUomLookupId, TrackingTypeLookupId, WeightKg?, VolumeM3?, Barcode?, PurchaseCost?, SalePrice?, PreferredWarehouseId?, PreferredBinId?, MinQty?, MinPickQty?, MaxPickQty?, IsActive, RowVersion.
- InventoryLot ([AuditEntity(PRODUCT)]): LotId, ProductId, LotNumber, DateOnly? ManufactureDate, DateOnly? ExpiryDate, IsActive.
- InventorySerial (sin auditoría: su rastro es EntityStatusHistory más el ledger): SerialId, ProductId, LotId?, SerialNumber, int? StatusCodeId, CurrentWarehouseId?, CurrentBinId?.

Saldo y ledger:
- StockBalance (sin [AuditEntity]; ITenantScoped): StockBalanceId, TenantId, ProductId, WarehouseId, WarehouseBinId?, LotId?, QtyOnHand, QtyReserved, QtyAvailable (computada, set privado), [NotAudited] UpdatedAtUtc, RowVersion.
- InventoryTransaction (sin [AuditEntity]; solo inserción; ITenantScoped): long InventoryTransactionId, TenantId, TxnTypeLookupId, ProductId, LotId?, SerialId?, FromWarehouseId?, FromBinId?, ToWarehouseId?, ToBinId?, Quantity (CON SIGNO, D3), RefEntityLookupId?, RefId?, ReasonLookupId?, Notes?, CreatedAtUtc, CreatedBy?.

Compras:
- Supplier ([AuditEntity(SUPPLIER)]; ITenantScoped, ISoftDeletable): SupplierId, TenantId, Name, ContactName?, Phone?, Email?, PaymentTermLookupId?, Notes?, IsActive, RowVersion.
- PurchaseOrder ([AuditEntity(PURCHASE_ORDER)]; ITenantScoped, ISoftDeletable, IHasStatus): PurchaseOrderId, PublicId, TenantId, SupplierId, WarehouseId, Number, DateOnly OrderDate, DateOnly? ExpectedDate, StatusCodeId, CurrencyLookupId?, Notes?, IsActive, CreatedAtUtc, CreatedBy?, RowVersion, Lines.
- PurchaseOrderLine ([AuditEntity(PURCHASE_ORDER)]): PurchaseOrderLineId, PurchaseOrderId, ProductId, QtyOrdered, QtyReceived, UnitCost, LineTotal (computada).
- PurchaseOrderShortageResolution ([AuditEntity(PURCHASE_ORDER)]; ITenantScoped): PurchaseOrderShortageResolutionId, TenantId, PurchaseOrderId, PurchaseOrderLineId, ActionLookupId, Quantity, ReasonLookupId?, Notes?, ReorderPurchaseOrderId?, long? InventoryTransactionId, CreatedAtUtc, CreatedBy?.

Recepción:
- Asn ([AuditEntity(ASN)]; ITenantScoped, ISoftDeletable, IHasStatus): AsnId, TenantId, WarehouseId, ClientId?, PurchaseOrderId?, Reference?, DateOnly? ExpectedDate, StatusCodeId, IsActive, CreatedAtUtc, CreatedBy?, Lines.
- AsnLine ([AuditEntity(ASN)]): AsnLineId, AsnId, ProductId, ExpectedQty, LotNumber?, PurchaseOrderLineId?.
- ReceiptHeader ([AuditEntity(RECEIPT)]; ITenantScoped, ISoftDeletable, IHasStatus): ReceiptHeaderId, PublicId, TenantId, WarehouseId, AsnId?, DockId?, ReceiptTypeLookupId, Number, StatusCodeId, ReceivedAtUtc?, ReceivedBy?, IsActive, CreatedAtUtc, CreatedBy?, RowVersion, Lines.
- ReceiptLine ([AuditEntity(RECEIPT)]): ReceiptLineId, ReceiptHeaderId, AsnLineId?, ProductId, LotId?, SerialId?, ReceivedQty, ExpectedQty?, SerialNumbersJson?, StagingBinId?, long? AdjustmentTxnId.

Tareas y conteo:
- WarehouseTask (sin [AuditEntity]; ITenantScoped, IHasStatus): int WarehouseTaskId, TenantId, WarehouseId, TaskTypeLookupId, StatusCodeId, ProductId?, LotId?, SerialId?, Quantity?, FromBinId?, ToBinId?, RefEntityLookupId?, RefId?, AssignedToUserId?, Priority, CreatedAtUtc, CreatedBy?, CompletedAtUtc?.
- CycleCount ([AuditEntity(CYCLE_COUNT)]; ITenantScoped, ISoftDeletable, IHasStatus): CycleCountId, TenantId, WarehouseId, Number, StatusCodeId, CreatedAtUtc, CreatedBy?, ReconciledAtUtc?, ReconciledBy?, IsActive, RowVersion, Lines.
- CycleCountLine ([AuditEntity(CYCLE_COUNT)]): CycleCountLineId, CycleCountId, WarehouseBinId, ProductId, LotId?, SystemQty, CountedQty?, VarianceQty (computada, contra la foto), CountedSerialsJson?, ReconciledSystemQty? (saldo en mano al reconciliar), bool SystemQtyChanged, long? AdjustmentTxnId.

Recolección:
- PickBatch ([AuditEntity(PICK_BATCH)]; ITenantScoped, ISoftDeletable, IHasStatus): PickBatchId, PublicId, TenantId, WarehouseId, Number, StatusCodeId, TransportOrderId?, ClientInvoiceNumber?, CollectedAtUtc, CollectedBy?, PackedAtUtc?, PackedBy?, CancelledAtUtc?, CancelledBy?, IsActive, RowVersion, Lines.
- PickBatchLine ([AuditEntity(PICK_BATCH)]): PickBatchLineId, PickBatchId, ProductId, LotId?, SerialId?, FromBinId, Quantity, UnitCost?, long IssueTxnId, long? ReversalTxnId.

Cross-dock:
- DockAppointment ([AuditEntity(DOCK_APPOINTMENT)]; ITenantScoped, IHasStatus): DockAppointmentId, TenantId, WarehouseId, WarehouseDockId, DirectionLookupId, AsnId?, TripId?, ScheduledStartUtc, ScheduledEndUtc?, StatusCodeId, CreatedAtUtc, CreatedBy?.
- CrossDockPlan ([AuditEntity(CROSSDOCK_PLAN)]; ITenantScoped, IHasStatus): CrossDockPlanId, TenantId, WarehouseId, Number, StatusCodeId, StagingZoneId?, CreatedAtUtc, CreatedBy?, CompletedAtUtc?, Allocations.
- CrossDockAllocation ([AuditEntity(CROSSDOCK_PLAN)]; IHasStatus): CrossDockAllocationId, CrossDockPlanId, ReceiptLineId, TransportOrderId, CargoLineId?, AllocatedQty, ConfirmedQty? (cubierta al confirmar el recibo; NULL con el recibo abierto), StatusCodeId, WarehouseTaskId?, long? InventoryTransactionId, CreatedAtUtc, CreatedBy?.

PickWave, PickTask, Carton y CartonLine NO se mapean (D1).

**(5) Configuraciones**: WarehouseConfigurations.cs, InventoryConfigurations.cs y WmsDocumentConfigurations.cs.
- ToTable exacto; PublicId con NEWID().
- IsRowVersion en: Warehouse, Product, StockBalance, Supplier, PurchaseOrder, ReceiptHeader, CycleCount y PickBatch.
- Precisión: cantidades decimal(16,3), dinero decimal(18,4), pesos decimal(12,3), volumen decimal(12,4).
- DateOnly en las columnas DATE.
- Computadas stored:
  - QtyAvailable '[QtyOnHand]-[QtyReserved]';
  - LineTotal '[QtyOrdered]*[UnitCost]';
  - VarianceQty 'isnull([CountedQty],(0))-[SystemQty]'.
- Relaciones simples con NoAction; las FKs compuestas del SQL no se mapean.
- Índices espejo con su nombre y filtro:
  - UQ_Warehouse_Code
  - UQ_WarehouseZone
  - UQ_WarehouseBin
  - UQ_WarehouseBin_WhCode
  - UQ_WarehouseDock
  - UQ_Product_Sku (HasFilter(null))
  - UX_Product_Barcode ('[Barcode] IS NOT NULL AND [IsActive] = 1')
  - UX_ProductCategory_Name ('[IsActive] = 1')
  - UQ_Lot
  - UQ_Serial
  - UQ_StockBalance (HasFilter(null))
  - UQ_PurchaseOrder_Number
  - UX_Supplier_Name
  - UQ_Receipt_Number
  - UX_Receipt_Asn ('[AsnId] IS NOT NULL AND [IsActive] = 1')
  - UQ_CycleCount_Number
  - UQ_CycleCountLine (HasFilter(null))
  - UQ_PickBatch_Number
  - UX_PickBatch_Order ('[TransportOrderId] IS NOT NULL AND [IsActive] = 1')
  - UQ_CrossDockPlan_Number

**(6) TeikemDbContext**: 26 DbSets.
- Filtro de tenant automático para las 15 entidades con TenantId.
- Las 11 hijas sin TenantId solo se alcanzan por su padre; el comentario de bloque las enumera.

**(7) Contracts.** Firma posicional COMPLETA: records sealed que ninguna pieza cambia. '+Extra' significa [JsonExtensionData] IDictionary<string, JsonElement>? Extra.

WarehouseContracts:
- WarehouseCreateRequest(string? Code, string? Name, string? Line1=null, string? City=null, string? State=null, string? PostalCode=null, string? Country=null)
- WarehousePatchRequest(string? Name=null, string? Line1=null, string? City=null, string? State=null, string? PostalCode=null, string? Country=null, string? RowVersion=null) +Extra
- WarehouseDeactivateRequest(string? Comment=null, string? RowVersion=null)
- WarehouseDto(int Id, Guid PublicId, string Code, string Name, string? Line1, string? City, string? State, string? PostalCode, string CountryCode, string StatusCode, string Status, bool IsActive, int ZoneCount, int BinCount, int DockCount, decimal QtyOnHand, string RowVersion)
- WarehouseZoneRequest(string? Code, string? Name, string? ZoneType=null)
- WarehouseZonePatchRequest(string? Name=null, string? ZoneType=null) +Extra
- WarehouseZoneDto(int Id, string Code, string Name, string? ZoneTypeCode, string? ZoneType, bool IsActive, int BinCount)
- WarehouseBinRequest(int? ZoneId, string? Code=null, string? Aisle=null, string? Rack=null, string? Level=null, string? Position=null, decimal? MaxWeightKg=null)
- WarehouseBinPatchRequest(string? Aisle=null, string? Rack=null, string? Level=null, string? Position=null, decimal? MaxWeightKg=null, bool? ClearMaxWeight=null) +Extra
- WarehouseBinQuery(int? ZoneId=null, string? Search=null, bool IncludeInactive=false, bool OnlyWithStock=false)
- WarehouseBinDto(int Id, int ZoneId, string ZoneCode, string? ZoneTypeCode, string Code, string? Aisle, string? Rack, string? Level, string? Position, decimal? MaxWeightKg, bool IsActive, decimal QtyOnHand, int ProductCount)
- WarehouseDockRequest(string? Code, string? DockType)
- WarehouseDockPatchRequest(string? DockType=null) +Extra
- WarehouseDockStatusRequest(string? Status, string? Comment=null)
- WarehouseDockDto(int Id, string Code, string DockTypeCode, string DockType, string StatusCode, string Status, string? StatusColor, bool IsActive)
- WarehouseDetailDto(WarehouseDto Warehouse, IReadOnlyList<WarehouseZoneDto> Zones, IReadOnlyList<WarehouseDockDto> Docks)

ProductContracts:
- LotInput(string? Number, DateOnly? ManufactureDate=null, DateOnly? ExpiryDate=null)
- ProductCreateRequest(string? Sku, string? Name, Guid? OwnerClientPublicId=null, int? CategoryId=null, string? BaseUom=null, string? TrackingType=null, decimal? WeightKg=null, decimal? VolumeM3=null, string? Barcode=null, decimal? PurchaseCost=null, decimal? SalePrice=null, Guid? PreferredWarehousePublicId=null, int? PreferredBinId=null, decimal? MinQty=null, decimal? MinPickQty=null, decimal? MaxPickQty=null)
- ProductPatchRequest(string? Name=null, Guid? OwnerClientPublicId=null, bool? ClearOwner=null, int? CategoryId=null, bool? ClearCategory=null, string? BaseUom=null, string? TrackingType=null, decimal? WeightKg=null, decimal? VolumeM3=null, string? Barcode=null, bool? ClearBarcode=null, decimal? PurchaseCost=null, decimal? SalePrice=null, Guid? PreferredWarehousePublicId=null, int? PreferredBinId=null, bool? ClearPreferred=null, decimal? MinQty=null, decimal? MinPickQty=null, decimal? MaxPickQty=null, string? RowVersion=null) +Extra (sku)
- ProductListQuery(string? Search=null, int[]? CategoryIds=null, Guid? OwnerClientPublicId=null, bool? OwnOnly=null, bool ActiveOnly=false, Guid? WarehousePublicId=null, bool OnlyAvailable=false, int Skip=0, int Take=100)
- ProductListItemDto(int Id, Guid PublicId, string Sku, string Name, int? CategoryId, string? CategoryName, Guid? OwnerClientPublicId, string? OwnerName, bool IsOwn, string BaseUomCode, string TrackingTypeCode, string? Barcode, decimal? PurchaseCost, decimal? SalePrice, decimal QtyOnHand, decimal QtyReserved, decimal QtyAvailable, decimal? MinQty, bool IsBelowMin, bool IsActive)
- ProductPageDto(int Total, int Skip, int Take, IReadOnlyList<ProductListItemDto> Items)
- ProductDetailDto(ProductListItemDto Product, decimal? WeightKg, decimal? VolumeM3, Guid? PreferredWarehousePublicId, string? PreferredWarehouseCode, int? PreferredBinId, string? PreferredBinCode, decimal? MinPickQty, decimal? MaxPickQty, bool HasMovements, string RowVersion)
- ProductCategoryRequest(string? Name, int? ParentId=null)
- ProductCategoryPatchRequest(string? Name=null, int? ParentId=null, bool? ClearParent=null)
- ProductCategoryDto(int Id, string Name, int? ParentId, string Path, bool IsActive, int ProductCount)
- LotDto(int Id, string LotNumber, DateOnly? ManufactureDate, DateOnly? ExpiryDate, int? DaysToExpiry, decimal QtyOnHand, bool IsActive)
- SerialDto(int Id, string SerialNumber, int? LotId, string? LotNumber, string? StatusCode, string? Status, Guid? WarehousePublicId, string? WarehouseCode, int? BinId, string? BinCode)

InventoryContracts:
- InventoryScope(int? OwnerClientId), con static Any. Tipo interno de servicio, como OrderScope; nunca se deserializa (D44).
- BalanceQuery(Guid[]? WarehousePublicIds=null, int[]? BinIds=null, Guid[]? ProductPublicIds=null, int[]? CategoryIds=null, string? LotNumber=null, bool IncludeZero=false, bool OnlyAvailable=false, string? Search=null, int Skip=0, int Take=200)
- BalanceDto(int Id, Guid WarehousePublicId, string WarehouseCode, int? BinId, string? BinCode, string? ZoneCode, string? ZoneTypeCode, Guid ProductPublicId, string Sku, string ProductName, string? CategoryName, string? OwnerName, bool IsOwn, int? LotId, string? LotNumber, DateOnly? ExpiryDate, decimal QtyOnHand, decimal QtyReserved, decimal QtyAvailable, decimal? CostValue, decimal? SaleValue, DateTime UpdatedAtUtc)
- BalancePageDto(int Total, int Skip, int Take, decimal TotalOnHand, decimal TotalAvailable, IReadOnlyList<BalanceDto> Items)
- KardexQuery(DateOnly? From=null, DateOnly? To=null, string[]? Types=null, Guid[]? WarehousePublicIds=null, int[]? BinIds=null, Guid[]? ProductPublicIds=null, int[]? CategoryIds=null, string? LotNumber=null, string? SerialNumber=null, string? RefEntity=null, int? RefId=null, string? Search=null, int Skip=0, int Take=200)
- KardexRowDto(long Id, DateTime CreatedAtUtc, string TypeCode, string Type, Guid ProductPublicId, string Sku, string ProductName, decimal Quantity, decimal SignedQuantity, string? FromWarehouseCode, string? FromBinCode, string? ToWarehouseCode, string? ToBinCode, string Position, string? LotNumber, string? SerialNumber, string? RefEntityCode, int? RefId, string? RefLabel, string? ReasonCode, string? Reason, string? Notes, int? UserId, string? UserName). Quantity = valor del ledger CON signo; SignedQuantity = según la perspectiva del filtro de ubicación.
- KardexPageDto(int Total, int Skip, int Take, IReadOnlyList<KardexRowDto> Items)
- AdjustmentRequest(Guid? ProductPublicId, Guid? WarehousePublicId, int? BinId, decimal? Quantity, string? Reason, string? Notes=null, int? LotId=null, LotInput? Lot=null, IReadOnlyList<string>? SerialNumbers=null)
- TransferRequest(Guid? ProductPublicId, int? FromBinId, int? ToBinId, decimal? Quantity, Guid? FromWarehousePublicId=null, Guid? ToWarehousePublicId=null, int? LotId=null, IReadOnlyList<string>? SerialNumbers=null, string? Notes=null)
- MovementResultDto(IReadOnlyList<KardexRowDto> Transactions, IReadOnlyList<BalanceDto> Balances)
- GenealogyDestinationDto(string RefEntityCode, int RefId, string? RefLabel, Guid? OrderPublicId, string? PackBatchNumber, string? ClientName, string? ConsigneeName, decimal Quantity)
- GenealogyDto(Guid ProductPublicId, string Sku, string ProductName, int LotId, string LotNumber, DateOnly? ManufactureDate, DateOnly? ExpiryDate, decimal QtyIn, decimal QtyOut, decimal QtyOnHand, IReadOnlyList<KardexRowDto> Movements, IReadOnlyList<GenealogyDestinationDto> Destinations)
- SerialTraceDto(SerialDto Serial, Guid ProductPublicId, string Sku, IReadOnlyList<KardexRowDto> Movements, IReadOnlyList<StatusHistoryDto> StatusHistory)
- ReconciliationRowDto(Guid ProductPublicId, string Sku, string WarehouseCode, string? BinCode, string? LotNumber, decimal LedgerQty, decimal BalanceQty)
- ReconciliationDto(DateTime CheckedAtUtc, int BalancesChecked, IReadOnlyList<ReconciliationRowDto> Mismatches)

ReceivingContracts:
- AsnLineRequest(Guid? ProductPublicId, decimal? ExpectedQty, string? LotNumber=null)
- AsnCreateRequest(Guid? WarehousePublicId, Guid? ClientPublicId, string? Reference=null, DateOnly? ExpectedDate=null, IReadOnlyList<AsnLineRequest>? Lines=null)
- AsnLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, decimal ExpectedQty, string? LotNumber, int? PurchaseOrderLineId)
- AsnDto(int Id, Guid WarehousePublicId, string WarehouseCode, Guid? ClientPublicId, string? ClientName, Guid? PurchaseOrderPublicId, string? PurchaseOrderNumber, string? Reference, DateOnly? ExpectedDate, string StatusCode, string Status, bool IsActive, IReadOnlyList<AsnLineDto> Lines, Guid? ReceiptPublicId, string? ReceiptNumber)
- AsnQuery(Guid? WarehousePublicId=null, string[]? Status=null, Guid? ClientPublicId=null, string? Search=null)
- ReceiptLineRequest(Guid? ProductPublicId, decimal? ReceivedQty, LotInput? Lot=null, IReadOnlyList<string>? SerialNumbers=null, int? StagingBinId=null)
- ReceiptCreateRequest(Guid? WarehousePublicId=null, string? Type=null, int? AsnId=null, Guid? PurchaseOrderPublicId=null, int? DockId=null, int? StagingBinId=null, IReadOnlyList<ReceiptLineRequest>? Lines=null)
- ReceiptLineUpdateRequest(decimal? ReceivedQty=null, LotInput? Lot=null, bool? ClearLot=null, IReadOnlyList<string>? SerialNumbers=null, int? StagingBinId=null)
- ReceiptConfirmRequest(string? Comment=null, string? RowVersion=null)
- ReceiptQuery(Guid? WarehousePublicId=null, string[]? Status=null, string[]? Types=null, DateOnly? From=null, DateOnly? To=null, Guid[]? ProductPublicIds=null, bool? HasVariance=null, string? Search=null, int Skip=0, int Take=100)
- ReceiptListItemDto(int Id, Guid PublicId, string Number, string TypeCode, string Type, string Origin, string? OriginRef, string? SenderName, Guid WarehousePublicId, string WarehouseCode, string? DockCode, string StatusCode, string Status, int LineCount, decimal ExpectedQty, decimal ReceivedQty, decimal VarianceQty, bool HasVariance, DateTime CreatedAtUtc, DateTime? ReceivedAtUtc)
- ReceiptLineDto(int Id, int? AsnLineId, Guid ProductPublicId, string Sku, string ProductName, string TrackingTypeCode, decimal? ExpectedQty, decimal ReceivedQty, decimal VarianceQty, int? LotId, string? LotNumber, DateOnly? ExpiryDate, IReadOnlyList<string> SerialNumbers, int? StagingBinId, string? StagingBinCode, long? AdjustmentTxnId, decimal? UnitCost, decimal AllocatedToCrossDock)
- ReceiptDetailDto(ReceiptListItemDto Header, IReadOnlyList<ReceiptLineDto> Lines, IReadOnlyList<WarehouseTaskDto> PutawayTasks, string RowVersion)
- ReceiptPageDto(int Total, int Skip, int Take, IReadOnlyList<ReceiptListItemDto> Items)

WarehouseTaskContracts:
- WarehouseTaskDto(int Id, string TypeCode, string Type, string StatusCode, string Status, int Priority, Guid WarehousePublicId, string WarehouseCode, Guid? ProductPublicId, string? Sku, string? ProductName, int? LotId, string? LotNumber, string? SerialNumber, decimal? Quantity, int? FromBinId, string? FromBinCode, int? ToBinId, string? ToBinCode, string? RefEntityCode, int? RefId, string? RefLabel, int? AssignedToUserId, string? AssignedToName, bool CompletableFromQueue, DateTime CreatedAtUtc, DateTime? CompletedAtUtc)
- WarehouseTaskQuery(Guid? WarehousePublicId=null, string[]? Types=null, string[]? Status=null, bool AssignedToMe=false, int? AssignedUserId=null, bool IncludeClosed=false, int Skip=0, int Take=100)
- WarehouseTaskPageDto(int Total, int Skip, int Take, IReadOnlyList<WarehouseTaskDto> Items)
- TaskAssignRequest(int? UserId)
- TaskCompleteRequest(int? ToBinId=null, decimal? Quantity=null, IReadOnlyList<string>? SerialNumbers=null, string? Comment=null)
- TaskCancelRequest(string? Comment=null)
- ReplenishmentRunRequest(Guid? WarehousePublicId=null)
- ReplenishmentResultDto(int ProductsEvaluated, int TasksCreated, int SkippedWithOpenTask, int SkippedNoReserve, IReadOnlyList<WarehouseTaskDto> Tasks)
- PutawaySuggestionDto(int BinId, string BinCode, string ZoneCode, string? ZoneTypeCode, string ReasonCode, string Reason, string RotationClass)

CycleCountContracts:
- CycleCountCreateRequest(Guid? WarehousePublicId=null, int[]? ZoneIds=null, int[]? BinIds=null, Guid[]? ProductPublicIds=null, int[]? CategoryIds=null)
- CycleCountDto(int Id, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode, string Status, int LineCount, int CountedLines, int VarianceLines, decimal NetVariance, DateTime CreatedAtUtc, DateTime? ReconciledAtUtc, bool IsActive)
- CycleCountLineDto(int Id, int BinId, string BinCode, string ZoneCode, Guid ProductPublicId, string Sku, string ProductName, string? CategoryName, string TrackingTypeCode, int? LotId, string? LotNumber, decimal SystemQty, decimal? CountedQty, decimal? VarianceQty, IReadOnlyList<string> ExpectedSerials, IReadOnlyList<string> CountedSerials, bool IsStale, decimal CurrentQty, decimal? ReconciledSystemQty, bool SystemQtyChanged, decimal? AdjustedQty, long? AdjustmentTxnId)
- CycleCountDetailDto(CycleCountDto Count, IReadOnlyList<CycleCountLineDto> Lines, string RowVersion)
- CycleCountQuery(Guid? WarehousePublicId=null, string[]? Status=null, DateOnly? From=null, DateOnly? To=null, int[]? BinIds=null, Guid[]? ProductPublicIds=null, int[]? CategoryIds=null, string? Search=null)
- CycleCountLinesQuery(int[]? BinIds=null, Guid[]? ProductPublicIds=null, int[]? CategoryIds=null, bool? OnlyVariance=null, bool? OnlyPending=null, string? Search=null)
- CountCaptureItem(int LineId, decimal? CountedQty=null, IReadOnlyList<string>? SerialNumbers=null)
- CountCaptureRequest(IReadOnlyList<CountCaptureItem>? Lines, string? RowVersion=null)
- CountAddLineRequest(int? BinId, Guid? ProductPublicId, int? LotId=null, LotInput? Lot=null, decimal? CountedQty=null, IReadOnlyList<string>? SerialNumbers=null)
- CountReconcileRequest(string? Comment=null, string? RowVersion=null)

PickBatchContracts:
- PickBatchLineRequest(Guid? ProductPublicId, decimal? Quantity, int? BinId=null, int? LotId=null, IReadOnlyList<string>? SerialNumbers=null)
- PickBatchCreateRequest(Guid? WarehousePublicId=null, IReadOnlyList<PickBatchLineRequest>? Lines=null)
- PickBatchQuery(DateOnly? From=null, DateOnly? To=null, Guid[]? ProductPublicIds=null, string[]? Status=null, string? OrderNumber=null, string? InvoiceNumber=null, string? Search=null, bool IncludeDeleted=false, int Skip=0, int Take=100)
- PickBatchLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, decimal Quantity, int BinId, string BinCode, int? LotId, string? LotNumber, string? SerialNumber, decimal? UnitCost, long IssueTxnId, long? ReversalTxnId)
- PickBatchDto(int Id, Guid PublicId, string Number, Guid WarehousePublicId, string WarehouseCode, string StatusCode, string Status, DateTime CollectedAtUtc, string? CollectedBy, DateTime? PackedAtUtc, Guid? OrderPublicId, string? OrderNumber, string? PackBatchNumber, string? ClientInvoiceNumber, string? OrderStatusCode, string? OrderStatus, string? ClientName, string? DisplayNumbers, bool CanPack, bool CanDelete, decimal TotalQty, decimal? TotalCost, IReadOnlyList<PickBatchLineDto> Lines, bool IsActive, string RowVersion)
- PickBatchPageDto(int Total, int Skip, int Take, IReadOnlyList<PickBatchDto> Items)
- PickBatchPackRequest(OrderCreateRequest? Order, string? RowVersion=null)
- PickBatchPackResultDto(PickBatchDto Batch, OrderDetailDto Order)
- PickBatchDeleteRequest(string? Comment=null, string? RowVersion=null)

PurchasingContracts:
- SupplierRequest(string? Name, string? ContactName=null, string? Phone=null, string? Email=null, string? PaymentTerm=null, string? Notes=null)
- SupplierPatchRequest(string? Name=null, string? ContactName=null, string? Phone=null, string? Email=null, string? PaymentTerm=null, string? Notes=null, string? RowVersion=null)
- SupplierDto(int Id, string Name, string? ContactName, string? Phone, string? Email, string? PaymentTermCode, string? Notes, bool IsActive, string RowVersion)
- PurchaseOrderLineRequest(Guid? ProductPublicId, decimal? QtyOrdered, decimal? UnitCost=null)
- PurchaseOrderCreateRequest(int? SupplierId, Guid? WarehousePublicId=null, DateOnly? OrderDate=null, DateOnly? ExpectedDate=null, string? Currency=null, string? Notes=null, IReadOnlyList<PurchaseOrderLineRequest>? Lines=null)
- PurchaseOrderPatchRequest(DateOnly? ExpectedDate=null, string? Notes=null, IReadOnlyList<PurchaseOrderLineRequest>? Lines=null, string? RowVersion=null) +Extra
- PurchaseOrderStatusRequest(string? Comment=null, string? RowVersion=null)
- PurchaseOrderQuery(string[]? Status=null, int? SupplierId=null, Guid? WarehousePublicId=null, DateOnly? From=null, DateOnly? To=null, string? Search=null, int Skip=0, int Take=100)
- PurchaseOrderLineDto(int Id, Guid ProductPublicId, string Sku, string ProductName, decimal QtyOrdered, decimal QtyReceived, decimal QtyResolved, decimal QtyPending, decimal UnitCost, decimal LineTotal)
- PurchaseOrderDto(int Id, Guid PublicId, string Number, int SupplierId, string SupplierName, Guid WarehousePublicId, string WarehouseCode, DateOnly OrderDate, DateOnly? ExpectedDate, string StatusCode, string Status, string? CurrencyCode, string? Notes, decimal Total, bool HasShortage, bool CanEdit, bool CanCancel, bool CanDelete, bool IsActive, IReadOnlyList<PurchaseOrderLineDto> Lines, string RowVersion)
- PurchaseOrderPageDto(int Total, int Skip, int Take, IReadOnlyList<PurchaseOrderDto> Items)
- ShortageResolutionDto(int Id, string ActionCode, string Action, decimal Quantity, string? ReasonCode, string? Reason, string? Notes, Guid? ReorderPurchaseOrderPublicId, string? ReorderPurchaseOrderNumber, long? InventoryTransactionId, DateTime CreatedAtUtc, string? CreatedBy)
- ShortageLineDto(int PurchaseOrderLineId, Guid ProductPublicId, string Sku, string ProductName, decimal QtyOrdered, decimal QtyReceived, decimal QtyResolved, decimal QtyPending, decimal UnitCost, decimal PendingCost, IReadOnlyList<ShortageResolutionDto> Resolutions)
- PoShortageSummaryDto(Guid PublicId, string Number, int SupplierId, string SupplierName, int LinesWithShortage, decimal QtyPending, decimal PendingCost)
- ShortageResolveRequest(string? Action, decimal? Quantity=null, string? Reason=null, string? Notes=null, int? BinId=null, int? LotId=null, LotInput? Lot=null, IReadOnlyList<string>? SerialNumbers=null, string? RowVersion=null)
- ShortageResolveResultDto(ShortageLineDto Line, PurchaseOrderDto PurchaseOrder, PurchaseOrderDto? Reorder)

CrossDockContracts:
- DockAppointmentRequest(Guid? WarehousePublicId, int? DockId, string? Direction, DateTime? ScheduledStartUtc, DateTime? ScheduledEndUtc=null, int? AsnId=null, Guid? TripPublicId=null)
- DockAppointmentPatchRequest(DateTime? ScheduledStartUtc=null, DateTime? ScheduledEndUtc=null, int? DockId=null)
- DockAppointmentStatusRequest(string? Status, string? Comment=null)
- DockAppointmentQuery(Guid? WarehousePublicId=null, int? DockId=null, DateTime? FromUtc=null, DateTime? ToUtc=null, string[]? Status=null)
- DockAppointmentDto(int Id, Guid WarehousePublicId, int DockId, string DockCode, string DockTypeCode, string DockStatusCode, string DirectionCode, string Direction, DateTime ScheduledStartUtc, DateTime? ScheduledEndUtc, int? AsnId, string? AsnReference, Guid? TripPublicId, string? TripCode, string StatusCode, string Status, string? StatusColor)
- CrossDockPlanRequest(Guid? WarehousePublicId=null, int? StagingZoneId=null)
- CrossDockAllocationRequest(int? ReceiptLineId, Guid? OrderPublicId, decimal? Quantity, int? CargoLineId=null)
- CrossDockAllocationDto(int Id, int ReceiptLineId, string ReceiptNumber, string ReceiptStatusCode, Guid ProductPublicId, string Sku, string? LotNumber, Guid OrderPublicId, string PackBatchNumber, string ClientName, decimal Quantity, decimal? ConfirmedQty, decimal ShortQty, string StatusCode, string Status, int? TaskId, long? InventoryTransactionId)
- CrossDockPlanDto(int Id, string Number, Guid WarehousePublicId, string WarehouseCode, int? StagingZoneId, string? StagingZoneCode, string StatusCode, string Status, int AllocationCount, decimal AllocatedQty, decimal MovedQty, decimal ShortQty, DateTime CreatedAtUtc, DateTime? CompletedAtUtc, IReadOnlyList<CrossDockAllocationDto> Allocations)
- CrossDockCandidateDto(int ReceiptLineId, string ReceiptNumber, string ReceiptStatusCode, Guid ProductPublicId, string Sku, string? LotNumber, int? StagingBinId, string? StagingBinCode, decimal BaseQty, decimal AllocatedQty, decimal Allocatable)
- CrossDockMoveRequest(string? Comment=null)

OrderContracts.cs agrega OrderDeletionOptions(string? AllowedSourceEntityType=null), para P7. NO toca OrderDetailDto.

Ninguna solicitud lleva TenantId, InventoryScope ni ids internos de encabezados con PublicId.

**(8) Reglas puras transversales** (Domain/Wms). Los mensajes son public const o métodos estáticos, en cultura invariante.

InventoryRules:
- ValidateQuantity(decimal?):
  - 'La cantidad debe ser mayor que cero.'
  - 'La cantidad admite como máximo 3 decimales.'
  - 'La cantidad excede el máximo permitido.'
- Available(onHand, reserved).
- ValidateTracking, con los mensajes LotRequired, LotNotAllowed, SerialInteger, SerialCount y SerialNotAllowed.
- ValidatePosting(type, from, to, magnitude, reason, serial): dirección por tipo, SameBin, ReasonRequired y SerialQty.
- StoredQuantity(magnitude, hasTo) → +magnitude si hay To; −magnitude si solo From.
- Rebuild(rows): por clave, To += |Q| y From −= |Q|.
- NetByProduct(rows): Σ Quantity sin TRANSFER.
- Round4, CostValue y FormatQty.
- InsufficientStockMessage(sku, bin, disponible, solicitado) = 'Inventario insuficiente de {sku} en {bin}: disponible {x}, solicitado {y}.'
- ProductInactiveMessage y BinInactiveMessage.
- ReleaseExceedsReserved = 'La reserva a liberar excede lo reservado.'
- MaxPageSize = 200.

SerialRules:
- Normalize: trim, máximo 80 caracteres, duplicados sin distinguir mayúsculas, 500 por línea.
- Mensajes NotAvailable, AlreadyInStock y Scrapped.
- TargetStatus(txnType, sign).

StockAllocator.Allocate(qty, lotId, binId, candidatos con disponible = en mano − reservado):
- FEFO (vencimiento ascendente, NULL al final);
- luego zona PICKING < RESERVE < REFRIGERATED < STAGING;
- excluye QUARANTINE, CROSSDOCK y disponible ≤ 0;
- determinista.

PutawayRules (D24):
- Classify(issued30d, onHandTotal) → FAST si issued30d > 0 y issued30d ≥ onHandTotal; SLOW en otro caso.
- Rank(candidatos, producto, lote, qty, pesoUnitario, rotation, take). Razones en orden:
  1. PREFERRED
  2. CONSOLIDATE_LOT
  3. CONSOLIDATE
  4. PICKING_FAST 'Picking por alta rotación' (solo FAST: posiciones PICKING vacías o del mismo producto con capacidad)
  5. RESERVE_EMPTY
  6. RESERVE
- Excluye QUARANTINE, STAGING, CROSSDOCK, las inactivas y la de origen.
- REFRIGERATED solo como preferida o para consolidar.
- Capacidad por MaxWeightKg.
- Desempate por código.

PurchaseStatusRules:
- Pending(ordered, received, resolved).
- IsReceivable, con 'La orden de compra debe estar enviada o recibida parcial para recibir contra ella.'
- NothingPending = 'La orden de compra no tiene cantidades pendientes de recibir.'
- StatusAfterReceipt y StatusAfterResolution.

**(9) Costuras IMPLEMENTADAS**

Wms/InventoryQueries.cs: ÚNICO lugar con SQL crudo; 17 sentencias con 'TenantId = {tenantId}':
- (1) UpsertBalance, con UPDLOCK y HOLDLOCK.
- (2) LockBalance: 'SELECT * FROM dbo.StockBalance WITH (UPDLOCK, ROWLOCK)', tracked.
- (3-5) Rangos por producto, almacén y posición, con HOLDLOCK.
- (6-14) Encabezados PickBatch, CrossDockPlan, CycleCount, ReceiptHeader, Asn, PurchaseOrder, Product, Warehouse y WarehouseTask ('SELECT {Pk} AS Value …', después EF tracked).
- (15) Muelle, con JOIN al almacén.
- (16) Series con OPENJSON, en orden ascendente.
- (17) EnsureLot: si las fechas difieren → 409 'El lote {n} ya existe con otras fechas; corrija las fechas o use otro número de lote.'
- Todos exigen transacción con proveedor relacional; con InMemory cargan tracked sin bloqueo.
- La cabecera documenta el ORDEN DE BLOQUEO del lote, con la excepción de PACKBATCH en Recolectar (D48) y la regla de escritores de CrossDockAllocation.

Wms/InventoryLedger.cs (scoped; ctor TeikemDbContext, ITenantContext, ILookupCache, StatusService). Única vía de escritura.
- PostAsync(IReadOnlyList<InventoryPosting>, ct) → ids en el orden de entrada. Los pasos:
  1. ValidatePosting sobre la magnitud (400).
  2. Exige transacción.
  3. Claves de saldo ordenadas: Upsert + Lock.
  4. Después de bloquear, valida producto y posición/almacén activos.
  5. EnsureSerials, con bloqueo ascendente.
  6. Deltas. Una salida sin FromReserved exige disponible ≥ qty. Con FromReserved exige QtyReserved ≥ qty y baja reservado y en mano juntos. Si no alcanza → InsufficientStockException (409 'insufficient_stock', con Errors por posting).
  7. Series vía TransitionAsync (INVENTORY_SERIAL), con CurrentWarehouseId/CurrentBinId.
  8. Inserta InventoryTransaction con Quantity = StoredQuantity (con signo), CreatedBy, ReasonLookupId y RefEntity/RefId TAL COMO LLEGAN. No hay SetRef ni UPDATE posterior.
  9. SaveChanges: 547 → InsufficientStockException; violación de único → 409.
- ReserveAsync(IReadOnlyList<StockReservation>, ct): orden de clave, LockBalance y disponible ≥ qty (si no, 409 insufficient_stock); QtyReserved += qty. No escribe movimiento: reservar no mueve inventario.
- ReleaseAsync(IReadOnlyList<StockReservation>, ct): QtyReserved −= qty; si queda < 0 → 409 ReleaseExceedsReserved.
- ReconcileAsync(int? productId, ct): Rebuild y NetByProduct contra StockBalance.

Wms/WmsResolve.cs: ResolveWarehouseAsync, ResolveWarehouseOrDefaultAsync, ResolveZoneAsync, ResolveBinAsync, ResolveBinByCodeAsync, ResolveDockAsync, ResolveProductAsync(Guid, requireActive, InventoryScope? scope=null), ResolveLotAsync y TrackingOfAsync. Mensajes 404/422/400 como en el plan ganador ('Almacén no encontrado.', 'Indique el almacén: la compañía tiene más de uno.', 'Posición no encontrada.', etc.). Con un scope de dueño, un producto de otro dueño → 404.

Wms/WarehouseTaskWriter.cs:
- CreateAsync(WarehouseTaskSpec), con historial null→PENDING.
- AdvanceAsync (escalonado).
- CancelAsync.
- ReduceAsync (cancela si llega a 0).

Wms/PutawaySuggester.cs: SuggestAsync(warehouseId, productId, lotId, qty, excludeBinId, take=3).
- Carga posiciones, zonas, saldos y pesos por lotes.
- Calcula issued30d con UNA consulta (−Σ Quantity de ISSUE y CROSSDOCK del producto en los últimos 30 días) y onHandTotal.
- Aplica Classify y Rank.

Wms/WmsExceptions.cs: InsufficientStockException : TeikemException(409, 'insufficient_stock').

Wms/WmsSeams.cs (tipos que no son HTTP):
- InventoryPosting(string TxnType, int ProductId, decimal Quantity, int? LotId=null, int? SerialId=null, string? SerialNumber=null, int? FromWarehouseId=null, int? FromBinId=null, int? ToWarehouseId=null, int? ToBinId=null, string? RefEntityType=null, int? RefId=null, string? ReasonCode=null, string? Notes=null, bool FromReserved=false). Quantity es la magnitud.
- StockReservation(int ProductId, int WarehouseId, int BinId, int? LotId, decimal Quantity).
- BalanceKey, ReconciliationRow, WarehouseTaskSpec y PutawaySuggestion(…, string RotationClass).
- IWarehouseTaskHandler:
  - string TaskType { get; }
  - string RequiredPermission { get; }
  - string? NotFromQueueMessage { get; }
  - Task LockReferencesAsync(WarehouseTask snapshot, CancellationToken ct)
  - Task<decimal> CompleteAsync(WarehouseTask task, TaskCompleteRequest req, CancellationToken ct) → cantidad completada. Hace el movimiento y los efectos; WarehouseTaskService hace el split y el DONE.
- IPurchaseOrderReceiving:
  - Task<PurchaseOrderForReceipt> LockForReceiptAsync(Guid purchaseOrderPublicId, CancellationToken ct): bloquea la PO, valida IsReceivable y NothingPending, devuelve las líneas con pendiente.
  - Task ApplyReceiptAsync(int purchaseOrderId, IReadOnlyList<PurchaseOrderReceiptQty> quantities, CancellationToken ct): bloquea la PO, QtyReceived +=, y transición PARTIAL/RECEIVED.
  - Tipos: PurchaseOrderForReceipt(int PurchaseOrderId, string Number, int WarehouseId, IReadOnlyList<PurchaseOrderPendingLine> Lines), PurchaseOrderPendingLine(int PurchaseOrderLineId, int ProductId, decimal QtyPending, decimal UnitCost) y PurchaseOrderReceiptQty(int PurchaseOrderLineId, decimal Quantity).
- IReceiptConfirmationParticipant: Task<IReadOnlyDictionary<int, decimal>> OnReceiptConfirmedAsync(ReceiptHeader receipt, IReadOnlyList<ReceiptLine> lines, CancellationToken ct). Devuelve la cantidad por ReceiptLineId que se va a cruce de muelle. Corre dentro de la transacción de confirmación, después del ledger y antes de las PUTAWAY.
- IOrderInventoryLines: Task<IReadOnlyDictionary<int, IReadOnlyList<OrderInventoryLine>>> GetAsync(IReadOnlyCollection<int> transportOrderIds, CancellationToken ct).
  - OrderInventoryLine(int TransportOrderId, int PickBatchId, string PickBatchNumber, int ProductId, Guid ProductPublicId, string Sku, string ProductName, int? LotId, string? LotNumber, int? SerialId, string? SerialNumber, decimal Quantity, decimal? UnitCost, decimal? SalePrice).

Services/WmsOwnedEntityResolvers.cs: 11 resolvers reales y 3 ClosedOwnedEntityResolver.

**(10) DI**, bloque '// Lote 6':
- InventoryLedger, WarehouseTaskWriter, PutawaySuggester.
- Servicios de P1 a P9.
- IWarehouseTaskHandler: PutawayTaskHandler, ReplenishTaskHandler, CountTaskHandler y CrossDockTaskHandler.
- IPurchaseOrderReceiving → PurchaseOrderReceivingService.
- IReceiptConfirmationParticipant → CrossDockReceiptParticipant.
- IOrderInventoryLines → OrderInventoryLinesProvider.
- IStatusTransitionEffect: WarehouseTaskStatusEffect y DockAppointmentStatusEffect (dependencias perezosas).
- IDataSource: las 7 fuentes.
- IOwnedEntityResolver: 11 reales y 3 cerrados.

**(11) SystemAnalyticsSeeder**, bloque '// Lote 6', módulo WAREHOUSE. El helper Chart gana field, fn, isMoney y module opcionales.
- Vistas:
  - 'Inventario' (STOCK_BALANCE)
  - 'Inventario bajo mínimo' (PRODUCT, filtro IsBelowMin = true, como el mock)
  - 'Productos por cliente dueño' (PRODUCT, filtro IsActive = true, columnas OwnerName, Sku, Name, QtyOnHand y QtyAvailable, orden OwnerName)
  - 'Kárdex de movimientos' (INVENTORY_TRANSACTION, CreatedAtUtc desc)
  - 'Movimientos por tipo' (INVENTORY_TRANSACTION, GroupJson {by:[TxnType], aggregates:[COUNT, SUM Quantity], totals:true}; L874)
  - 'Ajustes de inventario' (TxnTypeCode = ADJUSTMENT)
  - 'Próximos a vencer'
  - 'Recepciones con diferencia'
- Indicadores (90-96): 'Productos activos', 'Inventario disponible', 'Movimientos registrados', 'Valor de inventario a costo', 'Productos bajo mínimo', 'Valor de inventario a venta' y 'Tareas de almacén pendientes'.
- Gráficos: 'Valor de inventario por categoría', 'Disponible por categoría', 'Movimientos por tipo' (donut, LAST30), 'Movimientos por usuario', 'Productos por categoría' y 'Movimientos por día'.

**(12) Fuentes de datos**: los esqueletos llevan Fields COMPLETOS; AnalyticsSeedFieldsTests los verifica.
- WAREHOUSE: Id, PublicId, Code, Name, City, Status, StatusCode, IsActive, ZoneCount, BinCount, ActiveBinCount, DockCount, QtyOnHand.
- PRODUCT: Id, PublicId, Sku, Name, CategoryId, Category, OwnerClientId, OwnerName, IsOwn, BaseUom, TrackingType, Barcode, PurchaseCost$, SalePrice$, QtyOnHand, QtyReserved, QtyAvailable, CostValue$, SaleValue$, MinQty, IsBelowMin, IsActive.
- STOCK_BALANCE: Id, WarehouseId, WarehouseCode, ZoneCode, ZoneType, BinCode, ProductId, Sku, ProductName, Category, OwnerName, IsOwn, LotNumber, ExpiryDate, DaysToExpiry, QtyOnHand, QtyReserved, QtyAvailable, CostValue$, SaleValue$, UpdatedAtUtc.
- INVENTORY_TRANSACTION (DateField CreatedAtUtc): Id, CreatedAtUtc, Date, TxnType, TxnTypeCode, ProductId, Sku, ProductName, Category, Quantity (con signo), SignedQuantity, FromWarehouse, FromBin, ToWarehouse, ToBin, Position, LotNumber, SerialNumber, RefEntity, RefId, RefLabel, Reason, ReasonCode, UserName.
- RECEIPT (DateField ReceivedAtUtc): Id, PublicId, Number, Type, TypeCode, Origin, WarehouseCode, SupplierName, ClientName, PurchaseOrderNumber, Status, StatusCode, LineCount, ExpectedQty, ReceivedQty, VarianceQty, HasVariance, ReceivedCost$, CreatedAtUtc, ReceivedAtUtc.
- WAREHOUSE_TASK (DateField CreatedAtUtc): Id, CreatedAtUtc, Type, TypeCode, Status, StatusCode, Priority, WarehouseCode, Sku, Quantity, FromBin, ToBin, AssignedTo, CompletedAtUtc, AgeHours, RefLabel.
- PICK_BATCH (DateField CollectedAtUtc): Id, PublicId, Number, CollectedAtUtc, Status, StatusCode, WarehouseCode, LineCount, TotalQty, TotalCost$, PackBatchNumber, OrderNumber, ClientInvoiceNumber, ClientName, PackedAtUtc, IsActive.

**(13) Esqueletos compilables**, con firma pública completa. El cuerpo es NotImplementedException, SALVO CrossDockReceiptParticipant (diccionario vacío) y OrderInventoryLinesProvider (vacío), que otra pieza consume.
- P1: WarehouseService, WarehouseLayoutService, WarehouseDataSource.
- P2: ProductService, ProductCategoryService, ProductDataSource.
- P3: InventoryReadService, InventoryAdjustmentService, TraceabilityService, StockBalanceDataSource, InventoryTransactionDataSource.
- P4: AsnService, ReceiptService, ReceiptDataSource.
- P5: WarehouseTaskService, ReplenishmentService, WarehouseTaskStatusEffect, WarehouseTaskDataSource, PutawayTaskHandler, ReplenishTaskHandler.
- P6: CycleCountService, CountTaskHandler.
- P7: PickBatchService, PickBatchDataSource, OrderInventoryLinesProvider.
- P8: SupplierService, PurchaseOrderService, PurchaseShortageService, PurchaseOrderReceivingService.
- P9: DockAppointmentService, CrossDockService, DockAppointmentStatusEffect, CrossDockTaskHandler, CrossDockReceiptParticipant.

**(14) DemoTenantSeeder.cs** (D49): después de aprovisionar o verificar el tenant demo, y solo si tiene WMS_LOTSERIAL, siembra de forma idempotente por código:
- almacén ALM-01 'Almacén principal' (PR), ACTIVE con historial WAREHOUSE vía StatusService;
- zonas STG (STAGING, posición STG-01), PCK (PICKING: A01-R01-N1-P01 a P04), RSV (RESERVE: B01-R01-N1-P01 a P04) y QUA (QUARANTINE: Q-01);
- muelles D1 INBOUND y D2 OUTBOUND, FREE con historial.
El ctor gana StatusService. No siembra productos ni inventario, así que la conciliación queda en cero.

**(15) Pruebas de P0.** Ver 'pruebas'. WmsFixture (InMemory, al estilo de TripServiceFixture) siembra lookups, estatus con StageKind, capacidades, un tenant o dos, y builders de almacén, zonas, posiciones y productos, con StatusService, InventoryLedger y WarehouseTaskWriter reales. Lo usan las pruebas de servicio de P3 a P9.

Archivos:

- `Diseño/logistica-db-estructura.sql`
- `Diseño/logistica-db-seed.sql`
- `src/Teikem.Domain/Constants/CatalogDomains.cs`
- `src/Teikem.Domain/Constants/PermissionCatalog.cs`
- `src/Teikem.Domain/Orders/NumberingRules.cs`
- `src/Teikem.Domain/Wms/Warehouse.cs`
- `src/Teikem.Domain/Wms/Product.cs`
- `src/Teikem.Domain/Wms/Stock.cs`
- `src/Teikem.Domain/Wms/Purchasing.cs`
- `src/Teikem.Domain/Wms/Receiving.cs`
- `src/Teikem.Domain/Wms/WarehouseTask.cs`
- `src/Teikem.Domain/Wms/CycleCount.cs`
- `src/Teikem.Domain/Wms/PickBatch.cs`
- `src/Teikem.Domain/Wms/CrossDock.cs`
- `src/Teikem.Domain/Wms/InventoryRules.cs`
- `src/Teikem.Domain/Wms/StockAllocator.cs`
- `src/Teikem.Domain/Wms/SerialRules.cs`
- `src/Teikem.Domain/Wms/PutawayRules.cs`
- `src/Teikem.Domain/Wms/PurchaseStatusRules.cs`
- `src/Teikem.Domain/Wms/WmsNumbering.cs`
- `src/Teikem.Infrastructure/Persistence/Configurations/WarehouseConfigurations.cs`
- `src/Teikem.Infrastructure/Persistence/Configurations/InventoryConfigurations.cs`
- `src/Teikem.Infrastructure/Persistence/Configurations/WmsDocumentConfigurations.cs`
- `src/Teikem.Infrastructure/Persistence/TeikemDbContext.cs`
- `src/Teikem.Infrastructure/DependencyInjection.cs`
- `src/Teikem.Infrastructure/Seeding/SystemAnalyticsSeeder.cs`
- `src/Teikem.Infrastructure/Seeding/DemoTenantSeeder.cs`
- `src/Teikem.Infrastructure/Contracts/WarehouseContracts.cs`
- `src/Teikem.Infrastructure/Contracts/ProductContracts.cs`
- `src/Teikem.Infrastructure/Contracts/InventoryContracts.cs`
- `src/Teikem.Infrastructure/Contracts/ReceivingContracts.cs`
- `src/Teikem.Infrastructure/Contracts/WarehouseTaskContracts.cs`
- `src/Teikem.Infrastructure/Contracts/CycleCountContracts.cs`
- `src/Teikem.Infrastructure/Contracts/PickBatchContracts.cs`
- `src/Teikem.Infrastructure/Contracts/PurchasingContracts.cs`
- `src/Teikem.Infrastructure/Contracts/CrossDockContracts.cs`
- `src/Teikem.Infrastructure/Contracts/OrderContracts.cs`
- `src/Teikem.Infrastructure/Wms/InventoryQueries.cs`
- `src/Teikem.Infrastructure/Wms/InventoryLedger.cs`
- `src/Teikem.Infrastructure/Wms/WmsResolve.cs`
- `src/Teikem.Infrastructure/Wms/WarehouseTaskWriter.cs`
- `src/Teikem.Infrastructure/Wms/PutawaySuggester.cs`
- `src/Teikem.Infrastructure/Wms/WmsExceptions.cs`
- `src/Teikem.Infrastructure/Wms/WmsSeams.cs`
- `src/Teikem.Infrastructure/Services/WmsOwnedEntityResolvers.cs`
- `tests/Teikem.Tests/WmsFixture.cs`
- `tests/Teikem.Tests/WmsCatalogTests.cs`
- `tests/Teikem.Tests/WmsContractsTests.cs`
- `tests/Teikem.Tests/InventoryRulesTests.cs`
- `tests/Teikem.Tests/StockAllocatorTests.cs`
- `tests/Teikem.Tests/SerialRulesTests.cs`
- `tests/Teikem.Tests/PutawayRulesTests.cs`
- `tests/Teikem.Tests/PurchaseStatusRulesTests.cs`
- `tests/Teikem.Tests/InventoryLedgerTests.cs`
- `tests/Teikem.Tests/WmsWriteConfinementTests.cs`
- `tests/Teikem.Tests/WmsOwnedEntityResolversTests.cs`
- `tests/Teikem.Tests/TenantIsolationModelTests.cs`
- `tests/Teikem.Tests/RawSqlConfinementTests.cs`
- `tests/Teikem.Tests/OwnedEntityResolverCoverageTests.cs`
- `tests/Teikem.Tests/AnalyticsSeedFieldsTests.cs`
- `tests/Teikem.Tests/FleetCatalogTests.cs`
- `tests/Teikem.Tests/OrderCatalogTests.cs`
- `tests/Teikem.Tests/TripCatalogTests.cs`

### P1 — Almacenes y ubicaciones: almacén (alta, edición, baja definitiva vigilada, almacén por defecto), zonas tipadas, posiciones pasillo-rack-nivel-posición únicas por almacén, muelles con estatus, fuente WAREHOUSE

- Toca archivos compartidos: no. Depende de: P0.

Pantalla de mantenimiento de almacenes (R6) y jerarquía física (R2, R3). Sin cambios de fondo respecto del plan ganador.

**WarehouseRules (puro)**
- NormalizeCode: 'El código solo admite letras, números, guion y guion bajo (máximo 30).'
- ComposeBinCode(aisle, rack, level, position) → 'A01-R02-N3-P04', con máximo 40. Sin código ni partes → 'Indique el código de la posición o su pasillo/rack/nivel/posición.'
- Mensajes de duplicado:
  - 'Ya existe un almacén con ese código.'
  - 'Ya existe una zona con ese código en el almacén.'
  - 'Ya existe una posición con ese código en el almacén.'
  - 'Ya existe un muelle con ese código en el almacén.'
- WarehouseNotEmpty(code) = 'El almacén {code} tiene inventario o documentos abiertos; no se puede dar de baja.' (Errors por tipo de documento).
- BinNotEmpty(code) = 'La posición {code} tiene inventario; no se puede desactivar.'
- ZoneHasActiveBins = 'La zona tiene posiciones activas; desactívelas primero.'
- DockHasAppointments = 'El muelle tiene citas agendadas o en curso.'
- MaxWeight = 'La capacidad de peso debe ser mayor que cero.'
- ManualDockStatuses = {FREE, OCCUPIED, MAINTENANCE}.
- DefaultWarehouse(activos).

**WarehouseService**
- ListAsync(bool includeInactive).
- GetAsync(Guid) → WarehouseDetailDto.
- CreateAsync: código único (409); país default 'PR'; historial null→ACTIVE.
- UpdateAsync: code en Extra → 400 'El código del almacén no se puede cambiar.'; rowVersion.
- DeactivateAsync:
  1. LockHeader Warehouse.
  2. Rango de saldos HOLDLOCK.
  3. 409 si hay QtyOnHand ≠ 0 o QtyReserved ≠ 0, o documentos abiertos: recibos OPEN, conteos OPEN/COUNTED, tareas abiertas, recolecciones COLLECTED, planes OPEN/ALLOCATED o citas SCHEDULED/ARRIVED.
  4. ACTIVE→INACTIVE (terminal) + IsActive=0.

**WarehouseLayoutService**
- Zonas: List/Create/Update/SetActive; ZoneType por catálogo (400 'Tipo de zona desconocido: '{x}'.').
- Posiciones:
  - ListBinsAsync sin N+1;
  - CreateBin con código compuesto y WarehouseId de la zona (409 si el código se repite en el almacén);
  - UpdateBin: código y zona inmutables;
  - SetBinActive(false): bloquea el almacén y el rango por posición; 409 con inventario o tareas abiertas que la usan.
- Muelles:
  - List/Create/Update/SetActive; nacen FREE con historial WAREHOUSE_DOCK;
  - SetDockStatusAsync manual por StatusService;
  - no se desactivan con citas SCHEDULED/ARRIVED.
- Toda hija se resuelve por WmsResolve.

**WarehousesController** `api/v1/warehouses`: [Authorize], [RequireModule(WMS_LOTSERIAL)].
- inventory.view: GET, GET {publicId}, GET {publicId}/zones, GET {publicId}/bins, GET {publicId}/docks.
- warehouse.manage: el resto de las rutas (alta, edición, baja, zonas, posiciones, muelles, estatus de muelle y des/reactivación).

**WarehouseDataSource**: LoadAsync con q.Ids y tope de filas.

**Pruebas**: WarehouseRulesTests.

Archivos:

- `src/Teikem.Domain/Wms/WarehouseRules.cs`
- `src/Teikem.Infrastructure/Services/WarehouseService.cs`
- `src/Teikem.Infrastructure/Services/WarehouseLayoutService.cs`
- `src/Teikem.Infrastructure/Analytics/WarehouseDataSource.cs`
- `src/Teikem.Api/Controllers/WarehousesController.cs`
- `tests/Teikem.Tests/WarehouseRulesTests.cs`

### P2 — Productos y categorías: maestro de SKU propio o 3PL (dueño), seguimiento, costos y precios, mínimos, desactivación vigilada e inmutables tras movimiento; categorías jerárquicas sin ciclos; lotes y series por producto con InventoryScope; fuente PRODUCT

- Toca archivos compartidos: no. Depende de: P0.

Maestro de productos (R23, R24, R26, R31, R32).

**ProductRules (puro)**
- NormalizeSku.
- Money(value, campo): 'El costo y el precio no pueden ser negativos.' y 'El {campo} admite como máximo 4 decimales.'
- Mínimos:
  - 'El máximo de la posición de picking debe ser mayor o igual al mínimo.'
  - 'El mínimo de picking requiere una posición preferida en una zona PICKING.'
- Immutable(campo) = 'No se puede cambiar {campo} de un producto que ya tiene movimientos.'
- DeactivateWithStock(sku, qty) = 'El producto {sku} tiene inventario en mano ({qty}); no se puede desactivar.'
- DeactivateOpenDocs(sku) = 'El producto {sku} está en recibos abiertos o tareas pendientes; ciérrelos antes de desactivarlo.'
- SkuTaken = 'Ya existe un producto con ese SKU para ese dueño.'
- BarcodeTaken = 'Ya existe un producto activo con ese código de barras.'
- PreferredBinMismatch = 'La posición preferida debe pertenecer al almacén preferido.'
- Categorías: CategoryCycle = 'Una categoría no puede ser su propia ascendente.'; CategoryDepth (máximo 5 niveles); CategoryPath.

**ProductService**
Todas las lecturas reciben InventoryScope (D44): con OwnerClientId, solo los productos de ese dueño; los demás dan 404, sin oráculo.
- ListAsync(ProductListQuery, InventoryScope) → ProductPageDto: totales agrupados sin N+1; OnlyAvailable para el selector de recolección (R37); ActiveOnly para los selectores (R24); dueño = cliente o 'Propio' (R31).
- GetAsync(Guid, InventoryScope) → ProductDetailDto.
- ListLotsAsync(Guid, InventoryScope).
- ListSerialsAsync(Guid, string? status, string? search, InventoryScope).
- CreateAsync:
  - dueño por ClientQueries.ResolveClientAsync;
  - UoM y seguimiento por catálogo;
  - preferidos por WmsResolve;
  - 409 SkuTaken y 409 BarcodeTaken.
- UpdateAsync: sku en Extra → 400; seguimiento, UoM o dueño con movimientos → 409 Immutable (LockHeader Product y verificación dentro de la transacción).
- DeactivateAsync, en orden obligatorio:
  1. LockHeader Product.
  2. Rango de saldos HOLDLOCK.
  3. Σ|QtyOnHand| > 0 → 409.
  4. Recibos OPEN o tareas abiertas → 409.
  5. IsActive = 0.
- ReactivateAsync.

**ProductCategoryService**
- Padre del mismo tenant.
- Ciclo o profundidad → 400.
- 409 'Ya existe una categoría con ese nombre en ese nivel.'
- 409 'La categoría tiene productos activos.'

**Controladores** [RequireModule(WMS_LOTSERIAL)]; pasan InventoryScope.Any.
- ProductsController `api/v1/products`:
  - inventory.view: GET, GET {publicId}, GET {publicId}/lots, GET {publicId}/serials.
  - inventory.manage: POST, PATCH {publicId}, POST {publicId}/deactivate, POST {publicId}/reactivate.
- ProductCategoriesController `api/v1/product-categories`:
  - inventory.view: GET.
  - inventory.manage: POST, PATCH {id}, POST {id}/deactivate|reactivate.

**ProductDataSource**
- CostValue = Round4(QtyOnHand × PurchaseCost).
- IsBelowMin = MinQty != null && disponible < MinQty && IsActive.

**Pruebas**: ProductRulesTests.

Archivos:

- `src/Teikem.Domain/Wms/ProductRules.cs`
- `src/Teikem.Infrastructure/Services/ProductService.cs`
- `src/Teikem.Infrastructure/Services/ProductCategoryService.cs`
- `src/Teikem.Infrastructure/Analytics/ProductDataSource.cs`
- `src/Teikem.Api/Controllers/ProductsController.cs`
- `src/Teikem.Api/Controllers/ProductCategoriesController.cs`
- `tests/Teikem.Tests/ProductRulesTests.cs`

### P3 — Inventario: saldos con filtros, Kárdex de solo lectura con signo del ledger y origen legible, ajuste manual ± con motivo, transferencias, genealogía de lote, rastro de serie y conciliación; lecturas con InventoryScope; fuentes STOCK_BALANCE e INVENTORY_TRANSACTION; pruebas de servicio InMemory

- Toca archivos compartidos: no. Depende de: P0.

Inventario, Kárdex y trazabilidad (R1, R25, R27-R29, R32, R33).

**KardexRules (puro)**
- SignedQuantity(storedQuantity, type, from, to, filtro):
  - sin filtro de ubicación: la cantidad del ledger tal cual (RECEIPT +, ISSUE −, CROSSDOCK −, ADJUSTMENT con su signo), salvo TRANSFER = 0;
  - con filtro de almacenes o posiciones: salida del filtro −|Q|, entrada +|Q|, interna 0.
- Position(row): 'ALM-01/A01-R01-N1-P01', 'A → B' o '—'.
- RefLabel por EntityType, con fallback 'TIPO·id'.
- TypeChip: Recepción, Despacho, Transferencia, Ajuste o Cruce de muelle.

**AdjustmentRules (puro)**
- ToPosting(qty con signo): > 0 → To; < 0 → From; 0 → 400 'La cantidad del ajuste no puede ser cero.' El ledger recibe la magnitud y guarda el signo.
- Motivo obligatorio del catálogo. RECEIPT_VARIANCE, COUNT_VARIANCE y PICK_BATCH_REVERSAL los asigna el sistema: 400 'El motivo {x} lo asigna el sistema.'
- Reglas de series.
- Transferencia a la misma posición → 400.

**InventoryReadService** (lecturas con InventoryScope)
- BalancesAsync(BalanceQuery, InventoryScope): filtros multi, con subcategorías; IncludeZero; disponible calculado en código; Round4.
- KardexAsync(KardexQuery, InventoryScope): Desde/Hasta en UTC (hasta exclusivo +1 día), Tipo y filtros; RefLabel y usuario por lote de consultas; orden CreatedAtUtc desc, Id desc.

**InventoryAdjustmentService** (escritura interna, sin scope)
- AdjustAsync(AdjustmentRequest): RunInTransactionAsync; resuelve almacén o default, posición, producto (activo si es entrada) y lote (EnsureLot si es entrada); ledger ADJUSTMENT con ReasonCode.
- TransferAsync(TransferRequest): una fila TRANSFER, incluso entre almacenes (D40). Respeta lo reservado: no transfiere más que el disponible.

**TraceabilityService**
- LotGenealogyAsync(int lotId, InventoryScope): lote vía Product filtrado. Devuelve movimientos, QtyIn/QtyOut/QtyOnHand y destinos:
  - ISSUE con Ref PICK_BATCH → su orden;
  - TRANSPORT_ORDER → la orden;
  - CROSSDOCK → la orden de la asignación.
  Se arma por LINQ con el filtro de tenant (D32). La vista SQL ampliada queda para BI.
- SerialTraceAsync(Guid productPublicId, string serialNumber, InventoryScope): movimientos más historial INVENTORY_SERIAL.
- ReconcileAsync(Guid? productPublicId) → ReconciliationDto.

**InventoryController** `api/v1/inventory`, [RequireModule(WMS_LOTSERIAL)], InventoryScope.Any:
- GET balances (inventory.view)
- GET transactions (inventory.view)
- POST adjustments (inventory.adjust)
- POST transfers (inventory.adjust)
- GET lots/{lotId:int}/genealogy (inventory.view)
- GET serials/trace (inventory.view)
- GET reconciliation (inventory.adjust)

**Fuentes**
- StockBalanceDataSource.
- InventoryTransactionDataSource: Quantity con signo, SignedQuantity y Date.
- Ambas con tope MaxRows y respetan q.Ids y el rango.

**Pruebas**: KardexRulesTests y AdjustmentRulesTests (puras), más InventoryAdjustmentServiceTests e InventoryReadServiceTests (InMemory con WmsFixture).

Archivos:

- `src/Teikem.Domain/Wms/KardexRules.cs`
- `src/Teikem.Domain/Wms/AdjustmentRules.cs`
- `src/Teikem.Infrastructure/Services/InventoryReadService.cs`
- `src/Teikem.Infrastructure/Services/InventoryAdjustmentService.cs`
- `src/Teikem.Infrastructure/Services/TraceabilityService.cs`
- `src/Teikem.Infrastructure/Analytics/StockBalanceDataSource.cs`
- `src/Teikem.Infrastructure/Analytics/InventoryTransactionDataSource.cs`
- `src/Teikem.Api/Controllers/InventoryController.cs`
- `tests/Teikem.Tests/KardexRulesTests.cs`
- `tests/Teikem.Tests/AdjustmentRulesTests.cs`
- `tests/Teikem.Tests/InventoryAdjustmentServiceTests.cs`
- `tests/Teikem.Tests/InventoryReadServiceTests.cs`

### P4 — Recepción: ASN de cliente, recibo contra ASN, contra PO (vía IPurchaseOrderReceiving), ciego y de devolución; cantidad recibida precargada; lote con fechas y series; confirmación al ledger con ajuste de varianza; reparto de cross-dock (IReceiptConfirmationParticipant); putaway del remanente; fuente RECEIPT; pruebas de servicio InMemory

- Toca archivos compartidos: no. Depende de: P0.

Recepción (R7, R8, R9, R10 para crear el putaway, R18). El recibo contra PO alimenta R14.

**ReceiptPostingRules (puro)**
Plan(expected?, received, tracking, serials[]) → asientos en MAGNITUD; el ledger pone el signo.
- BLIND/RETURN → RECEIPT por lo recibido.
- Con esperado (D4) → RECEIPT por lo esperado y ADJUSTMENT RECEIPT_VARIANCE por la diferencia, de entrada o de salida.
- Línea extra → solo ADJUSTMENT de entrada.
- Matriz SERIAL como en el plan ganador.
- Recibido 0 → RECEIPT E y ADJUSTMENT de salida E.
- Invariante: el neto CON SIGNO es igual a lo recibido.

**ReceiptRules (puro)**
- ReceiptNotOpen(n) = 'El recibo {n} ya fue confirmado; no se puede modificar.'
- AsnBusy = 'El aviso de llegada ya tiene un recibo abierto o confirmado.'
- AsnNotExpected = 'El aviso de llegada no está pendiente de recibir.'
- NoStagingBin = 'El almacén no tiene una posición de recepción (zona STAGING); indíquela.'
- StagingMustBeStaging = 'La posición de recepción debe estar en una zona STAGING o CROSSDOCK.'
- OwnerMismatch(sku) = 'El producto {sku} no pertenece al cliente del aviso de llegada.'
- OwnProductsOnly(sku) = 'La orden de compra solo admite productos propios; {sku} pertenece a un cliente.'
- AsnLineNotRemovable = 'Las líneas del aviso de llegada no se eliminan; capture 0 como recibido.'
- LineHasCrossDock = 'La línea tiene asignaciones de cruce de muelle; cancélelas antes de eliminarla.'
- MaxLines 200.
- TypeFor(origin).

**AsnService**
- ListAsync(AsnQuery, InventoryScope) y GetAsync(int, InventoryScope): con scope, solo los ASN del cliente.
- CreateAsync: ASN de cliente, con productos activos del mismo dueño; nace EXPECTED.
- CancelAsync(int).
- CreateForPurchaseOrderAsync(PurchaseOrderForReceipt, ct): uso interno, recibe las líneas pendientes de la costura.

**ReceiptService**
- ListAsync, GetAsync.
- CreateAsync:
  - almacén por WmsResolve o default;
  - staging validado;
  - modo ASN: EXPECTED, del mismo almacén, con LockHeader Asn;
  - modo PO: purchasing.receive (EnsureAsync), módulo PURCHASING (EnsureEnabledAsync), IPurchaseOrderReceiving.LockForReceiptAsync (P8 bloquea y valida) y ASN por lo pendiente;
  - BLIND/RETURN con las líneas del request;
  - número REC-##### al final;
  - ReceivedQty = ExpectedQty (R8);
  - UX_Receipt_Asn → 409;
  - nace OPEN.
- UpdateLineAsync: la línea se busca dentro del recibo (404); solo OPEN; lote vía EnsureLot; series normalizadas. Bajar ReceivedQty por debajo de lo asignado a cross-dock se permite: el faltante se ve al confirmar.
- AddLineAsync / RemoveLineAsync: solo líneas extra; 409 LineHasCrossDock.
- ConfirmAsync, dentro de RunInTransactionAsync:
  1. LockHeader Receipt.
  2. Re-verifica OPEN (422).
  3. LockHeader Asn.
  4. Valida el seguimiento de cada línea (400 con Errors).
  5. Postings por ReceiptPostingRules (To staging; Ref RECEIPT + id) → InventoryLedger.PostAsync.
  6. AdjustmentTxnId.
  7. Si hay PO: IPurchaseOrderReceiving.ApplyReceiptAsync (bloquea la PO: Receipt < Asn < PO).
  8. ASN → RECEIVED.
  9. Recibo OPEN→RECEIVED, con ReceivedAtUtc/By.
  10. IReceiptConfirmationParticipant.OnReceiptConfirmedAsync → cantidad a cross-dock por línea.
  11. PUTAWAY por (recibido − cross-dock) > 0, con sugerencia de PutawaySuggester.
  12. Si no se creó ninguna PUTAWAY → RECEIVED→PUTAWAY directo.
- DeleteAsync(Guid): solo OPEN, sin asignaciones de cross-dock (409 si las hay) → IsActive=0. Si el ASN nació de una PO, se CANCELA; si es de cliente, queda EXPECTED.

**Controladores** [RequireModule(WMS_LOTSERIAL)]:
- ReceiptsController `api/v1/receipts`:
  - inventory.view: GET, GET {publicId}.
  - warehouse.receive: POST, PUT {publicId}/lines/{lineId:int}, POST {publicId}/lines, DELETE {publicId}/lines/{lineId:int}, POST {publicId}/confirm, DELETE {publicId}.
- AsnsController `api/v1/asns`:
  - inventory.view: GET, GET {id:int}.
  - warehouse.receive: POST, POST {id:int}/cancel.

**ReceiptDataSource**: ReceivedCost = Σ recibido × UnitCost de la línea de PO.

**Pruebas**
- ReceiptPostingRulesTests y ReceiptRulesTests (puras).
- ReceiptServiceTests (InMemory, WmsFixture, con fakes de IPurchaseOrderReceiving e IReceiptConfirmationParticipant para no depender de P8/P9).

Archivos:

- `src/Teikem.Domain/Wms/ReceiptRules.cs`
- `src/Teikem.Domain/Wms/ReceiptPostingRules.cs`
- `src/Teikem.Infrastructure/Services/AsnService.cs`
- `src/Teikem.Infrastructure/Services/ReceiptService.cs`
- `src/Teikem.Infrastructure/Analytics/ReceiptDataSource.cs`
- `src/Teikem.Api/Controllers/ReceiptsController.cs`
- `src/Teikem.Api/Controllers/AsnsController.cs`
- `tests/Teikem.Tests/ReceiptRulesTests.cs`
- `tests/Teikem.Tests/ReceiptPostingRulesTests.cs`
- `tests/Teikem.Tests/ReceiptServiceTests.cs`

### P5 — Cola de tareas de almacén con handlers por tipo, putaway dirigido con rotación y reabasto: lista, asignar, iniciar, completar por handler (PUTAWAY/REPLENISH: TRANSFER), cancelar; reabasto bajo demanda; efecto que cierra el recibo; fuente WAREHOUSE_TASK

- Toca archivos compartidos: no. Depende de: P0.

Cola unificada (R17), putaway (R10) y reabasto (R15), con handlers por tipo (D41).

**WarehouseTaskRules (puro)**
- NoHandler(tipo) = 'Las tareas de tipo {tipo} no se completan desde la cola.'
- TaskNotOpen = 'La tarea ya fue completada o cancelada.'
- AssigneeNotMember = 'El usuario no es miembro activo de la compañía.'
- QtyExceeds = 'La cantidad excede la de la tarea.'
- DestinationRequired = 'Indique la posición de destino.'
- Split(taskQty, completedQty) → remanente.
- Orden de la cola: Priority asc, CreatedAtUtc asc.
El permiso por tipo YA NO vive aquí: lo declara cada handler (RequiredPermission).

**Handlers** (IWarehouseTaskHandler, registrados por P0 en DI)
- PutawayTaskHandler: TaskType PUTAWAY; RequiredPermission warehouse.receive.
  - LockReferencesAsync: si el Ref es RECEIPT → LockHeader Receipt.
  - CompleteAsync: destino = req.ToBinId ?? task.ToBinId (400 DestinationRequired), del mismo almacén (WmsResolve → 404) y distinto del origen. TRANSFER con Ref WAREHOUSE_TASK + id y series si aplica. Devuelve la cantidad movida.
- ReplenishTaskHandler: TaskType REPLENISH; RequiredPermission warehouse.pick. TRANSFER de FromBin a ToBin con el lote de la tarea; no toca lo reservado.

**ReplenishmentRules (puro)**: como en el plan ganador (objetivo MaxPickQty o 2×MinPickQty; origen StockAllocator restringido a RESERVE; motivos de omisión OPEN_TASK, NO_RESERVE y NOT_BELOW_MIN).

**WarehouseTaskService**
Resuelve IEnumerable<IWarehouseTaskHandler> por TaskType.
- ListAsync y GetAsync. CompletableFromQueue = existe un handler y su NotFromQueueMessage es null.
- AssignAsync (warehouse.manage; UserTenant ACTIVE).
- StartAsync: EnsureAsync(handler.RequiredPermission) → 403 + PERMISSION_DENIED; sin handler → 422 NoHandler.
- CompleteAsync:
  1. Snapshot sin bloqueo.
  2. Handler (422 NoHandler o NotFromQueueMessage).
  3. EnsureAsync del permiso del handler.
  4. handler.LockReferencesAsync.
  5. LockHeader WarehouseTask.
  6. Re-verifica abierta (422 al segundo de dos).
  7. completed = handler.CompleteAsync.
  8. Si completed < Quantity → remanente como tarea nueva (WarehouseTaskWriter, mismo Ref) y Quantity = completed.
  9. AdvanceAsync DONE.
  Se permite completar sin iniciar.
- CancelAsync (warehouse.manage).
- SuggestPutawayAsync → PutawaySuggestionDto, con RotationClass.

**ReplenishmentService.RunAsync**: LockHeader Warehouse (idempotente por almacén); crea las REPLENISH.

**WarehouseTaskStatusEffect**: cuando una PUTAWAY con Ref RECEIPT llega a DONE o CANCELLED y no queda otra abierta → recibo RECEIVED→PUTAWAY (sin cambio si no está en RECEIVED).

**WarehouseTasksController** `api/v1/warehouse-tasks`, [RequireModule(WMS_LOTSERIAL)]:
- inventory.view: GET, GET {id:int}, GET putaway-suggestions.
- warehouse.manage: POST {id:int}/assign, POST {id:int}/cancel.
- inventory.view + permiso del handler en el servicio: POST {id:int}/start, POST {id:int}/complete.
- warehouse.pick: POST replenishment/run.

**WarehouseTaskDataSource**: AgeHours.

**Pruebas**: WarehouseTaskRulesTests, ReplenishmentRulesTests y WarehouseTaskStatusEffectTests (InMemory).

Archivos:

- `src/Teikem.Domain/Wms/WarehouseTaskRules.cs`
- `src/Teikem.Domain/Wms/ReplenishmentRules.cs`
- `src/Teikem.Infrastructure/Services/WarehouseTaskService.cs`
- `src/Teikem.Infrastructure/Services/ReplenishmentService.cs`
- `src/Teikem.Infrastructure/Services/WarehouseTaskStatusEffect.cs`
- `src/Teikem.Infrastructure/Services/PutawayTaskHandler.cs`
- `src/Teikem.Infrastructure/Services/ReplenishTaskHandler.cs`
- `src/Teikem.Infrastructure/Analytics/WarehouseTaskDataSource.cs`
- `src/Teikem.Api/Controllers/WarehouseTasksController.cs`
- `tests/Teikem.Tests/WarehouseTaskRulesTests.cs`
- `tests/Teikem.Tests/ReplenishmentRulesTests.cs`
- `tests/Teikem.Tests/WarehouseTaskStatusEffectTests.cs`

### P6 — Conteo cíclico en modo informado: foto del sistema, captura de cantidades o series, confirmación con warehouse.count contra el saldo actual bloqueado (SystemQtyChanged), guarda de reservado, ajustes enlazados en AdjustmentTxnId; handler COUNT de la cola

- Toca archivos compartidos: no. Depende de: P0.

Conteo cíclico (R16, R33, bitácora L542: 'al confirmar, cada línea con diferencia genera un ajuste').

**CycleCountRules (puro)**
- SelectLines, con tope de 1000: 'El conteo admite como máximo 1000 líneas; acote los filtros.'
- IsStale(systemQty, currentQty).
- Variance(counted, system): informativa, contra la foto.
- Adjustment(counted, currentAtReconcile): lo que se asienta.
- SerialVariance(expectedAtReconcile[], counted[], ubicaciones) → bajas, altas y TRANSFER.
- Mensajes:
  - CountNotOpen = 'El conteo ya fue reconciliado; solo se consulta.'
  - CountIncomplete(n) = 'Faltan {n} línea(s) por contar.'
  - ReservedAboveCount(sku, bin, contado, reservado) = 'El conteo de {sku} en {bin} ({x}) es menor que lo reservado ({y}); libere la reserva antes de reconciliar.'
  - SerialCountedByList = 'En productos con serie se capturan los números de serie, no la cantidad.'
  - LineDuplicated = 'Esa posición, producto y lote ya están en el conteo.'

**CountTaskHandler** (IWarehouseTaskHandler)
- TaskType COUNT; RequiredPermission warehouse.count.
- NotFromQueueMessage = 'Las tareas de conteo se completan desde Conteo cíclico.' (iniciar desde la cola sí se permite).
- CompleteAsync nunca se invoca.

**CycleCountService**
- ListAsync.
- CreateAsync: CC-#####; SystemQty = foto; nace OPEN; una tarea COUNT (Ref CYCLE_COUNT).
- GetAsync(int, CycleCountLinesQuery): modo informado con SystemQty, ExpectedSerials, CurrentQty e IsStale.
- CaptureAsync, AddLineAsync, FinishAsync (OPEN→COUNTED).
- RefreshAsync: opcional; re-fotografía las líneas stale y borra su captura, para quien prefiere recontar.
- ReconcileAsync (D22), dentro de RunInTransactionAsync:
  1. LockHeader CycleCount.
  2. Re-verifica que no esté RECONCILED (422 al segundo).
  3. Todas contadas (si no, 422 CountIncomplete).
  4. Bloquea los saldos de todas las líneas en orden de clave (LockBalance de InventoryQueries) y las series implicadas.
  5. Por línea: ReconciledSystemQty = QtyOnHand actual; SystemQtyChanged = actual ≠ SystemQty.
  6. Si CountedQty < QtyReserved → 409 ReservedAboveCount, con Errors por línea y SIN escribir nada.
  7. Postings ADJUSTMENT COUNT_VARIANCE por Adjustment ≠ 0 (Ref CYCLE_COUNT + id), o el plan de series contra la ubicación ACTUAL.
  8. AdjustmentTxnId.
  9. Transición escalonada a RECONCILED; ReconciledAtUtc/By.
  10. Tarea COUNT → DONE.
  La respuesta marca las líneas SystemQtyChanged para revisión.
- DeleteAsync: solo OPEN → IsActive=0 y COUNT → CANCELLED.

**CycleCountsController** `api/v1/cycle-counts`, [RequireModule(WMS_LOTSERIAL)]:
- inventory.view: GET.
- warehouse.count: GET {id:int}, POST, PUT {id:int}/lines, POST {id:int}/lines, POST {id:int}/finish, POST {id:int}/refresh, POST {id:int}/reconcile (D22, como el mock) y DELETE {id:int}.

**Pruebas**: CycleCountRulesTests (pura) y CycleCountServiceTests (InMemory: ajuste contra el saldo actual con SystemQtyChanged, 409 de reservado sin efectos, doble reconciliación → 422).

Archivos:

- `src/Teikem.Domain/Wms/CycleCountRules.cs`
- `src/Teikem.Infrastructure/Services/CycleCountService.cs`
- `src/Teikem.Infrastructure/Services/CountTaskHandler.cs`
- `src/Teikem.Api/Controllers/CycleCountsController.cs`
- `tests/Teikem.Tests/CycleCountRulesTests.cs`
- `tests/Teikem.Tests/CycleCountServiceTests.cs`

### P7 — Recolección y empaque ad hoc: recolectar (número EMP primero, cabecera antes del ISSUE con Ref en el INSERT, FEFO o explícito), lista con filtros y búsqueda final, empacar (orden real vía OrderService), eliminar con reversa; guarda de borrado en Órdenes; IOrderInventoryLines para el Lote 10; fuente PICK_BATCH

- Toca archivos compartidos: no. Depende de: P0.

Recolección y empaque (R13, R34-R43, R11).

**PickBatchRules (puro)**
- SingleOwner → 400 'Una recolección solo puede tener productos de un mismo dueño.'
- OrderClientMustBeOwner → 400 'La orden debe ser del cliente dueño del inventario ({cliente}).'
- CanDelete(status, orderIsActive, orderIsInitial).
- DeleteBlocked(n, estatus) = 'La orden de la recolección {n} ya avanzó a '{estatus}'; la recolección ya no se puede eliminar.'
- NotCollected(n) = 'La recolección {n} ya fue empacada.'
- PackSpecialNotAllowed = 'Un empaque no puede ser una entrega especial ni llevar chofer.'
- DisplayNumbers = 'Orden: {x} · Factura: {y|—}'.
- MatchesFilters y después MatchesSearch.
- MaxLines 100.

**OrderRules.cs** (Lote 3; solo agrega): PickBatchOrderDeleteMessage(n) = 'Esta orden nació de la recolección {n}; elimínela desde Recolección y empaque para restaurar el inventario.' y CanDeleteFromOrders.

**OrderService.cs** (Lote 3; cambio mínimo): sobrecarga DeleteAsync(Guid, OrderScope, OrderDeletionOptions, CancellationToken). Con origen PICK_BATCH no permitido → 409 (D12). Nada más cambia.

**PickBatchService**
- ListAsync, GetAsync.
- CollectAsync(PickBatchCreateRequest) (D48):
  1. NumberSequenceService.EnsureAsync(PACKBATCH), FUERA de la transacción.
  2. RunInTransactionAsync:
     a. almacén; productos activos; SingleOwner; asignación por línea sin bloqueo (series por CurrentBinId AVAILABLE, explícitos validados, o StockAllocator FEFO sobre el disponible = en mano − reservado);
     b. NextAsync(PACKBATCH) como PRIMER bloqueo (excepción documentada al orden);
     c. INSERT de PickBatch COLLECTED con su número EMP (SaveChanges → id), con historial PICK_BATCH;
     d. InventoryLedger.PostAsync con ISSUE From posición y Ref PICK_BATCH + id YA en el INSERT. El ledger bloquea y re-verifica: un faltante da 409 insufficient_stock y revierte TODO, incluido el número, así que los EMP quedan sin huecos;
     e. líneas con UnitCost = PurchaseCost (D35) e IssueTxnId.
  Sin SetRef ni UPDATE del ledger.
- PackAsync(Guid, PickBatchPackRequest), como en el plan ganador:
  1. warehouse.pick + orders.create.
  2. LockHeader PickBatch; re-verifica COLLECTED.
  3. Sin especial ni chofer.
  4. OrderClientMustBeOwner.
  5. OrderService.CreateAsync con OrderCreationOptions(batch.Number, PICK_BATCH, id). La numeración de la orden respeta Client.ClientAssignsOrderNumber y ClientAssignsInvoiceNumber.
  6. Factura final copiada al lote (R40).
  7. COLLECTED→PACKED.
  8. UX_PickBatch_Order como última línea.
- DeleteAsync(Guid, PickBatchDeleteRequest):
  1. LockHeader PickBatch; activo (404 al segundo).
  2. Si PACKED: orders.cancel; orden activa y en etapa inicial (422 DeleteBlocked); OrderService.DeleteAsync con OrderDeletionOptions(PICK_BATCH).
  3. Reversa por línea: ADJUSTMENT de entrada PICK_BATCH_REVERSAL a la posición original (422 si está inactiva), Ref PICK_BATCH; la serie vuelve a AVAILABLE; ReversalTxnId.
  4. CANCELLED, IsActive=0.

**OrderInventoryLinesProvider** (IOrderInventoryLines, D45): GetAsync(transportOrderIds) → líneas de PickBatchLine de lotes PACKED activos con esas órdenes y sin ReversalTxnId. Producto, lote, serie, cantidad, UnitCost congelado y SalePrice vigente del producto. Una sola consulta con filtro de tenant. Es insumo de PRODUCT_SALE y de la Contabilización de despachos (Lote 10).

**PickBatchesController** `api/v1/pick-batches`, [RequireModule(WMS_LOTSERIAL)]:
- inventory.view: GET, GET {publicId}.
- warehouse.pick: POST, POST {publicId}/pack, DELETE {publicId}.

**PickBatchDataSource**: TotalCost = Σ qty × UnitCost (Round4).

**Pruebas**: PickBatchRulesTests, OrderPickBatchGuardTests y OrderInventoryLinesTests (InMemory: lotes PACKED, COLLECTED y CANCELLED, líneas revertidas, otro tenant).

Archivos:

- `src/Teikem.Domain/Wms/PickBatchRules.cs`
- `src/Teikem.Domain/Orders/OrderRules.cs`
- `src/Teikem.Infrastructure/Services/PickBatchService.cs`
- `src/Teikem.Infrastructure/Services/OrderService.cs`
- `src/Teikem.Infrastructure/Services/OrderInventoryLinesProvider.cs`
- `src/Teikem.Infrastructure/Analytics/PickBatchDataSource.cs`
- `src/Teikem.Api/Controllers/PickBatchesController.cs`
- `tests/Teikem.Tests/PickBatchRulesTests.cs`
- `tests/Teikem.Tests/OrderPickBatchGuardTests.cs`
- `tests/Teikem.Tests/OrderInventoryLinesTests.cs`

### P8 — Compras mínimas y Ajustes de inventario: proveedores, órdenes de compra (borrador, enviar, cancelar incluso desde PARTIAL con bitácora, eliminar sin recepciones; edición por capacidad EDIT_PURCHASE_ORDER), costura IPurchaseOrderReceiving para la recepción, faltantes por línea y su resolución (Cerrar, Reordenar, Ajuste manual)

- Toca archivos compartidos: no. Depende de: P0.

Compras 13B (lo mínimo que exige la recepción) y la pantalla 'Ajustes de inventario' (R14, bitácora L762).

**PurchaseOrderRules (puro)**
- CanCancel = DRAFT, SENT o PARTIAL (L461). RECEIVED → 'Una orden de compra recibida completa no se cancela.'
- HasOpenReceipt = 'La orden de compra tiene un recibo abierto; confírmelo o elimínelo antes de cancelar.'
- IsDeletable(status, tieneRecepciones, tieneReciboAbierto): DRAFT, SENT o CANCELLED, sin recepciones y sin recibo abierto. Mensajes:
  - 'Una orden de compra con recepciones no se elimina; cancélela.'
  - 'Una orden de compra cancelada con recepciones se conserva con su bitácora; no se elimina.'
- La edición NO es regla pura: la decide la capacidad (D46). Con la capacidad habilitada por el tenant fuera de DRAFT, las guardas de línea siguen activas: ReceivedLineLocked(sku, x) = 'La línea de {sku} ya tiene recepciones: no se elimina, no baja de lo recibido ({x}) y su costo no cambia.'
- Líneas:
  - producto propio;
  - QtyOrdered > 0;
  - UnitCost ≥ 0, con default Product.PurchaseCost ('Indique el costo unitario de {sku}.');
  - producto no repetido: 'El producto {sku} está repetido en la orden de compra.';
  - máximo 200 líneas.
- Total con Round4.

**ShortageRules (puro)**
- Pending y Validate(action, qty, pending).
- Mensajes:
  - NoPendingShortage = 'La línea ya no tiene faltante pendiente.'
  - ManualExceeds(p) = 'La cantidad del ajuste excede el faltante pendiente ({p}).'
  - UnknownAction = 'Acción desconocida: use CLOSE, REORDER o MANUAL_ADJUSTMENT.'
  - PoNotReceivedYet = 'La orden de compra todavía no tiene recepciones confirmadas.'
  - PoCancelled = 'La orden de compra está cancelada; su faltante ya no se resuelve.'
- PendingCost.

**PurchaseOrderReceivingService** (IPurchaseOrderReceiving; la consume P4)
- LockForReceiptAsync(Guid): LockHeader PO; IsReceivable y NothingPending de PurchaseStatusRules (P0); devuelve PurchaseOrderForReceipt con pendiente = ordered − received − resolved.
- ApplyReceiptAsync(poId, cantidades): LockHeader PO (orden Receipt < Asn < PO); QtyReceived += por PurchaseOrderLineId; transición escalonada con StatusAfterReceipt.

**SupplierService**: List, Create, Update (rowVersion), SetActive; 409 'Ya existe un proveedor activo con ese nombre.'; término de pago por catálogo.

**PurchaseOrderService**
- List, Get: CanEdit por StatusService.IsAllowed de EDIT_PURCHASE_ORDER; CanCancel y CanDelete por las reglas.
- Create: PO-#####, nace DRAFT.
- Update: StatusService.EnsureAllowedAsync(PURCHASE_ORDER, status, EDIT_PURCHASE_ORDER) → 422 fuera de DRAFT por defecto (D46); reemplaza líneas respetando ReceivedLineLocked; rowVersion.
- Send: DRAFT→SENT.
- Cancel(Guid, PurchaseOrderStatusRequest): LockHeader PO; CanCancel; HasOpenReceipt (recibo activo OPEN sobre un ASN de la PO → 409). Transición a CANCELLED con el comentario en el historial: no hay regla lateral sembrada, así que el motor la permite desde DRAFT, SENT y PARTIAL.
- Delete: IsDeletable → IsActive=0.
- CreateDraftAsync: uso interno de Reordenar.

**PurchaseShortageService**
- ListWithShortageAsync: PO activas NO canceladas, con recibo RECEIVED y pendiente > 0.
- LinesAsync.
- ResolveAsync: LockHeader PO; 422 PoCancelled; línea dentro de la PO; pendiente recalculado bajo bloqueo (409 al segundo). Acciones:
  - CLOSE;
  - REORDER (purchasing.manage);
  - MANUAL_ADJUSTMENT: módulo WMS_LOTSERIAL; ADJUSTMENT de entrada PO_SHORTAGE o FOUND con Ref PURCHASE_ORDER.
  Guarda la fila de resolución; si todo queda en 0 y la PO está PARTIAL → RECEIVED.

**Controladores** [RequireModule(PURCHASING)]:
- SuppliersController `api/v1/suppliers`:
  - purchasing.view: GET.
  - purchasing.manage: POST, PATCH {id:int}, POST {id:int}/deactivate|reactivate.
- PurchaseOrdersController `api/v1/purchase-orders`:
  - purchasing.view: GET, GET {publicId}, GET shortages, GET {publicId}/shortage-lines.
  - purchasing.manage: POST, PATCH {publicId}, POST {publicId}/send, POST {publicId}/cancel, DELETE {publicId}.
  - inventory.adjust: POST {publicId}/lines/{lineId:int}/resolve.

**Pruebas**: PurchaseOrderRulesTests, ShortageRulesTests y PurchaseOrderReceivingTests (InMemory: PARTIAL/RECEIVED, NothingPending, PO en DRAFT o CANCELLED → 422).

Archivos:

- `src/Teikem.Domain/Wms/PurchaseOrderRules.cs`
- `src/Teikem.Domain/Wms/ShortageRules.cs`
- `src/Teikem.Infrastructure/Services/SupplierService.cs`
- `src/Teikem.Infrastructure/Services/PurchaseOrderService.cs`
- `src/Teikem.Infrastructure/Services/PurchaseShortageService.cs`
- `src/Teikem.Infrastructure/Services/PurchaseOrderReceivingService.cs`
- `src/Teikem.Api/Controllers/SuppliersController.cs`
- `src/Teikem.Api/Controllers/PurchaseOrdersController.cs`
- `tests/Teikem.Tests/PurchaseOrderRulesTests.cs`
- `tests/Teikem.Tests/ShortageRulesTests.cs`
- `tests/Teikem.Tests/PurchaseOrderReceivingTests.cs`

### P9 — Cruce de muelle (demo, módulo CROSSDOCK): citas sin solapamiento y ocupación del muelle; planes XD-##### con asignación sobre recibos ABIERTOS (reparto al confirmar vía IReceiptConfirmationParticipant, faltante visible) o confirmados (reduce el putaway); reserva del staging; mover desde la pantalla o la cola (CrossDockTaskHandler)

- Toca archivos compartidos: no. Depende de: P0.

Cruce de muelle como demo funcional (R19b, R20-R22; maestro L316-L320).

**DockScheduleRules (puro)**: sin cambios.
- Solapamiento semiabierto; sin fin = 60 minutos.
- Compatible(dockType, direction).
- Mensajes Overlap y Window; horizonte de ayer a +90 días.
- ASN o Trip, nunca ambos.

**CrossDockRules (puro)**
- AllocatableOpen(baseQty = ReceivedQty de la línea abierta, activeAllocated).
- AllocatableConfirmed(received − activeAllocated, pendingPutaway).
- Split(received, asignaciones PLANNED en orden CreatedAtUtc/Id) → ConfirmedQty por asignación (FIFO), remanente a putaway y ShortQty = AllocatedQty − ConfirmedQty por asignación: el faltante outbound visible de L320.
- Mensajes:
  - Exceeds(a) = 'La cantidad excede lo disponible para cruce de muelle ({a}).'
  - ReceiptNotConfirmed = 'La recepción de la línea todavía no se confirma; la mercancía se mueve después de confirmar.'
  - NothingConfirmed = 'La asignación no recibió mercancía al confirmar; cancélela.'
  - ExactQty(q) = 'La tarea de cruce de muelle se completa por la cantidad confirmada ({q}).'
  - StagingZone = 'La mercancía debe estar en la zona de staging del plan.'
  - OrderNotShippable = 'La orden no admite asignaciones (cancelada, entregada o dada de baja).'
  - PlanNotOpen = 'El plan ya fue completado.'
  - CanComplete: 'Mueva o cancele las asignaciones pendientes antes de completar el plan.'

**DockAppointmentService**: como en el plan ganador (LockDock, solapamiento con 409, SCHEDULED, Reschedule, SetStatus). El motivo del NO_SHOW va en el comentario del historial (D30).

**DockAppointmentStatusEffect**: ARRIVED ocupa el muelle; COMPLETED, NO_SHOW o CANCELLED lo liberan si no queda otra llegada; MAINTENANCE no se toca.

**CrossDockService**
Regla de bloqueo: todo escritor de asignaciones bloquea Plan → ReceiptHeader de la línea → tarea → saldos.
- List, Get, Create: XD-#####, OPEN; zona de staging CROSSDOCK o STAGING del mismo almacén.
- CandidatesAsync: líneas de recibos OPEN (BaseQty = ReceivedQty) y RECEIVED/PUTAWAY (con putaway pendiente) del almacén.
- AllocateAsync(planId, req). Dentro de RunInTransactionAsync: LockHeader Plan y luego Receipt; orden activa por PublicId (404); OrderNotShippable. Según el recibo:
  - OPEN: AllocatableOpen; asignación PLANNED con ConfirmedQty NULL, sin tarea ni reserva;
  - RECEIVED/PUTAWAY: AllocatableConfirmed; WarehouseTaskWriter.ReduceAsync sobre la PUTAWAY; InventoryLedger.ReserveAsync sobre el saldo de staging; tarea CROSSDOCK (Ref CROSSDOCK_ALLOCATION); ConfirmedQty = qty.
  En ambos casos: historial de la asignación y plan OPEN→ALLOCATED en la primera.
- CancelAllocationAsync: Plan → Receipt → tarea. Solo PLANNED → CANCELLED. Si ConfirmedQty > 0: ReleaseAsync, CROSSDOCK → CANCELLED y PUTAWAY nueva por ConfirmedQty (R22).
- MoveAsync(planId, allocationId) y MoveCoreAsync(allocation), que comparte con el handler:
  - re-verifica PLANNED;
  - recibo OPEN → 422 ReceiptNotConfirmed; ConfirmedQty 0 → 422 NothingConfirmed;
  - CROSSDOCK From staging por ConfirmedQty con FromReserved = true (sale del inventario y libera la reserva), Ref CROSSDOCK_ALLOCATION;
  - InventoryTransactionId; tarea DONE; asignación MOVED.
- CompleteAsync: CanComplete → COMPLETED.

**CrossDockReceiptParticipant** (IReceiptConfirmationParticipant; lo llama P4 dentro de la confirmación, con el recibo YA bloqueado; NO bloquea el plan):
- lee las asignaciones PLANNED con ConfirmedQty NULL de las líneas;
- aplica Split con el ReceivedQty final;
- por asignación con ConfirmedQty > 0: ReserveAsync en el staging de la línea y tarea CROSSDOCK;
- guarda ConfirmedQty (0 = faltante total, visible);
- valida StagingZone contra la zona del plan (400 con Errors);
- devuelve Σ ConfirmedQty por línea.
Sin asignaciones → diccionario vacío.

**CrossDockTaskHandler** (IWarehouseTaskHandler): TaskType CROSSDOCK; RequiredPermission warehouse.crossdock; se completa DESDE LA COLA.
- LockReferencesAsync: Plan y luego Receipt de la asignación.
- CompleteAsync: MoveCoreAsync; req.Quantity distinto de ConfirmedQty → 400 ExactQty.

**Controladores** [RequireModule(CROSSDOCK)]:
- DockAppointmentsController `api/v1/dock-appointments`:
  - inventory.view: GET.
  - warehouse.crossdock: POST, PATCH {id:int}, POST {id:int}/status.
- CrossDockPlansController `api/v1/cross-dock-plans`:
  - inventory.view: GET, GET {id:int}, GET {id:int}/candidates.
  - warehouse.crossdock: POST, POST {id:int}/allocations, DELETE {id:int}/allocations/{allocationId:int}, POST {id:int}/allocations/{allocationId:int}/move, POST {id:int}/complete.

**Pruebas**: DockScheduleRulesTests, CrossDockRulesTests (incluye Split) y DockAppointmentStatusEffectTests.

Archivos:

- `src/Teikem.Domain/Wms/DockScheduleRules.cs`
- `src/Teikem.Domain/Wms/CrossDockRules.cs`
- `src/Teikem.Infrastructure/Services/DockAppointmentService.cs`
- `src/Teikem.Infrastructure/Services/CrossDockService.cs`
- `src/Teikem.Infrastructure/Services/CrossDockTaskHandler.cs`
- `src/Teikem.Infrastructure/Services/CrossDockReceiptParticipant.cs`
- `src/Teikem.Infrastructure/Services/DockAppointmentStatusEffect.cs`
- `src/Teikem.Api/Controllers/DockAppointmentsController.cs`
- `src/Teikem.Api/Controllers/CrossDockPlansController.cs`
- `tests/Teikem.Tests/DockScheduleRulesTests.cs`
- `tests/Teikem.Tests/CrossDockRulesTests.cs`
- `tests/Teikem.Tests/DockAppointmentStatusEffectTests.cs`

### P10 — Cierre: smoke del Lote 6, seguridad de controladores, cobertura de handlers de tareas, docs/lote6-decisiones.md, capítulo 06 del manual funcional, FAQ e índice

- Toca archivos compartidos: no. Depende de: P0, P1, P2, P3, P4, P5, P6, P7, P8, P9.

Cierra el lote.

**Smoke**
- scripts/smoke.sh agrega los pasos de 'smoke' ANTES de 'sesiones: refresh con rotación y logout'.
- Usa $TS. Las aserciones que dependen del número de almacenes activos son condicionales.
- Termina en 'SMOKE OK'.

**WmsControllerSecurityTests**, por reflexión sobre los 13 controladores:
- mapa exacto (controlador, acción) → permiso;
- [RequireModule] de clase;
- ningún parámetro 'tenantId';
- toda acción pública está mapeada.

**WarehouseTaskHandlerCoverageTests**:
- las clases que implementan IWarehouseTaskHandler en Teikem.Infrastructure cubren exactamente PUTAWAY (warehouse.receive), REPLENISH (warehouse.pick), COUNT (warehouse.count, no desde la cola) y CROSSDOCK (warehouse.crossdock), con un handler por tipo;
- DependencyInjection.cs registra las cuatro;
- el conjunto de tipos 'desde la cola' es {PUTAWAY, REPLENISH, CROSSDOCK}.
Leen constantes públicas de cada handler.

**docs/lote6-decisiones.md**
- Formato del Lote 5: mapa de lo construido, cómo se prueba (build, test, db-init ×2, smoke), decisiones a revisar (D1-D49 que queden abiertas) y lo que queda fuera.
- Registra el diferimiento del filtro por almacén de UserDataScope (D42) y actualiza la nota de docs/lote1-decisiones.md:59 SOLO por referencia (no edita el documento del Lote 1).
- Enlace del CI.

**docs/manual/06-inventario-y-almacen.md**: cada funcionalidad con qué hace, permiso y módulo, pantalla/endpoint, validaciones con el mensaje EXACTO y el HTTP, y estatus y transiciones. Secciones:
- almacenes (incluido el almacén demo ALM-01);
- productos;
- inventario y Kárdex (cantidad con signo);
- ajustes y transferencias;
- recepción;
- tareas y handlers por tipo;
- putaway con rotación;
- reabasto;
- conteo cíclico (ajuste contra el saldo actual y marca 'saldo movido');
- recolección y empaque;
- compras (cancelar desde recibida parcial; capacidad de edición);
- cruce de muelle (asignar antes o después de confirmar; faltante);
- trazabilidad;
- conciliación;
- permisos y módulos;
- análisis;
- casos frecuentes.

**faq.md**: sección 'Lote 6 — Inventario y almacén', con cada mensaje de error del lote y qué hacer. Por ejemplo: insufficient_stock, conteo menor que lo reservado, orden nacida de recolección, serie dada de baja, almacén con documentos abiertos, OC con recibo abierto, asignación sin mercancía confirmada.

**README.md** indexa el capítulo 06.

Archivos:

- `scripts/smoke.sh`
- `tests/Teikem.Tests/WmsControllerSecurityTests.cs`
- `tests/Teikem.Tests/WarehouseTaskHandlerCoverageTests.cs`
- `docs/lote6-decisiones.md`
- `docs/manual/06-inventario-y-almacen.md`
- `docs/manual/faq.md`
- `docs/manual/README.md`

## Cambios SQL

- Diseño/logistica-db-estructura.sql · CAPA 5 dbo.Client (inline '-- Lote 6'): + CONSTRAINT UQ_Client_IdTenant UNIQUE (ClientId, TenantId). Es el destino de FK_Product_Client y FK_Asn_Client.
- Diseño/logistica-db-estructura.sql · dbo.NumberSequence: CK_NumberSequence_Kind agrega 'RECEIPT', 'CYCLECOUNT', 'CROSSDOCK' y 'PURCHASE' (por tenant, ClientId NULL). La recolección reutiliza PACKBATCH.
- Diseño/logistica-db-estructura.sql · CAPA 7 dbo.Warehouse: + CONSTRAINT UQ_Warehouse_IdTenant UNIQUE (WarehouseId, TenantId). GeoPoint sin cambio.
- Diseño/logistica-db-estructura.sql · CAPA 7 dbo.WarehouseZone: + CONSTRAINT UQ_WarehouseZone_IdWh UNIQUE (WarehouseZoneId, WarehouseId).
- Diseño/logistica-db-estructura.sql · CAPA 7 dbo.WarehouseBin:
- + WarehouseId INT NOT NULL.
- FK_WarehouseBin_Zone (WarehouseZoneId, WarehouseId) → WarehouseZone, en lugar del REFERENCES simple.
- + UQ_WarehouseBin_IdWh (WarehouseBinId, WarehouseId).
- + UQ_WarehouseBin_WhCode (WarehouseId, Code).
- + CK_WarehouseBin_MaxWeight CHECK (MaxWeightKg IS NULL OR MaxWeightKg > 0).
- Se conserva UQ_WarehouseBin.
- Diseño/logistica-db-estructura.sql · CAPA 7 dbo.WarehouseDock: + CONSTRAINT UQ_WarehouseDock_IdWh UNIQUE (WarehouseDockId, WarehouseId).
- Diseño/logistica-db-estructura.sql · CAPA 8 dbo.ProductCategory:
- + UQ_ProductCategory_IdTenant (ProductCategoryId, TenantId).
- ParentId pasa a una FK compuesta por ALTER TABLE después del CREATE: FK_ProductCategory_Parent (ParentId, TenantId).
- + CREATE UNIQUE INDEX UX_ProductCategory_Name ON dbo.ProductCategory(TenantId, ParentId, Name) WHERE IsActive = 1.
- Diseño/logistica-db-estructura.sql · CAPA 8 dbo.Product:
- + MinQty, MinPickQty y MaxPickQty DECIMAL(16,3) NULL.
- FKs compuestas: FK_Product_Client (ClientId, TenantId), FK_Product_Category (ProductCategoryId, TenantId), FK_Product_PrefWarehouse (PreferredWarehouseId, TenantId) y FK_Product_PrefBin (PreferredBinId, PreferredWarehouseId) → WarehouseBin(WarehouseBinId, WarehouseId).
- + UQ_Product_IdTenant.
- + CK_Product_Numbers (costos, pesos y mínimos ≥ 0; MaxPickQty ≥ MinPickQty) y CK_Product_PrefBin.
- + UX_Product_Barcode (TenantId, Barcode) WHERE Barcode IS NOT NULL AND IsActive = 1.
- Comentario: ClientId = dueño del inventario (cliente 3PL; NULL = propio del tenant), distinto de TenantId.
- Diseño/logistica-db-estructura.sql · CAPA 8 dbo.InventoryLot:
- + UQ_Lot_IdProduct (LotId, ProductId).
- + CK_Lot_Dates.
- Comentario: sin TenantId propio; hereda la tenencia de Product por FK compuesta.
- Diseño/logistica-db-estructura.sql · CAPA 8 dbo.InventorySerial:
- + CurrentWarehouseId INT NULL y CurrentBinId INT NULL.
- FK_Serial_Lot (LotId, ProductId).
- FK_Serial_CurrentBin (CurrentBinId, CurrentWarehouseId).
- + UQ_Serial_IdProduct.
- + CK_Serial_Location.
- + IX_Serial_CurrentBin (filtrado).
- Comentario de tenencia indirecta, igual que en InventoryLot.
- Diseño/logistica-db-estructura.sql · CAPA 8 dbo.StockBalance:
- FKs compuestas: FK_StockBalance_Product (ProductId, TenantId), FK_StockBalance_Warehouse (WarehouseId, TenantId), FK_StockBalance_Bin (WarehouseBinId, WarehouseId) y FK_StockBalance_Lot (LotId, ProductId).
- + CONSTRAINT CK_StockBalance_Qty CHECK (QtyOnHand >= 0 AND QtyReserved >= 0 AND QtyReserved <= QtyOnHand).
- + IX_StockBalance_Bin (WarehouseBinId).
- Diseño/logistica-db-estructura.sql · CAPA 8 dbo.InventoryTransaction:
- + ReasonLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId) (AdjustmentReason).
- FKs compuestas: FK_InvTxn_Product (ProductId, TenantId), FK_InvTxn_FromWh y FK_InvTxn_ToWh (…, TenantId), FK_InvTxn_FromBin (FromBinId, FromWarehouseId), FK_InvTxn_ToBin (ToBinId, ToWarehouseId), FK_InvTxn_Lot (LotId, ProductId) y FK_InvTxn_Serial (SerialId, ProductId).
- Comentario: 'Quantity con signo (maestro L331): + entra a To; − sale de From. TRANSFER + con From y To.'
- + CONSTRAINT CK_InvTxn_Quantity CHECK (Quantity <> 0).
- + CONSTRAINT CK_InvTxn_Direction CHECK (((Quantity > 0 AND ToWarehouseId IS NOT NULL) OR (Quantity < 0 AND FromWarehouseId IS NOT NULL AND ToWarehouseId IS NULL)) AND (FromBinId IS NULL OR FromWarehouseId IS NOT NULL) AND (ToBinId IS NULL OR ToWarehouseId IS NOT NULL)).
- + IX_InvTxn_Tenant_Date (TenantId, CreatedAtUtc), IX_InvTxn_Lot (filtrado) e IX_InvTxn_Serial (filtrado).
- Diseño/logistica-db-estructura.sql · CAPA 13B dbo.Supplier: + UQ_Supplier_IdTenant y + UX_Supplier_Name (TenantId, Name) WHERE IsActive = 1.
- Diseño/logistica-db-estructura.sql · CAPA 13B dbo.PurchaseOrder:
- FKs compuestas FK_PurchaseOrder_Supplier y FK_PurchaseOrder_Warehouse.
- + CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME().
- + CreatedBy INT NULL.
- + UQ_PurchaseOrder_IdTenant.
- Diseño/logistica-db-estructura.sql · CAPA 13B dbo.PurchaseOrderLine: + CK_POLine_Numbers (QtyOrdered > 0, QtyReceived >= 0, UnitCost >= 0) y + UQ_POLine_IdPo (PurchaseOrderLineId, PurchaseOrderId).
- Diseño/logistica-db-estructura.sql · CAPA 13B NUEVA dbo.PurchaseOrderShortageResolution:
- Columnas: PurchaseOrderShortageResolutionId INT IDENTITY PK, TenantId, PurchaseOrderId, PurchaseOrderLineId, ActionLookupId (ShortageAction), Quantity DECIMAL(16,3), ReasonLookupId NULL, Notes NVARCHAR(300) NULL, ReorderPurchaseOrderId NULL, InventoryTransactionId BIGINT NULL, CreatedAtUtc y CreatedBy.
- Restricciones: FK_PoShortage_Po (PurchaseOrderId, TenantId), FK_PoShortage_Line (PurchaseOrderLineId, PurchaseOrderId), FK_PoShortage_Reorder (ReorderPurchaseOrderId, TenantId) y CK_PoShortage_Qty (Quantity > 0).
- IX_PoShortage_Line.
- Diseño/logistica-db-estructura.sql · CAPA 14 dbo.Asn:
- FKs compuestas FK_Asn_Warehouse, FK_Asn_Client y FK_Asn_Po.
- + CK_Asn_Origin (ClientId IS NULL OR PurchaseOrderId IS NULL).
- + CreatedAtUtc y CreatedBy.
- + UQ_Asn_IdTenant.
- Diseño/logistica-db-estructura.sql · CAPA 14 dbo.AsnLine: + PurchaseOrderLineId INT NULL REFERENCES dbo.PurchaseOrderLine y + CK_AsnLine_Qty (ExpectedQty > 0).
- Diseño/logistica-db-estructura.sql · CAPA 14 dbo.ReceiptHeader:
- FKs compuestas FK_Receipt_Warehouse, FK_Receipt_Asn (AsnId, TenantId) y FK_Receipt_Dock (DockId, WarehouseId).
- + CreatedAtUtc, CreatedBy y ReceivedBy.
- + UQ_Receipt_IdTenant.
- + UX_Receipt_Asn (AsnId) WHERE AsnId IS NOT NULL AND IsActive = 1.
- + IX_Receipt_Tenant_Status.
- Comentario: ReceivedAtUtc = fecha de confirmación (Lote 10).
- Diseño/logistica-db-estructura.sql · CAPA 14 dbo.ReceiptLine:
- + ExpectedQty DECIMAL(16,3) NULL, SerialNumbersJson NVARCHAR(MAX) NULL y AdjustmentTxnId BIGINT NULL REFERENCES InventoryTransaction.
- FKs compuestas de lote y serie.
- + CK_ReceiptLine_Qty.
- + IX_ReceiptLine_Header.
- Diseño/logistica-db-estructura.sql · CAPA 14 dbo.WarehouseTask:
- WarehouseTaskId pasa a INT IDENTITY(1,1) PRIMARY KEY.
- FKs compuestas FK_WhTask_Warehouse, FK_WhTask_Product, FK_WhTask_FromBin y FK_WhTask_ToBin.
- + CreatedBy.
- + CK_WarehouseTask_Qty.
- + IX_WarehouseTask_Ref (RefEntityLookupId, RefId).
- Diseño/logistica-db-estructura.sql · CAPA 14 dbo.PickWave, dbo.PickTask, dbo.Carton y dbo.CartonLine: SIN cambios (D1). Comentario: el vacío de TenantId de Carton se resuelve con las olas.
- Diseño/logistica-db-estructura.sql · CAPA 14 dbo.CycleCount:
- + IsActive BIT NOT NULL DEFAULT 1.
- + CreatedBy, ReconciledAtUtc, ReconciledBy y RowVersion.
- FK_CycleCount_Warehouse (WarehouseId, TenantId).
- + UQ_CycleCount_IdTenant.
- Diseño/logistica-db-estructura.sql · CAPA 14 dbo.CycleCountLine:
- + CountedSerialsJson NVARCHAR(MAX) NULL.
- + ReconciledSystemQty DECIMAL(16,3) NULL (saldo en mano bloqueado al reconciliar, base del ajuste; D22).
- + SystemQtyChanged BIT NOT NULL DEFAULT 0.
- FK compuesta de lote.
- + UQ_CycleCountLine (CycleCountId, WarehouseBinId, ProductId, LotId).
- + CK_CycleCountLine_Qty CHECK (SystemQty >= 0 AND (CountedQty IS NULL OR CountedQty >= 0) AND (ReconciledSystemQty IS NULL OR ReconciledSystemQty >= 0)).
- Diseño/logistica-db-estructura.sql · CAPA 14 NUEVA dbo.PickBatch (tras CycleCountLine):
- Columnas: PickBatchId INT IDENTITY PK, PublicId, TenantId, WarehouseId, Number NVARCHAR(40), StatusCodeId (PickBatchStatus), TransportOrderId NULL, ClientInvoiceNumber NVARCHAR(40) NULL, CollectedAtUtc/By, PackedAtUtc/By, CancelledAtUtc/By, IsActive y RowVersion.
- Restricciones: UQ_PickBatch_Number (TenantId, Number), UQ_PickBatch_IdTenant, FK_PickBatch_Warehouse (WarehouseId, TenantId), FK_PickBatch_Order (TransportOrderId, TenantId) y CK_PickBatch_Packed.
- UX_PickBatch_Order (TransportOrderId) WHERE TransportOrderId IS NOT NULL AND IsActive = 1.
- IX_PickBatch_Tenant_Date.
- Diseño/logistica-db-estructura.sql · CAPA 14 NUEVA dbo.PickBatchLine:
- Columnas: PickBatchLineId INT IDENTITY PK, PickBatchId, ProductId, LotId NULL, SerialId NULL, FromBinId, Quantity DECIMAL(16,3) (magnitud), UnitCost DECIMAL(18,4) NULL, IssueTxnId BIGINT NOT NULL y ReversalTxnId BIGINT NULL.
- FKs compuestas de lote y serie.
- CK_PickBatchLine (Quantity > 0, UnitCost >= 0, serie → Quantity = 1).
- IX_PickBatchLine_Batch.
- Diseño/logistica-db-estructura.sql · CAPA 15 dbo.DockAppointment:
- + WarehouseId INT NOT NULL.
- FKs compuestas FK_DockAppt_Warehouse, FK_DockAppt_Dock (WarehouseDockId, WarehouseId), FK_DockAppt_Asn y FK_DockAppt_Trip.
- + CreatedAtUtc y CreatedBy.
- + CK_DockAppt_Window y CK_DockAppt_Ref.
- Diseño/logistica-db-estructura.sql · CAPA 15 dbo.CrossDockPlan:
- FKs compuestas FK_CdPlan_Warehouse y FK_CdPlan_StagingZone (StagingZoneId, WarehouseId).
- + CreatedBy y CompletedAtUtc.
- + UQ_CrossDockPlan_IdTenant.
- Diseño/logistica-db-estructura.sql · CAPA 15 dbo.CrossDockAllocation:
- TransportOrderId pasa a NOT NULL.
- + WarehouseTaskId INT NULL REFERENCES WarehouseTask.
- + InventoryTransactionId BIGINT NULL.
- + ConfirmedQty DECIMAL(16,3) NULL: cantidad cubierta al confirmar el recibo; NULL mientras el recibo está abierto; AllocatedQty − ConfirmedQty = faltante outbound (D29).
- + CreatedAtUtc y CreatedBy.
- + CONSTRAINT CK_CdAlloc_Qty CHECK (AllocatedQty > 0 AND (ConfirmedQty IS NULL OR (ConfirmedQty >= 0 AND ConfirmedQty <= AllocatedQty))).
- + IX_CdAlloc_ReceiptLine.
- Diseño/logistica-db-estructura.sql · VISTA dbo.vw_LotGenealogy (D32): se amplía conservando las columnas existentes en su orden y agregando al final:
- t.InventoryTransactionId;
- t.FromBinId, t.ToBinId;
- NetQuantity = CASE WHEN txn.InternalCode = 'TRANSFER' THEN 0 ELSE t.Quantity END;
- t.ReasonLookupId y reason.InternalCode AS Reason (LEFT JOIN dbo.LookupCode reason ON reason.LookupCodeId = t.ReasonLookupId);
- t.CreatedBy.
- Diseño/logistica-db-estructura.sql · PRINT final: ~136 tablas (incluye PurchaseOrderShortageResolution, PickBatch y PickBatchLine) + vista de genealogía.
- Diseño/logistica-db-seed.sql · 1) CATALOG DOMAINS: + AdjustmentReason, ShortageAction y PickBatchStatus.
- Diseño/logistica-db-seed.sql · 2) LOOKUP CODES, bloque '-- Lote 6':
- ('ZoneType','STAGING',…).
- AdjustmentReason (9) y ShortageAction (3).
- EntityType (69-78): RECEIPT, ASN, WAREHOUSE_DOCK, INVENTORY_SERIAL, WAREHOUSE_TASK, PICK_BATCH, DOCK_APPOINTMENT, CROSSDOCK_ALLOCATION, INVENTORY_TRANSACTION y STOCK_BALANCE.
- ('Capability','EDIT_PURCHASE_ORDER','Editar orden de compra','Edit purchase order').
- Diseño/logistica-db-seed.sql · 3) STATUS CODES, bloque '-- Lote 6':
- PickBatchStatus COLLECTED (PIPE 1, inicial), PACKED (PIPE 2) y CANCELLED (TERM 3).
- CANCELLED TERM en WarehouseTaskStatus, AppointmentStatus y AllocationStatus.
- SerialStatus SCRAPPED TERM.
- UPDATE idempotente: SerialStatus RESERVED y SHIPPED → @LAT (D16).
- Diseño/logistica-db-seed.sql · 3G) STATUS LATERAL ENTRY por defecto, bloque '-- Lote 6':
- PICK_BATCH: CANCELLED desde COLLECTED y PACKED.
- WAREHOUSE_TASK: CANCELLED desde PENDING e IN_PROGRESS.
- DOCK_APPOINTMENT: NO_SHOW y CANCELLED desde SCHEDULED.
- CROSSDOCK_ALLOCATION: CANCELLED desde PLANNED.
- ASN: CANCELLED desde EXPECTED.
- PURCHASE_ORDER: SIN regla, así que CANCELLED se permite desde DRAFT, SENT y PARTIAL (D47). Un comentario lo explica.
- Diseño/logistica-db-seed.sql · 3H) STATUS CAPABILITY por defecto (TenantId NULL), bloque nuevo '-- Lote 6, PURCHASE_ORDER': mismo MERGE que 3B-3E, con EDIT_PURCHASE_ORDER IsAllowed = 0 en PurchaseOrderStatus SENT, PARTIAL, RECEIVED y CANCELLED. El tenant la cambia desde /status/capabilities/PURCHASE_ORDER (D46).
- Diseño/logistica-db-seed.sql · 4) PERMISOS: + inventory.view, inventory.manage, inventory.adjust y warehouse.manage (58 en total).
5) Plantillas: WarehouseOperator, ReadOnly y Billing reciben inventory.view.
PRINT final: 'permisos (58)', capacidades (CONTRACT, TRANSPORT_ORDER, WORK_ORDER, TRIP, PURCHASE_ORDER) y entradas laterales (TRIP, ROUTE, PICK_BATCH, WAREHOUSE_TASK, DOCK_APPOINTMENT, CROSSDOCK_ALLOCATION, ASN).
0) Módulos sin cambio: CROSSDOCK sigue apagado para el tenant demo. El almacén demo lo siembra DemoTenantSeeder (C#), no el SQL, porque el tenant no existe en una BD limpia.

## Decisiones

1. DECISIÓN D1: las olas de picking por orden (PickWave/PickTask) y el packing en cartones (Carton/CartonLine) se DIFIEREN; sus tablas no se mapean. Motivo: CargoLine es 'paquete' (cotización, lectura de la orden, fuente Órdenes, capacidad de rutas), y el PATCH de paquetes da de baja las líneas. Recolección y empaque cubre los dos ejemplos reales del documento.

Consecuencias:
- QtyReserved solo lo mueve el cross-dock en este lote. InventoryLedger.ReserveAsync/ReleaseAsync y el CHECK ya quedan listos para las olas.
- El vacío de TenantId de Carton se anota para ese lote.
- Las líneas de producto por orden se exponen con IOrderInventoryLines (D45).
   - Alternativa: Construir las olas ya: líneas de producto en la orden, con filtro ProductId IS NULL en la cotización, las piezas, OrderReadService, la fuente Órdenes y la capacidad de rutas (5 o más archivos de los Lotes 3 y 5), más reserva, PickTask y cartones.
2. DECISIÓN D2: una sola vía de escritura del inventario (InventoryLedger), con StockBalance como proyección bloqueada. La conciliación reconstruye el saldo desde el ledger, y el smoke exige cero descuadres.
   - Alternativa: Mantener StockBalance con triggers SQL o una vista indexada sobre el ledger.
3. DECISIÓN D3 (injerto): InventoryTransaction.Quantity CON SIGNO, según el maestro L331 ('cada despacho escribe movimiento negativo; cada recepción, positivo'):
- RECEIPT +;
- ISSUE y CROSSDOCK −;
- ADJUSTMENT ± (To si es positivo, From si es negativo);
- TRANSFER + en una sola fila con From y To.
Los llamadores pasan la magnitud y el ledger fija el signo. CK_InvTxn_Quantity (<> 0) y CK_InvTxn_Direction impiden signos incoherentes. El Kárdex muestra la cantidad del ledger, y SignedQuantity aplica la perspectiva del filtro (TRANSFER es neutra sin filtro). La conciliación por producto es Σ Quantity sin TRANSFER.
   - Alternativa: Lo del plan ganador: Quantity > 0 siempre, con la dirección dada por From/To (CK_InvTxn_Quantity > 0) y el signo solo derivado en el Kárdex. Cada lectura necesita saber el tipo para interpretar la cantidad.
4. DECISIÓN D4: la varianza de recepción se asienta como RECEIPT por lo ESPERADO más ADJUSTMENT RECEIPT_VARIANCE (con su signo) por la diferencia, enlazado en ReceiptLine.AdjustmentTxnId. El stock neto es igual a lo que entró. Los recibos ciegos o de devolución no generan ajuste.
   - Alternativa: RECEIPT por lo recibido, con la varianza solo como dato de la línea (sin fila de ajuste), como el mock con INV_ADJUSTMENTS.
5. DECISIÓN D5: se confirma el recibo completo, no línea por línea. ReceiptLine no tiene estatus.
   - Alternativa: Confirmación por línea (POST .../lines/{lineId}/confirm), con StatusCodeId nuevo en ReceiptLine.
6. DECISIÓN D6: un ASN tiene un solo recibo activo (UX_Receipt_Asn).
- Recibir contra una PO genera un ASN nuevo por lo pendiente, así que una PO admite varias entregas.
- Eliminar un recibo OPEN nacido de una PO cancela su ASN; el de un cliente queda EXPECTED.
- ReceiptHeader.ReceivedAtUtc es la fecha de confirmación: cubre el 'doneDate' que pedirá Contabilización de compras (Lote 10).
   - Alternativa: Varios recibos por ASN con su pendiente propio, más una fecha de confirmación por línea.
7. DECISIÓN D7: motivo de ajuste como catálogo (AdjustmentReason, columna InventoryTransaction.ReasonLookupId), obligatorio en ADJUSTMENT.
- Tres motivos los asigna solo el sistema: RECEIPT_VARIANCE, COUNT_VARIANCE y PICK_BATCH_REVERSAL.
- Sin tabla InventoryAdjustment ni flujo de aprobación: el ajuste es un movimiento del ledger con motivo, usuario y fecha.
   - Alternativa: Motivo en texto libre (Notes), como el mock. O una tabla InventoryAdjustment con aprobación de supervisor antes de asentar.
8. DECISIÓN D8: tabla nueva PurchaseOrderShortageResolution.
- CLOSE y REORDER resuelven el faltante completo de la línea.
- MANUAL_ADJUSTMENT admite una cantidad parcial, con motivo y posición.
- La PO pasa a RECEIVED cuando ninguna línea tiene pendiente.
- Una PO cancelada ya no muestra faltantes.
   - Alternativa: Sin tabla: faltante derivado de QtyOrdered − QtyReceived y cierre de la PO completa. O CLOSE/REORDER parciales.
9. DECISIÓN D9: Compras mínimas entran en este lote: proveedores, y PO en borrador, enviada, recibida parcial/completa, cancelada y eliminada, con costo por línea. Una PO solo admite productos propios, sin productos repetidos. La recepción consume la PO a través de la costura IPurchaseOrderReceiving.
   - Alternativa: Diferir Compras a su propio lote y probar faltantes con PO sembradas por SQL.
10. DECISIÓN D10: la recolección toma su número del contador PACKBATCH (EMP-#####). Al empacar, la orden recibe ese mismo número como PackBatchNumber.
   - Alternativa: Contador propio PK-##### (Kind PICKBATCH, como el mock), con la orden con su propio EMP-#####.
11. DECISIÓN D11: 'Empacar' crea la orden con OrderService.CreateAsync (origen PICK_BATCH) en la misma transacción que marca el lote PACKED.
- Las líneas de la orden son PAQUETES sin ProductId.
- La factura se copia al lote.
- Permisos: warehouse.pick + orders.create.
- La numeración respeta Client.ClientAssignsOrderNumber y ClientAssignsInvoiceNumber.
   - Alternativa: Poner ProductId, lote y serie en las CargoLine de la orden, marcadas como ya recolectadas.
12. DECISIÓN D12: borrar desde Órdenes una orden nacida de una recolección responde 409; solo 'Recolección y empaque' la borra y restaura el inventario. CANCELAR esa orden NO restaura inventario: la mercancía regresa con un recibo RETURN.
   - Alternativa: Un efecto sobre OrderStatus que restaure el inventario al cancelar, o permitir borrarla desde Órdenes con reversa automática.
13. DECISIÓN D13: eliminar una recolección revierte cada línea con un ADJUSTMENT de entrada (PICK_BATCH_REVERSAL) a su posición original; si la posición está inactiva, 422. La serie vuelve a AVAILABLE y el ISSUE original no se toca.
   - Alternativa: Revertir con un RECEIPT de devolución, o hacia staging en vez de la posición original.
14. DECISIÓN D14: la recolección asigna posición y lote por FEFO: vencimiento, luego PICKING > RESERVE > REFRIGERATED > STAGING, sobre el disponible (en mano − reservado). Excluye QUARANTINE y CROSSDOCK. Los productos con serie se recolectan escaneando la serie.
   - Alternativa: Que el usuario escoja siempre posición y lote.
15. DECISIÓN D15: una recolección solo lleva productos de UN dueño. Si el dueño es un cliente 3PL, la orden del empaque debe ser de ese cliente.
   - Alternativa: Sin validación de dueño.
16. DECISIÓN D16: SerialStatus cambia en el seed: RESERVED y SHIPPED pasan a LATERAL y se agrega SCRAPPED terminal. Así AVAILABLE→SHIPPED→AVAILABLE es legal y queda con historial.
   - Alternativa: Mantener el seed y bloquear (422) las reversas y devoluciones de productos con serie.
17. DECISIÓN D17: la ubicación actual de la serie vive en InventorySerial.CurrentWarehouseId/CurrentBinId (FK compuesta). Solo el ledger las mantiene. StockBalance sigue sin columna de serie.
   - Alternativa: Derivar la ubicación del último movimiento de la serie en el ledger.
18. DECISIÓN D18: FKs compuestas en SQL en las cadenas (Id, TenantId), (Posición, Almacén) y (Lote/Serie, Producto). WarehouseBin gana WarehouseId y el código de posición es único por almacén.
   - Alternativa: Solo guardas de servicio, con código de posición único solo por zona.
19. DECISIÓN D19: WarehouseTaskId pasa de BIGINT a INT (StatusService, resolvers y RefId son int). InventoryTransactionId sigue BIGINT.
   - Alternativa: Mantener BIGINT y dejar las tareas sin historial de estatus.
20. DECISIÓN D20: estatus nuevos en el seed:
- dominio PickBatchStatus;
- CANCELLED en WarehouseTaskStatus, AppointmentStatus y AllocationStatus;
- entradas laterales para PICK_BATCH, WAREHOUSE_TASK, DOCK_APPOINTMENT, CROSSDOCK_ALLOCATION y ASN.
PURCHASE_ORDER NO lleva regla lateral para CANCELLED, porque se cancela desde DRAFT, SENT y PARTIAL (D47). CycleCount gana IsActive.
   - Alternativa: Baja lógica sin estatus para tareas, citas y asignaciones, y sin dominio propio para la recolección.
21. DECISIÓN D21: zona STAGING. El recibo exige una posición de recepción: por defecto, la primera activa de una zona STAGING (422 si no hay). No se crean saldos 'sin posición'. El tenant demo ya la trae (D49); un tenant nuevo la crea al dar de alta su almacén.
   - Alternativa: Crear automáticamente la zona STG y la posición STG-01 en cada alta de almacén, o permitir WarehouseBinId NULL como 'recibido sin ubicar'.
22. DECISIÓN D22 (injerto, bitácora L542): el conteo lo confirma quien tiene warehouse.count (el Operador de almacén ya lo tiene). Al reconciliar:
- se bloquea el saldo actual de cada línea y se ajusta CONTADO − SALDO ACTUAL;
- se guarda ReconciledSystemQty;
- se marca SystemQtyChanged cuando el saldo se movió desde la foto, para revisión.
Si lo contado es menor que lo reservado → 409, sin escribir nada. En serie, las faltantes se dan de baja, las nuevas o despachadas entran, y las de otra posición se transfieren. 'Refrescar' queda como opción para recontar.
Riesgo aceptado: si el conteo físico ocurrió ANTES de un movimiento posterior a la foto, el ajuste duplica ese movimiento. La marca lo hace visible.
   - Alternativa: Lo del plan ganador: exigir que ningún saldo contado se haya movido (409 con las líneas, sin escribir nada, y 'Refrescar' obligatorio) y reconciliar solo con inventory.adjust (supervisor).
23. DECISIÓN D23: mínimos como columnas de Product: MinQty (disponible total) y MinPickQty/MaxPickQty (reabasto de la posición preferida en PICKING). El reabasto corre bajo demanda, es idempotente por almacén y excluye productos con serie.
   - Alternativa: Tabla ReplenishmentRule por producto/almacén/posición, con reabasto automático tras cada salida.
24. DECISIÓN D24 (injerto, maestro L301): el putaway dirigido aproxima la ROTACIÓN con las salidas (ISSUE + CROSSDOCK) de los últimos 30 días. FAST si salidas30d > 0 y salidas30d ≥ existencia total; SLOW en otro caso. Orden de sugerencia: preferida, consolidar lote, consolidar producto, PICKING por alta rotación (solo FAST), reserva vacía, reserva con capacidad. El operador puede cambiar el destino al completar, dentro del mismo almacén.
   - Alternativa: Solo el tipo de zona, sin rotación (plan ganador); otro umbral (por ejemplo, top 20% de SKU por salidas); o destino obligatorio salvo warehouse.manage.
25. DECISIÓN D25: desactivar un producto se bloquea si tiene inventario EN MANO distinto de cero, recibos abiertos o tareas pendientes. Seguimiento, UoM y dueño son inmutables desde el primer movimiento.
   - Alternativa: Lectura literal de L326: bloquear solo si el disponible es mayor que cero, y permitir reclasificar.
26. DECISIÓN D26: la baja de un almacén es definitiva (INACTIVE terminal) y solo procede con el almacén vacío, sin reservas y sin documentos abiertos. Con un solo almacén activo se toma por defecto; con más, 400.
   - Alternativa: Baja reversible (lateral SUSPENDED) y selector de almacén siempre obligatorio.
27. DECISIÓN D27: cuatro permisos nuevos en WAREHOUSE (58 en total): inventory.view, inventory.manage, inventory.adjust y warehouse.manage. Se reutilizan los warehouse.* y purchasing.* existentes.
Plantillas: inventory.view para Operador de almacén, Solo lectura y Facturación; el resto de los nuevos solo para el Admin. El Operador recolecta pero no empaca (sin orders.create) y, con D22, sí reconcilia conteos.
   - Alternativa: Solo warehouse.*, o dar orders.create al Operador de almacén.
28. DECISIÓN D28: módulos por controlador: WMS_LOTSERIAL, PURCHASING y CROSSDOCK. 'Módulo apagado para Advance · demo' es una etiqueta de la interfaz: el backend responde 403 module_disabled hasta que el tenant enciende CROSSDOCK (con AAL2). Los muelles son estructura del almacén.
   - Alternativa: Un estado 'demo' en TenantModule que exponga el módulo en solo lectura sin encenderlo.
29. DECISIÓN D29 (injerto, maestro L316-L320): cross-dock en dos modos.
- (a) Sobre un recibo ABIERTO: se asigna contra lo recibido/esperado. Al confirmar, CrossDockRules.Split reparte FIFO: tareas CROSSDOCK con reserva por lo cubierto, PUTAWAY por el remanente, y ShortQty visible por asignación (el faltante outbound).
- (b) Sobre un recibo ya confirmado: se reduce la PUTAWAY pendiente y se reserva al asignar.
Lo asignado en staging queda RESERVADO. Mover = CROSSDOCK que sale del inventario, desde la pantalla o desde la cola. Cancelar libera la reserva y devuelve la cantidad a putaway.
   - Alternativa: Solo el modo (b) del plan ganador, sin reserva, y con el faltante outbound condicionado a que exista CargoLine con ProductId (hoy no hay).
30. DECISIÓN D30: citas de muelle.
- Sin solapamiento por muelle; sin fin se asumen 60 minutos.
- Dirección compatible con el tipo de muelle.
- ARRIVED ocupa el muelle; los cierres lo liberan.
- Horizonte: de ayer a +90 días.
- El motivo de un NO_SHOW va en el comentario del historial, sin penalización ni columna propia.
   - Alternativa: Solapamiento con aviso, sin efecto sobre el muelle; y una columna de motivo o penalización para NO_SHOW.
31. DECISIÓN D31: costos y precios se ven con inventory.view y en las fuentes de análisis.
   - Alternativa: Ocultar PurchaseCost, SalePrice y CostValue sin inventory.manage o purchasing.view.
32. DECISIÓN D32 (injerto): la API arma la genealogía por LINQ con filtro de tenant. vw_LotGenealogy se AMPLÍA para BI y SQL externo con InventoryTransactionId, FromBinId, ToBinId, NetQuantity (0 en TRANSFER), ReasonLookupId/Reason y CreatedBy, conservando las columnas existentes en su orden.
   - Alternativa: Mapear la vista como entidad sin llave con filtro de tenant y usarla también en la API; o dejarla sin cambios.
33. DECISIÓN D33: quedan fuera:
- plantillas de exportación (ACCT_TEMPLATES) y contabilización de compras y despachos (Lote 10); quedan los insumos ReceivedAtUtc, UnitCost congelados, IOrderInventoryLines y las fuentes RECEIPT/PICK_BATCH;
- conversión de unidades (ProductUom);
- búsqueda COD por número de orden (11B);
- Vehicle/Driver.HomeWarehouseId y Trip.OriginWarehouseId (siguen sin escribirse, aunque Warehouse ya está mapeado).
   - Alternativa: Modelar ya la tabla de plantillas de exportación con su selector en solo lectura, y agregar las navegaciones HomeWarehouse con validación en VehicleService/DriverService.
34. DECISIÓN D34: lotes y series.
- Un lote existente con otras fechas → 409.
- Serie: cantidad entera igual al número de series.
- Una serie en inventario no se recibe de nuevo.
- Una serie dada de baja no vuelve.
   - Alternativa: Sobrescribir las fechas del lote y aceptar series duplicadas como reingreso.
35. DECISIÓN D35: el costo se congela en PurchaseOrderLine.UnitCost y en PickBatchLine.UnitCost. El precio de venta NO se congela: IOrderInventoryLines devuelve el SalePrice vigente, y el Lote 10 decide al facturar.
   - Alternativa: Leer Product.PurchaseCost vivo al contabilizar, o congelar también SalePrice en PickBatchLine.
36. DECISIÓN D36: cantidades en la unidad base con hasta 3 decimales; enteras con serie. Sin ProductUom.
   - Alternativa: Solo enteros en todo el WMS.
37. DECISIÓN D37: inventario insuficiente = 409 'insufficient_stock' con Errors por línea. La operación completa se revierte, incluido el número EMP.
   - Alternativa: 422 status_rule, o recolección parcial con aviso.
38. DECISIÓN D38: topes técnicos: 200 líneas por recibo, 100 por recolección, 500 series por línea, 1000 líneas por conteo, 200 filas por página.
   - Alternativa: Sin topes.
39. DECISIÓN D39: la lista de Recolecciones excluye las eliminadas por defecto (includeDeleted=true las muestra). Las fechas se filtran en UTC.
   - Alternativa: Mostrar siempre las eliminadas y filtrar por la fecha local del tenant.
40. DECISIÓN D40: las transferencias entre almacenes son instantáneas: una fila TRANSFER, sin 'en tránsito'.
   - Alternativa: Transferencia en dos pasos con inventario en tránsito, reutilizando ASN y recibo.
41. DECISIÓN D41 (injerto): cola unificada con IWarehouseTaskHandler registrado por tipo. Cada handler declara su permiso y si se completa desde la cola:
- PUTAWAY → warehouse.receive;
- REPLENISH → warehouse.pick;
- CROSSDOCK → warehouse.crossdock (se completa desde la cola = Mover);
- COUNT → warehouse.count (solo desde Conteo cíclico).
Un tipo sin handler (PICK, PACK, LOAD) responde 422 hasta que su lote registre el suyo, sin tocar WarehouseTaskService. Asignar y cancelar requieren warehouse.manage.
   - Alternativa: Un switch fijo por tipo en WarehouseTaskService (plan ganador), con CROSSDOCK solo desde su pantalla; o un permiso único warehouse.tasks.
42. DECISIÓN D42 (vacío): se DIFIERE el filtro por almacén de UserDataScope, que docs/lote1-decisiones.md:59 dejó para los Lotes 2 y 6 (el Lote 2 tampoco aplicó el de cliente). Un usuario con alcance de almacén ve todos los almacenes del tenant. El alcance por dueño para el Portal sí queda como costura (InventoryScope, D44).
   - Alternativa: Aplicarlo ya: WmsResolve y todas las consultas WMS filtran por los almacenes del UserDataScope del usuario (404 fuera del alcance), con pruebas y un paso de smoke.
43. DECISIÓN D43 (vacío): la recolección ad hoc tiene tablas propias PickBatch/PickBatchLine. Sostienen el listado y sus filtros, el número y la factura guardados en el lote, la reversa por línea, el costo congelado y la fuente PICK_BATCH.
   - Alternativa: Modelarla solo sobre InventoryTransaction (Ref PICK_BATCH) + TransportOrder.SourceEntity, sin tabla intermedia (listado y reversa derivados del ledger).
44. DECISIÓN D44 (injerto): InventoryScope(OwnerClientId), con el mismo patrón que OrderScope, en las lecturas de productos, lotes, series, saldos, Kárdex, genealogía, rastro de serie y ASN. En este lote todos los controladores pasan Any; el Portal (Lote 8) pasará el cliente. Las escrituras no reciben scope todavía.
   - Alternativa: No agregar scope ahora y que el Lote 8 reescriba las consultas o cree servicios de lectura propios del Portal.
45. DECISIÓN D45 (injerto): IOrderInventoryLines expone las líneas de producto por orden desde PickBatchLine (producto, lote, serie, cantidad, costo congelado y precio vigente). El Lote 10 facturará PRODUCT_SALE (13B L464) sin ProductId en las CargoLine de paquete. Las asignaciones de cross-dock (mercancía de terceros) no se incluyen.
   - Alternativa: Poner ProductId en CargoLine al empacar (el maestro lo sugiere para la venta a farmacia), filtrando en cotización y piezas.
46. DECISIÓN D46 (injerto): la edición de la OC se protege con la capacidad EDIT_PURCHASE_ORDER, sembrada en StatusCapability y negada en SENT, PARTIAL, RECEIVED y CANCELLED, más StatusService.EnsureAllowedAsync (convención de CLAUDE.md). El tenant puede habilitarla en SENT o PARTIAL; aun así, las líneas con recepciones no se eliminan, no bajan de lo recibido y no cambian de costo.
   - Alternativa: Regla pura fija IsEditable = DRAFT (plan ganador), sin configuración por tenant.
47. DECISIÓN D47 (injerto, maestro L461): una OC se CANCELA desde DRAFT, SENT o PARTIAL, con comentario en el historial ('una vez el recibo arrancó, la baja es cancelación con bitácora'). Se rechaza si tiene un recibo OPEN (409). Se ELIMINA solo en DRAFT, SENT o CANCELLED y sin recepciones confirmadas ni recibo abierto. Una cancelada con recepciones se conserva. Cancelar deja de mostrar su faltante.
   - Alternativa: Cancelar solo desde DRAFT/SENT (regla lateral del plan ganador) y que una PARTIAL se cierre resolviendo sus faltantes con CLOSE.
48. DECISIÓN D48 (injerto): 'Recolectar' saca el número EMP (EnsureAsync fuera y NextAsync como PRIMER bloqueo de la transacción), inserta la cabecera y después contabiliza los ISSUE con Ref PICK_BATCH+id en el INSERT. El ledger nunca recibe UPDATE. Es una excepción documentada al orden de bloqueo (PACKBATCH antes de saldos, sin ciclo posible). Las altas de órdenes esperan a lo sumo la duración de una recolección, y un faltante revierte también el número (EMP sin huecos).
   - Alternativa: Insertar la cabecera con un número provisional y asignar el EMP al final (el contador se retiene menos tiempo, a costa de un UPDATE de PickBatch); o el SetRef diferido del plan ganador (UPDATE del RefId en InventoryTransaction).
49. DECISIÓN D49 (injerto): DemoTenantSeeder siembra, idempotente por código, el almacén ALM-01 con zonas STG (STAGING, STG-01), PCK, RSV y QUA, posiciones y muelles D1/D2, sin inventario. Así el tenant demo recibe desde el primer arranque. Consecuencia: el smoke ve al menos un almacén previo, y las aserciones de 'almacén por defecto' son condicionales.
   - Alternativa: No sembrar nada (el tenant demo crea su almacén a mano), o sembrar también productos e inventario de ejemplo (medidores de glucosa).

## Pruebas unitarias

- tests/Teikem.Tests/WmsCatalogTests.cs (P0): constantes contra los literales del seed.
- Estatus WMS con PickBatchStatus; CANCELLED en tareas, citas y asignaciones; SerialStatus SCRAPPED y el UPDATE de RESERVED/SHIPPED a LATERAL.
- ZoneType STAGING, AdjustmentReason (9), ShortageAction (3) y los 10 EntityTypes nuevos.
- 58 permisos y plantillas; mapas OwnerRead y OwnerWrite.
- IsKnownKind y CK_NumberSequence_Kind.
- Bloque 3G SIN regla lateral para PURCHASE_ORDER.
- Capacidad EDIT_PURCHASE_ORDER en LookupCode y bloque 3H negado en SENT, PARTIAL, RECEIVED y CANCELLED.
- Capabilities.EditPurchaseOrder.
- La estructura contiene: CK_StockBalance_Qty, CK_InvTxn_Quantity con 'Quantity <> 0', CK_InvTxn_Direction con los dos brazos de signo, UX_Receipt_Asn, UX_PickBatch_Order, UQ_WarehouseBin_WhCode, FK_StockBalance_Bin, 'WarehouseTaskId INT IDENTITY', ReconciledSystemQty, SystemQtyChanged, ConfirmedQty, las tablas nuevas y vw_LotGenealogy con FromBinId, ToBinId, NetQuantity, Reason y CreatedBy.
- tests/Teikem.Tests/FleetCatalogTests.cs, OrderCatalogTests.cs y TripCatalogTests.cs (P0, SE AJUSTAN): el conteo pasa de 54 a 58 permisos, y 'permisos (58)' en el PRINT.
- tests/Teikem.Tests/WmsContractsTests.cs (P0), por reflexión:
- firmas posicionales exactas de todos los records (incluidos CycleCountLineDto con ReconciledSystemQty/SystemQtyChanged/AdjustedQty, CrossDockAllocationDto con ConfirmedQty/ShortQty, PurchaseOrderDto con CanEdit/CanCancel/CanDelete, WarehouseTaskDto con CompletableFromQueue y PutawaySuggestionDto con RotationClass);
- todos sealed; los +Extra presentes;
- ningún request con TenantId, InventoryScope ni id int de encabezado con PublicId;
- InventoryScope tiene Any con OwnerClientId null;
- los tipos de WmsSeams (incluidas las 4 interfaces de costura) fuera de Contracts;
- OrderDetailDto sin cambios.
- tests/Teikem.Tests/InventoryRulesTests.cs (P0):
- ValidateQuantity y ValidateTracking con mensajes exactos.
- ValidatePosting por tipo sobre la magnitud.
- StoredQuantity: RECEIPT +10, ISSUE −3, TRANSFER +2, ADJUSTMENT de salida −1.
- Rebuild por clave con esas filas → saldos exactos; lote NULL frente a lote X.
- NetByProduct = 10 − 3 − 1 = 6 (TRANSFER no cuenta).
- CostValue y Round4.
- InsufficientStockMessage.
- tests/Teikem.Tests/StockAllocatorTests.cs (P0):
- FEFO, zonas y exclusiones.
- Reparto 5 + 2.
- Shortfall.
- El disponible ya descuenta lo reservado.
- Determinismo.
- tests/Teikem.Tests/SerialRulesTests.cs (P0): Normalize, TargetStatus por tipo y signo, y mensajes exactos.
- tests/Teikem.Tests/PutawayRulesTests.cs (P0):
- Classify: 0 salidas → SLOW; salidas ≥ existencia → FAST; salidas < existencia → SLOW.
- Orden de razones con FAST (PICKING_FAST antes que RESERVE_EMPTY) y con SLOW (sin PICKING salvo preferida o consolidación).
- Preferida sin capacidad; CONSOLIDATE_LOT antes que CONSOLIDATE.
- Exclusiones; REFRIGERATED solo como preferida o para consolidar; peso NULL.
- Determinismo.
- tests/Teikem.Tests/PurchaseStatusRulesTests.cs (P0): Pending con clamp, IsReceivable, NothingPending, StatusAfterReceipt y StatusAfterResolution.
- tests/Teikem.Tests/InventoryLedgerTests.cs (P0, InMemory con WmsFixture):
- Básicos: RECEIPT guarda Quantity +; ISSUE guarda Quantity − con Ref en el INSERT; ISSUE insuficiente → 409 sin cambios; TRANSFER + con From/To; ADJUSTMENT sin motivo → 400.
- Entradas inválidas: producto inactivo → 422; posición inactiva → 422.
- Series: nacimiento AVAILABLE; ISSUE de otra posición → 409; reversa SHIPPED→AVAILABLE.
- Reservas: ReserveAsync por encima del disponible → 409; Reserve 3 de 5 → disponible 2 y un ISSUE de 3 sin FromReserved → 409; ISSUE de 3 con FromReserved → en mano 2 y reservado 0; ReleaseAsync de más → 409.
- Conciliación: ReconcileAsync sin descuadres y con 1 descuadre forzado.
- Ids en el orden de entrada.
- tests/Teikem.Tests/WmsWriteConfinementTests.cs (P0), por texto sobre src/:
- escrituras de StockBalance (QtyOnHand y QtyReserved), InventoryTransaction, CurrentBinId y SerialStatus solo en InventoryLedger.cs;
- sin Remove sobre el ledger;
- ningún '.RefId =' sobre un InventoryTransaction fuera de la construcción en el ledger (no hay SetRef);
- nadie lee QtyAvailable, VarianceQty ni LineTotal en la lógica.
- tests/Teikem.Tests/TenantIsolationModelTests.cs (P0, SE EXTIENDE):
- 15 entidades WMS con filtro y exactamente 11 hijas sin TenantId;
- computadas, RowVersion, índices espejo con filtro y precisiones;
- ReconciledSystemQty y ConfirmedQty en decimal(16,3); SystemQtyChanged bool;
- WarehouseTaskId int; InventoryTransactionId long;
- DateOnly;
- GeoPoint sin mapear.
- tests/Teikem.Tests/RawSqlConfinementTests.cs (P0, SE EXTIENDE): InventoryQueries.cs permitido; las 17 sentencias con 'TenantId = {tenantId}'; upsert, bloqueos, rangos, encabezados, muelle, series y EnsureLot presentes; la de Warehouse sin 'SELECT *'; sin SqlRaw.
- tests/Teikem.Tests/OwnedEntityResolverCoverageTests.cs (P0, SE AJUSTA) y WmsOwnedEntityResolversTests.cs (P0, InMemory con dos tenants): 11 resolvers reales y 3 cerrados; WAREHOUSE_DOCK por JOIN.
- tests/Teikem.Tests/AnalyticsSeedFieldsTests.cs (P0, SE EXTIENDE):
- campos válidos de las 7 fuentes en vistas, indicadores y gráficos del Lote 6, incluidas 'Productos por cliente dueño' (filtro IsActive) y 'Movimientos por tipo' (GroupJson by TxnType con SUM Quantity);
- DateField por fuente;
- IsMoney.
- tests/Teikem.Tests/WarehouseRulesTests.cs (P1): NormalizeCode, ComposeBinCode, DefaultWarehouse, muelle manual y mensajes.
- tests/Teikem.Tests/ProductRulesTests.cs (P2): NormalizeSku, Money, mínimos, Immutable, desactivación, ciclo, profundidad y CategoryPath.
- tests/Teikem.Tests/KardexRulesTests.cs (P3):
- SignedQuantity sin filtro = cantidad del ledger (RECEIPT +, ISSUE −, CROSSDOCK −, ADJUSTMENT ±) y TRANSFER 0;
- con filtro: salida −, entrada + e interna 0;
- Position; RefLabel con fallback; chips.
- tests/Teikem.Tests/AdjustmentRulesTests.cs (P3): ToPosting (+3 → To, −2 → From, 0 → 400), motivos de sistema, series y misma posición.
- tests/Teikem.Tests/InventoryAdjustmentServiceTests.cs (P3, InMemory):
- ajuste +3 con FOUND → saldo y Kárdex +3;
- −2 con DAMAGE → Kárdex −2;
- −1000 → 409 sin cambios;
- entrada con lote nuevo → EnsureLot;
- transferencia entre almacenes = 1 fila;
- transferencia de lo reservado → 409.
- tests/Teikem.Tests/InventoryReadServiceTests.cs (P3, InMemory):
- InventoryScope(cliente A) en saldos, Kárdex, genealogía y rastro: solo productos de A, y 404 en la genealogía de un lote de otro dueño;
- Any ve todo;
- SignedQuantity con filtro de posición;
- Categoría con subcategoría.
- tests/Teikem.Tests/ReceiptPostingRulesTests.cs (P4): matriz completa, incluida SERIAL. Propiedad: el neto CON SIGNO (RECEIPT + ADJUSTMENT) es igual a lo recibido, sobre 200 casos generados.
- tests/Teikem.Tests/ReceiptRulesTests.cs (P4): precarga R8, ReceiptNotOpen, AsnBusy, NoStagingBin, staging, OwnerMismatch, OwnProductsOnly, AsnLineNotRemovable, LineHasCrossDock, tope 200 y TypeFor.
- tests/Teikem.Tests/ReceiptServiceTests.cs (P4, InMemory, con fakes de IPurchaseOrderReceiving e IReceiptConfirmationParticipant):
- confirmar esperado 10 / recibido 8 → RECEIPT +10, ADJUSTMENT −2, AdjustmentTxnId enlazado, ASN RECEIVED y ApplyReceiptAsync llamado con 8;
- segunda confirmación → 422;
- participante que toma 3 de 8 → PUTAWAY por 5;
- participante que toma todo → recibo directo a PUTAWAY;
- borrar un recibo OPEN de PO → su ASN CANCELLED.
- tests/Teikem.Tests/WarehouseTaskRulesTests.cs (P5): NoHandler, TaskNotOpen, QtyExceeds, DestinationRequired, Split y orden de la cola.
- tests/Teikem.Tests/ReplenishmentRulesTests.cs (P5): objetivo, límite de reserva, FEFO desde RESERVE, omisiones y una tarea por asignación.
- tests/Teikem.Tests/WarehouseTaskStatusEffectTests.cs (P5, InMemory): última PUTAWAY → recibo PUTAWAY; otra abierta → sin cambio; CANCELLED terminal; recibo fuera de RECEIVED → sin cambio; REPLENISH → sin cambio.
- tests/Teikem.Tests/CycleCountRulesTests.cs (P6): SelectLines con tope, IsStale, Variance frente a Adjustment (contra el saldo actual), SerialVariance y los mensajes ReservedAboveCount, CountIncomplete y SerialCountedByList.
- tests/Teikem.Tests/CycleCountServiceTests.cs (P6, InMemory):
- foto 8, contado 9, ISSUE de 1 → reconciliar ajusta +2 (9 − 7), SystemQtyChanged=true y ReconciledSystemQty=7;
- sin movimiento → SystemQtyChanged=false;
- contado menor que lo reservado → 409 sin efectos;
- doble reconciliación → 422;
- tarea COUNT DONE.
- tests/Teikem.Tests/PickBatchRulesTests.cs (P7): SingleOwner, OrderClientMustBeOwner, CanDelete, DisplayNumbers, filtros antes de la búsqueda y tope 100.
- tests/Teikem.Tests/OrderPickBatchGuardTests.cs (P7): CanDeleteFromOrders con y sin opción y default de OrderDeletionOptions.
- tests/Teikem.Tests/OrderInventoryLinesTests.cs (P7, InMemory): lote PACKED → líneas con UnitCost congelado y SalePrice vigente; COLLECTED, CANCELLED y líneas revertidas excluidas; orden de otro tenant → vacío.
- tests/Teikem.Tests/PurchaseOrderRulesTests.cs (P8):
- CanCancel en DRAFT, SENT y PARTIAL, pero no en RECEIVED;
- IsDeletable con y sin recepciones, con recibo abierto y CANCELLED con recepciones (los dos mensajes);
- ReceivedLineLocked;
- línea repetida; costo por defecto y obligatorio;
- Total Round4.
- tests/Teikem.Tests/ShortageRulesTests.cs (P8): Pending, CLOSE/REORDER completos, ManualExceeds, NoPendingShortage, UnknownAction, PoNotReceivedYet, PoCancelled y PendingCost.
- tests/Teikem.Tests/PurchaseOrderReceivingTests.cs (P8, InMemory): LockForReceiptAsync con PO DRAFT → 422, sin pendiente → NothingPending y SENT → líneas pendientes; ApplyReceiptAsync parcial → PARTIAL y completo → RECEIVED, con historial.
- tests/Teikem.Tests/DockScheduleRulesTests.cs (P9): semiabierto, 60 minutos, compatibilidad, ventana, horizonte y ASN+Trip.
- tests/Teikem.Tests/CrossDockRulesTests.cs (P9):
- AllocatableOpen y AllocatableConfirmed.
- Split: recibido 10 con asignaciones 4 y 8 → confirmadas 4 y 6, ShortQty 2, remanente 0; recibido 15 → 4 y 8, remanente 3; recibido 0 → todo faltante.
- Mensajes Exceeds, ReceiptNotConfirmed, NothingConfirmed y ExactQty.
- CanComplete.
- tests/Teikem.Tests/DockAppointmentStatusEffectTests.cs (P9, InMemory): ARRIVED ocupa; COMPLETED libera; otra llegada activa lo impide; MAINTENANCE intacto; NO_SHOW y CANCELLED liberan.
- tests/Teikem.Tests/WmsControllerSecurityTests.cs (P10): mapa exacto de permisos, [RequireModule] de clase, sin 'tenantId' y todas las acciones mapeadas.
- tests/Teikem.Tests/WarehouseTaskHandlerCoverageTests.cs (P10):
- handlers exactamente para PUTAWAY (warehouse.receive), REPLENISH (warehouse.pick), COUNT (warehouse.count, NotFromQueueMessage no nulo) y CROSSDOCK (warehouse.crossdock), uno por tipo;
- los cuatro registrados en DependencyInjection.cs.

## Pasos de smoke

- db-init dos veces sobre BD limpia: 58 permisos y la segunda corrida idempotente. Operador de almacén, Solo lectura y Facturación reciben inventory.view; nadie más recibe inventory.adjust, inventory.manage ni warehouse.manage.
- catálogos y estatus (Lote 6):
- /me del admin con los 4 permisos nuevos.
- GET /status de los 11 dominios WMS: SerialStatus con RESERVED/SHIPPED LATERAL y SCRAPPED TERMINAL; CANCELLED en tareas, citas y asignaciones.
- /catalogs: ZoneType con STAGING; AdjustmentReason 9; ShortageAction 3.
- /status/lateral-entries: PICK_BATCH (CANCELLED desde COLLECTED y PACKED); PURCHASE_ORDER sin reglas.
- /status/capabilities/PURCHASE_ORDER: EDIT_PURCHASE_ORDER negada en SENT, PARTIAL, RECEIVED y CANCELLED.
- almacén demo (Lote 6): GET /warehouses contiene ALM-01 ACTIVE, con zonas STG (STAGING, con STG-01), PCK, RSV y QUA, muelles D1/D2 FREE, y saldos vacíos.
- almacenes (Lote 6):
- W1$TS → 200 ACTIVE con historial; código repetido → 409; PATCH code → 400.
- Zonas STG, PCK, RSV, QUA y XD; ZoneType 'FOO' → 400.
- Posición compuesta 'A01-R01-N1-P01'; mismo código en otra zona del almacén → 409.
- Muelles D1 INBOUND, D2 OUTBOUND y D3 BOTH.
- W2$TS.
- Operación sin warehousePublicId con más de un almacén activo → 400 'Indique el almacén: la compañía tiene más de uno.' (condicional).
- productos (Lote 6):
- Categorías y ciclo → 400.
- PN (NONE, costo 12.3456, preferida PCK, MinPickQty 5 / MaxPickQty 8), PL (LOT, MinQty 50), PS (SERIAL) y P3 (dueño cliente A).
- SKU repetido por dueño → 409; costo negativo → 400; código de barras repetido → 409; posición preferida de otro almacén → 400.
- ownerName e isOwn en la lista.
- compras (Lote 6):
- Proveedor; PO1 con PN 10 (lineTotal 123.456) y PL 5 @ 2.5; PO con P3 → 400.
- PATCH en DRAFT → 200. Enviar → SENT; canEdit=false.
- PATCH en SENT → 422 (capacidad EDIT_PURCHASE_ORDER negada).
- recepción contra PO (Lote 6):
- POST /receipts {purchaseOrderPublicId: PO1, stagingBinId: STG-01} → OPEN, REC-#####, receivedQty == expectedQty. Operador sin purchasing.receive → 403.
- PN receivedQty 8; PL con lote L1$TS.
- Confirmar con PL sin lote → 400. Confirmar → RECEIVED.
- Kárdex (ref RECEIPT): PN quantity +10 y ADJUSTMENT quantity −2 con RECEIPT_VARIANCE; PL +5.
- Saldos 8 y 5; PO1 PARTIAL; 2 PUTAWAY sugeridas.
- Segundo confirm → 422.
- Otro recibo confirmado DOS veces en paralelo → un 200 y un 422, con una sola tanda de RECEIPT.
- recepción ciega, con serie y ASN de cliente (Lote 6):
- BLIND PS 3 con S1/S2/S3$TS → sin ADJUSTMENT. 2 series para 3 → 400; 1.5 → 400; serie repetida → 400.
- S1 otra vez → 409.
- ASN de A con P3; con PN → 400.
- Segundo recibo sobre el mismo ASN → 409; eliminar el primero → 204; ahora el segundo → 200.
- Un recibo OPEN contra PO2 eliminado → su ASN queda CANCELLED.
- putaway y cola de tareas (Lote 6):
- GET PUTAWAY con completableFromQueue=true; la sugerencia de PN trae reasonCode PREFERRED y rotationClass.
- Asignar a un usuario de otro tenant → 400.
- Completar PN → TRANSFER (quantity +, ref WAREHOUSE_TASK). Completar hacia una posición de W2 → 404.
- Doble completado en paralelo → un 200 y un 422.
- Última PUTAWAY → recibo en PUTAWAY.
- Usuario 'lectura$TS' al completar → 403 + PERMISSION_DENIED.
- reabasto (Lote 6): transferencia de 6 PCK→RSV (quedan 2); run → 1 REPLENISH por 6; segunda corrida → 0 tareas y skippedWithOpenTask 1; completar → PCK 8.
- ajustes y transferencias (Lote 6):
- Sin motivo → 400; RECEIPT_VARIANCE → 400 'lo asigna el sistema'.
- −2 DAMAGE → ADJUSTMENT con quantity −2.
- −1000 → 409 insufficient_stock con el mensaje exacto.
- Transferencia a la misma posición → 400; de W1 a W2 → una fila TRANSFER.
- PS −1 con S3 → SCRAPPED; +1 con S3 → 409.
- Sin inventory.adjust → 403.
- conteo cíclico (Lote 6, D22):
- CC-##### sobre PCK, en modo informado, con tarea COUNT (completableFromQueue=false). Completar la COUNT desde la cola → 422 'Las tareas de conteo se completan desde Conteo cíclico.'
- Capturar PN systemQty+1 y otra línea systemQty−1. Recolectar 1 de PN.
- Reconciliar con el Operador de almacén (warehouse.count) → 200 RECONCILED:
  - la línea de PN con systemQtyChanged=true y reconciledSystemQty = foto−1;
  - ADJUSTMENT COUNT_VARIANCE = contado − saldo actual (+2);
  - la otra línea con systemQtyChanged=false y −1.
- Reconciliar con el despachador (sin warehouse.count) → 403.
- Conteo con contado menor que lo reservado (posición de staging con cross-dock asignado, tras el paso de cruce de muelle) → 409 'El conteo de … es menor que lo reservado (…); …', sin cambios en el Kárdex.
- Tarea COUNT DONE; capturar después → 422.
- recolección (Lote 6, D48):
- PN 2 y PL 1 FEFO → COLLECTED EMP-#####; 2 ISSUE con quantity negativa y refId == id del lote desde el INSERT.
- PN + P3 → 400; PS con S2 → S2 SHIPPED.
- PN 9999 → 409 sin efecto parcial, y la SIGUIENTE recolección exitosa lleva el EMP consecutivo a la anterior (el fallido no consumió número).
- Filtros from/to/status/productPublicIds y búsqueda final.
- empacar (Lote 6):
- B1 con cliente B, consignatario, STANDARD, 1 caja, COD 25 y FAC$TS → PACKED, con displayNumbers; la orden con packBatchNumber == número de B1.
- Filtros invoiceNumber y orderNumber.
- Empacar de nuevo → 422; B-P3 con el cliente B → 400; especial → 400; Operador → 403.
- Doble empaque en paralelo → una orden.
- Sin factura → autogenerada y guardada en el lote.
- eliminar recolección (Lote 6):
- DELETE /orders de la orden de B1 → 409 con el mensaje exacto.
- DELETE /pick-batches/B1 → 204: orden inactiva, ADJUSTMENT PICK_BATCH_REVERSAL positivos y saldos exactos a los de antes.
- Orden confirmada → 422 DeleteBlocked.
- PS con S2 → S2 AVAILABLE.
- Dos DELETE en paralelo → un 204 y un 404.
- includeDeleted.
- concurrencia de inventario (Lote 6):
- PC$TS con 5 unidades: 8 recolecciones simultáneas de 1 → exactamente 5 × 200 y 3 × 409, saldo 0, 5 ISSUE, y los 5 EMP nuevos consecutivos sin huecos.
- 4 recibos ciegos simultáneos → 4 REC distintos y consecutivos.
- Desactivar PD$TS contra confirmar su recibo → nunca un producto inactivo con inventario.
- faltantes y cancelación de compras (Lote 6, D47):
- Faltantes: GET shortages → PO1 con PN pendiente 2. MANUAL 3 → 400 ManualExceeds(2). MANUAL 1 FOUND → ADJUSTMENT +1 (ref PURCHASE_ORDER). REORDER sin purchasing.manage → 403; con el admin → PO DRAFT nueva. Resolver de nuevo → 409. PO1 → RECEIVED.
- PO2 recibida corta: dos CLOSE en paralelo → un 200 y un 409.
- Cancelación: PO3 recibida parcial (PARTIAL) con un recibo OPEN → cancel → 409 HasOpenReceipt. Eliminar el recibo → cancel con comment → CANCELLED, con el comentario en el historial PURCHASE_ORDER. PO3 ya no aparece en shortages. DELETE PO3 → 409 'Una orden de compra cancelada con recepciones se conserva con su bitácora; no se elimina.'
- Cancelar PO1 (RECEIVED) → 422.
- Resolver un faltante sin inventory.adjust → 403.
- baja de producto y de almacén (Lote 6):
- PN con inventario → 409; a 0 → baja → 200; oculto en activeOnly y visible en saldos y Kárdex; recibo ciego con PN → 422; reactivar.
- Posición con stock → 409.
- W2 con stock → 409; vacío → INACTIVE; segunda baja → 422.
- cruce de muelle (Lote 6, módulo CROSSDOCK, D29):
- Módulo: apagado → 403 module_disabled; reauth AAL2 y encender.
- Citas: SCHEDULED en D1 09-10; 09:30 → 409; OUTBOUND en D1 → 400; dos solapadas en paralelo en D3 → un 200 y un 409; ARRIVED → OCCUPIED; COMPLETED → FREE.
- Plan XD con zona XD.
- Modo (a), recibo abierto: recibo ciego OPEN a XD-01 con PX 10 → asignar 4 a la orden O1 y 8 a O2 → 409/400 Exceeds si se pide más de 10 − 4. Asignar 6 a O2. Bajar receivedQty a 8 → confirmar:
  - O1 confirmedQty 4, O2 confirmedQty 4 con shortQty 2;
  - tareas CROSSDOCK por 4 y 4; ninguna PUTAWAY; el recibo pasa directo a PUTAWAY;
  - saldo XD-01 con qtyReserved 8.
  - Una recolección de PX → 409 insufficient_stock (lo reservado no se recolecta).
- Mover: la CROSSDOCK de O1 desde la COLA (POST /warehouse-tasks/{id}/complete) → CROSSDOCK con quantity −4 en el Kárdex, asignación MOVED y reservado 4. Completarla con quantity 3 → 400 ExactQty. Mover O2 desde el plan.
- Modo (b), recibo confirmado: otro recibo confirmado a XD-01 → asignar 3 → su PUTAWAY baja en 3 y reservado +3. Cancelar esa asignación → reserva liberada y PUTAWAY nueva por 3.
- Completar el plan → COMPLETED. Apagar CROSSDOCK.
- trazabilidad (Lote 6):
- Genealogía de L1 con RECEIPT, TRANSFER e ISSUE, destino a la orden, cliente y consignatario; qtyIn/qtyOut/qtyOnHand cuadran.
- Rastro de S2 con AVAILABLE→SHIPPED→AVAILABLE.
- Kárdex: types, fechas, binIds (perspectiva) y categoría con subcategoría; refLabel y userName.
- sin descuadre (Lote 6):
- GET /inventory/reconciliation → mismatches == [] después de TODO (incluidos los concurrentes, reversas y cross-dock).
- Σ qtyReserved de saldos == Σ confirmedQty de las asignaciones PLANNED (0 al final).
- Sin inventory.adjust → 403.
- análisis (Lote 6):
- /analytics/sources con las 7 fuentes.
- Vistas 'Inventario', 'Inventario bajo mínimo', 'Productos por cliente dueño' (solo activos), 'Kárdex de movimientos', 'Movimientos por tipo' (agrupada: la fila ISSUE con suma negativa y la RECEIPT positiva) y 'Ajustes de inventario'.
- 7 indicadores y 6 gráficos.
- 'Valor de inventario a costo' = Σ costValue de los saldos (4 decimales).
- 'Productos bajo mínimo' ≥ 1.
- aislamiento y BOLA (Lote 6):
- T3: 404 en almacén, producto, ajuste y pick con binId nuestro; listas, fuentes, custom-fields, contactos e historial sin datos nuestros.
- Mismo tenant: línea de otro recibo → 404; toBinId de W2 → 404; conteo con posición de W2 → 404; genealogía de un lote de otro tenant → 404.
- RBAC y módulo (Lote 6):
- 'lectura$TS': GET → 200; POST → 403 con PERMISSION_DENIED.
- Operador de almacén: recolectar → 200; empacar → 403; reconciliar conteo → 200 (warehouse.count, D22); ajustar → 403.
- WMS_LOTSERIAL apagado → 403 module_disabled y se vuelve a encender.
- PURCHASING apagado + recibo contra PO → 403 module_disabled y se vuelve a encender.
- auditoría (Lote 6): /audit con WAREHOUSE, PRODUCT, RECEIPT, PICK_BATCH y PURCHASE_ORDER; ninguna fila de AuditLog para StockBalance ni InventoryTransaction; historiales de estatus legibles con inventory.view y sin él → 403.

## Anexo: especificación extraída

### entidades

- **tabla**: Warehouse; **existeEnSql**: True; **notas**: L955. Sin entidad EF ni configuración en src/ todavía (Lote 5 lo dejó anotado: 'Warehouse no existe aún en código'). Vehicle.HomeWarehouseId y Driver.HomeWarehouseId ya tienen FK en SQL pero no mapeadas en EF.
- **tabla**: WarehouseZone; **existeEnSql**: True; **notas**: L971. ZoneTypeLookupId referencia catálogo 'ZoneType' (LookupCode) — verificar seed.
- **tabla**: WarehouseBin; **existeEnSql**: True; **notas**: L981. Jerarquía Almacén→Zona→Bin confirmada; código de ubicación (Aisle/Rack/Level/Position).
- **tabla**: WarehouseDock; **existeEnSql**: True; **notas**: L991. Tipado inbound/outbound vía DockTypeLookupId; usado por DockAppointment y Cross-dock.
- **tabla**: ProductCategory; **existeEnSql**: True; **notas**: L1005. Jerarquía por ParentId, usada en filtro 'Categoría' de Inventario/Conteo/Kárdex.
- **tabla**: Product; **existeEnSql**: True; **notas**: L1013. ClientId=dueño del inventario (NULL=propio de Advance). PurchaseCost/SalePrice/PreferredWarehouseId/PreferredBinId ya están. Falta añadir explícitamente en el flujo real la regla 'no se puede desactivar si hay inventario disponible' (L326) — no hay columna ni constraint que la exprese, es lógica de servicio.
- **tabla**: InventoryLot; **existeEnSql**: True; **notas**: L1032, tabla 'Lot' del documento = InventoryLot en SQL.
- **tabla**: InventorySerial; **existeEnSql**: True; **notas**: L1042. StatusCodeId Entity='SerialStatus' cubre 'disponible/reservado/despachado' (L328).
- **tabla**: StockBalance; **existeEnSql**: True; **notas**: L1051. QtyAvailable es columna computada persistida (QtyOnHand-QtyReserved), confirma L329.
- **tabla**: InventoryTransaction; **existeEnSql**: True; **notas**: L1067. RefEntityLookupId/RefId = ledger 'fuente de verdad' (L291, L309, L330). No tiene AdjustmentTxnId propio pero CycleCountLine sí lo referencia hacia InventoryTransaction (dirección inversa, correcto).
- **tabla**: WarehouseTask; **existeEnSql**: True; **notas**: L1789. TaskTypeLookupId cubre putaway/pick/pack/replenish/count/load/crossdock (L291, L308).
- **tabla**: PickWave; **existeEnSql**: True; **notas**: L1811. Ligado a TripId — soporta picking por olas atado a trip de salida (L302).
- **tabla**: PickTask; **existeEnSql**: True; **notas**: L1823. CargoLineId opcional, FromBinId obligatorio, StatusCodeId propio.
- **tabla**: Carton / CartonLine; **existeEnSql**: True; **notas**: L1846/L1857 aprox. Empaque real de esquema (Packing, L303) — distinto del 'PickBatch' ad hoc del mock.
- **tabla**: CycleCount / CycleCountLine; **existeEnSql**: True; **notas**: Conteo cíclico modo informado (L307, bitácora L542). VarianceQty computada, AdjustmentTxnId enlaza al ledger.
- **tabla**: DockAppointment; **existeEnSql**: True; **notas**: L1882 (Capa 15). Pendiente por el Lote 5 (anotado explícitamente en docs/lote5-decisiones.md: 'Cross-dock y DockAppointment (anotado para el Lote 6 en el plan)').
- **tabla**: CrossDockPlan; **existeEnSql**: True; **notas**: L1895. StagingZoneId referencia WarehouseZone — flujo inbound→staging→outbound (L319).
- **tabla**: CrossDockAllocation; **existeEnSql**: True; **notas**: L1907. Enlaza ReceiptLine con TransportOrder/CargoLine — emparejamiento inbound↔outbound (L318).
- **tabla**: PurchaseOrder / PurchaseOrderLine; **existeEnSql**: True; **notas**: L1710/L1727. QtyOrdered vs QtyReceived permite calcular el faltante, pero no hay tabla de 'resolución' persistente.
- **tabla**: ReceiptHeader / ReceiptLine; **existeEnSql**: True; **notas**: L1761/L1777. Falta columna equivalente a 'RECEIPTS[].doneDate' del mock (bitácora L767) para agrupar contabilización de despachos por fecha de confirmación — revisar si ReceivedAtUtc de ReceiptHeader ya cubre esa necesidad o si Lote 10 debe agregar algo a nivel de línea.
- **tabla**: vw_LotGenealogy; **existeEnSql**: True; **notas**: L2317 (vista). Reconstruye genealogía de lote (L331).
- **tabla**: TransportOrder; **existeEnSql**: True; **notas**: L1262+. PackBatchNumber NOT NULL (L1271) con índice único filtrado por IsActive (L1305); SourceEntityTypeLookupId/SourceEntityId (L1288-1289) preparados para enlazar con PICK_BATCH; ClientInvoiceNumber, OrderNumber, PackageNumber (revisar si esta última existe como columna — el documento (módulo 2, L242) dice que en el mock es un campo único por orden pero el esquema real necesitará una tabla de líneas de paquete).
- **tabla**: NumberSequence; **existeEnSql**: True; **notas**: L717-727. Kind incluye PACKBATCH (ClientId NULL, por tenant) — soporta la regla 'nunca lo asigna el cliente' (módulo 2, L241).
- **tabla**: Client (OrderNumberBy/InvoiceNumberBy/OrderNumberFormat/InvoiceNumberFormat/PackageNumberFormat); **existeEnSql**: True; **notas**: L694-696 y campos hermanos — soporta numeración configurable por cliente (bitácora L825-834, módulo 2 L240/L243). Verificar columnas OrderNumberBy/InvoiceNumberBy existen con esos nombres exactos en Client — no confirmado en esta lectura, revisar antes de mapear.
- **tabla**: ImportTemplate / ImportBatch; **existeEnSql**: True; **notas**: L1224/L1239. Ya generalizado por Kind — reutilizable para plantillas de importación de consignatarios si se agrega Kind='CONSIGNEE', pero esa pantalla (Portal/Catálogo) es de otro módulo; aquí solo aplica si el Lote 6 toca import de inventario/empaque, que el documento no describe explícitamente (el mock solo describe plantillas de EXPORTACIÓN de Contabilidad, no de importación de inventario).
- **tabla**: PurchaseOrderShortage / PO_ADJUSTMENTS (mock); **notas**: El mock (`PO_ADJUSTMENTS`, bitácora L762) registra resoluciones de faltante (Cerrar/Reordenar/Ajuste manual) por línea de PO, descontando de un pendiente acumulado. El SQL no tiene tabla equivalente — solo PurchaseOrderLine.QtyOrdered/QtyReceived, de donde se podría derivar el faltante pero no hay dónde persistir 'ya resuelto explícitamente' ni el motivo/acción tomada. VACÍO.
- **tabla**: AcctTemplate / ExportTemplate (mock ACCT_TEMPLATES); **notas**: El pedido explícito de Luis (bitácora L769) es que las plantillas de exportación sean 'configurables y guardables para uso futuro', reutilizado luego en Facturación/Liquidación (bitácora L808). No hay tabla en el SQL. Nota: la Contabilización de compras/despachos en sí es Lote 10 según el alcance dado, pero el motor de plantillas de exportación nace aquí conceptualmente ligado a 'Ajustes de inventario'/almacén — a definir en qué lote se modela la tabla.
- **tabla**: Warehouse; **existeEnSql**: True; **notas**: L955-968. Cols: WarehouseId, PublicId, TenantId, Code, Name, Line1/City/State/PostalCode, CountryLookupId (LookupCode Entity='Country'), GeoPoint GEOGRAPHY, StatusCodeId (StatusCode Entity='WarehouseStatus'), IsActive, RowVersion. UNIQUE(TenantId,Code).
- **tabla**: WarehouseZone; **existeEnSql**: True; **notas**: L971-978. Cols: WarehouseZoneId, WarehouseId FK, Code, Name, ZoneTypeLookupId NULL (LookupCode Entity='ZoneType'), IsActive. UNIQUE(WarehouseId,Code).
- **tabla**: WarehouseLocation; **notas**: No existe con ese nombre. La tabla real de posiciones físicas es dbo.WarehouseBin (L981-988): WarehouseBinId, WarehouseZoneId FK, Code, Aisle/Rack/Level/Position, MaxWeightKg, IsActive. UNIQUE(WarehouseZoneId,Code).
- **tabla**: WarehouseDock; **existeEnSql**: True; **notas**: L991-999. Cols: WarehouseDockId, WarehouseId FK, Code, DockTypeLookupId (Entity='DockType'), StatusCodeId (StatusCode Entity='DockStatus'), IsActive. UNIQUE(WarehouseId,Code).
- **tabla**: Product; **existeEnSql**: True; **notas**: L1013-1029. Cols: ProductId, PublicId, TenantId, ClientId NULL (FK Client), Sku, Name, ProductCategoryId NULL, BaseUomLookupId (Entity='UnitOfMeasure'), TrackingTypeLookupId (Entity='TrackingType'), WeightKg, VolumeM3, Barcode, PurchaseCost, SalePrice, PreferredWarehouseId, PreferredBinId (comentario: sugerencia de putaway, NO la ubicación real), IsActive, RowVersion. UNIQUE(TenantId,ClientId,Sku).
- **tabla**: ProductUom; **notas**: No existe tabla de UdM alternativas/conversión por producto. Product solo tiene BaseUomLookupId único; no hay tabla de conversiones (ej. caja->unidad).
- **tabla**: StockBalance; **existeEnSql**: True; **notas**: L1051-1065. Cols: StockBalanceId, TenantId, ProductId, WarehouseId, WarehouseBinId NULL, LotId NULL, QtyOnHand, QtyReserved, QtyAvailable AS (QtyOnHand-QtyReserved) PERSISTED (computada), UpdatedAtUtc, RowVersion. UNIQUE(ProductId,WarehouseId,WarehouseBinId,LotId). Índice IX_StockBalance_WH(WarehouseId,ProductId). No incluye SerialId en la clave (el número de serie no particiona el saldo de StockBalance).
- **tabla**: InventoryTransaction; **existeEnSql**: True; **notas**: L1067-1087. PK BIGINT. Cols: TenantId, TxnTypeLookupId (Entity='InventoryTxnType'), ProductId, LotId NULL, SerialId NULL, FromWarehouseId/FromBinId NULL, ToWarehouseId/ToBinId NULL, Quantity, RefEntityLookupId (Entity='EntityType')+RefId (patrón polimórfico genérico, sin FK real), Notes, CreatedAtUtc default SYSUTCDATETIME(), CreatedBy. Índices por (ProductId,CreatedAtUtc) y (RefEntityLookupId,RefId). No tiene TenantId scoping check pero sí columna TenantId (ITenantScoped esperado).
- **tabla**: InventoryLot; **existeEnSql**: True; **notas**: L1032-1038. Cols: LotId, ProductId FK, LotNumber, ManufactureDate, ExpiryDate, IsActive. UNIQUE(ProductId,LotNumber). No tiene TenantId propio (se hereda vía Product).
- **tabla**: InventorySerial; **existeEnSql**: True; **notas**: L1041-1048. Cols: SerialId, ProductId FK, LotId NULL FK, SerialNumber, StatusCodeId NULL (StatusCode Entity='SerialStatus'). UNIQUE(ProductId,SerialNumber). Sin TenantId propio.
- **tabla**: Receipt; **notas**: No existe tabla 'Receipt'; la entidad real se llama ReceiptHeader (L1761-1774): ReceiptHeaderId, PublicId, TenantId, WarehouseId, AsnId NULL (FK Asn), DockId NULL (FK WarehouseDock), ReceiptTypeLookupId (Entity='ReceiptType'), Number, StatusCodeId (StatusCode Entity='ReceiptStatus'), ReceivedAtUtc, IsActive, RowVersion. UNIQUE(TenantId,Number).
- **tabla**: ReceiptLine; **existeEnSql**: True; **notas**: L1777-1787. Cols: ReceiptLineId, ReceiptHeaderId FK, AsnLineId NULL FK, ProductId FK, LotId NULL, SerialId NULL, ReceivedQty, StagingBinId NULL FK WarehouseBin. Sin StatusCodeId propio ni RowVersion.
- **tabla**: PickWave; **existeEnSql**: True; **notas**: L1811-1821. Cols: PickWaveId, TenantId, WarehouseId, Number, StatusCodeId (Entity='PickWaveStatus'), TripId NULL FK Trip, CreatedAtUtc. UNIQUE(TenantId,Number).
- **tabla**: PickTask; **existeEnSql**: True; **notas**: L1823-1834. Cols: PickTaskId, PickWaveId FK, CargoLineId NULL FK, ProductId FK, LotId NULL, FromBinId NOT NULL FK WarehouseBin, QtyToPick, QtyPicked default 0, StatusCodeId (Entity='PickTaskStatus'), Sequence NULL.
- **tabla**: Carton; **existeEnSql**: True; **notas**: L1836-1844. Cols: CartonId, PickWaveId NULL FK, TransportOrderId NULL FK, CartonTypeLookupId NULL (Entity='CartonType'), Barcode, WeightKg, StatusCodeId (Entity='CartonStatus'). Sin TenantId propio.
- **tabla**: CartonLine; **existeEnSql**: True; **notas**: L1846-1854. Cols: CartonLineId, CartonId FK, ProductId FK, LotId NULL, SerialId NULL, Quantity.
- **tabla**: CycleCount; **existeEnSql**: True; **notas**: L1856-1865. Cols: CycleCountId, TenantId, WarehouseId FK, Number, StatusCodeId (Entity='CycleCountStatus'), CreatedAtUtc. UNIQUE(TenantId,Number).
- **tabla**: CycleCountLine; **existeEnSql**: True; **notas**: L1867-1877. Cols: CycleCountLineId, CycleCountId FK, WarehouseBinId FK NOT NULL, ProductId FK, LotId NULL, SystemQty, CountedQty NULL, VarianceQty AS (ISNULL(CountedQty,0)-SystemQty) PERSISTED (computada), AdjustmentTxnId NULL FK InventoryTransaction (enlaza el ajuste generado, si lo hay).
- **tabla**: InventoryAdjustment; **notas**: No existe tabla dedicada de ajustes. El ajuste de inventario se modela reutilizando InventoryTransaction con TxnTypeLookupId='ADJUSTMENT' (seed InventoryTxnType), enlazado desde CycleCountLine.AdjustmentTxnId.
- **tabla**: DockAppointment; **existeEnSql**: True; **notas**: L1882-1893. Cols: DockAppointmentId, TenantId, WarehouseDockId FK, DirectionLookupId (Entity='DockDirection'), AsnId NULL FK, TripId NULL FK, ScheduledStartUtc NOT NULL, ScheduledEndUtc NULL, StatusCodeId (Entity='AppointmentStatus'). Índice (WarehouseDockId,ScheduledStartUtc).
- **tabla**: CrossDockPlan; **existeEnSql**: True; **notas**: L1895-1905. Cols: CrossDockPlanId, TenantId, WarehouseId FK, Number, StatusCodeId (Entity='CrossDockStatus'), StagingZoneId NULL FK WarehouseZone, CreatedAtUtc. UNIQUE(TenantId,Number).
- **tabla**: CrossDockAllocation; **existeEnSql**: True; **notas**: L1907-1915. Cols: CrossDockAllocationId, CrossDockPlanId FK, ReceiptLineId FK NOT NULL, TransportOrderId NULL FK, CargoLineId NULL FK, AllocatedQty, StatusCodeId (Entity='AllocationStatus'). Vincula recepción->salida sin pasar por StockBalance completo (WMS lean).
- **tabla**: TransportOrder; **existeEnSql**: True; **notas**: L1262-1309. Tabla de órdenes de transporte (no de compra); relevante a Inventario solo como referencia de CargoLine/Carton/DockAppointment/CrossDockAllocation. No es tabla WMS propiamente, ver reglas R-cargoline.
- **tabla**: CargoLine; **existeEnSql**: True; **notas**: L1336-1354. Cols: CargoLineId, TransportOrderId FK, PickupStopId/DeliveryStopId NULL FK OrderStop, ProductId NULL FK, PackageTypeLookupId NULL, PackageNumber, Description, Quantity DEFAULT 1 CHECK>0, UomLookupId NULL (Entity='UnitOfMeasure'), WeightKg, VolumeM3, LotId NULL FK InventoryLot, SerialId NULL FK InventorySerial, HandlingFlags INT default 0, IsActive. Es la línea de carga de la orden (no de cross-dock ni de PO).
- **tabla**: Trip; **existeEnSql**: True; **notas**: L1383-1413. Relevante a WMS solo por OriginWarehouseId NULL FK Warehouse y por ser referenciado desde PickWave.TripId y DockAppointment.TripId. Cols completas: TripId, PublicId, TenantId, Code, PlanDate, DispatchZoneId (FK compuesta agregada después), OriginWarehouseId NULL, VehicleId/DriverId NULL (FKs compuestas con TenantId), StatusCodeId (Entity='TripStatus'), Planned/Actual Start/End Utc, TotalDistanceKm, TotalDurationMin, IsActive, auditoría, RowVersion. UNIQUE(TenantId,Code) y (TripId,TenantId).

### reglas

- **id**: R1; **regla**: Todo movimiento de inventario escribe en el ledger InventoryTransaction, que es la fuente de verdad; el balance siempre se reconstruye del ledger.; **fuente**: L291, L330; **esRequisitoReal**: True
- **id**: R2; **regla**: Jerarquía física Almacén→Zona→Bin con códigos de ubicación (pasillo-rack-nivel-posición); zonas tipadas por catálogo ZoneType.; **fuente**: L293; **esRequisitoReal**: True
- **id**: R3; **regla**: Warehouse es entidad propia del tenant desde el día uno (multi-almacén desde la base); zonas, bins, StockBalance, PO y equipos en alquiler ya referencian WarehouseId. Un tenant con varios almacenes no requiere cambio de esquema, solo habilitar el selector de almacén en pantallas que hoy lo asumen implícito.; **fuente**: L294; **esRequisitoReal**: True
- **id**: R4; **regla**: Una posición (bin) puede tener varios productos distintos; StockBalance es la tabla puente (ProductId+WarehouseId+WarehouseBinId+LotId) — no existe 'un producto por bin'.; **fuente**: L295; **esRequisitoReal**: True
- **id**: R5; **regla**: La posición en StockBalance es la ubicación real y actual, no una sugerencia. La sugerencia de dónde debería ir un producto (Product.PreferredWarehouseId/PreferredBinId) es un concepto aparte, opcional, que no reemplaza el balance real.; **fuente**: L296; **esRequisitoReal**: True
- **id**: R6; **regla**: Warehouse necesita pantalla propia de alta/baja (código, nombre, dirección, estatus), por encima de Ubicaciones en la jerarquía del menú Almacén.; **fuente**: L297; **esRequisitoReal**: True
- **id**: R7; **regla**: Recepción (ReceiptLine) contra ASN (validando esperado vs recibido) o ciega; cada línea confirmada escribe un InventoryTransaction positivo y deja la mercancía en bin de staging.; **fuente**: L298; **esRequisitoReal**: True
- **id**: R8; **regla**: Al abrir un recibo nuevo, la cantidad recibida arranca igual a la esperada (se asume que llega completo); el receptor la corrige y solo entonces se genera el ajuste si hubo diferencia.; **fuente**: L299; **esRequisitoReal**: True
- **id**: R9; **regla**: Recibir distinto a lo esperado es el caso normal: la diferencia entre esperado y recibido se registra como ajuste de inventario al confirmar, mismo mecanismo/ledger que el conteo cíclico (InventoryTransaction/AdjustmentTxnId); el stock siempre refleja lo que físicamente entró, el ajuste es el rastro de auditoría del porqué no cuadró.; **fuente**: L300; **esRequisitoReal**: True
- **id**: R10; **regla**: Putaway dirigido: el sistema sugiere bin destino (tipo de zona, rotación, capacidad) y genera WarehouseTask PUTAWAY; al completarse, movimiento staging→bin.; **fuente**: L301; **esRequisitoReal**: True
- **id**: R11; **regla**: Picking por olas agrupa órdenes (atadas a un trip de salida) en PickWave, genera PickTask con secuencia optimizada y reserva stock (QtyReserved). Lo que dispara un PickTask es que CargoLine tenga ProductId, no que el paquete ya venga armado por el cliente. El dueño del producto (Product.ClientId) puede ser un cliente 3PL o NULL (propio de Advance) sin cambiar la mecánica de picking/pantalla.; **fuente**: L302; **esRequisitoReal**: True
- **id**: R12; **regla**: Packing (esquema real): consolida lo pickeado en cartones con su contenido (CartonLine), peso y código de barras.; **fuente**: L303; **esRequisitoReal**: True
- **id**: R13; **regla**: Nota de alcance (mock): 'Recolección y empaque' ad hoc no parte de PickWave/orden existente — es un diseño de UI paralelo al esquema real de arriba, vigente para el caso de Advance vendiendo su propio inventario. Se escogen productos, se procesan como recolectados (baja inventario de inmediato, ledger tipo Despacho) y la recolección puede convertirse en un empaque = crear una orden real (reusando autocompletar de consignatario y listas de servicio/paquete de Entrada de órdenes).; **fuente**: L304; **esRequisitoReal**: True
- **id**: R14; **regla**: Ajuste de inventario contra compras recibidas parcialmente: el faltante contra una PO no se resuelve solo al confirmar el recibo (eso ya generó el ajuste de lo que sí entró); queda pendiente hasta resolución explícita con tres acciones — Cerrar (acepta el faltante, sin movimiento), Reordenar (crea borrador de PO nueva por la cantidad faltante) o Ajuste manual (cantidad+motivo, escribe al ledger). Cada resolución se registra y descuenta del faltante pendiente de esa PO.; **fuente**: L305, ampliado en bitácora L762; **esRequisitoReal**: True
- **id**: R15; **regla**: Replenishment: tareas para reabastecer zonas de picking desde reserva cuando el disponible baja del mínimo.; **fuente**: L306; **esRequisitoReal**: True
- **id**: R16; **regla**: Conteo cíclico: cuenta por bin/producto, calcula varianza y genera el ajuste como InventoryTransaction (enlazado en AdjustmentTxnId) — nada se ajusta fuera del ledger. Modo 'informado': el operador ve la cantidad esperada mientras cuenta (decisión de Luis).; **fuente**: L307; bitácora L542; **esRequisitoReal**: True
- **id**: R17; **regla**: Cola de tareas unificada (WarehouseTask) para el operador móvil, con tipo, prioridad y asignación.; **fuente**: L308; **esRequisitoReal**: True
- **id**: R18; **regla**: Trazabilidad: todo movimiento (recepción, putaway, pick, ajuste) referencia su origen vía RefEntity+RefId y queda en el ledger.; **fuente**: L309; **esRequisitoReal**: True
- **id**: R19; **regla**: Cross-dock (módulo CROSSDOCK) existe completo en el esquema pero no tenía pantalla planificada en el mock para Advance, porque su caso real suena a almacenaje (Recibo+Ubicaciones+Inventario), no a cross-dock puro. Se mantiene diseñado por ser candidato fuerte para el futuro módulo Marítimo (DockAppointment).; **fuente**: L313; **notas_no_aplica**: decisión de alcance/mock, no requisito de negocio del sistema real más allá de mantener el esquema
- **id**: R19b; **regla**: CONTRADICCIÓN resuelta por bitácora (más reciente gana): Luis pidió construir de todos modos una pantalla de demo funcional de Cruce de muelle, marcada 'Módulo apagado para Advance · demo', mostrando muelles tipados, agenda de citas (DockAppointment) y emparejamiento inbound↔outbound (CrossDockAllocation) que escribe movimiento CROSSDOCK al ledger; lo no asignado cae a putaway normal. Orden en el menú: Conteo cíclico antes de Cruce de muelle.; **fuente**: bitácora L543-544, contradice/actualiza L313; **esRequisitoReal**: True
- **id**: R20; **regla**: Emparejamiento inbound↔outbound (CrossDockAllocation) asigna cantidades de líneas de recepción directamente a órdenes/trips de salida, sin pasar por reserva.; **fuente**: L318; **esRequisitoReal**: True
- **id**: R21; **regla**: Flujo directo cross-dock: muelle inbound → staging cross-dock → muelle outbound vía WarehouseTask CROSSDOCK; el ledger registra el movimiento con InventoryTxnType='CROSSDOCK'.; **fuente**: L319; **esRequisitoReal**: True
- **id**: R22; **regla**: Excedente/faltante en cross-dock: lo no asignado en una recepción cae a putaway normal (a reserva); el faltante outbound queda visible como pendiente.; **fuente**: L320; **esRequisitoReal**: True
- **id**: R23; **regla**: Maestro de productos (SKU) propio o por cliente (3PL), con UoM base, tipo de seguimiento (ninguno/lote/serie), peso/volumen, código de barras, PurchaseCost (default de línea de PO) y SalePrice (alimenta línea PRODUCT_SALE de factura), ambos editables por producto.; **fuente**: L325; **esRequisitoReal**: True
- **id**: R24; **regla**: Desactivar un producto (Product.IsActive=0) solo se permite si no tiene inventario disponible. Un producto inactivo desaparece de selectores de creación pero sigue apareciendo en Inventario e informes.; **fuente**: L326; **esRequisitoReal**: True
- **id**: R25; **regla**: Ajuste manual de inventario: cualquier producto admite ajuste directo (cantidad +/- con motivo) — mismo ledger, mismo reporte de ajustes que recepción y conteo cíclico.; **fuente**: L327; **esRequisitoReal**: True
- **id**: R26; **regla**: Lotes y series con fechas de fabricación/expiración; series con estatus (disponible/reservado/despachado).; **fuente**: L328; **esRequisitoReal**: True
- **id**: R27; **regla**: Balance por ubicación (StockBalance): en mano, reservado y disponible (columna computada) por almacén/bin/lote.; **fuente**: L329; **esRequisitoReal**: True
- **id**: R28; **regla**: Trazabilidad punta a punta: desde una CargoLine (con lote/serie) se sigue el rastro vía InventoryTransaction.RefEntity='TransportOrder'; cada despacho descuenta stock (movimiento negativo), cada recepción suma (movimiento positivo). Vista vw_LotGenealogy reconstruye la genealogía de un lote.; **fuente**: L331; **esRequisitoReal**: True
- **id**: R29; **regla**: Kárdex de movimientos: vista de solo lectura del ledger InventoryTransaction. Columnas: fecha, hora, tipo (chip Recepción/Despacho/Transferencia/Ajuste/Cruce de muelle), SKU, producto, cantidad con signo, posición, lote/serie, origen (RefEntity·RefId) y usuario.; **fuente**: bitácora L541; **esRequisitoReal**: True
- **id**: R30; **regla**: Orden del menú Almacén: Conteo cíclico va antes de Cruce de muelle.; **fuente**: bitácora L544; **esRequisitoReal**: True
- **id**: R31; **regla**: Dueño del inventario (Product.ClientId): modal de producto trae selector 'Dueño del inventario' (Propio=Advance o cliente 3PL); tabla trae columna Dueño (propios en gris tenue).; **fuente**: bitácora L548; **esRequisitoReal**: True
- **id**: R32; **regla**: Filtros de Inventario: Almacén, Ubicación, Producto y Categoría (multi-selección con buscador). El filtro de Cliente se quitó porque el cliente ya es el tenant del encabezado — filtrar por él era redundante.; **fuente**: bitácora L549; **esRequisitoReal**: True
- **id**: R33; **regla**: Mismos filtros de Inventario replicados en Conteo cíclico y Kárdex. El Kárdex mantiene además Tipo de movimiento y rango de fechas (Desde/Hasta primero).; **fuente**: bitácora L551, L553; **esRequisitoReal**: True
- **id**: R34; **regla**: Recolección y empaque: una sola lista de 'Recolecciones' (no dos pestañas) con todos los lotes, recolectados y empacados; 'Empacar' es acción por item en estatus Recolectado, vía modal (mismo patrón genModal/genScrim), no panel inline. Confirmar sigue creando la orden real en ORDERS/TransportOrder.; **fuente**: bitácora L777-778; **esRequisitoReal**: True
- **id**: R35; **regla**: La orden nacida de un empaque aparece de inmediato en Órdenes/Entrada de órdenes, sin pieza adicional, porque usa la misma tabla/servicio de creación de órdenes.; **fuente**: bitácora L779; **esRequisitoReal**: True
- **id**: R36; **regla**: Cada item del listado de Recolecciones muestra el número de orden cuando ya fue empacado, y trae opción de eliminar según reglas de estatus: un lote Recolectado (sin orden) siempre se puede eliminar; un lote Empacado solo se puede eliminar si su orden sigue en el estatus inicial (mismo principio de 'eliminar una orden solo es posible en el estatus inicial', módulo 2 L253). Eliminar restaura el inventario (suma de vuelta + InventoryTransaction tipo Ajuste positivo referenciando el lote, nunca se edita/borra el movimiento original de Despacho) y, si estaba empacado, también quita la orden.; **fuente**: bitácora L780; **esRequisitoReal**: True
- **id**: R37; **regla**: El selector de productos de Recolección es multi-select buscable (por SKU o nombre); cada producto marcado trae cantidad editable (precargada en 1) y disponible en inventario visible.; **fuente**: bitácora L781; **esRequisitoReal**: True
- **id**: R38; **regla**: Filtros del listado de Recolecciones: Desde/Hasta (fecha del lote), Producto (multi-select) y Estado (Recolectado/Empacada); el buscador de texto libre se aplica después, sobre lo ya filtrado.; **fuente**: bitácora L782; **esRequisitoReal**: True
- **id**: R39; **regla**: Distinción de conceptos: número de orden/empaque (PackBatchNumber) lo crea el sistema para identificar la orden/empaque, nunca lo escribe el cliente; número de factura (ClientInvoiceNumber) lo asigna el cliente, es opcional y nuevo en el flujo de empaque.; **fuente**: bitácora L789; **esRequisitoReal**: True
- **id**: R40; **regla**: Campo de número de factura en el modal de empaque; al confirmar, el valor se guarda tanto en la orden como en el lote de recolección (mismo dato, dos referencias).; **fuente**: bitácora L790; **esRequisitoReal**: True
- **id**: R41; **regla**: Trazabilidad orden↔lote: la orden nueva guarda una referencia al id del lote que la originó — mismo principio de RefEntity/RefId aplicado a nivel de este flujo (en el esquema real: TransportOrder.SourceEntityTypeLookupId/SourceEntityId).; **fuente**: bitácora L791; **esRequisitoReal**: True
- **id**: R42; **regla**: El listado de Recolecciones muestra ambos números cuando el lote ya está empacado ('Orden: X · Factura: Y', o Factura: — si aún no la da el cliente).; **fuente**: bitácora L792; **esRequisitoReal**: True
- **id**: R43; **regla**: Filtros de No. de orden y No. de factura en Recolección y empaque, con coincidencia parcial (LIKE), combinables con los filtros ya existentes y con el buscador de texto libre (que también revisa el número de factura).; **fuente**: bitácora L793; **esRequisitoReal**: True
- **id**: R44; **regla**: El listado de Órdenes/Entrada de órdenes trae columna 'Empaque' (id del lote que originó la orden, o '—' si no viene de un empaque), ordenable como el resto de columnas.; **fuente**: bitácora L794; **esRequisitoReal**: True
- **id**: R45; **regla**: Cuatro identificadores por orden con reglas de autogenerado independientes: OrderNumber (cliente o Teikem), ClientInvoiceNumber (cliente o Teikem, ajustes independientes Client.OrderNumberBy/InvoiceNumberBy), PackBatchNumber (siempre tiene valor, nunca lo asigna el cliente — es el id del lote de empaque si nace de Recolección y empaque, o autogenerado en cualquier otra vía), PackageNumber (opcional, autogenerado si se deja en blanco; en el mock es un campo único por orden, el esquema real necesitará una lista de bultos cuando la orden tiene más de una pieza).; **fuente**: módulo 2 L238-242, ampliado en bitácora L801-811; **esRequisitoReal**: True
- **id**: R46; **regla**: PackBatchNumber es la primera columna del listado de Entrada de órdenes/Órdenes y entra en sus filtros de búsqueda, junto al número de factura.; **fuente**: módulo 2 L241; bitácora L802/L806; **esRequisitoReal**: True
- **id**: R47; **regla**: Nomenclatura configurable por cliente (Client.NumberFormat: orden/factura/paquete) — patrón de texto con '#' por dígito del consecutivo y '@' por letra (en el mock siempre resuelve a 'A', simplificación deliberada), configurable en Clientes y contratos y en Portal de clientes→Configuración (mismo dato). El número de empaque queda fuera a propósito: identificador interno de operación/almacén con formato fijo EMP-nnnnn, el cliente no necesita reconocerlo ni configurarlo.; **fuente**: módulo 2 L243; bitácora L831-832; **esRequisitoReal**: True
- **id**: R48; **regla**: Entrada rápida y entrega especial no muestran en pantalla los campos de número de paquete ni la distinción orden/factura habilitada por cliente, pero igual autogeneran empaque y factura en silencio, para cumplir 'siempre tiene valor' sin forzar campos sin sentido de negocio en esos flujos.; **fuente**: bitácora L810; **esRequisitoReal**: True
- **id**: R49; **regla**: Procesar entregas (COD) debe poder buscar también por número de orden, además del ya existente Factura.; **fuente**: bitácora L807; **esRequisitoReal**: True
- **id**: R50; **regla**: Plantillas de exportación configurables y guardables (Contabilización de compras/despachos, luego extendidas a Facturación y Liquidación): un solo motor de plantillas, con catálogo de campos por alcance, formato CSV/TXT, delimitador, formato de fecha, encabezado sí/no y columnas a exportar; guardar actualiza en el sitio o clona como plantilla nueva.; **fuente**: bitácora L769, L808; **esRequisitoReal**: True; **nota**: la Contabilización de compras/despachos en sí queda para Lote 10 según alcance de esta tarea; el motor de plantillas de exportación se documenta aquí porque nace ligado a Ajustes de inventario/almacén, pero su tabla persistente no existe en el SQL
- **id**: R1; **regla**: QtyAvailable de StockBalance es columna computada persistida = QtyOnHand - QtyReserved; nunca se escribe directo, la calcula SQL Server.; **fuente**: logistica-db-estructura.sql:1060; **esRequisitoReal**: True
- **id**: R2; **regla**: El saldo de inventario (StockBalance) es único por combinación (ProductId, WarehouseId, WarehouseBinId, LotId); no distingue por número de serie, por lo que el número de serie no fragmenta el saldo agregado.; **fuente**: logistica-db-estructura.sql:1062; **esRequisitoReal**: True
- **id**: R3; **regla**: Cada lote (InventoryLot) es único por (ProductId, LotNumber); cada serie (InventorySerial) es única por (ProductId, SerialNumber); ninguna de las dos tablas tiene TenantId propio, heredan el tenant vía Product.; **fuente**: logistica-db-estructura.sql:1037, 1047; **esRequisitoReal**: True
- **id**: R4; **regla**: InventoryTransaction usa un patrón polimórfico genérico RefEntityLookupId (LookupCode Entity='EntityType') + RefId sin FK real, para enlazar el movimiento con su origen (recepción, pick, ajuste, cross-dock, etc.).; **fuente**: logistica-db-estructura.sql:1079-1080, 1086; **esRequisitoReal**: True
- **id**: R5; **regla**: No existe una tabla InventoryAdjustment separada: el ajuste de inventario se modela como una InventoryTransaction con TxnTypeLookupId='ADJUSTMENT' (catálogo InventoryTxnType), y CycleCountLine.AdjustmentTxnId enlaza la línea contada con la transacción de ajuste generada.; **fuente**: logistica-db-estructura.sql:1875; logistica-db-seed.sql:188; **esRequisitoReal**: True
- **id**: R6; **regla**: VarianceQty en CycleCountLine es columna computada persistida = ISNULL(CountedQty,0) - SystemQty; se calcula sola al capturar CountedQty.; **fuente**: logistica-db-estructura.sql:1874; **esRequisitoReal**: True
- **id**: R7; **regla**: Product.PreferredBinId es explícitamente una sugerencia de putaway por defecto al recibir, y el comentario del esquema aclara que NO es la ubicación real del inventario (esa vive en StockBalance).; **fuente**: logistica-db-estructura.sql:1026; **esRequisitoReal**: True
- **id**: R8; **regla**: El número de recepción (ReceiptHeader.Number) es único por tenant (UQ_Receipt_Number); el número de ola de picking (PickWave.Number), el de conteo cíclico (CycleCount.Number) y el de plan de cross-dock (CrossDockPlan.Number) también son únicos por tenant.; **fuente**: logistica-db-estructura.sql:1773, 1819, 1863, 1903; **esRequisitoReal**: True
- **id**: R9; **regla**: WarehouseBin (la posición física real, llamada así en SQL en vez de 'WarehouseLocation') es única por (WarehouseZoneId, Code); WarehouseZone es única por (WarehouseId, Code); WarehouseDock única por (WarehouseId, Code).; **fuente**: logistica-db-estructura.sql:987, 977, 998; **esRequisitoReal**: True
- **id**: R10; **regla**: CargoLine.Quantity tiene CHECK > 0 (comentario Lote 3), lo que aplica también a las líneas de carga que referencian producto/lote/serie de inventario.; **fuente**: logistica-db-estructura.sql:1352; **esRequisitoReal**: True
- **id**: R11; **regla**: CrossDockAllocation enlaza directamente ReceiptLine (entrada) con TransportOrder/CargoLine (salida) mediante AllocatedQty, sin pasar por un StockBalance intermedio completo, modelando el flujo cross-dock como asignación directa recepción->orden.; **fuente**: logistica-db-estructura.sql:1907-1915; **esRequisitoReal**: True
- **id**: R12; **regla**: WarehouseTask es una cola genérica de tareas de almacén (putaway, pick, pack, reabasto, conteo, carga, cross-dock) con Priority (default 100) y un índice de cola por (WarehouseId, TaskTypeLookupId, StatusCodeId, Priority); no hay tablas separadas de tarea por tipo salvo PickTask.; **fuente**: logistica-db-estructura.sql:1789-1808; **esRequisitoReal**: True
- **id**: R13; **regla**: El seed del catálogo InventoryTxnType define únicamente RECEIPT, ISSUE, TRANSFER, ADJUSTMENT y CROSSDOCK como tipos de movimiento de inventario permitidos.; **fuente**: logistica-db-seed.sql:188; **esRequisitoReal**: True
- **id**: R45-R49 (alcance); **regla**: Las reglas de numeración de 4 identificadores de la orden (OrderNumber/ClientInvoiceNumber/PackBatchNumber/PackageNumber), la nomenclatura configurable por cliente (Client.NumberFormat) y el modo either/or reemplazado por dos preguntas independientes YA están modeladas en el esquema por el Lote 3 (columnas TransportOrder.PackBatchNumber/ClientInvoiceNumber L1268-1271, Client.ClientAssignsOrderNumber/ClientAssignsInvoiceNumber/OrderNumberFormat/InvoiceNumberFormat/PackageNumberFormat L691-696, CargoLine.PackageNumber L1343) y probablemente en OrderService de un lote anterior. Para el Lote 6 esto es CONTEXTO/DEPENDENCIA a reutilizar, no una regla nueva a construir — la especificación recibida las presenta mezcladas con reglas propias del Lote 6 (R14-R38) sin distinguir 'ya construido en Lote 2/3' de 'pendiente en Lote 6'. Corrección: reclasificar R39-R49 como 'ya implementado, Lote 6 solo integra (Recolección y empaque escribe en estas columnas)', salvo la parte de PICK_BATCH/PickBatchNumber en sí, que si depende de una tabla nueva sí es trabajo del Lote 6.; **fuente**: logistica-db-estructura.sql:691-696,1268-1289,1343; docs/lote3 (no verificado en detalle en esta revisión, falta confirmar si OrderService ya implementa la lógica de generación en código); **esRequisitoReal**: True
- **id**: R-vacío-PackageNumber; **regla**: CORRECCIÓN: no falta diseñar 'una lista de bultos' para PackageNumber — ya existe como columna por línea en CargoLine (Lote 3). El vacío correspondiente en la especificación recibida debe eliminarse.; **fuente**: logistica-db-estructura.sql:1343; **esRequisitoReal**: True
- **id**: R-ClientAssigns; **regla**: Corrección de nombres de columna: 'quién asigna número de orden/factura' se modela con dos columnas booleanas ClientAssignsOrderNumber/ClientAssignsInvoiceNumber (no con columnas de texto 'OrderNumberBy'/'InvoiceNumberBy' como cita literalmente el documento maestro L232/240 y como repitió la especificación recibida).; **fuente**: logistica-db-estructura.sql:691-692; **esRequisitoReal**: True
- **id**: R-EntityType-PickBatch; **regla**: Falta sembrar un código EntityType (p.ej. 'PICK_BATCH') en el seed para que TransportOrder.SourceEntityTypeLookupId pueda referenciar el lote de Recolección y empaque, tal como el comentario del propio SQL lo anticipa ('lo llena Recolección y empaque / IMPORT_BATCH'). No está en el seed actual.; **fuente**: logistica-db-estructura.sql:1288 (comentario); logistica-db-seed.sql:140-153 (EntityType sembrado, sin PICK_BATCH); **esRequisitoReal**: True
- **id**: R-nomenclatura-@ inconsistencia interna del documento; **regla**: El propio documento maestro se contradice en el símbolo de letra de la nomenclatura configurable: L827 dice 'usando @@@ para letras y # para números' (tres arrobas) mientras que L831 (bitácora posterior, gana) dice 'El símbolo @ (letra) también se resuelve... aunque en el mock se resuelve siempre a A'. Gana la versión de L831 (un solo '@'), pero conviene anotar la contradicción interna al documentar la regla, no solo citar una de las dos versiones como si no hubiera discrepancia.; **fuente**: L827 vs L831 (bitácora más reciente gana); **esRequisitoReal**: True

### endpoints

- **metodo**: GET; **ruta**: /api/v1/warehouses; **permiso**: warehouse.view (a confirmar nombre); **modulo**: WMS
- **metodo**: POST; **ruta**: /api/v1/warehouses; **permiso**: warehouse.manage; **modulo**: WMS
- **metodo**: PUT; **ruta**: /api/v1/warehouses/{publicId}; **permiso**: warehouse.manage; **modulo**: WMS
- **metodo**: GET; **ruta**: /api/v1/warehouses/{publicId}/zones; **permiso**: warehouse.manage; **modulo**: WMS
- **metodo**: POST; **ruta**: /api/v1/warehouses/{publicId}/zones; **permiso**: warehouse.manage; **modulo**: WMS
- **metodo**: GET; **ruta**: /api/v1/warehouses/{publicId}/bins; **permiso**: warehouse.manage; **modulo**: WMS
- **metodo**: POST; **ruta**: /api/v1/warehouses/{publicId}/bins; **permiso**: warehouse.manage; **modulo**: WMS
- **metodo**: GET; **ruta**: /api/v1/products; **permiso**: inventory.view; **modulo**: INVENTORY
- **metodo**: POST; **ruta**: /api/v1/products; **permiso**: inventory.manage; **modulo**: INVENTORY
- **metodo**: PUT; **ruta**: /api/v1/products/{publicId}; **permiso**: inventory.manage; **modulo**: INVENTORY
- **metodo**: POST; **ruta**: /api/v1/products/{publicId}/deactivate; **permiso**: inventory.manage; **modulo**: INVENTORY
- **metodo**: GET; **ruta**: /api/v1/stock-balances; **permiso**: inventory.view; **modulo**: INVENTORY
- **metodo**: GET; **ruta**: /api/v1/inventory-transactions; **permiso**: inventory.view; **modulo**: INVENTORY
- **metodo**: POST; **ruta**: /api/v1/inventory-transactions/adjustments; **permiso**: inventory.adjust; **modulo**: INVENTORY
- **metodo**: POST; **ruta**: /api/v1/receipts; **permiso**: warehouse.receive; **modulo**: WMS
- **metodo**: POST; **ruta**: /api/v1/receipts/{publicId}/lines/{lineId}/confirm; **permiso**: warehouse.receive; **modulo**: WMS
- **metodo**: GET; **ruta**: /api/v1/purchase-orders/{publicId}/shortage-lines; **permiso**: inventory.adjust; **modulo**: INVENTORY (vacío: tabla de resolución de faltantes no existe en SQL)
- **metodo**: POST; **ruta**: /api/v1/purchase-orders/{publicId}/shortage-lines/{lineId}/resolve; **permiso**: inventory.adjust; **modulo**: INVENTORY (vacío: acciones Cerrar/Reordenar/Ajuste manual sin tabla persistente)
- **metodo**: GET; **ruta**: /api/v1/cycle-counts; **permiso**: warehouse.count; **modulo**: WMS
- **metodo**: POST; **ruta**: /api/v1/cycle-counts/{publicId}/confirm; **permiso**: warehouse.count; **modulo**: WMS
- **metodo**: GET; **ruta**: /api/v1/dock-appointments; **permiso**: crossdock.view; **modulo**: CROSSDOCK (demo apagado para Advance)
- **metodo**: POST; **ruta**: /api/v1/cross-dock-plans/{publicId}/allocations; **permiso**: crossdock.manage; **modulo**: CROSSDOCK (demo apagado para Advance)
- **metodo**: GET; **ruta**: /api/v1/pick-batches; **permiso**: picking.view; **modulo**: WMS (Recolección y empaque ad hoc)
- **metodo**: POST; **ruta**: /api/v1/pick-batches; **permiso**: picking.collect; **modulo**: WMS (recolectar productos, descuenta stock)
- **metodo**: POST; **ruta**: /api/v1/pick-batches/{id}/pack; **permiso**: picking.pack; **modulo**: WMS (crea TransportOrder vía OrderService.CreateAsync con SourceEntityType='PICK_BATCH')
- **metodo**: DELETE; **ruta**: /api/v1/pick-batches/{id}; **permiso**: picking.pack; **modulo**: WMS (solo si recolectado sin orden, o si la orden sigue en estatus inicial; restaura inventario con ajuste positivo)
- **metodo**: POST; **ruta**: api/v1/scan/outbound; **permiso**: trips.scan; **modulo**: Patrón de estación de escaneo a reutilizar como modelo para escaneo de recepción/pick/cross-dock en WMS (src/Teikem.Api/Controllers/ScanController.cs + Services/OutboundScanService.cs): responde siempre 200 con resultado tipado (FOUND/NOT_FOUND/etc.), nunca DELETE, no recibe TenantId del request.
- **metodo**: GET; **ruta**: api/v1/clients; **permiso**: clients.read; **modulo**: CATALOG — patrón de controlador delgado (controller delega 100% en servicio, DTOs en Contracts/) a replicar para los controladores de Inventario/Almacén
- **metodo**: POST; **ruta**: api/v1/clients/{publicId}/status; **permiso**: clients.update; **modulo**: Patrón de transición de estatus vía StatusService.TransitionAsync + StatusChangeRequest, a reutilizar para estatus de lote/serie/orden de recepción/pick si el módulo define dominios de estatus propios
- **metodo**: GET; **ruta**: api/v1/clients/{publicId}/contacts; **permiso**: clients.read; **modulo**: Patrón de ContactPointService (owner polimórfico) — reutilizable si alguna entidad WMS (proveedor, ubicación) necesita puntos de contacto
- **metodo**: N/A; **ruta**: api/v1/customfields/definitions (patrón, ver ExtensibilityControllers.cs); **permiso**: admin.customfields; **modulo**: CUSTOM_FIELDS — CustomFieldService reutilizable si el módulo 6 expone campos personalizados sobre sus entidades (p.ej. Item, Lot)

### estatus

- **dominio**: WarehouseStatus; **efectos**: ['ACTIVE (pipe, inicial) -> INACTIVE (terminal)']
- **dominio**: DockStatus; **efectos**: ['FREE (pipe, inicial) -> OCCUPIED (lateral) / MAINTENANCE (lateral)']
- **dominio**: SerialStatus; **efectos**: ['AVAILABLE (pipe, inicial) -> RESERVED (pipe) -> SHIPPED (terminal)']
- **dominio**: ReceiptStatus; **efectos**: ['OPEN (pipe, inicial) -> RECEIVED (pipe) -> PUTAWAY (terminal)']
- **dominio**: WarehouseTaskStatus; **efectos**: ['PENDING (pipe, inicial) -> IN_PROGRESS (pipe) -> DONE (terminal)']
- **dominio**: PickWaveStatus; **efectos**: ['OPEN (pipe, inicial) -> PICKING (pipe) -> PACKED (pipe) -> SHIPPED (terminal)']
- **dominio**: PickTaskStatus; **efectos**: ['PENDING (pipe, inicial) -> PICKED (terminal) / SHORT (lateral, faltante)']
- **dominio**: CartonStatus; **efectos**: ['OPEN (pipe, inicial) -> CLOSED (pipe) -> SHIPPED (terminal)']
- **dominio**: CycleCountStatus; **efectos**: ['OPEN (pipe, inicial) -> COUNTED (pipe) -> RECONCILED (terminal)']
- **dominio**: AppointmentStatus; **efectos**: ['SCHEDULED (pipe, inicial) -> ARRIVED (pipe) -> COMPLETED (terminal) / NO_SHOW (lateral)']
- **dominio**: CrossDockStatus; **efectos**: ['OPEN (pipe, inicial) -> ALLOCATED (pipe) -> COMPLETED (terminal)']
- **dominio**: AllocationStatus; **efectos**: ['PLANNED (pipe, inicial) -> MOVED (terminal)']

### vacios

- Warehouse, WarehouseZone, WarehouseBin, WarehouseDock, Product, InventoryLot, InventorySerial, StockBalance, InventoryTransaction, WarehouseTask, PickWave, PickTask, Carton, CartonLine, CycleCount, CycleCountLine, DockAppointment, CrossDockPlan, CrossDockAllocation, PurchaseOrder(Line), ReceiptHeader/Line: todas existen en el SQL pero NINGUNA tiene entidad EF/configuración en src/ todavía (confirmado: no hay archivos con esos nombres fuera de bin/). Todo el módulo se construye desde cero en código.
- No existe tabla equivalente a PO_ADJUSTMENTS del mock (bitácora L762): no hay dónde persistir la resolución explícita de un faltante de PO (acción tomada: Cerrar/Reordenar/Ajuste manual, motivo, fecha, usuario, cuánto descuenta del pendiente). PurchaseOrderLine.QtyOrdered/QtyReceived alcanza para CALCULAR el faltante pero no para registrar su resolución — hay que decidir si se agrega una tabla nueva (p. ej. PurchaseOrderShortageResolution) en este lote.
- No existe tabla de plantillas de exportación (ACCT_TEMPLATES del mock) para Contabilización de compras/despachos/Facturación/Liquidación — motor de plantillas configurables (formato, delimitador, columnas). El documento marca la Contabilización de compras como Lote 10, pero el mecanismo de plantillas nace documentado junto a Ajustes de inventario en la misma bitácora; falta decidir en qué lote se modela la tabla y si Ajustes de inventario de este Lote 6 depende de ella o puede cerrarse sin exportación.
- PICK_BATCH / 'lote de recolección ad hoc' no tiene tabla propia en el SQL (el documento lo modela como concepto de mock, resuelto a nivel de esquema real vía TransportOrder.SourceEntityTypeLookupId/SourceEntityId apuntando a un EntityType='PICK_BATCH' o similar). No queda claro si el Lote 6 debe crear una tabla PickBatch/PickBatchLine real (para sostener el listado de 'Recolecciones', sus filtros propios de fecha/producto/estado, y el número de factura guardado dos veces) o si debe modelarse enteramente sobre InventoryTransaction + TransportOrder sin tabla intermedia — es una decisión de diseño pendiente, no zanjada por el documento ni por el SQL.
- No hay columna equivalente a 'RECEIPTS[].doneDate' en ReceiptLine/ReceiptHeader del SQL para agrupar por fecha de confirmación de recibo contra PO (bitácora L767 la pide explícitamente para Contabilización de compras, Lote 10, pero depende de que Recepción — Lote 6 — deje ese dato disponible).
- PackageNumber (número de paquete): el documento reconoce explícitamente (módulo 2 L242) que 'en el mock es un campo único por orden — el esquema real necesitará una lista (una fila por bulto) cuando la orden tiene más de una pieza'. No hay tabla de líneas de paquete en el SQL; queda como decisión de diseño abierta para este lote.
- No se confirmaron los nombres exactos de columnas Client.OrderNumberBy/InvoiceNumberBy en el SQL (solo se verificaron OrderNumberFormat/InvoiceNumberFormat/PackageNumberFormat en L694-696); falta revisar el bloque completo de la tabla Client para confirmar si las dos preguntas independientes 'quién asigna' ya están modeladas con esos nombres o con otros.
- Ambigüedad de término 'dueño del producto': el documento usa indistintamente 'Cliente' (3PL) y 'Propio de Advance' (ClientId NULL) para Product.ClientId, pero el mismo campo se reutiliza en TransportOrder/Client como el TENANT — hay que asegurar en el diseño que 'Product.ClientId' (dueño del inventario, tabla Client) no se confunda con el tenant dueño de los datos (TenantId), aunque el documento los distingue con cuidado (L302, L548-549) conviene dejarlo explícito en la especificación técnica.
- Cross-dock queda como 'módulo apagado para Advance · demo' según la bitácora (contradice/actualiza la nota de alcance original L313): no hay decisión documentada sobre si el endpoint/pantalla debe estar detrás de [RequireModule(ModuleKeys.CROSSDOCK)] deshabilitado por tenant, o si simplemente se construye igual que cualquier otro módulo y el tenant Advance no lo activa — a definir cómo se traduce 'marcada visualmente como módulo apagado · demo' a nivel de backend real (¿solo es un flag de UI, o el sistema real necesita un estado 'demo' distinto de 'activo'?).
- El documento no específica el permiso/nombre exacto de ningún endpoint de este módulo (los propuestos en 'endpoints' son inferidos por convención del proyecto, no están en el documento) — falta una tabla de PermissionCatalog para Almacén/Inventario/Cross-dock/Recolección y empaque.
- IDataSource para indicadores/vistas de Almacenaje: el documento (bitácora L892 'Indicadores por módulo de negocio: Entrega de órdenes, Almacenaje y Contabilidad') menciona que existe un módulo de indicadores de 'Almacenaje', pero esa sección no fue leída en profundidad en este barrido (fuera del rango de secciones pedido) — falta confirmar si el Lote 6 debe registrar su propio IDataSource o si ya lo hizo un lote anterior.
- El documento (no evaluado en este alcance, solo SQL) probablemente nombra 'WarehouseLocation' y 'Receipt' — en el SQL esas tablas existen bajo otros nombres: WarehouseBin y ReceiptHeader respectivamente; hay que confirmar contra el maestro si el nombre es solo terminológico o si se esperaban columnas adicionales.
- No existe tabla ProductUom (conversión de unidades de medida por producto, p.ej. caja=12 unidades); Product solo tiene un único BaseUomLookupId. Si el documento pide manejar múltiples UdM por producto con factores de conversión, falta la tabla.
- No existe tabla InventoryAdjustment dedicada; el ajuste se modela como InventoryTransaction tipo ADJUSTMENT enlazado desde CycleCountLine.AdjustmentTxnId. Si el documento describe un flujo de ajuste con motivo/aprobación propios (no solo cantidad), falta esa estructura (no hay columna de motivo/razón de ajuste en InventoryTransaction ni en CycleCountLine).
- ReceiptLine no tiene StatusCodeId propio ni RowVersion, solo el encabezado ReceiptHeader tiene estatus; si el documento requiere estatus por línea de recepción (parcial/discrepancia por línea), no está en el SQL.
- Carton y CartonLine no tienen TenantId propio (se infiere vía PickWave/TransportOrder, ambos NULL-ables), lo que puede dificultar el filtro de tenant si ambas FKs son NULL simultáneamente; no se pudo confirmar en el SQL una regla que impida que ambas queden NULL a la vez.
- InventoryLot e InventorySerial no tienen TenantId propio; dependen de Product.TenantId. No hay una constraint visible en el SQL que documente explícitamente esta regla de multi-tenencia indirecta (se infiere por diseño, no está anotada como comentario).
- No se encontró en el SQL una tabla o columna que registre el motivo de un DockAppointment.NO_SHOW ni penalización asociada, si el documento la pidiera.
- StockBalance no referencia InventorySerial (solo LotId), por lo que si el documento pide trazabilidad de saldo disponible por serie individual (no solo lote), no hay columna en StockBalance para ello; se infiere desde InventorySerial.StatusCodeId por separado.
- No se leyó todavía la sección del módulo Lote 6 en Diseño/logistica-funcionalidades-maestro.md ni las tablas correspondientes en Diseño/logistica-db-estructura.sql; esta respuesta cubre solo lo pedido explícitamente (reutilizables y endpoints/permisos existentes). Falta el análisis de reglas de negocio y entidades del propio módulo.
- Existen permisos ya sembrados para almacén: warehouse.receive, warehouse.pick, warehouse.count, warehouse.crossdock (categoría WAREHOUSE, en src/Teikem.Domain/Constants/PermissionCatalog.cs líneas 30-33 y 93-96) — el Lote 6 debe usarlos y no crear duplicados, salvo que el documento maestro pida permisos nuevos (p.ej. para trazabilidad de lote/serie) que no existen aún.
- Existen ModuleKeys ya definidos que aplican directamente: WmsLotSerial = "WMS_LOTSERIAL" y CrossDock = "CROSSDOCK" (src/Teikem.Domain/Constants/CatalogDomains.cs líneas 438-439) — confirmar en el documento maestro si el Lote 6 se ata a uno, ambos, o requiere un módulo adicional.
- No se confirmó si existe ya un StorageLocation/InventoryItem/Lot/Serial en el SQL: falta cruzar contra Diseño/logistica-db-estructura.sql antes de decidir qué tablas reutilizar vs. crear.
- Reutilizables transversales confirmados en código pero pendientes de verificar aplicabilidad exacta al dominio de inventario: IDataSource/IDataSourceRegistry (src/Teikem.Infrastructure/Analytics/DataSources.cs) para exponer indicadores de inventario; StatusService (src/Teikem.Infrastructure/Services/StatusService.cs) si el módulo define dominios de estatus (p.ej. LotStatus, ReceiptStatus); ContactPointService y CustomFieldService solo si las entidades del módulo lo requieren.
- ELIMINAR de la lista de vacíos original: 'PackageNumber... el esquema real necesitará una lista de bultos' — ya resuelto por Lote 3 con CargoLine.PackageNumber (columna por línea de carga). No es un vacío del Lote 6.
- ELIMINAR/CORREGIR: 'No se confirmaron los nombres exactos de columnas Client.OrderNumberBy/InvoiceNumberBy' — sí están confirmados, pero con otros nombres y otro tipo de dato: ClientAssignsOrderNumber/ClientAssignsInvoiceNumber (BIT), no columnas 'By' de texto. Debe corregirse la cita, no dejarse como pendiente de verificar.
- NUEVO vacío no señalado en la especificación recibida: el catálogo EntityType sembrado (logistica-db-seed.sql) no tiene código para 'lote de recolección/empaque' (PICK_BATCH) ni para ReceiptHeader (solo RECEIPT_LINE existe), pese a que TransportOrder.SourceEntityTypeLookupId ya está pensado explícitamente para apuntar ahí (comentario en el propio SQL, línea 1288). Hay que decidir el código exacto y sembrarlo en este lote.
- NUEVO: buena parte de las reglas de numeración de identificadores (R39-R49 de la especificación recibida) describe funcionalidad que YA fue implementada en el esquema por el Lote 3 (TransportOrder/CargoLine/Client), y probablemente en el servicio OrderService de ese mismo lote (no verificado en código en esta revisión por estar fuera del alcance de archivos leídos). La especificación consolidada las presenta al mismo nivel que las reglas genuinamente nuevas del Lote 6 (recepción, putaway, picking, conteo, cross-dock, ajustes) sin marcar la distinción 'ya construido, solo integrar' vs 'nuevo por construir'. Falta revisar src/Teikem.Infrastructure/Services/OrderService.cs (o equivalente) para confirmar si la lógica de generación de PackBatchNumber/ClientInvoiceNumber/PackageNumber ya vive en código, antes de que el plan del Lote 6 la vuelva a diseñar desde cero.
- Persiste sin resolver (confirmado, no corregido): no existe tabla para la resolución de faltantes de PO (PO_ADJUSTMENTS del mock) — PurchaseOrderLine solo tiene QtyOrdered/QtyReceived/UnitCost/LineTotal (columna computada), sin lugar para registrar acción tomada (Cerrar/Reordenar/Ajuste manual), motivo, fecha ni usuario. Confirmado leyendo Diseño/logistica-db-estructura.sql:1723-1731.
- Persiste sin resolver (confirmado): no existe tabla de plantillas de exportación (ACCT_TEMPLATES) en el SQL para ningún alcance (compras/despacho/facturación/liquidación). Si 'Ajustes de inventario'/Contabilización de compras del Lote 6 requiere exponer ya el selector de plantilla (aunque sea solo lectura, delegando la contabilización en sí al Lote 10), falta decidir si la tabla se crea aquí o se pospone completa a Lote 10.
- Persiste sin resolver (confirmado): no existe entidad EF para NINGUNA tabla de este módulo (Warehouse, WarehouseZone, WarehouseBin, WarehouseDock, Product, InventoryLot, InventorySerial, StockBalance, InventoryTransaction, WarehouseTask, PickWave, PickTask, Carton, CartonLine, CycleCount, CycleCountLine, DockAppointment, CrossDockPlan, CrossDockAllocation, PurchaseOrder/Line, ReceiptHeader/Line) — confirmado con búsqueda en src/ (cero archivos coinciden). Todo el mapeo EF (Configurations/) se construye desde cero en este lote.
- Persiste sin resolver (confirmado): ningún permiso de PermissionCatalog.cs cubre inventory.*, warehouse.manage, picking.*, crossdock.view/manage citados en los endpoints propuestos — solo existen warehouse.receive/pick/count/crossdock (categoría WAREHOUSE, líneas 30-33 y 93-96 de PermissionCatalog.cs). Cualquier endpoint de mantenimiento de almacén/producto/ajuste manual necesita permisos nuevos a definir y sembrar (código + seed SQL).
- Vehicle.HomeWarehouseId y Driver.HomeWarehouseId siguen sin mapear en EF (confirmado en docs/lote4-decisiones.md:234 y docs/lote4-plan.md:1297/1339), y Vehicle/Driver no tienen PublicId/RowVersion/CreatedAtUtc pese al principio general de CLAUDE.md — si el Lote 6 mapea Warehouse por primera vez, hay que decidir si también resuelve estas dos FKs pendientes de lotes previos o las deja explícitamente para otro lote.

### reutilizar

- src/Teikem.Infrastructure/Persistence/... (TeikemDbContext y filtro global ITenantScoped) — aplicar a Warehouse, Product, StockBalance, InventoryTransaction, PickWave, CycleCount, DockAppointment, CrossDockPlan (todas tienen TenantId); InventoryLot, InventorySerial, WarehouseZone, WarehouseBin/Dock, CargoLine, CartonLine, PickTask, ReceiptLine, CrossDockAllocation NO tienen TenantId propio y heredan tenant vía FK (deben tratarse como scoped indirecto, no ITenantScoped directo)
- StatusService.TransitionAsync — para todos los StatusCodeId listados en la sección de estatus (Warehouse, WarehouseDock, InventorySerial, ReceiptHeader, WarehouseTask, PickWave, PickTask, Carton, CycleCount, DockAppointment, CrossDockPlan, CrossDockAllocation)
- ILookupCache — para resolver LookupCode de Country, ZoneType, DockType, UnitOfMeasure, TrackingType, InventoryTxnType, ReceiptType, WarehouseTaskType, CartonType, DockDirection, PackageType, EntityType (patrón RefEntityLookupId/RefId)
- [AuditEntity(...)] — patrón ya usado en otras tablas con RowVersion (Warehouse, Product, ReceiptHeader, Trip) para auditoría automática
- Patrón RefEntityLookupId + RefId ya usado en InventoryTransaction y WarehouseTask — mismo patrón polimórfico que ContactPoint/CustomFieldValue mencionado en CLAUDE.md, requeriría IOwnedEntityResolver si se expone edición directa
- StatusService (src/Teikem.Infrastructure/Services/StatusService.cs): TransitionAsync(statusDomain, entityTypeCode, entityId, fromStatusId, toCode, comment, ct) para cualquier cambio de estatus con historial (EntityStatusHistory) y efectos (IStatusTransitionEffect); EnsureAllowedAsync(entityTypeCode, statusCodeId, capabilityCode, ct) para validar acciones permitidas en el estatus actual. No implementar transiciones de estatus a mano.
- ContactPointService (src/Teikem.Infrastructure/Services/ContactPointService.cs): GetForOwnerAsync/AddAsync/UpdateAsync/DeactivateAsync sobre owner polimórfico (ownerEntity+ownerId) vía IOwnedEntityResolver; reutilizable si alguna entidad del módulo (ubicación de almacén, proveedor) necesita teléfonos/correos.
- CustomFieldService (src/Teikem.Infrastructure/Services/CustomFieldService.cs): GetDefinitionsAsync/CreateDefinitionAsync/UpdateDefinitionAsync/GetValuesAsync/SetValuesAsync; reutilizable si el documento pide campos personalizados sobre Item/Lot/Serial/StorageLocation. Requiere registrar el EntityType y un IOwnedEntityResolver propio.
- IDataSource / IDataSourceRegistry (src/Teikem.Infrastructure/Analytics/DataSources.cs): interfaz a implementar para exponer entidades de inventario (Item, Lot, StorageLocation, movimiento) al motor de vistas/indicadores/gráficos; registro en src/Teikem.Infrastructure/DependencyInjection.cs junto a los AddScoped<IDataSource, ...> existentes (patrón: VehicleDataSource, TripDataSource, etc., líneas ~152-191).
- PermissionCatalog (src/Teikem.Domain/Constants/PermissionCatalog.cs): ya trae warehouse.receive, warehouse.pick, warehouse.count, warehouse.crossdock listos para usar (categoría WAREHOUSE); seguir la convención recurso.acción para cualquier permiso nuevo (p.ej. inventory.* o lotserial.*) y añadirlo tanto en All como en PermissionSeeder/seed SQL.
- ModuleKeys (src/Teikem.Domain/Constants/CatalogDomains.cs líneas 434-449): ya existen WmsLotSerial ("WMS_LOTSERIAL") y CrossDock ("CROSSDOCK") para decorar controladores con [RequireModule(...)].
- Patrón de controlador delgado + Contracts/DTOs (ej. ClientsController.cs, OrdersController.cs): controller sin lógica, [Authorize] + [RequireModule] a nivel de clase, [RequirePermission] por acción, delega en un Service inyectado por constructor primario, retorna DTOs de Infrastructure/Contracts; usar publicId (Guid) en rutas externas, baja lógica vía /deactivate y /reactivate devolviendo NoContent, PATCH para actualizaciones parciales con soporte de rowVersion.
- Patrón de estación de escaneo (ScanController.cs + OutboundScanService.cs): útil como modelo para escaneo de recepción/pick/cross-dock del Lote 6 — endpoint que siempre responde 200 con resultado tipado por enum de decisión, valida solo tamaño/vacío del código con 400, la solicitud no lleva TenantId ni ids internos (se resuelven server-side desde el principal), y no permite mutar entidades fuera de su alcance declarado.
- Excepciones de dominio y ProblemDetails (NotFoundException, ValidationException, ConflictException, StatusRuleException, ForbiddenException) manejadas por middleware existente: los nuevos servicios deben lanzarlas en vez de retornar códigos HTTP manualmente.
- ILookupCache para resolver LookupCode por Entity+InternalCode (catálogos de inventario: tipo de movimiento, unidad, etc.) en vez de strings sueltos.
