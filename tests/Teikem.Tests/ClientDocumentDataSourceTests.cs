using Microsoft.EntityFrameworkCore;
using Teikem.Domain.Clients;
using Teikem.Domain.Constants;
using Teikem.Domain.Tax;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Analytics;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 248 (Lote D del plan) — fuente CLIENT_DOCUMENT: contrato (clave, campos, sin DateField, IdField y relación Client), vigencia de hoy por documento (estado, días,
/// de impuestos, respalda exención), renovados marcados RENEWED, q.Ids y solo la compañía actual. El permiso de lectura (clients.read en
/// PermissionCatalog.DataSourceExtraReadPermission) lo registra la integración del lote y lo prueba Lote248CatalogTests.
/// </summary>
public sealed class ClientDocumentDataSourceTests
{
    private static DateOnly Today => TenantClock.Default.Today;

    private static ClientDocumentDataSource Source(SalesVendorWorld x) => new(x.F.Db, x.F.Lookups, x.F.Tenant, TenantClock.Default);

    [Fact]
    public async Task Contract_is_a_current_state_source_with_the_client_relation()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var s = Source(x);
        Assert.Equal((ClientDocumentCodes.EntityType, (string?)null, (string?)null, "DocumentId"), (s.Key, s.EntityTypeCode, s.DateField, s.IdField));
        Assert.Equal(new[]
        {
            "DocumentId", "ClientId", "ClientCode", "ClientName", "DocumentType", "DocumentTypeLabel", "DocumentNumber", "ValidFrom", "ValidTo", "DaysToExpiry",
            "ExpiryState", "IsTax", "BacksExemption", "CreatedAtUtc",
        }, s.Fields.Select(f => f.Key));
        var rel = Assert.Single(s.Relations);
        Assert.Equal(("Client", EntityTypes.Client, "ClientId"), (rel.Key, rel.TargetSourceKey, rel.LocalField));
        Assert.Contains(s.Relations, r => s.Fields.Any(f => f.Key == r.LocalField));
    }

    [Fact]
    public async Task Rows_carry_today_validity_and_only_the_current_company()
    {
        await using var x = await SalesVendorWorld.CreateAsync();
        var marks = await x.F.Db.LookupCodes.AsTracking().SingleAsync(l => l.Entity == ClientDocumentCodes.TypeDomain && l.InternalCode == ClientDocumentCodes.IvuExemptionState);
        marks.ExtraJson = "{\"requiresValidTo\":true,\"isTax\":true}";
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();
        var c = await x.ClientAsync("C1");
        var old = await x.Documents.UploadAsync(c.PublicId, ClientDocumentCodes.IvuExemptionState, null, "a.png", "image/png", SalesVendorWorld.Png, default, "EX-1", null, Today.AddDays(4));
        var renewal = await x.Documents.UploadAsync(c.PublicId, ClientDocumentCodes.IvuExemptionState, null, "b.png", "image/png", SalesVendorWorld.Png, default, "EX-2", null, Today.AddDays(300));
        var other = await x.Documents.UploadAsync(c.PublicId, ClientDocumentCodes.Other, null, "c.png", "image/png", SalesVendorWorld.Png, default);
        var renewalRow = await x.F.Db.Set<ClientDocument>().AsNoTracking().SingleAsync(d => d.PublicId == renewal.PublicId);
        x.F.Db.Set<ClientTaxExemption>().Add(new ClientTaxExemption
        {
            PublicId = Guid.NewGuid(), TenantId = WmsFixture.TenantId, ClientId = c.ClientId, ExemptionType = "Estatal", CertificateNumber = "EX-2", ValidFrom = Today,
            ValidTo = Today.AddDays(300), IsActive = true, ClientDocumentId = renewalRow.ClientDocumentId, CreatedAtUtc = DateTime.UtcNow,
        });
        await x.F.Db.SaveChangesAsync();
        x.F.Db.ChangeTracker.Clear();

        var rows = await Source(x).LoadAsync(new DataQuery(), default);
        Assert.Equal(3, rows.Count);
        var byNumber = rows.ToDictionary(r => (string?)r["DocumentNumber"] ?? "-");
        Assert.Equal((ClientDocumentExpiryStates.Renewed, 4, true, false), ((string)byNumber["EX-1"]["ExpiryState"]!, (int)byNumber["EX-1"]["DaysToExpiry"]!, (bool)byNumber["EX-1"]["IsTax"]!,
            (bool)byNumber["EX-1"]["BacksExemption"]!));
        Assert.Equal((ClientDocumentExpiryStates.Valid, true, "C1", c.ClientId), ((string)byNumber["EX-2"]["ExpiryState"]!, (bool)byNumber["EX-2"]["BacksExemption"]!,
            (string)byNumber["EX-2"]["ClientCode"]!, (int)byNumber["EX-2"]["ClientId"]!));
        Assert.Equal((ClientDocumentExpiryStates.NoExpiry, (object?)null, ClientDocumentCodes.Other), ((string)byNumber["-"]["ExpiryState"]!, byNumber["-"]["DaysToExpiry"], (string)byNumber["-"]["DocumentType"]!));

        var oldId = (await x.F.Db.Set<ClientDocument>().AsNoTracking().SingleAsync(d => d.PublicId == old.PublicId)).ClientDocumentId;
        var one = await Source(x).LoadAsync(new DataQuery { Ids = new[] { oldId } }, default);
        Assert.Equal(oldId, (int)Assert.Single(one)["DocumentId"]!);
        Assert.Empty(await Source(x).LoadAsync(new DataQuery { Ids = Array.Empty<int>() }, default));
        Assert.NotNull(other);

        using (x.F.AsTenant(WmsFixture.OtherTenantId))
            Assert.Empty(await Source(x).LoadAsync(new DataQuery(), default));
    }
}
