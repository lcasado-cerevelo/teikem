using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Carga de combustible (bitácora). km/L y costo/km se calculan al leer (FuelEfficiency), no se guardan. El odómetro debe
/// ser monótono por fecha en el mismo vehículo y sube el del vehículo (máximo monotónico, con bloqueo de fila). Quitar una
/// carga = IsActive 0 (deja de contar en la eficiencia).
/// </summary>
[AuditEntity(Constants.EntityTypes.FuelLog)]
public class FuelLog : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int FuelLogId { get; set; }
    public int TenantId { get; set; }
    public int VehicleId { get; set; }
    public int? DriverId { get; set; }
    public DateTime FillDateUtc { get; set; }
    public decimal? OdometerKm { get; set; }
    public decimal Liters { get; set; }
    public decimal TotalCost { get; set; }
    public int? CurrencyLookupId { get; set; }
    public string? Station { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }

    public Vehicle? Vehicle { get; set; }
    public Driver? Driver { get; set; }
    public LookupCode? Currency { get; set; }
}
