using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Teikem.Domain.Constants;
using Teikem.Domain.Files;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

// Lote 127: el almacén de archivos — cifrado en reposo por compañía, referencia en la base, tipo real por el contenido, tope de tamaño, permiso del dueño, baja lógica y aislamiento entre compañías.
public sealed class StoredFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "teikem-files-" + Guid.NewGuid().ToString("N"));
    private static readonly string Key = Convert.ToBase64String(Encoding.UTF8.GetBytes("llave-de-prueba-de-32-bytes-0001"));

    // PNG mínimo válido (cabecera) y un JPEG mínimo; el texto no es ningún tipo admitido
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 2, 3 };
    private static readonly byte[] Text = Encoding.UTF8.GetBytes("esto no es una imagen");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* temporal */ }
    }

    private FileStoreOptions Opts(int maxMb = 10, string? key = null) => new() { Root = _root, MasterKey = key ?? Key, MaxMegabytes = maxMb };

    private async Task<WmsFixture> NewWorldAsync(int maxMb = 10, string? key = null)
    {
        var opts = Options.Create(Opts(maxMb, key));
        var f = await WmsFixture.CreateAsync(s =>
        {
            s.AddSingleton<ITenantClock>(TenantClock.Default);
            s.AddSingleton<IOptions<FileStoreOptions>>(opts);
            s.AddSingleton<IFileStore>(new EncryptedDiskFileStore(opts));
            s.AddSingleton<IOwnedEntityResolver, TransportOrderOwnedEntityResolver>();
            s.AddSingleton<IOwnedEntityResolver, DeliveryProofOwnedEntityResolver>();
            s.AddSingleton<StoredFileService>();
        });
        f.SetModules(ModuleKeys.LtlGround);
        return f;
    }

    [Fact]
    public async Task The_disk_store_encrypts_per_tenant_and_decrypts_only_with_the_same_tenant()
    {
        var store = new EncryptedDiskFileStore(Options.Create(Opts()));
        Assert.True(store.IsConfigured);
        var content = Encoding.UTF8.GetBytes("contenido secreto de la firma");
        var key = await store.SaveAsync(7, Guid.NewGuid(), content, default);
        var onDisk = await File.ReadAllBytesAsync(Path.Combine(_root, key));
        Assert.False(Encoding.UTF8.GetString(onDisk).Contains("secreto", StringComparison.Ordinal));   // en el disco no está en claro
        Assert.Equal(content, await store.ReadAsync(7, key, default));
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => store.ReadAsync(8, key, default));   // la llave de otra compañía no abre
        Assert.False(new EncryptedDiskFileStore(Options.Create(Opts(key: ""))).IsConfigured);
        Assert.False(new EncryptedDiskFileStore(Options.Create(Opts(key: "corta"))).IsConfigured);
    }

    [Fact]
    public async Task Attaching_validates_the_real_type_the_size_the_kind_and_the_owner_and_serves_it_back()
    {
        await using var f = await NewWorldAsync(maxMb: 1);
        var svc = f.Get<StoredFileService>();
        var c = await f.AddClientAsync("C1");
        var o = await f.AddOrderAsync(c.ClientId, "T-1", OrderStatuses.Delivered);

        var dto = await svc.AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "delivery_photo", "../../foto de la puerta.PNG", "image/png", Png, default);
        Assert.Equal((StoredFileCodes.KindDeliveryPhoto, "foto de la puerta.png", "image/png", (long)Png.Length), (dto.Kind, dto.FileName, dto.ContentType, dto.SizeBytes));
        var row = await f.Db.Set<StoredFile>().AsNoTracking().SingleAsync();
        Assert.Equal(StoredFileRules.Sha256Hex(Png), row.Sha256);
        Assert.True(row.IsEncrypted);
        Assert.True(File.Exists(Path.Combine(_root, row.StorageKey)));

        var list = await svc.ListAsync(EntityTypes.TransportOrder, o.TransportOrderId, default);
        Assert.Equal(dto.PublicId, Assert.Single(list).PublicId);
        var content = await svc.GetContentAsync(dto.PublicId, default);
        Assert.Equal(Png, content.Bytes);
        Assert.Equal("image/png", content.ContentType);

        // el tipo lo decide el contenido: un texto no entra, y un PNG que dice ser JPEG tampoco; un JPEG real sí, aunque el nombre no diga nada
        Assert.Equal(StoredFileRules.TypeNotAllowed, (await Assert.ThrowsAsync<ValidationException>(() => svc.AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "DOCUMENT", "nota.txt", "text/plain", Text, default))).Message);
        Assert.Equal(StoredFileRules.ContentMismatch, (await Assert.ThrowsAsync<ValidationException>(() => svc.AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "DOCUMENT", "x.jpg", "image/jpeg", Png, default))).Message);
        Assert.Equal("archivo.jpg", (await svc.AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "DAMAGE_PHOTO", null, null, Jpeg, default)).FileName);
        Assert.Equal(StoredFileRules.KindInvalid, (await Assert.ThrowsAsync<ValidationException>(() => svc.AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "SELFIE", "a.png", null, Png, default))).Message);
        Assert.Equal(StoredFileRules.FileRequired, (await Assert.ThrowsAsync<ValidationException>(() => svc.AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "DOCUMENT", "a.png", null, Array.Empty<byte>(), default))).Message);
        var big = new byte[1024 * 1024 + 1];
        Png.CopyTo(big, 0);
        Assert.Equal(StoredFileRules.TooLarge(1), (await Assert.ThrowsAsync<ValidationException>(() => svc.AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "DOCUMENT", "a.png", null, big, default))).Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.AttachAsync(EntityTypes.TransportOrder, 999_999, "DOCUMENT", "a.png", null, Png, default));   // dueño inexistente
        await Assert.ThrowsAsync<ValidationException>(() => svc.AttachAsync("WAREHOUSE", 1, "DOCUMENT", "a.png", null, Png, default));                     // sin resolver registrado aquí

        // baja lógica: ya no se lista ni se sirve; el contenido sigue en el almacén
        await svc.RemoveAsync(dto.PublicId, default);
        Assert.DoesNotContain(await svc.ListAsync(EntityTypes.TransportOrder, o.TransportOrderId, default), x => x.PublicId == dto.PublicId);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetContentAsync(dto.PublicId, default));
        Assert.True(File.Exists(Path.Combine(_root, row.StorageKey)));
    }

    [Fact]
    public async Task The_owner_permission_decides_and_other_tenants_never_see_the_file()
    {
        await using var f = await NewWorldAsync();
        var svc = f.Get<StoredFileService>();
        var c = await f.AddClientAsync("C1");
        var o = await f.AddOrderAsync(c.ClientId, "T-1", OrderStatuses.Delivered);
        var dto = await svc.AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "DOCUMENT", "guia.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4 hola"), default);

        f.SetPermissions(PermissionCatalog.OrdersView);                                   // ve, pero no escribe
        Assert.Single(await svc.ListAsync(EntityTypes.TransportOrder, o.TransportOrderId, default));
        Assert.Equal("guia.pdf", (await svc.GetContentAsync(dto.PublicId, default)).FileName);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "DOCUMENT", "otra.pdf", null, Encoding.ASCII.GetBytes("%PDF-1.4"), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.RemoveAsync(dto.PublicId, default));
        f.SetPermissions(PermissionCatalog.OrdersDeliver);                                // sin orders.view no ve
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.GetContentAsync(dto.PublicId, default));

        f.SetPermissions(PermissionCatalog.OrdersView, PermissionCatalog.OrdersDeliver);
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            f.SetPermissions(PermissionCatalog.OrdersView, PermissionCatalog.OrdersDeliver);   // con permiso en la otra compañía, el archivo igual no existe para ella
            await Assert.ThrowsAsync<NotFoundException>(() => svc.GetContentAsync(dto.PublicId, default));
            Assert.Empty(await svc.ListAsync(EntityTypes.TransportOrder, o.TransportOrderId, default));
        }
    }

    [Fact]
    public async Task Without_a_master_key_the_store_refuses_uploads_with_a_clear_message()
    {
        await using var f = await NewWorldAsync(key: "");
        var c = await f.AddClientAsync("C1");
        var o = await f.AddOrderAsync(c.ClientId, "T-1", OrderStatuses.Delivered);
        var ex = await Assert.ThrowsAsync<StatusRuleException>(() => f.Get<StoredFileService>().AttachAsync(EntityTypes.TransportOrder, o.TransportOrderId, "DOCUMENT", "a.png", null, Png, default));
        Assert.Equal(StoredFileRules.NotConfigured, ex.Message);
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("application/pdf", ".pdf")]
    public void Safe_file_names_drop_folders_and_odd_characters_and_carry_the_real_extension(string type, string ext)
    {
        Assert.Equal("Firma Ana" + ext, StoredFileRules.SafeFileName(@"C:\\fotos\\<Firma: Ana>.jpeg", type));
        Assert.Equal("archivo" + ext, StoredFileRules.SafeFileName("   ", type));
        Assert.True(StoredFileRules.SafeFileName(new string('a', 500), type).Length <= StoredFileRules.MaxFileNameLength);
        Assert.Null(StoredFileRules.SniffContentType(Text));
        Assert.Equal("image/png", StoredFileRules.SniffContentType(Png));
    }
}
