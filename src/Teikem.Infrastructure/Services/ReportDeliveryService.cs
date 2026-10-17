using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Analytics;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Persistence;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 92 (P12) — arma el correo de un informe programado: título, período, la tabla (hasta ReportScheduleRules.MaxRows filas, con una nota si hay más) y la fila de totales. Todo el texto de datos va codificado
/// para HTML. Los importes salen con dos decimales, las fechas en la zona de la compañía y los sí/no en español.
/// </summary>
public static class ReportEmailBuilder
{
    public static string RangeLabel(string? mode) => mode switch
    {
        DateRangeModes.Last7 => "Últimos 7 días",
        DateRangeModes.Last30 => "Últimos 30 días",
        DateRangeModes.ThisMonth => "Este mes",
        _ => "Todo el historial",
    };

    public static string Subject(string companyName, string reportName, DateOnly today) => $"{companyName} · {reportName} · {today:yyyy-MM-dd}";

    public static string FormatValue(object? v, ReportColumn col, TimeZoneInfo zone)
    {
        switch (v)
        {
            case null: return string.Empty;
            case bool b: return b ? "Sí" : "No";
            case DateTime dt:
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dt, DateTimeKind.Utc), zone);
                return local.TimeOfDay == TimeSpan.Zero ? local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            case DateOnly d: return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case decimal m: return col.IsMoney ? m.ToString("C2", CultureInfo.GetCultureInfo("en-US")) : m.ToString("0.##", CultureInfo.InvariantCulture);
            case double or float: return Convert.ToDecimal(v, CultureInfo.InvariantCulture).ToString(col.IsMoney ? "C2" : "0.##", CultureInfo.GetCultureInfo("en-US"));
            case IFormattable f: return col.IsMoney ? f.ToString("C2", CultureInfo.GetCultureInfo("en-US")) : f.ToString(null, CultureInfo.InvariantCulture);
            default: return v.ToString() ?? string.Empty;
        }
    }

    public static string Html(string companyName, string reportName, string rangeMode, ReportRunResultDto result, TimeZoneInfo zone)
    {
        // solo se codifican los caracteres especiales de HTML: las tildes viajan tal cual (el correo va en UTF-8)
        static string E(string? s) => (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;color:#1b2430;font-size:14px\">");
        sb.Append($"<h2 style=\"margin:0 0 4px\">{E(reportName)}</h2>");
        sb.Append($"<p style=\"margin:0 0 14px;color:#5b6573\">{E(companyName)} · {E(RangeLabel(rangeMode))}</p>");
        if (result.Rows.Count == 0)
        {
            sb.Append("<p>No hay datos en el período.</p></div>");
            return sb.ToString();
        }
        sb.Append("<table style=\"border-collapse:collapse;width:100%\"><thead><tr>");
        foreach (var c in result.Columns)
            sb.Append($"<th style=\"text-align:{(IsNumber(c) ? "right" : "left")};padding:6px 8px;border-bottom:2px solid #d5dbe3;background:#f3f5f8;white-space:nowrap\">{E(c.Label)}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in result.Rows)
        {
            sb.Append("<tr>");
            foreach (var c in result.Columns)
            {
                row.TryGetValue(c.Key, out var v);
                sb.Append($"<td style=\"text-align:{(IsNumber(c) ? "right" : "left")};padding:5px 8px;border-bottom:1px solid #e6eaf0\">{E(FormatValue(v, c, zone))}</td>");
            }
            sb.Append("</tr>");
        }
        if (result.Totals is { Count: > 0 })
        {
            sb.Append("<tr>");
            foreach (var c in result.Columns)
            {
                result.Totals.TryGetValue(c.Key, out var v);
                sb.Append($"<td style=\"text-align:{(IsNumber(c) ? "right" : "left")};padding:6px 8px;font-weight:bold;border-top:2px solid #d5dbe3\">{E(FormatValue(v, c, zone))}</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        if (result.Total > result.Rows.Count)
            sb.Append($"<p style=\"color:#5b6573\">Se muestran {result.Rows.Count} de {result.Total} filas. Vea el resto en Teikem.</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private static bool IsNumber(ReportColumn c) => c.Type == DataFieldType.Number || c.IsMoney;
}

/// <summary>
/// Lote 92 (P12) — envío de los informes programados por correo. El trabajo `REPORT_DELIVERY` (cada 5 minutos, por compañía) llama RunDueAsync: por cada informe activo, propio de un usuario (no de sistema), con programación y
/// destinatarios, mira si ya pasó su última ocurrencia (hora local de la compañía) desde el último envío; si toca, lo corre COMO SU DUEÑO (con sus permisos, su visibilidad y su alcance de datos de almacenes y clientes), arma el
/// correo y lo manda a cada destinatario. El resultado queda en el informe (LastDeliveryStatus: SENT, SKIPPED sin correo configurado o dueño inactivo, FAILED si algún destinatario falló); un informe se intenta una vez por
/// ocurrencia, no en cada pasada del trabajo. «Enviar ahora» (SendNowAsync) lo manda ya, sin tocar la programación. Un informe que falla no impide a los demás.
/// </summary>
public sealed class ReportDeliveryService(TeikemDbContext db, ITenantContext tenant, ITenantClock clock, AnalyticsService analytics, ITransactionalEmailSender email)
{
    private TenantContext T => (TenantContext)tenant;

    /// <summary>Envía los informes que ya tocan. Devuelve cuántos se enviaron, cuántos se omitieron y cuántos fallaron.</summary>
    public async Task<(int Sent, int Skipped, int Failed)> RunDueAsync(CancellationToken ct)
    {
        var reports = await db.ReportDefinitions.AsTracking()
            .Where(r => r.IsActive && !r.IsSystem && r.ScheduleCron != null && r.DeliveryEmails != null && r.OwnerUserId != null).ToListAsync(ct);
        int sent = 0, skipped = 0, failed = 0;
        var now = clock.UtcNow;
        foreach (var r in reports)
        {
            if (ReportScheduleRules.TryParse(r.ScheduleCron) is not { } schedule) continue;
            if (r.LastDeliveryUtc is null)
            {
                r.LastDeliveryUtc = now;   // programación vieja sin base: empieza a contar desde ahora
                continue;
            }
            if (ReportScheduleRules.DueOccurrenceUtc(schedule, r.LastDeliveryUtc, now, clock.Zone) is not DateTime occurrence) continue;
            var result = await DeliverAsync(r, ct);
            r.LastDeliveryUtc = occurrence;
            r.LastDeliveryStatus = result.Status;
            r.LastDeliveryMessage = Trim(result.Message);
            switch (result.Status)
            {
                case ReportScheduleRules.StatusSent: sent++; break;
                case ReportScheduleRules.StatusSkipped: skipped++; break;
                default: failed++; break;
            }
        }
        await db.SaveChangesAsync(ct);
        return (sent, skipped, failed);
    }

    /// <summary>«Enviar ahora»: manda el informe a sus destinatarios en este momento. No cambia la programación ni el «último envío»; sí deja el resultado en el informe.</summary>
    public async Task<ReportDeliveryResultDto> SendNowAsync(int reportId, CancellationToken ct)
    {
        var r = await db.ReportDefinitions.AsTracking().Include(x => x.Shares).FirstOrDefaultAsync(x => x.ReportDefinitionId == reportId && x.IsActive, ct)
                ?? throw new NotFoundException("Vista", reportId);
        if (r.IsSystem) throw new ValidationException("report", ReportScheduleRules.SystemReportMessage);
        var caller = tenant.UserId;
        if (r.OwnerUserId != caller && !r.Shares.Any(s => s.CanEdit && s.UserId == caller)) throw new ForbiddenException("No tiene permiso para enviar este informe.");
        if (r.OwnerUserId is null) throw new ValidationException("report", ReportScheduleRules.NoOwnerMessage);
        if (string.IsNullOrWhiteSpace(r.DeliveryEmails)) throw new ValidationException("deliveryEmails", ReportScheduleRules.NoScheduleMessage);
        var result = await DeliverAsync(r, ct);
        r.LastDeliveryStatus = result.Status;
        r.LastDeliveryMessage = Trim("Manual: " + result.Message);
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<ReportDeliveryResultDto> DeliverAsync(Domain.Analytics.ReportDefinition r, CancellationToken ct)
    {
        var recipients = ReportScheduleRules.ParseEmails(r.DeliveryEmails);
        if (recipients.Count == 0) return new ReportDeliveryResultDto(ReportScheduleRules.StatusSkipped, ReportScheduleRules.NoScheduleMessage, recipients, 0);
        if (!email.IsConfigured) return new ReportDeliveryResultDto(ReportScheduleRules.StatusSkipped, "El envío de correo no está configurado.", recipients, 0);
        var ownerId = r.OwnerUserId!.Value;
        var ownerActive = await db.Users.AsNoTracking().AnyAsync(u => u.Id == ownerId && u.IsActive, ct);
        if (!ownerActive) return new ReportDeliveryResultDto(ReportScheduleRules.StatusSkipped, "El dueño del informe ya no está activo.", recipients, 0);

        var scope = await db.UserDataScopes.AsNoTracking().Where(s => s.UserId == ownerId).Select(s => new { Entity = s.ScopeEntity!.InternalCode, s.ScopeId }).ToListAsync(ct);
        var warehouses = scope.Where(s => s.Entity == EntityTypes.Warehouse).Select(s => s.ScopeId).ToList();
        var clients = scope.Where(s => s.Entity == EntityTypes.Client).Select(s => s.ScopeId).ToList();
        var company = await db.Tenants.AsNoTracking().Where(t => t.TenantId == r.TenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? "Teikem";

        ReportRunResultDto result;
        var (prevWh, prevCl, prevAdmin) = (T.ScopedWarehouseIds, T.ScopedClientIds, T.IsPlatformAdmin);
        using (T.As(r.TenantId, ownerId))
        {
            T.ScopedWarehouseIds = warehouses.Count > 0 ? warehouses : null;
            T.ScopedClientIds = clients.Count > 0 ? clients : null;
            T.IsPlatformAdmin = false;
            try
            {
                var range = ReportScheduleRules.IsValidRange(r.DeliveryRangeMode) ? ReportScheduleRules.NormalizeRange(r.DeliveryRangeMode) : ReportScheduleRules.DefaultRange;
                result = await analytics.RunReportAsync(r.ReportDefinitionId, new ReportRunRequest(range, null, null, 0, ReportScheduleRules.MaxRows), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is NotFoundException or ForbiddenException or ValidationException or InvalidOperationException)
            {
                return new ReportDeliveryResultDto(ReportScheduleRules.StatusFailed, "No se pudo correr el informe: " + ex.Message, recipients, 0);
            }
            finally
            {
                (T.ScopedWarehouseIds, T.ScopedClientIds, T.IsPlatformAdmin) = (prevWh, prevCl, prevAdmin);
            }
        }

        var mode = ReportScheduleRules.IsValidRange(r.DeliveryRangeMode) ? ReportScheduleRules.NormalizeRange(r.DeliveryRangeMode) : ReportScheduleRules.DefaultRange;
        var html = ReportEmailBuilder.Html(company, r.Name, mode, result, clock.Zone);
        var subject = ReportEmailBuilder.Subject(company, r.Name, clock.Today);
        var failed = new List<string>();
        foreach (var to in recipients)
        {
            try { await email.SendAsync(to, null, subject, html, ct, company); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { failed.Add(to); }
        }
        return failed.Count == 0
            ? new ReportDeliveryResultDto(ReportScheduleRules.StatusSent, $"Enviado a {recipients.Count} destinatario(s) con {result.Rows.Count} fila(s).", recipients, result.Rows.Count)
            : new ReportDeliveryResultDto(ReportScheduleRules.StatusFailed, "No se pudo enviar a: " + string.Join(", ", failed), recipients, result.Rows.Count);
    }

    private static string? Trim(string? s) => s is null ? null : s.Length <= 400 ? s : s[..400];
}
