/* ============================================================================
   TEIKEM — CAMBIOS A LA BASE DE DATOS DESDE LA PUESTA EN PRODUCCIÓN (2026-10-09)
   ESTRUCTURA Y DATOS en UN SOLO archivo, IDEMPOTENTE: se puede correr cuantas veces
   haga falta (db-init y db-update lo aplican después de estructura y seed).
   ----------------------------------------------------------------------------
   Reglas:
   - logistica-db-estructura.sql y logistica-db-seed.sql están CONGELADOS: no se editan.
     Todo cambio nuevo (tablas, columnas, índices, vistas, catálogos, permisos, datos)
     se AGREGA AL FINAL de este archivo, en una sección con su fecha y descripción.
   - Cada sección debe poder correr dos veces sin daño:
       columnas : IF COL_LENGTH('dbo.T','C') IS NULL ALTER TABLE ...
       tablas   : IF OBJECT_ID('dbo.T','U') IS NULL CREATE TABLE ...
       datos    : MERGE o IF NOT EXISTS ... INSERT
   - Lotes separados con GO. Las FKs respetan el orden de capas. No se borra nada con datos.
   - Las secciones ya publicadas no se reescriben: si algo cambia, se agrega otra sección.
   ============================================================================ */

/* ----------------------------------------------------------------------------
   2026-10-10 — Permiso warehouse.transfer (transferir inventario entre posiciones del
   mismo almacén desde el aparato). inventory.adjust lo implica (PermissionCatalog.Implied).
   El PermissionSeeder (código) lo siembra igual y lo propaga a los roles de los tenants;
   esta sección deja el espejo en la base.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'warehouse.transfer')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('warehouse.transfer',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'WAREHOUSE'),
            N'{"es":"Transferir inventario entre posiciones (aparato)","en":"Transfer stock between bins (device)"}', 1);
GO

-- Plantilla de sistema WarehouseOperator (TenantId NULL): recibe el permiso
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'warehouse.transfer'
WHERE r.TenantId IS NULL AND r.Name = 'WarehouseOperator'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-10 — Permiso warehouse.adjust (ajustar la CANTIDAD de una posición desde el aparato;
   sube o baja, nunca mueve). NINGUNA plantilla de rol lo trae: el administrador lo asigna a un
   rol propio (Sistema → Roles). TenantAdmin lo recibe por "todos" (PermissionSeeder).
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'warehouse.adjust')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('warehouse.adjust',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'WAREHOUSE'),
            N'{"es":"Ajustar la cantidad de una posición (aparato)","en":"Adjust a bin''s quantity (device)"}', 1);
GO

/* ----------------------------------------------------------------------------
   2026-10-10 (b) — Corrección: los permisos warehouse.transfer y warehouse.adjust no llegaron a los roles YA
   CLONADOS de cada compañía. El PermissionSeeder solo propaga a los roles clonados los códigos que son NUEVOS
   para la plantilla en su corrida, y la sección anterior ya los había puesto en la plantilla (WarehouseOperator)
   o en la base antes de que corriera, así que "Operador de almacén" y "Admin de compañía" de cada compañía
   quedaron sin ellos y el aparato no mostraba Transferir ni Ajustar.
   - warehouse.transfer → roles de compañía "WarehouseOperator" y "TenantAdmin".
   - warehouse.adjust   → solo "TenantAdmin" (ninguna otra plantilla lo trae; el administrador lo asigna a un rol propio).
   Solo AGREGA (nunca quita). Idempotente.
   ---------------------------------------------------------------------------- */
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'warehouse.transfer'
WHERE r.TenantId IS NOT NULL AND r.Name IN ('WarehouseOperator', 'TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'warehouse.adjust'
WHERE r.TenantId IS NOT NULL AND r.Name = 'TenantAdmin'
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-10 — Conteo cíclico: cierre automático al terminar si el conteo cuadra.
   Ajuste de compañía Tenant.CountAutoCloseMatching (APAGADO por defecto: las compañías ya en producción no cambian de
   comportamiento hasta que el administrador lo enciende en Ajustes de la compañía → Operaciones). Encendido, terminar un
   conteo que cuadra (todo contado y nada que ajustar contra el saldo actual) lo deja en Concordancia sin pasar por la web.
   Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'CountAutoCloseMatching') IS NULL
    ALTER TABLE dbo.Tenant ADD CountAutoCloseMatching BIT NOT NULL
        CONSTRAINT DF_Tenant_CountAutoCloseMatching DEFAULT 0;
GO

/* ----------------------------------------------------------------------------
   2026-10-11 — Despacho manual (DMA-#####): salida de inventario SIN entrega, con motivo obligatorio,
   nota libre, numeración propia y permiso propio. Reutiliza la recolección (dbo.PickBatch): una fila con
   ManualIssueReasonId NO NULO es un despacho manual (nunca se empaca).
   - Contador MANUALISSUE (DMA-#####, por tenant, ClientId NULL): se amplía CK_NumberSequence_Kind. La fila
     del contador la crea el API (NumberSequenceService.EnsureAsync); no consume PACKBATCH (EMP).
   - dbo.PickBatch: ManualIssueReasonId (FK a LookupCode, Entity='ManualIssueReason') y Note NVARCHAR(500);
     índice filtrado para el filtro kind=MANUAL de la lista.
   - Catálogo ManualIssueReason (editable por compañía en Sistema → Catálogos, como DamageFinalDestination):
     SAMPLE Muestra, INTERNAL_USE Uso interno, CUSTOMER_PICKUP Retiro del cliente, SALE Venta, OTHER Otro.
   - Permiso warehouse.issue → plantillas WarehouseOperator y TenantAdmin Y los roles YA CLONADOS de cada
     compañía con esos nombres (el PermissionSeeder no lo propagaría: esta sección ya lo pone en la plantilla).
   Solo AGREGA (nunca quita). Idempotente.
   ---------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND parent_object_id = OBJECT_ID('dbo.NumberSequence')
           AND definition NOT LIKE '%MANUALISSUE%')
BEGIN
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE'));
END
GO

IF COL_LENGTH('dbo.PickBatch', 'ManualIssueReasonId') IS NULL
    ALTER TABLE dbo.PickBatch ADD ManualIssueReasonId INT NULL;
GO
IF COL_LENGTH('dbo.PickBatch', 'Note') IS NULL
    ALTER TABLE dbo.PickBatch ADD Note NVARCHAR(500) NULL;
GO
IF OBJECT_ID('dbo.FK_PickBatch_ManualIssueReason', 'F') IS NULL
    ALTER TABLE dbo.PickBatch ADD CONSTRAINT FK_PickBatch_ManualIssueReason FOREIGN KEY (ManualIssueReasonId) REFERENCES dbo.LookupCode(LookupCodeId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PickBatch_Tenant_Manual' AND object_id = OBJECT_ID('dbo.PickBatch'))
    CREATE INDEX IX_PickBatch_Tenant_Manual ON dbo.PickBatch(TenantId, CollectedAtUtc) INCLUDE (ManualIssueReasonId) WHERE ManualIssueReasonId IS NOT NULL;
GO

-- Catálogo ManualIssueReason (dominio global de sistema; la compañía renombra o deshabilita con su override)
MERGE dbo.CatalogDomain AS t
USING (SELECT N'ManualIssueReason' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'ManualIssueReason', 1, N'{"es":"Motivo del despacho manual","en":"Manual issue reason"}', 1, 1);
GO

MERGE dbo.LookupCode AS t
USING (VALUES
    (N'SAMPLE',          N'{"es":"Muestra","en":"Sample"}',                 1),
    (N'INTERNAL_USE',    N'{"es":"Uso interno","en":"Internal use"}',        2),
    (N'CUSTOMER_PICKUP', N'{"es":"Retiro del cliente","en":"Customer pickup"}', 3),
    (N'SALE',            N'{"es":"Venta","en":"Sale"}',                     4),
    (N'OTHER',           N'{"es":"Otro","en":"Other"}',                     9)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'ManualIssueReason' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'ManualIssueReason', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Permiso warehouse.issue
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'warehouse.issue')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('warehouse.issue',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'WAREHOUSE'),
            N'{"es":"Despacho manual (salida sin entrega)","en":"Manual issue (stock out without delivery)"}', 1);
GO

-- Plantillas de sistema (TenantId NULL) y roles ya clonados de cada compañía: WarehouseOperator y TenantAdmin
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'warehouse.issue'
WHERE r.Name IN ('WarehouseOperator', 'TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-11 (b) — Motivo por default del despacho manual, por compañía (Ajustes de la compañía).
   Para no complicar el despacho en el aparato: la app y la web PRESELECCIONAN este motivo; el servidor
   sigue exigiendo motivo en cada despacho. NULL = sin default (así quedan todas las compañías: no se
   siembra ningún valor). Si la compañía luego deshabilita o se inactiva ese motivo, el API lo ignora
   (no preselecciona) sin borrarlo.
   - dbo.Tenant.DefaultManualIssueReasonLookupId INT NULL, FK a LookupCode (Entity='ManualIssueReason').
   Solo AGREGA (nunca quita). Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'DefaultManualIssueReasonLookupId') IS NULL
    ALTER TABLE dbo.Tenant ADD DefaultManualIssueReasonLookupId INT NULL;
GO
IF OBJECT_ID('dbo.FK_Tenant_DefaultManualIssueReason', 'F') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT FK_Tenant_DefaultManualIssueReason
        FOREIGN KEY (DefaultManualIssueReasonLookupId) REFERENCES dbo.LookupCode(LookupCodeId);
GO

/* ----------------------------------------------------------------------------
   2026-10-11 (c) — Categoría por defecto de los productos NUEVOS, por compañía (Ajustes de la compañía → Operación).
   Tenant.DefaultProductCategoryId (NULL = ninguna): la pantalla de Productos la preselecciona al crear un producto; la API no
   la aplica sola. FK a ProductCategory (para quitarla se pone NULL; las categorías se dan de baja con IsActive = 0).
   Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'DefaultProductCategoryId') IS NULL
    ALTER TABLE dbo.Tenant ADD DefaultProductCategoryId INT NULL;
GO

IF OBJECT_ID('dbo.FK_Tenant_DefaultProductCategory', 'F') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT FK_Tenant_DefaultProductCategory
        FOREIGN KEY (DefaultProductCategoryId) REFERENCES dbo.ProductCategory(ProductCategoryId);
GO

/* Advance Depot: la categoría por defecto es AxisCare. La migración de Depot la creó como «AxisCare» (junto); se busca ignorando
   espacios y mayúsculas («Axis Care» también sirve). Solo se llena si todavía está vacía (nunca pisa lo que el administrador ya eligió)
   y si la categoría existe y está activa: si no existe, no hace nada y se elige en Ajustes → Operación. */
UPDATE t
SET t.DefaultProductCategoryId = c.ProductCategoryId
FROM dbo.Tenant t
JOIN dbo.ProductCategory c ON c.TenantId = t.TenantId AND c.IsActive = 1
WHERE t.Name = N'Advance Depot'
  AND t.DefaultProductCategoryId IS NULL
  AND REPLACE(c.Name, N' ', N'') = N'AxisCare';
GO

/* ----------------------------------------------------------------------------
   2026-10-11 (d) — Cerrar una ruta (Lote 33): permiso trips.close.
   POST /api/v1/trips/{id}/complete (IN_PROGRESS -> COMPLETED) solo si todas las ordenes de la ruta terminaron.
   - Permiso trips.close -> plantillas Dispatcher y TenantAdmin Y los roles YA CLONADOS de cada compania con esos nombres
     (el PermissionSeeder no lo propagaria). Solo AGREGA (nunca quita). Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'trips.close')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('trips.close',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'TRIPS'),
            N'{"es":"Cerrar rutas","en":"Close trips"}', 1);
GO

INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'trips.close'
WHERE r.Name IN ('Dispatcher', 'TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-11 (e) — Rotacion del inventario FEFO / FIFO configurable (Lote 34).
   - dbo.Tenant.RotationMethod VARCHAR(10) NOT NULL DEFAULT 'FEFO' (CHECK FEFO|FIFO): metodo de la compania; FEFO para todas las existentes
     (asi era siempre).
   - dbo.Product.RotationMethod VARCHAR(10) NULL (CHECK FEFO|FIFO): excepcion del producto; NULL = el de la compania.
   - dbo.StockBalance.FirstReceivedAtUtc DATETIME2 NULL: fecha de PRIMERA ENTRADA del saldo (la usa FIFO; la fija el ledger). Se rellena
     una sola vez con la primera entrada que registra el Kardex en esa posicion/lote (si no hay, con UpdatedAtUtc); un saldo sin fecha
     queda al final del orden FIFO.
   Solo AGREGA (nunca quita). Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'RotationMethod') IS NULL
    ALTER TABLE dbo.Tenant ADD RotationMethod VARCHAR(10) NOT NULL CONSTRAINT DF_Tenant_RotationMethod DEFAULT 'FEFO';
GO
IF OBJECT_ID('dbo.CK_Tenant_RotationMethod', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_RotationMethod CHECK (RotationMethod IN ('FEFO', 'FIFO'));
GO
IF COL_LENGTH('dbo.Product', 'RotationMethod') IS NULL
    ALTER TABLE dbo.Product ADD RotationMethod VARCHAR(10) NULL;
GO
IF OBJECT_ID('dbo.CK_Product_RotationMethod', 'C') IS NULL
    ALTER TABLE dbo.Product ADD CONSTRAINT CK_Product_RotationMethod CHECK (RotationMethod IS NULL OR RotationMethod IN ('FEFO', 'FIFO'));
GO
IF COL_LENGTH('dbo.StockBalance', 'FirstReceivedAtUtc') IS NULL
    ALTER TABLE dbo.StockBalance ADD FirstReceivedAtUtc DATETIME2 NULL;
GO

UPDATE sb
SET sb.FirstReceivedAtUtc = COALESCE(
        (SELECT MIN(t.CreatedAtUtc)
         FROM dbo.InventoryTransaction t
         WHERE t.ProductId = sb.ProductId AND t.ToWarehouseId = sb.WarehouseId
           AND ISNULL(t.ToBinId, -1) = ISNULL(sb.WarehouseBinId, -1) AND ISNULL(t.LotId, -1) = ISNULL(sb.LotId, -1)),
        sb.UpdatedAtUtc)
FROM dbo.StockBalance sb
WHERE sb.FirstReceivedAtUtc IS NULL AND sb.QtyOnHand > 0;
GO

/* ----------------------------------------------------------------------------
   2026-10-11 (f) — Importación CSV de consignatarios (Lote 38, F-B). Reutiliza dbo.ImportTemplate/dbo.ImportBatch con
   Kind='CONSIGNEE'. ImportBatch.ClientId pasa a NULL (lote de compartidos o con cliente por fila); los lotes ORDER siguen
   exigiendo cliente (CK_ImportBatch_OrderClient). Se quita y se recrea IX_ImportBatch_Client (depende de la columna).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ImportBatch') AND name = 'ClientId' AND is_nullable = 0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ImportBatch_Client' AND object_id = OBJECT_ID('dbo.ImportBatch'))
        DROP INDEX IX_ImportBatch_Client ON dbo.ImportBatch;
    ALTER TABLE dbo.ImportBatch ALTER COLUMN ClientId INT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ImportBatch_Client' AND object_id = OBJECT_ID('dbo.ImportBatch'))
    CREATE INDEX IX_ImportBatch_Client ON dbo.ImportBatch(TenantId, ClientId);
GO
IF OBJECT_ID('dbo.CK_ImportBatch_OrderClient', 'C') IS NULL
    ALTER TABLE dbo.ImportBatch ADD CONSTRAINT CK_ImportBatch_OrderClient CHECK (Kind <> 'ORDER' OR ClientId IS NOT NULL);
GO

/* ----------------------------------------------------------------------------
   2026-10-11 (g) — Motivo de despacho manual «Retiro del dueño» (OWNER_PICKUP). Va aquí y no dentro de la sección publicada del
   despacho manual porque el MERGE de aquella solo INSERTA lo que falta: en una base que ya la corrió no habría reordenado nada.
   - Agrega OWNER_PICKUP (sistema, activo) y deja el orden SAMPLE 1, INTERNAL_USE 2, OWNER_PICKUP 3, CUSTOMER_PICKUP 4, SALE 5, OTHER 9.
   Solo toca el orden de los motivos de sistema (no el nombre ni el estado que una compañía haya puesto con su override). Idempotente.
   ---------------------------------------------------------------------------- */
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'SAMPLE',          N'{"es":"Muestra","en":"Sample"}',                 1),
    (N'INTERNAL_USE',    N'{"es":"Uso interno","en":"Internal use"}',        2),
    (N'OWNER_PICKUP',    N'{"es":"Retiro del dueño","en":"Owner pickup"}',   3),
    (N'CUSTOMER_PICKUP', N'{"es":"Retiro del cliente","en":"Customer pickup"}', 4),
    (N'SALE',            N'{"es":"Venta","en":"Sale"}',                     5),
    (N'OTHER',           N'{"es":"Otro","en":"Other"}',                     9)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'ManualIssueReason' AND t.InternalCode = s.Code
WHEN MATCHED AND t.SortOrder <> s.Srt THEN
    UPDATE SET SortOrder = s.Srt
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'ManualIssueReason', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

/* ----------------------------------------------------------------------------
   2026-10-12 (a) — Ver precios de las órdenes (Lote 39): permiso orders.view_price.
   Sin él, el API oculta el monto cotizado de las órdenes (lista, ficha, confirmar/reprecio, Análisis), responde 403 en
   GET /orders/{id}/quote y quita las cifras del crédito del 422 credit_exceeded. El monto COD sigue visible.
   - Permiso orders.view_price (categoría ORDERS) -> plantillas TenantAdmin y Billing Y los roles YA CLONADOS de cada
     compañía con esos nombres (el PermissionSeeder no lo propagaría: esta sección ya lo pone en la plantilla).
   Solo AGREGA (nunca quita). Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'orders.view_price')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('orders.view_price',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ORDERS'),
            N'{"es":"Ver precios de las órdenes","en":"View order prices"}', 1);
GO

INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'orders.view_price'
WHERE r.Name IN ('TenantAdmin', 'Billing')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-12 (b) — Índices para buscar sin recorrer TransportOrder ni Trip (verificación del Lote 41 con varias compañías
   y volumen sintético: docs/lote41-decisiones.md). El filtro global de tenant de EF llega a SQL como
   "(@bypass = 1 OR TenantId = @tid)" y con ese OR SQL Server no puede buscar por los índices que empiezan por TenantId:
   estas búsquedas recorrían la tabla completa con las filas de TODAS las compañías (y, con READ COMMITTED de bloqueo,
   chocaban con las escrituras de otras compañías). Cada índice empieza por una columna que ya identifica la fila:
   - IX_TransportOrder_PublicId / IX_Trip_PublicId: orden o ruta por PublicId (ficha, agregar/quitar de ruta, bloqueo de la
     ruta por PublicId). Medido a 508 mil órdenes / 10 mil rutas: 17.291 -> 6 lecturas y 202 -> 4.
   - IX_Order_Client_Balance: saldo de crédito del cliente al crear o confirmar una orden (Σ QuotedAmount de las órdenes en
     curso del cliente), dentro de la transacción del alta. Filtrado igual que la consulta para que nada más lo use. Medido:
     17.291 -> 156 lecturas; con 4 compañías creando órdenes a la vez, de 175-202 interbloqueos a 0.
   - IX_Order_OrderNumber / IX_Order_PackBatchNumber / IX_Order_ClientInvoiceNumber: búsqueda exacta del escaneo
     (número de orden, empaque o factura) y revisión de factura repetida. Medido: 17.506 -> 12 lecturas.
   - IX_Trip_PlanDate: rutas de un día (otras rutas del mismo chofer o vehículo en la ficha, rutas abiertas de la zona en el
     escaneo, lista del día). Medido: 204 -> 18 lecturas; sin él, «Planificar el día» de una compañía hacía esperar hasta
     1,3 s a las escrituras de rutas de OTRA compañía (bloqueo de página sobre Trip).
   No son únicos a propósito (los únicos por compañía ya existen): solo AGREGAN. Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransportOrder_PublicId' AND object_id = OBJECT_ID('dbo.TransportOrder'))
    CREATE INDEX IX_TransportOrder_PublicId ON dbo.TransportOrder(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Trip_PublicId' AND object_id = OBJECT_ID('dbo.Trip'))
    CREATE INDEX IX_Trip_PublicId ON dbo.Trip(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Order_Client_Balance' AND object_id = OBJECT_ID('dbo.TransportOrder'))
    CREATE INDEX IX_Order_Client_Balance ON dbo.TransportOrder(ClientId, StatusCodeId) INCLUDE (TenantId, QuotedAmount)
        WHERE IsActive = 1 AND QuotedAmount IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Order_OrderNumber' AND object_id = OBJECT_ID('dbo.TransportOrder'))
    CREATE INDEX IX_Order_OrderNumber ON dbo.TransportOrder(OrderNumber) WHERE IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Order_PackBatchNumber' AND object_id = OBJECT_ID('dbo.TransportOrder'))
    CREATE INDEX IX_Order_PackBatchNumber ON dbo.TransportOrder(PackBatchNumber) WHERE IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Order_ClientInvoiceNumber' AND object_id = OBJECT_ID('dbo.TransportOrder'))
    CREATE INDEX IX_Order_ClientInvoiceNumber ON dbo.TransportOrder(ClientInvoiceNumber) WHERE IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Trip_PlanDate' AND object_id = OBJECT_ID('dbo.Trip'))
    CREATE INDEX IX_Trip_PlanDate ON dbo.Trip(PlanDate) INCLUDE (TenantId, DriverId, VehicleId, DispatchZoneId, StatusCodeId)
        WHERE IsActive = 1;
GO

/* ----------------------------------------------------------------------------
   2026-10-12 (c) — Índice de la lista de órdenes por compañía (Lote 42: filtro de tenant buscable; docs/lote42-decisiones.md).
   Desde el Lote 42 el filtro global de tenant de EF llega a SQL como "[t].[TenantId] = @tid" (sin el OR del bypass), así que
   SQL Server ya puede buscar por los índices que empiezan por TenantId. La lista de órdenes (más recientes primero: ORDER BY
   CreatedAtUtc DESC, TransportOrderId DESC), sus COUNT y sus filtros por estatus, servicio, cliente, fechas y códigos seguían
   recorriendo TransportOrder completo (17.255 páginas a 508 mil órdenes) y ordenándolo en cada página: con este índice leen
   solo las filas de la compañía, ya ordenadas. Medido (508 mil órdenes, 4 compañías): página 1 17.255 -> 318 lecturas;
   COUNT por estatus 17.255 -> 133/895; COUNT de búsqueda de la compañía chica 264.039 -> 14.994; «Ordenar todo lo filtrado»
   (10.000 filas) 16,4 s -> 2,5 s. Filtrado por IsActive = 1 como la lista. No es único: solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Order_Tenant_Created' AND object_id = OBJECT_ID('dbo.TransportOrder'))
    CREATE INDEX IX_Order_Tenant_Created ON dbo.TransportOrder(TenantId, CreatedAtUtc DESC, TransportOrderId DESC)
        INCLUDE (ClientId, StatusCodeId, ServiceTypeLookupId, OrderNumber, ClientInvoiceNumber, PackBatchNumber) WHERE IsActive = 1;
GO

/* ----------------------------------------------------------------------------
   2026-10-12 (d) — Impresoras y plantillas de label (Lote 43, F-G1).
   La impresora es un PERFIL de impresión de la compañía. El servidor nunca abre conexiones a impresoras: imprime el navegador,
   DIRECTO a la impresora de la computadora con QZ Tray (QZ_TRAY, instalado en cada PC) o, de respaldo, con el PDF y el diálogo
   del navegador (BROWSER).
   - dbo.Printer: nombre (único entre las activas), nombre en el sistema (el exacto de Windows/macOS que usa QZ Tray; NULL = el
     nombre), modelo, nota, uso LABEL | DOCUMENT (fijo después del alta), conexión BROWSER | QZ_TRAY, tamaño 4X6 | 4X4 | 4X2
     (labels) o LETTER (documentos), «Girar 90°», predeterminada por uso (una entre las activas) y activa.
   - dbo.LabelTemplate: nombre (único entre las activas), construcción del label en JSON canónico (la valida el API) y
     predeterminada (una por compañía entre las activas).
   - dbo.Tenant.PrintLabelsDirect BIT NOT NULL DEFAULT 0: ajuste de la compañía «Imprimir labels directo a la impresora» (apagado:
     ninguna compañía cambia de comportamiento hasta que lo enciende).
   - dbo.Client.AutoPrintInvoice BIT NOT NULL DEFAULT 0: bandera por cliente «Imprimir la factura automáticamente» (solo la
     bandera; la impresión de la factura llega con facturación).
   - EntityType PRINTER y LABEL_TEMPLATE (bitácora).
   - Permiso printers.manage (categoría ADMIN) -> plantilla TenantAdmin Y los roles YA CLONADOS de cada compañía con ese nombre
     (el PermissionSeeder no lo propagaría: esta sección ya lo pone en la plantilla).
   Primera tabla creada en este archivo: el IF y el CREATE TABLE van en la MISMA línea. Solo AGREGA (nunca quita). Idempotente.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Printer', N'U') IS NULL CREATE TABLE dbo.Printer (
    PrinterId      INT IDENTITY(1,1) CONSTRAINT PK_Printer PRIMARY KEY,
    PublicId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Printer_PublicId DEFAULT NEWID(),
    TenantId       INT NOT NULL CONSTRAINT FK_Printer_Tenant REFERENCES dbo.Tenant(TenantId),
    Name           NVARCHAR(80) NOT NULL,
    SystemName     NVARCHAR(200) NULL,
    Model          NVARCHAR(80) NULL,
    Notes          NVARCHAR(300) NULL,
    Purpose        VARCHAR(10) NOT NULL,
    ConnectionType VARCHAR(12) NOT NULL CONSTRAINT DF_Printer_ConnectionType DEFAULT 'BROWSER',
    MediaSize      VARCHAR(10) NOT NULL,
    RotateMedia    BIT NOT NULL CONSTRAINT DF_Printer_RotateMedia DEFAULT 0,
    IsDefault      BIT NOT NULL CONSTRAINT DF_Printer_IsDefault DEFAULT 0,
    IsActive       BIT NOT NULL CONSTRAINT DF_Printer_IsActive DEFAULT 1,
    CreatedAtUtc   DATETIME2 NOT NULL CONSTRAINT DF_Printer_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy      INT NULL CONSTRAINT FK_Printer_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc   DATETIME2 NULL,
    UpdatedBy      INT NULL CONSTRAINT FK_Printer_UpdatedBy REFERENCES dbo.AspNetUsers(Id),
    RowVersion     ROWVERSION,
    CONSTRAINT UQ_Printer_PublicId UNIQUE (PublicId),
    CONSTRAINT CK_Printer_Purpose CHECK (Purpose IN ('LABEL', 'DOCUMENT')),
    CONSTRAINT CK_Printer_Connection CHECK (ConnectionType IN ('BROWSER', 'QZ_TRAY')),
    CONSTRAINT CK_Printer_Media CHECK ((Purpose = 'LABEL' AND MediaSize IN ('4X6', '4X4', '4X2')) OR (Purpose = 'DOCUMENT' AND MediaSize = 'LETTER'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Printer_Name' AND object_id = OBJECT_ID('dbo.Printer'))
    CREATE UNIQUE INDEX UX_Printer_Name ON dbo.Printer(TenantId, Name) WHERE IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Printer_Default' AND object_id = OBJECT_ID('dbo.Printer'))
    CREATE UNIQUE INDEX UX_Printer_Default ON dbo.Printer(TenantId, Purpose) WHERE IsDefault = 1 AND IsActive = 1;
GO

IF OBJECT_ID(N'dbo.LabelTemplate', N'U') IS NULL CREATE TABLE dbo.LabelTemplate (
    LabelTemplateId INT IDENTITY(1,1) CONSTRAINT PK_LabelTemplate PRIMARY KEY,
    PublicId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_LabelTemplate_PublicId DEFAULT NEWID(),
    TenantId        INT NOT NULL CONSTRAINT FK_LabelTemplate_Tenant REFERENCES dbo.Tenant(TenantId),
    Name            NVARCHAR(80) NOT NULL,
    LayoutJson      NVARCHAR(2000) NOT NULL,
    IsDefault       BIT NOT NULL CONSTRAINT DF_LabelTemplate_IsDefault DEFAULT 0,
    IsActive        BIT NOT NULL CONSTRAINT DF_LabelTemplate_IsActive DEFAULT 1,
    CreatedAtUtc    DATETIME2 NOT NULL CONSTRAINT DF_LabelTemplate_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy       INT NULL CONSTRAINT FK_LabelTemplate_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc    DATETIME2 NULL,
    UpdatedBy       INT NULL CONSTRAINT FK_LabelTemplate_UpdatedBy REFERENCES dbo.AspNetUsers(Id),
    RowVersion      ROWVERSION,
    CONSTRAINT UQ_LabelTemplate_PublicId UNIQUE (PublicId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LabelTemplate_Name' AND object_id = OBJECT_ID('dbo.LabelTemplate'))
    CREATE UNIQUE INDEX UX_LabelTemplate_Name ON dbo.LabelTemplate(TenantId, Name) WHERE IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LabelTemplate_Default' AND object_id = OBJECT_ID('dbo.LabelTemplate'))
    CREATE UNIQUE INDEX UX_LabelTemplate_Default ON dbo.LabelTemplate(TenantId) WHERE IsDefault = 1 AND IsActive = 1;
GO

IF COL_LENGTH('dbo.Tenant', 'PrintLabelsDirect') IS NULL
    ALTER TABLE dbo.Tenant ADD PrintLabelsDirect BIT NOT NULL CONSTRAINT DF_Tenant_PrintLabelsDirect DEFAULT 0;
GO
IF COL_LENGTH('dbo.Client', 'AutoPrintInvoice') IS NULL
    ALTER TABLE dbo.Client ADD AutoPrintInvoice BIT NOT NULL CONSTRAINT DF_Client_AutoPrintInvoice DEFAULT 0;
GO

-- EntityType de la bitácora (el último sembrado es DAMAGE_REPORT, orden 87)
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'PRINTER',        N'{"es":"Impresora","en":"Printer"}',                 88),
    (N'LABEL_TEMPLATE', N'{"es":"Plantilla de label","en":"Label template"}', 89)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Permiso printers.manage
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'printers.manage')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('printers.manage',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ADMIN'),
            N'{"es":"Gestionar impresoras y plantillas de label","en":"Manage printers & label templates"}', 1);
GO

-- Plantilla de sistema (TenantId NULL) y roles ya clonados de cada compañía: TenantAdmin
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'printers.manage'
WHERE r.Name IN ('TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-12 (e) — Escaneo por número de paquete (Lote 44, F-G2). El label de cada paquete lleva en el código de barras el número
   de su paquete (dbo.CargoLine.PackageNumber); la estación de escaneo y GET /orders/lookup lo buscan como cuarta precedencia
   (después de número de orden, empaque y factura) y solo cuando los tres primeros no coinciden. Este índice hace esa búsqueda
   exacta sin recorrer las líneas de carga de todas las compañías. Filtrado como la consulta (líneas activas con número).
   No es único a propósito: el contador de paquetes es por cliente y un número tecleado puede repetirse (el escaneo lo trata
   como ambiguo con su mensaje). Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CargoLine_PackageNumber' AND object_id = OBJECT_ID('dbo.CargoLine'))
    CREATE INDEX IX_CargoLine_PackageNumber ON dbo.CargoLine(PackageNumber) INCLUDE (TransportOrderId)
        WHERE IsActive = 1 AND PackageNumber IS NOT NULL;
GO

/* ----------------------------------------------------------------------------
   2026-10-12 — Filtro Usuario del Kárdex (docs/kardex-filtro-usuario-diseno.md). Las opciones del filtro leen los usuarios distintos de los
   movimientos de la compañía (InventoryTransaction.CreatedBy) y el filtro es una condición más sobre esa columna; este índice evita recorrer
   todo el libro de movimientos. Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InvTxn_Tenant_CreatedBy' AND object_id = OBJECT_ID('dbo.InventoryTransaction'))
    CREATE INDEX IX_InvTxn_Tenant_CreatedBy ON dbo.InventoryTransaction(TenantId, CreatedBy);
GO

/* ----------------------------------------------------------------------------
   2026-10-12 — IP preferida / red de confianza (docs/ip-preferida-mfa-diseno.md). Una compañía carga las IPs o rangos de sus localidades; quien
   inicia sesión desde una de ellas no recibe el pedido del código MFA al entrar (la contraseña sí; las acciones sensibles siguen pidiendo MFA).
   - dbo.TenantTrustedNetwork: la lista por compañía (IP o rango normalizado, activa, vencimiento opcional, nota y quién confirmó que es solo red interna).
   - dbo.Tenant.TrustedNetworkMaxEntries: máximo de entradas por compañía (NULL = sin tope).
   - SecurityEventType TRUSTED_NETWORK: alta, cambio y baja de entradas en la bitácora de seguridad.
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.TenantTrustedNetwork', 'U') IS NULL CREATE TABLE dbo.TenantTrustedNetwork (
    TenantTrustedNetworkId INT IDENTITY(1,1) CONSTRAINT PK_TenantTrustedNetwork PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_TenantTrustedNetwork_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL CONSTRAINT FK_TenantTrustedNetwork_Tenant REFERENCES dbo.Tenant(TenantId),
    Name NVARCHAR(100) NOT NULL,
    Cidr VARCHAR(50) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_TenantTrustedNetwork_IsActive DEFAULT 1,
    ExpiresAtUtc DATETIME2 NULL,
    Note NVARCHAR(300) NULL,
    InternalOnlyConfirmedByUserId INT NULL,
    InternalOnlyConfirmedAtUtc DATETIME2 NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_TenantTrustedNetwork_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL,
    CONSTRAINT UQ_TenantTrustedNetwork UNIQUE (TenantId, Cidr),
    CONSTRAINT UX_TenantTrustedNetwork_PublicId UNIQUE (PublicId)
);
GO

IF COL_LENGTH('dbo.Tenant', 'TrustedNetworkMaxEntries') IS NULL
    ALTER TABLE dbo.Tenant ADD TrustedNetworkMaxEntries INT NULL;
GO
IF OBJECT_ID('dbo.CK_Tenant_TrustedNetworkMaxEntries', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_TrustedNetworkMaxEntries CHECK (TrustedNetworkMaxEntries IS NULL OR TrustedNetworkMaxEntries >= 0);
GO

MERGE dbo.LookupCode AS t
USING (VALUES (N'TRUSTED_NETWORK', N'{"es":"Red de confianza","en":"Trusted network"}', 12)) AS s (Code, LabelJson, Srt)
ON t.Entity = N'SecurityEventType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'SecurityEventType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

/* ----------------------------------------------------------------------------
   2026-10-12 — Ajuste múltiple AJM-###### (docs/ajuste-multiple-diseno.md): poner al día el inventario de un almacén por producto y posición,
   varias a la vez, en UNA operación (todo o nada). Cada línea con diferencia genera un movimiento de tipo Ajuste como el ajuste normal, con motivo
   PHYSICAL_COUNT («Inventario físico», lo asigna el sistema) y este documento como origen (RefEntityType MULTI_ADJUSTMENT).
   - dbo.MultiAdjustment (cabecera) y dbo.MultiAdjustmentLine (producto, posición, lote, cantidad del sistema al guardar, contada, diferencia y el movimiento).
   - Contador MULTIADJUST (AJM-######, por tenant, ClientId NULL): se amplía CK_NumberSequence_Kind. La fila del contador la crea el API.
   - Catálogos: AdjustmentReason PHYSICAL_COUNT y EntityType MULTI_ADJUSTMENT.
   - Permiso inventory.adjust.multi: NO se agrega a ningún rol (el administrador lo da a un rol propio desde Roles).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.MultiAdjustment', 'U') IS NULL CREATE TABLE dbo.MultiAdjustment (
    MultiAdjustmentId INT IDENTITY(1,1) CONSTRAINT PK_MultiAdjustment PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_MultiAdjustment_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL CONSTRAINT FK_MultiAdjustment_Tenant REFERENCES dbo.Tenant(TenantId),
    WarehouseId INT NOT NULL CONSTRAINT FK_MultiAdjustment_Warehouse REFERENCES dbo.Warehouse(WarehouseId),
    Number NVARCHAR(40) NOT NULL,
    Notes NVARCHAR(300) NOT NULL,
    LineCount INT NOT NULL CONSTRAINT DF_MultiAdjustment_LineCount DEFAULT 0,
    ProductCount INT NOT NULL CONSTRAINT DF_MultiAdjustment_ProductCount DEFAULT 0,
    UnitsUp DECIMAL(16,3) NOT NULL CONSTRAINT DF_MultiAdjustment_UnitsUp DEFAULT 0,
    UnitsDown DECIMAL(16,3) NOT NULL CONSTRAINT DF_MultiAdjustment_UnitsDown DEFAULT 0,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_MultiAdjustment_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL,
    CONSTRAINT UX_MultiAdjustment_PublicId UNIQUE (PublicId),
    CONSTRAINT UQ_MultiAdjustment_Number UNIQUE (TenantId, Number)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MultiAdjustment_Tenant_Date' AND object_id = OBJECT_ID('dbo.MultiAdjustment'))
    CREATE INDEX IX_MultiAdjustment_Tenant_Date ON dbo.MultiAdjustment(TenantId, CreatedAtUtc);
GO
IF OBJECT_ID('dbo.MultiAdjustmentLine', 'U') IS NULL CREATE TABLE dbo.MultiAdjustmentLine (
    MultiAdjustmentLineId BIGINT IDENTITY(1,1) CONSTRAINT PK_MultiAdjustmentLine PRIMARY KEY,
    MultiAdjustmentId INT NOT NULL CONSTRAINT FK_MultiAdjustmentLine_Doc REFERENCES dbo.MultiAdjustment(MultiAdjustmentId),
    ProductId INT NOT NULL CONSTRAINT FK_MultiAdjustmentLine_Product REFERENCES dbo.Product(ProductId),
    WarehouseBinId INT NOT NULL CONSTRAINT FK_MultiAdjustmentLine_Bin REFERENCES dbo.WarehouseBin(WarehouseBinId),
    LotId INT NULL CONSTRAINT FK_MultiAdjustmentLine_Lot REFERENCES dbo.InventoryLot(LotId),
    SystemQty DECIMAL(16,3) NOT NULL,
    CountedQty DECIMAL(16,3) NOT NULL,
    Difference DECIMAL(17,3) NOT NULL,
    InventoryTransactionId BIGINT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MultiAdjustmentLine_Doc' AND object_id = OBJECT_ID('dbo.MultiAdjustmentLine'))
    CREATE INDEX IX_MultiAdjustmentLine_Doc ON dbo.MultiAdjustmentLine(MultiAdjustmentId);
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND parent_object_id = OBJECT_ID('dbo.NumberSequence')
           AND definition NOT LIKE '%MULTIADJUST%')
BEGIN
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST'));
END
GO

MERGE dbo.LookupCode AS t
USING (VALUES (N'PHYSICAL_COUNT', N'{"es":"Inventario físico","en":"Physical inventory"}', 12)) AS s (Code, LabelJson, Srt)
ON t.Entity = N'AdjustmentReason' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'AdjustmentReason', s.Code, s.LabelJson, s.Srt, 1, 1);
GO
MERGE dbo.LookupCode AS t
USING (VALUES (N'MULTI_ADJUSTMENT', N'{"es":"Ajuste múltiple","en":"Multi-adjustment"}', 90)) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'inventory.adjust.multi')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('inventory.adjust.multi',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'WAREHOUSE'),
            N'{"es":"Ajuste múltiple de inventario","en":"Multi-position inventory adjustment"}', 1);
GO

/* ----------------------------------------------------------------------------
   2026-10-12 — Eliminar posiciones (docs/posiciones-eliminar-diseno.md): «Desactivar» es temporal (IsActive = 0, se reactiva) y «Eliminar» es
   definitivo (IsRemoved = 1, siempre inactiva, no se reactiva y su código queda libre). La fila se conserva: la posición la referencian saldos,
   movimientos, series, tareas, conteos, recibos, daños, rentas y descuadres.
   - dbo.WarehouseBin: IsRemoved, RemovedAtUtc, RemovedBy y CHECK (IsRemoved = 0 OR IsActive = 0).
   - La unicidad del código (por zona y por almacén) pasa a ser SOLO entre las no eliminadas: UQ_WarehouseBin y UQ_WarehouseBin_WhCode dejan de ser
     restricciones y son índices únicos filtrados con el mismo nombre (las llaves foráneas apuntan a UQ_WarehouseBin_IdWh, que no cambia).
   Solo AGREGA / reemplaza esos dos índices. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.WarehouseBin', 'IsRemoved') IS NULL
    ALTER TABLE dbo.WarehouseBin ADD IsRemoved BIT NOT NULL CONSTRAINT DF_WarehouseBin_IsRemoved DEFAULT 0;
GO
IF COL_LENGTH('dbo.WarehouseBin', 'RemovedAtUtc') IS NULL
    ALTER TABLE dbo.WarehouseBin ADD RemovedAtUtc DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.WarehouseBin', 'RemovedBy') IS NULL
    ALTER TABLE dbo.WarehouseBin ADD RemovedBy INT NULL CONSTRAINT FK_WarehouseBin_RemovedBy REFERENCES dbo.AspNetUsers(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_WarehouseBin_Removed' AND parent_object_id = OBJECT_ID('dbo.WarehouseBin'))
    ALTER TABLE dbo.WarehouseBin ADD CONSTRAINT CK_WarehouseBin_Removed CHECK (IsRemoved = 0 OR IsActive = 0);
GO
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_WarehouseBin' AND parent_object_id = OBJECT_ID('dbo.WarehouseBin'))
    ALTER TABLE dbo.WarehouseBin DROP CONSTRAINT UQ_WarehouseBin;
GO
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_WarehouseBin_WhCode' AND parent_object_id = OBJECT_ID('dbo.WarehouseBin'))
    ALTER TABLE dbo.WarehouseBin DROP CONSTRAINT UQ_WarehouseBin_WhCode;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_WarehouseBin' AND object_id = OBJECT_ID('dbo.WarehouseBin'))
    CREATE UNIQUE INDEX UQ_WarehouseBin ON dbo.WarehouseBin (WarehouseZoneId, Code) WHERE IsRemoved = 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_WarehouseBin_WhCode' AND object_id = OBJECT_ID('dbo.WarehouseBin'))
    CREATE UNIQUE INDEX UQ_WarehouseBin_WhCode ON dbo.WarehouseBin (WarehouseId, Code) WHERE IsRemoved = 0;
GO

/* ----------------------------------------------------------------------------
   2026-10-12 — «Muelle» pasa a «Plataforma» en el módulo de almacén (decisión de Luis): etiquetas en español de la base de datos.
   Cambia SOLO el texto en español del JSON de etiquetas (el inglés conserva «dock»): descripción del módulo CROSSDOCK, nombres de los dominios
   DockType / DockDirection / DockStatus, los tipos de entidad Muelle / Cita de muelle / Asignación de cruce de muelle, el tipo de movimiento
   Cruce de muelle, el evento de actividad «Cruce de muelle completado» y el estatus de plataforma «Ocupado» → «Ocupada».
   Idempotente: solo toca filas que todavía dicen «muelle».
   ---------------------------------------------------------------------------- */
UPDATE dbo.ModuleDefinition
SET Description = REPLACE(REPLACE(Description, N'Muelle', N'Plataforma'), N'muelle', N'plataforma')
WHERE ModuleKey = 'CROSSDOCK' AND Description LIKE N'%uelle%';
GO
UPDATE dbo.CatalogDomain
SET LabelJson = REPLACE(REPLACE(LabelJson, N'Muelle', N'Plataforma'), N'muelle', N'plataforma')
WHERE DomainKey IN ('DockType', 'DockDirection', 'DockStatus') AND LabelJson LIKE N'%uelle%';
GO
UPDATE dbo.LookupCode
SET LabelJson = REPLACE(REPLACE(LabelJson, N'Muelle', N'Plataforma'), N'muelle', N'plataforma')
WHERE LabelJson LIKE N'%uelle%'
  AND ((Entity = 'EntityType' AND InternalCode IN ('WAREHOUSE_DOCK', 'DOCK_APPOINTMENT', 'CROSSDOCK_ALLOCATION', 'CROSSDOCK_PLAN'))
       OR (Entity = 'InventoryTxnType' AND InternalCode = 'CROSSDOCK')
       OR (Entity = 'ActivityEventType' AND InternalCode = 'CROSSDOCK_COMPLETED'));
GO
UPDATE dbo.StatusCode
SET LabelJson = REPLACE(LabelJson, N'"es":"Ocupado"', N'"es":"Ocupada"')
WHERE Entity = 'DockStatus' AND InternalCode = 'OCCUPIED' AND LabelJson LIKE N'%"es":"Ocupado"%';
GO

/* ----------------------------------------------------------------------------
   2026-10-12 — Duración de la sesión en MINUTOS (la pantalla la pide en horas con decimales: 24 = 1 día, 0.25 = 15 minutos).
   dbo.Tenant: SessionMinutes y DeviceSessionMinutes (NULL = sin configurar: vale SessionDays / DeviceSessionDays × 1440, lo de siempre) y su CHECK
   (1 minuto a 365 días). Los días no se tocan: al guardar minutos el sistema también deja los días equivalentes. Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'SessionMinutes') IS NULL
    ALTER TABLE dbo.Tenant ADD SessionMinutes INT NULL;
GO
IF COL_LENGTH('dbo.Tenant', 'DeviceSessionMinutes') IS NULL
    ALTER TABLE dbo.Tenant ADD DeviceSessionMinutes INT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Tenant_SessionMinutes' AND parent_object_id = OBJECT_ID('dbo.Tenant'))
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_SessionMinutes CHECK ((SessionMinutes IS NULL OR SessionMinutes BETWEEN 1 AND 525600) AND (DeviceSessionMinutes IS NULL OR DeviceSessionMinutes BETWEEN 1 AND 525600));
GO

/* ----------------------------------------------------------------------------
   2026-10-13 — Importación de consignatarios, mejoras (Lote 45, F-B2; docs/lote45-decisiones.md).
   - dbo.Location.ContactName: persona de contacto del consignatario (opcional, 150). El teléfono sigue siendo un ContactPoint.
   - dbo.ImportTemplate.DefaultClientId: cliente por defecto de la importación con esa plantilla («todo el archivo es del
     cliente X»; la fila con su propio cliente manda). NULL = compartidos. No limita el uso (eso sigue siendo ClientId).
   - Las columnas «No usar» y las columnas unidas viajan en ColumnsJson (sin cambio de estructura). Sin permisos nuevos.
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Location', 'ContactName') IS NULL
    ALTER TABLE dbo.Location ADD ContactName NVARCHAR(150) NULL;
GO
IF COL_LENGTH('dbo.ImportTemplate', 'DefaultClientId') IS NULL
    ALTER TABLE dbo.ImportTemplate ADD DefaultClientId INT NULL CONSTRAINT FK_ImportTemplate_DefaultClient REFERENCES dbo.Client(ClientId);
GO

/* ----------------------------------------------------------------------------
   2026-10-14 — Catálogo de códigos postales con tipo, distrito y geolocalización; variantes de mapeo en las plantillas de
   importación de consignatarios (Lote 46, F-B3; docs/lote46-decisiones.md).
   - dbo.PostalLocality: columnas que llena el script de DATOS aparte Diseño/logistica-db-zip-catalog.sql (se corre a mano,
     después de esta sección; fuentes USPS «Zip Locale Detail» y GeoNames, CC BY 4.0):
       ZipClass     tipo del ZIP: STANDARD (normal, de entrega), PO_BOX (apartados), UNIQUE (único de una entidad, de entrega)
                    o MILITARY (APO/FPO); NULL = sin clasificar (el script de datos no se ha corrido o el ZIP no está en la fuente).
       LocaleType   tipo de oficina USPS (una letra); DistrictName / DistrictNo: distrito postal.
       Latitude / Longitude (grados, 6 decimales) y GeoAccuracy (precisión GeoNames); NULL si la fuente no las trae.
     Índice IX_PostalLocality_PostalCode (el script de datos cruza por ZIP; la importación lee el catálogo en memoria).
     Una dirección de ENTREGA (consignatario) usa solo STANDARD y UNIQUE; una dirección POSTAL puede usar todos.
   - dbo.ImportTemplate.VariantsJson: variantes de mapeo de una plantilla CONSIGNEE (condición por fila y columnas propias);
     NULL = una sola variante (ColumnsJson), lo de siempre.
   Solo AGREGA. Idempotente. Sin permisos nuevos (nada que propagar).
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.PostalLocality', 'ZipClass') IS NULL
    ALTER TABLE dbo.PostalLocality ADD ZipClass VARCHAR(10) NULL;
GO
IF COL_LENGTH('dbo.PostalLocality', 'LocaleType') IS NULL
    ALTER TABLE dbo.PostalLocality ADD LocaleType CHAR(1) NULL;
GO
IF COL_LENGTH('dbo.PostalLocality', 'DistrictName') IS NULL
    ALTER TABLE dbo.PostalLocality ADD DistrictName NVARCHAR(40) NULL;
GO
IF COL_LENGTH('dbo.PostalLocality', 'DistrictNo') IS NULL
    ALTER TABLE dbo.PostalLocality ADD DistrictNo VARCHAR(3) NULL;
GO
IF COL_LENGTH('dbo.PostalLocality', 'Latitude') IS NULL
    ALTER TABLE dbo.PostalLocality ADD Latitude DECIMAL(9,6) NULL;
GO
IF COL_LENGTH('dbo.PostalLocality', 'Longitude') IS NULL
    ALTER TABLE dbo.PostalLocality ADD Longitude DECIMAL(9,6) NULL;
GO
IF COL_LENGTH('dbo.PostalLocality', 'GeoAccuracy') IS NULL
    ALTER TABLE dbo.PostalLocality ADD GeoAccuracy TINYINT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PostalLocality_ZipClass' AND parent_object_id = OBJECT_ID('dbo.PostalLocality'))
    ALTER TABLE dbo.PostalLocality ADD CONSTRAINT CK_PostalLocality_ZipClass CHECK (ZipClass IS NULL OR ZipClass IN ('STANDARD','PO_BOX','UNIQUE','MILITARY'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PostalLocality_PostalCode' AND object_id = OBJECT_ID('dbo.PostalLocality'))
    CREATE INDEX IX_PostalLocality_PostalCode ON dbo.PostalLocality (PostalCode);
GO
IF COL_LENGTH('dbo.ImportTemplate', 'VariantsJson') IS NULL
    ALTER TABLE dbo.ImportTemplate ADD VariantsJson NVARCHAR(MAX) NULL;
GO
/* ----------------------------------------------------------------------------
   2026-10-14 (b) — Fuera del catálogo los ZIP que ya no existen (pedido de Luis).
   El catálogo sembrado (logistica-db-seed.sql, congelado) traía 1.250 ZIP que NO figuran en el archivo oficial del correo de EE. UU. (Zip Locale Detail,
   41.486 ZIP) —entre ellos 00636 y 00930 de Puerto Rico— y 26 ZIP militares que el boletín postal 22708 (julio de 2026) da por cerrados. Contraste: zip-codes.com no
   tiene 00636, 00930, 01133, 10072 ni 44322. Como el seed vuelve a insertarlos cada vez que se aplica, se borran aquí (update.sql corre después del seed).
   Nada referencia dbo.PostalLocality por clave foránea (los almacenes copian la dirección como texto). Idempotente: si ya no están, no hace nada.
   ---------------------------------------------------------------------------- */
DELETE FROM dbo.PostalLocality WHERE PostalCode IN (
'00636',
'00930',
'01133',
'01195',
'01470',
'01471',
'01477',
'01517',
'01580',
'01582',
'01654',
'01806',
'01807',
'01808',
'02031',
'02207',
'02216',
'02228',
'02239',
'02266',
'02295',
'02340',
'02565',
'02573',
'02636',
'02783',
'02854',
'03107',
'03274',
'03805',
'04075',
'04123',
'04124',
'04467',
'04846',
'05144',
'05544',
'06087',
'06245',
'06386',
'06454',
'06497',
'06650',
'06749',
'06814',
'06816',
'06817',
'06832',
'06842',
'06859',
'06920',
'06921',
'06922',
'06925',
'06928',
'07182',
'07194',
'07309',
'07477',
'07709',
'07829',
'07983',
'08905',
'08922',
'08988',
'09000',
'09001',
'09003',
'09004',
'09006',
'09007',
'09011',
'09013',
'09014',
'09028',
'09031',
'09033',
'09036',
'09038',
'09042',
'09045',
'09051',
'09054',
'09055',
'09056',
'09058',
'09059',
'09063',
'09074',
'09075',
'09076',
'09080',
'09081',
'09086',
'09088',
'09089',
'09090',
'09092',
'09099',
'09100',
'09102',
'09107',
'09110',
'09131',
'09137',
'09138',
'09139',
'09143',
'09165',
'09166',
'09169',
'09172',
'09177',
'09182',
'09183',
'09185',
'09186',
'09201',
'09212',
'09225',
'09226',
'09229',
'09237',
'09244',
'09245',
'09252',
'09262',
'09264',
'09267',
'09300',
'09302',
'09304',
'09305',
'09306',
'09308',
'09310',
'09311',
'09312',
'09313',
'09314',
'09316',
'09318',
'09319',
'09320',
'09322',
'09323',
'09324',
'09327',
'09328',
'09330',
'09331',
'09332',
'09333',
'09334',
'09336',
'09337',
'09338',
'09339',
'09340',
'09342',
'09344',
'09346',
'09347',
'09348',
'09350',
'09351',
'09352',
'09353',
'09354',
'09355',
'09356',
'09358',
'09359',
'09360',
'09361',
'09362',
'09363',
'09364',
'09365',
'09367',
'09368',
'09369',
'09370',
'09371',
'09372',
'09373',
'09374',
'09375',
'09376',
'09377',
'09378',
'09379',
'09380',
'09381',
'09382',
'09383',
'09384',
'09386',
'09387',
'09388',
'09389',
'09390',
'09391',
'09393',
'09394',
'09396',
'09397',
'09399',
'09402',
'09409',
'09420',
'09496',
'09499',
'09515',
'09549',
'09557',
'09574',
'09589',
'09593',
'09601',
'09605',
'09607',
'09611',
'09612',
'09617',
'09619',
'09626',
'09631',
'09644',
'09702',
'09703',
'09713',
'09721',
'09740',
'09746',
'09747',
'09757',
'09758',
'09771',
'09789',
'09790',
'09798',
'09807',
'09811',
'09819',
'09825',
'09835',
'09841',
'09843',
'09852',
'09855',
'09858',
'09865',
'09868',
'09871',
'09872',
'09873',
'09874',
'09875',
'09876',
'09880',
'09888',
'09890',
'10015',
'10046',
'10047',
'10048',
'10072',
'10079',
'10082',
'10094',
'10095',
'10096',
'10098',
'10099',
'10149',
'10161',
'10184',
'10196',
'10197',
'10200',
'10257',
'10292',
'10499',
'10557',
'10558',
'10571',
'10572',
'10943',
'11025',
'11026',
'11027',
'11041',
'11043',
'11044',
'11051',
'11052',
'11053',
'11054',
'11055',
'11099',
'11240',
'11244',
'11248',
'11254',
'11255',
'11381',
'11390',
'11535',
'11536',
'11592',
'11594',
'11595',
'11597',
'11708',
'11736',
'11750',
'11774',
'11819',
'11854',
'11855',
'12252',
'12256',
'12593',
'13837',
'14645',
'14664',
'14673',
'14683',
'14925',
'15123',
'15263',
'15266',
'15273',
'15285',
'15288',
'15740',
'16021',
'16215',
'16532',
'16533',
'16534',
'16554',
'16918',
'17008',
'17012',
'17091',
'17270',
'17326',
'17354',
'17415',
'17583',
'17738',
'17767',
'17773',
'17839',
'17883',
'17942',
'18010',
'18025',
'18050',
'18175',
'18373',
'18448',
'18514',
'18522',
'18690',
'18924',
'18926',
'19080',
'19088',
'19089',
'19098',
'19099',
'19371',
'19388',
'19420',
'19483',
'19485',
'19487',
'19488',
'19489',
'19542',
'19640',
'19887',
'19889',
'20023',
'20046',
'20051',
'20088',
'20097',
'20098',
'20107',
'20193',
'20199',
'20307',
'20409',
'20532',
'20558',
'20635',
'20643',
'21098',
'21260',
'21261',
'21265',
'21268',
'21274',
'21280',
'21283',
'21288',
'21606',
'21681',
'21682',
'21683',
'21684',
'21685',
'21686',
'21687',
'21688',
'21748',
'22047',
'22092',
'22093',
'22120',
'22184',
'22218',
'22222',
'22223',
'22229',
'22234',
'22321',
'22336',
'22721',
'23101',
'23240',
'23512',
'23520',
'23521',
'23825',
'24042',
'24043',
'24044',
'24045',
'24048',
'24468',
'24512',
'24544',
'24842',
'24961',
'25429',
'25926',
'25965',
'26461',
'27151',
'27156',
'27220',
'27321',
'27322',
'27395',
'27480',
'27564',
'27621',
'27854',
'28250',
'28674',
'29318',
'29390',
'29391',
'29430',
'29573',
'29698',
'30073',
'30163',
'30219',
'30270',
'30330',
'30347',
'30376',
'30379',
'30386',
'30387',
'30389',
'30390',
'30399',
'30447',
'30544',
'30596',
'30612',
'30811',
'30911',
'30913',
'31084',
'31120',
'31144',
'31191',
'31195',
'31196',
'31197',
'31198',
'31199'
);
GO
DELETE FROM dbo.PostalLocality WHERE PostalCode IN (
'31209',
'31212',
'31297',
'31760',
'32026',
'32072',
'32215',
'32230',
'32237',
'32267',
'32290',
'32395',
'32454',
'32590',
'32592',
'32613',
'32782',
'32890',
'32893',
'32898',
'33072',
'33107',
'33110',
'33111',
'33121',
'33148',
'33151',
'33159',
'33336',
'33337',
'33349',
'33439',
'33447',
'33651',
'33661',
'33662',
'33663',
'33690',
'33737',
'33900',
'33911',
'34006',
'34043',
'34050',
'34076',
'34079',
'34098',
'34099',
'34278',
'35041',
'35144',
'35225',
'35230',
'35237',
'35240',
'35245',
'35263',
'35277',
'35278',
'35279',
'35280',
'35281',
'35286',
'35289',
'35299',
'35468',
'35485',
'36023',
'36045',
'36061',
'36133',
'36134',
'36210',
'36449',
'36457',
'36462',
'36501',
'36515',
'36621',
'36622',
'36625',
'36630',
'36641',
'36690',
'36721',
'36762',
'36802',
'36859',
'36872',
'37237',
'37245',
'37247',
'37248',
'37249',
'37990',
'38110',
'38142',
'38147',
'38165',
'38227',
'38609',
'38628',
'38630',
'38669',
'38675',
'38749',
'38926',
'38945',
'38955',
'38958',
'39072',
'39081',
'39107',
'39235',
'39250',
'39271',
'39754',
'40366',
'40386',
'40446',
'40495',
'40754',
'40931',
'40999',
'41065',
'41307',
'41313',
'41333',
'41338',
'41362',
'41433',
'41459',
'41747',
'42084',
'42283',
'42287',
'42375',
'42403',
'42731',
'43032',
'43098',
'43163',
'43196',
'43198',
'43265',
'43299',
'43306',
'43307',
'43550',
'43618',
'43789',
'44178',
'44185',
'44189',
'44322',
'44393',
'44399',
'44486',
'44488',
'44631',
'44845',
'44999',
'45025',
'45026',
'45043',
'45110',
'45138',
'45145',
'45228',
'45400',
'45408',
'45418',
'45427',
'45454',
'45463',
'46211',
'46223',
'46266',
'46274',
'46275',
'46291',
'46295',
'46296',
'46604',
'46620',
'47139',
'47261',
'47430',
'47439',
'47490',
'47614',
'47727',
'47739',
'47741',
'47744',
'47811',
'47812',
'47830',
'47856',
'47864',
'47934',
'47935',
'47936',
'47937',
'47938',
'47939',
'47984',
'48391',
'48559',
'48736',
'48769',
'48802',
'48863',
'48921',
'48950',
'49069',
'49121',
'49550',
'49790',
'50347',
'50397',
'50706',
'51344',
'51602',
'52204',
'52319',
'52350',
'52538',
'53199',
'53244',
'53267',
'53268',
'53407',
'53778',
'53779',
'53789',
'53927',
'54010',
'54182',
'54404',
'54432',
'54434',
'54439',
'54472',
'54640',
'54818',
'54834',
'54934',
'54990',
'55002',
'55161',
'55165',
'55166',
'55168',
'55169',
'55171',
'55172',
'55175',
'55177',
'55187',
'55188',
'55191',
'55348',
'55365',
'55380',
'55383',
'55392',
'55393',
'55394',
'55399',
'55460',
'55468',
'55472',
'55473',
'55478',
'55550',
'55551',
'55552',
'55553',
'55554',
'55555',
'55556',
'55557',
'55558',
'55559',
'55560',
'55561',
'55562',
'55563',
'55564',
'55565',
'55566',
'55567',
'55568',
'55570',
'55571',
'55573',
'55574',
'55575',
'55576',
'55578',
'55579',
'55580',
'55581',
'55582',
'55583',
'55584',
'55585',
'55586',
'55587',
'55588',
'55589',
'55590',
'55591',
'55594',
'55595',
'55596',
'55597',
'55598',
'55701',
'55747',
'55942',
'55988',
'56006',
'56177',
'56430',
'56456',
'56478',
'56682',
'56901',
'56902',
'56904',
'56915',
'56920',
'56933',
'56944',
'56945',
'56950',
'56965',
'56972',
'57056',
'57079',
'57188',
'57189',
'57192',
'57194',
'57195',
'57196',
'57253',
'58319',
'59773',
'60001',
'60049',
'60092',
'60125',
'60170',
'60209',
'60290',
'60570',
'60597',
'60663',
'60679',
'62224',
'62247',
'62435',
'62713',
'62721',
'62746',
'62805',
'62855',
'62857',
'62955',
'62971',
'63001',
'63182',
'63190',
'63196',
'63198',
'63464',
'63938',
'63963',
'64172',
'64183',
'64185',
'64192',
'64193',
'64194',
'64447',
'64789',
'64869',
'64944',
'65701',
'65776',
'66019',
'66077',
'66279',
'66628',
'66637',
'66642',
'66652',
'66653',
'66692',
'66759',
'68009',
'68120',
'68155',
'68181',
'69190',
'70140',
'70149',
'70500',
'70595',
'70883',
'71208',
'71768',
'71844',
'71951',
'72105',
'72189',
'72198',
'72439',
'73094',
'73185',
'73193',
'73197',
'73198',
'73199',
'73451',
'73454',
'74183',
'74184',
'74189',
'74194',
'74542',
'75037',
'75245',
'75258',
'75286',
'75310',
'75323',
'75334',
'75340',
'75343',
'75344',
'75353',
'75363',
'75364',
'75386',
'75387',
'75388',
'75396',
'76299',
'76505',
'76545',
'76546',
'76795',
'77097',
'77209',
'77246',
'77247',
'77250',
'77260',
'77276',
'77278',
'77285',
'77286',
'77294',
'77296',
'77298',
'77709',
'77869',
'78049',
'78262',
'78275',
'78286',
'78287',
'78461',
'78470',
'78471',
'78472',
'78473',
'78474',
'78475',
'78476',
'78477',
'78478',
'78769',
'78780',
'78781',
'78785',
'78786',
'78788',
'78789',
'78798',
'79187',
'79320',
'79405',
'79990',
'80028',
'80244'
);
GO
DELETE FROM dbo.PostalLocality WHERE PostalCode IN (
'80251',
'80252',
'80262',
'80271',
'80279',
'80280',
'80281',
'80291',
'80295',
'80321',
'80322',
'80323',
'80328',
'80329',
'80940',
'80943',
'80944',
'80945',
'81075',
'81127',
'81134',
'81153',
'81247',
'82008',
'83230',
'83601',
'83721',
'83727',
'83730',
'83731',
'83733',
'83757',
'84136',
'84141',
'84144',
'84184',
'84189',
'84244',
'84407',
'84717',
'85025',
'85039',
'85055',
'85065',
'85073',
'85075',
'85077',
'85096',
'85097',
'85098',
'85099',
'85217',
'85218',
'85219',
'85220',
'85221',
'85222',
'85223',
'85227',
'85228',
'85230',
'85231',
'85232',
'85235',
'85237',
'85238',
'85239',
'85240',
'85241',
'85242',
'85243',
'85245',
'85247',
'85272',
'85273',
'85278',
'85279',
'85289',
'85290',
'85291',
'85292',
'85293',
'85294',
'85313',
'85662',
'85671',
'85720',
'85722',
'85777',
'86330',
'86555',
'87165',
'88516',
'88901',
'88905',
'89026',
'89067',
'89159',
'89164',
'89185',
'89405',
'89452',
'89555',
'89802',
'89824',
'90090',
'90101',
'90102',
'90103',
'90233',
'90261',
'90313',
'90397',
'90398',
'90612',
'90659',
'90834',
'90835',
'90845',
'90888',
'90899',
'91131',
'91191',
'91329',
'91363',
'91388',
'91399',
'91495',
'91496',
'91497',
'91526',
'91611',
'91612',
'91795',
'91797',
'91798',
'91799',
'91841',
'91947',
'91990',
'92090',
'92133',
'92162',
'92164',
'92184',
'92190',
'92194',
'92292',
'92326',
'92412',
'92414',
'92424',
'92515',
'92709',
'92710',
'92712',
'92725',
'92825',
'92850',
'93093',
'93381',
'93382',
'93780',
'93784',
'94013',
'94101',
'94106',
'94135',
'94136',
'94138',
'94150',
'94152',
'94153',
'94154',
'94155',
'94156',
'94162',
'94171',
'94175',
'94199',
'94207',
'94208',
'94211',
'94234',
'94245',
'94246',
'94282',
'94286',
'94291',
'94625',
'95250',
'95314',
'95887',
'96200',
'96203',
'96207',
'96217',
'96219',
'96220',
'96221',
'96258',
'96259',
'96267',
'96275',
'96313',
'96323',
'96330',
'96343',
'96348',
'96372',
'96373',
'96378',
'96400',
'96426',
'96427',
'96444',
'96447',
'96501',
'96509',
'96510',
'96512',
'96513',
'96514',
'96518',
'96522',
'96536',
'96538',
'96541',
'96544',
'96546',
'96547',
'96606',
'96608',
'96609',
'96614',
'96617',
'96621',
'96622',
'96624',
'96634',
'96687',
'96827',
'96835',
'96919',
'97255',
'97258',
'97259',
'97271',
'97272',
'97299',
'97313',
'97372',
'97425',
'97427',
'97428',
'97472',
'97482',
'98054',
'98132',
'98151',
'98171',
'98184',
'98189',
'98205',
'98442',
'98450',
'98455',
'98460',
'98477',
'98492',
'98667',
'98929',
'99107',
'99165',
'99299',
'99779'
);
GO
/* ----------------------------------------------------------------------------
   2026-10-14 (c) — «Catálogo» pasa a «Listas» (pedido de Luis: «catálogo» se confunde con otras cosas): etiquetas de la base de datos.
   Módulo CATALOG (menú): Catálogo / Catalog -> Listas / Lists. Tipos de entidad: «Valor de catálogo» -> «Valor de lista», «Lista de catálogo / Catalog list»
   -> «Lista de valores / Value list». Tipo de campo personalizado «Referencia a catálogo» -> «Referencia a lista». Permiso admin.catalogs:
   «Gestionar catálogos / Manage catalogs» -> «Gestionar listas / Manage lists». Los códigos internos (CATALOG, admin.catalogs) NO cambian.
   Idempotente: solo toca filas que todavía dicen «catálogo».
   ---------------------------------------------------------------------------- */
UPDATE dbo.ModuleDefinition SET Name = N'Listas', NameEn = N'Lists'
WHERE ModuleKey = 'CATALOG' AND (Name <> N'Listas' OR NameEn <> N'Lists');
GO
UPDATE dbo.LookupCode
SET LabelJson = REPLACE(REPLACE(REPLACE(REPLACE(LabelJson, N'Valor de catálogo', N'Valor de lista'), N'Lista de catálogo', N'Lista de valores'), N'Catalog list', N'Value list'), N'Referencia a catálogo', N'Referencia a lista')
WHERE ((Entity = 'EntityType' AND InternalCode IN ('LOOKUP_CODE', 'CATALOG_DOMAIN')) OR (Entity = 'CustomFieldDataType' AND InternalCode = 'LOOKUP_REF'))
  AND (LabelJson LIKE N'%catálogo%' OR LabelJson LIKE N'%Catalog list%' OR LabelJson LIKE N'%Catalog reference%');
GO
UPDATE dbo.LookupCode
SET LabelJson = REPLACE(LabelJson, N'Catalog reference', N'List reference')
WHERE Entity = 'CustomFieldDataType' AND InternalCode = 'LOOKUP_REF' AND LabelJson LIKE N'%Catalog reference%';
GO
UPDATE dbo.Permission
SET LabelJson = REPLACE(REPLACE(LabelJson, N'Gestionar catálogos', N'Gestionar listas'), N'Manage catalogs', N'Manage lists')
WHERE Code = 'admin.catalogs' AND (LabelJson LIKE N'%catálogos%' OR LabelJson LIKE N'%Manage catalogs%');
GO
/* ----------------------------------------------------------------------------
   2026-10-14 (d) — «Imprimir los labels automáticamente» por cliente (Lote 48): dbo.Client.AutoPrintLabels BIT NOT NULL DEFAULT 0.
   Bandera del cliente (sender), como AutoPrintInvoice: al guardar una orden de ese cliente en la pantalla de órdenes, sus labels salen solos aunque
   el interruptor «Label al guardar» de la estación esté apagado. Apagada por defecto: ningún cliente cambia de comportamiento. Idempotente.
   Además el punto de recogido por defecto (Client.DefaultPickupLocationId) ya puede ser la dirección física o la postal del cliente, no solo un
   punto PICKUP/BOTH: no cambia el esquema (la FK apunta a dbo.Location).
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Client', 'AutoPrintLabels') IS NULL
    ALTER TABLE dbo.Client ADD AutoPrintLabels BIT NOT NULL CONSTRAINT DF_Client_AutoPrintLabels DEFAULT 0;
GO
/* ----------------------------------------------------------------------------
   2026-10-14 (e) — Módulos nuevos (Lote 49, P2.3): SALES_ORDERS (órdenes de venta), TAX (impuestos, IVU) y BILLING (facturación).
   Solo se DEFINEN en dbo.ModuleDefinition: ninguna compañía los tiene encendidos todavía (se encienden por compañía en
   Sistema → Módulos cuando cada uno se construya: P3 impuestos, P4 ventas). Dependencias: SALES_ORDERS -> WMS_LOTSERIAL (descuenta
   inventario); BILLING -> SALES_ORDERS; TAX no depende de nadie (lo usan las ventas y la facturación cuando están encendidas).
   Notificaciones (P2.1) NO es un módulo: va siempre disponible bajo SYSTEM (permiso notifications.manage). Idempotente: MERGE por clave;
   no toca IsCore ni lo que cada compañía tenga encendido.
   ---------------------------------------------------------------------------- */
MERGE dbo.ModuleDefinition AS t
USING (VALUES
 ('SALES_ORDERS', N'Órdenes de venta', N'Sales orders', N'Ventas de productos con pedido, picking, entrega y cobro', N'Dinero', 'WMS_LOTSERIAL', 100),
 ('TAX', N'Impuestos (IVU)', N'Taxes (IVU)', N'Tasas, categorías de producto, exenciones del cliente e informe de impuestos', N'Dinero', NULL, 110),
 ('BILLING', N'Facturación', N'Billing', N'Facturas y notas de crédito de ventas y servicios', N'Dinero', 'SALES_ORDERS', 120)
) AS s(ModuleKey, Name, NameEn, Description, Category, DependsOnModuleKey, SortOrder)
ON t.ModuleKey = s.ModuleKey
WHEN MATCHED THEN UPDATE SET Name = s.Name, NameEn = s.NameEn, Description = s.Description, Category = s.Category, DependsOnModuleKey = s.DependsOnModuleKey, SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN INSERT (ModuleKey, Name, NameEn, Description, Category, DependsOnModuleKey, SortOrder) VALUES (s.ModuleKey, s.Name, s.NameEn, s.Description, s.Category, s.DependsOnModuleKey, s.SortOrder);
GO
