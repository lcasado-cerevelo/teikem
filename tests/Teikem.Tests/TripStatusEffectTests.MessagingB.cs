using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.IO.Compression;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Domain.Messaging;
using Teikem.Domain.Security;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Files;
using Teikem.Infrastructure.Messaging;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Lote 212 (B): fotos del chat (miniatura, acceso, retención), aviso emergente al chofer y grupo «vivo» de un rol.</summary>
public partial class TripStatusEffectTests
{
    private sealed class ChatPush : IPushSender
    {
        public bool IsConfigured => true;
        public List<(string Token, string Title, string? Body, IReadOnlyDictionary<string, string>? Data)> Sent { get; } = new();
        public PushOutcome Next { get; set; } = PushOutcome.Sent;
        public Task<PushOutcome> SendAsync(string deviceToken, string title, string? body, IReadOnlyDictionary<string, string>? data, CancellationToken ct)
        {
            Sent.Add((deviceToken, title, body, data));
            return Task.FromResult(Next);
        }
    }

    /// <summary>Un PNG válido de color liso, armado a mano (sin depender de librerías nativas de imagen).</summary>
    private static byte[] PngBytes(int w = 64, int h = 48)
    {
        static uint Crc(ReadOnlySpan<byte> data)
        {
            var c = 0xFFFFFFFFu;
            foreach (var x in data) { c ^= x; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; }
            return ~c;
        }
        static void Chunk(Stream o, string type, byte[] body)
        {
            var len = BitConverter.GetBytes((uint)body.Length); Array.Reverse(len); o.Write(len);
            var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
            o.Write(td);
            var crc = BitConverter.GetBytes(Crc(td)); Array.Reverse(crc); o.Write(crc);
        }
        var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BitConverter.GetBytes((uint)w).Reverse().ToArray().CopyTo(ihdr, 0);
        BitConverter.GetBytes((uint)h).Reverse().ToArray().CopyTo(ihdr, 4);
        ihdr[8] = 8; ihdr[9] = 2;   // 8 bits, RGB
        Chunk(ms, "IHDR", ihdr);
        var raw = new byte[h * (1 + w * 3)];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) { var i = y * (1 + w * 3) + 1 + x * 3; raw[i] = 200; raw[i + 1] = 30; raw[i + 2] = 30; }
        var z = new MemoryStream();
        using (var d = new ZLibStream(z, CompressionLevel.Fastest, true)) d.Write(raw);
        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static string PngBase64(int w = 64, int h = 48) => Convert.ToBase64String(PngBytes(w, h));

    private sealed record MessagingB(MessagingService Messages, MessageRetentionService Retention, EncryptedDiskFileStore Files, ChatPush Push, TenantContext Tenant, string Root) : IDisposable
    {
        public void Dispose() { try { Directory.Delete(Root, true); } catch { /* limpieza de la prueba */ } }
    }

    private static async Task<MessagingB> MessagingBAsync(Fixture f)
    {
        var (_, _, _, tenant) = await MessagingAsync(f);
        var root = Path.Combine(Path.GetTempPath(), "teikem-chat-" + Guid.NewGuid().ToString("N"));
        var files = new EncryptedDiskFileStore(Options.Create(new FileStoreOptions { Root = root, MasterKey = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray()) }));
        var push = new ChatPush();
        var lookups = f.Services.GetRequiredService<ILookupCache>();
        var driverPush = new DriverPushService(f.Db, tenant, push);
        var messages = new MessagingService(f.Db, tenant, new PresenceService(f.Db), new CapturingLiveNotifier(), null, files, driverPush);
        if (!await f.Db.Tenants.AnyAsync(t => t.TenantId == TenantId))
        {
            f.Db.Tenants.Add(new Tenant { TenantId = TenantId, Name = "Compañía de prueba" });
            await f.Db.SaveChangesAsync();
        }
        return new MessagingB(messages, new MessageRetentionService(f.Db, tenant, files), files, push, tenant, root);
    }

    [Fact]
    public async Task A_photo_message_has_a_thumbnail_and_only_members_of_the_thread_can_download_it()
    {
        await using var f = await Fixture.CreateAsync();
        using var b = await MessagingBAsync(f);
        b.Tenant.UserId = 1;

        var thread = await b.Messages.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 2 }, Attachments: new[] { new MessageAttachmentUpload("a.png", PngBase64()) }), default);

        var message = thread.Messages.Single();
        Assert.Equal(string.Empty, message.Body);
        var photo = Assert.Single(message.Attachments!);
        Assert.Equal("image/png", photo.ContentType);
        Assert.Equal(MessageAttachmentStates.Active, photo.State);
        var canThumb = ImageThumbnail.TryCreate(PngBytes()) is not null;   // la miniatura necesita la librería nativa de imágenes (en Windows viaja con el paquete)
        Assert.Equal(canThumb, photo.HasThumb);
        Assert.Equal("📷 Foto", thread.Thread.LastMessagePreview);

        b.Tenant.UserId = 2;
        var (full, fullType) = await b.Messages.GetAttachmentAsync(photo.PublicId, false, default);
        var (thumb, thumbType) = await b.Messages.GetAttachmentAsync(photo.PublicId, true, default);
        Assert.Equal("image/png", fullType);
        Assert.Equal(PngBytes(), full);                                   // el contenido sale idéntico al que se subió
        if (canThumb) Assert.Equal("image/jpeg", thumbType);
        else { Assert.Equal("image/png", thumbType); Assert.Equal(full, thumb); }   // sin miniatura se entrega la foto

        b.Tenant.UserId = 3;   // no es parte del hilo: ni se entera de que existe
        await Assert.ThrowsAsync<NotFoundException>(() => b.Messages.GetAttachmentAsync(photo.PublicId, false, default));
    }

    [Fact]
    public async Task Photos_are_validated_with_exact_messages()
    {
        await using var f = await Fixture.CreateAsync();
        using var b = await MessagingBAsync(f);
        b.Tenant.UserId = 1;
        var thread = await b.Messages.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 2 }, Text: "hola"), default);

        async Task<string> Fail(params MessageAttachmentUpload[] ups)
            => (await Assert.ThrowsAsync<ValidationException>(() => b.Messages.SendAsync(thread.Thread.PublicId, new MessageSendRequest("x", Attachments: ups), default))).Message;

        Assert.Equal(MessagingRules.TooManyAttachmentsMessage, await Fail(Enumerable.Range(0, 5).Select(_ => new MessageAttachmentUpload("a.png", PngBase64(8, 8))).ToArray()));
        Assert.Equal(MessagingRules.AttachmentInvalidMessage, await Fail(new MessageAttachmentUpload("a.png", "###no es base64###")));
        Assert.Equal(MessagingRules.AttachmentTypeMessage, await Fail(new MessageAttachmentUpload("a.txt", Convert.ToBase64String("hola, esto no es una imagen"u8.ToArray()))));
        var big = new byte[3 * 1024 * 1024 + 1];
        Assert.Equal(MessagingRules.AttachmentTooLargeMessage, await Fail(new MessageAttachmentUpload("a.png", Convert.ToBase64String(big))));
        Assert.Equal(MessagingRules.BodyRequiredMessage, (await Assert.ThrowsAsync<ValidationException>(() => b.Messages.SendAsync(thread.Thread.PublicId, new MessageSendRequest(null), default))).Message);
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("ARCHIVE")]
    public async Task Expired_photos_are_deleted_or_archived_but_the_text_stays(string action)
    {
        await using var f = await Fixture.CreateAsync();
        using var b = await MessagingBAsync(f);
        b.Tenant.UserId = 1;
        await b.Retention.UpdateSettingsAsync(new MessagingSettingsRequest(30, action), default);
        var thread = await b.Messages.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 2 }, Text: "mira el portón", Attachments: new[] { new MessageAttachmentUpload("a.png", PngBase64(16, 16)) }), default);
        var fresh = await b.Messages.SendAsync(thread.Thread.PublicId, new MessageSendRequest("otra", Attachments: new[] { new MessageAttachmentUpload("b.png", PngBase64(16, 16)) }), default);
        var old = await f.Db.MessageAttachments.OrderBy(a => a.MessageAttachmentId).FirstAsync();
        var oldKey = old.StorageKey!;
        old.CreatedAtUtc = DateTime.UtcNow.AddDays(-40);
        await f.Db.SaveChangesAsync();

        var (deleted, archived) = await b.Retention.PurgeAsync(default);

        Assert.Equal(action == "DELETE" ? (1, 0) : (0, 1), (deleted, archived));
        var after = await b.Messages.GetThreadAsync(thread.Thread.PublicId, null, 50, default);
        Assert.Equal("mira el portón", after.Messages[0].Body);   // el texto nunca se borra
        var expiredState = after.Messages[0].Attachments!.Single().State;
        Assert.Equal(action == "DELETE" ? MessageAttachmentStates.Deleted : MessageAttachmentStates.Archived, expiredState);
        Assert.Equal(MessageAttachmentStates.Active, after.Messages[1].Attachments!.Single().State);   // la reciente sigue
        var gone = await Assert.ThrowsAsync<ConflictException>(() => b.Messages.GetAttachmentAsync(after.Messages[0].Attachments!.Single().PublicId, false, default));
        Assert.Contains(action == "DELETE" ? "se borró el" : "se archivó el", gone.Message);
        var stillOnDisk = File.Exists(Path.Combine(b.Root, oldKey));
        Assert.Equal(action == "ARCHIVE", stillOnDisk);   // borrar quita el contenido; archivar lo conserva
        Assert.Equal((0, 0), await b.Retention.PurgeAsync(default));   // la segunda pasada no encuentra nada
        _ = fresh;
    }

    [Fact]
    public async Task The_photo_retention_settings_are_validated()
    {
        await using var f = await Fixture.CreateAsync();
        using var b = await MessagingBAsync(f);
        Assert.Equal(new MessagingSettingsDto(30, "DELETE"), await b.Retention.GetSettingsAsync(default));
        Assert.Equal(MessagingRules.PhotoDaysMessage, (await Assert.ThrowsAsync<ValidationException>(() => b.Retention.UpdateSettingsAsync(new MessagingSettingsRequest(0), default))).Message);
        Assert.Equal(MessagingRules.PhotoActionMessage, (await Assert.ThrowsAsync<ValidationException>(() => b.Retention.UpdateSettingsAsync(new MessagingSettingsRequest(null, "x"), default))).Message);
        Assert.Equal(new MessagingSettingsDto(60, "ARCHIVE"), await b.Retention.UpdateSettingsAsync(new MessagingSettingsRequest(60, "archive"), default));
    }

    [Fact]
    public async Task A_message_to_a_driver_sends_a_push_unless_the_thread_is_muted_and_a_dead_token_is_deactivated()
    {
        await using var f = await Fixture.CreateAsync();
        using var b = await MessagingBAsync(f);
        var platform = await f.Lookups.GetIdAsync(LookupDomains.DevicePlatform, "ANDROID");
        f.Db.Drivers.Add(new Driver { DriverId = 7, TenantId = TenantId, EmployeeCode = "D7", FullName = "Chofer Dos", UserId = 2, StatusCodeId = 1, IsActive = true });
        f.Db.DriverDevices.Add(new DriverDevice { TenantId = TenantId, DriverId = 7, PlatformLookupId = platform, PushToken = "tok-chofer", IsActive = true });
        await f.Db.SaveChangesAsync();
        b.Tenant.UserId = 1;

        var thread = await b.Messages.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 2 }, Text: "¿ya saliste?"), default);

        var sent = Assert.Single(b.Push.Sent);
        Assert.Equal("tok-chofer", sent.Token);
        Assert.Equal("Usuario 1", sent.Title);
        Assert.Equal("¿ya saliste?", sent.Body);
        Assert.Equal("MESSAGE", sent.Data!["kind"]);
        Assert.Equal(thread.Thread.PublicId.ToString(), sent.Data["thread"]);

        b.Tenant.UserId = 2;   // el chofer silencia el hilo: no más avisos
        await b.Messages.MuteAsync(thread.Thread.PublicId, new MessageMuteRequest(true), default);
        b.Tenant.UserId = 1;
        await b.Messages.SendAsync(thread.Thread.PublicId, new MessageSendRequest("otra"), default);
        Assert.Single(b.Push.Sent);

        b.Tenant.UserId = 2;   // lo reactiva y FCM dice que el token ya no sirve
        await b.Messages.MuteAsync(thread.Thread.PublicId, new MessageMuteRequest(false), default);
        b.Tenant.UserId = 1;
        b.Push.Next = PushOutcome.InvalidToken;
        await b.Messages.SendAsync(thread.Thread.PublicId, new MessageSendRequest("tercera"), default);
        Assert.Equal(2, b.Push.Sent.Count);
        Assert.False((await f.Db.DriverDevices.AsNoTracking().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task The_role_group_follows_the_role_and_is_one_per_company()
    {
        await using var f = await Fixture.CreateAsync();
        using var b = await MessagingBAsync(f);
        f.Db.AppRoles.Add(new Role { RoleId = 5, TenantId = TenantId, Name = "Despachadores", IsActive = true });
        f.Db.AppUserRoles.Add(new UserRole { UserId = 1, RoleId = 5, TenantId = TenantId });
        await f.Db.SaveChangesAsync();

        b.Tenant.UserId = 2;   // un chofer le escribe al grupo de Despachadores
        var thread = await b.Messages.CreateThreadAsync(new MessageThreadRequest(RoleId: 5, FollowRole: true, Text: "necesito ayuda con la ruta"), default);
        Assert.Equal(new[] { 1, 2 }, thread.Members.Select(m => m.UserId).OrderBy(x => x));
        Assert.Equal("Despachadores", thread.Thread.Name);

        // otro chofer le escribe al mismo rol: es el MISMO grupo
        b.Tenant.UserId = 3;
        var again = await b.Messages.CreateThreadAsync(new MessageThreadRequest(RoleId: 5, FollowRole: true, Text: "yo también"), default);
        Assert.Equal(thread.Thread.PublicId, again.Thread.PublicId);
        Assert.Equal(new[] { 1, 2, 3 }, again.Members.Select(m => m.UserId).OrderBy(x => x));

        // entra un despachador nuevo: al escribir en el grupo ya forma parte, y ve el historial solo desde que entra
        f.Db.AppUserRoles.Add(new UserRole { UserId = 4, RoleId = 5, TenantId = TenantId });
        f.Db.UserTenants.Single(m => m.UserId == 4).MessagingMode = MessagingModes.Full;
        await f.Db.SaveChangesAsync();
        b.Tenant.UserId = 1;
        await b.Messages.SendAsync(thread.Thread.PublicId, new MessageSendRequest("ya voy"), default);
        var seenByNew = (await f.Db.MessageThreadMembers.AsNoTracking().Where(m => m.MessageThreadId == f.Db.MessageThreads.Single().MessageThreadId && m.LeftAtUtc == null).Select(m => m.UserId).ToListAsync());
        Assert.Contains(4, seenByNew);

        // al despachador 1 le quitan el rol: sale del grupo (el 2 lo creó y se queda)
        f.Db.AppUserRoles.Remove(f.Db.AppUserRoles.Single(r => r.UserId == 1));
        await f.Db.SaveChangesAsync();
        b.Tenant.UserId = 4;
        await b.Messages.SendAsync(thread.Thread.PublicId, new MessageSendRequest("hola"), default);
        var members = await f.Db.MessageThreadMembers.AsNoTracking().Where(m => m.LeftAtUtc == null).Select(m => m.UserId).ToListAsync();
        Assert.DoesNotContain(1, members);
        Assert.Contains(2, members);

        // un grupo de rol no admite agregar personas a mano
        b.Tenant.UserId = 2;
        var ex = await Assert.ThrowsAsync<ConflictException>(() => b.Messages.AddMembersAsync(thread.Thread.PublicId, new MessageMembersRequest(new[] { 3 }), default));
        Assert.Equal(MessagingRules.RoleGroupNoAddMessage, ex.Message);
    }

    [Fact]
    public async Task Following_a_role_needs_the_role()
    {
        await using var f = await Fixture.CreateAsync();
        using var b = await MessagingBAsync(f);
        b.Tenant.UserId = 1;
        var ex = await Assert.ThrowsAsync<ValidationException>(() => b.Messages.CreateThreadAsync(new MessageThreadRequest(UserIds: new[] { 2 }, Text: "x", FollowRole: true), default));
        Assert.Equal(MessagingRules.FollowRoleNeedsRoleMessage, ex.Message);
    }
}
