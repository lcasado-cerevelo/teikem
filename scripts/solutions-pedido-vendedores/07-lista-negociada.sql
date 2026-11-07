/* ============================================================================================================================
   07-lista-negociada.sql — Pedido de vendedores (lote 228, P8): la lista de precios NEGOCIADOS (guantes y cajas) de Advance Solutions.
   ----------------------------------------------------------------------------------------------------------------------------
   Para qué: en el formulario los guantes Padimont (NECH 1001–1004) SOLO se vendían con «precio custom» y las cajas también tenían
   precio custom. En Teikem eso es una lista de precios de CLIENTES ESCOGIDOS (gana sobre la general para esos clientes). El vendedor no
   teclea precios: el supervisor de ventas fija la lista y a qué clientes aplica.
   Qué hace:
     - Crea (si no existe una lista activa con el nombre @Nombre) una lista «aplica a clientes escogidos», vigente desde @Desde, en
       BORRADOR, con los guantes y las cajas que existan en Teikem. Precio de cada renglón: el de @Productos si lo llena; si no, el
       precio de venta actual del producto (de QuickBooks) como punto de partida; sin ninguno de los dos, el renglón no se crea y se avisa.
     - Agrega los clientes de @Clientes (número de cliente en Teikem). Viene VACÍA: el análisis no dice qué clientes tienen precio negociado.
     - Solo la publica si @Publicar = 1, tiene clientes y no se cruza con otra lista de clientes publicada (mismos clientes, fechas y
       productos). Si se cruza, ABORTA y dice con cuál.
     - Si la lista ya existe: agrega lo que falte (renglones y clientes); no cambia precios ya puestos.
   Si cada cliente tiene SU precio, haga una lista por grupo de clientes con el mismo precio (copie el script cambiando @Nombre, o
   desde la pantalla). Para monitor y tirillas negociados, agregue sus filas a @Productos (070120, 52800).
   Idempotente: la segunda corrida cambia 0 filas.
   Después, en pantalla: Almacén → Productos e inventario → «Precios y ofertas» → la lista → precios, clientes → «Publicar».
   Cómo correrlo: SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 07-lista-negociada.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';
DECLARE @Nombre   NVARCHAR(120) = N'Precios negociados: guantes y cajas';
DECLARE @Desde    DATE = CAST(DATEADD(HOUR, -4, SYSUTCDATETIME()) AS DATE);   -- hoy en Puerto Rico
DECLARE @Publicar BIT = 0;                              -- 0 = queda en borrador (lo publica el supervisor) · 1 = publicarla ya

-- >>> LLENAR (opcional): precio negociado de cada uno; NULL = el precio de venta actual del producto como punto de partida.
DECLARE @Productos TABLE (Sku NVARCHAR(60) NOT NULL, Precio DECIMAL(18,4) NULL,
    Clave AS UPPER(REPLACE(REPLACE(REPLACE(Sku, N' ', N''), N'-', N''), N'.', N'')));
INSERT @Productos (Sku, Precio) VALUES
    (N'NECH 1001', NULL), (N'NECH 1002', NULL), (N'NECH 1003', NULL), (N'NECH 1004', NULL),   -- guantes Padimont S/M/L/XL
    (N'979', NULL), (N'101010', NULL), (N'121212', NULL);                                     -- cajas 9×7×9, 10×10×10, 12×12×12
-- >>> LLENAR: números de cliente (Client.Code) a los que aplica.
DECLARE @Clientes TABLE (ClientCode NVARCHAR(30) NOT NULL);
-- INSERT @Clientes VALUES (N'FARMACIA-UNO'), (N'FARMACIA-DOS');
-- <<<

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(2000), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'07-lista-negociada · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);

DECLARE @Match TABLE (Sku NVARCHAR(60), ProductId INT, Precio DECIMAL(18,4), Origen NVARCHAR(40));
INSERT @Match
SELECT x.Sku, p.ProductId, COALESCE(x.Precio, p.SalePrice), CASE WHEN x.Precio IS NOT NULL THEN N'negociado (@Productos)' WHEN p.SalePrice IS NOT NULL THEN N'precio de venta actual' END
FROM @Productos x JOIN dbo.Product p ON p.TenantId = @TenantId AND p.ClientId IS NULL AND p.IsActive = 1
 AND UPPER(REPLACE(REPLACE(REPLACE(p.Sku, N' ', N''), N'-', N''), N'.', N'')) = x.Clave;
SELECT x.Sku AS [Producto que NO existe en Teikem (activo): no va] FROM @Productos x WHERE NOT EXISTS (SELECT 1 FROM @Match m WHERE m.Sku = x.Sku);
SELECT m.Sku AS [SKU AMBIGUO: no va] FROM @Match m GROUP BY m.Sku HAVING COUNT(*) > 1;
DELETE m FROM @Match m WHERE m.Sku IN (SELECT Sku FROM @Match GROUP BY Sku HAVING COUNT(*) > 1);
SELECT Sku AS [Sin precio negociado ni precio de venta: póngalo en @Productos o en la pantalla] FROM @Match WHERE Precio IS NULL;
DELETE FROM @Match WHERE Precio IS NULL;
SELECT x.ClientCode AS [Número de cliente que NO existe (se ignora)] FROM @Clientes x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Client c WHERE c.TenantId = @TenantId AND c.Code = x.ClientCode AND c.IsActive = 1);

DECLARE @ListId INT, @Publicada BIT, @General BIT;
SELECT TOP (1) @ListId = PriceListId, @Publicada = IsPublished, @General = AppliesToAll
FROM dbo.PriceList WHERE TenantId = @TenantId AND Name = @Nombre AND IsActive = 1 ORDER BY PriceListId;
IF @ListId IS NOT NULL AND @General = 1
BEGIN
    SET @msg = CONCAT(N'ABORTADO: ya existe la lista «', @Nombre, N'» y es GENERAL (aplica a todos). Cambie @Nombre. No se cambió nada.');
    THROW 50002, @msg, 1;
END

BEGIN TRAN;

IF @ListId IS NULL
BEGIN
    INSERT dbo.PriceList (TenantId, Name, ValidFrom, ValidTo, IsPublished, AppliesToAll, Notes, IsActive)
    VALUES (@TenantId, @Nombre, @Desde, NULL, 0, 0,
            N'Creada por script (lote 228). Precios de partida: los negociados o el precio de venta actual. Revise precios y clientes antes de publicar.', 1);
    SET @ListId = SCOPE_IDENTITY(); SET @Publicada = 0; SET @Cambios += 1;
    PRINT CONCAT(N'Lista «', @Nombre, N'» creada en borrador (id ', @ListId, N').');
END
ELSE PRINT CONCAT(N'La lista «', @Nombre, N'» ya existía (id ', @ListId, N').');

INSERT dbo.PriceListItem (TenantId, PriceListId, ProductId, Price, IsActive)
SELECT @TenantId, @ListId, m.ProductId, m.Precio, 1 FROM @Match m
WHERE NOT EXISTS (SELECT 1 FROM dbo.PriceListItem i WHERE i.PriceListId = @ListId AND i.ProductId = m.ProductId AND i.IsActive = 1);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Renglones agregados: ', @n);

INSERT dbo.PriceListClient (TenantId, PriceListId, ClientId, IsActive)
SELECT DISTINCT @TenantId, @ListId, c.ClientId, 1 FROM @Clientes x JOIN dbo.Client c ON c.TenantId = @TenantId AND c.Code = x.ClientCode AND c.IsActive = 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.PriceListClient lc WHERE lc.PriceListId = @ListId AND lc.ClientId = c.ClientId AND lc.IsActive = 1);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Clientes agregados: ', @n);

IF @Publicar = 1 AND @Publicada = 0
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.PriceListClient WHERE PriceListId = @ListId AND IsActive = 1)
        PRINT N'No se publica: la lista no tiene clientes (llene @Clientes o escójalos en la pantalla).';
    ELSE
    BEGIN
        -- cruce con otras listas de clientes publicadas: algún cliente en común, fechas que se cruzan y algún producto en común
        SELECT @msg = STRING_AGG(CONVERT(NVARCHAR(MAX), o.Name), N'; ') FROM dbo.PriceList o
        WHERE o.TenantId = @TenantId AND o.IsActive = 1 AND o.IsPublished = 1 AND o.AppliesToAll = 0 AND o.PriceListId <> @ListId
          AND (o.ValidTo IS NULL OR o.ValidTo >= @Desde)
          AND EXISTS (SELECT 1 FROM dbo.PriceListClient a JOIN dbo.PriceListClient b ON b.ClientId = a.ClientId
                      WHERE a.PriceListId = o.PriceListId AND a.IsActive = 1 AND b.PriceListId = @ListId AND b.IsActive = 1)
          AND EXISTS (SELECT 1 FROM dbo.PriceListItem a JOIN dbo.PriceListItem b ON b.ProductId = a.ProductId
                      WHERE a.PriceListId = o.PriceListId AND a.IsActive = 1 AND b.PriceListId = @ListId AND b.IsActive = 1);
        IF @msg IS NOT NULL
        BEGIN
            SET @msg = CONCAT(N'ABORTADO: la lista se cruzaría con «', @msg, N'» (mismos clientes, fechas y productos). Corrija en «Precios y ofertas». No se cambió nada.');
            THROW 50003, @msg, 1;
        END
        UPDATE dbo.PriceList SET IsPublished = 1, UpdatedAtUtc = SYSUTCDATETIME() WHERE PriceListId = @ListId AND IsPublished = 0;
        SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT N'Lista publicada.';
    END
END

SELECT p.Sku, p.Name AS [Producto], i.Price AS [Precio en la lista], m.Origen AS [De dónde salió]
FROM dbo.PriceListItem i JOIN dbo.Product p ON p.ProductId = i.ProductId LEFT JOIN @Match m ON m.ProductId = i.ProductId
WHERE i.PriceListId = @ListId AND i.IsActive = 1 ORDER BY p.Sku;
SELECT l.PriceListId, l.Name AS [Lista], l.ValidFrom AS [Desde], l.IsPublished AS [Publicada],
       (SELECT COUNT(*) FROM dbo.PriceListClient c WHERE c.PriceListId = l.PriceListId AND c.IsActive = 1) AS [Clientes]
FROM dbo.PriceList l WHERE l.PriceListId = @ListId;

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
