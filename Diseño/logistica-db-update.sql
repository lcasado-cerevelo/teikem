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
