using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Entities;
using Teikem.Domain.Files;
using Teikem.Domain.Identity;
using Teikem.Domain.Sales;
using Teikem.Domain.Security;
using Teikem.Domain.Wms;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 223 (P3 del pedido de vendedores, pedido guiado): el borrador se guarda por pasos (CLIENT sin productos; desde PRODUCTS con productos; DELIVERY con a dónde se
/// entrega), la firma de quien autoriza es un StoredFile SIGNATURE de la orden ligado a SignatureFileId, y «Enviar pedido» exige reautenticar al vendedor (PASSWORD con su
/// contraseña y su MFA; BIOMETRIC solo con una verificación reciente de la sesión) antes de solicitar, y deja SubmittedWithAuth.
/// </summary>
public sealed class SalesDraftStepTests
{
    private const string Password = "Clave-Segura1";
    private const int TotpLookupId = 6900;

    private sealed class CapturingSecurityEventWriter : ISecurityEventWriter
    {
        public List<(string EventType, string Outcome, string Detail)> Events { get; } = new();

        public Task WriteAsync(string eventType, string outcome, int? userId = null, int? tenantId = null, object? detail = null, CancellationToken ct = default)
        {
            Events.Add((eventType, outcome, detail is null ? "" : JsonSerializer.Serialize(detail)));
            return Task.CompletedTask;
        }
    }

    private sealed record World(SalesVendorWorld X, long SessionId) : IAsyncDisposable
    {
        public SalesDraftService Drafts => X.F.Get<SalesDraftService>();
        public SalesOrderService Sales => X.Sales;
        public CapturingSecurityEventWriter Security => X.F.Get<CapturingSecurityEventWriter>();
        public ValueTask DisposeAsync() => X.DisposeAsync();

        public ApplicationUser User()
        {
            X.F.Db.ChangeTracker.Clear();
            return X.F.Db.Users.AsNoTracking().Single(u => u.Id == 1);
        }

        public RefreshToken Session()
        {
            X.F.Db.ChangeTracker.Clear();
            return X.F.Db.RefreshTokens.IgnoreQueryFilters().AsNoTracking().Single(t => t.RefreshTokenId == SessionId);
        }
    }

    /// <summary>El mundo de ventas (SalesVendorWorld) + Identity y AuthService (como AuthLockoutTests): el usuario 1 con contraseña y su sesión.</summary>
    private static async Task<World> CreateAsync()
    {
        var x = await SalesVendorWorld.CreateAsync(s =>
        {
            s.AddIdentityCore<ApplicationUser>().AddRoles<ApplicationRole>().AddEntityFrameworkStores<TeikemDbContext>();
            s.Configure<IdentityOptions>(o => { o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); });
            s.AddSingleton<CapturingSecurityEventWriter>();
            s.AddSingleton<ISecurityEventWriter>(sp => sp.GetRequiredService<CapturingSecurityEventWriter>());
            s.AddSingleton(new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', 64) })));
            s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            s.AddSingleton<IPasswordBreachChecker, NoOpPasswordBreachChecker>();
            s.AddSingleton<DeviceService>();
            s.AddSingleton<PinService>();
            s.AddSingleton<IOptions<OnboardingOptions>>(Options.Create(new OnboardingOptions { Enabled = false }));
            s.AddSingleton<ITransactionalEmailSender, NoEmailSender>();
            s.AddSingleton<AuthService>();
            s.AddSingleton<IOwnedEntityResolver, SalesOrderOwnedEntityResolver>();
            s.AddSingleton<SalesDraftService>();
        });
        var f = x.F;
        var me = new ApplicationUser { Id = 1, UserName = "vendedor@t.local", Email = "vendedor@t.local", FullName = "Vendedor Uno", SecurityStamp = "s1", IsActive = true, LockoutEnabled = true };
        me.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(me, Password);
        f.Db.Users.Add(me);
        var session = new RefreshToken
        {
            UserId = 1, TenantId = WmsFixture.TenantId, TokenHash = Guid.NewGuid().ToString("N"), IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        };
        f.Db.RefreshTokens.Add(session);
        // el catálogo que ReauthAsync consulta (¿tiene TOTP confirmado?)
        f.Db.LookupCodes.Add(new LookupCode { LookupCodeId = TotpLookupId, Entity = LookupDomains.MfaFactorType, InternalCode = MfaFactorTypes.Totp, LabelJson = "{\"es\":\"TOTP\"}", IsActive = true });
        await f.Db.SaveChangesAsync();
        f.Lookups.Load(f.Db.LookupCodes.IgnoreQueryFilters().AsNoTracking().ToList());
        f.Db.ChangeTracker.Clear();
        return new World(x, session.RefreshTokenId);
    }

    private static SalesOrderRequest Step(SalesVendorWorld x, string step, Client? c = null, Product? p = null, decimal qty = 1m)
        => new(WarehousePublicId: x.W.PublicId, ClientPublicId: c?.PublicId, Step: step, Lines: p is null ? null : new[] { new SalesOrderLineRequest(p.PublicId, qty) });

    private static async Task<SalesOrderDto> ReadyDraftAsync(World w, Client c, Product p, decimal qty = 1m) => await w.X.DraftAsync(c, p, qty);

    // ---------------------------------------------------------------- borrador por pasos

    [Fact]
    public async Task The_client_step_saves_a_draft_without_products_and_the_next_steps_ask_for_what_they_need()
    {
        await using var w = await CreateAsync();
        var x = w.X;
        var c = await x.ClientAsync("C1", r => r.PaymentTermLookupId = x.Lookup(LookupDomains.PaymentTerm, "NET30"));
        var p = await x.ProductAsync("MED", 10m);

        // paso 1: solo el cliente (urgente y la nota); la forma de pago se prellena con la del cliente
        var d = await w.Sales.CreateAsync(new SalesOrderRequest(WarehousePublicId: x.W.PublicId, ClientPublicId: c.PublicId, IsUrgent: true, Notes: "Antes del mediodía", Step: "client"), default);
        Assert.Equal((SalesOrderStatuses.Draft, 0, true, SalesPaymentMethods.Credit), (d.Status, d.Lines.Count, d.Terms!.IsUrgent, d.Terms.PaymentMethod));
        // sin paso, la orden sigue exigiendo productos (la ventana de siempre no cambia)
        var noLines = await Assert.ThrowsAsync<ValidationException>(() => w.Sales.CreateAsync(new SalesOrderRequest(WarehousePublicId: x.W.PublicId, ClientPublicId: c.PublicId), default));
        Assert.Equal(SalesRules.LinesRequired, noLines.Message);
        // un borrador sin productos no se solicita
        var empty = await Assert.ThrowsAsync<ValidationException>(() => w.Sales.RequestAsync(d.PublicId, null, default));
        Assert.Equal(SalesRules.LinesRequired, empty.Message);

        // paso 2: productos — sin productos no avanza; con ellos, el carrito queda en la orden
        var stillEmpty = await Assert.ThrowsAsync<ValidationException>(() => w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(Step: SalesDraftSteps.Products), default));
        Assert.Equal(SalesRules.LinesRequired, stillEmpty.Message);
        var withLines = await w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(Step: SalesDraftSteps.Products, Lines: new[] { new SalesOrderLineRequest(p.PublicId, 3m) }), default);
        Assert.Equal((1, 30m), (withLines.Lines.Count, withLines.Subtotal));

        // paso 3: entrega — «Entrega» sin consignatario no avanza; recogido sí; con consignatario, entrega
        var noWhere = await Assert.ThrowsAsync<ValidationException>(() => w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(Step: SalesDraftSteps.Delivery, DeliveryMode: "DELIVERY"), default));
        Assert.Equal(SalesDraftStepRules.ConsigneeRequired, noWhere.Message);
        var pickup = await w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(Step: SalesDraftSteps.Delivery, DeliveryMode: "PICKUP"), default);
        Assert.Equal(SalesDeliveryModes.Pickup, pickup.Terms!.DeliveryMode);
        var consignee = await x.ConsigneeAsync(c);
        var delivery = await w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(Step: SalesDraftSteps.Delivery, DeliveryMode: "DELIVERY", ConsigneePublicId: consignee.PublicId), default);
        Assert.Equal((SalesDeliveryModes.Delivery, consignee.PublicId), (delivery.Terms!.DeliveryMode, delivery.ConsigneePublicId));
        // quitar el consignatario de un pedido de entrega en el paso de autorización tampoco pasa
        var cleared = await Assert.ThrowsAsync<ValidationException>(() => w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(Step: SalesDraftSteps.Authorization, ClearConsignee: true), default));
        Assert.Equal(SalesDraftStepRules.ConsigneeRequired, cleared.Message);

        // paso 4: autorización — el nombre de quien autoriza
        var auth = await w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(Step: SalesDraftSteps.Authorization, AuthorizedByName: "  Ana Rivera "), default);
        Assert.Equal(("Ana Rivera", 1, SalesOrderStatuses.Draft), (auth.Terms!.AuthorizedByName, auth.Lines.Count, auth.Status));

        // un paso que no existe
        var bad = await Assert.ThrowsAsync<ValidationException>(() => w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(Step: "PAGO"), default));
        Assert.Equal(SalesDraftStepRules.StepInvalid, bad.Message);
    }

    [Fact]
    public async Task Going_back_to_the_client_step_may_empty_the_cart_and_change_the_client()
    {
        await using var w = await CreateAsync();
        var x = w.X;
        var c1 = await x.ClientAsync("C1");
        var c2 = await x.ClientAsync("C2");
        var p = await x.ProductAsync("MED", 10m);
        var d = await w.Sales.CreateAsync(Step(x, SalesDraftSteps.Products, c1, p, 2m), default);
        Assert.Single(d.Lines);

        var back = await w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(ClientPublicId: c2.PublicId, Step: SalesDraftSteps.Client, Lines: Array.Empty<SalesOrderLineRequest>()), default);
        Assert.Equal((c2.PublicId, 0, 0m), (back.ClientPublicId, back.Lines.Count, back.Subtotal));
        // sin paso, mandar la lista vacía sigue siendo un error
        var noStep = await Assert.ThrowsAsync<ValidationException>(() => w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(Lines: Array.Empty<SalesOrderLineRequest>()), default));
        Assert.Equal(SalesRules.LinesRequired, noStep.Message);
    }

    // ---------------------------------------------------------------- firma como archivo

    [Fact]
    public async Task The_drawn_signature_is_an_encrypted_file_of_the_order_linked_with_the_name_of_who_authorizes()
    {
        await using var w = await CreateAsync();
        var x = w.X;
        var c = await x.ClientAsync("C1");
        var p = await x.ProductAsync("MED", 10m);
        var d = await ReadyDraftAsync(w, c, p);

        var signed = await w.Drafts.SetSignatureAsync(d.PublicId, " Luis Pérez ", "firma.png", "image/png", SalesVendorWorld.Png, default);
        Assert.Equal("Luis Pérez", signed.Terms!.AuthorizedByName);
        var fileId = Assert.IsType<Guid>(signed.Terms.SignatureFilePublicId);
        var file = await x.F.Db.Set<StoredFile>().AsNoTracking().SingleAsync(f => f.PublicId == fileId);
        var order = await x.F.Db.Set<SalesOrder>().AsNoTracking().SingleAsync(o => o.PublicId == d.PublicId);
        Assert.Equal((StoredFileCodes.KindSignature, x.Lookup(LookupDomains.EntityType, SalesCodes.EntityType), order.SalesOrderId, true, "image/png"),
            (file.Kind, file.OwnerEntityLookupId, file.OwnerId, file.IsEncrypted, file.ContentType));
        Assert.Equal(file.StoredFileId, order.SignatureFileId);
        // se ve con sales.view (GET /files/{id})
        x.F.SetPermissions(PermissionCatalog.SalesView);
        var content = await x.F.Get<StoredFileService>().GetContentAsync(fileId, default);
        Assert.Equal(SalesVendorWorld.Png, content.Bytes);
        x.F.SetPermissions(PermissionCatalog.SalesView, PermissionCatalog.SalesCreate);

        // otra firma reemplaza a la anterior (sin nombre: el nombre no cambia); quitarla solo la desliga
        var again = await w.Drafts.SetSignatureAsync(d.PublicId, null, null, null, SalesVendorWorld.Png, default);
        Assert.NotEqual(fileId, again.Terms!.SignatureFilePublicId);
        Assert.Equal("Luis Pérez", again.Terms.AuthorizedByName);
        var removed = await w.Drafts.ClearSignatureAsync(d.PublicId, default);
        Assert.Null(removed.Terms!.SignatureFilePublicId);
        Assert.Equal(2, await x.F.Db.Set<StoredFile>().AsNoTracking().CountAsync(f => f.IsActive && f.Kind == StoredFileCodes.KindSignature));
    }

    [Fact]
    public async Task A_signature_must_be_a_png_or_jpg_and_only_goes_on_a_draft()
    {
        await using var w = await CreateAsync();
        var x = w.X;
        var c = await x.ClientAsync("C1");
        var p = await x.ProductAsync("MED", 10m);
        var d = await ReadyDraftAsync(w, c, p);

        var empty = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SetSignatureAsync(d.PublicId, null, "f.png", "image/png", Array.Empty<byte>(), default));
        Assert.Equal(SalesDraftStepRules.SignatureRequiredFile, empty.Message);
        var text = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SetSignatureAsync(d.PublicId, null, "f.txt", "text/plain", Encoding.UTF8.GetBytes("no es una firma"), default));
        Assert.Equal(SalesDraftStepRules.SignatureNotImage, text.Message);
        var pdf = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SetSignatureAsync(d.PublicId, null, "f.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4 x"), default));
        Assert.Equal(SalesDraftStepRules.SignatureNotImage, pdf.Message);
        var longName = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SetSignatureAsync(d.PublicId, new string('n', 151), "f.png", "image/png", SalesVendorWorld.Png, default));
        Assert.Equal(SalesOrderExtraRules.AuthorizedByTooLong, longName.Message);
        Assert.Empty(await x.F.Db.Set<StoredFile>().AsNoTracking().ToListAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => w.Drafts.SetSignatureAsync(Guid.NewGuid(), null, "f.png", "image/png", SalesVendorWorld.Png, default));

        await w.Sales.RequestAsync(d.PublicId, null, default);
        var closed = await Assert.ThrowsAsync<StatusRuleException>(() => w.Drafts.SetSignatureAsync(d.PublicId, null, "f.png", "image/png", SalesVendorWorld.Png, default));
        Assert.Equal(SalesRules.OnlyDraftEditable, closed.Message);
        await Assert.ThrowsAsync<StatusRuleException>(() => w.Drafts.ClearSignatureAsync(d.PublicId, default));
    }

    // ---------------------------------------------------------------- enviar exige reautenticación

    [Fact]
    public async Task Submitting_asks_for_the_password_and_records_it_as_the_sellers_signature()
    {
        await using var w = await CreateAsync();
        var x = w.X;
        var c = await x.ClientAsync("C1");
        var p = await x.ProductAsync("MED", 10m);
        var d = await ReadyDraftAsync(w, c, p, 2m);

        var none = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(), w.SessionId, default));
        Assert.Equal(SalesDraftStepRules.SubmitAuthRequired, none.Message);
        var pin = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest("PIN"), w.SessionId, default));
        Assert.Equal(SalesOrderExtraRules.SubmitAuthInvalid, pin.Message);
        var noPassword = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest("password"), w.SessionId, default));
        Assert.Equal(SalesDraftStepRules.PasswordRequired, noPassword.Message);

        // contraseña equivocada: 400 (no 401), cuenta para el bloqueo y la orden sigue en borrador
        var wrong = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, "equivocada"), w.SessionId, default));
        Assert.Equal((SalesDraftStepRules.PasswordWrong, 400), (wrong.Message, wrong.StatusCode));
        Assert.Equal(1, w.User().AccessFailedCount);
        Assert.Equal(SalesOrderStatuses.Draft, (await w.Sales.GetAsync(d.PublicId, default)).Status);

        // la buena: solicita (reserva) y queda PASSWORD; la sesión queda verificada y en la bitácora de seguridad
        var sent = await w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, Password), w.SessionId, default);
        Assert.Equal((SalesOrderStatuses.Requested, SalesSubmitAuth.Password, 2m), (sent.Status, sent.Terms!.SubmittedWithAuth, sent.Lines[0].QtyReserved));
        Assert.Equal(0, w.User().AccessFailedCount);
        Assert.NotNull(w.Session().Aal2VerifiedAtUtc);
        Assert.Contains(w.Security.Events, e => e.EventType == SecurityEventTypes.Reauth && e.Outcome == SecurityOutcomes.Success
            && e.Detail.Contains("\"stage\":\"sales_submit\"") && e.Detail.Contains("\"method\":\"PASSWORD\""));

        // ya no es borrador: no se envía otra vez (y no se gasta un intento de contraseña)
        var twice = await Assert.ThrowsAsync<StatusRuleException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, "equivocada"), w.SessionId, default));
        Assert.Equal(SalesRules.OnlyDraftRequestable, twice.Message);
        Assert.Equal(0, w.User().AccessFailedCount);
        // solicitar por la ruta de siempre no marca reautenticación
        var other = await ReadyDraftAsync(w, c, p);
        Assert.Null((await w.Sales.RequestAsync(other.PublicId, null, default)).Terms!.SubmittedWithAuth);
    }

    [Fact]
    public async Task The_password_does_not_skip_the_mfa_of_the_user()
    {
        await using var w = await CreateAsync();
        var x = w.X;
        x.F.Db.UserMfaFactors.Add(new UserMfaFactor { UserId = 1, FactorTypeLookupId = TotpLookupId, IsActive = true, IsConfirmed = true, SecretEnc = [1, 2, 3] });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var c = await x.ClientAsync("C1");
        var p = await x.ProductAsync("MED", 10m);
        var d = await ReadyDraftAsync(w, c, p);

        var noCode = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, Password), w.SessionId, default));
        Assert.Equal(SalesDraftStepRules.MfaWrong, noCode.Message);
        Assert.True(noCode.Errors!.ContainsKey("mfaCode"));
        var badCode = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, Password, "000000"), w.SessionId, default));
        Assert.Equal(SalesDraftStepRules.MfaWrong, badCode.Message);
        Assert.Equal(SalesOrderStatuses.Draft, (await w.Sales.GetAsync(d.PublicId, default)).Status);
        Assert.Null(w.Session().Aal2VerifiedAtUtc);
    }

    [Fact]
    public async Task Biometric_only_counts_with_a_recent_verification_of_the_session()
    {
        await using var w = await CreateAsync();
        var x = w.X;
        var c = await x.ClientAsync("C1");
        var p = await x.ProductAsync("MED", 10m);
        var d = await ReadyDraftAsync(w, c, p);

        x.F.Tenant.Aal2VerifiedAtUtc = null;   // sesión abierta sin verificación (p. ej. solo con PIN)
        await Assert.ThrowsAsync<StepUpRequiredException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Biometric), w.SessionId, default));
        x.F.Tenant.Aal2VerifiedAtUtc = DateTime.UtcNow.AddMinutes(-31);   // fuera de la ventana de la compañía (30 minutos)
        var old = await Assert.ThrowsAsync<StepUpRequiredException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Biometric), w.SessionId, default));
        Assert.Equal(403, old.StatusCode);
        Assert.Equal(SalesOrderStatuses.Draft, (await w.Sales.GetAsync(d.PublicId, default)).Status);

        x.F.Tenant.Aal2VerifiedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var sent = await w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Biometric), w.SessionId, default);
        Assert.Equal((SalesOrderStatuses.Requested, SalesSubmitAuth.Biometric), (sent.Status, sent.Terms!.SubmittedWithAuth));
        Assert.Equal(0, w.User().AccessFailedCount);
    }

    [Fact]
    public async Task Submitting_a_prospect_order_reserves_and_waits_for_approval_and_a_shortage_asks_again()
    {
        await using var w = await CreateAsync();
        var x = w.X;
        var p = await x.ProductAsync("MED", 10m, stock: 3m);
        var dto = await x.Prospects.CreateBasicAsync(SalesVendorWorld.BasicRequest(), default);
        var prospect = await x.F.Db.Clients.AsNoTracking().SingleAsync(r => r.PublicId == dto.PublicId);
        var d = await ReadyDraftAsync(w, prospect, p, 5m);

        // falta inventario: 409 y la orden sigue en borrador; con allowBackorder se reserva lo que hay
        var shortage = await Assert.ThrowsAnyAsync<TeikemException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, Password), w.SessionId, default));
        Assert.Equal(409, shortage.StatusCode);
        x.F.Db.ChangeTracker.Clear();
        Assert.Null((await w.Sales.GetAsync(d.PublicId, default)).Terms!.SubmittedWithAuth);
        var sent = await w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, Password, AllowBackorder: true), w.SessionId, default);
        Assert.Equal((SalesOrderStatuses.Requested, true, 3m, 2m), (sent.Status, sent.Terms!.AwaitingClientApproval, sent.Lines[0].QtyReserved, sent.Lines[0].QtyBackordered));
    }

    [Fact]
    public async Task The_signature_the_client_requires_and_where_to_deliver_are_checked_before_the_password()
    {
        await using var w = await CreateAsync();
        var x = w.X;
        var p = await x.ProductAsync("MED", 10m);
        var c = await x.ClientAsync("FIRMA", r => r.RequireOrderSignature = true);
        var d = await ReadyDraftAsync(w, c, p);

        var missing = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, "equivocada"), w.SessionId, default));
        Assert.Equal(SalesOrderExtraRules.SignatureRequired, missing.Message);
        Assert.Equal(0, w.User().AccessFailedCount);   // no se gastó un intento de contraseña

        await w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(DeliveryMode: "DELIVERY"), default);   // la ventana de siempre lo deja guardar
        await w.Drafts.SetSignatureAsync(d.PublicId, "Luis Pérez", "firma.png", "image/png", SalesVendorWorld.Png, default);
        var noWhere = await Assert.ThrowsAsync<ValidationException>(() => w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, Password), w.SessionId, default));
        Assert.Equal(SalesDraftStepRules.ConsigneeRequired, noWhere.Message);

        await w.Sales.UpdateAsync(d.PublicId, new SalesOrderRequest(DeliveryMode: "PICKUP"), default);
        var sent = await w.Drafts.SubmitAsync(d.PublicId, new SalesOrderSubmitRequest(SalesSubmitAuth.Password, Password), w.SessionId, default);
        Assert.Equal((SalesOrderStatuses.Requested, "Luis Pérez", SalesSubmitAuth.Password), (sent.Status, sent.Terms!.AuthorizedByName, sent.Terms.SubmittedWithAuth));
        Assert.NotNull(sent.Terms.SignatureFilePublicId);
    }

    // ---------------------------------------------------------------- reglas puras

    [Fact]
    public void Step_and_recent_auth_rules()
    {
        Assert.Equal((SalesDraftSteps.Delivery, (string?)null), SalesDraftStepRules.ParseStep(" delivery "));
        Assert.Equal(((string?)null, (string?)null), SalesDraftStepRules.ParseStep(""));
        Assert.Equal(SalesDraftStepRules.StepInvalid, SalesDraftStepRules.ParseStep("PAY").Error);
        Assert.True(SalesDraftStepRules.LinesOptional(SalesDraftSteps.Client));
        Assert.False(SalesDraftStepRules.LinesOptional(null));
        Assert.True(SalesDraftStepRules.MissingConsignee("DELIVERY", false));
        Assert.False(SalesDraftStepRules.MissingConsignee(null, false));
        Assert.False(SalesDraftStepRules.MissingConsignee("PICKUP", false));
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(SalesDraftStepRules.RecentAuth(now.AddMinutes(-30), 30, now));
        Assert.False(SalesDraftStepRules.RecentAuth(now.AddMinutes(-31), 30, now));
        Assert.True(SalesDraftStepRules.RecentAuth(now.AddMinutes(-29), 0, now));   // sin ventana = 30
        Assert.False(SalesDraftStepRules.RecentAuth(null, 30, now));
        Assert.True(SalesDraftStepRules.IsSignatureType("image/jpeg"));
        Assert.False(SalesDraftStepRules.IsSignatureType("image/gif"));
    }
}
