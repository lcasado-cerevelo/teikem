using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;

namespace Teikem.Tests;

/// <summary>
/// Lote 28 (Rentas R2) — mundo compartido de las pruebas de devolución y de proceso: WmsFixture (InMemory) con el InventoryLedger,
/// StatusService, RentalStatusEffect y RentalProcessStatusEffect REALES; almacén W1 con PCK (A-01, A-02) y QUA (Q-01), el equipo
/// EQ-1 por serie (S-1 y S-2 en A-01, S-3 en A-02), un cliente con su localidad y un reloj fijo (2026-10-05).
/// </summary>
internal sealed record RentalReturnWorld(WmsFixture F, Warehouse W, WarehouseBin A1, WarehouseBin A2, WarehouseBin Q1, Product P, Client C, Location L)
{
    public static readonly DateOnly Today = new(2026, 10, 5);

    /// <param name="configure">Lote 29 (Rentas R3): servicios adicionales (fuentes de datos, Análisis, "Necesita tu atención").</param>
    public static async Task<RentalReturnWorld> CreateAsync(Action<IServiceCollection>? configure = null)
    {
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(new TenantClock(LocalDay.DefaultZone, () => new DateTime(2026, 10, 5, 16, 0, 0, DateTimeKind.Utc)));
            s.AddSingleton<RentalBinResolver>();
            s.AddSingleton<RentalService>();
            s.AddSingleton<RentalReturnService>();
            s.AddSingleton<RentalProcessService>();
            s.AddSingleton<IStatusTransitionEffect, RentalStatusEffect>();
            s.AddSingleton<IStatusTransitionEffect, RentalProcessStatusEffect>();
            configure?.Invoke(s);
        });
        var w = await f.AddWarehouseAsync("W1");
        var pick = await f.AddZoneAsync(w, "PCK", ZoneTypes.Picking);
        var qua = await f.AddZoneAsync(w, "QUA", ZoneTypes.Quarantine);
        var a1 = await f.AddBinAsync(pick, "A-01");
        var a2 = await f.AddBinAsync(pick, "A-02");
        var q1 = await f.AddBinAsync(qua, "Q-01");
        var c = await f.AddClientAsync("CLI");
        var p = await f.AddProductAsync("EQ-1", TrackingTypes.Serial, purchaseCost: 500m);
        await f.PostAsync(Receive(p, w, a1, "S-1"), Receive(p, w, a1, "S-2"), Receive(p, w, a2, "S-3"));
        var l = new Location
        {
            PublicId = Guid.NewGuid(), TenantId = c.TenantId, ClientId = c.ClientId, Name = "Hospital A", Line1 = "Calle 1", City = "Ponce",
            LocationTypeLookupId = f.LookupId(LookupDomains.LocationType, LocationTypes.Delivery), CountryLookupId = f.LookupId(LookupDomains.Country, "PR"),
            IsActive = true,
        };
        f.Db.Locations.Add(l);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new RentalReturnWorld(f, w, a1, a2, q1, p, c, l);
    }

    private static InventoryPosting Receive(Product p, Warehouse w, WarehouseBin b, string serial)
        => new(InventoryTxnTypes.Receipt, p.ProductId, 1m, SerialNumber: serial, ToWarehouseId: w.WarehouseId, ToBinId: b.WarehouseBinId);

    public async Task<T> Run<TService, T>(Func<TService, Task<T>> action) where TService : notnull
    {
        try { return await action(F.Get<TService>()); }
        finally { F.Db.ChangeTracker.Clear(); }
    }

    public Task<T> Rentals<T>(Func<RentalService, Task<T>> action) => Run(action);
    public Task<T> Returns<T>(Func<RentalReturnService, Task<T>> action) => Run(action);
    public Task<T> Processes<T>(Func<RentalProcessService, Task<T>> action) => Run(action);

    /// <summary>Renta creada, programada y despachada con esas series (quedan ON_RENT en EN-RENTA).</summary>
    public async Task<RentalDto> DispatchedAsync(params string[] serials)
    {
        var created = await Rentals(s => s.CreateAsync(new RentalCreateRequest(C.PublicId, L.PublicId, Today, Today.AddDays(30),
            Lines: new[] { new RentalLinesAddRequest(P.PublicId, serials) }), default));
        await Rentals(s => s.ScheduleAsync(created.Rental.PublicId, null, default));
        return await Rentals(s => s.DispatchAsync(created.Rental.PublicId, null, default));
    }

    public async Task<WarehouseBin> RentBinAsync()
        => await F.Db.WarehouseBins.AsNoTracking().SingleAsync(b => b.WarehouseId == W.WarehouseId && b.Code == RentalRules.RentalBinCode);

    public async Task<(decimal OnHand, decimal Reserved)> BalanceAsync(int binId)
    {
        var b = await F.BalanceAsync(P.ProductId, binId);
        return (b?.QtyOnHand ?? 0m, b?.QtyReserved ?? 0m);
    }

    /// <summary>En mano y disponible del producto en todas las posiciones.</summary>
    public async Task<(decimal OnHand, decimal Available)> TotalsAsync()
    {
        var all = await F.Db.StockBalances.AsNoTracking().Where(b => b.ProductId == P.ProductId).ToListAsync();
        return (all.Sum(b => b.QtyOnHand), all.Sum(b => b.QtyOnHand - b.QtyReserved));
    }

    public async Task<(string Status, int? BinId)> SerialAsync(string serial)
    {
        var s = await F.SerialAsync(P.ProductId, serial);
        return (F.StatusCodeOf(s.StatusCodeId!.Value), s.CurrentBinId);
    }

    public static RentalReturnLineInput Line(string serial, string? condition = null, int? toBinId = null, bool? requiresProcess = null, string? notes = null)
        => new(serial, condition, toBinId, requiresProcess, notes);
}
