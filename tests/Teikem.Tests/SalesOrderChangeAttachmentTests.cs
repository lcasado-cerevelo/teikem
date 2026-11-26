using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Sales;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 249 (P5): la evidencia de la autorización. Obligatoria por la compañía o por el cliente (400 con el mensaje exacto si falta); debe ser un archivo de ESA orden
// (404 si es de otra) y respalda una sola modificación (409); se sube como DOCUMENT de la orden y solo imagen o PDF (400 de StoredFileRules).
public sealed class SalesOrderChangeAttachmentTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8 };

    private static StoredFileService Files(SalesChangeWorld x)
    {
        var opts = Options.Create(new FileStoreOptions
        {
            Root = Path.Combine(Path.GetTempPath(), "teikem-l249-" + Guid.NewGuid().ToString("N")),
            MasterKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("llave-de-prueba-de-32-bytes-0001")), MaxMegabytes = 10,
        });
        return new StoredFileService(x.F.Db, x.F.Tenant, x.F.Lookups, new EncryptedDiskFileStore(opts), opts, x.F.Get<PermissionService>(),
            new IOwnedEntityResolver[] { new SalesOrderOwnedEntityResolver(x.F.Db) });
    }

    [Fact]
    public async Task Required_attachment_by_the_company_or_the_client_is_a_400_and_an_uploaded_document_backs_one_change()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var files = Files(x);
        var svc = ChangeKit.Service(x, files: files);

        await ChangeKit.SetTenantAsync(x.F, SalesChangeApprovalModes.None, attachment: true);
        var missing = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)), default));
        Assert.Equal(SalesChangeRules.AttachmentRequired, missing.Message);

        // la compañía no lo pide, el cliente sí (el cliente manda)
        await ChangeKit.SetTenantAsync(x.F, SalesChangeApprovalModes.None, attachment: false);
        await ChangeKit.SetClientAsync(x.F, x.C.ClientId, c => c.SalesChangeAttachmentRequired = true);
        var byClient = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)), default));
        Assert.Equal(SalesChangeRules.AttachmentRequired, byClient.Message);

        // tipo no permitido
        var bad = await Assert.ThrowsAsync<ValidationException>(() => svc.UploadAttachmentAsync(o.PublicId, "nota.txt", "text/plain", Encoding.UTF8.GetBytes("hola mundo"), default));
        Assert.Equal(StoredFileRules.TypeNotAllowed, bad.Message);

        var file = await svc.UploadAttachmentAsync(o.PublicId, "captura.png", "image/png", Png, default);
        Assert.Equal(StoredFileCodes.KindDocument, file.Kind);
        var dto = await svc.CreateAsync(o.PublicId, ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)) with { AttachmentFilePublicId = file.PublicId }, default);
        Assert.Equal((file.PublicId, "captura.png"), (dto.AttachmentFilePublicId!.Value, dto.AttachmentFileName));

        // ya respalda otra modificación
        var used = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(o.PublicId,
            ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 5m)) with { AttachmentFilePublicId = file.PublicId }, default));
        Assert.Equal(SalesOrderChangeService.AttachmentUsed, used.Message);
    }

    [Fact]
    public async Task A_file_of_another_order_is_a_404()
    {
        await using var x = await ChangeKit.WorldAsync();
        var o = await x.RequestedAsync((x.P1, 3m));
        var other = await x.RequestedAsync((x.P2, 1m));
        var files = Files(x);
        var svc = ChangeKit.Service(x, files: files);
        var otherFile = await svc.UploadAttachmentAsync(other.PublicId, "otra.png", "image/png", Png, default);

        var e = await Assert.ThrowsAsync<NotFoundException>(() => svc.CreateAsync(o.PublicId,
            ChangeKit.Req(ChangeKit.Qty(o.Lines[0].Id, 4m)) with { AttachmentFilePublicId = otherFile.PublicId }, default));
        Assert.Equal("Archivo no encontrado.", e.Message);
        Assert.Empty(await x.F.Db.Set<SalesOrderChange>().AsNoTracking().ToListAsync());
    }
}
