using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Contacts;
using Teikem.Domain.Files;
using Teikem.Domain.Identity;
using Teikem.Domain.Notifications;
using Teikem.Domain.Sales;
using Teikem.Domain.Security;
using Teikem.Domain.Tax;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Notifications;
using Teikem.Infrastructure.Orders;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Teikem.Infrastructure.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 243 (Lote G del plan de Solutions) — de punta a punta contra SQL Server REAL (el procesador reclama la cola con ExecuteUpdate, que InMemory no
/// soporta): se siembran el cliente con sus contactos, la tasa de IVU con sus componentes, la orden solicitada con un kit y la firma; se publica el aviso
/// «Orden de venta solicitada» como lo hace Solicitar y se corre NotificationProcessor.TryProcessAsync con el SalesOrderEmailContent REAL (su propio
/// scope, SalesOrderService real) y FakeEmailSender (nunca correo real). Comprueba los registros SENT y SKIPPED con su motivo, el cuerpo con la tabla y el
/// IVU por componente, la firma adjunta, la copia al cliente sin enlace ni notas y que el DbContext del procesador solo rastree NotificationLog.
/// Base desechable de NotificationSqlFixture; se omite sin TEIKEM_TEST_SQLSERVER.
/// </summary>
public sealed class SalesOrderEmailSqlTests(NotificationSqlFixture fx) : IClassFixture<NotificationSqlFixture>
{
    private static readonly SemaphoreSlim SeedGate = new(1, 1);
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 8, 7 };
    private static readonly NotificationEventCatalog Catalog = new(new INotificationEventProvider[] { new SalesEventProvider() });
    private static int _seed = 7000;
    private static int NextId() => Interlocked.Increment(ref _seed);

    // ---------------------------------------------------------------- catálogos (la base de pruebas no tiene el seed)

    private async Task SeedCatalogsAsync()
    {
        await SeedGate.WaitAsync();
        try
        {
            await using var db = fx.NewDb(new TenantContext());
            var wanted = new (string Entity, string Code)[]
            {
                (LookupDomains.StageKind, StageKinds.Pipeline), (LookupDomains.StageKind, StageKinds.Terminal),
                (LookupDomains.EntityType, EntityTypes.Client), (LookupDomains.EntityType, EntityTypes.ClientContact),
                (LookupDomains.ContactType, "EMAIL"), (LookupDomains.ContactType, "PHONE"),
                (LookupDomains.LocationType, LocationTypes.Corporate), (LookupDomains.LocationType, LocationTypes.Billing),
                (LookupDomains.UnitOfMeasure, "UN"), (LookupDomains.TrackingType, TrackingTypes.None),
            };
            foreach (var (entity, code) in wanted)
                if (!await db.Set<LookupCode>().AnyAsync(l => l.Entity == entity && l.InternalCode == code))
                    db.Set<LookupCode>().Add(new LookupCode { Entity = entity, InternalCode = code, LabelJson = $"{{\"es\":\"{code}\"}}", IsActive = true, IsSystem = true });
            await db.SaveChangesAsync();
            var pipe = await db.Set<LookupCode>().Where(l => l.Entity == LookupDomains.StageKind && l.InternalCode == StageKinds.Pipeline).Select(l => l.LookupCodeId).SingleAsync();
            var term = await db.Set<LookupCode>().Where(l => l.Entity == LookupDomains.StageKind && l.InternalCode == StageKinds.Terminal).Select(l => l.LookupCodeId).SingleAsync();
            foreach (var (code, label, stage, sort) in new[]
                     {
                         (SalesOrderStatuses.Draft, "Borrador", pipe, 1), (SalesOrderStatuses.Requested, "Solicitada", pipe, 2), (SalesOrderStatuses.Cancelled, "Cancelada", term, 9),
                     })
                if (!await db.StatusCodes.AnyAsync(s => s.Entity == SalesCodes.StatusDomain && s.InternalCode == code))
                    db.StatusCodes.Add(new StatusCode { Entity = SalesCodes.StatusDomain, InternalCode = code, LabelJson = $"{{\"es\":\"{label}\"}}", StageKindLookupId = stage, SortOrder = sort, IsActive = true });
            await db.SaveChangesAsync();
        }
        finally { SeedGate.Release(); }
    }

    private sealed class SqlLookups(NotificationSqlFixture fx) : ILookupCache
    {
        public async Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default) => (await TryGetIdAsync(entity, code, ct)) ?? throw new InvalidOperationException($"{entity}/{code}");
        public async Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default)
        {
            await using var db = fx.NewDb(new TenantContext());
            return await db.Set<LookupCode>().AsNoTracking().Where(l => l.Entity == entity && l.InternalCode == code).Select(l => (int?)l.LookupCodeId).FirstOrDefaultAsync(ct);
        }
        public async Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default)
        {
            await using var db = fx.NewDb(new TenantContext());
            return await db.Set<LookupCode>().AsNoTracking().FirstOrDefaultAsync(l => l.LookupCodeId == lookupCodeId, ct);
        }
        public async Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
        {
            await using var db = fx.NewDb(new TenantContext());
            return await db.Set<LookupCode>().AsNoTracking().Where(l => l.Entity == entity).ToListAsync(ct);
        }
        public void Invalidate() { }
    }

    /// <summary>Almacén de archivos falso: devuelve la firma (PNG) por su llave.</summary>
    private sealed class SignatureStore : IFileStore
    {
        public bool IsConfigured => true;
        public Task<string> SaveAsync(int tenantId, Guid publicId, ReadOnlyMemory<byte> content, CancellationToken ct) => Task.FromResult("k");
        public Task<byte[]> ReadAsync(int tenantId, string storageKey, CancellationToken ct) => Task.FromResult(storageKey == "firma-key" ? Png : Array.Empty<byte>());
        public Task DeleteAsync(int tenantId, string storageKey, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Envoltura del proveedor REAL: delega cada llamada y, al volver, anota qué tipos rastrea el DbContext del procesador (mismo scope). Así se
    /// comprueba que nada de lo que lee el proveedor (SalesOrderService.GetAsync rastrea) llega al SaveChanges del procesador.
    /// </summary>
    private sealed class TrackedProvider(SalesOrderEmailContent inner, TeikemDbContext callerDb, List<Type> tracked) : INotificationContentProvider
    {
        public IReadOnlyCollection<string> EventCodes => inner.EventCodes;
        public string? ReferencePermission => inner.ReferencePermission;
        public string? ReferenceModule => inner.ReferenceModule;
        private void Snapshot() { lock (tracked) tracked.AddRange(callerDb.ChangeTracker.Entries().Select(e => e.Entity.GetType())); }

        public async Task<IReadOnlyDictionary<string, string>> ValuesForReferenceAsync(int tenantId, string eventCode, string reference, CancellationToken ct)
            => await inner.ValuesForReferenceAsync(tenantId, eventCode, reference, ct);

        public async Task<NotificationContent?> BuildAsync(NotificationContentRequest req, CancellationToken ct)
        {
            var r = await inner.BuildAsync(req, ct);
            Snapshot();
            return r;
        }

        public async Task<NotificationContextResolution> RecipientsAsync(int tenantId, string eventCode, string contextKey, IReadOnlyDictionary<string, string> values, CancellationToken ct)
        {
            var r = await inner.RecipientsAsync(tenantId, eventCode, contextKey, values, ct);
            Snapshot();
            return r;
        }

        public NotificationContent Sample(string eventCode, NotificationAudience audience, string lang) => inner.Sample(eventCode, audience, lang);
    }

    /// <summary>El procesador con el contenido real y todo lo que SalesOrderService necesita, por scope (como en el API).</summary>
    private NotificationProcessor Processor(FakeEmailSender email, List<Type> tracked)
        => fx.NewProcessor(email, Catalog, s =>
        {
            Services(s);
            s.AddSingleton<SalesOrderEmailContent>();
            s.AddScoped<INotificationContentProvider>(sp => new TrackedProvider(sp.GetRequiredService<SalesOrderEmailContent>(), sp.GetRequiredService<TeikemDbContext>(), tracked));
        });

    /// <summary>
    /// El proveedor REAL llamado directo, como lo usan la vista previa y «Enviar prueba»: con el contexto de quien llama (<paramref name="caller"/>,
    /// p. ej. un usuario con alcance de datos de cliente).
    /// </summary>
    private SalesOrderEmailContent DirectProvider(TenantContext caller)
    {
        var s = new ServiceCollection();
        s.AddScoped<TenantContext>();
        s.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        s.AddScoped(sp => fx.NewDb(sp.GetRequiredService<ITenantContext>()));
        Services(s);
        var sp = s.BuildServiceProvider();
        return new SalesOrderEmailContent(sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<IConfiguration>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SalesOrderEmailContent>.Instance, caller);
    }

    /// <summary>Todo lo que SalesOrderService y el contenido necesitan, por scope (como en el API).</summary>
    private void Services(IServiceCollection s)
    {
        s.AddLogging();
        s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:WebBaseUrl"] = "https://app.teikem.test/" }).Build());
        s.AddSingleton<ILookupCache>(new SqlLookups(fx));
        s.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        s.AddSingleton<ISecurityEventWriter, ReceivingNullSecurityEventWriter>();
        s.AddSingleton<ITenantClock>(TenantClock.Default);
        s.AddSingleton<IClientBalanceProvider, FakeBalance>();
        s.AddSingleton<IFileStore, SignatureStore>();
        s.AddScoped<PermissionService>();
        s.AddScoped<ModuleService>();
        s.AddScoped(sp => new StatusService(sp.GetRequiredService<TeikemDbContext>(), sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<ILookupCache>(),
            Array.Empty<IStatusTransitionEffect>(), sp.GetRequiredService<PermissionService>()));
        s.AddScoped<INumberSequenceService, NumberSequenceService>();
        s.AddScoped<InventoryChangeSink>();
        s.AddScoped<IInventoryChangeSink>(sp => sp.GetRequiredService<InventoryChangeSink>());
        s.AddScoped<InventoryLedger>();
        s.AddScoped<TaxService>();
        s.AddScoped<SalesOrderService>();
    }

    // ---------------------------------------------------------------- siembra

    private sealed record World(int TenantId, int ClientId, string ClientName, int WarehouseId, int SellerId, string SellerEmail, int RateId, Product Normal, Product Kit, Product CompA, Product CompB, Product CompOrig);

    private async Task<World> WorldAsync(string company)
    {
        await SeedCatalogsAsync();
        var tenantId = await fx.NewTenantAsync(company);
        var lookups = new SqlLookups(fx);
        var n = NextId();
        await using var db = fx.NewDb(tenantId);
        var sellerEmail = $"vendedor{n}@solutions.test";
        var seller = new ApplicationUser { UserName = sellerEmail, Email = sellerEmail, FullName = "Ana Vendedora", IsActive = true };
        db.Users.Add(seller);
        var w = new Warehouse { PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "W1", Name = "Almacén Ponce", CountryLookupId = 1, StatusCodeId = 1, IsActive = true };
        db.Warehouses.Add(w);
        var uom = await lookups.GetIdAsync(LookupDomains.UnitOfMeasure, "UN");
        var none = await lookups.GetIdAsync(LookupDomains.TrackingType, TrackingTypes.None);
        Product P(string sku, string name) => new() { PublicId = Guid.NewGuid(), TenantId = tenantId, Sku = sku, Name = name, BaseUomLookupId = uom, TrackingTypeLookupId = none, IsActive = true };
        var normal = P($"MED-{n}", "Acetaminofén 500 mg");
        var kit = P($"KIT-{n}", "Botiquín básico");
        var compA = P($"VEN-{n}", "Vendas elásticas");
        var compB = P($"ALC70-{n}", "Alcohol 70 %");
        var compOrig = P($"ALC60-{n}", "Alcohol 60 %");
        db.Products.AddRange(normal, kit, compA, compB, compOrig);
        var rate = new TaxRate
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, Code = "IVU", Name = "IVU", Percent = 11.5m, EffectiveFrom = new DateOnly(2020, 1, 1), CreatedAtUtc = DateTime.UtcNow,
            Components =
            {
                new TaxRateComponent { TenantId = tenantId, Code = "STATE", Name = "IVU estatal", Percent = 10.5m, SortOrder = 1 },
                new TaxRateComponent { TenantId = tenantId, Code = "CITY", Name = "IVU municipal", Percent = 1m, SortOrder = 2 },
            },
        };
        db.TaxRates.Add(rate);
        var client = new Client { PublicId = Guid.NewGuid(), TenantId = tenantId, Code = $"FARM-{n}", Name = "Farmacia <Central>", StatusCodeId = 1, IsActive = true, NoticeLang = "en", CreatedAtUtc = DateTime.UtcNow };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        db.UserTenants.Add(new UserTenant { UserId = seller.Id, TenantId = tenantId, StatusCodeId = await fx.MembershipStatusIdAsync() });
        db.Locations.Add(new Location
        {
            PublicId = Guid.NewGuid(), TenantId = tenantId, ClientId = client.ClientId, Name = "Oficina", LocationTypeLookupId = await lookups.GetIdAsync(LookupDomains.LocationType, LocationTypes.Corporate),
            Line1 = "Calle Sol 10", City = "Ponce", State = "PR", PostalCode = "00716", CountryLookupId = 1, IsActive = true,
        });
        await db.SaveChangesAsync();
        return new World(tenantId, client.ClientId, client.Name, w.WarehouseId, seller.Id, sellerEmail, rate.TaxRateId, normal, kit, compA, compB, compOrig);
    }

    /// <summary>Un contacto del cliente con su correo (ContactPoint EMAIL del contacto) y la marca «Recibe avisos por correo».</summary>
    private async Task ContactAsync(World w, string name, string? email, bool primary = false, bool flagged = false, bool active = true)
    {
        var lookups = new SqlLookups(fx);
        await using var db = fx.NewDb(w.TenantId);
        var c = new ClientContact { ClientId = w.ClientId, FullName = name, IsPrimary = primary, IsActive = active, ReceivesEmailNotifications = flagged };
        db.ClientContacts.Add(c);
        await db.SaveChangesAsync();
        if (email is not null)
        {
            db.ContactPoints.Add(new ContactPoint
            {
                TenantId = w.TenantId, OwnerEntityLookupId = await lookups.GetIdAsync(LookupDomains.EntityType, EntityTypes.ClientContact), OwnerId = c.ClientContactId,
                ContactTypeLookupId = await lookups.GetIdAsync(LookupDomains.ContactType, "EMAIL"), Value = email, IsPrimary = true, IsActive = true,
            });
            await db.SaveChangesAsync();
        }
    }

    private async Task<int> StatusIdAsync(string code)
    {
        await using var db = fx.NewDb(new TenantContext());
        return await db.StatusCodes.AsNoTracking().Where(s => s.Entity == SalesCodes.StatusDomain && s.InternalCode == code).Select(s => s.StatusCodeId).SingleAsync();
    }

    /// <summary>
    /// La orden SOLICITADA: 3 × 12.50 (IVU 4.31) y un kit 1 × 25.00 (IVU 2.88) con dos piezas, una «en lugar de» otra; notas con HTML, quién autorizó y la firma.
    /// Subtotal 62.50, IVU 7.19 (estatal 6.56 + municipal 0.63), total 69.69.
    /// </summary>
    private async Task<SalesOrder> OrderAsync(World w, string status = SalesOrderStatuses.Requested)
    {
        var statusId = await StatusIdAsync(status);
        await using var db = fx.NewDb(w.TenantId);
        var file = new StoredFile
        {
            PublicId = Guid.NewGuid(), TenantId = w.TenantId, OwnerEntityLookupId = 1, OwnerId = 1, FileName = "firma.png", ContentType = "image/png", SizeBytes = Png.Length,
            Sha256 = new string('0', 64), StorageKey = "firma-key", IsActive = true,
        };
        db.Set<StoredFile>().Add(file);
        await db.SaveChangesAsync();
        var n = NextId();
        var order = new SalesOrder
        {
            PublicId = Guid.NewGuid(), TenantId = w.TenantId, Number = $"OV-{n:D5}", WarehouseId = w.WarehouseId, ClientId = w.ClientId, RequestedBy = w.SellerId, CreatedBy = w.SellerId,
            RequestedDate = new DateOnly(2026, 11, 24), RequestedAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow, Notes = "Nota <b>interna</b> del almacén",
            Subtotal = 62.50m, TaxAmount = 7.19m, StatusCodeId = statusId, DeliveryMode = SalesDeliveryModes.Pickup, AuthorizedByName = "María López", SignatureFileId = file.StoredFileId,
            Lines =
            {
                new SalesOrderLine { TenantId = w.TenantId, LineNo = 1, ProductId = w.Normal.ProductId, QtyOrdered = 3m, QtyReserved = 3m, UnitPrice = 12.50m, TaxRateId = w.RateId, TaxRatePct = 11.5m, TaxAmount = 4.31m },
                new SalesOrderLine
                {
                    TenantId = w.TenantId, LineNo = 2, ProductId = w.Kit.ProductId, QtyOrdered = 1m, QtyReserved = 1m, UnitPrice = 25.00m, TaxRateId = w.RateId, TaxRatePct = 11.5m, TaxAmount = 2.88m,
                    Components =
                    {
                        new SalesOrderLineComponent { TenantId = w.TenantId, ComponentProductId = w.CompA.ProductId, Quantity = 2m },
                        new SalesOrderLineComponent { TenantId = w.TenantId, ComponentProductId = w.CompB.ProductId, Quantity = 1m, SubstitutedProductId = w.CompOrig.ProductId },
                    },
                },
            },
        };
        db.Set<SalesOrder>().Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task<Guid> RuleAsync(World w, params NotificationRuleRecipient[] recipients)
    {
        var def = Catalog.Find(SalesEventCodes.OrderRequested)!;
        await using var db = fx.NewDb(w.TenantId);
        var rule = new NotificationRule { PublicId = Guid.NewGuid(), TenantId = w.TenantId, EventCode = SalesEventCodes.OrderRequested, Name = "Correo con la orden " + NextId(), IsActive = true };
        foreach (var channel in NotificationChannels.All)
        {
            var cd = def.ChannelDefaultFor(channel);
            rule.Channels.Add(new NotificationRuleChannel
            {
                TenantId = w.TenantId, Channel = channel, IsEnabled = channel == NotificationChannels.Email,
                SubjectJson = MultilingualText.Build(cd?.SubjectEs ?? def.DefaultSubjectEs, cd?.SubjectEn ?? def.DefaultSubjectEn),
                BodyJson = MultilingualText.Build(cd?.BodyEs ?? def.DefaultBodyEs, cd?.BodyEn ?? def.DefaultBodyEn),
            });
        }
        foreach (var r in recipients) { r.TenantId = w.TenantId; rule.Recipients.Add(r); }
        db.NotificationRules.Add(rule);
        await db.SaveChangesAsync();
        return rule.PublicId;
    }

    private static NotificationRuleRecipient Ctx(string key) => new() { Kind = NotificationRecipientKinds.Context, Value = key };
    private static NotificationRuleRecipient EmailR(string v) => new() { Kind = NotificationRecipientKinds.Email, Value = v };

    /// <summary>Publica el aviso como lo hace Solicitar (mismas variables, huella y clave de no repetición REQ-{guid}).</summary>
    private async Task<long> PublishAsync(World w, SalesOrder order, string? fingerprint = null)
    {
        await using var db = fx.NewDb(w.TenantId, w.SellerId);
        var lines = order.Lines.Select(l => (l.LineNo, l.ProductId, l.QtyOrdered, l.UnitPrice));
        var total = SalesOrderExtraRules.Total(order.Subtotal, order.TaxAmount, order.ShippingCharge, order.ShippingTaxAmount);
        var publisher = new NotificationPublisher(db, db.TenantForTest(), Catalog);
        Assert.True(await publisher.PublishAsync(SalesEventCodes.OrderRequested, new Dictionary<string, string?>
        {
            ["orderNumber"] = order.Number, ["client"] = w.ClientName, ["warehouse"] = "Almacén Ponce", ["lineCount"] = "2", ["detail"] = "Quedó todo reservado.",
            ["orderPublicId"] = order.PublicId.ToString("N"), ["orderFingerprint"] = fingerprint ?? SalesOrderEmailRules.Fingerprint(lines, total),
        }, SalesOrderService.RequestedDedupKey(order.PublicId), default));
        await db.SaveChangesAsync();
        return await db.NotificationOutbox.AsNoTracking().Where(o => o.DedupKey == SalesOrderService.RequestedDedupKey(order.PublicId)).Select(o => o.NotificationOutboxId).SingleAsync();
    }

    private async Task<List<NotificationLog>> LogAsync(World w, long outboxId)
    {
        await using var db = fx.NewDb(w.TenantId);
        return await db.NotificationLogs.AsNoTracking().Where(l => l.NotificationOutboxId == outboxId).OrderBy(l => l.NotificationLogId).ToListAsync();
    }

    private async Task<NotificationOutbox> OutboxAsync(World w, long id)
    {
        await using var db = fx.NewDb(w.TenantId);
        return await db.NotificationOutbox.AsNoTracking().SingleAsync(o => o.NotificationOutboxId == id);
    }

    // ---------------------------------------------------------------- pruebas

    [SqlServerFact]
    public async Task The_seller_the_typed_email_and_the_customer_copy_are_sent_with_the_full_order_the_tax_per_component_and_the_signature()
    {
        var w = await WorldAsync("Solutions L243 A");
        await ContactAsync(w, "Principal", "principal@farmacia.test", primary: true);
        await ContactAsync(w, "Marcado", "marcado@farmacia.test", flagged: true);
        await ContactAsync(w, "No marcado", "nomarcado@farmacia.test");
        await ContactAsync(w, "Marcado inactivo", "inactivo@farmacia.test", flagged: true, active: false);
        var order = await OrderAsync(w);
        await RuleAsync(w, Ctx(SalesContextRecipients.Seller), EmailR("almacen@solutions.test"), Ctx(SalesContextRecipients.Client));
        var outbox = await PublishAsync(w, order);
        var email = new FakeEmailSender();
        var tracked = new List<Type>();

        Assert.True(await Processor(email, tracked).TryProcessAsync(w.TenantId, outbox, DateTime.UtcNow, default));

        Assert.Equal(NotificationOutboxStatuses.Done, (await OutboxAsync(w, outbox)).Status);
        var log = await LogAsync(w, outbox);
        Assert.All(log, l => Assert.Equal((NotificationChannels.Email, NotificationLogStatuses.Sent), (l.Channel, l.Status)));
        Assert.Equal(new[] { "almacen@solutions.test", "marcado@farmacia.test", "principal@farmacia.test", w.SellerEmail }.OrderBy(x => x, StringComparer.Ordinal),
            log.Select(l => l.Recipient).OrderBy(x => x, StringComparer.Ordinal));
        Assert.DoesNotContain(email.Sent, d => d.Email is "nomarcado@farmacia.test" or "inactivo@farmacia.test");
        Assert.Equal(4, email.Sent.Count);

        // personal (vendedor y el correo escrito a mano): en español, la tabla completa, el IVU por componente, notas codificadas y el botón
        foreach (var to in new[] { w.SellerEmail, "almacen@solutions.test" })
        {
            var d = Assert.Single(email.Sent, x => x.Email == to);
            Assert.Equal($"Orden de venta {order.Number} — Farmacia <Central>", d.Subject);
            Assert.Equal("Solutions L243 A", d.CompanyName);
            Assert.StartsWith(NotificationHtml.Paragraphs($"Se solicitó la orden {order.Number} de Farmacia <Central> para Almacén Ponce.").Replace("<p>", "<p style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;color:#111827\">"), d.BodyHtml);   // el tramo de texto, codificado como siempre
            Assert.Contains("<table", d.BodyHtml);
            foreach (var sku in new[] { w.Normal.Sku, w.Kit.Sku, w.CompA.Sku, w.CompB.Sku, w.CompOrig.Sku }) Assert.Contains(sku, d.BodyHtml);
            Assert.Contains("en lugar de", d.BodyHtml);
            Assert.Contains("Farmacia &lt;Central&gt;", d.BodyHtml);
            Assert.DoesNotContain("Farmacia <Central>", d.BodyHtml);
            Assert.Contains("Calle Sol 10", d.BodyHtml);
            Assert.Contains("IVU estatal", d.BodyHtml);
            Assert.Contains("IVU municipal", d.BodyHtml);
            Assert.Contains("6.56", d.BodyHtml);
            Assert.Contains("0.63", d.BodyHtml);
            Assert.Contains("69.69", d.BodyHtml);
            Assert.Contains("Ana Vendedora", d.BodyHtml);
            Assert.Contains("Nota &lt;b&gt;interna&lt;/b&gt; del almacén", d.BodyHtml);
            Assert.DoesNotContain("<b>interna</b>", d.BodyHtml);
            Assert.Contains("María López", d.BodyHtml);
            Assert.Contains("firma adjunta", d.BodyHtml);
            Assert.Contains("Abrir la orden en Teikem", d.BodyHtml);
            Assert.Contains($"https://app.teikem.test/sales-orders?order={order.PublicId:N}", d.BodyHtml);
            Assert.DoesNotContain("Esta orden cambió después de solicitarse", d.BodyHtml);   // misma huella
            Assert.Equal($"/sales-orders?order={order.PublicId:N}", d.LinkUrl);
            var a = Assert.Single(d.Attachments!);
            Assert.Equal(($"firma-{order.Number}.png", "image/png"), (a.FileName, a.ContentType));
            Assert.Equal(Png, a.Content);
        }

        // copia al cliente: en su idioma (inglés), con la tabla, SIN el enlace a Teikem y SIN las notas
        foreach (var to in new[] { "principal@farmacia.test", "marcado@farmacia.test" })
        {
            var d = Assert.Single(email.Sent, x => x.Email == to);
            Assert.Equal($"Sales order {order.Number} — Farmacia <Central>", d.Subject);
            Assert.Contains("<table", d.BodyHtml);
            Assert.Contains(w.Kit.Sku, d.BodyHtml);
            Assert.Contains("69.69", d.BodyHtml);
            Assert.DoesNotContain("https://app.teikem.test", d.BodyHtml);
            Assert.DoesNotContain("Abrir la orden", d.BodyHtml);
            Assert.DoesNotContain("interna", d.BodyHtml);
        }

        // el DbContext del procesador solo rastreó sus registros (lo que leyó el proveedor quedó en su propio scope)
        Assert.NotEmpty(tracked);
        Assert.All(tracked, t => Assert.Equal(typeof(NotificationLog), t));

        // un reintento (la cola reabierta a mano) no repite lo enviado
        await using (var db = fx.NewDb(w.TenantId))
            await db.NotificationOutbox.Where(o => o.NotificationOutboxId == outbox).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, NotificationOutboxStatuses.Pending));
        Assert.True(await Processor(email, new List<Type>()).TryProcessAsync(w.TenantId, outbox, DateTime.UtcNow, default));
        Assert.Equal(4, email.Sent.Count);
        Assert.Equal(4, (await LogAsync(w, outbox)).Count);
    }

    [SqlServerFact]
    public async Task A_customer_without_emails_leaves_one_skipped_row_with_the_reason_and_the_others_still_get_the_email()
    {
        var w = await WorldAsync("Solutions L243 B");
        await ContactAsync(w, "Sin correo", null, primary: true, flagged: true);
        await ContactAsync(w, "No marcado", "nomarcado@farmacia.test");
        var order = await OrderAsync(w);
        await RuleAsync(w, Ctx(SalesContextRecipients.Seller), EmailR("almacen@solutions.test"), Ctx(SalesContextRecipients.Client));
        var outbox = await PublishAsync(w, order);
        var email = new FakeEmailSender();

        Assert.True(await Processor(email, new List<Type>()).TryProcessAsync(w.TenantId, outbox, DateTime.UtcNow, default));

        var log = await LogAsync(w, outbox);
        var skipped = Assert.Single(log, l => l.Status == NotificationLogStatuses.Skipped);
        Assert.Equal((NotificationChannels.Email, "(Cliente de la orden)", "El cliente no tiene correo principal ni contactos marcados con correo."),
            (skipped.Channel, skipped.Recipient, skipped.Error));
        Assert.Equal(new[] { "almacen@solutions.test", w.SellerEmail }.OrderBy(x => x, StringComparer.Ordinal),
            log.Where(l => l.Status == NotificationLogStatuses.Sent).Select(l => l.Recipient).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(2, email.Sent.Count);
        Assert.DoesNotContain(email.Sent, d => d.Email == "nomarcado@farmacia.test");
    }

    [SqlServerFact]
    public async Task A_seller_without_an_active_user_leaves_the_seller_row_skipped_with_the_reason()
    {
        var w = await WorldAsync("Solutions L243 C");
        await using (var db = fx.NewDb(w.TenantId))
            await db.Users.Where(u => u.Id == w.SellerId).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        var order = await OrderAsync(w);
        await RuleAsync(w, Ctx(SalesContextRecipients.Seller), EmailR("almacen@solutions.test"));
        var outbox = await PublishAsync(w, order);
        var email = new FakeEmailSender();

        Assert.True(await Processor(email, new List<Type>()).TryProcessAsync(w.TenantId, outbox, DateTime.UtcNow, default));

        var log = await LogAsync(w, outbox);
        var skipped = Assert.Single(log, l => l.Status == NotificationLogStatuses.Skipped);
        Assert.Equal(("(Vendedor de la orden)", "La orden no tiene un vendedor con usuario activo en la compañía."), (skipped.Recipient, skipped.Error));
        Assert.Equal("almacen@solutions.test", Assert.Single(email.Sent).Email);
    }

    [SqlServerFact]
    public async Task An_order_cancelled_before_sending_leaves_every_recipient_skipped_with_the_reason_and_sends_nothing()
    {
        var w = await WorldAsync("Solutions L243 D");
        await ContactAsync(w, "Principal", "principal@farmacia.test", primary: true);
        var order = await OrderAsync(w);
        await RuleAsync(w, Ctx(SalesContextRecipients.Seller), EmailR("almacen@solutions.test"), Ctx(SalesContextRecipients.Client));
        var outbox = await PublishAsync(w, order);
        // se cancela después de solicitar y antes de que el procesador tome el aviso
        var cancelled = await StatusIdAsync(SalesOrderStatuses.Cancelled);
        await using (var db = fx.NewDb(w.TenantId))
            await db.Set<SalesOrder>().Where(o => o.SalesOrderId == order.SalesOrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.StatusCodeId, cancelled));
        var email = new FakeEmailSender();

        Assert.True(await Processor(email, new List<Type>()).TryProcessAsync(w.TenantId, outbox, DateTime.UtcNow, default));

        Assert.Empty(email.Sent);
        var log = await LogAsync(w, outbox);
        Assert.Equal(3, log.Count);
        Assert.All(log, l => Assert.Equal((NotificationLogStatuses.Skipped, $"La orden {order.Number} se canceló antes de enviar el aviso."), (l.Status, l.Error)));
        Assert.Equal(NotificationOutboxStatuses.Done, (await OutboxAsync(w, outbox)).Status);   // SKIPPED no es una falla: no se reintenta
    }

    [SqlServerFact]
    public async Task An_order_that_changed_after_the_request_warns_the_staff_but_not_the_customer()
    {
        var w = await WorldAsync("Solutions L243 E");
        await ContactAsync(w, "Principal", "principal@farmacia.test", primary: true);
        var order = await OrderAsync(w);
        await RuleAsync(w, EmailR("almacen@solutions.test"), Ctx(SalesContextRecipients.Client));
        var outbox = await PublishAsync(w, order, fingerprint: "0000000000000000");   // la huella publicada ya no coincide con la orden
        var email = new FakeEmailSender();

        Assert.True(await Processor(email, new List<Type>()).TryProcessAsync(w.TenantId, outbox, DateTime.UtcNow, default));

        Assert.Contains("Esta orden cambió después de solicitarse; el correo muestra los datos vigentes.", Assert.Single(email.Sent, d => d.Email == "almacen@solutions.test").BodyHtml);
        var customer = Assert.Single(email.Sent, d => d.Email == "principal@farmacia.test").BodyHtml;
        Assert.DoesNotContain("cambió", customer);
        Assert.DoesNotContain("changed after", customer);
    }

    [SqlServerFact]
    public async Task A_seller_with_a_suspended_membership_does_not_receive_the_order_and_leaves_the_seller_row_skipped()
    {
        var w = await WorldAsync("Solutions L243 F");
        var suspended = await fx.MembershipStatusIdAsync(MembershipStatuses.Suspended);
        await using (var db = fx.NewDb(w.TenantId))
            await db.UserTenants.Where(m => m.UserId == w.SellerId && m.TenantId == w.TenantId).ExecuteUpdateAsync(s => s.SetProperty(m => m.StatusCodeId, suspended));
        var order = await OrderAsync(w);
        await RuleAsync(w, Ctx(SalesContextRecipients.Seller), EmailR("almacen@solutions.test"));
        var outbox = await PublishAsync(w, order);
        var email = new FakeEmailSender();

        Assert.True(await Processor(email, new List<Type>()).TryProcessAsync(w.TenantId, outbox, DateTime.UtcNow, default));

        var skipped = Assert.Single(await LogAsync(w, outbox), l => l.Status == NotificationLogStatuses.Skipped);
        Assert.Equal(("(Vendedor de la orden)", SalesOrderEmailContent.NoSellerReason), (skipped.Recipient, skipped.Error));
        Assert.Equal("almacen@solutions.test", Assert.Single(email.Sent).Email);
    }

    [SqlServerFact]
    public async Task A_user_whose_client_scope_excludes_the_order_gets_not_found_and_the_order_stays_hidden()
    {
        var w = await WorldAsync("Solutions L243 G");
        var order = await OrderAsync(w);
        var outOfScope = new TenantContext { TenantId = w.TenantId, UserId = w.SellerId, ScopedClientIds = new[] { w.ClientId + 100_000 } };
        var inScope = new TenantContext { TenantId = w.TenantId, UserId = w.SellerId, ScopedClientIds = new[] { w.ClientId } };

        var e = await Assert.ThrowsAsync<NotFoundException>(() => DirectProvider(outOfScope).ValuesForReferenceAsync(w.TenantId, SalesEventCodes.OrderRequested, order.Number, default));
        Assert.Equal($"Orden de venta '{order.Number}' no encontrada.", e.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => DirectProvider(outOfScope).ValuesForReferenceAsync(w.TenantId, SalesEventCodes.OrderRequested, order.PublicId.ToString("N"), default));
        var values = new Dictionary<string, string> { ["orderPublicId"] = order.PublicId.ToString("N") };
        Assert.Null(await DirectProvider(outOfScope).BuildAsync(new NotificationContentRequest(w.TenantId, SalesEventCodes.OrderRequested, values, NotificationAudience.Staff, "es"), default));

        Assert.Equal(order.Number, (await DirectProvider(inScope).ValuesForReferenceAsync(w.TenantId, SalesEventCodes.OrderRequested, order.Number, default))["orderNumber"]);
    }

    [SqlServerFact]
    public async Task Without_the_client_card_the_preview_omits_the_address_and_the_customer_copy_empties_the_internal_detail()
    {
        var w = await WorldAsync("Solutions L243 H");
        await using (var db = fx.NewDb(w.TenantId))
            await db.Clients.Where(c => c.ClientId == w.ClientId).ExecuteUpdateAsync(s => s.SetProperty(c => c.MerchantRegistration, "SURI-777"));
        var order = await OrderAsync(w);
        var provider = DirectProvider(new TenantContext());
        var values = new Dictionary<string, string> { ["orderPublicId"] = order.PublicId.ToString("N"), ["detail"] = "1 línea(s) con faltante en backorder." };

        var full = (await provider.BuildAsync(new NotificationContentRequest(w.TenantId, SalesEventCodes.OrderRequested, values, NotificationAudience.Staff, "es"), default))!;
        Assert.Contains("Calle Sol 10", full.Blocks["orderDetail"].Html);
        Assert.Contains("SURI-777", full.Blocks["orderDetail"].Html);
        Assert.Equal("1 línea(s) con faltante en backorder.", full.Values["detail"]);

        var noCard = (await provider.BuildAsync(new NotificationContentRequest(w.TenantId, SalesEventCodes.OrderRequested, values, NotificationAudience.Staff, "es", IncludeClientCard: false), default))!;
        Assert.DoesNotContain("Calle Sol 10", noCard.Blocks["orderDetail"].Html);
        Assert.DoesNotContain("SURI-777", noCard.Blocks["orderDetail"].Html);
        Assert.Contains(w.Kit.Sku, noCard.Blocks["orderDetail"].Html);   // la orden sí sale

        var customer = (await provider.BuildAsync(new NotificationContentRequest(w.TenantId, SalesEventCodes.OrderRequested, values, NotificationAudience.Customer, "es"), default))!;
        Assert.Equal(string.Empty, customer.Values["detail"]);
        Assert.Equal(string.Empty, NotificationRules.Render("{detail}", NotificationRules.Merge(values, customer.Values)));
    }
}
