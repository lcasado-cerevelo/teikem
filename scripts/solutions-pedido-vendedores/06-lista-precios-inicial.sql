/* ============================================================================================================================
   06-lista-precios-inicial.sql — Pedido de vendedores (lote 228, P8; ajustado en el lote 229): la lista de precios GENERAL de Advance
   Solutions con los precios del formulario de Formsite (leídos el 2026-11-06). La oferta OFERTA1 NO va aquí: desde el lote 229 las ofertas
   son aparte (02-kit-oferta1.sql) y un kit de oferta lleva su precio en su oferta, no en una lista.
   ----------------------------------------------------------------------------------------------------------------------------
   Para qué: al pedir, el precio sale de la lista vigente del cliente; si no tiene, de la lista vigente GENERAL; si no, del precio de
   venta del producto (y si hay una oferta vigente más barata, gana la oferta). El vendedor no teclea precios. Esta es la lista general
   («aplica a todos los clientes»).
   Qué hace:
     - Crea (si no existe una lista activa con el nombre @Nombre) la lista general, vigente desde @Desde (hoy, hora de Puerto Rico) y sin
       fecha final, y le pone un renglón por cada producto del formulario que exista en Teikem (SKU sin espacios ni guiones), con el
       precio del formulario. Los GUANTES (NECH 1001–1004) no van: en el formulario solo se venden a precio negociado (ver 07). Un kit
       de oferta (Product.OfferId) nunca entra: se informa y se salta.
     - Si la lista ya existe: agrega los renglones que falten. NO cambia el precio de un renglón que ya está (lo pudo cambiar el
       supervisor) salvo que ponga @ActualizarPrecios = 1.
     - La publica (@Publicar = 1) si no se cruza con otra lista general publicada en las mismas fechas y productos; si se cruza, ABORTA
       y dice con cuál (dos listas generales no pueden dar dos precios al mismo producto).
     - Informa: SKU del formulario que no existen (p. ej. las cajas), y los precios del formulario que difieren del precio de venta
       del producto (informativo; la lista manda).
   No depende de 02-kit-oferta1.sql. Idempotente: la segunda corrida cambia 0 filas.
   Después, en pantalla: Almacén → Productos e inventario → «Precios y ofertas» → la lista: revisar, poner fotos a los renglones si se quiere.
   Cómo correrlo: SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 06-lista-precios-inicial.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';
DECLARE @Nombre   NVARCHAR(120) = N'Lista general (precios del formulario, 2026-11-06)';
DECLARE @Desde    DATE = CAST(DATEADD(HOUR, -4, SYSUTCDATETIME()) AS DATE);   -- hoy en Puerto Rico (UTC−4, sin horario de verano)
DECLARE @Publicar BIT = 1;                              -- 1 = la deja publicada (vigente); 0 = en borrador
DECLARE @ActualizarPrecios BIT = 0;                     -- 1 = si el renglón ya existe con otro precio, lo pone al de abajo

-- Precios del formulario (2026-11-06). Si un SKU tiene otro nombre en Teikem, agregue otra fila con el mismo FormSku.
DECLARE @Precios TABLE (FormSku NVARCHAR(60) NOT NULL, Candidato NVARCHAR(60) NOT NULL, Precio DECIMAL(18,4) NOT NULL,
    Clave AS UPPER(REPLACE(REPLACE(REPLACE(Candidato, N' ', N''), N'-', N''), N'.', N'')));
INSERT @Precios (FormSku, Candidato, Precio) VALUES
    (N'070120', N'070120', 7.99), (N'52800', N'52800', 6.99), (N'24081028', N'24081028', 1.25), (N'24083225', N'24083225', 2.50),
    (N'PD-23-36-45gms15/10', N'PD-23-36-45gms15/10', 28.99), (N'PD-23-36-45gms15/10', N'PD-23-36/15/10', 28.99),
    (N'PD-23-36-45gms3/50',  N'PD-23-36-45gms3/50',  28.99), (N'PD-23-36-45gms3/50',  N'PD-23-36/3/50',  28.99),
    (N'979', N'979', 0.95), (N'101010', N'101010', 1.05), (N'121212', N'121212', 1.46),
    (N'10152-5', N'10152-5', 1.09), (N'00614-1', N'00614-1', 1.09), (N'00052-1', N'00052-1', 1.09), (N'00804-6', N'00804-6', 1.09),
    (N'00802-2', N'00802-2', 1.29), (N'YD869999', N'YD869999', 1.93), (N'FOB-AB885961', N'FOB-AB885961', 23.16),
    (N'ARC609517', N'ARC609517', 1.41), (N'ARC609513', N'ARC609513', 4.50), (N'ARC573775', N'ARC573775', 1.62),
    (N'ARC609522', N'ARC609522', 1.93), (N'ARC609529', N'ARC609529', 1.93), (N'ARC609530', N'ARC609530', 1.93), (N'ARC609521', N'ARC609521', 1.93),
    (N'ARC521387', N'ARC521387', 1.93), (N'ARC521386', N'ARC521386', 1.93),
    (N'MPR-30453', N'MPR-30453', 1.00), (N'MPR-30244', N'MPR-30244', 1.25), (N'MPR-40310', N'MPR-40310', 1.61), (N'MPR-30221', N'MPR-30221', 1.48),
    (N'MPR-30212', N'MPR-30212', 1.45), (N'MPR-30503', N'MPR-30503', 2.04), (N'MPR-49002', N'MPR-49002', 1.53),
    (N'MPR-10105', N'MPR-10105', 2.78), (N'MPR-10103', N'MPR-10103', 1.76);

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(2000), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'06-lista-precios-inicial · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);

-- Cada SKU del formulario → un solo producto propio y activo
DECLARE @Match TABLE (FormSku NVARCHAR(60), ProductId INT, Sku NVARCHAR(60), Precio DECIMAL(18,4), SalePrice DECIMAL(18,4));
INSERT @Match SELECT DISTINCT f.FormSku, p.ProductId, p.Sku, f.Precio, p.SalePrice
FROM @Precios f JOIN dbo.Product p ON p.TenantId = @TenantId AND p.ClientId IS NULL AND p.IsActive = 1
 AND UPPER(REPLACE(REPLACE(REPLACE(p.Sku, N' ', N''), N'-', N''), N'.', N'')) = f.Clave;
SELECT f.FormSku AS [SKU del formulario que NO existe en Teikem (activo): no va en la lista] FROM (SELECT DISTINCT FormSku FROM @Precios) f
WHERE NOT EXISTS (SELECT 1 FROM @Match m WHERE m.FormSku = f.FormSku) ORDER BY f.FormSku;
SELECT m.FormSku AS [SKU AMBIGUO (más de un producto): no va en la lista], STRING_AGG(m.Sku, N', ') AS [Productos] FROM @Match m GROUP BY m.FormSku HAVING COUNT(*) > 1;
-- Lote 229: un kit de oferta lleva su precio en su oferta (Precios y ofertas → «Ofertas»), nunca en una lista
SELECT m.Sku AS [Kit de una OFERTA: no va en la lista (su precio está en la oferta)] FROM @Match m JOIN dbo.Product p ON p.ProductId = m.ProductId WHERE p.OfferId IS NOT NULL;
DELETE m FROM @Match m WHERE EXISTS (SELECT 1 FROM dbo.Product p WHERE p.ProductId = m.ProductId AND p.OfferId IS NOT NULL);
DELETE m FROM @Match m WHERE m.FormSku IN (SELECT FormSku FROM @Match GROUP BY FormSku HAVING COUNT(*) > 1);
SELECT Sku, Precio AS [Precio del formulario], SalePrice AS [Precio de venta del producto (informativo)] FROM @Match
WHERE SalePrice IS NULL OR SalePrice <> Precio ORDER BY Sku;

-- La lista
DECLARE @ListId INT, @Publicada BIT, @General BIT;
SELECT TOP (1) @ListId = PriceListId, @Publicada = IsPublished, @General = AppliesToAll
FROM dbo.PriceList WHERE TenantId = @TenantId AND Name = @Nombre AND IsActive = 1 ORDER BY PriceListId;
IF @ListId IS NOT NULL AND @General = 0
BEGIN
    SET @msg = CONCAT(N'ABORTADO: ya existe la lista «', @Nombre, N'» y es de clientes escogidos, no general. Cambie @Nombre. No se cambió nada.');
    THROW 50002, @msg, 1;
END

-- Cruces con otras listas generales PUBLICADAS (mismas fechas y algún producto en común) si la nuestra va a quedar publicada
IF @Publicar = 1 OR @Publicada = 1
BEGIN
    DECLARE @Desde2 DATE = ISNULL((SELECT ValidFrom FROM dbo.PriceList WHERE PriceListId = @ListId), @Desde),
            @Hasta2 DATE = (SELECT ValidTo FROM dbo.PriceList WHERE PriceListId = @ListId);
    SELECT @msg = STRING_AGG(CONVERT(NVARCHAR(MAX), CONCAT(N'«', x.Name, N'» (', x.Skus, N')')), N'; ') FROM (
        SELECT o.Name, STRING_AGG(CONVERT(NVARCHAR(MAX), m.Sku), N', ') AS Skus
        FROM dbo.PriceList o
        JOIN dbo.PriceListItem oi ON oi.PriceListId = o.PriceListId AND oi.IsActive = 1
        JOIN (SELECT ProductId, Sku FROM @Match
              UNION SELECT i.ProductId, p.Sku FROM dbo.PriceListItem i JOIN dbo.Product p ON p.ProductId = i.ProductId WHERE i.PriceListId = @ListId AND i.IsActive = 1) m
          ON m.ProductId = oi.ProductId
        WHERE o.TenantId = @TenantId AND o.IsActive = 1 AND o.IsPublished = 1 AND o.AppliesToAll = 1 AND o.PriceListId <> ISNULL(@ListId, 0)
          AND (o.ValidTo IS NULL OR o.ValidTo >= @Desde2) AND (@Hasta2 IS NULL OR o.ValidFrom <= @Hasta2)
        GROUP BY o.Name) x;
    IF @msg IS NOT NULL
    BEGIN
        SET @msg = CONCAT(N'ABORTADO: la lista se cruzaría (mismas fechas, todos los clientes) con: ', @msg,
            N'. Termine la vigencia de la otra lista en «Precios y ofertas», o corra este script con @Publicar = 0. No se cambió nada.');
        THROW 50003, @msg, 1;
    END
END

BEGIN TRAN;

IF @ListId IS NULL
BEGIN
    INSERT dbo.PriceList (TenantId, Name, ValidFrom, ValidTo, IsPublished, AppliesToAll, Notes, IsActive)
    VALUES (@TenantId, @Nombre, @Desde, NULL, 0, 1, N'Creada por script (lote 228) con los precios del formulario de Formsite del 2026-11-06.', 1);
    SET @ListId = SCOPE_IDENTITY(); SET @Publicada = 0; SET @Cambios += 1;
    PRINT CONCAT(N'Lista «', @Nombre, N'» creada (id ', @ListId, N'), vigente desde ', CONVERT(NVARCHAR(10), @Desde, 23), N'.');
END
ELSE PRINT CONCAT(N'La lista «', @Nombre, N'» ya existía (id ', @ListId, N').');

INSERT dbo.PriceListItem (TenantId, PriceListId, ProductId, Price, IsActive)
SELECT @TenantId, @ListId, m.ProductId, m.Precio, 1 FROM @Match m
WHERE NOT EXISTS (SELECT 1 FROM dbo.PriceListItem i WHERE i.PriceListId = @ListId AND i.ProductId = m.ProductId AND i.IsActive = 1);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Renglones agregados: ', @n);

SELECT p.Sku, i.Price AS [Precio en la lista], m.Precio AS [Precio del formulario]
FROM dbo.PriceListItem i JOIN @Match m ON m.ProductId = i.ProductId JOIN dbo.Product p ON p.ProductId = i.ProductId
WHERE i.PriceListId = @ListId AND i.IsActive = 1 AND i.Price <> m.Precio;
IF @ActualizarPrecios = 1
BEGIN
    UPDATE i SET i.Price = m.Precio FROM dbo.PriceListItem i JOIN @Match m ON m.ProductId = i.ProductId
    WHERE i.PriceListId = @ListId AND i.IsActive = 1 AND i.Price <> m.Precio;
    SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Precios de renglones puestos al del formulario: ', @n);
END

IF @Publicar = 1 AND @Publicada = 0
BEGIN
    UPDATE dbo.PriceList SET IsPublished = 1, UpdatedAtUtc = SYSUTCDATETIME() WHERE PriceListId = @ListId AND IsPublished = 0;
    SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT N'Lista publicada (vigente).';
END

SELECT l.PriceListId, l.Name AS [Lista], l.ValidFrom AS [Desde], l.ValidTo AS [Hasta], l.IsPublished AS [Publicada], l.AppliesToAll AS [Todos los clientes],
       (SELECT COUNT(*) FROM dbo.PriceListItem i WHERE i.PriceListId = l.PriceListId AND i.IsActive = 1) AS [Renglones]
FROM dbo.PriceList l WHERE l.PriceListId = @ListId;

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
