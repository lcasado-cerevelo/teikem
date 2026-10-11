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
