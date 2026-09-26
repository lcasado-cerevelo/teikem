using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Conteo de lo que impide dar de baja un almacén (D26): inventario en mano o reservado y documentos abiertos.
/// Lo arma WarehouseService bajo el bloqueo del almacén y del rango de saldos; la regla solo decide y redacta.
/// </summary>
public sealed record WarehouseUsage(
    decimal QtyOnHand,
    decimal QtyReserved,
    int OpenReceipts,
    int OpenCycleCounts,
    int OpenTasks,
    int CollectedPickBatches,
    int OpenCrossDockPlans,
    int ActiveAppointments);

/// <summary>
/// Lote 6 (P1) — reglas puras de Almacenes y ubicaciones (R2, R3, R6). Sin BD: el servicio las aplica y el manual y la FAQ
/// citan los mensajes exactos (public const o métodos estáticos, cultura invariante).
/// - Códigos de almacén, zona y muelle: letras, números, guion y guion bajo, en mayúsculas, máximo 30.
/// - Código de posición: explícito (máximo 40, mismo alfabeto) o compuesto pasillo-rack-nivel-posición ('A01-R02-N3-P04').
/// - Baja de almacén definitiva (INACTIVE terminal) solo vacío y sin documentos abiertos; baja de posición solo sin
///   inventario ni tareas abiertas; baja de zona solo sin posiciones activas; baja de muelle solo sin citas vigentes.
/// - Almacén por defecto: el único activo del tenant (con más de uno, el llamador debe indicarlo).
/// </summary>
public static class WarehouseRules
{
    public const int CodeMaxLength = 30;
    public const int BinCodeMaxLength = 40;
    public const int BinPartMaxLength = 20;
    public const int NameMaxLength = 150;
    public const int ZoneNameMaxLength = 120;
    public const int Line1MaxLength = 200;
    public const int CityMaxLength = 100;
    public const int StateMaxLength = 100;
    public const int PostalCodeMaxLength = 20;

    /// <summary>País por defecto del almacén (LookupCode Country).</summary>
    public const string DefaultCountry = "PR";

    // ---------------------------------------------------------------- mensajes exactos

    public const string CodeInvalidMessage = "El código solo admite letras, números, guion y guion bajo (máximo 30).";
    public const string CodeRequiredMessage = "El código es obligatorio.";
    public const string NameRequiredMessage = "El nombre es obligatorio.";
    public const string BinCodeRequiredMessage = "Indique el código de la posición o su pasillo/rack/nivel/posición.";
    public const string BinCodeInvalidMessage = "El código de la posición solo admite letras, números, guion y guion bajo (máximo 40).";
    public const string BinPartInvalidMessage = "Pasillo, rack, nivel y posición solo admiten letras, números, guion y guion bajo (máximo 20 cada uno).";
    public const string WarehouseCodeImmutableMessage = "El código del almacén no se puede cambiar.";
    public const string ZoneCodeImmutableMessage = "El código de la zona no se puede cambiar.";
    public const string BinCodeImmutableMessage = "El código y la zona de la posición no se pueden cambiar.";
    public const string DockCodeImmutableMessage = "El código del muelle no se puede cambiar.";
    public const string ZoneRequiredMessage = "Indique la zona de la posición.";
    public const string DockTypeRequiredMessage = "Indique el tipo de muelle (INBOUND, OUTBOUND o BOTH).";
    public const string DockStatusRequiredMessage = "Indique el estatus del muelle (FREE, OCCUPIED o MAINTENANCE).";

    public const string DuplicateWarehouseMessage = "Ya existe un almacén con ese código.";
    public const string DuplicateZoneMessage = "Ya existe una zona con ese código en el almacén.";
    public const string DuplicateBinMessage = "Ya existe una posición con ese código en el almacén.";
    public const string DuplicateDockMessage = "Ya existe un muelle con ese código en el almacén.";

    public const string ZoneHasActiveBins = "La zona tiene posiciones activas; desactívelas primero.";
    public const string DockHasAppointments = "El muelle tiene citas agendadas o en curso.";
    public const string MaxWeight = "La capacidad de peso debe ser mayor que cero.";
    public const string MaxWeightPrecision = "La capacidad de peso admite como máximo 9 enteros y 3 decimales.";
    public const string WarehouseInactiveMessage = "El almacén está dado de baja; solo se consulta.";
    public const string ZoneInactiveMessage = "La zona está inactiva; reactívela primero.";
    public const string DockInactiveMessage = "El muelle está inactivo; reactívelo primero.";
    public const string BinHasOpenTasks = "La posición tiene tareas de almacén abiertas; complételas o cancélelas antes de desactivarla.";

    /// <summary>409 'El almacén {code} tiene inventario o documentos abiertos; no se puede dar de baja.' (Errors por tipo).</summary>
    public static string WarehouseNotEmpty(string code) => $"El almacén {code} tiene inventario o documentos abiertos; no se puede dar de baja.";

    /// <summary>409 'La posición {code} tiene inventario; no se puede desactivar.'</summary>
    public static string BinNotEmpty(string code) => $"La posición {code} tiene inventario; no se puede desactivar.";

    /// <summary>400 'Tipo de zona desconocido: '{x}'.'</summary>
    public static string UnknownZoneType(string? zoneType) => $"Tipo de zona desconocido: '{zoneType}'.";

    /// <summary>400 'Tipo de muelle desconocido: '{x}'.'</summary>
    public static string UnknownDockType(string? dockType) => $"Tipo de muelle desconocido: '{dockType}'.";

    /// <summary>400 'País desconocido: '{x}'.'</summary>
    public static string UnknownCountry(string? country) => $"País desconocido: '{country}'.";

    /// <summary>400 'Estatus de muelle no permitido: '{x}'. Use FREE, OCCUPIED o MAINTENANCE.'</summary>
    public static string DockStatusNotManual(string? status) => $"Estatus de muelle no permitido: '{status}'. Use FREE, OCCUPIED o MAINTENANCE.";

    /// <summary>400 '{campo} admite como máximo {n} caracteres.'</summary>
    public static string TooLong(string field, int max) => $"{field} admite como máximo {max.ToString(CultureInfo.InvariantCulture)} caracteres.";

    // ---------------------------------------------------------------- estatus manuales de muelle

    /// <summary>Estatus de muelle que un usuario puede fijar a mano (la llegada de una cita también ocupa el muelle).</summary>
    public static readonly IReadOnlySet<string> ManualDockStatuses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DockStatuses.Free, DockStatuses.Occupied, DockStatuses.Maintenance };

    public static bool IsManualDockStatus(string? code) => !string.IsNullOrWhiteSpace(code) && ManualDockStatuses.Contains(code.Trim());

    // ---------------------------------------------------------------- códigos

    /// <summary>
    /// Código de almacén, zona o muelle: recortado y en mayúsculas; solo [A-Z0-9_-], máximo 30.
    /// Vacío → (null, 'El código es obligatorio.'); inválido → (null, CodeInvalidMessage).
    /// </summary>
    public static (string? Code, string? Error) NormalizeCode(string? raw)
    {
        var code = raw?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) return (null, CodeRequiredMessage);
        if (code.Length > CodeMaxLength || !IsCodeText(code)) return (null, CodeInvalidMessage);
        return (code, null);
    }

    /// <summary>
    /// Código compuesto de posición: une con '-' las partes no vacías (recortadas, en mayúsculas) en el orden
    /// pasillo-rack-nivel-posición → 'A01-R02-N3-P04'. Sin partes → (null, BinCodeRequiredMessage); parte inválida o de
    /// más de 20 → BinPartInvalidMessage; resultado de más de 40 → BinCodeInvalidMessage.
    /// </summary>
    public static (string? Code, string? Error) ComposeBinCode(string? aisle, string? rack, string? level, string? position)
    {
        var parts = new List<string>(4);
        foreach (var raw in new[] { aisle, rack, level, position })
        {
            var (part, error) = NormalizeBinPart(raw);
            if (error is not null) return (null, error);
            if (part is not null) parts.Add(part);
        }
        if (parts.Count == 0) return (null, BinCodeRequiredMessage);
        var code = string.Join('-', parts);
        return code.Length > BinCodeMaxLength ? (null, BinCodeInvalidMessage) : (code, null);
    }

    /// <summary>
    /// Código de posición al crear: el explícito (máximo 40, [A-Z0-9_-]) si llega; si no, el compuesto de sus partes.
    /// Las partes se validan siempre (se guardan en sus columnas).
    /// </summary>
    public static (string? Code, string? Error) ResolveBinCode(string? code, string? aisle, string? rack, string? level, string? position)
    {
        foreach (var raw in new[] { aisle, rack, level, position })
            if (NormalizeBinPart(raw).Error is string partError) return (null, partError);

        var explicitCode = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(explicitCode)) return ComposeBinCode(aisle, rack, level, position);
        if (explicitCode.Length > BinCodeMaxLength || !IsCodeText(explicitCode)) return (null, BinCodeInvalidMessage);
        return (explicitCode, null);
    }

    /// <summary>Parte de la posición (pasillo, rack, nivel o posición): null si vacía; máximo 20, [A-Z0-9_-].</summary>
    public static (string? Part, string? Error) NormalizeBinPart(string? raw)
    {
        var part = raw?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(part)) return (null, null);
        if (part.Length > BinPartMaxLength || !IsCodeText(part)) return (null, BinPartInvalidMessage);
        return (part, null);
    }

    /// <summary>Capacidad de peso: null = sin límite; &gt; 0 y dentro de DECIMAL(12,3).</summary>
    public static string? ValidateMaxWeight(decimal? maxWeightKg)
    {
        if (maxWeightKg is not decimal w) return null;
        if (w <= 0) return MaxWeight;
        if (decimal.Round(w, 3) != w || w >= 1_000_000_000m) return MaxWeightPrecision;
        return null;
    }

    private static bool IsCodeText(string s)
    {
        foreach (var c in s)
            if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
        return true;
    }

    // ---------------------------------------------------------------- baja y almacén por defecto

    /// <summary>
    /// Motivos que impiden la baja del almacén, por tipo (vacío = procede). Claves estables para Errors del 409:
    /// inventory, receipts, cycleCounts, tasks, pickBatches, crossDockPlans, appointments.
    /// </summary>
    public static IDictionary<string, string[]> DeactivationBlockers(WarehouseUsage u)
    {
        var errors = new Dictionary<string, string[]>();
        if (u.QtyOnHand != 0 || u.QtyReserved != 0)
            errors["inventory"] = new[] { $"Inventario en mano {Qty(u.QtyOnHand)}, reservado {Qty(u.QtyReserved)}." };
        if (u.OpenReceipts > 0) errors["receipts"] = new[] { $"Recibos abiertos: {u.OpenReceipts}." };
        if (u.OpenCycleCounts > 0) errors["cycleCounts"] = new[] { $"Conteos cíclicos sin reconciliar: {u.OpenCycleCounts}." };
        if (u.OpenTasks > 0) errors["tasks"] = new[] { $"Tareas de almacén abiertas: {u.OpenTasks}." };
        if (u.CollectedPickBatches > 0) errors["pickBatches"] = new[] { $"Recolecciones sin empacar: {u.CollectedPickBatches}." };
        if (u.OpenCrossDockPlans > 0) errors["crossDockPlans"] = new[] { $"Planes de cruce de muelle abiertos: {u.OpenCrossDockPlans}." };
        if (u.ActiveAppointments > 0) errors["appointments"] = new[] { $"Citas de muelle agendadas o en curso: {u.ActiveAppointments}." };
        return errors;
    }

    /// <summary>Almacén por defecto: el único activo del tenant; con cero o más de uno, null (el llamador exige indicarlo).</summary>
    public static int? DefaultWarehouse(IReadOnlyCollection<int> activeWarehouseIds)
    {
        if (activeWarehouseIds is null) return null;
        var distinct = activeWarehouseIds.Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    private static string Qty(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);
}
