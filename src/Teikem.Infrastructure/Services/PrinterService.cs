using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Printing;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 43 (F-G1) — impresoras de la compañía (perfil de impresión; el servidor nunca se conecta a ellas: imprime el navegador, directo
/// con QZ Tray o con el PDF del navegador).
/// - SystemName: nombre exacto de la impresora en el sistema de la computadora (QZ Tray imprime por él); vacío = el nombre.
/// - Nombre obligatorio (recortado, ≤ 80) y único entre las ACTIVAS (409; UX_Printer_Name es la segunda barrera): el nombre de una
///   inactiva se puede reusar.
/// - Uso LABEL o DOCUMENT, fijo después del alta (400 si el PATCH intenta cambiarlo). Conexión BROWSER o QZ_TRAY. Tamaño según el uso.
/// - La primera impresora activa de un uso nace predeterminada. «Hacer predeterminada» quita la anterior en una transacción con DOS
///   SaveChanges (primero la quita, luego pone la nueva: EF podría ordenar los UPDATE al revés y chocar con UX_Printer_Default).
/// - Desactivar la predeterminada se permite y deja el uso sin predeterminada (aplica la integrada, D-FG6); reactivar no la vuelve
///   predeterminada. Nunca DELETE.
/// - Rutas por PublicId; una impresora de otra compañía es 404 (filtro global de tenant).
/// </summary>
public sealed class PrinterService(TeikemDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<PrinterDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Printers.AsNoTracking();
        if (!includeInactive) q = q.Where(p => p.IsActive);
        var rows = await q.OrderBy(p => p.Purpose).ThenBy(p => p.Name).ThenBy(p => p.PrinterId).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<PrinterDto> GetAsync(Guid publicId, CancellationToken ct) => ToDto(await LoadAsync(publicId, ct, track: false));

    public async Task<PrinterDto> CreateAsync(PrinterRequest req, CancellationToken ct)
    {
        var tenantId = ((TenantContext)tenant).RequireTenantId();
        var (name, nameError) = PrintingRules.NormalizeName(req.Name, PrintingRules.PrinterNameRequiredMessage);
        if (nameError is not null) throw new ValidationException("name", nameError);
        var (purpose, purposeError) = PrintingRules.NormalizePurpose(req.Purpose);
        if (purposeError is not null) throw new ValidationException("purpose", purposeError);
        var (connection, connectionError) = PrintingRules.NormalizeConnection(req.ConnectionType);
        if (connectionError is not null) throw new ValidationException("connectionType", connectionError);
        var (media, mediaError) = PrintingRules.NormalizeMedia(purpose!, req.MediaSize);
        if (mediaError is not null) throw new ValidationException("mediaSize", mediaError);
        var (model, modelError) = PrintingRules.NormalizeOptional(req.Model, PrintingRules.ModelMaxLength, PrintingRules.ModelTooLongMessage);
        if (modelError is not null) throw new ValidationException("model", modelError);
        var (notes, notesError) = PrintingRules.NormalizeOptional(req.Notes, PrintingRules.NotesMaxLength, PrintingRules.NotesTooLongMessage);
        if (notesError is not null) throw new ValidationException("notes", notesError);
        var (systemName, systemNameError) = PrintingRules.NormalizeOptional(req.SystemName, PrintingRules.SystemNameMaxLength, PrintingRules.SystemNameTooLongMessage);
        if (systemNameError is not null) throw new ValidationException("systemName", systemNameError);

        if (await db.Printers.AnyAsync(p => p.IsActive && p.Name == name, ct)) throw new ConflictException(PrintingRules.DuplicatePrinterMessage);
        var hasDefault = await db.Printers.AnyAsync(p => p.IsActive && p.IsDefault && p.Purpose == purpose, ct);

        var printer = new Printer
        {
            PublicId = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name!,
            SystemName = systemName,
            Model = model,
            Notes = notes,
            Purpose = purpose!,
            ConnectionType = connection!,
            MediaSize = media!,
            RotateMedia = req.RotateMedia ?? false,
            IsDefault = !hasDefault,   // la primera activa del uso nace predeterminada
            IsActive = true,
        };
        db.Printers.Add(printer);
        await db.SaveGuardedAsync(PrintingRules.DuplicatePrinterMessage, ct);
        return ToDto(printer);
    }

    /// <summary>Edición: null = sin cambio; "" en Model/Notes/SystemName los quita. El uso no se cambia (400). rowVersion opcional (409).</summary>
    public async Task<PrinterDto> UpdateAsync(Guid publicId, PrinterRequest req, CancellationToken ct)
    {
        var printer = await LoadAsync(publicId, ct);
        db.ApplyRowVersion(printer, req.RowVersion);

        if (PrintingRules.ChangesPurpose(printer.Purpose, req.Purpose)) throw new ValidationException("purpose", PrintingRules.PurposeImmutableMessage);
        if (req.Name is not null)
        {
            var (name, error) = PrintingRules.NormalizeName(req.Name, PrintingRules.PrinterNameRequiredMessage);
            if (error is not null) throw new ValidationException("name", error);
            if (printer.IsActive && !string.Equals(name, printer.Name, StringComparison.Ordinal)
                && await db.Printers.AnyAsync(p => p.IsActive && p.Name == name && p.PrinterId != printer.PrinterId, ct))
                throw new ConflictException(PrintingRules.DuplicatePrinterMessage);
            printer.Name = name!;
        }
        if (req.ConnectionType is not null)
        {
            var (connection, error) = PrintingRules.NormalizeConnection(req.ConnectionType);
            if (error is not null) throw new ValidationException("connectionType", error);
            printer.ConnectionType = connection!;
        }
        if (req.MediaSize is not null)
        {
            var (media, error) = PrintingRules.NormalizeMedia(printer.Purpose, req.MediaSize);
            if (error is not null) throw new ValidationException("mediaSize", error);
            printer.MediaSize = media!;
        }
        if (req.Model is not null)
        {
            var (model, error) = PrintingRules.NormalizeOptional(req.Model, PrintingRules.ModelMaxLength, PrintingRules.ModelTooLongMessage);
            if (error is not null) throw new ValidationException("model", error);
            printer.Model = model;
        }
        if (req.Notes is not null)
        {
            var (notes, error) = PrintingRules.NormalizeOptional(req.Notes, PrintingRules.NotesMaxLength, PrintingRules.NotesTooLongMessage);
            if (error is not null) throw new ValidationException("notes", error);
            printer.Notes = notes;
        }
        if (req.SystemName is not null)
        {
            var (systemName, error) = PrintingRules.NormalizeOptional(req.SystemName, PrintingRules.SystemNameMaxLength, PrintingRules.SystemNameTooLongMessage);
            if (error is not null) throw new ValidationException("systemName", error);
            printer.SystemName = systemName;
        }
        if (req.RotateMedia is { } rotate) printer.RotateMedia = rotate;

        await db.SaveGuardedAsync(PrintingRules.DuplicatePrinterMessage, ct);
        return ToDto(printer);
    }

    /// <summary>
    /// Baja lógica o reactivación. Desactivar la predeterminada la deja sin predeterminada (D-FG6). Reactivar con el nombre ya usado por
    /// otra activa es 409; reactivar no la vuelve predeterminada.
    /// </summary>
    public async Task<PrinterDto> SetActiveAsync(Guid publicId, bool active, CancellationToken ct)
    {
        var printer = await LoadAsync(publicId, ct);
        if (printer.IsActive == active) return ToDto(printer);
        if (active)
        {
            if (await db.Printers.AnyAsync(p => p.IsActive && p.Name == printer.Name && p.PrinterId != printer.PrinterId, ct))
                throw new ConflictException(PrintingRules.DuplicatePrinterMessage);
            printer.IsActive = true;
        }
        else
        {
            printer.IsActive = false;
            printer.IsDefault = false;
        }
        await db.SaveGuardedAsync(PrintingRules.DuplicatePrinterMessage, ct);
        return ToDto(printer);
    }

    /// <summary>
    /// La hace predeterminada de su uso: quita la anterior y pone esta, en una transacción con dos SaveChanges (el orden de los UPDATE
    /// importa por UX_Printer_Default). 409 si está inactiva.
    /// </summary>
    public Task<PrinterDto> MakeDefaultAsync(Guid publicId, CancellationToken ct)
        => db.RunInTransactionAsync(async ct2 =>
        {
            var printer = await LoadAsync(publicId, ct2);
            if (!printer.IsActive) throw new ConflictException(PrintingRules.PrinterInactiveDefaultMessage);
            if (printer.IsDefault) return ToDto(printer);

            var previous = await db.Printers
                .Where(p => p.IsDefault && p.Purpose == printer.Purpose && p.PrinterId != printer.PrinterId)
                .ToListAsync(ct2);
            if (previous.Count > 0)
            {
                foreach (var p in previous) p.IsDefault = false;
                await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            }
            printer.IsDefault = true;
            await db.SaveGuardedAsync(DbExtensions.ConcurrencyMessage, ct2);
            return ToDto(printer);
        }, ct);

    private async Task<Printer> LoadAsync(Guid publicId, CancellationToken ct, bool track = true)
    {
        var q = track ? db.Printers : db.Printers.AsNoTracking();
        return await q.FirstOrDefaultAsync(p => p.PublicId == publicId, ct)
               ?? throw new NotFoundException("Impresora", null, true);
    }

    internal static PrinterDto ToDto(Printer p) => new(
        p.PublicId, p.Name, p.Model, p.Purpose, p.ConnectionType, p.MediaSize, p.RotateMedia, p.Notes, p.IsDefault, p.IsActive,
        Convert.ToBase64String(p.RowVersion ?? Array.Empty<byte>()), p.SystemName);
}
