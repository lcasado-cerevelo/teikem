using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 (Lote D del plan) — documentos del cliente contra SQL Server REAL (se omite sin TEIKEM_TEST_SQLSERVER), sobre la base desechable de
/// <see cref="NotificationSqlFixture"/> (creada desde el modelo EF, sin FK): el RowVersion lo genera SQL Server y cambia al editar, un rowVersion viejo da 409 con el
/// mensaje del documento, la copia de la exención se sincroniza, quitar un documento que respalda una exención vigente da 409, y las consultas de la lista «Documentos por
/// vencer» y de la fuente CLIENT_DOCUMENT se traducen a SQL. El CHECK CK_ClientDocument_Dates vive en la sección del lote en update.sql y lo prueba Lote248SqlTests.
/// </summary>
public sealed class ClientDocumentSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    private static DateOnly Today => TenantClock.Default.Today;

    private sealed record World(int TenantId, int ClientId, Guid ClientPublicId, int StateTypeId);

    private static TenantContext Ctx(int tenantId) => new() { TenantId = tenantId, UserId = 1, IsAuthenticated = true, IsPlatformAdmin = true };

    /// <summary>Una compañía con un cliente y su propio tipo de documento con vigencia y de impuestos (código propio por compañía: el índice único (Entity, InternalCode) es global).</summary>
    private async Task<World> WorldAsync(string name)
    {
        var tenantId = await fx.NewTenantAsync(name);
        await using var db = fx.NewDb(Ctx(tenantId));
        var type = new LookupCode
        {
            Entity = ClientDocumentCodes.TypeDomain, InternalCode = "IVU_T" + tenantId, TenantId = tenantId, LabelJson = "{\"es\":\"Estatal\",\"en\":\"State\"}",
            ExtraJson = "{\"requiresValidTo\":true,\"isTax\":true}", IsActive = true,
        };
        db.LookupCodes.Add(type);
        var client = new Client { PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "C" + Guid.NewGuid().ToString("N")[..8], Name = "Farmacia " + name, StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return new World(tenantId, client.ClientId, client.PublicId, type.LookupCodeId);
    }

    private async Task<ClientDocument> DocAsync(World w, DateOnly? validTo, string? number)
    {
        await using var db = fx.NewDb(Ctx(w.TenantId));
        var file = new StoredFile
        {
            PublicId = Guid.NewGuid(), TenantId = w.TenantId, OwnerEntityLookupId = 1, OwnerId = w.ClientId, FileName = "c.pdf", ContentType = "application/pdf", SizeBytes = 10,
            Sha256 = "x", StorageKey = "k" + Guid.NewGuid().ToString("N"), IsActive = true,
        };
        db.Set<StoredFile>().Add(file);
        await db.SaveChangesAsync();
        var doc = new ClientDocument
        {
            PublicId = Guid.NewGuid(), TenantId = w.TenantId, ClientId = w.ClientId, StoredFileId = file.StoredFileId, DocumentTypeLookupId = w.StateTypeId, DocumentNumber = number,
            ValidFrom = validTo?.AddYears(-1), ValidTo = validTo, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        db.Set<ClientDocument>().Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    private async Task<ClientTaxExemption> ExemptionAsync(World w, ClientDocument? doc, DateOnly? validTo = null, string cert = "VIEJA")
    {
        await using var db = fx.NewDb(Ctx(w.TenantId));
        var e = new ClientTaxExemption
        {
            PublicId = Guid.NewGuid(), TenantId = w.TenantId, ClientId = w.ClientId, ExemptionType = "Estatal", CertificateNumber = doc?.DocumentNumber ?? cert,
            ValidFrom = doc?.ValidFrom ?? Today.AddYears(-1), ValidTo = doc is null ? validTo : doc.ValidTo, IsActive = true, ClientDocumentId = doc?.ClientDocumentId, CreatedAtUtc = DateTime.UtcNow,
        };
        db.Set<ClientTaxExemption>().Add(e);
        await db.SaveChangesAsync();
        return e;
    }

    /// <summary>Servicios con el usuario administrador (todo permiso) y los módulos dados (en la caché, como ModuleService los lee primero).</summary>
    private static (ClientDocumentService Docs, ClientDocumentExpiryService Expiry) Services(TeikemDbContext db, TenantContext ctx, params string[] moduleKeys)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set($"modules:{ctx.TenantId}", new HashSet<string>(moduleKeys, StringComparer.OrdinalIgnoreCase));
        var lookups = new TripTestLookups();
        var permissions = new PermissionService(db, ctx, cache, lookups, new ReceivingNullSecurityEventWriter());
        var modules = new ModuleService(db, ctx, cache);
        // el almacén de archivos no se usa al editar, quitar ni listar
        return (new ClientDocumentService(db, ctx, lookups, null!, permissions, modules, TenantClock.Default),
            new ClientDocumentExpiryService(db, ctx, lookups, permissions, modules, TenantClock.Default));
    }

    [SqlServerFact]
    public async Task RowVersion_changes_on_edit_a_stale_one_is_409_and_the_exemption_copy_follows()
    {
        var w = await WorldAsync("Documentos A");
        var doc = await DocAsync(w, Today.AddDays(20), "EX-1");
        var exemption = await ExemptionAsync(w, doc);

        string rowVersion;
        await using (var db = fx.NewDb(Ctx(w.TenantId)))
        {
            var listed = Assert.Single(await Services(db, Ctx(w.TenantId), ModuleKeys.Catalog).Docs.ListAsync(w.ClientPublicId, default));
            Assert.True(listed.BacksExemption);
            Assert.Equal((ClientDocumentExpiryStates.Expiring, 20, true), (listed.ExpiryState, listed.DaysToExpiry, listed.IsTax));
            rowVersion = listed.RowVersion!;
            Assert.False(string.IsNullOrEmpty(rowVersion));
        }

        await using (var db = fx.NewDb(Ctx(w.TenantId)))
        {
            var edited = await Services(db, Ctx(w.TenantId), ModuleKeys.Catalog).Docs.UpdateAsync(w.ClientPublicId, doc.PublicId,
                new ClientDocumentPatchRequest(DocumentNumber: "EX-2", ValidTo: Today.AddDays(400), RowVersion: rowVersion), default);
            Assert.NotEqual(rowVersion, edited.RowVersion);
            Assert.Equal(("EX-2", Today.AddDays(400)), (edited.DocumentNumber, edited.ValidTo));
        }
        await using (var db = fx.NewDb(Ctx(w.TenantId)))
        {
            var copy = await db.Set<ClientTaxExemption>().AsNoTracking().SingleAsync(e => e.ClientTaxExemptionId == exemption.ClientTaxExemptionId);
            Assert.Equal(("EX-2", (DateOnly?)Today.AddDays(400)), (copy.CertificateNumber, copy.ValidTo));
        }

        await using (var db = fx.NewDb(Ctx(w.TenantId)))
        {
            var stale = await Assert.ThrowsAsync<ConflictException>(() => Services(db, Ctx(w.TenantId), ModuleKeys.Catalog).Docs.UpdateAsync(w.ClientPublicId, doc.PublicId,
                new ClientDocumentPatchRequest(Notes: "otra", RowVersion: rowVersion), default));
            Assert.Equal(ClientDocumentService.ConcurrencyMessage, stale.Message);
        }

        await using (var db = fx.NewDb(Ctx(w.TenantId)))
        {
            var conflict = await Assert.ThrowsAsync<ConflictException>(() => Services(db, Ctx(w.TenantId), ModuleKeys.Catalog).Docs.RemoveAsync(w.ClientPublicId, doc.PublicId, default));
            Assert.Equal(ClientDocumentRules.BacksExemptionCannotRemove, conflict.Message);
        }
    }

    [SqlServerFact]
    public async Task The_expiring_list_and_the_data_source_translate_to_sql_and_stay_in_their_company()
    {
        var w = await WorldAsync("Documentos B");
        var renewed = await DocAsync(w, Today.AddDays(5), "EX-5");
        await DocAsync(w, Today.AddDays(200), "EX-200");   // del mismo cliente y tipo: renueva al EX-5
        var legacy = await ExemptionAsync(w, null, Today.AddDays(30), "VIEJA-B");
        int secondClient;
        await using (var seed = fx.NewDb(Ctx(w.TenantId)))
        {
            var c2 = new Client { PublicId = Guid.NewGuid(), TenantId = w.TenantId, Code = "C" + Guid.NewGuid().ToString("N")[..8], Name = "Segundo", StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
            seed.Clients.Add(c2);
            await seed.SaveChangesAsync();
            secondClient = c2.ClientId;
        }
        var soon = await DocAsync(w with { ClientId = secondClient }, Today.AddDays(7), "EX-7");
        var other = await WorldAsync("Documentos C");
        await DocAsync(other, Today.AddDays(5), "EX-OTRA");

        await using var db = fx.NewDb(Ctx(w.TenantId));
        var (_, expiry) = Services(db, Ctx(w.TenantId), ModuleKeys.Catalog, ModuleKeys.Tax);
        var list = await expiry.ListExpiringAsync(new ExpiringClientDocumentsQuery(WithinDays: 30, Q: null), default);
        Assert.Equal(new[] { soon.PublicId }, list.Items.Where(i => i.RowKind == ExpiringClientDocumentRowKinds.Document).Select(i => i.DocumentPublicId!.Value));
        Assert.Equal(new[] { legacy.PublicId }, list.Items.Where(i => i.RowKind == ExpiringClientDocumentRowKinds.ExemptionWithoutDocument).Select(i => i.ExemptionPublicId!.Value));
        Assert.Equal("Estatal", list.Items.Single(i => i.DocumentPublicId == soon.PublicId).DocumentTypeLabel);

        var rows = await new ClientDocumentDataSource(db, new TripTestLookups(), Ctx(w.TenantId), TenantClock.Default).LoadAsync(new DataQuery(), default);
        Assert.Equal(new[] { "EX-5", "EX-7", "EX-200" }, rows.Select(r => (string?)r["DocumentNumber"]));
        Assert.Equal(new object?[] { ClientDocumentExpiryStates.Renewed, ClientDocumentExpiryStates.Expiring, ClientDocumentExpiryStates.Valid }, rows.Select(r => r["ExpiryState"]));
        Assert.DoesNotContain(list.Items, i => i.DocumentPublicId == renewed.PublicId);
    }
}
