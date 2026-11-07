using Teikem.Domain.Common;

namespace Teikem.Domain.Integrations;

/// <summary>Lote 227 (P7 del pedido de vendedores) — códigos del puente entre compañías (módulo INTERCOMPANY, «Compañías enlazadas»).</summary>
public static class TenantLinkCodes
{
    /// <summary>EntityType de la bitácora del enlace (sección «2026-11-07 — Lote 227» de update.sql).</summary>
    public const string EntityType = "TENANT_LINK";
    /// <summary>EntityType de origen de la orden de transporte que el puente crea en la compañía que da el transporte (TransportOrder.SourceEntityType*).</summary>
    public const string OrderEntityType = "TENANT_LINK_ORDER";

    /// <summary>Del lado de quien mira: «soy el cliente» (CLIENT) o «doy el transporte» (PROVIDER).</summary>
    public const string RoleClient = "CLIENT";
    public const string RoleProvider = "PROVIDER";
    public static readonly string[] Roles = { RoleClient, RoleProvider };
}

/// <summary>Estado del enlace: PROPOSED (lo propuso una, falta la otra) → ACTIVE (aceptado por las dos) → ENDED; o PROPOSED → REJECTED. Los dos últimos son finales.</summary>
public static class TenantLinkStatuses
{
    public const string Proposed = "PROPOSED";
    public const string Active = "ACTIVE";
    public const string Rejected = "REJECTED";
    public const string Ended = "ENDED";
    public static readonly string[] All = { Proposed, Active, Rejected, Ended };
    /// <summary>Los que ocupan el lugar: una compañía cliente tiene a lo sumo uno propuesto o activo.</summary>
    public static readonly string[] Open = { Proposed, Active };
}

/// <summary>Estado de la orden creada en la otra compañía: PENDING (por crear o reintentando), CREATED (ya existe allá), FAILED (la otra compañía la rechazó: hay que reintentar a mano).</summary>
public static class TenantLinkOrderStates
{
    public const string Pending = "PENDING";
    public const string Created = "CREATED";
    public const string Failed = "FAILED";
    public static readonly string[] All = { Pending, Created, Failed };
}

/// <summary>
/// Enlace entre dos compañías de la plataforma: «la compañía cliente (ClientTenantId) es cliente de la que da el transporte (ProviderTenantId)», con el Client que la representa
/// del lado del proveedor (ProviderClientId; lo elige el administrador del proveedor). No es de una sola compañía (no lleva TenantId ni filtro global): el servicio lo consulta
/// SIEMPRE por la compañía del principal (cliente o proveedor). Nada cruza sin un enlace ACTIVE aceptado por las dos. Nunca se borra: se termina (ENDED) o se rechaza.
/// </summary>
[AuditEntity(TenantLinkCodes.EntityType)]
public class TenantLink
{
    public int TenantLinkId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    /// <summary>La compañía que despacha por la otra (Solutions).</summary>
    public int ClientTenantId { get; set; }
    /// <summary>La compañía que da el transporte (Logistics).</summary>
    public int ProviderTenantId { get; set; }
    /// <summary>El cliente del proveedor a cuyo nombre nacen las órdenes (NULL mientras el proveedor no acepte).</summary>
    public int? ProviderClientId { get; set; }
    public string Status { get; set; } = TenantLinkStatuses.Proposed;
    public int ProposedByTenantId { get; set; }
    public int? ProposedBy { get; set; }
    public DateTime ProposedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
    public int? RespondedBy { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public string? ResponseNote { get; set; }
    public int? EndedByTenantId { get; set; }
    public int? EndedBy { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string? EndReason { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>
/// La entrega de un despacho de la compañía cliente hecha por la compañía enlazada: el despacho (PickBatch) y la orden de venta de aquí ↔ la orden de transporte creada allá.
/// Es de la compañía cliente (TenantId). Única por despacho (idempotencia: crear la entrega dos veces no crea dos órdenes). RequestJson = lo que se manda (consignatario,
/// paquetes, prioridad…), guardado en la misma transacción del despacho y mandado después (al instante o por el trabajo INTERCOMPANY_SYNC). Lo de «Provider…» es la foto de
/// la orden de allá que se trae de vuelta: número, estatus, entrega.
/// </summary>
public class TenantLinkOrder : ITenantScoped
{
    public int TenantLinkOrderId { get; set; }
    public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int TenantLinkId { get; set; }
    public int SalesOrderId { get; set; }
    public int PickBatchId { get; set; }
    public int ProviderTenantId { get; set; }
    public string RequestJson { get; set; } = "{}";
    public string State { get; set; } = TenantLinkOrderStates.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    /// <summary>Quién la está mandando (token de concurrencia): dos copias del API nunca mandan la misma a la vez.</summary>
    public Guid? ClaimToken { get; set; }
    public DateTime? ClaimedUntilUtc { get; set; }
    public int? ProviderOrderId { get; set; }
    public Guid? ProviderOrderPublicId { get; set; }
    public string? ProviderOrderNumber { get; set; }
    public string? ProviderStatusCode { get; set; }
    public string? ProviderStatusLabelJson { get; set; }
    /// <summary>La orden de allá llegó a un estatus final (entregada, cancelada, devuelta…): ya no se sincroniza.</summary>
    public bool IsFinal { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public string? DeliveredTo { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
}

/// <summary>Lote 227 — reglas puras del puente: largos, mensajes exactos, intentos y qué estatus de allá es final.</summary>
public static class TenantLinkRules
{
    public const int MaxNoteLength = 250;
    public const int MaxErrorLength = 1000;
    public const int MaxDeliveredToLength = 150;
    public const int MaxOrderNotesLength = 500;
    /// <summary>Reintentos automáticos de una orden que no se pudo mandar por una falla pasajera (después queda FAILED).</summary>
    public const int MaxAutoAttempts = 10;
    /// <summary>Lo que dura el reclamo de quien está mandando una orden.</summary>
    public static readonly TimeSpan ClaimFor = TimeSpan.FromMinutes(2);

    public const string NoteTooLong = "La nota no puede pasar de 250 caracteres.";
    public const string RoleInvalid = "Indique el papel de su compañía: CLIENT (somos el cliente de la otra) o PROVIDER (la otra es nuestro cliente).";
    public const string CompanyRequired = "Indique la compañía con la que se quiere enlazar.";
    public const string CompanyNotAvailable = "No se encontró esa compañía, no tiene encendido el módulo de compañías enlazadas o usted no pertenece a ella.";
    public const string SameCompany = "Una compañía no se puede enlazar consigo misma.";
    public const string ProviderNeedsTransport = "La compañía que da el transporte necesita el módulo de Transporte encendido.";
    public const string ClientRequired = "Indique el cliente de su compañía a cuyo nombre nacerán las órdenes de la compañía enlazada.";
    public const string ClientNotUsable = "El cliente indicado no existe, está dado de baja o no está activo.";
    public const string ClientAlreadyLinked = "Ese cliente ya representa a otra compañía enlazada.";
    public const string AlreadyOpen = "Ya hay un enlace propuesto o activo entre estas compañías (o la compañía cliente ya despacha por otra). Termínelo antes de proponer otro.";
    public const string NotProposed = "El enlace ya no está propuesto: no se puede aceptar ni rechazar.";
    public const string CannotRespondOwn = "Su compañía propuso este enlace: lo acepta o lo rechaza la otra.";
    public const string NotActive = "Solo se termina un enlace activo.";
    public const string NotFound = "Enlace";
    public const string ReasonRequired = "Escriba el motivo.";
    public const string ConsigneeRequired = "La entrega por la compañía enlazada necesita el consignatario (de la orden de venta o uno nuevo).";
    public const string DeliveryNotFound = "Entrega por compañía enlazada";
    public const string RetryOnlyFailed = "Solo se reintenta una entrega que no se pudo crear en la compañía enlazada.";
    public const string NotCreatedYet = "La orden todavía no existe en la compañía enlazada.";
    public const string ProofFileNotFound = "Archivo";

    public static string ProviderUnavailable(string company) => $"La compañía enlazada «{company}» no está recibiendo órdenes ahora (está desactivada o apagó el módulo de compañías enlazadas o el de Transporte). Revise el enlace en Integraciones.";
    public static string LinkNotActive(string company) => $"El enlace con «{company}» ya no está activo: la orden no se mandó.";
    public static string LinkInactive(string company) => $"El enlace con «{company}» ya no está activo: no se puede consultar la orden de allá.";
    public static string ProviderClientNotUsable(string company) => $"El cliente que representa a esta compañía en «{company}» ya no puede recibir órdenes (dado de baja o inactivo).";

    public static (string? Value, string? Error) NormalizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return (null, null);
        var t = note.Trim();
        return t.Length > MaxNoteLength ? (null, NoteTooLong) : (t, null);
    }

    public static string? NormalizeRole(string? role)
    {
        var r = role?.Trim().ToUpperInvariant();
        return r is TenantLinkCodes.RoleClient or TenantLinkCodes.RoleProvider ? r : null;
    }

    /// <summary>Un estatus de la orden de allá es final si su etapa es terminal (entregada, cancelada, devuelta, en almacén) o si la orden se dio de baja.</summary>
    public static bool IsFinal(bool stageIsTerminal, bool orderIsActive) => stageIsTerminal || !orderIsActive;

    /// <summary>Espera antes del siguiente intento automático: 1, 2, 4, 8… minutos, hasta 60.</summary>
    public static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Max(0, attempts - 1))));

    public static string? Truncate(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];
}
