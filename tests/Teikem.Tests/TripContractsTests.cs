using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Teikem.Infrastructure.Contracts;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 5 / P0: la firma posicional COMPLETA de los contratos de Trips y rutas queda fijada por reflexión (nombre, tipo y
/// nulabilidad de cada parámetro en orden). Ninguna pieza la cambia: si un record cambia, esta prueba lo delata. Además:
/// todos son sealed, TripPatchRequest recoge llaves desconocidas en Extra, ninguna solicitud lleva TenantId ni ids int de
/// Trip/Route y ningún DTO de trips expone montos ni tarifas.
/// </summary>
public class TripContractsTests
{
    private static readonly NullabilityInfoContext Nullability = new();

    /// <summary>Firma 'Tipo Nombre' con la sintaxis de C# (int?, string?, IReadOnlyList&lt;RouteStopDto&gt;, …).</summary>
    private static string[] Signature(Type t)
        => t.GetConstructors().Single().GetParameters().Select(p => $"{TypeName(p.ParameterType, Nullability.Create(p))} {p.Name}").ToArray();

    private static string TypeName(Type t, NullabilityInfo info)
    {
        if (Nullable.GetUnderlyingType(t) is Type u) return TypeName(u, info) + "?";
        var name = t switch
        {
            _ when t == typeof(int) => "int",
            _ when t == typeof(bool) => "bool",
            _ when t == typeof(decimal) => "decimal",
            _ when t == typeof(double) => "double",
            _ when t == typeof(string) => "string",
            _ when t.IsArray => TypeName(t.GetElementType()!, info.ElementType!) + "[]",
            _ when t.IsGenericType => $"{t.Name[..t.Name.IndexOf('`')]}<{string.Join(", ", t.GetGenericArguments().Select((a, i) => TypeName(a, info.GenericTypeArguments[i])))}>",
            _ => t.Name,
        };
        return !t.IsValueType && info.ReadState == NullabilityState.Nullable ? name + "?" : name;
    }

    public static readonly TheoryData<Type, string> Signatures = new()
    {
        // ---- comunes y ficha
        { typeof(TripIssueDto), "string Code, string Message, bool Blocking" },
        { typeof(GeoPointDto), "double Lat, double Lng" },
        { typeof(DriverPingDto), "GeoPointDto Point, decimal? SpeedKmh, int? HeadingDeg, DateTime CapturedAtUtc, DateTime ReceivedAtUtc, bool LinkedToTrip" },
        { typeof(TripListQuery), "DateOnly? Date, DateOnly? From, DateOnly? To, string[]? Status, int? DispatchZoneId, Guid? DriverPublicId, string? Search, bool IncludeCancelled" },
        { typeof(TripListItemDto), "int Id, Guid PublicId, string Code, DateOnly PlanDate, int? DispatchZoneId, string? ZoneCode, string? ZoneName, Guid? DriverPublicId, string? DriverCode, string? DriverName, Guid? VehiclePublicId, string? VehicleCode, string StatusCode, string Status, string? StatusColor, bool IsEditable, int? RouteVersion, string? RouteStatusCode, int StopCount, int? EffectiveMaxStops, bool OverStopLimit, decimal? TotalDistanceKm, int? TotalDurationMin, DateTime? PlannedStartUtc, DateTime? PlannedEndUtc, bool IsActive" },
        { typeof(RouteStopDto), "int Id, int Sequence, int OrderStopId, Guid OrderPublicId, string OrderNumber, string PackBatchNumber, string ClientName, string? ConsigneeName, string Line1, string? Line2, string City, string? PostalCode, string? ZoneCode, GeoPointDto? Point, string? GeocodeAccuracyCode, string? GeocodeAccuracy, bool IsApproximate, DateTime? WindowStartUtc, DateTime? WindowEndUtc, int ServiceMinutes, DateTime? PlannedArrivalUtc, DateTime? PlannedDepartureUtc, decimal? DistanceFromPrevKm, int? DurationFromPrevMin, bool LateForWindow, int? Pieces, decimal? WeightKg, decimal? VolumeM3, string StatusCode, string Status, DateTime? ActualArrivalUtc, DateTime? ActualDepartureUtc" },
        { typeof(UnassignedStopDto), "Guid OrderPublicId, string OrderNumber, string ReasonCode, string Reason" },
        { typeof(TripDetailDto), "int Id, Guid PublicId, string Code, DateOnly PlanDate, int? DispatchZoneId, string? ZoneCode, string? ZoneName, Guid? DriverPublicId, string? DriverCode, string? DriverName, Guid? VehiclePublicId, string? VehicleCode, string? VehiclePlate, string StatusCode, string Status, bool IsEditable, bool CanEditHeader, bool IsTerminal, int? RouteId, int? RouteVersion, string? RouteStatusCode, string? RouteStatus, int StopCount, int? EffectiveMaxStops, bool OverStopLimit, decimal TotalWeightKg, decimal TotalVolumeM3, decimal? TotalDistanceKm, int? TotalDurationMin, DateTime? PlannedStartUtc, DateTime? PlannedEndUtc, DateTime? ActualStartUtc, DateTime? ActualEndUtc, IReadOnlyList<RouteStopDto> Stops, IReadOnlyList<TripIssueDto> Issues, IReadOnlyList<UnassignedStopDto> LastRunUnassigned, DriverPingDto? LastPing, bool IsActive, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, string RowVersion" },
        // ---- planificación y órdenes
        { typeof(TripCreateRequest), "DateOnly? PlanDate, int? DispatchZoneId, Guid? DriverPublicId, Guid? VehiclePublicId, DateTime? PlannedStartUtc" },
        { typeof(TripPatchRequest), "DateOnly? PlanDate, int? DispatchZoneId, bool? ClearZone, Guid? DriverPublicId, bool? ClearDriver, Guid? VehiclePublicId, bool? ClearVehicle, DateTime? PlannedStartUtc, bool? ClearPlannedStart, string? RowVersion" },
        { typeof(TripCancelRequest), "string? Comment, string? RowVersion" },
        { typeof(ZoneReassignRequest), "DateOnly? PlanDate, IReadOnlyList<int>? DispatchZoneIds, Guid? DriverPublicId" },
        { typeof(ZoneReassignResultDto), "Guid DriverPublicId, string DriverCode, string DriverName, int TripsUpdated, IReadOnlyList<TripListItemDto> Trips, IReadOnlyList<TripIssueDto> Issues" },
        { typeof(PlanDayRequest), "DateOnly? PlanDate, IReadOnlyList<int>? DispatchZoneIds, bool CreateEmptyTrips" },
        { typeof(PlanDayZoneResultDto), "int DispatchZoneId, string ZoneCode, Guid? TripPublicId, string? TripCode, bool TripCreated, int OrdersAssigned, int OrdersSkipped, IReadOnlyList<TripIssueDto> Issues" },
        { typeof(PlanDayResultDto), "DateOnly PlanDate, int TripsCreated, int OrdersAssigned, int OrdersSkipped, int OrdersWithoutZone, IReadOnlyList<PlanDayZoneResultDto> Zones" },
        { typeof(TripOrdersAddRequest), "IReadOnlyList<Guid>? OrderPublicIds, string? RowVersion" },
        { typeof(TripOrderRemoveRequest), "string? RowVersion" },
        { typeof(UnassignedOrdersQuery), "int? DispatchZoneId, bool NoZone, string? PostalCode, string? City, Guid? ClientPublicId, DateOnly? RequestedFrom, DateOnly? RequestedTo, string? Search, int Skip, int Take" },
        { typeof(UnassignedOrderDto), "int Id, Guid PublicId, string OrderNumber, string PackBatchNumber, string ClientInvoiceNumber, string ClientName, string? ConsigneeName, string City, string? PostalCode, int? DispatchZoneId, string? ZoneCode, bool ZoneAmbiguous, string StatusCode, string Status, DateTime? RequestedDate, DateTime? WindowStartUtc, DateTime? WindowEndUtc, int? Pieces, decimal? WeightKg, decimal? VolumeM3, GeoPointDto? Point, string? GeocodeAccuracyCode" },
        { typeof(UnassignedOrderPageDto), "int Total, int Skip, int Take, IReadOnlyList<UnassignedOrderDto> Items" },
        // ---- optimización, secuencia y pin
        { typeof(RouteSequenceRequest), "IReadOnlyList<int>? RouteStopIds, string? RowVersion" },
        { typeof(StopLocationRequest), "double? Lat, double? Lng, string? RowVersion" },
        { typeof(OptimizeRequest), "string? RowVersion" },
        { typeof(OptimizationResultDto), "int RunId, string EngineCode, string StatusCode, int RouteVersion, int AssignedCount, int UnassignedCount, IReadOnlyList<UnassignedStopDto> Unassigned, string? Polyline, TripDetailDto Trip" },
        { typeof(OptimizationRunDto), "int Id, string EngineCode, string Engine, string StatusCode, string Status, int? RouteId, int? RouteVersion, int? UnassignedCount, decimal? TotalDistanceKm, int? TotalDurationMin, string? ErrorMessage, DateTime StartedAtUtc, DateTime? CompletedAtUtc, string? StartedBy" },
        // ---- despacho, salida, monitoreo y escaneo
        { typeof(DispatchableTripDto), "TripListItemDto Trip, bool CanDispatch, IReadOnlyList<TripIssueDto> Issues" },
        { typeof(TripDispatchRequest), "string? Comment, string? RowVersion" },
        { typeof(TripBatchDispatchRequest), "IReadOnlyList<Guid>? TripPublicIds, string? Comment" },
        { typeof(TripDispatchResultItemDto), "Guid TripPublicId, string? Code, bool Dispatched, string? Error, IReadOnlyList<TripIssueDto> Issues" },
        { typeof(TripBatchDispatchResultDto), "int Requested, int Dispatched, IReadOnlyList<TripDispatchResultItemDto> Items" },
        { typeof(TripStartRequest), "string? Comment, string? RowVersion" },
        { typeof(MonitorQuery), "DateOnly? Date, int? DispatchZoneId, string? Search, bool IncludeCompleted" },
        { typeof(MonitorTripDto), "Guid PublicId, string Code, DateOnly PlanDate, string? ZoneCode, string? DriverCode, string? DriverName, string? VehicleCode, string StatusCode, string Status, int TotalStops, int CompletedStops, int FailedStops, int PendingStops, int ApproximateStops, DateTime? NextEtaUtc, DateTime? PlannedEndUtc, bool OverStopLimit, DriverPingDto? LastPing" },
        { typeof(MonitorTotalsDto), "int Trips, int TotalStops, int CompletedStops, int FailedStops, int PendingStops, int OverStopLimitTrips, int TripsWithoutPing" },
        { typeof(MonitorDto), "DateOnly Date, MonitorTotalsDto Totals, IReadOnlyList<MonitorTripDto> Trips" },
        { typeof(OutboundScanRequest), "string? Code, DateOnly? PlanDate" },
        { typeof(OutboundScanResultDto), "string Outcome, string Voice, string Message, string? MatchedBy, Guid? OrderPublicId, string? OrderNumber, string? PackBatchNumber, string? ZoneCode, Guid? TripPublicId, string? TripCode, string? ReasonCode" },
        // ---- DriverContracts (Lote 5)
        { typeof(DispatchZoneMemberDto), "int Id, string MatchTypeCode, string MatchType, string MatchValue" },
        { typeof(DispatchZoneMembersDto), "int ZoneId, string Code, string? Name, bool IsActive, IReadOnlyList<DispatchZoneMemberDto> Members" },
        { typeof(DispatchZoneMemberRequest), "string? MatchType, string? MatchValue" },
        { typeof(ZoneResolutionDto), "string? PostalCode, string? City, int? DispatchZoneId, string? ZoneCode, string? MatchedBy, bool Ambiguous, IReadOnlyList<string> Candidates" },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Positional_signature_is_fixed(Type type, string expected)
        => Assert.Equal(expected.Split(", "), Signature(type));

    private static IEnumerable<Type> AllTypes() => Signatures.Select(row => (Type)row[0]);

    private static IEnumerable<Type> Requests() => AllTypes().Where(t => t.Name.EndsWith("Request", StringComparison.Ordinal) || t.Name.EndsWith("Query", StringComparison.Ordinal));

    [Fact]
    public void All_contracts_are_sealed_records()
    {
        foreach (var t in AllTypes())
        {
            Assert.True(t.IsSealed, $"{t.Name} no es sealed");
            Assert.NotNull(t.GetMethod("<Clone>$")); // record
        }
    }

    [Fact]
    public void Optional_parameters_have_the_documented_defaults()
    {
        static object?[] Defaults(Type t) => t.GetConstructors().Single().GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : "<required>").ToArray();
        Assert.Equal(new object?[] { "<required>", null, null, null, null }, Defaults(typeof(TripCreateRequest)));
        Assert.All(Defaults(typeof(TripListQuery)).Take(7), d => Assert.Null(d));
        Assert.Equal(false, Defaults(typeof(TripListQuery))[7]);
        Assert.Equal(new object?[] { "<required>", null, false }, Defaults(typeof(PlanDayRequest)));
        Assert.Equal(new object?[] { null, false, null, null, null, null, null, null, 0, 100 }, Defaults(typeof(UnassignedOrdersQuery)));
        Assert.Equal(new object?[] { null, null, null, true }, Defaults(typeof(MonitorQuery)));
        Assert.Equal(new object?[] { "<required>", null }, Defaults(typeof(OutboundScanRequest)));
        Assert.Equal(new object?[] { "<required>", "<required>", null }, Defaults(typeof(StopLocationRequest)));
        Assert.All(Defaults(typeof(TripPatchRequest)), d => Assert.Null(d));
    }

    [Fact]
    public void Trip_patch_request_collects_unknown_keys_in_Extra()
    {
        var extra = typeof(TripPatchRequest).GetProperty("Extra");
        Assert.NotNull(extra);
        Assert.NotNull(extra!.GetCustomAttribute<JsonExtensionDataAttribute>());
        Assert.True(extra.CanWrite);
        Assert.Equal(typeof(IDictionary<string, JsonElement>), extra.PropertyType);

        var req = JsonSerializer.Deserialize<TripPatchRequest>("{\"code\":\"X\",\"driverPublicId\":null}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(req.Extra!.ContainsKey("code"));
    }

    [Fact]
    public void Requests_never_carry_the_tenant_nor_internal_trip_or_route_ids()
    {
        foreach (var t in Requests())
        {
            var names = t.GetProperties().Select(p => p.Name).ToList();
            Assert.DoesNotContain(names, n => n.Equals("TenantId", StringComparison.OrdinalIgnoreCase));
            foreach (var n in new[] { "TripId", "TripIds", "RouteId", "RouteIds" })
                Assert.DoesNotContain(n, names, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(t.GetProperties(), p => p.Name.StartsWith("Trip", StringComparison.Ordinal)
                && (p.PropertyType == typeof(int) || p.PropertyType == typeof(int?)));
        }
    }

    [Fact]
    public void No_trip_dto_exposes_amounts_or_rates()
    {
        foreach (var t in AllTypes())
            Assert.DoesNotContain(t.GetProperties(), p => p.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase)
                                                         || p.Name.Contains("Rate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Optimization_run_ids_are_int()
    {
        Assert.Equal(typeof(int), typeof(OptimizationResultDto).GetProperty(nameof(OptimizationResultDto.RunId))!.PropertyType);
        Assert.Equal(typeof(int), typeof(OptimizationRunDto).GetProperty(nameof(OptimizationRunDto.Id))!.PropertyType);
    }

    [Fact]
    public void Order_detail_tail_adds_the_assigned_trip_after_the_assigned_driver()
    {
        var tail = typeof(OrderDetailDto).GetConstructors().Single().GetParameters().TakeLast(5).ToArray();
        Assert.Equal(new[] { "Guid? AssignedDriverPublicId", "string? AssignedDriverCode", "string? AssignedDriverName", "Guid? AssignedTripPublicId", "string? AssignedTripCode" },
            tail.Select(p => $"{TypeName(p.ParameterType, Nullability.Create(p))} {p.Name}"));
        Assert.All(tail, p => Assert.True(p.HasDefaultValue));
    }

    [Fact]
    public void Service_seam_types_do_not_live_in_the_contracts_namespace()
    {
        var infra = typeof(TripDetailDto).Assembly;
        foreach (var name in new[] { "TripCreateSpec", "UnassignedPoolFilter", "UnassignedCandidate", "CurrentTripRef", "TripPingKey", "DriverPingRow" })
        {
            var t = infra.GetTypes().Single(x => x.Name == name);
            Assert.Equal("Teikem.Infrastructure.Trips", t.Namespace);
        }
    }
}
