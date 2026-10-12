using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote F-D2 (hallazgo de la pantalla de Flota): un vehículo dado de baja definitiva (estatus terminal) solo se consulta, igual que
/// el chofer eliminado. Antes el PATCH y el alta, edición y baja de documentos seguían funcionando sobre un vehículo ya dado de
/// baja (la pantalla lo muestra de solo lectura). Ahora responden 409 «El vehículo está dado de baja definitiva; solo se consulta su
/// historial.»; un vehículo vigente se sigue editando.
/// </summary>
public class VehicleRetiredReadOnlyTests
{
    private const int TenantId = 1;
    private const int ActiveStatus = 1, RetiredStatus = 3, TerminalKind = 900, PipelineKind = 901, PermitType = 700;
    private static readonly Guid LiveId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RetiredId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static TeikemDbContext InMemoryDb()
        => new(new DbContextOptionsBuilder<TeikemDbContext>()
                .UseInMemoryDatabase("vehicle-retired-" + Guid.NewGuid())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options,
            new TenantContext { TenantId = TenantId, UserId = 1 });

    private static async Task<TeikemDbContext> SeedAsync()
    {
        var db = InMemoryDb();
        db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = TerminalKind, Entity = LookupDomains.StageKind, InternalCode = StageKinds.Terminal, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = PipelineKind, Entity = LookupDomains.StageKind, InternalCode = StageKinds.Pipeline, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = PermitType, Entity = LookupDomains.VehicleDocType, InternalCode = "PERMIT", LabelJson = "{\"es\":\"Permiso\"}", IsActive = true });
        db.StatusCodes.AddRange(
            new StatusCode { StatusCodeId = ActiveStatus, Entity = StatusDomains.VehicleStatus, InternalCode = "ACTIVE", LabelJson = "{\"es\":\"Activo\"}", StageKindLookupId = PipelineKind, IsInitial = true },
            new StatusCode { StatusCodeId = RetiredStatus, Entity = StatusDomains.VehicleStatus, InternalCode = "INACTIVE", LabelJson = "{\"es\":\"Inactivo\"}", StageKindLookupId = TerminalKind });
        db.Vehicles.AddRange(
            new Vehicle { VehicleId = 10, PublicId = LiveId, TenantId = TenantId, Code = "V-LIVE", PlateNumber = "AAA", StatusCodeId = ActiveStatus, IsActive = true },
            new Vehicle { VehicleId = 20, PublicId = RetiredId, TenantId = TenantId, Code = "V-OLD", PlateNumber = "OLD", StatusCodeId = RetiredStatus, IsActive = false });
        db.VehicleDocuments.AddRange(
            new VehicleDocument { VehicleDocumentId = 1, VehicleId = 10, DocTypeLookupId = PermitType, DocNumber = "P-1", IsActive = true },
            new VehicleDocument { VehicleDocumentId = 2, VehicleId = 20, DocTypeLookupId = PermitType, DocNumber = "P-2", IsActive = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static VehicleService Vehicles(TeikemDbContext db)
        => new(db, new TenantContext { TenantId = TenantId, UserId = 1 }, new FakeLookups(), null!);

    private static VehicleDocumentService Documents(TeikemDbContext db)
        => new(db, new TenantContext { TenantId = TenantId, UserId = 1 }, new FakeLookups());

    [Fact]
    public async Task Retired_vehicle_cannot_be_patched_but_a_live_one_can()
    {
        using var db = await SeedAsync();
        var vehicles = Vehicles(db);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => vehicles.UpdateAsync(RetiredId, new VehiclePatchRequest(PlateNumber: "NUEVA"), default));
        Assert.Equal("El vehículo está dado de baja definitiva; solo se consulta su historial.", ex.Message);
        Assert.Equal(VehicleService.RetiredReadOnlyMessage, ex.Message);
        Assert.Equal("OLD", (await db.Vehicles.AsNoTracking().SingleAsync(v => v.VehicleId == 20)).PlateNumber);

        var updated = await vehicles.UpdateAsync(LiveId, new VehiclePatchRequest(PlateNumber: "NUEVA"), default);
        Assert.Equal("NUEVA", updated.PlateNumber);
    }

    [Fact]
    public async Task Retired_vehicle_documents_cannot_be_added_edited_or_removed()
    {
        using var db = await SeedAsync();
        var documents = Documents(db);

        var add = await Assert.ThrowsAsync<ConflictException>(() => documents.AddAsync(RetiredId, new VehicleDocumentRequest(DocType: "PERMIT", DocNumber: "NUEVO"), default));
        var edit = await Assert.ThrowsAsync<ConflictException>(() => documents.UpdateAsync(RetiredId, 2, new VehicleDocumentRequest(DocNumber: "OTRO"), default));
        var remove = await Assert.ThrowsAsync<ConflictException>(() => documents.DeactivateAsync(RetiredId, 2, default));
        Assert.All(new[] { add, edit, remove }, e => Assert.Equal(VehicleService.RetiredReadOnlyMessage, e.Message));

        var stored = await db.VehicleDocuments.AsNoTracking().Where(d => d.VehicleId == 20).ToListAsync();
        Assert.Single(stored);
        Assert.Equal("P-2", stored[0].DocNumber);
        Assert.True(stored[0].IsActive);
    }

    [Fact]
    public async Task Live_vehicle_documents_are_still_added_edited_and_removed()
    {
        using var db = await SeedAsync();
        var documents = Documents(db);

        var added = await documents.AddAsync(LiveId, new VehicleDocumentRequest(DocType: "PERMIT", DocNumber: "NUEVO"), default);
        Assert.Equal("NUEVO", added.DocNumber);
        var edited = await documents.UpdateAsync(LiveId, added.Id, new VehicleDocumentRequest(DocNumber: "EDITADO"), default);
        Assert.Equal("EDITADO", edited.DocNumber);
        var removed = await documents.DeactivateAsync(LiveId, added.Id, default);
        Assert.False(removed.IsActive);
    }

    [Fact]
    public async Task A_vehicle_of_another_tenant_is_still_404_not_409()
    {
        using var db = await SeedAsync();
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => Vehicles(db).UpdateAsync(Guid.NewGuid(), new VehiclePatchRequest(PlateNumber: "X"), default));
        Assert.Equal("Vehículo no encontrado.", ex.Message);
    }

    /// <summary>ILookupCache mínimo con el tipo de documento PERMIT.</summary>
    private sealed class FakeLookups : ILookupCache
    {
        private static readonly LookupCode[] Codes =
        {
            new() { LookupCodeId = PermitType, Entity = LookupDomains.VehicleDocType, InternalCode = "PERMIT", LabelJson = "{\"es\":\"Permiso\"}" },
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
