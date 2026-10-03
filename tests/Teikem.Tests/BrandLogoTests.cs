using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Tenancy;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Logos de la marca por compañía (2026-10): reglas puras (BrandLogoRules: tipo por contenido real, tamaño, SVG sin contenido
/// activo ni referencias externas), servicio (subir/reemplazar/quitar/listar, mensajes y códigos 400/413/415, aislamiento entre
/// compañías, soft delete), y el controlador (permisos, cabeceras de seguridad y caché).
/// </summary>
public sealed class BrandLogoTests
{
    // ------------------------------------------------------------------ archivos de prueba
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0xFF, 0xD9];
    private static readonly byte[] WebP = BuildWebP();
    private const string SvgNs = "http://www.w3.org/2000/svg";
    private static string Svg(string inner = "<rect width='10' height='10' fill='#1F6FE5'/>", string attrs = "") =>
        $"<svg xmlns=\"{SvgNs}\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 10 10\" {attrs}>{inner}</svg>";
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[] BuildWebP()
    {
        var body = Encoding.ASCII.GetBytes("WEBPVP8L").Concat(new byte[] { 1, 0, 0, 0, 0x2F, 0, 0, 0, 0, 0 }).ToArray();
        return Encoding.ASCII.GetBytes("RIFF").Concat(BitConverter.GetBytes(body.Length)).Concat(body).ToArray();
    }

    // ------------------------------------------------------------------ reglas puras

    [Fact]
    public void Real_content_decides_the_type_for_png_jpeg_webp_and_svg()
    {
        Assert.Equal("image/png", BrandLogoRules.Inspect(Png).ContentType);
        Assert.Equal("image/jpeg", BrandLogoRules.Inspect(Jpeg).ContentType);
        Assert.Equal("image/webp", BrandLogoRules.Inspect(WebP).ContentType);
        Assert.Equal("image/svg+xml", BrandLogoRules.Inspect(Bytes(Svg())).ContentType);
        // con BOM, declaración XML y comentarios
        Assert.Equal("image/svg+xml", BrandLogoRules.Inspect([0xEF, 0xBB, 0xBF, .. Bytes("<?xml version=\"1.0\"?>\n<!-- hecho a mano -->\n" + Svg())]).ContentType);
        // el tipo declarado se tolera si coincide, si es genérico o si falta
        Assert.True(BrandLogoRules.Inspect(Png, "image/png").Ok);
        Assert.True(BrandLogoRules.Inspect(Png, "application/octet-stream").Ok);
        Assert.True(BrandLogoRules.Inspect(Jpeg, "image/jpg").Ok);
        Assert.True(BrandLogoRules.Inspect(Bytes(Svg()), "image/svg+xml; charset=utf-8").Ok);
    }

    [Fact]
    public void A_declared_image_type_that_does_not_match_the_content_is_a_415()
    {
        var r = BrandLogoRules.Inspect(Jpeg, "image/png");
        Assert.Equal((415, "El contenido del archivo (image/jpeg) no coincide con el tipo declarado (image/png)."), (r.StatusCode, r.Error));
        Assert.Equal(415, BrandLogoRules.Inspect(Bytes(Svg()), "image/webp").StatusCode);
    }

    [Fact]
    public void Content_that_is_not_an_image_is_a_415_whatever_it_claims_to_be()
    {
        foreach (var data in new[] { Bytes("hola, esto es un texto"), Bytes("<html><body>x</body></html>"), Bytes("%PDF-1.7 ..."), new byte[] { 1, 2, 3, 4, 5 }, "GIF89a...."u8.ToArray(), [0xFF, 0xFE, 0x3C, 0x00] })
        {
            var r = BrandLogoRules.Inspect(data, "image/png");
            Assert.Equal((415, BrandLogoRules.UnsupportedMessage), (r.StatusCode, r.Error));
        }
        Assert.Equal("Formato no admitido: el logo debe ser SVG, PNG, JPG o WebP.", BrandLogoRules.UnsupportedMessage);
    }

    [Fact]
    public void Empty_is_a_400_and_more_than_512_KB_is_a_413_even_when_the_content_is_valid()
    {
        var empty = BrandLogoRules.Inspect([]);
        Assert.Equal((400, "Seleccione un archivo de logo."), (empty.StatusCode, empty.Error));

        var exactly = Bytes(Svg() + new string(' ', BrandLogoRules.MaxBytes - Bytes(Svg()).Length));
        Assert.Equal(BrandLogoRules.MaxBytes, exactly.Length);
        Assert.True(BrandLogoRules.Inspect(exactly).Ok);

        var tooBig = Bytes(Svg() + new string(' ', BrandLogoRules.MaxBytes - Bytes(Svg()).Length + 1));
        var r = BrandLogoRules.Inspect(tooBig);
        Assert.Equal((413, "El logo supera el tamaño máximo de 512 KB."), (r.StatusCode, r.Error));
        Assert.Equal(413, BrandLogoRules.Inspect(Png.Concat(new byte[BrandLogoRules.MaxBytes]).ToArray()).StatusCode);
    }

    [Fact]
    public void Truncated_or_damaged_images_are_a_400()
    {
        Assert.Equal(400, BrandLogoRules.Inspect(Png[..40]).StatusCode);                               // PNG sin IEND
        Assert.Equal(400, BrandLogoRules.Inspect(Png[..8]).StatusCode);                                // solo la firma
        Assert.Equal(400, BrandLogoRules.Inspect(Jpeg[..10]).StatusCode);                              // JPEG sin EOI
        Assert.Equal(400, BrandLogoRules.Inspect(WebP[..14]).StatusCode);                              // RIFF incompleto
        Assert.Equal(BrandLogoRules.CorruptImageMessage, BrandLogoRules.Inspect(Png[..40]).Error);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "El SVG no se acepta: contiene el elemento <script>, que puede ejecutar código o cargar contenido externo.")]
    [InlineData("<foreignObject width='1' height='1'><div xmlns='http://www.w3.org/1999/xhtml'>x</div></foreignObject>", "El SVG no se acepta: contiene el elemento <foreignObject>, que puede ejecutar código o cargar contenido externo.")]
    [InlineData("<g><iframe src='#a'/></g>", "El SVG no se acepta: contiene el elemento <iframe>, que puede ejecutar código o cargar contenido externo.")]
    [InlineData("<a href='#x'><set attributeName='href' to='javascript:alert(1)'/></a>", "El SVG no se acepta: contiene el elemento <set>, que puede ejecutar código o cargar contenido externo.")]
    [InlineData("<rect onclick='alert(1)' width='1' height='1'/>", "El SVG no se acepta: el atributo 'onclick' ejecuta código.")]
    [InlineData("<rect ONMOUSEOVER='x' width='1' height='1'/>", "El SVG no se acepta: el atributo 'ONMOUSEOVER' ejecuta código.")]
    [InlineData("<a href='javascript:alert(1)'><rect width='1' height='1'/></a>", "El SVG no se acepta: el atributo 'href' contiene un enlace de script (javascript:).")]
    [InlineData("<a xlink:href='java&#9;script:alert(1)'><rect width='1' height='1'/></a>", "El SVG no se acepta: el atributo 'xlink:href' contiene un enlace de script (javascript:).")]
    [InlineData("<image href='https://evil.example/x.png' width='1' height='1'/>", "El SVG no se acepta: el atributo 'href' apunta fuera del archivo (solo se permiten referencias internas #id).")]
    [InlineData("<use xlink:href='otro.svg#a'/>", "El SVG no se acepta: el atributo 'xlink:href' apunta fuera del archivo (solo se permiten referencias internas #id).")]
    [InlineData("<image href='data:text/html;base64,PHNjcmlwdD4=' width='1' height='1'/>", "El SVG no se acepta: el atributo 'href' apunta fuera del archivo (solo se permiten referencias internas #id).")]
    [InlineData("<style>@import url(https://evil.example/a.css);</style>", "El SVG no se acepta: una hoja de estilos importa o referencia contenido externo.")]
    [InlineData("<style>.a{fill:url(https://evil.example/x)}</style>", "El SVG no se acepta: una hoja de estilos importa o referencia contenido externo.")]
    [InlineData("<rect style='fill:url(http://evil.example/x)' width='1' height='1'/>", "El SVG no se acepta: una hoja de estilos importa o referencia contenido externo.")]
    public void An_svg_with_active_content_or_external_references_is_a_400_with_the_exact_message(string inner, string message)
    {
        var r = BrandLogoRules.Inspect(Bytes(Svg(inner)));
        Assert.Equal((400, message), (r.StatusCode, r.Error));
    }

    [Fact]
    public void An_svg_with_a_doctype_or_entities_is_a_400()
    {
        var xxe = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>" + Svg("<text>&x;</text>");
        var bomb = "<!DOCTYPE svg [<!ENTITY a \"aaaa\"><!ENTITY b \"&a;&a;&a;&a;\">]>" + Svg("<text>&b;</text>");
        foreach (var s in new[] { xxe, bomb, "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">" + Svg() })
        {
            var r = BrandLogoRules.Inspect(Bytes(s));
            Assert.Equal((400, "El SVG no se acepta: declara DOCTYPE o entidades."), (r.StatusCode, r.Error));
        }
    }

    [Fact]
    public void Harmless_svg_features_are_accepted()
    {
        var inner = "<defs><linearGradient id='g'><stop offset='0' stop-color='#fff'/></linearGradient><symbol id='s'><rect width='1' height='1'/></symbol></defs>"
            + "<style>.a{fill:url(#g)} .b{fill:red}</style><use href='#s'/><use xlink:href='#s'/><rect class='a' style='fill:url(#g)' width='1' height='1'/>"
            + "<image href='data:image/png;base64,iVBORw0KGgo=' width='1' height='1'/><metadata><x xmlns='http://example.org/ns'>info</x></metadata>";
        Assert.True(BrandLogoRules.Inspect(Bytes(Svg(inner))).Ok, BrandLogoRules.Inspect(Bytes(Svg(inner))).Error);
    }

    [Fact]
    public void Broken_xml_is_a_400_and_a_non_svg_root_is_a_415()
    {
        var broken = BrandLogoRules.Inspect(Bytes("<svg xmlns=\"" + SvgNs + "\"><rect></svg>"));
        Assert.Equal((400, "El SVG no es un XML válido."), (broken.StatusCode, broken.Error));
        Assert.Equal(415, BrandLogoRules.Inspect(Bytes("<?xml version=\"1.0\"?><catalog><item/></catalog>")).StatusCode);
        Assert.Equal(415, BrandLogoRules.Inspect(Bytes("<svg><rect/></svg>")).StatusCode);   // sin el espacio de nombres SVG
    }

    [Fact]
    public void Slots_are_the_four_pieces()
    {
        Assert.Equal(new[] { "lockup", "lockup-inverted", "mark", "mark-inverted" }, BrandLogoSlots.All);
        Assert.True(BrandLogoSlots.IsValid("mark"));
        Assert.False(BrandLogoSlots.IsValid("Mark"));
        Assert.False(BrandLogoSlots.IsValid(null));
    }

    // ------------------------------------------------------------------ servicio

    private static Task<WmsFixture> FixtureAsync() => WmsFixture.CreateAsync(s => s.AddSingleton<BrandLogoService>());

    [Fact]
    public async Task Upload_replace_list_get_and_remove_a_logo()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<BrandLogoService>();
        Assert.Empty(await svc.ListAsync(default));

        var dto = await svc.SaveAsync("lockup", Png, "image/png", default);
        Assert.Equal(("lockup", "image/png", Png.Length), (dto.Slot, dto.ContentType, dto.SizeBytes));
        Assert.Equal(64, dto.ETag.Length);

        var svg = Bytes(Svg());
        var replaced = await svc.SaveAsync("lockup", svg, null, default);
        Assert.Equal(("image/svg+xml", svg.Length), (replaced.ContentType, replaced.SizeBytes));
        Assert.NotEqual(dto.ETag, replaced.ETag);
        Assert.Equal(1, await f.Db.TenantBrandLogos.CountAsync());   // una fila por compañía y ranura

        await svc.SaveAsync("mark-inverted", Jpeg, null, default);
        Assert.Equal(new[] { "lockup", "mark-inverted" }, (await svc.ListAsync(default)).Select(l => l.Slot));

        var file = await svc.GetAsync("lockup", default);
        Assert.Equal(("image/svg+xml", replaced.ETag), (file.ContentType, file.ETag));
        Assert.Equal(svg, file.Content);

        await svc.RemoveAsync("lockup", default);
        Assert.Equal(new[] { "mark-inverted" }, (await svc.ListAsync(default)).Select(l => l.Slot));
        var gone = await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync("lockup", default));
        Assert.Equal("Logo 'lockup' no encontrado.", gone.Message);
        // soft delete: la fila sigue (inactiva, sin binario) y subir otro logo la reactiva
        var row = await f.Db.TenantBrandLogos.AsNoTracking().SingleAsync(l => l.Slot == "lockup");
        Assert.False(row.IsActive);
        Assert.Empty(row.Content);
        await svc.SaveAsync("lockup", Png, null, default);
        Assert.Equal(2, (await svc.ListAsync(default)).Count);
        Assert.Equal(2, await f.Db.TenantBrandLogos.CountAsync());
    }

    [Fact]
    public async Task Invalid_uploads_fail_with_the_exact_status_and_message_and_save_nothing()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<BrandLogoService>();

        var empty = await Assert.ThrowsAsync<ValidationException>(() => svc.SaveAsync("mark", [], null, default));
        Assert.Equal((400, "Seleccione un archivo de logo."), (empty.StatusCode, empty.Message));
        Assert.Equal(new[] { empty.Message }, empty.Errors!["file"]);

        var big = await Assert.ThrowsAsync<PayloadTooLargeException>(() => svc.SaveAsync("mark", new byte[BrandLogoRules.MaxBytes + 1], null, default));
        Assert.Equal((413, "El logo supera el tamaño máximo de 512 KB."), (big.StatusCode, big.Message));

        var unsupported = await Assert.ThrowsAsync<UnsupportedMediaException>(() => svc.SaveAsync("mark", Bytes("MZ no soy una imagen"), "image/png", default));
        Assert.Equal((415, "Formato no admitido: el logo debe ser SVG, PNG, JPG o WebP."), (unsupported.StatusCode, unsupported.Message));

        var mismatch = await Assert.ThrowsAsync<UnsupportedMediaException>(() => svc.SaveAsync("mark", Png, "image/jpeg", default));
        Assert.Equal("El contenido del archivo (image/png) no coincide con el tipo declarado (image/jpeg).", mismatch.Message);

        var active = await Assert.ThrowsAsync<ValidationException>(() => svc.SaveAsync("mark", Bytes(Svg("<script>alert(1)</script>")), "image/svg+xml", default));
        Assert.Equal(400, active.StatusCode);

        var slot = await Assert.ThrowsAsync<NotFoundException>(() => svc.SaveAsync("banner", Png, null, default));
        Assert.Equal("Ranura de logo 'banner' no encontrada.", slot.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync("banner", default));
        await Assert.ThrowsAsync<NotFoundException>(() => svc.RemoveAsync("banner", default));
        var none = await Assert.ThrowsAsync<NotFoundException>(() => svc.RemoveAsync("mark", default));
        Assert.Equal("Logo 'mark' no encontrado.", none.Message);

        Assert.Equal(0, await f.Db.TenantBrandLogos.CountAsync());
    }

    [Fact]
    public async Task Logos_are_isolated_between_companies()
    {
        await using var f = await FixtureAsync();
        var svc = f.Get<BrandLogoService>();
        await svc.SaveAsync("lockup", Png, null, default);
        using (f.AsTenant(WmsFixture.OtherTenantId))
        {
            Assert.Empty(await svc.ListAsync(default));
            await Assert.ThrowsAsync<NotFoundException>(() => svc.GetAsync("lockup", default));
            await Assert.ThrowsAsync<NotFoundException>(() => svc.RemoveAsync("lockup", default));   // no puede quitar el de la otra
            var other = await svc.SaveAsync("lockup", Jpeg, null, default);
            Assert.Equal("image/jpeg", other.ContentType);
            Assert.Equal(WmsFixture.OtherTenantId, (await f.Db.TenantBrandLogos.SingleAsync(l => l.Slot == "lockup")).TenantId);
        }
        Assert.Equal("image/png", (await svc.GetAsync("lockup", default)).ContentType);   // el de la primera sigue intacto
        Assert.Equal(2, await f.Db.TenantBrandLogos.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public void The_entity_is_audited_without_the_binary_and_is_tenant_scoped()
    {
        Assert.Equal(EntityTypes.TenantLogo, typeof(TenantBrandLogo).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        Assert.Equal("TENANT_LOGO", EntityTypes.TenantLogo);
        Assert.NotNull(typeof(TenantBrandLogo).GetProperty(nameof(TenantBrandLogo.Content))!.GetCustomAttribute<NotAuditedAttribute>());
        Assert.True(typeof(ITenantScoped).IsAssignableFrom(typeof(TenantBrandLogo)));
        Assert.True(typeof(ISoftDeletable).IsAssignableFrom(typeof(TenantBrandLogo)));
    }

    // ------------------------------------------------------------------ controlador

    [Fact]
    public void Writes_need_admin_tenant_and_reads_only_a_session()
    {
        var t = typeof(BrandLogosController);
        Assert.Equal("api/v1/tenant/brand/logos", t.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(t.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        string Perm(string method) => t.GetMethod(method)!.GetCustomAttribute<RequirePermissionAttribute>()!.Policy;
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.AdminTenant, Perm(nameof(BrandLogosController.Put)));
        Assert.Equal(RequirePermissionAttribute.Prefix + PermissionCatalog.AdminTenant, Perm(nameof(BrandLogosController.Remove)));
        Assert.Empty(t.GetMethod(nameof(BrandLogosController.Get))!.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.Empty(t.GetMethod(nameof(BrandLogosController.List))!.GetCustomAttributes<RequirePermissionAttribute>());
        Assert.NotNull(t.GetMethod(nameof(BrandLogosController.Put))!.GetCustomAttribute<BrandLogoUploadLimitAttribute>());
    }

    private static BrandLogosController Controller(WmsFixture f)
    {
        var c = new BrandLogosController(f.Get<BrandLogoService>()) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        c.Request.ContentType = "multipart/form-data; boundary=x";
        return c;
    }

    private static IFormFile Form(byte[] data, string contentType) =>
        new FormFile(new MemoryStream(data), 0, data.Length, "file", "logo.bin") { Headers = new HeaderDictionary(), ContentType = contentType };

    [Fact]
    public async Task Reading_serves_the_file_with_nosniff_a_restrictive_csp_and_validators()
    {
        await using var f = await FixtureAsync();
        var c = Controller(f);
        var saved = await c.Put("lockup", Form(Bytes(Svg()), "image/svg+xml"), default);
        var result = Assert.IsType<FileContentResult>(await c.Get("lockup", default));
        Assert.Equal("image/svg+xml", result.ContentType);
        Assert.Equal($"\"{saved.ETag}\"", result.EntityTag!.ToString());
        Assert.NotNull(result.LastModified);
        var h = c.Response.Headers;
        Assert.Equal("nosniff", h.XContentTypeOptions.ToString());
        Assert.Equal("private, no-cache", h.CacheControl.ToString());
        var csp = h.ContentSecurityPolicy.ToString();
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("sandbox", csp);
        Assert.DoesNotContain("script-src", csp);
        Assert.Contains("inline; filename=\"logo-lockup.svg\"", h.ContentDisposition.ToString());
    }

    [Fact]
    public async Task Upload_without_a_file_is_a_400_and_a_big_file_a_413()
    {
        await using var f = await FixtureAsync();
        var c = Controller(f);
        var none = await Assert.ThrowsAsync<ValidationException>(() => c.Put("mark", null, default));
        Assert.Equal("Seleccione un archivo de logo.", none.Message);

        await Assert.ThrowsAsync<PayloadTooLargeException>(() => c.Put("mark", Form(new byte[BrandLogoRules.MaxBytes + 1], "image/png"), default));
    }

    [Fact]
    public async Task The_size_filter_rejects_a_declared_body_over_the_limit_before_reading_it()
    {
        var http = new DefaultHttpContext();
        http.Request.ContentLength = BrandLogoUploadLimitAttribute.MaxRequestBytes + 1;
        var ctx = new Microsoft.AspNetCore.Mvc.Filters.ResourceExecutingContext(
            new ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            [], []);
        var reached = false;
        Microsoft.AspNetCore.Mvc.Filters.ResourceExecutionDelegate next = () => { reached = true; return Task.FromResult<Microsoft.AspNetCore.Mvc.Filters.ResourceExecutedContext>(null!); };
        var ex = await Assert.ThrowsAsync<PayloadTooLargeException>(() => new BrandLogoUploadLimitAttribute().OnResourceExecutionAsync(ctx, next));
        Assert.Equal("El logo supera el tamaño máximo de 512 KB.", ex.Message);
        Assert.False(reached);

        http.Request.ContentLength = BrandLogoRules.MaxBytes;   // dentro del límite y sin multipart: pasa a la acción
        await new BrandLogoUploadLimitAttribute().OnResourceExecutionAsync(ctx, next);
        Assert.True(reached);

        // multipart roto (cuerpo vacío): 400 con su mensaje, no un 500
        reached = false;
        http.Request.ContentType = "multipart/form-data; boundary=x";
        http.Request.Body = new MemoryStream();
        var broken = await Assert.ThrowsAsync<ValidationException>(() => new BrandLogoUploadLimitAttribute().OnResourceExecutionAsync(ctx, next));
        Assert.Equal("La subida del logo llegó incompleta o mal formada. Vuelva a intentarlo.", broken.Message);
        Assert.False(reached);
    }
}
