using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Wms;

/// <summary>
/// Lote 6 (P0) — consultas con bloqueo del inventario y del almacén. Es el ÚNICO lugar del lote con SQL crudo (19 sentencias; la 18
/// es la renta, Lote 27, y la 19 el proceso del equipo devuelto, Lote 28)
/// y cada una lleva 'TenantId =' explícito (RawSqlConfinementTests lo verifica): el SQL crudo no pasa por el filtro global.
///
/// ORDEN DE BLOQUEO ÚNICO del lote (evita interbloqueos entre recibir, completar tareas, contar, recolectar, cruzar y comprar):
///   1. Encabezado del documento, en este orden:
///      PickBatch &lt; CrossDockPlan &lt; CycleCount &lt; ReceiptHeader &lt; Asn &lt; PurchaseOrder &lt; Rental &lt; Product &lt; Warehouse &lt; WarehouseDock.
///      (Lote 27: la renta va antes del almacén, que el despacho bloquea para crear a demanda la posición EN-RENTA.)
///   2. WarehouseTask.
///   3. StockBalance, por clave ordenada (ProductId, WarehouseId, BinId, LotId): upsert con UPDLOCK + HOLDLOCK y bloqueo de fila.
///      Los rangos por producto, almacén o posición (desactivar) toman HOLDLOCK: bloquean también las filas por nacer.
///   4. InventorySerial, por SerialId ascendente (y los lotes con EnsureLot).
///   5. NumberSequence, al final.
///
/// Excepción documentada (D48): 'Recolectar' toma PACKBATCH (NumberSequence) como PRIMER bloqueo para insertar la cabecera con
/// su número EMP antes de los ISSUE. No hay ciclo: ninguna transacción bloquea saldos y después PACKBATCH, y el alta de
/// órdenes no toca saldos. Las altas de órdenes esperan a lo sumo una recolección; una recolección fallida revierte su número.
///
/// Reglas derivadas:
/// - Todo escritor de CrossDockAllocation bloquea el plan y después el recibo de la línea. La confirmación del recibo escribe
///   asignaciones bajo el bloqueo del recibo sin tocar el plan (no hay inversión).
/// - La cola de tareas llama a IWarehouseTaskHandler.LockReferencesAsync ANTES de bloquear la tarea.
/// - Desactivar producto, almacén o posición: encabezado (U), rango de saldos HOLDLOCK, verificación y SOLO ENTONCES escritura.
/// - Los interbloqueos restantes (1205) los reintenta la estrategia de ejecución (todo corre en RunInTransactionAsync).
///
/// Los bloqueos exigen una transacción abierta con proveedor relacional; con InMemory (pruebas) cargan tracked sin bloqueo.
/// Los encabezados se bloquean con 'SELECT {Pk} AS Value …' (nunca 'SELECT *': Warehouse.GeoPoint es GEOGRAPHY y no se mapea)
/// y después se cargan tracked con EF (recargados si ya estaban en el tracker sin cambios).
/// </summary>
public static class InventoryQueries
{
    public static string LotExistsWithOtherDates(string lotNumber)
        => $"El lote {lotNumber} ya existe con otras fechas; corrija las fechas o use otro número de lote.";

    public static string LotCreatedConcurrently(string lotNumber)
        => $"El lote {lotNumber} se acaba de crear en otra operación; intente de nuevo.";

    // ================================================================ (1-2) saldos por clave

    /// <summary>
    /// (1) Asegura la fila del saldo (en mano 0) con UPDLOCK + HOLDLOCK: bloquea la clave aunque aún no exista, de modo que
    /// dos movimientos concurrentes sobre una clave nueva se serializan. No-op con InMemory (el ledger la crea en el tracker).
    /// </summary>
    public static async Task UpsertBalanceAsync(this TeikemDbContext db, BalanceKey key, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        RequireTransaction(db, nameof(UpsertBalanceAsync));
        var tenantId = db.CurrentTenantId;
        var (p, w, b, l) = (key.ProductId, key.WarehouseId, key.BinId, key.LotId);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"IF NOT EXISTS (SELECT 1 FROM dbo.StockBalance WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND ProductId = {p} AND WarehouseId = {w} AND (WarehouseBinId = {b} OR (WarehouseBinId IS NULL AND {b} IS NULL)) AND (LotId = {l} OR (LotId IS NULL AND {l} IS NULL))) INSERT INTO dbo.StockBalance (TenantId, ProductId, WarehouseId, WarehouseBinId, LotId, QtyOnHand, QtyReserved, UpdatedAtUtc) VALUES ({tenantId}, {p}, {w}, {b}, {l}, 0, 0, SYSUTCDATETIME())",
            ct);
    }

    /// <summary>
    /// (2) Saldo de la clave con bloqueo de fila (UPDLOCK, ROWLOCK) hasta el fin de la transacción, tracked; null si no existe.
    /// Con InMemory devuelve el del tracker (incluidos los agregados sin guardar) o el guardado.
    /// </summary>
    public static async Task<StockBalance?> LockBalanceAsync(this TeikemDbContext db, BalanceKey key, CancellationToken ct)
    {
        var (p, w, b, l) = (key.ProductId, key.WarehouseId, key.BinId, key.LotId);
        if (!db.Database.IsRelational())
        {
            var local = db.ChangeTracker.Entries<StockBalance>()
                .Where(e => e.State != EntityState.Deleted && e.Entity.ProductId == p && e.Entity.WarehouseId == w
                            && e.Entity.WarehouseBinId == b && e.Entity.LotId == l)
                .Select(e => e.Entity).FirstOrDefault();
            if (local is not null) return local;
            return await db.StockBalances.AsTracking()
                .FirstOrDefaultAsync(x => x.ProductId == p && x.WarehouseId == w && x.WarehouseBinId == b && x.LotId == l, ct);
        }
        RequireTransaction(db, nameof(LockBalanceAsync));
        var tenantId = db.CurrentTenantId;
        var before = TrackedSnapshot<StockBalance>(db);
        var row = await db.StockBalances
            .FromSqlInterpolated($"SELECT * FROM dbo.StockBalance WITH (UPDLOCK, ROWLOCK) WHERE TenantId = {tenantId} AND ProductId = {p} AND WarehouseId = {w} AND (WarehouseBinId = {b} OR (WarehouseBinId IS NULL AND {b} IS NULL)) AND (LotId = {l} OR (LotId IS NULL AND {l} IS NULL))")
            .AsTracking()
            .FirstOrDefaultAsync(ct);
        if (row is not null) await RefreshIfStaleAsync(db, before, row, ct);
        return row;
    }

    // ================================================================ (3-5) rangos de saldos (desactivar producto, almacén, posición)

    /// <summary>(3) Todos los saldos del producto con UPDLOCK + HOLDLOCK (también las filas por nacer), tracked.</summary>
    public static async Task<IReadOnlyList<StockBalance>> LockBalancesByProductAsync(this TeikemDbContext db, int productId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.StockBalances.AsTracking().Where(b => b.ProductId == productId).OrderBy(b => b.StockBalanceId).ToListAsync(ct);
        RequireTransaction(db, nameof(LockBalancesByProductAsync));
        var tenantId = db.CurrentTenantId;
        return await db.StockBalances
            .FromSqlInterpolated($"SELECT * FROM dbo.StockBalance WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND ProductId = {productId}")
            .AsTracking().OrderBy(b => b.StockBalanceId).ToListAsync(ct);
    }

    /// <summary>(4) Todos los saldos del almacén con UPDLOCK + HOLDLOCK, tracked.</summary>
    public static async Task<IReadOnlyList<StockBalance>> LockBalancesByWarehouseAsync(this TeikemDbContext db, int warehouseId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.StockBalances.AsTracking().Where(b => b.WarehouseId == warehouseId).OrderBy(b => b.StockBalanceId).ToListAsync(ct);
        RequireTransaction(db, nameof(LockBalancesByWarehouseAsync));
        var tenantId = db.CurrentTenantId;
        return await db.StockBalances
            .FromSqlInterpolated($"SELECT * FROM dbo.StockBalance WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND WarehouseId = {warehouseId}")
            .AsTracking().OrderBy(b => b.StockBalanceId).ToListAsync(ct);
    }

    /// <summary>(5) Todos los saldos de la posición con UPDLOCK + HOLDLOCK, tracked.</summary>
    public static async Task<IReadOnlyList<StockBalance>> LockBalancesByBinAsync(this TeikemDbContext db, int binId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return await db.StockBalances.AsTracking().Where(b => b.WarehouseBinId == binId).OrderBy(b => b.StockBalanceId).ToListAsync(ct);
        RequireTransaction(db, nameof(LockBalancesByBinAsync));
        var tenantId = db.CurrentTenantId;
        return await db.StockBalances
            .FromSqlInterpolated($"SELECT * FROM dbo.StockBalance WITH (UPDLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND WarehouseBinId = {binId}")
            .AsTracking().OrderBy(b => b.StockBalanceId).ToListAsync(ct);
    }

    // ================================================================ (6-14) encabezados (paso 1 y 2 del orden de bloqueo)

    /// <summary>(6) Recolección con UPDLOCK, tracked. 404 'Recolección no encontrada.'</summary>
    public static async Task<PickBatch> LockPickBatchAsync(this TeikemDbContext db, int pickBatchId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockPickBatchAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT PickBatchId AS Value FROM dbo.PickBatch WITH (UPDLOCK, ROWLOCK) WHERE PickBatchId = {pickBatchId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Recolección", null, true), ct);
        }
        return await LoadTrackedAsync(db, db.PickBatches.Where(x => x.PickBatchId == pickBatchId), () => new NotFoundException("Recolección", null, true), ct);
    }

    /// <summary>(7) Plan de cruce de muelle con UPDLOCK, tracked. 404 'Plan de cruce de muelle no encontrado.'</summary>
    public static async Task<CrossDockPlan> LockCrossDockPlanAsync(this TeikemDbContext db, int crossDockPlanId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockCrossDockPlanAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT CrossDockPlanId AS Value FROM dbo.CrossDockPlan WITH (UPDLOCK, ROWLOCK) WHERE CrossDockPlanId = {crossDockPlanId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Plan de cruce de muelle"), ct);
        }
        return await LoadTrackedAsync(db, db.CrossDockPlans.Where(x => x.CrossDockPlanId == crossDockPlanId), () => new NotFoundException("Plan de cruce de muelle"), ct);
    }

    /// <summary>(8) Conteo cíclico con UPDLOCK, tracked. 404 'Conteo no encontrado.'</summary>
    public static async Task<CycleCount> LockCycleCountAsync(this TeikemDbContext db, int cycleCountId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockCycleCountAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT CycleCountId AS Value FROM dbo.CycleCount WITH (UPDLOCK, ROWLOCK) WHERE CycleCountId = {cycleCountId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Conteo"), ct);
        }
        return await LoadTrackedAsync(db, db.CycleCounts.Where(x => x.CycleCountId == cycleCountId), () => new NotFoundException("Conteo"), ct);
    }

    /// <summary>(9) Recibo con UPDLOCK, tracked. 404 'Recibo no encontrado.'</summary>
    public static async Task<ReceiptHeader> LockReceiptAsync(this TeikemDbContext db, int receiptHeaderId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockReceiptAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT ReceiptHeaderId AS Value FROM dbo.ReceiptHeader WITH (UPDLOCK, ROWLOCK) WHERE ReceiptHeaderId = {receiptHeaderId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Recibo"), ct);
        }
        return await LoadTrackedAsync(db, db.ReceiptHeaders.Where(x => x.ReceiptHeaderId == receiptHeaderId), () => new NotFoundException("Recibo"), ct);
    }

    /// <summary>(10) Aviso de llegada con UPDLOCK, tracked. 404 'Aviso de llegada no encontrado.'</summary>
    public static async Task<Asn> LockAsnAsync(this TeikemDbContext db, int asnId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockAsnAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT AsnId AS Value FROM dbo.Asn WITH (UPDLOCK, ROWLOCK) WHERE AsnId = {asnId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Aviso de llegada"), ct);
        }
        return await LoadTrackedAsync(db, db.Asns.Where(x => x.AsnId == asnId), () => new NotFoundException("Aviso de llegada"), ct);
    }

    /// <summary>(11) Orden de compra con UPDLOCK, tracked. 404 'Orden de compra no encontrada.'</summary>
    public static async Task<PurchaseOrder> LockPurchaseOrderAsync(this TeikemDbContext db, int purchaseOrderId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockPurchaseOrderAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT PurchaseOrderId AS Value FROM dbo.PurchaseOrder WITH (UPDLOCK, ROWLOCK) WHERE PurchaseOrderId = {purchaseOrderId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Orden de compra", null, true), ct);
        }
        return await LoadTrackedAsync(db, db.PurchaseOrders.Where(x => x.PurchaseOrderId == purchaseOrderId), () => new NotFoundException("Orden de compra", null, true), ct);
    }

    /// <summary>(18, Lote 27) Renta con UPDLOCK, tracked. 404 'Renta no encontrada.'</summary>
    public static async Task<Rental> LockRentalAsync(this TeikemDbContext db, int rentalId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockRentalAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT RentalId AS Value FROM dbo.Rental WITH (UPDLOCK, ROWLOCK) WHERE RentalId = {rentalId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Renta", null, true), ct);
        }
        return await LoadTrackedAsync(db, db.Rentals.Where(x => x.RentalId == rentalId), () => new NotFoundException("Renta", null, true), ct);
    }

    /// <summary>(19, Lote 28) Proceso de un equipo devuelto con UPDLOCK, tracked. 404 'Proceso no encontrado.'</summary>
    public static async Task<RentalProcess> LockRentalProcessAsync(this TeikemDbContext db, int rentalProcessId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockRentalProcessAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT RentalProcessId AS Value FROM dbo.RentalProcess WITH (UPDLOCK, ROWLOCK) WHERE RentalProcessId = {rentalProcessId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Proceso"), ct);
        }
        return await LoadTrackedAsync(db, db.RentalProcesses.Where(x => x.RentalProcessId == rentalProcessId), () => new NotFoundException("Proceso"), ct);
    }

    /// <summary>(12) Producto con UPDLOCK, tracked. 404 'Producto no encontrado.'</summary>
    public static async Task<Product> LockProductAsync(this TeikemDbContext db, int productId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockProductAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT ProductId AS Value FROM dbo.Product WITH (UPDLOCK, ROWLOCK) WHERE ProductId = {productId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Producto"), ct);
        }
        return await LoadTrackedAsync(db, db.Products.Where(x => x.ProductId == productId), () => new NotFoundException("Producto"), ct);
    }

    /// <summary>(13) Almacén con UPDLOCK, tracked (sin 'SELECT *': GeoPoint es GEOGRAPHY). 404 'Almacén no encontrado.'</summary>
    public static async Task<Warehouse> LockWarehouseAsync(this TeikemDbContext db, int warehouseId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockWarehouseAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT WarehouseId AS Value FROM dbo.Warehouse WITH (UPDLOCK, ROWLOCK) WHERE WarehouseId = {warehouseId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Almacén"), ct);
        }
        return await LoadTrackedAsync(db, db.Warehouses.Where(x => x.WarehouseId == warehouseId), () => new NotFoundException("Almacén"), ct);
    }

    /// <summary>(14) Tarea de almacén con UPDLOCK, tracked (paso 2 del orden de bloqueo). 404 'Tarea no encontrada.'</summary>
    public static async Task<WarehouseTask> LockWarehouseTaskAsync(this TeikemDbContext db, int warehouseTaskId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockWarehouseTaskAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT WarehouseTaskId AS Value FROM dbo.WarehouseTask WITH (UPDLOCK, ROWLOCK) WHERE WarehouseTaskId = {warehouseTaskId} AND TenantId = {tenantId}"),
                () => new NotFoundException("Tarea", null, true), ct);
        }
        return await LoadTrackedAsync(db, db.WarehouseTasks.Where(x => x.WarehouseTaskId == warehouseTaskId), () => new NotFoundException("Tarea", null, true), ct);
    }

    // ================================================================ (15) muelle

    /// <summary>
    /// (15) Muelle del almacén con UPDLOCK, con JOIN al almacén del tenant (WarehouseDock no tiene TenantId), tracked; último
    /// encabezado del orden de bloqueo. 404 'Muelle no encontrado.' si es de otro almacén o de otro tenant.
    /// </summary>
    public static async Task<WarehouseDock> LockDockAsync(this TeikemDbContext db, int warehouseId, int dockId, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(LockDockAsync));
            var tenantId = db.CurrentTenantId;
            await RequireLockedAsync(db.Database.SqlQuery<int>($"SELECT d.WarehouseDockId AS Value FROM dbo.WarehouseDock d WITH (UPDLOCK, ROWLOCK) JOIN dbo.Warehouse w ON w.WarehouseId = d.WarehouseId WHERE d.WarehouseDockId = {dockId} AND d.WarehouseId = {warehouseId} AND w.TenantId = {tenantId}"),
                () => new NotFoundException("Muelle"), ct);
        }
        var q = from d in db.WarehouseDocks
                join w in db.Warehouses on d.WarehouseId equals w.WarehouseId
                where d.WarehouseDockId == dockId && d.WarehouseId == warehouseId
                select d;
        return await LoadTrackedAsync(db, q, () => new NotFoundException("Muelle"), ct);
    }

    // ================================================================ (16) series

    /// <summary>
    /// (16) Series del producto del tenant por número, con UPDLOCK (JOIN a Product por TenantId; InventorySerial no tiene
    /// TenantId), tracked y en SerialId ascendente (paso 4 del orden de bloqueo). Las que no existen no aparecen.
    /// </summary>
    public static async Task<IReadOnlyList<InventorySerial>> LockSerialsAsync(this TeikemDbContext db, int productId, IReadOnlyCollection<string> serialNumbers, CancellationToken ct)
    {
        var numbers = (serialNumbers ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (numbers.Count == 0) return Array.Empty<InventorySerial>();
        if (!db.Database.IsRelational())
        {
            var rows = await (from s in db.InventorySerials.AsTracking()
                              join p in db.Products on s.ProductId equals p.ProductId
                              where s.ProductId == productId && numbers.Contains(s.SerialNumber)
                              select s).ToListAsync(ct);
            return rows.OrderBy(s => s.SerialId).ToList();
        }
        RequireTransaction(db, nameof(LockSerialsAsync));
        var tenantId = db.CurrentTenantId;
        var json = JsonSerializer.Serialize(numbers);
        var before = TrackedSnapshot<InventorySerial>(db);
        var locked = await db.InventorySerials
            .FromSqlInterpolated($"SELECT s.* FROM dbo.InventorySerial s WITH (UPDLOCK, ROWLOCK) JOIN dbo.Product p ON p.ProductId = s.ProductId WHERE p.TenantId = {tenantId} AND s.ProductId = {productId} AND s.SerialNumber IN (SELECT CAST(value AS NVARCHAR(80)) FROM OPENJSON({json}))")
            .AsTracking().ToListAsync(ct);
        foreach (var s in locked) await RefreshIfStaleAsync(db, before, s, ct);
        return locked.OrderBy(s => s.SerialId).ToList();
    }

    // ================================================================ (17) lotes

    /// <summary>
    /// (17) Lote del producto por número (D34): si existe con las mismas fechas se reutiliza (una fecha no capturada no se
    /// compara; una capturada debe coincidir con la guardada); con otras fechas → 409 'El lote {n} ya existe con otras fechas; corrija las fechas o use otro número de lote.'.
    /// Si no existe se crea. El chequeo toma UPDLOCK + HOLDLOCK sobre la clave (JOIN a Product por TenantId), así un alta
    /// concurrente del mismo lote espera; UQ_Lot es la última línea (409).
    /// </summary>
    public static async Task<int> EnsureLotAsync(this TeikemDbContext db, int productId, string lotNumber, DateOnly? manufactureDate,
        DateOnly? expiryDate, CancellationToken ct)
    {
        var number = (lotNumber ?? string.Empty).Trim();
        if (number.Length == 0) throw new ValidationException("lot", "Indique el número de lote.");
        if (number.Length > 60) throw new ValidationException("lot", "El número de lote admite como máximo 60 caracteres.");
        if (db.Database.IsRelational())
        {
            RequireTransaction(db, nameof(EnsureLotAsync));
            var tenantId = db.CurrentTenantId;
            _ = await db.Database.SqlQuery<int>($"SELECT l.LotId AS Value FROM dbo.InventoryLot l WITH (UPDLOCK, HOLDLOCK) JOIN dbo.Product p ON p.ProductId = l.ProductId WHERE p.TenantId = {tenantId} AND l.ProductId = {productId} AND l.LotNumber = {number}")
                .ToListAsync(ct);
        }
        var existing = db.ChangeTracker.Entries<InventoryLot>()
                           .Where(e => e.State != EntityState.Deleted && e.Entity.ProductId == productId && e.Entity.LotNumber == number)
                           .Select(e => e.Entity).FirstOrDefault()
                       ?? await (from l in db.InventoryLots.AsNoTracking()
                                 join p in db.Products.AsNoTracking() on l.ProductId equals p.ProductId
                                 where l.ProductId == productId && l.LotNumber == number
                                 select l).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            // Una fecha no capturada no se compara; una capturada debe coincidir con la guardada (D34, igual que ReceiptRules,
            // AdjustmentRules y CycleCountRules.LotDatesMatch).
            var sameMfg = manufactureDate is null || existing.ManufactureDate == manufactureDate;
            var sameExp = expiryDate is null || existing.ExpiryDate == expiryDate;
            if (!sameMfg || !sameExp) throw new ConflictException(LotExistsWithOtherDates(existing.LotNumber));
            return existing.LotId;
        }
        if (!await db.Products.AnyAsync(p => p.ProductId == productId, ct)) throw new NotFoundException("Producto");
        var lot = new InventoryLot { ProductId = productId, LotNumber = number, ManufactureDate = manufactureDate, ExpiryDate = expiryDate, IsActive = true };
        db.InventoryLots.Add(lot);
        await db.SaveGuardedAsync(LotCreatedConcurrently(number), ct);
        return lot.LotId;
    }

    // ================================================================ helpers

    internal static void RequireTransaction(TeikemDbContext db, string what)
    {
        if (db.Database.IsRelational() && db.Database.CurrentTransaction is null)
            throw new InvalidOperationException($"{what} requiere una transacción abierta (RunInTransactionAsync).");
    }

    private static async Task RequireLockedAsync(IQueryable<int> lockQuery, Func<TeikemException> notFound, CancellationToken ct)
    {
        // La consulta NO se compone: se ejecuta tal cual para que el hint de bloqueo aplique a la fila.
        var ids = await lockQuery.ToListAsync(ct);
        if (ids.Count == 0) throw notFound();
    }

    private static async Task<T> LoadTrackedAsync<T>(TeikemDbContext db, IQueryable<T> query, Func<TeikemException> notFound, CancellationToken ct)
        where T : class
    {
        var before = TrackedSnapshot<T>(db);
        var entity = await query.AsTracking().FirstOrDefaultAsync(ct) ?? throw notFound();
        await RefreshIfStaleAsync(db, before, entity, ct);
        return entity;
    }

    private static HashSet<object> TrackedSnapshot<T>(TeikemDbContext db) where T : class
        => new(db.ChangeTracker.Entries<T>().Select(e => (object)e.Entity), ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Una entidad que YA estaba en el tracker antes del bloqueo (y sin cambios) se recarga: EF devuelve la instancia rastreada
    /// sin refrescar sus valores, y esa lectura pudo ser anterior a otra transacción ya confirmada.
    /// </summary>
    private static async Task RefreshIfStaleAsync<T>(TeikemDbContext db, HashSet<object> trackedBefore, T entity, CancellationToken ct) where T : class
    {
        if (!db.Database.IsRelational() || !trackedBefore.Contains(entity)) return;
        var entry = db.Entry(entity);
        if (entry.State == EntityState.Unchanged) await entry.ReloadAsync(ct);
    }
}
