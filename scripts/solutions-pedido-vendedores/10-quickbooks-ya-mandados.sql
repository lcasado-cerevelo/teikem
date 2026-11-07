/* ============================================================================================================================
   10-quickbooks-ya-mandados.sql — Pedido de vendedores (lote 228, P8): marcar como «ya están en QuickBooks» los productos y clientes de
   Advance Solutions que vinieron de QuickBooks, para que la PRIMERA exportación no los traiga a todos como «nuevos».
   ----------------------------------------------------------------------------------------------------------------------------
   Por qué: el archivo de QuickBooks (pestaña QuickBooks de Órdenes de venta) lleva, junto a las facturas, los productos y clientes que
   nunca se han mandado (Product.QuickBooksSentAt / Client.QuickBooksSentAt vacíos = «nuevo»). Los de Solutions se migraron DESDE
   QuickBooks (lote 10), así que ya están allá; sin este script todos saldrían como «producto nuevo».
   Qué hace:
     - Productos propios de la compañía (activos e inactivos; los KITS no, porque nunca salen como ítem: van desglosados) sin la marca:
       se les pone la fecha de hoy, MENOS los SKU de @ExcluirProductos (los que se crearon en Teikem y todavía NO están en QuickBooks).
       Antes de aplicar, compare la lista que muestra con la Lista de ítems de QuickBooks.
     - Clientes: los que tienen «Código QuickBooks» (campo de la migración) ya cuentan como mandados; igual se les pone la marca. Los que
       NO tienen ese código (creados en Teikem) solo se marcan con @IncluirClientesSinCodigo = 1; si no, se listan y saldrán como
       «cliente nuevo» (con «ver datos») para que la oficina decida. Los PROSPECTOS nunca se marcan. @ExcluirClientes deja fuera a quien diga.
   Recomendación (lote 225): la primera exportación, SIN clientes ni productos nuevos (contestar «No» a la pregunta), hasta comprobar que
   QuickBooks acepta los nombres.
   Idempotente: la segunda corrida cambia 0 filas. No toca facturas.
   Cómo correrlo: SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 10-quickbooks-ya-mandados.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';
DECLARE @IncluirClientesSinCodigo BIT = 0;              -- 1 = marcar también los clientes sin «Código QuickBooks» (si ya los entraron a mano en QuickBooks)

-- >>> LLENAR (opcional): lo que todavía NO está en QuickBooks y debe salir como nuevo en el archivo
DECLARE @ExcluirProductos TABLE (Sku NVARCHAR(60) NOT NULL);
-- INSERT @ExcluirProductos VALUES (N'SKU-NUEVO-1');
DECLARE @ExcluirClientes TABLE (ClientCode NVARCHAR(30) NOT NULL);
-- INSERT @ExcluirClientes VALUES (N'CLIENTE-NUEVO');
-- <<<

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(800), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'10-quickbooks-ya-mandados · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);

DECLARE @Ahora    DATETIME2 = SYSUTCDATETIME();
DECLARE @KitType  INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'ProductType' AND InternalCode = N'KIT');
DECLARE @Prospect INT = (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity = N'ClientStatus' AND InternalCode = N'PROSPECT');
DECLARE @ClientEntity INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'EntityType' AND InternalCode = N'CLIENT');

-- Clientes con «Código QuickBooks» (campo personalizado qb_code de la migración)
DECLARE @ConCodigo TABLE (ClientId INT PRIMARY KEY);
INSERT @ConCodigo SELECT DISTINCT v.EntityId
FROM dbo.CustomFieldDefinition d JOIN dbo.CustomFieldValue v ON v.CustomFieldDefinitionId = d.CustomFieldDefinitionId
WHERE d.TenantId = @TenantId AND d.EntityTypeLookupId = @ClientEntity AND d.FieldKey = N'qb_code' AND d.IsActive = 1
  AND NULLIF(LTRIM(RTRIM(v.ValueText)), N'') IS NOT NULL;

SELECT x.Sku AS [SKU de @ExcluirProductos que no existe (se ignora)] FROM @ExcluirProductos x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Product p WHERE p.TenantId = @TenantId AND p.ClientId IS NULL AND p.Sku = UPPER(LTRIM(RTRIM(x.Sku))));

-- Lo que se va a marcar (para compararlo con QuickBooks)
SELECT p.Sku AS [Producto que se marca como ya en QuickBooks], p.Name AS [Nombre], p.IsActive AS [Activo]
FROM dbo.Product p
WHERE p.TenantId = @TenantId AND p.ClientId IS NULL AND p.QuickBooksSentAt IS NULL
  AND (p.ProductTypeLookupId IS NULL OR p.ProductTypeLookupId <> @KitType)
  AND p.Sku NOT IN (SELECT UPPER(LTRIM(RTRIM(Sku))) FROM @ExcluirProductos)
ORDER BY p.Sku;
SELECT c.Code AS [Cliente SIN «Código QuickBooks»], c.Name AS [Nombre],
       CASE WHEN c.StatusCodeId = @Prospect THEN N'prospecto: no se marca'
            WHEN @IncluirClientesSinCodigo = 1 AND c.Code NOT IN (SELECT ClientCode FROM @ExcluirClientes) THEN N'se marca (@IncluirClientesSinCodigo = 1)'
            ELSE N'no se marca: saldrá como «cliente nuevo»' END AS [Qué pasa]
FROM dbo.Client c
WHERE c.TenantId = @TenantId AND c.IsActive = 1 AND c.QuickBooksSentAt IS NULL AND c.ClientId NOT IN (SELECT ClientId FROM @ConCodigo)
ORDER BY c.Name;

BEGIN TRAN;

UPDATE p SET p.QuickBooksSentAt = @Ahora
FROM dbo.Product p
WHERE p.TenantId = @TenantId AND p.ClientId IS NULL AND p.QuickBooksSentAt IS NULL
  AND (p.ProductTypeLookupId IS NULL OR p.ProductTypeLookupId <> @KitType)
  AND p.Sku NOT IN (SELECT UPPER(LTRIM(RTRIM(Sku))) FROM @ExcluirProductos);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Productos marcados como ya en QuickBooks: ', @n);

UPDATE c SET c.QuickBooksSentAt = @Ahora
FROM dbo.Client c
WHERE c.TenantId = @TenantId AND c.QuickBooksSentAt IS NULL AND c.StatusCodeId <> @Prospect
  AND c.Code NOT IN (SELECT ClientCode FROM @ExcluirClientes)
  AND (c.ClientId IN (SELECT ClientId FROM @ConCodigo) OR (@IncluirClientesSinCodigo = 1 AND c.IsActive = 1));
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Clientes marcados como ya en QuickBooks: ', @n);

SELECT (SELECT COUNT(*) FROM dbo.Product WHERE TenantId = @TenantId AND ClientId IS NULL AND QuickBooksSentAt IS NULL
          AND (ProductTypeLookupId IS NULL OR ProductTypeLookupId <> @KitType)) AS [Productos que siguen «nuevos»],
       (SELECT COUNT(*) FROM dbo.Client WHERE TenantId = @TenantId AND IsActive = 1 AND QuickBooksSentAt IS NULL
          AND ClientId NOT IN (SELECT ClientId FROM @ConCodigo)) AS [Clientes que siguen «nuevos»];

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
