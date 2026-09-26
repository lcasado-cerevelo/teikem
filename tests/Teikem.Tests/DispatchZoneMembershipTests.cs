using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;
using TripEntity = Teikem.Domain.Trips.Trip;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P5 — Miembros de zonas de despacho (InMemory):
/// - un valor no puede pertenecer a dos zonas ACTIVAS (409 que nombra la zona dueña); una zona inactiva no choca;
/// - reactivar una zona cuyos miembros chocan con otra zona activa es 409;
/// - un miembro de otra zona (o una zona de otro tenant) es 404 (BOLA por id hijo);
/// - criterio desconocido y POLYGON son 400; repetido en la misma zona es 409;
/// - inactivar una zona con rutas abiertas (DRAFT/PLANNED) es 409.
/// </summary>
public class DispatchZoneMembershipTests
{
    private const int TenantId = 1;
    private const int OtherTenantId = 2;
    private const int Z1 = 11, Z2 = 12, ZInactive = 13, ZForeign = 21;

    private static TeikemDbContext InMemoryDb()
        => new(new DbContextOptionsBuilder<TeikemDbContext>().UseInMemoryDatabase("dispatch-zone-members-" + Guid.NewGuid()).Options,
            new TenantContext { TenantId = TenantId, UserId = 1 });

    private static async Task<TeikemDbContext> SeedAsync()
    {
        var db = InMemoryDb();
        // El catálogo ZoneMatchType también en la BD: la carga de miembros activos (TripQueries) puede unirse a LookupCode.
        db.LookupCodes.AddRange(FakeLookups.Codes.Select(c => new LookupCode
        {
            LookupCodeId = c.LookupCodeId, Entity = c.Entity, InternalCode = c.InternalCode, LabelJson = c.LabelJson,
            SortOrder = c.SortOrder, IsSystem = true, IsActive = true,
        }));
        db.DispatchZones.AddRange(
            new DispatchZone { DispatchZoneId = Z1, TenantId = TenantId, Code = "Z1", Name = "Zona 1", IsActive = true },
            new DispatchZone { DispatchZoneId = Z2, TenantId = TenantId, Code = "Z2", Name = "Zona 2", IsActive = true },
            new DispatchZone { DispatchZoneId = ZInactive, TenantId = TenantId, Code = "ZI", Name = "Inactiva", IsActive = false },
            new DispatchZone { DispatchZoneId = ZForeign, TenantId = OtherTenantId, Code = "ZX", Name = "Ajena", IsActive = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static DispatchZoneService Service(TeikemDbContext db)
        => new(db, new TenantContext { TenantId = TenantId, UserId = 1 }, new FakeLookups());

    private static DispatchZoneMemberRequest PostalCode(string value) => new(ZoneMatchTypes.PostalCode, value);

    [Fact]
    public async Task Value_of_another_active_zone_conflicts_but_an_inactive_zone_does_not()
    {
        using var db = await SeedAsync();
        var svc = Service(db);

        var z1 = await svc.AddMemberAsync(Z1, PostalCode("00949"), default);
        Assert.Single(z1.Members);
        Assert.Equal(ZoneMatchTypes.PostalCode, z1.Members[0].MatchTypeCode);
        Assert.Equal("00949", z1.Members[0].MatchValue);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.AddMemberAsync(Z2, PostalCode("00949"), default));
        Assert.Equal("El valor '00949' ya pertenece a la zona Z1.", ex.Message);
        Assert.Equal(DispatchZoneService.MemberConflictMessage("00949", "Z1"), ex.Message);

        // Con Z1 inactiva, el valor queda libre para Z2.
        await svc.SetActiveAsync(Z1, false, default);
        var z2 = await svc.AddMemberAsync(Z2, PostalCode("00949"), default);
        Assert.Single(z2.Members);
    }

    [Fact]
    public async Task Reactivating_a_zone_whose_members_clash_with_another_active_zone_is_409()
    {
        using var db = await SeedAsync();
        var svc = Service(db);

        await svc.AddMemberAsync(Z1, PostalCode("00949"), default);
        await svc.SetActiveAsync(Z1, false, default);
        await svc.AddMemberAsync(Z2, PostalCode("00949"), default);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.SetActiveAsync(Z1, true, default));
        Assert.Equal("El valor '00949' ya pertenece a la zona Z2.", ex.Message);
        Assert.False((await db.DispatchZones.AsNoTracking().SingleAsync(z => z.DispatchZoneId == Z1)).IsActive);

        // Sin choque (se quita el miembro de Z2), la reactivación procede.
        var z2 = await svc.ListMembersAsync(Z2, default);
        await svc.RemoveMemberAsync(Z2, z2.Members[0].Id, default);
        var reactivated = await svc.SetActiveAsync(Z1, true, default);
        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task Member_of_another_zone_or_zone_of_another_tenant_is_404()
    {
        using var db = await SeedAsync();
        var svc = Service(db);

        var z2 = await svc.AddMemberAsync(Z2, PostalCode("00950"), default);
        var memberId = z2.Members[0].Id;

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => svc.RemoveMemberAsync(Z1, memberId, default));
        Assert.Equal("Criterio de zona no encontrado.", ex.Message);
        Assert.True(await db.DispatchZoneMembers.AnyAsync(m => m.DispatchZoneMemberId == memberId));

        var foreign = await Assert.ThrowsAsync<NotFoundException>(() => svc.ListMembersAsync(ZForeign, default));
        Assert.Equal("Zona de despacho no encontrada.", foreign.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.AddMemberAsync(ZForeign, PostalCode("00951"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.RemoveMemberAsync(ZForeign, memberId, default));

        // En la zona correcta el DELETE es físico.
        await svc.RemoveMemberAsync(Z2, memberId, default);
        Assert.False(await db.DispatchZoneMembers.AnyAsync(m => m.DispatchZoneMemberId == memberId));
    }

    [Fact]
    public async Task Unknown_criterion_and_polygon_are_400()
    {
        using var db = await SeedAsync();
        var svc = Service(db);

        var unknown = await Assert.ThrowsAsync<ValidationException>(() => svc.AddMemberAsync(Z1, new DispatchZoneMemberRequest("FOO", "x"), default));
        Assert.Equal("Criterio de zona desconocido: 'FOO'.", unknown.Message);

        var missing = await Assert.ThrowsAsync<ValidationException>(() => svc.AddMemberAsync(Z1, new DispatchZoneMemberRequest(null, "00949"), default));
        Assert.Equal(DispatchZoneService.MatchTypeRequiredMessage, missing.Message);

        var polygon = await Assert.ThrowsAsync<ValidationException>(
            () => svc.AddMemberAsync(Z1, new DispatchZoneMemberRequest(ZoneMatchTypes.Polygon, "POLYGON((0 0, 1 1, 1 0, 0 0))"), default));
        Assert.Contains("polígono", polygon.Message);

        Assert.False(await db.DispatchZoneMembers.AnyAsync());
    }

    [Fact]
    public async Task Repeated_criterion_in_the_same_zone_is_409_and_inactive_zone_is_400()
    {
        using var db = await SeedAsync();
        var svc = Service(db);

        await svc.AddMemberAsync(Z1, PostalCode("00949"), default);
        var dup = await Assert.ThrowsAsync<ConflictException>(() => svc.AddMemberAsync(Z1, PostalCode("00949"), default));
        Assert.Equal("La zona ya tiene ese criterio.", dup.Message);

        var inactive = await Assert.ThrowsAsync<ValidationException>(() => svc.AddMemberAsync(ZInactive, PostalCode("00960"), default));
        Assert.Equal("La zona de despacho está inactiva.", inactive.Message);
    }

    [Fact]
    public async Task Resolve_without_postal_code_or_city_is_400()
    {
        using var db = await SeedAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service(db).ResolveAsync("  ", null, default));
        Assert.Equal("Indique el código postal o el pueblo.", ex.Message);
    }

    [Theory]
    [InlineData(TripStatuses.Draft, true)]
    [InlineData(TripStatuses.Planned, true)]
    [InlineData(TripStatuses.Cancelled, false)]
    public async Task Deactivating_a_zone_with_open_trips_is_409(string tripStatus, bool blocks)
    {
        using var db = await SeedAsync();
        db.StatusCodes.Add(new StatusCode { StatusCodeId = 900, Entity = StatusDomains.TripStatus, InternalCode = tripStatus, StageKindLookupId = 1 });
        db.Trips.Add(new TripEntity
        {
            TripId = 1, TenantId = TenantId, PublicId = Guid.NewGuid(), Code = "2026-0001", PlanDate = new DateOnly(2026, 9, 26),
            DispatchZoneId = Z1, StatusCodeId = 900, IsActive = tripStatus != TripStatuses.Cancelled,
        });
        await db.SaveChangesAsync();
        var svc = Service(db);

        if (blocks)
        {
            var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.SetActiveAsync(Z1, false, default));
            Assert.Equal("La zona tiene rutas abiertas; ciérrelas o cámbielas de zona antes de inactivarla.", ex.Message);
            Assert.True((await db.DispatchZones.AsNoTracking().SingleAsync(z => z.DispatchZoneId == Z1)).IsActive);
        }
        else
        {
            var dto = await svc.SetActiveAsync(Z1, false, default);
            Assert.False(dto.IsActive);
        }
    }

    /// <summary>ILookupCache mínimo con el dominio ZoneMatchType sembrado.</summary>
    private sealed class FakeLookups : ILookupCache
    {
        public static readonly LookupCode[] Codes =
        {
            new() { LookupCodeId = 501, Entity = LookupDomains.ZoneMatchType, InternalCode = ZoneMatchTypes.PostalCode, LabelJson = "{\"es\":\"Código postal\",\"en\":\"Postal code\"}", SortOrder = 1 },
            new() { LookupCodeId = 502, Entity = LookupDomains.ZoneMatchType, InternalCode = ZoneMatchTypes.PostalRange, LabelJson = "{\"es\":\"Rango postal\",\"en\":\"Postal range\"}", SortOrder = 2 },
            new() { LookupCodeId = 503, Entity = LookupDomains.ZoneMatchType, InternalCode = ZoneMatchTypes.Municipality, LabelJson = "{\"es\":\"Municipio\",\"en\":\"Municipality\"}", SortOrder = 3 },
            new() { LookupCodeId = 504, Entity = LookupDomains.ZoneMatchType, InternalCode = ZoneMatchTypes.Polygon, LabelJson = "{\"es\":\"Polígono\",\"en\":\"Polygon\"}", SortOrder = 4 },
        };

        public async Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default)
            => await TryGetIdAsync(entity, code, ct) ?? throw new InvalidOperationException($"{entity}.{code}");

        public Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default)
            => Task.FromResult(Codes.FirstOrDefault(c => c.Entity == entity && string.Equals(c.InternalCode, code, StringComparison.OrdinalIgnoreCase))?.LookupCodeId);

        public Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default)
            => Task.FromResult(Codes.FirstOrDefault(c => c.LookupCodeId == lookupCodeId));

        public Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LookupCode>>(Codes.Where(c => c.Entity == entity).ToList());

        public void Invalidate() { }
    }
}
