-- Demo (2026-10-07): convierte al usuario admin de Advance Solutions en «contador a ciegas» para mostrar el reconteo en la app
-- (escenarios C y D de docs/demo-dani.md), SIN tocar la web. Es reversible.
--   ON  : le quita al rol TenantAdmin de Solutions el permiso «Contar» (warehouse.count) y lo marca «Ve lo esperado al contar: Si».
--         Sin «Contar» la app ya no le muestra lo esperado y, al aceptar cada cantidad, lo verifica: si no coincide pide recontar
--         (sin decir cuanto era); si vuelve a fallar, la linea se cierra y queda para revision. Conserva «Capturar conteo».
--   OFF : lo deja como estaba (devuelve «Contar» y quita la marca).
-- Mientras esta en ON el admin de Solutions no puede reconciliar conteos en la web. Corra OFF al terminar la demostracion.
-- El API guarda los permisos 5 minutos en memoria: despues de ON/OFF espere 5 minutos o reinicie el API.
-- Uso (sqlcmd, SQL Server local con autenticacion de Windows):
--   sqlcmd -S localhost -E -C -d Teikem -v Modo=ON  -i "F:\Visual Studio 2022\Projects\teikem\scripts\demo\solutions-modo-contador.sql"
--   sqlcmd -S localhost -E -C -d Teikem -v Modo=OFF -i "F:\Visual Studio 2022\Projects\teikem\scripts\demo\solutions-modo-contador.sql"
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @modo NVARCHAR(3) = N'$(Modo)';
IF @modo NOT IN (N'ON', N'OFF') THROW 50000, N'Modo debe ser ON u OFF (use -v Modo=ON o -v Modo=OFF).', 1;

DECLARE @tenant INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = N'Advance Solutions');
IF @tenant IS NULL THROW 50000, N'No encuentro la compania Advance Solutions.', 1;
DECLARE @perm INT = (SELECT PermissionId FROM dbo.Permission WHERE Code = N'warehouse.count');
IF @perm IS NULL THROW 50000, N'No encuentro el permiso warehouse.count.', 1;
DECLARE @rol INT = (SELECT RoleId FROM dbo.Role WHERE TenantId = @tenant AND Name = N'TenantAdmin');
IF @rol IS NULL THROW 50000, N'No encuentro el rol TenantAdmin de Solutions.', 1;

BEGIN TRAN;
IF @modo = N'ON'
BEGIN
    DELETE FROM dbo.RolePermission WHERE RoleId = @rol AND PermissionId = @perm;
    UPDATE ut SET CountSeeExpected = 1
    FROM dbo.UserTenant ut
    WHERE ut.TenantId = @tenant AND EXISTS (SELECT 1 FROM dbo.UserRole ur WHERE ur.UserId = ut.UserId AND ur.TenantId = @tenant AND ur.RoleId = @rol);
    PRINT N'ON: el admin de Solutions cuenta como contador (sin Contar, ve lo esperado al contar: Si).';
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.RolePermission WHERE RoleId = @rol AND PermissionId = @perm)
        INSERT INTO dbo.RolePermission (RoleId, PermissionId) VALUES (@rol, @perm);
    UPDATE ut SET CountSeeExpected = NULL
    FROM dbo.UserTenant ut
    WHERE ut.TenantId = @tenant AND EXISTS (SELECT 1 FROM dbo.UserRole ur WHERE ur.UserId = ut.UserId AND ur.TenantId = @tenant AND ur.RoleId = @rol);
    PRINT N'OFF: el admin de Solutions quedo como estaba (con Contar, marca quitada).';
END
COMMIT;

SELECT u.Email AS Usuario,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = @rol AND rp.PermissionId = @perm) THEN N'si' ELSE N'no' END AS TieneContar,
       ut.CountSeeExpected AS VeLoEsperado
FROM dbo.UserTenant ut
JOIN dbo.AspNetUsers u ON u.Id = ut.UserId
WHERE ut.TenantId = @tenant AND EXISTS (SELECT 1 FROM dbo.UserRole ur WHERE ur.UserId = ut.UserId AND ur.TenantId = @tenant AND ur.RoleId = @rol);
