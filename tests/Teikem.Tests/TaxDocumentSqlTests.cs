using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Tax;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 — exención atada a su documento y exención por temporada contra SQL Server REAL (se omite sin TEIKEM_TEST_SQLSERVER), sobre una base desechable creada
/// desde el modelo EF (<see cref="MessagingSqlFixture"/>, sin llaves foráneas: las FK compuestas entre compañías y clientes viven en la sección del lote de update.sql y
/// las prueba Lote248SqlTests). Aquí: los índices únicos filtrados (un documento respalda a lo más una exención activa; un destino activo por período), la carrera de
/// dos vinculaciones a la vez (gana una, la otra recibe 409), la traducción a SQL del cálculo con documentos y períodos, y que la captura única con archivo es UNA
/// transacción (si falla la exención no queda ni documento ni archivo).
/// </summary>
public sealed class TaxDocumentSqlTests(MessagingSqlFixture fx) : IClassFixture<MessagingSqlFixture>, IAsyncLifetime
{
    private int A;
    private static DateOnly Today => TenantClock.Default.Today;
    private static readonly string Key = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("llave-de-prueba-de-32-bytes-0248"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "teikem-l248-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        if (fx.ConnectionString is null) return;
        A = await fx.NewTenantAsync("Compañía L248");
        await using var db = fx.NewDb(new TenantContext());
        if (!await db.Set<LookupCode>().AnyAsync(l => l.Entity == LookupDomains.EntityType && l.InternalCode == EntityTypes.Client))
        {
            db.Set<LookupCode>().Add(new LookupCode { Entity = LookupDomains.EntityType, InternalCode = EntityTypes.Client, LabelJson = "{\"es\":\"Cliente\"}", IsActive = true });
            db.Set<LookupCode>().Add(new LookupCode { Entity = "ChargeType", InternalCode = "PRODUCT_SALE", LabelJson = "{\"es\":\"Venta\"}", IsActive = true });
            await db.SaveChangesAsync();
        }
        await TaxTestData.EnsureDocumentTypeAsync(db);
    }

    public Task DisposeAsync()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    private TenantContext Ctx => new() { TenantId = A, UserId = 1, IsAuthenticated = true, IsPlatformAdmin = true };

    private TaxService Tax(TeikemDbContext db) => new(db, db.TenantOf(), TenantClock.Default, new DbLookups(fx));

    private async Task<int> ClientAsync()
    {
        await using var db = fx.NewDb(Ctx);
        var c = new Client { PublicId = Guid.NewGuid(), TenantId = A, Code = "C" + Guid.NewGuid().ToString("N")[..8], Name = "Farmacia", StatusCodeId = 1, IsActive = true, CreatedAtUtc = DateTime.UtcNow };
        db.Set<Client>().Add(c);
        await db.SaveChangesAsync();
        return c.ClientId;
    }

    private async Task<ClientDocument> DocAsync(int clientId, string number = "CERT-1")
    {
        await using var db = fx.NewDb(Ctx);
        return await TaxTestData.SeedTaxDocumentAsync(db, A, clientId, number, Today.AddDays(-10), Today.AddDays(100));
    }

    private static bool IsUnique(Exception ex) => ex is DbUpdateException d && DbExtensions.IsUniqueViolation(d);

    [SqlServerFact]
    public async Task A_document_backs_at_most_one_active_exemption_but_an_inactive_one_does_not_count()
    {
        var client = await ClientAsync();
        var doc = await DocAsync(client);
        ClientTaxExemption Row(bool active) => new()
        {
            PublicId = Guid.NewGuid(), TenantId = A, ClientId = client, ExemptionType = "Total", CertificateNumber = "CERT-1", ValidFrom = Today, ValidTo = Today.AddDays(100),
            IsActive = active, ClientDocumentId = doc.ClientDocumentId, CreatedAtUtc = DateTime.UtcNow,
        };
        await using (var db = fx.NewDb(Ctx))
        {
            db.Set<ClientTaxExemption>().AddRange(Row(false), Row(true));   // una inactiva y una activa con el mismo documento: se admite
            await db.SaveChangesAsync();
        }
        await using (var db = fx.NewDb(Ctx))
        {
            db.Set<ClientTaxExemption>().Add(Row(true));
            Assert.True(IsUnique(await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync())));   // UX_ClientTaxExemption_Document (2601)
        }
    }

    [SqlServerFact]
    public async Task Two_links_of_the_same_document_at_the_same_time_exactly_one_wins_and_the_other_gets_409()
    {
        var client = await ClientAsync();
        var doc = await DocAsync(client);
        async Task<Exception?> Link()
        {
            await using var db = fx.NewDb(Ctx);
            try
            {
                await Tax(db).CreateExemptionAsync(new TaxExemptionRequest(client, null, ClientDocumentPublicId: doc.PublicId), default);
                return null;
            }
            catch (Exception ex) { return ex; }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(Link)));
        Assert.Single(results, r => r is null);
        Assert.All(results.Where(r => r is not null), r => Assert.Equal(ClientExemptionRules.DocumentAlreadyBacks, Assert.IsType<ConflictException>(r).Message));
        await using var check = fx.NewDb(Ctx);
        Assert.Single(await check.Set<ClientTaxExemption>().AsNoTracking().Where(e => e.ClientDocumentId == doc.ClientDocumentId && e.IsActive).ToListAsync());
    }

    [SqlServerFact]
    public async Task The_calculation_reads_the_document_and_the_periods_from_sql_server()
    {
        await using (var db = fx.NewDb(Ctx))
        {
            var tax = Tax(db);
            if (!await db.Set<TaxRate>().AnyAsync())
                await tax.CreateRateAsync(new TaxRateRequest("IVU", "IVU", new DateOnly(2020, 1, 1), null,
                    new[] { new TaxComponentDto("STATE", "Estatal", 10.5m), new TaxComponentDto("CITY", "Municipal", 1m) }), default);
            await tax.UpdateSettingsAsync(new TaxSettingsRequest(DefaultRateCode: "IVU"), default);
        }
        var client = await ClientAsync();
        var doc = await DocAsync(client, "MUN-77");
        await using (var db = fx.NewDb(Ctx))
            await Tax(db).CreateExemptionAsync(new TaxExemptionRequest(client, null, Scope: "COMPONENTS", ComponentCodes: new[] { "CITY" }, ClientDocumentPublicId: doc.PublicId), default);

        int productId, categoryId, childId;
        await using (var db = fx.NewDb(Ctx))
        {
            var root = new ProductCategory { TenantId = A, Name = "Escolar" };
            db.Set<ProductCategory>().Add(root);
            await db.SaveChangesAsync();
            var child = new ProductCategory { TenantId = A, Name = "Libretas", ParentId = root.ProductCategoryId };
            db.Set<ProductCategory>().Add(child);
            await db.SaveChangesAsync();
            var p = new Product { TenantId = A, Sku = "SKU" + Guid.NewGuid().ToString("N")[..8], Name = "Libreta", BaseUomLookupId = 1, TrackingTypeLookupId = 1, ProductCategoryId = child.ProductCategoryId };
            db.Set<Product>().Add(p);
            await db.SaveChangesAsync();
            (productId, categoryId, childId) = (p.ProductId, root.ProductCategoryId, child.ProductCategoryId);
        }
        var day = Today.AddDays(3);
        await using (var db = fx.NewDb(Ctx))
            await new TaxExemptionPeriodService(db, db.TenantOf(), TenantClock.Default)
                .CreateAsync(new TaxExemptionPeriodRequest("Escolar", "OA", day, day, "SELECTED", null, new[] { categoryId }), default);

        await using (var db = fx.NewDb(Ctx))
        {
            var r = await Tax(db).CalculateAsync(new TaxCalcRequest(client, day, new[] { new TaxCalcLineRequest("lib", productId, null, 100m), new TaxCalcLineRequest("envio", null, "PRODUCT_SALE", 10m) }), default);
            Assert.Equal((TaxReasons.ExemptProductPeriod, "Escolar"), (r.Lines[0].Reason, r.Lines[0].ExemptionPeriod));
            Assert.Equal((TaxReasons.ExemptClientPartial, "MUN-77", 1.05m), (r.Lines[1].Reason, r.Lines[1].ExemptionCertificate, r.Lines[1].TaxAmount));
            var list = Assert.Single(await Tax(db).ListExemptionsAsync(client, default));
            Assert.Equal((doc.PublicId, "VALID", false), (list.DocumentPublicId, list.Status, list.MissingDocument));
        }
        Assert.True(childId > 0);
    }

    [SqlServerFact]
    public async Task A_period_has_one_active_row_per_target_and_inactive_rows_may_repeat()
    {
        int productId;
        await using (var db = fx.NewDb(Ctx))
        {
            var p = new Product { TenantId = A, Sku = "SKU" + Guid.NewGuid().ToString("N")[..8], Name = "Lápiz", BaseUomLookupId = 1, TrackingTypeLookupId = 1 };
            db.Set<Product>().Add(p);
            await db.SaveChangesAsync();
            productId = p.ProductId;
        }
        TaxExemptionPeriodDto period;
        await using (var db = fx.NewDb(Ctx))
        {
            var publicId = await db.Set<Product>().Where(x => x.ProductId == productId).Select(x => x.PublicId).SingleAsync();
            period = await new TaxExemptionPeriodService(db, db.TenantOf(), TenantClock.Default)
                .CreateAsync(new TaxExemptionPeriodRequest("Temporada", "OA", Today.AddDays(5), Today.AddDays(6), "SELECTED", new[] { publicId }), default);
            Assert.False(string.IsNullOrEmpty(period.RowVersion));   // SQL Server genera el RowVersion
        }
        await using (var db = fx.NewDb(Ctx))
        {
            var id = await db.Set<TaxExemptionPeriod>().Where(x => x.PublicId == period.PublicId).Select(x => x.TaxExemptionPeriodId).SingleAsync();
            db.Set<TaxExemptionPeriodProduct>().Add(new TaxExemptionPeriodProduct { TenantId = A, TaxExemptionPeriodId = id, ProductId = productId, IsActive = false });
            db.Set<TaxExemptionPeriodProduct>().Add(new TaxExemptionPeriodProduct { TenantId = A, TaxExemptionPeriodId = id, ProductId = productId, IsActive = false });
            await db.SaveChangesAsync();   // inactivas repetidas: se admiten
            db.Set<TaxExemptionPeriodProduct>().Add(new TaxExemptionPeriodProduct { TenantId = A, TaxExemptionPeriodId = id, ProductId = productId, IsActive = true });
            Assert.True(IsUnique(await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync())));   // UX_TaxExemptionPeriodProduct (2601)
        }
        // un rowVersion viejo → 409
        await using (var db = fx.NewDb(Ctx))
            await new TaxExemptionPeriodService(db, db.TenantOf(), TenantClock.Default).UpdateAsync(period.PublicId, new TaxExemptionPeriodPatchRequest(Name: "Temporada 2"), default);
        await using (var db = fx.NewDb(Ctx))
            await Assert.ThrowsAsync<ConflictException>(() => new TaxExemptionPeriodService(db, db.TenantOf(), TenantClock.Default)
                .UpdateAsync(period.PublicId, new TaxExemptionPeriodPatchRequest(Name: "Temporada 3", RowVersion: period.RowVersion), default));
    }

    /// <summary>Hace fallar el guardado de la exención (después de guardado el documento) para probar que la captura única revierte todo.</summary>
    private sealed class FailOnExemptionInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ClientTaxExemption>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("falla simulada al guardar la exención");
            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    [SqlServerFact]
    public async Task The_capture_with_a_new_file_is_one_transaction_nothing_stays_if_the_exemption_fails()
    {
        var client = await ClientAsync();
        var ctx = Ctx;
        await using var db = new TeikemDbContext(new DbContextOptionsBuilder<TeikemDbContext>().UseSqlServer(fx.ConnectionString)
            .AddInterceptors(new FailOnExemptionInsert()).Options, ctx);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var lookups = new DbLookups(fx);
        var opts = Options.Create(new FileStoreOptions { Root = _root, MasterKey = Key, MaxMegabytes = 10 });
        var files = new StoredFileService(db, ctx, lookups, new EncryptedDiskFileStore(opts), opts,
            new PermissionService(db, ctx, cache, lookups, new ReceivingNullSecurityEventWriter()), new IOwnedEntityResolver[] { new ClientOwnedEntityResolver(db) });
        var capture = new ClientTaxExemptionCaptureService(db, ctx, TenantClock.Default, lookups, new TaxService(db, ctx, TenantClock.Default, lookups), files);
        var publicId = await db.Set<Client>().Where(c => c.ClientId == client).Select(c => c.PublicId).SingleAsync();

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => capture.CaptureAsync(publicId,
            new ClientTaxExemptionCaptureRequest(null, TaxTestData.TaxDocumentType, "TOT-1", Today.AddDays(-1), Today.AddDays(200), "ALL"), "c.png", "image/png", png, default));

        await using var check = fx.NewDb(Ctx);
        Assert.Empty(await check.Set<ClientDocument>().AsNoTracking().Where(d => d.ClientId == client).ToListAsync());
        Assert.Empty(await check.Set<StoredFile>().AsNoTracking().Where(f => f.OwnerId == client).ToListAsync());
        Assert.Empty(await check.Set<ClientTaxExemption>().AsNoTracking().Where(e => e.ClientId == client).ToListAsync());
    }
}
