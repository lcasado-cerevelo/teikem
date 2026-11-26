using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Files;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Clients;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 — ClientDocumentQueries (la consulta de vigencia que comparten la lista, la fuente de datos y el escaneo): tipo con las marcas y el apagado de la compañía,
/// renovado, exención que respalda, ventana del «hasta», faltantes y aislamiento por compañía con TenantId explícito (también sin usuario).
/// </summary>
public sealed class ClientDocumentQueriesTests
{
    private const int T1 = WmsFixture.TenantId;
    private const int T2 = WmsFixture.OtherTenantId;
    private static readonly DateOnly Today = new(2026, 11, 27);
    private const int StateType = 9101, ResellerType = 9102, OtherType = 9103, OwnType = 9104;

    private static async Task<WmsFixture> WorldAsync()
    {
        var f = await WmsFixture.CreateAsync();
        f.Db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = StateType, Entity = ClientDocumentCodes.TypeDomain, InternalCode = ClientDocumentCodes.IvuExemptionState, LabelJson = "{\"es\":\"Estatal\",\"en\":\"State\"}",
                ExtraJson = "{\"requiresValidTo\":true,\"isTax\":true}", IsActive = true },
            new LookupCode { LookupCodeId = ResellerType, Entity = ClientDocumentCodes.TypeDomain, InternalCode = ClientDocumentCodes.ResellerCertificate, LabelJson = "{\"es\":\"Revendedor\"}",
                ExtraJson = "{\"requiresValidTo\":false,\"isTax\":true}", IsActive = true },
            new LookupCode { LookupCodeId = OtherType, Entity = ClientDocumentCodes.TypeDomain, InternalCode = ClientDocumentCodes.Other, LabelJson = "{\"es\":\"Otro\"}", IsActive = true },
            new LookupCode { LookupCodeId = OwnType, Entity = ClientDocumentCodes.TypeDomain, InternalCode = "PERMISO_T2", LabelJson = "{\"es\":\"Propio de T2\"}", IsActive = true, TenantId = T2 });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return f;
    }

    private static int _nextFile = 70000;

    private static async Task<ClientDocument> DocAsync(WmsFixture f, Client client, int typeId, DateOnly? validTo, string? number = null, bool fileActive = true, bool active = true)
    {
        var file = new StoredFile
        {
            StoredFileId = _nextFile++, PublicId = Guid.NewGuid(), TenantId = client.TenantId, OwnerEntityLookupId = 1, OwnerId = client.ClientId, FileName = "c.pdf",
            ContentType = "application/pdf", SizeBytes = 10, Sha256 = "x", StorageKey = "k", IsActive = fileActive,
        };
        var doc = new ClientDocument
        {
            PublicId = Guid.NewGuid(), TenantId = client.TenantId, ClientId = client.ClientId, StoredFileId = file.StoredFileId, DocumentTypeLookupId = typeId,
            DocumentNumber = number, ValidFrom = validTo?.AddYears(-1), ValidTo = validTo, IsActive = active, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<StoredFile>().Add(file);
        f.Db.Set<ClientDocument>().Add(doc);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return doc;
    }

    private static async Task<ClientTaxExemption> ExemptionAsync(WmsFixture f, ClientDocument doc, bool active = true)
    {
        var e = new ClientTaxExemption
        {
            PublicId = Guid.NewGuid(), TenantId = doc.TenantId, ClientId = doc.ClientId, ExemptionType = "Estatal", CertificateNumber = doc.DocumentNumber ?? "-",
            ValidFrom = doc.ValidFrom ?? Today, ValidTo = doc.ValidTo, IsActive = active, ClientDocumentId = doc.ClientDocumentId, CreatedAtUtc = DateTime.UtcNow,
        };
        f.Db.Set<ClientTaxExemption>().Add(e);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return e;
    }

    [Fact]
    public async Task TypeAsync_marcas_etiqueta_y_apagado_de_la_compania()
    {
        var f = await WorldAsync();
        var state = await ClientDocumentQueries.TypeAsync(f.Db, f.Lookups, T1, " ivu_exemption_state ", default);
        Assert.NotNull(state);
        Assert.Equal((StateType, ClientDocumentCodes.IvuExemptionState, true, new ClientDocumentTypeMarks(true, true)), (state!.LookupCodeId, state.Code, state.IsEnabled, state.Marks));
        Assert.Null(await ClientDocumentQueries.TypeAsync(f.Db, f.Lookups, T1, "NO_EXISTE", default));
        Assert.Null(await ClientDocumentQueries.TypeAsync(f.Db, f.Lookups, T1, null, default));
        // El tipo propio de otra compañía no existe para T1.
        Assert.Null(await ClientDocumentQueries.TypeAsync(f.Db, f.Lookups, T1, "PERMISO_T2", default));

        // La compañía apaga el tipo, le cambia la etiqueta y le quita «lleva vigencia».
        f.Db.LookupCodeOverrides.Add(new LookupCodeOverride { TenantId = T1, LookupCodeId = StateType, IsEnabled = false, CustomLabelJson = "{\"es\":\"Exención estatal\"}",
            CustomExtraJson = "{\"requiresValidTo\":false}" });
        // El ajuste de otra compañía no cuenta.
        f.Db.LookupCodeOverrides.Add(new LookupCodeOverride { TenantId = T2, LookupCodeId = OtherType, CustomExtraJson = "{\"isTax\":true}" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        state = await ClientDocumentQueries.TypeAsync(f.Db, f.Lookups, T1, ClientDocumentCodes.IvuExemptionState, default);
        Assert.False(state!.IsEnabled);
        Assert.Equal(new ClientDocumentTypeMarks(false, true), state.Marks);
        Assert.Equal("Exención estatal", Teikem.Domain.Common.MultilingualText.Resolve(state.LabelJson, "es"));
        Assert.Equal("State", Teikem.Domain.Common.MultilingualText.Resolve(state.LabelJson, "en"));

        var marks = await ClientDocumentQueries.MarksByTypeIdAsync(f.Db, T1, null, default);
        Assert.Equal(ClientDocumentTypeMarks.Default, marks[OtherType]);
        Assert.Equal(new ClientDocumentTypeMarks(false, true), marks[ResellerType]);
        Assert.False(marks.ContainsKey(OwnType));
        Assert.Empty(await ClientDocumentQueries.MarksByTypeIdAsync(f.Db, T1, Array.Empty<int>(), default));
    }

    [Fact]
    public async Task ValidityRows_renovado_respaldo_ventana_y_faltantes()
    {
        var f = await WorldAsync();
        var a = await f.AddClientAsync("A");
        var b = await f.AddClientAsync("B");
        var oldCert = await DocAsync(f, a, StateType, Today.AddDays(10), "E-1");
        var newCert = await DocAsync(f, a, StateType, Today.AddYears(1), "E-2");
        var reseller = await DocAsync(f, a, ResellerType, null);
        var expired = await DocAsync(f, b, StateType, Today.AddDays(-3), "E-9");
        await DocAsync(f, b, StateType, Today.AddDays(5), fileActive: false);   // archivo quitado: no cuenta
        await DocAsync(f, b, StateType, Today.AddDays(6), active: false);       // documento quitado: no cuenta
        var ex = await ExemptionAsync(f, newCert);
        await ExemptionAsync(f, expired, active: false);                          // exención dada de baja: no respalda

        var all = await ClientDocumentQueries.ValidityRowsAsync(f.Db, f.Lookups, T1, new ValidityFilter { IncludeMissing = true }, default);
        Assert.Equal(new[] { expired.ClientDocumentId, oldCert.ClientDocumentId, newCert.ClientDocumentId, reseller.ClientDocumentId }, all.Select(r => r.DocumentId));
        var old = all.Single(r => r.DocumentId == oldCert.ClientDocumentId);
        Assert.True(old.IsRenewed);
        Assert.Equal((a.ClientId, a.PublicId, "A", ClientDocumentCodes.IvuExemptionState, new ClientDocumentTypeMarks(true, true), "E-1"),
            (old.ClientId, old.ClientPublicId, old.ClientCode, old.TypeCode, old.Marks, old.Number));
        Assert.Null(old.BackingExemptionId);
        var nw = all.Single(r => r.DocumentId == newCert.ClientDocumentId);
        Assert.False(nw.IsRenewed);
        Assert.Equal(ex.ClientTaxExemptionId, nw.BackingExemptionId);
        Assert.False(all.Single(r => r.DocumentId == expired.ClientDocumentId).IsRenewed);
        Assert.Null(all.Single(r => r.DocumentId == expired.ClientDocumentId).BackingExemptionId);

        // Ventana sobre el «hasta» (bordes incluidos), sin faltantes. El renovado se sigue marcando aunque el que lo renueva quede fuera de la ventana.
        var window = await ClientDocumentQueries.ValidityRowsAsync(f.Db, f.Lookups, T1,
            new ValidityFilter { ValidToFrom = Today.AddDays(-3), ValidToTo = Today.AddDays(10) }, default);
        Assert.Equal(new[] { expired.ClientDocumentId, oldCert.ClientDocumentId }, window.Select(r => r.DocumentId));
        Assert.True(window.Single(r => r.DocumentId == oldCert.ClientDocumentId).IsRenewed);

        // Por cliente y por documentos.
        Assert.Equal(new[] { expired.ClientDocumentId },
            (await ClientDocumentQueries.ValidityRowsAsync(f.Db, f.Lookups, T1, new ValidityFilter { ClientId = b.ClientId, IncludeMissing = true }, default)).Select(r => r.DocumentId));
        Assert.Equal(new[] { reseller.ClientDocumentId },
            (await ClientDocumentQueries.ValidityRowsAsync(f.Db, f.Lookups, T1, new ValidityFilter { DocumentIds = new[] { reseller.ClientDocumentId }, IncludeMissing = true }, default))
            .Select(r => r.DocumentId));
        Assert.Empty(await ClientDocumentQueries.ValidityRowsAsync(f.Db, f.Lookups, T1, new ValidityFilter { DocumentIds = new[] { reseller.ClientDocumentId } }, default));
        Assert.Empty(await ClientDocumentQueries.ValidityRowsAsync(f.Db, f.Lookups, T1, new ValidityFilter { DocumentIds = Array.Empty<int>(), IncludeMissing = true }, default));

        // Cliente dado de baja: sus documentos no salen.
        var row = await f.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == b.ClientId);
        row.IsActive = false;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.DoesNotContain((await ClientDocumentQueries.ValidityRowsAsync(f.Db, f.Lookups, T1, new ValidityFilter { IncludeMissing = true }, default)),
            r => r.ClientId == b.ClientId);
    }

    [Fact]
    public async Task ActiveBackingExemption_vigente_vencida_y_dada_de_baja()
    {
        var f = await WorldAsync();
        var a = await f.AddClientAsync("A");
        var current = await DocAsync(f, a, StateType, Today, "E-1");
        var past = await DocAsync(f, a, StateType, Today.AddDays(-1), "E-2");
        var loose = await DocAsync(f, a, StateType, Today.AddDays(30), "E-3");
        var exCurrent = await ExemptionAsync(f, current);
        await ExemptionAsync(f, past);
        await ExemptionAsync(f, loose, active: false);

        var b1 = await ClientDocumentQueries.ActiveBackingExemptionAsync(f.Db, T1, current.ClientDocumentId, Today, default);
        Assert.Equal(new ClientDocumentBackingExemption(exCurrent.ClientTaxExemptionId, exCurrent.PublicId, Today, true), b1);
        var b2 = await ClientDocumentQueries.ActiveBackingExemptionAsync(f.Db, T1, past.ClientDocumentId, Today, default);
        Assert.False(b2!.IsCurrent);
        Assert.Null(await ClientDocumentQueries.ActiveBackingExemptionAsync(f.Db, T1, loose.ClientDocumentId, Today, default));
        // Con el TenantId de otra compañía no se ve nada.
        using (f.Tenant.BypassTenantFilter())
            Assert.Null(await ClientDocumentQueries.ActiveBackingExemptionAsync(f.Db, T2, current.ClientDocumentId, Today, default));
    }

    [Fact]
    public async Task Sin_usuario_filtra_por_el_TenantId_explicito()
    {
        var f = await WorldAsync();
        var a = await f.AddClientAsync("A");
        var other = await f.AddClientAsync("Z", T2);
        var mine = await DocAsync(f, a, StateType, Today.AddDays(3), "E-1");
        using (f.AsTenant(T2))
            await DocAsync(f, other, OwnType, Today.AddDays(3), "Z-1");

        using (f.Tenant.AsAnonymous(T1))
        {
            var rows = await ClientDocumentQueries.ValidityRowsAsync(f.Db, f.Lookups, T1, new ValidityFilter { IncludeMissing = true }, default);
            Assert.Equal(new[] { mine.ClientDocumentId }, rows.Select(r => r.DocumentId));
        }
        using (f.Tenant.BypassTenantFilter())
        {
            var rows = await ClientDocumentQueries.ValidityRowsAsync(f.Db, f.Lookups, T2, new ValidityFilter { IncludeMissing = true }, default);
            var only = Assert.Single(rows);
            Assert.Equal(("PERMISO_T2", ClientDocumentTypeMarks.Default), (only.TypeCode, only.Marks));
            Assert.Equal(OwnType, (await ClientDocumentQueries.TypeAsync(f.Db, f.Lookups, T2, "permiso_t2", default))!.LookupCodeId);
        }
    }
}
