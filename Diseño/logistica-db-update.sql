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
