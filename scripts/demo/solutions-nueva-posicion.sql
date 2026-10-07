-- Demo (2026-10-07): crea la posicion A-01 en el almacen de Advance Solutions (ALM-SOL), que solo tiene la posicion GENERAL.
-- Sirve para mostrar el conteo por producto con el producto en MAS DE UNA posicion (escenario E de docs/demo-dani.md).
--   - Solo crea la posicion (vacia). Es idempotente: si A-01 ya existe, no hace nada.
--   - Las existencias NO se tocan aqui: se pasan con Transferencias en la web (queda en el kardex): ver el libreto.
-- Uso (en una consola, con SQL Server local y autenticacion de Windows):
--   sqlcmd -S localhost -E -C -d Teikem -i "F:\Visual Studio 2022\Projects\teikem\scripts\demo\solutions-nueva-posicion.sql"
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON; -- sqlcmd lo trae apagado y la tabla tiene un indice filtrado (si no, el INSERT falla con el error 1934)

DECLARE @wh INT =
(
    SELECT w.WarehouseId
    FROM dbo.Warehouse w
    JOIN dbo.Tenant t ON t.TenantId = w.TenantId
    WHERE t.Name = N'Advance Solutions' AND w.Code = N'ALM-SOL'
);
IF @wh IS NULL THROW 50000, N'No encuentro el almacen ALM-SOL de Advance Solutions en esta base.', 1;

DECLARE @zone INT = (SELECT WarehouseZoneId FROM dbo.WarehouseZone WHERE WarehouseId = @wh AND Code = N'GEN');
IF @zone IS NULL THROW 50000, N'No encuentro la zona GEN del almacen de Solutions.', 1;

IF EXISTS (SELECT 1 FROM dbo.WarehouseBin WHERE WarehouseId = @wh AND Code = N'A-01')
    PRINT N'La posicion A-01 ya existe en Solutions: no se cambio nada.';
ELSE
BEGIN
    -- Cupo 100 (para el aviso de cupo en Recibo directo); sin existencia.
    INSERT INTO dbo.WarehouseBin (WarehouseZoneId, WarehouseId, Code, Aisle, Position, IsActive, MaxCapacityQty)
    VALUES (@zone, @wh, N'A-01', N'A', N'01', 1, 100);
    PRINT N'Posicion A-01 creada en Solutions (zona GEN, cupo 100).';
END

SELECT b.Code AS Posicion, z.Code AS Zona, b.MaxCapacityQty AS Cupo, b.IsActive AS Activa
FROM dbo.WarehouseBin b
JOIN dbo.WarehouseZone z ON z.WarehouseZoneId = b.WarehouseZoneId
WHERE b.WarehouseId = @wh
ORDER BY b.Code;
