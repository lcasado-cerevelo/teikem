using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Teikem.Api.Auth;
using Teikem.Api.Controllers;
using Teikem.Domain.Catalogs;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Services;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 1 de cambios de Almacén — catálogo global de localidades postales (dbo.PostalLocality): normalización de la búsqueda
/// (sin acentos ni mayúsculas, prefijo de ZIP), servicio de búsqueda, acceso del controlador (el de los catálogos de
/// referencia), modelo EF sin tenant y seed del catálogo USPS (42.522 ZIP; Puerto Rico con su municipio: 78 municipios).
/// </summary>
public sealed class PostalLocalityTests
{
    // ================================================================ reglas puras

    [Theory]
    [InlineData("Mayagüez", "MAYAGUEZ")]
    [InlineData("  san   germán ", "SAN GERMAN")]
    [InlineData("Añasco", "ANASCO")]
    [InlineData("RÍO GRANDE", "RIO GRANDE")]
    [InlineData("loíza", "LOIZA")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_removes_accents_case_and_extra_spaces(string? raw, string expected)
        => Assert.Equal(expected, PostalLocalityRules.Normalize(raw));

    [Theory]
    [InlineData("MAYA", "Mayagüez", "00680", 0)]
    [InlineData("GERMAN", "San Germán", "00683", 1)]
    [InlineData("AGU", "Mayagüez", "00680", 2)]
    [InlineData("PONCE", "Mayagüez", "00680", null)]
    [InlineData("0068", "Mayagüez", "00680", 0)]
    [InlineData("00949-1234", "Toa Baja", "00949", 0)]
    [InlineData("0095", "Toa Baja", "00949", null)]
    [InlineData("", "Toa Baja", "00949", 0)]
    public void Rank_matches_city_or_postal_code_prefix(string search, string city, string zip, int? expected)
        => Assert.Equal(expected, PostalLocalityRules.Rank(search, city, zip));

    [Theory]
    [InlineData("TOA BAJA", "SABANA SECA", "TOA BAJA", 0)]   // por municipio
    [InlineData("SABANA", "SABANA SECA", "TOA BAJA", 0)]     // por ciudad postal
    [InlineData("BAJA", "SABANA SECA", "TOA BAJA", 1)]       // palabra del municipio
    [InlineData("SECA", "SABANA SECA", null, 1)]             // sin municipio (fuera de PR)
    [InlineData("PONCE", "SABANA SECA", "TOA BAJA", null)]
    public void Rank_takes_the_best_of_postal_city_and_municipality(string search, string city, string? municipality, int? expected)
        => Assert.Equal(expected, PostalLocalityRules.RankNormalized(search, city, municipality, "00952"));

    [Theory]
    [InlineData("00949", true)]
    [InlineData("0094", true)]
    [InlineData("00949-1234", true)]
    [InlineData("-", false)]
    [InlineData("TOA", false)]
    [InlineData("A1", false)]
    public void Postal_code_search_is_only_digits(string search, bool expected)
        => Assert.Equal(expected, PostalLocalityRules.IsPostalCodeSearch(search));

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(7, 7)]
    [InlineData(500, 100)]
    public void Take_is_bounded(int take, int expected) => Assert.Equal(expected, PostalLocalityRules.Take(take));

    // ================================================================ servicio

    [Fact]
    public async Task Search_by_city_without_accents_or_by_postal_code_prefix()
    {
        await using var f = await WmsFixture.CreateAsync();
        var pr = f.LookupId(LookupDomains.Country, "PR");
        var us = f.LookupId(LookupDomains.Country, "US");
        f.Db.Set<PostalLocality>().AddRange(
            new PostalLocality { City = "MAYAGUEZ", PostalCode = "00680", State = "PR", Municipality = "Mayagüez", CountryLookupId = pr },
            new PostalLocality { City = "MAYAGUEZ", PostalCode = "00682", State = "PR", Municipality = "Mayagüez", CountryLookupId = pr },
            new PostalLocality { City = "SAN GERMAN", PostalCode = "00683", State = "PR", Municipality = "San Germán", CountryLookupId = pr },
            new PostalLocality { City = "TOA BAJA", PostalCode = "00949", State = "PR", Municipality = "Toa Baja", CountryLookupId = pr },
            new PostalLocality { City = "SABANA SECA", PostalCode = "00952", State = "PR", Municipality = "Toa Baja", CountryLookupId = pr },
            new PostalLocality { City = "SAN JUAN", PostalCode = "78589", State = "TX", CountryLookupId = us },
            new PostalLocality { City = "SAN JUAN", PostalCode = "00901", State = "PR", Municipality = "San Juan", CountryLookupId = pr },
            new PostalLocality { City = "MAYAGUEZ", PostalCode = "00681", State = "PR", Municipality = "Mayagüez", CountryLookupId = pr, IsActive = false });
        await f.Db.SaveChangesAsync();
        var svc = new PostalLocalityService(f.Db, f.Tenant, f.Get<ILookupCache>(), new MemoryCache(new MemoryCacheOptions()));

        var byCity = await svc.SearchAsync("mayaguez", 0, default);
        Assert.Equal(new[] { "00680", "00682" }, byCity.Select(l => l.PostalCode)); // la inactiva no sale
        Assert.All(byCity, l => Assert.Equal(("MAYAGUEZ", "Mayagüez", "PR", "PR"), (l.City, l.Municipality, l.State, l.CountryCode)));
        Assert.All(byCity, l => Assert.False(string.IsNullOrEmpty(l.Country)));

        // por municipio también sale el ZIP cuyo nombre postal es un barrio
        Assert.Equal(new[] { "00949", "00952" }, (await svc.SearchAsync("toa baja", 0, default)).Select(l => l.PostalCode).OrderBy(z => z));
        Assert.Equal(new[] { "00680", "00682", "00683" }, (await svc.SearchAsync("0068", 0, default)).Select(l => l.PostalCode));
        Assert.Equal(new[] { "SAN GERMAN" }, (await svc.SearchAsync("GERMAN", 0, default)).Select(l => l.City));
        // a igual rango, primero Puerto Rico (país por defecto de los almacenes)
        Assert.Equal(new[] { ("00901", "PR"), ("78589", "US") }, (await svc.SearchAsync("san juan", 0, default)).Select(l => (l.PostalCode, l.CountryCode)));
        Assert.Single(await svc.SearchAsync("", 1, default));
        Assert.Empty(await svc.SearchAsync("Ponce", 0, default));
    }

    // ================================================================ controlador

    [Fact]
    public void Controller_is_read_only_and_authenticated_like_the_reference_catalogs()
    {
        var t = typeof(PostalLocalitiesController);
        Assert.NotNull(t.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(t.GetCustomAttribute<RequireModuleAttribute>());
        // Mismo nivel que GET /api/v1/catalogs/{entity}: sin [RequirePermission].
        Assert.Null(typeof(CatalogsController).GetMethod(nameof(CatalogsController.Values))!.GetCustomAttribute<RequirePermissionAttribute>());
        var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var action = Assert.Single(actions);
        Assert.NotNull(action.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>());
        Assert.Null(action.GetCustomAttribute<RequirePermissionAttribute>());
    }

    // ================================================================ modelo y estructura

    [Fact]
    public void Postal_locality_is_a_global_table_mapped_one_to_one()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var e = db.Model.FindEntityType(typeof(PostalLocality))!;
        Assert.Equal("PostalLocality", e.GetTableName());
        Assert.Null(e.FindProperty("TenantId"));
        Assert.Null(e.GetQueryFilter());
        var uq = e.GetIndexes().Single(i => i.GetDatabaseName() == "UQ_PostalLocality");
        Assert.True(uq.IsUnique);
        Assert.Equal(new[] { "CountryLookupId", "PostalCode", "City" }, uq.Properties.Select(p => p.Name));
        Assert.Equal(100, e.FindProperty(nameof(PostalLocality.City))!.GetMaxLength());
        Assert.Equal(10, e.FindProperty(nameof(PostalLocality.PostalCode))!.GetMaxLength());
        Assert.Equal(100, e.FindProperty(nameof(PostalLocality.Municipality))!.GetMaxLength());

        var sql = File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-estructura.sql"));
        Assert.Contains("IF OBJECT_ID('dbo.PostalLocality') IS NULL", sql);
        Assert.Contains("CONSTRAINT UQ_PostalLocality UNIQUE (CountryLookupId, PostalCode, City)", sql);
        Assert.Contains("Municipality    NVARCHAR(100) NULL", sql);
        Assert.Contains("ELSE IF COL_LENGTH('dbo.PostalLocality', 'Municipality') IS NULL", sql);
        // Después de LookupCode (su FK) en el orden de capas.
        Assert.True(sql.IndexOf("CREATE TABLE dbo.LookupCode (", StringComparison.Ordinal) < sql.IndexOf("CREATE TABLE dbo.PostalLocality (", StringComparison.Ordinal));
    }

    // ================================================================ seed (catálogo USPS; Puerto Rico con municipio)

    private static readonly string[] Municipalities =
    {
        "Adjuntas", "Aguada", "Aguadilla", "Aguas Buenas", "Aibonito", "Añasco", "Arecibo", "Arroyo", "Barceloneta", "Barranquitas",
        "Bayamón", "Cabo Rojo", "Caguas", "Camuy", "Canóvanas", "Carolina", "Cataño", "Cayey", "Ceiba", "Ciales", "Cidra", "Coamo",
        "Comerío", "Corozal", "Culebra", "Dorado", "Fajardo", "Florida", "Guánica", "Guayama", "Guayanilla", "Guaynabo", "Gurabo",
        "Hatillo", "Hormigueros", "Humacao", "Isabela", "Jayuya", "Juana Díaz", "Juncos", "Lajas", "Lares", "Las Marías", "Las Piedras",
        "Loíza", "Luquillo", "Manatí", "Maricao", "Maunabo", "Mayagüez", "Moca", "Morovis", "Naguabo", "Naranjito", "Orocovis",
        "Patillas", "Peñuelas", "Ponce", "Quebradillas", "Rincón", "Río Grande", "Sabana Grande", "Salinas", "San Germán", "San Juan",
        "San Lorenzo", "San Sebastián", "Santa Isabel", "Toa Alta", "Toa Baja", "Trujillo Alto", "Utuado", "Vega Alta", "Vega Baja",
        "Vieques", "Villalba", "Yabucoa", "Yauco",
    };

    private sealed record SeedRow(string City, string Zip, string State, string? Municipality, string Country);

    private static string Seed() => File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-seed.sql"));

    private static List<SeedRow> SeedRows()
    {
        var seed = Seed();
        var start = seed.IndexOf("INSERT INTO dbo.PostalLocality", StringComparison.Ordinal);
        Assert.True(start >= 0, "No están los INSERT de PostalLocality en el seed.");
        var end = seed.IndexOf("PRINT 'Seed completado", start, StringComparison.Ordinal);
        return Regex.Matches(seed[start..end], @"\(N'([^']+)','(\d{5})','([A-Z]{2})',(?:N'([^']+)'|NULL),'([A-Z]{2})'\)")
            .Select(m => new SeedRow(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value,
                m.Groups[4].Success ? m.Groups[4].Value : null, m.Groups[5].Value))
            .ToList();
    }

    [Fact]
    public void Seed_has_the_usps_catalog_and_the_78_municipalities_of_puerto_rico()
    {
        var rows = SeedRows();
        Assert.Equal(42_522, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => (r.Country, r.Zip, r.City)).Distinct().Count()); // UQ_PostalLocality
        Assert.Equal(new[] { "PR", "US" }, rows.Select(r => r.Country).Distinct().OrderBy(c => c, StringComparer.Ordinal));

        var pr = rows.Where(r => r.Country == "PR").ToList();
        Assert.Equal(176, pr.Count);
        Assert.All(pr, r => Assert.Equal("PR", r.State));
        Assert.All(pr, r => Assert.InRange(string.CompareOrdinal(r.Zip, "00601"), 0, int.MaxValue));
        Assert.All(pr, r => Assert.InRange(string.CompareOrdinal(r.Zip, "00988"), int.MinValue, 0));
        Assert.All(pr, r => Assert.False(string.IsNullOrEmpty(r.Municipality)));
        Assert.Equal(78, Municipalities.Length);
        Assert.Equal(Municipalities.OrderBy(m => m, StringComparer.Ordinal), pr.Select(r => r.Municipality!).Distinct().OrderBy(m => m, StringComparer.Ordinal));
        Assert.All(rows.Where(r => r.Country == "US"), r => Assert.Null(r.Municipality));

        // Referencias conocidas: la zona de despacho demo (R-01..R-04), el almacén demo de Toa Baja y barrios con ZIP propio.
        foreach (var (municipality, zip) in new[] { ("Toa Baja", "00949"), ("Bayamón", "00959"), ("Caguas", "00725"), ("Aguas Buenas", "00703"),
                     ("Carolina", "00979"), ("Trujillo Alto", "00976"), ("Guaynabo", "00969"), ("San Juan", "00907"), ("Ponce", "00715"),
                     ("Mayagüez", "00680"), ("Toa Baja", "00952"), ("Lares", "00631"), ("Trujillo Alto", "00978") })
            Assert.Contains(pr, r => r.Municipality == municipality && r.Zip == zip);
        Assert.Contains(pr, r => r is { Zip: "00952", City: "SABANA SECA" });
        Assert.Contains(rows, r => r is { Zip: "10001", City: "NEW YORK", State: "NY", Country: "US" });
    }

    [Fact]
    public void Seed_uses_the_country_catalog_and_only_inserts()
    {
        var seed = Seed();
        Assert.Equal("PR", Teikem.Domain.Wms.WarehouseRules.DefaultCountry);
        var start = seed.IndexOf("INSERT INTO dbo.PostalLocality", StringComparison.Ordinal);
        var block = seed[start..seed.IndexOf("PRINT 'Seed completado", start, StringComparison.Ordinal)];
        Assert.Contains("JOIN dbo.LookupCode c ON c.Entity = 'Country' AND c.InternalCode = v.Country", block);
        // cada lote es idempotente: solo inserta lo que falta
        var inserts = Regex.Matches(block, "INSERT INTO dbo.PostalLocality").Count;
        Assert.Equal(inserts, Regex.Matches(block, @"WHERE NOT EXISTS \(SELECT 1 FROM dbo\.PostalLocality p").Count);
        Assert.DoesNotContain("DELETE", block);
        Assert.DoesNotContain("UPDATE", block);
    }
}
