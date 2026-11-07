/* ============================================================================================================================
   01-marcas.sql — Pedido de vendedores (lote 228, P8): marcas revisadas en los productos de Advance Solutions.
   ----------------------------------------------------------------------------------------------------------------------------
   Para qué: el pedido guiado (web y app) muestra una PESTAÑA POR MARCA (Product.Brand, sin distinguir mayúsculas). Las marcas que
   conocen los vendedores son las del formulario de Formsite: Prodigy, Padimont, Cartones, Global Personal Care, American Red Cross,
   MedPride. Este script deja cada producto del formulario en su marca y uniforma cómo se escribe cada marca.
   Qué hace (solo productos propios de la compañía, activos):
     1. Los productos del formulario (lista @Formulario, abajo) quedan con la marca de su sección del formulario. El SKU se busca sin
        espacios ni guiones (`24081028` = `24-081028`, `NECH 1001` = `NECH1001` = `NECH-1001`). Yardley y A. Belle van en GLOBAL
        porque el formulario los vende dentro de «Global Personal Care».
     2. Las demás marcas que son la misma escrita distinto (lista @Alias: «MedPride» → MEDPRIDE, «American Red Cross» → …) se uniforman.
     3. Informa: SKU del formulario que no existen en Teikem (p. ej. las cajas, que la migración dejó solo en Depot), SKU ambiguos,
        y cómo quedan las pestañas (marca → productos activos) y los productos sin marca (salen en la última pestaña, «sin marca»).
   No crea productos, no toca precios ni inactiva nada. Idempotente: la segunda corrida cambia 0 filas.
   Cómo correrlo: SSMS → base de Teikem → abrir → F5. Por omisión SIMULA (@Aplicar = 0: hace todo y lo deshace, para ver el informe).
     Revise el informe, ponga @Aplicar = 1 y vuelva a correrlo. sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 01-marcas.sql
   Requiere: la compañía (@Compania) y la versión con los lotes 221–225 instalada (corra antes 00-verificar.sql).
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';

-- Cómo se escribe cada marca (lo que ve el vendedor en la pestaña). Cambie aquí si prefiere otro texto.
DECLARE @Prodigy  NVARCHAR(100) = N'PRODIGY',  @Padimont NVARCHAR(100) = N'PADIMONT', @Cartones NVARCHAR(100) = N'CARTONES',
        @Global   NVARCHAR(100) = N'GLOBAL',   @Arc      NVARCHAR(100) = N'AMERICAN RED CROSS', @MedPride NVARCHAR(100) = N'MEDPRIDE';

-- Productos del formulario (2026-11-06) y su sección. Si un SKU tiene otro nombre en Teikem, agregue otra fila con el mismo FormSku.
DECLARE @Formulario TABLE (FormSku NVARCHAR(60) NOT NULL, Candidato NVARCHAR(60) NOT NULL, Marca NVARCHAR(100) NOT NULL,
    Clave AS UPPER(REPLACE(REPLACE(REPLACE(Candidato, N' ', N''), N'-', N''), N'.', N'')));
INSERT @Formulario (FormSku, Candidato, Marca) VALUES
    (N'070120', N'070120', @Prodigy), (N'52800', N'52800', @Prodigy), (N'24081028', N'24081028', @Prodigy), (N'24083225', N'24083225', @Prodigy),
    (N'PD-23-36-45gms15/10', N'PD-23-36-45gms15/10', @Padimont), (N'PD-23-36-45gms15/10', N'PD-23-36/15/10', @Padimont),
    (N'PD-23-36-45gms3/50',  N'PD-23-36-45gms3/50',  @Padimont), (N'PD-23-36-45gms3/50',  N'PD-23-36/3/50',  @Padimont),
    (N'NECH 1001', N'NECH 1001', @Padimont), (N'NECH 1002', N'NECH 1002', @Padimont), (N'NECH 1003', N'NECH 1003', @Padimont), (N'NECH 1004', N'NECH 1004', @Padimont),
    (N'979', N'979', @Cartones), (N'101010', N'101010', @Cartones), (N'121212', N'121212', @Cartones),
    (N'10152-5', N'10152-5', @Global), (N'00614-1', N'00614-1', @Global), (N'00052-1', N'00052-1', @Global), (N'00804-6', N'00804-6', @Global),
    (N'00802-2', N'00802-2', @Global), (N'YD869999', N'YD869999', @Global), (N'FOB-AB885961', N'FOB-AB885961', @Global),
    (N'ARC609517', N'ARC609517', @Arc), (N'ARC609513', N'ARC609513', @Arc), (N'ARC573775', N'ARC573775', @Arc),
    (N'ARC609522', N'ARC609522', @Arc), (N'ARC609529', N'ARC609529', @Arc), (N'ARC609530', N'ARC609530', @Arc), (N'ARC609521', N'ARC609521', @Arc),
    (N'ARC521387', N'ARC521387', @Arc), (N'ARC521386', N'ARC521386', @Arc),
    (N'MPR-30453', N'MPR-30453', @MedPride), (N'MPR-30244', N'MPR-30244', @MedPride), (N'MPR-40310', N'MPR-40310', @MedPride),
    (N'MPR-30221', N'MPR-30221', @MedPride), (N'MPR-30212', N'MPR-30212', @MedPride), (N'MPR-30503', N'MPR-30503', @MedPride),
    (N'MPR-49002', N'MPR-49002', @MedPride), (N'MPR-10105', N'MPR-10105', @MedPride), (N'MPR-10103', N'MPR-10103', @MedPride);

-- Otras formas de escribir la misma marca (comparación sin mayúsculas ni espacios de más) → cómo queda.
DECLARE @Alias TABLE (Alias NVARCHAR(100) NOT NULL, Marca NVARCHAR(100) NOT NULL);
INSERT @Alias VALUES
    (N'PRODIGY', @Prodigy),
    (N'PADIMONT', @Padimont), (N'PADIMONT HOLDINGS', @Padimont), (N'PADIMONT HOLDINGS CORP', @Padimont),
    (N'CARTONES', @Cartones), (N'DEPOT', @Cartones),        -- en QuickBooks las cajas venían con la marca DEPOT; el formulario las llama «Cartones»
    (N'GLOBAL', @Global), (N'GLOBAL PERSONAL CARE', @Global),
    (N'AMERICAN RED CROSS', @Arc), (N'ARC', @Arc),
    (N'MEDPRIDE', @MedPride), (N'MED PRIDE', @MedPride);

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(600), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'01-marcas · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);

-- Productos propios y activos de la compañía con su clave de SKU
DECLARE @Prod TABLE (ProductId INT PRIMARY KEY, Sku NVARCHAR(60), Brand NVARCHAR(100), Clave NVARCHAR(60));
INSERT @Prod SELECT p.ProductId, p.Sku, p.Brand, UPPER(REPLACE(REPLACE(REPLACE(p.Sku, N' ', N''), N'-', N''), N'.', N''))
FROM dbo.Product p WHERE p.TenantId = @TenantId AND p.ClientId IS NULL AND p.IsActive = 1;

-- Cada SKU del formulario → producto (solo si hay exactamente uno)
DECLARE @Match TABLE (FormSku NVARCHAR(60), ProductId INT, Sku NVARCHAR(60), Marca NVARCHAR(100));
INSERT @Match SELECT DISTINCT f.FormSku, p.ProductId, p.Sku, f.Marca FROM @Formulario f JOIN @Prod p ON p.Clave = f.Clave;

SELECT f.FormSku AS [SKU del formulario que NO existe en Teikem (activo)] FROM (SELECT DISTINCT FormSku FROM @Formulario) f
WHERE NOT EXISTS (SELECT 1 FROM @Match m WHERE m.FormSku = f.FormSku) ORDER BY f.FormSku;
SELECT m.FormSku AS [SKU del formulario AMBIGUO (más de un producto; no se toca)], STRING_AGG(m.Sku, N', ') AS [Productos]
FROM @Match m GROUP BY m.FormSku HAVING COUNT(*) > 1;

BEGIN TRAN;

-- 1) Productos del formulario → la marca de su sección (comparación exacta: también corrige mayúsculas)
UPDATE p SET p.Brand = m.Marca
FROM dbo.Product p
JOIN (SELECT FormSku, MIN(ProductId) AS ProductId, MIN(Marca) AS Marca FROM @Match GROUP BY FormSku HAVING COUNT(*) = 1) m ON m.ProductId = p.ProductId
WHERE p.TenantId = @TenantId AND (p.Brand IS NULL OR p.Brand COLLATE Latin1_General_BIN2 <> m.Marca COLLATE Latin1_General_BIN2);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Productos del formulario con su marca puesta o corregida: ', @n);

-- 2) Uniformar la escritura de las demás (la misma marca escrita distinto)
UPDATE p SET p.Brand = a.Marca
FROM dbo.Product p
JOIN @Alias a ON UPPER(LTRIM(RTRIM(p.Brand))) = UPPER(a.Alias)
WHERE p.TenantId = @TenantId AND p.ClientId IS NULL AND p.IsActive = 1
  AND p.Brand COLLATE Latin1_General_BIN2 <> a.Marca COLLATE Latin1_General_BIN2;
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Marcas uniformadas (misma marca escrita distinto): ', @n);

-- Informe: así quedan las pestañas del pedido
SELECT ISNULL(UPPER(LTRIM(RTRIM(NULLIF(Brand, N'')))), N'(sin marca: última pestaña)') AS [Pestaña del pedido], COUNT(*) AS [Productos activos]
FROM dbo.Product WHERE TenantId = @TenantId AND ClientId IS NULL AND IsActive = 1
GROUP BY UPPER(LTRIM(RTRIM(NULLIF(Brand, N'')))) ORDER BY 1;
SELECT Sku AS [Producto activo sin marca], Name AS [Nombre] FROM dbo.Product
WHERE TenantId = @TenantId AND ClientId IS NULL AND IsActive = 1 AND (Brand IS NULL OR LTRIM(RTRIM(Brand)) = N'') ORDER BY Sku;

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
