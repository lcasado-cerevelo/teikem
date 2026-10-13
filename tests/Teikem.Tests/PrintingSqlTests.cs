using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Common;
using Teikem.Domain.Constants;
using Teikem.Domain.Printing;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence.Scripts;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 43 (F-G1) — espejo SQL de impresoras y plantillas (sección nueva e idempotente al final de logistica-db-update.sql, con el IF y el
/// CREATE TABLE en la misma línea), modelo EF con los mismos índices filtrados, auditoría y permiso printers.manage.
/// </summary>
public class PrintingSqlTests
{
    private const string SectionTitle = "2026-10-12 (d) — Impresoras y plantillas de label (Lote 43, F-G1).";

    private static string UpdateSql()
        => File.ReadAllText(Path.Combine(DatabaseInitializer.ResolveRepoRoot(null), "Diseño", "logistica-db-update.sql")).Replace("\r\n", "\n");

    private static string Section()
    {
        var sql = UpdateSql();
        var start = sql.IndexOf(SectionTitle, StringComparison.Ordinal);
        Assert.True(start > 0, "Falta la sección 2026-10-12 (d) en logistica-db-update.sql.");
        Assert.True(start > sql.IndexOf("2026-10-12 (c) — Índice de la lista de órdenes por compañía", StringComparison.Ordinal), "La sección debe ir después de la (c).");
        // la sección llega hasta la siguiente (otras secciones posteriores —de cualquier rama— pueden llevar DROP o UNIQUE y no son de esta prueba)
        var next = sql.IndexOf("\n/* ----", start, StringComparison.Ordinal);
        return next < 0 ? sql[start..] : sql[start..next].TrimEnd('\n') + "\n";
    }

    [Fact]
    public void Update_sql_creates_both_tables_with_the_guard_on_the_same_line()
    {
        var s = Section();
        Assert.Contains("\nIF OBJECT_ID(N'dbo.Printer', N'U') IS NULL CREATE TABLE dbo.Printer (\n", s);
        Assert.Contains("\nIF OBJECT_ID(N'dbo.LabelTemplate', N'U') IS NULL CREATE TABLE dbo.LabelTemplate (\n", s);
        Assert.Contains("SystemName     NVARCHAR(200) NULL,", s);
        Assert.Contains("CONSTRAINT CK_Printer_Purpose CHECK (Purpose IN ('LABEL', 'DOCUMENT')),", s);
        Assert.Contains("CONSTRAINT CK_Printer_Connection CHECK (ConnectionType IN ('BROWSER', 'QZ_TRAY')),", s);
        Assert.Contains("CONSTRAINT CK_Printer_Media CHECK ((Purpose = 'LABEL' AND MediaSize IN ('4X6', '4X4', '4X2')) OR (Purpose = 'DOCUMENT' AND MediaSize = 'LETTER'))", s);
        Assert.Contains("LayoutJson      NVARCHAR(2000) NOT NULL,", s);
        Assert.Contains("CREATE UNIQUE INDEX UX_Printer_Name ON dbo.Printer(TenantId, Name) WHERE IsActive = 1;", s);
        Assert.Contains("CREATE UNIQUE INDEX UX_Printer_Default ON dbo.Printer(TenantId, Purpose) WHERE IsDefault = 1 AND IsActive = 1;", s);
        Assert.Contains("CREATE UNIQUE INDEX UX_LabelTemplate_Name ON dbo.LabelTemplate(TenantId, Name) WHERE IsActive = 1;", s);
        Assert.Contains("CREATE UNIQUE INDEX UX_LabelTemplate_Default ON dbo.LabelTemplate(TenantId) WHERE IsDefault = 1 AND IsActive = 1;", s);
        // cada CREATE INDEX con su guarda
        var lines = s.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            if (lines[i].TrimStart().StartsWith("CREATE UNIQUE INDEX", StringComparison.Ordinal))
                Assert.StartsWith("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = ", lines[i - 1]);
        Assert.DoesNotContain("DELETE", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP ", s, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("GO\n", s);
    }

    [Fact]
    public void Update_sql_adds_the_company_setting_the_client_flag_entity_types_and_the_permission_for_template_and_cloned_admins()
    {
        var s = Section();
        Assert.Contains("IF COL_LENGTH('dbo.Tenant', 'PrintLabelsDirect') IS NULL\n    ALTER TABLE dbo.Tenant ADD PrintLabelsDirect BIT NOT NULL CONSTRAINT DF_Tenant_PrintLabelsDirect DEFAULT 0;", s);
        Assert.Contains("IF COL_LENGTH('dbo.Client', 'AutoPrintInvoice') IS NULL\n    ALTER TABLE dbo.Client ADD AutoPrintInvoice BIT NOT NULL CONSTRAINT DF_Client_AutoPrintInvoice DEFAULT 0;", s);
        Assert.Contains("(N'PRINTER',", s);
        Assert.Contains("(N'LABEL_TEMPLATE',", s);
        Assert.Contains("ON t.Entity = N'EntityType' AND t.InternalCode = s.Code", s);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.Permission WHERE Code = 'printers.manage')", s);
        Assert.Contains("(SELECT LookupCodeId FROM dbo.LookupCode WHERE Entity = 'PermissionCategory' AND InternalCode = 'ADMIN')", s);
        Assert.Contains("N'{\"es\":\"Gestionar impresoras y plantillas de label\",\"en\":\"Manage printers & label templates\"}'", s);
        // plantilla (TenantId NULL) Y roles ya clonados de cada compañía (sin filtrar por TenantId), solo agregando
        Assert.Contains("JOIN dbo.Permission p ON p.Code = 'printers.manage'\nWHERE r.Name IN ('TenantAdmin')\n  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermission rp WHERE rp.RoleId = r.RoleId AND rp.PermissionId = p.PermissionId);", s);
        Assert.DoesNotContain("UPDATE dbo.Tenant", s, StringComparison.OrdinalIgnoreCase);   // nadie nace con el ajuste encendido
    }

    [Fact]
    public void Permission_is_admin_only_for_tenant_admin_and_mirrored()
    {
        var p = Assert.Single(PermissionCatalog.All, x => x.Code == "printers.manage");
        Assert.Equal("ADMIN", p.Category);
        Assert.Contains(PermissionCatalog.PrintersManage, PermissionCatalog.RoleTemplates["TenantAdmin"]);
        foreach (var role in PermissionCatalog.RoleTemplates.Keys.Where(r => r != "TenantAdmin"))
            Assert.DoesNotContain(PermissionCatalog.PrintersManage, PermissionCatalog.RoleTemplates[role]);
    }

    [Fact]
    public void Entities_are_audited_with_their_entity_type_and_technical_columns_are_not()
    {
        Assert.Equal(EntityTypes.Printer, typeof(Printer).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        Assert.Equal(EntityTypes.LabelTemplate, typeof(LabelTemplate).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        foreach (var t in new[] { typeof(Printer), typeof(LabelTemplate) })
        foreach (var name in new[] { "PublicId", "RowVersion", "CreatedAtUtc", "UpdatedAtUtc" })
            Assert.NotNull(t.GetProperty(name)!.GetCustomAttribute<NotAuditedAttribute>());
    }

    [Theory]
    [InlineData(typeof(Printer), "UX_Printer_Name", new[] { "TenantId", "Name" }, "[IsActive] = 1")]
    [InlineData(typeof(Printer), "UX_Printer_Default", new[] { "TenantId", "Purpose" }, "[IsDefault] = 1 AND [IsActive] = 1")]
    [InlineData(typeof(Printer), "UQ_Printer_PublicId", new[] { "PublicId" }, null)]
    [InlineData(typeof(LabelTemplate), "UX_LabelTemplate_Name", new[] { "TenantId", "Name" }, "[IsActive] = 1")]
    [InlineData(typeof(LabelTemplate), "UX_LabelTemplate_Default", new[] { "TenantId" }, "[IsDefault] = 1 AND [IsActive] = 1")]
    public void Ef_model_mirrors_the_unique_indexes(Type clr, string name, string[] columns, string? filter)
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var entity = db.Model.FindEntityType(clr)!;
        Assert.NotNull(entity.GetQueryFilter());   // filtro global de tenant
        var index = entity.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == name);
        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
        Assert.Equal(columns, index.Properties.Select(p => p.Name));
        Assert.Equal(filter, index.GetFilter());
        Assert.True(entity.FindProperty("RowVersion")!.IsConcurrencyToken);
    }
}

/// <summary>
/// Lote 43 (F-G1) — permiso y módulo exactos de cada acción de los controladores de impresión (patrón de WmsControllerSecurityTests).
/// </summary>
public class PrintingControllerSecurityTests
{
    private const string Station = "orders.view|trips.scan|trips.view|inventory.view|printers.manage";

    private static readonly Dictionary<(Type Controller, string Action), string> Expected = new()
    {
        [(typeof(PrintersController), nameof(PrintersController.List))] = PermissionCatalog.PrintersManage,
        [(typeof(PrintersController), nameof(PrintersController.Create))] = PermissionCatalog.PrintersManage,
        [(typeof(PrintersController), nameof(PrintersController.Get))] = PermissionCatalog.PrintersManage,
        [(typeof(PrintersController), nameof(PrintersController.Update))] = PermissionCatalog.PrintersManage,
        [(typeof(PrintersController), nameof(PrintersController.Deactivate))] = PermissionCatalog.PrintersManage,
        [(typeof(PrintersController), nameof(PrintersController.Reactivate))] = PermissionCatalog.PrintersManage,
        [(typeof(PrintersController), nameof(PrintersController.MakeDefault))] = PermissionCatalog.PrintersManage,
        [(typeof(LabelTemplatesController), nameof(LabelTemplatesController.List))] = PermissionCatalog.PrintersManage,
        [(typeof(LabelTemplatesController), nameof(LabelTemplatesController.Create))] = PermissionCatalog.PrintersManage,
        [(typeof(LabelTemplatesController), nameof(LabelTemplatesController.Get))] = PermissionCatalog.PrintersManage,
        [(typeof(LabelTemplatesController), nameof(LabelTemplatesController.Update))] = PermissionCatalog.PrintersManage,
        [(typeof(LabelTemplatesController), nameof(LabelTemplatesController.Deactivate))] = PermissionCatalog.PrintersManage,
        [(typeof(LabelTemplatesController), nameof(LabelTemplatesController.Reactivate))] = PermissionCatalog.PrintersManage,
        [(typeof(LabelTemplatesController), nameof(LabelTemplatesController.MakeDefault))] = PermissionCatalog.PrintersManage,
        [(typeof(PrintingController), nameof(PrintingController.StationOptions))] = Station,
        [(typeof(PrintingController), nameof(PrintingController.Settings))] = PermissionCatalog.PrintersManage,
        [(typeof(PrintingController), nameof(PrintingController.UpdateSettings))] = PermissionCatalog.PrintersManage,
        [(typeof(PrintingController), nameof(PrintingController.QzCertificate))] = Station,
        [(typeof(PrintingController), nameof(PrintingController.QzSign))] = Station,
    };

    private static readonly Type[] Controllers = { typeof(PrintersController), typeof(LabelTemplatesController), typeof(PrintingController) };

    private static List<MethodInfo> Actions(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()).ToList();

    [Fact]
    public void Each_action_requires_exactly_its_permission_and_the_map_covers_all()
    {
        foreach (var c in Controllers)
        foreach (var a in Actions(c))
        {
            Assert.True(Expected.TryGetValue((c, a.Name), out var perm), $"{c.Name}.{a.Name} sin permiso esperado en el mapa.");
            var policies = a.GetCustomAttributes<RequirePermissionAttribute>().Select(x => x.Policy).ToList();
            Assert.Equal(new[] { RequirePermissionAttribute.Prefix + perm }, policies);
            foreach (var p in a.GetParameters()) Assert.False(string.Equals(p.Name, "tenantId", StringComparison.OrdinalIgnoreCase));
        }
        Assert.Equal(Expected.Count, Controllers.Sum(c => Actions(c).Count));
        Assert.Equal(Station, PrintingController.StationOptionsPermission);
    }

    [Fact]
    public void Every_printing_controller_requires_the_system_module()
    {
        foreach (var c in Controllers)
            Assert.Equal(new[] { ModuleKeys.System }, c.GetCustomAttributes<RequireModuleAttribute>(inherit: true).Select(a => a.ModuleKey));
    }
}

/// <summary>
/// Lote 43 (F-G1) — firma de QZ Tray: sin certificado o sin clave, 409 claro; con ellos, SHA512withRSA verificable con el certificado;
/// límites del texto; la clave nunca aparece en el certificado devuelto.
/// </summary>
public class QzSigningServiceTests
{
    private static (string CertPem, string KeyPem, RSA Key) SelfSigned()
    {
        var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=Teikem QZ prueba", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return (cert.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem(), rsa);
    }

    private static QzSigningService Service(Dictionary<string, string?> values)
        => new(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void Without_material_it_is_a_clear_409()
    {
        var svc = Service(new());
        Assert.False(svc.IsConfigured);
        var e = Assert.Throws<ConflictException>(() => svc.GetCertificate());
        Assert.Equal(QzSigningService.NotConfiguredMessage, e.Message);
        Assert.Throws<ConflictException>(() => svc.Sign(new QzSignRequest("abc")));
        // una clave sin certificado tampoco basta
        Assert.False(Service(new() { ["Printing:Qz:PrivateKey"] = SelfSigned().KeyPem }).IsConfigured);
        // una clave dañada se trata como "sin configurar" (nunca un 500)
        Assert.False(Service(new() { ["Printing:Qz:Certificate"] = SelfSigned().CertPem, ["Printing:Qz:PrivateKey"] = "-----BEGIN PRIVATE KEY-----\nxx\n-----END PRIVATE KEY-----" }).IsConfigured);
    }

    [Fact]
    public void Signs_with_sha512_rsa_verifiable_with_the_certificate()
    {
        var (certPem, keyPem, _) = SelfSigned();
        var svc = Service(new() { ["Printing:Qz:Certificate"] = certPem, ["Printing:Qz:PrivateKey"] = keyPem });
        Assert.True(svc.IsConfigured);
        var cert = svc.GetCertificate().Certificate;
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", cert);
        Assert.DoesNotContain("PRIVATE KEY", cert);

        const string toSign = "0f4d1c6e8a7b";
        var sig = svc.Sign(new QzSignRequest(toSign));
        Assert.Equal("SHA512", sig.Algorithm);
        using var x509 = X509Certificate2.CreateFromPem(cert);
        using var pub = x509.GetRSAPublicKey()!;
        Assert.True(pub.VerifyData(Encoding.UTF8.GetBytes(toSign), Convert.FromBase64String(sig.Signature), HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Request_is_required_and_limited()
    {
        var (certPem, keyPem, _) = SelfSigned();
        var svc = Service(new() { ["Printing:Qz:Certificate"] = certPem, ["Printing:Qz:PrivateKey"] = keyPem });
        Assert.Equal("Falta el texto a firmar.", Assert.Throws<ValidationException>(() => svc.Sign(new QzSignRequest(""))).Message);
        Assert.Equal("Falta el texto a firmar.", Assert.Throws<ValidationException>(() => svc.Sign(null)).Message);
        Assert.Equal("El texto a firmar no puede exceder 8192 caracteres.", Assert.Throws<ValidationException>(() => svc.Sign(new QzSignRequest(new string('a', 8193)))).Message);
        Assert.NotEmpty(svc.Sign(new QzSignRequest(new string('a', 8192))).Signature);
    }

    [Fact]
    public void Reads_files_and_escaped_newlines_from_environment_variables()
    {
        var (certPem, keyPem, _) = SelfSigned();
        var dir = Directory.CreateTempSubdirectory("qz-test-");
        try
        {
            var certPath = Path.Combine(dir.FullName, "qz.crt");
            File.WriteAllText(certPath, certPem);
            var svc = Service(new() { ["Printing:Qz:CertificatePath"] = certPath, ["Printing:Qz:PrivateKey"] = keyPem.Replace("\n", "\\n") });
            Assert.True(svc.IsConfigured);
        }
        finally { dir.Delete(true); }
    }
}
