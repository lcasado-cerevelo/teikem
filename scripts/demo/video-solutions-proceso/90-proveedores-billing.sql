SET NOCOUNT ON; SET XACT_ABORT ON;
-- Solo para la copia de grabación del video de Advance Solutions. Aborta si la base no es Teikem_video*. Idempotente.
IF DB_NAME() NOT LIKE N'Teikem[_]video%' THROW 50001, N'Este script es solo para una base Teikem_video*.', 1;
DECLARE @T INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = N'Advance Solutions' AND IsActive = 1);
IF @T IS NULL THROW 50002, N'No existe la compañía Advance Solutions.', 1;
BEGIN TRAN;
-- 1) Proveedores: los de las compras de QuickBooks de Solutions (nombres tal como vienen en Solutions Purchases.CSV)
DECLARE @P TABLE (Name NVARCHAR(200) NOT NULL);
INSERT @P VALUES (N'ADVANCE DEPOT SOLUTIONS'), (N'ADVANCE LOGISTICS LLC'), (N'FARMACIA CENTRAL DRUG'), (N'GLOBAL IMPORT SALES LLC'), (N'PADIMONT HOLDINGS CORP'), (N'SHIELD LINE');
INSERT dbo.Supplier (TenantId, Name, IsActive)
SELECT @T, p.Name, 1 FROM @P p WHERE NOT EXISTS (SELECT 1 FROM dbo.Supplier s WHERE s.TenantId = @T AND s.Name = p.Name AND s.IsActive = 1);
PRINT CONCAT(N'Proveedores creados: ', @@ROWCOUNT);
-- 2) Módulo de Facturación encendido (la exportación a QuickBooks exporta FACTURAS)
UPDATE dbo.TenantModule SET IsEnabled = 1, EnabledAtUtc = SYSUTCDATETIME(), EnabledBy = 1 WHERE TenantId = @T AND ModuleKey = N'BILLING' AND IsEnabled = 0;
PRINT CONCAT(N'BILLING encendido (filas): ', @@ROWCOUNT);
IF NOT EXISTS (SELECT 1 FROM dbo.TenantModule WHERE TenantId = @T AND ModuleKey = N'BILLING')
    INSERT dbo.TenantModule (TenantId, ModuleKey, IsEnabled, EnabledAtUtc, EnabledBy) VALUES (@T, N'BILLING', 1, SYSUTCDATETIME(), 1);
-- 3) La oferta de prueba «OFERTA DEL DIA» (kit KIT-001 con nombre de prueba) se desactiva: en el video solo sale OFERTA1
UPDATE dbo.Offer SET IsActive = 0 WHERE TenantId = @T AND Name = N'OFERTA DEL DIA' AND IsActive = 1;
PRINT CONCAT(N'Ofertas de prueba desactivadas: ', @@ROWCOUNT);
COMMIT;
SELECT N'Proveedores de Solutions' AS Que, COUNT(*) AS N FROM dbo.Supplier WHERE TenantId = @T AND IsActive = 1
UNION ALL SELECT N'BILLING encendido', COUNT(*) FROM dbo.TenantModule WHERE TenantId = @T AND ModuleKey = N'BILLING' AND IsEnabled = 1;
