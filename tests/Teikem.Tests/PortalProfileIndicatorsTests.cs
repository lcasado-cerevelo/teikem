using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Identity;
using Teikem.Domain.Orders;
using Teikem.Domain.Common;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 120 (P10) — mi perfil (nombre, correo y rol; los datos de mi empresa en solo lectura; solo cambio mi nombre) y los indicadores del cliente (órdenes del período, entregas a tiempo, horas promedio, intentos fallidos, lo que hay
/// en curso, facturas y COD si la compañía tiene esos módulos). Todo del cliente de la sesión: nada de otros clientes.
/// </summary>
public class PortalProfileIndicatorsTests
{
    private sealed class World(WmsFixture f, Client c1, Client c2)
    {
        public WmsFixture F { get; } = f;
        public Client C1 { get; } = c1;
        public Client C2 { get; } = c2;
        public DateTime Now { get; } = new(2026, 10, 18, 16, 0, 0, DateTimeKind.Utc);
        public ITenantClock Clock => new TenantClock(TimeZoneInfo.Utc, () => Now);
    }

    private static async Task<World> NewWorldAsync(params string[] modules)
    {
        var f = await WmsFixture.CreateAsync();
        f.SetModules(modules);
        var c1 = await f.AddClientAsync("C1");
        var c2 = await f.AddClientAsync("C2");
        var tracked = await f.Db.Set<Client>().AsTracking().SingleAsync(c => c.ClientId == c1.ClientId);
        (tracked.LegalName, tracked.TaxId) = ("Cliente Uno LLC", "66-1234567");
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return new World(f, c1, c2);
    }

    private static void AsPortal(World x, Client c, int userId)
    {
        x.F.Tenant.IsPortal = true;
        x.F.Tenant.PortalClientId = c.ClientId;
        x.F.Tenant.ScopedClientIds = new[] { c.ClientId };
        x.F.Tenant.UserId = userId;
    }

    private static PortalProfileService Profile(World x) => new(x.F.Db, x.F.Tenant, x.F.Lookups);
    private static PortalIndicatorsService Indicators(World x) => new(x.F.Db, x.F.Tenant, x.F.Lookups, x.F.Get<ModuleService>(), x.Clock);

    // ------------------------------------------------------------ perfil

    [Fact]
    public async Task The_profile_shows_who_I_am_and_my_company_read_only_and_I_can_only_change_my_name()
    {
        var x = await NewWorldAsync(ModuleKeys.ClientPortal);
        await using var _ = x.F;
        x.F.Db.Users.Add(new ApplicationUser { Id = 41, UserName = "pat", Email = "pat@cliente.co", FullName = "Pat" });
        var active = x.F.StatusId(StatusDomains.PortalUserStatus, PortalUserStatuses.Active);
        x.F.Db.Set<PortalUser>().Add(new PortalUser { TenantId = WmsFixture.TenantId, ClientId = x.C1.ClientId, UserId = 41, Email = "pat@cliente.co", FullName = "Pat", StatusCodeId = active, IsActive = true });
        await x.F.Db.SaveChangesAsync();
        AsPortal(x, x.C1, 41);

        var me = await Profile(x).GetAsync(default);
        Assert.Equal(("Pat", "pat@cliente.co"), (me.FullName, me.Email));
        Assert.Equal(("C1", "Cliente Uno LLC", "66-1234567"), (me.Company.Code, me.Company.LegalName, me.Company.TaxId));

        var saved = await Profile(x).SaveAsync(new PortalProfileRequest("  Patricia Rivera "), default);
        Assert.Equal("Patricia Rivera", saved.FullName);
        Assert.Equal("Patricia Rivera", (await x.F.Db.Users.AsNoTracking().SingleAsync(u => u.Id == 41)).FullName);          // la cuenta y la persona del portal
        Assert.Equal("Patricia Rivera", (await x.F.Db.Set<PortalUser>().AsNoTracking().SingleAsync()).FullName);
        Assert.Equal("pat@cliente.co", (await Profile(x).GetAsync(default)).Email);                                             // el correo no cambia
        Assert.Equal("Cliente Uno LLC", (await x.F.Db.Set<Client>().AsNoTracking().SingleAsync(c => c.ClientId == x.C1.ClientId)).LegalName);   // la empresa no se toca
    }

    [Fact]
    public async Task The_name_is_required_and_limited_and_only_active_people_of_the_client_have_a_profile()
    {
        var x = await NewWorldAsync(ModuleKeys.ClientPortal);
        await using var _ = x.F;
        x.F.Db.Users.Add(new ApplicationUser { Id = 41, UserName = "pat", Email = "pat@cliente.co", FullName = "Pat" });
        x.F.Db.Set<PortalUser>().Add(new PortalUser { TenantId = WmsFixture.TenantId, ClientId = x.C1.ClientId, UserId = 41, Email = "pat@cliente.co", StatusCodeId = x.F.StatusId(StatusDomains.PortalUserStatus, PortalUserStatuses.Active), IsActive = true });
        await x.F.Db.SaveChangesAsync();
        AsPortal(x, x.C1, 41);
        Assert.Equal(PortalProfileService.NameRequired, (await Assert.ThrowsAsync<ValidationException>(() => Profile(x).SaveAsync(new PortalProfileRequest("  "), default))).Message);
        Assert.Equal(PortalProfileService.NameRequired, (await Assert.ThrowsAsync<ValidationException>(() => Profile(x).SaveAsync(null, default))).Message);
        Assert.Equal(PortalProfileService.NameTooLong, (await Assert.ThrowsAsync<ValidationException>(() => Profile(x).SaveAsync(new PortalProfileRequest(new string('x', 151)), default))).Message);
        // una persona de otro cliente (o ninguna) no tiene perfil aquí
        AsPortal(x, x.C2, 41);
        await Assert.ThrowsAsync<ForbiddenException>(() => Profile(x).GetAsync(default));
        x.F.Tenant.PortalClientId = null;
        await Assert.ThrowsAsync<ForbiddenException>(() => Profile(x).GetAsync(default));
    }

    // ------------------------------------------------------------ indicadores

    private static async Task<TransportOrder> OrderAsync(World x, Client c, string number, string status, DateTime created, DateTime? promised = null)
    {
        var o = await x.F.AddOrderAsync(c.ClientId, number, status);
        var row = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == o.TransportOrderId);
        row.CreatedAtUtc = created;
        row.PromisedDate = promised;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return row;
    }

    private static async Task DeliveredAtAsync(World x, TransportOrder o, DateTime at)
    {
        x.F.Db.EntityStatusHistories.Add(new EntityStatusHistory
        {
            TenantId = WmsFixture.TenantId, EntityTypeLookupId = x.F.LookupId(LookupDomains.EntityType, EntityTypes.TransportOrder), EntityId = o.TransportOrderId,
            ToStatusCodeId = x.F.StatusId(StatusDomains.OrderStatus, OrderStatuses.Delivered), ChangedAtUtc = at,
        });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Indicators_count_the_period_on_time_deliveries_hours_failed_attempts_and_what_is_open_now_only_for_my_client()
    {
        var x = await NewWorldAsync(ModuleKeys.ClientPortal);
        await using var _ = x.F;
        var now = x.Now;
        // C1: entregada a tiempo (prometida el 18, entregada el 17), entregada tarde (prometida el 10, entregada el 12), entregada sin fecha prometida, una en tránsito, una Sometida y una vieja fuera del período
        var onTime = await OrderAsync(x, x.C1, "O-1", OrderStatuses.Delivered, now.AddDays(-5), promised: new DateTime(2026, 10, 18));
        var late = await OrderAsync(x, x.C1, "O-2", OrderStatuses.Delivered, now.AddDays(-9), promised: new DateTime(2026, 10, 10));
        var noPromise = await OrderAsync(x, x.C1, "O-3", OrderStatuses.Delivered, now.AddDays(-2));
        await OrderAsync(x, x.C1, "O-4", OrderStatuses.InTransit, now.AddDays(-1));
        await OrderAsync(x, x.C1, "O-5", OrderStatuses.Submitted, now.AddHours(-3));
        var old = await OrderAsync(x, x.C1, "O-6", OrderStatuses.Delivered, now.AddDays(-60), promised: new DateTime(2026, 8, 20));
        await OrderAsync(x, x.C2, "X-1", OrderStatuses.InTransit, now.AddDays(-1));                                  // de otro cliente: no cuenta
        await DeliveredAtAsync(x, onTime, new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc));
        await DeliveredAtAsync(x, late, new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc));
        await DeliveredAtAsync(x, noPromise, new DateTime(2026, 10, 17, 18, 0, 0, DateTimeKind.Utc));
        await DeliveredAtAsync(x, old, new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc));
        // un intento fallido de C1 en el período, uno viejo y uno de otro cliente
        var attempts = x.F.Db.Set<DeliveryAttempt>();
        attempts.Add(new DeliveryAttempt { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, TransportOrderId = noPromise.TransportOrderId, AttemptNumber = 1, AttemptedAtUtc = now.AddDays(-3), Delivered = false, IsActive = true });
        attempts.Add(new DeliveryAttempt { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, TransportOrderId = old.TransportOrderId, AttemptNumber = 1, AttemptedAtUtc = now.AddDays(-58), Delivered = false, IsActive = true });
        await x.F.Db.SaveChangesAsync();
        AsPortal(x, x.C1, 41);

        var i = await Indicators(x).GetAsync(30, default);

        Assert.Equal(30, i.Days);
        Assert.Equal(5, i.Created);                                                    // O-1…O-5 (la vieja no)
        Assert.Equal(3, i.Delivered);                                                  // las tres entregadas dentro del período
        Assert.Equal((2, 1, 50.0), (i.DeliveredWithPromise, i.OnTime, i.OnTimePct));   // solo las que tenían fecha: una a tiempo y una tarde
        Assert.NotNull(i.AvgDeliveryHours);
        Assert.Equal((2, 1), (i.OpenNow, i.InTransitNow));                             // en tránsito y Sometida
        Assert.Equal(1, i.FailedAttempts);
        Assert.Null(i.InvoiceBalance);                                                 // sin los módulos de facturación y COD no hay esos números
        Assert.Null(i.CodToRemit);
        // 7 días: solo lo de la última semana
        var week = await Indicators(x).GetAsync(7, default);
        Assert.Equal((4, 3), (week.Created, week.Delivered));                       // O-1, O-3, O-4 y O-5 creadas; O-1, O-2 (el 12) y O-3 entregadas
        // el otro cliente ve solo lo suyo
        AsPortal(x, x.C2, 42);
        var other = await Indicators(x).GetAsync(null, default);
        Assert.Equal((30, 1, 0, 1), (other.Days, other.Created, other.Delivered, other.InTransitNow));
    }

    [Fact]
    public async Task With_no_deliveries_with_a_promised_date_there_is_no_percentage_and_the_days_are_0_7_30_or_90()
    {
        var x = await NewWorldAsync(ModuleKeys.ClientPortal);
        await using var _ = x.F;
        AsPortal(x, x.C1, 41);
        var empty = await Indicators(x).GetAsync(90, default);
        Assert.Equal((0, 0, 0), (empty.Created, empty.Delivered, empty.DeliveredWithPromise));
        Assert.Null(empty.OnTimePct);
        Assert.Null(empty.AvgDeliveryHours);
        Assert.Equal(PortalIndicatorsService.InvalidDaysMessage, (await Assert.ThrowsAsync<ValidationException>(() => Indicators(x).GetAsync(15, default))).Message);
        Assert.Equal("Los días deben ser 0 (hoy), 7, 30 o 90.", PortalIndicatorsService.InvalidDaysMessage);
        await Assert.ThrowsAsync<ValidationException>(() => Indicators(x).GetAsync(-1, default));
        Assert.Equal(0, (await Indicators(x).GetAsync(0, default)).Days);                       // 0 = hoy se acepta
        x.F.Tenant.PortalClientId = null;
        await Assert.ThrowsAsync<ForbiddenException>(() => Indicators(x).GetAsync(30, default));
    }

    [Fact]
    public async Task Today_counts_only_what_was_created_since_midnight_in_the_company_zone_not_the_last_24_hours()
    {
        var x = await NewWorldAsync(ModuleKeys.ClientPortal);
        await using var _ = x.F;
        var now = x.Now;   // 2026-10-18 16:00 UTC = 12:00 en Puerto Rico (UTC−4): la medianoche de hoy de la compañía es las 04:00 UTC
        await OrderAsync(x, x.C1, "H-1", OrderStatuses.Submitted, new DateTime(2026, 10, 18, 4, 30, 0, DateTimeKind.Utc));     // hoy 00:30 en PR: cuenta
        await OrderAsync(x, x.C1, "H-2", OrderStatuses.Submitted, now.AddMinutes(-5));                                          // hoy: cuenta
        await OrderAsync(x, x.C1, "H-3", OrderStatuses.Submitted, new DateTime(2026, 10, 18, 3, 0, 0, DateTimeKind.Utc));      // ayer 23:00 en PR (hoy en UTC): no cuenta
        await OrderAsync(x, x.C1, "H-4", OrderStatuses.Submitted, now.AddHours(-20));                                           // dentro de las últimas 24 horas pero ayer: no cuenta
        var delivered = await OrderAsync(x, x.C1, "H-5", OrderStatuses.Delivered, now.AddDays(-2));
        await DeliveredAtAsync(x, delivered, new DateTime(2026, 10, 18, 5, 0, 0, DateTimeKind.Utc));                            // entregada hoy (PR): cuenta como entregada hoy
        AsPortal(x, x.C1, 41);
        var pr = new TenantClock(LocalDay.DefaultZone, () => now);
        var service = new PortalIndicatorsService(x.F.Db, x.F.Tenant, x.F.Lookups, x.F.Get<ModuleService>(), pr);

        var today = await service.GetAsync(0, default);

        Assert.Equal((0, 2, 1), (today.Days, today.Created, today.Delivered));
        Assert.Equal(5, (await service.GetAsync(7, default)).Created);                         // la semana sí las trae todas
    }

    [Fact]
    public async Task With_billing_the_open_invoice_balance_and_overdue_count_appear_and_with_cod_the_money_to_remit()
    {
        var x = await NewWorldAsync(ModuleKeys.ClientPortal, ModuleKeys.Billing, ModuleKeys.Cod);
        await using var _ = x.F;
        AsPortal(x, x.C1, 41);
        var inv = new Teikem.Domain.Billing.Invoice { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.C1.ClientId, InvoiceNumber = "FAC-1", StatusCodeId = x.F.StatusId(Teikem.Domain.Billing.BillingCodes.InvoiceStatusDomain, Teikem.Domain.Billing.InvoiceStatuses.Overdue), CurrencyLookupId = 1, Subtotal = 100m, TaxAmount = 10m, IsActive = true };
        var other = new Teikem.Domain.Billing.Invoice { PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = x.C2.ClientId, InvoiceNumber = "FAC-9", StatusCodeId = x.F.StatusId(Teikem.Domain.Billing.BillingCodes.InvoiceStatusDomain, Teikem.Domain.Billing.InvoiceStatuses.Issued), CurrencyLookupId = 1, Subtotal = 900m, TaxAmount = 0m, IsActive = true };
        x.F.Db.Set<Teikem.Domain.Billing.Invoice>().AddRange(inv, other);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.Set<Teikem.Domain.Billing.Payment>().Add(new Teikem.Domain.Billing.Payment { TenantId = WmsFixture.TenantId, InvoiceId = inv.InvoiceId, PaymentMethodLookupId = x.F.LookupId(Teikem.Domain.Billing.BillingCodes.PaymentMethodDomain, "CASH"), Amount = 30m, PaidAtUtc = DateTime.UtcNow });
        var cod = await OrderAsync(x, x.C1, "O-1", OrderStatuses.Delivered, x.Now.AddDays(-2));
        var row = await x.F.Db.TransportOrders.AsTracking().SingleAsync(r => r.TransportOrderId == cod.TransportOrderId);
        (row.CodAmount, row.CodStatusCodeId) = (45m, x.F.StatusId(StatusDomains.CodStatus, CodStatuses.Collected));
        await x.F.Db.SaveChangesAsync();

        var i = await Indicators(x).GetAsync(30, default);

        Assert.Equal((80m, 1), (i.InvoiceBalance, i.OverdueInvoices));                 // 110 − 30 pagados; la factura de otro cliente no cuenta
        Assert.Equal(45m, i.CodToRemit);
    }
}
