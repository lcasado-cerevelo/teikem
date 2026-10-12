/* ============================================================================
   REVISAR (SOLO LECTURA) las tablas de la capa vieja de Rentas (16C anterior al Lote 27, 2026-10-11).
   No cambia nada: solo cuenta filas. Córralo en la base de PRODUCCIÓN (cambie USE si hace falta) y mire el resultado.

   Tablas viejas: RentalAsset, RentalAssetMaintenance, RentalContract, RentalBillingRule y el RentalCharge de esa capa.
   (Las tablas NUEVAS son Rental, RentalLine, RentalExtension, RentalLineRate, RentalReturn, RentalReturnLine, RentalProcess y RentalCharge nuevo;
   esas SÍ se usan: no las toque.)

   Cómo leer el resultado:
   - «No existe» -> esa tabla vieja ya no está (nada que hacer).
   - Existe y con 0 filas -> está vacía; se puede retirar con calma (pídaselo a Claude: se hace con una sección nueva, idempotente, de logistica-db-update.sql).
   - Existe y con filas -> HAY DATOS de la capa vieja: NO se retira; decidir con el negocio qué hacer con ellos antes de tocar nada.
   ============================================================================ */
SET NOCOUNT ON;

DECLARE @t TABLE (Tabla SYSNAME, Existe BIT, Filas BIGINT NULL);

DECLARE @nombres TABLE (n SYSNAME);
INSERT INTO @nombres VALUES (N'RentalAsset'), (N'RentalAssetMaintenance'), (N'RentalContract'), (N'RentalBillingRule');

DECLARE @n SYSNAME, @sql NVARCHAR(400), @c BIGINT;
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT n FROM @nombres;
OPEN cur;
FETCH NEXT FROM cur INTO @n;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @n, 'U') IS NULL
        INSERT INTO @t VALUES (@n, 0, NULL);
    ELSE
    BEGIN
        SET @sql = N'SELECT @c = COUNT_BIG(*) FROM dbo.' + QUOTENAME(@n);
        EXEC sp_executesql @sql, N'@c BIGINT OUTPUT', @c = @c OUTPUT;
        INSERT INTO @t VALUES (@n, 1, @c);
    END
    FETCH NEXT FROM cur INTO @n;
END
CLOSE cur;
DEALLOCATE cur;

-- RentalCharge existe en las dos capas: el de la capa VIEJA es el que tiene la columna RentalContractId (el nuevo cuelga de RentalId).
IF OBJECT_ID(N'dbo.RentalCharge', 'U') IS NULL
    INSERT INTO @t VALUES (N'RentalCharge (capa vieja)', 0, NULL);
ELSE IF COL_LENGTH('dbo.RentalCharge', 'RentalContractId') IS NULL
    INSERT INTO @t VALUES (N'RentalCharge (capa vieja)', 0, NULL);   -- es el nuevo
ELSE
BEGIN
    SELECT @c = COUNT_BIG(*) FROM dbo.RentalCharge;
    INSERT INTO @t VALUES (N'RentalCharge (capa vieja)', 1, @c);
END

SELECT Tabla,
       CASE WHEN Existe = 0 THEN N'No existe'
            WHEN Filas = 0 THEN N'Existe y está VACÍA'
            ELSE N'Existe y TIENE ' + CAST(Filas AS NVARCHAR(30)) + N' fila(s): NO retirar' END AS Resultado
FROM @t
ORDER BY Tabla;
