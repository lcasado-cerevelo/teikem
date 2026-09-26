using Teikem.Domain.Orders;
using Teikem.Infrastructure.Analytics;
using Teikem.Infrastructure.Dsl;
using Teikem.Infrastructure.Seeding;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>Correcciones de la revisión del Lote 3: indicador 'COD por cobrar', tipo de paquete principal y topes de texto.</summary>
public class Lote3HardeningTests
{
    private static Dictionary<string, object?> Row(params (string, object?)[] kv)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    [Fact]
    public void Cod_pending_indicator_excludes_cancelled_orders()
    {
        var f = RuleEvaluator.CompileFilter(SystemAnalyticsSeeder.CodPendingFilter);
        Assert.True(f(Row(("IsActive", true), ("CodStatusCode", "PENDING"), ("StatusCode", "CONFIRMED"))));
        Assert.True(f(Row(("IsActive", true), ("CodStatusCode", "PENDING"), ("StatusCode", "DRAFT"))));
        Assert.False(f(Row(("IsActive", true), ("CodStatusCode", "PENDING"), ("StatusCode", "CANCELLED"))));
        Assert.False(f(Row(("IsActive", false), ("CodStatusCode", "PENDING"), ("StatusCode", "CONFIRMED"))));
        Assert.False(f(Row(("IsActive", true), ("CodStatusCode", null), ("StatusCode", "CONFIRMED"))));
        // la versión anterior sí sumaba la cancelada (por eso se corrige al resembrar)
        Assert.True(RuleEvaluator.CompileFilter(SystemAnalyticsSeeder.CodPendingFilterV1)(Row(("IsActive", true), ("CodStatusCode", "PENDING"), ("StatusCode", "CANCELLED"))));
    }

    private static CargoLine Line(int id, int? type, decimal qty) => new() { CargoLineId = id, PackageTypeLookupId = type, Quantity = qty, Description = "x" };

    [Fact]
    public void Main_package_type_is_the_one_with_most_pieces_tie_first_line()
    {
        Assert.Equal(20, TransportOrderDataSource.MainPackageTypeId(new[] { Line(1, 10, 2), Line(2, 20, 1), Line(3, 20, 2) }));
        Assert.Equal(10, TransportOrderDataSource.MainPackageTypeId(new[] { Line(1, 10, 2), Line(2, 20, 2) }));
        Assert.Equal(30, TransportOrderDataSource.MainPackageTypeId(new[] { Line(5, null, 9), Line(6, 30, 1) }));
        Assert.Null(TransportOrderDataSource.MainPackageTypeId(new[] { Line(1, null, 1) })); // entrega especial
        Assert.Null(TransportOrderDataSource.MainPackageTypeId(null));
    }

    [Fact]
    public void Text_caps_match_sql_columns()
    {
        Assert.Equal(500, StatusService.CommentMaxLength);                 // EntityStatusHistory.Comment NVARCHAR(500)
        Assert.Equal("El comentario admite como máximo 500 caracteres.", StatusService.CommentTooLongMessage);
        Assert.Equal(150, PortalUserService.EmailMaxLength);                // PortalUser.Email NVARCHAR(150)
    }
}
