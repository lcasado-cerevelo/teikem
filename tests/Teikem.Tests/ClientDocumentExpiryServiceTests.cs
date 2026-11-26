using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 (Lote D del plan) — lista «Documentos por vencer» (ClientDocumentExpiryService): ventana por días (o el aviso de la compañía), vencidos y faltantes, sin
/// renovados ni clientes inactivos, orden por «hasta», tope de 2000 (Truncated), filas de exenciones sin documento solo con tax.manage y TAX, buscador y aislamiento por
/// compañía.
/// </summary>
public sealed class ClientDocumentExpiryServiceTests
{
    private static DateOnly Today => TenantClock.Default.Today;
    private const string IvuMarks = "{\"requiresValidTo\":true,\"isTax\":true}";

    private static async Task<SalesVendorWorld> WorldAsync()
    {
        var x = await SalesVendorWorld.CreateAsync(s => s.AddSingleton<ClientDocumentExpiryService>());
        foreach (var code in new[] { ClientDocumentCodes.IvuExemptionState, ClientDocumentCodes.IvuExemptionCity, ClientDocumentCodes.IvuExemptionTotal })
        {
            var l = await x.F.Db.LookupCodes.AsTracking().SingleAsync(r => r.Entity == ClientDocumentCodes.TypeDomain && r.InternalCode == code);
            l.ExtraJson = IvuMarks;
        }
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return x;
    }

    private static ClientDocumentExpiryService Svc(SalesVendorWorld x) => x.F.Get<ClientDocumentExpiryService>();

    private static Task<ClientDocumentDto> UploadAsync(SalesVendorWorld x, Guid client, DateOnly? validTo, string type = ClientDocumentCodes.IvuExemptionState, string? number = null)
        => x.Documents.UploadAsync(client, type, null, "c.png", "image/png", SalesVendorWorld.Png, default, number, null, validTo);

    private static async Task SetThresholdsAsync(SalesVendorWorld x, string csv)
    {
        var t = await x.F.Db.Tenants.AsTracking().SingleAsync(r => r.TenantId == WmsFixture.TenantId);
        t.DocExpiryThresholdDays = csv;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
    }

    private static async Task<ClientTaxExemption> LegacyExemptionAsync(SalesVendorWorld x, int clientId, DateOnly? validTo, string cert = "VIEJA-1")
    {
        var e = new ClientTaxExemption
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = clientId, ExemptionType = "Revendedor", CertificateNumber = cert, ValidFrom = Today.AddYears(-2),
            ValidTo = validTo, IsActive = true, CreatedAtUtc = DateTime.UtcNow,
        };
        x.F.Db.Set<ClientTaxExemption>().Add(e);
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        return e;
    }

    [Fact]
    public async Task Lists_expiring_expired_and_missing_without_renewed_or_inactive_clients_ordered_by_valid_to()
    {
        await using var x = await WorldAsync();
        var a = await x.ClientAsync("A1", c => c.Name = "Farmacia Alfa");
        var soon = await UploadAsync(x, a.PublicId, Today.AddDays(10), number: "EX-10");
        var far = await UploadAsync(x, a.PublicId, Today.AddDays(100), ClientDocumentCodes.IvuExemptionCity);
        var expired = await UploadAsync(x, a.PublicId, Today.AddDays(-5), ClientDocumentCodes.IvuExemptionTotal);
        var noExpiry = await UploadAsync(x, a.PublicId, null, ClientDocumentCodes.Other);   // no lleva vigencia: nunca sale
        var b = await x.ClientAsync("B1");
        var renewedOld = await UploadAsync(x, b.PublicId, Today.AddDays(3));
        await UploadAsync(x, b.PublicId, Today.AddDays(400));   // lo renueva (fuera de la ventana)
        // faltante: un documento de un tipo que después pasó a «lleva vigencia»
        var missing = await UploadAsync(x, b.PublicId, null, ClientDocumentCodes.MerchantRegistration);
        var m = await x.F.Db.LookupCodes.AsTracking().SingleAsync(r => r.Entity == ClientDocumentCodes.TypeDomain && r.InternalCode == ClientDocumentCodes.MerchantRegistration);
        m.ExtraJson = "{\"requiresValidTo\":true}";
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var gone = await x.ClientAsync("Z9");
        await UploadAsync(x, gone.PublicId, Today.AddDays(1));
        var row = await x.F.Db.Clients.AsTracking().SingleAsync(c => c.ClientId == gone.ClientId);
        row.IsActive = false;
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var list = await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(WithinDays: 30), default);
        Assert.Equal(new[] { expired.PublicId, soon.PublicId, missing.PublicId }, list.Items.Select(i => i.DocumentPublicId!.Value));
        Assert.DoesNotContain(list.Items, i => i.DocumentPublicId == far.PublicId || i.DocumentPublicId == renewedOld.PublicId || i.DocumentPublicId == noExpiry.PublicId);
        Assert.Equal((30, 30, false), (list.WarnDays, list.WithinDays, list.Truncated));
        var first = list.Items[1];
        Assert.Equal((ExpiringClientDocumentRowKinds.Document, "A1", "Farmacia Alfa", ClientDocumentCodes.IvuExemptionState, "EX-10", 10, ClientDocumentExpiryStates.Expiring, true),
            (first.RowKind, first.ClientCode, first.ClientName, first.DocumentType, first.DocumentNumber, first.DaysToExpiry, first.ExpiryState, first.IsTax));
        Assert.Equal(soon.FilePublicId, first.FilePublicId);
        Assert.Equal((ClientDocumentExpiryStates.Expired, ClientDocumentExpiryStates.MissingValidTo), (list.Items[0].ExpiryState, list.Items[2].ExpiryState));

        var onlyWindow = await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(WithinDays: 30, IncludeExpired: false, IncludeMissing: false), default);
        Assert.Equal(new[] { soon.PublicId }, onlyWindow.Items.Select(i => i.DocumentPublicId!.Value));
        var wide = await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(WithinDays: 365, IncludeExpired: false, IncludeMissing: false), default);
        Assert.Equal(new[] { soon.PublicId, far.PublicId }, wide.Items.Select(i => i.DocumentPublicId!.Value));
        var ofB = await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(WithinDays: 30, ClientPublicId: b.PublicId), default);
        Assert.Equal(new[] { missing.PublicId }, ofB.Items.Select(i => i.DocumentPublicId!.Value));
        await Assert.ThrowsAsync<NotFoundException>(() => Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(ClientPublicId: Guid.NewGuid()), default));
        var search = await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(WithinDays: 30, Q: "ex-10"), default);
        Assert.Equal(new[] { soon.PublicId }, search.Items.Select(i => i.DocumentPublicId!.Value));
    }

    [Fact]
    public async Task Without_within_days_the_window_is_the_company_warning_and_the_range_is_validated()
    {
        await using var x = await WorldAsync();
        var a = await x.ClientAsync("A1");
        var at8 = await UploadAsync(x, a.PublicId, Today.AddDays(8));
        var at12 = await UploadAsync(x, a.PublicId, Today.AddDays(12), ClientDocumentCodes.IvuExemptionCity);
        await SetThresholdsAsync(x, "10,2,0,-3");

        var byCompany = await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(), default);
        Assert.Equal((10, 10), (byCompany.WarnDays, byCompany.WithinDays));
        Assert.Equal(new[] { at8.PublicId }, byCompany.Items.Select(i => i.DocumentPublicId!.Value));
        var by20 = await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(WithinDays: 20), default);
        Assert.Equal(new[] { at8.PublicId, at12.PublicId }, by20.Items.Select(i => i.DocumentPublicId!.Value));
        Assert.Equal(ClientDocumentExpiryStates.Valid, by20.Items[1].ExpiryState);   // «por vencer» sigue siendo el aviso de la compañía

        foreach (var bad in new[] { 0, 366, -1 })
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(WithinDays: bad), default));
            Assert.Equal((ClientDocumentExpiryService.WithinDaysRange, true), (ex.Message, ex.Errors!.ContainsKey("withinDays")));
        }
    }

    [Fact]
    public async Task Exemptions_without_document_are_listed_only_with_tax_manage_and_the_tax_module()
    {
        await using var x = await WorldAsync();
        var a = await x.ClientAsync("A1");
        var legacy = await LegacyExemptionAsync(x, a.ClientId, null);
        var legacyExpired = await LegacyExemptionAsync(x, a.ClientId, Today.AddDays(-30), "VIEJA-2");

        x.F.SetPermissions(PermissionCatalog.ClientsRead, PermissionCatalog.TaxManage);
        Assert.Empty((await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(), default)).Items);   // TAX apagado
        x.F.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Catalog, ModuleKeys.Billing, ModuleKeys.Tax);
        var list = await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(), default);
        Assert.Equal(new[] { legacyExpired.PublicId, legacy.PublicId }, list.Items.Select(i => i.ExemptionPublicId!.Value));
        var row = list.Items[1];
        Assert.Equal((ExpiringClientDocumentRowKinds.ExemptionWithoutDocument, (Guid?)null, "Revendedor", "VIEJA-1", ClientDocumentExpiryStates.NoExpiry, a.PublicId),
            (row.RowKind, row.DocumentPublicId, row.DocumentTypeLabel, row.DocumentNumber, row.ExpiryState, row.ClientPublicId));
        Assert.Equal(new[] { legacy.PublicId },
            (await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(IncludeExpired: false), default)).Items.Select(i => i.ExemptionPublicId!.Value));

        x.F.SetPermissions(PermissionCatalog.ClientsRead);   // sin tax.manage
        Assert.Empty((await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(), default)).Items);
    }

    [Fact]
    public async Task The_list_stops_at_2000_rows_and_says_so()
    {
        await using var x = await WorldAsync();
        var typeId = x.Lookup(ClientDocumentCodes.TypeDomain, ClientDocumentCodes.IvuExemptionState);
        var ownerLookup = x.Lookup(LookupDomains.EntityType, ClientDocumentCodes.EntityType);
        var activeStatus = x.F.StatusId(StatusDomains.ClientStatus, ClientStatuses.Active);
        for (var i = 0; i < ClientDocumentExpiryService.MaxRows + 1; i++)
        {
            // cada documento en su propio cliente: del mismo cliente y tipo, el de «hasta» posterior renovaría a los demás
            var client = new Client
            {
                ClientId = 500000 + i, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, Code = "T" + i, Name = "Cliente T" + i, StatusCodeId = activeStatus,
                IsActive = true, CreatedAtUtc = DateTime.UtcNow,
            };
            var file = new StoredFile
            {
                StoredFileId = 900000 + i, PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, OwnerEntityLookupId = ownerLookup, OwnerId = client.ClientId, FileName = "c.pdf",
                ContentType = "application/pdf", SizeBytes = 10, Sha256 = "x", StorageKey = "k" + i, IsActive = true,
            };
            x.F.Db.Clients.Add(client);
            x.F.Db.Set<StoredFile>().Add(file);
            x.F.Db.Set<ClientDocument>().Add(new ClientDocument
            {
                PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = client.ClientId, StoredFileId = file.StoredFileId, DocumentTypeLookupId = typeId,
                ValidTo = Today.AddDays(-i), IsActive = true, CreatedAtUtc = DateTime.UtcNow,
            });
        }
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var list = await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(WithinDays: 1), default);
        Assert.True(list.Truncated);
        Assert.Equal(ClientDocumentExpiryService.MaxRows, list.Items.Count);
        Assert.Equal(Today.AddDays(-ClientDocumentExpiryService.MaxRows), list.Items[0].ValidTo);   // el más viejo primero
    }

    [Fact]
    public async Task Another_company_does_not_see_the_documents()
    {
        await using var x = await WorldAsync();
        var a = await x.ClientAsync("A1");
        await UploadAsync(x, a.PublicId, Today.AddDays(5));
        Assert.Single((await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(), default)).Items);
        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Empty((await Svc(x).ListExpiringAsync(new ExpiringClientDocumentsQuery(), default)).Items);
    }
}
