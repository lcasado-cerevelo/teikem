using Teikem.Domain.Clients;
using Teikem.Infrastructure.Clients;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Xunit;

namespace Teikem.Tests;

public class ContractCodFeeRulesTests
{
    [Fact]
    public void Fixed_negative_is_rejected()
        => Assert.Throws<ValidationException>(() => ContractRules.ValidateCodFee("FIXED", -0.01m));

    [Fact]
    public void Percent_above_100_is_rejected()
        => Assert.Throws<ValidationException>(() => ContractRules.ValidateCodFee("PERCENT", 150m));

    [Fact]
    public void Percent_negative_is_rejected()
        => Assert.Throws<ValidationException>(() => ContractRules.ValidateCodFee("PERCENT", -1m));

    [Fact]
    public void Percent_2_5_is_ok()
        => ContractRules.ValidateCodFee("PERCENT", 2.5m);

    [Fact]
    public void Fixed_2_00_is_ok_and_type_is_case_insensitive()
    {
        ContractRules.ValidateCodFee("FIXED", 2.00m);
        ContractRules.ValidateCodFee("fixed", 0m);
    }

    [Fact]
    public void Unknown_type_is_rejected()
    {
        Assert.Throws<ValidationException>(() => ContractRules.ValidateCodFee("PER_KG", 1m));
        Assert.Throws<ValidationException>(() => ContractRules.ValidateCodFee("", 1m));
        Assert.Throws<ValidationException>(() => ContractRules.ValidateCodFee(null, 1m));
    }

    [Fact]
    public void Missing_value_is_rejected()
        => Assert.Throws<ValidationException>(() => ContractRules.ValidateCodFee("FIXED", null));
}

public class ContractServiceLevelRulesTests
{
    [Fact]
    public void Repeated_service_type_is_rejected_case_insensitive()
    {
        var items = new[] { new ServiceLevelUpsert("STANDARD", 48), new ServiceLevelUpsert("standard", 24) };
        var ex = Assert.Throws<ValidationException>(() => ContractRules.ValidateServiceLevels(items));
        Assert.Contains("repetido", ex.Message);
    }

    [Fact]
    public void Max_transit_hours_must_be_positive()
    {
        Assert.Throws<ValidationException>(() => ContractRules.ValidateServiceLevels(new[] { new ServiceLevelUpsert("STANDARD", 0) }));
        Assert.Throws<ValidationException>(() => ContractRules.ValidateServiceLevels(new[] { new ServiceLevelUpsert("STANDARD", -5) }));
    }

    [Fact]
    public void On_time_target_pct_must_be_between_0_and_100()
    {
        Assert.Throws<ValidationException>(() => ContractRules.ValidateServiceLevels(new[] { new ServiceLevelUpsert("STANDARD", 48, OnTimeTargetPct: 101) }));
        Assert.Throws<ValidationException>(() => ContractRules.ValidateServiceLevels(new[] { new ServiceLevelUpsert("STANDARD", 48, OnTimeTargetPct: -1) }));
        ContractRules.ValidateServiceLevels(new[] { new ServiceLevelUpsert("STANDARD", 48, OnTimeTargetPct: 100), new ServiceLevelUpsert("EXPRESS", 8, OnTimeTargetPct: 0) });
    }

    [Fact]
    public void Empty_service_type_is_rejected_and_null_list_is_ok()
    {
        Assert.Throws<ValidationException>(() => ContractRules.ValidateServiceLevels(new[] { new ServiceLevelUpsert("  ") }));
        ContractRules.ValidateServiceLevels(null);
        ContractRules.ValidateServiceLevels(Array.Empty<ServiceLevelUpsert>());
    }

    [Fact]
    public void Negative_pickup_window_or_penalty_is_rejected()
    {
        Assert.Throws<ValidationException>(() => ContractRules.ValidateServiceLevels(new[] { new ServiceLevelUpsert("STANDARD", PickupWindowMin: -1) }));
        Assert.Throws<ValidationException>(() => ContractRules.ValidateServiceLevels(new[] { new ServiceLevelUpsert("STANDARD", PenaltyAmount: -1) }));
    }
}

public class ContractNumberRulesTests
{
    [Fact]
    public void Next_after_c1_is_c2()
        => Assert.Equal("ACME-C2", ContractRules.NextContractNumber("ACME", new[] { "ACME-C1" }));

    [Fact]
    public void First_contract_is_c1()
    {
        Assert.Equal("ACME-C1", ContractRules.NextContractNumber("ACME", Array.Empty<string>()));
        Assert.Equal("ACME-C1", ContractRules.NextContractNumber("ACME", null));
    }

    [Fact]
    public void Ignores_numbers_with_other_formats_and_uses_the_max()
    {
        var existing = new[] { "ACME-C1", "ACME-2026-01", "ACME-C7", "ACME-CX", "OTRO-C9", "acme-c3" };
        Assert.Equal("ACME-C8", ContractRules.NextContractNumber("ACME", existing));
    }

    [Fact]
    public void Empty_client_code_is_rejected()
        => Assert.Throws<ValidationException>(() => ContractRules.NextContractNumber("  ", new[] { "X-C1" }));
}

public class ContractActivationRulesTests
{
    [Fact]
    public void Suspended_client_cannot_activate()
        => Assert.Throws<StatusRuleException>(() => ContractRules.EnsureCanActivate("SUSPENDED", false));

    [Fact]
    public void Client_with_another_active_contract_cannot_activate()
        => Assert.Throws<StatusRuleException>(() => ContractRules.EnsureCanActivate("ACTIVE", true));

    [Fact]
    public void Active_client_without_other_active_contract_can_activate_even_if_end_date_is_past()
    {
        // La fecha fin no interviene en la regla: no hay vencimiento automático (decisión de Luis).
        ContractRules.EnsureCanActivate("ACTIVE", false);
        ContractRules.EnsureCanActivate("PROSPECT", false);
    }

    [Fact]
    public void Suspended_check_is_case_insensitive()
        => Assert.Throws<StatusRuleException>(() => ContractRules.EnsureCanActivate("suspended", false));

    [Fact]
    public void Null_client_status_only_fails_when_another_contract_is_active()
    {
        ContractRules.EnsureCanActivate(null, false);
        Assert.Throws<StatusRuleException>(() => ContractRules.EnsureCanActivate(null, true));
    }
}

public class ContractDateRulesTests
{
    [Fact]
    public void End_before_start_is_rejected_on_endDate_field()
    {
        var ex = Assert.Throws<ValidationException>(() => ContractRules.ValidateDates(new DateOnly(2026, 1, 1), new DateOnly(2025, 12, 31)));
        Assert.True(ex.Errors!.ContainsKey("endDate"));
        Assert.Contains("La fecha fin no puede ser anterior a la fecha de inicio.", ex.Message);
    }

    [Fact]
    public void End_equal_to_start_or_null_is_ok()
    {
        ContractRules.ValidateDates(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));
        ContractRules.ValidateDates(new DateOnly(2026, 1, 1), null);
    }

    [Fact]
    public void Custom_field_name_is_propagated()
    {
        var ex = Assert.Throws<ValidationException>(() => ContractRules.ValidateDates(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 31), "contract.endDate"));
        Assert.True(ex.Errors!.ContainsKey("contract.endDate"));
    }

    [Fact]
    public void Service_level_field_prefix_is_propagated_for_the_composite_client_create()
    {
        var items = new[] { new ServiceLevelUpsert("STANDARD", 48), new ServiceLevelUpsert("standard", 24) };
        var ex = Assert.Throws<ValidationException>(() => ContractRules.ValidateServiceLevels(items, "contract.serviceLevels"));
        Assert.True(ex.Errors!.ContainsKey("contract.serviceLevels[1].serviceType"));
    }
}

/// <summary>Regla pura del contrato vigente (CurrentContractRule): la misma que ClientQueries.CurrentContractAsync aplica en SQL.</summary>
public class CurrentContractRuleTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 25);

    private static Teikem.Domain.Clients.Contract C(int id, string status, DateOnly start, DateOnly? end = null, bool isActive = true)
        => new() { ContractId = id, StartDate = start, EndDate = end, IsActive = isActive, Title = status };

    private static string? StatusOf(Teikem.Domain.Clients.Contract c) => c.Title; // el "estatus" viaja en Title solo para la prueba

    [Fact]
    public void Active_with_start_on_or_before_asOf_wins_even_with_past_end_date()
    {
        var expired = C(1, "ACTIVE", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)); // fecha fin pasada: sigue vigente
        var draft = C(2, "DRAFT", new DateOnly(2026, 9, 1));
        Assert.Same(expired, CurrentContractRule.Pick(new[] { draft, expired }, StatusOf, AsOf));
    }

    [Fact]
    public void Active_with_future_start_is_ignored_and_draft_is_used()
    {
        var future = C(1, "ACTIVE", AsOf.AddDays(1));
        var draft = C(2, "DRAFT", new DateOnly(2026, 1, 1));
        Assert.Same(draft, CurrentContractRule.Pick(new[] { future, draft }, StatusOf, AsOf));
    }

    [Fact]
    public void Most_recent_draft_by_start_date_wins_and_ties_break_by_id()
    {
        var older = C(1, "DRAFT", new DateOnly(2026, 1, 1));
        var newer = C(2, "DRAFT", new DateOnly(2026, 3, 1));
        var sameDay = C(3, "DRAFT", new DateOnly(2026, 3, 1));
        Assert.Same(sameDay, CurrentContractRule.Pick(new[] { older, newer, sameDay }, StatusOf, AsOf));
        Assert.Same(newer, CurrentContractRule.Pick(new[] { older, newer }, StatusOf, AsOf));
    }

    [Fact]
    public void Expired_cancelled_or_inactive_contracts_are_never_current()
    {
        var rows = new[]
        {
            C(1, "EXPIRED", new DateOnly(2026, 1, 1)),
            C(2, "CANCELLED", new DateOnly(2026, 2, 1)),
            C(3, "ACTIVE", new DateOnly(2026, 1, 1), isActive: false),
            C(4, "DRAFT", new DateOnly(2026, 1, 1), isActive: false),
        };
        Assert.Null(CurrentContractRule.Pick(rows, StatusOf, AsOf));
        Assert.Null(CurrentContractRule.Pick(Array.Empty<Teikem.Domain.Clients.Contract>(), StatusOf, AsOf));
    }

    [Fact]
    public void Status_comparison_is_case_insensitive_and_null_safe()
    {
        var active = C(1, "active", new DateOnly(2026, 1, 1));
        var unknown = C(2, "", new DateOnly(2026, 1, 1));
        Assert.Same(active, CurrentContractRule.Pick(new[] { unknown, active }, c => c.ContractId == 2 ? null : c.Title, AsOf));
    }
}
