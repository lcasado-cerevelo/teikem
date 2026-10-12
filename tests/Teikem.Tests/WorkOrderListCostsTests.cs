using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Domain.Fleet;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Persistence;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote F-D3 (hallazgo de la pantalla «Órdenes de trabajo»): la lista de órdenes solo traía el total; la maqueta pinta Labor, Partes y
/// Total por orden, y pedir la ficha de cada una para llenar la tabla era una petición por fila. Ahora la lista trae LaborCost y
/// PartsCost (nulos cuando la orden no los tiene) y el total sigue siendo su suma.
/// </summary>
public class WorkOrderListCostsTests
{
    private const int TenantId = 1;
    private const int OpenStatus = 1, PipelineKind = 901, CorrectiveType = 700;

    private static async Task<TeikemDbContext> SeedAsync()
    {
        var db = new TeikemDbContext(
            new DbContextOptionsBuilder<TeikemDbContext>()
                .UseInMemoryDatabase("wo-list-costs-" + Guid.NewGuid())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options,
            new TenantContext { TenantId = TenantId, UserId = 1 });
        db.LookupCodes.AddRange(
            new LookupCode { LookupCodeId = PipelineKind, Entity = LookupDomains.StageKind, InternalCode = StageKinds.Pipeline, LabelJson = "{}", IsActive = true },
            new LookupCode { LookupCodeId = CorrectiveType, Entity = LookupDomains.MaintenanceType, InternalCode = "CORRECTIVE", LabelJson = "{\"es\":\"Correctivo\"}", IsActive = true });
        db.StatusCodes.Add(new StatusCode { StatusCodeId = OpenStatus, Entity = StatusDomains.WorkOrderStatus, InternalCode = "OPEN", LabelJson = "{\"es\":\"Abierta\"}", StageKindLookupId = PipelineKind, IsInitial = true });
        db.Vehicles.Add(new Vehicle { VehicleId = 10, PublicId = Guid.NewGuid(), TenantId = TenantId, Code = "V-01", PlateNumber = "AAA", StatusCodeId = 1, IsActive = true });
        db.MaintenanceWorkOrders.AddRange(
            new MaintenanceWorkOrder { WorkOrderId = 1, PublicId = Guid.NewGuid(), TenantId = TenantId, VehicleId = 10, Number = "OT-00001", MaintenanceTypeLookupId = CorrectiveType, StatusCodeId = OpenStatus, LaborCost = 120m, PartsCost = 340m, IsActive = true },
            new MaintenanceWorkOrder { WorkOrderId = 2, PublicId = Guid.NewGuid(), TenantId = TenantId, VehicleId = 10, Number = "OT-00002", MaintenanceTypeLookupId = CorrectiveType, StatusCodeId = OpenStatus, LaborCost = null, PartsCost = null, IsActive = true },
            new MaintenanceWorkOrder { WorkOrderId = 3, PublicId = Guid.NewGuid(), TenantId = TenantId, VehicleId = 10, Number = "OT-00003", MaintenanceTypeLookupId = CorrectiveType, StatusCodeId = OpenStatus, LaborCost = 60m, PartsCost = null, IsActive = true });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task The_list_returns_labor_and_parts_per_order_and_the_total_is_their_sum()
    {
        using var db = await SeedAsync();
        var service = new MaintenanceWorkOrderService(db, new TenantContext { TenantId = TenantId, UserId = 1 }, new FakeLookups(), null!, null!);

        var items = await service.ListAsync(new Teikem.Infrastructure.Contracts.WorkOrderListQuery(), default);

        var byNumber = items.ToDictionary(i => i.Number);
        Assert.Equal((120m, 340m, 460m), (byNumber["OT-00001"].LaborCost!.Value, byNumber["OT-00001"].PartsCost!.Value, byNumber["OT-00001"].TotalCost));
        Assert.Null(byNumber["OT-00002"].LaborCost);
        Assert.Null(byNumber["OT-00002"].PartsCost);
        Assert.Equal(0m, byNumber["OT-00002"].TotalCost);
        Assert.Equal(60m, byNumber["OT-00003"].LaborCost);
        Assert.Null(byNumber["OT-00003"].PartsCost);
        Assert.Equal(60m, byNumber["OT-00003"].TotalCost);
    }

    [Fact]
    public async Task Creating_an_order_without_a_vehicle_is_a_400_on_the_field_not_a_404()
    {
        using var db = await SeedAsync();
        var service = new MaintenanceWorkOrderService(db, new TenantContext { TenantId = TenantId, UserId = 1 }, new FakeLookups(), null!, null!);

        var ex = await Assert.ThrowsAsync<Teikem.Infrastructure.Exceptions.ValidationException>(() =>
            service.CreateAsync(new Teikem.Infrastructure.Contracts.WorkOrderCreateRequest(Guid.Empty, "CORRECTIVE"), default));

        Assert.Equal("El vehículo de la orden de trabajo es obligatorio.", ex.Errors!["vehiclePublicId"][0]);
        Assert.Equal(MaintenanceWorkOrderService.VehicleRequiredMessage, ex.Errors!["vehiclePublicId"][0]);
    }

    /// <summary>ILookupCache mínimo con el tipo de mantenimiento CORRECTIVE.</summary>
    private sealed class FakeLookups : ILookupCache
    {
        private static readonly LookupCode[] Codes =
        {
            new() { LookupCodeId = CorrectiveType, Entity = LookupDomains.MaintenanceType, InternalCode = "CORRECTIVE", LabelJson = "{\"es\":\"Correctivo\"}" },
        };

        public async Task<int> GetIdAsync(string entity, string code, CancellationToken ct = default)
            => await TryGetIdAsync(entity, code, ct) ?? throw new InvalidOperationException($"{entity}.{code}");

        public Task<int?> TryGetIdAsync(string entity, string code, CancellationToken ct = default)
            => Task.FromResult(Codes.FirstOrDefault(c => c.Entity == entity && string.Equals(c.InternalCode, code, StringComparison.OrdinalIgnoreCase))?.LookupCodeId);

        public Task<LookupCode?> GetAsync(int lookupCodeId, CancellationToken ct = default)
            => Task.FromResult(Codes.FirstOrDefault(c => c.LookupCodeId == lookupCodeId));

        public Task<IReadOnlyList<LookupCode>> GetDomainAsync(string entity, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LookupCode>>(Codes.Where(c => c.Entity == entity).ToList());

        public void Invalidate() { }
    }
}
