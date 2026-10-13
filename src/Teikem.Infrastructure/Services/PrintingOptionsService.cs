using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Printing;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 43 (F-G1) — opciones de impresión de una estación (este navegador) y ajustes de impresión de la compañía.
/// - Opciones: impresoras y plantillas ACTIVAS de la compañía (con su predeterminada marcada), el label integrado, el ajuste «Imprimir
///   labels directo a la impresora» y si el servidor puede firmar para QZ Tray. Lo lee quien imprime (órdenes, escaneo, inventario)
///   aunque no administre impresoras. No devuelve notas. La configuración de la estación vive en el navegador (D-FG2).
/// - Ajustes: Tenant.PrintLabelsDirect (printers.manage).
/// </summary>
public sealed class PrintingOptionsService(TeikemDbContext db, ITenantContext tenant, QzSigningService qz)
{
    public async Task<PrintStationOptionsDto> GetStationOptionsAsync(CancellationToken ct)
    {
        var printers = await db.Printers.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Purpose).ThenBy(p => p.Name).ThenBy(p => p.PrinterId)
            .Select(p => new PrinterOptionDto(p.PublicId, p.Name, p.Model, p.Purpose, p.ConnectionType, p.MediaSize, p.RotateMedia, p.IsDefault, p.SystemName))
            .ToListAsync(ct);
        var templates = await db.LabelTemplates.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name).ThenBy(t => t.LabelTemplateId)
            .ToListAsync(ct);
        var direct = (await GetSettingsAsync(ct)).PrintLabelsDirect;
        return new PrintStationOptionsDto(printers, templates.Select(LabelTemplateService.ToDto).ToList(), LabelTemplateService.ToDto(LabelLayout.BuiltIn),
            direct, qz.IsConfigured);
    }

    public async Task<PrintingSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var direct = await db.Tenants.AsNoTracking().Where(t => t.TenantId == tenantId).Select(t => (bool?)t.PrintLabelsDirect).FirstOrDefaultAsync(ct)
                     ?? throw new NotFoundException("Compañía", null, true);
        return new PrintingSettingsDto(direct);
    }

    public async Task<PrintingSettingsDto> UpdateSettingsAsync(PrintingSettingsRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct) ?? throw new NotFoundException("Compañía", null, true);
        if (req.PrintLabelsDirect is { } direct && t.PrintLabelsDirect != direct)
        {
            t.PrintLabelsDirect = direct;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct);
        }
        return new PrintingSettingsDto(t.PrintLabelsDirect);
    }
}
