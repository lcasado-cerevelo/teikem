/* ============================================================================================================================
   04-usuarios-vendedores.sql — Pedido de vendedores (lote 228, P8): un usuario por vendedor (rol «Ventas») en Advance Solutions.
   ----------------------------------------------------------------------------------------------------------------------------
   Lo que este script NO hace, a propósito: crear cuentas ni poner contraseñas. Una cuenta de Teikem se crea en la pantalla (la
   contraseña se guarda cifrada por el sistema, nunca en un script):
       Sistema → Roles y usuarios → «Nuevo usuario» → correo, nombre, rol «Ventas» → Guardar.
   Esa alta NO manda correo: la pantalla muestra UNA vez una contraseña temporal, que se le da al vendedor en persona o por teléfono;
   al entrar pone la suya (y el MFA). No use «Recuperar contraseña» ni invitaciones masivas para esto (cada una manda un correo).
   Qué hace el script (después de crear las cuentas en la pantalla, o para revisar):
     - Lee la lista @Personas (los 7 vendedores del formulario, la oficina y el supervisor). FALTAN LOS CORREOS de los vendedores y el
       nombre del supervisor: el análisis no los trae. Llene la columna Email (y el supervisor) antes de aplicar; las filas sin correo
       solo se informan.
     - Por cada persona con correo: si la cuenta existe y es miembro ACTIVO de la compañía, le asegura su rol (Ventas / Oficina de
       ventas / Supervisor de ventas). Solo AGREGA el rol: no le quita otros.
     - Informa quién falta: sin correo, sin cuenta (créela en la pantalla), cuenta de portal de cliente (no sirve), sin membresía en la
       compañía o con la membresía no activa.
   Requiere 03-roles.sql (los roles). Idempotente: la segunda corrida cambia 0 filas.
   Cómo correrlo: SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 04-usuarios-vendedores.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';

-- >>> LLENAR: el correo con que entra cada uno (el mismo que se usó en «Nuevo usuario»). Rol: Sales = Ventas · SalesOffice = Oficina de
--     ventas · SalesSupervisor = Supervisor de ventas. Para dar dos roles a una persona, ponga dos filas.
DECLARE @Personas TABLE (FullName NVARCHAR(150) NOT NULL, Email NVARCHAR(256) NULL, RoleName NVARCHAR(80) NOT NULL);
INSERT @Personas (FullName, Email, RoleName) VALUES
    (N'Carlos Planas',      NULL, N'Sales'),
    (N'Neda L. Ramos',      NULL, N'Sales'),
    (N'Marilyn Rodríguez',  NULL, N'Sales'),
    (N'Santa Vélez',        NULL, N'Sales'),
    (N'Cindy Olivas',       NULL, N'Sales'),
    (N'Indhyra Ortiz',      NULL, N'Sales'),
    (N'Radames Santiago',   NULL, N'Sales'),
    (N'Oficina (almacén)',  N'almacen@advancesolutionspr.com', N'SalesOffice'),   -- el correo de la oficina que estaba en el formulario
    (N'(supervisor de ventas: por definir)', NULL, N'SalesSupervisor');
-- <<<

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(800), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'04-usuarios-vendedores · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);

SELECT @msg = STRING_AGG(CONVERT(NVARCHAR(MAX), x.RoleName), N', ') FROM (SELECT DISTINCT RoleName FROM @Personas) x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Role r WHERE r.TenantId = @TenantId AND r.Name = x.RoleName AND r.IsActive = 1);
IF @msg IS NOT NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: la compañía no tiene activos los roles ', @msg, N'. Corra antes 03-roles.sql. No se cambió nada.');
    THROW 50002, @msg, 1;
END

DECLARE @Active INT = (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity = N'MembershipStatus' AND InternalCode = N'ACTIVE');
DECLARE @Portal INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'UserKind' AND InternalCode = N'PORTAL');

DECLARE @Estado TABLE (FullName NVARCHAR(150), Email NVARCHAR(256), RoleName NVARCHAR(80), UserId INT, RoleId INT, Situacion NVARCHAR(200));
INSERT @Estado
SELECT x.FullName, LTRIM(RTRIM(x.Email)), x.RoleName, u.Id, r.RoleId,
    CASE
        WHEN NULLIF(LTRIM(RTRIM(x.Email)), N'') IS NULL THEN N'FALTA EL CORREO: llénelo en @Personas'
        WHEN u.Id IS NULL THEN N'SIN CUENTA: créela en Sistema → Roles y usuarios → Nuevo usuario (no manda correo)'
        WHEN u.UserKindLookupId = @Portal THEN N'ES UNA CUENTA DE PORTAL DE CLIENTE: use otro correo'
        WHEN ut.UserTenantId IS NULL THEN N'NO ES MIEMBRO de la compañía: agréguelo con «Nuevo usuario» (mismo correo)'
        WHEN ut.StatusCodeId <> @Active THEN N'MEMBRESÍA NO ACTIVA: actívela en Roles y usuarios'
        WHEN u.IsActive = 0 THEN N'CUENTA DESACTIVADA: actívela en Roles y usuarios'
        WHEN EXISTS (SELECT 1 FROM dbo.UserRole ur WHERE ur.UserId = u.Id AND ur.RoleId = r.RoleId AND ur.TenantId = @TenantId) THEN N'ok (ya tiene el rol)'
        ELSE N'se le asigna el rol'
    END
FROM @Personas x
JOIN dbo.Role r ON r.TenantId = @TenantId AND r.Name = x.RoleName
LEFT JOIN dbo.AspNetUsers u ON u.NormalizedEmail = UPPER(LTRIM(RTRIM(x.Email)))
LEFT JOIN dbo.UserTenant ut ON ut.UserId = u.Id AND ut.TenantId = @TenantId;

BEGIN TRAN;

INSERT dbo.UserRole (UserId, RoleId, TenantId, GrantedBy)
SELECT DISTINCT e.UserId, e.RoleId, @TenantId, NULL FROM @Estado e WHERE e.Situacion = N'se le asigna el rol';
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Roles asignados: ', @n);

SELECT e.FullName AS [Persona], e.Email AS [Correo], JSON_VALUE(r.DescriptionJson, '$.es') AS [Rol], e.Situacion AS [Situación]
FROM @Estado e JOIN dbo.Role r ON r.RoleId = e.RoleId ORDER BY e.RoleName, e.FullName;

-- Otras personas de la compañía que ya tienen el rol Ventas (por si falta alguno en la lista)
SELECT u.FullName AS [Con rol Ventas en la compañía], u.Email AS [Correo]
FROM dbo.UserRole ur JOIN dbo.Role r ON r.RoleId = ur.RoleId AND r.TenantId = @TenantId AND r.Name = N'Sales'
JOIN dbo.AspNetUsers u ON u.Id = ur.UserId WHERE ur.TenantId = @TenantId ORDER BY u.FullName;

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
