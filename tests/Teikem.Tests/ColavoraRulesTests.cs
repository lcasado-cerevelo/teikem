using Teikem.Domain.Constants;
using Teikem.Domain.Migration.Colavora;
using Xunit;

namespace Teikem.Tests;

// Migración de Colavora Courier (2026-10-18): reglas puras de mapeo de estatus, eventos, catálogos, piezas y zonas.
public sealed class ColavoraRulesTests
{
    private static readonly DateTime Migration = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Completed", OrderStatuses.Delivered)]
    [InlineData("Delivered", OrderStatuses.Delivered)]
    [InlineData("Delivered_ReOpen", OrderStatuses.Delivered)]
    [InlineData("Assigned", OrderStatuses.Assigned)]
    [InlineData("Unassign", OrderStatuses.Unassigned)]
    [InlineData("Inbound", OrderStatuses.Unassigned)]
    [InlineData("Canceled", OrderStatuses.Cancelled)]
    [InlineData("Roundtrip", OrderStatuses.Returned)]
    [InlineData("Created", OrderStatuses.Submitted)]
    [InlineData("Our for Delivery", OrderStatuses.InTransit)]
    public void Status_maps_to_teikem(string colavora, string expected)
        => Assert.Equal(expected, ColavoraRules.MapStatus(colavora, Migration.AddDays(-30), Migration));

    [Fact]
    public void Pending_older_than_five_days_closes_as_delivered_and_recent_stays_submitted()
    {
        Assert.Equal(OrderStatuses.Delivered, ColavoraRules.MapStatus("Pending", Migration.AddDays(-6), Migration));
        Assert.Equal(OrderStatuses.Submitted, ColavoraRules.MapStatus("Pending", Migration.AddDays(-2), Migration));
        Assert.Equal(OrderStatuses.Submitted, ColavoraRules.MapStatus("Pending", Migration.AddDays(-5), Migration));
    }

    [Theory]
    [InlineData("AssignDL")]
    [InlineData("PWBackAD")]
    [InlineData("Price - COD")]
    [InlineData("")]
    [InlineData(null)]
    public void Garbage_statuses_from_the_bug_are_detected(string? status)
        => Assert.True(ColavoraRules.IsGarbageStatus(status));

    [Fact]
    public void Local_puerto_rico_time_becomes_utc_and_empty_dates_become_null()
    {
        Assert.Equal(new DateTime(2026, 10, 17, 22, 7, 45, DateTimeKind.Utc), ColavoraRules.ToUtc(new DateTime(2026, 10, 17, 18, 7, 45)));
        Assert.Null(ColavoraRules.ToUtc((DateTime?)new DateTime(1900, 1, 1)));
        Assert.Equal(new DateOnly(2026, 10, 17), ColavoraRules.LocalDate(new DateTime(2026, 10, 18, 3, 59, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Delivered_event_with_312_is_a_delivery_with_who_and_comment()
    {
        var e = ColavoraRules.Classify("Delivered", "312 - DELIVERED by: Carrier Axis (axisadv28@gmail.com) Comment: dejado con vecino");
        Assert.Equal(ColavoraRules.EventKind.Delivered, e.Kind);
        Assert.Equal("312", e.ReasonCode);
        Assert.Equal(OrderStatuses.Delivered, e.TargetStatus);
        var (by, comment) = ColavoraRules.ParseDeliveredText("312 - DELIVERED by: Carrier Axis (axisadv28@gmail.com) Comment: dejado con vecino");
        Assert.Equal("Carrier Axis (axisadv28@gmail.com)", by);
        Assert.Equal("dejado con vecino", comment);
    }

    [Theory]
    [InlineData("Comment", "306 - PATIENT IS NOT IN HOUSE ON DELIVERY SCHEDULED - 1st by: Carrier Advance Comment: nadie", "306")]
    [InlineData("Delivered", "315 - NOT DELIVERED by: Axissm01 Comment: hospitalizada", "315")]
    [InlineData("BOL Comment", "08 - Cliente no contesta", "08")]
    public void Failure_reasons_become_failed_attempts(string type, string text, string reason)
    {
        var e = ColavoraRules.Classify(type, text);
        Assert.Equal(ColavoraRules.EventKind.FailedAttempt, e.Kind);
        Assert.Equal(reason, e.ReasonCode);
        Assert.Equal(OrderStatuses.Failed, e.TargetStatus);
    }

    [Fact]
    public void Completed_by_paperwork_back_is_invoice_processing_not_a_second_delivery()
    {
        Assert.Equal(ColavoraRules.EventKind.PaperworkBack, ColavoraRules.Classify("Completed", "13 - Completada por PWBack").Kind);
        Assert.Equal(ColavoraRules.EventKind.PaperworkBack, ColavoraRules.Classify("Completed", "370 - PWBACK COMPLETED").Kind);
        Assert.Equal(ColavoraRules.EventKind.PaperworkBack, ColavoraRules.Classify("PWBack", "n1989177").Kind);
        Assert.Equal(ColavoraRules.EventKind.Comment, ColavoraRules.Classify("PWBack_del", "n1989177").Kind);
    }

    [Fact]
    public void Driver_assignment_is_parsed_with_code_name_and_reassign_flag()
    {
        var e = ColavoraRules.Classify("AssignDL", "Re-Assign Delivery Driver: 832 - Pedro Hernandez");
        Assert.Equal(ColavoraRules.EventKind.AssignDelivery, e.Kind);
        Assert.Equal("832", e.DriverCode);
        Assert.Equal("Pedro Hernandez", e.DriverName);
        Assert.True(e.Reassign);
        var p = ColavoraRules.Classify("AssignPk", "Assign PickUp Driver: 1003 - Customer Pickup Driver");
        Assert.Equal(ColavoraRules.EventKind.AssignPickup, p.Kind);
        Assert.Equal("1003", p.DriverCode);
        Assert.False(p.Reassign);
    }

    [Fact]
    public void Broken_accents_in_event_names_are_repaired()
    {
        Assert.Equal("Anibal Villafañe", ColavoraRules.CleanName("Anibal VillafaÃ±e"));
        Assert.Equal("Daniel Piñeiro", ColavoraRules.CleanName("Daniel  PiÃ±eiro"));
        Assert.Equal("Joel Flores", ColavoraRules.CleanName("Joel Flores"));
    }

    [Fact]
    public void Price_events_carry_their_amount_and_other_types_are_comments()
    {
        var p = ColavoraRules.Classify("Price - Extra Item", "12.50");
        Assert.Equal(ColavoraRules.EventKind.Price, p.Kind);
        Assert.Equal(12.50m, p.Amount);
        Assert.Equal(ColavoraRules.EventKind.Comment, ColavoraRules.Classify("Llamada", "ESTATUS DE ENTREGA").Kind);
        Assert.Equal(ColavoraRules.EventKind.Comment, ColavoraRules.Classify("Pending", "Shipment marked as Pending automatically").Kind);
        Assert.Equal(ColavoraRules.EventKind.Status, ColavoraRules.Classify("Import", "Colavora API Import").Kind);
        Assert.Equal(OrderStatuses.InTransit, ColavoraRules.Classify("Outbound", "6 - Outbound registered using system").TargetStatus);
    }

    [Fact]
    public void Edit_event_with_status_change_moves_the_order()
    {
        var e = ColavoraRules.Classify("Edit", "Status CHANGED FROM Assigned TO Pending");
        Assert.Equal(ColavoraRules.EventKind.Status, e.Kind);
        Assert.Equal(OrderStatuses.Submitted, e.TargetStatus);
    }

    [Fact]
    public void Manifest_is_driver_code_plus_date()
    {
        Assert.Equal(("837", new DateOnly(2026, 10, 17)), ColavoraRules.ParseManifest("83720261017"));
        Assert.Equal(("801", new DateOnly(2024, 1, 21)), ColavoraRules.ParseManifest("80120240121-Pk"));
        Assert.Equal(("1003", new DateOnly(2026, 10, 17)), ColavoraRules.ParseManifest("100320261017"));
        Assert.Null(ColavoraRules.ParseManifest("abc"));
        Assert.Equal("M-83720261017", ColavoraRules.TripCode("83720261017"));
    }

    [Theory]
    [InlineData("NEXT DAY ISLAND", "NEXT_DAY_ISLAND")]
    [InlineData("5 GAL CONTAINER", "N5_GAL_CONTAINER")]
    [InlineData("PATIENT- DECEASED", "PATIENT_DECEASED")]
    [InlineData("Ida y vuelta ó más", "IDA_Y_VUELTA_O_MAS")]
    public void Catalog_names_become_codes(string name, string code) => Assert.Equal(code, ColavoraRules.ToCode(name));

    [Fact]
    public void Pickup_services_and_invoice_packages_are_marked()
    {
        Assert.True(ColavoraRules.IsPickupServiceType("PV", false));
        Assert.True(ColavoraRules.IsPickupServiceType("XX", true));
        Assert.False(ColavoraRules.IsPickupServiceType("ND", false));
        Assert.True(ColavoraRules.IsInvoicePackageType("IN"));
    }

    [Theory]
    [InlineData("NEXT DAY", "10029", "NEXT_DAY_10029")]
    [InlineData("NEXT DAY", "10030", "NEXT_DAY_10030")]
    [InlineData("Box", "10029", "BOX_10029")]
    [InlineData("5 GAL CONTAINER", "10029", "N5_GAL_CONTAINER_10029")]
    public void Service_and_package_types_belong_to_each_company(string name, string companyId, string code)
        => Assert.Equal(code, ColavoraRules.CompanyCatalogCode(name, companyId));

    [Fact]
    public void Company_catalog_codes_never_exceed_the_column_length()
        => Assert.True(ColavoraRules.CompanyCatalogCode(new string('A', 80), "10029").Length <= 40);

    [Theory]
    [InlineData("None", null, "NONE")]
    [InlineData("None", 25.0, "CASH")]
    [InlineData("Cash or check", 10.0, "CASH_OR_CHECK")]
    [InlineData("Cash Only", 10.0, "CASH")]
    [InlineData("Credit Card", 10.0, "CREDIT_CARD")]
    public void Cod_type_text_maps_to_code(string text, double? amount, string expected)
        => Assert.Equal(expected, ColavoraRules.MapCodType(text, amount is null ? null : (decimal)amount));

    [Fact]
    public void Client_codes_and_test_senders()
    {
        Assert.Equal("10532", ColavoraRules.ClientCode("10532"));
        Assert.Equal("PRECISIONA", ColavoraRules.ClientCode("PRECISION AUTO"));
        Assert.True(ColavoraRules.IsTestSender("HI-TEC BRAKES-DEV", "HI-TEC BRAKES-DEV"));
        Assert.True(ColavoraRules.IsTestSender("911834035", "SAMPLE: Pacific Northwest Supply"));
        Assert.False(ColavoraRules.IsTestSender("MND", "MASCARO - PORTER (NEXT DAY)"));
    }

    [Fact]
    public void Addresses_zips_and_states_are_normalized()
    {
        Assert.Equal("URB PINA E 4", ColavoraRules.FirstNonEmpty(" ", "URB PINA E 4 "));
        Assert.Equal("00949", ColavoraRules.NormalizeZip("949"));
        Assert.Equal("00949", ColavoraRules.NormalizeZip("00949-1234"));
        Assert.Null(ColavoraRules.NormalizeZip(""));
        Assert.Equal("PR", ColavoraRules.NormalizeState("P.R."));
        Assert.Equal("PR", ColavoraRules.NormalizeState("00926"));
        Assert.Equal("FL", ColavoraRules.NormalizeState("fl"));
    }

    [Fact]
    public void Piece_numbers_fit_forty_characters_and_get_an_alternate_when_repeated()
    {
        Assert.Equal("2936248-00", ColavoraRules.PieceNumber("2936248-00 ", "W1", 1));
        Assert.Equal("W1-3", ColavoraRules.PieceNumber("", "W1", 3));
        var alt = ColavoraRules.PieceNumberAlternate("00003030009362480033", 4695307);
        Assert.Equal("00003030009362480033~4695307", alt);
        Assert.True(ColavoraRules.PieceNumberAlternate(new string('X', 40), 123).Length <= 40);
    }

    [Fact]
    public void Users_drivers_codes_and_roles()
    {
        Assert.True(ColavoraRules.IsDriver(4));
        Assert.False(ColavoraRules.IsDriver(1));
        Assert.Equal("2006", ColavoraRules.DriverCode("2006@delapaz"));
        Assert.Equal("812", ColavoraRules.DriverCode("812"));
        Assert.True(ColavoraRules.LooksLikeEmail("tsalazar@advancesupportco.com"));
        Assert.False(ColavoraRules.LooksLikeEmail("2006@delapaz"));
        Assert.Equal("Hector Hiraldo", ColavoraRules.FullName("Hector", "Hiraldo", "x"));
        Assert.Equal("TenantAdmin", ColavoraRules.StaffRole(true));
        Assert.Equal("Dispatcher", ColavoraRules.StaffRole(false));
    }

    [Fact]
    public void Zones_group_zips_by_majority_zone_and_pick_the_most_frequent_driver()
    {
        var consignees = new[]
        {
            new ColavoraConsigneeZone("01", "00725"), new ColavoraConsigneeZone("01", "00725"), new ColavoraConsigneeZone("02", "00725"),
            new ColavoraConsigneeZone("02", "00926"), new ColavoraConsigneeZone("", "00901"), new ColavoraConsigneeZone("02", null),
        };
        var zips = new[]
        {
            new ColavoraZipRow("00725", "METRO", "808", 13985), new ColavoraZipRow("00926", "METRO", "823", 13991),
        };
        var plan = ColavoraZoneRules.Build(consignees, zips);
        var zones = plan.Zones;
        Assert.Equal(2, zones.Count);
        var r01 = zones.Single(z => z.Code == "R01");
        Assert.Equal(new[] { "00725" }, r01.PostalCodes);
        Assert.Equal("808", r01.DriverCode);
        Assert.Equal(13985, r01.DriverUserId);
        Assert.Equal("Ruta 01 (chofer 808)", r01.Name);
        Assert.Equal("METRO", r01.GroupCode);
        var r02 = zones.Single(z => z.Code == "R02");
        Assert.Equal(new[] { "00926" }, r02.PostalCodes);   // 00725 se lo quedó la 01 por mayoría
    }

    [Fact]
    public void Zone_levels_come_from_zip_area_with_their_most_frequent_driver()
    {
        var zips = new[]
        {
            new ColavoraZipRow("00725", "METRO", "808", 13985), new ColavoraZipRow("00926", "METRO", "823", 13991), new ColavoraZipRow("00927", "METRO", "823", 13991),
            new ColavoraZipRow("00601", "ISLA", "704", 13921), new ColavoraZipRow("00602", "ISLA", "721", 13921), new ColavoraZipRow("00603", null, "702", null),
        };
        var consignees = new[] { new ColavoraConsigneeZone("04", "00601"), new ColavoraConsigneeZone("04", "00602"), new ColavoraConsigneeZone("04", "00725") };
        var plan = ColavoraZoneRules.Build(consignees, zips);
        Assert.Equal(new[] { "METRO", "ISLA" }, plan.Groups.Select(g => g.Code));
        var metro = plan.Groups.Single(g => g.Code == "METRO");
        Assert.Equal("Metro", metro.Name);
        Assert.Equal(1, metro.SortOrder);
        Assert.Equal("823", metro.DriverCode);
        Assert.Equal(13991, metro.DriverUserId);
        var isla = plan.Groups.Single(g => g.Code == "ISLA");
        Assert.Equal(13921, isla.DriverUserId);
        // la ruta 04 tiene dos ZIP de ISLA y uno de METRO: su nivel es ISLA
        Assert.Equal("ISLA", plan.Zones.Single(z => z.Code == "R04").GroupCode);
    }

    [Theory]
    [InlineData("003", "3")]
    [InlineData("3", "3")]
    [InlineData(" 0850 ", "850")]
    [InlineData("000", "0")]
    [InlineData("kenneth", "KENNETH")]
    [InlineData(null, "0")]
    public void Driver_codes_are_recognized_with_or_without_leading_zeros(string? code, string expected)
        => Assert.Equal(expected, ColavoraRules.DriverCodeAlias(code));

    [Theory]
    [InlineData("Assigned", 40, "DELIVERED")]
    [InlineData("Assigned", 20, "ASSIGNED")]
    [InlineData("Unassign", 31, "DELIVERED")]
    [InlineData("Out for delivery", 31, "DELIVERED")]
    [InlineData("Pending", 31, "DELIVERED")]
    [InlineData("Canceled", 90, "CANCELLED")]
    public void Open_orders_older_than_the_chosen_days_are_closed(string status, int ageDays, string expected)
        => Assert.Equal(expected.ToUpperInvariant(), ColavoraRules.MapStatus(status, Migration.AddDays(-ageDays), Migration, 5, 30)?.ToUpperInvariant());

    [Fact]
    public void Without_the_option_assigned_orders_stay_open()
        => Assert.Equal(OrderStatuses.Assigned, ColavoraRules.MapStatus("Assigned", Migration.AddDays(-90), Migration));

    [Theory]
    [InlineData("(787) 555 - 1234", "1", "7875551234", null, 0)]
    [InlineData("(787)555-1234", "1", "7875551234", null, 0)]
    [InlineData("787-555-1234 ext 12", "1", "7875551234", "12", 0)]
    [InlineData("787.555.1234 x45", "1", "7875551234", "45", 0)]
    [InlineData("1-787-555-1234", "1", "7875551234", null, 0)]
    [InlineData("+593987654321", "593", "987654321", null, 0)]
    [InlineData("555-1234", "1", "5551234", null, 0)]
    [InlineData("7875551234", "1", "7875551234", null, 0)]
    public void Single_phone_is_split_into_prefix_digits_and_extension(string raw, string cc, string national, string? ext, int warnings)
    {
        var p = ColavoraRules.ParsePhones(raw);
        var n = Assert.Single(p.Numbers);
        Assert.Equal(cc, n.CountryCallingCode);
        Assert.Equal(national, n.National);
        Assert.Equal(ext, p.Extension);
        Assert.Equal(warnings, p.Warnings.Count);
    }

    [Fact]
    public void Two_numbers_in_one_field_are_split_and_the_first_is_the_main_one()
    {
        var p = ColavoraRules.ParsePhones("787-555-1234 / 939-555-9876");
        Assert.Equal(new[] { "7875551234", "9395559876" }, p.Numbers.Select(n => n.National));
    }

    [Theory]
    [InlineData("PR")]
    [InlineData("9.99999E+11")]
    [InlineData("(787")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Junk_phones_are_never_stored_as_numbers(string? raw)
        => Assert.Empty(ColavoraRules.ParsePhones(raw).Numbers);

    [Fact]
    public void A_short_second_segment_warns_but_keeps_the_first_number()
    {
        var p = ColavoraRules.ParsePhones("787-555-1234/5678");
        Assert.Single(p.Numbers);
        Assert.Single(p.Warnings);
    }

    [Theory]
    [InlineData("AXISCARE RECOGIDO TIRILLAS", "AXISCARE", ColavoraRules.SubClientKind.Pickup)]
    [InlineData("AXISCARE DEVOLUCION RX", "AXISCARE", ColavoraRules.SubClientKind.Return)]
    [InlineData("axiscare devolución nutricion", "AXISCARE", ColavoraRules.SubClientKind.Return)]
    [InlineData("BEAUTY PROF RECOGIDO", "BEAUTY PROF", ColavoraRules.SubClientKind.Pickup)]
    public void Pickup_and_return_client_names_are_recognized(string name, string baseName, ColavoraRules.SubClientKind kind)
    {
        var p = ColavoraRules.ParseSubClientName(name);
        Assert.NotNull(p);
        Assert.Equal(baseName, p!.Value.Base);
        Assert.Equal(kind, p.Value.Kind);
    }

    [Theory]
    [InlineData("AXISCARE HEALTH LOGISTIC")]
    [InlineData("AXIS MED VET")]
    [InlineData("MASCARO - PORTER (NEXT DAY)")]
    [InlineData("RECOGIDO")]
    [InlineData(null)]
    public void Normal_client_names_are_not_pickup_or_return(string? name)
        => Assert.Null(ColavoraRules.ParseSubClientName(name));

    [Fact]
    public void Pickup_and_return_clients_join_the_client_with_most_orders_of_the_same_first_name()
    {
        var clients = new (string, string?)[]
        {
            ("10200", "AXISCARE HEALTH LOGISTIC"), ("10532", "AXISCARE HEALTH LOGISTIC NEW"), ("10202", "AXIS MED"),
            ("10280", "AXISCARE RECOGIDO TIRILLAS"), ("10291", "AXISCARE DEVOLUCION RX"), ("10999", "OTRO RECOGIDO X"),
        };
        var counts = new Dictionary<string, int> { ["10200"] = 5, ["10532"] = 410108, ["10280"] = 0, ["10291"] = 1 };
        var links = ColavoraRules.ResolveSubClients(clients, counts);

        Assert.Equal("10532", links["10280"].MainSender);
        Assert.Equal(ColavoraRules.SubClientKind.Pickup, links["10280"].Kind);
        Assert.Equal("10532", links["10291"].MainSender);
        Assert.Equal(ColavoraRules.SubClientKind.Return, links["10291"].Kind);
        Assert.False(links.ContainsKey("10999"));   // no hay cliente «OTRO» al que unirse: queda como cliente
        Assert.False(links.ContainsKey("10532"));
    }
}
