using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Services;

namespace Teikem.Tests;

/// <summary>ReceiptService reporta el daño declarado en sus líneas con DamageService (que mueve inventario con el ajuste): lo que necesita en el contenedor.</summary>
internal static class ReceiptDamageDeps
{
    public static IServiceCollection AddReceiptDamageDeps(this IServiceCollection s)
    {
        s.TryAddSingleton<ITenantClock>(TenantClock.Default);
        s.TryAddSingleton<InventoryReadService>();
        s.TryAddSingleton<InventoryAdjustmentService>();
        s.TryAddSingleton<DamageService>();
        return s;
    }
}
