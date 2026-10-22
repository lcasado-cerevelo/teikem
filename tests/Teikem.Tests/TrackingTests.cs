using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 103 — «¿Cómo va mi orden?» y la barra de progreso: la barra tiene tantas etapas como estatus tiene el camino normal de la compañía (con sus nombres), los laterales no avanzan la barra y la respuesta pública
/// no lleva nombres, direcciones ni números del cliente. El número de conduce se repite entre compañías: cada una tiene su identificador.
/// </summary>
public class TrackingTests
{
    // ---------------------------------------------------------------- reglas de la barra (cuatro etapas fijas)

    [Fact]
    public void The_bar_has_four_fixed_steps_in_the_askers_language()
    {
        Assert.Equal(new[] { "SUBMITTED", "RECEIVED", "ON_ROUTE", "DONE" }, OrderProgress.Steps().Select(s => s.Code));
        Assert.Equal(new[] { "Sometido", "Recibida", "En ruta", "Completada" }, OrderProgress.Steps("es").Select(s => s.Label));
        Assert.Equal(new[] { "Submitted", "Received", "On route", "Completed" }, OrderProgress.Steps("en").Select(s => s.Label));
        Assert.Equal(OrderProgress.TotalSteps, OrderProgress.Steps().Count);
    }

    [Theory]
    [InlineData("SUBMITTED", "Sometida", "PIPELINE", false, 1, OrderProgress.InProgress, "Sometido")]
    [InlineData("PICKUP", "Recogida", "PIPELINE", false, 2, OrderProgress.InProgress, "Recibida")]
    [InlineData("UNASSIGNED", "Sin asignar", "PIPELINE", false, 2, OrderProgress.InProgress, "Recibida")]
    [InlineData("ASSIGNED", "Asignada", "PIPELINE", false, 2, OrderProgress.InProgress, "Recibida")]
    [InlineData("IN_TRANSIT", "En tránsito", "PIPELINE", false, 3, OrderProgress.InProgress, "En ruta")]
    [InlineData("FAILED", "Intento fallido", "LATERAL", true, 3, OrderProgress.Alert, "Intentada")]
    [InlineData("UNASSIGNED", "Sin asignar", "PIPELINE", true, 3, OrderProgress.InProgress, "Reprogramada")]   // volvió al almacén tras un intento
    [InlineData("ASSIGNED", "Asignada", "PIPELINE", true, 3, OrderProgress.InProgress, "Reprogramada")]
    [InlineData("IN_TRANSIT", "En tránsito", "PIPELINE", true, 3, OrderProgress.InProgress, "En ruta")]          // y cuando el chofer la vuelve a coger, «En ruta»
    [InlineData("DELIVERED", "Entregada", "TERMINAL", false, 4, OrderProgress.Completed, "Entregada")]
    [InlineData("IN_WAREHOUSE", "En almacén", "TERMINAL", false, 4, OrderProgress.Completed, "En almacén")]
    [InlineData("RETURNED", "Devuelta", "TERMINAL", true, 4, OrderProgress.Completed, "Devuelta")]               // cualquier estatus final: prevalece el suyo
    [InlineData("CANCELLED", "Cancelada", "TERMINAL", false, 4, OrderProgress.Cancelled, "Cancelada")]
    public void Every_status_lands_in_one_of_the_four_steps(string code, string label, string kind, bool failedBefore, int step, string state, string shown)
    {
        var r = OrderProgress.Compute(code, label, kind, failedBefore, "es");
        Assert.Equal((step, 4, state, code, shown), (r.Step, r.Total, r.State, r.StatusCode, r.StatusLabel));
    }

    [Fact]
    public void The_extra_wording_follows_the_language_and_an_unknown_company_status_waits_in_received_with_an_alert()
    {
        Assert.Equal("Attempted", OrderProgress.Compute("FAILED", "x", "LATERAL", true, "en").StatusLabel);
        Assert.Equal("Rescheduled", OrderProgress.Compute("ASSIGNED", "x", "PIPELINE", true, "en").StatusLabel);
        Assert.Equal("On route", OrderProgress.Compute("in_transit", "x", "PIPELINE", false, "en").StatusLabel);
        var other = OrderProgress.Compute("CUSTOM_HOLD", "Retenida", "LATERAL", false, "es");
        Assert.Equal((2, OrderProgress.Alert, "Retenida"), (other.Step, other.State, other.StatusLabel));
    }

    // ---------------------------------------------------------------- consulta pública por enlace (servicio)

    private static async Task<(OrderReadFixture F, PublicTrackingService Svc)> WorldAsync()
    {
        var f = await OrderReadFixture.CreateAsync();
        // estatus de entrega y en tránsito (el fixture solo trae sometida, recogida, intento fallido y cancelada)
        var pipe = f.Db.LookupCodes.Single(l => l.Entity == LookupDomains.StageKind && l.InternalCode == StageKinds.Pipeline).LookupCodeId;
        var term = f.Db.LookupCodes.Single(l => l.Entity == LookupDomains.StageKind && l.InternalCode == StageKinds.Terminal).LookupCodeId;
        f.Db.StatusCodes.AddRange(
            new StatusCode { StatusCodeId = 300, Entity = StatusDomains.OrderStatus, InternalCode = "IN_TRANSIT", LabelJson = "{\"es\":\"En tránsito\",\"en\":\"In transit\"}", StageKindLookupId = pipe, SortOrder = 6, IsActive = true },
            new StatusCode { StatusCodeId = 301, Entity = StatusDomains.OrderStatus, InternalCode = "DELIVERED", LabelJson = "{\"es\":\"Entregada\",\"en\":\"Delivered\"}", StageKindLookupId = term, SortOrder = 8, IsActive = true });
        f.Db.Tenants.AddRange(
            new Tenant { TenantId = 1, Name = "Advance Logistics", IsActive = true, DefaultLangCode = "es" },
            new Tenant { TenantId = 2, Name = "Otra Compañía", IsActive = true, DefaultLangCode = "es" });
        await f.Db.SaveChangesAsync();
        // el mismo número de conduce en las dos compañías (se repite entre compañías) y un token propio por orden
        foreach (var o in f.Db.TransportOrders.IgnoreQueryFilters().ToList())
        {
            o.WaybillNumber = o.TransportOrderId switch { 1 => "CND-0000001", 2 => "CND-0000002", 3 => "CND-0000003", 4 => "CND-0000004", 5 => "CND-0000001", _ => o.WaybillNumber };
            o.TrackToken = "tok-" + o.TransportOrderId;
        }
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return (f, new PublicTrackingService(f.Db, f.Tenant, new OrderProgressService(f.Db, f.Tenant, f.Lookups)));
    }

    [Fact]
    public async Task The_public_answer_is_only_the_progress_with_the_companys_steps()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        var dto = await svc.TrackByTokenAsync("tok-2", null, default);   // O2 está RECOGIDA
        Assert.Equal("CND-0000002", dto.Waybill);
        Assert.Equal("Advance Logistics", dto.CompanyName);
        Assert.Equal(("RECEIVED", 2, OrderProgress.InProgress), (dto.Steps[dto.Step - 1].Key, dto.Step, dto.State));   // recogida = «Recibida»
        Assert.Equal(new[] { "SUBMITTED", "RECEIVED", "ON_ROUTE", "DONE" }, dto.Steps.Select(s => s.Key));        // siempre las mismas cuatro etapas
        Assert.Equal(dto.Steps.Count, dto.Total);
        Assert.Equal(new[] { true, false, false, false }, dto.Steps.Select(s => s.Done));
        Assert.Equal(new[] { false, true, false, false }, dto.Steps.Select(s => s.Current));
        // nada de personas, direcciones, números del cliente ni montos
        var json = JsonSerializer.Serialize(dto);
        foreach (var secret in new[] { "Consignatario", "Calle 1", "Cliente Uno", "2026-2", "F-2", "EMP-2", "San Juan", "00901", "26.25", "200" })
            Assert.DoesNotContain(secret, json);
        Assert.Equal(new[] { "Waybill", "CompanyName", "Step", "Total", "State", "StatusLabel", "Steps", "LastUpdateUtc" }, typeof(PublicTrackDto).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task The_token_answers_with_its_own_companys_order_and_the_right_language_and_the_same_number_in_two_companies_does_not_mix()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        var mine = await svc.TrackByTokenAsync("tok-1", null, default);   // O1: sometida, compañía 1
        var other = await svc.TrackByTokenAsync("tok-5", null, default);   // O5: recogida, compañía 2 (mismo conduce CND-0000001)
        Assert.Equal(("Advance Logistics", 1), (mine.CompanyName, mine.Step));
        Assert.Equal(("Otra Compañía", 2), (other.CompanyName, other.Step));
        // el idioma pedido se respeta (los estatus de la compañía en inglés)
        var en = await svc.TrackByTokenAsync("tok-1", "en", default);
        Assert.Equal("Submitted", en.StatusLabel);
        Assert.Equal("es", f.Tenant.Lang);                           // y la petición no deja cambiado el idioma
    }

    [Fact]
    public async Task A_failed_attempt_keeps_the_bar_where_it_was_using_its_history_and_a_cancelled_one_is_empty()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        var typeId = f.Db.LookupCodes.Single(l => l.Entity == LookupDomains.EntityType && l.InternalCode == EntityTypes.TransportOrder).LookupCodeId;
        var pickup = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.Pickup);
        var failed = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.Failed);
        var o = f.Db.TransportOrders.IgnoreQueryFilters().Single(x => x.TransportOrderId == 3);
        o.StatusCodeId = failed;
        f.Db.EntityStatusHistories.AddRange(
            new EntityStatusHistory { TenantId = 1, EntityTypeLookupId = typeId, EntityId = 3, ToStatusCodeId = pickup, ChangedAtUtc = DateTime.UtcNow.AddHours(-3) },
            new EntityStatusHistory { TenantId = 1, EntityTypeLookupId = typeId, EntityId = 3, FromStatusCodeId = pickup, ToStatusCodeId = failed, ChangedAtUtc = DateTime.UtcNow.AddHours(-1) });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var held = await svc.TrackByTokenAsync("tok-3", null, default);
        Assert.Equal((3, OrderProgress.Alert, "Intentada"), (held.Step, held.State, held.StatusLabel));   // sigue en «En ruta», en alerta
        Assert.Equal(new[] { true, true, false, false }, held.Steps.Select(s => s.Done));
        Assert.True(held.Steps[2].Current);
        Assert.InRange((DateTime.UtcNow - held.LastUpdateUtc!.Value).TotalMinutes, 55, 65);   // el último movimiento fue hace una hora

        var cancelled = await svc.TrackByTokenAsync("tok-4", null, default);          // O4: cancelada
        Assert.Equal((4, OrderProgress.Cancelled), (cancelled.Step, cancelled.State));
        Assert.All(cancelled.Steps, s => Assert.False(s.Done || s.Current));
    }

    [Fact]
    public async Task Everything_that_cannot_be_answered_gives_the_same_404()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        var inactive = f.Db.TransportOrders.IgnoreQueryFilters().Single(x => x.TransportOrderId == 1);
        inactive.IsActive = false;   // orden dada de baja
        var t = await f.Db.Tenants.IgnoreQueryFilters().SingleAsync(x => x.TenantId == 2);
        t.IsActive = false;          // compañía dada de baja
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        foreach (var token in new string?[] { "no-existe", "tok-1", "tok-5", null, "", "   ", new string('x', 65), "CND-0000002" })
        {
            var ex = await Assert.ThrowsAsync<ConsigneeLinkNotFoundException>(() => svc.TrackByTokenAsync(token, null, default));
            Assert.Equal(ConsigneeNoticeRules.TrackLinkNotFoundMessage, ex.Message);
            Assert.Equal(404, ex.StatusCode);
        }
    }
}
