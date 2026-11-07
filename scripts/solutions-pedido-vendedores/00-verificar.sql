/* ============================================================================================================================
   00-verificar.sql — Pedido de vendedores (lote 228, P8): revisión ANTES de correr los scripts de datos de Advance Solutions.
   ----------------------------------------------------------------------------------------------------------------------------
   Qué hace: SOLO LEE. No cambia nada. Dice si la base ya tiene lo que necesitan los scripts 01 a 10:
     - la compañía (por nombre, @Compania) y su id;
     - las tablas y columnas de los lotes 221 a 225 (si falta alguna: primero el instalador / db-update de esta versión);
     - los permisos nuevos (sales.pricelist, sales.approve_client, clients.create_basic, pulse.sales, sales.quickbooks.*, billing.consignment.pay);
     - las plantillas de rol «Supervisor de ventas» (SalesSupervisor) y «Oficina de ventas» (SalesOffice) (las crea el API al arrancar);
     - los módulos de la compañía (Órdenes de venta, Impuestos, Facturación);
     - conteos de productos, marcas, clientes y términos de pago, para comparar después.
   Cómo correrlo: en SSMS, conectado a la base de Teikem (Base de datos: la de producción, la que usa el API), abrir y Ejecutar (F5).
     O con sqlcmd:  sqlcmd -S <servidor> -d <base> -E -I -b -i 00-verificar.sql
   Si la compañía no se llama «Advance Solutions» en Teikem, cambie @Compania (abajo) — el mismo valor en TODOS los scripts.
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON;
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';

PRINT CONCAT(N'Servidor: ', @@SERVERNAME, N' · Base: ', DB_NAME(), N' · ', CONVERT(NVARCHAR(30), SYSUTCDATETIME(), 120), N' UTC');

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1);
IF @TenantId IS NULL
BEGIN
    SELECT TenantId, Name, IsActive FROM dbo.Tenant ORDER BY TenantId;   -- para ver cómo se llama
    DECLARE @msg NVARCHAR(400) = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(),
        N'. Arriba está la lista de compañías: ponga el nombre exacto en @Compania de cada script.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'Compañía «', @Compania, N'»: TenantId = ', @TenantId);

-- 1) Estructura de los lotes 221–225 (la pone el instalador con db-update). Sin esto los scripts 01–10 ni compilan.
DECLARE @Faltan TABLE (Que NVARCHAR(120));
INSERT @Faltan SELECT x.Que FROM (VALUES
    (N'tabla dbo.PriceList (lote 221)',            CASE WHEN OBJECT_ID(N'dbo.PriceList', N'U') IS NULL THEN 1 ELSE 0 END),
    (N'tabla dbo.PriceListItem (lote 221)',        CASE WHEN OBJECT_ID(N'dbo.PriceListItem', N'U') IS NULL THEN 1 ELSE 0 END),
    (N'tabla dbo.PriceListClient (lote 221)',      CASE WHEN OBJECT_ID(N'dbo.PriceListClient', N'U') IS NULL THEN 1 ELSE 0 END),
    (N'tabla dbo.ProductKitComponent (lote 68)',   CASE WHEN OBJECT_ID(N'dbo.ProductKitComponent', N'U') IS NULL THEN 1 ELSE 0 END),
    (N'tabla dbo.NotificationRule (lote 51)',      CASE WHEN OBJECT_ID(N'dbo.NotificationRule', N'U') IS NULL THEN 1 ELSE 0 END),
    (N'Product.IsPromotion (lote 221)',            CASE WHEN COL_LENGTH(N'dbo.Product', N'IsPromotion') IS NULL THEN 1 ELSE 0 END),
    (N'Product.ProductTypeLookupId (lote 68)',     CASE WHEN COL_LENGTH(N'dbo.Product', N'ProductTypeLookupId') IS NULL THEN 1 ELSE 0 END),
    (N'Product.QuickBooksSentAt (lote 225)',       CASE WHEN COL_LENGTH(N'dbo.Product', N'QuickBooksSentAt') IS NULL THEN 1 ELSE 0 END),
    (N'Client.QuickBooksSentAt (lote 225)',        CASE WHEN COL_LENGTH(N'dbo.Client', N'QuickBooksSentAt') IS NULL THEN 1 ELSE 0 END),
    (N'Client.ShippingMode (lote 222)',            CASE WHEN COL_LENGTH(N'dbo.Client', N'ShippingMode') IS NULL THEN 1 ELSE 0 END),
    (N'Tenant.SalesShippingCharge (lote 222)',     CASE WHEN COL_LENGTH(N'dbo.Tenant', N'SalesShippingCharge') IS NULL THEN 1 ELSE 0 END),
    (N'Tenant.QuickBooksTermsJson (lote 225)',     CASE WHEN COL_LENGTH(N'dbo.Tenant', N'QuickBooksTermsJson') IS NULL THEN 1 ELSE 0 END)
) x (Que, Falta) WHERE x.Falta = 1;
INSERT @Faltan SELECT CONCAT(N'permiso ', c.Code) FROM (VALUES
    ('sales.pricelist'), ('sales.approve_client'), ('clients.create_basic'), ('pulse.sales'),
    ('sales.quickbooks.view'), ('sales.quickbooks.mark'), ('sales.quickbooks.export'), ('billing.consignment.pay')
) c (Code) WHERE NOT EXISTS (SELECT 1 FROM dbo.Permission p WHERE p.Code = c.Code);
IF EXISTS (SELECT 1 FROM @Faltan)
BEGIN
    SELECT Que AS [Falta en la base] FROM @Faltan;
    PRINT N'ALTO: falta estructura o permisos del pedido de vendedores. Instale primero esta versión (el instalador corre db-update) y vuelva a correr este script.';
END
ELSE PRINT N'OK: la estructura y los permisos de los lotes 221–225 están en la base.';

-- 2) Plantillas de rol (las crea el API al arrancar: PermissionSeeder). El script 03 funciona igual sin ellas, pero conviene que existan.
SELECT n.Name AS [Plantilla], CASE WHEN r.RoleId IS NULL THEN N'NO EXISTE (arranque el API una vez)' ELSE N'existe' END AS [Estado]
FROM (VALUES (N'Sales'), (N'SalesSupervisor'), (N'SalesOffice')) n (Name)
LEFT JOIN dbo.Role r ON r.TenantId IS NULL AND r.Name = n.Name;

-- 3) Roles de la compañía (los que tocan los scripts 03, 04 y 09)
SELECT r.RoleId, r.Name AS [Rol], r.IsActive AS [Activo], (SELECT COUNT(*) FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId) AS [Permisos],
       (SELECT COUNT(*) FROM dbo.UserRole ur WHERE ur.RoleId = r.RoleId AND ur.TenantId = @TenantId) AS [Usuarios]
FROM dbo.Role r WHERE r.TenantId = @TenantId
  AND r.Name IN (N'TenantAdmin', N'Sales', N'SalesSupervisor', N'SalesOffice', N'Supervisor de ventas', N'Oficina de ventas', N'Ventas')
ORDER BY r.Name;

-- 4) Módulos: el pedido vive en Órdenes de venta (SALES_ORDERS); las facturas (y con ellas QuickBooks y la consignación) en Facturación (BILLING); el IVU en Impuestos (TAX).
SELECT m.ModuleKey AS [Módulo], CASE WHEN tm.IsEnabled = 1 THEN N'encendido' WHEN tm.IsEnabled = 0 THEN N'APAGADO' ELSE N'NO ASIGNADO' END AS [Estado]
FROM (VALUES ('SALES_ORDERS'), ('TAX'), ('BILLING'), ('WMS_LOTSERIAL'), ('CATALOG')) m (ModuleKey)
LEFT JOIN dbo.TenantModule tm ON tm.TenantId = @TenantId AND tm.ModuleKey = m.ModuleKey;

-- 5) Conteos para comparar después
SELECT COUNT(*) AS [Productos activos], SUM(CASE WHEN Brand IS NULL OR LTRIM(RTRIM(Brand)) = N'' THEN 1 ELSE 0 END) AS [Sin marca]
FROM dbo.Product WHERE TenantId = @TenantId AND ClientId IS NULL AND IsActive = 1;
SELECT ISNULL(Brand, N'(sin marca)') AS [Marca tal como está], COUNT(*) AS [Productos activos]
FROM dbo.Product WHERE TenantId = @TenantId AND ClientId IS NULL AND IsActive = 1 GROUP BY Brand ORDER BY Brand;
SELECT ISNULL(lc.InternalCode, N'(sin término)') AS [Término de pago], COUNT(*) AS [Clientes activos]
FROM dbo.Client c LEFT JOIN dbo.LookupCode lc ON lc.LookupCodeId = c.PaymentTermLookupId
WHERE c.TenantId = @TenantId AND c.IsActive = 1 GROUP BY lc.InternalCode ORDER BY lc.InternalCode;

-- 6) Lo que depende de columnas nuevas va por SQL dinámico (si faltan, arriba ya lo dijo)
IF NOT EXISTS (SELECT 1 FROM @Faltan)
    EXEC sys.sp_executesql N'
        SELECT
          (SELECT COUNT(*) FROM dbo.Product WHERE TenantId = @t AND ClientId IS NULL AND IsActive = 1 AND QuickBooksSentAt IS NULL) AS [Productos sin «mandado a QuickBooks»],
          (SELECT COUNT(*) FROM dbo.Client WHERE TenantId = @t AND IsActive = 1 AND QuickBooksSentAt IS NULL) AS [Clientes sin «mandado a QuickBooks»],
          (SELECT COUNT(*) FROM dbo.PriceList WHERE TenantId = @t AND IsActive = 1) AS [Listas de precios],
          (SELECT COUNT(*) FROM dbo.Product WHERE TenantId = @t AND Sku = N''OFERTA1'') AS [OFERTA1 existe],
          (SELECT COUNT(*) FROM dbo.NotificationRule WHERE TenantId = @t AND EventCode LIKE ''SALES[_]%'') AS [Reglas de aviso de ventas];
        SELECT SalesShippingCharge AS [Envío: cargo], SalesShippingFreeFrom AS [Envío: gratis desde], SalesShippingTaxable AS [Envío lleva IVU],
               QuickBooksSalesAccount AS [QB cuenta de ventas], QuickBooksTaxItem AS [QB ítem de IVU], QuickBooksTermsJson AS [QB términos]
        FROM dbo.Tenant WHERE TenantId = @t;', N'@t INT', @t = @TenantId;

PRINT N'Fin de la revisión (no se cambió nada).';
