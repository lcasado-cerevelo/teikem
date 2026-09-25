using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 2 (P4): cotización por contrato (la reutilizará Facturación). Resuelve el contrato vigente del cliente en asOf,
/// aplica tarifa base por servicio+paquete, pieza extra marginal, cargo por despacho y cargo por COD (R30–R35),
/// con las tarifas genéricas del tenant como respaldo. Requiere el módulo CATALOG.
/// </summary>
[ApiController]
[Route("api/v1/billing")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class BillingQuoteController(RateService rates) : ControllerBase
{
    /// <summary>Cotiza una orden (varias líneas: una por tipo de paquete) contra el contrato vigente del cliente en asOf (vacío = hoy).</summary>
    [HttpPost("contract-rate-quote"), RequirePermission(PermissionCatalog.ContractsRead)]
    public Task<RateQuoteDto> Quote([FromBody] RateQuoteRequest req, CancellationToken ct) => rates.QuoteAsync(req, ct);
}
