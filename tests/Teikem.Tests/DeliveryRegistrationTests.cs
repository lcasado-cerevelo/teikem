using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Geocoding;
using Teikem.Domain.Identity;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 118 — registrar la entrega desde la web: la orden pasa a «Entregada» (brincando lo que falte), queda el comprobante con la firma AUTOMÁTICA del usuario (no del consignatario), una entrega por orden,
/// los estatus finales no se entregan, y la ubicación se deja en cola solo si Google está encendido. Más las reglas puras de la geocodificación.
/// </summary>
public class DeliveryRegistrationTests
{
    private static DeliveryRegistrationService Svc(TripServiceFixture f) => f.Get<DeliveryRegistrationService>();
    private static DeliveryRegisterRequest Req(string? name = "Ana Rivera", string? notes = null, DateTime? at = null) => new(name, notes, at);

    [Fact]
    public async Task Registering_a_delivery_in_transit_leaves_the_order_delivered_and_a_proof_signed_by_the_user()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        f.Db.Users.Add(new ApplicationUser { Id = 1, UserName = "dispatch", Email = "dispatch@demo.test", FullName = "Dalys Dispatch" });
        await f.Db.SaveChangesAsync();
        var o = await f.SeedOrderAsync(OrderStatuses.InTransit);

        var dto = await Svc(f).RegisterAsync(o.PublicId, Req("Ana Rivera", "Dejado con la vecina"), default);

        Assert.Equal(OrderStatuses.Delivered, await f.OrderStatusAsync(o.Id));
        Assert.Equal(OrderStatuses.Delivered, dto.OrderStatusCode);
        Assert.Equal("Ana Rivera", dto.RecipientName);
        Assert.Equal("Dejado con la vecina", dto.Notes);
        Assert.Equal(DeliveryProofCodes.SourceStaffWeb, dto.Source);
        Assert.Equal(DeliveryProofCodes.SignatureUser, dto.SignatureKind);            // firma del usuario, no del consignatario
        Assert.Equal("Dalys Dispatch", dto.SignedByName);
        Assert.Equal("Dalys Dispatch", dto.RegisteredByName);
        Assert.Equal(DeliveryProofCodes.GeoNone, dto.GeoStatus);                      // sin Google encendido no se busca ubicación
        var stored = await f.Db.Set<DeliveryProof>().AsNoTracking().SingleAsync();
        Assert.Equal(1, stored.RecordedBy);
        Assert.Equal(1, stored.SignedByUserId);
        Assert.NotEqual(Guid.Empty, stored.PublicId);
        Assert.Equal(dto.PublicId, (await Svc(f).FindByOrderAsync(o.PublicId, default))!.PublicId);
    }

    [Fact]
    public async Task From_submitted_the_order_jumps_to_delivered_and_the_steps_in_between_are_recorded_as_skipped()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await f.SeedOrderAsync(OrderStatuses.Submitted);

        await Svc(f).RegisterAsync(o.PublicId, Req(), default);

        Assert.Equal(OrderStatuses.Delivered, await f.OrderStatusAsync(o.Id));
        var history = await f.Db.EntityStatusHistories.AsNoTracking().Where(h => h.EntityId == o.Id).OrderBy(h => h.EntityStatusHistoryId).ToListAsync();
        Assert.True(history.Count >= 2);
        Assert.Equal(1, history.Count(h => !h.IsSkipped));                             // solo el último paso (Entregada) no es brincado
        Assert.Single(history.Select(h => h.ChangedAtUtc).Distinct());                 // misma fecha y hora, como el escaneo
        Assert.All(history, h => Assert.Contains("Entrega registrada desde la web", h.Comment));
    }

    [Fact]
    public async Task An_order_with_a_failed_attempt_can_still_be_delivered()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await f.SeedOrderAsync(OrderStatuses.Failed);
        await Svc(f).RegisterAsync(o.PublicId, Req(), default);
        Assert.Equal(OrderStatuses.Delivered, await f.OrderStatusAsync(o.Id));
    }

    [Fact]
    public async Task Registering_twice_is_a_conflict_and_a_final_order_is_not_delivered()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await f.SeedOrderAsync(OrderStatuses.InTransit);
        await Svc(f).RegisterAsync(o.PublicId, Req(), default);
        var again = await Assert.ThrowsAsync<ConflictException>(() => Svc(f).RegisterAsync(o.PublicId, Req(), default));
        Assert.Contains("ya tiene registrada su entrega", again.Message);

        var cancelled = await f.SeedOrderAsync(OrderStatuses.Cancelled);
        var final = await Assert.ThrowsAsync<StatusRuleException>(() => Svc(f).RegisterAsync(cancelled.PublicId, Req(), default));
        Assert.Contains("estatus final", final.Message);
        Assert.Empty(await f.Db.Set<DeliveryProof>().AsNoTracking().Where(p => p.TransportOrderId == cancelled.Id).ToListAsync());
    }

    [Fact]
    public async Task The_recipient_is_required_and_the_date_cannot_be_in_the_future()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var o = await f.SeedOrderAsync(OrderStatuses.InTransit);
        Assert.Equal(DeliveryProofRules.RecipientRequired, (await Assert.ThrowsAsync<ValidationException>(() => Svc(f).RegisterAsync(o.PublicId, Req("  "), default))).Message);
        Assert.Equal(DeliveryProofRules.RecipientRequired, (await Assert.ThrowsAsync<ValidationException>(() => Svc(f).RegisterAsync(o.PublicId, null, default))).Message);
        Assert.Equal(DeliveryProofRules.RecipientTooLong, (await Assert.ThrowsAsync<ValidationException>(() => Svc(f).RegisterAsync(o.PublicId, Req(new string('x', 151)), default))).Message);
        Assert.Equal(DeliveryProofRules.NotesTooLong, (await Assert.ThrowsAsync<ValidationException>(() => Svc(f).RegisterAsync(o.PublicId, Req(notes: new string('n', 501)), default))).Message);
        Assert.Equal(DeliveryProofRules.FutureDelivery, (await Assert.ThrowsAsync<ValidationException>(() => Svc(f).RegisterAsync(o.PublicId, Req(at: DateTime.UtcNow.AddHours(2)), default))).Message);
        Assert.Equal(OrderStatuses.InTransit, await f.OrderStatusAsync(o.Id));       // nada cambió
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(f).RegisterAsync(Guid.NewGuid(), Req(), default));
    }

    [Fact]
    public async Task With_google_on_the_location_is_queued_and_with_it_off_there_is_no_search()
    {
        await using var f = await TripServiceFixture.CreateAsync();
        var on = new DeliveryRegistrationService(f.Db, f.Get<ITenantContext>(), f.Get<StatusService>(), Options.Create(new GoogleMapsOptions { Enabled = true, ApiKey = "clave-de-prueba" }));
        var o = await f.SeedOrderAsync(OrderStatuses.InTransit);
        var dto = await on.RegisterAsync(o.PublicId, Req(), default);
        Assert.Equal(DeliveryProofCodes.GeoPending, dto.GeoStatus);
        Assert.Null(dto.Latitude);

        var marker = new DeliveryRegistrationService(f.Db, f.Get<ITenantContext>(), f.Get<StatusService>(), Options.Create(new GoogleMapsOptions { Enabled = true, ApiKey = "<use env GoogleMaps__ApiKey>" }));
        var o2 = await f.SeedOrderAsync(OrderStatuses.InTransit);
        Assert.Equal(DeliveryProofCodes.GeoNone, (await marker.RegisterAsync(o2.PublicId, Req(), default)).GeoStatus);   // un marcador no es una llave
    }

    // ------------------------------------------------------------ reglas de la ubicación

    [Theory]
    [InlineData("ROOFTOP", "HIGH")]
    [InlineData("RANGE_INTERPOLATED", "MEDIUM")]
    [InlineData("GEOMETRIC_CENTER", "MEDIUM")]
    [InlineData("APPROXIMATE", "LOW")]
    [InlineData(null, "LOW")]
    public void Confidence_follows_the_google_location_type(string? type, string expected) => Assert.Equal(expected, DeliveryProofRules.ConfidenceOf(type));

    [Fact]
    public void The_minimum_confidence_and_the_island_box_filter_what_google_returns()
    {
        Assert.True(DeliveryProofRules.MeetsMinimum("LOW", null));
        Assert.True(DeliveryProofRules.MeetsMinimum("MEDIUM", "MEDIUM"));
        Assert.False(DeliveryProofRules.MeetsMinimum("LOW", "MEDIUM"));
        Assert.False(DeliveryProofRules.MeetsMinimum("MEDIUM", "HIGH"));
        Assert.True(DeliveryProofRules.InPuertoRico(18.4655, -66.1057));                // San Juan
        Assert.False(DeliveryProofRules.InPuertoRico(25.76, -80.19));                   // Miami
    }

    [Fact]
    public void The_address_skips_empty_parts_and_ends_with_puerto_rico()
        => Assert.Equal("Calle Luna 12, San Juan, 00901, Puerto Rico", DeliveryProofRules.BuildAddress(" Calle Luna 12 ", null, "San Juan", "", "00901"));

    private const string Ok = """{"status":"OK","results":[{"geometry":{"location":{"lat":18.4655,"lng":-66.1057},"location_type":"ROOFTOP"}}]}""";

    [Fact]
    public void Google_answers_become_a_located_not_found_or_failed_outcome()
    {
        var located = GoogleGeocoder.Interpret(DeliveryProofRules.ParseGeocode(Ok), "LOW");
        Assert.Equal((DeliveryProofCodes.GeoOk, 18.4655m, -66.1057m, "HIGH"), (located.Status, located.Lat, located.Lng, located.Confidence));
        Assert.Equal(DeliveryProofCodes.GeoNotFound, GoogleGeocoder.Interpret(DeliveryProofRules.ParseGeocode("""{"status":"ZERO_RESULTS","results":[]}"""), "LOW").Status);
        Assert.Equal(DeliveryProofCodes.GeoFailed, GoogleGeocoder.Interpret(DeliveryProofRules.ParseGeocode("""{"status":"OVER_QUERY_LIMIT","error_message":"x"}"""), "LOW").Status);
        Assert.Equal(DeliveryProofCodes.GeoFailed, GoogleGeocoder.Interpret(DeliveryProofRules.ParseGeocode("""{"status":"REQUEST_DENIED"}"""), "LOW").Status);
        Assert.Equal(DeliveryProofCodes.GeoFailed, GoogleGeocoder.Interpret(DeliveryProofRules.ParseGeocode("no es json"), "LOW").Status);
        Assert.Equal(DeliveryProofCodes.GeoFailed, GoogleGeocoder.Interpret(DeliveryProofRules.ParseGeocode(Ok), "LOW", httpOk: false).Status);
        // poca precisión para el mínimo pedido, o fuera de la isla: no se acepta
        Assert.Equal(DeliveryProofCodes.GeoNotFound, GoogleGeocoder.Interpret(DeliveryProofRules.ParseGeocode(Ok.Replace("ROOFTOP", "APPROXIMATE")), "HIGH").Status);
        Assert.Equal(DeliveryProofCodes.GeoNotFound, GoogleGeocoder.Interpret(DeliveryProofRules.ParseGeocode(Ok.Replace("18.4655", "25.7617").Replace("-66.1057", "-80.1918")), "LOW").Status);
    }

    // ------------------------------------------------------------ seguridad

    [Fact]
    public void Registering_needs_the_ltl_module_and_orders_deliver_and_reading_needs_orders_view()
    {
        var t = typeof(OrderDeliveryController);
        Assert.Equal(new[] { ModuleKeys.LtlGround }, t.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<AllowAnonymousAttribute>());
        var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var posts = actions.Where(a => a.GetCustomAttribute<HttpPostAttribute>() is not null).ToList();   // Lote 127: registrar la entrega y adjuntar archivos, los dos con orders.deliver
        Assert.Equal(2, posts.Count);
        var get = Assert.Single(actions, a => a.GetCustomAttribute<HttpGetAttribute>() is not null);
        Assert.All(posts, post => Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.OrdersDeliver }, post.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy)));
        Assert.Equal(new[] { RequirePermissionAttribute.Prefix + PermissionCatalog.OrdersView }, get.GetCustomAttributes<RequirePermissionAttribute>().Select(p => p.Policy));
        Assert.All(actions, a => Assert.DoesNotContain(a.GetParameters(), p => string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase)));
    }
}
