/* ============================================================================================================================
   11-impuesto-productos.sql — Advance Solutions: el impuesto (IVU) de cada producto, tomado del código «Sales Tax Code» de QuickBooks
   (Solutions Items.csv, 2026-11-10): «Tax» = lleva IVU · «Non» = NO lleva IVU (exento).
   La migración de los productos no cargó ese dato: todos quedaron sin categoría de impuesto, y en Teikem «sin categoría» = gravable.
   Qué hace:
     - Pone la categoría de impuesto EXENTO (LookupCode TaxCategory / EXEMPT) a los productos que en QuickBooks son «Non».
     - Los «Tax» NO se tocan (sin categoría ya es gravable). Para ver o cambiar uno: ficha del producto → campo «Impuesto».
     - Informa los «Non» que no existen en Teikem y cualquier producto que ya tenga otra categoría distinta.
   Los 11 «Non» del archivo: 051885, 070120, 24-081028, 24-083225, 52800, 53310, 53350 (Prodigy: monitor, lancetas, tiras, control) y NECH 1001–1004 (guantes).
   Los SKU se comparan sin espacios, guiones ni puntos (NECH 1001 = NECH1001). Idempotente: la segunda corrida cambia 0 filas.
   Cómo correrlo: SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 11-impuesto-productos.sql
   Requiere los lotes 55 y 237 (categoría de impuesto del producto y campo «Impuesto» en el editor). No toca precios ni inventario.
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';

-- Productos que en QuickBooks NO llevan IVU («Non»). Si en Teikem el SKU se escribe distinto, agregue otra fila.
DECLARE @Exentos TABLE (Candidato NVARCHAR(60) NOT NULL,
    Clave AS UPPER(REPLACE(REPLACE(REPLACE(Candidato, N' ', N''), N'-', N''), N'.', N'')));
INSERT @Exentos (Candidato) VALUES
    (N'051885'), (N'070120'), (N'24-081028'), (N'24-083225'), (N'52800'), (N'53310'), (N'53350'),
    (N'NECH 1001'), (N'NECH 1002'), (N'NECH 1003'), (N'NECH 1004');

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(600), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
DECLARE @ExentoId INT = (SELECT TOP 1 LookupCodeId FROM dbo.LookupCode WHERE Entity = N'TaxCategory' AND InternalCode = N'EXEMPT' AND IsActive = 1 AND (TenantId IS NULL OR TenantId = @TenantId) ORDER BY TenantId DESC);
IF @ExentoId IS NULL THROW 50002, N'ABORTADO: no existe la categoría de impuesto EXEMPT (LookupCode TaxCategory). ¿Se instaló la versión con el lote 55?', 1;
PRINT CONCAT(N'11-impuesto-productos · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);

DECLARE @Prod TABLE (ProductId INT PRIMARY KEY, Sku NVARCHAR(60), Clave NVARCHAR(60), TaxId INT);
INSERT @Prod SELECT p.ProductId, p.Sku, UPPER(REPLACE(REPLACE(REPLACE(p.Sku, N' ', N''), N'-', N''), N'.', N'')), p.TaxCategoryLookupId
FROM dbo.Product p WHERE p.TenantId = @TenantId AND p.ClientId IS NULL AND p.IsActive = 1;

SELECT e.Candidato AS [«Non» de QuickBooks que NO existe en Teikem (activo)] FROM @Exentos e
WHERE NOT EXISTS (SELECT 1 FROM @Prod p WHERE p.Clave = e.Clave) ORDER BY e.Candidato;

BEGIN TRAN;

UPDATE p SET p.TaxCategoryLookupId = @ExentoId
FROM dbo.Product p
JOIN @Prod x ON x.ProductId = p.ProductId
JOIN @Exentos e ON e.Clave = x.Clave
WHERE p.TenantId = @TenantId AND (p.TaxCategoryLookupId IS NULL OR p.TaxCategoryLookupId <> @ExentoId);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Productos puestos como EXENTOS (sin IVU): ', @n);

-- Informe: así queda
SELECT ISNULL(l.InternalCode, N'(sin categoría = gravable)') AS [Impuesto], COUNT(*) AS [Productos activos]
FROM dbo.Product p LEFT JOIN dbo.LookupCode l ON l.LookupCodeId = p.TaxCategoryLookupId
WHERE p.TenantId = @TenantId AND p.ClientId IS NULL AND p.IsActive = 1 GROUP BY l.InternalCode ORDER BY 1;
SELECT p.Sku AS [Exento que NO estaba en la lista de «Non» (revise)], p.Name AS [Nombre]
FROM dbo.Product p
WHERE p.TenantId = @TenantId AND p.ClientId IS NULL AND p.IsActive = 1 AND p.TaxCategoryLookupId = @ExentoId
  AND NOT EXISTS (SELECT 1 FROM @Exentos e WHERE e.Clave = UPPER(REPLACE(REPLACE(REPLACE(p.Sku, N' ', N''), N'-', N''), N'.', N''))) ORDER BY p.Sku;

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
