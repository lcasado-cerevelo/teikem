/* ============================================================================
   0001 — Permiso warehouse.transfer (2026-10-10): transferir inventario entre posiciones del mismo
   almacén desde el aparato (operario). inventory.adjust lo implica (PermissionCatalog.Implied).
   IDEMPOTENTE: se puede correr más de una vez. El PermissionSeeder (código) lo siembra igual y lo
   propaga a los roles; este archivo deja el espejo en la base para quien lea el SQL.
   ============================================================================ */
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
