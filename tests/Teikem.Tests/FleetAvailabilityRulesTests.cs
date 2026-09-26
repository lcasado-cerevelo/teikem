using Teikem.Domain.Fleet;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 4 (P3): reglas puras de disponibilidad para despacho. Bloquean inactivo, estatus distinto del inicial, sin
/// licencia vigente, documento de vehículo vencido y OT en proceso; solo avisan certificación vencida, documento por
/// vencer (≤ 30 días) y vehículo sin documentos. Los documentos superados (renovados) se ignoran.
/// </summary>
public class FleetAvailabilityRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    private static AvailabilityDocSnapshot Doc(string code, string label, DateOnly? expiry, bool superseded = false)
        => new(code, label, expiry, superseded);

    private static DriverAvailabilitySnapshot Driver(IReadOnlyList<AvailabilityDocSnapshot>? licenses = null,
        IReadOnlyList<AvailabilityDocSnapshot>? certs = null, bool isActive = true, bool isInitial = true, string status = "Activo")
        => new(1, isActive, isInitial, false, status,
            licenses ?? new[] { Doc("CDL_A", "CDL clase A", Today.AddDays(365)) },
            certs ?? Array.Empty<AvailabilityDocSnapshot>());

    private static VehicleAvailabilitySnapshot Vehicle(IReadOnlyList<AvailabilityDocSnapshot>? docs = null,
        IReadOnlyList<string>? workOrders = null, bool isActive = true, bool isInitial = true, string status = "Activo")
        => new(1, isActive, isInitial, false, status,
            docs ?? new[] { Doc("REGISTRATION", "Registro", Today.AddDays(200)) },
            workOrders ?? Array.Empty<string>());

    private static AvailabilityIssue Single(AvailabilityResult r, string code) => Assert.Single(r.Issues, i => i.Code == code);

    // ---------------------------------------------------------------- choferes

    [Fact]
    public void Active_driver_with_valid_license_is_available()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(), Today);
        Assert.True(r.Available);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Driver_without_licenses_is_blocked()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(licenses: Array.Empty<AvailabilityDocSnapshot>()), Today);
        Assert.False(r.Available);
        var issue = Single(r, "NO_VALID_LICENSE");
        Assert.True(issue.Blocking);
        Assert.Equal("Sin licencia vigente.", issue.Message);
    }

    [Fact]
    public void Driver_with_all_licenses_expired_is_blocked_with_the_latest_date()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(licenses: new[]
        {
            Doc("CDL_A", "CDL clase A", new DateOnly(2026, 9, 1)),
            Doc("CDL_B", "CDL clase B", new DateOnly(2026, 9, 25)),
        }), Today);
        Assert.False(r.Available);
        var issue = Single(r, "NO_VALID_LICENSE");
        Assert.True(issue.Blocking);
        Assert.Equal("Licencia vencida el 2026-09-25.", issue.Message);
    }

    [Fact]
    public void License_expiring_today_is_still_valid_and_warns()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(licenses: new[] { Doc("CDL_A", "CDL clase A", Today) }), Today);
        Assert.True(r.Available);
        Assert.False(Single(r, "DOC_EXPIRING").Blocking);
    }

    [Fact]
    public void Superseded_expired_license_is_ignored_when_a_valid_one_exists()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(licenses: new[]
        {
            Doc("CDL_A", "CDL clase A", Today.AddDays(-10), superseded: true),
            Doc("CDL_A", "CDL clase A", Today.AddDays(365)),
        }), Today);
        Assert.True(r.Available);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Only_superseded_licenses_count_as_no_license()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(licenses: new[] { Doc("CDL_A", "CDL clase A", Today.AddDays(-10), superseded: true) }), Today);
        Assert.False(r.Available);
        Assert.Equal("Sin licencia vigente.", Single(r, "NO_VALID_LICENSE").Message);
    }

    [Fact]
    public void Expired_certification_only_warns()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(certs: new[] { Doc("HAZMAT", "Materiales peligrosos", Today.AddDays(-1)) }), Today);
        Assert.True(r.Available);
        var issue = Single(r, "CERT_EXPIRED");
        Assert.False(issue.Blocking);
        Assert.Contains("2026-09-25", issue.Message);
    }

    [Fact]
    public void Superseded_expired_certification_does_not_warn()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(certs: new[]
        {
            Doc("HAZMAT", "Materiales peligrosos", Today.AddDays(-1), superseded: true),
            Doc("HAZMAT", "Materiales peligrosos", Today.AddDays(300)),
        }), Today);
        Assert.True(r.Available);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Unavailable_status_blocks_with_driver_status()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(isInitial: false, status: "No disponible"), Today);
        Assert.False(r.Available);
        var issue = Single(r, "DRIVER_STATUS");
        Assert.True(issue.Blocking);
        Assert.Contains("No disponible", issue.Message);
    }

    [Fact]
    public void Inactive_driver_is_blocked()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(isActive: false), Today);
        Assert.False(r.Available);
        Assert.True(Single(r, "DRIVER_INACTIVE").Blocking);
    }

    [Fact]
    public void Blocking_issues_come_before_warnings()
    {
        var r = FleetAvailabilityRules.EvaluateDriver(Driver(licenses: Array.Empty<AvailabilityDocSnapshot>(),
            certs: new[] { Doc("HAZMAT", "Materiales peligrosos", Today.AddDays(-5)) }), Today);
        Assert.False(r.Available);
        Assert.Equal(new[] { "NO_VALID_LICENSE", "CERT_EXPIRED" }, r.Issues.Select(i => i.Code));
    }

    // ---------------------------------------------------------------- vehículos

    [Fact]
    public void Vehicle_with_current_documents_is_available()
    {
        var r = FleetAvailabilityRules.EvaluateVehicle(Vehicle(), Today);
        Assert.True(r.Available);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Vehicle_with_expired_document_is_blocked()
    {
        var r = FleetAvailabilityRules.EvaluateVehicle(Vehicle(docs: new[] { Doc("REGISTRATION", "Registro", new DateOnly(2026, 9, 25)) }), Today);
        Assert.False(r.Available);
        var issue = Single(r, "VEHICLE_DOC_EXPIRED");
        Assert.True(issue.Blocking);
        Assert.Equal("Registro vencido el 2026-09-25.", issue.Message);
    }

    [Fact]
    public void Expired_registration_superseded_by_a_current_one_is_available()
    {
        var r = FleetAvailabilityRules.EvaluateVehicle(Vehicle(docs: new[]
        {
            Doc("REGISTRATION", "Registro", Today.AddDays(-1), superseded: true),
            Doc("REGISTRATION", "Registro", Today.AddDays(365)),
        }), Today);
        Assert.True(r.Available);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Work_order_in_progress_blocks_with_its_number()
    {
        var r = FleetAvailabilityRules.EvaluateVehicle(Vehicle(workOrders: new[] { "OT-00007" }), Today);
        Assert.False(r.Available);
        var issue = Single(r, "WORK_ORDER_IN_PROGRESS");
        Assert.True(issue.Blocking);
        Assert.Equal("Orden de trabajo OT-00007 en proceso.", issue.Message);
    }

    [Fact]
    public void Vehicle_without_documents_only_warns()
    {
        var r = FleetAvailabilityRules.EvaluateVehicle(Vehicle(docs: Array.Empty<AvailabilityDocSnapshot>()), Today);
        Assert.True(r.Available);
        Assert.False(Single(r, "VEHICLE_NO_DOCUMENTS").Blocking);
    }

    [Fact]
    public void Document_expiring_in_ten_days_only_warns()
    {
        var r = FleetAvailabilityRules.EvaluateVehicle(Vehicle(docs: new[] { Doc("INSURANCE", "Seguro", Today.AddDays(10)) }), Today);
        Assert.True(r.Available);
        var issue = Single(r, "DOC_EXPIRING");
        Assert.False(issue.Blocking);
        Assert.Equal("Seguro vence el 2026-10-06.", issue.Message);
    }

    [Fact]
    public void Document_expiring_in_31_days_does_not_warn()
    {
        var r = FleetAvailabilityRules.EvaluateVehicle(Vehicle(docs: new[] { Doc("INSURANCE", "Seguro", Today.AddDays(31)) }), Today);
        Assert.True(r.Available);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Document_without_expiry_never_blocks()
    {
        var r = FleetAvailabilityRules.EvaluateVehicle(Vehicle(docs: new[] { Doc("PERMIT", "Permiso", null) }), Today);
        Assert.True(r.Available);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Vehicle_in_maintenance_or_inactive_is_blocked()
    {
        var maintenance = FleetAvailabilityRules.EvaluateVehicle(Vehicle(isInitial: false, status: "Mantenimiento"), Today);
        Assert.False(maintenance.Available);
        Assert.True(Single(maintenance, "VEHICLE_STATUS").Blocking);

        var inactive = FleetAvailabilityRules.EvaluateVehicle(Vehicle(isActive: false), Today);
        Assert.False(inactive.Available);
        Assert.True(Single(inactive, "VEHICLE_INACTIVE").Blocking);
    }

    [Fact]
    public void Issue_codes_match_the_catalog_constants()
    {
        Assert.Equal("DRIVER_INACTIVE", AvailabilityIssueCodes.DriverInactive);
        Assert.Equal("DRIVER_STATUS", AvailabilityIssueCodes.DriverStatus);
        Assert.Equal("NO_VALID_LICENSE", AvailabilityIssueCodes.NoValidLicense);
        Assert.Equal("CERT_EXPIRED", AvailabilityIssueCodes.CertExpired);
        Assert.Equal("VEHICLE_INACTIVE", AvailabilityIssueCodes.VehicleInactive);
        Assert.Equal("VEHICLE_STATUS", AvailabilityIssueCodes.VehicleStatus);
        Assert.Equal("VEHICLE_DOC_EXPIRED", AvailabilityIssueCodes.VehicleDocExpired);
        Assert.Equal("WORK_ORDER_IN_PROGRESS", AvailabilityIssueCodes.WorkOrderInProgress);
        Assert.Equal("DOC_EXPIRING", AvailabilityIssueCodes.DocExpiring);
        Assert.Equal("VEHICLE_NO_DOCUMENTS", AvailabilityIssueCodes.VehicleNoDocuments);
    }
}
