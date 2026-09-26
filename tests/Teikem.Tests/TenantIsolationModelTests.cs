using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 4 / P0: pruebas del MODELO EF (sin conectarse a SQL Server) que sostienen la defensa en profundidad multi-tenant:
/// toda entidad con TenantId lleva el filtro global (salvo una lista cerrada de excepciones previas), las hijas de flota sin
/// TenantId son exactamente las esperadas, y los índices únicos filtrados, la columna computada, los RowVersion y la llave
/// de DriverPayPolicy coinciden con Diseño/logistica-db-estructura.sql.
/// </summary>
public class TenantIsolationModelTests
{
    /// <summary>Contexto con proveedor SQL Server (para leer nombres y filtros de índices) y una cadena ficticia: nunca se conecta.</summary>
    public static TeikemDbContext CreateSqlServerModelContext()
    {
        var options = new DbContextOptionsBuilder<TeikemDbContext>()
            .UseSqlServer("Server=model-only.invalid;Database=TeikemModel;User Id=x;Password=x;TrustServerCertificate=True")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options;
        return new TeikemDbContext(options, new TenantContext { TenantId = 1, UserId = 1 });
    }

    /// <summary>
    /// Entidades con propiedad TenantId SIN filtro global, anteriores al Lote 4 y revisadas: Tenant (la propia compañía; su
    /// TenantId es la PK). Lista cerrada: si aparece una nueva entidad con TenantId sin filtro, la prueba falla.
    /// </summary>
    private static readonly HashSet<string> TenantIdWithoutFilterExceptions = new(StringComparer.Ordinal)
    {
        "Tenant",
    };

    private static IEnumerable<IEntityType> DomainEntities(TeikemDbContext db)
        => db.Model.GetEntityTypes().Where(e => e.ClrType.Namespace?.StartsWith("Teikem.Domain", StringComparison.Ordinal) == true && !e.IsOwned());

    [Fact]
    public void Every_domain_entity_with_TenantId_has_the_global_tenant_filter()
    {
        using var db = CreateSqlServerModelContext();
        var missing = DomainEntities(db)
            .Where(e => e.FindProperty("TenantId") is not null && e.GetQueryFilter() is null)
            .Select(e => e.ClrType.Name)
            .Where(n => !TenantIdWithoutFilterExceptions.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0, "Entidades con TenantId sin filtro global: " + string.Join(", ", missing));
    }

    [Fact]
    public void Exceptions_list_is_still_accurate()
    {
        // Cada excepción declarada sigue existiendo y sigue sin filtro (si se corrige, se quita de la lista).
        using var db = CreateSqlServerModelContext();
        foreach (var name in TenantIdWithoutFilterExceptions)
        {
            var e = DomainEntities(db).Single(x => x.ClrType.Name == name);
            Assert.NotNull(e.FindProperty("TenantId"));
            Assert.Null(e.GetQueryFilter());
        }
    }

    [Fact]
    public void Fleet_entities_without_TenantId_are_exactly_the_children_reached_through_their_owner()
    {
        using var db = CreateSqlServerModelContext();
        var withoutTenant = DomainEntities(db)
            .Where(e => e.ClrType.Namespace == "Teikem.Domain.Fleet" && e.FindProperty("TenantId") is null)
            .Select(e => e.ClrType.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "DriverCertification", "DriverLicense", "DriverZone", "MaintenanceTask", "VehicleDocument" }, withoutTenant);

        // Y todas las demás de flota tienen filtro de tenant.
        var fleetWithTenant = DomainEntities(db).Where(e => e.ClrType.Namespace == "Teikem.Domain.Fleet" && e.FindProperty("TenantId") is not null).ToList();
        Assert.Equal(12, fleetWithTenant.Count);
        Assert.All(fleetWithTenant, e => Assert.NotNull(e.GetQueryFilter()));
    }

    [Fact]
    public void Work_order_total_cost_is_a_stored_computed_column()
    {
        using var db = CreateSqlServerModelContext();
        var p = db.Model.FindEntityType(typeof(MaintenanceWorkOrder))!.FindProperty(nameof(MaintenanceWorkOrder.TotalCost))!;
        Assert.Equal("ISNULL([LaborCost],0) + ISNULL([PartsCost],0)", p.GetComputedColumnSql());
        Assert.True(p.GetIsStored());
        Assert.Equal(ValueGenerated.OnAddOrUpdate, p.ValueGenerated);
    }

    [Theory]
    [InlineData(typeof(Vehicle))]
    [InlineData(typeof(Driver))]
    [InlineData(typeof(MaintenanceWorkOrder))]
    [InlineData(typeof(DriverTrip))]
    public void RowVersion_is_a_concurrency_token(Type clr)
    {
        using var db = CreateSqlServerModelContext();
        var p = db.Model.FindEntityType(clr)!.FindProperty("RowVersion")!;
        Assert.True(p.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, p.ValueGenerated);
    }

    [Theory]
    [InlineData(typeof(Vehicle), "UQ_Vehicle_Code", new[] { "TenantId", "Code" }, null)]
    [InlineData(typeof(Driver), "UQ_Driver_EmployeeCode", new[] { "TenantId", "EmployeeCode" }, null)]
    [InlineData(typeof(Driver), "UX_Driver_User", new[] { "TenantId", "UserId" }, "[UserId] IS NOT NULL")]
    [InlineData(typeof(DispatchZone), "UQ_DispatchZone", new[] { "TenantId", "Code" }, null)]
    [InlineData(typeof(MaintenanceWorkOrder), "UQ_WorkOrder_Number", new[] { "TenantId", "Number" }, null)]
    [InlineData(typeof(DriverDeliveryRate), "UQ_DriverDeliveryRate_Open", new[] { "DriverId", "ServiceTypeLookupId", "PackageTypeLookupId" }, "[EffectiveTo] IS NULL AND [IsActive] = 1")]
    [InlineData(typeof(DriverAttemptRate), "UQ_DriverAttemptRate_Open", new[] { "DriverId", "AttemptNumber" }, "[EffectiveTo] IS NULL AND [IsActive] = 1")]
    [InlineData(typeof(DriverTripRate), "UQ_DriverTripRate_Open", new[] { "DriverId", "SpecialServiceTypeId" }, "[EffectiveTo] IS NULL AND [IsActive] = 1")]
    [InlineData(typeof(DriverTrip), "UX_DriverTrip_Order", new[] { "TransportOrderId" }, "[TransportOrderId] IS NOT NULL AND [IsActive] = 1")]
    [InlineData(typeof(DriverDevice), "UX_DriverDevice_Token", new[] { "TenantId", "PushToken" }, "[PushToken] IS NOT NULL AND [IsActive] = 1")]
    public void Unique_indexes_mirror_the_sql_names_columns_and_filters(Type clr, string name, string[] columns, string? filter)
    {
        using var db = CreateSqlServerModelContext();
        var index = db.Model.FindEntityType(clr)!.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == name);
        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
        Assert.Equal(columns, index.Properties.Select(p => p.Name));
        Assert.Equal(filter, index.GetFilter());
    }

    [Fact]
    public void Driver_pay_policy_is_keyed_by_tenant_without_value_generation()
    {
        using var db = CreateSqlServerModelContext();
        var e = db.Model.FindEntityType(typeof(DriverPayPolicy))!;
        var key = e.FindPrimaryKey()!;
        Assert.Equal(new[] { "TenantId" }, key.Properties.Select(p => p.Name));
        Assert.Equal(ValueGenerated.Never, key.Properties[0].ValueGenerated);
        Assert.NotNull(e.GetQueryFilter());
    }

    [Fact]
    public void Driver_zone_has_a_composite_key_and_tables_map_one_to_one()
    {
        using var db = CreateSqlServerModelContext();
        var zone = db.Model.FindEntityType(typeof(DriverZone))!;
        Assert.Equal(new[] { "DriverId", "DispatchZoneId" }, zone.FindPrimaryKey()!.Properties.Select(p => p.Name));

        var expected = new Dictionary<Type, string>
        {
            [typeof(Vehicle)] = "Vehicle", [typeof(VehicleDocument)] = "VehicleDocument", [typeof(Driver)] = "Driver",
            [typeof(DriverLicense)] = "DriverLicense", [typeof(DriverCertification)] = "DriverCertification", [typeof(DriverDevice)] = "DriverDevice",
            [typeof(DispatchZone)] = "DispatchZone", [typeof(DriverZone)] = "DriverZone", [typeof(MaintenanceSchedule)] = "MaintenanceSchedule",
            [typeof(MaintenanceWorkOrder)] = "MaintenanceWorkOrder", [typeof(MaintenanceTask)] = "MaintenanceTask", [typeof(FuelLog)] = "FuelLog",
            [typeof(DriverPayPolicy)] = "DriverPayPolicy", [typeof(DriverDeliveryRate)] = "DriverDeliveryRate", [typeof(DriverAttemptRate)] = "DriverAttemptRate",
            [typeof(DriverTripRate)] = "DriverTripRate", [typeof(DriverTrip)] = "DriverTrip",
        };
        foreach (var (clr, table) in expected)
            Assert.Equal(table, db.Model.FindEntityType(clr)!.GetTableName());
    }

    [Fact]
    public void Decimal_columns_use_the_sql_precision()
    {
        using var db = CreateSqlServerModelContext();
        string Type<T>(string prop) => db.Model.FindEntityType(typeof(T))!.FindProperty(prop)!.GetColumnType();
        Assert.Equal("decimal(12,3)", Type<Vehicle>(nameof(Vehicle.MaxWeightKg)));
        Assert.Equal("decimal(12,4)", Type<Vehicle>(nameof(Vehicle.MaxVolumeM3)));
        Assert.Equal("decimal(12,1)", Type<Vehicle>(nameof(Vehicle.CurrentOdometerKm)));
        Assert.Equal("decimal(12,1)", Type<MaintenanceSchedule>(nameof(MaintenanceSchedule.IntervalKm)));
        Assert.Equal("decimal(12,1)", Type<FuelLog>(nameof(FuelLog.OdometerKm)));
        Assert.Equal("decimal(10,3)", Type<FuelLog>(nameof(FuelLog.Liters)));
        Assert.Equal("decimal(18,4)", Type<FuelLog>(nameof(FuelLog.TotalCost)));
        Assert.Equal("decimal(18,4)", Type<DriverTrip>(nameof(DriverTrip.Amount)));
        Assert.Equal("decimal(18,4)", Type<DriverDeliveryRate>(nameof(DriverDeliveryRate.Rate)));
        Assert.Equal("decimal(18,4)", Type<MaintenanceTask>(nameof(MaintenanceTask.PartCost)));
    }
}
