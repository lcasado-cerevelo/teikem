using System.Text.RegularExpressions;
using Teikem.Domain.Constants;
using Teikem.Domain.Orders;
using Teikem.Domain.Wms;
using Xunit;

namespace Teikem.Tests;

/// <summary>
/// Lote 27 (Rentas R1) — reglas puras de la renta (mensajes exactos del plan, estatus, fechas, "por vencer"/"vencida", tarifas y
/// equipos) y el espejo de catálogos, permisos y esquema en Diseño/logistica-db-seed.sql y Diseño/logistica-db-estructura.sql.
/// </summary>
public sealed class RentalRulesTests
{
    private static readonly Lazy<string> Seed = new(() => File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-seed.sql")).Replace("\r\n", "\n"));
    private static readonly Lazy<string> Structure = new(() => File.ReadAllText(Path.Combine(TripCatalogTests.RepoRoot(), "Diseño", "logistica-db-estructura.sql")).Replace("\r\n", "\n"));
    private static bool SeedHas(string domain, string code) => Seed.Value.Contains($"('{domain}','{code}',", StringComparison.Ordinal);

    // ---------------------------------------------------------------- mensajes del plan (exactos)

    [Fact]
    public void Plan_messages_are_exact()
    {
        Assert.Equal("Indique la localidad del cliente donde estará el equipo.", RentalRules.LocationRequired);
        Assert.Equal("La localidad no pertenece al cliente de la renta.", RentalRules.LocationNotOfClient);
        Assert.Equal("La fecha de recogido no puede ser anterior a la de inicio.", RentalRules.PickupBeforeStart);
        Assert.Equal("Solo se rentan equipos propios; EQ-1 pertenece a un cliente.", RentalRules.OnlyOwnEquipment("EQ-1"));
        Assert.Equal("El producto EQ-1 no se controla por serie; solo se rentan equipos con número de serie.", RentalRules.NotSerialTracked("EQ-1"));
        Assert.Equal("La serie S-1 ya está en la renta REN-00001.", RentalRules.SerialInRental("S-1", "REN-00001"));
        Assert.Equal("La renta REN-00001 ya fue despachada; no se puede modificar.", RentalRules.AlreadyDispatched("REN-00001"));
        Assert.Equal("La renta no tiene equipos; agregue al menos uno.", RentalRules.NoLines);
        Assert.Equal("La tarifa no puede ser negativa.", RentalRules.NegativeRate);
        Assert.Equal("Solo se cancela una renta en Borrador o Programada; para terminarla registre la devolución.", RentalRules.CancelNotAllowed);
        Assert.Equal("Solo se extiende una renta Programada o En renta.", RentalRules.ExtendNotAllowed);
        Assert.Equal("La nueva fecha de recogido debe ser posterior a la actual (2026-10-31).", RentalRules.NewPickupNotLater(new DateOnly(2026, 10, 31)));
        Assert.Equal("Indique el motivo de la extensión.", RentalRules.ExtensionReasonRequired);
        // Reutilizados del código.
        Assert.Equal("La serie S-1 no está disponible en A-01.", SerialRules.NotAvailable("S-1", "A-01"));
        Assert.Equal("El cliente está dado de baja; solo se consulta su historial.", Teikem.Infrastructure.Clients.ClientQueries.ClientInactiveMessage);
    }

    // ---------------------------------------------------------------- estatus

    [Theory]
    [InlineData(RentalStatuses.Draft, true, false, true, false)]
    [InlineData(RentalStatuses.Scheduled, true, true, true, true)]
    [InlineData(RentalStatuses.OnRent, false, true, false, true)]
    [InlineData(RentalStatuses.Returned, false, false, false, false)]
    [InlineData(RentalStatuses.Cancelled, false, false, false, false)]
    public void What_each_status_allows(string status, bool editable, bool open, bool cancel, bool extend)
    {
        Assert.Equal(editable, RentalRules.IsEditable(status));
        Assert.Equal(open, RentalRules.IsOpen(status));
        Assert.Equal(cancel, RentalRules.CanCancel(status));
        Assert.Equal(extend, RentalRules.CanExtend(status));
        Assert.Equal(editable, RentalRules.EditError(status, "REN-00001") is null);
    }

    [Fact]
    public void Status_errors_say_why()
    {
        Assert.Equal(RentalRules.AlreadyDispatched("REN-00002"), RentalRules.EditError(RentalStatuses.OnRent, "REN-00002"));
        Assert.Equal(RentalRules.AlreadyDispatched("REN-00002"), RentalRules.EditError(RentalStatuses.Returned, "REN-00002"));
        Assert.Equal("La renta REN-00002 está cancelada; solo se consulta.", RentalRules.EditError(RentalStatuses.Cancelled, "REN-00002"));
        Assert.Null(RentalRules.ScheduleError(RentalStatuses.Draft, "REN-00002"));
        Assert.Equal("Solo se programa una renta en Borrador; la renta REN-00002 no lo está.", RentalRules.ScheduleError(RentalStatuses.Scheduled, "REN-00002"));
        Assert.Equal(RentalRules.AlreadyDispatched("REN-00002"), RentalRules.ScheduleError(RentalStatuses.OnRent, "REN-00002"));
        Assert.Null(RentalRules.DispatchError(RentalStatuses.Scheduled, "REN-00002"));
        Assert.Equal("Solo se despacha una renta Programada; programe la renta REN-00002 primero.", RentalRules.DispatchError(RentalStatuses.Draft, "REN-00002"));
        Assert.Equal(RentalRules.AlreadyDispatched("REN-00002"), RentalRules.DispatchError(RentalStatuses.OnRent, "REN-00002"));
        Assert.Equal(RentalRules.Cancelled("REN-00002"), RentalRules.DispatchError(RentalStatuses.Cancelled, "REN-00002"));
    }

    // ---------------------------------------------------------------- fechas

    [Fact]
    public void Dates_overdue_and_due_soon_are_computed_with_today()
    {
        var today = new DateOnly(2026, 10, 5);
        Assert.Null(RentalRules.ValidateDates(today, today));
        Assert.Equal(RentalRules.PickupBeforeStart, RentalRules.ValidateDates(today, today.AddDays(-1)));
        Assert.Equal(-2, RentalRules.DaysToPickup(today.AddDays(-2), today));
        Assert.Equal(3, RentalRules.DaysToPickup(today.AddDays(3), today));

        // Vencida = abierta con el recogido ANTES de hoy; el día del recogido todavía no.
        Assert.True(RentalRules.IsOverdue(RentalStatuses.OnRent, today.AddDays(-1), today));
        Assert.True(RentalRules.IsOverdue(RentalStatuses.Scheduled, today.AddDays(-1), today));
        Assert.False(RentalRules.IsOverdue(RentalStatuses.OnRent, today, today));
        Assert.False(RentalRules.IsOverdue(RentalStatuses.Draft, today.AddDays(-9), today));
        Assert.False(RentalRules.IsOverdue(RentalStatuses.Returned, today.AddDays(-9), today));
        // Por vencer en N días: de hoy a hoy + N (ambos inclusive), solo abiertas.
        Assert.True(RentalRules.IsDueWithin(RentalStatuses.OnRent, today, today, 7));
        Assert.True(RentalRules.IsDueWithin(RentalStatuses.OnRent, today.AddDays(7), today, 7));
        Assert.False(RentalRules.IsDueWithin(RentalStatuses.OnRent, today.AddDays(8), today, 7));
        Assert.False(RentalRules.IsDueWithin(RentalStatuses.OnRent, today.AddDays(-1), today, 7));
        Assert.False(RentalRules.IsDueWithin(RentalStatuses.Cancelled, today, today, 7));

        // Extensión: la nueva fecha debe ser posterior (el mismo día no vale); la tarifa nueva empieza el día siguiente.
        Assert.Null(RentalRules.ValidateExtension(today, today.AddDays(1)));
        Assert.Equal(RentalRules.NewPickupNotLater(today), RentalRules.ValidateExtension(today, today));
        Assert.Equal(today.AddDays(1), RentalRules.ExtensionRateStart(today));
    }

    // ---------------------------------------------------------------- tarifas, textos y equipos

    [Fact]
    public void Rates_frequency_amount_and_change()
    {
        Assert.Equal(("ONE_TIME", (string?)null), RentalRules.NormalizeFrequency(" one_time "));
        Assert.Equal(("DAILY", (string?)null), RentalRules.NormalizeFrequency("Daily"));
        Assert.Equal(RentalRules.FrequencyRequired, RentalRules.NormalizeFrequency(null).Error);
        Assert.Equal("Frecuencia de cobro desconocida: 'YEARLY'. Use DAILY, WEEKLY, MONTHLY o ONE_TIME.", RentalRules.NormalizeFrequency("YEARLY").Error);
        Assert.Null(RentalRules.ValidateRateAmount(0m));
        Assert.Equal(RentalRules.NegativeRate, RentalRules.ValidateRateAmount(-0.01m));
        Assert.Equal(RentalRules.RateAmountRequired, RentalRules.ValidateRateAmount(null));
        Assert.False(RentalRules.RateChanged(1, 10m, 2, 1, 10m, 2));
        Assert.True(RentalRules.RateChanged(1, 10m, 2, 1, 12m, 2));
        Assert.True(RentalRules.RateChanged(1, 10m, 2, 3, 10m, 2));
        Assert.True(RentalRules.RateChanged(1, 10m, 2, 1, 10m, 4));
        Assert.Equal(new[] { "DAILY", "WEEKLY", "MONTHLY", "ONE_TIME" }, RentalBillingFrequencies.All);
    }

    [Fact]
    public void Texts_reasons_and_equipment()
    {
        Assert.Equal(((string?)null, (string?)null), RentalRules.NormalizeText("  ", 80, "x"));
        Assert.Equal(("C-1", (string?)null), RentalRules.NormalizeText(" C-1 ", 80, "x"));
        Assert.Equal(((string?)null, RentalRules.ContractNumberTooLong), RentalRules.NormalizeText(new string('x', 81), 80, RentalRules.ContractNumberTooLong));
        Assert.Equal(RentalRules.ExtensionReasonRequired, RentalRules.NormalizeReason(" ").Error);
        Assert.Equal("El motivo admite como máximo 300 caracteres.", RentalRules.NormalizeReason(new string('m', 301)).Error);
        Assert.Equal("Cliente pidió una semana más", RentalRules.NormalizeReason(" Cliente pidió una semana más ").Value);

        Assert.Null(RentalRules.CheckEquipment("EQ-1", isOwn: true, isSerial: true));
        Assert.Equal(RentalRules.OnlyOwnEquipment("EQ-1"), RentalRules.CheckEquipment("EQ-1", isOwn: false, isSerial: true));
        Assert.Equal(RentalRules.NotSerialTracked("EQ-1"), RentalRules.CheckEquipment("EQ-1", isOwn: true, isSerial: false));
        Assert.Equal("Renta REN-00007", RentalRules.DispatchNotes("REN-00007"));
        Assert.Equal(("RENT", "EN-RENTA"), (RentalRules.RentalZoneCode, RentalRules.RentalBinCode));
    }

    [Fact]
    public void Serials_on_rent_and_in_process_are_in_stock_and_kardex_references_read_well()
    {
        Assert.Equal(new[] { "AVAILABLE", "RESERVED", "ON_RENT", "IN_PROCESS" }, SerialStatuses.InStock);
        Assert.True(CycleCountRules.IsInStock(SerialStatuses.OnRent));
        Assert.True(CycleCountRules.IsInStock(SerialStatuses.InProcess));
        Assert.False(CycleCountRules.IsInStock(SerialStatuses.Shipped));

        Assert.Equal("Renta REN-00001", KardexRules.RefLabel(EntityTypes.Rental, 1, "REN-00001"));
        Assert.Equal("Rental REN-00001", KardexRules.RefLabel(EntityTypes.Rental, 1, "REN-00001", "en"));
        Assert.Equal("Devolución de renta DRN-00002", KardexRules.RefLabel(EntityTypes.RentalReturn, 2, "DRN-00002"));
        Assert.Equal("Rental return DRN-00002", KardexRules.RefLabel(EntityTypes.RentalReturn, 2, "DRN-00002", "en"));
        Assert.Equal("Proceso #7", KardexRules.RefLabel(EntityTypes.RentalProcess, 7));
        Assert.Equal("Process #7", KardexRules.RefLabel(EntityTypes.RentalProcess, 7, null, "en"));
        Assert.Equal("RENTAL·9", KardexRules.RefLabel(EntityTypes.Rental, 9));
    }

    [Fact]
    public void Numbering_rental_and_rental_return()
    {
        Assert.True(NumberingRules.IsKnownKind(NumberKinds.Rental));
        Assert.True(NumberingRules.IsKnownKind(NumberKinds.RentalReturn));
        Assert.Equal("REN-00001", WmsNumbering.Format(NumberKinds.Rental, 1));
        Assert.Equal("DRN-00012", WmsNumbering.Format(NumberKinds.RentalReturn, 12));
        foreach (var kind in new[] { "RENTAL", "RENTALRETURN" })
            Assert.Matches(new Regex($@"CK_NumberSequence_Kind CHECK \(Kind IN \([^)]*'{kind}'[^)]*\)\)"), Structure.Value);
    }

    // ---------------------------------------------------------------- espejo en el seed

    [Fact]
    public void Seed_has_the_rentals_module_statuses_lookups_and_lateral_rule()
    {
        var seed = Seed.Value;
        Assert.Contains("('RENTAL_EQUIPMENT','Rentas','Rentals',", seed);
        Assert.Contains("'Almacen','WMS_LOTSERIAL',50)", seed);
        foreach (var domain in new[] { "('RentalStatus',2,", "('RentalProcessStatus',2,", "('RentalReturnReason',1,", "('RentalReturnCondition',1," })
            Assert.Contains(domain, seed);
        Assert.Contains("('SerialStatus','ON_RENT','En renta','On rent',@LAT,", seed);
        Assert.Contains("('SerialStatus','IN_PROCESS','En proceso','In process',@LAT,", seed);
        Assert.Contains("('RentalStatus','DRAFT','Borrador','Draft',@PIPE,1,'#9CA3AF',1)", seed);
        Assert.Contains("('RentalStatus','SCHEDULED','Programada','Scheduled',@PIPE,2,", seed);
        Assert.Contains("('RentalStatus','ON_RENT','En renta','On rent',@PIPE,3,", seed);
        Assert.Contains("('RentalStatus','RETURNED','Devuelta','Returned',@TERM,4,", seed);
        Assert.Contains("('RentalStatus','CANCELLED','Cancelada','Cancelled',@TERM,5,", seed);
        foreach (var (code, kind) in new[] { ("PENDING", "@PIPE"), ("INSPECTION", "@PIPE"), ("CLEANING", "@PIPE"), ("TESTING", "@PIPE"), ("READY", "@TERM"),
                     ("REPAIR", "@LAT"), ("AWAITING_PARTS", "@LAT"), ("SCRAPPED", "@TERM") })
            Assert.Matches(new Regex($@"\('RentalProcessStatus','{code}','[^']+','[^']+',{kind},"), seed);
        Assert.True(SeedHas(LookupDomains.ZoneType, ZoneTypes.Rental));
        Assert.All(RentalBillingFrequencies.All, f => Assert.True(SeedHas(LookupDomains.RentalBillingFrequency, f), f));
        Assert.Contains("UPDATE dbo.LookupCode SET LabelJson = N'{\"es\":\"Fija\",\"en\":\"Fixed\"}'", seed);
        foreach (var code in new[] { RentalReturnReasons.EndOfContract, RentalReturnReasons.EarlyDamage, RentalReturnReasons.EarlyClient, RentalReturnReasons.Other })
            Assert.True(SeedHas(LookupDomains.RentalReturnReason, code), code);
        foreach (var code in new[] { RentalReturnConditions.Good, RentalReturnConditions.Damaged, RentalReturnConditions.Incomplete })
            Assert.True(SeedHas(LookupDomains.RentalReturnCondition, code), code);
        foreach (var e in new[] { EntityTypes.Rental, EntityTypes.RentalReturn, EntityTypes.RentalProcess })
            Assert.True(SeedHas(LookupDomains.EntityType, e), e);
        Assert.Contains("WHERE Entity = 'EntityType' AND InternalCode IN ('RENTAL_ASSET','RENTAL_CONTRACT') AND IsActive = 1;", seed);
        Assert.True(SeedHas(LookupDomains.AdjustmentReason, AdjustmentReasons.TrackingConversion));

        // 3I: CANCELLED solo desde DRAFT y SCHEDULED.
        var start = seed.IndexOf("3I) STATUS LATERAL ENTRY", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var block = seed[start..seed.IndexOf("4) PERMISOS", start, StringComparison.Ordinal)];
        Assert.Contains("et.InternalCode='RENTAL'", block);
        Assert.Contains("lat.Entity='RentalStatus' AND lat.InternalCode='CANCELLED'", block);
        Assert.Contains("frm.Entity='RentalStatus' AND frm.InternalCode IN ('DRAFT','SCHEDULED')", block);
    }

    [Fact]
    public void Permissions_rental_extend_and_return_are_in_the_catalog_the_seed_and_the_warehouse_operator_template()
    {
        var extend = Assert.Single(PermissionCatalog.All, p => p.Code == "rental.extend");
        Assert.Equal(("RENTAL", "Extender rentas", "Extend rentals"), (extend.Category, extend.LabelEs, extend.LabelEn));
        var ret = Assert.Single(PermissionCatalog.All, p => p.Code == "rental.return");
        Assert.Equal(("RENTAL", "Registrar devoluciones de renta", "Register rental returns"), (ret.Category, ret.LabelEs, ret.LabelEn));
        Assert.Contains("('rental.extend','RENTAL','Extender rentas','Extend rentals')", Seed.Value);
        Assert.Contains("('rental.return','RENTAL','Registrar devoluciones de renta','Register rental returns')", Seed.Value);

        var t = PermissionCatalog.RoleTemplates;
        foreach (var code in new[] { PermissionCatalog.RentalExtend, PermissionCatalog.RentalReturn })
        {
            Assert.Contains(code, t["WarehouseOperator"]);
            Assert.Contains(code, t["TenantAdmin"]);
            Assert.Contains($"('WarehouseOperator','{code}')", Seed.Value);
            Assert.All(t.Where(kv => kv.Key is not ("TenantAdmin" or "WarehouseOperator")), kv => Assert.DoesNotContain(code, kv.Value));
        }
        // 5b3: una sola vez, a los roles de tenant que gestionan rentas.
        Assert.Contains("5b3) Lote 27", Seed.Value);
        Assert.Contains("JOIN #L27RentalIsNew n ON n.IsNew = 1", Seed.Value);
        Assert.Contains("rm.Code = 'rental.manage'", Seed.Value);

        // Mapas de dueño (rutas polimórficas e historial de estatus).
        Assert.Equal(PermissionCatalog.RentalView, PermissionCatalog.OwnerReadPermission[EntityTypes.Rental]);
        Assert.Equal(PermissionCatalog.RentalView, PermissionCatalog.OwnerReadPermission[EntityTypes.RentalReturn]);
        Assert.Equal(PermissionCatalog.RentalView, PermissionCatalog.OwnerReadPermission[EntityTypes.RentalProcess]);
        Assert.Equal(PermissionCatalog.RentalManage, PermissionCatalog.OwnerWritePermission[EntityTypes.Rental]);
        Assert.Equal(PermissionCatalog.RentalReturn, PermissionCatalog.OwnerWritePermission[EntityTypes.RentalReturn]);
        Assert.Equal(PermissionCatalog.RentalMaintenance, PermissionCatalog.OwnerWritePermission[EntityTypes.RentalProcess]);
    }

    // ---------------------------------------------------------------- esquema

    [Fact]
    public void Layer_16C_is_rewritten_guarded_and_retires_the_old_tables_only_when_empty()
    {
        var sql = Structure.Value;
        foreach (var table in new[] { "Rental", "RentalLine", "RentalExtension", "RentalLineRate", "RentalReturn", "RentalReturnLine", "RentalProcess", "RentalCharge" })
            Assert.Contains($"IF OBJECT_ID('dbo.{table}') IS NULL\nBEGIN\n    CREATE TABLE dbo.{table} (", sql);
        Assert.DoesNotContain("CREATE TABLE dbo.RentalAsset", sql);
        Assert.DoesNotContain("CREATE TABLE dbo.RentalContract", sql);
        Assert.DoesNotContain("CREATE TABLE dbo.RentalBillingRule", sql);
        Assert.DoesNotContain("CREATE TABLE dbo.RentalAssetMaintenance", sql);
        // Retiro solo con todas vacías (si no, PRINT y se dejan).
        Assert.Contains("IF @OldRentalRows > 0", sql);
        Assert.Contains("IF OBJECT_ID('dbo.RentalContract') IS NOT NULL DROP TABLE dbo.RentalContract;", sql);
        // Claves del plan.
        Assert.Contains("CREATE UNIQUE INDEX UX_RentalLine_OpenSerial ON dbo.RentalLine(SerialId) WHERE ReturnedAtUtc IS NULL AND IsActive = 1;", sql);
        Assert.Contains("CONSTRAINT FK_RentalLine_Serial FOREIGN KEY (SerialId, ProductId) REFERENCES dbo.InventorySerial(SerialId, ProductId)", sql);
        Assert.Contains("CONSTRAINT CK_RentalExtension_Dates CHECK (NewPickupDate > PreviousPickupDate)", sql);
        Assert.Contains("CONSTRAINT CK_RentalLineRate_Amount CHECK (RateAmount >= 0)", sql);
        Assert.Contains("CREATE INDEX IX_Rental_StatusPickup ON dbo.Rental(TenantId, StatusCodeId, PickupDate);", sql);
        Assert.Contains("CONSTRAINT UQ_Rental_Number UNIQUE (TenantId, Number)", sql);
        Assert.Contains("CONSTRAINT UQ_RentalReturnLine_Line UNIQUE (RentalLineId)", sql);
        Assert.Contains("DeliveryShipmentId INT NULL,", sql);
        Assert.Contains("RentalLineId INT NOT NULL REFERENCES dbo.RentalLine(RentalLineId),\n        PeriodStart", sql);   // RentalCharge por equipo
        // FKs a Invoice guardadas, en la capa 16C (base existente) y en la 17 (base nueva), y después de crear Invoice.
        Assert.Equal(2, Regex.Matches(sql, @"OBJECT_ID\('dbo\.FK_Rental_Invoice', 'F'\) IS NULL").Count);
        Assert.Equal(2, Regex.Matches(sql, @"OBJECT_ID\('dbo\.FK_RentalCharge_Invoice', 'F'\) IS NULL").Count);
        Assert.True(sql.IndexOf("CREATE TABLE dbo.Invoice (", StringComparison.Ordinal) < sql.LastIndexOf("ADD CONSTRAINT FK_Rental_Invoice", StringComparison.Ordinal));
        // Base existente: el CHECK de contadores se recrea con RENTAL y RENTALRETURN.
        Assert.Contains("definition NOT LIKE '%RENTALRETURN%'", sql);
        // Orden por capas: la renta va después del inventario y de los clientes.
        Assert.True(sql.IndexOf("CREATE TABLE dbo.InventoryTransaction (", StringComparison.Ordinal) < sql.IndexOf("CREATE TABLE dbo.RentalLine (", StringComparison.Ordinal));
    }
}
