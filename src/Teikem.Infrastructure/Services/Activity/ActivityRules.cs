using System.Globalization;
using System.Text.Json;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>Metadatos de un evento del catálogo ActivityEventType (ExtraJson del LookupCode).</summary>
public sealed record ActivityEventMeta(string? Module, bool Mandatory, bool DefaultOn);

/// <summary>
/// Lote 7A (P1) — reglas puras del feed "Actividad reciente" (maestro, módulo 12, 'Pulso del día — diseño consolidado'):
/// ventana → instante UTC, tope de página, mapeo de movimientos y transiciones a códigos del catálogo, bandera obligatorio /
/// encendido por defecto, módulo visible y orden. Sin BD: se prueban con xunit (ActivityRulesTests).
/// </summary>
public static class ActivityRules
{
    /// <summary>Dominio del catálogo de eventos en LookupCode.Entity.</summary>
    public const string CatalogDomain = LookupDomains.ActivityEventType;

    public const string Window24h = "24h";
    public const string Window48h = "48h";
    public const string WindowToday = "today";

    /// <summary>Máximo de filas por página (y por defecto): 50, con "Ver más" en el panel.</summary>
    public const int MaxPageSize = 50;

    public const string PageTooLarge = "El máximo por página es 50.";
    public const string InvalidWindow = "La ventana debe ser 24h, 48h o today.";
    public static string ModuleForbidden(string module) => $"No tiene permiso para ver la actividad del módulo {module}.";

    /// <summary>Orden de las pestañas (maestro: Almacén, Operación, Contabilidad).</summary>
    private static readonly string[] ModuleOrder = { BusinessModules.Warehouse, BusinessModules.Operations, BusinessModules.Accounting };

    /// <summary>
    /// Motivos manuales del maestro (DAMAGE, LOSS, FOUND, EXPIRED, OTHER): INVENTORY_ADJUSTED aunque el ajuste lleve documento
    /// de origen (el FOUND de un faltante de compra lleva Ref PURCHASE_ORDER).
    /// </summary>
    public static readonly IReadOnlySet<string> ManualAdjustmentReasons = new HashSet<string>(
        new[] { AdjustmentReasons.Damage, AdjustmentReasons.Loss, AdjustmentReasons.Found, AdjustmentReasons.Expired, AdjustmentReasons.Other },
        StringComparer.OrdinalIgnoreCase);

    // ================================================================ ventana y página

    /// <summary>
    /// Ventana → instante UTC desde el que se leen eventos: 24h (default, también con null o vacío), 48h o 'today' (desde la
    /// medianoche de hoy en la zona indicada; UTC si no se indica). Cualquier otro valor → 400 InvalidWindow. Lote 15: el
    /// servicio pasa la zona de la compañía (ITenantClock, hora de Puerto Rico); ya no hay una zona UTC propia de Actividad.
    /// </summary>
    public static DateTime FromUtc(string? window, DateTime nowUtc, TimeZoneInfo? zone = null)
    {
        var now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        switch ((window ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case Window24h: return now.AddHours(-24);
            case Window48h: return now.AddHours(-48);
            case WindowToday:
            {
                var tz = zone ?? TimeZoneInfo.Utc;
                var local = TimeZoneInfo.ConvertTimeFromUtc(now, tz);
                var midnight = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
                // Medianoche inexistente (cambio de horario): se avanza a la primera hora válida del día.
                while (tz.IsInvalidTime(midnight)) midnight = midnight.AddMinutes(30);
                return TimeZoneInfo.ConvertTimeToUtc(midnight, tz);
            }
            default: throw new ValidationException("window", InvalidWindow);
        }
    }

    /// <summary>skip negativo → 0; take ≤ 0 → 50; take &gt; 50 → 400 'El máximo por página es 50.'.</summary>
    public static (int Skip, int Take) Page(int skip, int take)
    {
        if (take > MaxPageSize) throw new ValidationException("take", PageTooLarge);
        return (Math.Max(0, skip), take <= 0 ? MaxPageSize : take);
    }

    /// <summary>Orden del feed: más reciente primero; empates estables por código, tipo de entidad y id.</summary>
    public static IReadOnlyList<ActivityEventDto> Sort(IEnumerable<ActivityEventDto> events)
        => events.OrderByDescending(e => e.OccurredAtUtc)
            .ThenBy(e => e.Code, StringComparer.Ordinal)
            .ThenBy(e => e.EntityType, StringComparer.Ordinal)
            .ThenByDescending(e => e.EntityId)
            .ToList();

    /// <summary>Ordena, cuenta y recorta la página (skip/take ya normalizados con Page).</summary>
    public static (int Total, IReadOnlyList<ActivityEventDto> Items) Paginate(IEnumerable<ActivityEventDto> events, int skip, int take)
    {
        var sorted = Sort(events);
        return (sorted.Count, sorted.Skip(skip).Take(take).ToList());
    }

    // ================================================================ catálogo

    /// <summary>
    /// ExtraJson del LookupCode ActivityEventType: {"module":"WAREHOUSE","mandatory":true,"defaultOn":true}. Sin JSON o
    /// inválido → opcional y encendido; defaultOn ausente → encendido.
    /// </summary>
    public static ActivityEventMeta ParseMeta(string? extraJson)
    {
        if (string.IsNullOrWhiteSpace(extraJson)) return new ActivityEventMeta(null, false, true);
        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new ActivityEventMeta(null, false, true);
            string? module = null;
            bool mandatory = false, defaultOn = true;
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.NameEquals("module") && p.Value.ValueKind == JsonValueKind.String) module = p.Value.GetString();
                else if (p.NameEquals("mandatory") && p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) mandatory = p.Value.GetBoolean();
                else if (p.NameEquals("defaultOn") && p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) defaultOn = p.Value.GetBoolean();
            }
            return new ActivityEventMeta(module, mandatory, defaultOn);
        }
        catch (JsonException)
        {
            return new ActivityEventMeta(null, false, true);
        }
    }

    /// <summary>
    /// ¿Se muestra un evento? Un obligatorio siempre; un opcional solo si no se pidió 'solo obligatorios' y está encendido por
    /// defecto (BIN_MOVED nace apagado; la preferencia guardada por usuario llega con las notificaciones).
    /// </summary>
    public static bool IsShown(bool mandatory, bool defaultOn, bool onlyMandatory)
        => mandatory || (!onlyMandatory && defaultOn);

    /// <summary>
    /// Aplica el LookupCodeOverride del tenant (maestro L54) a los metadatos base de un evento. El módulo y la bandera de
    /// obligatorio son de la semilla base: un tenant no puede degradar un obligatorio. Deshabilitar (IsEnabled = 0) apaga un
    /// opcional (null = no se muestra); un obligatorio se sigue mostrando. El encendido por defecto se toma del CustomExtraJson
    /// solo si trae "defaultOn"; si no, queda el de la base.
    /// </summary>
    public static ActivityEventMeta? ApplyOverride(ActivityEventMeta baseMeta, bool isEnabled, string? customExtraJson)
    {
        if (!isEnabled && !baseMeta.Mandatory) return null;
        var defaultOn = baseMeta.DefaultOn;
        if (!string.IsNullOrWhiteSpace(customExtraJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(customExtraJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("defaultOn", out var d)
                    && d.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    defaultOn = d.GetBoolean();
            }
            catch (JsonException)
            {
                // JSON inválido en el override: se conserva el encendido de la base.
            }
        }
        return baseMeta with { DefaultOn = defaultOn };
    }

    // ================================================================ módulos

    /// <summary>Módulo del tenant (TenantModule) que debe estar encendido para ver la pestaña; null = sin requisito.</summary>
    public static string? TenantModuleFor(string businessModule)
        => string.Equals(businessModule, BusinessModules.Warehouse, StringComparison.OrdinalIgnoreCase) ? ModuleKeys.WmsLotSerial : null;

    /// <summary>Posición de un módulo de negocio en las pestañas (desconocidos al final, por nombre).</summary>
    public static int ModuleRank(string module)
    {
        var i = Array.FindIndex(ModuleOrder, m => string.Equals(m, module, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? ModuleOrder.Length : i;
    }

    /// <summary>
    /// Módulo a leer: el pedido si es visible (sin distinguir mayúsculas) o 403 ModuleForbidden; sin pedir, el primero
    /// visible; null si el usuario no ve ninguno.
    /// </summary>
    public static string? ResolveModule(string? requested, IReadOnlyList<string> visibleModules)
    {
        if (string.IsNullOrWhiteSpace(requested)) return visibleModules.Count > 0 ? visibleModules[0] : null;
        var wanted = requested.Trim().ToUpperInvariant();
        return visibleModules.FirstOrDefault(m => string.Equals(m, wanted, StringComparison.OrdinalIgnoreCase))
               ?? throw new ForbiddenException(ModuleForbidden(wanted));
    }

    // ================================================================ mapeos de Almacén

    /// <summary>
    /// ADJUSTMENT del ledger → evento por motivo (todo cambio de saldo fuera del flujo normal es obligatorio):
    /// RECEIPT_VARIANCE y COUNT_VARIANCE por su nombre; un motivo manual (DAMAGE, LOSS, FOUND, EXPIRED, OTHER) →
    /// INVENTORY_ADJUSTED con o sin documento de origen (el FOUND de un faltante de compra aparece además como
    /// PO_SHORTAGE_RESOLVED); cualquier otro motivo que no asigne el sistema (PO_SHORTAGE, un código nuevo del catálogo) sin
    /// documento de origen → INVENTORY_ADJUSTED. PICK_BATCH_REVERSAL (motivo de sistema) y el PO_SHORTAGE de un faltante (con
    /// Ref) ya los cuentan PICK_CANCELLED y PO_SHORTAGE_RESOLVED: null.
    /// </summary>
    public static string? AdjustmentEventCode(string? reasonCode, bool hasOperationRef)
    {
        if (string.IsNullOrWhiteSpace(reasonCode)) return null;
        var reason = reasonCode.Trim().ToUpperInvariant();
        if (reason == AdjustmentReasons.ReceiptVariance) return ActivityEvents.ReceiptVariance;
        if (reason == AdjustmentReasons.CountVariance) return ActivityEvents.CountVariance;
        if (Teikem.Domain.Wms.AdjustmentRules.SystemReasons.Contains(reason)) return null;
        return ManualAdjustmentReasons.Contains(reason) || !hasOperationRef ? ActivityEvents.InventoryAdjusted : null;
    }

    /// <summary>TRANSFER del ledger: entre almacenes distintos → INVENTORY_TRANSFERRED; dentro del mismo → BIN_MOVED.</summary>
    public static string TransferEventCode(int? fromWarehouseId, int? toWarehouseId)
        => fromWarehouseId.HasValue && toWarehouseId.HasValue && fromWarehouseId.Value != toWarehouseId.Value
            ? ActivityEvents.InventoryTransferred
            : ActivityEvents.BinMoved;

    /// <summary>
    /// Transición de EntityStatusHistory → evento de Almacén (maestro, tabla 'Catálogo inicial — Almacén'); null si la
    /// transición no produce evento. En WAREHOUSE_TASK el tipo de tarea decide el DONE (PUTAWAY, REPLENISH); cualquier
    /// tarea CANCELLED → TASK_CANCELLED.
    /// </summary>
    public static string? StatusEventCode(string entityTypeCode, string toStatusCode, string? taskTypeCode = null)
    {
        var entity = (entityTypeCode ?? string.Empty).ToUpperInvariant();
        var to = (toStatusCode ?? string.Empty).ToUpperInvariant();
        return entity switch
        {
            EntityTypes.Receipt => to switch
            {
                ReceiptStatuses.Received or ReceiptStatuses.ReceivedWithVariance => ActivityEvents.ReceiptConfirmed,   // Lote 13
                ReceiptStatuses.Putaway => ActivityEvents.ReceiptPutawayDone,
                _ => null,
            },
            EntityTypes.Asn => to == AsnStatuses.Cancelled ? ActivityEvents.AsnCancelled : null,
            EntityTypes.CycleCount => to switch
            {
                CycleCountStatuses.Counted => ActivityEvents.CountFinished,
                CycleCountStatuses.Reconciled or CycleCountStatuses.ReconciledVariance => ActivityEvents.CountReconciled,   // Lote 14 (D7)
                _ => null,
            },
            EntityTypes.PickBatch => to switch
            {
                PickBatchStatuses.Collected => ActivityEvents.PickCollected,
                PickBatchStatuses.Packed => ActivityEvents.PickPacked,
                PickBatchStatuses.Cancelled => ActivityEvents.PickCancelled,
                _ => null,
            },
            EntityTypes.PurchaseOrder => to switch
            {
                PurchaseOrderStatuses.Sent => ActivityEvents.PoSent,
                PurchaseOrderStatuses.Received => ActivityEvents.PoReceived,
                PurchaseOrderStatuses.Cancelled => ActivityEvents.PoCancelled,
                _ => null,
            },
            EntityTypes.CrossDockPlan => to == CrossDockStatuses.Completed ? ActivityEvents.CrossDockCompleted : null,
            EntityTypes.Warehouse => to == WarehouseStatuses.Inactive ? ActivityEvents.WarehouseDeactivated : null,
            EntityTypes.WarehouseTask => to switch
            {
                WarehouseTaskStatuses.Cancelled => ActivityEvents.TaskCancelled,
                WarehouseTaskStatuses.Done => (taskTypeCode ?? string.Empty).ToUpperInvariant() switch
                {
                    WarehouseTaskTypes.Putaway => ActivityEvents.PutawayDone,
                    WarehouseTaskTypes.Replenish => ActivityEvents.ReplenishDone,
                    _ => null,
                },
                _ => null,
            },
            _ => null,
        };
    }

    // ================================================================ textos cortos del detalle

    /// <summary>Une las partes no vacías con ' · '; null si no queda ninguna.</summary>
    public static string? Detail(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        return kept.Count == 0 ? null : string.Join(" · ", kept);
    }

    public static bool IsEnglish(string? lang) => string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);

    /// <summary>'1 línea' / '3 líneas' ('1 line' / '3 lines' en inglés).</summary>
    public static string Lines(int count, string? lang)
        => IsEnglish(lang) ? (count == 1 ? "1 line" : $"{count} lines") : (count == 1 ? "1 línea" : $"{count} líneas");

    /// <summary>Cantidad sin ceros sobrantes (cultura invariante); con signo explícito si signed.</summary>
    public static string Qty(decimal qty, bool signed = false)
    {
        var text = qty.ToString("0.####", CultureInfo.InvariantCulture);
        return signed && qty > 0 ? "+" + text : text;
    }

    /// <summary>'Diferencia neta +2' / 'Net variance +2'.</summary>
    public static string NetVariance(decimal qty, string? lang)
        => (IsEnglish(lang) ? "Net variance " : "Diferencia neta ") + Qty(qty, signed: true);
}
