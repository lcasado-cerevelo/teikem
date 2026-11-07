/* ============================================================================================================================
   05-forma-de-pago-clientes.sql — Pedido de vendedores (lote 228, P8): la forma de pago de los clientes de Advance Solutions.
   ----------------------------------------------------------------------------------------------------------------------------
   Cómo funciona en Teikem (lote 222): la «forma de pago» del cliente ES su término de pago (Client.PaymentTermLookupId, catálogo
   PaymentTerm, el que se migró de QuickBooks). El pedido la prellena así:
       CHEQUE → contra entrega: cheque · CASH → contra entrega: efectivo · ACH → contra entrega: ACH · COD → contra entrega (el vendedor
       escoge cómo) · NET15/NET20/NET30/NET45/NET60 → crédito con ese plazo · PK_BY_REP → crédito «vendedor recoge cheque» ·
       CONSIGNMENT → consignación (la orden nace marcada «en consignación») · sin término → el vendedor la escoge en cada pedido.
   Qué hace:
     1. Informa cuántos clientes hay con cada término y la lista de los que NO tienen (en QuickBooks venían 10 en blanco).
     2. Aplica la lista @Cambios (número de cliente → término), si la llena: para corregir clientes puntuales. Viene VACÍA.
     3. Si pone @TerminoSinTermino (p. ej. 'COD'), se lo pone a los clientes activos que no tienen ninguno. Por omisión NULL = no los toca.
   No toca el crédito (límite), ni los clientes prospecto, ni ningún otro dato. Idempotente: la segunda corrida cambia 0 filas.
   (Estos cambios no pasan por la bitácora de la pantalla; para uno o dos clientes, mejor la ficha del cliente → Perfil → Término de pago.)
   Cómo correrlo: SSMS → base de Teikem → F5. Por omisión SIMULA (@Aplicar = 0); revise y ponga @Aplicar = 1.
     sqlcmd: sqlcmd -S <servidor> -d <base> -E -I -b -i 05-forma-de-pago-clientes.sql
   ============================================================================================================================ */
SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON;
GO
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @Aplicar  BIT = 0;                              -- 0 = SIMULACIÓN (deshace al final) · 1 = APLICA
DECLARE @Compania NVARCHAR(200) = N'Advance Solutions';
DECLARE @TerminoSinTermino NVARCHAR(40) = NULL;         -- p. ej. N'COD'; NULL = los clientes sin término se quedan así

-- >>> LLENAR (opcional): número de cliente en Teikem (Client.Code, el «Customer #») → código del término
--     (COD, CHEQUE, CASH, ACH, NET15, NET20, NET30, NET45, NET60, PK_BY_REP, CONSIGNMENT).
DECLARE @Cambios TABLE (ClientCode NVARCHAR(30) NOT NULL, TermCode NVARCHAR(40) NOT NULL);
-- INSERT @Cambios VALUES (N'FARMACIA-UNO', N'NET30'), (N'FARMACIA-DOS', N'CONSIGNMENT');
-- <<<

DECLARE @TenantId INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = @Compania AND IsActive = 1), @msg NVARCHAR(800), @Filas INT = 0, @n INT;
IF @TenantId IS NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: no existe la compañía activa «', @Compania, N'» en la base ', DB_NAME(), N'. Corra 00-verificar.sql para ver los nombres y corrija @Compania.');
    THROW 50001, @msg, 1;
END
PRINT CONCAT(N'05-forma-de-pago-clientes · ', @@SERVERNAME, N' / ', DB_NAME(), N' · compañía «', @Compania, N'» (', @TenantId, N') · ', CASE WHEN @Aplicar = 1 THEN N'APLICA' ELSE N'SIMULACIÓN' END);

DECLARE @Prospect INT = (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity = N'ClientStatus' AND InternalCode = N'PROSPECT');

-- Validaciones de lo que se llenó
SELECT @msg = STRING_AGG(CONVERT(NVARCHAR(MAX), x.TermCode), N', ') FROM (
    SELECT DISTINCT TermCode FROM @Cambios UNION SELECT @TerminoSinTermino WHERE @TerminoSinTermino IS NOT NULL) x
WHERE NOT EXISTS (SELECT 1 FROM dbo.LookupCode l WHERE l.Entity = N'PaymentTerm' AND l.InternalCode = x.TermCode AND l.IsActive = 1);
IF @msg IS NOT NULL
BEGIN
    SET @msg = CONCAT(N'ABORTADO: estos términos no existen en el catálogo PaymentTerm: ', @msg, N'. No se cambió nada.');
    THROW 50002, @msg, 1;
END
SELECT x.ClientCode AS [Número de cliente de @Cambios que NO existe (se ignora)] FROM @Cambios x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Client c WHERE c.TenantId = @TenantId AND c.Code = x.ClientCode);

-- 1) Cómo están
SELECT ISNULL(l.InternalCode, N'(sin término)') AS [Término de pago], JSON_VALUE(l.LabelJson, '$.es') AS [Se ve como], COUNT(*) AS [Clientes activos]
FROM dbo.Client c LEFT JOIN dbo.LookupCode l ON l.LookupCodeId = c.PaymentTermLookupId
WHERE c.TenantId = @TenantId AND c.IsActive = 1 GROUP BY l.InternalCode, l.LabelJson ORDER BY l.InternalCode;

BEGIN TRAN;

-- 2) Cambios puntuales
UPDATE c SET c.PaymentTermLookupId = l.LookupCodeId, c.UpdatedAtUtc = SYSUTCDATETIME()
FROM dbo.Client c
JOIN @Cambios x ON x.ClientCode = c.Code
JOIN dbo.LookupCode l ON l.Entity = N'PaymentTerm' AND l.InternalCode = x.TermCode
WHERE c.TenantId = @TenantId AND (c.PaymentTermLookupId IS NULL OR c.PaymentTermLookupId <> l.LookupCodeId);
SET @n = @@ROWCOUNT; SET @Filas += @n; PRINT CONCAT(N'Clientes con término cambiado (lista @Cambios): ', @n);

-- 3) Los que no tienen ninguno (solo si se pidió)
IF @TerminoSinTermino IS NOT NULL
BEGIN
    UPDATE c SET c.PaymentTermLookupId = l.LookupCodeId, c.UpdatedAtUtc = SYSUTCDATETIME()
    FROM dbo.Client c JOIN dbo.LookupCode l ON l.Entity = N'PaymentTerm' AND l.InternalCode = @TerminoSinTermino
    WHERE c.TenantId = @TenantId AND c.IsActive = 1 AND c.PaymentTermLookupId IS NULL AND c.StatusCodeId <> @Prospect;
    SET @n = @@ROWCOUNT; SET @Filas += @n; PRINT CONCAT(N'Clientes sin término que reciben ', @TerminoSinTermino, N': ', @n);
END

SELECT c.Code AS [Cliente activo SIN término (el vendedor escoge la forma de pago en cada pedido)], c.Name AS [Nombre]
FROM dbo.Client c WHERE c.TenantId = @TenantId AND c.IsActive = 1 AND c.PaymentTermLookupId IS NULL ORDER BY c.Name;

IF @Aplicar = 1 BEGIN COMMIT; PRINT CONCAT(N'APLICADO. Filas cambiadas: ', @Filas); END
ELSE BEGIN ROLLBACK; PRINT CONCAT(N'SIMULACIÓN: se habrían cambiado ', @Filas, N' filas; no se guardó nada. Para aplicar ponga @Aplicar = 1.'); END
