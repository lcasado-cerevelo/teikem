using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>Capa F: campos personalizados por entidad (definiciones + valores por registro). Requiere el módulo CUSTOM_FIELDS.</summary>
[ApiController]
[Route("api/v1/custom-fields")]
[Authorize]
[RequireModule(ModuleKeys.CustomFields)]
public sealed class CustomFieldsController(CustomFieldService fields) : ControllerBase
{
    [HttpGet("definitions")]
    public Task<IReadOnlyList<CustomFieldDefinitionDto>> Definitions([FromQuery] string? entityType, [FromQuery] bool includeInactive, CancellationToken ct) => fields.GetDefinitionsAsync(entityType, includeInactive, ct);

    [HttpGet("definitions/{id:int}")]
    public Task<CustomFieldDefinitionDto> Definition(int id, CancellationToken ct) => fields.GetDefinitionAsync(id, ct);

    [HttpPost("definitions/{entityType}"), RequirePermission(PermissionCatalog.AdminCustomFields)]
    public Task<CustomFieldDefinitionDto> Create(string entityType, [FromBody] CustomFieldDefinitionUpsert req, CancellationToken ct) => fields.CreateDefinitionAsync(entityType, req, ct);

    [HttpPut("definitions/{id:int}"), RequirePermission(PermissionCatalog.AdminCustomFields)]
    public Task<CustomFieldDefinitionDto> Update(int id, [FromBody] CustomFieldDefinitionUpsert req, CancellationToken ct) => fields.UpdateDefinitionAsync(id, req, ct);

    [HttpDelete("definitions/{id:int}"), RequirePermission(PermissionCatalog.AdminCustomFields)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct) { await fields.SetDefinitionActiveAsync(id, false, ct); return NoContent(); }

    [HttpPost("definitions/{id:int}/restore"), RequirePermission(PermissionCatalog.AdminCustomFields)]
    public async Task<IActionResult> Restore(int id, CancellationToken ct) { await fields.SetDefinitionActiveAsync(id, true, ct); return NoContent(); }

    /// <summary>Valores de un registro (todas las definiciones activas de la entidad, con default si no hay valor).</summary>
    [HttpGet("values/{entityType}/{entityId:int}")]
    public Task<IReadOnlyList<CustomFieldValueDto>> Values(string entityType, int entityId, CancellationToken ct) => fields.GetValuesAsync(entityType, entityId, ct);

    /// <summary>Upsert de valores: { "values": { "cost_center": "CC-100", "fragile": true } }.</summary>
    [HttpPut("values/{entityType}/{entityId:int}")]
    public Task<IReadOnlyList<CustomFieldValueDto>> SetValues(string entityType, int entityId, [FromBody] CustomFieldValuesRequest req, CancellationToken ct) => fields.SetValuesAsync(entityType, entityId, req.Values, ct);
}

/// <summary>Módulos G/H/I: fuentes de datos, vistas, indicadores, gráficos y Pulso del día. Requiere el módulo ANALYTICS.</summary>
[ApiController]
[Route("api/v1/analytics")]
[Authorize]
[RequireModule(ModuleKeys.Analytics)]
[RequirePermission(PermissionCatalog.AnalyticsView)]
public sealed class AnalyticsController(AnalyticsService analytics) : ControllerBase
{
    [HttpGet("data-sources")]
    public Task<IReadOnlyList<DataSourceDto>> DataSources(CancellationToken ct) => analytics.GetDataSourcesAsync(ct);

    // ---- G: Vistas ----
    [HttpGet("reports")]
    public Task<IReadOnlyList<ReportDto>> Reports([FromQuery] string? baseEntityType, CancellationToken ct) => analytics.GetReportsAsync(baseEntityType, ct);

    [HttpGet("reports/{id:int}")]
    public Task<ReportDto> Report(int id, CancellationToken ct) => analytics.GetReportAsync(id, ct);

    [HttpPost("reports/{baseEntityType}"), RequirePermission(PermissionCatalog.AnalyticsManage)]
    public Task<ReportDto> CreateReport(string baseEntityType, [FromBody] ReportUpsertRequest req, CancellationToken ct) => analytics.CreateReportAsync(baseEntityType, req, ct);

    [HttpPut("reports/{id:int}"), RequirePermission(PermissionCatalog.AnalyticsManage)]
    public Task<ReportDto> UpdateReport(int id, [FromBody] ReportUpsertRequest req, CancellationToken ct) => analytics.UpdateReportAsync(id, req, ct);

    [HttpDelete("reports/{id:int}"), RequirePermission(PermissionCatalog.AnalyticsManage)]
    public async Task<IActionResult> DeleteReport(int id, CancellationToken ct) { await analytics.DeleteReportAsync(id, ct); return NoContent(); }

    [HttpPost("reports/{id:int}/run")]
    public Task<ReportRunResultDto> RunReport(int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReportRunRequest? run, CancellationToken ct) => analytics.RunReportAsync(id, run ?? new ReportRunRequest(null, null, null, null, null), ct);

    /// <summary>Vista previa del constructor sin guardar.</summary>
    [HttpPost("reports/{baseEntityType}/preview")]
    public Task<ReportRunResultDto> Preview(string baseEntityType, [FromBody] ReportUpsertRequest req, [FromQuery] string? dateRangeMode, [FromQuery] int? take, CancellationToken ct)
        => analytics.PreviewReportAsync(baseEntityType, req, new ReportRunRequest(dateRangeMode, null, null, 0, take), ct);

    // ---- H: Indicadores ----
    [HttpGet("indicators")]
    public Task<IReadOnlyList<AnalyticsDefinitionDto>> Indicators(CancellationToken ct) => analytics.GetIndicatorsAsync(ct);

    [HttpGet("indicators/{id:int}")]
    public Task<AnalyticsDefinitionDto> Indicator(int id, CancellationToken ct) => analytics.GetIndicatorAsync(id, ct);

    [HttpGet("indicators/{id:int}/value")]
    public Task<IndicatorValueDto> IndicatorValue(int id, CancellationToken ct) => analytics.EvaluateIndicatorAsync(id, ct);

    [HttpPost("indicators"), RequirePermission(PermissionCatalog.AnalyticsManage)]
    public Task<AnalyticsDefinitionDto> CreateIndicator([FromBody] IndicatorUpsertRequest req, CancellationToken ct) => analytics.CreateIndicatorAsync(req, ct);

    [HttpPut("indicators/{id:int}"), RequirePermission(PermissionCatalog.AnalyticsManage)]
    public Task<AnalyticsDefinitionDto> UpdateIndicator(int id, [FromBody] IndicatorUpsertRequest req, CancellationToken ct) => analytics.UpdateIndicatorAsync(id, req, ct);

    [HttpDelete("indicators/{id:int}"), RequirePermission(PermissionCatalog.AnalyticsManage)]
    public async Task<IActionResult> DeleteIndicator(int id, CancellationToken ct) { await analytics.DeleteIndicatorAsync(id, ct); return NoContent(); }

    /// <summary>Mi rango de fecha para este indicador (preferencia por usuario, no edita la definición).</summary>
    [HttpPut("indicators/{id:int}/my-date-range")]
    public Task<AnalyticsDefinitionDto> IndicatorMyRange(int id, [FromBody] DateRangeRequest req, CancellationToken ct) => analytics.SetIndicatorMyDateRangeAsync(id, req, ct);

    /// <summary>Rango por defecto de la definición (dueño; ajeno/sistema requiere analytics.dates).</summary>
    [HttpPut("indicators/{id:int}/default-date-range")]
    public Task<AnalyticsDefinitionDto> IndicatorDefaultRange(int id, [FromBody] DateRangeRequest req, CancellationToken ct) => analytics.SetIndicatorDefaultDateRangeAsync(id, req, ct);

    [HttpPut("indicators/{id:int}/my-pulse")]
    public Task<AnalyticsDefinitionDto> IndicatorPulse(int id, [FromBody] PulseRequest req, CancellationToken ct) => analytics.SetIndicatorMyPulseAsync(id, req.ShowInPulse, ct);

    // ---- I: Gráficos ----
    [HttpGet("charts")]
    public Task<IReadOnlyList<AnalyticsDefinitionDto>> Charts(CancellationToken ct) => analytics.GetChartsAsync(ct);

    [HttpGet("charts/{id:int}")]
    public Task<AnalyticsDefinitionDto> Chart(int id, CancellationToken ct) => analytics.GetChartAsync(id, ct);

    [HttpGet("charts/{id:int}/data")]
    public Task<ChartDataDto> ChartData(int id, CancellationToken ct) => analytics.EvaluateChartAsync(id, ct);

    [HttpPost("charts"), RequirePermission(PermissionCatalog.AnalyticsManage)]
    public Task<AnalyticsDefinitionDto> CreateChart([FromBody] ChartUpsertRequest req, CancellationToken ct) => analytics.CreateChartAsync(req, ct);

    [HttpPut("charts/{id:int}"), RequirePermission(PermissionCatalog.AnalyticsManage)]
    public Task<AnalyticsDefinitionDto> UpdateChart(int id, [FromBody] ChartUpsertRequest req, CancellationToken ct) => analytics.UpdateChartAsync(id, req, ct);

    [HttpDelete("charts/{id:int}"), RequirePermission(PermissionCatalog.AnalyticsManage)]
    public async Task<IActionResult> DeleteChart(int id, CancellationToken ct) { await analytics.DeleteChartAsync(id, ct); return NoContent(); }

    [HttpPut("charts/{id:int}/my-date-range")]
    public Task<AnalyticsDefinitionDto> ChartMyRange(int id, [FromBody] DateRangeRequest req, CancellationToken ct) => analytics.SetChartMyDateRangeAsync(id, req, ct);

    [HttpPut("charts/{id:int}/default-date-range")]
    public Task<AnalyticsDefinitionDto> ChartDefaultRange(int id, [FromBody] DateRangeRequest req, CancellationToken ct) => analytics.SetChartDefaultDateRangeAsync(id, req, ct);

    [HttpPut("charts/{id:int}/my-pulse")]
    public Task<AnalyticsDefinitionDto> ChartPulse(int id, [FromBody] PulseRequest req, CancellationToken ct) => analytics.SetChartMyPulseAsync(id, req.ShowInPulse, ct);

    /// <summary>Pulso del día: indicadores y gráficos que este usuario marcó (o vienen por default) y puede ver.</summary>
    [HttpGet("pulse")]
    public Task<PulseDto> Pulse(CancellationToken ct) => analytics.GetPulseAsync(ct);
}
