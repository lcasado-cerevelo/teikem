using System.Globalization;
using Teikem.Domain.Constants;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 27 (Rentas R1) — reglas puras de la renta: mensajes (los del plan, tal cual, y los nuevos del lote), qué se puede hacer en
/// cada estatus, fechas, tarifas, "por vencer"/"vencida" (datos calculados, no estatus) y la posición EN-RENTA. Sin BD.
/// </summary>
public static class RentalRules
{
    public const int MaxPageSize = 200;
    /// <summary>Máximo de equipos activos por renta.</summary>
    public const int MaxLines = 200;
    public const int MaxNotesLength = 1000;
    public const int MaxContractNumberLength = 80;
    public const int MaxReasonLength = 300;

    /// <summary>Zona y posición donde vive la existencia rentada de cada almacén (se crean a demanda, RentalBinResolver).</summary>
    public const string RentalZoneCode = "RENT";
    public const string RentalZoneName = "En renta";
    public const string RentalBinCode = "EN-RENTA";

    // ---------------------------------------------------------------- mensajes del plan (exactos)

    public const string NotFound = "Renta";
    public const string LocationRequired = "Indique la localidad del cliente donde estará el equipo.";
    public const string LocationNotOfClient = "La localidad no pertenece al cliente de la renta.";
    public const string PickupBeforeStart = "La fecha de recogido no puede ser anterior a la de inicio.";
    public static string OnlyOwnEquipment(string sku) => $"Solo se rentan equipos propios; {sku} pertenece a un cliente.";
    public static string NotSerialTracked(string sku) => $"El producto {sku} no se controla por serie; solo se rentan equipos con número de serie.";
    public static string SerialInRental(string serial, string rentalNumber) => $"La serie {serial} ya está en la renta {rentalNumber}.";
    public static string AlreadyDispatched(string rentalNumber) => $"La renta {rentalNumber} ya fue despachada; no se puede modificar.";
    public const string NoLines = "La renta no tiene equipos; agregue al menos uno.";
    public const string NegativeRate = "La tarifa no puede ser negativa.";
    public const string CancelNotAllowed = "Solo se cancela una renta en Borrador o Programada; para terminarla registre la devolución.";
    public const string ExtendNotAllowed = "Solo se extiende una renta Programada o En renta.";
    public static string NewPickupNotLater(DateOnly current)
        => $"La nueva fecha de recogido debe ser posterior a la actual ({current.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}).";
    public const string ExtensionReasonRequired = "Indique el motivo de la extensión.";

    // ---------------------------------------------------------------- mensajes nuevos del lote

    public const string ClientRequired = "Indique el cliente de la renta.";
    public const string StartDateRequired = "Indique la fecha de inicio de la renta.";
    public const string PickupDateRequired = "Indique la fecha de recogido.";
    public const string LocationInactive = "La localidad está dada de baja; elija otra.";
    public const string ClientImmutable = "El cliente de la renta no se cambia; cancele la renta y cree otra.";
    public static string ImmutableField(string field) => $"El campo '{field}' no se puede modificar.";
    public const string ProductRequired = "Indique el producto del equipo.";
    public const string SerialsRequired = "Indique al menos un número de serie.";
    public static readonly string TooManyLines = $"Una renta admite como máximo {MaxLines} equipos.";
    public static readonly string NotesTooLong = $"Las notas admiten como máximo {MaxNotesLength} caracteres.";
    public static readonly string ContractNumberTooLong = $"El número de contrato admite como máximo {MaxContractNumberLength} caracteres.";
    public static readonly string ReasonTooLong = $"El motivo admite como máximo {MaxReasonLength} caracteres.";
    public const string NegativeDeliveryCost = "El costo de transporte estimado no puede ser negativo.";
    public const string RateAmountRequired = "Indique el monto de la tarifa.";
    public const string FrequencyRequired = "Indique la frecuencia de cobro: DAILY, WEEKLY, MONTHLY o ONE_TIME.";
    public static string UnknownFrequency(string value) => $"Frecuencia de cobro desconocida: '{value}'. Use DAILY, WEEKLY, MONTHLY o ONE_TIME.";
    public static string UnknownCurrency(string value) => $"Moneda desconocida: '{value}'.";
    public const string DatesLockedByExtensions = "La renta ya tiene extensiones; la fecha de recogido se cambia con una extensión.";
    public const string WarehouseLockedByLines = "Quite los equipos de la renta antes de cambiar el almacén de origen.";
    public static string NotDraftToSchedule(string rentalNumber) => $"Solo se programa una renta en Borrador; la renta {rentalNumber} no lo está.";
    public static string NotScheduledToDispatch(string rentalNumber) => $"Solo se despacha una renta Programada; programe la renta {rentalNumber} primero.";
    public static string Cancelled(string rentalNumber) => $"La renta {rentalNumber} está cancelada; solo se consulta.";
    public static string RentalZoneCodeTaken(string warehouseCode)
        => $"El almacén {warehouseCode} ya tiene una zona {RentalZoneCode} que no es de rentas; cámbiele el código para poder despachar rentas.";
    public static string RentalBinCodeTaken(string warehouseCode)
        => $"El almacén {warehouseCode} ya tiene una posición {RentalBinCode} fuera de la zona En renta; cámbiele el código para poder despachar rentas.";
    public const string LineInactive = "El equipo ya no está en la renta.";

    // ---------------------------------------------------------------- estatus

    /// <summary>Antes del despacho (Borrador o Programada) la renta se edita: datos, equipos y tarifas.</summary>
    public static bool IsEditable(string? statusCode) => statusCode is RentalStatuses.Draft or RentalStatuses.Scheduled;

    /// <summary>Abierta = Programada o En renta (cuenta para "por vencer" y "vencida").</summary>
    public static bool IsOpen(string? statusCode) => statusCode is RentalStatuses.Scheduled or RentalStatuses.OnRent;

    public static bool CanCancel(string? statusCode) => statusCode is RentalStatuses.Draft or RentalStatuses.Scheduled;

    public static bool CanExtend(string? statusCode) => statusCode is RentalStatuses.Scheduled or RentalStatuses.OnRent;

    /// <summary>
    /// Mensaje (422) cuando la renta ya no se puede modificar: despachada o devuelta → AlreadyDispatched; cancelada → Cancelled;
    /// null si se edita.
    /// </summary>
    public static string? EditError(string? statusCode, string rentalNumber) => statusCode switch
    {
        RentalStatuses.Draft or RentalStatuses.Scheduled => null,
        RentalStatuses.Cancelled => Cancelled(rentalNumber),
        _ => AlreadyDispatched(rentalNumber),
    };

    /// <summary>Mensaje (422) al programar fuera de Borrador; null si se puede.</summary>
    public static string? ScheduleError(string? statusCode, string rentalNumber) => statusCode switch
    {
        RentalStatuses.Draft => null,
        RentalStatuses.Cancelled => Cancelled(rentalNumber),
        RentalStatuses.OnRent or RentalStatuses.Returned => AlreadyDispatched(rentalNumber),
        _ => NotDraftToSchedule(rentalNumber),
    };

    /// <summary>Mensaje (422) al despachar fuera de Programada; null si se puede.</summary>
    public static string? DispatchError(string? statusCode, string rentalNumber) => statusCode switch
    {
        RentalStatuses.Scheduled => null,
        RentalStatuses.Cancelled => Cancelled(rentalNumber),
        RentalStatuses.OnRent or RentalStatuses.Returned => AlreadyDispatched(rentalNumber),
        _ => NotScheduledToDispatch(rentalNumber),
    };

    // ---------------------------------------------------------------- fechas

    /// <summary>La fecha de recogido no puede ser anterior a la de inicio (el mismo día vale). null = válidas.</summary>
    public static string? ValidateDates(DateOnly start, DateOnly pickup) => pickup < start ? PickupBeforeStart : null;

    /// <summary>Días hasta el recogido (negativo = vencida).</summary>
    public static int DaysToPickup(DateOnly pickup, DateOnly today) => pickup.DayNumber - today.DayNumber;

    /// <summary>Vencida = abierta (Programada o En renta) con la fecha de recogido ANTES de hoy (dato calculado, no estatus).</summary>
    public static bool IsOverdue(string? statusCode, DateOnly pickup, DateOnly today) => IsOpen(statusCode) && pickup < today;

    /// <summary>Por vencer = abierta con la fecha de recogido entre hoy y hoy + días (ambos inclusive).</summary>
    public static bool IsDueWithin(string? statusCode, DateOnly pickup, DateOnly today, int days)
        => IsOpen(statusCode) && pickup >= today && pickup <= today.AddDays(Math.Max(0, days));

    /// <summary>Extensión: la nueva fecha debe ser posterior a la vigente. null = válida.</summary>
    public static string? ValidateExtension(DateOnly currentPickup, DateOnly newPickup) => newPickup > currentPickup ? null : NewPickupNotLater(currentPickup);

    /// <summary>La versión de tarifa que abre una extensión empieza el día siguiente al recogido anterior.</summary>
    public static DateOnly ExtensionRateStart(DateOnly previousPickup) => previousPickup.AddDays(1);

    // ---------------------------------------------------------------- equipos

    /// <summary>Solo equipos propios (sin dueño 3PL) con seguimiento SERIAL. null = se renta.</summary>
    public static string? CheckEquipment(string sku, bool isOwn, bool isSerial)
    {
        if (!isOwn) return OnlyOwnEquipment(sku);
        if (!isSerial) return NotSerialTracked(sku);
        return null;
    }

    /// <summary>Nota de los movimientos del despacho: 'Renta REN-00001'.</summary>
    public static string DispatchNotes(string rentalNumber) => $"Renta {rentalNumber}";

    // ---------------------------------------------------------------- textos y tarifas

    /// <summary>Texto opcional recortado (vacío = null) con largo máximo; devuelve (valor, error).</summary>
    public static (string? Value, string? Error) NormalizeText(string? value, int maxLength, string tooLong)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var t = value.Trim();
        return t.Length > maxLength ? (null, tooLong) : (t, null);
    }

    /// <summary>Motivo de la extensión: obligatorio y de hasta 300 caracteres.</summary>
    public static (string? Value, string? Error) NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (null, ExtensionReasonRequired);
        return NormalizeText(reason, MaxReasonLength, ReasonTooLong);
    }

    /// <summary>Frecuencia normalizada (mayúsculas) o error; vacía = FrequencyRequired.</summary>
    public static (string? Code, string? Error) NormalizeFrequency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, FrequencyRequired);
        var code = value.Trim().ToUpperInvariant();
        return RentalBillingFrequencies.All.Contains(code) ? (code, null) : (null, UnknownFrequency(value.Trim()));
    }

    /// <summary>Monto de la tarifa: obligatorio y ≥ 0. null = válido.</summary>
    public static string? ValidateRateAmount(decimal? amount) => amount switch
    {
        null => RateAmountRequired,
        < 0m => NegativeRate,
        _ => null,
    };

    /// <summary>¿La tarifa pedida es distinta de la vigente? (frecuencia, monto o moneda).</summary>
    public static bool RateChanged(int currentFrequencyId, decimal currentAmount, int currentCurrencyId, int frequencyId, decimal amount, int currencyId)
        => currentFrequencyId != frequencyId || currentAmount != amount || currentCurrencyId != currencyId;

    // ================================================================ Lote 28 (Rentas R2): devolución, proceso y conteo (D7)

    public const string ReturnNotFound = "Devolución de renta";
    public const string ProcessNotFound = "Proceso";
    public const int MaxReturnLineNotesLength = 500;

    // ---------------------------------------------------------------- mensajes del plan (exactos)

    /// <summary>'La serie {s} no está en renta en {n}.' (409): la serie no es un equipo despachado y sin devolver de la renta.</summary>
    public static string SerialNotOnRent(string serial, string rentalNumber) => $"La serie {serial} no está en renta en {rentalNumber}.";
    public const string OtherReasonNeedsNotes = "Con el motivo 'Otro' describa la devolución en las notas.";
    public const string DestinationInRentalZone = "La posición de destino no puede ser de la zona En renta.";
    public const string ProcessFinished = "El proceso ya terminó; solo se consulta.";
    /// <summary>D7 (409): una serie en renta contada en otra posición no se reconcilia; primero se registra su devolución.</summary>
    public static string SerialOnRentInCount(string serial, string rentalNumber)
        => $"La serie {serial} está en renta ({rentalNumber}); registre su devolución antes de reconciliar el conteo.";
    /// <summary>D7 (422): la posición EN-RENTA (zona RENTAL) no se cuenta.</summary>
    public static string RentalBinNotCounted(string binCode) => $"La posición {binCode} es de equipos en renta; no se cuenta.";

    // ---------------------------------------------------------------- mensajes nuevos del lote

    public static string ReturnNotOnRent(string rentalNumber) => $"Solo se registra la devolución de una renta En renta; la renta {rentalNumber} no lo está.";
    public const string ReturnReasonRequired = "Indique el motivo de la devolución: END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT u OTHER.";
    public static string UnknownReturnReason(string value) => $"Motivo de devolución desconocido: '{value}'. Use END_OF_CONTRACT, EARLY_DAMAGE, EARLY_CLIENT u OTHER.";
    public static string UnknownCondition(string value) => $"Condición desconocida: '{value}'. Use GOOD, DAMAGED o INCOMPLETE.";
    public const string ReturnLinesRequired = "Indique al menos una serie que se devuelve.";
    public const string ReturnSerialRequired = "Indique el número de serie del equipo devuelto.";
    public const string ReturnedOnFuture = "La fecha de devolución no puede ser futura.";
    public static string ReturnedOnBeforeStart(DateOnly start)
        => $"La fecha de devolución no puede ser anterior al inicio de la renta ({start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}).";
    public const string NegativePickupCost = "El costo de recogido estimado no puede ser negativo.";
    public static readonly string ReturnLineNotesTooLong = $"Las notas del equipo admiten como máximo {MaxReturnLineNotesLength} caracteres.";
    public const string ProcessStatusRequired = "Indique el estatus al que pasa el proceso.";
    public static string ProcessBinOtherWarehouse(string warehouseCode) => $"La posición de destino debe ser del almacén {warehouseCode} del proceso.";

    // ---------------------------------------------------------------- reglas

    /// <summary>Motivo de la devolución normalizado (mayúsculas) o error; vacío = ReturnReasonRequired. Sin BD: los cuatro del seed.</summary>
    public static (string? Code, string? Error) NormalizeReturnReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, ReturnReasonRequired);
        var code = value.Trim().ToUpperInvariant();
        return RentalReturnReasons.All.Contains(code) ? (code, null) : (null, UnknownReturnReason(value.Trim()));
    }

    /// <summary>Condición del equipo devuelto (GOOD por defecto) o error.</summary>
    public static (string? Code, string? Error) NormalizeCondition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (RentalReturnConditions.Good, null);
        var code = value.Trim().ToUpperInvariant();
        return RentalReturnConditions.All.Contains(code) ? (code, null) : (null, UnknownCondition(value.Trim()));
    }

    /// <summary>Con el motivo OTHER las notas son obligatorias (400). null = válido.</summary>
    public static string? ValidateReturnNotes(string? reasonCode, string? notes)
        => reasonCode == RentalReturnReasons.Other && string.IsNullOrWhiteSpace(notes) ? OtherReasonNeedsNotes : null;

    /// <summary>Fecha de devolución: no futura (hoy de la compañía) ni anterior al inicio de la renta. null = válida.</summary>
    public static string? ValidateReturnedOn(DateOnly returnedOn, DateOnly today, DateOnly? rentalStart = null)
    {
        if (returnedOn > today) return ReturnedOnFuture;
        if (rentalStart is DateOnly start && returnedOn < start) return ReturnedOnBeforeStart(start);
        return null;
    }

    /// <summary>Devolución anticipada = antes de la fecha de recogido vigente (dato calculado; para los reportes de R3).</summary>
    public static bool IsEarlyReturn(DateOnly returnedOn, DateOnly pickupDate) => returnedOn < pickupDate;

    /// <summary>Estatus de la serie al volver: EN PROCESO (y reservada) si pasa por proceso; si no, DISPONIBLE.</summary>
    public static string ReturnSerialStatus(bool requiresProcess) => requiresProcess ? SerialStatuses.InProcess : SerialStatuses.Available;

    /// <summary>Nota de los movimientos de la devolución: 'Devolución de renta DRN-00001 (REN-00001)'.</summary>
    public static string ReturnNotes(string returnNumber, string rentalNumber) => $"Devolución de renta {returnNumber} ({rentalNumber})";

    /// <summary>Nota del traslado al terminar el proceso: 'Proceso #7'.</summary>
    public static string ProcessMoveNotes(int processId) => $"Proceso #{processId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Nota de la baja del equipo al darlo de baja en su proceso: 'Baja del proceso #7'.</summary>
    public static string ScrapNotes(int processId) => $"Baja del proceso #{processId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>¿El proceso ya terminó? (estatus terminal: Lista o Dada de baja, o fecha de cierre sellada).</summary>
    public static bool IsProcessFinished(string? stageKindCode, DateTime? completedAtUtc)
        => completedAtUtc is not null || stageKindCode == StageKinds.Terminal;
}
