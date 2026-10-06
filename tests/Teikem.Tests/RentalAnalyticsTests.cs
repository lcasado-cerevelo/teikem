using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Analytics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Dsl;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 29 (Rentas R3) — fuentes de datos RENTAL, RENTAL_RETURN y RENTAL_PROCESS, el aviso "Necesita tu atención" de rentas
/// vencidas o por vencer y la regla de lectura por módulo, sobre InMemory (RentalReturnWorld: servicios, ledger y efectos REALES,
/// reloj fijo 2026-10-05 12:00 en Puerto Rico). Cubre: campos calculados con el día de la compañía (DaysToPickup, IsOverdue, por
/// vencer en 7 días; un reloj distinto cambia el resultado y "hoy" es el día local, no el UTC), los filtros sembrados, equipos y
/// extensiones; devoluciones con motivo, condición resumida y anticipada; la cola de procesos con días en proceso; el aislamiento
/// entre compañías en las tres fuentes y en el aviso; el aviso con su orden, tonos, parámetros y "Ver todos", sin rental.view o con
/// Rentas apagado no aporta; y AnalyticsService oculta fuentes, indicadores, gráficos y vistas de rentas con el módulo apagado.
/// </summary>
public class RentalAnalyticsTests
{
    private static readonly DateOnly Today = RentalReturnWorld.Today;

    /// <summary>Rentas del mundo: A (En renta, extendida), B (Programada, vencida), C (En renta, recoge hoy), D (Programada, +7),
    /// E (Programada, +8), F (Borrador vencido: no es abierta), G (Cancelada) y una renta, una devolución y un proceso de OTRA compañía.</summary>
    private sealed record Seeded(RentalReturnWorld W, RentalDto A, RentalDto B, RentalDto C, RentalDto D, RentalDto E, RentalDto F, RentalDto G);

    private static async Task<Seeded> SeedAsync(Action<IServiceCollection>? configure = null)
    {
        var w = await RentalReturnWorld.CreateAsync(configure);
        w.F.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.RentalEquipment, ModuleKeys.Analytics);
        await w.F.PostAsync(Receive(w, "S-4"), Receive(w, "S-5"), Receive(w, "S-6"));

        var a = await RentalAsync(w, Today, Today.AddDays(30), RentalStatuses.OnRent, "S-1");
        a = await w.Rentals(s => s.ExtendAsync(a.Rental.PublicId, new RentalExtendRequest(Today.AddDays(40), "Más tiempo"), default));
        var b = await RentalAsync(w, Today.AddDays(-20), Today.AddDays(-2), RentalStatuses.Scheduled, "S-2");
        var c = await RentalAsync(w, Today.AddDays(-10), Today, RentalStatuses.OnRent, "S-3");
        var d = await RentalAsync(w, Today, Today.AddDays(7), RentalStatuses.Scheduled, "S-4");
        var e = await RentalAsync(w, Today, Today.AddDays(8), RentalStatuses.Scheduled, "S-5");
        var f = await RentalAsync(w, Today.AddDays(-30), Today.AddDays(-5), RentalStatuses.Draft);
        var g = await RentalAsync(w, Today.AddDays(-30), Today.AddDays(-1), RentalStatuses.Cancelled, "S-6");

        // Otra compañía: una renta vencida, su devolución y un proceso abierto (escritos directamente).
        var db = w.F.Db;
        var other = new Rental
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.OtherTenantId, Number = "REN-90001", ClientId = 9001, LocationId = 9001, WarehouseId = 9001,
            StartDate = Today.AddDays(-9), PickupDate = Today.AddDays(-3), OriginalPickupDate = Today.AddDays(-3),
            StatusCodeId = w.F.StatusId(StatusDomains.RentalStatus, RentalStatuses.OnRent), CreatedAtUtc = DateTime.UtcNow,
        };
        db.Rentals.Add(other);
        await db.SaveChangesAsync();
        db.RentalReturns.Add(new RentalReturn
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.OtherTenantId, Number = "DRN-90001", RentalId = other.RentalId, ReturnedOn = Today,
            ReasonLookupId = w.F.LookupId(LookupDomains.RentalReturnReason, RentalReturnReasons.EarlyDamage), CreatedAtUtc = DateTime.UtcNow,
        });
        db.RentalProcesses.Add(new RentalProcess
        {
            TenantId = WmsFixture.OtherTenantId, SerialId = 9001, ProductId = 9001, WarehouseId = 9001, BinId = 9001,
            StatusCodeId = w.F.StatusId(StatusDomains.RentalProcessStatus, RentalProcessStatuses.Pending), StartedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new Seeded(w, a, b, c, d, e, f, g);
    }

    private static InventoryPosting Receive(RentalReturnWorld w, string serial)
        => new(InventoryTxnTypes.Receipt, w.P.ProductId, 1m, SerialNumber: serial, ToWarehouseId: w.W.WarehouseId, ToBinId: w.A1.WarehouseBinId);

    private static async Task<RentalDto> RentalAsync(RentalReturnWorld w, DateOnly start, DateOnly pickup, string state, params string[] serials)
    {
        var created = await w.Rentals(s => s.CreateAsync(new RentalCreateRequest(w.C.PublicId, w.L.PublicId, start, pickup, ContractNumber: "CT-" + pickup.DayNumber,
            Lines: serials.Length == 0 ? null : new[] { new RentalLinesAddRequest(w.P.PublicId, serials) }), default));
        if (state == RentalStatuses.Draft) return created;
        var scheduled = await w.Rentals(s => s.ScheduleAsync(created.Rental.PublicId, null, default));
        return state switch
        {
            RentalStatuses.Scheduled => scheduled,
            RentalStatuses.Cancelled => await w.Rentals(s => s.CancelAsync(created.Rental.PublicId, null, default)),
            _ => await w.Rentals(s => s.DispatchAsync(created.Rental.PublicId, null, default)),
        };
    }

    private static TenantClock ClockAt(DateTime utc) => new(LocalDay.DefaultZone, () => utc);

    private static async Task<Dictionary<string, DataRow>> RentalRowsAsync(RentalReturnWorld w, ITenantClock clock, DataQuery? q = null)
        => (await new RentalDataSource(w.F.Db, w.F.Lookups, w.F.Tenant, clock).LoadAsync(q ?? new DataQuery(), default))
            .ToDictionary(r => (string)r["Number"]!);

    private static IEnumerable<string> Matching(IEnumerable<DataRow> rows, string filter)
        => rows.Where(r => RuleEvaluator.Matches(r, filter)).Select(r => (string)r["Number"]!).OrderBy(n => n, StringComparer.Ordinal);

    // ================================================================ RENTAL

    [Fact]
    public async Task Rental_source_computes_due_and_overdue_with_the_company_day_and_isolates_tenants()
    {
        var s = await SeedAsync();
        await using var _ = s.W.F;
        var clock = s.W.F.Get<ITenantClock>();
        var rows = await RentalRowsAsync(s.W, clock);

        // Solo las 7 rentas de la compañía (la de otra compañía no sale).
        Assert.Equal(new[] { s.A, s.B, s.C, s.D, s.E, s.F, s.G }.Select(r => r.Rental.Number).OrderBy(n => n, StringComparer.Ordinal),
            rows.Keys.OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain("REN-90001", rows.Keys);

        var a = rows[s.A.Rental.Number];
        Assert.Equal((RentalStatuses.OnRent, true, 40, false), ((string)a["StatusCode"]!, (bool)a["IsOpen"]!, (int)a["DaysToPickup"]!, (bool)a["IsOverdue"]!));
        Assert.Equal((1, 10, 1, 1, 0), ((int)a["ExtensionCount"]!, (int)a["DaysExtended"]!, (int)a["Units"]!, (int)a["UnitsOnRent"]!, (int)a["UnitsReturned"]!));
        Assert.Equal((Today.AddDays(40), Today.AddDays(30), Today), ((DateOnly)a["PickupDate"]!, (DateOnly)a["OriginalPickupDate"]!, (DateOnly)a["StartDate"]!));
        Assert.Equal((s.W.C.Name, "Hospital A", "W1", s.W.C.ClientId, s.W.L.LocationId, s.W.W.WarehouseId),
            ((string)a["ClientName"]!, (string)a["LocationName"]!, (string)a["WarehouseCode"]!, (int)a["ClientId"]!, (int)a["LocationId"]!, (int)a["WarehouseId"]!));
        Assert.NotNull(a["DispatchedAtUtc"]);
        Assert.StartsWith("CT-", (string)a["ContractNumber"]!);

        // B Programada con el recogido hace 2 días: vencida (también una Programada, decisión 14). Reservada, aún sin despachar.
        var b = rows[s.B.Rental.Number];
        Assert.Equal((RentalStatuses.Scheduled, -2, true, 1, 0), ((string)b["StatusCode"]!, (int)b["DaysToPickup"]!, (bool)b["IsOverdue"]!, (int)b["Units"]!, (int)b["UnitsOnRent"]!));
        // F Borrador con el recogido vencido y G cancelada: no son abiertas, no están vencidas.
        Assert.Equal((false, false), ((bool)rows[s.F.Rental.Number]["IsOpen"]!, (bool)rows[s.F.Rental.Number]["IsOverdue"]!));
        Assert.Equal((false, false, 0), ((bool)rows[s.G.Rental.Number]["IsOpen"]!, (bool)rows[s.G.Rental.Number]["IsOverdue"]!, (int)rows[s.G.Rental.Number]["Units"]!));

        // Filtros sembrados: por vencer = C (hoy) y D (+7); E (+8) no; vencidas = B.
        Assert.Equal(new[] { s.C.Rental.Number, s.D.Rental.Number }.OrderBy(n => n, StringComparer.Ordinal), Matching(rows.Values, RentalAnalyticsRules.DueSoonFilter));
        Assert.Equal(new[] { s.B.Rental.Number }, Matching(rows.Values, RentalAnalyticsRules.OverdueFilter));
        Assert.Equal(new[] { s.A.Rental.Number, s.C.Rental.Number }.OrderBy(n => n, StringComparer.Ordinal), Matching(rows.Values, RentalAnalyticsRules.OnRentFilter));

        // Con el reloj ocho días después (2026-10-13): D pasa a vencida y E recoge hoy.
        var later = await RentalRowsAsync(s.W, ClockAt(new DateTime(2026, 10, 13, 16, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new[] { s.B.Rental.Number, s.C.Rental.Number, s.D.Rental.Number }.OrderBy(n => n, StringComparer.Ordinal), Matching(later.Values, RentalAnalyticsRules.OverdueFilter));
        Assert.Equal(0, (int)later[s.E.Rental.Number]["DaysToPickup"]!);
        Assert.Equal(new[] { s.E.Rental.Number }, Matching(later.Values, RentalAnalyticsRules.DueSoonFilter));

        // "Hoy" es el día LOCAL: 2026-10-06 02:00Z son las 22:00 del 5 en Puerto Rico; C (recoge el 5) todavía no está vencida.
        var night = await RentalRowsAsync(s.W, ClockAt(new DateTime(2026, 10, 6, 2, 0, 0, DateTimeKind.Utc)));
        Assert.Equal((0, false), ((int)night[s.C.Rental.Number]["DaysToPickup"]!, (bool)night[s.C.Rental.Number]["IsOverdue"]!));
        var nextDay = await RentalRowsAsync(s.W, ClockAt(new DateTime(2026, 10, 6, 4, 30, 0, DateTimeKind.Utc)));
        Assert.True((bool)nextDay[s.C.Rental.Number]["IsOverdue"]!);

        // Rango sobre StartDate (días locales: desde inclusivo, hasta exclusivo) e Ids.
        var ranged = await RentalRowsAsync(s.W, clock, new DataQuery { FromDay = Today, ToDayExclusive = Today.AddDays(1) });
        Assert.Equal(new[] { s.A.Rental.Number, s.D.Rental.Number, s.E.Rental.Number }.OrderBy(n => n, StringComparer.Ordinal), ranged.Keys.OrderBy(n => n, StringComparer.Ordinal));
        var byId = await RentalRowsAsync(s.W, clock, new DataQuery { Ids = new[] { s.B.Rental.Id } });
        Assert.Equal(s.B.Rental.Number, Assert.Single(byId.Keys));

        // Desde la otra compañía solo se ve la suya.
        using (s.W.F.AsTenant(WmsFixture.OtherTenantId))
        {
            var others = await RentalRowsAsync(s.W, clock);
            Assert.Equal("REN-90001", Assert.Single(others.Keys));
            Assert.True((bool)others["REN-90001"]["IsOverdue"]!);
        }
    }

    // ================================================================ RENTAL_RETURN y RENTAL_PROCESS

    [Fact]
    public async Task Return_and_process_sources_summarize_reason_condition_early_and_the_open_queue()
    {
        var w = await RentalReturnWorld.CreateAsync();
        await using var _ = w.F;
        var rental = await RentalAsync(w, Today.AddDays(-10), Today.AddDays(30), RentalStatuses.OnRent, "S-1", "S-2", "S-3");
        // 1) Anticipada por daño: S-3 dañado con proceso. 2) Fin del contrato: S-1 buena sin proceso y S-2 incompleta con proceso.
        var r1 = await w.Returns(s => s.CreateAsync(rental.Rental.PublicId, new RentalReturnCreateRequest(RentalReturnReasons.EarlyDamage,
            new[] { RentalReturnWorld.Line("S-3", RentalReturnConditions.Damaged) }, Today.AddDays(-1), Notes: "Golpe"), default));
        var r2 = await w.Returns(s => s.CreateAsync(rental.Rental.PublicId, new RentalReturnCreateRequest(RentalReturnReasons.EndOfContract,
            new[] { RentalReturnWorld.Line("S-1", RentalReturnConditions.Good, requiresProcess: false), RentalReturnWorld.Line("S-2", RentalReturnConditions.Incomplete) }), default));
        // Proceso de otra compañía (no debe verse).
        w.F.Db.RentalProcesses.Add(new RentalProcess
        {
            TenantId = WmsFixture.OtherTenantId, SerialId = 9001, ProductId = 9001, WarehouseId = 9001, BinId = 9001,
            StatusCodeId = w.F.StatusId(StatusDomains.RentalProcessStatus, RentalProcessStatuses.Pending), StartedAtUtc = DateTime.UtcNow,
        });
        await w.F.Db.SaveChangesAsync();
        w.F.Db.ChangeTracker.Clear();

        var returns = (await new RentalReturnDataSource(w.F.Db, w.F.Lookups, w.F.Tenant).LoadAsync(new DataQuery(), default)).ToDictionary(r => (string)r["Number"]!);
        Assert.Equal(2, returns.Count);
        var d1 = returns[r1.Return.Number];
        Assert.Equal((RentalReturnReasons.EarlyDamage, RentalReturnReasons.EarlyDamage, true, 31), ((string)d1["ReasonCode"]!, (string)d1["Reason"]!, (bool)d1["IsEarly"]!, (int)d1["DaysEarly"]!));
        Assert.Equal((RentalReturnConditions.Damaged, true, 1, 1, 1, 1), ((string)d1["ConditionCode"]!, (bool)d1["HasDamage"]!, (int)d1["Units"]!, (int)d1["DamagedUnits"]!,
            (int)d1["UnitsWithProcess"]!, (int)d1["OpenProcesses"]!));
        Assert.Equal((rental.Rental.Number, rental.Rental.Id, w.C.Name, "Hospital A", Today.AddDays(-1)),
            ((string)d1["RentalNumber"]!, (int)d1["RentalId"]!, (string)d1["ClientName"]!, (string)d1["LocationName"]!, (DateOnly)d1["ReturnedOn"]!));
        var d2 = returns[r2.Return.Number];
        Assert.Equal(("GOOD,INCOMPLETE", "GOOD, INCOMPLETE", false), ((string)d2["ConditionCode"]!, (string)d2["Condition"]!, (bool)d2["HasDamage"]!));
        Assert.Equal((2, 1, 0, 1, 1, 1, 30), ((int)d2["Units"]!, (int)d2["GoodUnits"]!, (int)d2["DamagedUnits"]!, (int)d2["IncompleteUnits"]!, (int)d2["UnitsWithProcess"]!,
            (int)d2["OpenProcesses"]!, (int)d2["DaysEarly"]!));
        // Rango por día de devolución (ayer solo la primera).
        var ranged = await new RentalReturnDataSource(w.F.Db, w.F.Lookups, w.F.Tenant).LoadAsync(new DataQuery { FromDay = Today.AddDays(-1), ToDayExclusive = Today }, default);
        Assert.Equal(r1.Return.Number, (string)Assert.Single(ranged)["Number"]!);

        // Cola de procesos: los dos abiertos (S-3 dañado, S-2 incompleto), sin el de la otra compañía.
        var process = new RentalProcessDataSource(w.F.Db, w.F.Lookups, w.F.Tenant, w.F.Get<ITenantClock>());
        var rows = (await process.LoadAsync(new DataQuery(), default)).ToDictionary(r => (string)r["SerialNumber"]!);
        Assert.Equal(new[] { "S-2", "S-3" }, rows.Keys.OrderBy(k => k, StringComparer.Ordinal));
        var p3 = rows["S-3"];
        Assert.Equal((RentalProcessStatuses.Pending, true, r1.Return.Number, rental.Rental.Number, w.C.Name, RentalReturnConditions.Damaged),
            ((string)p3["StatusCode"]!, (bool)p3["IsOpen"]!, (string)p3["ReturnNumber"]!, (string)p3["RentalNumber"]!, (string)p3["ClientName"]!, (string)p3["ConditionCode"]!));
        Assert.Equal(("EQ-1", "W1", "A-02"), ((string)p3["Sku"]!, (string)p3["WarehouseCode"]!, (string)p3["BinCode"]!));
        Assert.Equal(new[] { "S-2", "S-3" }, (await process.LoadAsync(new DataQuery(), default)).Where(r => RuleEvaluator.Matches(r, RentalAnalyticsRules.OpenProcessFilter))
            .Select(r => (string)r["SerialNumber"]!).OrderBy(k => k, StringComparer.Ordinal));

        // Terminar S-3: deja de estar abierto (Lista). Días en proceso: tres días después del inicio, con el reloj de la compañía.
        var p3Id = (int)p3["Id"]!;
        await w.Processes(s => s.CompleteAsync(p3Id, null, default));
        var later = new RentalProcessDataSource(w.F.Db, w.F.Lookups, w.F.Tenant, ClockAt(DateTime.UtcNow.AddDays(3)));
        var after = (await later.LoadAsync(new DataQuery(), default)).ToDictionary(r => (string)r["SerialNumber"]!);
        Assert.Equal((RentalProcessStatuses.Ready, false, 0), ((string)after["S-3"]["StatusCode"]!, (bool)after["S-3"]["IsOpen"]!, (int)after["S-3"]["DaysInProcess"]!));
        Assert.Equal((true, 3), ((bool)after["S-2"]["IsOpen"]!, (int)after["S-2"]["DaysInProcess"]!));

        using (w.F.AsTenant(WmsFixture.OtherTenantId))
        {
            Assert.Single(await process.LoadAsync(new DataQuery(), default));
            Assert.Empty(await new RentalReturnDataSource(w.F.Db, w.F.Lookups, w.F.Tenant).LoadAsync(new DataQuery(), default));
        }
    }

    [Fact]
    public void Rental_sources_declare_keys_date_fields_money_relations_and_the_rentals_module()
    {
        var tenant = new TenantContext { TenantId = 1, UserId = 1 };
        var db = new Teikem.Infrastructure.Persistence.TeikemDbContext(new DbContextOptionsBuilder<Teikem.Infrastructure.Persistence.TeikemDbContext>()
            .UseInMemoryDatabase("rental-sources-" + Guid.NewGuid()).Options, tenant);
        var lookups = new TripTestLookups();
        IDataSource[] sources = { new RentalDataSource(db, lookups, tenant), new RentalReturnDataSource(db, lookups, tenant), new RentalProcessDataSource(db, lookups, tenant) };
        var expected = new (string Key, string DateField, string[] Money, string[] Relations)[]
        {
            (EntityTypes.Rental, "StartDate", new[] { "EstimatedDeliveryCost" }, new[] { "Client", "Location", "Warehouse" }),
            (EntityTypes.RentalReturn, "ReturnedOn", new[] { "EstimatedPickupCost" }, new[] { "Rental", "Client" }),
            (EntityTypes.RentalProcess, "StartedAtUtc", Array.Empty<string>(), new[] { "Product", "Warehouse", "Return", "Rental" }),
        };
        foreach (var (source, (key, dateField, money, relations)) in sources.Zip(expected))
        {
            Assert.Equal(key, source.Key);
            Assert.Equal(key, source.EntityTypeCode);
            Assert.Equal(dateField, source.DateField);
            Assert.Equal("Id", source.IdField);
            Assert.Equal(BusinessModules.Warehouse, source.DefaultBusinessModule);
            Assert.Equal(ModuleKeys.RentalEquipment, source.TenantModule);
            Assert.Contains(source.Fields, f => f.Key == dateField && f.Type == DataFieldType.Date);
            Assert.Equal(money, source.Fields.Where(f => f.IsMoney).Select(f => f.Key));
            Assert.Equal(relations, source.Relations.Select(r => r.Key));
            Assert.All(source.Relations, r => Assert.Contains(source.Fields, f => f.Key == r.LocalField));
            Assert.Equal(source.Fields.Count, source.Fields.Select(f => f.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            // Lectura: rental.view (permiso de dueño de la entidad).
            Assert.Equal(PermissionCatalog.RentalView, PermissionCatalog.DataSourceReadPermission(source.EntityTypeCode));
        }
        var rental = sources[0].Fields.ToDictionary(f => f.Key, f => f.Type);
        Assert.Equal((DataFieldType.Number, DataFieldType.Bool, DataFieldType.Bool, DataFieldType.Date, DataFieldType.Number),
            (rental["DaysToPickup"], rental["IsOverdue"], rental["IsOpen"], rental["PickupDate"], rental["Units"]));
        // Las fuentes existentes no declaran módulo propio (siguen con la regla del módulo de negocio).
        Assert.Null(((IDataSource)new CycleCountDataSource(db, tenant)).TenantModule);
    }

    [Fact]
    public void Pure_rules_for_early_returns_days_in_process_and_condition_summary()
    {
        Assert.Equal((3, 0, 0), (RentalAnalyticsRules.DaysEarly(Today, Today.AddDays(3)), RentalAnalyticsRules.DaysEarly(Today, Today), RentalAnalyticsRules.DaysEarly(Today.AddDays(2), Today)));
        Assert.Equal((2, 4, 0), (RentalAnalyticsRules.DaysInProcess(Today.AddDays(-4), Today.AddDays(-2), Today), RentalAnalyticsRules.DaysInProcess(Today.AddDays(-4), null, Today),
            RentalAnalyticsRules.DaysInProcess(Today, null, Today.AddDays(-1))));
        Assert.Equal(new[] { "GOOD", "DAMAGED", "INCOMPLETE", "ZZZ" },
            RentalAnalyticsRules.DistinctConditions(new[] { "ZZZ", "INCOMPLETE", null, "GOOD", "DAMAGED", "GOOD", " " }));
        Assert.Equal(7, RentalAnalyticsRules.DueSoonDays);
        // Por vencer: abierta con el recogido entre hoy y hoy + 7; vencida: abierta con el recogido antes de hoy. Programada cuenta (decisión 14).
        Assert.True(RentalAnalyticsRules.NeedsAttention(RentalStatuses.Scheduled, Today.AddDays(-30), Today));
        Assert.False(RentalAnalyticsRules.NeedsAttention(RentalStatuses.Returned, Today, Today));
        Assert.False(RentalAnalyticsRules.NeedsAttention(RentalStatuses.Cancelled, Today.AddDays(-3), Today));
    }

    // ================================================================ "Necesita tu atención"

    private static void AttentionServices(IServiceCollection s)
    {
        s.AddSingleton<IAttentionItemProvider, RentalDueAttentionProvider>();
        s.AddSingleton<AttentionFeedService>();
    }

    [Fact]
    public async Task Attention_lists_overdue_and_due_within_seven_days_oldest_pickup_first()
    {
        var s = await SeedAsync(AttentionServices);
        await using var _ = s.W.F;
        var dto = await s.W.F.Get<AttentionFeedService>().GetAsync(default);

        // B (vencida hace 2 días), C (recoge hoy) y D (en 7 días); no E (+8), F (Borrador), G (Cancelada), A (+40) ni la de otra compañía.
        Assert.Equal(3, dto.Total);
        Assert.Equal(new[] { s.B.Rental.Number, s.C.Rental.Number, s.D.Rental.Number }, dto.Items.Select(i => i.Params["number"]));
        Assert.Equal(new[] { "danger", "warn", "warn" }, dto.Items.Select(i => i.Tone));
        Assert.All(dto.Items, i => Assert.Equal((RentalDueAttentionProvider.ItemCode, BusinessModules.Warehouse, 1, "/warehouse/rentals"), (i.Code, i.Module, i.Count, i.Route)));

        var first = dto.Items[0];
        Assert.Equal(new Dictionary<string, string> { ["rental"] = s.B.Rental.PublicId.ToString() }, first.Query);
        Assert.Equal((s.B.Rental.PublicId.ToString(), s.W.C.Name, "Hospital A", "W1", "2026-10-03", "-2", "true", "1", RentalStatuses.Scheduled),
            (first.Params["publicId"], first.Params["client"], first.Params["location"], first.Params["warehouse"], first.Params["pickupDate"],
             first.Params["daysToPickup"], first.Params["overdue"], first.Params["units"], first.Params["status"]));
        // Desde = 00:00 locales (Puerto Rico, UTC−4) del día de recogido.
        Assert.Equal(new DateTime(2026, 10, 3, 4, 0, 0, DateTimeKind.Utc), first.SinceUtc);
        Assert.Equal(("0", "false"), (dto.Items[1].Params["daysToPickup"], dto.Items[1].Params["overdue"]));
        Assert.Equal("7", dto.Items[2].Params["daysToPickup"]);

        var group = Assert.Single(dto.Groups);
        Assert.Equal((RentalDueAttentionProvider.ItemCode, 3, "/warehouse/rentals"), (group.Code, group.Total, group.Route));
        Assert.Equal(new Dictionary<string, string> { ["dueWithinDays"] = "7", ["overdue"] = "true" }, group.Query);

        // La otra compañía ve solo la suya (vencida hace 3 días).
        using (s.W.F.AsTenant(WmsFixture.OtherTenantId))
        {
            s.W.F.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.RentalEquipment);
            var other = await s.W.F.Get<AttentionFeedService>().GetAsync(default);
            Assert.Equal("REN-90001", Assert.Single(other.Items).Params["number"]);
        }
    }

    [Fact]
    public async Task Attention_needs_rental_view_and_the_rentals_module()
    {
        var s = await SeedAsync(AttentionServices);
        await using var _ = s.W.F;
        Task<AttentionDto> Get() => s.W.F.Get<AttentionFeedService>().GetAsync(default);

        s.W.F.SetPermissions(PermissionCatalog.PulseAttention, PermissionCatalog.InventoryView);
        Assert.Equal((0, 0, 0), ((await Get()).Total, (await Get()).Items.Count, (await Get()).Groups.Count));
        s.W.F.SetPermissions(PermissionCatalog.PulseAttention, PermissionCatalog.RentalView);
        Assert.Equal(3, (await Get()).Total);
        s.W.F.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.Analytics);   // Rentas apagado
        Assert.Equal(0, (await Get()).Total);

        // Sin nada vencido ni por vencer: el proveedor no aporta grupo.
        s.W.F.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.RentalEquipment);
        var provider = new RentalDueAttentionProvider(s.W.F.Db, ClockAt(new DateTime(2026, 9, 1, 16, 0, 0, DateTimeKind.Utc)));
        var none = await provider.ReadAsync(new AttentionScope(WmsFixture.TenantId, 1, "es", new HashSet<string>()), 5, default);
        // El 2026-09-01, B (recoge el 3 de octubre) aún no entra en la ventana de 7 días; ninguna otra tampoco.
        Assert.Equal((0, 0), (none.Total, none.Items.Count));
        Assert.Null(none.Group);
        Assert.True(RentalAnalyticsRules.NeedsAttention(RentalStatuses.OnRent, Today.AddDays(7), Today));
        Assert.False(RentalAnalyticsRules.NeedsAttention(RentalStatuses.OnRent, Today.AddDays(8), Today));
        Assert.False(RentalAnalyticsRules.NeedsAttention(RentalStatuses.Draft, Today.AddDays(-1), Today));
    }

    // ================================================================ regla de lectura por módulo (AnalyticsService)

    private static void AnalyticsServices(IServiceCollection s)
    {
        s.AddSingleton<IDataSource, RentalDataSource>();
        s.AddSingleton<IDataSource, RentalReturnDataSource>();
        s.AddSingleton<IDataSource, RentalProcessDataSource>();
        s.AddSingleton<IDataSourceRegistry, DataSourceRegistry>();
        s.AddSingleton<AnalyticsEngine>();
        s.AddTransient<AnalyticsService>();
    }

    [Fact]
    public async Task With_the_rentals_module_off_sources_indicators_charts_and_views_are_hidden()
    {
        var s = await SeedAsync(AnalyticsServices);
        await using var _ = s.W.F;
        var f = s.W.F;
        // Una devolución de hoy para el gráfico por motivo (C, recoge hoy: no es anticipada).
        await s.W.Returns(r => r.CreateAsync(s.C.Rental.PublicId, new RentalReturnCreateRequest(RentalReturnReasons.EndOfContract,
            new[] { RentalReturnWorld.Line("S-3", requiresProcess: false) }), default));

        var id = 9000;
        LookupCode L(string entity, string code) => new() { LookupCodeId = id++, Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true };
        f.Db.LookupCodes.AddRange(L(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), L(LookupDomains.AggregateFn, AggregateFns.Count),
            L(LookupDomains.AggregateFn, AggregateFns.Sum), L(LookupDomains.BusinessModule, BusinessModules.Warehouse),
            L(LookupDomains.DateRangeMode, DateRangeModes.All), L(LookupDomains.DateRangeMode, DateRangeModes.Last30), L(LookupDomains.ReportChartType, ChartTypes.Donut));
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        int Id(string domain, string code) => f.Lookups.GetIdAsync(domain, code).Result;
        var due = new IndicatorDefinition
        {
            TenantId = WmsFixture.TenantId, Name = RentalAnalyticsRules.DueSoonIndicatorName, DataSourceKey = EntityTypes.Rental, FilterJson = RentalAnalyticsRules.DueSoonFilter,
            IsSystem = true, AggregateFnLookupId = Id(LookupDomains.AggregateFn, AggregateFns.Count), BusinessModuleLookupId = Id(LookupDomains.BusinessModule, BusinessModules.Warehouse),
            VisibilityLookupId = Id(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), DateRangeModeLookupId = Id(LookupDomains.DateRangeMode, DateRangeModes.All),
        };
        var overdue = new IndicatorDefinition
        {
            TenantId = WmsFixture.TenantId, Name = RentalAnalyticsRules.OverdueIndicatorName, DataSourceKey = EntityTypes.Rental, FilterJson = RentalAnalyticsRules.OverdueFilter,
            IsSystem = true, AggregateFnLookupId = Id(LookupDomains.AggregateFn, AggregateFns.Count), BusinessModuleLookupId = Id(LookupDomains.BusinessModule, BusinessModules.Warehouse),
            VisibilityLookupId = Id(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), DateRangeModeLookupId = Id(LookupDomains.DateRangeMode, DateRangeModes.All),
        };
        var chart = new ChartDefinition
        {
            TenantId = WmsFixture.TenantId, Name = RentalAnalyticsRules.ReturnsByReasonChartName, DataSourceKey = EntityTypes.RentalReturn, GroupByField = "Reason",
            IsSystem = true, ChartTypeLookupId = Id(LookupDomains.ReportChartType, ChartTypes.Donut), AggregateFnLookupId = Id(LookupDomains.AggregateFn, AggregateFns.Count),
            BusinessModuleLookupId = Id(LookupDomains.BusinessModule, BusinessModules.Warehouse), VisibilityLookupId = Id(LookupDomains.ReportVisibility, ReportVisibilities.Tenant),
            DateRangeModeLookupId = Id(LookupDomains.DateRangeMode, DateRangeModes.Last30),
        };
        var byClient = new ReportDefinition
        {
            TenantId = WmsFixture.TenantId, BaseEntityTypeLookupId = f.LookupId(LookupDomains.EntityType, EntityTypes.Rental), Name = RentalAnalyticsRules.OnRentByClientReportName,
            VisibilityLookupId = Id(LookupDomains.ReportVisibility, ReportVisibilities.Tenant), IsSystem = true, ColumnsJson = "[]",
            FilterJson = RentalAnalyticsRules.OnRentFilter, GroupJson = RentalAnalyticsRules.OnRentByClientGroup,
        };
        f.Db.AddRange(due, overdue, chart, byClient);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        AnalyticsService Svc() => f.Get<AnalyticsService>();
        var rentalKeys = new[] { EntityTypes.Rental, EntityTypes.RentalProcess, EntityTypes.RentalReturn };

        // Encendido (y rental.view, el usuario es admin de plataforma): todo se lista y se lee.
        Assert.Equal(rentalKeys, (await Svc().GetDataSourcesAsync(default)).Select(d => d.Key).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(1m, (await Svc().EvaluateIndicatorAsync(due.IndicatorDefinitionId, default)).Value);       // D (+7); C ya devuelta
        Assert.Equal(1m, (await Svc().EvaluateIndicatorAsync(overdue.IndicatorDefinitionId, default)).Value);   // B
        var points = (await Svc().EvaluateChartAsync(chart.ChartDefinitionId, default)).Points;
        Assert.Equal((RentalReturnReasons.EndOfContract, 1m), (Assert.Single(points).Label, points[0].Value));
        var run = await Svc().RunReportAsync(byClient.ReportDefinitionId, new ReportRunRequest(null, null, null, null, null), default);
        Assert.Equal(1, run.Total);   // un solo cliente con rentas En renta (A; C ya se devolvió)
        Assert.Equal(s.W.C.Name, Assert.Single(run.Rows)["ClientName"]);
        Assert.Equal(2, (await Svc().GetIndicatorsAsync(default)).Count);

        // Apagado: ni las fuentes ni lo que las usa (404 al leer, como una fuente sin permiso).
        f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.Analytics);
        Assert.DoesNotContain(await Svc().GetDataSourcesAsync(default), d => rentalKeys.Contains(d.Key));
        Assert.Empty(await Svc().GetIndicatorsAsync(default));
        Assert.Empty(await Svc().GetChartsAsync(default));
        Assert.Empty(await Svc().GetReportsAsync(null, default));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().EvaluateIndicatorAsync(due.IndicatorDefinitionId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().EvaluateChartAsync(chart.ChartDefinitionId, default));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc().RunReportAsync(byClient.ReportDefinitionId, new ReportRunRequest(null, null, null, null, null), default));

        // Encendido pero sin rental.view: tampoco.
        f.SetModules(ModuleKeys.WmsLotSerial, ModuleKeys.RentalEquipment, ModuleKeys.Analytics);
        f.SetPermissions(PermissionCatalog.AnalyticsView, PermissionCatalog.InventoryView);
        Assert.Empty(await Svc().GetIndicatorsAsync(default));
        f.SetPermissions(PermissionCatalog.AnalyticsView, PermissionCatalog.RentalView);
        Assert.Equal(2, (await Svc().GetIndicatorsAsync(default)).Count);
    }
}
