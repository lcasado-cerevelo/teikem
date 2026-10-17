using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
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
    private static ProgressStatus S(string code, string kind, int sort, string? label = null, bool enabled = true) => new(code, label ?? code, kind, sort, enabled);

    // ---------------------------------------------------------------- reglas de la barra

    private static readonly ProgressStatus[] Seed =
    {
        S("DRAFT", "PIPELINE", 1), S("CONFIRMED", "PIPELINE", 2), S("PICKUP", "PIPELINE", 3), S("IN_TRANSIT", "PIPELINE", 6), S("DELIVERED", "TERMINAL", 8),
        S("ON_HOLD", "LATERAL", 20), S("FAILED", "LATERAL", 22), S("CANCELLED", "TERMINAL", 30),
    };

    [Fact]
    public void The_steps_are_the_companys_normal_path_in_order_ending_at_the_first_terminal()
    {
        var steps = OrderProgress.StepsOf(Seed);
        Assert.Equal(new[] { "DRAFT", "CONFIRMED", "PICKUP", "IN_TRANSIT", "DELIVERED" }, steps.Select(s => s.Code));   // ni los laterales ni la cancelación son etapas
        // si la compañía apaga un estatus o agrega otro, la barra cambia de tamaño
        var custom = Seed.Where(s => s.Code != "PICKUP").Append(S("OUT_FOR_DELIVERY", "PIPELINE", 7, "Va para su casa")).Append(S("INBOUND", "PIPELINE", 4, enabled: false)).ToList();
        var mine = OrderProgress.StepsOf(custom);
        Assert.Equal(new[] { "DRAFT", "CONFIRMED", "IN_TRANSIT", "OUT_FOR_DELIVERY", "DELIVERED" }, mine.Select(s => s.Code));
        Assert.Equal("Va para su casa", mine.Single(s => s.Code == "OUT_FOR_DELIVERY").Label);
        Assert.Empty(OrderProgress.StepsOf(Array.Empty<ProgressStatus>()));
    }

    [Fact]
    public void Progress_of_an_order_on_the_normal_path_is_its_position_and_the_last_step_is_completed()
    {
        var steps = OrderProgress.StepsOf(Seed);
        var mid = OrderProgress.Compute(steps, "PICKUP", "Recogido", "PIPELINE", null);
        Assert.Equal((3, 5, OrderProgress.InProgress), (mid.Step, mid.Total, mid.State));
        var first = OrderProgress.Compute(steps, "draft", "Borrador", "PIPELINE", null);
        Assert.Equal((1, OrderProgress.InProgress), (first.Step, first.State));
        var done = OrderProgress.Compute(steps, "DELIVERED", "Entregada", "TERMINAL", null);
        Assert.Equal((5, 5, OrderProgress.Completed), (done.Step, done.Total, done.State));
    }

    [Fact]
    public void A_held_or_failed_order_stays_where_it_was_and_a_cancelled_one_is_empty()
    {
        var steps = OrderProgress.StepsOf(Seed);
        // en espera: la barra se queda en el último estatus normal de su historial (del más reciente al más antiguo)
        var held = OrderProgress.Compute(steps, "ON_HOLD", "En espera", "LATERAL", new[] { "CONFIRMED", "DRAFT" });
        Assert.Equal((2, 5, OrderProgress.Alert, "ON_HOLD"), (held.Step, held.Total, held.State, held.StatusCode));
        // varios laterales seguidos: salta los laterales del historial
        var failed = OrderProgress.Compute(steps, "FAILED", "Fallida", "LATERAL", new[] { "ON_HOLD", "IN_TRANSIT", "PICKUP" });
        Assert.Equal((4, OrderProgress.Alert), (failed.Step, failed.State));
        // sin historial: queda en la primera etapa
        Assert.Equal(1, OrderProgress.Compute(steps, "ON_HOLD", "En espera", "LATERAL", null).Step);
        var cancelled = OrderProgress.Compute(steps, "CANCELLED", "Cancelada", "TERMINAL", new[] { "CONFIRMED" });
        Assert.Equal((0, OrderProgress.Cancelled), (cancelled.Step, cancelled.State));
        Assert.Equal("Cancelada", cancelled.StatusLabel);
    }

    // ---------------------------------------------------------------- identificador y número

    [Fact]
    public void Slug_rules_normalize_validate_and_reserve_words()
    {
        Assert.Equal("advance-depot", TrackingRules.Normalize("  Advance-Depot "));
        Assert.Null(TrackingRules.Normalize("   "));
        foreach (var ok in new[] { "abc", "advance-depot", "a1b2", new string('a', 40) }) Assert.True(TrackingRules.IsValidSlug(ok), ok);
        foreach (var bad in new[] { "ab", "-abc", "abc-", "ab--cd", "Abc", "con espacio", "ñandú", new string('a', 41), "a_b" }) Assert.False(TrackingRules.IsValidSlug(bad), bad);
        Assert.True(TrackingRules.IsReserved("api"));
        Assert.True(TrackingRules.IsReserved("login"));
        Assert.False(TrackingRules.IsReserved("advance"));
        Assert.True(TrackingRules.LooksLikeWaybill("CND-0000415"));
        Assert.True(TrackingRules.LooksLikeWaybill(" cnd-1 "));
        foreach (var bad in new[] { null, "", "ab", "CND 123", "CND-1; DROP", "../etc", new string('9', 31) }) Assert.False(TrackingRules.LooksLikeWaybill(bad), bad ?? "null");
    }

    // ---------------------------------------------------------------- consulta pública (servicio)

    private static async Task<(OrderReadFixture F, PublicTrackingService Svc)> WorldAsync()
    {
        var f = await OrderReadFixture.CreateAsync();
        // estatus de entrega y en tránsito (el fixture solo trae borrador, confirmada, en espera y cancelada)
        var pipe = f.Db.LookupCodes.Single(l => l.Entity == LookupDomains.StageKind && l.InternalCode == StageKinds.Pipeline).LookupCodeId;
        var term = f.Db.LookupCodes.Single(l => l.Entity == LookupDomains.StageKind && l.InternalCode == StageKinds.Terminal).LookupCodeId;
        f.Db.StatusCodes.AddRange(
            new StatusCode { StatusCodeId = 300, Entity = StatusDomains.OrderStatus, InternalCode = "IN_TRANSIT", LabelJson = "{\"es\":\"En tránsito\",\"en\":\"In transit\"}", StageKindLookupId = pipe, SortOrder = 6, IsActive = true },
            new StatusCode { StatusCodeId = 301, Entity = StatusDomains.OrderStatus, InternalCode = "DELIVERED", LabelJson = "{\"es\":\"Entregada\",\"en\":\"Delivered\"}", StageKindLookupId = term, SortOrder = 8, IsActive = true });
        f.Db.Tenants.AddRange(
            new Tenant { TenantId = 1, Name = "Advance Logistics", TrackingSlug = "advance", IsActive = true, DefaultLangCode = "es" },
            new Tenant { TenantId = 2, Name = "Otra Compañía", TrackingSlug = "otra", IsActive = true, DefaultLangCode = "es" });
        await f.Db.SaveChangesAsync();
        // el mismo número de conduce en las dos compañías (se repite entre compañías) y uno en la compañía 1
        foreach (var o in f.Db.TransportOrders.IgnoreQueryFilters().ToList())
            o.WaybillNumber = o.TransportOrderId switch { 1 => "CND-0000001", 2 => "CND-0000002", 3 => "CND-0000003", 4 => "CND-0000004", 5 => "CND-0000001", _ => o.WaybillNumber };
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return (f, new PublicTrackingService(f.Db, f.Tenant, new OrderProgressService(f.Db, f.Tenant, f.Lookups)));
    }

    [Fact]
    public async Task The_public_answer_is_only_the_progress_with_the_companys_steps()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        var dto = await svc.TrackAsync("advance", "cnd-0000002", null, default);   // O2 está CONFIRMADA
        Assert.Equal("CND-0000002", dto.Waybill);
        Assert.Equal("Advance Logistics", dto.CompanyName);
        Assert.Equal(("CONFIRMED", 2, OrderProgress.InProgress), (dto.Steps[dto.Step - 1].Key, dto.Step, dto.State));
        Assert.Equal(new[] { "DRAFT", "CONFIRMED", "IN_TRANSIT", "DELIVERED" }, dto.Steps.Select(s => s.Key));   // las etapas de ESTA compañía (4 aquí; 8 con el seed completo)
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
    public async Task The_same_number_in_two_companies_answers_with_each_companys_own_order_and_the_right_language()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        var mine = await svc.TrackAsync("advance", "CND-0000001", null, default);   // O1: borrador, compañía 1
        var other = await svc.TrackAsync("otra", "CND-0000001", null, default);     // O5: confirmada, compañía 2
        Assert.Equal(("Advance Logistics", 1), (mine.CompanyName, mine.Step));
        Assert.Equal(("Otra Compañía", 2), (other.CompanyName, other.Step));
        // el idioma pedido se respeta (los estatus de la compañía en inglés)
        var en = await svc.TrackAsync("advance", "CND-0000001", "en", default);
        Assert.Equal("DRAFT", en.StatusLabel.ToUpperInvariant());   // el fixture solo tiene etiqueta en español: cae a ella
        Assert.Equal("es", f.Tenant.Lang);                           // y la petición no deja cambiado el idioma
    }

    [Fact]
    public async Task A_held_order_keeps_the_bar_where_it_was_using_its_history_and_a_cancelled_one_is_empty()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        var typeId = f.Db.LookupCodes.Single(l => l.Entity == LookupDomains.EntityType && l.InternalCode == EntityTypes.TransportOrder).LookupCodeId;
        var confirmed = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.Confirmed);
        var onHold = f.StatusId(StatusDomains.OrderStatus, OrderStatuses.OnHold);
        var o = f.Db.TransportOrders.IgnoreQueryFilters().Single(x => x.TransportOrderId == 3);
        o.StatusCodeId = onHold;
        f.Db.EntityStatusHistories.AddRange(
            new EntityStatusHistory { TenantId = 1, EntityTypeLookupId = typeId, EntityId = 3, ToStatusCodeId = confirmed, ChangedAtUtc = DateTime.UtcNow.AddHours(-3) },
            new EntityStatusHistory { TenantId = 1, EntityTypeLookupId = typeId, EntityId = 3, FromStatusCodeId = confirmed, ToStatusCodeId = onHold, ChangedAtUtc = DateTime.UtcNow.AddHours(-1) });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var held = await svc.TrackAsync("advance", "CND-0000003", null, default);
        Assert.Equal((2, OrderProgress.Alert, "ON_HOLD"), (held.Step, held.State, held.StatusLabel));   // sigue en «Confirmada»
        Assert.True(held.Steps[0].Done);
        Assert.True(held.Steps[1].Current);
        Assert.InRange((DateTime.UtcNow - held.LastUpdateUtc!.Value).TotalMinutes, 55, 65);   // el último movimiento fue hace una hora

        var cancelled = await svc.TrackAsync("advance", "CND-0000004", null, default);          // O4: cancelada
        Assert.Equal((0, OrderProgress.Cancelled), (cancelled.Step, cancelled.State));
        Assert.All(cancelled.Steps, s => Assert.False(s.Done || s.Current));
    }

    [Fact]
    public async Task Everything_that_cannot_be_answered_gives_the_same_404()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        var t = await f.Db.Tenants.IgnoreQueryFilters().SingleAsync(x => x.TenantId == 2);
        t.TrackingSlug = null;   // la compañía 2 apagó el seguimiento
        var inactive = f.Db.TransportOrders.IgnoreQueryFilters().Single(x => x.TransportOrderId == 1);
        inactive.IsActive = false;   // orden dada de baja
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();

        var cases = new (string? Slug, string? Waybill)[]
        {
            ("nadie", "CND-0000002"),        // identificador desconocido
            ("otra", "CND-0000001"),         // compañía con el seguimiento apagado
            ("advance", "CND-9999999"),      // número que no existe
            ("advance", "CND-0000001"),      // orden dada de baja
            ("advance", "no es un número"),  // mal escrito
            ("Adv ance", "CND-0000002"),     // identificador inválido
            (null, null),
        };
        foreach (var (slug, waybill) in cases)
        {
            var ex = await Assert.ThrowsAsync<TrackingNotFoundException>(() => svc.TrackAsync(slug, waybill, null, default));
            Assert.Equal(TrackingRules.NotFoundMessage, ex.Message);
            Assert.Equal(404, ex.StatusCode);
        }
    }

    // ---------------------------------------------------------------- ajuste de la compañía

    [Fact]
    public async Task The_company_turns_public_tracking_on_with_a_valid_free_identifier_and_off_with_an_empty_one()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        Assert.Equal(new TrackingSettingsDto("advance", true), await svc.GetSettingsAsync(default));
        Assert.Equal(new TrackingSettingsDto("mi-compania", true), await svc.SetSettingsAsync(new TrackingSettingsRequest("  Mi-Compania "), default));
        await Assert.ThrowsAsync<TrackingNotFoundException>(() => svc.TrackAsync("advance", "CND-0000002", null, default));   // el anterior ya no sirve
        Assert.Equal("Advance Logistics", (await svc.TrackAsync("mi-compania", "CND-0000002", null, default)).CompanyName);
        Assert.Equal(new TrackingSettingsDto(null, false), await svc.SetSettingsAsync(new TrackingSettingsRequest("   "), default));
        await Assert.ThrowsAsync<TrackingNotFoundException>(() => svc.TrackAsync("mi-compania", "CND-0000002", null, default));
    }

    [Fact]
    public async Task Invalid_reserved_and_taken_identifiers_are_refused_with_their_message()
    {
        var (f, svc) = await WorldAsync();
        await using var _ = f;
        var bad = await Assert.ThrowsAsync<ValidationException>(() => svc.SetSettingsAsync(new TrackingSettingsRequest("a b"), default));
        Assert.Equal(TrackingRules.InvalidSlugMessage, bad.Errors!["slug"].Single());
        var reserved = await Assert.ThrowsAsync<ValidationException>(() => svc.SetSettingsAsync(new TrackingSettingsRequest("api"), default));
        Assert.Equal(TrackingRules.ReservedSlugMessage, reserved.Errors!["slug"].Single());
        var taken = await Assert.ThrowsAsync<ConflictException>(() => svc.SetSettingsAsync(new TrackingSettingsRequest("otra"), default));   // lo usa la compañía 2
        Assert.Equal(TrackingRules.SlugTakenMessage, taken.Message);
        Assert.Equal("advance", (await svc.GetSettingsAsync(default)).Slug);   // nada cambió
    }
}
