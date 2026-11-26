using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Teikem.Domain.Clients;
using Teikem.Domain.Common;
using Teikem.Domain.Tax;
using Teikem.Domain.Tenancy;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 (Lote D del plan) — el modelo EF del lote es 1:1 con su sección de logistica-db-update.sql: los índices (nombre, tabla, columnas, INCLUDE, único y
/// filtro) se buscan TEXTUALMENTE en la sección, los largos y tipos de las columnas nuevas coinciden, y las tres entidades nuevas del período llevan el filtro
/// global de compañía (ITenantScoped) y su bitácora.
/// </summary>
public sealed class Lote248ModelTests
{
    private static string Code() => Lote248SqlTests.Statements(Lote248SqlTests.Section());

    /// <summary>El índice tal como lo escribe la sección: «CREATE [UNIQUE ]INDEX nombre ON dbo.Tabla(col, col)[ INCLUDE (…)][ WHERE filtro];».</summary>
    private static string ExpectedSql(IIndex index)
    {
        var table = index.DeclaringEntityType.GetTableName();
        var cols = string.Join(", ", index.Properties.Select(p => p.GetColumnName()));
        var include = index.GetIncludeProperties() is { Count: > 0 } inc ? $" INCLUDE ({string.Join(", ", inc)})" : string.Empty;
        var filter = index.GetFilter() is { } f ? " WHERE " + f.Replace("[", string.Empty).Replace("]", string.Empty) : string.Empty;
        return $"CREATE {(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX {index.GetDatabaseName()} ON dbo.{table}({cols}){include}{filter};";
    }

    public static TheoryData<Type, string> LotIndexes => new()
    {
        { typeof(ClientDocument), "IX_ClientDocument_ValidTo" },
        { typeof(ClientTaxExemption), "UX_ClientTaxExemption_Document" },
        { typeof(TaxExemptionPeriod), "UX_TaxExemptionPeriod_PublicId" },
        { typeof(TaxExemptionPeriod), "IX_TaxExemptionPeriod_Dates" },
        { typeof(TaxExemptionPeriodProduct), "UX_TaxExemptionPeriodProduct" },
        { typeof(TaxExemptionPeriodCategory), "UX_TaxExemptionPeriodCategory" },
    };

    [Theory]
    [MemberData(nameof(LotIndexes))]
    public void Every_ef_index_of_the_lot_is_written_identically_in_the_section(Type entity, string name)
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var model = db.GetService<IDesignTimeModel>().Model;   // el INCLUDE solo vive en el modelo de diseño
        var index = Assert.Single(model.FindEntityType(entity)!.GetIndexes(), i => i.GetDatabaseName() == name);
        Assert.Contains(ExpectedSql(index), Code());
        Assert.Contains($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{name}' AND object_id = OBJECT_ID('dbo.{index.DeclaringEntityType.GetTableName()}'))", Code());
    }

    [Fact]
    public void The_section_creates_no_index_the_ef_model_does_not_know()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var known = db.Model.GetEntityTypes().SelectMany(e => e.GetIndexes()).Select(i => i.GetDatabaseName()).ToHashSet(StringComparer.Ordinal);
        var created = Regex.Matches(Code(), @"CREATE (?:UNIQUE )?INDEX (\w+)").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(LotIndexes.Count(), created.Count);
        foreach (var name in created) Assert.Contains(name, known);
    }

    [Fact]
    public void Column_lengths_and_types_match_the_section()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        var code = Code();

        var doc = db.Model.FindEntityType(typeof(ClientDocument))!;
        Assert.Equal(ClientDocumentRules.MaxNumberLength, doc.FindProperty(nameof(ClientDocument.DocumentNumber))!.GetMaxLength());
        Assert.Contains($"ADD DocumentNumber NVARCHAR({ClientDocumentRules.MaxNumberLength}) NULL;", code);
        foreach (var p in new[] { nameof(ClientDocument.ValidFrom), nameof(ClientDocument.ValidTo) })
        {
            Assert.Equal("date", doc.FindProperty(p)!.GetColumnType());
            Assert.Contains($"ADD {p} DATE NULL;", code);
        }
        Assert.True(doc.FindProperty(nameof(ClientDocument.RowVersion))!.IsConcurrencyToken);
        Assert.Contains("ADD RowVersion ROWVERSION NOT NULL;", code);

        var ex = db.Model.FindEntityType(typeof(ClientTaxExemption))!;
        Assert.True(ex.FindProperty(nameof(ClientTaxExemption.ClientDocumentId))!.IsNullable);
        Assert.Contains("ALTER TABLE dbo.ClientTaxExemption ADD ClientDocumentId INT NULL;", code);
        var fk = Assert.Single(ex.GetForeignKeys(), f => f.Properties.Single().Name == nameof(ClientTaxExemption.ClientDocumentId));
        Assert.Equal(typeof(ClientDocument), fk.PrincipalEntityType.ClrType);
        Assert.False(fk.IsRequired);

        var tenant = db.Model.FindEntityType(typeof(Tenant))!;
        var thresholds = tenant.FindProperty(nameof(Tenant.DocExpiryThresholdDays))!;
        Assert.Equal(60, thresholds.GetMaxLength());
        Assert.False(thresholds.IsNullable);
        Assert.Contains("ADD DocExpiryThresholdDays NVARCHAR(60) NOT NULL CONSTRAINT DF_Tenant_DocExpiryThresholdDays DEFAULT N'30,15,5';", code);
        Assert.Equal(DocumentExpiryCodes.DefaultThresholds, new Tenant().DocExpiryThresholdDays);
        Assert.Contains("ADD DocExpiryDailyAfterExpiry BIT NOT NULL CONSTRAINT DF_Tenant_DocExpiryDailyAfterExpiry DEFAULT 0;", code);
        Assert.Contains("ADD DocExpiryNotifyClient BIT NOT NULL CONSTRAINT DF_Tenant_DocExpiryNotifyClient DEFAULT 0;", code);

        var period = db.Model.FindEntityType(typeof(TaxExemptionPeriod))!;
        Assert.Equal("TaxExemptionPeriod", period.GetTableName());
        Assert.Equal(100, period.FindProperty(nameof(TaxExemptionPeriod.Name))!.GetMaxLength());
        Assert.Contains("Name                 NVARCHAR(100) NOT NULL,", code);
        Assert.Equal(300, period.FindProperty(nameof(TaxExemptionPeriod.Reason))!.GetMaxLength());
        Assert.Contains("Reason               NVARCHAR(300) NOT NULL,", code);
        Assert.Equal(20, period.FindProperty(nameof(TaxExemptionPeriod.Scope))!.GetMaxLength());
        Assert.Contains("Scope                NVARCHAR(20) NOT NULL CONSTRAINT DF_TaxExemptionPeriod_Scope DEFAULT N'ALL',", code);
        Assert.Equal(TaxExemptionPeriodScopes.All, new TaxExemptionPeriod().Scope);
        Assert.Contains("CHECK (Scope IN (N'ALL', N'SELECTED'))", code);
        Assert.Equal(new[] { "ALL", "SELECTED" }, TaxExemptionPeriodScopes.Values);
        foreach (var p in new[] { nameof(TaxExemptionPeriod.ValidFrom), nameof(TaxExemptionPeriod.ValidTo) })
        {
            Assert.Equal("date", period.FindProperty(p)!.GetColumnType());
            Assert.False(period.FindProperty(p)!.IsNullable);
        }
        Assert.True(period.FindProperty(nameof(TaxExemptionPeriod.RowVersion))!.IsConcurrencyToken);
        Assert.Equal("NEWID()", period.FindProperty(nameof(TaxExemptionPeriod.PublicId))!.GetDefaultValueSql());

        Assert.Equal("TaxExemptionPeriodProduct", db.Model.FindEntityType(typeof(TaxExemptionPeriodProduct))!.GetTableName());
        Assert.Equal("TaxExemptionPeriodCategory", db.Model.FindEntityType(typeof(TaxExemptionPeriodCategory))!.GetTableName());
    }

    [Theory]
    [InlineData(typeof(TaxExemptionPeriod))]
    [InlineData(typeof(TaxExemptionPeriodProduct))]
    [InlineData(typeof(TaxExemptionPeriodCategory))]
    public void The_new_entities_have_the_tenant_filter_soft_delete_and_audit(Type entity)
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        Assert.True(typeof(ITenantScoped).IsAssignableFrom(entity));
        Assert.True(typeof(ISoftDeletable).IsAssignableFrom(entity));
        Assert.NotNull(db.Model.FindEntityType(entity)!.GetQueryFilter());   // filtro global de compañía
        Assert.Equal(TaxCodes.ExemptionPeriodEntityType, entity.GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
    }

    [Fact]
    public void The_db_context_exposes_the_period_sets()
    {
        using var db = TenantIsolationModelTests.CreateSqlServerModelContext();
        Assert.NotNull(db.TaxExemptionPeriods);
        Assert.NotNull(db.TaxExemptionPeriodProducts);
        Assert.NotNull(db.TaxExemptionPeriodCategories);
        // la fila del documento sigue auditada como CLIENT_DOCUMENT y el toque de concurrencia no
        Assert.Equal(ClientDocumentCodes.EntityType, typeof(ClientDocument).GetCustomAttribute<AuditEntityAttribute>()!.EntityTypeCode);
        foreach (var p in new[] { nameof(ClientDocument.RowVersion), nameof(ClientDocument.UpdatedAtUtc) })
            Assert.NotNull(typeof(ClientDocument).GetProperty(p)!.GetCustomAttribute<NotAuditedAttribute>());
        foreach (var p in new[] { nameof(ClientDocument.DocumentNumber), nameof(ClientDocument.ValidFrom), nameof(ClientDocument.ValidTo) })
            Assert.Null(typeof(ClientDocument).GetProperty(p)!.GetCustomAttribute<NotAuditedAttribute>());
    }
}
