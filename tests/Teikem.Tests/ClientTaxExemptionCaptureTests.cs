using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Tax;
using Xunit;

namespace Teikem.Tests;

// Lote 248: la captura única de la exención desde la ficha del cliente — archivo nuevo (documento + exención) o documento ya subido, nunca los dos; TODO se
// valida antes de tocar el almacén de archivos (un doble cuenta las veces que se guarda contenido).
public sealed class ClientTaxExemptionCaptureTests
{
    /// <summary>El almacén real, contando cuántas veces se guardó contenido.</summary>
    private sealed class CountingFileStore(IFileStore inner) : IFileStore
    {
        public int Saves;
        public bool IsConfigured => inner.IsConfigured;
        public Task<string> SaveAsync(int tenantId, Guid publicId, ReadOnlyMemory<byte> content, CancellationToken ct) { Interlocked.Increment(ref Saves); return inner.SaveAsync(tenantId, publicId, content, ct); }
        public Task<byte[]> ReadAsync(int tenantId, string storageKey, CancellationToken ct) => inner.ReadAsync(tenantId, storageKey, ct);
        public Task DeleteAsync(int tenantId, string storageKey, CancellationToken ct) => inner.DeleteAsync(tenantId, storageKey, ct);
    }

    private sealed class World : IAsyncDisposable
    {
        public SalesVendorWorld X { get; private init; } = null!;
        public CountingFileStore Store { get; private init; } = null!;
        public ClientTaxExemptionCaptureService Svc { get; private init; } = null!;
        public TaxService Tax => X.F.Get<TaxService>();
        public static DateOnly Today => TenantClock.Default.Today;

        public static async Task<World> CreateAsync()
        {
            var x = await SalesVendorWorld.CreateAsync();
            x.F.SetModules(ModuleKeys.SalesOrders, ModuleKeys.Catalog, ModuleKeys.Tax);
            await TaxTestData.EnsureDocumentTypeAsync(x.F.Db, ClientDocumentCodes.IvuExemptionState);
            await TaxTestData.EnsureDocumentTypeAsync(x.F.Db, ClientDocumentCodes.IvuExemptionTotal);
            await TaxTestData.EnsureDocumentTypeAsync(x.F.Db, ClientDocumentCodes.Other, isTax: false, requiresValidTo: false);
            var store = new CountingFileStore(x.F.Get<IFileStore>());
            var files = new StoredFileService(x.F.Db, x.F.Tenant, x.F.Lookups, store, x.F.Get<IOptions<FileStoreOptions>>(), x.F.Get<PermissionService>(),
                x.F.Services.GetServices<IOwnedEntityResolver>());
            var w = new World { X = x, Store = store, Svc = new ClientTaxExemptionCaptureService(x.F.Db, x.F.Tenant, TenantClock.Default, x.F.Lookups, x.F.Get<TaxService>(), files) };
            await w.Tax.CreateRateAsync(new TaxRateRequest("IVU", "IVU", new DateOnly(2020, 1, 1), null,
                new[] { new TaxComponentDto("STATE", "Estatal", 10.5m), new TaxComponentDto("CITY", "Municipal", 1m) }), default);
            await w.Tax.UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "IVU"), default);
            return w;
        }

        public static ClientTaxExemptionCaptureRequest NewFile(string type = ClientDocumentCodes.IvuExemptionState, string? number = "EST-2026-01", DateOnly? from = null,
            DateOnly? to = null, string? scope = "COMPONENTS", params string[] codes)
            => new(null, type, number, from ?? Today.AddDays(-10), to ?? Today.AddDays(200), scope, codes.Length == 0 ? new[] { "STATE" } : codes);

        public ValueTask DisposeAsync() => X.DisposeAsync();
    }

    [Fact]
    public async Task With_a_new_file_the_document_and_the_exemption_are_created_together_and_the_sale_follows_the_certificate()
    {
        await using var w = await World.CreateAsync();
        var c = await w.X.ClientAsync("C1");
        var r = await w.Svc.CaptureAsync(c.PublicId, World.NewFile(), "cert.png", "image/png", SalesVendorWorld.Png, default);

        Assert.Equal(1, w.Store.Saves);
        var doc = await w.X.F.Db.Set<ClientDocument>().AsNoTracking().SingleAsync();
        Assert.Equal((r.DocumentPublicId, "EST-2026-01", World.Today.AddDays(200), true), (doc.PublicId, doc.DocumentNumber, doc.ValidTo, doc.IsActive));
        var file = await w.X.F.Db.Set<StoredFile>().AsNoTracking().SingleAsync();
        Assert.Equal((r.FilePublicId, StoredFileCodes.KindDocument), (file.PublicId, file.Kind));
        var ex = r.Exemption;
        Assert.Equal(("VALID", "EST-2026-01", World.Today.AddDays(200), false), (ex.Status, ex.CertificateNumber, ex.ValidTo, ex.MissingDocument));
        Assert.Equal((r.DocumentPublicId, r.FilePublicId, ClientDocumentCodes.IvuExemptionState), (ex.DocumentPublicId, ex.FilePublicId, ex.DocumentType));
        Assert.Equal(("COMPONENTS", "STATE"), (ex.Scope, ex.ComponentCodes!.Single()));
        Assert.False(string.IsNullOrWhiteSpace(ex.ExemptionType));   // la etiqueta del tipo de documento
        Assert.Equal(ex.Id, Assert.Single(await w.Svc.ListForClientAsync(c.PublicId, default)).Id);

        // la venta: exenta la parte estatal, se cobra el 1 % municipal
        var calc = await w.Tax.CalculateAsync(new TaxCalcRequest(c.ClientId, null, new[] { new TaxCalcLineRequest("a", null, "PRODUCT_SALE", 100m) }), default);
        Assert.Equal((1m, TaxReasons.ExemptClientPartial, "EST-2026-01"), (calc.TaxAmount, calc.Lines[0].Reason, calc.Lines[0].ExemptionCertificate));
    }

    [Fact]
    public async Task With_an_existing_document_only_the_exemption_is_created_and_the_same_document_cannot_back_two()
    {
        await using var w = await World.CreateAsync();
        var c = await w.X.ClientAsync("C1");
        var doc = await TaxTestData.SeedTaxDocumentAsync(w.X.F.Db, WmsFixture.TenantId, c.ClientId, "TOT-9", World.Today.AddDays(-5), World.Today.AddDays(90));
        var r = await w.Svc.CaptureAsync(c.PublicId, new ClientTaxExemptionCaptureRequest(doc.PublicId, Scope: "ALL"), null, null, null, default);
        Assert.Equal(0, w.Store.Saves);
        Assert.Equal((doc.PublicId, "TOT-9", "ALL"), (r.DocumentPublicId, r.Exemption.CertificateNumber, r.Exemption.Scope));
        Assert.Single(await w.X.F.Db.Set<ClientDocument>().AsNoTracking().ToListAsync());
        var again = await Assert.ThrowsAsync<ConflictException>(() => w.Svc.CaptureAsync(c.PublicId, new ClientTaxExemptionCaptureRequest(doc.PublicId), null, null, null, default));
        Assert.Equal(ClientExemptionRules.DocumentAlreadyBacks, again.Message);
        // el documento de OTRO cliente no existe para éste
        var other = await w.X.ClientAsync("C2");
        Assert.Equal("Documento no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() =>
            w.Svc.CaptureAsync(other.PublicId, new ClientTaxExemptionCaptureRequest(doc.PublicId), null, null, null, default))).Message);
    }

    [Fact]
    public async Task File_and_document_together_or_neither_are_rejected_and_everything_is_validated_before_storing_the_file()
    {
        await using var w = await World.CreateAsync();
        var c = await w.X.ClientAsync("C1");
        var doc = await TaxTestData.SeedTaxDocumentAsync(w.X.F.Db, WmsFixture.TenantId, c.ClientId, "TOT-9", null, World.Today.AddDays(90));
        async Task<string> Bad(ClientTaxExemptionCaptureRequest req, byte[]? content = null)
            => (await Assert.ThrowsAsync<ValidationException>(() => w.Svc.CaptureAsync(c.PublicId, req, "cert.png", "image/png", content ?? SalesVendorWorld.Png, default))).Message;

        Assert.Equal(ClientExemptionRules.FileAndDocument, await Bad(World.NewFile() with { ClientDocumentPublicId = doc.PublicId }));
        Assert.Equal(ClientExemptionRules.DocumentRequired,
            (await Assert.ThrowsAsync<ValidationException>(() => w.Svc.CaptureAsync(c.PublicId, World.NewFile(), null, null, null, default))).Message);
        Assert.Equal(ClientDocumentRules.TypeInvalid, await Bad(World.NewFile(type: "LICENCIA")));
        Assert.Equal(ClientExemptionRules.DocumentNotTax, await Bad(World.NewFile(type: ClientDocumentCodes.Other)));
        Assert.Equal(TaxRules.CertificateRequired, await Bad(World.NewFile(number: " ")));
        Assert.Equal(ClientDocumentRules.NumberTooLong, await Bad(World.NewFile(number: new string('9', 61))));
        Assert.Equal(ClientExemptionRules.ValidFromRequired, await Bad(World.NewFile() with { ValidFrom = null }));
        Assert.Equal(ClientExemptionRules.ValidToRequired, await Bad(World.NewFile() with { ValidTo = null }));
        Assert.Equal(ClientDocumentRules.DatesOrder, await Bad(World.NewFile(from: World.Today.AddDays(10), to: World.Today.AddDays(5))));
        Assert.Equal(ClientExemptionRules.ValidToPast, await Bad(World.NewFile(from: World.Today.AddDays(-30), to: World.Today.AddDays(-1))));
        Assert.Equal("Ninguna tasa de la compañía tiene el componente «COUNTY».", await Bad(World.NewFile(codes: "COUNTY")));
        Assert.Equal(ClientExemptionRules.FileRequired, await Bad(World.NewFile(), Array.Empty<byte>()));

        Assert.Equal(0, w.Store.Saves);   // nada llegó al almacén
        Assert.Single(await w.X.F.Db.Set<ClientDocument>().AsNoTracking().ToListAsync());   // solo el sembrado
        Assert.Empty(await w.X.F.Db.Set<ClientTaxExemption>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task A_client_of_another_company_is_not_found()
    {
        await using var w = await World.CreateAsync();
        var foreign = await w.X.F.AddClientAsync("AJENO", WmsFixture.OtherTenantId);
        Assert.Equal("Cliente no encontrado.", (await Assert.ThrowsAsync<NotFoundException>(() =>
            w.Svc.CaptureAsync(foreign.PublicId, World.NewFile(), "cert.png", "image/png", SalesVendorWorld.Png, default))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => w.Svc.ListForClientAsync(foreign.PublicId, default));
        Assert.Equal(0, w.Store.Saves);
    }
}
