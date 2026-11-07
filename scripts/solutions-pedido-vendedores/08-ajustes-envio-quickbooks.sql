/* ============================================================================================================================
   08-ajustes-envio-quickbooks.sql — Pedido de vendedores (lote 228, P8): ajustes de Advance Solutions (Sistema → Ajustes → Operación):
   la regla de envío ($12, gratis desde $120) y QuickBooks (términos de pago con su nombre en QuickBooks; cuentas e ítems opcionales).
   ----------------------------------------------------------------------------------------------------------------------------
   Envío (sección «Ventas»): cargo de $12.00; gratis cuando el total del pedido DESPUÉS del IVU llega a $120.00; solo en entrega (en
   recogido no se cobra). Cada cliente puede tener su propia regla en su ficha (usa la de la compañía / no se le cobra / regla propia).
     - Solo PONE el cargo y el mínimo si la compañía no tiene uno configurado (NULL); si ya hay otro valor, lo informa y no lo toca,
       salvo @Sobrescribir = 1.
     - «El cargo de envío lleva IVU»: lo decide el contador. @EnvioLlevaIvu = NULL no lo toca (hoy: no lleva); 1 = lleva; 0 = no lleva.
   QuickBooks (sección «QuickBooks»; QuickBooks Desktop, archivo IIF):
     - Términos: agrega al mapa «término de Teikem → nombre en QuickBooks» los nombres que tenían los clientes en QuickBooks Solutions
       (Solutions Customers.csv): CHEQUE, CASH, ACH, Net 15, Net 20, Net 30, Net 45, Consignment, PK BY REP. Solo agrega los términos
       que el mapa no tenga; lo que ya puso el administrador no se toca. COD y NET60 no existían en QuickBooks Solutions: se quedan sin
       nombre (si un cliente los usa, esas facturas salen sin término; póngalo en la pantalla si hace falta).
     - Cuentas e ítems: NULL = el valor por omisión de Teikem (Accounts Receivable · Sales · Cost of Goods Sold · Inventory Asset ·
       Sales Tax Payable · ítem de IVU «IVU» · ítem de envío «Shipping»). Los nombres tienen que ser EXACTOS a los de QuickBooks; llene
       las variables de abajo con los reales (Lista de cuentas y Lista de ítems de QuickBooks) o déjelas en NULL y póngalas luego en la
       pantalla. Solo se ponen las que llene y estén vacías (o con @Sobrescribir = 1).
   Idempotente: la segunda corrida cambia 0 filas.
   Cómo correrlo: SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 08-ajustes-envio-quickbooks.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';
DECLARE @Sobrescribir BIT = 0;                          -- 1 = pone los valores de abajo aunque ya haya otros

-- Envío
DECLARE @CargoEnvio    DECIMAL(18,2) = 12.00;
DECLARE @GratisDesde   DECIMAL(18,2) = 120.00;
DECLARE @EnvioLlevaIvu BIT = NULL;                      -- NULL = no tocar (pregunta para el contador)

-- QuickBooks: cuentas e ítems (NULL = valor por omisión de Teikem). Nombres EXACTOS de QuickBooks (subcuentas con «:», p. ej. N'Sales:Monitors').
DECLARE @QbCuentaPorCobrar NVARCHAR(159) = NULL;        -- por omisión: Accounts Receivable
DECLARE @QbCuentaVentas    NVARCHAR(159) = NULL;        -- por omisión: Sales
DECLARE @QbCuentaCosto     NVARCHAR(159) = NULL;        -- por omisión: Cost of Goods Sold   (la que usan los ítems de Solutions)
DECLARE @QbCuentaInventario NVARCHAR(159) = NULL;       -- por omisión: Inventory Asset      (la que usan los ítems de Solutions)
DECLARE @QbCuentaIvu       NVARCHAR(159) = NULL;        -- por omisión: Sales Tax Payable
DECLARE @QbItemIvu         NVARCHAR(31)  = NULL;        -- por omisión: IVU       (el ítem de impuesto de QuickBooks)
DECLARE @QbItemEnvio       NVARCHAR(31)  = NULL;        -- por omisión: Shipping  (debe existir en QuickBooks como ítem de servicio)

-- Términos de Teikem → nombre en QuickBooks Solutions (tal como venían en Solutions Customers.csv)
DECLARE @Terminos TABLE (Code NVARCHAR(40) NOT NULL PRIMARY KEY, QbName NVARCHAR(31) NOT NULL);
INSERT @Terminos VALUES
    (N'CHEQUE', N'CHEQUE'), (N'CASH', N'CASH'), (N'ACH', N'ACH'),
    (N'NET15', N'Net 15'), (N'NET20', N'Net 20'), (N'NET30', N'Net 30'), (N'NET45', N'Net 45'),
    (N'CONSIGNMENT', N'Consignment'), (N'PK_BY_REP', N'PK BY REP');

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(800), @Cambios INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'08-ajustes-envio-quickbooks · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);
IF @CargoEnvio IS NOT NULL AND @CargoEnvio <= 0 THROW 50002, N'ABORTADO: el cargo de envío debe ser mayor que cero (o NULL para no tocarlo).', 1;
IF @GratisDesde IS NOT NULL AND @GratisDesde < 0 THROW 50003, N'ABORTADO: el mínimo para envío gratis no puede ser negativo (o NULL para no tocarlo).', 1;

DECLARE @Json NVARCHAR(2000) = (SELECT QuickBooksTermsJson FROM dbo.Tenant WHERE TenantId = @TenantId);
IF @Json IS NOT NULL AND LTRIM(RTRIM(@Json)) <> N'' AND ISJSON(@Json) = 0
BEGIN
    SET @msg = CONCAT(N'ABORTADO: el mapa de términos de QuickBooks guardado no es JSON válido: ', LEFT(@Json, 300), N'. Corríjalo en Ajustes → Operación → QuickBooks. No se cambió nada.');
    THROW 50004, @msg, 1;
END

-- Cómo está antes
SELECT SalesShippingCharge AS [Envío: cargo (antes)], SalesShippingFreeFrom AS [Envío: gratis desde (antes)], SalesShippingTaxable AS [Envío lleva IVU (antes)],
       QuickBooksTermsJson AS [QB términos (antes)] FROM dbo.Tenant WHERE TenantId = @TenantId;

BEGIN TRAN;

-- Envío
UPDATE dbo.Tenant SET
    SalesShippingCharge   = CASE WHEN @CargoEnvio  IS NOT NULL AND (SalesShippingCharge   IS NULL OR @Sobrescribir = 1) THEN @CargoEnvio  ELSE SalesShippingCharge END,
    SalesShippingFreeFrom = CASE WHEN @GratisDesde IS NOT NULL AND (SalesShippingFreeFrom IS NULL OR @Sobrescribir = 1) THEN @GratisDesde ELSE SalesShippingFreeFrom END,
    SalesShippingTaxable  = ISNULL(@EnvioLlevaIvu, SalesShippingTaxable)
WHERE TenantId = @TenantId
  AND (   (@CargoEnvio  IS NOT NULL AND (SalesShippingCharge   IS NULL OR (@Sobrescribir = 1 AND SalesShippingCharge   <> @CargoEnvio)))
       OR (@GratisDesde IS NOT NULL AND (SalesShippingFreeFrom IS NULL OR (@Sobrescribir = 1 AND SalesShippingFreeFrom <> @GratisDesde)))
       OR (@EnvioLlevaIvu IS NOT NULL AND SalesShippingTaxable <> @EnvioLlevaIvu));
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Regla de envío de la compañía actualizada: ', @n, N' fila(s).');
SELECT N'AVISO: la compañía ya tenía otra regla de envío; no se tocó (use @Sobrescribir = 1 si debe cambiarse).' AS [Aviso], SalesShippingCharge, SalesShippingFreeFrom
FROM dbo.Tenant WHERE TenantId = @TenantId AND @Sobrescribir = 0
  AND ((@CargoEnvio IS NOT NULL AND SalesShippingCharge <> @CargoEnvio) OR (@GratisDesde IS NOT NULL AND SalesShippingFreeFrom <> @GratisDesde));

-- Cuentas e ítems de QuickBooks (solo las que se llenaron)
UPDATE dbo.Tenant SET
    QuickBooksReceivableAccount = CASE WHEN @QbCuentaPorCobrar  IS NOT NULL AND (QuickBooksReceivableAccount IS NULL OR @Sobrescribir = 1) THEN @QbCuentaPorCobrar  ELSE QuickBooksReceivableAccount END,
    QuickBooksSalesAccount      = CASE WHEN @QbCuentaVentas     IS NOT NULL AND (QuickBooksSalesAccount      IS NULL OR @Sobrescribir = 1) THEN @QbCuentaVentas     ELSE QuickBooksSalesAccount END,
    QuickBooksCostAccount       = CASE WHEN @QbCuentaCosto      IS NOT NULL AND (QuickBooksCostAccount       IS NULL OR @Sobrescribir = 1) THEN @QbCuentaCosto      ELSE QuickBooksCostAccount END,
    QuickBooksInventoryAccount  = CASE WHEN @QbCuentaInventario IS NOT NULL AND (QuickBooksInventoryAccount  IS NULL OR @Sobrescribir = 1) THEN @QbCuentaInventario ELSE QuickBooksInventoryAccount END,
    QuickBooksTaxAccount        = CASE WHEN @QbCuentaIvu        IS NOT NULL AND (QuickBooksTaxAccount        IS NULL OR @Sobrescribir = 1) THEN @QbCuentaIvu        ELSE QuickBooksTaxAccount END,
    QuickBooksTaxItem           = CASE WHEN @QbItemIvu          IS NOT NULL AND (QuickBooksTaxItem           IS NULL OR @Sobrescribir = 1) THEN @QbItemIvu          ELSE QuickBooksTaxItem END,
    QuickBooksShippingItem      = CASE WHEN @QbItemEnvio        IS NOT NULL AND (QuickBooksShippingItem      IS NULL OR @Sobrescribir = 1) THEN @QbItemEnvio        ELSE QuickBooksShippingItem END
WHERE TenantId = @TenantId
  AND (   (@QbCuentaPorCobrar  IS NOT NULL AND (QuickBooksReceivableAccount IS NULL OR (@Sobrescribir = 1 AND QuickBooksReceivableAccount <> @QbCuentaPorCobrar)))
       OR (@QbCuentaVentas     IS NOT NULL AND (QuickBooksSalesAccount      IS NULL OR (@Sobrescribir = 1 AND QuickBooksSalesAccount      <> @QbCuentaVentas)))
       OR (@QbCuentaCosto      IS NOT NULL AND (QuickBooksCostAccount       IS NULL OR (@Sobrescribir = 1 AND QuickBooksCostAccount       <> @QbCuentaCosto)))
       OR (@QbCuentaInventario IS NOT NULL AND (QuickBooksInventoryAccount  IS NULL OR (@Sobrescribir = 1 AND QuickBooksInventoryAccount  <> @QbCuentaInventario)))
       OR (@QbCuentaIvu        IS NOT NULL AND (QuickBooksTaxAccount        IS NULL OR (@Sobrescribir = 1 AND QuickBooksTaxAccount        <> @QbCuentaIvu)))
       OR (@QbItemIvu          IS NOT NULL AND (QuickBooksTaxItem           IS NULL OR (@Sobrescribir = 1 AND QuickBooksTaxItem           <> @QbItemIvu)))
       OR (@QbItemEnvio        IS NOT NULL AND (QuickBooksShippingItem      IS NULL OR (@Sobrescribir = 1 AND QuickBooksShippingItem      <> @QbItemEnvio))));
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Cuentas / ítems de QuickBooks actualizados: ', @n, N' fila(s).');

-- Mapa de términos: lo que ya hay + los que faltan (solo códigos que existen en el catálogo PaymentTerm). Mismo formato que guarda el API.
DECLARE @Mapa TABLE (Code NVARCHAR(40) NOT NULL PRIMARY KEY, QbName NVARCHAR(400) NOT NULL);
IF @Json IS NOT NULL AND ISJSON(@Json) = 1
    INSERT @Mapa SELECT UPPER(LTRIM(RTRIM([key]))), LTRIM(RTRIM([value])) FROM OPENJSON(@Json)
    WHERE type = 1 AND LTRIM(RTRIM([key])) <> N'' AND LTRIM(RTRIM([value])) <> N'';
INSERT @Mapa SELECT t.Code, t.QbName FROM @Terminos t
WHERE NOT EXISTS (SELECT 1 FROM @Mapa m WHERE m.Code = t.Code)
  AND EXISTS (SELECT 1 FROM dbo.LookupCode l WHERE l.Entity = N'PaymentTerm' AND l.InternalCode = t.Code AND l.IsActive = 1);
DECLARE @NuevoJson NVARCHAR(2000) = (
    SELECT CONCAT(N'{', STRING_AGG(CONVERT(NVARCHAR(MAX), CONCAT(N'"', STRING_ESCAPE(Code, 'json'), N'":"', STRING_ESCAPE(QbName, 'json'), N'"')), N',')
                  WITHIN GROUP (ORDER BY Code COLLATE Latin1_General_BIN2), N'}')
    FROM @Mapa HAVING COUNT(*) > 0);
UPDATE dbo.Tenant SET QuickBooksTermsJson = @NuevoJson
WHERE TenantId = @TenantId AND @NuevoJson IS NOT NULL
  AND (QuickBooksTermsJson IS NULL OR QuickBooksTermsJson COLLATE Latin1_General_BIN2 <> @NuevoJson COLLATE Latin1_General_BIN2);
SET @n = @@ROWCOUNT; SET @Cambios += @n; PRINT CONCAT(N'Mapa de términos de QuickBooks actualizado: ', @n, N' fila(s).');

SELECT l.InternalCode AS [Término de Teikem], JSON_VALUE(l.LabelJson, '$.es') AS [Se ve como], ISNULL(m.QbName, N'(sin nombre: sale sin término)') AS [Nombre en QuickBooks],
       (SELECT COUNT(*) FROM dbo.Client c WHERE c.TenantId = @TenantId AND c.IsActive = 1 AND c.PaymentTermLookupId = l.LookupCodeId) AS [Clientes]
FROM dbo.LookupCode l LEFT JOIN @Mapa m ON m.Code = l.InternalCode
WHERE l.Entity = N'PaymentTerm' AND l.IsActive = 1 ORDER BY l.SortOrder, l.InternalCode;

SELECT SalesShippingCharge AS [Envío: cargo], SalesShippingFreeFrom AS [Envío: gratis desde], SalesShippingTaxable AS [Envío lleva IVU],
       ISNULL(QuickBooksReceivableAccount, N'(Accounts Receivable)') AS [QB por cobrar], ISNULL(QuickBooksSalesAccount, N'(Sales)') AS [QB ventas],
       ISNULL(QuickBooksCostAccount, N'(Cost of Goods Sold)') AS [QB costo], ISNULL(QuickBooksInventoryAccount, N'(Inventory Asset)') AS [QB inventario],
       ISNULL(QuickBooksTaxAccount, N'(Sales Tax Payable)') AS [QB cuenta IVU], ISNULL(QuickBooksTaxItem, N'(IVU)') AS [QB ítem IVU],
       ISNULL(QuickBooksShippingItem, N'(Shipping)') AS [QB ítem envío]
FROM dbo.Tenant WHERE TenantId = @TenantId;

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Cambios); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Cambios, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
