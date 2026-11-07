using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>Lote 223: la orden de venta como dueña de archivos (la firma de quien autoriza): existe en la compañía (el filtro de tenant hace el resto).</summary>
public sealed class SalesOrderOwnedEntityResolver(TeikemDbContext db) : IOwnedEntityResolver
{
    public string EntityTypeCode => SalesCodes.EntityType;
    public Task<bool> ExistsInTenantAsync(int id, CancellationToken ct) => db.Set<SalesOrder>().AsNoTracking().AnyAsync(o => o.SalesOrderId == id, ct);
}

/// <summary>
/// Lote 223 (P3 del pedido de vendedores) — lo que el pedido guiado necesita del servidor además de crear y editar el borrador (POST / PATCH con `step`):
/// - <b>Firma de quien autoriza</b>: la imagen dibujada (PNG o JPG) se guarda cifrada como StoredFile SIGNATURE de la orden y se liga a SalesOrder.SignatureFileId, con el
///   nombre (AuthorizedByName) si viene. Solo en borrador. Cambiarla deja la anterior desligada (no se borra); quitarla solo la desliga. Se ve por GET /files/{id} con sales.view.
/// - <b>Enviar pedido</b>: reautenticación del propio vendedor y, en seguida, solicitar (valida inventario y crédito y reserva; un prospecto deja la orden esperando su
///   aprobación). PASSWORD = la contraseña (y el código MFA si lo tiene) con el mismo bloqueo por cuenta que /auth/reauth; BIOMETRIC = la app ya validó la huella o el rostro y
///   el servidor exige que la sesión traiga una verificación reciente (AAL2) dentro de la ventana de la compañía: la huella nunca se salta el MFA. Queda en
///   SalesOrder.SubmittedWithAuth y en la bitácora de seguridad (REAUTH, stage = sales_submit, con la sesión: quién, cuándo y desde qué sesión o teléfono).
/// </summary>
public sealed class SalesDraftService(TeikemDbContext db, ITenantContext tenant, SalesOrderService sales, StoredFileService files, AuthService auth, ISecurityEventWriter security)
{
    private TenantContext T => (TenantContext)tenant;

    // ---------------------------------------------------------------- firma de quien autoriza

    /// <summary>Sube la firma dibujada y la liga a la orden (con el nombre de quien autoriza si viene; "" lo quita). 400 sin imagen o si no es PNG/JPG; 422 si no es borrador.</summary>
    public async Task<SalesOrderDto> SetSignatureAsync(Guid publicId, string? authorizedByName, string? fileName, string? contentType, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        if (content.Length == 0) throw new ValidationException("file", SalesDraftStepRules.SignatureRequiredFile);
        if (!SalesDraftStepRules.IsSignatureType(StoredFileRules.SniffContentType(content.Span))) throw new ValidationException("file", SalesDraftStepRules.SignatureNotImage);
        string? name = null;
        if (authorizedByName is not null)
        {
            var (n, e) = SalesOrderExtraRules.NormalizeAuthorizedBy(authorizedByName);
            if (e is not null) throw new ValidationException("authorizedByName", e);
            name = n;
        }
        var orderId = await DraftIdAsync(publicId, ct);
        var file = await files.AttachAsync(SalesCodes.EntityType, orderId, StoredFileCodes.KindSignature, string.IsNullOrWhiteSpace(fileName) ? "firma" : fileName, contentType,
            content, ct, enforceOwnerWrite: false);   // sales.create ya lo exige el endpoint
        var fileId = await db.Set<StoredFile>().AsNoTracking().Where(f => f.PublicId == file.PublicId).Select(f => f.StoredFileId).FirstAsync(ct);
        await MutateDraftAsync(publicId, o =>
        {
            o.SignatureFileId = fileId;
            if (authorizedByName is not null) o.AuthorizedByName = name;
        }, ct);
        return await sales.GetAsync(publicId, ct);
    }

    /// <summary>Desliga la firma de la orden (el archivo se queda, desligado). 422 si no es borrador.</summary>
    public async Task<SalesOrderDto> ClearSignatureAsync(Guid publicId, CancellationToken ct)
    {
        await MutateDraftAsync(publicId, o => o.SignatureFileId = null, ct);
        return await sales.GetAsync(publicId, ct);
    }

    // ---------------------------------------------------------------- enviar pedido

    /// <summary>
    /// «Enviar pedido»: valida la orden (borrador, con productos, con a dónde entregar si es entrega, con la firma que exige el cliente), reautentica al vendedor y la solicita.
    /// 400 sin forma de reautenticación, sin contraseña, con contraseña o código MFA incorrectos (el intento cuenta para el bloqueo de la cuenta); 403 aal2_required si BIOMETRIC
    /// llega sin una verificación reciente; luego lo de solicitar (409 faltante, 422 crédito, etc.).
    /// </summary>
    public async Task<SalesOrderDto> SubmitAsync(Guid publicId, SalesOrderSubmitRequest? req, long sessionId, CancellationToken ct)
    {
        req ??= new SalesOrderSubmitRequest();
        var (method, error) = SalesOrderExtraRules.ParseSubmitAuth(req.AuthMethod);
        if (error is not null) throw new ValidationException("authMethod", error);
        if (method is null) throw new ValidationException("authMethod", SalesDraftStepRules.SubmitAuthRequired);
        var userId = T.RequireUserId();

        // la orden primero: no se gasta un intento de contraseña en un pedido que no se puede enviar
        var order = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.PublicId == publicId)
            .Select(o => new { o.SalesOrderId, o.StatusCodeId, o.ClientId, o.DeliveryMode, o.ConsigneeId, o.AuthorizedByName, o.SignatureFileId }).FirstOrDefaultAsync(ct) ?? throw NotFound();
        if (await StatusCodeAsync(order.StatusCodeId, ct) != SalesOrderStatuses.Draft) throw new StatusRuleException(SalesRules.OnlyDraftRequestable);
        if (!await db.Set<SalesOrderLine>().AnyAsync(l => l.SalesOrderId == order.SalesOrderId, ct)) throw new ValidationException("lines", SalesRules.LinesRequired);
        if (SalesDraftStepRules.MissingConsignee(order.DeliveryMode, order.ConsigneeId is not null))
            throw new ValidationException("consigneePublicId", SalesDraftStepRules.ConsigneeRequired);
        var requiresSignature = await db.Set<Client>().AsNoTracking().Where(c => c.ClientId == order.ClientId).Select(c => c.RequireOrderSignature).FirstOrDefaultAsync(ct);
        if (SalesOrderExtraRules.MissingRequiredSignature(requiresSignature, order.AuthorizedByName, order.SignatureFileId))
            throw new ValidationException("signature", SalesOrderExtraRules.SignatureRequired);

        if (method == SalesSubmitAuth.Password) await VerifyPasswordAsync(sessionId, req, ct);
        else await EnsureRecentAuthAsync(ct);

        var result = await sales.RequestAsync(publicId, new SalesOrderRequestRequest(req.AllowBackorder, req.OverrideCredit, req.Comment), ct, method);
        await security.WriteAsync(SecurityEventTypes.Reauth, SecurityOutcomes.Success, userId, tenant.TenantId,
            new { stage = "sales_submit", method, salesOrder = publicId, number = result.Number, session = sessionId }, ct);
        return result;
    }

    /// <summary>
    /// La contraseña del propio usuario (y su código MFA si tiene uno confirmado), por AuthService.ReauthAsync: el mismo bloqueo por cuenta y la misma bitácora; también renueva la
    /// verificación reciente de la sesión. Contraseña o código incorrectos = 400 (no 401: el cliente del API refrescaría el token y repetiría el intento).
    /// </summary>
    private async Task VerifyPasswordAsync(long sessionId, SalesOrderSubmitRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Password)) throw new ValidationException("password", SalesDraftStepRules.PasswordRequired);
        try
        {
            await auth.ReauthAsync(sessionId, new ReauthRequest(req.Password, req.MfaCode), ct);
        }
        catch (UnauthorizedException ex) when (ex.Message == SalesDraftStepRules.PasswordWrong)
        {
            throw new ValidationException("password", ex.Message);
        }
        catch (UnauthorizedException ex) when (ex.Message == SalesDraftStepRules.MfaWrong)
        {
            throw new ValidationException("mfaCode", ex.Message);
        }
    }

    /// <summary>BIOMETRIC: la sesión debe traer una verificación AAL2 reciente (la ventana de la compañía, como [RequireAal2]); si no, 403 aal2_required.</summary>
    private async Task EnsureRecentAuthAsync(CancellationToken ct)
    {
        var window = tenant.TenantId is int tid
            ? await db.Tenants.AsNoTracking().IgnoreQueryFilters().Where(t => t.TenantId == tid).Select(t => t.Aal2WindowMinutes).FirstOrDefaultAsync(ct)
            : 30;
        if (!SalesDraftStepRules.RecentAuth(tenant.Aal2VerifiedAtUtc, window, DateTime.UtcNow)) throw new StepUpRequiredException();
    }

    // ---------------------------------------------------------------- piezas

    private static NotFoundException NotFound() => new("Orden de venta", feminine: true);

    private Task<string?> StatusCodeAsync(int statusCodeId, CancellationToken ct)
        => db.StatusCodes.AsNoTracking().Where(s => s.StatusCodeId == statusCodeId && s.Entity == SalesCodes.StatusDomain).Select(s => (string?)s.InternalCode).FirstOrDefaultAsync(ct);

    private async Task<int> DraftIdAsync(Guid publicId, CancellationToken ct)
    {
        var row = await db.Set<SalesOrder>().AsNoTracking().Where(o => o.PublicId == publicId).Select(o => new { o.SalesOrderId, o.StatusCodeId }).FirstOrDefaultAsync(ct) ?? throw NotFound();
        if (await StatusCodeAsync(row.StatusCodeId, ct) != SalesOrderStatuses.Draft) throw new StatusRuleException(SalesRules.OnlyDraftEditable);
        return row.SalesOrderId;
    }

    private async Task MutateDraftAsync(Guid publicId, Action<SalesOrder> change, CancellationToken ct)
    {
        await db.RunInTransactionAsync(async ct2 =>
        {
            var order = await db.Set<SalesOrder>().AsTracking().FirstOrDefaultAsync(o => o.PublicId == publicId, ct2) ?? throw NotFound();
            if (await StatusCodeAsync(order.StatusCodeId, ct2) != SalesOrderStatuses.Draft) throw new StatusRuleException(SalesRules.OnlyDraftEditable);
            change(order);
            await db.SaveGuardedAsync(SalesRules.Concurrency, ct2);
        }, ct);
    }
}
