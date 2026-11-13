SET NOCOUNT ON; SET XACT_ABORT ON;
-- Solo para la copia de grabación del video de Advance Solutions. Aborta si la base no es Teikem_video*. Idempotente.
-- El almacén de Solutions estaba en recibo «Con acomodo» sin posición de recepción (no deja recibir); para el video se pasa a «Directo a posición» y se corrige el nombre.
IF DB_NAME() NOT LIKE N'Teikem[_]video%' THROW 50001, N'Este script es solo para una base Teikem_video*.', 1;
DECLARE @T INT = (SELECT TenantId FROM dbo.Tenant WHERE Name = N'Advance Solutions' AND IsActive = 1);
IF @T IS NULL THROW 50002, N'No existe la compañía Advance Solutions.', 1;
DECLARE @Directo INT = (SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = N'ReceivingMode' AND InternalCode = N'DIRECT');
IF @Directo IS NULL THROW 50003, N'No existe el modo de recepción DIRECT.', 1;
UPDATE dbo.Warehouse SET ReceivingModeLookupId = @Directo, Name = N'Almacén Solutions'
WHERE TenantId = @T AND Code = N'ALM-SOLUTIONS' AND (ReceivingModeLookupId <> @Directo OR Name <> N'Almacén Solutions');
PRINT CONCAT(N'Almacén actualizado (filas): ', @@ROWCOUNT);
