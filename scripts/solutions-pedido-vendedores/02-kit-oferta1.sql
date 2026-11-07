/* ============================================================================================================================
   02-kit-oferta1.sql — Pedido de vendedores (lote 228, P8): la oferta OFERTA1 de Advance Solutions como KIT.
   ----------------------------------------------------------------------------------------------------------------------------
   Qué es: en el formulario de Formsite, «OFERTA1 — 6 cajas de tirillas No Coding + 1 monitor, $41.94». En Teikem una oferta es un
   kit (producto tipo KIT, sin existencia propia: se reservan y despachan sus componentes) marcado «Es promoción».
   Qué hace:
     - Si no existe el producto OFERTA1 (propio de la compañía), lo crea: tipo KIT, 6 × 52800 (tirillas) + 1 × 070120 (monitor),
       precio de venta $41.94, marca PRODIGY, «Es promoción» = sí, vigencia abierta (sin desde ni hasta), sin rastreo (NONE), unidad UN,
       la categoría y la categoría de impuesto de las tirillas (52800).
     - Si ya existe como kit: NO cambia nombre ni precio (los pudo cambiar el supervisor); solo pone lo que falte (marca, precio vacío,
       «Es promoción») y asegura los dos componentes con su cantidad. Un componente de más se informa, no se quita.
     - Si existe y NO es kit, o un componente no existe / está inactivo / es kit: ABORTA con el motivo (no cambia nada).
   Lo que NO puede hacer un script: la FOTO de la oferta (la imagen es la promoción). Paso manual, después de correrlo:
     Almacén → Productos e inventario → pestaña «Precios y ofertas» → tarjeta OFERTA1 → «Editar oferta» (o Productos → OFERTA1 →
     pestaña «Kit» → «Oferta (promoción)») → «Poner foto» → la imagen (JPG, PNG, WEBP o GIF). Ahí también se le pone vigencia («Oferta
     desde / hasta») si la tiene, y «Guardar oferta».
   Idempotente: la segunda corrida cambia 0 filas. No toca inventario.
   Cómo correrlo: después de 01-marcas.sql. SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 02-kit-oferta1.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';

DECLARE @KitSku    NVARCHAR(60)  = N'OFERTA1';
DECLARE @KitNombre NVARCHAR(200) = N'OFERTA1: 6 cajas de tirillas Prodigy No Coding + 1 monitor Prodigy';
DECLARE @KitPrecio DECIMAL(18,4) = 41.94;
DECLARE @KitMarca  NVARCHAR(100) = N'PRODIGY';          -- la misma que 01-marcas.sql pone a Prodigy
DECLARE @OfertaDesde DATE = NULL, @OfertaHasta DATE = NULL;   -- vigencia de la oferta (NULL = abierta); se cambia luego en la pantalla
DECLARE @Comp TABLE (Sku NVARCHAR(60) NOT NULL, Cantidad DECIMAL(16,3) NOT NULL, Orden INT NOT NULL,
    Clave AS UPPER(REPLACE(REPLACE(REPLACE(Sku, N' ', N''), N'-', N''), N'.', N'')));
INSERT @Comp (Sku, Cantidad, Orden) VALUES (N'52800', 6, 1), (N'070120', 1, 2);   -- 6 tirillas No Coding 50 + 1 monitor Auto Code

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(800), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'02-kit-oferta1 · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);

DECLARE @KitType INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'ProductType' AND InternalCode = N'KIT');
DECLARE @Un      INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'UnitOfMeasure' AND InternalCode = N'UN');
DECLARE @None    INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'TrackingType' AND InternalCode = N'NONE');
IF @KitType IS NULL OR @Un IS NULL OR @None IS NULL
    THROW 50002, N'ABORTADO: faltan catálogos (ProductType KIT, UnitOfMeasure UN o TrackingType NONE). Instale primero esta versión (db-update).', 1;

-- Componentes: cada uno debe existir una sola vez, propio, activo y no ser kit
DECLARE @CompOk TABLE (Sku NVARCHAR(60), ProductId INT, Cantidad DECIMAL(16,3), Orden INT, IsActive BIT, EsKit BIT, Nombre NVARCHAR(200));
INSERT @CompOk
SELECT c.Sku, p.ProductId, c.Cantidad, c.Orden, p.IsActive, CASE WHEN p.ProductTypeLookupId = @KitType THEN 1 ELSE 0 END, p.Name
FROM @Comp c
JOIN dbo.Product p ON p.TenantId = @TenantId AND p.ClientId IS NULL
 AND UPPER(REPLACE(REPLACE(REPLACE(p.Sku, N' ', N''), N'-', N''), N'.', N'')) = c.Clave;
SELECT @msg = STRING_AGG(x.Motivo, N' · ') FROM (
    SELECT CONCAT(c.Sku, N': no existe en la compañía') AS Motivo FROM @Comp c WHERE NOT EXISTS (SELECT 1 FROM @CompOk o WHERE o.Sku = c.Sku)
    UNION ALL SELECT CONCAT(o.Sku, N': hay más de un producto con ese SKU') FROM @CompOk o GROUP BY o.Sku HAVING COUNT(*) > 1
    UNION ALL SELECT CONCAT(o.Sku, N': está inactivo') FROM @CompOk o WHERE o.IsActive = 0
    UNION ALL SELECT CONCAT(o.Sku, N': es un kit (un kit no lleva otro kit)') FROM @CompOk o WHERE o.EsKit = 1) x;
IF @msg IS NOT NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: los componentes de ', @KitSku, N' no sirven: ', @msg, N'. No se cambió nada.');
    THROW 50003, @msg, 1;
END
SELECT Sku AS [Componente], Nombre, Cantidad AS [Cantidad por kit] FROM @CompOk ORDER BY Orden;

-- El kit
DECLARE @KitId INT, @KitEsKit BIT, @KitActivo BIT;
SELECT @KitId = ProductId, @KitEsKit = CASE WHEN ProductTypeLookupId = @KitType THEN 1 ELSE 0 END, @KitActivo = IsActive
FROM dbo.Product WHERE TenantId = @TenantId AND ClientId IS NULL AND Sku = @KitSku;
IF @KitId IS NOT NULL AND @KitEsKit = 0
BEGIN
    SET @msg = CONCAT(N'ABORTADO: ya existe el producto ', @KitSku, N' y no es kit. Conviértalo en kit desde su ficha (Almacén → Productos → ', @KitSku,
        N' → «Es un kit») o cambie @KitSku. No se cambió nada.');
    THROW 50004, @msg, 1;
END
IF @KitId IS NOT NULL AND @KitActivo = 0
    PRINT CONCAT(N'AVISO: ', @KitSku, N' existe pero está INACTIVO; este script no lo reactiva (hágalo desde su ficha si se va a vender).');

BEGIN TRAN;

IF @KitId IS NULL
BEGIN
    INSERT dbo.Product (TenantId, ClientId, Sku, Name, ProductCategoryId, BaseUomLookupId, TrackingTypeLookupId, SalePrice, IsActive, Brand,
                        ProductTypeLookupId, KitAdjustable, IsPromotion, PromoFrom, PromoTo, TaxCategoryLookupId)
    SELECT @TenantId, NULL, @KitSku, @KitNombre, p.ProductCategoryId, @Un, @None, @KitPrecio, 1, @KitMarca,
           @KitType, 0, 1, @OfertaDesde, @OfertaHasta, p.TaxCategoryLookupId
    FROM dbo.Product p WHERE p.ProductId = (SELECT ProductId FROM @CompOk WHERE Orden = 1);
    SET @n = @@ROWCOUNT; SET @Cambios += @n;
    SET @KitId = SCOPE_IDENTITY();
    PRINT CONCAT(N'Kit ', @KitSku, N' creado (ProductId ', @KitId, N') a ', @KitPrecio, N'.');
END
ELSE
BEGIN
    UPDATE dbo.Product SET
        Brand       = CASE WHEN Brand IS NULL OR LTRIM(RTRIM(Brand)) = N'' THEN @KitMarca ELSE Brand END,
        SalePrice   = ISNULL(SalePrice, @KitPrecio),
        IsPromotion = 1
    WHERE ProductId = @KitId
      AND (Brand IS NULL OR LTRIM(RTRIM(Brand)) = N'' OR SalePrice IS NULL OR IsPromotion = 0);
    SET @n = @@ROWCOUNT; SET @Cambios += @n;
    PRINT CONCAT(N'Kit ', @KitSku, N' ya existía (ProductId ', @KitId, N'): datos que faltaban puestos: ', @n, N' fila(s). Nombre y precio no se tocan.');
END

-- Componentes: agrega los que faltan y corrige la cantidad
UPDATE k SET k.Quantity = c.Cantidad, k.SortOrder = c.Orden
FROM dbo.ProductKitComponent k JOIN @CompOk c ON c.ProductId = k.ComponentProductId
WHERE k.KitProductId = @KitId AND (k.Quantity <> c.Cantidad);
SET @n = @@ROWCOUNT; SET @Cambios += @n; IF @n > 0 PRINT CONCAT(N'Cantidades de componentes corregidas: ', @n);
INSERT dbo.ProductKitComponent (TenantId, KitProductId, ComponentProductId, Quantity, SortOrder)
SELECT @TenantId, @KitId, c.ProductId, c.Cantidad, c.Orden FROM @CompOk c
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductKitComponent k WHERE k.KitProductId = @KitId AND k.ComponentProductId = c.ProductId);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Componentes agregados: ', @n);

SELECT p.Sku AS [Componente de más (no se quita; revíselo en la ficha del kit)], k.Quantity
FROM dbo.ProductKitComponent k JOIN dbo.Product p ON p.ProductId = k.ComponentProductId
WHERE k.KitProductId = @KitId AND k.ComponentProductId NOT IN (SELECT ProductId FROM @CompOk);

-- Cómo queda
SELECT p.Sku, p.Name AS [Nombre], p.SalePrice AS [Precio], p.Brand AS [Marca], p.IsPromotion AS [Es oferta], p.PromoFrom AS [Desde], p.PromoTo AS [Hasta],
       CASE WHEN p.PromoImageFileId IS NULL THEN N'FALTA (paso manual)' ELSE N'tiene' END AS [Foto]
FROM dbo.Product p WHERE p.ProductId = @KitId;
PRINT N'PASO MANUAL: poner la foto de la oferta (Almacén → Productos e inventario → Precios y ofertas → OFERTA1 → «Editar oferta» → «Poner foto»).';

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
