/* ============================================================================================================================
   03-roles.sql — Pedido de vendedores (lote 228, P8): roles «Supervisor de ventas» y «Oficina de ventas» en Advance Solutions.
   ----------------------------------------------------------------------------------------------------------------------------
   Por qué: los roles de fábrica nuevos (lote 222) se copian a una compañía SOLO cuando se aprovisiona; Solutions ya existía, así que no
   los tiene. Este script los crea en Solutions con el MISMO nombre interno que la plantilla (SalesSupervisor, SalesOffice: así reciben
   solos los permisos nuevos de futuras versiones) y se ven en pantalla como «Supervisor de ventas» y «Oficina de ventas».
   Qué hace (solo AGREGA; nunca quita un permiso ni borra un rol):
     - SalesSupervisor («Supervisor de ventas»): sales.pricelist, sales.approve_client, sales.view, clients.read, clients.update, pulse.sales.
     - SalesOffice («Oficina de ventas»): sales.view, sales.create, pulse.sales, sales.quickbooks.view, sales.quickbooks.mark,
       sales.quickbooks.export, billing.consignment.pay.
     - Sales («Ventas», el vendedor): asegura clients.create_basic y pulse.sales (los puso db-update; aquí solo se comprueba).
     - TenantAdmin: asegura los 8 permisos nuevos del pedido de vendedores.
     - Además, los dos roles nuevos reciben lo que tenga su plantilla de fábrica (si el API ya la creó). A Ventas y al administrador NO
       se les copia la plantilla entera (no se le devuelve a nadie un permiso que el administrador le haya quitado a propósito).
     - Si el rol existe desactivado, lo reactiva. Si el administrador ya había creado a mano un rol «Supervisor de ventas» u «Oficina de
       ventas», lo AVISA (no lo toca): pase sus usuarios al rol nuevo en Sistema → Roles y usuarios y desactive el viejo.
   Los usuarios con sesión abierta ven los permisos nuevos en hasta 5 minutos (o al volver a entrar).
   Idempotente: la segunda corrida cambia 0 filas.
   Cómo correrlo: SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 03-roles.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';

DECLARE @Roles TABLE (Name NVARCHAR(80) NOT NULL, DescriptionJson NVARCHAR(400) NOT NULL);
INSERT @Roles VALUES
    (N'SalesSupervisor', N'{"es":"Supervisor de ventas","en":"Sales supervisor"}'),
    (N'SalesOffice',     N'{"es":"Oficina de ventas","en":"Sales office"}'),
    (N'Sales',           N'{"es":"Ventas","en":"Sales"}'),
    (N'TenantAdmin',     N'{"es":"Admin de compañía","en":"Tenant admin"}');
DECLARE @Permisos TABLE (RoleName NVARCHAR(80) NOT NULL, Code NVARCHAR(80) NOT NULL);
INSERT @Permisos VALUES
    (N'SalesSupervisor', 'sales.pricelist'), (N'SalesSupervisor', 'sales.approve_client'), (N'SalesSupervisor', 'sales.view'),
    (N'SalesSupervisor', 'clients.read'), (N'SalesSupervisor', 'clients.update'), (N'SalesSupervisor', 'pulse.sales'),
    (N'SalesOffice', 'sales.view'), (N'SalesOffice', 'sales.create'), (N'SalesOffice', 'pulse.sales'),
    (N'SalesOffice', 'sales.quickbooks.view'), (N'SalesOffice', 'sales.quickbooks.mark'), (N'SalesOffice', 'sales.quickbooks.export'),
    (N'SalesOffice', 'billing.consignment.pay'),
    (N'Sales', 'clients.create_basic'), (N'Sales', 'pulse.sales'),
    (N'TenantAdmin', 'sales.pricelist'), (N'TenantAdmin', 'sales.approve_client'), (N'TenantAdmin', 'clients.create_basic'), (N'TenantAdmin', 'pulse.sales'),
    (N'TenantAdmin', 'sales.quickbooks.view'), (N'TenantAdmin', 'sales.quickbooks.mark'), (N'TenantAdmin', 'sales.quickbooks.export'),
    (N'TenantAdmin', 'billing.consignment.pay');

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(800), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'03-roles · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);

SELECT @msg = STRING_AGG(CONVERT(NVARCHAR(MAX), x.Code), N', ') FROM (SELECT DISTINCT Code FROM @Permisos) x WHERE NOT EXISTS (SELECT 1 FROM dbo.Permission p WHERE p.Code = x.Code);
IF @msg IS NOT NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: faltan los permisos ', @msg, N'. Instale primero esta versión (el instalador corre db-update). No se cambió nada.');
    THROW 50002, @msg, 1;
END
IF NOT EXISTS (SELECT 1 FROM dbo.Role WHERE TenantId = @TenantId AND Name = N'Sales')
    PRINT N'AVISO: la compañía no tiene el rol «Ventas» (Sales); se crea aquí.';

-- Roles creados a mano con el nombre visible: solo se avisa
SELECT r.RoleId, r.Name AS [Rol creado a mano (no se toca: pase sus usuarios al de fábrica y desactívelo)],
       (SELECT COUNT(*) FROM dbo.UserRole ur WHERE ur.RoleId = r.RoleId) AS [Usuarios]
FROM dbo.Role r WHERE r.TenantId = @TenantId AND r.IsActive = 1 AND r.Name IN (N'Supervisor de ventas', N'Oficina de ventas', N'Supervisor ventas', N'Oficina ventas');

BEGIN TRAN;

-- 1) Roles que faltan (descripción de la plantilla si existe)
INSERT dbo.Role (TenantId, Name, DescriptionJson, IsSystem, IsActive)
SELECT @TenantId, x.Name, ISNULL(t.DescriptionJson, x.DescriptionJson), 0, 1
FROM @Roles x LEFT JOIN dbo.Role t ON t.TenantId IS NULL AND t.Name = x.Name
WHERE NOT EXISTS (SELECT 1 FROM dbo.Role r WHERE r.TenantId = @TenantId AND r.Name = x.Name);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Roles creados: ', @n);

-- 2) Reactivar los que estén desactivados
UPDATE r SET r.IsActive = 1 FROM dbo.Role r JOIN @Roles x ON x.Name = r.Name WHERE r.TenantId = @TenantId AND r.IsActive = 0;
SET @n = @@ROWCOUNT; SET @Cambios += @n; IF @n > 0 PRINT CONCAT(N'Roles reactivados: ', @n);

-- 3) Permisos: los de la lista + (solo los dos roles nuevos) los de su plantilla de fábrica. Solo agrega.
INSERT dbo.RolePermission (RoleId, PermissionId)
SELECT DISTINCT r.RoleId, p.PermissionId
FROM dbo.Role r
JOIN (SELECT x.RoleName, pm.PermissionId FROM @Permisos x JOIN dbo.Permission pm ON pm.Code = x.Code
      UNION
      SELECT t.Name, tp.PermissionId FROM dbo.Role t JOIN dbo.RolePermission tp ON tp.RoleId = t.RoleId
      WHERE t.TenantId IS NULL AND t.Name IN (N'SalesSupervisor', N'SalesOffice')) p ON p.RoleName = r.Name   -- la plantilla solo para los dos roles nuevos
WHERE r.TenantId = @TenantId
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Permisos agregados a los roles: ', @n);

-- Cómo quedan
SELECT r.Name AS [Rol], JSON_VALUE(r.DescriptionJson, '$.es') AS [Se ve como], r.IsActive AS [Activo],
       STRING_AGG(CONVERT(NVARCHAR(MAX), CASE WHEN pm.Code LIKE 'sales.%' OR pm.Code LIKE 'clients.%' OR pm.Code LIKE 'pulse.sales' OR pm.Code LIKE 'billing.consignment%' THEN pm.Code END), N', ')
           WITHIN GROUP (ORDER BY pm.Code) AS [Permisos de ventas y clientes],
       COUNT(*) AS [Permisos en total]
FROM dbo.Role r JOIN dbo.RolePermission rp ON rp.RoleId = r.RoleId JOIN dbo.Permission pm ON pm.PermissionId = rp.PermissionId
WHERE r.TenantId = @TenantId AND r.Name IN (SELECT Name FROM @Roles)
GROUP BY r.Name, r.DescriptionJson, r.IsActive ORDER BY r.Name;

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
