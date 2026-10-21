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
/* ----------------------------------------------------------------------------
   2026-10-14 (f) — Trabajos programados por compañía (Lote 50, P2.2). Motor propio simple (sin Hangfire/Quartz):
   - dbo.ScheduledJob: una fila por (compañía, trabajo): interruptor, horario (INTERVAL cada N segundos | DAILY a una hora local),
     próxima corrida, último resultado y el RECLAMO (LockedBy/LockedUntilUtc) con el que varias copias del API no duplican la corrida.
   - dbo.ScheduledJobRun: historial de corridas (el trabajo JOB_RUN_PURGE conserva las últimas 100 por trabajo).
   - EntityType SCHEDULED_JOB (bitácora).
   - Permiso jobs.manage (categoría ADMIN) -> TenantAdmin de la plantilla Y de los roles YA CLONADOS de cada compañía.
   Las filas por compañía las crea el propio sistema al arrancar (con el horario por omisión): este script no siembra datos.
   Solo AGREGA (nunca quita). Idempotente.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.ScheduledJob', N'U') IS NULL CREATE TABLE dbo.ScheduledJob (
    ScheduledJobId      INT IDENTITY(1,1) CONSTRAINT PK_ScheduledJob PRIMARY KEY,
    TenantId            INT NOT NULL CONSTRAINT FK_ScheduledJob_Tenant REFERENCES dbo.Tenant(TenantId),
    JobKey              VARCHAR(60) NOT NULL,
    IsEnabled           BIT NOT NULL CONSTRAINT DF_ScheduledJob_IsEnabled DEFAULT 0,
    ScheduleKind        VARCHAR(10) NOT NULL CONSTRAINT DF_ScheduledJob_ScheduleKind DEFAULT 'INTERVAL',
    IntervalSeconds     INT NULL,
    DailyAt             TIME(0) NULL,
    NextRunUtc          DATETIME2 NULL,
    ManualRequested     BIT NOT NULL CONSTRAINT DF_ScheduledJob_ManualRequested DEFAULT 0,
    LastStartedUtc      DATETIME2 NULL,
    LastFinishedUtc     DATETIME2 NULL,
    LastStatus          VARCHAR(10) NOT NULL CONSTRAINT DF_ScheduledJob_LastStatus DEFAULT 'NEVER',
    LastMessage         NVARCHAR(400) NULL,
    LastError           NVARCHAR(1000) NULL,
    ConsecutiveFailures INT NOT NULL CONSTRAINT DF_ScheduledJob_ConsecutiveFailures DEFAULT 0,
    LockedBy            NVARCHAR(80) NULL,
    LockedUntilUtc      DATETIME2 NULL,
    CreatedAtUtc        DATETIME2 NOT NULL CONSTRAINT DF_ScheduledJob_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT NULL CONSTRAINT FK_ScheduledJob_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc        DATETIME2 NULL,
    UpdatedBy           INT NULL CONSTRAINT FK_ScheduledJob_UpdatedBy REFERENCES dbo.AspNetUsers(Id),
    RowVersion          ROWVERSION,
    CONSTRAINT CK_ScheduledJob_Kind CHECK (ScheduleKind IN ('INTERVAL', 'DAILY')),
    CONSTRAINT CK_ScheduledJob_Status CHECK (LastStatus IN ('NEVER', 'OK', 'FAILED', 'RUNNING')),
    CONSTRAINT CK_ScheduledJob_Schedule CHECK ((ScheduleKind = 'INTERVAL' AND IntervalSeconds BETWEEN 60 AND 604800 AND DailyAt IS NULL) OR (ScheduleKind = 'DAILY' AND DailyAt IS NOT NULL AND IntervalSeconds IS NULL))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ScheduledJob_Key' AND object_id = OBJECT_ID('dbo.ScheduledJob'))
    CREATE UNIQUE INDEX UX_ScheduledJob_Key ON dbo.ScheduledJob(TenantId, JobKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ScheduledJob_Due' AND object_id = OBJECT_ID('dbo.ScheduledJob'))
    CREATE INDEX IX_ScheduledJob_Due ON dbo.ScheduledJob(NextRunUtc) INCLUDE (TenantId, JobKey, IsEnabled, ManualRequested, LockedUntilUtc);
GO

IF OBJECT_ID(N'dbo.ScheduledJobRun', N'U') IS NULL CREATE TABLE dbo.ScheduledJobRun (
    ScheduledJobRunId BIGINT IDENTITY(1,1) CONSTRAINT PK_ScheduledJobRun PRIMARY KEY,
    TenantId          INT NOT NULL CONSTRAINT FK_ScheduledJobRun_Tenant REFERENCES dbo.Tenant(TenantId),
    JobKey            VARCHAR(60) NOT NULL,
    StartedUtc        DATETIME2 NOT NULL,
    FinishedUtc       DATETIME2 NULL,
    Status            VARCHAR(10) NOT NULL CONSTRAINT DF_ScheduledJobRun_Status DEFAULT 'RUNNING',
    TriggeredBy       VARCHAR(10) NOT NULL CONSTRAINT DF_ScheduledJobRun_TriggeredBy DEFAULT 'SCHEDULE',
    Message           NVARCHAR(400) NULL,
    Error             NVARCHAR(1000) NULL,
    InstanceId        NVARCHAR(80) NOT NULL,
    CONSTRAINT CK_ScheduledJobRun_Status CHECK (Status IN ('RUNNING', 'OK', 'FAILED')),
    CONSTRAINT CK_ScheduledJobRun_Trigger CHECK (TriggeredBy IN ('SCHEDULE', 'MANUAL'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ScheduledJobRun_Job' AND object_id = OBJECT_ID('dbo.ScheduledJobRun'))
    CREATE INDEX IX_ScheduledJobRun_Job ON dbo.ScheduledJobRun(TenantId, JobKey, ScheduledJobRunId);
GO

-- EntityType de la bitácora
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'SCHEDULED_JOB', N'{"es":"Trabajo programado","en":"Scheduled job"}', 120)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Permiso jobs.manage
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'jobs.manage')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('jobs.manage',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ADMIN'),
            N'{"es":"Gestionar los trabajos programados","en":"Manage scheduled jobs"}', 1);
GO

-- Plantilla de sistema (TenantId NULL) y roles ya clonados de cada compañía: TenantAdmin
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'jobs.manage'
WHERE r.Name IN ('TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO
/* ----------------------------------------------------------------------------
   2026-10-14 (g) — Avisos / notificaciones (Lote 51). «Cuando pase el evento X, avisar a estos destinatarios por estos canales.»
   - dbo.NotificationRule (regla por compañía), NotificationRuleChannel (plantilla por canal: asunto y texto en JSON es/en),
     NotificationRuleRecipient (rol, usuario, correo o teléfono suelto).
   - dbo.NotificationOutbox: cola que se escribe en la MISMA transacción de la operación; la procesa un servicio de fondo con reclamo
     atómico y reintentos (1, 5 y 30 min). NotificationLog: cada envío (cola x regla x canal x destinatario; único = idempotente).
   - dbo.UserNotification: bandeja del usuario (canal APP, la campanita). dbo.UserNotificationPref: eventos silenciados por usuario.
   - El catálogo de EVENTOS vive en código (no hay tabla): cada módulo registra los suyos.
   - EntityType NOTIFICATION_RULE (bitácora). Permiso notifications.manage (categoría ADMIN) -> TenantAdmin de la plantilla Y de los
     roles YA CLONADOS de cada compañía. Los avisos no son un módulo: van bajo SYSTEM.
   Solo AGREGA (nunca quita). Idempotente. Autocontenida: no depende de otras secciones nuevas de este archivo.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.NotificationRule', N'U') IS NULL CREATE TABLE dbo.NotificationRule (
    NotificationRuleId INT IDENTITY(1,1) CONSTRAINT PK_NotificationRule PRIMARY KEY,
    PublicId           UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_NotificationRule_PublicId DEFAULT NEWID(),
    TenantId           INT NOT NULL CONSTRAINT FK_NotificationRule_Tenant REFERENCES dbo.Tenant(TenantId),
    EventCode          VARCHAR(60) NOT NULL,
    Name               NVARCHAR(100) NOT NULL,
    IsActive           BIT NOT NULL CONSTRAINT DF_NotificationRule_IsActive DEFAULT 0,
    CreatedAtUtc       DATETIME2 NOT NULL CONSTRAINT DF_NotificationRule_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy          INT NULL CONSTRAINT FK_NotificationRule_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc       DATETIME2 NULL,
    UpdatedBy          INT NULL CONSTRAINT FK_NotificationRule_UpdatedBy REFERENCES dbo.AspNetUsers(Id),
    RowVersion         ROWVERSION,
    CONSTRAINT UQ_NotificationRule_PublicId UNIQUE (PublicId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_NotificationRule_Name' AND object_id = OBJECT_ID('dbo.NotificationRule'))
    CREATE UNIQUE INDEX UX_NotificationRule_Name ON dbo.NotificationRule(TenantId, Name);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NotificationRule_Event' AND object_id = OBJECT_ID('dbo.NotificationRule'))
    CREATE INDEX IX_NotificationRule_Event ON dbo.NotificationRule(TenantId, EventCode, IsActive);
GO

IF OBJECT_ID(N'dbo.NotificationRuleChannel', N'U') IS NULL CREATE TABLE dbo.NotificationRuleChannel (
    NotificationRuleChannelId INT IDENTITY(1,1) CONSTRAINT PK_NotificationRuleChannel PRIMARY KEY,
    TenantId           INT NOT NULL CONSTRAINT FK_NotificationRuleChannel_Tenant REFERENCES dbo.Tenant(TenantId),
    NotificationRuleId INT NOT NULL CONSTRAINT FK_NotificationRuleChannel_Rule REFERENCES dbo.NotificationRule(NotificationRuleId),
    Channel            VARCHAR(10) NOT NULL,
    IsEnabled          BIT NOT NULL CONSTRAINT DF_NotificationRuleChannel_IsEnabled DEFAULT 0,
    SubjectJson        NVARCHAR(1000) NULL,
    BodyJson           NVARCHAR(4000) NULL,
    CONSTRAINT CK_NotificationRuleChannel_Channel CHECK (Channel IN ('EMAIL', 'SMS', 'APP', 'PUSH'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_NotificationRuleChannel' AND object_id = OBJECT_ID('dbo.NotificationRuleChannel'))
    CREATE UNIQUE INDEX UX_NotificationRuleChannel ON dbo.NotificationRuleChannel(NotificationRuleId, Channel);
GO

IF OBJECT_ID(N'dbo.NotificationRuleRecipient', N'U') IS NULL CREATE TABLE dbo.NotificationRuleRecipient (
    NotificationRuleRecipientId INT IDENTITY(1,1) CONSTRAINT PK_NotificationRuleRecipient PRIMARY KEY,
    TenantId           INT NOT NULL CONSTRAINT FK_NotificationRuleRecipient_Tenant REFERENCES dbo.Tenant(TenantId),
    NotificationRuleId INT NOT NULL CONSTRAINT FK_NotificationRuleRecipient_Rule REFERENCES dbo.NotificationRule(NotificationRuleId),
    Kind               VARCHAR(10) NOT NULL,
    RoleId             INT NULL CONSTRAINT FK_NotificationRuleRecipient_Role REFERENCES dbo.Role(RoleId),
    UserId             INT NULL CONSTRAINT FK_NotificationRuleRecipient_User REFERENCES dbo.AspNetUsers(Id),
    Value              NVARCHAR(200) NULL,
    CONSTRAINT CK_NotificationRuleRecipient_Kind CHECK (
        (Kind = 'ROLE' AND RoleId IS NOT NULL AND UserId IS NULL AND Value IS NULL) OR
        (Kind = 'USER' AND UserId IS NOT NULL AND RoleId IS NULL AND Value IS NULL) OR
        (Kind IN ('EMAIL', 'PHONE') AND Value IS NOT NULL AND RoleId IS NULL AND UserId IS NULL))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NotificationRuleRecipient_Rule' AND object_id = OBJECT_ID('dbo.NotificationRuleRecipient'))
    CREATE INDEX IX_NotificationRuleRecipient_Rule ON dbo.NotificationRuleRecipient(NotificationRuleId);
GO

IF OBJECT_ID(N'dbo.NotificationOutbox', N'U') IS NULL CREATE TABLE dbo.NotificationOutbox (
    NotificationOutboxId BIGINT IDENTITY(1,1) CONSTRAINT PK_NotificationOutbox PRIMARY KEY,
    TenantId        INT NOT NULL CONSTRAINT FK_NotificationOutbox_Tenant REFERENCES dbo.Tenant(TenantId),
    EventCode       VARCHAR(60) NOT NULL,
    PayloadJson     NVARCHAR(4000) NULL,
    DedupKey        VARCHAR(100) NULL,
    Status          VARCHAR(10) NOT NULL CONSTRAINT DF_NotificationOutbox_Status DEFAULT 'PENDING',
    Attempts        INT NOT NULL CONSTRAINT DF_NotificationOutbox_Attempts DEFAULT 0,
    NextAttemptUtc  DATETIME2 NOT NULL CONSTRAINT DF_NotificationOutbox_NextAttemptUtc DEFAULT SYSUTCDATETIME(),
    LockedBy        NVARCHAR(80) NULL,
    LockedUntilUtc  DATETIME2 NULL,
    LastError       NVARCHAR(1000) NULL,
    CreatedAtUtc    DATETIME2 NOT NULL CONSTRAINT DF_NotificationOutbox_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy       INT NULL CONSTRAINT FK_NotificationOutbox_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    ProcessedUtc    DATETIME2 NULL,
    CONSTRAINT CK_NotificationOutbox_Status CHECK (Status IN ('PENDING', 'DONE', 'FAILED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NotificationOutbox_Due' AND object_id = OBJECT_ID('dbo.NotificationOutbox'))
    CREATE INDEX IX_NotificationOutbox_Due ON dbo.NotificationOutbox(Status, NextAttemptUtc) INCLUDE (TenantId, LockedUntilUtc);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_NotificationOutbox_Dedup' AND object_id = OBJECT_ID('dbo.NotificationOutbox'))
    CREATE UNIQUE INDEX UX_NotificationOutbox_Dedup ON dbo.NotificationOutbox(TenantId, EventCode, DedupKey) WHERE DedupKey IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.NotificationLog', N'U') IS NULL CREATE TABLE dbo.NotificationLog (
    NotificationLogId BIGINT IDENTITY(1,1) CONSTRAINT PK_NotificationLog PRIMARY KEY,
    TenantId             INT NOT NULL CONSTRAINT FK_NotificationLog_Tenant REFERENCES dbo.Tenant(TenantId),
    NotificationOutboxId BIGINT NOT NULL CONSTRAINT FK_NotificationLog_Outbox REFERENCES dbo.NotificationOutbox(NotificationOutboxId),
    NotificationRuleId   INT NOT NULL CONSTRAINT FK_NotificationLog_Rule REFERENCES dbo.NotificationRule(NotificationRuleId),
    EventCode            VARCHAR(60) NOT NULL,
    Channel              VARCHAR(10) NOT NULL,
    Recipient            NVARCHAR(200) NOT NULL,
    Status               VARCHAR(10) NOT NULL CONSTRAINT DF_NotificationLog_Status DEFAULT 'PENDING',
    Error                NVARCHAR(1000) NULL,
    Attempts             INT NOT NULL CONSTRAINT DF_NotificationLog_Attempts DEFAULT 0,
    CreatedAtUtc         DATETIME2 NOT NULL CONSTRAINT DF_NotificationLog_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    SentAtUtc            DATETIME2 NULL,
    CONSTRAINT CK_NotificationLog_Channel CHECK (Channel IN ('EMAIL', 'SMS', 'APP', 'PUSH')),
    CONSTRAINT CK_NotificationLog_Status CHECK (Status IN ('PENDING', 'SENT', 'FAILED', 'SKIPPED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_NotificationLog_Delivery' AND object_id = OBJECT_ID('dbo.NotificationLog'))
    CREATE UNIQUE INDEX UX_NotificationLog_Delivery ON dbo.NotificationLog(NotificationOutboxId, NotificationRuleId, Channel, Recipient);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NotificationLog_Tenant' AND object_id = OBJECT_ID('dbo.NotificationLog'))
    CREATE INDEX IX_NotificationLog_Tenant ON dbo.NotificationLog(TenantId, CreatedAtUtc);
GO

IF OBJECT_ID(N'dbo.UserNotification', N'U') IS NULL CREATE TABLE dbo.UserNotification (
    UserNotificationId   BIGINT IDENTITY(1,1) CONSTRAINT PK_UserNotification PRIMARY KEY,
    TenantId             INT NOT NULL CONSTRAINT FK_UserNotification_Tenant REFERENCES dbo.Tenant(TenantId),
    UserId               INT NOT NULL CONSTRAINT FK_UserNotification_User REFERENCES dbo.AspNetUsers(Id),
    EventCode            VARCHAR(60) NOT NULL,
    Title                NVARCHAR(200) NOT NULL,
    Body                 NVARCHAR(1000) NULL,
    LinkUrl              NVARCHAR(300) NULL,
    IsRead               BIT NOT NULL CONSTRAINT DF_UserNotification_IsRead DEFAULT 0,
    CreatedAtUtc         DATETIME2 NOT NULL CONSTRAINT DF_UserNotification_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    ReadAtUtc            DATETIME2 NULL,
    NotificationOutboxId BIGINT NULL CONSTRAINT FK_UserNotification_Outbox REFERENCES dbo.NotificationOutbox(NotificationOutboxId),
    NotificationRuleId   INT NULL CONSTRAINT FK_UserNotification_Rule REFERENCES dbo.NotificationRule(NotificationRuleId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserNotification_Inbox' AND object_id = OBJECT_ID('dbo.UserNotification'))
    CREATE INDEX IX_UserNotification_Inbox ON dbo.UserNotification(TenantId, UserId, IsRead, UserNotificationId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_UserNotification_Delivery' AND object_id = OBJECT_ID('dbo.UserNotification'))
    CREATE UNIQUE INDEX UX_UserNotification_Delivery ON dbo.UserNotification(NotificationOutboxId, NotificationRuleId, UserId) WHERE NotificationOutboxId IS NOT NULL AND NotificationRuleId IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.UserNotificationPref', N'U') IS NULL CREATE TABLE dbo.UserNotificationPref (
    UserNotificationPrefId INT IDENTITY(1,1) CONSTRAINT PK_UserNotificationPref PRIMARY KEY,
    TenantId   INT NOT NULL CONSTRAINT FK_UserNotificationPref_Tenant REFERENCES dbo.Tenant(TenantId),
    UserId     INT NOT NULL CONSTRAINT FK_UserNotificationPref_User REFERENCES dbo.AspNetUsers(Id),
    EventCode  VARCHAR(60) NOT NULL,
    IsMuted    BIT NOT NULL CONSTRAINT DF_UserNotificationPref_IsMuted DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_UserNotificationPref' AND object_id = OBJECT_ID('dbo.UserNotificationPref'))
    CREATE UNIQUE INDEX UX_UserNotificationPref ON dbo.UserNotificationPref(TenantId, UserId, EventCode);
GO

-- EntityType de la bitácora
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'NOTIFICATION_RULE', N'{"es":"Regla de aviso","en":"Notification rule"}', 121)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Permiso notifications.manage
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'notifications.manage')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('notifications.manage',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ADMIN'),
            N'{"es":"Gestionar las reglas de aviso","en":"Manage notification rules"}', 1);
GO

-- Plantilla de sistema (TenantId NULL) y roles ya clonados de cada compañía: TenantAdmin
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'notifications.manage'
WHERE r.Name IN ('TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO
/* ----------------------------------------------------------------------------
   2026-10-15 (h) — Mensajería interna con hilos (Lote 53). Un mensaje se puede contestar y el hilo se conserva; entre dos personas
   (DIRECT, único por pareja) o de grupo (GROUP: varias personas o un rol «en foto»). No es un chat en vivo, pero la entrega es inmediata
   por la conexión en vivo de la web (SignalR) y, sin sesión, el mensaje queda pendiente hasta que la persona entra.
   - dbo.MessageThread (hilo y resumen del último mensaje), dbo.MessageThreadMember (participantes, hasta dónde leyó cada uno, silenciado,
     si salió), dbo.Message (los mensajes: nunca se editan ni se borran) y dbo.UserPresence («visto por última vez» de la web abierta).
   - Permiso messages.use (categoría ADMIN) -> TODOS los roles ya existentes (plantillas y roles clonados o propios de cada compañía):
     la idea es que todos se puedan escribir; un administrador puede quitárselo a un rol.
   Solo AGREGA (nunca quita). Idempotente. Autocontenida: no depende de otras secciones nuevas de este archivo.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.MessageThread', N'U') IS NULL CREATE TABLE dbo.MessageThread (
    MessageThreadId     BIGINT IDENTITY(1,1) CONSTRAINT PK_MessageThread PRIMARY KEY,
    PublicId            UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_MessageThread_PublicId DEFAULT NEWID(),
    TenantId            INT NOT NULL CONSTRAINT FK_MessageThread_Tenant REFERENCES dbo.Tenant(TenantId),
    Kind                VARCHAR(10) NOT NULL,
    Title               NVARCHAR(100) NULL,
    DirectKey           VARCHAR(40) NULL,
    CreatedBy           INT NOT NULL CONSTRAINT FK_MessageThread_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    CreatedAtUtc        DATETIME2 NOT NULL CONSTRAINT DF_MessageThread_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    LastMessageId       BIGINT NOT NULL CONSTRAINT DF_MessageThread_LastMessageId DEFAULT 0,
    LastMessageAtUtc    DATETIME2 NOT NULL CONSTRAINT DF_MessageThread_LastMessageAtUtc DEFAULT SYSUTCDATETIME(),
    LastMessagePreview  NVARCHAR(200) NULL,
    LastSenderId        INT NULL CONSTRAINT FK_MessageThread_LastSender REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT UQ_MessageThread_PublicId UNIQUE (PublicId),
    CONSTRAINT CK_MessageThread_Kind CHECK ((Kind = 'DIRECT' AND DirectKey IS NOT NULL AND Title IS NULL) OR (Kind = 'GROUP' AND DirectKey IS NULL))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_MessageThread_Direct' AND object_id = OBJECT_ID('dbo.MessageThread'))
    CREATE UNIQUE INDEX UX_MessageThread_Direct ON dbo.MessageThread(TenantId, DirectKey) WHERE DirectKey IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.MessageThreadMember', N'U') IS NULL CREATE TABLE dbo.MessageThreadMember (
    MessageThreadMemberId BIGINT IDENTITY(1,1) CONSTRAINT PK_MessageThreadMember PRIMARY KEY,
    TenantId             INT NOT NULL CONSTRAINT FK_MessageThreadMember_Tenant REFERENCES dbo.Tenant(TenantId),
    MessageThreadId      BIGINT NOT NULL CONSTRAINT FK_MessageThreadMember_Thread REFERENCES dbo.MessageThread(MessageThreadId),
    UserId               INT NOT NULL CONSTRAINT FK_MessageThreadMember_User REFERENCES dbo.AspNetUsers(Id),
    JoinedAtUtc          DATETIME2 NOT NULL CONSTRAINT DF_MessageThreadMember_JoinedAtUtc DEFAULT SYSUTCDATETIME(),
    JoinedAfterMessageId BIGINT NOT NULL CONSTRAINT DF_MessageThreadMember_JoinedAfter DEFAULT 0,
    LastReadMessageId    BIGINT NOT NULL CONSTRAINT DF_MessageThreadMember_LastRead DEFAULT 0,
    IsMuted              BIT NOT NULL CONSTRAINT DF_MessageThreadMember_IsMuted DEFAULT 0,
    LeftAtUtc            DATETIME2 NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_MessageThreadMember' AND object_id = OBJECT_ID('dbo.MessageThreadMember'))
    CREATE UNIQUE INDEX UX_MessageThreadMember ON dbo.MessageThreadMember(MessageThreadId, UserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MessageThreadMember_User' AND object_id = OBJECT_ID('dbo.MessageThreadMember'))
    CREATE INDEX IX_MessageThreadMember_User ON dbo.MessageThreadMember(TenantId, UserId, LeftAtUtc) INCLUDE (MessageThreadId, LastReadMessageId, JoinedAfterMessageId, IsMuted);
GO

IF OBJECT_ID(N'dbo.Message', N'U') IS NULL CREATE TABLE dbo.Message (
    MessageId       BIGINT IDENTITY(1,1) CONSTRAINT PK_Message PRIMARY KEY,
    TenantId        INT NOT NULL CONSTRAINT FK_Message_Tenant REFERENCES dbo.Tenant(TenantId),
    MessageThreadId BIGINT NOT NULL CONSTRAINT FK_Message_Thread REFERENCES dbo.MessageThread(MessageThreadId),
    SenderId        INT NOT NULL CONSTRAINT FK_Message_Sender REFERENCES dbo.AspNetUsers(Id),
    Body            NVARCHAR(2000) NOT NULL,
    CreatedAtUtc    DATETIME2 NOT NULL CONSTRAINT DF_Message_CreatedAtUtc DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Message_Thread' AND object_id = OBJECT_ID('dbo.Message'))
    CREATE INDEX IX_Message_Thread ON dbo.Message(MessageThreadId, MessageId) INCLUDE (SenderId);
GO

IF OBJECT_ID(N'dbo.UserPresence', N'U') IS NULL CREATE TABLE dbo.UserPresence (
    UserId      INT NOT NULL CONSTRAINT PK_UserPresence PRIMARY KEY CONSTRAINT FK_UserPresence_User REFERENCES dbo.AspNetUsers(Id),
    LastSeenUtc DATETIME2 NOT NULL
);
GO

-- Permiso messages.use
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'messages.use')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('messages.use',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ADMIN'),
            N'{"es":"Usar la mensajería interna","en":"Use internal messaging"}', 1);
GO

-- A TODOS los roles ya existentes: plantillas de sistema (TenantId NULL) y los de cada compañía (clonados o propios)
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'messages.use'
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO
/* ----------------------------------------------------------------------------
   2026-10-15 (i) — Avisos manuales (Lote 54). Quien tenga el permiso notifications.send envía un aviso a personas, a roles o a todos; no se
   responde. Cada persona lo recibe en su campanita (dbo.UserNotification, ahora con AnnouncementId para mostrar «De Ana») y dbo.Announcement
   es el historial de lo enviado (a quién, cuántas personas).
   - dbo.Announcement (por compañía; nunca se edita ni se borra) y dbo.UserNotification.AnnouncementId (NULL = los avisos de las reglas).
   - Permiso notifications.send (categoría ADMIN) -> TenantAdmin de la plantilla Y de los roles YA CLONADOS de cada compañía (el administrador se
     lo asigna a quien quiera; no se da a otros roles por omisión).
   Solo AGREGA (nunca quita). Idempotente. Depende de la sección (g) (dbo.UserNotification).
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.Announcement', N'U') IS NULL CREATE TABLE dbo.Announcement (
    AnnouncementId  BIGINT IDENTITY(1,1) CONSTRAINT PK_Announcement PRIMARY KEY,
    PublicId        UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Announcement_PublicId DEFAULT NEWID(),
    TenantId        INT NOT NULL CONSTRAINT FK_Announcement_Tenant REFERENCES dbo.Tenant(TenantId),
    Title           NVARCHAR(150) NOT NULL,
    Body            NVARCHAR(1000) NULL,
    SentBy          INT NOT NULL CONSTRAINT FK_Announcement_SentBy REFERENCES dbo.AspNetUsers(Id),
    SentAtUtc       DATETIME2 NOT NULL CONSTRAINT DF_Announcement_SentAtUtc DEFAULT SYSUTCDATETIME(),
    ToAll           BIT NOT NULL CONSTRAINT DF_Announcement_ToAll DEFAULT 0,
    AudienceText    NVARCHAR(300) NOT NULL,
    RecipientCount  INT NOT NULL CONSTRAINT DF_Announcement_RecipientCount DEFAULT 0,
    CONSTRAINT UQ_Announcement_PublicId UNIQUE (PublicId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Announcement_Tenant' AND object_id = OBJECT_ID('dbo.Announcement'))
    CREATE INDEX IX_Announcement_Tenant ON dbo.Announcement(TenantId, AnnouncementId);
GO
IF COL_LENGTH('dbo.UserNotification', 'AnnouncementId') IS NULL
    ALTER TABLE dbo.UserNotification ADD AnnouncementId BIGINT NULL CONSTRAINT FK_UserNotification_Announcement REFERENCES dbo.Announcement(AnnouncementId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserNotification_Announcement' AND object_id = OBJECT_ID('dbo.UserNotification'))
    CREATE INDEX IX_UserNotification_Announcement ON dbo.UserNotification(AnnouncementId) WHERE AnnouncementId IS NOT NULL;
GO

-- Permiso notifications.send
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'notifications.send')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('notifications.send',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ADMIN'),
            N'{"es":"Enviar avisos a otras personas","en":"Send notices to other people"}', 1);
GO

-- Plantilla de sistema (TenantId NULL) y roles ya clonados de cada compañía: TenantAdmin
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'notifications.send'
WHERE r.Name IN ('TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-15 (j) — Impuestos / IVU (Lote 55, P3). Todo es configuración: ninguna tasa ni regla va fija en código.
   - dbo.TaxRate + dbo.TaxRateComponent: tasas por compañía con vigencia (EffectiveFrom/To) y componentes (estatal, municipal…). Una tasa NO se
     edita: se cierra su vigencia y se crea otra con el mismo Code.
   - dbo.TenantTaxSetting (una fila por compañía): precios con impuesto incluido, redondeo por línea o por documento y tasa por omisión.
   - dbo.TaxChargeRule: qué tipos de cargo (flete, manejo…) pagan impuesto. dbo.ClientTaxExemption: exenciones (certificado) de clientes.
   - dbo.Product.TaxCategoryLookupId (catálogo TaxCategory: TAXABLE / EXEMPT / ZERO_RATE; NULL = gravable).
   - dbo.InvoiceLine: columnas de copia del impuesto (TaxRateId, TaxRatePct, TaxAmount, IsTaxExempt) para cuando se construya la facturación.
   - Permiso tax.manage (categoría BILLING) -> TenantAdmin de la plantilla Y de los roles YA CLONADOS de cada compañía. EntityType TAX_RATE,
     TAX_EXEMPTION y TAX_SETTING (bitácora).
   No siembra tasas ni cifras legales (las define el contador de la compañía). Solo AGREGA (nunca quita). Idempotente.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.TaxRate', N'U') IS NULL CREATE TABLE dbo.TaxRate (
    TaxRateId     INT IDENTITY(1,1) CONSTRAINT PK_TaxRate PRIMARY KEY,
    PublicId      UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_TaxRate_PublicId DEFAULT NEWID(),
    TenantId      INT NOT NULL CONSTRAINT FK_TaxRate_Tenant REFERENCES dbo.Tenant(TenantId),
    Code          VARCHAR(30) NOT NULL,
    Name          NVARCHAR(100) NOT NULL,
    [Percent]     DECIMAL(9,4) NOT NULL,
    EffectiveFrom DATE NOT NULL,
    EffectiveTo   DATE NULL,
    CreatedAtUtc  DATETIME2 NOT NULL CONSTRAINT DF_TaxRate_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy     INT NULL,
    UpdatedAtUtc  DATETIME2 NULL,
    UpdatedBy     INT NULL,
    CONSTRAINT UQ_TaxRate_PublicId UNIQUE (PublicId),
    CONSTRAINT CK_TaxRate_Percent CHECK ([Percent] >= 0 AND [Percent] <= 100),
    CONSTRAINT CK_TaxRate_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TaxRate_Code_From' AND object_id = OBJECT_ID('dbo.TaxRate'))
    CREATE UNIQUE INDEX UX_TaxRate_Code_From ON dbo.TaxRate(TenantId, Code, EffectiveFrom);
GO

IF OBJECT_ID(N'dbo.TaxRateComponent', N'U') IS NULL CREATE TABLE dbo.TaxRateComponent (
    TaxRateComponentId INT IDENTITY(1,1) CONSTRAINT PK_TaxRateComponent PRIMARY KEY,
    TenantId   INT NOT NULL CONSTRAINT FK_TaxRateComponent_Tenant REFERENCES dbo.Tenant(TenantId),
    TaxRateId  INT NOT NULL CONSTRAINT FK_TaxRateComponent_Rate REFERENCES dbo.TaxRate(TaxRateId),
    Code       VARCHAR(30) NOT NULL,
    Name       NVARCHAR(100) NOT NULL,
    [Percent]  DECIMAL(9,4) NOT NULL,
    SortOrder  INT NOT NULL CONSTRAINT DF_TaxRateComponent_SortOrder DEFAULT 0,
    CONSTRAINT CK_TaxRateComponent_Percent CHECK ([Percent] >= 0 AND [Percent] <= 100)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TaxRateComponent_Code' AND object_id = OBJECT_ID('dbo.TaxRateComponent'))
    CREATE UNIQUE INDEX UX_TaxRateComponent_Code ON dbo.TaxRateComponent(TaxRateId, Code);
GO

IF OBJECT_ID(N'dbo.TenantTaxSetting', N'U') IS NULL CREATE TABLE dbo.TenantTaxSetting (
    TenantTaxSettingId INT IDENTITY(1,1) CONSTRAINT PK_TenantTaxSetting PRIMARY KEY,
    TenantId        INT NOT NULL CONSTRAINT FK_TenantTaxSetting_Tenant REFERENCES dbo.Tenant(TenantId),
    PricesIncludeTax BIT NOT NULL CONSTRAINT DF_TenantTaxSetting_Incl DEFAULT 0,
    RoundingMode    VARCHAR(10) NOT NULL CONSTRAINT DF_TenantTaxSetting_Round DEFAULT 'LINE',
    DefaultRateCode VARCHAR(30) NULL,
    CreatedAtUtc    DATETIME2 NOT NULL CONSTRAINT DF_TenantTaxSetting_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy       INT NULL,
    UpdatedAtUtc    DATETIME2 NULL,
    UpdatedBy       INT NULL,
    CONSTRAINT CK_TenantTaxSetting_Round CHECK (RoundingMode IN ('LINE','DOCUMENT'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TenantTaxSetting_Tenant' AND object_id = OBJECT_ID('dbo.TenantTaxSetting'))
    CREATE UNIQUE INDEX UX_TenantTaxSetting_Tenant ON dbo.TenantTaxSetting(TenantId);
GO

IF OBJECT_ID(N'dbo.TaxChargeRule', N'U') IS NULL CREATE TABLE dbo.TaxChargeRule (
    TaxChargeRuleId    INT IDENTITY(1,1) CONSTRAINT PK_TaxChargeRule PRIMARY KEY,
    TenantId           INT NOT NULL CONSTRAINT FK_TaxChargeRule_Tenant REFERENCES dbo.Tenant(TenantId),
    ChargeTypeLookupId INT NOT NULL CONSTRAINT FK_TaxChargeRule_ChargeType REFERENCES dbo.LookupCode(LookupCodeId),
    IsTaxable          BIT NOT NULL CONSTRAINT DF_TaxChargeRule_IsTaxable DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TaxChargeRule' AND object_id = OBJECT_ID('dbo.TaxChargeRule'))
    CREATE UNIQUE INDEX UX_TaxChargeRule ON dbo.TaxChargeRule(TenantId, ChargeTypeLookupId);
GO

IF OBJECT_ID(N'dbo.ClientTaxExemption', N'U') IS NULL CREATE TABLE dbo.ClientTaxExemption (
    ClientTaxExemptionId INT IDENTITY(1,1) CONSTRAINT PK_ClientTaxExemption PRIMARY KEY,
    PublicId          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_ClientTaxExemption_PublicId DEFAULT NEWID(),
    TenantId          INT NOT NULL CONSTRAINT FK_ClientTaxExemption_Tenant REFERENCES dbo.Tenant(TenantId),
    ClientId          INT NOT NULL CONSTRAINT FK_ClientTaxExemption_Client REFERENCES dbo.Client(ClientId),
    ExemptionType     NVARCHAR(60) NOT NULL,
    CertificateNumber NVARCHAR(60) NOT NULL,
    ValidFrom         DATE NOT NULL,
    ValidTo           DATE NULL,
    Notes             NVARCHAR(300) NULL,
    IsActive          BIT NOT NULL CONSTRAINT DF_ClientTaxExemption_IsActive DEFAULT 1,
    CreatedAtUtc      DATETIME2 NOT NULL CONSTRAINT DF_ClientTaxExemption_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy         INT NULL,
    UpdatedAtUtc      DATETIME2 NULL,
    UpdatedBy         INT NULL,
    CONSTRAINT UQ_ClientTaxExemption_PublicId UNIQUE (PublicId),
    CONSTRAINT CK_ClientTaxExemption_Dates CHECK (ValidTo IS NULL OR ValidTo >= ValidFrom)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientTaxExemption_Client' AND object_id = OBJECT_ID('dbo.ClientTaxExemption'))
    CREATE INDEX IX_ClientTaxExemption_Client ON dbo.ClientTaxExemption(TenantId, ClientId, ValidFrom);
GO

-- Producto: categoría de impuesto (NULL = gravable)
IF COL_LENGTH('dbo.Product', 'TaxCategoryLookupId') IS NULL
    ALTER TABLE dbo.Product ADD TaxCategoryLookupId INT NULL CONSTRAINT FK_Product_TaxCategory REFERENCES dbo.LookupCode(LookupCodeId);
GO

-- Línea de factura: copia del impuesto con que se calculó (la facturación aún no está construida; queda lista)
IF OBJECT_ID(N'dbo.InvoiceLine', N'U') IS NOT NULL AND COL_LENGTH('dbo.InvoiceLine', 'TaxRateId') IS NULL
    ALTER TABLE dbo.InvoiceLine ADD TaxRateId INT NULL CONSTRAINT FK_InvoiceLine_TaxRate REFERENCES dbo.TaxRate(TaxRateId);
GO
IF OBJECT_ID(N'dbo.InvoiceLine', N'U') IS NOT NULL AND COL_LENGTH('dbo.InvoiceLine', 'TaxRatePct') IS NULL
    ALTER TABLE dbo.InvoiceLine ADD TaxRatePct DECIMAL(9,4) NULL;
GO
IF OBJECT_ID(N'dbo.InvoiceLine', N'U') IS NOT NULL AND COL_LENGTH('dbo.InvoiceLine', 'TaxAmount') IS NULL
    ALTER TABLE dbo.InvoiceLine ADD TaxAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_InvoiceLine_TaxAmount DEFAULT 0;
GO
IF OBJECT_ID(N'dbo.InvoiceLine', N'U') IS NOT NULL AND COL_LENGTH('dbo.InvoiceLine', 'IsTaxExempt') IS NULL
    ALTER TABLE dbo.InvoiceLine ADD IsTaxExempt BIT NOT NULL CONSTRAINT DF_InvoiceLine_IsTaxExempt DEFAULT 0;
GO

-- Catálogo TaxCategory (dominio global de sistema; la compañía renombra con su override)
MERGE dbo.CatalogDomain AS t
USING (SELECT N'TaxCategory' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'TaxCategory', 1, N'{"es":"Categoría de impuesto","en":"Tax category"}', 1, 1);
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'TAXABLE',   N'{"es":"Gravable","en":"Taxable"}',            1),
    (N'EXEMPT',    N'{"es":"Exento","en":"Exempt"}',               2),
    (N'ZERO_RATE', N'{"es":"Tasa cero","en":"Zero rate"}',         3)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'TaxCategory' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'TaxCategory', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- EntityType de la bitácora
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'TAX_RATE',      N'{"es":"Tasa de impuesto","en":"Tax rate"}',            122),
    (N'TAX_EXEMPTION', N'{"es":"Exención de impuesto","en":"Tax exemption"}',   123),
    (N'TAX_SETTING',   N'{"es":"Reglas de impuestos","en":"Tax settings"}',     124)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Permiso tax.manage
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'tax.manage')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('tax.manage',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'BILLING'),
            N'{"es":"Administrar impuestos (tasas, exenciones)","en":"Manage taxes (rates, exemptions)"}', 1);
GO

-- Plantilla de sistema (TenantId NULL) y roles ya clonados de cada compañía: TenantAdmin
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'tax.manage'
WHERE r.Name IN ('TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-15 (k) — Órdenes de venta (Lote 57, P4 parte S1). El documento que viaja de Ventas a Almacén (OV-#####).
   - dbo.SalesOrder, dbo.SalesOrderLine (con copia del impuesto) y dbo.SalesOrderReservation (qué saldo reservó cada línea).
   - Contador SALESORDER (OV-#####, por compañía, ClientId NULL): se amplía CK_NumberSequence_Kind. La fila del contador la crea el API.
   - Dominio de estatus SalesOrderStatus (DRAFT inicial -> REQUESTED -> PARTIAL -> SHIPPED; CANCELLED) y EntityType SALES_ORDER.
   - Permisos sales.view, sales.create, sales.cancel, sales.price y sales.backorder (categoría ORDERS) -> TenantAdmin de la plantilla Y de los roles
     YA CLONADOS; el Operador de almacén recibe sales.view y sales.backorder. La plantilla de rol «Ventas» la crea el API al arrancar.
   Solo AGREGA (nunca quita). Idempotente. El módulo SALES_ORDERS (sección e) viene apagado: se enciende por compañía.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.SalesOrder', N'U') IS NULL CREATE TABLE dbo.SalesOrder (
    SalesOrderId   INT IDENTITY(1,1) CONSTRAINT PK_SalesOrder PRIMARY KEY,
    PublicId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_SalesOrder_PublicId DEFAULT NEWID(),
    TenantId       INT NOT NULL CONSTRAINT FK_SalesOrder_Tenant REFERENCES dbo.Tenant(TenantId),
    Number         NVARCHAR(40) NOT NULL,
    WarehouseId    INT NOT NULL CONSTRAINT FK_SalesOrder_Warehouse REFERENCES dbo.Warehouse(WarehouseId),
    ClientId       INT NOT NULL CONSTRAINT FK_SalesOrder_Client REFERENCES dbo.Client(ClientId),
    ConsigneeId    INT NULL CONSTRAINT FK_SalesOrder_Consignee REFERENCES dbo.Location(LocationId),
    RequestedBy    INT NULL CONSTRAINT FK_SalesOrder_RequestedBy REFERENCES dbo.AspNetUsers(Id),
    RequestedDate  DATE NOT NULL,
    RequiredDate   DATE NULL,
    CurrencyLookupId INT NULL CONSTRAINT FK_SalesOrder_Currency REFERENCES dbo.LookupCode(LookupCodeId),
    Notes          NVARCHAR(500) NULL,
    Subtotal       DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesOrder_Subtotal DEFAULT 0,
    TaxAmount      DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesOrder_TaxAmount DEFAULT 0,
    StatusCodeId   INT NOT NULL CONSTRAINT FK_SalesOrder_Status REFERENCES dbo.StatusCode(StatusCodeId),
    RequestedAtUtc DATETIME2 NULL,
    CreatedAtUtc   DATETIME2 NOT NULL CONSTRAINT DF_SalesOrder_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy      INT NULL,
    UpdatedAtUtc   DATETIME2 NULL,
    UpdatedBy      INT NULL,
    RowVersion     ROWVERSION,
    CONSTRAINT UQ_SalesOrder_PublicId UNIQUE (PublicId),
    CONSTRAINT UQ_SalesOrder_Number UNIQUE (TenantId, Number)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesOrder_Client' AND object_id = OBJECT_ID('dbo.SalesOrder'))
    CREATE INDEX IX_SalesOrder_Client ON dbo.SalesOrder(TenantId, ClientId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesOrder_Status' AND object_id = OBJECT_ID('dbo.SalesOrder'))
    CREATE INDEX IX_SalesOrder_Status ON dbo.SalesOrder(TenantId, StatusCodeId);
GO

IF OBJECT_ID(N'dbo.SalesOrderLine', N'U') IS NULL CREATE TABLE dbo.SalesOrderLine (
    SalesOrderLineId INT IDENTITY(1,1) CONSTRAINT PK_SalesOrderLine PRIMARY KEY,
    TenantId       INT NOT NULL CONSTRAINT FK_SalesOrderLine_Tenant REFERENCES dbo.Tenant(TenantId),
    SalesOrderId   INT NOT NULL CONSTRAINT FK_SalesOrderLine_Order REFERENCES dbo.SalesOrder(SalesOrderId),
    [LineNo]       INT NOT NULL,
    ProductId      INT NOT NULL CONSTRAINT FK_SalesOrderLine_Product REFERENCES dbo.Product(ProductId),
    LotId          INT NULL CONSTRAINT FK_SalesOrderLine_Lot REFERENCES dbo.InventoryLot(LotId),
    QtyOrdered     DECIMAL(14,3) NOT NULL,
    QtyReserved    DECIMAL(14,3) NOT NULL CONSTRAINT DF_SalesOrderLine_QtyReserved DEFAULT 0,
    QtyBackordered DECIMAL(14,3) NOT NULL CONSTRAINT DF_SalesOrderLine_QtyBackordered DEFAULT 0,
    QtyShipped     DECIMAL(14,3) NOT NULL CONSTRAINT DF_SalesOrderLine_QtyShipped DEFAULT 0,
    QtyInvoiced    DECIMAL(14,3) NOT NULL CONSTRAINT DF_SalesOrderLine_QtyInvoiced DEFAULT 0,
    QtyReturned    DECIMAL(14,3) NOT NULL CONSTRAINT DF_SalesOrderLine_QtyReturned DEFAULT 0,
    QtyCredited    DECIMAL(14,3) NOT NULL CONSTRAINT DF_SalesOrderLine_QtyCredited DEFAULT 0,
    IsClosed       BIT NOT NULL CONSTRAINT DF_SalesOrderLine_IsClosed DEFAULT 0,
    UnitPrice      DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesOrderLine_UnitPrice DEFAULT 0,
    TaxRateId      INT NULL CONSTRAINT FK_SalesOrderLine_TaxRate REFERENCES dbo.TaxRate(TaxRateId),
    TaxRatePct     DECIMAL(9,4) NOT NULL CONSTRAINT DF_SalesOrderLine_TaxRatePct DEFAULT 0,
    TaxAmount      DECIMAL(18,4) NOT NULL CONSTRAINT DF_SalesOrderLine_TaxAmount DEFAULT 0,
    IsTaxExempt    BIT NOT NULL CONSTRAINT DF_SalesOrderLine_IsTaxExempt DEFAULT 0,
    CONSTRAINT CK_SalesOrderLine_Qty CHECK (QtyOrdered > 0 AND QtyReserved >= 0 AND QtyBackordered >= 0 AND QtyShipped >= 0)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SalesOrderLine_No' AND object_id = OBJECT_ID('dbo.SalesOrderLine'))
    CREATE UNIQUE INDEX UX_SalesOrderLine_No ON dbo.SalesOrderLine(SalesOrderId, [LineNo]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesOrderLine_Product' AND object_id = OBJECT_ID('dbo.SalesOrderLine'))
    CREATE INDEX IX_SalesOrderLine_Product ON dbo.SalesOrderLine(TenantId, ProductId);
GO

IF OBJECT_ID(N'dbo.SalesOrderReservation', N'U') IS NULL CREATE TABLE dbo.SalesOrderReservation (
    SalesOrderReservationId INT IDENTITY(1,1) CONSTRAINT PK_SalesOrderReservation PRIMARY KEY,
    TenantId         INT NOT NULL CONSTRAINT FK_SalesOrderReservation_Tenant REFERENCES dbo.Tenant(TenantId),
    SalesOrderLineId INT NOT NULL CONSTRAINT FK_SalesOrderReservation_Line REFERENCES dbo.SalesOrderLine(SalesOrderLineId),
    WarehouseId      INT NOT NULL CONSTRAINT FK_SalesOrderReservation_Warehouse REFERENCES dbo.Warehouse(WarehouseId),
    WarehouseBinId   INT NOT NULL CONSTRAINT FK_SalesOrderReservation_Bin REFERENCES dbo.WarehouseBin(WarehouseBinId),
    LotId            INT NULL CONSTRAINT FK_SalesOrderReservation_Lot REFERENCES dbo.InventoryLot(LotId),
    Quantity         DECIMAL(14,3) NOT NULL,
    CONSTRAINT CK_SalesOrderReservation_Qty CHECK (Quantity > 0)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesOrderReservation_Line' AND object_id = OBJECT_ID('dbo.SalesOrderReservation'))
    CREATE INDEX IX_SalesOrderReservation_Line ON dbo.SalesOrderReservation(SalesOrderLineId);
GO

-- Contador SALESORDER (OV-#####)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND parent_object_id = OBJECT_ID('dbo.NumberSequence')
           AND definition NOT LIKE '%SALESORDER%')
BEGIN
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER'));
END
GO

-- Dominio de estatus SalesOrderStatus y sus estatus
MERGE dbo.CatalogDomain AS t
USING (SELECT N'SalesOrderStatus' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'SalesOrderStatus', 2, N'{"es":"Estatus de la orden de venta","en":"Sales order status"}', 1, 1);
GO
MERGE dbo.StatusCode AS t
USING (
    SELECT v.Code, v.LabelJson, v.Color, v.Srt, v.Init, k.LookupCodeId AS StageKindLookupId
    FROM (VALUES
        (N'DRAFT',     N'{"es":"Borrador","en":"Draft"}',                    '#9CA3AF', 1, 1, 'PIPELINE'),
        (N'REQUESTED', N'{"es":"Solicitada","en":"Requested"}',              '#3B82F6', 2, 0, 'PIPELINE'),
        (N'PARTIAL',   N'{"es":"Despachada parcial","en":"Partially shipped"}', '#F59E0B', 3, 0, 'PIPELINE'),
        (N'SHIPPED',   N'{"es":"Despachada","en":"Shipped"}',                '#10B981', 4, 0, 'TERMINAL'),
        (N'CANCELLED', N'{"es":"Cancelada","en":"Cancelled"}',               '#6B7280', 5, 0, 'TERMINAL')
    ) AS v (Code, LabelJson, Color, Srt, Init, Kind)
    JOIN dbo.LookupCode k ON k.Entity = N'StageKind' AND k.InternalCode = v.Kind
) AS s
ON t.Entity = N'SalesOrderStatus' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (N'SalesOrderStatus', s.Code, s.LabelJson, s.Color, s.Srt, s.StageKindLookupId, s.Init, 1);
GO

-- EntityType de la bitácora y del historial de estatus
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'SALES_ORDER', N'{"es":"Orden de venta","en":"Sales order"}', 125)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Permisos
MERGE dbo.Permission AS t
USING (VALUES
    ('sales.view',      N'{"es":"Ver órdenes de venta","en":"View sales orders"}'),
    ('sales.create',    N'{"es":"Crear, editar y solicitar órdenes de venta","en":"Create, edit and request sales orders"}'),
    ('sales.cancel',    N'{"es":"Cancelar órdenes de venta","en":"Cancel sales orders"}'),
    ('sales.price',     N'{"es":"Cambiar el precio en las órdenes de venta","en":"Change the price on sales orders"}'),
    ('sales.backorder', N'{"es":"Surtir y cerrar backorders","en":"Fulfill and close backorders"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ORDERS'), s.LabelJson, 1);
GO

-- TenantAdmin (plantilla y roles clonados): los cinco
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code IN ('sales.view','sales.create','sales.cancel','sales.price','sales.backorder')
WHERE r.Name IN ('TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO
-- Operador de almacén (plantilla y roles clonados): ver y surtir backorders
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code IN ('sales.view','sales.backorder')
WHERE r.Name IN ('WarehouseOperator')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-15 (l) — Despachar contra la orden de venta (Lote 61, P4 parte S2). Dos columnas opcionales que enlazan la recolección con la orden:
   - dbo.PickBatch.SalesOrderId (la orden que atiende la recolección) y dbo.PickBatchLine.SalesOrderLineId (la línea de la orden que atendió).
   NULL = recolección normal. Solo AGREGA. Idempotente. Depende de la sección (k).
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.PickBatch', 'SalesOrderId') IS NULL
    ALTER TABLE dbo.PickBatch ADD SalesOrderId INT NULL CONSTRAINT FK_PickBatch_SalesOrder REFERENCES dbo.SalesOrder(SalesOrderId);
GO
IF COL_LENGTH('dbo.PickBatchLine', 'SalesOrderLineId') IS NULL
    ALTER TABLE dbo.PickBatchLine ADD SalesOrderLineId INT NULL CONSTRAINT FK_PickBatchLine_SalesOrderLine REFERENCES dbo.SalesOrderLine(SalesOrderLineId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PickBatch_SalesOrder' AND object_id = OBJECT_ID('dbo.PickBatch'))
    CREATE INDEX IX_PickBatch_SalesOrder ON dbo.PickBatch(SalesOrderId) WHERE SalesOrderId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PickBatchLine_SalesOrderLine' AND object_id = OBJECT_ID('dbo.PickBatchLine'))
    CREATE INDEX IX_PickBatchLine_SalesOrderLine ON dbo.PickBatchLine(SalesOrderLineId) WHERE SalesOrderLineId IS NOT NULL;
GO

/* ----------------------------------------------------------------------------
   2026-10-15 (m) — Entrega de una orden de venta (Lote 62, P4 parte S2). dbo.TransportOrder.SalesOrderId (opcional): la orden de venta de la que nació la entrega.
   NULL = orden de transporte normal. Solo AGREGA. Idempotente. Depende de la sección (k).
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.TransportOrder', 'SalesOrderId') IS NULL
    ALTER TABLE dbo.TransportOrder ADD SalesOrderId INT NULL CONSTRAINT FK_TransportOrder_SalesOrder REFERENCES dbo.SalesOrder(SalesOrderId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransportOrder_SalesOrder' AND object_id = OBJECT_ID('dbo.TransportOrder'))
    CREATE INDEX IX_TransportOrder_SalesOrder ON dbo.TransportOrder(SalesOrderId) WHERE SalesOrderId IS NOT NULL;
GO

/* ----------------------------------------------------------------------------
   2026-10-15 (n) — Ajuste de estatus de la orden de venta (Lote 62): «Despachada parcial» (PARTIAL) y «Despachada» (SHIPPED) pasan a ser etapas LATERALES.
   El motor de estatus solo deja avanzar de una etapa del pipeline a la siguiente y un terminal no admite más transiciones; deshacer una recolección (la reversa)
   tiene que reabrir una orden despachada, y una lateral puede volver a la etapa del pipeline de la que salió (SOLICITADA). Solo cambia la clase de etapa de esos dos
   estatus. Idempotente. Depende de la sección (k).
   ---------------------------------------------------------------------------- */
UPDATE s SET StageKindLookupId = k.LookupCodeId
FROM dbo.StatusCode s
JOIN dbo.LookupCode k ON k.Entity = N'StageKind' AND k.InternalCode = N'LATERAL'
WHERE s.Entity = N'SalesOrderStatus' AND s.InternalCode IN (N'PARTIAL', N'SHIPPED') AND s.StageKindLookupId <> k.LookupCodeId;
GO

/* ----------------------------------------------------------------------------
   2026-10-16 (o) — Análisis de órdenes de venta (Lote 67, P4 parte S4). EntityType SALES_ORDER_LINE: la base de las vistas sobre las líneas de las órdenes de venta
   (la fuente de datos SALES_ORDER_LINE). Solo AGREGA. Idempotente. Depende de la sección (k). Los indicadores, el gráfico y las vistas de sistema los siembra el
   código por compañía (SystemAnalyticsSeeder), igual que los de Rentas.
   ---------------------------------------------------------------------------- */
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'SALES_ORDER_LINE', N'{"es":"Línea de orden de venta","en":"Sales order line"}', 126)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

/* ----------------------------------------------------------------------------
   2026-10-16 (p) — Kits (Lote 68, P4-T). Un kit es un producto que se arma al pedir con otros productos y NO tiene existencia ni movimientos propios.
   - Catálogo ProductType (STANDARD / KIT) y dbo.Product.ProductTypeLookupId (NULL = producto normal) + dbo.Product.KitAdjustable (el kit se puede ajustar al pedir).
   - dbo.ProductKitComponent: kit -> componente -> cantidad (único por par; sin kits dentro de kits lo valida el servicio).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
MERGE dbo.CatalogDomain AS t
USING (SELECT N'ProductType' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'ProductType', 1, N'{"es":"Tipo de producto","en":"Product type"}', 1, 1);
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'STANDARD', N'{"es":"Producto","en":"Product"}', 1),
    (N'KIT',      N'{"es":"Kit","en":"Kit"}',          2)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'ProductType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'ProductType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO
IF COL_LENGTH('dbo.Product', 'ProductTypeLookupId') IS NULL
    ALTER TABLE dbo.Product ADD ProductTypeLookupId INT NULL CONSTRAINT FK_Product_ProductType REFERENCES dbo.LookupCode(LookupCodeId);
GO
IF COL_LENGTH('dbo.Product', 'KitAdjustable') IS NULL
    ALTER TABLE dbo.Product ADD KitAdjustable BIT NOT NULL CONSTRAINT DF_Product_KitAdjustable DEFAULT 0;
GO
IF OBJECT_ID(N'dbo.ProductKitComponent', N'U') IS NULL CREATE TABLE dbo.ProductKitComponent (
    ProductKitComponentId INT IDENTITY(1,1) CONSTRAINT PK_ProductKitComponent PRIMARY KEY,
    TenantId           INT NOT NULL CONSTRAINT FK_ProductKitComponent_Tenant REFERENCES dbo.Tenant(TenantId),
    KitProductId       INT NOT NULL CONSTRAINT FK_ProductKitComponent_Kit REFERENCES dbo.Product(ProductId),
    ComponentProductId INT NOT NULL CONSTRAINT FK_ProductKitComponent_Component REFERENCES dbo.Product(ProductId),
    Quantity           DECIMAL(16,3) NOT NULL,
    SortOrder          INT NOT NULL CONSTRAINT DF_ProductKitComponent_SortOrder DEFAULT 0,
    CONSTRAINT CK_ProductKitComponent_Qty CHECK (Quantity > 0),
    CONSTRAINT CK_ProductKitComponent_NotSelf CHECK (KitProductId <> ComponentProductId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ProductKitComponent' AND object_id = OBJECT_ID('dbo.ProductKitComponent'))
    CREATE UNIQUE INDEX UQ_ProductKitComponent ON dbo.ProductKitComponent(KitProductId, ComponentProductId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductKitComponent_Component' AND object_id = OBJECT_ID('dbo.ProductKitComponent'))
    CREATE INDEX IX_ProductKitComponent_Component ON dbo.ProductKitComponent(ComponentProductId);
GO

/* ----------------------------------------------------------------------------
   2026-10-16 (q) — Órdenes de venta con kits (Lote 69, P4-T). Depende de las secciones (k), (l) y (p).
   - dbo.SalesOrderLineComponent: la FOTO de los componentes de un kit en cada línea de la orden (cantidad por kit); se borra con la línea.
   - dbo.SalesOrderReservation.ProductId (opcional): el producto que se reservó; NULL = el de la línea (filas anteriores); en una línea de kit es el componente.
   - dbo.PickBatchLine.KitProductId / KitQuantity (opcionales): la línea de recolección que salió de un kit y cuántos kits representa (para deshacer en kits).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.SalesOrderLineComponent', N'U') IS NULL CREATE TABLE dbo.SalesOrderLineComponent (
    SalesOrderLineComponentId INT IDENTITY(1,1) CONSTRAINT PK_SalesOrderLineComponent PRIMARY KEY,
    TenantId           INT NOT NULL CONSTRAINT FK_SalesOrderLineComponent_Tenant REFERENCES dbo.Tenant(TenantId),
    SalesOrderLineId   INT NOT NULL CONSTRAINT FK_SalesOrderLineComponent_Line REFERENCES dbo.SalesOrderLine(SalesOrderLineId) ON DELETE CASCADE,
    ComponentProductId INT NOT NULL CONSTRAINT FK_SalesOrderLineComponent_Product REFERENCES dbo.Product(ProductId),
    Quantity           DECIMAL(16,3) NOT NULL,
    CONSTRAINT CK_SalesOrderLineComponent_Qty CHECK (Quantity > 0)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_SalesOrderLineComponent' AND object_id = OBJECT_ID('dbo.SalesOrderLineComponent'))
    CREATE UNIQUE INDEX UQ_SalesOrderLineComponent ON dbo.SalesOrderLineComponent(SalesOrderLineId, ComponentProductId);
GO
IF COL_LENGTH('dbo.SalesOrderReservation', 'ProductId') IS NULL
    ALTER TABLE dbo.SalesOrderReservation ADD ProductId INT NULL CONSTRAINT FK_SalesOrderReservation_Product REFERENCES dbo.Product(ProductId);
GO
IF COL_LENGTH('dbo.PickBatchLine', 'KitProductId') IS NULL
    ALTER TABLE dbo.PickBatchLine ADD KitProductId INT NULL CONSTRAINT FK_PickBatchLine_KitProduct REFERENCES dbo.Product(ProductId);
GO
IF COL_LENGTH('dbo.PickBatchLine', 'KitQuantity') IS NULL
    ALTER TABLE dbo.PickBatchLine ADD KitQuantity DECIMAL(14,3) NULL;
GO

/* ----------------------------------------------------------------------------
   2026-10-16 (r) — Facturación (Lote 72, P5). Completa las tablas BillingRun, Invoice, InvoiceLine y Payment del DDL original:
   - dbo.BillingRun: ClientId (corrida de un solo cliente), Note, ReviewedBy/ReviewedAtUtc y ExportedAtUtc/ExportedBy.
   - dbo.Invoice: Notes.
   - dbo.InvoiceLine: SalesOrderLineId (la línea de orden de venta que se facturó), OriginalUnitAmount (el monto calculado antes de un ajuste manual) y AdjustmentNote.
   - dbo.Tenant: SalesBillingTrigger (ON_DISPATCH | ON_DELIVERY: cuándo se factura el producto vendido) e InvoiceDueDays (días hasta el vencimiento).
   - Estatus CANCELLED (terminal) de BillingRunStatus (una corrida generada o revisada se cancela y libera lo que había facturado).
   - Contador BILLINVOICE (FAC-#####), EntityType BILLING_RUN y permiso billing.view (a quien ya tiene generar, aprobar o exportar facturación).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.BillingRun', 'ClientId') IS NULL
    ALTER TABLE dbo.BillingRun ADD ClientId INT NULL CONSTRAINT FK_BillingRun_Client REFERENCES dbo.Client(ClientId);
GO
IF COL_LENGTH('dbo.BillingRun', 'Note') IS NULL
    ALTER TABLE dbo.BillingRun ADD Note NVARCHAR(500) NULL;
GO
IF COL_LENGTH('dbo.BillingRun', 'ReviewedBy') IS NULL
    ALTER TABLE dbo.BillingRun ADD ReviewedBy INT NULL CONSTRAINT FK_BillingRun_ReviewedBy REFERENCES dbo.AspNetUsers(Id);
GO
IF COL_LENGTH('dbo.BillingRun', 'ReviewedAtUtc') IS NULL
    ALTER TABLE dbo.BillingRun ADD ReviewedAtUtc DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.BillingRun', 'ExportedAtUtc') IS NULL
    ALTER TABLE dbo.BillingRun ADD ExportedAtUtc DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.BillingRun', 'ExportedBy') IS NULL
    ALTER TABLE dbo.BillingRun ADD ExportedBy INT NULL CONSTRAINT FK_BillingRun_ExportedBy REFERENCES dbo.AspNetUsers(Id);
GO
IF COL_LENGTH('dbo.Invoice', 'Notes') IS NULL
    ALTER TABLE dbo.Invoice ADD Notes NVARCHAR(500) NULL;
GO
IF COL_LENGTH('dbo.InvoiceLine', 'SalesOrderLineId') IS NULL
    ALTER TABLE dbo.InvoiceLine ADD SalesOrderLineId INT NULL CONSTRAINT FK_InvoiceLine_SalesOrderLine REFERENCES dbo.SalesOrderLine(SalesOrderLineId);
GO
IF COL_LENGTH('dbo.InvoiceLine', 'OriginalUnitAmount') IS NULL
    ALTER TABLE dbo.InvoiceLine ADD OriginalUnitAmount DECIMAL(18,4) NULL;
GO
IF COL_LENGTH('dbo.InvoiceLine', 'AdjustmentNote') IS NULL
    ALTER TABLE dbo.InvoiceLine ADD AdjustmentNote NVARCHAR(250) NULL;
GO
IF COL_LENGTH('dbo.Tenant', 'SalesBillingTrigger') IS NULL
    ALTER TABLE dbo.Tenant ADD SalesBillingTrigger VARCHAR(12) NOT NULL CONSTRAINT DF_Tenant_SalesBillingTrigger DEFAULT 'ON_DISPATCH';
GO
IF COL_LENGTH('dbo.Tenant', 'InvoiceDueDays') IS NULL
    ALTER TABLE dbo.Tenant ADD InvoiceDueDays INT NOT NULL CONSTRAINT DF_Tenant_InvoiceDueDays DEFAULT 30;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Tenant_SalesBillingTrigger')
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_SalesBillingTrigger CHECK (SalesBillingTrigger IN ('ON_DISPATCH','ON_DELIVERY'));
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Invoice_PublicId' AND object_id = OBJECT_ID('dbo.Invoice'))
    CREATE INDEX IX_Invoice_PublicId ON dbo.Invoice(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Invoice_Run' AND object_id = OBJECT_ID('dbo.Invoice'))
    CREATE INDEX IX_Invoice_Run ON dbo.Invoice(BillingRunId) WHERE BillingRunId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InvoiceLine_SalesOrderLine' AND object_id = OBJECT_ID('dbo.InvoiceLine'))
    CREATE INDEX IX_InvoiceLine_SalesOrderLine ON dbo.InvoiceLine(SalesOrderLineId) WHERE SalesOrderLineId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Payment_Invoice' AND object_id = OBJECT_ID('dbo.Payment'))
    CREATE INDEX IX_Payment_Invoice ON dbo.Payment(InvoiceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_BillingRun_Status' AND object_id = OBJECT_ID('dbo.BillingRun'))
    CREATE INDEX IX_BillingRun_Status ON dbo.BillingRun(TenantId, StatusCodeId);
GO

-- Contador de facturas: el CHECK de los tipos de contador se vuelve a crear con BILLINVOICE
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND definition NOT LIKE '%BILLINVOICE%')
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind')
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE'));
GO

-- EntityType de la corrida de facturación
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'BILLING_RUN', N'{"es":"Corrida de facturación","en":"Billing run"}', 127)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Estatus CANCELLED (terminal) de la corrida de facturación
MERGE dbo.StatusCode AS t
USING (
    SELECT N'CANCELLED' AS Code, N'{"es":"Cancelada","en":"Cancelled"}' AS LabelJson, '#6B7280' AS Color, 5 AS Srt, k.LookupCodeId AS StageKindLookupId
    FROM dbo.LookupCode k WHERE k.Entity = N'StageKind' AND k.InternalCode = N'TERMINAL'
) AS s
ON t.Entity = N'BillingRunStatus' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (N'BillingRunStatus', s.Code, s.LabelJson, s.Color, s.Srt, s.StageKindLookupId, 0, 1);
GO

-- Permiso billing.view
MERGE dbo.Permission AS t
USING (VALUES ('billing.view', N'{"es":"Ver facturación","en":"View billing"}')) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'BILLING'), s.LabelJson, 1);
GO
-- A todo rol (plantilla y clonados) que ya genera, aprueba o exporta facturación, y al administrador de la compañía
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, v.PermissionId
FROM dbo.Role r
JOIN dbo.Permission v ON v.Code = 'billing.view'
WHERE (r.Name = 'TenantAdmin' OR EXISTS (
          SELECT 1 FROM dbo.RolePermission rp JOIN dbo.Permission p ON p.PermissionId = rp.PermissionId
          WHERE rp.RoleId = r.RoleId AND p.Code IN ('billing.generate','billing.approve','billing.export')))
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = v.PermissionId);
GO

-- =====================================================================================================================
-- 2026-10-16 (s) Lote 74 — flete de las órdenes de transporte en la facturación
-- TransportOrder.FreightInvoiced: el flete de la orden ya está en una factura no anulada (se reclama al generar la corrida bajo bloqueo de la orden
-- y se libera al cancelar la corrida o anular la factura). Las líneas de factura ya traen TransportOrderId, RateComponentId y ChargeType desde el diseño original.
-- =====================================================================================================================
IF COL_LENGTH('dbo.TransportOrder', 'FreightInvoiced') IS NULL
    ALTER TABLE dbo.TransportOrder ADD FreightInvoiced BIT NOT NULL CONSTRAINT DF_TransportOrder_FreightInvoiced DEFAULT 0;
GO

-- =====================================================================================================================
-- 2026-10-16 (t) Lote 75 — pagos de las facturas y estado de cuenta
-- Payment: quién lo registró y la anulación (un pago no se borra: se anula con motivo y deja de contar en el saldo).
-- =====================================================================================================================
IF COL_LENGTH('dbo.Payment', 'RecordedBy') IS NULL ALTER TABLE dbo.Payment ADD RecordedBy INT NULL;
GO
IF COL_LENGTH('dbo.Payment', 'VoidedAtUtc') IS NULL ALTER TABLE dbo.Payment ADD VoidedAtUtc DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.Payment', 'VoidedBy') IS NULL ALTER TABLE dbo.Payment ADD VoidedBy INT NULL;
GO
IF COL_LENGTH('dbo.Payment', 'VoidReason') IS NULL ALTER TABLE dbo.Payment ADD VoidReason NVARCHAR(250) NULL;
GO

-- =====================================================================================================================
-- 2026-10-16 (u) Lote 77 — plantillas de exportación a contabilidad
-- ExportTemplate: formato (CSV/TXT), delimitador, formato de fecha, encabezado y columnas (JSON) por compañía y alcance (hoy BILLING). Una plantilla no se borra: se desactiva (IsActive = 0).
-- El nombre no se repite entre las activas de un alcance. EntityType EXPORT_TEMPLATE para la bitácora.
-- =====================================================================================================================
IF OBJECT_ID(N'dbo.ExportTemplate', N'U') IS NULL CREATE TABLE dbo.ExportTemplate (
    ExportTemplateId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_ExportTemplate_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Scope VARCHAR(20) NOT NULL,
    Name NVARCHAR(80) NOT NULL,
    Format VARCHAR(3) NOT NULL CONSTRAINT CK_ExportTemplate_Format CHECK (Format IN ('CSV','TXT')),
    Delimiter NCHAR(1) NOT NULL,
    DateFormat VARCHAR(10) NOT NULL CONSTRAINT CK_ExportTemplate_DateFormat CHECK (DateFormat IN ('YYYY-MM-DD','MM/DD/YYYY','DD/MM/YYYY')),
    IncludeHeader BIT NOT NULL CONSTRAINT DF_ExportTemplate_Header DEFAULT 1,
    ColumnsJson NVARCHAR(MAX) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_ExportTemplate_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ExportTemplate_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL, UpdatedAtUtc DATETIME2 NULL, UpdatedBy INT NULL,
    RowVersion ROWVERSION
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ExportTemplate_PublicId' AND object_id = OBJECT_ID('dbo.ExportTemplate')) CREATE INDEX IX_ExportTemplate_PublicId ON dbo.ExportTemplate(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ExportTemplate_Name' AND object_id = OBJECT_ID('dbo.ExportTemplate')) CREATE UNIQUE INDEX UX_ExportTemplate_Name ON dbo.ExportTemplate(TenantId, Scope, Name) WHERE IsActive = 1;
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'EXPORT_TEMPLATE', N'{"es":"Plantilla de exportación","en":"Export template"}', 128)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- =====================================================================================================================
-- 2026-10-16 (v) Lote 79 (P6) — notas de crédito
-- CreditNote / CreditNoteLine / CreditApplication; dominio de estatus CreditNoteStatus (DRAFT -> APPROVED -> APPLIED; VOID), catálogo CreditReason, EntityType CREDIT_NOTE,
-- contador CREDITNOTE (NC-#####) y permiso billing.credit (a quien ya aprueba facturación y al administrador de la compañía, también en los roles ya clonados). Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF OBJECT_ID(N'dbo.CreditNote', N'U') IS NULL CREATE TABLE dbo.CreditNote (
    CreditNoteId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_CreditNote_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    CreditNumber NVARCHAR(30) NOT NULL,
    ClientId INT NOT NULL REFERENCES dbo.Client(ClientId),
    InvoiceId INT NULL REFERENCES dbo.Invoice(InvoiceId),
    ExternalInvoiceRef NVARCHAR(80) NULL,
    ReasonLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    CurrencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    Subtotal DECIMAL(18,4) NOT NULL CONSTRAINT DF_CreditNote_Subtotal DEFAULT 0,
    TaxAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_CreditNote_Tax DEFAULT 0,
    Note NVARCHAR(500) NULL,
    ApprovedBy INT NULL, ApprovedAtUtc DATETIME2 NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CreditNote_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL, UpdatedAtUtc DATETIME2 NULL, UpdatedBy INT NULL,
    RowVersion ROWVERSION
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CreditNote_PublicId' AND object_id = OBJECT_ID('dbo.CreditNote')) CREATE INDEX IX_CreditNote_PublicId ON dbo.CreditNote(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_CreditNote_Number' AND object_id = OBJECT_ID('dbo.CreditNote')) CREATE UNIQUE INDEX UX_CreditNote_Number ON dbo.CreditNote(TenantId, CreditNumber);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CreditNote_Client' AND object_id = OBJECT_ID('dbo.CreditNote')) CREATE INDEX IX_CreditNote_Client ON dbo.CreditNote(TenantId, ClientId, StatusCodeId);
GO
IF OBJECT_ID(N'dbo.CreditNoteLine', N'U') IS NULL CREATE TABLE dbo.CreditNoteLine (
    CreditNoteLineId INT IDENTITY(1,1) PRIMARY KEY,
    CreditNoteId INT NOT NULL REFERENCES dbo.CreditNote(CreditNoteId),
    InvoiceLineId INT NULL REFERENCES dbo.InvoiceLine(InvoiceLineId),
    ProductId INT NULL REFERENCES dbo.Product(ProductId),
    ChargeTypeLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Description NVARCHAR(250) NOT NULL,
    Quantity DECIMAL(14,3) NOT NULL CONSTRAINT DF_CreditNoteLine_Qty DEFAULT 1,
    UnitAmount DECIMAL(18,4) NOT NULL,
    TaxRateId INT NULL REFERENCES dbo.TaxRate(TaxRateId),
    TaxRatePct DECIMAL(9,4) NULL,
    TaxAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_CreditNoteLine_Tax DEFAULT 0,
    IsTaxExempt BIT NOT NULL CONSTRAINT DF_CreditNoteLine_Exempt DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CreditNoteLine_InvoiceLine' AND object_id = OBJECT_ID('dbo.CreditNoteLine')) CREATE INDEX IX_CreditNoteLine_InvoiceLine ON dbo.CreditNoteLine(InvoiceLineId) WHERE InvoiceLineId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CreditNoteLine_Note' AND object_id = OBJECT_ID('dbo.CreditNoteLine')) CREATE INDEX IX_CreditNoteLine_Note ON dbo.CreditNoteLine(CreditNoteId);
GO
IF OBJECT_ID(N'dbo.CreditApplication', N'U') IS NULL CREATE TABLE dbo.CreditApplication (
    CreditApplicationId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    CreditNoteId INT NOT NULL REFERENCES dbo.CreditNote(CreditNoteId),
    InvoiceId INT NOT NULL REFERENCES dbo.Invoice(InvoiceId),
    Amount DECIMAL(18,4) NOT NULL CONSTRAINT CK_CreditApplication_Amount CHECK (Amount > 0),
    AppliedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CreditApplication_At DEFAULT SYSUTCDATETIME(),
    AppliedBy INT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CreditApplication_Invoice' AND object_id = OBJECT_ID('dbo.CreditApplication')) CREATE INDEX IX_CreditApplication_Invoice ON dbo.CreditApplication(InvoiceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CreditApplication_Note' AND object_id = OBJECT_ID('dbo.CreditApplication')) CREATE INDEX IX_CreditApplication_Note ON dbo.CreditApplication(CreditNoteId);
GO

-- Dominio de estatus CreditNoteStatus y sus estatus (VOID se alcanza desde borrador o aprobada: entrada lateral permitida desde cualquier etapa)
MERGE dbo.CatalogDomain AS t
USING (SELECT N'CreditNoteStatus' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'CreditNoteStatus', 2, N'{"es":"Estatus de la nota de crédito","en":"Credit note status"}', 1, 1);
GO
MERGE dbo.StatusCode AS t
USING (
    SELECT v.Code, v.LabelJson, v.Color, v.Srt, v.Init, k.LookupCodeId AS StageKindLookupId
    FROM (VALUES
        (N'DRAFT',    N'{"es":"Borrador","en":"Draft"}',    '#9CA3AF', 1, 1, 'PIPELINE'),
        (N'APPROVED', N'{"es":"Aprobada","en":"Approved"}', '#3B82F6', 2, 0, 'PIPELINE'),
        (N'APPLIED',  N'{"es":"Aplicada","en":"Applied"}',  '#059669', 3, 0, 'TERMINAL'),
        (N'VOID',     N'{"es":"Anulada","en":"Void"}',      '#6B7280', 4, 0, 'TERMINAL')
    ) AS v (Code, LabelJson, Color, Srt, Init, Kind)
    JOIN dbo.LookupCode k ON k.Entity = N'StageKind' AND k.InternalCode = v.Kind
) AS s
ON t.Entity = N'CreditNoteStatus' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (N'CreditNoteStatus', s.Code, s.LabelJson, s.Color, s.Srt, s.StageKindLookupId, s.Init, 1);
GO

-- Catálogo de motivos de la nota de crédito
MERGE dbo.CatalogDomain AS t
USING (SELECT N'CreditReason' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'CreditReason', 1, N'{"es":"Motivo de la nota de crédito","en":"Credit note reason"}', 1, 1);
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'RETURN',           N'{"es":"Devolución","en":"Return"}',                       1),
    (N'PRICE_ADJUSTMENT', N'{"es":"Ajuste de precio","en":"Price adjustment"}',       2),
    (N'SERVICE_FAILURE',  N'{"es":"Falla del servicio","en":"Service failure"}',      3),
    (N'BILLING_ERROR',    N'{"es":"Error de facturación","en":"Billing error"}',      4)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'CreditReason' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'CreditReason', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- EntityType de la bitácora y del historial de estatus
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'CREDIT_NOTE', N'{"es":"Nota de crédito","en":"Credit note"}', 129)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Contador de notas de crédito: el CHECK de los tipos de contador se vuelve a crear con CREDITNOTE
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND definition NOT LIKE '%CREDITNOTE%')
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind')
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE','CREDITNOTE'));
GO

-- Permiso billing.credit
MERGE dbo.Permission AS t
USING (VALUES ('billing.credit', N'{"es":"Notas de crédito","en":"Credit notes"}')) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'BILLING'), s.LabelJson, 1);
GO
-- A todo rol (plantilla y clonados) que ya aprueba facturación, y al administrador de la compañía
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, v.PermissionId
FROM dbo.Role r
JOIN dbo.Permission v ON v.Code = 'billing.credit'
WHERE (r.Name = 'TenantAdmin' OR EXISTS (
          SELECT 1 FROM dbo.RolePermission rp JOIN dbo.Permission p ON p.PermissionId = rp.PermissionId
          WHERE rp.RoleId = r.RoleId AND p.Code = 'billing.approve'))
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = v.PermissionId);
GO

-- =====================================================================================================================
-- 2026-10-16 (w) Lote 81 (P6) — devoluciones de venta
-- SalesReturn / SalesReturnLine; dominio de estatus SalesReturnStatus (REQUESTED -> AUTHORIZED -> RECEIVED -> CREDITED; REJECTED y CANCELLED), catálogo ReturnReason, causa de daño CUSTOMER_RETURN,
-- EntityType SALES_RETURN, contador SALESRETURN (DV-#####), Tenant.SalesReturnWindowDays, ReceiptHeader.SalesReturnId, CreditNote.SalesReturnId y los permisos sales.return y sales.return.authorize.
-- Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF COL_LENGTH('dbo.Tenant', 'SalesReturnWindowDays') IS NULL
    ALTER TABLE dbo.Tenant ADD SalesReturnWindowDays INT NOT NULL CONSTRAINT DF_Tenant_SalesReturnWindowDays DEFAULT 30;
GO
IF OBJECT_ID(N'dbo.SalesReturn', N'U') IS NULL CREATE TABLE dbo.SalesReturn (
    SalesReturnId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_SalesReturn_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Number NVARCHAR(30) NOT NULL,
    SalesOrderId INT NOT NULL REFERENCES dbo.SalesOrder(SalesOrderId),
    ClientId INT NOT NULL REFERENCES dbo.Client(ClientId),
    WarehouseId INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    ReasonLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    Note NVARCHAR(500) NULL,
    RequestedBy INT NULL,
    RequestedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_SalesReturn_Requested DEFAULT SYSUTCDATETIME(),
    OutsideWindow BIT NOT NULL CONSTRAINT DF_SalesReturn_Outside DEFAULT 0,
    AuthorizedBy INT NULL, AuthorizedAtUtc DATETIME2 NULL, AuthorizationNote NVARCHAR(500) NULL,
    ReceivedAtUtc DATETIME2 NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_SalesReturn_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL, UpdatedAtUtc DATETIME2 NULL, UpdatedBy INT NULL,
    RowVersion ROWVERSION
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesReturn_PublicId' AND object_id = OBJECT_ID('dbo.SalesReturn')) CREATE INDEX IX_SalesReturn_PublicId ON dbo.SalesReturn(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SalesReturn_Number' AND object_id = OBJECT_ID('dbo.SalesReturn')) CREATE UNIQUE INDEX UX_SalesReturn_Number ON dbo.SalesReturn(TenantId, Number);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesReturn_Order' AND object_id = OBJECT_ID('dbo.SalesReturn')) CREATE INDEX IX_SalesReturn_Order ON dbo.SalesReturn(TenantId, SalesOrderId);
GO
IF OBJECT_ID(N'dbo.SalesReturnLine', N'U') IS NULL CREATE TABLE dbo.SalesReturnLine (
    SalesReturnLineId INT IDENTITY(1,1) PRIMARY KEY,
    SalesReturnId INT NOT NULL REFERENCES dbo.SalesReturn(SalesReturnId),
    SalesOrderLineId INT NOT NULL REFERENCES dbo.SalesOrderLine(SalesOrderLineId),
    ProductId INT NOT NULL REFERENCES dbo.Product(ProductId),
    QtyRequested DECIMAL(14,3) NOT NULL,
    QtyReceived DECIMAL(14,3) NOT NULL CONSTRAINT DF_SalesReturnLine_Received DEFAULT 0,
    QtyDamaged DECIMAL(14,3) NOT NULL CONSTRAINT DF_SalesReturnLine_Damaged DEFAULT 0,
    QtyCredited DECIMAL(14,3) NOT NULL CONSTRAINT DF_SalesReturnLine_Credited DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesReturnLine_OrderLine' AND object_id = OBJECT_ID('dbo.SalesReturnLine')) CREATE INDEX IX_SalesReturnLine_OrderLine ON dbo.SalesReturnLine(SalesOrderLineId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SalesReturnLine_Return' AND object_id = OBJECT_ID('dbo.SalesReturnLine')) CREATE INDEX IX_SalesReturnLine_Return ON dbo.SalesReturnLine(SalesReturnId);
GO
IF COL_LENGTH('dbo.ReceiptHeader', 'SalesReturnId') IS NULL
    ALTER TABLE dbo.ReceiptHeader ADD SalesReturnId INT NULL CONSTRAINT FK_ReceiptHeader_SalesReturn REFERENCES dbo.SalesReturn(SalesReturnId);
GO
IF COL_LENGTH('dbo.CreditNote', 'SalesReturnId') IS NULL
    ALTER TABLE dbo.CreditNote ADD SalesReturnId INT NULL CONSTRAINT FK_CreditNote_SalesReturn REFERENCES dbo.SalesReturn(SalesReturnId);
GO

-- Dominio de estatus SalesReturnStatus y sus estatus (REJECTED y CANCELLED se alcanzan desde una etapa anterior: entrada lateral permitida desde cualquier etapa)
MERGE dbo.CatalogDomain AS t
USING (SELECT N'SalesReturnStatus' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'SalesReturnStatus', 2, N'{"es":"Estatus de la devolución de venta","en":"Sales return status"}', 1, 1);
GO
MERGE dbo.StatusCode AS t
USING (
    SELECT v.Code, v.LabelJson, v.Color, v.Srt, v.Init, k.LookupCodeId AS StageKindLookupId
    FROM (VALUES
        (N'REQUESTED',  N'{"es":"Solicitada","en":"Requested"}',   '#9CA3AF', 1, 1, 'PIPELINE'),
        (N'AUTHORIZED', N'{"es":"Autorizada","en":"Authorized"}',  '#3B82F6', 2, 0, 'PIPELINE'),
        (N'RECEIVED',   N'{"es":"Recibida","en":"Received"}',      '#F59E0B', 3, 0, 'PIPELINE'),
        (N'CREDITED',   N'{"es":"Acreditada","en":"Credited"}',    '#059669', 4, 0, 'TERMINAL'),
        (N'REJECTED',   N'{"es":"Rechazada","en":"Rejected"}',     '#EF4444', 5, 0, 'TERMINAL'),
        (N'CANCELLED',  N'{"es":"Cancelada","en":"Cancelled"}',    '#6B7280', 6, 0, 'TERMINAL')
    ) AS v (Code, LabelJson, Color, Srt, Init, Kind)
    JOIN dbo.LookupCode k ON k.Entity = N'StageKind' AND k.InternalCode = v.Kind
) AS s
ON t.Entity = N'SalesReturnStatus' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (N'SalesReturnStatus', s.Code, s.LabelJson, s.Color, s.Srt, s.StageKindLookupId, s.Init, 1);
GO

-- Catálogo de motivos de la devolución
MERGE dbo.CatalogDomain AS t
USING (SELECT N'ReturnReason' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'ReturnReason', 1, N'{"es":"Motivo de la devolución","en":"Return reason"}', 1, 1);
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'DAMAGED',    N'{"es":"Dañado","en":"Damaged"}',                         1),
    (N'WRONG_ITEM', N'{"es":"Equivocado","en":"Wrong item"}',                  2),
    (N'NOT_NEEDED', N'{"es":"No lo necesitaba o sobrante","en":"Not needed or surplus"}', 3),
    (N'EXPIRED',    N'{"es":"Vencido","en":"Expired"}',                        4),
    (N'OTHER',      N'{"es":"Otro","en":"Other"}',                             5)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'ReturnReason' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'ReturnReason', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Causa de daño «Devolución de cliente»
MERGE dbo.LookupCode AS t
USING (VALUES (N'CUSTOMER_RETURN', N'{"es":"Devolución de cliente","en":"Customer return"}', 5)) AS s (Code, LabelJson, Srt)
ON t.Entity = N'DamageCause' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'DamageCause', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- EntityType de la bitácora y del historial de estatus
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'SALES_RETURN', N'{"es":"Devolución de venta","en":"Sales return"}', 130)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Contador de devoluciones: el CHECK de los tipos de contador se vuelve a crear con SALESRETURN
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND definition NOT LIKE '%SALESRETURN%')
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind')
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE','CREDITNOTE','SALESRETURN'));
GO

-- Permisos sales.return y sales.return.authorize
MERGE dbo.Permission AS t
USING (VALUES
    ('sales.return',           N'{"es":"Pedir devoluciones de venta","en":"Request sales returns"}'),
    ('sales.return.authorize', N'{"es":"Autorizar devoluciones de venta","en":"Authorize sales returns"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ORDERS'), s.LabelJson, 1);
GO
-- sales.return: a todo rol (plantilla y clonados) que ya crea órdenes de venta y al administrador de la compañía
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, v.PermissionId
FROM dbo.Role r
JOIN dbo.Permission v ON v.Code = 'sales.return'
WHERE (r.Name = 'TenantAdmin' OR EXISTS (
          SELECT 1 FROM dbo.RolePermission rp JOIN dbo.Permission p ON p.PermissionId = rp.PermissionId
          WHERE rp.RoleId = r.RoleId AND p.Code = 'sales.create'))
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = v.PermissionId);
GO
-- sales.return.authorize: al administrador de la compañía y a quien ya cancela órdenes de venta (supervisión)
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, v.PermissionId
FROM dbo.Role r
JOIN dbo.Permission v ON v.Code = 'sales.return.authorize'
WHERE (r.Name = 'TenantAdmin' OR EXISTS (
          SELECT 1 FROM dbo.RolePermission rp JOIN dbo.Permission p ON p.PermissionId = rp.PermissionId
          WHERE rp.RoleId = r.RoleId AND p.Code = 'sales.cancel'))
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = v.PermissionId);
GO

-- =====================================================================================================================
-- 2026-10-17 (x) Lote 84 (P8) — COD: cobro contra entrega y reconciliación por hojas escaneadas
-- CodCollection (ya existía en el DDL original) gana quién lo cobró, quién lo cuadró y en qué corrida; tablas CodReconcileRun / CodReconcileItem (cada corrida de reconciliación con los códigos que
-- se procesaron y su resultado); EntityType COD_COLLECTION de la bitácora. Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF COL_LENGTH('dbo.CodCollection', 'CollectedBy') IS NULL
    ALTER TABLE dbo.CodCollection ADD CollectedBy INT NULL;
GO
IF COL_LENGTH('dbo.CodCollection', 'ReconciledBy') IS NULL
    ALTER TABLE dbo.CodCollection ADD ReconciledBy INT NULL;
GO
IF OBJECT_ID(N'dbo.CodReconcileRun', N'U') IS NULL CREATE TABLE dbo.CodReconcileRun (
    CodReconcileRunId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_CodReconcileRun_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Source NVARCHAR(300) NULL,
    Total INT NOT NULL CONSTRAINT DF_CodReconcileRun_Total DEFAULT 0,
    Reconciled INT NOT NULL CONSTRAINT DF_CodReconcileRun_Reconciled DEFAULT 0,
    Issues INT NOT NULL CONSTRAINT DF_CodReconcileRun_Issues DEFAULT 0,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_CodReconcileRun_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CodReconcileRun_PublicId' AND object_id = OBJECT_ID('dbo.CodReconcileRun')) CREATE INDEX IX_CodReconcileRun_PublicId ON dbo.CodReconcileRun(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CodReconcileRun_Tenant' AND object_id = OBJECT_ID('dbo.CodReconcileRun')) CREATE INDEX IX_CodReconcileRun_Tenant ON dbo.CodReconcileRun(TenantId, CreatedAtUtc);
GO
IF OBJECT_ID(N'dbo.CodReconcileItem', N'U') IS NULL CREATE TABLE dbo.CodReconcileItem (
    CodReconcileItemId INT IDENTITY(1,1) PRIMARY KEY,
    CodReconcileRunId INT NOT NULL REFERENCES dbo.CodReconcileRun(CodReconcileRunId),
    Code NVARCHAR(80) NOT NULL,
    Source NVARCHAR(300) NULL,
    Result NVARCHAR(30) NOT NULL,
    TransportOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    Amount DECIMAL(18,4) NOT NULL CONSTRAINT DF_CodReconcileItem_Amount DEFAULT 0,
    Collections INT NOT NULL CONSTRAINT DF_CodReconcileItem_Collections DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CodReconcileItem_Run' AND object_id = OBJECT_ID('dbo.CodReconcileItem')) CREATE INDEX IX_CodReconcileItem_Run ON dbo.CodReconcileItem(CodReconcileRunId);
GO
IF COL_LENGTH('dbo.CodCollection', 'ReconcileRunId') IS NULL
    ALTER TABLE dbo.CodCollection ADD ReconcileRunId INT NULL CONSTRAINT FK_CodCollection_ReconcileRun REFERENCES dbo.CodReconcileRun(CodReconcileRunId);
GO

-- EntityType de la bitácora del cobro
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'COD_COLLECTION', N'{"es":"Cobro COD","en":"COD collection"}', 140)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- =====================================================================================================================
-- 2026-10-17 (y) Lote 86 (P8) — remesa COD al cliente
-- CodRemittanceBatch / CodRemittanceLine (ya existían en el DDL original; NetRemitted y NetAmount son columnas calculadas); comisión por omisión de la compañía (Tenant.CodDefaultCommissionPct, 8 %);
-- EntityType COD_REMITTANCE (bitácora e historial de estatus); contador CODREMIT (REM-#####). Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF COL_LENGTH('dbo.Tenant', 'CodDefaultCommissionPct') IS NULL
    ALTER TABLE dbo.Tenant ADD CodDefaultCommissionPct DECIMAL(5,2) NOT NULL CONSTRAINT DF_Tenant_CodDefaultCommissionPct DEFAULT 8;
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'COD_REMITTANCE', N'{"es":"Remesa COD","en":"COD remittance"}', 150)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO
-- Contador de remesas: el CHECK de los tipos de contador se vuelve a crear con CODREMIT
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND definition NOT LIKE '%CODREMIT%')
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind')
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE','CREDITNOTE','SALESRETURN','CODREMIT'));
GO

-- =====================================================================================================================
-- 2026-10-17 (z) Lote 88 (P12) — los dos ríos del Pulso: permisos pulse.orders (río «Paquetes en la calle») y pulse.cod (río «Dinero COD»)
-- A los roles que ya existen (plantillas y clonados, por nombre): Despachador, Facturación y Solo lectura → pulse.orders; Facturación y Solo lectura → pulse.cod; el administrador de la compañía los recibe por
-- «todos los permisos». Solo AGREGA. Idempotente.
-- =====================================================================================================================
MERGE dbo.Permission AS t
USING (VALUES
    ('pulse.orders', N'{"es":"Ver el río Paquetes en la calle del Pulso","en":"See the Packages on the street river"}'),
    ('pulse.cod',    N'{"es":"Ver el río Dinero COD del Pulso","en":"See the COD money river"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'PULSE'), s.LabelJson, 1);
GO
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON (p.Code = 'pulse.orders' AND r.Name IN ('TenantAdmin', 'Dispatcher', 'Billing', 'ReadOnly'))
                      OR (p.Code = 'pulse.cod' AND r.Name IN ('TenantAdmin', 'Billing', 'ReadOnly'))
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);
GO

-- =====================================================================================================================
-- 2026-10-17 (aa) Lote 90 (P12) — fotos diarias de indicadores
-- KpiDailySnapshot ya existía en el DDL original; aquí se siembran las claves de indicador (dominio KpiKey) que guarda el trabajo KPI_SNAPSHOT (órdenes por etapa y dinero COD). El trabajo mismo lo crea el ejecutor de
-- trabajos programados con su horario de fábrica (23:55). Solo AGREGA. Idempotente.
-- =====================================================================================================================
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'ORDERS_CREATED',        N'{"es":"Órdenes nuevas del día","en":"New orders of the day"}',               10),
    (N'ORDERS_IN_WAREHOUSE',   N'{"es":"Órdenes en almacén","en":"Orders in warehouse"}',                     11),
    (N'ORDERS_UNASSIGNED',     N'{"es":"Órdenes sin chofer","en":"Orders without a driver"}',                 12),
    (N'ORDERS_ON_ROUTE',       N'{"es":"Órdenes en ruta","en":"Orders on route"}',                            13),
    (N'ORDERS_DELIVERED',      N'{"es":"Órdenes entregadas del día","en":"Orders delivered in the day"}',     14),
    (N'ORDERS_OPEN',           N'{"es":"Órdenes abiertas","en":"Open orders"}',                               15),
    (N'COD_TO_COLLECT',        N'{"es":"COD por cobrar","en":"COD to collect"}',                              16),
    (N'COD_COLLECTED',         N'{"es":"COD cobrado sin cuadrar","en":"COD collected, not reconciled"}',      17),
    (N'COD_RECONCILED',        N'{"es":"COD cuadrado sin remesa","en":"COD reconciled, no remittance"}',      18),
    (N'COD_REMITTANCE_READY',  N'{"es":"Remesas listas (neto)","en":"Remittances ready (net)"}',              19)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'KpiKey' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'KpiKey', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- =====================================================================================================================
-- 2026-10-17 (ab) Lote 91 (P12) — «Actividad reciente» para Operación y Contabilidad
-- Catálogo ActivityEventType de las pestañas Operación (órdenes y rutas) y Contabilidad (facturación y COD); el almacén ya tenía el suyo en el seed original. Qué transición produce cada
-- evento vive en código (OperationsActivityProvider, BillingActivityProvider, CodActivityProvider). Solo AGREGA. Idempotente.
-- =====================================================================================================================
MERGE dbo.LookupCode AS t
USING (
    SELECT v.Code, N'{"es":"' + v.Es + N'","en":"' + v.En + N'"}' AS LabelJson,
           N'{"module":"' + v.Module + N'","mandatory":' + CASE v.Mandatory WHEN 1 THEN N'true' ELSE N'false' END
             + N',"defaultOn":' + CASE v.DefaultOn WHEN 1 THEN N'true' ELSE N'false' END + N'}' AS ExtraJson,
           v.Srt
    FROM (VALUES
    (N'ORDER_DELIVERED', N'Orden entregada', N'Order delivered', N'OPERATIONS', 0, 1, 101),
    (N'ORDER_FAILED', N'Orden fallida', N'Order failed', N'OPERATIONS', 1, 1, 102),
    (N'ORDER_CANCELLED', N'Orden cancelada', N'Order cancelled', N'OPERATIONS', 1, 1, 103),
    (N'ORDER_ON_HOLD', N'Orden en pausa', N'Order on hold', N'OPERATIONS', 0, 1, 104),
    (N'TRIP_DISPATCHED', N'Ruta despachada', N'Trip dispatched', N'OPERATIONS', 0, 1, 105),
    (N'TRIP_COMPLETED', N'Ruta completada', N'Trip completed', N'OPERATIONS', 0, 1, 106),
    (N'TRIP_CANCELLED', N'Ruta cancelada', N'Trip cancelled', N'OPERATIONS', 1, 1, 107),
    (N'INVOICE_ISSUED', N'Factura emitida', N'Invoice issued', N'ACCOUNTING', 0, 1, 201),
    (N'INVOICE_PAID', N'Factura pagada', N'Invoice paid', N'ACCOUNTING', 0, 1, 202),
    (N'INVOICE_OVERDUE', N'Factura vencida', N'Invoice overdue', N'ACCOUNTING', 1, 1, 203),
    (N'INVOICE_VOIDED', N'Factura anulada', N'Invoice voided', N'ACCOUNTING', 1, 1, 204),
    (N'PAYMENT_RECORDED', N'Pago registrado', N'Payment recorded', N'ACCOUNTING', 0, 1, 205),
    (N'PAYMENT_VOIDED', N'Pago anulado', N'Payment voided', N'ACCOUNTING', 1, 1, 206),
    (N'BILLING_RUN_APPROVED', N'Corrida de facturación aprobada', N'Billing run approved', N'ACCOUNTING', 1, 1, 207),
    (N'COD_COLLECTED', N'Cobro COD registrado', N'COD collection recorded', N'ACCOUNTING', 0, 1, 211),
    (N'COD_RECONCILED', N'Cobro COD cuadrado', N'COD collection reconciled', N'ACCOUNTING', 0, 1, 212),
    (N'COD_REMITTANCE_APPROVED', N'Remesa COD aprobada', N'COD remittance approved', N'ACCOUNTING', 1, 1, 213),
    (N'COD_REMITTED', N'Remesa COD remitida', N'COD remittance sent', N'ACCOUNTING', 1, 1, 214)
    ) v(Code, Es, En, Module, Mandatory, DefaultOn, Srt)
) AS s ON t.Entity = N'ActivityEventType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ExtraJson, SortOrder, IsSystem, IsActive)
    VALUES (N'ActivityEventType', s.Code, s.LabelJson, s.ExtraJson, s.Srt, 1, 1);
GO

-- =====================================================================================================================
-- 2026-10-17 (ac) Lote 92 (P12) — informes programados por correo
-- ReportDefinition ya traía ScheduleCron y DeliveryEmails; se agregan el período que se envía y el resultado del último envío. El trabajo REPORT_DELIVERY se registra solo (trabajos programados).
-- Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF COL_LENGTH('dbo.ReportDefinition', 'DeliveryRangeMode') IS NULL
    ALTER TABLE dbo.ReportDefinition ADD DeliveryRangeMode NVARCHAR(20) NULL;
GO
IF COL_LENGTH('dbo.ReportDefinition', 'LastDeliveryUtc') IS NULL
    ALTER TABLE dbo.ReportDefinition ADD LastDeliveryUtc DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.ReportDefinition', 'LastDeliveryStatus') IS NULL
    ALTER TABLE dbo.ReportDefinition ADD LastDeliveryStatus NVARCHAR(20) NULL;
GO
IF COL_LENGTH('dbo.ReportDefinition', 'LastDeliveryMessage') IS NULL
    ALTER TABLE dbo.ReportDefinition ADD LastDeliveryMessage NVARCHAR(400) NULL;
GO

-- =====================================================================================================================
-- 2026-10-17 (ad) Lote 93 (P9) — intentos de entrega
-- Tabla DeliveryAttempt (una fila por intento de entrega de una orden: número, chofer, resultado y razón de fallo); es la fuente de verdad de la liquidación a choferes. Un intento no se borra: se anula
-- (IsActive 0, con motivo). EntityType DELIVERY_ATTEMPT para la bitácora. Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF OBJECT_ID(N'dbo.DeliveryAttempt', N'U') IS NULL CREATE TABLE dbo.DeliveryAttempt (
    DeliveryAttemptId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_DeliveryAttempt_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    AttemptNumber INT NOT NULL,
    DriverId INT NULL REFERENCES dbo.Driver(DriverId),
    TripId INT NULL REFERENCES dbo.Trip(TripId),
    AttemptedAtUtc DATETIME2 NOT NULL,
    Delivered BIT NOT NULL,
    FailureReasonLookupId INT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Notes NVARCHAR(400) NULL,
    Source NVARCHAR(20) NOT NULL,
    RecordedBy INT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_DeliveryAttempt_IsActive DEFAULT 1,
    VoidedAtUtc DATETIME2 NULL,
    VoidedBy INT NULL,
    VoidReason NVARCHAR(400) NULL,
    SettledRunId INT NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_DeliveryAttempt_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_DeliveryAttempt_Number CHECK (AttemptNumber >= 1)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DeliveryAttempt_PublicId' AND object_id = OBJECT_ID('dbo.DeliveryAttempt')) CREATE UNIQUE INDEX IX_DeliveryAttempt_PublicId ON dbo.DeliveryAttempt(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DeliveryAttempt_Order_Number' AND object_id = OBJECT_ID('dbo.DeliveryAttempt')) CREATE UNIQUE INDEX UX_DeliveryAttempt_Order_Number ON dbo.DeliveryAttempt(TransportOrderId, AttemptNumber) WHERE IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DeliveryAttempt_Order' AND object_id = OBJECT_ID('dbo.DeliveryAttempt')) CREATE INDEX IX_DeliveryAttempt_Order ON dbo.DeliveryAttempt(TransportOrderId, AttemptNumber);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DeliveryAttempt_Driver' AND object_id = OBJECT_ID('dbo.DeliveryAttempt')) CREATE INDEX IX_DeliveryAttempt_Driver ON dbo.DeliveryAttempt(TenantId, DriverId, AttemptedAtUtc);
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'DELIVERY_ATTEMPT', N'{"es":"Intento de entrega","en":"Delivery attempt"}', 160)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- =====================================================================================================================
-- 2026-10-17 (ae) Lote 94 (P9) — liquidación a choferes
-- Tablas nuevas DriverSettlementRun / DriverSettlementLine (el DDL original trae CarrierSettlement / SettlementLine, otro diseño; no se tocan). DeliveryAttempt.SettledRunId y DriverTrip.SettlementRunId
-- reservan lo que una corrida ya pagó. Dominio de estatus DriverSettlementStatus (GENERATED, REVIEWED, APPROVED, EXPORTED, CANCELLED), EntityType DRIVER_SETTLEMENT, contador DRIVERSETTLE (LIQ-#####) y
-- permisos driverpay.approve y driverpay.export (a TenantAdmin). Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF OBJECT_ID(N'dbo.DriverSettlementRun', N'U') IS NULL CREATE TABLE dbo.DriverSettlementRun (
    DriverSettlementRunId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_DriverSettlementRun_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Number NVARCHAR(40) NOT NULL,
    DriverId INT NULL REFERENCES dbo.Driver(DriverId),
    PeriodStart DATE NOT NULL,
    PeriodEnd DATE NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    PayoutFormulaCode NVARCHAR(40) NOT NULL,
    CurrencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    LineCount INT NOT NULL CONSTRAINT DF_DriverSettlementRun_LineCount DEFAULT 0,
    TotalAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_DriverSettlementRun_Total DEFAULT 0,
    SkippedNoDriver INT NOT NULL CONSTRAINT DF_DriverSettlementRun_Skipped DEFAULT 0,
    Note NVARCHAR(500) NULL,
    GeneratedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_DriverSettlementRun_Generated DEFAULT SYSUTCDATETIME(),
    GeneratedBy INT NULL,
    ReviewedBy INT NULL, ReviewedAtUtc DATETIME2 NULL,
    ApprovedBy INT NULL, ApprovedAtUtc DATETIME2 NULL,
    ExportedBy INT NULL, ExportedAtUtc DATETIME2 NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_DriverSettlementRun_Period CHECK (PeriodEnd >= PeriodStart)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DriverSettlementRun_PublicId' AND object_id = OBJECT_ID('dbo.DriverSettlementRun')) CREATE INDEX IX_DriverSettlementRun_PublicId ON dbo.DriverSettlementRun(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_DriverSettlementRun_Number' AND object_id = OBJECT_ID('dbo.DriverSettlementRun')) CREATE UNIQUE INDEX UQ_DriverSettlementRun_Number ON dbo.DriverSettlementRun(TenantId, Number);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DriverSettlementRun_Status' AND object_id = OBJECT_ID('dbo.DriverSettlementRun')) CREATE INDEX IX_DriverSettlementRun_Status ON dbo.DriverSettlementRun(TenantId, StatusCodeId);
GO
IF OBJECT_ID(N'dbo.DriverSettlementLine', N'U') IS NULL CREATE TABLE dbo.DriverSettlementLine (
    DriverSettlementLineId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    DriverSettlementRunId INT NOT NULL REFERENCES dbo.DriverSettlementRun(DriverSettlementRunId),
    DriverId INT NOT NULL REFERENCES dbo.Driver(DriverId),
    Kind NVARCHAR(20) NOT NULL,
    ServiceDate DATE NOT NULL,
    TransportOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    DriverTripId INT NULL REFERENCES dbo.DriverTrip(DriverTripId),
    Description NVARCHAR(250) NOT NULL,
    AttemptCount INT NOT NULL CONSTRAINT DF_DriverSettlementLine_Attempts DEFAULT 0,
    BreakdownJson NVARCHAR(2000) NULL,
    RateMissing BIT NOT NULL CONSTRAINT DF_DriverSettlementLine_RateMissing DEFAULT 0,
    CalculatedAmount DECIMAL(18,4) NOT NULL,
    Amount DECIMAL(18,4) NOT NULL,
    AdjustmentNote NVARCHAR(250) NULL,
    AdjustedBy INT NULL,
    AdjustedAtUtc DATETIME2 NULL,
    CONSTRAINT CK_DriverSettlementLine_Kind CHECK (Kind IN ('DELIVERY','TRIP'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DriverSettlementLine_Run' AND object_id = OBJECT_ID('dbo.DriverSettlementLine')) CREATE INDEX IX_DriverSettlementLine_Run ON dbo.DriverSettlementLine(DriverSettlementRunId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DeliveryAttempt_SettledRun')
    ALTER TABLE dbo.DeliveryAttempt ADD CONSTRAINT FK_DeliveryAttempt_SettledRun FOREIGN KEY (SettledRunId) REFERENCES dbo.DriverSettlementRun(DriverSettlementRunId);
GO
IF COL_LENGTH('dbo.DriverTrip', 'SettlementRunId') IS NULL
    ALTER TABLE dbo.DriverTrip ADD SettlementRunId INT NULL CONSTRAINT FK_DriverTrip_SettlementRun REFERENCES dbo.DriverSettlementRun(DriverSettlementRunId);
GO

-- Dominio de estatus DriverSettlementStatus y sus estatus
MERGE dbo.CatalogDomain AS t
USING (SELECT N'DriverSettlementStatus' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'DriverSettlementStatus', 2, N'{"es":"Estatus de la liquidación a choferes","en":"Driver settlement status"}', 1, 1);
GO
MERGE dbo.StatusCode AS t
USING (
    SELECT v.Code, v.LabelJson, v.Color, v.Srt, v.Init, k.LookupCodeId AS StageKindLookupId
    FROM (VALUES
        (N'GENERATED', N'{"es":"Generada","en":"Generated"}',  '#9CA3AF', 1, 1, 'PIPELINE'),
        (N'REVIEWED',  N'{"es":"Revisada","en":"Reviewed"}',   '#F59E0B', 2, 0, 'PIPELINE'),
        (N'APPROVED',  N'{"es":"Aprobada","en":"Approved"}',   '#3B82F6', 3, 0, 'PIPELINE'),
        (N'EXPORTED',  N'{"es":"Exportada","en":"Exported"}',  '#059669', 4, 0, 'TERMINAL'),
        (N'CANCELLED', N'{"es":"Cancelada","en":"Cancelled"}', '#6B7280', 5, 0, 'TERMINAL')
    ) AS v (Code, LabelJson, Color, Srt, Init, Kind)
    JOIN dbo.LookupCode k ON k.Entity = N'StageKind' AND k.InternalCode = v.Kind
) AS s
ON t.Entity = N'DriverSettlementStatus' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (N'DriverSettlementStatus', s.Code, s.LabelJson, s.Color, s.Srt, s.StageKindLookupId, s.Init, 1);
GO

-- EntityType de la bitácora y del historial de estatus
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'DRIVER_SETTLEMENT', N'{"es":"Liquidación a choferes","en":"Driver settlement"}', 170)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Contador de liquidaciones: el CHECK de los tipos de contador se vuelve a crear con DRIVERSETTLE
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND definition NOT LIKE '%DRIVERSETTLE%')
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind')
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE','CREDITNOTE','SALESRETURN','CODREMIT','DRIVERSETTLE'));
GO

-- Permisos driverpay.approve y driverpay.export (al administrador de la compañía, plantilla y clonados)
MERGE dbo.Permission AS t
USING (VALUES
    ('driverpay.approve', N'{"es":"Aprobar liquidaciones a choferes","en":"Approve driver settlements"}'),
    ('driverpay.export',  N'{"es":"Exportar liquidaciones a choferes","en":"Export driver settlements"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'FLEET'), s.LabelJson, 1);
GO
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code IN ('driverpay.approve', 'driverpay.export') AND r.Name = 'TenantAdmin'
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);
GO

-- =====================================================================================================================
-- 2026-10-17 (af) Lote 97 (P10) — número de conduce (waybill number)
-- TransportOrder.WaybillNumber: el identificador ÚNICO de cada orden de entrega en la compañía (CND-#######), que usan el cliente y el consignatario. Lo genera Teikem al crear la orden (contador WAYBILL por
-- compañía). Las órdenes que ya existen lo reciben aquí, en orden de creación (por compañía), y el contador queda en el siguiente. Índice único entre las órdenes activas. Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF COL_LENGTH('dbo.TransportOrder', 'WaybillNumber') IS NULL
    ALTER TABLE dbo.TransportOrder ADD WaybillNumber NVARCHAR(40) NULL;
GO
-- el contador: el CHECK de los tipos de contador se vuelve a crear con WAYBILL
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND definition NOT LIKE '%WAYBILL%')
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind')
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE','CREDITNOTE','SALESRETURN','CODREMIT','DRIVERSETTLE','WAYBILL'));
GO
-- las órdenes existentes (incluidas las dadas de baja: su número no se reutiliza) reciben su conduce en orden de creación
IF EXISTS (SELECT 1 FROM dbo.TransportOrder WHERE WaybillNumber IS NULL)
BEGIN
    ;WITH base AS (
        SELECT o.TransportOrderId,
               COALESCE((SELECT MAX(TRY_CAST(SUBSTRING(x.WaybillNumber, 5, 20) AS BIGINT)) FROM dbo.TransportOrder x WHERE x.TenantId = o.TenantId AND x.WaybillNumber LIKE 'CND-%'), 0) AS MaxDone,
               ROW_NUMBER() OVER (PARTITION BY o.TenantId ORDER BY o.TransportOrderId) AS Rn
        FROM dbo.TransportOrder o WHERE o.WaybillNumber IS NULL
    )
    UPDATE o SET WaybillNumber = 'CND-' + RIGHT('0000000' + CAST(b.MaxDone + b.Rn AS VARCHAR(20)), 7)
    FROM dbo.TransportOrder o JOIN base b ON b.TransportOrderId = o.TransportOrderId;
END
GO
-- el contador de cada compañía queda en el siguiente número libre
MERGE dbo.NumberSequence AS t
USING (SELECT TenantId, MAX(TRY_CAST(SUBSTRING(WaybillNumber, 5, 20) AS BIGINT)) + 1 AS NextValue FROM dbo.TransportOrder WHERE WaybillNumber LIKE 'CND-%' GROUP BY TenantId) AS s
ON t.TenantId = s.TenantId AND t.Kind = 'WAYBILL' AND t.ClientId IS NULL
WHEN MATCHED AND t.NextValue < s.NextValue THEN UPDATE SET NextValue = s.NextValue
WHEN NOT MATCHED THEN INSERT (TenantId, Kind, ClientId, NextValue) VALUES (s.TenantId, 'WAYBILL', NULL, s.NextValue);
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.TransportOrder') AND name = 'WaybillNumber' AND is_nullable = 1)
    ALTER TABLE dbo.TransportOrder ALTER COLUMN WaybillNumber NVARCHAR(40) NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Order_Waybill' AND object_id = OBJECT_ID('dbo.TransportOrder'))
    CREATE UNIQUE INDEX UX_Order_Waybill ON dbo.TransportOrder(TenantId, WaybillNumber) WHERE IsActive = 1;
GO

-- =====================================================================================================================
-- 2026-10-17 (ag) Lote 98 (P10) — avisos al cliente
-- ClientNotificationSetting (qué avisos y por qué canal los quiere cada cliente: lo decide la compañía; sin fila = apagado), ClientNotificationEvent (los hechos que se avisan, solo de clientes con el aviso encendido),
-- ClientNotification (el resumen que se manda y que ve el portal del cliente) y ClientNotificationDelivery (cada envío por canal y destinatario, con su resultado y reintentos). EntityType de la configuración para la
-- bitácora. Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF OBJECT_ID(N'dbo.ClientNotificationSetting', N'U') IS NULL CREATE TABLE dbo.ClientNotificationSetting (
    ClientNotificationSettingId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId INT NOT NULL REFERENCES dbo.Client(ClientId),
    Kind NVARCHAR(30) NOT NULL,
    EmailEnabled BIT NOT NULL CONSTRAINT DF_ClientNotificationSetting_Email DEFAULT 0,
    AppEnabled BIT NOT NULL CONSTRAINT DF_ClientNotificationSetting_App DEFAULT 0,
    SmsEnabled BIT NOT NULL CONSTRAINT DF_ClientNotificationSetting_Sms DEFAULT 0,
    ExtraEmails NVARCHAR(400) NULL,
    ExtraPhones NVARCHAR(400) NULL,
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ClientNotificationSetting_Updated DEFAULT SYSUTCDATETIME(),
    UpdatedBy INT NULL,
    CONSTRAINT CK_ClientNotificationSetting_Kind CHECK (Kind IN ('PICKED_UP','OUT_FOR_DELIVERY','DELIVERED','COD_PROCESSED','INVOICE_ISSUED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ClientNotificationSetting' AND object_id = OBJECT_ID('dbo.ClientNotificationSetting')) CREATE UNIQUE INDEX UQ_ClientNotificationSetting ON dbo.ClientNotificationSetting(TenantId, ClientId, Kind);
GO
IF OBJECT_ID(N'dbo.ClientNotification', N'U') IS NULL CREATE TABLE dbo.ClientNotification (
    ClientNotificationId BIGINT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_ClientNotification_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId INT NOT NULL REFERENCES dbo.Client(ClientId),
    Kind NVARCHAR(30) NOT NULL,
    Subject NVARCHAR(200) NOT NULL,
    BodyText NVARCHAR(2000) NOT NULL,
    SummaryJson NVARCHAR(1000) NULL,
    InPortal BIT NOT NULL CONSTRAINT DF_ClientNotification_InPortal DEFAULT 0,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ClientNotification_Created DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientNotification_Client' AND object_id = OBJECT_ID('dbo.ClientNotification')) CREATE INDEX IX_ClientNotification_Client ON dbo.ClientNotification(TenantId, ClientId, CreatedAtUtc DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientNotification_PublicId' AND object_id = OBJECT_ID('dbo.ClientNotification')) CREATE UNIQUE INDEX IX_ClientNotification_PublicId ON dbo.ClientNotification(PublicId);
GO
IF OBJECT_ID(N'dbo.ClientNotificationEvent', N'U') IS NULL CREATE TABLE dbo.ClientNotificationEvent (
    ClientNotificationEventId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId INT NOT NULL REFERENCES dbo.Client(ClientId),
    Kind NVARCHAR(30) NOT NULL,
    TransportOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    TripId INT NULL REFERENCES dbo.Trip(TripId),
    RefId INT NULL,
    Pieces INT NOT NULL CONSTRAINT DF_ClientNotificationEvent_Pieces DEFAULT 0,
    OccurredAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ClientNotificationEvent_Occurred DEFAULT SYSUTCDATETIME(),
    NotificationId BIGINT NULL REFERENCES dbo.ClientNotification(ClientNotificationId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientNotificationEvent_Pending' AND object_id = OBJECT_ID('dbo.ClientNotificationEvent')) CREATE INDEX IX_ClientNotificationEvent_Pending ON dbo.ClientNotificationEvent(TenantId, ClientId, Kind) WHERE NotificationId IS NULL;
GO
IF OBJECT_ID(N'dbo.ClientNotificationDelivery', N'U') IS NULL CREATE TABLE dbo.ClientNotificationDelivery (
    ClientNotificationDeliveryId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientNotificationId BIGINT NOT NULL REFERENCES dbo.ClientNotification(ClientNotificationId),
    Channel NVARCHAR(10) NOT NULL,
    Recipient NVARCHAR(200) NOT NULL,
    Status NVARCHAR(10) NOT NULL,
    Message NVARCHAR(400) NULL,
    Attempts INT NOT NULL CONSTRAINT DF_ClientNotificationDelivery_Attempts DEFAULT 0,
    NextAttemptUtc DATETIME2 NULL,
    SentAtUtc DATETIME2 NULL,
    CONSTRAINT CK_ClientNotificationDelivery_Status CHECK (Status IN ('PENDING','SENT','SKIPPED','FAILED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientNotificationDelivery_Notification' AND object_id = OBJECT_ID('dbo.ClientNotificationDelivery')) CREATE INDEX IX_ClientNotificationDelivery_Notification ON dbo.ClientNotificationDelivery(ClientNotificationId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientNotificationDelivery_Pending' AND object_id = OBJECT_ID('dbo.ClientNotificationDelivery')) CREATE INDEX IX_ClientNotificationDelivery_Pending ON dbo.ClientNotificationDelivery(TenantId, NextAttemptUtc) WHERE Status = 'PENDING';
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'CLIENT_NOTIFICATION_SETTING', N'{"es":"Avisos del cliente","en":"Client notifications"}', 180)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO


-- =====================================================================================================================
-- (ah) 2026-10-17 — Lote 99: aceptación del documento legal único («Política de privacidad y términos de uso»). Tabla global por usuario (sin TenantId):
-- quién aceptó qué versión, cuándo y desde dónde. Al cambiar la versión vigente en el código, todos vuelven a aceptar; los usuarios existentes (sin filas) aceptan en su siguiente ingreso.
-- =====================================================================================================================
IF OBJECT_ID(N'dbo.LegalAcceptance', N'U') IS NULL CREATE TABLE dbo.LegalAcceptance (
    LegalAcceptanceId BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    Version NVARCHAR(20) NOT NULL,
    Lang NVARCHAR(5) NOT NULL CONSTRAINT DF_LegalAcceptance_Lang DEFAULT N'es',
    IpAddress NVARCHAR(64) NULL,
    UserAgent NVARCHAR(400) NULL,
    AcceptedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_LegalAcceptance_Accepted DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LegalAcceptance_User' AND object_id = OBJECT_ID('dbo.LegalAcceptance')) CREATE INDEX IX_LegalAcceptance_User ON dbo.LegalAcceptance(UserId, Version);
GO

-- =====================================================================================================================
-- (ai) 2026-10-17 — Lote 100: bloqueo de cuenta con espera que crece (1, 5, 15 y 60 min). LockoutLevel = cuántos bloqueos seguidos lleva la cuenta;
-- LockoutLevelAtUtc = cuándo fue el último (pasadas 24 horas sin otro bloqueo se empieza de nuevo).
-- =====================================================================================================================
IF COL_LENGTH('dbo.AspNetUsers', 'LockoutLevel') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD LockoutLevel INT NOT NULL CONSTRAINT DF_AspNetUsers_LockoutLevel DEFAULT 0;
GO
IF COL_LENGTH('dbo.AspNetUsers', 'LockoutLevelAtUtc') IS NULL
    ALTER TABLE dbo.AspNetUsers ADD LockoutLevelAtUtc DATETIME2 NULL;
GO

-- =====================================================================================================================
-- (aj) 2026-10-17 — Lote 100: computadoras de confianza. Una computadora que no se reconoce se confirma por correo y queda de confianza 30 días sin uso. Solo el resumen (SHA-256)
-- de la marca secreta del navegador y del enlace del correo; tabla global por usuario (sin TenantId).
-- =====================================================================================================================
IF OBJECT_ID(N'dbo.TrustedBrowser', N'U') IS NULL CREATE TABLE dbo.TrustedBrowser (
    TrustedBrowserId BIGINT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_TrustedBrowser_PublicId DEFAULT NEWID(),
    UserId INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    TokenHash NVARCHAR(64) NOT NULL,
    ConfirmHash NVARCHAR(64) NULL,
    LinkExpiresAtUtc DATETIME2 NULL,
    EmailSentAtUtc DATETIME2 NULL,
    Label NVARCHAR(80) NOT NULL CONSTRAINT DF_TrustedBrowser_Label DEFAULT N'',
    IpAddress NVARCHAR(64) NULL,
    UserAgent NVARCHAR(400) NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_TrustedBrowser_Created DEFAULT SYSUTCDATETIME(),
    ConfirmedAtUtc DATETIME2 NULL,
    LastUsedAtUtc DATETIME2 NULL,
    ExpiresAtUtc DATETIME2 NULL,
    RevokedAtUtc DATETIME2 NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TrustedBrowser_UserToken' AND object_id = OBJECT_ID('dbo.TrustedBrowser')) CREATE UNIQUE INDEX UX_TrustedBrowser_UserToken ON dbo.TrustedBrowser(UserId, TokenHash);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TrustedBrowser_PublicId' AND object_id = OBJECT_ID('dbo.TrustedBrowser')) CREATE UNIQUE INDEX UX_TrustedBrowser_PublicId ON dbo.TrustedBrowser(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TrustedBrowser_Confirm' AND object_id = OBJECT_ID('dbo.TrustedBrowser')) CREATE INDEX IX_TrustedBrowser_Confirm ON dbo.TrustedBrowser(ConfirmHash) WHERE ConfirmHash IS NOT NULL;
GO

-- =====================================================================================================================
-- (ak) 2026-10-17 — Lote 101: acceso del portal por cliente. RefreshToken.PortalClientId = el cliente con el que entró un usuario de portal (se conserva al renovar la sesión);
-- TrustedBrowser.MfaVerifiedAtUtc = cuándo se escribió el código del segundo paso desde esa computadora (los portales no lo repiten por 30 días); TrustedBrowser.SingleUse = «No confiar en esta computadora» (un solo ingreso).
-- =====================================================================================================================
IF COL_LENGTH('dbo.RefreshToken', 'PortalClientId') IS NULL
    ALTER TABLE dbo.RefreshToken ADD PortalClientId INT NULL;
GO
IF COL_LENGTH('dbo.TrustedBrowser', 'MfaVerifiedAtUtc') IS NULL
    ALTER TABLE dbo.TrustedBrowser ADD MfaVerifiedAtUtc DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.TrustedBrowser', 'SingleUse') IS NULL
    ALTER TABLE dbo.TrustedBrowser ADD SingleUse BIT NOT NULL CONSTRAINT DF_TrustedBrowser_SingleUse DEFAULT 0;
GO

-- =====================================================================================================================
-- (al) 2026-10-17 — Lote 103: seguimiento público «¿Cómo va mi orden?». Tenant.TrackingSlug = identificador corto de la dirección pública de la compañía (el conduce se repite entre compañías, así que la página
-- necesita saber de cuál es); NULL = el seguimiento público está APAGADO. No se enciende solo para ninguna compañía existente: lo enciende su administrador (Ajustes de la compañía).
-- =====================================================================================================================
IF COL_LENGTH('dbo.Tenant', 'TrackingSlug') IS NULL
    ALTER TABLE dbo.Tenant ADD TrackingSlug NVARCHAR(40) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Tenant_TrackingSlug' AND object_id = OBJECT_ID('dbo.Tenant')) CREATE UNIQUE INDEX UX_Tenant_TrackingSlug ON dbo.Tenant(TrackingSlug) WHERE TrackingSlug IS NOT NULL;
GO

-- =====================================================================================================================
-- (am) 2026-10-17 — Lote 104 (portal de clientes): mensajes del portal con el equipo de servicio al cliente.
-- ClientConversation = la conversación de UNA persona de un cliente con el equipo (una por persona, se crea con el primer mensaje); ClientMessage = sus mensajes (nunca se editan ni se borran);
-- ClientServiceTeamMember = el equipo de cada cliente (sin filas = lo atiende quien tenga clientmessages.reply). Permisos clientmessages.reply y clientmessages.manage (al administrador de la compañía:
-- plantilla y clonados). Solo AGREGA. Idempotente.
-- =====================================================================================================================
IF OBJECT_ID(N'dbo.ClientConversation', N'U') IS NULL CREATE TABLE dbo.ClientConversation (
    ClientConversationId BIGINT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_ClientConversation_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId INT NOT NULL REFERENCES dbo.Client(ClientId),
    PortalUserId INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    StartedBy VARCHAR(10) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ClientConversation_Created DEFAULT SYSUTCDATETIME(),
    LastMessageId BIGINT NOT NULL CONSTRAINT DF_ClientConversation_LastId DEFAULT 0,
    LastMessageAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ClientConversation_LastAt DEFAULT SYSUTCDATETIME(),
    LastMessagePreview NVARCHAR(200) NULL,
    LastSenderKind VARCHAR(10) NULL,
    TeamLastReadMessageId BIGINT NOT NULL CONSTRAINT DF_ClientConversation_TeamRead DEFAULT 0,
    ClientLastReadMessageId BIGINT NOT NULL CONSTRAINT DF_ClientConversation_ClientRead DEFAULT 0,
    CONSTRAINT CK_ClientConversation_StartedBy CHECK (StartedBy IN ('CLIENT','STAFF'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ClientConversation_PublicId' AND object_id = OBJECT_ID('dbo.ClientConversation')) CREATE UNIQUE INDEX UQ_ClientConversation_PublicId ON dbo.ClientConversation(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ClientConversation_Person' AND object_id = OBJECT_ID('dbo.ClientConversation')) CREATE UNIQUE INDEX UX_ClientConversation_Person ON dbo.ClientConversation(TenantId, ClientId, PortalUserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientConversation_Last' AND object_id = OBJECT_ID('dbo.ClientConversation')) CREATE INDEX IX_ClientConversation_Last ON dbo.ClientConversation(TenantId, LastMessageAtUtc DESC);
GO
IF OBJECT_ID(N'dbo.ClientMessage', N'U') IS NULL CREATE TABLE dbo.ClientMessage (
    ClientMessageId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientConversationId BIGINT NOT NULL REFERENCES dbo.ClientConversation(ClientConversationId),
    SenderKind VARCHAR(10) NOT NULL,
    SenderUserId INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    Body NVARCHAR(2000) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ClientMessage_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_ClientMessage_Sender CHECK (SenderKind IN ('CLIENT','STAFF'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientMessage_Conversation' AND object_id = OBJECT_ID('dbo.ClientMessage')) CREATE INDEX IX_ClientMessage_Conversation ON dbo.ClientMessage(ClientConversationId, ClientMessageId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientMessage_Sender' AND object_id = OBJECT_ID('dbo.ClientMessage')) CREATE INDEX IX_ClientMessage_Sender ON dbo.ClientMessage(SenderUserId, CreatedAtUtc);
GO
IF OBJECT_ID(N'dbo.ClientServiceTeamMember', N'U') IS NULL CREATE TABLE dbo.ClientServiceTeamMember (
    ClientServiceTeamMemberId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId INT NOT NULL REFERENCES dbo.Client(ClientId),
    UserId INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    AddedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ClientServiceTeamMember_Added DEFAULT SYSUTCDATETIME(),
    AddedBy INT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ClientServiceTeamMember' AND object_id = OBJECT_ID('dbo.ClientServiceTeamMember')) CREATE UNIQUE INDEX UX_ClientServiceTeamMember ON dbo.ClientServiceTeamMember(TenantId, ClientId, UserId);
GO
MERGE dbo.Permission AS t
USING (VALUES
    ('clientmessages.reply',  N'{"es":"Atender los mensajes de los clientes","en":"Handle client messages"}'),
    ('clientmessages.manage', N'{"es":"Designar el equipo de servicio al cliente","en":"Manage the customer service team"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'CLIENTS'), s.LabelJson, 1);
GO
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code IN ('clientmessages.reply', 'clientmessages.manage') AND r.Name = 'TenantAdmin'
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);
GO

/* =========================================================================
   2026-10-17 (an) — Lote 106: nuevo flujo de estatus de la orden de transporte (dictado por Luis; el del seed original era solo un ejemplo).
   Sometida → Recogida → Sin asignar → Asignada → En tránsito → Entregada (terminal); Intento fallido (lateral); Cancelada y En almacén (terminales).
   Se renombra EN SITIO (conserva ids e historial): DRAFT→SUBMITTED, INBOUND→UNASSIGNED, PLANNED→ASSIGNED; se BORRAN CONFIRMED, ARRIVED, ON_HOLD y PARTIAL (nada en producción); FAILED pasa a «Intento fallido». Idempotente. Sin órdenes en producción que remapear.
   ========================================================================= */
DECLARE @PIPE INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity='StageKind' AND InternalCode='PIPELINE');
DECLARE @TERM INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity='StageKind' AND InternalCode='TERMINAL');

UPDATE dbo.StatusCode SET InternalCode='SUBMITTED', LabelJson=N'{"es":"Sometida","en":"Submitted"}', SortOrder=1, ColorHex='#9CA3AF', IsInitial=1, IsActive=1
WHERE Entity='OrderStatus' AND InternalCode='DRAFT' AND NOT EXISTS (SELECT 1 FROM dbo.StatusCode x WHERE x.Entity='OrderStatus' AND x.InternalCode='SUBMITTED');
UPDATE dbo.StatusCode SET InternalCode='UNASSIGNED', LabelJson=N'{"es":"Sin asignar","en":"Unassigned"}', SortOrder=3, ColorHex='#8B5CF6'
WHERE Entity='OrderStatus' AND InternalCode='INBOUND' AND NOT EXISTS (SELECT 1 FROM dbo.StatusCode x WHERE x.Entity='OrderStatus' AND x.InternalCode='UNASSIGNED');
UPDATE dbo.StatusCode SET InternalCode='ASSIGNED', LabelJson=N'{"es":"Asignada","en":"Assigned"}', SortOrder=4, ColorHex='#0EA5E9'
WHERE Entity='OrderStatus' AND InternalCode='PLANNED' AND NOT EXISTS (SELECT 1 FROM dbo.StatusCode x WHERE x.Entity='OrderStatus' AND x.InternalCode='ASSIGNED');

UPDATE dbo.StatusCode SET LabelJson=N'{"es":"Sometida","en":"Submitted"}', SortOrder=1, ColorHex='#9CA3AF', IsInitial=1, IsActive=1, StageKindLookupId=@PIPE WHERE Entity='OrderStatus' AND InternalCode='SUBMITTED';
UPDATE dbo.StatusCode SET LabelJson=N'{"es":"Recogida","en":"Picked up"}', SortOrder=2 WHERE Entity='OrderStatus' AND InternalCode='PICKUP';
UPDATE dbo.StatusCode SET LabelJson=N'{"es":"Sin asignar","en":"Unassigned"}', SortOrder=3, IsActive=1 WHERE Entity='OrderStatus' AND InternalCode='UNASSIGNED';
UPDATE dbo.StatusCode SET LabelJson=N'{"es":"Asignada","en":"Assigned"}', SortOrder=4, IsActive=1 WHERE Entity='OrderStatus' AND InternalCode='ASSIGNED';
UPDATE dbo.StatusCode SET SortOrder=5 WHERE Entity='OrderStatus' AND InternalCode='IN_TRANSIT';
UPDATE dbo.StatusCode SET LabelJson=N'{"es":"Intento fallido","en":"Failed attempt"}', ColorHex='#EF4444', SortOrder=22 WHERE Entity='OrderStatus' AND InternalCode='FAILED';

IF NOT EXISTS (SELECT 1 FROM dbo.StatusCode WHERE Entity='OrderStatus' AND InternalCode='IN_WAREHOUSE')
    INSERT INTO dbo.StatusCode (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES ('OrderStatus','IN_WAREHOUSE', N'{"es":"En almacén","en":"In warehouse"}', '#14B8A6', 9, @TERM, 0, 1);

/* Se BORRAN los cuatro estatus que ya no existen (Luis, 2026-10-17: «bórralo, no hay nada en producción»). Antes se remapea lo que pudiera referenciarlos en bases de desarrollo:
   CONFIRMED→PICKUP, ARRIVED→IN_TRANSIT, ON_HOLD y PARTIAL→FAILED; y se quitan sus filas de capacidades, entradas laterales y ajustes por compañía. Idempotente. */
DECLARE @old TABLE (StatusCodeId INT PRIMARY KEY, NewId INT NULL);
INSERT INTO @old (StatusCodeId, NewId)
SELECT s.StatusCodeId,
       (SELECT TOP 1 n.StatusCodeId FROM dbo.StatusCode n WHERE n.Entity='OrderStatus' AND n.InternalCode =
            CASE s.InternalCode WHEN 'CONFIRMED' THEN 'PICKUP' WHEN 'ARRIVED' THEN 'IN_TRANSIT' ELSE 'FAILED' END)
FROM dbo.StatusCode s WHERE s.Entity='OrderStatus' AND s.InternalCode IN ('CONFIRMED','ARRIVED','ON_HOLD','PARTIAL');

UPDATE o SET o.StatusCodeId = x.NewId FROM dbo.TransportOrder o JOIN @old x ON x.StatusCodeId = o.StatusCodeId;
UPDATE h SET h.FromStatusCodeId = x.NewId FROM dbo.EntityStatusHistory h JOIN @old x ON x.StatusCodeId = h.FromStatusCodeId;
UPDATE h SET h.ToStatusCodeId = x.NewId FROM dbo.EntityStatusHistory h JOIN @old x ON x.StatusCodeId = h.ToStatusCodeId;
DELETE c FROM dbo.StatusCapability c JOIN @old x ON x.StatusCodeId = c.StatusCodeId;
DELETE l FROM dbo.StatusLateralEntry l JOIN @old x ON x.StatusCodeId IN (l.LateralStatusCodeId, l.FromStatusCodeId);
DELETE v FROM dbo.StatusCodeOverride v JOIN @old x ON x.StatusCodeId = v.StatusCodeId;
DELETE s FROM dbo.StatusCode s JOIN @old x ON x.StatusCodeId = s.StatusCodeId;
GO

/* Capacidades por defecto (TenantId NULL): solo se EDITA en Sometida (EDIT_CARGO sin fila de bloqueo = permitido), se recotiza solo en Sometida, y En almacén (terminal) no permite nada. */
DELETE c FROM dbo.StatusCapability c
JOIN dbo.StatusCode s ON s.StatusCodeId = c.StatusCodeId AND s.Entity='OrderStatus' AND s.InternalCode='SUBMITTED'
JOIN dbo.LookupCode cap ON cap.LookupCodeId = c.CapabilityLookupId AND cap.Entity='Capability' AND cap.InternalCode='REPRICE'
JOIN dbo.LookupCode et ON et.LookupCodeId = c.EntityTypeLookupId AND et.Entity='EntityType' AND et.InternalCode='TRANSPORT_ORDER'
WHERE c.TenantId IS NULL;
GO
MERGE dbo.StatusCapability AS t
USING (
    SELECT et.LookupCodeId AS EntityTypeLookupId, s.StatusCodeId, c.LookupCodeId AS CapabilityLookupId
    FROM dbo.LookupCode et
    CROSS JOIN dbo.StatusCode s
    CROSS JOIN dbo.LookupCode c
    WHERE et.Entity='EntityType' AND et.InternalCode='TRANSPORT_ORDER'
      AND s.Entity='OrderStatus' AND s.InternalCode='IN_WAREHOUSE'
      AND c.Entity='Capability' AND c.InternalCode IN ('EDIT_CARGO','REPRICE','CANCEL','ASSIGN_TRIP')
) AS s
ON t.TenantId IS NULL AND t.EntityTypeLookupId = s.EntityTypeLookupId AND t.StatusCodeId = s.StatusCodeId AND t.CapabilityLookupId = s.CapabilityLookupId
WHEN NOT MATCHED THEN
    INSERT (TenantId, EntityTypeLookupId, StatusCodeId, CapabilityLookupId, IsAllowed)
    VALUES (NULL, s.EntityTypeLookupId, s.StatusCodeId, s.CapabilityLookupId, 0);
GO

/* Informes, indicadores y gráficos de órdenes que filtran por los códigos viejos. */
UPDATE dbo.IndicatorDefinition SET FilterJson = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(FilterJson, '"CONFIRMED"', '"SUBMITTED"'), '"DRAFT"', '"SUBMITTED"'), '"INBOUND"', '"UNASSIGNED"'), '"PLANNED"', '"ASSIGNED"'), '"ARRIVED"', '"FAILED"'), '"ON_HOLD"', '"FAILED"')
WHERE DataSourceKey = 'TRANSPORT_ORDER' AND FilterJson IS NOT NULL AND (FilterJson LIKE '%"CONFIRMED"%' OR FilterJson LIKE '%"DRAFT"%' OR FilterJson LIKE '%"INBOUND"%' OR FilterJson LIKE '%"PLANNED"%' OR FilterJson LIKE '%"ARRIVED"%' OR FilterJson LIKE '%"ON_HOLD"%');
UPDATE dbo.ChartDefinition SET FilterJson = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(FilterJson, '"CONFIRMED"', '"SUBMITTED"'), '"DRAFT"', '"SUBMITTED"'), '"INBOUND"', '"UNASSIGNED"'), '"PLANNED"', '"ASSIGNED"'), '"ARRIVED"', '"FAILED"'), '"ON_HOLD"', '"FAILED"')
WHERE DataSourceKey = 'TRANSPORT_ORDER' AND FilterJson IS NOT NULL AND (FilterJson LIKE '%"CONFIRMED"%' OR FilterJson LIKE '%"DRAFT"%' OR FilterJson LIKE '%"INBOUND"%' OR FilterJson LIKE '%"PLANNED"%' OR FilterJson LIKE '%"ARRIVED"%' OR FilterJson LIKE '%"ON_HOLD"%');
UPDATE r SET FilterJson = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(r.FilterJson, '"CONFIRMED"', '"SUBMITTED"'), '"DRAFT"', '"SUBMITTED"'), '"INBOUND"', '"UNASSIGNED"'), '"PLANNED"', '"ASSIGNED"'), '"ARRIVED"', '"FAILED"'), '"ON_HOLD"', '"FAILED"')
FROM dbo.ReportDefinition r
JOIN dbo.LookupCode et ON et.LookupCodeId = r.BaseEntityTypeLookupId AND et.InternalCode = 'TRANSPORT_ORDER'
WHERE r.FilterJson IS NOT NULL AND (r.FilterJson LIKE '%"CONFIRMED"%' OR r.FilterJson LIKE '%"DRAFT"%' OR r.FilterJson LIKE '%"INBOUND"%' OR r.FilterJson LIKE '%"PLANNED"%' OR r.FilterJson LIKE '%"ARRIVED"%' OR r.FilterJson LIKE '%"ON_HOLD"%');
GO

/* =========================================================================
   2026-10-17 (ao) — Lote 106: permiso orders.reopen (reabrir una orden en estatus final: Entregada, Cancelada, En almacén…). Se propaga a los roles TenantAdmin ya clonados (solo agregar).
   ========================================================================= */
MERGE dbo.Permission AS t
USING (VALUES
    ('orders.reopen', N'{"es":"Reabrir órdenes en estatus final","en":"Reopen orders in a final status"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ORDERS'), s.LabelJson, 1);
GO
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'orders.reopen' AND r.Name = 'TenantAdmin'
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);
GO

/* =========================================================================
   2026-10-17 (ap) — Lote 107: estatus «brincado». Un escaneo que lleva la orden más adelante registra los estatus del medio con la misma fecha y hora y esta marca.
   ========================================================================= */
IF COL_LENGTH('dbo.EntityStatusHistory', 'IsSkipped') IS NULL
    ALTER TABLE dbo.EntityStatusHistory ADD IsSkipped BIT NOT NULL CONSTRAINT DF_EntityStatusHistory_IsSkipped DEFAULT 0;
GO

/* =========================================================================
   2026-10-18 (aq) — Lote 109: piezas. Cada pieza (paquete) de una orden tiene su propio número único (el del label); se crean al someter la orden.
   ========================================================================= */
IF OBJECT_ID(N'dbo.OrderPiece', N'U') IS NULL CREATE TABLE dbo.OrderPiece (
    OrderPieceId     INT IDENTITY(1,1) PRIMARY KEY,
    TenantId         INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    CargoLineId      INT NOT NULL REFERENCES dbo.CargoLine(CargoLineId),
    Sequence         INT NOT NULL,
    PackageNumber    NVARCHAR(40) NOT NULL,
    IsActive         BIT NOT NULL CONSTRAINT DF_OrderPiece_IsActive DEFAULT 1,
    CreatedAtUtc     DATETIME2 NOT NULL CONSTRAINT DF_OrderPiece_Created DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrderPiece_Order' AND object_id = OBJECT_ID('dbo.OrderPiece'))
    CREATE INDEX IX_OrderPiece_Order ON dbo.OrderPiece (TransportOrderId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_OrderPiece_Number' AND object_id = OBJECT_ID('dbo.OrderPiece'))
    CREATE UNIQUE INDEX UX_OrderPiece_Number ON dbo.OrderPiece (TenantId, PackageNumber) WHERE IsActive = 1;
GO

/* =========================================================================
   2026-10-18 (ar) — Lote 110: escaneo por pieza. Cada pieza se registra por acción (PICKUP, ARRIVE, ROUTE, DEPART) y una pieza que no se escaneó queda faltante «Por verificar».
   ========================================================================= */
IF COL_LENGTH('dbo.OrderPiece', 'MissingStatus') IS NULL ALTER TABLE dbo.OrderPiece ADD MissingStatus VARCHAR(20) NULL;
GO
IF COL_LENGTH('dbo.OrderPiece', 'MissingAtUtc') IS NULL ALTER TABLE dbo.OrderPiece ADD MissingAtUtc DATETIME2 NULL;
GO
IF OBJECT_ID(N'dbo.OrderPieceScan', N'U') IS NULL CREATE TABLE dbo.OrderPieceScan (
    OrderPieceScanId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId         INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    OrderPieceId     INT NOT NULL REFERENCES dbo.OrderPiece(OrderPieceId),
    Action           VARCHAR(10) NOT NULL,
    ScannedAtUtc     DATETIME2 NOT NULL CONSTRAINT DF_OrderPieceScan_At DEFAULT SYSUTCDATETIME(),
    ScannedBy        INT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_OrderPieceScan_Piece_Action' AND object_id = OBJECT_ID('dbo.OrderPieceScan'))
    CREATE UNIQUE INDEX UX_OrderPieceScan_Piece_Action ON dbo.OrderPieceScan (OrderPieceId, Action);
GO

/* =========================================================================
   2026-10-18 (as) — Lote 111: reclamaciones. Las piezas faltantes pasan Por verificar → Perdido → Reclamada → Resuelta; la reclamación (REC-#####) junta las piezas perdidas de una orden, con su valor declarado y su resolución.
   Dominio de estatus ClaimStatus (CLAIMED inicial → RESOLVED terminal), EntityType CLAIM, permisos claims.view y claims.manage (propagados a los TenantAdmin ya clonados).
   ========================================================================= */
IF OBJECT_ID(N'dbo.Claim', N'U') IS NULL CREATE TABLE dbo.Claim (
    ClaimId          INT IDENTITY(1,1) PRIMARY KEY,
    PublicId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Claim_PublicId DEFAULT NEWID(),
    TenantId         INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Number           NVARCHAR(30) NOT NULL,
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    ClientId         INT NOT NULL REFERENCES dbo.Client(ClientId),
    StatusCodeId     INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    Description      NVARCHAR(500) NOT NULL,
    DeclaredValue    DECIMAL(14,2) NULL,
    Notes            NVARCHAR(1000) NULL,
    Resolution       VARCHAR(10) NULL,
    AmountPaid       DECIMAL(14,2) NULL,
    ResolutionNotes  NVARCHAR(1000) NULL,
    IsActive         BIT NOT NULL CONSTRAINT DF_Claim_IsActive DEFAULT 1,
    CreatedAtUtc     DATETIME2 NOT NULL CONSTRAINT DF_Claim_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy        INT NULL,
    ResolvedAtUtc    DATETIME2 NULL,
    ResolvedBy       INT NULL,
    RowVersion       ROWVERSION,
    CONSTRAINT CK_Claim_Resolution CHECK (Resolution IS NULL OR Resolution IN ('PAID','REJECTED','CLOSED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Claim_Number' AND object_id = OBJECT_ID('dbo.Claim'))
    CREATE UNIQUE INDEX UX_Claim_Number ON dbo.Claim (TenantId, Number);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Claim_Order' AND object_id = OBJECT_ID('dbo.Claim'))
    CREATE INDEX IX_Claim_Order ON dbo.Claim (TransportOrderId);
GO
IF COL_LENGTH('dbo.OrderPiece', 'ClaimId') IS NULL ALTER TABLE dbo.OrderPiece ADD ClaimId INT NULL REFERENCES dbo.Claim(ClaimId);
GO
IF COL_LENGTH('dbo.OrderPiece', 'ReappearedAtUtc') IS NULL ALTER TABLE dbo.OrderPiece ADD ReappearedAtUtc DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.OrderPiece', 'ReappearedAction') IS NULL ALTER TABLE dbo.OrderPiece ADD ReappearedAction VARCHAR(10) NULL;
GO

MERGE dbo.CatalogDomain AS t
USING (SELECT N'ClaimStatus' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'ClaimStatus', 2, N'{"es":"Estatus de la reclamación","en":"Claim status"}', 1, 1);
GO
MERGE dbo.StatusCode AS t
USING (
    SELECT v.Code, v.LabelJson, v.Color, v.Srt, v.Init, k.LookupCodeId AS StageKindLookupId
    FROM (VALUES
        (N'CLAIMED',  N'{"es":"Reclamada","en":"Claimed"}',  '#F59E0B', 1, 1, 'PIPELINE'),
        (N'RESOLVED', N'{"es":"Resuelta","en":"Resolved"}',  '#10B981', 2, 0, 'TERMINAL')
    ) AS v (Code, LabelJson, Color, Srt, Init, Kind)
    JOIN dbo.LookupCode k ON k.Entity = N'StageKind' AND k.InternalCode = v.Kind
) AS s
ON t.Entity = N'ClaimStatus' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (N'ClaimStatus', s.Code, s.LabelJson, s.Color, s.Srt, s.StageKindLookupId, s.Init, 1);
GO
MERGE dbo.LookupCode AS t
USING (VALUES (N'CLAIM', N'{"es":"Reclamación","en":"Claim"}', 130)) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO
MERGE dbo.Permission AS t
USING (VALUES
    ('claims.view',   N'{"es":"Ver los faltantes y las reclamaciones","en":"View missing pieces and claims"}'),
    ('claims.manage', N'{"es":"Marcar piezas como perdidas, reclamar y resolver reclamaciones","en":"Mark pieces as lost, claim and resolve claims"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ORDERS'), s.LabelJson, 1);
GO
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code IN ('claims.view', 'claims.manage') AND r.Name = 'TenantAdmin'
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);
GO

/* =========================================================================
   2026-10-18 (at) — Lote 112: Devolución. Estatus final OrderStatus «Devuelta» (RETURNED), permiso orders.return (propagado a los TenantAdmin ya clonados) y TransportOrder.ReturnOfOrderId (la orden de vuelta apunta a la que devolvió).
   ========================================================================= */
IF NOT EXISTS (SELECT 1 FROM dbo.StatusCode WHERE Entity = N'OrderStatus' AND InternalCode = N'RETURNED')
    INSERT INTO dbo.StatusCode (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (N'OrderStatus', N'RETURNED', N'{"es":"Devuelta","en":"Returned"}', '#F97316', 10, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'StageKind' AND InternalCode = 'TERMINAL'), 0, 1);
GO
MERGE dbo.StatusCapability AS t
USING (
    SELECT et.LookupCodeId AS EntityTypeLookupId, s.StatusCodeId, c.LookupCodeId AS CapabilityLookupId
    FROM dbo.LookupCode et
    CROSS JOIN dbo.StatusCode s
    CROSS JOIN dbo.LookupCode c
    WHERE et.Entity = 'EntityType' AND et.InternalCode = 'TRANSPORT_ORDER'
      AND s.Entity = 'OrderStatus' AND s.InternalCode = 'RETURNED'
      AND c.Entity = 'Capability' AND c.InternalCode IN ('EDIT_CARGO', 'REPRICE', 'CANCEL', 'ASSIGN_TRIP')
) AS s
ON t.TenantId IS NULL AND t.EntityTypeLookupId = s.EntityTypeLookupId AND t.StatusCodeId = s.StatusCodeId AND t.CapabilityLookupId = s.CapabilityLookupId
WHEN NOT MATCHED THEN
    INSERT (TenantId, EntityTypeLookupId, StatusCodeId, CapabilityLookupId, IsAllowed)
    VALUES (NULL, s.EntityTypeLookupId, s.StatusCodeId, s.CapabilityLookupId, 0);
GO
IF COL_LENGTH('dbo.TransportOrder', 'ReturnOfOrderId') IS NULL ALTER TABLE dbo.TransportOrder ADD ReturnOfOrderId INT NULL REFERENCES dbo.TransportOrder(TransportOrderId);
GO
MERGE dbo.Permission AS t
USING (VALUES
    ('orders.return', N'{"es":"Devolver órdenes (crear la orden de vuelta)","en":"Return orders (create the return order)"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ORDERS'), s.LabelJson, 1);
GO
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'orders.return' AND r.Name = 'TenantAdmin'
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);
GO

/* =========================================================================
   2026-10-18 (au) — Lote 113: «Devolver tras N intentos». Por compañía (Tenant.ReturnSuggestAfterAttempts, por omisión 2; 0 = sin sugerencia): cuando una orden lleva N intentos fallidos se SUGIERE devolverla (marca y mensaje); nunca bloquea.
   ========================================================================= */
IF COL_LENGTH('dbo.Tenant', 'ReturnSuggestAfterAttempts') IS NULL ALTER TABLE dbo.Tenant ADD ReturnSuggestAfterAttempts INT NOT NULL CONSTRAINT DF_Tenant_ReturnSuggest DEFAULT 2 CONSTRAINT CK_Tenant_ReturnSuggest CHECK (ReturnSuggestAfterAttempts BETWEEN 0 AND 20);
GO

/* =========================================================================
   2026-10-18 (av) — Lote 114: tipo de servicio «Pick up» (recojo y traigo): Sometida → Recogida → En almacén (final). La marca vive en el ExtraJson del tipo de servicio {"pickUp":true,"billable":true}
   (billable = interruptor «facturable»; se edita en Listas, por compañía con su ajuste propio). Se siembra el tipo PICK_UP.
   ========================================================================= */
MERGE dbo.LookupCode AS t
USING (SELECT N'ServiceType' AS Entity, N'PICK_UP' AS InternalCode) AS s ON t.Entity = s.Entity AND t.InternalCode = s.InternalCode AND t.TenantId IS NULL
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ExtraJson, SortOrder, IsSystem, IsActive)
    VALUES (N'ServiceType', N'PICK_UP', N'{"es":"Pick up","en":"Pick up"}', N'{"pickUp":true,"billable":true}', 5, 1, 1);
GO

/* =========================================================================
   2026-10-18 (aw) — Lote 115: cargo por devolución (opcional por contrato del cliente). Modo ORDER (monto único por orden devuelta, a todos los tipos de paquete), PACKAGE_TYPE (monto por cada tipo de paquete devuelto,
   tabla ContractReturnFee) o PIECE (monto por cada pieza devuelta). La orden devuelta guarda lo que se le cobra (TransportOrder.ReturnFeeAmount) y se factura aparte del flete (ReturnFeeInvoiced, InvoiceLine.IsReturnFee).
   ========================================================================= */
IF COL_LENGTH('dbo.Contract', 'ReturnFeeMode') IS NULL ALTER TABLE dbo.Contract ADD ReturnFeeMode VARCHAR(12) NULL CONSTRAINT CK_Contract_ReturnFeeMode CHECK (ReturnFeeMode IS NULL OR ReturnFeeMode IN ('ORDER','PACKAGE_TYPE','PIECE'));
GO
IF COL_LENGTH('dbo.Contract', 'ReturnFeeAmount') IS NULL ALTER TABLE dbo.Contract ADD ReturnFeeAmount DECIMAL(14,2) NULL;
GO
IF OBJECT_ID(N'dbo.ContractReturnFee', N'U') IS NULL CREATE TABLE dbo.ContractReturnFee (
    ContractReturnFeeId  INT IDENTITY(1,1) PRIMARY KEY,
    ContractId           INT NOT NULL REFERENCES dbo.Contract(ContractId),
    PackageTypeLookupId  INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    Amount               DECIMAL(14,2) NOT NULL CONSTRAINT CK_ContractReturnFee_Amount CHECK (Amount >= 0),
    IsActive             BIT NOT NULL CONSTRAINT DF_ContractReturnFee_IsActive DEFAULT 1
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ContractReturnFee' AND object_id = OBJECT_ID('dbo.ContractReturnFee'))
    CREATE UNIQUE INDEX UX_ContractReturnFee ON dbo.ContractReturnFee (ContractId, PackageTypeLookupId) WHERE IsActive = 1;
GO
IF COL_LENGTH('dbo.TransportOrder', 'ReturnFeeAmount') IS NULL ALTER TABLE dbo.TransportOrder ADD ReturnFeeAmount DECIMAL(14,2) NULL;
GO
IF COL_LENGTH('dbo.TransportOrder', 'ReturnFeeInvoiced') IS NULL ALTER TABLE dbo.TransportOrder ADD ReturnFeeInvoiced BIT NOT NULL CONSTRAINT DF_TransportOrder_ReturnFeeInvoiced DEFAULT 0;
GO
IF COL_LENGTH('dbo.InvoiceLine', 'IsReturnFee') IS NULL ALTER TABLE dbo.InvoiceLine ADD IsReturnFee BIT NOT NULL CONSTRAINT DF_InvoiceLine_IsReturnFee DEFAULT 0;
GO

/* =========================================================================
   2026-10-18 (ax) — Lote 116: avisos al consignatario (docs/p12). EL CLIENTE (no la compañía) prende el aviso al consignatario (Client.NotifyConsignee, idioma Client.NoticeLang) y marca, por consignatario, si acepta correo y/o texto
   (Location.NoticeEmailOptIn / NoticeSmsOptIn), con advertencia y bitácora (ConsigneeNoticeConsent). Los avisos salen por la tabla ConsigneeNotice (cola con reintentos; un aviso de alta CONSENT y los de estatus STATUS) con un enlace al
   avance de la orden por TOKEN propio (TransportOrder.TrackToken) y un enlace de baja (ConsigneeNotice.Token → ConsigneeUnsubscribe).
   ========================================================================= */
IF COL_LENGTH('dbo.Client', 'NotifyConsignee') IS NULL ALTER TABLE dbo.Client ADD NotifyConsignee BIT NOT NULL CONSTRAINT DF_Client_NotifyConsignee DEFAULT 0;
GO
IF COL_LENGTH('dbo.Client', 'NoticeLang') IS NULL ALTER TABLE dbo.Client ADD NoticeLang VARCHAR(2) NULL;
GO
IF COL_LENGTH('dbo.Location', 'NoticeEmailOptIn') IS NULL ALTER TABLE dbo.Location ADD NoticeEmailOptIn BIT NOT NULL CONSTRAINT DF_Location_NoticeEmailOptIn DEFAULT 0;
GO
IF COL_LENGTH('dbo.Location', 'NoticeSmsOptIn') IS NULL ALTER TABLE dbo.Location ADD NoticeSmsOptIn BIT NOT NULL CONSTRAINT DF_Location_NoticeSmsOptIn DEFAULT 0;
GO
IF COL_LENGTH('dbo.TransportOrder', 'TrackToken') IS NULL ALTER TABLE dbo.TransportOrder ADD TrackToken VARCHAR(64) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_TransportOrder_TrackToken' AND object_id = OBJECT_ID('dbo.TransportOrder'))
    CREATE UNIQUE INDEX UX_TransportOrder_TrackToken ON dbo.TransportOrder (TrackToken) WHERE TrackToken IS NOT NULL;
GO
IF OBJECT_ID(N'dbo.ConsigneeNoticeConsent', N'U') IS NULL CREATE TABLE dbo.ConsigneeNoticeConsent (
    ConsigneeNoticeConsentId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId        INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId        INT NOT NULL REFERENCES dbo.Client(ClientId),
    LocationId      INT NOT NULL REFERENCES dbo.Location(LocationId),
    Channel         VARCHAR(5) NOT NULL,
    Accepted        BIT NOT NULL,
    Contact         NVARCHAR(200) NOT NULL,
    WarningAccepted BIT NOT NULL,
    Source          VARCHAR(10) NOT NULL,
    ChangedBy       INT NULL,
    ChangedAtUtc    DATETIME2 NOT NULL CONSTRAINT DF_ConsigneeNoticeConsent_At DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_ConsigneeNoticeConsent_Channel CHECK (Channel IN ('EMAIL','SMS')),
    CONSTRAINT CK_ConsigneeNoticeConsent_Source CHECK (Source IN ('CLIENT','CONSIGNEE'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConsigneeNoticeConsent_Location' AND object_id = OBJECT_ID('dbo.ConsigneeNoticeConsent'))
    CREATE INDEX IX_ConsigneeNoticeConsent_Location ON dbo.ConsigneeNoticeConsent (LocationId, ChangedAtUtc);
GO
IF OBJECT_ID(N'dbo.ConsigneeNotice', N'U') IS NULL CREATE TABLE dbo.ConsigneeNotice (
    ConsigneeNoticeId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId          INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId          INT NOT NULL REFERENCES dbo.Client(ClientId),
    LocationId        INT NOT NULL REFERENCES dbo.Location(LocationId),
    TransportOrderId  INT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    Kind              VARCHAR(8) NOT NULL,
    Channel           VARCHAR(5) NOT NULL,
    Contact           NVARCHAR(200) NOT NULL,
    StatusCode        VARCHAR(40) NULL,
    Lang              VARCHAR(2) NOT NULL,
    Token             VARCHAR(64) NOT NULL,
    Status            VARCHAR(8) NOT NULL CONSTRAINT DF_ConsigneeNotice_Status DEFAULT 'PENDING',
    Attempts          INT NOT NULL CONSTRAINT DF_ConsigneeNotice_Attempts DEFAULT 0,
    NextAttemptUtc    DATETIME2 NULL,
    SentAtUtc         DATETIME2 NULL,
    Message           NVARCHAR(400) NULL,
    CreatedAtUtc      DATETIME2 NOT NULL CONSTRAINT DF_ConsigneeNotice_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_ConsigneeNotice_Kind CHECK (Kind IN ('STATUS','CONSENT')),
    CONSTRAINT CK_ConsigneeNotice_Channel CHECK (Channel IN ('EMAIL','SMS')),
    CONSTRAINT CK_ConsigneeNotice_Status CHECK (Status IN ('PENDING','SENT','SKIPPED','FAILED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ConsigneeNotice_Token' AND object_id = OBJECT_ID('dbo.ConsigneeNotice'))
    CREATE UNIQUE INDEX UX_ConsigneeNotice_Token ON dbo.ConsigneeNotice (Token);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConsigneeNotice_Pending' AND object_id = OBJECT_ID('dbo.ConsigneeNotice'))
    CREATE INDEX IX_ConsigneeNotice_Pending ON dbo.ConsigneeNotice (Status, NextAttemptUtc);
GO
IF OBJECT_ID(N'dbo.ConsigneeUnsubscribe', N'U') IS NULL CREATE TABLE dbo.ConsigneeUnsubscribe (
    ConsigneeUnsubscribeId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId     INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Channel      VARCHAR(5) NOT NULL,
    Contact      NVARCHAR(200) NOT NULL,
    AtUtc        DATETIME2 NOT NULL CONSTRAINT DF_ConsigneeUnsubscribe_At DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_ConsigneeUnsubscribe_Channel CHECK (Channel IN ('EMAIL','SMS'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ConsigneeUnsubscribe' AND object_id = OBJECT_ID('dbo.ConsigneeUnsubscribe'))
    CREATE UNIQUE INDEX UX_ConsigneeUnsubscribe ON dbo.ConsigneeUnsubscribe (TenantId, Channel, Contact);
GO

/* =========================================================================
   2026-10-18 (ay) — Lote 117: acceso del consignatario (docs/p10-portal-diseno.md). El CLIENTE invita al consignatario por correo; la fila de PortalUser lleva el consignatario (LocationId, una localización de entrega
   del cliente) y el rol de portal CONSIGNEE. El consignatario entra al portal con contraseña + computadora de confianza (sin MFA) y solo ve y cambia lo suyo.
   ========================================================================= */
IF COL_LENGTH('dbo.PortalUser', 'LocationId') IS NULL ALTER TABLE dbo.PortalUser ADD LocationId INT NULL CONSTRAINT FK_PortalUser_Location REFERENCES dbo.Location(LocationId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PortalUser_Location' AND object_id = OBJECT_ID('dbo.PortalUser'))
    CREATE INDEX IX_PortalUser_Location ON dbo.PortalUser (LocationId) WHERE LocationId IS NOT NULL;
GO
MERGE dbo.LookupCode AS t
USING (VALUES (N'CONSIGNEE', N'{"es":"Consignatario","en":"Consignee"}', 4)) AS s (Code, LabelJson, Srt)
ON t.Entity = N'PortalRole' AND t.InternalCode = s.Code AND t.TenantId IS NULL
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'PortalRole', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

/* =========================================================================
   2026-10-18 (az) — Lote 117b: mensajes del consignatario con su cliente (dictado por Luis). ConsigneeConversation = la conversación de UNA persona de un consignatario con su cliente (la abre el consignatario con su primer
   mensaje; del lado del cliente la contestan sus administradores y operadores); ConsigneeMessage = sus mensajes (nunca se editan ni se borran).
   ========================================================================= */
IF OBJECT_ID(N'dbo.ConsigneeConversation', N'U') IS NULL CREATE TABLE dbo.ConsigneeConversation (
    ConsigneeConversationId BIGINT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_ConsigneeConversation_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ClientId INT NOT NULL REFERENCES dbo.Client(ClientId),
    LocationId INT NOT NULL REFERENCES dbo.Location(LocationId),
    ConsigneeUserId INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ConsigneeConversation_Created DEFAULT SYSUTCDATETIME(),
    LastMessageId BIGINT NOT NULL CONSTRAINT DF_ConsigneeConversation_LastId DEFAULT 0,
    LastMessageAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ConsigneeConversation_LastAt DEFAULT SYSUTCDATETIME(),
    LastMessagePreview NVARCHAR(200) NULL,
    LastSenderKind VARCHAR(10) NULL,
    ClientLastReadMessageId BIGINT NOT NULL CONSTRAINT DF_ConsigneeConversation_ClientRead DEFAULT 0,
    ConsigneeLastReadMessageId BIGINT NOT NULL CONSTRAINT DF_ConsigneeConversation_ConsigneeRead DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ConsigneeConversation_PublicId' AND object_id = OBJECT_ID('dbo.ConsigneeConversation')) CREATE UNIQUE INDEX UQ_ConsigneeConversation_PublicId ON dbo.ConsigneeConversation(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ConsigneeConversation_Person' AND object_id = OBJECT_ID('dbo.ConsigneeConversation')) CREATE UNIQUE INDEX UX_ConsigneeConversation_Person ON dbo.ConsigneeConversation(TenantId, ClientId, LocationId, ConsigneeUserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConsigneeConversation_Last' AND object_id = OBJECT_ID('dbo.ConsigneeConversation')) CREATE INDEX IX_ConsigneeConversation_Last ON dbo.ConsigneeConversation(TenantId, ClientId, LastMessageAtUtc);
GO
IF OBJECT_ID(N'dbo.ConsigneeMessage', N'U') IS NULL CREATE TABLE dbo.ConsigneeMessage (
    ConsigneeMessageId BIGINT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    ConsigneeConversationId BIGINT NOT NULL REFERENCES dbo.ConsigneeConversation(ConsigneeConversationId),
    SenderKind VARCHAR(10) NOT NULL,
    SenderUserId INT NOT NULL REFERENCES dbo.AspNetUsers(Id),
    Body NVARCHAR(2000) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ConsigneeMessage_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_ConsigneeMessage_Sender CHECK (SenderKind IN ('CONSIGNEE','CLIENT'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConsigneeMessage_Conversation' AND object_id = OBJECT_ID('dbo.ConsigneeMessage')) CREATE INDEX IX_ConsigneeMessage_Conversation ON dbo.ConsigneeMessage(ConsigneeConversationId, ConsigneeMessageId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConsigneeMessage_Sender' AND object_id = OBJECT_ID('dbo.ConsigneeMessage')) CREATE INDEX IX_ConsigneeMessage_Sender ON dbo.ConsigneeMessage(SenderUserId, CreatedAtUtc);
GO

/* =========================================================================
   2026-10-18 (ba) — Corrección del Lote 111: las reclamaciones se guardaban con PublicId = 00000000-0000-0000-0000-000000000000 (EF mandaba el Guid vacío y el DEFAULT NEWID() no entraba), así que abrir o resolver una
   reclamación por su identificador actuaba sobre otra. Se les asigna un identificador propio. Idempotente.
   ========================================================================= */
UPDATE dbo.Claim SET PublicId = NEWID() WHERE PublicId = '00000000-0000-0000-0000-000000000000';
GO

/* =========================================================================
   2026-10-18 (bb) — Lote 118: registrar la entrega desde la web (dictado de Luis). Tabla DeliveryProof (el comprobante: quién recibió, cuándo, quién lo registró, la firma automática del usuario y el estado de la
   geocodificación asíncrona con Google), permiso orders.deliver (a TenantAdmin; Luis designa a quién más) y EntityType DELIVERY_PROOF para la bitácora. A lo sumo un comprobante vigente por orden. Solo AGREGA. Idempotente.
   ========================================================================= */
IF OBJECT_ID(N'dbo.DeliveryProof', N'U') IS NULL CREATE TABLE dbo.DeliveryProof (
    DeliveryProofId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_DeliveryProof_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    TransportOrderId INT NOT NULL REFERENCES dbo.TransportOrder(TransportOrderId),
    DeliveredAtUtc DATETIME2 NOT NULL,
    RecipientName NVARCHAR(150) NOT NULL,
    Notes NVARCHAR(500) NULL,
    Source VARCHAR(20) NOT NULL CONSTRAINT DF_DeliveryProof_Source DEFAULT 'STAFF_WEB',
    RecordedBy INT NOT NULL,
    SignedByUserId INT NOT NULL,
    SignatureKind VARCHAR(20) NOT NULL CONSTRAINT DF_DeliveryProof_Signature DEFAULT 'USER',
    GeoStatus VARCHAR(12) NOT NULL CONSTRAINT DF_DeliveryProof_GeoStatus DEFAULT 'NONE',
    GeoAccuracy VARCHAR(8) NULL,
    Latitude DECIMAL(9,6) NULL,
    Longitude DECIMAL(9,6) NULL,
    GeoCheckedAtUtc DATETIME2 NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_DeliveryProof_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_DeliveryProof_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_DeliveryProof_Source CHECK (Source IN ('STAFF_WEB','DRIVER_APP')),
    CONSTRAINT CK_DeliveryProof_Signature CHECK (SignatureKind IN ('USER','RECIPIENT')),
    CONSTRAINT CK_DeliveryProof_GeoStatus CHECK (GeoStatus IN ('NONE','FROM_STOP','PENDING','RUNNING','OK','NOT_FOUND','FAILED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DeliveryProof_PublicId' AND object_id = OBJECT_ID('dbo.DeliveryProof')) CREATE UNIQUE INDEX IX_DeliveryProof_PublicId ON dbo.DeliveryProof(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_DeliveryProof_Order' AND object_id = OBJECT_ID('dbo.DeliveryProof')) CREATE UNIQUE INDEX UX_DeliveryProof_Order ON dbo.DeliveryProof(TransportOrderId) WHERE IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DeliveryProof_Geo' AND object_id = OBJECT_ID('dbo.DeliveryProof')) CREATE INDEX IX_DeliveryProof_Geo ON dbo.DeliveryProof(GeoStatus, GeoCheckedAtUtc) WHERE GeoStatus IN ('PENDING','RUNNING');
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'DELIVERY_PROOF', N'{"es":"Comprobante de entrega","en":"Proof of delivery"}', 161)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO
MERGE dbo.Permission AS t
USING (VALUES
    ('orders.deliver', N'{"es":"Registrar la entrega de una orden desde la web","en":"Register an order delivery from the web"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ORDERS'), s.LabelJson, 1);
GO
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'orders.deliver' AND r.Name = 'TenantAdmin'
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);
GO

/* =========================================================================
   2026-10-18 (bc) — Lote 119 (P10): preferencias de aviso de cada persona del portal (dictado de Luis: una pantalla para configurarlo). La COMPAÑÍA decide qué avisos recibe el cliente y por qué canal (techo); cada persona del
   portal solo puede APAGAR lo que quiera dejar de recibir: una fila por persona y aviso con sus interruptores (por omisión, encendido). Solo AGREGA. Idempotente.
   ========================================================================= */
IF OBJECT_ID(N'dbo.PortalNoticePref', N'U') IS NULL CREATE TABLE dbo.PortalNoticePref (
    PortalNoticePrefId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    PortalUserId INT NOT NULL REFERENCES dbo.PortalUser(PortalUserId),
    Kind VARCHAR(30) NOT NULL,
    EmailEnabled BIT NOT NULL CONSTRAINT DF_PortalNoticePref_Email DEFAULT 1,
    AppEnabled BIT NOT NULL CONSTRAINT DF_PortalNoticePref_App DEFAULT 1,
    UpdatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_PortalNoticePref_Updated DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_PortalNoticePref' AND object_id = OBJECT_ID('dbo.PortalNoticePref')) CREATE UNIQUE INDEX UQ_PortalNoticePref ON dbo.PortalNoticePref(PortalUserId, Kind);
GO

/* =========================================================================
   2026-10-18 (bd) — Lote 126: PROCESAMIENTO DE LA FACTURA del cliente (dictado de Luis). La factura del cliente viaja con la orden (su número está en ClientInvoiceNumber; si no
   viene, le pegan un label). Cuando la orden ya se entregó, la factura se PROCESA como «second check»: en la pantalla de leer hojas se sube el documento (o se teclea el número) y
   el sistema valida. Sea hoja de COD (que además cuadra el cobro) o solo cotejo, la orden guarda cuándo se procesó su factura, quién y con qué lote de lectura (CodReconcileRun:
   el archivo y la fila de cada hoja). Una sola vez por orden. El contrato puede cobrar el cotejo (apagado por omisión). Sin órdenes ficticias. Solo AGREGA. Idempotente.
   ========================================================================= */
IF COL_LENGTH('dbo.TransportOrder', 'InvoiceProcessedAtUtc') IS NULL ALTER TABLE dbo.TransportOrder ADD InvoiceProcessedAtUtc DATETIME2 NULL;
GO
IF COL_LENGTH('dbo.TransportOrder', 'InvoiceProcessedBy') IS NULL ALTER TABLE dbo.TransportOrder ADD InvoiceProcessedBy INT NULL;
GO
IF COL_LENGTH('dbo.TransportOrder', 'InvoiceProcessRunId') IS NULL ALTER TABLE dbo.TransportOrder ADD InvoiceProcessRunId INT NULL CONSTRAINT FK_TransportOrder_InvoiceProcessRun REFERENCES dbo.CodReconcileRun(CodReconcileRunId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransportOrder_InvoiceProcessRun' AND object_id = OBJECT_ID('dbo.TransportOrder')) CREATE INDEX IX_TransportOrder_InvoiceProcessRun ON dbo.TransportOrder(InvoiceProcessRunId) WHERE InvoiceProcessRunId IS NOT NULL;
GO
IF COL_LENGTH('dbo.Contract', 'BillInvoiceCheck') IS NULL ALTER TABLE dbo.Contract ADD BillInvoiceCheck BIT NOT NULL CONSTRAINT DF_Contract_BillInvoiceCheck DEFAULT 0;
GO
IF COL_LENGTH('dbo.Contract', 'InvoiceCheckFee') IS NULL ALTER TABLE dbo.Contract ADD InvoiceCheckFee DECIMAL(14,2) NULL;
GO

/* =========================================================================
   2026-10-18 (be) — Lote 126: tipo de cargo «Cotejo de factura» (INVOICE_CHECK) para la línea de factura que cobra el procesamiento de la factura del cliente cuando el contrato
   lo tiene encendido (Contract.BillInvoiceCheck / InvoiceCheckFee). Solo datos. Idempotente.
   ========================================================================= */
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'INVOICE_CHECK', N'{"es":"Cotejo de factura","en":"Invoice check"}', 9)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'ChargeType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'ChargeType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

/* =========================================================================
   2026-10-18 (bg) — Lote 127: ALMACÉN DE ARCHIVOS (dictado de Luis): la firma del que recibió, las fotos de la entrega o de un daño y los documentos se guardan como ARCHIVOS cifrados en el
   almacén del servidor (IFileStore; llave por compañía) y en la base solo la REFERENCIA: a quién pertenece (EntityType + id, el patrón polimórfico de ContactPoint), la clase, nombre, tipo,
   tamaño, hash y la llave en el almacén. Se sirven solo por el API con el permiso del dueño; se quitan en lógico. Solo AGREGA. Idempotente.
   ========================================================================= */
IF OBJECT_ID(N'dbo.StoredFile', N'U') IS NULL CREATE TABLE dbo.StoredFile (
    StoredFileId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_StoredFile_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    OwnerEntityLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    OwnerId INT NOT NULL,
    Kind VARCHAR(30) NOT NULL,
    FileName NVARCHAR(200) NOT NULL,
    ContentType VARCHAR(100) NOT NULL,
    SizeBytes BIGINT NOT NULL,
    Sha256 CHAR(64) NOT NULL,
    StorageKey NVARCHAR(300) NOT NULL,
    IsEncrypted BIT NOT NULL CONSTRAINT DF_StoredFile_Encrypted DEFAULT 1,
    UploadedBy INT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_StoredFile_IsActive DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_StoredFile_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_StoredFile_Kind CHECK (Kind IN ('SIGNATURE','DELIVERY_PHOTO','DAMAGE_PHOTO','DOCUMENT'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_StoredFile_PublicId' AND object_id = OBJECT_ID('dbo.StoredFile')) CREATE UNIQUE INDEX UX_StoredFile_PublicId ON dbo.StoredFile(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StoredFile_Owner' AND object_id = OBJECT_ID('dbo.StoredFile')) CREATE INDEX IX_StoredFile_Owner ON dbo.StoredFile(OwnerEntityLookupId, OwnerId) INCLUDE (IsActive, Kind);
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'STORED_FILE', N'{"es":"Archivo","en":"File"}', 162)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

/* =========================================================================
   2026-10-19 (bh) — Lote 129: NIVEL DE ZONA (Isla/Metro; dictado de Luis). ZoneGroup = grupo de zonas por compañía (código, nombre, orden, chofer por omisión del nivel). La zona de despacho
   gana su nivel (ZoneGroupId); la orden guarda la zona de su parada de entrega (DeliveryZoneId, resuelta por ZIP y pueblo) y de ahí sale su nivel para listas, filtros y vistas. Las tarifas
   del chofer (por pieza) y del contrato ganan un nivel OPCIONAL: con nivel aplican solo a las órdenes de ese nivel y ganan sobre la general; los índices únicos de «una sola fila abierta»
   se rehacen incluyendo el nivel (NULL cuenta como un valor). Solo AGREGA. Idempotente.
   ========================================================================= */
IF OBJECT_ID(N'dbo.ZoneGroup', N'U') IS NULL CREATE TABLE dbo.ZoneGroup (
    ZoneGroupId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Code NVARCHAR(20) NOT NULL,
    Name NVARCHAR(120) NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_ZoneGroup_Sort DEFAULT 0,
    DefaultDriverId INT NULL REFERENCES dbo.Driver(DriverId),
    IsActive BIT NOT NULL CONSTRAINT DF_ZoneGroup_IsActive DEFAULT 1,
    CONSTRAINT UQ_ZoneGroup UNIQUE (TenantId, Code)
);
GO
IF COL_LENGTH('dbo.DispatchZone', 'ZoneGroupId') IS NULL ALTER TABLE dbo.DispatchZone ADD ZoneGroupId INT NULL CONSTRAINT FK_DispatchZone_ZoneGroup REFERENCES dbo.ZoneGroup(ZoneGroupId);
GO
IF COL_LENGTH('dbo.TransportOrder', 'DeliveryZoneId') IS NULL ALTER TABLE dbo.TransportOrder ADD DeliveryZoneId INT NULL CONSTRAINT FK_TransportOrder_DeliveryZone REFERENCES dbo.DispatchZone(DispatchZoneId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransportOrder_DeliveryZone' AND object_id = OBJECT_ID('dbo.TransportOrder')) CREATE INDEX IX_TransportOrder_DeliveryZone ON dbo.TransportOrder(DeliveryZoneId) WHERE DeliveryZoneId IS NOT NULL;
GO
IF COL_LENGTH('dbo.DriverDeliveryRate', 'ZoneGroupId') IS NULL ALTER TABLE dbo.DriverDeliveryRate ADD ZoneGroupId INT NULL CONSTRAINT FK_DriverDeliveryRate_ZoneGroup REFERENCES dbo.ZoneGroup(ZoneGroupId);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_DriverDeliveryRate_Open' AND object_id = OBJECT_ID('dbo.DriverDeliveryRate')) DROP INDEX UQ_DriverDeliveryRate_Open ON dbo.DriverDeliveryRate;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_DriverDeliveryRate_Open2' AND object_id = OBJECT_ID('dbo.DriverDeliveryRate'))
    CREATE UNIQUE INDEX UQ_DriverDeliveryRate_Open2 ON dbo.DriverDeliveryRate(DriverId, ServiceTypeLookupId, PackageTypeLookupId, ZoneGroupId) WHERE EffectiveTo IS NULL AND IsActive = 1;
GO
IF COL_LENGTH('dbo.RateComponent', 'ZoneGroupId') IS NULL ALTER TABLE dbo.RateComponent ADD ZoneGroupId INT NULL CONSTRAINT FK_RateComponent_ZoneGroup REFERENCES dbo.ZoneGroup(ZoneGroupId);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_RateComponent_Open' AND object_id = OBJECT_ID('dbo.RateComponent')) DROP INDEX UQ_RateComponent_Open ON dbo.RateComponent;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_RateComponent_Open2' AND object_id = OBJECT_ID('dbo.RateComponent'))
    CREATE UNIQUE INDEX UQ_RateComponent_Open2 ON dbo.RateComponent(TenantId, ContractId, ComponentTypeLookupId, ServiceTypeLookupId, PackageTypeLookupId, ZoneGroupId) WHERE EffectiveTo IS NULL AND IsActive = 1;
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'ZONE_GROUP', N'{"es":"Nivel de zona","en":"Zone level"}', 163)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

/* =========================================================================
   2026-10-19 (bi) — Lote 131 (P11): archivos con VARIAS líneas por orden (caso AxisCare, para cualquier cliente). La plantilla de órdenes gana MultiLine (agrupa las filas por número de orden)
   y LineRulesJson (reglas que dicen qué líneas NO son pieza: descripción o factura; todo lo demás es pieza). Solo AGREGA. Idempotente.
   ========================================================================= */
IF COL_LENGTH('dbo.ImportTemplate', 'MultiLine') IS NULL
    ALTER TABLE dbo.ImportTemplate ADD MultiLine BIT NOT NULL CONSTRAINT DF_ImportTemplate_MultiLine DEFAULT 0;
GO
IF COL_LENGTH('dbo.ImportTemplate', 'LineRulesJson') IS NULL
    ALTER TABLE dbo.ImportTemplate ADD LineRulesJson NVARCHAR(MAX) NULL;
GO

/* =========================================================================
   2026-10-19 (bj) — Lote 132 (P11): INTEGRACIONES. dbo.Integration (de dónde o a dónde van los datos, con qué plantilla y cuándo; con su CUENTA DE SERVICIO y su rol),
   dbo.IntegrationRun (bitácora de corridas), dbo.IntegrationFileLog (archivos procesados con su hash SHA-256: el mismo contenido no se procesa dos veces) y dbo.IntegrationInboxItem
   (la bandeja de lo que no se pudo procesar solo: motivo exacto y datos tal como llegaron, para corregir y reprocesar; el detalle se purga pasados los días de retención).
   NotificationRule.ScopeKey: regla de aviso administrada por una integración (solo dispara con los avisos de esa integración).
   Módulo INTEGRATIONS (apagado: se enciende por compañía) y permisos integrations.view / .create / .create.transport / .create.warehouse / .create.files / .manage / .inbox
   -> TenantAdmin de la plantilla Y de los roles YA CLONADOS de cada compañía. EntityTypes INTEGRATION e INTEGRATION_INBOX. Solo AGREGA. Idempotente.
   ========================================================================= */
IF OBJECT_ID(N'dbo.Integration', N'U') IS NULL CREATE TABLE dbo.Integration (
    IntegrationId       INT IDENTITY(1,1) CONSTRAINT PK_Integration PRIMARY KEY,
    PublicId            UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Integration_PublicId DEFAULT NEWID(),
    TenantId            INT NOT NULL CONSTRAINT FK_Integration_Tenant REFERENCES dbo.Tenant(TenantId),
    Name                NVARCHAR(120) NOT NULL,
    Description         NVARCHAR(400) NULL,
    Direction           VARCHAR(10) NOT NULL CONSTRAINT DF_Integration_Direction DEFAULT 'INBOUND',
    ClientId            INT NULL CONSTRAINT FK_Integration_Client REFERENCES dbo.Client(ClientId),
    ConnectorKind       VARCHAR(20) NOT NULL CONSTRAINT DF_Integration_Connector DEFAULT 'MANUAL',
    ConnectorConfigJson NVARCHAR(MAX) NULL,
    SecretEnc           VARBINARY(MAX) NULL,
    ImportTemplateId    INT NULL CONSTRAINT FK_Integration_Template REFERENCES dbo.ImportTemplate(ImportTemplateId),
    ScheduleKind        VARCHAR(10) NOT NULL CONSTRAINT DF_Integration_ScheduleKind DEFAULT 'MANUAL',
    IntervalSeconds     INT NULL,
    NextRunUtc          DATETIME2 NULL,
    ScopesJson          NVARCHAR(MAX) NOT NULL CONSTRAINT DF_Integration_Scopes DEFAULT '[]',
    ServiceUserId       INT NULL CONSTRAINT FK_Integration_ServiceUser REFERENCES dbo.AspNetUsers(Id),
    ServiceRoleId       INT NULL CONSTRAINT FK_Integration_ServiceRole REFERENCES dbo.Role(RoleId),
    NotifyUserIdsJson   NVARCHAR(MAX) NOT NULL CONSTRAINT DF_Integration_Notify DEFAULT '[]',
    NotifyRuleId        INT NULL,
    RetentionDays       INT NOT NULL CONSTRAINT DF_Integration_Retention DEFAULT 30,
    IsActive            BIT NOT NULL CONSTRAINT DF_Integration_IsActive DEFAULT 1,
    LastStartedUtc      DATETIME2 NULL,
    LastFinishedUtc     DATETIME2 NULL,
    LastStatus          VARCHAR(10) NOT NULL CONSTRAINT DF_Integration_LastStatus DEFAULT 'NEVER',
    LastMessage         NVARCHAR(400) NULL,
    LockedBy            NVARCHAR(80) NULL,
    LockedUntilUtc      DATETIME2 NULL,
    CreatedAtUtc        DATETIME2 NOT NULL CONSTRAINT DF_Integration_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT NULL CONSTRAINT FK_Integration_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc        DATETIME2 NULL,
    UpdatedBy           INT NULL CONSTRAINT FK_Integration_UpdatedBy REFERENCES dbo.AspNetUsers(Id),
    RowVersion          ROWVERSION,
    CONSTRAINT UQ_Integration_Name UNIQUE (TenantId, Name),
    CONSTRAINT UQ_Integration_PublicId UNIQUE (PublicId),
    CONSTRAINT CK_Integration_Direction CHECK (Direction IN ('INBOUND', 'OUTBOUND')),
    CONSTRAINT CK_Integration_Connector CHECK (ConnectorKind IN ('FOLDER', 'MANUAL', 'EMAIL', 'SFTP')),
    CONSTRAINT CK_Integration_Schedule CHECK ((ScheduleKind = 'MANUAL' AND IntervalSeconds IS NULL) OR (ScheduleKind = 'INTERVAL' AND IntervalSeconds BETWEEN 60 AND 604800)),
    CONSTRAINT CK_Integration_Status CHECK (LastStatus IN ('NEVER', 'RUNNING', 'OK', 'PARTIAL', 'FAILED')),
    CONSTRAINT CK_Integration_Retention CHECK (RetentionDays BETWEEN 1 AND 365)
);
GO
IF OBJECT_ID(N'dbo.IntegrationRun', N'U') IS NULL CREATE TABLE dbo.IntegrationRun (
    IntegrationRunId    BIGINT IDENTITY(1,1) CONSTRAINT PK_IntegrationRun PRIMARY KEY,
    TenantId            INT NOT NULL CONSTRAINT FK_IntegrationRun_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationId       INT NOT NULL CONSTRAINT FK_IntegrationRun_Integration REFERENCES dbo.Integration(IntegrationId),
    StartedUtc          DATETIME2 NOT NULL CONSTRAINT DF_IntegrationRun_Started DEFAULT SYSUTCDATETIME(),
    FinishedUtc         DATETIME2 NULL,
    Status              VARCHAR(10) NOT NULL CONSTRAINT DF_IntegrationRun_Status DEFAULT 'RUNNING',
    TriggeredBy         VARCHAR(10) NOT NULL CONSTRAINT DF_IntegrationRun_Trigger DEFAULT 'MANUAL',
    TriggeredByUserId   INT NULL CONSTRAINT FK_IntegrationRun_User REFERENCES dbo.AspNetUsers(Id),
    FilesRead           INT NOT NULL CONSTRAINT DF_IntegrationRun_Files DEFAULT 0,
    OrdersCreated       INT NOT NULL CONSTRAINT DF_IntegrationRun_Orders DEFAULT 0,
    Rejected            INT NOT NULL CONSTRAINT DF_IntegrationRun_Rejected DEFAULT 0,
    Message             NVARCHAR(1000) NULL,
    CONSTRAINT CK_IntegrationRun_Status CHECK (Status IN ('RUNNING', 'OK', 'PARTIAL', 'FAILED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationRun_Integration' AND object_id = OBJECT_ID('dbo.IntegrationRun'))
    CREATE INDEX IX_IntegrationRun_Integration ON dbo.IntegrationRun(TenantId, IntegrationId, IntegrationRunId DESC);
GO
IF OBJECT_ID(N'dbo.IntegrationFileLog', N'U') IS NULL CREATE TABLE dbo.IntegrationFileLog (
    IntegrationFileLogId BIGINT IDENTITY(1,1) CONSTRAINT PK_IntegrationFileLog PRIMARY KEY,
    TenantId            INT NOT NULL CONSTRAINT FK_IntegrationFileLog_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationId       INT NOT NULL CONSTRAINT FK_IntegrationFileLog_Integration REFERENCES dbo.Integration(IntegrationId),
    IntegrationRunId    BIGINT NOT NULL CONSTRAINT FK_IntegrationFileLog_Run REFERENCES dbo.IntegrationRun(IntegrationRunId),
    FileName            NVARCHAR(260) NOT NULL,
    Sha256              CHAR(64) NOT NULL,
    SizeBytes           BIGINT NOT NULL CONSTRAINT DF_IntegrationFileLog_Size DEFAULT 0,
    Result              VARCHAR(12) NOT NULL CONSTRAINT DF_IntegrationFileLog_Result DEFAULT 'PROCESSED',
    Message             NVARCHAR(1000) NULL,
    OrdersCreated       INT NOT NULL CONSTRAINT DF_IntegrationFileLog_Orders DEFAULT 0,
    Rejected            INT NOT NULL CONSTRAINT DF_IntegrationFileLog_Rejected DEFAULT 0,
    ProcessedUtc        DATETIME2 NOT NULL CONSTRAINT DF_IntegrationFileLog_Processed DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_IntegrationFileLog_Result CHECK (Result IN ('PROCESSED', 'DUPLICATE', 'FAILED', 'REPROCESSED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IntegrationFileLog_Hash' AND object_id = OBJECT_ID('dbo.IntegrationFileLog'))
    CREATE UNIQUE INDEX UX_IntegrationFileLog_Hash ON dbo.IntegrationFileLog(IntegrationId, Sha256) WHERE Result = 'PROCESSED';
GO
IF OBJECT_ID(N'dbo.IntegrationInboxItem', N'U') IS NULL CREATE TABLE dbo.IntegrationInboxItem (
    IntegrationInboxItemId BIGINT IDENTITY(1,1) CONSTRAINT PK_IntegrationInboxItem PRIMARY KEY,
    TenantId            INT NOT NULL CONSTRAINT FK_IntegrationInbox_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationId       INT NOT NULL CONSTRAINT FK_IntegrationInbox_Integration REFERENCES dbo.Integration(IntegrationId),
    IntegrationRunId    BIGINT NOT NULL CONSTRAINT FK_IntegrationInbox_Run REFERENCES dbo.IntegrationRun(IntegrationRunId),
    IntegrationFileLogId BIGINT NULL CONSTRAINT FK_IntegrationInbox_File REFERENCES dbo.IntegrationFileLog(IntegrationFileLogId),
    SourceRow           INT NULL,
    ReferenceKey        NVARCHAR(80) NULL,
    Reason              NVARCHAR(1000) NOT NULL,
    DataJson            NVARCHAR(MAX) NULL,
    Status              VARCHAR(12) NOT NULL CONSTRAINT DF_IntegrationInbox_Status DEFAULT 'PENDING',
    CreatedAtUtc        DATETIME2 NOT NULL CONSTRAINT DF_IntegrationInbox_Created DEFAULT SYSUTCDATETIME(),
    ResolvedAtUtc       DATETIME2 NULL,
    ResolvedBy          INT NULL CONSTRAINT FK_IntegrationInbox_ResolvedBy REFERENCES dbo.AspNetUsers(Id),
    Note                NVARCHAR(400) NULL,
    OrderPublicId       UNIQUEIDENTIFIER NULL,
    RowVersion          ROWVERSION,
    CONSTRAINT CK_IntegrationInbox_Status CHECK (Status IN ('PENDING', 'REPROCESSED', 'DISCARDED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationInbox_Status' AND object_id = OBJECT_ID('dbo.IntegrationInboxItem'))
    CREATE INDEX IX_IntegrationInbox_Status ON dbo.IntegrationInboxItem(TenantId, IntegrationId, Status, IntegrationInboxItemId DESC);
GO
IF COL_LENGTH('dbo.NotificationRule', 'ScopeKey') IS NULL ALTER TABLE dbo.NotificationRule ADD ScopeKey VARCHAR(40) NULL;
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'INTEGRATION', N'{"es":"Integración","en":"Integration"}', 164),
    (N'INTEGRATION_INBOX', N'{"es":"Bandeja de integración","en":"Integration inbox"}', 165)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO
MERGE dbo.ModuleDefinition AS t
USING (VALUES
 ('INTEGRATIONS', N'Integraciones', N'Integrations', N'Entrada y salida de datos por carpeta, correo, SFTP y API, con bandeja de lo que no se pudo procesar', N'Sistema', NULL, 130)
) AS s(ModuleKey, Name, NameEn, Description, Category, DependsOnModuleKey, SortOrder)
ON t.ModuleKey = s.ModuleKey
WHEN MATCHED THEN UPDATE SET Name = s.Name, NameEn = s.NameEn, Description = s.Description, Category = s.Category, DependsOnModuleKey = s.DependsOnModuleKey, SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN INSERT (ModuleKey, Name, NameEn, Description, Category, DependsOnModuleKey, SortOrder) VALUES (s.ModuleKey, s.Name, s.NameEn, s.Description, s.Category, s.DependsOnModuleKey, s.SortOrder);
GO
-- Permisos (categoría ADMIN)
MERGE dbo.Permission AS t
USING (VALUES
    ('integrations.view', N'{"es":"Ver las integraciones y su historial","en":"View integrations and their history"}'),
    ('integrations.create', N'{"es":"Crear integraciones (base; además el permiso de cada familia)","en":"Create integrations (base; plus each family''s permission)"}'),
    ('integrations.create.transport', N'{"es":"Crear integraciones de transporte (órdenes, tracking, facturas, consignatarios, perfil)","en":"Create transport integrations (orders, tracking, invoices, consignees, profile)"}'),
    ('integrations.create.warehouse', N'{"es":"Crear integraciones de almacén (inventario, productos, recibo, despacho, ajustes, transferencias, órdenes de compra y venta)","en":"Create warehouse integrations (inventory, products, receiving, dispatch, adjustments, transfers, purchase and sales orders)"}'),
    ('integrations.create.files', N'{"es":"Crear integraciones por archivo (carpeta, SFTP, correo)","en":"Create file integrations (folder, SFTP, email)"}'),
    ('integrations.manage', N'{"es":"Administrar integraciones (editar, apagar, correr ahora)","en":"Manage integrations (edit, turn off, run now)"}'),
    ('integrations.inbox', N'{"es":"Trabajar la bandeja de las integraciones (corregir, reprocesar, descartar)","en":"Work the integrations inbox (fix, reprocess, discard)"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ADMIN'), s.LabelJson, 1);
GO
-- Plantilla de sistema (TenantId NULL) y roles ya clonados de cada compañía: TenantAdmin
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code LIKE 'integrations.%'
WHERE r.Name IN ('TenantAdmin')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* =========================================================================
   2026-10-19 (bk) — Lote 134 (P11): conector FTP (además de SFTP). El CHECK de dbo.Integration.ConnectorKind ahora admite 'FTP'. Solo AMPLÍA. Idempotente.
   ========================================================================= */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Integration_Connector' AND parent_object_id = OBJECT_ID('dbo.Integration') AND definition NOT LIKE '%''FTP''%')
BEGIN
    ALTER TABLE dbo.Integration DROP CONSTRAINT CK_Integration_Connector;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Integration_Connector' AND parent_object_id = OBJECT_ID('dbo.Integration'))
    ALTER TABLE dbo.Integration ADD CONSTRAINT CK_Integration_Connector CHECK (ConnectorKind IN ('FOLDER', 'MANUAL', 'EMAIL', 'SFTP', 'FTP'));
GO

/* =========================================================================
   2026-10-19 (bl) — Lote 135 (P11): API DE LOS CLIENTES. El conector 'API' (el cliente llama a Teikem con una llave), dbo.IntegrationApiKey (llave pública tk_…, hash SHA-256 del secreto, límite
   de uso por minuto, vencimiento, revocación) y dbo.IntegrationApiLog (bitácora de cada solicitud, 30 días). Solo AMPLÍA. Idempotente.
   ========================================================================= */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Integration_Connector' AND parent_object_id = OBJECT_ID('dbo.Integration') AND definition NOT LIKE '%''API''%')
    ALTER TABLE dbo.Integration DROP CONSTRAINT CK_Integration_Connector;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Integration_Connector' AND parent_object_id = OBJECT_ID('dbo.Integration'))
    ALTER TABLE dbo.Integration ADD CONSTRAINT CK_Integration_Connector CHECK (ConnectorKind IN ('FOLDER', 'MANUAL', 'EMAIL', 'SFTP', 'FTP', 'API'));
GO
IF OBJECT_ID(N'dbo.IntegrationApiKey', N'U') IS NULL CREATE TABLE dbo.IntegrationApiKey (
    IntegrationApiKeyId INT IDENTITY(1,1) CONSTRAINT PK_IntegrationApiKey PRIMARY KEY,
    TenantId            INT NOT NULL CONSTRAINT FK_IntegrationApiKey_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationId       INT NOT NULL CONSTRAINT FK_IntegrationApiKey_Integration REFERENCES dbo.Integration(IntegrationId),
    KeyId               VARCHAR(40) NOT NULL,
    SecretHash          CHAR(64) NOT NULL,
    SecretLast4         VARCHAR(4) NOT NULL,
    Label               NVARCHAR(80) NOT NULL,
    RateLimitPerMinute  INT NOT NULL CONSTRAINT DF_IntegrationApiKey_Rate DEFAULT 60,
    IsActive            BIT NOT NULL CONSTRAINT DF_IntegrationApiKey_IsActive DEFAULT 1,
    ExpiresAtUtc        DATETIME2 NULL,
    LastUsedUtc         DATETIME2 NULL,
    CreatedAtUtc        DATETIME2 NOT NULL CONSTRAINT DF_IntegrationApiKey_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT NULL CONSTRAINT FK_IntegrationApiKey_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    RevokedAtUtc        DATETIME2 NULL,
    RevokedBy           INT NULL CONSTRAINT FK_IntegrationApiKey_RevokedBy REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT UQ_IntegrationApiKey_KeyId UNIQUE (KeyId),
    CONSTRAINT CK_IntegrationApiKey_Rate CHECK (RateLimitPerMinute BETWEEN 1 AND 6000)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationApiKey_Integration' AND object_id = OBJECT_ID('dbo.IntegrationApiKey'))
    CREATE INDEX IX_IntegrationApiKey_Integration ON dbo.IntegrationApiKey(TenantId, IntegrationId);
GO
IF OBJECT_ID(N'dbo.IntegrationApiLog', N'U') IS NULL CREATE TABLE dbo.IntegrationApiLog (
    IntegrationApiLogId BIGINT IDENTITY(1,1) CONSTRAINT PK_IntegrationApiLog PRIMARY KEY,
    TenantId            INT NOT NULL CONSTRAINT FK_IntegrationApiLog_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationApiKeyId INT NOT NULL CONSTRAINT FK_IntegrationApiLog_Key REFERENCES dbo.IntegrationApiKey(IntegrationApiKeyId),
    CreatedAtUtc        DATETIME2 NOT NULL CONSTRAINT DF_IntegrationApiLog_Created DEFAULT SYSUTCDATETIME(),
    Method              VARCHAR(8) NOT NULL,
    Path                NVARCHAR(300) NOT NULL,
    StatusCode          INT NOT NULL,
    DurationMs          INT NOT NULL CONSTRAINT DF_IntegrationApiLog_Ms DEFAULT 0
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationApiLog_Key' AND object_id = OBJECT_ID('dbo.IntegrationApiLog'))
    CREATE INDEX IX_IntegrationApiLog_Key ON dbo.IntegrationApiLog(TenantId, IntegrationApiKeyId, IntegrationApiLogId DESC);
GO

/* =========================================================================
   2026-10-19 (bm) — Lote 136 (P11): WEBHOOKS DE SALIDA. dbo.IntegrationWebhook (URL del cliente, eventos, secreto de firma HMAC cifrado, suspensión automática, quitado lógico) y
   dbo.IntegrationWebhookDelivery (cada salida: cuerpo, intentos, siguiente intento; la bandeja de «salidas no entregadas» son las UNDELIVERED; se purga a los 30 días). Solo AMPLÍA. Idempotente.
   ========================================================================= */
IF OBJECT_ID(N'dbo.IntegrationWebhook', N'U') IS NULL CREATE TABLE dbo.IntegrationWebhook (
    IntegrationWebhookId INT IDENTITY(1,1) CONSTRAINT PK_IntegrationWebhook PRIMARY KEY,
    PublicId             UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_IntegrationWebhook_Public DEFAULT NEWID(),
    TenantId             INT NOT NULL CONSTRAINT FK_IntegrationWebhook_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationId        INT NOT NULL CONSTRAINT FK_IntegrationWebhook_Integration REFERENCES dbo.Integration(IntegrationId),
    Url                  NVARCHAR(500) NOT NULL,
    EventsJson           NVARCHAR(400) NOT NULL CONSTRAINT DF_IntegrationWebhook_Events DEFAULT '[]',
    SecretEnc            VARBINARY(400) NOT NULL,
    SecretLast4          VARCHAR(4) NOT NULL,
    IsActive             BIT NOT NULL CONSTRAINT DF_IntegrationWebhook_IsActive DEFAULT 1,
    ConsecutiveFailures  INT NOT NULL CONSTRAINT DF_IntegrationWebhook_Failures DEFAULT 0,
    SuspendedAtUtc       DATETIME2 NULL,
    LastDeliveryUtc      DATETIME2 NULL,
    LastStatus           VARCHAR(12) NULL,
    CreatedAtUtc         DATETIME2 NOT NULL CONSTRAINT DF_IntegrationWebhook_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy            INT NULL CONSTRAINT FK_IntegrationWebhook_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    RemovedAtUtc         DATETIME2 NULL,
    CONSTRAINT UQ_IntegrationWebhook_Public UNIQUE (PublicId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationWebhook_Integration' AND object_id = OBJECT_ID('dbo.IntegrationWebhook'))
    CREATE INDEX IX_IntegrationWebhook_Integration ON dbo.IntegrationWebhook(TenantId, IntegrationId);
GO
IF OBJECT_ID(N'dbo.IntegrationWebhookDelivery', N'U') IS NULL CREATE TABLE dbo.IntegrationWebhookDelivery (
    IntegrationWebhookDeliveryId BIGINT IDENTITY(1,1) CONSTRAINT PK_IntegrationWebhookDelivery PRIMARY KEY,
    TenantId             INT NOT NULL CONSTRAINT FK_IntegrationWebhookDelivery_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationWebhookId INT NOT NULL CONSTRAINT FK_IntegrationWebhookDelivery_Webhook REFERENCES dbo.IntegrationWebhook(IntegrationWebhookId),
    EventId              UNIQUEIDENTIFIER NOT NULL,
    EventType            VARCHAR(40) NOT NULL,
    PayloadJson          NVARCHAR(MAX) NOT NULL,
    Status               VARCHAR(12) NOT NULL CONSTRAINT DF_IntegrationWebhookDelivery_Status DEFAULT 'PENDING',
    AttemptCount         INT NOT NULL CONSTRAINT DF_IntegrationWebhookDelivery_Attempts DEFAULT 0,
    NextAttemptUtc       DATETIME2 NOT NULL CONSTRAINT DF_IntegrationWebhookDelivery_Next DEFAULT SYSUTCDATETIME(),
    LastAttemptUtc       DATETIME2 NULL,
    ResponseCode         INT NULL,
    Error                NVARCHAR(500) NULL,
    CreatedAtUtc         DATETIME2 NOT NULL CONSTRAINT DF_IntegrationWebhookDelivery_Created DEFAULT SYSUTCDATETIME(),
    DeliveredAtUtc       DATETIME2 NULL,
    ResolvedBy           INT NULL CONSTRAINT FK_IntegrationWebhookDelivery_ResolvedBy REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT CK_IntegrationWebhookDelivery_Status CHECK (Status IN ('PENDING', 'DELIVERED', 'UNDELIVERED', 'DISCARDED'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationWebhookDelivery_Due' AND object_id = OBJECT_ID('dbo.IntegrationWebhookDelivery'))
    CREATE INDEX IX_IntegrationWebhookDelivery_Due ON dbo.IntegrationWebhookDelivery(TenantId, Status, NextAttemptUtc);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationWebhookDelivery_Webhook' AND object_id = OBJECT_ID('dbo.IntegrationWebhookDelivery'))
    CREATE INDEX IX_IntegrationWebhookDelivery_Webhook ON dbo.IntegrationWebhookDelivery(TenantId, IntegrationWebhookId, IntegrationWebhookDeliveryId);
GO

/* =========================================================================
   2026-10-19 (bn) — Lote 137 (P11): FORMATO DEL ARCHIVO DE UNA INTEGRACIÓN (hoja de Excel, JSON y XML con mapeo por ruta). dbo.Integration.FormatJson (NULL = CSV como siempre). Solo AMPLÍA. Idempotente.
   ========================================================================= */
IF COL_LENGTH('dbo.Integration', 'FormatJson') IS NULL ALTER TABLE dbo.Integration ADD FormatJson NVARCHAR(MAX) NULL;
GO

/* =========================================================================
   2026-10-19 (bo) — Lote 137b (P11): INTEGRACIONES DE SALIDA. dbo.Integration.OutputJson (qué manda: estatus, disparador, formato, columnas y la API saliente), el conector 'HTTP' (API saliente genérica),
   dbo.IntegrationOutput (cada envío: contenido ya armado, intentos y siguiente intento; los UNDELIVERED son la bandeja de salidas) y dbo.IntegrationOutputOrder (las órdenes que esperan ir en un envío).
   Solo AMPLÍA. Idempotente.
   ========================================================================= */
IF COL_LENGTH('dbo.Integration', 'OutputJson') IS NULL ALTER TABLE dbo.Integration ADD OutputJson NVARCHAR(MAX) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Integration_Connector' AND parent_object_id = OBJECT_ID('dbo.Integration') AND definition NOT LIKE '%''HTTP''%')
    ALTER TABLE dbo.Integration DROP CONSTRAINT CK_Integration_Connector;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Integration_Connector' AND parent_object_id = OBJECT_ID('dbo.Integration'))
    ALTER TABLE dbo.Integration ADD CONSTRAINT CK_Integration_Connector CHECK (ConnectorKind IN ('FOLDER', 'MANUAL', 'EMAIL', 'SFTP', 'FTP', 'API', 'HTTP'));
GO
IF OBJECT_ID(N'dbo.IntegrationOutput', N'U') IS NULL CREATE TABLE dbo.IntegrationOutput (
    IntegrationOutputId BIGINT IDENTITY(1,1) CONSTRAINT PK_IntegrationOutput PRIMARY KEY,
    TenantId       INT NOT NULL CONSTRAINT FK_IntegrationOutput_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationId  INT NOT NULL CONSTRAINT FK_IntegrationOutput_Integration REFERENCES dbo.Integration(IntegrationId),
    FileName       NVARCHAR(160) NOT NULL,
    Format         VARCHAR(4) NOT NULL,
    OrderCount     INT NOT NULL CONSTRAINT DF_IntegrationOutput_Orders DEFAULT 0,
    Content        NVARCHAR(MAX) NOT NULL,
    SizeBytes      INT NOT NULL CONSTRAINT DF_IntegrationOutput_Size DEFAULT 0,
    Status         VARCHAR(12) NOT NULL CONSTRAINT DF_IntegrationOutput_Status DEFAULT 'PENDING',
    AttemptCount   INT NOT NULL CONSTRAINT DF_IntegrationOutput_Attempts DEFAULT 0,
    NextAttemptUtc DATETIME2 NOT NULL CONSTRAINT DF_IntegrationOutput_Next DEFAULT SYSUTCDATETIME(),
    LastAttemptUtc DATETIME2 NULL,
    ResponseCode   INT NULL,
    Error          NVARCHAR(500) NULL,
    CreatedAtUtc   DATETIME2 NOT NULL CONSTRAINT DF_IntegrationOutput_Created DEFAULT SYSUTCDATETIME(),
    SentAtUtc      DATETIME2 NULL,
    ResolvedBy     INT NULL CONSTRAINT FK_IntegrationOutput_ResolvedBy REFERENCES dbo.AspNetUsers(Id),
    CONSTRAINT CK_IntegrationOutput_Status CHECK (Status IN ('PENDING', 'SENT', 'UNDELIVERED', 'DISCARDED')),
    CONSTRAINT CK_IntegrationOutput_Format CHECK (Format IN ('CSV', 'JSON', 'XML'))
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationOutput_Due' AND object_id = OBJECT_ID('dbo.IntegrationOutput'))
    CREATE INDEX IX_IntegrationOutput_Due ON dbo.IntegrationOutput(TenantId, Status, NextAttemptUtc);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationOutput_Integration' AND object_id = OBJECT_ID('dbo.IntegrationOutput'))
    CREATE INDEX IX_IntegrationOutput_Integration ON dbo.IntegrationOutput(TenantId, IntegrationId, IntegrationOutputId);
GO
IF OBJECT_ID(N'dbo.IntegrationOutputOrder', N'U') IS NULL CREATE TABLE dbo.IntegrationOutputOrder (
    IntegrationOutputOrderId BIGINT IDENTITY(1,1) CONSTRAINT PK_IntegrationOutputOrder PRIMARY KEY,
    TenantId            INT NOT NULL CONSTRAINT FK_IntegrationOutputOrder_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationId       INT NOT NULL CONSTRAINT FK_IntegrationOutputOrder_Integration REFERENCES dbo.Integration(IntegrationId),
    TransportOrderId    INT NOT NULL CONSTRAINT FK_IntegrationOutputOrder_Order REFERENCES dbo.TransportOrder(TransportOrderId),
    StatusCode          VARCHAR(30) NOT NULL,
    QueuedAtUtc         DATETIME2 NOT NULL CONSTRAINT DF_IntegrationOutputOrder_Queued DEFAULT SYSUTCDATETIME(),
    IntegrationOutputId BIGINT NULL CONSTRAINT FK_IntegrationOutputOrder_Output REFERENCES dbo.IntegrationOutput(IntegrationOutputId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationOutputOrder_Queue' AND object_id = OBJECT_ID('dbo.IntegrationOutputOrder'))
    CREATE INDEX IX_IntegrationOutputOrder_Queue ON dbo.IntegrationOutputOrder(TenantId, IntegrationId, IntegrationOutputId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IntegrationOutputOrder_Open' AND object_id = OBJECT_ID('dbo.IntegrationOutputOrder'))
    CREATE UNIQUE INDEX UX_IntegrationOutputOrder_Open ON dbo.IntegrationOutputOrder(IntegrationId, TransportOrderId, StatusCode) WHERE IntegrationOutputId IS NULL;
GO

/* ----------------------------------------------------------------------------
   2026-10-20 — Rotacion obligatoria o sugerida en las salidas.
   - dbo.Tenant.RotationEnforcement VARCHAR(10) NOT NULL DEFAULT 'SUGGEST' (CHECK SUGGEST|ENFORCE): SUGGEST = el sistema propone el lote/posicion que
     toca y se puede cambiar (las pantallas advierten); ENFORCE = el sistema escoge solo y el servidor rechaza (409) una posicion o lote distintos.
     SUGGEST para todas las companias existentes (asi era siempre). Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'RotationEnforcement') IS NULL
    ALTER TABLE dbo.Tenant ADD RotationEnforcement VARCHAR(10) NOT NULL CONSTRAINT DF_Tenant_RotationEnforcement DEFAULT 'SUGGEST';
GO
IF OBJECT_ID('dbo.CK_Tenant_RotationEnforcement', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_RotationEnforcement CHECK (RotationEnforcement IN ('SUGGEST', 'ENFORCE'));
GO

/* ----------------------------------------------------------------------------
   2026-10-20 — Avisos de inventario (Lote 141).
   - dbo.Tenant.InventoryExpiryWarnDays INT NOT NULL DEFAULT 30 (1 a 365): dias de anticipacion del aviso de lotes por vencer.
   - dbo.Tenant.InventoryNoMovementDays INT NOT NULL DEFAULT 90 (1 a 3650): dias sin movimientos del aviso de productos sin movimiento.
   Solo AGREGA. Idempotente. Los eventos (INVENTORY_*) y el trabajo INVENTORY_ALERT_SCAN se registran desde codigo: no hay datos que sembrar.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'InventoryExpiryWarnDays') IS NULL
    ALTER TABLE dbo.Tenant ADD InventoryExpiryWarnDays INT NOT NULL CONSTRAINT DF_Tenant_InventoryExpiryWarnDays DEFAULT 30;
GO
IF COL_LENGTH('dbo.Tenant', 'InventoryNoMovementDays') IS NULL
    ALTER TABLE dbo.Tenant ADD InventoryNoMovementDays INT NOT NULL CONSTRAINT DF_Tenant_InventoryNoMovementDays DEFAULT 90;
GO
IF OBJECT_ID('dbo.CK_Tenant_InventoryExpiryWarnDays', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_InventoryExpiryWarnDays CHECK (InventoryExpiryWarnDays BETWEEN 1 AND 365);
GO
IF OBJECT_ID('dbo.CK_Tenant_InventoryNoMovementDays', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_InventoryNoMovementDays CHECK (InventoryNoMovementDays BETWEEN 1 AND 3650);
GO

/* ----------------------------------------------------------------------------
   2026-10-20 — Rotacion por tipo de producto: con lote (FEFO o FIFO) y sin lote ni series (FIFO o por posicion).
   - dbo.Tenant.RotationMethodNoLot VARCHAR(10) NOT NULL DEFAULT 'FIFO' (CHECK FIFO|POSITION): metodo de los productos SIN lote y por serie (no vencen).
     dbo.Tenant.RotationMethod (FEFO|FIFO) pasa a ser el de los productos CON lote; no cambia ningun valor existente.
   - dbo.Product.RotationMethod: la excepcion del producto ahora admite tambien POSITION (CHECK FEFO|FIFO|POSITION). Una excepcion que no aplica al tipo del
     producto (p. ej. FEFO en uno sin lote) se ignora al resolver y manda el de la compania para ese tipo.
   Solo AGREGA o AMPLIA un CHECK. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'RotationMethodNoLot') IS NULL
    ALTER TABLE dbo.Tenant ADD RotationMethodNoLot VARCHAR(10) NOT NULL CONSTRAINT DF_Tenant_RotationMethodNoLot DEFAULT 'FIFO';
GO
IF OBJECT_ID('dbo.CK_Tenant_RotationMethodNoLot', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_RotationMethodNoLot CHECK (RotationMethodNoLot IN ('FIFO', 'POSITION'));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Product_RotationMethod' AND parent_object_id = OBJECT_ID('dbo.Product') AND definition NOT LIKE '%POSITION%')
    ALTER TABLE dbo.Product DROP CONSTRAINT CK_Product_RotationMethod;
GO
IF OBJECT_ID('dbo.CK_Product_RotationMethod', 'C') IS NULL
    ALTER TABLE dbo.Product ADD CONSTRAINT CK_Product_RotationMethod CHECK (RotationMethod IS NULL OR RotationMethod IN ('FEFO', 'FIFO', 'POSITION'));
GO

/* ----------------------------------------------------------------------------
   2026-10-20 — Aviso de salida (Lote 143): el dueño de la mercancia (un cliente de almacen) avisa que quiere que el almacen saque. Sin precio.
   - dbo.ExitNotice, dbo.ExitNoticeLine (reservado / pendiente / despachado) y dbo.ExitNoticeReservation (que saldo reservo cada linea; el ledger reserva por saldo).
   - dbo.PickBatch.ExitNoticeId y dbo.PickBatchLine.ExitNoticeLineId enlazan la recoleccion con el aviso que atiende (NULL = recoleccion normal).
   - Contador EXITNOTICE (SAL-#####, por compania, ClientId NULL): se amplia CK_NumberSequence_Kind. La fila del contador la crea el API.
   - Dominio de estatus ExitNoticeStatus (REQUESTED inicial -> PARTIAL -> SHIPPED; CANCELLED) y EntityType EXIT_NOTICE.
   - Permiso exitnotice.create (crear, modificar, cancelar y atender el pendiente); ver usa inventory.view y despachar warehouse.pick. Se agrega a los roles ya
     clonados (TenantAdmin y WarehouseOperator). Solo AGREGA. Idempotente. El modulo es WMS_LOTSERIAL (ya existe).
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.ExitNotice', N'U') IS NULL CREATE TABLE dbo.ExitNotice (
    ExitNoticeId   INT IDENTITY(1,1) CONSTRAINT PK_ExitNotice PRIMARY KEY,
    PublicId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_ExitNotice_PublicId DEFAULT NEWID(),
    TenantId       INT NOT NULL CONSTRAINT FK_ExitNotice_Tenant REFERENCES dbo.Tenant(TenantId),
    Number         VARCHAR(40) NOT NULL,
    WarehouseId    INT NOT NULL CONSTRAINT FK_ExitNotice_Warehouse REFERENCES dbo.Warehouse(WarehouseId),
    ClientId       INT NOT NULL CONSTRAINT FK_ExitNotice_Client REFERENCES dbo.Client(ClientId),
    Reference      NVARCHAR(80) NULL,
    Destination    NVARCHAR(200) NULL,
    RequestedBy    INT NULL CONSTRAINT FK_ExitNotice_RequestedBy REFERENCES dbo.AspNetUsers(Id),
    RequestedDate  DATE NOT NULL,
    RequiredDate   DATE NULL,
    Notes          NVARCHAR(500) NULL,
    StatusCodeId   INT NOT NULL CONSTRAINT FK_ExitNotice_Status REFERENCES dbo.StatusCode(StatusCodeId),
    RequestedAtUtc DATETIME2 NULL,
    CreatedAtUtc   DATETIME2 NOT NULL CONSTRAINT DF_ExitNotice_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
    CreatedBy      INT NULL CONSTRAINT FK_ExitNotice_CreatedBy REFERENCES dbo.AspNetUsers(Id),
    UpdatedAtUtc   DATETIME2 NULL,
    UpdatedBy      INT NULL CONSTRAINT FK_ExitNotice_UpdatedBy REFERENCES dbo.AspNetUsers(Id),
    RowVersion     ROWVERSION NOT NULL,
    CONSTRAINT UQ_ExitNotice_PublicId UNIQUE (PublicId),
    CONSTRAINT UQ_ExitNotice_Number UNIQUE (TenantId, Number)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ExitNotice_Client' AND object_id = OBJECT_ID('dbo.ExitNotice'))
    CREATE INDEX IX_ExitNotice_Client ON dbo.ExitNotice(TenantId, ClientId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ExitNotice_Status' AND object_id = OBJECT_ID('dbo.ExitNotice'))
    CREATE INDEX IX_ExitNotice_Status ON dbo.ExitNotice(TenantId, StatusCodeId);
GO
IF OBJECT_ID(N'dbo.ExitNoticeLine', N'U') IS NULL CREATE TABLE dbo.ExitNoticeLine (
    ExitNoticeLineId INT IDENTITY(1,1) CONSTRAINT PK_ExitNoticeLine PRIMARY KEY,
    TenantId       INT NOT NULL CONSTRAINT FK_ExitNoticeLine_Tenant REFERENCES dbo.Tenant(TenantId),
    ExitNoticeId   INT NOT NULL CONSTRAINT FK_ExitNoticeLine_Notice REFERENCES dbo.ExitNotice(ExitNoticeId),
    [LineNo]       INT NOT NULL,
    ProductId      INT NOT NULL CONSTRAINT FK_ExitNoticeLine_Product REFERENCES dbo.Product(ProductId),
    LotId          INT NULL CONSTRAINT FK_ExitNoticeLine_Lot REFERENCES dbo.InventoryLot(LotId),
    QtyOrdered     DECIMAL(14,3) NOT NULL,
    QtyReserved    DECIMAL(14,3) NOT NULL CONSTRAINT DF_ExitNoticeLine_QtyReserved DEFAULT 0,
    QtyBackordered DECIMAL(14,3) NOT NULL CONSTRAINT DF_ExitNoticeLine_QtyBackordered DEFAULT 0,
    QtyShipped     DECIMAL(14,3) NOT NULL CONSTRAINT DF_ExitNoticeLine_QtyShipped DEFAULT 0,
    IsClosed       BIT NOT NULL CONSTRAINT DF_ExitNoticeLine_IsClosed DEFAULT 0,
    CONSTRAINT CK_ExitNoticeLine_Qty CHECK (QtyOrdered > 0 AND QtyReserved >= 0 AND QtyBackordered >= 0 AND QtyShipped >= 0)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ExitNoticeLine_No' AND object_id = OBJECT_ID('dbo.ExitNoticeLine'))
    CREATE UNIQUE INDEX UX_ExitNoticeLine_No ON dbo.ExitNoticeLine(ExitNoticeId, [LineNo]);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ExitNoticeLine_Product' AND object_id = OBJECT_ID('dbo.ExitNoticeLine'))
    CREATE INDEX IX_ExitNoticeLine_Product ON dbo.ExitNoticeLine(TenantId, ProductId);
GO
IF OBJECT_ID(N'dbo.ExitNoticeReservation', N'U') IS NULL CREATE TABLE dbo.ExitNoticeReservation (
    ExitNoticeReservationId INT IDENTITY(1,1) CONSTRAINT PK_ExitNoticeReservation PRIMARY KEY,
    TenantId         INT NOT NULL CONSTRAINT FK_ExitNoticeReservation_Tenant REFERENCES dbo.Tenant(TenantId),
    ExitNoticeLineId INT NOT NULL CONSTRAINT FK_ExitNoticeReservation_Line REFERENCES dbo.ExitNoticeLine(ExitNoticeLineId),
    WarehouseId      INT NOT NULL CONSTRAINT FK_ExitNoticeReservation_Warehouse REFERENCES dbo.Warehouse(WarehouseId),
    WarehouseBinId   INT NOT NULL CONSTRAINT FK_ExitNoticeReservation_Bin REFERENCES dbo.WarehouseBin(WarehouseBinId),
    LotId            INT NULL CONSTRAINT FK_ExitNoticeReservation_Lot REFERENCES dbo.InventoryLot(LotId),
    Quantity         DECIMAL(14,3) NOT NULL,
    CONSTRAINT CK_ExitNoticeReservation_Qty CHECK (Quantity > 0)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ExitNoticeReservation_Line' AND object_id = OBJECT_ID('dbo.ExitNoticeReservation'))
    CREATE INDEX IX_ExitNoticeReservation_Line ON dbo.ExitNoticeReservation(ExitNoticeLineId);
GO

IF COL_LENGTH('dbo.PickBatch', 'ExitNoticeId') IS NULL
    ALTER TABLE dbo.PickBatch ADD ExitNoticeId INT NULL CONSTRAINT FK_PickBatch_ExitNotice REFERENCES dbo.ExitNotice(ExitNoticeId);
GO
IF COL_LENGTH('dbo.PickBatchLine', 'ExitNoticeLineId') IS NULL
    ALTER TABLE dbo.PickBatchLine ADD ExitNoticeLineId INT NULL CONSTRAINT FK_PickBatchLine_ExitNoticeLine REFERENCES dbo.ExitNoticeLine(ExitNoticeLineId);
GO

-- Contador EXITNOTICE (SAL-#####)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND parent_object_id = OBJECT_ID('dbo.NumberSequence')
           AND definition NOT LIKE '%EXITNOTICE%')
BEGIN
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE','CREDITNOTE','SALESRETURN','CODREMIT','DRIVERSETTLE','WAYBILL','EXITNOTICE'));
END
GO

-- Dominio de estatus ExitNoticeStatus y sus estatus
MERGE dbo.CatalogDomain AS t
USING (SELECT N'ExitNoticeStatus' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'ExitNoticeStatus', 2, N'{"es":"Estatus del aviso de salida","en":"Exit notice status"}', 1, 1);
GO
MERGE dbo.StatusCode AS t
USING (
    SELECT v.Code, v.LabelJson, v.Color, v.Srt, v.Init, k.LookupCodeId AS StageKindLookupId
    FROM (VALUES
        (N'REQUESTED', N'{"es":"Solicitado","en":"Requested"}',                 '#3B82F6', 1, 1, 'PIPELINE'),
        -- PARTIAL y SHIPPED son LATERALES (como en la orden de venta): deshacer una recoleccion reabre el aviso
        (N'PARTIAL',   N'{"es":"Despachado parcial","en":"Partially shipped"}', '#F59E0B', 2, 0, 'LATERAL'),
        (N'SHIPPED',   N'{"es":"Despachado","en":"Shipped"}',                   '#10B981', 3, 0, 'LATERAL'),
        (N'CANCELLED', N'{"es":"Cancelado","en":"Cancelled"}',                  '#6B7280', 4, 0, 'TERMINAL')
    ) AS v (Code, LabelJson, Color, Srt, Init, Kind)
    JOIN dbo.LookupCode k ON k.Entity = N'StageKind' AND k.InternalCode = v.Kind
) AS s
ON t.Entity = N'ExitNoticeStatus' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (N'ExitNoticeStatus', s.Code, s.LabelJson, s.Color, s.Srt, s.StageKindLookupId, s.Init, 1);
GO

-- EntityType de la bitacora y del historial de estatus
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'EXIT_NOTICE', N'{"es":"Aviso de salida","en":"Exit notice"}', 127)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO

-- Permiso exitnotice.create
MERGE dbo.Permission AS t
USING (VALUES
    ('exitnotice.create', N'{"es":"Crear, modificar y cancelar avisos de salida y atender su pendiente","en":"Create, edit and cancel exit notices and handle their backorder"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'WAREHOUSE'), s.LabelJson, 1);
GO
-- TenantAdmin y operador de almacen (plantilla y roles YA clonados): el PermissionSeeder solo propaga a los roles lo que es nuevo en su corrida
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'exitnotice.create'
WHERE r.Name IN ('TenantAdmin', 'WarehouseOperator')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-20 — El despacho exige documento (Lote 145).
   - dbo.Tenant.DispatchRequirement VARCHAR(15) NOT NULL DEFAULT 'FREE' (CHECK FREE|ANY_DOCUMENT|EXIT_NOTICE|SALES_ORDER): FREE = como siempre; los demas
     rechazan (409) el despacho manual sin documento. Empacar queda fuera. FREE para todas las companias existentes. Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'DispatchRequirement') IS NULL
    ALTER TABLE dbo.Tenant ADD DispatchRequirement VARCHAR(15) NOT NULL CONSTRAINT DF_Tenant_DispatchRequirement DEFAULT 'FREE';
GO
IF OBJECT_ID('dbo.CK_Tenant_DispatchRequirement', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_DispatchRequirement CHECK (DispatchRequirement IN ('FREE', 'ANY_DOCUMENT', 'EXIT_NOTICE', 'SALES_ORDER'));
GO

/* ----------------------------------------------------------------------------
   2026-10-20 — Capa de costo por linea de recibo (Lote 147).
   - dbo.AsnLine.UnitPrice DECIMAL(18,4) NULL: precio unitario opcional del aviso de llegada (solo avisos de un cliente).
   - dbo.ReceiptLine.UnitCost DECIMAL(18,4) NULL y CostSource VARCHAR(10) NULL (PO|ASN|PRODUCT): costo congelado al confirmar el recibo (la linea ES la capa).
     Prioridad: costo de la linea de la orden de compra > precio del aviso > Product.PurchaseCost. Sin sincronizacion en ningun sentido.
   Los recibos ya confirmados quedan sin capa (NULL): no se inventa costo historico. Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.AsnLine', 'UnitPrice') IS NULL
    ALTER TABLE dbo.AsnLine ADD UnitPrice DECIMAL(18,4) NULL;
GO
IF OBJECT_ID('dbo.CK_AsnLine_UnitPrice', 'C') IS NULL
    ALTER TABLE dbo.AsnLine ADD CONSTRAINT CK_AsnLine_UnitPrice CHECK (UnitPrice IS NULL OR UnitPrice >= 0);
GO
IF COL_LENGTH('dbo.ReceiptLine', 'UnitCost') IS NULL
    ALTER TABLE dbo.ReceiptLine ADD UnitCost DECIMAL(18,4) NULL;
GO
IF COL_LENGTH('dbo.ReceiptLine', 'CostSource') IS NULL
    ALTER TABLE dbo.ReceiptLine ADD CostSource VARCHAR(10) NULL;
GO
IF OBJECT_ID('dbo.CK_ReceiptLine_Cost', 'C') IS NULL
    ALTER TABLE dbo.ReceiptLine ADD CONSTRAINT CK_ReceiptLine_Cost CHECK ((UnitCost IS NULL AND CostSource IS NULL) OR (UnitCost >= 0 AND CostSource IN ('PO', 'ASN', 'PRODUCT')));
GO

/* ----------------------------------------------------------------------------
   2026-10-20 — Valuacion del inventario (Lote 148).
   - dbo.Tenant.ValuationMethod VARCHAR(10) NOT NULL DEFAULT 'AVERAGE' (CHECK AVERAGE|LAYERS): costo promedio (por omision) o por capas (lo primero que entro). Con el, el
     costo congelado de las salidas sale de las capas del Lote 147 y el reporte «Valor del inventario» valua lo que hay en mano. AVERAGE para todas las companias existentes.
   - Permiso inventory.value (ver el valor del inventario, con costos): TenantAdmin y Billing (plantilla y roles YA clonados; el PermissionSeeder solo propaga lo nuevo en su corrida).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'ValuationMethod') IS NULL
    ALTER TABLE dbo.Tenant ADD ValuationMethod VARCHAR(10) NOT NULL CONSTRAINT DF_Tenant_ValuationMethod DEFAULT 'AVERAGE';
GO
IF OBJECT_ID('dbo.CK_Tenant_ValuationMethod', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_ValuationMethod CHECK (ValuationMethod IN ('AVERAGE', 'LAYERS'));
GO
MERGE dbo.Permission AS t
USING (VALUES
    ('inventory.value', N'{"es":"Ver el valor del inventario","en":"View inventory value"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'WAREHOUSE'), s.LabelJson, 1);
GO
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'inventory.value'
WHERE r.Name IN ('TenantAdmin', 'Billing')
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
GO

/* ----------------------------------------------------------------------------
   2026-10-20 — El recibo exige documento (Lote 149).
   - dbo.Tenant.ReceiptRequirement VARCHAR(15) NOT NULL DEFAULT 'FREE' (CHECK FREE|ANY_DOCUMENT|ASN|PURCHASE_ORDER): FREE = como siempre; los demas rechazan (409) el recibo
     a ciegas o la devolucion manual sin documento, o contra un documento de otro tipo. Los recibos de una devolucion de venta quedan fuera. FREE para todas las companias existentes.
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'ReceiptRequirement') IS NULL
    ALTER TABLE dbo.Tenant ADD ReceiptRequirement VARCHAR(15) NOT NULL CONSTRAINT DF_Tenant_ReceiptRequirement DEFAULT 'FREE';
GO
IF OBJECT_ID('dbo.CK_Tenant_ReceiptRequirement', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_ReceiptRequirement CHECK (ReceiptRequirement IN ('FREE', 'ANY_DOCUMENT', 'ASN', 'PURCHASE_ORDER'));
GO

/* ----------------------------------------------------------------------------
   2026-10-20 — Permiso de ACCESO por item del menu (Lote 152).
   - Categoria nueva MENU («Acceso al menu», orden 0) con los 44 permisos de entrada, en el orden del menu: los 16 que ya existian y servian (orders.view, trips.scan, trips.view, claims.view, rental.view,
     sales.view, purchasing.view, billing.view, cod.view, driverpay.view, clients.read, locations.read, fleet.view, integrations.view, notifications.send, admin.audit) se MUEVEN a esa categoria con su codigo
     y su etiqueta nueva, y se crean 28 (pulse.view, dispatch.view, warehouses.view, bins.view, products.view, suppliers.view, notices.view, receipts.view, pickbatches.view, adjustments.view, counts.view,
     crossdock.view, kardex.view, purchaseaccounting.view, dispatchaccounting.view, tax.view, drivers.view, reports.view, customfields.view, indicators.view, charts.view, printers.view, users.view,
     devices.view, catalogs.view, notifications.view, jobs.view, settings.view).
   - Nadie pierde un item: cada rol (plantillas y ya clonados) que tenia el permiso que antes daba la entrada recibe los nuevos que lo reemplazan; pulse.view lo reciben todos los roles. Solo AGREGA a los roles.
   Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.LookupCode WHERE Entity = N'PermissionCategory' AND InternalCode = N'MENU')
    INSERT INTO dbo.LookupCode (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'PermissionCategory', N'MENU', N'{"es":"Acceso al menú","en":"Menu access"}', 0, 1, 1);
GO
MERGE dbo.Permission AS t
USING (VALUES
    ('pulse.view', N'{"es":"Pulso del día: entrar y ver","en":"Day pulse: open and view"}'),
    ('orders.view', N'{"es":"Envíos: entrar y ver","en":"Shipments: open and view"}'),
    ('trips.scan', N'{"es":"Estación de escaneo: entrar y ver","en":"Scan station: open and view"}'),
    ('dispatch.view', N'{"es":"Sala de despacho: entrar y ver","en":"Dispatch room: open and view"}'),
    ('trips.view', N'{"es":"Monitoreo de ruta: entrar y ver","en":"Route monitor: open and view"}'),
    ('claims.view', N'{"es":"Reclamaciones: entrar y ver","en":"Claims: open and view"}'),
    ('warehouses.view', N'{"es":"Almacenes: entrar y ver","en":"Warehouses: open and view"}'),
    ('bins.view', N'{"es":"Posiciones: entrar y ver","en":"Bins: open and view"}'),
    ('products.view', N'{"es":"Productos e inventario: entrar y ver","en":"Products & inventory: open and view"}'),
    ('suppliers.view', N'{"es":"Proveedores: entrar y ver","en":"Suppliers: open and view"}'),
    ('notices.view', N'{"es":"Llegadas y salidas: entrar y ver","en":"Arrivals & departures: open and view"}'),
    ('receipts.view', N'{"es":"Recibo: entrar y ver","en":"Receiving: open and view"}'),
    ('pickbatches.view', N'{"es":"Recolección y empaque: entrar y ver","en":"Picking & packing: open and view"}'),
    ('adjustments.view', N'{"es":"Transferencias y ajustes: entrar y ver","en":"Transfers & adjustments: open and view"}'),
    ('counts.view', N'{"es":"Conteo cíclico: entrar y ver","en":"Cycle count: open and view"}'),
    ('crossdock.view', N'{"es":"Cruce de plataforma: entrar y ver","en":"Cross-dock: open and view"}'),
    ('rental.view', N'{"es":"Rentas: entrar y ver","en":"Rentals: open and view"}'),
    ('kardex.view', N'{"es":"Kárdex de movimientos: entrar y ver","en":"Movement ledger: open and view"}'),
    ('purchasing.view', N'{"es":"Compras: entrar y ver","en":"Purchases: open and view"}'),
    ('purchaseaccounting.view', N'{"es":"Contabilización de compras: entrar y ver","en":"Purchase accounting: open and view"}'),
    ('sales.view', N'{"es":"Ventas: entrar y ver","en":"Sales: open and view"}'),
    ('dispatchaccounting.view', N'{"es":"Contabilización de despachos: entrar y ver","en":"Dispatch accounting: open and view"}'),
    ('billing.view', N'{"es":"Facturación: entrar y ver","en":"Billing: open and view"}'),
    ('cod.view', N'{"es":"Procesar entregas: entrar y ver","en":"Process deliveries: open and view"}'),
    ('driverpay.view', N'{"es":"Liquidación a choferes: entrar y ver","en":"Driver settlement: open and view"}'),
    ('tax.view', N'{"es":"Impuestos: entrar y ver","en":"Taxes: open and view"}'),
    ('clients.read', N'{"es":"Clientes y contratos: entrar y ver","en":"Clients & contracts: open and view"}'),
    ('locations.read', N'{"es":"Consignatarios: entrar y ver","en":"Consignees: open and view"}'),
    ('drivers.view', N'{"es":"Choferes y tarifas: entrar y ver","en":"Drivers & rates: open and view"}'),
    ('fleet.view', N'{"es":"Flota y mantenimiento: entrar y ver","en":"Fleet & maintenance: open and view"}'),
    ('reports.view', N'{"es":"Vistas e informes: entrar y ver","en":"Views & reports: open and view"}'),
    ('customfields.view', N'{"es":"Campos personalizados: entrar y ver","en":"Custom fields: open and view"}'),
    ('indicators.view', N'{"es":"Indicadores: entrar y ver","en":"Indicators: open and view"}'),
    ('charts.view', N'{"es":"Gráficos: entrar y ver","en":"Charts: open and view"}'),
    ('printers.view', N'{"es":"Impresoras y labels: entrar y ver","en":"Printers & labels: open and view"}'),
    ('users.view', N'{"es":"Roles y usuarios: entrar y ver","en":"Roles & users: open and view"}'),
    ('devices.view', N'{"es":"Aparatos móviles: entrar y ver","en":"Mobile devices: open and view"}'),
    ('catalogs.view', N'{"es":"Listas de valores: entrar y ver","en":"Value lists: open and view"}'),
    ('integrations.view', N'{"es":"Integraciones / API: entrar y ver","en":"Integrations / API: open and view"}'),
    ('notifications.view', N'{"es":"Reglas de aviso: entrar y ver","en":"Notification rules: open and view"}'),
    ('notifications.send', N'{"es":"Enviar avisos: entrar y ver","en":"Send notices: open and view"}'),
    ('jobs.view', N'{"es":"Trabajos programados: entrar y ver","en":"Scheduled jobs: open and view"}'),
    ('admin.audit', N'{"es":"Seguridad y auditoría: entrar y ver","en":"Security & audit: open and view"}'),
    ('settings.view', N'{"es":"Ajustes de la compañía: entrar y ver","en":"Tenant settings: open and view"}')
) AS s (Code, LabelJson)
ON t.Code = s.Code
WHEN MATCHED THEN
    UPDATE SET LabelJson = s.LabelJson,
               CategoryLookupId = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'MENU')
WHEN NOT MATCHED THEN
    INSERT (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES (s.Code, (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'MENU'), s.LabelJson, 1);
GO
-- 2026-10-21 (Lote 185): estas dos migraciones de datos (acceso por ítem y pulse.view) se aplican UNA SOLA VEZ (marca dbo.AppliedMigration). Antes se repetían en cada db-update y devolvían a un rol el
-- acceso que el administrador le había quitado. En las bases que ya las tenían se aplican una última vez y desde ahí lo quitado se queda quitado.
IF OBJECT_ID(N'dbo.AppliedMigration', N'U') IS NULL CREATE TABLE dbo.AppliedMigration (Code VARCHAR(60) NOT NULL PRIMARY KEY, AppliedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_AppliedMigration_At DEFAULT SYSUTCDATETIME());
GO
IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L152_MENU_GRANTS')
BEGIN
-- el permiso que antes daba la entrada => los de acceso que lo reemplazan (a todos los roles, plantillas y clonados)
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT rp.RoleId, pn.PermissionId
FROM (VALUES
    ('inventory.view', 'warehouses.view'),
    ('inventory.view', 'bins.view'),
    ('inventory.view', 'products.view'),
    ('inventory.view', 'notices.view'),
    ('inventory.view', 'receipts.view'),
    ('inventory.view', 'pickbatches.view'),
    ('inventory.view', 'adjustments.view'),
    ('inventory.view', 'counts.view'),
    ('inventory.view', 'crossdock.view'),
    ('inventory.view', 'kardex.view'),
    ('purchasing.view', 'suppliers.view'),
    ('purchasing.view', 'purchaseaccounting.view'),
    ('trips.dispatch', 'dispatch.view'),
    ('billing.export', 'dispatchaccounting.view'),
    ('tax.manage', 'tax.view'),
    ('fleet.view', 'drivers.view'),
    ('analytics.view', 'reports.view'),
    ('analytics.view', 'indicators.view'),
    ('analytics.view', 'charts.view'),
    ('admin.customfields', 'customfields.view'),
    ('printers.manage', 'printers.view'),
    ('admin.users', 'users.view'),
    ('admin.roles', 'users.view'),
    ('devices.manage', 'devices.view'),
    ('admin.catalogs', 'catalogs.view'),
    ('notifications.manage', 'notifications.view'),
    ('jobs.manage', 'jobs.view'),
    ('admin.tenant', 'settings.view')
) AS g (OldCode, NewCode)
JOIN dbo.Permission po ON po.Code = g.OldCode
JOIN dbo.Permission pn ON pn.Code = g.NewCode
JOIN dbo.RolePermission rp ON rp.PermissionId = po.PermissionId
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = rp.RoleId AND x.PermissionId = pn.PermissionId);
-- pulse.view: todos los roles
INSERT INTO dbo.RolePermission (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN dbo.Permission p ON p.Code = 'pulse.view'
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = r.RoleId AND x.PermissionId = p.PermissionId);
INSERT INTO dbo.AppliedMigration (Code) VALUES ('L152_MENU_GRANTS');
END
GO
/* ----------------------------------------------------------------------------
   2026-10-20 (Lote 156) — Lo que el cliente puede crear solo desde su portal: dbo.Client.PortalCanCreateProducts / PortalCanCreateArrivals / PortalCanCreateExits
   (BIT NOT NULL DEFAULT 0). Las enciende el administrador de la compañía por cliente; apagadas por defecto: ningún cliente cambia de comportamiento. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Client', 'PortalCanCreateProducts') IS NULL
    ALTER TABLE dbo.Client ADD PortalCanCreateProducts BIT NOT NULL CONSTRAINT DF_Client_PortalCanCreateProducts DEFAULT 0;
GO
IF COL_LENGTH('dbo.Client', 'PortalCanCreateArrivals') IS NULL
    ALTER TABLE dbo.Client ADD PortalCanCreateArrivals BIT NOT NULL CONSTRAINT DF_Client_PortalCanCreateArrivals DEFAULT 0;
GO
IF COL_LENGTH('dbo.Client', 'PortalCanCreateExits') IS NULL
    ALTER TABLE dbo.Client ADD PortalCanCreateExits BIT NOT NULL CONSTRAINT DF_Client_PortalCanCreateExits DEFAULT 0;
GO
/* ----------------------------------------------------------------------------
   2026-10-20 (Lote 157) — Vencimiento del lote en la línea del aviso de llegada: dbo.AsnLine.ExpiryDate DATE NULL. Solo con número de lote en un producto por lote; al recibir, el lote nace con
   esa fecha (antes el receptor la capturaba). Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.AsnLine', 'ExpiryDate') IS NULL
    ALTER TABLE dbo.AsnLine ADD ExpiryDate DATE NULL;
GO
/* ----------------------------------------------------------------------------
   2026-10-20 (Lote 162) — Teléfonos: solo dígitos + prefijo internacional aparte (dictado de Luis).
   - dbo.ContactPoint.CountryCallingCode VARCHAR(4) NULL: prefijo del teléfono, solo dígitos y sin «+» ('1', '593'); null en los correos.
   - dbo.Supplier.PhoneCountryCallingCode VARCHAR(4) NULL: lo mismo para su teléfono.
   - Limpieza de lo ya guardado (idempotente: solo toca filas con prefijo vacío y un teléfono que, quitado el formato, son puros dígitos):
     el número queda sin formato y el prefijo es el de la compañía (dbo.Tenant.PhoneCountryCode sin «+»), o el «1» si venía «+1…» o con 11 dígitos
     empezando en 1 (con prefijo 1). Lo que traiga texto (extensión, letras) o un «+» con otro país NO se toca: queda con prefijo vacío para revisión manual.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.ContactPoint', 'CountryCallingCode') IS NULL
    ALTER TABLE dbo.ContactPoint ADD CountryCallingCode VARCHAR(4) NULL;
GO
IF COL_LENGTH('dbo.Supplier', 'PhoneCountryCallingCode') IS NULL
    ALTER TABLE dbo.Supplier ADD PhoneCountryCallingCode VARCHAR(4) NULL;
GO
;WITH P AS (
    SELECT cp.ContactPointId, cp.Value, REPLACE(t.PhoneCountryCode, '+', '') AS DefCc,
           REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(cp.Value)), ' ', ''), '-', ''), '(', ''), ')', ''), '.', ''), '+', '') AS Digits,
           CASE WHEN LTRIM(cp.Value) LIKE '+%' THEN 1 ELSE 0 END AS HasPlus
    FROM dbo.ContactPoint cp
    JOIN dbo.LookupCode lc ON lc.LookupCodeId = cp.ContactTypeLookupId AND lc.Entity = 'ContactType' AND lc.InternalCode IN ('PHONE', 'MOBILE', 'FAX', 'WHATSAPP')
    JOIN dbo.Tenant t ON t.TenantId = cp.TenantId
    WHERE cp.CountryCallingCode IS NULL
), R AS (
    SELECT ContactPointId, Digits, DefCc, HasPlus,
           CASE WHEN HasPlus = 1 AND Digits LIKE '1%' AND LEN(Digits) = 11 THEN '1'
                WHEN HasPlus = 1 AND DefCc <> '1' AND Digits LIKE DefCc + '%' THEN DefCc
                WHEN HasPlus = 0 THEN DefCc END AS Cc
    FROM P
    WHERE Digits <> '' AND Digits NOT LIKE '%[^0-9]%' AND LEN(Digits) BETWEEN 7 AND 15
)
UPDATE cp SET
    CountryCallingCode = R.Cc,
    Value = CASE WHEN R.Cc = '1' AND LEN(R.Digits) = 11 AND R.Digits LIKE '1%' THEN SUBSTRING(R.Digits, 2, 10)
                 WHEN R.HasPlus = 1 THEN SUBSTRING(R.Digits, LEN(R.Cc) + 1, 20)
                 ELSE R.Digits END
FROM dbo.ContactPoint cp
JOIN R ON R.ContactPointId = cp.ContactPointId
WHERE R.Cc IS NOT NULL;
GO
-- Proveedores: mismo criterio que arriba (solo filas con prefijo vacío y un teléfono que, sin formato, son puros dígitos).
;WITH Sp AS (
    SELECT s.SupplierId, REPLACE(t.PhoneCountryCode, '+', '') AS DefCc,
           REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(s.Phone)), ' ', ''), '-', ''), '(', ''), ')', ''), '.', ''), '+', '') AS Digits,
           CASE WHEN LTRIM(s.Phone) LIKE '+%' THEN 1 ELSE 0 END AS HasPlus
    FROM dbo.Supplier s
    JOIN dbo.Tenant t ON t.TenantId = s.TenantId
    WHERE s.PhoneCountryCallingCode IS NULL AND s.Phone IS NOT NULL
), R AS (
    SELECT SupplierId, Digits, HasPlus,
           CASE WHEN HasPlus = 1 AND Digits LIKE '1%' AND LEN(Digits) = 11 THEN '1'
                WHEN HasPlus = 1 AND DefCc <> '1' AND Digits LIKE DefCc + '%' THEN DefCc
                WHEN HasPlus = 0 THEN DefCc END AS Cc
    FROM Sp
    WHERE Digits <> '' AND Digits NOT LIKE '%[^0-9]%' AND LEN(Digits) BETWEEN 7 AND 15
)
UPDATE s SET
    PhoneCountryCallingCode = R.Cc,
    Phone = CASE WHEN R.Cc = '1' AND LEN(R.Digits) = 11 AND R.Digits LIKE '1%' THEN SUBSTRING(R.Digits, 2, 10)
                 WHEN R.HasPlus = 1 THEN SUBSTRING(R.Digits, LEN(R.Cc) + 1, 20)
                 ELSE R.Digits END
FROM dbo.Supplier s
JOIN R ON R.SupplierId = s.SupplierId
WHERE R.Cc IS NOT NULL;
GO
/* ----------------------------------------------------------------------------
   2026-10-20 (Lote 164) — Reclamaciones: el número pasa de REC-##### a RCL-##### (el recibo se queda con REC-#####; antes los dos compartían prefijo).
   Solo renombra las reclamaciones que todavía traen REC-. Idempotente (la segunda corrida no encuentra ninguna).
   ---------------------------------------------------------------------------- */
UPDATE dbo.Claim SET Number = 'RCL-' + SUBSTRING(Number, 5, 40) WHERE Number LIKE 'REC-%';
GO
/* ----------------------------------------------------------------------------
   2026-10-20 (Lote 165) — Estatus del envío en masculino (dictado de Luis: «orden» pasa a «envío»): Sometido, Recogido, Asignado, Entregado, Cancelado, Devuelto.
   Solo cambia la etiqueta en español (LabelJson); los códigos y las demás columnas no se tocan. Idempotente. «Sin asignar», «En tránsito», «Intento fallido» y «En almacén» no cambian.
   ---------------------------------------------------------------------------- */
UPDATE dbo.StatusCode SET LabelJson = N'{"es":"Sometido","en":"Submitted"}'  WHERE Entity = 'OrderStatus' AND InternalCode = 'SUBMITTED' AND LabelJson <> N'{"es":"Sometido","en":"Submitted"}';
UPDATE dbo.StatusCode SET LabelJson = N'{"es":"Recogido","en":"Picked up"}'    WHERE Entity = 'OrderStatus' AND InternalCode = 'PICKUP' AND LabelJson <> N'{"es":"Recogido","en":"Picked up"}';
UPDATE dbo.StatusCode SET LabelJson = N'{"es":"Asignado","en":"Assigned"}'     WHERE Entity = 'OrderStatus' AND InternalCode = 'ASSIGNED' AND LabelJson <> N'{"es":"Asignado","en":"Assigned"}';
UPDATE dbo.StatusCode SET LabelJson = N'{"es":"Entregado","en":"Delivered"}'   WHERE Entity = 'OrderStatus' AND InternalCode = 'DELIVERED' AND LabelJson <> N'{"es":"Entregado","en":"Delivered"}';
UPDATE dbo.StatusCode SET LabelJson = N'{"es":"Cancelado","en":"Cancelled"}'   WHERE Entity = 'OrderStatus' AND InternalCode = 'CANCELLED' AND LabelJson <> N'{"es":"Cancelado","en":"Cancelled"}';
UPDATE dbo.StatusCode SET LabelJson = N'{"es":"Devuelto","en":"Returned"}'     WHERE Entity = 'OrderStatus' AND InternalCode = 'RETURNED' AND LabelJson <> N'{"es":"Devuelto","en":"Returned"}';
GO
/* ----------------------------------------------------------------------------
   2026-10-20 (Lote 166) — Nombres sembrados del sistema en «envío» (dictado de Luis): el reporte «Órdenes» y los indicadores y gráficos de sistema de transporte cambian de nombre y descripción.
   Solo toca filas de sistema (IsSystem = 1) y no pisa un nombre que la compañía ya tenga. Idempotente: la segunda corrida no encuentra los nombres viejos.
   ---------------------------------------------------------------------------- */
UPDATE d SET d.Name = N'Envíos', d.DescriptionJson = N'{"es":"Envíos de transporte con empaque, consignatario, estatus y COD","en":"Transport shipments with pack batch, consignee, status and COD"}' FROM dbo.ReportDefinition d WHERE d.IsSystem = 1 AND d.Name = N'Órdenes' AND NOT EXISTS (SELECT 1 FROM dbo.ReportDefinition x WHERE x.TenantId = d.TenantId AND x.Name = N'Envíos');
UPDATE d SET d.Name = N'Envíos en curso', d.DescriptionJson = N'{"es":"Envíos activos confirmados y aún no entregados","en":"Active shipments confirmed and not yet delivered"}' FROM dbo.IndicatorDefinition d WHERE d.IsSystem = 1 AND d.Name = N'Órdenes en curso' AND NOT EXISTS (SELECT 1 FROM dbo.IndicatorDefinition x WHERE x.TenantId = d.TenantId AND x.Name = N'Envíos en curso');
UPDATE d SET d.Name = N'Envíos sin chofer asignado', d.DescriptionJson = N'{"es":"Envíos activos pendientes de despacho sin ruta con chofer ni entrega especial asignada","en":"Active shipments pending dispatch without a driver"}' FROM dbo.IndicatorDefinition d WHERE d.IsSystem = 1 AND d.Name = N'Órdenes sin chofer asignado' AND NOT EXISTS (SELECT 1 FROM dbo.IndicatorDefinition x WHERE x.TenantId = d.TenantId AND x.Name = N'Envíos sin chofer asignado');
UPDATE d SET d.Name = N'Envíos en excepción', d.DescriptionJson = N'{"es":"Envíos activos en espera, entrega parcial o fallida","en":"Active shipments on hold, partial or failed"}' FROM dbo.IndicatorDefinition d WHERE d.IsSystem = 1 AND d.Name = N'Órdenes en excepción' AND NOT EXISTS (SELECT 1 FROM dbo.IndicatorDefinition x WHERE x.TenantId = d.TenantId AND x.Name = N'Envíos en excepción');
UPDATE d SET d.Name = N'Envíos con SLA en riesgo', d.DescriptionJson = N'{"es":"Envíos abiertos a punto de pasar el SLA de su contrato o que ya lo pasaron","en":"Open shipments about to miss their contract SLA or already past it"}' FROM dbo.IndicatorDefinition d WHERE d.IsSystem = 1 AND d.Name = N'Órdenes con SLA en riesgo' AND NOT EXISTS (SELECT 1 FROM dbo.IndicatorDefinition x WHERE x.TenantId = d.TenantId AND x.Name = N'Envíos con SLA en riesgo');
UPDATE d SET d.Name = N'Envíos por estatus', d.DescriptionJson = N'{"es":"Distribución de los envíos de los últimos 30 días por estatus","en":"Shipments of the last 30 days by status"}' FROM dbo.ChartDefinition d WHERE d.IsSystem = 1 AND d.Name = N'Órdenes por estatus' AND NOT EXISTS (SELECT 1 FROM dbo.ChartDefinition x WHERE x.TenantId = d.TenantId AND x.Name = N'Envíos por estatus');
UPDATE d SET d.Name = N'Envíos por estado del SLA', d.DescriptionJson = N'{"es":"Envíos de los últimos 30 días con SLA, por estado (a tiempo, en riesgo, vencido, cumplido, fuera de SLA)","en":"Shipments of the last 30 days with an SLA, by state (on track, at risk, past due, met, missed)"}' FROM dbo.ChartDefinition d WHERE d.IsSystem = 1 AND d.Name = N'Órdenes por estado del SLA' AND NOT EXISTS (SELECT 1 FROM dbo.ChartDefinition x WHERE x.TenantId = d.TenantId AND x.Name = N'Envíos por estado del SLA');
UPDATE dbo.IndicatorDefinition SET DescriptionJson = N'{"es":"Suma del COD pendiente de cobro de los envíos activos no cancelados","en":"Sum of pending COD of active, non-cancelled shipments"}' WHERE IsSystem = 1 AND Name = N'COD por cobrar' AND DescriptionJson <> N'{"es":"Suma del COD pendiente de cobro de los envíos activos no cancelados","en":"Sum of pending COD of active, non-cancelled shipments"}';
UPDATE dbo.IndicatorDefinition SET DescriptionJson = N'{"es":"Porcentaje de los envíos entregados en los últimos 30 días (con SLA) que se entregaron dentro del plazo de su contrato","en":"Share of shipments delivered in the last 30 days (with an SLA) delivered within their contract deadline"}' WHERE IsSystem = 1 AND Name = N'Entregas dentro del SLA' AND DescriptionJson <> N'{"es":"Porcentaje de los envíos entregados en los últimos 30 días (con SLA) que se entregaron dentro del plazo de su contrato","en":"Share of shipments delivered in the last 30 days (with an SLA) delivered within their contract deadline"}';
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 167) — Búsqueda global («Buscar o ejecutar»): índices para encontrar por prefijo sin recorrer la tabla.
   - dbo.ContactPoint (Value) para buscar por teléfono (los teléfonos se guardan solo con dígitos).
   - dbo.WarehouseBin (Code) para buscar una posición por código sin saber su almacén ni su zona.
   Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ContactPoint_Value' AND object_id = OBJECT_ID('dbo.ContactPoint'))
    CREATE INDEX IX_ContactPoint_Value ON dbo.ContactPoint (Value) WHERE IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WarehouseBin_Code' AND object_id = OBJECT_ID('dbo.WarehouseBin'))
    CREATE INDEX IX_WarehouseBin_Code ON dbo.WarehouseBin (Code) WHERE IsRemoved = 0;
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 168) — Avisos de flota (documentos de vehiculos y choferes por vencer o vencidos; mantenimiento proximo o vencido).
   - dbo.Tenant.FleetDocWarnDays INT NOT NULL DEFAULT 30 (1 a 365): dias de anticipacion del aviso de documentos por vencer.
   Solo AGREGA. Idempotente. Los eventos (FLEET_*) y el trabajo FLEET_ALERT_SCAN se registran desde codigo: no hay datos que sembrar.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Tenant', 'FleetDocWarnDays') IS NULL
    ALTER TABLE dbo.Tenant ADD FleetDocWarnDays INT NOT NULL CONSTRAINT DF_Tenant_FleetDocWarnDays DEFAULT 30;
GO
IF OBJECT_ID('dbo.CK_Tenant_FleetDocWarnDays', 'C') IS NULL
    ALTER TABLE dbo.Tenant ADD CONSTRAINT CK_Tenant_FleetDocWarnDays CHECK (FleetDocWarnDays BETWEEN 1 AND 365);
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (pendiente de la migración de Colavora §3) — Descripción larga de la línea de carga.
   - dbo.CargoLine.Description pasa de NVARCHAR(250) a NVARCHAR(MAX): el contenido de un envío (p. ej. los ítems de AxisCare, decenas por orden)
     va dentro de la descripción de la línea del paquete, no como una línea por ítem. Ningún índice incluye la columna.
   Idempotente: solo cambia si todavía no es MAX.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.CargoLine', 'Description') <> -1
    ALTER TABLE dbo.CargoLine ALTER COLUMN Description NVARCHAR(MAX) NOT NULL;
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 170) — Numero del aviso de llegada (A5): LLE-##### por compania.
   - dbo.Asn.Number NVARCHAR(40) NOT NULL, unico por compania (UX_Asn_Number). Los avisos que ya existian se numeran en orden de creacion (por compania).
   - Contador ASN en dbo.NumberSequence (ClientId NULL): se amplia CK_NumberSequence_Kind y, para las companias con avisos, la fila del contador queda lista para seguir despues del ultimo numero.
   Solo AGREGA y completa. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Asn', 'Number') IS NULL
    ALTER TABLE dbo.Asn ADD Number NVARCHAR(40) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND parent_object_id = OBJECT_ID('dbo.NumberSequence')
           AND definition NOT LIKE '%''ASN''%')
BEGIN
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE','CREDITNOTE','SALESRETURN','CODREMIT','DRIVERSETTLE','WAYBILL','EXITNOTICE','ASN'));
END
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Asn') AND name = 'Number' AND is_nullable = 1)
BEGIN
    -- los avisos existentes, en orden de creacion dentro de cada compania
    ;WITH n AS (SELECT AsnId, ROW_NUMBER() OVER (PARTITION BY TenantId ORDER BY AsnId) AS rn FROM dbo.Asn WHERE Number IS NULL)
    UPDATE a SET a.Number = 'LLE-' + RIGHT('00000' + CAST(n.rn AS VARCHAR(10)), 5)
    FROM dbo.Asn a JOIN n ON n.AsnId = a.AsnId;

    -- el contador de cada compania sigue despues del ultimo numero
    INSERT INTO dbo.NumberSequence (TenantId, Kind, ClientId, NextValue)
    SELECT a.TenantId, 'ASN', NULL, COUNT(*) + 1
    FROM dbo.Asn a
    WHERE NOT EXISTS (SELECT 1 FROM dbo.NumberSequence s WHERE s.TenantId = a.TenantId AND s.Kind = 'ASN' AND s.ClientId IS NULL)
    GROUP BY a.TenantId;

    ALTER TABLE dbo.Asn ALTER COLUMN Number NVARCHAR(40) NOT NULL;
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Asn_Number' AND object_id = OBJECT_ID('dbo.Asn'))
    CREATE UNIQUE INDEX UX_Asn_Number ON dbo.Asn (TenantId, Number);
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 171) — Asignar el aviso de llegada y el aviso de salida a una persona (B2): «Mis tareas» de la app.
   - dbo.Asn.AssignedToUserId INT NULL y dbo.ExitNotice.AssignedToUserId INT NULL (quien lo va a recibir / surtir; NULL = sin asignar).
   Solo AGREGA. Idempotente. Con llave foranea a dbo.AspNetUsers (igual que dbo.WarehouseTask.AssignedToUserId).
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Asn', 'AssignedToUserId') IS NULL
    ALTER TABLE dbo.Asn ADD AssignedToUserId INT NULL CONSTRAINT FK_Asn_AssignedTo REFERENCES dbo.AspNetUsers(Id);
GO
IF COL_LENGTH('dbo.ExitNotice', 'AssignedToUserId') IS NULL
    ALTER TABLE dbo.ExitNotice ADD AssignedToUserId INT NULL CONSTRAINT FK_ExitNotice_AssignedTo REFERENCES dbo.AspNetUsers(Id);
GO
/* ----------------------------------------------------------------------------
   2026-10-21 — Servicio y paquete por omisión de cada cliente (entrada rápida de envíos, dictado de Luis).
   - dbo.Client.DefaultServiceTypeLookupId / DefaultPackageTypeLookupId INT NULL → dbo.LookupCode: lo que la entrada rápida ya trae puesto al elegir
     el cliente (NULL = los de la compañía: Tenant.DefaultServiceTypeLookupId / DefaultPackageTypeLookupId).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Client', 'DefaultServiceTypeLookupId') IS NULL
    ALTER TABLE dbo.Client ADD DefaultServiceTypeLookupId INT NULL CONSTRAINT FK_Client_DefaultServiceType REFERENCES dbo.LookupCode(LookupCodeId);
GO
IF COL_LENGTH('dbo.Client', 'DefaultPackageTypeLookupId') IS NULL
    ALTER TABLE dbo.Client ADD DefaultPackageTypeLookupId INT NULL CONSTRAINT FK_Client_DefaultPackageType REFERENCES dbo.LookupCode(LookupCodeId);
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 174) — Recibo con tandas (C2): confirmaciones parciales de un recibo (el camión que llega por partes).
   - dbo.ReceiptLine.PostedQty DECIMAL(16,3) NOT NULL DEFAULT 0: lo ya asentado en el inventario por tandas.
   - dbo.ReceiptBatch: una fila por tanda (numero consecutivo por recibo, quien y cuando, lineas y unidades).
   Los recibos ya confirmados quedan con PostedQty = 0 y sin tandas (se comportan como siempre). Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.ReceiptLine', 'PostedQty') IS NULL
    ALTER TABLE dbo.ReceiptLine ADD PostedQty DECIMAL(16,3) NOT NULL CONSTRAINT DF_ReceiptLine_PostedQty DEFAULT 0;
GO
IF OBJECT_ID('dbo.ReceiptBatch', 'U') IS NULL CREATE TABLE dbo.ReceiptBatch (
    ReceiptBatchId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReceiptBatch PRIMARY KEY,
    ReceiptHeaderId INT NOT NULL CONSTRAINT FK_ReceiptBatch_Receipt REFERENCES dbo.ReceiptHeader(ReceiptHeaderId),
    BatchNumber     INT NOT NULL,
    ConfirmedAtUtc  DATETIME2(3) NOT NULL,
    ConfirmedBy     INT NULL,
    Comment         NVARCHAR(300) NULL,
    LineCount       INT NOT NULL,
    UnitCount       DECIMAL(16,3) NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReceiptBatch_Number' AND object_id = OBJECT_ID('dbo.ReceiptBatch'))
    CREATE UNIQUE INDEX UX_ReceiptBatch_Number ON dbo.ReceiptBatch (ReceiptHeaderId, BatchNumber);
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 175) — Recibir por partes desde el aparato: dbo.ReceiptBatch.ClientKey UNIQUEIDENTIFIER NULL (id del envio de la cola; el reintento
   del mismo envio no suma dos veces; unico donde no es NULL). Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.ReceiptBatch', 'ClientKey') IS NULL
    ALTER TABLE dbo.ReceiptBatch ADD ClientKey UNIQUEIDENTIFIER NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReceiptBatch_ClientKey' AND object_id = OBJECT_ID('dbo.ReceiptBatch'))
    CREATE UNIQUE INDEX UX_ReceiptBatch_ClientKey ON dbo.ReceiptBatch (ClientKey) WHERE ClientKey IS NOT NULL;
GO
/* ----------------------------------------------------------------------------
   2026-10-21 — Label de FACTURA (dictado de Luis): las plantillas de label tienen un TIPO.
   - dbo.LabelTemplate.Kind VARCHAR(10) NOT NULL DEFAULT 'PIECE' (PIECE = label de cada pieza; INVOICE = label de la factura: un código de barras con el número de
     la factura, uno por envío). Las que ya existen son PIECE.
   - UX_LabelTemplate_Name pasa a (TenantId, Kind, Name) y UX_LabelTemplate_Default a (TenantId, Kind): el nombre es único por tipo y hay UNA predeterminada por
     compañía y tipo (la de pieza y la de factura).
   Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.LabelTemplate', 'Kind') IS NULL
    ALTER TABLE dbo.LabelTemplate ADD Kind VARCHAR(10) NOT NULL CONSTRAINT DF_LabelTemplate_Kind DEFAULT 'PIECE';
GO
IF OBJECT_ID('dbo.CK_LabelTemplate_Kind', 'C') IS NULL
    ALTER TABLE dbo.LabelTemplate ADD CONSTRAINT CK_LabelTemplate_Kind CHECK (Kind IN ('PIECE', 'INVOICE'));
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LabelTemplate_Name' AND object_id = OBJECT_ID('dbo.LabelTemplate'))
   AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE i.name = 'UX_LabelTemplate_Name' AND i.object_id = OBJECT_ID('dbo.LabelTemplate') AND c.name = 'Kind')
    DROP INDEX UX_LabelTemplate_Name ON dbo.LabelTemplate;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LabelTemplate_Name' AND object_id = OBJECT_ID('dbo.LabelTemplate'))
    CREATE UNIQUE INDEX UX_LabelTemplate_Name ON dbo.LabelTemplate(TenantId, Kind, Name) WHERE IsActive = 1;
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LabelTemplate_Default' AND object_id = OBJECT_ID('dbo.LabelTemplate'))
   AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                   WHERE i.name = 'UX_LabelTemplate_Default' AND i.object_id = OBJECT_ID('dbo.LabelTemplate') AND c.name = 'Kind')
    DROP INDEX UX_LabelTemplate_Default ON dbo.LabelTemplate;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_LabelTemplate_Default' AND object_id = OBJECT_ID('dbo.LabelTemplate'))
    CREATE UNIQUE INDEX UX_LabelTemplate_Default ON dbo.LabelTemplate(TenantId, Kind) WHERE IsDefault = 1 AND IsActive = 1;
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 176) — Recoleccion maestra (C3/C4): varios documentos (ordenes de venta y/o avisos de salida) DEL MISMO CLIENTE y almacen que se recolectan juntos y salen
   en UN solo envio.
   - dbo.PickGroup: el documento maestro (numero EMP-##### del contador PACKBATCH = el del envio, almacen, cliente, nota, quien/cuando, el envio al empacar).
   - dbo.PickBatch.PickGroupId INT NULL -> dbo.PickGroup: cada documento conserva su recoleccion; las del grupo comparten el envio, asi que UX_PickBatch_Order se recrea
     sin las del grupo (PickGroupId IS NULL) y el envio unico del grupo es UX_PickGroup_Order.
   Los datos existentes no cambian (ninguna recoleccion tiene grupo). Solo AGREGA (el indice se recrea con la misma regla). Idempotente.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.PickGroup', 'U') IS NULL CREATE TABLE dbo.PickGroup (
    PickGroupId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PickGroup PRIMARY KEY,
    PublicId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_PickGroup_PublicId DEFAULT NEWID(),
    TenantId         INT NOT NULL,
    WarehouseId      INT NOT NULL,
    ClientId         INT NOT NULL,
    Number           NVARCHAR(40) NOT NULL,
    Note             NVARCHAR(500) NULL,
    CollectedAtUtc   DATETIME2 NOT NULL CONSTRAINT DF_PickGroup_CollectedAt DEFAULT SYSUTCDATETIME(),
    CollectedBy      INT NULL,
    TransportOrderId INT NULL,
    PackedAtUtc      DATETIME2 NULL,
    PackedBy         INT NULL,
    IsActive         BIT NOT NULL CONSTRAINT DF_PickGroup_IsActive DEFAULT 1,
    RowVersion       ROWVERSION,
    CONSTRAINT UQ_PickGroup_Number UNIQUE (TenantId, Number),
    CONSTRAINT UQ_PickGroup_IdTenant UNIQUE (PickGroupId, TenantId),
    CONSTRAINT FK_PickGroup_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId),
    CONSTRAINT FK_PickGroup_Order FOREIGN KEY (TransportOrderId, TenantId) REFERENCES dbo.TransportOrder(TransportOrderId, TenantId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PickGroup_Order' AND object_id = OBJECT_ID('dbo.PickGroup'))
    CREATE UNIQUE INDEX UX_PickGroup_Order ON dbo.PickGroup (TransportOrderId) WHERE TransportOrderId IS NOT NULL AND IsActive = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PickGroup_Tenant_Date' AND object_id = OBJECT_ID('dbo.PickGroup'))
    CREATE INDEX IX_PickGroup_Tenant_Date ON dbo.PickGroup (TenantId, CollectedAtUtc);
GO
IF COL_LENGTH('dbo.PickBatch', 'PickGroupId') IS NULL
    ALTER TABLE dbo.PickBatch ADD PickGroupId INT NULL CONSTRAINT FK_PickBatch_Group REFERENCES dbo.PickGroup(PickGroupId);
GO
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PickBatch_Order' AND object_id = OBJECT_ID('dbo.PickBatch') AND filter_definition NOT LIKE '%PickGroupId%')
    DROP INDEX UX_PickBatch_Order ON dbo.PickBatch;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PickBatch_Order' AND object_id = OBJECT_ID('dbo.PickBatch'))
    CREATE UNIQUE INDEX UX_PickBatch_Order ON dbo.PickBatch (TransportOrderId) WHERE TransportOrderId IS NOT NULL AND IsActive = 1 AND PickGroupId IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PickBatch_Group' AND object_id = OBJECT_ID('dbo.PickBatch'))
    CREATE INDEX IX_PickBatch_Group ON dbo.PickBatch (PickGroupId) WHERE PickGroupId IS NOT NULL;
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 181, H1) — Contabilización de compras y de despachos.
   - dbo.AccountingRun / dbo.AccountingRunLine: la corrida (CMP-##### de compras, DSP-##### de despachos) y sus líneas, que copian el costo ya congelado
     (capa del recibo; costo de la salida). El índice único filtrado UX_AccountingRunLine_Source impide que una línea esté en dos corridas vivas.
   - Dominio de estatus AccountingRunStatus (GENERATED, ACCEPTED, EXPORTED, CANCELLED) y EntityType ACCOUNTING_RUN.
   - Contadores ACCTPURCH y ACCTDISP (CK_NumberSequence_Kind se amplía).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.AccountingRun', N'U') IS NULL CREATE TABLE dbo.AccountingRun (
    AccountingRunId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_AccountingRun_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    Kind VARCHAR(10) NOT NULL CONSTRAINT CK_AccountingRun_Kind CHECK (Kind IN ('PURCHASES','DISPATCHES')),
    Number NVARCHAR(40) NOT NULL,
    PeriodStart DATE NOT NULL,
    PeriodEnd DATE NOT NULL,
    StatusCodeId INT NOT NULL REFERENCES dbo.StatusCode(StatusCodeId),
    CurrencyLookupId INT NOT NULL REFERENCES dbo.LookupCode(LookupCodeId),
    LineCount INT NOT NULL CONSTRAINT DF_AccountingRun_LineCount DEFAULT 0,
    TotalAmount DECIMAL(18,4) NOT NULL CONSTRAINT DF_AccountingRun_Total DEFAULT 0,
    UnpricedLineCount INT NOT NULL CONSTRAINT DF_AccountingRun_Unpriced DEFAULT 0,
    Note NVARCHAR(500) NULL,
    GeneratedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_AccountingRun_Generated DEFAULT SYSUTCDATETIME(),
    GeneratedBy INT NULL,
    AcceptedAtUtc DATETIME2 NULL, AcceptedBy INT NULL,
    ExportedAtUtc DATETIME2 NULL, ExportedBy INT NULL,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT CK_AccountingRun_Period CHECK (PeriodEnd >= PeriodStart)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccountingRun_PublicId' AND object_id = OBJECT_ID('dbo.AccountingRun')) CREATE INDEX IX_AccountingRun_PublicId ON dbo.AccountingRun(PublicId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_AccountingRun_Number' AND object_id = OBJECT_ID('dbo.AccountingRun')) CREATE UNIQUE INDEX UQ_AccountingRun_Number ON dbo.AccountingRun(TenantId, Number);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccountingRun_Kind_Status' AND object_id = OBJECT_ID('dbo.AccountingRun')) CREATE INDEX IX_AccountingRun_Kind_Status ON dbo.AccountingRun(TenantId, Kind, StatusCodeId);
GO
IF OBJECT_ID(N'dbo.AccountingRunLine', N'U') IS NULL CREATE TABLE dbo.AccountingRunLine (
    AccountingRunLineId INT IDENTITY(1,1) PRIMARY KEY,
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    AccountingRunId INT NOT NULL REFERENCES dbo.AccountingRun(AccountingRunId),
    SourceType VARCHAR(12) NOT NULL CONSTRAINT CK_AccountingRunLine_Source CHECK (SourceType IN ('RECEIPT_LINE','PICK_LINE')),
    SourceId INT NOT NULL,
    DocDate DATE NOT NULL,
    DocumentNumber NVARCHAR(60) NOT NULL,
    ReferenceNumber NVARCHAR(60) NULL,
    PartyName NVARCHAR(200) NULL,
    WarehouseCode NVARCHAR(20) NULL,
    ProductId INT NOT NULL REFERENCES dbo.Product(ProductId),
    Sku NVARCHAR(60) NOT NULL,
    ProductName NVARCHAR(200) NOT NULL,
    LotNumber NVARCHAR(60) NULL,
    Quantity DECIMAL(16,3) NOT NULL,
    UnitCost DECIMAL(18,4) NULL,
    TotalCost DECIMAL(18,4) NULL,
    CostSource NVARCHAR(10) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_AccountingRunLine_IsActive DEFAULT 1
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AccountingRunLine_Run' AND object_id = OBJECT_ID('dbo.AccountingRunLine')) CREATE INDEX IX_AccountingRunLine_Run ON dbo.AccountingRunLine(AccountingRunId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_AccountingRunLine_Source' AND object_id = OBJECT_ID('dbo.AccountingRunLine'))
    CREATE UNIQUE INDEX UX_AccountingRunLine_Source ON dbo.AccountingRunLine (SourceType, SourceId) WHERE IsActive = 1;
GO
MERGE dbo.CatalogDomain AS t
USING (SELECT N'AccountingRunStatus' AS DomainKey) AS s ON t.DomainKey = s.DomainKey
WHEN NOT MATCHED THEN
    INSERT (DomainKey, Scope, LabelJson, IsSystem, IsActive)
    VALUES (N'AccountingRunStatus', 2, N'{"es":"Estatus de la corrida de contabilización","en":"Accounting run status"}', 1, 1);
GO
MERGE dbo.StatusCode AS t
USING (
    SELECT v.Code, v.LabelJson, v.Color, v.Srt, v.Init, k.LookupCodeId AS StageKindLookupId
    FROM (VALUES
        (N'GENERATED', N'{"es":"Generada","en":"Generated"}',  '#9CA3AF', 1, 1, 'PIPELINE'),
        (N'ACCEPTED',  N'{"es":"Aceptada","en":"Accepted"}',   '#3B82F6', 2, 0, 'PIPELINE'),
        (N'EXPORTED',  N'{"es":"Exportada","en":"Exported"}',  '#059669', 3, 0, 'TERMINAL'),
        (N'CANCELLED', N'{"es":"Cancelada","en":"Cancelled"}', '#6B7280', 4, 0, 'TERMINAL')
    ) AS v (Code, LabelJson, Color, Srt, Init, Kind)
    JOIN dbo.LookupCode k ON k.Entity = N'StageKind' AND k.InternalCode = v.Kind
) AS s
ON t.Entity = N'AccountingRunStatus' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, ColorHex, SortOrder, StageKindLookupId, IsInitial, IsActive)
    VALUES (N'AccountingRunStatus', s.Code, s.LabelJson, s.Color, s.Srt, s.StageKindLookupId, s.Init, 1);
GO
MERGE dbo.LookupCode AS t
USING (VALUES
    (N'ACCOUNTING_RUN', N'{"es":"Corrida de contabilización","en":"Accounting run"}', 175)
) AS s (Code, LabelJson, Srt)
ON t.Entity = N'EntityType' AND t.InternalCode = s.Code
WHEN NOT MATCHED THEN
    INSERT (Entity, InternalCode, LabelJson, SortOrder, IsSystem, IsActive)
    VALUES (N'EntityType', s.Code, s.LabelJson, s.Srt, 1, 1);
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND parent_object_id = OBJECT_ID('dbo.NumberSequence')
           AND definition NOT LIKE '%''ACCTDISP''%')
BEGIN
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE','CREDITNOTE','SALESRETURN','CODREMIT','DRIVERSETTLE','WAYBILL','EXITNOTICE','ASN','ACCTPURCH','ACCTDISP'));
END
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 182, C4) — Recepción maestra: varios documentos del mismo dueño que llegan juntos.
   - dbo.ReceiptGroup (RCM-#####): la maestra; dbo.ReceiptHeader.ReceiptGroupId la liga a sus recibos (cada documento conserva su recibo de siempre).
   - Contador RECEIPTGROUP (CK_NumberSequence_Kind se amplía).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.ReceiptGroup', N'U') IS NULL CREATE TABLE dbo.ReceiptGroup (
    ReceiptGroupId INT IDENTITY(1,1) PRIMARY KEY,
    PublicId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_ReceiptGroup_PublicId DEFAULT NEWID(),
    TenantId INT NOT NULL REFERENCES dbo.Tenant(TenantId),
    WarehouseId INT NOT NULL,
    ClientId INT NULL,
    Number NVARCHAR(40) NOT NULL,
    Note NVARCHAR(500) NULL,
    CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ReceiptGroup_Created DEFAULT SYSUTCDATETIME(),
    CreatedBy INT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_ReceiptGroup_IsActive DEFAULT 1,
    RowVersion ROWVERSION NOT NULL,
    CONSTRAINT UQ_ReceiptGroup_Number UNIQUE (TenantId, Number),
    CONSTRAINT UQ_ReceiptGroup_IdTenant UNIQUE (ReceiptGroupId, TenantId),
    CONSTRAINT FK_ReceiptGroup_Warehouse FOREIGN KEY (WarehouseId, TenantId) REFERENCES dbo.Warehouse(WarehouseId, TenantId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReceiptGroup_Tenant_Date' AND object_id = OBJECT_ID('dbo.ReceiptGroup')) CREATE INDEX IX_ReceiptGroup_Tenant_Date ON dbo.ReceiptGroup (TenantId, CreatedAtUtc);
GO
IF COL_LENGTH('dbo.ReceiptHeader', 'ReceiptGroupId') IS NULL
    ALTER TABLE dbo.ReceiptHeader ADD ReceiptGroupId INT NULL CONSTRAINT FK_ReceiptHeader_Group REFERENCES dbo.ReceiptGroup(ReceiptGroupId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Receipt_Group' AND object_id = OBJECT_ID('dbo.ReceiptHeader'))
    CREATE INDEX IX_Receipt_Group ON dbo.ReceiptHeader (ReceiptGroupId) WHERE ReceiptGroupId IS NOT NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NumberSequence_Kind' AND parent_object_id = OBJECT_ID('dbo.NumberSequence')
           AND definition NOT LIKE '%''RECEIPTGROUP''%')
BEGIN
    ALTER TABLE dbo.NumberSequence DROP CONSTRAINT CK_NumberSequence_Kind;
    ALTER TABLE dbo.NumberSequence ADD CONSTRAINT CK_NumberSequence_Kind CHECK (Kind IN ('ORDER','INVOICE','PACKAGE','PACKBATCH','WORKORDER','TRIP','RECEIPT','CYCLECOUNT','CROSSDOCK','PURCHASE','RENTAL','RENTALRETURN','MANUALISSUE','MULTIADJUST','SALESORDER','BILLINVOICE','CREDITNOTE','SALESRETURN','CODREMIT','DRIVERSETTLE','WAYBILL','EXITNOTICE','ASN','ACCTPURCH','ACCTDISP','RECEIPTGROUP'));
END
GO
/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 184, C4) — Recibir juntos desde el aparato: llave de la maestra.
   - dbo.ReceiptGroup.ClientKey: la llave del camión que arma el aparato; los recibos que llegan con la misma llave entran a la misma maestra (índice único filtrado).
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.ReceiptGroup', 'ClientKey') IS NULL
    ALTER TABLE dbo.ReceiptGroup ADD ClientKey UNIQUEIDENTIFIER NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReceiptGroup_ClientKey' AND object_id = OBJECT_ID('dbo.ReceiptGroup'))
    CREATE UNIQUE INDEX UX_ReceiptGroup_ClientKey ON dbo.ReceiptGroup (TenantId, ClientKey) WHERE ClientKey IS NOT NULL;
GO

/* ----------------------------------------------------------------------------
   2026-10-21 — Integraciones de SALIDA de un catálogo (productos…), dictado de Luis: la plantilla de la entidad (columna -> campo) sirve también para SACAR datos por SFTP/FTP/HTTP,
   por horario y al cambiar.
   - dbo.Integration.EntityKind VARCHAR(20) NULL: la entidad que manda (PRODUCT); NULL = envios del cliente (Lote 137b). La plantilla va en ImportTemplateId.
   - dbo.IntegrationOutputEntity: los registros del catalogo que cambiaron y esperan ir en un envio (IntegrationOutputId NULL = aun sin envio); un registro pendiente por integracion.
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Integration', 'EntityKind') IS NULL ALTER TABLE dbo.Integration ADD EntityKind VARCHAR(20) NULL;
GO
IF OBJECT_ID(N'dbo.IntegrationOutputEntity', N'U') IS NULL CREATE TABLE dbo.IntegrationOutputEntity (
    IntegrationOutputEntityId BIGINT IDENTITY(1,1) CONSTRAINT PK_IntegrationOutputEntity PRIMARY KEY,
    TenantId            INT NOT NULL CONSTRAINT FK_IntegrationOutputEntity_Tenant REFERENCES dbo.Tenant(TenantId),
    IntegrationId       INT NOT NULL CONSTRAINT FK_IntegrationOutputEntity_Integration REFERENCES dbo.Integration(IntegrationId),
    EntityId            INT NOT NULL,
    QueuedAtUtc         DATETIME2 NOT NULL CONSTRAINT DF_IntegrationOutputEntity_Queued DEFAULT SYSUTCDATETIME(),
    IntegrationOutputId BIGINT NULL CONSTRAINT FK_IntegrationOutputEntity_Output REFERENCES dbo.IntegrationOutput(IntegrationOutputId)
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationOutputEntity_Queue' AND object_id = OBJECT_ID('dbo.IntegrationOutputEntity'))
    CREATE INDEX IX_IntegrationOutputEntity_Queue ON dbo.IntegrationOutputEntity(TenantId, IntegrationId, IntegrationOutputId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_IntegrationOutputEntity_Open' AND object_id = OBJECT_ID('dbo.IntegrationOutputEntity'))
    CREATE UNIQUE INDEX UX_IntegrationOutputEntity_Open ON dbo.IntegrationOutputEntity(IntegrationId, EntityId) WHERE IntegrationOutputId IS NULL;
GO

/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 187) — Series y daño por partes (C2): dbo.ReceiptLine.DamageReportedQty = de DamagedQty, las unidades que ya tienen su reporte
   de daño (el daño se reporta en la tanda que recibe las unidades dañadas). Los recibos ya reportados quedan con DamageReportedQty = DamagedQty.
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.ReceiptLine', 'DamageReportedQty') IS NULL
    ALTER TABLE dbo.ReceiptLine ADD DamageReportedQty DECIMAL(16,3) NOT NULL CONSTRAINT DF_ReceiptLine_DamageReportedQty DEFAULT 0;
GO
UPDATE dbo.ReceiptLine SET DamageReportedQty = DamagedQty WHERE DamageReportId IS NOT NULL AND DamageReportedQty = 0 AND DamagedQty > 0;
GO

/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 193) — Permiso warehouse.pick.any: ver y despachar en el aparato CUALQUIER documento (también los que no están asignados);
   sin él, el aparato solo muestra lo asignado a la persona. Para no cambiarle el trabajo a nadie, lo reciben todos los roles (plantillas y los ya clonados
   de cada compañía) que ya tienen warehouse.pick; el administrador lo quita de los roles que no deban. Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'warehouse.pick.any')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('warehouse.pick.any',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'WAREHOUSE'),
            N'{"es":"Despachar cualquier documento en el aparato (no solo los asignados)","en":"Dispatch any document on the device (not only assigned ones)"}', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L193_PICK_ANY')
BEGIN
    INSERT INTO dbo.RolePermission (RoleId, PermissionId)
    SELECT rp.RoleId, any1.PermissionId
    FROM dbo.RolePermission rp
    JOIN dbo.Permission pick ON pick.PermissionId = rp.PermissionId AND pick.Code = 'warehouse.pick'
    JOIN dbo.Permission any1 ON any1.Code = 'warehouse.pick.any'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = rp.RoleId AND x.PermissionId = any1.PermissionId);
    INSERT INTO dbo.AppliedMigration (Code) VALUES ('L193_PICK_ANY');
END
GO

/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 195) — Asignar la orden de compra (a quien la va a recibir) y la orden de venta (a quien la va a despachar) a una persona, como ya se hace con el aviso de
   llegada y el de salida (Lote 171): dbo.PurchaseOrder.AssignedToUserId y dbo.SalesOrder.AssignedToUserId INT NULL (NULL = sin asignar), con llave foránea a dbo.AspNetUsers.
   Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.PurchaseOrder', 'AssignedToUserId') IS NULL
    ALTER TABLE dbo.PurchaseOrder ADD AssignedToUserId INT NULL CONSTRAINT FK_PurchaseOrder_AssignedTo REFERENCES dbo.AspNetUsers(Id);
GO
IF COL_LENGTH('dbo.SalesOrder', 'AssignedToUserId') IS NULL
    ALTER TABLE dbo.SalesOrder ADD AssignedToUserId INT NULL CONSTRAINT FK_SalesOrder_AssignedTo REFERENCES dbo.AspNetUsers(Id);
GO

/* ----------------------------------------------------------------------------
   2026-10-21 (Lote 199) — Permisos del aparato de almacén: warehouse.app.viewall («Ver todo en el aparato»: también lo asignado a otras personas) y warehouse.app.reassign
   («Reasignar desde el aparato»; exige ver todo). Los administradores (TenantAdmin) los reciben: se propagan a los roles que ya tienen driverpay.approve (que solo trae TenantAdmin por
   omisión), plantillas y clonados. Al supervisor del almacén se los da el administrador desde Sistema → Roles. Solo AGREGA. Idempotente.
   ---------------------------------------------------------------------------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'warehouse.app.viewall')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('warehouse.app.viewall',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'WAREHOUSE'),
            N'{"es":"Ver todo en el aparato (también lo asignado a otras personas)","en":"See everything on the device (also what is assigned to others)"}', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'warehouse.app.reassign')
    INSERT INTO dbo.Permission (Code, CategoryLookupId, LabelJson, IsSystem)
    VALUES ('warehouse.app.reassign',
            (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'WAREHOUSE'),
            N'{"es":"Reasignar desde el aparato (exige ver todo)","en":"Reassign from the device (requires see-everything)"}', 1);
GO
IF NOT EXISTS (SELECT 1 FROM dbo.AppliedMigration WHERE Code = 'L199_APP_VIEWALL_REASSIGN')
BEGIN
    INSERT INTO dbo.RolePermission (RoleId, PermissionId)
    SELECT rp.RoleId, np.PermissionId
    FROM dbo.RolePermission rp
    JOIN dbo.Permission hallmark ON hallmark.PermissionId = rp.PermissionId AND hallmark.Code = 'driverpay.approve'
    JOIN dbo.Permission np ON np.Code IN ('warehouse.app.viewall', 'warehouse.app.reassign')
    WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermission x WHERE x.RoleId = rp.RoleId AND x.PermissionId = np.PermissionId);
    INSERT INTO dbo.AppliedMigration (Code) VALUES ('L199_APP_VIEWALL_REASSIGN');
END
GO
