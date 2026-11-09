/* ============================================================================================================================
   02-kit-oferta1.sql — Pedido de vendedores (lote 228, P8; rehecho en el lote 229): la OFERTA «OFERTA1» de Advance Solutions con su kit.
   ----------------------------------------------------------------------------------------------------------------------------
   Qué es: en el formulario de Formsite, «OFERTA1 — 6 cajas de tirillas No Coding + 1 monitor, $41.94». Desde el lote 229 una oferta es
   una cosa aparte de las listas de precios (Contabilidad → Precios y ofertas → «Ofertas»): nombre, foto, vigencia y sus renglones. Aquí
   la oferta lleva UN renglón: un kit creado PARA ella (producto tipo KIT, sin existencia propia: se reservan y despachan sus piezas) que
   solo se vende mientras la oferta esté publicada y vigente.
   Qué hace:
     - La OFERTA «OFERTA1» (si no existe una activa con ese nombre): vigente desde HOY (hora de Puerto Rico), SIN fecha de fin, para todos
       los clientes y PUBLICADA. Si ya existe, no le cambia fechas ni estado (los pudo cambiar el supervisor): solo lo informa.
     - El KIT OFERTA1 (si no existe, propio de la compañía): 6 × 52800 (tirillas) + 1 × 070120 (monitor), atado a la oferta, NO ajustable,
       precio de venta $41.94, marca PRODIGY, sin rastreo (NONE), unidad UN, la categoría y la categoría de impuesto de las tirillas.
       Si ya existe como kit: NO cambia nombre ni precio; solo pone lo que falte (marca, precio vacío, atarlo a la oferta si no lo está) y
       asegura las dos piezas con su cantidad. Una pieza de más se informa, no se quita.
     - El RENGLÓN de la oferta: el kit a $41.94 (si ya está, no le cambia el precio).
     - ABORTA (sin cambiar nada) si: falta la estructura del lote 229, el producto existe y NO es kit, el kit es de OTRA oferta, o una pieza
       no existe / está inactiva / es kit.
   Lo que NO puede hacer un script: la FOTO (la imagen es la promoción). Paso manual, después de correrlo:
     Contabilidad → Precios y ofertas → «Ofertas» → OFERTA1 → «Poner foto» (la de la oferta) y, si se quiere, la del renglón o la del
     producto (Almacén → Productos e inventario → OFERTA1 → «Foto»). JPG, PNG o WEBP.
   Idempotente: la segunda corrida cambia 0 filas. No toca inventario.
   Cómo correrlo: después de 01-marcas.sql. SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 02-kit-oferta1.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';

DECLARE @Oferta      NVARCHAR(120) = N'OFERTA1';        -- nombre de la oferta (así la ve el supervisor en «Precios y ofertas»)
DECLARE @OfertaDesde DATE = CAST(DATEADD(HOUR, -4, SYSUTCDATETIME()) AS DATE);   -- hoy en Puerto Rico (UTC−4, sin horario de verano)
DECLARE @OfertaHasta DATE = NULL;                       -- NULL = sin fecha de fin (la oferta no vence); se cambia luego en la pantalla
DECLARE @KitSku    NVARCHAR(60)  = N'OFERTA1';
DECLARE @KitNombre NVARCHAR(200) = N'OFERTA1: 6 cajas de tirillas Prodigy No Coding + 1 monitor Prodigy';
DECLARE @KitPrecio DECIMAL(18,4) = 41.94;               -- el precio de oferta (renglón) y el precio de venta del kit
DECLARE @KitMarca  NVARCHAR(100) = N'PRODIGY';          -- la misma que 01-marcas.sql pone a Prodigy
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

IF OBJECT_ID(N'dbo.Offer', N'U') IS NULL OR OBJECT_ID(N'dbo.OfferItem', N'U') IS NULL OR COL_LENGTH(N'dbo.Product', N'OfferId') IS NULL
    THROW 50005, N'ABORTADO: falta la estructura de las ofertas (lote 229: dbo.Offer, dbo.OfferItem, Product.OfferId). Instale primero esta versión (db-update).', 1;
DECLARE @KitType INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'ProductType' AND InternalCode = N'KIT');
DECLARE @Un      INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'UnitOfMeasure' AND InternalCode = N'UN');
DECLARE @None    INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'TrackingType' AND InternalCode = N'NONE');
IF @KitType IS NULL OR @Un IS NULL OR @None IS NULL
    THROW 50002, N'ABORTADO: faltan catálogos (ProductType KIT, UnitOfMeasure UN o TrackingType NONE). Instale primero esta versión (db-update).', 1;

-- Piezas: cada una debe existir una sola vez, propia, activa y no ser kit
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
    SET @msg = CONCAT(N'ABORTADO: las piezas de ', @KitSku, N' no sirven: ', @msg, N'. No se cambió nada.');
    THROW 50003, @msg, 1;
END
SELECT Sku AS [Pieza], Nombre, Cantidad AS [Cantidad por kit] FROM @CompOk ORDER BY Orden;

-- La oferta (la activa con ese nombre)
DECLARE @OfferId INT, @OfPublicada BIT, @OfDesde DATE, @OfHasta DATE;
SELECT TOP (1) @OfferId = OfferId, @OfPublicada = IsPublished, @OfDesde = ValidFrom, @OfHasta = ValidTo
FROM dbo.Offer WHERE TenantId = @TenantId AND Name = @Oferta AND IsActive = 1 ORDER BY OfferId;

-- El kit
DECLARE @KitId INT, @KitEsKit BIT, @KitActivo BIT, @KitOferta INT;
SELECT @KitId = ProductId, @KitEsKit = CASE WHEN ProductTypeLookupId = @KitType THEN 1 ELSE 0 END, @KitActivo = IsActive, @KitOferta = OfferId
FROM dbo.Product WHERE TenantId = @TenantId AND ClientId IS NULL AND Sku = @KitSku;
IF @KitId IS NOT NULL AND @KitEsKit = 0
BEGIN
    SET @msg = CONCAT(N'ABORTADO: ya existe el producto ', @KitSku, N' y no es kit. Conviértalo en kit desde su ficha (Almacén → Productos → ', @KitSku,
        N' → «Es un kit») o cambie @KitSku. No se cambió nada.');
    THROW 50004, @msg, 1;
END
IF @KitOferta IS NOT NULL AND (@OfferId IS NULL OR @KitOferta <> @OfferId)
BEGIN
    SET @msg = CONCAT(N'ABORTADO: el kit ', @KitSku, N' ya es de otra oferta («', (SELECT Name FROM dbo.Offer WHERE OfferId = @KitOferta),
        N'»). Revíselo en Precios y ofertas o cambie @Oferta. No se cambió nada.');
    THROW 50006, @msg, 1;
END
IF @KitId IS NOT NULL AND @KitActivo = 0
    PRINT CONCAT(N'AVISO: ', @KitSku, N' existe pero está INACTIVO; este script no lo reactiva (hágalo desde su ficha si se va a vender).');

BEGIN TRAN;

IF @OfferId IS NULL
BEGIN
    INSERT dbo.Offer (TenantId, Name, ValidFrom, ValidTo, IsPublished, AppliesToAll, Notes, IsActive)
    VALUES (@TenantId, @Oferta, @OfertaDesde, @OfertaHasta, 1, 1, N'Creada por script (lote 229): la OFERTA1 del formulario de Formsite.', 1);
    SET @OfferId = SCOPE_IDENTITY(); SET @Cambios += 1;
    PRINT CONCAT(N'Oferta «', @Oferta, N'» creada (id ', @OfferId, N'), publicada, desde ', CONVERT(NVARCHAR(10), @OfertaDesde, 23),
        CASE WHEN @OfertaHasta IS NULL THEN N', sin fecha de fin.' ELSE CONCAT(N' hasta ', CONVERT(NVARCHAR(10), @OfertaHasta, 23), N'.') END);
END
ELSE
    PRINT CONCAT(N'La oferta «', @Oferta, N'» ya existía (id ', @OfferId, N'): ', CASE WHEN @OfPublicada = 1 THEN N'publicada' ELSE N'EN BORRADOR' END,
        N', desde ', CONVERT(NVARCHAR(10), @OfDesde, 23), CASE WHEN @OfHasta IS NULL THEN N', sin fecha de fin' ELSE CONCAT(N' hasta ', CONVERT(NVARCHAR(10), @OfHasta, 23)) END,
        N'. No se cambian sus fechas ni su estado.');

IF @KitId IS NULL
BEGIN
    INSERT dbo.Product (TenantId, ClientId, Sku, Name, ProductCategoryId, BaseUomLookupId, TrackingTypeLookupId, SalePrice, IsActive, Brand,
                        ProductTypeLookupId, KitAdjustable, OfferId, TaxCategoryLookupId)
    SELECT @TenantId, NULL, @KitSku, @KitNombre, p.ProductCategoryId, @Un, @None, @KitPrecio, 1, @KitMarca,
           @KitType, 0, @OfferId, p.TaxCategoryLookupId
    FROM dbo.Product p WHERE p.ProductId = (SELECT ProductId FROM @CompOk WHERE Orden = 1);
    SET @n = @@ROWCOUNT; SET @Cambios += @n;
    SET @KitId = SCOPE_IDENTITY();
    PRINT CONCAT(N'Kit ', @KitSku, N' creado (ProductId ', @KitId, N'), atado a la oferta, no ajustable, a ', @KitPrecio, N'.');
END
ELSE
BEGIN
    UPDATE dbo.Product SET
        Brand     = CASE WHEN Brand IS NULL OR LTRIM(RTRIM(Brand)) = N'' THEN @KitMarca ELSE Brand END,
        SalePrice = ISNULL(SalePrice, @KitPrecio),
        OfferId   = ISNULL(OfferId, @OfferId)
    WHERE ProductId = @KitId
      AND (Brand IS NULL OR LTRIM(RTRIM(Brand)) = N'' OR SalePrice IS NULL OR OfferId IS NULL);
    SET @n = @@ROWCOUNT; SET @Cambios += @n;
    PRINT CONCAT(N'Kit ', @KitSku, N' ya existía (ProductId ', @KitId, N'): datos que faltaban puestos: ', @n, N' fila(s). Nombre y precio no se tocan.');
END

-- Piezas: agrega las que faltan y corrige la cantidad
UPDATE k SET k.Quantity = c.Cantidad, k.SortOrder = c.Orden
FROM dbo.ProductKitComponent k JOIN @CompOk c ON c.ProductId = k.ComponentProductId
WHERE k.KitProductId = @KitId AND (k.Quantity <> c.Cantidad);
SET @n = @@ROWCOUNT; SET @Cambios += @n; IF @n > 0 PRINT CONCAT(N'Cantidades de piezas corregidas: ', @n);
INSERT dbo.ProductKitComponent (TenantId, KitProductId, ComponentProductId, Quantity, SortOrder)
SELECT @TenantId, @KitId, c.ProductId, c.Cantidad, c.Orden FROM @CompOk c
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductKitComponent k WHERE k.KitProductId = @KitId AND k.ComponentProductId = c.ProductId);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Piezas agregadas: ', @n);

SELECT p.Sku AS [Pieza de más (no se quita; revísela en la ficha del kit)], k.Quantity
FROM dbo.ProductKitComponent k JOIN dbo.Product p ON p.ProductId = k.ComponentProductId
WHERE k.KitProductId = @KitId AND k.ComponentProductId NOT IN (SELECT ProductId FROM @CompOk);

-- El renglón de la oferta: el kit a su precio de oferta (si ya está, no se cambia el precio)
INSERT dbo.OfferItem (TenantId, OfferId, ProductId, Price, IsActive)
SELECT @TenantId, @OfferId, @KitId, @KitPrecio, 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.OfferItem i WHERE i.OfferId = @OfferId AND i.ProductId = @KitId AND i.IsActive = 1);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Renglón de la oferta agregado: ', @n);

-- Cómo queda
SELECT o.Name AS [Oferta], o.ValidFrom AS [Desde], COALESCE(CONVERT(NVARCHAR(10), o.ValidTo, 23), N'sin fecha de fin') AS [Hasta],
       o.IsPublished AS [Publicada], o.AppliesToAll AS [Todos los clientes], p.Sku AS [Kit], i.Price AS [Precio de oferta], p.KitAdjustable AS [Ajustable],
       CASE WHEN o.ImageFileId IS NULL THEN N'FALTA (paso manual)' ELSE N'tiene' END AS [Foto de la oferta]
FROM dbo.Offer o
JOIN dbo.OfferItem i ON i.OfferId = o.OfferId AND i.IsActive = 1 AND i.ProductId = @KitId
JOIN dbo.Product p ON p.ProductId = i.ProductId
WHERE o.OfferId = @OfferId;
PRINT N'PASO MANUAL: poner la foto de la oferta (Contabilidad → Precios y ofertas → «Ofertas» → OFERTA1 → «Poner foto»).';

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
